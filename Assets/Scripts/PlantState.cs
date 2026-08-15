using System;
using System.Globalization;
using UnityEngine;

[Serializable]
public sealed class PlantState
{
    public const int CurrentSchemaVersion = 11;
    public const int MinimumStageIndex = 0;
    public const int MaximumStageIndex = 5;

    public int schemaVersion = CurrentSchemaVersion;
    public int stageIndex;
    public float growthProgress;
    public float displayedGrowthProgress;
    public float vitality = 1f;
    public float hydration;
    public float lightExposure;
    public bool lightEnvironmentSampled;
    public string lightDayUtc;
    public string lightWindowStartUtc;
    public int lightBoostsToday;
    public float bond;
    public string lastInteractionUtc;
    public string lastSavedUtc;
    public string interactionDayUtc;
    public int meaningfulInteractionsToday;
    public string lastVisitUtc;
    public string interactionWindowStartUtc;
    public int careDayCount;
    public string lastCareDayUtc;
    public string lastCareProgressUtc;
    public string lastWateredUtc;
    public string lastHydrationUpdateUtc;
    public string wateringDayUtc;
    public string wateringWindowStartUtc;
    public int wateringsInCurrentDay;
    public string lastStageAdvancedUtc;

    public static PlantState CreateDefault(DateTime utcNow)
    {
        return CreateDefault(utcNow, PlantExperienceProfile.Companion);
    }

    public static PlantState CreateDefault(DateTime utcNow, PlantExperienceProfile profile)
    {
        var normalizedNow = EnsureUtc(utcNow);
        var resolvedProfile = profile ?? PlantExperienceProfile.Companion;
        return new PlantState
        {
            schemaVersion = CurrentSchemaVersion,
            stageIndex = MinimumStageIndex,
            growthProgress = 0f,
            displayedGrowthProgress = 0f,
            vitality = 1f,
            hydration = 0f,
            lightExposure = 0f,
            lightEnvironmentSampled = false,
            lightDayUtc = FormatUtcDay(normalizedNow),
            lightWindowStartUtc = FormatUtc(normalizedNow),
            lightBoostsToday = 0,
            bond = 0f,
            lastInteractionUtc = FormatUtc(normalizedNow),
            lastSavedUtc = FormatUtc(normalizedNow),
            interactionDayUtc = FormatUtcDay(normalizedNow),
            meaningfulInteractionsToday = 0,
            lastVisitUtc = FormatUtc(normalizedNow.Subtract(resolvedProfile.VisitRewardInterval)),
            interactionWindowStartUtc = FormatUtc(normalizedNow),
            careDayCount = 0,
            lastCareDayUtc = string.Empty,
            lastCareProgressUtc = string.Empty,
            lastWateredUtc = string.Empty,
            lastHydrationUpdateUtc = FormatUtc(normalizedNow),
            wateringDayUtc = FormatUtcDay(normalizedNow),
            wateringWindowStartUtc = FormatUtc(normalizedNow),
            wateringsInCurrentDay = 0,
            lastStageAdvancedUtc = string.Empty
        };
    }

    public PlantState Copy()
    {
        return new PlantState
        {
            schemaVersion = schemaVersion,
            stageIndex = stageIndex,
            growthProgress = growthProgress,
            displayedGrowthProgress = displayedGrowthProgress,
            vitality = vitality,
            hydration = hydration,
            lightExposure = lightExposure,
            lightEnvironmentSampled = lightEnvironmentSampled,
            lightDayUtc = lightDayUtc,
            lightWindowStartUtc = lightWindowStartUtc,
            lightBoostsToday = lightBoostsToday,
            bond = bond,
            lastInteractionUtc = lastInteractionUtc,
            lastSavedUtc = lastSavedUtc,
            interactionDayUtc = interactionDayUtc,
            meaningfulInteractionsToday = meaningfulInteractionsToday,
            lastVisitUtc = lastVisitUtc,
            interactionWindowStartUtc = interactionWindowStartUtc,
            careDayCount = careDayCount,
            lastCareDayUtc = lastCareDayUtc,
            lastCareProgressUtc = lastCareProgressUtc,
            lastWateredUtc = lastWateredUtc,
            lastHydrationUpdateUtc = lastHydrationUpdateUtc,
            wateringDayUtc = wateringDayUtc,
            wateringWindowStartUtc = wateringWindowStartUtc,
            wateringsInCurrentDay = wateringsInCurrentDay,
            lastStageAdvancedUtc = lastStageAdvancedUtc
        };
    }

    public bool Normalize(DateTime utcNow)
    {
        if (schemaVersion > CurrentSchemaVersion)
        {
            return false;
        }

        var normalizedNow = EnsureUtc(utcNow);
        var sourceSchemaVersion = schemaVersion;
        var legacyStageIndex = Mathf.Clamp(stageIndex, 0, 4);
        if (sourceSchemaVersion < 4)
        {
            careDayCount = Mathf.Max(careDayCount, GetLegacyMinimumCareDays(legacyStageIndex));
        }

        stageIndex = sourceSchemaVersion < 6
            ? Mathf.Clamp(legacyStageIndex + 1, MinimumStageIndex, MaximumStageIndex)
            : Mathf.Clamp(stageIndex, MinimumStageIndex, MaximumStageIndex);

        growthProgress = sourceSchemaVersion < 7
            ? PlantStageProgressionController.GetStageAnchor(stageIndex)
            : Mathf.Clamp01(growthProgress);
        displayedGrowthProgress = sourceSchemaVersion < 8
            ? growthProgress
            : Mathf.Clamp(displayedGrowthProgress, 0f, growthProgress);
        if (sourceSchemaVersion >= 7 && sourceSchemaVersion < 10)
        {
            growthProgress = PlantStageProgressionController.RemapLegacyBondScaleProgress(
                growthProgress);
            displayedGrowthProgress = sourceSchemaVersion < 8
                ? growthProgress
                : Mathf.Clamp(
                    PlantStageProgressionController.RemapLegacyBondScaleProgress(
                        displayedGrowthProgress),
                    0f,
                    growthProgress);
        }

        stageIndex = PlantStageProgressionController.GetStageIndexForProgress(growthProgress);

        schemaVersion = CurrentSchemaVersion;
        vitality = Mathf.Clamp01(vitality);
        hydration = sourceSchemaVersion < 9 ? 0f : Mathf.Clamp01(hydration);
        lightExposure = sourceSchemaVersion < 11 ? 0f : Mathf.Clamp01(lightExposure);
        lightEnvironmentSampled = sourceSchemaVersion >= 11 && lightEnvironmentSampled;
        lightDayUtc = FormatUtcDay(ParseUtcDayOrDefault(lightDayUtc, normalizedNow));
        lightWindowStartUtc = FormatUtc(ParseUtcOrDefault(lightWindowStartUtc, normalizedNow));
        lightBoostsToday = sourceSchemaVersion < 11 ? 0 : Mathf.Max(0, lightBoostsToday);
        bond = Mathf.Clamp01(bond);
        lastInteractionUtc = FormatUtc(ParseUtcOrDefault(lastInteractionUtc, normalizedNow));
        lastSavedUtc = FormatUtc(ParseUtcOrDefault(lastSavedUtc, normalizedNow));
        interactionDayUtc = FormatUtcDay(ParseUtcDayOrDefault(interactionDayUtc, normalizedNow));
        meaningfulInteractionsToday = Mathf.Max(0, meaningfulInteractionsToday);
        lastVisitUtc = FormatUtc(ParseUtcOrDefault(lastVisitUtc, GetLastInteractionUtc(normalizedNow)));
        interactionWindowStartUtc = FormatUtc(ParseUtcOrDefault(interactionWindowStartUtc, normalizedNow));
        careDayCount = Mathf.Max(0, careDayCount);
        lastCareDayUtc = NormalizeOptionalUtcDay(lastCareDayUtc);
        lastCareProgressUtc = NormalizeOptionalUtc(lastCareProgressUtc);
        lastWateredUtc = NormalizeOptionalUtc(lastWateredUtc);
        lastHydrationUpdateUtc = FormatUtc(ParseUtcOrDefault(lastHydrationUpdateUtc, normalizedNow));
        wateringDayUtc = FormatUtcDay(ParseUtcDayOrDefault(wateringDayUtc, normalizedNow));
        wateringWindowStartUtc = FormatUtc(ParseUtcOrDefault(wateringWindowStartUtc, normalizedNow));
        wateringsInCurrentDay = Mathf.Max(0, wateringsInCurrentDay);
        lastStageAdvancedUtc = NormalizeOptionalUtc(lastStageAdvancedUtc);
        return true;
    }

    public DateTime GetLastInteractionUtc(DateTime fallbackUtc)
    {
        return ParseUtcOrDefault(lastInteractionUtc, EnsureUtc(fallbackUtc));
    }

    public DateTime GetLastSavedUtc(DateTime fallbackUtc)
    {
        return ParseUtcOrDefault(lastSavedUtc, EnsureUtc(fallbackUtc));
    }

    public DateTime GetLastVisitUtc(DateTime fallbackUtc)
    {
        return ParseUtcOrDefault(lastVisitUtc, EnsureUtc(fallbackUtc));
    }

    public DateTime GetInteractionWindowStartUtc(DateTime fallbackUtc)
    {
        return ParseUtcOrDefault(interactionWindowStartUtc, EnsureUtc(fallbackUtc));
    }

    public bool IsInteractionDay(DateTime utcNow)
    {
        var now = EnsureUtc(utcNow);
        return string.Equals(interactionDayUtc, FormatUtcDay(now), StringComparison.Ordinal);
    }

    public bool IsCareDay(DateTime utcNow)
    {
        return !string.IsNullOrEmpty(lastCareDayUtc)
            && string.Equals(lastCareDayUtc, FormatUtcDay(utcNow), StringComparison.Ordinal);
    }

    public bool IsStageAdvanceDay(DateTime utcNow)
    {
        return TryParseUtc(lastStageAdvancedUtc, out var advancedUtc)
            && string.Equals(FormatUtcDay(advancedUtc), FormatUtcDay(utcNow), StringComparison.Ordinal);
    }

    public bool CanRegisterCareDay(DateTime utcNow)
    {
        return !TryParseUtc(lastCareDayUtc, out var lastCareUtc)
            || EnsureUtc(utcNow).Date > lastCareUtc.Date;
    }

    public bool CanAdvanceStage(DateTime utcNow)
    {
        return !TryParseUtc(lastStageAdvancedUtc, out var advancedUtc)
            || EnsureUtc(utcNow).Date > advancedUtc.Date;
    }

    public bool TryGetLastWateredUtc(out DateTime wateredUtc)
    {
        return TryParseUtc(lastWateredUtc, out wateredUtc);
    }

    public DateTime GetLastHydrationUpdateUtc(DateTime fallbackUtc)
    {
        return ParseUtcOrDefault(lastHydrationUpdateUtc, EnsureUtc(fallbackUtc));
    }

    public DateTime GetWateringWindowStartUtc(DateTime fallbackUtc)
    {
        return ParseUtcOrDefault(wateringWindowStartUtc, EnsureUtc(fallbackUtc));
    }

    public bool IsWateringDay(DateTime utcNow)
    {
        return string.Equals(wateringDayUtc, FormatUtcDay(utcNow), StringComparison.Ordinal);
    }

    public DateTime GetLightWindowStartUtc(DateTime fallbackUtc)
    {
        return ParseUtcOrDefault(lightWindowStartUtc, EnsureUtc(fallbackUtc));
    }

    public bool IsLightDay(DateTime utcNow)
    {
        return string.Equals(lightDayUtc, FormatUtcDay(utcNow), StringComparison.Ordinal);
    }

    public bool TryGetLastCareProgressUtc(out DateTime careProgressUtc)
    {
        return TryParseUtc(lastCareProgressUtc, out careProgressUtc);
    }

    public bool TryGetLastStageAdvancedUtc(out DateTime stageAdvancedUtc)
    {
        return TryParseUtc(lastStageAdvancedUtc, out stageAdvancedUtc);
    }

    public static string FormatUtc(DateTime value)
    {
        return EnsureUtc(value).ToString("O", CultureInfo.InvariantCulture);
    }

    public static string FormatUtcDay(DateTime value)
    {
        return EnsureUtc(value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static DateTime ParseUtcDayOrDefault(string value, DateTime fallbackUtc)
    {
        if (DateTime.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return EnsureUtc(parsed);
        }

        return EnsureUtc(fallbackUtc);
    }

    private static DateTime ParseUtcOrDefault(string value, DateTime fallbackUtc)
    {
        if (DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return EnsureUtc(parsed);
        }

        return EnsureUtc(fallbackUtc);
    }

    private static string NormalizeOptionalUtc(string value)
    {
        return TryParseUtc(value, out var parsed) ? FormatUtc(parsed) : string.Empty;
    }

    private static string NormalizeOptionalUtcDay(string value)
    {
        if (DateTime.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return FormatUtcDay(parsed);
        }

        return string.Empty;
    }

    private static bool TryParseUtc(string value, out DateTime parsedUtc)
    {
        if (DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            parsedUtc = EnsureUtc(parsed);
            return true;
        }

        parsedUtc = default;
        return false;
    }

    private static int GetLegacyMinimumCareDays(int normalizedStageIndex)
    {
        switch (normalizedStageIndex)
        {
            case 1: return 1;
            case 2: return 3;
            case 3: return 5;
            case 4: return 8;
            default: return 0;
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
