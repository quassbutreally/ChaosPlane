namespace ChaosPlane.Models;

/// <summary>
/// A single entry in FailureConfig.json — the user's configuration for a specific failure.
///
/// Only failures with an entry here (with Enabled = true and a Tier set) are
/// triggerable. Failures in the catalogue with no config entry are browsable but inert.
/// </summary>
public class FailureConfigEntry
{
    /// <summary>
    /// Must match a CatalogueFailure.Id exactly.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Whether this failure is currently opt-in to the trigger pool.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The tier the user has assigned. Must be set for the failure to be triggerable.
    /// </summary>
    public FailureTier? Tier { get; set; }
}
