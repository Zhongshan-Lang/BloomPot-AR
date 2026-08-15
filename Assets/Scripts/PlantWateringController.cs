using System;
using UnityEngine;

public sealed class PlantWateringController
{
    public const int MaximumWateringsPerDay = 5;
    public const float HydrationPerWatering = 0.2f;

    private readonly PlantExperienceProfile _profile;
    private readonly PlantHydrationController _hydrationController;

    public PlantWateringController()
        : this(PlantExperienceProfile.Companion)
    {
    }

    public PlantWateringController(PlantExperienceProfile profile)
    {
        _profile = profile ?? PlantExperienceProfile.Companion;
        _hydrationController = new PlantHydrationController(_profile);
    }

    public PlantWateringResult RegisterWatering(PlantState state, DateTime utcNow)
    {
        var now = PrepareState(state, utcNow);
        var previousVitality = state.vitality;
        var previousHydration = state.hydration;
        var previousBond = state.bond;
        var rewarded = CanWaterPrepared(state, now);
        var careDayAdded = false;
        if (rewarded)
        {
            state.hydration = Mathf.Clamp01(state.hydration + HydrationPerWatering);
            state.wateringsInCurrentDay++;
            state.lastWateredUtc = PlantState.FormatUtc(now);
            state.lastHydrationUpdateUtc = PlantState.FormatUtc(now);
            state.lastInteractionUtc = PlantState.FormatUtc(now);
            careDayAdded = PlantStageProgressionController.RegisterCareDay(state, now, _profile);
        }

        return new PlantWateringResult(
            rewarded,
            careDayAdded,
            previousVitality,
            state.vitality,
            previousHydration,
            state.hydration,
            previousBond,
            state.bond,
            GetRemainingWateringsPrepared(state));
    }

    public bool CanWater(PlantState state, DateTime utcNow)
    {
        if (state == null)
        {
            return false;
        }

        var now = PrepareState(state, utcNow);
        return CanWaterPrepared(state, now);
    }

    public int GetRemainingWaterings(PlantState state, DateTime utcNow)
    {
        if (state == null)
        {
            return 0;
        }

        PrepareState(state, utcNow);
        return GetRemainingWateringsPrepared(state);
    }

    public TimeSpan GetRemainingCooldown(PlantState state, DateTime utcNow)
    {
        if (state == null)
        {
            return TimeSpan.Zero;
        }

        var now = PrepareState(state, utcNow);
        if (GetRemainingWateringsPrepared(state) > 0)
        {
            return TimeSpan.Zero;
        }

        var availableUtc = _profile.IsPortfolioDemo
            ? state.GetWateringWindowStartUtc(now).Add(_profile.SimulatedDayDuration)
            : now.Date.AddDays(1d);
        return availableUtc > now ? availableUtc - now : TimeSpan.Zero;
    }

    private DateTime PrepareState(PlantState state, DateTime utcNow)
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

        _hydrationController.ApplyTimeProgress(state, now);
        ResetWateringCounterIfNeeded(state, now);
        return now;
    }

    private void ResetWateringCounterIfNeeded(PlantState state, DateTime utcNow)
    {
        if (state.TryGetLastWateredUtc(out var lastWateredUtc) && utcNow < lastWateredUtc)
        {
            return;
        }

        if (_profile.IsPortfolioDemo)
        {
            var windowStart = state.GetWateringWindowStartUtc(utcNow);
            var elapsed = utcNow - windowStart;
            if (elapsed >= _profile.SimulatedDayDuration)
            {
                state.wateringWindowStartUtc = PlantState.FormatUtc(utcNow);
                state.wateringsInCurrentDay = 0;
            }

            return;
        }

        if (!state.IsWateringDay(utcNow))
        {
            state.wateringDayUtc = PlantState.FormatUtcDay(utcNow);
            state.wateringsInCurrentDay = 0;
        }
    }

    private static bool CanWaterPrepared(PlantState state, DateTime utcNow)
    {
        if (state.TryGetLastWateredUtc(out var lastWateredUtc) && utcNow < lastWateredUtc)
        {
            return false;
        }

        return state.hydration < 0.9999f
            && state.wateringsInCurrentDay < MaximumWateringsPerDay;
    }

    private static int GetRemainingWateringsPrepared(PlantState state)
    {
        return Mathf.Max(0, MaximumWateringsPerDay - state.wateringsInCurrentDay);
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        switch (value.Kind)
        {
            case DateTimeKind.Utc: return value;
            case DateTimeKind.Local: return value.ToUniversalTime();
            default: return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }
    }
}

public readonly struct PlantWateringResult
{
    public PlantWateringResult(
        bool rewarded,
        bool careDayAdded,
        float previousVitality,
        float currentVitality,
        float previousHydration,
        float currentHydration,
        float previousBond,
        float currentBond,
        int remainingWaterings)
    {
        Rewarded = rewarded;
        CareDayAdded = careDayAdded;
        PreviousVitality = previousVitality;
        CurrentVitality = currentVitality;
        PreviousHydration = previousHydration;
        CurrentHydration = currentHydration;
        PreviousBond = previousBond;
        CurrentBond = currentBond;
        RemainingWaterings = remainingWaterings;
    }

    public bool Rewarded { get; }
    public bool CareDayAdded { get; }
    public float PreviousVitality { get; }
    public float CurrentVitality { get; }
    public float PreviousHydration { get; }
    public float CurrentHydration { get; }
    public float PreviousBond { get; }
    public float CurrentBond { get; }
    public int RemainingWaterings { get; }
    public bool StateChanged => Rewarded || CareDayAdded;
}
