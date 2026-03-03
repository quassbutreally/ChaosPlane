using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ChaosPlane.Models;

namespace ChaosPlane.Services;

/// <summary>
/// Manages the outbound WebSocket connection from ChaosPlane to the EBS.
/// Receives trigger events from the extension frontend (relayed via the EBS)
/// and feeds them into the FailureOrchestrator.
///
/// Reconnects automatically if the connection drops.
/// </summary>
public class ExtensionService : IAsyncDisposable
{
    private readonly AppSettings _settings;
    private readonly HttpClient  _http = new();

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private bool _connected;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Fired when a tier trigger arrives from the extension.</summary>
    public event Func<FailureTier, string, Task>? TierTriggered;

    /// <summary>Fired when a specific failure trigger arrives (Pick Your Poison).</summary>
    public event Func<string, string, Task>? FailureTriggered;

    /// <summary>Fired when the connection state changes.</summary>
    public event Action<bool>? ConnectionChanged;

    public bool IsConnected => _connected;

    public ExtensionService(AppSettings settings)
    {
        _settings = settings;
    }

    // ── Public: active failure broadcast ─────────────────────────────────────

    /// <summary>
    /// Posts the current active failure IDs to the EBS, which broadcasts
    /// them to all viewers watching the extension panel.
    /// Call this whenever the active failure list changes.
    /// </summary>
    public async Task PublishActiveFailuresAsync(IEnumerable<string> failureIds)
    {
        if (!_connected) return;

        try
        {
            var ebsHttpUrl = _settings.Ebs.Url
                .Replace("wss://", "https://")
                .Replace("ws://",  "http://")
                .Replace("/relay", "");

            var json    = JsonSerializer.Serialize(new { failureIds = failureIds.ToArray() });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            await _http.PostAsync($"{ebsHttpUrl}/active-failures", content);
        }
        catch
        {
            // Non-critical — viewers will just see stale state
        }
    }

    // ── Connection ────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts the connection loop. Connects to the EBS and reconnects
    /// automatically on disconnect. Call once at startup.
    /// </summary>
    public void Start()
    {
        _cts = new CancellationTokenSource();
        _ = RunLoopAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();

        if (_ws != null)
        {
            try
            {
                if (_ws.State == WebSocketState.Open)
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Stopped", CancellationToken.None);
            }
            catch
            {
                // ignored
            }

            _ws.Dispose();
            _ws = null;
        }

        SetConnected(false);
    }

    // ── Private: connection loop ──────────────────────────────────────────────

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _ws = new ClientWebSocket();
                await _ws.ConnectAsync(new Uri(_settings.Ebs.Url), ct);

                SetConnected(true);
                await ReceiveLoopAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Connection failed or dropped — wait before retrying
            }
            finally
            {
                SetConnected(false);
                _ws?.Dispose();
                _ws = null;
            }

            if (!ct.IsCancellationRequested)
                await Task.Delay(TimeSpan.FromSeconds(5), ct).ContinueWith(_ => { });
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer  = new byte[4096];
        var ping    = new Timer(_ =>
        {
            if (_ws?.State == WebSocketState.Open)
                _ = _ws.SendAsync(
                    new ArraySegment<byte>(Array.Empty<byte>()),
                    WebSocketMessageType.Binary,
                    true,
                    CancellationToken.None);
        }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        try
        {
            while (_ws!.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await _ws.ReceiveAsync(buffer, ct);

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                if (result.MessageType != WebSocketMessageType.Text)
                    continue;

                var json    = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var message = JsonSerializer.Deserialize<RelayMessage>(json, JsonOptions);

                if (message?.Type == "trigger" && message.Payload != null)
                    await HandleTriggerAsync(message.Payload);
            }
        }
        finally
        {
            await ping.DisposeAsync();
        }
    }

    private async Task HandleTriggerAsync(TriggerPayload payload)
    {
        // Specific failure requested (Pick Your Poison style)
        if (!string.IsNullOrEmpty(payload.FailureId))
        {
            if (FailureTriggered != null)
                await FailureTriggered.Invoke(payload.FailureId, payload.ViewerName);
            return;
        }

        // Tier-based trigger
        if (!string.IsNullOrEmpty(payload.Tier) &&
            Enum.TryParse<FailureTier>(payload.Tier, ignoreCase: true, out var tier))
        {
            if (TierTriggered != null)
                await TierTriggered.Invoke(tier, payload.ViewerName);
        }
    }

    private void SetConnected(bool connected)
    {
        if (_connected == connected) return;
        _connected = connected;
        App.DispatchToUi(() => ConnectionChanged?.Invoke(connected));
    }

    // ── Message types ─────────────────────────────────────────────────────────

    private class RelayMessage
    {
        public string        Type    { get; set; } = string.Empty;
        public TriggerPayload? Payload { get; set; }
    }

    private class TriggerPayload
    {
        public string? FailureId  { get; set; }
        public string? Tier       { get; set; }
        public int     BitsSpent  { get; set; }
        public string  ViewerName { get; set; } = string.Empty;
    }
    // ── Disposal ──────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}