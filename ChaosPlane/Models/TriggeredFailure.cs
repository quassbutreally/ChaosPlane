namespace ChaosPlane.Models;

/// <summary>
/// A runtime record of a failure that has been triggered during the current session.
/// Drives the Active Failures list and Event Log on the dashboard.
/// Never serialised — resets when the app restarts.
/// </summary>
public class TriggeredFailure
{
    /// <summary>Unique per-trigger instance ID for deduplication in the UI.</summary>
    public Guid InstanceId { get; init; } = Guid.NewGuid();

    /// <summary>The resolved failure that was triggered.</summary>
    public ResolvedFailure Failure { get; init; } = null!;

    /// <summary>Twitch display name of the viewer who redeemed the reward.</summary>
    public string RedeemedBy { get; init; } = string.Empty;

    /// <summary>
    /// True if this was triggered via Pick Your Poison rather than a tier reward.
    /// </summary>
    public bool WasPickYourPoison { get; init; }

    /// <summary>UTC time the failure was triggered.</summary>
    public DateTimeOffset TriggeredAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Whether the failure is currently active (not yet reset).</summary>
    public bool IsActive { get; set; } = true;

    // ── Convenience pass-throughs for XAML bindings ──────────────────────────

    public string      Name        => Failure.Name;
    public string      Description => Failure.Description;
    public FailureTier Tier        => Failure.EffectiveTier!.Value;

    public string TierLabel => Tier switch
    {
        FailureTier.Minor    => "MINOR",
        FailureTier.Moderate => "MODERATE",
        FailureTier.Severe   => "SEVERE",
        _                    => string.Empty
    };

    public string FormattedTime => TriggeredAt.ToLocalTime().ToString("HH:mm:ss");

    public string EventLogSummary =>
        WasPickYourPoison
            ? $"{FormattedTime}  ☠️  {RedeemedBy} → {Name}  [Pick Your Poison]"
            : $"{FormattedTime}  {TierLabel}  {RedeemedBy} → {Name}";
}
