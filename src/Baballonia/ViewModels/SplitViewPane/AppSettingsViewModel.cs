using System;
using Baballonia.Contracts;
using Baballonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace Baballonia.ViewModels.SplitViewPane;

public partial class AppSettingsViewModel : ViewModelBase
{
    public IOscTarget OscTarget { get; private set;}
    public ILocalSettingsService SettingsService { get; }
    public GithubService GithubService { get; private set;}
    public ParameterSenderService ParameterSenderService { get; private set;}

    [ObservableProperty]
    [property: SavedSetting("AppSettings_RecalibrateAddress", "/avatar/parameters/etvr_recalibrate")]
    private string _recalibrateAddress;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_RecalibrateAddress", "/avatar/parameters/etvr_recenter")]
    private string _recenterAddress;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_UseOSCQuery", false)]
    private bool _useOscQuery;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_OSCPrefix", "")]
    private string _oscPrefix;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_OneEuroMinFreqCutoff", 1f)]
    private float _oneEuroMinFreqCutoff;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_OneEuroSpeedCutoff", 1f)]
    private float _oneEuroSpeedCutoff;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_UseGPU", false)]
    private bool _useGPU;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_CheckForUpdates", false)]
    private bool _checkForUpdates;

    [ObservableProperty] private bool _onboardingEnabled;

    public AppSettingsViewModel()
    {
        // General/Calibration Settings
        OscTarget = Ioc.Default.GetService<IOscTarget>()!;
        GithubService = Ioc.Default.GetService<GithubService>()!;
        SettingsService = Ioc.Default.GetService<ILocalSettingsService>()!;
        SettingsService.Load(this);

        // Risky Settings
        ParameterSenderService = Ioc.Default.GetService<ParameterSenderService>()!;

        OnboardingEnabled = !(OperatingSystem.IsAndroid() || OperatingSystem.IsIOS());

        PropertyChanged += (_, _) =>
        {
            SettingsService.Save(this);
        };
    }
}
