using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ChaosPlane.ViewModels;
using ChaosPlane.Views;
using Windows.Graphics;
namespace ChaosPlane;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel => App.MainViewModel;

    private Button? _activeNavButton;

    public MainWindow()
    {
        InitializeComponent();
        SetupTitleBar();
        NavigateTo(NavDashboard, typeof(DashboardView));

        // Window cannot use x:Bind — wire up all dynamic text imperatively
        var vm = App.MainViewModel;
        vm.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.XplaneConnected):
                    UpdateIndicator(XplaneIndicator, vm.XplaneConnected); break;
                case nameof(MainViewModel.TwitchConnected):
                    UpdateIndicator(TwitchIndicator, vm.TwitchConnected); break;
                case nameof(MainViewModel.StatusMessage):
                    StatusMessageText.Text = vm.StatusMessage ?? string.Empty; break;
            }
        };

        vm.Dashboard.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(DashboardViewModel.ActiveFailureCount):
                    ActiveFailureCountText.Text = vm.Dashboard.ActiveFailureCount.ToString(); break;
                case nameof(DashboardViewModel.SessionTriggerCount):
                    SessionTriggerCountText.Text = vm.Dashboard.SessionTriggerCount.ToString(); break;
                case nameof(DashboardViewModel.LastEventText):
                    LastEventText.Text = vm.Dashboard.LastEventText ?? "— no events yet —"; break;
            }
        };
    }

    private static void UpdateIndicator(TextBlock indicator, bool connected)
    {
        indicator.Foreground = connected
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x00, 0xE6, 0x76))
            : new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x3D, 0x4F, 0x47));
    }

    // ── Title bar ─────────────────────────────────────────────────────────────

    private void SetupTitleBar()
    {
        AppWindow.Resize(new SizeInt32(1280, 800));
        var presenter = (Microsoft.UI.Windowing.OverlappedPresenter)AppWindow.Presenter;
        presenter.IsResizable    = false;
        presenter.IsMaximizable  = false;
        presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarGrid);
    }

    private void MinimiseButton_Click(object sender, RoutedEventArgs e) =>
        ((Microsoft.UI.Windowing.OverlappedPresenter)AppWindow.Presenter).Minimize();

    private void CloseButton_Click(object sender, RoutedEventArgs e) =>
        Close();

    // ── Navigation ────────────────────────────────────────────────────────────

    private void NavDashboard_Click(object sender, RoutedEventArgs e) =>
        NavigateTo(NavDashboard, typeof(DashboardView));

    private void NavFailureBrowser_Click(object sender, RoutedEventArgs e) =>
        NavigateTo(NavFailureBrowser, typeof(FailureBrowserView));

    private void NavSettings_Click(object sender, RoutedEventArgs e) =>
        _ = OpenSettingsAsync();

    private async Task OpenSettingsAsync()
    {
        var dialog = new SettingsDialog(ContentFrame.XamlRoot);
        await dialog.ShowAsync();
        // Re-highlight whichever nav item was active before settings was opened
        if (_activeNavButton != null)
            SetActiveNav(_activeNavButton);
    }

    private void NavigateTo(Button navButton, Type pageType)
    {
        if (ContentFrame.CurrentSourcePageType == pageType) return;

        ContentFrame.Navigate(pageType);
        SetActiveNav(navButton);
    }

    private void SetActiveNav(Button button)
    {
        if (_activeNavButton != null)
        {
            _activeNavButton.Background = (SolidColorBrush)Application.Current.Resources["CpPanelBrush"];
            _activeNavButton.Foreground = (SolidColorBrush)Application.Current.Resources["CpAmberBrush"];
        }

        _activeNavButton = button;
        button.Background = (SolidColorBrush)Application.Current.Resources["CpPanelRaisedBrush"];
        button.Foreground = (SolidColorBrush)Application.Current.Resources["CpTextBrush"];
    }
}
