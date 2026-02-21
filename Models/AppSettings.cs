namespace ChaosPlane.Models;

public class AppSettings
{
    public TwitchSettings  Twitch  { get; set; } = new();
    public XPlaneSettings  XPlane  { get; set; } = new();
    public RewardsSettings Rewards { get; set; } = new();
}

public class TwitchSettings
{
    /// <summary>
    /// Your Twitch application Client ID from dev.twitch.tv.
    /// Must be set before OAuth will work.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    public string ChannelName      { get; set; } = string.Empty;
    public string BroadcasterUserId { get; set; } = string.Empty;

    /// <summary>
    /// OAuth access token stored after first successful login.
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Twitch reward IDs created by the app, keyed by tier.
    /// Persisted so we can identify our rewards across sessions.
    /// </summary>
    public TwitchRewardIds RewardIds { get; set; } = new();
}

public class TwitchRewardIds
{
    public string Minor        { get; set; } = string.Empty;
    public string Moderate     { get; set; } = string.Empty;
    public string Severe       { get; set; } = string.Empty;
    public string PickYourPoison { get; set; } = string.Empty;

    public string? GetForTier(FailureTier tier) => tier switch
    {
        FailureTier.Minor    => Minor,
        FailureTier.Moderate => Moderate,
        FailureTier.Severe   => Severe,
        _                    => null
    };
}

public class XPlaneSettings
{
    public string Host { get; set; } = "localhost";
    public int    Port { get; set; } = 8086;

    public string BaseUrl => $"http://{Host}:{Port}/api/v3";
}

public class RewardsSettings
{
    public RewardConfig Minor        { get; set; } = new() { Title = "🟢 Minor Failure",    Cost = 5000   };
    public RewardConfig Moderate     { get; set; } = new() { Title = "🟡 Moderate Failure", Cost = 10000  };
    public RewardConfig Severe       { get; set; } = new() { Title = "🔴 Severe Failure",   Cost = 20000  };
    public RewardConfig PickYourPoison { get; set; } = new() { Title = "☠️ Pick Your Poison", Cost = 50000 };

    public RewardConfig GetForTier(FailureTier tier) => tier switch
    {
        FailureTier.Minor    => Minor,
        FailureTier.Moderate => Moderate,
        FailureTier.Severe   => Severe,
        _                    => throw new ArgumentOutOfRangeException(nameof(tier))
    };
}

public class RewardConfig
{
    public string Title   { get; set; } = string.Empty;
    public int    Cost    { get; set; }
    public bool   Enabled { get; set; } = true;
}
