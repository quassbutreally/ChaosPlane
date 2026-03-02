using ChaosPlane.Models;

namespace ChaosPlane.Services;

/// <summary>
/// Coordinates failure triggering between Twitch and X-Plane.
///
/// Responsibilities:
///   - Random failure selection from a tier pool (for tier rewards)
///   - Fuzzy name matching (for Pick Your Poison)
///   - Calling XPlaneService to write datarefs
///   - Calling TwitchService to fulfil/refund redemptions and post chat
///   - Raising events that the dashboard ViewModel subscribes to
/// </summary>
public class FailureOrchestrator
{
    private readonly CatalogueService _catalogue;
    private readonly XPlaneService    _xplane;
    private readonly TwitchService    _twitch;
    private readonly ExtensionService _extension;

    private readonly Random _random = new();

    // Minimum fuzzy match score (0–1) to consider a Pick Your Poison input a valid match
    private const double FuzzyThreshold = 0.35;

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Fired after a failure is successfully triggered in X-Plane.</summary>
    public event Action<TriggeredFailure>? FailureTriggered;

    /// <summary>Fired after a failure is reset in X-Plane.</summary>
    public event Action<TriggeredFailure>? FailureReset;

    /// <summary>Fired when a Pick Your Poison input matched nothing (points refunded).</summary>
    public event Action<string, string>? PickYourPoisonNoMatch; // (viewerName, input)

    public FailureOrchestrator(
        CatalogueService catalogue,
        XPlaneService    xplane,
        TwitchService    twitch,
        ExtensionService extension)
    {
        _catalogue = catalogue;
        _xplane    = xplane;
        _twitch    = twitch;
        _extension = extension;

        // Wire up Twitch events
        _twitch.TierRewardRedeemed     += OnTierRewardRedeemedAsync;
        _twitch.PickYourPoisonRedeemed += OnPickYourPoisonRedeemedAsync;

        // Wire up Extension events
        _extension.TierTriggered    += OnExtensionTierTriggeredAsync;
        _extension.FailureTriggered += OnExtensionFailureTriggeredAsync;
    }

    // ── Public: catalogue access for UI ──────────────────────────────────────

    /// <summary>Returns all currently triggerable failures (for manual trigger search).</summary>
    public IReadOnlyList<ResolvedFailure> GetTriggerableFailures() =>
        _catalogue.Triggerable;

    // ── Public: manual trigger (from UI) ─────────────────────────────────────

    /// <summary>
    /// Triggers a specific failure directly (e.g. from a test button in the UI).
    /// Does not interact with Twitch.
    /// </summary>
    public async Task<bool> TriggerAsync(ResolvedFailure failure, string triggeredBy = "Host")
    {
        try
        {
            await _xplane.TriggerAsync(failure);
        }
        catch (XPlaneException)
        {
            return false;
        }

        var triggered = new TriggeredFailure
        {
            Failure           = failure,
            RedeemedBy        = triggeredBy,
            WasPickYourPoison = false
        };

        FailureTriggered?.Invoke(triggered);
        return true;
    }

    /// <summary>
    /// Resets a triggered failure in X-Plane.
    /// </summary>
    public async Task ResetAsync(TriggeredFailure triggered)
    {
        await _xplane.ResetAsync(triggered.Failure);
        triggered.IsActive = false;
        FailureReset?.Invoke(triggered);
    }

    // ── Private: Twitch event handlers ───────────────────────────────────────

    private async Task OnTierRewardRedeemedAsync(FailureTier tier, string viewerName, string redemptionId)
    {
        var pool = _catalogue.ForTier(tier);

        if (pool.Count == 0)
        {
            var rewardId = _twitch.IsConnected ? GetRewardIdForTier(tier) : null;
            if (rewardId != null)
                await _twitch.RefundRedemptionAsync(rewardId, redemptionId);
            return;
        }

        var failure = pool[_random.Next(pool.Count)];

        try
        {
            await _xplane.TriggerAsync(failure);
        }
        catch (XPlaneException)
        {
            // X-Plane unreachable — refund the redemption
            var rewardId = GetRewardIdForTier(tier);
            if (rewardId != null)
                await _twitch.RefundRedemptionAsync(rewardId, redemptionId);
            _twitch.AnnounceNoMatch(viewerName, $"[X-Plane unreachable — {failure.Name} refunded]");
            return;
        }

        var triggered = new TriggeredFailure
        {
            Failure           = failure,
            RedeemedBy        = viewerName,
            WasPickYourPoison = false
        };

        var rid = GetRewardIdForTier(tier);
        if (rid != null)
            await _twitch.FulfillRedemptionAsync(rid, redemptionId);

        _twitch.AnnounceFailure(triggered);
        FailureTriggered?.Invoke(triggered);
    }

    private async Task OnPickYourPoisonRedeemedAsync(string userInput, string viewerName, string redemptionId)
    {
        var pickYourPoisonRewardId = GetPickYourPoisonRewardId();

        if (string.IsNullOrWhiteSpace(userInput))
        {
            await RefundPickYourPoison(viewerName, userInput, redemptionId, pickYourPoisonRewardId);
            return;
        }

        var match = FuzzyMatch(userInput, _catalogue.All);

        if (match == null)
        {
            await RefundPickYourPoison(viewerName, userInput, redemptionId, pickYourPoisonRewardId);
            return;
        }

        try
        {
            await _xplane.TriggerAsync(match);
        }
        catch (XPlaneException)
        {
            await RefundPickYourPoison(viewerName, userInput, redemptionId, pickYourPoisonRewardId);
            return;
        }

        var triggered = new TriggeredFailure
        {
            Failure           = match,
            RedeemedBy        = viewerName,
            WasPickYourPoison = true
        };

        if (pickYourPoisonRewardId != null)
            await _twitch.FulfillRedemptionAsync(pickYourPoisonRewardId, redemptionId);

        _twitch.AnnounceFailure(triggered);
        FailureTriggered?.Invoke(triggered);
    }

    private async Task RefundPickYourPoison(
        string viewerName, string input, string redemptionId, string? rewardId)
    {
        if (rewardId != null)
            await _twitch.RefundRedemptionAsync(rewardId, redemptionId);

        _twitch.AnnounceNoMatch(viewerName, input);
        PickYourPoisonNoMatch?.Invoke(viewerName, input);
    }

    // ── Extension event handlers ──────────────────────────────────────────────

    private async Task OnExtensionTierTriggeredAsync(FailureTier tier, string viewerName)
    {
        var pool = _catalogue.ForTier(tier);
        if (pool.Count == 0) return;

        var failure = pool[_random.Next(pool.Count)];

        try
        {
            await _xplane.TriggerAsync(failure);
        }
        catch (XPlaneException)
        {
            _twitch.AnnounceNoMatch(viewerName, $"[X-Plane unreachable — {failure.Name} not triggered]");
            return;
        }

        var triggered = new TriggeredFailure
        {
            Failure           = failure,
            RedeemedBy        = viewerName,
            WasPickYourPoison = false
        };

        _twitch.AnnounceFailure(triggered);
        FailureTriggered?.Invoke(triggered);
    }

    private async Task OnExtensionFailureTriggeredAsync(string failureId, string viewerName)
    {
        var failure = _catalogue.FindById(failureId);

        if (failure == null)
        {
            _twitch.AnnounceNoMatch(viewerName, failureId);
            PickYourPoisonNoMatch?.Invoke(viewerName, failureId);
            return;
        }

        try
        {
            await _xplane.TriggerAsync(failure);
        }
        catch (XPlaneException)
        {
            _twitch.AnnounceNoMatch(viewerName, $"[X-Plane unreachable — {failure.Name} not triggered]");
            return;
        }

        var triggered = new TriggeredFailure
        {
            Failure           = failure,
            RedeemedBy        = viewerName,
            WasPickYourPoison = true
        };

        _twitch.AnnounceFailure(triggered);
        FailureTriggered?.Invoke(triggered);
    }

    // ── Fuzzy matching ────────────────────────────────────────────────────────

    /// <summary>
    /// Finds the best-matching triggerable failure for a given input string.
    /// Returns null if no match exceeds the threshold.
    ///
    /// Matching strategy:
    ///   1. Exact match (case-insensitive)
    ///   2. Contains match (input is a substring of the name)
    ///   3. Bigram similarity (Dice coefficient) — handles typos and partial names
    /// </summary>
    private ResolvedFailure? FuzzyMatch(string input, IReadOnlyList<ResolvedFailure> pool)
    {
        if (pool.Count == 0) return null;

        var normalised = input.Trim().ToLowerInvariant();

        // 1. Exact match
        var exact = pool.FirstOrDefault(
            f => f.Name.Equals(normalised, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        // 2. Contains match
        var contains = pool.FirstOrDefault(
            f => f.Name.Contains(normalised, StringComparison.OrdinalIgnoreCase));
        if (contains != null) return contains;

        // 3. Bigram (Dice coefficient) fuzzy match
        var inputBigrams = GetBigrams(normalised);
        if (inputBigrams.Count == 0) return null;

        ResolvedFailure? bestMatch = null;
        double bestScore = 0;

        foreach (var failure in pool)
        {
            var nameBigrams = GetBigrams(failure.Name.ToLowerInvariant());
            var score       = DiceCoefficient(inputBigrams, nameBigrams);

            if (score > bestScore)
            {
                bestScore = score;
                bestMatch = failure;
            }
        }

        return bestScore >= FuzzyThreshold ? bestMatch : null;
    }

    private static HashSet<string> GetBigrams(string s)
    {
        var bigrams = new HashSet<string>();
        for (var i = 0; i < s.Length - 1; i++)
            bigrams.Add(s.Substring(i, 2));
        return bigrams;
    }

    private static double DiceCoefficient(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        var intersection = a.Count(x => b.Contains(x));
        return 2.0 * intersection / (a.Count + b.Count);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // These access the settings indirectly through TwitchService's settings reference.
    // In a DI container this would be injected; for now we get it via the service.

    private string? GetRewardIdForTier(FailureTier tier)
    {
        // TwitchService exposes settings via a property in a real implementation.
        // For now we'll add a helper method to TwitchService rather than exposing
        // settings directly — see TwitchService.GetRewardIdForTier().
        return _twitch.GetRewardIdForTier(tier);
    }

    private string? GetPickYourPoisonRewardId()
    {
        return _twitch.GetPickYourPoisonRewardId();
    }
}