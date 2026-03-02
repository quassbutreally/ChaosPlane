using ChaosPlane.Models;
using TwitchLib.Api;
using TwitchLib.Api.Core.Enums;
using TwitchLib.Api.Helix.Models.ChannelPoints.CreateCustomReward;
using TwitchLib.Api.Helix.Models.ChannelPoints.UpdateCustomReward;
using TwitchLib.Api.Helix.Models.ChannelPoints.UpdateCustomRewardRedemptionStatus;
using TwitchLib.Client;
using TwitchLib.Client.Models;
using TwitchLib.EventSub.Core.EventArgs.Channel;
using TwitchLib.EventSub.Websockets;

namespace ChaosPlane.Services;

/// <summary>
/// Manages all Twitch integration:
///   - OAuth via embedded browser (implicit grant, no client secret)
///   - Four channel point rewards (Minor, Moderate, Severe, Pick Your Poison)
///   - EventSub WebSocket for real-time redemption events
///   - TwitchLib.Client for chat announcements and refunds
/// </summary>
public class TwitchService : IAsyncDisposable
{
    private static string ClientId => Secrets.TwitchClientId;
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;

    private TwitchAPI?               _api;
    private TwitchClient?            _chatClient;
    private EventSubWebsocketClient? _eventSub;

    private bool _connected;

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired when a tier reward is redeemed.
    /// Args: (tier, viewerDisplayName, redemptionId)
    /// </summary>
    public event Func<FailureTier, string, string, Task>? TierRewardRedeemed;

    /// <summary>
    /// Fired when the Pick Your Poison reward is redeemed.
    /// Args: (userInput, viewerDisplayName, redemptionId)
    /// </summary>
    public event Func<string, string, string, Task>? PickYourPoisonRedeemed;

    /// <summary>
    /// Fired when the connection state changes.
    /// </summary>
    public event Action<bool>? ConnectionChanged;

    // ── State ─────────────────────────────────────────────────────────────────

    public bool IsConnected => _connected;
    public string? ChannelName => _settings.Twitch.ChannelName;

    public TwitchService(AppSettings settings, SettingsService settingsService)
    {
        _settings        = settings;
        _settingsService = settingsService;
    }

    // ── OAuth ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the OAuth authorisation URL to open in the browser.
    /// Scopes required:
    ///   channel:manage:redemptions — create/update/refund rewards
    ///   channel:read:redemptions   — receive redemption events
    ///   chat:edit                  — post chat messages
    /// </summary>
    public string BuildAuthUrl(string redirectUri)
    {
        const string scopes = "channel:manage:redemptions channel:read:redemptions chat:edit chat:read";
        return $"https://id.twitch.tv/oauth2/authorize" +
               $"?client_id={Uri.EscapeDataString(ClientId)}" +
               $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
               $"&response_type=token" +
               $"&scope={Uri.EscapeDataString(scopes)}";
    }

    /// <summary>
    /// Called after the local OAuth listener captures the access token.
    /// Validates the token, stores it, and fetches the broadcaster user ID.
    /// </summary>
    public async Task<bool> ApplyTokenAsync(string accessToken)
    {
        _api = new TwitchAPI();
        _api.Settings.ClientId    = ClientId;
        _api.Settings.AccessToken = accessToken;

        try
        {
            var validation = await _api.Auth.ValidateAccessTokenAsync(accessToken);
            if (validation == null) return false;

            _settings.Twitch.AccessToken       = accessToken;
            _settings.Twitch.ChannelName       = validation.Login;
            _settings.Twitch.BroadcasterUserId = validation.UserId;

            await _settingsService.SaveAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ── Connection ────────────────────────────────────────────────────────────

    /// <summary>
    /// Connects to Twitch using the stored access token.
    /// Initialises the chat client and EventSub WebSocket.
    /// </summary>
    public async Task<bool> ConnectAsync()
    {
        if (string.IsNullOrEmpty(_settings.Twitch.AccessToken))
            return false;

        try
        {
            _api = new TwitchAPI();
            _api.Settings.ClientId    = ClientId;
            _api.Settings.AccessToken = _settings.Twitch.AccessToken;

            // Validate token is still good
            var validation = await _api.Auth.ValidateAccessTokenAsync(_settings.Twitch.AccessToken);
            if (validation == null) return false;

            await ConnectChatAsync();
            await ConnectEventSubAsync();

            _connected = true;
            ConnectionChanged?.Invoke(true);
            return true;
        }
        catch
        {
            _connected = false;
            ConnectionChanged?.Invoke(false);
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        if (_eventSub != null)
        {
            await _eventSub.DisconnectAsync();
            _eventSub = null;
        }

        _chatClient?.Disconnect();
        _chatClient = null;

        _connected = false;
        ConnectionChanged?.Invoke(false);
    }

    // ── Rewards ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates or updates all four ChaosPlane rewards on Twitch.
    /// Persists the reward IDs to settings so we can identify them in EventSub events.
    /// </summary>
    public async Task CreateOrUpdateRewardsAsync()
    {
        if (_api == null || string.IsNullOrEmpty(_settings.Twitch.BroadcasterUserId))
            throw new InvalidOperationException("Must be connected to Twitch before managing rewards.");

        var ids = _settings.Twitch.RewardIds;

        ids.Minor          = await UpsertRewardAsync(ids.Minor,          _settings.Rewards.Minor,          requiresInput: false);
        ids.Moderate       = await UpsertRewardAsync(ids.Moderate,       _settings.Rewards.Moderate,       requiresInput: false);
        ids.Severe         = await UpsertRewardAsync(ids.Severe,         _settings.Rewards.Severe,         requiresInput: false);
        ids.PickYourPoison = await UpsertRewardAsync(ids.PickYourPoison,  _settings.Rewards.PickYourPoison, requiresInput: true);

        await _settingsService.SaveAsync();
    }

    private async Task<string> UpsertRewardAsync(string existingId, RewardConfig config, bool requiresInput)
    {
        var broadcasterId = _settings.Twitch.BroadcasterUserId;

        // Try to update existing reward first
        if (!string.IsNullOrEmpty(existingId))
        {
            try
            {
                var updateRequest = new UpdateCustomRewardRequest
                {
                    Title               = config.Title,
                    Cost                = config.Cost,
                    IsEnabled           = config.Enabled,
                    IsUserInputRequired = requiresInput
                };

                await _api!.Helix.ChannelPoints.UpdateCustomRewardAsync(
                    broadcasterId, existingId, updateRequest);

                return existingId; // Still valid
            }
            catch
            {
                // Reward may have been deleted — fall through to create
            }
        }

        // Try to create new reward
        try
        {
            var createRequest = new CreateCustomRewardsRequest
            {
                Title               = config.Title,
                Cost                = config.Cost,
                IsEnabled           = config.Enabled,
                IsUserInputRequired = requiresInput,
                ShouldRedemptionsSkipRequestQueue = false
            };

            var response = await _api!.Helix.ChannelPoints.CreateCustomRewardsAsync(
                broadcasterId, createRequest);

            return response.Data[0].Id;
        }
        catch
        {
            // Creation failed — likely a duplicate title. Query Twitch for the
            // existing reward by name and recover its ID.
            var existing = await _api!.Helix.ChannelPoints.GetCustomRewardAsync(
                broadcasterId, onlyManageableRewards: true);

            var match = existing.Data.FirstOrDefault(r =>
                r.Title.Equals(config.Title, StringComparison.OrdinalIgnoreCase));

            if (match != null)
                return match.Id;

            throw; // Unknown error — re-throw so the caller knows something went wrong
        }
    }

    // ── Chat ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Posts a chat message announcing that a failure was triggered.
    /// </summary>
    public void AnnounceFailure(TriggeredFailure triggered)
    {
        if (_chatClient == null || !_chatClient.IsConnected) return;
        if (string.IsNullOrEmpty(_settings.Twitch.ChannelName)) return;

        var message = triggered.WasPickYourPoison
            ? $"☠️ @{triggered.RedeemedBy} picked their poison: {triggered.Name}! Good luck! 💀"
            : $"⚠️ @{triggered.RedeemedBy} triggered a {triggered.TierLabel} failure: {triggered.Name}! Good luck! 🔥";

        _chatClient.SendMessage(_settings.Twitch.ChannelName, message);
    }

    /// <summary>
    /// Posts a chat message when a Pick Your Poison input didn't match any failure.
    /// </summary>
    public void AnnounceNoMatch(string viewerDisplayName, string input)
    {
        if (_chatClient == null || !_chatClient.IsConnected) return;
        if (string.IsNullOrEmpty(_settings.Twitch.ChannelName)) return;

        _chatClient.SendMessage(_settings.Twitch.ChannelName,
            $"@{viewerDisplayName} — no matching failure found for \"{input}\". Points refunded!");
    }

    // ── Redemption management ─────────────────────────────────────────────────

    /// <summary>
    /// Marks a redemption as fulfilled (so it doesn't stay in the pending queue).
    /// </summary>
    public async Task FulfillRedemptionAsync(string rewardId, string redemptionId)
    {
        if (_api == null) return;

        try
        {
            await _api.Helix.ChannelPoints.UpdateRedemptionStatusAsync(
                _settings.Twitch.BroadcasterUserId,
                rewardId,
                [redemptionId],
                new UpdateCustomRewardRedemptionStatusRequest
                {
                    Status = CustomRewardRedemptionStatus.FULFILLED
                });
        }
        catch
        {
            // Non-fatal — don't crash the stream over a queue status update
        }
    }

    /// <summary>
    /// Refunds a redemption (returns points to the viewer).
    /// </summary>
    public async Task RefundRedemptionAsync(string rewardId, string redemptionId)
    {
        if (_api == null) return;

        try
        {
            await _api.Helix.ChannelPoints.UpdateRedemptionStatusAsync(
                _settings.Twitch.BroadcasterUserId,
                rewardId,
                [redemptionId],
                new UpdateCustomRewardRedemptionStatusRequest
                {
                    Status = CustomRewardRedemptionStatus.CANCELED
                });
        }
        catch
        {
            // Non-fatal
        }
    }

    // ── Private: Chat client setup ────────────────────────────────────────────

    private Task ConnectChatAsync()
    {
        var credentials = new ConnectionCredentials(
            _settings.Twitch.ChannelName,
            $"oauth:{_settings.Twitch.AccessToken}");

        _chatClient = new TwitchClient();
        _chatClient.Initialize(credentials, _settings.Twitch.ChannelName);
        _chatClient.Connect();

        return Task.CompletedTask;
    }

    // ── Private: EventSub setup ───────────────────────────────────────────────

    private async Task ConnectEventSubAsync()
    {
        _eventSub = new EventSubWebsocketClient();

        _eventSub.WebsocketConnected                     += OnWebsocketConnected;
        _eventSub.WebsocketDisconnected                  += OnWebsocketDisconnected;
        _eventSub.ChannelPointsCustomRewardRedemptionAdd += OnRedemptionAsync;

        await _eventSub.ConnectAsync();
    }

    private async Task OnWebsocketConnected(object sender, TwitchLib.EventSub.Websockets.Core.EventArgs.WebsocketConnectedArgs e)
    {
        if (!e.IsRequestedReconnect)
        {
            // Delete any stale subscriptions for this event type before creating a new one.
            // This prevents duplicate triggers if ConnectAsync is called more than once.
            try
            {
                var existing = await _api!.Helix.EventSub.GetEventSubSubscriptionsAsync(
                    type: "channel.channel_points_custom_reward_redemption.add");

                foreach (var sub in existing.Subscriptions)
                    await _api.Helix.EventSub.DeleteEventSubSubscriptionAsync(sub.Id);
            }
            catch
            {
                // Non-fatal — proceed with creating the subscription regardless
            }

            await _api!.Helix.EventSub.CreateEventSubSubscriptionAsync(
                "channel.channel_points_custom_reward_redemption.add",
                "1",
                new Dictionary<string, string>
                {
                    ["broadcaster_user_id"] = _settings.Twitch.BroadcasterUserId
                },
                TwitchLib.Api.Core.Enums.EventSubTransportMethod.Websocket,
                _eventSub!.SessionId);
        }
    }

    private Task OnWebsocketDisconnected(object sender, EventArgs e)
    {
        _connected = false;
        ConnectionChanged?.Invoke(false);
        return Task.CompletedTask;
    }

    private async Task OnRedemptionAsync(object sender, ChannelPointsCustomRewardRedemptionArgs e)
    {
        var notification = e.Payload.Event;
        var rewardId     = notification.Reward.Id;
        var redemptionId = notification.Id;
        var viewerName   = notification.UserName;
        var userInput    = notification.UserInput ?? string.Empty;
        var ids          = _settings.Twitch.RewardIds;

        if (rewardId == ids.PickYourPoison)
        {
            if (PickYourPoisonRedeemed != null)
                await PickYourPoisonRedeemed.Invoke(userInput.Trim(), viewerName, redemptionId);
        }
        else
        {
            FailureTier? tier = rewardId switch
            {
                _ when rewardId == ids.Minor    => FailureTier.Minor,
                _ when rewardId == ids.Moderate => FailureTier.Moderate,
                _ when rewardId == ids.Severe   => FailureTier.Severe,
                _                               => null
            };

            if (tier.HasValue && TierRewardRedeemed != null)
                await TierRewardRedeemed.Invoke(tier.Value, viewerName, redemptionId);
        }
    }

    // ── Reward ID helpers (used by FailureOrchestrator) ──────────────────────

    public string? GetRewardIdForTier(FailureTier tier) =>
        _settings.Twitch.RewardIds.GetForTier(tier) is { Length: > 0 } id ? id : null;

    public string? GetPickYourPoisonRewardId() =>
        _settings.Twitch.RewardIds.PickYourPoison is { Length: > 0 } id ? id : null;

    // ── Disposal ──────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }
}