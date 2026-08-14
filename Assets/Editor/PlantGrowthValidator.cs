using System;
using UnityEditor;
using UnityEngine;

public static class PlantGrowthValidator
{
    [MenuItem("BloomPot/Validate Plant Growth")]
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
        var controller = new PlantGrowthController();
        var now = new DateTime(2026, 8, 12, 12, 0, 0, DateTimeKind.Utc);

        var withinGrace = CreateState(now.AddHours(-6), now.AddHours(-6), 1f);
        var graceResult = controller.ApplyOfflineProgress(withinGrace, now);
        Require(!graceResult.VitalityChanged, "Vitality changed during the 12-hour grace period.");
        Require(Mathf.Approximately(graceResult.WiltAmount, 0f), "A healthy plant wilted during grace.");

        var oneDayInactive = CreateState(now.AddHours(-36), now.AddHours(-36), 1f);
        var oneDayResult = controller.ApplyOfflineProgress(oneDayInactive, now);
        Require(
            Mathf.Abs(oneDayInactive.vitality - 0.92f) < 0.0001f,
            $"One inactive day produced vitality {oneDayInactive.vitality}, expected 0.92.");
        Require(Mathf.Approximately(oneDayResult.WiltAmount, 0f), "Minor inactivity caused visible wilt too early.");

        var incremental = CreateState(now.AddDays(-10), now.AddDays(-2), 0.8f);
        controller.ApplyOfflineProgress(incremental, now);
        Require(
            Mathf.Abs(incremental.vitality - 0.64f) < 0.0001f,
            "Offline decay was not incremental from lastSavedUtc.");

        var longInactive = CreateState(now.AddDays(-100), now.AddDays(-100), 1f);
        var longResult = controller.ApplyOfflineProgress(longInactive, now);
        Require(
            Mathf.Approximately(longInactive.vitality, 0f),
            "Long-term companion vitality did not continue below 35% to zero.");
        Require(
            Mathf.Abs(longResult.WiltAmount - PlantGrowthController.MaximumCompanionWilt) < 0.0001f,
            "Long inactivity did not clamp to the companion wilt maximum.");

        var futureClock = CreateState(now.AddHours(2), now.AddHours(2), 0.7f);
        controller.ApplyOfflineProgress(futureClock, now);
        Require(Mathf.Abs(futureClock.vitality - 0.7f) < 0.0001f, "Clock rollback reduced vitality.");

        var demoController = new PlantGrowthController(PlantExperienceProfile.PortfolioDemo);
        var demoState = CreateState(now, now, 1f);
        demoController.ResetRealtime(now);
        demoController.ApplyRealtimeProgress(demoState, now.AddSeconds(9d));
        Require(Mathf.Approximately(demoState.vitality, 1f), "Demo vitality changed during its ten-second grace period.");
        demoController.ApplyRealtimeProgress(demoState, now.AddSeconds(20d));
        Require(Mathf.Abs(demoState.vitality - 0.9f) < 0.0001f, "Demo twenty-second vitality was not 90%.");
        demoController.ApplyRealtimeProgress(demoState, now.AddSeconds(50d));
        Require(Mathf.Abs(demoState.vitality - 0.6f) < 0.0001f, "Demo fifty-second vitality was not 60%.");

        demoState.lastInteractionUtc = PlantState.FormatUtc(now.AddSeconds(50d));
        demoController.ApplyRealtimeProgress(demoState, now.AddSeconds(59d));
        Require(Mathf.Abs(demoState.vitality - 0.6f) < 0.0001f, "A demo interaction did not restart the grace period.");
        demoController.ApplyRealtimeProgress(demoState, now.AddSeconds(120d));
        Require(Mathf.Approximately(demoState.vitality, 0f), "Demo vitality did not continue below 35% to zero.");

        for (var index = 0; index <= 100; index++)
        {
            var vitality = index / 100f;
            var wilt = controller.CalculateWilt(vitality);
            var appearanceDecay = controller.CalculateAppearanceDecay(vitality);
            Require(
                wilt >= 0f && wilt <= PlantGrowthController.MaximumCompanionWilt,
                "Wilt mapping left its supported range.");
            Require(
                appearanceDecay >= 0f && appearanceDecay <= 1f,
                "Appearance decay mapping left its supported range.");
        }

        Require(
            Mathf.Approximately(
                controller.CalculateWilt(PlantGrowthController.FullWiltVitalityThreshold),
                controller.CalculateWilt(0f)),
            "Wilt deformation did not remain clamped below 25% vitality.");
        Require(
            Mathf.Abs(controller.CalculateWilt(0f) - 0.99f) < 0.0001f,
            "Companion wilt did not reach the increased 0.99 maximum.");
        Require(
            Mathf.Abs(
                controller.CalculateAppearanceDecay(
                    PlantGrowthController.CriticalAppearanceVitalityThreshold)
                - PlantGrowthController.AppearanceDecayAtCriticalThreshold) < 0.0001f,
            "Appearance decay did not preserve the existing value at 35% vitality.");
        Require(
            Mathf.Approximately(controller.CalculateAppearanceDecay(0f), 1f),
            "Appearance decay did not reach full strength at zero vitality.");

        return
            "PLANT_GROWTH_VALIDATION: PASS\n" +
            "gracePeriod: PASS\n" +
            "incrementalOfflineDecay: PASS\n" +
            "fullVitalityRange: PASS\n" +
            "clockRollback: PASS\n" +
            "portfolioDemoTimeline: PASS\n" +
            "wiltAndAppearanceMapping: PASS";
    }

    private static PlantState CreateState(
        DateTime lastInteractionUtc,
        DateTime lastSavedUtc,
        float vitality)
    {
        return new PlantState
        {
            schemaVersion = PlantState.CurrentSchemaVersion,
            stageIndex = 0,
            vitality = vitality,
            bond = 0f,
            lastInteractionUtc = PlantState.FormatUtc(lastInteractionUtc),
            lastSavedUtc = PlantState.FormatUtc(lastSavedUtc)
        };
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"PLANT_GROWTH_VALIDATION: FAIL - {message}");
        }
    }
}
