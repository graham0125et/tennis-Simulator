using UnityEngine;

/// <summary>
/// Optional, deliberately separate behaviour informed by observed WTA
/// baseline patterns. Attach it to the active AI player and toggle either the
/// component or Apply WTA Matchplay Logic to compare it with ordinary AI play.
/// It does not replace the existing matchplay tactic and solver systems.
/// </summary>
[DisallowMultipleComponent]
public sealed class WtaMatchplayObservationLogic : MonoBehaviour
{
    [Header("Enable")]
    public bool applyWtaMatchplayLogic = true;
    public bool debugLogs = false;

    [Header("Big first-serve return")]
    [Tooltip("A first serve at or above this pace uses a compact, retained-pace block return.")]
    public float bigFirstServeMph = 110f;
    [Tooltip("Short virtual swipe used for the compact return.")]
    [Range(0.02f, 0.12f)] public float compactReturnSwipeDuration = 0.055f;
    [Tooltip("Caps the forward-swing fraction for a compact rather than late full return.")]
    [Range(0f, 1f)] public float compactReturnForwardSwingProgress = 0.86f;
    [Range(0f, 1f)] public float compactReturnQualityFloor = 0.76f;

    [Header("Early and high-ball contact")]
    [Tooltip("Fast, post-bounce balls in the rising band are preferred for an early compact contact.")]
    public float earlyRisingMinimumIncomingSpeedMps = 24f;
    public Vector2 earlyRisingContactHeightRange = new Vector2(0.60f, 1.35f);
    [Tooltip("Negative plan score added to a first post-bounce rising contact in the preferred band.")]
    [Range(0f, 2f)] public float earlyRisingPlanScoreBonus = 0.55f;
    [Tooltip("At this post-bounce contact height, the AI may favour a shaped topspin reply.")]
    public float highBallShapeMinimumHeight = 1.65f;
    [Range(0f, 1f)] public float highBallTopspinChance = 0.70f;
    [Range(0f, 1f)] public float highBallTopspinIntentFloor = 0.72f;

    [Header("Recovery and court position")]
    [Tooltip("A contact this far from the neutral ready point is treated as wide and recovers more aggressively.")]
    public float wideContactDistanceFromNeutral = 3.25f;
    [Tooltip("Speed multiplier while recovering from a wide contact. Existing hit-settle timing remains in charge.")]
    [Range(1f, 1.5f)] public float wideRecoverySpeedMultiplier = 1.15f;
    [Tooltip("Extra depth behind the ordinary neutral ready position for a defensive recovery. With a 1m-behind-baseline base this gives roughly 2.5m.")]
    public float defensiveExtraBehindNeutralReady = 1.5f;
    [Tooltip("Advance from the ordinary neutral ready position on a genuine offensive recovery. With a 1m-behind-baseline base this gives about 1m inside the baseline.")]
    public float offensiveAdvanceFromNeutralReady = 2f;

    public bool IsActive => isActiveAndEnabled && applyWtaMatchplayLogic;

    public bool IsBigFirstServe(float speedMps)
    {
        return speedMps >= Mathf.Max(0f, bigFirstServeMph) * 0.44704f;
    }

    public float GetContactPlanScoreAdjustment(
        float incomingSpeedMps,
        int ownSideBounceCount,
        float contactHeight,
        float verticalVelocity)
    {
        if (ownSideBounceCount != 1 || verticalVelocity <= 0f ||
            incomingSpeedMps < Mathf.Max(0f, earlyRisingMinimumIncomingSpeedMps))
        {
            return 0f;
        }

        float minHeight = Mathf.Min(earlyRisingContactHeightRange.x, earlyRisingContactHeightRange.y);
        float maxHeight = Mathf.Max(earlyRisingContactHeightRange.x, earlyRisingContactHeightRange.y);
        return contactHeight >= minHeight && contactHeight <= maxHeight
            ? -Mathf.Max(0f, earlyRisingPlanScoreBonus)
            : 0f;
    }

    public bool ShouldUseHighBallTopspin(int ownSideBounceCount, float contactHeight, bool baselineVolley)
    {
        return ownSideBounceCount > 0 && !baselineVolley &&
            contactHeight >= Mathf.Max(0f, highBallShapeMinimumHeight) &&
            Random.value <= Mathf.Clamp01(highBallTopspinChance);
    }

    public bool IsWideContact(Vector3 contactPoint, Vector3 neutralReadyPoint)
    {
        return Mathf.Abs(contactPoint.z - neutralReadyPoint.z) >= Mathf.Max(0f, wideContactDistanceFromNeutral);
    }

    public float GetRecoverySpeedMultiplier(bool wideContact)
    {
        return wideContact ? Mathf.Max(1f, wideRecoverySpeedMultiplier) : 1f;
    }

    public Vector3 GetRecoveryTarget(
        Vector3 neutralReadyPoint,
        float courtSideSign,
        bool defensiveRecovery,
        bool offensiveRecovery)
    {
        float side = Mathf.Abs(courtSideSign) > 0.001f ? Mathf.Sign(courtSideSign) : 1f;
        Vector3 target = neutralReadyPoint;
        if (defensiveRecovery)
            target.x += side * Mathf.Max(0f, defensiveExtraBehindNeutralReady);
        else if (offensiveRecovery)
            target.x -= side * Mathf.Max(0f, offensiveAdvanceFromNeutralReady);
        return target;
    }
}
