using UnityEngine;

public class ballCannon : MonoBehaviour
{
    public static event System.Action<Rigidbody, Vector3, Vector3> CannonBallLaunched;

    public enum FeedShotType
    {
        Fixed,
        ShortFlat,
        DeepFlat,
        Topspin,
        Slice,
        Lob,
        Drop
    }

    [System.Serializable]
    public struct FeedLaunch
    {
        public Vector3 horizontalDirection;
        public Vector3 targetPoint;
        public float speed;
        public float angle;
        public float spinRpm;
        public FeedShotType shotType;
        public int zoneIndex;
        public Vector2Int zoneCoord;
        public float lateralOffsetFromCenter;
    }

    [System.Serializable]
    public struct FeedZoneProbabilities
    {
        [Range(0f, 1f)] public float nearLeft;
        [Range(0f, 1f)] public float nearMiddle;
        [Range(0f, 1f)] public float nearRight;
        [Range(0f, 1f)] public float midLeft;
        [Range(0f, 1f)] public float midMiddle;
        [Range(0f, 1f)] public float midRight;
        [Range(0f, 1f)] public float deepLeft;
        [Range(0f, 1f)] public float deepMiddle;
        [Range(0f, 1f)] public float deepRight;

        public FeedZoneProbabilities(
            float nearLeft,
            float nearMiddle,
            float nearRight,
            float midLeft,
            float midMiddle,
            float midRight,
            float deepLeft,
            float deepMiddle,
            float deepRight)
        {
            this.nearLeft = nearLeft;
            this.nearMiddle = nearMiddle;
            this.nearRight = nearRight;
            this.midLeft = midLeft;
            this.midMiddle = midMiddle;
            this.midRight = midRight;
            this.deepLeft = deepLeft;
            this.deepMiddle = deepMiddle;
            this.deepRight = deepRight;
        }
    }

    [System.Serializable]
    public struct FeedShotProbabilities
    {
        [Range(0f, 1f)] public float shortFlat;
        [Range(0f, 1f)] public float deepFlat;
        [Range(0f, 1f)] public float topspin;
        [Range(0f, 1f)] public float slice;
        [Range(0f, 1f)] public float lob;
        [Range(0f, 1f)] public float drop;

        public FeedShotProbabilities(
            float shortFlat,
            float deepFlat,
            float topspin,
            float slice,
            float lob,
            float drop)
        {
            this.shortFlat = shortFlat;
            this.deepFlat = deepFlat;
            this.topspin = topspin;
            this.slice = slice;
            this.lob = lob;
            this.drop = drop;
        }
    }

    [System.Serializable]
    public struct FeedShotProfile
    {
        public Vector2 speedRange;
        public Vector2 angleRange;
        public Vector2 spinRpmRange;

        public FeedShotProfile(Vector2 speedRange, Vector2 angleRange, Vector2 spinRpmRange)
        {
            this.speedRange = speedRange;
            this.angleRange = angleRange;
            this.spinRpmRange = spinRpmRange;
        }
    }

    [Header("Launch Settings")]
    public float launchSpeed = 20f;
    public float launchAngle = 15f;
    public Vector3 launchDirection = Vector3.forward;

    [Header("Optional Target")]
    public Transform target;

    [Header("Random Feed")]
    public bool randomizeFeed = true;
    [Range(0f, 1f)] public float deepFeedChance = 0.75f;
    public bool aimAtRandomTargetPoint = true;
    public Vector2 lateralTargetRange = new Vector2(-3f, 3f);
    public Vector2 shortDepthOffsetRange = new Vector2(-1f, 1.25f);
    public Vector2 deepDepthOffsetRange = new Vector2(1.5f, 4.5f);

    [Header("3x3 Target Grid")]
    public bool useReticleBoundsGrid = true;
    public UIWorldReticle reticleBoundsSource;
    public bool autoFindReticleBounds = true;
    public Transform targetMinBound;
    public Transform targetMaxBound;
    public Transform targetFrontLeftBound;
    public Transform targetFrontRightBound;
    public Transform targetRearRightBound;
    public Transform targetRearLeftBound;
    public bool mirrorReticleBoundsAcrossNet = true;
    public bool useNetPointAsMirrorCenter = true;
    public float targetGridMirrorCenterX = 0f;
    public bool useSymmetricLateralGrid = true;
    public bool useNetPointAsLateralCenter = true;
    public float targetGridCenterZ = 0f;
    public Vector2 fallbackTargetXRange = new Vector2(-12.5f, -1f);
    public Vector2 fallbackTargetZRange = new Vector2(-5f, 5f);
    public Vector2 targetCellPadding = new Vector2(0.25f, 0.25f);
    public bool avoidSameLateralColumnStreaks = true;
    [Range(1, 10)] public int maxSameLateralColumnStreak = 2;
    public FeedZoneProbabilities zoneProbabilities = new FeedZoneProbabilities(
        0.05f, 0.06f, 0.05f,
        0.25f, 0.30f, 0.25f,
        0.75f, 1.00f, 0.75f
    );

    [Header("Shot Type Probabilities")]
    public FeedShotProbabilities shotProbabilities = new FeedShotProbabilities(
        0.15f,
        0.65f,
        0.15f,
        0.05f,
        0.00f,
        0.00f
    );

    [Header("Feed Shot Profiles")]
    public FeedShotProfile shortFlatProfile = new FeedShotProfile(
        new Vector2(13f, 18f),
        new Vector2(5f, 9f),
        new Vector2(0f, 250f)
    );
    public FeedShotProfile deepFlatProfile = new FeedShotProfile(
        new Vector2(16f, 22f),
        new Vector2(6f, 12f),
        new Vector2(0f, 350f)
    );
    public FeedShotProfile topspinProfile = new FeedShotProfile(
        new Vector2(16f, 23f),
        new Vector2(9f, 16f),
        new Vector2(1800f, 3000f)
    );
    public FeedShotProfile sliceProfile = new FeedShotProfile(
        new Vector2(12f, 18f),
        new Vector2(5f, 10f),
        new Vector2(-1200f, -2200f)
    );
    public FeedShotProfile lobProfile = new FeedShotProfile(
        new Vector2(13f, 20f),
        new Vector2(18f, 28f),
        new Vector2(500f, 1200f)
    );
    public FeedShotProfile dropProfile = new FeedShotProfile(
        new Vector2(8f, 13f),
        new Vector2(10f, 18f),
        new Vector2(-500f, -1500f)
    );

    [Header("Legacy Random Feed Speeds")]
    public Vector2 shortFlatSpeedRange = new Vector2(13f, 18f);
    public Vector2 deepFlatSpeedRange = new Vector2(16f, 22f);

    [Header("No Lob Angle Limits")]
    public Vector2 shortFlatAngleRange = new Vector2(5f, 9f);
    public Vector2 deepFlatAngleRange = new Vector2(6f, 12f);
    [Range(0f, 20f)] public float noLobMaxLaunchAngle = 12f;

    [Header("Trajectory Solver Feed")]
    public bool useTrajectorySolverForFeed = true;
    public ShotSolverComponent solverComponent;
    public bool autoFindSolverComponent = true;
    public Transform netPoint;
    public bool autoFindNetPoint = true;
    public string netObjectName = "net";
    public bool autoSetFeedNetHeightFromRenderer = true;
    public float feedNetHeight = 0.914f;
    public float feedNetMargin = 0.25f;
    public bool requireSolvedRandomFeeds = true;
    public float minimumRandomFeedNetMargin = 0.45f;
    public bool allowExtraSpeedForFlatFeed = true;
    public float extraFlatFeedMaxSpeed = 28f;
    public bool logSolverFailures = false;

    [Header("Last Feed Debug")]
    public FeedShotType lastFeedShotType = FeedShotType.Fixed;
    public Vector3 lastFeedTargetPoint;
    public float lastFeedSpeed;
    public float lastFeedAngle;
    public float lastFeedSpinRpm;
    public bool lastFeedUsedSolver;
    public bool lastFeedUsedExtraSpeed;
    public float lastFeedTargetDistance;
    public float lastFeedNetDistance;
    public int lastFeedZoneIndex = -1;
    public Vector2Int lastFeedZoneCoord = new Vector2Int(-1, -1);
    public float lastFeedLateralOffsetFromCenter;

    private Rigidbody rb;
    private int lastPickedLateralColumn = -1;
    private int sameLateralColumnStreak = 0;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    public void Launch()
    {
        Launch(rb, ResolveLaunchDirection(transform.position));
    }

    public void Launch(Rigidbody targetRb)
    {
        if (targetRb == null)
            return;

        Launch(targetRb, ResolveLaunchDirection(targetRb.position));
    }

    public void Launch(Rigidbody targetRb, Vector3 horizontalDirection)
    {
        if (targetRb == null)
            return;

        targetRb.isKinematic = false;
        targetRb.useGravity = true;
        targetRb.linearVelocity = Vector3.zero;
        targetRb.angularVelocity = Vector3.zero;
        targetRb.WakeUp();
        Vector3 launchVelocity = CalculateLaunchVelocity(horizontalDirection);
        targetRb.linearVelocity = launchVelocity;
        CannonBallLaunched?.Invoke(targetRb, targetRb.position, launchVelocity);
        targetRb.GetComponent<BallController>()?.OnHit();
    }

    public void Launch(Rigidbody targetRb, FeedLaunch feedLaunch)
    {
        if (targetRb == null)
            return;

        targetRb.isKinematic = false;
        targetRb.useGravity = true;
        targetRb.linearVelocity = Vector3.zero;
        targetRb.angularVelocity = Vector3.zero;
        targetRb.WakeUp();

        Vector3 launchVelocity = CalculateLaunchVelocity(
            feedLaunch.horizontalDirection,
            feedLaunch.speed,
            feedLaunch.angle
        );
        targetRb.linearVelocity = launchVelocity;
        CannonBallLaunched?.Invoke(targetRb, targetRb.position, launchVelocity);

        BallController controller = targetRb.GetComponent<BallController>();
        if (controller != null)
            controller.SetSpin(BuildWorldSpinVector(feedLaunch.horizontalDirection, feedLaunch.spinRpm));

        controller?.OnHit();
    }

    public Vector3 CalculateLaunchVelocity()
    {
        return CalculateLaunchVelocity(ResolveLaunchDirection(transform.position));
    }

    public Vector3 CalculateLaunchVelocity(Vector3 horizontalDirection)
    {
        return CalculateLaunchVelocity(horizontalDirection, launchSpeed, launchAngle);
    }

    public Vector3 CalculateLaunchVelocity(Vector3 horizontalDirection, float speed, float angle)
    {
        Vector3 horizontalDir = horizontalDirection;
        horizontalDir.y = 0f;

        if (horizontalDir.sqrMagnitude < 1e-6f)
            horizontalDir = launchDirection;

        horizontalDir.y = 0f;
        horizontalDir.Normalize();

        float rad = angle * Mathf.Deg2Rad;
        float horizontalSpeed = speed * Mathf.Cos(rad);
        float verticalSpeed = speed * Mathf.Sin(rad);

        Vector3 velocity = horizontalDir * horizontalSpeed;
        velocity.y = verticalSpeed;

        return velocity;
    }

    public FeedLaunch BuildFeedLaunch(Vector3 origin, Transform targetOverride, Vector3 fallbackHorizontalDirection)
    {
        Vector3 targetPoint = ResolveBaseTargetPoint(origin, targetOverride, fallbackHorizontalDirection);
        int zoneIndex = -1;
        Vector2Int zoneCoord = new Vector2Int(-1, -1);
        bool usedGridTarget = false;

        Vector3 baseHorizontalDirection = targetPoint - origin;
        baseHorizontalDirection.y = 0f;

        if (baseHorizontalDirection.sqrMagnitude < 1e-6f)
            baseHorizontalDirection = fallbackHorizontalDirection;

        if (baseHorizontalDirection.sqrMagnitude < 1e-6f)
            baseHorizontalDirection = launchDirection;

        baseHorizontalDirection.y = 0f;
        if (baseHorizontalDirection.sqrMagnitude < 1e-6f)
            baseHorizontalDirection = Vector3.forward;

        baseHorizontalDirection.Normalize();

        FeedShotType shotType = randomizeFeed ? PickWeightedShotType() : FeedShotType.Fixed;
        FeedShotProfile profile = GetProfile(shotType);
        float speed = randomizeFeed ? RandomRange(profile.speedRange) : launchSpeed;
        float angle = randomizeFeed ? RandomRange(profile.angleRange) : launchAngle;
        float spinRpm = randomizeFeed ? RandomRange(profile.spinRpmRange) : 0f;
        Vector2 selectedSpeedRange = randomizeFeed ? profile.speedRange : new Vector2(speed, speed);
        Vector2 selectedAngleRange = randomizeFeed ? profile.angleRange : new Vector2(angle, angle);
        bool usedSolver = false;

        if (!IsLob(shotType))
            angle = Mathf.Min(angle, noLobMaxLaunchAngle);

        if (randomizeFeed && aimAtRandomTargetPoint)
        {
            if (useReticleBoundsGrid && TryPickGridTarget(out Vector3 gridTarget, out zoneIndex, out zoneCoord))
            {
                targetPoint = gridTarget;
                usedGridTarget = true;
            }
            else
            {
                Vector3 right = Vector3.Cross(Vector3.up, baseHorizontalDirection);
                if (right.sqrMagnitude < 1e-6f)
                    right = Vector3.right;
                right.Normalize();

                bool deepFeed = shotType == FeedShotType.DeepFlat || shotType == FeedShotType.Topspin || shotType == FeedShotType.Slice;
                float lateralOffset = RandomRange(lateralTargetRange);
                float depthOffset = RandomRange(deepFeed ? deepDepthOffsetRange : shortDepthOffsetRange);
                targetPoint += right * lateralOffset + baseHorizontalDirection * depthOffset;
            }
        }

        Vector3 horizontalDirection = targetPoint - origin;
        horizontalDirection.y = 0f;

        if (horizontalDirection.sqrMagnitude < 1e-6f)
            horizontalDirection = baseHorizontalDirection;

        horizontalDirection.y = 0f;
        horizontalDirection.Normalize();

        float solvedSpeed = float.NaN;
        float solvedAngle = float.NaN;
        float targetDistance = HorizontalDistance(origin, targetPoint);
        float netDistance = ResolveNetDistanceAlongShot(origin, horizontalDirection, targetDistance);
        bool usedExtraSpeed = false;

        bool solvedFeed = useTrajectorySolverForFeed &&
            TrySolveTrajectoryFeed(
                origin,
                targetPoint,
                horizontalDirection,
                selectedSpeedRange,
                selectedAngleRange,
                spinRpm,
                shotType,
                out solvedSpeed,
                out solvedAngle,
                out targetDistance,
                out netDistance,
                out usedExtraSpeed);

        if (!solvedFeed && useTrajectorySolverForFeed && randomizeFeed && requireSolvedRandomFeeds)
        {
            Vector2 safeSpeedRange = new Vector2(
                Mathf.Min(selectedSpeedRange.x, selectedSpeedRange.y),
                Mathf.Max(Mathf.Max(selectedSpeedRange.x, selectedSpeedRange.y), extraFlatFeedMaxSpeed)
            );

            solvedFeed = TrySolveTrajectoryFeed(
                origin,
                targetPoint,
                horizontalDirection,
                safeSpeedRange,
                selectedAngleRange,
                spinRpm,
                shotType,
                out solvedSpeed,
                out solvedAngle,
                out targetDistance,
                out netDistance,
                out usedExtraSpeed);
        }

        if (solvedFeed)
        {
            speed = solvedSpeed;
            angle = solvedAngle;
            usedSolver = true;
            lastFeedUsedExtraSpeed = usedExtraSpeed;
            lastFeedTargetDistance = targetDistance;
            lastFeedNetDistance = netDistance;
        }
        else
        {
            lastFeedUsedExtraSpeed = false;
            lastFeedTargetDistance = targetDistance;
            lastFeedNetDistance = netDistance;
        }

        lastFeedShotType = shotType;
        lastFeedTargetPoint = targetPoint;
        lastFeedSpeed = speed;
        lastFeedAngle = angle;
        lastFeedSpinRpm = spinRpm;
        lastFeedUsedSolver = usedSolver;
        lastFeedZoneIndex = zoneIndex;
        lastFeedZoneCoord = zoneCoord;
        lastFeedLateralOffsetFromCenter = targetPoint.z - ResolveLateralCenterZ();

        return new FeedLaunch
        {
            horizontalDirection = horizontalDirection,
            targetPoint = targetPoint,
            speed = speed,
            angle = angle,
            spinRpm = spinRpm,
            shotType = shotType,
            zoneIndex = usedGridTarget ? zoneIndex : -1,
            zoneCoord = usedGridTarget ? zoneCoord : new Vector2Int(-1, -1),
            lateralOffsetFromCenter = targetPoint.z - ResolveLateralCenterZ()
        };
    }

    private Vector3 ResolveLaunchDirection(Vector3 origin)
    {
        if (target != null)
            return target.position - origin;

        return launchDirection;
    }

    private bool TrySolveTrajectoryFeed(
        Vector3 origin,
        Vector3 targetPoint,
        Vector3 horizontalDirection,
        Vector2 speedRange,
        Vector2 angleRange,
        float spinRpm,
        FeedShotType shotType,
        out float solvedSpeed,
        out float solvedAngle,
        out float targetDistance,
        out float netDistance,
        out bool usedExtraSpeed)
    {
        solvedSpeed = float.NaN;
        solvedAngle = float.NaN;
        usedExtraSpeed = false;
        targetDistance = HorizontalDistance(origin, targetPoint);
        netDistance = ResolveNetDistanceAlongShot(origin, horizontalDirection, targetDistance);

        ShotSolverComponent component = GetSolverComponent();
        if (component == null || component.traj == null)
        {
            if (logSolverFailures)
                Debug.LogWarning("[Cannon Solver] No ShotSolverComponent/traj available. Using simple launch fallback.");
            return false;
        }

        if (targetDistance < 0.5f)
        {
            if (logSolverFailures)
                Debug.LogWarning($"[Cannon Solver] Target too close. distance={targetDistance:F2}");
            return false;
        }

        netDistance = Mathf.Clamp(netDistance, 0.1f, targetDistance - 0.5f);
        if (targetDistance < netDistance + 0.5f)
        {
            if (logSolverFailures)
                Debug.LogWarning($"[Cannon Solver] Target too close to net. target={targetDistance:F2}, net={netDistance:F2}");
            return false;
        }

        float minSpeed = Mathf.Min(speedRange.x, speedRange.y);
        float maxSpeed = Mathf.Max(speedRange.x, speedRange.y);
        float minAngle = Mathf.Min(angleRange.x, angleRange.y);
        float maxAngle = Mathf.Max(angleRange.x, angleRange.y);
        if (!IsLob(shotType))
            maxAngle = Mathf.Min(maxAngle, noLobMaxLaunchAngle);

        if (maxSpeed <= 0f || maxAngle <= 0f)
            return false;

        DragShotSolver feedSolver = new DragShotSolver(component.traj)
        {
            minSpeed = Mathf.Max(0.1f, minSpeed),
            maxSpeed = Mathf.Max(0.1f, maxSpeed),
            minAngleDeg = Mathf.Max(0.1f, minAngle),
            maxAngleDeg = Mathf.Max(0.1f, maxAngle),
            netMargin = ResolveFeedNetMargin()
        };

        Vector2 startPos = new Vector2(0f, origin.y);
        Vector3 solverSpin = new Vector3(0f, 0f, -BaseShotLibrary.RpmToRadPerSecond(spinRpm));
        float resolvedFeedNetHeight = ResolveFeedNetHeight();
        float resolvedFeedNetMargin = ResolveFeedNetMargin();
        var solution = feedSolver.SolveForReticle(
            startPos,
            targetDistance,
            netDistance,
            resolvedFeedNetHeight,
            resolvedFeedNetMargin,
            solverSpin
        );

        if ((!float.IsFinite(solution.v0) || !float.IsFinite(solution.theta)) &&
            allowExtraSpeedForFlatFeed &&
            extraFlatFeedMaxSpeed > maxSpeed)
        {
            feedSolver.minSpeed = Mathf.Max(0.1f, maxSpeed);
            feedSolver.maxSpeed = Mathf.Max(feedSolver.minSpeed, extraFlatFeedMaxSpeed);

            solution = feedSolver.SolveForReticle(
                startPos,
                targetDistance,
                netDistance,
                resolvedFeedNetHeight,
                feedNetMargin,
                solverSpin
            );

            usedExtraSpeed = float.IsFinite(solution.v0) && float.IsFinite(solution.theta);
        }

        if (!float.IsFinite(solution.v0) || !float.IsFinite(solution.theta))
        {
            if (logSolverFailures)
            {
                Debug.LogWarning(
                    $"[Cannon Solver] No {shotType} solution. target={targetDistance:F2}m, net={netDistance:F2}m, " +
                    $"speedRange={minSpeed:F1}-{maxSpeed:F1}, extraSpeedMax={extraFlatFeedMaxSpeed:F1}, " +
                    $"angleRange={minAngle:F1}-{maxAngle:F1}, spin={spinRpm:F0}rpm"
                );
            }
            return false;
        }

        solvedSpeed = solution.v0;
        solvedAngle = solution.theta * Mathf.Rad2Deg;
        return true;
    }

    private ShotSolverComponent GetSolverComponent()
    {
        if (solverComponent != null)
            return solverComponent;

        if (!autoFindSolverComponent)
            return null;

        solverComponent = FindFirstObjectByType<ShotSolverComponent>();
        return solverComponent;
    }

    private Transform ResolveNetPoint()
    {
        if (netPoint != null)
            return netPoint;

        if (!autoFindNetPoint || string.IsNullOrWhiteSpace(netObjectName))
            return null;

        GameObject netObject = GameObject.Find(netObjectName);
        if (netObject == null)
            return null;

        netPoint = netObject.transform;
        return netPoint;
    }

    private bool TryResolveNetBounds(out Bounds bounds)
    {
        bounds = default(Bounds);
        Transform resolvedNetPoint = ResolveNetPoint();
        if (resolvedNetPoint == null)
            return false;

        bool hasBounds = false;
        Renderer[] renderers = resolvedNetPoint.GetComponentsInChildren<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        Collider[] colliders = resolvedNetPoint.GetComponentsInChildren<Collider>();
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null)
                continue;

            if (!hasBounds)
            {
                bounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        return hasBounds;
    }

    private Vector3 ResolveNetWorldPoint()
    {
        if (TryResolveNetBounds(out Bounds bounds))
            return bounds.center;

        Transform resolvedNetPoint = ResolveNetPoint();
        return resolvedNetPoint != null ? resolvedNetPoint.position : Vector3.zero;
    }

    private float ResolveFeedNetHeight()
    {
        if (autoSetFeedNetHeightFromRenderer && TryResolveNetBounds(out Bounds bounds))
        {
            if (float.IsFinite(bounds.max.y) && bounds.max.y > 0.1f)
                return bounds.max.y;
        }

        return feedNetHeight;
    }

    private float ResolveFeedNetMargin()
    {
        if (randomizeFeed)
            return Mathf.Max(feedNetMargin, minimumRandomFeedNetMargin);

        return feedNetMargin;
    }

    private float ResolveNetDistanceAlongShot(Vector3 origin, Vector3 horizontalDirection, float targetDistance)
    {
        Vector3 shotDir = horizontalDirection;
        shotDir.y = 0f;

        if (shotDir.sqrMagnitude < 1e-6f)
            return targetDistance * 0.5f;

        shotDir.Normalize();

        if (ResolveNetPoint() != null)
        {
            Vector3 toNet = ResolveNetWorldPoint() - origin;
            toNet.y = 0f;

            float projected = Vector3.Dot(toNet, shotDir);
            if (projected > 0f && projected < targetDistance)
                return projected;
        }

        return targetDistance * 0.5f;
    }

    private Vector3 ResolveBaseTargetPoint(Vector3 origin, Transform targetOverride, Vector3 fallbackHorizontalDirection)
    {
        if (targetOverride != null)
            return targetOverride.position;

        if (target != null)
            return target.position;

        Vector3 fallback = fallbackHorizontalDirection;
        fallback.y = 0f;

        if (fallback.sqrMagnitude < 1e-6f)
            fallback = launchDirection;

        fallback.y = 0f;

        if (fallback.sqrMagnitude < 1e-6f)
            fallback = Vector3.forward;

        return origin + fallback.normalized * 10f;
    }

    private bool TryPickGridTarget(out Vector3 targetPoint, out int zoneIndex, out Vector2Int zoneCoord)
    {
        targetPoint = Vector3.zero;
        zoneIndex = -1;
        zoneCoord = new Vector2Int(-1, -1);

        if (!TryResolveGridBounds(out Vector2 xRange, out Vector2 zRange))
            return false;

        int picked = PickWeightedZoneIndex();
        int row = picked / 3;
        int column = picked % 3;
        zoneIndex = picked;
        zoneCoord = new Vector2Int(column, row);

        float mirrorCenter = ResolveMirrorCenterX();
        float nearX = Mathf.Abs(xRange.x - mirrorCenter) <= Mathf.Abs(xRange.y - mirrorCenter) ? xRange.x : xRange.y;
        float deepX = Mathf.Approximately(nearX, xRange.x) ? xRange.y : xRange.x;

        float rowStartT = row / 3f;
        float rowEndT = (row + 1) / 3f;
        float xA = Mathf.Lerp(nearX, deepX, rowStartT);
        float xB = Mathf.Lerp(nearX, deepX, rowEndT);
        float xMin = Mathf.Min(xA, xB) + targetCellPadding.x;
        float xMax = Mathf.Max(xA, xB) - targetCellPadding.x;

        float colStartT = column / 3f;
        float colEndT = (column + 1) / 3f;
        float zA = Mathf.Lerp(zRange.x, zRange.y, colStartT);
        float zB = Mathf.Lerp(zRange.x, zRange.y, colEndT);
        float zMin = Mathf.Min(zA, zB) + targetCellPadding.y;
        float zMax = Mathf.Max(zA, zB) - targetCellPadding.y;

        if (xMin > xMax)
        {
            float mid = (xMin + xMax) * 0.5f;
            xMin = mid;
            xMax = mid;
        }

        if (zMin > zMax)
        {
            float mid = (zMin + zMax) * 0.5f;
            zMin = mid;
            zMax = mid;
        }

        targetPoint = new Vector3(Random.Range(xMin, xMax), 0f, Random.Range(zMin, zMax));
        return true;
    }

    private bool TryResolveGridBounds(out Vector2 xRange, out Vector2 zRange)
    {
        Transform minBound = targetMinBound;
        Transform maxBound = targetMaxBound;

        if (TryResolveFourCornerBounds(out xRange, out zRange))
        {
            // Explicit court bounds win. These are the practice-feed target court.
        }
        else
        {
            if ((minBound == null || maxBound == null) && reticleBoundsSource == null && autoFindReticleBounds)
                reticleBoundsSource = FindFirstObjectByType<UIWorldReticle>();

            if ((minBound == null || maxBound == null) && reticleBoundsSource != null)
            {
                minBound = reticleBoundsSource.minBound;
                maxBound = reticleBoundsSource.maxBound;
            }

            if (minBound != null && maxBound != null)
            {
                float minX = Mathf.Min(minBound.position.x, maxBound.position.x);
                float maxX = Mathf.Max(minBound.position.x, maxBound.position.x);
                float minZ = Mathf.Min(minBound.position.z, maxBound.position.z);
                float maxZ = Mathf.Max(minBound.position.z, maxBound.position.z);

                xRange = new Vector2(minX, maxX);
                zRange = new Vector2(minZ, maxZ);
            }
            else
            {
                xRange = new Vector2(Mathf.Min(fallbackTargetXRange.x, fallbackTargetXRange.y), Mathf.Max(fallbackTargetXRange.x, fallbackTargetXRange.y));
                zRange = new Vector2(Mathf.Min(fallbackTargetZRange.x, fallbackTargetZRange.y), Mathf.Max(fallbackTargetZRange.x, fallbackTargetZRange.y));
            }
        }

        if (mirrorReticleBoundsAcrossNet)
        {
            float center = ResolveMirrorCenterX();
            float mirroredA = center * 2f - xRange.x;
            float mirroredB = center * 2f - xRange.y;
            xRange = new Vector2(Mathf.Min(mirroredA, mirroredB), Mathf.Max(mirroredA, mirroredB));
        }

        if (useSymmetricLateralGrid)
        {
            float centerZ = ResolveLateralCenterZ();
            float halfWidth = Mathf.Max(Mathf.Abs(zRange.x - centerZ), Mathf.Abs(zRange.y - centerZ));

            if (halfWidth < 0.1f)
            {
                float fallbackMinZ = Mathf.Min(fallbackTargetZRange.x, fallbackTargetZRange.y);
                float fallbackMaxZ = Mathf.Max(fallbackTargetZRange.x, fallbackTargetZRange.y);
                halfWidth = Mathf.Max(Mathf.Abs(fallbackMinZ - centerZ), Mathf.Abs(fallbackMaxZ - centerZ));
            }

            zRange = new Vector2(centerZ - halfWidth, centerZ + halfWidth);
        }

        return Mathf.Abs(xRange.y - xRange.x) > 0.1f && Mathf.Abs(zRange.y - zRange.x) > 0.1f;
    }

    private bool TryResolveFourCornerBounds(out Vector2 xRange, out Vector2 zRange)
    {
        xRange = Vector2.zero;
        zRange = Vector2.zero;

        Transform[] bounds =
        {
            targetFrontLeftBound,
            targetFrontRightBound,
            targetRearRightBound,
            targetRearLeftBound
        };

        bool found = false;
        float minX = 0f;
        float maxX = 0f;
        float minZ = 0f;
        float maxZ = 0f;

        for (int i = 0; i < bounds.Length; i++)
        {
            Transform bound = bounds[i];
            if (bound == null)
                continue;

            Vector3 p = bound.position;
            if (!found)
            {
                minX = maxX = p.x;
                minZ = maxZ = p.z;
                found = true;
            }
            else
            {
                minX = Mathf.Min(minX, p.x);
                maxX = Mathf.Max(maxX, p.x);
                minZ = Mathf.Min(minZ, p.z);
                maxZ = Mathf.Max(maxZ, p.z);
            }
        }

        if (!found || Mathf.Abs(maxX - minX) < 0.1f || Mathf.Abs(maxZ - minZ) < 0.1f)
            return false;

        xRange = new Vector2(minX, maxX);
        zRange = new Vector2(minZ, maxZ);
        return true;
    }

    private int PickWeightedZoneIndex()
    {
        float[] weights =
        {
            zoneProbabilities.nearLeft,
            zoneProbabilities.nearMiddle,
            zoneProbabilities.nearRight,
            zoneProbabilities.midLeft,
            zoneProbabilities.midMiddle,
            zoneProbabilities.midRight,
            zoneProbabilities.deepLeft,
            zoneProbabilities.deepMiddle,
            zoneProbabilities.deepRight
        };

        if (avoidSameLateralColumnStreaks && lastPickedLateralColumn >= 0 && sameLateralColumnStreak >= Mathf.Max(1, maxSameLateralColumnStreak))
        {
            bool hasOtherColumnWeight = false;
            for (int i = 0; i < weights.Length; i++)
            {
                if (i % 3 != lastPickedLateralColumn && weights[i] > 0f)
                {
                    hasOtherColumnWeight = true;
                    break;
                }
            }

            if (hasOtherColumnWeight)
            {
                for (int i = 0; i < weights.Length; i++)
                {
                    if (i % 3 == lastPickedLateralColumn)
                        weights[i] = 0f;
                }
            }
        }

        int picked = PickWeightedIndex(weights, 7);
        UpdateLateralColumnStreak(picked % 3);
        return picked;
    }

    private void UpdateLateralColumnStreak(int column)
    {
        if (column == lastPickedLateralColumn)
            sameLateralColumnStreak++;
        else
        {
            lastPickedLateralColumn = column;
            sameLateralColumnStreak = 1;
        }
    }

    private FeedShotType PickWeightedShotType()
    {
        float[] weights =
        {
            shotProbabilities.shortFlat,
            shotProbabilities.deepFlat,
            shotProbabilities.topspin,
            shotProbabilities.slice,
            shotProbabilities.lob,
            shotProbabilities.drop
        };

        switch (PickWeightedIndex(weights, 1))
        {
            case 0: return FeedShotType.ShortFlat;
            case 1: return FeedShotType.DeepFlat;
            case 2: return FeedShotType.Topspin;
            case 3: return FeedShotType.Slice;
            case 4: return FeedShotType.Lob;
            case 5: return FeedShotType.Drop;
            default: return FeedShotType.DeepFlat;
        }
    }

    private int PickWeightedIndex(float[] weights, int fallbackIndex)
    {
        float total = 0f;
        for (int i = 0; i < weights.Length; i++)
            total += Mathf.Max(0f, weights[i]);

        if (total <= 0f)
            return Mathf.Clamp(fallbackIndex, 0, weights.Length - 1);

        float roll = Random.value * total;
        float running = 0f;

        for (int i = 0; i < weights.Length; i++)
        {
            running += Mathf.Max(0f, weights[i]);
            if (roll <= running)
                return i;
        }

        return weights.Length - 1;
    }

    private FeedShotProfile GetProfile(FeedShotType shotType)
    {
        switch (shotType)
        {
            case FeedShotType.ShortFlat:
                return shortFlatProfile;
            case FeedShotType.DeepFlat:
                return deepFlatProfile;
            case FeedShotType.Topspin:
                return topspinProfile;
            case FeedShotType.Slice:
                return sliceProfile;
            case FeedShotType.Lob:
                return lobProfile;
            case FeedShotType.Drop:
                return dropProfile;
            default:
                return new FeedShotProfile(
                    new Vector2(launchSpeed, launchSpeed),
                    new Vector2(launchAngle, launchAngle),
                    Vector2.zero
                );
        }
    }

    private bool IsLob(FeedShotType shotType)
    {
        return shotType == FeedShotType.Lob;
    }

    private float ResolveMirrorCenterX()
    {
        if (useNetPointAsMirrorCenter && ResolveNetPoint() != null)
            return ResolveNetWorldPoint().x;

        return targetGridMirrorCenterX;
    }

    private float ResolveLateralCenterZ()
    {
        if (useNetPointAsLateralCenter && ResolveNetPoint() != null)
            return ResolveNetWorldPoint().z;

        return targetGridCenterZ;
    }

    private Vector3 BuildWorldSpinVector(Vector3 horizontalDirection, float spinRpm)
    {
        if (Mathf.Abs(spinRpm) < 0.001f)
            return Vector3.zero;

        Vector3 dir = horizontalDirection;
        dir.y = 0f;

        if (dir.sqrMagnitude < 1e-6f)
            dir = launchDirection;

        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f)
            dir = Vector3.forward;

        dir.Normalize();
        Vector3 axis = Vector3.Cross(Vector3.up, dir);
        if (axis.sqrMagnitude < 1e-6f)
            axis = Vector3.back;

        axis.Normalize();
        return axis * BaseShotLibrary.RpmToRadPerSecond(spinRpm);
    }

    private float RandomRange(Vector2 range)
    {
        float min = Mathf.Min(range.x, range.y);
        float max = Mathf.Max(range.x, range.y);
        return Random.Range(min, max);
    }

    private float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }
}












