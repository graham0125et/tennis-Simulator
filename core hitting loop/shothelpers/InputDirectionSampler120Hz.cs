using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif

[DisallowMultipleComponent]
public class InputDirectionSampler120Hz : MonoBehaviour
{
    private const string RuntimeObjectName = "InputDirectionSampler120Hz";
    private const float CentimetresPerInch = 2.54f;

    public struct Sample
    {
        public int sequence;
        public float timestamp;
        public float deltaTime;
        public Vector2 position;
        public Vector2 delta;
        public Vector2 velocity;
        public float deltaCentimetres;
        public float speedCentimetresPerSecond;
        public Vector2 direction;
        public bool isInputSystemEvent;
        public bool usedInputSystemDelta;
        public bool rejectedAsSpike;
    }

    public struct SampleStats
    {
        public int totalSamples;
        public int movingSamples;
        public int inputEventSamples;
        public int fallbackSamples;
        public int zeroMoveSamples;
        public int deltaDrivenSamples;
        public int positionDrivenSamples;
        public int rejectedSpikeSamples;
        public float durationSeconds;
        public float averageSampleHz;
        public float distanceCentimetres;
        public float averageMovingDeltaPixels;
        public float maxDeltaPixels;
        public float averageMovingSpeedCentimetresPerSecond;
        public float maxSpeedCentimetresPerSecond;
    }

    public struct StillCompletionInfo
    {
        public bool completed;
        public int completionSequence;
        public float completionTimestamp;
        public int lastMovingSequence;
        public float lastMovingTimestamp;
        public int latestSequence;
        public float latestTimestamp;
        public float stillDurationSeconds;
    }

    [Header("Sampling")]
    public float sampleRateHz = 120f;
    public int maxStoredSamples = 1200;
    public float minDirectionDeltaPixels = 0.001f;
    public int maxResampledCatchUpPerFrame = 120;

    [Header("Input Source")]
    public bool useInputSystemEventBuffer = true;
    public bool disableInputSystemEventMerging = true;
    public bool useResampledFallbackWhenNoEvents = true;
    public float maxInputEventDeltaPixels = 250f;

    [Header("Virtual Cursor Wrapping")]
    public bool enableVirtualCursorWrapping = true;
    public int cursorWrapMarginPixels = 6;
    public bool restoreCursorStateWhenWrappingEnds = true;

    private static InputDirectionSampler120Hz instance;

    private readonly List<Sample> samples = new List<Sample>(1200);
    private Vector2 lastRawPosition;
    private Vector2 lastPhysicalPosition;
    private float lastRawTimestamp;
    private Vector2 lastSamplePosition;
    private float lastSampleTimestamp;
    private float nextSampleTimestamp;
    private Vector2 virtualPosition;
    private bool virtualCursorWrappingActive;
    private bool suppressNextWarpSpike;
    private CursorLockMode previousCursorLockState;
    private bool previousCursorVisible;
    private bool receivedInputSystemEvent;
    private int nextSequence;
    private float dpi = 96f;
    private bool leftButtonHeld;
    private bool rightButtonHeld;
    private float lastLeftButtonPressTime = -999f;
    private float lastRightButtonPressTime = -999f;
    private int lastLeftButtonPressSequence = -1;
    private int lastRightButtonPressSequence = -1;

#if ENABLE_INPUT_SYSTEM
    private bool previousDisableRedundantEventsMerging;
#endif

    public static InputDirectionSampler120Hz Instance => instance;
    public float Dpi => dpi;
    public int LatestSequence => nextSequence - 1;
    public Vector2 CurrentPosition => samples.Count > 0 ? samples[samples.Count - 1].position : lastSamplePosition;
    public bool VirtualCursorWrappingActive => virtualCursorWrappingActive;
    public Vector2 LatestDirection => samples.Count > 0 ? samples[samples.Count - 1].direction : Vector2.zero;
    public Vector2 LatestVelocity => samples.Count > 0 ? samples[samples.Count - 1].velocity : Vector2.zero;
    public float LatestSpeedCentimetresPerSecond => samples.Count > 0 ? samples[samples.Count - 1].speedCentimetresPerSecond : 0f;
    public Sample LatestSample => samples.Count > 0 ? samples[samples.Count - 1] : default(Sample);
    public bool LeftButtonHeld => leftButtonHeld;
    public bool RightButtonHeld => rightButtonHeld;
    public bool AnyButtonHeld => leftButtonHeld || rightButtonHeld;
    public int LastLeftButtonPressSequence => lastLeftButtonPressSequence;
    public int LastRightButtonPressSequence => lastRightButtonPressSequence;

    public static InputDirectionSampler120Hz EnsureExists()
    {
        if (instance != null)
            return instance;

        instance = FindFirstObjectByType<InputDirectionSampler120Hz>();
        if (instance != null)
            return instance;

        GameObject samplerObject = new GameObject(RuntimeObjectName);
        DontDestroyOnLoad(samplerObject);
        return samplerObject.AddComponent<InputDirectionSampler120Hz>();
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            enabled = false;
            Destroy(this);
            return;
        }

        instance = this;
        dpi = Screen.dpi > 0f ? Screen.dpi : 96f;
        lastRawPosition = ReadMousePosition();
        lastPhysicalPosition = ReadPhysicalMousePosition();
        virtualPosition = lastRawPosition;
        lastRawTimestamp = Time.unscaledTime;
        lastSamplePosition = lastRawPosition;
        lastSampleTimestamp = lastRawTimestamp;
        nextSampleTimestamp = lastRawTimestamp + SampleInterval;
        leftButtonHeld = Input.GetMouseButton(0);
        rightButtonHeld = Input.GetMouseButton(1);

        CaptureSample(lastSamplePosition, lastSampleTimestamp, false, false, false);
    }

    void OnEnable()
    {
#if ENABLE_INPUT_SYSTEM
        if (useInputSystemEventBuffer)
        {
            if (InputSystem.settings != null)
            {
                previousDisableRedundantEventsMerging = InputSystem.settings.disableRedundantEventsMerging;
                if (disableInputSystemEventMerging)
                    InputSystem.settings.disableRedundantEventsMerging = true;
            }

            InputSystem.onEvent += OnInputSystemEvent;
        }
#endif
    }

    void OnDisable()
    {
        SetVirtualCursorWrapping(false);

#if ENABLE_INPUT_SYSTEM
        if (useInputSystemEventBuffer)
        {
            InputSystem.onEvent -= OnInputSystemEvent;

            if (InputSystem.settings != null && disableInputSystemEventMerging)
                InputSystem.settings.disableRedundantEventsMerging = previousDisableRedundantEventsMerging;
        }
#endif
    }

    void Update()
    {
        UpdateButtonStateFromPolling(Time.unscaledTime);
        if (Input.GetKeyDown(KeyCode.Escape))
            SetVirtualCursorWrapping(false);

        WrapPhysicalCursorIfNeeded();

        if (!UsingInputSystemEvents() || (useResampledFallbackWhenNoEvents && !receivedInputSystemEvent))
            ResampleMousePosition();
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
            SetVirtualCursorWrapping(false);
    }

    public void SetVirtualCursorWrapping(bool active)
    {
        active = active && enableVirtualCursorWrapping;

        if (virtualCursorWrappingActive == active)
            return;

        virtualCursorWrappingActive = active;

        if (active)
        {
            previousCursorLockState = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.Confined;
            Cursor.visible = true;

            Vector2 physical = ReadPhysicalMousePosition();
            lastPhysicalPosition = physical;
            virtualPosition = lastSamplePosition;
            lastRawPosition = virtualPosition;
            lastRawTimestamp = Time.unscaledTime;
            return;
        }

        if (restoreCursorStateWhenWrappingEnds)
        {
            Cursor.lockState = previousCursorLockState;
            Cursor.visible = previousCursorVisible;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        Vector2 current = ReadPhysicalMousePosition();
        lastPhysicalPosition = current;
        virtualPosition = current;
        lastRawPosition = current;
        lastSamplePosition = current;
        lastRawTimestamp = Time.unscaledTime;
        lastSampleTimestamp = lastRawTimestamp;
        nextSampleTimestamp = lastRawTimestamp + SampleInterval;
        CaptureSample(current, lastRawTimestamp, false, false, false);
    }

    private bool UsingInputSystemEvents()
    {
#if ENABLE_INPUT_SYSTEM
        return useInputSystemEventBuffer;
#else
        return false;
#endif
    }

#if ENABLE_INPUT_SYSTEM
    private void OnInputSystemEvent(InputEventPtr eventPtr, InputDevice device)
    {
        if (!useInputSystemEventBuffer)
            return;

        Mouse mouse = device as Mouse;
        if (mouse == null)
            return;

        bool hasPosition = mouse.position.ReadValueFromEvent(eventPtr, out Vector2 eventPosition);
        bool hasDelta = mouse.delta.ReadValueFromEvent(eventPtr, out Vector2 delta);
        bool hasLeftButton = mouse.leftButton.ReadValueFromEvent(eventPtr, out float leftValue);
        bool hasRightButton = mouse.rightButton.ReadValueFromEvent(eventPtr, out float rightValue);

        if (!hasPosition && !hasDelta && !hasLeftButton && !hasRightButton)
            return;

        Vector2 position = hasPosition ? eventPosition : lastSamplePosition;
        float maxDeltaPixels = Mathf.Max(1f, maxInputEventDeltaPixels);
        bool usedDelta = hasDelta && delta.sqrMagnitude > 0.000001f;
        bool rejectedSpike = usedDelta && delta.magnitude > maxDeltaPixels;

        if (virtualCursorWrappingActive)
        {
            if (suppressNextWarpSpike && usedDelta && delta.magnitude > maxDeltaPixels)
            {
                suppressNextWarpSpike = false;
                position = lastSamplePosition;
                rejectedSpike = true;
            }
            else if (rejectedSpike)
            {
                position = lastSamplePosition;
            }
            else if (usedDelta)
            {
                suppressNextWarpSpike = false;
                virtualPosition += delta;
                position = virtualPosition;
            }
            else if (hasPosition)
            {
                suppressNextWarpSpike = false;
                Vector2 physicalDelta = eventPosition - lastPhysicalPosition;
                if (physicalDelta.magnitude <= maxDeltaPixels)
                    virtualPosition += physicalDelta;

                position = virtualPosition;
            }
            else
            {
                position = virtualPosition;
            }

            if (hasPosition)
                lastPhysicalPosition = eventPosition;
        }
        else if (rejectedSpike)
            position = lastSamplePosition;
        else if (usedDelta)
            position = lastSamplePosition + delta;
        else if (!hasPosition)
            position = lastSamplePosition;
        else if ((position - lastSamplePosition).magnitude > maxDeltaPixels)
        {
            position = lastSamplePosition;
            rejectedSpike = true;
        }

        float timestamp = (float)eventPtr.time;
        if (timestamp <= 0f)
            timestamp = Time.unscaledTime;

        bool eventLeftHeld = hasLeftButton ? leftValue > 0.5f : leftButtonHeld;
        bool eventRightHeld = hasRightButton ? rightValue > 0.5f : rightButtonHeld;
        UpdateButtonState(eventLeftHeld, eventRightHeld, timestamp);

        bool buttonOnlyEvent = !hasPosition && !hasDelta && (hasLeftButton || hasRightButton);
        if (!buttonOnlyEvent &&
            !rejectedSpike &&
            hasDelta &&
            delta.sqrMagnitude <= 0.000001f &&
            (position - lastSamplePosition).sqrMagnitude <= 0.000001f)
        {
            return;
        }

        CaptureSample(position, timestamp, true, usedDelta, rejectedSpike);

        if (virtualCursorWrappingActive)
            virtualPosition = position;

        lastRawPosition = position;
        lastRawTimestamp = timestamp;
        nextSampleTimestamp = timestamp + SampleInterval;
        receivedInputSystemEvent = true;
    }
#endif

    private float SampleInterval => 1f / Mathf.Max(1f, sampleRateHz);

    public bool WasLeftButtonPressedRecently(float seconds)
    {
        return Time.unscaledTime - lastLeftButtonPressTime <= Mathf.Max(0f, seconds);
    }

    public bool WasRightButtonPressedRecently(float seconds)
    {
        return Time.unscaledTime - lastRightButtonPressTime <= Mathf.Max(0f, seconds);
    }

    public bool WasAnyButtonPressedRecently(float seconds)
    {
        return WasLeftButtonPressedRecently(seconds) || WasRightButtonPressedRecently(seconds);
    }

    private void UpdateButtonStateFromPolling(float timestamp)
    {
        UpdateButtonState(Input.GetMouseButton(0), Input.GetMouseButton(1), timestamp);
    }

    private void UpdateButtonState(bool newLeftHeld, bool newRightHeld, float timestamp)
    {
        if (newLeftHeld && !leftButtonHeld)
        {
            lastLeftButtonPressTime = timestamp;
            lastLeftButtonPressSequence = LatestSequence;
        }

        if (newRightHeld && !rightButtonHeld)
        {
            lastRightButtonPressTime = timestamp;
            lastRightButtonPressSequence = LatestSequence;
        }

        leftButtonHeld = newLeftHeld;
        rightButtonHeld = newRightHeld;
    }

    private void ResampleMousePosition()
    {
        Vector2 currentPosition = ReadMousePosition();
        float now = Time.unscaledTime;
        float rawDeltaTime = now - lastRawTimestamp;

        if (rawDeltaTime <= 1e-6f)
            return;

        int generated = 0;
        int maxCatchUp = Mathf.Max(1, maxResampledCatchUpPerFrame);

        while (nextSampleTimestamp <= now && generated < maxCatchUp)
        {
            float t = Mathf.InverseLerp(lastRawTimestamp, now, nextSampleTimestamp);
            Vector2 resampledPosition = Vector2.LerpUnclamped(lastRawPosition, currentPosition, t);

            CaptureSample(resampledPosition, nextSampleTimestamp, false, false, false);
            nextSampleTimestamp += SampleInterval;
            generated++;
        }

        if (nextSampleTimestamp < now)
        {
            CaptureSample(currentPosition, now, false, false, false);
            nextSampleTimestamp = now + SampleInterval;
        }

        lastRawPosition = currentPosition;
        lastRawTimestamp = now;
    }

    private void CaptureSample(
        Vector2 currentPosition,
        float now,
        bool isInputSystemEvent,
        bool usedInputSystemDelta,
        bool rejectedAsSpike)
    {
        float dt = Mathf.Max(now - lastSampleTimestamp, 1e-6f);
        Vector2 delta = currentPosition - lastSamplePosition;
        float deltaPixels = delta.magnitude;
        float deltaCm = PixelsToCentimetres(deltaPixels);
        float minDirectionDeltaSqr = minDirectionDeltaPixels * minDirectionDeltaPixels;

        Sample sample = new Sample
        {
            sequence = nextSequence++,
            timestamp = now,
            deltaTime = dt,
            position = currentPosition,
            delta = delta,
            velocity = delta / dt,
            deltaCentimetres = deltaCm,
            speedCentimetresPerSecond = deltaCm / dt,
            direction = delta.sqrMagnitude > minDirectionDeltaSqr ? delta.normalized : Vector2.zero,
            isInputSystemEvent = isInputSystemEvent,
            usedInputSystemDelta = usedInputSystemDelta,
            rejectedAsSpike = rejectedAsSpike
        };

        samples.Add(sample);
        TrimHistory();

        lastSamplePosition = currentPosition;
        lastSampleTimestamp = now;
    }

    private void TrimHistory()
    {
        int safeMax = Mathf.Max(16, maxStoredSamples);
        int overflow = samples.Count - safeMax;

        if (overflow > 0)
            samples.RemoveRange(0, overflow);
    }

    private Vector2 ReadMousePosition()
    {
        if (virtualCursorWrappingActive)
            return ReadVirtualMousePositionFromPhysicalDelta();

        return ReadPhysicalMousePosition();
    }

    private Vector2 ReadPhysicalMousePosition()
    {
        Vector3 mouse = Input.mousePosition;
        return new Vector2(mouse.x, mouse.y);
    }

    private Vector2 ReadVirtualMousePositionFromPhysicalDelta()
    {
        Vector2 physical = ReadPhysicalMousePosition();
        Vector2 physicalDelta = physical - lastPhysicalPosition;
        float maxDeltaPixels = Mathf.Max(1f, maxInputEventDeltaPixels);

        if (physicalDelta.magnitude <= maxDeltaPixels)
            virtualPosition += physicalDelta;

        lastPhysicalPosition = physical;
        return virtualPosition;
    }

    private void WrapPhysicalCursorIfNeeded()
    {
        if (!virtualCursorWrappingActive)
            return;

#if ENABLE_INPUT_SYSTEM
        Mouse mouse = Mouse.current;
        if (mouse == null)
            return;

        Vector2 physical = mouse.position.ReadValue();
        float width = Mathf.Max(1, Screen.width);
        float height = Mathf.Max(1, Screen.height);
        float margin = Mathf.Clamp(cursorWrapMarginPixels, 1, Mathf.FloorToInt(Mathf.Min(width, height) * 0.25f));
        Vector2 wrapped = physical;

        if (physical.x <= margin)
            wrapped.x = width - margin - 1f;
        else if (physical.x >= width - margin)
            wrapped.x = margin + 1f;

        if (physical.y <= margin)
            wrapped.y = height - margin - 1f;
        else if (physical.y >= height - margin)
            wrapped.y = margin + 1f;

        if ((wrapped - physical).sqrMagnitude <= 0.001f)
            return;

        mouse.WarpCursorPosition(wrapped);
        lastPhysicalPosition = wrapped;
        suppressNextWarpSpike = true;
#endif
    }

    public float PixelsToCentimetres(float pixels)
    {
        return (pixels / Mathf.Max(dpi, 1f)) * CentimetresPerInch;
    }

    public Vector2 PixelsToCentimetres(Vector2 pixels)
    {
        float scale = CentimetresPerInch / Mathf.Max(dpi, 1f);
        return pixels * scale;
    }

    public void CopyTracePointsBetween(
        int sequenceExclusive,
        int sequenceInclusive,
        List<Vector2> pointsCentimetres,
        List<float> speedsCentimetresPerSecond,
        float minDeltaPixels = 0.001f)
    {
        if (pointsCentimetres == null || speedsCentimetresPerSecond == null)
            return;

        pointsCentimetres.Clear();
        speedsCentimetresPerSecond.Clear();

        Vector2 cumulativePixels = Vector2.zero;
        float minDeltaSqr = minDeltaPixels * minDeltaPixels;

        pointsCentimetres.Add(Vector2.zero);
        speedsCentimetresPerSecond.Add(0f);

        for (int i = 0; i < samples.Count; i++)
        {
            Sample sample = samples[i];
            if (sample.sequence <= sequenceExclusive || sample.sequence > sequenceInclusive)
                continue;

            if (sample.delta.sqrMagnitude <= minDeltaSqr)
                continue;

            cumulativePixels += sample.delta;
            pointsCentimetres.Add(PixelsToCentimetres(cumulativePixels));
            speedsCentimetresPerSecond.Add(sample.speedCentimetresPerSecond);
        }
    }

    public float ConsumeDistanceCentimetres(ref int lastConsumedSequence)
    {
        float distance = GetDistanceCentimetresSince(lastConsumedSequence, out int newestSequence);
        lastConsumedSequence = newestSequence;
        return distance;
    }

    public float GetDistanceCentimetresSince(int sequenceExclusive, out int newestSequence)
    {
        newestSequence = LatestSequence;
        float distance = 0f;

        for (int i = 0; i < samples.Count; i++)
        {
            Sample sample = samples[i];
            if (sample.sequence > sequenceExclusive)
                distance += sample.deltaCentimetres;
        }

        return distance;
    }

    public Vector2 GetWeightedDirectionSince(
        int sequenceExclusive,
        out float totalWeight,
        float minDeltaPixels = 0.001f)
    {
        return GetWeightedDirectionBetween(sequenceExclusive, LatestSequence, out totalWeight, minDeltaPixels);
    }

    public Vector2 GetWeightedDirectionBetween(
        int sequenceExclusive,
        int sequenceInclusive,
        out float totalWeight,
        float minDeltaPixels = 0.001f)
    {
        Vector2 weightedDirection = Vector2.zero;
        totalWeight = 0f;
        float minDeltaSqr = minDeltaPixels * minDeltaPixels;

        for (int i = 0; i < samples.Count; i++)
        {
            Sample sample = samples[i];
            if (sample.sequence <= sequenceExclusive || sample.sequence > sequenceInclusive)
                continue;

            if (sample.delta.sqrMagnitude <= minDeltaSqr || sample.direction == Vector2.zero)
                continue;

            float weight = sample.deltaCentimetres;
            weightedDirection += sample.direction * weight;
            totalWeight += weight;
        }

        return totalWeight > 0.001f ? weightedDirection.normalized : Vector2.zero;
    }

    public SampleStats GetSampleStatsBetween(
        int sequenceExclusive,
        int sequenceInclusive,
        float minDeltaPixels = 0.001f)
    {
        SampleStats stats = new SampleStats();
        bool hasFirst = false;
        float firstTimestamp = 0f;
        float lastTimestamp = 0f;
        float minDeltaSqr = minDeltaPixels * minDeltaPixels;
        float movingDeltaPixelsTotal = 0f;
        float movingSpeedTotal = 0f;

        for (int i = 0; i < samples.Count; i++)
        {
            Sample sample = samples[i];
            if (sample.sequence <= sequenceExclusive || sample.sequence > sequenceInclusive)
                continue;

            if (!hasFirst)
            {
                firstTimestamp = sample.timestamp;
                hasFirst = true;
            }

            lastTimestamp = sample.timestamp;
            stats.totalSamples++;
            stats.distanceCentimetres += sample.deltaCentimetres;
            if (sample.isInputSystemEvent)
            {
                stats.inputEventSamples++;
                if (sample.rejectedAsSpike)
                    stats.rejectedSpikeSamples++;
                else if (sample.usedInputSystemDelta)
                    stats.deltaDrivenSamples++;
                else
                    stats.positionDrivenSamples++;
            }
            else
            {
                stats.fallbackSamples++;
            }

            if (sample.delta.sqrMagnitude > minDeltaSqr)
            {
                stats.movingSamples++;
                float deltaPixels = sample.delta.magnitude;
                movingDeltaPixelsTotal += deltaPixels;
                movingSpeedTotal += sample.speedCentimetresPerSecond;

                if (deltaPixels > stats.maxDeltaPixels)
                    stats.maxDeltaPixels = deltaPixels;

                if (sample.speedCentimetresPerSecond > stats.maxSpeedCentimetresPerSecond)
                    stats.maxSpeedCentimetresPerSecond = sample.speedCentimetresPerSecond;
            }
            else
            {
                stats.zeroMoveSamples++;
            }
        }

        stats.durationSeconds = hasFirst ? Mathf.Max(0f, lastTimestamp - firstTimestamp) : 0f;
        stats.averageSampleHz = stats.durationSeconds > 1e-6f && stats.totalSamples > 1
            ? (stats.totalSamples - 1) / stats.durationSeconds
            : 0f;
        stats.averageMovingDeltaPixels = stats.movingSamples > 0
            ? movingDeltaPixelsTotal / stats.movingSamples
            : 0f;
        stats.averageMovingSpeedCentimetresPerSecond = stats.movingSamples > 0
            ? movingSpeedTotal / stats.movingSamples
            : 0f;

        return stats;
    }

    public bool TryGetStillCompletionSince(
        int sequenceExclusive,
        float movementSpeedThresholdCentimetresPerSecond,
        float minDeltaPixels,
        float requiredStillSeconds,
        out StillCompletionInfo info)
    {
        info = new StillCompletionInfo
        {
            completionSequence = -1,
            lastMovingSequence = sequenceExclusive,
            latestSequence = LatestSequence
        };

        float speedThreshold = Mathf.Max(0f, movementSpeedThresholdCentimetresPerSecond);
        float deltaThreshold = Mathf.Max(0f, minDeltaPixels);
        float stillThreshold = Mathf.Max(0f, requiredStillSeconds);
        bool hasSample = false;
        bool hasMovingSample = false;

        for (int i = 0; i < samples.Count; i++)
        {
            Sample sample = samples[i];
            if (sample.sequence <= sequenceExclusive)
                continue;

            hasSample = true;
            info.latestSequence = sample.sequence;
            info.latestTimestamp = sample.timestamp;

            bool moving =
                sample.delta.magnitude > deltaThreshold &&
                sample.speedCentimetresPerSecond > speedThreshold;

            if (moving)
            {
                hasMovingSample = true;
                info.lastMovingSequence = sample.sequence;
                info.lastMovingTimestamp = sample.timestamp;
                continue;
            }

            if (!hasMovingSample)
                continue;

            float stillDuration = sample.timestamp - info.lastMovingTimestamp;
            if (stillDuration >= stillThreshold)
            {
                info.completed = true;
                info.completionSequence = sample.sequence;
                info.completionTimestamp = sample.timestamp;
                info.stillDurationSeconds = stillDuration;
                return true;
            }
        }

        if (hasSample && hasMovingSample)
            info.stillDurationSeconds = Mathf.Max(0f, info.latestTimestamp - info.lastMovingTimestamp);

        return false;
    }

    public bool TryGetWorldPosition(Camera camera, LayerMask mask, out Vector3 hitPoint, float maxDistance = 500f)
    {
        hitPoint = Vector3.zero;

        if (camera == null)
            return false;

        Ray ray = camera.ScreenPointToRay(CurrentPosition);
        if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, mask))
        {
            hitPoint = hit.point;
            return true;
        }

        return false;
    }
}
