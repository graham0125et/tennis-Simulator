using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class SwingPathUI : MonoBehaviour
{
    [Header("UI Area")]
    public RectTransform circleArea;
    public LineRenderer lineRenderer;
    public RectTransform targetMarker;

    [Header("Speed Grade")]
    public Gradient speedGradient;
    public float targetSpeedMetresPerSecond = 22.35f;
    public float speedToleranceMetresPerSecond = 3f;
    public TextMeshProUGUI speedGradeLabel;
    public TextMeshProUGUI speedValueLabel;
    public TextMeshProUGUI targetSpeedLabel;

    [Header("Swipe Metrics Helper")]
    public bool showSwipeMetricsHelper = true;
    public Vector2 swipeMetricsAnchoredPosition = new Vector2(-18f, 0f);
    public Vector2 swipeMetricsSize = new Vector2(150f, 150f);
    public float rawSwipeSpeedMaxCentimetresPerSecond = 400f;
    public float rawSwipeDistanceMaxCentimetres = 70f;
    public float rawSwipeDurationMaxSeconds = 0.36f;
    public TextMeshProUGUI swipeMetricsSummaryLabel;
    public TextMeshProUGUI swipeMetricsValuesLabel;
    [Header("Drawing")]
    public float displaySeconds = 3f;
    public float traceThickness = 4f;
    public float targetLineThickness = 7f;
    public Color circleColor = new Color(1f, 1f, 1f, 0.35f);
    public Color targetLineColor = new Color(0.75f, 0.75f, 0.75f, 0.85f);
    public Color manualSpeedColor = new Color(0.25f, 0.65f, 1f, 1f);
    public Color targetSpeedLineColor = new Color(1f, 1f, 1f, 0.95f);
    public Color capSpeedLineColor = new Color(0.75f, 0.75f, 0.75f, 0.85f);
    public Color slowColor = new Color(1f, 0.25f, 0.2f, 1f);
    public Color goodColor = new Color(0.25f, 1f, 0.45f, 1f);
    public Color fastColor = new Color(1f, 0.75f, 0.15f, 1f);

    [Header("Trace Fit")]
    public bool autoFitTrace = true;
    public float manualCentimetresToRadius = 18f;
    public float minTraceDeltaPixels = 0.001f;

    private readonly List<Vector2> tracePoints = new List<Vector2>(128);
    private readonly List<float> traceSpeeds = new List<float>(128);

    private ShotTraceGraphic traceGraphic;
    private SwipeMetricsGraphic swipeMetricsGraphic;
    private float shownAtUnscaledTime;
#if UNITY_EDITOR
    private bool editorGraphicRefreshQueued;
#endif

    private const float ScreenSpaceSwipeSpeedMax = 400f;
    private const float ScreenSpaceSwipeDistanceMax = 70f;
    private const float ScreenSpaceSwipeDurationMax = 0.36f;

    void Awake()
    {
        NormalizeSwipeMetricRanges();
        EnsureGraphic();
        Hide();
    }

    void OnValidate()
    {
        NormalizeSwipeMetricRanges();

        RequestEditorGraphicRefresh();
    }
    private void NormalizeSwipeMetricRanges()
    {
        if (rawSwipeSpeedMaxCentimetresPerSecond <= 100f)
            rawSwipeSpeedMaxCentimetresPerSecond = ScreenSpaceSwipeSpeedMax;

        if (rawSwipeDistanceMaxCentimetres <= 20f)
            rawSwipeDistanceMaxCentimetres = ScreenSpaceSwipeDistanceMax;

        if (rawSwipeDurationMaxSeconds <= 0.22f)
            rawSwipeDurationMaxSeconds = ScreenSpaceSwipeDurationMax;
    }

    private void RequestEditorGraphicRefresh()
    {
#if UNITY_EDITOR
        if (editorGraphicRefreshQueued)
            return;

        editorGraphicRefreshQueued = true;
        EditorApplication.delayCall += RefreshGraphicFromEditorDelay;
#endif
    }

#if UNITY_EDITOR
    private void RefreshGraphicFromEditorDelay()
    {
        editorGraphicRefreshQueued = false;

        if (this == null || gameObject == null)
            return;

        EnsureGraphic();
        ApplyGraphicSettings();
    }
#endif

    void Update()
    {
        if (displaySeconds <= 0f || traceGraphic == null || !traceGraphic.enabled)
            return;

        if (Time.unscaledTime - shownAtUnscaledTime >= displaySeconds)
            Hide();
    }

    public void ShowFromSampler(
        InputDirectionSampler120Hz sampler,
        int startSequenceExclusive,
        int endSequenceInclusive,
        Vector3 aimDir,
        float blendedSpeedMetresPerSecond,
        float manualSpeedMetresPerSecond = -1f,
        float targetSpeedMetresPerSecondOverride = -1f,
        float backswingCapSpeedMetresPerSecond = -1f,
        float maxScaleSpeedMetresPerSecond = -1f,
        float rawSwipeSpeedCentimetresPerSecond = -1f,
        float rawSwipeDurationSeconds = -1f,
        float rawSwipeDistanceCentimetres = -1f)
    {
        if (sampler == null)
            return;

        sampler.CopyTracePointsBetween(
            startSequenceExclusive,
            endSequenceInclusive,
            tracePoints,
            traceSpeeds,
            minTraceDeltaPixels
        );

        ShowTrace(
            tracePoints,
            traceSpeeds,
            aimDir,
            blendedSpeedMetresPerSecond,
            manualSpeedMetresPerSecond,
            targetSpeedMetresPerSecondOverride,
            backswingCapSpeedMetresPerSecond,
            maxScaleSpeedMetresPerSecond,
            rawSwipeSpeedCentimetresPerSecond,
            rawSwipeDurationSeconds,
            rawSwipeDistanceCentimetres
        );
    }

    public void ShowPath(List<Vector2> path, List<float> speeds, Vector3 aimDir)
    {
        tracePoints.Clear();
        traceSpeeds.Clear();

        if (path != null && path.Count > 0)
        {
            Vector2 origin = path[0];
            for (int i = 0; i < path.Count; i++)
                tracePoints.Add(path[i] - origin);
        }

        if (speeds != null)
            traceSpeeds.AddRange(speeds);

        float shotSpeed = EstimateShotSpeedMetresPerSecond(traceSpeeds);
        ShowTrace(tracePoints, traceSpeeds, aimDir, shotSpeed, shotSpeed, targetSpeedMetresPerSecond, targetSpeedMetresPerSecond, -1f, -1f, -1f, -1f);
    }

    public void Hide()
    {
        if (traceGraphic != null)
            traceGraphic.enabled = false;

        if (lineRenderer != null)
            lineRenderer.enabled = false;

        if (targetMarker != null)
            targetMarker.gameObject.SetActive(false);

        SetLabel(speedGradeLabel, string.Empty, Color.white);
        SetLabel(speedValueLabel, string.Empty, Color.white);
        SetLabel(targetSpeedLabel, string.Empty, Color.white);
        SetSwipeMetricsVisible(false);
    }

    private void ShowTrace(
        List<Vector2> path,
        List<float> speeds,
        Vector3 aimDir,
        float blendedSpeedMetresPerSecond,
        float manualSpeedMetresPerSecond,
        float targetSpeedMetresPerSecondOverride,
        float backswingCapSpeedMetresPerSecond,
        float maxScaleSpeedMetresPerSecond,
        float rawSwipeSpeedCentimetresPerSecond,
        float rawSwipeDurationSeconds,
        float rawSwipeDistanceCentimetres)
    {
        EnsureGraphic();

        if (traceGraphic == null)
            return;

        if (path == null || path.Count < 2)
        {
            tracePoints.Clear();
            tracePoints.Add(Vector2.zero);
            tracePoints.Add(Vector2.up * 0.01f);
            path = tracePoints;
        }

        float targetSpeed = targetSpeedMetresPerSecondOverride > 0f
            ? targetSpeedMetresPerSecondOverride
            : targetSpeedMetresPerSecond;

        float manualSpeed = manualSpeedMetresPerSecond > 0f
            ? manualSpeedMetresPerSecond
            : blendedSpeedMetresPerSecond;

        float backswingCapSpeed = backswingCapSpeedMetresPerSecond > 0f
            ? backswingCapSpeedMetresPerSecond
            : targetSpeed;

        SwingSpeedGrade grade = EvaluateSpeedGrade(blendedSpeedMetresPerSecond, targetSpeed);
        Color gradeColor = GradeColor(grade);

        ApplyGraphicSettings();
        traceGraphic.SetTrace(
            path,
            speeds,
            AimDirToUiDirection(aimDir),
            manualSpeed,
            blendedSpeedMetresPerSecond,
            targetSpeed,
            backswingCapSpeed,
            maxScaleSpeedMetresPerSecond,
            grade,
            gradeColor
        );

        traceGraphic.enabled = true;
        shownAtUnscaledTime = Time.unscaledTime;

        if (targetMarker != null)
            targetMarker.gameObject.SetActive(false);

        SetLabel(speedGradeLabel, GradeText(grade), gradeColor);
        SetLabel(speedValueLabel, $"Manual {manualSpeed:F1} | Blend {blendedSpeedMetresPerSecond:F1} m/s", gradeColor);
        SetLabel(targetSpeedLabel, $"Target {targetSpeed:F1} | Cap {backswingCapSpeed:F1} m/s", targetSpeedLineColor);
        ShowSwipeMetrics(rawSwipeSpeedCentimetresPerSecond, rawSwipeDurationSeconds, rawSwipeDistanceCentimetres);
    }

    private void EnsureGraphic()
    {
        RectTransform parent = circleArea != null ? circleArea : transform as RectTransform;
        if (parent == null)
            return;

        if (traceGraphic == null)
            traceGraphic = parent.GetComponentInChildren<ShotTraceGraphic>(true);

        if (traceGraphic == null)
        {
            GameObject graphicObject = new GameObject("Shot Trace Graphic", typeof(RectTransform), typeof(CanvasRenderer));
            graphicObject.transform.SetParent(parent, false);
            traceGraphic = graphicObject.AddComponent<ShotTraceGraphic>();
        }

        RectTransform rt = traceGraphic.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        traceGraphic.raycastTarget = false;
        ApplyGraphicSettings();
        EnsureSwipeMetricsGraphic();
    }

    private void EnsureSwipeMetricsGraphic()
    {
        RectTransform traceParent = circleArea != null ? circleArea : transform as RectTransform;
        if (traceParent == null)
            return;

        if (swipeMetricsGraphic == null)
        {
            Transform existing = traceParent.Find("Swipe Metrics Helper");
            if (existing != null)
                swipeMetricsGraphic = existing.GetComponent<SwipeMetricsGraphic>();
        }

        if (swipeMetricsGraphic == null)
        {
            GameObject helperObject = new GameObject("Swipe Metrics Helper", typeof(RectTransform), typeof(CanvasRenderer));
            helperObject.transform.SetParent(traceParent, false);
            swipeMetricsGraphic = helperObject.AddComponent<SwipeMetricsGraphic>();
        }

        RectTransform rt = swipeMetricsGraphic.rectTransform;
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.anchoredPosition = swipeMetricsAnchoredPosition;
        rt.sizeDelta = swipeMetricsSize;
        swipeMetricsGraphic.raycastTarget = false;

        swipeMetricsGraphic.slowColor = new Color(1f, 0.92f, 0.05f, 0.95f);
        swipeMetricsGraphic.goodColor = new Color(0.25f, 1f, 0.35f, 0.95f);
        swipeMetricsGraphic.fastColor = new Color(0.08f, 0.72f, 1f, 0.95f);

        EnsureMetricLabel(ref swipeMetricsSummaryLabel, "Swipe Metrics Summary", new Vector2(-180f, 52f), 18f, FontStyles.Bold);
        EnsureMetricLabel(ref swipeMetricsValuesLabel, "Swipe Metrics Values", new Vector2(-180f, -28f), 14f, FontStyles.Normal);
    }

    private void EnsureMetricLabel(ref TextMeshProUGUI label, string name, Vector2 anchoredPosition, float fontSize, FontStyles fontStyle)
    {
        if (label != null)
            return;

        RectTransform traceParent = circleArea != null ? circleArea : transform as RectTransform;
        if (traceParent == null)
            return;

        GameObject labelObject = new GameObject(name, typeof(RectTransform));
        labelObject.transform.SetParent(traceParent, false);
        RectTransform rt = labelObject.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = new Vector2(170f, fontSize > 16f ? 54f : 76f);

        label = labelObject.AddComponent<TextMeshProUGUI>();
        label.raycastTarget = false;
        label.alignment = TextAlignmentOptions.Right;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.fontSize = fontSize;
        label.fontStyle = fontStyle;
        label.color = Color.white;
    }

    private void ShowSwipeMetrics(float rawSwipeSpeedCentimetresPerSecond, float rawSwipeDurationSeconds, float rawSwipeDistanceCentimetres)
    {
        EnsureSwipeMetricsGraphic();

        bool hasMetrics = showSwipeMetricsHelper && rawSwipeSpeedCentimetresPerSecond >= 0f && rawSwipeDurationSeconds >= 0f && rawSwipeDistanceCentimetres >= 0f;
        if (!hasMetrics || swipeMetricsGraphic == null)
        {
            SetSwipeMetricsVisible(false);
            return;
        }

        float speed01 = Mathf.Clamp01(rawSwipeSpeedCentimetresPerSecond / Mathf.Max(1f, rawSwipeSpeedMaxCentimetresPerSecond));
        float duration01 = Mathf.Clamp01(rawSwipeDurationSeconds / Mathf.Max(0.001f, rawSwipeDurationMaxSeconds));
        float distance01 = Mathf.Clamp01(rawSwipeDistanceCentimetres / Mathf.Max(0.01f, rawSwipeDistanceMaxCentimetres));
        swipeMetricsGraphic.SetMetrics(speed01, duration01, distance01);
        SetSwipeMetricsVisible(true);

        SetLabel(swipeMetricsSummaryLabel, $"RHYTHM {MetricText(speed01, "SLOW", "GOOD", "FAST")}\nLENGTH {MetricText(distance01, "SHORT", "GOOD", "LONG")}\nDURATION {MetricText(duration01, "SHORT", "GOOD", "LONG")}", Color.white);
        SetLabel(swipeMetricsValuesLabel, $"{rawSwipeSpeedCentimetresPerSecond:F0} cm/s\n{rawSwipeDistanceCentimetres:F1} cm\n{rawSwipeDurationSeconds:F3} s", new Color(1f, 1f, 1f, 0.86f));
    }

    private void SetSwipeMetricsVisible(bool visible)
    {
        if (swipeMetricsGraphic != null)
            swipeMetricsGraphic.enabled = visible;

        if (swipeMetricsSummaryLabel != null)
            swipeMetricsSummaryLabel.gameObject.SetActive(visible);

        if (swipeMetricsValuesLabel != null)
            swipeMetricsValuesLabel.gameObject.SetActive(visible);
    }

    private static string MetricText(float value01, string low, string mid, string high)
    {
        if (value01 < 0.35f)
            return low;

        if (value01 > 0.75f)
            return high;

        return mid;
    }

    private void ApplyGraphicSettings()
    {
        if (traceGraphic == null)
            return;

        traceGraphic.speedGradient = speedGradient;
        traceGraphic.traceThickness = traceThickness;
        traceGraphic.targetLineThickness = targetLineThickness;
        traceGraphic.circleColor = circleColor;
        traceGraphic.targetLineColor = targetLineColor;
        traceGraphic.manualSpeedColor = manualSpeedColor;
        traceGraphic.targetSpeedLineColor = targetSpeedLineColor;
        traceGraphic.capSpeedLineColor = capSpeedLineColor;
        traceGraphic.slowColor = slowColor;
        traceGraphic.goodColor = goodColor;
        traceGraphic.fastColor = fastColor;
        traceGraphic.autoFitTrace = autoFitTrace;
        traceGraphic.manualCentimetresToRadius = manualCentimetresToRadius;
    }

    private SwingSpeedGrade EvaluateSpeedGrade(float shotSpeed, float targetSpeed)
    {
        float tolerance = Mathf.Max(0.01f, speedToleranceMetresPerSecond);

        if (shotSpeed < targetSpeed - tolerance)
            return SwingSpeedGrade.Slow;

        if (shotSpeed > targetSpeed + tolerance)
            return SwingSpeedGrade.Fast;

        return SwingSpeedGrade.Good;
    }

    private Color GradeColor(SwingSpeedGrade grade)
    {
        if (grade == SwingSpeedGrade.Slow)
            return slowColor;

        if (grade == SwingSpeedGrade.Fast)
            return fastColor;

        return goodColor;
    }

    private static string GradeText(SwingSpeedGrade grade)
    {
        if (grade == SwingSpeedGrade.Slow)
            return "SLOW";

        if (grade == SwingSpeedGrade.Fast)
            return "FAST";

        return "GOOD";
    }

    private static Vector2 AimDirToUiDirection(Vector3 aimDir)
    {
        Vector2 uiDir = new Vector2(-aimDir.z, aimDir.x);
        return uiDir.sqrMagnitude > 1e-6f ? uiDir.normalized : Vector2.up;
    }

    private static float EstimateShotSpeedMetresPerSecond(List<float> speedsCentimetresPerSecond)
    {
        if (speedsCentimetresPerSecond == null || speedsCentimetresPerSecond.Count == 0)
            return 0f;

        float total = 0f;
        int count = 0;

        for (int i = 0; i < speedsCentimetresPerSecond.Count; i++)
        {
            if (speedsCentimetresPerSecond[i] <= 0f)
                continue;

            total += speedsCentimetresPerSecond[i];
            count++;
        }

        return count > 0 ? (total / count) * 0.01f : 0f;
    }

    private static void SetLabel(TextMeshProUGUI label, string text, Color color)
    {
        if (label == null)
            return;

        label.text = text;
        label.color = color;
    }

}
