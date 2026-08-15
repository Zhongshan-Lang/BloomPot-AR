using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class HydrangeaWiltRigV3Controller : MonoBehaviour
{
    private const string ExpectedSchema = "BloomPot.HydrangeaWiltRig.v3";
    private const int ExpectedBoneCount = 110;
    private const int ExpectedLeafBoneCount = 63;
    private const float LeafIdleCycleDuration = 4.8f;
    private const float LeafIdleResetThreshold = 0.0001f;
    public const float MaximumLeafPitchAmplitude = 0.65f;
    public const float MaximumLeafYawAmplitude = 0.3f;
    public const float MaximumLeafRollAmplitude = 1.6f;
    private static readonly string[] ExpectedBlendShapes =
    {
        "Sprout",
        "Leafing",
        "Bud",
        "HalfBloom",
        "Bloom"
    };

    [SerializeField] private SkinnedMeshRenderer _renderer;
    [SerializeField] private TextAsset _pivotData;
    [SerializeField, Min(0.01f)] private float _transitionDuration = 0.75f;

    private readonly List<BoneBinding> _bindings = new List<BoneBinding>();
    private readonly Dictionary<string, int> _bindingIndices = new Dictionary<string, int>();
    private Matrix4x4[] _deformationMatrices = Array.Empty<Matrix4x4>();
    private Matrix4x4[] _targetWorldMatrices = Array.Empty<Matrix4x4>();
    private Quaternion[] _baseLocalRotations = Array.Empty<Quaternion>();
    private HydrangeaWiltRigV3Document _document;
    private float _pivotCoordinateScale = 1f;
    private Matrix4x4 _jsonToRendererCoordinates = Matrix4x4.identity;
    private float _coordinateHandedness = 1f;
    private float _currentWilt;
    private float _targetWilt;
    private float _leafIdleMotionStrength;
    private float _leafIdleElapsed;
    private bool _leafIdlePoseApplied;
    private int _leafBindingCount;
    private string _stageName = "Bud";
    private string _transitionFromStage;
    private string _transitionToStage;
    private float _growthTransition;
    private bool _usesGrowthTransition;
    private bool _poseDirty = true;
    private bool _initialized;
    private bool _hasLoggedFailure;

    public bool IsInitialized => _initialized;
    public int BoneCount => _bindings.Count;
    public float CurrentWilt => _currentWilt;
    public string CurrentStageName => _stageName;
    public float PivotCoordinateScale => _pivotCoordinateScale;
    public int AnimatedLeafCount => _leafBindingCount;

    private void Awake()
    {
        if (_renderer != null && _pivotData != null)
        {
            Initialize();
        }
    }

    private void LateUpdate()
    {
        if (!_initialized)
        {
            return;
        }

        if (!Mathf.Approximately(_currentWilt, _targetWilt))
        {
            var maxDelta = Time.deltaTime / Mathf.Max(0.01f, _transitionDuration);
            _currentWilt = Mathf.MoveTowards(_currentWilt, _targetWilt, maxDelta);
            _poseDirty = true;
        }

        if (_leafIdleMotionStrength > LeafIdleResetThreshold)
        {
            _leafIdleElapsed += Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.1f);
        }

        if (_poseDirty)
        {
            ApplyPoseNow();
        }
        else
        {
            ApplyLeafIdlePose(false);
        }
    }

    public bool Initialize(SkinnedMeshRenderer renderer, TextAsset pivotData)
    {
        _renderer = renderer;
        _pivotData = pivotData;
        return Initialize();
    }

    public bool Initialize()
    {
        if (_initialized)
        {
            return true;
        }

        try
        {
            InitializeOrThrow();
            _initialized = true;
            _hasLoggedFailure = false;
            _poseDirty = true;
            Require(ApplyPoseNow(), "The initial bind pose could not be applied.");
            Debug.Log(
                $"HydrangeaWiltRigV3Controller: initialized {_bindings.Count} bones from schema {_document.schema}.",
                this);
            return true;
        }
        catch (Exception exception)
        {
            _initialized = false;
            if (!_hasLoggedFailure)
            {
                Debug.LogError($"HydrangeaWiltRigV3Controller initialization failed: {exception.Message}", this);
                _hasLoggedFailure = true;
            }

            return false;
        }
    }

    public void SetGrowthStage(string stageName)
    {
        RequireStage(stageName);
        _stageName = stageName;
        _usesGrowthTransition = false;
        _poseDirty = true;
    }

    public void SetGrowthTransition(string fromStage, string toStage, float value01)
    {
        RequireStage(fromStage);
        RequireStage(toStage);
        _transitionFromStage = fromStage;
        _transitionToStage = toStage;
        _growthTransition = Mathf.Clamp01(value01);
        _stageName = _growthTransition >= 1f ? toStage : fromStage;
        _usesGrowthTransition = true;
        _poseDirty = true;
    }

    public void SetWilt(float value01)
    {
        _currentWilt = Mathf.Clamp01(value01);
        _targetWilt = _currentWilt;
        _poseDirty = true;
        if (_initialized)
        {
            ApplyPoseNow();
        }
    }

    public void SetWiltTarget(float value01)
    {
        _targetWilt = Mathf.Clamp01(value01);
    }

    public void RestoreHealthy()
    {
        SetWiltTarget(0f);
    }

    public void SetLeafIdleMotionStrength(float strength)
    {
        _leafIdleMotionStrength = Mathf.Clamp01(strength);
    }

    public bool ApplyPoseNow()
    {
        if (!_initialized)
        {
            return false;
        }

        try
        {
            ApplyPoseOrThrow();
            ApplyLeafIdlePose(true);
            _poseDirty = false;
            return true;
        }
        catch (Exception exception)
        {
            _initialized = false;
            Debug.LogError($"HydrangeaWiltRigV3Controller pose application failed: {exception.Message}", this);
            return false;
        }
    }

    public static bool ContainsWiltRig(Transform root)
    {
        if (root == null)
        {
            return false;
        }

        return root.GetComponentsInChildren<Transform>(true)
            .Any(child => child.name == "WiltStem_01" || child.name.StartsWith("WiltLeaf_", StringComparison.Ordinal));
    }

    private void InitializeOrThrow()
    {
        if (_renderer == null)
        {
            var renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Require(renderers.Length == 1, $"Expected one SkinnedMeshRenderer, found {renderers.Length}.");
            _renderer = renderers[0];
        }

        Require(_pivotData != null, "Pivot JSON TextAsset is not assigned.");
        Require(_renderer.sharedMesh != null, "SkinnedMeshRenderer has no shared mesh.");
        Require(_renderer.sharedMaterials.Length == 4,
            $"Expected four material slots, found {_renderer.sharedMaterials.Length}.");

        var blendShapeNames = new List<string>();
        for (var i = 0; i < _renderer.sharedMesh.blendShapeCount; i++)
        {
            blendShapeNames.Add(_renderer.sharedMesh.GetBlendShapeName(i));
        }

        Require(blendShapeNames.SequenceEqual(ExpectedBlendShapes),
            $"Expected Blend Shapes [{string.Join(", ", ExpectedBlendShapes)}], " +
            $"found [{string.Join(", ", blendShapeNames)}].");

        _document = JsonUtility.FromJson<HydrangeaWiltRigV3Document>(_pivotData.text);
        Require(_document != null, "Pivot JSON could not be parsed.");
        Require(_document.schema == ExpectedSchema,
            $"Expected JSON schema '{ExpectedSchema}', found '{_document.schema}'.");
        Require(_document.boneCount == ExpectedBoneCount,
            $"Expected JSON boneCount {ExpectedBoneCount}, found {_document.boneCount}.");
        Require(_document.bones != null && _document.bones.Length == ExpectedBoneCount,
            $"Expected {ExpectedBoneCount} JSON bone entries, found {_document.bones?.Length ?? 0}.");

        var rendererBones = _renderer.bones;
        var bindposes = _renderer.sharedMesh.bindposes;
        Require(rendererBones.Length == ExpectedBoneCount,
            $"Expected {ExpectedBoneCount} renderer bones, found {rendererBones.Length}.");
        Require(bindposes.Length == rendererBones.Length,
            $"Renderer bones ({rendererBones.Length}) and bindposes ({bindposes.Length}) differ.");

        var rendererBoneIndices = new Dictionary<string, int>();
        for (var index = 0; index < rendererBones.Length; index++)
        {
            var bone = rendererBones[index];
            Require(bone != null, $"Renderer bone {index} is null.");
            Require(!rendererBoneIndices.ContainsKey(bone.name), $"Duplicate renderer bone name '{bone.name}'.");
            rendererBoneIndices.Add(bone.name, index);
        }

        _bindings.Clear();
        _bindingIndices.Clear();
        _leafBindingCount = 0;
        foreach (var boneData in _document.bones)
        {
            Require(boneData != null && !string.IsNullOrEmpty(boneData.name), "JSON contains an unnamed bone.");
            Require(!_bindingIndices.ContainsKey(boneData.name), $"Duplicate JSON bone name '{boneData.name}'.");
            Require(rendererBoneIndices.TryGetValue(boneData.name, out var rendererIndex),
                $"JSON bone '{boneData.name}' was not found in renderer.bones.");

            var parentIndex = -1;
            if (!string.IsNullOrEmpty(boneData.parent))
            {
                Require(_bindingIndices.TryGetValue(boneData.parent, out parentIndex),
                    $"Parent '{boneData.parent}' for '{boneData.name}' must appear earlier in JSON.");
                Require(rendererBones[rendererIndex].parent == _bindings[parentIndex].Transform,
                    $"Imported parent for '{boneData.name}' does not match JSON parent '{boneData.parent}'.");
            }

            ValidateBoneData(boneData);
            var leafMotionIndex = string.Equals(boneData.kind, "leaf", StringComparison.Ordinal)
                ? _leafBindingCount++
                : -1;
            _bindingIndices.Add(boneData.name, _bindings.Count);
            _bindings.Add(new BoneBinding(
                boneData,
                rendererBones[rendererIndex],
                rendererIndex,
                parentIndex,
                bindposes[rendererIndex],
                leafMotionIndex));
        }

        Require(_leafBindingCount == ExpectedLeafBoneCount,
            $"Expected {ExpectedLeafBoneCount} leaf bones, found {_leafBindingCount}.");

        _deformationMatrices = new Matrix4x4[_bindings.Count];
        _targetWorldMatrices = new Matrix4x4[_bindings.Count];
        _baseLocalRotations = new Quaternion[_bindings.Count];
        _pivotCoordinateScale = CalculateCoordinateTransform(
            out _jsonToRendererCoordinates,
            out _coordinateHandedness);
        ValidatePositiveUniformScale(_renderer.transform.lossyScale);
    }

    private void ApplyPoseOrThrow()
    {
        ValidatePositiveUniformScale(_renderer.transform.lossyScale);
        var rendererLocalToWorld = _renderer.localToWorldMatrix;

        for (var index = 0; index < _bindings.Count; index++)
        {
            var binding = _bindings[index];
            Matrix4x4 totalDeformation;
            if (binding.ParentIndex < 0)
            {
                totalDeformation = Matrix4x4.identity;
            }
            else
            {
                var localDeformation = BuildLocalDeformation(binding.Data);
                totalDeformation = _deformationMatrices[binding.ParentIndex] * localDeformation;
            }

            _deformationMatrices[index] = totalDeformation;
            _targetWorldMatrices[index] = rendererLocalToWorld * totalDeformation * binding.Bindpose.inverse;
            Require(IsFinite(_targetWorldMatrices[index]), $"Target world matrix for '{binding.Data.name}' is invalid.");
        }

        for (var index = 0; index < _bindings.Count; index++)
        {
            var binding = _bindings[index];
            var parentWorldToLocal = binding.ParentIndex >= 0
                ? _targetWorldMatrices[binding.ParentIndex].inverse
                : binding.Transform.parent != null
                    ? binding.Transform.parent.worldToLocalMatrix
                    : Matrix4x4.identity;
            var targetLocal = parentWorldToLocal * _targetWorldMatrices[index];
            DecomposeTrs(targetLocal, binding.Data.name, out var position, out var rotation, out var scale);
            binding.Transform.localPosition = position;
            binding.Transform.localRotation = rotation;
            binding.Transform.localScale = scale;
            _baseLocalRotations[index] = rotation;
        }
    }

    private void ApplyLeafIdlePose(bool basePoseWasJustApplied)
    {
        if (_leafIdleMotionStrength <= LeafIdleResetThreshold)
        {
            if (_leafIdlePoseApplied && !basePoseWasJustApplied)
            {
                for (var index = 0; index < _bindings.Count; index++)
                {
                    var binding = _bindings[index];
                    if (binding.LeafMotionIndex >= 0)
                    {
                        binding.Transform.localRotation = _baseLocalRotations[index];
                    }
                }
            }

            _leafIdlePoseApplied = false;
            return;
        }

        for (var index = 0; index < _bindings.Count; index++)
        {
            var binding = _bindings[index];
            if (binding.LeafMotionIndex < 0)
            {
                continue;
            }

            var idleEuler = EvaluateLeafIdleEuler(
                binding.LeafMotionIndex,
                _leafIdleElapsed,
                _leafIdleMotionStrength);
            binding.Transform.localRotation =
                _baseLocalRotations[index] * Quaternion.Euler(idleEuler);
        }

        _leafIdlePoseApplied = true;
    }

    public static Vector3 EvaluateLeafIdleEuler(
        int leafIndex,
        float elapsed,
        float strength)
    {
        if (leafIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(leafIndex));
        }

        var clampedStrength = Mathf.Clamp01(strength);
        var phaseOffset = Mathf.Repeat(leafIndex * 2.3999632f, Mathf.PI * 2f);
        var speedStep = ((leafIndex * 37) % 19) / 18f;
        var speed = Mathf.Lerp(0.82f, 1.18f, speedStep);
        var direction = (leafIndex & 1) == 0 ? 1f : -1f;
        var phase = elapsed * Mathf.PI * 2f / LeafIdleCycleDuration * speed + phaseOffset;

        return new Vector3(
            Mathf.Sin(phase * 0.73f + 0.6f) * MaximumLeafPitchAmplitude * clampedStrength,
            Mathf.Sin(phase * 0.41f - 0.2f) * MaximumLeafYawAmplitude * clampedStrength,
            Mathf.Sin(phase) * MaximumLeafRollAmplitude * clampedStrength * direction);
    }

    private Matrix4x4 BuildLocalDeformation(HydrangeaWiltRigV3Bone bone)
    {
        var response = Mathf.Pow(Mathf.Clamp01(_currentWilt), Mathf.Max(0.0001f, bone.responseExponent));
        Vector3 pivot;
        Quaternion rotation;
        float scale;

        if (_usesGrowthTransition)
        {
            var from = bone.stages.Get(_transitionFromStage);
            var to = bone.stages.Get(_transitionToStage);
            pivot = Vector3.Lerp(ReadPivot(from), ReadPivot(to), _growthTransition);

            var fromAngle = bone.maxAngleDegrees * from.angleCoefficient * response;
            var toAngle = bone.maxAngleDegrees * to.angleCoefficient * response;
            var fromRotation = Quaternion.AngleAxis(fromAngle, ReadAxis(from));
            var toRotation = Quaternion.AngleAxis(toAngle, ReadAxis(to));
            if (Quaternion.Dot(fromRotation, toRotation) < 0f)
            {
                toRotation = new Quaternion(-toRotation.x, -toRotation.y, -toRotation.z, -toRotation.w);
            }

            rotation = Quaternion.Slerp(fromRotation, toRotation, _growthTransition);
            var fromScale = CalculateScale(bone.minimumScale, from.scaleCoefficient, response);
            var toScale = CalculateScale(bone.minimumScale, to.scaleCoefficient, response);
            scale = Mathf.Lerp(fromScale, toScale, _growthTransition);
        }
        else
        {
            var stage = bone.stages.Get(_stageName);
            pivot = ReadPivot(stage);
            rotation = Quaternion.AngleAxis(
                bone.maxAngleDegrees * stage.angleCoefficient * response,
                ReadAxis(stage));
            scale = CalculateScale(bone.minimumScale, stage.scaleCoefficient, response);
        }

        return Matrix4x4.Translate(pivot)
            * Matrix4x4.Rotate(rotation)
            * Matrix4x4.Scale(Vector3.one * scale)
            * Matrix4x4.Translate(-pivot);
    }

    private void RequireStage(string stageName)
    {
        if (_document == null || !ExpectedBlendShapes.Contains(stageName))
        {
            throw new ArgumentException($"Unknown Hydrangea growth stage '{stageName}'.", nameof(stageName));
        }
    }

    private static float CalculateScale(float minimumScale, float coefficient, float response)
    {
        return 1f - (1f - minimumScale) * coefficient * response;
    }

    private Vector3 ReadPivot(HydrangeaWiltRigV3Stage stage)
    {
        return _jsonToRendererCoordinates.MultiplyPoint3x4(ReadVector(stage.pivotUnity));
    }

    private Vector3 ReadAxis(HydrangeaWiltRigV3Stage stage)
    {
        // Rotation axes are pseudovectors, so mirrored coordinate systems add a sign flip.
        return (_jsonToRendererCoordinates.MultiplyVector(ReadVector(stage.axisUnity))
            * _coordinateHandedness).normalized;
    }

    private float CalculateCoordinateTransform(
        out Matrix4x4 coordinateTransform,
        out float coordinateHandedness)
    {
        var normal = Matrix4x4.zero;
        var targetX = Vector4.zero;
        var targetY = Vector4.zero;
        var targetZ = Vector4.zero;
        foreach (var binding in _bindings)
        {
            if (binding.ParentIndex < 0)
            {
                continue;
            }

            var pivot = ReadVector(binding.Data.stages.Bloom.pivotUnity);
            var source = new Vector4(pivot.x, pivot.y, pivot.z, 1f);
            var target = (Vector3)binding.Bindpose.inverse.GetColumn(3);
            for (var row = 0; row < 4; row++)
            {
                for (var column = 0; column < 4; column++)
                {
                    normal[row, column] += source[row] * source[column];
                }

                targetX[row] += source[row] * target.x;
                targetY[row] += source[row] * target.y;
                targetZ[row] += source[row] * target.z;
            }
        }

        Require(Mathf.Abs(normal.determinant) > 0.000001f,
            "Bloom pivots cannot determine an FBX coordinate transform.");
        var inverseNormal = normal.inverse;
        var rowX = inverseNormal * targetX;
        var rowY = inverseNormal * targetY;
        var rowZ = inverseNormal * targetZ;
        var fitted = Matrix4x4.identity;
        fitted.SetRow(0, rowX);
        fitted.SetRow(1, rowY);
        fitted.SetRow(2, rowZ);

        var right = (Vector3)fitted.GetColumn(0);
        var up = (Vector3)fitted.GetColumn(1);
        var forward = (Vector3)fitted.GetColumn(2);
        var scales = new Vector3(right.magnitude, up.magnitude, forward.magnitude);
        var coordinateScale = (scales.x + scales.y + scales.z) / 3f;
        Require(coordinateScale > 0.000001f && coordinateScale < 1000f,
            $"Invalid FBX/JSON coordinate scale {coordinateScale:G9}.");
        Require(Mathf.Abs(coordinateScale - 0.01f) <= 0.000001f,
            $"WiltRig v3 expects an FBX/JSON coordinate scale of 0.01, found {coordinateScale:G9}.");
        Require(Mathf.Max(scales.x, Mathf.Max(scales.y, scales.z))
                - Mathf.Min(scales.x, Mathf.Min(scales.y, scales.z))
                <= coordinateScale * 0.002f,
            $"FBX/JSON coordinate transform has non-uniform scale {scales}.");

        right /= scales.x;
        up /= scales.y;
        forward /= scales.z;
        Require(Mathf.Abs(Vector3.Dot(right, up)) < 0.002f
                && Mathf.Abs(Vector3.Dot(right, forward)) < 0.002f
                && Mathf.Abs(Vector3.Dot(up, forward)) < 0.002f,
            "FBX/JSON coordinate transform contains shear.");
        var handedness = Vector3.Dot(Vector3.Cross(right, up), forward);
        Require(Mathf.Abs(handedness) > 0.999f,
            $"FBX/JSON coordinate transform has invalid handedness {handedness:G9}.");

        var maximumResidual = 0f;
        foreach (var binding in _bindings)
        {
            if (binding.ParentIndex < 0)
            {
                continue;
            }

            var expectedOrigin = fitted.MultiplyPoint3x4(
                ReadVector(binding.Data.stages.Bloom.pivotUnity));
            var importedRestOrigin = (Vector3)binding.Bindpose.inverse.GetColumn(3);
            maximumResidual = Mathf.Max(maximumResidual, Vector3.Distance(expectedOrigin, importedRestOrigin));
        }

        const float residualTolerance = 0.00002f;
        Require(maximumResidual <= residualTolerance,
            $"FBX bindpose origins do not match transformed Bloom pivots. " +
            $"scale={coordinateScale:G9}, max residual={maximumResidual:G9}, " +
            $"tolerance={residualTolerance:G9}.");

        coordinateTransform = fitted;
        coordinateHandedness = handedness < 0f ? -1f : 1f;
        Debug.Log(
            $"HydrangeaWiltRigV3Controller: FBX/JSON coordinate scale {coordinateScale:G9}, " +
            $"handedness {coordinateHandedness:+0;-0}, " +
            $"transform rows [{FormatTransformRow(fitted, 0)}; {FormatTransformRow(fitted, 1)}; " +
            $"{FormatTransformRow(fitted, 2)}], max bindpose residual {maximumResidual:G9}.",
            this);
        return coordinateScale;
    }

    private static string FormatTransformRow(Matrix4x4 matrix, int row)
    {
        return $"{matrix[row, 0]:G5},{matrix[row, 1]:G5},{matrix[row, 2]:G5},{matrix[row, 3]:G5}";
    }

    private static Vector3 ReadVector(float[] values)
    {
        return new Vector3(values[0], values[1], values[2]);
    }

    private static void ValidateBoneData(HydrangeaWiltRigV3Bone bone)
    {
        Require(bone.stages != null, $"Bone '{bone.name}' has no stage data.");
        foreach (var stageName in ExpectedBlendShapes)
        {
            var stage = bone.stages.Get(stageName);
            Require(stage != null, $"Bone '{bone.name}' has no '{stageName}' data.");
            Require(stage.pivotUnity != null && stage.pivotUnity.Length == 3,
                $"Bone '{bone.name}' stage '{stageName}' has an invalid pivotUnity.");
            Require(stage.axisUnity != null && stage.axisUnity.Length == 3,
                $"Bone '{bone.name}' stage '{stageName}' has an invalid axisUnity.");
            Require(ReadVector(stage.axisUnity).sqrMagnitude > 0.99f,
                $"Bone '{bone.name}' stage '{stageName}' has a zero rotation axis.");
        }
    }

    private static void ValidatePositiveUniformScale(Vector3 scale)
    {
        Require(scale.x > 0f && scale.y > 0f && scale.z > 0f,
            $"WiltRig requires positive parent scale, found {scale}.");
        var largest = Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
        var smallest = Mathf.Min(scale.x, Mathf.Min(scale.y, scale.z));
        Require(largest - smallest <= Mathf.Max(0.0001f, largest * 0.001f),
            $"WiltRig requires uniform parent scale, found {scale}.");
    }

    private static void DecomposeTrs(
        Matrix4x4 matrix,
        string boneName,
        out Vector3 position,
        out Quaternion rotation,
        out Vector3 scale)
    {
        Require(IsFinite(matrix), $"Local matrix for '{boneName}' contains NaN or Infinity.");
        position = matrix.GetColumn(3);

        var right = (Vector3)matrix.GetColumn(0);
        var up = (Vector3)matrix.GetColumn(1);
        var forward = (Vector3)matrix.GetColumn(2);
        scale = new Vector3(right.magnitude, up.magnitude, forward.magnitude);
        Require(scale.x > 0.000001f && scale.y > 0.000001f && scale.z > 0.000001f,
            $"Local matrix for '{boneName}' has a zero scale axis.");
        Require(matrix.determinant > 0f, $"Local matrix for '{boneName}' is mirrored or singular.");

        right /= scale.x;
        up /= scale.y;
        forward /= scale.z;
        Require(Mathf.Abs(Vector3.Dot(right, up)) < 0.002f
                && Mathf.Abs(Vector3.Dot(right, forward)) < 0.002f
                && Mathf.Abs(Vector3.Dot(up, forward)) < 0.002f,
            $"Local matrix for '{boneName}' contains unsupported shear.");

        rotation = Quaternion.LookRotation(forward, up);
        Require(IsFinite(rotation), $"Local rotation for '{boneName}' is invalid.");
    }

    private static bool IsFinite(Matrix4x4 matrix)
    {
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                if (!IsFinite(matrix[row, column]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsFinite(Quaternion value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private readonly struct BoneBinding
    {
        public BoneBinding(
            HydrangeaWiltRigV3Bone data,
            Transform transform,
            int rendererIndex,
            int parentIndex,
            Matrix4x4 bindpose,
            int leafMotionIndex)
        {
            Data = data;
            Transform = transform;
            RendererIndex = rendererIndex;
            ParentIndex = parentIndex;
            Bindpose = bindpose;
            LeafMotionIndex = leafMotionIndex;
        }

        public HydrangeaWiltRigV3Bone Data { get; }
        public Transform Transform { get; }
        public int RendererIndex { get; }
        public int ParentIndex { get; }
        public Matrix4x4 Bindpose { get; }
        public int LeafMotionIndex { get; }
    }
}

[Serializable]
public sealed class HydrangeaWiltRigV3Document
{
    public string schema;
    public string units;
    public int boneCount;
    public HydrangeaWiltRigV3Bone[] bones;
}

[Serializable]
public sealed class HydrangeaWiltRigV3Bone
{
    public string name;
    public string kind;
    public string parent;
    public float maxAngleDegrees;
    public float minimumScale = 1f;
    public float responseExponent = 1.35f;
    public HydrangeaWiltRigV3Stages stages;
}

[Serializable]
public sealed class HydrangeaWiltRigV3Stages
{
    public HydrangeaWiltRigV3Stage Sprout;
    public HydrangeaWiltRigV3Stage Leafing;
    public HydrangeaWiltRigV3Stage Bud;
    public HydrangeaWiltRigV3Stage HalfBloom;
    public HydrangeaWiltRigV3Stage Bloom;

    public HydrangeaWiltRigV3Stage Get(string stageName)
    {
        switch (stageName)
        {
            case "Sprout": return Sprout;
            case "Leafing": return Leafing;
            case "Bud": return Bud;
            case "HalfBloom": return HalfBloom;
            case "Bloom": return Bloom;
            default: throw new ArgumentException($"Unknown Hydrangea growth stage '{stageName}'.", nameof(stageName));
        }
    }
}

[Serializable]
public sealed class HydrangeaWiltRigV3Stage
{
    public float[] pivotUnity;
    public float[] axisUnity;
    public float angleCoefficient;
    public float scaleCoefficient;
}
