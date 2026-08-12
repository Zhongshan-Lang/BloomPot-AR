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

        var repeatedVisit = controller.RegisterVisit(state, now.AddHours(1));
        Require(!repeatedVisit.Rewarded, "Visit cooldown did not prevent repeated rewards.");
        var laterVisit = controller.RegisterVisit(state, now.AddHours(4));
        Require(laterVisit.Rewarded, "Visit did not recover after the four-hour cooldown.");

        for (var index = 0; index < PlantInteractionController.MaximumMeaningfulInteractionsPerUtcDay; index++)
        {
            var tap = controller.RegisterTap(state, now.AddHours(5).AddMinutes(index));
            Require(tap.Rewarded, $"Meaningful tap {index + 1} was unexpectedly rejected.");
        }

        var limitedTap = controller.RegisterTap(state, now.AddHours(6));
        Require(!limitedTap.Rewarded, "The daily meaningful-interaction limit was not enforced.");
        Require(limitedTap.RemainingMeaningfulInteractions == 0, "Daily remaining count was incorrect.");

        var nextDayTap = controller.RegisterTap(state, now.AddDays(1));
        Require(nextDayTap.Rewarded, "The UTC daily interaction count did not reset.");
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
        Require(legacyState.stageIndex == 3, "Migration changed the growth stage.");
        Require(Mathf.Abs(legacyState.vitality - 0.63f) < 0.0001f, "Migration changed vitality.");
        Require(legacyState.meaningfulInteractionsToday == 0, "Migration created interaction count.");

        var demoController = new PlantInteractionController(PlantExperienceProfile.PortfolioDemo);
        var demoState = PlantState.CreateDefault(now, PlantExperienceProfile.PortfolioDemo);
        demoState.vitality = 0.4f;
        var demoVisit = demoController.RegisterVisit(demoState, now);
        Require(demoVisit.Rewarded, "The first demo visit was not rewarded.");
        Require(!demoController.RegisterVisit(demoState, now.AddSeconds(19d)).Rewarded,
            "The demo visit cooldown ended before twenty seconds.");
        Require(demoController.RegisterVisit(demoState, now.AddSeconds(20d)).Rewarded,
            "The demo visit cooldown did not end at twenty seconds.");

        for (var index = 0; index < PlantInteractionController.MaximumMeaningfulInteractionsPerUtcDay; index++)
        {
            Require(demoController.RegisterTap(demoState, now.AddSeconds(21d + index)).Rewarded,
                $"Demo interaction {index + 1} was unexpectedly rejected.");
        }

        Require(!demoController.RegisterTap(demoState, now.AddSeconds(30d)).Rewarded,
            "The demo interaction window did not enforce its limit.");
        Require(demoController.RegisterTap(demoState, now.AddSeconds(61d)).Rewarded,
            "The demo interaction limit did not reset after sixty seconds.");

        return
            "PLANT_INTERACTION_VALIDATION: PASS\n" +
            "visitCooldown: PASS\n" +
            "dailyTapLimit: PASS\n" +
            "nextDayReset: PASS\n" +
            "fullStatePersistence: PASS\n" +
            "portfolioDemoCooldowns: PASS\n" +
            "schemaV1Migration: PASS";
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"PLANT_INTERACTION_VALIDATION: FAIL - {message}");
        }
    }
}
