using System.Text.Json;
using ChaosPlane.Models;

namespace ChaosPlane.Services;

/// <summary>
/// Loads the shipped FailureCatalogue.json and merges it with the user's
/// FailureConfig.json to produce a list of ResolvedFailures.
///
/// This is the single source of truth for what failures exist and whether
/// they are triggerable.
/// </summary>
public class CatalogueService
{
    private readonly FailureConfigService _configService;

    private List<CatalogueFailure> _catalogue = [];
    private List<ResolvedFailure>  _resolved  = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
        AllowTrailingCommas         = true
    };

    public CatalogueService(FailureConfigService configService)
    {
        _configService = configService;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>All resolved failures (catalogue merged with config).</summary>
    public IReadOnlyList<ResolvedFailure> All => _resolved;

    /// <summary>Only failures that are currently triggerable (Enabled + tier assigned).</summary>
    public IReadOnlyList<ResolvedFailure> Triggerable =>
        _resolved.Where(f => f.IsTriggerable).ToList();

    /// <summary>Triggerable failures filtered to a specific tier.</summary>
    public IReadOnlyList<ResolvedFailure> ForTier(FailureTier tier) =>
        _resolved.Where(f => f.IsTriggerable && f.EffectiveTier == tier).ToList();

    /// <summary>
    /// Loads the catalogue from disk and merges with current config.
    /// Call once at startup; call again after config is saved to refresh.
    /// </summary>
    public async Task LoadAsync(string cataloguePath)
    {
        await LoadCatalogueAsync(cataloguePath);
        Merge();
    }

    /// <summary>
    /// Re-merges catalogue with the latest config without reloading the
    /// catalogue file. Call after the user saves changes in the failure browser.
    /// </summary>
    public void Refresh()
    {
        Merge();
    }

    /// <summary>
    /// Looks up a resolved failure by ID. Returns null if not found.
    /// </summary>
    public ResolvedFailure? FindById(string id) =>
        _resolved.FirstOrDefault(f => f.Id == id);

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task LoadCatalogueAsync(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"FailureCatalogue.json not found at: {path}");

        await using var stream = File.OpenRead(path);

        var doc = await JsonSerializer.DeserializeAsync<CatalogueDocument>(stream, JsonOptions)
                  ?? throw new InvalidDataException("FailureCatalogue.json deserialised to null.");

        _catalogue = doc.Failures;
    }

    private void Merge()
    {
        var config = _configService.Config;

        // Build lookup of config entries by failure ID for O(1) access
        var configById = config
            .ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);

        _resolved = _catalogue.Select(catalogueFailure =>
        {
            configById.TryGetValue(catalogueFailure.Id, out var entry);

            return new ResolvedFailure
            {
                Id            = catalogueFailure.Id,
                Name          = catalogueFailure.Name,
                Description   = catalogueFailure.Description,
                Category      = catalogueFailure.Category,
                SuggestedTier = catalogueFailure.SuggestedTier,
                Actions       = catalogueFailure.Actions,

                // Config-derived — null/false if no config entry
                Enabled      = entry?.Enabled ?? false,
                AssignedTier = entry?.Tier     // null if not configured
            };
        }).ToList();
    }

    // ── Private JSON document type ────────────────────────────────────────────

    private class CatalogueDocument
    {
        public List<CatalogueFailure> Failures { get; set; } = [];
    }
}
