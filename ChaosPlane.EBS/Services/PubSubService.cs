using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ChaosPlane.EBS.Services;

/// <summary>
/// Broadcasts messages to the Twitch Extension frontend via the
/// Twitch Extension PubSub API.
///
/// Used to push active failure state to all viewers watching the panel.
/// </summary>
public class PubSubService(IOptions<TwitchConfig> config, ILogger<PubSubService> logger)
{
    private readonly TwitchConfig              _config = config.Value;
    private readonly HttpClient                _http = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Broadcasts the current set of active failure IDs to all viewers
    /// watching the extension panel.
    /// </summary>
    public async Task BroadcastActiveFailuresAsync(IEnumerable<string> failureIds)
    {
        var message = JsonSerializer.Serialize(new
        {
            type       = "active_failures",
            failureIds = failureIds.ToArray()
        });

        await SendPubSubMessageAsync("broadcast", message);
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private async Task SendPubSubMessageAsync(string target, string message)
    {
        try
        {
            var jwt     = BuildServerJwt();

            var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://api.twitch.tv/helix/extensions/pubsub")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        target       = new[] { "broadcast" },
                        broadcaster_id = _config.BroadcasterUserId,
                        is_global_broadcast = false,
                        message
                    }, JsonOptions),
                    Encoding.UTF8,
                    "application/json")
            };

            request.Headers.Add("Authorization", $"Bearer {jwt}");
            request.Headers.Add("Client-Id", _config.ExtensionClientId);

            var response = await _http.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                logger.LogWarning("PubSub broadcast failed: {Status} {Body}",
                    response.StatusCode, body);
            }
            else
            {
                logger.LogInformation("PubSub broadcast sent: {Target}", target);
            }
        }
        catch (Exception ex)
        {
            logger.LogError("PubSub error: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// Builds a short-lived server-side JWT signed with the extension secret.
    /// Required for calling Twitch extension APIs from the EBS.
    /// </summary>
    private string BuildServerJwt()
    {
        var secretBytes = Convert.FromBase64String(_config.ExtensionSecret);
        var key         = new SymmetricSecurityKey(secretBytes);
        var creds       = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var exp = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds();

        var payload = new JwtPayload
        {
            { "exp",     exp },
            { "user_id", _config.BroadcasterUserId },
            { "role",    "external" },
            { "pubsub_perms", new Dictionary<string, object>
                {
                    { "send", new[] { "broadcast" } }
                }
            }
        };

        var header  = new JwtHeader(creds);
        var token   = new JwtSecurityToken(header, payload);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}