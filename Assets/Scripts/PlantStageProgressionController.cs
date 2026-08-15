using System;
using UnityEngine;

public sealed class PlantStageProgressionController
{
    public const float MinimumGrowthVitality = 0.7f;
    public const float CriticalGrowthVitality = 0.35f;
    public const int FullGrowthCareDays = 8;
    public const float FullGrowthBond = 0.9f;

    private static readonly int[] RequiredCareDays = { 0, 1, 2, 4, 6, 8 };
    private static readonly float[] RequiredBond = { 0f, 0.08f, 0.25f, 0.45f, 0.7f, 0.9f };
    private const float LegacyFullGrowthBond = 0.5f;
    private static readonly float[] LegacyRequiredBond = { 0f, 0.025f, 0.1f, 0.2f, 0.35f, 0.5f };

    public PlantStageProgressionController()
        : this(PlantExperienceProfile.Companion)
    {
    }

    private readonly PlantExperienceProfile _profile;

    public PlantStageProgressionController(PlantExperienceProfile profile)
    {
        _profile = profile ?? PlantExperienceProfile.Companion;
    }

    public PlantGrowthProgressResult EvaluateProgress(PlantState state, DateTime utcNow)
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

        var previousProgress = state.growthProgress;
        var previousStage = GetStageIndexForProgress(previousProgress);
        var potential = CalculateGrowthPotential(
            state.careDayCount,
            state.bond,
            state.vitality,
            state.hydration,
            state.lightExposure);
        potential = LimitMilestoneAdvance(state, previousProgress, potential, now);
        state.growthProgress = Mathf.Max(previousProgress, potential);
        state.stageIndex = GetStageIndexForProgress(state.growthProgress);
        if (state.stageIndex > previousStage)
        {
            state.lastStageAdvancedUtc = PlantState.FormatUtc(now);
        }

        return new PlantGrowthProgressResult(
            previousProgress,
            state.growthProgress,
            potential,
            previousStage,
            state.stageIndex,
            state.growthProgress > previousProgress + 0.0001f);
    }

    public static bool RegisterCareDay(PlantState state, DateTime utcNow)
    {
        return RegisterCareDay(state, utcNow, PlantExperienceProfile.Companion);
    }

    public static bool RegisterCareDay(
        PlantState state,
        DateTime utcNow,
        PlantExperienceProfile profile)
    {
        if (state == null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var now = EnsureUtc(utcNow);
        var resolvedProfile = profile ?? PlantExperienceProfile.Companion;
        if (resolvedProfile.IsPortfolioDemo)
        {
            if (state.TryGetLastCareProgressUtc(out var lastCareProgressUtc))
            {
                var elapsed = now - lastCareProgressUtc;
                if (elapsed < TimeSpan.Zero || elapsed < resolvedProfile.SimulatedDayDuration)
                {
                    return false;
                }
            }

            state.careDayCount = Mathf.Max(0, state.careDayCount) + 1;
            state.lastCareProgressUtc = PlantState.FormatUtc(now);
            return true;
        }

        if (!state.CanRegisterCareDay(now))
        {
            return false;
        }

        state.careDayCount = Mathf.Max(0, state.careDayCount) + 1;
        state.lastCareDayUtc = PlantState.FormatUtcDay(now);
        return true;
    }

    public static float CalculateGrowthPotential(
        int careDayCount,
        float bond,
        float vitality,
        float lightExposure = PlantLightController.NeutralLight)
    {
        return CalculateGrowthPotential(
            careDayCount,
            bond,
            vitality,
            PlantHydrationController.NeutralHydration,
            lightExposure);
    }

    public static float CalculateGrowthPotential(
        int careDayCount,
        float bond,
        float vitality,
        float hydration,
        float lightExposure)
    {
        return CalculateGrowthFactors(
            careDayCount,
            bond,
            vitality,
            hydration,
            lightExposure).GrowthPotential;
    }

    public static PlantGrowthFactors CalculateGrowthFactors(
        int careDayCount,
        float bond,
        float vitality,
        float hydration,
        float lightExposure)
    {
        var dayProgress = Mathf.Clamp01(Mathf.Max(0, careDayCount) / (float)FullGrowthCareDays);
        var bondProgress = Mathf.Clamp01(Mathf.Clamp01(bond) / FullGrowthBond);
        var vitalityMultiplier = Mathf.SmoothStep(
            0f,
            1f,
            Mathf.InverseLerp(CriticalGrowthVitality, MinimumGrowthVitality, vitality));
        var baseGrowth = Mathf.Pow(dayProgress, 0.6f) * Mathf.Pow(bondProgress, 0.4f);
        var hydrationMultiplier = PlantHydrationController.CalculateGrowthMultiplier(hydration);
        var lightMultiplier = PlantLightController.CalculateGrowthMultiplier(lightExposure);
        var conditionMultiplier = vitalityMultiplier * hydrationMultiplier * lightMultiplier;
        var unconstrainedPotential = baseGrowth * conditionMultiplier;
        var careDayCeiling = GetCareDayGrowthCeiling(careDayCount);
        var growthPotential = Mathf.Min(unconstrainedPotential, careDayCeiling);

        return new PlantGrowthFactors(
            dayProgress,
            bondProgress,
            baseGrowth,
            vitalityMultiplier,
            hydrationMultiplier,
            lightMultiplier,
            conditionMultiplier,
            careDayCeiling,
            growthPotential);
    }

    public static int GetRequiredCareDays(int stageIndex)
    {
        return RequiredCareDays[Mathf.Clamp(
            stageIndex,
            PlantState.MinimumStageIndex,
            PlantState.MaximumStageIndex)];
    }

    public static float GetRequiredBond(int stageIndex)
    {
        return RequiredBond[Mathf.Clamp(
            stageIndex,
            PlantState.MinimumStageIndex,
            PlantState.MaximumStageIndex)];
    }

    public static float GetStageAnchor(int stageIndex)
    {
        var normalizedStage = Mathf.Clamp(
            stageIndex,
            PlantState.MinimumStageIndex,
            PlantState.MaximumStageIndex);
        var dayProgress = GetRequiredCareDays(normalizedStage) / (float)FullGrowthCareDays;
        var bondProgress = GetRequiredBond(normalizedStage) / FullGrowthBond;
        return Mathf.Pow(dayProgress, 0.6f) * Mathf.Pow(bondProgress, 0.4f);
    }

    public static float RemapLegacyBondScaleProgress(float legacyProgress)
    {
        var normalizedProgress = Mathf.Clamp01(legacyProgress);
        for (var upperStage = 1; upperStage <= PlantState.MaximumStageIndex; upperStage++)
        {
            var legacyUpper = GetLegacyStageAnchor(upperStage);
            if (normalizedProgress > legacyUpper + 0.0001f)
            {
                continue;
            }

            var lowerStage = upperStage - 1;
            var legacyLower = GetLegacyStageAnchor(lowerStage);
            var stagePosition = Mathf.InverseLerp(legacyLower, legacyUpper, normalizedProgress);
            return Mathf.Lerp(
                GetStageAnchor(lowerStage),
                GetStageAnchor(upperStage),
                stagePosition);
        }

        return 1f;
    }

    public static float GetCareDayGrowthCeiling(int careDayCount)
    {
        var normalizedDays = Mathf.Max(0, careDayCount);
        if (normalizedDays >= FullGrowthCareDays)
        {
            return 1f;
        }

        for (var upperStage = 1; upperStage <= PlantState.MaximumStageIndex; upperStage++)
        {
            var upperDay = GetRequiredCareDays(upperStage);
            if (normalizedDays > upperDay)
            {
                continue;
            }

            var lowerStage = upperStage - 1;
            var lowerDay = GetRequiredCareDays(lowerStage);
            var dayPosition = Mathf.InverseLerp(lowerDay, upperDay, normalizedDays);
            return Mathf.Lerp(
                GetStageAnchor(lowerStage),
                GetStageAnchor(upperStage),
                dayPosition);
        }

        return 1f;
    }

    public static int GetStageIndexForProgress(float growthProgress)
    {
        var normalizedProgress = Mathf.Clamp01(growthProgress);
        for (var stageIndex = PlantState.MaximumStageIndex;
             stageIndex > PlantState.MinimumStageIndex;
             stageIndex--)
        {
            if (normalizedProgress + 0.0001f >= GetStageAnchor(stageIndex))
            {
                return stageIndex;
            }
        }

        return PlantState.MinimumStageIndex;
    }

    private float LimitMilestoneAdvance(
        PlantState state,
        float previousProgress,
        float potential,
        DateTime utcNow)
    {
        var currentStage = GetStageIndexForProgress(previousProgress);
        if (currentStage >= PlantState.MaximumStageIndex)
        {
            return potential;
        }

        var nextStageAnchor = GetStageAnchor(currentStage + 1);
        if (potential + 0.0001f < nextStageAnchor)
        {
            return potential;
        }

        if (!CanAdvanceMilestone(state, utcNow))
        {
            return previousProgress;
        }

        return Mathf.Min(potential, nextStageAnchor);
    }

    private static float GetLegacyStageAnchor(int stageIndex)
    {
        var normalizedStage = Mathf.Clamp(
            stageIndex,
            PlantState.MinimumStageIndex,
            PlantState.MaximumStageIndex);
        var dayProgress = RequiredCareDays[normalizedStage] / (float)FullGrowthCareDays;
        var bondProgress = LegacyRequiredBond[normalizedStage] / LegacyFullGrowthBond;
        return Mathf.Pow(dayProgress, 0.6f) * Mathf.Pow(bondProgress, 0.4f);
    }

    private bool CanAdvanceMilestone(PlantState state, DateTime utcNow)
    {
        if (!state.TryGetLastStageAdvancedUtc(out var lastAdvancedUtc))
        {
            return true;
        }

        if (utcNow < lastAdvancedUtc)
        {
            return false;
        }

        return _profile.IsPortfolioDemo
            ? utcNow - lastAdvancedUtc >= _profile.SimulatedDayDuration
            : utcNow.Date > lastAdvancedUtc.Date;
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

public readonly struct PlantGrowthFactors
{
    public PlantGrowthFactors(
        float dayProgress,
        float bondProgress,
        float baseGrowth,
        float vitalityMultiplier,
        float hydrationMultiplier,
        float lightMultiplier,
        float conditionMultiplier,
        float careDayCeiling,
        float growthPotential)
    {
        DayProgress = dayProgress;
        BondProgress = bondProgress;
        BaseGrowth = baseGrowth;
        VitalityMultiplier = vitalityMultiplier;
        HydrationMultiplier = hydrationMultiplier;
        LightMultiplier = lightMultiplier;
        ConditionMultiplier = conditionMultiplier;
        CareDayCeiling = careDayCeiling;
        GrowthPotential = growthPotential;
    }

    public float DayProgress { get; }
    public float BondProgress { get; }
    public float BaseGrowth { get; }
    public float VitalityMultiplier { get; }
    public float HydrationMultiplier { get; }
    public float LightMultiplier { get; }
    public float ConditionMultiplier { get; }
    public float CareDayCeiling { get; }
    public float GrowthPotential { get; }
}

public readonly struct PlantGrowthProgressResult
{
    public PlantGrowthProgressResult(
        float previousProgress,
        float currentProgress,
        float potentialProgress,
        int previousStageIndex,
        int currentStageIndex,
        bool progressChanged)
    {
        PreviousProgress = previousProgress;
        CurrentProgress = currentProgress;
        PotentialProgress = potentialProgress;
        PreviousStageIndex = previousStageIndex;
        CurrentStageIndex = currentStageIndex;
        ProgressChanged = progressChanged;
    }

    public float PreviousProgress { get; }
    public float CurrentProgress { get; }
    public float PotentialProgress { get; }
    public int PreviousStageIndex { get; }
    public int CurrentStageIndex { get; }
    public bool ProgressChanged { get; }
    public bool StageChanged => CurrentStageIndex != PreviousStageIndex;
}
