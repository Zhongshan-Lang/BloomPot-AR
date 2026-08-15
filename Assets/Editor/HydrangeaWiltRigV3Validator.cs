using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class HydrangeaWiltRigV3Validator
{
    private const string ModelAssetPath = "Assets/Flower1/Hydrangea_AR_Interactive_WiltRig_v3.fbx";
    private const string BaselineModelAssetPath = "Assets/Flower1/Hydrangea_AR_Interactive_DroopRig_v2.fbx";
    private const string PivotAssetPath = "Assets/Flower1/Data/Hydrangea_WiltRig_v3_Pivots.json";
    private const string FormalScenePath = "Assets/Scenes/SampleScene.unity";
    private static readonly string[] ExpectedBlendShapes =
    {
        "Sprout",
        "Leafing",
        "Bud",
        "HalfBloom",
        "Bloom"
    };
    private static readonly Dictionary<string, float> ExpectedMaximumWiltDisplacements =
        new Dictionary<string, float>
        {
            { "Sprout", 1.365714012990793e-07f },
            { "Leafing", 0.15163803100585938f },
            { "Bud", 0.4257838726043701f },
            { "HalfBloom", 0.8815342783927917f },
            { "Bloom", 1.4144446849822998f }
        };
    private static readonly Dictionary<string, Vector3> ExpectedHealthyBoundsSizes =
        new Dictionary<string, Vector3>
        {
            { "Sprout", new Vector3(0.5012378f, 1.1404135f, 1.5875881f) },
            { "Leafing", new Vector3(1.9800342f, 2.9312727f, 3.3622620f) },
            { "Bud", new Vector3(3.0875536f, 4.5028758f, 5.0341370f) },
            { "HalfBloom", new Vector3(3.7440104f, 5.5819609f, 6.7845235f) },
            { "Bloom", new Vector3(4.8425958f, 6.6741776f, 7.7917414f) }
        };
    private static readonly Dictionary<string, Vector3> ExpectedWiltedBoundsSizes =
        new Dictionary<string, Vector3>
        {
            { "Sprout", new Vector3(0.5012378f, 1.1404135f, 1.5875881f) },
            { "Leafing", new Vector3(1.9475374f, 2.9317228f, 3.3922519f) },
            { "Bud", new Vector3(2.9914199f, 4.5550389f, 5.0543146f) },
            { "HalfBloom", new Vector3(4.1687686f, 6.3759768f, 6.6725293f) },
            { "Bloom", new Vector3(5.4779487f, 7.8425074f, 7.5061717f) }
        };

    [MenuItem("BloomPot/Validate Hydrangea Wilt Rig v3")]
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

    [MenuItem("BloomPot/Open Hydrangea Wilt Rig v3 Test Scene")]
    public static void OpenTestScene()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/HydrangeaWiltRigV3Test.unity");
    }

    [MenuItem("BloomPot/Validate Formal Hydrangea v3 Scene")]
    public static void ValidateFormalSceneFromMenu()
    {
        try
        {
            Debug.Log(ValidateFormalSceneOrThrow());
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    public static string ValidateFormalSceneOrThrow()
    {
        var scene = SceneManager.GetSceneByPath(FormalScenePath);
        var openedForValidation = !scene.isLoaded;
        if (openedForValidation)
        {
            scene = EditorSceneManager.OpenScene(FormalScenePath, OpenSceneMode.Additive);
        }

        try
        {
            var experiences = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<HydrangeaInteractiveExperience>(true))
                .ToArray();
            Require(experiences.Length == 1,
                $"Formal scene must contain one HydrangeaInteractiveExperience, found {experiences.Length}.");

            var experience = experiences[0];
            var serializedExperience = new SerializedObject(experience);
            var pivotProperty = serializedExperience.FindProperty("_wiltRigV3PivotData");
            var testControlsProperty = serializedExperience.FindProperty("_showDroopTestControls");
            var axisFixProperty = serializedExperience.FindProperty("_applyBlenderAxisFix");
            Require(pivotProperty?.objectReferenceValue == AssetDatabase.LoadAssetAtPath<TextAsset>(PivotAssetPath),
                "Formal scene does not reference the WiltRig v3 Pivot JSON.");
            Require(testControlsProperty != null && !testControlsProperty.boolValue,
                "Formal scene must keep droop test controls disabled.");
            Require(axisFixProperty != null && !axisFixProperty.boolValue,
                "Formal scene must not apply the legacy Blender axis correction.");

            var sceneModels = experience.GetComponentsInChildren<Transform>(true)
                .Where(child => child.name == "Hydrangea_Growth_BlendShape_Unity")
                .ToArray();
            Require(sceneModels.Length == 1,
                $"Formal scene must contain one hydrangea model template, found {sceneModels.Length}.");
            var sourceModel = PrefabUtility.GetCorrespondingObjectFromSource(sceneModels[0].gameObject);
            Require(AssetDatabase.GetAssetPath(sourceModel) == ModelAssetPath,
                $"Formal model source is '{AssetDatabase.GetAssetPath(sourceModel)}', expected '{ModelAssetPath}'.");

            var dependencies = AssetDatabase.GetDependencies(FormalScenePath, true);
            Require(dependencies.Contains(ModelAssetPath), "Formal scene dependency list is missing WiltRig v3.");
            Require(dependencies.Contains(PivotAssetPath), "Formal scene dependency list is missing the Pivot JSON.");
            Require(!dependencies.Contains("Assets/Flower1/Hydrangea_Growth_BlendShape_Unity.fbx"),
                "Formal scene still depends on the pre-rig model.");
            Require(!dependencies.Contains("Assets/Flower1/Hydrangea_AR_Interactive_DroopRig.fbx")
                    && !dependencies.Contains(BaselineModelAssetPath),
                "Formal scene still depends on a historical DroopRig model.");

            var buildScenes = EditorBuildSettings.scenes.Where(buildScene => buildScene.enabled).ToArray();
            Require(buildScenes.Length == 1 && buildScenes[0].path == FormalScenePath,
                "Build Profile must contain only the formal SampleScene.");

            return
                "HYDRANGEA_WILTRIG_V3_FORMAL_SCENE: PASS\n" +
                $"scene: {FormalScenePath}\n" +
                $"model: {ModelAssetPath}\n" +
                $"pivotData: {PivotAssetPath}\n" +
                "testControls: disabled\n" +
                "buildScenes: SampleScene only\n" +
                "historicalModelDependencies: none";
        }
        finally
        {
            if (openedForValidation && scene.isLoaded)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
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

    public static string ValidateOrThrow()
    {
        ValidateImporter();

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelAssetPath);
        var pivotData = AssetDatabase.LoadAssetAtPath<TextAsset>(PivotAssetPath);
        Require(model != null, "Imported v3 FBX model was not found.");
        Require(pivotData != null, "Pivot JSON TextAsset was not found.");

        var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Require(renderers.Length == 1, $"Expected one SkinnedMeshRenderer, found {renderers.Length}.");
        var renderer = renderers[0];
        var mesh = renderer.sharedMesh;
        Require(mesh != null, "SkinnedMeshRenderer has no shared mesh.");
        Require(mesh.vertexCount == 748102, $"Expected 748102 imported vertices, found {mesh.vertexCount}.");
        var baselineModel = AssetDatabase.LoadAssetAtPath<GameObject>(BaselineModelAssetPath);
        var baselineRenderer = baselineModel != null
            ? baselineModel.GetComponentsInChildren<SkinnedMeshRenderer>(true).SingleOrDefault()
            : null;
        Require(baselineRenderer != null && baselineRenderer.sharedMesh != null,
            "The v2 baseline SkinnedMeshRenderer was not found.");
        Require(mesh.vertexCount == baselineRenderer.sharedMesh.vertexCount,
            $"v3 imported vertices ({mesh.vertexCount}) differ from v2 ({baselineRenderer.sharedMesh.vertexCount}).");
        Require(mesh.subMeshCount == 4, $"Expected four submeshes, found {mesh.subMeshCount}.");

        var blendShapeNames = Enumerable.Range(0, mesh.blendShapeCount)
            .Select(mesh.GetBlendShapeName)
            .ToArray();
        Require(blendShapeNames.SequenceEqual(ExpectedBlendShapes),
            $"Blend Shapes differ. Expected [{string.Join(", ", ExpectedBlendShapes)}], " +
            $"found [{string.Join(", ", blendShapeNames)}].");

        var bones = renderer.bones;
        Require(bones.Length == 110, $"Expected 110 renderer bones, found {bones.Length}.");
        Require(mesh.bindposes.Length == bones.Length,
            $"Expected 110 bindposes, found {mesh.bindposes.Length}.");
        Require(bones.Select(bone => bone.name).Distinct().Count() == bones.Length,
            "Renderer contains duplicate bone names.");

        var transforms = model.GetComponentsInChildren<Transform>(true);
        Require(CountExact(transforms, "Root") == 1, "Expected one Root bone.");
        Require(CountPrefix(transforms, "WiltStem_") == 1, "Expected one WiltStem_ bone.");
        Require(CountPrefix(transforms, "WiltBranch_Main_") == 5, "Expected five WiltBranch_Main_ bones.");
        Require(CountPrefix(transforms, "WiltBranch_Hub_") == 5, "Expected five WiltBranch_Hub_ bones.");
        Require(CountPrefix(transforms, "WiltBranch_Sub_") == 30, "Expected 30 WiltBranch_Sub_ bones.");
        Require(CountPrefix(transforms, "WiltLeaf_") == 63, "Expected 63 WiltLeaf_ bones.");
        Require(CountPrefix(transforms, "WiltHead_") == 5, "Expected five WiltHead_ bones.");

        var materials = renderer.sharedMaterials;
        Require(materials.Length == 4, $"Expected four material slots, found {materials.Length}.");
        Require(materials.All(material => material != null), "One or more material slots are unassigned.");

        var document = JsonUtility.FromJson<HydrangeaWiltRigV3Document>(pivotData.text);
        Require(document != null, "Pivot JSON could not be parsed.");
        Require(document.schema == "BloomPot.HydrangeaWiltRig.v3",
            $"Unexpected Pivot JSON schema '{document.schema}'.");
        Require(document.units == "meters", $"Unexpected Pivot JSON units '{document.units}'.");
        Require(document.boneCount == 110 && document.bones != null && document.bones.Length == 110,
            "Pivot JSON does not contain exactly 110 bones.");
        Require(document.bones.Select(bone => bone.name).Distinct().Count() == 110,
            "Pivot JSON contains duplicate bone names.");
        Require(document.bones.All(bone => bones.Any(rendererBone => rendererBone.name == bone.name)),
            "One or more Pivot JSON bones are missing from renderer.bones.");

        ValidateRuntimeMatrices(model, pivotData);

        return
            "HYDRANGEA_WILTRIG_V3_VALIDATION: PASS\n" +
            $"asset: {ModelAssetPath}\n" +
            $"importedVertices: {mesh.vertexCount} (matches v2; Blender logical vertices: 679081)\n" +
            $"blendShapes ({blendShapeNames.Length}): {string.Join(", ", blendShapeNames)}\n" +
            $"rendererBones: {bones.Length}\n" +
            $"materials ({materials.Length}): {string.Join(", ", materials.Select(material => material.name))}\n" +
            "matrixTests: 20 discrete combinations + 40 adjacent-transition samples + restore\n" +
            "leafIdleTests: 63 leaves + root attachment + pose restore\n" +
            "geometryTests: BakeMesh baseline/max-wilt displacement for all five stages";
    }

    private static void ValidateImporter()
    {
        var importer = AssetImporter.GetAtPath(ModelAssetPath) as ModelImporter;
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
    }

    private static void ValidateRuntimeMatrices(GameObject model, TextAsset pivotData)
    {
        var instance = UnityEngine.Object.Instantiate(model);
        instance.name = "HydrangeaWiltRigV3ValidationInstance";
        try
        {
            var renderer = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
            var controller = instance.AddComponent<HydrangeaWiltRigV3Controller>();
            Require(controller.Initialize(renderer, pivotData), "Runtime controller failed to initialize.");
            Require(Mathf.Abs(controller.PivotCoordinateScale - 0.01f) <= 0.000001f,
                $"Expected FBX/JSON pivot scale 0.01, found {controller.PivotCoordinateScale:G9}.");
            Require(controller.AnimatedLeafCount == 63,
                $"Expected 63 animated leaves, found {controller.AnimatedLeafCount}.");

            ValidateBakedGeometry(renderer, controller);
            ValidateLeafIdleOverlay(renderer, controller);

            controller.SetGrowthStage("Bud");
            controller.SetWilt(0f);
            Require(controller.ApplyPoseNow(), "Failed to apply the bind pose baseline.");
            var baseline = CaptureLocalMatrices(renderer.bones);

            foreach (var stage in ExpectedBlendShapes)
            {
                foreach (var wilt in new[] { 0f, 0.3f, 0.6f, 1f })
                {
                    controller.SetGrowthStage(stage);
                    controller.SetWilt(wilt);
                    Require(controller.ApplyPoseNow(), $"Matrix application failed for {stage}, wilt {wilt}.");
                    RequireTransformsFinite(renderer.bones, $"{stage}, wilt {wilt}");
                }
            }

            var transitions = new[]
            {
                (From: "Sprout", To: "Leafing"),
                (From: "Leafing", To: "Bud"),
                (From: "Bud", To: "HalfBloom"),
                (From: "HalfBloom", To: "Bloom")
            };
            foreach (var transition in transitions)
            {
                foreach (var wilt in new[] { 0.6f, 1f })
                {
                    foreach (var t in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
                    {
                        controller.SetGrowthTransition(transition.From, transition.To, t);
                        controller.SetWilt(wilt);
                        Require(controller.ApplyPoseNow(),
                            $"Matrix application failed for {transition.From}->{transition.To}, t {t}, wilt {wilt}.");
                        RequireTransformsFinite(renderer.bones,
                            $"{transition.From}->{transition.To}, t {t}, wilt {wilt}");
                    }
                }
            }

            controller.SetGrowthStage("Bud");
            controller.SetWilt(1f);
            controller.SetWilt(0f);
            Require(controller.ApplyPoseNow(), "Failed to restore wilt 1 to 0.");
            var restored = CaptureLocalMatrices(renderer.bones);
            Require(MaxMatrixDifference(baseline, restored) < 0.0001f,
                "Wilt 1 to 0 did not restore the bind pose within tolerance.");

            ValidateHydrangeaView(instance, renderer, controller);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    private static void ValidateHydrangeaView(
        GameObject instance,
        SkinnedMeshRenderer renderer,
        HydrangeaWiltRigV3Controller controller)
    {
        var view = instance.AddComponent<HydrangeaView>();
        Require(
            view.Bind(
                renderer,
                null,
                controller,
                0f,
                0f,
                new Color(0.95f, 0.78f, 0.32f, 1f),
                0.35f),
            "HydrangeaView failed to bind the validated v3 renderer.");
        Require(view.IsReady, "HydrangeaView did not report ready after binding.");
        Require(view.SupportsWilt, "HydrangeaView did not expose v3 wilt support.");
        Require(view.CurrentStageName == "Bud", "HydrangeaView did not initialize to Bud.");

        Require(view.SetStageImmediate("Seed"), "HydrangeaView rejected the Basis-backed Seed stage.");
        Require(view.CurrentStageName == "Seed", "HydrangeaView did not retain the Seed stage.");
        for (var index = 0; index < renderer.sharedMesh.blendShapeCount; index++)
        {
            Require(
                Mathf.Abs(renderer.GetBlendShapeWeight(index)) < 0.001f,
                "HydrangeaView did not represent Seed with zero Blend Shape weights.");
        }
        Require(controller.CurrentStageName == "Sprout",
            "The Seed stage did not use Sprout pivot data for the wilt rig.");

        foreach (var stage in ExpectedBlendShapes)
        {
            Require(view.SetStageImmediate(stage), $"HydrangeaView rejected stage '{stage}'.");
            Require(view.CurrentStageName == stage, $"HydrangeaView did not retain stage '{stage}'.");
            for (var index = 0; index < renderer.sharedMesh.blendShapeCount; index++)
            {
                var expectedWeight = renderer.sharedMesh.GetBlendShapeName(index) == stage ? 100f : 0f;
                Require(
                    Mathf.Abs(renderer.GetBlendShapeWeight(index) - expectedWeight) < 0.001f,
                    $"HydrangeaView applied an unexpected Blend Shape weight for '{stage}'.");
            }
        }

        var leafingAnchor = PlantStageProgressionController.GetStageAnchor(2);
        var budAnchor = PlantStageProgressionController.GetStageAnchor(3);
        Require(view.SetGrowthProgressImmediate(Mathf.Lerp(leafingAnchor, budAnchor, 0.4f)),
            "HydrangeaView rejected continuous growth between Leafing and Bud.");
        Require(Mathf.Abs(renderer.GetBlendShapeWeight(
                    renderer.sharedMesh.GetBlendShapeIndex("Leafing")) - 60f) < 0.01f,
            "Continuous growth did not preserve the expected Leafing contribution.");
        Require(Mathf.Abs(renderer.GetBlendShapeWeight(
                    renderer.sharedMesh.GetBlendShapeIndex("Bud")) - 40f) < 0.01f,
            "Continuous growth did not apply the expected Bud contribution.");

        var propertyBlock = new MaterialPropertyBlock();
        view.SetAppearanceDecayImmediate(0f);
        renderer.GetPropertyBlock(propertyBlock, 0);
        var healthyColor = propertyBlock.GetColor("_BaseColor");
        view.SetAppearanceDecayImmediate(PlantGrowthController.AppearanceDecayAtCriticalThreshold);
        propertyBlock.Clear();
        renderer.GetPropertyBlock(propertyBlock, 0);
        var maximumWiltColor = propertyBlock.GetColor("_BaseColor");
        view.SetAppearanceDecayImmediate(1f);
        propertyBlock.Clear();
        renderer.GetPropertyBlock(propertyBlock, 0);
        var wiltedColor = propertyBlock.GetColor("_BaseColor");
        Require(
            wiltedColor.b < healthyColor.b,
            "HydrangeaView did not apply the expected warm wilt tint.");
        Require(
            wiltedColor.b < maximumWiltColor.b,
            "HydrangeaView did not intensify yellowing below 35% vitality.");
        Require(
            wiltedColor.grayscale < maximumWiltColor.grayscale,
            "HydrangeaView did not reduce brightness below 35% vitality.");

        propertyBlock.Clear();
        renderer.GetPropertyBlock(propertyBlock, 2);
        Require(
            Mathf.Abs(propertyBlock.GetFloat("_WiltBrightness") - 0.58f) < 0.0001f,
            "HydrangeaView did not apply the expected zero-vitality petal brightness.");

        view.SetAppearanceDecayImmediate(0f);
        propertyBlock.Clear();
        renderer.GetPropertyBlock(propertyBlock, 0);
        var restoredColor = propertyBlock.GetColor("_BaseColor");
        Require(
            Vector4.Distance(healthyColor, restoredColor) < 0.0001f,
            "HydrangeaView did not restore the healthy material color.");
    }

    private static void ValidateLeafIdleOverlay(
        SkinnedMeshRenderer renderer,
        HydrangeaWiltRigV3Controller controller)
    {
        var leaves = renderer.bones
            .Where(bone => bone.name.StartsWith("WiltLeaf_", StringComparison.Ordinal))
            .ToArray();
        Require(leaves.Length == 63, $"Expected 63 leaf bones, found {leaves.Length}.");

        controller.SetGrowthStage("Bloom");
        controller.SetWilt(0.6f);
        controller.SetLeafIdleMotionStrength(0f);
        Require(controller.ApplyPoseNow(), "Failed to establish the leaf idle baseline.");
        var baselinePositions = leaves.Select(leaf => leaf.localPosition).ToArray();
        var baselineRotations = leaves.Select(leaf => leaf.localRotation).ToArray();
        var baselineScales = leaves.Select(leaf => leaf.localScale).ToArray();

        controller.SetLeafIdleMotionStrength(1f);
        Require(controller.ApplyPoseNow(), "Failed to apply leaf idle motion.");
        var movedLeaves = 0;
        for (var index = 0; index < leaves.Length; index++)
        {
            Require(Vector3.Distance(leaves[index].localPosition, baselinePositions[index]) < 0.000001f,
                $"Leaf '{leaves[index].name}' idle motion changed its root position.");
            Require(Vector3.Distance(leaves[index].localScale, baselineScales[index]) < 0.000001f,
                $"Leaf '{leaves[index].name}' idle motion changed its scale.");
            if (Quaternion.Angle(leaves[index].localRotation, baselineRotations[index]) > 0.05f)
            {
                movedLeaves++;
            }
        }

        Require(movedLeaves >= 50,
            $"Leaf idle motion affected only {movedLeaves} of 63 leaves.");

        controller.SetLeafIdleMotionStrength(0f);
        Require(controller.ApplyPoseNow(), "Failed to restore the leaf idle baseline.");
        for (var index = 0; index < leaves.Length; index++)
        {
            Require(Quaternion.Angle(leaves[index].localRotation, baselineRotations[index]) < 0.001f,
                $"Leaf '{leaves[index].name}' did not restore its base rotation.");
        }
    }

    private static void ValidateBakedGeometry(
        SkinnedMeshRenderer renderer,
        HydrangeaWiltRigV3Controller controller)
    {
        var bakedMesh = new Mesh { name = "HydrangeaWiltRigV3ValidationBake" };
        try
        {
            foreach (var stage in ExpectedBlendShapes)
            {
                SetBlendShapeStage(renderer, stage);
                controller.SetGrowthStage(stage);
                controller.SetWilt(0f);
                Require(controller.ApplyPoseNow(), $"Failed to bake the {stage} healthy baseline.");
                renderer.BakeMesh(bakedMesh);
                var healthyVertices = bakedMesh.vertices;
                RequireVerticesFinite(healthyVertices, $"{stage}, wilt 0");
                var healthyBounds = CalculateBounds(healthyVertices);
                RequireBoundsSize(
                    healthyBounds,
                    ExpectedHealthyBoundsSizes[stage],
                    $"{stage}, wilt 0");

                controller.SetWilt(1f);
                Require(controller.ApplyPoseNow(), $"Failed to bake {stage}, wilt 1.");
                renderer.BakeMesh(bakedMesh);
                var wiltedVertices = bakedMesh.vertices;
                RequireVerticesFinite(wiltedVertices, $"{stage}, wilt 1");
                Require(healthyVertices.Length == wiltedVertices.Length,
                    $"Baked vertex count changed for {stage}.");

                var maximumDisplacement = 0f;
                for (var index = 0; index < healthyVertices.Length; index++)
                {
                    maximumDisplacement = Mathf.Max(
                        maximumDisplacement,
                        Vector3.Distance(healthyVertices[index], wiltedVertices[index]));
                }

                // BakeMesh output includes the imported FBX hierarchy scale, so its values
                // match the Blender verification coordinates rather than sharedMesh.bounds.
                var expectedDisplacement = ExpectedMaximumWiltDisplacements[stage];
                var displacementTolerance = Mathf.Max(0.00005f, expectedDisplacement * 0.03f);
                Require(Mathf.Abs(maximumDisplacement - expectedDisplacement) <= displacementTolerance,
                    $"{stage}, wilt 1 maximum baked displacement was {maximumDisplacement:G9}; " +
                    $"expected {expectedDisplacement:G9} +/- {displacementTolerance:G9}.");

                var wiltedBounds = CalculateBounds(wiltedVertices);
                RequireBoundsSize(
                    wiltedBounds,
                    ExpectedWiltedBoundsSizes[stage],
                    $"{stage}, wilt 1");
                Debug.Log(
                    $"HYDRANGEA_WILTRIG_V3_GEOMETRY: {stage} PASS; " +
                    $"max displacement {maximumDisplacement:G9}; " +
                    $"healthy bounds {healthyBounds.size}; wilted bounds {wiltedBounds.size}.");
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(bakedMesh);
        }
    }

    private static void SetBlendShapeStage(SkinnedMeshRenderer renderer, string stage)
    {
        var mesh = renderer.sharedMesh;
        for (var index = 0; index < mesh.blendShapeCount; index++)
        {
            renderer.SetBlendShapeWeight(index, mesh.GetBlendShapeName(index) == stage ? 100f : 0f);
        }
    }

    private static void RequireVerticesFinite(IEnumerable<Vector3> vertices, string context)
    {
        Require(vertices.All(IsFinite), $"Baked mesh contains NaN or Infinity during {context}.");
    }

    private static Bounds CalculateBounds(IReadOnlyList<Vector3> vertices)
    {
        Require(vertices.Count > 0, "Baked mesh contains no vertices.");
        var bounds = new Bounds(vertices[0], Vector3.zero);
        for (var index = 1; index < vertices.Count; index++)
        {
            bounds.Encapsulate(vertices[index]);
        }

        return bounds;
    }

    private static void RequireBoundsSize(Bounds actual, Vector3 expectedSize, string context)
    {
        var tolerance = Vector3.Max(Vector3.one * 0.002f, expectedSize * 0.01f);
        var difference = actual.size - expectedSize;
        Require(
            Mathf.Abs(difference.x) <= tolerance.x
            && Mathf.Abs(difference.y) <= tolerance.y
            && Mathf.Abs(difference.z) <= tolerance.z,
            $"{context} baked bounds size was {actual.size}; expected {expectedSize} +/- {tolerance}.");
    }

    private static Dictionary<string, Matrix4x4> CaptureLocalMatrices(IEnumerable<Transform> bones)
    {
        return bones.ToDictionary(
            bone => bone.name,
            bone => Matrix4x4.TRS(bone.localPosition, bone.localRotation, bone.localScale));
    }

    private static float MaxMatrixDifference(
        IReadOnlyDictionary<string, Matrix4x4> left,
        IReadOnlyDictionary<string, Matrix4x4> right)
    {
        var maximum = 0f;
        foreach (var pair in left)
        {
            var other = right[pair.Key];
            for (var row = 0; row < 4; row++)
            {
                for (var column = 0; column < 4; column++)
                {
                    maximum = Mathf.Max(maximum, Mathf.Abs(pair.Value[row, column] - other[row, column]));
                }
            }
        }

        return maximum;
    }

    private static void RequireTransformsFinite(IEnumerable<Transform> bones, string context)
    {
        foreach (var bone in bones)
        {
            Require(IsFinite(bone.localPosition) && IsFinite(bone.localRotation) && IsFinite(bone.localScale),
                $"Bone '{bone.name}' contains NaN or Infinity during {context}.");
        }
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static bool IsFinite(Quaternion value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static int CountExact(IEnumerable<Transform> transforms, string name)
    {
        return transforms.Count(transform => transform.name == name);
    }

    private static int CountPrefix(IEnumerable<Transform> transforms, string prefix)
    {
        return transforms.Count(transform => transform.name.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"HYDRANGEA_WILTRIG_V3_VALIDATION: FAIL - {message}");
        }
    }
}
