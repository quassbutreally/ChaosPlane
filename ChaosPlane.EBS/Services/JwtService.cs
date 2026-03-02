using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ChaosPlane.EBS.Services;

/// <summary>
/// Verifies JWTs signed by Twitch for the extension.
/// Every request from the extension frontend includes one of these.
/// </summary>
public class JwtService(IOptions<TwitchConfig> config)
{
    private readonly TwitchConfig _config = config.Value;

    /// <summary>
    /// Validates a Twitch extension JWT and returns its claims.
    /// Returns null if the token is invalid or expired.
    /// </summary>
    public ClaimsPrincipal? Verify(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        try
        {
            // Twitch signs extension JWTs with the extension secret (base64-encoded)
            var secretBytes = Convert.FromBase64String(_config.ExtensionSecret);
            var key         = new SymmetricSecurityKey(secretBytes);

            var handler    = new JwtSecurityTokenHandler();
            var parameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey         = key,
                ValidateIssuer           = false, // Twitch doesn't set iss
                ValidateAudience         = false, // Twitch doesn't set aud
                ValidateLifetime         = true,
                ClockSkew                = TimeSpan.FromSeconds(30)
            };

            return handler.ValidateToken(token, parameters, out _);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts the Twitch user ID from a verified claims principal.
    /// </summary>
    public static string? GetUserId(ClaimsPrincipal claims) =>
        claims.FindFirst("user_id")?.Value;

    /// <summary>
    /// Extracts the channel ID (broadcaster) from a verified claims principal.
    /// </summary>
    public static string? GetChannelId(ClaimsPrincipal claims) =>
        claims.FindFirst("channel_id")?.Value;
}