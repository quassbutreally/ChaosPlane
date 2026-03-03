using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChaosPlane.ViewModels;
using ChaosPlane.Services;
using ChaosPlane.Models;

namespace ChaosPlane.Views;

public sealed partial class SettingsDialog : ContentDialog
{
    public SettingsViewModel ViewModel { get; }

    public SettingsDialog(Microsoft.UI.Xaml.XamlRoot xamlRoot)
    {
        XamlRoot  = xamlRoot;
        ViewModel = new SettingsViewModel(App.MainViewModel, LaunchOAuthDialogAsync);
        InitializeComponent();

        this.Loaded += (_, _) =>
        {
            if (this.GetTemplateChild("BackgroundElement") is FrameworkElement bg)
                bg.MaxWidth = 800;
        };
    }

    private async Task LaunchOAuthDialogAsync()
    {
        using var listener = new LocalOAuthListener();

        var authUrl = App.TwitchService.BuildAuthUrl(LocalOAuthListener.RedirectUri);
        Windows.System.Launcher.LaunchUriAsync(new Uri(authUrl));

        // WinUI only allows one ContentDialog open at a time — hide settings first
        this.Hide();

        var waitDialog = new ContentDialog
        {
            Title           = "Authorise in Browser",
            Content         = "Complete the Twitch login in your browser.\nWaiting for authorisation...",
            CloseButtonText = "Cancel",
            XamlRoot        = XamlRoot,
            Background      = (Microsoft.UI.Xaml.Media.Brush)
                              Microsoft.UI.Xaml.Application.Current.Resources["CpPanelBrush"]
        };

        var dialogTask = waitDialog.ShowAsync().AsTask();
        var tokenTask  = listener.WaitForTokenAsync(TimeSpan.FromMinutes(5));
        var completed  = await Task.WhenAny(dialogTask, tokenTask);

        if (completed == tokenTask)
        {
            waitDialog.Hide();
            var token = await tokenTask;
            if (!string.IsNullOrEmpty(token))
                await App.MainViewModel.ApplyOAuthTokenAsync(token);
        }

        // Re-open settings so the user can see the updated connection status
        await this.ShowAsync();
    }
}

/// <summary>
/// Flat ViewModel for the Settings dialog.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly Func<Task>    _launchOAuth;

    public SettingsViewModel(MainViewModel main, Func<Task> launchOAuth)
    {
        _main        = main;
        _launchOAuth = launchOAuth;

        var s = main.CurrentSettings;
        _xplaneHost          = s.XPlane.Host;
        _xplanePort          = s.XPlane.Port.ToString();
        _minorTitle          = s.Rewards.Minor.Title;
        _minorCost           = s.Rewards.Minor.Cost.ToString();
        _moderateTitle       = s.Rewards.Moderate.Title;
        _moderateCost        = s.Rewards.Moderate.Cost.ToString();
        _severeTitle         = s.Rewards.Severe.Title;
        _severeCost          = s.Rewards.Severe.Cost.ToString();
        _pickYourPoisonTitle = s.Rewards.PickYourPoison.Title;
        _pickYourPoisonCost  = s.Rewards.PickYourPoison.Cost.ToString();

        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.TwitchConnected))
            {
                OnPropertyChanged(nameof(TwitchStatusText));
                ConnectTwitchCommand.NotifyCanExecuteChanged();
            }
            if (e.PropertyName == nameof(MainViewModel.XplaneConnected))
                OnPropertyChanged(nameof(XplaneStatusText));
            if (e.PropertyName == nameof(MainViewModel.TwitchStatusText))
                OnPropertyChanged(nameof(TwitchStatusText));
            if (e.PropertyName == nameof(MainViewModel.XplaneStatusText))
                OnPropertyChanged(nameof(XplaneStatusText));
        };
    }

    [ObservableProperty] private string _xplaneHost;
    [ObservableProperty] private string _xplanePort;
    [ObservableProperty] private string _minorTitle;
    [ObservableProperty] private string _minorCost;
    [ObservableProperty] private string _moderateTitle;
    [ObservableProperty] private string _moderateCost;
    [ObservableProperty] private string _severeTitle;
    [ObservableProperty] private string _severeCost;
    [ObservableProperty] private string _pickYourPoisonTitle;
    [ObservableProperty] private string _pickYourPoisonCost;

    public string XplaneStatusText => _main.XplaneStatusText;
    public string TwitchStatusText => _main.TwitchStatusText;

    public IRelayCommand ConnectXPlaneCommand         => _main.ConnectXPlaneCommand;
    public IRelayCommand DisconnectTwitchCommand      => _main.DisconnectTwitchCommand;
    public IRelayCommand CreateOrUpdateRewardsCommand => _main.CreateOrUpdateRewardsCommand;

    [RelayCommand(CanExecute = nameof(CanConnectTwitch))]
    private async Task ConnectTwitchAsync() => await _launchOAuth();
    private bool CanConnectTwitch() => !_main.TwitchConnected;

    partial void OnXplaneHostChanged(string value)
    {
        _main.CurrentSettings.XPlane.Host = value;
        _ = _main.SaveSettingsAsync();
    }

    partial void OnXplanePortChanged(string value)
    {
        if (int.TryParse(value, out var port))
        {
            _main.CurrentSettings.XPlane.Port = port;
            _ = _main.SaveSettingsAsync();
        }
    }

    partial void OnMinorTitleChanged(string value)          => SaveReward(r => r.Minor.Title = value);
    partial void OnMinorCostChanged(string value)           => SaveReward(r => r.Minor.Cost  = Parse(value));
    partial void OnModerateTitleChanged(string value)       => SaveReward(r => r.Moderate.Title = value);
    partial void OnModerateCostChanged(string value)        => SaveReward(r => r.Moderate.Cost  = Parse(value));
    partial void OnSevereTitleChanged(string value)         => SaveReward(r => r.Severe.Title = value);
    partial void OnSevereCostChanged(string value)          => SaveReward(r => r.Severe.Cost  = Parse(value));
    partial void OnPickYourPoisonTitleChanged(string value) => SaveReward(r => r.PickYourPoison.Title = value);
    partial void OnPickYourPoisonCostChanged(string value)  => SaveReward(r => r.PickYourPoison.Cost  = Parse(value));

    private void SaveReward(Action<RewardsSettings> update)
    {
        update(_main.CurrentSettings.Rewards);
        _ = _main.SaveSettingsAsync();
    }

    private static int Parse(string value) =>
        int.TryParse(value, out var n) ? n : 0;
}