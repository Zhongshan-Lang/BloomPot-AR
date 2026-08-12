using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class PlantPersistenceValidator
{
    [MenuItem("BloomPot/Validate Plant Persistence")]
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
        var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        var projectTemp = Path.GetFullPath(Path.Combine(projectRoot, "Temp"));
        var validationDirectory = Path.GetFullPath(Path.Combine(projectTemp, "PlantPersistenceValidation"));
        Require(
            validationDirectory.StartsWith(projectTemp + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
            "Validation directory escaped the project Temp directory.");

        var now = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);
        var service = new PlantSaveService(validationDirectory, () => now);
        var demoService = new PlantSaveService(
            validationDirectory,
            () => now,
            PlantExperienceProfile.PortfolioDemo.SaveFileName,
            PlantExperienceProfile.PortfolioDemo);
        CleanValidationFiles(service);
        CleanValidationFiles(demoService);

        try
        {
            var defaultState = service.LoadOrCreate();
            Require(defaultState.schemaVersion == PlantState.CurrentSchemaVersion, "Default schema was incorrect.");
            Require(defaultState.stageIndex == 0, "Default stage was not Sprout.");
            Require(Mathf.Approximately(defaultState.vitality, 1f), "Default vitality was not full.");
            Require(
                !string.Equals(service.SavePath, demoService.SavePath, StringComparison.OrdinalIgnoreCase),
                "Companion and portfolio demo modes shared a save file.");
            var defaultDemoState = demoService.LoadOrCreate();
            Require(
                defaultDemoState.GetLastVisitUtc(now) == now.Subtract(PlantExperienceProfile.PortfolioDemo.VisitRewardInterval),
                "Demo default state did not use the demo visit interval.");

            defaultState.stageIndex = 4;
            defaultState.vitality = 0.72f;
            defaultState.bond = 0.4f;
            defaultState.lastInteractionUtc = PlantState.FormatUtc(now.AddDays(-2));
            defaultState.interactionDayUtc = PlantState.FormatUtcDay(now);
            defaultState.meaningfulInteractionsToday = 3;
            defaultState.lastVisitUtc = PlantState.FormatUtc(now.AddHours(-5));
            Require(service.TrySave(defaultState, out var firstSaveError), $"First save failed: {firstSaveError}");
            Require(File.Exists(service.SavePath), "Primary save was not created.");

            now = now.AddHours(1);
            defaultState.vitality = 0.31f;
            Require(service.TrySave(defaultState, out var secondSaveError), $"Second save failed: {secondSaveError}");
            Require(File.Exists(service.BackupPath), "Second save did not create a backup.");

            var loaded = service.LoadOrCreate();
            Require(loaded.stageIndex == 4, "Stage did not survive the round trip.");
            Require(Mathf.Abs(loaded.vitality - 0.31f) < 0.0001f, "Latest vitality did not survive the round trip.");
            Require(loaded.GetLastSavedUtc(now) == now, "Last-saved UTC was not updated.");
            Require(loaded.meaningfulInteractionsToday == 3, "Daily interaction count did not survive the round trip.");
            Require(loaded.GetLastVisitUtc(now) == now.AddHours(-5), "Last-visit UTC did not survive the round trip.");

            File.WriteAllText(service.SavePath, "{ invalid json", new UTF8Encoding(false));
            var recovered = service.LoadOrCreate();
            Require(
                Mathf.Abs(recovered.vitality - 0.72f) < 0.0001f,
                "Backup recovery did not restore the previous valid state.");

            var futureState = recovered.Copy();
            futureState.schemaVersion = PlantState.CurrentSchemaVersion + 1;
            Require(
                !service.TrySave(futureState, out var futureSchemaError)
                && futureSchemaError.Contains("Unsupported future schema"),
                "A future schema was not protected from downgrade overwrite.");

            return
                "PLANT_PERSISTENCE_VALIDATION: PASS\n" +
                "defaultState: PASS\n" +
                "independentModeSaves: PASS\n" +
                "jsonRoundTrip: PASS\n" +
                "backupRecovery: PASS\n" +
                "futureSchemaProtection: PASS";
        }
        finally
        {
            CleanValidationFiles(service);
            CleanValidationFiles(demoService);
            if (Directory.Exists(validationDirectory))
            {
                Directory.Delete(validationDirectory, false);
            }
        }
    }

    private static void CleanValidationFiles(PlantSaveService service)
    {
        DeleteIfPresent(service.SavePath);
        DeleteIfPresent(service.BackupPath);
        DeleteIfPresent(service.TemporaryPath);
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"PLANT_PERSISTENCE_VALIDATION: FAIL - {message}");
        }
    }
}
