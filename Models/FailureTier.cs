using System.Text.Json.Serialization;

namespace ChaosPlane.Models;

/// <summary>
/// Severity tier of a failure. Determines which Twitch reward pool it belongs to.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FailureTier
{
    Minor,
    Moderate,
    Severe
}
