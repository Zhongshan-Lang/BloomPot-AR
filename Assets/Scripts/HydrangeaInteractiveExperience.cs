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
    private const float CompanionStateEvaluationInterval = 1f;
    private const float CompanionGrowthTransitionDuration = 8f;
    private const float DemoGrowthTransitionDuration = 20f;
    private const float LightSamplingDuration = 3f;
    private const string SceneModelName = "Hydrangea_Growth_BlendShape_Unity";
    private static readonly Vector3 BlenderAxisFixEuler = new Vector3(-90f, 0f, 0f);
    private static readonly string[] GrowthStageNames =
    {
        "Seed",
        "Sprout",
        "Leafing",
        "Bud",
        "HalfBloom",
        "Bloom"
    };

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
    private PlantHydrationController _plantHydrationController;
    private PlantInteractionController _plantInteractionController;
    private PlantStageProgressionController _stageProgressionController;
    private PlantWateringController _wateringController;
    private PlantLightController _plantLightController;
    private World _illuminationWorld;
    private Text _statusText;
    private UIImage _vitalityFillImage;
    private Slider _vitalitySlider;
    private UIImage _vitalitySliderHandleImage;
    private Text _vitalityValueText;
    private UIImage _hydrationFillImage;
    private Text _hydrationValueText;
    private UIImage _bondFillImage;
    private Text _bondValueText;
    private UIImage _growthFillImage;
    private Text _growthValueText;
    private UIImage _lightFillImage;
    private Text _lightValueText;
    private RectTransform _vitalityHudRect;
    private RectTransform _careHudRect;
    private Text _experienceModeText;
    private Button _demoResetButton;
    private Text _growthProgressText;
    private Button _wateringButton;
    private Text _wateringButtonText;
    private Button _lightButton;
    private Text _lightButtonText;
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
    private bool _demoSessionStartedFromSeed;
    private float _automaticGrowthTarget = -1f;
    private float _nextDemoAutosaveTime;
    private float _nextCompanionStateEvaluationTime;
    private bool _lightSamplingActive;
    private float _lightSamplingStartTime;
    private float _lightSampleSum;
    private int _lightSampleCount;
    private string _lightDetectionMessage;
    private float _lightDetectionMessageUntil;

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
        TrySubscribeToIllumination();
        EnsureEventSystem();
        EnsureUi();
        RefreshUiState(IsTracked());
    }

    private void Update()
    {
        var tracked = IsTracked();
        StartDemoSessionFromSeedOnFirstRecognition(tracked);
        EnsureModelReady(tracked);
        UpdateLightExposure(tracked);
        UpdateRealtimeGrowth();
        UpdateCompanionInteraction(tracked);
        UpdateVitalityHudSafeArea();
        var modelReady = _view != null && _view.IsReady;
        var vitality = _plantState != null ? _plantState.vitality : 1f;
        var appearanceDecay = ShouldShowDroopTestControls
            ? _requestedDroopLevel
            : CalculateCurrentAppearanceDecay();
        _view.Tick(
            tracked && modelReady && !ShouldShowDroopTestControls,
            vitality,
            appearanceDecay);
        SyncDisplayedGrowthProgress();
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
        if (ShouldShowDroopTestControls)
        {
            _view?.SetAppearanceDecayImmediate(_requestedDroopLevel);
        }
    }

    public void SetDroopLevelImmediate(float value01)
    {
        _requestedDroopLevel = Mathf.Clamp01(value01);
        _view?.SetWiltImmediate(_requestedDroopLevel);
        if (ShouldShowDroopTestControls)
        {
            _view?.SetAppearanceDecayImmediate(_requestedDroopLevel);
        }
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

    private void OnDestroy()
    {
        UnsubscribeFromIllumination();
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
            _plantHydrationController = new PlantHydrationController(_experienceProfile);
            _plantInteractionController = new PlantInteractionController(_experienceProfile);
            _stageProgressionController = new PlantStageProgressionController(_experienceProfile);
            _wateringController = new PlantWateringController(_experienceProfile);
            _plantLightController = new PlantLightController(_experienceProfile);
            var now = System.DateTime.UtcNow;
            var progress = _plantGrowthController.ApplyOfflineProgress(_plantState, now);
            var hydrationProgress = _plantHydrationController.ApplyTimeProgress(_plantState, now);
            _plantGrowthController.ResetRealtime(now);
            _nextCompanionStateEvaluationTime =
                Time.unscaledTime + CompanionStateEvaluationInterval;
            _requestedDroopLevel = ShouldShowDroopTestControls ? 0f : progress.WiltAmount;
            var growthChanged = EvaluateGrowthProgress(now);
            if (progress.VitalityChanged || hydrationProgress.HydrationChanged || growthChanged)
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
            _plantHydrationController = new PlantHydrationController(_experienceProfile);
            _plantInteractionController = new PlantInteractionController(_experienceProfile);
            _stageProgressionController = new PlantStageProgressionController(_experienceProfile);
            _wateringController = new PlantWateringController(_experienceProfile);
            _plantLightController = new PlantLightController(_experienceProfile);
            _plantGrowthController.ResetRealtime(System.DateTime.UtcNow);
            _nextCompanionStateEvaluationTime =
                Time.unscaledTime + CompanionStateEvaluationInterval;
            _requestedDroopLevel = 0f;
        }
    }

    private void UpdateRealtimeGrowth()
    {
        if (_experienceProfile == null
            || _plantGrowthController == null
            || _plantHydrationController == null
            || _plantState == null
            || ShouldShowDroopTestControls)
        {
            return;
        }

        if (!_experienceProfile.IsPortfolioDemo
            && Time.unscaledTime < _nextCompanionStateEvaluationTime)
        {
            return;
        }

        if (!_experienceProfile.IsPortfolioDemo)
        {
            _nextCompanionStateEvaluationTime =
                Time.unscaledTime + CompanionStateEvaluationInterval;
        }

        var now = System.DateTime.UtcNow;
        var progress = _plantGrowthController.ApplyRealtimeProgress(_plantState, now);
        var hydrationProgress = _plantHydrationController.ApplyTimeProgress(_plantState, now);
        var vitalityChanged = progress.VitalityChanged;
        var growthChanged = vitalityChanged && EvaluateGrowthProgress(now);

        if (!vitalityChanged && !hydrationProgress.HydrationChanged && !growthChanged)
        {
            return;
        }

        if (vitalityChanged)
        {
            ApplyCompanionStateToView();
        }

        _hasUiStateSnapshot = false;
        if (Time.unscaledTime >= _nextDemoAutosaveTime)
        {
            SavePersistentState();
            _nextDemoAutosaveTime = Time.unscaledTime + DemoAutosaveInterval;
        }
    }

    private void TrySubscribeToIllumination()
    {
        var vuforiaBehaviour = VuforiaBehaviour.Instance;
        var world = vuforiaBehaviour != null ? vuforiaBehaviour.World : null;
        if (world == null || ReferenceEquals(world, _illuminationWorld))
        {
            return;
        }

        UnsubscribeFromIllumination();
        _illuminationWorld = world;
        _illuminationWorld.OnStateUpdated += OnVuforiaStateUpdated;
    }

    private void UnsubscribeFromIllumination()
    {
        if (_illuminationWorld == null)
        {
            return;
        }

        _illuminationWorld.OnStateUpdated -= OnVuforiaStateUpdated;
        _illuminationWorld = null;
    }

    private void OnVuforiaStateUpdated()
    {
        if (!_lightSamplingActive || _illuminationWorld == null)
        {
            return;
        }

        var illumination = _illuminationWorld.IlluminationData;
        float normalizedLight;
        if (illumination.AmbientIntensity.HasValue
            && illumination.AmbientIntensity.Value >= 0f)
        {
            normalizedLight = PlantLightController.NormalizeAmbientIntensity(
                illumination.AmbientIntensity.Value);
        }
        else if (illumination.IntensityCorrection.HasValue)
        {
            normalizedLight = PlantLightController.NormalizeIntensityCorrection(
                illumination.IntensityCorrection.Value);
        }
        else
        {
            return;
        }

        _lightSampleSum += normalizedLight;
        _lightSampleCount++;
    }

    private void UpdateLightExposure(bool tracked)
    {
        if (_plantLightController == null
            || _plantState == null
            || ShouldShowDroopTestControls)
        {
            return;
        }

        TrySubscribeToIllumination();
        var now = System.DateTime.UtcNow;
        if (_plantLightController.RefreshDay(_plantState, now))
        {
            ResetLightSampling();
            _hasUiStateSnapshot = false;
            SavePersistentState();
        }

        if (_plantState.lightEnvironmentSampled)
        {
            ResetLightSampling();
            return;
        }

        if (!tracked)
        {
            ResetLightSampling();
            return;
        }

        if (!_lightSamplingActive)
        {
            _lightSamplingActive = true;
            _lightSamplingStartTime = Time.unscaledTime;
            _lightSampleSum = 0f;
            _lightSampleCount = 0;
            _hasUiStateSnapshot = false;
            return;
        }

        if (Time.unscaledTime - _lightSamplingStartTime < LightSamplingDuration)
        {
            return;
        }

        var usedMeasuredLight = _lightSampleCount > 0;
        var initialLight = usedMeasuredLight
            ? _lightSampleSum / _lightSampleCount
            : PlantLightController.NeutralLight;
        var result = _plantLightController.CaptureEnvironmentLight(
            _plantState,
            initialLight,
            now);
        ResetLightSampling();
        if (!result.Captured)
        {
            return;
        }

        var lightPercent = Mathf.RoundToInt(result.CurrentLight * 100f);
        _lightDetectionMessage = usedMeasuredLight
            ? $"\u5149\u7167\u68c0\u6d4b\u6210\u529f\uff1a{lightPercent}%  \u00b7  " +
              GetLightEffectLabel(result.CurrentLight)
            : "\u672a\u83b7\u53d6\u771f\u5b9e\u5149\u7167\uff0c\u5df2\u4f7f\u7528\u4e2d\u6027\u503c 70%";
        _lightDetectionMessageUntil = Time.unscaledTime + 4f;

        var growthChanged = EvaluateGrowthProgress(now);
        if (growthChanged)
        {
            ApplyCompanionStateToView();
        }

        _hasUiStateSnapshot = false;
        SavePersistentState();
    }

    private void ResetLightSampling()
    {
        _lightSamplingActive = false;
        _lightSamplingStartTime = 0f;
        _lightSampleSum = 0f;
        _lightSampleCount = 0;
    }

    private void SavePersistentState()
    {
        if (_plantSaveService == null || _plantState == null)
        {
            return;
        }

        SyncDisplayedGrowthProgress();

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
        _automaticGrowthTarget = -1f;
        ResetLightSampling();
        _lightDetectionMessage = string.Empty;
        _lightDetectionMessageUntil = 0f;
        _view?.SetGrowthProgressImmediate(_plantState.growthProgress);
        ApplyCompanionStateToView();
        SavePersistentState();
    }

    private void StartDemoSessionFromSeedOnFirstRecognition(bool tracked)
    {
        if (!tracked
            || _demoSessionStartedFromSeed
            || ShouldShowDroopTestControls
            || _experienceProfile == null
            || !_experienceProfile.IsPortfolioDemo)
        {
            return;
        }

        _demoSessionStartedFromSeed = true;
        ResetDemoState();
    }

    private void SetDemoVitality(float vitality)
    {
        if (_experienceProfile == null
            || !_experienceProfile.IsPortfolioDemo
            || _plantState == null
            || _plantGrowthController == null)
        {
            return;
        }

        var now = System.DateTime.UtcNow;
        _plantState.vitality = Mathf.Clamp01(vitality);
        _plantState.lastInteractionUtc = PlantState.FormatUtc(now);
        _plantGrowthController.ResetRealtime(now);
        var growthChanged = EvaluateGrowthProgress(now);

        var wiltTarget = _plantGrowthController.CalculateWilt(_plantState.vitality);
        if (!Mathf.Approximately(_requestedDroopLevel, wiltTarget))
        {
            _requestedDroopLevel = wiltTarget;
            _view?.SetWiltTarget(_requestedDroopLevel);
        }

        _hasUiStateSnapshot = false;
        RefreshUiState(IsTracked());
        if (growthChanged)
        {
            SavePersistentState();
        }
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
        _stageButtons.Add(CreateStageButton(row.transform, font, "\u79cd\u5b50", "Seed"));
        _stageButtons.Add(CreateStageButton(row.transform, font, "\u5e7c\u82d7", "Sprout"));
        _stageButtons.Add(CreateStageButton(row.transform, font, "\u5c55\u53f6", "Leafing"));
        _stageButtons.Add(CreateStageButton(row.transform, font, "\u82b1\u82de", "Bud"));
        _stageButtons.Add(CreateStageButton(row.transform, font, "\u534a\u5f00", "HalfBloom"));
        _stageButtons.Add(CreateStageButton(row.transform, font, "\u76db\u5f00", "Bloom"));

        EnsureVitalityUi(canvasObject.transform, font);
        EnsureCareUi(canvasObject.transform, font);

        if (ShouldShowDroopTestControls)
        {
            CreateDroopControls(canvasObject.transform, font);
        }

        LogUiDiagnostics("created");
    }

    private void RefreshUiState(bool tracked)
    {
        var lightDetectionMessageActive = !string.IsNullOrEmpty(_lightDetectionMessage)
            && Time.unscaledTime < _lightDetectionMessageUntil;
        if (!lightDetectionMessageActive && _lightDetectionMessageUntil > 0f)
        {
            _lightDetectionMessage = string.Empty;
            _lightDetectionMessageUntil = 0f;
            _hasUiStateSnapshot = false;
        }

        var modelReady = _view != null && _view.IsReady;
        var currentStageName = _view != null ? _view.CurrentStageName : null;
        var vitality = _plantState != null ? Mathf.Clamp01(_plantState.vitality) : 1f;
        UpdateCareUi(tracked, modelReady);
        UpdatePlantMetricUi();
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
            else if (lightDetectionMessageActive)
            {
                _statusText.text = _lightDetectionMessage;
            }
            else if (_plantState != null && !_plantState.lightEnvironmentSampled)
            {
                _statusText.text = "\u5df2\u8bc6\u522b\u56fe\u50cf\uff0c\u6b63\u5728\u68c0\u6d4b\u5f53\u524d\u73af\u5883\u5149\u7167";
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
                var hasStage = _buttonStages.TryGetValue(button, out var stageName);
                var stageIndex = hasStage ? GetStageIndex(stageName) : -1;
                var freeStageSelection = _experienceProfile == null
                    || _experienceProfile.IsPortfolioDemo
                    || ShouldShowDroopTestControls;
                button.interactable = tracked
                    && hasStage
                    && _view != null
                    && _view.CanPresentStage(stageName)
                    && (freeStageSelection
                        || (_plantState != null && stageIndex == _plantState.stageIndex));

                if (button.targetGraphic is UIImage image)
                {
                    var isCurrentStage = freeStageSelection
                        ? string.Equals(stageName, currentStageName, System.StringComparison.Ordinal)
                        : _plantState != null && stageIndex == _plantState.stageIndex;
                    image.color = isCurrentStage
                        ? new Color(0.77f, 0.52f, 0.2f, 0.98f)
                        : new Color(0.23f, 0.45f, 0.35f, 0.95f);
                }
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
        var now = System.DateTime.UtcNow;
        var result = _plantInteractionController.RegisterVisit(_plantState, now);
        var growthChanged = EvaluateGrowthProgress(now);
        if (result.StateChanged || growthChanged)
        {
            ApplyCompanionStateToView();
            SavePersistentState();
        }
    }

    private void RegisterCompanionTap()
    {
        var now = System.DateTime.UtcNow;
        var result = _plantInteractionController.RegisterTap(_plantState, now);
        _view?.PlayInteractionResponse(result.Rewarded ? 1f : 0.55f);
        var growthChanged = EvaluateGrowthProgress(now);
        if (result.StateChanged || growthChanged)
        {
            ApplyCompanionStateToView();
            SavePersistentState();
        }
    }

    private void RegisterWatering()
    {
        if (_wateringController == null || _plantState == null)
        {
            return;
        }

        var now = System.DateTime.UtcNow;
        var result = _wateringController.RegisterWatering(_plantState, now);
        _view?.PlayInteractionResponse(result.Rewarded ? 0.85f : 0.35f);
        var growthChanged = EvaluateGrowthProgress(now);
        if (result.StateChanged || growthChanged)
        {
            ApplyCompanionStateToView();
            SavePersistentState();
        }

        _hasUiStateSnapshot = false;
        RefreshUiState(IsTracked());
    }

    private void RegisterLightBoost()
    {
        if (_plantLightController == null || _plantState == null)
        {
            return;
        }

        var now = System.DateTime.UtcNow;
        var result = _plantLightController.AddLightBoost(_plantState, now);
        _view?.PlayInteractionResponse(result.Added ? 0.7f : 0.25f);
        if (result.Added)
        {
            EvaluateGrowthProgress(now);
            SavePersistentState();
        }

        _hasUiStateSnapshot = false;
        RefreshUiState(IsTracked());
    }

    private bool EvaluateGrowthProgress(System.DateTime utcNow)
    {
        if (_experienceProfile == null
            || ShouldShowDroopTestControls
            || _stageProgressionController == null
            || _plantState == null)
        {
            return false;
        }

        var result = _stageProgressionController.EvaluateProgress(_plantState, utcNow);
        if (!result.ProgressChanged)
        {
            return false;
        }

        StartAutomaticGrowthTransition(result.CurrentProgress);

        _hasUiStateSnapshot = false;
        return true;
    }

    private void StartAutomaticGrowthTransition(float targetProgress)
    {
        _automaticGrowthTarget = Mathf.Clamp01(targetProgress);
        var transitionDuration = _experienceProfile != null && _experienceProfile.IsPortfolioDemo
            ? DemoGrowthTransitionDuration
            : CompanionGrowthTransitionDuration;
        _view?.SetGrowthProgressTarget(_automaticGrowthTarget, transitionDuration);
    }

    private void SyncDisplayedGrowthProgress()
    {
        if (_automaticGrowthTarget < 0f || _plantState == null || _view == null || !_view.IsReady)
        {
            return;
        }

        var displayedProgress = Mathf.Min(
            _plantState.growthProgress,
            _view.CurrentGrowthProgress);
        _plantState.displayedGrowthProgress = Mathf.Max(
            _plantState.displayedGrowthProgress,
            displayedProgress);
        if (Mathf.Abs(_view.CurrentGrowthProgress - _automaticGrowthTarget) < 0.0001f)
        {
            _plantState.displayedGrowthProgress = _automaticGrowthTarget;
            _automaticGrowthTarget = -1f;
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

    private float CalculateCurrentAppearanceDecay()
    {
        return _plantGrowthController != null && _plantState != null
            ? _plantGrowthController.CalculateAppearanceDecay(_plantState.vitality)
            : 0f;
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
        buttonRect.sizeDelta = new Vector2(90f, 52f);

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
        if (_experienceProfile != null
            && !_experienceProfile.IsPortfolioDemo
            && !ShouldShowDroopTestControls)
        {
            return;
        }

        _automaticGrowthTarget = -1f;
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
            && (vitalityHud.Find("Mode") == null
                || vitalityHud.Find("ResetDemo") == null
                || vitalityHud.Find("VitalityTrack") == null
                || vitalityHud.Find("HydrationTrack") == null
                || vitalityHud.Find("BondTrack") == null
                || vitalityHud.Find("GrowthTrack") == null
                || vitalityHud.Find("LightTrack") == null))
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
            vitalityRect.sizeDelta = new Vector2(400f, 278f);
            _vitalityHudRect = vitalityRect;

            var card = vitalityObject.AddComponent<UIImage>();
            card.color = new Color(0.07f, 0.12f, 0.1f, 0.84f);
            card.raycastTarget = false;

            var titleObject = CreateUiObject("Title", vitalityHud);
            var titleRect = titleObject.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0.83f);
            titleRect.anchorMax = new Vector2(0.3f, 1f);
            titleRect.offsetMin = new Vector2(16f, 0f);
            titleRect.offsetMax = Vector2.zero;
            var title = titleObject.AddComponent<Text>();
            title.font = font;
            title.fontSize = 20;
            title.alignment = TextAnchor.MiddleLeft;
            title.color = new Color(0.9f, 0.94f, 0.87f, 1f);
            title.text = "\u690d\u7269\u72b6\u6001";
            title.raycastTarget = false;

            var modeObject = CreateUiObject("Mode", vitalityHud);
            var modeRect = modeObject.GetComponent<RectTransform>();
            modeRect.anchorMin = new Vector2(0.3f, 0.83f);
            modeRect.anchorMax = new Vector2(0.72f, 1f);
            modeRect.offsetMin = Vector2.zero;
            modeRect.offsetMax = Vector2.zero;
            _experienceModeText = modeObject.AddComponent<Text>();
            _experienceModeText.font = font;
            _experienceModeText.fontSize = 15;
            _experienceModeText.alignment = TextAnchor.MiddleCenter;
            _experienceModeText.color = new Color(0.96f, 0.7f, 0.24f, 1f);
            _experienceModeText.raycastTarget = false;

            CreateMetricRow(vitalityHud, font, "Vitality", "\u6d3b\u529b", 0.67f, 0.82f,
                out var vitalityTrack, out _vitalityFillImage, out _vitalityValueText);
            CreateMetricRow(vitalityHud, font, "Hydration", "\u6c34\u5206", 0.52f, 0.67f,
                out _, out _hydrationFillImage, out _hydrationValueText);
            CreateMetricRow(vitalityHud, font, "Bond", "\u4eb2\u5bc6", 0.37f, 0.52f,
                out _, out _bondFillImage, out _bondValueText);
            CreateMetricRow(vitalityHud, font, "Growth", "\u6210\u957f", 0.22f, 0.37f,
                out _, out _growthFillImage, out _growthValueText);
            CreateMetricRow(vitalityHud, font, "Light", "\u5149\u7167", 0.07f, 0.22f,
                out _, out _lightFillImage, out _lightValueText);

            var fillRect = _vitalityFillImage.rectTransform;
            var handleObject = CreateUiObject("Handle", vitalityTrack.transform);
            var handleRect = handleObject.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(24f, 32f);
            _vitalitySliderHandleImage = handleObject.AddComponent<UIImage>();
            _vitalitySliderHandleImage.color = new Color(0.96f, 0.95f, 0.84f, 1f);

            _vitalitySlider = vitalityTrack.gameObject.AddComponent<Slider>();
            _vitalitySlider.minValue = 0f;
            _vitalitySlider.maxValue = 1f;
            _vitalitySlider.direction = Slider.Direction.LeftToRight;
            _vitalitySlider.fillRect = fillRect;
            _vitalitySlider.handleRect = handleRect;
            _vitalitySlider.targetGraphic = _vitalitySliderHandleImage;

            var resetObject = CreateUiObject("ResetDemo", vitalityHud);
            var resetRect = resetObject.GetComponent<RectTransform>();
            resetRect.anchorMin = new Vector2(0.74f, 0.82f);
            resetRect.anchorMax = new Vector2(0.97f, 0.97f);
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
            _vitalityFillImage = vitalityHud.Find("VitalityTrack/Fill")?.GetComponent<UIImage>();
            _vitalitySlider = vitalityHud.Find("VitalityTrack")?.GetComponent<Slider>();
            _vitalitySliderHandleImage =
                vitalityHud.Find("VitalityTrack/Handle")?.GetComponent<UIImage>();
            _vitalityValueText = vitalityHud.Find("VitalityValue")?.GetComponent<Text>();
            _hydrationFillImage = vitalityHud.Find("HydrationTrack/Fill")?.GetComponent<UIImage>();
            _hydrationValueText = vitalityHud.Find("HydrationValue")?.GetComponent<Text>();
            _bondFillImage = vitalityHud.Find("BondTrack/Fill")?.GetComponent<UIImage>();
            _bondValueText = vitalityHud.Find("BondValue")?.GetComponent<Text>();
            _growthFillImage = vitalityHud.Find("GrowthTrack/Fill")?.GetComponent<UIImage>();
            _growthValueText = vitalityHud.Find("GrowthValue")?.GetComponent<Text>();
            _lightFillImage = vitalityHud.Find("LightTrack/Fill")?.GetComponent<UIImage>();
            _lightValueText = vitalityHud.Find("LightValue")?.GetComponent<Text>();
            _experienceModeText = vitalityHud.Find("Mode")?.GetComponent<Text>();
            _demoResetButton = vitalityHud.Find("ResetDemo")?.GetComponent<Button>();
        }

        var demoMode = _experienceProfile != null && _experienceProfile.IsPortfolioDemo;
        EnsureVitalitySlider(vitalityHud, demoMode);
        if (_experienceModeText != null)
        {
            _experienceModeText.gameObject.SetActive(demoMode);
            _experienceModeText.text = demoMode ? "\u6f14\u793a \u00b7 \u53ef\u62d6\u52a8" : string.Empty;
        }

        if (_demoResetButton != null)
        {
            _demoResetButton.gameObject.SetActive(demoMode);
            _demoResetButton.onClick.RemoveAllListeners();
            _demoResetButton.onClick.AddListener(ResetDemoState);
        }

        UpdateVitalityHudSafeArea();
    }

    private static void CreateMetricRow(
        Transform parent,
        Font font,
        string metricName,
        string labelText,
        float rowMin,
        float rowMax,
        out UIImage track,
        out UIImage fill,
        out Text valueText)
    {
        var labelObject = CreateUiObject(metricName + "Label", parent);
        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0f, rowMin);
        labelRect.anchorMax = new Vector2(0.22f, rowMax);
        labelRect.offsetMin = new Vector2(16f, 0f);
        labelRect.offsetMax = Vector2.zero;
        var label = labelObject.AddComponent<Text>();
        label.font = font;
        label.fontSize = 17;
        label.alignment = TextAnchor.MiddleLeft;
        label.color = new Color(0.86f, 0.91f, 0.84f, 1f);
        label.text = labelText;
        label.raycastTarget = false;

        var trackObject = CreateUiObject(metricName + "Track", parent);
        var trackRect = trackObject.GetComponent<RectTransform>();
        trackRect.anchorMin = new Vector2(0.22f, rowMin + 0.045f);
        trackRect.anchorMax = new Vector2(0.78f, rowMax - 0.045f);
        trackRect.offsetMin = Vector2.zero;
        trackRect.offsetMax = Vector2.zero;
        track = trackObject.AddComponent<UIImage>();
        track.color = new Color(0.02f, 0.04f, 0.03f, 0.72f);
        track.raycastTarget = false;

        var fillObject = CreateUiObject("Fill", trackObject.transform);
        var fillRect = fillObject.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(3f, 3f);
        fillRect.offsetMax = new Vector2(-3f, -3f);
        fill = fillObject.AddComponent<UIImage>();
        fill.raycastTarget = false;

        var valueObject = CreateUiObject(metricName + "Value", parent);
        var valueRect = valueObject.GetComponent<RectTransform>();
        valueRect.anchorMin = new Vector2(0.79f, rowMin);
        valueRect.anchorMax = new Vector2(0.97f, rowMax);
        valueRect.offsetMin = Vector2.zero;
        valueRect.offsetMax = Vector2.zero;
        valueText = valueObject.AddComponent<Text>();
        valueText.font = font;
        valueText.fontSize = 17;
        valueText.fontStyle = FontStyle.Bold;
        valueText.alignment = TextAnchor.MiddleRight;
        valueText.color = Color.white;
        valueText.raycastTarget = false;
    }

    private void EnsureCareUi(Transform canvas, Font font)
    {
        var careHud = canvas.Find("CareHud");
        if (careHud != null
            && (careHud.Find("Progress") == null
                || careHud.Find("Water") == null
                || careHud.Find("Light") == null))
        {
            careHud.gameObject.SetActive(false);
            Destroy(careHud.gameObject);
            careHud = null;
        }

        if (careHud == null)
        {
            var careObject = CreateUiObject("CareHud", canvas);
            careHud = careObject.transform;
            _careHudRect = careObject.GetComponent<RectTransform>();
            _careHudRect.anchorMin = new Vector2(0f, 1f);
            _careHudRect.anchorMax = new Vector2(0f, 1f);
            _careHudRect.pivot = new Vector2(0f, 1f);
            _careHudRect.anchoredPosition = new Vector2(32f, -54f);
            _careHudRect.sizeDelta = new Vector2(430f, 154f);

            var card = careObject.AddComponent<UIImage>();
            card.color = new Color(0.07f, 0.12f, 0.1f, 0.86f);
            card.raycastTarget = false;

            var progressObject = CreateUiObject("Progress", careHud);
            var progressRect = progressObject.GetComponent<RectTransform>();
            progressRect.anchorMin = new Vector2(0f, 0.38f);
            progressRect.anchorMax = Vector2.one;
            progressRect.offsetMin = new Vector2(18f, 4f);
            progressRect.offsetMax = new Vector2(-18f, -10f);
            _growthProgressText = progressObject.AddComponent<Text>();
            _growthProgressText.font = font;
            _growthProgressText.fontSize = 17;
            _growthProgressText.alignment = TextAnchor.MiddleLeft;
            _growthProgressText.color = new Color(0.92f, 0.96f, 0.88f, 1f);
            _growthProgressText.raycastTarget = false;

            var waterObject = CreateUiObject("Water", careHud);
            var waterRect = waterObject.GetComponent<RectTransform>();
            waterRect.anchorMin = new Vector2(0.04f, 0.08f);
            waterRect.anchorMax = new Vector2(0.49f, 0.36f);
            waterRect.offsetMin = Vector2.zero;
            waterRect.offsetMax = Vector2.zero;
            var waterImage = waterObject.AddComponent<UIImage>();
            waterImage.color = new Color(0.16f, 0.49f, 0.62f, 0.98f);
            _wateringButton = waterObject.AddComponent<Button>();
            _wateringButton.targetGraphic = waterImage;

            var labelObject = CreateUiObject("Label", waterObject.transform);
            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            _wateringButtonText = labelObject.AddComponent<Text>();
            _wateringButtonText.font = font;
            _wateringButtonText.fontSize = 15;
            _wateringButtonText.fontStyle = FontStyle.Bold;
            _wateringButtonText.alignment = TextAnchor.MiddleCenter;
            _wateringButtonText.color = Color.white;
            _wateringButtonText.raycastTarget = false;

            var lightObject = CreateUiObject("Light", careHud);
            var lightRect = lightObject.GetComponent<RectTransform>();
            lightRect.anchorMin = new Vector2(0.51f, 0.08f);
            lightRect.anchorMax = new Vector2(0.96f, 0.36f);
            lightRect.offsetMin = Vector2.zero;
            lightRect.offsetMax = Vector2.zero;
            var lightImage = lightObject.AddComponent<UIImage>();
            lightImage.color = new Color(0.76f, 0.55f, 0.14f, 0.98f);
            _lightButton = lightObject.AddComponent<Button>();
            _lightButton.targetGraphic = lightImage;

            var lightLabelObject = CreateUiObject("Label", lightObject.transform);
            var lightLabelRect = lightLabelObject.GetComponent<RectTransform>();
            lightLabelRect.anchorMin = Vector2.zero;
            lightLabelRect.anchorMax = Vector2.one;
            lightLabelRect.offsetMin = Vector2.zero;
            lightLabelRect.offsetMax = Vector2.zero;
            _lightButtonText = lightLabelObject.AddComponent<Text>();
            _lightButtonText.font = font;
            _lightButtonText.fontSize = 15;
            _lightButtonText.fontStyle = FontStyle.Bold;
            _lightButtonText.alignment = TextAnchor.MiddleCenter;
            _lightButtonText.color = Color.white;
            _lightButtonText.raycastTarget = false;
        }
        else
        {
            careHud.gameObject.SetActive(true);
            _careHudRect = careHud.GetComponent<RectTransform>();
            _growthProgressText = careHud.Find("Progress")?.GetComponent<Text>();
            _wateringButton = careHud.Find("Water")?.GetComponent<Button>();
            _wateringButtonText = careHud.Find("Water/Label")?.GetComponent<Text>();
            _lightButton = careHud.Find("Light")?.GetComponent<Button>();
            _lightButtonText = careHud.Find("Light/Label")?.GetComponent<Text>();
        }

        careHud.gameObject.SetActive(!ShouldShowDroopTestControls);
        if (_wateringButton != null)
        {
            _wateringButton.onClick.RemoveAllListeners();
            _wateringButton.onClick.AddListener(RegisterWatering);
        }

        if (_lightButton != null)
        {
            _lightButton.onClick.RemoveAllListeners();
            _lightButton.onClick.AddListener(RegisterLightBoost);
        }

        UpdateVitalityHudSafeArea();
    }

    private void EnsureVitalitySlider(Transform vitalityHud, bool demoMode)
    {
        var track = vitalityHud != null ? vitalityHud.Find("VitalityTrack") : null;
        var fillRect = vitalityHud != null
            ? vitalityHud.Find("VitalityTrack/Fill") as RectTransform
            : null;
        if (track == null || fillRect == null)
        {
            return;
        }

        var trackImage = track.GetComponent<UIImage>();
        var handle = track.Find("Handle") as RectTransform;
        if (handle == null)
        {
            var handleObject = CreateUiObject("Handle", track);
            handle = handleObject.GetComponent<RectTransform>();
            handle.sizeDelta = new Vector2(28f, 40f);
            _vitalitySliderHandleImage = handleObject.AddComponent<UIImage>();
            _vitalitySliderHandleImage.color = new Color(0.96f, 0.95f, 0.84f, 1f);
        }
        else
        {
            _vitalitySliderHandleImage = handle.GetComponent<UIImage>();
        }

        _vitalitySlider = track.GetComponent<Slider>();
        if (_vitalitySlider == null)
        {
            _vitalitySlider = track.gameObject.AddComponent<Slider>();
        }

        _vitalitySlider.minValue = 0f;
        _vitalitySlider.maxValue = 1f;
        _vitalitySlider.wholeNumbers = false;
        _vitalitySlider.direction = Slider.Direction.LeftToRight;
        _vitalitySlider.fillRect = fillRect;
        _vitalitySlider.handleRect = handle;
        _vitalitySlider.targetGraphic = _vitalitySliderHandleImage;
        _vitalitySlider.interactable = demoMode;
        _vitalitySlider.onValueChanged.RemoveAllListeners();
        if (demoMode)
        {
            _vitalitySlider.onValueChanged.AddListener(SetDemoVitality);
        }

        if (trackImage != null)
        {
            trackImage.raycastTarget = demoMode;
        }

        if (_vitalitySliderHandleImage != null)
        {
            _vitalitySliderHandleImage.raycastTarget = demoMode;
            _vitalitySliderHandleImage.gameObject.SetActive(demoMode);
        }
    }

    private void UpdateVitalityHudSafeArea()
    {
        if ((_vitalityHudRect == null && _careHudRect == null)
            || Screen.width <= 0
            || Screen.height <= 0)
        {
            return;
        }

        var referenceRect = _vitalityHudRect != null ? _vitalityHudRect : _careHudRect;
        var canvas = referenceRect.GetComponentInParent<Canvas>();
        var scaleFactor = canvas != null ? Mathf.Max(0.01f, canvas.scaleFactor) : 1f;
        var safeArea = Screen.safeArea;
        var leftInset = safeArea.xMin / scaleFactor;
        var rightInset = (Screen.width - safeArea.xMax) / scaleFactor;
        var topInset = (Screen.height - safeArea.yMax) / scaleFactor;
        if (_vitalityHudRect != null)
        {
            _vitalityHudRect.anchoredPosition = new Vector2(
                -32f - rightInset,
                -32f - topInset);
        }

        if (_careHudRect != null)
        {
            _careHudRect.anchoredPosition = new Vector2(
                32f + leftInset,
                -32f - topInset);
        }
    }

    private void UpdateVitalityUi(float vitality)
    {
        var normalizedVitality = Mathf.Clamp01(vitality);
        if (_vitalitySlider != null)
        {
            _vitalitySlider.SetValueWithoutNotify(normalizedVitality);
        }

        if (_vitalityFillImage != null)
        {
            if (_vitalitySlider == null)
            {
                var fillRect = _vitalityFillImage.rectTransform;
                fillRect.anchorMax = new Vector2(normalizedVitality, 1f);
            }

            _vitalityFillImage.color = GetVitalityColor(normalizedVitality);
        }

        if (_vitalityValueText != null)
        {
            _vitalityValueText.text = $"{Mathf.RoundToInt(normalizedVitality * 100f)}%";
        }
    }

    private void UpdatePlantMetricUi()
    {
        if (_plantState == null)
        {
            return;
        }

        UpdateVitalityUi(_plantState.vitality);
        UpdateMetricUi(
            _hydrationFillImage,
            _hydrationValueText,
            _plantState.hydration,
            new Color(0.2f, 0.67f, 0.9f, 1f));
        UpdateMetricUi(
            _bondFillImage,
            _bondValueText,
            _plantState.bond,
            new Color(0.93f, 0.48f, 0.5f, 1f));
        UpdateMetricUi(
            _growthFillImage,
            _growthValueText,
            _plantState.displayedGrowthProgress,
            new Color(0.78f, 0.62f, 0.24f, 1f));
        UpdateMetricUi(
            _lightFillImage,
            _lightValueText,
            _plantState.lightExposure,
            GetLightColor(_plantState.lightExposure));
    }

    private static void UpdateMetricUi(UIImage fillImage, Text valueText, float value, Color color)
    {
        var normalizedValue = Mathf.Clamp01(value);
        if (fillImage != null)
        {
            var fillRect = fillImage.rectTransform;
            fillRect.anchorMax = new Vector2(normalizedValue, 1f);
            fillImage.color = color;
        }

        if (valueText != null)
        {
            valueText.text = $"{Mathf.RoundToInt(normalizedValue * 100f)}%";
        }
    }

    private void UpdateCareUi(bool tracked, bool modelReady)
    {
        if (_careHudRect == null
            || !_careHudRect.gameObject.activeSelf
            || _plantState == null
            || _wateringController == null
            || _plantLightController == null)
        {
            return;
        }

        var demoMode = _experienceProfile != null && _experienceProfile.IsPortfolioDemo;
        var displayedStageIndex = PlantStageProgressionController.GetStageIndexForProgress(
            _plantState.displayedGrowthProgress);
        if (_growthProgressText != null)
        {
            if (displayedStageIndex >= PlantState.MaximumStageIndex)
            {
                var modePrefix = demoMode ? "\u6f14\u793a\u6210\u957f \u00b7 " : string.Empty;
                _growthProgressText.text =
                    $"{modePrefix}\u5df2\u76db\u5f00  \u00b7  \u6210\u957f 100%\n" +
                    $"\u7167\u6599 {_plantState.careDayCount} \u5929  \u00b7  " +
                    $"\u4eb2\u5bc6\u5ea6 {Mathf.RoundToInt(_plantState.bond * 100f)}%";
            }
            else
            {
                var nextStage = displayedStageIndex + 1;
                var requiredDays = PlantStageProgressionController.GetRequiredCareDays(nextStage);
                var requiredBond = PlantStageProgressionController.GetRequiredBond(nextStage);
                _growthProgressText.text =
                    $"\u6210\u957f {Mathf.RoundToInt(_plantState.displayedGrowthProgress * 100f)}%  \u00b7  " +
                    $"\u4e0b\u4e00\u5f62\u6001 {GetStageLabel(GetStageName(nextStage))}\n" +
                    $"\u7167\u6599 {_plantState.careDayCount}/{requiredDays} \u5929  \u00b7  " +
                    $"\u4eb2\u5bc6 {Mathf.RoundToInt(_plantState.bond * 100f)}/" +
                    $"{Mathf.RoundToInt(requiredBond * 100f)}%  \u00b7  " +
                    $"\u5065\u5eb7 {Mathf.RoundToInt(_plantState.vitality * 100f)}%";
            }
        }

        var now = System.DateTime.UtcNow;
        var canWater = _wateringController.CanWater(_plantState, now);
        if (_wateringButton != null)
        {
            _wateringButton.interactable = tracked && modelReady && canWater;
        }

        if (_wateringButtonText != null)
        {
            if (canWater)
            {
                var usedWaterings = PlantWateringController.MaximumWateringsPerDay
                    - _wateringController.GetRemainingWaterings(_plantState, now);
                _wateringButtonText.text =
                    $"\u6dcb\u6c34 +20%  {usedWaterings}/" +
                    PlantWateringController.MaximumWateringsPerDay;
            }
            else if (_plantState.hydration >= 0.9999f)
            {
                _wateringButtonText.text = "\u6c34\u5206\u5145\u8db3";
            }
            else
            {
                _wateringButtonText.text = demoMode
                    ? "\u672c\u8f6e\u5df2\u6dcb\u6c34 5/5"
                    : "\u4eca\u65e5\u5df2\u6dcb\u6c34 5/5";
            }
        }

        var canAddLight = _plantLightController.CanAddLightBoost(_plantState, now);
        if (_lightButton != null)
        {
            _lightButton.interactable = tracked && modelReady && canAddLight;
        }

        if (_lightButtonText != null)
        {
            if (!_plantState.lightEnvironmentSampled)
            {
                _lightButtonText.text = tracked
                    ? "\u6b63\u5728\u68c0\u6d4b\u5149\u7167"
                    : "\u7b49\u5f85\u5149\u7167\u68c0\u6d4b";
            }
            else if (canAddLight)
            {
                var usedBoosts = PlantLightController.MaximumBoostsPerDay
                    - _plantLightController.GetRemainingBoosts(_plantState, now);
                _lightButtonText.text =
                    $"\u8865\u5149 +5%  {usedBoosts}/" + PlantLightController.MaximumBoostsPerDay;
            }
            else if (_plantState.lightExposure >= 0.9999f)
            {
                _lightButtonText.text = "\u5149\u7167\u5145\u8db3";
            }
            else
            {
                _lightButtonText.text = demoMode
                    ? "\u672c\u8f6e\u5df2\u8865\u5149 6/6"
                    : "\u4eca\u65e5\u5df2\u8865\u5149 6/6";
            }
        }
    }

    private static Color GetLightColor(float lightExposure)
    {
        var low = new Color(0.72f, 0.42f, 0.16f, 1f);
        var neutral = new Color(0.94f, 0.72f, 0.2f, 1f);
        var bright = new Color(1f, 0.91f, 0.48f, 1f);
        var normalizedLight = Mathf.Clamp01(lightExposure);
        return normalizedLight <= PlantLightController.NeutralLight
            ? Color.Lerp(low, neutral, normalizedLight / PlantLightController.NeutralLight)
            : Color.Lerp(
                neutral,
                bright,
                (normalizedLight - PlantLightController.NeutralLight)
                    / (1f - PlantLightController.NeutralLight));
    }

    private static string GetLightEffectLabel(float lightExposure)
    {
        if (lightExposure < PlantLightController.NeutralLight - 0.005f)
        {
            return "\u751f\u957f\u53d7\u5230\u6291\u5236";
        }

        if (lightExposure > PlantLightController.NeutralLight + 0.005f)
        {
            return "\u751f\u957f\u83b7\u5f97\u4fc3\u8fdb";
        }

        return "\u4e2d\u6027\u5149\u7167";
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
                ShouldShowDroopTestControls
                    ? _requestedDroopLevel
                    : CalculateCurrentAppearanceDecay(),
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

        if (_experienceProfile != null
            && !ShouldShowDroopTestControls
            && _plantState != null)
        {
            _view.SetGrowthProgressImmediate(_plantState.displayedGrowthProgress);
            if (_plantState.growthProgress > _plantState.displayedGrowthProgress + 0.0001f)
            {
                StartAutomaticGrowthTransition(_plantState.growthProgress);
            }
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
        EnsureCareUi(existingCanvas.transform, LoadUiFont());
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
        var hasSeedButton = false;
        foreach (var button in existingCanvas.GetComponentsInChildren<Button>(true))
        {
            var stageName = ResolveButtonStageName(button);
            if (!string.IsNullOrEmpty(stageName))
            {
                hasSeedButton |= stageName == "Seed";
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

        var stageButtonRow = panel.Find("Buttons");
        if (!hasSeedButton && stageButtonRow != null)
        {
            var seedButton = CreateStageButton(stageButtonRow, LoadUiFont(), "\u79cd\u5b50", "Seed");
            seedButton.transform.SetSiblingIndex(0);
            _stageButtons.Insert(0, seedButton);
        }

        foreach (var button in _stageButtons)
        {
            var buttonRect = button != null ? button.GetComponent<RectTransform>() : null;
            if (buttonRect != null)
            {
                buttonRect.sizeDelta = new Vector2(90f, 52f);
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

    private static string GetStageName(int stageIndex)
    {
        return GrowthStageNames[Mathf.Clamp(
            stageIndex,
            PlantState.MinimumStageIndex,
            PlantState.MaximumStageIndex)];
    }

    private static int GetStageIndex(string stageName)
    {
        for (var index = 0; index < GrowthStageNames.Length; index++)
        {
            if (string.Equals(GrowthStageNames[index], stageName, System.StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static string GetStageLabel(string stageName)
    {
        switch (stageName)
        {
            case "Seed":
                return "\u79cd\u5b50";
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
        if (label.Contains("\u79cd\u5b50") || label.Contains("Seed"))
        {
            return "Seed";
        }

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
