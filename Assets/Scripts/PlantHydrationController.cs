using System;
using UnityEngine;

public sealed class PlantHydrationController
{
    public const float HydrationLossPerHalfDay = 0.1f;
    public const float NeutralHydration = 0.6f;
    public const float MinimumGrowthMultiplier = 0.75f;
    public const float MaximumGrowthMultiplier = 1.05f;

    private readonly PlantExperienceProfile _profile;

    public PlantHydrationController()
        : this(PlantExperienceProfile.Companion)
    {
    }

    public PlantHydrationController(PlantExperienceProfile profile)
    {
        _profile = profile ?? PlantExperienceProfile.Companion;
    }

    public PlantHydrationResult ApplyTimeProgress(PlantState state, DateTime utcNow)
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

        var previousHydration = state.hydration;
        var previousUpdate = state.GetLastHydrationUpdateUtc(now);
        if (now <= previousUpdate)
        {
            return new PlantHydrationResult(TimeSpan.Zero, previousHydration, state.hydration);
        }

        var elapsed = now - previousUpdate;
        if (elapsed > TimeSpan.Zero)
        {
            state.hydration = CalculateHydrationAfterElapsed(
                state.hydration,
                elapsed,
                _profile);
        }

        state.lastHydrationUpdateUtc = PlantState.FormatUtc(now);
        return new PlantHydrationResult(elapsed, previousHydration, state.hydration);
    }

    public static float CalculateGrowthMultiplier(float hydration)
    {
        var normalizedHydration = Mathf.Clamp01(hydration);
        if (normalizedHydration <= NeutralHydration)
        {
            var progressToNeutral = Mathf.SmoothStep(
                0f,
                1f,
                normalizedHydration / NeutralHydration);
            return Mathf.Lerp(MinimumGrowthMultiplier, 1f, progressToNeutral);
        }

        return Mathf.Lerp(
            1f,
            MaximumGrowthMultiplier,
            (normalizedHydration - NeutralHydration) / (1f - NeutralHydration));
    }

    public static float CalculateHydrationAfterElapsed(
        float hydration,
        TimeSpan elapsed,
        PlantExperienceProfile profile)
    {
        var elapsedSeconds = Math.Max(0d, elapsed.TotalSeconds);
        var hydrationLoss = (float)elapsedSeconds * GetHydrationLossPerSecond(profile);
        return Mathf.Max(0f, Mathf.Clamp01(hydration) - hydrationLoss);
    }

    public static float GetHydrationLossPerSecond(PlantExperienceProfile profile)
    {
        var resolvedProfile = profile ?? PlantExperienceProfile.Companion;
        var halfDay = resolvedProfile.IsPortfolioDemo
            ? TimeSpan.FromTicks(resolvedProfile.SimulatedDayDuration.Ticks / 2)
            : TimeSpan.FromHours(12d);
        return halfDay > TimeSpan.Zero
            ? HydrationLossPerHalfDay / (float)halfDay.TotalSeconds
            : 0f;
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

public readonly struct PlantHydrationResult
{
    public PlantHydrationResult(
        TimeSpan elapsed,
        float previousHydration,
        float currentHydration)
    {
        Elapsed = elapsed;
        PreviousHydration = previousHydration;
        CurrentHydration = currentHydration;
    }

    public TimeSpan Elapsed { get; }
    public float PreviousHydration { get; }
    public float CurrentHydration { get; }
    public bool HydrationChanged => !Mathf.Approximately(PreviousHydration, CurrentHydration);
}
