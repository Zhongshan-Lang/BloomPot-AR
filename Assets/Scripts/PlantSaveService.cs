using System;
using System.IO;
using System.Text;
using UnityEngine;

public sealed class PlantSaveService
{
    private const string DefaultSaveFileName = "plant-state.json";
    private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(false);

    private readonly Func<DateTime> _utcNow;
    private readonly PlantExperienceProfile _profile;

    public PlantSaveService()
        : this(
            Application.persistentDataPath,
            () => DateTime.UtcNow,
            DefaultSaveFileName,
            PlantExperienceProfile.Companion)
    {
    }

    public PlantSaveService(string directoryPath, Func<DateTime> utcNow)
        : this(
            directoryPath,
            utcNow,
            DefaultSaveFileName,
            PlantExperienceProfile.Companion)
    {
    }

    public PlantSaveService(
        string directoryPath,
        Func<DateTime> utcNow,
        string saveFileName,
        PlantExperienceProfile profile)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new ArgumentException("A save directory is required.", nameof(directoryPath));
        }

        if (string.IsNullOrWhiteSpace(saveFileName)
            || Path.GetFileName(saveFileName) != saveFileName)
        {
            throw new ArgumentException("A plain save file name is required.", nameof(saveFileName));
        }

        SaveDirectory = Path.GetFullPath(directoryPath);
        SavePath = Path.Combine(SaveDirectory, saveFileName);
        BackupPath = SavePath + ".bak";
        TemporaryPath = SavePath + ".tmp";
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        _profile = profile ?? PlantExperienceProfile.Companion;
    }

    public string SaveDirectory { get; }
    public string SavePath { get; }
    public string BackupPath { get; }
    public string TemporaryPath { get; }

    public PlantState LoadOrCreate()
    {
        var now = _utcNow().ToUniversalTime();
        if (TryReadState(SavePath, now, out var state, out var primaryError))
        {
            return state;
        }

        if (TryReadState(BackupPath, now, out state, out var backupError))
        {
            Debug.LogWarning(
                $"PlantSaveService: primary save could not be loaded; using backup. {primaryError}");
            return state;
        }

        if (!string.IsNullOrEmpty(primaryError) || !string.IsNullOrEmpty(backupError))
        {
            Debug.LogWarning(
                $"PlantSaveService: no valid save was found; creating a default state. " +
                $"Primary: {primaryError ?? "missing"}. Backup: {backupError ?? "missing"}.");
        }

        return PlantState.CreateDefault(now, _profile);
    }

    public bool TrySave(PlantState state, out string error)
    {
        error = null;
        if (state == null)
        {
            error = "Plant state is null.";
            return false;
        }

        var now = _utcNow().ToUniversalTime();
        if (!state.Normalize(now))
        {
            error = $"Unsupported future schema version {state.schemaVersion}.";
            return false;
        }

        state.lastSavedUtc = PlantState.FormatUtc(now);
        try
        {
            Directory.CreateDirectory(SaveDirectory);
            WriteDurableText(TemporaryPath, JsonUtility.ToJson(state, true));
            PromoteTemporarySave();
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
        finally
        {
            TryDelete(TemporaryPath);
        }
    }

    private static bool TryReadState(
        string path,
        DateTime utcNow,
        out PlantState state,
        out string error)
    {
        state = null;
        error = null;
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            state = JsonUtility.FromJson<PlantState>(json);
            if (state == null)
            {
                error = $"'{path}' contained no plant state.";
                return false;
            }

            if (!state.Normalize(utcNow))
            {
                error = $"'{path}' uses unsupported schema version {state.schemaVersion}.";
                state = null;
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            error = $"'{path}' is invalid: {exception.Message}";
            state = null;
            return false;
        }
    }

    private void PromoteTemporarySave()
    {
        if (!File.Exists(SavePath))
        {
            File.Move(TemporaryPath, SavePath);
            return;
        }

        try
        {
            File.Replace(TemporaryPath, SavePath, BackupPath, true);
        }
        catch (PlatformNotSupportedException)
        {
            PromoteWithPortableFallback();
        }
        catch (IOException)
        {
            PromoteWithPortableFallback();
        }
    }

    private void PromoteWithPortableFallback()
    {
        File.Copy(SavePath, BackupPath, true);
        File.Delete(SavePath);
        File.Move(TemporaryPath, SavePath);
    }

    private static void WriteDurableText(string path, string content)
    {
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream, Utf8WithoutBom))
        {
            writer.Write(content);
            writer.Flush();
            stream.Flush(true);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // A stale temporary file is harmless and will be overwritten on the next save.
        }
    }
}
