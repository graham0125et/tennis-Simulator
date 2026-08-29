using UnityEngine;
using System.Collections.Generic;
using static swipeMouseBall;

[DisallowMultipleComponent]
public class TennisAIPlayerController : MonoBehaviour
{
    public enum AIState
    {
        Idle,
        TrackingIncoming,
        SwipePrepared,
        WaitingTightZone,
        HitOrMiss,
        Recover
    }

    // Movement commitment is deliberately independent of return quality. A late
    // ball still deserves a full chase, even when it can only produce a block or
    // a defensive reply.
    private enum PursuitMode
    {
        Comfortable,
        Stretch,
        Emergency
    }

    // Contact quality and movement commitment are deliberately separate. A ball
    // that can be reached but not planted for is still a valid running contact.
    private enum ContactApproachMode
    {
        Planted,
        Running
    }

    private enum IncomingPaceBand
    {
        Slow,
        Moderate,
        Fast
    }

    private enum TacticalMovementClass
    {
        BaselineLateral,
        Retreat,
        StepIn,
        Volley,
        Emergency
    }

    private enum ContactReadinessTier
    {
        Comfortable,
        Planted,
        Running,
        Emergency
    }

    private enum PostBounceReturnProfile
    {
        Normal,
        LiftedRising,
        LowDefensive
    }

    public enum SwipeSkill
    {
        Good,
        Ok,
        Bad
    }

    public enum AIDecisionMode
    {
        Practice,
        Matchplay
    }

    public enum MatchplayRallyState
    {
        Defensive,
        Neutral,
        Offensive
    }

    public enum MatchplayTactic
    {
        DefensiveReset,
        DeepMiddle,
        BodyJammer,
        CrosscourtProbe,
        SameSideRepeat,
        ChangeDirection,
        WideAngle,
        ApproachPressure,
        WinnerAttempt,
        MomentumReversal
    }

    [Header("References")]
    public PlayerMovement movement;
    public hitController hitController;
    public Rigidbody ball;
    public Transform basePosition;
    public bool autoFindBasePosition = true;
    public string basePositionName = "";
    public Transform returnTarget;

    [Header("Optional WTA Observation Logic")]
    [Tooltip("Attach WtaMatchplayObservationLogic to this AI to compare the data-informed WTA behaviour against the ordinary matchplay logic. Disable its component or Apply WTA Matchplay Logic to turn it off instantly.")]
    public WtaMatchplayObservationLogic wtaMatchplayLogic;
    public bool autoFindWtaMatchplayLogic = true;

    [Header("State")]
    public bool aiEnabled = true;
    public AIState state = AIState.Idle;
    public bool debugLogs = false;
    public bool logBounceReplans = true;
    public bool logInterceptSelectionDiagnostics = true;

    [Header("AI Swipe Status Gizmo")]
    public bool showSwipeStatusGizmo = true;
    [Tooltip("Seconds that a green, yellow, red, or black result remains above the AI before returning to a clear outline.")]
    public float swipeStatusGizmoDuration = 3f;
    public float swipeStatusGizmoHeight = 3.45f;
    public float swipeStatusGizmoRadius = 0.42f;
    public float swipeStatusPassedBehindDistance = 1.5f;
    public bool showSwipeStatusText = true;
    public float swipeStatusTextCharacterSize = 0.055f;
    public bool logSwipeStatusTransitions = true;
    [Tooltip("Small tolerance for a first predicted bounce exactly on a legal AI-side singles line.")]
    public float legalBounceBoundaryTolerance = 0.04f;

    [Header("AI Intercept Plan Gizmo")]
    [Tooltip("Draw the current planned ball intercept and the AI's route to its tight-hit-zone stance while this object is selected.")]
    public bool showInterceptPlanGizmo = true;
    public Color interceptPlanRouteColor = new Color(1f, 0.15f, 0.85f, 1f);
    public Color interceptPlanContactColor = new Color(1f, 0.55f, 0.05f, 1f);
    [Tooltip("Draw the best currently viable baseline, retreat, step-in, and volley alternatives as faint route lines.")]
    public bool showTacticalInterceptOptionsGizmo = true;
    public Color baselineOptionColor = new Color(0.10f, 0.95f, 0.95f, 0.55f);
    public Color retreatOptionColor = new Color(0.20f, 0.45f, 1f, 0.55f);
    public Color stepInOptionColor = new Color(1f, 0.85f, 0.10f, 0.55f);
    public Color volleyOptionColor = new Color(1f, 0.25f, 0.10f, 0.55f);

    [Header("Participation")]
    public bool participatesInRally = true;
    public Transform inactivePoint;
    public bool autoFindInactivePoint = true;
    public string inactivePointName = "inactivePoint";
    public float inactiveStopDistance = 0.2f;

    private bool serviceHoldActive;
    private bool serviceReturnPreparationActive;
    private bool serviceReturnHitAllowed = true;
    private Vector3 serviceHoldPosition;

    [Header("Movement")]
    public float moveSpeed = 7f;
    [Tooltip("Minimum acceleration configured on the AI movement motor. This gives an urgent intercept a genuine first-step burst without increasing top speed.")]
    public float aiMinimumAcceleration = 10.5f;
    [Tooltip("Minimum braking strength used to settle before the opponent hits and to change direction for the next ball.")]
    public float aiMinimumDeceleration = 10.5f;
    [Range(0f, 1f)] public float aiMinimumSustainedAccelerationFloor = 0.35f;
    public float contactStopDistance = 0.25f;
    public float recoveryStopDistance = 0.25f;
    public Vector3 bodyOffsetFromContact = new Vector3(0.65f, 0f, 0f);
    public float minimumBodyContactOffset = 0.9f;
    public float contactBrakeBuffer = 0.22f;
    public float recoverySpeedMultiplier = 1.35f;
    public float recoveryBrakeBuffer = 0.08f;

    [Header("Recovery Brake / Settle")]
    [Tooltip("Once the ball is inside the opponent's actual swing zone, limit recovery to a reachable, settled position before the reply. Until then the AI recovers at full pace.")]
    public bool enableRecoveryBrakeSettle = true;
    [Tooltip("Time reserved for the AI to settle before the opponent's predicted contact.")]
    public float recoverySettleLeadTime = 0.35f;
    [Tooltip("Once this much time remains after the opponent can hit, the AI holds its current recovery position and lets the movement motor brake it.")]
    public float recoverySettleMinimumRemainingTime = 0.35f;
    public bool debugRecoveryBrakeSettle = false;

    [Header("Running Contact Planning")]
    [Tooltip("Use a planted contact only when the AI can arrive, brake, and set before the predicted contact. Otherwise it carries its movement through the swept tight-zone contact.")]
    public bool enableRunningContactPlanning = true;
    [Tooltip("Maximum planar speed that still counts as planted at contact.")]
    public float plantedContactMaxResidualSpeed = 0.7f;
    [Tooltip("Small timing allowance when comparing a simulated planted arrival with the predicted ball contact.")]
    public float plantedContactTimingTolerance = 0.04f;
    [Tooltip("The AI requests only the minimum approach speed that can reach a running contact, plus this small safety factor.")]
    [Range(1f, 1.2f)] public float runningContactSpeedSafetyFactor = 1.05f;
    public float runningContactMinimumApproachSpeed = 1.1f;
    [Tooltip("Movement at or below this speed at contact is still treated as a normal shot. Only genuinely fast movement through contact forces a defensive running-contact response.")]
    public float significantRunningContactSpeed = 2.25f;
    [Tooltip("Small numerical allowance when testing whether full-speed movement can reach an intercept. It is time, not extra fictional movement distance.")]
    public float runningContactTimingTolerance = 0.025f;
    [Tooltip("If the remaining arrival slack is at or below this value, command maximum movement speed with no approach slowdown.")]
    public float fullSpeedPursuitSlack = 0.16f;
    public bool debugRunningContactPlanning = false;

    [Header("Persistent Ball Pursuit")]
    [Tooltip("Keep a legal incoming ball chaseable even when the chosen contact is late or stretched.")]
    public bool alwaysPursueLegalIncomingBall = true;
    [Tooltip("How far short of the motor-predicted reach a contact may be before it is classified as an emergency chase.")]
    public float stretchReachDeficit = 0.65f;
    [Tooltip("Score added to contacts that require a last-chance emergency chase. Comfortable contacts remain preferred when available.")]
    public float emergencyReachScorePenalty = 4.5f;
    public float emergencyReachDeficitScore = 1.25f;
    [Tooltip("Near contact, refresh the trajectory plan at this rate while 120 Hz contact observation remains active throughout.")]
    public float closeContactPlanRefreshHz = 120f;
    public float closeContactPlanWindow = 0.75f;

    [Header("Comfortable Baseline Intercepts")]
    [Tooltip("For ordinary post-bounce balls, favour a contact the AI can reach and set for at least this long before the ball arrives.")]
    public float preferredContactArrivalLeadTime = 0.50f;
    [Tooltip("Tactical score per metre that a post-bounce stance is forward of the AI's base line. This keeps normal rally movement along the baseline.")]
    public float postBounceForwardFromBasePenalty = 1.45f;
    [Tooltip("Tactical score per metre that a post-bounce stance is away from the AI's base depth, before lateral movement is considered.")]
    public float postBounceDepthFromBasePenalty = 0.75f;
    [Tooltip("Keep the current movement route unless the new planned stance shifts at least this far, the timing changes materially, or it is a clear comfort upgrade.")]
    public float movementPlanRetargetDistance = 0.55f;
    public float movementPlanRetargetTime = 0.16f;

    [Header("Tactical Intercept Options")]
    [Tooltip("Planar launch pace below this value is slow; values between the slow and fast boundaries are moderate.")]
    public float slowBallMaximumPlanarLaunchSpeedMps = 18f;
    [Tooltip("Planar launch pace at or above this value is fast. 28 m/s is about 63 mph.")]
    public float fastBallMinimumPlanarLaunchSpeedMps = 28f;
    [Tooltip("Stances within this depth of the normal base position are baseline/lateral plans. Deeper stances are retreats; shallower stances are step-ins.")]
    public float tacticalBaselineDepthTolerance = 0.40f;
    [Tooltip("The first bounce must land at least this far inside the normal base depth before a forward movement can count as a genuinely short-ball step-in.")]
    public float shortBallMinimumDepthInsideBase = 2.25f;
    [Tooltip("A moderate-paced step-in needs this planted time margin and must beat the best non-forward score by the configured amount.")]
    public float moderateStepInMinimumPlantLead = 0.42f;
    public float moderateStepInRequiredScoreAdvantage = 0.75f;
    [Tooltip("A slow short ball may select a comfortable step-in when its score is within this tolerance of the best baseline/retreat option.")]
    public float slowStepInMinimumPlantLead = 0.30f;
    public float slowStepInScoreTolerance = 1.25f;
    [Tooltip("Keep a viable tactical movement class through small replans. A class switch or route reversal requires this much extra arrival slack or score benefit.")]
    public float tacticalClassSwitchMinimumSlackGain = 0.18f;
    public float tacticalClassSwitchRequiredScoreGain = 1.0f;
    public float tacticalRouteReversalDotThreshold = -0.10f;
    public float tacticalPlanPathValidityTolerance = 0.55f;
    public float tacticalEarlierContactTimeTolerance = 0.08f;

    [Header("Fast Ball Interception")]
    [Tooltip("Incoming peak speed at which the specialised lateral/backward interception policy begins. 28 m/s is about 63 mph.")]
    public float fastBallInterceptThresholdMps = 28f;
    [Tooltip("Speed at which the specialised interception policy is fully applied. 36 m/s is about 81 mph.")]
    public float fastBallInterceptFullSpeedMps = 36f;
    [Tooltip("At full fast-ball speed, this is the preferred planted lead time. A fixed 0.5 seconds is usually unavailable on an 80 mph return.")]
    public float fastBallPreferredArrivalLeadTime = 0.20f;
    [Tooltip("At full fast-ball speed, prefer setting this far behind the normal base depth while moving laterally to the ball.")]
    public float fastBallPreferredDepthBehindBase = 0.45f;
    [Tooltip("Do not accept the edge of the large tight hit zone while still far from the selected fast-ball intercept.")]
    public bool gateFastBallContactToPlannedIntercept = true;
    public float fastBallPlannedContactTolerance = 0.65f;
    public float fastBallEmergencyContactTolerance = 1.00f;
    public float fastBallContactBailoutTime = 0.07f;
    public float fastBallMovementRetargetDistance = 0.22f;
    public float fastBallMovementRetargetTime = 0.06f;
    public float fastBallFinalInterceptFreezeSeconds = 0.10f;

    [Header("Body Avoidance Return")]
    public bool useLateralContactAdjustment = true;
    [Tooltip("Keeps the incoming ball this far to one side of the AI body at the planned contact.")]
    public float lateralContactOffset = 0.55f;
    public float lateralContactChoiceDeadband = 0.08f;
    public bool hitEarlyWhenBodyCollisionImminent = true;
    public float bodyCollisionLookAheadSeconds = 0.055f;
    public float bodyCollisionSafetyRadius = 0.08f;
    public float bodyCollisionMaximumHitDistance = 1.4f;
    public float bodyCollisionMinimumSwipePreparation = 0.025f;
    public bool debugBodyAvoidanceLogs = false;

    [Header("AI Side Bounds")]
    public bool autoFindAIBounds = true;
    public Transform aiSideFL;
    public Transform aiSideFR;
    public Transform aiSideBL;
    public Transform aiSideBR;
    public string aiSideFLName = "aiSideFL";
    public string aiSideFRName = "aiSideFR";
    public string aiSideBLName = "aiSideBL";
    public string aiSideBRName = "aiSideBR";
    public float sideBoundPadding = 3f;
    [Tooltip("Extra metres allowed behind the AI baseline. At least 8m is always available for deep-ball defence.")]
    public float backBoundPadding = 8f;
    public float baseBehindBackLine = 1f;
    public bool snapToBaseOnStartIfOutsideBounds = true;
    public bool useMirroredAIBoundsForReturnTarget = true;
    public bool mirrorBoundsToBaseSide = true;

    [Header("Incoming Prediction")]
    public float netX = 0f;
    public float predictionSeconds = 3.0f;
    public float predictionStep = 0.02f;
    public float predictionBallRadius = 0.033f;
    public float bounceVelocityRetentionY = 0.72f;
    public float bounceVelocityRetentionXZ = 0.88f;
    public bool preferPostBounceContact = true;
    [Header("Fast Baseline Contact Policy")]
    [Tooltip("From a deep baseline position, preserve a controllable post-bounce contact for a fast incoming ball instead of running forward for an unnecessary volley. A forward pre-bounce contact remains only as an emergency fallback when no bounce plan exists.")]
    public bool preferPostBounceForFastBaselineBalls = true;
    [Tooltip("Incoming speed at which a deep baseline player stops treating a forward volley intercept as the normal option.")]
    public float fastBaselinePostBounceMinimumSpeedMps = 21f;
    [Tooltip("The AI must be at least this far from the net before the fast-baseline no-volley policy applies.")]
    public float fastBaselinePostBounceMinimumDepthFromNet = 6f;
    [Tooltip("A candidate must advance this far toward the net before it is treated as an avoidable forward volley.")]
    public float fastBaselineForwardAdvanceTolerance = 0.35f;
    [Tooltip("Large preference penalty which leaves the forward volley available only if prediction finds no usable post-bounce contact.")]
    public float fastBaselinePreBounceFallbackPenalty = 100f;
    [Tooltip("When no good bounce contact is available, permit any reachable pre-bounce height within the tight hit zone.")]
    public bool allowEmergencyPreBounceContact = true;
    [Tooltip("A forward pre-bounce intercept is only allowed when it is already reachable. If a usable post-bounce path exists, post-bounce remains the default.")]
    public bool requireReachableForwardPreBounceContact = true;
    public float forwardPreBounceAdvanceTolerance = 0.35f;
    public Vector2 idealContactHeightRange = new Vector2(0.75f, 1.15f);
    public Vector2 emergencyContactHeightRange = new Vector2(0.45f, 3.10f);
    [Tooltip("Preferred height for an early standing overhead. Lower reachable balls may still be taken before the bounce when required.")]
    public float standingOverheadContactMinHeight = 1.30f;
    public float reactionTime = 0.18f;

    [Header("Fast Serve Return")]
    public bool fastServeReturnAssist = true;
    public float fastServeSpeedThresholdMps = 23f;
    public float fastServeFullAssistSpeedMps = 31f;
    public float fastServeReactionTime = 0.10f;
    public float fastServeReachToleranceBonus = 0.45f;
    [Range(0f, 1f)] public float fastServeMinimumQuality = 0.68f;
    [Range(0f, 1f)] public float fastReturnFullSpeedMinimumQuality = 0.88f;
    public Vector2 fastReturnTargetDepthRange01 = new Vector2(0.74f, 0.86f);
    [Range(0f, 1f)] public float fastReturnPressureErrorScale = 0.35f;
    [Range(0f, 1f)] public float fastReturnLateralErrorScale = 0.50f;
    [Range(0f, 1f)] public float fastReturnLandingDispersionScale = 0.50f;
    [Range(0f, 1f)] public float fastServeForwardSwingProgress = 0.92f;
    [Tooltip("Start preparing the return this long before the predicted contact instead of waiting for physical swing-zone entry.")]
    public float fastServeSwipePreparationLeadTime = 0.34f;
    [Tooltip("Fast returns use a shorter blocking swipe so the ball cannot cross the contact zone before the swipe completes.")]
    public float fastServeVirtualSwipeDuration = 0.065f;
    [Tooltip("Inside this time-to-contact, keep the prepared fast-return swipe instead of restarting it after replans.")]
    public float fastServeSwipeOverwriteLockTime = 0.22f;
    public bool debugFastServeReturnLogs = false;
    public float reachTolerance = 0.75f;
    public float replanContactDistance = 0.65f;
    public float replanContactTime = 0.18f;
    [Tooltip("Once the ball enters this time window, preserve the first prepared swipe rather than replacing it late.")]
    public float normalSwipeOverwriteLockTime = 0.36f;
    [Tooltip("The initial swipe plus this many total preparations are allowed for one incoming ball. Two permits one early correction.")]
    [Min(1)] public int maxSwipePreparationsPerIncomingBall = 2;
    public bool lockSwipeReplanningAtSwingZoneEntry = true;
    public float approachSlowDownDistance = 2.2f;
    public float minimumApproachSpeed = 2.2f;

    [Header("120 Hz AI Observation")]
    [Tooltip("Run incoming-ball, swing-zone and contact decisions from Unity's fixed 120 Hz physics loop.")]
    public bool processAIControlInFixedUpdate = true;
    [Tooltip("Trajectory planning is heavier than zone observation, so it is capped independently while contact remains 120 Hz.")]
    public float trajectoryPlanRefreshHz = 60f;
    public bool useSweptContactDetection = true;
    public float sweptContactBallRadius = 0.033f;
    public bool logSweptContacts = true;

    [Header("Baseline Volley Control")]
    public bool avoidBaselineVolleys = true;
    public float baselineVolleyMinDepthFromNet = 5.2f;
    public float baselineVolleyPressureBonus = 0.35f;
    public float baselineVolleyRiskBonus = 2f;
    public float baselineVolleyDispersionMultiplier = 1.75f;
    public Vector2 baselineVolleyGoodSpeedRange = new Vector2(11f, 15f);
    public Vector2 baselineVolleyOkSpeedRange = new Vector2(9f, 13f);
    public Vector2 baselineVolleyBadSpeedRange = new Vector2(7f, 11f);
    [Range(0f, 1f)] public float baselineVolleySliceChance = 0.65f;
    [Range(0f, 1f)] public float baselineVolleyHeightIntent = 0.65f;
    public float baselineVolleyQualityPenalty = 0.16f;
    public float baselineVolleyNormDistPenalty = 0.16f;
    public float baselineVolleyLateralErrorMultiplier = 1.45f;
    public float baselineVolleySpeedControlErrorFraction = 0.12f;
    public bool debugVolleyLogs = false;

    [Header("Swing Zones")]
    public float fallbackSwingRadius = 4.5f;
    public Vector3 tightHitZoneLocalOffset = new Vector3(0f, 1.50f, 0f);
    public Vector3 tightHitZoneRadii = new Vector3(1.9f, 1.60f, 2.25f);
    public bool extendTightHitZoneToStandingOverhead = true;
    [Tooltip("Maximum standing overhead racket-contact height relative to the AI root; no jump included.")]
    public float standingOverheadTightZoneTop = 3.10f;
    public bool logTightHitZoneHeight = true;
    [Tooltip("Stop repositioning and commit as soon as an incoming ball enters the tight hit zone on the AI side.")]
    public bool useDescendingTightContactLock = true;
    [Tooltip("Optional. If enabled, only descending tight-zone balls trigger the contact lock.")]
    public bool tightContactLockRequiresDescending = false;
    [Tooltip("Vertical speed at or below which a tight-zone ball is treated as descending when that optional restriction is enabled.")]
    public float tightContactLockMaxVerticalSpeed = 0.10f;
    public float tightHitZonePendingWindow = 1.0f;
    public float virtualSwipeDuration = 0.12f;
    public bool allowSwipeOverwrite = true;
    public float tightHitZoneForwardBias = 0.35f;

    [Header("Post-Bounce Contact Preference")]
    public bool preferFirstBounceApexContact = true;
    [Tooltip("Higher values make the AI favour the first post-bounce apex over a lower, later contact.")]
    public float firstBounceApexVerticalSpeedWeight = 0.70f;
    public bool preferActualBounceRisingContact = true;
    public Vector2 preferredRisingContactHeightRange = new Vector2(0.80f, 1.40f);
    public float risingContactMinimumVerticalSpeed = 0.10f;
    public float closeBounceHalfVolleyWindow = 0.30f;
    public float risingContactPriority = 3.5f;
    public float descendingAfterBouncePenalty = 2.5f;

    [Header("Early Volley Advantage")]
    [Tooltip("Permit an early volley only when the AI can be planted at a high, short-ball contact near the net. Ordinary incoming balls remain post-bounce interceptions.")]
    public bool allowEarlyVolleyForAdvantage = true;
    public float volleyAdvantageMaxDepthFromNet = 3.25f;
    public float volleyAdvantageMinContactHeight = 1.50f;
    [Tooltip("A volley is only an advantage when the AI is already close to its stance; it will not abandon the baseline for a speculative forward run.")]
    public float volleyAdvantageMaxApproachDistance = 1.35f;

    [Header("Fast Low-Bounce Defence")]
    [Tooltip("Use a specialised choice between moving back for a rising ball and taking an immediate low defensive return.")]
    public bool useFastLowBounceDefence = true;
    [Tooltip("Peak incoming pace at which a low first post-bounce contact is treated as a fast defensive situation.")]
    public float lowBounceFastIncomingSpeedMps = 20f;
    [Tooltip("At or below this planned post-bounce height, use the low defensive profile when no better rising contact can be reached.")]
    public float lowBounceDefensiveContactHeight = 0.85f;
    [Tooltip("A reachable rising plan must retain at least this much time before the first tight-zone entry is deferred.")]
    public float lowBounceLiftedContactMinimumLeadTime = 0.07f;
    [Tooltip("How close the live ball must be to the selected rising height before it may be taken from the tight zone.")]
    public float lowBounceLiftedContactHeightTolerance = 0.12f;
    [Range(0f, 1f)] public float lowBounceDefensiveVirtualHold = 0.90f;
    [Tooltip("Additional requested clearance, capped by the active shot type's existing safety clearance.")]
    public float lowBounceSafetyClearanceBonus = 0.35f;
    [Range(0f, 1f)] public float lowBounceMinimumTargetDepth01 = 0.66f;
    [Range(0f, 0.25f)] public float lowBounceTargetSidePadding01 = 0.08f;
    [Range(0f, 1f)] public float lowBounceMinimumQuality = 0.76f;
    [Range(0f, 1f)] public float lowBounceLateralErrorScale = 0.65f;
    [Range(0f, 1f)] public float lowBounceTopspinMinimumSpinIntent = 0.62f;
    public bool logLowBounceReturnDiagnostics = true;

    [Header("Final Intercept Lock")]
    public bool freezeFinalIntercept = true;
    [Range(0.20f, 0.45f)] public float finalInterceptFreezeSeconds = 0.30f;
    public float frozenInterceptExpiryGrace = 0.20f;
    public bool logFinalInterceptFreeze = true;

    [Header("AI Backswing Charge")]
    public float aiBackswingFullChargeSeconds = 1.0f;
    [Range(0f, 1f)] public float aiBackswingDefensiveTarget = 0.55f;
    [Range(0f, 1f)] public float aiBackswingNeutralTarget = 0.85f;
    [Range(0f, 1f)] public float aiBackswingAttackTarget = 1.0f;
    public float aiBackswingFastShotThreshold = 20.5f;
    public float aiBackswingEarlyMinContactTime = 0.45f;
    public float aiBackswingMaxPowerBoost = 0.3334f;
    [Range(0f, 1f)] public float aiBackswingDeceptionChance = 0.22f;
    public bool debugBackswingChargeLogs = false;

    [Header("Return Target")]
    public Vector2 returnTargetXRange = new Vector2(-11.5f, -4.5f);
    public Vector2 returnTargetZRange = new Vector2(-3.8f, 3.8f);
    public float returnTargetY = 0.04f;
    public bool autoFindReturnTargetBounds = true;
    public bool nearSideUsesPlayerReticleBounds = true;
    public bool farSideUsesOurBounds = true;
    public string nearSideBaseName = "basePositionAI_P";
    public UIWorldReticle playerReticleBoundsSource;
    public Transform targetBoundFL;
    public Transform targetBoundFR;
    public Transform targetBoundRR;
    public Transform targetBoundRL;
    public string targetBoundFLName = "AIBoundFL";
    public string targetBoundFRName = "AIBoundFR";
    public string targetBoundRRName = "AIBoundRR";
    public string targetBoundRLName = "AIBoundRL";
    public float targetBoundsPaddingX = 0.45f;
    public float targetBoundsPaddingZ = 0.45f;
    public bool avoidShortReturnTargets = true;
    public float minReturnDepthFromNet = 3.0f;
    public float flatShotMinReturnDepthFromNet = 4.75f;

    [Header("Risk Reward Targeting")]
    public bool useRiskRewardTargeting = true;
    [Range(3, 9)] public int riskGridSize = 5;
    [Range(0f, 1f)] public float safeTargetChance = 0.78f;
    [Range(0f, 1f)] public float neutralTargetChance = 0.18f;
    [Range(0f, 1f)] public float sideLaneTargetChance = 0.34f;
    [Range(0f, 1f)] public float highRiskTestChance = 0.12f;
    public float safeMaxRisk = 5.8f;
    public float neutralMaxRisk = 8.2f;
    public float backCourtPreference = 2.4f;
    public float centerCourtPreference = 0.55f;
    public float sideLanePreference = 2.2f;
    public float targetCellJitter = 0.28f;
    public bool debugTargetRiskLogs = false;

    [Header("Decision Mode")]
    public AIDecisionMode decisionMode = AIDecisionMode.Practice;

    [Header("Practice Mode")]
    public bool practiceMode = true;
    [Range(0f, 1f)] public float practiceVariationChance = 0.18f;
    [Range(0f, 1f)] public float practiceDropShotChance = 0.075f;
    public float practiceDropMaxPressure = 0.48f;
    public float practiceDropMaxIncomingSpeed = 18f;
    public Vector2 practiceDropLandingPastNetBaseline = new Vector2(1.5f, 3.0f);
    public Vector2 practiceDropLandingPastNetMidCourt = new Vector2(1.0f, 2.0f);
    public Vector2 practiceDropLandingPastNetFrontCourt = new Vector2(0.5f, 1.5f);
    public Vector2 practiceDropSpeedRange = new Vector2(2.5f, 4.5f);
    public Vector2 practiceDropHeightIntentRange = new Vector2(0.52f, 0.82f);
    [Range(0f, 1f)] public float practiceDropSpinIntent = 0.9f;
    public float practiceRallyMinDepthFromNet = 4.0f;
    public float practiceRallyBackCourtBias = 0.65f;
    public bool debugPracticeLogs = false;

    [Header("Matchplay Mode")]
    public bool autoFindMatchplayOpponent = true;
    public Transform matchplayOpponent;
    public string matchplayOpponentName = "tennisplayer1";
    public float matchplayOpponentReachSpeed = 7f;
    [Range(0f, 1f)] public float matchplayDefensivePressure = 0.68f;
    [Range(0f, 1f)] public float matchplayOffensivePressure = 0.30f;
    [Range(0f, 1f)] public float matchplayOffensiveChance = 0.35f;
    [Range(0f, 1f)] public float matchplayWinnerChance = 0.18f;
    [Header("Matchplay Attacking Opportunities")]
    [Tooltip("Minimum chance of entering an attacking state when the ball is genuinely attackable. Older serialized Inspector values may be lower.")]
    [Range(0f, 1f)] public float matchplayMinimumOffensiveOpportunityChance = 0.42f;
    [Tooltip("Minimum chance of choosing a winner attempt when the opponent is wide or the AI has a short ball.")]
    [Range(0f, 1f)] public float matchplayMinimumWinnerOpportunityChance = 0.28f;
    [Tooltip("A contact nearer than this to the net is treated as an attacking short-ball opportunity.")]
    public float matchplayShortBallWinnerDepthFromNet = 5.6f;
    [Tooltip("Lets a genuine winner attempt beat the safe deep fallback when projected opponent pressure supports it.")]
    public float matchplayWinnerOpportunityScoreBonus = 0.70f;
    [Range(0f, 1f)] public float matchplayDeepMiddleChance = 0.24f;
    [Range(0f, 1f)] public float matchplayBodyJammerChance = 0.16f;
    [Range(0f, 1f)] public float matchplaySameSideRepeatChance = 0.18f;
    [Range(0f, 1f)] public float matchplayChangeDirectionChance = 0.22f;
    [Range(0f, 1f)] public float matchplayWideAngleChance = 0.24f;
    [Range(0f, 1f)] public float matchplayApproachPressureChance = 0.16f;
    public Vector2 matchplayDefensiveSpeedRange = new Vector2(26.5f, 30.5f);
    public Vector2 matchplayNeutralSpeedRange = new Vector2(29.1f, 33.5f);
    public Vector2 matchplayOffensiveSpeedRange = new Vector2(30.5f, 34.5f);
    public Vector2 matchplayWinnerSpeedRange = new Vector2(32f, 35.3f);
    [Header("Matchplay Rally Pace")]
    [Tooltip("Keeps normal matchplay shot selection in the intended 65-75 mph rally band even when older Inspector values are still serialized on the scene object.")]
    public bool enforceMatchplayRallyPaceEnvelope = true;
    public Vector2 matchplayDefensivePaceEnvelope = new Vector2(26.5f, 30.5f);
    public Vector2 matchplayNeutralPaceEnvelope = new Vector2(29.1f, 33.5f);
    public Vector2 matchplayOffensivePaceEnvelope = new Vector2(30.5f, 34.5f);
    public Vector2 matchplayWinnerPaceEnvelope = new Vector2(32f, 35.3f);
    [Header("Matchplay Probe Variation")]
    [Tooltip("Safe probe targets sit this fraction of a half-court away from centre on the side away from the opponent, rather than aiming blindly at the sideline.")]
    public Vector2 matchplayProbeAwayFromCenterRange01 = new Vector2(0.24f, 0.36f);
    [Tooltip("Lets the chosen tactic survive against an equally safe deep-middle fallback, so neutral rallies retain meaningful variety.")]
    public float matchplaySelectedTacticScoreBonus = 0.48f;
    [Tooltip("Reward for a moderate, safe lateral probe away from the opponent.")]
    public float matchplayProbeValueWeight = 0.55f;
    [Header("Matchplay Rally Safety")]
    public float matchplayMinimumNonDropDepthFromNet = 4.5f;
    public float matchplayBaselineVolleyMinimumDesiredSpeed = 18f;
    [Tooltip("Use the selected net clearance to define the launch angle for normal match shots. Tactical height intent remains available when this is disabled.")]
    public bool matchplayUseClearanceDrivenHeight = true;
    [Tooltip("Incoming shots at or above this peak speed cannot use a drop/touch profile.")]
    public float fastBallNoTouchThresholdMps = 18f;
    public bool logWeakReturnDiagnostics = true;
    public float weakReturnLaunchSpeedThreshold = 20f;
    public bool debugMatchplayLogs = false;

    [Header("Matchplay Response And Recovery")]
    [Tooltip("Reaction allowance used when estimating how soon the human can make the next contact.")]
    public float matchplayOpponentReactionSeconds = 0.22f;
    [Tooltip("Small preparation allowance between the opponent arriving and their next racket contact.")]
    public float matchplayOpponentReturnPreparationSeconds = 0.12f;
    [Tooltip("Time the AI needs immediately after contact before its recovery movement is fully available.")]
    public float matchplayRecoveryContactSettleSeconds = 0.20f;
    [Tooltip("Recovery-margin target below which a defensive recovery ball is preferred.")]
    public float matchplayDefensiveRecoveryMarginTarget = 0.45f;
    public float matchplayRecoveryMarginWeight = 2.25f;
    public float matchplayOpponentPressureWeight = 1.65f;
    public float matchplayOpponentAttackPenalty = 2.0f;
    public float matchplayWideEasyReachPenalty = 1.5f;
    [Tooltip("Slow, high-control range used only when the AI needs time to recover after a running or badly stretched contact.")]
    public Vector2 matchplayRecoverySpeedRange = new Vector2(18.5f, 22.5f);
    [Range(0f, 1f)] public float matchplayRecoveryVirtualControlHold = 0.72f;
    [Tooltip("Extra intended net clearance for a recovery ball. This is added through the normal clearance-driven angle calculation.")]
    public float matchplayRecoverySafetyClearanceBonus = 0.20f;
    [Tooltip("Minimum intended clearance for a selected recovery ball. This is an intended trajectory clearance, not an extra angle lift.")]
    public float matchplayRecoveryMinimumIntendedClearance = 1.20f;
    [Tooltip("Maximum intended clearance for a severely negative recovery margin. Keeps the recovery ball below lob territory while materially buying time.")]
    public float matchplayRecoveryMaximumIntendedClearance = 2.00f;
    [Tooltip("A recovery deficit of this many seconds reaches the maximum recovery clearance.")]
    public float matchplayRecoverySevereMarginSeconds = 1.25f;
    public bool debugMatchplayRecoveryLogs = false;

    [Header("Matchplay Opponent Momentum")]
    [Tooltip("Reward targets which make a moving opponent brake and reverse, but only while the AI itself is not under defensive recovery pressure.")]
    public bool matchplayUseOpponentMomentum = true;
    public float matchplayMomentumMinimumSpeed = 1.25f;
    public float matchplayMomentumTurnSeconds = 0.28f;
    public float matchplayMomentumReversalWeight = 0.85f;
    [Range(0f, 1f)] public float matchplayMomentumReversalChance = 0.22f;

    [Header("Matchplay Contact Diagnostics")]
    [Tooltip("Writes one compact plan/recovery line per incoming shot so planted and running decisions can be checked without enabling verbose logs.")]
    public bool logMatchplayContactPlanDiagnostics = true;

    [Header("AI Reticle Visual")]
    public bool showAIReticle = true;
    public float aiReticleRadius = 0.32f;
    public float aiReticleLineWidth = 0.035f;
    public Color aiReticleColor = new Color(0.1f, 0.9f, 1f, 0.9f);
    public int aiReticleSegments = 48;
    public float aimPreviewInterval = 0.55f;
    public float aiReticleMoveSpeed = 14f;

    [Header("Virtual Swipe")]
    public BaseShotType baseShotType = BaseShotType.Topspin;
    public ShotModifier shotModifier = ShotModifier.Normal;
    public ShotComputationSolver.LiveShotSolveMode liveShotSolveMode = ShotComputationSolver.LiveShotSolveMode.FixedAngleOnly;
    public bool varyShotType = true;
    [Range(0f, 1f)] public float topspinChance = 0.50f;
    [Range(0f, 1f)] public float sliceChance = 0.22f;
    [Range(0f, 1f)] public float flatChance = 0.18f;
    [Range(0f, 1f)] public float customHeightChance = 0.22f;
    public Vector2 customHeightIntentRange = new Vector2(0.25f, 0.82f);
    [Range(0f, 1f)] public float goodSwipeChance = 0.62f;
    [Range(0f, 1f)] public float okSwipeChance = 0.30f;
    public Vector2 goodSpeedRange = new Vector2(22f, 27f);
    public Vector2 okSpeedRange = new Vector2(18f, 23f);
    public Vector2 badSpeedRange = new Vector2(13f, 20f);
    public Vector2 goodLateralErrorDeg = new Vector2(-2f, 2f);
    public Vector2 okLateralErrorDeg = new Vector2(-5f, 5f);
    public Vector2 badLateralErrorDeg = new Vector2(-10f, 10f);
    public Vector2 goodQualityRange = new Vector2(0.82f, 1.0f);
    public Vector2 okQualityRange = new Vector2(0.62f, 0.82f);
    public Vector2 badQualityRange = new Vector2(0.40f, 0.65f);
    [Range(0f, 1f)] public float matchplayNormalForwardSwingProgress = 0.96f;
    [Range(0f, 1f)] public float matchplayDefensiveForwardSwingProgress = 0.86f;
    [Range(0f, 1f)] public float matchplayTouchForwardSwingProgress = 0.68f;
    public bool tightenRallyAccuracy = true;
    [Range(0f, 1f)] public float calmGoodSwipeChance = 0.86f;
    [Range(0f, 1f)] public float calmOkSwipeChance = 0.12f;
    public Vector2 calmGoodSpeedRange = new Vector2(22f, 24.5f);
    public Vector2 calmOkSpeedRange = new Vector2(19.5f, 22.5f);
    public Vector2 calmBadSpeedRange = new Vector2(16f, 20f);
    public Vector2 calmGoodLateralErrorDeg = new Vector2(-0.8f, 0.8f);
    public Vector2 calmOkLateralErrorDeg = new Vector2(-2.0f, 2.0f);
    public Vector2 calmBadLateralErrorDeg = new Vector2(-5.0f, 5.0f);
    public float riskAccuracyPenalty = 0.25f;
    public float pressureAccuracyPenalty = 0.75f;
    public bool compensateSwipeForIncomingPace = true;
    public float incomingPaceCompensationScale = 1f;
    public float incomingSpinSafetySpeedMps = 0.8f;
    public float minCompensatedSwipeSpeed = 1.5f;
    public bool adjustSpinIntentFromLiveIncoming = true;
    public float incomingSpeedSpinIntentBoost = 0.18f;
    public float incomingSpinIntentBoost = 0.22f;
    public Vector2 incomingSpeedSpinIntentRange = new Vector2(12f, 28f);
    public Vector2 incomingSpinIntentRangeRad = new Vector2(35f, 180f);
    public float liveIncomingSpeedReplanDelta = 2.25f;
    public float liveIncomingSpinReplanDelta = 18f;
    public float aiVisualBackswingChargeSeconds = 0.75f;
    public bool debugPaceCompensationLogs = false;
    public bool logPossibleBodyContacts = false;
    public float bodyContactLogRadius = 0.12f;

    [Header("AI Landing Dispersion")]
    public bool useLandingDispersionModel = true;
    public Vector2 topspinSafeDispersion = new Vector2(0.9f, 0.7f);
    public Vector2 topspinAggressiveDispersion = new Vector2(1.45f, 1.15f);
    public Vector2 flatSafeDispersion = new Vector2(1.15f, 0.8f);
    public Vector2 flatAggressiveDispersion = new Vector2(1.8f, 1.35f);
    public Vector2 sliceSafeDispersion = new Vector2(0.8f, 0.6f);
    public Vector2 sliceAggressiveDispersion = new Vector2(1.25f, 0.95f);
    public Vector2 winnerDispersion = new Vector2(2.5f, 2.0f);
    public float pressureDispersionMultiplier = 1.4f;
    public float maxDispersionSigmaSample = 2.25f;
    public bool clampSafeDispersedTargetsToBounds = true;
    public bool debugDispersionLogs = false;
    [Range(0f, 1f)] public float spinIntent = 0.45f;
    [Range(0f, 1f)] public float holdScale = 0.35f;

    private struct ContactPlan
    {
        public bool valid;
        public Vector3 contactPoint;
        public Vector3 stancePoint;
        public float timeUntilContact;
        public float worldContactTime;
        public int bounceCount;
        public int ownSideBounceCount;
        public Vector3 incomingVelocity;
        public Vector3 incomingSpin;
        public IncomingPaceBand paceBand;
        public TacticalMovementClass movementClass;
        public ContactReadinessTier readinessTier;
        public float launchSpeedMps;
        public float launchPlanarSpeedMps;
        public float firstBounceDepthFromNet;
        public float tacticalScore;
        public bool shortBall;
        public PursuitMode pursuitMode;
        public ContactApproachMode approachMode;
        public float requiredMoveDistance;
        public float estimatedReachDistance;
        public float approachMoveSpeed;
        public float minimumApproachSpeed;
        public float estimatedRunningArrivalTime;
        public float estimatedPlantedArrivalTime;
        public float arrivalSlack;
        public bool requiresFullSpeed;
        public bool significantRunningContact;
        public bool IsVolley => ownSideBounceCount <= 0;
    }

    private struct TacticalPlanChoice
    {
        public bool valid;
        public ContactPlan plan;
        public float score;
        public ContactReadinessTier readinessTier;
    }

    private struct IncomingTrajectoryPoint
    {
        public float time;
        public Vector3 position;
        public Vector3 velocity;
        public Vector3 spin;
        public int bounceCount;
        public int ownSideBounceCount;
        public bool isBounce;
        public bool actualBounce;
    }

    private struct TargetPick
    {
        public bool valid;
        public Vector3 position;
        public float risk;
        public float depth01;
        public int gridX;
        public int gridZ;
        public Vector2 xRange;
        public Vector2 zRange;
    }

    private struct MatchplayDecision
    {
        public bool valid;
        public MatchplayRallyState rallyState;
        public MatchplayTactic tactic;
        public Vector3 intendedTarget;
        public float risk;
        public BaseShotType shotType;
        public float heightIntent;
        public bool usesCustomHeightIntent;
        public Vector2 speedRange;
        public float minimumQuality;
        public float qualityBias;
        public float opponentReachSeconds;
        public float ballTravelSeconds;
        public float advantageSeconds;
        public float opponentReturnSeconds;
        public float recoverySeconds;
        public float recoveryMarginSeconds;
        public float opponentAttackPotential;
        public bool runningContact;
        public bool recoveryBall;
        public float safetyClearanceBonus;
        public float recoveryIntendedClearanceFloor;
        public float opponentMomentumReversal01;
        public float opponentMomentumTurnSeconds;
    }

    private ContactPlan currentPlan;
    private SwipeData pendingSwipe;
    private float swipeEndTime;
    private float swipePreparedAt;
    private float tightDeadline;
    private float hitOrMissUntil;
    private Vector3 runtimeBasePosition;
    private bool hasRuntimeBasePosition;
    private bool didHitThisBall;
    private bool abandonedIncomingBall;
    private int trackedBallId;
    private int trackedShotKey = -1;
    private MagnusBallistics predictionBallistics;
    private LineRenderer aiReticleRenderer;
    private Material aiReticleMaterial;
    private Vector3 desiredReturnTargetPosition;
    private bool hasDesiredReturnTargetPosition;
    private float nextAimPreviewTime;
    private TargetPick lastTargetPick;
    private BaseShotType pendingShotType;
    private float pendingHeightIntent;
    private bool pendingUsesCustomHeightIntent;
    private bool pendingPracticeDropShot;
    private bool pendingPracticeVariation;
    private bool lastParticipatesInRally = true;
    private float visualBackswingScale;
    private Vector3 pendingLiveIncomingVelocity;
    private Vector3 pendingLiveIncomingSpin;
    private Vector3 pendingPlannedContactPoint;
    private float pendingPlannedContactWorldTime;
    private float pendingDesiredReturnSpeed;
    private float pendingPressure;
    private float pendingRisk;
    private bool pendingBaselineVolley;
    private MatchplayRallyState pendingRallyState;
    private MatchplayTactic pendingMatchplayTactic;
    private MatchplayTactic selectedMatchplayTactic;
    private bool pendingUsedMatchplayDecision;
    private Vector3 pendingIntendedTarget;
    private Vector3 pendingFinalTarget;
    private float pendingIncomingReferenceSpeed;
    private float pendingContactIncomingSpeed;
    private float pendingExpectedPaceBonus;
    private float pendingIncomingSpinSafety;
    private float pendingFastReturn01;
    private bool pendingFastBallTouchSuppressed;
    private PostBounceReturnProfile pendingPostBounceReturnProfile;
    private float pendingVirtualControlHold;
    private float pendingLowBounceSafetyClearanceBonus;
    private float pendingRecoverySafetyClearanceBonus;
    private float pendingRecoveryIntendedClearanceFloor;
    private ContactApproachMode pendingContactApproachMode;
    private bool pendingSignificantRunningContact;
    private bool pendingRecoveryBall;
    private float pendingOpponentReturnSeconds;
    private float pendingAIRecoverySeconds;
    private float pendingRecoveryMarginSeconds;
    private float recoveryMovementStartedAt = -1f;
    private float pendingOpponentAttackPotential;
    private float pendingOpponentMomentumReversal01;
    private float pendingOpponentMomentumTurnSeconds;
    private float trackedIncomingPeakSpeedMps;
    private float trackedIncomingPeakPlanarSpeedMps;
    private Vector3 trackedIncomingLaunchVelocity;
    private float trackedIncomingLaunchSpeedMps;
    private float trackedIncomingLaunchPlanarSpeedMps;
    private IncomingPaceBand trackedIncomingPaceBand;
    private ContactPlan latestBaselineOption;
    private ContactPlan latestRetreatOption;
    private ContactPlan latestStepInOption;
    private ContactPlan latestVolleyOption;
    private bool aiBackswingCharging;
    private float aiBackswingChargeStartTime;
    private float aiBackswingTargetScale;
    private bool hasLastMatchplayTarget;
    private Vector3 lastMatchplayTarget;
    private float contactLateralSideSign;
    private int lastTightHitZoneHeightLoggedShot = -1;
    private int lastMatchplayContactPlanDiagnosticShotKey = int.MinValue;
    private int lastMatchplayContactPlanDiagnosticBallId = int.MinValue;
    private int lastInterceptSelectionLogShotKey = int.MinValue;
    private float lastInterceptSelectionLogTime = float.NegativeInfinity;
    private Vector3 lastInterceptSelectionLogStance;

    private enum SwipeStatusGizmoState { Clear, Green, Yellow, Red, Black }
    private SwipeStatusGizmoState swipeStatusGizmoState;
    private float swipeStatusGizmoUntil;
    private int swipeStatusShotKey = -1;
    private int swipeStatusShotSequence = -1;
    private int lastReturnBlockedShotKey = -1;
    private int swipePreparationCount;
    private bool swipeStatusTrackingIncomingShot;
    private bool swipeStatusSawTightZone;
    private TextMesh swipeStatusTextMesh;
    private int observedOwnSideBounceShotKey = -1;
    private int observedOwnSideBounceCount;
    private float nextTrajectoryPlanTime;
    private bool finalInterceptFrozen;
    private ContactPlan frozenInterceptPlan;
    private int frozenInterceptShotKey = -1;
    private float frozenInterceptWorldTime;
    private bool hasPhysicsContactSample;
    private int physicsContactSampleShotKey = -1;
    private Vector3 previousPhysicsBallPosition;
    private Vector3 previousPhysicsZoneCenter;
    private Quaternion previousPhysicsZoneRotation = Quaternion.identity;
    private bool sweptContactDetectedThisTick;
    private HitContactConfirmation sweptContactThisTick;
    private int configuredContactZoneControllerId;
    private readonly List<IncomingTrajectoryPoint> incomingTrajectory = new List<IncomingTrajectoryPoint>(192);
    private int incomingTrajectoryShotKey = -1;
    private bool incomingTrajectoryValid;
    private float nextIncomingTrajectoryPredictionTime;
    private Vector3 actualBouncePosition;
    private Vector3 actualBounceVelocityOut;
    private Vector3 actualBounceSpinOut;
    private int actualBounceShotKey = -1;
    private int actualBounceOwnSideCount;

    public bool IsMatchplayMode => decisionMode == AIDecisionMode.Matchplay;
    private bool IsPracticeModeActive => decisionMode == AIDecisionMode.Practice && practiceMode;

    public void SetDecisionMode(AIDecisionMode mode)
    {
        decisionMode = mode;
    }

    public void SetMatchplayMode(bool enabled)
    {
        decisionMode = enabled ? AIDecisionMode.Matchplay : AIDecisionMode.Practice;
    }

    public float CurrentVisualBackswingScale => visualBackswingScale;
    public bool IsInVisualTimingWindow =>
        aiEnabled &&
        participatesInRally &&
        (state == AIState.WaitingTightZone ||
         state == AIState.HitOrMiss ||
         (hitController != null && hitController.ballIsInHittingZone));

    private void Awake()
    {
        EnsureStandingOverheadTightZone();
        EnsureReferences();
        ConfigureSharedAuthoritativeContactZone();
        EnsureReturnTargetBounds();
        EnsureAIBounds();
        ConfigureMovementForAI();
        CacheBasePosition();
        SnapToBaseIfNeeded();
        pendingShotType = baseShotType;
        pendingHeightIntent = BaseShotLibrary.HeightIntent;
        predictionBallistics = new MagnusBallistics();
    }

    private void ConfigureSharedAuthoritativeContactZone()
    {
        if (hitController == null)
            return;

        int controllerId = hitController.GetInstanceID();
        if (configuredContactZoneControllerId == controllerId)
            return;

        hitController.ConfigureAuthoritativeContactZone(
            transform,
            tightHitZoneLocalOffset + Vector3.forward * Mathf.Max(0f, tightHitZoneForwardBias),
            tightHitZoneRadii);
        configuredContactZoneControllerId = controllerId;
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
        emergencyContactHeightRange.y = Mathf.Max(emergencyContactHeightRange.y, upperLimit);

        if (debugLogs)
            Debug.Log($"[TennisAI TIGHT HIT] Vertical reach={lowerLimit:F2}m to {upperLimit:F2}m (standing overhead).");
    }

    private void OnDisable()
    {
        BallController.CourtBounceApplied -= OnCourtBounceApplied;
        if (movement != null)
            movement.ClearExternalMove();
    }

    private void OnEnable()
    {
        BallController.CourtBounceApplied -= OnCourtBounceApplied;
        BallController.CourtBounceApplied += OnCourtBounceApplied;
    }

    private void OnCourtBounceApplied(Rigidbody bouncedBall, string courtName, Vector3 bouncePosition, Vector3 velocityIn, Vector3 velocityOut)
    {
        if (ball == null || bouncedBall != ball)
            return;

        bool bouncedOnOwnSide = IsOnOwnSide(bouncePosition, 0.05f);
        if (!didHitThisBall && !abandonedIncomingBall && bouncedOnOwnSide)
            RecordActualOwnSideBounce(bouncedBall);

        if (bouncedOnOwnSide)
        {
            BallController bouncedController = bouncedBall.GetComponent<BallController>();
            actualBouncePosition = bouncePosition;
            actualBounceVelocityOut = velocityOut;
            actualBounceSpinOut = bouncedController != null ? bouncedController.spinRadPerSecond : Vector3.zero;
            actualBounceShotKey = GetBallShotKey(bouncedBall);
            actualBounceOwnSideCount = GetKnownOwnSideBounceCount();
            incomingTrajectoryValid = false;
        }

        bool eligibleForReplan = !didHitThisBall && !abandonedIncomingBall && bouncedOnOwnSide;
        if (logBounceReplans)
            Debug.Log($"[TennisAI BOUNCE] court={courtName} pos={bouncePosition} vIn={velocityIn} vOut={velocityOut} replan={eligibleForReplan} knownOwnBounces={GetKnownOwnSideBounceCount()}.");
        if (!eligibleForReplan)
            return;

        // The live custom court bounce is more detailed than the planning approximation.
        // Rebuild immediately from its actual post-bounce position, velocity and spin.
        ClearFinalInterceptFreeze("actual court bounce");
        currentPlan = default;
        contactLateralSideSign = 0f;
        RefreshContactPlan(false, true);

        if (logBounceReplans)
            Debug.Log($"[TennisAI BOUNCE REPLAN] court={courtName} pos={bouncePosition} vIn={velocityIn} vOut={velocityOut} plan={(currentPlan.valid ? currentPlan.contactPoint.ToString() : "none")}.");
    }

    private void Update()
    {
        EnsureReferences();
        HandleParticipationTransition();
        UpdateVisualBackswingScale();
        UpdateSwipeStatusText();
        TickAimPreview();
        UpdateAIReticleMotionAndVisual();

        if (!processAIControlInFixedUpdate)
        {
            UpdatePhysicsContactSample();
            TickAIControl();
        }
    }

    private void FixedUpdate()
    {
        if (!processAIControlInFixedUpdate)
            return;

        EnsureReferences();
        HandleParticipationTransition();
        UpdatePhysicsContactSample();
        TickAIControl();
    }

    private void HandleParticipationTransition()
    {
        if (lastParticipatesInRally == participatesInRally)
            return;

        lastParticipatesInRally = participatesInRally;
        currentPlan = default;
        pendingSwipe = default;
        didHitThisBall = false;
        abandonedIncomingBall = false;
        incomingTrajectory.Clear();
        incomingTrajectoryValid = false;
        incomingTrajectoryShotKey = -1;
        actualBounceShotKey = -1;
        actualBounceOwnSideCount = 0;
        recoveryMovementStartedAt = -1f;
        contactLateralSideSign = 0f;
        ClearFinalInterceptFreeze("participation changed");
        ResetPhysicsContactSample();
        state = AIState.Idle;

        if (participatesInRally)
            ConfigureMovementForAI();
    }

    private void TickAIControl()
    {
        if (!aiEnabled || hitController == null || movement == null)
        {
            StopMoving();
            state = AIState.Idle;
            return;
        }

        if (serviceHoldActive)
        {
            TickServiceHold();
            return;
        }

        if (!participatesInRally)
        {
            TickInactive();
            return;
        }

        if (ball == null)
        {
            StopMoving();
            state = AIState.Idle;
            return;
        }

        TrackBallIdentity();
        UpdateTrackedIncomingPeakSpeed();
        UpdateHitEligibilityAfterBallLeavesSide();
        UpdateSwipeStatusGizmoTracking();

        switch (state)
        {
            case AIState.Idle:
                TickIdle();
                break;
            case AIState.TrackingIncoming:
                TickTrackingIncoming();
                break;
            case AIState.SwipePrepared:
                TickSwipePrepared();
                break;
            case AIState.WaitingTightZone:
                TickWaitingTightZone();
                break;
            case AIState.HitOrMiss:
                TickHitOrMiss();
                break;
            case AIState.Recover:
                TickRecover();
                break;
        }
    }

    private void EnsureReferences()
    {
        if (movement == null)
            movement = GetComponent<PlayerMovement>();

        if (autoFindWtaMatchplayLogic && wtaMatchplayLogic == null)
            wtaMatchplayLogic = GetComponent<WtaMatchplayObservationLogic>();

        if (movement != null)
            movement.allowManualInput = false;

        if (autoFindBasePosition && basePosition == null && !string.IsNullOrEmpty(basePositionName))
            basePosition = FindTransform(basePositionName);

        if (autoFindInactivePoint && inactivePoint == null && !string.IsNullOrEmpty(inactivePointName))
            inactivePoint = FindTransform(inactivePointName);

        if (hitController == null)
            hitController = GetComponent<hitController>();

        ConfigureSharedAuthoritativeContactZone();

        if (ball == null)
        {
            BallController ballController = FindFirstObjectByType<BallController>();
            if (ballController != null)
                ball = ballController.GetComponent<Rigidbody>();
        }

        if (hitController != null && hitController.ball == null && ball != null)
            hitController.ball = ball;

        if (returnTarget == null)
            returnTarget = EnsureRuntimeReturnTarget();

        EnsureAIReticleVisual();
    }

    private WtaMatchplayObservationLogic GetActiveWtaMatchplayLogic()
    {
        return wtaMatchplayLogic != null && wtaMatchplayLogic.IsActive
            ? wtaMatchplayLogic
            : null;
    }

    private void TickInactive()
    {
        currentPlan = default;
        pendingSwipe = default;
        didHitThisBall = true;
        abandonedIncomingBall = false;
        hasDesiredReturnTargetPosition = false;
        visualBackswingScale = 0f;
        state = AIState.Idle;

        if (hitController != null)
            hitController.ballIsInHittingZone = false;

        if (aiReticleRenderer != null)
            aiReticleRenderer.enabled = false;

        if (movement != null)
        {
            movement.allowManualInput = false;
            movement.enableRecoveryAssist = false;
            movement.minBound = null;
            movement.maxBound = null;
        }

        if (inactivePoint == null)
        {
            StopMoving();
            return;
        }

        Vector3 target = inactivePoint.position;
        target.y = transform.position.y;
        bool arrived = MoveTowardUnclamped(
            target,
            inactiveStopDistance,
            moveSpeed * Mathf.Max(0.1f, recoverySpeedMultiplier),
            true,
            recoveryBrakeBuffer);

        if (arrived)
            StopMoving();
    }

    private void UpdateVisualBackswingScale()
    {
        float target = 0f;
        if (aiEnabled && participatesInRally)
        {
            switch (state)
            {
                case AIState.TrackingIncoming:
                case AIState.SwipePrepared:
                case AIState.WaitingTightZone:
                    target = ComputeCurrentAIBackswingScale();
                    break;
                case AIState.HitOrMiss:
                    target = Mathf.Clamp01(pendingSwipe.backswingScale);
                    break;
            }
        }

        float chargeSeconds = Mathf.Max(0.02f, aiVisualBackswingChargeSeconds);
        float riseRate = Time.deltaTime / chargeSeconds;
        float fallRate = Time.deltaTime / Mathf.Max(0.05f, chargeSeconds * 0.45f);
        visualBackswingScale = Mathf.MoveTowards(
            visualBackswingScale,
            target,
            target > visualBackswingScale ? riseRate : fallRate);
    }

    public void SnapToBaseFromBounds()
    {
        if (movement == null)
            movement = GetComponent<PlayerMovement>();

        EnsureAIBounds();
        ConfigureMovementForAI();
        CacheBasePosition();
        transform.position = runtimeBasePosition;
        FaceDirection(GetFacingDirection());
    }

    private void EnsureAIBounds()
    {
        if (!autoFindAIBounds)
            return;

        if (aiSideFL == null)
            aiSideFL = FindTransform(aiSideFLName);
        if (aiSideFR == null)
            aiSideFR = FindTransform(aiSideFRName);
        if (aiSideBL == null)
            aiSideBL = FindTransform(aiSideBLName);
        if (aiSideBR == null)
            aiSideBR = FindTransform(aiSideBRName);
    }

    private void EnsureReturnTargetBounds()
    {
        if (!autoFindReturnTargetBounds)
            return;

        if (playerReticleBoundsSource == null)
            playerReticleBoundsSource = FindFirstObjectByType<UIWorldReticle>();

        if (targetBoundFL == null)
            targetBoundFL = FindTransform(targetBoundFLName);
        if (targetBoundFR == null)
            targetBoundFR = FindTransform(targetBoundFRName);
        if (targetBoundRR == null)
            targetBoundRR = FindTransform(targetBoundRRName);
        if (targetBoundRL == null)
            targetBoundRL = FindTransform(targetBoundRLName);
    }

    private void ConfigureMovementForAI()
    {
        if (movement == null)
            return;

        movement.allowManualInput = false;
        movement.enableRecoveryAssist = false;
        movement.moveSpeed = moveSpeed;
        movement.maxAcceleration = Mathf.Max(movement.maxAcceleration, Mathf.Max(0f, aiMinimumAcceleration));
        movement.deceleration = Mathf.Max(movement.deceleration, Mathf.Max(0f, aiMinimumDeceleration));
        movement.sustainedAccelerationFloor = Mathf.Max(
            movement.sustainedAccelerationFloor,
            Mathf.Clamp01(aiMinimumSustainedAccelerationFloor));

        if (!HasAIBounds())
            return;

        if (ShouldMirrorBoundsToBaseSide())
        {
            movement.minBound = null;
            movement.maxBound = null;
            return;
        }

        movement.minBound = aiSideBL;
        movement.maxBound = aiSideFR;
        movement.sideBoundPadding = sideBoundPadding;
        movement.backBoundPadding = Mathf.Max(8f, backBoundPadding);
        movement.backBoundIsMinX = GetBackCenter().x < GetFrontCenter().x;
    }

    private void CacheBasePosition()
    {
        if (basePosition != null)
        {
            runtimeBasePosition = basePosition.position;
            hasRuntimeBasePosition = true;
            return;
        }

        runtimeBasePosition = HasAIBounds() ? GetBasePositionFromBounds() : transform.position;
        hasRuntimeBasePosition = true;
    }

    private void SnapToBaseIfNeeded()
    {
        if (!snapToBaseOnStartIfOutsideBounds || !HasAIBounds())
            return;

        if (!IsInsideMovementBounds(transform.position))
            transform.position = runtimeBasePosition;

        FaceDirection(GetFacingDirection());
    }

    private Transform EnsureRuntimeReturnTarget()
    {
        GameObject existing = GameObject.Find("AI_ReturnTarget_Runtime");
        string targetName = $"AI_ReturnTarget_Runtime_{gameObject.name}";
        GameObject namedExisting = GameObject.Find(targetName);
        if (namedExisting != null)
            return namedExisting.transform;

        existing = GameObject.Find("AI_ReturnTarget_Runtime");
        if (existing != null)
        {
            existing.name = targetName;
            return existing.transform;
        }

        GameObject target = new GameObject(targetName);
        target.hideFlags = HideFlags.DontSave;
        target.transform.position = new Vector3(
            (returnTargetXRange.x + returnTargetXRange.y) * 0.5f,
            returnTargetY,
            0f);
        return target.transform;
    }

    private void EnsureAIReticleVisual()
    {
        if (!showAIReticle || returnTarget == null)
        {
            if (aiReticleRenderer != null)
                aiReticleRenderer.enabled = false;
            return;
        }

        if (aiReticleRenderer == null)
        {
            Transform existing = returnTarget.Find("AI_Reticle_Ring");
            GameObject ring = existing != null ? existing.gameObject : new GameObject("AI_Reticle_Ring");
            ring.transform.SetParent(returnTarget, false);
            ring.transform.localPosition = Vector3.zero;
            ring.transform.localRotation = Quaternion.identity;
            ring.transform.localScale = Vector3.one;

            aiReticleRenderer = ring.GetComponent<LineRenderer>();
            if (aiReticleRenderer == null)
                aiReticleRenderer = ring.AddComponent<LineRenderer>();

            aiReticleRenderer.useWorldSpace = false;
            aiReticleRenderer.loop = true;
            aiReticleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            aiReticleRenderer.receiveShadows = false;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null)
            {
                aiReticleMaterial = new Material(shader);
                aiReticleRenderer.material = aiReticleMaterial;
            }
        }

        aiReticleRenderer.enabled = true;
        aiReticleRenderer.startWidth = aiReticleLineWidth;
        aiReticleRenderer.endWidth = aiReticleLineWidth;
        aiReticleRenderer.startColor = aiReticleColor;
        aiReticleRenderer.endColor = aiReticleColor;
        if (aiReticleMaterial != null)
            aiReticleMaterial.color = aiReticleColor;

        int segments = Mathf.Clamp(aiReticleSegments, 12, 128);
        if (aiReticleRenderer.positionCount != segments)
            aiReticleRenderer.positionCount = segments;

        float radius = Mathf.Max(0.05f, aiReticleRadius);
        for (int i = 0; i < segments; i++)
        {
            float t = (i / (float)segments) * Mathf.PI * 2f;
            aiReticleRenderer.SetPosition(i, new Vector3(Mathf.Cos(t) * radius, 0.03f, Mathf.Sin(t) * radius));
        }
    }

    private void TickAimPreview()
    {
        if (returnTarget == null)
            return;

        bool aimingState =
            state == AIState.TrackingIncoming ||
            state == AIState.SwipePrepared ||
            state == AIState.WaitingTightZone;

        if (!aimingState)
            return;

        if (Time.time < nextAimPreviewTime && hasDesiredReturnTargetPosition)
            return;

        SetDesiredReturnTarget(PickReturnTarget(), false);
        nextAimPreviewTime = Time.time + Mathf.Max(0.05f, aimPreviewInterval);
    }

    private void SetDesiredReturnTarget(Vector3 target, bool snap)
    {
        target.y = returnTargetY;
        desiredReturnTargetPosition = target;
        hasDesiredReturnTargetPosition = true;

        if (returnTarget == null)
            return;

        if (snap)
            returnTarget.position = desiredReturnTargetPosition;
    }

    private void UpdateAIReticleMotionAndVisual()
    {
        if (returnTarget != null && hasDesiredReturnTargetPosition)
        {
            Vector3 current = returnTarget.position;
            Vector3 target = desiredReturnTargetPosition;
            target.y = returnTargetY;

            float step = Mathf.Max(0.1f, aiReticleMoveSpeed) * Time.deltaTime;
            returnTarget.position = Vector3.MoveTowards(current, target, step);
        }

        EnsureAIReticleVisual();
    }

    private void UpdatePhysicsContactSample()
    {
        sweptContactDetectedThisTick = false;
        sweptContactThisTick = default;

        if (ball == null || hitController == null ||
            !hitController.TryGetAuthoritativeContactZonePose(out Vector3 zoneCenter, out Quaternion zoneRotation, out _))
        {
            ResetPhysicsContactSample();
            return;
        }

        int shotKey = GetBallShotKey(ball);
        Vector3 currentBallPosition = ball.position;
        if (!hasPhysicsContactSample || shotKey != physicsContactSampleShotKey)
        {
            hasPhysicsContactSample = true;
            physicsContactSampleShotKey = shotKey;
            previousPhysicsBallPosition = currentBallPosition;
            previousPhysicsZoneCenter = zoneCenter;
            previousPhysicsZoneRotation = zoneRotation;
            return;
        }

        float radius = sweptContactBallRadius > 0f
            ? sweptContactBallRadius
            : hitController.GetBallContactRadius(ball);
        if (useSweptContactDetection &&
            hitController.SweepIntersectsAuthoritativeContactZone(
                previousPhysicsBallPosition,
                currentBallPosition,
                previousPhysicsZoneCenter,
                previousPhysicsZoneRotation,
                radius,
                out Vector3 sweptContactPoint,
                out float sweptFraction))
        {
            sweptContactDetectedThisTick = true;
            sweptContactThisTick = HitContactConfirmation.Confirmed(sweptContactPoint, true);
            if (logSweptContacts && !hitController.IsPointInsideAuthoritativeContactZone(currentBallPosition, radius))
            {
                Debug.Log($"[AI SWEPT CONTACT] shot={swipeStatusShotSequence} point={sweptContactPoint} " +
                    $"fraction={sweptFraction:F3} segment={previousPhysicsBallPosition}->{currentBallPosition}.");
            }
        }

        previousPhysicsBallPosition = currentBallPosition;
        previousPhysicsZoneCenter = zoneCenter;
        previousPhysicsZoneRotation = zoneRotation;
    }

    private void ResetPhysicsContactSample()
    {
        hasPhysicsContactSample = false;
        physicsContactSampleShotKey = -1;
        sweptContactDetectedThisTick = false;
        sweptContactThisTick = default;
    }

    private bool TryGetAuthoritativeContact(out HitContactConfirmation confirmation)
    {
        confirmation = default;
        if (ball == null || hitController == null)
            return false;

        float radius = sweptContactBallRadius > 0f
            ? sweptContactBallRadius
            : hitController.GetBallContactRadius(ball);
        if (hitController.IsPointInsideAuthoritativeContactZone(ball.position, radius))
        {
            confirmation = HitContactConfirmation.Confirmed(ball.position, false);
            return true;
        }

        if (sweptContactDetectedThisTick)
        {
            confirmation = sweptContactThisTick;
            return true;
        }

        return false;
    }

    private void TrackBallIdentity()
    {
        int id = ball != null ? ball.gameObject.GetInstanceID() : 0;
        int shotKey = GetBallShotKey(ball);
        if (id == trackedBallId && shotKey == trackedShotKey)
            return;

        trackedBallId = id;
        trackedShotKey = shotKey;
        bool incoming = IsIncomingBall();
        Vector3 observedVelocity = incoming && ball != null
            ? ball.linearVelocity
            : Vector3.zero;
        BallController ballController = ball != null ? ball.GetComponent<BallController>() : null;
        bool hasExactLaunchSnapshot = incoming && ballController != null &&
            ballController.LastLaunchShotSequence == ballController.ShotSequence &&
            ballController.LastLaunchSpeedMps > 0.01f;
        trackedIncomingLaunchVelocity = hasExactLaunchSnapshot
            ? ballController.LastLaunchVelocity
            : observedVelocity;
        trackedIncomingLaunchSpeedMps = trackedIncomingLaunchVelocity.magnitude;
        trackedIncomingLaunchPlanarSpeedMps = PlanarSpeed(trackedIncomingLaunchVelocity);
        trackedIncomingPeakSpeedMps = Mathf.Max(observedVelocity.magnitude, trackedIncomingLaunchSpeedMps);
        trackedIncomingPeakPlanarSpeedMps = Mathf.Max(PlanarSpeed(observedVelocity), trackedIncomingLaunchPlanarSpeedMps);
        trackedIncomingPaceBand = ClassifyIncomingPace(trackedIncomingLaunchPlanarSpeedMps);
        didHitThisBall = false;
        abandonedIncomingBall = false;
        currentPlan = default;
        latestBaselineOption = default;
        latestRetreatOption = default;
        latestStepInOption = default;
        latestVolleyOption = default;
        contactLateralSideSign = 0f;
        nextTrajectoryPlanTime = 0f;
        incomingTrajectory.Clear();
        incomingTrajectoryValid = false;
        incomingTrajectoryShotKey = shotKey;
        actualBounceShotKey = -1;
        actualBounceOwnSideCount = 0;
        ClearFinalInterceptFreeze("new shot sequence");
        if (state != AIState.Recover)
            state = AIState.Idle;

        // Begin from zero at the opponent's racket contact and accumulate at
        // the same configured charge speed as the player. This only opens the
        // available cap; the later tactical decision still chooses shot pace.
        ResetAIBackswingCharge();
        if (incoming)
        {
            StartOrRaiseAIBackswingCharge(1f, "opponent shot launched");
            if (logInterceptSelectionDiagnostics)
            {
                Debug.Log($"[AI INCOMING LAUNCH] shot={shotKey} pace={trackedIncomingPaceBand} " +
                    $"launch={trackedIncomingLaunchSpeedMps:F2}m/s ({trackedIncomingLaunchSpeedMps * 2.23694f:F0}mph) " +
                    $"planar={trackedIncomingLaunchPlanarSpeedMps:F2}m/s ({trackedIncomingLaunchPlanarSpeedMps * 2.23694f:F0}mph) " +
                    $"velocity={trackedIncomingLaunchVelocity} exact={hasExactLaunchSnapshot}.");
            }
        }
    }

    private void UpdateTrackedIncomingPeakSpeed()
    {
        if (ball == null || !IsIncomingBall())
            return;

        trackedIncomingPeakSpeedMps = Mathf.Max(
            trackedIncomingPeakSpeedMps,
            ball.linearVelocity.magnitude);
        trackedIncomingPeakPlanarSpeedMps = Mathf.Max(
            trackedIncomingPeakPlanarSpeedMps,
            PlanarSpeed(ball.linearVelocity));
    }

    private float GetIncomingReferenceSpeedMps()
    {
        float liveSpeed = currentPlan.valid
            ? currentPlan.incomingVelocity.magnitude
            : ball != null ? ball.linearVelocity.magnitude : 0f;
        return Mathf.Max(
            trackedIncomingLaunchSpeedMps,
            Mathf.Max(liveSpeed, trackedIncomingPeakSpeedMps));
    }

    private float GetIncomingTacticalPaceMps()
    {
        // Tactical intent is fixed from racket launch, so drag, gravity and the
        // bounce cannot make one incoming shot switch pace bands mid-flight.
        return trackedIncomingLaunchPlanarSpeedMps > 0.01f
            ? trackedIncomingLaunchPlanarSpeedMps
            : trackedIncomingPeakPlanarSpeedMps;
    }

    private IncomingPaceBand ClassifyIncomingPace(float planarLaunchSpeedMps)
    {
        float slowMaximum = Mathf.Max(0f, slowBallMaximumPlanarLaunchSpeedMps);
        float fastMinimum = Mathf.Max(slowMaximum + 0.01f, fastBallMinimumPlanarLaunchSpeedMps);
        if (planarLaunchSpeedMps < slowMaximum)
            return IncomingPaceBand.Slow;
        if (planarLaunchSpeedMps >= fastMinimum)
            return IncomingPaceBand.Fast;
        return IncomingPaceBand.Moderate;
    }

    private static float PlanarSpeed(Vector3 velocity)
    {
        velocity.y = 0f;
        return velocity.magnitude;
    }

    private void UpdateHitEligibilityAfterBallLeavesSide()
    {
        if (!didHitThisBall || ball == null)
            return;

        if (IsOnOwnSide(ball.position, 0.35f))
            return;

        didHitThisBall = false;
        abandonedIncomingBall = false;
        currentPlan = default;
        contactLateralSideSign = 0f;
    }

    private void UpdateSwipeStatusGizmoTracking()
    {
        if (swipeStatusGizmoState != SwipeStatusGizmoState.Clear && Time.time > swipeStatusGizmoUntil)
            swipeStatusGizmoState = SwipeStatusGizmoState.Clear;

        if (ball == null)
            return;

        BallController controller = ball.GetComponent<BallController>();
        int shotSequence = controller != null ? controller.ShotSequence : 0;
        int shotKey = controller != null ? (controller.GetInstanceID() * 397) ^ shotSequence : 0;
        bool incoming = IsIncomingBall();

        if (incoming && shotKey != swipeStatusShotKey)
        {
            swipeStatusShotKey = shotKey;
            swipeStatusShotSequence = shotSequence;
            swipePreparationCount = 0;
            swipeStatusSawTightZone = false;
            swipeStatusTrackingIncomingShot = true;
            LogSwipeStatusTransition("TRACK", "incoming shot detected");
        }

        if (!swipeStatusTrackingIncomingShot || didHitThisBall || abandonedIncomingBall)
            return;

        if (IsBallInTightHitZone())
            swipeStatusSawTightZone = true;

        float sideSign = GetAISideSign();
        bool passedBehindAI = sideSign * (ball.position.x - transform.position.x) > Mathf.Max(0.1f, swipeStatusPassedBehindDistance);
        if (passedBehindAI || !incoming)
        {
            string reason = passedBehindAI
                ? "ball passed behind AI"
                : "ball stopped being incoming before contact";
            SetSwipeStatusGizmo(swipeStatusSawTightZone ? SwipeStatusGizmoState.Red : SwipeStatusGizmoState.Black, reason);
            swipeStatusTrackingIncomingShot = false;
        }
    }

    private void RecordSwipePreparationForGizmo()
    {
        if (!swipeStatusTrackingIncomingShot)
            return;

        swipePreparationCount++;
        if (swipePreparationCount > 1)
            SetSwipeStatusGizmo(SwipeStatusGizmoState.Yellow, "contact plan changed; early re-swipe");
    }

    private void RecordSwipeCompletionForGizmo()
    {
        if (!swipeStatusTrackingIncomingShot)
            return;

        if (swipeStatusGizmoState != SwipeStatusGizmoState.Yellow)
            SetSwipeStatusGizmo(SwipeStatusGizmoState.Green, "virtual swipe ready; waiting for tight-zone contact");
    }

    private void RecordSwipeMissForGizmo(string reason)
    {
        if (!swipeStatusTrackingIncomingShot)
            return;

        SetSwipeStatusGizmo(swipeStatusSawTightZone ? SwipeStatusGizmoState.Red : SwipeStatusGizmoState.Black, reason);
        swipeStatusTrackingIncomingShot = false;
    }

    private void SetSwipeStatusGizmo(SwipeStatusGizmoState value, string reason)
    {
        bool changed = swipeStatusGizmoState != value;
        swipeStatusGizmoState = value;
        swipeStatusGizmoUntil = Time.time + Mathf.Max(0.1f, swipeStatusGizmoDuration);
        if (changed)
            LogSwipeStatusTransition(GetSwipeStatusLabel(value), reason);
    }

    private void LogSwipeStatusTransition(string status, string reason)
    {
        if (!logSwipeStatusTransitions || ball == null)
            return;

        BallController controller = ball.GetComponent<BallController>();
        int liveShotSequence = controller != null ? controller.ShotSequence : 0;
        int shotSequence = swipeStatusShotSequence >= 0 ? swipeStatusShotSequence : liveShotSequence;
        int liveBounces = controller != null ? controller.CourtBouncesSinceLastHit : -1;
        int knownOwnBounces = GetKnownOwnSideBounceCount();
        string plan = currentPlan.valid
            ? $"planContact={currentPlan.contactPoint} stance={currentPlan.stancePoint} t={currentPlan.timeUntilContact:F2}s predictedOwnBounces={currentPlan.ownSideBounceCount} pace={currentPlan.paceBand} movement={currentPlan.movementClass} launch={currentPlan.launchSpeedMps:F1}m/s pursuit={currentPlan.pursuitMode} move={currentPlan.requiredMoveDistance:F2}m reach={currentPlan.estimatedReachDistance:F2}m"
            : "plan=none";
        Debug.Log($"[AI SWIPE STATUS] shot={shotSequence} liveShot={liveShotSequence} status={status} reason=\"{reason}\" state={state} " +
            $"knownOwnBounces={knownOwnBounces} prepared={swipePreparationCount} tightSeen={swipeStatusSawTightZone} " +
            $"ball={ball.position} velocity={ball.linearVelocity} {plan}.");
    }

    private void UpdateSwipeStatusText()
    {
        if (swipeStatusGizmoState != SwipeStatusGizmoState.Clear && Time.time > swipeStatusGizmoUntil)
            swipeStatusGizmoState = SwipeStatusGizmoState.Clear;

        if (!showSwipeStatusText)
        {
            if (swipeStatusTextMesh != null)
                swipeStatusTextMesh.gameObject.SetActive(false);
            return;
        }

        if (swipeStatusGizmoState == SwipeStatusGizmoState.Clear)
        {
            if (swipeStatusTextMesh != null)
                swipeStatusTextMesh.gameObject.SetActive(false);
            return;
        }

        EnsureSwipeStatusText();
        if (swipeStatusTextMesh == null)
            return;

        swipeStatusTextMesh.gameObject.SetActive(true);
        swipeStatusTextMesh.text = GetSwipeStatusLabel(swipeStatusGizmoState);
        swipeStatusTextMesh.color = GetSwipeStatusColor(swipeStatusGizmoState);
        swipeStatusTextMesh.transform.position = transform.position + Vector3.up * (Mathf.Max(0.1f, swipeStatusGizmoHeight) + 0.18f);

        Camera camera = Camera.main;
        if (camera != null)
        {
            Vector3 toCamera = camera.transform.position - swipeStatusTextMesh.transform.position;
            if (toCamera.sqrMagnitude > 0.0001f)
                swipeStatusTextMesh.transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
        }
    }

    private void EnsureSwipeStatusText()
    {
        if (swipeStatusTextMesh != null)
            return;

        GameObject statusObject = new GameObject("AI Swipe Status Text");
        statusObject.layer = gameObject.layer;
        statusObject.transform.SetParent(transform, false);
        swipeStatusTextMesh = statusObject.AddComponent<TextMesh>();
        swipeStatusTextMesh.anchor = TextAnchor.LowerCenter;
        swipeStatusTextMesh.alignment = TextAlignment.Center;
        swipeStatusTextMesh.fontSize = 48;
        swipeStatusTextMesh.characterSize = Mathf.Max(0.01f, swipeStatusTextCharacterSize);
        swipeStatusTextMesh.text = string.Empty;
    }

    private static string GetSwipeStatusLabel(SwipeStatusGizmoState value)
    {
        return value switch
        {
            SwipeStatusGizmoState.Green => "SWIPE",
            SwipeStatusGizmoState.Yellow => "RESWIPE",
            SwipeStatusGizmoState.Red => "MISSED",
            SwipeStatusGizmoState.Black => "NO ZONE",
            _ => string.Empty
        };
    }

    private static Color GetSwipeStatusColor(SwipeStatusGizmoState value)
    {
        return value switch
        {
            SwipeStatusGizmoState.Green => Color.green,
            SwipeStatusGizmoState.Yellow => Color.yellow,
            SwipeStatusGizmoState.Red => Color.red,
            SwipeStatusGizmoState.Black => Color.black,
            _ => new Color(1f, 1f, 1f, 0.35f)
        };
    }

    private void TickIdle()
    {
        hitController.ballIsInHittingZone = false;

        if (IsIncomingBall())
        {
            ChangeState(AIState.TrackingIncoming, "incoming ball detected");
            return;
        }

        TickRecoverMovement(false);
    }

    private void TickTrackingIncoming()
    {
        if (!IsIncomingBall())
        {
            ChangeState(AIState.Recover, "ball no longer incoming");
            return;
        }

        RefreshContactPlan(false);
        UpdateEarlyAIBackswingCharge();

        if (TryGetTightContactLock(out HitContactConfirmation contact))
        {
            StopForPlantedContact();
            PrepareVirtualSwipe("ball entered tight hit zone");
            HitWithPendingSwipe(contact);
            return;
        }

        MoveTowardCurrentPlan();

        if (IsBallInSwingZone())
            PrepareVirtualSwipe("ball entered swing zone");
        else if (ShouldPreArmFastServeSwipe())
            PrepareVirtualSwipe("fast serve predicted contact pre-arm");
    }

    private void TickSwipePrepared()
    {
        if (!IsIncomingBall())
        {
            ChangeState(AIState.Recover, "ball no longer incoming during swipe");
            return;
        }

        RefreshContactPlan(true);
        bool contactLocked = TryGetTightContactLock(out HitContactConfirmation contact);
        if (contactLocked)
        {
            StopForPlantedContact();
            HitWithPendingSwipe(contact);
            return;
        }
        else
            MoveTowardCurrentPlan();

        if (!contactLocked && allowSwipeOverwrite && CanOverwritePreparedSwipe() && ShouldOverwriteSwipe())
            PrepareVirtualSwipe("contact changed during virtual swipe");

        if (ShouldHitBeforeBodyCollision())
        {
            if (debugBodyAvoidanceLogs)
                Debug.Log($"[TennisAI BODY AVOID] Early prepared hit before predicted body collision. ball={ball.position} ai={transform.position} preparedFor={Time.time - swipePreparedAt:F3}s.");
            if (TryGetAuthoritativeContact(out HitContactConfirmation bodyContact))
                HitWithPendingSwipe(bodyContact);
            return;
        }

        if (Time.time >= swipeEndTime)
        {
            RecordSwipeCompletionForGizmo();
            tightDeadline = swipeEndTime + Mathf.Max(0.01f, tightHitZonePendingWindow);
            ChangeState(AIState.WaitingTightZone, "virtual swipe finished");
        }
    }

    private void TickWaitingTightZone()
    {
        if (!IsIncomingBall())
        {
            RegisterMiss("ball no longer incoming while waiting tight zone");
            return;
        }

        RefreshContactPlan(true);
        if (TryGetTightContactLock(out HitContactConfirmation contact))
        {
            StopForPlantedContact();
            HitWithPendingSwipe(contact);
            return;
        }

        MoveTowardCurrentPlan();

        if (allowSwipeOverwrite && CanOverwritePreparedSwipe() && ShouldOverwriteSwipe())
        {
            PrepareVirtualSwipe("contact changed while waiting tight zone");
            return;
        }

        if (TryGetAuthoritativeContact(out HitContactConfirmation waitingContact) &&
            !ShouldDeferTightContactForPlannedFastIntercept(waitingContact) &&
            !ShouldDeferTightContactForLiftedBounce())
        {
            HitWithPendingSwipe(waitingContact);
            return;
        }

        if (Time.time > tightDeadline)
            RegisterMiss("tight hit zone deadline expired");
    }

    private void TickHitOrMiss()
    {
        StopMoving();
        if (Time.time >= hitOrMissUntil)
            ChangeState(AIState.Recover, "hit/miss settle complete");
    }

    private void TickRecover()
    {
        hitController.ballIsInHittingZone = false;

        if (IsIncomingBall() && !didHitThisBall)
        {
            ChangeState(AIState.TrackingIncoming, "new incoming during recovery");
            return;
        }

        TickRecoverMovement(true);
    }

    private void RefreshContactPlan(bool keepExistingIfNoBetter, bool force = false)
    {
        UpdateIncomingTrajectoryPrediction(force);

        if (currentPlan.valid && currentPlan.worldContactTime > 0f)
            currentPlan.timeUntilContact = Mathf.Max(0f, currentPlan.worldContactTime - Time.time);

        if (TryUseFrozenIntercept())
            return;

        if (!force && currentPlan.valid && Time.time < nextTrajectoryPlanTime)
            return;

        float refreshHz = GetCurrentTrajectoryPlanRefreshHz();
        nextTrajectoryPlanTime = Time.time + 1f / refreshHz;

        ContactPlan plan;
        if (TryBuildContactPlan(out plan))
        {
            if (!ShouldKeepCurrentMovementPlan(plan, force))
            {
                currentPlan = plan;
                LogInterceptSelection("trajectory");
            }
            TryFreezeFinalIntercept();
            return;
        }

        if (!keepExistingIfNoBetter)
        {
            currentPlan = BuildFallbackContactPlan();
            LogInterceptSelection("fallback");
            TryFreezeFinalIntercept();
        }
    }

    private void LogInterceptSelection(string source)
    {
        if (!logInterceptSelectionDiagnostics || !currentPlan.valid)
            return;

        int shotKey = ball != null ? GetBallShotKey(ball) : swipeStatusShotSequence;
        bool newShot = shotKey != lastInterceptSelectionLogShotKey;
        float stanceShift = HorizontalDistance(lastInterceptSelectionLogStance, currentPlan.stancePoint);
        if (!newShot &&
            Time.time - lastInterceptSelectionLogTime < 0.12f &&
            stanceShift < 0.25f)
        {
            return;
        }

        lastInterceptSelectionLogShotKey = shotKey;
        lastInterceptSelectionLogTime = Time.time;
        lastInterceptSelectionLogStance = currentPlan.stancePoint;

        Vector3 liveVelocity = movement != null ? movement.PlanarVelocity : Vector3.zero;
        Debug.Log($"[AI INTERCEPT SELECTED] shot={swipeStatusShotSequence} source={source} " +
            $"ai={transform.position} liveVelocity={liveVelocity} liveSpeed={liveVelocity.magnitude:F2}m/s " +
            $"contact={currentPlan.contactPoint} stance={currentPlan.stancePoint} " +
            $"contactT={currentPlan.timeUntilContact:F3}s runT={currentPlan.estimatedRunningArrivalTime:F3}s " +
            $"plantT={currentPlan.estimatedPlantedArrivalTime:F3}s slack={currentPlan.arrivalSlack:F3}s " +
            $"launch={currentPlan.launchSpeedMps:F2}m/s planarLaunch={currentPlan.launchPlanarSpeedMps:F2}m/s " +
            $"pace={currentPlan.paceBand} movement={currentPlan.movementClass} readiness={currentPlan.readinessTier} " +
            $"short={currentPlan.shortBall} bounceDepth={currentPlan.firstBounceDepthFromNet:F2}m score={currentPlan.tacticalScore:F2} " +
            $"bounces={currentPlan.ownSideBounceCount} pursuit={currentPlan.pursuitMode} " +
            $"sprint={currentPlan.requiresFullSpeed}.");
    }

    private bool ShouldKeepCurrentMovementPlan(ContactPlan replacement, bool force)
    {
        if (force || !currentPlan.valid || !replacement.valid)
            return false;

        // An emergency target is provisional. Keep following the live flight
        // every refresh until a genuinely reachable intercept is found.
        if (currentPlan.pursuitMode == PursuitMode.Emergency)
            return false;

        float currentRunningArrival = EstimateTimeToReachAtFullSpeed(
            currentPlan.stancePoint,
            0f,
            moveSpeed,
            contactStopDistance);
        bool currentStillReachable = !float.IsPositiveInfinity(currentRunningArrival) &&
            currentRunningArrival <= currentPlan.timeUntilContact + Mathf.Max(0f, runningContactTimingTolerance);
        if (!currentStillReachable || !IsPlanOnCurrentPredictedPath(currentPlan))
            return false;

        bool currentComfortable = IsComfortablePostBouncePlan(currentPlan);
        bool replacementComfortable = IsComfortablePostBouncePlan(replacement);
        if (replacementComfortable && !currentComfortable)
            return false;

        float currentLiveSlack = currentPlan.timeUntilContact - currentRunningArrival;
        bool readinessUpgrade = replacement.readinessTier < currentPlan.readinessTier;
        bool safetyUpgrade = replacement.arrivalSlack >=
            currentLiveSlack + Mathf.Max(0f, tacticalClassSwitchMinimumSlackGain);
        bool scoreUpgrade = currentPlan.tacticalScore - replacement.tacticalScore >=
            Mathf.Max(0f, tacticalClassSwitchRequiredScoreGain);
        bool tacticalClassChanged = currentPlan.movementClass != replacement.movementClass;
        bool routeReversal = IsTacticalRouteReversal(currentPlan, replacement);
        bool replacementMovesEarlier = replacement.worldContactTime <
            currentPlan.worldContactTime - Mathf.Max(0f, tacticalEarlierContactTimeTolerance);

        if (tacticalClassChanged)
        {
            bool currentNonForward = IsNonForwardMovementClass(currentPlan.movementClass);
            bool replacementStepsIn = replacement.movementClass == TacticalMovementClass.StepIn;
            if (currentNonForward && replacementStepsIn && replacement.paceBand != IncomingPaceBand.Slow &&
                !readinessUpgrade && !safetyUpgrade)
            {
                return true;
            }

            // A viable tactical class is a commitment, not a fresh coin toss at
            // every prediction sample. Switch only for a material improvement.
            if (!readinessUpgrade && !safetyUpgrade && !scoreUpgrade)
                return true;
        }

        if ((routeReversal || replacementMovesEarlier) &&
            !readinessUpgrade && !safetyUpgrade && !scoreUpgrade)
        {
            return true;
        }

        float fastBall01 = GetFastBallIntercept01(Mathf.Max(
            GetIncomingReferenceSpeedMps(),
            replacement.incomingVelocity.magnitude));
        float retargetDistance = Mathf.Lerp(
            Mathf.Max(0.01f, movementPlanRetargetDistance),
            Mathf.Max(0.01f, fastBallMovementRetargetDistance),
            fastBall01);
        float retargetTime = Mathf.Lerp(
            Mathf.Max(0.01f, movementPlanRetargetTime),
            Mathf.Max(0.01f, fastBallMovementRetargetTime),
            fastBall01);
        float stanceShift = HorizontalDistance(currentPlan.stancePoint, replacement.stancePoint);
        float timingShift = Mathf.Abs(currentPlan.worldContactTime - replacement.worldContactTime);
        return stanceShift < retargetDistance && timingShift < retargetTime;
    }

    private bool IsPlanOnCurrentPredictedPath(ContactPlan plan)
    {
        if (!plan.valid || incomingTrajectory.Count == 0)
            return false;

        float targetTime = plan.worldContactTime > 0f
            ? plan.worldContactTime - Time.time
            : plan.timeUntilContact;
        if (targetTime < -Mathf.Max(0f, tacticalEarlierContactTimeTolerance))
            return false;

        float nearestTimeDelta = float.PositiveInfinity;
        Vector3 nearestPosition = Vector3.zero;
        for (int i = 0; i < incomingTrajectory.Count; i++)
        {
            float delta = Mathf.Abs(incomingTrajectory[i].time - targetTime);
            if (delta >= nearestTimeDelta)
                continue;
            nearestTimeDelta = delta;
            nearestPosition = incomingTrajectory[i].position;
        }

        float sampleAllowance = Mathf.Max(0.02f, predictionStep) *
            Mathf.Max(1f, plan.incomingVelocity.magnitude);
        float tolerance = Mathf.Max(0.05f, tacticalPlanPathValidityTolerance) + sampleAllowance;
        return Vector3.Distance(nearestPosition, plan.contactPoint) <= tolerance;
    }

    private bool IsTacticalRouteReversal(ContactPlan current, ContactPlan replacement)
    {
        Vector3 currentRoute = current.stancePoint - transform.position;
        Vector3 replacementRoute = replacement.stancePoint - transform.position;
        currentRoute.y = 0f;
        replacementRoute.y = 0f;
        if (currentRoute.sqrMagnitude < 0.09f || replacementRoute.sqrMagnitude < 0.09f)
            return false;

        return Vector3.Dot(currentRoute.normalized, replacementRoute.normalized) <=
            Mathf.Clamp(tacticalRouteReversalDotThreshold, -1f, 1f);
    }

    private static bool IsNonForwardMovementClass(TacticalMovementClass movementClass)
    {
        return movementClass == TacticalMovementClass.BaselineLateral ||
            movementClass == TacticalMovementClass.Retreat;
    }

    private bool IsComfortablePostBouncePlan(ContactPlan plan)
    {
        if (!plan.valid || plan.ownSideBounceCount <= 0 ||
            float.IsPositiveInfinity(plan.estimatedPlantedArrivalTime))
        {
            return false;
        }

        float referenceSpeed = Mathf.Max(GetIncomingReferenceSpeedMps(), plan.incomingVelocity.magnitude);
        return plan.estimatedPlantedArrivalTime <=
            plan.timeUntilContact - GetPreferredContactArrivalLeadTime(referenceSpeed);
    }

    private float GetFastBallIntercept01(float incomingSpeedMps)
    {
        float start = Mathf.Max(0f, fastBallInterceptThresholdMps);
        float full = Mathf.Max(start + 0.01f, fastBallInterceptFullSpeedMps);
        return Mathf.InverseLerp(start, full, Mathf.Max(0f, incomingSpeedMps));
    }

    private float GetPreferredContactArrivalLeadTime(float incomingSpeedMps)
    {
        return Mathf.Lerp(
            Mathf.Max(0f, preferredContactArrivalLeadTime),
            Mathf.Max(0f, fastBallPreferredArrivalLeadTime),
            GetFastBallIntercept01(incomingSpeedMps));
    }

    private float GetCurrentTrajectoryPlanRefreshHz()
    {
        float refreshHz = Mathf.Max(1f, trajectoryPlanRefreshHz);
        if (currentPlan.valid &&
            currentPlan.timeUntilContact <= Mathf.Max(0.01f, closeContactPlanWindow))
        {
            refreshHz = Mathf.Max(refreshHz, Mathf.Max(1f, closeContactPlanRefreshHz));
        }

        return refreshHz;
    }

    private bool TryUseFrozenIntercept()
    {
        if (!finalInterceptFrozen || ball == null)
            return false;

        if (GetBallShotKey(ball) != frozenInterceptShotKey ||
            Time.time > frozenInterceptWorldTime + Mathf.Max(0f, frozenInterceptExpiryGrace))
        {
            ClearFinalInterceptFreeze("frozen intercept expired");
            return false;
        }

        currentPlan = frozenInterceptPlan;
        currentPlan.timeUntilContact = Mathf.Max(0f, frozenInterceptWorldTime - Time.time);
        return true;
    }

    private void TryFreezeFinalIntercept()
    {
        if (!freezeFinalIntercept || finalInterceptFrozen || !currentPlan.valid || ball == null)
            return;
        if (currentPlan.pursuitMode == PursuitMode.Emergency)
            return;

        float incomingReferenceSpeed = Mathf.Max(
            GetIncomingReferenceSpeedMps(),
            currentPlan.incomingVelocity.magnitude);
        float freezeWindow = Mathf.Lerp(
            Mathf.Max(0.01f, finalInterceptFreezeSeconds),
            Mathf.Max(0.01f, fastBallFinalInterceptFreezeSeconds),
            GetFastBallIntercept01(incomingReferenceSpeed));
        if (currentPlan.timeUntilContact > freezeWindow)
            return;

        finalInterceptFrozen = true;
        frozenInterceptPlan = currentPlan;
        frozenInterceptShotKey = GetBallShotKey(ball);
        frozenInterceptWorldTime = currentPlan.worldContactTime > 0f
            ? currentPlan.worldContactTime
            : Time.time + Mathf.Max(0f, currentPlan.timeUntilContact);

        if (logFinalInterceptFreeze)
        {
            Debug.Log($"[AI INTERCEPT LOCK] shot={swipeStatusShotSequence} contact={currentPlan.contactPoint} " +
                $"stance={currentPlan.stancePoint} t={currentPlan.timeUntilContact:F3}s bounces={currentPlan.ownSideBounceCount} " +
                $"pace={currentPlan.paceBand} movement={currentPlan.movementClass} " +
                $"launch={currentPlan.launchSpeedMps:F2}m/s planarLaunch={currentPlan.launchPlanarSpeedMps:F2}m/s.");
        }
    }

    private void ClearFinalInterceptFreeze(string reason)
    {
        if (finalInterceptFrozen && logFinalInterceptFreeze)
            Debug.Log($"[AI INTERCEPT UNLOCK] shot={swipeStatusShotSequence} reason=\"{reason}\".");

        finalInterceptFrozen = false;
        frozenInterceptPlan = default;
        frozenInterceptShotKey = -1;
        frozenInterceptWorldTime = 0f;
    }

    private void UpdateIncomingTrajectoryPrediction(bool force)
    {
        if (ball == null || !IsIncomingBall())
        {
            incomingTrajectory.Clear();
            incomingTrajectoryValid = false;
            return;
        }

        int shotKey = GetBallShotKey(ball);
        float refreshInterval = 1f / Mathf.Max(1f, trajectoryPlanRefreshHz);
        if (!force && incomingTrajectoryValid && incomingTrajectoryShotKey == shotKey &&
            Time.time < nextIncomingTrajectoryPredictionTime)
        {
            return;
        }

        incomingTrajectory.Clear();
        incomingTrajectoryShotKey = shotKey;
        incomingTrajectoryValid = false;
        nextIncomingTrajectoryPredictionTime = Time.time + refreshInterval;

        BallController ballController = ball.GetComponent<BallController>();
        Vector3 position = ball.position;
        Vector3 velocity = ball.linearVelocity;
        Vector3 spin = ballController != null ? ballController.spinRadPerSecond : Vector3.zero;
        int bounceCount = ballController != null ? ballController.CourtBouncesSinceLastHit : 0;
        int ownSideBounceCount = GetKnownOwnSideBounceCount();

        // Keep the exact live bounce as part of the persistent path for debug,
        // diagnostics and any planner that needs to know where the real bounce
        // occurred. The current state remains the simulation origin.
        if (actualBounceShotKey == shotKey && ballController != null)
        {
            float bounceAge = Mathf.Max(0f, Time.time - ballController.LastCourtBounceTime);
            incomingTrajectory.Add(new IncomingTrajectoryPoint
            {
                time = -bounceAge,
                position = actualBouncePosition,
                velocity = actualBounceVelocityOut,
                spin = actualBounceSpinOut,
                bounceCount = Mathf.Max(0, actualBounceOwnSideCount),
                ownSideBounceCount = Mathf.Max(0, actualBounceOwnSideCount),
                isBounce = true,
                actualBounce = true
            });
        }

        incomingTrajectory.Add(new IncomingTrajectoryPoint
        {
            time = 0f,
            position = position,
            velocity = velocity,
            spin = spin,
            bounceCount = bounceCount,
            ownSideBounceCount = ownSideBounceCount
        });

        float dt = Mathf.Clamp(predictionStep, 0.005f, 0.08f);
        int steps = Mathf.CeilToInt(Mathf.Max(0.1f, predictionSeconds) / dt);
        float simulatedTime = 0f;
        int maxSamples = 220;

        for (int i = 0; i < steps && incomingTrajectory.Count < maxSamples; i++)
        {
            Vector3 previousPosition = position;
            Vector3 previousVelocity = velocity;
            Vector3 acceleration = Physics.gravity;
            if (predictionBallistics != null)
            {
                acceleration += predictionBallistics.DragAcceleration(velocity, spin);
                acceleration += predictionBallistics.MagnusAcceleration(velocity, spin);
                spin = predictionBallistics.ApplySpinDecay(spin, dt);
            }

            velocity += acceleration * dt;
            position += velocity * dt;
            simulatedTime += dt;

            if (position.y <= predictionBallRadius && velocity.y < 0f)
            {
                float denominator = previousPosition.y - position.y;
                float alpha = Mathf.Abs(denominator) > 0.0001f
                    ? Mathf.Clamp01((previousPosition.y - predictionBallRadius) / denominator)
                    : 0f;
                Vector3 predictedBouncePosition = Vector3.Lerp(previousPosition, position, alpha);
                predictedBouncePosition.y = predictionBallRadius;
                Vector3 velocityAtBounce = Vector3.Lerp(previousVelocity, velocity, alpha);
                float bounceTime = Mathf.Max(0f, simulatedTime - dt + dt * alpha);

                Vector3 bouncedVelocity;
                Vector3 bouncedSpin;
                if (ballController != null && ballController.TryPredictCustomCourtBounce(
                        velocityAtBounce,
                        spin,
                        out bouncedVelocity,
                        out bouncedSpin))
                {
                    velocity = bouncedVelocity;
                    spin = bouncedSpin;
                }
                else
                {
                    velocity = velocityAtBounce;
                    velocity.y = -velocity.y * Mathf.Clamp01(bounceVelocityRetentionY);
                    velocity.x *= Mathf.Clamp01(bounceVelocityRetentionXZ);
                    velocity.z *= Mathf.Clamp01(bounceVelocityRetentionXZ);
                }

                position = predictedBouncePosition;
                bounceCount++;
                bool ownSideBounce = IsOnOwnSide(position, 0f);
                if (ownSideBounce)
                    ownSideBounceCount++;

                incomingTrajectory.Add(new IncomingTrajectoryPoint
                {
                    time = bounceTime,
                    position = predictedBouncePosition,
                    velocity = velocity,
                    spin = spin,
                    bounceCount = bounceCount,
                    ownSideBounceCount = ownSideBounceCount,
                    isBounce = true,
                    actualBounce = false
                });

                if (ownSideBounce && ownSideBounceCount == 1 && !IsInsideLegalAISideCourt(position))
                    break;
            }

            incomingTrajectory.Add(new IncomingTrajectoryPoint
            {
                time = simulatedTime,
                position = position,
                velocity = velocity,
                spin = spin,
                bounceCount = bounceCount,
                ownSideBounceCount = ownSideBounceCount
            });
        }

        incomingTrajectoryValid = incomingTrajectory.Count > 1;
        if (debugLogs && incomingTrajectoryValid)
        {
            int bounceSamples = 0;
            int actualBounceSamples = 0;
            for (int i = 0; i < incomingTrajectory.Count; i++)
            {
                if (incomingTrajectory[i].isBounce)
                    bounceSamples++;
                if (incomingTrajectory[i].actualBounce)
                    actualBounceSamples++;
            }

            Debug.Log($"[TennisAI TRAJECTORY] shot={shotKey} samples={incomingTrajectory.Count} " +
                $"bounces={bounceSamples} actualBounces={actualBounceSamples} " +
                $"launch={trackedIncomingLaunchSpeedMps:F2}m/s planarLaunch={trackedIncomingLaunchPlanarSpeedMps:F2}m/s " +
                $"pace={trackedIncomingPaceBand} current={position} velocity={velocity} spin={spin}.");
        }
    }

    private bool TryBuildContactPlan(out ContactPlan bestPlan)
    {
        bestPlan = default;
        if (ball == null)
            return false;

        Vector3 pos = ball.position;
        Vector3 vel = ball.linearVelocity;
        Vector3 spin = Vector3.zero;
        BallController ballController = ball.GetComponent<BallController>();
        if (ballController != null)
            spin = ballController.spinRadPerSecond;

        float bestScore = float.PositiveInfinity;
        TacticalPlanChoice baselineChoice = default;
        TacticalPlanChoice retreatChoice = default;
        TacticalPlanChoice stepInChoice = default;
        TacticalPlanChoice volleyChoice = default;
        TacticalPlanChoice emergencyPreBounceChoice = default;
        latestBaselineOption = default;
        latestRetreatOption = default;
        latestStepInOption = default;
        latestVolleyOption = default;
        int knownOwnSideBounces = GetKnownOwnSideBounceCount();
        bool planningFromActualOwnSideBounce = knownOwnSideBounces > 0;
        int bounceCount = knownOwnSideBounces;
        int ownSideBounceCount = knownOwnSideBounces;
        Vector3 baselineReference = GetTacticalBaselineReference();
        float baseDepth = Mathf.Abs(baselineReference.x - netX);
        float incomingReferenceSpeed = GetIncomingReferenceSpeedMps();
        float tacticalPace = GetIncomingTacticalPaceMps();
        IncomingPaceBand paceBand = ClassifyIncomingPace(tacticalPace);
        trackedIncomingPaceBand = paceBand;
        float fastBallIntercept01 = GetFastBallIntercept01(incomingReferenceSpeed);
        float preferredArrivalLead = GetPreferredContactArrivalLeadTime(incomingReferenceSpeed);
        float fastServe01 = GetFastServeReturn01(incomingReferenceSpeed);
        WtaMatchplayObservationLogic wta = GetActiveWtaMatchplayLogic();
        bool preferFastBaselinePostBounce = ShouldPreferFastBaselinePostBounce(incomingReferenceSpeed, knownOwnSideBounces);
        bool hasPredictedFirstBounce = TryGetFirstOwnSideBounceDepth(out float firstBounceDepthFromNet);
        bool shortBall = hasPredictedFirstBounce &&
            baseDepth - firstBounceDepthFromNet >= Mathf.Max(0f, shortBallMinimumDepthInsideBase);
        float effectiveReactionTime = Mathf.Lerp(
            Mathf.Max(0f, reactionTime),
            Mathf.Max(0f, fastServeReactionTime),
            fastServe01);
        float dt = Mathf.Clamp(predictionStep, 0.005f, 0.08f);
        int steps = incomingTrajectoryValid
            ? Mathf.Max(0, incomingTrajectory.Count - 1)
            : Mathf.CeilToInt(Mathf.Max(0.1f, predictionSeconds) / dt);

        for (int i = 1; i <= steps; i++)
        {
            float t;
            if (incomingTrajectoryValid)
            {
                IncomingTrajectoryPoint trajectoryPoint = incomingTrajectory[Mathf.Min(i, incomingTrajectory.Count - 1)];
                t = trajectoryPoint.time;
                pos = trajectoryPoint.position;
                vel = trajectoryPoint.velocity;
                spin = trajectoryPoint.spin;
                bounceCount = trajectoryPoint.bounceCount;
                ownSideBounceCount = trajectoryPoint.ownSideBounceCount;
                if (t <= 0f)
                    continue;
            }
            else
            {
                t = i * dt;
                Vector3 accel = Physics.gravity;
                if (predictionBallistics != null)
                {
                    accel += predictionBallistics.DragAcceleration(vel, spin);
                    accel += predictionBallistics.MagnusAcceleration(vel, spin);
                    spin = predictionBallistics.ApplySpinDecay(spin, dt);
                }

                vel += accel * dt;
                pos += vel * dt;

                if (pos.y <= predictionBallRadius && vel.y < 0f)
                {
                    pos.y = predictionBallRadius;
                    vel.y = -vel.y * Mathf.Clamp01(bounceVelocityRetentionY);
                    vel.x *= Mathf.Clamp01(bounceVelocityRetentionXZ);
                    vel.z *= Mathf.Clamp01(bounceVelocityRetentionXZ);
                    bounceCount++;

                    if (IsOnOwnSide(pos, 0f))
                    {
                        ownSideBounceCount++;
                        if (ownSideBounceCount == 1 && !IsInsideLegalAISideCourt(pos))
                            break;
                    }
                }
            }

            if (!IsOnOwnSide(pos, 0f))
                continue;

            // A point ends on the second bounce. Only plan a playable first-bounce return.
            if (ownSideBounceCount > 1)
                continue;

            bool standingOverheadCandidate = extendTightHitZoneToStandingOverhead &&
                pos.y >= Mathf.Max(idealContactHeightRange.y, standingOverheadContactMinHeight) &&
                pos.y <= emergencyContactHeightRange.y;
            bool emergencyPreBounceCandidate = allowEmergencyPreBounceContact &&
                ownSideBounceCount <= 0 &&
                pos.y >= emergencyContactHeightRange.x &&
                pos.y <= emergencyContactHeightRange.y;
            if (preferPostBounceContact && ownSideBounceCount <= 0 &&
                !standingOverheadCandidate && !emergencyPreBounceCandidate)
                continue;

            if (pos.y < emergencyContactHeightRange.x || pos.y > emergencyContactHeightRange.y)
                continue;

            Vector3 stance = ContactToStance(pos);
            if (!CanAuthoritativeZoneReachContact(pos, stance))
                continue;
            float moveDistance = HorizontalDistance(transform.position, stance);
            float availableMove = EstimateReachableMoveDistance(stance, t, effectiveReactionTime);
            float reachDeficit = Mathf.Max(0f, moveDistance - availableMove);
            float runningArrivalTime = EstimateTimeToReachAtFullSpeed(
                stance,
                effectiveReactionTime,
                moveSpeed,
                contactStopDistance);
            float plantedArrivalTime = EstimateTimeToReachAndPlant(
                stance,
                effectiveReactionTime,
                moveSpeed,
                contactStopDistance);
            bool canRunTightZoneOnPath =
                !float.IsPositiveInfinity(runningArrivalTime) &&
                runningArrivalTime <= t + Mathf.Max(0f, runningContactTimingTolerance);
            bool canPlantTightZoneOnPath =
                !float.IsPositiveInfinity(plantedArrivalTime) &&
                plantedArrivalTime <= t + Mathf.Max(0f, plantedContactTimingTolerance);
            float plantedArrivalMargin = t - plantedArrivalTime;
            bool canArriveComfortably = canPlantTightZoneOnPath &&
                plantedArrivalMargin >= preferredArrivalLead;
            PursuitMode pursuitMode = canPlantTightZoneOnPath
                ? PursuitMode.Comfortable
                : canRunTightZoneOnPath
                    ? PursuitMode.Stretch
                    : PursuitMode.Emergency;

            // Every ordinary candidate must pass the actual motor-time test.
            // Impossible trajectory samples are left to the explicit
            // best-effort fallback and cannot masquerade as tactical choices.
            if (!canRunTightZoneOnPath)
                continue;

            float stanceDepth = Mathf.Abs(stance.x - netX);
            float forwardFromBase = Mathf.Max(0f, baseDepth - stanceDepth);
            float behindBase = Mathf.Max(0f, stanceDepth - baseDepth);
            bool genuineVolleyAdvantage = allowEarlyVolleyForAdvantage &&
                ownSideBounceCount <= 0 &&
                standingOverheadCandidate &&
                pos.y >= Mathf.Max(0f, volleyAdvantageMinContactHeight) &&
                Mathf.Abs(pos.x - netX) <= Mathf.Max(0f, volleyAdvantageMaxDepthFromNet) &&
                Mathf.Abs(transform.position.x - netX) <=
                    Mathf.Max(0f, volleyAdvantageMaxDepthFromNet) + Mathf.Max(0f, volleyAdvantageMaxApproachDistance) &&
                moveDistance <= Mathf.Max(0f, volleyAdvantageMaxApproachDistance) &&
                canPlantTightZoneOnPath;
            TacticalMovementClass movementClass;
            if (ownSideBounceCount <= 0)
            {
                movementClass = genuineVolleyAdvantage
                    ? TacticalMovementClass.Volley
                    : TacticalMovementClass.Emergency;
            }
            else if (forwardFromBase > Mathf.Max(0f, tacticalBaselineDepthTolerance))
            {
                movementClass = TacticalMovementClass.StepIn;
            }
            else if (behindBase > Mathf.Max(0f, tacticalBaselineDepthTolerance))
            {
                movementClass = TacticalMovementClass.Retreat;
            }
            else
            {
                movementClass = TacticalMovementClass.BaselineLateral;
            }
            ContactReadinessTier readinessTier = canArriveComfortably
                ? ContactReadinessTier.Comfortable
                : canPlantTightZoneOnPath
                    ? ContactReadinessTier.Planted
                    : ContactReadinessTier.Running;

            // Do not discard a physically legal contact just because it is late.
            // The selected mode governs movement commitment; return quality is
            // resolved later by the ordinary pressure and swipe systems.
            float idealHeight = Mathf.Clamp(pos.y, idealContactHeightRange.x, idealContactHeightRange.y);
            float heightScore = Mathf.Abs(pos.y - idealHeight) * 3f;
            float moveScore = moveDistance * 0.18f;
            float timeScore = Mathf.Abs(t - 0.65f) * 0.15f;
            float emergencyPenalty = (pos.y < idealContactHeightRange.x || pos.y > idealContactHeightRange.y) ? 2f : 0f;
            float baselineVolleyPenalty = avoidBaselineVolleys && ownSideBounceCount <= 0 && IsDeepCourtPosition(pos)
                ? 4.5f
                : 0f;
            bool forwardFastBaselineIntercept = preferFastBaselinePostBounce && ownSideBounceCount <= 0 &&
                IsForwardOfCurrentBaselinePosition(stance);
            float fastBaselinePreBouncePenalty = forwardFastBaselineIntercept
                ? Mathf.Max(0f, fastBaselinePreBounceFallbackPenalty)
                : 0f;
            float emergencyPreBouncePenalty = preferPostBounceContact && ownSideBounceCount <= 0 &&
                !standingOverheadCandidate
                ? 5f
                : 0f;
            float baselinePositionPenalty = 0f;
            float comfortableArrivalPenalty = 0f;
            if (ownSideBounceCount > 0)
            {
                float preferredStanceDepth = baseDepth +
                    Mathf.Max(0f, fastBallPreferredDepthBehindBase) * fastBallIntercept01;
                float paceForwardScale = paceBand switch
                {
                    IncomingPaceBand.Slow => 0.55f,
                    IncomingPaceBand.Moderate => 1.35f,
                    _ => 2.75f
                };
                baselinePositionPenalty =
                    Mathf.Abs(stanceDepth - preferredStanceDepth) * Mathf.Max(0f, postBounceDepthFromBasePenalty) +
                    forwardFromBase * Mathf.Max(0f, postBounceForwardFromBasePenalty) *
                        paceForwardScale;
                comfortableArrivalPenalty = Mathf.Max(0f,
                    preferredArrivalLead - plantedArrivalMargin) * 3f;
            }
            float firstBounceApexPenalty = preferFirstBounceApexContact && ownSideBounceCount == 1 &&
                !(preferActualBounceRisingContact && planningFromActualOwnSideBounce)
                ? Mathf.Abs(vel.y) * Mathf.Max(0f, firstBounceApexVerticalSpeedWeight)
                : 0f;
            float predictedPostBounceRisingPreference = 0f;
            if (preferActualBounceRisingContact && ownSideBounceCount == 1)
            {
                float minRisingHeight = Mathf.Min(preferredRisingContactHeightRange.x, preferredRisingContactHeightRange.y);
                float maxRisingHeight = Mathf.Max(preferredRisingContactHeightRange.x, preferredRisingContactHeightRange.y);
                bool rising = vel.y >= Mathf.Max(0f, risingContactMinimumVerticalSpeed);
                bool inPreferredRisingBand = rising && pos.y >= minRisingHeight && pos.y <= maxRisingHeight;
                bool immediateHalfVolley = rising && pos.y < minRisingHeight &&
                    t <= Mathf.Max(0f, closeBounceHalfVolleyWindow);
                if (inPreferredRisingBand)
                    predictedPostBounceRisingPreference -= Mathf.Max(0f, risingContactPriority) - t * 0.35f;
                else if (immediateHalfVolley)
                    predictedPostBounceRisingPreference -= Mathf.Max(0f, risingContactPriority) * 0.65f - t * 0.20f;
                else if (vel.y < -Mathf.Max(0f, risingContactMinimumVerticalSpeed))
                    predictedPostBounceRisingPreference += Mathf.Max(0f, descendingAfterBouncePenalty);
            }
            float actualBounceContactPreference = 0f;
            if (preferActualBounceRisingContact && planningFromActualOwnSideBounce &&
                ownSideBounceCount == knownOwnSideBounces)
            {
                float minRisingHeight = Mathf.Min(preferredRisingContactHeightRange.x, preferredRisingContactHeightRange.y);
                float maxRisingHeight = Mathf.Max(preferredRisingContactHeightRange.x, preferredRisingContactHeightRange.y);
                bool rising = vel.y >= Mathf.Max(0f, risingContactMinimumVerticalSpeed);
                bool inPreferredRisingBand = rising && pos.y >= minRisingHeight && pos.y <= maxRisingHeight;
                bool immediateHalfVolley = rising && pos.y < minRisingHeight &&
                    t <= Mathf.Max(0f, closeBounceHalfVolleyWindow);

                if (inPreferredRisingBand)
                    actualBounceContactPreference -= Mathf.Max(0f, risingContactPriority) - t * 0.35f;
                else if (immediateHalfVolley)
                    actualBounceContactPreference -= Mathf.Max(0f, risingContactPriority) * 0.65f - t * 0.20f;
                else if (vel.y < -Mathf.Max(0f, risingContactMinimumVerticalSpeed))
                    actualBounceContactPreference += Mathf.Max(0f, descendingAfterBouncePenalty);
            }
            float reachPenalty = pursuitMode switch
            {
                PursuitMode.Comfortable => 0f,
                PursuitMode.Stretch => 0.8f + reachDeficit * 0.8f,
                _ => Mathf.Max(0f, emergencyReachScorePenalty) +
                     reachDeficit * Mathf.Max(0f, emergencyReachDeficitScore)
            };
            float score = heightScore + moveScore + timeScore + emergencyPenalty + baselineVolleyPenalty +
                fastBaselinePreBouncePenalty + emergencyPreBouncePenalty + firstBounceApexPenalty +
                actualBounceContactPreference + predictedPostBounceRisingPreference + reachPenalty +
                baselinePositionPenalty + comfortableArrivalPenalty;
            if (wta != null)
            {
                score += wta.GetContactPlanScoreAdjustment(
                    vel.magnitude,
                    ownSideBounceCount,
                    pos.y,
                    vel.y);
            }

            ContactPlan candidatePlan = new ContactPlan
            {
                valid = true,
                contactPoint = pos,
                stancePoint = stance,
                timeUntilContact = t,
                worldContactTime = Time.time + t,
                bounceCount = bounceCount,
                ownSideBounceCount = ownSideBounceCount,
                incomingVelocity = vel,
                incomingSpin = spin,
                paceBand = paceBand,
                movementClass = movementClass,
                readinessTier = readinessTier,
                launchSpeedMps = trackedIncomingLaunchSpeedMps,
                launchPlanarSpeedMps = trackedIncomingLaunchPlanarSpeedMps,
                firstBounceDepthFromNet = hasPredictedFirstBounce ? firstBounceDepthFromNet : -1f,
                tacticalScore = score,
                shortBall = shortBall,
                pursuitMode = pursuitMode,
                requiredMoveDistance = moveDistance,
                estimatedReachDistance = availableMove,
                estimatedRunningArrivalTime = runningArrivalTime,
                estimatedPlantedArrivalTime = plantedArrivalTime,
                arrivalSlack = t - runningArrivalTime,
                requiresFullSpeed = t - runningArrivalTime <= Mathf.Max(0f, fullSpeedPursuitSlack)
            };
            if (score < bestScore)
            {
                bestScore = score;
                bestPlan = candidatePlan;
            }

            // Retain the best option in every tactical movement class. This is
            // deliberately separate from reachability: a point can be reachable
            // yet still be a poor decision because it drags the AI forward and
            // exposes the court for the next ball.
            switch (movementClass)
            {
                case TacticalMovementClass.BaselineLateral:
                    ConsiderTacticalPlan(ref baselineChoice, candidatePlan, score, readinessTier);
                    break;
                case TacticalMovementClass.Retreat:
                    ConsiderTacticalPlan(ref retreatChoice, candidatePlan, score, readinessTier);
                    break;
                case TacticalMovementClass.StepIn:
                    ConsiderTacticalPlan(ref stepInChoice, candidatePlan, score, readinessTier);
                    break;
                case TacticalMovementClass.Volley:
                    ConsiderTacticalPlan(ref volleyChoice, candidatePlan, score, readinessTier);
                    break;
                default:
                    ConsiderTacticalPlan(ref emergencyPreBounceChoice, candidatePlan, score, readinessTier);
                    break;
            }
        }

        if (preferPostBounceContact)
        {
            latestBaselineOption = baselineChoice.valid ? baselineChoice.plan : default;
            latestRetreatOption = retreatChoice.valid ? retreatChoice.plan : default;
            latestStepInOption = stepInChoice.valid ? stepInChoice.plan : default;
            latestVolleyOption = volleyChoice.valid ? volleyChoice.plan : default;

            TacticalPlanChoice tacticalChoice = SelectTacticalPlan(
                paceBand,
                baselineChoice,
                retreatChoice,
                stepInChoice,
                volleyChoice,
                emergencyPreBounceChoice);
            if (tacticalChoice.valid)
                bestPlan = tacticalChoice.plan;
            else
                // A late or stretched ball is still a ball to pursue. The
                // movement motor will make the best available emergency run;
                // it must never turn an incoming shot into an intentional hold.
                bestPlan = bestPlan.valid ? bestPlan : BuildFallbackContactPlan();
        }

        if (bestPlan.valid)
            ConfigureContactApproach(ref bestPlan, effectiveReactionTime);

        return bestPlan.valid;
    }

    private static void ConsiderTacticalPlan(
        ref TacticalPlanChoice choice,
        ContactPlan candidate,
        float score,
        ContactReadinessTier readinessTier)
    {
        if (!candidate.valid)
            return;

        bool betterReadiness = !choice.valid || readinessTier < choice.readinessTier;
        bool sameReadinessBetterScore = choice.valid && readinessTier == choice.readinessTier && score < choice.score;
        if (!betterReadiness && !sameReadinessBetterScore)
            return;

        choice.valid = true;
        choice.plan = candidate;
        choice.score = score;
        choice.readinessTier = readinessTier;
    }

    private static TacticalPlanChoice BetterTacticalChoice(TacticalPlanChoice a, TacticalPlanChoice b)
    {
        if (!a.valid)
            return b;
        if (!b.valid)
            return a;
        if (a.readinessTier != b.readinessTier)
            return a.readinessTier < b.readinessTier ? a : b;
        return a.score <= b.score ? a : b;
    }

    private TacticalPlanChoice SelectTacticalPlan(
        IncomingPaceBand paceBand,
        TacticalPlanChoice baselineChoice,
        TacticalPlanChoice retreatChoice,
        TacticalPlanChoice stepInChoice,
        TacticalPlanChoice volleyChoice,
        TacticalPlanChoice emergencyPreBounceChoice)
    {
        // A volley candidate has already passed the strict high-contact,
        // near-net, short-approach and planted-arrival advantage test.
        if (volleyChoice.valid)
            return volleyChoice;

        TacticalPlanChoice nonForwardChoice = BetterTacticalChoice(baselineChoice, retreatChoice);
        switch (paceBand)
        {
            case IncomingPaceBand.Fast:
                // Fast shots should not pull a baseline player forward merely
                // because that sample is technically reachable. Preserve the
                // baseline/retreat option whenever one exists.
                if (nonForwardChoice.valid)
                    return nonForwardChoice;
                if (stepInChoice.valid)
                    return stepInChoice;
                break;

            case IncomingPaceBand.Moderate:
                // Moderate balls advance only for a clearly short ball with a
                // planted arrival and a material tactical score improvement.
                if (IsModerateStepInAdvantage(stepInChoice, nonForwardChoice))
                    return stepInChoice;
                if (nonForwardChoice.valid)
                    return nonForwardChoice;
                if (stepInChoice.valid)
                    return stepInChoice;
                break;

            default:
                // Slow short balls are the natural step-in candidates, but the
                // AI must still arrive planted and the option must remain close
                // in quality to the best baseline/retreat contact.
                if (IsSlowStepInAdvantage(stepInChoice, nonForwardChoice))
                    return stepInChoice;
                if (nonForwardChoice.valid)
                    return nonForwardChoice;
                if (stepInChoice.valid)
                    return stepInChoice;
                break;
        }

        return emergencyPreBounceChoice;
    }

    private bool IsModerateStepInAdvantage(TacticalPlanChoice stepIn, TacticalPlanChoice nonForward)
    {
        if (!stepIn.valid || !stepIn.plan.shortBall ||
            float.IsPositiveInfinity(stepIn.plan.estimatedPlantedArrivalTime))
        {
            return false;
        }

        float plantedLead = stepIn.plan.timeUntilContact - stepIn.plan.estimatedPlantedArrivalTime;
        if (plantedLead < Mathf.Max(0f, moderateStepInMinimumPlantLead))
            return false;
        if (!nonForward.valid)
            return true;

        bool readinessUpgrade = stepIn.readinessTier < nonForward.readinessTier;
        bool scoreAdvantage = stepIn.score + Mathf.Max(0f, moderateStepInRequiredScoreAdvantage) <= nonForward.score;
        return scoreAdvantage ||
            (readinessUpgrade && stepIn.score <= nonForward.score + 0.25f);
    }

    private bool IsSlowStepInAdvantage(TacticalPlanChoice stepIn, TacticalPlanChoice nonForward)
    {
        if (!stepIn.valid || !stepIn.plan.shortBall ||
            float.IsPositiveInfinity(stepIn.plan.estimatedPlantedArrivalTime))
        {
            return false;
        }

        float plantedLead = stepIn.plan.timeUntilContact - stepIn.plan.estimatedPlantedArrivalTime;
        if (plantedLead < Mathf.Max(0f, slowStepInMinimumPlantLead))
            return false;
        if (!nonForward.valid)
            return true;
        if (stepIn.readinessTier > nonForward.readinessTier)
            return false;
        if (stepIn.readinessTier < nonForward.readinessTier)
            return true;
        return stepIn.score <= nonForward.score + Mathf.Max(0f, slowStepInScoreTolerance);
    }

    private bool TryGetFirstOwnSideBounceDepth(out float depthFromNet)
    {
        depthFromNet = 0f;
        bool found = false;
        float earliestTime = float.PositiveInfinity;
        for (int i = 0; i < incomingTrajectory.Count; i++)
        {
            IncomingTrajectoryPoint point = incomingTrajectory[i];
            if (!point.isBounce || point.ownSideBounceCount != 1 || !IsOnOwnSide(point.position, 0.05f))
                continue;

            if (found && point.time >= earliestTime)
                continue;

            found = true;
            earliestTime = point.time;
            depthFromNet = Mathf.Abs(point.position.x - netX);
        }

        return found;
    }

    private bool ShouldPreferFastBaselinePostBounce(float incomingSpeedMps, int knownOwnSideBounces)
    {
        if (!preferPostBounceForFastBaselineBalls || knownOwnSideBounces > 0)
            return false;

        float currentDepth = Mathf.Abs(transform.position.x - netX);
        if (currentDepth < Mathf.Max(0f, fastBaselinePostBounceMinimumDepthFromNet))
            return false;

        return incomingSpeedMps >= Mathf.Max(0f, fastBaselinePostBounceMinimumSpeedMps);
    }

    private bool IsForwardOfCurrentBaselinePosition(Vector3 candidatePosition, float tolerance = -1f)
    {
        float currentDepth = Mathf.Abs(transform.position.x - netX);
        float candidateDepth = Mathf.Abs(candidatePosition.x - netX);
        float advanceTolerance = tolerance >= 0f ? tolerance : fastBaselineForwardAdvanceTolerance;
        return candidateDepth < currentDepth - Mathf.Max(0f, advanceTolerance);
    }

    private ContactPlan BuildFallbackContactPlan()
    {
        Vector3 ballPos = ball != null ? ball.position : transform.position;
        Vector3 incomingVelocity = ball != null ? ball.linearVelocity : Vector3.zero;
        Vector3 incomingSpin = ball != null && ball.TryGetComponent(out BallController ballController)
            ? ballController.spinRadPerSecond
            : Vector3.zero;
        float contactTime = 0.25f;
        Vector3 contact = ballPos;
        if (ball != null && Mathf.Abs(incomingVelocity.x) > 0.05f)
        {
            Vector3 baseReference = hasRuntimeBasePosition ? runtimeBasePosition : transform.position;
            float bodyDepthOffset = Mathf.Max(minimumBodyContactOffset, Mathf.Abs(bodyOffsetFromContact.x));
            float preferredContactX = baseReference.x - GetAISideSign() * bodyDepthOffset;
            float planeTime = (preferredContactX - ballPos.x) / incomingVelocity.x;
            if (planeTime > 0f)
            {
                contactTime = Mathf.Clamp(planeTime, 0.08f, Mathf.Max(0.25f, predictionSeconds));
                contact = ballPos + incomingVelocity * contactTime + Physics.gravity * (0.5f * contactTime * contactTime);
                contact.y = Mathf.Clamp(contact.y, emergencyContactHeightRange.x, emergencyContactHeightRange.y);
            }
        }
        Vector3 stance = ContactToStance(contact);
        int bounceCount = 0;
        int ownSideBounceCount = 0;
        float bestScore = float.PositiveInfinity;
        float selectedRunningArrival = float.PositiveInfinity;
        float incomingReferenceSpeed = Mathf.Max(incomingVelocity.magnitude, GetIncomingReferenceSpeedMps());
        float fallbackReactionTime = Mathf.Lerp(
            Mathf.Max(0f, reactionTime),
            Mathf.Max(0f, fastServeReactionTime),
            GetFastServeReturn01(incomingReferenceSpeed));

        // If the normal tactical filters find nothing, still choose the
        // least-unreachable point on the simulated flight. This makes an
        // honest lateral/backward chase instead of standing still.
        for (int i = 0; i < incomingTrajectory.Count; i++)
        {
            IncomingTrajectoryPoint point = incomingTrajectory[i];
            if (point.time <= 0f || !IsOnOwnSide(point.position, 0f) || point.ownSideBounceCount > 1)
                continue;
            if (point.position.y < emergencyContactHeightRange.x || point.position.y > emergencyContactHeightRange.y)
                continue;

            Vector3 candidateStance = ContactToStance(point.position);
            float required = HorizontalDistance(transform.position, candidateStance);
            float reachable = EstimateReachableMoveDistance(candidateStance, point.time, 0f);
            float deficit = Mathf.Max(0f, required - reachable);
            float runningArrival = EstimateTimeToReachAtFullSpeed(
                candidateStance,
                fallbackReactionTime,
                moveSpeed,
                contactStopDistance);
            float lateness = float.IsPositiveInfinity(runningArrival)
                ? 4f
                : Mathf.Max(0f, runningArrival - point.time);
            float currentDepth = Mathf.Abs(transform.position.x - netX);
            float candidateDepth = Mathf.Abs(candidateStance.x - netX);
            float forwardPenalty = Mathf.Max(0f, currentDepth - candidateDepth) * 4f;
            float preBouncePenalty = point.ownSideBounceCount > 0 ? 0f : 8f;
            float score = lateness * 20f + deficit * 2f + required * 0.10f + forwardPenalty + preBouncePenalty;
            if (score >= bestScore)
                continue;

            bestScore = score;
            contact = point.position;
            stance = candidateStance;
            contactTime = point.time;
            bounceCount = point.bounceCount;
            ownSideBounceCount = point.ownSideBounceCount;
            incomingVelocity = point.velocity;
            incomingSpin = point.spin;
            selectedRunningArrival = runningArrival;
        }

        float requiredMove = HorizontalDistance(transform.position, stance);
        float estimatedReach = EstimateReachableMoveDistance(stance, contactTime, 0f);
        if (float.IsPositiveInfinity(selectedRunningArrival))
        {
            selectedRunningArrival = EstimateTimeToReachAtFullSpeed(
                stance,
                fallbackReactionTime,
                moveSpeed,
                contactStopDistance);
        }
        Vector3 fallbackBase = GetTacticalBaselineReference();
        float fallbackBaseDepth = Mathf.Abs(fallbackBase.x - netX);
        float fallbackStanceDepth = Mathf.Abs(stance.x - netX);
        TacticalMovementClass fallbackMovementClass = TacticalMovementClass.Emergency;
        if (ownSideBounceCount > 0)
        {
            if (fallbackStanceDepth < fallbackBaseDepth - Mathf.Max(0f, tacticalBaselineDepthTolerance))
                fallbackMovementClass = TacticalMovementClass.StepIn;
            else if (fallbackStanceDepth > fallbackBaseDepth + Mathf.Max(0f, tacticalBaselineDepthTolerance))
                fallbackMovementClass = TacticalMovementClass.Retreat;
            else
                fallbackMovementClass = TacticalMovementClass.BaselineLateral;
        }
        bool hasFirstBounce = TryGetFirstOwnSideBounceDepth(out float firstBounceDepth);
        return new ContactPlan
        {
            valid = true,
            contactPoint = contact,
            stancePoint = stance,
            timeUntilContact = contactTime,
            worldContactTime = Time.time + contactTime,
            bounceCount = bounceCount,
            ownSideBounceCount = ownSideBounceCount,
            incomingVelocity = incomingVelocity,
            incomingSpin = incomingSpin,
            paceBand = trackedIncomingPaceBand,
            movementClass = fallbackMovementClass,
            readinessTier = ContactReadinessTier.Emergency,
            launchSpeedMps = trackedIncomingLaunchSpeedMps,
            launchPlanarSpeedMps = trackedIncomingLaunchPlanarSpeedMps,
            firstBounceDepthFromNet = hasFirstBounce ? firstBounceDepth : -1f,
            tacticalScore = bestScore,
            shortBall = hasFirstBounce &&
                fallbackBaseDepth - firstBounceDepth >= Mathf.Max(0f, shortBallMinimumDepthInsideBase),
            pursuitMode = PursuitMode.Emergency,
            approachMode = ContactApproachMode.Running,
            requiredMoveDistance = requiredMove,
            estimatedReachDistance = estimatedReach,
            approachMoveSpeed = Mathf.Max(0.01f, moveSpeed),
            minimumApproachSpeed = Mathf.Max(0.01f, moveSpeed),
            estimatedRunningArrivalTime = selectedRunningArrival,
            estimatedPlantedArrivalTime = float.PositiveInfinity,
            arrivalSlack = contactTime - selectedRunningArrival,
            requiresFullSpeed = true,
            significantRunningContact = true
        };
    }

    private float EstimateReachableMoveDistance(Vector3 target, float timeUntilContact, float effectiveReactionTime)
    {
        return EstimateReachableMoveDistance(target, timeUntilContact, effectiveReactionTime, moveSpeed);
    }

    private float EstimateReachableMoveDistance(Vector3 target, float timeUntilContact, float effectiveReactionTime, float targetMaxSpeed)
    {
        float remainingTime = Mathf.Max(0f, timeUntilContact - Mathf.Max(0f, effectiveReactionTime));
        if (remainingTime <= 0f)
            return 0f;

        if (movement == null)
            return remainingTime * Mathf.Max(0.01f, targetMaxSpeed);

        Vector3 desiredDirection = target - transform.position;
        desiredDirection.y = 0f;
        if (desiredDirection.sqrMagnitude <= 0.0001f)
            return 0f;
        desiredDirection.Normalize();

        float speed = Mathf.Max(0f, movement.CurrentSpeed);
        Vector3 currentVelocity = movement.PlanarVelocity;
        currentVelocity.y = 0f;
        if (speed > 0.15f && currentVelocity.sqrMagnitude > 0.0001f)
        {
            float directionDot = Vector3.Dot(currentVelocity.normalized, desiredDirection);
            if (movement.useWrongFooting && directionDot <= movement.wrongFootDot)
            {
                remainingTime = Mathf.Max(0f, remainingTime - EstimateWrongFootResetTime(speed, targetMaxSpeed));
                speed = 0f;
            }
            else if (directionDot < movement.smoothTurnDot)
            {
                float turn01 = Mathf.InverseLerp(movement.smoothTurnDot, movement.wrongFootDot, directionDot);
                remainingTime = Mathf.Max(0f, remainingTime - Mathf.Lerp(0.02f, 0.08f, turn01));
                speed *= Mathf.Lerp(1f, 0.55f, turn01);
            }
        }

        float targetSpeed = Mathf.Max(0.01f, targetMaxSpeed);
        const float simulationStep = 1f / 120f;
        float distance = 0f;
        while (remainingTime > 0f)
        {
            float step = Mathf.Min(simulationStep, remainingTime);
            float acceleration = EstimateMotorAcceleration(speed, targetSpeed);
            speed = Mathf.MoveTowards(speed, targetSpeed, acceleration * step);
            distance += speed * step;
            remainingTime -= step;
        }

        return distance;
    }

    private float EstimateTimeToReachAtFullSpeed(
        Vector3 target,
        float reactionDelay,
        float targetMaxSpeed,
        float stopDistance)
    {
        Vector3 desiredDirection = target - transform.position;
        desiredDirection.y = 0f;
        float remainingDistance = desiredDirection.magnitude;
        float elapsed = Mathf.Max(0f, reactionDelay);
        if (remainingDistance <= Mathf.Max(0.01f, stopDistance))
            return elapsed;

        float maximumSpeed = Mathf.Max(0.01f, targetMaxSpeed);
        if (movement == null)
            return elapsed + Mathf.Max(0f, remainingDistance - stopDistance) / maximumSpeed;

        desiredDirection /= Mathf.Max(0.0001f, remainingDistance);
        float speed = Mathf.Max(0f, movement.CurrentSpeed);
        Vector3 currentVelocity = movement.PlanarVelocity;
        currentVelocity.y = 0f;
        if (speed > 0.15f && currentVelocity.sqrMagnitude > 0.0001f)
        {
            float directionDot = Vector3.Dot(currentVelocity.normalized, desiredDirection);
            if (movement.useWrongFooting && directionDot <= movement.wrongFootDot)
            {
                // Match the real motor: brake the existing recovery run, pay
                // the repeated replant/braking time, then accelerate toward the ball.
                elapsed += EstimateWrongFootResetTime(speed, maximumSpeed);
                speed = 0f;
            }
            else if (directionDot < movement.smoothTurnDot)
            {
                float turn01 = Mathf.InverseLerp(movement.smoothTurnDot, movement.wrongFootDot, directionDot);
                elapsed += Mathf.Lerp(0.02f, 0.08f, turn01);
                speed *= Mathf.Lerp(1f, 0.55f, turn01);
            }
        }

        const float simulationStep = 1f / 120f;
        const float maximumSimulationTime = 4f;
        float targetDistance = Mathf.Max(0.01f, stopDistance);
        while (elapsed <= maximumSimulationTime)
        {
            speed = Mathf.MoveTowards(
                speed,
                maximumSpeed,
                EstimateMotorAcceleration(speed, maximumSpeed) * simulationStep);
            remainingDistance = Mathf.Max(0f, remainingDistance - speed * simulationStep);
            elapsed += simulationStep;
            if (remainingDistance <= targetDistance)
                return elapsed;
        }

        return float.PositiveInfinity;
    }

    private float EstimateWrongFootResetTime(float speed, float targetMaxSpeed)
    {
        if (movement == null || speed <= Mathf.Max(0f, movement.replantReleaseSpeed))
            return 0f;

        // PlayerMovement leaves each replant when either its timer expires or
        // speed is low, but immediately re-enters while velocity still points
        // the wrong way. The net delay is therefore the braking time down to
        // release speed, with at least one scaled replant interval.
        float brakeTime =
            (speed - Mathf.Max(0f, movement.replantReleaseSpeed)) /
            Mathf.Max(0.01f, movement.deceleration);
        return Mathf.Max(
            Mathf.Max(0f, brakeTime),
            movement.GetScaledReplantDelay(speed, targetMaxSpeed));
    }

    private void ConfigureContactApproach(ref ContactPlan plan, float effectiveReactionTime)
    {
        if (!plan.valid)
            return;

        float minimumSpeed = EstimateMinimumApproachSpeed(plan.stancePoint, plan.timeUntilContact, effectiveReactionTime);
        float runningArrivalTime = EstimateTimeToReachAtFullSpeed(
            plan.stancePoint,
            effectiveReactionTime,
            moveSpeed,
            contactStopDistance);
        float plantedArrivalTime = EstimateTimeToReachAndPlant(plan.stancePoint, effectiveReactionTime, moveSpeed, contactStopDistance);
        bool canReachOnTime =
            !float.IsPositiveInfinity(runningArrivalTime) &&
            runningArrivalTime <= plan.timeUntilContact + Mathf.Max(0f, runningContactTimingTolerance);
        bool canPlantOnTime =
            (!float.IsPositiveInfinity(plantedArrivalTime) &&
             plantedArrivalTime <= plan.timeUntilContact + Mathf.Max(0f, plantedContactTimingTolerance));
        bool canPlant = !enableRunningContactPlanning || canPlantOnTime;
        float arrivalSlack = plan.timeUntilContact - runningArrivalTime;
        bool requiresFullSpeed = plan.requiresFullSpeed || !canReachOnTime ||
            arrivalSlack <= Mathf.Max(0f, fullSpeedPursuitSlack);

        plan.minimumApproachSpeed = minimumSpeed;
        plan.estimatedRunningArrivalTime = runningArrivalTime;
        plan.estimatedPlantedArrivalTime = plantedArrivalTime;
        plan.arrivalSlack = arrivalSlack;
        plan.requiresFullSpeed = requiresFullSpeed;
        plan.significantRunningContact = enableRunningContactPlanning && !canPlant;
        plan.approachMode = plan.significantRunningContact ? ContactApproachMode.Running : ContactApproachMode.Planted;
        if (plan.pursuitMode == PursuitMode.Emergency)
        {
            plan.readinessTier = ContactReadinessTier.Emergency;
        }
        else if (!canPlantOnTime)
        {
            plan.readinessTier = ContactReadinessTier.Running;
        }
        else
        {
            float preferredLead = GetPreferredContactArrivalLeadTime(GetIncomingReferenceSpeedMps());
            plan.readinessTier = plan.timeUntilContact - plantedArrivalTime >= preferredLead
                ? ContactReadinessTier.Comfortable
                : ContactReadinessTier.Planted;
        }
        plan.approachMoveSpeed = requiresFullSpeed
            ? Mathf.Max(0.01f, moveSpeed)
            : !plan.significantRunningContact
                ? Mathf.Max(0.01f, moveSpeed)
                : Mathf.Clamp(
                Mathf.Max(Mathf.Max(0f, runningContactMinimumApproachSpeed), minimumSpeed * Mathf.Max(1f, runningContactSpeedSafetyFactor)),
                0.01f,
                Mathf.Max(0.01f, moveSpeed));
    }

    private float EstimateMinimumApproachSpeed(Vector3 target, float timeUntilContact, float effectiveReactionTime)
    {
        float requiredDistance = HorizontalDistance(transform.position, target);
        if (requiredDistance <= Mathf.Max(0.01f, contactStopDistance))
            return 0f;

        float maximumSpeed = Mathf.Max(0.01f, moveSpeed);
        if (EstimateReachableMoveDistance(target, timeUntilContact, effectiveReactionTime, maximumSpeed) < requiredDistance)
            return maximumSpeed;

        float low = 0.01f;
        float high = maximumSpeed;
        for (int i = 0; i < 8; i++)
        {
            float candidate = (low + high) * 0.5f;
            if (EstimateReachableMoveDistance(target, timeUntilContact, effectiveReactionTime, candidate) >= requiredDistance)
                high = candidate;
            else
                low = candidate;
        }

        return high;
    }

    private void LogMatchplayContactPlanDiagnostic()
    {
        if (!logMatchplayContactPlanDiagnostics || !pendingUsedMatchplayDecision)
            return;

        int ballId = ball != null ? ball.GetInstanceID() : 0;
        if (lastMatchplayContactPlanDiagnosticShotKey == swipeStatusShotSequence &&
            lastMatchplayContactPlanDiagnosticBallId == ballId)
        {
            return;
        }

        lastMatchplayContactPlanDiagnosticShotKey = swipeStatusShotSequence;
        lastMatchplayContactPlanDiagnosticBallId = ballId;
        float liveMoveSpeed = movement != null ? Mathf.Max(0f, movement.CurrentSpeed) : 0f;
        string mode = pendingSignificantRunningContact ? "RUNNING" : "NORMAL";
        Debug.Log($"[AI CONTACT PLAN] shot={swipeStatusShotSequence} mode={mode} " +
            $"liveMove={liveMoveSpeed:F2}m/s minMove={currentPlan.minimumApproachSpeed:F2}m/s plannedMove={currentPlan.approachMoveSpeed:F2}m/s " +
            $"contact={currentPlan.contactPoint} contactT={currentPlan.timeUntilContact:F2}s " +
            $"incomingVelocity={currentPlan.incomingVelocity} incomingSpin={currentPlan.incomingSpin} " +
            $"launch={currentPlan.launchSpeedMps:F2}m/s planarLaunch={currentPlan.launchPlanarSpeedMps:F2}m/s pace={currentPlan.paceBand} " +
            $"movement={currentPlan.movementClass} readiness={currentPlan.readinessTier} short={currentPlan.shortBall} bounceDepth={currentPlan.firstBounceDepthFromNet:F2}m " +
            $"trajectoryPhase={(currentPlan.ownSideBounceCount > 0 ? "POST_BOUNCE" : "PRE_BOUNCE_FALLBACK")} " +
            $"predictedOwnBounces={currentPlan.ownSideBounceCount} trajectorySamples={incomingTrajectory.Count} " +
            $"runT={currentPlan.estimatedRunningArrivalTime:F2}s plantT={currentPlan.estimatedPlantedArrivalTime:F2}s " +
            $"arrivalSlack={currentPlan.arrivalSlack:F2}s sprint={currentPlan.requiresFullSpeed} " +
            $"pursuit={currentPlan.pursuitMode} tactic={pendingMatchplayTactic} recovery={pendingRecoveryBall} " +
            $"return={pendingOpponentReturnSeconds:F2}s recoveryT={pendingAIRecoverySeconds:F2}s margin={pendingRecoveryMarginSeconds:F2}s " +
            $"clearanceFloor={pendingRecoveryIntendedClearanceFloor:F2}m momentum={pendingOpponentMomentumReversal01:F2}/{pendingOpponentMomentumTurnSeconds:F2}s.");
    }

    private float EstimateTimeToReachAndPlant(Vector3 target, float reactionDelay, float targetSpeed, float stopDistance)
    {
        Vector3 desiredDirection = target - transform.position;
        desiredDirection.y = 0f;
        float remainingDistance = desiredDirection.magnitude;
        float elapsed = Mathf.Max(0f, reactionDelay);
        if (remainingDistance <= Mathf.Max(0.01f, stopDistance) && (movement == null || movement.CurrentSpeed <= Mathf.Max(0.01f, plantedContactMaxResidualSpeed)))
            return elapsed;

        if (movement == null)
            return elapsed + remainingDistance / Mathf.Max(0.01f, targetSpeed);

        desiredDirection /= Mathf.Max(0.0001f, remainingDistance);
        float speed = Mathf.Max(0f, movement.CurrentSpeed);
        Vector3 currentVelocity = movement.PlanarVelocity;
        currentVelocity.y = 0f;
        if (speed > 0.15f && currentVelocity.sqrMagnitude > 0.0001f)
        {
            float directionDot = Vector3.Dot(currentVelocity.normalized, desiredDirection);
            if (movement.useWrongFooting && directionDot <= movement.wrongFootDot)
            {
                elapsed += EstimateWrongFootResetTime(speed, targetSpeed);
                speed = 0f;
            }
            else if (directionDot < movement.smoothTurnDot)
            {
                float turn01 = Mathf.InverseLerp(movement.smoothTurnDot, movement.wrongFootDot, directionDot);
                elapsed += Mathf.Lerp(0.02f, 0.08f, turn01);
                speed *= Mathf.Lerp(1f, 0.55f, turn01);
            }
        }

        float maxSpeed = Mathf.Max(0.01f, targetSpeed);
        float residualSpeed = Mathf.Min(
            Mathf.Max(0.01f, plantedContactMaxResidualSpeed),
            Mathf.Max(0.01f, maxSpeed));
        float deceleration = Mathf.Max(0.01f, movement.deceleration);
        const float simulationStep = 1f / 120f;
        const float maximumSimulationTime = 4f;
        while (elapsed <= maximumSimulationTime)
        {
            float stoppingDistance = Mathf.Max(0f, (speed * speed - residualSpeed * residualSpeed) / (2f * deceleration));
            bool insideBrakeZone = remainingDistance <= Mathf.Max(0.01f, stopDistance) + stoppingDistance + Mathf.Max(0f, contactBrakeBuffer);
            float step = simulationStep;
            if (insideBrakeZone)
            {
                // Do not brake all the way to zero while still outside the
                // contact stop distance. The old version could stop early and
                // then never complete the final controlled approach.
                speed = speed > residualSpeed
                    ? Mathf.MoveTowards(speed, residualSpeed, deceleration * step)
                    : Mathf.MoveTowards(speed, residualSpeed, EstimateMotorAcceleration(speed, maxSpeed) * step);
            }
            else
            {
                speed = Mathf.MoveTowards(speed, maxSpeed, EstimateMotorAcceleration(speed, maxSpeed) * step);
            }

            remainingDistance = Mathf.Max(0f, remainingDistance - speed * step);
            elapsed += step;
            if (remainingDistance <= Mathf.Max(0.01f, stopDistance) && speed <= residualSpeed + 0.01f)
                return elapsed;
        }

        return float.PositiveInfinity;
    }

    private float EstimateMotorAcceleration(float speed, float targetSpeed)
    {
        if (movement == null || targetSpeed <= 0.001f)
            return 0f;

        float speed01 = Mathf.Clamp01(speed / targetSpeed);
        float taper = 1f - Mathf.Pow(speed01, Mathf.Max(0.01f, movement.accelerationTaperExponent));
        taper = Mathf.Max(Mathf.Clamp01(movement.sustainedAccelerationFloor), taper);
        float acceleration = Mathf.Max(0f, movement.maxAcceleration) * taper;
        if (!movement.useExplosiveStart)
            return acceleration;

        float burstRange = Mathf.Max(0.01f, movement.firstStepSpeedFraction);
        float burst01 = Mathf.Clamp01(speed01 / burstRange);
        float burstFalloff = 1f - Mathf.Pow(burst01, Mathf.Max(0.01f, movement.firstStepCurveExponent));
        float burstMultiplier = Mathf.Lerp(1f, Mathf.Max(1f, movement.firstStepAccelerationMultiplier), burstFalloff);
        return acceleration * burstMultiplier;
    }

    private Vector3 ContactToStance(Vector3 contact)
    {
        Vector3 offset = bodyOffsetFromContact;
        float minOffset = Mathf.Max(0f, minimumBodyContactOffset);
        if (Mathf.Abs(offset.x) < minOffset)
            offset.x = minOffset * (Mathf.Abs(offset.x) > 0.001f ? Mathf.Sign(offset.x) : GetAISideSign());
        if (Mathf.Sign(offset.x) != GetAISideSign())
            offset.x = Mathf.Abs(offset.x) * GetAISideSign();

        float stanceZ = contact.z + offset.z;
        if (useLateralContactAdjustment && lateralContactOffset > 0f)
        {
            if (Mathf.Abs(contactLateralSideSign) < 0.5f)
            {
                float lateralDelta = contact.z - transform.position.z;
                if (Mathf.Abs(lateralDelta) > Mathf.Max(0f, lateralContactChoiceDeadband))
                    contactLateralSideSign = Mathf.Sign(lateralDelta);
                else
                {
                    float positiveSideStanceZ = contact.z - lateralContactOffset;
                    float negativeSideStanceZ = contact.z + lateralContactOffset;
                    contactLateralSideSign = Mathf.Abs(transform.position.z - positiveSideStanceZ) <= Mathf.Abs(transform.position.z - negativeSideStanceZ)
                        ? 1f
                        : -1f;
                }
            }
            stanceZ -= contactLateralSideSign * Mathf.Max(0f, lateralContactOffset);
        }

        return ClampToMovementBounds(new Vector3(contact.x + offset.x, transform.position.y, stanceZ));
    }

    private bool CanAuthoritativeZoneReachContact(Vector3 contact, Vector3 stance)
    {
        if (hitController == null ||
            !hitController.TryGetAuthoritativeContactZonePose(out Vector3 currentCenter, out Quaternion rotation, out Vector3 radii))
        {
            return true;
        }

        Vector3 rootToZoneCenter = currentCenter - transform.position;
        Vector3 predictedZoneCenter = stance + rootToZoneCenter;
        float ballRadius = sweptContactBallRadius > 0f
            ? sweptContactBallRadius
            : hitController.GetBallContactRadius(ball);
        radii += Vector3.one * Mathf.Max(0f, ballRadius);
        Vector3 local = Quaternion.Inverse(rotation) * (contact - predictedZoneCenter);
        float normalized =
            (local.x * local.x) / (radii.x * radii.x) +
            (local.y * local.y) / (radii.y * radii.y) +
            (local.z * local.z) / (radii.z * radii.z);
        return normalized <= 1f;
    }

    private void MoveTowardCurrentPlan()
    {
        if (!currentPlan.valid)
        {
            StopMoving();
            return;
        }

        float remainingContactTime = currentPlan.worldContactTime > 0f
            ? Mathf.Max(0f, currentPlan.worldContactTime - Time.time)
            : Mathf.Max(0f, currentPlan.timeUntilContact);
        float liveRunningArrival = EstimateTimeToReachAtFullSpeed(
            currentPlan.stancePoint,
            0f,
            moveSpeed,
            contactStopDistance);
        bool mustSprint = currentPlan.requiresFullSpeed ||
            currentPlan.pursuitMode != PursuitMode.Comfortable ||
            float.IsPositiveInfinity(liveRunningArrival) ||
            liveRunningArrival + Mathf.Max(0f, fullSpeedPursuitSlack) >= remainingContactTime;
        if (mustSprint)
        {
            // An urgent intercept must use the real configured top speed. Do
            // not run it through the generic 2.7 m approach slowdown or the
            // early planted brake that made 7 m/s plans move at a jog.
            MoveToward(
                ClampToMovementBounds(currentPlan.stancePoint),
                contactStopDistance,
                moveSpeed,
                false,
                0f,
                false);
            return;
        }

        bool plantedContact = !enableRunningContactPlanning || currentPlan.approachMode == ContactApproachMode.Planted;
        if (!plantedContact)
        {
            MoveThroughCurrentPlan();
            return;
        }

        // Once the plan is classified as planted, use the normal arrival brake
        // even for a small stretch adjustment. That keeps low-speed contact
        // movement from carrying through as if it were a sprint.
        bool balancedContact = currentPlan.approachMode == ContactApproachMode.Planted;
        float stopDistance = balancedContact
            ? contactStopDistance
            : Mathf.Min(contactStopDistance, 0.12f);

        // A stretch or emergency chase must not begin the normal arrival brake
        // several metres before a live, moving contact point.
        MoveToward(
            ClampToMovementBounds(currentPlan.stancePoint),
            stopDistance,
            moveSpeed,
            balancedContact,
            balancedContact ? contactBrakeBuffer : 0f,
            balancedContact);
    }

    private void MoveThroughCurrentPlan()
    {
        Vector3 target = ClampToMovementBounds(currentPlan.stancePoint);
        Vector3 toTarget = target - transform.position;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;
        float targetSpeed = Mathf.Clamp(
            Mathf.Max(Mathf.Max(0f, runningContactMinimumApproachSpeed), currentPlan.approachMoveSpeed),
            0.01f,
            Mathf.Max(0.01f, moveSpeed));

        Vector3 direction;
        if (distance > 0.025f)
        {
            direction = toTarget / distance;
        }
        else if (movement != null && movement.PlanarVelocity.sqrMagnitude > 0.0001f)
        {
            direction = movement.PlanarVelocity.normalized;
        }
        else
        {
            direction = currentPlan.contactPoint - transform.position;
            direction.y = 0f;
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : GetFacingDirection();
        }

        if (movement != null)
            movement.SetExternalMove(direction, targetSpeed);
        else
            transform.position += direction * targetSpeed * Time.deltaTime;

        FaceDirection(GetFacingDirection());
    }

    private void StopForPlantedContact()
    {
        if (!enableRunningContactPlanning || !currentPlan.valid || currentPlan.approachMode == ContactApproachMode.Planted)
            StopMoving();
    }

    private bool IsRunningContactPlan()
    {
        return enableRunningContactPlanning && currentPlan.valid && currentPlan.significantRunningContact;
    }

    private void TickRecoverMovement(bool stopWhenArrived)
    {
        if (!hasRuntimeBasePosition)
            CacheBasePosition();

        Vector3 recoveryTarget = runtimeBasePosition;
        float recoverySpeed = moveSpeed * Mathf.Max(0.1f, recoverySpeedMultiplier);
        WtaMatchplayObservationLogic wta = GetActiveWtaMatchplayLogic();
        if (wta != null)
        {
            bool defensiveRecovery = pendingRecoveryBall || pendingRallyState == MatchplayRallyState.Defensive;
            bool offensiveRecovery = !defensiveRecovery && pendingRallyState == MatchplayRallyState.Offensive;
            recoveryTarget = wta.GetRecoveryTarget(
                recoveryTarget,
                GetAISideSign(),
                defensiveRecovery,
                offensiveRecovery);
            bool wideContact = wta.IsWideContact(pendingPlannedContactPoint, runtimeBasePosition);
            recoverySpeed *= wta.GetRecoverySpeedMultiplier(wideContact);

            if (wta.debugLogs && wideContact)
            {
                Debug.Log($"[WTA RECOVERY] wide contact={pendingPlannedContactPoint} target={recoveryTarget} " +
                    $"state={pendingRallyState} speed={recoverySpeed:F2}m/s.");
            }
        }

        // Recover at full pace while the ball is travelling to the player.
        // Once it reaches the player's swing zone, clear the recovery command
        // and brake before racket contact. The next incoming shot can then use
        // the explosive first step instead of paying for a centreward reversal.
        bool opponentCanContactNow = IsBallInOpponentSwingZone();
        if (opponentCanContactNow)
        {
            StopMoving();
            FaceDirection(GetFacingDirection());
            return;
        }

        bool arrived = MoveToward(
            ClampToMovementBounds(recoveryTarget),
            recoveryStopDistance,
            recoverySpeed,
            false,
            0f,
            false);
        if (arrived && stopWhenArrived)
            StopMoving();
    }

    private Vector3 GetReachableRecoveryTarget(Vector3 recoveryTarget, float recoverySpeed)
    {
        if (!enableRecoveryBrakeSettle || !IsMatchplayMode ||
            recoveryMovementStartedAt < 0f || pendingOpponentReturnSeconds <= 0f)
        {
            return recoveryTarget;
        }

        float elapsed = Mathf.Max(0f, Time.time - recoveryMovementStartedAt);
        float remainingOpponentTime = pendingOpponentReturnSeconds - elapsed;
        float settleLead = Mathf.Max(
            Mathf.Max(0f, recoverySettleLeadTime),
            Mathf.Max(0f, recoverySettleMinimumRemainingTime));

        if (remainingOpponentTime <= settleLead)
        {
            if (debugRecoveryBrakeSettle)
                Debug.Log($"[AI RECOVERY SETTLE] remaining={remainingOpponentTime:F2}s <= settleLead={settleLead:F2}s; holding current position={transform.position}.");
            return transform.position;
        }

        Vector3 currentVelocity = movement != null ? movement.PlanarVelocity : Vector3.zero;
        float timeToBase = EstimateTravelTimeAndPlantFrom(
            transform.position,
            currentVelocity,
            recoveryTarget,
            recoverySpeed,
            recoveryStopDistance,
            recoveryBrakeBuffer);
        float availableTravelTime = Mathf.Max(0f, remainingOpponentTime - settleLead);
        bool cannotReachBaseAndSettle = timeToBase > availableTravelTime;
        if (!cannotReachBaseAndSettle)
            return recoveryTarget;

        Vector3 towardBase = recoveryTarget - transform.position;
        towardBase.y = 0f;
        float baseDistance = towardBase.magnitude;
        if (baseDistance <= Mathf.Max(0.01f, recoveryStopDistance))
            return transform.position;

        // The player will contact before full recovery finishes.  Advance only
        // as far as the actual movement motor can travel, then arrive/brake at
        // that intermediate point.  This keeps the AI recovering toward base
        // instead of either over-running or freezing where it hit the ball.
        float reachableDistance = EstimateReachableMoveDistance(
            recoveryTarget,
            availableTravelTime,
            0f,
            recoverySpeed);
        float partialDistance = Mathf.Clamp(reachableDistance, 0f, baseDistance);
        Vector3 partialTarget = transform.position + towardBase / baseDistance * partialDistance;
        partialTarget = ClampToMovementBounds(partialTarget);

        if (debugRecoveryBrakeSettle)
        {
            Debug.Log($"[AI RECOVERY PARTIAL] base arrival unavailable before player contact. " +
                $"timeToBase={timeToBase:F2}s remaining={remainingOpponentTime:F2}s " +
                $"settleLead={settleLead:F2}s current={transform.position} base={recoveryTarget} " +
                $"partial={partialTarget} reachable={partialDistance:F2}m.");
        }

        return partialTarget;
    }

    private bool IsBallInOpponentSwingZone()
    {
        if (ball == null)
            return false;

        Transform opponent = ResolveMatchplayOpponent(-GetAISideSign());
        if (opponent == null)
            return false;

        hitController opponentHitController = opponent.GetComponent<hitController>();
        if (opponentHitController == null)
            opponentHitController = opponent.GetComponentInChildren<hitController>();
        if (opponentHitController == null)
            return false;

        float ballRadius = opponentHitController.GetBallContactRadius(ball);
        return opponentHitController.IsPointInsideAuthoritativeContactZone(ball.position, ballRadius);
    }

    private bool MoveToward(Vector3 target, float stopDistance, float targetSpeed)
    {
        return MoveToward(target, stopDistance, targetSpeed, true, contactBrakeBuffer);
    }

    private bool MoveToward(Vector3 target, float stopDistance, float targetSpeed, bool useArrivalBrake, float brakeBuffer, bool useApproachSlowDown = true)
    {
        return MoveTowardInternal(ClampToMovementBounds(target), stopDistance, targetSpeed, useArrivalBrake, brakeBuffer, useApproachSlowDown);
    }

    private bool MoveTowardUnclamped(Vector3 target, float stopDistance, float targetSpeed, bool useArrivalBrake, float brakeBuffer)
    {
        return MoveTowardInternal(target, stopDistance, targetSpeed, useArrivalBrake, brakeBuffer, true);
    }

    private bool MoveTowardInternal(Vector3 target, float stopDistance, float targetSpeed, bool useArrivalBrake, float brakeBuffer, bool useApproachSlowDown)
    {
        Vector3 toTarget = target - transform.position;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;

        if (distance <= Mathf.Max(0.01f, stopDistance))
        {
            StopMoving();
            return true;
        }

        Vector3 dir = toTarget / distance;
        if (useArrivalBrake && ShouldBrakeForArrival(dir, distance, stopDistance, brakeBuffer))
        {
            StopMoving();
            FaceDirection(GetFacingDirection());
            return false;
        }

        float controlledSpeed = useApproachSlowDown
            ? GetControlledMoveSpeed(distance, stopDistance, targetSpeed)
            : targetSpeed;
        if (movement != null)
            movement.SetExternalMove(dir, Mathf.Max(0.01f, controlledSpeed));
        else
            transform.position += dir * Mathf.Max(0.01f, controlledSpeed) * Time.deltaTime;

        FaceDirection(GetFacingDirection());
        return false;
    }

    private bool ShouldBrakeForArrival(Vector3 directionToTarget, float distance, float stopDistance, float brakeBuffer)
    {
        if (movement == null)
            return false;

        float speed = movement.CurrentSpeed;
        if (speed <= 0.25f)
            return false;

        Vector3 velocity = movement.PlanarVelocity;
        velocity.y = 0f;
        if (velocity.sqrMagnitude <= 0.0001f)
            return false;

        float movingTowardTarget = Vector3.Dot(velocity.normalized, directionToTarget);
        if (movingTowardTarget <= 0.15f)
            return false;

        float decel = Mathf.Max(0.1f, movement.deceleration);
        float stoppingDistance = (speed * speed) / (2f * decel);
        float brakeDistance = Mathf.Max(0f, stopDistance) + stoppingDistance + Mathf.Max(0f, brakeBuffer);

        return distance <= brakeDistance;
    }

    private float GetControlledMoveSpeed(float distance, float stopDistance, float targetSpeed)
    {
        float slowDistance = Mathf.Max(stopDistance + 0.01f, approachSlowDownDistance);
        if (distance >= slowDistance)
            return targetSpeed;

        float t = Mathf.InverseLerp(Mathf.Max(0.01f, stopDistance), slowDistance, distance);
        return Mathf.Lerp(Mathf.Max(0.05f, minimumApproachSpeed), targetSpeed, t);
    }

    private void StopMoving()
    {
        if (movement != null)
            movement.ClearExternalMove();
    }

    private void FaceDirection(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 12f * Time.deltaTime);
    }

    private Vector3 GetFacingDirection()
    {
        return GetAISideSign() > 0f ? Vector3.left : Vector3.right;
    }

    private bool IsIncomingBall()
    {
        if (ball == null || didHitThisBall || abandonedIncomingBall)
            return false;

        Vector3 vel = ball.linearVelocity;
        if (vel.sqrMagnitude < 0.25f)
            return false;

        float sideSign = GetAISideSign();
        if (sideSign * vel.x <= 0.05f)
            return false;

        return true;
    }

    private bool IsBallInSwingZone()
    {
        if (ball == null)
            return false;

        Collider zone = hitController != null ? hitController.hitZone : null;
        if (zone != null)
        {
            Vector3 closest = zone.ClosestPoint(ball.position);
            if ((closest - ball.position).sqrMagnitude <= 0.0004f)
                return true;
        }

        return HorizontalDistance(transform.position, ball.position) <= Mathf.Max(0.1f, fallbackSwingRadius);
    }

    private bool ShouldPreArmFastServeSwipe()
    {
        if (!fastServeReturnAssist || !currentPlan.valid)
            return false;

        float fastServe01 = GetFastServeReturn01(currentPlan.incomingVelocity.magnitude);
        if (fastServe01 <= 0f)
            return false;

        float leadTime = Mathf.Max(
            Mathf.Max(0.01f, virtualSwipeDuration),
            Mathf.Max(0.01f, fastServeSwipePreparationLeadTime));
        return currentPlan.timeUntilContact <= leadTime;
    }

    private bool CanOverwritePreparedSwipe()
    {
        if (!currentPlan.valid || finalInterceptFrozen)
            return false;

        if (lockSwipeReplanningAtSwingZoneEntry && IsBallInSwingZone())
            return false;

        if (swipeStatusTrackingIncomingShot &&
            swipePreparationCount >= Mathf.Max(1, maxSwipePreparationsPerIncomingBall))
            return false;

        float fastServe01 = GetFastServeReturn01(currentPlan.incomingVelocity.magnitude);
        float lockTime = fastServe01 > 0f
            ? Mathf.Max(0.01f, fastServeSwipeOverwriteLockTime)
            : Mathf.Max(0.01f, normalSwipeOverwriteLockTime);
        return currentPlan.timeUntilContact > lockTime;
    }

    private bool IsBallInTightHitZone()
    {
        if (!TryGetAuthoritativeContact(out HitContactConfirmation contact))
            return false;

        if (logTightHitZoneHeight &&
            hitController.TryGetAuthoritativeContactZonePose(out Vector3 center, out Quaternion rotation, out Vector3 radii))
        {
            BallController controller = ball.GetComponent<BallController>();
            int shotSequence = controller != null ? controller.ShotSequence : 0;
            int shotKey = controller != null ? (controller.GetInstanceID() * 397) ^ shotSequence : 0;
            if (shotKey != lastTightHitZoneHeightLoggedShot)
            {
                lastTightHitZoneHeightLoggedShot = shotKey;
                Vector3 local = Quaternion.Inverse(rotation) * (contact.contactPosition - center);
                float normalized =
                    (local.x * local.x) / (radii.x * radii.x) +
                    (local.y * local.y) / (radii.y * radii.y) +
                    (local.z * local.z) / (radii.z * radii.z);
                string plan = currentPlan.valid
                    ? $"plannedContact={currentPlan.contactPoint} stance={currentPlan.stancePoint} timeToContact={currentPlan.timeUntilContact:F2}s"
                    : "plannedContact=none";
                Debug.Log($"[TIGHT HEIGHT AI] shot={shotSequence} ballHeight={contact.contactPosition.y:F2}m local={local} swept={contact.swept} " +
                    $"zoneCenter={center} verticalRange=[{center.y - radii.y:F2},{center.y + radii.y:F2}] normalized={normalized:F3} " +
                    $"ai={transform.position} {plan}.");
            }
        }

        return true;
    }

    private bool TryGetTightContactLock(out HitContactConfirmation contact)
    {
        contact = default;
        if (!useDescendingTightContactLock || ball == null || didHitThisBall || abandonedIncomingBall)
            return false;
        if (!TryGetAuthoritativeContact(out contact))
            return false;
        if (!IsOnOwnSide(contact.contactPosition, 0.05f))
            return false;
        if (ShouldDeferTightContactForPlannedFastIntercept(contact))
            return false;
        if (ShouldDeferTightContactForFastBaselineBounce())
            return false;
        if (ShouldDeferTightContactForLiftedBounce())
            return false;
        if (tightContactLockRequiresDescending &&
            ball.linearVelocity.y > Mathf.Max(0f, tightContactLockMaxVerticalSpeed))
            return false;

        return true;
    }

    private bool ShouldDeferTightContactForPlannedFastIntercept(HitContactConfirmation contact)
    {
        if (!gateFastBallContactToPlannedIntercept || ball == null || !currentPlan.valid)
            return false;

        float incomingReferenceSpeed = Mathf.Max(
            GetIncomingReferenceSpeedMps(),
            Mathf.Max(ball.linearVelocity.magnitude, currentPlan.incomingVelocity.magnitude));
        if (GetFastBallIntercept01(incomingReferenceSpeed) <= 0f)
            return false;

        if (currentPlan.timeUntilContact <= Mathf.Max(0f, fastBallContactBailoutTime))
            return false;

        float tolerance = currentPlan.pursuitMode == PursuitMode.Emergency
            ? Mathf.Max(0.05f, fastBallEmergencyContactTolerance)
            : Mathf.Max(0.05f, fastBallPlannedContactTolerance);
        return Vector3.Distance(contact.contactPosition, currentPlan.contactPoint) > tolerance;
    }

    private bool ShouldDeferTightContactForFastBaselineBounce()
    {
        if (ball == null || !currentPlan.valid || currentPlan.ownSideBounceCount <= 0)
            return false;

        // The plan has found a playable post-bounce ball. Do not let the
        // 120 Hz tight-zone lock override it with a hurried forward volley
        // while the AI is stationed deep at the baseline.
        if (!ShouldPreferFastBaselinePostBounce(GetIncomingReferenceSpeedMps(), GetKnownOwnSideBounceCount()))
            return false;

        return IsForwardOfCurrentBaselinePosition(ball.position);
    }

    private PostBounceReturnProfile GetPostBounceReturnProfile()
    {
        if (!useFastLowBounceDefence || !currentPlan.valid || currentPlan.ownSideBounceCount <= 0)
            return PostBounceReturnProfile.Normal;

        float incomingSpeed = Mathf.Max(GetIncomingReferenceSpeedMps(), currentPlan.incomingVelocity.magnitude);
        if (incomingSpeed < Mathf.Max(0f, lowBounceFastIncomingSpeedMps))
            return PostBounceReturnProfile.Normal;

        float risingMinHeight = Mathf.Min(preferredRisingContactHeightRange.x, preferredRisingContactHeightRange.y);
        float risingMaxHeight = Mathf.Max(preferredRisingContactHeightRange.x, preferredRisingContactHeightRange.y);
        bool plannedRisingContact =
            currentPlan.pursuitMode != PursuitMode.Emergency &&
            currentPlan.timeUntilContact >= Mathf.Max(0f, lowBounceLiftedContactMinimumLeadTime) &&
            currentPlan.incomingVelocity.y >= Mathf.Max(0f, risingContactMinimumVerticalSpeed) &&
            currentPlan.contactPoint.y >= risingMinHeight &&
            currentPlan.contactPoint.y <= risingMaxHeight;

        if (plannedRisingContact)
            return PostBounceReturnProfile.LiftedRising;

        bool lowContact = currentPlan.contactPoint.y <= Mathf.Max(0.05f, lowBounceDefensiveContactHeight) ||
            (ball != null && ball.position.y <= Mathf.Max(0.05f, lowBounceDefensiveContactHeight));
        return lowContact ? PostBounceReturnProfile.LowDefensive : PostBounceReturnProfile.Normal;
    }

    private bool ShouldDeferTightContactForLiftedBounce()
    {
        if (GetPostBounceReturnProfile() != PostBounceReturnProfile.LiftedRising || ball == null)
            return false;

        if (ball.linearVelocity.y <= Mathf.Max(0f, risingContactMinimumVerticalSpeed))
            return false;

        float lowerAcceptedHeight = currentPlan.contactPoint.y - Mathf.Max(0f, lowBounceLiftedContactHeightTolerance);
        return ball.position.y < lowerAcceptedHeight;
    }

    private BaseShotType PickLowBounceDefensiveShotType()
    {
        if (!currentPlan.valid)
            return BaseShotType.Topspin;

        Vector3 incomingDirection = currentPlan.incomingVelocity;
        incomingDirection.y = 0f;
        if (incomingDirection.sqrMagnitude < 0.001f)
            return BaseShotType.Topspin;

        incomingDirection.Normalize();
        Vector3 topspinAxis = Vector3.Cross(Vector3.up, incomingDirection);
        float signedSpin = Vector3.Dot(currentPlan.incomingSpin, topspinAxis);

        // A skidding/backspin ball needs a topspin lift. Against a strong topspin
        // kick, a controlled slice is a viable safe change only when contact is
        // not at the absolute floor.
        if (signedSpin < -20f)
            return BaseShotType.Topspin;
        if (signedSpin > 65f && currentPlan.contactPoint.y >= 0.62f)
            return BaseShotType.Slice;
        return BaseShotType.Topspin;
    }

    private Vector3 EnforceLowBounceDefensiveTarget(Vector3 target)
    {
        if (!lastTargetPick.valid)
            return target;

        float minX = Mathf.Min(lastTargetPick.xRange.x, lastTargetPick.xRange.y);
        float maxX = Mathf.Max(lastTargetPick.xRange.x, lastTargetPick.xRange.y);
        float minZ = Mathf.Min(lastTargetPick.zRange.x, lastTargetPick.zRange.y);
        float maxZ = Mathf.Max(lastTargetPick.zRange.x, lastTargetPick.zRange.y);
        float minimumDepth01 = Mathf.Clamp01(lowBounceMinimumTargetDepth01);
        if (GetTargetDepth01(target.x, minX, maxX) < minimumDepth01)
            target.x = GetXAtTargetDepth01(minX, maxX, minimumDepth01);

        // Preserve the selected lane; this only keeps an emergency reply away
        // from the extreme sideline rather than forcing it down the middle.
        float sidePadding = (maxZ - minZ) * Mathf.Clamp01(lowBounceTargetSidePadding01);
        target.z = Mathf.Clamp(target.z, minZ + sidePadding, maxZ - sidePadding);
        target.y = returnTargetY;
        return target;
    }

    private void UpdateEarlyAIBackswingCharge()
    {
        if (!currentPlan.valid)
            return;

        float pressure = GetShotPressure01();
        if (IsBaselineVolley(currentPlan))
            return;

        float timeToContact = Mathf.Max(0f, currentPlan.timeUntilContact);
        if (timeToContact < Mathf.Max(0f, aiBackswingEarlyMinContactTime))
            return;

        float target = pressure > 0.72f
            ? aiBackswingDefensiveTarget
            : pressure < 0.35f ? aiBackswingNeutralTarget : Mathf.Lerp(aiBackswingDefensiveTarget, aiBackswingNeutralTarget, 0.65f);

        if (IsMatchplayMode && pressure < 0.45f)
            target = Mathf.Max(target, aiBackswingNeutralTarget);
        else if (IsMatchplayMode && pressure < 0.62f)
            target = Mathf.Max(target, Mathf.Lerp(aiBackswingNeutralTarget, aiBackswingAttackTarget, 0.18f));

        StartOrRaiseAIBackswingCharge(target, "early tracking");
    }

    public void SetServiceHoldPosition(Vector3 position)
    {
        serviceHoldPosition = position;
        serviceHoldActive = true;
        serviceReturnPreparationActive = false;
        serviceReturnHitAllowed = false;
        currentPlan = default;
        pendingSwipe = default;
        didHitThisBall = true;
        abandonedIncomingBall = false;
        contactLateralSideSign = 0f;
        state = AIState.Idle;
    }

    public void ClearServiceHoldPosition()
    {
        serviceHoldActive = false;
        movement?.ClearExternalMove();
    }

    public void BeginServeReturnPreparation()
    {
        serviceHoldActive = false;
        serviceReturnPreparationActive = true;
        serviceReturnHitAllowed = false;
        participatesInRally = true;
        didHitThisBall = false;
        abandonedIncomingBall = false;
        currentPlan = default;
        pendingSwipe = default;
        contactLateralSideSign = 0f;
        state = AIState.Idle;
        movement?.ClearExternalMove();
        ConfigureMovementForAI();
        if (debugFastServeReturnLogs)
            Debug.Log($"[TennisAI FAST SERVE] return preparation started; reaction={fastServeReactionTime:F2}s reachBonus={fastServeReachToleranceBonus:F2}m.");
    }
    public void BeginRallyAfterService()
    {
        bool preservePreparedReturn = serviceReturnPreparationActive && currentPlan.valid;
        serviceHoldActive = false;
        serviceReturnPreparationActive = false;
        serviceReturnHitAllowed = true;
        participatesInRally = true;
        didHitThisBall = false;
        abandonedIncomingBall = false;
        if (!preservePreparedReturn)
        {
            currentPlan = default;
            pendingSwipe = default;
            state = AIState.Idle;
        }
        movement?.ClearExternalMove();
        ConfigureMovementForAI();
    }

    private void TickServiceHold()
    {
        currentPlan = default;
        pendingSwipe = default;
        didHitThisBall = true;
        abandonedIncomingBall = false;
        state = AIState.Idle;
        hasDesiredReturnTargetPosition = false;

        if (hitController != null)
            hitController.ballIsInHittingZone = false;
        if (aiReticleRenderer != null)
            aiReticleRenderer.enabled = false;

        Vector3 target = serviceHoldPosition;
        target.y = transform.position.y;
        bool arrived = MoveTowardUnclamped(
            target,
            inactiveStopDistance,
            moveSpeed * Mathf.Max(0.1f, recoverySpeedMultiplier),
            true,
            recoveryBrakeBuffer);
        if (arrived)
            StopMoving();
    }

    private void UpdateAIBackswingChargeForShot(float desiredReturnSpeed, float risk, float pressure, bool matchplayDecision, bool baselineVolley)
    {
        float target = baselineVolley || pressure > 0.72f
            ? aiBackswingDefensiveTarget
            : aiBackswingNeutralTarget;

        bool wantsPower =
            desiredReturnSpeed >= Mathf.Max(0f, aiBackswingFastShotThreshold) ||
            risk >= 6.4f ||
            pendingShotType == BaseShotType.Flat ||
            (matchplayDecision && pressure < 0.45f);

        if (wantsPower)
            target = aiBackswingAttackTarget;
        else if (matchplayDecision && pressure < 0.62f && desiredReturnSpeed >= 18.5f)
            target = Mathf.Max(target, Mathf.Lerp(aiBackswingNeutralTarget, aiBackswingAttackTarget, 0.28f));
        else if (Random.value < Mathf.Clamp01(aiBackswingDeceptionChance) && pressure < 0.55f)
            target = Mathf.Max(target, Mathf.Lerp(aiBackswingNeutralTarget, aiBackswingAttackTarget, 0.75f));

        float fastServeReturn01 = currentPlan.valid
            ? GetFastServeReturn01(currentPlan.incomingVelocity.magnitude)
            : 0f;
        if (fastServeReturn01 > 0f)
            target = Mathf.Max(target, Mathf.Lerp(aiBackswingNeutralTarget, aiBackswingAttackTarget, fastServeReturn01));

        StartOrRaiseAIBackswingCharge(target, "shot intent");
    }

    private float GetAIVirtualForwardSwingProgress(
        bool matchplayDecision,
        bool practiceDropShot,
        bool baselineVolley,
        float risk,
        float pressure,
        float desiredReturnSpeed,
        float quality)
    {
        if (practiceDropShot)
            return Mathf.Clamp01(matchplayTouchForwardSwingProgress);

        if (baselineVolley || pressure > 0.72f)
            return Mathf.Max(Mathf.Clamp01(quality), Mathf.Clamp01(matchplayDefensiveForwardSwingProgress));

        if (matchplayDecision)
        {
            float requestedPace01 = Mathf.InverseLerp(20f, 28.5f, desiredReturnSpeed);
            float progress = Mathf.Lerp(matchplayDefensiveForwardSwingProgress, matchplayNormalForwardSwingProgress, requestedPace01);
            return Mathf.Clamp01(Mathf.Max(progress, quality));
        }

        return Mathf.Clamp01(quality);
    }

    private void StartOrRaiseAIBackswingCharge(float targetScale, string reason)
    {
        targetScale = Mathf.Clamp01(targetScale);
        if (!aiBackswingCharging)
        {
            aiBackswingCharging = true;
            aiBackswingChargeStartTime = Time.time;
            aiBackswingTargetScale = targetScale;
            if (debugBackswingChargeLogs)
                Debug.Log($"[TennisAI BACKSWING] start reason={reason}, target={aiBackswingTargetScale:F2}, timeToContact={(currentPlan.valid ? currentPlan.timeUntilContact : 0f):F2}s");
            return;
        }

        if (targetScale > aiBackswingTargetScale)
        {
            aiBackswingTargetScale = targetScale;
            if (debugBackswingChargeLogs)
                Debug.Log($"[TennisAI BACKSWING] raise reason={reason}, target={aiBackswingTargetScale:F2}, charged={ComputeCurrentAIBackswingScale():F2}");
        }
    }

    private float ComputeCurrentAIBackswingScale()
    {
        if (!aiBackswingCharging)
            return 0f;

        float chargeSeconds = Mathf.Max(0.05f, aiBackswingFullChargeSeconds);
        float timeHeld = Mathf.Max(0f, Time.time - aiBackswingChargeStartTime);
        return Mathf.Clamp01(timeHeld / chargeSeconds) * Mathf.Clamp01(aiBackswingTargetScale);
    }

    private float ComputeAIBackswingCapSpeed(float backswingScale)
    {
        return BaseShotLibrary.GetBackswingCapSpeed(backswingScale);
    }

    private void ApplyCurrentAIBackswingToPendingSwipe()
    {
        float charge = ComputeCurrentAIBackswingScale();
        float cap = ComputeAIBackswingCapSpeed(charge);
        pendingSwipe = new SwipeData(
            pendingSwipe.isLMB,
            pendingSwipe.isRMB,
            pendingSwipe.aimDir,
            pendingSwipe.speed,
            pendingSwipe.quality,
            pendingSwipe.normDist,
            pendingSwipe.holdScale,
            pendingSwipe.spinIntent,
            charge,
            pendingSwipe.forwardSwingProgress,
            cap);

        visualBackswingScale = charge;

        if (debugBackswingChargeLogs)
            Debug.Log($"[TennisAI BACKSWING] contact charge={charge:F2}, cap={cap:F2}m/s, held={(aiBackswingCharging ? Time.time - aiBackswingChargeStartTime : 0f):F2}s, target={aiBackswingTargetScale:F2}");
    }

    private void ResetAIBackswingCharge()
    {
        aiBackswingCharging = false;
        aiBackswingChargeStartTime = 0f;
        aiBackswingTargetScale = 0f;
    }
    private void PrepareVirtualSwipe(string reason)
    {
        if (returnTarget == null)
            returnTarget = EnsureRuntimeReturnTarget();

        RecordSwipePreparationForGizmo();

        RefreshCurrentPlanWithLiveIncomingBallState();

        float pressure = GetShotPressure01();
        float incomingReferenceSpeed = GetIncomingReferenceSpeedMps();
        float fastServeReturn01 = GetFastServeReturn01(incomingReferenceSpeed);
        bool suppressTouchForFastBall =
            incomingReferenceSpeed >= Mathf.Max(0f, fastBallNoTouchThresholdMps);
        float accuracyPressure = pressure * Mathf.Lerp(1f, Mathf.Clamp01(fastReturnPressureErrorScale), fastServeReturn01);
        bool baselineVolley = IsBaselineVolley(currentPlan);
        float volleyDifficulty = baselineVolley ? GetBaselineVolleyDifficulty01(currentPlan) : 0f;
        PostBounceReturnProfile postBounceReturnProfile = GetPostBounceReturnProfile();
        WtaMatchplayObservationLogic wta = GetActiveWtaMatchplayLogic();
        bool wtaCompactFirstServeReturn = wta != null && serviceReturnPreparationActive &&
            wta.IsBigFirstServe(incomingReferenceSpeed);
        bool useMatchplayDecision = TryBuildMatchplayDecision(pressure, baselineVolley, volleyDifficulty, out MatchplayDecision matchplayDecision);
        pendingPracticeDropShot =
            !useMatchplayDecision &&
            !suppressTouchForFastBall &&
            ShouldPlayPracticeDropShot(pressure, baselineVolley);
        pendingPracticeVariation = !useMatchplayDecision && !pendingPracticeDropShot && IsPracticeModeActive && Random.value <= Mathf.Clamp01(practiceVariationChance);
        Vector3 intendedTarget = useMatchplayDecision
            ? matchplayDecision.intendedTarget
            : pendingPracticeDropShot ? PickPracticeDropTarget() : PickReturnTarget();

        float risk = useMatchplayDecision ? matchplayDecision.risk : lastTargetPick.valid ? lastTargetPick.risk : 5f;
        if (!useMatchplayDecision && baselineVolley)
            risk = Mathf.Clamp(risk + baselineVolleyRiskBonus * Mathf.Lerp(0.65f, 1f, volleyDifficulty), 1f, 10f);
        if (pendingPracticeDropShot)
            risk = Mathf.Min(risk, safeMaxRisk);

        if (useMatchplayDecision)
        {
            pendingShotType = matchplayDecision.shotType;
            pendingHeightIntent = matchplayDecision.heightIntent;
            pendingUsesCustomHeightIntent = matchplayDecision.usesCustomHeightIntent;
        }
        else
        {
            pendingShotType = PickShotType(pressure, risk, baselineVolley, pendingPracticeVariation, pendingPracticeDropShot);
            pendingHeightIntent = PickHeightIntent(pendingShotType, pressure, risk, baselineVolley, pendingPracticeDropShot, out pendingUsesCustomHeightIntent);
        }

        if (suppressTouchForFastBall && pendingShotType != BaseShotType.Lob)
        {
            pendingPracticeDropShot = false;
            pendingHeightIntent = BaseShotLibrary.DefaultHeightIntent;
            pendingUsesCustomHeightIntent = false;
        }

        if (postBounceReturnProfile == PostBounceReturnProfile.LowDefensive)
        {
            pendingPracticeDropShot = false;
            pendingPracticeVariation = false;
            pendingShotType = PickLowBounceDefensiveShotType();
            pendingHeightIntent = BaseShotLibrary.DefaultHeightIntent;
            pendingUsesCustomHeightIntent = false;
        }

        bool wtaHighBallShape = wta != null && wta.ShouldUseHighBallTopspin(
            currentPlan.ownSideBounceCount,
            currentPlan.contactPoint.y,
            baselineVolley);
        if (wtaHighBallShape)
        {
            pendingPracticeDropShot = false;
            pendingShotType = BaseShotType.Topspin;
            pendingHeightIntent = BaseShotLibrary.DefaultHeightIntent;
            pendingUsesCustomHeightIntent = false;
        }

        Vector3 target = pendingPracticeDropShot
            ? intendedTarget
            : ApplyLandingDispersion(intendedTarget, pendingShotType, accuracyPressure, risk);
        target = EnforceFastReturnDepth(target, fastServeReturn01);
        if (useMatchplayDecision && pendingShotType != BaseShotType.Drop)
            target = EnforceMatchplayRallySafety(target, matchplayDecision);
        if (postBounceReturnProfile == PostBounceReturnProfile.LowDefensive)
            target = EnforceLowBounceDefensiveTarget(target);
        SetDesiredReturnTarget(target, true);

        SwipeSkill skill = PickSwipeSkill(accuracyPressure, risk);
        float lateralError = RandomInRange(GetLateralErrorRange(skill, accuracyPressure, risk));
        lateralError *= Mathf.Lerp(1f, Mathf.Clamp01(fastReturnLateralErrorScale), fastServeReturn01);
        if (postBounceReturnProfile == PostBounceReturnProfile.LowDefensive)
            lateralError *= Mathf.Clamp01(lowBounceLateralErrorScale);
        float desiredReturnSpeed = useMatchplayDecision
            ? RandomInRange(matchplayDecision.speedRange)
            : pendingPracticeDropShot
                ? RandomInRange(practiceDropSpeedRange)
                : RandomInRange(baselineVolley ? GetBaselineVolleySpeedRange(skill, volleyDifficulty) : GetSpeedRange(skill, pressure, risk));
        if (useMatchplayDecision && baselineVolley && pendingShotType != BaseShotType.Drop)
            desiredReturnSpeed = Mathf.Max(desiredReturnSpeed, Mathf.Max(0f, matchplayBaselineVolleyMinimumDesiredSpeed));
        desiredReturnSpeed = Mathf.Clamp(desiredReturnSpeed, 0f, BaseShotLibrary.RallyMaxSpeedMps);
        float quality = RandomInRange(GetQualityRange(skill));
        if (fastServeReturn01 > 0f)
            quality = Mathf.Max(
                quality,
                Mathf.Lerp(
                    Mathf.Clamp01(fastServeMinimumQuality),
                    Mathf.Max(Mathf.Clamp01(fastServeMinimumQuality), Mathf.Clamp01(fastReturnFullSpeedMinimumQuality)),
                    fastServeReturn01));
        if (wtaCompactFirstServeReturn)
            quality = Mathf.Max(quality, wta.compactReturnQualityFloor);
        if (useMatchplayDecision)
            quality = Mathf.Lerp(quality, Mathf.Max(quality, matchplayDecision.minimumQuality), Mathf.Clamp01(matchplayDecision.qualityBias));
        if (pendingPracticeDropShot)
            quality = Mathf.Max(quality, 0.82f);
        if (postBounceReturnProfile == PostBounceReturnProfile.LowDefensive)
            quality = Mathf.Max(quality, Mathf.Clamp01(lowBounceMinimumQuality));
        float normDist = Mathf.Lerp(0.08f, 0.58f, 1f - quality);
        if (baselineVolley)
        {
            quality = Mathf.Clamp01(quality - Mathf.Max(0f, baselineVolleyQualityPenalty) * volleyDifficulty);
            normDist = Mathf.Clamp01(normDist + Mathf.Max(0f, baselineVolleyNormDistPenalty) * volleyDifficulty);
            float lateralSign = Mathf.Abs(lateralError) > 0.01f ? Mathf.Sign(lateralError) : (Random.value < 0.5f ? -1f : 1f);
            float multiplier = Mathf.Lerp(1f, Mathf.Max(1f, baselineVolleyLateralErrorMultiplier), volleyDifficulty);
            lateralError *= multiplier;
            lateralError += lateralSign * volleyDifficulty * 1.25f;
        }

        Vector3 toTarget = target - ball.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.0001f)
            toTarget = GetFacingDirection();
        toTarget.Normalize();

        Vector3 aimDir = Quaternion.AngleAxis(lateralError, Vector3.up) * toTarget;
        UpdateAIBackswingChargeForShot(desiredReturnSpeed, risk, pressure, useMatchplayDecision, baselineVolley);
        float aiBackswingScale = ComputeCurrentAIBackswingScale();
        float aiBackswingCapSpeed = ComputeAIBackswingCapSpeed(aiBackswingScale);
        float speed = GetPaceCompensatedSwipeSpeed(desiredReturnSpeed, pendingShotType, quality, aimDir, currentPlan, out float expectedPaceBonus, out float incomingSpinSafety);
        if (baselineVolley)
        {
            float signedSpeedError = Random.value < 0.5f ? -1f : 1f;
            speed *= Mathf.Max(0.2f, 1f + signedSpeedError * Mathf.Max(0f, baselineVolleySpeedControlErrorFraction) * volleyDifficulty);
        }
        speed = Mathf.Clamp(speed, 0f, BaseShotLibrary.RallyMaxSpeedMps);
        float forwardSwingProgress = GetAIVirtualForwardSwingProgress(
            useMatchplayDecision,
            pendingPracticeDropShot,
            baselineVolley,
            risk,
            pressure,
            desiredReturnSpeed,
            quality);
        if (fastServeReturn01 > 0f)
            forwardSwingProgress = Mathf.Max(
                forwardSwingProgress,
                Mathf.Lerp(matchplayDefensiveForwardSwingProgress, Mathf.Clamp01(fastServeForwardSwingProgress), fastServeReturn01));
        if (wtaCompactFirstServeReturn)
        {
            // A compact block uses retained pace rather than asking for a
            // late full forward swing against a 110+ mph first serve.
            forwardSwingProgress = Mathf.Min(forwardSwingProgress, wta.compactReturnForwardSwingProgress);
        }
        float adjustedSpinIntent = pendingPracticeDropShot
            ? Mathf.Clamp01(practiceDropSpinIntent)
            : GetLiveIncomingAdjustedSpinIntent(pendingShotType, currentPlan, baselineVolley);
        if (postBounceReturnProfile == PostBounceReturnProfile.LowDefensive && pendingShotType == BaseShotType.Topspin)
            adjustedSpinIntent = Mathf.Max(adjustedSpinIntent, Mathf.Clamp01(lowBounceTopspinMinimumSpinIntent));
        if (wtaHighBallShape)
            adjustedSpinIntent = Mathf.Max(adjustedSpinIntent, wta.highBallTopspinIntentFloor);
        float virtualControlHold = postBounceReturnProfile == PostBounceReturnProfile.LowDefensive
            ? Mathf.Max(Mathf.Clamp01(holdScale), Mathf.Clamp01(lowBounceDefensiveVirtualHold))
            : Mathf.Clamp01(holdScale);
        if (useMatchplayDecision && matchplayDecision.recoveryBall)
            virtualControlHold = Mathf.Max(virtualControlHold, Mathf.Clamp01(matchplayRecoveryVirtualControlHold));
        pendingSwipe = new SwipeData(
            isLMB: !pendingPracticeDropShot,
            isRMB: pendingPracticeDropShot,
            aimDir: aimDir,
            speed: speed,
            quality: quality,
            normDist: normDist,
            holdScale: virtualControlHold,
            spinIntent: adjustedSpinIntent,
            backswingScale: aiBackswingScale,
            forwardSwingProgress: forwardSwingProgress,
            backswingCapSpeed: aiBackswingCapSpeed);
        pendingLiveIncomingVelocity = currentPlan.incomingVelocity;
        pendingLiveIncomingSpin = currentPlan.incomingSpin;
        pendingPlannedContactPoint = currentPlan.contactPoint;
        pendingPlannedContactWorldTime = Time.time + Mathf.Max(0f, currentPlan.timeUntilContact);
        pendingDesiredReturnSpeed = desiredReturnSpeed;
        pendingPressure = pressure;
        pendingRisk = risk;
        pendingBaselineVolley = baselineVolley;
        pendingRallyState = useMatchplayDecision ? matchplayDecision.rallyState : MatchplayRallyState.Neutral;
        pendingMatchplayTactic = useMatchplayDecision ? matchplayDecision.tactic : MatchplayTactic.DeepMiddle;
        pendingUsedMatchplayDecision = useMatchplayDecision;
        pendingIntendedTarget = intendedTarget;
        pendingFinalTarget = target;
        pendingIncomingReferenceSpeed = incomingReferenceSpeed;
        pendingContactIncomingSpeed = currentPlan.incomingVelocity.magnitude;
        pendingExpectedPaceBonus = expectedPaceBonus;
        pendingIncomingSpinSafety = incomingSpinSafety;
        pendingFastReturn01 = fastServeReturn01;
        pendingFastBallTouchSuppressed = suppressTouchForFastBall;
        pendingPostBounceReturnProfile = postBounceReturnProfile;
        pendingVirtualControlHold = virtualControlHold;
        pendingLowBounceSafetyClearanceBonus = postBounceReturnProfile == PostBounceReturnProfile.LowDefensive
            ? Mathf.Max(0f, lowBounceSafetyClearanceBonus)
            : 0f;
        pendingRecoverySafetyClearanceBonus = useMatchplayDecision
            ? Mathf.Max(0f, matchplayDecision.safetyClearanceBonus)
            : 0f;
        pendingRecoveryIntendedClearanceFloor = useMatchplayDecision
            ? Mathf.Max(0f, matchplayDecision.recoveryIntendedClearanceFloor)
            : 0f;
        pendingContactApproachMode = currentPlan.valid ? currentPlan.approachMode : ContactApproachMode.Planted;
        pendingSignificantRunningContact = currentPlan.valid && currentPlan.significantRunningContact;
        pendingRecoveryBall = useMatchplayDecision && matchplayDecision.recoveryBall;
        pendingOpponentReturnSeconds = useMatchplayDecision ? matchplayDecision.opponentReturnSeconds : 0f;
        pendingAIRecoverySeconds = useMatchplayDecision ? matchplayDecision.recoverySeconds : 0f;
        pendingRecoveryMarginSeconds = useMatchplayDecision ? matchplayDecision.recoveryMarginSeconds : 0f;
        pendingOpponentAttackPotential = useMatchplayDecision ? matchplayDecision.opponentAttackPotential : 0f;
        pendingOpponentMomentumReversal01 = useMatchplayDecision ? matchplayDecision.opponentMomentumReversal01 : 0f;
        pendingOpponentMomentumTurnSeconds = useMatchplayDecision ? matchplayDecision.opponentMomentumTurnSeconds : 0f;

        if (useMatchplayDecision)
            LogMatchplayContactPlanDiagnostic();

        if (logLowBounceReturnDiagnostics && postBounceReturnProfile != PostBounceReturnProfile.Normal)
        {
            Debug.Log($"[AI LOW BOUNCE] shot={swipeStatusShotSequence} profile={postBounceReturnProfile} " +
                $"ball={ball.position} planContact={currentPlan.contactPoint} stance={currentPlan.stancePoint} " +
                $"t={currentPlan.timeUntilContact:F2}s pursuit={currentPlan.pursuitMode} incomingPeak={incomingReferenceSpeed:F1}m/s " +
                $"target={target} hold={virtualControlHold:F2} safetyBonus={pendingLowBounceSafetyClearanceBonus:F2}m shot={pendingShotType}.");
        }

        if (debugMatchplayRecoveryLogs && useMatchplayDecision)
        {
            Debug.Log($"[AI RECOVERY PLAN] shot={swipeStatusShotSequence} approach={pendingContactApproachMode} " +
                $"minimumMove={currentPlan.minimumApproachSpeed:F2}m/s requestedMove={currentPlan.approachMoveSpeed:F2}m/s " +
                $"plantTime={currentPlan.estimatedPlantedArrivalTime:F2}s contactT={currentPlan.timeUntilContact:F2}s " +
                $"playerReturn={pendingOpponentReturnSeconds:F2}s aiRecovery={pendingAIRecoverySeconds:F2}s " +
                $"margin={pendingRecoveryMarginSeconds:F2}s attack={pendingOpponentAttackPotential:F2} " +
                $"recoveryBall={pendingRecoveryBall} clearanceFloor={pendingRecoveryIntendedClearanceFloor:F2}m " +
                $"momentum={pendingOpponentMomentumReversal01:F2}/{pendingOpponentMomentumTurnSeconds:F2}s " +
                $"target={target} shot={pendingShotType} hold={virtualControlHold:F2}.");
        }
        else if (debugRunningContactPlanning && currentPlan.valid)
        {
            Debug.Log($"[AI CONTACT APPROACH] shot={swipeStatusShotSequence} mode={currentPlan.approachMode} " +
                $"move={currentPlan.requiredMoveDistance:F2}m min={currentPlan.minimumApproachSpeed:F2}m/s requested={currentPlan.approachMoveSpeed:F2}m/s " +
                $"plant={currentPlan.estimatedPlantedArrivalTime:F2}s contact={currentPlan.timeUntilContact:F2}s pursuit={currentPlan.pursuitMode}.");
        }

        float effectiveSwipeDuration = Mathf.Lerp(
            Mathf.Max(0.01f, virtualSwipeDuration),
            Mathf.Max(0.01f, fastServeVirtualSwipeDuration),
            fastServeReturn01);
        if (wtaCompactFirstServeReturn)
            effectiveSwipeDuration = Mathf.Min(effectiveSwipeDuration, wta.compactReturnSwipeDuration);
        swipePreparedAt = Time.time;
        swipeEndTime = Time.time + effectiveSwipeDuration;
        tightDeadline = swipeEndTime + Mathf.Max(0.01f, tightHitZonePendingWindow);

        if (debugFastServeReturnLogs && fastServeReturn01 > 0f)
            Debug.Log($"[TennisAI FAST SERVE] incomingPeak={incomingReferenceSpeed:F1}m/s incomingContact={currentPlan.incomingVelocity.magnitude:F1}m/s spin={currentPlan.incomingSpin.magnitude:F1}rad/s fastAssist={fastServeReturn01:F2} contact={currentPlan.contactPoint} t={currentPlan.timeUntilContact:F2}s quality={quality:F2} forward={forwardSwingProgress:F2}");
        if (debugLogs)
            Debug.Log($"[TennisAI] Virtual swipe prepared ({reason}): shot={pendingShotType}, drop={pendingPracticeDropShot}, variation={pendingPracticeVariation}, customHeight={pendingUsesCustomHeightIntent}, heightIntent={pendingHeightIntent:F2}, skill={skill}, desiredReturn={desiredReturnSpeed:F1}, speed={speed:F1}, spinIntent={adjustedSpinIntent:F2}, quality={quality:F2}, lateralError={lateralError:F1}, risk={risk:F1}, pressure={pressure:F2}, intended={intendedTarget}, target={target}");

        if (debugPaceCompensationLogs)
            Debug.Log($"[TennisAI PACE] desiredReturn={desiredReturnSpeed:F2}, generatedSwipe={speed:F2}, expectedPaceBonus={expectedPaceBonus:F2}, spinSafety={incomingSpinSafety:F2}, liveIncomingSpeed={currentPlan.incomingVelocity.magnitude:F2}, liveIncomingSpinRad={currentPlan.incomingSpin.magnitude:F1}, spinIntent={adjustedSpinIntent:F2}, shot={pendingShotType}");

        if (debugVolleyLogs && baselineVolley)
            Debug.Log($"[TennisAI VOLLEY] Baseline volley block: difficulty={volleyDifficulty:F2}, contact={currentPlan.contactPoint}, incomingSpeed={currentPlan.incomingVelocity.magnitude:F1}, speed={speed:F1}, shot={pendingShotType}, risk={risk:F1}, pressure={pressure:F2}");

        if (debugPracticeLogs && pendingPracticeDropShot)
            Debug.Log($"[TennisAI PRACTICE] Drop shot test: contactDistFromNet={Mathf.Abs(currentPlan.contactPoint.x - netX):F2}, landingPastNet={Mathf.Abs(target.x - netX):F2}, target={target}, speed={speed:F2}, spinIntent={adjustedSpinIntent:F2}, heightIntent={pendingHeightIntent:F2}");

        ChangeState(AIState.SwipePrepared, reason);
    }

    private bool ShouldHitBeforeBodyCollision()
    {
        if (!hitEarlyWhenBodyCollisionImminent || ball == null || didHitThisBall || abandonedIncomingBall)
            return false;
        if (ShouldDeferTightContactForFastBaselineBounce())
            return false;
        if (Time.time - swipePreparedAt < Mathf.Max(0f, bodyCollisionMinimumSwipePreparation))
            return false;
        if (!IsBallInTightHitZone())
            return false;
        if (HorizontalDistance(ball.position, transform.position) > Mathf.Max(0.1f, bodyCollisionMaximumHitDistance))
            return false;

        return TryPredictPhysicalBodyCollision(out _, out _);
    }

    private bool TryPredictPhysicalBodyCollision(out float timeToCollision, out string colliderName)
    {
        timeToCollision = float.PositiveInfinity;
        colliderName = string.Empty;
        if (ball == null)
            return false;

        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        float lookAhead = Mathf.Max(0f, bodyCollisionLookAheadSeconds);
        float step = Mathf.Clamp(Time.fixedDeltaTime * 0.5f, 0.005f, 0.0125f);
        float safetyRadius = Mathf.Max(0f, bodyCollisionSafetyRadius);
        Vector3 start = ball.position;
        Vector3 velocity = ball.linearVelocity;

        for (float t = 0f; t <= lookAhead + 0.0001f; t += step)
        {
            Vector3 predicted = start + velocity * t + Physics.gravity * (0.5f * t * t);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider bodyCollider = colliders[i];
                if (bodyCollider == null || bodyCollider == hitController?.hitZone || bodyCollider.isTrigger || bodyCollider.attachedRigidbody == ball)
                    continue;

                Vector3 closest = bodyCollider.ClosestPoint(predicted);
                if (Vector3.Distance(closest, predicted) > safetyRadius)
                    continue;

                timeToCollision = t;
                colliderName = bodyCollider.name;
                if (debugBodyAvoidanceLogs)
                    Debug.Log($"[TennisAI BODY AVOID] Predicted body collision with {colliderName} in {timeToCollision:F3}s at {predicted}.");
                return true;
            }
        }

        return false;
    }

    private bool ShouldOverwriteSwipe()
    {
        if (!currentPlan.valid || finalInterceptFrozen)
            return false;

        float contactDelta = Vector3.Distance(currentPlan.contactPoint, pendingPlannedContactPoint);
        float currentContactWorldTime = Time.time + Mathf.Max(0f, currentPlan.timeUntilContact);
        float timeDelta = Mathf.Abs(currentContactWorldTime - pendingPlannedContactWorldTime);
        float speedDelta = Mathf.Abs(currentPlan.incomingVelocity.magnitude - pendingLiveIncomingVelocity.magnitude);
        float spinDelta = Mathf.Abs(currentPlan.incomingSpin.magnitude - pendingLiveIncomingSpin.magnitude);
        if (contactDelta < replanContactDistance &&
            timeDelta < replanContactTime &&
            speedDelta < Mathf.Max(0f, liveIncomingSpeedReplanDelta) &&
            spinDelta < Mathf.Max(0f, liveIncomingSpinReplanDelta))
        {
            return false;
        }
        return true;
    }

    private void HitWithPendingSwipe(HitContactConfirmation contact)
    {
        if (!serviceReturnHitAllowed)
        {
            if (logSwipeStatusTransitions && lastReturnBlockedShotKey != swipeStatusShotKey)
            {
                lastReturnBlockedShotKey = swipeStatusShotKey;
                Debug.LogWarning($"[AI RETURN BLOCKED] shot={swipeStatusShotSequence} reason=serviceReturnHitAllowedFalse " +
                    $"preparationActive={serviceReturnPreparationActive} state={state} ball={ball?.position}.");
            }
            return;
        }

        if (hitController == null || ball == null || didHitThisBall || abandonedIncomingBall)
            return;

        MatchServicePointController matchService = MatchServicePointController.Active;
        if (matchService != null && matchService.IsMatchActive &&
            (!hitController.matchHitAllowed || !matchService.IsHitAllowed(hitController)))
        {
            if (logSwipeStatusTransitions && lastReturnBlockedShotKey != swipeStatusShotKey)
            {
                lastReturnBlockedShotKey = swipeStatusShotKey;
                Debug.LogWarning($"[AI RETURN BLOCKED] shot={swipeStatusShotSequence} matchHitAllowed={hitController.matchHitAllowed} " +
                    $"phaseAllows={matchService.IsHitAllowed(hitController)} state={state} ball={ball.position}.");
            }
            return;
        }

        RecordSwipeCompletionForGizmo();

        hitController.ball = ball;
        hitController.reticle = returnTarget;
        hitController.ballIsInHittingZone = true;
        hitController.liveShotSolveMode = liveShotSolveMode;

        if (logPossibleBodyContacts && IsBallPossiblyOnBody(out string bodyContactInfo))
            Debug.LogWarning($"[TennisAI BODY CONTACT?] {name} hitting while ball is near/inside body bounds: {bodyContactInfo}");

        ApplyCurrentAIBackswingToPendingSwipe();

        if (debugLogs)
            Debug.Log($"[TennisAI] Tight zone reached. Hitting with pending virtual swipe. shot={pendingShotType}, customHeight={pendingUsesCustomHeightIntent}, heightIntent={pendingHeightIntent:F2}, backswing={pendingSwipe.backswingScale:F2}, bsCap={pendingSwipe.backswingCapSpeed:F1}");
        if (logSwipeStatusTransitions)
            Debug.Log($"[AI RETURN ATTEMPT] shot={swipeStatusShotSequence} contact={contact.contactPosition} swept={contact.swept} " +
                $"velocity={ball.linearVelocity} planContact={currentPlan.contactPoint} state={state}.");

        float previousHeightIntent = BaseShotLibrary.HeightIntent;
        if (pendingUsesCustomHeightIntent)
            BaseShotLibrary.HeightIntent = Mathf.Clamp01(pendingHeightIntent);
        float safetyClearanceBonus = Mathf.Max(pendingLowBounceSafetyClearanceBonus, pendingRecoverySafetyClearanceBonus);
        if (safetyClearanceBonus > 0f)
            hitController.SetOneShotSafetyClearanceBonus(safetyClearanceBonus);
        if (pendingRecoveryIntendedClearanceFloor > 0f)
            hitController.SetOneShotIntendedNetClearanceFloor(pendingRecoveryIntendedClearanceFloor);

        HitAttemptResult hitResult;
        try
        {
            hitResult = hitController.HitBallUsingSwipe(pendingSwipe, pendingShotType, shotModifier, contact);
        }
        finally
        {
            hitController.ClearOneShotSafetyClearanceBonus();
            if (pendingUsesCustomHeightIntent)
                BaseShotLibrary.HeightIntent = previousHeightIntent;
        }

        if (logSwipeStatusTransitions)
            Debug.Log($"[AI RETURN RESULT] shot={swipeStatusShotSequence} result={hitResult} contact={contact.contactPosition} swept={contact.swept}.");

        bool launched = hitResult == HitAttemptResult.Launched;
        bool confirmedMishit = hitResult == HitAttemptResult.SolverFailed;
        bool alreadyAccepted = hitResult == HitAttemptResult.AlreadyHitThisShot;
        if (launched || confirmedMishit || alreadyAccepted)
        {
            didHitThisBall = true;
            abandonedIncomingBall = false;
            if (launched)
                recoveryMovementStartedAt = Time.time;
            swipeStatusTrackingIncomingShot = false;
            ClearFinalInterceptFreeze(launched ? "return launched" : "contact accepted without launch");
            ResetAIBackswingCharge();
            hitOrMissUntil = Time.time + 0.2f;

            if (launched && logSwipeStatusTransitions)
            {
                BallController launchedController = ball.GetComponent<BallController>();
                int launchedSequence = launchedController != null ? launchedController.ShotSequence : -1;
                Vector3 launchPosition = ball.position;
                Vector3 launchVelocity = ball.linearVelocity;
                Vector3 launchSpin = launchedController != null ? launchedController.spinRadPerSecond : Vector3.zero;
                Debug.Log($"[AI RETURN LAUNCHED] incomingShot={swipeStatusShotSequence} liveShot={launchedSequence} " +
                    $"position={launchPosition} speed={launchVelocity.magnitude:F2}m/s velocity={launchVelocity} spin={launchSpin}.");
                StartCoroutine(LogAIReturnFirstFixedUpdate(
                    ball, swipeStatusShotSequence, launchedSequence, launchPosition, launchVelocity, launchSpin));
            }

            if (launched && logWeakReturnDiagnostics)
            {
                BallController launchedController = ball.GetComponent<BallController>();
                int launchedSequence = launchedController != null ? launchedController.ShotSequence : -1;
                float launchSpeed = ball.linearVelocity.magnitude;
                bool weak = launchSpeed < Mathf.Max(0f, weakReturnLaunchSpeedThreshold);
                float intendedDepth = Mathf.Abs(pendingIntendedTarget.x - netX);
                float finalDepth = Mathf.Abs(pendingFinalTarget.x - netX);
                string tactic = pendingUsedMatchplayDecision ? pendingMatchplayTactic.ToString() : "non-matchplay";
                string solverReject = hitController.aimingController != null
                    ? hitController.aimingController.LastFixedAngleRejectReason.ToString()
                    : "NoAimingController";

                Debug.Log(
                    $"[AI RETURN DIAGNOSTIC] incomingShot={swipeStatusShotSequence} launchedShot={launchedSequence} weak={weak} " +
                    $"shot={pendingShotType} state={pendingRallyState} tactic={tactic} baselineVolley={pendingBaselineVolley} " +
                    $"drop={pendingPracticeDropShot} fastTouchSuppressed={pendingFastBallTouchSuppressed} clearanceDrivenHeight={!pendingUsesCustomHeightIntent} " +
                    $"incomingPeak={pendingIncomingReferenceSpeed:F2}m/s incomingContact={pendingContactIncomingSpeed:F2}m/s fastAssist={pendingFastReturn01:F2} " +
                    $"pressure={pendingPressure:F2} risk={pendingRisk:F1} intendedTarget={pendingIntendedTarget} intendedDepth={intendedDepth:F2}m " +
                    $"postBounceProfile={pendingPostBounceReturnProfile} virtualHold={pendingVirtualControlHold:F2} safetyBonus={safetyClearanceBonus:F2}m " +
                    $"approach={pendingContactApproachMode} recoveryBall={pendingRecoveryBall} playerReturn={pendingOpponentReturnSeconds:F2}s aiRecovery={pendingAIRecoverySeconds:F2}s margin={pendingRecoveryMarginSeconds:F2}s playerAttack={pendingOpponentAttackPotential:F2} " +
                    $"recoveryClearanceFloor={pendingRecoveryIntendedClearanceFloor:F2}m momentum={pendingOpponentMomentumReversal01:F2}/{pendingOpponentMomentumTurnSeconds:F2}s " +
                    $"finalTarget={pendingFinalTarget} finalDepth={finalDepth:F2}m desired={pendingDesiredReturnSpeed:F2}m/s " +
                    $"swipe={pendingSwipe.speed:F2}m/s quality={pendingSwipe.quality:F2} forward={pendingSwipe.forwardSwingProgress:F2} " +
                    $"backswing={pendingSwipe.backswingScale:F2} cap={pendingSwipe.backswingCapSpeed:F2}m/s " +
                    $"paceEstimate={pendingExpectedPaceBonus:F2}m/s spinSafety={pendingIncomingSpinSafety:F2}m/s " +
                    $"racketDrive={hitController.lastRacketDriveShotSpeed:F2}m/s paceBonus={hitController.lastIncomingPaceBonus:F2}m/s " +
                    $"manualContact={hitController.lastManualAfterContactShotSpeed:F2}m/s manualWithPace={hitController.lastManualShotSpeed:F2}m/s " +
                    $"solverTarget={hitController.lastTargetShotSpeed:F2}m/s blended={hitController.lastBlendedShotSpeed:F2}m/s " +
                    $"maxAssist={hitController.lastMaxAssistedShotSpeed:F2}m/s targetCapped={hitController.lastTargetSpeedCapped} " +
                    $"solverUsed={hitController.lastSolverUsed} solverSource={hitController.lastSolverCacheSource} solverReject={solverReject} " +
                    $"candidates={hitController.lastSolverCandidateCount} extended={hitController.lastSolverTargetExtended} " +
                    $"extension={hitController.lastSolverTargetExtensionM:F2}m safetyLift={hitController.lastSolverSafetyLiftDeg:F1}deg " +
                    $"solverClear={hitController.lastSolverNetClearanceCm:F0}cm actualClear={hitController.lastActualNetClearanceCm:F0}cm " +
                    $"launch={launchSpeed:F2}m/s ({launchSpeed * 2.23694f:F0}mph) contact={contact.contactPosition}.");
            }

            ChangeState(AIState.HitOrMiss, launched ? "shot launched" : "confirmed contact mishit");
            return;
        }

        hitController.ballIsInHittingZone = false;
        if (hitResult == HitAttemptResult.MissingReference)
            RegisterMiss("shared hit controller missing required reference");
    }

    private System.Collections.IEnumerator LogAIReturnFirstFixedUpdate(
        Rigidbody launchedBall,
        int incomingShotSequence,
        int launchedShotSequence,
        Vector3 launchPosition,
        Vector3 launchVelocity,
        Vector3 launchSpin)
    {
        yield return new WaitForFixedUpdate();
        if (launchedBall == null)
            yield break;

        BallController controller = launchedBall.GetComponent<BallController>();
        int liveSequence = controller != null ? controller.ShotSequence : -1;
        Vector3 liveSpin = controller != null ? controller.spinRadPerSecond : Vector3.zero;
        Vector3 liveVelocity = launchedBall.linearVelocity;
        Debug.Log($"[AI RETURN FIRST FIXED] incomingShot={incomingShotSequence} launchedShot={launchedShotSequence} liveShot={liveSequence} " +
            $"launchPosition={launchPosition} position={launchedBall.position} positionDelta={Vector3.Distance(launchPosition, launchedBall.position):F3}m " +
            $"launchVelocity={launchVelocity} velocity={liveVelocity} velocityDelta={Vector3.Distance(launchVelocity, liveVelocity):F3}m/s " +
            $"launchSpeed={launchVelocity.magnitude:F2}m/s speed={liveVelocity.magnitude:F2}m/s launchSpin={launchSpin} spin={liveSpin}.");
    }

    private void RegisterMiss(string reason)
    {
        RecordSwipeMissForGizmo(reason);
        didHitThisBall = false;
        abandonedIncomingBall = true;
        hitController.ballIsInHittingZone = false;
        ClearFinalInterceptFreeze("return missed");
        hitOrMissUntil = Time.time + 0.2f;

        if (debugLogs)
            Debug.Log($"[TennisAI] Miss/cancel: {reason}");

        if (logTightHitZoneHeight && ball != null)
        {
            BallController controller = ball.GetComponent<BallController>();
            int shotSequence = controller != null ? controller.ShotSequence : 0;
            Vector3 center = transform.position;
            Quaternion rotation = transform.rotation;
            Vector3 radii = tightHitZoneRadii;
            if (hitController != null)
                hitController.TryGetAuthoritativeContactZonePose(out center, out rotation, out radii);
            Vector3 local = Quaternion.Inverse(rotation) * (ball.position - center);
            string plan = currentPlan.valid
                ? $"plannedContact={currentPlan.contactPoint} stance={currentPlan.stancePoint} timeToContact={currentPlan.timeUntilContact:F2}s"
                : "plannedContact=none";
            Debug.LogWarning($"[TIGHT HEIGHT AI MISS] shot={shotSequence} reason=\"{reason}\" ballHeight={ball.position.y:F2}m " +
                $"ball={ball.position} local={local} zoneCenter={center} radii={radii} velocity={ball.linearVelocity} ai={transform.position} {plan}.");
        }

        ChangeState(AIState.HitOrMiss, reason);
    }

    private bool IsBallPossiblyOnBody(out string info)
    {
        info = "";
        if (ball == null)
            return false;

        Vector3 ballPos = ball.position;
        float radius = Mathf.Max(0f, bodyContactLogRadius);
        float bestDistance = float.PositiveInfinity;
        string bestName = "";
        bool inside = false;

        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider c = colliders[i];
            if (c == null || c == hitController?.hitZone || c.attachedRigidbody == ball || c.isTrigger)
                continue;

            Bounds expanded = c.bounds;
            expanded.Expand(radius * 2f);
            if (!expanded.Contains(ballPos))
                continue;

            Vector3 closest = c.ClosestPoint(ballPos);
            float distance = Vector3.Distance(closest, ballPos);
            if (c.bounds.Contains(ballPos))
                inside = true;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestName = c.name;
            }
        }

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null)
                continue;

            Bounds expanded = r.bounds;
            expanded.Expand(radius * 2f);
            if (!expanded.Contains(ballPos))
                continue;

            Vector3 closest = r.bounds.ClosestPoint(ballPos);
            float distance = Vector3.Distance(closest, ballPos);
            if (r.bounds.Contains(ballPos))
                inside = true;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestName = r.name;
            }
        }

        if (string.IsNullOrEmpty(bestName))
            return false;

        info = $"nearest={bestName}, distance={bestDistance:F3}m, insideBounds={inside}, ball={ballPos}, ai={transform.position}";
        return inside || bestDistance <= radius;
    }

    private bool TryBuildMatchplayDecision(float pressure, bool baselineVolley, float volleyDifficulty, out MatchplayDecision decision)
    {
        decision = default;
        if (!IsMatchplayMode)
            return false;

        if (!TryGetRawReturnTargetBounds(out Vector2 xRange, out Vector2 zRange))
        {
            xRange = returnTargetXRange;
            zRange = returnTargetZRange;
        }

        float minX = Mathf.Min(xRange.x, xRange.y) + Mathf.Max(0f, targetBoundsPaddingX);
        float maxX = Mathf.Max(xRange.x, xRange.y) - Mathf.Max(0f, targetBoundsPaddingX);
        float minZ = Mathf.Min(zRange.x, zRange.y) + Mathf.Max(0f, targetBoundsPaddingZ);
        float maxZ = Mathf.Max(zRange.x, zRange.y) - Mathf.Max(0f, targetBoundsPaddingZ);
        if (minX > maxX)
        {
            float mid = (minX + maxX) * 0.5f;
            minX = mid;
            maxX = mid;
        }
        if (minZ > maxZ)
        {
            float mid = (minZ + maxZ) * 0.5f;
            minZ = mid;
            maxZ = mid;
        }

        ConstrainTargetXRangeAwayFromNet(ref minX, ref maxX);
        MatchplayRallyState rallyState = PickMatchplayRallyState(pressure, baselineVolley, volleyDifficulty);
        float targetSideSign = GetTargetSideSign(minX, maxX);
        Vector3 opponent = GetMatchplayOpponentPosition(targetSideSign, minZ, maxZ);

        MatchplayTactic tactic = PickMatchplayTactic(rallyState, pressure, opponent, minX, maxX, minZ, maxZ);
        selectedMatchplayTactic = tactic;
        MatchplayDecision best = default;
        float bestScore = float.NegativeInfinity;
        AddMatchplayTacticCandidates(ref best, ref bestScore, tactic, rallyState, minX, maxX, minZ, maxZ, opponent);

        // A tactic is a preference, not a blind instruction. Keep a safe deep
        // alternative in the candidate set so a wide/short idea loses when it
        // would give a comfortably positioned opponent an easy attack.
        if (tactic != MatchplayTactic.DefensiveReset && tactic != MatchplayTactic.DeepMiddle)
            AddMatchplayTacticCandidates(ref best, ref bestScore, MatchplayTactic.DeepMiddle, rallyState, minX, maxX, minZ, maxZ, opponent);
        if (rallyState != MatchplayRallyState.Defensive && tactic != MatchplayTactic.CrosscourtProbe)
            AddMatchplayTacticCandidates(ref best, ref bestScore, MatchplayTactic.CrosscourtProbe, rallyState, minX, maxX, minZ, maxZ, opponent);
        if (rallyState != MatchplayRallyState.Defensive &&
            tactic != MatchplayTactic.MomentumReversal &&
            HasMatchplayOpponentMomentum(targetSideSign))
        {
            AddMatchplayTacticCandidates(ref best, ref bestScore, MatchplayTactic.MomentumReversal, rallyState, minX, maxX, minZ, maxZ, opponent);
        }

        if (!best.valid)
        {
            AddMatchplayCandidate(ref best, ref bestScore, rallyState, MatchplayTactic.DeepMiddle, minX, maxX, minZ, maxZ, opponent, 0.82f, 0.50f, 2.7f, false);
            AddMatchplayCandidate(ref best, ref bestScore, rallyState, MatchplayTactic.CrosscourtProbe, minX, maxX, minZ, maxZ, opponent, 0.80f, GetModerateProbeAwayZ01(opponent.z, minZ, maxZ), 4.6f, false);
        }

        if (!best.valid)
            return false;

        decision = best;
        int grid = Mathf.Clamp(riskGridSize, 3, 9);
        lastTargetPick = new TargetPick
        {
            valid = true,
            position = best.intendedTarget,
            risk = best.risk,
            depth01 = GetTargetDepth01(best.intendedTarget.x, minX, maxX),
            gridX = Mathf.Clamp(Mathf.FloorToInt(Mathf.InverseLerp(minX, maxX, best.intendedTarget.x) * grid), 0, grid - 1),
            gridZ = Mathf.Clamp(Mathf.FloorToInt(Mathf.InverseLerp(minZ, maxZ, best.intendedTarget.z) * grid), 0, grid - 1),
            xRange = new Vector2(minX, maxX),
            zRange = new Vector2(minZ, maxZ)
        };

        hasLastMatchplayTarget = true;
        lastMatchplayTarget = decision.intendedTarget;

        if (debugMatchplayLogs)
            Debug.Log($"[TennisAI MATCH] state={decision.rallyState}, tactic={decision.tactic}, target={decision.intendedTarget}, risk={decision.risk:F1}, shot={decision.shotType}, height={decision.heightIntent:F2}, speed={decision.speedRange.x:F1}-{decision.speedRange.y:F1}, oppReach={decision.opponentReachSeconds:F2}s, ballTravel={decision.ballTravelSeconds:F2}s, advantage={decision.advantageSeconds:F2}s, playerReturn={decision.opponentReturnSeconds:F2}s, aiRecovery={decision.recoverySeconds:F2}s, margin={decision.recoveryMarginSeconds:F2}s, attack={decision.opponentAttackPotential:F2}, approach={(decision.runningContact ? "running" : "planted")}, recoveryBall={decision.recoveryBall}, recoveryClearance={decision.recoveryIntendedClearanceFloor:F2}m, momentum={decision.opponentMomentumReversal01:F2}/{decision.opponentMomentumTurnSeconds:F2}s");

        return true;
    }

    private MatchplayTactic PickMatchplayTactic(MatchplayRallyState state, float pressure, Vector3 opponent, float minX, float maxX, float minZ, float maxZ)
    {
        if (state == MatchplayRallyState.Defensive)
            return Random.value < 0.68f ? MatchplayTactic.DefensiveReset : MatchplayTactic.DeepMiddle;

        bool opponentHasMomentum = HasMatchplayOpponentMomentum(GetTargetSideSign(minX, maxX));
        float opponentLateralEdge = GetLateralEdge01(opponent.z, minZ, maxZ);
        float contactDepth = currentPlan.valid ? Mathf.Abs(currentPlan.contactPoint.x - netX) : Mathf.Abs(transform.position.x - netX);
        bool shortBall = contactDepth < Mathf.Max(0f, matchplayShortBallWinnerDepthFromNet);
        bool opponentWide = opponentLateralEdge > 0.62f;
        bool winnerOpportunity = opponentWide || shortBall;

        if (state == MatchplayRallyState.Offensive)
        {
            float roll = Random.value;
            if (opponentHasMomentum && roll < Mathf.Clamp01(matchplayMomentumReversalChance + 0.12f))
                return MatchplayTactic.MomentumReversal;
            if (winnerOpportunity && roll < GetEffectiveMatchplayWinnerOpportunityChance())
                return MatchplayTactic.WinnerAttempt;
            if (shortBall && roll < Mathf.Clamp01(matchplayApproachPressureChance + 0.35f))
                return MatchplayTactic.ApproachPressure;
            if (roll < Mathf.Clamp01(matchplayWideAngleChance + 0.24f))
                return MatchplayTactic.WideAngle;
            return Random.value < 0.55f ? MatchplayTactic.ChangeDirection : MatchplayTactic.CrosscourtProbe;
        }

        float neutralRoll = Random.value;
        if (opponentHasMomentum && neutralRoll < Mathf.Clamp01(matchplayMomentumReversalChance))
            return MatchplayTactic.MomentumReversal;
        float deep = Mathf.Clamp01(matchplayDeepMiddleChance);
        float body = deep + Mathf.Clamp01(matchplayBodyJammerChance);
        float repeat = body + Mathf.Clamp01(matchplaySameSideRepeatChance);
        float change = repeat + Mathf.Clamp01(matchplayChangeDirectionChance);
        float angle = change + Mathf.Clamp01(matchplayWideAngleChance);

        if (neutralRoll < deep)
            return MatchplayTactic.DeepMiddle;
        if (neutralRoll < body)
            return MatchplayTactic.BodyJammer;
        if (neutralRoll < repeat)
            return MatchplayTactic.SameSideRepeat;
        if (neutralRoll < change)
            return MatchplayTactic.ChangeDirection;
        if (opponentWide || neutralRoll < angle)
            return MatchplayTactic.CrosscourtProbe;
        return MatchplayTactic.DeepMiddle;
    }

    private void AddMatchplayTacticCandidates(
        ref MatchplayDecision best,
        ref float bestScore,
        MatchplayTactic tactic,
        MatchplayRallyState rallyState,
        float minX,
        float maxX,
        float minZ,
        float maxZ,
        Vector3 opponent)
    {
        float opponentZ01 = Mathf.InverseLerp(minZ, maxZ, Mathf.Clamp(opponent.z, minZ, maxZ));
        float awayZ01 = GetAwayFromOpponentZ01(opponent.z, minZ, maxZ, 0.88f);
        float probeZ01 = GetModerateProbeAwayZ01(opponent.z, minZ, maxZ);
        float bodyZ01 = Mathf.Clamp01(opponentZ01 + Random.Range(-0.08f, 0.08f));
        float repeatZ01 = hasLastMatchplayTarget ? Mathf.InverseLerp(minZ, maxZ, Mathf.Clamp(lastMatchplayTarget.z, minZ, maxZ)) : bodyZ01;
        float changeZ01 = hasLastMatchplayTarget ? 1f - repeatZ01 : awayZ01;
        float momentumReversalZ01 = GetMomentumReversalZ01(opponent.z, minZ, maxZ, GetMatchplayOpponentPlanarVelocity(GetTargetSideSign(minX, maxX)));

        switch (tactic)
        {
            case MatchplayTactic.DefensiveReset:
                AddMatchplayCandidate(ref best, ref bestScore, rallyState, tactic, minX, maxX, minZ, maxZ, opponent, 0.88f, 0.50f, 2.2f, false);
                AddMatchplayCandidate(ref best, ref bestScore, rallyState, tactic, minX, maxX, minZ, maxZ, opponent, 0.78f, 0.36f, 3.2f, false);
                AddMatchplayCandidate(ref best, ref bestScore, rallyState, tactic, minX, maxX, minZ, maxZ, opponent, 0.78f, 0.64f, 3.2f, false);
                break;
            case MatchplayTactic.DeepMiddle:
                AddMatchplayCandidate(ref best, ref bestScore, rallyState, tactic, minX, maxX, minZ, maxZ, opponent, 0.86f, 0.50f, 2.7f, false);
                AddMatchplayCandidate(ref best, ref bestScore, rallyState, tactic, minX, maxX, minZ, maxZ, opponent, 0.78f, 0.44f, 3.3f, false);
                AddMatchplayCandidate(ref best, ref bestScore, rallyState, tactic, minX, maxX, minZ, maxZ, opponent, 0.78f, 0.56f, 3.3f, false);
                break;
            case MatchplayTactic.BodyJammer:
                AddMatchplayCandidate(ref best, ref bestScore, rallyState, tactic, minX, maxX, minZ, maxZ, opponent, 0.76f, bodyZ01, 3.9f, false);
                AddMatchplayCandidate(ref best, ref bestScore, rallyState, tactic, minX, maxX, minZ, maxZ, opponent, 0.84f, bodyZ01, 4.4f, false);
                break;
            case MatchplayTactic.SameSideRepeat:
                AddMatchplayCandidate(ref best, ref bestScore, rallyState, tactic, minX, maxX, minZ, maxZ, opponent, 0.78f, repeatZ01, 4.6f, false);
                AddMatchplayCandidate(ref best, ref bestScore, rallyState, tactic, minX, maxX, minZ, maxZ, opponent, 0.86f, repeatZ01, 5.0f, false);
                break;
            case MatchplayTactic.ChangeDirection:
                AddMatchplayCandidate(ref best, ref bestScore, rallyState, tactic, minX, maxX, minZ, maxZ, opponent, 0.80f, changeZ01, 5.9f, false);
                AddMatchplayCandidate(ref best, ref bestScore, rallyState, tactic, minX, maxX, minZ, maxZ, opponent, 0.72f, changeZ01, 6.4f, false);
                break;
            case MatchplayTactic.CrosscourtProbe:
                AddMatchplayCandidate(ref best, ref bestScore, rallyState, tactic, minX, maxX, minZ, maxZ, opponent, 0.84f, probeZ01, 4.4f, false);
                AddMatchplayCandidate(ref best, ref bestScore, rallyState, tactic, minX, maxX, minZ, maxZ, opponent, 0.78f, Mathf.Lerp(0.50f, probeZ01, 0.78f), 4.9f, false);
                break;
            case MatchplayTactic.WideAngle:
                AddMatchplayCandidate(ref best, ref bestScore, rallyState, tactic, minX, maxX, minZ, maxZ, opponent, 0.62f, awayZ01, 7.2f, false);
                AddMatchplayCandidate(ref best, ref bestScore, rallyState, tactic, minX, maxX, minZ, maxZ, opponent, 0.72f, awayZ01, 7.7f, false);
                break;
            case MatchplayTactic.ApproachPressure:
                AddMatchplayCandidate(ref best, ref bestScore, rallyState, tactic, minX, maxX, minZ, maxZ, opponent, 0.86f, awayZ01, 6.8f, false);
                AddMatchplayCandidate(ref best, ref bestScore, rallyState, tactic, minX, maxX, minZ, maxZ, opponent, 0.82f, bodyZ01, 6.0f, false);
                break;
            case MatchplayTactic.WinnerAttempt:
                AddMatchplayCandidate(ref best, ref bestScore, rallyState, tactic, minX, maxX, minZ, maxZ, opponent, 0.91f, awayZ01, 8.8f, true);
                AddMatchplayCandidate(ref best, ref bestScore, rallyState, tactic, minX, maxX, minZ, maxZ, opponent, 0.74f, awayZ01, 8.3f, true);
                break;
            case MatchplayTactic.MomentumReversal:
                AddMatchplayCandidate(ref best, ref bestScore, rallyState, tactic, minX, maxX, minZ, maxZ, opponent, 0.86f, momentumReversalZ01, 4.5f, false);
                AddMatchplayCandidate(ref best, ref bestScore, rallyState, tactic, minX, maxX, minZ, maxZ, opponent, 0.76f, momentumReversalZ01, 5.1f, false);
                break;
            default:
                AddMatchplayCandidate(ref best, ref bestScore, rallyState, tactic, minX, maxX, minZ, maxZ, opponent, 0.82f, 0.50f, 3.0f, false);
                break;
        }
    }
    private MatchplayRallyState PickMatchplayRallyState(float pressure, bool baselineVolley, float volleyDifficulty)
    {
        if (IsRunningContactPlan())
            return MatchplayRallyState.Defensive;

        if (baselineVolley || pressure >= Mathf.Clamp01(matchplayDefensivePressure))
            return MatchplayRallyState.Defensive;

        float contactDepth = currentPlan.valid ? Mathf.Abs(currentPlan.contactPoint.x - netX) : Mathf.Abs(transform.position.x - netX);
        bool shortBall = contactDepth < Mathf.Max(0f, matchplayShortBallWinnerDepthFromNet);
        float offensiveChance = Mathf.Max(
            Mathf.Clamp01(matchplayOffensiveChance),
            Mathf.Clamp01(matchplayMinimumOffensiveOpportunityChance));
        if (pressure <= Mathf.Clamp01(matchplayOffensivePressure) && (shortBall || Random.value <= offensiveChance))
            return MatchplayRallyState.Offensive;

        return MatchplayRallyState.Neutral;
    }

    private float GetEffectiveMatchplayWinnerOpportunityChance()
    {
        return Mathf.Max(
            Mathf.Clamp01(matchplayWinnerChance),
            Mathf.Clamp01(matchplayMinimumWinnerOpportunityChance));
    }

    private bool HasMatchplayWinnerOpportunity(Vector3 opponent, float minZ, float maxZ)
    {
        float contactDepth = currentPlan.valid ? Mathf.Abs(currentPlan.contactPoint.x - netX) : Mathf.Abs(transform.position.x - netX);
        bool shortBall = contactDepth < Mathf.Max(0f, matchplayShortBallWinnerDepthFromNet);
        bool opponentWide = GetLateralEdge01(opponent.z, minZ, maxZ) > 0.62f;
        return shortBall || opponentWide;
    }

    private void AddMatchplayCandidate(
        ref MatchplayDecision best,
        ref float bestScore,
        MatchplayRallyState rallyState,
        MatchplayTactic tactic,
        float minX,
        float maxX,
        float minZ,
        float maxZ,
        Vector3 opponent,
        float depth01,
        float z01,
        float baseRisk,
        bool winnerCandidate)
    {
        if (rallyState == MatchplayRallyState.Defensive && baseRisk > safeMaxRisk + 0.4f)
            return;
        bool selectedWinnerAttempt = tactic == selectedMatchplayTactic;
        float winnerOpportunityChance = GetEffectiveMatchplayWinnerOpportunityChance();
        if (rallyState == MatchplayRallyState.Neutral && winnerCandidate && !selectedWinnerAttempt && Random.value > winnerOpportunityChance * 0.35f)
            return;
        if (rallyState == MatchplayRallyState.Offensive && winnerCandidate && !selectedWinnerAttempt && Random.value > winnerOpportunityChance)
            return;

        float x = GetXAtTargetDepth01(minX, maxX, Mathf.Clamp01(depth01));
        float z = Mathf.Lerp(minZ, maxZ, Mathf.Clamp01(z01));
        Vector3 target = new Vector3(x, returnTargetY, z);
        float risk = Mathf.Clamp(baseRisk + EstimateTargetRisk(target, new Vector2(minX, maxX), new Vector2(minZ, maxZ), 2, 2) * 0.18f, 1f, 10f);
        bool runningContact = IsRunningContactPlan();
        Vector2 speedRange = GetMatchplaySpeedRange(rallyState, winnerCandidate, false);
        EvaluateMatchplayResponse(
            target,
            speedRange,
            opponent,
            minX,
            maxX,
            minZ,
            maxZ,
            out float ballTravel,
            out float opponentReach,
            out float opponentReturn,
            out float recoverySeconds,
            out float recoveryMargin,
            out float opponentAttackPotential,
            out float opponentMomentumReversal01,
            out float opponentMomentumTurnSeconds);

        // A normal/offensive shot remains available when the AI has recovery
        // time. If the projected margin is actually negative, though, it must
        // buy time even from a nominally neutral contact. Defensive contacts
        // retain the small positive safety-margin preference.
        bool needsRecoveryTime = recoveryMargin < 0f;
        bool defensiveMarginAtRisk = rallyState == MatchplayRallyState.Defensive &&
            recoveryMargin < Mathf.Max(0f, matchplayDefensiveRecoveryMarginTarget);
        bool recoveryBall = !winnerCandidate && (runningContact || needsRecoveryTime || defensiveMarginAtRisk);
        if (recoveryBall)
        {
            speedRange = GetMatchplaySpeedRange(rallyState, winnerCandidate, true);
            EvaluateMatchplayResponse(
                target,
                speedRange,
                opponent,
                minX,
                maxX,
                minZ,
                maxZ,
                out ballTravel,
                out opponentReach,
                out opponentReturn,
                out recoverySeconds,
                out recoveryMargin,
                out opponentAttackPotential,
                out opponentMomentumReversal01,
                out opponentMomentumTurnSeconds);
        }

        float advantage = opponentReach - ballTravel;
        float opponentLateSeconds = Mathf.Max(0f, Mathf.Max(0f, matchplayOpponentReactionSeconds) + opponentReach - ballTravel);
        float depth = GetTargetDepth01(target.x, minX, maxX);
        float lateralEdge = GetLateralEdge01(target.z, minZ, maxZ);
        float playerComfort01 = Mathf.Clamp01((ballTravel - (Mathf.Max(0f, matchplayOpponentReactionSeconds) + opponentReach) + 0.18f) / 0.85f);
        float wideEasyReach = lateralEdge * playerComfort01;
        float safetyValue = (10f - risk) * 0.45f;
        float recoveryValue = Mathf.Clamp(recoveryMargin, -1.5f, 1.5f);
        float momentumValue = opponentMomentumReversal01 * Mathf.Max(0f, matchplayMomentumReversalWeight);
        float executionPenalty = runningContact
            ? 0.78f
            : currentPlan.pursuitMode == PursuitMode.Stretch
                ? 0.28f
                : currentPlan.pursuitMode == PursuitMode.Emergency ? 0.65f : 0f;
        float selectedTacticBonus = tactic == selectedMatchplayTactic
            ? Mathf.Max(0f, matchplaySelectedTacticScoreBonus)
            : 0f;
        float probeValue = tactic == MatchplayTactic.CrosscourtProbe
            ? GetModerateProbeValue(target, opponent, minZ, maxZ)
            : 0f;
        float winnerOpportunityBonus = winnerCandidate && HasMatchplayWinnerOpportunity(opponent, minZ, maxZ)
            ? Mathf.Max(0f, matchplayWinnerOpportunityScoreBonus)
            : 0f;

        float score;
        switch (rallyState)
        {
            case MatchplayRallyState.Defensive:
                score = safetyValue * 1.55f + depth * 2.45f + recoveryValue * Mathf.Max(0f, matchplayRecoveryMarginWeight) +
                    opponentLateSeconds * Mathf.Max(0f, matchplayOpponentPressureWeight) * 0.45f -
                    opponentAttackPotential * Mathf.Max(0f, matchplayOpponentAttackPenalty) -
                    wideEasyReach * Mathf.Max(0f, matchplayWideEasyReachPenalty) - executionPenalty + Random.Range(-0.3f, 0.3f);
                break;
            case MatchplayRallyState.Offensive:
                score = safetyValue * 0.55f + advantage * 2.2f + opponentLateSeconds * Mathf.Max(0f, matchplayOpponentPressureWeight) * 1.55f +
                    depth * 0.75f + recoveryValue * Mathf.Max(0f, matchplayRecoveryMarginWeight) * 0.65f -
                    opponentAttackPotential * Mathf.Max(0f, matchplayOpponentAttackPenalty) * 0.55f + momentumValue * 1.15f - executionPenalty + Random.Range(-0.45f, 0.45f);
                break;
            default:
                score = safetyValue + advantage * 1.35f + depth * 1.75f + recoveryValue * Mathf.Max(0f, matchplayRecoveryMarginWeight) * 0.85f +
                    opponentLateSeconds * Mathf.Max(0f, matchplayOpponentPressureWeight) -
                    opponentAttackPotential * Mathf.Max(0f, matchplayOpponentAttackPenalty) -
                    wideEasyReach * Mathf.Max(0f, matchplayWideEasyReachPenalty) * 0.75f + momentumValue - executionPenalty + Random.Range(-0.35f, 0.35f);
                break;
        }

        score += selectedTacticBonus + probeValue * Mathf.Max(0f, matchplayProbeValueWeight) + winnerOpportunityBonus;

        if (score <= bestScore)
            return;

        BaseShotType shotType = PickMatchplayShotType(rallyState, risk, winnerCandidate, recoveryBall);
        bool useClearanceDrivenHeight = matchplayUseClearanceDrivenHeight && shotType != BaseShotType.Lob;
        bestScore = score;
        best = new MatchplayDecision
        {
            valid = true,
            rallyState = rallyState,
            tactic = tactic,
            intendedTarget = target,
            risk = risk,
            shotType = shotType,
            heightIntent = useClearanceDrivenHeight
                ? BaseShotLibrary.DefaultHeightIntent
                : PickMatchplayHeightIntent(rallyState, shotType, risk),
            usesCustomHeightIntent = !useClearanceDrivenHeight,
            speedRange = speedRange,
            minimumQuality = rallyState == MatchplayRallyState.Defensive ? 0.78f : rallyState == MatchplayRallyState.Neutral ? 0.72f : 0.66f,
            qualityBias = rallyState == MatchplayRallyState.Defensive ? 0.75f : 0.45f,
            opponentReachSeconds = opponentReach,
            ballTravelSeconds = ballTravel,
            advantageSeconds = advantage,
            opponentReturnSeconds = opponentReturn,
            recoverySeconds = recoverySeconds,
            recoveryMarginSeconds = recoveryMargin,
            opponentAttackPotential = opponentAttackPotential,
            runningContact = runningContact,
            recoveryBall = recoveryBall,
            safetyClearanceBonus = recoveryBall ? Mathf.Max(0f, matchplayRecoverySafetyClearanceBonus) : 0f,
            recoveryIntendedClearanceFloor = recoveryBall
                ? GetRecoveryIntendedClearanceFloor(recoveryMargin, runningContact)
                : 0f,
            opponentMomentumReversal01 = opponentMomentumReversal01,
            opponentMomentumTurnSeconds = opponentMomentumTurnSeconds
        };
    }

    private void EvaluateMatchplayResponse(
        Vector3 target,
        Vector2 speedRange,
        Vector3 opponent,
        float minX,
        float maxX,
        float minZ,
        float maxZ,
        out float ballTravel,
        out float opponentReach,
        out float opponentReturn,
        out float recoverySeconds,
        out float recoveryMargin,
        out float opponentAttackPotential,
        out float opponentMomentumReversal01,
        out float opponentMomentumTurnSeconds)
    {
        float estimatedSpeed = Mathf.Max(1f, (Mathf.Min(speedRange.x, speedRange.y) + Mathf.Max(speedRange.x, speedRange.y)) * 0.5f);
        float shotDistance = currentPlan.valid ? HorizontalDistance(currentPlan.contactPoint, target) : HorizontalDistance(transform.position, target);
        ballTravel = shotDistance / estimatedSpeed + 0.12f;
        opponentMomentumReversal01 = GetOpponentMomentumReversal(target, opponent, out opponentMomentumTurnSeconds);
        opponentReach = HorizontalDistance(opponent, target) / Mathf.Max(0.1f, matchplayOpponentReachSpeed) + opponentMomentumTurnSeconds;
        float opponentArrival = Mathf.Max(ballTravel, Mathf.Max(0f, matchplayOpponentReactionSeconds) + opponentReach);
        opponentReturn = opponentArrival + Mathf.Max(0f, matchplayOpponentReturnPreparationSeconds);
        recoverySeconds = EstimateAIRecoverySeconds();
        recoveryMargin = opponentReturn - recoverySeconds;

        float playerComfort01 = Mathf.Clamp01((ballTravel - (Mathf.Max(0f, matchplayOpponentReactionSeconds) + opponentReach) + 0.18f) / 0.85f);
        float targetShallow01 = 1f - GetTargetDepth01(target.x, minX, maxX);
        float wideEasyReach = GetLateralEdge01(target.z, minZ, maxZ) * playerComfort01;
        opponentAttackPotential = Mathf.Clamp01(playerComfort01 * 0.55f + targetShallow01 * 0.22f + wideEasyReach * 0.38f);
    }

    private float GetRecoveryIntendedClearanceFloor(float recoveryMarginSeconds, bool significantRunningContact)
    {
        float minimum = Mathf.Max(0f, matchplayRecoveryMinimumIntendedClearance);
        float maximum = Mathf.Max(minimum, matchplayRecoveryMaximumIntendedClearance);
        if (maximum <= 0f)
            return 0f;

        // A running contact gets the minimum recovery flight. When the AI
        // cannot realistically get back to base before the opponent's next
        // reply, progressively raise the *intended clearance* through the
        // standard atan2 clearance-angle path so the shot actually buys time.
        float deficit01 = Mathf.Clamp01(
            Mathf.Max(0f, -recoveryMarginSeconds) /
            Mathf.Max(0.01f, matchplayRecoverySevereMarginSeconds));
        if (significantRunningContact)
            deficit01 = Mathf.Max(deficit01, 0.15f);

        return Mathf.Lerp(minimum, maximum, deficit01);
    }

    private float EstimateAIRecoverySeconds()
    {
        if (!hasRuntimeBasePosition)
            CacheBasePosition();

        Vector3 recoveryTarget = hasRuntimeBasePosition ? ClampToMovementBounds(runtimeBasePosition) : transform.position;
        Vector3 contactStart = currentPlan.valid ? currentPlan.stancePoint : transform.position;
        Vector3 exitVelocity = GetEstimatedContactExitVelocity();
        float travelTime = EstimateTravelTimeAndPlantFrom(
            contactStart,
            exitVelocity,
            recoveryTarget,
            Mathf.Max(0.01f, moveSpeed * Mathf.Max(0.1f, recoverySpeedMultiplier)),
            recoveryStopDistance,
            recoveryBrakeBuffer);
        return Mathf.Max(0f, matchplayRecoveryContactSettleSeconds) + travelTime;
    }

    private Vector3 GetEstimatedContactExitVelocity()
    {
        if (!IsRunningContactPlan())
            return Vector3.zero;

        Vector3 direction = currentPlan.stancePoint - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f && movement != null)
            direction = movement.PlanarVelocity;
        if (direction.sqrMagnitude <= 0.0001f)
            return Vector3.zero;

        float speed = Mathf.Max(Mathf.Max(0f, runningContactMinimumApproachSpeed), currentPlan.approachMoveSpeed);
        return direction.normalized * speed;
    }

    private float EstimateTravelTimeAndPlantFrom(
        Vector3 startPosition,
        Vector3 initialVelocity,
        Vector3 target,
        float targetSpeed,
        float stopDistance,
        float brakeBuffer)
    {
        Vector3 desiredDirection = target - startPosition;
        desiredDirection.y = 0f;
        float remainingDistance = desiredDirection.magnitude;
        float residualSpeed = Mathf.Min(
            Mathf.Max(0.01f, plantedContactMaxResidualSpeed),
            Mathf.Max(0.01f, targetSpeed));
        if (remainingDistance <= Mathf.Max(0.01f, stopDistance) && initialVelocity.magnitude <= residualSpeed)
            return 0f;

        if (movement == null)
            return remainingDistance / Mathf.Max(0.01f, targetSpeed);

        desiredDirection /= Mathf.Max(0.0001f, remainingDistance);
        float elapsed = 0f;
        Vector3 planarVelocity = initialVelocity;
        planarVelocity.y = 0f;
        float speed = planarVelocity.magnitude;
        if (speed > 0.15f)
        {
            float directionDot = Vector3.Dot(planarVelocity.normalized, desiredDirection);
            if (movement.useWrongFooting && directionDot <= movement.wrongFootDot)
            {
                elapsed += EstimateWrongFootResetTime(speed, targetSpeed);
                speed = 0f;
            }
            else if (directionDot < movement.smoothTurnDot)
            {
                float turn01 = Mathf.InverseLerp(movement.smoothTurnDot, movement.wrongFootDot, directionDot);
                elapsed += Mathf.Lerp(0.02f, 0.08f, turn01);
                speed *= Mathf.Lerp(1f, 0.55f, turn01);
            }
        }

        float maxSpeed = Mathf.Max(0.01f, targetSpeed);
        float deceleration = Mathf.Max(0.01f, movement.deceleration);
        const float simulationStep = 1f / 120f;
        const float maximumSimulationTime = 4f;
        while (elapsed <= maximumSimulationTime)
        {
            float stoppingDistance = Mathf.Max(0f, (speed * speed - residualSpeed * residualSpeed) / (2f * deceleration));
            bool insideBrakeZone = remainingDistance <= Mathf.Max(0.01f, stopDistance) + stoppingDistance + Mathf.Max(0f, brakeBuffer);
            if (insideBrakeZone)
            {
                speed = speed > residualSpeed
                    ? Mathf.MoveTowards(speed, residualSpeed, deceleration * simulationStep)
                    : Mathf.MoveTowards(speed, residualSpeed, EstimateMotorAcceleration(speed, maxSpeed) * simulationStep);
            }
            else
            {
                speed = Mathf.MoveTowards(speed, maxSpeed, EstimateMotorAcceleration(speed, maxSpeed) * simulationStep);
            }

            remainingDistance = Mathf.Max(0f, remainingDistance - speed * simulationStep);
            elapsed += simulationStep;
            if (remainingDistance <= Mathf.Max(0.01f, stopDistance) && speed <= residualSpeed + 0.01f)
                return elapsed;
        }

        return maximumSimulationTime;
    }

    private Vector2 GetMatchplaySpeedRange(MatchplayRallyState state, bool winnerCandidate, bool recoveryBall)
    {
        Vector2 speedRange;
        if (recoveryBall && !winnerCandidate)
            speedRange = matchplayRecoverySpeedRange;
        else if (winnerCandidate)
            speedRange = matchplayWinnerSpeedRange;
        else if (state == MatchplayRallyState.Defensive)
            speedRange = matchplayDefensiveSpeedRange;
        else if (state == MatchplayRallyState.Offensive)
            speedRange = matchplayOffensiveSpeedRange;
        else
            speedRange = matchplayNeutralSpeedRange;

        if (enforceMatchplayRallyPaceEnvelope && !recoveryBall)
        {
            Vector2 envelope = winnerCandidate
                ? matchplayWinnerPaceEnvelope
                : state == MatchplayRallyState.Defensive
                    ? matchplayDefensivePaceEnvelope
                    : state == MatchplayRallyState.Offensive
                        ? matchplayOffensivePaceEnvelope
                        : matchplayNeutralPaceEnvelope;
            speedRange = ConstrainRallySpeedRangeToEnvelope(speedRange, envelope);
        }

        return ClampRallySpeedRange(speedRange);
    }

    private static Vector2 ClampRallySpeedRange(Vector2 range)
    {
        float maxSpeed = BaseShotLibrary.RallyMaxSpeedMps;
        return new Vector2(
            Mathf.Clamp(range.x, 0f, maxSpeed),
            Mathf.Clamp(range.y, 0f, maxSpeed));
    }

    private static Vector2 ConstrainRallySpeedRangeToEnvelope(Vector2 requested, Vector2 envelope)
    {
        float min = Mathf.Max(0f, Mathf.Min(envelope.x, envelope.y));
        float max = Mathf.Max(min, Mathf.Max(envelope.x, envelope.y));
        float requestedMin = Mathf.Min(requested.x, requested.y);
        float requestedMax = Mathf.Max(requested.x, requested.y);
        if (requestedMax < min)
            return new Vector2(min, max);

        float constrainedMin = Mathf.Clamp(requestedMin, min, max);
        float constrainedMax = Mathf.Clamp(requestedMax, min, max);
        if (constrainedMax < constrainedMin)
            constrainedMax = constrainedMin;
        return new Vector2(constrainedMin, constrainedMax);
    }

    private BaseShotType PickMatchplayShotType(MatchplayRallyState state, float risk, bool winnerCandidate, bool recoveryBall)
    {
        if (!varyShotType)
            return baseShotType;

        if (recoveryBall)
            return Random.value < 0.82f ? BaseShotType.Topspin : BaseShotType.Slice;

        if (state == MatchplayRallyState.Defensive)
        {
            float roll = Random.value;
            if (roll < 0.78f)
                return BaseShotType.Topspin;
            if (roll < 0.98f)
                return BaseShotType.Slice;
            return BaseShotType.Flat;
        }

        if (state == MatchplayRallyState.Offensive || winnerCandidate)
        {
            float roll = Random.value;
            if (roll < 0.66f)
                return BaseShotType.Topspin;
            if (roll < 0.95f)
                return BaseShotType.Flat;
            return BaseShotType.Slice;
        }

        if (risk > 7f)
            return Random.value < 0.78f ? BaseShotType.Topspin : BaseShotType.Slice;

        float neutralRoll = Random.value;
        if (neutralRoll < 0.72f)
            return BaseShotType.Topspin;
        if (neutralRoll < 0.89f)
            return BaseShotType.Flat;
        return BaseShotType.Slice;
    }

    private float PickMatchplayHeightIntent(MatchplayRallyState state, BaseShotType shotType, float risk)
    {
        if (state == MatchplayRallyState.Defensive)
            return shotType == BaseShotType.Slice ? Random.Range(0.64f, 0.78f) : Random.Range(0.60f, 0.74f);

        if (state == MatchplayRallyState.Offensive)
            return shotType == BaseShotType.Flat ? Random.Range(0.42f, 0.56f) : Random.Range(0.48f, 0.64f);

        if (shotType == BaseShotType.Slice)
            return Random.Range(0.58f, 0.72f);
        if (shotType == BaseShotType.Flat)
            return Random.Range(0.48f, 0.62f);
        return Random.Range(0.54f, 0.68f);
    }

    private float GetXAtTargetDepth01(float minX, float maxX, float depth01)
    {
        float targetSideSign = GetTargetSideSign(minX, maxX);
        float nearX = targetSideSign >= 0f ? Mathf.Min(minX, maxX) : Mathf.Max(minX, maxX);
        float farX = targetSideSign >= 0f ? Mathf.Max(minX, maxX) : Mathf.Min(minX, maxX);
        return Mathf.Lerp(nearX, farX, Mathf.Clamp01(depth01));
    }

    private float GetAwayFromOpponentZ01(float opponentZ, float minZ, float maxZ, float amount)
    {
        float center = (minZ + maxZ) * 0.5f;
        bool opponentOnHighSide = opponentZ >= center;
        return opponentOnHighSide ? 1f - Mathf.Clamp01(amount) : Mathf.Clamp01(amount);
    }

    private float GetModerateProbeAwayZ01(float opponentZ, float minZ, float maxZ)
    {
        float minimum = Mathf.Clamp01(Mathf.Min(matchplayProbeAwayFromCenterRange01.x, matchplayProbeAwayFromCenterRange01.y));
        float maximum = Mathf.Clamp01(Mathf.Max(matchplayProbeAwayFromCenterRange01.x, matchplayProbeAwayFromCenterRange01.y));
        float offset = Random.Range(minimum, maximum);
        float centre = (minZ + maxZ) * 0.5f;
        float directionAway = Mathf.Abs(opponentZ - centre) <= 0.12f
            ? (Random.value < 0.5f ? -1f : 1f)
            : opponentZ >= centre ? -1f : 1f;
        return Mathf.Clamp01(0.5f + directionAway * offset);
    }

    private static float GetModerateProbeValue(Vector3 target, Vector3 opponent, float minZ, float maxZ)
    {
        float width = Mathf.Max(0.01f, Mathf.Abs(maxZ - minZ));
        float lateralSeparation01 = Mathf.Abs(target.z - opponent.z) / width;
        return Mathf.Clamp01(1f - Mathf.Abs(lateralSeparation01 - 0.34f) / 0.34f);
    }

    private bool HasMatchplayOpponentMomentum(float targetSideSign)
    {
        if (!matchplayUseOpponentMomentum)
            return false;

        return GetMatchplayOpponentPlanarVelocity(targetSideSign).magnitude >= Mathf.Max(0f, matchplayMomentumMinimumSpeed);
    }

    private Vector3 GetMatchplayOpponentPlanarVelocity(float targetSideSign)
    {
        if (!matchplayUseOpponentMomentum)
            return Vector3.zero;

        Transform opponent = ResolveMatchplayOpponent(targetSideSign);
        if (opponent == null)
            return Vector3.zero;

        PlayerMovement opponentMovement = opponent.GetComponent<PlayerMovement>();
        if (opponentMovement == null)
            opponentMovement = opponent.GetComponentInChildren<PlayerMovement>();
        if (opponentMovement == null)
            return Vector3.zero;

        Vector3 velocity = opponentMovement.PlanarVelocity;
        velocity.y = 0f;
        return velocity;
    }

    private float GetMomentumReversalZ01(float opponentZ, float minZ, float maxZ, Vector3 opponentVelocity)
    {
        opponentVelocity.y = 0f;
        if (Mathf.Abs(opponentVelocity.z) < 0.1f)
            return GetAwayFromOpponentZ01(opponentZ, minZ, maxZ, 0.70f);

        float courtWidth = Mathf.Abs(maxZ - minZ);
        float oppositeRunOffset = Mathf.Min(courtWidth * 0.36f, 2.45f);
        float targetZ = opponentZ - Mathf.Sign(opponentVelocity.z) * oppositeRunOffset;
        return Mathf.InverseLerp(minZ, maxZ, Mathf.Clamp(targetZ, minZ, maxZ));
    }

    private float GetOpponentMomentumReversal(Vector3 target, Vector3 opponent, out float turnSeconds)
    {
        turnSeconds = 0f;
        float targetSideSign = Mathf.Sign(target.x - netX);
        Vector3 velocity = GetMatchplayOpponentPlanarVelocity(targetSideSign);
        float speed = velocity.magnitude;
        float minimumSpeed = Mathf.Max(0.01f, matchplayMomentumMinimumSpeed);
        if (speed < minimumSpeed)
            return 0f;

        Vector3 towardTarget = target - opponent;
        towardTarget.y = 0f;
        if (towardTarget.sqrMagnitude <= 0.0001f)
            return 0f;

        float directionDot = Vector3.Dot(velocity.normalized, towardTarget.normalized);
        float oppositeDirection01 = Mathf.Clamp01((0.10f - directionDot) / 0.95f);
        float speed01 = Mathf.InverseLerp(minimumSpeed, Mathf.Max(minimumSpeed + 0.01f, matchplayOpponentReachSpeed), speed);
        float reversal01 = Mathf.Clamp01(oppositeDirection01 * speed01);
        turnSeconds = reversal01 * Mathf.Max(0f, matchplayMomentumTurnSeconds);
        return reversal01;
    }

    private Vector3 GetMatchplayOpponentPosition(float targetSideSign, float minZ, float maxZ)
    {
        Transform opponent = ResolveMatchplayOpponent(targetSideSign);
        if (opponent != null)
            return opponent.position;

        float targetX = netX + targetSideSign * 6.5f;
        return new Vector3(targetX, returnTargetY, (minZ + maxZ) * 0.5f);
    }

    private Transform ResolveMatchplayOpponent(float targetSideSign)
    {
        if (matchplayOpponent != null)
            return matchplayOpponent;

        if (autoFindMatchplayOpponent && !string.IsNullOrEmpty(matchplayOpponentName))
            matchplayOpponent = FindTransform(matchplayOpponentName);

        if (matchplayOpponent != null)
            return matchplayOpponent;

        hitController[] hitters = FindObjectsByType<hitController>(FindObjectsSortMode.None);
        Transform best = null;
        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < hitters.Length; i++)
        {
            if (hitters[i] == null || hitters[i].transform == transform)
                continue;

            Transform candidate = hitters[i].transform;
            if (Mathf.Sign(candidate.position.x - netX) != Mathf.Sign(targetSideSign))
                continue;

            float distance = HorizontalDistance(transform.position, candidate.position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        matchplayOpponent = best;
        return matchplayOpponent;
    }
    private Vector3 PickReturnTarget()
    {
        lastTargetPick = default;

        if (TryPickBoundedReturnTarget(out TargetPick boundedPick))
        {
            lastTargetPick = boundedPick;
            if (debugTargetRiskLogs)
                Debug.Log($"[TennisAI] Target cell: risk={boundedPick.risk:F1}, depth={boundedPick.depth01:F2}, grid=({boundedPick.gridX},{boundedPick.gridZ}), pos={boundedPick.position}");

            return boundedPick.position;
        }

        if (PickInBounds(returnTargetXRange, returnTargetZRange, out TargetPick fallbackPick))
        {
            lastTargetPick = fallbackPick;
            return fallbackPick.position;
        }

        Vector3 fallbackTarget = new Vector3((returnTargetXRange.x + returnTargetXRange.y) * 0.5f, returnTargetY, (returnTargetZRange.x + returnTargetZRange.y) * 0.5f);
        lastTargetPick = new TargetPick { valid = true, position = fallbackTarget, risk = 5f, depth01 = 0.5f };
        return fallbackTarget;
    }

    private bool ShouldPlayPracticeDropShot(float pressure, bool baselineVolley)
    {
        if (!IsPracticeModeActive || baselineVolley || !currentPlan.valid)
            return false;

        if (pressure > Mathf.Clamp01(practiceDropMaxPressure))
            return false;

        if (GetIncomingReferenceSpeedMps() > Mathf.Max(0f, practiceDropMaxIncomingSpeed))
            return false;

        return Random.value <= Mathf.Clamp01(practiceDropShotChance);
    }

    private Vector3 PickPracticeDropTarget()
    {
        Vector2 xRange;
        Vector2 zRange;
        if (!TryGetRawReturnTargetBounds(out xRange, out zRange))
        {
            xRange = returnTargetXRange;
            zRange = returnTargetZRange;
        }

        float targetSideSign = GetPracticeTargetSideSign(xRange);
        float contactDistanceFromNet = currentPlan.valid
            ? Mathf.Abs(currentPlan.contactPoint.x - netX)
            : Mathf.Abs(transform.position.x - netX);

        Vector2 landingRange = GetPracticeDropLandingRange(contactDistanceFromNet);
        float landingPastNet = Random.Range(
            Mathf.Min(landingRange.x, landingRange.y),
            Mathf.Max(landingRange.x, landingRange.y));

        float minX = Mathf.Min(xRange.x, xRange.y);
        float maxX = Mathf.Max(xRange.x, xRange.y);
        float minZ = Mathf.Min(zRange.x, zRange.y) + Mathf.Max(0f, targetBoundsPaddingZ);
        float maxZ = Mathf.Max(zRange.x, zRange.y) - Mathf.Max(0f, targetBoundsPaddingZ);
        if (minZ > maxZ)
        {
            float mid = (minZ + maxZ) * 0.5f;
            minZ = mid;
            maxZ = mid;
        }

        float targetX = netX + targetSideSign * landingPastNet;
        targetX = Mathf.Clamp(targetX, minX, maxX);
        float centerZ = (minZ + maxZ) * 0.5f;
        float halfWidth = Mathf.Max(0f, (maxZ - minZ) * 0.5f);
        float targetZ = Mathf.Clamp(centerZ + Random.Range(-halfWidth * 0.35f, halfWidth * 0.35f), minZ, maxZ);

        Vector3 target = new Vector3(targetX, returnTargetY, targetZ);
        lastTargetPick = new TargetPick
        {
            valid = true,
            position = target,
            risk = 4f,
            depth01 = GetTargetDepth01(target.x, minX, maxX),
            gridX = 2,
            gridZ = 2,
            xRange = new Vector2(minX, maxX),
            zRange = new Vector2(minZ, maxZ)
        };

        return target;
    }

    private Vector2 GetPracticeDropLandingRange(float contactDistanceFromNet)
    {
        if (contactDistanceFromNet >= 7.5f)
            return practiceDropLandingPastNetBaseline;

        if (contactDistanceFromNet >= 3.8f)
            return practiceDropLandingPastNetMidCourt;

        return practiceDropLandingPastNetFrontCourt;
    }

    private float GetPracticeTargetSideSign(Vector2 xRange)
    {
        float center = (xRange.x + xRange.y) * 0.5f;
        if (Mathf.Abs(center - netX) > 0.01f)
            return Mathf.Sign(center - netX);

        float aiSide = GetAISideSign();
        return Mathf.Abs(aiSide) > 0.01f ? -Mathf.Sign(aiSide) : -1f;
    }

    private bool TryGetRawReturnTargetBounds(out Vector2 xRange, out Vector2 zRange)
    {
        if (ShouldUsePlayerReticleBoundsForTarget() && TryGetPlayerReticleBounds(out xRange, out zRange))
            return true;

        if (ShouldUseOurBoundsForTarget() && TryGetFourCornerBounds(targetBoundFL, targetBoundFR, targetBoundRR, targetBoundRL, out xRange, out zRange))
            return true;

        xRange = Vector2.zero;
        zRange = Vector2.zero;
        return false;
    }

    private bool TryPickBoundedReturnTarget(out TargetPick pick)
    {
        pick = default;

        if (ShouldUsePlayerReticleBoundsForTarget() && TryGetPlayerReticleBounds(out Vector2 playerX, out Vector2 playerZ))
            return PickInBounds(playerX, playerZ, out pick);

        if (ShouldUseOurBoundsForTarget() && TryGetFourCornerBounds(targetBoundFL, targetBoundFR, targetBoundRR, targetBoundRL, out Vector2 ourX, out Vector2 ourZ))
            return PickInBounds(ourX, ourZ, out pick);

        if (useMirroredAIBoundsForReturnTarget && HasAIBounds())
        {
            Vector3 aiFront = GetFrontCenter();
            Vector3 aiBack = GetBackCenter();
            Vector3 fl = GetBoundPosition(aiSideFL);
            Vector3 fr = GetBoundPosition(aiSideFR);
            Vector3 bl = GetBoundPosition(aiSideBL);
            Vector3 br = GetBoundPosition(aiSideBR);

            float aiX = Random.Range(Mathf.Min(aiFront.x, aiBack.x), Mathf.Max(aiFront.x, aiBack.x));
            float targetX = (2f * netX) - aiX;
            targetX = ClampTargetXAwayFromNet(targetX);
            float targetZ = Random.Range(
                Mathf.Min(fl.z, fr.z, bl.z, br.z),
                Mathf.Max(fl.z, fr.z, bl.z, br.z));

            Vector3 position = new Vector3(targetX, returnTargetY, targetZ);
            pick = new TargetPick
            {
                valid = true,
                position = position,
                risk = EstimateTargetRisk(position, new Vector2(targetX, targetX), new Vector2(targetZ, targetZ), 0, 0),
                depth01 = 0.5f,
                gridX = 2,
                gridZ = 2,
                xRange = new Vector2(targetX, targetX),
                zRange = new Vector2(targetZ, targetZ)
            };
            return true;
        }

        return false;
    }

    private bool TryPickBoundedReturnTarget(out Vector3 boundedTarget)
    {
        boundedTarget = Vector3.zero;
        if (TryPickBoundedReturnTarget(out TargetPick pick))
        {
            boundedTarget = pick.position;
            return true;
        }

        return false;
    }

    private bool PickInBounds(Vector2 xRange, Vector2 zRange, out Vector3 target)
    {
        target = Vector3.zero;
        if (!PickInBounds(xRange, zRange, out TargetPick pick))
            return false;

        target = pick.position;
        return true;
    }

    private bool PickInBounds(Vector2 xRange, Vector2 zRange, out TargetPick pick)
    {
        pick = default;
        float minX = Mathf.Min(xRange.x, xRange.y) + Mathf.Max(0f, targetBoundsPaddingX);
        float maxX = Mathf.Max(xRange.x, xRange.y) - Mathf.Max(0f, targetBoundsPaddingX);
        float minZ = Mathf.Min(zRange.x, zRange.y) + Mathf.Max(0f, targetBoundsPaddingZ);
        float maxZ = Mathf.Max(zRange.x, zRange.y) - Mathf.Max(0f, targetBoundsPaddingZ);

        if (minX > maxX)
        {
            float mid = (minX + maxX) * 0.5f;
            minX = mid;
            maxX = mid;
        }

        if (minZ > maxZ)
        {
            float mid = (minZ + maxZ) * 0.5f;
            minZ = mid;
            maxZ = mid;
        }

        ConstrainTargetXRangeAwayFromNet(ref minX, ref maxX);
        pick = useRiskRewardTargeting
            ? PickRiskRewardCell(minX, maxX, minZ, maxZ)
            : PickRandomCellTarget(minX, maxX, minZ, maxZ);
        return true;
    }

    private TargetPick PickRandomCellTarget(float minX, float maxX, float minZ, float maxZ)
    {
        Vector3 position = new Vector3(Random.Range(minX, maxX), returnTargetY, Random.Range(minZ, maxZ));
        return new TargetPick
        {
            valid = true,
            position = position,
            risk = EstimateTargetRisk(position, new Vector2(minX, maxX), new Vector2(minZ, maxZ), 2, 2),
            depth01 = GetTargetDepth01(position.x, minX, maxX),
            gridX = 2,
            gridZ = 2,
            xRange = new Vector2(minX, maxX),
            zRange = new Vector2(minZ, maxZ)
        };
    }

    private TargetPick PickRiskRewardCell(float minX, float maxX, float minZ, float maxZ)
    {
        int grid = Mathf.Clamp(riskGridSize, 3, 9);
        float pressure = GetShotPressure01();
        float roll = Random.value;
        float maxRisk = 10f;
        int mode = 2;
        bool preferSideLane = Random.value <= Mathf.Clamp01(sideLaneTargetChance);
        bool highRiskTest = Random.value <= Mathf.Clamp01(highRiskTestChance);

        if (highRiskTest)
        {
            maxRisk = 10f;
            mode = 3;
        }
        else if (roll <= Mathf.Clamp01(safeTargetChance))
        {
            maxRisk = safeMaxRisk;
            mode = 0;
        }
        else if (roll <= Mathf.Clamp01(safeTargetChance + neutralTargetChance))
        {
            maxRisk = neutralMaxRisk;
            mode = 1;
        }

        if (pressure > 0.65f)
        {
            maxRisk = Mathf.Min(maxRisk, safeMaxRisk);
            highRiskTest = false;
        }

        if (IsPracticeModeActive && !pendingPracticeVariation)
        {
            maxRisk = Mathf.Min(maxRisk, safeMaxRisk);
            highRiskTest = false;
            preferSideLane = false;
            mode = 0;
        }

        TargetPick bestPick = default;
        float bestScore = float.PositiveInfinity;
        bool found = false;

        for (int x = 0; x < grid; x++)
        {
            float cellMinX = Mathf.Lerp(minX, maxX, x / (float)grid);
            float cellMaxX = Mathf.Lerp(minX, maxX, (x + 1) / (float)grid);
            for (int z = 0; z < grid; z++)
            {
                float cellMinZ = Mathf.Lerp(minZ, maxZ, z / (float)grid);
                float cellMaxZ = Mathf.Lerp(minZ, maxZ, (z + 1) / (float)grid);
                Vector3 center = new Vector3((cellMinX + cellMaxX) * 0.5f, returnTargetY, (cellMinZ + cellMaxZ) * 0.5f);
                float risk = EstimateTargetRisk(center, new Vector2(minX, maxX), new Vector2(minZ, maxZ), x, z);
                if (risk > maxRisk)
                    continue;

                float depth01 = GetTargetDepth01(center.x, minX, maxX);
                float lateralEdge01 = GetLateralEdge01(center.z, minZ, maxZ);
                float riskWeight = mode == 2 ? 0.35f : mode == 3 ? -0.25f : 0.85f;
                float practiceDepthBoost = IsPracticeModeActive && !pendingPracticeVariation ? Mathf.Max(0f, practiceRallyBackCourtBias) : 0f;
                float depthWeight = mode == 2 ? backCourtPreference * 0.7f : mode == 3 ? backCourtPreference * 0.35f : backCourtPreference + practiceDepthBoost;
                float centerWeight = preferSideLane ? -sideLanePreference : (mode == 2 ? centerCourtPreference * 0.35f : centerCourtPreference);
                float score =
                    risk * riskWeight
                    - depth01 * depthWeight
                    + lateralEdge01 * centerWeight
                    + pressure * risk * 0.35f
                    + Random.Range(0f, mode == 0 ? 1.15f : mode == 3 ? 2.1f : 1.65f);

                if (score < bestScore)
                {
                    bestScore = score;
                    bestPick = BuildTargetPickFromCell(cellMinX, cellMaxX, cellMinZ, cellMaxZ, minX, maxX, minZ, maxZ, x, z, risk);
                    found = true;
                }
            }
        }

        if (found)
            return bestPick;

        return PickRandomCellTarget(minX, maxX, minZ, maxZ);
    }

    private TargetPick BuildTargetPickFromCell(float cellMinX, float cellMaxX, float cellMinZ, float cellMaxZ, float minX, float maxX, float minZ, float maxZ, int gridX, int gridZ, float risk)
    {
        float jitter = Mathf.Clamp01(targetCellJitter);
        float centerX = (cellMinX + cellMaxX) * 0.5f;
        float centerZ = (cellMinZ + cellMaxZ) * 0.5f;
        float halfX = (cellMaxX - cellMinX) * 0.5f * jitter;
        float halfZ = (cellMaxZ - cellMinZ) * 0.5f * jitter;
        Vector3 position = new Vector3(
            Mathf.Clamp(Random.Range(centerX - halfX, centerX + halfX), minX, maxX),
            returnTargetY,
            Mathf.Clamp(Random.Range(centerZ - halfZ, centerZ + halfZ), minZ, maxZ));

        return new TargetPick
        {
            valid = true,
            position = position,
            risk = risk,
            depth01 = GetTargetDepth01(position.x, minX, maxX),
            gridX = gridX,
            gridZ = gridZ,
            xRange = new Vector2(minX, maxX),
            zRange = new Vector2(minZ, maxZ)
        };
    }

    private float EstimateTargetRisk(Vector3 target, Vector2 xRange, Vector2 zRange, int gridX, int gridZ)
    {
        int grid = Mathf.Clamp(riskGridSize, 3, 9);
        float lateralEdge01 = GetLateralEdge01(target.z, Mathf.Min(zRange.x, zRange.y), Mathf.Max(zRange.x, zRange.y));
        float frontCourt01 = 1f - GetTargetDepth01(target.x, Mathf.Min(xRange.x, xRange.y), Mathf.Max(xRange.x, xRange.y));
        bool lateralEdgeCell = gridZ == 0 || gridZ == grid - 1;
        bool depthEdgeCell = gridX == 0 || gridX == grid - 1;
        bool cornerCell = lateralEdgeCell && depthEdgeCell;

        float risk = 1.6f;
        risk += frontCourt01 * 2.2f;
        risk += cornerCell ? 5.8f : 0f;
        risk += depthEdgeCell && frontCourt01 > 0.55f ? 1.8f : 0f;
        risk += lateralEdge01 * (cornerCell ? 1.2f : 0.25f);
        return Mathf.Clamp(risk, 1f, 10f);
    }

    private float GetTargetDepth01(float x, float minX, float maxX)
    {
        float nearDepth = Mathf.Min(Mathf.Abs(minX - netX), Mathf.Abs(maxX - netX));
        float farDepth = Mathf.Max(Mathf.Abs(minX - netX), Mathf.Abs(maxX - netX));
        if (farDepth - nearDepth <= 0.001f)
            return 0.5f;

        return Mathf.InverseLerp(nearDepth, farDepth, Mathf.Abs(x - netX));
    }

    private float GetLateralEdge01(float z, float minZ, float maxZ)
    {
        float centerZ = (minZ + maxZ) * 0.5f;
        float halfWidth = Mathf.Max(0.001f, (maxZ - minZ) * 0.5f);
        return Mathf.Clamp01(Mathf.Abs(z - centerZ) / halfWidth);
    }

    private void ConstrainTargetXRangeAwayFromNet(ref float minX, ref float maxX)
    {
        float minDepth = GetMinimumReturnDepthFromNet();
        if (minDepth <= 0f)
            return;

        float originalMinX = minX;
        float originalMaxX = maxX;
        float targetSideSign = GetTargetSideSign(minX, maxX);
        float depthLimit = netX + targetSideSign * minDepth;

        if (targetSideSign > 0f)
            minX = Mathf.Max(minX, depthLimit);
        else
            maxX = Mathf.Min(maxX, depthLimit);

        if (minX <= maxX)
            return;

        float farthestX = Mathf.Abs(originalMinX - netX) >= Mathf.Abs(originalMaxX - netX) ? originalMinX : originalMaxX;
        minX = farthestX;
        maxX = farthestX;
    }

    private float ClampTargetXAwayFromNet(float targetX)
    {
        float minDepth = GetMinimumReturnDepthFromNet();
        if (minDepth <= 0f)
            return targetX;

        float sideSign = Mathf.Abs(targetX - netX) > 0.01f ? Mathf.Sign(targetX - netX) : -GetAISideSign();
        float depthLimit = netX + sideSign * minDepth;
        return sideSign > 0f ? Mathf.Max(targetX, depthLimit) : Mathf.Min(targetX, depthLimit);
    }

    private float GetTargetSideSign(float minX, float maxX)
    {
        float midX = (minX + maxX) * 0.5f;
        if (Mathf.Abs(midX - netX) > 0.01f)
            return Mathf.Sign(midX - netX);

        float minDepth = Mathf.Abs(minX - netX);
        float maxDepth = Mathf.Abs(maxX - netX);
        if (minDepth > maxDepth && minDepth > 0.01f)
            return Mathf.Sign(minX - netX);
        if (maxDepth > 0.01f)
            return Mathf.Sign(maxX - netX);

        float aiSideSign = GetAISideSign();
        return Mathf.Abs(aiSideSign) > 0.01f ? -Mathf.Sign(aiSideSign) : -1f;
    }

    private float GetMinimumReturnDepthFromNet()
    {
        if (!avoidShortReturnTargets)
            return 0f;

        float depth = Mathf.Max(0f, minReturnDepthFromNet);
        if (IsPracticeModeActive && !pendingPracticeDropShot)
            depth = Mathf.Max(depth, practiceRallyMinDepthFromNet);

        if (baseShotType == BaseShotType.Flat)
            depth = Mathf.Max(depth, flatShotMinReturnDepthFromNet);

        return depth;
    }

    private bool ShouldUsePlayerReticleBoundsForTarget()
    {
        if (!nearSideUsesPlayerReticleBounds)
            return false;

        float nearSign;
        if (!TryGetNamedSideSign(nearSideBaseName, out nearSign))
            return GetAISideSign() > 0f;

        return Mathf.Sign(GetAISideSign()) == Mathf.Sign(nearSign);
    }

    private bool ShouldUseOurBoundsForTarget()
    {
        if (!farSideUsesOurBounds)
            return false;

        float nearSign;
        if (!TryGetNamedSideSign(nearSideBaseName, out nearSign))
            return GetAISideSign() < 0f;

        return Mathf.Sign(GetAISideSign()) != Mathf.Sign(nearSign);
    }

    private bool TryGetPlayerReticleBounds(out Vector2 xRange, out Vector2 zRange)
    {
        xRange = Vector2.zero;
        zRange = Vector2.zero;

        if (playerReticleBoundsSource == null)
            return false;

        Transform minBound = playerReticleBoundsSource.minBound;
        Transform maxBound = playerReticleBoundsSource.maxBound;
        if (minBound == null || maxBound == null)
            return false;

        xRange = new Vector2(minBound.position.x, maxBound.position.x);
        zRange = new Vector2(minBound.position.z, maxBound.position.z);
        return Mathf.Abs(xRange.y - xRange.x) > 0.1f && Mathf.Abs(zRange.y - zRange.x) > 0.1f;
    }

    private bool TryGetFourCornerBounds(Transform fl, Transform fr, Transform rr, Transform rl, out Vector2 xRange, out Vector2 zRange)
    {
        xRange = Vector2.zero;
        zRange = Vector2.zero;
        if (fl == null || fr == null || rr == null || rl == null)
            return false;

        float minX = Mathf.Min(fl.position.x, fr.position.x, rr.position.x, rl.position.x);
        float maxX = Mathf.Max(fl.position.x, fr.position.x, rr.position.x, rl.position.x);
        float minZ = Mathf.Min(fl.position.z, fr.position.z, rr.position.z, rl.position.z);
        float maxZ = Mathf.Max(fl.position.z, fr.position.z, rr.position.z, rl.position.z);

        xRange = new Vector2(minX, maxX);
        zRange = new Vector2(minZ, maxZ);
        return Mathf.Abs(maxX - minX) > 0.1f && Mathf.Abs(maxZ - minZ) > 0.1f;
    }

    private bool TryGetNamedSideSign(string objectName, out float sideSign)
    {
        sideSign = 0f;
        Transform named = FindTransform(objectName);
        if (named == null || Mathf.Abs(named.position.x - netX) < 0.01f)
            return false;

        sideSign = Mathf.Sign(named.position.x - netX);
        return true;
    }

    private Vector3 ApplyLandingDispersion(Vector3 intendedTarget, BaseShotType shotType, float pressure, float risk)
    {
        if (!useLandingDispersionModel)
            return intendedTarget;

        Vector2 sigma = GetLandingDispersionSigma(shotType, risk);
        float pressureScale = Mathf.Lerp(1f, Mathf.Max(1f, pressureDispersionMultiplier), Mathf.Clamp01(pressure));
        sigma *= pressureScale;
        float fastReturn01 = currentPlan.valid ? GetFastServeReturn01(currentPlan.incomingVelocity.magnitude) : 0f;
        sigma *= Mathf.Lerp(1f, Mathf.Clamp01(fastReturnLandingDispersionScale), fastReturn01);
        if (IsBaselineVolley(currentPlan))
            sigma *= Mathf.Max(1f, baselineVolleyDispersionMultiplier);

        float depthError = SampleGaussianClamped() * Mathf.Max(0f, sigma.x);
        float widthError = SampleGaussianClamped() * Mathf.Max(0f, sigma.y);
        float targetSideSign = Mathf.Abs(intendedTarget.x - netX) > 0.01f
            ? Mathf.Sign(intendedTarget.x - netX)
            : -GetAISideSign();

        Vector3 dispersed = intendedTarget;
        dispersed.x += targetSideSign * depthError;
        dispersed.z += widthError;
        dispersed.y = returnTargetY;

        bool safeEnoughToClamp = risk <= safeMaxRisk && pressure < 0.65f;
        if (clampSafeDispersedTargetsToBounds && safeEnoughToClamp && lastTargetPick.valid)
        {
            float minX = Mathf.Min(lastTargetPick.xRange.x, lastTargetPick.xRange.y);
            float maxX = Mathf.Max(lastTargetPick.xRange.x, lastTargetPick.xRange.y);
            float minZ = Mathf.Min(lastTargetPick.zRange.x, lastTargetPick.zRange.y);
            float maxZ = Mathf.Max(lastTargetPick.zRange.x, lastTargetPick.zRange.y);
            dispersed.x = Mathf.Clamp(dispersed.x, minX, maxX);
            dispersed.z = Mathf.Clamp(dispersed.z, minZ, maxZ);
        }

        if (debugDispersionLogs)
        {
            Debug.Log(
                $"[TennisAI DISPERSION] shot={shotType}, risk={risk:F1}, pressure={pressure:F2}, " +
                $"sigmaDepth={sigma.x:F2}m, sigmaWidth={sigma.y:F2}m, depthError={depthError:F2}m, widthError={widthError:F2}m, " +
                $"intended={intendedTarget}, dispersed={dispersed}");
        }

        return dispersed;
    }

    private Vector3 EnforceFastReturnDepth(Vector3 target, float fastReturn01)
    {
        if (fastReturn01 <= 0f || !lastTargetPick.valid)
            return target;

        float minX = Mathf.Min(lastTargetPick.xRange.x, lastTargetPick.xRange.y);
        float maxX = Mathf.Max(lastTargetPick.xRange.x, lastTargetPick.xRange.y);
        float minimumDepth01 = Mathf.Lerp(
            Mathf.Clamp01(Mathf.Min(fastReturnTargetDepthRange01.x, fastReturnTargetDepthRange01.y)),
            Mathf.Clamp01(Mathf.Max(fastReturnTargetDepthRange01.x, fastReturnTargetDepthRange01.y)),
            fastReturn01);

        if (GetTargetDepth01(target.x, minX, maxX) < minimumDepth01)
            target.x = GetXAtTargetDepth01(minX, maxX, minimumDepth01);

        target.z = Mathf.Clamp(
            target.z,
            Mathf.Min(lastTargetPick.zRange.x, lastTargetPick.zRange.y),
            Mathf.Max(lastTargetPick.zRange.x, lastTargetPick.zRange.y));
        return target;
    }

    private Vector3 EnforceMatchplayRallySafety(Vector3 target, MatchplayDecision decision)
    {
        if (!decision.valid || !lastTargetPick.valid)
            return target;

        float minX = Mathf.Min(lastTargetPick.xRange.x, lastTargetPick.xRange.y);
        float maxX = Mathf.Max(lastTargetPick.xRange.x, lastTargetPick.xRange.y);
        float minZ = Mathf.Min(lastTargetPick.zRange.x, lastTargetPick.zRange.y);
        float maxZ = Mathf.Max(lastTargetPick.zRange.x, lastTargetPick.zRange.y);
        float targetSideSign = GetTargetSideSign(minX, maxX);
        float minimumDepthFromNet = Mathf.Max(0f, matchplayMinimumNonDropDepthFromNet);
        if (Mathf.Abs(target.x - netX) < minimumDepthFromNet)
            target.x = netX + targetSideSign * minimumDepthFromNet;

        target.x = Mathf.Clamp(target.x, minX, maxX);
        target.z = Mathf.Clamp(target.z, minZ, maxZ);
        target.y = returnTargetY;
        return target;
    }

    private Vector2 GetLandingDispersionSigma(BaseShotType shotType, float risk)
    {
        if (risk >= neutralMaxRisk)
            return winnerDispersion;

        float aggressive01 = Mathf.InverseLerp(safeMaxRisk, neutralMaxRisk, risk);
        switch (shotType)
        {
            case BaseShotType.Flat:
                return LerpRange(flatSafeDispersion, flatAggressiveDispersion, aggressive01);
            case BaseShotType.Slice:
                return LerpRange(sliceSafeDispersion, sliceAggressiveDispersion, aggressive01);
            case BaseShotType.Topspin:
            default:
                return LerpRange(topspinSafeDispersion, topspinAggressiveDispersion, aggressive01);
        }
    }

    private float SampleGaussianClamped()
    {
        float u1 = Mathf.Max(0.000001f, Random.value);
        float u2 = Mathf.Max(0.000001f, Random.value);
        float standardNormal = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
        return Mathf.Clamp(standardNormal, -Mathf.Max(0.1f, maxDispersionSigmaSample), Mathf.Max(0.1f, maxDispersionSigmaSample));
    }

    private BaseShotType PickShotType(float pressure, float risk)
    {
        return PickShotType(pressure, risk, false, false, false);
    }

    private BaseShotType PickShotType(float pressure, float risk, bool baselineVolley)
    {
        return PickShotType(pressure, risk, baselineVolley, false, false);
    }

    private BaseShotType PickShotType(float pressure, float risk, bool baselineVolley, bool practiceVariation, bool practiceDrop)
    {
        if (!varyShotType)
            return baseShotType;

        if (practiceDrop)
            return BaseShotType.Drop;

        if (baselineVolley)
            return Random.value < Mathf.Clamp01(baselineVolleySliceChance) ? BaseShotType.Slice : BaseShotType.Topspin;

        if (pressure > 0.75f)
            return Random.value < 0.72f ? BaseShotType.Topspin : BaseShotType.Slice;

        if (IsPracticeModeActive && !practiceVariation)
            return Random.value < 0.78f ? BaseShotType.Topspin : BaseShotType.Flat;

        float top = Mathf.Max(0f, topspinChance);
        float slice = Mathf.Max(0f, sliceChance);
        float flat = Mathf.Max(0f, flatChance);

        if (risk > 7.5f)
        {
            top *= 1.25f;
            slice *= 1.15f;
            flat *= 0.55f;
        }

        float total = top + slice + flat;
        if (total <= 0.001f)
            return baseShotType;

        float roll = Random.value * total;
        if (roll <= top)
            return BaseShotType.Topspin;
        if (roll <= top + slice)
            return BaseShotType.Slice;
        return BaseShotType.Flat;
    }

    private float PickHeightIntent(BaseShotType shotType, float pressure, float risk, out bool usesCustomHeight)
    {
        return PickHeightIntent(shotType, pressure, risk, false, false, out usesCustomHeight);
    }

    private float PickHeightIntent(BaseShotType shotType, float pressure, float risk, bool baselineVolley, out bool usesCustomHeight)
    {
        return PickHeightIntent(shotType, pressure, risk, baselineVolley, false, out usesCustomHeight);
    }

    private float PickHeightIntent(BaseShotType shotType, float pressure, float risk, bool baselineVolley, bool practiceDrop, out bool usesCustomHeight)
    {
        if (practiceDrop)
        {
            usesCustomHeight = true;
            float dropMin = Mathf.Clamp01(Mathf.Min(practiceDropHeightIntentRange.x, practiceDropHeightIntentRange.y));
            float dropMax = Mathf.Clamp01(Mathf.Max(practiceDropHeightIntentRange.x, practiceDropHeightIntentRange.y));
            return Random.Range(dropMin, dropMax);
        }

        if (baselineVolley)
        {
            usesCustomHeight = true;
            float safeIntent = shotType == BaseShotType.Slice
                ? baselineVolleyHeightIntent - 0.08f
                : baselineVolleyHeightIntent;
            return Mathf.Clamp01(Mathf.Lerp(safeIntent, 0.76f, Mathf.Clamp01(pressure) * 0.35f));
        }

        usesCustomHeight = varyShotType && Random.value <= Mathf.Clamp01(customHeightChance);
        if (!usesCustomHeight)
            return BaseShotLibrary.DefaultHeightIntent;

        float min = Mathf.Clamp01(Mathf.Min(customHeightIntentRange.x, customHeightIntentRange.y));
        float max = Mathf.Clamp01(Mathf.Max(customHeightIntentRange.x, customHeightIntentRange.y));
        float intent = Random.Range(min, max);

        if (pressure > 0.65f)
            intent = Mathf.Lerp(intent, 0.58f, 0.45f);
        else if (risk > 7f)
            intent = Mathf.Lerp(intent, shotType == BaseShotType.Flat ? 0.62f : 0.68f, 0.35f);

        return Mathf.Clamp01(intent);
    }

    private SwipeSkill PickSwipeSkill()
    {
        return PickSwipeSkill(0.5f, 5f);
    }

    private SwipeSkill PickSwipeSkill(float pressure, float risk)
    {
        float good = Mathf.Clamp01(goodSwipeChance);
        float ok = Mathf.Clamp01(okSwipeChance);

        if (tightenRallyAccuracy)
        {
            float pressure01 = GetAccuracyPressure01(pressure, risk);
            good = Mathf.Lerp(Mathf.Clamp01(calmGoodSwipeChance), good, pressure01);
            ok = Mathf.Lerp(Mathf.Clamp01(calmOkSwipeChance), ok, pressure01);
        }

        if (good + ok > 0.98f)
            ok = Mathf.Max(0f, 0.98f - good);

        float roll = Random.value;
        if (roll <= good)
            return SwipeSkill.Good;
        if (roll <= good + ok)
            return SwipeSkill.Ok;
        return SwipeSkill.Bad;
    }

    private Vector2 GetSpeedRange(SwipeSkill skill)
    {
        return GetSpeedRange(skill, 0.5f, 5f);
    }

    private Vector2 GetSpeedRange(SwipeSkill skill, float pressure, float risk)
    {
        Vector2 normal;
        if (skill == SwipeSkill.Good)
            normal = goodSpeedRange;
        else if (skill == SwipeSkill.Ok)
            normal = okSpeedRange;
        else
            normal = badSpeedRange;

        if (!tightenRallyAccuracy)
            return ClampRallySpeedRange(normal);

        Vector2 calm = skill == SwipeSkill.Good ? calmGoodSpeedRange : skill == SwipeSkill.Ok ? calmOkSpeedRange : calmBadSpeedRange;
        return ClampRallySpeedRange(LerpRange(calm, normal, GetAccuracyPressure01(pressure, risk)));
    }

    private float GetFastServeReturn01(float incomingSpeedMps)
    {
        if (!fastServeReturnAssist)
            return 0f;

        float start = Mathf.Max(0f, fastServeSpeedThresholdMps);
        float full = Mathf.Max(start + 0.1f, fastServeFullAssistSpeedMps);
        return Mathf.InverseLerp(start, full, Mathf.Max(0f, incomingSpeedMps));
    }
    private float GetPaceCompensatedSwipeSpeed(
        float desiredReturnSpeed,
        BaseShotType shotType,
        float quality,
        Vector3 returnDirection,
        ContactPlan plan,
        out float expectedPaceBonus,
        out float incomingSpinSafety)
    {
        expectedPaceBonus = 0f;
        incomingSpinSafety = 0f;

        float desired = Mathf.Clamp(desiredReturnSpeed, 0f, BaseShotLibrary.RallyMaxSpeedMps);
        if (!compensateSwipeForIncomingPace || !plan.valid)
            return desired;

        Vector3 safeDirection = returnDirection;
        safeDirection.y = 0f;
        if (safeDirection.sqrMagnitude < 0.0001f)
            safeDirection = GetFacingDirection();
        safeDirection.Normalize();

        ShotContactProfile profile = BaseShotLibrary.GetContactProfile(shotType);
        float rebound = EstimateQualityAdjustedRebound(profile.reboundCoefficient, quality);
        float incomingAlongReturn = Mathf.Max(0f, Vector3.Dot(-plan.incomingVelocity, safeDirection));
        expectedPaceBonus = incomingAlongReturn * rebound * Mathf.Max(0f, incomingPaceCompensationScale);

        float incomingSpinRpm = GetIncomingSignedSpinRpm(plan.incomingVelocity, plan.incomingSpin);
        float spinDifficulty = Mathf.InverseLerp(700f, 2400f, Mathf.Abs(incomingSpinRpm));
        incomingSpinSafety = spinDifficulty * Mathf.Max(0f, incomingSpinSafetySpeedMps);

        return Mathf.Min(
            BaseShotLibrary.RallyMaxSpeedMps,
            Mathf.Max(Mathf.Max(0f, minCompensatedSwipeSpeed), desired - expectedPaceBonus - incomingSpinSafety));
    }

    private void RefreshCurrentPlanWithLiveIncomingBallState()
    {
        RefreshPlanWithLiveIncomingBallState(ref currentPlan);
    }

    private void RefreshPlanWithLiveIncomingBallState(ref ContactPlan plan)
    {
        if (!plan.valid || ball == null)
            return;

        plan.incomingVelocity = ball.linearVelocity;
        if (ball.TryGetComponent(out BallController ballController))
            plan.incomingSpin = ballController.spinRadPerSecond;
        else
            plan.incomingSpin = ball.angularVelocity;
    }

    private float GetLiveIncomingAdjustedSpinIntent(BaseShotType shotType, ContactPlan plan, bool baselineVolley)
    {
        float intent = Mathf.Clamp01(spinIntent);
        if (!adjustSpinIntentFromLiveIncoming || !plan.valid)
            return intent;

        float speedBoost = Mathf.InverseLerp(
            Mathf.Min(incomingSpeedSpinIntentRange.x, incomingSpeedSpinIntentRange.y),
            Mathf.Max(incomingSpeedSpinIntentRange.x, incomingSpeedSpinIntentRange.y),
            plan.incomingVelocity.magnitude) * Mathf.Max(0f, incomingSpeedSpinIntentBoost);
        float spinBoost = Mathf.InverseLerp(
            Mathf.Min(incomingSpinIntentRangeRad.x, incomingSpinIntentRangeRad.y),
            Mathf.Max(incomingSpinIntentRangeRad.x, incomingSpinIntentRangeRad.y),
            plan.incomingSpin.magnitude) * Mathf.Max(0f, incomingSpinIntentBoost);

        if (shotType == BaseShotType.Flat)
            return Mathf.Clamp01(intent + speedBoost * 0.45f + spinBoost * 0.35f);

        float volleyScale = baselineVolley ? 1.25f : 1f;
        return Mathf.Clamp01(intent + (speedBoost + spinBoost) * volleyScale);
    }

    private static float EstimateQualityAdjustedRebound(float sweetSpotReboundCoefficient, float quality01)
    {
        float sweet = Mathf.Max(0.01f, sweetSpotReboundCoefficient);
        float mishit = Mathf.Max(0.08f, sweet * 0.55f);
        float q = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(quality01));
        return Mathf.Lerp(mishit, sweet, q);
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
        return signedRadPerSecond * 60f / (2f * Mathf.PI);
    }

    private Vector2 GetBaselineVolleySpeedRange(SwipeSkill skill, float difficulty)
    {
        Vector2 range;
        if (skill == SwipeSkill.Good)
            range = baselineVolleyGoodSpeedRange;
        else if (skill == SwipeSkill.Ok)
            range = baselineVolleyOkSpeedRange;
        else
            range = baselineVolleyBadSpeedRange;

        float min = Mathf.Min(range.x, range.y);
        float max = Mathf.Max(range.x, range.y);
        max = Mathf.Lerp(max, min, Mathf.Clamp01(difficulty) * 0.45f);
        return new Vector2(min, Mathf.Max(min, max));
    }

    private Vector2 GetLateralErrorRange(SwipeSkill skill)
    {
        return GetLateralErrorRange(skill, 0.5f, 5f);
    }

    private Vector2 GetLateralErrorRange(SwipeSkill skill, float pressure, float risk)
    {
        Vector2 normal;
        if (skill == SwipeSkill.Good)
            normal = goodLateralErrorDeg;
        else if (skill == SwipeSkill.Ok)
            normal = okLateralErrorDeg;
        else
            normal = badLateralErrorDeg;

        if (!tightenRallyAccuracy)
            return normal;

        Vector2 calm = skill == SwipeSkill.Good ? calmGoodLateralErrorDeg : skill == SwipeSkill.Ok ? calmOkLateralErrorDeg : calmBadLateralErrorDeg;
        return LerpRange(calm, normal, GetAccuracyPressure01(pressure, risk));
    }

    private Vector2 GetQualityRange(SwipeSkill skill)
    {
        if (skill == SwipeSkill.Good)
            return goodQualityRange;
        if (skill == SwipeSkill.Ok)
            return okQualityRange;
        return badQualityRange;
    }

    private static float RandomInRange(Vector2 range)
    {
        return Random.Range(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));
    }

    private static Vector2 LerpRange(Vector2 a, Vector2 b, float t)
    {
        return new Vector2(Mathf.Lerp(a.x, b.x, t), Mathf.Lerp(a.y, b.y, t));
    }

    private float GetAccuracyPressure01(float pressure, float risk)
    {
        float risk01 = Mathf.InverseLerp(5f, 10f, risk);
        return Mathf.Clamp01(pressure * Mathf.Max(0f, pressureAccuracyPenalty) + risk01 * Mathf.Max(0f, riskAccuracyPenalty));
    }

    private float GetShotPressure01()
    {
        if (!currentPlan.valid)
            return 0.5f;

        float moveDistance = HorizontalDistance(transform.position, currentPlan.stancePoint);
        float movePressure = Mathf.InverseLerp(0.35f, 2.4f, moveDistance);
        float timePressure = 1f - Mathf.InverseLerp(0.18f, 0.95f, currentPlan.timeUntilContact);
        float idealHeight = Mathf.Clamp(currentPlan.contactPoint.y, idealContactHeightRange.x, idealContactHeightRange.y);
        float heightPressure = Mathf.InverseLerp(0.08f, 0.55f, Mathf.Abs(currentPlan.contactPoint.y - idealHeight));
        float emergencyPressure = currentPlan.ownSideBounceCount <= 0 && preferPostBounceContact ? 0.25f : 0f;
        float baselineVolleyPressure = IsBaselineVolley(currentPlan)
            ? baselineVolleyPressureBonus * Mathf.Lerp(0.65f, 1f, GetBaselineVolleyDifficulty01(currentPlan))
            : 0f;

        return Mathf.Clamp01(Mathf.Max(movePressure, timePressure, heightPressure, emergencyPressure) + baselineVolleyPressure);
    }

    private bool IsBaselineVolley(ContactPlan plan)
    {
        return avoidBaselineVolleys && plan.valid && plan.IsVolley && IsDeepCourtPosition(plan.contactPoint);
    }

    private bool IsDeepCourtPosition(Vector3 position)
    {
        return Mathf.Abs(position.x - netX) >= Mathf.Max(0f, baselineVolleyMinDepthFromNet);
    }

    private float GetBaselineVolleyDifficulty01(ContactPlan plan)
    {
        if (!plan.valid)
            return 0f;

        float speedDifficulty = Mathf.InverseLerp(14f, 28f, plan.incomingVelocity.magnitude);
        float depthDifficulty = Mathf.InverseLerp(
            Mathf.Max(0f, baselineVolleyMinDepthFromNet),
            Mathf.Max(0.1f, baselineVolleyMinDepthFromNet + 4f),
            Mathf.Abs(plan.contactPoint.x - netX));
        float timeDifficulty = 1f - Mathf.InverseLerp(0.16f, 0.8f, plan.timeUntilContact);
        return Mathf.Clamp01(speedDifficulty * 0.45f + depthDifficulty * 0.35f + timeDifficulty * 0.2f);
    }

    private float GetAISideSign()
    {
        float baseSign;
        if (TryGetBaseSideSign(out baseSign))
            return baseSign;

        if (HasAIBounds())
        {
            float boundsCenterX = (aiSideFL.position.x + aiSideFR.position.x + aiSideBL.position.x + aiSideBR.position.x) * 0.25f;
            if (Mathf.Abs(boundsCenterX - netX) >= 0.01f)
                return Mathf.Sign(boundsCenterX - netX);
        }

        if (Mathf.Abs(transform.position.x - netX) < 0.01f)
            return 1f;
        return Mathf.Sign(transform.position.x - netX);
    }

    private bool TryGetBaseSideSign(out float sideSign)
    {
        sideSign = 0f;
        if (basePosition == null || Mathf.Abs(basePosition.position.x - netX) < 0.01f)
            return false;

        sideSign = Mathf.Sign(basePosition.position.x - netX);
        return true;
    }

    private bool IsOnOwnSide(Vector3 point, float tolerance)
    {
        float sideSign = GetAISideSign();
        return sideSign * (point.x - netX) > -Mathf.Max(0f, tolerance);
    }

    private void RecordActualOwnSideBounce(Rigidbody bouncedBall)
    {
        int shotKey = GetBallShotKey(bouncedBall);
        if (shotKey != observedOwnSideBounceShotKey)
        {
            observedOwnSideBounceShotKey = shotKey;
            observedOwnSideBounceCount = 0;
        }

        BallController controller = bouncedBall != null ? bouncedBall.GetComponent<BallController>() : null;
        int liveCount = controller != null ? controller.CourtBouncesSinceLastHit : 0;
        observedOwnSideBounceCount = Mathf.Max(observedOwnSideBounceCount + 1, liveCount);
    }

    private int GetKnownOwnSideBounceCount()
    {
        if (ball == null)
            return 0;

        int shotKey = GetBallShotKey(ball);
        int observedCount = shotKey == observedOwnSideBounceShotKey ? observedOwnSideBounceCount : 0;
        BallController controller = ball.GetComponent<BallController>();
        int liveCount = controller != null && IsOnOwnSide(ball.position, 0.05f)
            ? controller.CourtBouncesSinceLastHit
            : 0;
        return Mathf.Max(observedCount, liveCount);
    }

    private bool IsInsideLegalAISideCourt(Vector3 point)
    {
        if (!HasAIBounds())
            return IsOnOwnSide(point, 0f);

        Vector3 fl = GetBoundPosition(aiSideFL);
        Vector3 fr = GetBoundPosition(aiSideFR);
        Vector3 bl = GetBoundPosition(aiSideBL);
        Vector3 br = GetBoundPosition(aiSideBR);
        float tolerance = Mathf.Max(0f, legalBounceBoundaryTolerance);
        float minX = Mathf.Min(fl.x, fr.x, bl.x, br.x) - tolerance;
        float maxX = Mathf.Max(fl.x, fr.x, bl.x, br.x) + tolerance;
        float minZ = Mathf.Min(fl.z, fr.z, bl.z, br.z) - tolerance;
        float maxZ = Mathf.Max(fl.z, fr.z, bl.z, br.z) + tolerance;
        return point.x >= minX && point.x <= maxX && point.z >= minZ && point.z <= maxZ;
    }

    private static int GetBallShotKey(Rigidbody body)
    {
        if (body == null)
            return -1;

        BallController controller = body.GetComponent<BallController>();
        int shotSequence = controller != null ? controller.ShotSequence : 0;
        return (body.GetInstanceID() * 397) ^ shotSequence;
    }

    private bool HasAIBounds()
    {
        return aiSideFL != null && aiSideFR != null && aiSideBL != null && aiSideBR != null;
    }

    private Vector3 GetFrontCenter()
    {
        return (GetBoundPosition(aiSideFL) + GetBoundPosition(aiSideFR)) * 0.5f;
    }

    private Vector3 GetBackCenter()
    {
        return (GetBoundPosition(aiSideBL) + GetBoundPosition(aiSideBR)) * 0.5f;
    }

    private Vector3 GetTacticalBaselineReference()
    {
        Vector3 reference = HasAIBounds()
            ? GetBackCenter()
            : hasRuntimeBasePosition
                ? runtimeBasePosition
                : transform.position;
        reference.y = transform.position.y;
        return reference;
    }

    private Vector3 GetBasePositionFromBounds()
    {
        Vector3 backCenter = GetBackCenter();
        Vector3 frontCenter = GetFrontCenter();
        Vector3 backDirection = backCenter - frontCenter;
        backDirection.y = 0f;
        if (backDirection.sqrMagnitude <= 0.0001f)
            backDirection = GetAISideSign() > 0f ? Vector3.right : Vector3.left;
        else
            backDirection.Normalize();

        Vector3 basePoint = backCenter + backDirection * Mathf.Max(0f, baseBehindBackLine);
        basePoint.y = transform.position.y;
        return ClampToMovementBounds(basePoint);
    }

    private Vector3 ClampToMovementBounds(Vector3 point)
    {
        if (!HasAIBounds())
            return point;

        Vector3 fl = GetBoundPosition(aiSideFL);
        Vector3 fr = GetBoundPosition(aiSideFR);
        Vector3 bl = GetBoundPosition(aiSideBL);
        Vector3 br = GetBoundPosition(aiSideBR);
        float minX = Mathf.Min(fl.x, fr.x, bl.x, br.x);
        float maxX = Mathf.Max(fl.x, fr.x, bl.x, br.x);
        float minZ = Mathf.Min(fl.z, fr.z, bl.z, br.z) - Mathf.Max(0f, sideBoundPadding);
        float maxZ = Mathf.Max(fl.z, fr.z, bl.z, br.z) + Mathf.Max(0f, sideBoundPadding);

        if (GetBackCenter().x < GetFrontCenter().x)
            minX -= Mathf.Max(8f, backBoundPadding);
        else
            maxX += Mathf.Max(8f, backBoundPadding);

        point.x = Mathf.Clamp(point.x, minX, maxX);
        point.z = Mathf.Clamp(point.z, minZ, maxZ);
        return point;
    }

    private Vector3 GetBoundPosition(Transform bound)
    {
        Vector3 position = bound.position;
        if (!ShouldMirrorBoundsToBaseSide())
            return position;

        float baseSign;
        if (!TryGetBaseSideSign(out baseSign))
            return position;

        float rawBoundsCenterX = (aiSideFL.position.x + aiSideFR.position.x + aiSideBL.position.x + aiSideBR.position.x) * 0.25f;
        if (Mathf.Abs(rawBoundsCenterX - netX) < 0.01f || Mathf.Sign(rawBoundsCenterX - netX) == baseSign)
            return position;

        position.x = (2f * netX) - position.x;
        return position;
    }

    private bool ShouldMirrorBoundsToBaseSide()
    {
        if (!mirrorBoundsToBaseSide || basePosition == null || !HasAIBounds())
            return false;

        float baseSign;
        if (!TryGetBaseSideSign(out baseSign))
            return false;

        float rawBoundsCenterX = (aiSideFL.position.x + aiSideFR.position.x + aiSideBL.position.x + aiSideBR.position.x) * 0.25f;
        return Mathf.Abs(rawBoundsCenterX - netX) >= 0.01f && Mathf.Sign(rawBoundsCenterX - netX) != baseSign;
    }

    private bool IsInsideMovementBounds(Vector3 point)
    {
        if (!HasAIBounds())
            return true;

        Vector3 clamped = ClampToMovementBounds(point);
        return Mathf.Abs(clamped.x - point.x) < 0.01f && Mathf.Abs(clamped.z - point.z) < 0.01f;
    }

    private static Transform FindTransform(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
            return null;

        GameObject found = GameObject.Find(objectName);
        return found != null ? found.transform : null;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private void ChangeState(AIState newState, string reason)
    {
        if (state == newState)
            return;

        if (debugLogs)
            Debug.Log($"[TennisAI] {state} -> {newState}: {reason}");

        state = newState;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Vector3 zoneCenter = transform.position + transform.rotation *
            (tightHitZoneLocalOffset + Vector3.forward * Mathf.Max(0f, tightHitZoneForwardBias));
        Quaternion zoneRotation = transform.rotation;
        Vector3 zoneRadii = tightHitZoneRadii;
        if (hitController != null)
            hitController.TryGetAuthoritativeContactZonePose(out zoneCenter, out zoneRotation, out zoneRadii);
        Gizmos.matrix = Matrix4x4.TRS(zoneCenter, zoneRotation, zoneRadii * 2f);
        Gizmos.DrawWireSphere(Vector3.zero, 0.5f);
        Gizmos.matrix = Matrix4x4.identity;

        if (showTacticalInterceptOptionsGizmo && currentPlan.valid)
        {
            DrawTacticalOptionGizmo(latestBaselineOption, baselineOptionColor);
            DrawTacticalOptionGizmo(latestRetreatOption, retreatOptionColor);
            DrawTacticalOptionGizmo(latestStepInOption, stepInOptionColor);
            DrawTacticalOptionGizmo(latestVolleyOption, volleyOptionColor);
        }

        if (currentPlan.valid)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(currentPlan.contactPoint, 0.08f);
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(currentPlan.stancePoint, 0.18f);

            if (showInterceptPlanGizmo)
            {
                // Magenta: root route to the stance that places the tight hit
                // zone on the ball. Orange: the ball interception itself.
                Vector3 routeStart = transform.position + Vector3.up * 0.06f;
                Vector3 stanceMarker = currentPlan.stancePoint + Vector3.up * 0.06f;
                Vector3 contactMarker = currentPlan.contactPoint;

                Gizmos.color = interceptPlanRouteColor;
                Gizmos.DrawLine(routeStart, stanceMarker);
                DrawGizmoArrowHead(routeStart, stanceMarker, 0.24f);
                Gizmos.DrawWireSphere(stanceMarker, 0.24f);

                Gizmos.color = interceptPlanContactColor;
                Gizmos.DrawWireSphere(contactMarker, 0.15f);
                Gizmos.DrawLine(stanceMarker, contactMarker);
            }
        }

        Gizmos.color = Color.white;
        Vector3 basePoint = basePosition != null ? basePosition.position : (hasRuntimeBasePosition ? runtimeBasePosition : transform.position);
        Gizmos.DrawWireSphere(basePoint, 0.25f);
    }

    private void DrawTacticalOptionGizmo(ContactPlan option, Color color)
    {
        if (!option.valid)
            return;
        if (currentPlan.valid &&
            HorizontalDistance(option.stancePoint, currentPlan.stancePoint) < 0.05f &&
            HorizontalDistance(option.contactPoint, currentPlan.contactPoint) < 0.05f)
        {
            return;
        }

        Vector3 routeStart = transform.position + Vector3.up * 0.035f;
        Vector3 stanceMarker = option.stancePoint + Vector3.up * 0.035f;
        Gizmos.color = color;
        Gizmos.DrawLine(routeStart, stanceMarker);
        Gizmos.DrawWireSphere(stanceMarker, 0.13f);
        Gizmos.DrawWireSphere(option.contactPoint, 0.08f);
        Gizmos.DrawLine(stanceMarker, option.contactPoint);
    }

    private static void DrawGizmoArrowHead(Vector3 start, Vector3 end, float size)
    {
        Vector3 direction = end - start;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        direction.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, direction);
        Vector3 back = end - direction * Mathf.Max(0.01f, size);
        Gizmos.DrawLine(end, back + side * size * 0.55f);
        Gizmos.DrawLine(end, back - side * size * 0.55f);
    }

    private void OnDrawGizmos()
    {
        if (!showSwipeStatusGizmo)
            return;

        Gizmos.color = GetSwipeStatusColor(swipeStatusGizmoState);
        Vector3 center = transform.position + Vector3.up * Mathf.Max(0.1f, swipeStatusGizmoHeight);
        float radius = Mathf.Max(0.05f, swipeStatusGizmoRadius);
        const int segments = 28;
        Vector3 previous = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector3 next = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }
}
