using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ChaosPlane.EBS.Services;

/// <summary>
/// Manages the persistent WebSocket connection from the ChaosPlane desktop app.
/// Receives trigger requests from the extension frontend and forwards them to
/// ChaosPlane over the WebSocket.
///
/// Only one desktop connection is expected at a time (single streamer tool).
/// </summary>
public class RelayService(ILogger<RelayService> logger)
{
    private WebSocket? _desktopSocket;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // ── Desktop connection ────────────────────────────────────────────────────

    /// <summary>
    /// Called when ChaosPlane connects. Holds the WebSocket open until the
    /// desktop app disconnects or the connection drops.
    /// </summary>
    public async Task HandleDesktopConnectionAsync(WebSocket ws)
    {
        logger.LogInformation("ChaosPlane desktop connected");
        _desktopSocket = ws;

        var buffer = new byte[1024];

        try
        {
            // Keep alive — read until the socket closes
            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(buffer, CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Close)
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("Desktop WebSocket error: {Message}", ex.Message);
        }
        finally
        {
            _desktopSocket = null;
            logger.LogInformation("ChaosPlane desktop disconnected");

            if (ws.State == WebSocketState.Open)
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None);
        }
    }

    // ── Trigger relay ─────────────────────────────────────────────────────────

    /// <summary>
    /// Forwards a trigger request to the ChaosPlane desktop app.
    /// Returns false if ChaosPlane is not connected.
    /// </summary>
    public async Task<bool> SendTriggerAsync(TriggerRequest request)
    {
        if (_desktopSocket == null || _desktopSocket.State != WebSocketState.Open)
        {
            logger.LogWarning("Trigger received but ChaosPlane is not connected");
            return false;
        }

        var message = new RelayMessage { Type = "trigger", Payload = request };
        var json    = JsonSerializer.Serialize(message, JsonOptions);
        var bytes   = Encoding.UTF8.GetBytes(json);

        await _sendLock.WaitAsync();
        try
        {
            await _desktopSocket.SendAsync(
                bytes,
                WebSocketMessageType.Text,
                endOfMessage: true,
                CancellationToken.None);

            logger.LogInformation("Trigger relayed: {FailureId} / {Tier} from {Viewer}",
                request.FailureId, request.Tier, request.ViewerName);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError("Failed to relay trigger: {Message}", ex.Message);
            return false;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// Whether the ChaosPlane desktop app is currently connected.
    /// </summary>
    public bool IsDesktopConnected =>
        _desktopSocket?.State == WebSocketState.Open;
}