using System;
using UnityEngine;

public sealed class PlantGrowthController
{
    public const double InactivityGraceHours = 12d;
    public const float VitalityLossPerDay = 0.08f;
    public const float MinimumVitality = 0.35f;
    public const float HealthyVitalityThreshold = 0.85f;
    public const float MaximumCompanionWilt = 0.45f;
    public const double MaximumOfflineDaysPerEvaluation = 30d;

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
        var decayStart = lastSaved > graceEnd ? lastSaved : graceEnd;
        var elapsed = now > decayStart ? now - decayStart : TimeSpan.Zero;
        var evaluatedSeconds = Math.Min(
            elapsed.TotalSeconds,
            _profile.MaximumDecayEvaluation.TotalSeconds);

        if (evaluatedSeconds > 0d)
        {
            var vitalityLoss = (float)evaluatedSeconds * _profile.VitalityLossPerSecond;
            state.vitality = Mathf.Max(MinimumVitality, state.vitality - vitalityLoss);
        }

        return new PlantGrowthResult(
            elapsed,
            previousVitality,
            state.vitality,
            CalculateWilt(state.vitality));
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
        var decayStart = previousEvaluation > graceEnd ? previousEvaluation : graceEnd;
        var elapsed = now > decayStart ? now - decayStart : TimeSpan.Zero;
        var evaluatedSeconds = Math.Min(
            elapsed.TotalSeconds,
            _profile.MaximumDecayEvaluation.TotalSeconds);
        if (_profile.UsesRealtimeDecay && evaluatedSeconds > 0d)
        {
            var vitalityLoss = (float)evaluatedSeconds * _profile.VitalityLossPerSecond;
            state.vitality = Mathf.Max(MinimumVitality, state.vitality - vitalityLoss);
        }

        return new PlantGrowthResult(
            elapsed,
            previousVitality,
            state.vitality,
            CalculateWilt(state.vitality));
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

        var vitalityRange = HealthyVitalityThreshold - MinimumVitality;
        var depleted = (HealthyVitalityThreshold - normalizedVitality) / vitalityRange;
        return Mathf.SmoothStep(0f, MaximumCompanionWilt, Mathf.Clamp01(depleted));
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
        float wiltAmount)
    {
        EvaluatedInactivity = evaluatedInactivity;
        PreviousVitality = previousVitality;
        CurrentVitality = currentVitality;
        WiltAmount = wiltAmount;
    }

    public TimeSpan EvaluatedInactivity { get; }
    public float PreviousVitality { get; }
    public float CurrentVitality { get; }
    public float WiltAmount { get; }
    public bool VitalityChanged => !Mathf.Approximately(PreviousVitality, CurrentVitality);
}
