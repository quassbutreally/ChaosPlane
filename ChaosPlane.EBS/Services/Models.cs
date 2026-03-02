namespace ChaosPlane.EBS.Services;

// ── Config ────────────────────────────────────────────────────────────────────

public class TwitchConfig
{
    public string ExtensionSecret    { get; set; } = string.Empty;
    public string BroadcasterUserId  { get; set; } = string.Empty;
    public string ExtensionClientId  { get; set; } = string.Empty;
}

// ── Messages between extension frontend → EBS → ChaosPlane ───────────────────

/// <summary>
/// Sent by the extension frontend when a viewer triggers a failure.
/// </summary>
public class TriggerRequest
{
    /// <summary>The failure ID from the catalogue, or null for a random tier pick.</summary>
    public string? FailureId   { get; set; }

    /// <summary>The tier if no specific failure was chosen (Minor/Moderate/Severe).</summary>
    public string? Tier        { get; set; }

    /// <summary>Bits spent — used to validate the transaction.</summary>
    public int     BitsSpent   { get; set; }

    /// <summary>Twitch user ID of the viewer who triggered.</summary>
    public string  ViewerId    { get; set; } = string.Empty;

    /// <summary>Twitch display name of the viewer.</summary>
    public string  ViewerName  { get; set; } = string.Empty;
}

/// <summary>
/// Sent from EBS to ChaosPlane over the WebSocket relay.
/// </summary>
public class RelayMessage
{
    public string        Type    { get; set; } = string.Empty; // "trigger"
    public TriggerRequest? Payload { get; set; }
}