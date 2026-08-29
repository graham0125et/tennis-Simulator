using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class UIWorldReticle : MonoBehaviour
{
    [Header("Bounds")]
    public Transform minBound;
    public Transform maxBound;

    [Header("Aim Plane Mask")]
    public LayerMask aimMask;

    [Header("Movement Settings")]
    public float followSmoothTime = 0.02f;

    [HideInInspector]
    public bool reticleLocked = false;

    [Header("Input Sampling")]
    public bool useSharedInputSampler = true;
    public bool unlockWhenMouseReleased = true;
    public InputDirectionSampler120Hz inputSampler;

    public Vector3 basePosition; // where reticle sits before backswing

    [Header("Swing Zone Lock")]
    public bool lockWhenBallInSwingZone = false;
    public KeyCode swingZoneLockOverrideKey = KeyCode.Space;

    private bool swingZoneLockActive;
    private Vector3 followVelocity = Vector3.zero;
    private Vector3 followTarget;
    private Vector3 lastValidHit;

    public Vector2 AimScreenDirection => GetInputSampler() != null ? GetInputSampler().LatestDirection : Vector2.zero;
    public Vector2 AimScreenVelocity => GetInputSampler() != null ? GetInputSampler().LatestVelocity : Vector2.zero;
    public float AimSpeedCentimetresPerSecond => GetInputSampler() != null ? GetInputSampler().LatestSpeedCentimetresPerSecond : 0f;
    public InputDirectionSampler120Hz.Sample LatestAimSample => GetInputSampler() != null ? GetInputSampler().LatestSample : default(InputDirectionSampler120Hz.Sample);

    void Awake()
    {
        if (useSharedInputSampler)
            inputSampler = InputDirectionSampler120Hz.EnsureExists();
    }

    void LateUpdate()
    {
        // ------------------------------------------------------------
        // FREE RETICLE MOVEMENT (NEW SYSTEM)
        //
        // The reticle now ALWAYS follows the mouse unless it is locked.
        // There is no MMB aiming mode anymore.
        //
        // This matches the new tennis stroke model:
        //   • Player moves mouse to aim freely
        //   • When backswing begins, reticle locks
        //   • Forward swipe uses the locked aim
        //
        // So the logic is:
        //   If reticleLocked == false → follow mouse
        //   If reticleLocked == true  → stay still (but clamp)
        // ------------------------------------------------------------

        bool lockedBySwingZone = lockWhenBallInSwingZone && swingZoneLockActive && !IsSwingZoneLockOverrideHeld();
        if (lockedBySwingZone)
            reticleLocked = true;

        if (reticleLocked && !lockedBySwingZone && unlockWhenMouseReleased && !IsAnySwipeMouseButtonHeld())
        {
            reticleLocked = false;
            followVelocity = Vector3.zero;
        }

        if (reticleLocked)
        {
            // Reticle is frozen in place, but still clamped to bounds
            ClampToBounds();
            return;
        }

        // ------------------------------------------------------------
        // RETICLE FOLLOWS MOUSE (UNLOCKED)
        // ------------------------------------------------------------
        Vector3 hitPos;

        if (TryGetMouseWorldPosition(out hitPos))
            lastValidHit = hitPos;

        followTarget = lastValidHit;
        followTarget.y = transform.position.y; // keep reticle on same plane

        if (followSmoothTime <= 0f)
        {
            transform.position = followTarget;
        }
        else
        {
            transform.position = Vector3.SmoothDamp(
                transform.position,
                followTarget,
                ref followVelocity,
                followSmoothTime
            );
        }

        ClampToBounds();
    }

    public void SetSwingZoneLockActive(bool active)
    {
        swingZoneLockActive = active;
    }

    public void ReleaseAndResetFollow()
    {
        reticleLocked = false;
        swingZoneLockActive = false;
        followVelocity = Vector3.zero;
        followTarget = transform.position;
        lastValidHit = transform.position;
    }

    private bool IsSwingZoneLockOverrideHeld()
    {
        if (swingZoneLockOverrideKey == KeyCode.None)
            return false;

        return Input.GetKey(swingZoneLockOverrideKey);
    }

    bool TryGetMouseWorldPosition(out Vector3 hitPoint)
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            hitPoint = Vector3.zero;
            return false;
        }

        Ray ray = mainCamera.ScreenPointToRay(GetSampledScreenPosition());

        if (Physics.Raycast(ray, out RaycastHit hit, 500f, aimMask))
        {
            hitPoint = hit.point;
            return true;
        }

        hitPoint = Vector3.zero;
        return false;
    }

    private InputDirectionSampler120Hz GetInputSampler()
    {
        if (!useSharedInputSampler)
            return null;

        if (inputSampler == null)
            inputSampler = InputDirectionSampler120Hz.EnsureExists();

        return inputSampler;
    }

    private Vector2 GetSampledScreenPosition()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
            return Mouse.current.position.ReadValue();
#endif
        Vector3 mouse = Input.mousePosition;
        return new Vector2(mouse.x, mouse.y);
    }

    private bool IsAnySwipeMouseButtonHeld()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
            return Mouse.current.leftButton.isPressed || Mouse.current.rightButton.isPressed;
#endif
        return Input.GetMouseButton(0) || Input.GetMouseButton(1);
    }

    void ClampToBounds()
    {
        Vector3 pos = transform.position;

        pos.x = Mathf.Clamp(pos.x, minBound.position.x, maxBound.position.x);
        pos.z = Mathf.Clamp(pos.z, minBound.position.z, maxBound.position.z);

        transform.position = pos;
    }
}
