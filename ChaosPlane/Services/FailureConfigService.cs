using System.Text.Json;
using System.Text.Json.Serialization;
using ChaosPlane.Models;

namespace ChaosPlane.Services;

/// <summary>
/// Manages FailureConfig.json — the user's per-failure tier assignments and
/// enabled state. This file lives alongside the exe and is written by the app.
///
/// It is NOT shipped read-only; it starts empty and grows as the user
/// configures failures in the failure browser.
/// </summary>
public class FailureConfigService(string configPath)
{
    private List<FailureConfigEntry> _config = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented           = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling     = JsonCommentHandling.Skip,
        AllowTrailingCommas     = true,
        Converters              = { new JsonStringEnumConverter() }
    };

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Current in-memory config entries.</summary>
    public IReadOnlyList<FailureConfigEntry> Config => _config;

    /// <summary>
    /// Loads FailureConfig.json from disk. If the file doesn't exist, starts
    /// with an empty config (all failures unassigned).
    /// </summary>
    public async Task LoadAsync()
    {
        if (!File.Exists(configPath))
        {
            _config = [];
            return;
        }

        try
        {
            await using var stream = File.OpenRead(configPath);
            var doc = await JsonSerializer.DeserializeAsync<ConfigDocument>(stream, JsonOptions);
            _config = doc?.Entries ?? [];
        }
        catch (JsonException ex)
        {
            // Corrupted config — start fresh rather than crashing
            // In production we'd log this properly
            System.Diagnostics.Debug.WriteLine($"FailureConfig.json parse error: {ex.Message}");
            _config = [];
        }
    }

    /// <summary>
    /// Persists the current in-memory config to disk.
    /// </summary>
    public async Task SaveAsync()
    {
        var doc = new ConfigDocument { Entries = _config };
        var json = JsonSerializer.Serialize(doc, JsonOptions);
        await File.WriteAllTextAsync(configPath, json);
    }

    /// <summary>
    /// Applies a batch of changes from the failure browser UI and saves to disk.
    /// Each item represents the user's current choice for a single failure.
    /// </summary>
    public async Task ApplyAndSaveAsync(IEnumerable<FailureConfigEntry> updatedEntries)
    {
        // Only persist entries where the user has made an explicit choice
        // (i.e. at least Enabled = true OR an assigned tier).
        // Failures with Enabled = false and no tier can be omitted to keep
        // the file lean, but we keep all entries that have ever been touched
        // to preserve the user's deliberate "disabled" choices.
        _config = updatedEntries.ToList();
        await SaveAsync();
    }

    /// <summary>
    /// Gets the config entry for a specific failure ID, or null if unconfigured.
    /// </summary>
    public FailureConfigEntry? GetEntry(string failureId) =>
        _config.FirstOrDefault(e => e.Id.Equals(failureId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Updates or inserts a single config entry in memory (does not save to disk).
    /// Call SaveAsync() when done with a batch of changes.
    /// </summary>
    public void SetEntry(FailureConfigEntry entry)
    {
        var existing = _config.FindIndex(
            e => e.Id.Equals(entry.Id, StringComparison.OrdinalIgnoreCase));

        if (existing >= 0)
            _config[existing] = entry;
        else
            _config.Add(entry);
    }

    // ── Private JSON document type ────────────────────────────────────────────

    private class ConfigDocument
    {
        public List<FailureConfigEntry> Entries { get; set; } = [];
    }
}
