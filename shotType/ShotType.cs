using UnityEngine;

public enum BaseShotType
{
    Flat,
    Topspin,
    Slice,
    Lob,
    Drop
}

public enum ShotModifier
{
    Normal,
    Fast,
    Slow
}

public struct ShotIntent
{
    public float speedMultiplier;   // Fast = >1, Slow = <1
    public float angleOffsetDeg;    // Legacy/fallback launch angle offset.
    public float spinRpm;           // Positive = topspin, negative = slice/backspin.
}

public struct ShotContactProfile
{
    public float defaultSpinRpm;
    public float maxCustomSpinRpm;
    public float baseRetention;
    public float heavyRetention;
    public float reboundCoefficient;
    public float paceFloorScale;
    public float minRetainedContactV0;

    public ShotContactProfile(
        float defaultSpinRpm,
        float maxCustomSpinRpm,
        float baseRetention,
        float heavyRetention,
        float reboundCoefficient,
        float paceFloorScale,
        float minRetainedContactV0 = 0f)
    {
        this.defaultSpinRpm = defaultSpinRpm;
        this.maxCustomSpinRpm = maxCustomSpinRpm;
        this.baseRetention = baseRetention;
        this.heavyRetention = heavyRetention;
        this.reboundCoefficient = reboundCoefficient;
        this.paceFloorScale = paceFloorScale;
        this.minRetainedContactV0 = minRetainedContactV0;
    }

    public float SpinRpm(float spinIntent)
    {
        return Mathf.Lerp(defaultSpinRpm, maxCustomSpinRpm, Mathf.Clamp01(spinIntent));
    }

    public float ContactRetention(float spinIntent)
    {
        return Mathf.Lerp(baseRetention, heavyRetention, Mathf.Clamp01(spinIntent));
    }
}
public struct ShotHeightRange
{
    public float lowerOffsetDeg;
    public float fallbackDefaultAngleDeg;
    public float upperOffsetDeg;
    public float maxNetClearance;

    public ShotHeightRange(
        float lowerOffsetDeg,
        float fallbackDefaultAngleDeg,
        float upperOffsetDeg,
        float maxNetClearance)
    {
        this.lowerOffsetDeg = lowerOffsetDeg;
        this.fallbackDefaultAngleDeg = fallbackDefaultAngleDeg;
        this.upperOffsetDeg = upperOffsetDeg;
        this.maxNetClearance = maxNetClearance;
    }

    public float Evaluate(float normalizedIntent, float defaultAngleDeg)
    {
        float safeDefault = float.IsFinite(defaultAngleDeg) ? defaultAngleDeg : fallbackDefaultAngleDeg;
        float intent = Mathf.Clamp01(normalizedIntent);

        if (intent <= 0.5f)
            return Mathf.Lerp(safeDefault + lowerOffsetDeg, safeDefault, intent / 0.5f);

        return Mathf.Lerp(safeDefault, safeDefault + upperOffsetDeg, (intent - 0.5f) / 0.5f);
    }

    public float MinAngleDeg(float defaultAngleDeg)
    {
        float safeDefault = float.IsFinite(defaultAngleDeg) ? defaultAngleDeg : fallbackDefaultAngleDeg;
        return safeDefault + lowerOffsetDeg;
    }

    public float MaxAngleDeg(float defaultAngleDeg)
    {
        float safeDefault = float.IsFinite(defaultAngleDeg) ? defaultAngleDeg : fallbackDefaultAngleDeg;
        return safeDefault + upperOffsetDeg;
    }
}
