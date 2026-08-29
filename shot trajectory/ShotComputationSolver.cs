using UnityEngine;

public class ShotComputationSolver
{
    public enum LiveShotSolveMode
    {
        FixedAngleThenFull,
        FixedAngleOnly
    }

    public struct ShotResult
    {
        public float rawManualV0;
        public float rawRacketSpeed;
        public float actualRacketSpeed;
        public float racketSpeedCap;
        public float backswingRacketCap;
        public float spinRacketCap;
        public float racketDriveSpeed;
        public float backswingScale;
        public float forwardSwingProgress;
        public float forwardSwingScale;
        public float backswingCapSpeed;
        public float manualAfterContactV0;
        public float manualV0;
        public float preMultiplierV0;
        public float targetV0;
        public float targetV0Uncapped;
        public float maxAssistedV0;
        public bool targetSpeedCapped;
        public bool rallyMaxSpeedClamped;
        public float combinedMultiplier;
        public float contactRetention;
        public float spinIntent;
        public float finalV0;
        public float finalTheta;

        public float incomingAlongReturn;
        public float reboundCoefficient;
        public float incomingPaceBonus;
        public float paceFloorScale;
        public float paceFloorV0;
        public bool paceFloorApplied;
        public float minRetainedContactV0;
        public float retainedContactFloorV0;
        public bool retainedContactFloorApplied;

        public float appliedSpin;              // Legacy alias: signed rad/s.
        public float appliedSpinRpm;           // Signed RPM for tuning/debug.
        public float appliedSpinRadPerSecond;  // Signed rad/s used by physics.
        public Vector3 spinVector;             // Legacy alias: rad/s spin vector when known.
        public Vector3 spinRadPerSecond;

        public float playerSpinRpm;
        public float incomingSpinProjectedRpm;
        public float incomingSpinCarryRate;
        public float residualSpinRpm;
        public float contactVulnerability;
        public float incomingSpinAngleBiasDeg;

        public float heightCorrectionDifficulty;
        public float heightCorrectionControlHold;
        public float heightCorrectionBlend;
        public float commonErrorHeightAngleDeg;
        public float correctedHeightAngleDeg;
        public float executedHeightAngleDeg;
        public float missingHeightCorrectionDeg;
        public float correctedNetClearance;
        public float commonErrorNetClearance;
        public float lowContactDifficulty;
        public float incomingSpeedDifficulty;
        public float incomingBackspinDifficulty;
        public float residualSpinDifficulty;
        public float qualityDifficulty;

        public ShotIntent baseIntent;
        public ShotIntent modIntent;

        public float heightIntent;
        public float defaultHeightAngleDeg;
        public float requestedHeightAngleDeg;
        public float minHeightAngleDeg;
        public float maxHeightAngleDeg;
        public bool fixedHeightAngleUsed;
        public bool solverUsed;
        public bool solverTargetExtended;
        public float solverTargetExtensionM;
        public float solverIntendedNetClearance;
        public int solverCandidateCount;
        public float solverSafetyLiftDeg;
        public bool highContactClearanceRetry;
        public float highContactClearanceLiftM;
        public bool solverCacheHit;
        public string solverCacheSource;
    }

    public ShotResult ComputeShot(
        float manualV0,
        float speedBlend,
        float quality,
        BaseShotType baseType,
        ShotModifier modifier,
        float holdScale,
        float minHoldAngleDeg,
        float maxExtraPowerFraction,
        float spinIntent,
        float backswingScale,
        float forwardSwingProgress,
        float backswingCapSpeed,
        float maxShotPower,
        float desiredNetClearance,
        float heightIntent,
        float heightAngleDeg,
        bool useHeightAngleOverride,
        Vector3 incomingVelocity,
        Vector3 incomingSpinRadPerSecond,
        Vector3 returnDirection,
        AimingController aimingController,
        Transform reticle,
        Transform ball,
        LiveShotSolveMode liveSolveMode = LiveShotSolveMode.FixedAngleOnly,
        float absoluteMaxSpeed = -1f)
    {
        float allowedMaxSpeed = absoluteMaxSpeed > 0f ? absoluteMaxSpeed : BaseShotLibrary.RallyMaxSpeedMps;
        ShotResult result = new ShotResult();
        result.solverCacheSource = "none";
        result.solverIntendedNetClearance = float.IsFinite(desiredNetClearance)
            ? Mathf.Max(0f, desiredNetClearance)
            : 0f;
        result.rawManualV0 = manualV0;
        result.spinIntent = Mathf.Clamp01(spinIntent);
        result.heightIntent = Mathf.Clamp01(heightIntent);
        result.backswingScale = Mathf.Clamp01(backswingScale);
        result.forwardSwingProgress = Mathf.Clamp01(forwardSwingProgress);
        result.forwardSwingScale = ComputeForwardSwingScale(result.forwardSwingProgress);
        result.backswingCapSpeed = Mathf.Min(allowedMaxSpeed, Mathf.Max(0f, backswingCapSpeed));

        ShotContactProfile contactProfile = BaseShotLibrary.GetContactProfile(baseType);
        result.contactRetention = contactProfile.ContactRetention(result.spinIntent);
        float quality01 = Mathf.Clamp01(quality);
        float sweetSpotReboundCoefficient = Mathf.Max(0.01f, contactProfile.reboundCoefficient);
        result.reboundCoefficient = ComputeQualityAdjustedReboundCoefficient(sweetSpotReboundCoefficient, quality01);
        result.paceFloorScale = contactProfile.paceFloorScale;
        result.minRetainedContactV0 = Mathf.Max(0f, contactProfile.minRetainedContactV0);

        Vector3 safeReturnDirection = SafeHorizontalDirection(returnDirection, reticle, ball);
        Vector3 outgoingSpinAxis = Vector3.Cross(Vector3.up, safeReturnDirection);
        if (outgoingSpinAxis.sqrMagnitude < 0.0001f)
            outgoingSpinAxis = Vector3.back;
        else
            outgoingSpinAxis.Normalize();

        float sweetSpotDriveMultiplier = 1f + sweetSpotReboundCoefficient;
        float actualDriveMultiplier = 1f + result.reboundCoefficient;
        float safeBackswingCapSpeed = Mathf.Min(
            allowedMaxSpeed,
            result.backswingCapSpeed > 0f ? result.backswingCapSpeed : Mathf.Max(0f, maxShotPower));

        result.rawRacketSpeed = Mathf.Max(0f, manualV0) / sweetSpotDriveMultiplier;
        result.backswingRacketCap = safeBackswingCapSpeed > 0f
            ? safeBackswingCapSpeed / sweetSpotDriveMultiplier
            : float.PositiveInfinity;

        float noRetentionBaseV0 = BaseShotLibrary.BaselineNoBackswingSpeedMps;
        float retainedCapV0 = safeBackswingCapSpeed > noRetentionBaseV0
            ? noRetentionBaseV0 + (safeBackswingCapSpeed - noRetentionBaseV0) * result.contactRetention
            : safeBackswingCapSpeed;
        result.spinRacketCap = retainedCapV0 > 0f
            ? retainedCapV0 / sweetSpotDriveMultiplier
            : 0f;
        result.racketSpeedCap = result.spinRacketCap * result.forwardSwingScale;
        result.actualRacketSpeed = Mathf.Min(result.rawRacketSpeed, result.racketSpeedCap);
        result.racketDriveSpeed = result.actualRacketSpeed * actualDriveMultiplier;
        result.retainedContactFloorV0 = ComputeRetainedContactFloor(result.rawManualV0, result.minRetainedContactV0, quality01);
        if (result.racketDriveSpeed < result.retainedContactFloorV0)
        {
            result.racketDriveSpeed = result.retainedContactFloorV0;
            result.retainedContactFloorApplied = true;
        }

        result.incomingAlongReturn = Mathf.Max(0f, Vector3.Dot(-incomingVelocity, safeReturnDirection));
        result.incomingPaceBonus = result.incomingAlongReturn * result.reboundCoefficient;
        result.manualAfterContactV0 = result.racketDriveSpeed;
        result.manualV0 = result.manualAfterContactV0 + result.incomingPaceBonus;
        result.paceFloorV0 = result.incomingPaceBonus * result.paceFloorScale;

        ShotIntent baseIntent = BaseShotLibrary.Get(baseType);
        ShotIntent modIntent = ModifierLibrary.Get(modifier);
        float contactHeight = ball != null ? ball.position.y : 1f;
        float angleDistanceToNet = aimingController != null && ball != null
            ? Mathf.Abs(aimingController.netX - ball.position.x)
            : float.NaN;
        ShotHeightRange heightRange = BaseShotLibrary.GetClearanceDrivenHeightRange(
            baseType,
            contactHeight,
            aimingController != null ? aimingController.netHeight : float.NaN,
            angleDistanceToNet,
            desiredNetClearance
        );

        result.baseIntent = baseIntent;
        result.modIntent = modIntent;

        float speedMultiplier = baseIntent.speedMultiplier * modIntent.speedMultiplier;
        float hNorm = Mathf.InverseLerp(0.3f, 1.5f, contactHeight);
        float heightSpinMultiplier = (baseType == BaseShotType.Slice || baseType == BaseShotType.Drop)
            ? 1f
            : BaseShotLibrary.GetHeightSpinMultiplier(contactHeight);

        float playerSpinBaseRpm = BaseShotLibrary.GetSpinRpm(baseType, result.spinIntent) + modIntent.spinRpm;
        result.playerSpinRpm = playerSpinBaseRpm * heightSpinMultiplier;
        result.incomingSpinProjectedRpm = GetIncomingSignedSpinRpm(incomingVelocity, incomingSpinRadPerSecond);
        result.incomingSpinCarryRate = BaseShotLibrary.GetIncomingSpinCarryRate(baseType, result.spinIntent);
        result.residualSpinRpm = result.incomingSpinProjectedRpm * result.incomingSpinCarryRate;

        float finalSpinRpm = result.playerSpinRpm + result.residualSpinRpm;
        float finalSpinRadPerSecond = BaseShotLibrary.RpmToRadPerSecond(finalSpinRpm);
        Vector3 worldSpinVector = outgoingSpinAxis * finalSpinRadPerSecond;
        Vector3 solverSpinVector = new Vector3(0f, 0f, -finalSpinRadPerSecond);
        UpdateHeightCorrectionDifficulty(ref result, contactHeight, incomingVelocity, quality01);
        result.correctedNetClearance = ComputeCorrectedNetClearance(desiredNetClearance, result);
        result.commonErrorNetClearance = ComputeCommonErrorNetClearance(result.correctedNetClearance, result);

        result.contactVulnerability = Mathf.Lerp(1.2f, 0.5f, quality01) * Mathf.Lerp(1.0f, 0.4f, result.spinIntent);
        result.incomingSpinAngleBiasDeg = Mathf.Clamp(
            (result.incomingSpinProjectedRpm / 1000f) * 0.65f * result.contactVulnerability,
            -3f,
            3f
        );

        if (baseType == BaseShotType.Slice)
            result.incomingSpinAngleBiasDeg = 0f;

        bool spinOverrideSet = false;
        if (aimingController != null)
        {
            aimingController.SetShotSolveSpinOverride(solverSpinVector);
            spinOverrideSet = true;
        }

        float defaultAngleDeg = heightRange.fallbackDefaultAngleDeg;

        float baseRequestedAngleDeg = useHeightAngleOverride
            ? BaseShotLibrary.GetHeightAngleDeg(baseType, result.heightIntent, defaultAngleDeg)
            : defaultAngleDeg;

        if (float.IsFinite(heightAngleDeg))
            baseRequestedAngleDeg = heightAngleDeg;

        float preferredFixedAngleDeg = ApplyTopspinRacketAngleCompensation(
            baseType,
            baseRequestedAngleDeg,
            Mathf.Max(result.manualV0, safeBackswingCapSpeed),
            solverSpinVector,
            ball,
            aimingController
        );
        float correctedRequestedAngleDeg = preferredFixedAngleDeg + result.incomingSpinAngleBiasDeg;
        float minAllowedAngleDeg = heightRange.MinAngleDeg(defaultAngleDeg);
        float maxAllowedAngleDeg = heightRange.MaxAngleDeg(defaultAngleDeg);
        if (baseType == BaseShotType.Topspin)
            minAllowedAngleDeg = Mathf.Min(minAllowedAngleDeg, preferredFixedAngleDeg);

        bool clearanceFirstAngleSearch = UsesClearanceFirstAngleSearch(baseType) &&
            !float.IsFinite(heightAngleDeg) &&
            Mathf.Abs(result.heightIntent - BaseShotLibrary.DefaultHeightIntent) <= 0.025f;
        float geometricClearanceAngleDeg = ComputeClearanceGeometryAngleDeg(
            ball,
            aimingController,
            result.solverIntendedNetClearance
        );
        float solverPreferredAngleDeg = clearanceFirstAngleSearch && float.IsFinite(geometricClearanceAngleDeg)
            ? geometricClearanceAngleDeg
            : preferredFixedAngleDeg;
        float solverPreferredAngleWithBiasDeg = solverPreferredAngleDeg + result.incomingSpinAngleBiasDeg;

        if (clearanceFirstAngleSearch && float.IsFinite(geometricClearanceAngleDeg))
        {
            minAllowedAngleDeg = Mathf.Min(minAllowedAngleDeg, geometricClearanceAngleDeg);
            maxAllowedAngleDeg = Mathf.Max(maxAllowedAngleDeg, geometricClearanceAngleDeg);
        }

        result.defaultHeightAngleDeg = defaultAngleDeg;
        result.minHeightAngleDeg = minAllowedAngleDeg;
        result.maxHeightAngleDeg = maxAllowedAngleDeg;

        bool neutralValid = false;
        // If the fixed-angle trajectory cannot be solved, the deterministic path must
        // still use the shared contact result (racket drive + retained incoming pace).
        // Starting from raw swipe speed here discarded most incoming pace on fallbacks.
        float workingV0 = result.manualV0;
        float workingTheta = solverPreferredAngleWithBiasDeg * Mathf.Deg2Rad;
        bool deterministicFallbackUsed = false;

        if (aimingController != null && float.IsFinite(solverPreferredAngleDeg))
        {
            if (UsesClearanceFirstAngleSearch(baseType))
            {
                bool solved = TrySolveBoundedClearanceShot(
                    aimingController,
                    ball,
                    reticle,
                    baseType,
                    modifier,
                    solverPreferredAngleDeg,
                    out var boundedShot,
                    out float targetExtensionM,
                    out float safetyLiftDeg,
                    out int candidateCount,
                    out bool netClipSeen);

                // High contact changes the geometry enough that the normal
                // clearance can occasionally leave no viable fixed-angle
                // trajectory, especially when topspin is pulling the ball
                // down before it reaches the net. Keep the normal clearance
                // as the first choice, then raise it only after an actual
                // net-clip failure. The control-hold clearance already feeds
                // result.solverIntendedNetClearance upstream, so it is
                // preserved automatically for this retry.
                if (!solved &&
                    netClipSeen &&
                    ball != null &&
                    ball.position.y >= HighContactViabilityHeight)
                {
                    float baseClearance = result.solverIntendedNetClearance;
                    float maxRetryClearance = baseClearance + HighContactMaxClearanceLiftM;
                    if (float.IsFinite(heightRange.maxNetClearance))
                        maxRetryClearance = Mathf.Min(maxRetryClearance, heightRange.maxNetClearance);

                    for (float retryClearance = baseClearance + HighContactClearanceStepM;
                         retryClearance <= maxRetryClearance + 0.001f && !solved;
                         retryClearance += HighContactClearanceStepM)
                    {
                        float retryAngleDeg = ComputeClearanceGeometryAngleDeg(
                            ball,
                            aimingController,
                            retryClearance);
                        if (!float.IsFinite(retryAngleDeg))
                            continue;

                        bool retrySolved = TrySolveBoundedClearanceShot(
                            aimingController,
                            ball,
                            reticle,
                            baseType,
                            modifier,
                            retryAngleDeg,
                            out var retryShot,
                            out float retryExtensionM,
                            out float retrySafetyLiftDeg,
                            out int retryCandidateCount,
                            out bool retryNetClipSeen);

                        candidateCount += retryCandidateCount;
                        netClipSeen |= retryNetClipSeen;

                        if (!retrySolved)
                            continue;

                        solved = true;
                        boundedShot = retryShot;
                        targetExtensionM = retryExtensionM;
                        safetyLiftDeg = retrySafetyLiftDeg;
                        solverPreferredAngleDeg = retryAngleDeg;
                        result.solverIntendedNetClearance = retryClearance;
                        result.correctedNetClearance = ComputeCorrectedNetClearance(
                            retryClearance,
                            result);
                        result.commonErrorNetClearance = ComputeCommonErrorNetClearance(
                            result.correctedNetClearance,
                            result);
                        result.highContactClearanceRetry = true;
                        result.highContactClearanceLiftM = retryClearance - baseClearance;
                    }
                }

                result.solverCandidateCount = candidateCount;
                result.solverSafetyLiftDeg = safetyLiftDeg;

                if (solved)
                {
                    workingV0 = boundedShot.v0;
                    workingTheta = boundedShot.theta;
                    result.solverTargetExtended = targetExtensionM > 0.001f;
                    result.solverTargetExtensionM = targetExtensionM;
                    result.maxHeightAngleDeg = Mathf.Max(
                        result.maxHeightAngleDeg,
                        solverPreferredAngleDeg + safetyLiftDeg);
                    result.fixedHeightAngleUsed = true;
                    result.solverUsed = true;
                }
                else
                {
                    const float maxSafetyLiftDeg = 3f;
                    float fallbackLiftDeg = netClipSeen ? maxSafetyLiftDeg : 0f;
                    float fallbackAngleDeg = solverPreferredAngleDeg + fallbackLiftDeg;
                    workingTheta = fallbackAngleDeg * Mathf.Deg2Rad;
                    result.solverSafetyLiftDeg = fallbackLiftDeg;
                    result.maxHeightAngleDeg = Mathf.Max(result.maxHeightAngleDeg, fallbackAngleDeg);
                    result.fixedHeightAngleUsed = true;
                    deterministicFallbackUsed = true;
                }
            }
            else
            {
                var fixedAngleShot = aimingController.GetShotParametersForAngleRangeAtTarget(
                    ball.position,
                    reticle.position,
                    baseType,
                    modifier,
                    solverPreferredAngleDeg * Mathf.Deg2Rad,
                    solverPreferredAngleDeg * Mathf.Deg2Rad,
                    solverPreferredAngleDeg * Mathf.Deg2Rad,
                    result.correctedNetClearance,
                    heightRange.maxNetClearance,
                    bypassCache: false,
                    maxExtendedAngleSteps: 0
                );
                result.solverCandidateCount = 1;

                if (float.IsFinite(fixedAngleShot.v0) && float.IsFinite(fixedAngleShot.theta))
                {
                    workingV0 = fixedAngleShot.v0;
                    workingTheta = fixedAngleShot.theta;
                    result.fixedHeightAngleUsed = true;
                    result.solverUsed = true;
                }
                else if (liveSolveMode == LiveShotSolveMode.FixedAngleThenFull)
                {
                    var neutralShot = aimingController.GetFreshShotParametersAtTarget(
                        ball.position,
                        reticle.position,
                        result.correctedNetClearance);
                    float neutralV0 = neutralShot.v0;
                    float neutralTheta = neutralShot.theta;
                    neutralValid = float.IsFinite(neutralV0) && float.IsFinite(neutralTheta);

                    if (neutralValid)
                    {
                        workingV0 = neutralV0;
                        workingTheta = neutralTheta;
                        result.solverUsed = true;
                    }
                }
            }
        }

        if (aimingController != null)
        {
            result.solverCacheSource = aimingController.LastLiveShotSolveSource;
            result.solverCacheHit = aimingController.LastLiveShotSolveUsedCache;
        }

        if (deterministicFallbackUsed)
        {
            result.solverCacheSource = "clearance-deterministic-fallback";
            result.solverCacheHit = false;
        }

        if (useHeightAngleOverride && float.IsFinite(workingTheta))
        {
            float correctedSolvedAngleDeg = workingTheta * Mathf.Rad2Deg;

            if (baseType == BaseShotType.Slice)
            {
                result.correctedHeightAngleDeg = correctedSolvedAngleDeg;
                result.commonErrorHeightAngleDeg = correctedSolvedAngleDeg;
                result.executedHeightAngleDeg = correctedSolvedAngleDeg;
                result.requestedHeightAngleDeg = correctedSolvedAngleDeg;
                result.heightCorrectionControlHold = Mathf.Clamp01(holdScale);
                result.heightCorrectionBlend = 1f;
                result.missingHeightCorrectionDeg = 0f;
                result.commonErrorNetClearance = result.correctedNetClearance;
            }
            else
            {
                float commonErrorAngleDeg = GetCommonErrorHeightAngleDeg(
                    ref result,
                    correctedSolvedAngleDeg,
                    ball,
                    aimingController
                );

                float executedAngleDeg = ApplyControlHeightCorrection(
                    ref result,
                    commonErrorAngleDeg,
                    correctedSolvedAngleDeg,
                    holdScale
                );

                workingTheta = executedAngleDeg * Mathf.Deg2Rad;
            }
        }
        else
        {
            result.correctedHeightAngleDeg = correctedRequestedAngleDeg;
            result.commonErrorHeightAngleDeg = correctedRequestedAngleDeg;
            result.executedHeightAngleDeg = correctedRequestedAngleDeg;
            result.requestedHeightAngleDeg = correctedRequestedAngleDeg;
            result.heightCorrectionControlHold = Mathf.Clamp01(holdScale);
            result.heightCorrectionBlend = 1f;
            result.correctedNetClearance = result.correctedNetClearance > 0f ? result.correctedNetClearance : desiredNetClearance;
            result.commonErrorNetClearance = result.commonErrorNetClearance > 0f ? result.commonErrorNetClearance : result.correctedNetClearance;
        }

        if (spinOverrideSet)
            aimingController.ClearShotSolveSpinOverride();

        workingV0 *= speedMultiplier;

        result.preMultiplierV0 = workingV0;
        result.targetV0Uncapped = workingV0;
        float retainedContactCapV0 = float.IsFinite(result.racketSpeedCap)
            ? Mathf.Max(result.racketSpeedCap * (1f + result.reboundCoefficient), result.retainedContactFloorV0)
            : float.PositiveInfinity;
        result.maxAssistedV0 = float.IsFinite(retainedContactCapV0)
            ? retainedContactCapV0 + result.incomingPaceBonus
            : float.PositiveInfinity;

        if (float.IsFinite(result.maxAssistedV0) && workingV0 > result.maxAssistedV0)
        {
            workingV0 = result.maxAssistedV0;
            result.targetSpeedCapped = true;
        }

        result.combinedMultiplier = speedMultiplier;

        float finalV0 = workingV0;
        float finalTheta = workingTheta;

        if (!float.IsFinite(finalTheta))
        {
            float targetDistance = 8f;
            if (reticle != null && ball != null)
                targetDistance = Mathf.Max(0.001f, Mathf.Abs(reticle.position.x - ball.position.x));

            float fallbackDeg = Mathf.Lerp(8f, 30f, Mathf.Clamp01(targetDistance / 12f));
            float qualityBiasDeg = Mathf.Lerp(0f, -6f, 1f - quality01);
            fallbackDeg = Mathf.Clamp(fallbackDeg + qualityBiasDeg + result.incomingSpinAngleBiasDeg, 8f, 30f);

            finalTheta = fallbackDeg * Mathf.Deg2Rad;
            finalV0 = Mathf.Clamp(
                finalV0,
                6f,
                Mathf.Min(allowedMaxSpeed, float.IsFinite(result.maxAssistedV0) ? result.maxAssistedV0 : allowedMaxSpeed));

            float fallbackAngleDeg = finalTheta * Mathf.Rad2Deg;
            result.correctedHeightAngleDeg = fallbackAngleDeg;
            result.commonErrorHeightAngleDeg = fallbackAngleDeg;
            result.executedHeightAngleDeg = fallbackAngleDeg;
            result.requestedHeightAngleDeg = fallbackAngleDeg;
            result.heightCorrectionBlend = 1f;
            result.correctedNetClearance = result.correctedNetClearance > 0f ? result.correctedNetClearance : desiredNetClearance;
            result.commonErrorNetClearance = result.commonErrorNetClearance > 0f ? result.commonErrorNetClearance : result.correctedNetClearance;
        }

        result.appliedSpin = finalSpinRadPerSecond;
        result.appliedSpinRpm = finalSpinRpm;
        result.appliedSpinRadPerSecond = finalSpinRadPerSecond;
        result.spinVector = worldSpinVector;
        result.spinRadPerSecond = worldSpinVector;

#if UNITY_EDITOR
        Debug.Log(
            $"[SPIN] Final Spin Applied\n" +
            $"  finalSpinRpm={finalSpinRpm:F1}\n" +
            $"  finalSpinRad={finalSpinRadPerSecond:F2}\n" +
            $"  playerSpinRpm={result.playerSpinRpm:F1}\n" +
            $"  incomingSpinProjectedRpm={result.incomingSpinProjectedRpm:F1}\n" +
            $"  carryRate={result.incomingSpinCarryRate:F2}\n" +
            $"  residualSpinRpm={result.residualSpinRpm:F1}\n" +
            $"  contactAngleBias={result.incomingSpinAngleBiasDeg:F2}deg\n" +
            $"  contactVulnerability={result.contactVulnerability:F2}\n" +
            $"  rawRacket={result.rawRacketSpeed:F2}\n" +
            $"  actualRacket={result.actualRacketSpeed:F2}\n" +
            $"  racketCap={result.racketSpeedCap:F2}\n" +
            $"  swingScale={result.forwardSwingScale:F2}\n" +
            $"  racketDrive={result.racketDriveSpeed:F2}\n" +
            $"  heightControlHold={result.heightCorrectionControlHold:F2}\n" +
            $"  heightDifficulty={result.heightCorrectionDifficulty:F2}\n" +
            $"  heightCorrectionBlend={result.heightCorrectionBlend:F2}\n" +
            $"  commonErrorAngle={result.commonErrorHeightAngleDeg:F2}deg\n" +
            $"  correctedAngle={result.correctedHeightAngleDeg:F2}deg\n" +
            $"  executedAngle={result.executedHeightAngleDeg:F2}deg\n" +
            $"  missingLift={result.missingHeightCorrectionDeg:F2}deg\n" +
            $"  correctedClearance={result.correctedNetClearance:F2}m\n" +
            $"  commonClearance={result.commonErrorNetClearance:F2}m\n" +
            $"  spinIntent={result.spinIntent:F2}\n" +
            $"  contactRetention={result.contactRetention:F2}\n" +
            $"  retainedFloor={result.retainedContactFloorV0:F2}\n" +
            $"  retainedFloorApplied={result.retainedContactFloorApplied}\n" +
            $"  manualRaw={result.rawManualV0:F2}\n" +
            $"  manualAfterContact={result.manualAfterContactV0:F2}\n" +
            $"  incomingPaceBonus={result.incomingPaceBonus:F2}\n" +
            $"  availableManual={result.manualV0:F2}\n" +
            $"  maxAssistedAfterContact={result.maxAssistedV0:F2}\n" +
            $"  paceFloor={result.paceFloorV0:F2}\n" +
            $"  targetRaw={result.targetV0Uncapped:F2}\n" +
            $"  targetCapped={result.targetSpeedCapped}\n" +
            $"  highContactClearanceRetry={result.highContactClearanceRetry}\n" +
            $"  highContactClearanceLift={result.highContactClearanceLiftM:F2}\n" +
            $"  contactHeight={contactHeight:F2}m\n" +
            $"  hNorm={hNorm:F2}\n" +
            $"  heightMult={heightSpinMultiplier:F2}\n" +
            $"  baseType={baseType}"
        );
#endif

        result.targetV0 = finalV0;

        float blend = Mathf.Clamp01(speedBlend);
        finalV0 = finalV0 * blend + result.manualV0 * (1f - blend);

        if (finalV0 < result.paceFloorV0)
        {
            finalV0 = result.paceFloorV0;
            result.paceFloorApplied = true;
        }

        float preRallyMaxClampV0 = finalV0;
        finalV0 = Mathf.Min(finalV0, allowedMaxSpeed);
        result.rallyMaxSpeedClamped = preRallyMaxClampV0 > finalV0 + 0.0001f;

        result.finalV0 = finalV0;
        result.finalTheta = finalTheta;

        LogTopspinShapeDiagnostic(
            baseType,
            ball,
            aimingController,
            solverSpinVector,
            baseRequestedAngleDeg,
            preferredFixedAngleDeg,
            correctedRequestedAngleDeg,
            result.targetV0,
            result.finalV0,
            result.finalTheta,
            result.correctedNetClearance,
            result.commonErrorNetClearance,
            result.heightCorrectionBlend,
            result.missingHeightCorrectionDeg,
            result.solverCacheSource
        );

        return result;
    }

    private const int MaxClearanceSolverCandidates = 6;
    private const float MaxClearanceSafetyLiftDeg = 3f;
    private const float HighContactViabilityHeight = 1.5f;
    private const float HighContactClearanceStepM = 0.10f;
    private const float HighContactMaxClearanceLiftM = 0.40f;

    private static bool TrySolveBoundedClearanceShot(
        AimingController aimingController,
        Transform ball,
        Transform reticle,
        BaseShotType baseType,
        ShotModifier modifier,
        float intendedAngleDeg,
        out (float v0, float theta) solvedShot,
        out float extensionM,
        out float safetyLiftDeg,
        out int candidateCount,
        out bool netClipSeen)
    {
        solvedShot = (float.NaN, float.NaN);
        extensionM = 0f;
        safetyLiftDeg = 0f;
        candidateCount = 0;
        netClipSeen = false;
        if (aimingController == null || ball == null || reticle == null)
            return false;

        Vector3 contactWorld = ball.position;
        Vector3 direction = reticle.position - ball.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f)
            return false;
        direction.Normalize();

        UIWorldReticle bounds = reticle.GetComponent<UIWorldReticle>();
        Vector3 deepestTarget = reticle.position;
        float deepestExtensionM = 0f;

        if (TryClearanceAngleCandidate(
                aimingController,
                contactWorld,
                reticle.position,
                baseType,
                modifier,
                intendedAngleDeg,
                ref candidateCount,
                out solvedShot,
                out AimingController.FixedAngleRejectReason rejectReason))
            return true;

        netClipSeen |= IsPhysicalNetClipReason(rejectReason);

        const float deeperStepM = 0.75f;
        const int deeperCandidateCount = 3;
        for (int step = 1; step <= deeperCandidateCount && candidateCount < MaxClearanceSolverCandidates; step++)
        {
            float distance = step * deeperStepM;
            Vector3 deeperTarget = reticle.position + direction * distance;
            if (!IsInsideReticleBounds(deeperTarget, bounds))
                break;

            deepestTarget = deeperTarget;
            deepestExtensionM = distance;
            if (TryClearanceAngleCandidate(
                    aimingController,
                    contactWorld,
                    deeperTarget,
                    baseType,
                    modifier,
                    intendedAngleDeg,
                    ref candidateCount,
                    out solvedShot,
                    out rejectReason))
            {
                extensionM = distance;
                return true;
            }

            netClipSeen |= IsPhysicalNetClipReason(rejectReason);
        }

        if (!netClipSeen)
            return false;

        // The selected normal/safety clearance already owns the base angle. Extra lift is
        // permitted only after the exact trajectory physically clips the net.
        for (int i = 0; i < 2 && candidateCount < MaxClearanceSolverCandidates; i++)
        {
            float liftDeg = i == 0 ? 2.5f : MaxClearanceSafetyLiftDeg;
            if (TryClearanceAngleCandidate(
                    aimingController,
                    contactWorld,
                    deepestTarget,
                    baseType,
                    modifier,
                    intendedAngleDeg + liftDeg,
                    ref candidateCount,
                    out solvedShot,
                    out _))
            {
                extensionM = deepestExtensionM;
                safetyLiftDeg = liftDeg;
                return true;
            }
        }

        return false;
    }

    private static bool TryClearanceAngleCandidate(
        AimingController aimingController,
        Vector3 contactWorld,
        Vector3 target,
        BaseShotType baseType,
        ShotModifier modifier,
        float angleDeg,
        ref int candidateCount,
        out (float v0, float theta) solvedShot,
        out AimingController.FixedAngleRejectReason rejectReason)
    {
        solvedShot = (float.NaN, float.NaN);
        rejectReason = AimingController.FixedAngleRejectReason.Invalid;
        if (candidateCount >= MaxClearanceSolverCandidates || !float.IsFinite(angleDeg))
            return false;

        candidateCount++;
        float angleRad = angleDeg * Mathf.Deg2Rad;
        solvedShot = aimingController.GetShotParametersForAngleRangeAtTarget(
            contactWorld,
            target,
            baseType,
            modifier,
            angleRad,
            angleRad,
            angleRad,
            // A point-mass path at net height still clips with a tennis-ball radius.
            desiredNetClearance: 0.04f,
            maxNetClearance: -1f,
            bypassCache: false,
            maxExtendedAngleSteps: 0
        );
        rejectReason = aimingController.LastFixedAngleRejectReason;
        return float.IsFinite(solvedShot.v0) && float.IsFinite(solvedShot.theta);
    }

    private static bool IsPhysicalNetClipReason(AimingController.FixedAngleRejectReason reason)
    {
        return reason == AimingController.FixedAngleRejectReason.NetClipped ||
               reason == AimingController.FixedAngleRejectReason.NetTooLow;
    }

    private static bool IsInsideReticleBounds(Vector3 point, UIWorldReticle bounds)
    {
        if (bounds == null || bounds.minBound == null || bounds.maxBound == null)
            return true;

        float minX = Mathf.Min(bounds.minBound.position.x, bounds.maxBound.position.x);
        float maxX = Mathf.Max(bounds.minBound.position.x, bounds.maxBound.position.x);
        float minZ = Mathf.Min(bounds.minBound.position.z, bounds.maxBound.position.z);
        float maxZ = Mathf.Max(bounds.minBound.position.z, bounds.maxBound.position.z);
        return point.x >= minX && point.x <= maxX && point.z >= minZ && point.z <= maxZ;
    }

    private static bool UsesClearanceFirstAngleSearch(BaseShotType baseType)
    {
        return baseType != BaseShotType.Lob;
    }

    private static float ComputeClearanceGeometryAngleDeg(
        Transform ball,
        AimingController aimingController,
        float netClearance)
    {
        if (ball == null || aimingController == null)
            return float.NaN;

        float distanceToNet = Mathf.Abs(aimingController.netX - ball.position.x);
        if (!float.IsFinite(distanceToNet) || distanceToNet < 0.05f)
            return float.NaN;

        float targetNetY = aimingController.netHeight + Mathf.Max(0f, netClearance);
        float angleDeg = Mathf.Atan2(targetNetY - ball.position.y, distanceToNet) * Mathf.Rad2Deg;
        return float.IsFinite(angleDeg) ? angleDeg : float.NaN;
    }
    private static void LogTopspinShapeDiagnostic(
        BaseShotType baseType,
        Transform ball,
        AimingController aimingController,
        Vector3 solverSpinVector,
        float rawRacketAngleDeg,
        float compensatedSolverAngleDeg,
        float correctedRequestedAngleDeg,
        float targetV0,
        float finalV0,
        float finalTheta,
        float correctedClearance,
        float commonClearance,
        float heightBlend,
        float missingLiftDeg,
        string cacheSource)
    {
        if (baseType != BaseShotType.Topspin || ball == null || aimingController == null || aimingController.solverComponent == null || aimingController.solverComponent.traj == null)
            return;

        float distanceToNet = Mathf.Abs(aimingController.netX - ball.position.x);
        if (!float.IsFinite(distanceToNet) || distanceToNet < 0.05f)
            return;

        Vector2 startPos = new Vector2(0f, ball.position.y);
        float netHeight = aimingController.netHeight;
        float finalAngleDeg = finalTheta * Mathf.Rad2Deg;
        float finalNoSpinCm = NetClearanceCm(aimingController, startPos, finalV0, finalTheta, distanceToNet, Vector3.zero, netHeight);
        float finalWithSpinCm = NetClearanceCm(aimingController, startPos, finalV0, finalTheta, distanceToNet, solverSpinVector, netHeight);
        float targetNoSpinCm = NetClearanceCm(aimingController, startPos, targetV0, compensatedSolverAngleDeg * Mathf.Deg2Rad, distanceToNet, Vector3.zero, netHeight);
        float targetWithSpinCm = NetClearanceCm(aimingController, startPos, targetV0, compensatedSolverAngleDeg * Mathf.Deg2Rad, distanceToNet, solverSpinVector, netHeight);
        float rawRacketNoSpinCm = NetClearanceCm(aimingController, startPos, targetV0, rawRacketAngleDeg * Mathf.Deg2Rad, distanceToNet, Vector3.zero, netHeight);
        float rawRacketWithSpinCm = NetClearanceCm(aimingController, startPos, targetV0, rawRacketAngleDeg * Mathf.Deg2Rad, distanceToNet, solverSpinVector, netHeight);
        float finalMagnusCm = finalWithSpinCm - finalNoSpinCm;
        float targetMagnusCm = targetWithSpinCm - targetNoSpinCm;
        float rawRacketMagnusCm = rawRacketWithSpinCm - rawRacketNoSpinCm;

        Debug.Log(
            $"[TOPSPIN SHAPE] contactY={ball.position.y:F2}m, distNet={distanceToNet:F2}m, " +
            $"rawRacketAngle={rawRacketAngleDeg:F2}deg, compensatedSolverAngle={compensatedSolverAngleDeg:F2}deg, correctedRequestedAngle={correctedRequestedAngleDeg:F2}deg, finalAngle={finalAngleDeg:F2}deg, " +
            $"targetV0={targetV0:F2}m/s ({targetV0 * 2.23694f:F0}mph), finalV0={finalV0:F2}m/s ({finalV0 * 2.23694f:F0}mph), " +
            $"rawRacketNoSpinClear={rawRacketNoSpinCm:F0}cm, rawRacketWithSpinClear={rawRacketWithSpinCm:F0}cm, rawRacketMagnus={rawRacketMagnusCm:F0}cm, " +
            $"targetNoSpinClear={targetNoSpinCm:F0}cm, targetWithSpinClear={targetWithSpinCm:F0}cm, targetMagnus={targetMagnusCm:F0}cm, " +
            $"finalNoSpinClear={finalNoSpinCm:F0}cm, finalWithSpinClear={finalWithSpinCm:F0}cm, finalMagnus={finalMagnusCm:F0}cm, " +
            $"correctedClearance={correctedClearance * 100f:F0}cm, commonClearance={commonClearance * 100f:F0}cm, heightBlend={heightBlend:F2}, missingLift={missingLiftDeg:F2}deg, cacheSource={(string.IsNullOrEmpty(cacheSource) ? "none" : cacheSource)}"
        );
    }

    private static float NetClearanceCm(
        AimingController aimingController,
        Vector2 startPos,
        float v0,
        float theta,
        float distanceToNet,
        Vector3 spin,
        float netHeight)
    {
        if (!float.IsFinite(v0) || !float.IsFinite(theta) || v0 <= 0.01f)
            return float.NaN;

        float yAtNet = aimingController.solverComponent.traj.GetHeightAtX(startPos, v0, theta, distanceToNet, spin);
        if (!float.IsFinite(yAtNet) || yAtNet <= -100f)
            return float.NaN;

        return (yAtNet - netHeight) * 100f;
    }
    private static float ComputeQualityAdjustedReboundCoefficient(float sweetSpotReboundCoefficient, float quality01)
    {
        float sweet = Mathf.Max(0.01f, sweetSpotReboundCoefficient);
        float mishit = Mathf.Max(0.08f, sweet * 0.55f);
        float q = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(quality01));
        return Mathf.Lerp(mishit, sweet, q);
    }

    private static float ComputeRetainedContactFloor(float rawManualV0, float minRetainedContactV0, float quality01)
    {
        float manual = Mathf.Max(0f, rawManualV0);
        float baseFloor = Mathf.Max(0f, minRetainedContactV0);
        if (manual <= 0f || baseFloor <= 0f)
            return 0f;

        float qualityScale = Mathf.Lerp(
            0.82f,
            1f,
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.85f, Mathf.Clamp01(quality01))));
        return Mathf.Min(manual, baseFloor * qualityScale);
    }

    private static float ComputeForwardSwingScale(float progress)
    {
        float p = Mathf.Clamp01(progress);
        if (p <= 0.001f)
            return 0f;

        return Mathf.Clamp01(0.25f + 0.75f * p);
    }

    private static void UpdateHeightCorrectionDifficulty(
        ref ShotResult result,
        float contactHeight,
        Vector3 incomingVelocity,
        float quality01)
    {
        result.lowContactDifficulty = Mathf.Clamp01(1f - Mathf.InverseLerp(0.45f, 1.20f, contactHeight));
        result.incomingSpeedDifficulty = Mathf.Clamp01(Mathf.InverseLerp(10f, 28f, incomingVelocity.magnitude));
        result.incomingBackspinDifficulty = Mathf.Clamp01(Mathf.InverseLerp(250f, 1800f, Mathf.Max(0f, -result.incomingSpinProjectedRpm)));
        result.residualSpinDifficulty = Mathf.Clamp01(Mathf.InverseLerp(500f, 2600f, Mathf.Abs(result.residualSpinRpm)));
        result.qualityDifficulty = 1f - Mathf.Clamp01(quality01);

        float difficulty =
            result.lowContactDifficulty * 0.55f +
            result.incomingSpeedDifficulty * 0.20f +
            result.incomingBackspinDifficulty * 0.35f +
            result.residualSpinDifficulty * 0.25f +
            result.qualityDifficulty * 0.20f;

        result.heightCorrectionDifficulty = Mathf.Clamp01(difficulty);
    }

    private static float ComputeCorrectedNetClearance(float desiredNetClearance, ShotResult result)
    {
        float safeDesired = Mathf.Max(0.05f, desiredNetClearance);
        float lowScale = Mathf.Lerp(1f, 0.72f, result.lowContactDifficulty);
        float spinScale = Mathf.Lerp(1f, 0.88f, Mathf.Max(result.incomingBackspinDifficulty, result.residualSpinDifficulty * 0.5f));
        float corrected = safeDesired * lowScale * spinScale;
        return Mathf.Clamp(corrected, 0.20f, safeDesired);
    }

    private static float ComputeCommonErrorNetClearance(float correctedNetClearance, ShotResult result)
    {
        float pressure = Mathf.Clamp01(Mathf.Max(
            result.lowContactDifficulty,
            Mathf.Max(result.incomingBackspinDifficulty, result.residualSpinDifficulty * 0.85f)
        ) + result.qualityDifficulty * 0.15f);

        float nearNetClearance = Mathf.Lerp(0.22f, 0.10f, Mathf.Max(result.lowContactDifficulty, result.incomingBackspinDifficulty));
        return Mathf.Lerp(correctedNetClearance, nearNetClearance, pressure);
    }

    private static float ApplyTopspinRacketAngleCompensation(
        BaseShotType baseType,
        float requestedAngleDeg,
        float estimatedLaunchSpeed,
        Vector3 solverSpinVector,
        Transform ball,
        AimingController aimingController)
    {
        if (baseType != BaseShotType.Topspin || ball == null || aimingController == null || aimingController.solverComponent == null || aimingController.solverComponent.traj == null)
            return requestedAngleDeg;

        if (!float.IsFinite(requestedAngleDeg) || solverSpinVector.sqrMagnitude < 0.0001f)
            return requestedAngleDeg;

        float distanceToNet = Mathf.Abs(aimingController.netX - ball.position.x);
        if (!float.IsFinite(distanceToNet) || distanceToNet < 0.25f)
            return requestedAngleDeg;

        float v0 = Mathf.Clamp(Mathf.Max(estimatedLaunchSpeed, BaseShotLibrary.BaselineNoBackswingSpeedMps), 8f, BaseShotLibrary.RallyMaxSpeedMps);
        Vector2 startPos = new Vector2(0f, ball.position.y);
        float targetNoSpinY = aimingController.solverComponent.traj.GetHeightAtX(
            startPos,
            v0,
            requestedAngleDeg * Mathf.Deg2Rad,
            distanceToNet,
            Vector3.zero
        );

        if (!float.IsFinite(targetNoSpinY) || targetNoSpinY <= -100f)
            return requestedAngleDeg;

        float lowDeg = Mathf.Max(-15f, requestedAngleDeg - 30f);
        float highDeg = requestedAngleDeg;
        float lowSpinY = aimingController.solverComponent.traj.GetHeightAtX(startPos, v0, lowDeg * Mathf.Deg2Rad, distanceToNet, solverSpinVector);
        float highSpinY = aimingController.solverComponent.traj.GetHeightAtX(startPos, v0, highDeg * Mathf.Deg2Rad, distanceToNet, solverSpinVector);

        if (!float.IsFinite(lowSpinY) || !float.IsFinite(highSpinY) || lowSpinY <= -100f || highSpinY <= -100f)
            return requestedAngleDeg;

        if (highSpinY <= targetNoSpinY)
            return requestedAngleDeg;

        if (lowSpinY >= targetNoSpinY)
            return lowDeg;

        for (int i = 0; i < 14; i++)
        {
            float midDeg = (lowDeg + highDeg) * 0.5f;
            float midSpinY = aimingController.solverComponent.traj.GetHeightAtX(startPos, v0, midDeg * Mathf.Deg2Rad, distanceToNet, solverSpinVector);

            if (!float.IsFinite(midSpinY) || midSpinY <= -100f)
                break;

            if (midSpinY > targetNoSpinY)
                highDeg = midDeg;
            else
                lowDeg = midDeg;
        }

        return highDeg;
    }
    private static float GetCommonErrorHeightAngleDeg(
        ref ShotResult result,
        float correctedAngleDeg,
        Transform ball,
        AimingController aimingController)
    {
        if (result.heightCorrectionDifficulty <= 0.001f)
            return correctedAngleDeg;

        float distanceToNet = 6f;
        if (aimingController != null && ball != null)
            distanceToNet = Mathf.Max(0.5f, Mathf.Abs(aimingController.netX - ball.position.x));

        float clearanceDrop = Mathf.Max(0f, result.correctedNetClearance - result.commonErrorNetClearance);
        float clearanceAngleDropDeg = Mathf.Atan2(clearanceDrop, distanceToNet) * Mathf.Rad2Deg * 0.85f;

        // Common error means the player did not fully account for awkward residual spin/contact bite.
        float residualSpinDropDeg = Mathf.Clamp(Mathf.Abs(result.residualSpinRpm) / 1000f * 0.65f, 0f, 2f);
        float contactBiasDropDeg = Mathf.Abs(result.incomingSpinAngleBiasDeg) * 0.5f;
        float spinAngleDropDeg = (residualSpinDropDeg + contactBiasDropDeg) * Mathf.Clamp01(result.heightCorrectionDifficulty);

        float commonAngleDeg = correctedAngleDeg - clearanceAngleDropDeg - spinAngleDropDeg;
        return Mathf.Clamp(commonAngleDeg, -2f, correctedAngleDeg);
    }

    private static float ApplyControlHeightCorrection(
        ref ShotResult result,
        float commonErrorAngleDeg,
        float correctedAngleDeg,
        float holdScale)
    {
        result.heightCorrectionControlHold = Mathf.Clamp01(holdScale);

        // Easy balls stay close to the intended/custom solver angle. Difficult balls need control hold to reach it.
        result.heightCorrectionBlend = Mathf.Lerp(1f, result.heightCorrectionControlHold, result.heightCorrectionDifficulty);
        float executedAngleDeg = Mathf.Lerp(commonErrorAngleDeg, correctedAngleDeg, result.heightCorrectionBlend);

        result.correctedHeightAngleDeg = correctedAngleDeg;
        result.commonErrorHeightAngleDeg = commonErrorAngleDeg;
        result.executedHeightAngleDeg = executedAngleDeg;
        result.requestedHeightAngleDeg = executedAngleDeg;
        result.missingHeightCorrectionDeg = Mathf.Max(0f, correctedAngleDeg - executedAngleDeg);

        return executedAngleDeg;
    }

    private static Vector3 SafeHorizontalDirection(Vector3 direction, Transform reticle, Transform ball)
    {
        Vector3 flat = direction;
        flat.y = 0f;
        if (flat.sqrMagnitude >= 0.0001f)
            return flat.normalized;

        if (reticle != null && ball != null)
        {
            flat = reticle.position - ball.position;
            flat.y = 0f;
            if (flat.sqrMagnitude >= 0.0001f)
                return flat.normalized;
        }

        return Vector3.forward;
    }

    private static float GetIncomingSignedSpinRpm(Vector3 incomingVelocity, Vector3 incomingSpinRadPerSecond)
    {
        Vector3 incomingDir = incomingVelocity;
        incomingDir.y = 0f;

        if (incomingDir.sqrMagnitude < 0.0001f || incomingSpinRadPerSecond.sqrMagnitude < 0.0001f)
            return 0f;

        incomingDir.Normalize();
        Vector3 incomingTopspinAxis = Vector3.Cross(Vector3.up, incomingDir);
        if (incomingTopspinAxis.sqrMagnitude < 0.0001f)
            return 0f;

        incomingTopspinAxis.Normalize();
        float signedRadPerSecond = Vector3.Dot(incomingSpinRadPerSecond, incomingTopspinAxis);
        return BaseShotLibrary.RadPerSecondToRpm(signedRadPerSecond);
    }
}
