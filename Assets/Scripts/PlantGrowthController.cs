using System;
using UnityEngine;

public sealed class PlantGrowthController
{
    public const double InactivityGraceHours = 12d;
    public const float VitalityLossPerDay = 0.08f;
    public const float FullWiltVitalityThreshold = 0.25f;
    public const float CriticalAppearanceVitalityThreshold = 0.35f;
    public const float HealthyVitalityThreshold = 0.85f;
    public const float MaximumCompanionWilt = 0.99f;
    public const float AppearanceDecayAtCriticalThreshold = 0.45f;
    public const double MaximumOfflineDaysPerEvaluation = 30d;
    public const float MaximumHydrationVitalityLossPerDay = 0.08f;
    public const float MaximumHydrationVitalityRecoveryPerDay = 0.06f;

    private readonly PlantExperienceProfile _profile;
    private DateTime? _lastRealtimeEvaluationUtc;

    public PlantGrowthController()
        : this(PlantExperienceProfile.Companion)
    {
    }

    public PlantGrowthController(PlantExperienceProfile profile)
    {
        _profile = profile ?? PlantExperienceProfile.Companion;
    }

    public PlantGrowthResult ApplyOfflineProgress(PlantState state, DateTime utcNow)
    {
        if (state == null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var now = EnsureUtc(utcNow);
        if (!state.Normalize(now))
        {
            throw new InvalidOperationException(
                $"Unsupported plant state schema version {state.schemaVersion}.");
        }

        var previousVitality = state.vitality;
        var lastInteraction = state.GetLastInteractionUtc(now);
        var lastSaved = state.GetLastSavedUtc(now);
        var graceEnd = lastInteraction.Add(_profile.InactivityGrace);
        var vitalityDelta = CalculateVitalityDelta(
            state.hydration,
            lastSaved,
            now,
            graceEnd,
            true);
        state.vitality = Mathf.Clamp(
            state.vitality + vitalityDelta.NetChange,
            _profile.MinimumVitality,
            1f);

        return new PlantGrowthResult(
            vitalityDelta.EvaluatedInactivity,
            previousVitality,
            state.vitality,
            CalculateWilt(state.vitality),
            vitalityDelta.InactivityLoss,
            vitalityDelta.HydrationChange);
    }

    public PlantGrowthResult ApplyRealtimeProgress(PlantState state, DateTime utcNow)
    {
        if (state == null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var now = EnsureUtc(utcNow);
        if (!state.Normalize(now))
        {
            throw new InvalidOperationException(
                $"Unsupported plant state schema version {state.schemaVersion}.");
        }

        var previousVitality = state.vitality;
        var previousEvaluation = _lastRealtimeEvaluationUtc ?? now;
        _lastRealtimeEvaluationUtc = now;
        var graceEnd = state.GetLastInteractionUtc(now).Add(_profile.InactivityGrace);
        var vitalityDelta = CalculateVitalityDelta(
            state.hydration,
            previousEvaluation,
            now,
            graceEnd,
            true);
        state.vitality = Mathf.Clamp(
            state.vitality + vitalityDelta.NetChange,
            _profile.MinimumVitality,
            1f);

        return new PlantGrowthResult(
            vitalityDelta.EvaluatedInactivity,
            previousVitality,
            state.vitality,
            CalculateWilt(state.vitality),
            vitalityDelta.InactivityLoss,
            vitalityDelta.HydrationChange);
    }

    public void ResetRealtime(DateTime utcNow)
    {
        _lastRealtimeEvaluationUtc = EnsureUtc(utcNow);
    }

    public float CalculateWilt(float vitality)
    {
        var normalizedVitality = Mathf.Clamp01(vitality);
        if (normalizedVitality >= HealthyVitalityThreshold)
        {
            return 0f;
        }

        var vitalityRange = HealthyVitalityThreshold - FullWiltVitalityThreshold;
        var depleted = (HealthyVitalityThreshold - normalizedVitality) / vitalityRange;
        return Mathf.SmoothStep(0f, MaximumCompanionWilt, Mathf.Clamp01(depleted));
    }

    public float CalculateAppearanceDecay(float vitality)
    {
        var normalizedVitality = Mathf.Clamp01(vitality);
        if (normalizedVitality >= HealthyVitalityThreshold)
        {
            return 0f;
        }

        if (normalizedVitality >= CriticalAppearanceVitalityThreshold)
        {
            var wiltRange = HealthyVitalityThreshold - CriticalAppearanceVitalityThreshold;
            var depleted = (HealthyVitalityThreshold - normalizedVitality) / wiltRange;
            return Mathf.SmoothStep(
                0f,
                AppearanceDecayAtCriticalThreshold,
                Mathf.Clamp01(depleted));
        }

        var criticalDepletion =
            (CriticalAppearanceVitalityThreshold - normalizedVitality)
            / CriticalAppearanceVitalityThreshold;
        return Mathf.Lerp(
            AppearanceDecayAtCriticalThreshold,
            1f,
            Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(criticalDepletion)));
    }

    public static float CalculateHydrationVitalityRatePerDay(float hydration)
    {
        var normalizedHydration = Mathf.Clamp01(hydration);
        if (normalizedHydration < PlantHydrationController.NeutralHydration)
        {
            return -MaximumHydrationVitalityLossPerDay
                * (PlantHydrationController.NeutralHydration - normalizedHydration)
                / PlantHydrationController.NeutralHydration;
        }

        return MaximumHydrationVitalityRecoveryPerDay
            * (normalizedHydration - PlantHydrationController.NeutralHydration)
            / (1f - PlantHydrationController.NeutralHydration);
    }

    public float CalculateHydrationVitalityChange(float hydration, TimeSpan elapsed)
    {
        return IntegrateHydrationVitalityChange(
            hydration,
            elapsed,
            includeRecovery: true,
            includeLoss: true);
    }

    private PlantVitalityDelta CalculateVitalityDelta(
        float hydrationAtStart,
        DateTime intervalStart,
        DateTime intervalEnd,
        DateTime graceEnd,
        bool includeInactivityLoss)
    {
        if (intervalEnd <= intervalStart)
        {
            return PlantVitalityDelta.None;
        }

        var maximumEvaluation = _profile.MaximumDecayEvaluation;
        var evaluationStart = intervalEnd - intervalStart > maximumEvaluation
            ? intervalEnd - maximumEvaluation
            : intervalStart;
        var skipped = evaluationStart - intervalStart;
        var startingHydration = PlantHydrationController.CalculateHydrationAfterElapsed(
            hydrationAtStart,
            skipped,
            _profile);
        var totalElapsed = intervalEnd - evaluationStart;
        var hydrationRecovery = IntegrateHydrationVitalityChange(
            startingHydration,
            totalElapsed,
            includeRecovery: true,
            includeLoss: false);

        var negativeStart = evaluationStart > graceEnd ? evaluationStart : graceEnd;
        var hydrationLoss = 0f;
        var inactivityLoss = 0f;
        var evaluatedInactivity = TimeSpan.Zero;
        if (intervalEnd > negativeStart)
        {
            evaluatedInactivity = intervalEnd - negativeStart;
            var hydrationAtNegativeStart = PlantHydrationController.CalculateHydrationAfterElapsed(
                startingHydration,
                negativeStart - evaluationStart,
                _profile);
            hydrationLoss = IntegrateHydrationVitalityChange(
                hydrationAtNegativeStart,
                evaluatedInactivity,
                includeRecovery: false,
                includeLoss: true);
            if (includeInactivityLoss)
            {
                inactivityLoss = (float)evaluatedInactivity.TotalSeconds
                    * _profile.VitalityLossPerSecond;
            }
        }

        return new PlantVitalityDelta(
            evaluatedInactivity,
            inactivityLoss,
            hydrationRecovery + hydrationLoss);
    }

    private float IntegrateHydrationVitalityChange(
        float startingHydration,
        TimeSpan elapsed,
        bool includeRecovery,
        bool includeLoss)
    {
        var remainingSeconds = Math.Max(0d, elapsed.TotalSeconds);
        if (remainingSeconds <= 0d)
        {
            return 0f;
        }

        var hydration = Mathf.Clamp01(startingHydration);
        var hydrationLossPerSecond = PlantHydrationController.GetHydrationLossPerSecond(_profile);
        var daySeconds = _profile.IsPortfolioDemo
            ? _profile.SimulatedDayDuration.TotalSeconds
            : TimeSpan.FromDays(1d).TotalSeconds;
        var vitalityChange = 0d;

        for (var segmentIndex = 0; segmentIndex < 3 && remainingSeconds > 0d; segmentIndex++)
        {
            var boundary = hydration > PlantHydrationController.NeutralHydration
                ? PlantHydrationController.NeutralHydration
                : 0f;
            var secondsToBoundary = hydrationLossPerSecond > 0f && hydration > boundary
                ? (hydration - boundary) / hydrationLossPerSecond
                : double.PositiveInfinity;
            var segmentSeconds = Math.Min(remainingSeconds, secondsToBoundary);
            var endingHydration = Mathf.Max(
                boundary,
                hydration - hydrationLossPerSecond * (float)segmentSeconds);
            var startingRate = FilterHydrationRate(
                CalculateHydrationVitalityRatePerDay(hydration) / (float)daySeconds,
                includeRecovery,
                includeLoss);
            var endingRate = FilterHydrationRate(
                CalculateHydrationVitalityRatePerDay(endingHydration) / (float)daySeconds,
                includeRecovery,
                includeLoss);
            vitalityChange += (startingRate + endingRate) * 0.5d * segmentSeconds;
            remainingSeconds -= segmentSeconds;
            hydration = endingHydration;

            if (double.IsPositiveInfinity(secondsToBoundary))
            {
                break;
            }
        }

        return (float)vitalityChange;
    }

    private static float FilterHydrationRate(
        float rate,
        bool includeRecovery,
        bool includeLoss)
    {
        if (rate > 0f && !includeRecovery)
        {
            return 0f;
        }

        if (rate < 0f && !includeLoss)
        {
            return 0f;
        }

        return rate;
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        switch (value.Kind)
        {
            case DateTimeKind.Utc:
                return value;
            case DateTimeKind.Local:
                return value.ToUniversalTime();
            default:
                return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }
    }
}

public readonly struct PlantGrowthResult
{
    public PlantGrowthResult(
        TimeSpan evaluatedInactivity,
        float previousVitality,
        float currentVitality,
        float wiltAmount,
        float inactivityVitalityLoss,
        float hydrationVitalityChange)
    {
        EvaluatedInactivity = evaluatedInactivity;
        PreviousVitality = previousVitality;
        CurrentVitality = currentVitality;
        WiltAmount = wiltAmount;
        InactivityVitalityLoss = inactivityVitalityLoss;
        HydrationVitalityChange = hydrationVitalityChange;
    }

    public TimeSpan EvaluatedInactivity { get; }
    public float PreviousVitality { get; }
    public float CurrentVitality { get; }
    public float WiltAmount { get; }
    public float InactivityVitalityLoss { get; }
    public float HydrationVitalityChange { get; }
    public bool VitalityChanged => !Mathf.Approximately(PreviousVitality, CurrentVitality);
}

internal readonly struct PlantVitalityDelta
{
    public PlantVitalityDelta(
        TimeSpan evaluatedInactivity,
        float inactivityLoss,
        float hydrationChange)
    {
        EvaluatedInactivity = evaluatedInactivity;
        InactivityLoss = inactivityLoss;
        HydrationChange = hydrationChange;
    }

    public static PlantVitalityDelta None { get; } = new PlantVitalityDelta(
        TimeSpan.Zero,
        0f,
        0f);

    public TimeSpan EvaluatedInactivity { get; }
    public float InactivityLoss { get; }
    public float HydrationChange { get; }
    public float NetChange => HydrationChange - InactivityLoss;
}
