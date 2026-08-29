using UnityEngine;

public static class SmallAngleBooster
{
    public static Vector3 Apply(Vector3 lateralDir)
    {
        lateralDir.y = 0f;
        if (lateralDir.sqrMagnitude < 1e-6f)
            return Vector3.right;

        lateralDir.Normalize();

        // Convert direction to an angle around court-forward (+X).
        float angle = Mathf.Atan2(-lateralDir.z, lateralDir.x) * Mathf.Rad2Deg;
        float absA = Mathf.Abs(angle);
        float sign = Mathf.Sign(angle);

        // Thresholds
        const float b1 = 0.6f, b2 = 5f;
        const float k1 = 3f, k2 = 1f;

        float k;

        if (absA <= b1)
            k = k1;
        else if (absA >= b2)
            k = k2;
        else
        {
            float t = (absA - b1) / (b2 - b1);
            k = Mathf.Lerp(k1, k2, t);
        }

        float boostedAngle = absA * k * sign;
        float rad = boostedAngle * Mathf.Deg2Rad;

        return new Vector3(Mathf.Cos(rad), 0f, -Mathf.Sin(rad)).normalized;
    }
}
