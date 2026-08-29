using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class LateralAimReticleGauge : MonoBehaviour
{
    private struct AimSample
    {
        public float angleDeg;
        public bool warning;
    }

    [Header("References")]
    public swipeMouseBall swipeSource;
    public Camera cameraOverride;

    [Header("Toggle")]
    public bool helperEnabled = true;
    public KeyCode toggleKey = KeyCode.L;
    public bool logToggle;

    [Header("Timing")]
    public float finalFreezeSeconds = 0.8f;
    public float fadeSeconds = 0.35f;
    public float sampleIntervalSeconds = 0.012f;

    [Header("Gauge Layout")]
    public float radiusPixels = 30f;
    public float verticalOffsetPixels = 34f;
    public float arcSpanDeg = 180f;
    public int arcSegments = 42;
    [Range(0.25f, 0.9f)] public float collarInnerRadiusFraction = 0.54f;
    public float arcWidthPixels = 2.2f;
    public float needleWidthPixels = 2.4f;
    public float targetLineWidthPixels = 1.8f;
    public float finalLineWidthPixels = 3.2f;
    public float dotWidthPixels = 8.0f;
    public int maxSamples = 56;

    [Header("Visibility")]
    [Range(0f, 1f)] public float idleAlpha = 0f;
    [Range(0f, 1f)] public float armedAlpha = 0.72f;
    [Range(0f, 1f)] public float activeAlpha = 1f;

    [Header("Colours")]
    public Color arcColor = new Color(0.05f, 0.95f, 1f, 0.72f);
    public Color targetLineColor = new Color(0.85f, 1f, 1f, 0.88f);
    public Color liveNeedleColor = new Color(1f, 0.96f, 0.45f, 0.98f);
    public Color oldDotColor = new Color(0.10f, 0.90f, 1f, 0.82f);
    public Color newDotColor = new Color(1f, 0.98f, 0.42f, 1f);
    public Color finalLineColor = new Color(0.42f, 1f, 0.55f, 1f);
    public Color warningColor = new Color(1f, 0.34f, 0.08f, 1f);

    [Header("Control Warnings")]
    [Range(0.1f, 1f)] public float warningAngleFraction = 0.9f;
    public float wobbleWarningDegPerSecond = 260f;

    [Header("Rendering")]
    public Material lineMaterial;
    public int sortingOrder = 25;

    private readonly List<AimSample> samples = new List<AimSample>();
    private readonly List<LineRenderer> dotRenderers = new List<LineRenderer>();
    private LineRenderer arcRenderer;
    private LineRenderer targetRenderer;
    private LineRenderer liveRenderer;
    private LineRenderer finalRenderer;
    private Material runtimeMaterial;
    private bool wasActive;
    private bool wasArmed;
    private bool hasLastAngle;
    private float lastAngleDeg;
    private float lastSampleTime;
    private float nextSampleTime;
    private bool freezeVisible;
    private float finalAngleDeg;
    private float freezeUntilTime;
    private float fadeUntilTime;

    private void Awake()
    {
        EnsureRenderers();
    }

    private void OnEnable()
    {
        hitController.PlayerLateralAimResolved += HandleFinalLateralAimResolved;
    }

    private void OnDisable()
    {
        hitController.PlayerLateralAimResolved -= HandleFinalLateralAimResolved;
    }

    private void Update()
    {
        if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey))
        {
            helperEnabled = !helperEnabled;
            if (logToggle)
                Debug.Log($"[Lateral Aim Gauge] {(helperEnabled ? "ON" : "OFF")}");
        }
    }

    private void LateUpdate()
    {
        EnsureRenderers();

        if (!helperEnabled)
        {
            HideAll();
            return;
        }

        if (swipeSource == null)
            swipeSource = FindFirstObjectByType<swipeMouseBall>();

        if (swipeSource == null || !swipeSource.TryGetLateralAimCourse(out Vector3 origin, out Vector3 reticlePosition, out _))
        {
            HideAll();
            return;
        }

        Camera cam = cameraOverride != null ? cameraOverride : Camera.main;
        if (cam == null)
        {
            HideAll();
            return;
        }

        bool armed = swipeSource.LateralAimSwipeArmed;
        bool active = swipeSource.LateralAimSwipeActive;
        bool finalFreeze = freezeVisible && Time.time < fadeUntilTime;
        bool visible = armed || active || finalFreeze || idleAlpha > 0f;

        if (!visible)
        {
            HideAll();
            return;
        }

        if (armed && !wasArmed && !active)
            ClearSwipeSamples();

        if (active && !wasActive)
            BeginSwipeSamples();

        if (active)
            SampleLiveAim();

        wasActive = active;
        wasArmed = armed;

        float alpha = ResolveAlpha(armed, active, finalFreeze);
        if (alpha <= 0.001f)
        {
            HideAll();
            return;
        }

        GaugeFrame frame = BuildFrame(cam, reticlePosition);
        DrawArc(frame, alpha);
        DrawTargetLine(frame, alpha);
        DrawDots(frame, alpha);
        DrawLiveNeedle(frame, alpha, active);
        DrawFinalNeedle(frame, alpha, finalFreeze);
    }

    private void OnDestroy()
    {
        if (runtimeMaterial != null)
            Destroy(runtimeMaterial);
    }

    private void HandleFinalLateralAimResolved(hitController controller, float angleDeg, Vector3 toReticle, Vector3 finalDir)
    {
        if (swipeSource != null && controller != swipeSource.LateralAimHitController)
            return;

        finalAngleDeg = angleDeg;
        freezeVisible = true;
        freezeUntilTime = Time.time + Mathf.Max(0.01f, finalFreezeSeconds);
        fadeUntilTime = freezeUntilTime + Mathf.Max(0.01f, fadeSeconds);
    }

    private void BeginSwipeSamples()
    {
        ClearSwipeSamples();
        freezeVisible = false;
    }

    private void ClearSwipeSamples()
    {
        samples.Clear();
        hasLastAngle = false;
        lastSampleTime = Time.time;
        nextSampleTime = 0f;
    }

    private void SampleLiveAim()
    {
        if (Time.time < nextSampleTime)
            return;

        if (!swipeSource.TryGetLiveLateralAim(out _, out _, out _, out float angleDeg))
            return;

        float maxAngle = GetMaxAngle();
        bool warning = Mathf.Abs(angleDeg) >= maxAngle * Mathf.Clamp01(warningAngleFraction);

        if (hasLastAngle)
        {
            float dt = Mathf.Max(0.0001f, Time.time - lastSampleTime);
            float angleSpeed = Mathf.Abs(Mathf.DeltaAngle(lastAngleDeg, angleDeg)) / dt;
            warning |= angleSpeed >= Mathf.Max(1f, wobbleWarningDegPerSecond);
        }

        samples.Add(new AimSample { angleDeg = angleDeg, warning = warning });
        while (samples.Count > Mathf.Max(1, maxSamples))
            samples.RemoveAt(0);

        hasLastAngle = true;
        lastAngleDeg = angleDeg;
        lastSampleTime = Time.time;
        nextSampleTime = Time.time + Mathf.Max(0.001f, sampleIntervalSeconds);
    }

    private float ResolveAlpha(bool armed, bool active, bool finalFreeze)
    {
        if (active)
            return activeAlpha;

        if (finalFreeze)
        {
            if (Time.time <= freezeUntilTime)
                return activeAlpha;

            float t = Mathf.InverseLerp(fadeUntilTime, freezeUntilTime, Time.time);
            return Mathf.Clamp01(t) * activeAlpha;
        }

        if (armed)
            return armedAlpha;

        return idleAlpha;
    }

    private struct GaugeFrame
    {
        public Vector3 center;
        public Vector3 right;
        public Vector3 up;
        public float pixelWorld;
        public float radius;
        public float innerRadius;
        public float outerRadius;
        public float dotRadius;
    }

    private GaugeFrame BuildFrame(Camera cam, Vector3 reticlePosition)
    {
        float pixelWorld = GetWorldUnitsPerPixel(cam, reticlePosition);
        float radius = Mathf.Max(8f, radiusPixels) * pixelWorld;
        Vector3 up = cam.transform.up.normalized;
        Vector3 right = cam.transform.right.normalized;

        GaugeFrame frame = new GaugeFrame
        {
            center = reticlePosition + up * (verticalOffsetPixels * pixelWorld),
            right = right,
            up = up,
            pixelWorld = pixelWorld,
            radius = radius,
            innerRadius = radius * Mathf.Clamp(collarInnerRadiusFraction, 0.25f, 0.9f),
            outerRadius = radius,
            dotRadius = Mathf.Lerp(radius * Mathf.Clamp(collarInnerRadiusFraction, 0.25f, 0.9f), radius, 0.52f)
        };

        return frame;
    }

    private float GetWorldUnitsPerPixel(Camera cam, Vector3 worldPos)
    {
        int screenHeight = Mathf.Max(1, Screen.height);
        if (cam.orthographic)
            return (cam.orthographicSize * 2f) / screenHeight;

        float distance = Vector3.Dot(worldPos - cam.transform.position, cam.transform.forward);
        distance = Mathf.Max(0.1f, distance);
        return 2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / screenHeight;
    }

    private void DrawArc(GaugeFrame frame, float alpha)
    {
        int segments = Mathf.Clamp(arcSegments, 10, 96);
        arcRenderer.loop = false;
        int outerCount = segments + 1;
        int innerCount = segments + 1;
        arcRenderer.positionCount = outerCount + 1 + innerCount + 1;
        ApplyLineStyle(arcRenderer, arcWidthPixels, WithAlpha(arcColor, alpha), frame.pixelWorld);

        int index = 0;
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float angle = Mathf.Lerp(GetArcLeftDeg(), GetArcRightDeg(), t);
            arcRenderer.SetPosition(index++, PointOnArc(frame, angle, frame.outerRadius));
        }

        arcRenderer.SetPosition(index++, PointOnArc(frame, GetArcRightDeg(), frame.innerRadius));

        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float angle = Mathf.Lerp(GetArcRightDeg(), GetArcLeftDeg(), t);
            arcRenderer.SetPosition(index++, PointOnArc(frame, angle, frame.innerRadius));
        }

        arcRenderer.SetPosition(index, PointOnArc(frame, GetArcLeftDeg(), frame.outerRadius));
        arcRenderer.enabled = true;
    }

    private void DrawTargetLine(GaugeFrame frame, float alpha)
    {
        DrawRadialLine(targetRenderer, frame, 0f, targetLineWidthPixels, WithAlpha(targetLineColor, alpha));
    }

    private void DrawLiveNeedle(GaugeFrame frame, float alpha, bool active)
    {
        if (!active || !swipeSource.TryGetLiveLateralAim(out _, out _, out _, out float angleDeg))
        {
            liveRenderer.enabled = false;
            return;
        }

        bool warning = Mathf.Abs(angleDeg) >= GetMaxAngle() * Mathf.Clamp01(warningAngleFraction);
        Color color = warning ? warningColor : liveNeedleColor;
        DrawRadialLine(liveRenderer, frame, angleDeg, needleWidthPixels, WithAlpha(color, alpha));
    }

    private void DrawFinalNeedle(GaugeFrame frame, float alpha, bool finalFreeze)
    {
        if (!finalFreeze)
        {
            finalRenderer.enabled = false;
            return;
        }

        DrawRadialLine(finalRenderer, frame, finalAngleDeg, finalLineWidthPixels, WithAlpha(finalLineColor, alpha));
    }

    private void DrawRadialLine(LineRenderer renderer, GaugeFrame frame, float angleDeg, float widthPixels, Color color)
    {
        float visualAngle = AimAngleToArcAngle(angleDeg);
        renderer.loop = false;
        renderer.positionCount = 2;
        ApplyLineStyle(renderer, widthPixels, color, frame.pixelWorld);
        renderer.SetPosition(0, PointOnArc(frame, visualAngle, frame.innerRadius));
        renderer.SetPosition(1, PointOnArc(frame, visualAngle, frame.outerRadius));
        renderer.enabled = true;
    }

    private void DrawDots(GaugeFrame frame, float alpha)
    {
        EnsureDotPool(Mathf.Max(0, maxSamples));

        int count = samples.Count;
        float dotDiameterPixels = Mathf.Max(3f, dotWidthPixels);
        float margin = dotDiameterPixels * frame.pixelWorld * 0.75f;
        float inner = Mathf.Min(frame.outerRadius, frame.innerRadius + margin);
        float outer = Mathf.Max(inner, frame.outerRadius - margin);

        for (int i = 0; i < dotRenderers.Count; i++)
        {
            LineRenderer dot = dotRenderers[i];
            if (i >= count)
            {
                dot.enabled = false;
                continue;
            }

            AimSample sample = samples[i];
            float ageT = count <= 1 ? 1f : i / (float)(count - 1);
            Color color = sample.warning ? warningColor : Color.Lerp(oldDotColor, newDotColor, ageT);
            color = WithAlpha(color, alpha);

            float arcAngle = AimAngleToArcAngle(sample.angleDeg);
            float radialT = Mathf.Lerp(0.18f, 0.86f, ageT);
            float sampleRadius = Mathf.Lerp(inner, outer, radialT);
            Vector3 point = PointOnArc(frame, arcAngle, sampleRadius);

            DrawDotBlob(dot, frame, point, dotDiameterPixels, color);
        }
    }

    private void DrawDotBlob(LineRenderer dot, GaugeFrame frame, Vector3 center, float diameterPixels, Color color)
    {
        int segments = 8;
        float radius = diameterPixels * frame.pixelWorld * 0.48f;

        dot.loop = true;
        dot.positionCount = segments;
        ApplyLineStyle(dot, diameterPixels * 0.52f, color, frame.pixelWorld);

        for (int i = 0; i < segments; i++)
        {
            float angle = i / (float)segments * Mathf.PI * 2f;
            Vector3 offset = frame.right * (Mathf.Cos(angle) * radius) + frame.up * (Mathf.Sin(angle) * radius);
            dot.SetPosition(i, center + offset);
        }

        dot.enabled = true;
    }

    private float AimAngleToArcAngle(float aimAngleDeg)
    {
        float maxAngle = GetMaxAngle();
        float clamped = Mathf.Clamp(aimAngleDeg, -maxAngle, maxAngle);
        float t = Mathf.InverseLerp(-maxAngle, maxAngle, clamped);
        return Mathf.Lerp(GetArcLeftDeg(), GetArcRightDeg(), t);
    }

    private float GetMaxAngle()
    {
        if (swipeSource != null)
            return Mathf.Max(1f, swipeSource.LateralAimMaxAngleDeg);

        return 30f;
    }

    private float GetArcLeftDeg()
    {
        return 90f + Mathf.Abs(arcSpanDeg) * 0.5f;
    }

    private float GetArcRightDeg()
    {
        return 90f - Mathf.Abs(arcSpanDeg) * 0.5f;
    }

    private Vector3 PointOnArc(GaugeFrame frame, float visualAngleDeg, float radius)
    {
        float rad = visualAngleDeg * Mathf.Deg2Rad;
        return frame.center + frame.right * (Mathf.Cos(rad) * radius) + frame.up * (Mathf.Sin(rad) * radius);
    }

    private Color WithAlpha(Color color, float alpha)
    {
        color.a *= Mathf.Clamp01(alpha);
        return color;
    }

    private void EnsureRenderers()
    {
        arcRenderer = EnsureRenderer(arcRenderer, "Arc", 2);
        targetRenderer = EnsureRenderer(targetRenderer, "Target Direction", 3);
        liveRenderer = EnsureRenderer(liveRenderer, "Live Direction", 5);
        finalRenderer = EnsureRenderer(finalRenderer, "Final Direction", 6);
        EnsureDotPool(Mathf.Max(0, maxSamples));
    }

    private LineRenderer EnsureRenderer(LineRenderer renderer, string objectName, int orderOffset)
    {
        if (renderer != null)
            return renderer;

        GameObject lineObject = new GameObject(objectName);
        lineObject.transform.SetParent(transform, false);
        renderer = lineObject.AddComponent<LineRenderer>();
        renderer.useWorldSpace = true;
        renderer.numCapVertices = 4;
        renderer.numCornerVertices = 4;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.textureMode = LineTextureMode.Stretch;
        renderer.alignment = LineAlignment.View;
        renderer.sortingOrder = sortingOrder + orderOffset;
        renderer.sharedMaterial = GetLineMaterial();
        renderer.enabled = false;
        return renderer;
    }

    private void EnsureDotPool(int desiredCount)
    {
        while (dotRenderers.Count < desiredCount)
        {
            LineRenderer dot = EnsureRenderer(null, "Aim Dot " + dotRenderers.Count, 4);
            dotRenderers.Add(dot);
        }
    }

    private void ApplyLineStyle(LineRenderer renderer, float widthPixels, Color color, float pixelWorld)
    {
        if (renderer == null)
            return;

        float width = Mathf.Max(0.5f, widthPixels) * Mathf.Max(0.0001f, pixelWorld);
        renderer.startWidth = width;
        renderer.endWidth = width;
        renderer.startColor = color;
        renderer.endColor = color;
        renderer.sharedMaterial = lineMaterial != null ? lineMaterial : GetLineMaterial();
    }
    private Material GetLineMaterial()
    {
        if (lineMaterial != null)
            return lineMaterial;

        if (runtimeMaterial != null)
            return runtimeMaterial;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("HDRP/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Transparent");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            return null;

        runtimeMaterial = new Material(shader)
        {
            name = "Runtime Lateral Aim Gauge Material"
        };

        if (runtimeMaterial.HasProperty("_BaseColor"))
            runtimeMaterial.SetColor("_BaseColor", Color.white);
        if (runtimeMaterial.HasProperty("_Color"))
            runtimeMaterial.SetColor("_Color", Color.white);

        return runtimeMaterial;
    }

    private void HideAll()
    {
        wasActive = false;
        wasArmed = false;

        SetRendererVisible(arcRenderer, false);
        SetRendererVisible(targetRenderer, false);
        SetRendererVisible(liveRenderer, false);
        SetRendererVisible(finalRenderer, false);

        for (int i = 0; i < dotRenderers.Count; i++)
            SetRendererVisible(dotRenderers[i], false);
    }

    private void SetRendererVisible(LineRenderer renderer, bool visible)
    {
        if (renderer != null && renderer.enabled != visible)
            renderer.enabled = visible;
    }
}