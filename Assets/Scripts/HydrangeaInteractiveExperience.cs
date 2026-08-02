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
    private const string SceneModelName = "Hydrangea_Growth_BlendShape_Unity";
    private static readonly string[] ManagedBlendShapes = { "Sprout", "Leafing", "Bud", "HalfBloom", "Bloom" };
    private static readonly Vector3 BlenderAxisFixEuler = new Vector3(-90f, 0f, 0f);

    [Header("Placement")]
    [SerializeField] private Vector3 _modelLocalPosition = Vector3.zero;
    [SerializeField] private bool _applyBlenderAxisFix = true;
    [SerializeField] private Vector3 _additionalEulerOffset = Vector3.zero;
    [SerializeField] private float _modelBaseScale = 0.02f;

    private readonly Dictionary<string, int> _blendShapeIndices = new Dictionary<string, int>();
    private readonly Dictionary<Button, string> _buttonStages = new Dictionary<Button, string>();

    private ObserverBehaviour _observer;
    private Transform _stageRoot;
    private GameObject _modelInstance;
    private SkinnedMeshRenderer _meshRenderer;
    private Text _statusText;
    private readonly List<Button> _stageButtons = new List<Button>();

    private Coroutine _transitionRoutine;
    private float _presenceScale = 0.92f;
    private float _nextModelLoadAttemptTime;
    private bool _hasLoggedMissingSceneModel;
    private bool _hasLoggedModelDiagnostics;
    private bool _hasUiStateSnapshot;
    private bool _lastUiTracked;
    private bool _lastUiModelReady;
    private int _fallbackBlendShapeIndex = -1;
    private string _currentStageName;
    private string _lastUiStageName;

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
        RefreshUiState(tracked);
        UpdatePlacementAnimation(tracked);
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
            && _currentStageName == _lastUiStageName)
        {
            return;
        }

        _hasUiStateSnapshot = true;
        _lastUiTracked = tracked;
        _lastUiModelReady = modelReady;
        _lastUiStageName = _currentStageName;

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
                    : $"\u5f53\u524d\u9636\u6bb5\uff1a{stageLabel}\uff0c\u53ef\u70b9\u51fb\u5207\u6362";
            }
        }

        foreach (var button in _stageButtons)
        {
            if (button != null)
            {
                button.interactable = tracked && _buttonStages.TryGetValue(button, out var stageName) && CanPresentStage(stageName);
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

        _currentStageName = blendShapeName;
        _transitionRoutine = StartCoroutine(AnimateStageChange(blendShapeName));
    }

    private IEnumerator AnimateStageChange(string blendShapeName)
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

        var templateSkinnedRenderer = sceneModel.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (templateSkinnedRenderer == null || templateSkinnedRenderer.sharedMesh == null)
        {
            return false;
        }

        _modelInstance = new GameObject("Hydrangea_AR_Interactive_Runtime");
        _modelInstance.transform.SetParent(_stageRoot, false);

        var runtimeRenderer = _modelInstance.AddComponent<SkinnedMeshRenderer>();
        runtimeRenderer.sharedMesh = templateSkinnedRenderer.sharedMesh;
        runtimeRenderer.sharedMaterials = templateSkinnedRenderer.sharedMaterials;
        runtimeRenderer.shadowCastingMode = templateSkinnedRenderer.shadowCastingMode;
        runtimeRenderer.receiveShadows = templateSkinnedRenderer.receiveShadows;
        runtimeRenderer.lightProbeUsage = templateSkinnedRenderer.lightProbeUsage;
        runtimeRenderer.reflectionProbeUsage = templateSkinnedRenderer.reflectionProbeUsage;
        runtimeRenderer.probeAnchor = templateSkinnedRenderer.probeAnchor;
        runtimeRenderer.rootBone = null;
        runtimeRenderer.bones = new Transform[0];

        _meshRenderer = runtimeRenderer;
        ConfigureMeshRenderer();
        sceneModel.gameObject.SetActive(false);
        LogModelDiagnostics("Created from scene SkinnedMeshRenderer template");
        return true;
    }

    private bool TryBindExistingUi()
    {
        var existingCanvas = GameObject.Find("HydrangeaInteractiveCanvas");
        if (existingCanvas == null)
        {
            return false;
        }

        _statusText = existingCanvas.transform.Find("Panel/Status")?.GetComponent<Text>();
        _stageButtons.Clear();
        _buttonStages.Clear();
        foreach (var button in existingCanvas.GetComponentsInChildren<Button>(true))
        {
            var stageName = ResolveButtonStageName(button);
            _stageButtons.Add(button);
            _buttonStages[button] = stageName;
            if (!string.IsNullOrEmpty(stageName))
            {
                // Runtime-created UI can survive editor play-mode reload settings.
                // Rebind it to this component instance instead of retaining stale delegates.
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => SetStage(stageName));
            }
        }

        return true;
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
            Debug.LogError("HydrangeaInteractiveExperience: flower material resource is missing.");
            return;
        }

        var mesh = _meshRenderer.sharedMesh;
        var slotCount = Mathf.Min(materials.Length, mesh != null ? mesh.subMeshCount : materials.Length);
        var flowerSlots = new List<int>();

        for (var i = 0; i < slotCount; i++)
        {
            var materialName = materials[i] != null ? materials[i].name.ToLowerInvariant() : string.Empty;
            if (materialName.Contains("flower") || materialName.Contains("petal") || materialName.Contains("center"))
            {
                flowerSlots.Add(i);
            }
        }

        // The current FBX exposes stems, leaves, and flower materials. Internally,
        // petals and centers may still occupy separate submeshes but share one material.
        if (flowerSlots.Count == 0 && slotCount >= 3)
        {
            for (var i = 2; i < slotCount; i++)
            {
                flowerSlots.Add(i);
            }
        }

        if (flowerSlots.Count == 0)
        {
            Debug.LogWarning("HydrangeaInteractiveExperience: no flower material slot was found; material override was skipped.");
            return;
        }

        foreach (var slot in flowerSlots)
        {
            materials[slot] = petalMaterial;
        }

        _meshRenderer.sharedMaterials = materials;
        Debug.Log($"HydrangeaInteractiveExperience: applied flower material to slot(s) {string.Join(", ", flowerSlots)}.");
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
