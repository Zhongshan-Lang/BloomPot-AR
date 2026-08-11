using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class HydrangeaDroopRigValidator
{
    private const string AssetPath = "Assets/Flower1/Hydrangea_AR_Interactive_DroopRig_v2.fbx";
    private static readonly string[] ExpectedBlendShapes =
    {
        "Sprout",
        "Leafing",
        "Bud",
        "HalfBloom",
        "Bloom"
    };

    [MenuItem("BloomPot/Validate Hydrangea Droop Rig")]
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

    public static void RunBatchValidation()
    {
        try
        {
            Debug.Log(ValidateOrThrow());
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static string ValidateOrThrow()
    {
        var importer = AssetImporter.GetAtPath(AssetPath) as ModelImporter;
        Require(importer != null, "ModelImporter was not found.");
        Require(importer.importBlendShapes, "Import BlendShapes must be enabled.");
        Require(!importer.importAnimation, "Import Animation must be disabled.");
        Require(importer.animationType == ModelImporterAnimationType.Generic, "Rig must use Generic animation type.");
        Require(!importer.optimizeGameObjects, "Optimize Game Objects must be disabled.");
        Require(!importer.importCameras, "Import Cameras must be disabled.");
        Require(!importer.importLights, "Import Lights must be disabled.");
        Require(!importer.isReadable, "Read/Write should remain disabled.");
        Require(importer.meshCompression == ModelImporterMeshCompression.Off, "Mesh Compression must be Off.");
        Require(importer.preserveHierarchy, "Preserve Hierarchy must be enabled.");

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetPath);
        Require(model != null, "Imported FBX model asset was not found.");

        var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Require(renderers.Length == 1, $"Expected one SkinnedMeshRenderer, found {renderers.Length}.");

        var renderer = renderers[0];
        var mesh = renderer.sharedMesh;
        Require(mesh != null, "SkinnedMeshRenderer has no shared mesh.");

        var blendShapeNames = new List<string>();
        for (var i = 0; i < mesh.blendShapeCount; i++)
        {
            blendShapeNames.Add(mesh.GetBlendShapeName(i));
        }

        Require(
            blendShapeNames.SequenceEqual(ExpectedBlendShapes),
            $"Blend Shapes differ. Expected [{string.Join(", ", ExpectedBlendShapes)}], " +
            $"found [{string.Join(", ", blendShapeNames)}].");

        var transforms = model.GetComponentsInChildren<Transform>(true);
        var leafBones = transforms.Count(transform => transform.name.StartsWith("DroopLeaf_", StringComparison.Ordinal));
        var headBones = transforms.Count(transform => transform.name.StartsWith("DroopHead_", StringComparison.Ordinal));
        Require(leafBones == 63, $"Expected 63 DroopLeaf_ bones, found {leafBones}.");
        Require(headBones == 5, $"Expected 5 DroopHead_ bones, found {headBones}.");
        Require(renderer.bones.Length == 69, $"Expected 69 renderer bones, found {renderer.bones.Length}.");
        Require(renderer.rootBone != null, "Renderer root bone is missing.");

        var materials = renderer.sharedMaterials;
        Require(materials.Length == 4, $"Expected four material slots, found {materials.Length}.");
        Require(materials.All(material => material != null), "One or more material slots are unassigned.");

        return
            "HYDRANGEA_DROOPRIG_VALIDATION: PASS\n" +
            $"asset: {AssetPath}\n" +
            $"renderer: {renderer.name}\n" +
            $"mesh: {mesh.name}\n" +
            $"blendShapes ({blendShapeNames.Count}): {string.Join(", ", blendShapeNames)}\n" +
            $"rendererBones: {renderer.bones.Length}\n" +
            $"rootBone: {renderer.rootBone.name}\n" +
            $"droopLeafBones: {leafBones}\n" +
            $"droopHeadBones: {headBones}\n" +
            $"materials ({materials.Length}): {string.Join(", ", materials.Select(material => material.name))}";
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"HYDRANGEA_DROOPRIG_VALIDATION: FAIL - {message}");
        }
    }
}
