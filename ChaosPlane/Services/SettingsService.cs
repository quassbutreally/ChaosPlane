using System.Text.Json;
using ChaosPlane.Models;

namespace ChaosPlane.Services;

/// <summary>
/// Loads and saves appsettings.json from AppData.
/// On first run (file not found), writes a default settings file using the
/// supplied clientId so the app is functional immediately after a clean build.
/// </summary>
public class SettingsService(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented               = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
        AllowTrailingCommas         = true
    };

    public AppSettings Settings { get; private set; } = new();

    /// <summary>
    /// Loads appsettings.json. If the file doesn't exist, saves defaults
    /// (including the hardcoded Client ID) so it's created for next time.
    /// </summary>
    public async Task LoadAsync()
    {
        if (!File.Exists(path))
        {
            // First run — persist defaults so token/reward IDs can be saved later
            await SaveAsync();
            return;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var loaded = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions);
            if (loaded == null) return;
            
            Settings.Twitch.ChannelName       = loaded.Twitch.ChannelName;
            Settings.Twitch.BroadcasterUserId = loaded.Twitch.BroadcasterUserId;
            Settings.Twitch.AccessToken       = loaded.Twitch.AccessToken;
            Settings.Twitch.RewardIds.Minor          = loaded.Twitch.RewardIds.Minor;
            Settings.Twitch.RewardIds.Moderate       = loaded.Twitch.RewardIds.Moderate;
            Settings.Twitch.RewardIds.Severe         = loaded.Twitch.RewardIds.Severe;
            Settings.Twitch.RewardIds.PickYourPoison = loaded.Twitch.RewardIds.PickYourPoison;

            Settings.XPlane.Host = loaded.XPlane.Host;
            Settings.XPlane.Port = loaded.XPlane.Port;

            Settings.Rewards.Minor.Title        = loaded.Rewards.Minor.Title;
            Settings.Rewards.Minor.Cost         = loaded.Rewards.Minor.Cost;
            Settings.Rewards.Minor.Enabled      = loaded.Rewards.Minor.Enabled;
            Settings.Rewards.Moderate.Title     = loaded.Rewards.Moderate.Title;
            Settings.Rewards.Moderate.Cost      = loaded.Rewards.Moderate.Cost;
            Settings.Rewards.Moderate.Enabled   = loaded.Rewards.Moderate.Enabled;
            Settings.Rewards.Severe.Title       = loaded.Rewards.Severe.Title;
            Settings.Rewards.Severe.Cost        = loaded.Rewards.Severe.Cost;
            Settings.Rewards.Severe.Enabled     = loaded.Rewards.Severe.Enabled;
            Settings.Rewards.PickYourPoison.Title   = loaded.Rewards.PickYourPoison.Title;
            Settings.Rewards.PickYourPoison.Cost    = loaded.Rewards.PickYourPoison.Cost;
            Settings.Rewards.PickYourPoison.Enabled = loaded.Rewards.PickYourPoison.Enabled;

            if (!string.IsNullOrEmpty(loaded.Ebs.Url))
                Settings.Ebs.Url = loaded.Ebs.Url;
        }
        catch (JsonException)
        {
            // Keep defaults on corrupt file
        }
    }

    /// <summary>Persists the current settings to disk.</summary>
    public async Task SaveAsync()
    {
        var json = JsonSerializer.Serialize(Settings, JsonOptions);
        await File.WriteAllTextAsync(path, json);
    }
}