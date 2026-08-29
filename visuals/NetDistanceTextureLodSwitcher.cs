using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
[DisallowMultipleComponent]
public class NetDistanceTextureLodSwitcher : MonoBehaviour
{
    [Header("Camera")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private float farLodDistance = 24f;
    [SerializeField] private float hysteresis = 2f;

    [Header("Official Net Shape")]
    [SerializeField] private float netSpan = 12.801f;
    [SerializeField] private float postHeight = 1.07f;
    [SerializeField] private float centerHeight = 0.914f;
    [SerializeField] private float singlesSupportX = 5.029f;
    [SerializeField] private float bottomHeight = 0.035f;
    [SerializeField] private float topTapeInset = 0.070f;
    [SerializeField] private float cardDepthOffset = 0.018f;
    [SerializeField] private int meshSegments = 48;

    [Header("Texture Net")]
    [SerializeField] private int textureWidth = 2048;
    [SerializeField] private int textureHeight = 256;
    [SerializeField] private int verticalLineCount = 64;
    [SerializeField] private int horizontalLineCount = 10;
    [SerializeField] private int verticalLinePixels = 1;
    [SerializeField] private int horizontalLinePixels = 1;
    [SerializeField] private Color lineColor = new Color(0.010f, 0.015f, 0.006f, 0.16f);
    [SerializeField] private Color backgroundColor = new Color(0.006f, 0.010f, 0.004f, 0.055f);
    [SerializeField] private float mipMapBias = 0.25f;

    [Header("Renderer Matching")]
    [SerializeField] private bool autoRefreshRenderers = true;
    [SerializeField] private string[] detailedGridNameTokens =
    {
        "net_unity_merged_round_cords",
        "net_unity_merged_distance_ribbons",
        "net_unity_merged_knots",
        "net_vertical_cord_",
        "net_horizontal_cord_",
        "net_bottom_edge_cord",
        "net_distance_",
        "net_knot_"
    };
    [SerializeField] private string[] excludedNameTokens =
    {
        "white",
        "tape",
        "strap",
        "wood",
        "post",
        "stick",
        "metal",
        "anchor",
        "foot",
        "lod",
        "alpha"
    };

    private const string AlphaCardChildName = "Net Distance Alpha Card";

    private readonly List<Renderer> detailedGridRenderers = new List<Renderer>();
    private MeshFilter alphaCardMeshFilter;
    private MeshRenderer alphaCardRenderer;
    private Mesh alphaCardMesh;
    private Texture2D alphaTexture;
    private Material alphaMaterial;
    private bool usingFarLod;

    private void OnEnable()
    {
        Rebuild();
        ApplyLodState(false);
    }

    private void OnValidate()
    {
        farLodDistance = Mathf.Max(0.1f, farLodDistance);
        hysteresis = Mathf.Max(0f, hysteresis);
        netSpan = Mathf.Max(0.1f, netSpan);
        postHeight = Mathf.Max(centerHeight, postHeight);
        centerHeight = Mathf.Max(0.1f, centerHeight);
        singlesSupportX = Mathf.Max(0.1f, singlesSupportX);
        bottomHeight = Mathf.Max(0f, bottomHeight);
        topTapeInset = Mathf.Max(0f, topTapeInset);
        cardDepthOffset = Mathf.Max(0.001f, cardDepthOffset);
        meshSegments = Mathf.Clamp(meshSegments, 4, 160);
        textureWidth = Mathf.Clamp(textureWidth, 64, 4096);
        textureHeight = Mathf.Clamp(textureHeight, 32, 1024);
        verticalLineCount = Mathf.Clamp(verticalLineCount, 3, 160);
        horizontalLineCount = Mathf.Clamp(horizontalLineCount, 1, 40);
        verticalLinePixels = Mathf.Clamp(verticalLinePixels, 1, 24);
        horizontalLinePixels = Mathf.Clamp(horizontalLinePixels, 1, 24);
        mipMapBias = Mathf.Clamp(mipMapBias, -1f, 2f);

        Rebuild();
        UpdateLod();
    }

    private void LateUpdate()
    {
        UpdateLod();
    }

    [ContextMenu("Rebuild Net Texture LOD")]
    public void Rebuild()
    {
        EnsureAlphaCardObjects();
        RefreshDetailedRenderers();
        BuildAlphaMesh();
        BuildAlphaTexture();
        ConfigureMaterial();
    }

    [ContextMenu("Apply Broadcast-Style Net Texture Preset")]
    public void ApplyBroadcastStylePreset()
    {
        farLodDistance = 24f;
        textureWidth = 2048;
        textureHeight = 256;
        verticalLineCount = 64;
        horizontalLineCount = 10;
        verticalLinePixels = 1;
        horizontalLinePixels = 1;
        lineColor = new Color(0.010f, 0.015f, 0.006f, 0.16f);
        backgroundColor = new Color(0.006f, 0.010f, 0.004f, 0.055f);
        mipMapBias = 0.25f;
        Rebuild();
        UpdateLod();
    }

    [ContextMenu("Apply Anti-Shimmer Net Texture Preset")]
    public void ApplyAntiShimmerPreset()
    {
        farLodDistance = 22f;
        textureWidth = 1024;
        textureHeight = 128;
        verticalLineCount = 32;
        horizontalLineCount = 5;
        verticalLinePixels = 1;
        horizontalLinePixels = 1;
        lineColor = new Color(0.010f, 0.015f, 0.006f, 0.10f);
        backgroundColor = new Color(0.006f, 0.010f, 0.004f, 0.070f);
        mipMapBias = 1.15f;
        Rebuild();
        UpdateLod();
    }

    [ContextMenu("Refresh Detailed Net Renderers")]
    public void RefreshDetailedRenderers()
    {
        detailedGridRenderers.Clear();

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || renderer == alphaCardRenderer)
                continue;

            string searchText = BuildSearchText(renderer);
            if (ContainsAnyToken(searchText, excludedNameTokens))
                continue;

            if (ContainsAnyToken(searchText, detailedGridNameTokens))
                detailedGridRenderers.Add(renderer);
        }
    }

    private void EnsureAlphaCardObjects()
    {
        Transform child = transform.Find(AlphaCardChildName);
        if (child == null)
        {
            GameObject card = new GameObject(AlphaCardChildName);
            card.transform.SetParent(transform, false);
            child = card.transform;
        }

        alphaCardMeshFilter = child.GetComponent<MeshFilter>();
        if (alphaCardMeshFilter == null)
            alphaCardMeshFilter = child.gameObject.AddComponent<MeshFilter>();

        alphaCardRenderer = child.GetComponent<MeshRenderer>();
        if (alphaCardRenderer == null)
            alphaCardRenderer = child.gameObject.AddComponent<MeshRenderer>();

        alphaCardRenderer.shadowCastingMode = ShadowCastingMode.Off;
        alphaCardRenderer.receiveShadows = false;
        alphaCardRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        alphaCardRenderer.lightProbeUsage = LightProbeUsage.Off;
        alphaCardRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    private void BuildAlphaMesh()
    {
        if (alphaCardMesh == null)
            alphaCardMesh = new Mesh { name = "Net Distance Alpha Card Mesh" };

        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();

        float halfSpan = netSpan * 0.5f;

        for (int i = 0; i <= meshSegments; i++)
        {
            float t = i / (float)meshSegments;
            float x = Mathf.Lerp(-halfSpan, halfSpan, t);
            float bottom = BottomHeightAtX(x);
            float top = Mathf.Max(bottom + 0.05f, TopHeightAtX(x) - topTapeInset);

            vertices.Add(new Vector3(x, bottom, -cardDepthOffset));
            vertices.Add(new Vector3(x, top, -cardDepthOffset));
            vertices.Add(new Vector3(x, bottom, cardDepthOffset));
            vertices.Add(new Vector3(x, top, cardDepthOffset));

            uvs.Add(new Vector2(t, 0f));
            uvs.Add(new Vector2(t, 1f));
            uvs.Add(new Vector2(t, 0f));
            uvs.Add(new Vector2(t, 1f));
        }

        for (int i = 0; i < meshSegments; i++)
        {
            int a = i * 4;
            int b = a + 4;

            AddDoubleSidedQuad(triangles, a, b, b + 1, a + 1);
            AddDoubleSidedQuad(triangles, a + 2, a + 3, b + 3, b + 2);
        }

        alphaCardMesh.Clear();
        alphaCardMesh.SetVertices(vertices);
        alphaCardMesh.SetUVs(0, uvs);
        alphaCardMesh.SetTriangles(triangles, 0);
        alphaCardMesh.RecalculateBounds();
        alphaCardMesh.RecalculateNormals();

        alphaCardMeshFilter.sharedMesh = alphaCardMesh;
    }

    private void AddDoubleSidedQuad(List<int> triangles, int a, int b, int c, int d)
    {
        triangles.Add(a);
        triangles.Add(b);
        triangles.Add(c);
        triangles.Add(a);
        triangles.Add(c);
        triangles.Add(d);

        triangles.Add(c);
        triangles.Add(b);
        triangles.Add(a);
        triangles.Add(d);
        triangles.Add(c);
        triangles.Add(a);
    }

    private void BuildAlphaTexture()
    {
        if (alphaTexture == null || alphaTexture.width != textureWidth || alphaTexture.height != textureHeight)
        {
            alphaTexture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, true)
            {
                name = "Generated_Net_Distance_Alpha_Texture",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
                mipMapBias = mipMapBias
            };
        }
        else
        {
            alphaTexture.mipMapBias = mipMapBias;
        }

        Color[] pixels = new Color[textureWidth * textureHeight];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = backgroundColor;

        for (int i = 0; i < verticalLineCount; i++)
        {
            int x = Mathf.RoundToInt(i * (textureWidth - 1f) / Mathf.Max(1, verticalLineCount - 1));
            DrawVerticalLine(pixels, x, verticalLinePixels);
        }

        for (int i = 0; i < horizontalLineCount; i++)
        {
            int y = Mathf.RoundToInt((i + 1f) * (textureHeight - 1f) / (horizontalLineCount + 1f));
            DrawHorizontalLine(pixels, y, horizontalLinePixels);
        }

        alphaTexture.SetPixels(pixels);
        alphaTexture.Apply(true, false);
    }

    private void DrawVerticalLine(Color[] pixels, int centerX, int width)
    {
        int half = width / 2;
        for (int x = centerX - half; x <= centerX + half; x++)
        {
            if (x < 0 || x >= textureWidth)
                continue;

            for (int y = 0; y < textureHeight; y++)
                pixels[y * textureWidth + x] = lineColor;
        }
    }

    private void DrawHorizontalLine(Color[] pixels, int centerY, int height)
    {
        int half = height / 2;
        for (int y = centerY - half; y <= centerY + half; y++)
        {
            if (y < 0 || y >= textureHeight)
                continue;

            for (int x = 0; x < textureWidth; x++)
                pixels[y * textureWidth + x] = lineColor;
        }
    }

    private void ConfigureMaterial()
    {
        if (alphaMaterial == null)
        {
            Shader shader = Shader.Find("HDRP/Unlit");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Transparent");
            if (shader == null)
                shader = Shader.Find("Standard");

            alphaMaterial = new Material(shader)
            {
                name = "Generated_Net_Distance_Alpha_Card"
            };
        }

        SetTextureIfPresent(alphaMaterial, "_BaseColorMap", alphaTexture);
        SetTextureIfPresent(alphaMaterial, "_MainTex", alphaTexture);
        SetColorIfPresent(alphaMaterial, "_BaseColor", Color.white);
        SetColorIfPresent(alphaMaterial, "_Color", Color.white);
        SetColorIfPresent(alphaMaterial, "_UnlitColor", Color.white);

        SetFloatIfPresent(alphaMaterial, "_SurfaceType", 1f);
        SetFloatIfPresent(alphaMaterial, "_BlendMode", 0f);
        SetFloatIfPresent(alphaMaterial, "_AlphaCutoffEnable", 0f);
        SetFloatIfPresent(alphaMaterial, "_TransparentCullMode", 0f);
        SetFloatIfPresent(alphaMaterial, "_CullMode", 0f);
        SetFloatIfPresent(alphaMaterial, "_DoubleSidedEnable", 1f);
        SetFloatIfPresent(alphaMaterial, "_ZWrite", 0f);
        SetFloatIfPresent(alphaMaterial, "_EnableFogOnTransparent", 0f);

        alphaMaterial.renderQueue = 3000;
        alphaMaterial.SetOverrideTag("RenderType", "Transparent");
        alphaMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        alphaMaterial.EnableKeyword("_ALPHABLEND_ON");
        alphaMaterial.DisableKeyword("_ALPHATEST_ON");
        alphaMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        alphaMaterial.enableInstancing = true;

        alphaCardRenderer.sharedMaterial = alphaMaterial;
    }

    private void UpdateLod()
    {
        if (autoRefreshRenderers && detailedGridRenderers.Count == 0)
            RefreshDetailedRenderers();

        Camera cameraToUse = targetCamera != null ? targetCamera : Camera.main;
        if (cameraToUse == null)
            return;

        float distance = Vector3.Distance(cameraToUse.transform.position, transform.position);
        bool shouldUseFar = usingFarLod
            ? distance > Mathf.Max(0.1f, farLodDistance - hysteresis)
            : distance >= farLodDistance;

        if (shouldUseFar != usingFarLod)
            ApplyLodState(shouldUseFar);
    }

    private void ApplyLodState(bool useFar)
    {
        usingFarLod = useFar;

        foreach (Renderer renderer in detailedGridRenderers)
        {
            if (renderer != null)
                renderer.enabled = !useFar;
        }

        if (alphaCardRenderer != null)
            alphaCardRenderer.enabled = useFar;
    }

    private float TopHeightAtX(float x)
    {
        float ax = Mathf.Abs(x);
        if (ax >= singlesSupportX)
            return postHeight;

        float t = Mathf.Clamp01(ax / singlesSupportX);
        return centerHeight + (postHeight - centerHeight) * Mathf.Pow(t, 1.85f);
    }

    private float BottomHeightAtX(float x)
    {
        float halfSpan = netSpan * 0.5f;
        return bottomHeight + 0.020f * Mathf.Clamp01(Mathf.Abs(x) / halfSpan);
    }

    private string BuildSearchText(Renderer renderer)
    {
        string text = renderer.name + " " + renderer.gameObject.name;
        Transform parent = renderer.transform.parent;
        while (parent != null && parent != transform)
        {
            text += " " + parent.name;
            parent = parent.parent;
        }

        foreach (Material material in renderer.sharedMaterials)
        {
            if (material != null)
                text += " " + material.name;
        }

        return text.ToLowerInvariant();
    }

    private bool ContainsAnyToken(string searchText, string[] tokens)
    {
        foreach (string token in tokens)
        {
            if (!string.IsNullOrWhiteSpace(token) && searchText.Contains(token.ToLowerInvariant()))
                return true;
        }

        return false;
    }

    private void SetTextureIfPresent(Material material, string propertyName, Texture texture)
    {
        if (material.HasProperty(propertyName))
            material.SetTexture(propertyName, texture);
    }

    private void SetColorIfPresent(Material material, string propertyName, Color color)
    {
        if (material.HasProperty(propertyName))
            material.SetColor(propertyName, color);
    }

    private void SetFloatIfPresent(Material material, string propertyName, float value)
    {
        if (material.HasProperty(propertyName))
            material.SetFloat(propertyName, value);
    }
}
