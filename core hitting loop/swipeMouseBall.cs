using System.Collections;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEditor.XR;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using static swipeMouseBall;

public class swipeMouseBall : MonoBehaviour
{
    // ----------------------------------------------------------------------
    // BALL PREFAB & SPAWNING
    // ----------------------------------------------------------------------
    // This section manages the lifecycle of the ball object. The script
    // does NOT create physics or shot behaviour â€” it simply spawns the
    // ball prefab at a designated spawn point and keeps a reference to
    // its Rigidbody for later use.
    //
    // Responsibilities:
    //   â€¢ Spawn a new ball when the scene starts or when the user presses G
    //   â€¢ Maintain a clean reference to the active ball instance
    //   â€¢ Pass this reference to hitController so it always hits the correct ball
    //
    // This ensures the shot system never interacts with stale or destroyed
    // ball objects, which is essential for consistency.
    // ----------------------------------------------------------------------
    [Header("Ball Prefab & Spawn")]
    public GameObject ballPrefab;      // Prefab only
    public GameObject ballInstance;    // Runtime instance
    public Transform spawnPoint;       // Spawn location
    public Rigidbody ball;             // Runtime Rigidbody reference

    [Header("Ball Cannon Feed")]
    public KeyCode cannonSpawnKey = KeyCode.C;
    public Transform cannonSpawnPoint;
    public Transform cannonTargetPoint;
    public ballCannon cannonSettings;
    public float cannonLaunchSpeed = 18f;
    public float cannonLaunchAngle = 12f;
    public Vector3 cannonFallbackDirection = Vector3.back;

    [Header("Cannon Embedded Settings")]
    public bool useSwipeMouseBallCannonSettings = true;
    public bool cannonRandomizeFeed = true;
    public bool cannonAimAtRandomTargetPoint = true;
    public bool cannonUseCourtBounds = true;
    public bool cannonAutoFindCourtBoundsByName = true;
    public Transform cannonBoundFL;
    public Transform cannonBoundFR;
    public Transform cannonBoundRR;
    public Transform cannonBoundRL;
    public string cannonBoundFLName = "AIBoundFL";
    public string cannonBoundFRName = "AIBoundFR";
    public string cannonBoundRRName = "AIBoundRR";
    public string cannonBoundRLName = "AIBoundRL";
    public bool cannonMirrorCourtBoundsAcrossNet = false;
    public bool cannonUseSymmetricLateralGrid = false;
    public bool cannonUseNetAsLateralCenter = false;
    public Vector2 cannonFallbackTargetXRange = new Vector2(-12.5f, -1f);
    public Vector2 cannonFallbackTargetZRange = new Vector2(-5f, 5f);
    public Vector2 cannonTargetCellPadding = new Vector2(0.35f, 0.35f);
    [Range(0f, 35f)] public float cannonNoLobMaxLaunchAngle = 18f;
    public bool cannonAvoidSameLateralColumnStreaks = true;
    [Range(1, 10)] public int cannonMaxSameLateralColumnStreak = 2;
    public ballCannon.FeedZoneProbabilities cannonZoneProbabilities = new ballCannon.FeedZoneProbabilities(
        0.10f, 0.10f, 0.10f,
        0.35f, 0.35f, 0.35f,
        1.00f, 1.00f, 1.00f
    );

    [Header("Cannon Shot Mix")]
    public ballCannon.FeedShotProbabilities cannonShotProbabilities = new ballCannon.FeedShotProbabilities(
        0.20f,
        0.45f,
        0.25f,
        0.10f,
        0.00f,
        0.00f
    );
    public ballCannon.FeedShotProfile cannonShortFlatProfile = new ballCannon.FeedShotProfile(
        new Vector2(12f, 19f),
        new Vector2(6f, 12f),
        new Vector2(0f, 250f)
    );
    public ballCannon.FeedShotProfile cannonDeepFlatProfile = new ballCannon.FeedShotProfile(
        new Vector2(15f, 25f),
        new Vector2(7f, 15f),
        new Vector2(0f, 350f)
    );
    public ballCannon.FeedShotProfile cannonTopspinProfile = new ballCannon.FeedShotProfile(
        new Vector2(15f, 26f),
        new Vector2(9f, 19f),
        new Vector2(1800f, 3200f)
    );
    public ballCannon.FeedShotProfile cannonSliceProfile = new ballCannon.FeedShotProfile(
        new Vector2(11f, 21f),
        new Vector2(6f, 14f),
        new Vector2(-1200f, -2400f)
    );
    public ballCannon.FeedShotProfile cannonLobProfile = new ballCannon.FeedShotProfile(
        new Vector2(13f, 20f),
        new Vector2(18f, 28f),
        new Vector2(500f, 1200f)
    );
    public ballCannon.FeedShotProfile cannonDropProfile = new ballCannon.FeedShotProfile(
        new Vector2(8f, 13f),
        new Vector2(10f, 18f),
        new Vector2(-500f, -1500f)
    );

    [Header("Cannon Solver Settings")]
    public bool cannonUseTrajectorySolver = true;
    public ShotSolverComponent cannonSolverComponent;
    public bool cannonAutoFindSolverComponent = true;
    public Transform cannonNetPoint;
    public bool cannonAutoFindNetPoint = true;
    public string cannonNetObjectName = "net";
    public bool cannonAutoSetNetHeightFromRenderer = true;
    public float cannonFeedNetHeight = 0.914f;
    public float cannonFeedNetMargin = 0.25f;
    public float cannonMinimumRandomFeedNetMargin = 0.45f;
    public bool cannonRequireSolvedRandomFeeds = true;
    public bool cannonAllowExtraSpeedForFlatFeed = true;
    public float cannonExtraFlatFeedMaxSpeed = 32f;
    public bool cannonLogSolverFailures = false;

    [Header("Cannon Auto Fire")]
    public bool cannonAutoFire = false;
    public KeyCode cannonAutoFireToggleKey = KeyCode.V;
    public float cannonAutoFireInterval = 3f;
    public bool cannonAutoFireImmediately = true;
    public bool cannonAutoFireWaitForDeadBall = true;
    public float cannonAutoFireDeadBallSpeed = 0.65f;
    public float cannonAutoFireRetryDelay = 0.25f;
    private float nextCannonAutoFireTime = -1f;

    // ----------------------------------------------------------------------
    // SHOT SETTINGS (RAW SWIPE OUTPUT)
    // ----------------------------------------------------------------------
    // These values represent the *raw* output of the swipe gesture before
    // any blending, error modelling, or shot shaping occurs.
    //
    // finalAimDir      â†’ the 2D swipe direction converted to world space
    // finalSwipeSpeed  â†’ the computed swipe speed (already in m/s)
    //
    // These values are passed directly into hitController, which then
    // applies shot error, reticle blending, and launch angle logic.
    //
    // NOTE:
    //   â€¢ Legacy forceMultiplier-based scaling has been removed.
    //   â€¢ finalSwipeSpeed now comes purely from the gesture power model
    //     (minShotPower â†’ maxShotPower, plus globalPowerScale).
    // ----------------------------------------------------------------------
    [Header("Shot Settings")]
    public Vector3 finalAimDir;
    public float finalSwipeSpeed;        
        
    // ----------------------------------------------------------------------
    // DEBUG & EXTERNAL REFERENCES
    // ----------------------------------------------------------------------
    // hitControllerInstance â†’ receives final swipe data and performs the hit
    // reticleInstance       â†’ UI reticle that locks during swipe arming
    // heightUI              â†’ UI element showing shot height feedback
    //
    // These references allow swipeMouseBall to coordinate with the rest of
    // the shot system without owning any physics or shot logic itself.
    // ----------------------------------------------------------------------
    [Header("Debug")]
    public bool debugLogs = false;
    public hitController hitControllerInstance;


    [Header("References")]
    public hitController hitController;
    public UIWorldReticle reticleInstance;
    public ShotHeightUI heightUI;

    [Header("Shot Trace UI")]
    public SwingPathUI swingPathUI;
    public bool autoFindSwingPathUI = true;

    [Header("Lateral Aim Helper")]
    public bool enableLateralAimHelper = false;
    public bool autoCreateLateralAimHelper = false;
    public LateralAimLineHelper lateralAimLineHelper;

    [Header("Lateral Aim Reticle Gauge")]
    public bool enableLateralAimReticleGauge = true;
    public bool autoCreateLateralAimReticleGauge = true;
    public LateralAimReticleGauge lateralAimReticleGauge;

    [Header("Zone Feedback")]
    public bool enableZoneFeedback = true;
    public bool autoFindReticleRenderers = true;
    public Color reticleDefaultColor = Color.white;
    public Color reticleSwingReadyColor = Color.cyan;
    public Color reticleTightHitColor = Color.green;
    public bool enableHeightIntentReticleColor = true;
    public bool heightIntentOverridesSwingReadyColor = true;
    public Color reticleLowHeightColor = Color.green;
    public Color reticleDefaultHeightColor = Color.white;
    public Color reticleHighHeightColor = Color.blue;
    public float reticleTightHitFlashSeconds = 1f;
    [Header("Debug Zone Visuals")]
    public bool showRuntimeZoneFlashMeshes = false;
    public Color zoneFlashColor = new Color(0.1f, 0.55f, 1f, 0.35f);
    public float zoneFlashSeconds = 1f;
    public float zoneFlashLineWidth = 0.035f;
    public bool flashSwingZoneOnEntry = true;
    public bool flashTightZoneOnEntry = true;

    // Debug previous-state trackers
    private bool prevLMB = false;
    private bool prevRMB = false;
    private bool prevInZone = false;
    private Renderer[] reticleFeedbackRenderers;
    private Color[] reticleFeedbackDefaultColors;
    private bool reticleFeedbackDefaultsCaptured;
    private bool feedbackHasZoneHistory;
    private bool feedbackPreviousSwingZone;
    private bool feedbackPreviousTightZone;
    private float reticleTightFlashUntil;
    private float swingZoneFlashUntil;
    private float tightZoneFlashUntil;
    private GameObject swingZoneFlashVisual;
    private GameObject tightZoneFlashVisual;
    private Material zoneFlashMaterial;
    private float prevBackswingScale = -1f;
    // Debug previous-state trackers
    private float prevSwipeStartTime = -1f;


    // ----------------------------------------------------------------------
    // SWIPE STATE VARIABLES
    // ----------------------------------------------------------------------
    // These fields track the swipe gesture from the moment it begins until
    // the moment it ends. This script uses a *movementâ€‘based* detection
    // model instead of Unityâ€™s builtâ€‘in mouse delta, giving smoother,
    // more expressive input and better control over swipe quality.
    //
    // swipeStart          â†’ screen position where the swipe began
    // swipeStartTime      â†’ timestamp of swipe start (used for duration)
    // swipeDetected       â†’ whether a completed swipe has been registered
    //
    // lastSwipeDir        â†’ direction of the previous processed swipe frame
    // lastSwipeSpeed      â†’ speed of the previous processed swipe frame
    //
    // swipeEnd            â†’ last recorded mouse position during the swipe
    // swipeInProgress     â†’ true while the player is actively swiping
    //
    // lastMousePos        â†’ previous frameâ€™s mouse position (for movement delta)
    // mouseStillTimer     â†’ how long the mouse has remained below movement threshold
    //
    // finalNormDist       â†’ normalized swipe distance (0â€“1), used for power + quality
    //
    // These values feed into:
    //   â€¢ gesture classification (short/medium/long)
    //   â€¢ swipe quality metrics
    //   â€¢ power model (distance, duration, avg speed)
    // ----------------------------------------------------------------------
    private Vector2 swipeStart;
    private float swipeStartTime;
    private bool swipeDetected = false;

    private Vector2 lastSwipeDir = Vector2.zero;
    private float lastSwipeSpeed = 0f;

    private Vector2 swipeEnd;
    private bool swipeInProgress = false;
    private Vector2 lastMousePos;
    public float finalNormDist;

    [Header("Swipe Thresholds")]
    public float minSwipeStartDistanceCm = 1.2f;
    public float swipeMovementSpeedThresholdCmPerSecond = 15f;
    public float swipeStillDurationSeconds = 0.05f;
    public float swipeStillDeltaPixels = 0.001f;
    [Tooltip("Frame-level grace window for a mouse press to arm as the ball enters the hit zone.")]
    public float swipeArmPressBufferSeconds = 0.10f;
    private bool ignoreFirstFrame = false;

    [Header("Shot Type Direction")]
    public float shotTypeDurationDeadZone = 0.04f;
    public bool invertShotTypeDirectionY = false;

    [Header("Swipe Aim Direction")]
    public float maxSwipeLateralAimAngleDeg = 30f;
    [Range(0f, 0.5f)]
    public float swipeLateralDeadZone = 0.15f;
    [Range(0f, 1f)]
    public float minSwipeForwardForAim = 0.35f;
    public bool invertSwipeLateralAim = false;

    [Header("Tight Hit Zone")]
    public bool requireTightHitZone = true;
    public Transform tightHitZoneCenter;
    public Vector3 tightHitZoneLocalOffset = new Vector3(0f, 1.50f, 0f);
    public Vector3 tightHitZoneRadii = new Vector3(2.15f, 1.60f, 2.55f);
    public bool extendTightHitZoneToStandingOverhead = true;
    [Tooltip("Maximum standing overhead racket-contact height relative to the player root; no jump included.")]
    public float standingOverheadTightZoneTop = 3.10f;
    public bool logTightHitZoneHeight = true;
    public float tightHitZonePendingWindow = 1.0f;
    public bool processTightHitZoneInFixedUpdate = true;
    [Tooltip("Treat a fast ball crossing the authoritative player contact zone between physics ticks as a valid contact.")]
    public bool useSweptPlayerContactDetection = true;
    public bool logSweptPlayerContacts = true;
    public float sweptPlayerContactBallRadius = 0.033f;
    public bool drawTightHitZoneGizmo = true;
    public float fullForwardSwingDurationSeconds = 0.38f;
    public Color tightHitZoneGizmoColor = new Color(0.25f, 1f, 0.25f, 0.35f);

    [Header("Player Serve Contact")]
    [Tooltip("How long a completed forward serve swipe waits for the tossed ball to enter the low/high contact-marker window.")]
    public float servePreparedContactWindowSeconds = 0.65f;
    [Tooltip("The match toss timer owns serve timing, so the normal short rally swipe-arm timeout is not applied while serving.")]
    public bool keepPlayerServeSwipeArmedForToss = true;
    [Tooltip("Releasing either shot button after forward motion explicitly completes a player serve swipe.")]
    public bool completePlayerServeSwipeOnButtonRelease = true;
    public bool logPlayerServeContact = true;

    [Header("AI Serve Return")]
    [Tooltip("Allow a completed player return swipe to wait through the AI's serve flight until the legal post-bounce return phase begins.")]
    public bool deferPlayerReturnUntilAIServeBounce = true;
    public float aiServeReturnDeferralSeconds = 0.90f;
    public bool logAIServeReturnDeferrals = true;

    // Swipe Mouse State Varibles
    private bool isLMB;
    private bool isRMB;
    private float lastSwipeButtonPressTime = -999f;
    private bool bufferedSwipePressWasLMB;
    private bool bufferedSwipePressWasRMB;


    // ----------------------------------------------------------------------
    // BACKSWING POWER (LMB/RMB HOLD BEFORE SWIPE)
    // ----------------------------------------------------------------------
    // These fields track how long the player holds LMB/RMB BEFORE the swipe.
    // Holding increases the *raw swipe speed* before it is passed to HitController.
    // This avoids doubleâ€‘multiplying manual speed later.
    //
    // backswingScale        â†’ 0..1 based on hold duration
    // maxHoldTime           â†’ time to reach full power
    // maxBackswingPower     â†’ how much extra speed full hold adds (e.g., +30%)
    // holdStartTime         â†’ timestamp when LMB/RMB was pressed
    //
    // IMPORTANT:
    //   â€¢ This multiplier is applied to the *raw swipe speed* BEFORE it becomes
    //     finalSwipeSpeed.
    //   â€¢ HitController receives the ALREADYâ€‘boosted speed.
    //   â€¢ The solver receives the multiplier so it can compute a higher target V0
    //     and a flatter angle.
    // ----------------------------------------------------------------------

    [Header("Backswing Power")]
    public float maxHoldTime = 1.5f;          // seconds to reach full power
    public float maxBackswingPower = 0.3334f; // 60 mph base -> about 80 mph full boost

    [Header("Slice Power")]
    public bool sliceIgnoresBackswingPowerCap = false; // Legacy; retention is owned by ShotComputationSolver.
    [Tooltip("Legacy serialized setting; slice retention is applied by ShotComputationSolver.")]
    public float sliceNoBackswingPostRetentionCapMph = 60f;

    public float backswingScale = 0f;         // 0..1 (updated every frame)

    [Header("Forward Swing Control")]
    [Range(0f, 1f)]
    public float minForwardControlHoldRatio = 0.65f;
    public float minForwardControlHoldSeconds = 0.035f;

    [Header("Forward Swing Spin")]
    public float minForwardSpinHoldSeconds = 0.15f;
    public float maxForwardSpinHoldSeconds = 0.30f;

    // BACKSWING STATE
    private bool backswingActive = false;
    private float backswingStartTime = 0f;
    private float frozenBackswing = 0f;       // stored power for the shot

    // State for motion-based arming
    private Vector2 backswingMotionStartPos; // where we started measuring back motion

    // BACKSWING SYSTEM
    private Vector2 backswingStartPos;     // where the mouse was when backswing began
    private Vector2 backswingDelta;        // how far the mouse moved during backswing

    
    private Vector2 swipeArmingPos = Vector2.zero;     // baseline position captured when swipeArmed becomes true


    // ----------------------------------------------------------------------
    // ADVANCED SWIPE METRICS
    // ----------------------------------------------------------------------
    // These fields track more nuanced aspects of the swipe:
    //
    // earlySwipeDir / lateSwipeDir     â†’ direction stability
    // totalDirectionWobble             â†’ how much the swipe direction wobbled
    // totalSpeedWobble                 â†’ how much swipe speed fluctuated
    //
    // These metrics are used to compute finalShotQuality, which influences
    // shot error (direction, depth, height) in ShotDirectionCalculator.
    //
    // This gives your game expressive, humanâ€‘like variability.
    // ----------------------------------------------------------------------
    private float accumulatedDistance = 0f;
    private Vector2 earlySwipeDir = Vector2.zero;
    private Vector2 lateSwipeDir = Vector2.zero;
    private float swipeCompletionTime = 0f;
    private bool waitingForTightHitZone = false;
    private float tightHitZoneDeadline = 0f;
    private int lastTightHitZoneHeightLoggedShot = -1;
    private float swipeMotionStartTime = 0f;
    private int swipeMotionStartSampleSequence = -1;
    private int swipeCompletionSampleSequence = -1;
    private float forwardControlHoldTime = 0f;
    private float forwardControlElapsedTime = 0f;
    private float finalForwardControlHoldScale = 0f;
    private float forwardSpinHoldTime = 0f;
    private float finalForwardSpinIntent = 0f;
    private bool hasTightHitZoneHistory = false;
    private bool previousBallInTightHitZone = false;
    private bool wasBallInSwingZone = false;
    private float swingZoneEnterTime = -1f;
    private bool sweptPlayerContactDetectedThisTick;
    private HitContactConfirmation sweptPlayerContactThisTick;
    private bool hasPlayerPhysicsContactSample;
    private int playerPhysicsContactShotKey = -1;
    private Vector3 previousPlayerPhysicsBallPosition;
    private Vector3 previousPlayerPhysicsZoneCenter;
    private Quaternion previousPlayerPhysicsZoneRotation = Quaternion.identity;

    private bool pendingPhysicsShot;
    private SwipeData pendingSwipeData;
    private BaseShotType pendingBaseType;
    private ShotModifier pendingModifier;
    private InputDirectionSampler120Hz pendingTraceSampler;
    private int pendingTraceEndSequence;
    private Vector3 pendingTraceTargetDir;
    private float pendingTraceManualSpeed;
    private float pendingTraceBlendedFallbackSpeed;
    private float pendingTraceTargetSpeed;
    private float pendingTraceBackswingCapSpeed;
    private float pendingTraceMaxScaleSpeed;
    private float pendingTraceRawSwipeSpeedCentimetresPerSecond;
    private float pendingTraceRawSwipeDurationSeconds;
    private float pendingTraceRawSwipeDistanceCentimetres;
    private float pendingAIServeReturnDeferralDeadline;
    private bool pendingAIServeReturnDeferralLogged;
    private float pendingShotQueuedTime;
    private float pendingShotSwipeCompletionTime;
    private HitContactConfirmation pendingHitContactConfirmation;
    private bool playerServeContactLoggedForPreparedSwipe;


    // ----------------------------------------------------------------------
    // SWIPE CLASSIFICATION RESULT STRUCT
    // ----------------------------------------------------------------------
    private struct SwipeClassification
    {
        public bool isShort;
        public bool isLong;
        public float normAvg;
        public float normDist;
        public float normDur;
    }

    // ----------------------------------------------------------------------
    // CLASSIFY SWIPE (duration, distance, avg speed â†’ categories)
    // ----------------------------------------------------------------------
    private SwipeClassification ClassifySwipe(
        float duration,
        float distance,
        float avgSpeed
    )
    {
        SwipeClassification c = new SwipeClassification();
        // Screen-space swipe distances are DPI-scaled and can wrap vertically.
        c.isShort = duration < 0.08f || distance < 12f;
        c.isLong = duration > 0.32f || distance > 65f;

        // A normal committed swipe can be 40-70cm in this screen-space model.
        c.normAvg = Mathf.InverseLerp(60f, 400f, avgSpeed);
        c.normDist = Mathf.InverseLerp(8f, 70f, distance);
        c.normDur = Mathf.InverseLerp(0.09f, 0.36f, duration);

        return c;
    }

    // ----------------------------------------------------------------------
    // ENUM â€” SwipeType
    // ----------------------------------------------------------------------
    // Classifies the swipe into Short, Medium, or Long.
    // Used by ComputeShotQuality and other swipeâ€‘related helpers.
    // ----------------------------------------------------------------------
    public enum SwipeType
    {
        Short,
        Medium,
        Long
    }

    // ----------------------------------------------------------------------
    // DATA STRUCT â€” SwipeData
    // ----------------------------------------------------------------------
    // Bundles all raw swipe output into a single, clean container.
    // This makes HitBallUsingSwipe easier to extend and keeps the API tidy.
    // ----------------------------------------------------------------------
    public struct SwipeData
    {
        public bool isLMB;
        public bool isRMB;

        public Vector3 aimDir;
        public float speed;
        public float quality;
        public float normDist;
        public float holdScale;
        public float spinIntent;
        public float backswingScale;
        public float forwardSwingProgress;
        public float backswingCapSpeed;

        public SwipeData(
            bool isLMB,
            bool isRMB,
            Vector3 aimDir,
            float speed,
            float quality,
            float normDist,
            float holdScale = 0f,
            float spinIntent = 0f,
            float backswingScale = 1f,
            float forwardSwingProgress = 1f,
            float backswingCapSpeed = 0f)
        {
            this.isLMB = isLMB;
            this.isRMB = isRMB;
            this.aimDir = aimDir;
            this.speed = speed;
            this.quality = quality;
            this.normDist = normDist;
            this.holdScale = Mathf.Clamp01(holdScale);
            this.spinIntent = Mathf.Clamp01(spinIntent);
            this.backswingScale = Mathf.Clamp01(backswingScale);
            this.forwardSwingProgress = Mathf.Clamp01(forwardSwingProgress);
            this.backswingCapSpeed = Mathf.Max(0f, backswingCapSpeed);
        }
    }



    // ----------------------------------------------------------------------
    // FINAL SHOT OUTPUT VALUES
    // ----------------------------------------------------------------------
    // finalShotQuality â†’ 0â€“1 measure of swipe stability and control
    // finalLaunchAngle â†’ vertical angle used by HitController
    //
    // These values are computed at the end of the swipe and passed directly
    // into HitController, which uses them to build the final launch velocity.
    // ----------------------------------------------------------------------
    public float finalShotQuality;
   
    // ----------------------------------------------------------------------
    // SWIPE ARMING SYSTEM
    // ----------------------------------------------------------------------
    // A swipe is only valid if:
    //   â€¢ the player is holding LMB
    //   â€¢ the ball is inside the hitting zone
    //   â€¢ the swipe begins within maxSwipeDelay seconds
    //
    // This prevents accidental swipes and ensures the player can only hit
    // when the ball is actually hittable.
    // ----------------------------------------------------------------------
    private bool swipeArmed = false;
    private float armTime;
    public float maxSwipeDelay = 0.45f;

    // Prevents re-arming while mouse is still held after a swipe
    private bool swipeConsumedUntilRelease = false;


    //Mouse Initialised
    private bool mouseInitialized = false;

    //120 Hz shared input sampling
    private InputDirectionSampler120Hz inputSampler;
    private Vector2 sampledMousePos;
    private float dpi;
    private int lastConsumedSampleSequence = -1;
    private int swipeStartSampleSequence = -1;
    private Vector2 weightedSwipeDir;
    private float weightedSwipeDirTotal;

    [Header("Gameplay Cursor")]
    public bool enableGameplayCursorWrapping = true;
    public bool wrapCursorWhileSwipeArmed = true;

    // ----------------------------------------------------------------------
    // START â€” INITIAL BALL SPAWN
    // ----------------------------------------------------------------------
    // When the scene loads, we spawn the first ball immediately. This keeps
    // the game ready for input without requiring any manual setup.
    // ----------------------------------------------------------------------
    void Start()
    {
        EnsureStandingOverheadTightZone();
        ConfigureSharedAuthoritativeContactZone();
        inputSampler = InputDirectionSampler120Hz.EnsureExists();
        dpi = inputSampler.Dpi;
        sampledMousePos = inputSampler.CurrentPosition;
        lastConsumedSampleSequence = inputSampler.LatestSequence;
        swipeStartSampleSequence = lastConsumedSampleSequence;

        if (debugLogs) Debug.Log("[swipeMouseBall] Start -> Spawning initial ball");
        SpawnNewBall();
        EnsureLateralAimLineHelper();
        EnsureLateralAimReticleGauge();
    }

    private void ConfigureSharedAuthoritativeContactZone()
    {
        hitController controller = hitControllerInstance != null ? hitControllerInstance : hitController;
        if (controller == null)
            return;

        controller.ConfigureAuthoritativeContactZone(
            GetTightHitZoneCenter(),
            tightHitZoneLocalOffset,
            tightHitZoneRadii);
    }

    private void EnsureStandingOverheadTightZone()
    {
        if (!extendTightHitZoneToStandingOverhead)
            return;

        float currentRadiusY = Mathf.Max(0.01f, Mathf.Abs(tightHitZoneRadii.y));
        float lowerLimit = tightHitZoneLocalOffset.y - currentRadiusY;
        float upperLimit = Mathf.Max(
            tightHitZoneLocalOffset.y + currentRadiusY,
            standingOverheadTightZoneTop);
        tightHitZoneLocalOffset.y = (lowerLimit + upperLimit) * 0.5f;
        tightHitZoneRadii.y = (upperLimit - lowerLimit) * 0.5f;

        if (debugLogs)
            Debug.Log($"[TIGHT HIT] Player vertical reach={lowerLimit:F2}m to {upperLimit:F2}m (standing overhead).");
    }

    private void EnsureLateralAimLineHelper()
    {
        if (lateralAimLineHelper != null)
        {
            lateralAimLineHelper.swipeSource = this;
            return;
        }

        if (!enableLateralAimHelper || !autoCreateLateralAimHelper)
            return;

        GameObject helperObject = new GameObject("Lateral Aim Line Helper");
        helperObject.transform.SetParent(transform, false);
        lateralAimLineHelper = helperObject.AddComponent<LateralAimLineHelper>();
        lateralAimLineHelper.swipeSource = this;
        lateralAimLineHelper.helperEnabled = enableLateralAimHelper;
    }

    private void EnsureLateralAimReticleGauge()
    {
        if (lateralAimReticleGauge != null)
        {
            lateralAimReticleGauge.swipeSource = this;
            return;
        }

        if (!enableLateralAimReticleGauge || !autoCreateLateralAimReticleGauge)
            return;

        GameObject helperObject = new GameObject("Lateral Aim Reticle Gauge");
        helperObject.transform.SetParent(transform, false);
        lateralAimReticleGauge = helperObject.AddComponent<LateralAimReticleGauge>();
        lateralAimReticleGauge.swipeSource = this;
        lateralAimReticleGauge.helperEnabled = enableLateralAimReticleGauge;
    }

    private InputDirectionSampler120Hz GetInputSampler()
    {
        if (inputSampler == null)
            inputSampler = InputDirectionSampler120Hz.EnsureExists();

        return inputSampler;
    }

    private void SetGameplayCursorWrapping(bool active)
    {
        if (!enableGameplayCursorWrapping || !wrapCursorWhileSwipeArmed)
            active = false;

        InputDirectionSampler120Hz sampler = GetInputSampler();
        if (sampler != null)
            sampler.SetVirtualCursorWrapping(active);
    }

    private void ReleaseGameplayCursor()
    {
        if (inputSampler != null)
            inputSampler.SetVirtualCursorWrapping(false);
    }

    private void UnlockReticle()
    {
        if (reticleInstance != null)
            reticleInstance.reticleLocked = false;

        ReleaseGameplayCursor();
    }

    private Transform GetTightHitZoneCenter()
    {
        if (tightHitZoneCenter != null)
            return tightHitZoneCenter;

        hitController controller = hitControllerInstance != null ? hitControllerInstance : hitController;
        if (controller != null && controller.contactPoint != null)
            return controller.contactPoint;

        if (controller != null)
            return controller.transform;

        return transform;
    }

    private Vector3 GetTightHitZoneWorldCenter()
    {
        Transform center = GetTightHitZoneCenter();
        if (center == null)
            return transform.position;

        return center.position + center.rotation * tightHitZoneLocalOffset;
    }

    private bool IsBallInTightHitZone()
    {
        if (!requireTightHitZone)
            return true;

        Transform center = GetTightHitZoneCenter();
        if (center == null)
            return false;

        Vector3 ballPos;
        if (ball != null)
            ballPos = ball.position;
        else if (ballInstance != null)
            ballPos = ballInstance.transform.position;
        else
            return false;

        Quaternion centerRotation = center.rotation;
        Vector3 worldCenter = center.position + centerRotation * tightHitZoneLocalOffset;
        Vector3 localOffset = Quaternion.Inverse(centerRotation) * (ballPos - worldCenter);
        float radiusX = Mathf.Max(0.01f, Mathf.Abs(tightHitZoneRadii.x));
        float radiusY = Mathf.Max(0.01f, Mathf.Abs(tightHitZoneRadii.y));
        float radiusZ = Mathf.Max(0.01f, Mathf.Abs(tightHitZoneRadii.z));

        float normalized =
            (localOffset.x * localOffset.x) / (radiusX * radiusX) +
            (localOffset.y * localOffset.y) / (radiusY * radiusY) +
            (localOffset.z * localOffset.z) / (radiusZ * radiusZ);

        bool inside = normalized <= 1f;
        if (inside && logTightHitZoneHeight)
        {
            BallController controller = ball != null ? ball.GetComponent<BallController>() :
                ballInstance != null ? ballInstance.GetComponent<BallController>() : null;
            int shotSequence = controller != null ? controller.ShotSequence : 0;
            int shotKey = controller != null ? (controller.GetInstanceID() * 397) ^ shotSequence : 0;
            if (shotKey != lastTightHitZoneHeightLoggedShot)
            {
                lastTightHitZoneHeightLoggedShot = shotKey;
                Debug.Log($"[TIGHT HEIGHT PLAYER] shot={shotSequence} ballHeight={ballPos.y:F2}m local={localOffset} " +
                    $"zoneCenter={worldCenter} verticalRange=[{worldCenter.y - radiusY:F2},{worldCenter.y + radiusY:F2}] normalized={normalized:F3}.");
            }
        }

        return inside;
    }

    private bool IsPlayerServeTossActive()
    {
        MatchServicePointController match = MatchServicePointController.Active;
        return match != null && match.IsPlayerServeTossActive(hitControllerInstance);
    }

    private bool TryGetActiveContact(out HitContactConfirmation confirmation)
    {
        confirmation = default;
        MatchServicePointController match = MatchServicePointController.Active;
        if (match != null && match.IsPlayerServeTossActive(hitControllerInstance))
            return match.TryGetPlayerServeContact(hitControllerInstance, out confirmation);

        if (IsBallInTightHitZone())
        {
            confirmation = HitContactConfirmation.Confirmed(ball != null ? ball.position : Vector3.zero, false);
            return true;
        }

        if (sweptPlayerContactDetectedThisTick)
        {
            confirmation = sweptPlayerContactThisTick;
            return true;
        }

        return false;
    }

    private void RememberActiveContact(HitContactConfirmation confirmation)
    {
        if (!confirmation.confirmed)
            return;

        pendingHitContactConfirmation = confirmation;
        if (!logPlayerServeContact || playerServeContactLoggedForPreparedSwipe || !IsPlayerServeTossActive())
            return;

        playerServeContactLoggedForPreparedSwipe = true;
        Vector3 velocity = ball != null ? ball.linearVelocity : Vector3.zero;
        string swipeState = waitingForTightHitZone ? "WaitingForContact" :
            swipeInProgress ? "ForwardSwipe" : swipeDetected ? "Completed" :
            swipeArmed ? "ArmedBackswing" : "Idle";
        Debug.Log($"[PLAYER SERVE CONTACT] point={confirmation.contactPosition} verticalSpeed={velocity.y:F2}m/s " +
            $"swipeState={swipeState}.");
    }

    private Vector3 GetSafeTightHitZoneRadii()
    {
        return new Vector3(
            Mathf.Max(0.01f, Mathf.Abs(tightHitZoneRadii.x)),
            Mathf.Max(0.01f, Mathf.Abs(tightHitZoneRadii.y)),
            Mathf.Max(0.01f, Mathf.Abs(tightHitZoneRadii.z))
        );
    }

    private void CompleteSwipeForTightHit(string reason, bool updateCompletionTime)
    {
        if (updateCompletionTime || swipeCompletionTime <= swipeStartTime)
            swipeCompletionTime = Time.time;

        if (inputSampler != null &&
            (updateCompletionTime || swipeCompletionSampleSequence < swipeStartSampleSequence))
        {
            swipeCompletionSampleSequence = inputSampler.LatestSequence;
        }

        FreezeForwardControlHold();

        waitingForTightHitZone = false;
        tightHitZoneDeadline = 0f;
        swipeDetected = true;
        swipeInProgress = false;
        swipeArmed = false;
        swipeConsumedUntilRelease = true;
        ReleaseGameplayCursor();

        if (debugLogs)
        {
            if (IsPlayerServeTossActive())
                Debug.Log($"[PLAYER SERVE SWIPE COMPLETE] reason=\"{reason}\" contactConfirmed={pendingHitContactConfirmation.confirmed}.");
            else
                Debug.Log($"[TIGHT HIT] Contact accepted: {reason}");
        }
    }

    private void BeginPendingTightHitZone()
    {
        if (swipeCompletionTime <= swipeStartTime)
            swipeCompletionTime = Time.time;

        if (inputSampler != null && swipeCompletionSampleSequence < swipeStartSampleSequence)
            swipeCompletionSampleSequence = inputSampler.LatestSequence;

        FreezeForwardControlHold();
        FreezeForwardSpinIntent();

        bool playerServeToss = IsPlayerServeTossActive();
        float pendingWindow = playerServeToss
            ? Mathf.Max(0f, servePreparedContactWindowSeconds)
            : Mathf.Max(0f, tightHitZonePendingWindow);
        waitingForTightHitZone = true;
        tightHitZoneDeadline = swipeCompletionTime + pendingWindow;
        swipeDetected = false;
        swipeInProgress = false;
        swipeArmed = true;
        swipeConsumedUntilRelease = true;
        ReleaseGameplayCursor();

        if (debugLogs)
        {
            if (playerServeToss)
                Debug.Log($"[PLAYER SERVE PREPARED] Forward swipe complete; waiting {pendingWindow:F2}s for the low/high service contact window.");
            else
                Debug.Log($"[TIGHT HIT] Swing finished early. Waiting {pendingWindow:F2}s for ball to enter tight hit zone.");
        }
    }

    private void FreezeForwardControlHold()
    {
        finalForwardControlHoldScale = ComputeForwardControlHoldScale();
    }

    private void FreezeForwardSpinIntent()
    {
        finalForwardSpinIntent = ComputeForwardSpinIntent();
    }

    private float ComputeForwardControlHoldScale()
    {
        float elapsed = Mathf.Max(0.001f, forwardControlElapsedTime);
        float ratio = Mathf.Clamp01(forwardControlHoldTime / elapsed);

        if (forwardControlHoldTime < Mathf.Max(0f, minForwardControlHoldSeconds))
            return 0f;

        if (ratio < Mathf.Clamp01(minForwardControlHoldRatio))
            return 0f;

        return ratio;
    }

    private float ComputeForwardSpinIntent()
    {
        float minHold = Mathf.Max(0f, minForwardSpinHoldSeconds);
        float maxHold = Mathf.Max(minHold + 0.001f, maxForwardSpinHoldSeconds);
        return Mathf.Clamp01(Mathf.InverseLerp(minHold, maxHold, forwardSpinHoldTime));
    }

    private void CancelPendingTightHitZone(string reason)
    {
        if (debugLogs)
        {
            if (IsPlayerServeTossActive())
            {
                Vector3 position = ball != null ? ball.position : Vector3.zero;
                Vector3 velocity = ball != null ? ball.linearVelocity : Vector3.zero;
                Debug.Log($"[PLAYER SERVE CONTACT MISSED] reason=\"{reason}\" ball={position} velocity={velocity}.");
            }
            else
                Debug.Log($"[TIGHT HIT] Miss: {reason}");
        }

        waitingForTightHitZone = false;
        tightHitZoneDeadline = 0f;
        swipeCompletionTime = 0f;
        swipeMotionStartTime = 0f;
        swipeMotionStartSampleSequence = -1;
        swipeCompletionSampleSequence = -1;
        forwardControlHoldTime = 0f;
        forwardControlElapsedTime = 0f;
        finalForwardControlHoldScale = 0f;
        forwardSpinHoldTime = 0f;
        finalForwardSpinIntent = 0f;
        hasTightHitZoneHistory = false;
        previousBallInTightHitZone = false;
        pendingHitContactConfirmation = default;
        playerServeContactLoggedForPreparedSwipe = false;
        swipeDetected = false;
        swipeInProgress = false;
        swipeArmed = false;
        swipeConsumedUntilRelease = true;
        accumulatedDistance = 0f;
        isLMB = false;
        isRMB = false;
        frozenBackswing = 0f;
        backswingScale = 0f;
        backswingActive = false;
        if (reticleInstance != null)
            reticleInstance.SetSwingZoneLockActive(false);
        UnlockReticle();
    }

    private void ResetTightHitZoneState()
    {
        waitingForTightHitZone = false;
        tightHitZoneDeadline = 0f;
        swipeCompletionTime = 0f;
        swipeMotionStartTime = 0f;
        swipeMotionStartSampleSequence = -1;
        swipeCompletionSampleSequence = -1;
        forwardControlHoldTime = 0f;
        forwardControlElapsedTime = 0f;
        finalForwardControlHoldScale = 0f;
        forwardSpinHoldTime = 0f;
        finalForwardSpinIntent = 0f;
        hasTightHitZoneHistory = false;
        previousBallInTightHitZone = false;
        pendingHitContactConfirmation = default;
        playerServeContactLoggedForPreparedSwipe = false;
    }

    private bool TryCompleteSwipeOnTightHitZoneEdge(bool ballEnteredTightHitZone, bool ballExitedTightHitZone)
    {
        if (!swipeInProgress)
            return false;

        if (ballExitedTightHitZone)
        {
            CompleteSwipeForTightHit("ball exited tight zone while swing was in progress", true);
            return true;
        }

        if (ballEnteredTightHitZone)
        {
            CompleteSwipeForTightHit("ball entered tight zone while swing was in progress", true);
            return true;
        }

        return false;
    }

    private void ShowShotTrace(
        InputDirectionSampler120Hz sampler,
        int sequenceInclusive,
        Vector3 targetDir,
        float manualSpeedMetresPerSecond,
        float blendedSpeedMetresPerSecond,
        float targetSpeedMetresPerSecond,
        float backswingCapSpeedMetresPerSecond,
        float maxScaleSpeedMetresPerSecond,
        float rawSwipeSpeedCentimetresPerSecond,
        float rawSwipeDurationSeconds,
        float rawSwipeDistanceCentimetres)
    {
        if (swingPathUI == null && autoFindSwingPathUI)
            swingPathUI = FindFirstObjectByType<SwingPathUI>();

        if (swingPathUI == null)
            return;

        swingPathUI.ShowFromSampler(
            sampler,
            swipeStartSampleSequence,
            sequenceInclusive,
            targetDir,
            blendedSpeedMetresPerSecond,
            manualSpeedMetresPerSecond,
            targetSpeedMetresPerSecond,
            backswingCapSpeedMetresPerSecond,
            maxScaleSpeedMetresPerSecond,
            rawSwipeSpeedCentimetresPerSecond,
            rawSwipeDurationSeconds,
            rawSwipeDistanceCentimetres
        );
    }

    private Vector3 GetReticleTargetDirectionForTrace()
    {
        if (reticleInstance == null)
            return finalAimDir;

        Vector3 origin = GetTraceContactPosition();
        Vector3 targetDir = reticleInstance.transform.position - origin;
        targetDir.y = 0f;

        if (targetDir.sqrMagnitude < 1e-6f)
            return finalAimDir.sqrMagnitude > 1e-6f ? finalAimDir.normalized : Vector3.forward;

        return targetDir.normalized;
    }

    private Vector3 GetTraceContactPosition()
    {
        hitController controller = hitControllerInstance != null ? hitControllerInstance : hitController;

        if (controller != null && controller.ballIsInHittingZone && controller.ball != null)
            return controller.ball.transform.position;

        if (controller != null && controller.contactPoint != null)
            return controller.contactPoint.position;

        if (ballInstance != null)
            return ballInstance.transform.position;

        return transform.position;
    }

    public bool LateralAimSwipeArmed => swipeArmed;
    public bool LateralAimSwipeActive => swipeInProgress || waitingForTightHitZone;
    public bool LateralAimSwipeInProgress => swipeInProgress;
    public bool LateralAimWaitingForTightHitZone => waitingForTightHitZone;
    public float LateralAimMaxAngleDeg => Mathf.Abs(maxSwipeLateralAimAngleDeg);
    public hitController LateralAimHitController => hitControllerInstance != null ? hitControllerInstance : hitController;

    public bool TryGetLateralAimCourse(out Vector3 origin, out Vector3 reticlePosition, out Vector3 reticleDir)
    {
        origin = Vector3.zero;
        reticlePosition = Vector3.zero;
        reticleDir = Vector3.zero;

        hitController controller = LateralAimHitController;
        Transform reticleTransform = reticleInstance != null ? reticleInstance.transform : null;
        if (reticleTransform == null && controller != null)
            reticleTransform = controller.reticle;

        if (reticleTransform == null)
            return false;

        origin = GetTraceContactPosition();
        reticlePosition = reticleTransform.position;
        reticleDir = reticlePosition - origin;
        reticleDir.y = 0f;
        if (reticleDir.sqrMagnitude < 1e-6f)
            return false;

        reticleDir.Normalize();
        return true;
    }

    public bool TryGetLiveLateralAim(out Vector3 origin, out Vector3 reticlePosition, out Vector3 aimDir, out float angleToReticleDeg)
    {
        origin = Vector3.zero;
        reticlePosition = Vector3.zero;
        aimDir = Vector3.zero;
        angleToReticleDeg = 0f;

        if (!LateralAimSwipeActive)
            return false;

        if (!TryGetLateralAimCourse(out origin, out reticlePosition, out Vector3 reticleDir))
            return false;

        hitController controller = LateralAimHitController;
        Transform reticleTransform = reticleInstance != null ? reticleInstance.transform : null;
        if (reticleTransform == null && controller != null)
            reticleTransform = controller.reticle;

        if (reticleTransform == null)
            return false;

        if (swipeInProgress)
        {
            InputDirectionSampler120Hz sampler = GetInputSampler();
            int latestSequence = sampler != null ? sampler.LatestSequence : swipeStartSampleSequence;
            Vector2 liveSwipeDir = Vector2.zero;
            float liveSwipeWeight = 0f;

            if (sampler != null && swipeStartSampleSequence >= 0 && latestSequence >= swipeStartSampleSequence)
            {
                liveSwipeDir = sampler.GetWeightedDirectionBetween(
                    swipeStartSampleSequence,
                    latestSequence,
                    out liveSwipeWeight,
                    swipeStillDeltaPixels
                );
            }

            if (liveSwipeWeight <= 0.001f || liveSwipeDir.sqrMagnitude < 1e-6f)
            {
                Vector2 swipeDelta = sampledMousePos - swipeStart;
                if (swipeDelta.sqrMagnitude > 1e-6f)
                    liveSwipeDir = swipeDelta.normalized;
                else if (lateSwipeDir.sqrMagnitude > 1e-6f)
                    liveSwipeDir = lateSwipeDir.normalized;
            }

            if (liveSwipeDir.sqrMagnitude < 1e-6f)
                return false;

            aimDir = ComputeAimDirectionWeighted(origin, reticleTransform, liveSwipeDir);
        }
        else
        {
            aimDir = finalAimDir;
        }

        aimDir.y = 0f;
        if (aimDir.sqrMagnitude < 1e-6f)
            return false;
        aimDir.Normalize();

        angleToReticleDeg = Vector3.SignedAngle(reticleDir, aimDir, Vector3.up);
        return true;
    }

    private void FixedUpdate()
    {
        UpdateSweptPlayerContactSample();

        if (!processTightHitZoneInFixedUpdate)
        {
            TryFirePendingPhysicsShot();
            return;
        }

        ProcessWaitingTightHitZoneOnPhysicsTick();
        TryFirePendingPhysicsShot();
    }

    private void UpdateSweptPlayerContactSample()
    {
        sweptPlayerContactDetectedThisTick = false;
        sweptPlayerContactThisTick = default;

        MatchServicePointController match = MatchServicePointController.Active;
        if (ball == null || hitControllerInstance == null ||
            (match != null && match.IsPlayerServeTossActive(hitControllerInstance)) ||
            !hitControllerInstance.TryGetAuthoritativeContactZonePose(
                out Vector3 zoneCenter,
                out Quaternion zoneRotation,
                out _))
        {
            hasPlayerPhysicsContactSample = false;
            playerPhysicsContactShotKey = -1;
            return;
        }

        int shotKey = ball.GetComponent<BallController>() != null
            ? (ball.GetComponent<BallController>().GetInstanceID() * 397) ^ ball.GetComponent<BallController>().ShotSequence
            : ball.GetInstanceID();
        Vector3 currentBallPosition = ball.position;
        if (!hasPlayerPhysicsContactSample || shotKey != playerPhysicsContactShotKey)
        {
            hasPlayerPhysicsContactSample = true;
            playerPhysicsContactShotKey = shotKey;
            previousPlayerPhysicsBallPosition = currentBallPosition;
            previousPlayerPhysicsZoneCenter = zoneCenter;
            previousPlayerPhysicsZoneRotation = zoneRotation;
            return;
        }

        float radius = sweptPlayerContactBallRadius > 0f
            ? sweptPlayerContactBallRadius
            : hitControllerInstance.GetBallContactRadius(ball);
        if (useSweptPlayerContactDetection &&
            hitControllerInstance.SweepIntersectsAuthoritativeContactZone(
                previousPlayerPhysicsBallPosition,
                currentBallPosition,
                previousPlayerPhysicsZoneCenter,
                previousPlayerPhysicsZoneRotation,
                radius,
                out Vector3 sweptContactPoint,
                out float sweptFraction))
        {
            sweptPlayerContactDetectedThisTick = true;
            sweptPlayerContactThisTick = HitContactConfirmation.Confirmed(sweptContactPoint, true);
            if (logSweptPlayerContacts &&
                !hitControllerInstance.IsPointInsideAuthoritativeContactZone(currentBallPosition, radius))
            {
                Debug.Log($"[PLAYER SWEPT CONTACT] shot={ball.GetComponent<BallController>()?.ShotSequence ?? -1} " +
                    $"point={sweptContactPoint} fraction={sweptFraction:F3} " +
                    $"segment={previousPlayerPhysicsBallPosition}->{currentBallPosition}.");
            }
        }

        previousPlayerPhysicsBallPosition = currentBallPosition;
        previousPlayerPhysicsZoneCenter = zoneCenter;
        previousPlayerPhysicsZoneRotation = zoneRotation;
    }

    private void ProcessWaitingTightHitZoneOnPhysicsTick()
    {
        if (!waitingForTightHitZone)
            return;

        if (TryGetActiveContact(out HitContactConfirmation confirmation))
        {
            RememberActiveContact(confirmation);
            CompleteSwipeForTightHit(
                confirmation.confirmed
                    ? "tossed ball entered service contact window (FixedUpdate)"
                    : "ball entered tight zone during pending window (FixedUpdate)",
                false);
            return;
        }

        if (Time.time > tightHitZoneDeadline)
            CancelPendingTightHitZone("ball did not enter tight zone before pending window expired");
    }

    private void UpdateZoneFeedback(bool ballInSwingZone, bool ballInTightHitZone)
    {
        if (!enableZoneFeedback)
            return;

        if (!feedbackHasZoneHistory)
        {
            feedbackPreviousSwingZone = ballInSwingZone;
            feedbackPreviousTightZone = ballInTightHitZone;
            feedbackHasZoneHistory = true;
        }

        bool enteredSwingZone = ballInSwingZone && !feedbackPreviousSwingZone;
        bool enteredTightZone = ballInTightHitZone && !feedbackPreviousTightZone;

        if (enteredSwingZone && flashSwingZoneOnEntry)
            swingZoneFlashUntil = Time.time + Mathf.Max(0f, zoneFlashSeconds);

        if (enteredTightZone)
        {
            reticleTightFlashUntil = Time.time + Mathf.Max(0f, reticleTightHitFlashSeconds);

            if (flashTightZoneOnEntry)
                tightZoneFlashUntil = Time.time + Mathf.Max(0f, zoneFlashSeconds);
        }

        feedbackPreviousSwingZone = ballInSwingZone;
        feedbackPreviousTightZone = ballInTightHitZone;

        UpdateReticleFeedbackColor(ballInSwingZone);
        UpdateZoneFlashVisuals();
    }

    private void UpdateReticleFeedbackColor(bool ballInSwingZone)
    {
        EnsureReticleFeedbackRenderers();

        if (reticleFeedbackRenderers == null || reticleFeedbackRenderers.Length == 0)
            return;

        bool useDefaultColor = false;
        Color targetColor;
        if (Time.time < reticleTightFlashUntil)
        {
            targetColor = reticleTightHitColor;
        }
        else if (ballInSwingZone && (!enableHeightIntentReticleColor || !heightIntentOverridesSwingReadyColor))
        {
            targetColor = reticleSwingReadyColor;
        }
        else
        {
            targetColor = GetHeightIntentReticleColor();
            useDefaultColor = !enableHeightIntentReticleColor;
        }

        for (int i = 0; i < reticleFeedbackRenderers.Length; i++)
        {
            Renderer renderer = reticleFeedbackRenderers[i];
            if (renderer == null)
                continue;

            Color color = useDefaultColor
                ? GetReticleDefaultColor(i)
                : targetColor;

            SetRendererColor(renderer, color);
        }
    }

    private Color GetHeightIntentReticleColor()
    {
        if (!enableHeightIntentReticleColor)
            return reticleDefaultColor;

        float intent = Mathf.Clamp01(BaseShotLibrary.HeightIntent);

        if (intent <= 0.5f)
            return Color.Lerp(reticleLowHeightColor, reticleDefaultHeightColor, intent / 0.5f);

        return Color.Lerp(reticleDefaultHeightColor, reticleHighHeightColor, (intent - 0.5f) / 0.5f);
    }

    private void EnsureReticleFeedbackRenderers()
    {
        if (reticleFeedbackDefaultsCaptured)
            return;

        if (reticleFeedbackRenderers == null && autoFindReticleRenderers && reticleInstance != null)
            reticleFeedbackRenderers = reticleInstance.GetComponentsInChildren<Renderer>(true);

        if (reticleFeedbackRenderers == null)
            reticleFeedbackRenderers = new Renderer[0];

        reticleFeedbackDefaultColors = new Color[reticleFeedbackRenderers.Length];
        for (int i = 0; i < reticleFeedbackRenderers.Length; i++)
        {
            Renderer renderer = reticleFeedbackRenderers[i];
            reticleFeedbackDefaultColors[i] = renderer != null
                ? GetRendererColor(renderer, reticleDefaultColor)
                : reticleDefaultColor;
        }

        reticleFeedbackDefaultsCaptured = true;
    }

    private Color GetReticleDefaultColor(int index)
    {
        if (reticleFeedbackDefaultColors != null &&
            index >= 0 &&
            index < reticleFeedbackDefaultColors.Length)
        {
            return reticleFeedbackDefaultColors[index];
        }

        return reticleDefaultColor;
    }

    private Color GetRendererColor(Renderer renderer, Color fallback)
    {
        if (renderer == null || renderer.material == null)
            return fallback;

        Material material = renderer.material;
        if (material.HasProperty("_BaseColor"))
            return material.GetColor("_BaseColor");

        if (material.HasProperty("_Color"))
            return material.GetColor("_Color");

        return fallback;
    }

    private void SetRendererColor(Renderer renderer, Color color)
    {
        if (renderer == null || renderer.material == null)
            return;

        Material material = renderer.material;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);

        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
    }

    private void UpdateZoneFlashVisuals()
    {
        if (!showRuntimeZoneFlashMeshes)
        {
            // Keep the runtime zone meshes available as an optional debug aid,
            // but never leave one visible when the debug option is disabled.
            if (swingZoneFlashVisual != null)
                swingZoneFlashVisual.SetActive(false);

            if (tightZoneFlashVisual != null)
                tightZoneFlashVisual.SetActive(false);

            return;
        }

        UpdateSwingZoneFlashVisual();
        UpdateTightZoneFlashVisual();
    }

    private Material GetZoneFlashMaterial()
    {
        if (zoneFlashMaterial != null)
            return zoneFlashMaterial;

        Shader shader =
            Shader.Find("HDRP/Unlit") ??
            Shader.Find("Universal Render Pipeline/Unlit") ??
            Shader.Find("Sprites/Default") ??
            Shader.Find("Standard") ??
            Shader.Find("Hidden/Internal-Colored");

        if (shader == null)
            return null;

        zoneFlashMaterial = new Material(shader);
        zoneFlashMaterial.name = "Runtime Zone Flash Material";
        SetMaterialColor(zoneFlashMaterial, zoneFlashColor);
        return zoneFlashMaterial;
    }

    private void SetMaterialColor(Material material, Color color)
    {
        if (material == null)
            return;

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);

        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
    }

    private GameObject EnsureZoneFlashVisual(ref GameObject visual, string name)
    {
        if (visual != null)
            return visual;

        visual = new GameObject(name);
        visual.name = name;
        CreateZoneFlashRing(visual.transform, name + " XY", ZoneFlashRingPlane.XY);
        CreateZoneFlashRing(visual.transform, name + " XZ", ZoneFlashRingPlane.XZ);
        CreateZoneFlashRing(visual.transform, name + " YZ", ZoneFlashRingPlane.YZ);

        visual.SetActive(false);
        return visual;
    }

    private enum ZoneFlashRingPlane
    {
        XY,
        XZ,
        YZ
    }

    private void CreateZoneFlashRing(Transform parent, string name, ZoneFlashRingPlane plane)
    {
        GameObject ringObject = new GameObject(name);
        ringObject.transform.SetParent(parent, false);

        LineRenderer line = ringObject.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = 96;
        line.widthMultiplier = Mathf.Max(0.001f, zoneFlashLineWidth);
        line.numCapVertices = 4;
        line.numCornerVertices = 4;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;

        Material material = GetZoneFlashMaterial();
        if (material != null)
            line.material = material;

        for (int i = 0; i < line.positionCount; i++)
        {
            float t = i / (float)line.positionCount;
            float angle = t * Mathf.PI * 2f;
            float a = Mathf.Cos(angle) * 0.5f;
            float b = Mathf.Sin(angle) * 0.5f;

            Vector3 point = plane switch
            {
                ZoneFlashRingPlane.XY => new Vector3(a, b, 0f),
                ZoneFlashRingPlane.XZ => new Vector3(a, 0f, b),
                _ => new Vector3(0f, a, b)
            };

            line.SetPosition(i, point);
        }
    }

    private void UpdateSwingZoneFlashVisual()
    {
        bool visible = Time.time < swingZoneFlashUntil;
        GameObject visual = EnsureZoneFlashVisual(ref swingZoneFlashVisual, "Swing Zone Flash");
        visual.SetActive(visible);

        if (!visible)
            return;

        Collider zone = hitControllerInstance != null ? hitControllerInstance.hitZone : null;
        if (zone == null && hitController != null)
            zone = hitController.hitZone;

        if (zone != null)
        {
            Bounds bounds = zone.bounds;
            visual.transform.position = bounds.center;
            visual.transform.rotation = zone.transform.rotation;
            visual.transform.localScale = bounds.size;
        }
        else if (hitControllerInstance != null)
        {
            visual.transform.position = hitControllerInstance.transform.position;
            float diameter = Mathf.Max(0.1f, hitControllerInstance.hitRadius * 2f);
            visual.transform.localScale = Vector3.one * diameter;
        }

        SetZoneVisualColor(visual);
    }

    private void UpdateTightZoneFlashVisual()
    {
        bool visible = Time.time < tightZoneFlashUntil;
        GameObject visual = EnsureZoneFlashVisual(ref tightZoneFlashVisual, "Tight Hit Zone Flash");
        visual.SetActive(visible);

        if (!visible)
            return;

        Transform center = GetTightHitZoneCenter();
        visual.transform.position = GetTightHitZoneWorldCenter();
        visual.transform.rotation = center != null ? center.rotation : Quaternion.identity;
        visual.transform.localScale = GetSafeTightHitZoneRadii() * 2f;
        SetZoneVisualColor(visual);
    }

    private void SetZoneVisualColor(GameObject visual)
    {
        if (visual == null)
            return;

        LineRenderer[] lines = visual.GetComponentsInChildren<LineRenderer>(true);
        for (int i = 0; i < lines.Length; i++)
        {
            LineRenderer line = lines[i];
            if (line == null)
                continue;

            line.widthMultiplier = Mathf.Max(0.001f, zoneFlashLineWidth);
            SetMaterialColor(line.material, zoneFlashColor);
        }
    }

    private void QueueShotForPhysicsTick(
        SwipeData swipeData,
        BaseShotType baseType,
        ShotModifier modifier,
        InputDirectionSampler120Hz sampler,
        int traceEndSequence,
        Vector3 traceTargetDir,
        float manualTraceSpeed,
        float blendedFallbackTraceSpeed,
        float targetTraceSpeed,
        float backswingCapTraceSpeed,
        float maxScaleTraceSpeed,
        float rawSwipeSpeedCentimetresPerSecond,
        float rawSwipeDurationSeconds,
        float rawSwipeDistanceCentimetres)
    {
        pendingSwipeData = swipeData;
        pendingBaseType = baseType;
        pendingModifier = modifier;
        pendingTraceSampler = sampler;
        pendingTraceEndSequence = traceEndSequence;
        pendingTraceTargetDir = traceTargetDir;
        pendingTraceManualSpeed = manualTraceSpeed;
        pendingTraceBlendedFallbackSpeed = blendedFallbackTraceSpeed;
        pendingTraceTargetSpeed = targetTraceSpeed;
        pendingTraceBackswingCapSpeed = backswingCapTraceSpeed;
        pendingTraceMaxScaleSpeed = maxScaleTraceSpeed;
        pendingTraceRawSwipeSpeedCentimetresPerSecond = rawSwipeSpeedCentimetresPerSecond;
        pendingTraceRawSwipeDurationSeconds = rawSwipeDurationSeconds;
        pendingTraceRawSwipeDistanceCentimetres = rawSwipeDistanceCentimetres;
        pendingShotQueuedTime = Time.time;
        pendingShotSwipeCompletionTime = swipeCompletionTime;
        MatchServicePointController activeMatch = MatchServicePointController.Active;
        pendingAIServeReturnDeferralDeadline = deferPlayerReturnUntilAIServeBounce &&
            IsAIServePreReturnPhase(activeMatch)
            ? Time.time + Mathf.Max(0.05f, aiServeReturnDeferralSeconds)
            : 0f;
        pendingAIServeReturnDeferralLogged = false;
        pendingPhysicsShot = true;

        swipeDetected = false;
        swipeInProgress = false;
        swipeArmed = false;
    }

    private bool IsAIServePreReturnPhase(MatchServicePointController match)
    {
        if (match == null || !match.IsMatchActive || match.serverIndex != 1)
            return false;

        return match.phase == MatchServicePointController.MatchPhase.TossInProgress ||
            match.phase == MatchServicePointController.MatchPhase.ServeFlight;
    }

    private void TryFirePendingPhysicsShot()
    {
        if (!pendingPhysicsShot)
            return;

        MatchServicePointController activeMatch = MatchServicePointController.Active;
        if (pendingAIServeReturnDeferralDeadline > Time.time &&
            IsAIServePreReturnPhase(activeMatch))
        {
            pendingHitContactConfirmation = default;
            pendingPhysicsShot = true;
            if (logAIServeReturnDeferrals && !pendingAIServeReturnDeferralLogged)
            {
                pendingAIServeReturnDeferralLogged = true;
                Debug.Log($"[AI SERVE RETURN DEFERRED] waiting for legal post-bounce return phase; " +
                    $"deadline={pendingAIServeReturnDeferralDeadline:F3} ball={ball?.position}.");
            }
            return;
        }

        HitContactConfirmation currentContact = default;
        bool isPlayerReturningAIServe = activeMatch != null && activeMatch.IsMatchActive &&
            activeMatch.serverIndex == 1 &&
            activeMatch.phase == MatchServicePointController.MatchPhase.Rally;
        if (isPlayerReturningAIServe && !TryGetActiveContact(out currentContact))
        {
            if (pendingAIServeReturnDeferralDeadline > Time.time)
            {
                pendingPhysicsShot = true;
                return;
            }

            if (logAIServeReturnDeferrals)
                Debug.Log($"[AI SERVE RETURN MISSED] completed swipe expired before a post-bounce contact was confirmed; ball={ball?.position}.");
            pendingPhysicsShot = false;
            ResetAfterShotFired();
            return;
        }

        if (isPlayerReturningAIServe)
        {
            pendingHitContactConfirmation = currentContact;
        }

        pendingPhysicsShot = false;

        if (hitControllerInstance == null)
        {
            ResetAfterShotFired();
            return;
        }

        if (debugLogs)
        {
            float queueDelayMs = Mathf.Max(0f, Time.time - pendingShotQueuedTime) * 1000f;
            float swipeEndToFireMs = pendingShotSwipeCompletionTime > 0f
                ? Mathf.Max(0f, Time.time - pendingShotSwipeCompletionTime) * 1000f
                : -1f;
            string swipeEndToFireText = swipeEndToFireMs >= 0f
                ? swipeEndToFireMs.ToString("F1")
                : "n/a";

            Debug.Log(
                $"[SHOT TIMING] physicsFire t={Time.time:F3}, " +
                $"queueDelay={queueDelayMs:F1}ms, " +
                $"swipeEndToFire={swipeEndToFireText}ms"
            );
        }

        HitAttemptResult hitResult = hitControllerInstance.HitBallUsingSwipe(
            pendingSwipeData,
            pendingBaseType,
            pendingModifier,
            pendingHitContactConfirmation
        );

        bool hasShotUiData = hitControllerInstance != null && hitControllerInstance.hasLastShotUiData;
        float manualTraceSpeed = hasShotUiData
            ? hitControllerInstance.lastManualShotSpeed
            : pendingTraceManualSpeed;
        float blendedTraceSpeed = hasShotUiData
            ? hitControllerInstance.lastBlendedShotSpeed
            : pendingTraceBlendedFallbackSpeed;
        float targetTraceSpeed = hasShotUiData
            ? hitControllerInstance.lastTargetShotSpeed
            : pendingTraceTargetSpeed;

        if (hitResult == HitAttemptResult.Launched)
        {
            ShowShotTrace(
                pendingTraceSampler,
                pendingTraceEndSequence,
                pendingTraceTargetDir,
                manualTraceSpeed,
                blendedTraceSpeed,
                targetTraceSpeed,
                pendingTraceBackswingCapSpeed,
                pendingTraceMaxScaleSpeed,
                pendingTraceRawSwipeSpeedCentimetresPerSecond,
                pendingTraceRawSwipeDurationSeconds,
                pendingTraceRawSwipeDistanceCentimetres
            );
        }
        else if (debugLogs)
        {
            Debug.Log($"[SWIPE HIT RESULT] {hitResult}; shot trace suppressed because no launch occurred.");
        }

        ResetAfterShotFired();
    }

    private void ResetAfterShotFired()
    {
        swipeDetected = false;
        accumulatedDistance = 0f;
        BaseShotLibrary.ResetHeightIntent();
        reticleTightFlashUntil = 0f;
        if (reticleInstance != null)
            reticleInstance.SetSwingZoneLockActive(false);
        if (heightUI != null)
        {
            float defaultHeightAngle = BaseShotLibrary.GetHeightAngleDeg(BaseShotType.Flat, BaseShotLibrary.DefaultHeightIntent);
            heightUI.UpdateTargetHeight(defaultHeightAngle);
            heightUI.UpdateActualHeight(defaultHeightAngle);
        }
        UpdateReticleFeedbackColor(false);
        UnlockReticle();
        ResetTightHitZoneState();
        isLMB = false;
        isRMB = false;
        frozenBackswing = 0f;
        forwardControlHoldTime = 0f;
        forwardControlElapsedTime = 0f;
        finalForwardControlHoldScale = 0f;
        forwardSpinHoldTime = 0f;
        finalForwardSpinIntent = 0f;
        pendingAIServeReturnDeferralDeadline = 0f;
        pendingAIServeReturnDeferralLogged = false;
        backswingScale = 0f;
        backswingActive = false;
    }

    public void ResetServeBackswingCharge()
    {
        pendingPhysicsShot = false;
        waitingForTightHitZone = false;
        tightHitZoneDeadline = 0f;
        swipeDetected = false;
        swipeInProgress = false;
        swipeArmed = false;
        swipeConsumedUntilRelease = false;
        accumulatedDistance = 0f;
        isLMB = false;
        isRMB = false;
        backswingActive = false;
        backswingStartTime = -1f;
        backswingScale = 0f;
        frozenBackswing = 0f;
        forwardControlHoldTime = 0f;
        forwardControlElapsedTime = 0f;
        finalForwardControlHoldScale = 0f;
        forwardSpinHoldTime = 0f;
        finalForwardSpinIntent = 0f;
        lastSwipeButtonPressTime = -999f;
        ResetTightHitZoneState();
        if (reticleInstance != null)
            reticleInstance.SetSwingZoneLockActive(false);
        UnlockReticle();
    }

    // ----------------------------------------------------------------------
    // UPDATE â€” MAIN SWIPE INPUT LOOP
    // ----------------------------------------------------------------------
    // This loop handles the entire lifecycle of a swipe gesture:
    //
    //   1. Spawn ball with G
    //   2. Arm swipe (LMB or RMB) when ball is hittable
    //   3. Validate whether a swipe is still allowed
    //   4. Movementâ€‘based swipe detection (core gesture tracking)
    //   5. Swipe metrics: distance, duration, average speed
    //   6. Shot quality (wobble + stability)
    //   7. Lateral aim calculation
    //   8. Power model (gesturePower â†’ finalSwipeSpeed)
    //   9. Fire the shot
    //
    // No height control, no scroll input, no slowdown logic,
    // and no swingâ€‘path UI tracking remain in this script.
    // ----------------------------------------------------------------------

    void Update()
    {
        InputDirectionSampler120Hz sampler = GetInputSampler();
        sampledMousePos = sampler.CurrentPosition;
        dpi = sampler.Dpi;

        // ------------------------------------------------------------
        // INITIAL SETUP & RESPAWN
        //
        // This section prepares the input system for the first frame and
        // handles manual ball respawning. The mouse position must be
        // initialised before any swipe logic runs, otherwise the first
        // delta calculation would produce a huge artificial movement spike.
        // 
        // The respawn key (G) is intentionally lightweight: it destroys the
        // current ball and spawns a fresh one at the designated spawn point.
        // This keeps the hitting loop stable and prevents stale references.
        // ------------------------------------------------------------
        // ------------------------------------------------------------
        // INITIAL SETUP & RESPAWN
        // ------------------------------------------------------------
        if (!mouseInitialized)
        {
            lastMousePos = sampledMousePos;
            lastConsumedSampleSequence = sampler.LatestSequence;
            mouseInitialized = true;
        }

        // Scroll is the service toss control during a service phase.  Do not
        // also feed that same scroll input into the rally height-angle control.
        MatchServicePointController matchService = MatchServicePointController.Active;
        if (matchService == null || !matchService.IsServicePhase)
            ShotTypeResolver.UpdateBaseHeightModifier();

        if (Input.GetKeyDown(KeyCode.G))
        {
            if (debugLogs) Debug.Log("[swipeMouseBall] G pressed -> Respawning ball");
            SpawnNewBall();
        }

        if (Input.GetKeyDown(cannonSpawnKey))
        {
            if (debugLogs) Debug.Log($"[swipeMouseBall] {cannonSpawnKey} pressed -> Spawning cannon ball");
            SpawnCannonBall();
        }

        if (cannonAutoFireToggleKey != KeyCode.None && Input.GetKeyDown(cannonAutoFireToggleKey))
        {
            cannonAutoFire = !cannonAutoFire;
            nextCannonAutoFireTime = cannonAutoFire && cannonAutoFireImmediately
                ? Time.time
                : Time.time + Mathf.Max(0.1f, cannonAutoFireInterval);

            if (debugLogs)
                Debug.Log($"[swipeMouseBall] Cannon auto fire {(cannonAutoFire ? "ON" : "OFF")}");
        }

        if (cannonAutoFire)
        {
            if (nextCannonAutoFireTime < 0f)
            {
                nextCannonAutoFireTime = cannonAutoFireImmediately
                    ? Time.time
                    : Time.time + Mathf.Max(0.1f, cannonAutoFireInterval);
            }

            if (Time.time >= nextCannonAutoFireTime)
            {
                if (ShouldDelayCannonAutoFireForRally())
                {
                    nextCannonAutoFireTime = Time.time + Mathf.Max(0.05f, cannonAutoFireRetryDelay);
                }
                else
                {
                    SpawnCannonBall();
                    nextCannonAutoFireTime = Time.time + Mathf.Max(0.1f, cannonAutoFireInterval);
                }
            }
        }
        else
        {
            nextCannonAutoFireTime = -1f;
        }
        // ------------------------------------------------------------
        // BASIC INPUT FLAGS
        //
        // These flags represent the raw state of the mouse buttons and
        // whether the ball is currently hittable. They are intentionally
        // simple: the entire stroke system is built on top of these three
        // truths:
        //
        //   â€¢ LMB/RMB pressed?      â†’ player is interacting with the racket
        //   â€¢ Ball in hitting zone? â†’ player is allowed to begin a stroke
        //   â€¢ No MMB aiming mode    â†’ reticle moves freely at all times
        //
        // The new design removes MMB aiming entirely. The reticle now
        // follows the mouse by default, just like a real player adjusting
        // their aim with body positioning. Only when a stroke is armed
        // (backswing or forward swipe) does the reticle become locked.
        // ------------------------------------------------------------
        // ------------------------------------------------------------
        // BASIC INPUT FLAGS
        // ------------------------------------------------------------
        bool legacyLmb = Input.GetMouseButton(0);
        bool legacyRmb = Input.GetMouseButton(1);
        bool legacyLmbDown = Input.GetMouseButtonDown(0);
        bool legacyRmbDown = Input.GetMouseButtonDown(1);
        bool legacyLmbUp = Input.GetMouseButtonUp(0);
        bool legacyRmbUp = Input.GetMouseButtonUp(1);
        float armPressBufferSeconds = Mathf.Max(0f, swipeArmPressBufferSeconds);
        bool samplerLeftRecent = sampler != null && sampler.WasLeftButtonPressedRecently(armPressBufferSeconds);
        bool samplerRightRecent = sampler != null && sampler.WasRightButtonPressedRecently(armPressBufferSeconds);
        bool lmb = legacyLmb || (sampler != null && sampler.LeftButtonHeld);
        bool rmb = legacyRmb || (sampler != null && sampler.RightButtonHeld);
        bool swipeButtonsDown = lmb || rmb;
        if (legacyLmbDown || legacyRmbDown)
        {
            lastSwipeButtonPressTime = Time.unscaledTime;
            bufferedSwipePressWasLMB = legacyLmbDown || lmb;
            bufferedSwipePressWasRMB = legacyRmbDown || rmb;
        }

        bool bufferedSwipePress =
            Time.unscaledTime - lastSwipeButtonPressTime <= armPressBufferSeconds ||
            samplerLeftRecent ||
            samplerRightRecent;
        bool serveBackswingAllowed = matchService == null ||
            matchService.CanAccumulateServeBackswing(hitControllerInstance);

        if (swipeInProgress)
        {
            float dt = Mathf.Max(0f, Time.deltaTime);
            forwardControlElapsedTime += dt;

            if (lmb || rmb)
                forwardControlHoldTime += dt;

            if (rmb)
                forwardSpinHoldTime += dt;

            finalForwardControlHoldScale = ComputeForwardControlHoldScale();
            finalForwardSpinIntent = ComputeForwardSpinIntent();
        }
        

        bool playerServeToss = matchService != null &&
            matchService.IsPlayerServeTossActive(hitControllerInstance);
        bool normalSwingZone = hitControllerInstance != null && hitControllerInstance.ballIsInHittingZone;
        bool ballInTightHitZone = TryGetActiveContact(out HitContactConfirmation activeContactConfirmation);
        bool inZone = playerServeToss || normalSwingZone || ballInTightHitZone;
        if (inZone && !wasBallInSwingZone)
            swingZoneEnterTime = Time.time;
        else if (!inZone && wasBallInSwingZone)
            swingZoneEnterTime = -1f;
        wasBallInSwingZone = inZone;

        if (reticleInstance != null)
            reticleInstance.SetSwingZoneLockActive(inZone);

        if (ballInTightHitZone && activeContactConfirmation.confirmed &&
            (swipeArmed || swipeInProgress || waitingForTightHitZone))
        {
            RememberActiveContact(activeContactConfirmation);
        }
        if (!hasTightHitZoneHistory)
        {
            previousBallInTightHitZone = ballInTightHitZone;
            hasTightHitZoneHistory = true;
        }

        bool ballEnteredTightHitZone = ballInTightHitZone && !previousBallInTightHitZone;
        bool ballExitedTightHitZone = !ballInTightHitZone && previousBallInTightHitZone;
        previousBallInTightHitZone = ballInTightHitZone;

        UpdateZoneFeedback(inZone, ballInTightHitZone);

        // ------------------------------------------------------------
        // 1. BACKSWING ARMING (ball in zone + LMB/RMB pressed)
        //
        // A tennis stroke begins with a backswing. The moment the player
        // presses LMB/RMB while the ball is in the hitting zone, the system
        // enters "backswing armed" state. This represents the racket being
        // taken back behind the body.
        //
        // Key behaviours:
        //   â€¢ The reticle locks immediately, freezing the player's aim.
        //   â€¢ The backswing timer starts (holdStartTime).
        //   â€¢ The system is now waiting for the forward swipe.
        //
        // This mirrors real tennis: once the racket goes back, the player
        // commits to the shot direction and cannot adjust aim until after
        // contact.
        // ------------------------------------------------------------
        // ------------------------------------------------------------
        // 1. BACKSWING ARMING (ball in zone + LMB/RMB pressed)
        // ------------------------------------------------------------
        // ------------------------------------------------------------
        // 1. BACKSWING ACCUMULATION (hold LMB/RMB to build power)
        // ------------------------------------------------------------

        // ------------------------------------------------------------
        // BACKSWING ACCUMULATION (only while LMB/RMB is held)
        // ------------------------------------------------------------
        // ------------------------------------------------------------
        // BACKSWING ACCUMULATION (only while LMB or RMB is held)
        // ------------------------------------------------------------
        // ------------------------------------------------------------
        // BACKSWING ACCUMULATION
        // ------------------------------------------------------------
        if (swipeInProgress)
            goto SkipBackswing;

        if (!serveBackswingAllowed)
        {
            ResetServeBackswingCharge();
            goto SkipBackswing;
        }

        if (!lmb && !rmb)
        {
            backswingActive = false;
            backswingStartTime = -1f;
            backswingScale = 0f;          // FIX: explicitly zero on release
            goto SkipBackswing;
        }

        if (!backswingActive)
        {
            backswingActive = true;
            backswingStartTime = Time.time;
            frozenBackswing = 0f;
            forwardControlHoldTime = 0f;
            forwardControlElapsedTime = 0f;
            finalForwardControlHoldScale = 0f;
            if (debugLogs) Debug.Log("[backswing] Started accumulating");
        }

        if (backswingStartTime >= 0f)
        {
            float heldTime = Time.time - backswingStartTime;
            // FIX 1: was hardcoded 0.6f â€” now uses Inspector field
            backswingScale = Mathf.Clamp01(Mathf.InverseLerp(0f, maxHoldTime, heldTime));
        }
        else
        {
            backswingScale = 0f;
        }

        if (debugLogs && !Mathf.Approximately(backswingScale, prevBackswingScale))
        {
            float potentialMax = BaseShotLibrary.GetBackswingCapSpeed(backswingScale);

            Debug.Log($"[backswing] scale={backswingScale:F2}  " +
                      $"potentialMax={potentialMax:F2} m/s");
        }

    SkipBackswing:;



        // ------------------------------------------------------------
        // 2. BACKSWING RELEASE (do NOT reset backswingScale)
        //
        // When the player releases LMB/RMB, the backswing phase ends.
        // Importantly, we do NOT reset backswingScale here. The stored
        // power remains available until the forward swipe begins.
        //
        // This allows:
        //   â€¢ Backswing â†’ pause â†’ forward swipe
        //   â€¢ The pause can be 0.1s or 2 seconds
        //   â€¢ The stroke remains armed the entire time
        //
        // This is authentic tennis behaviour: players often take the racket
        // back early, wait for the ball to drop into the ideal contact zone,
        // and then accelerate forward.
        // ------------------------------------------------------------
        // ------------------------------------------------------------
        // 2. BACKSWING RELEASE (stop accumulating, but keep stored power)
        // ------------------------------------------------------------


        // Do NOT reset backswingScale here.
        // Do NOT reset frozenBackswing here.
        // Power stays stored until the forward swipe begins.



        // ------------------------------------------------------------
        // 2.Debug. BACKSWING DEBUG (backswingScale already computed in Section 1)
        // ------------------------------------------------------------
               

        // Change-only debug
        if (debugLogs)
        {
            bool changed =
                lmb != prevLMB ||
                rmb != prevRMB ||
                inZone != prevInZone ||
                !Mathf.Approximately(backswingScale, prevBackswingScale);

            if (changed)
            {
                Debug.Log($"[swipe INPUT] LMB={lmb} RMB={rmb} inZone={inZone} " +
                          $"backswingScale={backswingScale:F2}");

                prevLMB = lmb;
                prevRMB = rmb;
                prevInZone = inZone;
                prevBackswingScale = backswingScale;
            }
        }


        // ------------------------------------------------------------
        // 4. ARM THE FORWARD SWIPE
        //
        // Once the player has performed a backswing, the system enters the
        // "swipe armed" state. This means the next significant mouse movement
        // will be interpreted as the forward swing.
        //
        // The forward swipe:
        //   â€¢ Must begin within a short time window (maxSwipeDelay)
        //   â€¢ Must occur while the ball is still hittable
        //   â€¢ Uses the frozen backswingScale to cap its maximum speed
        //
        // This separation of backswing â†’ forward swing mirrors real tennis,
        // where the take-back and the acceleration are distinct phases.
        // ------------------------------------------------------------
        bool aiming = serveBackswingAllowed && swipeButtonsDown;
        bool canArmSwipe = serveBackswingAllowed && (swipeButtonsDown || bufferedSwipePress);

        if (aiming && swipeConsumedUntilRelease && !waitingForTightHitZone)
        {
            UnlockReticle();
            return;
        }

        if (canArmSwipe && !swipeArmed)
        {
            if (inZone)
            {
                // existing block where you set swipeArmed = true
                swipeArmed = true;
                armTime = Time.time;
                SetGameplayCursorWrapping(true);

                // Capture which button was held when swipe is armed : left or right mouse button
                isLMB = lmb || (!swipeButtonsDown && (bufferedSwipePressWasLMB || samplerLeftRecent));
                isRMB = rmb || (!swipeButtonsDown && (bufferedSwipePressWasRMB || samplerRightRecent));

                // capture baseline for forward detection
                swipeArmingPos = sampledMousePos;  // shared 120Hz sampler position

                swipeInProgress = false;
                swipeDetected = false;
                waitingForTightHitZone = false;
                tightHitZoneDeadline = 0f;
                swipeCompletionTime = 0f;
                swipeMotionStartTime = 0f;
                swipeMotionStartSampleSequence = -1;
                swipeCompletionSampleSequence = -1;
                pendingHitContactConfirmation = default;
                playerServeContactLoggedForPreparedSwipe = false;
                previousBallInTightHitZone = ballInTightHitZone;
                hasTightHitZoneHistory = true;
                accumulatedDistance = 0f;
                ignoreFirstFrame = true;

                if (debugLogs)
                    Debug.Log($"[swipe] ARMED at {armTime:F3} bufferedPress={bufferedSwipePress && !swipeButtonsDown}");
            }
        }

        if (!lmb && !rmb)
            swipeConsumedUntilRelease = false;

        // --- IGNORE FIRST FRAME AFTER ARMING ---
        if (ignoreFirstFrame)
        {
            lastConsumedSampleSequence = sampler.LatestSequence;  // clear any movement from click frame
            ignoreFirstFrame = false;
            return;
        }

        // ------------------------------------------------------------
        // 5. SWIPE VALIDATION
        //
        // This ensures that a swipe only counts if it begins within the
        // allowed timing window after arming. If the player waits too long,
        // the stroke is cancelled and the reticle unlocks.
        //
        // This prevents accidental hits and ensures the player must commit
        // to the stroke rhythmically, matching the timing of the incoming
        // ball â€” a core part of tennis feel.
        // ------------------------------------------------------------
        if (!swipeArmed && !waitingForTightHitZone)
        {
            swipeInProgress = false;
            swipeDetected = false;
            UnlockReticle();
            return;
        }

        bool useRallySwipeArmTimeout = !playerServeToss || !keepPlayerServeSwipeArmedForToss;
        if (useRallySwipeArmTimeout && !swipeInProgress && !waitingForTightHitZone && Time.time - armTime > maxSwipeDelay)
        {
            if (debugLogs) Debug.Log("[swipe] Swipe window EXPIRED");
            swipeArmed = false;
            swipeInProgress = false;
            swipeDetected = false;
            UnlockReticle();
            return;
        }

        // ------------------------------------------------------------
        // 6. MOVEMENT-BASED SWIPE DETECTION
        //
        // This is the heart of the forward swing. The system watches for a
        // burst of mouse movement to signal the start of the forward swipe.
        // Once detected:
        //
        //   â€¢ The backswing is frozen permanently.
        //   â€¢ The reticle remains locked.
        //   â€¢ Swipe metrics begin accumulating.
        //
        // The swipe ends when the mouse slows down for long enough. This
        // produces a natural, expressive gesture that feels like a real
        // racket acceleration.
        // ------------------------------------------------------------
        // ------------------------------------------------------------
        // MOVEMENT MEASUREMENT (cm per second, DPI independent)
        // ------------------------------------------------------------

        bool completedFromPendingTightHit = false;

        if (waitingForTightHitZone)
        {
            if (ballInTightHitZone)
            {
                RememberActiveContact(activeContactConfirmation);
                CompleteSwipeForTightHit("ball entered tight zone during pending window", false);
                completedFromPendingTightHit = true;
            }
            else if (Time.time > tightHitZoneDeadline)
            {
                CancelPendingTightHitZone("ball did not enter tight zone before pending window expired");
                return;
            }
            else
            {
                return;
            }
        }

        Vector2 mousePos = sampledMousePos;
        int movementWindowStartSequence = lastConsumedSampleSequence;
        float cmThisFrame = completedFromPendingTightHit
            ? 0f
            : sampler.ConsumeDistanceCentimetres(ref lastConsumedSampleSequence);

        InputDirectionSampler120Hz.SampleStats movementWindowStats = sampler.GetSampleStatsBetween(
            movementWindowStartSequence,
            lastConsumedSampleSequence,
            swipeStillDeltaPixels
        );
        float movementWindowDuration = movementWindowStats.durationSeconds > 1e-6f
            ? movementWindowStats.durationSeconds
            : Time.deltaTime;
        float cmPerSecond = cmThisFrame / Mathf.Max(movementWindowDuration, 1e-6f);

        if (!swipeArmed && !lmb && !rmb && !completedFromPendingTightHit)
        {
            swipeInProgress = false;
            UnlockReticle();
            return;
        }

        if (!playerServeToss && !completedFromPendingTightHit &&
            cmPerSecond <= swipeMovementSpeedThresholdCmPerSecond &&
            TryCompleteSwipeOnTightHitZoneEdge(ballEnteredTightHitZone, ballExitedTightHitZone))
        {
            completedFromPendingTightHit = true;
        }

        if (!completedFromPendingTightHit && cmPerSecond > swipeMovementSpeedThresholdCmPerSecond)
        {
            reticleInstance.reticleLocked = true;

            if (swipeMotionStartSampleSequence < 0)
            {
                swipeMotionStartSampleSequence = movementWindowStartSequence;
                swipeMotionStartTime = Time.time;
            }

            if (!swipeInProgress)
            {
                // ------------------------------------------------------------
                // DISTANCE GATE
                // Ignore movement until the mouse has physically travelled
                // minSwipeStartDistanceCm from the arming position.
                // Prevents accidental swipe triggers from hand tremor or
                // tiny positional drift when pressing the mouse button.
                // ------------------------------------------------------------
                
                Vector2 distFromArm = mousePos - swipeArmingPos;
                float cmFromArm = (distFromArm.magnitude / dpi) * 2.54f;
                if (debugLogs)
                {
                    Debug.Log(
                        $"[SWIPE SUMMARY]  " +
                        $"mousePos=({mousePos.x:F1},{mousePos.y:F1}),  " +
                        $"swipeArmingPos=({swipeArmingPos.x:F1},{swipeArmingPos.y:F1}),  " +
                        $"distFromArmPx={distFromArm.magnitude:F2} px,  " +
                        $"cmFromArm={cmFromArm:F3} cm,  " +
                        $"threshold={minSwipeStartDistanceCm:F2} cm"
                    );
                }

                if (cmFromArm > minSwipeStartDistanceCm)
                {
                   

                    swipeStart = swipeArmingPos;
                    swipeStartTime = swipeMotionStartTime > 0f ? swipeMotionStartTime : Time.time;
                    swipeInProgress = true;
                    swipeStartSampleSequence = swipeMotionStartSampleSequence >= 0
                        ? swipeMotionStartSampleSequence
                        : movementWindowStartSequence;
                    weightedSwipeDir = Vector2.zero;
                    weightedSwipeDirTotal = 0f;
                    swipeCompletionTime = 0f;
                    swipeCompletionSampleSequence = -1;
                    forwardControlHoldTime = 0f;
                    forwardControlElapsedTime = 0f;
                    finalForwardControlHoldScale = 0f;
                    frozenBackswing = backswingScale;
                    if (debugLogs)
                        Debug.Log($"[swipe START] cmFromArm={cmFromArm:F2}  frozenBackswing={frozenBackswing:F2}");
                    if (debugLogs)
                    {
                        MatchServicePointController serveMatchService = MatchServicePointController.Active;
                        float frozenMax = serveMatchService != null && serveMatchService.IsCurrentServerController(hitControllerInstance)
                            ? serveMatchService.GetPlayerServeBackswingCapSpeed(frozenBackswing)
                            : BaseShotLibrary.GetBackswingCapSpeed(frozenBackswing);

                        Debug.Log(
                        $"[freeze] backswing frozen at scale={frozenBackswing:F2}, " +
                        $"maxAllowedSpeed={frozenMax:F2} m/s"
                          );

                    }

                    if (debugLogs && !Mathf.Approximately(prevSwipeStartTime, swipeStartTime))
                    {
                        Debug.Log($"[swipe START] swipeStartTime={swipeStartTime:F3} frozenBackswing={frozenBackswing:F2}");

                        if (playerServeToss && logPlayerServeContact)
                            Debug.Log($"[PLAYER SERVE SWIPE START] t={swipeStartTime:F3} backswing={frozenBackswing:F2}.");

                    }

                    earlySwipeDir = Vector2.zero;
                    lateSwipeDir = Vector2.zero;
                }
                else
                {
                    // Too close to arm point â€” ignore, wait for more movement
                    if (debugLogs)
                        Debug.Log($"[swipe GATED] cmFromArm={cmFromArm:F2} < min={minSwipeStartDistanceCm:F2}");
                }
            }

            swipeEnd = mousePos;
            // accumulated relevant cm calculation and time
            accumulatedDistance += cmThisFrame;   // true distance in cm


            Vector2 frameDir = (mousePos - swipeStart).normalized;
            if (Time.time - swipeStartTime < 0.05f)
                earlySwipeDir = frameDir;
            lateSwipeDir = frameDir;

            if (!playerServeToss && !completedFromPendingTightHit &&
                TryCompleteSwipeOnTightHitZoneEdge(ballEnteredTightHitZone, ballExitedTightHitZone))
            {
                completedFromPendingTightHit = true;
            }
        }
        else if (!completedFromPendingTightHit)
        {
            if (!swipeInProgress)
            {
                swipeMotionStartTime = 0f;
                swipeMotionStartSampleSequence = -1;
                accumulatedDistance = 0f;
            }

            if (swipeInProgress &&
                sampler.TryGetStillCompletionSince(
                    swipeStartSampleSequence,
                    swipeMovementSpeedThresholdCmPerSecond,
                    swipeStillDeltaPixels,
                    swipeStillDurationSeconds,
                    out InputDirectionSampler120Hz.StillCompletionInfo stillInfo))
            {
                swipeCompletionTime = stillInfo.completionTimestamp;
                swipeCompletionSampleSequence = stillInfo.completionSequence;

                if (ballInTightHitZone)
                {
                    RememberActiveContact(activeContactConfirmation);
                    CompleteSwipeForTightHit("swing finished while ball was already in tight zone", false);
                }
                else
                    BeginPendingTightHitZone();
            }
        }

        bool serveStrokeButtonReleased = legacyLmbUp || legacyRmbUp;
        if (playerServeToss && completePlayerServeSwipeOnButtonRelease &&
            swipeInProgress && !completedFromPendingTightHit && serveStrokeButtonReleased)
        {
            swipeCompletionTime = Time.time;
            swipeCompletionSampleSequence = sampler.LatestSequence;

            if (ballInTightHitZone)
            {
                RememberActiveContact(activeContactConfirmation);
                CompleteSwipeForTightHit("serve shot button released inside service contact window", false);
            }
            else
            {
                BeginPendingTightHitZone();
            }
        }

        // ------------------------------------------------------------
        // 7. SWIPE METRICS
        //
        // The forward swipe is analysed for duration, distance, and average
        // speed. These metrics classify the swipe as short, medium, or long,
        // and feed into shot quality and shot type.
        //
        // This preserves the expressive feel of your original swipe system:
        // players can flick quickly for a punchy shot or sweep longer for a
        // heavier, more controlled stroke.
        // ------------------------------------------------------------
        if (!swipeDetected)
            return;

        int metricsEndSequence = swipeCompletionSampleSequence >= swipeStartSampleSequence
            ? swipeCompletionSampleSequence
            : sampler.LatestSequence;

        InputDirectionSampler120Hz.SampleStats metricSampleStats = sampler.GetSampleStatsBetween(
            swipeStartSampleSequence,
            metricsEndSequence
        );

        // Swipe Duration in Seconds
        float frameDuration = (swipeCompletionTime > swipeStartTime ? swipeCompletionTime : Time.time) - swipeStartTime;
        float sampledDuration = metricSampleStats.durationSeconds;

        if (sampledDuration <= 0f && metricSampleStats.totalSamples > 1)
            sampledDuration = (metricSampleStats.totalSamples - 1) / Mathf.Max(1f, sampler.sampleRateHz);

        float duration = Mathf.Max(frameDuration, sampledDuration);
        if (duration <= 0f) duration = 0.001f;

        float contactTime = swipeCompletionTime > swipeStartTime ? swipeCompletionTime : Time.time;
        float fullForwardDuration = Mathf.Max(0.001f, fullForwardSwingDurationSeconds);
        float availableSwingZoneTime = swingZoneEnterTime >= 0f
            ? Mathf.Max(0.001f, contactTime - swingZoneEnterTime)
            : fullForwardDuration;
        float forwardProgressDuration = Mathf.Min(fullForwardDuration, availableSwingZoneTime);
        float effectiveForwardSwingProgress = Mathf.Clamp01(duration / Mathf.Max(0.001f, forwardProgressDuration));

        // Swipe Distance in CM
        float distance = accumulatedDistance;

        // Swipe Average Speed in CM per second
        float avgSpeed = distance / duration;

        // ------------------------------------------------------------
        // DEBUG: Swipe Metrics Summary
        // ------------------------------------------------------------
        if (debugLogs)
        {
            Debug.Log(
                $"[SWIPE SUMMARY]  " +
                $"distance={distance:F2} cm,  " +
                $"duration={duration:F3} s,  " +
                $"frameDuration={frameDuration:F3} s,  " +
                $"sampledDuration={sampledDuration:F3} s,  " +
                $"samples={metricSampleStats.totalSamples},  " +
                $"avgSpeed={avgSpeed:F2} cm/s"
            );
        }


        var c = ClassifySwipe(duration, distance, avgSpeed);
        finalNormDist = c.normDist;

        float avgW, distW, durW;

        if (c.isShort)
        {
            avgW = 0.70f;
            distW = 0.15f;
            durW = 0.15f;
        }
        else if (c.isLong)
        {
            avgW = 0.90f;
            distW = 0.05f;
            durW = 0.05f;
        }
        else
        {
            avgW = 0.80f;
            distW = 0.10f;
            durW = 0.10f;
        }

        float gesturePower =
            c.normAvg * avgW +
            c.normDist * distW +
            c.normDur * durW;

        gesturePower = Mathf.Clamp01(gesturePower);



        // ------------------------------------------------------------
        // 8. SHOT QUALITY
        //
        // Shot quality measures how stable the swipe direction was between
        // the early and late parts of the gesture. A wobblier swipe produces
        // more error in the final shot.
        //
        // This adds human-like variability and prevents robotic precision,
        // making the game feel more athletic and less mechanical.
        // ------------------------------------------------------------
        SwipeType swipeType =
        c.isShort ? SwipeType.Short :
        c.isLong ? SwipeType.Long :
        SwipeType.Medium;


        float shotQuality = ComputeShotQuality(earlySwipeDir, lateSwipeDir, swipeType);
        finalShotQuality = shotQuality;

        // ------------------------------------------------------------
        // 9. AIM DIRECTION
        //
        // The aim direction is determined by the reticle position at the
        // moment the swipe begins. Because the reticle locks during the
        // backswing, the player must commit to their aim before swinging.
        //
        // This matches real tennis: once the racket is taken back, the
        // player's body alignment determines the shot direction.
        // ------------------------------------------------------------
        weightedSwipeDir = sampler.GetWeightedDirectionBetween(
            swipeStartSampleSequence,
            metricsEndSequence,
            out weightedSwipeDirTotal
        );

        Vector2 finalSwipeDir = weightedSwipeDirTotal > 0.001f
            ? weightedSwipeDir
            : lateSwipeDir;
        finalAimDir = ComputeAimDirectionWeighted(
            ballInstance.transform.position,
            reticleInstance.transform,
            finalSwipeDir
        );

        if (debugLogs)
        {
            Vector3 reticleDirForLog = reticleInstance.transform.position - ballInstance.transform.position;
            reticleDirForLog.y = 0f;
            if (reticleDirForLog.sqrMagnitude > 1e-6f)
                reticleDirForLog.Normalize();

            float swipeAimAngle = Vector3.SignedAngle(reticleDirForLog, finalAimDir, Vector3.up);
            InputDirectionSampler120Hz.SampleStats sampleStats = metricSampleStats;

            Debug.Log(
                $"[SWIPE AIM] finalSwipeDir={finalSwipeDir}, " +
                $"aimAngleToReticle={swipeAimAngle:F2}, " +
                $"finalAimDir={finalAimDir}, " +
                $"samples={sampleStats.totalSamples}, movingSamples={sampleStats.movingSamples}, " +
                $"zeroMoveSamples={sampleStats.zeroMoveSamples}, " +
                $"eventSamples={sampleStats.inputEventSamples}, fallbackSamples={sampleStats.fallbackSamples}, " +
                $"deltaDrivenSamples={sampleStats.deltaDrivenSamples}, positionDrivenSamples={sampleStats.positionDrivenSamples}, " +
                $"rejectedSpikeSamples={sampleStats.rejectedSpikeSamples}, " +
                $"sampleHz={sampleStats.averageSampleHz:F1}, sampledDuration={sampleStats.durationSeconds:F3}s, " +
                $"sampledDistance={sampleStats.distanceCentimetres:F2}cm, " +
                $"avgMoveDeltaPx={sampleStats.averageMovingDeltaPixels:F2}, maxDeltaPx={sampleStats.maxDeltaPixels:F2}, " +
                $"avgMoveSpeed={sampleStats.averageMovingSpeedCentimetresPerSecond:F1}cm/s, " +
                $"maxMoveSpeed={sampleStats.maxSpeedCentimetresPerSecond:F1}cm/s, " +
                $"directionWeightCm={weightedSwipeDirTotal:F2}, " +
                $"startSeq={swipeStartSampleSequence}, latestSeq={metricsEndSequence}, " +
                $"deadZone={swipeLateralDeadZone:F2}, minForward={minSwipeForwardForAim:F2}, " +
                $"invertSwipeLateralAim={invertSwipeLateralAim}"
            );
        }

        // ------------------------------------------------------------
        // 10. NEW POWER MODEL â€” backswing sets max allowed speed
        //
        // The forward swipe still determines the raw swing speed, but the
        // backswing sets the *maximum possible* speed the player can reach.
        //
        //   rawSwipeSpeed = gesture-based speed (0 â†’ 100f)
        //   maxAllowedSpeed = lerp(60 mph, 80 mph, backswingScale) before shot retention
        //   finalSwipeSpeed = clamp(rawSwipeSpeed, 0, maxAllowedSpeed)
        //
        // This creates a clean, realistic tennis mechanic:
        //
        //   â€¢ No backswing â†’ even a huge swipe cannot exceed 60 mph
        //   â€¢ Full backswing â†’ a fast swipe can reach 80 mph before shot retention
        //   â€¢ Forward swipe still determines the actual speed
        //
        // This is controller-friendly and mirrors real racket physics.
        // ------------------------------------------------------------
        MatchServicePointController powerMatchService = MatchServicePointController.Active;
        float maxAllowedSpeed = powerMatchService != null && powerMatchService.IsCurrentServerController(hitControllerInstance)
            ? powerMatchService.GetPlayerServeBackswingCapSpeed(frozenBackswing)
            : BaseShotLibrary.GetBackswingCapSpeed(frozenBackswing);

        lastSwipeSpeed = Mathf.Lerp(6f, maxAllowedSpeed, gesturePower);
        finalSwipeSpeed = lastSwipeSpeed;
        Vector3 traceTargetDir = GetReticleTargetDirectionForTrace();

        if (debugLogs)
        {
            Debug.Log($"[power] gesturePower={gesturePower:F2}  " +
                      // FIX 4: was backswingScale (always 0 post-freeze) â€” now frozenBackswing
                      $"frozenBackswing={frozenBackswing:F2}  " +
                      $"maxAllowed={maxAllowedSpeed:F2} m/s  " +
                      $"final={finalSwipeSpeed:F2} m/s");
        }
    
        // ------------------------------------------------------------
        // 11. FIRE THE SHOT
        //
        // The final shot is built from:
        //   â€¢ Aim direction (reticle)
        //   â€¢ Shot type (swipe shape)
        //   â€¢ Shot quality (swipe stability)
        //   â€¢ Final speed (backswing cap + swipe speed)
        //
        // The HitController receives a clean, unified SwipeData object and
        // handles all physics, spin, and trajectory shaping.
        //
        // This keeps swipeMouseBall focused purely on input interpretation.
        // ------------------------------------------------------------
        if (debugLogs)
        {
            Debug.Log(
                $"[stroke] aimDir={finalAimDir}, " +
                $"quality={finalShotQuality:F2}, " +
                $"speed={finalSwipeSpeed:F2} m/s, " +
                $"backswingScale={backswingScale:F2}"
            );
        }
        
        Vector2 shotTypeDir = finalSwipeDir;

        float shotTypeDurationGate = Mathf.Max(0f, shotTypeDurationDeadZone);
        float shotTypeY = invertShotTypeDirectionY ? -shotTypeDir.y : shotTypeDir.y;

        const float shotTypeDurationTolerance = 0.005f;
        bool swipeDurationValid = duration + shotTypeDurationTolerance >= shotTypeDurationGate;
        bool swipeForward = swipeDurationValid && shotTypeY >= 0f;
        bool swipeBackward = swipeDurationValid && shotTypeY < 0f;

        if (!swipeDurationValid)
        {
            if (debugLogs)
            {
                Debug.Log(
                    $"[SHOT TYPE] Duration dead zone: swipe too short to resolve shot type. " +
                    $"shotTypeSwipe={finalSwipeDir}, shotTypeDir={shotTypeDir}, " +
                    $"shotTypeY={shotTypeY:F2}, duration={duration:F3}s, " +
                    $"sampledDuration={sampledDuration:F3}s, frameDuration={frameDuration:F3}s, " +
                    $"samples={metricSampleStats.totalSamples}, movingSamples={metricSampleStats.movingSamples}, " +
                    $"minDuration={shotTypeDurationGate:F3}s"
                );
            }

            swipeDetected = false;
            accumulatedDistance = 0f;
            UnlockReticle();
            isLMB = false;
            isRMB = false;
            frozenBackswing = 0f;
            backswingScale = 0f;
            backswingActive = false;
            ResetTightHitZoneState();
            return;
        }

        bool shortSwipe = c.isShort;


        // GET RETICLE FROM HITCONTROLLER
        Transform reticleTransform = hitController.reticle;
        UIWorldReticle reticleUI = reticleTransform.GetComponent<UIWorldReticle>();
        





        // RESOLVE SHOT TYPE
        BaseShotType baseType = ShotTypeResolver.ResolveBaseType(
            isLMB,
            isRMB,
            swipeForward,
            swipeBackward,
            shortSwipe,
            reticleUI
        );



        float shotBackswingScale = frozenBackswing;
        float shotBackswingCapSpeed = maxAllowedSpeed;
        ShotModifier modifier = ShotTypeResolver.ResolveModifier(finalSwipeSpeed);

        SwipeData swipeData = new SwipeData(
        isLMB,
        isRMB,
        finalAimDir,
        finalSwipeSpeed,
        finalShotQuality,
        finalNormDist,
        finalForwardControlHoldScale,
        finalForwardSpinIntent,
        shotBackswingScale,
        effectiveForwardSwingProgress,
        shotBackswingCapSpeed
        );

        Debug.Log($"[SWIPE FLAGS] LMB={isLMB}, RMB={isRMB}, forward={swipeForward}, backward={swipeBackward}, shotTypeSwipe={finalSwipeDir}, shotTypeDir={shotTypeDir}, shotTypeY={shotTypeY:F2}, duration={duration:F3}s, short={shortSwipe}");
        Debug.Log(
            $"[SWIPE SPEED] speed={swipeData.speed}, quality={swipeData.quality}, " +
            $"controlHold={swipeData.holdScale:F2}, controlHeld={forwardControlHoldTime:F3}s, " +
            $"spinIntent={swipeData.spinIntent:F2}, spinHeld={forwardSpinHoldTime:F3}s, " +
            $"controlElapsed={forwardControlElapsedTime:F3}s, backswing={frozenBackswing:F2}, forwardP={swipeData.forwardSwingProgress:F2}, bsCap={swipeData.backswingCapSpeed:F2}"
        );
        Debug.Log($"[RESOLVED] baseType={baseType}, modifier={modifier}");

        QueueShotForPhysicsTick(
            swipeData,
            baseType,
            modifier,
            sampler,
            metricsEndSequence,
            traceTargetDir,
            finalSwipeSpeed,
            finalSwipeSpeed,
            finalSwipeSpeed,
            shotBackswingCapSpeed,
            BaseShotLibrary.RallyMaxSpeedMps,
            avgSpeed,
            duration,
            distance
        );

        return;
    }

    // Temporary placeholder until UI is added
    private void ShowReticleLockedIndicator(bool locked)
    {
        // TODO: Add UI indicator logic here
    }
    // ----------------------------------------------------------------------
    // HELPER â€” Compute Horizontal Aim Direction
    // ----------------------------------------------------------------------
    // Converts swipe lateral movement into a blended worldâ€‘space aim direction.
    // Keeps all aim logic in one place so the swipe interpreter stays clean.
    // ----------------------------------------------------------------------
    private Vector3 ComputeAimDirectionWeighted(
    Vector3 ballPos,
    Transform reticle,
    Vector2 weightedDir,
    float lateralInfluence = 1.0f)
    {
        // Forward direction from ball to reticle (horizontal only)
        Vector3 forward = reticle.position - ballPos;
        forward.y = 0f;
        forward.Normalize();

        if (weightedDir.sqrMagnitude < 1e-6f)
            return forward;

        float signedSwipeX = invertSwipeLateralAim ? -weightedDir.x : weightedDir.x;
        float absSwipeX = Mathf.Abs(signedSwipeX);
        float absSwipeY = Mathf.Abs(weightedDir.y);
        if (absSwipeY < minSwipeForwardForAim)
            return forward;

        float deadZone = Mathf.Clamp01(swipeLateralDeadZone);

        if (absSwipeX <= deadZone)
            signedSwipeX = 0f;
        else
        {
            float remapped = Mathf.InverseLerp(deadZone, 1f, absSwipeX);
            signedSwipeX = Mathf.Sign(signedSwipeX) * remapped;
        }

        float lateralAngle = Mathf.Atan2(signedSwipeX, Mathf.Max(absSwipeY, 0.001f)) * Mathf.Rad2Deg;
        lateralAngle *= lateralInfluence;
        lateralAngle = Mathf.Clamp(
            lateralAngle,
            -Mathf.Abs(maxSwipeLateralAimAngleDeg),
            Mathf.Abs(maxSwipeLateralAimAngleDeg)
        );

        Vector3 blended = (Quaternion.AngleAxis(lateralAngle, Vector3.up) * forward).normalized;

        return blended;
    }

    // ----------------------------------------------------------------------
    // HELPER â€” Compute Shot Quality From Wobble
    // ----------------------------------------------------------------------
    // Converts early/late swipe direction + swipe type into a 0â€“1 quality value.
    // This isolates wobble logic so itâ€™s easy to tune, extend, or override.
    // ----------------------------------------------------------------------
    private float ComputeShotQuality(Vector2 early, Vector2 late, SwipeType type)
    {
        // Safety: ensure early direction is valid
        if (early == Vector2.zero)
            early = late;

        float wobble = Vector2.Angle(early, late);

        float wobbleScale =
            type == SwipeType.Short ? 0.030f :
            type == SwipeType.Long ? 0.050f :
                                      0.015f;

        return Mathf.Clamp01(1f - wobble * wobbleScale);
    }


    // ----------------------------------------------------------------------
    // CLEAN SPAWN SYSTEM WITH CALLBACK
    // ----------------------------------------------------------------------
    // Handles the entire ball lifecycle:
    //
    //   1. Destroy old ball safely
    //   2. Spawn a new ball at the spawn point
    //   3. Ensure the ball has the correct tag
    //   4. Update internal references (ball, ballInstance)
    //   5. Notify hitController so it always hits the correct ball
    //
    // This prevents stale references and ensures consistent gameplay.
    // ----------------------------------------------------------------------
    public void SpawnNewBall()
    {
        SpawnBallAt(spawnPoint, false);
    }

    private void ApplySwipeMouseBallCannonSettings(ballCannon launcher)
    {
        if (launcher == null || !useSwipeMouseBallCannonSettings)
            return;

        ResolveCannonCourtBoundsByName();

        launcher.launchSpeed = cannonLaunchSpeed;
        launcher.launchAngle = cannonLaunchAngle;
        launcher.launchDirection = cannonFallbackDirection;
        launcher.target = cannonTargetPoint;

        launcher.randomizeFeed = cannonRandomizeFeed;
        launcher.aimAtRandomTargetPoint = cannonAimAtRandomTargetPoint;
        launcher.useReticleBoundsGrid = cannonUseCourtBounds;
        launcher.reticleBoundsSource = reticleInstance;
        launcher.autoFindReticleBounds = false;
        launcher.targetMinBound = null;
        launcher.targetMaxBound = null;
        launcher.targetFrontLeftBound = cannonBoundFL;
        launcher.targetFrontRightBound = cannonBoundFR;
        launcher.targetRearRightBound = cannonBoundRR;
        launcher.targetRearLeftBound = cannonBoundRL;
        launcher.mirrorReticleBoundsAcrossNet = cannonMirrorCourtBoundsAcrossNet;
        launcher.useSymmetricLateralGrid = cannonUseSymmetricLateralGrid;
        launcher.useNetPointAsLateralCenter = cannonUseNetAsLateralCenter;
        launcher.fallbackTargetXRange = cannonFallbackTargetXRange;
        launcher.fallbackTargetZRange = cannonFallbackTargetZRange;
        launcher.targetCellPadding = cannonTargetCellPadding;
        launcher.noLobMaxLaunchAngle = cannonNoLobMaxLaunchAngle;
        launcher.avoidSameLateralColumnStreaks = cannonAvoidSameLateralColumnStreaks;
        launcher.maxSameLateralColumnStreak = cannonMaxSameLateralColumnStreak;
        launcher.zoneProbabilities = cannonZoneProbabilities;

        launcher.shotProbabilities = cannonShotProbabilities;
        launcher.shortFlatProfile = cannonShortFlatProfile;
        launcher.deepFlatProfile = cannonDeepFlatProfile;
        launcher.topspinProfile = cannonTopspinProfile;
        launcher.sliceProfile = cannonSliceProfile;
        launcher.lobProfile = cannonLobProfile;
        launcher.dropProfile = cannonDropProfile;
        launcher.shortFlatSpeedRange = cannonShortFlatProfile.speedRange;
        launcher.deepFlatSpeedRange = cannonDeepFlatProfile.speedRange;
        launcher.shortFlatAngleRange = cannonShortFlatProfile.angleRange;
        launcher.deepFlatAngleRange = cannonDeepFlatProfile.angleRange;

        launcher.useTrajectorySolverForFeed = cannonUseTrajectorySolver;
        launcher.solverComponent = cannonSolverComponent;
        launcher.autoFindSolverComponent = cannonAutoFindSolverComponent;
        launcher.netPoint = cannonNetPoint;
        launcher.autoFindNetPoint = cannonAutoFindNetPoint;
        launcher.netObjectName = cannonNetObjectName;
        launcher.autoSetFeedNetHeightFromRenderer = cannonAutoSetNetHeightFromRenderer;
        launcher.feedNetHeight = cannonFeedNetHeight;
        launcher.feedNetMargin = cannonFeedNetMargin;
        launcher.minimumRandomFeedNetMargin = cannonMinimumRandomFeedNetMargin;
        launcher.requireSolvedRandomFeeds = cannonRequireSolvedRandomFeeds;
        launcher.allowExtraSpeedForFlatFeed = cannonAllowExtraSpeedForFlatFeed;
        launcher.extraFlatFeedMaxSpeed = cannonExtraFlatFeedMaxSpeed;
        launcher.logSolverFailures = cannonLogSolverFailures;
    }

    private void ResolveCannonCourtBoundsByName()
    {
        if (!cannonAutoFindCourtBoundsByName)
            return;

        if (cannonBoundFL == null)
            cannonBoundFL = FindSceneTransformByName(cannonBoundFLName);

        if (cannonBoundFR == null)
            cannonBoundFR = FindSceneTransformByName(cannonBoundFRName);

        if (cannonBoundRR == null)
            cannonBoundRR = FindSceneTransformByName(cannonBoundRRName);

        if (cannonBoundRL == null)
            cannonBoundRL = FindSceneTransformByName(cannonBoundRLName);
    }

    private Transform FindSceneTransformByName(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
            return null;

        GameObject found = GameObject.Find(objectName);
        return found != null ? found.transform : null;
    }

    public void SpawnCannonBall()
    {
        Transform source = cannonSpawnPoint != null ? cannonSpawnPoint : spawnPoint;
        GameObject newBall = SpawnBallAt(source, true);

        if (newBall == null || ball == null)
            return;

        bool usingRuntimeFallbackSettings = false;
        ballCannon launcher = cannonSettings;

        if (launcher == null)
        {
            launcher = FindSceneCannonSettings(newBall);
            if (launcher != null)
                cannonSettings = launcher;
        }

        if (launcher == null)
        {
            launcher = newBall.GetComponent<ballCannon>();
            usingRuntimeFallbackSettings = true;
        }

        if (launcher == null)
        {
            launcher = newBall.AddComponent<ballCannon>();
            usingRuntimeFallbackSettings = true;
        }

        if (usingRuntimeFallbackSettings)
        {
            launcher.launchSpeed = cannonLaunchSpeed;
            launcher.launchAngle = cannonLaunchAngle;
            launcher.launchDirection = cannonFallbackDirection;
        }

        ApplySwipeMouseBallCannonSettings(launcher);

        if (launcher.reticleBoundsSource == null)
            launcher.reticleBoundsSource = reticleInstance;

        if (launcher.target == null)
            launcher.target = cannonTargetPoint;

        ballCannon.FeedLaunch feedLaunch = launcher.BuildFeedLaunch(
            newBall.transform.position,
            cannonTargetPoint,
            cannonFallbackDirection
        );

        launcher.Launch(ball, feedLaunch);

        if (debugLogs)
        {
            Debug.Log(
                $"[Cannon] {feedLaunch.shotType} from {(source != null ? source.name : "fallback")} " +
                $"speed={feedLaunch.speed:F1}m/s, angle={feedLaunch.angle:F1}, spin={feedLaunch.spinRpm:F0}rpm, " +
                $"zone={feedLaunch.zoneIndex} cell={feedLaunch.zoneCoord}, lateralOffset={feedLaunch.lateralOffsetFromCenter:F2}m, " +
                $"target={feedLaunch.targetPoint}, dir={feedLaunch.horizontalDirection}"
            );
        }
    }

    private bool ShouldDelayCannonAutoFireForRally()
    {
        if (!cannonAutoFireWaitForDeadBall || ball == null)
            return false;

        GameObject ballObject = ball.gameObject;
        if (!ballObject.activeInHierarchy)
            return false;

        if (ball.isKinematic)
            return false;

        return ball.linearVelocity.sqrMagnitude > cannonAutoFireDeadBallSpeed * cannonAutoFireDeadBallSpeed;
    }

    private ballCannon FindSceneCannonSettings(GameObject spawnedBall)
    {
        ballCannon[] candidates = FindObjectsByType<ballCannon>(FindObjectsSortMode.None);

        for (int i = 0; i < candidates.Length; i++)
        {
            ballCannon candidate = candidates[i];
            if (candidate == null)
                continue;

            if (spawnedBall != null && candidate.gameObject == spawnedBall)
                continue;

            if (spawnedBall != null && candidate.transform.IsChildOf(spawnedBall.transform))
                continue;

            return candidate;
        }

        return null;
    }

    private GameObject SpawnBallAt(Transform selectedSpawnPoint, bool isCannonSpawn)
    {
        ResetTightHitZoneState();
        swipeDetected = false;
        swipeInProgress = false;
        swipeArmed = false;
        UnlockReticle();

        // ------------------------------------------------------------
        // 1. Destroy old ball
        // ------------------------------------------------------------
        if (ball != null && ball.gameObject != null)
        {
            GameObject oldBallObject = ball.gameObject;
            if (CanDestroySpawnedBall(oldBallObject))
            {
                if (debugLogs)
                    Debug.Log($"[swipeMouseBall] Destroying old ball id={ball.GetInstanceID()}");

                Destroy(oldBallObject);
            }
            else if (debugLogs)
            {
                Debug.Log($"[swipeMouseBall] Skipped destroying prefab/asset ball reference id={ball.GetInstanceID()}");
            }

            ball = null;
            ballInstance = null;
        }

        // ------------------------------------------------------------
        // 2. Ensure spawn point exists
        // ------------------------------------------------------------
        if (selectedSpawnPoint == null)
        {
            if (debugLogs)
                Debug.LogWarning("[swipeMouseBall] selected spawn point was null -> using this.transform");

            selectedSpawnPoint = transform;
        }

        // ------------------------------------------------------------
        // 3. Instantiate new ball
        // ------------------------------------------------------------
        GameObject newBall = Instantiate(
            ballPrefab,
            selectedSpawnPoint.position,
            selectedSpawnPoint.rotation
        );

        ball = newBall.GetComponent<Rigidbody>();
        ballInstance = newBall;   // used for height + trajectory logic

        if (debugLogs)
        {
            Debug.Log($"[Spawner] Spawned ball GO={newBall.name} id={newBall.GetInstanceID()} rbId={(ball != null ? ball.GetInstanceID() : -1)}");
            Debug.Log($"[Spawner] Spawned ball TAG = {newBall.tag}");
        }

        // ------------------------------------------------------------
        // 4. Ensure correct tag
        // ------------------------------------------------------------
        if (newBall.tag != "Ball")
            newBall.tag = "Ball";

        // ------------------------------------------------------------
        // 5. Notify hitController
        // ------------------------------------------------------------
        var phc = FindFirstObjectByType<hitController>();
        if (phc != null)
        {
            phc.SetBallReference(ball.transform);

            if (debugLogs)
                Debug.Log($"[Spawner] PHC updated with new ball instance ID = {ball.gameObject.GetInstanceID()}");
        }
        else
        {
            Debug.LogWarning("[Spawner] No hitController found in scene to update ball reference.");
        }

        if (debugLogs)
            Debug.Log(isCannonSpawn ? "[swipeMouseBall] Cannon ball spawned and assigned" : "[swipeMouseBall] New ball spawned and assigned");

        return newBall;
    }

    private static bool CanDestroySpawnedBall(GameObject target)
    {
        if (target == null)
            return false;

#if UNITY_EDITOR
        if (EditorUtility.IsPersistent(target))
            return false;
#endif

        return target.scene.IsValid();
    }

    void OnDrawGizmosSelected()
    {
        if (!drawTightHitZoneGizmo)
            return;

        Transform center = GetTightHitZoneCenter();
        if (center == null)
            return;

        Vector3 radii = GetSafeTightHitZoneRadii();
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;

        Gizmos.color = tightHitZoneGizmoColor;
        Gizmos.matrix = Matrix4x4.TRS(GetTightHitZoneWorldCenter(), center.rotation, radii * 2f);
        Gizmos.DrawWireSphere(Vector3.zero, 0.5f);

        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }
}










