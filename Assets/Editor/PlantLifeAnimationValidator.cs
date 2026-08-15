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
        Require(depleted.MaximumMotionStrength < healthy.MaximumMotionStrength,
            "Low vitality did not reduce leaf idle motion strength.");

        ValidateLeafIdleMotion();

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
            $"depletedScale={depleted.MaximumScaleDeviation:F5}, " +
            $"leafRoll={HydrangeaWiltRigV3Controller.MaximumLeafRollAmplitude:F2}");
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
            bounds.MaximumMotionStrength = Mathf.Max(bounds.MaximumMotionStrength, frame.MotionStrength);
        }

        return bounds;
    }

    private static void ValidateLeafIdleMotion()
    {
        var firstLeaf = HydrangeaWiltRigV3Controller.EvaluateLeafIdleEuler(0, 2f, 1f);
        var secondLeaf = HydrangeaWiltRigV3Controller.EvaluateLeafIdleEuler(1, 2f, 1f);
        Require(Vector3.Distance(firstLeaf, secondLeaf) > 0.1f,
            "Adjacent leaves did not receive visibly different idle phases.");
        Require(HydrangeaWiltRigV3Controller.EvaluateLeafIdleEuler(0, 2f, 0f) == Vector3.zero,
            "Zero leaf idle strength did not restore the base pose.");

        for (var leafIndex = 0; leafIndex < 63; leafIndex++)
        {
            for (var frame = 0; frame < 720; frame++)
            {
                var rotation = HydrangeaWiltRigV3Controller.EvaluateLeafIdleEuler(
                    leafIndex,
                    frame / 60f,
                    1f);
                Require(Mathf.Abs(rotation.x)
                        <= HydrangeaWiltRigV3Controller.MaximumLeafPitchAmplitude + 0.0001f,
                    "Leaf idle pitch exceeded its safety boundary.");
                Require(Mathf.Abs(rotation.y)
                        <= HydrangeaWiltRigV3Controller.MaximumLeafYawAmplitude + 0.0001f,
                    "Leaf idle yaw exceeded its safety boundary.");
                Require(Mathf.Abs(rotation.z)
                        <= HydrangeaWiltRigV3Controller.MaximumLeafRollAmplitude + 0.0001f,
                    "Leaf idle roll exceeded its safety boundary.");
            }
        }
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
        public float MaximumMotionStrength;
    }
}
