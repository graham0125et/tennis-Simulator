using UnityEngine;

public class ShotDirectionCalculator : MonoBehaviour
{
    [Header("Error Tuning")]
    public float maxAngleError = 8f;          // degrees at 0 quality
    public float lateralErrorScale = 1f;      // world units lateral drift at 0 quality
    public float depthErrorScale = 0.15f;     // fraction of speed at 0 quality
    public float heightErrorScale = 2f;       // degrees at 0 quality
    public float netErrorPenalty = 3f;        // degrees downward at 0 quality

    // ----------------------------------------------------------------------
    // DETERMINISTIC ERROR MODEL
    // ----------------------------------------------------------------------
    // All error is computed from quality alone.
    // qualityLost = 1 - quality (0 = perfect shot, 1 = worst shot)
    //
    // Angular error:   0.8 degrees per 10% quality lost
    // Lateral drift:   0.05 world units per 10% quality lost
    // Depth error:     1.5% speed per 10% quality lost
    // Height error:    0.2 degrees per 10% quality lost
    // Net penalty:     scales from 0 at quality=1 to full at quality=0
    //
    // Aim blending now lives in hitController. This component only keeps
    // standalone error helpers for future shot-error tuning.
    // ----------------------------------------------------------------------

    public Vector3 ApplyDirectionError(Vector3 dir, float shotQuality)
    {
        dir = dir.normalized;

        float qualityLost = 1f - Mathf.Clamp01(shotQuality);
        float errorAngle = maxAngleError * qualityLost;

        // Sign from direction itself — shots pulling right get right error
        float sign = Mathf.Sign(dir.x);

        Quaternion rot = Quaternion.Euler(0f, errorAngle * sign, 0f);
        return (rot * dir).normalized;
    }

    public float ApplyDepthError(float speed, float shotQuality)
    {
        float qualityLost = 1f - Mathf.Clamp01(shotQuality);
        // 1.5% speed reduction per 10% quality lost, always short not long
        float depthError = depthErrorScale * qualityLost;
        return speed * (1f - depthError);
    }

    public float ApplyHeightError(float angle, float shotQuality)
    {
        float qualityLost = 1f - Mathf.Clamp01(shotQuality);

        // 0.2 degrees lower per 10% quality lost
        float heightError = heightErrorScale * qualityLost;
        float finalAngle = angle - heightError;

        // Net penalty: scales smoothly, no random threshold
        // At quality 0 full penalty, at quality 1 zero penalty
        float penalty = netErrorPenalty * qualityLost * qualityLost;
        finalAngle -= penalty;

        return finalAngle;
    }
}
