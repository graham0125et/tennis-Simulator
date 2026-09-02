using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// SUMMARY
// Optional debug renderer for the raw GPU fluid particles.
//
// It draws:
// - a tiny solid marker at each particle position, which is the particle "point"
// - an optional wire sphere showing the SPH smoothing/influence radius
//
// This is visual-only. It does not affect the simulation.
// ============================================================================
public class GPUFluidRenderer : MonoBehaviour
{
    const string DebugShaderName = "Custom/GPUFluidDebug";

    [Header("Scene Object Setup")]
    [Tooltip("Disable any normal MeshRenderer on this GameObject. The debug view is drawn procedurally from the GPU particle buffer.")]
    public bool disableAttachedMeshRenderer = true;

    [Header("Particle Centers")]
    public bool drawParticleCenters = true;
    [Min(0.001f)] public float pointRadius = 0.05f;
    public Color activePointColor = new Color(0.1f, 0.45f, 1f, 0.9f);
    public Color dormantPointColor = new Color(1f, 0.65f, 0.15f, 0.55f);
    [Min(1)] public int maxCenterParticles = 100000;

    [Header("Influence Radius")]
    public bool drawInfluenceRadius = false;
    public bool useSmoothingRadius = true;
    [Min(0.001f)] public float manualInfluenceRadius = 0.625f;
    public Color influenceColor = new Color(0.8f, 0.95f, 1f, 0.22f);
    [Min(1)] public int maxInfluenceParticles = 3000;
    [Range(8, 96)] public int wireSphereSegments = 32;

    [Header("Filtering")]
    public bool showDormantParticles = true;
    public bool sprayOnly = false;
    public float sprayMinSpeed = 1.5f;
    public float sprayMinHeightAboveSurface = 0.35f;

    [Header("Optional Mesh Overrides")]
    public Mesh centerMeshOverride;
    public Mesh influenceWireMeshOverride;

    [Header("Optional Material Overrides")]
    public Material centerMaterialOverride;
    public Material influenceMaterialOverride;

    [Header("Legacy Fields")]
    [Tooltip("Legacy center mesh slot from the old renderer. Used only as the center mesh if assigned.")]
    public Mesh mesh;
    [Tooltip("Legacy material slot from the old renderer. Only used if it already uses Custom/GPUFluidDebug.")]
    public Material material;

    // Set by FluidSimulator at runtime.
    [HideInInspector] public ComputeBuffer particleBuffer;
    [HideInInspector] public int activeParticles;
    [HideInInspector] public float particleRadius = 0.3125f;
    [HideInInspector] public float smoothingRadius = 0.625f;
    [HideInInspector] public Vector3 boundsMin = new Vector3(-20f, -2f, -20f);
    [HideInInspector] public Vector3 boundsMax = new Vector3(20f, 6f, 20f);
    [HideInInspector] public float mainBodySurfaceY = -2f;

    Mesh generatedCenterMesh;
    Mesh generatedWireSphereMesh;
    Material generatedCenterMaterial;
    Material generatedInfluenceMaterial;
    ComputeBuffer fallbackParticleBuffer;
    int cachedWireSphereSegments = -1;

    const int ParticleStride = sizeof(float) * 12;

    void OnEnable()
    {
        DisableNormalRendererIfNeeded();
    }

    void OnValidate()
    {
        DisableNormalRendererIfNeeded();
    }

    void Update()
    {
        DisableNormalRendererIfNeeded();

        bool hasNormalParticles = particleBuffer != null && activeParticles > 0;
        if (!hasNormalParticles)
            return;

        EnsureResources();

        float boundsPadding = Mathf.Max(smoothingRadius * 4f, 4f);
        Bounds drawBounds = new Bounds(
            (boundsMin + boundsMax) * 0.5f,
            (boundsMax - boundsMin) + Vector3.one * boundsPadding);

        if (hasNormalParticles && drawParticleCenters)
        {
            Mesh drawMesh = GetCenterMesh();
            Material drawMaterial = GetCenterMaterial();
            if (drawMesh != null && drawMaterial != null)
            {
                BindNormalParticleMaterial(drawMaterial, activePointColor, dormantPointColor, pointRadius);
                Graphics.DrawMeshInstancedProcedural(
                    drawMesh,
                    0,
                    drawMaterial,
                    drawBounds,
                    Mathf.Min(activeParticles, maxCenterParticles));
            }
        }

        if (hasNormalParticles && drawInfluenceRadius)
        {
            Mesh drawMesh = GetWireSphereMesh();
            Material drawMaterial = GetInfluenceMaterial();
            if (drawMesh != null && drawMaterial != null)
            {
                float radius = useSmoothingRadius ? smoothingRadius : manualInfluenceRadius;
                BindNormalParticleMaterial(drawMaterial, influenceColor, influenceColor, radius);
                Graphics.DrawMeshInstancedProcedural(
                    drawMesh,
                    0,
                    drawMaterial,
                    drawBounds,
                    Mathf.Min(activeParticles, maxInfluenceParticles));
            }
        }
    }

    void BindNormalParticleMaterial(Material target, Color activeColor, Color dormantColor, float scale)
    {
        target.SetBuffer("particles", particleBuffer);
        BindCommonMaterialValues(target, activeColor, dormantColor, scale);
    }

    void BindCommonMaterialValues(Material target, Color activeColor, Color dormantColor, float scale)
    {
        target.SetColor("_Color", activeColor);
        target.SetColor("_DormantColor", dormantColor);
        target.SetFloat("_Scale", Mathf.Max(scale, 0.0001f));
        target.SetFloat("_ShowDormant", showDormantParticles ? 1f : 0f);
        target.SetFloat("_SprayOnly", sprayOnly ? 1f : 0f);
        target.SetFloat("_SprayMinSpeed", sprayMinSpeed);
        target.SetFloat("_SpraySurfaceY", mainBodySurfaceY);
        target.SetFloat("_SprayHeightAboveSurface", sprayMinHeightAboveSurface);
    }

    void EnsureResources()
    {
        if (generatedCenterMesh == null)
            generatedCenterMesh = CreateOctahedronMesh();

        if (generatedWireSphereMesh == null || cachedWireSphereSegments != wireSphereSegments)
        {
            DestroyGenerated(generatedWireSphereMesh);
            generatedWireSphereMesh = CreateWireSphereMesh(wireSphereSegments);
            cachedWireSphereSegments = wireSphereSegments;
        }

        EnsureFallbackBuffers();

        Shader debugShader = Shader.Find(DebugShaderName);
        if (debugShader == null)
            return;

        if (generatedCenterMaterial == null)
        {
            generatedCenterMaterial = new Material(debugShader)
            {
                name = "Generated GPU Particle Debug Center",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        if (generatedInfluenceMaterial == null)
        {
            generatedInfluenceMaterial = new Material(debugShader)
            {
                name = "Generated GPU Particle Debug Influence",
                hideFlags = HideFlags.HideAndDontSave
            };
        }
    }

    Mesh GetCenterMesh()
    {
        if (centerMeshOverride != null)
            return centerMeshOverride;
        if (mesh != null)
            return mesh;
        return generatedCenterMesh;
    }

    Mesh GetWireSphereMesh()
    {
        if (influenceWireMeshOverride != null)
            return influenceWireMeshOverride;
        return generatedWireSphereMesh;
    }

    Material GetCenterMaterial()
    {
        if (centerMaterialOverride != null)
            return centerMaterialOverride;
        if (material != null && material.shader != null && material.shader.name == DebugShaderName)
            return material;
        return generatedCenterMaterial;
    }

    Material GetInfluenceMaterial()
    {
        if (influenceMaterialOverride != null)
            return influenceMaterialOverride;
        return generatedInfluenceMaterial;
    }

    void EnsureFallbackBuffers()
    {
        if (fallbackParticleBuffer == null)
        {
            fallbackParticleBuffer = new ComputeBuffer(1, ParticleStride);
            fallbackParticleBuffer.SetData(new[] { new ParticleFallback() });
        }
    }

    static Mesh CreateOctahedronMesh()
    {
        Mesh m = new Mesh { name = "Generated Particle Point Octahedron" };
        m.hideFlags = HideFlags.HideAndDontSave;

        Vector3[] vertices =
        {
            new Vector3(0f, 1f, 0f),
            new Vector3(1f, 0f, 0f),
            new Vector3(0f, 0f, 1f),
            new Vector3(-1f, 0f, 0f),
            new Vector3(0f, 0f, -1f),
            new Vector3(0f, -1f, 0f)
        };

        int[] triangles =
        {
            0, 1, 2,
            0, 2, 3,
            0, 3, 4,
            0, 4, 1,
            5, 2, 1,
            5, 3, 2,
            5, 4, 3,
            5, 1, 4
        };

        m.vertices = vertices;
        m.triangles = triangles;
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    static Mesh CreateWireSphereMesh(int segments)
    {
        int safeSegments = Mathf.Max(8, segments);
        Mesh m = new Mesh { name = "Generated Particle Influence Wire Sphere" };
        m.hideFlags = HideFlags.HideAndDontSave;

        List<Vector3> vertices = new List<Vector3>(safeSegments * 6);
        List<int> indices = new List<int>(safeSegments * 6);

        AddCircle(vertices, indices, safeSegments, 0);
        AddCircle(vertices, indices, safeSegments, 1);
        AddCircle(vertices, indices, safeSegments, 2);

        m.SetVertices(vertices);
        m.SetIndices(indices.ToArray(), MeshTopology.Lines, 0);
        m.RecalculateBounds();
        return m;
    }

    void DisableNormalRendererIfNeeded()
    {
        if (!disableAttachedMeshRenderer)
            return;

        MeshRenderer attachedRenderer = GetComponent<MeshRenderer>();
        if (attachedRenderer != null && attachedRenderer.enabled)
            attachedRenderer.enabled = false;
    }

    static void AddCircle(List<Vector3> vertices, List<int> indices, int segments, int plane)
    {
        int start = vertices.Count;
        for (int i = 0; i < segments; i++)
        {
            float t = (i / (float)segments) * Mathf.PI * 2f;
            float a = Mathf.Cos(t);
            float b = Mathf.Sin(t);

            if (plane == 0)
                vertices.Add(new Vector3(a, b, 0f));
            else if (plane == 1)
                vertices.Add(new Vector3(a, 0f, b));
            else
                vertices.Add(new Vector3(0f, a, b));
        }

        for (int i = 0; i < segments; i++)
        {
            indices.Add(start + i);
            indices.Add(start + ((i + 1) % segments));
        }
    }

    void OnDestroy()
    {
        DestroyGenerated(generatedCenterMesh);
        DestroyGenerated(generatedWireSphereMesh);
        DestroyGenerated(generatedCenterMaterial);
        DestroyGenerated(generatedInfluenceMaterial);
        ReleaseBuffer(ref fallbackParticleBuffer);
    }

    static void DestroyGenerated(Object obj)
    {
        if (obj == null)
            return;

        if (Application.isPlaying)
            Destroy(obj);
        else
            DestroyImmediate(obj);
    }

    static void ReleaseBuffer(ref ComputeBuffer buffer)
    {
        if (buffer == null)
            return;

        buffer.Release();
        buffer = null;
    }

    struct ParticleFallback
    {
        public Vector3 pos;
        public Vector3 vel;
        public float invMass;
        public float density;
        public int state;
        public float sleepTimer;
        public Vector2 padding;
    }

    struct SurfaceParticleFallback
    {
        public Vector3 pos;
        public float life;
        public Vector3 vel;
        public int active;
    }
}
