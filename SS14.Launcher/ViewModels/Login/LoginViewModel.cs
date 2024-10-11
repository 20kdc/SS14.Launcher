using System;
using System.Threading.Tasks;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using SS14.Launcher.Api;
using SS14.Launcher.Models.Data;
using SS14.Launcher.Models.Logins;
using System.Linq;
using SS14.Launcher.ViewModels.IdentityTabs;

namespace SS14.Launcher.ViewModels.Login;

public class LoginViewModel : BaseLoginViewModel
{
    private readonly AuthApi _authApi;
    private readonly LoginManager _loginMgr;
    private readonly DataManager _dataManager;

    [Reactive] public string EditingUsername { get; set; } = "";
    [Reactive] public string EditingPassword { get; set; } = "";

    [Reactive] public bool IsInputValid { get; private set; }
    [Reactive] public bool IsPasswordVisible { get; set; }

    public LoginViewModel(LoginTabViewModel parentVm, AuthApi authApi,
        LoginManager loginMgr, DataManager dataManager) : base(parentVm)
    {
        BusyText = "Logging in...";
        _authApi = authApi;
        _loginMgr = loginMgr;
        _dataManager = dataManager;

        this.WhenAnyValue(x => x.EditingUsername, x => x.EditingPassword)
            .Subscribe(s => { IsInputValid = !string.IsNullOrEmpty(s.Item1) && !string.IsNullOrEmpty(s.Item2); });
    }

    public async void OnLogInButtonPressed()
    {
        if (!IsInputValid || Busy)
        {
            return;
        }

        Busy = true;
        try
        {
            var request = new AuthApi.AuthenticateRequest(EditingUsername, EditingPassword);
            var resp = await _authApi.AuthenticateAsync(request);

            await DoLogin(this, request, resp, _loginMgr, _authApi);

            _dataManager.CommitConfig();
        }
        finally
        {
            Busy = false;
        }
    }

    public static async Task<bool> DoLogin<T>(
        T vm,
        AuthApi.AuthenticateRequest request,
        AuthenticateResult resp,
        LoginManager loginMgr,
        AuthApi authApi)
        where T : BaseLoginViewModel, IErrorOverlayOwner
    {
        if (resp.IsSuccess)
        {
            var loginInfo = resp.LoginInfo;
            var oldLogin = loginMgr.GetLoggedInAccountByAccountLoginGuid(loginInfo.UserId);
            if (oldLogin != null && oldLogin.LoginInfo is LoginInfoAccount accountInfo)
            {
                // Already had this login, apparently.
                //
                // Log the OLD token out since we don't need two of them.
                // This also has the upside of re-available-ing the account
                // if the user used the main login prompt on an account we already had, but as expired.

                await authApi.LogoutTokenAsync(accountInfo.Token.Token);
                loginMgr.ActiveAccount = loginMgr.GetLoggedInAccountByLoginInfo(accountInfo);
                loginMgr.UpdateToNewToken(loginMgr.ActiveAccount!, accountInfo.Token);
                return true;
            }

            loginMgr.AddFreshLogin(loginInfo);
            loginMgr.ActiveAccount = loginMgr.GetLoggedInAccountByLoginInfo(loginInfo);
            return true;
        }

        if (resp.Code == AuthApi.AuthenticateDenyResponseCode.TfaRequired)
        {
            vm.ParentVM.SwitchToAuthTfa(request);
            return false;
        }

        var errors = AuthErrorsOverlayViewModel.AuthCodeToErrors(resp.Errors, resp.Code);
        vm.OverlayControl = new AuthErrorsOverlayViewModel(vm, "Unable to log in", errors);
        return false;
    }

    public void RegisterPressed()
    {
        // Registration is purely via website now, sorry.
        Helpers.OpenUri(ConfigConstants.AccountRegisterUrl);
    }

    public void ResendConfirmationPressed()
    {
        // Registration is purely via website now, sorry.
        Helpers.OpenUri(ConfigConstants.AccountResendConfirmationUrl);
    }
}
