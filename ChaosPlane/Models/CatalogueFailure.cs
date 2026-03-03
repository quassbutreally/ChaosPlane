namespace ChaosPlane.Models;

/// <summary>
/// A single entry in FailureCatalogue.json — the shipped, read-only reference of every
/// meaningful CL650 failure. This is the source of truth for what's *available*.
///
/// It is never written by the app. User configuration lives in FailureConfig.json.
/// </summary>
public class CatalogueFailure
{
    /// <summary>
    /// Unique stable identifier used to cross-reference with FailureConfig.json.
    /// Snake_case, e.g. "eng1_fire_ext1".
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable name shown in the UI and Twitch chat.
    /// e.g. "Engine 1 Fire (Extinguishable)"
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Short description of what the failure does and what the crew needs to do.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// ATA chapter / logical grouping for display in the failure browser.
    /// e.g. "Fire Protection", "Hydraulics", "Autopilot"
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Tier suggested by the catalogue based on category and severity.
    /// Shown as a default in the failure browser — user can override.
    /// Null means the catalogue has no strong opinion (rare).
    /// </summary>
    public FailureTier? SuggestedTier { get; set; }

    /// <summary>
    /// One or more dataref writes to perform when triggering this failure.
    /// </summary>
    public List<DatarefAction> Actions { get; set; } = [];
}
