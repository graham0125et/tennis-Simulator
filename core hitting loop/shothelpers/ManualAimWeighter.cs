using UnityEngine;

public static class ManualAimWeighter
{
    public static Vector3 Apply(
        Vector3 toReticle,
        Vector3 playerAim
    )
    {
        // Compute angle difference
        float angleDiff = Vector3.SignedAngle(toReticle, playerAim, Vector3.up);
        float absA = Mathf.Abs(angleDiff);

        // Thresholds
        const float a1 = 1f, a2 = 5f;
        const float w1 = 1f, w2 = 0.4f;

        float w;

        if (absA <= a1)
            w = w1;
        else if (absA >= a2)
            w = w2;
        else
        {
            float t = (absA - a1) / (a2 - a1);
            w = Mathf.Lerp(w1, w2, t);
        }

        // Blend reticle direction with player aim
        return Vector3.Slerp(toReticle, playerAim, w).normalized;
    }
}
