using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class CourtAlignmentGizmo : MonoBehaviour
{
    [Header("Visibility")]
    public bool drawWhenUnselected = true;
    public bool drawLabels = true;
    public bool drawNetHeight = true;
    public bool drawServiceBoxes = true;
    public bool drawSinglesLines = true;
    public bool drawCenterMarks = true;

    [Header("Court Dimensions (metres)")]
    public float courtLength = 23.77f;
    public float doublesWidth = 10.97f;
    public float singlesWidth = 8.23f;
    public float serviceLineDistanceFromNet = 6.40f;
    public float netHeight = 0.914f;
    public float netPostHeight = 1.07f;

    [Header("Draw Placement")]
    public float courtSurfaceY = 0.02f;
    public float lineLift = 0.02f;
    public float centerMarkLength = 0.20f;
    public float labelSize = 0.18f;

    [Header("Colours")]
    public Color doublesColour = new Color(1f, 1f, 1f, 0.95f);
    public Color singlesColour = new Color(0.45f, 0.9f, 1f, 0.95f);
    public Color serviceColour = new Color(1f, 0.9f, 0.2f, 0.95f);
    public Color netColour = new Color(1f, 0.15f, 0.1f, 1f);
    public Color netHeightColour = new Color(1f, 0.45f, 0.1f, 0.95f);

    private void OnDrawGizmos()
    {
        if (drawWhenUnselected)
            DrawCourtGizmo();
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawWhenUnselected)
            DrawCourtGizmo();
    }

    private void DrawCourtGizmo()
    {
        float halfLength = courtLength * 0.5f;
        float halfDoubles = doublesWidth * 0.5f;
        float halfSingles = singlesWidth * 0.5f;
        float y = courtSurfaceY + lineLift;

        Color previousColour = Gizmos.color;

        Gizmos.color = doublesColour;
        DrawRect(-halfLength, halfLength, -halfDoubles, halfDoubles, y);
        DrawLineLocal(0f, -halfDoubles, 0f, halfDoubles, y);

        if (drawSinglesLines)
        {
            Gizmos.color = singlesColour;
            DrawLineLocal(-halfLength, -halfSingles, halfLength, -halfSingles, y);
            DrawLineLocal(-halfLength, halfSingles, halfLength, halfSingles, y);
        }

        if (drawServiceBoxes)
        {
            Gizmos.color = serviceColour;
            DrawLineLocal(-serviceLineDistanceFromNet, -halfSingles, -serviceLineDistanceFromNet, halfSingles, y);
            DrawLineLocal(serviceLineDistanceFromNet, -halfSingles, serviceLineDistanceFromNet, halfSingles, y);
            DrawLineLocal(-serviceLineDistanceFromNet, 0f, serviceLineDistanceFromNet, 0f, y);
        }

        if (drawCenterMarks)
        {
            Gizmos.color = serviceColour;
            float halfMark = centerMarkLength * 0.5f;
            DrawLineLocal(-halfLength, -halfMark, -halfLength, halfMark, y);
            DrawLineLocal(halfLength, -halfMark, halfLength, halfMark, y);
        }

        Gizmos.color = netColour;
        DrawLineLocal(0f, -halfDoubles, 0f, halfDoubles, y + 0.015f);

        if (drawNetHeight)
        {
            Gizmos.color = netHeightColour;
            DrawLineLocal3D(new Vector3(0f, netHeight, -halfDoubles), new Vector3(0f, netHeight, halfDoubles));
            DrawLineLocal3D(new Vector3(0f, 0f, -halfDoubles), new Vector3(0f, netPostHeight, -halfDoubles));
            DrawLineLocal3D(new Vector3(0f, 0f, halfDoubles), new Vector3(0f, netPostHeight, halfDoubles));
        }

#if UNITY_EDITOR
        if (drawLabels)
        {
            Handles.color = netColour;
            DrawLabel(new Vector3(0f, netHeight + 0.15f, 0f), "NET / X=0");
            DrawLabel(new Vector3(-halfLength, y + 0.08f, 0f), "BASELINE");
            DrawLabel(new Vector3(halfLength, y + 0.08f, 0f), "BASELINE");
            DrawLabel(new Vector3(-serviceLineDistanceFromNet, y + 0.08f, halfSingles + 0.25f), "SERVICE");
            DrawLabel(new Vector3(serviceLineDistanceFromNet, y + 0.08f, halfSingles + 0.25f), "SERVICE");
        }
#endif

        Gizmos.color = previousColour;
    }

    private void DrawRect(float minX, float maxX, float minZ, float maxZ, float y)
    {
        DrawLineLocal(minX, minZ, maxX, minZ, y);
        DrawLineLocal(maxX, minZ, maxX, maxZ, y);
        DrawLineLocal(maxX, maxZ, minX, maxZ, y);
        DrawLineLocal(minX, maxZ, minX, minZ, y);
    }

    private void DrawLineLocal(float x0, float z0, float x1, float z1, float y)
    {
        DrawLineLocal3D(new Vector3(x0, y, z0), new Vector3(x1, y, z1));
    }

    private void DrawLineLocal3D(Vector3 localA, Vector3 localB)
    {
        Gizmos.DrawLine(transform.TransformPoint(localA), transform.TransformPoint(localB));
    }

#if UNITY_EDITOR
    private void DrawLabel(Vector3 localPosition, string label)
    {
        GUIStyle style = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = Mathf.Max(8, Mathf.RoundToInt(labelSize * 60f))
        };
        style.normal.textColor = Handles.color;
        Handles.Label(transform.TransformPoint(localPosition), label, style);
    }
#endif
}