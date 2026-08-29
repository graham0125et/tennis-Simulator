using UnityEngine;
using UnityEngine.UI;

[AddComponentMenu("")]
public class SwipeMetricsGraphic : MaskableGraphic
{
    public Color slowColor = new Color(1f, 0.92f, 0.05f, 0.95f);
    public Color goodColor = new Color(0.25f, 1f, 0.35f, 0.95f);
    public Color fastColor = new Color(0.08f, 0.72f, 1f, 0.95f);
    public Color backgroundColor = new Color(0f, 0f, 0f, 0.42f);
    public Color lineColor = new Color(1f, 1f, 1f, 0.72f);
    public float outlineThickness = 2f;
    public float separatorThickness = 2f;
    public float innerRadiusRatio = 0.16f;

    private float speed01;
    private float duration01;
    private float distance01;

    public void SetMetrics(float sourceSpeed01, float sourceDuration01, float sourceDistance01)
    {
        speed01 = Mathf.Clamp01(sourceSpeed01);
        duration01 = Mathf.Clamp01(sourceDuration01);
        distance01 = Mathf.Clamp01(sourceDistance01);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect rect = rectTransform.rect;
        if (rect.width <= 1f || rect.height <= 1f)
            return;

        Vector2 center = rect.center;
        float radius = Mathf.Max(4f, Mathf.Min(rect.width, rect.height) * 0.5f - 4f);
        float innerRadius = Mathf.Clamp(radius * innerRadiusRatio, 0f, radius * 0.75f);

        AddSector(vh, center, innerRadius, radius, 90f, 210f, backgroundColor, 20);
        AddSector(vh, center, innerRadius, radius, 210f, 330f, backgroundColor, 20);
        AddSector(vh, center, innerRadius, radius, 330f, 450f, backgroundColor, 20);

        AddSector(vh, center, innerRadius, Mathf.Lerp(innerRadius + 6f, radius, speed01), 90f, 210f, MetricColor(speed01), 20);
        AddSector(vh, center, innerRadius, Mathf.Lerp(innerRadius + 6f, radius, duration01), 210f, 330f, MetricColor(duration01), 20);
        AddSector(vh, center, innerRadius, Mathf.Lerp(innerRadius + 6f, radius, distance01), 330f, 450f, MetricColor(distance01), 20);

        AddCircle(vh, center, radius, 72, outlineThickness, lineColor);
        AddCircle(vh, center, innerRadius, 48, outlineThickness, WithAlpha(lineColor, lineColor.a * 0.8f));
        AddSeparator(vh, center, radius, 90f);
        AddSeparator(vh, center, radius, 210f);
        AddSeparator(vh, center, radius, 330f);
    }

    private Color MetricColor(float value01)
    {
        if (value01 < 0.35f)
            return slowColor;

        if (value01 > 0.75f)
            return fastColor;

        return goodColor;
    }

    private void AddSeparator(VertexHelper vh, Vector2 center, float radius, float angleDeg)
    {
        Vector2 dir = AngleToDirection(angleDeg);
        AddLine(vh, center, center + dir * radius, separatorThickness, lineColor);
    }

    private static void AddSector(VertexHelper vh, Vector2 center, float innerRadius, float outerRadius, float startDeg, float endDeg, Color color, int segments)
    {
        if (outerRadius <= innerRadius + 0.01f)
            return;

        int startIndex = vh.currentVertCount;
        segments = Mathf.Max(2, segments);

        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float angle = Mathf.Lerp(startDeg, endDeg, t);
            Vector2 dir = AngleToDirection(angle);
            vh.AddVert(center + dir * outerRadius, color, Vector2.zero);
            vh.AddVert(center + dir * innerRadius, color, Vector2.zero);
        }

        for (int i = 0; i < segments; i++)
        {
            int a = startIndex + i * 2;
            int b = a + 1;
            int c = a + 2;
            int d = a + 3;
            vh.AddTriangle(a, c, b);
            vh.AddTriangle(c, d, b);
        }
    }

    private static void AddCircle(VertexHelper vh, Vector2 center, float radius, int segments, float thickness, Color color)
    {
        segments = Mathf.Max(8, segments);
        float half = Mathf.Max(0.5f, thickness * 0.5f);
        float inner = Mathf.Max(0f, radius - half);
        float outer = radius + half;

        for (int i = 0; i < segments; i++)
        {
            float a0 = i * Mathf.PI * 2f / segments;
            float a1 = (i + 1) * Mathf.PI * 2f / segments;
            Vector2 outer0 = center + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * outer;
            Vector2 outer1 = center + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * outer;
            Vector2 inner0 = center + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * inner;
            Vector2 inner1 = center + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * inner;
            AddQuad(vh, outer0, outer1, inner1, inner0, color);
        }
    }

    private static void AddLine(VertexHelper vh, Vector2 from, Vector2 to, float thickness, Color color)
    {
        Vector2 delta = to - from;
        if (delta.sqrMagnitude < 0.0001f)
            return;

        Vector2 normal = new Vector2(-delta.y, delta.x).normalized * (thickness * 0.5f);
        AddQuad(vh, from - normal, from + normal, to + normal, to - normal, color);
    }

    private static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
    {
        int index = vh.currentVertCount;
        vh.AddVert(a, color, Vector2.zero);
        vh.AddVert(b, color, Vector2.zero);
        vh.AddVert(c, color, Vector2.zero);
        vh.AddVert(d, color, Vector2.zero);
        vh.AddTriangle(index, index + 1, index + 2);
        vh.AddTriangle(index, index + 2, index + 3);
    }

    private static Vector2 AngleToDirection(float angleDeg)
    {
        float rad = angleDeg * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }
}