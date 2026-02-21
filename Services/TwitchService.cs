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
public class TwitchService(AppSettings settings, SettingsService settingsService) : IAsyncDisposable
{
    private TwitchAPI?             _api;
    private TwitchClient?          _chatClient;
    private EventSubWebsocketClient? _eventSub;

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

    public bool IsConnected { get; private set; }

    public string ChannelName => settings.Twitch.ChannelName;

    // ── OAuth ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the OAuth authorisation URL to open in the embedded WebView2.
    /// Scopes required:
    ///   channel:manage:redemptions — create/update/refund rewards
    ///   channel:read:redemptions   — receive redemption events
    ///   chat:edit                  — post chat messages
    /// </summary>
    public string BuildAuthUrl(string redirectUri)
    {
        const string scopes = "channel:manage:redemptions channel:read:redemptions chat:edit chat:read";
        return $"https://id.twitch.tv/oauth2/authorize" +
               $"?client_id={Uri.EscapeDataString(settings.Twitch.ClientId)}" +
               $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
               $"&response_type=token" +
               $"&scope={Uri.EscapeDataString(scopes)}";
    }

    /// <summary>
    /// Called after WebView2 captures the access token from the redirect URI fragment.
    /// Validates the token, stores it, and fetches the broadcaster user ID.
    /// </summary>
    public async Task<bool> ApplyTokenAsync(string accessToken)
    {
        _api = new TwitchAPI();
        _api.Settings.ClientId    = settings.Twitch.ClientId;
        _api.Settings.AccessToken = accessToken;

        try
        {
            var validation = await _api.Auth.ValidateAccessTokenAsync(accessToken);
            if (validation == null) return false;

            settings.Twitch.AccessToken        = accessToken;
            settings.Twitch.ChannelName        = validation.Login;
            settings.Twitch.BroadcasterUserId  = validation.UserId;

            await settingsService.SaveAsync();
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
    /// Initializes the chat client and EventSub WebSocket.
    /// </summary>
    public async Task<bool> ConnectAsync()
    {
        if (string.IsNullOrEmpty(settings.Twitch.AccessToken))
            return false;

        try
        {
            _api = new TwitchAPI();
            _api.Settings.ClientId    = settings.Twitch.ClientId;
            _api.Settings.AccessToken = settings.Twitch.AccessToken;

            // Validate token is still good
            var validation = await _api.Auth.ValidateAccessTokenAsync(settings.Twitch.AccessToken);
            if (validation == null) return false;

            await ConnectChatAsync();
            await ConnectEventSubAsync();

            IsConnected = true;
            ConnectionChanged?.Invoke(true);
            return true;
        }
        catch
        {
            IsConnected = false;
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

        IsConnected = false;
        ConnectionChanged?.Invoke(false);
    }

    // ── Rewards ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates or updates all four ChaosPlane rewards on Twitch.
    /// Persists the reward IDs to settings so we can identify them in EventSub events.
    /// </summary>
    public async Task CreateOrUpdateRewardsAsync()
    {
        if (_api == null || string.IsNullOrEmpty(settings.Twitch.BroadcasterUserId))
            throw new InvalidOperationException("Must be connected to Twitch before managing rewards.");

        var ids = settings.Twitch.RewardIds;

        ids.Minor         = await UpsertRewardAsync(ids.Minor,         settings.Rewards.Minor,        requiresInput: false);
        ids.Moderate      = await UpsertRewardAsync(ids.Moderate,      settings.Rewards.Moderate,     requiresInput: false);
        ids.Severe        = await UpsertRewardAsync(ids.Severe,        settings.Rewards.Severe,       requiresInput: false);
        ids.PickYourPoison = await UpsertRewardAsync(ids.PickYourPoison, settings.Rewards.PickYourPoison, requiresInput: true);

        await settingsService.SaveAsync();
    }

    private async Task<string> UpsertRewardAsync(string existingId, RewardConfig config, bool requiresInput)
    {
        var broadcasterId = settings.Twitch.BroadcasterUserId;

        // Try to update existing reward first
        if (!string.IsNullOrEmpty(existingId))
        {
            try
            {
                var updateRequest = new UpdateCustomRewardRequest
                {
                    Title                 = config.Title,
                    Cost                  = config.Cost,
                    IsEnabled             = config.Enabled,
                    IsUserInputRequired   = requiresInput
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

        // Create new reward
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

    // ── Chat ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Posts a chat message announcing that a failure was triggered.
    /// </summary>
    public void AnnounceFailure(TriggeredFailure triggered)
    {
        if (_chatClient is not { IsConnected: true }) return;
        if (string.IsNullOrEmpty(settings.Twitch.ChannelName)) return;

        var message = triggered.WasPickYourPoison
            ? $"☠️ @{triggered.RedeemedBy} picked their poison: {triggered.Name}! Good luck! 💀"
            : $"⚠️ @{triggered.RedeemedBy} triggered a {triggered.TierLabel} failure: {triggered.Name}! Good luck! 🔥";

        _chatClient.SendMessage(settings.Twitch.ChannelName, message);
    }

    /// <summary>
    /// Posts a chat message when a Pick Your Poison input didn't match any failure.
    /// </summary>
    public void AnnounceNoMatch(string viewerDisplayName, string input)
    {
        if (_chatClient == null || !_chatClient.IsConnected) return;
        if (string.IsNullOrEmpty(settings.Twitch.ChannelName)) return;

        _chatClient.SendMessage(settings.Twitch.ChannelName,
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
                settings.Twitch.BroadcasterUserId,
                rewardId,
                [redemptionId],
                new UpdateCustomRewardRedemptionStatusRequest()
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
                settings.Twitch.BroadcasterUserId,
                rewardId,
                [redemptionId],
                new UpdateCustomRewardRedemptionStatusRequest
                {
                    Status = CustomRewardRedemptionStatus.CANCELED
                }
            );
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
            settings.Twitch.ChannelName,
            $"oauth:{settings.Twitch.AccessToken}");

        _chatClient = new TwitchClient();
        _chatClient.Initialize(credentials, settings.Twitch.ChannelName);
        _chatClient.Connect();

        return Task.CompletedTask;
    }

    // ── Private: EventSub setup ───────────────────────────────────────────────

    private async Task ConnectEventSubAsync()
    {
        _eventSub = new EventSubWebsocketClient();

        _eventSub.WebsocketConnected    += OnWebsocketConnected;
        _eventSub.WebsocketDisconnected += OnWebsocketDisconnected;
        _eventSub.ChannelPointsCustomRewardRedemptionAdd += OnRedemptionAsync;

        await _eventSub.ConnectAsync();
    }

    private async Task OnWebsocketConnected(object? sender, TwitchLib.EventSub.Websockets.Core.EventArgs.WebsocketConnectedArgs e)
    {
        if (!e.IsRequestedReconnect)
        {
            // Subscribe to redemption events for our broadcaster
            await _api!.Helix.EventSub.CreateEventSubSubscriptionAsync(
                "channel.channel_points_custom_reward_redemption.add",
                "1",
                new Dictionary<string, string>
                {
                    ["broadcaster_user_id"] = settings.Twitch.BroadcasterUserId
                },
                EventSubTransportMethod.Websocket,
                _eventSub?.SessionId);
        }
    }

    private Task OnWebsocketDisconnected(object? sender, EventArgs e)
    {
        IsConnected = false;
        ConnectionChanged?.Invoke(false);
        return Task.CompletedTask;
    }

    private async Task OnRedemptionAsync(object? sender, ChannelPointsCustomRewardRedemptionArgs e)
    {
        var notification  = e.Payload.Event;
        var rewardId      = notification.Reward.Id;
        var redemptionId  = notification.Id;
        var viewerName    = notification.UserName;
        var userInput     = notification.UserInput;
        var ids           = settings.Twitch.RewardIds;

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
        settings.Twitch.RewardIds.GetForTier(tier) is { Length: > 0 } id ? id : null;

    public string? GetPickYourPoisonRewardId() =>
        settings.Twitch.RewardIds.PickYourPoison is { Length: > 0 } id ? id : null;

    // ── Disposal ──────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }
}
