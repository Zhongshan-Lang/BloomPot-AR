using System;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class BloomPotAcceptanceValidator
{
    [MenuItem("BloomPot/Validate Complete Experience", false, 1)]
    public static void ValidateFromMenu()
    {
        try
        {
            Debug.Log(ValidateAllOrThrow());
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    public static void RunBatchValidation()
    {
        try
        {
            Debug.Log(ValidateAllOrThrow());
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    public static string ValidateAllOrThrow()
    {
        var report = new StringBuilder();
        report.AppendLine(PlantGrowthValidator.ValidateOrThrow());
        report.AppendLine(PlantInteractionValidator.ValidateOrThrow());
        report.AppendLine(PlantPersistenceValidator.ValidateOrThrow());
        report.AppendLine(PlantLightValidator.ValidateOrThrow());
        PlantLifeAnimationValidator.Validate();
        report.AppendLine("PLANT_LIFE_ANIMATION_VALIDATION: PASS");
        report.AppendLine(HydrangeaWiltRigV3Validator.ValidateOrThrow());
        report.AppendLine(HydrangeaWiltRigV3Validator.ValidateFormalSceneOrThrow());
        report.AppendLine("BLOOMPOT_COMPLETE_ACCEPTANCE: PASS");
        return report.ToString();
    }
}
