using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChaosPlane.Models;

namespace ChaosPlane.Services;

/// <summary>
/// Communicates with the X-Plane 12 local REST API (http://localhost:8086/api/v3).
///
/// Workflow for triggering a dataref:
///   1. GET /datarefs?filter[name]=CL650/... → resolve name to a numeric session ID
///   2. PATCH /datarefs/{id}/value → write the value
///
/// Dataref IDs are session-ephemeral (they change on every sim restart), so we
/// maintain a name→ID cache that is cleared on disconnect.
/// </summary>
public class XPlaneService
{
    private readonly HttpClient _http;

    // name → numeric ID cache, valid for the current sim session only
    private readonly Dictionary<string, long> _idCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public XPlaneService(HttpClient http)
    {
        _http         = http;
        _http.Timeout = TimeSpan.FromSeconds(5);
    }

    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(30);

    private string _baseUrl = "http://localhost:8086/api/v3";
    private bool   _connected;
    private Timer? _pingTimer;

    // ── Events ────────────────────────────────────────────────────────────────

    public event Action<bool>? ConnectionChanged;

    // ── Connection state ──────────────────────────────────────────────────────

    public bool IsConnected => _connected;

    /// <summary>
    /// Configures the base URL from settings, does an immediate ping,
    /// and starts a background timer to ping every 30 seconds.
    /// </summary>
    public async Task<bool> ConnectAsync(XPlaneSettings settings)
    {
        _baseUrl = settings.BaseUrl;
        _idCache.Clear();

        // Start (or restart) the periodic ping timer
        _pingTimer?.Dispose();
        _pingTimer = new Timer(
            _ => _ = PingAsync(),
            null,
            PingInterval,
            PingInterval);

        // Immediate ping so the result is available right away
        return await PingAsync();
    }

    /// <summary>
    /// Pings X-Plane with a lightweight dataref request.
    /// Updates IsConnected and fires ConnectionChanged if state changes.
    /// </summary>
    public async Task<bool> PingAsync()
    {
        bool ok;
        try
        {
            var response = await _http.GetAsync($"{_baseUrl}/datarefs?limit=1");
            ok = response.IsSuccessStatusCode;
        }
        catch
        {
            ok = false;
        }

        if (ok != _connected)
        {
            _connected = ok;
            ConnectionChanged?.Invoke(_connected);
        }

        return _connected;
    }

    /// <summary>
    /// Stops the ping timer and marks as disconnected.
    /// </summary>
    public void Disconnect()
    {
        _pingTimer?.Dispose();
        _pingTimer = null;

        if (_connected)
        {
            _connected = false;
            ConnectionChanged?.Invoke(false);
        }

        _idCache.Clear();
    }

    // ── Failure triggering ────────────────────────────────────────────────────

    /// <summary>
    /// Triggers a failure by writing value = 1 to each of its datarefs.
    /// </summary>
    public async Task TriggerAsync(ResolvedFailure failure)
    {
        foreach (var action in failure.Actions)
            await WriteDatarefAsync(action.Dataref, action.Value);
    }

    /// <summary>
    /// Resets a failure by writing value = 0 to each of its datarefs.
    /// </summary>
    public async Task ResetAsync(ResolvedFailure failure)
    {
        foreach (var action in failure.Actions)
            await WriteDatarefAsync(action.Dataref, 0);
    }

    // ── Low-level dataref access ──────────────────────────────────────────────

    /// <summary>
    /// Resolves a dataref name to its session ID (cached), then PATCHes the value.
    /// Throws XPlaneException if X-Plane is unreachable or the dataref is unknown.
    /// </summary>
    public async Task WriteDatarefAsync(string datarefName, int value)
    {
        var id = await ResolveIdAsync(datarefName);
        await PatchValueAsync(id, value);
    }

    /// <summary>
    /// Resolves a dataref name → numeric ID, using the cache where possible.
    /// </summary>
    private async Task<long> ResolveIdAsync(string datarefName)
    {
        if (_idCache.TryGetValue(datarefName, out var cachedId))
            return cachedId;

        var url = $"{_baseUrl}/datarefs?filter[name]={Uri.EscapeDataString(datarefName)}";

        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(url);
        }
        catch (Exception ex)
        {
            throw new XPlaneException($"Failed to reach X-Plane at {url}", ex);
        }

        if (!response.IsSuccessStatusCode)
            throw new XPlaneException(
                $"X-Plane returned {(int)response.StatusCode} looking up dataref '{datarefName}'");

        var result = await response.Content.ReadFromJsonAsync<DatarefListResponse>(JsonOptions)
                     ?? throw new XPlaneException($"Empty response looking up dataref '{datarefName}'");

        var entry = result.Data?.FirstOrDefault(d =>
            d.Name.Equals(datarefName, StringComparison.OrdinalIgnoreCase) == true);

        if (entry == null)
            throw new XPlaneException($"Dataref not found in X-Plane: '{datarefName}'");

        _idCache[datarefName] = entry.Id;
        return entry.Id;
    }

    /// <summary>
    /// PATCHes a dataref value by numeric ID.
    /// </summary>
    private async Task PatchValueAsync(long id, int value)
    {
        var url     = $"{_baseUrl}/datarefs/{id}/value";
        var payload = new { data = value };

        HttpResponseMessage response;
        try
        {
            response = await _http.PatchAsJsonAsync(url, payload, JsonOptions);
        }
        catch (Exception ex)
        {
            throw new XPlaneException($"Failed to PATCH dataref ID {id}", ex);
        }

        if (!response.IsSuccessStatusCode)
            throw new XPlaneException(
                $"X-Plane returned {(int)response.StatusCode} PATCHing dataref ID {id}");
    }

    // ── JSON shapes ───────────────────────────────────────────────────────────

    private class DatarefListResponse
    {
        [JsonPropertyName("data")]
        public List<DatarefEntry>? Data { get; set; }
    }

    private class DatarefEntry
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("is_writable")]
        public bool IsWritable { get; set; }
        
        [JsonPropertyName("name")]
        public string Name { get; set; }
        
        [JsonPropertyName("value_type")]
        public string ValueType { get; set; }
    }

}

/// <summary>
/// Thrown when an X-Plane API call fails.
/// </summary>
public class XPlaneException : Exception
{
    public XPlaneException(string message) : base(message) { }
    public XPlaneException(string message, Exception inner) : base(message, inner) { }
}