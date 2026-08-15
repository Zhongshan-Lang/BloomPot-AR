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
            Mathf.Abs(oneDayInactive.vitality - 0.84f) < 0.0001f,
            $"One dry inactive day produced vitality {oneDayInactive.vitality}, expected 0.84.");
        Require(Mathf.Abs(oneDayResult.InactivityVitalityLoss - 0.08f) < 0.0001f
                && Mathf.Abs(oneDayResult.HydrationVitalityChange + 0.08f) < 0.0001f,
            "Dryness and inactivity were not combined independently.");
        Require(oneDayResult.WiltAmount > 0f && oneDayResult.WiltAmount < 0.01f,
            "One dry inactive day did not produce the expected subtle initial wilt.");

        var incremental = CreateState(now.AddDays(-10), now.AddDays(-2), 0.8f);
        controller.ApplyOfflineProgress(incremental, now);
        Require(
            Mathf.Abs(incremental.vitality - 0.48f) < 0.0001f,
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
        Require(Mathf.Abs(demoState.vitality - 0.86f) < 0.0001f,
            "Demo twenty-second dry vitality did not combine both penalties.");
        demoController.ApplyRealtimeProgress(demoState, now.AddSeconds(50d));
        Require(Mathf.Abs(demoState.vitality - 0.44f) < 0.0001f,
            "Demo fifty-second dry vitality was incorrect.");

        demoState.lastInteractionUtc = PlantState.FormatUtc(now.AddSeconds(50d));
        demoController.ApplyRealtimeProgress(demoState, now.AddSeconds(59d));
        Require(Mathf.Abs(demoState.vitality - 0.44f) < 0.0001f,
            "A demo interaction did not restart protection from negative changes.");
        demoController.ApplyRealtimeProgress(demoState, now.AddSeconds(120d));
        Require(demoState.vitality < 0.0001f,
            $"Demo vitality stopped at {demoState.vitality:F8} instead of reaching zero.");

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

        ValidateHydrationVitality(now);
        ValidateStageProgression(now);

        return
            "PLANT_GROWTH_VALIDATION: PASS\n" +
            "gracePeriod: PASS\n" +
            "incrementalOfflineDecay: PASS\n" +
            "fullVitalityRange: PASS\n" +
            "clockRollback: PASS\n" +
            "portfolioDemoTimeline: PASS\n" +
            "hydrationVitalityCoupling: PASS\n" +
            "wiltAndAppearanceMapping: PASS\n" +
            "stageProgression: PASS";
    }

    private static void ValidateHydrationVitality(DateTime now)
    {
        Require(Mathf.Abs(
                PlantGrowthController.CalculateHydrationVitalityRatePerDay(0f) + 0.08f)
                < 0.0001f,
            "Zero hydration did not produce eight percent daily vitality loss.");
        Require(Mathf.Abs(
                PlantGrowthController.CalculateHydrationVitalityRatePerDay(0.3f) + 0.04f)
                < 0.0001f,
            "Thirty percent hydration did not produce four percent daily vitality loss.");
        Require(Mathf.Approximately(
                PlantGrowthController.CalculateHydrationVitalityRatePerDay(
                    PlantHydrationController.NeutralHydration),
                0f),
            "Sixty percent hydration was not vitality-neutral.");
        Require(Mathf.Abs(
                PlantGrowthController.CalculateHydrationVitalityRatePerDay(0.8f) - 0.03f)
                < 0.0001f,
            "Eighty percent hydration did not produce three percent daily recovery.");
        Require(Mathf.Abs(
                PlantGrowthController.CalculateHydrationVitalityRatePerDay(1f) - 0.06f)
                < 0.0001f,
            "Full hydration did not produce six percent daily recovery.");

        var controller = new PlantGrowthController();
        Require(Mathf.Abs(
                controller.CalculateHydrationVitalityChange(0f, TimeSpan.FromDays(1d))
                + 0.08f) < 0.0001f,
            "A full dry day did not integrate to eight percent vitality loss.");
        Require(Mathf.Abs(
                controller.CalculateHydrationVitalityChange(1f, TimeSpan.FromDays(1d))
                - 0.045f) < 0.0001f,
            "Full hydration did not account for continuous daily water loss.");

        var protectedDryState = CreateState(now, now, 0.5f);
        protectedDryState.hydration = 0f;
        controller.ApplyOfflineProgress(protectedDryState, now.AddHours(12d));
        Require(Mathf.Abs(protectedDryState.vitality - 0.5f) < 0.0001f,
            "Dryness reduced vitality during the companion protection period.");

        var recoveringState = CreateState(now, now, 0.5f);
        recoveringState.hydration = 1f;
        var recovery = controller.ApplyOfflineProgress(recoveringState, now.AddHours(12d));
        Require(Mathf.Abs(recoveringState.vitality - 0.52625f) < 0.0001f
                && recovery.HydrationVitalityChange > 0f,
            "High hydration did not recover vitality continuously during protection.");

        var ignoredButWateredState = CreateState(now.AddHours(-12d), now, 0.5f);
        ignoredButWateredState.hydration = 1f;
        controller.ApplyOfflineProgress(ignoredButWateredState, now.AddDays(1d));
        Require(Mathf.Abs(ignoredButWateredState.vitality - 0.465f) < 0.0001f,
            "Full hydration completely replaced the need for interaction.");

        var realtimeState = CreateState(now.AddHours(-13d), now, 0.5f);
        realtimeState.hydration = 0f;
        controller.ResetRealtime(now);
        controller.ApplyRealtimeProgress(realtimeState, now.AddHours(1d));
        Require(Mathf.Abs(realtimeState.vitality - 0.49333334f) < 0.0001f,
            "Companion realtime did not combine dryness and inactivity consistently.");
    }

    private static void ValidateStageProgression(DateTime now)
    {
        var progression = new PlantStageProgressionController();
        var expectedBondRequirements = new[] { 0f, 0.08f, 0.25f, 0.45f, 0.7f, 0.9f };
        for (var stageIndex = 0; stageIndex < expectedBondRequirements.Length; stageIndex++)
        {
            Require(Mathf.Abs(
                    PlantStageProgressionController.GetRequiredBond(stageIndex)
                    - expectedBondRequirements[stageIndex]) < 0.0001f,
                $"Stage index {stageIndex} used an unexpected bond requirement.");
        }

        var legacyHalfBloomAnchor = Mathf.Pow(6f / 8f, 0.6f)
            * Mathf.Pow(0.35f / 0.5f, 0.4f);
        var legacyBudAnchor = Mathf.Pow(4f / 8f, 0.6f)
            * Mathf.Pow(0.2f / 0.5f, 0.4f);
        var legacyState = new PlantState
        {
            schemaVersion = 9,
            stageIndex = 4,
            growthProgress = legacyHalfBloomAnchor,
            displayedGrowthProgress = legacyBudAnchor,
            vitality = 1f,
            hydration = 0.5f,
            bond = 0.7f,
            lastInteractionUtc = PlantState.FormatUtc(now),
            lastSavedUtc = PlantState.FormatUtc(now)
        };
        Require(legacyState.Normalize(now), "Schema v9 growth state did not migrate.");
        Require(legacyState.stageIndex == 4
                && Mathf.Abs(
                    legacyState.growthProgress
                    - PlantStageProgressionController.GetStageAnchor(4)) < 0.0001f
                && Mathf.Abs(
                    legacyState.displayedGrowthProgress
                    - PlantStageProgressionController.GetStageAnchor(3)) < 0.0001f,
            "Higher bond requirements changed an existing plant's visible milestones.");

        var state = PlantState.CreateDefault(now);
        state.lightExposure = PlantLightController.NeutralLight;
        state.lightEnvironmentSampled = true;
        state.hydration = PlantHydrationController.NeutralHydration;
        state.careDayCount = 1;
        state.bond = PlantStageProgressionController.GetRequiredBond(1);
        state.vitality = 0.69f;

        var lowVitality = progression.EvaluateProgress(state, now);
        Require(state.stageIndex == 0
                && lowVitality.CurrentProgress
                    < PlantStageProgressionController.GetStageAnchor(1),
            "A low-vitality seed reached the Sprout anchor.");

        state.vitality = PlantStageProgressionController.MinimumGrowthVitality;
        var sprout = progression.EvaluateProgress(state, now);
        Require(sprout.StageChanged && state.stageIndex == 1,
            "Seed did not advance to Sprout at all three thresholds.");

        var highBondFirstDay = PlantStageProgressionController.CalculateGrowthPotential(1, 1f, 1f);
        Require(Mathf.Abs(
                highBondFirstDay
                - PlantStageProgressionController.GetStageAnchor(1)) < 0.0001f,
            "High bond allowed the first care day to skip beyond Sprout.");

        var hydrationFactors = PlantStageProgressionController.CalculateGrowthFactors(
            8,
            0.35f,
            1f,
            PlantHydrationController.NeutralHydration,
            PlantLightController.NeutralLight);
        Require(Mathf.Abs(hydrationFactors.HydrationMultiplier - 1f) < 0.0001f
                && Mathf.Abs(hydrationFactors.ConditionMultiplier - 1f) < 0.0001f,
            "Neutral plant conditions did not preserve base growth.");
        var dryGrowth = PlantStageProgressionController.CalculateGrowthPotential(
            8, 0.35f, 1f, 0f, PlantLightController.NeutralLight);
        var neutralHydrationGrowth = PlantStageProgressionController.CalculateGrowthPotential(
            8,
            0.35f,
            1f,
            PlantHydrationController.NeutralHydration,
            PlantLightController.NeutralLight);
        var fullHydrationGrowth = PlantStageProgressionController.CalculateGrowthPotential(
            8, 0.35f, 1f, 1f, PlantLightController.NeutralLight);
        Require(dryGrowth < neutralHydrationGrowth && fullHydrationGrowth > neutralHydrationGrowth,
            "Hydration did not inhibit and promote continuous growth potential.");
        var thirdDayCeiling = PlantStageProgressionController.GetCareDayGrowthCeiling(3);
        var expectedThirdDayCeiling = Mathf.Lerp(
            PlantStageProgressionController.GetStageAnchor(2),
            PlantStageProgressionController.GetStageAnchor(3),
            0.5f);
        Require(Mathf.Abs(thirdDayCeiling - expectedThirdDayCeiling) < 0.0001f,
            "The third care day did not stop midway between Leafing and Bud.");

        var catchUpProfile = PlantExperienceProfile.PortfolioDemo;
        var catchUpProgression = new PlantStageProgressionController(catchUpProfile);
        var catchUpState = PlantState.CreateDefault(now, catchUpProfile);
        catchUpState.lightExposure = PlantLightController.NeutralLight;
        catchUpState.lightEnvironmentSampled = true;
        catchUpState.hydration = PlantHydrationController.NeutralHydration;
        catchUpState.careDayCount = 8;
        catchUpState.bond = 1f;
        catchUpState.vitality = 1f;
        Require(catchUpProgression.EvaluateProgress(catchUpState, now).CurrentStageIndex == 1,
            "Accumulated care skipped more than one milestone during recovery.");
        Require(!catchUpProgression.EvaluateProgress(catchUpState, now.AddSeconds(19d)).ProgressChanged,
            "Demo recovery crossed a second milestone before one simulated day elapsed.");
        Require(catchUpProgression.EvaluateProgress(catchUpState, now.AddSeconds(20d)).CurrentStageIndex == 2,
            "Demo recovery did not unlock the next milestone after one simulated day.");

        for (var stageIndex = 2; stageIndex <= PlantState.MaximumStageIndex; stageIndex++)
        {
            state.careDayCount = PlantStageProgressionController.GetRequiredCareDays(stageIndex);
            state.bond = PlantStageProgressionController.GetRequiredBond(stageIndex);
            state.vitality = PlantStageProgressionController.MinimumGrowthVitality;
            var result = progression.EvaluateProgress(state, now.AddDays(stageIndex));
            Require(result.StageChanged && state.stageIndex == stageIndex,
                $"Growth did not reach stage index {stageIndex} at its configured anchor.");
            Require(Mathf.Abs(
                    state.growthProgress
                    - PlantStageProgressionController.GetStageAnchor(stageIndex)) < 0.0001f,
                $"Stage index {stageIndex} did not land on its growth anchor.");
        }

        var continuousState = PlantState.CreateDefault(now);
        continuousState.lightExposure = PlantLightController.NeutralLight;
        continuousState.lightEnvironmentSampled = true;
        continuousState.hydration = PlantHydrationController.NeutralHydration;
        continuousState.careDayCount = 1;
        continuousState.bond = PlantStageProgressionController.GetRequiredBond(1);
        continuousState.vitality = 1f;
        progression.EvaluateProgress(continuousState, now);
        continuousState.careDayCount = 2;
        continuousState.bond = PlantStageProgressionController.GetRequiredBond(2);
        progression.EvaluateProgress(continuousState, now.AddDays(1d));
        continuousState.careDayCount = 3;
        continuousState.bond = 0.35f;
        progression.EvaluateProgress(continuousState, now.AddDays(2d));
        Require(
            continuousState.growthProgress > PlantStageProgressionController.GetStageAnchor(2)
            && continuousState.growthProgress < PlantStageProgressionController.GetStageAnchor(3),
            "Growth potential did not produce a value between Leafing and Bud.");

        var retainedProgress = continuousState.growthProgress;
        continuousState.vitality = 0f;
        continuousState.careDayCount = 8;
        continuousState.bond = 1f;
        var unhealthy = progression.EvaluateProgress(continuousState, now.AddDays(1d));
        Require(!unhealthy.ProgressChanged
                && Mathf.Approximately(continuousState.growthProgress, retainedProgress),
            "Low vitality regressed or advanced previously earned growth.");

        var careState = PlantState.CreateDefault(now);
        Require(PlantStageProgressionController.RegisterCareDay(careState, now),
            "The first care action did not register a care day.");
        Require(!PlantStageProgressionController.RegisterCareDay(careState, now.AddHours(4d)),
            "Repeated care on one UTC day increased careDayCount.");
        Require(PlantStageProgressionController.RegisterCareDay(careState, now.AddDays(1d)),
            "Care on the next UTC day did not increase careDayCount.");
        Require(careState.careDayCount == 2, "Care-day counting was incorrect.");

        var futureCareState = PlantState.CreateDefault(now);
        futureCareState.lastCareDayUtc = PlantState.FormatUtcDay(now.AddDays(1d));
        futureCareState.careDayCount = 1;
        Require(!PlantStageProgressionController.RegisterCareDay(futureCareState, now),
            "Clock rollback registered an extra care day.");

        ValidateDemoStageProgression(now);
    }

    private static void ValidateDemoStageProgression(DateTime now)
    {
        var profile = PlantExperienceProfile.PortfolioDemo;
        var state = PlantState.CreateDefault(now, profile);
        state.vitality = 1f;
        state.bond = 1f;

        Require(PlantStageProgressionController.RegisterCareDay(state, now, profile),
            "The first demo care action did not register a simulated day.");
        Require(!PlantStageProgressionController.RegisterCareDay(state, now.AddSeconds(19d), profile),
            "A demo care day completed before twenty seconds.");
        Require(PlantStageProgressionController.RegisterCareDay(state, now.AddSeconds(20d), profile),
            "The second demo care day did not open after twenty seconds.");
        Require(state.careDayCount == 2,
            "Two demo care periods did not equal two simulated companion days.");

        state.lastCareProgressUtc = PlantState.FormatUtc(now.AddSeconds(60d));
        Require(!PlantStageProgressionController.RegisterCareDay(state, now.AddSeconds(59d), profile),
            "Clock rollback registered a demo care day.");
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
            lastSavedUtc = PlantState.FormatUtc(lastSavedUtc),
            lastHydrationUpdateUtc = PlantState.FormatUtc(lastSavedUtc)
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
