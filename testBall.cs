using System.Collections;
using UnityEngine;

public class GhostShotTester : MonoBehaviour
{
    [Header("References")]
    public hitController hit;
    public Transform ball;
    public Transform reticle;
    public ShotSolverComponent solverComponent;
    public AimingController aimingController;

    [Header("Trajectory Line")]
    public LineRenderer lineRenderer;
    public int steps = 200;
    public float dt = 0.01f;

    // Test swipe inputs (MATCH REAL GAMEPLAY)
    public float testSwipeSpeed = 25.4f;   // 100% reticle speed
    public float testQuality = 1f;         // perfect contact
    public float testHold = 1f;            // full hold

    void Awake()
    {
        if (hit == null)
            hit = FindFirstObjectByType<hitController>();

        if (hit != null)
        {
            if (ball == null && hit.ball != null)
                ball = hit.ball.transform;

            if (reticle == null)
                reticle = hit.reticle;

            if (solverComponent == null)
                solverComponent = hit.solverComponent;

            if (aimingController == null)
                aimingController = hit.aimingController;
        }
    }

    void Update()
    {
        if (solverComponent == null || solverComponent.traj == null)
            return;

        if (Input.GetKeyDown(KeyCode.Keypad8))
            FireGhostShot(BaseShotType.Topspin);

        if (Input.GetKeyDown(KeyCode.Keypad9))
            FireGhostShot(BaseShotType.Flat);

        if (Input.GetKeyDown(KeyCode.Keypad7))
            FireGhostShot(BaseShotType.Slice);
    }
    private Transform FindActiveBall()
    {
        BallController bc = FindFirstObjectByType<BallController>(FindObjectsInactive.Exclude);
        return bc != null ? bc.transform : null;
    }
    private void FireGhostShot(BaseShotType shotType)
    {
        // Try to find the ball if missing or inactive
        if (ball == null || !ball.gameObject.activeInHierarchy)
        {
            BallController bc = FindFirstObjectByType<BallController>(FindObjectsInactive.Exclude);

            if (bc != null)
            {
                ball = bc.transform;
            }
            else
            {
                Debug.LogWarning("[GHOST] No active ball found in scene. Cannot fire real ball.");
                // BUT STILL DRAW GHOST ARC
                // So do NOT return here — continue to compute ghost trajectory
            }
        }

        if (reticle == null)
        {
            Debug.LogWarning("[GHOST] Missing reticle reference.");
            return;
        }

        if (aimingController != null)
            aimingController.enabled = false;


        // -----------------------------
        // 1. Compute lateralDir EXACTLY like HitController
        // -----------------------------
        Vector3 toReticle = (reticle.position - ball.position);
        if (toReticle.sqrMagnitude < 1e-6f) toReticle = Vector3.forward;
        toReticle.y = 0f;
        toReticle.Normalize();

        Vector3 playerAim = toReticle;

        float maxAngle = 45f;
        float signedAngle = Vector3.SignedAngle(toReticle, playerAim, Vector3.up);
        float clampedAngle = Mathf.Clamp(signedAngle, -maxAngle, maxAngle);

        Quaternion rot = Quaternion.AngleAxis(clampedAngle, Vector3.up);
        Vector3 lateralDir = (rot * toReticle).normalized;

        lateralDir = ManualAimWeighter.Apply(toReticle, playerAim);
        lateralDir = SmallAngleBooster.Apply(lateralDir);

        // -----------------------------
        // 2. Compute shot using REAL GAMEPLAY INPUTS
        // -----------------------------
        ShotComputationSolver solver = new ShotComputationSolver();

        var shot = solver.ComputeShot(
            manualV0: testSwipeSpeed,          // MATCH REAL GAMEPLAY
            speedBlend: hit.speedBlend,        // MATCH REAL GAMEPLAY
            quality: testQuality,              // MATCH REAL GAMEPLAY
            baseType: shotType,
            modifier: ShotModifier.Normal,
            holdScale: testHold,               // MATCH REAL GAMEPLAY
            minHoldAngleDeg: hit.minHoldAngleDeg,
            maxExtraPowerFraction: hit.maxExtraPowerFraction,
            spinIntent: hit.currentSpinIntent,
            backswingScale: 1f,
            forwardSwingProgress: 1f,
            backswingCapSpeed: hit.maxShotPower,
            maxShotPower: hit.maxShotPower,
            desiredNetClearance: hit.solverComponent != null ? hit.solverComponent.netMargin : hit.netMargin,
            heightIntent: BaseShotLibrary.HeightIntent,
            heightAngleDeg: float.NaN,
            useHeightAngleOverride: true,
            incomingVelocity: Vector3.zero,
            incomingSpinRadPerSecond: Vector3.zero,
            returnDirection: lateralDir,
            aimingController: aimingController,
            reticle: reticle,
            ball: ball
        );

        // -----------------------------
        // 3. Build launch velocity EXACTLY like HitController
        // -----------------------------
        // -----------------------------
        // 3. FORCE AN 8° FLAT SHOT AND SOLVE ONLY FOR SPEED
        // -----------------------------
        Vector3 placePos = ball.position + lateralDir * hit.forwardPlaceOffset;
        Vector3 spinAxis = Vector3.Cross(Vector3.up, lateralDir).normalized;
        shot.spinRadPerSecond = spinAxis * shot.appliedSpinRadPerSecond;

        float forcedTheta = 8f * Mathf.Deg2Rad;

        float requiredSpeed = solverComponent.solver.SolveSpeedForFixedAngle(
            new Vector2(placePos.x, placePos.y),
            reticle.position.x,
            forcedTheta,
            shot.spinRadPerSecond
        );

        float vx = requiredSpeed * Mathf.Cos(forcedTheta);
        float vy = requiredSpeed * Mathf.Sin(forcedTheta);

        Vector3 launchVelocity = lateralDir * vx + Vector3.up * vy;



        // -----------------------------
        // 4. Place ball EXACTLY like HitController
        // -----------------------------
        
        ball.position = placePos;

        // -----------------------------
        // 5. Draw 3D trajectory using SAME PHYSICS as real ball
        // -----------------------------
        DrawGhostTrajectory3D(placePos, launchVelocity, shot.spinRadPerSecond);

        // -----------------------------
        // 6. Fire real ball EXACTLY like HitController
        // -----------------------------
        FireRealBall(placePos, launchVelocity, shot);

        Debug.Log($"[GHOST] Fired {shotType} ghost shot: v0={launchVelocity}, spinRpm={shot.appliedSpinRpm:F0}, spinRad={shot.appliedSpinRadPerSecond:F2}");

        StartCoroutine(ReenableAiming());
    }

    private void DrawGhostTrajectory3D(Vector3 startPos, Vector3 launchVelocity, Vector3 spin)
    {
        if (lineRenderer == null)
        {
            GameObject lrObj = new GameObject("GhostTrajectoryLine");
            lrObj.transform.SetParent(transform);
            lineRenderer = lrObj.AddComponent<LineRenderer>();
            lineRenderer.widthMultiplier = 0.03f;
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            lineRenderer.startColor = Color.cyan;
            lineRenderer.endColor = Color.blue;
        }

        var phys = solverComponent.traj.phys;
        var magnus = solverComponent.traj.magnus;

        Vector3 pos = startPos;
        Vector3 vel = launchVelocity;
        Vector3 spin3 = spin;

        lineRenderer.positionCount = steps;

        for (int i = 0; i < steps; i++)
        {
            lineRenderer.SetPosition(i, pos);

            if (pos.y <= 0f && i > 0)
            {
                lineRenderer.positionCount = i + 1;
                break;
            }

            spin3 = magnus.ApplySpinDecay(spin3, dt);

            Vector3 k1_v = TotalAccel(vel, spin3, phys, magnus);
            Vector3 k1_p = vel;

            Vector3 v2 = vel + 0.5f * dt * k1_v;
            Vector3 k2_v = TotalAccel(v2, spin3, phys, magnus);
            Vector3 k2_p = v2;

            Vector3 v3 = vel + 0.5f * dt * k2_v;
            Vector3 k3_v = TotalAccel(v3, spin3, phys, magnus);
            Vector3 k3_p = v3;

            Vector3 v4 = vel + dt * k3_v;
            Vector3 k4_v = TotalAccel(v4, spin3, phys, magnus);
            Vector3 k4_p = v4;

            vel += (dt / 6f) * (k1_v + 2f * k2_v + 2f * k3_v + k4_v);
            pos += (dt / 6f) * (k1_p + 2f * k2_p + 2f * k3_p + k4_p);
        }
    }

    private Vector3 TotalAccel(Vector3 vel, Vector3 spin, DragBallistics phys, MagnusBallistics magnus)
    {
        Vector3 drag = magnus.DragAcceleration(vel, spin);
        Vector3 mag = magnus.MagnusAcceleration(vel, spin);
        Vector3 gravity = new Vector3(0f, phys.gravity, 0f);
        return drag + mag + gravity;
    }

    private void FireRealBall(Vector3 placePos, Vector3 launchVelocity, ShotComputationSolver.ShotResult shot)
    {
        if (ball == null) return;

        // ⭐ Ensure GameObject is active
        if (!ball.gameObject.activeSelf)
            ball.gameObject.SetActive(true);

        // ⭐ Ensure BallController is enabled
        BallController bc = ball.GetComponent<BallController>();
        if (bc != null && !bc.enabled)
            bc.enabled = true;

        Rigidbody rb = ball.GetComponent<Rigidbody>();
        if (rb == null) return;

        // Position + physics setup
        rb.position = placePos;
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.WakeUp();

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        rb.linearVelocity = launchVelocity;

        // ⭐ Start coroutines safely AFTER one frame
        StartCoroutine(StartBallCoroutinesNextFrame(bc, shot));
    }

    private IEnumerator StartBallCoroutinesNextFrame(BallController bc, ShotComputationSolver.ShotResult shot)
    {
        // ⭐ Wait one frame so Unity registers the GameObject as active
        yield return null;

        if (bc != null)
        {
            bc.SetSpin(shot.spinRadPerSecond);
            bc.StartCoroutine(bc.TemporaryExternalControl(0.08f));
            bc.StartCoroutine(bc.LogVelocityForSeconds());
        }
    }


    private IEnumerator ReenableAiming()
    {
        yield return new WaitForSeconds(0.1f);
        if (aimingController != null)
            aimingController.enabled = true;
    }
}

