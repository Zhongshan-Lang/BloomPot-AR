using System;
using UnityEditor;
using UnityEngine;

public static class PlantLightValidator
{
    [MenuItem("BloomPot/Validate Plant Light")]
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
        var now = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
        var controller = new PlantLightController();
        var state = PlantState.CreateDefault(now);
        Require(Mathf.Approximately(state.lightExposure, 0f)
                && !state.lightEnvironmentSampled
                && state.lightBoostsToday == 0,
            "A new day did not begin with zero unsampled light.");

        var capture = controller.CaptureEnvironmentLight(state, 0.6f, now);
        Require(capture.Captured && Mathf.Abs(state.lightExposure - 0.6f) < 0.0001f,
            "Environment light was not captured.");
        Require(!controller.CaptureEnvironmentLight(state, 0.9f, now.AddMinutes(1d)).Captured
                && Mathf.Abs(state.lightExposure - 0.6f) < 0.0001f,
            "A second environment reading overwrote the daily initial value.");

        for (var boost = 0; boost < PlantLightController.MaximumBoostsPerDay; boost++)
        {
            Require(controller.AddLightBoost(state, now).Added,
                $"Light boost {boost + 1} was unexpectedly rejected.");
        }

        Require(Mathf.Abs(state.lightExposure - 0.9f) < 0.0001f,
            "Six boosts did not add thirty percent light.");
        Require(!controller.AddLightBoost(state, now).Added,
            "The daily light boost limit was not enforced.");

        var fullState = PlantState.CreateDefault(now);
        fullState.lightExposure = 0.98f;
        fullState.lightEnvironmentSampled = true;
        Require(controller.AddLightBoost(fullState, now).Added
                && Mathf.Approximately(fullState.lightExposure, 1f)
                && !controller.CanAddLightBoost(fullState, now),
            "Light did not clamp at one hundred percent.");

        Require(controller.RefreshDay(state, now.AddDays(1d))
                && Mathf.Approximately(state.lightExposure, 0f)
                && !state.lightEnvironmentSampled
                && state.lightBoostsToday == 0,
            "Companion light did not reset on the next day.");

        var demoController = new PlantLightController(PlantExperienceProfile.PortfolioDemo);
        var demoState = PlantState.CreateDefault(now, PlantExperienceProfile.PortfolioDemo);
        demoController.CaptureEnvironmentLight(demoState, 0.8f, now);
        Require(!demoController.RefreshDay(demoState, now.AddSeconds(19d)),
            "Demo light reset before one simulated day.");
        Require(demoController.RefreshDay(demoState, now.AddSeconds(20d))
                && !demoState.lightEnvironmentSampled,
            "Demo light did not reset after twenty seconds.");

        Require(Mathf.Abs(PlantLightController.CalculateGrowthMultiplier(0f) - 0.7f) < 0.0001f,
            "Zero light did not produce seventy percent growth speed.");
        Require(Mathf.Abs(
                PlantLightController.CalculateGrowthMultiplier(PlantLightController.NeutralLight)
                - 1f) < 0.0001f,
            "Seventy percent light was not neutral.");
        Require(Mathf.Abs(PlantLightController.CalculateGrowthMultiplier(1f) - 1.2f) < 0.0001f,
            "Full light did not produce one hundred twenty percent growth speed.");

        var lowGrowth = PlantStageProgressionController.CalculateGrowthPotential(3, 0.2f, 1f, 0f);
        var neutralGrowth = PlantStageProgressionController.CalculateGrowthPotential(
            3, 0.2f, 1f, PlantLightController.NeutralLight);
        var brightGrowth = PlantStageProgressionController.CalculateGrowthPotential(3, 0.2f, 1f, 1f);
        Require(lowGrowth < neutralGrowth && brightGrowth > neutralGrowth,
            "Light did not inhibit and promote continuous growth potential.");

        Require(Mathf.Abs(
                PlantLightController.NormalizeIntensityCorrection(0.466f)
                - PlantLightController.NeutralLight) < 0.0001f,
            "ARCore middle gray did not map to neutral light.");
        Require(Mathf.Abs(PlantLightController.NormalizeAmbientIntensity(1500f) - 1f) < 0.0001f,
            "Bright ARKit illumination did not map to full light.");

        return
            "PLANT_LIGHT_VALIDATION: PASS\n" +
            "dailyReset: PASS\n" +
            "environmentCapture: PASS\n" +
            "sixBoostLimit: PASS\n" +
            "growthMultiplier: PASS\n" +
            "sensorNormalization: PASS";
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"PLANT_LIGHT_VALIDATION: FAIL - {message}");
        }
    }
}
