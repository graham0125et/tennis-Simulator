using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public class NetDistanceReadabilityOverlay : MonoBehaviour
{
    [Header("Official Dimensions")]
    [SerializeField] private float netSpan = 12.801f;
    [SerializeField] private float postHeight = 1.07f;
    [SerializeField] private float centerHeight = 0.914f;
    [SerializeField] private float singlesSupportX = 5.029f;
    [SerializeField] private float bottomHeight = 0.035f;

    [Header("Grid")]
    [SerializeField] private int verticalLineCount = 55;
    [SerializeField] private int horizontalLineCount = 8;
    [SerializeField] private float verticalRibbonWidth = 0.028f;
    [SerializeField] private float horizontalRibbonHeight = 0.024f;
    [SerializeField] private float depthOffset = 0.032f;
    [SerializeField] private Color overlayColor = new Color(0.006f, 0.010f, 0.005f, 1f);

    [Header("Rendering")]
    [SerializeField] private bool receiveShadows = false;
    [SerializeField] private bool castShadows = false;
    [SerializeField] private int sortingOrder = 2;
    [SerializeField] private Material overrideMaterial;

    private const string OverlayChildName = "Net Distance Readability Overlay";

    private Mesh overlayMesh;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Material generatedMaterial;

    private void OnEnable()
    {
        Rebuild();
    }

    private void OnDisable()
    {
        if (meshRenderer != null)
            meshRenderer.enabled = false;
    }

    private void OnValidate()
    {
        verticalLineCount = Mathf.Clamp(verticalLineCount, 5, 180);
        horizontalLineCount = Mathf.Clamp(horizontalLineCount, 2, 32);
        netSpan = Mathf.Max(0.1f, netSpan);
        postHeight = Mathf.Max(centerHeight, postHeight);
        centerHeight = Mathf.Max(0.1f, centerHeight);
        bottomHeight = Mathf.Max(0f, bottomHeight);
        verticalRibbonWidth = Mathf.Max(0.001f, verticalRibbonWidth);
        horizontalRibbonHeight = Mathf.Max(0.001f, horizontalRibbonHeight);
        depthOffset = Mathf.Max(0.001f, depthOffset);

        Rebuild();
    }

    [ContextMenu("Rebuild Net Distance Overlay")]
    public void Rebuild()
    {
        EnsureOverlayObjects();

        if (overlayMesh == null)
        {
            overlayMesh = new Mesh { name = "Net Distance Readability Overlay Mesh" };
            overlayMesh.MarkDynamic();
        }

        BuildMesh(overlayMesh);

        meshFilter.sharedMesh = overlayMesh;
        Material material = overrideMaterial != null ? overrideMaterial : GetGeneratedMaterial();
        if (overrideMaterial == null)
            SetMaterialColor(material, overlayColor);

        meshRenderer.sharedMaterial = material;
        meshRenderer.receiveShadows = receiveShadows;
        meshRenderer.shadowCastingMode = castShadows
            ? UnityEngine.Rendering.ShadowCastingMode.On
            : UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.sortingOrder = sortingOrder;
        meshRenderer.enabled = true;
    }

    private void EnsureOverlayObjects()
    {
        Transform child = transform.Find(OverlayChildName);
        if (child == null)
        {
            GameObject overlay = new GameObject(OverlayChildName);
            overlay.transform.SetParent(transform, false);
            child = overlay.transform;
        }

        meshFilter = child.GetComponent<MeshFilter>();
        if (meshFilter == null)
            meshFilter = child.gameObject.AddComponent<MeshFilter>();

        meshRenderer = child.GetComponent<MeshRenderer>();
        if (meshRenderer == null)
            meshRenderer = child.gameObject.AddComponent<MeshRenderer>();
    }

    private Material GetGeneratedMaterial()
    {
        if (generatedMaterial != null)
            return generatedMaterial;

        Shader shader = Shader.Find("HDRP/Unlit");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Standard");

        generatedMaterial = new Material(shader)
        {
            name = "Generated_Net_Distance_Readability"
        };

        SetMaterialColor(generatedMaterial, overlayColor);
        generatedMaterial.renderQueue = 2450;
        return generatedMaterial;
    }

    private void SetMaterialColor(Material material, Color color)
    {
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
        if (material.HasProperty("_UnlitColor"))
            material.SetColor("_UnlitColor", color);
    }

    private void BuildMesh(Mesh mesh)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();

        float halfSpan = netSpan * 0.5f;

        for (int i = 0; i < verticalLineCount; i++)
        {
            float t = verticalLineCount == 1 ? 0.5f : i / (verticalLineCount - 1f);
            float x = Mathf.Lerp(-halfSpan, halfSpan, t);
            AddVerticalRibbon(vertices, triangles, x);
        }

        for (int i = 0; i < horizontalLineCount; i++)
        {
            float v = (i + 1f) / (horizontalLineCount + 1f);
            AddHorizontalRibbon(vertices, triangles, v);
        }

        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
    }

    private void AddVerticalRibbon(List<Vector3> vertices, List<int> triangles, float x)
    {
        float halfWidth = verticalRibbonWidth * 0.5f;
        float top = TopHeightAtX(x) - 0.055f;
        float bottom = BottomHeightAtX(x);

        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 a = new Vector3(x - halfWidth, bottom, side * depthOffset);
            Vector3 b = new Vector3(x + halfWidth, bottom, side * depthOffset);
            Vector3 c = new Vector3(x + halfWidth, top, side * depthOffset);
            Vector3 d = new Vector3(x - halfWidth, top, side * depthOffset);
            AddDoubleSidedQuad(vertices, triangles, a, b, c, d);
        }
    }

    private void AddHorizontalRibbon(List<Vector3> vertices, List<int> triangles, float normalizedHeight)
    {
        const int segmentCount = 48;
        float halfHeight = horizontalRibbonHeight * 0.5f;
        float halfSpan = netSpan * 0.5f;

        for (int side = -1; side <= 1; side += 2)
        {
            for (int i = 0; i < segmentCount; i++)
            {
                float t0 = i / (float)segmentCount;
                float t1 = (i + 1) / (float)segmentCount;
                float x0 = Mathf.Lerp(-halfSpan, halfSpan, t0);
                float x1 = Mathf.Lerp(-halfSpan, halfSpan, t1);
                float y0 = HeightAtNormalizedNetPoint(x0, normalizedHeight);
                float y1 = HeightAtNormalizedNetPoint(x1, normalizedHeight);
                float z0 = side * depthOffset;
                float z1 = side * depthOffset;

                Vector3 a = new Vector3(x0, y0 - halfHeight, z0);
                Vector3 b = new Vector3(x1, y1 - halfHeight, z1);
                Vector3 c = new Vector3(x1, y1 + halfHeight, z1);
                Vector3 d = new Vector3(x0, y0 + halfHeight, z0);
                AddDoubleSidedQuad(vertices, triangles, a, b, c, d);
            }
        }
    }

    private void AddDoubleSidedQuad(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        int start = vertices.Count;
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(d);

        triangles.Add(start);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
        triangles.Add(start);
        triangles.Add(start + 2);
        triangles.Add(start + 3);

        triangles.Add(start + 2);
        triangles.Add(start + 1);
        triangles.Add(start);
        triangles.Add(start + 3);
        triangles.Add(start + 2);
        triangles.Add(start);
    }

    private float HeightAtNormalizedNetPoint(float x, float normalizedHeight)
    {
        float bottom = BottomHeightAtX(x);
        float top = TopHeightAtX(x) - 0.055f;
        return Mathf.Lerp(bottom, top, normalizedHeight);
    }

    private float TopHeightAtX(float x)
    {
        float ax = Mathf.Abs(x);
        if (ax >= singlesSupportX)
            return postHeight;

        float t = Mathf.Clamp01(ax / Mathf.Max(0.001f, singlesSupportX));
        return centerHeight + (postHeight - centerHeight) * Mathf.Pow(t, 1.85f);
    }

    private float BottomHeightAtX(float x)
    {
        float halfSpan = netSpan * 0.5f;
        float edgeLift = 0.020f * Mathf.Clamp01(Mathf.Abs(x) / Mathf.Max(0.001f, halfSpan));
        return bottomHeight + edgeLift;
    }
}
