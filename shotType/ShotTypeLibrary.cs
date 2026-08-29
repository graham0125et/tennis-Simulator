using UnityEngine;

public static class BaseShotLibrary
{
    public const float DefaultHeightIntent = 0.5f;
    public const float BaselineNoBackswingSpeedMps = 26.8224f; // 60 mph
    public const float RallyMaxSpeedMps = 35.7632f;            // 80 mph
    public static float HeightIntent = DefaultHeightIntent;
    public static float HeightIntentScrollStep = 0.05f;

    public static float GetBackswingCapSpeed(float backswingScale)
    {
        return Mathf.Lerp(
            BaselineNoBackswingSpeedMps,
            RallyMaxSpeedMps,
            Mathf.Clamp01(backswingScale));
    }

    public static void ResetHeightIntent()
    {
        HeightIntent = DefaultHeightIntent;
    }

    public static void AddHeightIntentScroll(float scrollSteps)
    {
        if (!float.IsFinite(scrollSteps) || Mathf.Approximately(scrollSteps, 0f))
            return;

        HeightIntent = Mathf.Clamp01(HeightIntent + scrollSteps * HeightIntentScrollStep);
    }

    public static ShotHeightRange GetHeightRange(BaseShotType type)
    {
        switch (type)
        {
            case BaseShotType.Flat:
                return new ShotHeightRange(-10f, 14f, 6f, 1.45f);

            case BaseShotType.Topspin:
                return new ShotHeightRange(-20f, 10f, 8f, 1.60f);

            case BaseShotType.Slice:
                return new ShotHeightRange(-12f, 14f, 5f, 1.25f);

            case BaseShotType.Lob:
                return new ShotHeightRange(-8f, 32f, 10f, 4.00f);

            case BaseShotType.Drop:
                return new ShotHeightRange(-5f, 16f, 8f, 1.80f);

            default:
                return new ShotHeightRange(-10f, 14f, 6f, 1.45f);
        }
    }

    public static ShotHeightRange GetClearanceDrivenHeightRange(
        BaseShotType type,
        float contactHeight,
        float netHeight,
        float distanceToNet,
        float netClearance)
    {
        ShotHeightRange range = GetHeightRange(type);
        if (type == BaseShotType.Lob)
            return range;

        if (!float.IsFinite(contactHeight) ||
            !float.IsFinite(netHeight) ||
            !float.IsFinite(distanceToNet) ||
            !float.IsFinite(netClearance) ||
            distanceToNet < 0.05f)
        {
            return range;
        }

        float targetNetY = netHeight + Mathf.Max(0.05f, netClearance);
        float dynamicDefaultAngleDeg = Mathf.Atan2(
            targetNetY - contactHeight,
            distanceToNet
        ) * Mathf.Rad2Deg;

        if (float.IsFinite(dynamicDefaultAngleDeg))
            range.fallbackDefaultAngleDeg = dynamicDefaultAngleDeg;

        return range;
    }

    public static float GetFallbackDefaultHeightAngleDeg(BaseShotType type)
    {
        return GetHeightRange(type).fallbackDefaultAngleDeg;
    }

    public static float GetHeightAngleDeg(BaseShotType type)
    {
        return GetHeightAngleDeg(type, HeightIntent, GetFallbackDefaultHeightAngleDeg(type));
    }

    public static float GetHeightAngleDeg(BaseShotType type, float heightIntent)
    {
        return GetHeightAngleDeg(type, heightIntent, GetFallbackDefaultHeightAngleDeg(type));
    }

    public static float GetHeightAngleDeg(BaseShotType type, float heightIntent, float defaultAngleDeg)
    {
        return GetHeightRange(type).Evaluate(heightIntent, defaultAngleDeg);
    }

    public static float RpmToRadPerSecond(float rpm)
    {
        return rpm * Mathf.PI * 2f / 60f;
    }

    public static float RadPerSecondToRpm(float radPerSecond)
    {
        return radPerSecond * 60f / (Mathf.PI * 2f);
    }

    public static ShotContactProfile GetContactProfile(BaseShotType type)
    {
        switch (type)
        {
            case BaseShotType.Flat:
                return new ShotContactProfile(0f, 600f, 1.00f, 0.96f, 0.40f, 0.90f, BaselineNoBackswingSpeedMps);

            case BaseShotType.Topspin:
                return new ShotContactProfile(2500f, 4000f, 0.90f, 0.75f, 0.32f, 0.80f, BaselineNoBackswingSpeedMps);

            case BaseShotType.Slice:
                return new ShotContactProfile(-1800f, -3200f, 0.80f, 0.60f, 0.30f, 0.65f, BaselineNoBackswingSpeedMps);

            case BaseShotType.Lob:
                return new ShotContactProfile(900f, 1800f, 0.88f, 0.72f, 0.22f, 0.55f, 18f);

            case BaseShotType.Drop:
                return new ShotContactProfile(-1200f, -2200f, 0.70f, 0.45f, 0.08f, 0.20f, 8f);

            default:
                return new ShotContactProfile(0f, 600f, 1.00f, 0.96f, 0.40f, 0.90f, BaselineNoBackswingSpeedMps);
        }
    }

    public static float GetSpinRpm(BaseShotType type, float spinIntent)
    {
        return GetContactProfile(type).SpinRpm(spinIntent);
    }

    public static float GetContactRetention(BaseShotType type, float spinIntent)
    {
        return GetContactProfile(type).ContactRetention(spinIntent);
    }

    public static float GetReboundCoefficient(BaseShotType type)
    {
        return GetContactProfile(type).reboundCoefficient;
    }

    public static float GetPaceFloorScale(BaseShotType type)
    {
        return GetContactProfile(type).paceFloorScale;
    }

    public static float GetIncomingSpinCarryRate(BaseShotType type, float spinIntent)
    {
        return Mathf.Lerp(0.30f, 0.10f, Mathf.Clamp01(spinIntent));
    }
    public static float GetHeightSpinMultiplier(float contactHeight)
    {
        float hNorm = Mathf.InverseLerp(0.3f, 1.5f, contactHeight);
        return Mathf.Lerp(0.4f, 1.3f, hNorm);
    }

    public static ShotIntent Get(BaseShotType type)
    {
        switch (type)
        {
            case BaseShotType.Flat:
                return new ShotIntent
                {
                    speedMultiplier = 1f,
                    angleOffsetDeg = -2f,
                    spinRpm = 0f
                };

            case BaseShotType.Topspin:
                return new ShotIntent
                {
                    speedMultiplier = 1f,
                    angleOffsetDeg = +4f,
                    spinRpm = 2500f
                };

            case BaseShotType.Slice:
                return new ShotIntent
                {
                    speedMultiplier = 1f,
                    angleOffsetDeg = +2f,
                    spinRpm = -1800f
                };

            case BaseShotType.Lob:
                return new ShotIntent
                {
                    speedMultiplier = 1f,
                    angleOffsetDeg = +18f,
                    spinRpm = 900f
                };

            case BaseShotType.Drop:
                return new ShotIntent
                {
                    speedMultiplier = 1f,
                    angleOffsetDeg = +12f,
                    spinRpm = -1200f
                };

            default:
                return new ShotIntent
                {
                    speedMultiplier = 1f,
                    angleOffsetDeg = 0f,
                    spinRpm = 0f
                };
        }
    }
}

public static class ModifierLibrary
{
    public static ShotIntent Get(ShotModifier mod)
    {
        switch (mod)
        {
            case ShotModifier.Fast:
                return new ShotIntent { speedMultiplier = 1f, angleOffsetDeg = -3f, spinRpm = 0f };

            case ShotModifier.Slow:
                return new ShotIntent { speedMultiplier = 1f, angleOffsetDeg = +6f, spinRpm = 0f };

            default:
                return new ShotIntent { speedMultiplier = 1f, angleOffsetDeg = 0f, spinRpm = 0f };
        }
    }
}
