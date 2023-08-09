using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CodeHollow.FeedReader;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Splat;
using SS14.Launcher.Api;
using SS14.Launcher.Models.Data;
using SS14.Launcher.Models.Logins;
using SS14.Launcher.Utility;

namespace SS14.Launcher.ViewModels.IdentityTabs;

public class GuestTabViewModel : IdentityTabViewModel
{
    private readonly AuthApi _authApi;
    private readonly LoginManager _loginMgr;
    private readonly DataManager _dataManager;

    [Reactive] public string EditingUsername { get; set; } = "";

    [Reactive] public bool IsInputValid { get; private set; }

    public GuestTabViewModel()
    {
        _authApi = Locator.Current.GetRequiredService<AuthApi>();
        _loginMgr = Locator.Current.GetRequiredService<LoginManager>();
        // TODO: BusyText
        // BusyText = "Logging in...";

        this.WhenAnyValue(x => x.EditingUsername)
            .Subscribe(s => { IsInputValid = !string.IsNullOrEmpty(s); });
    }

    public void SkipLoginPressed()
    {
        // Registration is purely via website now, sorry.
        //Helpers.OpenUri(ConfigConstants.AccountRegisterUrl);
        DoUnauthLogin();
    }

    private void DoUnauthLogin()
    {
        string username = EditingUsername.Trim();
        if (String.IsNullOrWhiteSpace(username) || username.Length == 0)
        {
            // TODO Overlay
            // this.OverlayControl = new AuthErrorsOverlayViewModel(this, "Username needed",
            //     new string[]{"Even though no account will be created, servers will still need a username to call you by.  Please enter a username in the username field.  (No password is needed)"});
            return;
        }

        if (!username.All(x => char.IsLetterOrDigit(x) || x == '_'))
        {
            // TODO Overlay
            // this.OverlayControl = new AuthErrorsOverlayViewModel(this, "Username bad characters",
            //     new string[]{"Username can only contain 0-9 a-z A-Z and _"});
            return;
        }

        var loginInfo = new LoginInfo();
        loginInfo.UserId = Guid.NewGuid(); // Guid.Empty;
        loginInfo.Username = EditingUsername;
        loginInfo.Token = new Models.LoginToken("", DateTimeOffset.UtcNow.AddHours(2));
        loginInfo.AuthServer = LoginInfo.CommonAuthServers.Offline.ToString();

        var oldLogin = _loginMgr.Logins.Lookup(loginInfo.UserId);
        if (oldLogin.HasValue)
        {
            _loginMgr.ActiveAccountId = loginInfo.UserId;
            _loginMgr.UpdateToNewToken(_loginMgr.ActiveAccount!, loginInfo.Token);
        } else {
            _loginMgr.AddFreshLogin(loginInfo);
            _loginMgr.ActiveAccountId = loginInfo.UserId;
        }
    }

    public override void Selected()
    {
        base.Selected();
    }

    public override string Name => "Guest";
}
