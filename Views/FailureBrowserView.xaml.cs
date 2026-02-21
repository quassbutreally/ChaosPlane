using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using ChaosPlane.Models;
using ChaosPlane.ViewModels;

namespace ChaosPlane.Views;

public sealed partial class FailureBrowserView : Page
{
    public FailureBrowserViewModel ViewModel => App.MainViewModel.FailureBrowser;

    public FailureBrowserView()
    {
        InitializeComponent();
    }
}

// ── Value converters ──────────────────────────────────────────────────────────

/// <summary>FailureTier? → display label string.</summary>
public class TierToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is FailureTier tier ? tier switch
        {
            FailureTier.Minor    => "MINOR",
            FailureTier.Moderate => "MODERATE",
            FailureTier.Severe   => "SEVERE",
            _                    => "—"
        } : "—";

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>FailureTier? → background brush for the tier pill.</summary>
public class TierToBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not FailureTier tier)
            return new SolidColorBrush(Colors.Transparent);

        return tier switch
        {
            FailureTier.Minor    => new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, 0x69, 0xFF, 0x47)),
            FailureTier.Moderate => new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, 0xFF, 0xCC, 0x00)),
            FailureTier.Severe   => new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, 0xFF, 0x3D, 0x3D)),
            _                    => new SolidColorBrush(Colors.Transparent)
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>FailureTier? → foreground brush for the tier pill text.</summary>
public class TierToForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not FailureTier tier)
            return new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x6E, 0x84, 0x7A));

        return tier switch
        {
            FailureTier.Minor    => new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x69, 0xFF, 0x47)),
            FailureTier.Moderate => new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xCC, 0x00)),
            FailureTier.Severe   => new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x3D, 0x3D)),
            _                    => new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x6E, 0x84, 0x7A))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>bool connected → Brush (green / muted dot).</summary>
public class BoolToConnectionBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x00, 0xE6, 0x76))  // CpGreen
            : new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x3D, 0x4F, 0x47)); // CpTextMuted

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>bool hasUnsaved → Color (amber / muted) for the save bar dot.</summary>
public class BoolToUnsavedColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var brush = value is true
            ? (SolidColorBrush)Microsoft.UI.Xaml.Application.Current.Resources["CpAmberBrush"]
            : (SolidColorBrush)Microsoft.UI.Xaml.Application.Current.Resources["CpTextMutedBrush"];
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}
