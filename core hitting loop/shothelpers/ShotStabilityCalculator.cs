using UnityEngine;

public static class ShotStabilityCalculator
{
    public struct StabilityResult
    {
        public Vector3 adjustedAimDir;
        public float guiAngleToReticle;
        public float guiFinalShotAngle;
    }

    public static StabilityResult Compute(
        float normDist,
        float guiAngleToReticle,
        float quality,
        Vector3 toReticle,
        Vector3 blendedAim)
    {
        StabilityResult result = new StabilityResult();

        blendedAim.y = 0f;
        toReticle.y = 0f;

        if (toReticle.sqrMagnitude < 1e-6f)
            toReticle = Vector3.right;

        if (blendedAim.sqrMagnitude < 1e-6f)
            blendedAim = toReticle;

        blendedAim.Normalize();
        toReticle.Normalize();

        Vector3 courtForward = Vector3.right;
        float currentAimAngle = Vector3.SignedAngle(courtForward, blendedAim, Vector3.up);

        // Keep this as an after-effect only: no reticle re-blend, no aim compression.
        float straightnessStability = Mathf.Clamp01(1f - Mathf.Abs(guiAngleToReticle) / 20f);
        float stability =
            normDist * 0.2f +
            straightnessStability * 0.6f +
            quality * 0.2f;

        stability = Mathf.Clamp01(stability);

        float sign = 0f;
        if (guiAngleToReticle > 0.001f)
            sign = 1f;
        else if (guiAngleToReticle < -0.001f)
            sign = -1f;
        else if (currentAimAngle > 0.001f)
            sign = 1f;
        else if (currentAimAngle < -0.001f)
            sign = -1f;

        float maxErr = 1f;
        float err = (1f - stability) * maxErr * sign;
        Vector3 finalDir = (Quaternion.AngleAxis(err, Vector3.up) * blendedAim).normalized;

        result.adjustedAimDir = finalDir;
        result.guiAngleToReticle = guiAngleToReticle;
        result.guiFinalShotAngle = Vector3.SignedAngle(toReticle, finalDir, Vector3.up);

        return result;
    }
}
