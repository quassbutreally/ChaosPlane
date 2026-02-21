namespace ChaosPlane.Models;

/// <summary>
/// Runtime merge of a CatalogueFailure and its corresponding FailureConfigEntry (if any).
/// This is what the FailureBrowserViewModel binds to and what FailureOrchestrator works with.
///
/// Created by CatalogueService — never serialised.
/// </summary>
public class ResolvedFailure
{
    // ── Catalogue data (always present) ──────────────────────────────────────

    public string Id          { get; init; } = string.Empty;
    public string Name        { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Category    { get; init; } = string.Empty;

    /// <summary>The catalogue's suggested tier, if any.</summary>
    public FailureTier? SuggestedTier { get; init; }

    /// <summary>Dataref writes to trigger this failure.</summary>
    public List<DatarefAction> Actions { get; init; } = [];

    // ── Config data (null / false if no config entry exists) ─────────────────

    /// <summary>
    /// True if the user has explicitly enabled this failure.
    /// False if there is no config entry, or the entry has Enabled = false.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The user-assigned tier. Null if no config entry exists (failure is not triggerable).
    /// </summary>
    public FailureTier? AssignedTier { get; set; }

    // ── Derived ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The effective tier used for pooling: AssignedTier if set, otherwise null.
    /// A null EffectiveTier means this failure is NOT in any trigger pool.
    /// </summary>
    public FailureTier? EffectiveTier => AssignedTier;

    /// <summary>
    /// True if this failure will actually be triggered by a redemption.
    /// Requires both Enabled = true and an AssignedTier.
    /// </summary>
    public bool IsTriggerable => Enabled && AssignedTier.HasValue;

    /// <summary>
    /// The tier to display in the browser — AssignedTier if set, SuggestedTier as a hint otherwise.
    /// </summary>
    public FailureTier? DisplayTier => AssignedTier ?? SuggestedTier;
}
