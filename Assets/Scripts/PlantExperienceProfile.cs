using System;

public enum PlantExperienceMode
{
    Companion,
    PortfolioDemo
}

public sealed class PlantExperienceProfile
{
    public const string DemoBuildDefine = "BLOOMPOT_PORTFOLIO_DEMO";
    public const string EditorDemoPreviewKey = "BloomPot.PortfolioDemoPreview";

    private PlantExperienceProfile(
        PlantExperienceMode mode,
        string displayName,
        string saveFileName,
        TimeSpan inactivityGrace,
        float vitalityLossPerSecond,
        TimeSpan maximumDecayEvaluation,
        TimeSpan visitRewardInterval,
        TimeSpan interactionLimitWindow,
        bool usesUtcDailyInteractionReset,
        bool usesRealtimeDecay)
    {
        Mode = mode;
        DisplayName = displayName;
        SaveFileName = saveFileName;
        InactivityGrace = inactivityGrace;
        VitalityLossPerSecond = vitalityLossPerSecond;
        MaximumDecayEvaluation = maximumDecayEvaluation;
        VisitRewardInterval = visitRewardInterval;
        InteractionLimitWindow = interactionLimitWindow;
        UsesUtcDailyInteractionReset = usesUtcDailyInteractionReset;
        UsesRealtimeDecay = usesRealtimeDecay;
    }

    public PlantExperienceMode Mode { get; }
    public string DisplayName { get; }
    public string SaveFileName { get; }
    public TimeSpan InactivityGrace { get; }
    public float VitalityLossPerSecond { get; }
    public TimeSpan MaximumDecayEvaluation { get; }
    public TimeSpan VisitRewardInterval { get; }
    public TimeSpan InteractionLimitWindow { get; }
    public bool UsesUtcDailyInteractionReset { get; }
    public bool UsesRealtimeDecay { get; }
    public bool IsPortfolioDemo => Mode == PlantExperienceMode.PortfolioDemo;

    public static PlantExperienceProfile Companion { get; } = new PlantExperienceProfile(
        PlantExperienceMode.Companion,
        "\u957f\u671f\u966a\u4f34",
        "plant-state.json",
        TimeSpan.FromHours(12d),
        0.08f / 86400f,
        TimeSpan.FromDays(30d),
        TimeSpan.FromHours(4d),
        TimeSpan.FromDays(1d),
        true,
        false);

    public static PlantExperienceProfile PortfolioDemo { get; } = new PlantExperienceProfile(
        PlantExperienceMode.PortfolioDemo,
        "\u4f5c\u54c1\u96c6\u6f14\u793a",
        "plant-state-demo.json",
        TimeSpan.FromSeconds(5d),
        0.02f,
        TimeSpan.FromSeconds(60d),
        TimeSpan.FromSeconds(20d),
        TimeSpan.FromSeconds(60d),
        false,
        true);

    public static PlantExperienceProfile ResolveCurrent()
    {
#if BLOOMPOT_PORTFOLIO_DEMO
        return PortfolioDemo;
#elif UNITY_EDITOR
        return UnityEditor.EditorPrefs.GetBool(EditorDemoPreviewKey, false)
            ? PortfolioDemo
            : Companion;
#else
        return Companion;
#endif
    }
}
