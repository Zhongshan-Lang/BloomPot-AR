using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BloomPotExperienceTools
{
    private const string CompanionProductName = "BloomPot AR";
    private const string DemoProductName = "BloomPot AR Demo";
    private const string CompanionApplicationId = "com.zhongshanlang.bloompotar";
    private const string DemoApplicationId = "com.zhongshanlang.bloompotar.demo";

    [MenuItem("BloomPot/Experience Mode/Preview Portfolio Demo", false, 10)]
    private static void TogglePortfolioDemoPreview()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Stop Play Mode before changing the BloomPot experience preview.");
            return;
        }

        var enabled = !EditorPrefs.GetBool(PlantExperienceProfile.EditorDemoPreviewKey, false);
        EditorPrefs.SetBool(PlantExperienceProfile.EditorDemoPreviewKey, enabled);
        Debug.Log(
            enabled
                ? "BloomPot Editor preview: Portfolio Demo mode enabled."
                : "BloomPot Editor preview: Long-term Companion mode enabled.");
    }

    [MenuItem("BloomPot/Experience Mode/Preview Portfolio Demo", true)]
    private static bool ValidatePortfolioDemoPreview()
    {
        Menu.SetChecked(
            "BloomPot/Experience Mode/Preview Portfolio Demo",
            EditorPrefs.GetBool(PlantExperienceProfile.EditorDemoPreviewKey, false));
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    [MenuItem("BloomPot/Experience Mode/Use Long-term Companion", false, 11)]
    private static void UseCompanionPreview()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Stop Play Mode before changing the BloomPot experience preview.");
            return;
        }

        EditorPrefs.SetBool(PlantExperienceProfile.EditorDemoPreviewKey, false);
        Debug.Log("BloomPot Editor preview: Long-term Companion mode enabled.");
    }

    [MenuItem("BloomPot/Build Android/Companion APK", false, 30)]
    private static void BuildCompanionApk()
    {
        BuildAndroid(
            PlantExperienceMode.Companion,
            CompanionProductName,
            CompanionApplicationId,
            "BloomPot-AR-Companion.apk");
    }

    [MenuItem("BloomPot/Build Android/Portfolio Demo APK", false, 31)]
    private static void BuildPortfolioDemoApk()
    {
        BuildAndroid(
            PlantExperienceMode.PortfolioDemo,
            DemoProductName,
            DemoApplicationId,
            "BloomPot-AR-Portfolio-Demo.apk");
    }

    private static void BuildAndroid(
        PlantExperienceMode mode,
        string productName,
        string applicationId,
        string fileName)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException("Stop Play Mode before building BloomPot AR.");
        }

        var scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled && !string.IsNullOrWhiteSpace(scene.path))
            .Select(scene => scene.path)
            .ToArray();
        if (scenes.Length == 0)
        {
            throw new InvalidOperationException("No enabled scenes are present in Build Settings.");
        }

        var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        var outputDirectory = Path.Combine(projectRoot, "Builds", "Android");
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, fileName);

        var previousProductName = PlayerSettings.productName;
        var previousApplicationId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
        try
        {
            PlayerSettings.productName = productName;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, applicationId);
            AssetDatabase.SaveAssets();

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = BuildOptions.None,
                extraScriptingDefines = mode == PlantExperienceMode.PortfolioDemo
                    ? new[] { PlantExperienceProfile.DemoBuildDefine }
                    : Array.Empty<string>()
            };

            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"BloomPot Android build failed: {report.summary.result}, " +
                    $"errors={report.summary.totalErrors}.");
            }

            Debug.Log(
                $"BloomPot Android build succeeded: {mode}\n" +
                $"path: {outputPath}\n" +
                $"size: {report.summary.totalSize} bytes");
            EditorUtility.RevealInFinder(outputPath);
        }
        finally
        {
            PlayerSettings.productName = previousProductName;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, previousApplicationId);
            AssetDatabase.SaveAssets();
        }
    }
}
