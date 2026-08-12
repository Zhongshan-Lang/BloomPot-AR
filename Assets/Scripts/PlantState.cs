using System;
using System.Globalization;
using UnityEngine;

[Serializable]
public sealed class PlantState
{
    public const int CurrentSchemaVersion = 3;
    public const int MinimumStageIndex = 0;
    public const int MaximumStageIndex = 4;

    public int schemaVersion = CurrentSchemaVersion;
    public int stageIndex;
    public float vitality = 1f;
    public float bond;
    public string lastInteractionUtc;
    public string lastSavedUtc;
    public string interactionDayUtc;
    public int meaningfulInteractionsToday;
    public string lastVisitUtc;
    public string interactionWindowStartUtc;

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
            vitality = 1f,
            bond = 0f,
            lastInteractionUtc = FormatUtc(normalizedNow),
            lastSavedUtc = FormatUtc(normalizedNow),
            interactionDayUtc = FormatUtcDay(normalizedNow),
            meaningfulInteractionsToday = 0,
            lastVisitUtc = FormatUtc(normalizedNow.Subtract(resolvedProfile.VisitRewardInterval)),
            interactionWindowStartUtc = FormatUtc(normalizedNow)
        };
    }

    public PlantState Copy()
    {
        return new PlantState
        {
            schemaVersion = schemaVersion,
            stageIndex = stageIndex,
            vitality = vitality,
            bond = bond,
            lastInteractionUtc = lastInteractionUtc,
            lastSavedUtc = lastSavedUtc,
            interactionDayUtc = interactionDayUtc,
            meaningfulInteractionsToday = meaningfulInteractionsToday,
            lastVisitUtc = lastVisitUtc,
            interactionWindowStartUtc = interactionWindowStartUtc
        };
    }

    public bool Normalize(DateTime utcNow)
    {
        if (schemaVersion > CurrentSchemaVersion)
        {
            return false;
        }

        var normalizedNow = EnsureUtc(utcNow);
        schemaVersion = CurrentSchemaVersion;
        stageIndex = Mathf.Clamp(stageIndex, MinimumStageIndex, MaximumStageIndex);
        vitality = Mathf.Clamp01(vitality);
        bond = Mathf.Clamp01(bond);
        lastInteractionUtc = FormatUtc(ParseUtcOrDefault(lastInteractionUtc, normalizedNow));
        lastSavedUtc = FormatUtc(ParseUtcOrDefault(lastSavedUtc, normalizedNow));
        interactionDayUtc = FormatUtcDay(ParseUtcDayOrDefault(interactionDayUtc, normalizedNow));
        meaningfulInteractionsToday = Mathf.Max(0, meaningfulInteractionsToday);
        lastVisitUtc = FormatUtc(ParseUtcOrDefault(lastVisitUtc, GetLastInteractionUtc(normalizedNow)));
        interactionWindowStartUtc = FormatUtc(ParseUtcOrDefault(interactionWindowStartUtc, normalizedNow));
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
