using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [Header("Speed")]
    [Tooltip("Top controlled court movement speed in metres/second. Existing scenes may still have the old low value serialized.")]
    public float moveSpeed = 6f;
    public float maxAcceleration = 5f;
    public float deceleration = 9.5f;
    public float accelerationTaperExponent = 1.5f;
    public float inputDeadZone = 0.05f;

    [Header("Input")]
    public bool allowManualInput = true;
    [Tooltip("Use only held WASD/arrow keys for movement. Disable to include Unity's legacy joystick axes.")]
    public bool keyboardMovementOnly = true;
    [Header("First Step Burst")]
    public bool useExplosiveStart = true;
    [Tooltip("Multiplier applied only at very low speed for a sharper first step.")]
    public float firstStepAccelerationMultiplier = 3.25f;
    [Range(0.05f, 0.8f)]
    [Tooltip("Fraction of top speed where the first-step burst has faded back to normal acceleration.")]
    public float firstStepSpeedFraction = 0.32f;
    [Tooltip("Higher values make the burst drop away sooner after the initial step.")]
    public float firstStepCurveExponent = 2.2f;
    [Range(0f, 1f)]
    [Tooltip("Minimum fraction of max acceleration kept near top speed so movement does not feel floaty.")]
    public float sustainedAccelerationFloor = 0.18f;

    [Header("Direction Change")]
    public bool useWrongFooting = true;
    [Range(-1f, 1f)] public float smoothTurnDot = 0.6f;
    [Range(-1f, 1f)] public float wrongFootDot = -0.3f;
    [Range(0f, 1f)] public float lateralSpeedBleed = 0.4f;
    public float replantDelay = 0.12f;
    public float replantReleaseSpeed = 0.3f;
    [Tooltip("Scale a wrong-foot replant by the current speed. A nearly stationary player can reverse almost immediately, while a sprinting player pays the full delay.")]
    public bool scaleReplantDelayWithSpeed = true;
    [Tooltip("Speed at which the full replant delay is applied.")]
    public float fullReplantDelaySpeed = 5.5f;
    [Range(0.05f, 1f)]
    [Tooltip("Fraction of the normal replant delay retained at walking/near-stationary speed.")]
    public float lowSpeedReplantDelayMultiplier = 0.20f;

    [Header("Recovery")]
    public bool enableRecoveryAssist = false;
    public Transform readyPosition;
    public float recoverySpeedMultiplier = 0.75f;
    public float recoverySprintDistance = 4f;
    public float recoveryStopDistance = 0.15f;

    [Header("Bounds")]
    public Transform minBound;
    public Transform maxBound;
    [Tooltip("Extra metres allowed left/right beyond the bound transforms.")]
    public float sideBoundPadding = 3f;
    [Tooltip("Extra metres allowed behind the player baseline. At least 8m is always available; the net-side bound is left unchanged.")]
    public float backBoundPadding = 8f;
    [Tooltip("Current court setup usually has the player back court on the lower X side, with the net toward higher X.")]
    public bool backBoundIsMinX = true;

    [Header("Debug")]
    public bool debugMovement = false;
    [SerializeField] private Vector3 planarVelocity;
    [SerializeField] private float currentSpeed;
    [SerializeField] private bool isReplantingFeet;
    [SerializeField] private float replantTimer;

    [Header("Animation")]
    public Animator movementAnimator;
    public string moveXParameter = "MoveX";
    public string moveZParameter = "MoveZ";
    public string moveSpeedParameter = "MoveSpeed";
    public string moveSpeed01Parameter = "MoveSpeed01";
    public float animationDampTime = 0.08f;
    public bool scaleAnimationPlaybackWithMoveSpeed = true;
    [Range(0.25f, 3f)] public float idleAnimationPlaybackSpeed = 1f;
    [Range(0.25f, 3f)] public float minMovingAnimationPlaybackSpeed = 1f;
    [Range(0.25f, 3f)] public float maxMovingAnimationPlaybackSpeed = 1.45f;
    [Tooltip("How quickly the Animator playback speed catches up to movement speed changes.")]
    public float animationPlaybackResponsiveness = 8f;
    public bool swapAnimationAxes = true;
    public bool invertAnimationX = false;
    public bool invertAnimationZ = false;

    private bool externalMoveActive;
    private bool externalMoveAllowed = true;
    private Vector3 externalMoveDirection;
    private float externalMoveSpeed;

    private int moveXHash;
    private int moveZHash;
    private int moveSpeedHash;
    private int moveSpeed01Hash;

    public Vector3 PlanarVelocity => planarVelocity;
    public float CurrentSpeed => currentSpeed;

    public float GetScaledReplantDelay(float speed, float targetMaxSpeed = 0f)
    {
        float baseDelay = Mathf.Max(0f, replantDelay);
        if (!scaleReplantDelayWithSpeed || baseDelay <= 0f)
            return baseDelay;

        float referenceSpeed = Mathf.Max(
            0.1f,
            fullReplantDelaySpeed > 0f ? fullReplantDelaySpeed : Mathf.Max(moveSpeed, targetMaxSpeed));
        float speed01 = Mathf.InverseLerp(Mathf.Max(0f, replantReleaseSpeed), referenceSpeed, Mathf.Max(0f, speed));
        float multiplier = Mathf.Lerp(
            Mathf.Clamp(lowSpeedReplantDelayMultiplier, 0.05f, 1f),
            1f,
            speed01);
        return baseDelay * multiplier;
    }

    public void SetExternalMove(Vector3 worldDirection, float targetMaxSpeed)
    {
        if (!externalMoveAllowed)
        {
            ClearExternalMove();
            return;
        }

        worldDirection.y = 0f;
        if (worldDirection.sqrMagnitude <= inputDeadZone * inputDeadZone || targetMaxSpeed <= 0.001f)
        {
            ClearExternalMove();
            return;
        }

        externalMoveActive = true;
        externalMoveDirection = worldDirection.normalized;
        externalMoveSpeed = Mathf.Max(0f, targetMaxSpeed);
    }

    public void ClearExternalMove()
    {
        externalMoveActive = false;
        externalMoveDirection = Vector3.zero;
        externalMoveSpeed = 0f;
    }

    public void SetExternalMoveAllowed(bool allowed)
    {
        externalMoveAllowed = allowed;
        if (!allowed)
            ClearExternalMove();
    }

    void Awake()
    {
        if (movementAnimator == null)
            movementAnimator = GetComponentInChildren<Animator>();

        moveXHash = Animator.StringToHash(moveXParameter);
        moveZHash = Animator.StringToHash(moveZParameter);
        moveSpeedHash = Animator.StringToHash(moveSpeedParameter);
        moveSpeed01Hash = Animator.StringToHash(moveSpeed01Parameter);
    }

    void Reset()
    {
        moveSpeed = 6f;
        maxAcceleration = 5f;
        deceleration = 9.5f;
        accelerationTaperExponent = 1.5f;
        useExplosiveStart = true;
        firstStepAccelerationMultiplier = 3.25f;
        firstStepSpeedFraction = 0.32f;
        firstStepCurveExponent = 2.2f;
        sustainedAccelerationFloor = 0.18f;
        recoverySpeedMultiplier = 0.75f;
        recoverySprintDistance = 4f;
        recoveryStopDistance = 0.15f;
        smoothTurnDot = 0.6f;
        wrongFootDot = -0.3f;
        lateralSpeedBleed = 0.4f;
        replantDelay = 0.12f;
        replantReleaseSpeed = 0.3f;
        scaleReplantDelayWithSpeed = true;
        fullReplantDelaySpeed = 5.5f;
        lowSpeedReplantDelayMultiplier = 0.20f;
        sideBoundPadding = 3f;
        backBoundPadding = 8f;
        backBoundIsMinX = true;
        scaleAnimationPlaybackWithMoveSpeed = true;
        idleAnimationPlaybackSpeed = 1f;
        minMovingAnimationPlaybackSpeed = 1f;
        maxMovingAnimationPlaybackSpeed = 1.45f;
        animationPlaybackResponsiveness = 8f;
        swapAnimationAxes = true;
        invertAnimationX = false;
        invertAnimationZ = false;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f)
            return;

        Vector3 desiredDirection = Vector3.zero;
        bool hasManualInput = allowManualInput && TryGetCameraRelativeInput(out desiredDirection);
        bool hasDesiredMove = hasManualInput;
        float targetMaxSpeed = Mathf.Max(0f, moveSpeed);

        if (externalMoveActive)
        {
            desiredDirection = externalMoveDirection;
            targetMaxSpeed = externalMoveSpeed;
            hasDesiredMove = true;
        }
        else if (!hasManualInput && enableRecoveryAssist)
        {
            hasDesiredMove = TryGetRecoveryDirection(out desiredDirection, out targetMaxSpeed);
        }

        UpdatePlanarVelocity(hasDesiredMove, desiredDirection, targetMaxSpeed, dt);
        MoveAndClamp(dt);
        UpdateMovementAnimator(dt);
    }

    private bool TryGetCameraRelativeInput(out Vector3 desiredDirection)
    {
        float h;
        float v;
        if (keyboardMovementOnly)
        {
            h = (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D) ? 1f : 0f) -
                (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A) ? 1f : 0f);
            v = (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W) ? 1f : 0f) -
                (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S) ? 1f : 0f);
        }
        else
        {
            h = Input.GetAxisRaw("Horizontal");
            v = Input.GetAxisRaw("Vertical");
        }
        Vector2 input = new Vector2(h, v);

        if (input.sqrMagnitude <= inputDeadZone * inputDeadZone)
        {
            desiredDirection = Vector3.zero;
            return false;
        }

        input = Vector2.ClampMagnitude(input, 1f);

        Camera mainCamera = Camera.main;
        Vector3 camForward = mainCamera != null ? mainCamera.transform.forward : Vector3.forward;
        Vector3 camRight = mainCamera != null ? mainCamera.transform.right : Vector3.right;
        camForward.y = 0f;
        camRight.y = 0f;
        camForward.Normalize();
        camRight.Normalize();

        desiredDirection = camForward * input.y + camRight * input.x;
        desiredDirection.y = 0f;

        if (desiredDirection.sqrMagnitude <= 0.0001f)
        {
            desiredDirection = Vector3.zero;
            return false;
        }

        desiredDirection.Normalize();
        return true;
    }

    private bool TryGetRecoveryDirection(out Vector3 desiredDirection, out float targetMaxSpeed)
    {
        desiredDirection = Vector3.zero;
        targetMaxSpeed = Mathf.Max(0f, moveSpeed) * Mathf.Clamp01(recoverySpeedMultiplier);

        if (readyPosition == null)
            return false;

        Vector3 toReady = readyPosition.position - transform.position;
        toReady.y = 0f;
        float distance = toReady.magnitude;

        if (distance <= recoveryStopDistance)
            return false;

        desiredDirection = toReady / distance;
        if (distance >= recoverySprintDistance)
            targetMaxSpeed = Mathf.Max(0f, moveSpeed);

        return true;
    }

    private void UpdatePlanarVelocity(bool hasDesiredMove, Vector3 desiredDirection, float targetMaxSpeed, float dt)
    {
        planarVelocity.y = 0f;
        currentSpeed = planarVelocity.magnitude;

        if (isReplantingFeet)
        {
            Brake(dt);
            replantTimer -= dt;

            if (replantTimer <= 0f || currentSpeed <= replantReleaseSpeed)
                isReplantingFeet = false;

            return;
        }

        if (!hasDesiredMove || targetMaxSpeed <= 0.001f)
        {
            Brake(dt);
            return;
        }

        if (currentSpeed > 0.15f && desiredDirection.sqrMagnitude > 0.0001f)
        {
            float dot = Vector3.Dot(planarVelocity / currentSpeed, desiredDirection);

            if (useWrongFooting && dot <= wrongFootDot)
            {
                isReplantingFeet = true;
                replantTimer = GetScaledReplantDelay(currentSpeed, targetMaxSpeed);
                Brake(dt);
                return;
            }

            if (dot < smoothTurnDot)
            {
                float turnPenalty = Mathf.InverseLerp(smoothTurnDot, wrongFootDot, dot);
                float bleed = deceleration * lateralSpeedBleed * turnPenalty * dt;
                planarVelocity = Vector3.MoveTowards(planarVelocity, Vector3.zero, bleed);
                currentSpeed = planarVelocity.magnitude;
            }
        }

        float accel = GetAcceleration(currentSpeed, maxAcceleration, targetMaxSpeed);
        Vector3 targetVelocity = desiredDirection * targetMaxSpeed;
        planarVelocity = Vector3.MoveTowards(planarVelocity, targetVelocity, accel * dt);
        currentSpeed = planarVelocity.magnitude;
    }

    private float GetAcceleration(float speed, float accel, float targetMaxSpeed)
    {
        if (targetMaxSpeed <= 0.001f)
            return 0f;

        float t = Mathf.Clamp01(speed / targetMaxSpeed);
        float taper = 1f - Mathf.Pow(t, Mathf.Max(0.01f, accelerationTaperExponent));
        taper = Mathf.Max(Mathf.Clamp01(sustainedAccelerationFloor), taper);

        float acceleration = Mathf.Max(0f, accel) * taper;
        if (!useExplosiveStart)
            return acceleration;

        float burstRange = Mathf.Max(0.01f, firstStepSpeedFraction);
        float burstT = Mathf.Clamp01(t / burstRange);
        float burstFalloff = 1f - Mathf.Pow(burstT, Mathf.Max(0.01f, firstStepCurveExponent));
        float burstMultiplier = Mathf.Lerp(1f, Mathf.Max(1f, firstStepAccelerationMultiplier), burstFalloff);
        return acceleration * burstMultiplier;
    }

    private void Brake(float dt)
    {
        planarVelocity = Vector3.MoveTowards(planarVelocity, Vector3.zero, Mathf.Max(0f, deceleration) * dt);
        currentSpeed = planarVelocity.magnitude;
    }

    private void MoveAndClamp(float dt)
    {
        Vector3 previous = transform.position;
        Vector3 next = previous + planarVelocity * dt;

        if (minBound != null && maxBound != null)
        {
            float rawNextX = previous.x + planarVelocity.x * dt;
            float rawNextZ = previous.z + planarVelocity.z * dt;

            float minX = Mathf.Min(minBound.position.x, maxBound.position.x);
            float maxX = Mathf.Max(minBound.position.x, maxBound.position.x);
            float minZ = Mathf.Min(minBound.position.z, maxBound.position.z) - Mathf.Max(0f, sideBoundPadding);
            float maxZ = Mathf.Max(minBound.position.z, maxBound.position.z) + Mathf.Max(0f, sideBoundPadding);

            if (backBoundIsMinX)
                minX -= Mathf.Max(8f, backBoundPadding);
            else
                maxX += Mathf.Max(8f, backBoundPadding);

            next.x = Mathf.Clamp(next.x, minX, maxX);
            next.z = Mathf.Clamp(next.z, minZ, maxZ);

            if (!Mathf.Approximately(next.x, rawNextX))
                planarVelocity.x = 0f;

            if (!Mathf.Approximately(next.z, rawNextZ))
                planarVelocity.z = 0f;
        }

        transform.position = next;
        currentSpeed = planarVelocity.magnitude;

        if (debugMovement)
        {
            Debug.DrawRay(transform.position + Vector3.up * 0.1f, planarVelocity, Color.cyan, 0f, false);
        }
    }

    private void UpdateMovementAnimator(float dt)
    {
        if (movementAnimator == null)
            return;

        float maxSpeed = Mathf.Max(0.01f, moveSpeed);
        Vector3 localVelocity = transform.InverseTransformDirection(planarVelocity);
        localVelocity.y = 0f;

        float animX = Mathf.Clamp(localVelocity.x / maxSpeed, -1f, 1f);
        float animZ = Mathf.Clamp(localVelocity.z / maxSpeed, -1f, 1f);

        if (swapAnimationAxes)
        {
            float originalX = animX;
            animX = animZ;
            animZ = originalX;
        }

        if (invertAnimationX)
            animX = -animX;

        if (invertAnimationZ)
            animZ = -animZ;

        if (currentSpeed <= inputDeadZone)
        {
            animX = 0f;
            animZ = 0f;
        }

        float speed01 = Mathf.Clamp01(currentSpeed / maxSpeed);

        movementAnimator.SetFloat(moveXHash, animX, animationDampTime, dt);
        movementAnimator.SetFloat(moveZHash, animZ, animationDampTime, dt);
        movementAnimator.SetFloat(moveSpeedHash, currentSpeed, animationDampTime, dt);
        movementAnimator.SetFloat(moveSpeed01Hash, speed01, animationDampTime, dt);

        UpdateAnimatorPlaybackSpeed(speed01, dt);
    }

    private void UpdateAnimatorPlaybackSpeed(float speed01, float dt)
    {
        if (!scaleAnimationPlaybackWithMoveSpeed)
        {
            movementAnimator.speed = 1f;
            return;
        }

        bool isMoving = currentSpeed > inputDeadZone;
        float targetPlaybackSpeed = isMoving
            ? Mathf.Lerp(minMovingAnimationPlaybackSpeed, maxMovingAnimationPlaybackSpeed, speed01)
            : idleAnimationPlaybackSpeed;

        float responsiveness = Mathf.Max(0f, animationPlaybackResponsiveness);
        if (responsiveness <= 0f)
        {
            movementAnimator.speed = targetPlaybackSpeed;
            return;
        }

        movementAnimator.speed = Mathf.MoveTowards(
            movementAnimator.speed,
            targetPlaybackSpeed,
            responsiveness * dt);
    }
}



