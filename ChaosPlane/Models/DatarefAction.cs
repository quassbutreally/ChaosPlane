namespace ChaosPlane.Models;

/// <summary>
/// A single dataref write to perform when triggering or clearing a failure.
/// Most failures have one action; compound failures (e.g. both yaw dampers) have multiple.
/// </summary>
public class DatarefAction
{
    /// <summary>
    /// Fully-qualified CL650 dataref name.
    /// e.g. "CL650/systems/fireprot/eng/1/zone/A/fire_ext_1/state"
    /// </summary>
    public string Dataref { get; set; } = string.Empty;

    /// <summary>
    /// Value to write. Typically 1 to trigger, 0 to clear.
    /// </summary>
    public int Value { get; set; }
}
