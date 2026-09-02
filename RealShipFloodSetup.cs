using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Scene-local setup for testing the canonical FluidSimulator against the imported
/// real ship. It creates static mesh colliders at runtime, configures the existing
/// Fluid.compute asset through FluidSimulator, and provides a reversible transparent
/// hull view for inspecting the water from outside the ship.
/// </summary>
[DefaultExecutionOrder(-1000)]
public sealed class RealShipFloodSetup : MonoBehaviour
{
    [Header("Ship")]
    public GameObject shipRoot;
    public Transform shipStern;
    public Transform seaLevel;
    public bool useSeaLevelTransformForPressure = true;
    public float seaLevelHeightOffset = 3.4f;
    public bool addMeshCollidersOnPlay = true;
    public bool includeAllShipMeshesAsSolid = true;
    public bool makeSternCoverSolid = true;
    public string shipHullTag = "ShipHull";

    [Header("Breach")]
    public Transform breachPoint;
    public bool useExternalBreach = true;
    public float externalWaterLevel = 3f;
    [Min(0.05f)] public float breachWidth = 0.8f;
    [Min(0.05f)] public float breachHeight = 0.8f;
    [Range(0.05f, 1f)] public float breachDischargeCoefficient = 0.62f;
    [Min(0.1f)] public float breachMaxJetSpeed = 8f;

    [Header("Simulation Bounds")]
    public Vector3 boundsMin = new Vector3(-8f, -0.5f, -94f);
    public Vector3 boundsMax = new Vector3(24f, 27f, 18f);
    public ComputeShader fluidCS;

    [Header("Water Surface")]
    public bool createSurfaceTileRenderer = true;
    public ComputeShader surfaceTileCS;
    public Material surfaceTileMaterial;
    public Mesh surfaceTileMesh;
    [Min(1)] public int surfaceTileCountX = 64;
    [Min(1)] public int surfaceTileCountZ = 224;
    [Min(0.05f)] public float surfaceTileSize = 0.5f;

    [Header("Transparent Hull Debug")]
    public bool transparentHullForDebug = false;
    [Range(0.03f, 1f)] public float hullOpacity = 0.18f;
    public bool includeSternInTransparentHull = true;

    private FluidSimulator simulator;
    private SurfaceTileRenderer surfaceRenderer;
    private bool lastTransparentHullState;
    private float lastHullOpacity = -1f;
    private readonly Dictionary<Renderer, Material[]> originalMaterials = new Dictionary<Renderer, Material[]>();

    void Awake()
    {
        ResolveReferences();
        ConfigureShipColliders();
        ConfigureSurfaceRenderer();
        ConfigureSimulator();
        ApplyHullTransparency(true);
    }

    void Update()
    {
        if (transparentHullForDebug != lastTransparentHullState ||
            !Mathf.Approximately(hullOpacity, lastHullOpacity))
        {
            ApplyHullTransparency(false);
        }
    }

    void OnDestroy()
    {
        RestoreOriginalMaterials();
    }

    void ResolveReferences()
    {
        if (shipRoot == null)
        {
            GameObject found = GameObject.Find("TheShipv3");
            if (found != null) shipRoot = found;
        }

        if (shipStern == null)
        {
            GameObject found = GameObject.Find("shipStern");
            if (found != null) shipStern = found.transform;
        }

        if (seaLevel == null)
        {
            GameObject found = GameObject.Find("seaLevel");
            if (found != null) seaLevel = found.transform;
        }

        if (breachPoint == null)
        {
            Transform child = transform.Find("BreachPoint_RealShip");
            if (child != null) breachPoint = child;
        }

        simulator = GetComponent<FluidSimulator>();
        if (simulator == null) simulator = gameObject.AddComponent<FluidSimulator>();

        if (fluidCS == null)
            Debug.LogError("[RealShipFloodSetup] Fluid.compute is not assigned. Assign the canonical Assets/Scripts/Fluid.compute asset.", this);
        if (shipRoot == null)
            Debug.LogError("[RealShipFloodSetup] Could not find TheShipv3. Assign shipRoot explicitly.", this);
        if (seaLevel == null)
        {
            GameObject found = GameObject.Find("seaLevel");
            if (found != null) seaLevel = found.transform;
        }

        if (breachPoint == null)
            Debug.LogError("[RealShipFloodSetup] Could not find BreachPoint_RealShip.", this);
        if (seaLevel == null)
            Debug.LogWarning("[RealShipFloodSetup] Could not find seaLevel; using the manual externalWaterLevel.", this);
    }

    void ConfigureShipColliders()
    {
        if (shipRoot == null) return;

        SetTagSafely(shipRoot, shipHullTag);

        if (addMeshCollidersOnPlay && includeAllShipMeshesAsSolid)
        {
            MeshFilter[] meshFilters = shipRoot.GetComponentsInChildren<MeshFilter>(true);
            int added = 0;
            for (int i = 0; i < meshFilters.Length; i++)
            {
                MeshFilter meshFilter = meshFilters[i];
                if (meshFilter == null || meshFilter.sharedMesh == null) continue;
                if (meshFilter.GetComponent<Collider>() != null) continue;

                MeshCollider meshCollider = meshFilter.gameObject.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = meshFilter.sharedMesh;
                meshCollider.convex = false;
                added++;
            }

            Debug.Log($"[RealShipFloodSetup] Added {added} static mesh colliders under {shipRoot.name}.", this);
        }

        if (shipStern != null)
        {
            SetTagSafely(shipStern.gameObject, shipHullTag);
            if (makeSternCoverSolid)
            {
                Collider[] sternColliders = shipStern.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < sternColliders.Length; i++)
                {
                    if (sternColliders[i] != null) sternColliders[i].isTrigger = false;
                }
            }
        }
    }

    void ConfigureSurfaceRenderer()
    {
        if (!createSurfaceTileRenderer) return;

        surfaceRenderer = FindFirstObjectByType<SurfaceTileRenderer>();
        if (surfaceRenderer == null)
        {
            GameObject waterObject = new GameObject("RealShipWaterTiles");
            waterObject.transform.SetParent(transform, false);
            surfaceRenderer = waterObject.AddComponent<SurfaceTileRenderer>();
        }

        surfaceRenderer.tileCS = surfaceTileCS;
        surfaceRenderer.tileMaterial = surfaceTileMaterial;
        surfaceRenderer.tileMesh = surfaceTileMesh;
        surfaceRenderer.tileCountX = Mathf.Max(1, surfaceTileCountX);
        surfaceRenderer.tileCountZ = Mathf.Max(1, surfaceTileCountZ);
        surfaceRenderer.tileSize = Mathf.Max(0.05f, surfaceTileSize);
        surfaceRenderer.boundsMin = boundsMin;
        surfaceRenderer.boundsMax = boundsMax;
        surfaceRenderer.floorY = boundsMin.y;
        surfaceRenderer.useBulkHeightmapFootprint = true;
        surfaceRenderer.renderAsHeightmapSurface = true;

        if (surfaceTileCS == null || surfaceTileMaterial == null || surfaceTileMesh == null)
            Debug.LogWarning("[RealShipFloodSetup] Surface tile references are incomplete; water splats will still be available, but the connected tile surface may not render.", this);
    }

    void ConfigureSimulator()
    {
        if (simulator == null) return;

        simulator.fluidCS = fluidCS;
        simulator.seaLevel = seaLevel;
        simulator.useSeaLevelTransformForPressure = useSeaLevelTransformForPressure;
        simulator.seaLevelHeightOffset = seaLevelHeightOffset;
        simulator.gpuRenderer = FindFirstObjectByType<GPUFluidRenderer>();
        simulator.surfaceTileRenderer = surfaceRenderer;
        simulator.boundsMin = boundsMin;
        simulator.boundsMax = boundsMax;
        simulator.spawnPoint = breachPoint;
        simulator.continuousSpawn = useExternalBreach;
        simulator.usePrimaryVoxelBreachInflow = useExternalBreach;
        simulator.primaryVoxelExternalWaterLevel = externalWaterLevel;
        simulator.primaryVoxelBreachWidth = breachWidth;
        simulator.primaryVoxelBreachHeight = breachHeight;
        simulator.primaryVoxelBreachDischargeCoefficient = breachDischargeCoefficient;
        simulator.primaryVoxelBreachMaxJetSpeed = breachMaxJetSpeed;
        simulator.usePrimaryVoxelVolume = true;
        simulator.drawPrimaryVoxelCurrentSplats = true;
        simulator.drawPrimaryVoxelUnderwaterCurrentSplats = false;
        simulator.useTaggedShipHullColliders = true;
        simulator.shipHullColliderTag = shipHullTag;
        simulator.autoRebuildSolidCollidersEachFrame = false;
        simulator.disableSurfaceTileRendererForProfiling = false;
    }

    void ApplyHullTransparency(bool force)
    {
        if (!force && transparentHullForDebug == lastTransparentHullState &&
            Mathf.Approximately(hullOpacity, lastHullOpacity)) return;

        if (!transparentHullForDebug)
        {
            RestoreOriginalMaterials();
        }
        else
        {
            RestoreOriginalMaterials();
            ApplyTransparentMaterials(shipRoot != null ? shipRoot.transform : null);
            if (includeSternInTransparentHull && shipStern != null)
                ApplyTransparentMaterials(shipStern);
        }

        lastTransparentHullState = transparentHullForDebug;
        lastHullOpacity = hullOpacity;
    }

    void ApplyTransparentMaterials(Transform root)
    {
        if (root == null) return;
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null) continue;

            if (!originalMaterials.ContainsKey(renderer))
                originalMaterials.Add(renderer, renderer.sharedMaterials);

            Material[] transparentMaterials = new Material[renderer.sharedMaterials.Length];
            for (int m = 0; m < renderer.sharedMaterials.Length; m++)
            {
                Material source = renderer.sharedMaterials[m];
                if (source == null) continue;

                Material copy = new Material(source)
                {
                    name = source.name + " (RealShip Transparent Debug)"
                };
                SetTransparent(copy);
                SetColorAlpha(copy, "_BaseColor");
                SetColorAlpha(copy, "_Color");
                transparentMaterials[m] = copy;
            }
            renderer.sharedMaterials = transparentMaterials;
        }
    }

    void RestoreOriginalMaterials()
    {
        foreach (KeyValuePair<Renderer, Material[]> pair in originalMaterials)
        {
            if (pair.Key != null) pair.Key.sharedMaterials = pair.Value;
        }
        originalMaterials.Clear();
    }

    static void SetTransparent(Material material)
    {
        if (material == null) return;
        SetFloatIfPresent(material, "_SurfaceType", 1f);
        SetFloatIfPresent(material, "_BlendMode", 0f);
        SetFloatIfPresent(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
        SetFloatIfPresent(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        SetFloatIfPresent(material, "_ZWrite", 0f);
        SetFloatIfPresent(material, "_AlphaCutoffEnable", 0f);
        material.renderQueue = (int)RenderQueue.Transparent;
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
    }

    static void SetFloatIfPresent(Material material, string property, float value)
    {
        if (material.HasProperty(property)) material.SetFloat(property, value);
    }

    void SetColorAlpha(Material material, string property)
    {
        if (!material.HasProperty(property)) return;
        Color color = material.GetColor(property);
        color.a = hullOpacity;
        material.SetColor(property, color);
    }

    static void SetTagSafely(GameObject target, string tag)
    {
        if (target == null || string.IsNullOrEmpty(tag)) return;
        try { target.tag = tag; }
        catch (UnityException) { Debug.LogWarning($"[RealShipFloodSetup] Tag '{tag}' is not defined; collider collection will not use it.", target); }
    }

    void OnDrawGizmosSelected()
    {
        if (seaLevel == null)
        {
            GameObject found = GameObject.Find("seaLevel");
            if (found != null) seaLevel = found.transform;
        }

        if (breachPoint == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(breachPoint.position, new Vector3(breachWidth, breachHeight, 0.25f));
        Gizmos.DrawLine(breachPoint.position, breachPoint.position + breachPoint.forward * 1.5f);
    }
}
