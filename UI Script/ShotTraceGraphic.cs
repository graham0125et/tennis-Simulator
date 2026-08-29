using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public enum SwingSpeedGrade
{
    Slow,
    Good,
    Fast
}

[AddComponentMenu("")]
public class ShotTraceGraphic : MaskableGraphic
{
    public Gradient speedGradient;
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
    public bool autoFitTrace = true;
    public float manualCentimetresToRadius = 18f;

    private readonly List<Vector2> points = new List<Vector2>(128);
    private readonly List<float> speeds = new List<float>(128);
    private readonly List<Vector2> fittedPoints = new List<Vector2>(128);

    private Vector2 targetDirection = Vector2.up;
    private float manualSpeed;
    private float blendedSpeed;
    private float targetSpeed;
    private float backswingCapSpeed;
    private float maxScaleSpeed;
    private SwingSpeedGrade grade = SwingSpeedGrade.Good;
    private Color gradeColor = Color.white;

    public void SetTrace(
        List<Vector2> sourcePoints,
        List<float> sourceSpeeds,
        Vector2 sourceTargetDirection,
        float sourceManualSpeed,
        float sourceBlendedSpeed,
        float sourceTargetSpeed,
        float sourceBackswingCapSpeed,
        float sourceMaxScaleSpeed,
        SwingSpeedGrade sourceGrade,
        Color sourceGradeColor)
    {
        points.Clear();
        speeds.Clear();

        if (sourcePoints != null)
            points.AddRange(sourcePoints);

        if (sourceSpeeds != null)
            speeds.AddRange(sourceSpeeds);

        targetDirection = sourceTargetDirection.sqrMagnitude > 1e-6f
            ? sourceTargetDirection.normalized
            : Vector2.up;
        manualSpeed = Mathf.Max(0f, sourceManualSpeed);
        blendedSpeed = Mathf.Max(0f, sourceBlendedSpeed);
        targetSpeed = Mathf.Max(0.01f, sourceTargetSpeed);
        backswingCapSpeed = Mathf.Max(0f, sourceBackswingCapSpeed);
        maxScaleSpeed = MaxPositiveSpeed(sourceMaxScaleSpeed, targetSpeed, backswingCapSpeed, blendedSpeed, manualSpeed);
        grade = sourceGrade;
        gradeColor = sourceGradeColor;

        SetAllDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect rect = rectTransform.rect;
        if (rect.width <= 1f || rect.height <= 1f)
            return;

        float padding = 8f;
        float lowerBand = 58f;
        float circleRadius = Mathf.Min(
            rect.width * 0.5f - padding,
            (rect.height - lowerBand) * 0.5f - padding
        );
        circleRadius = Mathf.Max(8f, circleRadius);

        Vector2 circleCenter = new Vector2(
            rect.center.x,
            rect.yMax - padding - circleRadius
        );

        AddCircle(vh, circleCenter, circleRadius, 48, 2f, circleColor);
        AddLine(
            vh,
            circleCenter - targetDirection * circleRadius * 0.82f,
            circleCenter + targetDirection * circleRadius * 0.82f,
            targetLineThickness,
            WithAlpha(targetLineColor, targetLineColor.a * 0.65f)
        );

        BuildFittedPoints(circleCenter, circleRadius * 0.82f);
        DrawTrace(vh);
        DrawTargetLineUnderCircle(vh, rect, circleCenter, circleRadius);
        DrawSpeedBar(vh, rect);
    }

    private void BuildFittedPoints(Vector2 center, float radius)
    {
        fittedPoints.Clear();

        if (points.Count == 0)
            return;

        Vector2 min = points[0];
        Vector2 max = points[0];

        for (int i = 1; i < points.Count; i++)
        {
            min = Vector2.Min(min, points[i]);
            max = Vector2.Max(max, points[i]);
        }

        Vector2 pathCenter = (min + max) * 0.5f;
        Vector2 size = max - min;
        float largestExtent = Mathf.Max(size.x, size.y) * 0.5f;
        float scale = autoFitTrace
            ? radius / Mathf.Max(0.01f, largestExtent)
            : radius / Mathf.Max(0.01f, manualCentimetresToRadius);

        for (int i = 0; i < points.Count; i++)
            fittedPoints.Add(center + (points[i] - pathCenter) * scale);
    }

    private void DrawTrace(VertexHelper vh)
    {
        if (fittedPoints.Count < 2)
            return;

        float maxSpeed = 0f;
        for (int i = 0; i < speeds.Count; i++)
            maxSpeed = Mathf.Max(maxSpeed, speeds[i]);

        for (int i = 1; i < fittedPoints.Count; i++)
        {
            float speed = i < speeds.Count ? speeds[i] : maxSpeed;
            float t = maxSpeed > 0.001f ? Mathf.Clamp01(speed / maxSpeed) : 1f;
            Color segmentColor = EvaluateTraceColor(t);
            AddLine(vh, fittedPoints[i - 1], fittedPoints[i], traceThickness, segmentColor);
        }
    }

    private void DrawTargetLineUnderCircle(VertexHelper vh, Rect rect, Vector2 circleCenter, float circleRadius)
    {
        float y = Mathf.Max(rect.yMin + 30f, circleCenter.y - circleRadius - 22f);
        Vector2 center = new Vector2(rect.center.x, y);
        float length = circleRadius * 1.1f;

        AddLine(
            vh,
            center - targetDirection * length * 0.5f,
            center + targetDirection * length * 0.5f,
            targetLineThickness,
            targetLineColor
        );
    }

    private void DrawSpeedBar(VertexHelper vh, Rect rect)
    {
        float padding = 14f;
        float y = rect.yMin + 12f;
        float xMin = rect.xMin + padding;
        float xMax = rect.xMax - padding;
        float maxBarSpeed = MaxPositiveSpeed(maxScaleSpeed, targetSpeed, backswingCapSpeed, blendedSpeed, manualSpeed);
        float manualT = Mathf.Clamp01(manualSpeed / maxBarSpeed);
        float blendedT = Mathf.Clamp01(blendedSpeed / maxBarSpeed);
        float targetT = Mathf.Clamp01(targetSpeed / maxBarSpeed);
        float capT = backswingCapSpeed > 0f ? Mathf.Clamp01(backswingCapSpeed / maxBarSpeed) : -1f;

        Vector2 start = new Vector2(xMin, y);
        Vector2 end = new Vector2(xMax, y);
        AddLine(vh, start, end, 8f, new Color(1f, 1f, 1f, 0.16f));

        Vector2 manualEnd = new Vector2(Mathf.Lerp(xMin, xMax, manualT), y - 3f);
        AddLine(vh, start + Vector2.down * 3f, manualEnd, 4f, manualSpeedColor);

        Vector2 blendedEnd = new Vector2(Mathf.Lerp(xMin, xMax, blendedT), y + 3f);
        AddLine(vh, start + Vector2.up * 3f, blendedEnd, 4f, gradeColor);

        float targetX = Mathf.Lerp(xMin, xMax, targetT);
        AddLine(
            vh,
            new Vector2(targetX, y - 11f),
            new Vector2(targetX, y + 11f),
            3f,
            targetSpeedLineColor
        );

        if (capT >= 0f)
        {
            float capX = Mathf.Lerp(xMin, xMax, capT);
            AddLine(
                vh,
                new Vector2(capX, y - 8f),
                new Vector2(capX, y + 8f),
                3f,
                capSpeedLineColor
            );
        }
    }

    private Color EvaluateTraceColor(float t)
    {
        if (speedGradient != null)
            return speedGradient.Evaluate(t);

        if (grade == SwingSpeedGrade.Slow)
            return Color.Lerp(slowColor, goodColor, t);

        if (grade == SwingSpeedGrade.Fast)
            return Color.Lerp(goodColor, fastColor, t);

        return Color.Lerp(slowColor, goodColor, t);
    }

    private static float MaxPositiveSpeed(float a, float b, float c, float d, float e)
    {
        return Mathf.Max(1f, Mathf.Max(a, Mathf.Max(b, Mathf.Max(c, Mathf.Max(d, e)))));
    }

    private static void AddCircle(
        VertexHelper vh,
        Vector2 center,
        float radius,
        int segments,
        float thickness,
        Color color)
    {
        Vector2 previous = center + Vector2.right * radius;

        for (int i = 1; i <= segments; i++)
        {
            float angle = (i / (float)segments) * Mathf.PI * 2f;
            Vector2 next = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            AddLine(vh, previous, next, thickness, color);
            previous = next;
        }
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }

    private static void AddLine(VertexHelper vh, Vector2 a, Vector2 b, float thickness, Color color)
    {
        Vector2 delta = b - a;
        if (delta.sqrMagnitude <= 0.000001f)
            return;

        Vector2 normal = new Vector2(-delta.y, delta.x).normalized * (thickness * 0.5f);

        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = color;

        int startIndex = vh.currentVertCount;

        vertex.position = a - normal;
        vh.AddVert(vertex);
        vertex.position = a + normal;
        vh.AddVert(vertex);
        vertex.position = b + normal;
        vh.AddVert(vertex);
        vertex.position = b - normal;
        vh.AddVert(vertex);

        vh.AddTriangle(startIndex, startIndex + 1, startIndex + 2);
        vh.AddTriangle(startIndex + 2, startIndex + 3, startIndex);
    }
}
