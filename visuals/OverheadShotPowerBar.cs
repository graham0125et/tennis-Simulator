using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(hitController))]
public class OverheadShotPowerBar : MonoBehaviour
{
    [Header("References")]
    public hitController hitController;
    public swipeMouseBall swipeSource;
    public TennisAIPlayerController aiSource;
    public Camera targetCamera;

    [Header("Layout")]
    public Vector3 worldOffset = new Vector3(0f, 2.25f, 0f);
    public float barWidth = 0.75f;
    public float barThickness = 0.045f;
    public float markerHeight = 0.09f;
    public float markerWidth = 0.08f;
    public float timingMarkerSize = 0.11f;
    public float timingMarkerYOffset = -0.18f;
    public int sortingOrder = 80;

    [Header("Power Scale")]
    public float displayMaxPowerMps = 36f;
    public float lastShotDisplaySeconds = 4f;

    [Header("Colors")]
    public Color backgroundColor = new Color(0f, 0f, 0f, 0.42f);
    public Color emptyColor = new Color(0.12f, 0.16f, 0.18f, 0.74f);
    public Color lowPowerColor = new Color(0.18f, 0.75f, 1f, 0.92f);
    public Color highPowerColor = new Color(1f, 0.9f, 0.22f, 0.96f);
    public Color manualMarkerColor = new Color(0.2f, 0.95f, 1f, 0.95f);
    public Color blendedMarkerColor = new Color(0.65f, 1f, 0.35f, 0.98f);
    public Color timingIdleColor = new Color(1f, 1f, 1f, 0.25f);
    public Color timingReadyColor = new Color(0.1f, 1f, 0.45f, 0.95f);

    private LineRenderer backgroundRenderer;
    private LineRenderer emptyRenderer;
    private LineRenderer fillRenderer;
    private LineRenderer manualMarkerRenderer;
    private LineRenderer blendedMarkerRenderer;
    private LineRenderer timingRenderer;
    private Material lineMaterial;

    private void Awake()
    {
        EnsureReferences();
        EnsureRenderers();
    }

    private void LateUpdate()
    {
        EnsureReferences();
        EnsureRenderers();

        if (hitController == null)
        {
            SetAllVisible(false);
            return;
        }

        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null)
        {
            SetAllVisible(false);
            return;
        }

        Vector3 center = transform.position + worldOffset;
        if (cam.WorldToViewportPoint(center).z <= 0f)
        {
            SetAllVisible(false);
            return;
        }

        SetAllVisible(true);

        Vector3 right = cam.transform.right.normalized;
        Vector3 up = cam.transform.up.normalized;
        Vector3 left = center - right * (barWidth * 0.5f);
        Vector3 fullRight = center + right * (barWidth * 0.5f);

        DrawLine(backgroundRenderer, left, fullRight, backgroundColor, barThickness * 1.8f);
        DrawLine(emptyRenderer, left, fullRight, emptyColor, barThickness);

        float backswing01 = GetLiveBackswing01();
        Vector3 fillEnd = Vector3.Lerp(left, fullRight, backswing01);
        Color fillColor = Color.Lerp(lowPowerColor, highPowerColor, Mathf.Clamp01(backswing01));
        DrawLine(fillRenderer, left, fillEnd, fillColor, barThickness);
        fillRenderer.enabled = backswing01 > 0.01f;

        DrawLastShotMarkers(left, right, up);
        DrawTimingMarker(center + up * timingMarkerYOffset, right, up);
    }

    private void EnsureReferences()
    {
        if (hitController == null)
            hitController = GetComponent<hitController>();

        if (swipeSource == null && hitController != null)
            swipeSource = hitController.swipe;

        if (swipeSource == null)
            swipeSource = GetComponent<swipeMouseBall>();

        if (aiSource == null)
            aiSource = GetComponent<TennisAIPlayerController>();

        if (targetCamera == null)
            targetCamera = Camera.main;
    }

    private float GetLiveBackswing01()
    {
        if (aiSource != null)
            return Mathf.Clamp01(aiSource.CurrentVisualBackswingScale);

        return swipeSource != null ? Mathf.Clamp01(swipeSource.backswingScale) : 0f;
    }

    private void DrawLastShotMarkers(Vector3 left, Vector3 right, Vector3 up)
    {
        bool show = hitController.hasLastShotUiData;
        float alpha = 1f;
        if (show && lastShotDisplaySeconds > 0f)
        {
            float age = Time.time - hitController.lastShotUiTime;
            show = age <= lastShotDisplaySeconds;
            alpha = Mathf.Clamp01(1f - Mathf.InverseLerp(lastShotDisplaySeconds * 0.72f, lastShotDisplaySeconds, age));
        }

        if (!show)
        {
            manualMarkerRenderer.enabled = false;
            blendedMarkerRenderer.enabled = false;
            return;
        }

        float maxPower = Mathf.Max(1f, displayMaxPowerMps, hitController.lastManualShotSpeed, hitController.lastBlendedShotSpeed);
        DrawPowerMarker(manualMarkerRenderer, left, right, up, hitController.lastManualShotSpeed / maxPower, WithAlpha(manualMarkerColor, manualMarkerColor.a * alpha), -0.55f);
        DrawPowerMarker(blendedMarkerRenderer, left, right, up, hitController.lastBlendedShotSpeed / maxPower, WithAlpha(blendedMarkerColor, blendedMarkerColor.a * alpha), 0.55f);
    }

    private void DrawPowerMarker(LineRenderer renderer, Vector3 left, Vector3 right, Vector3 up, float normalized, Color color, float laneOffset)
    {
        float t = Mathf.Clamp01(normalized);
        Vector3 point = Vector3.Lerp(left, right, t);
        Vector3 barRight = (right - left).normalized;
        Vector3 lane = point + up * markerHeight * laneOffset;
        DrawLine(renderer, lane - barRight * markerWidth * 0.5f, lane + barRight * markerWidth * 0.5f, color, barThickness * 0.55f);
    }

    private void DrawTimingMarker(Vector3 center, Vector3 right, Vector3 up)
    {
        bool ready = hitController.ballIsInHittingZone || (aiSource != null && aiSource.IsInVisualTimingWindow);
        Color color = ready ? timingReadyColor : timingIdleColor;
        float size = timingMarkerSize * (ready ? 1.15f : 1f);

        timingRenderer.enabled = true;
        timingRenderer.loop = true;
        timingRenderer.positionCount = 4;
        timingRenderer.startWidth = barThickness * 0.58f;
        timingRenderer.endWidth = barThickness * 0.58f;
        timingRenderer.startColor = color;
        timingRenderer.endColor = color;
        timingRenderer.SetPosition(0, center + up * size);
        timingRenderer.SetPosition(1, center + right * size);
        timingRenderer.SetPosition(2, center - up * size);
        timingRenderer.SetPosition(3, center - right * size);
    }

    private void DrawLine(LineRenderer renderer, Vector3 a, Vector3 b, Color color, float width)
    {
        renderer.enabled = true;
        renderer.loop = false;
        renderer.positionCount = 2;
        renderer.startWidth = Mathf.Max(0.005f, width);
        renderer.endWidth = Mathf.Max(0.005f, width);
        renderer.startColor = color;
        renderer.endColor = color;
        renderer.SetPosition(0, a);
        renderer.SetPosition(1, b);
    }

    private void EnsureRenderers()
    {
        EnsureMaterial();
        backgroundRenderer = EnsureRenderer(backgroundRenderer, "Power Background", sortingOrder);
        emptyRenderer = EnsureRenderer(emptyRenderer, "Power Empty", sortingOrder + 1);
        fillRenderer = EnsureRenderer(fillRenderer, "Power Fill", sortingOrder + 2);
        manualMarkerRenderer = EnsureRenderer(manualMarkerRenderer, "Manual Marker", sortingOrder + 3);
        blendedMarkerRenderer = EnsureRenderer(blendedMarkerRenderer, "Blended Marker", sortingOrder + 4);
        timingRenderer = EnsureRenderer(timingRenderer, "Timing Marker", sortingOrder + 5);
    }

    private void EnsureMaterial()
    {
        if (lineMaterial != null)
            return;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (shader != null)
        {
            lineMaterial = new Material(shader);
            lineMaterial.name = "OverheadShotPowerBar_LineMaterial";
            lineMaterial.hideFlags = HideFlags.DontSave;
        }
    }

    private LineRenderer EnsureRenderer(LineRenderer renderer, string objectName, int order)
    {
        if (renderer != null)
            return renderer;

        GameObject lineObject = new GameObject(objectName);
        lineObject.hideFlags = HideFlags.DontSave;
        lineObject.transform.SetParent(transform, false);
        renderer = lineObject.AddComponent<LineRenderer>();
        renderer.useWorldSpace = true;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.alignment = LineAlignment.View;
        renderer.numCapVertices = 4;
        renderer.sortingOrder = order;
        if (lineMaterial != null)
            renderer.material = lineMaterial;
        return renderer;
    }

    private void SetAllVisible(bool visible)
    {
        SetVisible(backgroundRenderer, visible);
        SetVisible(emptyRenderer, visible);
        SetVisible(fillRenderer, visible);
        SetVisible(manualMarkerRenderer, visible);
        SetVisible(blendedMarkerRenderer, visible);
        SetVisible(timingRenderer, visible);
    }

    private static void SetVisible(LineRenderer renderer, bool visible)
    {
        if (renderer != null)
            renderer.enabled = visible;
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = Mathf.Clamp01(alpha);
        return color;
    }
}
