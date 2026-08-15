using System;
using UnityEngine;

public sealed class PlantInteractionController
{
    public const int MaximumMeaningfulInteractionsPerUtcDay = 5;
    public const float BondPerMeaningfulInteraction = 0.025f;
    public const float VitalityPerVisit = 0.04f;
    public const float BondPerVisit = 0.01f;
    public static readonly TimeSpan MinimumVisitInterval = TimeSpan.FromHours(4d);

    private readonly PlantExperienceProfile _profile;

    public PlantInteractionController()
        : this(PlantExperienceProfile.Companion)
    {
    }

    public PlantInteractionController(PlantExperienceProfile profile)
    {
        _profile = profile ?? PlantExperienceProfile.Companion;
    }

    public PlantInteractionResult RegisterVisit(PlantState state, DateTime utcNow)
    {
        var now = ValidateAndNormalize(state, utcNow);
        var previousVitality = state.vitality;
        var previousBond = state.bond;
        var elapsedSinceVisit = now - state.GetLastVisitUtc(now);
        var rewarded = elapsedSinceVisit >= _profile.VisitRewardInterval;

        if (rewarded)
        {
            state.vitality = Mathf.Clamp01(state.vitality + VitalityPerVisit);
            state.bond = Mathf.Clamp01(state.bond + BondPerVisit);
            state.lastInteractionUtc = PlantState.FormatUtc(now);
            state.lastVisitUtc = PlantState.FormatUtc(now);
            PlantStageProgressionController.RegisterCareDay(state, now, _profile);
        }

        return new PlantInteractionResult(
            PlantInteractionKind.Visit,
            rewarded,
            previousVitality,
            state.vitality,
            previousBond,
            state.bond,
            GetRemainingMeaningfulInteractions(state, now));
    }

    public PlantInteractionResult RegisterTap(PlantState state, DateTime utcNow)
    {
        var now = ValidateAndNormalize(state, utcNow);
        ResetInteractionCounterIfNeeded(state, now);
        var previousVitality = state.vitality;
        var previousBond = state.bond;
        var rewarded = state.meaningfulInteractionsToday < MaximumMeaningfulInteractionsPerUtcDay;

        if (rewarded)
        {
            state.meaningfulInteractionsToday++;
            state.bond = Mathf.Clamp01(state.bond + BondPerMeaningfulInteraction);
            state.lastInteractionUtc = PlantState.FormatUtc(now);
            PlantStageProgressionController.RegisterCareDay(state, now, _profile);
        }

        return new PlantInteractionResult(
            PlantInteractionKind.Tap,
            rewarded,
            previousVitality,
            state.vitality,
            previousBond,
            state.bond,
            GetRemainingMeaningfulInteractions(state, now));
    }

    public int GetRemainingMeaningfulInteractions(PlantState state, DateTime utcNow)
    {
        var now = ValidateAndNormalize(state, utcNow);
        ResetInteractionCounterIfNeeded(state, now);
        return Mathf.Max(
            0,
            MaximumMeaningfulInteractionsPerUtcDay - state.meaningfulInteractionsToday);
    }

    private static DateTime ValidateAndNormalize(PlantState state, DateTime utcNow)
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

        return now;
    }

    private void ResetInteractionCounterIfNeeded(PlantState state, DateTime utcNow)
    {
        if (_profile.UsesUtcDailyInteractionReset)
        {
            if (!state.IsInteractionDay(utcNow))
            {
                state.interactionDayUtc = PlantState.FormatUtcDay(utcNow);
                state.meaningfulInteractionsToday = 0;
            }

            return;
        }

        var windowStart = state.GetInteractionWindowStartUtc(utcNow);
        var elapsed = utcNow - windowStart;
        if (elapsed < TimeSpan.Zero || elapsed >= _profile.InteractionLimitWindow)
        {
            state.interactionWindowStartUtc = PlantState.FormatUtc(utcNow);
            state.meaningfulInteractionsToday = 0;
        }
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

public enum PlantInteractionKind
{
    Visit,
    Tap
}

public readonly struct PlantInteractionResult
{
    public PlantInteractionResult(
        PlantInteractionKind kind,
        bool rewarded,
        float previousVitality,
        float currentVitality,
        float previousBond,
        float currentBond,
        int remainingMeaningfulInteractions)
    {
        Kind = kind;
        Rewarded = rewarded;
        PreviousVitality = previousVitality;
        CurrentVitality = currentVitality;
        PreviousBond = previousBond;
        CurrentBond = currentBond;
        RemainingMeaningfulInteractions = remainingMeaningfulInteractions;
    }

    public PlantInteractionKind Kind { get; }
    public bool Rewarded { get; }
    public float PreviousVitality { get; }
    public float CurrentVitality { get; }
    public float PreviousBond { get; }
    public float CurrentBond { get; }
    public int RemainingMeaningfulInteractions { get; }
    public bool StateChanged =>
        Rewarded
        || !Mathf.Approximately(PreviousVitality, CurrentVitality)
        || !Mathf.Approximately(PreviousBond, CurrentBond);
}
