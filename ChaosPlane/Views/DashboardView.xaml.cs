using Microsoft.UI.Xaml.Controls;
using ChaosPlane.ViewModels;

namespace ChaosPlane.Views;

public sealed partial class DashboardView : Page
{
    public DashboardViewModel ViewModel => App.MainViewModel.Dashboard;

    public DashboardView()
    {
        InitializeComponent();
    }
}
