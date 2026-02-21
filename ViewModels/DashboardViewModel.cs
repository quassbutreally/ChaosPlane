using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChaosPlane.Models;
using ChaosPlane.Services;

namespace ChaosPlane.ViewModels;

/// <summary>
/// ViewModel for the live session dashboard.
///
/// Shows:
///   - Currently active failures (with reset buttons)
///   - Recent redemption log
///   - Manual trigger controls for host use
///   - Pick Your Poison no-match notifications
/// </summary>
public partial class DashboardViewModel : ObservableObject
{
    private readonly FailureOrchestrator _orchestrator;
    private readonly TwitchService       _twitchService;
    private readonly XPlaneService       _xplaneService;

    private const int MaxLogEntries = 50;

    // ── Observable collections ────────────────────────────────────────────────

    /// <summary>Failures currently active in the sim.</summary>
    public ObservableCollection<ActiveFailureViewModel> ActiveFailures { get; } = [];

    /// <summary>Rolling log of recent events (redemptions, refunds, resets).</summary>
    public ObservableCollection<LogEntryViewModel> RecentLog { get; } = [];

    // ── Manual trigger ────────────────────────────────────────────────────────

    /// <summary>Text typed by the host to search for a failure to manually trigger.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ManualTriggerCommand))]
    private string _manualSearchText = string.Empty;

    /// <summary>Results matching the manual search text.</summary>
    public ObservableCollection<ResolvedFailure> ManualSearchResults { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ManualTriggerCommand))]
    private ResolvedFailure? _selectedManualFailure;

    // ── Status ────────────────────────────────────────────────────────────────

    [ObservableProperty]
    private int _activeFailureCount;

    [ObservableProperty]
    private int _sessionTriggerCount;

    [ObservableProperty]
    private string? _lastEventText;

    // ── Constructor ───────────────────────────────────────────────────────────

    public DashboardViewModel(
        FailureOrchestrator orchestrator,
        TwitchService       twitchService,
        XPlaneService       xplaneService)
    {
        _orchestrator  = orchestrator;
        _twitchService = twitchService;
        _xplaneService = xplaneService;

        _orchestrator.FailureReset += OnFailureReset;
    }

    // ── Called by MainViewModel (wired up there to avoid circular deps) ────────

    public void OnFailureTriggered(TriggeredFailure triggered)
    {
        // Dispatch to UI thread — events may fire on background threads
        App.DispatchToUi(() =>
        {
            var active = new ActiveFailureViewModel(triggered, ResetFailureAsync);
            ActiveFailures.Insert(0, active);
            ActiveFailureCount = ActiveFailures.Count;
            SessionTriggerCount++;

            var emoji = triggered.WasPickYourPoison ? "☠️" : TierEmoji(triggered.Failure.EffectiveTier);
            var label = triggered.WasPickYourPoison
                ? $"{emoji} {triggered.RedeemedBy} — Pick Your Poison: {triggered.Name}"
                : $"{emoji} {triggered.RedeemedBy} — {triggered.TierLabel}: {triggered.Name}";

            AddLog(label, LogEntryKind.Triggered);
            LastEventText = label;
        });
    }

    public void OnPickYourPoisonNoMatch(string viewerName, string input)
    {
        App.DispatchToUi(() =>
        {
            var label = $"❌ {viewerName} — no match for \"{input}\" — refunded";
            AddLog(label, LogEntryKind.Refunded);
            LastEventText = label;
        });
    }

    private void OnFailureReset(TriggeredFailure triggered)
    {
        App.DispatchToUi(() =>
        {
            var toRemove = ActiveFailures.FirstOrDefault(a => a.InstanceId == triggered.InstanceId);
            if (toRemove != null)
                ActiveFailures.Remove(toRemove);

            ActiveFailureCount = ActiveFailures.Count;
            AddLog($"✅ Reset: {triggered.Name}", LogEntryKind.Reset);
        });
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void SearchManual()
    {
        ManualSearchResults.Clear();

        if (string.IsNullOrWhiteSpace(ManualSearchText)) return;

        var results = _orchestrator
            // Access catalogue through orchestrator isn't ideal but avoids
            // threading a CatalogueService dependency here
            .GetTriggerableFailures()
            .Where(f => f.Name.Contains(ManualSearchText, StringComparison.OrdinalIgnoreCase) ||
                        f.Category.Contains(ManualSearchText, StringComparison.OrdinalIgnoreCase))
            .Take(20);

        foreach (var r in results)
            ManualSearchResults.Add(r);
    }

    [RelayCommand(CanExecute = nameof(CanManualTrigger))]
    private async Task ManualTriggerAsync()
    {
        if (SelectedManualFailure == null) return;

        var ok = await _orchestrator.TriggerAsync(SelectedManualFailure, triggeredBy: "Host");

        if (!ok)
        {
            AddLog("❌ X-Plane unreachable — failure not triggered", LogEntryKind.Refunded);
            return;
        }

        ManualSearchText      = string.Empty;
        SelectedManualFailure = null;
        ManualSearchResults.Clear();
    }

    private bool CanManualTrigger() =>
        SelectedManualFailure != null && _xplaneService.IsConnected;

    [RelayCommand]
    private async Task ResetAllAsync()
    {
        var toReset = ActiveFailures.ToList();
        foreach (var active in toReset)
            await _orchestrator.ResetAsync(active.Triggered);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task ResetFailureAsync(ActiveFailureViewModel active)
    {
        await _orchestrator.ResetAsync(active.Triggered);
    }

    private void AddLog(string text, LogEntryKind kind)
    {
        RecentLog.Insert(0, new LogEntryViewModel(text, kind));
        while (RecentLog.Count > MaxLogEntries)
            RecentLog.RemoveAt(RecentLog.Count - 1);
    }

    private static string TierEmoji(FailureTier? tier) => tier switch
    {
        FailureTier.Minor    => "🟢",
        FailureTier.Moderate => "🟡",
        FailureTier.Severe   => "🔴",
        _                    => "⚠️"
    };

    partial void OnManualSearchTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            ManualSearchResults.Clear();
            return;
        }

        // Live search on every keystroke
        ManualSearchResults.Clear();
        var results = _orchestrator
            .GetTriggerableFailures()
            .Where(f => f.Name.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                        f.Category.Contains(value, StringComparison.OrdinalIgnoreCase))
            .Take(20);

        foreach (var r in results)
            ManualSearchResults.Add(r);
    }
}

// ── Supporting ViewModels ─────────────────────────────────────────────────────

public partial class ActiveFailureViewModel : ObservableObject
{
    private readonly Func<ActiveFailureViewModel, Task> _resetCallback;

    public ActiveFailureViewModel(
        TriggeredFailure triggered,
        Func<ActiveFailureViewModel, Task> resetCallback)
    {
        Triggered      = triggered;
        _resetCallback = resetCallback;
        InstanceId     = triggered.InstanceId;
        Name           = triggered.Name;
        RedeemedBy     = triggered.RedeemedBy;
        TierLabel      = triggered.TierLabel;
        TriggeredAt    = triggered.TriggeredAt;
        WasPickYourPoison = triggered.WasPickYourPoison;
    }

    public TriggeredFailure Triggered     { get; }
    public Guid             InstanceId    { get; }
    public string           Name          { get; }
    public string           RedeemedBy    { get; }
    public string           TierLabel     { get; }
    public DateTimeOffset   TriggeredAt   { get; }
    public bool             WasPickYourPoison { get; }

    public string TimeAgo =>
        (DateTimeOffset.UtcNow - TriggeredAt) switch
        {
            { TotalSeconds: < 60 }  t => $"{(int)t.TotalSeconds}s ago",
            { TotalMinutes: < 60 }  t => $"{(int)t.TotalMinutes}m ago",
            var                     t => $"{(int)t.TotalHours}h ago"
        };

    [RelayCommand]
    private async Task ResetAsync() => await _resetCallback(this);
}

public class LogEntryViewModel
{
    public LogEntryViewModel(string text, LogEntryKind kind)
    {
        Text      = text;
        Kind      = kind;
        Timestamp = DateTimeOffset.Now;
    }

    public string         Text      { get; }
    public LogEntryKind   Kind      { get; }
    public DateTimeOffset Timestamp { get; }
    public string         TimeLabel => Timestamp.ToString("HH:mm:ss");
}

public enum LogEntryKind { Triggered, Refunded, Reset }