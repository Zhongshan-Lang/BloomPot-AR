using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class HydrangeaView : MonoBehaviour
{
    private const string PetalMaterialResourcePath = "HydrangeaInteractive/Materials/M_Hydrangea_Petals_Random";
    private const string CenterMaterialResourcePath = "HydrangeaInteractive/Materials/M_Hydrangea_Centers";
    private static readonly string[] GrowthStageNames =
        { "Seed", "Sprout", "Leafing", "Bud", "HalfBloom", "Bloom" };
    private static readonly string[] ManagedBlendShapes = { "Sprout", "Leafing", "Bud", "HalfBloom", "Bloom" };
    private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
    private static readonly int BloomMaturityProperty = Shader.PropertyToID("_BloomMaturity");
    private static readonly int WiltAmountProperty = Shader.PropertyToID("_WiltAmount");
    private static readonly int WiltColorProperty = Shader.PropertyToID("_WiltColor");
    private static readonly int WiltStrengthProperty = Shader.PropertyToID("_WiltStrength");
    private static readonly int WiltBrightnessProperty = Shader.PropertyToID("_WiltBrightness");
    private const float MaximumCriticalWiltTintStrength = 0.85f;
    private const float MinimumCriticalWiltBrightness = 0.58f;

    private readonly Dictionary<string, int> _blendShapeIndices = new Dictionary<string, int>();
    private readonly PlantLifeAnimationController _lifeAnimationController =
        new PlantLifeAnimationController();

    private SkinnedMeshRenderer _renderer;
    private HydrangeaDroopRigController _droopController;
    private HydrangeaWiltRigV3Controller _wiltRigV3Controller;
    private MaterialPropertyBlock _materialPropertyBlock;
    private Coroutine _transitionRoutine;
    private Coroutine _responseRoutine;
    private Color[] _materialBaseColors = new Color[0];
    private Color _wiltTint = new Color(0.95f, 0.78f, 0.32f, 1f);
    private float _wiltTintStrength = 0.35f;
    private float _currentBloomMaturity;
    private float _currentGrowthProgress;
    private float _requestedWilt;
    private float _appearanceDecay;
    private float _lastAppliedBloomMaturity = -1f;
    private float _lastAppliedAppearanceDecay = -1f;
    private int _fallbackBlendShapeIndex = -1;
    private int[] _rendererBlendShapeIndices = new int[0];
    private string _currentStageName;
    private float _responseScale = 1f;
    private float _responseRoll;
    private float _lifeScale = 1f;
    private Vector3 _lifeRotationEuler;

    public bool IsReady => _renderer != null && CanPresentAnyStage();
    public bool SupportsWilt => _droopController != null || _wiltRigV3Controller != null;
    public string CurrentStageName => _currentStageName;
    public float CurrentGrowthProgress => _currentGrowthProgress;
    public float ResponseScale => _responseScale;
    public float ResponseRoll => _responseRoll;
    public float LifeScale => _lifeScale;
    public Vector3 LifeRotationEuler => _lifeRotationEuler;

    public bool Bind(
        SkinnedMeshRenderer renderer,
        HydrangeaDroopRigController droopController,
        HydrangeaWiltRigV3Controller wiltRigV3Controller,
        float initialWilt,
        float initialAppearanceDecay,
        Color wiltTint,
        float wiltTintStrength)
    {
        Unbind();
        if (renderer == null || renderer.sharedMesh == null)
        {
            return false;
        }

        _renderer = renderer;
        _droopController = droopController;
        _wiltRigV3Controller = wiltRigV3Controller;
        _wiltTint = wiltTint;
        _wiltTintStrength = Mathf.Clamp(wiltTintStrength, 0f, 0.5f);
        _appearanceDecay = Mathf.Clamp01(initialAppearanceDecay);

        ConfigureRenderer();
        ResolveBlendShapes();
        if (!CanPresentAnyStage())
        {
            Unbind();
            return false;
        }

        SetWiltImmediate(initialWilt);
        ApplyInitialVisibleStage();
        return true;
    }

    public void Unbind()
    {
        if (_transitionRoutine != null)
        {
            StopCoroutine(_transitionRoutine);
            _transitionRoutine = null;
        }

        if (_responseRoutine != null)
        {
            StopCoroutine(_responseRoutine);
            _responseRoutine = null;
        }

        _renderer = null;
        _droopController = null;
        _wiltRigV3Controller = null;
        _blendShapeIndices.Clear();
        _rendererBlendShapeIndices = new int[0];
        _fallbackBlendShapeIndex = -1;
        _currentStageName = null;
        _currentBloomMaturity = 0f;
        _currentGrowthProgress = 0f;
        _requestedWilt = 0f;
        _appearanceDecay = 0f;
        _responseScale = 1f;
        _responseRoll = 0f;
        _lifeScale = 1f;
        _lifeRotationEuler = Vector3.zero;
        _lifeAnimationController.Reset();
        ResetMaterialAppearanceCache();
    }

    public void Tick(bool allowLifeAnimation, float vitality, float appearanceDecay)
    {
        var lifeFrame = _lifeAnimationController.Evaluate(
            allowLifeAnimation && _transitionRoutine == null,
            vitality,
            Time.unscaledDeltaTime);
        _lifeScale = lifeFrame.Scale;
        _lifeRotationEuler = lifeFrame.RotationEuler;
        _wiltRigV3Controller?.SetLeafIdleMotionStrength(lifeFrame.MotionStrength);
        _appearanceDecay = Mathf.Clamp01(appearanceDecay);
        ApplyMaterialAppearance(_appearanceDecay);
    }

    public void SetWiltTarget(float value01)
    {
        var wilt = Mathf.Clamp01(value01);
        _requestedWilt = wilt;
        _droopController?.SetDroopTarget(wilt);
        _wiltRigV3Controller?.SetWiltTarget(wilt);
    }

    public void PlayInteractionResponse(float strength = 1f)
    {
        if (_renderer == null)
        {
            return;
        }

        if (_responseRoutine != null)
        {
            StopCoroutine(_responseRoutine);
        }

        _responseRoutine = StartCoroutine(AnimateInteractionResponse(Mathf.Clamp01(strength)));
    }

    public void SetWiltImmediate(float value01)
    {
        var wilt = Mathf.Clamp01(value01);
        _requestedWilt = wilt;
        _droopController?.SetDroop(wilt);
        _wiltRigV3Controller?.SetWilt(wilt);
        ApplyMaterialAppearance(_appearanceDecay, true);
    }

    public void SetAppearanceDecayImmediate(float value01)
    {
        _appearanceDecay = Mathf.Clamp01(value01);
        ApplyMaterialAppearance(_appearanceDecay, true);
    }

    public void SetStage(string blendShapeName)
    {
        if (_renderer == null || !CanPresentStage(blendShapeName))
        {
            return;
        }

        SetGrowthProgressTarget(
            PlantStageProgressionController.GetStageAnchor(GetStageIndex(blendShapeName)),
            0.35f);
    }

    public bool SetStageImmediate(string blendShapeName)
    {
        if (_renderer == null || !CanPresentStage(blendShapeName))
        {
            return false;
        }

        return SetGrowthProgressImmediate(
            PlantStageProgressionController.GetStageAnchor(GetStageIndex(blendShapeName)));
    }

    public void SetGrowthProgressTarget(float growthProgress, float duration)
    {
        if (_renderer == null || !CanPresentAnyStage())
        {
            return;
        }

        if (_transitionRoutine != null)
        {
            StopCoroutine(_transitionRoutine);
        }

        var targetProgress = Mathf.Clamp01(growthProgress);
        if (duration <= 0f || Mathf.Abs(targetProgress - _currentGrowthProgress) < 0.0001f)
        {
            SetGrowthProgressImmediate(targetProgress);
            return;
        }

        _transitionRoutine = StartCoroutine(AnimateGrowthProgress(targetProgress, duration));
    }

    public bool SetGrowthProgressImmediate(float growthProgress)
    {
        if (_renderer == null || !CanPresentAnyStage())
        {
            return false;
        }

        if (_transitionRoutine != null)
        {
            StopCoroutine(_transitionRoutine);
            _transitionRoutine = null;
        }

        ApplyGrowthProgress(Mathf.Clamp01(growthProgress));
        return true;
    }

    public bool CanPresentStage(string stageName)
    {
        if (_renderer == null || _renderer.sharedMesh == null)
        {
            return false;
        }

        if (stageName == "Seed")
        {
            return _blendShapeIndices.ContainsKey("Sprout");
        }

        if (_blendShapeIndices.ContainsKey(stageName))
        {
            return true;
        }

        return stageName == "HalfBloom"
            && _blendShapeIndices.ContainsKey("Bud")
            && _blendShapeIndices.ContainsKey("Bloom");
    }

    private IEnumerator AnimateGrowthProgress(float targetProgress, float duration)
    {
        var fromProgress = _currentGrowthProgress;
        var elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            ApplyGrowthProgress(Mathf.Lerp(fromProgress, targetProgress, t));
            yield return null;
        }

        ApplyGrowthProgress(targetProgress);
        _transitionRoutine = null;
    }

    private IEnumerator AnimateInteractionResponse(float strength)
    {
        const float duration = 0.58f;
        var elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            var t = Mathf.Clamp01(elapsed / duration);
            var envelope = 1f - t;
            _responseScale = 1f + Mathf.Sin(t * Mathf.PI) * 0.035f * strength;
            _responseRoll = Mathf.Sin(t * Mathf.PI * 3f) * envelope * 2.2f * strength;
            yield return null;
        }

        _responseScale = 1f;
        _responseRoll = 0f;
        _responseRoutine = null;
    }

    private void ApplyStageImmediately(string blendShapeName)
    {
        ApplyGrowthProgress(
            PlantStageProgressionController.GetStageAnchor(GetStageIndex(blendShapeName)));
    }

    private void ApplyGrowthProgress(float growthProgress)
    {
        if (_renderer == null)
        {
            return;
        }

        var progress = Mathf.Clamp01(growthProgress);
        var lowerStageIndex = PlantStageProgressionController.GetStageIndexForProgress(progress);
        var upperStageIndex = Mathf.Min(lowerStageIndex + 1, PlantState.MaximumStageIndex);
        var lowerAnchor = PlantStageProgressionController.GetStageAnchor(lowerStageIndex);
        var upperAnchor = PlantStageProgressionController.GetStageAnchor(upperStageIndex);
        var segmentProgress = upperStageIndex == lowerStageIndex
            ? 0f
            : Mathf.InverseLerp(lowerAnchor, upperAnchor, progress);
        var lowerStageName = GrowthStageNames[lowerStageIndex];
        var upperStageName = GrowthStageNames[upperStageIndex];
        if (!CanPresentStage(lowerStageName) || !CanPresentStage(upperStageName))
        {
            return;
        }

        foreach (var index in _rendererBlendShapeIndices)
        {
            var lowerWeight = GetTargetWeight(lowerStageName, index);
            var upperWeight = GetTargetWeight(upperStageName, index);
            _renderer.SetBlendShapeWeight(index, Mathf.Lerp(lowerWeight, upperWeight, segmentProgress));
        }

        SetBloomMaturity(Mathf.Lerp(
            GetBloomMaturity(lowerStageName),
            GetBloomMaturity(upperStageName),
            segmentProgress));
        SetDroopStageScale(Vector2.Lerp(
            GetDroopStageScale(lowerStageName),
            GetDroopStageScale(upperStageName),
            segmentProgress));
        _wiltRigV3Controller?.SetGrowthTransition(
            GetWiltRigStageName(lowerStageName),
            GetWiltRigStageName(upperStageName),
            segmentProgress);
        _currentGrowthProgress = progress;
        _currentStageName = lowerStageName;
    }

    private void SetDroopStageScale(Vector2 stageScale)
    {
        _droopController?.SetStageScale(stageScale.x, stageScale.y);
    }

    private static Vector2 GetDroopStageScale(string stageName)
    {
        switch (stageName)
        {
            case "Seed":
            case "Sprout":
                return Vector2.zero;
            case "Leafing":
                return new Vector2(0.4f, 0f);
            case "Bud":
                return new Vector2(0.7f, 0.55f);
            case "HalfBloom":
                return new Vector2(0.9f, 0.85f);
            case "Bloom":
                return Vector2.one;
            default:
                return Vector2.zero;
        }
    }

    private void SetBloomMaturity(float maturity)
    {
        _currentBloomMaturity = Mathf.Clamp01(maturity);
        ApplyMaterialAppearance(_appearanceDecay);
    }

    private void ConfigureRenderer()
    {
        _renderer.enabled = true;
        _renderer.updateWhenOffscreen = true;
        ApplyHydrangeaMaterialOverrides();
        _renderer.SetPropertyBlock(null);
        CacheMaterialBaseColors();
        ApplyMaterialAppearance(_appearanceDecay, true);

        var meshBounds = _renderer.sharedMesh.bounds;
        if (meshBounds.extents == Vector3.zero)
        {
            meshBounds = new Bounds(Vector3.zero, Vector3.one * 100f);
        }

        _renderer.localBounds = meshBounds;
    }

    private void ApplyHydrangeaMaterialOverrides()
    {
        var materials = _renderer.sharedMaterials;
        if (materials.Length == 0)
        {
            return;
        }

        var petalMaterial = Resources.Load<Material>(PetalMaterialResourcePath);
        if (petalMaterial == null)
        {
            Debug.LogError("HydrangeaView: petal material resource is missing.", this);
            return;
        }

        var centerMaterial = Resources.Load<Material>(CenterMaterialResourcePath);
        if (centerMaterial == null)
        {
            Debug.LogError("HydrangeaView: flower center material resource is missing.", this);
            return;
        }

        var mesh = _renderer.sharedMesh;
        var slotCount = Mathf.Min(materials.Length, mesh != null ? mesh.subMeshCount : materials.Length);
        var petalSlots = new List<int>();
        var centerSlots = new List<int>();
        var genericFlowerSlots = new List<int>();

        for (var i = 0; i < slotCount; i++)
        {
            var materialName = materials[i] != null ? materials[i].name.ToLowerInvariant() : string.Empty;
            if (materialName.Contains("center"))
            {
                centerSlots.Add(i);
            }
            else if (materialName.Contains("petal"))
            {
                petalSlots.Add(i);
            }
            else if (materialName.Contains("flower"))
            {
                genericFlowerSlots.Add(i);
            }
        }

        // Older material remaps gave both flower submeshes the same generic name.
        foreach (var slot in genericFlowerSlots)
        {
            if (petalSlots.Count == 0)
            {
                petalSlots.Add(slot);
            }
            else if (centerSlots.Count == 0 && slotCount >= 4)
            {
                centerSlots.Add(slot);
            }
            else
            {
                petalSlots.Add(slot);
            }
        }

        if (petalSlots.Count == 0 && slotCount >= 3)
        {
            petalSlots.Add(2);
        }

        if (centerSlots.Count == 0 && slotCount >= 4)
        {
            centerSlots.Add(3);
        }

        if (petalSlots.Count == 0)
        {
            Debug.LogWarning("HydrangeaView: no petal material slot was found; material override was skipped.", this);
            return;
        }

        foreach (var slot in petalSlots)
        {
            materials[slot] = petalMaterial;
        }

        foreach (var slot in centerSlots)
        {
            materials[slot] = centerMaterial;
        }

        _renderer.sharedMaterials = materials;
        Debug.Log(
            $"HydrangeaView: applied petal material to slot(s) {string.Join(", ", petalSlots)} " +
            $"and center material to slot(s) {string.Join(", ", centerSlots)}.",
            this);
    }

    private void CacheMaterialBaseColors()
    {
        var materials = _renderer != null ? _renderer.sharedMaterials : new Material[0];
        _materialBaseColors = new Color[materials.Length];
        for (var i = 0; i < materials.Length; i++)
        {
            var material = materials[i];
            _materialBaseColors[i] = material != null && material.HasProperty(BaseColorProperty)
                ? material.GetColor(BaseColorProperty)
                : Color.white;
        }

        _lastAppliedBloomMaturity = -1f;
        _lastAppliedAppearanceDecay = -1f;
    }

    private void ApplyMaterialAppearance(float appearanceDecay, bool force = false)
    {
        if (_renderer == null)
        {
            return;
        }

        var materials = _renderer.sharedMaterials;
        if (_materialBaseColors.Length != materials.Length)
        {
            CacheMaterialBaseColors();
        }

        var clampedDecay = Mathf.Clamp01(appearanceDecay);
        if (!force
            && Mathf.Abs(_lastAppliedAppearanceDecay - clampedDecay) < 0.0001f
            && Mathf.Abs(_lastAppliedBloomMaturity - _currentBloomMaturity) < 0.0001f)
        {
            return;
        }

        var smoothWilt = Mathf.SmoothStep(0f, 1f, clampedDecay);
        var criticalProgress = Mathf.InverseLerp(
            PlantGrowthController.AppearanceDecayAtCriticalThreshold,
            1f,
            clampedDecay);
        var smoothCriticalProgress = Mathf.SmoothStep(0f, 1f, criticalProgress);
        var criticalTintStrength = Mathf.Lerp(
            _wiltTintStrength,
            MaximumCriticalWiltTintStrength,
            smoothCriticalProgress);
        var criticalBrightness = Mathf.Lerp(
            1f,
            MinimumCriticalWiltBrightness,
            smoothCriticalProgress);
        var tintAmount = smoothWilt * criticalTintStrength;
        var standardMaterialMultiplier = Color.Lerp(Color.white, _wiltTint, tintAmount);
        _materialPropertyBlock ??= new MaterialPropertyBlock();

        for (var i = 0; i < materials.Length; i++)
        {
            var material = materials[i];
            if (material == null)
            {
                continue;
            }

            _materialPropertyBlock.Clear();
            _renderer.GetPropertyBlock(_materialPropertyBlock, i);
            _materialPropertyBlock.SetFloat(BloomMaturityProperty, _currentBloomMaturity);
            _materialPropertyBlock.SetFloat(WiltAmountProperty, smoothWilt);
            _materialPropertyBlock.SetColor(WiltColorProperty, _wiltTint);
            if (material.HasProperty(WiltStrengthProperty))
            {
                _materialPropertyBlock.SetFloat(WiltStrengthProperty, criticalTintStrength);
            }

            if (material.HasProperty(WiltBrightnessProperty))
            {
                _materialPropertyBlock.SetFloat(WiltBrightnessProperty, criticalBrightness);
            }

            if (material.HasProperty(BaseColorProperty))
            {
                var baseColor = _materialBaseColors[i];
                if (!material.HasProperty(WiltAmountProperty))
                {
                    baseColor = new Color(
                        baseColor.r * standardMaterialMultiplier.r * criticalBrightness,
                        baseColor.g * standardMaterialMultiplier.g * criticalBrightness,
                        baseColor.b * standardMaterialMultiplier.b * criticalBrightness,
                        baseColor.a);
                }

                _materialPropertyBlock.SetColor(BaseColorProperty, baseColor);
            }

            _renderer.SetPropertyBlock(_materialPropertyBlock, i);
        }

        _lastAppliedBloomMaturity = _currentBloomMaturity;
        _lastAppliedAppearanceDecay = clampedDecay;
    }

    private void ResetMaterialAppearanceCache()
    {
        _materialBaseColors = new Color[0];
        _lastAppliedBloomMaturity = -1f;
        _lastAppliedAppearanceDecay = -1f;
    }

    private void ResolveBlendShapes()
    {
        _blendShapeIndices.Clear();
        _fallbackBlendShapeIndex = -1;
        if (_renderer == null || _renderer.sharedMesh == null)
        {
            return;
        }

        if (_renderer.sharedMesh.blendShapeCount > 0)
        {
            _fallbackBlendShapeIndex = 0;
        }

        foreach (var blendShapeName in ManagedBlendShapes)
        {
            var index = ResolveBlendShapeIndex(blendShapeName);
            if (index >= 0)
            {
                _blendShapeIndices[blendShapeName] = index;
            }
        }

        var uniqueIndices = new List<int>(_blendShapeIndices.Count);
        foreach (var index in _blendShapeIndices.Values)
        {
            if (!uniqueIndices.Contains(index))
            {
                uniqueIndices.Add(index);
            }
        }

        uniqueIndices.Sort();
        _rendererBlendShapeIndices = uniqueIndices.ToArray();
    }

    private void ApplyInitialVisibleStage()
    {
        if (_renderer == null || _renderer.sharedMesh == null)
        {
            return;
        }

        if (_blendShapeIndices.ContainsKey("Bud"))
        {
            _currentStageName = "Bud";
            ApplyStageImmediately("Bud");
            return;
        }

        if (_fallbackBlendShapeIndex < 0)
        {
            return;
        }

        for (var i = 0; i < _renderer.sharedMesh.blendShapeCount; i++)
        {
            _renderer.SetBlendShapeWeight(i, i == _fallbackBlendShapeIndex ? 100f : 0f);
        }

        SetBloomMaturity(GetBloomMaturity("Bud"));
        SetDroopStageScale(GetDroopStageScale("Bud"));
        Debug.Log(
            $"HydrangeaView: Bud blend shape was not found, using fallback blend shape index " +
            $"{_fallbackBlendShapeIndex} ({_renderer.sharedMesh.GetBlendShapeName(_fallbackBlendShapeIndex)}).",
            this);
    }

    private int ResolveBlendShapeIndex(string canonicalStageName)
    {
        if (_renderer == null || _renderer.sharedMesh == null)
        {
            return -1;
        }

        var directIndex = _renderer.sharedMesh.GetBlendShapeIndex(canonicalStageName);
        if (directIndex >= 0)
        {
            return directIndex;
        }

        var canonicalLower = canonicalStageName.ToLowerInvariant();
        for (var i = 0; i < _renderer.sharedMesh.blendShapeCount; i++)
        {
            var actualName = _renderer.sharedMesh.GetBlendShapeName(i).ToLowerInvariant();
            if (actualName.Contains(canonicalLower))
            {
                return i;
            }
        }

        return -1;
    }

    private bool CanPresentAnyStage()
    {
        foreach (var stageName in ManagedBlendShapes)
        {
            if (CanPresentStage(stageName))
            {
                return true;
            }
        }

        return false;
    }

    private float GetTargetWeight(string stageName, int blendShapeIndex)
    {
        if (stageName == "Seed")
        {
            return 0f;
        }

        if (_blendShapeIndices.TryGetValue(stageName, out var directIndex))
        {
            return blendShapeIndex == directIndex ? 100f : 0f;
        }

        if (stageName == "HalfBloom"
            && _blendShapeIndices.TryGetValue("Bud", out var budIndex)
            && _blendShapeIndices.TryGetValue("Bloom", out var bloomIndex))
        {
            if (blendShapeIndex == budIndex)
            {
                return 40f;
            }

            return blendShapeIndex == bloomIndex ? 60f : 0f;
        }

        return 0f;
    }

    private static string GetWiltRigStageName(string stageName)
    {
        return stageName == "Seed" ? "Sprout" : stageName;
    }

    private static float GetBloomMaturity(string stageName)
    {
        switch (stageName)
        {
            case "Bud":
                return 0.05f;
            case "HalfBloom":
                return 0.52f;
            case "Bloom":
                return 1f;
            default:
                return 0f;
        }
    }

    private static int GetStageIndex(string stageName)
    {
        for (var i = 0; i < GrowthStageNames.Length; i++)
        {
            if (GrowthStageNames[i] == stageName)
            {
                return i;
            }
        }

        return PlantState.MinimumStageIndex;
    }
}
