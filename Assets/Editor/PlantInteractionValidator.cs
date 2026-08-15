using System;
using UnityEditor;
using UnityEngine;

public static class PlantInteractionValidator
{
    [MenuItem("BloomPot/Validate Plant Interaction")]
    public static void ValidateFromMenu()
    {
        try
        {
            Debug.Log(ValidateOrThrow());
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    public static string ValidateOrThrow()
    {
        var controller = new PlantInteractionController();
        var now = new DateTime(2026, 8, 12, 12, 0, 0, DateTimeKind.Utc);
        var state = PlantState.CreateDefault(now);
        state.vitality = 0.4f;
        state.bond = 0f;

        var visit = controller.RegisterVisit(state, now);
        Require(visit.Rewarded, "The first visit was not rewarded.");
        Require(Mathf.Abs(state.vitality - 0.44f) < 0.0001f, "Visit vitality recovery was incorrect.");
        Require(Mathf.Abs(state.bond - 0.01f) < 0.0001f, "Visit bond recovery was incorrect.");
        Require(state.careDayCount == 1, "The first rewarded visit did not register a care day.");

        var repeatedVisit = controller.RegisterVisit(state, now.AddHours(1));
        Require(!repeatedVisit.Rewarded, "Visit cooldown did not prevent repeated rewards.");
        var laterVisit = controller.RegisterVisit(state, now.AddHours(4));
        Require(laterVisit.Rewarded, "Visit did not recover after the four-hour cooldown.");
        Require(state.careDayCount == 1, "Repeated same-day visits registered extra care days.");

        var vitalityBeforeTaps = state.vitality;
        for (var index = 0; index < PlantInteractionController.MaximumMeaningfulInteractionsPerUtcDay; index++)
        {
            var tap = controller.RegisterTap(state, now.AddHours(5).AddMinutes(index));
            Require(tap.Rewarded, $"Meaningful tap {index + 1} was unexpectedly rejected.");
        }

        Require(Mathf.Approximately(state.vitality, vitalityBeforeTaps),
            "Tapping restored vitality and overlapped with watering.");

        var limitedTap = controller.RegisterTap(state, now.AddHours(6));
        Require(!limitedTap.Rewarded, "The daily meaningful-interaction limit was not enforced.");
        Require(limitedTap.RemainingMeaningfulInteractions == 0, "Daily remaining count was incorrect.");

        var nextDayTap = controller.RegisterTap(state, now.AddDays(1));
        Require(nextDayTap.Rewarded, "The UTC daily interaction count did not reset.");
        Require(state.careDayCount == 2, "The next-day interaction did not register a care day.");
        Require(
            nextDayTap.RemainingMeaningfulInteractions
                == PlantInteractionController.MaximumMeaningfulInteractionsPerUtcDay - 1,
            "The reset daily remaining count was incorrect.");

        var fullState = PlantState.CreateDefault(now.AddDays(2));
        fullState.vitality = 1f;
        fullState.bond = 1f;
        var fullTap = controller.RegisterTap(fullState, now.AddDays(2));
        Require(fullTap.Rewarded && fullTap.StateChanged, "A rewarded full-state tap was not marked for saving.");

        var legacyState = new PlantState
        {
            schemaVersion = 1,
            stageIndex = 3,
            vitality = 0.63f,
            bond = 0.2f,
            lastInteractionUtc = PlantState.FormatUtc(now.AddDays(-1)),
            lastSavedUtc = PlantState.FormatUtc(now.AddHours(-2))
        };
        Require(legacyState.Normalize(now), "Schema v1 did not migrate.");
        Require(legacyState.schemaVersion == PlantState.CurrentSchemaVersion, "Migrated schema was not current.");
        Require(legacyState.stageIndex == 4, "Migration changed the growth stage.");
        Require(Mathf.Abs(
                legacyState.growthProgress
                - PlantStageProgressionController.GetStageAnchor(4)) < 0.0001f,
            "Legacy migration did not preserve the visible stage growth anchor.");
        Require(Mathf.Approximately(
                legacyState.displayedGrowthProgress,
                legacyState.growthProgress),
            "Legacy migration did not initialize displayed growth from earned growth.");
        Require(Mathf.Abs(legacyState.vitality - 0.63f) < 0.0001f, "Migration changed vitality.");
        Require(Mathf.Approximately(legacyState.hydration, 0f),
            "Legacy migration did not initialize hydration safely.");
        Require(legacyState.meaningfulInteractionsToday == 0, "Migration created interaction count.");
        Require(legacyState.careDayCount == 5, "Schema v1 migration did not preserve stage-equivalent care days.");

        var preSeedState = new PlantState
        {
            schemaVersion = 5,
            stageIndex = 4,
            vitality = 0.8f,
            bond = 0.6f,
            lastInteractionUtc = PlantState.FormatUtc(now),
            lastSavedUtc = PlantState.FormatUtc(now)
        };
        Require(preSeedState.Normalize(now), "Schema v5 did not migrate.");
        Require(preSeedState.stageIndex == 5,
            "Adding Seed changed the visible stage of an existing Bloom plant.");
        Require(Mathf.Approximately(preSeedState.growthProgress, 1f),
            "Existing Bloom migration did not preserve full growth.");

        ValidateWatering(now);
        ValidateHydrationTimeline(now);

        var demoController = new PlantInteractionController(PlantExperienceProfile.PortfolioDemo);
        var demoState = PlantState.CreateDefault(now, PlantExperienceProfile.PortfolioDemo);
        demoState.vitality = 0.4f;
        var demoVisit = demoController.RegisterVisit(demoState, now);
        Require(demoVisit.Rewarded, "The first demo visit was not rewarded.");
        Require(!demoController.RegisterVisit(demoState, now.AddSeconds(19d)).Rewarded,
            "The demo visit cooldown ended before twenty seconds.");
        Require(demoController.RegisterVisit(demoState, now.AddSeconds(20d)).Rewarded,
            "The demo visit cooldown did not end at twenty seconds.");
        Require(demoState.careDayCount == 2,
            "Twenty seconds did not create the second simulated care day.");

        for (var index = 0; index < PlantInteractionController.MaximumMeaningfulInteractionsPerUtcDay; index++)
        {
            Require(demoController.RegisterTap(demoState, now.AddSeconds(21d + index)).Rewarded,
                $"Demo interaction {index + 1} was unexpectedly rejected.");
        }

        Require(!demoController.RegisterTap(demoState, now.AddSeconds(39d)).Rewarded,
            "The demo interaction window did not enforce its limit.");
        Require(demoController.RegisterTap(demoState, now.AddSeconds(40d)).Rewarded,
            "The demo interaction limit did not reset after forty seconds.");
        Require(demoState.careDayCount == 3,
            "Demo interactions did not respect the twenty-second care-day interval.");

        return
            "PLANT_INTERACTION_VALIDATION: PASS\n" +
            "visitCooldown: PASS\n" +
            "dailyTapLimit: PASS\n" +
            "tapBondOnly: PASS\n" +
            "nextDayReset: PASS\n" +
            "fullStatePersistence: PASS\n" +
            "wateringAndCareDays: PASS\n" +
            "continuousHydrationTimeline: PASS\n" +
            "portfolioDemoCooldowns: PASS\n" +
            "schemaV5SeedMigration: PASS\n" +
            "schemaV1Migration: PASS";
    }

    private static void ValidateWatering(DateTime now)
    {
        var controller = new PlantWateringController();
        var state = PlantState.CreateDefault(now);
        state.vitality = 0.5f;
        state.bond = 0.1f;

        var first = controller.RegisterWatering(state, now);
        Require(first.Rewarded && first.CareDayAdded, "The first watering was not rewarded.");
        Require(Mathf.Abs(state.hydration - 0.2f) < 0.0001f,
            "Watering did not add twenty percent hydration.");
        Require(Mathf.Abs(state.vitality - 0.5f) < 0.0001f,
            "Watering directly changed vitality instead of hydration only.");
        Require(Mathf.Abs(state.bond - 0.1f) < 0.0001f,
            "Watering directly changed bond instead of hydration only.");

        for (var watering = 1; watering < PlantWateringController.MaximumWateringsPerDay; watering++)
        {
            Require(controller.RegisterWatering(state, now).Rewarded,
                $"Companion watering {watering + 1} was unexpectedly rejected.");
        }

        Require(Mathf.Approximately(state.hydration, 1f),
            "Five waterings did not fill hydration to one hundred percent.");
        Require(!controller.RegisterWatering(state, now).Rewarded,
            "Companion watering exceeded the five-per-day limit.");
        Require(!controller.CanWater(state, now.AddHours(6d)),
            "Hydration decay bypassed the same-day watering limit.");
        Require(controller.RegisterWatering(state, now.AddDays(1d)).Rewarded,
            "Companion watering count did not reset on the next UTC day.");
        Require(state.careDayCount == 2, "Watering care-day count was incorrect.");

        Require(Mathf.Abs(
                PlantHydrationController.CalculateGrowthMultiplier(0f)
                - PlantHydrationController.MinimumGrowthMultiplier) < 0.0001f,
            "Empty hydration did not apply the configured growth penalty.");
        Require(Mathf.Abs(
                PlantHydrationController.CalculateGrowthMultiplier(
                    PlantHydrationController.NeutralHydration) - 1f) < 0.0001f,
            "Neutral hydration did not produce neutral growth.");
        Require(Mathf.Abs(
                PlantHydrationController.CalculateGrowthMultiplier(1f)
                - PlantHydrationController.MaximumGrowthMultiplier) < 0.0001f,
            "Full hydration did not apply the configured small growth bonus.");

        var futureWatering = PlantState.CreateDefault(now);
        futureWatering.lastWateredUtc = PlantState.FormatUtc(now.AddHours(1d));
        Require(!controller.CanWater(futureWatering, now),
            "Clock rollback incorrectly made watering available.");

        var demoController = new PlantWateringController(PlantExperienceProfile.PortfolioDemo);
        var demoState = PlantState.CreateDefault(now, PlantExperienceProfile.PortfolioDemo);
        for (var watering = 0; watering < PlantWateringController.MaximumWateringsPerDay; watering++)
        {
            Require(demoController.RegisterWatering(demoState, now).Rewarded,
                $"Demo watering {watering + 1} was unexpectedly rejected.");
        }

        Require(!demoController.CanWater(demoState, now.AddSeconds(5d)),
            "Demo watering exceeded five uses inside one simulated day.");
        var nextDemoDayWatering = demoController.RegisterWatering(demoState, now.AddSeconds(20d));
        Require(nextDemoDayWatering.Rewarded && nextDemoDayWatering.CareDayAdded,
            "The demo watering count did not reset after twenty seconds.");
        Require(demoState.careDayCount == 2,
            "Demo watering care-day count was incorrect.");
    }

    private static void ValidateHydrationTimeline(DateTime now)
    {
        var controller = new PlantHydrationController();
        var state = PlantState.CreateDefault(now);
        state.hydration = 1f;
        controller.ApplyTimeProgress(state, now.AddHours(6d));
        Require(Mathf.Abs(state.hydration - 0.95f) < 0.0001f,
            "Companion hydration did not decline continuously at six hours.");
        controller.ApplyTimeProgress(state, now.AddHours(12d));
        Require(Mathf.Abs(state.hydration - 0.9f) < 0.0001f,
            "Companion hydration did not decline ten percent per half-day.");
        controller.ApplyTimeProgress(state, now.AddHours(24d));
        Require(Mathf.Abs(state.hydration - 0.8f) < 0.0001f,
            "Companion hydration full-day decline was incorrect.");

        var demoController = new PlantHydrationController(PlantExperienceProfile.PortfolioDemo);
        var demoState = PlantState.CreateDefault(now, PlantExperienceProfile.PortfolioDemo);
        demoState.hydration = 1f;
        demoController.ApplyTimeProgress(demoState, now.AddSeconds(5d));
        Require(Mathf.Abs(demoState.hydration - 0.95f) < 0.0001f,
            "Demo hydration did not visibly decline between half-day boundaries.");
        demoController.ApplyTimeProgress(demoState, now.AddSeconds(10d));
        Require(Mathf.Abs(demoState.hydration - 0.9f) < 0.0001f,
            "Demo hydration did not decline ten percent per simulated half-day.");
        demoController.ApplyTimeProgress(demoState, now.AddSeconds(20d));
        Require(Mathf.Abs(demoState.hydration - 0.8f) < 0.0001f,
            "Demo hydration full-day decline was incorrect.");

        var rollbackState = PlantState.CreateDefault(now);
        rollbackState.hydration = 0.7f;
        rollbackState.lastHydrationUpdateUtc = PlantState.FormatUtc(now.AddHours(1d));
        controller.ApplyTimeProgress(rollbackState, now);
        controller.ApplyTimeProgress(rollbackState, now.AddHours(1d));
        Require(Mathf.Abs(rollbackState.hydration - 0.7f) < 0.0001f,
            "Clock rollback caused hydration to be deducted twice.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"PLANT_INTERACTION_VALIDATION: FAIL - {message}");
        }
    }
}
