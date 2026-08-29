using UnityEngine;

public static class ShotTypeResolver
{
    public static BaseShotType ResolveBaseType(
        bool isLMB,
        bool isRMB,
        bool swipeForward,
        bool swipeBackward,
        bool shortSwipe,
        UIWorldReticle reticle
    )
    {
        if (isRMB)
        {
            if (swipeBackward)
                return BaseShotType.Slice;

            if (swipeForward)
                return BaseShotType.Topspin;

            return BaseShotType.Topspin;
        }

        if (isLMB)
        {
            if (swipeBackward)
                return BaseShotType.Lob;

            if (swipeForward)
                return BaseShotType.Flat;

            return BaseShotType.Flat;
        }

        return BaseShotType.Flat;
    }

    public static ShotModifier ResolveModifier(float swipeSpeed)
    {
        if (swipeSpeed > 22f)
            return ShotModifier.Fast;

        if (swipeSpeed < 10f)
            return ShotModifier.Slow;

        return ShotModifier.Normal;
    }

    private static bool ReticleInServiceBox(UIWorldReticle reticle)
    {
        if (reticle == null)
            return false;

        return reticle.transform.position.z < 0f;
    }

    public static void UpdateBaseHeightModifier()
    {
        BaseShotLibrary.AddHeightIntentScroll(Input.mouseScrollDelta.y);
    }
}