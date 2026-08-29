using System.Collections;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif


[RequireComponent(typeof(Rigidbody))]
public class BallController : MonoBehaviour
{
    public static event System.Action<Rigidbody, string, Vector3, Vector3, Vector3> CourtBounceApplied;
    public static event System.Action<Rigidbody, string, Vector3, Vector3> CollisionReported;
    public static event System.Action<Rigidbody, GameObject, Vector3, Vector3> CollisionObjectReported;

    public enum CourtSurfacePreset
    {
        Grass,
        Hard,
        Clay,
        Custom
    }

    // ----------------------------------------------------------------------
    // OVERVIEW
    // ----------------------------------------------------------------------
    // This BallController is now a lightweight physics companion for the ball.
    // HitController is responsible for computing the true launch velocity,
    // including direction, speed, angle, shot quality, and depth logic.
    //
    // BallController no longer applies any forces or modifies velocity
    // EXCEPT for applying the same aerodynamic drag model used by the solver.
    //
    // Instead, it:
    //   • Prepares the Rigidbody for high-quality physics during the first frame
    //   • Applies custom drag each physics step (matching DragTrajectorySolver)
    //   • Logs velocity over time for debugging and tuning
    //   • Reports collisions for analysis (court, net, player, etc.)
    //   • Tracks the last velocity for internal comparisons
    //
    // This separation of responsibilities keeps the shot model deterministic,
    // expressive, and easy to tune — while BallController remains clean and stable.
    // ----------------------------------------------------------------------

    [Header("Physics (matches solver drag)")]
    private ShotSolverComponent solverComponent;

    // Provides access to traj.phys.k and traj.phys.gravity so the real ball
    // uses the exact same drag + gravity model as the RK4 solver.
    // Spin values. Rigidbody.angularVelocity and spinRadPerSecond are radians/second.
    public Vector3 spinRadPerSecond = Vector3.zero;
    public float spinMagnitudeRadPerSecond = 0f;
    public float spinRpm = 0f;

    // Legacy aliases kept for other scripts/debug inspectors.
    public Vector3 spinVector = Vector3.zero;
    public float spinMagnitude = 0f;

    private Vector3 pendingSpinRadPerSecond;
    private bool hasPendingSpin;

    //Spin Rate logging
    private float spinLogInterval = 0.1f;
    private float spinLogDuration = 3f;
    private Coroutine spinLogRoutine;

    
    
    [Header("Custom Court Bounce")]
    public bool useCustomCourtBounce = true;
    public bool overrideCourtPhysicsMaterial = true;
    public string courtNameContains = "court";
    public string excludedCourtNameContains = "aimplane";
    public bool allowUpwardLowSurfaceAsCourt = true;
    [Range(0.3f, 0.9f)] public float normalRestitution = 0.66f;
    [Range(0f, 1.5f)] public float courtFriction = 0.65f;
    [Range(0f, 0.6f)] public float tangentialRestitution = 0.12f;
    [Range(0.4f, 1f)] public float spinRetentionAfterBounce = 0.82f;
    [Range(0.35f, 0.8f)] public float bounceInertiaFactor = 0.58f;
    public float bounceBallRadius = 0.033f;
    public float minCustomBounceNormalSpeed = 0.75f;
    public float minCourtNormalY = 0.65f;
    public float maxCourtContactY = 0.35f;
    public float customBounceCooldown = 0.035f;
    public float maxPostBounceSpinRpm = 5000f;

    [Header("Custom Bounce Speed Guides")]
    public bool constrainHorizontalBounceRatio = true;
    public bool allowBackspinReverseBounce = false;
    public Vector2 flatHorizontalRatioRange = new Vector2(0.50f, 0.75f);
    public Vector2 topspinHorizontalRatioRange = new Vector2(0.70f, 1.02f);
    public Vector2 heavyTopspinHorizontalRatioRange = new Vector2(0.85f, 1.08f);
    public Vector2 backspinHorizontalRatioRange = new Vector2(0.30f, 0.65f);
    public float heavyTopspinThresholdRpm = 2500f;
    public float spinClassThresholdRpm = 250f;

    [Header("Custom Bounce Spin Guides")]
    public CourtSurfacePreset courtSurfacePreset = CourtSurfacePreset.Hard;
    public Vector2 grassFlatSpinEfficiency = new Vector2(0.15f, 0.25f);
    public Vector2 hardFlatSpinEfficiency = new Vector2(0.25f, 0.35f);
    public Vector2 clayFlatSpinEfficiency = new Vector2(0.35f, 0.45f);
    public Vector2 customFlatSpinEfficiency = new Vector2(0.25f, 0.35f);
    public bool limitGeneratedFlatBounceSpin = true;
    public float maxGeneratedFlatBounceSpinRpm = 1400f;
    public float maxBackspinReversalTopspinRpm = 800f;
    public float maxSlipSpeedForSpinEfficiency = 10f;
    public bool restartSpinLogAfterCustomBounce = true;
    public bool logCustomBounce = false;

    [Header("Bounce Apex Debug")]
    public bool logFirstBounceApex = false;
    public bool logEveryBounceApex = false;
    public float bounceApexMinTrackingSeconds = 0.02f;
    public float bounceApexMaxTrackingSeconds = 3f;

    private bool waitingForFirstBounceApex = true;
    private bool trackingBounceApex = false;
    private float bounceApexStartTime;
    private float bounceApexContactY;
    private float bounceApexY;
    private Vector3 bounceApexVelocityOut;
    private string bounceApexCourtName;
    private float bounceApexSpinBeforeRpm;
    private float bounceApexSpinAfterRpm;
    private int bounceApexSequence = 0;
    [Header("Spin Debug Visual")]
    public bool showSpinDebug = false;
    public bool showSpinDebugOverlay = false;
    [Range(250f, 6000f)] public float spinDebugFullScaleRpm = 2500f;
    public float spinDebugRingRadius = 0.12f;
    public float spinDebugLineWidth = 0.012f;
    private SpinDebugVisualizer spinDebugVisualizer;

    [Header("Visual Spin")]
    public bool raiseRigidbodyAngularVelocityCap = true;
    public float maxVisualSpinRpm = 6000f;

    [Header("Debug")]
    public bool debugLogs = false;
    public bool keepInterpolationEnabled = true;
    public bool keepContinuousCollisionEnabled = true;

    [Header("Ball Physics Quality")]
    public int solverIterations = 12;
    public int solverVelocityIterations = 4;
    public float maxDepenetrationVelocity = 4f;

    // Minimum velocity considered "moving" after a hit
    public float minObservedVelocity = 0.05f;

    // Timed velocity logging settings
    public float velocityLogInterval = 0.1f;
    public float velocityLogDuration = 3f;

    [Header("Shot Height Tracker")]
    public bool logShotHeightTracker = true;
    public float shotHeightLogInterval = 0.1f;
    public float shotHeightLogDuration = 2f;

    // Internal references and state
    private Rigidbody rb;
    private int rbInstanceId;
    private float lastHitTime;
    private Vector3 lastVel;
    private bool checkingVelocity = false;
    public int CourtBouncesSinceLastHit { get; private set; }
    public float LastCourtBounceTime { get; private set; } = -999f;
    public int ShotSequence { get; private set; }
    public int LastLaunchShotSequence { get; private set; } = -1;
    public Vector3 LastLaunchVelocity { get; private set; }
    public float LastLaunchSpeedMps { get; private set; }
    public float LastLaunchPlanarSpeedMps { get; private set; }
    public float LastLaunchTime { get; private set; } = -999f;

    // Timed velocity logging state

    
    // Pre-physics state used for custom court bounce. Collision callbacks run after
    // Unity has already resolved contacts, so bounce math must use this saved state.
    private Vector3 prePhysicsVelocity;
    private Vector3 prePhysicsPosition;
    private Vector3 prePhysicsSpinRadPerSecond;
    private float lastCustomBounceTime = -999f;

    // External control flag to prevent BallController from overriding velocity
    private bool externalControl = false;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();

        if (rb == null)
        {
            Debug.LogError("[BallController] No Rigidbody found. Disabling BallController.");
            enabled = false;
            return;
        }

        // NEW: Automatically find the solver in the scene, because the ball is a prefab
        solverComponent = FindFirstObjectByType<ShotSolverComponent>();
        if (solverComponent == null)
        {
            Debug.LogWarning("[BallController] No ShotSolverComponent found in scene. Custom drag will be disabled.");
        }

        rbInstanceId = rb.GetInstanceID();

#if UNITY_EDITOR
        try
        {
            var scriptName = UnityEditor.MonoScript.FromMonoBehaviour(this).GetClass().Name;
            if (debugLogs) Debug.Log($"[BallController] Awake INSTANCE ID = {gameObject.GetInstanceID()} script = {scriptName}");
        }
        catch { if (debugLogs) Debug.Log($"[BallController] Awake INSTANCE ID = {gameObject.GetInstanceID()}"); }
#else
    if (debugLogs) Debug.Log($"[BallController] Awake INSTANCE ID = {gameObject.GetInstanceID()}");
#endif

        if (debugLogs)
            Debug.Log($"[BallController] Awake rb.id={rbInstanceId} mass={rb.mass} drag={rb.linearDamping} isKinematic={rb.isKinematic}");

        ApplySmoothFlightSettings();
        ApplyBallSolverQuality();
        EnsureSpinDebugVisualizer();
    }

    void OnValidate()
    {
        if (!Application.isPlaying)
            return;

        EnsureSpinDebugVisualizer();
    }

    private void EnsureSpinDebugVisualizer()
    {
        if (!showSpinDebug)
        {
            if (spinDebugVisualizer != null)
                spinDebugVisualizer.SetVisible(false);
            return;
        }

        spinDebugVisualizer = GetComponent<SpinDebugVisualizer>();
        if (spinDebugVisualizer == null)
            spinDebugVisualizer = gameObject.AddComponent<SpinDebugVisualizer>();

        spinDebugVisualizer.Configure(
            this,
            showSpinDebugOverlay,
            spinDebugFullScaleRpm,
            spinDebugRingRadius,
            spinDebugLineWidth
        );
        spinDebugVisualizer.SetVisible(true);
    }


    // ----------------------------------------------------------------------
    // Public external-control API
    // ----------------------------------------------------------------------
    /// <summary>
    /// Enable or disable external control. When enabled, BallController will skip its
    /// normal physics-driven updates (useful immediately after hitController sets velocity).
    /// </summary>
    public void SetExternalControl(bool enabled)
    {
        externalControl = enabled;
        if (debugLogs) Debug.Log($"[BallController] SetExternalControl({enabled})");
    }

    /// <summary>
    /// Temporarily enable external control for the given duration (seconds).
    /// Use this to prevent BallController from clobbering velocity right after a hit.
    /// </summary>
    public IEnumerator TemporaryExternalControl(float duration)
    {
        externalControl = true;
        // DO NOT set isKinematic here

        yield return new WaitForSeconds(duration);

        externalControl = false;

        // Apply spin after external control ends.
        if (hasPendingSpin)
        {
            ApplySpinRadPerSecond(pendingSpinRadPerSecond);
            hasPendingSpin = false;

            if (spinLogRoutine != null)
                StopCoroutine(spinLogRoutine);

            spinLogRoutine = StartCoroutine(LogSpinOverTime());
        }
    }


    // ----------------------------------------------------------------------
    // OnHit — called by HitController after it sets rb.velocity
    // ----------------------------------------------------------------------
    /// <summary>
    /// Call this after hitController applies the launch velocity. BallController
    /// will boost physics quality for the first physics step, start timed logging,
    /// and check for near-zero velocity (overlap/clipping detection).
    /// </summary>
    public void OnHit()
    {
        if (rb == null) return;

        ShotSequence++;
        LastLaunchShotSequence = ShotSequence;
        LastLaunchVelocity = rb.linearVelocity;
        LastLaunchSpeedMps = LastLaunchVelocity.magnitude;
        Vector3 planarLaunchVelocity = LastLaunchVelocity;
        planarLaunchVelocity.y = 0f;
        LastLaunchPlanarSpeedMps = planarLaunchVelocity.magnitude;
        LastLaunchTime = Time.time;
        lastHitTime = Time.time;
        CourtBouncesSinceLastHit = 0;
        LastCourtBounceTime = -999f;
        ResetFirstBounceApexTracking();
        rb.WakeUp();

        // Save previous physics settings so we can restore them later
        float prevSleep = rb.sleepThreshold;
        CollisionDetectionMode prevCD = rb.collisionDetectionMode;

        // Boost physics quality for the first frame after impact
        rb.sleepThreshold = 0f;
        ApplySmoothFlightSettings();
        ApplyBallSolverQuality();

        if (debugLogs) Debug.Log($"[BallController] OnHit triggered. LaunchVelocity={rb.linearVelocity}");

        // Start timed velocity logging
        StartCoroutine(LogVelocityForSeconds());
        if (logShotHeightTracker)
            StartCoroutine(LogShotHeightForSeconds(ShotSequence));

        // Restore physics settings next physics step
        if (!checkingVelocity)
            StartCoroutine(CheckVelocityAfterPhysics(prevSleep, prevCD));
    }

    // ----------------------------------------------------------------------
    // CheckVelocityAfterPhysics
    // ----------------------------------------------------------------------
    private IEnumerator CheckVelocityAfterPhysics(float prevSleep, CollisionDetectionMode prevCD)
    {
        if (checkingVelocity) yield break;
        checkingVelocity = true;

        // Wait one physics step so we can inspect the velocity Unity actually applied
        yield return new WaitForFixedUpdate();

        if (rb == null)
        {
            checkingVelocity = false;
            yield break;
        }

        if (debugLogs)
            Debug.Log($"[BallController] After FixedUpdate rb.id={rbInstanceId} velocity={rb.linearVelocity} pos={rb.position}");

        if (rb.linearVelocity.magnitude < minObservedVelocity)
        {
            Debug.LogWarning($"[BallController] Low velocity after hit (|v|={rb.linearVelocity.magnitude:F3}). Likely collider interference or overlap.");
        }

        // Restore original physics settings
        rb.sleepThreshold = prevSleep;
        if (!keepContinuousCollisionEnabled)
            rb.collisionDetectionMode = prevCD;
        ApplySmoothFlightSettings();

        checkingVelocity = false;
    }

    private void ApplySmoothFlightSettings()
    {
        if (rb == null)
            return;

        if (keepInterpolationEnabled)
            rb.interpolation = RigidbodyInterpolation.Interpolate;

        if (keepContinuousCollisionEnabled)
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    private void ApplyBallSolverQuality()
    {
        if (rb == null)
            return;

        rb.solverIterations = Mathf.Max(1, solverIterations);
        rb.solverVelocityIterations = Mathf.Max(1, solverVelocityIterations);
        rb.maxDepenetrationVelocity = Mathf.Max(0.1f, maxDepenetrationVelocity);
        ApplyAngularVelocityCap();
    }

    private void ApplyAngularVelocityCap()
    {
        if (rb == null || !raiseRigidbodyAngularVelocityCap)
            return;

        float capRadPerSecond = BaseShotLibrary.RpmToRadPerSecond(Mathf.Max(1f, maxVisualSpinRpm));
        rb.maxAngularVelocity = Mathf.Max(rb.maxAngularVelocity, capRadPerSecond);
    }

    // ----------------------------------------------------------------------
    // Collision debugging + custom court bounce
    // ----------------------------------------------------------------------
    void OnCollisionEnter(Collision collision)
    {
        Vector3 contactPoint = collision != null && collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
        GameObject collisionObject = collision != null ? collision.gameObject : null;
        Vector3 relativeVelocity = collision != null ? collision.relativeVelocity : Vector3.zero;
        CollisionObjectReported?.Invoke(rb, collisionObject, contactPoint, relativeVelocity);
        CollisionReported?.Invoke(rb, collisionObject != null ? collisionObject.name : string.Empty, contactPoint, relativeVelocity);
        TryApplyCustomCourtBounce(collision);

        if (!debugLogs) return;

        if (Time.time - lastHitTime < 1.0f)
        {
            foreach (var cp in collision.contacts)
            {
                Debug.Log($"[BallController] Collision with {collision.gameObject.name} at {cp.point} normal={cp.normal} relVel={collision.relativeVelocity}");
            }
        }
    }

    private void TryApplyCustomCourtBounce(Collision collision)
    {
        if (!useCustomCourtBounce || rb == null || collision == null)
            return;

        if (Time.time - lastCustomBounceTime < customBounceCooldown)
            return;

        if (!TryGetCourtContact(collision, out ContactPoint contact))
            return;

        OverrideCourtPhysicsMaterial(collision.collider);

        Vector3 normal = contact.normal.sqrMagnitude > 0.0001f ? contact.normal.normalized : Vector3.up;
        Vector3 vIn = prePhysicsVelocity.sqrMagnitude > 0.0001f ? prePhysicsVelocity : rb.linearVelocity;
        Vector3 trajectoryBouncePosition = EstimateTrajectoryBouncePosition(contact.point, vIn);
        Vector3 spinIn = prePhysicsSpinRadPerSecond.sqrMagnitude > 0.0001f ? prePhysicsSpinRadPerSecond : spinRadPerSecond;

        float normalSpeedIn = Vector3.Dot(vIn, normal);
        if (normalSpeedIn >= -minCustomBounceNormalSpeed)
            return;

        float mass = Mathf.Max(0.001f, rb.mass);
        float radius = Mathf.Max(0.001f, bounceBallRadius);
        float inertia = Mathf.Max(0.000001f, bounceInertiaFactor * mass * radius * radius);

        Vector3 normalVelocityIn = normal * normalSpeedIn;
        Vector3 tangentVelocityIn = vIn - normalVelocityIn;
        Vector3 contactRadius = -normal * radius;
        Vector3 contactVelocity = tangentVelocityIn + Vector3.Cross(spinIn, contactRadius);

        float normalImpulseMagnitude = -(1f + Mathf.Clamp01(normalRestitution)) * mass * normalSpeedIn;
        Vector3 normalImpulse = normal * normalImpulseMagnitude;

        float tangentEffectiveMass = 1f / mass + (radius * radius) / inertia;
        Vector3 idealTangentImpulse = contactVelocity.sqrMagnitude > 0.000001f
            ? -(1f + Mathf.Max(0f, tangentialRestitution)) * contactVelocity / tangentEffectiveMass
            : Vector3.zero;

        float maxTangentImpulse = Mathf.Max(0f, courtFriction) * normalImpulseMagnitude;
        Vector3 tangentImpulse = Vector3.ClampMagnitude(idealTangentImpulse, maxTangentImpulse);
        float clamp01 = idealTangentImpulse.sqrMagnitude > 0.000001f
            ? Mathf.Clamp01(tangentImpulse.magnitude / idealTangentImpulse.magnitude)
            : 1f;

        Vector3 vOut = vIn + normalImpulse / mass + tangentImpulse / mass;
        Vector3 spinOutBeforeGuide = spinIn + Vector3.Cross(contactRadius, tangentImpulse) / inertia;
        spinOutBeforeGuide *= Mathf.Clamp01(spinRetentionAfterBounce);

        float signedSpinBeforeRpm = SignedSpinRpmForVelocity(spinIn, tangentVelocityIn, normal);
        float signedSpinAfterRpmBeforeClamp = SignedSpinRpmForVelocity(spinOutBeforeGuide, vOut - Vector3.Project(vOut, normal), normal);
        Vector3 vOutBeforeRatioClamp = vOut;
        vOut = ApplyHorizontalRatioGuide(vIn, vOut, normal, signedSpinBeforeRpm);

        Vector3 tangentOutAfterRatio = vOut - Vector3.Project(vOut, normal);
        bool spinLimited;
        float spinLimitRpm;
        float rollingEfficiency;
        Vector3 spinOut = ApplyBounceSpinGuides(
            spinIn,
            spinOutBeforeGuide,
            tangentVelocityIn,
            tangentOutAfterRatio,
            normal,
            radius,
            contactVelocity.magnitude,
            signedSpinBeforeRpm,
            out spinLimited,
            out spinLimitRpm,
            out rollingEfficiency
        );
        spinOut = ClampSpinMagnitude(spinOut, maxPostBounceSpinRpm);
        float signedSpinAfterRpm = SignedSpinRpmForVelocity(spinOut, tangentOutAfterRatio, normal);

        rb.linearVelocity = vOut;
        ApplySpinRadPerSecond(spinOut);
        if (restartSpinLogAfterCustomBounce)
            RestartSpinLog();
        lastCustomBounceTime = Time.time;
        CourtBouncesSinceLastHit++;
        LastCourtBounceTime = Time.time;
        prePhysicsVelocity = vOut;
        prePhysicsSpinRadPerSecond = spinOut;
        lastVel = vOut;

        BeginBounceApexTracking(
            collision.gameObject.name,
            contact.point.y,
            vOut,
            signedSpinBeforeRpm,
            signedSpinAfterRpm
        );

        // Unity can depenetrate the rigidbody a long distance on the combined court mesh.
        // Restore X/Z to the trajectory ground crossing before reporting the bounce.
        rb.position = new Vector3(trajectoryBouncePosition.x, contact.point.y + radius, trajectoryBouncePosition.z);
        CourtBounceApplied?.Invoke(rb, collision.gameObject.name, trajectoryBouncePosition, vIn, vOut);

        if (logCustomBounce)
        {
            Vector3 tangentOut = vOut - Vector3.Project(vOut, normal);
            float verticalRatio = Mathf.Abs(normalSpeedIn) > 0.001f
                ? Vector3.Dot(vOut, normal) / Mathf.Abs(normalSpeedIn)
                : 0f;
            float horizontalRatio = tangentVelocityIn.magnitude > 0.001f
                ? tangentOut.magnitude / tangentVelocityIn.magnitude
                : 0f;
            float unclampedHorizontalRatio = tangentVelocityIn.magnitude > 0.001f
                ? (vOutBeforeRatioClamp - Vector3.Project(vOutBeforeRatioClamp, normal)).magnitude / tangentVelocityIn.magnitude
                : 0f;

            Debug.Log(
                $"[BOUNCE] court={collision.gameObject.name}, vIn={vIn.magnitude:F2}, vOut={vOut.magnitude:F2}, " +
                $"vNRatio={verticalRatio:F2}, vTRatio={horizontalRatio:F2}, rawVTRatio={unclampedHorizontalRatio:F2}, " +
                $"spinRpm={signedSpinBeforeRpm:F0}->{signedSpinAfterRpm:F0}, rawSpinAfter={signedSpinAfterRpmBeforeClamp:F0}, " +
                $"spinLimit={(spinLimitRpm > 0f ? spinLimitRpm.ToString("F0") : "off")}, spinEff={rollingEfficiency:F2}, " +
                $"spinLimited={spinLimited}, surface={courtSurfacePreset}, " +
                $"slip={contactVelocity.magnitude:F2}, mu={courtFriction:F2}, tangentClamp={clamp01:F2}, " +
                $"normalImpulse={normalImpulseMagnitude:F3}, tangentImpulse={tangentImpulse.magnitude:F3}"
            );
            Debug.Log(
                $"[SPIN BOUNCE] before={signedSpinBeforeRpm:F0}rpm, rawAfter={signedSpinAfterRpmBeforeClamp:F0}rpm, " +
                $"final={signedSpinAfterRpm:F0}rpm, finalRad={spinOut.magnitude:F2}, limit={(spinLimitRpm > 0f ? spinLimitRpm.ToString("F0") : "off")}, " +
                $"limited={spinLimited}, surface={courtSurfacePreset}, court={collision.gameObject.name}"
            );
        }
    }

    // Planning counterpart to the live custom court bounce. This intentionally
    // uses the same impulse, friction, horizontal-ratio and spin-guide rules as
    // TryApplyCustomCourtBounce so AI trajectory previews do not switch to the
    // older fixed retention approximation at the first bounce.
    public bool TryPredictCustomCourtBounce(
        Vector3 velocityIn,
        Vector3 spinIn,
        out Vector3 velocityOut,
        out Vector3 spinOut)
    {
        velocityOut = velocityIn;
        spinOut = spinIn;
        if (!useCustomCourtBounce)
            return false;

        Vector3 normal = Vector3.up;
        float normalSpeedIn = Vector3.Dot(velocityIn, normal);
        if (normalSpeedIn >= -Mathf.Max(0f, minCustomBounceNormalSpeed))
            return false;

        float mass = Mathf.Max(0.001f, rb != null ? rb.mass : 1f);
        float radius = Mathf.Max(0.001f, bounceBallRadius);
        float inertia = Mathf.Max(0.000001f, bounceInertiaFactor * mass * radius * radius);

        Vector3 normalVelocityIn = normal * normalSpeedIn;
        Vector3 tangentVelocityIn = velocityIn - normalVelocityIn;
        Vector3 contactRadius = -normal * radius;
        Vector3 contactVelocity = tangentVelocityIn + Vector3.Cross(spinIn, contactRadius);

        float normalImpulseMagnitude = -(1f + Mathf.Clamp01(normalRestitution)) * mass * normalSpeedIn;
        Vector3 normalImpulse = normal * normalImpulseMagnitude;
        float tangentEffectiveMass = 1f / mass + (radius * radius) / inertia;
        Vector3 idealTangentImpulse = contactVelocity.sqrMagnitude > 0.000001f
            ? -(1f + Mathf.Max(0f, tangentialRestitution)) * contactVelocity / tangentEffectiveMass
            : Vector3.zero;
        float maxTangentImpulse = Mathf.Max(0f, courtFriction) * normalImpulseMagnitude;
        Vector3 tangentImpulse = Vector3.ClampMagnitude(idealTangentImpulse, maxTangentImpulse);

        velocityOut = velocityIn + normalImpulse / mass + tangentImpulse / mass;
        Vector3 spinOutBeforeGuide = spinIn + Vector3.Cross(contactRadius, tangentImpulse) / inertia;
        spinOutBeforeGuide *= Mathf.Clamp01(spinRetentionAfterBounce);

        float signedSpinBeforeRpm = SignedSpinRpmForVelocity(spinIn, tangentVelocityIn, normal);
        velocityOut = ApplyHorizontalRatioGuide(velocityIn, velocityOut, normal, signedSpinBeforeRpm);
        Vector3 tangentVelocityOut = velocityOut - Vector3.Project(velocityOut, normal);
        spinOut = ApplyBounceSpinGuides(
            spinIn,
            spinOutBeforeGuide,
            tangentVelocityIn,
            tangentVelocityOut,
            normal,
            radius,
            contactVelocity.magnitude,
            signedSpinBeforeRpm,
            out _,
            out _,
            out _);
        spinOut = ClampSpinMagnitude(spinOut, maxPostBounceSpinRpm);
        return true;
    }

    private Vector3 EstimateTrajectoryBouncePosition(Vector3 rawContactPoint, Vector3 velocityBeforePhysics)
    {
        float radius = Mathf.Max(0.001f, bounceBallRadius);
        float courtY = rawContactPoint.y;
        Vector3 start = prePhysicsPosition;
        Vector3 projected = start + velocityBeforePhysics * Time.fixedDeltaTime;
        float startBottom = start.y - radius;
        float projectedBottom = projected.y - radius;

        float denominator = projectedBottom - startBottom;
        float t = Mathf.Abs(denominator) > 0.00001f
            ? Mathf.Clamp01((courtY - startBottom) / denominator)
            : 0f;
        Vector3 crossing = Vector3.Lerp(start, projected, t);
        crossing.y = courtY;
        return crossing;
    }

    private void ResetFirstBounceApexTracking()
    {
        waitingForFirstBounceApex = logFirstBounceApex;
        trackingBounceApex = false;
        bounceApexY = float.NegativeInfinity;
    }

    private void BeginBounceApexTracking(
        string courtName,
        float contactY,
        Vector3 velocityOut,
        float spinBeforeRpm,
        float spinAfterRpm)
    {
        if (!logFirstBounceApex || rb == null)
            return;

        if (!waitingForFirstBounceApex && !logEveryBounceApex)
        {
            // During bounce tuning, the master logFirstBounceApex flag acts as the enable switch.
            // Keep logging later bounces even if older scene serialization left logEveryBounceApex false.
        }

        waitingForFirstBounceApex = false;
        trackingBounceApex = true;
        bounceApexStartTime = Time.time;
        bounceApexContactY = contactY;
        bounceApexY = Mathf.Max(rb.position.y, contactY + bounceBallRadius);
        bounceApexVelocityOut = velocityOut;
        bounceApexCourtName = string.IsNullOrEmpty(courtName) ? "court" : courtName;
        bounceApexSpinBeforeRpm = spinBeforeRpm;
        bounceApexSpinAfterRpm = spinAfterRpm;
        bounceApexSequence++;
    }

    private void UpdateBounceApexTracking()
    {
        if (!trackingBounceApex || rb == null)
            return;

        bounceApexY = Mathf.Max(bounceApexY, rb.position.y);

        float elapsed = Time.time - bounceApexStartTime;
        bool startedDescending = elapsed >= Mathf.Max(0f, bounceApexMinTrackingSeconds) && rb.linearVelocity.y <= 0f;
        bool timedOut = elapsed >= Mathf.Max(0.1f, bounceApexMaxTrackingSeconds);

        if (!startedDescending && !timedOut)
            return;

        float centerApexCm = Mathf.Max(0f, (bounceApexY - bounceApexContactY) * 100f);
        float bottomApexCm = Mathf.Max(0f, (bounceApexY - bounceApexContactY - bounceBallRadius) * 100f);

        Debug.Log(
            $"[BOUNCE APEX] seq={bounceApexSequence}, court={bounceApexCourtName}, " +
            $"centerApex={centerApexCm:F1}cm, bottomApex={bottomApexCm:F1}cm, " +
            $"contactY={bounceApexContactY:F3}, apexY={bounceApexY:F3}, " +
            $"vOut={bounceApexVelocityOut.magnitude:F2}m/s, vOutY={bounceApexVelocityOut.y:F2}, " +
            $"spinRpm={bounceApexSpinBeforeRpm:F0}->{bounceApexSpinAfterRpm:F0}, " +
            $"elapsed={elapsed:F3}s, timedOut={timedOut}"
        );

        trackingBounceApex = false;
    }
    private bool TryGetCourtContact(Collision collision, out ContactPoint bestContact)
    {
        bestContact = default;
        float bestY = minCourtNormalY;
        bool found = false;

        foreach (ContactPoint contact in collision.contacts)
        {
            if (contact.normal.y < bestY)
                continue;

            if (!IsCourtLikeCollision(collision, contact))
                continue;

            bestContact = contact;
            bestY = contact.normal.y;
            found = true;
        }

        return found;
    }

    private bool IsCourtLikeCollision(Collision collision, ContactPoint contact)
    {
        string objectName = collision.gameObject.name.ToLowerInvariant();
        string parentName = collision.transform != null && collision.transform.parent != null
            ? collision.transform.parent.name.ToLowerInvariant()
            : string.Empty;
        string excludedNeedle = string.IsNullOrWhiteSpace(excludedCourtNameContains)
            ? string.Empty
            : excludedCourtNameContains.ToLowerInvariant();
        if (!string.IsNullOrEmpty(excludedNeedle) && (objectName.Contains(excludedNeedle) || parentName.Contains(excludedNeedle)))
            return false;

        string needle = string.IsNullOrWhiteSpace(courtNameContains)
            ? string.Empty
            : courtNameContains.ToLowerInvariant();

        if (!string.IsNullOrEmpty(needle) && (objectName.Contains(needle) || parentName.Contains(needle)))
            return true;

        return allowUpwardLowSurfaceAsCourt && contact.normal.y >= minCourtNormalY && contact.point.y <= maxCourtContactY;
    }

    private void OverrideCourtPhysicsMaterial(Collider courtCollider)
    {
        if (!overrideCourtPhysicsMaterial || courtCollider == null)
            return;

        var material = courtCollider.material;
        if (material == null)
            return;

        material.bounciness = 0f;
        material.dynamicFriction = 0f;
        material.staticFriction = 0f;
    }

    private Vector3 ApplyHorizontalRatioGuide(Vector3 vIn, Vector3 vOut, Vector3 normal, float signedSpinBeforeRpm)
    {
        if (!constrainHorizontalBounceRatio)
            return vOut;

        Vector3 tangentIn = vIn - Vector3.Project(vIn, normal);
        float tangentInSpeed = tangentIn.magnitude;
        if (tangentInSpeed < 0.001f)
            return vOut;

        Vector3 tangentOut = vOut - Vector3.Project(vOut, normal);
        float tangentOutSpeed = tangentOut.magnitude;
        Vector2 ratioRange = GetHorizontalRatioRange(signedSpinBeforeRpm);
        float targetSpeed = Mathf.Clamp(tangentOutSpeed, ratioRange.x * tangentInSpeed, ratioRange.y * tangentInSpeed);

        Vector3 tangentDir = tangentOutSpeed > 0.001f ? tangentOut / tangentOutSpeed : tangentIn.normalized;
        if (!allowBackspinReverseBounce && Vector3.Dot(tangentDir, tangentIn.normalized) < 0f)
            tangentDir = tangentIn.normalized;

        return Vector3.Project(vOut, normal) + tangentDir * targetSpeed;
    }

    private Vector2 GetHorizontalRatioRange(float signedSpinRpm)
    {
        if (signedSpinRpm > spinClassThresholdRpm)
        {
            float heavyT = Mathf.InverseLerp(spinClassThresholdRpm, Mathf.Max(spinClassThresholdRpm + 1f, heavyTopspinThresholdRpm), signedSpinRpm);
            return new Vector2(
                Mathf.Lerp(topspinHorizontalRatioRange.x, heavyTopspinHorizontalRatioRange.x, heavyT),
                Mathf.Lerp(topspinHorizontalRatioRange.y, heavyTopspinHorizontalRatioRange.y, heavyT)
            );
        }

        if (signedSpinRpm < -spinClassThresholdRpm)
            return backspinHorizontalRatioRange;

        return flatHorizontalRatioRange;
    }

    private Vector3 ApplyBounceSpinGuides(
        Vector3 spinIn,
        Vector3 spinOut,
        Vector3 tangentVelocityIn,
        Vector3 tangentVelocityOut,
        Vector3 normal,
        float radius,
        float slipSpeed,
        float signedSpinBeforeRpm,
        out bool spinLimited,
        out float spinLimitRpm,
        out float rollingEfficiency)
    {
        spinLimited = false;
        spinLimitRpm = 0f;
        rollingEfficiency = 0f;

        if (!limitGeneratedFlatBounceSpin)
            return spinOut;

        Vector3 tangentReference = tangentVelocityOut.sqrMagnitude > 0.001f ? tangentVelocityOut : tangentVelocityIn;
        if (tangentReference.sqrMagnitude < 0.001f)
            return spinOut;

        Vector3 topspinAxis = Vector3.Cross(normal, tangentReference.normalized);
        if (topspinAxis.sqrMagnitude < 0.0001f)
            return spinOut;
        topspinAxis.Normalize();

        float signedSpinBeforeAbs = Mathf.Abs(signedSpinBeforeRpm);
        float signedSpinAfterRad = Vector3.Dot(spinOut, topspinAxis);
        float signedSpinAfterRpm = BaseShotLibrary.RadPerSecondToRpm(signedSpinAfterRad);

        if (signedSpinBeforeAbs <= spinClassThresholdRpm)
        {
            rollingEfficiency = GetFlatSpinRollingEfficiency(slipSpeed);
            float rollingRpm = TangentSpeedToRollingRpm(tangentVelocityOut.magnitude, radius);
            spinLimitRpm = Mathf.Min(maxGeneratedFlatBounceSpinRpm, rollingRpm * rollingEfficiency);

            if (signedSpinAfterRpm > spinLimitRpm)
                return ReplaceSpinAlongAxis(spinOut, topspinAxis, BaseShotLibrary.RpmToRadPerSecond(spinLimitRpm), out spinLimited);

            return spinOut;
        }

        if (signedSpinBeforeRpm < -spinClassThresholdRpm && signedSpinAfterRpm > maxBackspinReversalTopspinRpm)
        {
            spinLimitRpm = maxBackspinReversalTopspinRpm;
            return ReplaceSpinAlongAxis(spinOut, topspinAxis, BaseShotLibrary.RpmToRadPerSecond(spinLimitRpm), out spinLimited);
        }

        return spinOut;
    }

    private Vector3 ReplaceSpinAlongAxis(Vector3 spin, Vector3 axis, float targetRadPerSecond, out bool changed)
    {
        float current = Vector3.Dot(spin, axis);
        changed = !Mathf.Approximately(current, targetRadPerSecond);
        return spin + axis * (targetRadPerSecond - current);
    }

    private float GetFlatSpinRollingEfficiency(float slipSpeed)
    {
        Vector2 range = GetFlatSpinEfficiencyRange();
        float slipT = Mathf.InverseLerp(0f, Mathf.Max(0.001f, maxSlipSpeedForSpinEfficiency), Mathf.Max(0f, slipSpeed));
        return Mathf.Lerp(range.x, range.y, slipT);
    }

    private Vector2 GetFlatSpinEfficiencyRange()
    {
        switch (courtSurfacePreset)
        {
            case CourtSurfacePreset.Grass:
                return grassFlatSpinEfficiency;
            case CourtSurfacePreset.Clay:
                return clayFlatSpinEfficiency;
            case CourtSurfacePreset.Custom:
                return customFlatSpinEfficiency;
            case CourtSurfacePreset.Hard:
            default:
                return hardFlatSpinEfficiency;
        }
    }

    private float TangentSpeedToRollingRpm(float tangentSpeed, float radius)
    {
        if (radius <= 0.0001f)
            return 0f;

        float rollingRadPerSecond = Mathf.Max(0f, tangentSpeed) / radius;
        return BaseShotLibrary.RadPerSecondToRpm(rollingRadPerSecond);
    }

    private Vector3 ClampSpinMagnitude(Vector3 spin, float maxRpm)
    {
        if (maxRpm <= 0f)
            return spin;

        float maxRad = BaseShotLibrary.RpmToRadPerSecond(maxRpm);
        if (spin.magnitude <= maxRad)
            return spin;

        return spin.normalized * maxRad;
    }

    private float SignedSpinRpmForVelocity(Vector3 spin, Vector3 tangentVelocity, Vector3 normal)
    {
        if (spin.sqrMagnitude < 0.0001f)
            return 0f;

        if (tangentVelocity.sqrMagnitude < 0.001f)
            return BaseShotLibrary.RadPerSecondToRpm(spin.magnitude);

        Vector3 topspinAxis = Vector3.Cross(normal, tangentVelocity.normalized).normalized;
        return BaseShotLibrary.RadPerSecondToRpm(Vector3.Dot(spin, topspinAxis));
    }
    public void SetSpin(Vector3 spinRadPerSecond)
    {
        if (rb == null)
        {
            pendingSpinRadPerSecond = spinRadPerSecond;
            hasPendingSpin = true;
            return;
        }

        ApplySpinRadPerSecond(spinRadPerSecond);
        hasPendingSpin = false;
        RestartSpinLog();
    }

    private void RestartSpinLog()
    {
        if (spinLogRoutine != null)
            StopCoroutine(spinLogRoutine);

        if (isActiveAndEnabled)
            spinLogRoutine = StartCoroutine(LogSpinOverTime());
    }

    public void SetSpin(float legacyMagnitude, Vector3 spinRadPerSecond)
    {
        SetSpin(spinRadPerSecond);
    }

    private void ApplySpinRadPerSecond(Vector3 newSpinRadPerSecond)
    {
        spinRadPerSecond = newSpinRadPerSecond;
        spinMagnitudeRadPerSecond = spinRadPerSecond.magnitude;
        spinRpm = BaseShotLibrary.RadPerSecondToRpm(GetSignedSpinRadPerSecond());

        spinVector = spinRadPerSecond;
        spinMagnitude = spinMagnitudeRadPerSecond;

        ApplyAngularVelocityCap();
        rb.angularVelocity = spinRadPerSecond;
    }

    private float GetSignedSpinRadPerSecond()
    {
        if (rb == null || spinRadPerSecond.sqrMagnitude < 0.0001f)
            return 0f;

        Vector3 horizontalVelocity = rb.linearVelocity;
        horizontalVelocity.y = 0f;

        if (horizontalVelocity.sqrMagnitude < 0.001f)
            return spinMagnitudeRadPerSecond;

        Vector3 topspinAxis = Vector3.Cross(Vector3.up, horizontalVelocity.normalized).normalized;
        return Vector3.Dot(spinRadPerSecond, topspinAxis);
    }


    // ----------------------------------------------------------------------
    // FixedUpdate — apply custom drag + track last velocity
    // ----------------------------------------------------------------------
    void FixedUpdate()
    {
        if (rb == null) return;

        if (solverComponent != null && solverComponent.traj != null && solverComponent.traj.magnus != null)
        {
            Vector3 v = rb.linearVelocity;
            if (v.sqrMagnitude > 0.0001f)
            {
                Vector3 spinState = solverComponent.traj.magnus.ApplySpinDecay(spinRadPerSecond, Time.fixedDeltaTime);
                ApplySpinRadPerSecond(spinState);

                Vector3 dragAccel = solverComponent.traj.magnus.DragAcceleration(v, spinRadPerSecond);
                Vector3 magnusAccel = solverComponent.traj.magnus.MagnusAcceleration(v, spinRadPerSecond);
                rb.linearVelocity += (dragAccel + magnusAccel) * Time.fixedDeltaTime;
            }
        }

        UpdateBounceApexTracking();

        prePhysicsPosition = rb.position;
        prePhysicsVelocity = rb.linearVelocity;
        prePhysicsSpinRadPerSecond = spinRadPerSecond;

        if (externalControl) return;

        lastVel = rb.linearVelocity;
    }

    // ----------------------------------------------------------------------
    // Timed velocity logging (fixed + robust)
    // ----------------------------------------------------------------------
    public IEnumerator LogVelocityForSeconds()
    {
        if (rb == null) yield break;
        float endTime = Time.time + velocityLogDuration;

        while (Time.time < endTime)
        {
            float speed = rb.linearVelocity.magnitude;   // Use rb.velocity, not linearVelocity
            Debug.Log($"[BallController] Speed = {speed:F2} m/s");

            yield return new WaitForSeconds(velocityLogInterval);
        }
    }

    private IEnumerator LogShotHeightForSeconds(int shotSequence)
    {
        if (rb == null)
            yield break;

        float startTime = Time.time;
        float duration = Mathf.Max(0f, shotHeightLogDuration);
        float interval = Mathf.Max(0.01f, shotHeightLogInterval);

        while (Time.time - startTime <= duration + 0.0001f)
        {
            if (shotSequence != ShotSequence)
            {
                Debug.Log($"[BALL HEIGHT] shot={shotSequence} trace ended when shot={ShotSequence} launched.");
                yield break;
            }

            float elapsed = Time.time - startTime;
            Vector3 position = rb.position;
            Vector3 velocity = rb.linearVelocity;
            Debug.Log($"[BALL HEIGHT] shot={shotSequence} t={elapsed:F2}s height={position.y:F2}m pos={position} verticalSpeed={velocity.y:F2}m/s speed={velocity.magnitude:F2}m/s bounces={CourtBouncesSinceLastHit}.");
            yield return new WaitForSeconds(interval);
        }
    }

    private IEnumerator LogSpinOverTime()
    {
        float elapsed = 0f;

        while (elapsed < spinLogDuration)
        {
            float radPerSecond = spinMagnitudeRadPerSecond;
            float rpm = spinRpm;
            Vector3 axis = spinRadPerSecond.sqrMagnitude > 0.0001f ? spinRadPerSecond.normalized : Vector3.zero;
            float visualRadPerSecond = rb != null ? rb.angularVelocity.magnitude : 0f;

            Debug.Log($"[SPIN] t={elapsed:F2}s  rpm={rpm:F0}  rad/s={radPerSecond:F2}  visualRad/s={visualRadPerSecond:F2}  axis=({axis.x:F2}, {axis.y:F2}, {axis.z:F2})");

            yield return new WaitForSeconds(spinLogInterval);
            elapsed += spinLogInterval;
        }
    }


}
