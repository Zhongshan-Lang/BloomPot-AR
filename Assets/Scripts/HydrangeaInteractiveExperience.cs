using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Vuforia;
using UIImage = UnityEngine.UI.Image;

public sealed class HydrangeaInteractiveExperience : MonoBehaviour
{
    private const float ModelLoadRetryDelay = 2f;
    private const string PetalMaterialResourcePath = "HydrangeaInteractive/Materials/M_Hydrangea_Petals_Random";
    private const string CenterMaterialResourcePath = "HydrangeaInteractive/Materials/M_Hydrangea_Centers";
    private const string SceneModelName = "Hydrangea_Growth_BlendShape_Unity";
    private static readonly string[] ManagedBlendShapes = { "Sprout", "Leafing", "Bud", "HalfBloom", "Bloom" };
    private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
    private static readonly int BloomMaturityProperty = Shader.PropertyToID("_BloomMaturity");
    private static readonly int WiltAmountProperty = Shader.PropertyToID("_WiltAmount");
    private static readonly int WiltColorProperty = Shader.PropertyToID("_WiltColor");
    private static readonly Vector3 BlenderAxisFixEuler = new Vector3(-90f, 0f, 0f);

    [Header("Placement")]
    [SerializeField] private Vector3 _modelLocalPosition = Vector3.zero;
    [SerializeField] private bool _applyBlenderAxisFix = true;
    [SerializeField] private Vector3 _additionalEulerOffset = Vector3.zero;
    [SerializeField] private float _modelBaseScale = 0.02f;

    [Header("Demo Testing")]
    [SerializeField] private bool _showDroopTestControls;
    [SerializeField] private TextAsset _wiltRigV3PivotData;

    [Header("Wilt Appearance")]
    [SerializeField] private Color _wiltTint = new Color(0.95f, 0.78f, 0.32f, 1f);
    [SerializeField, Range(0f, 0.5f)] private float _wiltTintStrength = 0.35f;

    private readonly Dictionary<string, int> _blendShapeIndices = new Dictionary<string, int>();
    private readonly Dictionary<Button, string> _buttonStages = new Dictionary<Button, string>();
    private readonly Dictionary<Button, float> _buttonDroopLevels = new Dictionary<Button, float>();

    private ObserverBehaviour _observer;
    private Transform _stageRoot;
    private GameObject _modelInstance;
    private SkinnedMeshRenderer _meshRenderer;
    private HydrangeaDroopRigController _droopController;
    private HydrangeaWiltRigV3Controller _wiltRigV3Controller;
    private MaterialPropertyBlock _materialPropertyBlock;
    private Text _statusText;
    private readonly List<Button> _stageButtons = new List<Button>();
    private readonly List<Button> _droopButtons = new List<Button>();

    private Coroutine _transitionRoutine;
    private float _presenceScale = 0.92f;
    private float _currentBloomMaturity;
    private Color[] _materialBaseColors = new Color[0];
    private float _lastAppliedBloomMaturity = -1f;
    private float _lastAppliedWiltAmount = -1f;
    [SerializeField, Range(0f, 1f)] private float _requestedDroopLevel;
    private float _nextModelLoadAttemptTime;
    private bool _hasLoggedMissingSceneModel;
    private bool _hasLoggedModelDiagnostics;
    private bool _hasUiStateSnapshot;
    private bool _lastUiTracked;
    private bool _lastUiModelReady;
    private int _fallbackBlendShapeIndex = -1;
    private string _currentStageName;
    private string _lastUiStageName;
    private float _lastUiDroopLevel = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnsurePortraitOrientation()
    {
        Screen.orientation = ScreenOrientation.Portrait;
    }

    private void Awake()
    {
        _observer = GetComponent<ObserverBehaviour>();
    }

    private void Start()
    {
        EnsureEventSystem();
        EnsureUi();
        RefreshUiState(IsTracked());
    }

    private void Update()
    {
        var tracked = IsTracked();
        EnsureModelReady(tracked);
        UpdateWiltAppearance();
        RefreshUiState(tracked);
        UpdatePlacementAnimation(tracked);
    }

    public float RequestedDroopLevel => _requestedDroopLevel;
    public bool SupportsDroopRig => _droopController != null || _wiltRigV3Controller != null;
    private bool ShouldShowDroopTestControls =>
        _showDroopTestControls || gameObject.scene.name == "HydrangeaDroopRigTest";

    public void SetDroopLevel(float value01)
    {
        _requestedDroopLevel = Mathf.Clamp01(value01);
        _droopController?.SetDroopTarget(_requestedDroopLevel);
        _wiltRigV3Controller?.SetWiltTarget(_requestedDroopLevel);
    }

    public void SetDroopLevelImmediate(float value01)
    {
        _requestedDroopLevel = Mathf.Clamp01(value01);
        _droopController?.SetDroop(_requestedDroopLevel);
        _wiltRigV3Controller?.SetWilt(_requestedDroopLevel);
        ApplyMaterialAppearance(GetCurrentWiltAmount(), true);
    }

    public void RestoreDroop()
    {
        SetDroopLevel(0f);
    }

    private void EnsureModelInstance()
    {
        if (_stageRoot == null)
        {
            var stageRootObject = new GameObject("HydrangeaStageRoot");
            _stageRoot = stageRootObject.transform;
            _stageRoot.SetParent(transform, false);
        }

        ApplyPlacementTransform();

        if (_modelInstance != null)
        {
            return;
        }

        if (TryBindSceneModel())
        {
            _hasLoggedMissingSceneModel = false;
            return;
        }

        if (!_hasLoggedMissingSceneModel)
        {
            Debug.LogError(
                $"HydrangeaInteractiveExperience: required scene model '{SceneModelName}' " +
                "with a valid SkinnedMeshRenderer was not found under the ImageTarget.");
            _hasLoggedMissingSceneModel = true;
        }
    }

    private void EnsureEventSystem()
    {
        var eventSystem = Object.FindAnyObjectByType<EventSystem>();
        if (eventSystem != null)
        {
            if (eventSystem.GetComponent<BaseInputModule>() == null)
            {
                eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            }

            return;
        }

        var eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<InputSystemUIInputModule>();
    }

    private void EnsureUi()
    {
        if (TryBindExistingUi())
        {
            return;
        }

        var font = LoadUiFont();

        var canvasObject = new GameObject("HydrangeaInteractiveCanvas");
        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var canvasScaler = canvasObject.AddComponent<CanvasScaler>();
        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = new Vector2(1080f, 1920f);
        canvasObject.AddComponent<GraphicRaycaster>();

        var panel = CreateUiObject("Panel", canvasObject.transform);
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0f);
        panelRect.anchorMax = new Vector2(0.5f, 0f);
        panelRect.pivot = new Vector2(0.5f, 0f);
        panelRect.anchoredPosition = new Vector2(0f, 48f);
        panelRect.sizeDelta = new Vector2(620f, 156f);

        var panelImage = panel.AddComponent<UIImage>();
        panelImage.color = new Color(0.07f, 0.12f, 0.1f, 0.82f);

        var panelLayout = panel.AddComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(20, 20, 18, 18);
        panelLayout.spacing = 14f;
        panelLayout.childControlWidth = true;
        panelLayout.childControlHeight = false;
        panelLayout.childForceExpandHeight = false;

        var statusObject = CreateUiObject("Status", panel.transform);
        _statusText = statusObject.AddComponent<Text>();
        _statusText.font = font;
        _statusText.fontSize = 24;
        _statusText.alignment = TextAnchor.MiddleCenter;
        _statusText.color = new Color(0.96f, 0.98f, 0.95f, 1f);
        var statusLayout = statusObject.AddComponent<LayoutElement>();
        statusLayout.preferredHeight = 42f;

        var row = CreateUiObject("Buttons", panel.transform);
        var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 8f;
        rowLayout.childAlignment = TextAnchor.MiddleCenter;
        rowLayout.childControlWidth = false;
        rowLayout.childControlHeight = false;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;
        var rowElement = row.AddComponent<LayoutElement>();
        rowElement.preferredHeight = 56f;

        _stageButtons.Clear();
        _stageButtons.Add(CreateStageButton(row.transform, font, "\u5e7c\u82d7", "Sprout"));
        _stageButtons.Add(CreateStageButton(row.transform, font, "\u5c55\u53f6", "Leafing"));
        _stageButtons.Add(CreateStageButton(row.transform, font, "\u82b1\u82de", "Bud"));
        _stageButtons.Add(CreateStageButton(row.transform, font, "\u534a\u5f00", "HalfBloom"));
        _stageButtons.Add(CreateStageButton(row.transform, font, "\u76db\u5f00", "Bloom"));

        if (ShouldShowDroopTestControls)
        {
            CreateDroopControls(canvasObject.transform, font);
        }

        LogUiDiagnostics("created");
    }

    private void ResolveBlendShapes()
    {
        _blendShapeIndices.Clear();
        _fallbackBlendShapeIndex = -1;
        if (_meshRenderer == null || _meshRenderer.sharedMesh == null)
        {
            return;
        }

        if (_meshRenderer.sharedMesh.blendShapeCount > 0)
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
    }

    private void RefreshUiState(bool tracked)
    {
        var modelReady = _meshRenderer != null && CanPresentAnyStage();
        if (_hasUiStateSnapshot
            && tracked == _lastUiTracked
            && modelReady == _lastUiModelReady
            && _currentStageName == _lastUiStageName
            && Mathf.Approximately(_requestedDroopLevel, _lastUiDroopLevel))
        {
            return;
        }

        _hasUiStateSnapshot = true;
        _lastUiTracked = tracked;
        _lastUiModelReady = modelReady;
        _lastUiStageName = _currentStageName;
        _lastUiDroopLevel = _requestedDroopLevel;

        if (_statusText != null)
        {
            if (!tracked)
            {
                _statusText.text = "\u8bf7\u5c06\u76f8\u673a\u5bf9\u51c6\u8bc6\u522b\u56fe\uff0c\u7b49\u5f85\u7ee3\u7403\u82b1\u52a0\u8f7d";
            }
            else if (!modelReady)
            {
                _statusText.text = "\u5df2\u8bc6\u522b\u56fe\u50cf\uff0c\u6b63\u5728\u51c6\u5907\u4ea4\u4e92\u7ee3\u7403\u82b1\u6a21\u578b";
            }
            else
            {
                var stageLabel = GetStageLabel(_currentStageName);
                _statusText.text = string.IsNullOrEmpty(stageLabel)
                    ? "\u5df2\u8bc6\u522b\u56fe\u50cf\uff0c\u53ef\u70b9\u51fb\u5207\u6362\u7ee3\u7403\u82b1\u9636\u6bb5"
                    : $"\u9636\u6bb5\uff1a{stageLabel}  \u72b6\u6001\uff1a{GetDroopLabel(_requestedDroopLevel)}";
            }
        }

        foreach (var button in _stageButtons)
        {
            if (button != null)
            {
                button.interactable = tracked && _buttonStages.TryGetValue(button, out var stageName) && CanPresentStage(stageName);
            }
        }

        foreach (var button in _droopButtons)
        {
            if (button == null || !_buttonDroopLevels.TryGetValue(button, out var droopLevel))
            {
                continue;
            }

            button.interactable = tracked && SupportsDroopRig;
            if (button.targetGraphic is UIImage image)
            {
                image.color = Mathf.Approximately(droopLevel, _requestedDroopLevel)
                    ? new Color(0.77f, 0.52f, 0.2f, 0.98f)
                    : new Color(0.18f, 0.32f, 0.25f, 0.95f);
            }
        }
    }

    private void UpdatePlacementAnimation(bool tracked)
    {
        if (_stageRoot == null)
        {
            return;
        }

        var targetScale = tracked ? 1f : 0.92f;
        var smoothing = 1f - Mathf.Exp(-8f * Time.deltaTime);
        _presenceScale = Mathf.Lerp(_presenceScale, targetScale, smoothing);
        ApplyPlacementTransform();
    }

    private bool IsTracked()
    {
        if (_observer == null)
        {
            return true;
        }

        var status = _observer.TargetStatus.Status;
        return status == Status.TRACKED || status == Status.EXTENDED_TRACKED;
    }

    private Button CreateStageButton(Transform parent, Font font, string label, string blendShapeName)
    {
        var buttonObject = CreateUiObject(label + "Button", parent);
        var buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.sizeDelta = new Vector2(108f, 52f);

        var image = buttonObject.AddComponent<UIImage>();
        image.color = new Color(0.23f, 0.45f, 0.35f, 0.95f);

        var button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => SetStage(blendShapeName));
        _buttonStages[button] = blendShapeName;

        var labelObject = CreateUiObject("Label", buttonObject.transform);
        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        var text = labelObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = 18;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.text = label;

        return button;
    }

    private void CreateDroopControls(Transform canvas, Font font)
    {
        if (canvas.Find("DroopTestPanel") != null)
        {
            return;
        }

        var testPanel = CreateUiObject("DroopTestPanel", canvas);
        var testPanelRect = testPanel.GetComponent<RectTransform>();
        testPanelRect.anchorMin = new Vector2(0.5f, 0f);
        testPanelRect.anchorMax = new Vector2(0.5f, 0f);
        testPanelRect.pivot = new Vector2(0.5f, 0f);
        testPanelRect.anchoredPosition = new Vector2(0f, 220f);
        testPanelRect.sizeDelta = new Vector2(620f, 64f);

        var panelImage = testPanel.AddComponent<UIImage>();
        panelImage.color = new Color(0.07f, 0.12f, 0.1f, 0.82f);

        var layout = testPanel.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 10, 10);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        _droopButtons.Add(CreateDroopButton(testPanel.transform, font, "DroopHealthyButton", "\u5065\u5eb7 0", 0f));
        _droopButtons.Add(CreateDroopButton(testPanel.transform, font, "DroopLightButton", "\u8f7b\u5fae 0.3", 0.3f));
        _droopButtons.Add(CreateDroopButton(testPanel.transform, font, "DroopMediumButton", "\u660e\u663e 0.6", 0.6f));
        _droopButtons.Add(CreateDroopButton(testPanel.transform, font, "DroopVisibleButton", "\u660e\u663e 1.0", 1f));
    }

    private Button CreateDroopButton(Transform parent, Font font, string objectName, string label, float droopLevel)
    {
        var buttonObject = CreateUiObject(objectName, parent);
        var buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.sizeDelta = new Vector2(135f, 42f);

        var image = buttonObject.AddComponent<UIImage>();
        image.color = new Color(0.18f, 0.32f, 0.25f, 0.95f);

        var button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => SetDroopLevel(droopLevel));
        _buttonDroopLevels[button] = droopLevel;

        var labelObject = CreateUiObject("Label", buttonObject.transform);
        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        var text = labelObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = 16;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.text = label;

        return button;
    }

    private void EnsureModelReady(bool tracked)
    {
        if ((_meshRenderer != null && CanPresentAnyStage())
            || !tracked
            || Time.unscaledTime < _nextModelLoadAttemptTime)
        {
            return;
        }

        _nextModelLoadAttemptTime = Time.unscaledTime + ModelLoadRetryDelay;
        EnsureModelInstance();
        ResolveBlendShapes();

        if (_meshRenderer == null || !CanPresentAnyStage())
        {
            if (_modelInstance != null)
            {
                Destroy(_modelInstance);
            }

            _modelInstance = null;
            _meshRenderer = null;
            _droopController = null;
            _wiltRigV3Controller = null;
            ResetMaterialAppearanceCache();
            return;
        }

        ApplyInitialVisibleStage();
    }

    private void SetStage(string blendShapeName)
    {
        if (_meshRenderer == null || !CanPresentStage(blendShapeName))
        {
            return;
        }

        if (_transitionRoutine != null)
        {
            StopCoroutine(_transitionRoutine);
        }

        var fromStageName = string.IsNullOrEmpty(_currentStageName) ? blendShapeName : _currentStageName;
        _currentStageName = blendShapeName;
        _transitionRoutine = StartCoroutine(AnimateStageChange(fromStageName, blendShapeName));
    }

    private IEnumerator AnimateStageChange(string fromStageName, string blendShapeName)
    {
        var targetWeights = BuildTargetWeights(blendShapeName);
        if (targetWeights == null)
        {
            yield break;
        }

        var rendererIndices = CollectRendererBlendShapeIndices();
        var fromWeights = new Dictionary<int, float>();
        foreach (var index in rendererIndices)
        {
            fromWeights[index] = _meshRenderer.GetBlendShapeWeight(index);
        }

        const float duration = 0.35f;
        var fromBloomMaturity = _currentBloomMaturity;
        var targetBloomMaturity = GetBloomMaturity(blendShapeName);
        var fromDroopStageScale = _droopController != null
            ? new Vector2(_droopController.LeafStageScale, _droopController.HeadStageScale)
            : Vector2.one;
        var targetDroopStageScale = GetDroopStageScale(blendShapeName);
        var elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            foreach (var index in rendererIndices)
            {
                targetWeights.TryGetValue(index, out var targetWeight);
                var weight = Mathf.Lerp(fromWeights[index], targetWeight, t);
                _meshRenderer.SetBlendShapeWeight(index, weight);
            }

            SetBloomMaturity(Mathf.Lerp(fromBloomMaturity, targetBloomMaturity, t));
            SetDroopStageScale(Vector2.Lerp(fromDroopStageScale, targetDroopStageScale, t));
            _wiltRigV3Controller?.SetGrowthTransition(fromStageName, blendShapeName, t);

            yield return null;
        }

        ApplyStageImmediately(blendShapeName);
        _transitionRoutine = null;
    }

    private void ApplyStageImmediately(string blendShapeName)
    {
        if (_meshRenderer == null)
        {
            return;
        }

        var targetWeights = BuildTargetWeights(blendShapeName);
        if (targetWeights == null)
        {
            return;
        }

        foreach (var index in CollectRendererBlendShapeIndices())
        {
            targetWeights.TryGetValue(index, out var targetWeight);
            _meshRenderer.SetBlendShapeWeight(index, targetWeight);
        }

        SetBloomMaturity(GetBloomMaturity(blendShapeName));
        SetDroopStageScale(GetDroopStageScale(blendShapeName));
        _wiltRigV3Controller?.SetGrowthStage(blendShapeName);
    }

    private void SetDroopStageScale(Vector2 stageScale)
    {
        _droopController?.SetStageScale(stageScale.x, stageScale.y);
    }

    private static Vector2 GetDroopStageScale(string stageName)
    {
        switch (stageName)
        {
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
        ApplyMaterialAppearance(GetCurrentWiltAmount());
    }

    private void UpdateWiltAppearance()
    {
        ApplyMaterialAppearance(GetCurrentWiltAmount());
    }

    private float GetCurrentWiltAmount()
    {
        if (_wiltRigV3Controller != null)
        {
            return _wiltRigV3Controller.CurrentWilt;
        }

        if (_droopController != null)
        {
            return _droopController.CurrentDroop;
        }

        return _requestedDroopLevel;
    }

    private void CacheMaterialBaseColors()
    {
        var materials = _meshRenderer != null ? _meshRenderer.sharedMaterials : new Material[0];
        _materialBaseColors = new Color[materials.Length];
        for (var i = 0; i < materials.Length; i++)
        {
            var material = materials[i];
            _materialBaseColors[i] = material != null && material.HasProperty(BaseColorProperty)
                ? material.GetColor(BaseColorProperty)
                : Color.white;
        }

        _lastAppliedBloomMaturity = -1f;
        _lastAppliedWiltAmount = -1f;
    }

    private void ApplyMaterialAppearance(float wiltAmount, bool force = false)
    {
        if (_meshRenderer == null)
        {
            return;
        }

        var materials = _meshRenderer.sharedMaterials;
        if (_materialBaseColors.Length != materials.Length)
        {
            CacheMaterialBaseColors();
        }

        var clampedWilt = Mathf.Clamp01(wiltAmount);
        if (!force
            && Mathf.Abs(_lastAppliedWiltAmount - clampedWilt) < 0.0001f
            && Mathf.Abs(_lastAppliedBloomMaturity - _currentBloomMaturity) < 0.0001f)
        {
            return;
        }

        var smoothWilt = Mathf.SmoothStep(0f, 1f, clampedWilt);
        var tintAmount = smoothWilt * _wiltTintStrength;
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
            _meshRenderer.GetPropertyBlock(_materialPropertyBlock, i);
            _materialPropertyBlock.SetFloat(BloomMaturityProperty, _currentBloomMaturity);
            _materialPropertyBlock.SetFloat(WiltAmountProperty, smoothWilt);
            _materialPropertyBlock.SetColor(WiltColorProperty, _wiltTint);

            if (material.HasProperty(BaseColorProperty))
            {
                var baseColor = _materialBaseColors[i];
                if (!material.HasProperty(WiltAmountProperty))
                {
                    baseColor = new Color(
                        baseColor.r * standardMaterialMultiplier.r,
                        baseColor.g * standardMaterialMultiplier.g,
                        baseColor.b * standardMaterialMultiplier.b,
                        baseColor.a);
                }

                _materialPropertyBlock.SetColor(BaseColorProperty, baseColor);
            }

            _meshRenderer.SetPropertyBlock(_materialPropertyBlock, i);
        }

        _lastAppliedBloomMaturity = _currentBloomMaturity;
        _lastAppliedWiltAmount = clampedWilt;
    }

    private void ResetMaterialAppearanceCache()
    {
        _materialBaseColors = new Color[0];
        _lastAppliedBloomMaturity = -1f;
        _lastAppliedWiltAmount = -1f;
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

    private static Font LoadUiFont()
    {
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
        {
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        return font;
    }

    private static GameObject CreateUiObject(string name, Transform parent)
    {
        var gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        return gameObject;
    }

    private bool TryBindSceneModel()
    {
        var sceneModel = transform.Find(SceneModelName);
        if (sceneModel == null)
        {
            return false;
        }

        var templateRenderers = sceneModel.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (templateRenderers.Length != 1 || templateRenderers[0].sharedMesh == null)
        {
            Debug.LogError(
                $"HydrangeaInteractiveExperience: expected exactly one valid SkinnedMeshRenderer under " +
                $"'{SceneModelName}', found {templateRenderers.Length}.");
            return false;
        }

        var runtimeModel = Instantiate(sceneModel.gameObject, _stageRoot, false);
        runtimeModel.name = "Hydrangea_AR_Interactive_Runtime";
        runtimeModel.transform.localPosition = Vector3.zero;
        runtimeModel.transform.localRotation = Quaternion.identity;
        runtimeModel.transform.localScale = Vector3.one;

        var runtimeAnimators = runtimeModel.GetComponentsInChildren<Animator>(true);
        foreach (var animator in runtimeAnimators)
        {
            animator.enabled = false;
        }

        runtimeModel.SetActive(true);

        var runtimeRenderers = runtimeModel.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (runtimeRenderers.Length != 1 || runtimeRenderers[0].sharedMesh == null)
        {
            Debug.LogError(
                $"HydrangeaInteractiveExperience: cloned model should contain exactly one valid " +
                $"SkinnedMeshRenderer, found {runtimeRenderers.Length}.");
            Destroy(runtimeModel);
            return false;
        }

        _modelInstance = runtimeModel;
        _meshRenderer = runtimeRenderers[0];
        if (HydrangeaWiltRigV3Controller.ContainsWiltRig(runtimeModel.transform))
        {
            if (_wiltRigV3PivotData == null)
            {
                Debug.LogError(
                    "HydrangeaInteractiveExperience: WiltRig v3 was detected, but its Pivot JSON is not assigned.",
                    this);
                Destroy(runtimeModel);
                return false;
            }

            _wiltRigV3Controller = runtimeModel.GetComponent<HydrangeaWiltRigV3Controller>();
            if (_wiltRigV3Controller == null)
            {
                _wiltRigV3Controller = runtimeModel.AddComponent<HydrangeaWiltRigV3Controller>();
            }

            if (!_wiltRigV3Controller.Initialize(_meshRenderer, _wiltRigV3PivotData))
            {
                _wiltRigV3Controller = null;
                _modelInstance = null;
                _meshRenderer = null;
                Destroy(runtimeModel);
                return false;
            }

            _wiltRigV3Controller.SetWilt(_requestedDroopLevel);
        }
        else if (HydrangeaDroopRigController.ContainsDroopRig(runtimeModel.transform))
        {
            _droopController = runtimeModel.GetComponent<HydrangeaDroopRigController>();
            if (_droopController == null)
            {
                _droopController = runtimeModel.AddComponent<HydrangeaDroopRigController>();
            }

            _droopController.Initialize();
            _droopController.SetDroop(_requestedDroopLevel);
        }

        ConfigureMeshRenderer();
        sceneModel.gameObject.SetActive(false);
        LogModelDiagnostics("Cloned complete scene model hierarchy");
        return true;
    }

    private bool TryBindExistingUi()
    {
        var existingCanvas = GameObject.Find("HydrangeaInteractiveCanvas");
        if (existingCanvas == null)
        {
            return false;
        }

        var panel = existingCanvas.transform.Find("Panel");
        if (panel == null)
        {
            return false;
        }

        _statusText = panel.Find("Status")?.GetComponent<Text>();
        var oldNestedDroopControls = panel.Find("DroopButtons");
        if (oldNestedDroopControls != null)
        {
            oldNestedDroopControls.gameObject.SetActive(false);
        }

        var existingDroopControls = existingCanvas.transform.Find("DroopTestPanel");
        if (ShouldShowDroopTestControls)
        {
            if (existingDroopControls != null)
            {
                existingDroopControls.gameObject.SetActive(true);
            }

            CreateDroopControls(existingCanvas.transform, LoadUiFont());
        }
        else if (existingDroopControls != null)
        {
            existingDroopControls.gameObject.SetActive(false);
        }

        _stageButtons.Clear();
        _droopButtons.Clear();
        _buttonStages.Clear();
        _buttonDroopLevels.Clear();
        foreach (var button in existingCanvas.GetComponentsInChildren<Button>(true))
        {
            var stageName = ResolveButtonStageName(button);
            if (!string.IsNullOrEmpty(stageName))
            {
                _stageButtons.Add(button);
                _buttonStages[button] = stageName;
                // Runtime-created UI can survive editor play-mode reload settings.
                // Rebind it to this component instance instead of retaining stale delegates.
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => SetStage(stageName));
                continue;
            }

            if (TryResolveDroopLevel(button, out var droopLevel))
            {
                _droopButtons.Add(button);
                _buttonDroopLevels[button] = droopLevel;
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => SetDroopLevel(droopLevel));
            }
        }

        LogUiDiagnostics("rebound");

        return true;
    }

    private void LogUiDiagnostics(string source)
    {
        Debug.Log(
            $"HydrangeaInteractiveExperience UI {source}: scene={gameObject.scene.name}, " +
            $"showDroopControls={ShouldShowDroopTestControls}, " +
            $"stageButtons={_stageButtons.Count}, droopButtons={_droopButtons.Count}.",
            this);
    }

    private void ApplyPlacementTransform()
    {
        if (_stageRoot == null)
        {
            return;
        }

        _stageRoot.localPosition = _modelLocalPosition;

        var baseRotation = _applyBlenderAxisFix
            ? Quaternion.Euler(BlenderAxisFixEuler)
            : Quaternion.identity;
        _stageRoot.localRotation = baseRotation * Quaternion.Euler(_additionalEulerOffset);

        var safeScale = Mathf.Max(0.0001f, _modelBaseScale);
        _stageRoot.localScale = Vector3.one * safeScale * _presenceScale;
    }

    private void ConfigureMeshRenderer()
    {
        if (_meshRenderer == null)
        {
            return;
        }

        _meshRenderer.enabled = true;
        _meshRenderer.updateWhenOffscreen = true;
        ApplyHydrangeaMaterialOverrides();
        _meshRenderer.SetPropertyBlock(null);
        CacheMaterialBaseColors();
        ApplyMaterialAppearance(GetCurrentWiltAmount(), true);

        if (_meshRenderer.sharedMesh == null)
        {
            return;
        }

        var meshBounds = _meshRenderer.sharedMesh.bounds;
        if (meshBounds.extents == Vector3.zero)
        {
            meshBounds = new Bounds(Vector3.zero, Vector3.one * 100f);
        }

        _meshRenderer.localBounds = meshBounds;
    }

    private void ApplyHydrangeaMaterialOverrides()
    {
        var materials = _meshRenderer.sharedMaterials;
        if (materials.Length == 0)
        {
            return;
        }

        var petalMaterial = Resources.Load<Material>(PetalMaterialResourcePath);
        if (petalMaterial == null)
        {
            Debug.LogError("HydrangeaInteractiveExperience: petal material resource is missing.");
            return;
        }

        var centerMaterial = Resources.Load<Material>(CenterMaterialResourcePath);
        if (centerMaterial == null)
        {
            Debug.LogError("HydrangeaInteractiveExperience: flower center material resource is missing.");
            return;
        }

        var mesh = _meshRenderer.sharedMesh;
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
        // Preserve their known order: petals first, centers second.
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
            Debug.LogWarning("HydrangeaInteractiveExperience: no petal material slot was found; material override was skipped.");
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

        _meshRenderer.sharedMaterials = materials;
        Debug.Log(
            $"HydrangeaInteractiveExperience: applied petal material to slot(s) {string.Join(", ", petalSlots)} " +
            $"and center material to slot(s) {string.Join(", ", centerSlots)}.");
    }

    private void ApplyInitialVisibleStage()
    {
        if (_meshRenderer == null || _meshRenderer.sharedMesh == null)
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

        for (var i = 0; i < _meshRenderer.sharedMesh.blendShapeCount; i++)
        {
            _meshRenderer.SetBlendShapeWeight(i, i == _fallbackBlendShapeIndex ? 100f : 0f);
        }

        SetBloomMaturity(GetBloomMaturity("Bud"));
        SetDroopStageScale(GetDroopStageScale("Bud"));

        Debug.Log($"HydrangeaInteractiveExperience: Bud blend shape was not found, using fallback blend shape index {_fallbackBlendShapeIndex} ({_meshRenderer.sharedMesh.GetBlendShapeName(_fallbackBlendShapeIndex)}).");
    }

    private static string GetStageLabel(string stageName)
    {
        switch (stageName)
        {
            case "Sprout":
                return "\u5e7c\u82d7";
            case "Leafing":
                return "\u5c55\u53f6";
            case "Bud":
                return "\u82b1\u82de";
            case "HalfBloom":
                return "\u534a\u5f00";
            case "Bloom":
                return "\u76db\u5f00";
            default:
                return string.Empty;
        }
    }

    private static string GetDroopLabel(float droopLevel)
    {
        if (droopLevel <= 0.01f)
        {
            return "\u5065\u5eb7";
        }

        return droopLevel <= 0.3f
            ? "\u8f7b\u5fae\u4e0b\u5782"
            : "\u660e\u663e\u4e0b\u5782";
    }

    private int ResolveBlendShapeIndex(string canonicalStageName)
    {
        if (_meshRenderer == null || _meshRenderer.sharedMesh == null)
        {
            return -1;
        }

        var directIndex = _meshRenderer.sharedMesh.GetBlendShapeIndex(canonicalStageName);
        if (directIndex >= 0)
        {
            return directIndex;
        }

        var stageToken = canonicalStageName.ToLowerInvariant();
        for (var i = 0; i < _meshRenderer.sharedMesh.blendShapeCount; i++)
        {
            var actualName = _meshRenderer.sharedMesh.GetBlendShapeName(i).ToLowerInvariant();
            if (actualName.Contains(stageToken))
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

    private bool CanPresentStage(string stageName)
    {
        if (_meshRenderer == null || _meshRenderer.sharedMesh == null)
        {
            return false;
        }

        if (_blendShapeIndices.ContainsKey(stageName))
        {
            return true;
        }

        return stageName == "HalfBloom"
            && _blendShapeIndices.ContainsKey("Bud")
            && _blendShapeIndices.ContainsKey("Bloom");
    }

    private Dictionary<int, float> BuildTargetWeights(string stageName)
    {
        if (_meshRenderer == null || _meshRenderer.sharedMesh == null)
        {
            return null;
        }

        var targetWeights = new Dictionary<int, float>();

        if (_blendShapeIndices.TryGetValue(stageName, out var directIndex))
        {
            targetWeights[directIndex] = 100f;
            return targetWeights;
        }

        if (stageName == "HalfBloom"
            && _blendShapeIndices.TryGetValue("Bud", out var budIndex)
            && _blendShapeIndices.TryGetValue("Bloom", out var bloomIndex))
        {
            // This asset has no dedicated HalfBloom shape, so we approximate it.
            targetWeights[budIndex] = 40f;
            targetWeights[bloomIndex] = 60f;
            return targetWeights;
        }

        return null;
    }

    private List<int> CollectRendererBlendShapeIndices()
    {
        return _blendShapeIndices.Values
            .Distinct()
            .OrderBy(index => index)
            .ToList();
    }

    private string ResolveButtonStageName(Button button)
    {
        var label = button.GetComponentInChildren<Text>(true)?.text ?? button.name;
        if (label.Contains("\u5e7c\u82d7") || label.Contains("Sprout"))
        {
            return "Sprout";
        }

        if (label.Contains("\u5c55\u53f6") || label.Contains("Leafing"))
        {
            return "Leafing";
        }

        if (label.Contains("\u82b1\u82de") || label.Contains("Bud"))
        {
            return "Bud";
        }

        if (label.Contains("\u534a\u5f00") || label.Contains("HalfBloom"))
        {
            return "HalfBloom";
        }

        return label.Contains("\u76db\u5f00") || label.Contains("Bloom")
            ? "Bloom"
            : string.Empty;
    }

    private static bool TryResolveDroopLevel(Button button, out float droopLevel)
    {
        switch (button.name)
        {
            case "DroopHealthyButton":
                droopLevel = 0f;
                return true;
            case "DroopLightButton":
                droopLevel = 0.3f;
                return true;
            case "DroopVisibleButton":
                droopLevel = 1f;
                return true;
            default:
                droopLevel = 0f;
                return false;
        }
    }

    private void LogModelDiagnostics(string source)
    {
        if (_hasLoggedModelDiagnostics || _meshRenderer == null)
        {
            return;
        }

        _hasLoggedModelDiagnostics = true;
        var mesh = _meshRenderer.sharedMesh;
        var meshName = mesh != null ? mesh.name : "<null>";
        var blendShapeCount = mesh != null ? mesh.blendShapeCount : -1;
        var subMeshCount = mesh != null ? mesh.subMeshCount : -1;
        var meshBounds = mesh != null ? mesh.bounds : default;
        var localBounds = _meshRenderer.localBounds;
        var rootBoneName = _meshRenderer.rootBone != null ? _meshRenderer.rootBone.name : "<null>";
        var rendererBoneCount = _meshRenderer.bones != null ? _meshRenderer.bones.Length : 0;
        var materialNames = new List<string>();
        foreach (var material in _meshRenderer.sharedMaterials)
        {
            materialNames.Add(material != null ? material.name : "<null>");
        }

        var blendShapeNames = new List<string>();
        if (mesh != null)
        {
            for (var i = 0; i < mesh.blendShapeCount; i++)
            {
                blendShapeNames.Add(mesh.GetBlendShapeName(i));
            }
        }

        Debug.Log(
            "HydrangeaInteractiveExperience diagnostics\n" +
            $"source: {source}\n" +
            $"rendererEnabled: {_meshRenderer.enabled}\n" +
            $"updateWhenOffscreen: {_meshRenderer.updateWhenOffscreen}\n" +
            $"mesh: {meshName}\n" +
            $"subMeshes: {subMeshCount}\n" +
            $"blendShapes: {blendShapeCount}\n" +
            $"rootBone: {rootBoneName}\n" +
            $"rendererBones: {rendererBoneCount}\n" +
            $"animators: {_modelInstance.GetComponentsInChildren<Animator>(true).Length}\n" +
            $"droopLeafBones: {(_droopController != null ? _droopController.LeafBoneCount : 0)}\n" +
            $"droopHeadBones: {(_droopController != null ? _droopController.HeadBoneCount : 0)}\n" +
            $"wiltRigV3Bones: {(_wiltRigV3Controller != null ? _wiltRigV3Controller.BoneCount : 0)}\n" +
            $"meshBoundsCenter: {meshBounds.center} meshBoundsExtents: {meshBounds.extents}\n" +
            $"localBoundsCenter: {localBounds.center} localBoundsExtents: {localBounds.extents}\n" +
            $"stageRootLocalPosition: {(_stageRoot != null ? _stageRoot.localPosition.ToString() : "<null>")}\n" +
            $"stageRootLocalRotation: {(_stageRoot != null ? _stageRoot.localEulerAngles.ToString() : "<null>")}\n" +
            $"stageRootLocalScale: {(_stageRoot != null ? _stageRoot.localScale.ToString() : "<null>")}\n" +
            $"rendererWorldPosition: {_meshRenderer.transform.position}\n" +
            $"rendererWorldScale: {_meshRenderer.transform.lossyScale}\n" +
            $"materials: {string.Join(", ", materialNames)}\n" +
            $"blendShapeNames: {string.Join(", ", blendShapeNames)}");
    }
}
