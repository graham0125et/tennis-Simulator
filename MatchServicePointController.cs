using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(-500)]
public sealed class MatchServicePointController : MonoBehaviour
{
    public enum MatchPhase { Disabled, PreparingPoint, FirstServeReady, SecondServeTransition, SecondServeReady, TossInProgress, ServeFlight, Rally, PointReset, MatchComplete }
    public enum ServiceCourt { Deuce, Ad }

    [Header("Mode")]
    public bool matchMode = false;
    public bool autoStartWhenMatchplayAI = true;
    public KeyCode toggleMatchModeKey = KeyCode.F8;
    public KeyCode forcePlayerServeKey = KeyCode.F1;
    public KeyCode forceAIServeKey = KeyCode.F2;
    public bool debugLogs = true;

    [Header("Rally feedback")]
    [Tooltip("Records actual launch pace, ball flight and receiver movement for up to ten shots per side, then logs one report when the point ends.")]
    public bool enableRallyFeedback = true;
    public RallyFeedbackRecorder rallyFeedback;

    [Header("Body contact scoring")]
    [Tooltip("Ignores an artificial overlap with the hitter immediately after a scripted racket launch.")]
    public float racketContactBodyCollisionGraceSeconds = 0.08f;

    [Header("Service timing")]
    public float serviceTimeLimitSeconds = 20f;
    public float pointResetDelaySeconds = 2f;
    public float serverBehindBaseline = 0.75f;
    public float serverStandTolerance = 0.18f;
    public float tossContactWindowSeconds = 3f;
    public float tossScrollForMaximumHeight = 4f;
    public float minimumTossHeightFallback = 2f;
    public float maximumTossHeightFallback = 3.5f;
    [Tooltip("The toss apex is always at least this far above the low service-contact marker.")]
    public float minimumTossRiseAboveLowContact = 0.85f;
    [Tooltip("A full toss reaches this far above the high service-contact marker.")]
    public float maximumTossRiseAboveHighContact = 1.15f;
    public float aiTossDelaySeconds = 0.35f;
    public float aiServeContactDelaySeconds = 0.60f;
    [Tooltip("AI toss apex above serviceContactPointLow; independent of rally height controls.")]
    public float aiTossApexAboveLowContact = 0.55f;
    public float startPositionSnapDelaySeconds = 3f;
    [Tooltip("Maximum horizontal distance from the frozen toss spawn at which the player can contact a serve.")]
    public float playerServeContactPlanarReach = 1.10f;
    [Tooltip("Small height tolerance around the low/high service-contact markers, in addition to the ball radius.")]
    public float playerServeContactHeightTolerance = 0.04f;

    [Header("Service timing")]
    [Tooltip("Pause after a first-serve fault before the same server begins the second-serve toss sequence.")]
    public float secondServeTransitionDelaySeconds = 1.25f;

    [Header("Serve speeds (m/s)")]
    public Vector2 firstServeSpeedRange = new Vector2(42f, 50f);
    public Vector2 secondServeSpeedRange = new Vector2(34f, 42f);
    public Vector2 aiFirstServeTargetDepthRange01 = new Vector2(0.72f, 0.90f);
    public Vector2 aiSecondServeTargetDepthRange01 = new Vector2(0.70f, 0.84f);
    public float aiServeTargetLateralInset = 0.45f;
    [Tooltip("The AI aims near a service-box sideline rather than randomly through the centre. Values are fractions inward from either sideline.")]
    public Vector2 aiServeCornerDistanceFromSideline01 = new Vector2(0.06f, 0.24f);
    [Tooltip("Safe target variants checked before an AI serve is launched. The next candidate alternates to the other corner.")]
    [Min(1)] public int aiServeTargetSolveAttempts = 6;
    [Min(1)] public int aiServeSpeedSolveAttempts = 4;
    [Tooltip("Bisection iterations used to refine the exact ground-landing angle for an AI serve. Negative angles are deliberately supported for high contacts.")]
    [Range(10, 24)] public int aiServeLandingAngleRefinementIterations = 18;
    [Tooltip("If the preferred serve spin cannot make a legal deep target, try the other serve types before faulting.")]
    public bool aiServeTryAlternateShotTypes = true;
    [Tooltip("The solver tries this additional clearance on later safe-serve candidates.")]
    public float aiServeSafetyClearanceStep = 0.10f;
    [Tooltip("Maximum net clearance used by AI serve safety candidates.")]
    public float aiServeMaximumNetClearance = 0.45f;
    public float serviceNetClearance = 0.20f;
    public float netHeight = 0.914f;
    public float serviceBackswingMinMph = 80f;
    public float serviceBackswingMaxMph = 110f;
    public float flatServeSpinRpm = 250f;
    public float sliceServeSpinRpm = -2200f;
    public float kickServeSpinRpm = 3800f;
    public BaseShotType aiServeShotType = BaseShotType.Flat;
    [Header("AI second-serve mix")]
    [Tooltip("Use a safer spin mix on a second serve instead of always repeating the first-serve type.")]
    public bool aiUseSecondServeSpinMix = true;
    [Range(0f, 1f)] public float aiSecondServeKickChance = 0.58f;
    [Range(0f, 1f)] public float aiSecondServeSliceChance = 0.32f;

    [Header("Scene references (auto-found by name)")]
    public swipeMouseBall playerSwipe;
    public hitController playerHitController;
    public TennisAIPlayerController receiverAI;
    public TennisAIPlayerController serverAI;
    public Transform servicePlayerBoundL;
    public Transform servicePlayerBoundM;
    public Transform servicePlayerBoundR;
    public Transform serviceAIBoundL;
    public Transform serviceAIBoundM;
    public Transform serviceAIBoundR;
    public Transform serviceContactPointLow;
    public Transform serviceContactPointHigh;
    public Transform playerServerSpawnPoint;
    public Transform aiServerSpawnPoint;
    public Transform netPoint;
    public Transform[] courtBounds = new Transform[4];

    [Header("Match start positions")]
    public Transform playerServeStartR;
    public Transform playerServeStartL;
    public Transform aiServeStart1;
    public Transform aiServeStart2;
    public Transform aiReceiveStart1;
    public Transform aiReceiveStart2;
    public Transform playerReceiveStart1;
    public Transform playerReceiveStart2;
    public Transform inbetweenPointPosition;

    [Header("Debug gizmos")]
    public bool drawCourtBoundsGizmo = true;
    public bool drawServiceBoxGizmo = true;
    public bool drawActivePositionGizmos = true;
    public float serverBallForwardOffset = 0.35f;
    public float serverBallRightOffset = 0.18f;
    public float serverBallHeightOffset = 1.49f;
    public float serverBallMaximumPlanarDistance = 1f;
    public Color courtBoundsGizmoColor = new Color(1f, 0.85f, 0.1f, 0.95f);
    public Color serviceBoxGizmoColor = new Color(0.1f, 0.95f, 1f, 0.95f);
    public Color activeServeStartGizmoColor = new Color(0.2f, 1f, 0.2f, 1f);
    public Color activeReceiveStartGizmoColor = new Color(0.2f, 0.55f, 1f, 1f);
    public Color activeBallSpawnGizmoColor = new Color(1f, 0.35f, 0.1f, 1f);
    public bool logServiceBoxTrigger = true;
    [Tooltip("When descending below this height, use the current trajectory to project the first landing.")]
    public float serviceNearGroundProjectionHeight = 0.55f;
    public bool useNearGroundServiceProjection = true;

    [Header("Rally first-bounce detection")]
    public bool useRallyCourtTriggers = true;
    [Tooltip("Thin surface trigger height above each singles-court half.")]
    public float rallyCourtTriggerHeight = 0.08f;
    public bool useNearGroundRallyProjection = true;
    public float rallyNearGroundProjectionHeight = 0.55f;
    public bool logRallyBounceDetection = true;
    public Color nearRallyTriggerGizmoColor = new Color(0.25f, 1f, 0.35f, 0.8f);
    public Color farRallyTriggerGizmoColor = new Color(0.3f, 0.65f, 1f, 0.8f);

    [Header("Service boundary markers")]
    public Transform nearServiceOuterLeft;
    public Transform nearServiceOuterRight;
    public Transform nearServiceCentreLeft;
    public Transform nearServiceCentreRight;
    public Transform farServiceOuterLeft;
    public Transform farServiceOuterRight;
    public Transform farServiceCentreLeft;
    public Transform farServiceCentreRight;

    [Header("Tiebreak scoreboard")]
    public bool showTiebreakScoreboard = true;
    public string playerDisplayName = "PLAYER";
    public string aiDisplayName = "AI";
    public bool useDedicatedScoreboardCanvas = true;
    public Canvas scoreboardCanvas;
    public Vector2 scoreboardInset = new Vector2(32f, 150f);
    public Vector2 scoreboardSize = new Vector2(300f, 76f);
    public float scoreboardFontSize = 15f;
    public Color scoreboardPanelColor = new Color(0.035f, 0.16f, 0.17f, 0.72f);
    public Color scoreboardTextColor = new Color(0.92f, 0.98f, 0.96f, 1f);
    public Color scoreboardScoreColumnColor = new Color(0.04f, 0.44f, 0.43f, 0.82f);
    public Color scoreboardScoreColor = new Color(0.02f, 0.16f, 0.18f, 1f);
    public Color scoreboardServerIndicatorColor = new Color(0.82f, 1f, 0.92f, 1f);
    public int tiebreakPointsToWin = 7;
    public bool tiebreakWinByTwo = true;

    [Header("Post-point ball run-out")]
    [Tooltip("The dead ball remains under physics for at least this long after the point decision.")]
    public float postPointMinimumRunSeconds = 3f;
    [Tooltip("The dead ball is frozen after this time even if it has not settled.")]
    public float postPointMaximumRunSeconds = 10f;
    [Tooltip("After the minimum time, this speed counts as nearly stopped.")]
    public float postPointNearStopSpeedMps = 0.45f;

    [Header("Point decision overlay")]
    public bool showPointDecisionOverlay = true;
    public Vector2 pointDecisionOverlaySize = new Vector2(1050f, 82f);
    public Vector2 pointDecisionOverlayOffset = new Vector2(0f, -34f);
    public float pointDecisionOverlayFontSize = 24f;
    public Color pointDecisionOverlayColor = new Color(0.02f, 0.08f, 0.09f, 0.82f);
    public Color pointDecisionTextColor = new Color(0.96f, 1f, 0.98f, 1f);
    [Header("State (runtime)")]
    public MatchPhase phase = MatchPhase.Disabled;
    public int serverIndex;
    public int tiebreakPointIndex;
    public int playerTiebreakPoints;
    public int aiTiebreakPoints;
    public int serviceAttempt;
    public ServiceCourt serviceCourt;
    public bool firstServerWasPlayer;
    public bool lastPointWonByPlayer;
    public bool tiebreakComplete;
    public int tiebreakWinner = -1;

    public static MatchServicePointController Active { get; private set; }
    public static event Action<MatchPhase> PhaseChanged;
    public static event Action<int, int, int> ScoreChanged;
    public static event Action<int, bool, string> PointEnded;

    private Transform originalReticleMin;
    private Transform originalReticleMax;
    private Transform serviceReticleMin;
    private Transform serviceReticleMax;
    private Rigidbody ball;
    private Transform serverSpawnPoint;
    private hitController[] hitControllers = Array.Empty<hitController>();
    private TennisAIPlayerController[] aiControllers = Array.Empty<TennisAIPlayerController>();
    private PlayerMovement playerMovement;
    private PlayerMovement aiMovement;
    private Transform serverTransform;
    private Vector3 standTarget;
    private Transform playerReceiverStartTarget;
    private float pointStateStartedAt;
    private float tossStartedAt;
    private float secondServeTransitionStartedAt;
    private float pointResetAt;
    private bool postPointBallRunActive;
    private float tossTargetHeight;
    private bool serveTouchedNet;
    private bool pointFinalized;
    private bool initializedMatch;
    private ServiceCourt pointServiceCourt;
    private bool resolvingReferences;
    private float originalSolverMaxSpeed;
    private bool solverMaxSpeedOverridden;
    private KeyCode originalCannonSpawnKey;
    private bool originalCannonAutoFire;
    private bool cannonSettingsOverridden;
    private int forcedFirstServerIndex = -1;
    private int lastHitterIndex;
    private int lastRacketContactHitterIndex = -1;
    private float lastRacketContactTime = float.NegativeInfinity;
    private RallyShotState rallyShotState;
    private bool matchAISelectionLocked;
    private bool originalAIDecisionModeSaved;
    private TennisAIPlayerController.AIDecisionMode originalAIDecisionMode;
    private bool hasPredictedServeLanding;
    private Vector3 predictedServeLanding;
    private bool spawnGuardLoggedThisPoint;
    private ServiceAttemptState frozenServiceState;
    private bool hasFrozenServiceState;
    private bool reuseFrozenPointServiceStateForSecondServe;
    private Vector3 playerTossContactAnchor;
    private bool hasPlayerTossContactAnchor;
    private GameObject serviceBoxTriggerObject;
    private BoxCollider serviceBoxTrigger;
    private bool serviceBoxTriggerEntered;
    private Vector3 serviceBoxTriggerEntryPosition;
    private bool serviceFirstBounceSeen;
    private bool serviceLandingResolved;
    private bool serviceBounceHandledByProjection;
    private bool rallyBounceHandledByProjection;
    private bool rallyProjectionLoggedForShot;
    private bool hasPredictedRallyLanding;
    private Vector3 predictedRallyLanding;
    private GameObject nearRallyCourtTriggerObject;
    private GameObject farRallyCourtTriggerObject;
    private BoxCollider nearRallyCourtTrigger;
    private BoxCollider farRallyCourtTrigger;
    private GameObject scoreboardRoot;
    private GameObject scoreboardCanvasRoot;
    private TextMeshProUGUI scoreboardPlayerNameText;
    private TextMeshProUGUI scoreboardAINameText;
    private TextMeshProUGUI scoreboardPlayerScoreText;
    private TextMeshProUGUI scoreboardAIScoreText;
    private TextMeshProUGUI scoreboardServerIndicatorText;
    private GameObject pointDecisionOverlayRoot;
    private TextMeshProUGUI pointDecisionOverlayText;

    public bool IsMatchActive => matchMode && phase != MatchPhase.Disabled;
    public bool IsServicePhase => IsMatchActive &&
        (phase == MatchPhase.PreparingPoint || phase == MatchPhase.FirstServeReady ||
         phase == MatchPhase.SecondServeTransition || phase == MatchPhase.SecondServeReady || phase == MatchPhase.TossInProgress ||
         phase == MatchPhase.ServeFlight);

    public bool IsCurrentServerController(hitController controller)
    {
        return IsServicePhase && controller != null && controller == GetServerHitController();
    }

    public bool IsPlayerServeTossActive(hitController controller)
    {
        return IsMatchActive &&
               phase == MatchPhase.TossInProgress &&
               serverIndex == 0 &&
               controller != null &&
               controller == playerHitController;
    }

    public bool TryGetPlayerServeContact(
        hitController controller,
        out HitContactConfirmation confirmation)
    {
        confirmation = default;
        if (!IsPlayerServeTossActive(controller) || ball == null || !hasFrozenServiceState)
            return false;

        float ballRadius = controller != null ? controller.GetBallContactRadius(ball) : 0.033f;
        Vector3 contactPoint = ball.position;
        if (!IsInsideFrozenPlayerServeContactVolume(contactPoint, ballRadius))
            return false;

        confirmation = HitContactConfirmation.Confirmed(contactPoint, false);
        return true;
    }

    public bool CanAccumulateServeBackswing(hitController controller)
    {
        if (!IsMatchActive || phase == MatchPhase.Rally)
            return true;
        if (controller == null || controller != GetServerHitController())
            return true;
        return phase == MatchPhase.TossInProgress;
    }

    public float CurrentServeSpeedCapMps => serviceBackswingMaxMph / 2.23694f;

    public float GetPlayerServeBackswingCapSpeed(float scale)
    {
        return Mathf.Lerp(serviceBackswingMinMph, serviceBackswingMaxMph, Mathf.Clamp01(scale)) / 2.23694f;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindFirstObjectByType<MatchServicePointController>() != null)
            return;
        new GameObject("Match Service Point Controller").AddComponent<MatchServicePointController>();
    }

    private void Awake()
    {
        if (Active != null && Active != this)
        {
            Destroy(gameObject);
            return;
        }
        Active = this;
        ResolveReferences();
    }

    private void EnsureScoreboardUI()
    {
        if (scoreboardRoot != null || !showTiebreakScoreboard)
            return;

        if (useDedicatedScoreboardCanvas && scoreboardCanvasRoot == null)
        {
            scoreboardCanvasRoot = new GameObject("Tiebreak Scoreboard Canvas", typeof(Canvas), typeof(CanvasScaler));
            scoreboardCanvas = scoreboardCanvasRoot.GetComponent<Canvas>();
            scoreboardCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            scoreboardCanvas.overrideSorting = true;
            scoreboardCanvas.sortingOrder = 80;
            CanvasScaler scaler = scoreboardCanvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }
        else if (scoreboardCanvas == null)
            scoreboardCanvas = FindFirstObjectByType<Canvas>();
        if (scoreboardCanvas == null)
            return;

        scoreboardRoot = new GameObject("Tiebreak Scoreboard", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        scoreboardRoot.transform.SetParent(scoreboardCanvas.transform, false);
        RectTransform panelRect = scoreboardRoot.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.zero;
        panelRect.pivot = Vector2.zero;
        panelRect.anchoredPosition = new Vector2(
            Mathf.Max(24f, scoreboardInset.x),
            Mathf.Max(140f, scoreboardInset.y));
        panelRect.sizeDelta = new Vector2(
            Mathf.Max(280f, scoreboardSize.x),
            Mathf.Max(72f, scoreboardSize.y));

        Image panelImage = scoreboardRoot.GetComponent<Image>();
        panelImage.color = scoreboardPanelColor;
        panelImage.raycastTarget = false;
        Sprite roundedBackground = Resources.GetBuiltinResource<Sprite>("UI/Skin/Background.psd");
        if (roundedBackground != null)
        {
            panelImage.sprite = roundedBackground;
            panelImage.type = Image.Type.Sliced;
        }
        Outline panelOutline = scoreboardRoot.AddComponent<Outline>();
        panelOutline.effectColor = new Color(0.12f, 0.72f, 0.66f, 0.48f);
        panelOutline.effectDistance = new Vector2(1.5f, -1.5f);
        panelOutline.useGraphicAlpha = true;

        GameObject scoreColumnObject = new GameObject("Score Column", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        scoreColumnObject.transform.SetParent(scoreboardRoot.transform, false);
        RectTransform scoreColumnRect = scoreColumnObject.GetComponent<RectTransform>();
        scoreColumnRect.anchorMin = new Vector2(0.78f, 0f);
        scoreColumnRect.anchorMax = new Vector2(1f, 1f);
        scoreColumnRect.offsetMin = Vector2.zero;
        scoreColumnRect.offsetMax = Vector2.zero;
        Image scoreColumnImage = scoreColumnObject.GetComponent<Image>();
        scoreColumnImage.color = scoreboardScoreColumnColor;
        scoreColumnImage.raycastTarget = false;
        if (roundedBackground != null)
        {
            scoreColumnImage.sprite = roundedBackground;
            scoreColumnImage.type = Image.Type.Sliced;
        }

        scoreboardServerIndicatorText = CreateScoreboardText(scoreboardRoot.transform, "Server Indicator", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -10f), new Vector2(20f, 28f), scoreboardFontSize, TextAlignmentOptions.Center, scoreboardServerIndicatorColor, FontStyles.Bold);
        scoreboardPlayerNameText = CreateScoreboardText(scoreboardRoot.transform, "Player Name", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(126f, -10f), new Vector2(190f, 28f), scoreboardFontSize, TextAlignmentOptions.Left, scoreboardTextColor, FontStyles.Bold);
        scoreboardAINameText = CreateScoreboardText(scoreboardRoot.transform, "AI Name", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(126f, -46f), new Vector2(190f, 28f), scoreboardFontSize, TextAlignmentOptions.Left, scoreboardTextColor, FontStyles.Bold);
        scoreboardPlayerScoreText = CreateScoreboardText(scoreboardRoot.transform, "Player Score", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-33f, -10f), new Vector2(42f, 28f), scoreboardFontSize + 2f, TextAlignmentOptions.Center, scoreboardScoreColor, FontStyles.Bold);
        scoreboardAIScoreText = CreateScoreboardText(scoreboardRoot.transform, "AI Score", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-33f, -46f), new Vector2(42f, 28f), scoreboardFontSize + 2f, TextAlignmentOptions.Center, scoreboardScoreColor, FontStyles.Bold);

        GameObject separatorObject = new GameObject("Score Separator", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        separatorObject.transform.SetParent(scoreboardRoot.transform, false);
        RectTransform separatorRect = separatorObject.GetComponent<RectTransform>();
        separatorRect.anchorMin = new Vector2(0f, 1f);
        separatorRect.anchorMax = new Vector2(1f, 1f);
        separatorRect.pivot = new Vector2(0.5f, 0.5f);
        separatorRect.offsetMin = new Vector2(12f, -39f);
        separatorRect.offsetMax = new Vector2(-12f, -38f);
        Image separatorImage = separatorObject.GetComponent<Image>();
        separatorImage.color = new Color(scoreboardTextColor.r, scoreboardTextColor.g, scoreboardTextColor.b, 0.28f);
        separatorImage.raycastTarget = false;
    }

    private TextMeshProUGUI CreateScoreboardText(
        Transform parent,
        string objectName,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPosition,
        Vector2 size,
        float fontSize,
        TextAlignmentOptions alignment,
        Color color,
        FontStyles style)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return text;
    }

    private void EnsurePointDecisionOverlayUI()
    {
        if (pointDecisionOverlayRoot != null || !showPointDecisionOverlay)
            return;

        if (scoreboardCanvas == null && useDedicatedScoreboardCanvas)
        {
            if (scoreboardCanvasRoot == null)
            {
                scoreboardCanvasRoot = new GameObject("Match Debug Canvas", typeof(Canvas), typeof(CanvasScaler));
                scoreboardCanvas = scoreboardCanvasRoot.GetComponent<Canvas>();
                scoreboardCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                scoreboardCanvas.overrideSorting = true;
                scoreboardCanvas.sortingOrder = 90;
                CanvasScaler scaler = scoreboardCanvasRoot.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;
            }
            else
                scoreboardCanvas = scoreboardCanvasRoot.GetComponent<Canvas>();
        }
        else if (scoreboardCanvas == null)
            scoreboardCanvas = FindFirstObjectByType<Canvas>();
        if (scoreboardCanvas == null)
            return;

        pointDecisionOverlayRoot = new GameObject("Point Decision Debug Overlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        pointDecisionOverlayRoot.transform.SetParent(scoreboardCanvas.transform, false);
        RectTransform panelRect = pointDecisionOverlayRoot.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 1f);
        panelRect.anchorMax = new Vector2(0.5f, 1f);
        panelRect.pivot = new Vector2(0.5f, 1f);
        panelRect.anchoredPosition = pointDecisionOverlayOffset;
        panelRect.sizeDelta = pointDecisionOverlaySize;

        Image panelImage = pointDecisionOverlayRoot.GetComponent<Image>();
        panelImage.color = pointDecisionOverlayColor;
        panelImage.raycastTarget = false;

        GameObject textObject = new GameObject("Point Decision Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(pointDecisionOverlayRoot.transform, false);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(18f, 8f);
        textRect.offsetMax = new Vector2(-18f, -8f);

        pointDecisionOverlayText = textObject.GetComponent<TextMeshProUGUI>();
        pointDecisionOverlayText.fontSize = pointDecisionOverlayFontSize;
        pointDecisionOverlayText.fontStyle = FontStyles.Bold;
        pointDecisionOverlayText.color = pointDecisionTextColor;
        pointDecisionOverlayText.alignment = TextAlignmentOptions.Center;
        pointDecisionOverlayText.textWrappingMode = TextWrappingModes.Normal;
        pointDecisionOverlayText.overflowMode = TextOverflowModes.Ellipsis;
        pointDecisionOverlayText.raycastTarget = false;
        pointDecisionOverlayRoot.SetActive(false);
    }

    private void ShowPointDecisionOverlay(string winnerName, string reason, int playerScore, int aiScore)
    {
        EnsurePointDecisionOverlayUI();
        if (pointDecisionOverlayRoot == null || pointDecisionOverlayText == null)
            return;

        pointDecisionOverlayText.text = $"{winnerName} WINS THE POINT  |  {reason}\nScore: {playerScore}-{aiScore}";
        pointDecisionOverlayRoot.SetActive(showPointDecisionOverlay && matchMode);
    }

    private void HidePointDecisionOverlay()
    {
        if (pointDecisionOverlayRoot != null)
            pointDecisionOverlayRoot.SetActive(false);
    }

    private void UpdateScoreboardUI()
    {
        EnsureScoreboardUI();
        if (scoreboardRoot == null)
            return;

        bool visible = showTiebreakScoreboard && matchMode;
        scoreboardRoot.SetActive(visible);
        if (!visible)
            return;

        scoreboardPlayerNameText.text = string.IsNullOrWhiteSpace(playerDisplayName) ? "PLAYER" : playerDisplayName;
        scoreboardAINameText.text = string.IsNullOrWhiteSpace(aiDisplayName) ? "AI" : aiDisplayName;
        scoreboardPlayerScoreText.text = playerTiebreakPoints.ToString();
        scoreboardAIScoreText.text = aiTiebreakPoints.ToString();
        scoreboardServerIndicatorText.text = "\u25CF";
        scoreboardServerIndicatorText.rectTransform.anchoredPosition = serverIndex == 0
            ? new Vector2(12f, -10f)
            : new Vector2(12f, -46f);
    }
    private void OnEnable()
    {
        hitController.PlayerBallLaunched += OnBallLaunched;
        hitController.RacketContactRegistered += OnRacketContactRegistered;
        BallController.CourtBounceApplied += OnCourtBounce;
        BallController.CollisionReported += OnBallCollision;
        BallController.CollisionObjectReported += OnBallCollisionObject;
    }

    private void OnDisable()
    {
        hitController.PlayerBallLaunched -= OnBallLaunched;
        hitController.RacketContactRegistered -= OnRacketContactRegistered;
        BallController.CourtBounceApplied -= OnCourtBounce;
        BallController.CollisionReported -= OnBallCollision;
        BallController.CollisionObjectReported -= OnBallCollisionObject;
        RestoreReticleBounds();
        RestoreServiceSolverSpeedCap();
        RestoreCannonSettings();
        SetHitGates(true);
        ClearAIServiceHolds();
        SetAIParticipation(true);
        if (Active == this)
            Active = null;
    }

    private void OnDestroy()
    {
        if (scoreboardCanvasRoot != null)
            Destroy(scoreboardCanvasRoot);
    }

    private void Update()
    {
        ResolveReferences();
        EnsureScoreboardUI();
        EnsurePointDecisionOverlayUI();
        if (forcePlayerServeKey != KeyCode.None && Input.GetKeyDown(forcePlayerServeKey))
            RestartMatchWithForcedFirstServer(0);
        if (forceAIServeKey != KeyCode.None && Input.GetKeyDown(forceAIServeKey))
            RestartMatchWithForcedFirstServer(1);
        if (toggleMatchModeKey != KeyCode.None && Input.GetKeyDown(toggleMatchModeKey))
            SetMatchMode(!matchMode);

        if (!matchMode && autoStartWhenMatchplayAI && HasMatchplayAI())
            matchMode = true;

        if (!matchMode)
        {
            if (phase != MatchPhase.Disabled)
                StopMatchMode();
            UpdateScoreboardUI();
            return;
        }

        if (!initializedMatch)
            StartMatchMode();
        TickPhase();
        UpdateScoreboardUI();
    }

    public void SetMatchMode(bool enabled)
    {
        matchMode = enabled;
        if (!enabled)
        {
            StopMatchMode();
            return;
        }
        initializedMatch = false;
    }

    public bool IsHitAllowed(
        hitController controller,
        HitContactConfirmation contactConfirmation = default)
    {
        if (!IsMatchActive)
            return true;
        if (controller == null || pointFinalized)
            return false;
        if (phase == MatchPhase.Rally)
            return controller == playerHitController || controller == GetAIHitController(receiverAI);
        if (phase == MatchPhase.TossInProgress)
        {
            if (controller != GetServerHitController())
                return false;

            if (serverIndex != 0)
                return IsBallInServiceContactWindow();

            if (contactConfirmation.confirmed)
            {
                float ballRadius = controller.GetBallContactRadius(ball);
                return IsInsideFrozenPlayerServeContactVolume(
                    contactConfirmation.contactPosition,
                    ballRadius);
            }

            return TryGetPlayerServeContact(controller, out _);
        }
        return false;
    }

    private void RestartMatchWithForcedFirstServer(int firstServer)
    {
        forcedFirstServerIndex = Mathf.Clamp(firstServer, 0, 1);
        if (initializedMatch)
            StopMatchMode();
        matchMode = true;
        initializedMatch = false;
        Log(forcedFirstServerIndex == 0 ? "F1: player forced to serve first." : "F2: AI forced to serve first.");
    }

    private void StartMatchMode()
    {
        ResolveReferences();
        if (playerSwipe == null || playerHitController == null)
            return;

        if (receiverAI != null)
        {
            if (!originalAIDecisionModeSaved)
            {
                originalAIDecisionMode = receiverAI.decisionMode;
                originalAIDecisionModeSaved = true;
            }
            receiverAI.SetMatchplayMode(true);
        }

        SynchronizeActiveAIShotTuning();

        initializedMatch = true;
        DisablePracticeCannon();
        ConfigureRallyCourtTriggers();
        firstServerWasPlayer = forcedFirstServerIndex >= 0
            ? forcedFirstServerIndex == 0
            : UnityEngine.Random.value < 0.5f;
        forcedFirstServerIndex = -1;
        serverIndex = firstServerWasPlayer ? 0 : 1;
        tiebreakPointIndex = 0;
        playerTiebreakPoints = 0;
        aiTiebreakPoints = 0;
        tiebreakComplete = false;
        tiebreakWinner = -1;
        BeginPoint();
    }

    private void SynchronizeActiveAIShotTuning()
    {
        CopySharedShotTuning(playerHitController, GetAIHitController(receiverAI));
        hitController servingAIHit = GetAIHitController(serverAI);
        if (servingAIHit != GetAIHitController(receiverAI))
            CopySharedShotTuning(playerHitController, servingAIHit);
    }

    private void CopySharedShotTuning(hitController source, hitController target)
    {
        if (source == null || target == null || source == target)
            return;

        target.manualAimWeight = source.manualAimWeight;
        target.maxManualAimAngleDeg = source.maxManualAimAngleDeg;
        target.speedBlend = source.speedBlend;
        target.minShotPower = source.minShotPower;
        target.maxShotPower = source.maxShotPower;
        target.globalPowerScale = source.globalPowerScale;
        target.minHoldAngleDeg = source.minHoldAngleDeg;
        target.maxExtraPowerFraction = source.maxExtraPowerFraction;

        target.useSituationDefaultNetClearance = source.useSituationDefaultNetClearance;
        target.lowContactHeight = source.lowContactHeight;
        target.highContactHeight = source.highContactHeight;
        target.slowIncomingSpeed = source.slowIncomingSpeed;
        target.fastIncomingSpeed = source.fastIncomingSpeed;
        target.flatNormalClearance = source.flatNormalClearance;
        target.flatSafetyClearance = source.flatSafetyClearance;
        target.topspinNormalClearance = source.topspinNormalClearance;
        target.topspinSafetyClearance = source.topspinSafetyClearance;
        target.sliceNormalClearance = source.sliceNormalClearance;
        target.sliceSafetyClearance = source.sliceSafetyClearance;
        target.flatLowPowerClearance = source.flatLowPowerClearance;
        target.topspinLowPowerClearance = source.topspinLowPowerClearance;
        target.topspinHeavyNormalClearance = source.topspinHeavyNormalClearance;
        target.topspinHeavyLowPowerClearance = source.topspinHeavyLowPowerClearance;
        target.topspinHeavySafetyClearance = source.topspinHeavySafetyClearance;
        target.topspinHeavySpinStart = source.topspinHeavySpinStart;
        target.sliceLowPowerClearance = source.sliceLowPowerClearance;
        target.highCustomAngleRiskBypassDeg = source.highCustomAngleRiskBypassDeg;
        target.useRiskScoreNetClearance = source.useRiskScoreNetClearance;
        target.compensateTopspinMagnusNetRise = source.compensateTopspinMagnusNetRise;
        target.topspinMagnusCompensationScale = source.topspinMagnusCompensationScale;
        target.topspinMaxMagnusClearanceCompensation = source.topspinMaxMagnusClearanceCompensation;
        target.topspinMinCompensatedClearance = source.topspinMinCompensatedClearance;
        target.matchClearanceToBackswingCap = source.matchClearanceToBackswingCap;
        target.clearanceLowPowerMph = source.clearanceLowPowerMph;
        target.clearanceHighPowerMph = source.clearanceHighPowerMph;
        target.flatClearance = source.flatClearance;
        target.topspinClearance = source.topspinClearance;
        target.sliceClearance = source.sliceClearance;
        target.longSliceMinTargetDistanceFromNet = source.longSliceMinTargetDistanceFromNet;
        target.longSliceFullBonusTargetDistanceFromNet = source.longSliceFullBonusTargetDistanceFromNet;
        target.longSliceExtraNetClearance = source.longSliceExtraNetClearance;
        target.longSliceMaxNetClearance = source.longSliceMaxNetClearance;

        // TennisAIPlayerController already applies its own baseline-volley
        // pressure, quality, speed and dispersion model. Avoid applying the
        // shared player volley penalty a second time to the same AI shot.
        target.applyVolleyDifficultyModel = false;

        Log($"AI shot tuning synchronized: speedBlend={target.speedBlend:F2}, manualAim={target.manualAimWeight:F2}, " +
            $"flatClear={target.flatNormalClearance:F2}/{target.flatSafetyClearance:F2}, " +
            $"topspinClear={target.topspinNormalClearance:F2}/{target.topspinSafetyClearance:F2}, " +
            $"sliceClear={target.sliceNormalClearance:F2}/{target.sliceSafetyClearance:F2}.");
    }

    private void StopMatchMode()
    {
        initializedMatch = false;
        pointFinalized = false;
        postPointBallRunActive = false;
        rallyFeedback?.StopRecording();
        HidePointDecisionOverlay();
        SetRallyCourtTriggersEnabled(false);
        FreezeBallAtSpawn();
        RestoreReticleBounds();
        SetHitGates(true);
        ClearAIServiceHolds();
        SetAIParticipation(true);
        if (receiverAI != null && originalAIDecisionModeSaved)
            receiverAI.SetDecisionMode(originalAIDecisionMode);
        originalAIDecisionModeSaved = false;
        SetPhase(MatchPhase.Disabled);
    }

    private void BeginPoint()
    {
        EnsureRallyFeedback();
        rallyFeedback?.BeginPoint(tiebreakPointIndex + 1);
        pointFinalized = false;
        postPointBallRunActive = false;
        HidePointDecisionOverlay();
        serviceAttempt = 0;
        reuseFrozenPointServiceStateForSecondServe = false;
        lastHitterIndex = serverIndex;
        rallyShotState = default;
        playerSwipe?.ResetServeBackswingCharge();
        pointServiceCourt = (tiebreakPointIndex & 1) == 0 ? ServiceCourt.Deuce : ServiceCourt.Ad;
        serviceCourt = pointServiceCourt;
        serverTransform = serverIndex == 0 ? playerHitController.transform : GetAIHitController(serverAI)?.transform;
        serverSpawnPoint = ResolveServerSpawnPoint(serverIndex);
        playerMovement = playerHitController != null ? playerHitController.GetComponent<PlayerMovement>() : null;
        aiMovement = serverAI != null ? serverAI.movement : null;
        standTarget = EnsureStandTargetOnAssignedSide(ComputeServerStandTarget(serverTransform));
        hasPredictedServeLanding = false;
        spawnGuardLoggedThisPoint = false;
        playerReceiverStartTarget = serverIndex == 1 ? ResolvePlayerReceiveStart() : null;
        pointStateStartedAt = Time.time;
        EnsureServerOnAssignedSide();
        EnsureReceiverOnAssignedSide();
        UpdateServerSpawnPointPlacement();
        ConfigureParticipantsForService();
        Log($"Point {tiebreakPointIndex + 1}: {(serverIndex == 0 ? "PLAYER" : "AI")} serving from {serviceCourt}. spawn={FormatPosition(serverSpawnPoint)}");
        SetPhase(MatchPhase.PreparingPoint);
    }

    private void FixedUpdate()
    {
        rallyFeedback?.TickFixed();
        // Resolve just before Unity physics reports the court collision.  This
        // keeps the frozen service-box decision ahead of mesh depenetration.
        if (phase == MatchPhase.ServeFlight && useNearGroundServiceProjection)
            TryResolveNearGroundServiceProjection();

        if (phase == MatchPhase.Rally && useNearGroundRallyProjection)
            TryResolveNearGroundRallyProjection("near-ground tick");
    }
    private void TickPhase()
    {
        // Let the characters reach their explicit start markers first.  Once
        // ready, normal service-line clamping still constrains their movement.
        if (IsServicePhase && phase != MatchPhase.PreparingPoint)
            ClampServerToServiceLine();

        if (phase == MatchPhase.PreparingPoint || phase == MatchPhase.FirstServeReady ||
            phase == MatchPhase.SecondServeTransition || phase == MatchPhase.SecondServeReady)
            KeepFrozenBallAtServerSpawn();

        if (phase == MatchPhase.PreparingPoint)
        {
            MoveServerIntoPosition();
            MovePlayerReceiverIntoPosition();
            if (Time.time - pointStateStartedAt >= Mathf.Max(0.5f, startPositionSnapDelaySeconds))
                SnapAIToRequiredStartPosition();
            if (IsServerInPosition() && IsPlayerReceiverInPosition())
                BeginServeAttempt();
            return;
        }

        if (phase == MatchPhase.SecondServeTransition)
        {
            SetHitGates(false);
            if (Time.time - secondServeTransitionStartedAt < Mathf.Max(0f, secondServeTransitionDelaySeconds))
                return;

            Log("Second-serve interval complete; readying the toss.");
            BeginServeAttempt();
            return;
        }

        if (phase == MatchPhase.FirstServeReady || phase == MatchPhase.SecondServeReady)
        {
            if (Time.time - pointStateStartedAt >= Mathf.Max(1f, serviceTimeLimitSeconds))
            {
                if (serviceAttempt == 0)
                    StartSecondServe("service time fault");
                else
                    AwardPoint(serverIndex == 0 ? 1 : 0, "second-service time fault");
                return;
            }

            if (serverIndex == 0)
                TryBeginPlayerToss();
            else if (Time.time - pointStateStartedAt >= aiTossDelaySeconds)
                BeginAIToss();
            return;
        }

        if (phase == MatchPhase.TossInProgress)
        {
            if (serverIndex != 0 &&
                Time.time - tossStartedAt >= aiServeContactDelaySeconds &&
                ball != null && ball.linearVelocity.y <= 0f &&
                IsBallInServiceContactWindow())
            {
                LaunchAIServe();
                return;
            }
            if (Time.time - tossStartedAt > Mathf.Max(0.5f, tossContactWindowSeconds))
                ResetTossForRetoss();
            return;
        }

        if (phase == MatchPhase.ServeFlight && useNearGroundServiceProjection)
            TryResolveNearGroundServiceProjection();

        if (phase == MatchPhase.ServeFlight || phase == MatchPhase.Rally)
            UpdateTrackedShotNetCrossing();

        if (phase == MatchPhase.ServeFlight || phase == MatchPhase.Rally)
            CheckBallOutsideCourt();

        if ((phase == MatchPhase.PointReset || phase == MatchPhase.MatchComplete) && postPointBallRunActive)
        {
            TickPostPointBallRun();
            return;
        }
    }

    private void TickPostPointBallRun()
    {
        float elapsed = Mathf.Max(0f, Time.time - pointResetAt);
        float minimumRun = Mathf.Max(Mathf.Max(0f, postPointMinimumRunSeconds), Mathf.Max(0f, pointResetDelaySeconds));
        float maximumRun = Mathf.Max(minimumRun, postPointMaximumRunSeconds);
        float speed = ball != null && !ball.isKinematic ? ball.linearVelocity.magnitude : 0f;
        bool nearGround = ball == null || ball.position.y <= 0.25f;
        bool nearStop = ball == null || ball.isKinematic || ball.IsSleeping() ||
            (nearGround && speed <= Mathf.Max(0f, postPointNearStopSpeedMps));
        bool maximumReached = elapsed >= maximumRun;

        if (elapsed < minimumRun || (!nearStop && !maximumReached))
            return;

        Log($"Post-point ball run complete: elapsed={elapsed:F2}s speed={speed:F2}m/s nearGround={nearGround} nearStop={nearStop} forcedByMaximum={maximumReached} finalPosition={(ball != null ? ball.position.ToString("F2") : "none")}.");
        postPointBallRunActive = false;
        FreezeBallAtSpawn();

        if (phase != MatchPhase.PointReset)
            return;

        serverIndex = NextServerIndex(tiebreakPointIndex + 1);
        tiebreakPointIndex++;
        BeginPoint();
    }

    private void BeginServeAttempt()
    {
        bool reusingPointServiceState = serviceAttempt == 1 &&
            reuseFrozenPointServiceStateForSecondServe && hasFrozenServiceState && frozenServiceState.valid;
        GetServerHitController()?.ResetAcceptedContactForNewServeAttempt();
        playerSwipe?.ResetServeBackswingCharge();
        hasPlayerTossContactAnchor = false;
        rallyShotState = default;
        pointStateStartedAt = Time.time;
        serveTouchedNet = false;
        serviceFirstBounceSeen = false;
        serviceLandingResolved = false;
        serviceBounceHandledByProjection = false;
        serviceBoxTriggerEntered = false;
        ConfigureParticipantsForService();
        EnsureBallAtServerSpawn();
        FreezeBallAtSpawn();
        if (reusingPointServiceState)
            ReuseFrozenPointServiceStateForSecondServe();
        else
            CaptureFrozenServiceState();
        RestoreReticleBounds();
        ConfigureServiceReticle();
        ConfigureServiceBoxTrigger();
        ApplyServiceSolverSpeedCap();
        SetHitGates(false);
        SetPhase(serviceAttempt == 0 ? MatchPhase.FirstServeReady : MatchPhase.SecondServeReady);
    }

    private void TryBeginPlayerToss()
    {
        float scroll = Input.mouseScrollDelta.y;
        if (scroll <= 0.01f || ball == null)
            return;

        float minimumApex = ResolveTossLowHeight() + Mathf.Max(0.1f, minimumTossRiseAboveLowContact);
        float maximumApex = Mathf.Max(
            minimumApex,
            ResolveTossHighHeight() + Mathf.Max(0.1f, maximumTossRiseAboveHighContact));
        tossTargetHeight = Mathf.Lerp(
            minimumApex, maximumApex,
            Mathf.Clamp01(scroll / Mathf.Max(0.1f, tossScrollForMaximumHeight)));

        // The player can move along the service bounds before tossing, so
        // freeze contact reach from the real toss origin rather than the
        // earlier point-setup spawn snapshot.
        playerTossContactAnchor = ball.position;
        hasPlayerTossContactAnchor = true;

        float gravity = Mathf.Abs(Physics.gravity.y);
        float upwardSpeed = Mathf.Sqrt(Mathf.Max(0.1f, 2f * gravity * Mathf.Max(0.05f, tossTargetHeight - ball.position.y)));
        ReleaseBallFromServerSpawn();
        ball.isKinematic = false;
        ball.useGravity = true;
        ball.linearVelocity = Vector3.up * upwardSpeed;
        ball.angularVelocity = Vector3.zero;
        ball.WakeUp();
        tossStartedAt = Time.time;
        Log($"PLAYER toss launched successfully: startY={ball.position.y:F2}m apex={tossTargetHeight:F2}m.");
        SetHitGatesForServerToss();
        SetPhase(MatchPhase.TossInProgress);
    }

    private bool IsBallInServiceContactWindow()
    {
        if (ball == null)
            return false;

        float ballRadius = playerHitController != null
            ? playerHitController.GetBallContactRadius(ball)
            : 0.033f;
        float padding = ballRadius + Mathf.Max(0f, playerServeContactHeightTolerance);
        float lowY = ResolveTossLowHeight();
        float highY = ResolveTossHighHeight();
        return ball.position.y >= lowY - padding && ball.position.y <= highY + padding;
    }

    private bool IsInsideFrozenPlayerServeContactVolume(Vector3 point, float ballRadius)
    {
        if (!hasFrozenServiceState || !frozenServiceState.isPlayerServing || !hasPlayerTossContactAnchor)
            return false;

        float padding = Mathf.Max(0f, ballRadius) + Mathf.Max(0f, playerServeContactHeightTolerance);
        float lowY = ResolveTossLowHeight();
        float highY = ResolveTossHighHeight();
        if (point.y < lowY - padding || point.y > highY + padding)
            return false;

        Vector2 pointXZ = new Vector2(point.x, point.z);
        Vector2 spawnXZ = new Vector2(playerTossContactAnchor.x, playerTossContactAnchor.z);
        float planarReach = Mathf.Max(0.1f, playerServeContactPlanarReach) + Mathf.Max(0f, ballRadius);
        return (pointXZ - spawnXZ).sqrMagnitude <= planarReach * planarReach;
    }

    private void ResetTossForRetoss()
    {
        GetServerHitController()?.ResetAcceptedContactForNewServeAttempt();
        playerSwipe?.ResetServeBackswingCharge();
        hasPlayerTossContactAnchor = false;
        rallyShotState = default;
        serveTouchedNet = false;
        serviceFirstBounceSeen = false;
        serviceLandingResolved = false;
        serviceBounceHandledByProjection = false;
        serviceBoxTriggerEntered = false;
        ConfigureParticipantsForService();
        FreezeBallAtSpawn();
        ConfigureServiceReticle();
        pointStateStartedAt = Time.time;
        SetHitGates(false);
        SetPhase(serviceAttempt == 0 ? MatchPhase.FirstServeReady : MatchPhase.SecondServeReady);
    }

    private void BeginAIToss()
    {
        if (ball == null)
            return;

        tossTargetHeight = ResolveTossLowHeight() + Mathf.Max(0.1f, aiTossApexAboveLowContact);

        float gravity = Mathf.Abs(Physics.gravity.y);
        float upwardSpeed = Mathf.Sqrt(Mathf.Max(0.1f, 2f * gravity * Mathf.Max(0.05f, tossTargetHeight - ball.position.y)));
        ReleaseBallFromServerSpawn();
        ball.isKinematic = false;
        ball.useGravity = true;
        ball.linearVelocity = Vector3.up * upwardSpeed;
        ball.angularVelocity = Vector3.zero;
        ball.WakeUp();
        tossStartedAt = Time.time;
        Log($"AI toss launched successfully: startY={ball.position.y:F2}m apex={tossTargetHeight:F2}m; planned contact after {aiServeContactDelaySeconds:F2}s.");
        SetHitGates(false);
        SetPhase(MatchPhase.TossInProgress);
    }

    private void LaunchAIServe()
    {
        if (ball == null || serverAI == null)
            return;

        ServiceBox box = GetFrozenServiceBox();
        if (!box.valid || netPoint == null)
        {
            AwardPoint(serverIndex == 0 ? 1 : 0, "missing service target references");
            return;
        }

        if (!TryBuildSafeAIServeLaunch(box, out Vector3 target, out float speed, out Vector3 spin, out float angle, out float clearance, out BaseShotType serveShotType))
        {
            RegisterServiceFault("AI service solver could not find a safe deep-corner trajectory");
            return;
        }

        Vector3 horizontal = target - ball.position;
        horizontal.y = 0f;

        ball.isKinematic = false;
        ball.useGravity = true;
        ball.linearVelocity = horizontal.normalized * (speed * Mathf.Cos(angle)) + Vector3.up * (speed * Mathf.Sin(angle));
        BallController bc = ball.GetComponent<BallController>();
        if (bc != null)
        {
            bc.SetSpin(spin);
            bc.OnHit();
        }
        RecordPredictedServeLanding(ball.position, ball.linearVelocity, spin);
        lastHitterIndex = 1;
        BeginTrackedShot(1, ball.position);
        rallyFeedback?.RegisterLaunch(1, ball, ball.position, ball.linearVelocity);
        RestoreReticleBounds();
        SetHitGates(false);
        SetPhase(MatchPhase.ServeFlight);
        Log($"AI serve hit: type={GetServeShotLabel(serveShotType)} height={ball.position.y:F2}m speed={speed:F1}m/s ({speed * 2.23694f:F0}mph) angle={angle * Mathf.Rad2Deg:F1}deg clearance={clearance:F2}m target={target} predictedLanding={FormatPredictedLanding()}.");
    }

    private bool TryBuildSafeAIServeLaunch(
        ServiceBox box,
        out Vector3 target,
        out float speed,
        out Vector3 spin,
        out float angle,
        out float usedClearance,
        out BaseShotType serveShotType)
    {
        target = default;
        speed = 0f;
        spin = default;
        angle = float.NaN;
        usedClearance = Mathf.Max(0f, serviceNetClearance);
        serveShotType = aiServeShotType;
        if (ball == null)
            return false;

        List<BaseShotType> shotTypeCandidates = BuildAIServeShotTypeCandidates();
        if (shotTypeCandidates.Count == 0)
            return false;
        serveShotType = shotTypeCandidates[0];

        Vector2 requestedRange = serviceAttempt == 0 ? firstServeSpeedRange : secondServeSpeedRange;
        float requestedMin = Mathf.Max(0f, Mathf.Min(requestedRange.x, requestedRange.y));
        float requestedMax = Mathf.Max(requestedMin, Mathf.Max(requestedRange.x, requestedRange.y));
        float maximumSpeed = Mathf.Min(CurrentServeSpeedCapMps, requestedMax);
        float minimumSpeed = Mathf.Min(maximumSpeed, requestedMin);
        if (maximumSpeed <= 0.1f)
            return false;

        float baseClearance = Mathf.Max(0f, serviceNetClearance);
        float maximumClearance = Mathf.Max(baseClearance, aiServeMaximumNetClearance);
        float clearanceStep = Mathf.Max(0f, aiServeSafetyClearanceStep);
        int clearanceAttempts = clearanceStep > 0.001f
            ? Mathf.Max(1, Mathf.CeilToInt((maximumClearance - baseClearance) / clearanceStep) + 1)
            : 1;
        int targetAttempts = Mathf.Max(1, aiServeTargetSolveAttempts);
        int speedAttempts = Mathf.Max(1, aiServeSpeedSolveAttempts);
        bool firstCornerIsHigh = UnityEngine.Random.value >= 0.5f;

        for (int targetAttempt = 0; targetAttempt < targetAttempts; targetAttempt++)
        {
            Vector3 candidateTarget = PickAIServeCornerTarget(box, targetAttempt, firstCornerIsHigh);
            Vector3 horizontal = candidateTarget - ball.position;
            horizontal.y = 0f;
            if (horizontal.sqrMagnitude < 0.001f)
                continue;

            for (int shotTypeAttempt = 0; shotTypeAttempt < shotTypeCandidates.Count; shotTypeAttempt++)
            {
                BaseShotType candidateShotType = shotTypeCandidates[shotTypeAttempt];
                Vector3 candidateSpin = BuildServeSpin(horizontal.normalized, candidateShotType);
                for (int clearanceAttempt = 0; clearanceAttempt < clearanceAttempts; clearanceAttempt++)
                {
                    float candidateClearance = Mathf.Min(maximumClearance, baseClearance + clearanceAttempt * clearanceStep);
                    for (int speedAttempt = 0; speedAttempt < speedAttempts; speedAttempt++)
                    {
                        float speed01 = speedAttempts <= 1 ? 0f : speedAttempt / (float)(speedAttempts - 1);
                        float candidateSpeed = Mathf.Lerp(maximumSpeed, minimumSpeed, speed01);
                        float candidateAngle = SolveServiceAngle(ball.position, candidateTarget, candidateSpeed, candidateSpin, candidateClearance);
                        if (!float.IsFinite(candidateAngle))
                            continue;

                        target = candidateTarget;
                        speed = candidateSpeed;
                        spin = candidateSpin;
                        angle = candidateAngle;
                        usedClearance = candidateClearance;
                        serveShotType = candidateShotType;
                        return true;
                    }
                }
            }
        }

        if (debugLogs)
        {
            string shotTypes = string.Join(", ", shotTypeCandidates.ConvertAll(GetServeShotLabel));
            Log($"AI serve solver exhausted: contact={ball.position:F2}, box={box}, speeds={minimumSpeed:F1}-{maximumSpeed:F1}m/s, " +
                $"clearance={baseClearance:F2}-{maximumClearance:F2}m, types=[{shotTypes}], targets={targetAttempts}.");
        }
        return false;
    }

    private Vector3 PickAIServeCornerTarget(ServiceBox box, int targetAttempt, bool firstCornerIsHigh)
    {
        Vector2 targetDepthRange = serviceAttempt == 0
            ? aiFirstServeTargetDepthRange01
            : aiSecondServeTargetDepthRange01;
        float depthMin01 = Mathf.Clamp01(Mathf.Min(targetDepthRange.x, targetDepthRange.y));
        float depthMax01 = Mathf.Clamp01(Mathf.Max(targetDepthRange.x, targetDepthRange.y));
        // Start near the deep service-line corners, then deliberately vary
        // across the configured deep range if an earlier target is unsuitable.
        // It never falls back to a short, near-net target just to make a solve.
        float deepRange01 = (targetAttempt % 4) switch
        {
            0 => 0.90f,
            1 => 0.74f,
            2 => 0.98f,
            _ => 0.60f
        };
        float targetDepth01 = Mathf.Lerp(depthMin01, depthMax01, deepRange01);
        float targetDepth = Mathf.Lerp(box.netLineX, box.serviceLineX, targetDepth01);

        float boxWidth = Mathf.Abs(box.lateralMax - box.lateralMin);
        float lateralPadding = Mathf.Min(Mathf.Max(0f, aiServeTargetLateralInset), boxWidth * 0.22f);
        float lateralMin = box.lateralMin + lateralPadding;
        float lateralMax = box.lateralMax - lateralPadding;
        if (lateralMax < lateralMin)
        {
            float middle = (box.lateralMin + box.lateralMax) * 0.5f;
            lateralMin = middle;
            lateralMax = middle;
        }

        float cornerMin01 = Mathf.Clamp01(Mathf.Min(aiServeCornerDistanceFromSideline01.x, aiServeCornerDistanceFromSideline01.y));
        float cornerMax01 = Mathf.Clamp01(Mathf.Max(aiServeCornerDistanceFromSideline01.x, aiServeCornerDistanceFromSideline01.y));
        float inward01 = UnityEngine.Random.Range(cornerMin01, cornerMax01);
        bool highCorner = (targetAttempt & 1) == 0 ? firstCornerIsHigh : !firstCornerIsHigh;
        float targetLateral = highCorner
            ? Mathf.Lerp(lateralMax, lateralMin, inward01)
            : Mathf.Lerp(lateralMin, lateralMax, inward01);
        return new Vector3(targetDepth, 0f, targetLateral);
    }

    private float SolveServiceAngle(Vector3 start, Vector3 target, float speed, Vector3 spin, float intendedClearance)
    {
        if (playerHitController == null || playerHitController.solverComponent == null || netPoint == null)
            return float.NaN;
        DragTrajectorySolver traj = playerHitController.solverComponent.traj;
        if (traj == null)
            return float.NaN;

        Vector3 horizontal = target - start;
        horizontal.y = 0f;
        if (horizontal.sqrMagnitude < 0.001f)
            return float.NaN;
        Vector3 direction = horizontal.normalized;
        if (Mathf.Abs(direction.x) < 0.001f)
            return float.NaN;

        // Solve in distance along the actual diagonal serve direction. Using
        // court X alone underestimates both the target and net distances.
        Vector2 start2D = new Vector2(0f, start.y);
        float netDistance = (netPoint.position.x - start.x) / direction.x;
        float targetDistance = horizontal.magnitude;
        if (!float.IsFinite(netDistance) || netDistance <= 0f || targetDistance <= netDistance)
            return float.NaN;

        Vector3 topspinAxis = Vector3.Cross(Vector3.up, direction).normalized;
        float signedSpinRadPerSecond = Vector3.Dot(spin, topspinAxis);
        Vector3 solverSpin = Vector3.back * signedSpinRadPerSecond;
        float requiredNetY = netHeight + Mathf.Max(0f, intendedClearance);
        // At a fixed speed and target distance, landing height changes
        // monotonically with launch angle over this practical serve range.
        // Bracketing then refining its zero finds the exact landing angle,
        // including a negative (downward) angle from a high serve contact.
        // +/-45 keeps enough horizontal speed for the solver to reach a
        // service-box target. At extreme 60 degree loft, drag can stop the
        // simulated ball before it ever reaches the target, which is not a
        // useful bracket for a serve.
        const float minAngleDegrees = -45f;
        const float maxAngleDegrees = 45f;
        float lower = minAngleDegrees * Mathf.Deg2Rad;
        float upper = maxAngleDegrees * Mathf.Deg2Rad;
        float lowerLandingY = traj.GetHeightAtX(start2D, speed, lower, targetDistance, solverSpin);
        float upperLandingY = traj.GetHeightAtX(start2D, speed, upper, targetDistance, solverSpin);
        if (!float.IsFinite(lowerLandingY) || !float.IsFinite(upperLandingY) ||
            lowerLandingY > 0f || upperLandingY < 0f)
        {
            return float.NaN;
        }

        int iterations = Mathf.Clamp(aiServeLandingAngleRefinementIterations, 10, 24);
        for (int i = 0; i < iterations; i++)
        {
            float middle = (lower + upper) * 0.5f;
            float middleLandingY = traj.GetHeightAtX(start2D, speed, middle, targetDistance, solverSpin);
            if (!float.IsFinite(middleLandingY))
                return float.NaN;

            if (middleLandingY < 0f)
            {
                lower = middle;
                lowerLandingY = middleLandingY;
            }
            else
            {
                upper = middle;
                upperLandingY = middleLandingY;
            }
        }

        float landingAngle = (lower + upper) * 0.5f;
        float netY = traj.GetHeightAtX(start2D, speed, landingAngle, netDistance, solverSpin);
        if (!float.IsFinite(netY) || netY < requiredNetY)
            return float.NaN;
        return landingAngle;
    }

    private bool TryResolveHitterIndex(hitController hitter, out int hitterIndex)
    {
        hitterIndex = -1;
        if (hitter == null)
            return false;
        if (hitter == playerHitController)
        {
            hitterIndex = 0;
            return true;
        }

        hitController activeAIHitController = GetAIHitController(receiverAI);
        if (hitter == activeAIHitController || hitter == GetAIHitController(serverAI))
        {
            hitterIndex = 1;
            return true;
        }
        return false;
    }

    private void BeginTrackedShot(int hitterIndex, Vector3 contactPoint)
    {
        float netX = netPoint != null ? netPoint.position.x : 0f;
        Transform hitterTransform = hitterIndex == 0
            ? playerHitController != null ? playerHitController.transform : null
            : GetAIHitController(receiverAI) != null ? GetAIHitController(receiverAI).transform
            : GetAIHitController(serverAI) != null ? GetAIHitController(serverAI).transform : null;
        float originSideSign = hitterTransform != null
            ? Mathf.Sign(hitterTransform.position.x - netX)
            : Mathf.Sign(contactPoint.x - netX);
        if (Mathf.Abs(originSideSign) < 0.01f)
            originSideSign = Mathf.Sign(contactPoint.x - netX);
        if (Mathf.Abs(originSideSign) < 0.01f)
            originSideSign = hitterIndex == serverIndex && serverTransform != null
                ? Mathf.Sign(serverTransform.position.x - netX)
                : (hitterIndex == 0 ? -1f : 1f);

        lastHitterIndex = hitterIndex;
        rallyBounceHandledByProjection = false;
        rallyProjectionLoggedForShot = false;
        hasPredictedRallyLanding = false;
        rallyShotState = new RallyShotState
        {
            valid = true,
            hitterIndex = hitterIndex,
            originSideSign = originSideSign,
            opponentSideSign = -originSideSign,
            contactPoint = contactPoint
        };
        Log($"Shot ownership: hitter={(hitterIndex == 0 ? "PLAYER" : "AI")} contact={contactPoint} originSide={originSideSign:+0;-0} opponentSide={-originSideSign:+0;-0}.");
    }

    private void MarkTrackedShotLegalFirstBounce(Vector3 bouncePoint)
    {
        if (!rallyShotState.valid)
            BeginTrackedShot(lastHitterIndex, bouncePoint);
        rallyShotState.crossedNet = true;
        rallyShotState.firstBounceSeen = true;
        rallyShotState.firstBounceInsideCourt = true;
        rallyShotState.firstBounceOnOpponentSide = true;
        rallyShotState.firstBouncePoint = bouncePoint;
    }

    private void UpdateTrackedShotNetCrossing()
    {
        if (!rallyShotState.valid || ball == null || netPoint == null || rallyShotState.crossedNet)
            return;
        float ballSideSign = Mathf.Sign(ball.position.x - netPoint.position.x);
        if (Mathf.Abs(ballSideSign) > 0.01f && ballSideSign == rallyShotState.opponentSideSign)
            rallyShotState.crossedNet = true;
    }

    private int TrackedHitterIndex => rallyShotState.valid ? rallyShotState.hitterIndex : lastHitterIndex;
    private int OpponentOfTrackedHitter() => TrackedHitterIndex == 0 ? 1 : 0;

    private void OnRacketContactRegistered(hitController hitter, Rigidbody contactedBody, Vector3 contactPoint)
    {
        if (!IsMatchActive || contactedBody == null || contactedBody != ball)
            return;
        if (!TryResolveHitterIndex(hitter, out int hitterIndex))
            return;

        lastRacketContactHitterIndex = hitterIndex;
        lastRacketContactTime = Time.time;
        rallyFeedback?.RegisterReceiverContact(hitterIndex, contactPoint);
        BeginTrackedShot(hitterIndex, contactPoint);
    }

    private void OnBallLaunched(Rigidbody launchedBody, Vector3 startPosition, Vector3 launchVelocity)
    {
        if (!IsMatchActive || launchedBody == null || launchedBody != ball)
            return;

        hitController hitter = hitController.LastLaunchController;
        if (TryResolveHitterIndex(hitter, out int hitterIndex))
        {
            BeginTrackedShot(hitterIndex, startPosition);
            rallyFeedback?.RegisterLaunch(hitterIndex, launchedBody, startPosition, launchVelocity);
        }

        if (phase == MatchPhase.TossInProgress && hitter == GetServerHitController())
        {
            BallController controller = launchedBody.GetComponent<BallController>();
            RecordPredictedServeLanding(startPosition, launchVelocity, controller != null ? controller.spinRadPerSecond : Vector3.zero);
            Log($"PLAYER serve hit: height={launchedBody.position.y:F2}m speed={launchVelocity.magnitude:F1}m/s ({launchVelocity.magnitude * 2.23694f:F0}mph) predictedLanding={FormatPredictedLanding()}.");
            if (debugLogs)
                StartCoroutine(LogPlayerServeFirstFixedUpdate(launchedBody));
            RestoreReticleBounds();
            SetPhase(MatchPhase.ServeFlight);
            SetHitGates(false);
            if (serverIndex == 0 && receiverAI != null)
                receiverAI.BeginServeReturnPreparation();
        }
    }

    private System.Collections.IEnumerator LogPlayerServeFirstFixedUpdate(Rigidbody launchedBody)
    {
        yield return new WaitForFixedUpdate();
        if (launchedBody == null)
            yield break;

        BallController controller = launchedBody.GetComponent<BallController>();
        Log($"PLAYER serve first FixedUpdate: position={launchedBody.position} velocity={launchedBody.linearVelocity} " +
            $"speed={launchedBody.linearVelocity.magnitude:F2}m/s spin={(controller != null ? controller.spinRadPerSecond : Vector3.zero)}.");
    }

    private void OnBallCollision(Rigidbody collidedBody, string objectName, Vector3 contactPoint, Vector3 relativeVelocity)
    {
        if (!IsMatchActive || collidedBody == null || collidedBody != ball || string.IsNullOrEmpty(objectName))
            return;
        if (!IsNetCollisionName(objectName))
            return;
        HandlePhysicalNetContact(contactPoint, objectName);
    }

    private void OnBallCollisionObject(Rigidbody collidedBody, GameObject collisionObject, Vector3 contactPoint, Vector3 relativeVelocity)
    {
        if (!IsMatchActive || pointFinalized || collidedBody == null || collidedBody != ball || collisionObject == null)
            return;
        if (phase != MatchPhase.ServeFlight && phase != MatchPhase.Rally)
            return;
        if (IsNetCollisionObject(collisionObject))
        {
            HandlePhysicalNetContact(contactPoint, collisionObject.name);
            return;
        }
        if (!TryResolveStruckParticipant(collisionObject, out int struckParticipantIndex))
            return;

        bool isImmediateHitterOverlap = struckParticipantIndex == lastRacketContactHitterIndex &&
            Time.time - lastRacketContactTime <= Mathf.Max(0f, racketContactBodyCollisionGraceSeconds);
        if (isImmediateHitterOverlap)
        {
            Log($"Ignored immediate post-racket body overlap: participant={(struckParticipantIndex == 0 ? "PLAYER" : "AI")} object={collisionObject.name} point={contactPoint}.");
            return;
        }

        int winnerIndex = struckParticipantIndex == 0 ? 1 : 0;
        Log($"Body collision: struck={(struckParticipantIndex == 0 ? "PLAYER" : "AI")} object={collisionObject.name} point={contactPoint} relativeVelocity={relativeVelocity} winner={(winnerIndex == 0 ? "PLAYER" : "AI")}.");
        AwardPoint(winnerIndex, $"ball struck {(struckParticipantIndex == 0 ? "player" : "AI")} body before a return");
    }

    private void HandlePhysicalNetContact(Vector3 contactPoint, string sourceName)
    {
        if (pointFinalized)
            return;

        if (phase == MatchPhase.ServeFlight)
        {
            serveTouchedNet = true;
            Log($"Physical service-net contact: object={sourceName} point={contactPoint}.");
            return;
        }

        if (phase != MatchPhase.Rally)
            return;

        if (!rallyShotState.valid)
            BeginTrackedShot(lastHitterIndex, ball != null ? ball.position : contactPoint);

        rallyShotState.physicalNetTouched = true;
        rallyShotState.physicalNetTouchPoint = contactPoint;
        float netX = netPoint != null ? netPoint.position.x : 0f;
        float impactSide = Mathf.Sign(contactPoint.x - netX);
        bool clearlyStillOnHitterSide = Mathf.Abs(impactSide) > 0.01f && impactSide == rallyShotState.originSideSign;
        Log($"Physical rally-net contact: hitter={(TrackedHitterIndex == 0 ? "PLAYER" : "AI")} object={sourceName} point={contactPoint} crossed={rallyShotState.crossedNet} impactOnHitterSide={clearlyStillOnHitterSide}.");

        // A real collision on the hitter's side is authoritative: do not let
        // a solver projection subsequently award a fictitious bounce across
        // the net. Contacts exactly on the cord are held until their actual
        // bounce establishes whether the ball made it over.
        if (clearlyStillOnHitterSide)
            AwardPoint(OpponentOfTrackedHitter(), "rally ball physically hit the net before crossing");
    }

    private static bool IsNetCollisionName(string name)
    {
        return !string.IsNullOrEmpty(name) && name.IndexOf("net", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private bool IsNetCollisionObject(GameObject collisionObject)
    {
        for (Transform candidate = collisionObject != null ? collisionObject.transform : null;
             candidate != null;
             candidate = candidate.parent)
        {
            if (candidate == netPoint || IsNetCollisionName(candidate.name))
                return true;
        }

        return netPoint != null && collisionObject != null && netPoint.IsChildOf(collisionObject.transform);
    }

    private bool TryResolveStruckParticipant(GameObject collisionObject, out int participantIndex)
    {
        participantIndex = -1;
        if (collisionObject == null)
            return false;

        Transform collisionTransform = collisionObject.transform;
        string collisionName = collisionObject.name.ToLowerInvariant();
        if (collisionName.Contains("racket") || collisionName.Contains("racquet") || collisionName.Contains("hitzone") || collisionName.Contains("hit zone"))
            return false;

        hitController activeAIHitController = GetAIHitController(receiverAI);
        if (IsHitZoneTransform(collisionTransform, playerHitController) ||
            IsHitZoneTransform(collisionTransform, activeAIHitController))
            return false;

        PlayerMovement owningMovement = collisionObject.GetComponentInParent<PlayerMovement>();
        if (owningMovement != null)
        {
            if (playerMovement != null && owningMovement == playerMovement)
            {
                participantIndex = 0;
                return true;
            }
            if (receiverAI != null && receiverAI.movement != null && owningMovement == receiverAI.movement)
            {
                participantIndex = 1;
                return true;
            }
        }

        hitController owningHitController = collisionObject.GetComponentInParent<hitController>();
        if (owningHitController != null)
        {
            if (owningHitController == playerHitController)
            {
                participantIndex = 0;
                return true;
            }
            if (owningHitController == activeAIHitController)
            {
                participantIndex = 1;
                return true;
            }
        }

        if (BelongsToParticipant(collisionTransform, playerHitController != null ? playerHitController.transform : null) ||
            BelongsToParticipant(collisionTransform, playerMovement != null ? playerMovement.transform : null))
        {
            participantIndex = 0;
            return true;
        }
        if (BelongsToParticipant(collisionTransform, receiverAI != null ? receiverAI.transform : null) ||
            BelongsToParticipant(collisionTransform, activeAIHitController != null ? activeAIHitController.transform : null))
        {
            participantIndex = 1;
            return true;
        }

        return false;
    }

    private static bool IsHitZoneTransform(Transform candidate, hitController controller)
    {
        Transform hitZoneTransform = controller != null && controller.hitZone != null ? controller.hitZone.transform : null;
        return hitZoneTransform != null && (candidate == hitZoneTransform || candidate.IsChildOf(hitZoneTransform));
    }

    private static bool BelongsToParticipant(Transform candidate, Transform participantRoot)
    {
        return candidate != null && participantRoot != null &&
            (candidate == participantRoot || candidate.IsChildOf(participantRoot));
    }

    private bool TryResolveNearGroundServiceProjection()
    {
        if (!IsMatchActive || phase != MatchPhase.ServeFlight || serviceLandingResolved ||
            ball == null || !hasFrozenServiceState || ball.isKinematic)
            return false;

        Vector3 velocity = ball.linearVelocity;
        Vector3 horizontalVelocity = velocity;
        horizontalVelocity.y = 0f;
        if (ball.position.y > Mathf.Max(0.1f, serviceNearGroundProjectionHeight) ||
            velocity.y >= -0.02f || horizontalVelocity.sqrMagnitude < 0.01f)
            return false;

        ShotSolverComponent component = playerHitController != null ? playerHitController.solverComponent : null;
        BallController controller = ball.GetComponent<BallController>();
        Vector3 spin = controller != null ? controller.spinRadPerSecond : Vector3.zero;
        float speed = velocity.magnitude;
        float theta = Mathf.Atan2(velocity.y, horizontalVelocity.magnitude);
        Vector3 solverSpin = velocity.x < 0f ? new Vector3(-spin.x, spin.y, -spin.z) : spin;
        float landingDistance = component != null && component.solver != null
            ? component.solver.PredictLandingX(new Vector2(0f, ball.position.y), speed, theta, solverSpin)
            : float.NaN;
        if (!float.IsFinite(landingDistance) || landingDistance < 0f)
            return false;

        Vector3 projectedLanding = ball.position + horizontalVelocity.normalized * landingDistance;
        projectedLanding.y = 0f;
        bool validServiceBox = IsServeBounceValid(projectedLanding);
        ServiceBox activeBox = GetFrozenServiceBox();
        serviceLandingResolved = true;
        serviceFirstBounceSeen = true;

        Log($"Near-ground service projection: current={ball.position} velocity={velocity} projectedLanding={projectedLanding} " +
            $"validServiceBox={validServiceBox} boxX=[{activeBox.depthMin:F2},{activeBox.depthMax:F2}] " +
            $"boxZ=[{activeBox.lateralMin:F2},{activeBox.lateralMax:F2}] triggerEntered={serviceBoxTriggerEntered}.");

        if (serveTouchedNet && validServiceBox)
        {
            Log("Near-ground projection detected a service let.");
            ResetTossForRetoss();
            return true;
        }

        if (validServiceBox)
        {
            MarkTrackedShotLegalFirstBounce(projectedLanding);
            serviceBounceHandledByProjection = true;
            DeactivateServiceBoxTrigger();
            RestoreReticleBounds();
            RestoreServiceSolverSpeedCap();
            SetPhase(MatchPhase.Rally);
            SetHitGates(true);
            ClearAIServiceHolds();
            SetAIParticipation(true);
            TennisAIPlayerController rallyAI = serverIndex == 0 ? receiverAI : serverAI;
            if (rallyAI != null)
            {
                rallyAI.BeginRallyAfterService();
                Log(serverIndex == 0
                    ? "Near-ground valid player serve: AI receiver re-armed and rally enabled."
                    : "Near-ground valid AI serve: AI server re-armed for the next rally ball.");
            }
            else
                Log("Near-ground valid serve: rally enabled, but no active AI controller was available to re-arm.");
            Log("Transitioned to RALLY after near-ground projected valid serve.");
        }
        else
        {
            RegisterServiceFault("serve projected outside the service box");
        }

        return true;
    }

    private bool TryResolveNearGroundRallyProjection(string source)
    {
        if (!IsMatchActive || phase != MatchPhase.Rally || pointFinalized ||
            rallyShotState.firstBounceSeen || rallyProjectionLoggedForShot ||
            ball == null || ball.isKinematic)
            return false;
        if (rallyShotState.physicalNetTouched)
            return false;

        Vector3 velocity = ball.linearVelocity;
        Vector3 horizontalVelocity = velocity;
        horizontalVelocity.y = 0f;
        if (ball.position.y > Mathf.Max(0.1f, rallyNearGroundProjectionHeight) ||
            velocity.y >= -0.02f || horizontalVelocity.sqrMagnitude < 0.01f)
            return false;

        ShotSolverComponent component = playerHitController != null ? playerHitController.solverComponent : null;
        BallController controller = ball.GetComponent<BallController>();
        Vector3 spin = controller != null ? controller.spinRadPerSecond : Vector3.zero;
        float speed = velocity.magnitude;
        float theta = Mathf.Atan2(velocity.y, horizontalVelocity.magnitude);
        Vector3 solverSpin = velocity.x < 0f ? new Vector3(-spin.x, spin.y, -spin.z) : spin;
        float landingDistance = component != null && component.solver != null
            ? component.solver.PredictLandingX(new Vector2(0f, ball.position.y), speed, theta, solverSpin)
            : float.NaN;

        if (!float.IsFinite(landingDistance) || landingDistance < 0f)
        {
            float gravity = Mathf.Max(0.01f, Mathf.Abs(Physics.gravity.y));
            float discriminant = velocity.y * velocity.y + 2f * gravity * Mathf.Max(0f, ball.position.y);
            float timeToGround = (velocity.y + Mathf.Sqrt(Mathf.Max(0f, discriminant))) / gravity;
            landingDistance = horizontalVelocity.magnitude * Mathf.Max(0f, timeToGround);
        }
        if (!float.IsFinite(landingDistance) || landingDistance < 0f)
            return false;

        predictedRallyLanding = ball.position + horizontalVelocity.normalized * landingDistance;
        predictedRallyLanding.y = 0f;
        hasPredictedRallyLanding = true;
        rallyProjectionLoggedForShot = true;
        bool projectedInsideCourt = IsInsideCourt(predictedRallyLanding);

        if (logRallyBounceDetection)
            Log($"Rally first-bounce projection ({source}): current={ball.position} velocity={velocity} projected={predictedRallyLanding} insideSinglesCourt={projectedInsideCourt}.");

        // An inside projection can safely resolve the first bounce just before
        // contact.  An outside projection remains provisional: the thin court
        // trigger or physical court collision gets the final chance to call it.
        if (!projectedInsideCourt)
            return false;

        rallyBounceHandledByProjection = true;
        HandleRallyBounce(predictedRallyLanding);
        return true;
    }

    private void OnCourtBounce(Rigidbody bouncedBody, string courtName, Vector3 contactPoint, Vector3 velocityIn, Vector3 velocityOut)
    {
        if (!IsMatchActive || bouncedBody == null || bouncedBody != ball)
            return;

        if (phase == MatchPhase.Rally && serviceBounceHandledByProjection)
        {
            serviceBounceHandledByProjection = false;
            Log($"Ignoring physical first-serve bounce after near-ground projection: position={contactPoint}.");
            return;
        }

        if (phase == MatchPhase.Rally && rallyBounceHandledByProjection)
        {
            rallyBounceHandledByProjection = false;
            if (logRallyBounceDetection)
                Log($"Ignoring physical rally bounce already resolved by trigger/projection: position={contactPoint}.");
            return;
        }

        if (phase == MatchPhase.ServeFlight)
        {
            if (serviceFirstBounceSeen)
                return;
            if (velocityIn.y > 0.05f)
            {
                Log($"Ignoring non-descending service contact: velocityIn={velocityIn}.");
                return;
            }
            serviceFirstBounceSeen = true;
            bool validServiceBox = IsServeBounceValid(contactPoint);
            ServiceBox activeBox = GetFrozenServiceBox();
            float predictionError = hasPredictedServeLanding ? Vector2.Distance(new Vector2(contactPoint.x, contactPoint.z), new Vector2(predictedServeLanding.x, predictedServeLanding.z)) : float.NaN;
            Log($"Serve bounce: actual={contactPoint} predicted={FormatPredictedLanding()} predictionError={predictionError:F2}m " +
                $"server={serverTransform.position} netX={activeBox.netLineX:F2} validServiceBox={validServiceBox} netTouched={serveTouchedNet} " +
                $"boxX=[{activeBox.depthMin:F2},{activeBox.depthMax:F2}] boxZ=[{activeBox.lateralMin:F2},{activeBox.lateralMax:F2}] " +
                $"triggerEntered={serviceBoxTriggerEntered} triggerEntry={serviceBoxTriggerEntryPosition}.");
            if (serveTouchedNet && validServiceBox)
            {
                Log("Serve let; replaying the same service attempt.");
                ResetTossForRetoss();
            }
            else if (validServiceBox)
            {
                MarkTrackedShotLegalFirstBounce(contactPoint);
                DeactivateServiceBoxTrigger();
                RestoreReticleBounds();
                RestoreServiceSolverSpeedCap();
                SetPhase(MatchPhase.Rally);
                SetHitGates(true);
                ClearAIServiceHolds();
                SetAIParticipation(true);
                TennisAIPlayerController rallyAI = serverIndex == 0 ? receiverAI : serverAI;
                if (rallyAI != null)
                {
                    rallyAI.BeginRallyAfterService();
                    Log(serverIndex == 0
                        ? "Valid player serve: AI receiver re-armed and rally enabled."
                        : "Valid AI serve: AI server re-armed for the next rally ball.");
                }
                else
                    Log("Valid serve: rally enabled, but no active AI controller was available to re-arm.");
                Log("Transitioned to RALLY after valid serve.");
            }
            else
                RegisterServiceFault("serve landed outside the service box");
            return;
        }

        if (phase != MatchPhase.Rally)
            return;
        HandleRallyBounce(contactPoint);
    }

    private void HandleRallyBounce(Vector3 contactPoint)
    {
        if (!rallyShotState.valid)
            BeginTrackedShot(lastHitterIndex, contactPoint);

        bool insideCourt = IsInsideCourt(contactPoint);
        float netX = netPoint != null ? netPoint.position.x : 0f;
        float bounceSideSign = Mathf.Sign(contactPoint.x - netX);
        bool onOpponentSide = Mathf.Abs(bounceSideSign) > 0.01f &&
            bounceSideSign == rallyShotState.opponentSideSign;

        if (!rallyShotState.firstBounceSeen)
        {
            rallyShotState.firstBounceSeen = true;
            rallyShotState.firstBounceInsideCourt = insideCourt;
            rallyShotState.firstBounceOnOpponentSide = onOpponentSide;
            rallyShotState.firstBouncePoint = contactPoint;
            if (onOpponentSide)
                rallyShotState.crossedNet = true;

            Log($"Tracked first bounce: hitter={(TrackedHitterIndex == 0 ? "PLAYER" : "AI")} point={contactPoint} inside={insideCourt} opponentSide={onOpponentSide} crossedNet={rallyShotState.crossedNet} physicalNet={rallyShotState.physicalNetTouched}.");

            if (!insideCourt)
                AwardPoint(OpponentOfTrackedHitter(), "rally first bounce landed out");
            else if (rallyShotState.physicalNetTouched && !onOpponentSide)
                AwardPoint(OpponentOfTrackedHitter(), "rally ball physically hit the net and fell back on hitter side");
            else if (!onOpponentSide)
                AwardPoint(OpponentOfTrackedHitter(), "shot failed to cross net and bounced on hitter side");
            return;
        }

        if (rallyShotState.firstBounceInsideCourt && rallyShotState.firstBounceOnOpponentSide)
            AwardPoint(TrackedHitterIndex, "second bounce after legal opponent-side first bounce");
        else
            AwardPoint(OpponentOfTrackedHitter(), "second bounce after illegal first bounce");
    }

    private void RegisterServiceFault(string reason)
    {
        SetHitGates(false);
        if (serviceAttempt == 0)
        {
            FreezeBallAtSpawn();
            StartSecondServe(reason);
        }
        else
            AwardPoint(serverIndex == 0 ? 1 : 0, "second serve fault: " + reason);
    }

    private void StartSecondServe(string reason)
    {
        serviceAttempt = 1;
        reuseFrozenPointServiceStateForSecondServe = hasFrozenServiceState && frozenServiceState.valid;
        if (reuseFrozenPointServiceStateForSecondServe)
        {
            if (frozenServiceState.court != pointServiceCourt)
                Log($"Second-serve point-state guard restored frozen court {frozenServiceState.court} to point court {pointServiceCourt}.");
            serviceCourt = pointServiceCourt;
            standTarget = EnsureStandTargetOnAssignedSide(frozenServiceState.serverStartPos);
        }
        else
        {
            if (serviceCourt != pointServiceCourt)
                Log($"Second-serve court guard restored {serviceCourt} to frozen point court {pointServiceCourt}.");
            serviceCourt = pointServiceCourt;
            standTarget = EnsureStandTargetOnAssignedSide(ComputeServerStandTarget(serverTransform));
        }
        RestoreServiceSolverSpeedCap();
        FreezeBallAtSpawn();
        secondServeTransitionStartedAt = Time.time;
        SetHitGates(false);
        SetPhase(MatchPhase.SecondServeTransition);
        Log($"First-serve fault; holding {Mathf.Max(0f, secondServeTransitionDelaySeconds):F2}s before second serve: {reason}");
    }

    private void AwardPoint(int winnerIndex, string reason)
    {
        if (pointFinalized)
            return;

        MatchPhase decisionPhase = phase;
        int previousPlayerScore = playerTiebreakPoints;
        int previousAIScore = aiTiebreakPoints;
        string winnerName = winnerIndex == 0 ? "PLAYER" : "AI";
        string loserName = winnerIndex == 0 ? "AI" : "PLAYER";
        string trackedHitterName = TrackedHitterIndex == 0 ? "PLAYER" : "AI";
        string firstBouncePosition = rallyShotState.firstBounceSeen
            ? rallyShotState.firstBouncePoint.ToString("F2")
            : "none";
        string ballPosition = ball != null ? ball.position.ToString("F2") : "none";
        string ballVelocity = ball != null ? ball.linearVelocity.ToString("F2") : "none";

        pointFinalized = true;
        rallyFeedback?.CompletePoint(reason);
        lastPointWonByPlayer = winnerIndex == 0;
        if (lastPointWonByPlayer) playerTiebreakPoints++; else aiTiebreakPoints++;
        ShowPointDecisionOverlay(winnerName, reason, playerTiebreakPoints, aiTiebreakPoints);

        Log($"POINT DECISION | winner={winnerName} loser={loserName} reason=\"{reason}\" " +
            $"phase={decisionPhase} point={tiebreakPointIndex + 1} score={previousPlayerScore}-{previousAIScore}->{playerTiebreakPoints}-{aiTiebreakPoints} " +
            $"server={(serverIndex == 0 ? "PLAYER" : "AI")} serveAttempt={serviceAttempt + 1} trackedHitter={trackedHitterName} " +
            $"shotValid={rallyShotState.valid} crossedNet={rallyShotState.crossedNet} firstBounceSeen={rallyShotState.firstBounceSeen} " +
            $"firstBounceInside={rallyShotState.firstBounceInsideCourt} firstBounceOpponentSide={rallyShotState.firstBounceOnOpponentSide} " +
            $"firstBounce={firstBouncePosition} ball={ballPosition} velocity={ballVelocity}.");

        PointEnded?.Invoke(winnerIndex, lastPointWonByPlayer, reason);
        ScoreChanged?.Invoke(playerTiebreakPoints, aiTiebreakPoints, tiebreakPointIndex);
        UpdateScoreboardUI();
        bool wonTiebreak = HasTiebreakWinner();
        RestoreReticleBounds();
        RestoreServiceSolverSpeedCap();
        RestoreCannonSettings();
        SetHitGates(false);
        ClearAIServiceHolds();
        SetAIParticipation(false);
        HoldAIAtInbetweenPoint();
        pointResetAt = Time.time;
        postPointBallRunActive = true;
        if (wonTiebreak)
        {
            DeactivateServiceBoxTrigger();
            hasFrozenServiceState = false;
            tiebreakComplete = true;
            tiebreakWinner = winnerIndex;
            SetPhase(MatchPhase.MatchComplete);
            Log($"Tiebreak complete: winner={(winnerIndex == 0 ? "player" : "AI")} final score={playerTiebreakPoints}-{aiTiebreakPoints}.");
            return;
        }
        DeactivateServiceBoxTrigger();
        hasFrozenServiceState = false;
        SetPhase(MatchPhase.PointReset);
    }

    private bool HasTiebreakWinner()
    {
        int target = Mathf.Max(1, tiebreakPointsToWin);
        int high = Mathf.Max(playerTiebreakPoints, aiTiebreakPoints);
        int lead = Mathf.Abs(playerTiebreakPoints - aiTiebreakPoints);
        return high >= target && (!tiebreakWinByTwo || lead >= 2);
    }
    private void CheckBallOutsideCourt()
    {
        if (ball == null || phase != MatchPhase.Rally || ball.isKinematic || IsInsideCourt(ball.position))
            return;

        if (!rallyShotState.firstBounceSeen)
        {
            // Crossing a baseline or sideline while airborne is not itself the
            // first-bounce decision.  Give the near-ground projection, thin
            // court trigger, or physical court collision time to resolve it.
            if (ball.linearVelocity.y >= -0.02f)
                return;
            if (useNearGroundRallyProjection && TryResolveNearGroundRallyProjection("outside-boundary fallback"))
                return;
            if (ball.position.y > Mathf.Max(0.1f, rallyCourtTriggerHeight + 0.02f))
                return;

            Vector3 firstGroundCrossing = ball.position;
            firstGroundCrossing.y = 0f;
            if (logRallyBounceDetection)
                Log($"Rally first-bounce OUT fallback: crossing={firstGroundCrossing} predicted={(hasPredictedRallyLanding ? predictedRallyLanding.ToString("F2") : "none")} velocity={ball.linearVelocity}.");
            HandleRallyBounce(firstGroundCrossing);
            return;
        }

        if (ball.position.y <= 0.25f || ball.linearVelocity.y <= 0f)
        {
            bool legalFirstBounce = rallyShotState.valid &&
                rallyShotState.firstBounceSeen &&
                rallyShotState.firstBounceInsideCourt &&
                rallyShotState.firstBounceOnOpponentSide;
            AwardPoint(
                legalFirstBounce ? TrackedHitterIndex : OpponentOfTrackedHitter(),
                legalFirstBounce ? "rally ball left playable court after legal first bounce" : "rally ball left court after an illegal first bounce");
        }
    }

    private struct RallyShotState
    {
        public bool valid;
        public int hitterIndex;
        public float originSideSign;
        public float opponentSideSign;
        public Vector3 contactPoint;
        public bool crossedNet;
        public bool firstBounceSeen;
        public bool firstBounceInsideCourt;
        public bool firstBounceOnOpponentSide;
        public Vector3 firstBouncePoint;
        public bool physicalNetTouched;
        public Vector3 physicalNetTouchPoint;
    }

    private struct ServiceAttemptState
    {
        public bool valid;
        public ServiceBox box;
        public bool isPlayerServing;
        public ServiceCourt court;
        public int attempt;
        public Vector3 serverStartPos;
        public Vector3 receiverStartPos;
        public Vector3 spawnPos;
        public float launchTime;
    }

    private ServiceBox GetFrozenServiceBox()
    {
        return hasFrozenServiceState ? frozenServiceState.box : default;
    }

    private void CaptureFrozenServiceState()
    {
        ServiceBox box = ResolveServiceBox();
        Vector3 receiverStart = serverIndex == 0
            ? ResolveAIReceiverServicePosition()
            : playerReceiverStartTarget != null ? playerReceiverStartTarget.position : Vector3.zero;

        frozenServiceState = new ServiceAttemptState
        {
            valid = box.valid,
            box = box,
            isPlayerServing = serverIndex == 0,
            court = serviceCourt,
            attempt = serviceAttempt,
            serverStartPos = serverTransform != null ? serverTransform.position : Vector3.zero,
            receiverStartPos = receiverStart,
            spawnPos = serverSpawnPoint != null ? serverSpawnPoint.position : Vector3.zero,
            launchTime = Time.time
        };
        hasFrozenServiceState = box.valid;
        serviceBoxTriggerEntered = false;
        serviceFirstBounceSeen = false;
        serviceLandingResolved = false;
        serviceBounceHandledByProjection = false;

        Log($"Service state frozen: server={(serverIndex == 0 ? "PLAYER" : "AI")} court={serviceCourt} attempt={serviceAttempt + 1} " +
            $"serverStart={frozenServiceState.serverStartPos} receiverStart={receiverStart} spawn={frozenServiceState.spawnPos} " +
            $"boxX=[{box.depthMin:F2},{box.depthMax:F2}] boxZ=[{box.lateralMin:F2},{box.lateralMax:F2}].");
    }

    private void ReuseFrozenPointServiceStateForSecondServe()
    {
        // The second serve belongs to the same point. Never resolve the
        // marker geometry again here: live movement or a side-sign guard can
        // otherwise select the opposite service box between attempts.
        frozenServiceState.court = pointServiceCourt;
        frozenServiceState.attempt = serviceAttempt;
        frozenServiceState.launchTime = Time.time;
        hasFrozenServiceState = frozenServiceState.valid;
        serviceBoxTriggerEntered = false;
        serviceFirstBounceSeen = false;
        serviceLandingResolved = false;
        serviceBounceHandledByProjection = false;

        ServiceBox box = frozenServiceState.box;
        Log($"Second serve reusing frozen point state: point={tiebreakPointIndex + 1} court={frozenServiceState.court} " +
            $"serverStart={frozenServiceState.serverStartPos} receiverStart={frozenServiceState.receiverStartPos} " +
            $"spawn={frozenServiceState.spawnPos} boxX=[{box.depthMin:F2},{box.depthMax:F2}] boxZ=[{box.lateralMin:F2},{box.lateralMax:F2}].");
    }

    private void ConfigureServiceBoxTrigger()
    {
        ServiceBox box = GetFrozenServiceBox();
        if (!box.valid)
            return;

        if (serviceBoxTriggerObject == null)
        {
            serviceBoxTriggerObject = new GameObject("Runtime Active Service Box Trigger");
            serviceBoxTriggerObject.transform.SetParent(transform, false);
            serviceBoxTrigger = serviceBoxTriggerObject.AddComponent<BoxCollider>();
            serviceBoxTrigger.isTrigger = true;
            serviceBoxTriggerObject.AddComponent<ServiceBoxTriggerRelay>().Initialize(this);
        }

        serviceBoxTriggerObject.transform.position = new Vector3(
            (box.depthMin + box.depthMax) * 0.5f,
            0.12f,
            (box.lateralMin + box.lateralMax) * 0.5f);
        serviceBoxTriggerObject.transform.rotation = Quaternion.identity;
        serviceBoxTrigger.size = new Vector3(
            Mathf.Max(0.05f, Mathf.Abs(box.depthMax - box.depthMin)),
            0.45f,
            Mathf.Max(0.05f, Mathf.Abs(box.lateralMax - box.lateralMin)));
        serviceBoxTrigger.enabled = true;
        serviceBoxTriggerEntered = false;

        if (logServiceBoxTrigger)
            Log($"Service box trigger active: centre={serviceBoxTriggerObject.transform.position} size={serviceBoxTrigger.size}.");
    }

    private void DeactivateServiceBoxTrigger()
    {
        if (serviceBoxTrigger != null)
            serviceBoxTrigger.enabled = false;
        serviceBoxTriggerEntered = false;
    }

    private void OnServiceBoxTriggerEntered(Rigidbody enteredBody, Vector3 position)
    {
        if (!IsMatchActive || phase != MatchPhase.ServeFlight || enteredBody == null || enteredBody != ball)
            return;

        serviceBoxTriggerEntered = true;
        serviceBoxTriggerEntryPosition = position;
        if (logServiceBoxTrigger)
            Log($"Service box trigger entered: position={position}.");
    }

    private void ConfigureRallyCourtTriggers()
    {
        if (!TryGetSinglesCourtExtents(out float minX, out float maxX, out float minZ, out float maxZ, out float surfaceY))
            return;

        float currentNetX = netPoint != null ? netPoint.position.x : (minX + maxX) * 0.5f;
        currentNetX = Mathf.Clamp(currentNetX, minX, maxX);
        ConfigureRallyCourtTrigger(
            ref nearRallyCourtTriggerObject,
            ref nearRallyCourtTrigger,
            "Runtime Near Singles Court Trigger",
            minX,
            currentNetX,
            minZ,
            maxZ,
            surfaceY,
            -1f);
        ConfigureRallyCourtTrigger(
            ref farRallyCourtTriggerObject,
            ref farRallyCourtTrigger,
            "Runtime Far Singles Court Trigger",
            currentNetX,
            maxX,
            minZ,
            maxZ,
            surfaceY,
            1f);
        SetRallyCourtTriggersEnabled(useRallyCourtTriggers && matchMode);

        if (logRallyBounceDetection)
            Log($"Rally court triggers configured: nearX=[{minX:F2},{currentNetX:F2}] farX=[{currentNetX:F2},{maxX:F2}] z=[{minZ:F2},{maxZ:F2}] height={Mathf.Max(0.04f, rallyCourtTriggerHeight):F2}m.");
    }

    private void ConfigureRallyCourtTrigger(
        ref GameObject triggerObject,
        ref BoxCollider triggerCollider,
        string objectName,
        float minX,
        float maxX,
        float minZ,
        float maxZ,
        float surfaceY,
        float sideSign)
    {
        if (triggerObject == null)
        {
            triggerObject = new GameObject(objectName);
            triggerObject.transform.SetParent(transform, false);
            triggerCollider = triggerObject.AddComponent<BoxCollider>();
            triggerCollider.isTrigger = true;
            triggerObject.AddComponent<RallyCourtTriggerRelay>().Initialize(this, sideSign);
        }

        float height = Mathf.Max(0.04f, rallyCourtTriggerHeight);
        triggerObject.transform.position = new Vector3(
            (minX + maxX) * 0.5f,
            surfaceY + height * 0.5f,
            (minZ + maxZ) * 0.5f);
        triggerObject.transform.rotation = Quaternion.identity;
        triggerCollider.size = new Vector3(
            Mathf.Max(0.05f, maxX - minX),
            height,
            Mathf.Max(0.05f, maxZ - minZ));
    }

    private void SetRallyCourtTriggersEnabled(bool enabled)
    {
        if (nearRallyCourtTrigger != null)
            nearRallyCourtTrigger.enabled = enabled;
        if (farRallyCourtTrigger != null)
            farRallyCourtTrigger.enabled = enabled;
    }

    private void OnRallyCourtTriggerEntered(Rigidbody enteredBody, Vector3 position, float sideSign)
    {
        if (!IsMatchActive || phase != MatchPhase.Rally || pointFinalized ||
            enteredBody == null || enteredBody != ball || rallyShotState.firstBounceSeen ||
            enteredBody.linearVelocity.y >= -0.02f)
            return;

        Vector3 crossingPoint = enteredBody.position;
        crossingPoint.y = 0f;
        rallyProjectionLoggedForShot = true;
        hasPredictedRallyLanding = true;
        predictedRallyLanding = crossingPoint;
        rallyBounceHandledByProjection = true;

        if (logRallyBounceDetection)
            Log($"Rally court trigger first-bounce IN: side={(sideSign < 0f ? "near" : "far")} crossing={crossingPoint} sourcePosition={position} velocity={enteredBody.linearVelocity}.");

        HandleRallyBounce(crossingPoint);
    }

    private bool TryGetSinglesCourtExtents(out float minX, out float maxX, out float minZ, out float maxZ, out float surfaceY)
    {
        minX = float.PositiveInfinity;
        maxX = float.NegativeInfinity;
        minZ = float.PositiveInfinity;
        maxZ = float.NegativeInfinity;
        surfaceY = 0f;
        int markerCount = 0;

        for (int i = 0; i < courtBounds.Length; i++)
        {
            Transform marker = courtBounds[i];
            if (marker == null)
                continue;
            minX = Mathf.Min(minX, marker.position.x);
            maxX = Mathf.Max(maxX, marker.position.x);
            minZ = Mathf.Min(minZ, marker.position.z);
            maxZ = Mathf.Max(maxZ, marker.position.z);
            surfaceY += marker.position.y;
            markerCount++;
        }

        if (markerCount <= 0 || !float.IsFinite(minX) || !float.IsFinite(minZ))
            return false;
        surfaceY /= markerCount;
        return maxX > minX && maxZ > minZ;
    }

    private struct ServiceBox
    {
        public bool valid;
        public float depthMin, depthMax, lateralMin, lateralMax;
        public float netLineX, serviceLineX;
    }

    private ServiceBox ResolveServiceBox()
    {
        Transform[] sideA = { nearServiceOuterLeft, nearServiceCentreLeft, nearServiceCentreRight, nearServiceOuterRight };
        Transform[] sideB = { farServiceOuterLeft, farServiceCentreLeft, farServiceCentreRight, farServiceOuterRight };
        if (!AllMarkersValid(sideA) || !AllMarkersValid(sideB))
            return default;

        float sideAX = AverageMarkerX(sideA);
        float sideBX = AverageMarkerX(sideB);
        float netLineX = netPoint != null ? netPoint.position.x : (sideAX + sideBX) * 0.5f;
        Vector3 serverPosition = serverTransform != null ? serverTransform.position : standTarget;

        // Choose the service-marker row on the opposite side of the net from
        // the actual server. This avoids relying on B/F naming orientation.
        float serverXSign = Mathf.Sign(serverPosition.x - netLineX);
        float sideASign = Mathf.Sign(sideAX - netLineX);
        Transform[] targetMarkers = sideASign != 0f && sideASign == -serverXSign ? sideA : sideB;
        float targetServiceLineX = ReferenceEquals(targetMarkers, sideA) ? sideAX : sideBX;

        float[] lateralValues = new float[4];
        for (int i = 0; i < targetMarkers.Length; i++)
            lateralValues[i] = targetMarkers[i].position.z;
        Array.Sort(lateralValues);

        float courtCentreZ = ResolveCourtCentreZ(lateralValues[0], lateralValues[3]);
        bool serverOnLowLateralSide = serverPosition.z < courtCentreZ;
        float selectedLateralA = serverOnLowLateralSide ? lateralValues[2] : lateralValues[0];
        float selectedLateralB = serverOnLowLateralSide ? lateralValues[3] : lateralValues[1];

        return new ServiceBox
        {
            valid = true,
            depthMin = Mathf.Min(netLineX, targetServiceLineX),
            depthMax = Mathf.Max(netLineX, targetServiceLineX),
            lateralMin = Mathf.Min(selectedLateralA, selectedLateralB),
            lateralMax = Mathf.Max(selectedLateralA, selectedLateralB),
            netLineX = netLineX,
            serviceLineX = targetServiceLineX
        };
    }

    private static bool AllMarkersValid(Transform[] markers)
    {
        if (markers == null || markers.Length == 0)
            return false;
        for (int i = 0; i < markers.Length; i++)
            if (markers[i] == null)
                return false;
        return true;
    }

    private static float AverageMarkerX(Transform[] markers)
    {
        float total = 0f;
        for (int i = 0; i < markers.Length; i++)
            total += markers[i].position.x;
        return total / markers.Length;
    }

    private float ResolveCourtCentreZ(float fallbackMin, float fallbackMax)
    {
        float minZ = float.PositiveInfinity;
        float maxZ = float.NegativeInfinity;
        if (courtBounds != null)
        {
            for (int i = 0; i < courtBounds.Length; i++)
            {
                if (courtBounds[i] == null) continue;
                minZ = Mathf.Min(minZ, courtBounds[i].position.z);
                maxZ = Mathf.Max(maxZ, courtBounds[i].position.z);
            }
        }
        return float.IsFinite(minZ) && float.IsFinite(maxZ)
            ? (minZ + maxZ) * 0.5f
            : (fallbackMin + fallbackMax) * 0.5f;
    }

    private bool IsServeBounceValid(Vector3 point)
    {
        ServiceBox box = GetFrozenServiceBox();
        if (!box.valid)
            return false;
        return point.x >= box.depthMin - 0.04f && point.x <= box.depthMax + 0.04f &&
               point.z >= box.lateralMin - 0.04f && point.z <= box.lateralMax + 0.04f;
    }

    private Vector3 GetNearServiceCentre() => nearServiceCentreLeft != null && nearServiceCentreRight != null ? (nearServiceCentreLeft.position + nearServiceCentreRight.position) * 0.5f : Vector3.zero;
    private Vector3 GetFarServiceCentre() => farServiceCentreLeft != null && farServiceCentreRight != null ? (farServiceCentreLeft.position + farServiceCentreRight.position) * 0.5f : Vector3.zero;

    private void ConfigureServiceReticle()
    {
        if (serverIndex != 0)
        {
            RestoreReticleBounds();
            return;
        }

        UIWorldReticle reticle = playerHitController != null && playerHitController.reticle != null
            ? playerHitController.reticle.GetComponent<UIWorldReticle>()
            : null;
        ServiceBox box = GetFrozenServiceBox();
        if (reticle == null || !box.valid)
            return;
        EnsureTemporaryReticleBounds();
        serviceReticleMin.position = new Vector3(box.depthMin, 0f, box.lateralMin);
        serviceReticleMax.position = new Vector3(box.depthMax, 0f, box.lateralMax);
        if (originalReticleMin == null) originalReticleMin = reticle.minBound;
        if (originalReticleMax == null) originalReticleMax = reticle.maxBound;
        reticle.minBound = serviceReticleMin;
        reticle.maxBound = serviceReticleMax;
        reticle.transform.position = new Vector3(
            Mathf.Clamp(reticle.transform.position.x, box.depthMin, box.depthMax),
            reticle.transform.position.y,
            Mathf.Clamp(reticle.transform.position.z, box.lateralMin, box.lateralMax));
    }

    private void RestoreReticleBounds()
    {
        UIWorldReticle reticle = playerHitController != null && playerHitController.reticle != null
            ? playerHitController.reticle.GetComponent<UIWorldReticle>()
            : null;
        bool restoredServiceBounds = reticle != null && originalReticleMin != null && originalReticleMax != null;
        if (reticle != null && originalReticleMin != null && originalReticleMax != null)
        {
            reticle.minBound = originalReticleMin;
            reticle.maxBound = originalReticleMax;

            Vector3 position = reticle.transform.position;
            position.x = Mathf.Clamp(
                position.x,
                Mathf.Min(originalReticleMin.position.x, originalReticleMax.position.x),
                Mathf.Max(originalReticleMin.position.x, originalReticleMax.position.x));
            position.z = Mathf.Clamp(
                position.z,
                Mathf.Min(originalReticleMin.position.z, originalReticleMax.position.z),
                Mathf.Max(originalReticleMin.position.z, originalReticleMax.position.z));
            reticle.transform.position = position;
            reticle.ReleaseAndResetFollow();
        }
        originalReticleMin = null;
        originalReticleMax = null;
        if (restoredServiceBounds)
            Log($"Player reticle restored to rally bounds: min={reticle.minBound.position} max={reticle.maxBound.position} position={reticle.transform.position}.");
    }

    private void ApplyServiceSolverSpeedCap()
    {
        if (solverMaxSpeedOverridden || playerHitController == null || playerHitController.solverComponent == null || playerHitController.solverComponent.solver == null)
            return;
        originalSolverMaxSpeed = playerHitController.solverComponent.solver.maxSpeed;
        playerHitController.solverComponent.solver.maxSpeed = Mathf.Max(originalSolverMaxSpeed, CurrentServeSpeedCapMps);
        solverMaxSpeedOverridden = true;
    }

    private void DisablePracticeCannon()
    {
        if (cannonSettingsOverridden || playerSwipe == null)
            return;
        originalCannonSpawnKey = playerSwipe.cannonSpawnKey;
        originalCannonAutoFire = playerSwipe.cannonAutoFire;
        playerSwipe.cannonSpawnKey = KeyCode.None;
        playerSwipe.cannonAutoFire = false;
        cannonSettingsOverridden = true;
    }

    private void RestoreCannonSettings()
    {
        if (!cannonSettingsOverridden || playerSwipe == null)
            return;
        playerSwipe.cannonSpawnKey = originalCannonSpawnKey;
        playerSwipe.cannonAutoFire = originalCannonAutoFire;
        cannonSettingsOverridden = false;
    }

    private void RestoreServiceSolverSpeedCap()
    {
        if (!solverMaxSpeedOverridden || playerHitController == null || playerHitController.solverComponent == null || playerHitController.solverComponent.solver == null)
            return;
        playerHitController.solverComponent.solver.maxSpeed = originalSolverMaxSpeed;
        solverMaxSpeedOverridden = false;
    }

    private void EnsureTemporaryReticleBounds()
    {
        if (serviceReticleMin != null && serviceReticleMax != null)
            return;
        serviceReticleMin = new GameObject("Runtime Service Reticle Min").transform;
        serviceReticleMax = new GameObject("Runtime Service Reticle Max").transform;
        serviceReticleMin.SetParent(transform);
        serviceReticleMax.SetParent(transform);
    }

    private void ConfigureParticipantsForService()
    {
        BaseShotLibrary.ResetHeightIntent();
        ClearAIServiceHolds();
        SetAIParticipation(false);
        SetHitGates(false);

        TennisAIPlayerController heldAI = serverIndex == 0 ? receiverAI : serverAI;
        if (heldAI == null)
            return;

        Vector3 holdPosition = serverIndex == 0
            ? ResolveAIReceiverServicePosition()
            : standTarget;
        heldAI.SetServiceHoldPosition(holdPosition);
    }

    private Vector3 ResolveAIReceiverServicePosition()
    {
        Transform receiveStart = serviceCourt == ServiceCourt.Deuce ? aiReceiveStart2 : aiReceiveStart1;
        if (receiveStart != null)
            return receiveStart.position;
        if (receiverAI != null && receiverAI.basePosition != null)
            return receiverAI.basePosition.position;
        return receiverAI != null ? receiverAI.transform.position : Vector3.zero;
    }

    private Transform ResolvePlayerReceiveStart()
    {
        return serviceCourt == ServiceCourt.Deuce ? playerReceiveStart2 : playerReceiveStart1;
    }

    private void HoldAIAtInbetweenPoint()
    {
        if (serverAI != null && inbetweenPointPosition != null)
            serverAI.SetServiceHoldPosition(inbetweenPointPosition.position);
    }

    private void ClearAIServiceHolds()
    {
        for (int i = 0; i < aiControllers.Length; i++)
            aiControllers[i]?.ClearServiceHoldPosition();
    }

    private void SetHitGates(bool enabled)
    {
        for (int i = 0; i < hitControllers.Length; i++)
            if (hitControllers[i] != null) hitControllers[i].matchHitAllowed = enabled;
    }

    private void SetHitGatesForServerToss()
    {
        SetHitGates(false);
        hitController server = GetServerHitController();
        if (server != null) server.matchHitAllowed = true;
    }

    private void SetAIParticipation(bool enabled)
    {
        for (int i = 0; i < aiControllers.Length; i++)
            if (aiControllers[i] != null) aiControllers[i].participatesInRally = enabled && aiControllers[i] == receiverAI;
    }

    private void MoveServerIntoPosition()
    {
        if (serverTransform == null)
            return;
        PlayerMovement movement = serverIndex == 0 ? playerMovement : aiMovement;
        Vector3 delta = standTarget - serverTransform.position;
        delta.y = 0f;
        if (delta.magnitude > serverStandTolerance)
            movement?.SetExternalMove(delta.normalized, serverIndex == 0 ? 4f : 7f);
        else
        {
            movement?.ClearExternalMove();
            Vector3 p = serverTransform.position;
            p.x = standTarget.x;
            p.z = standTarget.z;
            serverTransform.position = p;
        }
    }

    private void MovePlayerReceiverIntoPosition()
    {
        if (playerReceiverStartTarget == null || playerHitController == null)
            return;

        Vector3 delta = playerReceiverStartTarget.position - playerHitController.transform.position;
        delta.y = 0f;
        if (delta.magnitude > serverStandTolerance)
            playerMovement?.SetExternalMove(delta.normalized, 4f);
        else
        {
            playerMovement?.ClearExternalMove();
            Vector3 p = playerHitController.transform.position;
            p.x = playerReceiverStartTarget.position.x;
            p.z = playerReceiverStartTarget.position.z;
            playerHitController.transform.position = p;
        }
    }

    private void SnapAIToRequiredStartPosition()
    {
        if (serverIndex != 0)
        {
            if (serverTransform == null || IsServerInPosition())
                return;
            SetPlanarPosition(serverTransform, standTarget);
            aiMovement?.ClearExternalMove();
            Log("AI server did not reach its start in time; snapped to its service start.");
            return;
        }

        if (receiverAI == null)
            return;
        Vector3 target = ResolveAIReceiverServicePosition();
        Vector3 delta = receiverAI.transform.position - target;
        delta.y = 0f;
        if (delta.magnitude <= serverStandTolerance)
            return;

        SetPlanarPosition(receiverAI.transform, target);
        receiverAI.movement?.ClearExternalMove();
        receiverAI.SetServiceHoldPosition(target);
        Log("AI receiver did not reach its start in time; snapped to its receive start.");
    }

    private static void SetPlanarPosition(Transform target, Vector3 position)
    {
        if (target == null)
            return;
        Vector3 snapped = target.position;
        snapped.x = position.x;
        snapped.z = position.z;
        target.position = snapped;
    }

    private bool IsServerInPosition()
    {
        if (serverTransform == null)
            return false;
        Vector3 delta = serverTransform.position - standTarget;
        delta.y = 0f;
        return delta.magnitude <= serverStandTolerance;
    }

    private bool IsPlayerReceiverInPosition()
    {
        if (playerReceiverStartTarget == null || playerHitController == null)
            return true;
        Vector3 delta = playerHitController.transform.position - playerReceiverStartTarget.position;
        delta.y = 0f;
        return delta.magnitude <= serverStandTolerance;
    }

    private void ClampServerToServiceLine()
    {
        GetServerServiceLine(out Transform left, out Transform middle, out Transform right);
        if (serverTransform == null || left == null || middle == null || right == null)
            return;

        Transform segmentStart = serviceCourt == ServiceCourt.Deuce
            ? middle
            : left;
        Transform segmentEnd = serviceCourt == ServiceCourt.Deuce
            ? right
            : middle;
        if (serverIndex != 0)
        {
            segmentStart = serviceCourt == ServiceCourt.Deuce ? left : middle;
            segmentEnd = serviceCourt == ServiceCourt.Deuce ? middle : right;
        }

        Vector3 a = segmentStart.position;
        Vector3 b = segmentEnd.position;
        Vector3 segment = b - a;
        float t = segment.sqrMagnitude > 0.0001f
            ? Mathf.Clamp01(Vector3.Dot(serverTransform.position - a, segment) / segment.sqrMagnitude)
            : 0f;
        serverTransform.position = Vector3.Lerp(a, b, t);
    }

    private Vector3 EnsureStandTargetOnAssignedSide(Vector3 candidate)
    {
        Transform assignedBaseline = serverIndex == 0 ? servicePlayerBoundM : serviceAIBoundM;
        if (netPoint == null || assignedBaseline == null)
            return candidate;

        float expectedSign = Mathf.Sign(assignedBaseline.position.x - netPoint.position.x);
        float candidateSign = Mathf.Sign(candidate.x - netPoint.position.x);
        if (expectedSign == 0f || candidateSign == expectedSign)
            return candidate;

        float behindBaseline = Mathf.Max(0.35f, Mathf.Abs(candidate.x - assignedBaseline.position.x));
        candidate.x = assignedBaseline.position.x + expectedSign * behindBaseline;
        Log($"Corrected server start to assigned side: server={(serverIndex == 0 ? "PLAYER" : "AI")} target={candidate}.");
        return candidate;
    }

    private void EnsureServerOnAssignedSide()
    {
        if (serverTransform == null || netPoint == null)
            return;

        float courtCentreZ = ResolveCourtCentreZ(-1f, 1f);
        float expectedXSign = Mathf.Sign(standTarget.x - netPoint.position.x);
        float actualXSign = Mathf.Sign(serverTransform.position.x - netPoint.position.x);
        float expectedZSign = Mathf.Sign(standTarget.z - courtCentreZ);
        float actualZSign = Mathf.Sign(serverTransform.position.z - courtCentreZ);
        bool wrongNetSide = expectedXSign != 0f && actualXSign != expectedXSign;
        bool wrongLateralSide = expectedZSign != 0f && actualZSign != expectedZSign;
        if (!wrongNetSide && !wrongLateralSide)
            return;

        SetPlanarPosition(serverTransform, standTarget);
        Log($"Server position guard moved {(serverIndex == 0 ? "PLAYER" : "AI")} to start={standTarget}; wrongNetSide={wrongNetSide} wrongLateralSide={wrongLateralSide}.");
    }

    private void EnsureReceiverOnAssignedSide()
    {
        Transform receiver = serverIndex == 0
            ? receiverAI != null ? receiverAI.transform : null
            : playerHitController != null ? playerHitController.transform : null;
        Vector3 target = serverIndex == 0
            ? ResolveAIReceiverServicePosition()
            : playerReceiverStartTarget != null ? playerReceiverStartTarget.position : Vector3.zero;
        if (receiver == null || netPoint == null || (serverIndex != 0 && playerReceiverStartTarget == null))
            return;

        float courtCentreZ = ResolveCourtCentreZ(-1f, 1f);
        bool wrongX = Mathf.Sign(receiver.position.x - netPoint.position.x) != Mathf.Sign(target.x - netPoint.position.x);
        bool wrongZ = Mathf.Sign(receiver.position.z - courtCentreZ) != Mathf.Sign(target.z - courtCentreZ);
        if (!wrongX && !wrongZ)
            return;

        SetPlanarPosition(receiver, target);
        Log($"Receiver position guard moved {(serverIndex == 0 ? "AI" : "PLAYER")} to receiveStart={target}; wrongNetSide={wrongX} wrongLateralSide={wrongZ}.");
    }

    private void UpdateServerSpawnPointPlacement()
    {
        if (serverTransform == null || serverSpawnPoint == null || netPoint == null)
            return;

        Vector3 towardNet = netPoint.position - serverTransform.position;
        towardNet.y = 0f;
        if (towardNet.sqrMagnitude < 0.001f)
            return;
        towardNet.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, towardNet).normalized;
        Vector3 desired = serverTransform.position +
            towardNet * Mathf.Max(0f, serverBallForwardOffset) +
            right * serverBallRightOffset;
        desired.y = serverTransform.position.y + Mathf.Max(0.1f, serverBallHeightOffset);

        Vector3 planarOffset = serverSpawnPoint.position - serverTransform.position;
        planarOffset.y = 0f;
        if (!spawnGuardLoggedThisPoint && planarOffset.magnitude > Mathf.Max(0.1f, serverBallMaximumPlanarDistance))
        {
            Log($"Ball spawn guard corrected {serverSpawnPoint.name}: old={serverSpawnPoint.position} server={serverTransform.position} planarDistance={planarOffset.magnitude:F2}m.");
            spawnGuardLoggedThisPoint = true;
        }
        serverSpawnPoint.position = desired;
    }

    private void RecordPredictedServeLanding(Vector3 start, Vector3 velocity, Vector3 spin)
    {
        hasPredictedServeLanding = false;
        ShotSolverComponent component = playerHitController != null ? playerHitController.solverComponent : null;
        if (component == null || component.solver == null)
            return;

        Vector3 horizontal = velocity;
        horizontal.y = 0f;
        float speed = velocity.magnitude;
        if (horizontal.sqrMagnitude < 0.001f || speed < 0.01f)
            return;

        float theta = Mathf.Atan2(velocity.y, horizontal.magnitude);
        Vector3 solverSpin = velocity.x < 0f ? new Vector3(-spin.x, spin.y, -spin.z) : spin;
        float landingDistance = component.solver.PredictLandingX(new Vector2(0f, start.y), speed, theta, solverSpin);
        if (!float.IsFinite(landingDistance) || landingDistance < 0f)
            return;

        predictedServeLanding = start + horizontal.normalized * landingDistance;
        predictedServeLanding.y = 0f;
        hasPredictedServeLanding = true;
    }

    private string FormatPredictedLanding()
    {
        return hasPredictedServeLanding ? predictedServeLanding.ToString("F2") : "<unavailable>";
    }

    private Vector3 ComputeServerStandTarget(Transform server)
    {
        Transform start = serverIndex == 0
            ? serviceCourt == ServiceCourt.Deuce ? playerServeStartR : playerServeStartL
            : serviceCourt == ServiceCourt.Deuce ? aiServeStart2 : aiServeStart1;
        if (start != null)
            return start.position;

        GetServerServiceLine(out _, out Transform middle, out _);
        if (middle == null)
            return server != null ? server.position : Vector3.zero;
        return middle.position;
    }

    private void GetServerServiceLine(out Transform left, out Transform middle, out Transform right)
    {
        bool aiServer = serverIndex != 0;
        left = aiServer && serviceAIBoundL != null ? serviceAIBoundL : servicePlayerBoundL;
        middle = aiServer && serviceAIBoundM != null ? serviceAIBoundM : servicePlayerBoundM;
        right = aiServer && serviceAIBoundR != null ? serviceAIBoundR : servicePlayerBoundR;
    }

    private void EnsureBallAtServerSpawn()
    {
        if (ball != null)
            return;
        playerSwipe.SpawnNewBall();
        ball = playerSwipe.ball;
        for (int i = 0; i < hitControllers.Length; i++)
            if (hitControllers[i] != null && ball != null) hitControllers[i].SetBallReference(ball.transform);
    }

    private void FreezeBallAtSpawn()
    {
        if (ball == null)
            return;
        if (ball.isKinematic)
            ball.isKinematic = false;
        UpdateServerSpawnPointPlacement();
        if (serverSpawnPoint != null)
        {
            ball.transform.SetParent(serverSpawnPoint, false);
            ball.transform.localPosition = Vector3.zero;
            ball.transform.localRotation = Quaternion.identity;
        }
        ball.linearVelocity = Vector3.zero;
        ball.angularVelocity = Vector3.zero;
        ball.isKinematic = true;
        ball.useGravity = false;
        ball.Sleep();
    }

    private void KeepFrozenBallAtServerSpawn()
    {
        if (ball == null || !ball.isKinematic || serverSpawnPoint == null)
            return;
        UpdateServerSpawnPointPlacement();
        if (ball.transform.parent != serverSpawnPoint)
            ball.transform.SetParent(serverSpawnPoint, false);
        ball.transform.localPosition = Vector3.zero;
        ball.transform.localRotation = Quaternion.identity;
    }

    private void ReleaseBallFromServerSpawn()
    {
        if (ball != null && ball.transform.parent != null)
            ball.transform.SetParent(null, true);
    }

    private Transform ResolveServerSpawnPoint(int servingIndex)
    {
        Transform owner = servingIndex == 0
            ? playerHitController != null ? playerHitController.transform : null
            : serverAI != null ? serverAI.transform : null;

        Transform ownedSpawn = servingIndex == 0
            ? FindChildByName(owner, "serverSpawnPoint")
            : FindChildByName(owner, "serverSpawnPoint_AI");
        if (ownedSpawn == null && servingIndex != 0)
            ownedSpawn = FindChildByName(owner, "serverSpawnPoint");
        if (ownedSpawn != null)
            return ownedSpawn;

        return servingIndex == 0 ? playerServerSpawnPoint : aiServerSpawnPoint;
    }

    private static Transform FindChildByName(Transform root, string targetName)
    {
        if (root == null || string.IsNullOrEmpty(targetName))
            return null;
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == targetName)
                return child;
        return null;
    }

    private static string FormatPosition(Transform marker)
    {
        return marker == null ? "<missing>" : marker.position.ToString("F2");
    }

    private BaseShotType PickAIServeShotType()
    {
        if (serviceAttempt == 0 || !aiUseSecondServeSpinMix)
            return aiServeShotType;

        float kickChance = Mathf.Clamp01(aiSecondServeKickChance);
        float sliceChance = Mathf.Clamp01(aiSecondServeSliceChance);
        sliceChance = Mathf.Min(sliceChance, 1f - kickChance);
        float roll = UnityEngine.Random.value;
        if (roll < kickChance)
            return BaseShotType.Topspin; // kick serve
        if (roll < kickChance + sliceChance)
            return BaseShotType.Slice;
        return BaseShotType.Flat;
    }

    private List<BaseShotType> BuildAIServeShotTypeCandidates()
    {
        BaseShotType preferred = PickAIServeShotType();
        List<BaseShotType> candidates = new List<BaseShotType>(3) { preferred };
        if (!aiServeTryAlternateShotTypes)
            return candidates;

        if (serviceAttempt == 0)
        {
            AddServeShotTypeCandidate(candidates, BaseShotType.Flat);
            AddServeShotTypeCandidate(candidates, BaseShotType.Topspin);
            AddServeShotTypeCandidate(candidates, BaseShotType.Slice);
        }
        else
        {
            // A second serve should exhaust kick/slice solutions before it
            // falls back to a flatter ball.
            AddServeShotTypeCandidate(candidates, BaseShotType.Topspin);
            AddServeShotTypeCandidate(candidates, BaseShotType.Slice);
            AddServeShotTypeCandidate(candidates, BaseShotType.Flat);
        }
        return candidates;
    }

    private static void AddServeShotTypeCandidate(List<BaseShotType> candidates, BaseShotType shotType)
    {
        if (!candidates.Contains(shotType))
            candidates.Add(shotType);
    }

    private static string GetServeShotLabel(BaseShotType shotType)
    {
        return shotType == BaseShotType.Topspin ? "Kick" : shotType.ToString();
    }

    private Vector3 BuildServeSpin(Vector3 direction, BaseShotType shotType)
    {
        float rpm = shotType == BaseShotType.Slice ? sliceServeSpinRpm : shotType == BaseShotType.Topspin ? kickServeSpinRpm : flatServeSpinRpm;
        return Vector3.Cross(Vector3.up, direction).normalized * BaseShotLibrary.RpmToRadPerSecond(Mathf.Abs(rpm)) * Mathf.Sign(rpm);
    }

    private int NextServerIndex(int nextPoint)
    {
        if (nextPoint <= 0) return firstServerWasPlayer ? 0 : 1;
        if (nextPoint == 1) return firstServerWasPlayer ? 1 : 0;
        return ((nextPoint - 1) / 2) % 2 == 0 ? (firstServerWasPlayer ? 1 : 0) : (firstServerWasPlayer ? 0 : 1);
    }

    private float ResolveTossLowHeight() => serviceContactPointLow != null ? serviceContactPointLow.position.y : minimumTossHeightFallback;
    private float ResolveTossHighHeight() => serviceContactPointHigh != null ? Mathf.Max(ResolveTossLowHeight(), serviceContactPointHigh.position.y) : Mathf.Max(ResolveTossLowHeight(), maximumTossHeightFallback);

    private bool HasMatchplayAI()
    {
        for (int i = 0; i < aiControllers.Length; i++)
            if (aiControllers[i] != null && aiControllers[i].IsMatchplayMode) return true;
        return false;
    }

    private hitController GetServerHitController() => serverIndex == 0 ? playerHitController : GetAIHitController(serverAI);
    private hitController GetAIHitController(TennisAIPlayerController ai) => ai != null ? ai.hitController : null;

    private hitController FindControllerForBall(Rigidbody targetBall)
    {
        for (int i = 0; i < hitControllers.Length; i++)
            if (hitControllers[i] != null && hitControllers[i].ball == targetBall) return hitControllers[i];
        return null;
    }

    private bool IsInsideCourt(Vector3 point)
    {
        float minX = float.PositiveInfinity, maxX = float.NegativeInfinity, minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
        for (int i = 0; i < courtBounds.Length; i++)
        {
            if (courtBounds[i] == null) continue;
            minX = Mathf.Min(minX, courtBounds[i].position.x); maxX = Mathf.Max(maxX, courtBounds[i].position.x);
            minZ = Mathf.Min(minZ, courtBounds[i].position.z); maxZ = Mathf.Max(maxZ, courtBounds[i].position.z);
        }
        if (!float.IsFinite(minX) || !float.IsFinite(minZ)) return true;
        return point.x >= minX - 0.04f && point.x <= maxX + 0.04f && point.z >= minZ - 0.04f && point.z <= maxZ + 0.04f;
    }

    private void SetPhase(MatchPhase next)
    {
        if (phase == next) return;
        phase = next;
        if (next == MatchPhase.Rally)
        {
            RestoreReticleBounds();
            playerMovement?.SetExternalMoveAllowed(false);
        }
        else if (next == MatchPhase.PreparingPoint)
            playerMovement?.SetExternalMoveAllowed(true);
        PhaseChanged?.Invoke(phase);
    }

    private void ResolveReferences()
    {
        if (resolvingReferences) return;
        resolvingReferences = true;
        if (playerSwipe == null) playerSwipe = FindFirstObjectByType<swipeMouseBall>();
        if (playerHitController == null) playerHitController = playerSwipe != null ? playerSwipe.hitControllerInstance : FindPlayerHitController();
        if (aiControllers.Length == 0) aiControllers = FindObjectsByType<TennisAIPlayerController>(FindObjectsSortMode.None);
        if (hitControllers.Length == 0) hitControllers = FindObjectsByType<hitController>(FindObjectsSortMode.None);
        if (!matchAISelectionLocked || receiverAI == null || serverAI == null)
        {
            TennisAIPlayerController selectedAI = FindBestMatchAI();
            if (selectedAI != null)
            {
                receiverAI = selectedAI;
                serverAI = selectedAI;
                matchAISelectionLocked = true;
            }
        }
        if (playerMovement == null && playerHitController != null) playerMovement = playerHitController.GetComponent<PlayerMovement>();
        aiMovement = serverAI != null ? serverAI.movement : null;

        servicePlayerBoundL = servicePlayerBoundL != null ? servicePlayerBoundL : FindNamed("servicePlayerBoundL");
        servicePlayerBoundM = servicePlayerBoundM != null ? servicePlayerBoundM : FindNamed("servicePlayerBoundM");
        servicePlayerBoundR = servicePlayerBoundR != null ? servicePlayerBoundR : FindNamed("servicePlayerBoundR");
        serviceAIBoundL = serviceAIBoundL != null ? serviceAIBoundL : FindNamed("serviceAIBoundL");
        serviceAIBoundM = serviceAIBoundM != null ? serviceAIBoundM : FindNamed("serviceAIBoundM");
        serviceAIBoundR = serviceAIBoundR != null ? serviceAIBoundR : FindNamed("serviceAIBoundR");
        serviceContactPointLow = serviceContactPointLow != null ? serviceContactPointLow : FindNamed("serviceContactPointLow");
        serviceContactPointHigh = serviceContactPointHigh != null ? serviceContactPointHigh : FindNamed("serviceContactPointHigh");
        playerServeStartR = playerServeStartR != null ? playerServeStartR : FindNamed("playerServeStart_R");
        playerServeStartL = playerServeStartL != null ? playerServeStartL : FindNamed("playerServeStart_L");
        aiServeStart1 = aiServeStart1 != null ? aiServeStart1 : FindNamed("AIServeStart_1");
        aiServeStart2 = aiServeStart2 != null ? aiServeStart2 : FindNamed("AIServeStart_2");
        aiReceiveStart1 = aiReceiveStart1 != null ? aiReceiveStart1 : FindNamed("AIRecieveStart_1");
        aiReceiveStart2 = aiReceiveStart2 != null ? aiReceiveStart2 : FindNamed("AIRecieveStart_2");
        playerReceiveStart1 = playerReceiveStart1 != null ? playerReceiveStart1 : FindNamed("playerRecieveStart_1");
        playerReceiveStart2 = playerReceiveStart2 != null ? playerReceiveStart2 : FindNamed("playerRecieveStart_2");
        inbetweenPointPosition = inbetweenPointPosition != null ? inbetweenPointPosition : FindNamed("inbetweenpointPosition");
        playerServerSpawnPoint = playerServerSpawnPoint != null ? playerServerSpawnPoint : FindNamed("serverSpawnPoint");
        aiServerSpawnPoint = aiServerSpawnPoint != null ? aiServerSpawnPoint : FindNamed("serverSpawnPoint_AI");
        netPoint = netPoint != null ? netPoint : (FindNamed("tennisNetV4_GameplayCollider") ?? FindNamed("net"));
        nearServiceOuterLeft = nearServiceOuterLeft != null ? nearServiceOuterLeft : FindNamed("outServiceBounds_BL");
        nearServiceCentreLeft = nearServiceCentreLeft != null ? nearServiceCentreLeft : FindNamed("outServiceBounds_BM1");
        nearServiceCentreRight = nearServiceCentreRight != null ? nearServiceCentreRight : FindNamed("outServiceBounds_BM2");
        nearServiceOuterRight = nearServiceOuterRight != null ? nearServiceOuterRight : FindNamed("outServiceBounds_BR");
        farServiceOuterLeft = farServiceOuterLeft != null ? farServiceOuterLeft : FindNamed("outServiceBounds_FL");
        farServiceCentreLeft = farServiceCentreLeft != null ? farServiceCentreLeft : FindNamed("outServiceBounds_FM1");
        farServiceCentreRight = farServiceCentreRight != null ? farServiceCentreRight : FindNamed("outServiceBounds_FM2");
        farServiceOuterRight = farServiceOuterRight != null ? farServiceOuterRight : FindNamed("outServiceBounds_FR");
        if (courtBounds == null || courtBounds.Length < 4) courtBounds = new Transform[4];
        if (courtBounds[0] == null)
        {
            courtBounds[0] = FindNamed("outCourtBounds_BL"); courtBounds[1] = FindNamed("outCourtBounds_BR");
            courtBounds[2] = FindNamed("outCourtBounds_FR"); courtBounds[3] = FindNamed("outCourtBounds_FL");
        }
        resolvingReferences = false;
    }

    private void EnsureRallyFeedback()
    {
        if (!enableRallyFeedback)
            return;

        if (rallyFeedback == null)
            rallyFeedback = new RallyFeedbackRecorder();

        rallyFeedback.Configure(
            playerHitController != null ? playerHitController.transform : null,
            receiverAI != null ? receiverAI.transform : null,
            ball);
    }

    private hitController FindPlayerHitController()
    {
        hitController[] candidates = FindObjectsByType<hitController>(FindObjectsSortMode.None);
        for (int i = 0; i < candidates.Length; i++) if (candidates[i] != null && candidates[i].swipe != null) return candidates[i];
        return candidates.Length > 0 ? candidates[0] : null;
    }

    private TennisAIPlayerController FindBestMatchAI()
    {
        TennisAIPlayerController best = null;
        float bestScore = float.NegativeInfinity;
        Vector3 playerPosition = playerHitController != null ? playerHitController.transform.position : Vector3.zero;
        for (int i = 0; i < aiControllers.Length; i++)
        {
            TennisAIPlayerController candidate = aiControllers[i];
            if (candidate == null || candidate.hitController == playerHitController || !candidate.gameObject.activeInHierarchy)
                continue;

            float score = Mathf.Abs(candidate.transform.position.x - playerPosition.x);
            if (candidate.name.Equals("tennisplayerAI_2", StringComparison.OrdinalIgnoreCase)) score += 2000f;
            if (candidate.participatesInRally) score += 1000f;
            if (candidate.IsMatchplayMode) score += 500f;
            if (score <= bestScore) continue;
            bestScore = score;
            best = candidate;
        }
        return best;
    }

    private Transform FindNamed(string objectName)
    {
        GameObject found = string.IsNullOrEmpty(objectName) ? null : GameObject.Find(objectName);
        return found != null ? found.transform : null;
    }

    private void OnDrawGizmos()
    {
        rallyFeedback?.DrawMovementGizmos();
        if (drawActivePositionGizmos)
            DrawActivePositionGizmos();

        if (drawCourtBoundsGizmo)
            DrawCourtBoundsGizmo();

        if (useRallyCourtTriggers && TryGetSinglesCourtExtents(out float minX, out float maxX, out float minZ, out float maxZ, out float surfaceY))
        {
            float currentNetX = netPoint != null ? Mathf.Clamp(netPoint.position.x, minX, maxX) : (minX + maxX) * 0.5f;
            DrawXZBox(minX, currentNetX, minZ, maxZ, nearRallyTriggerGizmoColor, surfaceY + Mathf.Max(0.02f, rallyCourtTriggerHeight));
            DrawXZBox(currentNetX, maxX, minZ, maxZ, farRallyTriggerGizmoColor, surfaceY + Mathf.Max(0.02f, rallyCourtTriggerHeight));
        }

        if (drawServiceBoxGizmo && IsServicePhase)
        {
            ServiceBox box = GetFrozenServiceBox();
            if (box.valid)
                DrawXZBox(box.depthMin, box.depthMax, box.lateralMin, box.lateralMax, serviceBoxGizmoColor, 0.06f);
        }
    }

    private void DrawActivePositionGizmos()
    {
        if (!IsMatchActive)
            return;

        Gizmos.color = activeServeStartGizmoColor;
        Gizmos.DrawWireSphere(standTarget + Vector3.up * 0.08f, 0.22f);

        Vector3 receiveTarget = serverIndex == 0
            ? ResolveAIReceiverServicePosition()
            : playerReceiverStartTarget != null ? playerReceiverStartTarget.position : Vector3.zero;
        if (receiveTarget != Vector3.zero)
        {
            Gizmos.color = activeReceiveStartGizmoColor;
            Gizmos.DrawWireCube(receiveTarget + Vector3.up * 0.12f, new Vector3(0.38f, 0.24f, 0.38f));
        }

        if (serverSpawnPoint != null)
        {
            Gizmos.color = activeBallSpawnGizmoColor;
            Gizmos.DrawWireSphere(serverSpawnPoint.position, 0.14f);
            if (serverTransform != null)
            {
                Gizmos.DrawLine(serverTransform.position + Vector3.up * 0.05f, serverSpawnPoint.position);
                Gizmos.DrawWireSphere(serverTransform.position + Vector3.up * serverBallHeightOffset, Mathf.Max(0.1f, serverBallMaximumPlanarDistance));
            }
        }
    }

    private void DrawCourtBoundsGizmo()
    {
        if (courtBounds == null || courtBounds.Length == 0)
            return;

        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minZ = float.PositiveInfinity;
        float maxZ = float.NegativeInfinity;
        float y = 0.04f;
        for (int i = 0; i < courtBounds.Length; i++)
        {
            Transform marker = courtBounds[i];
            if (marker == null)
                continue;
            minX = Mathf.Min(minX, marker.position.x);
            maxX = Mathf.Max(maxX, marker.position.x);
            minZ = Mathf.Min(minZ, marker.position.z);
            maxZ = Mathf.Max(maxZ, marker.position.z);
            y = Mathf.Max(y, marker.position.y + 0.04f);
        }

        if (float.IsFinite(minX) && float.IsFinite(minZ))
            DrawXZBox(minX, maxX, minZ, maxZ, courtBoundsGizmoColor, y);
    }

    private static void DrawXZBox(float minX, float maxX, float minZ, float maxZ, Color color, float y)
    {
        Gizmos.color = color;
        Vector3 a = new Vector3(minX, y, minZ);
        Vector3 b = new Vector3(minX, y, maxZ);
        Vector3 c = new Vector3(maxX, y, maxZ);
        Vector3 d = new Vector3(maxX, y, minZ);
        Gizmos.DrawLine(a, b);
        Gizmos.DrawLine(b, c);
        Gizmos.DrawLine(c, d);
        Gizmos.DrawLine(d, a);
    }

    private sealed class ServiceBoxTriggerRelay : MonoBehaviour
    {
        private MatchServicePointController owner;

        public void Initialize(MatchServicePointController controller)
        {
            owner = controller;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (owner == null || other == null)
                return;
            owner.OnServiceBoxTriggerEntered(other.attachedRigidbody, other.transform.position);
        }
    }

    private sealed class RallyCourtTriggerRelay : MonoBehaviour
    {
        private MatchServicePointController owner;
        private float sideSign;

        public void Initialize(MatchServicePointController controller, float courtSideSign)
        {
            owner = controller;
            sideSign = courtSideSign;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (owner == null || other == null)
                return;
            owner.OnRallyCourtTriggerEntered(other.attachedRigidbody, other.transform.position, sideSign);
        }
    }

    private void SetPhaseIfNeeded(MatchPhase next) => SetPhase(next);
    private void Log(string message) { if (debugLogs) Debug.Log("[MatchService] " + message); }
}

/// <summary>
/// Captures the measurable part of a match rally. The match controller supplies
/// authoritative launch, contact and point-end events, while this class samples
/// real ball and receiver movement at the physics cadence.
/// </summary>
public sealed class RallyFeedbackRecorder
{
    private const int PlayerIndex = 0;
    private const int AIIndex = 1;
    private const int MaximumRecordedShotsPerSide = 10;
    private const float MovementSampleInterval = 0.25f;
    private const float GizmoHeight = 0.08f;

    public bool drawMovementGizmos = true;
    public Color playerMovementColor = new Color(0.1f, 0.7f, 1f, 0.95f);
    public Color aiMovementColor = new Color(1f, 0.55f, 0.12f, 0.95f);

    private Transform playerTransform;
    private Transform aiTransform;
    private Rigidbody ball;
    private readonly List<ShotRecord> completedShots = new List<ShotRecord>(20);
    private readonly List<MovementTrace> completedMovementTraces = new List<MovementTrace>(20);
    private readonly int[] recordedShotsByHitter = new int[2];
    private ShotRecord activeShot;
    private int activePointNumber;
    private bool pointActive;

    private sealed class ShotRecord
    {
        public int hitterIndex;
        public int receiverIndex;
        public float launchMph;
        public float launchTime;
        public Vector3 lastBallPosition;
        public float ballPathDistance;
        public Transform receiverTransform;
        public Vector3 lastReceiverPosition;
        public float receiverPathDistance;
        public float nextMovementSampleAt;
        public readonly List<MovementSample> movementSamples = new List<MovementSample>(8);
        public float resolvedAt;
    }

    private readonly struct MovementSample
    {
        public readonly float time;
        public readonly float pathDistance;
        public readonly Vector3 position;

        public MovementSample(float time, float pathDistance, Vector3 position)
        {
            this.time = time;
            this.pathDistance = pathDistance;
            this.position = position;
        }
    }

    private sealed class MovementTrace
    {
        public int receiverIndex;
        public readonly List<MovementSample> samples = new List<MovementSample>(8);
        public Vector3 endPosition;
    }

    public void Configure(Transform player, Transform ai, Rigidbody trackedBall)
    {
        playerTransform = player;
        aiTransform = ai;
        if (trackedBall != null)
            ball = trackedBall;
    }

    public void BeginPoint(int pointNumber)
    {
        activePointNumber = Mathf.Max(1, pointNumber);
        pointActive = true;
        activeShot = null;
        completedShots.Clear();
        completedMovementTraces.Clear();
        Array.Clear(recordedShotsByHitter, 0, recordedShotsByHitter.Length);
    }

    public void StopRecording()
    {
        pointActive = false;
        activeShot = null;
    }

    public void RegisterLaunch(int hitterIndex, Rigidbody launchedBall, Vector3 launchPosition, Vector3 launchVelocity)
    {
        if (!pointActive || !IsParticipant(hitterIndex))
            return;

        if (launchedBall != null)
            ball = launchedBall;
        if (activeShot != null)
            CompleteActiveShot(launchPosition);
        if (recordedShotsByHitter[hitterIndex] >= MaximumRecordedShotsPerSide)
            return;

        int receiverIndex = OpponentOf(hitterIndex);
        Transform receiver = TransformFor(receiverIndex);
        Vector3 receiverPosition = receiver != null ? receiver.position : Vector3.zero;
        activeShot = new ShotRecord
        {
            hitterIndex = hitterIndex,
            receiverIndex = receiverIndex,
            launchMph = launchVelocity.magnitude * 2.23693629f,
            launchTime = Time.time,
            lastBallPosition = launchPosition,
            receiverTransform = receiver,
            lastReceiverPosition = receiverPosition,
            nextMovementSampleAt = MovementSampleInterval
        };
        recordedShotsByHitter[hitterIndex]++;
        if (receiver != null)
            activeShot.movementSamples.Add(new MovementSample(0f, 0f, receiverPosition));
    }

    /// <summary>Ends the current incoming-ball window at an accepted racket contact.</summary>
    public void RegisterReceiverContact(int hitterIndex, Vector3 contactPoint)
    {
        if (activeShot != null && hitterIndex == activeShot.receiverIndex)
            CompleteActiveShot(contactPoint);
    }

    public void CompletePoint(string pointReason)
    {
        if (!pointActive)
            return;

        if (activeShot != null)
            CompleteActiveShot(ball != null ? ball.position : activeShot.lastBallPosition);
        pointActive = false;
        Debug.Log(BuildPointReport(pointReason));
    }

    public void TickFixed()
    {
        if (activeShot != null)
            SampleActiveShot(false, ball != null ? ball.position : activeShot.lastBallPosition);
    }

    private void CompleteActiveShot(Vector3 terminalBallPosition)
    {
        if (activeShot == null)
            return;

        SampleActiveShot(true, terminalBallPosition);
        activeShot.resolvedAt = Time.time;
        completedShots.Add(activeShot);
        if (activeShot.movementSamples.Count > 0)
        {
            MovementTrace trace = new MovementTrace
            {
                receiverIndex = activeShot.receiverIndex,
                endPosition = activeShot.lastReceiverPosition
            };
            trace.samples.AddRange(activeShot.movementSamples);
            completedMovementTraces.Add(trace);
        }
        activeShot = null;
    }

    private void SampleActiveShot(bool forceFinalSample, Vector3 currentBallPosition)
    {
        activeShot.ballPathDistance += Vector3.Distance(activeShot.lastBallPosition, currentBallPosition);
        activeShot.lastBallPosition = currentBallPosition;
        if (activeShot.receiverTransform == null)
            return;

        Vector3 receiverPosition = activeShot.receiverTransform.position;
        activeShot.receiverPathDistance += HorizontalDistance(activeShot.lastReceiverPosition, receiverPosition);
        activeShot.lastReceiverPosition = receiverPosition;
        float elapsed = Mathf.Max(0f, Time.time - activeShot.launchTime);
        if (!forceFinalSample && elapsed + 0.0001f < activeShot.nextMovementSampleAt)
            return;

        activeShot.movementSamples.Add(new MovementSample(elapsed, activeShot.receiverPathDistance, receiverPosition));
        if (!forceFinalSample)
            while (activeShot.nextMovementSampleAt <= elapsed)
                activeShot.nextMovementSampleAt += MovementSampleInterval;
    }

    private string BuildPointReport(string pointReason)
    {
        StringBuilder builder = new StringBuilder(1024);
        builder.Append("[RALLY FEEDBACK] Point ").Append(activePointNumber)
            .Append(" — ").Append(completedShots.Count).Append(" recorded shots (cap 10 per side)")
            .Append(" | ended: ").Append(pointReason);
        builder.Append("\nLaunch MPH");
        AppendLaunchLine(builder, PlayerIndex, "Player");
        AppendLaunchLine(builder, AIIndex, "AI");
        builder.Append("\nBall flight to receiver");
        AppendFlightLine(builder, PlayerIndex, "P→AI");
        AppendFlightLine(builder, AIIndex, "AI→P");
        builder.Append("\nMovement to contact");
        AppendMovementLine(builder, PlayerIndex, "Player");
        AppendMovementLine(builder, AIIndex, "AI");
        return builder.ToString();
    }

    private void AppendLaunchLine(StringBuilder builder, int hitterIndex, string label)
    {
        builder.Append("\n  ").Append(label).Append(": ");
        bool first = true;
        for (int i = 0; i < completedShots.Count; i++)
        {
            if (completedShots[i].hitterIndex != hitterIndex) continue;
            if (!first) builder.Append(", ");
            builder.Append(completedShots[i].launchMph.ToString("F0"));
            first = false;
        }
        if (first) builder.Append("—");
    }

    private void AppendFlightLine(StringBuilder builder, int hitterIndex, string label)
    {
        builder.Append("\n  ").Append(label).Append(": ");
        bool first = true;
        for (int i = 0; i < completedShots.Count; i++)
        {
            ShotRecord shot = completedShots[i];
            if (shot.hitterIndex != hitterIndex) continue;
            if (!first) builder.Append(", ");
            AppendDistanceAndTime(builder, shot.ballPathDistance, ShotDuration(shot));
            first = false;
        }
        if (first) builder.Append("—");
    }

    private void AppendMovementLine(StringBuilder builder, int receiverIndex, string label)
    {
        builder.Append("\n  ").Append(label).Append(": ");
        bool first = true;
        for (int i = 0; i < completedShots.Count; i++)
        {
            ShotRecord shot = completedShots[i];
            if (shot.receiverIndex != receiverIndex) continue;
            if (!first) builder.Append(", ");
            AppendDistanceAndTime(builder, shot.receiverPathDistance, ShotDuration(shot));
            first = false;
        }
        if (first) builder.Append("—");
    }

    private static void AppendDistanceAndTime(StringBuilder builder, float distance, float time)
    {
        builder.Append(distance.ToString("F1")).Append("m / ").Append(time.ToString("F2")).Append("s");
    }

    private static float ShotDuration(ShotRecord shot) => Mathf.Max(0f, shot.resolvedAt - shot.launchTime);
    private Transform TransformFor(int participantIndex) => participantIndex == PlayerIndex ? playerTransform : aiTransform;
    private static bool IsParticipant(int participantIndex) => participantIndex == PlayerIndex || participantIndex == AIIndex;
    private static int OpponentOf(int participantIndex) => participantIndex == PlayerIndex ? AIIndex : PlayerIndex;

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    public void DrawMovementGizmos()
    {
        if (!drawMovementGizmos)
            return;
        for (int i = 0; i < completedMovementTraces.Count; i++)
            DrawTrace(completedMovementTraces[i]);
    }

    private void DrawTrace(MovementTrace trace)
    {
        if (trace == null || trace.samples.Count == 0)
            return;
        Color color = trace.receiverIndex == PlayerIndex ? playerMovementColor : aiMovementColor;
        Gizmos.color = color;
        Vector3 previous = Raise(trace.samples[0].position);
        Gizmos.DrawWireSphere(previous, 0.08f);
        for (int i = 1; i < trace.samples.Count; i++)
        {
            MovementSample sample = trace.samples[i];
            Vector3 current = Raise(sample.position);
            Gizmos.DrawLine(previous, current);
            Vector3 tangent = current - previous;
            tangent.y = 0f;
            if (tangent.sqrMagnitude < 0.0001f) tangent = Vector3.right;
            Vector3 across = Vector3.Cross(Vector3.up, tangent.normalized) * 0.085f;
            Gizmos.DrawLine(current - across, current + across);
#if UNITY_EDITOR
            UnityEditor.Handles.color = color;
            UnityEditor.Handles.Label(current + Vector3.up * 0.04f, $"{sample.time:F2}s  {sample.pathDistance:F1}m");
#endif
            previous = current;
        }
        Gizmos.DrawSphere(Raise(trace.endPosition), 0.09f);
    }

    private static Vector3 Raise(Vector3 position) => position + Vector3.up * GizmoHeight;
}
