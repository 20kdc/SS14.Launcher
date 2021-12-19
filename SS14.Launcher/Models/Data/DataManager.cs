using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using DbUp.Engine;
using DbUp.SQLite.Helpers;
using DynamicData;
using JetBrains.Annotations;
using Microsoft.Data.Sqlite;
using Microsoft.Toolkit.Mvvm.Messaging;
using Newtonsoft.Json;
using ReactiveUI;
using Serilog;

namespace SS14.Launcher.Models.Data;

/// <summary>
/// A CVar entry in the <see cref="DataManager"/>. This is a separate object to allow data binding easily.
/// </summary>
/// <typeparam name="T">The type of value stored by the CVar.</typeparam>
public interface ICVarEntry<T> : INotifyPropertyChanged
{
    public T Value { get; set; }
}

/// <summary>
///     Handles storage of all permanent data,
///     like username, current build, favorite servers...
/// </summary>
/// <remarks>
/// All data is stored in an SQLite DB. Simple config variables are stored K/V in a single table.
/// More complex things like logins is stored in individual tables.
/// </remarks>
public sealed class DataManager : ReactiveObject
{
    private delegate void DbCommand(SqliteConnection connection);

    private readonly SourceCache<FavoriteServer, string> _favoriteServers = new(f => f.Address);
    private readonly SourceCache<InstalledServerContent, string> _serverContent = new(i => i.ForkId);

    private readonly SourceCache<LoginInfo, Guid> _logins = new(l => l.UserId);

    // When using dynamic engine management, this is used to keep track of installed engine versions.
    private readonly SourceCache<InstalledEngineVersion, string> _engineInstallations = new(v => v.Version);

    private readonly Dictionary<string, CVarEntry> _configEntries = new();

    private readonly List<DbCommand> _dbCommandQueue = new();
    private readonly SemaphoreSlim _dbWritingSemaphore = new(1);

    static DataManager()
    {
        SqlMapper.AddTypeHandler(new GuidTypeHandler());
        SqlMapper.AddTypeHandler(new DateTimeOffsetTypeHandler());
    }

    public DataManager()
    {
        // Set up subscriptions to listen for when the list-data (e.g. logins) changes in any way.
        // All these operations match directly SQL UPDATE/INSERT/DELETE.

        // Favorites
        _favoriteServers.Connect()
            .WhenAnyPropertyChanged()
            .Subscribe(c => ChangeFavoriteServer(ChangeReason.Update, c!));

        _favoriteServers.Connect()
            .ForEachChange(c => ChangeFavoriteServer(c.Reason, c.Current))
            .Subscribe(_ => WeakReferenceMessenger.Default.Send(new FavoritesChanged()));

        // Server content
        _serverContent.Connect()
            .WhenAnyPropertyChanged()
            .Subscribe(c => ChangeServerContent(ChangeReason.Update, c!));

        _serverContent.Connect()
            .ForEachChange(c => ChangeServerContent(c.Reason, c.Current))
            .Subscribe();

        // Logins
        _logins.Connect()
            .ForEachChange(c => ChangeLogin(c.Reason, c.Current))
            .Subscribe();

        _logins.Connect()
            .WhenAnyPropertyChanged()
            .Subscribe(c => ChangeLogin(ChangeReason.Update, c!));

        // Engine installations. Doesn't need UPDATE because immutable.
        _engineInstallations.Connect()
            .ForEachChange(c => ChangeEngineInstallation(c.Reason, c.Current))
            .Subscribe();
    }

    public Guid Fingerprint => Guid.Parse(GetCVar(CVars.Fingerprint));

    public Guid? SelectedLoginId
    {
        get
        {
            var value = GetCVar(CVars.SelectedLogin);
            if (value == "")
                return null;

            return Guid.Parse(value);
        }
        set
        {
            if (value != null && !_logins.Lookup(value.Value).HasValue)
            {
                throw new ArgumentException("We are not logged in for that user ID.");
            }

            SetCVar(CVars.SelectedLogin, value.ToString()!);
            CommitConfig();
        }
    }

    public IObservableCache<FavoriteServer, string> FavoriteServers => _favoriteServers;
    public IObservableCache<InstalledServerContent, string> ServerContent => _serverContent;
    public IObservableCache<LoginInfo, Guid> Logins => _logins;
    public IObservableCache<InstalledEngineVersion, string> EngineInstallations => _engineInstallations;

    public bool ActuallyMultiAccounts =>
#if DEBUG
        true;
#else
            GetCVar(CVars.MultiAccounts);
#endif

    public void AddFavoriteServer(FavoriteServer server)
    {
        if (_favoriteServers.Lookup(server.Address).HasValue)
        {
            throw new ArgumentException("A server with that address is already a favorite.");
        }

        _favoriteServers.AddOrUpdate(server);
    }

    public void RemoveFavoriteServer(FavoriteServer server)
    {
        _favoriteServers.Remove(server);
    }

    public void AddInstallation(InstalledServerContent installedServerContent)
    {
        if (_favoriteServers.Lookup(installedServerContent.ForkId).HasValue)
        {
            throw new ArgumentException("An installation with that fork ID already exists.");
        }

        _serverContent.AddOrUpdate(installedServerContent); // Will do a save.
    }

    public void RemoveInstallation(InstalledServerContent installedServerContent)
    {
        _serverContent.Remove(installedServerContent);
    }

    public void AddEngineInstallation(InstalledEngineVersion version)
    {
        _engineInstallations.AddOrUpdate(version);
    }

    public void RemoveEngineInstallation(InstalledEngineVersion version)
    {
        _engineInstallations.Remove(version);
    }

    public void AddLogin(LoginInfo login)
    {
        if (_logins.Lookup(login.UserId).HasValue)
        {
            throw new ArgumentException("A login with that UID already exists.");
        }

        _logins.AddOrUpdate(login);
    }

    public void RemoveLogin(LoginInfo loginInfo)
    {
        _logins.Remove(loginInfo);

        if (loginInfo.UserId == SelectedLoginId)
        {
            SelectedLoginId = null;
        }
    }

    public int GetNewInstallationId()
    {
        var id = GetCVar(CVars.NextInstallationId);
        id += 1;
        SetCVar(CVars.NextInstallationId, id);
        return id;
    }

    /// <summary>
    ///     Loads config file from disk, or resets the loaded config to default if the config doesn't exist on disk.
    /// </summary>
    public void Load()
    {
        InitializeCVars();

        using var connection = new SqliteConnection(GetCfgDbConnectionString());
        connection.Open();

        var sw = Stopwatch.StartNew();
        var result = DbUp.DeployChanges.To
            .SQLiteDatabase(new SharedConnection(connection))
            .WithScripts(LoadMigrationScriptsList())
            .LogToAutodetectedLog()
            .WithTransactionPerScript()
            .Build()
            .PerformUpgrade();

        if (result.Error is { } error)
            throw error;

        Log.Debug("Did migrations in {MigrationTime}", sw.Elapsed);

        if (connection.ExecuteScalar<bool>("SELECT COUNT(*) > 0 FROM Config"))
        {
            // Load from SQLite DB.
            LoadSqliteConfig(connection);
        }
        else
        {
            // SQLite DB empty, load from old JSON config file.
            LoadJsonConfig();

            // Loading of JSON config will have created a bunch of DB commands.
            // These will be committed at the end of the function.

            // Add an unused config key so the above count check is always correct.
            AddDbCommand(con => con.Execute("INSERT INTO Config VALUES ('Populated', TRUE)"));
        }

        if (GetCVar(CVars.Fingerprint) == "")
        {
            // If we don't have a fingerprint yet this is either a fresh config or an older config.
            // Generate a fingerprint and immediately save it to disk.
            SetCVar(CVars.Fingerprint, Guid.NewGuid().ToString());
        }

        CommitConfig();
    }

    private static IEnumerable<SqlScript> LoadMigrationScriptsList()
    {
        var assembly = typeof(DataManager).Assembly;
        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.EndsWith(".sql"))
                continue;

            var index = resourceName.LastIndexOf('.', resourceName.Length - 5, resourceName.Length - 4);
            index += 1;

            var name = resourceName[index..^4];
            yield return new LazySqlScript(name, () =>
            {
                using var reader = new StreamReader(assembly.GetManifestResourceStream(resourceName)!);

                return reader.ReadToEnd();
            });
        }
    }

    private void LoadSqliteConfig(SqliteConnection sqliteConnection)
    {
        // Load logins.
        _logins.AddOrUpdate(
            sqliteConnection.Query<(Guid id, string name, string token, DateTimeOffset expires)>(
                    "SELECT UserId, UserName, Token, Expires FROM Login")
                .Select(l => new LoginInfo
                {
                    UserId = l.id,
                    Username = l.name,
                    Token = new LoginToken(l.token, l.expires)
                }));

        // Favorites
        _favoriteServers.AddOrUpdate(
            sqliteConnection.Query<(string addr, string name)>(
                    "SELECT Address,Name FROM FavoriteServer")
                .Select(l => new FavoriteServer(l.name, l.addr)));

        // Favorites
        _engineInstallations.AddOrUpdate(
            sqliteConnection.Query<InstalledEngineVersion>("SELECT Version,Signature FROM EngineInstallation"));

        // Favorites
        _serverContent.AddOrUpdate(
            sqliteConnection.Query<InstalledServerContent>(
                "SELECT CurrentVersion,CurrentHash,ForkId,DiskId,CurrentEngineVersion FROM ServerContent"));

        // Load CVars.
        var configRows = sqliteConnection.Query<(string, object)>("SELECT Key, Value FROM Config");
        foreach (var (k, v) in configRows)
        {
            if (!_configEntries.TryGetValue(k, out var entry))
                continue;

            if (entry.Type == typeof(string))
                Set((string) v);
            else if (entry.Type == typeof(bool))
                Set((long) v != 0);
            else if (entry.Type == typeof(int))
                Set((int)(long) v);

            void Set<T>(T value) => ((CVarEntry<T>)entry).ValueInternal = value;
        }

        // Avoid DB commands from config load.
        _dbCommandQueue.Clear();
    }

    private void InitializeCVars()
    {
        Debug.Assert(_configEntries.Count == 0);

        var baseMethod = typeof(DataManager)
            .GetMethod(nameof(CreateEntry), BindingFlags.NonPublic | BindingFlags.Instance)!;

        foreach (var field in typeof(CVars).GetFields(BindingFlags.Static | BindingFlags.Public))
        {
            if (!field.FieldType.IsAssignableTo(typeof(CVarDef)))
                continue;

            var def = (CVarDef)field.GetValue(null)!;
            var method = baseMethod.MakeGenericMethod(def.ValueType);
            _configEntries.Add(def.Name, (CVarEntry)method.Invoke(this, new object?[] { def })!);
        }
    }

    private CVarEntry<T> CreateEntry<T>(CVarDef<T> def)
    {
        return new CVarEntry<T>(this, def);
    }

    private void LoadJsonConfig()
    {
        var path = GetCfgJsonPath();

        using var changeSuppress = SuppressChangeNotifications();
        var text = File.ReadAllText(path);
        var data = JsonConvert.DeserializeObject<JsonData>(text)!;

        _favoriteServers.Edit(a =>
        {
            a.Clear();
            if (data.Favorites != null)
                a.AddOrUpdate(data.Favorites);
        });

        _logins.Edit(p =>
        {
            p.Clear();
            if (data.Logins != null)
            {
                p.AddOrUpdate(data.Logins);
            }
        });

        _engineInstallations.Edit(p =>
        {
            p.Clear();

            if (data.Engines != null)
            {
                p.AddOrUpdate(data.Engines);
            }
        });

        if (data.ServerContent != null)
        {
            _serverContent.Edit(a =>
            {
                a.Clear();
                a.AddOrUpdate(data.ServerContent);
            });
        }

        SetCVar(CVars.CompatMode, data.ForceGLES2 ?? CVars.CompatMode.DefaultValue);
        SetCVar(CVars.Fingerprint, data.Fingerprint.ToString());
        SetCVar(CVars.DynamicPgo, data.DynamicPGO ?? CVars.DynamicPgo.DefaultValue);
        SetCVar(CVars.DisableSigning, data.DisableSigning);
        SetCVar(CVars.LogClient, data.LogClient);
        SetCVar(CVars.LogLauncher, data.LogLauncher);
        SetCVar(CVars.MultiAccounts, data.MultiAccounts);
        SetCVar(CVars.HasDismissedEarlyAccessWarning, data.DismissedEarlyAccessWarning ?? false);
        SetCVar(CVars.NextInstallationId, data.NextInstallationId);
    }

    [SuppressMessage("ReSharper", "UseAwaitUsing")]
    public async void CommitConfig()
    {
        if (_dbCommandQueue.Count == 0)
            return;

        var commands = _dbCommandQueue.ToArray();
        _dbCommandQueue.Clear();
        Log.Debug("Committing config to disk, running {DbCommandCount} commands", commands.Length);

        await Task.Run(async () =>
        {
            // SQLite is thread safe and won't have any problems with having multiple writers
            // (but they'll be synchronous).
            // That said, we need something to wait on when we shut down to make sure everything is written, so.
            await _dbWritingSemaphore.WaitAsync();
            try
            {
                using var connection = new SqliteConnection(GetCfgDbConnectionString());
                connection.Open();
                using var transaction = connection.BeginTransaction();

                foreach (var cmd in commands)
                {
                    cmd(connection);
                }

                var sw = Stopwatch.StartNew();
                transaction.Commit();
                Log.Debug("Commit took: {CommitElapsed}", sw.Elapsed);
            }
            finally
            {
                _dbWritingSemaphore.Release();
            }
        });
    }

    public void Close()
    {
        CommitConfig();
        // Wait for any DB writes to finish to make sure we commit everything.
        _dbWritingSemaphore.Wait();
    }

    private static string GetCfgJsonPath()
    {
        return Path.Combine(LauncherPaths.DirUserData, "launcher_config.json");
    }

    private static string GetCfgDbConnectionString()
    {
        var path = Path.Combine(LauncherPaths.DirUserData, "settings.db");
        return $"Data Source={path};Mode=ReadWriteCreate";
    }

    private void AddDbCommand(DbCommand cmd)
    {
        _dbCommandQueue.Add(cmd);
    }

    private void ChangeFavoriteServer(ChangeReason reason, FavoriteServer server)
    {
        // Make immutable copy to avoid race condition bugs.
        var data = new
        {
            server.Address,
            server.Name
        };
        AddDbCommand(con =>
        {
            con.Execute(reason switch
                {
                    ChangeReason.Add => "INSERT INTO FavoriteServer VALUES (@Address, @Name)",
                    ChangeReason.Update => "UPDATE FavoriteServer SET Name = @Name WHERE Address = @Address",
                    ChangeReason.Remove => "DELETE FROM FavoriteServer WHERE Address = @Address",
                    _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
                },
                data
            );
        });
    }

    private void ChangeServerContent(ChangeReason reason, InstalledServerContent serverContent)
    {
        // Make immutable copy to avoid race condition bugs.
        var data = new
        {
            serverContent.ForkId,
            serverContent.CurrentVersion,
            serverContent.CurrentHash,
            serverContent.CurrentEngineVersion,
            serverContent.DiskId
        };
        AddDbCommand(con =>
        {
            con.Execute(reason switch
                {
                    ChangeReason.Add =>
                        "INSERT INTO ServerContent VALUES (@ForkId, @CurrentVersion, @CurrentHash, @CurrentEngineVersion, @DiskId)",
                    ChangeReason.Update =>
                        "UPDATE ServerContent SET CurrentVersion = @CurrentVersion, CurrentHash = @CurrentHash, CurrentEngineVersion = @CurrentEngineVersion WHERE ForkId = @ForkId",
                    ChangeReason.Remove => "DELETE FROM ServerContent WHERE ForkId = @ForkId",
                    _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
                },
                data
            );
        });
    }

    private void ChangeLogin(ChangeReason reason, LoginInfo login)
    {
        // Make immutable copy to avoid race condition bugs.
        var data = new
        {
            login.UserId,
            UserName = login.Username,
            login.Token.Token,
            Expires = login.Token.ExpireTime
        };
        AddDbCommand(con =>
        {
            con.Execute(reason switch
                {
                    ChangeReason.Add => "INSERT INTO Login VALUES (@UserId, @UserName, @Token, @Expires)",
                    ChangeReason.Update =>
                        "UPDATE Login SET UserName = @UserName, Token = @Token, Expires = @Expires WHERE UserId = @UserId",
                    ChangeReason.Remove => "DELETE FROM Login WHERE UserId = @UserId",
                    _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
                },
                data
            );
        });
    }

    private void ChangeEngineInstallation(ChangeReason reason, InstalledEngineVersion engine)
    {
        AddDbCommand(con => con.Execute(reason switch
            {
                ChangeReason.Add => "INSERT INTO EngineInstallation VALUES (@Version, @Signature)",
                ChangeReason.Update =>
                    "UPDATE EngineInstallation SET Signature = @Signature WHERE Version = @Version",
                ChangeReason.Remove => "DELETE FROM EngineInstallation WHERE Version = @Version",
                _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
            },
            // Already immutable.
            engine
        ));
    }

    public T GetCVar<T>([ValueProvider("SS14.Launcher.Models.Data.CVars")] CVarDef<T> cVar)
    {
        var entry = (CVarEntry<T>)_configEntries[cVar.Name];
        return entry.Value;
    }

    public ICVarEntry<T> GetCVarEntry<T>([ValueProvider("SS14.Launcher.Models.Data.CVars")] CVarDef<T> cVar)
    {
        return (CVarEntry<T>)_configEntries[cVar.Name];
    }

    public void SetCVar<T>([ValueProvider("SS14.Launcher.Models.Data.CVars")] CVarDef<T> cVar, T value)
    {
        var name = cVar.Name;
        var entry = (CVarEntry<T>)_configEntries[cVar.Name];
        if (EqualityComparer<T>.Default.Equals(entry.ValueInternal, value))
            return;

        entry.ValueInternal = value;
        entry.FireValueChanged();

        AddDbCommand(con => con.Execute(
            "INSERT OR REPLACE INTO Config VALUES (@Key, @Value)",
            new
            {
                Key = name,
                Value = value
            }));
    }

    private abstract class CVarEntry
    {
        public abstract Type Type { get; }
    }

    private sealed class CVarEntry<T> : CVarEntry, ICVarEntry<T>
    {
        private readonly DataManager _mgr;
        private readonly CVarDef<T> _cVar;

        public CVarEntry(DataManager mgr, CVarDef<T> cVar)
        {
            _mgr = mgr;
            _cVar = cVar;
            ValueInternal = cVar.DefaultValue;
        }

        public override Type Type => typeof(T);

        public event PropertyChangedEventHandler? PropertyChanged;

        public T Value
        {
            get => ValueInternal;
            set => _mgr.SetCVar(_cVar, value);
        }

        public T ValueInternal;

        public void FireValueChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
    }

    [Serializable]
    private sealed class JsonData
    {
        [JsonProperty(PropertyName = "selected_login")]
        public Guid? SelectedLogin { get; set; }

        [JsonProperty(PropertyName = "favorites")]
        public List<FavoriteServer>? Favorites { get; set; }

        [JsonProperty(PropertyName = "server_content")]
        public List<InstalledServerContent>? ServerContent { get; set; }

        [JsonProperty(PropertyName = "engines")]
        public List<InstalledEngineVersion>? Engines { get; set; }

        [JsonProperty(PropertyName = "logins")]
        public List<LoginInfo>? Logins { get; set; }

        [JsonProperty(PropertyName = "next_installation_id")]
        public int NextInstallationId { get; set; } = 1;

        [JsonProperty(PropertyName = "fingerprint")]
        public Guid Fingerprint { get; set; }

        [JsonProperty(PropertyName = "force_gles2")]
        public bool? ForceGLES2 { get; set; }

        [JsonProperty(PropertyName = "dynamic_pgo")]
        public bool? DynamicPGO { get; set; }

        [JsonProperty(PropertyName = "dismissed_early_access_warning")]
        public bool? DismissedEarlyAccessWarning { get; set; }

        [JsonProperty(PropertyName = "disable_signing")]
        public bool DisableSigning { get; set; }

        [JsonProperty(PropertyName = "log_client")]
        public bool LogClient { get; set; }

        [JsonProperty(PropertyName = "log_launcher")]
        public bool LogLauncher { get; set; }

        [JsonProperty(PropertyName = "multi_accounts")]
        public bool MultiAccounts { get; set; }
    }
}

public record FavoritesChanged;
