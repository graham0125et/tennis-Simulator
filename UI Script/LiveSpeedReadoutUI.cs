using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LiveSpeedReadoutUI : MonoBehaviour
{
    private const float MetresPerSecondToMph = 2.23693629f;

    [Header("References")]
    public Canvas canvas;
    public Camera targetCamera;
    public Rigidbody ballRigidbody;
    public Transform player;
    public PlayerMovement playerMovement;
    public swipeMouseBall swipeSource;
    public hitController hitController;
    public ShotHeightUI shotHeightUI;

    [Header("Visibility")]
    public bool showBallSpeed = true;
    public bool showPlayerSpeed = true;
    public bool showTogglePanel = true;
    public bool showWorldSpeedText = false;
    public bool showBallGauge = true;
    public bool showPlayerGauge = true;
    [Tooltip("Nonessential speed/spin/shot metric UI refresh rate. Keeps text formatting and TMP rebuilds away from every frame.")]
    [Range(5f, 60f)] public float uiRefreshHz = 15f;

    [Header("Ball Gauge")]
    public Vector3 ballGaugeWorldOffset = new Vector3(0f, 0.22f, 0f);
    public Vector2 ballGaugeScreenOffset = new Vector2(0f, 0f);
    public Vector2 ballGaugeSize = new Vector2(38f, 24f);
    public float ballGaugeMaxMph = 80f;
    public float ballGaugeTrackThickness = 1.15f;
    public float ballGaugeLiveSectorThickness = 2.4f;
    public float ballGaugeLiveFillThickness = 8f;
    public float ballGaugeLaunchMarkerThickness = 1.4f;
    [HideInInspector]
    public float ballGaugeLiveMarkerThickness = 2.2f;
    public float launchResetBelowMph = 3f;
    public float launchStartAboveMph = 7f;
    public float launchRestartRiseMph = 6f;
    public float launchDirectionFlipDot = 0.1f;

    [Header("Player Gauge")]
    public Vector3 playerGaugeWorldOffset = new Vector3(0f, -0.05f, 0f);
    public Vector2 playerGaugeScreenOffset = new Vector2(0f, -8f);
    public Vector2 playerGaugeSize = new Vector2(38f, 24f);
    public float playerGaugeMaxMph = 16f;
    public float playerGaugeTrackThickness = 1.15f;
    public float playerGaugeLiveSectorThickness = 2.4f;
    public float playerGaugeLiveFillThickness = 8f;

    [Header("World Text Labels")]
    public Vector3 ballWorldOffset = new Vector3(0f, 0.45f, 0f);
    public Vector2 ballScreenOffset = Vector2.zero;
    public Vector3 playerWorldOffset = new Vector3(0f, 1.35f, 0f);
    public Vector2 playerScreenOffset = Vector2.zero;
    public float labelFontSize = 14f;
    public float labelOutlineWidth = 0.18f;

    [Header("Ball Speed Colour MPH")]
    public float ballOrangeMph = 25f;
    public float ballGreenMph = 55f;
    public float ballBlueMph = 90f;

    [Header("Player Speed Colour MPH")]
    public float playerOrangeMph = 1f;
    public float playerGreenMph = 7f;
    public float playerBlueMph = 14f;

    [Header("Colours")]
    public Color slowColour = new Color(1f, 0.55f, 0.16f, 1f);
    public Color moderateColour = new Color(0.15f, 1f, 0.25f, 1f);
    public Color fastColour = new Color(0.12f, 0.35f, 1f, 1f);
    public Color gaugeTrackColour = new Color(1f, 1f, 1f, 0.45f);
    public Color launchMarkerColour = new Color(1f, 1f, 1f, 0.9f);
    public Color panelColour = new Color(0.02f, 0.03f, 0.035f, 0.62f);
    public Color panelTextColour = new Color(0.9f, 0.95f, 1f, 1f);

    [Header("Right Panel")]
    public Vector2 panelSize = new Vector2(230f, 150f);
    public bool panelAnchorBottomRight = true;
    public Vector2 panelOffset = new Vector2(-22f, 22f);

    [Header("Shot Metrics Panel")]
    public bool showBallSpeedInShotMetricsPanel = true;
    public float shotMetricsSpeedRowSpacing = 22f;
    public float shotMetricsSpeedFontSize = 12f;
    public float playerTravelDistanceMetres = 24f;
    public float cannonTravelDistanceMetres = 23.77f;
    public float liveTimerStopDistanceMetres = 23.77f;
    public float travelEstimateStopSpeed = 0.75f;

    [Header("Shot Flight Debug Log")]
    public bool logShotFlightMetrics = false;
    public float shotFlightLogDistanceMetres = 23.77f;
    public float shotFlightPreBounceSampleBottomHeightMetres = 0.20f;
    public float shotFlightBallRadiusMetres = 0.033f;

    private RectTransform canvasRect;
    private TextMeshProUGUI ballLabel;
    private TextMeshProUGUI playerLabel;
    private RectTransform ballGaugeRect;
    private LiveSpeedGaugeGraphic ballGaugeGraphic;
    private RectTransform playerGaugeRect;
    private LiveSpeedGaugeGraphic playerGaugeGraphic;
    private RectTransform panelRect;
    private Toggle ballToggle;
    private Toggle playerToggle;
    private TextMeshProUGUI liveLabelText;
    private TextMeshProUGUI ballValueText;
    private TextMeshProUGUI launchLabelText;
    private TextMeshProUGUI launchValueText;
    private TextMeshProUGUI averageRallyLabelText;
    private TextMeshProUGUI averageRallyValueText;
    private Button averageRallyResetButton;
    private TextMeshProUGUI playerValueText;
    private TextMeshProUGUI shotPanelLiveLabelText;
    private TextMeshProUGUI shotPanelLiveValueText;
    private TextMeshProUGUI shotPanelLaunchLabelText;
    private TextMeshProUGUI shotPanelLaunchValueText;
    private TextMeshProUGUI shotPanelTravelLabelText;
    private TextMeshProUGUI shotPanelTravelValueText;
    private TextMeshProUGUI shotPanelLiveTimerLabelText;
    private TextMeshProUGUI shotPanelLiveTimerValueText;
    private TextMeshProUGUI shotPanelSpeedRetentionLabelText;
    private TextMeshProUGUI shotPanelSpeedRetentionValueText;
    private TextMeshProUGUI shotPanelAverageRallyLabelText;
    private TextMeshProUGUI shotPanelAverageRallyValueText;
    private Button shotPanelAverageRallyResetButton;
    private TextMeshProUGUI shotPanelBackswingCapLabelText;
    private TextMeshProUGUI shotPanelRetainedCapLabelText;
    private TextMeshProUGUI shotPanelSolverClearanceLabelText;
    private TextMeshProUGUI shotPanelActualClearanceLabelText;
    private TextMeshProUGUI fontTemplate;

    private Vector3 lastPlayerPosition;
    private bool hasLastPlayerPosition;
    private float fallbackPlayerSpeedMps;
    private float nextReferenceRefreshTime;
    private float nextUiRefreshTime;
    private float trackedLaunchSpeedMph;
    private float averageRallySpeedSumMph;
    private int averageRallySpeedCount;
    private bool launchTrackingActive;
    private Vector3 previousBallVelocity;
    private bool hasPreviousBallVelocity;
    private bool travelTrackingActive;
    private bool travelTimeMeasured;
    private bool travelTimeEstimated = true;
    private float travelDisplaySeconds;
    private float travelStartTime;
    private float travelTargetDistanceMetres;
    private Vector3 travelStartPosition;
    private Vector3 travelDirection;
    private bool liveTimerActive;
    private bool liveTimerHasValue;
    private float liveTimerSeconds;
    private float liveTimerStartTime;
    private Vector3 liveTimerStartPosition;
    private Vector3 liveTimerDirection;
    private bool speedRetentionActive;
    private bool speedRetentionHasValue;
    private bool speedRetentionMeasured;
    private float speedRetentionPercent;
    private float speedRetentionLaunchSpeed;
    private float speedRetentionPreviousDistance;
    private float speedRetentionPreviousSpeed;
    private bool shotFlightLogActive;
    private bool shotFlightDistanceLogged;
    private bool shotFlightBounceLogged;
    private bool shotFlightLogPrinted;
    private string shotFlightSource = "shot";
    private string shotFlightBounceCourtName = "court";
    private float shotFlightLaunchTime;
    private float shotFlightLaunchSpeed;
    private float shotFlightDistanceSeconds;
    private float shotFlightDistanceSpeed;
    private float shotFlightBounceInSpeed;
    private float shotFlightBounceOutSpeed;
    private float shotFlightBounceTime;
    private float shotFlightBounceDistance;
    private bool shotFlightHasLowPreBounceSample;
    private float shotFlightLowPreBounceSpeed;
    private float shotFlightLowPreBounceTime;
    private float shotFlightLowPreBounceBottomHeight;
    private float shotFlightCollisionPreBounceSpeed;
    private string shotFlightPreBounceSource = "collision";
    private Vector3 shotFlightStartPosition;
    private Vector3 shotFlightDirection;
    private float shotFlightPreviousDistance;
    private float shotFlightPreviousSpeed;

    void Awake()
    {
        ResolveReferences(true);
        BuildUi();
    }

    void OnEnable()
    {
        hitController.PlayerBallLaunched += HandlePlayerBallLaunched;
        ballCannon.CannonBallLaunched += HandleCannonBallLaunched;
        BallController.CourtBounceApplied += HandleCourtBounceApplied;
        ApplyToggleState();
    }

    void OnDisable()
    {
        hitController.PlayerBallLaunched -= HandlePlayerBallLaunched;
        ballCannon.CannonBallLaunched -= HandleCannonBallLaunched;
        BallController.CourtBounceApplied -= HandleCourtBounceApplied;
    }

    void Update()
    {
        if (Time.unscaledTime >= nextReferenceRefreshTime)
        {
            ResolveReferences(false);
            nextReferenceRefreshTime = Time.unscaledTime + 0.25f;
        }

        float ballMph = GetBallSpeedMph();
        float playerMph = GetPlayerSpeedMph();
        UpdateLaunchSpeed(ballMph);
        UpdateTravelTime();
        UpdateLiveTimer();
        UpdateSpeedRetentionAtDistance();
        UpdateShotFlightDebugLogDistance();
        UpdateShotFlightPreBounceSample();

        UpdateBallGauge(ballMph);
        UpdatePlayerGauge(playerMph);

        UpdateWorldLabel(
            ballLabel,
            ballRigidbody != null ? ballRigidbody.transform : null,
            ballWorldOffset,
            ballScreenOffset,
            showWorldSpeedText && showBallSpeed,
            ballMph,
            ballOrangeMph,
            ballGreenMph,
            ballBlueMph);

        UpdateWorldLabel(
            playerLabel,
            player,
            playerWorldOffset,
            playerScreenOffset,
            showWorldSpeedText && showPlayerSpeed,
            playerMph,
            playerOrangeMph,
            playerGreenMph,
            playerBlueMph);

        float refreshInterval = 1f / Mathf.Max(1f, uiRefreshHz);
        if (Time.unscaledTime >= nextUiRefreshTime)
        {
            nextUiRefreshTime = Time.unscaledTime + refreshInterval;
            UpdateShotMetricsSpeedRows(ballMph);
            UpdatePanelValues(ballMph, playerMph);
        }

        if (panelRect != null && panelRect.gameObject.activeSelf != showTogglePanel)
            panelRect.gameObject.SetActive(showTogglePanel);
    }

    private void ResolveReferences(bool force)
    {
        if (canvas == null)
            canvas = GetComponentInParent<Canvas>();

        if (canvas == null)
            canvas = FindFirstObjectByType<Canvas>();

        if (canvas != null)
            canvasRect = canvas.GetComponent<RectTransform>();

        if (targetCamera == null)
            targetCamera = Camera.main;

        if (playerMovement == null)
            playerMovement = FindFirstObjectByType<PlayerMovement>();

        if (player == null && playerMovement != null)
            player = playerMovement.transform;

        if (swipeSource == null)
            swipeSource = FindFirstObjectByType<swipeMouseBall>();

        if (hitController == null)
            hitController = FindFirstObjectByType<hitController>();

        if (shotHeightUI == null)
            shotHeightUI = FindFirstObjectByType<ShotHeightUI>();

        if (force || ballRigidbody == null || !ballRigidbody.gameObject.activeInHierarchy)
            ballRigidbody = FindBallRigidbody();
    }

    private Rigidbody FindBallRigidbody()
    {
        if (swipeSource != null && swipeSource.ball != null)
            return swipeSource.ball;

        if (hitController != null && hitController.ball != null)
            return hitController.ball;

        BallController controller = FindFirstObjectByType<BallController>();
        if (controller != null)
        {
            Rigidbody controllerBody = controller.GetComponent<Rigidbody>();
            if (controllerBody != null)
                return controllerBody;
        }

        Rigidbody[] bodies = FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);
        for (int i = 0; i < bodies.Length; i++)
        {
            if (bodies[i] != null && bodies[i].name.Contains("tennisBall"))
                return bodies[i];
        }

        return null;
    }

    private void BuildUi()
    {
        if (canvasRect == null)
            return;

        fontTemplate = FindFirstObjectByType<TextMeshProUGUI>();

        ballGaugeGraphic = CreateBallGauge();
        playerGaugeGraphic = CreatePlayerGauge();
        ballLabel = CreateWorldLabel("LiveBallSpeedLabel");
        playerLabel = CreateWorldLabel("LivePlayerSpeedLabel");
        panelRect = CreateTogglePanel();
        EnsureShotMetricsSpeedRows();
        ApplyToggleState();
    }

    private LiveSpeedGaugeGraphic CreateBallGauge()
    {
        GameObject gaugeObject = new GameObject("LiveBallSpeedGauge", typeof(RectTransform), typeof(CanvasRenderer), typeof(LiveSpeedGaugeGraphic));
        gaugeObject.transform.SetParent(canvasRect, false);
        gaugeObject.layer = canvas.gameObject.layer;

        ballGaugeRect = gaugeObject.GetComponent<RectTransform>();
        ballGaugeRect.anchorMin = new Vector2(0.5f, 0.5f);
        ballGaugeRect.anchorMax = new Vector2(0.5f, 0.5f);
        ballGaugeRect.pivot = new Vector2(0.5f, 0.05f);
        ballGaugeRect.sizeDelta = ballGaugeSize;

        LiveSpeedGaugeGraphic gauge = gaugeObject.GetComponent<LiveSpeedGaugeGraphic>();
        gauge.raycastTarget = false;
        gauge.trackColour = gaugeTrackColour;
        gauge.launchMarkerColour = launchMarkerColour;
        gauge.slowColour = slowColour;
        gauge.moderateColour = moderateColour;
        gauge.fastColour = fastColour;
        gauge.orangeMph = ballOrangeMph;
        gauge.greenMph = ballGreenMph;
        gauge.blueMph = ballBlueMph;
        gauge.SetSpeeds(0f, 0f, Mathf.Max(1f, ballGaugeMaxMph));
        gaugeObject.SetActive(false);
        return gauge;
    }

    private LiveSpeedGaugeGraphic CreatePlayerGauge()
    {
        GameObject gaugeObject = new GameObject("LivePlayerSpeedGauge", typeof(RectTransform), typeof(CanvasRenderer), typeof(LiveSpeedGaugeGraphic));
        gaugeObject.transform.SetParent(canvasRect, false);
        gaugeObject.layer = canvas.gameObject.layer;

        playerGaugeRect = gaugeObject.GetComponent<RectTransform>();
        playerGaugeRect.anchorMin = new Vector2(0.5f, 0.5f);
        playerGaugeRect.anchorMax = new Vector2(0.5f, 0.5f);
        playerGaugeRect.pivot = new Vector2(0.5f, 0.95f);
        playerGaugeRect.sizeDelta = playerGaugeSize;

        LiveSpeedGaugeGraphic gauge = gaugeObject.GetComponent<LiveSpeedGaugeGraphic>();
        gauge.raycastTarget = false;
        gauge.trackColour = gaugeTrackColour;
        gauge.showLaunchMarker = false;
        gauge.slowColour = slowColour;
        gauge.moderateColour = moderateColour;
        gauge.fastColour = fastColour;
        gauge.orangeMph = playerOrangeMph;
        gauge.greenMph = playerGreenMph;
        gauge.blueMph = playerBlueMph;
        gauge.SetSpeeds(0f, 0f, Mathf.Max(1f, playerGaugeMaxMph));
        gaugeObject.SetActive(false);
        return gauge;
    }

    private TextMeshProUGUI CreateWorldLabel(string labelName)
    {
        GameObject labelObject = new GameObject(labelName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(canvasRect, false);
        labelObject.layer = canvas.gameObject.layer;

        RectTransform rect = labelObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(120f, 30f);

        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        ConfigureText(label, labelFontSize, TextAlignmentOptions.Center, true);
        label.raycastTarget = false;
        label.gameObject.SetActive(false);
        return label;
    }

    private RectTransform CreateTogglePanel()
    {
        GameObject panelObject = new GameObject("LiveSpeedReadoutPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panelObject.transform.SetParent(canvasRect, false);
        panelObject.layer = canvas.gameObject.layer;

        RectTransform rect = panelObject.GetComponent<RectTransform>();
        rect.anchorMin = panelAnchorBottomRight ? new Vector2(1f, 0f) : new Vector2(1f, 1f);
        rect.anchorMax = panelAnchorBottomRight ? new Vector2(1f, 0f) : new Vector2(1f, 1f);
        rect.pivot = panelAnchorBottomRight ? new Vector2(1f, 0f) : new Vector2(1f, 1f);
        rect.anchoredPosition = panelOffset;
        rect.sizeDelta = new Vector2(Mathf.Max(230f, panelSize.x), Mathf.Max(176f, panelSize.y));

        Image background = panelObject.GetComponent<Image>();
        background.color = panelColour;
        background.raycastTarget = true;

        TextMeshProUGUI title = CreatePanelText(rect, "Ball Speed", new Vector2(12f, -8f), new Vector2(-24f, 22f), 12f, TextAlignmentOptions.Left);
        title.color = panelTextColour;

        ballToggle = CreateToggle(rect, "Ball", new Vector2(12f, -34f), showBallSpeed);
        playerToggle = CreateToggle(rect, "Player", new Vector2(12f, -60f), showPlayerSpeed);

        liveLabelText = CreatePanelText(rect, "Live", new Vector2(12f, -86f), new Vector2(70f, 22f), 11f, TextAlignmentOptions.Left);
        liveLabelText.color = panelTextColour;
        ballValueText = CreatePanelText(rect, "0 MPH", new Vector2(-100f, -86f), new Vector2(76f, 22f), 11f, TextAlignmentOptions.Right);

        launchLabelText = CreatePanelText(rect, "Launch", new Vector2(12f, -112f), new Vector2(70f, 22f), 11f, TextAlignmentOptions.Left);
        launchLabelText.color = panelTextColour;
        launchValueText = CreatePanelText(rect, "0 MPH", new Vector2(-100f, -112f), new Vector2(76f, 22f), 11f, TextAlignmentOptions.Right);
        averageRallyLabelText = CreatePanelText(rect, "Avg Rally", new Vector2(12f, -138f), new Vector2(76f, 22f), 11f, TextAlignmentOptions.Left);
        averageRallyLabelText.color = panelTextColour;
        averageRallyValueText = CreatePanelText(rect, "--", new Vector2(-100f, -138f), new Vector2(76f, 22f), 11f, TextAlignmentOptions.Right);
        averageRallyResetButton = CreateSmallPanelButton(rect, "Reset", new Vector2(-18f, -138f), new Vector2(54f, 20f));
        playerValueText = CreatePanelText(rect, "0 MPH", new Vector2(-100f, -60f), new Vector2(76f, 22f), 11f, TextAlignmentOptions.Right);

        ballToggle.onValueChanged.AddListener(value => showBallSpeed = value);
        playerToggle.onValueChanged.AddListener(value => showPlayerSpeed = value);
        if (averageRallyResetButton != null)
            averageRallyResetButton.onClick.AddListener(ResetAverageRallySpeed);
        panelObject.SetActive(showTogglePanel);
        return rect;
    }

    private Button CreateSmallPanelButton(RectTransform parent, string label, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject buttonObject = new GameObject(label + " Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        buttonObject.layer = parent.gameObject.layer;

        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(1f, 1f);
        buttonRect.anchorMax = new Vector2(1f, 1f);
        buttonRect.pivot = new Vector2(1f, 1f);
        buttonRect.anchoredPosition = anchoredPosition;
        buttonRect.sizeDelta = size;

        Image background = buttonObject.GetComponent<Image>();
        background.color = new Color(1f, 1f, 1f, 0.16f);

        Button button = buttonObject.GetComponent<Button>();
        ColorBlock colours = button.colors;
        colours.normalColor = new Color(1f, 1f, 1f, 0.16f);
        colours.highlightedColor = new Color(1f, 1f, 1f, 0.28f);
        colours.pressedColor = new Color(0.65f, 0.9f, 1f, 0.38f);
        colours.selectedColor = colours.highlightedColor;
        colours.disabledColor = new Color(1f, 1f, 1f, 0.08f);
        button.colors = colours;

        TextMeshProUGUI text = CreatePanelText(buttonRect, label, Vector2.zero, Vector2.zero, 9.5f, TextAlignmentOptions.Center);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = Vector2.zero;
        text.rectTransform.offsetMax = Vector2.zero;
        text.color = panelTextColour;
        text.raycastTarget = false;
        return button;
    }

    private Toggle CreateToggle(RectTransform parent, string label, Vector2 anchoredPosition, bool isOn)
    {
        GameObject rowObject = new GameObject(label + " Row", typeof(RectTransform));
        rowObject.transform.SetParent(parent, false);
        rowObject.layer = parent.gameObject.layer;

        RectTransform rowRect = rowObject.GetComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0f, 1f);
        rowRect.anchorMax = new Vector2(1f, 1f);
        rowRect.pivot = new Vector2(0f, 1f);
        rowRect.anchoredPosition = anchoredPosition;
        rowRect.sizeDelta = new Vector2(-24f, 22f);

        GameObject toggleObject = new GameObject(label + " Toggle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Toggle));
        toggleObject.transform.SetParent(rowRect, false);
        toggleObject.layer = parent.gameObject.layer;

        RectTransform toggleRect = toggleObject.GetComponent<RectTransform>();
        toggleRect.anchorMin = new Vector2(0f, 0.5f);
        toggleRect.anchorMax = new Vector2(0f, 0.5f);
        toggleRect.pivot = new Vector2(0f, 0.5f);
        toggleRect.anchoredPosition = Vector2.zero;
        toggleRect.sizeDelta = new Vector2(18f, 18f);

        Image background = toggleObject.GetComponent<Image>();
        background.color = new Color(1f, 1f, 1f, 0.22f);

        GameObject checkObject = new GameObject("Checkmark", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        checkObject.transform.SetParent(toggleRect, false);
        checkObject.layer = parent.gameObject.layer;

        RectTransform checkRect = checkObject.GetComponent<RectTransform>();
        checkRect.anchorMin = new Vector2(0.5f, 0.5f);
        checkRect.anchorMax = new Vector2(0.5f, 0.5f);
        checkRect.pivot = new Vector2(0.5f, 0.5f);
        checkRect.anchoredPosition = Vector2.zero;
        checkRect.sizeDelta = new Vector2(11f, 11f);

        Image checkImage = checkObject.GetComponent<Image>();
        checkImage.color = moderateColour;

        Toggle toggle = toggleObject.GetComponent<Toggle>();
        toggle.targetGraphic = background;
        toggle.graphic = checkImage;
        toggle.isOn = isOn;

        TextMeshProUGUI labelText = CreatePanelText(rowRect, label, new Vector2(26f, -1f), new Vector2(-26f, 22f), 11f, TextAlignmentOptions.Left);
        labelText.color = panelTextColour;

        return toggle;
    }

    private TextMeshProUGUI CreatePanelText(RectTransform parent, string text, Vector2 anchoredPosition, Vector2 size, float fontSize, TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject(text + " Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        textObject.layer = parent.gameObject.layer;

        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        TextMeshProUGUI label = textObject.GetComponent<TextMeshProUGUI>();
        ConfigureText(label, fontSize, alignment, false);
        label.text = text;
        return label;
    }

    private void ConfigureText(TextMeshProUGUI label, float fontSize, TextAlignmentOptions alignment, bool withOutline)
    {
        label.text = string.Empty;
        label.fontSize = fontSize;
        label.fontStyle = FontStyles.Bold;
        label.alignment = alignment;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;

        if (fontTemplate != null && fontTemplate.font != null)
        {
            label.font = fontTemplate.font;
            label.fontSharedMaterial = fontTemplate.fontSharedMaterial;
        }

        if (withOutline)
        {
            label.outlineWidth = labelOutlineWidth;
            label.outlineColor = new Color32(0, 0, 0, 220);
        }
    }

    private void ApplyToggleState()
    {
        if (ballToggle != null)
            ballToggle.SetIsOnWithoutNotify(showBallSpeed);

        if (playerToggle != null)
            playerToggle.SetIsOnWithoutNotify(showPlayerSpeed);

        if (panelRect != null)
            panelRect.gameObject.SetActive(showTogglePanel);
    }

    private float GetBallSpeedMph()
    {
        if (ballRigidbody == null)
            return 0f;

        return ballRigidbody.linearVelocity.magnitude * MetresPerSecondToMph;
    }

    private float GetPlayerSpeedMph()
    {
        return GetPlayerSpeedMps() * MetresPerSecondToMph;
    }

    private float GetPlayerSpeedMps()
    {
        if (playerMovement != null)
            return playerMovement.CurrentSpeed;

        if (player == null)
        {
            hasLastPlayerPosition = false;
            fallbackPlayerSpeedMps = 0f;
            return 0f;
        }

        if (!hasLastPlayerPosition)
        {
            lastPlayerPosition = player.position;
            hasLastPlayerPosition = true;
            fallbackPlayerSpeedMps = 0f;
            return fallbackPlayerSpeedMps;
        }

        float dt = Time.deltaTime;
        if (dt > 0f)
        {
            Vector3 delta = player.position - lastPlayerPosition;
            delta.y = 0f;
            fallbackPlayerSpeedMps = delta.magnitude / dt;
        }

        lastPlayerPosition = player.position;
        return fallbackPlayerSpeedMps;
    }

    private void UpdateLaunchSpeed(float ballMph)
    {
        Vector3 currentVelocity = ballRigidbody != null ? ballRigidbody.linearVelocity : Vector3.zero;
        bool restartedBySpeedJump = launchTrackingActive && ballMph - (previousBallVelocity.magnitude * MetresPerSecondToMph) >= launchRestartRiseMph;
        bool restartedByReturnDirection = launchTrackingActive && HasHorizontalDirectionFlip(previousBallVelocity, currentVelocity);

        if (ballMph < launchResetBelowMph)
        {
            launchTrackingActive = false;
            trackedLaunchSpeedMph = 0f;
            hasPreviousBallVelocity = true;
            previousBallVelocity = currentVelocity;
            return;
        }

        if ((!launchTrackingActive && ballMph >= launchStartAboveMph) || restartedBySpeedJump || restartedByReturnDirection)
        {
            launchTrackingActive = true;
            trackedLaunchSpeedMph = ballMph;
            hasPreviousBallVelocity = true;
            previousBallVelocity = currentVelocity;
            return;
        }

        if (launchTrackingActive && ballMph > trackedLaunchSpeedMph)
            trackedLaunchSpeedMph = ballMph;

        hasPreviousBallVelocity = true;
        previousBallVelocity = currentVelocity;
    }

    private bool HasHorizontalDirectionFlip(Vector3 previousVelocity, Vector3 currentVelocity)
    {
        if (!hasPreviousBallVelocity)
            return false;

        Vector3 previousFlat = previousVelocity;
        Vector3 currentFlat = currentVelocity;
        previousFlat.y = 0f;
        currentFlat.y = 0f;

        if (previousFlat.sqrMagnitude < 0.25f || currentFlat.sqrMagnitude < 0.25f)
            return false;

        float dot = Vector3.Dot(previousFlat.normalized, currentFlat.normalized);
        return dot <= launchDirectionFlipDot && currentVelocity.magnitude * MetresPerSecondToMph >= launchStartAboveMph;
    }

    private void UpdateBallGauge(float ballMph)
    {
        if (ballGaugeGraphic == null || ballGaugeRect == null)
            return;

        Transform ballTransform = ballRigidbody != null ? ballRigidbody.transform : null;
        bool shouldShow = showBallGauge && showBallSpeed && ballTransform != null && targetCamera != null && canvasRect != null;
        if (!shouldShow)
        {
            ballGaugeGraphic.gameObject.SetActive(false);
            return;
        }

        Vector3 screenPosition = targetCamera.WorldToScreenPoint(ballTransform.position + ballGaugeWorldOffset);
        if (screenPosition.z <= 0f)
        {
            ballGaugeGraphic.gameObject.SetActive(false);
            return;
        }

        Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? targetCamera : null;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPosition, uiCamera, out Vector2 anchoredPosition))
        {
            ballGaugeGraphic.gameObject.SetActive(false);
            return;
        }

        ballGaugeRect.sizeDelta = ballGaugeSize;
        ballGaugeRect.anchoredPosition = anchoredPosition + ballGaugeScreenOffset;
        ballGaugeGraphic.trackColour = gaugeTrackColour;
        ballGaugeGraphic.launchMarkerColour = launchMarkerColour;
        ballGaugeGraphic.slowColour = slowColour;
        ballGaugeGraphic.moderateColour = moderateColour;
        ballGaugeGraphic.fastColour = fastColour;
        ballGaugeGraphic.orangeMph = ballOrangeMph;
        ballGaugeGraphic.greenMph = ballGreenMph;
        ballGaugeGraphic.blueMph = ballBlueMph;
        ballGaugeGraphic.trackThickness = ballGaugeTrackThickness;
        ballGaugeGraphic.liveSectorThickness = ballGaugeLiveSectorThickness;
        ballGaugeGraphic.liveFillThickness = ballGaugeLiveFillThickness;
        ballGaugeGraphic.launchMarkerThickness = ballGaugeLaunchMarkerThickness;
        ballGaugeGraphic.SetSpeeds(ballMph, trackedLaunchSpeedMph, Mathf.Max(1f, ballGaugeMaxMph));

        if (!ballGaugeGraphic.gameObject.activeSelf)
            ballGaugeGraphic.gameObject.SetActive(true);
    }

    private void UpdatePlayerGauge(float playerMph)
    {
        if (playerGaugeGraphic == null || playerGaugeRect == null)
            return;

        bool shouldShow = showPlayerGauge && showPlayerSpeed && player != null && targetCamera != null && canvasRect != null;
        if (!shouldShow)
        {
            playerGaugeGraphic.gameObject.SetActive(false);
            return;
        }

        Vector3 screenPosition = targetCamera.WorldToScreenPoint(player.position + playerGaugeWorldOffset);
        if (screenPosition.z <= 0f)
        {
            playerGaugeGraphic.gameObject.SetActive(false);
            return;
        }

        Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? targetCamera : null;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPosition, uiCamera, out Vector2 anchoredPosition))
        {
            playerGaugeGraphic.gameObject.SetActive(false);
            return;
        }

        float movementMaxMph = playerMovement != null
            ? Mathf.Max(1f, playerMovement.moveSpeed * MetresPerSecondToMph)
            : Mathf.Max(1f, playerGaugeMaxMph);
        float gaugeMaxMph = Mathf.Max(1f, playerGaugeMaxMph, movementMaxMph);

        playerGaugeRect.sizeDelta = playerGaugeSize;
        playerGaugeRect.anchoredPosition = anchoredPosition + playerGaugeScreenOffset;
        playerGaugeGraphic.trackColour = gaugeTrackColour;
        playerGaugeGraphic.showLaunchMarker = false;
        playerGaugeGraphic.slowColour = slowColour;
        playerGaugeGraphic.moderateColour = moderateColour;
        playerGaugeGraphic.fastColour = fastColour;
        playerGaugeGraphic.orangeMph = playerOrangeMph;
        playerGaugeGraphic.greenMph = playerGreenMph;
        playerGaugeGraphic.blueMph = playerBlueMph;
        playerGaugeGraphic.trackThickness = playerGaugeTrackThickness;
        playerGaugeGraphic.liveSectorThickness = playerGaugeLiveSectorThickness;
        playerGaugeGraphic.liveFillThickness = playerGaugeLiveFillThickness;
        playerGaugeGraphic.SetSpeeds(playerMph, 0f, gaugeMaxMph);

        if (!playerGaugeGraphic.gameObject.activeSelf)
            playerGaugeGraphic.gameObject.SetActive(true);
    }

    private void UpdateWorldLabel(
        TextMeshProUGUI label,
        Transform target,
        Vector3 worldOffset,
        Vector2 screenOffset,
        bool visible,
        float speedMph,
        float orangeMph,
        float greenMph,
        float blueMph)
    {
        if (label == null)
            return;

        bool shouldShow = visible && target != null && targetCamera != null && canvasRect != null;
        if (!shouldShow)
        {
            label.gameObject.SetActive(false);
            return;
        }

        Vector3 screenPosition = targetCamera.WorldToScreenPoint(target.position + worldOffset);
        if (screenPosition.z <= 0f)
        {
            label.gameObject.SetActive(false);
            return;
        }

        Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? targetCamera : null;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPosition, uiCamera, out Vector2 anchoredPosition))
        {
            label.gameObject.SetActive(false);
            return;
        }

        RectTransform labelRect = label.rectTransform;
        labelRect.anchoredPosition = anchoredPosition + screenOffset;
        SetTextIfChanged(label, $"{speedMph:0} MPH");
        label.color = GradeSpeedColour(speedMph, orangeMph, greenMph, blueMph);

        if (!label.gameObject.activeSelf)
            label.gameObject.SetActive(true);
    }

    private void UpdatePanelValues(float ballMph, float playerMph)
    {
        if (ballValueText != null)
        {
            SetTextIfChanged(ballValueText, showBallSpeed ? $"{ballMph:0} MPH" : "OFF");
            ballValueText.color = showBallSpeed ? GradeSpeedColour(ballMph, ballOrangeMph, ballGreenMph, ballBlueMph) : new Color(1f, 1f, 1f, 0.35f);
        }

        if (launchValueText != null)
        {
            SetTextIfChanged(launchValueText, showBallSpeed ? $"{trackedLaunchSpeedMph:0} MPH" : "OFF");
            launchValueText.color = showBallSpeed ? GradeSpeedColour(trackedLaunchSpeedMph, ballOrangeMph, ballGreenMph, ballBlueMph) : new Color(1f, 1f, 1f, 0.35f);
        }

        if (averageRallyValueText != null)
        {
            SetTextIfChanged(averageRallyValueText, showBallSpeed ? FormatAverageRallySpeed() : "OFF");
            float averageMph = averageRallySpeedCount > 0 ? averageRallySpeedSumMph / averageRallySpeedCount : 0f;
            averageRallyValueText.color = showBallSpeed && averageRallySpeedCount > 0
                ? GradeSpeedColour(averageMph, ballOrangeMph, ballGreenMph, ballBlueMph)
                : new Color(1f, 1f, 1f, 0.35f);
        }

        if (playerValueText != null)
        {
            SetTextIfChanged(playerValueText, showPlayerSpeed ? $"{playerMph:0} MPH" : "OFF");
            playerValueText.color = showPlayerSpeed ? GradeSpeedColour(playerMph, playerOrangeMph, playerGreenMph, playerBlueMph) : new Color(1f, 1f, 1f, 0.35f);
        }
    }

    private void EnsureShotMetricsSpeedRows()
    {
        if (shotHeightUI == null)
            return;

        RectTransform parent = shotHeightUI.transform as RectTransform;
        if (parent == null)
            return;

        RectTransform actualLabelRect = FindChildRect(parent, "ActualHeightLabel");
        RectTransform actualValueRect = shotHeightUI.actualHeightAngleValue != null
            ? shotHeightUI.actualHeightAngleValue.rectTransform
            : null;

        TextMeshProUGUI labelTemplate = actualLabelRect != null ? actualLabelRect.GetComponent<TextMeshProUGUI>() : fontTemplate;
        TextMeshProUGUI valueTemplate = shotHeightUI.actualHeightAngleValue != null ? shotHeightUI.actualHeightAngleValue : fontTemplate;

        float rowSpacing = Mathf.Max(10f, shotMetricsSpeedRowSpacing);
        float liveY = actualLabelRect != null ? actualLabelRect.anchoredPosition.y - rowSpacing : -84.5f;
        float launchY = liveY - rowSpacing;
        float travelY = launchY - rowSpacing;
        float liveTimerY = travelY - rowSpacing;
        float speedRetentionY = liveTimerY - rowSpacing;
        float averageRallyY = speedRetentionY - rowSpacing;

        Vector2 labelPosition = actualLabelRect != null ? actualLabelRect.anchoredPosition : new Vector2(0f, liveY);
        Vector2 valuePosition = actualValueRect != null ? actualValueRect.anchoredPosition : new Vector2(106.5f, liveY);
        Vector2 labelSize = actualLabelRect != null ? actualLabelRect.sizeDelta : new Vector2(200f, 50f);
        Vector2 valueSize = actualValueRect != null ? actualValueRect.sizeDelta : new Vector2(200f, 50f);

        RectTransform lateralLabelRect = FindChildRect(parent, "LateralAimLabel");
        RectTransform lateralValueRect = shotHeightUI.lateralAimValue != null
            ? shotHeightUI.lateralAimValue.rectTransform
            : FindChildRect(parent, "LaterAimValue");

        if (lateralLabelRect != null)
        {
            Vector2 clearanceLabelPosition = lateralLabelRect.anchoredPosition;
            Vector2 clearanceValuePosition = lateralValueRect != null ? lateralValueRect.anchoredPosition : valuePosition;
            Vector2 clearanceLabelSize = lateralLabelRect.sizeDelta;
            Vector2 clearanceValueSize = lateralValueRect != null ? lateralValueRect.sizeDelta : valueSize;

            clearanceLabelPosition.y += rowSpacing * 4f;
            clearanceValuePosition.y = clearanceLabelPosition.y;
            shotPanelBackswingCapLabelText = CreateShotMetricsText(parent, "BackswingCapSpeedLabel", "Backswing Cap", clearanceLabelPosition, clearanceLabelSize, labelTemplate, TextAlignmentOptions.Center);
            shotHeightUI.backswingCapSpeedValue = CreateShotMetricsText(parent, "BackswingCapSpeedValue", "--", clearanceValuePosition, clearanceValueSize, valueTemplate, TextAlignmentOptions.Center);

            clearanceLabelPosition = lateralLabelRect.anchoredPosition;
            clearanceValuePosition = lateralValueRect != null ? lateralValueRect.anchoredPosition : valuePosition;
            clearanceLabelPosition.y += rowSpacing * 3f;
            clearanceValuePosition.y = clearanceLabelPosition.y;
            shotPanelRetainedCapLabelText = CreateShotMetricsText(parent, "RetainedCapSpeedLabel", "Retained Cap", clearanceLabelPosition, clearanceLabelSize, labelTemplate, TextAlignmentOptions.Center);
            shotHeightUI.retainedCapSpeedValue = CreateShotMetricsText(parent, "RetainedCapSpeedValue", "--", clearanceValuePosition, clearanceValueSize, valueTemplate, TextAlignmentOptions.Center);

            clearanceLabelPosition = lateralLabelRect.anchoredPosition;
            clearanceValuePosition = lateralValueRect != null ? lateralValueRect.anchoredPosition : valuePosition;
            clearanceLabelPosition.y += rowSpacing * 2f;
            clearanceValuePosition.y = clearanceLabelPosition.y;
            shotPanelSolverClearanceLabelText = CreateShotMetricsText(parent, "SolverNetClearanceLabel", "Solver Net Clear", clearanceLabelPosition, clearanceLabelSize, labelTemplate, TextAlignmentOptions.Center);
            shotHeightUI.solverNetClearanceValue = CreateShotMetricsText(parent, "SolverNetClearanceValue", "--", clearanceValuePosition, clearanceValueSize, valueTemplate, TextAlignmentOptions.Center);

            clearanceLabelPosition = lateralLabelRect.anchoredPosition;
            clearanceValuePosition = lateralValueRect != null ? lateralValueRect.anchoredPosition : valuePosition;
            clearanceLabelPosition.y += rowSpacing;
            clearanceValuePosition.y = clearanceLabelPosition.y;
            shotPanelActualClearanceLabelText = CreateShotMetricsText(parent, "ActualNetClearanceLabel", "Actual Net Clear", clearanceLabelPosition, clearanceLabelSize, labelTemplate, TextAlignmentOptions.Center);
            shotHeightUI.actualNetClearanceValue = CreateShotMetricsText(parent, "ActualNetClearanceValue", "--", clearanceValuePosition, clearanceValueSize, valueTemplate, TextAlignmentOptions.Center);
        }

        labelPosition.y = liveY;
        valuePosition.y = liveY;
        shotPanelLiveLabelText = CreateShotMetricsText(parent, "LiveBallSpeedLabel", "Live Ball Speed", labelPosition, labelSize, labelTemplate, TextAlignmentOptions.Center);
        shotPanelLiveValueText = CreateShotMetricsText(parent, "LiveBallSpeedValue", "0 MPH", valuePosition, valueSize, valueTemplate, TextAlignmentOptions.Center);

        labelPosition.y = launchY;
        valuePosition.y = launchY;
        shotPanelLaunchLabelText = CreateShotMetricsText(parent, "BallLaunchSpeedLabel", "Ball Launch Speed", labelPosition, labelSize, labelTemplate, TextAlignmentOptions.Center);
        shotPanelLaunchValueText = CreateShotMetricsText(parent, "BallLaunchSpeedValue", "0 MPH", valuePosition, valueSize, valueTemplate, TextAlignmentOptions.Center);

        labelPosition.y = travelY;
        valuePosition.y = travelY;
        shotPanelTravelLabelText = CreateShotMetricsText(parent, "BallTravelTimeLabel", "Ball Travel Time", labelPosition, labelSize, labelTemplate, TextAlignmentOptions.Center);
        shotPanelTravelValueText = CreateShotMetricsText(parent, "BallTravelTimeValue", "--", valuePosition, valueSize, valueTemplate, TextAlignmentOptions.Center);

        labelPosition.y = liveTimerY;
        valuePosition.y = liveTimerY;
        shotPanelLiveTimerLabelText = CreateShotMetricsText(parent, "LiveTimerLabel", "Live Timer", labelPosition, labelSize, labelTemplate, TextAlignmentOptions.Center);
        shotPanelLiveTimerValueText = CreateShotMetricsText(parent, "LiveTimerValue", "--", valuePosition, valueSize, valueTemplate, TextAlignmentOptions.Center);

        labelPosition.y = speedRetentionY;
        valuePosition.y = speedRetentionY;
        shotPanelSpeedRetentionLabelText = CreateShotMetricsText(parent, "SpeedRetentionLabel", "23.77m Speed %", labelPosition, labelSize, labelTemplate, TextAlignmentOptions.Center);
        shotPanelSpeedRetentionValueText = CreateShotMetricsText(parent, "SpeedRetentionValue", "--", valuePosition, valueSize, valueTemplate, TextAlignmentOptions.Center);

        labelPosition.y = averageRallyY;
        valuePosition.y = averageRallyY;
        shotPanelAverageRallyLabelText = CreateShotMetricsText(parent, "AverageRallySpeedLabel", "Avg Rally Pace", labelPosition, labelSize, labelTemplate, TextAlignmentOptions.Center);
        shotPanelAverageRallyValueText = CreateShotMetricsText(parent, "AverageRallySpeedValue", "--", valuePosition, valueSize, valueTemplate, TextAlignmentOptions.Center);
        shotPanelAverageRallyResetButton = CreateShotMetricsButton(parent, "AverageRallySpeedReset", "Reset", valuePosition + new Vector2(64f, 0f), new Vector2(54f, 18f), valueTemplate);
    }

    private RectTransform FindChildRect(RectTransform parent, string childName)
    {
        if (parent == null)
            return null;

        Transform child = parent.Find(childName);
        return child != null ? child as RectTransform : null;
    }

    private TextMeshProUGUI CreateShotMetricsText(
        RectTransform parent,
        string objectName,
        string text,
        Vector2 anchoredPosition,
        Vector2 size,
        TextMeshProUGUI template,
        TextAlignmentOptions fallbackAlignment)
    {
        Transform existing = parent.Find(objectName);
        GameObject textObject = existing != null
            ? existing.gameObject
            : new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));

        textObject.transform.SetParent(parent, false);
        textObject.layer = parent.gameObject.layer;

        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        TextMeshProUGUI label = textObject.GetComponent<TextMeshProUGUI>();
        ConfigureShotMetricsText(label, template, fallbackAlignment);
        label.text = text;
        label.gameObject.SetActive(showBallSpeedInShotMetricsPanel);
        return label;
    }

    private Button CreateShotMetricsButton(RectTransform parent, string objectName, string text, Vector2 anchoredPosition, Vector2 size, TextMeshProUGUI template)
    {
        Transform existing = parent.Find(objectName);
        GameObject buttonObject = existing != null
            ? existing.gameObject
            : new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));

        buttonObject.transform.SetParent(parent, false);
        buttonObject.layer = parent.gameObject.layer;

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        Image background = buttonObject.GetComponent<Image>();
        background.color = new Color(1f, 1f, 1f, 0.14f);

        Button button = buttonObject.GetComponent<Button>();
        ColorBlock colours = button.colors;
        colours.normalColor = new Color(1f, 1f, 1f, 0.14f);
        colours.highlightedColor = new Color(1f, 1f, 1f, 0.26f);
        colours.pressedColor = new Color(0.65f, 0.9f, 1f, 0.36f);
        colours.selectedColor = colours.highlightedColor;
        colours.disabledColor = new Color(1f, 1f, 1f, 0.06f);
        button.colors = colours;
        button.onClick.RemoveListener(ResetAverageRallySpeed);
        button.onClick.AddListener(ResetAverageRallySpeed);

        TextMeshProUGUI label = buttonObject.GetComponentInChildren<TextMeshProUGUI>();
        if (label == null)
        {
            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(buttonObject.transform, false);
            labelObject.layer = parent.gameObject.layer;
            label = labelObject.GetComponent<TextMeshProUGUI>();
        }

        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        ConfigureShotMetricsText(label, template, TextAlignmentOptions.Center);
        label.text = text;
        label.fontSize = Mathf.Max(8f, label.fontSize - 2f);
        label.raycastTarget = false;
        buttonObject.SetActive(showBallSpeedInShotMetricsPanel);
        return button;
    }

    private void ConfigureShotMetricsText(TextMeshProUGUI label, TextMeshProUGUI template, TextAlignmentOptions fallbackAlignment)
    {
        label.fontSize = shotMetricsSpeedFontSize > 0f ? shotMetricsSpeedFontSize : (template != null ? template.fontSize : 12f);
        label.fontStyle = template != null ? template.fontStyle : FontStyles.Normal;
        label.alignment = template != null ? template.alignment : fallbackAlignment;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        label.color = template != null ? template.color : panelTextColour;

        if (template != null && template.font != null)
        {
            label.font = template.font;
            label.fontSharedMaterial = template.fontSharedMaterial;
        }
        else if (fontTemplate != null && fontTemplate.font != null)
        {
            label.font = fontTemplate.font;
            label.fontSharedMaterial = fontTemplate.fontSharedMaterial;
        }
    }

    private void UpdateShotMetricsSpeedRows(float ballMph)
    {
        EnsureShotMetricsSpeedRows();

        SetShotMetricsRowActive(shotPanelLiveLabelText);
        SetShotMetricsRowActive(shotPanelLiveValueText);
        SetShotMetricsRowActive(shotPanelLaunchLabelText);
        SetShotMetricsRowActive(shotPanelLaunchValueText);
        SetShotMetricsRowActive(shotPanelTravelLabelText);
        SetShotMetricsRowActive(shotPanelTravelValueText);
        SetShotMetricsRowActive(shotPanelLiveTimerLabelText);
        SetShotMetricsRowActive(shotPanelLiveTimerValueText);
        SetShotMetricsRowActive(shotPanelSpeedRetentionLabelText);
        SetShotMetricsRowActive(shotPanelSpeedRetentionValueText);
        SetShotMetricsRowActive(shotPanelAverageRallyLabelText);
        SetShotMetricsRowActive(shotPanelAverageRallyValueText);
        if (shotPanelAverageRallyResetButton != null && shotPanelAverageRallyResetButton.gameObject.activeSelf != showBallSpeedInShotMetricsPanel)
            shotPanelAverageRallyResetButton.gameObject.SetActive(showBallSpeedInShotMetricsPanel);
        SetShotMetricsRowActive(shotPanelBackswingCapLabelText);
        SetShotMetricsRowActive(shotHeightUI != null ? shotHeightUI.backswingCapSpeedValue : null);
        SetShotMetricsRowActive(shotPanelRetainedCapLabelText);
        SetShotMetricsRowActive(shotHeightUI != null ? shotHeightUI.retainedCapSpeedValue : null);
        SetShotMetricsRowActive(shotPanelSolverClearanceLabelText);
        SetShotMetricsRowActive(shotHeightUI != null ? shotHeightUI.solverNetClearanceValue : null);
        SetShotMetricsRowActive(shotPanelActualClearanceLabelText);
        SetShotMetricsRowActive(shotHeightUI != null ? shotHeightUI.actualNetClearanceValue : null);

        Color inactiveColour = new Color(1f, 1f, 1f, 0.35f);

        if (shotPanelLiveValueText != null)
        {
            SetTextIfChanged(shotPanelLiveValueText, showBallSpeed ? $"{ballMph:0} MPH" : "OFF");
            shotPanelLiveValueText.color = showBallSpeed ? GradeSpeedColour(ballMph, ballOrangeMph, ballGreenMph, ballBlueMph) : inactiveColour;
        }

        if (shotPanelLaunchValueText != null)
        {
            SetTextIfChanged(shotPanelLaunchValueText, showBallSpeed ? $"{trackedLaunchSpeedMph:0} MPH" : "OFF");
            shotPanelLaunchValueText.color = showBallSpeed ? GradeSpeedColour(trackedLaunchSpeedMph, ballOrangeMph, ballGreenMph, ballBlueMph) : inactiveColour;
        }

        if (shotPanelTravelValueText != null)
        {
            SetTextIfChanged(shotPanelTravelValueText, showBallSpeed ? FormatTravelTime() : "OFF");
            shotPanelTravelValueText.color = showBallSpeed ? panelTextColour : inactiveColour;
        }

        if (shotPanelLiveTimerValueText != null)
        {
            SetTextIfChanged(shotPanelLiveTimerValueText, showBallSpeed ? FormatLiveTimer() : "OFF");
            shotPanelLiveTimerValueText.color = showBallSpeed ? panelTextColour : inactiveColour;
        }

        if (shotPanelSpeedRetentionValueText != null)
        {
            SetTextIfChanged(shotPanelSpeedRetentionValueText, showBallSpeed ? FormatSpeedRetention() : "OFF");
            shotPanelSpeedRetentionValueText.color = showBallSpeed ? panelTextColour : inactiveColour;
        }

        if (shotPanelAverageRallyValueText != null)
        {
            SetTextIfChanged(shotPanelAverageRallyValueText, showBallSpeed ? FormatAverageRallySpeed() : "OFF");
            float averageMph = averageRallySpeedCount > 0 ? averageRallySpeedSumMph / averageRallySpeedCount : 0f;
            shotPanelAverageRallyValueText.color = showBallSpeed && averageRallySpeedCount > 0
                ? GradeSpeedColour(averageMph, ballOrangeMph, ballGreenMph, ballBlueMph)
                : inactiveColour;
        }
        UpdateNetClearanceRows(inactiveColour);
    }


    private void UpdateNetClearanceRows(Color inactiveColour)
    {
        bool hasShot = hitController != null && hitController.hasLastShotUiData;
        string backswingCapText = showBallSpeed && hasShot ? FormatMph(hitController.lastBackswingCapSpeedMph) : "--";
        string retainedCapText = showBallSpeed && hasShot ? FormatMph(hitController.lastRetainedCapSpeedMph) : "--";
        string solverText = showBallSpeed && hasShot ? FormatClearanceCm(hitController.lastSolverNetClearanceCm) : "--";
        string actualText = showBallSpeed && hasShot ? FormatClearanceCm(hitController.lastActualNetClearanceCm) : "--";
        Color valueColour = showBallSpeed ? panelTextColour : inactiveColour;

        if (shotHeightUI != null && shotHeightUI.backswingCapSpeedValue != null)
        {
            SetTextIfChanged(shotHeightUI.backswingCapSpeedValue, showBallSpeed ? backswingCapText : "OFF");
            shotHeightUI.backswingCapSpeedValue.color = valueColour;
        }

        if (shotHeightUI != null && shotHeightUI.retainedCapSpeedValue != null)
        {
            SetTextIfChanged(shotHeightUI.retainedCapSpeedValue, showBallSpeed ? retainedCapText : "OFF");
            shotHeightUI.retainedCapSpeedValue.color = valueColour;
        }

        if (shotHeightUI != null && shotHeightUI.solverNetClearanceValue != null)
        {
            SetTextIfChanged(shotHeightUI.solverNetClearanceValue, showBallSpeed ? solverText : "OFF");
            shotHeightUI.solverNetClearanceValue.color = valueColour;
        }

        if (shotHeightUI != null && shotHeightUI.actualNetClearanceValue != null)
        {
            SetTextIfChanged(shotHeightUI.actualNetClearanceValue, showBallSpeed ? actualText : "OFF");
            shotHeightUI.actualNetClearanceValue.color = valueColour;
        }
    }

    private static void SetTextIfChanged(TMP_Text label, string value)
    {
        if (label != null && label.text != value)
            label.text = value;
    }

    private static string FormatClearanceCm(float clearanceCm)
    {
        return float.IsFinite(clearanceCm) ? $"{clearanceCm:F0} cm" : "--";
    }

    private static string FormatMph(float speedMph)
    {
        return float.IsFinite(speedMph) ? $"{speedMph:F0} mph" : "--";
    }
    private void HandlePlayerBallLaunched(Rigidbody launchedBody, Vector3 startPosition, Vector3 launchVelocity)
    {
        if (ballRigidbody != null && launchedBody != ballRigidbody)
            return;

        ballRigidbody = launchedBody;
        shotFlightSource = "player";
        RegisterAverageRallySpeed(launchVelocity);
        BeginTravelTracking(startPosition, launchVelocity, playerTravelDistanceMetres);
    }

    private void HandleCannonBallLaunched(Rigidbody launchedBody, Vector3 startPosition, Vector3 launchVelocity)
    {
        if (ballRigidbody != null && launchedBody != ballRigidbody)
            return;

        ballRigidbody = launchedBody;
        shotFlightSource = "cannon";
        RegisterAverageRallySpeed(launchVelocity);
        BeginTravelTracking(startPosition, launchVelocity, cannonTravelDistanceMetres);
    }

    private void RegisterAverageRallySpeed(Vector3 launchVelocity)
    {
        float speedMph = launchVelocity.magnitude * MetresPerSecondToMph;
        if (speedMph < 5f)
            return;

        averageRallySpeedSumMph += speedMph;
        averageRallySpeedCount++;
    }

    private void ResetAverageRallySpeed()
    {
        averageRallySpeedSumMph = 0f;
        averageRallySpeedCount = 0;
    }

    private string FormatAverageRallySpeed()
    {
        if (averageRallySpeedCount <= 0)
            return "--";

        return $"{averageRallySpeedSumMph / averageRallySpeedCount:0} MPH";
    }

    private void BeginTravelTracking(Vector3 startPosition, Vector3 launchVelocity, float targetDistance)
    {
        Vector3 horizontalVelocity = launchVelocity;
        horizontalVelocity.y = 0f;

        float horizontalSpeed = horizontalVelocity.magnitude;
        if (horizontalSpeed <= 0.01f)
        {
            travelTrackingActive = false;
            travelTimeMeasured = false;
            travelTimeEstimated = true;
            travelDisplaySeconds = 0f;
            return;
        }

        travelStartPosition = startPosition;
        travelDirection = horizontalVelocity / horizontalSpeed;
        travelStartTime = Time.time;
        travelTargetDistanceMetres = Mathf.Max(0.1f, targetDistance);
        travelDisplaySeconds = travelTargetDistanceMetres / horizontalSpeed;
        travelTimeMeasured = false;
        travelTimeEstimated = true;
        travelTrackingActive = true;

        liveTimerStartPosition = startPosition;
        liveTimerDirection = travelDirection;
        liveTimerStartTime = Time.time;
        liveTimerSeconds = 0f;
        liveTimerHasValue = true;
        liveTimerActive = true;

        speedRetentionLaunchSpeed = launchVelocity.magnitude;
        speedRetentionPreviousDistance = 0f;
        speedRetentionPreviousSpeed = speedRetentionLaunchSpeed;
        speedRetentionPercent = 100f;
        speedRetentionHasValue = speedRetentionLaunchSpeed > 0.01f;
        speedRetentionMeasured = false;
        speedRetentionActive = speedRetentionHasValue;

        BeginShotFlightDebugLog(startPosition, launchVelocity);
    }

    private void BeginShotFlightDebugLog(Vector3 startPosition, Vector3 launchVelocity)
    {
        if (!logShotFlightMetrics)
            return;

        Vector3 horizontalVelocity = launchVelocity;
        horizontalVelocity.y = 0f;
        float horizontalSpeed = horizontalVelocity.magnitude;

        shotFlightLogActive = horizontalSpeed > 0.01f;
        shotFlightDistanceLogged = false;
        shotFlightBounceLogged = false;
        shotFlightLogPrinted = false;
        shotFlightLaunchTime = Time.time;
        shotFlightLaunchSpeed = launchVelocity.magnitude;
        shotFlightDistanceSeconds = 0f;
        shotFlightDistanceSpeed = 0f;
        shotFlightBounceInSpeed = 0f;
        shotFlightBounceOutSpeed = 0f;
        shotFlightBounceTime = 0f;
        shotFlightBounceDistance = 0f;
        shotFlightHasLowPreBounceSample = false;
        shotFlightLowPreBounceSpeed = 0f;
        shotFlightLowPreBounceTime = 0f;
        shotFlightLowPreBounceBottomHeight = 0f;
        shotFlightCollisionPreBounceSpeed = 0f;
        shotFlightPreBounceSource = "collision";
        shotFlightBounceCourtName = "court";
        shotFlightStartPosition = startPosition;
        shotFlightDirection = shotFlightLogActive ? horizontalVelocity / horizontalSpeed : Vector3.zero;
        shotFlightPreviousDistance = 0f;
        shotFlightPreviousSpeed = shotFlightLaunchSpeed;
    }

    private void UpdateTravelTime()
    {
        if (!travelTrackingActive || ballRigidbody == null)
            return;

        float elapsed = Mathf.Max(0f, Time.time - travelStartTime);
        Vector3 offset = ballRigidbody.position - travelStartPosition;
        offset.y = 0f;

        float travelled = Vector3.Dot(offset, travelDirection);
        if (travelled >= travelTargetDistanceMetres)
        {
            travelDisplaySeconds = elapsed;
            travelTimeMeasured = true;
            travelTimeEstimated = false;
            travelTrackingActive = false;
            return;
        }

        Vector3 velocity = ballRigidbody.linearVelocity;
        velocity.y = 0f;

        float projectedSpeed = Vector3.Dot(velocity, travelDirection);
        float remaining = Mathf.Max(0f, travelTargetDistanceMetres - travelled);
        if (projectedSpeed > travelEstimateStopSpeed)
        {
            travelDisplaySeconds = elapsed + remaining / projectedSpeed;
            travelTimeEstimated = true;
            return;
        }

        travelDisplaySeconds = Mathf.Max(travelDisplaySeconds, elapsed);
        travelTimeEstimated = true;
        travelTrackingActive = false;
    }

    private void UpdateLiveTimer()
    {
        if (!liveTimerActive || ballRigidbody == null)
            return;

        liveTimerSeconds = Mathf.Max(0f, Time.time - liveTimerStartTime);

        Vector3 offset = ballRigidbody.position - liveTimerStartPosition;
        offset.y = 0f;

        float travelled = Vector3.Dot(offset, liveTimerDirection);
        if (travelled >= Mathf.Max(0.1f, liveTimerStopDistanceMetres))
            liveTimerActive = false;
    }

    private void UpdateSpeedRetentionAtDistance()
    {
        if (!speedRetentionActive || ballRigidbody == null || speedRetentionLaunchSpeed <= 0.01f)
            return;

        float targetDistance = Mathf.Max(0.1f, liveTimerStopDistanceMetres);
        Vector3 offset = ballRigidbody.position - liveTimerStartPosition;
        offset.y = 0f;

        float distance = Vector3.Dot(offset, liveTimerDirection);
        float currentSpeed = ballRigidbody.linearVelocity.magnitude;

        if (distance >= targetDistance)
        {
            float speedAtTarget = currentSpeed;
            float span = distance - speedRetentionPreviousDistance;
            if (span > 0.001f)
            {
                float t = Mathf.InverseLerp(speedRetentionPreviousDistance, distance, targetDistance);
                speedAtTarget = Mathf.Lerp(speedRetentionPreviousSpeed, currentSpeed, t);
            }

            speedRetentionPercent = Mathf.Max(0f, speedAtTarget / speedRetentionLaunchSpeed * 100f);
            speedRetentionMeasured = true;
            speedRetentionActive = false;
            return;
        }

        if (distance >= 0f)
            speedRetentionPercent = Mathf.Max(0f, currentSpeed / speedRetentionLaunchSpeed * 100f);

        speedRetentionPreviousDistance = distance;
        speedRetentionPreviousSpeed = currentSpeed;
    }

    private void UpdateShotFlightDebugLogDistance()
    {
        if (!shotFlightLogActive || shotFlightDistanceLogged || ballRigidbody == null)
            return;

        float targetDistance = Mathf.Max(0.1f, shotFlightLogDistanceMetres);
        Vector3 offset = ballRigidbody.position - shotFlightStartPosition;
        offset.y = 0f;

        float distance = Vector3.Dot(offset, shotFlightDirection);
        float currentSpeed = ballRigidbody.linearVelocity.magnitude;

        if (distance >= targetDistance)
        {
            float speedAtTarget = currentSpeed;
            float timeAtTarget = Mathf.Max(0f, Time.time - shotFlightLaunchTime);
            float span = distance - shotFlightPreviousDistance;
            if (span > 0.001f)
            {
                float t = Mathf.InverseLerp(shotFlightPreviousDistance, distance, targetDistance);
                speedAtTarget = Mathf.Lerp(shotFlightPreviousSpeed, currentSpeed, t);
                timeAtTarget = Mathf.Lerp(Time.time - Time.deltaTime - shotFlightLaunchTime, Time.time - shotFlightLaunchTime, t);
            }

            shotFlightDistanceSpeed = speedAtTarget;
            shotFlightDistanceSeconds = Mathf.Max(0f, timeAtTarget);
            shotFlightDistanceLogged = true;
            TryPrintShotFlightDebugLog();
            return;
        }

        if (distance >= 0f)
        {
            shotFlightPreviousDistance = distance;
            shotFlightPreviousSpeed = currentSpeed;
        }
    }

    private void UpdateShotFlightPreBounceSample()
    {
        if (!shotFlightLogActive || shotFlightBounceLogged || ballRigidbody == null)
            return;

        Vector3 velocity = ballRigidbody.linearVelocity;
        if (velocity.y >= 0f)
            return;

        Vector3 offset = ballRigidbody.position - shotFlightStartPosition;
        offset.y = 0f;
        float distance = Vector3.Dot(offset, shotFlightDirection);
        if (distance < 0f)
            return;

        float bottomHeight = ballRigidbody.position.y - Mathf.Max(0f, shotFlightBallRadiusMetres);
        float sampleHeight = Mathf.Max(0f, shotFlightPreBounceSampleBottomHeightMetres);
        if (bottomHeight > sampleHeight)
            return;

        shotFlightHasLowPreBounceSample = true;
        shotFlightLowPreBounceSpeed = velocity.magnitude;
        shotFlightLowPreBounceTime = Mathf.Max(0f, Time.time - shotFlightLaunchTime);
        shotFlightLowPreBounceBottomHeight = Mathf.Max(0f, bottomHeight);
    }

    private void HandleCourtBounceApplied(Rigidbody bouncedBody, string courtName, Vector3 contactPoint, Vector3 velocityIn, Vector3 velocityOut)
    {
        if (!shotFlightLogActive || shotFlightBounceLogged)
            return;

        if (ballRigidbody != null && bouncedBody != ballRigidbody)
            return;

        shotFlightBounceCourtName = string.IsNullOrEmpty(courtName) ? "court" : courtName;
        shotFlightCollisionPreBounceSpeed = velocityIn.magnitude;
        shotFlightBounceInSpeed = shotFlightHasLowPreBounceSample ? shotFlightLowPreBounceSpeed : shotFlightCollisionPreBounceSpeed;
        shotFlightPreBounceSource = shotFlightHasLowPreBounceSample ? "lowHeight" : "collision";
        shotFlightBounceOutSpeed = velocityOut.magnitude;
        shotFlightBounceTime = Mathf.Max(0f, Time.time - shotFlightLaunchTime);

        Vector3 bounceOffset = contactPoint - shotFlightStartPosition;
        bounceOffset.y = 0f;
        shotFlightBounceDistance = Mathf.Max(0f, Vector3.Dot(bounceOffset, shotFlightDirection));

        shotFlightBounceLogged = true;
        TryPrintShotFlightDebugLog();
    }

    private void TryPrintShotFlightDebugLog()
    {
        if (!logShotFlightMetrics || shotFlightLogPrinted || !shotFlightDistanceLogged || !shotFlightBounceLogged)
            return;

        float retentionPercent = shotFlightLaunchSpeed > 0.01f
            ? shotFlightDistanceSpeed / shotFlightLaunchSpeed * 100f
            : 0f;
        float bounceRetentionPercent = shotFlightBounceInSpeed > 0.01f
            ? shotFlightBounceOutSpeed / shotFlightBounceInSpeed * 100f
            : 0f;
        float preBounceLaunchRetentionPercent = shotFlightLaunchSpeed > 0.01f
            ? shotFlightBounceInSpeed / shotFlightLaunchSpeed * 100f
            : 0f;
        float preBounceDragLossPercent = Mathf.Max(0f, 100f - preBounceLaunchRetentionPercent);
        string preBounceDetail = shotFlightHasLowPreBounceSample
            ? $"source={shotFlightPreBounceSource}, sampleT={shotFlightLowPreBounceTime:F3}s, sampleBottomY={shotFlightLowPreBounceBottomHeight:F2}m, collisionPre={FormatSpeedPair(shotFlightCollisionPreBounceSpeed)}"
            : $"source={shotFlightPreBounceSource}";

        Debug.Log(
            $"[SHOT FLIGHT] source={shotFlightSource}, distance={shotFlightLogDistanceMetres:F2}m, " +
            $"launch={FormatSpeedPair(shotFlightLaunchSpeed)}, " +
            $"travelTime={shotFlightDistanceSeconds:F3}s, " +
            $"speedAtDistance={FormatSpeedPair(shotFlightDistanceSpeed)} ({retentionPercent:F0}% launch), " +
            $"bounceCourt={shotFlightBounceCourtName}, " +
            $"bounceTime={shotFlightBounceTime:F3}s, bounceDistance={shotFlightBounceDistance:F2}m, " +
            $"preBounce={FormatSpeedPair(shotFlightBounceInSpeed)} ({preBounceLaunchRetentionPercent:F0}% launch, dragLoss={preBounceDragLossPercent:F0}%, {preBounceDetail}), " +
            $"postBounce={FormatSpeedPair(shotFlightBounceOutSpeed)} ({bounceRetentionPercent:F0}% pre)"
        );

        shotFlightLogPrinted = true;
        shotFlightLogActive = false;
    }

    private string FormatSpeedPair(float metresPerSecond)
    {
        return $"{metresPerSecond:F2}m/s/{metresPerSecond * MetresPerSecondToMph:F0}mph";
    }

    private string FormatTravelTime()
    {
        if (travelDisplaySeconds <= 0f)
            return "--";

        string prefix = travelTimeEstimated && !travelTimeMeasured ? "~" : string.Empty;
        return $"{prefix}{travelDisplaySeconds:0.00} s";
    }

    private string FormatLiveTimer()
    {
        if (!liveTimerHasValue)
            return "--";

        return $"{liveTimerSeconds:0.00} s";
    }

    private string FormatSpeedRetention()
    {
        if (!speedRetentionHasValue)
            return "--";

        string prefix = speedRetentionMeasured ? string.Empty : "~";
        return $"{prefix}{speedRetentionPercent:0}%";
    }

    private void SetShotMetricsRowActive(TextMeshProUGUI text)
    {
        if (text != null && text.gameObject.activeSelf != showBallSpeedInShotMetricsPanel)
            text.gameObject.SetActive(showBallSpeedInShotMetricsPanel);
    }

    private Color GradeSpeedColour(float mph, float orangeMph, float greenMph, float blueMph)
    {
        if (mph <= greenMph)
        {
            float t = Mathf.InverseLerp(orangeMph, greenMph, mph);
            return Color.Lerp(slowColour, moderateColour, t);
        }

        float fastT = Mathf.InverseLerp(greenMph, blueMph, mph);
        return Color.Lerp(moderateColour, fastColour, fastT);
    }
}

[AddComponentMenu("")]
public class LiveSpeedGaugeGraphic : MaskableGraphic
{
    public float liveSpeedMph;
    public float launchSpeedMph;
    public float maxSpeedMph = 80f;
    public bool showLaunchMarker = true;
    public Color trackColour = new Color(1f, 1f, 1f, 0.45f);
    public Color launchMarkerColour = new Color(1f, 1f, 1f, 0.9f);
    public Color slowColour = new Color(1f, 0.55f, 0.16f, 1f);
    public Color moderateColour = new Color(0.15f, 1f, 0.25f, 1f);
    public Color fastColour = new Color(0.12f, 0.35f, 1f, 1f);
    public float orangeMph = 25f;
    public float greenMph = 55f;
    public float blueMph = 90f;
    public float trackThickness = 1.15f;
    public float liveSectorThickness = 2.4f;
    public float liveFillThickness = 8f;
    public float launchMarkerThickness = 1.4f;

    public void SetSpeeds(float liveMph, float launchMph, float maxMph)
    {
        liveSpeedMph = Mathf.Max(0f, liveMph);
        launchSpeedMph = Mathf.Max(0f, launchMph);
        maxSpeedMph = Mathf.Max(1f, maxMph);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect rect = rectTransform.rect;
        if (rect.width <= 1f || rect.height <= 1f)
            return;

        Vector2 center = new Vector2(rect.center.x, rect.yMin + 4f);
        float radius = Mathf.Min(rect.width * 0.46f, rect.height - 8f);
        radius = Mathf.Max(8f, radius);
        float fillThickness = Mathf.Clamp(liveFillThickness, Mathf.Max(2f, liveSectorThickness), radius * 0.85f);
        float fillCenterRadius = Mathf.Max(1f, radius - fillThickness * 0.5f);

        const float startDeg = 205f;
        const float endDeg = -25f;
        DrawArc(vh, center, fillCenterRadius, startDeg, endDeg, 34, trackThickness, trackColour);

        float liveT = Mathf.Clamp01(liveSpeedMph / Mathf.Max(1f, maxSpeedMph));
        if (liveT > 0.001f)
        {
            float liveAngle = Mathf.Lerp(startDeg, endDeg, liveT);
            int liveSegments = Mathf.Max(2, Mathf.CeilToInt(34f * liveT));
            DrawFilledArc(vh, center, radius, fillThickness, startDeg, liveAngle, liveSegments);
        }

        if (showLaunchMarker)
        {
            Color markerColor = GradeSpeedColour(launchSpeedMph);
            markerColor.a *= launchMarkerColour.a;
            DrawMarker(vh, center, radius, SpeedToAngle(launchSpeedMph, startDeg, endDeg), launchMarkerThickness, markerColor, 0.34f, 1.02f);
        }
    }

    private float SpeedToAngle(float mph, float startDeg, float endDeg)
    {
        float t = Mathf.Clamp01(mph / Mathf.Max(1f, maxSpeedMph));
        return Mathf.Lerp(startDeg, endDeg, t);
    }

    private void DrawArc(VertexHelper vh, Vector2 center, float radius, float startDeg, float endDeg, int segments, float thickness, Color lineColor)
    {
        Vector2 previous = PointOnCircle(center, radius, startDeg);
        for (int i = 1; i <= segments; i++)
        {
            float t = i / (float)segments;
            float angle = Mathf.Lerp(startDeg, endDeg, t);
            Vector2 next = PointOnCircle(center, radius, angle);
            AddLine(vh, previous, next, thickness, lineColor);
            previous = next;
        }
    }

    private void DrawFilledArc(VertexHelper vh, Vector2 center, float outerRadius, float thickness, float startDeg, float endDeg, int segments)
    {
        float innerRadius = Mathf.Max(1f, outerRadius - thickness);

        for (int i = 0; i < segments; i++)
        {
            float t0 = i / (float)segments;
            float t1 = (i + 1) / (float)segments;
            float angle0 = Mathf.Lerp(startDeg, endDeg, t0);
            float angle1 = Mathf.Lerp(startDeg, endDeg, t1);
            float speedAtSegment = Mathf.Lerp(0f, liveSpeedMph, t1);
            Color segmentColor = GradeSpeedColour(speedAtSegment);

            Vector2 inner0 = PointOnCircle(center, innerRadius, angle0);
            Vector2 outer0 = PointOnCircle(center, outerRadius, angle0);
            Vector2 outer1 = PointOnCircle(center, outerRadius, angle1);
            Vector2 inner1 = PointOnCircle(center, innerRadius, angle1);

            AddQuad(vh, inner0, outer0, outer1, inner1, segmentColor);
        }
    }

    private void DrawMarker(VertexHelper vh, Vector2 center, float radius, float angleDeg, float thickness, Color markerColor, float innerScale, float outerScale)
    {
        Vector2 dir = DirectionFromAngle(angleDeg);
        AddLine(vh, center + dir * radius * innerScale, center + dir * radius * outerScale, thickness, markerColor);
    }

    private static Vector2 PointOnCircle(Vector2 center, float radius, float angleDeg)
    {
        return center + DirectionFromAngle(angleDeg) * radius;
    }

    private static Vector2 DirectionFromAngle(float angleDeg)
    {
        float rad = angleDeg * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
    }

    private Color GradeSpeedColour(float mph)
    {
        if (mph <= greenMph)
        {
            float t = Mathf.InverseLerp(orangeMph, greenMph, mph);
            return Color.Lerp(slowColour, moderateColour, t);
        }

        float fastT = Mathf.InverseLerp(greenMph, blueMph, mph);
        return Color.Lerp(moderateColour, fastColour, fastT);
    }

    private static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
    {
        int index = vh.currentVertCount;
        vh.AddVert(a, color, Vector2.zero);
        vh.AddVert(b, color, Vector2.zero);
        vh.AddVert(c, color, Vector2.zero);
        vh.AddVert(d, color, Vector2.zero);
        vh.AddTriangle(index, index + 1, index + 2);
        vh.AddTriangle(index, index + 2, index + 3);
    }

    private static void AddLine(VertexHelper vh, Vector2 a, Vector2 b, float thickness, Color lineColor)
    {
        Vector2 direction = b - a;
        if (direction.sqrMagnitude < 0.000001f)
            return;

        Vector2 normal = new Vector2(-direction.y, direction.x).normalized * (thickness * 0.5f);
        int index = vh.currentVertCount;
        vh.AddVert(a - normal, lineColor, Vector2.zero);
        vh.AddVert(a + normal, lineColor, Vector2.zero);
        vh.AddVert(b + normal, lineColor, Vector2.zero);
        vh.AddVert(b - normal, lineColor, Vector2.zero);
        vh.AddTriangle(index, index + 1, index + 2);
        vh.AddTriangle(index, index + 2, index + 3);
    }
}




