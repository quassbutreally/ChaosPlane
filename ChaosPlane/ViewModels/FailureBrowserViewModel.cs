using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChaosPlane.Models;
using ChaosPlane.Services;

namespace ChaosPlane.ViewModels;

/// <summary>
/// ViewModel for the failure browser / configuration screen.
///
/// The user can:
///   - Browse all catalogue failures, grouped by ATA category
///   - Filter by name or category
///   - Enable/disable individual failures
///   - Assign a tier (or accept the suggested tier)
///   - Save changes back to FailureConfig.json
/// </summary>
public partial class FailureBrowserViewModel : ObservableObject
{
    private readonly CatalogueService     _catalogueService;
    private readonly FailureConfigService _configService;

    // Full unfiltered list of editable items (rebuilt on Refresh)
    private List<FailureRowViewModel> _allRows = [];

    // ── Observable collections ────────────────────────────────────────────────

    /// <summary>The filtered, currently displayed rows.</summary>
    public ObservableCollection<FailureRowViewModel> Rows { get; } = [];

    /// <summary>All unique category names, for the filter dropdown.</summary>
    public ObservableCollection<string> Categories { get; } = [];

    // ── Filter state ──────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredCount))]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredCount))]
    private string _selectedCategory = "All";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredCount))]
    private bool _showEnabledOnly;

    public int FilteredCount => Rows.Count;
    public int TotalCount    => _allRows.Count;

    // ── Status ────────────────────────────────────────────────────────────────

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private string? _saveStatus;

    [ObservableProperty]
    private bool _isSaving;

    // ── Constructor ───────────────────────────────────────────────────────────

    public FailureBrowserViewModel(
        CatalogueService     catalogueService,
        FailureConfigService configService)
    {
        _catalogueService = catalogueService;
        _configService    = configService;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds rows from the current catalogue + config state.
    /// Call after catalogue load or config save.
    /// </summary>
    public void Refresh()
    {
        _allRows = _catalogueService.All
            .Select(f => new FailureRowViewModel(f, OnRowChanged))
            .ToList();

        RebuildCategories();
        ApplyFilter();

        HasUnsavedChanges = false;
        SaveStatus = null;
        OnPropertyChanged(nameof(TotalCount));
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void ApplyFilter()
    {
        var filtered = _allRows.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
            filtered = filtered.Where(r =>
                r.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                r.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        if (SelectedCategory != "All")
            filtered = filtered.Where(r => r.Category == SelectedCategory);

        if (ShowEnabledOnly)
            filtered = filtered.Where(r => r.IsEnabled);

        Rows.Clear();
        foreach (var row in filtered)
            Rows.Add(row);

        OnPropertyChanged(nameof(FilteredCount));
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        IsSaving = true;
        SaveStatus = null;

        try
        {
            var entries = _allRows
                .Where(r => r.IsEnabled || r.AssignedTier.HasValue)
                .Select(r => new FailureConfigEntry
                {
                    Id      = r.Id,
                    Enabled = r.IsEnabled,
                    Tier    = r.AssignedTier
                })
                .ToList();

            await _configService.ApplyAndSaveAsync(entries);

            // Re-merge catalogue with new config
            _catalogueService.Refresh();

            HasUnsavedChanges = false;
            SaveStatus = $"Saved {entries.Count} configured failures.";
        }
        catch (Exception ex)
        {
            SaveStatus = $"Save failed: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void AcceptAllSuggestedTiers()
    {
        foreach (var row in _allRows.Where(r => r.AssignedTier == null && r.SuggestedTier != null))
            row.AssignedTier = row.SuggestedTier!.Value;

        HasUnsavedChanges = true;
    }

    [RelayCommand]
    private void EnableAll()
    {
        foreach (var row in Rows)
            row.IsEnabled = true;
        HasUnsavedChanges = true;
    }

    [RelayCommand]
    private void DisableAll()
    {
        foreach (var row in Rows)
            row.IsEnabled = false;
        HasUnsavedChanges = true;
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText       = string.Empty;
        SelectedCategory = "All";
        ShowEnabledOnly  = false;
        ApplyFilter();
    }

    // ── Partial property change hooks ─────────────────────────────────────────

    partial void OnSearchTextChanged(string value)       => ApplyFilter();
    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();
    partial void OnShowEnabledOnlyChanged(bool value)    => ApplyFilter();

    // ── Private helpers ───────────────────────────────────────────────────────

    private void RebuildCategories()
    {
        Categories.Clear();
        Categories.Add("All");

        foreach (var cat in _allRows.Select(r => r.Category).Distinct().Order())
            Categories.Add(cat);
    }

    private void OnRowChanged()
    {
        HasUnsavedChanges = true;
    }
}

/// <summary>
/// A single editable row in the failure browser.
/// Wraps a ResolvedFailure and tracks pending changes.
/// </summary>
public partial class FailureRowViewModel : ObservableObject
{
    private readonly Action _onChanged;

    public FailureRowViewModel(ResolvedFailure failure, Action onChanged)
    {
        _onChanged    = onChanged;
        Id            = failure.Id;
        Name          = failure.Name;
        Description   = failure.Description;
        Category      = failure.Category;
        SuggestedTier = failure.SuggestedTier;

        // Initialise from current config state
        _isEnabled    = failure.Enabled;
        _assignedTier = failure.AssignedTier;
    }

    // ── Display properties (read-only) ────────────────────────────────────────

    public string       Id            { get; }
    public string       Name          { get; }
    public string       Description   { get; }
    public string       Category      { get; }
    public FailureTier? SuggestedTier { get; }

    /// <summary>Tier options for the assigned tier ComboBox.</summary>
    public IList<FailureTier?> TierOptions => _tierOptions;

    private static readonly IList<FailureTier?> _tierOptions =
    [
        null,
        FailureTier.Minor,
        FailureTier.Moderate,
        FailureTier.Severe
    ];

    // ── Editable properties ───────────────────────────────────────────────────

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private FailureTier? _assignedTier;

    // ── Computed display helpers ──────────────────────────────────────────────

    /// <summary>The effective tier to display — assigned if set, else suggested.</summary>
    public FailureTier? DisplayTier => AssignedTier ?? SuggestedTier;

    /// <summary>Whether this failure will actually fire when redeemed.</summary>
    public bool IsTriggerable => IsEnabled && AssignedTier.HasValue;

    // ── Change notification ───────────────────────────────────────────────────

    partial void OnIsEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(IsTriggerable));
        _onChanged();
    }

    partial void OnAssignedTierChanged(FailureTier? value)
    {
        OnPropertyChanged(nameof(DisplayTier));
        OnPropertyChanged(nameof(IsTriggerable));
        _onChanged();
    }
}