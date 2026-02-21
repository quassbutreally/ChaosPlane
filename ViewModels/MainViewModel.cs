using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChaosPlane.Models;
using ChaosPlane.Services;

namespace ChaosPlane.ViewModels;

/// <summary>
/// Top-level ViewModel. Owns all services and drives the connection lifecycle.
/// Exposes child ViewModels for the two main views.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly SettingsService      _settingsService;
    private readonly CatalogueService     _catalogueService;
    private readonly FailureConfigService _configService;
    private readonly XPlaneService        _xplaneService;
    private readonly TwitchService        _twitchService;
    private readonly FailureOrchestrator  _orchestrator;

    // ── Child ViewModels ──────────────────────────────────────────────────────

    public FailureBrowserViewModel FailureBrowser { get; }
    public DashboardViewModel      Dashboard      { get; }

    // ── Observable state ──────────────────────────────────────────────────────

    [ObservableProperty]
    private bool _twitchConnected;

    [ObservableProperty]
    private bool _xplaneConnected;

    [ObservableProperty]
    private string _twitchStatusText = "Not connected";

    [ObservableProperty]
    private string _xplaneStatusText = "Not connected";

    [ObservableProperty]
    private string? _channelName;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusMessage;

    // ── Constructor ───────────────────────────────────────────────────────────

    public MainViewModel(
        SettingsService      settingsService,
        CatalogueService     catalogueService,
        FailureConfigService configService,
        XPlaneService        xplaneService,
        TwitchService        twitchService,
        FailureOrchestrator  orchestrator)
    {
        _settingsService  = settingsService;
        _catalogueService = catalogueService;
        _configService    = configService;
        _xplaneService    = xplaneService;
        _twitchService    = twitchService;
        _orchestrator     = orchestrator;

        FailureBrowser = new FailureBrowserViewModel(catalogueService, configService);
        Dashboard      = new DashboardViewModel(orchestrator, twitchService, xplaneService);

        // Wire up service events
        _twitchService.ConnectionChanged  += OnTwitchConnectionChanged;
        _xplaneService.ConnectionChanged  += OnXplaneConnectionChanged;
        _orchestrator.FailureTriggered    += Dashboard.OnFailureTriggered;
        _orchestrator.PickYourPoisonNoMatch += Dashboard.OnPickYourPoisonNoMatch;
    }

    // ── Settings access (used by SettingsView) ────────────────────────────────

    public AppSettings CurrentSettings => _settingsService.Settings;

    public async Task SaveSettingsAsync() => await _settingsService.SaveAsync();

    // ── Initialisation ────────────────────────────────────────────────────────

    /// <summary>
    /// Called by the main window on startup. Loads settings and catalogue,
    /// then attempts to reconnect to services using stored credentials.
    /// </summary>
    public async Task InitialiseAsync(string cataloguePath)
    {
        IsBusy = true;
        StatusMessage = "Loading settings...";

        await _settingsService.LoadAsync();
        await _configService.LoadAsync();
        await _catalogueService.LoadAsync(cataloguePath);

        FailureBrowser.Refresh();

        StatusMessage = "Connecting to X-Plane...";
        await TryConnectXPlaneAsync();

        if (!string.IsNullOrEmpty(_settingsService.Settings.Twitch.AccessToken))
        {
            StatusMessage = "Reconnecting to Twitch...";
            await TryConnectTwitchAsync();
        }

        StatusMessage = null;
        IsBusy = false;
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ConnectXPlaneAsync()
    {
        IsBusy = true;
        await TryConnectXPlaneAsync();
        IsBusy = false;
    }

    [RelayCommand]
    private async Task ConnectTwitchAsync()
    {
        IsBusy = true;
        await TryConnectTwitchAsync();
        IsBusy = false;
    }

    [RelayCommand]
    private async Task DisconnectTwitchAsync()
    {
        await _twitchService.DisconnectAsync();
    }

    [RelayCommand]
    private async Task CreateOrUpdateRewardsAsync()
    {
        if (!TwitchConnected)
        {
            StatusMessage = "Connect to Twitch first.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Updating Twitch rewards...";

        try
        {
            await _twitchService.CreateOrUpdateRewardsAsync();
            StatusMessage = "Rewards updated successfully.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to update rewards: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ── OAuth callback ────────────────────────────────────────────────────────

    /// <summary>
    /// Called by the OAuth window after WebView2 captures the access token
    /// from the redirect URI fragment.
    /// </summary>
    public async Task ApplyOAuthTokenAsync(string accessToken)
    {
        IsBusy = true;
        StatusMessage = "Validating Twitch token...";

        var ok = await _twitchService.ApplyTokenAsync(accessToken);

        if (ok)
        {
            await TryConnectTwitchAsync();
        }
        else
        {
            StatusMessage = "Token validation failed. Please try again.";
            IsBusy = false;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task TryConnectXPlaneAsync()
    {
        var settings = _settingsService.Settings.XPlane;
        var ok       = await _xplaneService.ConnectAsync(settings);

        XplaneConnected  = ok;
        XplaneStatusText = ok
            ? $"Connected — {settings.Host}:{settings.Port}"
            : $"Not connected — {settings.Host}:{settings.Port}";
    }

    private async Task TryConnectTwitchAsync()
    {
        try
        {
            var ok = await _twitchService.ConnectAsync();

            TwitchConnected  = ok;
            TwitchStatusText = ok
                ? $"Connected as {_twitchService.ChannelName}"
                : "Connection failed — check your token.";
            ChannelName = _twitchService.ChannelName;
        }
        catch (Exception ex)
        {
            TwitchConnected  = false;
            TwitchStatusText = $"Error: {ex.Message}";
        }

        IsBusy        = false;
        StatusMessage = null;
    }

    private void OnXplaneConnectionChanged(bool connected)
    {
        App.DispatchToUi(() =>
        {
            XplaneConnected  = connected;
            XplaneStatusText = connected
                ? $"Connected — {_settingsService.Settings.XPlane.Host}:{_settingsService.Settings.XPlane.Port}"
                : $"Not connected — {_settingsService.Settings.XPlane.Host}:{_settingsService.Settings.XPlane.Port}";
        });
    }

    private void OnTwitchConnectionChanged(bool connected)
    {
        // Called from TwitchService potentially on a background thread
        // CommunityToolkit.Mvvm dispatches property changes to the UI thread
        TwitchConnected  = connected;
        TwitchStatusText = connected
            ? $"Connected as {_twitchService.ChannelName}"
            : "Disconnected";
    }
}