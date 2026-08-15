using System;
using UnityEngine;

public sealed class PlantLightController
{
    public const float NeutralLight = 0.7f;
    public const float LightPerBoost = 0.05f;
    public const int MaximumBoostsPerDay = 6;
    public const float MinimumGrowthMultiplier = 0.7f;
    public const float MaximumGrowthMultiplier = 1.2f;

    private const float ArCoreMiddleGray = 0.466f;
    private readonly PlantExperienceProfile _profile;

    public PlantLightController()
        : this(PlantExperienceProfile.Companion)
    {
    }

    public PlantLightController(PlantExperienceProfile profile)
    {
        _profile = profile ?? PlantExperienceProfile.Companion;
    }

    public bool RefreshDay(PlantState state, DateTime utcNow)
    {
        var now = ValidateAndNormalize(state, utcNow);
        if (_profile.IsPortfolioDemo)
        {
            var windowStart = state.GetLightWindowStartUtc(now);
            var elapsed = now - windowStart;
            if (elapsed < TimeSpan.Zero || elapsed < _profile.SimulatedDayDuration)
            {
                return false;
            }
        }
        else if (state.IsLightDay(now))
        {
            return false;
        }

        ResetDailyLight(state, now);
        return true;
    }

    public PlantLightCaptureResult CaptureEnvironmentLight(
        PlantState state,
        float normalizedLight,
        DateTime utcNow)
    {
        var now = ValidateAndNormalize(state, utcNow);
        RefreshDay(state, now);
        var previousLight = state.lightExposure;
        if (state.lightEnvironmentSampled)
        {
            return new PlantLightCaptureResult(false, previousLight, state.lightExposure);
        }

        state.lightExposure = Mathf.Clamp01(normalizedLight);
        state.lightEnvironmentSampled = true;
        return new PlantLightCaptureResult(true, previousLight, state.lightExposure);
    }

    public PlantLightBoostResult AddLightBoost(PlantState state, DateTime utcNow)
    {
        var now = ValidateAndNormalize(state, utcNow);
        RefreshDay(state, now);
        var previousLight = state.lightExposure;
        var added = state.lightEnvironmentSampled
            && state.lightExposure < 0.9999f
            && state.lightBoostsToday < MaximumBoostsPerDay;
        if (added)
        {
            state.lightExposure = Mathf.Clamp01(state.lightExposure + LightPerBoost);
            state.lightBoostsToday++;
        }

        return new PlantLightBoostResult(
            added,
            previousLight,
            state.lightExposure,
            GetRemainingBoostsPrepared(state));
    }

    public bool CanAddLightBoost(PlantState state, DateTime utcNow)
    {
        var now = ValidateAndNormalize(state, utcNow);
        RefreshDay(state, now);
        return state.lightEnvironmentSampled
            && state.lightExposure < 0.9999f
            && state.lightBoostsToday < MaximumBoostsPerDay;
    }

    public int GetRemainingBoosts(PlantState state, DateTime utcNow)
    {
        var now = ValidateAndNormalize(state, utcNow);
        RefreshDay(state, now);
        return GetRemainingBoostsPrepared(state);
    }

    public static float CalculateGrowthMultiplier(float lightExposure)
    {
        var normalizedLight = Mathf.Clamp01(lightExposure);
        if (normalizedLight <= NeutralLight)
        {
            return Mathf.Lerp(
                MinimumGrowthMultiplier,
                1f,
                normalizedLight / NeutralLight);
        }

        return Mathf.Lerp(
            1f,
            MaximumGrowthMultiplier,
            (normalizedLight - NeutralLight) / (1f - NeutralLight));
    }

    public static float NormalizeIntensityCorrection(float intensityCorrection)
    {
        return Mathf.Clamp01(
            NeutralLight + (intensityCorrection - ArCoreMiddleGray) * 1.5f);
    }

    public static float NormalizeAmbientIntensity(float lumens)
    {
        return Mathf.Lerp(0.15f, 1f, Mathf.InverseLerp(100f, 1500f, lumens));
    }

    private static int GetRemainingBoostsPrepared(PlantState state)
    {
        return Mathf.Max(0, MaximumBoostsPerDay - state.lightBoostsToday);
    }

    private static void ResetDailyLight(PlantState state, DateTime utcNow)
    {
        state.lightExposure = 0f;
        state.lightEnvironmentSampled = false;
        state.lightBoostsToday = 0;
        state.lightDayUtc = PlantState.FormatUtcDay(utcNow);
        state.lightWindowStartUtc = PlantState.FormatUtc(utcNow);
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

public readonly struct PlantLightCaptureResult
{
    public PlantLightCaptureResult(bool captured, float previousLight, float currentLight)
    {
        Captured = captured;
        PreviousLight = previousLight;
        CurrentLight = currentLight;
    }

    public bool Captured { get; }
    public float PreviousLight { get; }
    public float CurrentLight { get; }
}

public readonly struct PlantLightBoostResult
{
    public PlantLightBoostResult(
        bool added,
        float previousLight,
        float currentLight,
        int remainingBoosts)
    {
        Added = added;
        PreviousLight = previousLight;
        CurrentLight = currentLight;
        RemainingBoosts = remainingBoosts;
    }

    public bool Added { get; }
    public float PreviousLight { get; }
    public float CurrentLight { get; }
    public int RemainingBoosts { get; }
}
