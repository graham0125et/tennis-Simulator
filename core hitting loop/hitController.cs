using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using static swipeMouseBall;

[Serializable]
public struct ShotClearanceProfile
{
    public float baseClearance;
    public float lowContactBonus;
    public float fastIncomingBonus;
    public float controlledHoldBonus;
    public float incomingTopspinBonus;
    public float incomingSliceBonus;
    public float minClearance;
    public float maxClearance;
}

public enum HitAttemptResult
{
    Launched,
    AlreadyHitThisShot,
    MatchBlocked,
    OutsideContactZone,
    MissingReference,
    SolverFailed
}

public struct HitContactConfirmation
{
    public bool confirmed;
    public bool swept;
    public Vector3 contactPosition;

    public static HitContactConfirmation Confirmed(Vector3 position, bool wasSwept)
    {
        return new HitContactConfirmation
        {
            confirmed = true,
            swept = wasSwept,
            contactPosition = position
        };
    }
}

[RequireComponent(typeof(Collider))]
public class hitController : MonoBehaviour
{
    public bool matchHitAllowed = true;
    public static event Action<Rigidbody, Vector3, Vector3> PlayerBallLaunched;
    public static event Action<hitController, Rigidbody, Vector3> RacketContactRegistered;
    public static hitController LastLaunchController { get; private set; }
    public static event Action<hitController, float, Vector3, Vector3> PlayerLateralAimResolved;

    // OVERVIEW
    // High-level orchestrator: consumes swipe input, decides whether a hit
    // should occur, computes shot parameters, and applies a deterministic
    // initial velocity to the ball's Rigidbody. Delegates detailed logic
    // to swipeMouseBall, ShotDirectionCalculator, BallController, and the
    // drag-aware trajectory solver.

    // -------------------------
    // References (assign in Inspector)
    // -------------------------
    [Header("References")]
    public Rigidbody ball;                          // ball Rigidbody instance
    public Transform reticle;                       // landing marker
    

    public swipeMouseBall swipe;                    // input provider
    public Collider hitZone;                        // trigger collider for hit zone
    public ShotDirectionCalculator shotDirectionCalculator; // aim + error model
    public ShotHeightUI shotHeightUI;

    [Header("Authoritative Contact Zone")]
    [Tooltip("Log the final result of every accepted or rejected shared hit request.")]
    public bool logHitAttemptResults = true;
    [SerializeField] private Transform authoritativeContactZoneCenter;
    [SerializeField] private Vector3 authoritativeContactZoneLocalOffset = new Vector3(0f, 1.5f, 0f);
    [SerializeField] private Vector3 authoritativeContactZoneRadii = new Vector3(1.9f, 1.6f, 2.25f);
    [SerializeField] private bool authoritativeContactZoneConfigured;

    [Header("Solver")]
    public ShotSolverComponent solverComponent;     // assign the scene ShotSolverComponent
    public AimingController aimingController;
    public ShotComputationSolver.LiveShotSolveMode liveShotSolveMode = ShotComputationSolver.LiveShotSolveMode.FixedAngleOnly;

    // -------------------------
    // Hit Settings
    // -------------------------
    [Header("Hit Settings")]
    public float hitRadius = 3.5f;                  // fallback distance check (meters)
    public float minSwipeSpeed = 0f;                // ignore tiny swipes (m/s)

    [Header("Aim Blending")]
    [FormerlySerializedAs("blendFactor")]
    [Range(0f, 1f)]
    [Tooltip("0 = reticle only, 1 = sampled swipe/manual aim only.")]
    public float manualAimWeight = 0.62f;
    public float maxManualAimAngleDeg = 45f;

    [Header("Aim After-Effects")]
    public bool applyShotStabilityAfterAim = false;
    public bool applySmallAngleBoosterAfterAim = false;

    public float minUp = 0.15f;                     // minimum upward component for aim
    public float hitCooldown = 0.15f;               // seconds between allowed hits
    public ForceMode debugForceMode = ForceMode.VelocityChange;

    [Header("Global Shot Speed")]
    public float globalPowerScale = 1.4f;           // overall power multiplier
    public Transform contactPoint;                  // contact transform; fallback to ball.transform

    [Header("Shot Type System")]
    public BaseShotType currentBaseShotType = BaseShotType.Flat;
    public ShotModifier currentModifier = ShotModifier.Normal;
    [Range(0f, 1f)] public float currentSpinIntent = 0f;


    // -------------------------
    // Shot Power Limits
    // -------------------------
    [Header("Shot Power Limits")]
    public float minShotPower = 12f;                // minimum shot speed (m/s)
    public float maxShotPower = BaseShotLibrary.RallyMaxSpeedMps; // maximum shot speed (m/s)
    public float swipePowerCurve = 1.3f;            // exponent shaping swipe->power mapping

    // -------------------------
    // Net and Solver Settings
    // -------------------------
    [Header("Net Settings")]
    public float netX = 0f;                         // net center X
    public float netHeight = 0.914f;                // net height (meters)
    public float netMargin = 0.25f;                 // clearance margin (meters)

    [Header("Launch Tuning")]
    [Range(0f, 1f)]
    public float speedBlend = 0.8f;                 // 0 = manual only, 1 = solver only
    public float forwardPlaceOffset = 0.05f;        // teleport offset to avoid overlap
    public bool neutralizeUnityDrag = true;         // set rb.drag = 0 if solver models drag

    //Extra angle
    public float minHoldAngleDeg = 4f;
    public float maxExtraPowerFraction = 0.30f;
    public float currentHoldScale;   // 0..1 from LMB/RMB hold logic

    [Header("Situation Default Net Clearance")]
    public bool useSituationDefaultNetClearance = true;
    public float lowContactHeight = 0.3f;
    public float highContactHeight = 1.3f;
    public float slowIncomingSpeed = 8f;
    public float fastIncomingSpeed = 28f;
    public bool logSituationClearance = false;

    [Header("Risk Score Net Clearance")]
    public float flatNormalClearance = 0.80f;
    public float flatSafetyClearance = 1.50f;
    public float topspinNormalClearance = 1.25f;
    public float topspinSafetyClearance = 1.75f;
    public float sliceNormalClearance = 1.00f;
    public float sliceSafetyClearance = 1.75f;
    public float flatLowPowerClearance = 1.20f;
    public float topspinLowPowerClearance = 1.50f;
    public float topspinHeavyNormalClearance = 1.50f;
    public float topspinHeavyLowPowerClearance = 2.00f;
    public float topspinHeavySafetyClearance = 2.00f;
    [Range(0f, 1f)] public float topspinHeavySpinStart = 0.65f;
    public float sliceLowPowerClearance = 1.50f;
    public float highCustomAngleRiskBypassDeg = 4f;
    public bool useRiskScoreNetClearance = true;
    public bool compensateTopspinMagnusNetRise = true;
    public float topspinMagnusCompensationScale = 1.0f;
    public float topspinMaxMagnusClearanceCompensation = 0.55f;
    public float topspinMinCompensatedClearance = 0.30f;
    public bool matchClearanceToBackswingCap = true;
    public float clearanceLowPowerMph = 60f;
    public float clearanceHighPowerMph = 80f;

    [Header("Volley Control Difficulty")]
    public bool applyVolleyDifficultyModel = true;
    public float volleyDeepDistanceFromNet = 5.2f;
    public float volleyFastIncomingSpeed = 28f;
    public float volleyQualityPenalty = 0.22f;
    public float volleyNormDistPenalty = 0.22f;
    public float volleyMaxLateralErrorDeg = 3.5f;
    public float volleySpeedReductionFraction = 0.08f;
    public float volleySpeedControlErrorFraction = 0.16f;
    public bool logVolleyDifficulty = false;
    public ShotClearanceProfile flatClearance = new ShotClearanceProfile
    {
        baseClearance = 0.55f,
        lowContactBonus = 0.45f,
        fastIncomingBonus = 0.25f,
        controlledHoldBonus = 0.06f,
        incomingTopspinBonus = 0.10f,
        incomingSliceBonus = 0.06f,
        minClearance = 0.45f,
        maxClearance = 1.25f
    };

    public ShotClearanceProfile topspinClearance = new ShotClearanceProfile
    {
        baseClearance = 0.85f,
        lowContactBonus = 0.55f,
        fastIncomingBonus = 0.30f,
        controlledHoldBonus = 0.18f,
        incomingTopspinBonus = 0.14f,
        incomingSliceBonus = 0.08f,
        minClearance = 0.65f,
        maxClearance = 1.75f
    };

    public ShotClearanceProfile sliceClearance = new ShotClearanceProfile
    {
        baseClearance = 0.50f,
        lowContactBonus = 0.40f,
        fastIncomingBonus = 0.24f,
        controlledHoldBonus = 0.10f,
        incomingTopspinBonus = 0.10f,
        incomingSliceBonus = 0.06f,
        minClearance = 0.45f,
        maxClearance = 1.25f
    };

    [Header("Long Slice Net Clearance")]
    public float longSliceMinTargetDistanceFromNet = 4.5f;
    public float longSliceFullBonusTargetDistanceFromNet = 8.5f;
    public float longSliceExtraNetClearance = 0.35f;
    public float longSliceMaxNetClearance = 1.60f;

    public ShotClearanceProfile lobClearance = new ShotClearanceProfile
    {
        baseClearance = 1.8f,
        lowContactBonus = 0.70f,
        fastIncomingBonus = 0.40f,
        controlledHoldBonus = 0.25f,
        incomingTopspinBonus = 0.20f,
        incomingSliceBonus = 0.12f,
        minClearance = 1.4f,
        maxClearance = 3.2f
    };

    public ShotClearanceProfile dropClearance = new ShotClearanceProfile
    {
        baseClearance = 0.70f,
        lowContactBonus = 0.40f,
        fastIncomingBonus = 0.20f,
        controlledHoldBonus = 0.16f,
        incomingTopspinBonus = 0.08f,
        incomingSliceBonus = 0.10f,
        minClearance = 0.50f,
        maxClearance = 1.35f
    };

    [Header("Drop Shot Profile")]
    public bool enableSliceRmbDropShotProfile = true;
    public float dropTargetMaxDistanceFromNet = 3.2f;
    public float dropContactBaselineDistanceFromNet = 7.5f;
    public float dropContactMidCourtDistanceFromNet = 3.8f;
    public Vector2 dropProfileClearanceBaseline = new Vector2(0.3f, 0.6f);
    public Vector2 dropProfileClearanceMidCourt = new Vector2(0.2f, 0.4f);
    public Vector2 dropProfileClearanceFrontCourt = new Vector2(0.1f, 0.3f);
    [Range(0f, 1f)] public float dropProfileHeightIntent = 0.72f;
    [Range(0f, 1f)] public float dropProfileSpinIntent = 0.9f;
    public bool logDropShotProfile = false;



    // -------------------------
    // Debug Flags
    // -------------------------
    [Header("Debug")]
    public bool debugLogs = false;
    public bool logRigidbodyStateBeforeAfter = false;
    public bool logDetailedDirection = false;
    public bool logTriggerEvents = false;
    public bool logMissingReferences = false;
    public bool logShotTiming = false;

    [Header("Solver Speed Stats")]
    public bool logSolverSpeedStats = true;
    public bool logSolverSpeedSamples = false;
    [Min(1)] public int solverSpeedStatsReportEvery = 12;
    private bool loggedMissingSwipeReference;
    private bool loggedMissingBallReference;

    [Header("Overhead Shot UI")]
    public bool autoCreateOverheadPowerBar = true;

    [Header("Last Shot UI Data")]
    [HideInInspector] public bool hasLastShotUiData;
    [HideInInspector] public float lastManualShotSpeed;
    [HideInInspector] public float lastBlendedShotSpeed;
    [HideInInspector] public float lastTargetShotSpeed;
    [HideInInspector] public float lastFinalShotSpeed;
    [HideInInspector] public float lastRacketDriveShotSpeed;
    [HideInInspector] public float lastManualAfterContactShotSpeed;
    [HideInInspector] public float lastIncomingPaceBonus;
    [HideInInspector] public float lastMaxAssistedShotSpeed;
    [HideInInspector] public bool lastTargetSpeedCapped;
    [HideInInspector] public bool lastSolverUsed;
    [HideInInspector] public string lastSolverCacheSource;
    [HideInInspector] public int lastSolverCandidateCount;
    [HideInInspector] public bool lastSolverTargetExtended;
    [HideInInspector] public float lastSolverTargetExtensionM;
    [HideInInspector] public float lastSolverSafetyLiftDeg;
    [HideInInspector] public float lastBackswingCapSpeedMph;
    [HideInInspector] public float lastRetainedCapSpeedMph;
    [HideInInspector] public float lastSolverNetClearanceCm;
    [HideInInspector] public float lastActualNetClearanceCm;
    [HideInInspector] public float lastShotUiTime = -100f;

    // -------------------------
    // Internal state (non-inspector)
    // -------------------------
    Camera mainCam;
    bool ballInZone = false;                        // trigger state
    [HideInInspector] public bool ballIsInHittingZone = false; // unified public flag
    private int lastAcceptedBallInstanceId = int.MinValue;
    private int lastAcceptedShotSequence = int.MinValue;
    bool coolingDown = false;                       // cooldown after a hit
                                                  // Tracks previous in-range state so we only log on transitions
    private bool lastBallInRange = false;
    public HitAttemptResult LastHitAttemptResult { get; private set; } = HitAttemptResult.MissingReference;
    private float oneShotSafetyClearanceBonus;
    private float oneShotIntendedNetClearanceFloor;

    /// <summary>
    /// Requests a one-shot clearance floor for an intentional defensive return.
    /// The normal shot-type safety limit remains authoritative.
    /// </summary>
    public void SetOneShotSafetyClearanceBonus(float bonus)
    {
        oneShotSafetyClearanceBonus = Mathf.Max(0f, bonus);
    }

    /// <summary>
    /// Requests a one-shot minimum intended clearance. This deliberately feeds
    /// the normal clearance-to-angle formula, rather than adding an arbitrary
    /// launch-angle lift. It is used by AI recovery balls that need extra flight
    /// time after a badly stretched contact.
    /// </summary>
    public void SetOneShotIntendedNetClearanceFloor(float minimumClearance)
    {
        oneShotIntendedNetClearanceFloor = Mathf.Max(0f, minimumClearance);
    }

    public void ClearOneShotSafetyClearanceBonus()
    {
        oneShotSafetyClearanceBonus = 0f;
        oneShotIntendedNetClearanceFloor = 0f;
    }

    // -------------------------
    // Drag-aware solver instances (wired in Awake)
    // -------------------------
    private DragBallistics phys;
    private DragTrajectorySolver traj;
    private DragShotSolver shotSolver;

    // Add a small guard field at class scope:
    private float lastCooldownLogTime = -10f;
    private const float cooldownLogInterval = 0.5f; // seconds

    // ----------------------------------------------------------------------
    // 1. SET BALL REFERENCE (called by SwipeMouseBall.SpawnNewBall)
    // ----------------------------------------------------------------------
    // Ensures HitController always targets the correct, newly spawned ball.
    // ----------------------------------------------------------------------

    public void SetBallReference(Transform newBall)
    {
        if (newBall == null)
        {
            Debug.LogWarning("[PHC] SetBallReference called with null.");
            ball = null;
            return;
        }

        var rb = newBall.GetComponent<Rigidbody>();
        if (rb == null)
        {
            Debug.LogWarning("[PHC] SetBallReference: provided transform has no Rigidbody.");
            return;
        }

        ball = rb;
        loggedMissingBallReference = false;
        if (logMissingReferences) Debug.Log($"[PHC] Ball reference updated to instance ID = {ball.gameObject.GetInstanceID()}");
    }

    // ----------------------------------------------------------------------
    // 0. INITIAL SETUP (Awake / Start)
    // ----------------------------------------------------------------------
    // • Cache camera reference
    // • Cache swipe reference
    // • Cache aiming controller
    // • Initialise cooldown timers
    // • Prepare UI hooks

    void Awake()
    {
        mainCam = Camera.main ?? Camera.current;

        if (hitZone == null)
        {
            var c = GetComponent<Collider>();
            if (c != null && c.isTrigger) hitZone = c;
        }

        // Prefer the inspector-provided solver component so preview and hit use same traj
        if (solverComponent != null)
        {
            // Use the wrapper's instances
            phys = solverComponent.phys;
            traj = solverComponent.traj;
            shotSolver = solverComponent.solver;
        }
        else
        {
            // Fallback for quick testing (not recommended for final)
            phys = new DragBallistics();

            // NEW: create MagnusBallistics
            MagnusBallistics magnus = new MagnusBallistics();

            // NEW: pass MagnusBallistics into the solver
            traj = new DragTrajectorySolver(phys, magnus);

            // NEW: pass updated traj into DragShotSolver
            shotSolver = new DragShotSolver(traj);
        }

        if (autoCreateOverheadPowerBar && GetComponent<OverheadShotPowerBar>() == null)
            gameObject.AddComponent<OverheadShotPowerBar>();
    }


    // ----------------------------------------------------------------------
    // 2. UPDATE LOOP — RANGE + COOLDOWN ONLY
    // ----------------------------------------------------------------------
    // • Check swipe reference
    // • Check ball reference
    // • Check cooldown
    // • Check if ball is in range
    //
    // IMPORTANT:
    //   HitController NO LONGER polls swipe input.
    //   SwipeMouseBall calls HitBallUsingSwipe() directly.
    // ----------------------------------------------------------------------

    void Update()
    {
        if (swipe == null)
        {
            if (logMissingReferences && !loggedMissingSwipeReference)
            {
                Debug.LogWarning("[PHC] swipe reference is null.");
                loggedMissingSwipeReference = true;
            }
            return;
        }

        loggedMissingSwipeReference = false;

        if (ball == null)
        {
            if (logMissingReferences && !loggedMissingBallReference)
            {
                Debug.LogWarning("[PHC] ball reference is null. Waiting for spawner callback.");
                loggedMissingBallReference = true;
            }
            return;
        }

        loggedMissingBallReference = false;

        if (coolingDown)
        {
            if (debugLogs && Time.time - lastCooldownLogTime > cooldownLogInterval)
            {
                Debug.Log($"[PHC] In hit cooldown; ignoring input. time={Time.time:F3}");
                lastCooldownLogTime = Time.time;
            }
            return;
        }

        bool inRangeNow = BallIsInRange();

        if (!inRangeNow)
        {
            if (debugLogs && lastBallInRange)
                Debug.Log("[PHC] Ball left range; hit disabled.");

            lastBallInRange = false;
            return;
        }
        else
        {
            if (debugLogs && !lastBallInRange)
                Debug.Log("[PHC] Ball entered range; ready for hit.");

            lastBallInRange = true;
        }

        // IMPORTANT:
        // No swipe polling here anymore.
        // SwipeMouseBall calls HitBallUsingSwipe() directly.
    }

    public void ConfigureAuthoritativeContactZone(Transform center, Vector3 localOffset, Vector3 radii)
    {
        authoritativeContactZoneCenter = center != null ? center : transform;
        authoritativeContactZoneLocalOffset = localOffset;
        authoritativeContactZoneRadii = new Vector3(
            Mathf.Max(0.01f, Mathf.Abs(radii.x)),
            Mathf.Max(0.01f, Mathf.Abs(radii.y)),
            Mathf.Max(0.01f, Mathf.Abs(radii.z)));
        authoritativeContactZoneConfigured = true;
    }

    public bool TryGetAuthoritativeContactZonePose(out Vector3 center, out Quaternion rotation, out Vector3 radii)
    {
        Transform source = authoritativeContactZoneCenter != null ? authoritativeContactZoneCenter : transform;
        center = source.position + source.rotation * authoritativeContactZoneLocalOffset;
        rotation = source.rotation;
        radii = new Vector3(
            Mathf.Max(0.01f, Mathf.Abs(authoritativeContactZoneRadii.x)),
            Mathf.Max(0.01f, Mathf.Abs(authoritativeContactZoneRadii.y)),
            Mathf.Max(0.01f, Mathf.Abs(authoritativeContactZoneRadii.z)));
        return authoritativeContactZoneConfigured;
    }

    public bool IsPointInsideAuthoritativeContactZone(Vector3 worldPoint, float objectRadius = 0f)
    {
        if (!TryGetAuthoritativeContactZonePose(out Vector3 center, out Quaternion rotation, out Vector3 radii))
            return BallIsInRange();

        radii += Vector3.one * Mathf.Max(0f, objectRadius);
        Vector3 local = Quaternion.Inverse(rotation) * (worldPoint - center);
        return NormalizedEllipsoidDistanceSquared(local, radii) <= 1f;
    }

    public bool SweepIntersectsAuthoritativeContactZone(
        Vector3 previousBallPosition,
        Vector3 currentBallPosition,
        Vector3 previousZoneCenter,
        Quaternion previousZoneRotation,
        float ballRadius,
        out Vector3 contactPosition,
        out float contactFraction)
    {
        contactPosition = currentBallPosition;
        contactFraction = 1f;
        if (!TryGetAuthoritativeContactZonePose(out Vector3 currentZoneCenter, out Quaternion currentZoneRotation, out Vector3 radii))
            return IsPointInsideAuthoritativeContactZone(currentBallPosition, ballRadius);

        radii += Vector3.one * Mathf.Max(0f, ballRadius);
        Vector3 previousLocal = DivideComponents(
            Quaternion.Inverse(previousZoneRotation) * (previousBallPosition - previousZoneCenter),
            radii);
        Vector3 currentLocal = DivideComponents(
            Quaternion.Inverse(currentZoneRotation) * (currentBallPosition - currentZoneCenter),
            radii);

        if (!TryIntersectUnitSphereSegment(previousLocal, currentLocal, out contactFraction))
            return false;

        contactPosition = Vector3.Lerp(previousBallPosition, currentBallPosition, contactFraction);
        return true;
    }

    public float GetBallContactRadius(Rigidbody targetBall = null)
    {
        Rigidbody source = targetBall != null ? targetBall : ball;
        if (source == null)
            return 0.033f;

        SphereCollider sphere = source.GetComponent<SphereCollider>();
        if (sphere != null)
        {
            Vector3 scale = sphere.transform.lossyScale;
            return Mathf.Max(0.001f, sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        }

        Collider collider = source.GetComponent<Collider>();
        if (collider != null)
            return Mathf.Max(0.001f, Mathf.Min(collider.bounds.extents.x, collider.bounds.extents.y, collider.bounds.extents.z));

        return 0.033f;
    }

    private static float NormalizedEllipsoidDistanceSquared(Vector3 local, Vector3 radii)
    {
        Vector3 normalized = DivideComponents(local, radii);
        return normalized.sqrMagnitude;
    }

    private static Vector3 DivideComponents(Vector3 value, Vector3 divisor)
    {
        return new Vector3(
            value.x / Mathf.Max(0.0001f, divisor.x),
            value.y / Mathf.Max(0.0001f, divisor.y),
            value.z / Mathf.Max(0.0001f, divisor.z));
    }

    private static bool TryIntersectUnitSphereSegment(Vector3 start, Vector3 end, out float fraction)
    {
        fraction = 0f;
        if (start.sqrMagnitude <= 1f)
            return true;

        Vector3 delta = end - start;
        float a = Vector3.Dot(delta, delta);
        if (a <= 0.0000001f)
            return end.sqrMagnitude <= 1f;

        float b = 2f * Vector3.Dot(start, delta);
        float c = Vector3.Dot(start, start) - 1f;
        float discriminant = b * b - 4f * a * c;
        if (discriminant < 0f)
            return false;

        float root = Mathf.Sqrt(discriminant);
        float inverse = 1f / (2f * a);
        float t0 = (-b - root) * inverse;
        float t1 = (-b + root) * inverse;
        if (t0 >= 0f && t0 <= 1f)
        {
            fraction = t0;
            return true;
        }
        if (t1 >= 0f && t1 <= 1f)
        {
            fraction = t1;
            return true;
        }

        return false;
    }

    // ============================================================
    //  3. Trigger Events
    // ------------------------------------------------------------
    //  These events fire when the ball enters or exits the player's
    //  hitting zone. This is the most reliable and responsive way
    //  to determine whether the ball is close enough to be struck.
    //
    //  The trigger zone acts as the primary detection method because
    //  it accounts for real-time ball movement, collider shapes,
    //  and Unity’s physics timing. It ensures that hits feel fair,
    //  consistent, and responsive.
    //
    //  If the trigger ever fails (rare, but possible with fast balls
    //  or unusual collider shapes), BallIsInRange() provides two
    //  fallback checks:
    //      1. Collider closest-point test
    //      2. Simple distance check
    //
    //  Together, these three layers guarantee robust hit detection.
    // ============================================================

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Ball")) return;

        if (logTriggerEvents)
        {
            Debug.Log($"[PHC] >>> TRIGGER ENTER at time={Time.time:F3}  ballInZone(before)={ballInZone}  colliderID={other.GetInstanceID()}");
        }

        // If we don't yet have a ball reference, try to grab it from the collider's attached rigidbody
        if (ball == null)
        {
            var otherRb = other.attachedRigidbody;
            if (otherRb != null)
            {
                ball = otherRb;
                if (logMissingReferences) Debug.Log($"[PHC] Auto-assigned ball reference from trigger (instance {ball.gameObject.GetInstanceID()}).");
            }
        }

        ballInZone = true;
        ballIsInHittingZone = true;

        if (logTriggerEvents) Debug.Log($"[PHC] OnTriggerEnter: Ball entered hit zone ({other.gameObject.name})");
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Ball")) return;

        if (logTriggerEvents)
        {
            Debug.Log($"[PHC] >>> TRIGGER EXIT at time={Time.time:F3}  ballInZone(before)={ballInZone}  colliderID={other.GetInstanceID()}");
        }

        // Only clear zone flags if the exiting collider belongs to the current ball (defensive)
        var otherRb = other.attachedRigidbody;
        if (otherRb == null || ball == null || otherRb.gameObject == ball.gameObject)
        {
            ballInZone = false;
            ballIsInHittingZone = false;

            if (logTriggerEvents) Debug.Log($"[PHC] OnTriggerExit: Ball left hit zone ({other.gameObject.name})");
        }
    }

    // Optional: helps with very fast balls that might skip Enter due to physics timing.
    // Enable only if you observe missed enters in testing.
    void OnTriggerStay(Collider other)
    {
        if (!other.CompareTag("Ball")) return;

        if (!ballInZone)
        {
            if (logTriggerEvents) Debug.Log($"[PHC] >>> TRIGGER STAY detected ball (colliderID={other.GetInstanceID()}) - forcing enter state.");
            // Defensive: set state without calling OnTriggerEnter to avoid duplicate logs
            ballInZone = true;
            ballIsInHittingZone = true;
        }
    }

    // ============================================================
    //  4. BallIsInRange()
    // ------------------------------------------------------------
    //  This method determines whether the ball is close enough to
    //  be hit. It is used as a fallback when trigger detection is
    //  unavailable or unreliable.
    //
    //  Priority order:
    //      1. Trigger zone (most accurate, real-time)
    //      2. Collider closest-point test (precise geometric check)
    //      3. Distance fallback (simple but reliable)
    //
    //  This layered approach ensures:
    //      • Hits feel fair
    //      • No “ghost hits” occur
    //      • No missed hits due to physics timing
    //
    //  The unified flag ballIsInHittingZone is updated here so that
    //  other systems (UI, animations, AI) can query hit readiness.
    // ============================================================

    bool BallIsInRange()
    {
        if (ball == null) return false;

        // 1) Trigger zone — primary detection
        if (ballInZone)
        {
            ballIsInHittingZone = true;
            return true;
        }

        // 2) Collider closest-point — precise fallback
        if (hitZone != null)
        {
            Vector3 closest = hitZone.ClosestPoint(ball.position);
            float sqrDist = (closest - ball.position).sqrMagnitude;
            const float toleranceSqr = 0.0001f; // ~1 cm^2 tolerance
            if (sqrDist <= toleranceSqr)
            {
                ballIsInHittingZone = true;
                return true;
            }
        }

        // 3) Distance fallback — last resort (use squared distance for performance)
        float sqrHitRadius = hitRadius * hitRadius;
        bool inRange = (transform.position - ball.position).sqrMagnitude <= sqrHitRadius;
        ballIsInHittingZone = inRange;
        return inRange;
    }


    // test shot

    // ----------------------------------------------------------------------
    // HITBALLUSINGSWIPE — MAIN SHOT EXECUTION PIPELINE
    // ----------------------------------------------------------------------
    // This method is the core of the shot-creation system. It takes the final
    // swipe data produced by swipeMouseBall (aim direction, swipe speed,
    // shot quality.) and transforms it into a fully-formed,
    // deterministic tennis shot.
    //
    // ----------------------------------------------------------------------
    // 5. HIT BALL USING SWIPE
    // ----------------------------------------------------------------------
    // Called directly by SwipeMouseBall when a swipe completes.
    // Contains the full shot-building pipeline:
    //
    //   5.1 Safety checks
    //   5.2 Horizontal aim blend
    //   5.3 Straight-shot difficulty model
    //       5.3a Length stability
    //       5.3b Straightness stability
    //       5.3c Wobble stability
    //   5.4 Drag-aware solver (TrajectoryCalculator)
    //   5.5 Speed blend + angle selection
    //   5.6 Build final 3D launch velocity
    //   5.7 Place ball + apply velocity
    //
    // No height from swipe. No scroll. No legacy metrics.
    // ----------------------------------------------------------------------
    //
    // This method is intentionally high-level and declarative. It orchestrates
    // the shot but delegates all detailed logic to the appropriate subsystems:
    //
    //      • swipeMouseBall ? raw swipe interpretation
    //        BallController ? physics quality + velocity logging
    //
    // This separation keeps the shot model clean, tunable, and expressive,
    // and makes it easy to add new shot types (topspin, slice, lob) later.
    // ----------------------------------------------------------------------

    private float ResolveActiveMaxShotPower()
    {
        MatchServicePointController match = MatchServicePointController.Active;
        if (match != null && match.IsCurrentServerController(this))
            return Mathf.Max(maxShotPower, match.CurrentServeSpeedCapMps);
        return maxShotPower;
    }

    private float ResolveActiveBackswingCapSpeed(float swipeCapSpeed)
    {
        MatchServicePointController match = MatchServicePointController.Active;
        if (match != null && match.IsCurrentServerController(this))
            return Mathf.Clamp(swipeCapSpeed, match.GetPlayerServeBackswingCapSpeed(0f), match.CurrentServeSpeedCapMps);
        return swipeCapSpeed;
    }

    private bool HasAcceptedCurrentShot(BallController controller)
    {
        if (ball == null)
            return false;

        int instanceId = ball.gameObject.GetInstanceID();
        int sequence = controller != null ? controller.ShotSequence : 0;
        return instanceId == lastAcceptedBallInstanceId && sequence == lastAcceptedShotSequence;
    }

    private void MarkCurrentShotAccepted(BallController controller)
    {
        if (ball == null)
            return;

        lastAcceptedBallInstanceId = ball.gameObject.GetInstanceID();
        lastAcceptedShotSequence = controller != null ? controller.ShotSequence : 0;
    }

    public void ResetAcceptedContactForNewServeAttempt()
    {
        lastAcceptedBallInstanceId = 0;
        lastAcceptedShotSequence = int.MinValue;
        LastHitAttemptResult = HitAttemptResult.MissingReference;
    }

    private HitAttemptResult FinishHitAttempt(HitAttemptResult result, string reason, BallController controller = null)
    {
        LastHitAttemptResult = result;
        if (logHitAttemptResults)
        {
            int sequence = controller != null ? controller.ShotSequence : -1;
            Debug.Log($"[HIT RESULT] controller={name} result={result} shot={sequence} reason=\"{reason}\".");
        }
        return result;
    }

    public HitAttemptResult HitBallUsingSwipe(
        SwipeData swipe,
        BaseShotType baseType,
        ShotModifier modifier,
        HitContactConfirmation contactConfirmation = default)
    {
        MatchServicePointController matchService = MatchServicePointController.Active;
        if (matchService != null && matchService.IsMatchActive &&
            (!matchHitAllowed || !matchService.IsHitAllowed(this, contactConfirmation)))
            return FinishHitAttempt(HitAttemptResult.MatchBlocked, "match phase/controller rejected contact");

        if (ball == null)
        {
            Debug.LogWarning("[PHC] Hit attempted but ball reference is null.");
            return FinishHitAttempt(HitAttemptResult.MissingReference, "ball reference is null");
        }

        Rigidbody rb = ball;
        BallController bc = ball.GetComponent<BallController>();

        if (reticle == null || shotHeightUI == null)
        {
            string missing = reticle == null ? "reticle" : "shot-height UI";
            Debug.LogWarning($"[PHC] Required {missing} reference is null.");
            return FinishHitAttempt(HitAttemptResult.MissingReference, $"{missing} reference is null", bc);
        }

        float ballRadius = GetBallContactRadius(ball);
        bool physicalContactConfirmed = contactConfirmation.confirmed ||
            IsPointInsideAuthoritativeContactZone(ball.position, ballRadius);
        if (!physicalContactConfirmed)
            return FinishHitAttempt(HitAttemptResult.OutsideContactZone, "ball did not intersect authoritative contact zone", bc);

        if (HasAcceptedCurrentShot(bc))
            return FinishHitAttempt(HitAttemptResult.AlreadyHitThisShot, "controller already accepted this ball shot sequence", bc);

        MarkCurrentShotAccepted(bc);
        hasLastShotUiData = false;
        currentBaseShotType = baseType;
        currentModifier = modifier;
        currentSpinIntent = Mathf.Clamp01(swipe.spinIntent);

        if (debugLogs)
            Debug.Log($"[PHC] RECEIVED SHOT TYPE = {baseType}, MODIFIER = {modifier}");

        Vector3 aimDir = swipe.aimDir;
        float speed = swipe.speed;
        float quality = swipe.quality;
        float normDist = swipe.normDist;
        currentHoldScale = Mathf.Clamp01(swipe.holdScale);

        if (debugLogs)
        {
            Debug.Log($"[PHC] IN speed(manual raw)={speed:F2}, aimDir={aimDir}, quality={quality:F2}, normDist={normDist:F2}, spinIntent={currentSpinIntent:F2}");
            Debug.Log($"[PHC] >>> HitBallUsingSwipe CALLED at time={Time.time:F3}  coolingDown={coolingDown}");
            Debug.Log($"[PHC] HitBallUsingSwipe CALLED WITH speed={speed:F2}");
        }

        
        // Ownership transfers as soon as the racket makes an accepted contact
        // attempt. A later solver failure is therefore a confirmed mishit.
        Vector3 acceptedContactPosition = contactConfirmation.confirmed
            ? contactConfirmation.contactPosition
            : rb.position;
        RacketContactRegistered?.Invoke(this, ball, acceptedContactPosition);

        Vector3 liveBallPosBeforeSolve = acceptedContactPosition;
        Vector3 liveBallVelocityBeforeSolve = rb.linearVelocity;
        Vector3 liveBallSpinBeforeSolve = bc != null ? bc.spinRadPerSecond : Vector3.zero;

        float volleyDifficulty = ComputeDeepVolleyDifficulty(bc, liveBallPosBeforeSolve, liveBallVelocityBeforeSolve);
        if (volleyDifficulty > 0f)
        {
            float originalQuality = quality;
            float originalNormDist = normDist;
            float originalSpeed = speed;
            float signedSpeedError = ComputeSignedVolleyError(liveBallPosBeforeSolve, liveBallVelocityBeforeSolve, 11.7f);

            quality = Mathf.Clamp01(quality - Mathf.Max(0f, volleyQualityPenalty) * volleyDifficulty);
            normDist = Mathf.Clamp01(normDist + Mathf.Max(0f, volleyNormDistPenalty) * volleyDifficulty);
            speed *= Mathf.Max(0.2f,
                1f - Mathf.Max(0f, volleySpeedReductionFraction) * volleyDifficulty
                + signedSpeedError * Mathf.Max(0f, volleySpeedControlErrorFraction) * volleyDifficulty);

            if (logVolleyDifficulty)
            {
                Debug.Log(
                    $"[VOLLEY DIFFICULTY] difficulty={volleyDifficulty:F2}, " +
                    $"quality={originalQuality:F2}->{quality:F2}, normDist={originalNormDist:F2}->{normDist:F2}, " +
                    $"speed={originalSpeed:F2}->{speed:F2}, noBounce={(bc != null ? bc.CourtBouncesSinceLastHit : -1)}");
            }
        }


        // ------------------------------------------------------------
        // 5.2 HORIZONTAL AIM BLEND
        // ------------------------------------------------------------
        // • Convert aimDir into world-space aim
        // • Clamp angle to ±45°
        // • Produce lateralDir (horizontal only)
        // ------------------------------------------------------------

        Vector3 toReticle = (reticle.position - ball.transform.position);
        if (toReticle.sqrMagnitude < 1e-6f) toReticle = Vector3.forward;
        toReticle.y = 0f;
        toReticle.Normalize();

        Vector3 playerAim = new Vector3(aimDir.x, 0f, aimDir.z);
        if (playerAim.sqrMagnitude < 1e-6f) playerAim = toReticle;
        playerAim.Normalize();

        float maxAngle = Mathf.Max(0f, maxManualAimAngleDeg);
        float signedAngle = Vector3.SignedAngle(toReticle, playerAim, Vector3.up);
        float clampedAngle = Mathf.Clamp(signedAngle, -maxAngle, maxAngle);

        Quaternion rot = Quaternion.AngleAxis(clampedAngle, Vector3.up);
        Vector3 clampedPlayerAim = (rot * toReticle).normalized;
        float manualWeight = Mathf.Clamp01(manualAimWeight);
        Vector3 lateralDir = Vector3.Slerp(toReticle, clampedPlayerAim, manualWeight).normalized;

        if (volleyDifficulty > 0f)
        {
            float signedVolleyError = ComputeSignedVolleyError(liveBallPosBeforeSolve, liveBallVelocityBeforeSolve, 23.1f);
            float volleyErrorDeg = signedVolleyError * Mathf.Max(0f, volleyMaxLateralErrorDeg) * volleyDifficulty;
            lateralDir = (Quaternion.AngleAxis(volleyErrorDeg, Vector3.up) * lateralDir).normalized;
        }

        float guiAngleToReticle = Vector3.SignedAngle(toReticle, lateralDir, Vector3.up);
        shotHeightUI.UpdateLateralAim(guiAngleToReticle);

        if (logDetailedDirection)
        {
            Debug.Log(
                $"[AIM BLEND] manualWeight={manualWeight:F2}, " +
                $"rawManualAngle={signedAngle:F2}, clampedManualAngle={clampedAngle:F2}, " +
                $"finalBlendAngle={guiAngleToReticle:F2}, " +
                $"toReticle={toReticle}, playerAim={playerAim}, lateralDir={lateralDir}"
            );
        }

        // ------------------------------------------------------------
        // 5.3 STRAIGHT SHOT STABILITY MODEL (extracted helper) //
        // 5.3a Length stability (normDist)
        // 5.3b Straightness stability (angle to reticle)
        // 5.3c Wobble stability (shotQuality)
        //
        // Produces:
        //   stability ? deterministic lateral drift
        // ------------------------------------------------------------

        float finalLateralAngle = guiAngleToReticle;

        if (applyShotStabilityAfterAim)
        {
            var stabilityResult = ShotStabilityCalculator.Compute(
                normDist,
                guiAngleToReticle,
                quality,
                toReticle,
                lateralDir
            );

            lateralDir = stabilityResult.adjustedAimDir;
            finalLateralAngle = stabilityResult.guiFinalShotAngle;

            if (logDetailedDirection)
            {
                Debug.Log(
                    $"[AIM STABILITY] enabled=true, " +
                    $"inputAngle={guiAngleToReticle:F2}, " +
                    $"outputAngle={finalLateralAngle:F2}, " +
                    $"quality={quality:F2}, normDist={normDist:F2}, lateralDir={lateralDir}"
                );
            }
        }

        if (applySmallAngleBoosterAfterAim)
        {
            float beforeBoostAngle = Vector3.SignedAngle(toReticle, lateralDir, Vector3.up);
            lateralDir = SmallAngleBooster.Apply(lateralDir);
            finalLateralAngle = Vector3.SignedAngle(toReticle, lateralDir, Vector3.up);

            if (logDetailedDirection)
            {
                Debug.Log(
                    $"[AIM BOOSTER] enabled=true, " +
                    $"inputAngle={beforeBoostAngle:F2}, " +
                    $"outputAngle={finalLateralAngle:F2}, lateralDir={lateralDir}"
                );
            }
        }

        shotHeightUI.UpdateLateralShot(finalLateralAngle);


        // ------------------------------------------------------------
        // 5.4 DRAG-AWARE SOLVER (TrajectoryCalculator)
        // ------------------------------------------------------------
        // • Query solver for v0 + theta
        // • Validate solver output
        // • Compute fallback angle + speed if solver fails
        // ------------------------------------------------------------

        // -----------------------------
        // SOLVER: get suggestion, blend, then ensure solver plans for finalV0
        // -----------------------------
        // Notes:
        //  • We must compute the actual finalV0 (including shot type + modifier)
        //    before asking the solver for the launch angle so the solver's theta
        //    matches the speed that will actually be used at launch.
        //  • If the aimingController exposes a helper that computes theta for a
        //    given v0 (GetShotParametersForV0), we call it. If not, we fall back
        //    to the original solver suggestion or the fallback path.
        //  • This preserves shot type semantics while preventing the solver from
        //    planning for a different speed than the one actually applied.
        // -----------------------------


        ShotComputationSolver solver = new ShotComputationSolver();
        Vector3 contactPos = acceptedContactPosition;
        Vector3 placePos = contactPos + lateralDir * forwardPlaceOffset;
        rb.transform.position = placePos;
        rb.position = placePos;

        bool useSliceDropProfile = ShouldUseSliceRmbDropProfile(swipe, baseType, placePos);
        bool useDropTrajectoryProfile = useSliceDropProfile || baseType == BaseShotType.Drop;
        BaseShotType solverBaseType = useDropTrajectoryProfile ? BaseShotType.Drop : baseType;
        ShotModifier solverModifier = useDropTrajectoryProfile ? ShotModifier.Slow : modifier;
        if (useDropTrajectoryProfile)
            currentSpinIntent = Mathf.Max(currentSpinIntent, Mathf.Clamp01(dropProfileSpinIntent));

        float heightIntent = useDropTrajectoryProfile
            ? Mathf.Clamp01(dropProfileHeightIntent)
            : BaseShotLibrary.HeightIntent;

        float desiredNetClearance = ComputeDesiredNetClearance(
            solverBaseType,
            placePos.y,
            placePos,
            speed,
            liveBallVelocityBeforeSolve,
            bc,
            currentHoldScale,
            swipe.forwardSwingProgress,
            heightIntent,
            ResolveActiveBackswingCapSpeed(swipe.backswingCapSpeed),
            out string clearanceDebug
        );

        if (!useDropTrajectoryProfile && oneShotSafetyClearanceBonus > 0f)
        {
            desiredNetClearance = ApplyOneShotSafetyClearanceFloor(
                solverBaseType,
                desiredNetClearance,
                oneShotSafetyClearanceBonus,
                ref clearanceDebug);
        }

        if (!useDropTrajectoryProfile && oneShotIntendedNetClearanceFloor > 0f)
        {
            desiredNetClearance = ApplyOneShotIntendedNetClearanceFloor(
                desiredNetClearance,
                oneShotIntendedNetClearanceFloor,
                ref clearanceDebug);
        }

        if (useDropTrajectoryProfile)
        {
            float contactDistanceFromNet = Mathf.Abs(placePos.x - netX);
            desiredNetClearance = ComputeDropProfileNetClearance(contactDistanceFromNet, out string dropProfileDebug);
            clearanceDebug = dropProfileDebug;
        }

        if (logDropShotProfile && useDropTrajectoryProfile)
        {
            Debug.Log($"[DROP SHOT PROFILE] source={(useSliceDropProfile ? "slice-rmb" : "drop")}, contactDistFromNet={Mathf.Abs(placePos.x - netX):F2}m, targetPastNet={Mathf.Abs(reticle.position.x - netX):F2}m, clearance={desiredNetClearance:F2}m, heightIntent={heightIntent:F2}, spinIntent={currentSpinIntent:F2}");
        }

        float solverStartRealtime = Time.realtimeSinceStartup;

        var shot = solver.ComputeShot(
            manualV0: speed,                     // swipe speed
            speedBlend: speedBlend,              // solver/manual blend
            quality: quality,                    // contact quality
            baseType: solverBaseType,            // effective shot type (Flat, Topspin, Slice, Drop)
            modifier: solverModifier,            // Fast, Slow, etc.
            holdScale: currentHoldScale,         // 0..1 from LMB/RMB hold
            minHoldAngleDeg: minHoldAngleDeg,    // inspector tuning
            maxExtraPowerFraction: maxExtraPowerFraction,
            spinIntent: currentSpinIntent,
            backswingScale: swipe.backswingScale,
            forwardSwingProgress: swipe.forwardSwingProgress,
            backswingCapSpeed: ResolveActiveBackswingCapSpeed(swipe.backswingCapSpeed),
            maxShotPower: ResolveActiveMaxShotPower(),
            desiredNetClearance: desiredNetClearance,
            heightIntent: heightIntent,
            heightAngleDeg: float.NaN,
            useHeightAngleOverride: true,
            incomingVelocity: liveBallVelocityBeforeSolve,
            incomingSpinRadPerSecond: liveBallSpinBeforeSolve,
            returnDirection: lateralDir,
            aimingController: aimingController,
            reticle: reticle,
            ball: ball.transform,
            liveSolveMode: ShotComputationSolver.LiveShotSolveMode.FixedAngleOnly,
            absoluteMaxSpeed: ResolveActiveMaxShotPower()
        );
        float solverMs = (Time.realtimeSinceStartup - solverStartRealtime) * 1000f;

        if (!float.IsFinite(shot.finalV0) || !float.IsFinite(shot.finalTheta) || shot.finalV0 <= 0.01f)
        {
            Debug.LogWarning($"[PHC] Solver produced an invalid launch: speed={shot.finalV0}, angle={shot.finalTheta}.");
            return FinishHitAttempt(HitAttemptResult.SolverFailed, "solver produced invalid speed or angle", bc);
        }

        // ------------------------------------------------------------
        // 5.x VERTICAL SHAPING + LAUNCH VELOCITY BUILD
        // ------------------------------------------------------------
        // Convert final speed + angle into vx, vy.
        // Apply vertical caps + quality shaping.
        // Build final 3D launch velocity.
        // ------------------------------------------------------------

        float vx = shot.finalV0 * Mathf.Cos(shot.finalTheta);
        float vy = shot.finalV0 * Mathf.Sin(shot.finalTheta);

        if (debugLogs)
            Debug.Log($"[PHC] finalTheta(deg)={shot.finalTheta * Mathf.Rad2Deg:F2}, vx={vx:F2}, vy={vy:F2}, launchMagnitudeExpected={shot.finalV0:F2}");

        // Cap vertical fraction (prevents moonballs)
        float maxVerticalFraction = 0.45f;
        float maxVy = shot.finalV0 * maxVerticalFraction;

        if (vy > maxVy)
        {
            vy = maxVy;
            vx = Mathf.Sqrt(Mathf.Max(0f, shot.finalV0 * shot.finalV0 - vy * vy));
        }

        // Quality-based vertical bias (clean contact = slightly higher)
        float qualityVerticalBias = Mathf.Lerp(-0.05f, 0.08f, Mathf.Clamp01(quality));
        vy = Mathf.Clamp(vy * (1f + qualityVerticalBias), 0f, shot.finalV0);

        // Build final launch velocity
        Vector3 launchVelocity = lateralDir * vx + Vector3.up * vy;

        if (debugLogs)
            Debug.Log($"[PHC] launchVelocity={launchVelocity}");

        // ------------------------------------------------------------
        // 5.x UI UPDATES (using solver output)
        // ------------------------------------------------------------

        float solverNetClearanceCm = GetSolverNetClearanceCm(shot, desiredNetClearance);
        float actualNetClearanceCm = EstimateActualNetClearanceCm(placePos, launchVelocity, shot);
        if (logShotTiming)
            LogActualTopspinShapeDiagnostic(
            baseType,
            solverBaseType,
            placePos,
            launchVelocity,
            shot,
            desiredNetClearance,
            solverNetClearanceCm,
            actualNetClearanceCm,
            vx,
            vy
        );
        float backswingCapSpeedMph = shot.backswingCapSpeed * 2.23694f;
        float retainedCapSpeedMps = Mathf.Max(shot.racketSpeedCap * (1f + shot.reboundCoefficient), shot.retainedContactFloorV0);
        float retainedCapSpeedMph = retainedCapSpeedMps * 2.23694f;

        shotHeightUI.UpdateManualBallSpeed(shot.manualV0);
        shotHeightUI.UpdateBlendedBallSpeed(shot.finalV0);
        shotHeightUI.UpdateTargetBallSpeed(shot.targetV0);
        shotHeightUI.UpdateTargetHeight(shot.finalTheta * Mathf.Rad2Deg);
        shotHeightUI.UpdateBackswingCapSpeed(backswingCapSpeedMph);
        shotHeightUI.UpdateRetainedCapSpeed(retainedCapSpeedMph);
        shotHeightUI.UpdateSolverNetClearance(solverNetClearanceCm);
        shotHeightUI.UpdateActualNetClearance(actualNetClearanceCm);

        // ------------------------------------------------------------
        // 5.x PLACE BALL + APPLY VELOCITY
        // ------------------------------------------------------------

        if (rb == null)
        {
            Debug.LogWarning("[PHC] No Rigidbody found on ball.");
            return FinishHitAttempt(HitAttemptResult.MissingReference, "ball Rigidbody is null", bc);
        }

        hasLastShotUiData = true;
        lastManualShotSpeed = shot.manualV0;
        lastBlendedShotSpeed = shot.finalV0;
        lastTargetShotSpeed = shot.targetV0;
        lastFinalShotSpeed = shot.finalV0;
        lastRacketDriveShotSpeed = shot.racketDriveSpeed;
        lastManualAfterContactShotSpeed = shot.manualAfterContactV0;
        lastIncomingPaceBonus = shot.incomingPaceBonus;
        lastMaxAssistedShotSpeed = shot.maxAssistedV0;
        lastTargetSpeedCapped = shot.targetSpeedCapped;
        lastSolverUsed = shot.solverUsed;
        lastSolverCacheSource = string.IsNullOrEmpty(shot.solverCacheSource) ? "none" : shot.solverCacheSource;
        lastSolverCandidateCount = shot.solverCandidateCount;
        lastSolverTargetExtended = shot.solverTargetExtended;
        lastSolverTargetExtensionM = shot.solverTargetExtensionM;
        lastSolverSafetyLiftDeg = shot.solverSafetyLiftDeg;
        lastBackswingCapSpeedMph = backswingCapSpeedMph;
        lastRetainedCapSpeedMph = retainedCapSpeedMph;
        lastSolverNetClearanceCm = solverNetClearanceCm;
        lastActualNetClearanceCm = actualNetClearanceCm;
        lastShotUiTime = Time.time;

        RecordSolverSpeedStats(solverBaseType, shot, desiredNetClearance, solverNetClearanceCm, actualNetClearanceCm, solverMs);

        ball.transform.position = placePos;
        rb.position = placePos;

        rb.isKinematic = false;
        rb.useGravity = true;
        rb.WakeUp();

        // Reset physics state
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // Apply final launch velocity
        rb.linearVelocity = launchVelocity;
        PlayerLateralAimResolved?.Invoke(this, finalLateralAngle, toReticle, lateralDir);
        LastLaunchController = this;
        PlayerBallLaunched?.Invoke(rb, rb.position, launchVelocity);

        if (logShotTiming)
        {
            float solveToVelocityMs = (Time.realtimeSinceStartup - solverStartRealtime) * 1000f;
            float speedShortfall = shot.targetV0 - shot.finalV0;
            Debug.Log(
                $"[SHOT SOLVE] t={Time.time:F3}, solverMs={solverMs:F2}, solveToVelocity={solveToVelocityMs:F2}ms, " +
                $"shotType={baseType}, solverType={solverBaseType}, modifier={modifier}, solverModifier={solverModifier}, dropProfile={useDropTrajectoryProfile}, " +
                $"cacheSource={(string.IsNullOrEmpty(shot.solverCacheSource) ? "none" : shot.solverCacheSource)}, cacheHit={shot.solverCacheHit}, " +
                $"liveBallY={liveBallPosBeforeSolve.y:F3}, launchY={placePos.y:F3}, deltaY={(placePos.y - liveBallPosBeforeSolve.y):F3}, " +
                $"liveBallVelY={liveBallVelocityBeforeSolve.y:F2}, liveSpinRad={liveBallSpinBeforeSolve.magnitude:F2}, " +
                $"rawManual={shot.rawManualV0:F2}, rawRacket={shot.rawRacketSpeed:F2}, actualRacket={shot.actualRacketSpeed:F2}, racketCap={shot.racketSpeedCap:F2}, " +
                $"swingScale={shot.forwardSwingScale:F2}, backswingCap={shot.backswingCapSpeed:F2}, racketDrive={shot.racketDriveSpeed:F2}, " +
                $"manualContact={shot.manualAfterContactV0:F2}, manual={shot.manualV0:F2}, " +
                $"incomingAlong={shot.incomingAlongReturn:F2}, rebound={shot.reboundCoefficient:F2}, paceBonus={shot.incomingPaceBonus:F2}, " +
                $"paceFloor={shot.paceFloorV0:F2}, floorApplied={shot.paceFloorApplied}, " +
                $"target={shot.targetV0:F2}, targetRaw={shot.targetV0Uncapped:F2}, maxAssist={shot.maxAssistedV0:F2}, " +
                $"retention={shot.contactRetention:F2}, spinIntent={shot.spinIntent:F2}, targetCapped={shot.targetSpeedCapped}, " +
                $"retainedFloor={shot.retainedContactFloorV0:F2}, retainedFloorApplied={shot.retainedContactFloorApplied}, " +
                $"playerSpin={shot.playerSpinRpm:F0}, incomingSpin={shot.incomingSpinProjectedRpm:F0}, carry={shot.incomingSpinCarryRate:F2}, residualSpin={shot.residualSpinRpm:F0}, " +
                $"angleBias={shot.incomingSpinAngleBiasDeg:F2}, contactVuln={shot.contactVulnerability:F2}, " +
                $"heightCtrl={shot.heightCorrectionControlHold:F2}, heightDiff={shot.heightCorrectionDifficulty:F2}, heightBlend={shot.heightCorrectionBlend:F2}, " +
                $"commonAngle={shot.commonErrorHeightAngleDeg:F2}deg, correctedAngle={shot.correctedHeightAngleDeg:F2}deg, missingLift={shot.missingHeightCorrectionDeg:F2}deg, " +
                $"commonClearance={shot.commonErrorNetClearance:F2}, correctedClearance={shot.correctedNetClearance:F2}, " +
                $"solverClearanceCm={solverNetClearanceCm:F0}, actualNetClearanceCm={actualNetClearanceCm:F0}, " +
                $"blended={shot.finalV0:F2}, shortfall={speedShortfall:F2}, theta={shot.finalTheta * Mathf.Rad2Deg:F2}deg, " +
                $"clearance={desiredNetClearance:F2}, heightIntent={heightIntent:F2}, defaultAngle={shot.defaultHeightAngleDeg:F2}deg, " +
                $"requestedAngle={shot.requestedHeightAngleDeg:F2}deg, angleRange={shot.minHeightAngleDeg:F2}..{shot.maxHeightAngleDeg:F2}deg, " +
                $"fixedHeight={shot.fixedHeightAngleUsed}, vx={vx:F2}, vy={vy:F2}"
            );
        }

        if (logSituationClearance)
            Debug.Log(clearanceDebug);

        Vector3 spinRadPerSecond = shot.spinRadPerSecond;
        if (spinRadPerSecond.sqrMagnitude < 0.0001f && Mathf.Abs(shot.appliedSpinRadPerSecond) > 0.001f)
        {
            Vector3 spinAxis = Vector3.Cross(Vector3.up, lateralDir).normalized;
            spinRadPerSecond = spinAxis * shot.appliedSpinRadPerSecond;
        }

        if (bc != null)
            bc.SetSpin(spinRadPerSecond);

        if (debugLogs)
        {
            Debug.Log($"[PHC] ShotType={baseType}, SolverShotType={solverBaseType}, Modifier={modifier}, SolverModifier={solverModifier}, DropProfile={useDropTrajectoryProfile}, SpinRPM={shot.appliedSpinRpm:F0}, SpinRad={shot.appliedSpinRadPerSecond:F2}, SpinVectorRad={spinRadPerSecond}");
            Debug.Log($"[PHC] Launched ball with velocity {rb.linearVelocity}");
        }

        // Ball controller callbacks
        if (bc != null)
        {
            bc.OnHit();
            MarkCurrentShotAccepted(bc);
            bc.StartCoroutine(bc.TemporaryExternalControl(0.08f));
        }

        // ------------------------------------------------------------
        // 5.x START HIT COOLDOWN
        // ------------------------------------------------------------
        StartCooldown(hitCooldown);

        return FinishHitAttempt(HitAttemptResult.Launched, contactConfirmation.swept
            ? "launched from swept authoritative contact"
            : "launched from authoritative contact", bc);
    }

    private const float MpsToMph = 2.23694f;
    private const int SolverSpeedStatsSize = 8;
    private static readonly int[] solverSpeedCounts = new int[SolverSpeedStatsSize];
    private static readonly float[] solverSpeedTargetSumMph = new float[SolverSpeedStatsSize];
    private static readonly float[] solverSpeedFinalSumMph = new float[SolverSpeedStatsSize];
    private static readonly float[] solverSpeedClearanceSumCm = new float[SolverSpeedStatsSize];
    private static readonly float[] solverSpeedActualClearanceSumCm = new float[SolverSpeedStatsSize];
    private static readonly float[] solverSpeedMinTargetMph = new float[SolverSpeedStatsSize];
    private static readonly float[] solverSpeedMaxTargetMph = new float[SolverSpeedStatsSize];
    private static int solverSpeedTotalSamples;

    private void RecordSolverSpeedStats(
        BaseShotType shotType,
        ShotComputationSolver.ShotResult shot,
        float desiredNetClearance,
        float solverNetClearanceCm,
        float actualNetClearanceCm,
        float solverMs)
    {
        if (!logSolverSpeedStats)
            return;

        int index = (int)shotType;
        if (index < 0 || index >= SolverSpeedStatsSize || !float.IsFinite(shot.targetV0))
            return;

        float targetMph = shot.targetV0 * MpsToMph;
        float finalMph = shot.finalV0 * MpsToMph;
        float clearanceCm = float.IsFinite(solverNetClearanceCm) ? solverNetClearanceCm : desiredNetClearance * 100f;
        float actualCm = float.IsFinite(actualNetClearanceCm) ? actualNetClearanceCm : 0f;

        int count = solverSpeedCounts[index];
        if (count == 0)
        {
            solverSpeedMinTargetMph[index] = targetMph;
            solverSpeedMaxTargetMph[index] = targetMph;
        }
        else
        {
            solverSpeedMinTargetMph[index] = Mathf.Min(solverSpeedMinTargetMph[index], targetMph);
            solverSpeedMaxTargetMph[index] = Mathf.Max(solverSpeedMaxTargetMph[index], targetMph);
        }

        solverSpeedCounts[index] = count + 1;
        solverSpeedTargetSumMph[index] += targetMph;
        solverSpeedFinalSumMph[index] += finalMph;
        solverSpeedClearanceSumCm[index] += clearanceCm;
        solverSpeedActualClearanceSumCm[index] += actualCm;
        solverSpeedTotalSamples++;

        if (logSolverSpeedSamples)
        {
            Debug.Log($"[SOLVER SPEED SAMPLE] type={shotType}, target={targetMph:F0}mph, final={finalMph:F0}mph, solverClear={clearanceCm:F0}cm, actualClear={actualNetClearanceCm:F0}cm, theta={shot.finalTheta * Mathf.Rad2Deg:F1}deg, candidates={shot.solverCandidateCount}, safetyLift={shot.solverSafetyLiftDeg:F1}deg, extension={shot.solverTargetExtensionM:F2}m, solverMs={solverMs:F1}, cache={(string.IsNullOrEmpty(shot.solverCacheSource) ? "none" : shot.solverCacheSource)}");
        }

        int reportEvery = Mathf.Max(1, solverSpeedStatsReportEvery);
        if (solverSpeedTotalSamples % reportEvery == 0)
            Debug.Log(BuildSolverSpeedStatsReport());
    }

    private static string BuildSolverSpeedStatsReport()
    {
        var sb = new System.Text.StringBuilder(512);
        sb.Append("[SOLVER SPEED STATS]");

        for (int i = 0; i < SolverSpeedStatsSize; i++)
        {
            int count = solverSpeedCounts[i];
            if (count <= 0)
                continue;

            float inv = 1f / count;
            sb.Append("\n  ");
            sb.Append(((BaseShotType)i).ToString());
            sb.Append(": n=");
            sb.Append(count);
            sb.Append(", targetAvg=");
            sb.Append((solverSpeedTargetSumMph[i] * inv).ToString("F0"));
            sb.Append("mph, targetRange=");
            sb.Append(solverSpeedMinTargetMph[i].ToString("F0"));
            sb.Append("-");
            sb.Append(solverSpeedMaxTargetMph[i].ToString("F0"));
            sb.Append("mph, finalAvg=");
            sb.Append((solverSpeedFinalSumMph[i] * inv).ToString("F0"));
            sb.Append("mph, solverClearAvg=");
            sb.Append((solverSpeedClearanceSumCm[i] * inv).ToString("F0"));
            sb.Append("cm, actualClearAvg=");
            sb.Append((solverSpeedActualClearanceSumCm[i] * inv).ToString("F0"));
            sb.Append("cm");
        }

        return sb.ToString();
    }
    private bool ShouldUseSliceRmbDropProfile(SwipeData swipe, BaseShotType baseType, Vector3 contactPosition)
    {
        if (!enableSliceRmbDropShotProfile || baseType != BaseShotType.Slice || !swipe.isRMB || reticle == null)
            return false;

        float contactSide = Mathf.Sign(contactPosition.x - netX);
        float targetSide = Mathf.Sign(reticle.position.x - netX);
        bool targetOnOtherSide = Mathf.Abs(contactSide) < 0.01f || Mathf.Abs(targetSide) < 0.01f || targetSide != contactSide;
        if (!targetOnOtherSide)
            return false;

        float targetPastNet = Mathf.Abs(reticle.position.x - netX);
        return targetPastNet <= Mathf.Max(0f, dropTargetMaxDistanceFromNet);
    }

    private float GetSolverNetClearanceCm(ShotComputationSolver.ShotResult shot, float desiredNetClearance)
    {
        float clearance = float.IsFinite(shot.solverIntendedNetClearance) && shot.solverIntendedNetClearance >= 0f
            ? shot.solverIntendedNetClearance
            : desiredNetClearance;

        return float.IsFinite(clearance) ? clearance * 100f : float.NaN;
    }

    private float EstimateActualNetClearanceCm(Vector3 startPosition, Vector3 launchVelocity, ShotComputationSolver.ShotResult shot)
    {
        Vector3 horizontalVelocity = launchVelocity;
        horizontalVelocity.y = 0f;
        float horizontalSpeed = horizontalVelocity.magnitude;
        if (horizontalSpeed <= 0.01f)
            return float.NaN;

        Vector3 horizontalDirection = horizontalVelocity / horizontalSpeed;
        if (Mathf.Abs(horizontalDirection.x) <= 0.0001f)
            return float.NaN;

        float distanceToNet = (netX - startPosition.x) / horizontalDirection.x;
        if (!float.IsFinite(distanceToNet) || distanceToNet < 0f)
            return float.NaN;

        float launchSpeed = launchVelocity.magnitude;
        float theta = Mathf.Atan2(launchVelocity.y, horizontalSpeed);
        float yAtNet = float.NaN;

        if (traj != null && launchSpeed > 0.01f)
        {
            Vector3 solverSpin = new Vector3(0f, 0f, -shot.appliedSpinRadPerSecond);
            yAtNet = traj.GetHeightAtX(new Vector2(0f, startPosition.y), launchSpeed, theta, distanceToNet, solverSpin);
        }

        if (!float.IsFinite(yAtNet) || yAtNet < -100f)
        {
            float timeToNet = distanceToNet / horizontalSpeed;
            yAtNet = startPosition.y + launchVelocity.y * timeToNet + 0.5f * Physics.gravity.y * timeToNet * timeToNet;
        }

        return (yAtNet - netHeight) * 100f;
    }

    private void LogActualTopspinShapeDiagnostic(
        BaseShotType baseType,
        BaseShotType solverBaseType,
        Vector3 startPosition,
        Vector3 launchVelocity,
        ShotComputationSolver.ShotResult shot,
        float desiredNetClearance,
        float solverNetClearanceCm,
        float actualNetClearanceCm,
        float launchVx,
        float launchVy)
    {
        if (baseType != BaseShotType.Topspin && solverBaseType != BaseShotType.Topspin)
            return;

        Vector3 horizontalVelocity = launchVelocity;
        horizontalVelocity.y = 0f;
        float horizontalSpeed = horizontalVelocity.magnitude;
        if (horizontalSpeed <= 0.01f)
            return;

        Vector3 horizontalDirection = horizontalVelocity / horizontalSpeed;
        if (Mathf.Abs(horizontalDirection.x) <= 0.0001f)
            return;

        float distanceToNet = (netX - startPosition.x) / horizontalDirection.x;
        if (!float.IsFinite(distanceToNet) || distanceToNet < 0f)
            return;

        float launchSpeed = launchVelocity.magnitude;
        float launchTheta = Mathf.Atan2(launchVelocity.y, horizontalSpeed);
        float noSpinClearanceCm = float.NaN;
        float withSpinClearanceCm = float.NaN;
        if (traj != null && launchSpeed > 0.01f)
        {
            Vector2 startPos2D = new Vector2(0f, startPosition.y);
            Vector3 solverSpin = new Vector3(0f, 0f, -shot.appliedSpinRadPerSecond);
            float yNoSpin = traj.GetHeightAtX(startPos2D, launchSpeed, launchTheta, distanceToNet, Vector3.zero);
            float yWithSpin = traj.GetHeightAtX(startPos2D, launchSpeed, launchTheta, distanceToNet, solverSpin);
            if (float.IsFinite(yNoSpin) && yNoSpin > -100f)
                noSpinClearanceCm = (yNoSpin - netHeight) * 100f;
            if (float.IsFinite(yWithSpin) && yWithSpin > -100f)
                withSpinClearanceCm = (yWithSpin - netHeight) * 100f;
        }

        float magnusCm = withSpinClearanceCm - noSpinClearanceCm;
        Debug.Log(
            $"[TOPSPIN ACTUAL] startY={startPosition.y:F2}m, distNet={distanceToNet:F2}m, " +
            $"launchSpeed={launchSpeed:F2}m/s ({launchSpeed * 2.23694f:F0}mph), launchTheta={launchTheta * Mathf.Rad2Deg:F2}deg, solverTheta={shot.finalTheta * Mathf.Rad2Deg:F2}deg, " +
            $"vx={launchVx:F2}, vy={launchVy:F2}, spinRpm={shot.appliedSpinRpm:F0}, spinRad={shot.appliedSpinRadPerSecond:F2}, " +
            $"actualNoSpinClear={noSpinClearanceCm:F0}cm, actualWithSpinClear={withSpinClearanceCm:F0}cm, actualMagnus={magnusCm:F0}cm, actualUiClear={actualNetClearanceCm:F0}cm, " +
            $"solverClearance={solverNetClearanceCm:F0}cm, desiredClearance={desiredNetClearance * 100f:F0}cm, correctedClearance={shot.correctedNetClearance * 100f:F0}cm, commonClearance={shot.commonErrorNetClearance * 100f:F0}cm, " +
            $"target={shot.targetV0:F2}m/s ({shot.targetV0 * 2.23694f:F0}mph), final={shot.finalV0:F2}m/s ({shot.finalV0 * 2.23694f:F0}mph), manual={shot.manualV0:F2}m/s ({shot.manualV0 * 2.23694f:F0}mph), shortfall={(shot.targetV0 - shot.finalV0):F2}m/s"
        );
    }
    private float ComputeDropProfileNetClearance(float contactDistanceFromNet, out string debugText)
    {
        Vector2 range = GetDropProfileClearanceRange(contactDistanceFromNet);
        float min = Mathf.Max(0f, Mathf.Min(range.x, range.y));
        float max = Mathf.Max(min, Mathf.Max(range.x, range.y));
        float clearance = UnityEngine.Random.Range(min, max);
        debugText = $"[DROP SHOT CLEARANCE] contactDistFromNet={contactDistanceFromNet:F2}m, range={min:F2}..{max:F2}m, clearance={clearance:F2}m";
        return clearance;
    }

    private Vector2 GetDropProfileClearanceRange(float contactDistanceFromNet)
    {
        if (contactDistanceFromNet >= Mathf.Max(0f, dropContactBaselineDistanceFromNet))
            return dropProfileClearanceBaseline;

        if (contactDistanceFromNet >= Mathf.Max(0f, dropContactMidCourtDistanceFromNet))
            return dropProfileClearanceMidCourt;

        return dropProfileClearanceFrontCourt;
    }
    //
    // Situation-dependent default net clearance.
    private float ComputeDesiredNetClearance(
        BaseShotType shotType,
        float contactHeight,
        Vector3 contactPosition,
        float estimatedLaunchSpeed,
        Vector3 incomingVelocity,
        BallController ballController,
        float holdScale,
        float forwardSwingProgress,
        float heightIntent,
        float backswingCapSpeed,
        out string debugText)
    {
        float fallbackClearance = solverComponent != null
            ? solverComponent.netMargin
            : netMargin;

        MatchServicePointController matchService = MatchServicePointController.Active;
        if (matchService != null && matchService.IsCurrentServerController(this))
        {
            debugText = $"[SERVICE CLEARANCE] clearance={matchService.serviceNetClearance:F2}m";
            return Mathf.Max(0.05f, matchService.serviceNetClearance);
        }

        if (!useSituationDefaultNetClearance)
        {
            debugText = $"[SHOT CLEARANCE] disabled clearance={fallbackClearance:F2}";
            return Mathf.Max(0f, fallbackClearance);
        }

        ShotClearanceProfile profile = GetClearanceProfile(shotType);

        if (useRiskScoreNetClearance && UsesRiskScoreClearance(shotType))
        {
            float normalClearance;
            float safetyClearance;
            GetRiskClearanceRange(shotType, out normalClearance, out safetyClearance);

            float defaultAngleDeg = BaseShotLibrary.GetFallbackDefaultHeightAngleDeg(shotType);
            float requestedAngleDeg = BaseShotLibrary.GetHeightAngleDeg(shotType, heightIntent, defaultAngleDeg);
            bool highCustomAngle = requestedAngleDeg >= defaultAngleDeg + Mathf.Max(0f, highCustomAngleRiskBypassDeg);

            float lowContactRisk = 1f - Mathf.InverseLerp(lowContactHeight, highContactHeight, contactHeight);
            float riskIncomingSpeed = incomingVelocity.magnitude;
            float speedRisk = Mathf.InverseLerp(15f, 35f, riskIncomingSpeed);
            float heightRisk = Mathf.Clamp01(lowContactRisk);
            float spinRisk = ComputeIncomingSpinRisk(ballController);
            float controlRisk = Mathf.Clamp01(holdScale);
            float swingRisk = Mathf.Clamp01(1f - forwardSwingProgress);

            float rawRisk =
                Mathf.Clamp01(speedRisk) * 0.30f +
                heightRisk * 0.25f +
                spinRisk * 0.20f +
                controlRisk * 0.15f +
                swingRisk * 0.10f;

            float riskScore = highCustomAngle ? 0f : Mathf.Clamp01(rawRisk);
            float riskClearance = Mathf.Lerp(normalClearance, safetyClearance, riskScore);
            float backswingCapMph = backswingCapSpeed * 2.23694f;
            float powerT = matchClearanceToBackswingCap
                ? Mathf.InverseLerp(clearanceLowPowerMph, clearanceHighPowerMph, backswingCapMph)
                : 1f;
            float lowPowerClearance = GetLowPowerRiskClearance(shotType, normalClearance, safetyClearance);
            float powerClearance = highCustomAngle
                ? normalClearance
                : Mathf.Lerp(lowPowerClearance, normalClearance, Mathf.Clamp01(powerT));
            riskClearance = Mathf.Max(riskClearance, powerClearance);
            riskClearance = Mathf.Min(riskClearance, safetyClearance);

            float topspinMagnusRise = ComputeTopspinMagnusNetRiseCompensation(
                shotType,
                contactPosition,
                requestedAngleDeg,
                estimatedLaunchSpeed,
                currentSpinIntent);
            if (topspinMagnusRise > 0f)
            {
                float compensationFloor = Mathf.Max(Mathf.Max(0.05f, topspinMinCompensatedClearance), powerClearance);
                riskClearance = Mathf.Max(compensationFloor, riskClearance - topspinMagnusRise);
            }

            debugText =
                $"[SHOT CLEARANCE] model=risk-score, type={shotType}, clearance={riskClearance:F2}m, " +
                $"normal={normalClearance:F2}, safety={safetyClearance:F2}, risk={riskScore:F2}, rawRisk={rawRisk:F2}, " +
                $"backswingCapMph={backswingCapMph:F1}, powerT={powerT:F2}, lowPowerClearance={lowPowerClearance:F2}, powerClearance={powerClearance:F2}, " +
                $"speedRisk={speedRisk:F2}, heightRisk={heightRisk:F2}, spinRisk={spinRisk:F2}, " +
                $"controlRisk={controlRisk:F2}, swingRisk={swingRisk:F2}, magnusRise={topspinMagnusRise:F2}, " +
                $"contactY={contactHeight:F2}, incomingSpeed={riskIncomingSpeed:F2}, " +
                $"heightIntent={heightIntent:F2}, requestedAngle={requestedAngleDeg:F2}, defaultAngle={defaultAngleDeg:F2}, " +
                $"highCustomAngle={highCustomAngle}";

            return riskClearance;
        }

        float lowContactT = 1f - Mathf.InverseLerp(lowContactHeight, highContactHeight, contactHeight);
        float incomingSpeed = incomingVelocity.magnitude;
        float incomingSpeedT = Mathf.InverseLerp(slowIncomingSpeed, fastIncomingSpeed, incomingSpeed);
        IncomingSpinClass incomingSpin = ClassifyIncomingSpin(incomingVelocity, ballController);

        float spinBonus = 0f;
        if (incomingSpin == IncomingSpinClass.Topspin)
            spinBonus = profile.incomingTopspinBonus;
        else if (incomingSpin == IncomingSpinClass.Slice)
            spinBonus = profile.incomingSliceBonus;

        float clearance =
            profile.baseClearance +
            profile.lowContactBonus * Mathf.Clamp01(lowContactT) +
            profile.fastIncomingBonus * Mathf.Clamp01(incomingSpeedT) +
            spinBonus;

        float longSliceBonus = ComputeLongSliceClearanceBonus(shotType);
        clearance += longSliceBonus;

        float minClearance = Mathf.Max(0f, profile.minClearance);
        float maxClearance = Mathf.Max(minClearance, profile.maxClearance);
        if (shotType == BaseShotType.Slice && longSliceBonus > 0f)
            maxClearance = Mathf.Max(maxClearance, longSliceMaxNetClearance);
        clearance = Mathf.Clamp(clearance, minClearance, maxClearance);

        debugText =
            $"[SHOT CLEARANCE] type={shotType}, clearance={clearance:F2}m, " +
            $"contactY={contactHeight:F2}, lowT={lowContactT:F2}, " +
            $"incomingSpeed={incomingSpeed:F2}, fastT={incomingSpeedT:F2}, " +
            $"holdForCorrection={holdScale:F2}, incomingSpin={incomingSpin}, spinBonus={spinBonus:F2}, longSliceBonus={longSliceBonus:F2}";

        return clearance;
    }

    private bool UsesRiskScoreClearance(BaseShotType shotType)
    {
        return shotType == BaseShotType.Flat ||
               shotType == BaseShotType.Topspin ||
               shotType == BaseShotType.Slice;
    }

    private float ApplyOneShotSafetyClearanceFloor(
        BaseShotType shotType,
        float desiredClearance,
        float safetyBonus,
        ref string debugText)
    {
        if (!useRiskScoreNetClearance || !UsesRiskScoreClearance(shotType))
            return desiredClearance;

        GetRiskClearanceRange(shotType, out float normalClearance, out float safetyClearance);
        float requestedFloor = Mathf.Min(
            safetyClearance,
            normalClearance + Mathf.Max(0f, safetyBonus));
        float adjusted = Mathf.Max(desiredClearance, requestedFloor);
        debugText += $", oneShotSafetyFloor={requestedFloor:F2}m, oneShotAdjusted={adjusted:F2}m";
        return adjusted;
    }

    private static float ApplyOneShotIntendedNetClearanceFloor(
        float desiredClearance,
        float requestedFloor,
        ref string debugText)
    {
        float adjusted = Mathf.Max(Mathf.Max(0f, desiredClearance), Mathf.Max(0f, requestedFloor));
        debugText += $", oneShotIntendedFloor={requestedFloor:F2}m, oneShotAdjusted={adjusted:F2}m";
        return adjusted;
    }

    private void GetRiskClearanceRange(BaseShotType shotType, out float normalClearance, out float safetyClearance)
    {
        switch (shotType)
        {
            case BaseShotType.Topspin:
            {
                float heavyT = Mathf.InverseLerp(Mathf.Clamp01(topspinHeavySpinStart), 1f, Mathf.Clamp01(currentSpinIntent));
                normalClearance = Mathf.Lerp(topspinNormalClearance, topspinHeavyNormalClearance, heavyT);
                safetyClearance = Mathf.Lerp(topspinSafetyClearance, topspinHeavySafetyClearance, heavyT);
                normalClearance = Mathf.Max(0.05f, normalClearance);
                safetyClearance = Mathf.Max(normalClearance, safetyClearance);
                break;
            }
            case BaseShotType.Slice:
                normalClearance = Mathf.Max(0.05f, sliceNormalClearance);
                safetyClearance = Mathf.Max(normalClearance, sliceSafetyClearance);
                break;
            case BaseShotType.Flat:
            default:
                normalClearance = Mathf.Max(0.05f, flatNormalClearance);
                safetyClearance = Mathf.Max(normalClearance, flatSafetyClearance);
                break;
        }
    }

    private float GetLowPowerRiskClearance(BaseShotType shotType, float normalClearance, float safetyClearance)
    {
        float lowPowerClearance;
        switch (shotType)
        {
            case BaseShotType.Topspin:
            {
                float heavyT = Mathf.InverseLerp(Mathf.Clamp01(topspinHeavySpinStart), 1f, Mathf.Clamp01(currentSpinIntent));
                lowPowerClearance = Mathf.Lerp(topspinLowPowerClearance, topspinHeavyLowPowerClearance, heavyT);
                break;
            }
            case BaseShotType.Slice:
                lowPowerClearance = sliceLowPowerClearance;
                break;
            case BaseShotType.Flat:
            default:
                lowPowerClearance = flatLowPowerClearance;
                break;
        }

        return Mathf.Clamp(Mathf.Max(normalClearance, lowPowerClearance), normalClearance, safetyClearance);
    }

    private float ComputeTopspinMagnusNetRiseCompensation(
        BaseShotType shotType,
        Vector3 contactPosition,
        float requestedAngleDeg,
        float estimatedLaunchSpeed,
        float spinIntent)
    {
        if (!compensateTopspinMagnusNetRise || shotType != BaseShotType.Topspin || traj == null)
            return 0f;

        float distanceToNet = Mathf.Abs(netX - contactPosition.x);
        if (!float.IsFinite(distanceToNet) || distanceToNet <= 0.05f)
            return 0f;

        float v0 = Mathf.Clamp(
            Mathf.Max(estimatedLaunchSpeed, BaseShotLibrary.BaselineNoBackswingSpeedMps),
            8f,
            BaseShotLibrary.RallyMaxSpeedMps);
        float theta = requestedAngleDeg * Mathf.Deg2Rad;
        float spinRpm = BaseShotLibrary.GetSpinRpm(BaseShotType.Topspin, spinIntent);
        float spinRad = BaseShotLibrary.RpmToRadPerSecond(spinRpm);
        Vector3 solverSpin = new Vector3(0f, 0f, -spinRad);
        Vector2 startPos = new Vector2(0f, contactPosition.y);

        float yWithSpin = traj.GetHeightAtX(startPos, v0, theta, distanceToNet, solverSpin);
        float yNoSpin = traj.GetHeightAtX(startPos, v0, theta, distanceToNet, Vector3.zero);
        if (!float.IsFinite(yWithSpin) || !float.IsFinite(yNoSpin) || yWithSpin <= -100f || yNoSpin <= -100f)
            return 0f;

        float magnusRise = Mathf.Max(0f, yWithSpin - yNoSpin);
        float scaledRise = magnusRise * Mathf.Max(0f, topspinMagnusCompensationScale);
        return Mathf.Clamp(scaledRise, 0f, Mathf.Max(0f, topspinMaxMagnusClearanceCompensation));
    }

    private float ComputeDeepVolleyDifficulty(BallController ballController, Vector3 contactPosition, Vector3 incomingVelocity)
    {
        if (!applyVolleyDifficultyModel || ballController == null)
            return 0f;

        if (ballController.CourtBouncesSinceLastHit > 0)
            return 0f;

        float depthFromNet = Mathf.Abs(contactPosition.x - netX);
        float deepStart = Mathf.Max(0f, volleyDeepDistanceFromNet);
        if (depthFromNet < deepStart)
            return 0f;

        float speedRisk = Mathf.InverseLerp(14f, Mathf.Max(14.1f, volleyFastIncomingSpeed), incomingVelocity.magnitude);
        float depthRisk = Mathf.InverseLerp(deepStart, deepStart + 4f, depthFromNet);
        float heightRisk = Mathf.Clamp01(Mathf.Abs(contactPosition.y - 0.95f) / 0.75f);

        return Mathf.Clamp01(speedRisk * 0.45f + depthRisk * 0.35f + heightRisk * 0.20f);
    }

    private static float ComputeSignedVolleyError(Vector3 contactPosition, Vector3 incomingVelocity, float salt)
    {
        float seed = contactPosition.x * 12.9898f
            + contactPosition.y * 37.719f
            + contactPosition.z * 78.233f
            + incomingVelocity.x * 5.137f
            + incomingVelocity.z * 9.271f
            + salt;
        float value = Mathf.Sin(seed) * 43758.5453f;
        return Mathf.Repeat(value, 1f) < 0.5f ? -1f : 1f;
    }
    private float ComputeIncomingSpinRisk(BallController ballController)
    {
        if (ballController == null)
            return 0f;

        float spinRpm = BaseShotLibrary.RadPerSecondToRpm(ballController.spinMagnitudeRadPerSecond);
        return Mathf.Clamp01(Mathf.Abs(spinRpm) / 3000f);
    }

    private float ComputeLongSliceClearanceBonus(BaseShotType shotType)
    {
        if (shotType != BaseShotType.Slice || reticle == null)
            return 0f;

        float start = Mathf.Max(0f, longSliceMinTargetDistanceFromNet);
        float end = Mathf.Max(start + 0.01f, longSliceFullBonusTargetDistanceFromNet);
        float targetDistanceFromNet = Mathf.Abs(reticle.position.x - netX);
        float distanceT = Mathf.InverseLerp(start, end, targetDistanceFromNet);
        if (distanceT <= 0f)
            return 0f;

        float spinT = Mathf.Lerp(0.75f, 1f, Mathf.Clamp01(currentSpinIntent));
        return Mathf.Max(0f, longSliceExtraNetClearance) * Mathf.Clamp01(distanceT) * spinT;
    }

    private ShotClearanceProfile GetClearanceProfile(BaseShotType shotType)
    {
        switch (shotType)
        {
            case BaseShotType.Topspin:
                return topspinClearance;
            case BaseShotType.Slice:
                return sliceClearance;
            case BaseShotType.Lob:
                return lobClearance;
            case BaseShotType.Drop:
                return dropClearance;
            case BaseShotType.Flat:
            default:
                return flatClearance;
        }
    }

    private enum IncomingSpinClass
    {
        Flat,
        Topspin,
        Slice
    }

    private IncomingSpinClass ClassifyIncomingSpin(Vector3 incomingVelocity, BallController ballController)
    {
        if (ballController == null || ballController.spinMagnitudeRadPerSecond <= 1f)
            return IncomingSpinClass.Flat;

        Vector3 horizontalVelocity = incomingVelocity;
        horizontalVelocity.y = 0f;
        if (horizontalVelocity.sqrMagnitude < 0.001f || ballController.spinRadPerSecond.sqrMagnitude < 1f)
            return IncomingSpinClass.Flat;

        Vector3 incomingDir = horizontalVelocity.normalized;
        Vector3 topspinAxis = Vector3.Cross(Vector3.up, incomingDir).normalized;
        float spinDot = Vector3.Dot(ballController.spinRadPerSecond.normalized, topspinAxis);

        if (spinDot > 0.25f)
            return IncomingSpinClass.Topspin;

        if (spinDot < -0.25f)
            return IncomingSpinClass.Slice;

        return IncomingSpinClass.Flat;
    }

    //
    // Ball height Contact Helper
    private Vector3 GetDynamicContactPoint()
    {
        // If ball is in the hitting zone, use its actual position
        if (ballIsInHittingZone && ball != null)
            return ball.transform.position;

        // Otherwise fall back to the fixed contactPoint transform
        if (contactPoint != null)
            return contactPoint.position;

        // Absolute fallback
        return ball != null ? ball.transform.position : transform.position;
    }


    // -----------------------------
    // Cooldown management (class scope)
    // -----------------------------
    private Coroutine cooldownRoutine = null;

    private void StartCooldown(float seconds)
    {
        if (cooldownRoutine != null)
        {
            StopCoroutine(cooldownRoutine);
            cooldownRoutine = null;
        }

        coolingDown = true;
        if (debugLogs) Debug.Log($"[PHC] StartCooldown called for {seconds:F3}s at time={Time.time:F3}");
        cooldownRoutine = StartCoroutine(HitCooldownCoroutine(seconds));
    }

    private IEnumerator HitCooldownCoroutine(float seconds)
    {
        if (debugLogs) Debug.Log($"[PHC] HitCooldownCoroutine started for {seconds:F3}s at time={Time.time:F3}");
        try
        {
            yield return new WaitForSeconds(seconds);
        }
        finally
        {
            coolingDown = false;
            cooldownRoutine = null;
            if (debugLogs) Debug.Log($"[PHC] HitCooldownCoroutine ended; coolingDown set false at time={Time.time:F3}");
        }
    }

    // -----------------------------
    // Instrumentation helpers
    // -----------------------------
    private IEnumerator LogRigidbodyAfterPhysics(Rigidbody rb)
    {
        yield return new WaitForFixedUpdate();

        if (rb != null)
            Debug.Log($"[PHC] After FixedUpdate rb.id={rb.GetInstanceID()} velocity={rb.linearVelocity} position={rb.position}");
    }

    private IEnumerator CheckForImmediateZero(Rigidbody rb, float delaySeconds)
    {
        yield return new WaitForSeconds(delaySeconds);

        if (rb == null) yield break;

        if (rb.linearVelocity.magnitude < 0.001f)
        {
            Debug.LogWarning($"[PHC] Near-zero velocity detected after hit.");
            Debug.Log($"[PHC] rb.id={rb.GetInstanceID()} velocity={rb.linearVelocity} position={rb.position}");
        }
    }

    private IEnumerator LogVelocityNextFixedUpdate(Rigidbody rb)
    {
        yield return new WaitForFixedUpdate();
        if (rb != null)
        {
            Debug.Log($"[PHC] Next FixedUpdate: rb.velocity={rb.linearVelocity} rb.position={rb.position} isKinematic={rb.isKinematic} drag={rb.linearDamping}");
        }
        else
        {
            Debug.Log("[PHC] Next FixedUpdate: rb is null");
        }
    }

    // -----------------------------
    // Optional collision helper
    // -----------------------------
    private IEnumerator TemporarilyIgnoreCollisionWithPlayer(GameObject ballObj, GameObject playerObj, float duration)
    {
        if (ballObj == null || playerObj == null) yield break;
        Collider ballCol = ballObj.GetComponent<Collider>();
        Collider[] playerCols = playerObj.GetComponentsInChildren<Collider>();
        if (ballCol == null || playerCols == null || playerCols.Length == 0) yield break;

        foreach (var pc in playerCols) Physics.IgnoreCollision(ballCol, pc, true);
        yield return new WaitForSeconds(duration);
        foreach (var pc in playerCols) Physics.IgnoreCollision(ballCol, pc, false);
    }
    
}
