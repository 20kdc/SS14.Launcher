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
using SS14.Launcher.Models.Data;
using SS14.Launcher.Models.Logins;
using SS14.Launcher.Utility;
using SS14.Launcher.ViewModels.IdentityTabs;
using SS14.Launcher.ViewModels.Login;
using SS14.Launcher.ViewModels.MainWindowTabs;
using SS14.Launcher.Views;

namespace SS14.Launcher.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, IErrorOverlayOwner
{
    private readonly DataManager _cfg;
    private readonly LoginManager _loginMgr;
    private readonly HttpClient _http;
    private readonly LocalizationManager localizationManager;

    private int _selectedIndex;

    public DataManager Cfg => _cfg;
    [Reactive] public bool OutOfDate { get; private set; }

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

        // Main Window Tabs

        ServersTab = new ServerListTabViewModel(this);
        NewsTab = new NewsTabViewModel();
        HomeTab = new HomePageViewModel(this);
        OptionsTab = new OptionsTabViewModel();

        Tabs = new MainWindowTabViewModel[]
        {
            HomeTab,
            ServersTab,
            NewsTab,
            OptionsTab
        };

        AccountDropDown = new AccountDropDownViewModel(this);
        IdentityViewModel = new MainWindowIdentityViewModel();

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

    [Reactive] public ConnectingViewModel? ConnectingVM { get; set; }

    [Reactive] public string? BusyTask { get; private set; }
    [Reactive] public ViewModelBase? OverlayViewModel { get; private set; }

    public bool ShowLanguageChangedPopup => localizationManager.RequiresRestart;
    public string LanguageChangedPopupHeaderText => localizationManager.GetParticularString("Language Changed Restart Popup - Header", "Restart Required");
    public string LanguageChangedPopupMessageText => localizationManager.GetParticularString("Language Changed Restart Popup - Message", "Language has been changed.  Please start the launcher again for changes to take effect.");
    public string LanguageChangedPopupButtonText => localizationManager.GetParticularString("Language Changed Restart Popup - Button", "Close Application");


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
        BusyTask = "Checking for launcher update...";
        await CheckLauncherUpdate();
        BusyTask = "Refreshing login status...";
        await CheckAccounts();
        BusyTask = null;

        if (_cfg.SelectedLoginId is { } g && _loginMgr.Logins.TryLookup(g, out var login))
        {
            TrySwitchToAccount(login);
        }

        // We should now start reacting to commands.
    }

    private async Task CheckAccounts()
    {
        // Check if accounts are still valid and refresh their tokens if necessary.
        await _loginMgr.Initialize();
    }

    public void OnDiscordButtonPressed()
    {
        Helpers.OpenUri(new Uri(ConfigConstants.DiscordUrl));
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

        try
        {
            var curVersion = await _http.GetStringAsync(ConfigConstants.LauncherVersionUrl);
            var versions = curVersion.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            OutOfDate = Array.IndexOf(versions, ConfigConstants.CurrentLauncherVersion) == -1;
        }
        catch (HttpRequestException e)
        {
            Log.Warning(e, "Unable to check for launcher update due to error, assuming up-to-date.");
            OutOfDate = false;
        }
        catch (TaskCanceledException e)
        {
            Log.Warning(e, "Unable to check for launcher update due to network timeout, assuming up-to-date.");
            OutOfDate = false;
        }
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
        BusyTask = "Checking account status";
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
            OverlayViewModel = new AuthErrorsOverlayViewModel(this, "Error connecting to authentication server",
                new[]
                {
                    e.InnerException?.Message ?? "Unknown error occured"
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
}
