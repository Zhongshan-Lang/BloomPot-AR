using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Vuforia;
using UIImage = UnityEngine.UI.Image;

public sealed class HydrangeaInteractiveExperience : MonoBehaviour
{
    private const float ModelLoadRetryDelay = 2f;
    private const float VisitRecognitionDuration = 3f;
    private const float DemoAutosaveInterval = 2f;
    private const string SceneModelName = "Hydrangea_Growth_BlendShape_Unity";
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

    private readonly Dictionary<Button, string> _buttonStages = new Dictionary<Button, string>();
    private readonly Dictionary<Button, float> _buttonDroopLevels = new Dictionary<Button, float>();
    private readonly List<RaycastResult> _uiRaycastResults = new List<RaycastResult>();

    private ObserverBehaviour _observer;
    private Transform _stageRoot;
    private GameObject _modelInstance;
    private SkinnedMeshRenderer _meshRenderer;
    private HydrangeaDroopRigController _droopController;
    private HydrangeaWiltRigV3Controller _wiltRigV3Controller;
    private HydrangeaView _view;
    private PlantSaveService _plantSaveService;
    private PlantExperienceProfile _experienceProfile;
    private PlantState _plantState;
    private PlantGrowthController _plantGrowthController;
    private PlantInteractionController _plantInteractionController;
    private Text _statusText;
    private UIImage _vitalityFillImage;
    private Text _vitalityValueText;
    private RectTransform _vitalityHudRect;
    private Text _experienceModeText;
    private Button _demoResetButton;
    private readonly List<Button> _stageButtons = new List<Button>();
    private readonly List<Button> _droopButtons = new List<Button>();

    private float _presenceScale = 0.92f;
    [SerializeField, Range(0f, 1f)] private float _requestedDroopLevel;
    private float _nextModelLoadAttemptTime;
    private bool _hasLoggedMissingSceneModel;
    private bool _hasLoggedModelDiagnostics;
    private bool _hasUiStateSnapshot;
    private bool _lastUiTracked;
    private bool _lastUiModelReady;
    private string _lastUiStageName;
    private float _lastUiDroopLevel = -1f;
    private float _lastUiVitality = -1f;
    private float _trackedVisitDuration;
    private bool _visitRegisteredForCurrentTrackingSession;
    private float _nextDemoAutosaveTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnsurePortraitOrientation()
    {
        Screen.orientation = ScreenOrientation.Portrait;
    }

    private void Awake()
    {
        _observer = GetComponent<ObserverBehaviour>();
        _view = GetComponent<HydrangeaView>();
        if (_view == null)
        {
            _view = gameObject.AddComponent<HydrangeaView>();
        }
    }

    private void Start()
    {
        InitializePersistentState();
        EnsureEventSystem();
        EnsureUi();
        RefreshUiState(IsTracked());
    }

    private void Update()
    {
        var tracked = IsTracked();
        EnsureModelReady(tracked);
        UpdateRealtimeGrowth();
        UpdateCompanionInteraction(tracked);
        UpdateVitalityHudSafeArea();
        var modelReady = _view != null && _view.IsReady;
        var vitality = _plantState != null ? _plantState.vitality : 1f;
        _view.Tick(
            tracked && modelReady && !ShouldShowDroopTestControls,
            vitality);
        RefreshUiState(tracked);
        UpdatePlacementAnimation(tracked);
    }

    public float RequestedDroopLevel => _requestedDroopLevel;
    public PlantState CurrentPlantState => _plantState;
    public float CompanionWiltLevel =>
        _plantGrowthController != null && _plantState != null
            ? _plantGrowthController.CalculateWilt(_plantState.vitality)
            : 0f;
    public bool SupportsDroopRig => _view != null && _view.SupportsWilt;
    private bool IsRigValidationScene =>
        gameObject.scene.name == "HydrangeaDroopRigTest"
        || gameObject.scene.name == "HydrangeaWiltRigV3Test";
    private bool ShouldShowDroopTestControls =>
        _showDroopTestControls || IsRigValidationScene;

    public void SetDroopLevel(float value01)
    {
        _requestedDroopLevel = Mathf.Clamp01(value01);
        _view?.SetWiltTarget(_requestedDroopLevel);
    }

    public void SetDroopLevelImmediate(float value01)
    {
        _requestedDroopLevel = Mathf.Clamp01(value01);
        _view?.SetWiltImmediate(_requestedDroopLevel);
    }

    public void RestoreDroop()
    {
        SetDroopLevel(0f);
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
        {
            SavePersistentState();
        }
    }

    private void OnApplicationQuit()
    {
        SavePersistentState();
    }

    private void InitializePersistentState()
    {
        try
        {
            _experienceProfile = PlantExperienceProfile.ResolveCurrent();
            _plantSaveService = new PlantSaveService(
                Application.persistentDataPath,
                () => System.DateTime.UtcNow,
                _experienceProfile.SaveFileName,
                _experienceProfile);
            _plantState = _plantSaveService.LoadOrCreate();
            _plantGrowthController = new PlantGrowthController(_experienceProfile);
            _plantInteractionController = new PlantInteractionController(_experienceProfile);
            var now = System.DateTime.UtcNow;
            var progress = _plantGrowthController.ApplyOfflineProgress(_plantState, now);
            _plantGrowthController.ResetRealtime(now);
            _requestedDroopLevel = ShouldShowDroopTestControls ? 0f : progress.WiltAmount;
            if (progress.VitalityChanged)
            {
                SavePersistentState();
            }
        }
        catch (System.Exception exception)
        {
            Debug.LogError($"HydrangeaInteractiveExperience: plant state initialization failed: {exception.Message}", this);
            _experienceProfile = _experienceProfile ?? PlantExperienceProfile.Companion;
            _plantState = PlantState.CreateDefault(System.DateTime.UtcNow, _experienceProfile);
            _plantGrowthController = new PlantGrowthController(_experienceProfile);
            _plantInteractionController = new PlantInteractionController(_experienceProfile);
            _requestedDroopLevel = 0f;
        }
    }

    private void UpdateRealtimeGrowth()
    {
        if (_experienceProfile == null
            || !_experienceProfile.UsesRealtimeDecay
            || _plantGrowthController == null
            || _plantState == null
            || ShouldShowDroopTestControls)
        {
            return;
        }

        var progress = _plantGrowthController.ApplyRealtimeProgress(
            _plantState,
            System.DateTime.UtcNow);
        if (!progress.VitalityChanged)
        {
            return;
        }

        ApplyCompanionStateToView();
        if (Time.unscaledTime >= _nextDemoAutosaveTime)
        {
            SavePersistentState();
            _nextDemoAutosaveTime = Time.unscaledTime + DemoAutosaveInterval;
        }
    }

    private void SavePersistentState()
    {
        if (_plantSaveService == null || _plantState == null)
        {
            return;
        }

        if (!_plantSaveService.TrySave(_plantState, out var error))
        {
            Debug.LogError($"HydrangeaInteractiveExperience: plant state save failed: {error}", this);
        }
    }

    private void ResetDemoState()
    {
        if (_experienceProfile == null || !_experienceProfile.IsPortfolioDemo)
        {
            return;
        }

        var now = System.DateTime.UtcNow;
        _plantState = PlantState.CreateDefault(now, _experienceProfile);
        _plantGrowthController.ResetRealtime(now);
        _trackedVisitDuration = 0f;
        _visitRegisteredForCurrentTrackingSession = false;
        ApplyCompanionStateToView();
        SavePersistentState();
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

        EnsureVitalityUi(canvasObject.transform, font);

        if (ShouldShowDroopTestControls)
        {
            CreateDroopControls(canvasObject.transform, font);
        }

        LogUiDiagnostics("created");
    }

    private void RefreshUiState(bool tracked)
    {
        var modelReady = _view != null && _view.IsReady;
        var currentStageName = _view != null ? _view.CurrentStageName : null;
        var vitality = _plantState != null ? Mathf.Clamp01(_plantState.vitality) : 1f;
        if (_hasUiStateSnapshot
            && tracked == _lastUiTracked
            && modelReady == _lastUiModelReady
            && currentStageName == _lastUiStageName
            && Mathf.Approximately(_requestedDroopLevel, _lastUiDroopLevel)
            && Mathf.Approximately(vitality, _lastUiVitality))
        {
            return;
        }

        _hasUiStateSnapshot = true;
        _lastUiTracked = tracked;
        _lastUiModelReady = modelReady;
        _lastUiStageName = currentStageName;
        _lastUiDroopLevel = _requestedDroopLevel;
        _lastUiVitality = vitality;

        UpdateVitalityUi(vitality);

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
                var stageLabel = GetStageLabel(currentStageName);
                _statusText.text = string.IsNullOrEmpty(stageLabel)
                    ? "\u5df2\u8bc6\u522b\u56fe\u50cf\uff0c\u53ef\u70b9\u51fb\u5207\u6362\u7ee3\u7403\u82b1\u9636\u6bb5"
                    : $"\u9636\u6bb5\uff1a{stageLabel}  \u72b6\u6001\uff1a{GetDroopLabel(_requestedDroopLevel)}";
            }
        }

        foreach (var button in _stageButtons)
        {
            if (button != null)
            {
                button.interactable = tracked
                    && _buttonStages.TryGetValue(button, out var stageName)
                    && _view != null
                    && _view.CanPresentStage(stageName);
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

    private void UpdateCompanionInteraction(bool tracked)
    {
        if (ShouldShowDroopTestControls || _plantState == null || _plantInteractionController == null)
        {
            return;
        }

        var modelReady = _view != null && _view.IsReady;
        if (!tracked || !modelReady)
        {
            _trackedVisitDuration = 0f;
            _visitRegisteredForCurrentTrackingSession = false;
            return;
        }

        if (!_visitRegisteredForCurrentTrackingSession)
        {
            _trackedVisitDuration += Time.unscaledDeltaTime;
            if (_trackedVisitDuration >= VisitRecognitionDuration)
            {
                RegisterVisit();
                _visitRegisteredForCurrentTrackingSession = true;
            }
        }

        if (TryGetCompanionTapPosition(out var screenPosition)
            && !IsPointerOverUi(screenPosition)
            && IsPointerOverPlant(screenPosition))
        {
            RegisterCompanionTap();
        }
    }

    private void RegisterVisit()
    {
        var result = _plantInteractionController.RegisterVisit(_plantState, System.DateTime.UtcNow);
        if (result.StateChanged)
        {
            ApplyCompanionStateToView();
            SavePersistentState();
        }
    }

    private void RegisterCompanionTap()
    {
        var result = _plantInteractionController.RegisterTap(_plantState, System.DateTime.UtcNow);
        _view?.PlayInteractionResponse(result.Rewarded ? 1f : 0.55f);
        if (result.StateChanged)
        {
            ApplyCompanionStateToView();
            SavePersistentState();
        }
    }

    private void ApplyCompanionStateToView()
    {
        if (_plantGrowthController == null || _plantState == null)
        {
            return;
        }

        _requestedDroopLevel = _plantGrowthController.CalculateWilt(_plantState.vitality);
        _view?.SetWiltTarget(_requestedDroopLevel);
        _hasUiStateSnapshot = false;
    }

    private static bool TryGetCompanionTapPosition(out Vector2 screenPosition)
    {
        if (Touchscreen.current?.primaryTouch.press.wasPressedThisFrame == true)
        {
            screenPosition = Touchscreen.current.primaryTouch.position.ReadValue();
            return true;
        }

        if (Mouse.current?.leftButton.wasPressedThisFrame == true)
        {
            screenPosition = Mouse.current.position.ReadValue();
            return true;
        }

        screenPosition = default;
        return false;
    }

    private bool IsPointerOverUi(Vector2 screenPosition)
    {
        if (EventSystem.current == null)
        {
            return false;
        }

        var pointerData = new PointerEventData(EventSystem.current) { position = screenPosition };
        _uiRaycastResults.Clear();
        EventSystem.current.RaycastAll(pointerData, _uiRaycastResults);
        return _uiRaycastResults.Count > 0;
    }

    private bool IsPointerOverPlant(Vector2 screenPosition)
    {
        if (_meshRenderer == null || !_meshRenderer.enabled)
        {
            return false;
        }

        var camera = Camera.main;
        if (camera == null)
        {
            return false;
        }

        return _meshRenderer.bounds.IntersectRay(camera.ScreenPointToRay(screenPosition));
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
        if ((_view != null && _view.IsReady)
            || !tracked
            || Time.unscaledTime < _nextModelLoadAttemptTime)
        {
            return;
        }

        _nextModelLoadAttemptTime = Time.unscaledTime + ModelLoadRetryDelay;
        EnsureModelInstance();

        if (_view == null || !_view.IsReady)
        {
            if (_modelInstance != null)
            {
                Destroy(_modelInstance);
            }

            _modelInstance = null;
            _meshRenderer = null;
            _droopController = null;
            _wiltRigV3Controller = null;
            _view?.Unbind();
            return;
        }
    }

    private void SetStage(string blendShapeName)
    {
        _view?.SetStage(blendShapeName);
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

    private void EnsureVitalityUi(Transform canvas, Font font)
    {
        var oldNestedVitality = canvas.Find("Panel/VitalityBar");
        if (oldNestedVitality != null)
        {
            oldNestedVitality.gameObject.SetActive(false);
        }

        var vitalityHud = canvas.Find("VitalityHud");
        if (vitalityHud != null
            && (vitalityHud.Find("Mode") == null || vitalityHud.Find("ResetDemo") == null))
        {
            vitalityHud.gameObject.SetActive(false);
            Destroy(vitalityHud.gameObject);
            vitalityHud = null;
        }

        if (vitalityHud == null)
        {
            var vitalityObject = CreateUiObject("VitalityHud", canvas);
            vitalityHud = vitalityObject.transform;
            var vitalityRect = vitalityObject.GetComponent<RectTransform>();
            vitalityRect.anchorMin = Vector2.one;
            vitalityRect.anchorMax = Vector2.one;
            vitalityRect.pivot = Vector2.one;
            vitalityRect.anchoredPosition = new Vector2(-32f, -54f);
            vitalityRect.sizeDelta = new Vector2(330f, 86f);
            _vitalityHudRect = vitalityRect;

            var card = vitalityObject.AddComponent<UIImage>();
            card.color = new Color(0.07f, 0.12f, 0.1f, 0.84f);
            card.raycastTarget = false;

            var titleObject = CreateUiObject("Title", vitalityHud);
            var titleRect = titleObject.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0.48f);
            titleRect.anchorMax = new Vector2(0.22f, 1f);
            titleRect.offsetMin = new Vector2(16f, 0f);
            titleRect.offsetMax = Vector2.zero;
            var title = titleObject.AddComponent<Text>();
            title.font = font;
            title.fontSize = 20;
            title.alignment = TextAnchor.MiddleLeft;
            title.color = new Color(0.9f, 0.94f, 0.87f, 1f);
            title.text = "\u6d3b\u529b";
            title.raycastTarget = false;

            var modeObject = CreateUiObject("Mode", vitalityHud);
            var modeRect = modeObject.GetComponent<RectTransform>();
            modeRect.anchorMin = new Vector2(0.22f, 0.48f);
            modeRect.anchorMax = new Vector2(0.5f, 1f);
            modeRect.offsetMin = Vector2.zero;
            modeRect.offsetMax = Vector2.zero;
            _experienceModeText = modeObject.AddComponent<Text>();
            _experienceModeText.font = font;
            _experienceModeText.fontSize = 15;
            _experienceModeText.alignment = TextAnchor.MiddleCenter;
            _experienceModeText.color = new Color(0.96f, 0.7f, 0.24f, 1f);
            _experienceModeText.raycastTarget = false;

            var backgroundObject = CreateUiObject("Track", vitalityHud);
            var backgroundRect = backgroundObject.GetComponent<RectTransform>();
            backgroundRect.anchorMin = new Vector2(0f, 0f);
            backgroundRect.anchorMax = new Vector2(1f, 0.38f);
            backgroundRect.offsetMin = new Vector2(16f, 14f);
            backgroundRect.offsetMax = new Vector2(-16f, 0f);
            var background = backgroundObject.AddComponent<UIImage>();
            background.color = new Color(0.02f, 0.04f, 0.03f, 0.7f);
            background.raycastTarget = false;

            var fillObject = CreateUiObject("Fill", backgroundObject.transform);
            var fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(3f, 3f);
            fillRect.offsetMax = new Vector2(-3f, -3f);
            _vitalityFillImage = fillObject.AddComponent<UIImage>();
            _vitalityFillImage.raycastTarget = false;

            var valueObject = CreateUiObject("Value", vitalityHud);
            var valueRect = valueObject.GetComponent<RectTransform>();
            valueRect.anchorMin = new Vector2(0.5f, 0.48f);
            valueRect.anchorMax = new Vector2(0.7f, 1f);
            valueRect.offsetMin = Vector2.zero;
            valueRect.offsetMax = Vector2.zero;
            _vitalityValueText = valueObject.AddComponent<Text>();
            _vitalityValueText.font = font;
            _vitalityValueText.fontSize = 20;
            _vitalityValueText.fontStyle = FontStyle.Bold;
            _vitalityValueText.alignment = TextAnchor.MiddleRight;
            _vitalityValueText.color = Color.white;
            _vitalityValueText.raycastTarget = false;

            var resetObject = CreateUiObject("ResetDemo", vitalityHud);
            var resetRect = resetObject.GetComponent<RectTransform>();
            resetRect.anchorMin = new Vector2(0.72f, 0.5f);
            resetRect.anchorMax = new Vector2(0.98f, 0.98f);
            resetRect.offsetMin = Vector2.zero;
            resetRect.offsetMax = Vector2.zero;
            var resetImage = resetObject.AddComponent<UIImage>();
            resetImage.color = new Color(0.38f, 0.28f, 0.12f, 0.95f);
            _demoResetButton = resetObject.AddComponent<Button>();
            _demoResetButton.targetGraphic = resetImage;
            var resetLabelObject = CreateUiObject("Label", resetObject.transform);
            var resetLabelRect = resetLabelObject.GetComponent<RectTransform>();
            resetLabelRect.anchorMin = Vector2.zero;
            resetLabelRect.anchorMax = Vector2.one;
            resetLabelRect.offsetMin = Vector2.zero;
            resetLabelRect.offsetMax = Vector2.zero;
            var resetLabel = resetLabelObject.AddComponent<Text>();
            resetLabel.font = font;
            resetLabel.fontSize = 16;
            resetLabel.alignment = TextAnchor.MiddleCenter;
            resetLabel.color = Color.white;
            resetLabel.text = "\u91cd\u7f6e";
            resetLabel.raycastTarget = false;
        }
        else
        {
            vitalityHud.gameObject.SetActive(true);
            _vitalityHudRect = vitalityHud.GetComponent<RectTransform>();
            _vitalityFillImage = vitalityHud.Find("Track/Fill")?.GetComponent<UIImage>();
            _vitalityValueText = vitalityHud.Find("Value")?.GetComponent<Text>();
            _experienceModeText = vitalityHud.Find("Mode")?.GetComponent<Text>();
            _demoResetButton = vitalityHud.Find("ResetDemo")?.GetComponent<Button>();
        }

        var demoMode = _experienceProfile != null && _experienceProfile.IsPortfolioDemo;
        if (_experienceModeText != null)
        {
            _experienceModeText.gameObject.SetActive(demoMode);
            _experienceModeText.text = demoMode ? "\u6f14\u793a\u6a21\u5f0f" : string.Empty;
        }

        if (_demoResetButton != null)
        {
            _demoResetButton.gameObject.SetActive(demoMode);
            _demoResetButton.onClick.RemoveAllListeners();
            _demoResetButton.onClick.AddListener(ResetDemoState);
        }

        if (_vitalityValueText != null)
        {
            var valueRect = _vitalityValueText.rectTransform;
            valueRect.anchorMin = new Vector2(demoMode ? 0.5f : 0.55f, 0.48f);
            valueRect.anchorMax = new Vector2(demoMode ? 0.7f : 1f, 1f);
            valueRect.offsetMax = new Vector2(demoMode ? 0f : -16f, 0f);
        }

        UpdateVitalityHudSafeArea();
    }

    private void UpdateVitalityHudSafeArea()
    {
        if (_vitalityHudRect == null || Screen.width <= 0 || Screen.height <= 0)
        {
            return;
        }

        var canvas = _vitalityHudRect.GetComponentInParent<Canvas>();
        var scaleFactor = canvas != null ? Mathf.Max(0.01f, canvas.scaleFactor) : 1f;
        var safeArea = Screen.safeArea;
        var rightInset = (Screen.width - safeArea.xMax) / scaleFactor;
        var topInset = (Screen.height - safeArea.yMax) / scaleFactor;
        _vitalityHudRect.anchoredPosition = new Vector2(
            -32f - rightInset,
            -32f - topInset);
    }

    private void UpdateVitalityUi(float vitality)
    {
        var normalizedVitality = Mathf.Clamp01(vitality);
        if (_vitalityFillImage != null)
        {
            var fillRect = _vitalityFillImage.rectTransform;
            fillRect.anchorMax = new Vector2(normalizedVitality, 1f);
            _vitalityFillImage.color = GetVitalityColor(normalizedVitality);
        }

        if (_vitalityValueText != null)
        {
            _vitalityValueText.text = $"{Mathf.RoundToInt(normalizedVitality * 100f)}%";
        }
    }

    private static Color GetVitalityColor(float vitality)
    {
        var low = new Color(0.82f, 0.25f, 0.17f, 1f);
        var medium = new Color(0.94f, 0.64f, 0.18f, 1f);
        var healthy = new Color(0.25f, 0.73f, 0.38f, 1f);
        var normalizedVitality = Mathf.Clamp01(vitality);
        return normalizedVitality < 0.55f
            ? Color.Lerp(low, medium, normalizedVitality / 0.55f)
            : Color.Lerp(medium, healthy, (normalizedVitality - 0.55f) / 0.45f);
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

        }
        else if (HydrangeaDroopRigController.ContainsDroopRig(runtimeModel.transform))
        {
            _droopController = runtimeModel.GetComponent<HydrangeaDroopRigController>();
            if (_droopController == null)
            {
                _droopController = runtimeModel.AddComponent<HydrangeaDroopRigController>();
            }

            _droopController.Initialize();
        }

        if (!_view.Bind(
                _meshRenderer,
                _droopController,
                _wiltRigV3Controller,
                _requestedDroopLevel,
                _wiltTint,
                _wiltTintStrength))
        {
            _modelInstance = null;
            _meshRenderer = null;
            _droopController = null;
            _wiltRigV3Controller = null;
            Destroy(runtimeModel);
            return false;
        }

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
        var panelRect = panel.GetComponent<RectTransform>();
        if (panelRect != null)
        {
            panelRect.sizeDelta = new Vector2(620f, 156f);
        }

        EnsureVitalityUi(existingCanvas.transform, LoadUiFont());
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
        var responseRoll = _view != null ? _view.ResponseRoll : 0f;
        var lifeRotation = _view != null ? _view.LifeRotationEuler : Vector3.zero;
        _stageRoot.localRotation =
            baseRotation
            * Quaternion.Euler(_additionalEulerOffset)
            * Quaternion.Euler(
                lifeRotation.x,
                lifeRotation.y,
                lifeRotation.z + responseRoll);

        var safeScale = Mathf.Max(0.0001f, _modelBaseScale);
        var responseScale = _view != null ? _view.ResponseScale : 1f;
        var lifeScale = _view != null ? _view.LifeScale : 1f;
        _stageRoot.localScale =
            Vector3.one * safeScale * _presenceScale * lifeScale * responseScale;
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
