using System.Collections.Generic;
using ReactiveUI;
using Splat;
using SS14.Launcher.Api;
using SS14.Launcher.Models.Data;
using SS14.Launcher.Models.Logins;
using SS14.Launcher.Utility;
using SS14.Launcher.ViewModels.IdentityTabs;
using SS14.Launcher.ViewModels.Login;

namespace SS14.Launcher.ViewModels;

public class MainWindowIdentityViewModel : ViewModelBase
{
    private readonly DataManager _cfg;

    // Identity Tabs
    public InformationTabViewModel InformationTab { get; }
    public KeyNewTabViewModel KeyNewTab { get; }
    public KeyImportTabViewModel KeyImportTab { get; }
    public GuestTabViewModel GuestTab { get; }
    public LoginTabViewModel WizardsDenLoginTab { get; }

    public IReadOnlyList<IdentityTabViewModel> IdentityTabs { get; }

    public LanguageDropDownViewModel LanguageDropDown { get; }

    public MainWindowIdentityViewModel()
    {
        _cfg = Locator.Current.GetRequiredService<DataManager>();

        // Identity Tabs

        InformationTab = new InformationTabViewModel();
        KeyNewTab = new KeyNewTabViewModel();
        KeyImportTab = new KeyImportTabViewModel();
        GuestTab = new GuestTabViewModel();
        WizardsDenLoginTab = new LoginTabViewModel();

        IdentityTabs = new IdentityTabViewModel[]
        {
            InformationTab,
            KeyNewTab,
            KeyImportTab,
            GuestTab,
            WizardsDenLoginTab
        };

        LanguageDropDown = new LanguageDropDownViewModel();
    }

    public string Version => $"v{LauncherVersion.Version}";

    public bool LogLauncher
    {
        // This not a clean solution, replace it with something better.
        get => _cfg.GetCVar(CVars.LogLauncher);
        set
        {
            _cfg.SetCVar(CVars.LogLauncher, value);
            _cfg.CommitConfig();
        }
    }

    private int _selectedIndex;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var previous = IdentityTabs[_selectedIndex];
            previous.IsSelected = false;

            this.RaiseAndSetIfChanged(ref _selectedIndex, value);

            RunSelectedOnTab();
        }
    }

    private void RunSelectedOnTab()
    {
        var tab = IdentityTabs[_selectedIndex];
        tab.IsSelected = true;
        tab.Selected();
    }
}
