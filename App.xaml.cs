using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using ChaosPlane.Models;
using ChaosPlane.Services;
using ChaosPlane.ViewModels;
using System.Net.Http;

namespace ChaosPlane;

public partial class App : Application
{
    private static MainWindow? _window;
    private static DispatcherQueue? _dispatcherQueue;

    // Services (owned by App for lifetime management)
    public static MainViewModel MainViewModel { get; private set; } = null!;
    public static TwitchService TwitchService { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // ── Build services ────────────────────────────────────────────────────

        var settingsPath = Path.Combine(
            AppContext.BaseDirectory, "appsettings.json");

        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ChaosPlane", "FailureConfig.json");

        var cataloguePath = Path.Combine(
            AppContext.BaseDirectory, "Data", "FailureCatalogue.json");

        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

        var settingsService  = new SettingsService(settingsPath);
        var configService    = new FailureConfigService(configPath);
        var catalogueService = new CatalogueService(configService);
        var xplaneService    = new XPlaneService(new HttpClient());
        var twitchService    = new TwitchService(settingsService.Settings, settingsService);
        var orchestrator     = new FailureOrchestrator(catalogueService, xplaneService, twitchService);

        TwitchService    = twitchService;
        MainViewModel = new MainViewModel(
            settingsService,
            catalogueService,
            configService,
            xplaneService,
            twitchService,
            orchestrator);

        // ── Launch window ─────────────────────────────────────────────────────

        _window = new MainWindow();
        _dispatcherQueue = _window.DispatcherQueue;

        _window.Activate();

        // Kick off async initialisation after the window is visible
        _ = MainViewModel.InitialiseAsync(cataloguePath);
    }

    /// <summary>
    /// Marshals an action onto the UI thread. Safe to call from any thread.
    /// Used by ViewModels that receive events from background service threads.
    /// </summary>
    public static void DispatchToUi(Action action)
    {
        if (_dispatcherQueue is null)
        {
            action();
            return;
        }

        if (_dispatcherQueue.HasThreadAccess)
            action();
        else
            _dispatcherQueue.TryEnqueue(() => action());
    }
}
