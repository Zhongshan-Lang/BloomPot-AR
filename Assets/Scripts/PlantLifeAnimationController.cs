using UnityEngine;

public sealed class PlantLifeAnimationController
{
    public const float MinimumVitalityMotion = 0.2f;
    public const float MinimumCycleDuration = 4.2f;
    public const float MaximumCycleDuration = 6.2f;
    public const float MaximumScaleAmplitude = 0.008f;
    public const float MaximumPitchAmplitude = 1f;
    public const float MaximumYawAmplitude = 0.55f;
    public const float MaximumRollAmplitude = 1.8f;

    private const float ActivationSmoothing = 2.8f;
    private float _elapsed;
    private float _activation;

    public PlantLifeAnimationFrame Evaluate(
        bool active,
        float vitality,
        float deltaTime)
    {
        var safeDeltaTime = Mathf.Clamp(deltaTime, 0f, 0.1f);
        var normalizedVitality = Mathf.Clamp01(vitality);
        var motionStrength = Mathf.Lerp(
            MinimumVitalityMotion,
            1f,
            normalizedVitality);
        var cycleDuration = Mathf.Lerp(
            MaximumCycleDuration,
            MinimumCycleDuration,
            normalizedVitality);

        if (active)
        {
            _elapsed += safeDeltaTime;
        }

        var targetActivation = active ? 1f : 0f;
        var smoothing = 1f - Mathf.Exp(-ActivationSmoothing * safeDeltaTime);
        _activation = Mathf.Lerp(_activation, targetActivation, smoothing);

        var phase = _elapsed * Mathf.PI * 2f / cycleDuration;
        var strength = _activation * motionStrength;
        var breath = Mathf.Sin(phase);
        var pitch = Mathf.Sin(phase * 0.5f + 0.7f);
        var yaw = Mathf.Sin(phase * 0.29f + 1.1f);
        var roll = Mathf.Sin(phase * 0.37f - 0.4f);

        return new PlantLifeAnimationFrame(
            1f + breath * MaximumScaleAmplitude * strength,
            new Vector3(
                pitch * MaximumPitchAmplitude * strength,
                yaw * MaximumYawAmplitude * strength,
                roll * MaximumRollAmplitude * strength),
            _activation,
            strength);
    }

    public void Reset()
    {
        _elapsed = 0f;
        _activation = 0f;
    }
}

public readonly struct PlantLifeAnimationFrame
{
    public PlantLifeAnimationFrame(
        float scale,
        Vector3 rotationEuler,
        float activation,
        float motionStrength)
    {
        Scale = scale;
        RotationEuler = rotationEuler;
        Activation = activation;
        MotionStrength = motionStrength;
    }

    public float Scale { get; }
    public Vector3 RotationEuler { get; }
    public float Activation { get; }
    public float MotionStrength { get; }
}
