﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reactive.Linq;
using System.Threading.Tasks;
using DynamicData;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using Splat;
using SS14.Launcher.Api;
using SS14.Launcher.Localization;
using SS14.Launcher.Models;
using SS14.Launcher.Models.Data;
using SS14.Launcher.Models.Logins;
using SS14.Launcher.Utility;
using SS14.Launcher.ViewModels.IdentityTabs;
using SS14.Launcher.ViewModels.Login;
using SS14.Launcher.ViewModels.MainWindowTabs;
using SS14.Launcher.Views;
using Avalonia.Controls;


namespace SS14.Launcher.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, IErrorOverlayOwner
{
    private readonly DataManager _cfg;
    private readonly LoginManager _loginMgr;
    private readonly HttpClient _http;
    private readonly LocalizationManager localizationManager;
    private readonly LauncherInfoManager _infoManager;
    private readonly AgeManager ageManager;

    private int _selectedIndex;

    public DataManager Cfg => _cfg;
    [Reactive] public bool OutOfDate { get; private set; }

    public string Title => ConfigConstants.LauncherTitle;

    // Main Tabs

    public HomePageViewModel HomeTab { get; }
    public ServerListTabViewModel ServersTab { get; }
    public NewsTabViewModel NewsTab { get; }
    public OptionsTabViewModel OptionsTab { get; }


    public MainWindowViewModel()
    {
        _cfg = Locator.Current.GetRequiredService<DataManager>();
        _loginMgr = Locator.Current.GetRequiredService<LoginManager>();
        _http = Locator.Current.GetRequiredService<HttpClient>();
        localizationManager = Locator.Current.GetRequiredService<LocalizationManager>();
        ageManager = Locator.Current.GetRequiredService<AgeManager>();

        // Main Window Tabs
        _infoManager = Locator.Current.GetRequiredService<LauncherInfoManager>();

        ServersTab = new ServerListTabViewModel(this);
        NewsTab = new NewsTabViewModel();
        HomeTab = new HomePageViewModel(this);
        OptionsTab = new OptionsTabViewModel();

        var tabs = new List<MainWindowTabViewModel>();
        tabs.Add(HomeTab);
        tabs.Add(ServersTab);
        tabs.Add(NewsTab);
        tabs.Add(OptionsTab);
#if DEVELOPMENT
        tabs.Add(new DevelopmentTabViewModel());
#endif
        Tabs = tabs;

        AccountDropDown = new AccountDropDownViewModel(this);
        IdentityViewModel = new MainWindowIdentityViewModel();
        AgeViewModel = new MainWindowAgeViewModel(this);
        MainWindowEighteenPlusInitialSettingViewModel = new MainWindowEighteenPlusInitialSettingViewModel(this);

        this.WhenAnyValue(x => x._loginMgr.ActiveAccount)
            .Subscribe(s =>
            {
                this.RaisePropertyChanged(nameof(Username));
                this.RaisePropertyChanged(nameof(LoggedIn));
                this.RaisePropertyChanged(nameof(LoginText));
                this.RaisePropertyChanged(nameof(ManageAccountText));
            });

        _cfg.Logins.Connect()
            .Subscribe(_ => { this.RaisePropertyChanged(nameof(AccountDropDownVisible)); });

        // If we leave the login view model (by an account getting selected)
        // we reset it to login state
        this.WhenAnyValue(x => x.LoggedIn)
            .DistinctUntilChanged() // Only when change.
            .Subscribe(x =>
            {
                CalculateActiveMainWindow();

                if (x)
                {
                    // "Switch" to main window.
                    RunSelectedOnTab();
                }
                else
                {
                    IdentityViewModel.WizardsDenLoginTab.SwitchToLogin();
                }
            });

        this.WhenAnyValue(x => x.localizationManager.RequiresRestart)
            .Subscribe(s =>
            {
                this.RaisePropertyChanged(nameof(ShowLanguageChangedPopup));
                this.RaisePropertyChanged(nameof(LanguageChangedPopupHeaderText));
                this.RaisePropertyChanged(nameof(LanguageChangedPopupMessageText));
                this.RaisePropertyChanged(nameof(LanguageChangedPopupButtonText));
            });

        CalculateActiveMainWindow();
    }

    public MainWindow? Control { get; set; }

    public IReadOnlyList<MainWindowTabViewModel> Tabs { get; }

    public bool LoggedIn => _loginMgr.ActiveAccount != null;
    public string LoginText => LoggedIn ? Loc.GetString("'Logged in' as {0}.", Username) : Loc.GetString("Not logged in.");
    public string ManageAccountText => LoggedIn ? Loc.GetString("Change Account...") : Loc.GetString("Log in...");
    private string? Username => _loginMgr.ActiveAccount?.Username;
    public bool AccountDropDownVisible => _loginMgr.Logins.Count != 0;

    public AccountDropDownViewModel AccountDropDown { get; }

    public MainWindowIdentityViewModel IdentityViewModel { get; }
    public MainWindowEighteenPlusInitialSettingViewModel MainWindowEighteenPlusInitialSettingViewModel { get; }
    public MainWindowAgeViewModel AgeViewModel { get; }

    public bool AgeKnown => ageManager.AgeKnown;

    [Reactive] public ConnectingViewModel? ConnectingVM { get; set; }

    [Reactive] public string? BusyTask { get; private set; }
    [Reactive] public ViewModelBase? OverlayViewModel { get; private set; }

    public bool ShowLanguageChangedPopup => localizationManager.RequiresRestart;
    public string LanguageChangedPopupHeaderText => localizationManager.GetParticularString("Language Changed Restart Popup - Header", "Restart Required");
    public string LanguageChangedPopupMessageText => localizationManager.GetParticularString("Language Changed Restart Popup - Message", "Language has been changed.  Please start the launcher again for changes to take effect.");
    public string LanguageChangedPopupButtonText => localizationManager.GetParticularString("Language Changed Restart Popup - Button", "Close Application");

    [Reactive] public bool ShowMainWindow {get; set;} = false;
    [Reactive] public bool ShowAgeWindow {get; set;} = false;
    [Reactive] public bool ShowEighteenPlusInitialSettingWindow {get; set;} = false;
    [Reactive] public bool ShowIdentityWindow {get; set;} = false;

    private bool ShouldAskAboutInitialEighteenPlusFilter
    {
        get
        {
            // Don't ask underage users if they want to see 18+ servers, but do ask adults if they want to see 18+ servers.
            return ageManager.UserIs18Plus && !Cfg.GetCVar(CVars.InitialEighteenPlusPreferenceSet);
        }
    }

    /// <summary>
    /// This function will check what main window should be shown (ex: main, age, identity, etc) and set properties so
    /// it is shown.
    /// </summary>
    public void CalculateActiveMainWindow()
    {
        Type correctActiveWindowType = typeof(MainWindow);
        if (!ageManager.AgeKnown)
            correctActiveWindowType = typeof(MainWindowAge);
        else if (ShouldAskAboutInitialEighteenPlusFilter)
            correctActiveWindowType = typeof(MainWindowEighteenPlusInitialSetting);
        else if (!LoggedIn)
            correctActiveWindowType = typeof(MainWindowIdentity);

        if (correctActiveWindowType == _activeMainWindowType)
            return;

        ShowMainWindow = false;
        ShowAgeWindow = false;
        ShowIdentityWindow = false;
        ShowEighteenPlusInitialSettingWindow = false;

        if (correctActiveWindowType == typeof(MainWindow))
            ShowMainWindow = true;
        else if (correctActiveWindowType == typeof(MainWindowAge))
            ShowAgeWindow = true;
        else if (correctActiveWindowType == typeof(MainWindowEighteenPlusInitialSetting))
            ShowEighteenPlusInitialSettingWindow = true;
        else if (correctActiveWindowType == typeof(MainWindowIdentity))
            ShowIdentityWindow = true;

        this.RaisePropertyChanged(nameof(ShowMainWindow));
        this.RaisePropertyChanged(nameof(ShowAgeWindow));
        this.RaisePropertyChanged(nameof(ShowEighteenPlusInitialSettingWindow));
        this.RaisePropertyChanged(nameof(ShowIdentityWindow));

        _activeMainWindowType = correctActiveWindowType;
    }
    private Type _activeMainWindowType = null;

    /// <summary>
    /// Whether to show modal popup about having difficulty getting launcher info file from server
    /// </summary>
    /// <value></value>
    public bool ShowLauncherInfoError
    {
        get => _showLauncherInfoError;
        set => this.RaiseAndSetIfChanged(ref _showLauncherInfoError, value);
    }
    private bool _showLauncherInfoError = false;

    /// <summary>
    /// Text to be displayed in the launcher info error modal
    /// </summary>
    /// <value></value>
    public string LauncherInfoError
    {
        get => _launcherInfoError;
        set => this.RaiseAndSetIfChanged(ref _launcherInfoError, value);
    }
    private string _launcherInfoError;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var previous = Tabs[_selectedIndex];
            previous.IsSelected = false;

            this.RaiseAndSetIfChanged(ref _selectedIndex, value);

            RunSelectedOnTab();
        }
    }

    private void RunSelectedOnTab()
    {
        var tab = Tabs[_selectedIndex];
        tab.IsSelected = true;
        tab.Selected();
    }

    public ICVarEntry<bool> HasDismissedEarlyAccessWarning => Cfg.GetCVarEntry(CVars.HasDismissedEarlyAccessWarning);

    public async void OnWindowInitialized()
    {
        BusyTask = Loc.GetString("Checking for launcher update...");
        await CheckLauncherUpdate();
        BusyTask = Loc.GetString("Refreshing login status...");
        await CheckAccounts();
        BusyTask = null;

        var selectedLoginInfo = _cfg.SelectedLoginInfo;

        if (selectedLoginInfo != null)
        {
            var loggedInAccount = _loginMgr.GetLoggedInAccountByLoginInfo(selectedLoginInfo);
            if (loggedInAccount != null)
                TrySwitchToAccount(loggedInAccount);
        }

        // We should now start reacting to commands.
    }

    private async Task CheckAccounts()
    {
        // Check if accounts are still valid and refresh their tokens if necessary.
        await _loginMgr.Initialize();
    }

    public void OnWebsiteButtonPressed()
    {
        Helpers.OpenUri(new Uri(ConfigConstants.WebsiteUrl));
    }

    private async Task CheckLauncherUpdate()
    {
        // await Task.Delay(1000);
        if (!ConfigConstants.DoVersionCheck)
        {
            return;
        }

        await _infoManager.LoadTask;
        if (_infoManager.Model == null)
        {
            // Error while loading.
            Log.Warning("Unable to check for launcher update due to error, assuming up-to-date.");
            OutOfDate = false;
            ShowLauncherInfoError = true;
            LauncherInfoError = Loc.GetString("There was an error getting launcher information from server.  You likely have a network/firewall issue.  More information is available in the log file.");
            return;
        }

        OutOfDate = Array.IndexOf(_infoManager.Model.AllowedVersions, ConfigConstants.CurrentLauncherVersion) == -1;
        Log.Debug("Launcher out of date? {Value}", OutOfDate);
    }

    public void ExitPressed()
    {
        Control?.Close();
    }

    public void DownloadPressed()
    {
        Helpers.OpenUri(new Uri(ConfigConstants.DownloadUrl));
    }

    public void DismissEarlyAccessPressed()
    {
        Cfg.SetCVar(CVars.HasDismissedEarlyAccessWarning, true);
        Cfg.CommitConfig();
    }

    public void SelectTabServers()
    {
        SelectedIndex = Tabs.IndexOf(ServersTab);
    }

    public void TrySwitchToAccount(LoggedInAccount account)
    {
        switch (account.Status)
        {
            case AccountLoginStatus.Unsure:
                TrySelectUnsureAccount(account);
                break;

            case AccountLoginStatus.Available:
                _loginMgr.ActiveAccount = account;
                break;

            case AccountLoginStatus.Expired:
                _loginMgr.ActiveAccount = null;
                IdentityViewModel.WizardsDenLoginTab.SwitchToExpiredLogin(account);
                break;
        }
    }

    private async void TrySelectUnsureAccount(LoggedInAccount account)
    {
        BusyTask = Loc.GetString("Checking account status");
        try
        {
            await _loginMgr.UpdateSingleAccountStatus(account);

            // Can't be unsure, that'd have thrown.
            Debug.Assert(account.Status != AccountLoginStatus.Unsure);
            TrySwitchToAccount(account);
        }
        catch (AuthApiException e)
        {
            Log.Warning(e, "AuthApiException while trying to refresh account {login}", account.LoginInfo);
            OverlayViewModel = new AuthErrorsOverlayViewModel(this, Loc.GetString("Error connecting to authentication server"),
                new[]
                {
                    e.InnerException?.Message ?? Loc.GetString("Unknown error occured")
                });
        }
        finally
        {
            BusyTask = null;
        }
    }

    public void OverlayOk()
    {
        OverlayViewModel = null;
    }

    public bool IsContentBundleDropValid(string fileName)
    {
        // Can only load content bundles if logged in, in some capacity.
        if (!LoggedIn)
            return false;

        // Disallow if currently connecting to a server.
        if (ConnectingVM != null)
            return false;

        return Path.GetExtension(fileName) == ".zip";
    }

    public void Dropped(string fileName)
    {
        // Trust view validated this.
        Debug.Assert(IsContentBundleDropValid(fileName));

        ConnectingViewModel.StartContentBundle(this, fileName);
    }

    public void OnLanguageChangedPopupPressed()
    {
        // Forces user to restart on language change.
        // Just a quick, temporary workaround until proper reloading of all strings is properly implemented.

        // Quit app
        Control?.Close();
    }

    public void OnShowLauncherInfoErrorPopupPressed()
    {
        ShowLauncherInfoError = false;
    }

    public void OnShowLauncherInfoErrorOpenLogDirectoryPressed()
    {
        // TODO: Combine this and the options log directory into one common function?

        Process.Start(new ProcessStartInfo
        {
            UseShellExecute = true,
            FileName = LauncherPaths.DirLogs
        });
    }

       /// <summary>
    /// Whether to show modal popup about age page entry error
    /// </summary>
    /// <value></value>
    public bool ShowAgeError
    {
        get => _showAgeError;
        set => this.RaiseAndSetIfChanged(ref _showAgeError, value);
    }
    private bool _showAgeError = false;

    /// <summary>
    /// Text to be displayed in the age error modal
    /// </summary>
    /// <value></value>
    public string AgeErrorText
    {
        get => _ageErrorText;
        set => this.RaiseAndSetIfChanged(ref _ageErrorText, value);
    }
    private string _ageErrorText;

    public void OnShowAgeErrorPopupPressed()
    {
        ShowAgeError = false;
    }
}
