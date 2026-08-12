using System;
using UnityEditor;
using UnityEngine;

public static class PlantLifeAnimationValidator
{
    [MenuItem("BloomPot/Validate Plant Life Animation")]
    public static void Validate()
    {
        var healthy = Sample(true, 1f, 12f);
        var depleted = Sample(true, 0f, 12f);
        var disabled = new PlantLifeAnimationController();

        Require(healthy.MaximumScaleDeviation <= PlantLifeAnimationController.MaximumScaleAmplitude + 0.0001f,
            "Healthy breathing exceeded the scale safety boundary.");
        Require(healthy.MaximumPitch <= PlantLifeAnimationController.MaximumPitchAmplitude + 0.001f,
            "Healthy breathing exceeded the pitch safety boundary.");
        Require(healthy.MaximumYaw <= PlantLifeAnimationController.MaximumYawAmplitude + 0.001f,
            "Healthy breathing exceeded the yaw safety boundary.");
        Require(healthy.MaximumRoll <= PlantLifeAnimationController.MaximumRollAmplitude + 0.001f,
            "Healthy breathing exceeded the roll safety boundary.");
        Require(depleted.MaximumScaleDeviation < healthy.MaximumScaleDeviation,
            "Low vitality did not reduce breathing strength.");

        for (var i = 0; i < 120; i++)
        {
            disabled.Evaluate(true, 1f, 1f / 60f);
        }

        PlantLifeAnimationFrame finalFrame = default;
        for (var i = 0; i < 240; i++)
        {
            finalFrame = disabled.Evaluate(false, 1f, 1f / 60f);
        }

        Require(finalFrame.Activation < 0.0001f,
            "Disabled life animation did not settle back to its baseline.");
        Require(Mathf.Abs(finalFrame.Scale - 1f) < 0.0001f,
            "Disabled life animation retained a scale offset.");
        Require(finalFrame.RotationEuler.sqrMagnitude < 0.0001f,
            "Disabled life animation retained a rotation offset.");

        Debug.Log(
            "PLANT_LIFE_ANIMATION_VALIDATION: PASS\n" +
            $"healthyScale={healthy.MaximumScaleDeviation:F5}, " +
            $"healthyPitch={healthy.MaximumPitch:F3}, " +
            $"healthyYaw={healthy.MaximumYaw:F3}, " +
            $"healthyRoll={healthy.MaximumRoll:F3}, " +
            $"depletedScale={depleted.MaximumScaleDeviation:F5}");
    }

    private static MotionBounds Sample(bool active, float vitality, float duration)
    {
        var controller = new PlantLifeAnimationController();
        var bounds = new MotionBounds();
        var frameCount = Mathf.CeilToInt(duration * 60f);
        for (var i = 0; i < frameCount; i++)
        {
            var frame = controller.Evaluate(active, vitality, 1f / 60f);
            bounds.MaximumScaleDeviation = Mathf.Max(
                bounds.MaximumScaleDeviation,
                Mathf.Abs(frame.Scale - 1f));
            bounds.MaximumPitch = Mathf.Max(bounds.MaximumPitch, Mathf.Abs(frame.RotationEuler.x));
            bounds.MaximumYaw = Mathf.Max(bounds.MaximumYaw, Mathf.Abs(frame.RotationEuler.y));
            bounds.MaximumRoll = Mathf.Max(bounds.MaximumRoll, Mathf.Abs(frame.RotationEuler.z));
        }

        return bounds;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class MotionBounds
    {
        public float MaximumScaleDeviation;
        public float MaximumPitch;
        public float MaximumYaw;
        public float MaximumRoll;
    }
}
