using UnityEngine;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine.Rendering;

public class SurfaceTileRenderer : MonoBehaviour
{
    [Header("Tile Grid")]
    public int tileCountX = 40;
    public int tileCountZ = 40;
    public float tileSize = 1.0f;

    [Header("Rendering")]
    public Material tileMaterial;
    public Mesh tileMesh;
    public bool useGeneratedDepthMesh = true;
    [Header("Detached Spray")]
    public bool drawDetachedSpray = true;
    public Shader sprayShader;
    [Range(0.2f, 2f)] public float sprayBrightness = 1f;
    [Range(0.1f, 1f)] public float sprayOpacity = 0.85f;

    [Header("Heightmap Surface")]
    public bool renderAsHeightmapSurface = true;
    [Range(1, 8)] public int heightmapVertexStep = 1;
    [Tooltip("Coverage iso-level used as the visible moving water boundary. Fractional coverage below it remains a hidden transport field instead of translucent water.")]
    [InspectorName("Heightmap Coverage Contour")]
    [Range(0.05f, 0.9f)] public float heightmapEdgeFade = 0.35f;
    [Tooltip("Minimum antialiasing width around the coverage contour. Keep this narrow so the interior reads as solid water rather than a brightness fade.")]
    [Range(0.005f, 0.1f)] public float heightmapContourSoftness = 0.02f;
    [Range(0f, 8f)] public float heightmapNormalStrength = 3f;
    [Range(0f, 1f)] public float heightmapSurfaceVisibility = 0.45f;
    [Range(0f, 1f)] public float heightmapShallowVisibility = 0.65f;
    [Header("Liquid Body Microcells")]
    [Tooltip("Keeps the height-map mesh as one connected liquid body while adding a fine world-space microcell cue in its shading.")]
    public bool useHeightmapLiquidMicrocells = true;
    [Range(0.65f, 1f)] public float heightmapLiquidOpacity = 0.93f;
    [Tooltip("Fine visual cell frequency per world metre. This is material detail, not a new simulation grid.")]
    [Range(1f, 32f)] public float heightmapMicrocellScale = 8f;
    [Range(0f, 0.03f)] public float heightmapMicrocellHeight = 0.006f;
    [Range(0f, 2f)] public float heightmapMicrocellNormalStrength = 0.32f;
    [Range(0f, 0.35f)] public float heightmapMicrocellEdgeStrength = 0.055f;

    [Header("Active Visual Ripples")]
    [Tooltip("Flow speed at which extra visual ripple detail begins. This never changes coverage or water volume.")]
    [Range(0f, 2f)] public float activeRippleFlowStart = 0.08f;
    [Range(0.01f, 3f)] public float activeRippleFlowFull = 0.55f;
    [Tooltip("Displayed wave displacement at which extra visual ripple detail begins.")]
    [Range(0f, 0.25f)] public float activeRippleWaveStart = 0.015f;
    [Range(0.005f, 0.5f)] public float activeRippleWaveFull = 0.09f;
    [Range(0f, 2f)] public float activeRippleNormalStrength = 0.62f;
    [Range(0.2f, 16f)] public float activeRippleScale = 3.4f;
    [Range(0f, 5f)] public float activeRippleSpeed = 0.9f;
    [Header("Continuous Surface Field")]
    [Tooltip("Response speed for temporally smoothed surface height and thickness.")]
    [Range(0f, 30f)] public float surfaceFieldHeightResponse = 8f;
    [Tooltip("Response speed when continuous wet coverage grows.")]
    [Range(0f, 30f)] public float surfaceFieldCoverageRiseResponse = 10f;
    [Tooltip("Response speed when continuous wet coverage recedes.")]
    [Range(0f, 30f)] public float surfaceFieldCoverageFallResponse = 3f;
    [Tooltip("Response speed for the continuous flow field.")]
    [Range(0f, 30f)] public float surfaceFieldFlowResponse = 6f;
    [Tooltip("Suppresses extremely weak reconstruction weights while preserving a soft edge.")]
    [Range(0f, 0.25f)] public float surfaceFieldMinimumCoverage = 0.01f;
    [Tooltip("Scales how quickly authoritative voxel flow hands wet coverage from one fine subvoxel surface cell to the next.")]
    [Range(0f, 1f)] public float surfaceFieldAdvectionStrength = 1f;
    [Tooltip("Legacy safety ceiling for render-only front travel during one frame, measured in fine heightmap cells.")]
    [Range(0.25f, 4f)] public float surfaceFieldMaximumAdvectionCells = 3.25f;
    [Tooltip("Hard limit on sequential subvoxel-circle advancement in one rendered frame. One means a flow chain cannot skip over a fine surface cell even during a slow frame.")]
    [Range(0.25f, 1f)] public float surfaceFieldMaximumMicrocellAdvancePerFrame = 1f;
    [Tooltip("Maximum authoritative voxel-mass correction per second. Advection remains the primary footprint motion; this only prevents long-term area drift.")]
    [Range(0.25f, 10f)] public float surfaceFieldCoverageMaximumSpeed = 2.5f;
    [Tooltip("How quickly voxel fill may add missing coverage after the existing footprint has been advected. Lower values make transport dominate over reconstruction.")]
    [Range(0f, 10f)] public float surfaceFieldVoxelMassCorrectionResponse = 1.5f;
    [Tooltip("Adds flow-speed-dependent support only where wet coverage has actually arrived from upstream. This makes fast parts of the front enter a new voxel before slow parts instead of reconstructing the whole voxel together.")]
    [Range(0f, 2f)] public float surfaceFieldFlowDrivenFrontStrength = 0.45f;
    [Tooltip("Very slow fallback used to seed genuinely isolated new water that has no upstream surface history. Keep this below the flow-driven response so ordinary fronts visibly travel rather than light up in place.")]
    [Range(0f, 2f)] public float surfaceFieldIsolatedSeedResponse = 0.25f;
    [Tooltip("Slow same-height continuity repair. It advances only from an already visible neighbouring microcell and remains clamped by authoritative voxel coverage, closing seams without lighting up isolated voxel circles.")]
    [Range(0f, 5f)] public float surfaceFieldContinuityRepairResponse = 1.2f;
    [Tooltip("Allow disconnected water with no upstream fine-cell donor to seed itself. Leave disabled for normal flooding so coarse voxel circles can never appear in place.")]
    public bool surfaceFieldAllowIsolatedSeeding = false;
    [Tooltip("How quickly coverage unsupported by the authoritative voxel map is removed after transport.")]
    [Range(0f, 10f)] public float surfaceFieldVoxelMassReleaseResponse = 2f;
    [Tooltip("Maximum render-field timestep. A stalled frame cannot force coverage, height, or advection to catch up as one visible jump.")]
    [Range(0.016f, 0.1f)] public float surfaceFieldMaximumDeltaTime = 0.05f;
    [Tooltip("Number of initial surface updates allowed to seed the already-existing authoritative water body. This closes permanently before normal flow-front propagation begins.")]
    [Range(2, 30)] public int surfaceFieldInitialSeedFrames = 12;

    [Header("Authoritative Voxel Surface Reconstruction")]
    [Tooltip("Build the visible height field directly from every exposed authoritative wet voxel. Render-only: it never changes transport, pressure, or hatch flow.")]
    public bool useFractionalPrimaryVoxelSurface = true;
    [Range(0.00001f, 0.25f)] public float primaryVoxelSurfaceMinimumFill = 0.00001f;
    [Tooltip("Fill range above the wet threshold over which a new circular source grows from zero strength. This removes the first-frame coverage burst without changing voxel mass.")]
    [Range(0.01f, 0.25f)] public float primaryVoxelSurfaceCoverageFadeFill = 0.08f;
    [Tooltip("Radius in authoritative-voxel cells of each hidden circular influence written into the surface map. Overlapping influences merge into one continuous rendered skin; no circles are drawn as geometry or shader dots.")]
    [Range(0.5f, 2f)] public float primaryVoxelSurfaceLateralBlendRadius = 1.25f;
    [Tooltip("Minimum open vertical gap, in primary voxel layers, required before two water bodies in one unobstructed column receive separate render surfaces.")]
    [Range(1, 4)] public int primaryVoxelSurfaceMinimumGapLayers = 2;
    [Tooltip("Use a faster height response while still obeying the smooth-motion speed limit. Disable for the softest ease-in/ease-out motion.")]
    public bool primaryVoxelSurfaceImmediateVerticalFollow = true;
    [Tooltip("Maximum vertical speed of the rendered surface in metres per second. Newly wet points start at the correct voxel top while still fading in, so they never rise visibly from the floor.")]
    [Range(0.1f, 6f)] public float primaryVoxelSurfaceMaximumVerticalSpeed = 1.5f;
    [Tooltip("Visual-only maximum rise/fall speed for the direct voxel-fill base. The target remains this voxel's own fill height; this prevents discrete voxel transfers from appearing as instant vertical surface jumps.")]
    [Range(0.05f, 3f)] public float primaryVoxelSurfaceVisualBaseMaximumVerticalSpeed = 0.5f;

    [Header("Visual Base Diagnostic")]
    [Tooltip("Logs the raw voxel-fill base target beside the capped rendered base. Read-only: it does not alter the surface.")]
    public bool logPrimaryVoxelSurfaceVisualBaseDiagnostic = false;
    [Range(0.1f, 2f)] public float primaryVoxelSurfaceVisualBaseDiagnosticInterval = 0.5f;

    [Header("Surface")]
    public float heightScale = 1f;
    [Range(0f, 1f)] public float blend = 0.1f;
    public float activationThreshold = 0.00001f;
    [Min(1)] public int updateEveryNFrames = 1;

    [Header("Blob Shape")]
    [Range(0f, 1f)] public float blobStretchStrength = 0.35f;
    [Range(0f, 1f)] public float blobWobbleStrength = 0.18f;
    [Range(0f, 4f)] public float blobWobbleSpeed = 1.4f;
    [Range(0f, 1f)] public float blobEdgeDeformStrength = 0.45f;
    [Range(0.01f, 0.5f)] public float blobEdgeSoftness = 0.18f;
    [Range(0.01f, 1f)] public float blobShapeResponse = 0.08f;
    [Range(0.05f, 3f)] public float blobShapeLagSeconds = 0.75f;

    [Header("3D Tile Shape")]
    public float floorY = -2f;
    public float minWaterDepth = 0.08f;
    public float unsupportedDepth = 0.04f;
    public float floorSupportDistance = 0.35f;
    public float airborneJoinDistance = 0.35f;
    public float mainBodyLeaveDistance = 0.75f;
    [HideInInspector] public bool useBulkHeightmapFootprint = true;
    public bool renderAirborneTiles = false;
    public bool showDetachedSidewalls = true;
    [Header("First Layer Surface")]
    public float singleLayerVisualDepth = 0.08f;
    [Range(0f, 1f)] public float singleLayerSurfaceRise = 0.35f;
    public float multiLayerHeightThreshold = 0.45f;
    public float layerRiseBlendRange = 0.35f;
    public float layerRiseSpeed = 0.85f;
    [Min(1)] public int firstLayerFullParticleCount = 8;
    public bool waitForStableFootprintBeforeRising = true;
    public float footprintStableSeconds = 5f;
    public float firstLayerMaxWaitSeconds = 8f;
    public float firstLayerRiseBlendSeconds = 4f;
    [Min(0)] public int footprintGrowthTileTolerance = 8;
    [Range(0f, 1f)] public float firstLayerRiseGate = 0f;

    [Min(1)] public int floorSupportMinParticles = 3;
    [Range(0f, 1f)] public float floorSupportRatioThreshold = 0.65f;
    [Range(1, 6)] public int topSubdivisions = 3;
    [Range(0f, 1f)] public float cornerBlendStrength = 0.65f;
    public float waveHeight = 0.015f;
    public float waveScale = 0.8f;

    [Header("Thickness Shading")]
    public float thicknessClearDepth = 0.04f;
    public float thicknessHeavyDepth = 0.35f;
    [Range(0f, 1f)] public float thinWaterOpacity = 0.22f;
    [Range(0f, 1f)] public float thicknessColorStrength = 0.65f;
    [Range(0f, 1f)] public float thinEdgeFadeStrength = 0.75f;

    [Header("Bounds (match FluidSimulator exactly)")]
    public Vector3 boundsMin = new Vector3(-20, -2, -20);
    public Vector3 boundsMax = new Vector3(20, 6, 20);

    [Header("Compute")]
    public ComputeShader tileCS;

    public int numParticles = 0;

    [Header("Runtime Surface Stats")]
    public int wetTileCount = 0;
    public int mainBodyTileCount = 0;
    public int airborneTileCount = 0;
    public float wetCoveragePercent = 0f;
    public float maxTileSurfaceHeight = 0f;
    public float averageWetTileHeight = 0f;
    public float mainBodySurfaceLevel = -2f;
    public float liveParticleMainBodyHeight = -2f;
    public float averageMainBodyParticleHeight = -2f;
    public int mainBodyFootprintTileArea = 0;
    public float footprintStableTimer = 0f;

    // Legacy bulk overlay is retained only for compatibility.
    [HideInInspector] public bool showBulkDebugOverlay = false;
    [Tooltip("Draws bulk debug cells this far above the represented water height.")]
    [HideInInspector] public float bulkDebugTileHeightOffset = 0.04f;
    [Range(0.1f, 1f)] public float bulkDebugTileInset = 0.85f;
    [HideInInspector] public int bulkDebugExposedActiveThreshold = 0;
    [HideInInspector] public Color bulkDebugActivePbfColor = new Color(0f, 1f, 0f, 0.45f);
    [HideInInspector] public Color bulkDebugStoredVolumeColor = new Color(0f, 0.35f, 1f, 0.45f);
    [HideInInspector] public Color bulkDebugExposedColor = new Color(1f, 0f, 0f, 0.5f);
    [HideInInspector] public Color bulkDebugMismatchColor = new Color(1f, 0.9f, 0f, 0.55f);

    [Header("Underwater Voxel Debug")]
    [Tooltip("Draws the full vertical voxel field from the lower simulation bound through the surface.")]
    public bool drawUnderwaterVoxels = true;
    [Tooltip("When enabled, cubes above the local water surface are hidden.")]
    public bool voxelShowOnlyUnderwater = true;
    [HideInInspector] public bool renderCoarseBulkVoxelField = false;
    [HideInInspector] public bool voxelShowBulkOnly = false;
    [HideInInspector] public bool voxelUseTopClip = false;
    [HideInInspector] public float voxelClipTopY = 0f;
    [Tooltip("-1 draws every layer; otherwise draws only this vertical layer.")]
    public int voxelSliceLayer = -1;
    [Tooltip("-1 draws through the configured voxel height; otherwise limits the highest visible layer.")]
    public int voxelMaxLayer = -1;
    [Tooltip("0 = fill, 1 = hydrostatic pressure, 2 = density, 3 = PBF contribution.")]
    [Range(0, 3)] public int voxelColorMode = 1;
    public float voxelPressureDisplayMax = 10000f;
    public float voxelDensityDisplayMax = 1000f;
    [Range(0f, 1f)] public float voxelOpacity = 0.24f;
    public Color voxelLowColor = new Color(0.02f, 0.2f, 0.8f, 1f);
    public Color voxelHighColor = new Color(1f, 0.18f, 0.03f, 1f);
    public Material voxelMaterialOverride;
    [Tooltip("0 = smoothed raymarched microvolume, 1 = blue coarse voxel cubes, 2 = raw microvolume.")]
    [Range(0, 2)] public int voxelRenderMode = 1;
    [Header("Primary Voxel Topology Diagnostic")]
    [Tooltip("Read-only overlay for surface gaps. Red = solid beside water, cyan = dry open beside water, green = an open cell bracketed by water, blue/orange/violet = selected surface slot. Bright cube faces identify blocked -Z/+Z/-X/+X directions.")]
    public bool drawPrimaryVoxelTopologyDiagnostic = true;
    [Range(0.1f, 1f)] public float primaryVoxelTopologyDiagnosticOpacity = 0.72f;
    [Header("Doorway Surface Diagnostic")]
    [Tooltip("Logs the selected voxel and four neighbours and automatically scans the whole authoritative grid for one-voxel surface gaps. Read-only; does not alter the simulation or surface.")]
    public bool logDoorwaySurfaceDiagnostic = false;
    [Tooltip("Authoritative primary-voxel X/Z coordinate to inspect. The current problem doorway was previously observed near X=13, Z=50.")]
    public Vector2Int doorwaySurfaceDiagnosticVoxel = new Vector2Int(13, 50);
    [Tooltip("Cardinal-neighbour distance, in authoritative voxels, included in the copied Console trace.")]
    [Range(1, 4)] public int doorwaySurfaceDiagnosticRadius = 1;
    [Tooltip("Seconds between compact GPU diagnostic readbacks.")]
    [Range(0.25f, 5f)] public float doorwaySurfaceDiagnosticInterval = 1f;
    [Range(8, 128)] public int microVolumeRaymarchSteps = 64;
    [Range(0f, 1f)] public float microVolumeOpacity = 0.34f;
    [Tooltip("Continuous optical density reconstructed between each wet field point's bottom and surface.")]
    [Range(0f, 2f)] public float microVolumeInteriorDensity = 0.65f;
    [Tooltip("Maximum ray distance used by the immersive underwater pass.")]
    [Range(1f, 40f)] public float microVolumeUnderwaterViewDistance = 12f;
    [Tooltip("How strongly pressure/density debug colours tint the underwater medium.")]
    [Range(0f, 1f)] public float microVolumeDiagnosticBlend = 0.3f;
    [Tooltip("Subtle internal cell-face cue so the underwater volume retains visible voxel detail.")]
    [Range(0f, 0.5f)] public float microVolumeGridStrength = 0.11f;
    public Color microVolumeWaterColor = new Color(0.025f, 0.24f, 0.36f, 1f);
    [HideInInspector] public ComputeBuffer microVoxelBuffer;
    [HideInInspector] public ComputeBuffer microVoxelRawBuffer;
    [HideInInspector] public int microVoxelGridCountX = 1;
    [HideInInspector] public int microVoxelGridCountZ = 1;
    [HideInInspector] public int microVoxelLayerCount = 1;
    [HideInInspector] public float microVoxelHeight = 1f;

    [HideInInspector] public ComputeBuffer particleBuffer;
    [HideInInspector] public ComputeBuffer cellHeadsBuffer;
    [HideInInspector] public ComputeBuffer nextIndexBuffer;
    [HideInInspector] public ComputeBuffer bulkWaterBuffer;
    [HideInInspector] public ComputeBuffer primaryVoxelFlowBuffer;
    [HideInInspector] public ComputeBuffer primarySurfaceWaveBuffer;
    [HideInInspector] public ComputeBuffer primaryVoxelSolidBuffer;
    [HideInInspector] public ComputeBuffer primaryVoxelFaceOpenBuffer;
    [HideInInspector] public ComputeBuffer primaryVoxelFaceFluxBuffer;
    [HideInInspector] public ComputeBuffer bulkVoxelBuffer;
    [HideInInspector] public ComputeBuffer sprayParticleBuffer;
    [HideInInspector] public int sprayParticleCapacity;
    [HideInInspector] public int bulkGridCountX = 1;
    [HideInInspector] public int bulkGridCountZ = 1;
    [HideInInspector] public int primaryVoxelFlowLayerCount = 1;
    [HideInInspector] public float primaryVoxelFlowHeight = 1f;
    [HideInInspector] public float primarySurfaceWaveMaxDisplacement = 0.55f;
    [HideInInspector] public float primarySurfaceWaveVisualResponse = 2.5f;
    [HideInInspector] public float primarySurfaceWaveVisualMaximumVerticalSpeed = 0.35f;
    [HideInInspector] public float primarySurfaceWaveVisualSpatialSmoothing = 0.75f;
    [HideInInspector] public bool usePrimarySurfaceWaveArtificialRippleLifecycle = false;
    [HideInInspector] public float primarySurfaceWaveVisualRippleDuration = 0.6f;
    [HideInInspector] public float primarySurfaceWaveVisualRippleMaximumAmplitude = 0.12f;
    [HideInInspector] public float primarySurfaceWaveVisualRippleActivationAmplitude = 0.008f;
    [HideInInspector] public int primaryVoxelSurfaceSlotCount = 1;
    [HideInInspector] public int bulkVoxelGridCountX = 1;
    [HideInInspector] public int bulkVoxelGridCountZ = 1;
    [HideInInspector] public int bulkVoxelLayerCount = 1;
    [HideInInspector] public float bulkVoxelHeight = 1f;

    ComputeBuffer tileBuffer;
    ComputeBuffer tileWriteBuffer;
    ComputeBuffer tileFlowBuffer;
    ComputeBuffer surfaceFieldBuffer;
    ComputeBuffer surfaceFieldWriteBuffer;
    ComputeBuffer doorwaySurfaceDiagnosticBuffer;
    ComputeBuffer doorwaySurfaceGapScanBuffer;
    ComputeBuffer doorwaySurfaceFaceScanBuffer;
    ComputeBuffer surfaceVisualBaseDiagnosticBuffer;
    ComputeBuffer fallbackBulkWaterBuffer;
    ComputeBuffer fallbackPrimaryVoxelFlowBuffer;
    ComputeBuffer fallbackPrimarySurfaceWaveBuffer;
    ComputeBuffer fallbackPrimaryVoxelSolidBuffer;
    ComputeBuffer fallbackPrimaryVoxelFaceOpenBuffer;
    ComputeBuffer fallbackPrimaryVoxelFaceFluxBuffer;
    Mesh generatedDepthMesh;
    Mesh generatedHeightmapMesh;
    MaterialPropertyBlock heightmapTopPropertyBlock;
    Mesh generatedVoxelCubeMesh;
    Material generatedVoxelMaterial;
    Material generatedMicroVolumeMaterial;
    Material generatedSprayMaterial;
    ComputeBuffer sprayIndirectArgsBuffer;
    int generatedDepthMeshSubdivisions = -1;
    int generatedHeightmapX = -1;
    int generatedHeightmapZ = -1;
    int generatedHeightmapStep = -1;
    int kernelTile = -1;
    int kernelContinuousSurfaceField = -1;
    int kernelDoorwaySurfaceDiagnostic = -1;
    int kernelDoorwaySurfaceGapScan = -1;
    int kernelDoorwaySurfaceFaceScan = -1;
    int totalTiles;
    int surfaceFieldHistoryFrames;
    float statsReadbackTimer;
    bool statsReadbackPending;
    float doorwaySurfaceDiagnosticTimer;
    bool doorwaySurfaceDiagnosticReadbackPending;
    bool doorwaySurfaceGapScanReadbackPending;
    bool doorwaySurfaceFaceScanReadbackPending;
    float surfaceVisualBaseDiagnosticTimer;
    bool surfaceVisualBaseDiagnosticReadbackPending;
    int previousMainBodyFootprintTileArea;
    float firstLayerWaitTimer;
    float cachedCellSize = 1f;
    float cachedSmoothingRadius = 1f;
    int cachedGridResolution = 1;
    BulkWaterCellData[] debugBulkCells;

    const int TileStride = sizeof(float) * 8;
    const int SurfaceFieldStride = sizeof(float) * 8;
    const int BulkWaterCellStride = sizeof(float) * 4 + sizeof(int) * 7;
    const int PrimaryVoxelCellStride = sizeof(float) * 8;
    const int PrimarySurfaceWaveCellStride = sizeof(float) * 8;
    const int PrimaryVoxelFaceFluxStride = sizeof(float) * 8;
    const int MaxDeckSurfaceSlots = 3;
    const int DoorwaySurfaceDiagnosticSampleCount = 5;
    const int DoorwaySurfaceDiagnosticEntryCount =
        DoorwaySurfaceDiagnosticSampleCount * MaxDeckSurfaceSlots;
    const int DoorwaySurfaceDiagnosticStride = sizeof(float) * 4 * 14;
    const int DoorwaySurfaceGapScanStride = sizeof(float) * 4 * 6;
    const int DoorwaySurfaceFaceScanStride = sizeof(float) * 4 * 8;
    const int SurfaceVisualBaseDiagnosticStride = sizeof(float) * 4 * 2;

    // The legacy tile buffer remains exposed only for fluid classification.
    // Visible water is reconstructed into surfaceFieldBuffer and never draws
    // individual tile geometry.
    public ComputeBuffer TileBuffer => tileBuffer;
    public ComputeBuffer SurfaceFieldBuffer => surfaceFieldBuffer;
    public int TileCountX => tileCountX;
    public int TileCountZ => tileCountZ;
    public float TileSize => tileSize;
    public int SurfaceFieldSlotCount => ActiveDeckSurfaceSlotCount;
    public float SurfaceVisibleContour => Mathf.Clamp(heightmapEdgeFade, 0.05f, 0.9f);
    public bool HasTileBuffer => tileBuffer != null;

    public void InitBuffers(ComputeBuffer particles,
                            ComputeBuffer cellHeads,
                            ComputeBuffer nextIndex,
                            float cellSz, float smoothRad, int gridRes,
                            ComputeBuffer bulkWater = null,
                            int bulkCountX = 1,
                            int bulkCountZ = 1)
    {
        particleBuffer = particles;
        cellHeadsBuffer = cellHeads;
        nextIndexBuffer = nextIndex;
        bulkWaterBuffer = bulkWater;
        bulkGridCountX = Mathf.Max(bulkCountX, 1);
        bulkGridCountZ = Mathf.Max(bulkCountZ, 1);
        cachedCellSize = Mathf.Max(cellSz, 0.0001f);
        cachedSmoothingRadius = Mathf.Max(smoothRad, 0.0001f);
        cachedGridResolution = Mathf.Max(gridRes, 1);

        if (tileCS == null)
            return;

        tileCS.SetFloat("_CellSize", cellSz);
        tileCS.SetFloat("_SmoothingRadius", smoothRad);
        tileCS.SetInt("_GridResolution", gridRes);
        BindBulkWaterBuffer();
    }

    void Start()
    {
        totalTiles = tileCountX * tileCountZ;
        TileData[] initialTiles = CreateInitialTiles();
        tileBuffer = new ComputeBuffer(totalTiles, TileStride);
        tileBuffer.SetData(initialTiles);
        tileWriteBuffer = new ComputeBuffer(totalTiles, TileStride);
        tileWriteBuffer.SetData(initialTiles);
        tileFlowBuffer = new ComputeBuffer(totalTiles, sizeof(float) * 4);
        tileFlowBuffer.SetData(new Vector4[Mathf.Max(1, totalTiles)]);
        SurfaceFieldData[] initialSurfaceFields = CreateInitialSurfaceFields();
        surfaceFieldBuffer = new ComputeBuffer(totalTiles * MaxDeckSurfaceSlots, SurfaceFieldStride);
        surfaceFieldBuffer.SetData(initialSurfaceFields);
        surfaceFieldWriteBuffer = new ComputeBuffer(totalTiles * MaxDeckSurfaceSlots, SurfaceFieldStride);
        surfaceFieldWriteBuffer.SetData(initialSurfaceFields);
        surfaceVisualBaseDiagnosticBuffer?.Release();
        surfaceVisualBaseDiagnosticBuffer = new ComputeBuffer(
            totalTiles * MaxDeckSurfaceSlots, SurfaceVisualBaseDiagnosticStride);
        surfaceFieldHistoryFrames = 0;

        kernelTile = tileCS.FindKernel("CSTileHeight");
        kernelContinuousSurfaceField = tileCS.FindKernel("CSBuildContinuousSurfaceField");
        if (tileCS.HasKernel("CSCollectDoorwaySurfaceDiagnostic"))
        {
            kernelDoorwaySurfaceDiagnostic = tileCS.FindKernel("CSCollectDoorwaySurfaceDiagnostic");
            doorwaySurfaceDiagnosticBuffer = new ComputeBuffer(
                DoorwaySurfaceDiagnosticEntryCount,
                DoorwaySurfaceDiagnosticStride);
        }
        if (tileCS.HasKernel("CSScanDoorwaySurfaceGaps"))
            kernelDoorwaySurfaceGapScan = tileCS.FindKernel("CSScanDoorwaySurfaceGaps");
        if (tileCS.HasKernel("CSScanDoorwaySurfaceFaces"))
            kernelDoorwaySurfaceFaceScan = tileCS.FindKernel("CSScanDoorwaySurfaceFaces");

        tileCS.SetInt("_TileCountX", tileCountX);
        tileCS.SetInt("_TileCountZ", tileCountZ);
        tileCS.SetInt("_TileCount", totalTiles);
        tileCS.SetVector("_BoundsMin", boundsMin);
        tileCS.SetVector("_BoundsMax", boundsMax);
        tileCS.SetFloat("_TileSize", tileSize);
        tileCS.SetBuffer(kernelTile, "previousTiles", tileBuffer);
        tileCS.SetBuffer(kernelTile, "tiles", tileWriteBuffer);
        tileCS.SetBuffer(kernelTile, "tileFlows", tileFlowBuffer);
        BindBulkWaterBuffer();

        if (tileMaterial != null)
        {
            tileMaterial.enableInstancing = true;
            tileMaterial.SetBuffer("_Tiles", tileBuffer);
            tileMaterial.SetBuffer("_TileFlows", tileFlowBuffer);
            tileMaterial.SetBuffer("_SurfaceFields", surfaceFieldBuffer);
        }

        wetTileCount = 0;
        wetCoveragePercent = 0f;
        maxTileSurfaceHeight = boundsMin.y;
        averageWetTileHeight = boundsMin.y;
        mainBodySurfaceLevel = floorY;
        liveParticleMainBodyHeight = floorY;
        averageMainBodyParticleHeight = floorY;
        mainBodyFootprintTileArea = 0;
        footprintStableTimer = 0f;
        firstLayerWaitTimer = 0f;
        firstLayerRiseGate = waitForStableFootprintBeforeRising ? 0f : 1f;
        previousMainBodyFootprintTileArea = 0;
    }

    TileData[] CreateInitialTiles()
    {
        TileData[] tiles = new TileData[Mathf.Max(1, tileCountX * tileCountZ)];
        float startY = boundsMin.y - 1000f;

        for (int z = 0; z < tileCountZ; z++)
        {
            float v = tileCountZ > 1 ? (z + 0.5f) / tileCountZ : 0.5f;

            for (int x = 0; x < tileCountX; x++)
            {
                float u = tileCountX > 1 ? (x + 0.5f) / tileCountX : 0.5f;
                int i = x + z * tileCountX;
                tiles[i].worldPos = new Vector3(
                    Mathf.Lerp(boundsMin.x, boundsMax.x, u),
                    startY,
                    Mathf.Lerp(boundsMin.z, boundsMax.z, v)
                );
                tiles[i].height = startY;
                tiles[i].active = 0;
            }
        }

        return tiles;
    }

    SurfaceFieldData[] CreateInitialSurfaceFields()
    {
        SurfaceFieldData[] fields = new SurfaceFieldData[Mathf.Max(1, tileCountX * tileCountZ * MaxDeckSurfaceSlots)];
        for (int i = 0; i < fields.Length; i++)
        {
            fields[i].height = floorY;
            fields[i].coverage = 0f;
            fields[i].flow = Vector2.zero;
            fields[i].thickness = 0f;
            fields[i].confidence = 0f;
            fields[i].padding = Vector2.zero;
        }
        return fields;
    }

    void BindBulkWaterBuffer()
    {
        if (tileCS == null || kernelTile < 0)
            return;

        EnsureFallbackBulkWaterBuffer();
        ComputeBuffer source = bulkWaterBuffer != null ? bulkWaterBuffer : fallbackBulkWaterBuffer;
        int sourceCountX = bulkWaterBuffer != null ? Mathf.Max(bulkGridCountX, 1) : 1;
        int sourceCountZ = bulkWaterBuffer != null ? Mathf.Max(bulkGridCountZ, 1) : 1;
        tileCS.SetBuffer(kernelTile, "bulkWaterCells", source);
        if (kernelContinuousSurfaceField >= 0)
            tileCS.SetBuffer(kernelContinuousSurfaceField, "bulkWaterCells", source);
        tileCS.SetInt("_UseBulkHeightmapFootprint", useBulkHeightmapFootprint && bulkWaterBuffer != null ? 1 : 0);
        tileCS.SetInt("_BulkGridCountX", sourceCountX);
        tileCS.SetInt("_BulkGridCountZ", sourceCountZ);

        EnsureFallbackPrimaryVoxelFlowBuffer();
        EnsureFallbackPrimarySurfaceWaveBuffer();
        EnsureFallbackPrimaryVoxelSolidBuffer();
        EnsureFallbackPrimaryVoxelFaceOpenBuffer();
        EnsureFallbackPrimaryVoxelFaceFluxBuffer();
        ComputeBuffer primarySource = primaryVoxelFlowBuffer != null
            ? primaryVoxelFlowBuffer
            : fallbackPrimaryVoxelFlowBuffer;
        ComputeBuffer primarySolidSource = primaryVoxelSolidBuffer != null
            ? primaryVoxelSolidBuffer
            : fallbackPrimaryVoxelSolidBuffer;
        ComputeBuffer primaryWaveSource = primarySurfaceWaveBuffer != null
            ? primarySurfaceWaveBuffer
            : fallbackPrimarySurfaceWaveBuffer;
        ComputeBuffer primaryFaceOpenSource = primaryVoxelFaceOpenBuffer != null
            ? primaryVoxelFaceOpenBuffer
            : fallbackPrimaryVoxelFaceOpenBuffer;
        ComputeBuffer primaryFaceFluxSource = primaryVoxelFaceFluxBuffer != null
            ? primaryVoxelFaceFluxBuffer
            : fallbackPrimaryVoxelFaceFluxBuffer;
        tileCS.SetBuffer(kernelTile, "primaryVoxelFlowCells", primarySource);
        if (kernelContinuousSurfaceField >= 0)
        {
            tileCS.SetBuffer(kernelContinuousSurfaceField, "primaryVoxelFlowCells", primarySource);
            tileCS.SetBuffer(kernelContinuousSurfaceField, "primaryVoxelSolidCells", primarySolidSource);
            tileCS.SetBuffer(kernelContinuousSurfaceField, "primaryVoxelFaceOpenCells", primaryFaceOpenSource);
            tileCS.SetBuffer(kernelContinuousSurfaceField, "primarySurfaceWaveCells", primaryWaveSource);
        }
        if (kernelDoorwaySurfaceDiagnostic >= 0)
        {
            tileCS.SetBuffer(kernelDoorwaySurfaceDiagnostic, "primaryVoxelFlowCells", primarySource);
            tileCS.SetBuffer(kernelDoorwaySurfaceDiagnostic, "primaryVoxelSolidCells", primarySolidSource);
            tileCS.SetBuffer(kernelDoorwaySurfaceDiagnostic, "primaryVoxelFaceOpenCells", primaryFaceOpenSource);
            tileCS.SetBuffer(kernelDoorwaySurfaceDiagnostic, "primarySurfaceWaveCells", primaryWaveSource);
        }
        if (kernelDoorwaySurfaceGapScan >= 0)
        {
            tileCS.SetBuffer(kernelDoorwaySurfaceGapScan, "primaryVoxelFlowCells", primarySource);
            tileCS.SetBuffer(kernelDoorwaySurfaceGapScan, "primaryVoxelSolidCells", primarySolidSource);
            tileCS.SetBuffer(kernelDoorwaySurfaceGapScan, "primaryVoxelFaceOpenCells", primaryFaceOpenSource);
            tileCS.SetBuffer(kernelDoorwaySurfaceGapScan, "primarySurfaceWaveCells", primaryWaveSource);
        }
        if (kernelDoorwaySurfaceFaceScan >= 0)
        {
            tileCS.SetBuffer(kernelDoorwaySurfaceFaceScan, "primaryVoxelFlowCells", primarySource);
            tileCS.SetBuffer(kernelDoorwaySurfaceFaceScan, "primaryVoxelSolidCells", primarySolidSource);
            tileCS.SetBuffer(kernelDoorwaySurfaceFaceScan, "primarySurfaceWaveCells", primaryWaveSource);
            tileCS.SetBuffer(kernelDoorwaySurfaceFaceScan, "primaryVoxelFaceOpenCells", primaryFaceOpenSource);
            tileCS.SetBuffer(kernelDoorwaySurfaceFaceScan, "primaryVoxelFaceFluxCells", primaryFaceFluxSource);
        }
        tileCS.SetInt("_UsePrimaryVoxelFlowMap", primaryVoxelFlowBuffer != null ? 1 : 0);
        tileCS.SetInt("_UsePrimarySurfaceWaveMap", primarySurfaceWaveBuffer != null ? 1 : 0);
        tileCS.SetInt("_PrimaryVoxelFlowLayerCount", Mathf.Max(primaryVoxelFlowLayerCount, 1));
        tileCS.SetInt("_PrimaryVoxelSurfaceSlotCount",
            Mathf.Clamp(primaryVoxelSurfaceSlotCount, 1, MaxDeckSurfaceSlots));
        tileCS.SetInt("_PrimaryVoxelSurfaceMinimumGapLayers",
            Mathf.Clamp(primaryVoxelSurfaceMinimumGapLayers, 1, 4));
        tileCS.SetInt("_UseFractionalPrimaryVoxelSurface",
            useFractionalPrimaryVoxelSurface && primaryVoxelFlowBuffer != null ? 1 : 0);
        tileCS.SetFloat("_PrimaryVoxelSurfaceMinimumFill",
            Mathf.Clamp(primaryVoxelSurfaceMinimumFill, 0.00001f, 0.25f));
        tileCS.SetFloat("_PrimaryVoxelSurfaceCoverageFadeFill",
            Mathf.Clamp(primaryVoxelSurfaceCoverageFadeFill, 0.01f, 0.25f));
        tileCS.SetFloat("_PrimaryVoxelSurfaceLateralBlendRadius",
            Mathf.Clamp(primaryVoxelSurfaceLateralBlendRadius, 0.5f, 2f));
        tileCS.SetInt("_PrimaryVoxelSurfaceImmediateVerticalFollow",
            primaryVoxelSurfaceImmediateVerticalFollow ? 1 : 0);
        tileCS.SetFloat("_PrimaryVoxelSurfaceCellHeight",
            Mathf.Max(primaryVoxelFlowHeight, 0.0001f));
        tileCS.SetFloat("_PrimarySurfaceWaveMaxDisplacement",
            Mathf.Max(primarySurfaceWaveMaxDisplacement, 0.001f));
        tileCS.SetFloat("_PrimarySurfaceWaveVisualResponse",
            Mathf.Max(primarySurfaceWaveVisualResponse, 0.01f));
        tileCS.SetFloat("_PrimarySurfaceWaveVisualMaximumVerticalSpeed",
            Mathf.Max(primarySurfaceWaveVisualMaximumVerticalSpeed, 0.01f));
        tileCS.SetFloat("_PrimarySurfaceWaveVisualSpatialSmoothing",
            Mathf.Clamp01(primarySurfaceWaveVisualSpatialSmoothing));
        tileCS.SetInt("_UsePrimarySurfaceWaveArtificialRippleLifecycle",
            usePrimarySurfaceWaveArtificialRippleLifecycle ? 1 : 0);
        tileCS.SetFloat("_PrimarySurfaceWaveVisualRippleDuration",
            Mathf.Max(primarySurfaceWaveVisualRippleDuration, 0.01f));
        tileCS.SetFloat("_PrimarySurfaceWaveVisualRippleMaximumAmplitude",
            Mathf.Max(primarySurfaceWaveVisualRippleMaximumAmplitude, 0.001f));
        tileCS.SetFloat("_PrimarySurfaceWaveVisualRippleActivationAmplitude",
            Mathf.Max(primarySurfaceWaveVisualRippleActivationAmplitude, 0.0001f));
    }

    void EnsureFallbackPrimaryVoxelFlowBuffer()
    {
        if (fallbackPrimaryVoxelFlowBuffer != null)
            return;

        fallbackPrimaryVoxelFlowBuffer = new ComputeBuffer(1, PrimaryVoxelCellStride);
    }

    void EnsureFallbackPrimarySurfaceWaveBuffer()
    {
        if (fallbackPrimarySurfaceWaveBuffer != null)
            return;

        fallbackPrimarySurfaceWaveBuffer = new ComputeBuffer(1, PrimarySurfaceWaveCellStride);
        fallbackPrimarySurfaceWaveBuffer.SetData(new Vector4[2]);
    }

    void EnsureFallbackPrimaryVoxelSolidBuffer()
    {
        if (fallbackPrimaryVoxelSolidBuffer != null)
            return;

        fallbackPrimaryVoxelSolidBuffer = new ComputeBuffer(1, sizeof(uint));
        fallbackPrimaryVoxelSolidBuffer.SetData(new uint[] { 0u });
    }

    void EnsureFallbackPrimaryVoxelFaceOpenBuffer()
    {
        if (fallbackPrimaryVoxelFaceOpenBuffer != null)
            return;

        fallbackPrimaryVoxelFaceOpenBuffer = new ComputeBuffer(1, sizeof(uint));
        fallbackPrimaryVoxelFaceOpenBuffer.SetData(new uint[] { 0u });
    }

    void EnsureFallbackPrimaryVoxelFaceFluxBuffer()
    {
        if (fallbackPrimaryVoxelFaceFluxBuffer != null)
            return;

        fallbackPrimaryVoxelFaceFluxBuffer = new ComputeBuffer(
            1, PrimaryVoxelFaceFluxStride);
    }

    int ActiveDeckSurfaceSlotCount => useFractionalPrimaryVoxelSurface &&
        primaryVoxelFlowBuffer != null
        ? Mathf.Clamp(primaryVoxelSurfaceSlotCount, 1, MaxDeckSurfaceSlots)
        : 1;

    void EnsureFallbackBulkWaterBuffer()
    {
        int count = 1;
        if (fallbackBulkWaterBuffer != null && fallbackBulkWaterBuffer.count == count)
            return;

        fallbackBulkWaterBuffer?.Release();
        fallbackBulkWaterBuffer = new ComputeBuffer(count, BulkWaterCellStride);
        BulkWaterCellData[] disabled = new BulkWaterCellData[count];
        for (int i = 0; i < disabled.Length; i++)
            disabled[i] = BulkWaterCellData.MakeDisabled(boundsMin.y - 1000f);
        fallbackBulkWaterBuffer.SetData(disabled);
    }

    public void DispatchTiles()
    {
        if (tileCS == null || tileBuffer == null || tileWriteBuffer == null ||
            tileFlowBuffer == null || surfaceFieldBuffer == null || surfaceFieldWriteBuffer == null) return;
        if (tileMaterial == null) return;
        if (particleBuffer == null || cellHeadsBuffer == null || nextIndexBuffer == null) return;
        if (!tileCS.HasKernel("CSTileHeight")) return;
        bool updateSurfaceData = updateEveryNFrames <= 1 || Time.frameCount % updateEveryNFrames == 0;

        if (updateSurfaceData)
        {
            tileCS.SetBuffer(kernelTile, "particles", particleBuffer);
            tileCS.SetBuffer(kernelTile, "cellHeads", cellHeadsBuffer);
            tileCS.SetBuffer(kernelTile, "nextIndex", nextIndexBuffer);
            // Surface-particle advection reads the currently published tile
            // state. CSTileHeight writes a separate buffer and swaps only
            // after its dispatch, making every 3x3 history read deterministic.
            tileCS.SetBuffer(kernelTile, "previousTiles", tileBuffer);
            tileCS.SetBuffer(kernelTile, "tiles", tileWriteBuffer);
            tileCS.SetBuffer(kernelTile, "tileFlows", tileFlowBuffer);
                BindBulkWaterBuffer();

            tileCS.SetFloat("_Blend", blend);
            tileCS.SetFloat("_HeightScale", heightScale);
            tileCS.SetFloat("_ActivationThreshold", activationThreshold);
            tileCS.SetFloat("_FloorSupportDistance", floorSupportDistance);
            tileCS.SetFloat("_MainBodyLeaveDistance", mainBodyLeaveDistance);
            tileCS.SetInt("_RenderAirborneTiles", renderAirborneTiles ? 1 : 0);
            tileCS.SetFloat("_SingleLayerVisualDepth", singleLayerVisualDepth);
            tileCS.SetFloat("_SingleLayerSurfaceRise", singleLayerSurfaceRise);
            tileCS.SetFloat("_SingleLayerRiseGate", waitForStableFootprintBeforeRising ? firstLayerRiseGate : 1f);
            tileCS.SetFloat("_MultiLayerHeightThreshold", multiLayerHeightThreshold);
            tileCS.SetFloat("_LayerRiseBlendRange", Mathf.Max(layerRiseBlendRange, 0.0001f));
            tileCS.SetFloat("_LayerRiseSpeed", Mathf.Max(layerRiseSpeed, 0.0001f));
            tileCS.SetFloat("_FirstLayerFullParticleCount", firstLayerFullParticleCount);
            tileCS.SetFloat("_FloorSupportMinParticles", floorSupportMinParticles);
            tileCS.SetFloat("_FloorSupportRatioThreshold", floorSupportRatioThreshold);
            tileCS.SetFloat("_BlobTileStretchStrength", blobStretchStrength);
            tileCS.SetFloat("_BlobTileWobbleStrength", blobWobbleStrength);
            tileCS.SetFloat("_BlobTileEdgeDeformStrength", blobEdgeDeformStrength);
            tileCS.SetFloat("_BlobShapeResponse", blobShapeResponse);
            tileCS.SetFloat("_BlobShapeLagSeconds", blobShapeLagSeconds);
            float requestedSurfaceDeltaTime = Time.deltaTime * Mathf.Max(1, updateEveryNFrames);
            float boundedSurfaceDeltaTime = Mathf.Min(
                requestedSurfaceDeltaTime,
                Mathf.Max(surfaceFieldMaximumDeltaTime, 1f / 240f));
            tileCS.SetFloat("_TileDeltaTime", boundedSurfaceDeltaTime);
            tileCS.SetFloat("_BlobTileMaxSpeed", 3f);
            tileCS.SetFloat("_TileTime", Time.time);
            tileCS.SetInt("_NumParticles", numParticles);
            tileCS.SetVector("_BoundsMin", boundsMin);
            tileCS.SetVector("_BoundsMax", boundsMax);
            tileCS.SetFloat("_TileSize", tileSize);

            int groups = Mathf.CeilToInt(totalTiles / 256f);
            tileCS.Dispatch(kernelTile, groups, 1, 1);

            // Reconstruct a continuous render field from the completed
            // main-body data. Its own read/write pair gives height, coverage,
            // flow, and thickness deterministic temporal history.
            tileCS.SetBuffer(kernelContinuousSurfaceField, "tiles", tileWriteBuffer);
            tileCS.SetBuffer(kernelContinuousSurfaceField, "tileFlows", tileFlowBuffer);
            tileCS.SetBuffer(kernelContinuousSurfaceField, "previousSurfaceFields", surfaceFieldBuffer);
            tileCS.SetBuffer(kernelContinuousSurfaceField, "surfaceFields", surfaceFieldWriteBuffer);
            tileCS.SetBuffer(kernelContinuousSurfaceField, "surfaceVisualBaseDiagnostics",
                surfaceVisualBaseDiagnosticBuffer);
            bool collectVisualBaseDiagnostic = logPrimaryVoxelSurfaceVisualBaseDiagnostic &&
                !surfaceVisualBaseDiagnosticReadbackPending;
            tileCS.SetInt("_EnableSurfaceVisualBaseDiagnostic", collectVisualBaseDiagnostic ? 1 : 0);
            tileCS.SetFloat("_SurfaceFieldHeightResponse", Mathf.Max(surfaceFieldHeightResponse, 0f));
            tileCS.SetFloat("_SurfaceFieldCoverageRiseResponse", Mathf.Max(surfaceFieldCoverageRiseResponse, 0f));
            tileCS.SetFloat("_SurfaceFieldCoverageFallResponse", Mathf.Max(surfaceFieldCoverageFallResponse, 0f));
            tileCS.SetFloat("_SurfaceFieldFlowResponse", Mathf.Max(surfaceFieldFlowResponse, 0f));
            tileCS.SetFloat("_SurfaceFieldMinimumCoverage", Mathf.Clamp01(surfaceFieldMinimumCoverage));
            tileCS.SetFloat("_SurfaceFieldAdvectionStrength", Mathf.Clamp01(surfaceFieldAdvectionStrength));
            tileCS.SetFloat("_SurfaceFieldMaximumAdvectionCells",
                Mathf.Max(surfaceFieldMaximumAdvectionCells, 0.01f));
            tileCS.SetFloat("_SurfaceFieldMaximumMicrocellAdvancePerFrame",
                Mathf.Clamp(surfaceFieldMaximumMicrocellAdvancePerFrame, 0.25f, 1f));
            tileCS.SetFloat("_SurfaceFieldCoverageMaximumSpeed",
                Mathf.Max(surfaceFieldCoverageMaximumSpeed, 0.01f));
            tileCS.SetFloat("_SurfaceFieldVoxelMassCorrectionResponse",
                Mathf.Max(surfaceFieldVoxelMassCorrectionResponse, 0f));
            tileCS.SetFloat("_SurfaceFieldFlowDrivenFrontStrength",
                Mathf.Max(surfaceFieldFlowDrivenFrontStrength, 0f));
            tileCS.SetFloat("_SurfaceFieldIsolatedSeedResponse",
                Mathf.Max(surfaceFieldIsolatedSeedResponse, 0f));
            tileCS.SetFloat("_SurfaceFieldContinuityRepairResponse",
                Mathf.Max(surfaceFieldContinuityRepairResponse, 0f));
            tileCS.SetInt("_SurfaceFieldAllowIsolatedSeeding",
                surfaceFieldAllowIsolatedSeeding ? 1 : 0);
            tileCS.SetFloat("_SurfaceFieldVisibleContour",
                Mathf.Clamp(heightmapEdgeFade, 0.05f, 0.9f));
            tileCS.SetInt("_SurfaceFieldSeedFromTarget",
                surfaceFieldHistoryFrames < Mathf.Clamp(surfaceFieldInitialSeedFrames, 2, 30)
                    ? 1 : 0);
            tileCS.SetFloat("_SurfaceFieldVoxelMassReleaseResponse",
                Mathf.Max(surfaceFieldVoxelMassReleaseResponse, 0f));
            tileCS.SetFloat("_PrimaryVoxelSurfaceMaximumVerticalSpeed",
                Mathf.Max(primaryVoxelSurfaceMaximumVerticalSpeed, 0.01f));
            tileCS.SetFloat("_PrimaryVoxelSurfaceVisualBaseMaximumVerticalSpeed",
                Mathf.Max(primaryVoxelSurfaceVisualBaseMaximumVerticalSpeed, 0.01f));
            int surfaceFieldGroups = Mathf.CeilToInt(
                totalTiles * ActiveDeckSurfaceSlotCount / 256f);
            tileCS.Dispatch(kernelContinuousSurfaceField, Mathf.Max(surfaceFieldGroups, 1), 1, 1);
            surfaceFieldHistoryFrames++;
            if (collectVisualBaseDiagnostic)
                QueuePrimaryVoxelSurfaceVisualBaseDiagnostic();

            ComputeBuffer completedBuffer = tileWriteBuffer;
            tileWriteBuffer = tileBuffer;
            tileBuffer = completedBuffer;

            ComputeBuffer completedSurfaceField = surfaceFieldWriteBuffer;
            surfaceFieldWriteBuffer = surfaceFieldBuffer;
            surfaceFieldBuffer = completedSurfaceField;
        }

        UpdateDoorwaySurfaceDiagnostic();

        tileMaterial.enableInstancing = true;
        tileMaterial.SetBuffer("_Tiles", tileBuffer);
        tileMaterial.SetBuffer("_TileFlows", tileFlowBuffer);
        tileMaterial.SetBuffer("_SurfaceFields", surfaceFieldBuffer);
        tileMaterial.SetFloat("_TileSize", tileSize);
        tileMaterial.SetFloat("_BlobStretchStrength", blobStretchStrength);
        tileMaterial.SetFloat("_BlobWobbleStrength", blobWobbleStrength);
        tileMaterial.SetFloat("_BlobWobbleSpeed", blobWobbleSpeed);
        tileMaterial.SetFloat("_BlobEdgeDeformStrength", blobEdgeDeformStrength);
        tileMaterial.SetFloat("_BlobEdgeSoftness", blobEdgeSoftness);
        tileMaterial.SetFloat("_BlobShapeResponse", blobShapeResponse);
        tileMaterial.SetFloat("_BlobShapeLagSeconds", blobShapeLagSeconds);
        tileMaterial.SetFloat("_WaterFloorY", floorY);
        tileMaterial.SetFloat("_MinWaterDepth", minWaterDepth);
        tileMaterial.SetFloat("_UnsupportedDepth", unsupportedDepth);
        tileMaterial.SetFloat("_ShowDetachedSidewalls", showDetachedSidewalls ? 1f : 0f);
        tileMaterial.SetFloat("_FloorSupportDistance", floorSupportDistance);
        tileMaterial.SetFloat("_AirborneJoinDistance", airborneJoinDistance);
        tileMaterial.SetFloat("_FloorSupportMinParticles", floorSupportMinParticles);
        tileMaterial.SetFloat("_FloorSupportRatioThreshold", floorSupportRatioThreshold);
        tileMaterial.SetFloat("_CornerBlendStrength", cornerBlendStrength);
        tileMaterial.SetFloat("_WaveHeight", waveHeight);
        tileMaterial.SetFloat("_WaveScale", waveScale);
        tileMaterial.SetFloat("_ThicknessClearDepth", thicknessClearDepth);
        tileMaterial.SetFloat("_ThicknessHeavyDepth", Mathf.Max(thicknessHeavyDepth, thicknessClearDepth + 0.0001f));
        tileMaterial.SetFloat("_ThinWaterOpacity", thinWaterOpacity);
        tileMaterial.SetFloat("_ThicknessColorStrength", thicknessColorStrength);
        tileMaterial.SetFloat("_ThinEdgeFadeStrength", thinEdgeFadeStrength);
        tileMaterial.SetFloat("_RenderAsHeightmap", renderAsHeightmapSurface ? 1f : 0f);
        tileMaterial.SetFloat("_HeightmapSidewallOnly", 0f);
        tileMaterial.SetFloat("_HeightmapEdgeFade", Mathf.Clamp(heightmapEdgeFade, 0.05f, 0.9f));
        tileMaterial.SetFloat("_HeightmapContourSoftness",
            Mathf.Clamp(heightmapContourSoftness, 0.005f, 0.1f));
        tileMaterial.SetFloat("_HeightmapNormalStrength", heightmapNormalStrength);
        tileMaterial.SetFloat("_HeightmapSurfaceVisibility", heightmapSurfaceVisibility);
        tileMaterial.SetFloat("_HeightmapShallowVisibility", heightmapShallowVisibility);
        tileMaterial.SetFloat("_UseHeightmapLiquidMicrocells", useHeightmapLiquidMicrocells ? 1f : 0f);
        tileMaterial.SetFloat("_HeightmapLiquidOpacity", Mathf.Clamp01(heightmapLiquidOpacity));
        tileMaterial.SetFloat("_HeightmapMicrocellScale", Mathf.Max(heightmapMicrocellScale, 0.0001f));
        tileMaterial.SetFloat("_HeightmapMicrocellHeight", Mathf.Max(heightmapMicrocellHeight, 0f));
        tileMaterial.SetFloat("_HeightmapMicrocellNormalStrength", Mathf.Max(heightmapMicrocellNormalStrength, 0f));
        tileMaterial.SetFloat("_HeightmapMicrocellEdgeStrength", Mathf.Clamp01(heightmapMicrocellEdgeStrength));
        tileMaterial.SetFloat("_ActiveRippleFlowStart", Mathf.Max(activeRippleFlowStart, 0f));
        tileMaterial.SetFloat("_ActiveRippleFlowFull", Mathf.Max(activeRippleFlowFull, activeRippleFlowStart + 0.0001f));
        tileMaterial.SetFloat("_ActiveRippleWaveStart", Mathf.Max(activeRippleWaveStart, 0f));
        tileMaterial.SetFloat("_ActiveRippleWaveFull", Mathf.Max(activeRippleWaveFull, activeRippleWaveStart + 0.0001f));
        tileMaterial.SetFloat("_ActiveRippleNormalStrength", Mathf.Max(activeRippleNormalStrength, 0f));
        tileMaterial.SetFloat("_ActiveRippleScale", Mathf.Max(activeRippleScale, 0.001f));
        tileMaterial.SetFloat("_ActiveRippleSpeed", Mathf.Max(activeRippleSpeed, 0f));
        tileMaterial.SetVector("_BoundsMin", boundsMin);
        tileMaterial.SetVector("_BoundsMax", boundsMax);
        tileMaterial.SetInt("_TileCountX", tileCountX);
        tileMaterial.SetInt("_TileCountZ", tileCountZ);
        tileMaterial.SetInt("_SurfaceFieldOffset", 0);

        Mesh drawMesh = renderAsHeightmapSurface ? GetHeightmapMesh() : GetDrawMesh();
        if (drawMesh == null) return;

        Bounds drawBounds = new Bounds(
            (boundsMin + boundsMax) * 0.5f,
            boundsMax - boundsMin + Vector3.one * 10f);

        if (renderAsHeightmapSurface)
        {
            if (heightmapTopPropertyBlock == null)
                heightmapTopPropertyBlock = new MaterialPropertyBlock();
            for (int deckSlot = 0; deckSlot < ActiveDeckSurfaceSlotCount; deckSlot++)
            {
                heightmapTopPropertyBlock.Clear();
                heightmapTopPropertyBlock.SetFloat("_RenderAsHeightmap", 1f);
                heightmapTopPropertyBlock.SetFloat("_HeightmapSidewallOnly", 0f);
                heightmapTopPropertyBlock.SetInt("_SurfaceFieldOffset", deckSlot * totalTiles);
                Graphics.DrawMesh(drawMesh, Matrix4x4.identity, tileMaterial, gameObject.layer, null, 0, heightmapTopPropertyBlock);
            }
        }
        else
        {
            Graphics.DrawMeshInstancedProcedural(
                drawMesh, 0, tileMaterial,
                drawBounds,
                totalTiles
            );
        }

        DrawUnderwaterVoxelOverlay();
        DrawDetachedSpray();

        UpdateSurfaceStats();
    }

    // Underwater atmosphere is a separate diagnostic/visual overlay. It must
    // not depend on any alternative liquid-body renderer being enabled.
    void DrawUnderwaterVoxelOverlay()
    {
        if (!drawUnderwaterVoxels)
            return;

        // Topology mode deliberately uses the coarse authoritative display
        // buffer even when the normal underwater view is raymarched.
        if (drawPrimaryVoxelTopologyDiagnostic)
        {
            DrawUnderwaterVoxels();
            return;
        }

        bool drawn = false;
        if (voxelRenderMode == 2 && microVoxelRawBuffer != null)
            drawn = DrawMicroVoxelVolume(microVoxelRawBuffer);
        else if (voxelRenderMode == 0 && microVoxelBuffer != null)
            drawn = DrawMicroVoxelVolume(microVoxelBuffer);

        if (!drawn)
            DrawUnderwaterVoxels();
    }

    void DrawDetachedSpray()
    {
        if (!drawDetachedSpray || sprayParticleBuffer == null || sprayParticleCapacity <= 0)
            return;

        if (generatedVoxelCubeMesh == null)
            generatedVoxelCubeMesh = CreateVoxelCubeMesh();
        if (generatedSprayMaterial == null)
        {
            Shader shader = sprayShader != null ? sprayShader : Shader.Find("Custom/FluidSpray");
            if (shader != null && shader.isSupported)
                generatedSprayMaterial = new Material(shader)
                {
                    name = "Generated Fluid Detached Spray Material",
                    enableInstancing = true
                };
        }
        if (generatedSprayMaterial == null)
            return;
        if (sprayIndirectArgsBuffer == null)
        {
            sprayIndirectArgsBuffer = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);
            sprayIndirectArgsBuffer.SetData(new uint[]
            {
                generatedVoxelCubeMesh.GetIndexCount(0), (uint)sprayParticleCapacity,
                generatedVoxelCubeMesh.GetIndexStart(0), (uint)generatedVoxelCubeMesh.GetBaseVertex(0), 0
            });
        }

        generatedSprayMaterial.SetBuffer("_SprayParticles", sprayParticleBuffer);
        generatedSprayMaterial.SetColor("_SprayColor", new Color(0.48f, 0.88f, 1f, 1f));
        generatedSprayMaterial.SetFloat("_SprayOpacity", Mathf.Clamp01(sprayOpacity));
        generatedSprayMaterial.SetFloat("_SprayBrightness", Mathf.Max(sprayBrightness, 0f));
        Bounds sprayBounds = new Bounds(
            (boundsMin + boundsMax) * 0.5f,
            boundsMax - boundsMin + Vector3.one * 8f);
        Graphics.DrawMeshInstancedIndirect(
            generatedVoxelCubeMesh, 0, generatedSprayMaterial,
            sprayBounds, sprayIndirectArgsBuffer);
    }

    bool DrawMicroVoxelVolume(ComputeBuffer sourceBuffer)
    {
        if (!drawUnderwaterVoxels || sourceBuffer == null || microVoxelGridCountX <= 0 ||
            microVoxelGridCountZ <= 0 || microVoxelLayerCount <= 0)
            return false;

        EnsureMicroVolumeResources();
        if (generatedMicroVolumeMaterial == null || generatedVoxelCubeMesh == null)
            return false;

        generatedMicroVolumeMaterial.SetBuffer("_MicroVoxelCells", sourceBuffer);
        generatedMicroVolumeMaterial.SetVector("_BoundsMin", boundsMin);
        generatedMicroVolumeMaterial.SetVector("_BoundsMax", boundsMax);
        generatedMicroVolumeMaterial.SetInt("_MicroGridCountX", microVoxelGridCountX);
        generatedMicroVolumeMaterial.SetInt("_MicroGridCountZ", microVoxelGridCountZ);
        generatedMicroVolumeMaterial.SetInt("_MicroLayerCount", microVoxelLayerCount);
        generatedMicroVolumeMaterial.SetFloat("_MicroCellSizeX",
            Mathf.Max((boundsMax.x - boundsMin.x) / microVoxelGridCountX, 0.0001f));
        generatedMicroVolumeMaterial.SetFloat("_MicroCellSizeZ",
            Mathf.Max((boundsMax.z - boundsMin.z) / microVoxelGridCountZ, 0.0001f));
        generatedMicroVolumeMaterial.SetFloat("_MicroLayerHeight", Mathf.Max(microVoxelHeight, 0.0001f));
        generatedMicroVolumeMaterial.SetInt("_MicroRaymarchSteps", Mathf.Clamp(microVolumeRaymarchSteps, 8, 128));
        generatedMicroVolumeMaterial.SetFloat("_MicroOpacity", Mathf.Clamp01(microVolumeOpacity));
        generatedMicroVolumeMaterial.SetFloat("_MicroInteriorDensity", Mathf.Max(microVolumeInteriorDensity, 0f));
        generatedMicroVolumeMaterial.SetFloat("_MicroUnderwaterViewDistance",
            Mathf.Max(microVolumeUnderwaterViewDistance, 1f));
        generatedMicroVolumeMaterial.SetFloat("_MicroDiagnosticBlend", Mathf.Clamp01(microVolumeDiagnosticBlend));
        generatedMicroVolumeMaterial.SetFloat("_MicroGridStrength", Mathf.Clamp(microVolumeGridStrength, 0f, 0.5f));
        generatedMicroVolumeMaterial.SetColor("_MicroWaterColor", microVolumeWaterColor);
        generatedMicroVolumeMaterial.SetInt("_MicroColorMode", voxelColorMode);
        generatedMicroVolumeMaterial.SetFloat("_MicroPressureDisplayMax", Mathf.Max(voxelPressureDisplayMax, 0.0001f));
        generatedMicroVolumeMaterial.SetFloat("_MicroDensityDisplayMax", Mathf.Max(voxelDensityDisplayMax, 0.0001f));
        generatedMicroVolumeMaterial.SetColor("_MicroLowColor", voxelLowColor);
        generatedMicroVolumeMaterial.SetColor("_MicroHighColor", voxelHighColor);

        bool hasSurfaceField = surfaceFieldBuffer != null && tileCountX > 0 && tileCountZ > 0;
        generatedMicroVolumeMaterial.SetInt("_HasSurfaceField", hasSurfaceField ? 1 : 0);
        if (hasSurfaceField)
            generatedMicroVolumeMaterial.SetBuffer("_SurfaceFields", surfaceFieldBuffer);
        generatedMicroVolumeMaterial.SetInt("_SurfaceFieldCountX", Mathf.Max(tileCountX, 1));
        generatedMicroVolumeMaterial.SetInt("_SurfaceFieldCountZ", Mathf.Max(tileCountZ, 1));
        generatedMicroVolumeMaterial.SetFloat("_SurfaceCoverageThreshold",
            Mathf.Max(surfaceFieldMinimumCoverage, 0.001f));

        Camera drawCamera = Camera.main;
        bool cameraInsideBounds = false;
        bool cameraMayBeSubmerged = false;
        if (drawCamera != null)
        {
            Vector3 cameraPosition = drawCamera.transform.position;
            cameraInsideBounds = cameraPosition.x >= boundsMin.x && cameraPosition.x <= boundsMax.x &&
                                 cameraPosition.y >= boundsMin.y && cameraPosition.y <= boundsMax.y &&
                                 cameraPosition.z >= boundsMin.z && cameraPosition.z <= boundsMax.z;
            cameraMayBeSubmerged = cameraInsideBounds && wetTileCount > 0 &&
                cameraPosition.y <= maxTileSurfaceHeight + Mathf.Max(microVoxelHeight, 0.05f);
        }

        // From inside the proxy cube its back-facing polygons are the exit
        // faces.  When the camera may be submerged, Always depth testing lets
        // the volume tint opaque geometry too; the shader still verifies the
        // local surface field before enabling this immersive path.
        CullMode cullMode = cameraInsideBounds ? CullMode.Front : CullMode.Back;
        CompareFunction depthTest = cameraMayBeSubmerged
            ? CompareFunction.Always
            : CompareFunction.LessEqual;
        generatedMicroVolumeMaterial.SetFloat("_CullMode", (float)cullMode);
        generatedMicroVolumeMaterial.SetFloat("_ZTestMode", (float)depthTest);
        generatedMicroVolumeMaterial.SetFloat("_MicroUnderwaterOnly", cameraMayBeSubmerged ? 1f : 0f);

        Vector3 center = (boundsMin + boundsMax) * 0.5f;
        Vector3 size = boundsMax - boundsMin;
        Bounds volumeBounds = new Bounds(center, size + Vector3.one * 2f);
        // The shader maps the unit cube directly into the absolute simulation
        // bounds, so one procedural instance is sufficient and avoids relying
        // on a Matrix4x4 Graphics.DrawMesh overload that is unavailable in this
        // Unity version.
        Graphics.DrawMeshInstancedProcedural(
            generatedVoxelCubeMesh, 0, generatedMicroVolumeMaterial, volumeBounds, 1);
        return true;
    }

    void EnsureMicroVolumeResources()
    {
        if (generatedVoxelCubeMesh == null)
            generatedVoxelCubeMesh = CreateVoxelCubeMesh();
        if (generatedMicroVolumeMaterial == null)
        {
            Shader shader = Shader.Find("Custom/FluidMicroVolume");
            if (shader != null && shader.isSupported)
                generatedMicroVolumeMaterial = new Material(shader) { name = "Generated Fluid Microvolume Material" };
        }
    }

    void DrawUnderwaterVoxels()
    {
        if ((!drawUnderwaterVoxels && !renderCoarseBulkVoxelField) || bulkVoxelBuffer == null)
            return;

        int voxelCount = Mathf.Max(1, bulkVoxelGridCountX) *
                         Mathf.Max(1, bulkVoxelGridCountZ) *
                         Mathf.Max(1, bulkVoxelLayerCount);
        if (voxelCount <= 0)
            return;

        EnsureVoxelDebugResources();
        Material material = voxelMaterialOverride != null
            ? voxelMaterialOverride
            : generatedVoxelMaterial;
        if (material == null || generatedVoxelCubeMesh == null)
            return;

        material.SetBuffer("_BulkVoxelCells", bulkVoxelBuffer);
        material.SetVector("_BoundsMin", boundsMin);
        material.SetVector("_BoundsMax", boundsMax);
        material.SetInt("_VoxelGridCountX", Mathf.Max(1, bulkVoxelGridCountX));
        material.SetInt("_VoxelGridCountZ", Mathf.Max(1, bulkVoxelGridCountZ));
        material.SetInt("_VoxelLayerCount", Mathf.Max(1, bulkVoxelLayerCount));
        material.SetFloat("_VoxelCellSizeX", Mathf.Max((boundsMax.x - boundsMin.x) /
            Mathf.Max(1, bulkVoxelGridCountX), 0.0001f));
        material.SetFloat("_VoxelCellSizeZ", Mathf.Max((boundsMax.z - boundsMin.z) /
            Mathf.Max(1, bulkVoxelGridCountZ), 0.0001f));
        material.SetFloat("_VoxelLayerHeight", Mathf.Max(bulkVoxelHeight, 0.0001f));
        material.SetInt("_VoxelSliceLayer", voxelSliceLayer);
        material.SetInt("_VoxelMaxLayer", voxelMaxLayer);
        material.SetInt("_VoxelShowOnlyUnderwater", voxelShowOnlyUnderwater ? 1 : 0);
        material.SetInt("_VoxelShowBulkOnly", voxelShowBulkOnly ? 1 : 0);
        material.SetInt("_VoxelUseTopClip", voxelUseTopClip ? 1 : 0);
        material.SetFloat("_VoxelClipTopY", voxelClipTopY);
        material.SetInt("_VoxelColorMode", voxelColorMode);
        material.SetFloat("_VoxelPressureDisplayMax", Mathf.Max(voxelPressureDisplayMax, 0.0001f));
        material.SetFloat("_VoxelDensityDisplayMax", Mathf.Max(voxelDensityDisplayMax, 0.0001f));
        material.SetInt("_VoxelTopologyDiagnostic", drawPrimaryVoxelTopologyDiagnostic ? 1 : 0);
        material.SetFloat("_VoxelOpacity", drawPrimaryVoxelTopologyDiagnostic
            ? Mathf.Clamp01(primaryVoxelTopologyDiagnosticOpacity)
            : Mathf.Clamp01(voxelOpacity));
        material.SetColor("_VoxelLowColor", voxelLowColor);
        material.SetColor("_VoxelHighColor", voxelHighColor);
        bool useMappedSurfaceClip = renderAsHeightmapSurface && surfaceFieldBuffer != null;
        if (useMappedSurfaceClip)
            material.SetBuffer("_SurfaceFields", surfaceFieldBuffer);
        material.SetInt("_UseMappedSurfaceClip", useMappedSurfaceClip ? 1 : 0);
        material.SetInt("_SurfaceFieldCountX", Mathf.Max(tileCountX, 1));
        material.SetInt("_SurfaceFieldCountZ", Mathf.Max(tileCountZ, 1));
        material.SetInt("_SurfaceFieldSlotCount", ActiveDeckSurfaceSlotCount);
        material.SetFloat("_SurfaceFieldVisibleContour", SurfaceVisibleContour);
        material.SetFloat("_MappedSurfaceClearance",
            Mathf.Max(0.005f, primaryVoxelFlowHeight * 0.02f));

        Bounds voxelBounds = new Bounds(
            (boundsMin + boundsMax) * 0.5f,
            boundsMax - boundsMin + Vector3.one * 2f);
        Graphics.DrawMeshInstancedProcedural(
            generatedVoxelCubeMesh, 0, material, voxelBounds, voxelCount);
    }

    void EnsureVoxelDebugResources()
    {
        if (generatedVoxelCubeMesh == null)
            generatedVoxelCubeMesh = CreateVoxelCubeMesh();

        if (generatedVoxelMaterial == null)
        {
            Shader shader = Shader.Find("Custom/FluidVoxelDebug");
            if (shader != null)
                generatedVoxelMaterial = new Material(shader) { name = "Generated Fluid Voxel Debug Material" };
        }
    }

    static int GetOrAddRoundedVoxelVertex(Vector3 vertex, List<Vector3> vertices,
                                           Dictionary<Vector3, int> vertexIndices)
    {
        if (vertexIndices.TryGetValue(vertex, out int index))
            return index;

        index = vertices.Count;
        vertices.Add(vertex);
        vertexIndices.Add(vertex, index);
        return index;
    }

    static void AddRoundedVoxelFace(Vector3 origin, Vector3 axisU, Vector3 axisV, bool flip,
                                    List<Vector3> vertices, Dictionary<Vector3, int> vertexIndices,
                                    List<int> triangles)
    {
        const int segments = 2;
        int[,] face = new int[segments + 1, segments + 1];
        for (int y = 0; y <= segments; y++)
        {
            for (int x = 0; x <= segments; x++)
            {
                Vector3 vertex = origin + axisU * ((float)x / segments) + axisV * ((float)y / segments);
                face[x, y] = GetOrAddRoundedVoxelVertex(vertex, vertices, vertexIndices);
            }
        }

        for (int y = 0; y < segments; y++)
        {
            for (int x = 0; x < segments; x++)
            {
                int a = face[x, y];
                int b = face[x + 1, y];
                int c = face[x, y + 1];
                int d = face[x + 1, y + 1];
                if (flip)
                {
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
                else
                {
                    triangles.Add(a); triangles.Add(b); triangles.Add(c);
                    triangles.Add(b); triangles.Add(d); triangles.Add(c);
                }
            }
        }
    }

    static Mesh CreateVoxelCubeMesh()
    {
        Mesh mesh = new Mesh { name = "Generated Fluid Rounded Voxel" };
        List<Vector3> vertices = new List<Vector3>();
        Dictionary<Vector3, int> vertexIndices = new Dictionary<Vector3, int>();
        List<int> triangles = new List<int>();

        AddRoundedVoxelFace(new Vector3(-0.5f, -0.5f, -0.5f), Vector3.right, Vector3.up, true,
            vertices, vertexIndices, triangles);
        AddRoundedVoxelFace(new Vector3(-0.5f, -0.5f, 0.5f), Vector3.right, Vector3.up, false,
            vertices, vertexIndices, triangles);
        AddRoundedVoxelFace(new Vector3(-0.5f, -0.5f, -0.5f), Vector3.right, Vector3.forward, false,
            vertices, vertexIndices, triangles);
        AddRoundedVoxelFace(new Vector3(-0.5f, 0.5f, -0.5f), Vector3.right, Vector3.forward, true,
            vertices, vertexIndices, triangles);
        AddRoundedVoxelFace(new Vector3(-0.5f, -0.5f, -0.5f), Vector3.forward, Vector3.up, false,
            vertices, vertexIndices, triangles);
        AddRoundedVoxelFace(new Vector3(0.5f, -0.5f, -0.5f), Vector3.forward, Vector3.up, true,
            vertices, vertexIndices, triangles);

        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        // Shared face vertices give connected accents a soft liquid highlight.
        // Detached instances are spherified in FluidVoxelSurface.shader.
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    void UpdateSurfaceStats()
    {
        if (tileBuffer == null || totalTiles <= 0)
            return;

        statsReadbackTimer += Time.deltaTime;
        if (statsReadbackTimer < 0.5f || statsReadbackPending)
            return;

        float readbackDeltaTime = statsReadbackTimer;
        statsReadbackTimer = 0f;
        statsReadbackPending = true;
        ComputeBuffer requestedTileBuffer = tileBuffer;

        // A synchronous GetData on the 640 x 640 tile field copied about
        // 13 MB and stalled the GPU every half-second. Read it asynchronously
        // and process the temporary native view without allocating a managed
        // TileData array, so diagnostics cannot pause surface motion.
        AsyncGPUReadback.Request(requestedTileBuffer, request =>
        {
            statsReadbackPending = false;
            if (this == null || request.hasError)
                return;

            NativeArray<TileData> cpuTiles = request.GetData<TileData>();
            if (!cpuTiles.IsCreated || cpuTiles.Length == 0)
                return;

            ApplySurfaceStats(cpuTiles, readbackDeltaTime);
        });

        // This path is tiny compared with the full tile field and is disabled
        // during normal rendering; retain it only for the explicit gizmo.
        UpdateBulkDebugReadback();
    }

    void QueuePrimaryVoxelSurfaceVisualBaseDiagnostic()
    {
        if (!logPrimaryVoxelSurfaceVisualBaseDiagnostic ||
            surfaceVisualBaseDiagnosticBuffer == null || surfaceVisualBaseDiagnosticReadbackPending)
            return;

        surfaceVisualBaseDiagnosticTimer += Time.deltaTime;
        if (surfaceVisualBaseDiagnosticTimer <
            Mathf.Max(primaryVoxelSurfaceVisualBaseDiagnosticInterval, 0.1f))
            return;

        surfaceVisualBaseDiagnosticTimer = 0f;
        surfaceVisualBaseDiagnosticReadbackPending = true;
        ComputeBuffer requestedBuffer = surfaceVisualBaseDiagnosticBuffer;
        int requestedFrame = Time.frameCount;
        int requestedTileCount = totalTiles;
        AsyncGPUReadback.Request(requestedBuffer, request =>
        {
            surfaceVisualBaseDiagnosticReadbackPending = false;
            if (this == null || !logPrimaryVoxelSurfaceVisualBaseDiagnostic || request.hasError)
                return;

            NativeArray<SurfaceVisualBaseDiagnosticData> entries =
                request.GetData<SurfaceVisualBaseDiagnosticData>();
            int active = 0;
            int capped = 0;
            int violations = 0;
            float rawSum = 0f;
            float visualSum = 0f;
            float rawMaximum = 0f;
            float visualMaximum = 0f;
            float capMaximum = 0f;
            int worstIndex = -1;
            for (int i = 0; i < entries.Length; i++)
            {
                SurfaceVisualBaseDiagnosticData entry = entries[i];
                if (entry.steps.w <= 0.5f)
                    continue;
                float rawStep = Mathf.Abs(entry.steps.x);
                float visualStep = Mathf.Abs(entry.steps.y);
                float cap = Mathf.Max(entry.steps.z, 0f);
                active++;
                rawSum += rawStep;
                visualSum += visualStep;
                rawMaximum = Mathf.Max(rawMaximum, rawStep);
                visualMaximum = Mathf.Max(visualMaximum, visualStep);
                capMaximum = Mathf.Max(capMaximum, cap);
                if (rawStep > cap + 0.00001f && visualStep <= cap + 0.0001f)
                    capped++;
                if (visualStep > cap + 0.0005f)
                    violations++;
                if (worstIndex < 0 || rawStep > Mathf.Abs(entries[worstIndex].steps.x))
                    worstIndex = i;
            }

            string worst = "none";
            if (worstIndex >= 0)
            {
                SurfaceVisualBaseDiagnosticData entry = entries[worstIndex];
                int slot = requestedTileCount > 0 ? worstIndex / requestedTileCount : 0;
                int tile = requestedTileCount > 0 ? worstIndex % requestedTileCount : 0;
                int x = tile % Mathf.Max(tileCountX, 1);
                int z = tile / Mathf.Max(tileCountX, 1);
                worst = $"worst=({x},{z}) slot={slot} raw/visual/cap=" +
                    $"{Mathf.Abs(entry.steps.x):F4}/{Mathf.Abs(entry.steps.y):F4}/{entry.steps.z:F4}m " +
                    $"target/previous/render/eta={entry.baseHeights.x:F3}/{entry.baseHeights.y:F3}/" +
                    $"{entry.baseHeights.z:F3}/{entry.baseHeights.w:F4}";
            }

            Debug.Log($"[SurfaceVisualBaseDiagnostic] frame={requestedFrame} active={active} " +
                $"rawStep(mean/max)={(active > 0 ? rawSum / active : 0f):F4}/{rawMaximum:F4}m " +
                $"visualStep(mean/max)={(active > 0 ? visualSum / active : 0f):F4}/{visualMaximum:F4}m " +
                $"cap(max)={capMaximum:F4}m capped={capped} violations={violations} {worst}");
        });
    }
    void UpdateDoorwaySurfaceDiagnostic()
    {
        if (!logDoorwaySurfaceDiagnostic)
        {
            doorwaySurfaceDiagnosticTimer = 0f;
            return;
        }
        if (tileCS == null || kernelDoorwaySurfaceDiagnostic < 0 ||
            kernelDoorwaySurfaceGapScan < 0 ||
            doorwaySurfaceDiagnosticBuffer == null || surfaceFieldBuffer == null ||
            surfaceFieldWriteBuffer == null ||
            primaryVoxelFlowBuffer == null || primaryVoxelSolidBuffer == null)
            return;

        bool canScanDoorwayFaces = kernelDoorwaySurfaceFaceScan >= 0 &&
            primaryVoxelFaceOpenBuffer != null && primaryVoxelFaceFluxBuffer != null;
        doorwaySurfaceDiagnosticTimer += Time.deltaTime;
        if (doorwaySurfaceDiagnosticReadbackPending || doorwaySurfaceGapScanReadbackPending ||
            (canScanDoorwayFaces && doorwaySurfaceFaceScanReadbackPending) ||
            doorwaySurfaceDiagnosticTimer < Mathf.Max(doorwaySurfaceDiagnosticInterval, 0.25f))
            return;

        doorwaySurfaceDiagnosticTimer = 0f;
        int selectedX = Mathf.Clamp(doorwaySurfaceDiagnosticVoxel.x, 0, Mathf.Max(bulkGridCountX - 1, 0));
        int selectedZ = Mathf.Clamp(doorwaySurfaceDiagnosticVoxel.y, 0, Mathf.Max(bulkGridCountZ - 1, 0));
        int radius = Mathf.Clamp(doorwaySurfaceDiagnosticRadius, 1, 4);
        tileCS.SetInt("_DoorwaySurfaceDiagnosticX", selectedX);
        tileCS.SetInt("_DoorwaySurfaceDiagnosticZ", selectedZ);
        tileCS.SetInt("_DoorwaySurfaceDiagnosticRadius", radius);
        tileCS.SetBuffer(kernelDoorwaySurfaceDiagnostic,
            "diagnosticSurfaceFields", surfaceFieldBuffer);
        tileCS.SetBuffer(kernelDoorwaySurfaceDiagnostic,
            "diagnosticPreviousSurfaceFields", surfaceFieldWriteBuffer);
        tileCS.SetBuffer(kernelDoorwaySurfaceDiagnostic,
            "doorwaySurfaceDiagnostics", doorwaySurfaceDiagnosticBuffer);
        tileCS.Dispatch(kernelDoorwaySurfaceDiagnostic, 1, 1, 1);

        int gapScanEntryCount = Mathf.Max(1,
            bulkGridCountX * bulkGridCountZ * MaxDeckSurfaceSlots);
        if (doorwaySurfaceGapScanBuffer == null ||
            doorwaySurfaceGapScanBuffer.count != gapScanEntryCount)
        {
            doorwaySurfaceGapScanBuffer?.Release();
            doorwaySurfaceGapScanBuffer = new ComputeBuffer(
                gapScanEntryCount, DoorwaySurfaceGapScanStride);
        }
        tileCS.SetBuffer(kernelDoorwaySurfaceGapScan,
            "diagnosticSurfaceFields", surfaceFieldBuffer);
        tileCS.SetBuffer(kernelDoorwaySurfaceGapScan,
            "diagnosticPreviousSurfaceFields", surfaceFieldWriteBuffer);
        tileCS.SetBuffer(kernelDoorwaySurfaceGapScan,
            "doorwaySurfaceGapScan", doorwaySurfaceGapScanBuffer);
        tileCS.Dispatch(kernelDoorwaySurfaceGapScan,
            Mathf.CeilToInt(bulkGridCountX / 8f),
            Mathf.CeilToInt(bulkGridCountZ / 8f),
            ActiveDeckSurfaceSlotCount);

        if (canScanDoorwayFaces)
        {
            int faceScanEntryCount = Mathf.Max(1,
                bulkGridCountX * bulkGridCountZ * MaxDeckSurfaceSlots * 2);
            if (doorwaySurfaceFaceScanBuffer == null ||
                doorwaySurfaceFaceScanBuffer.count != faceScanEntryCount)
            {
                doorwaySurfaceFaceScanBuffer?.Release();
                doorwaySurfaceFaceScanBuffer = new ComputeBuffer(
                    faceScanEntryCount, DoorwaySurfaceFaceScanStride);
            }
            tileCS.SetBuffer(kernelDoorwaySurfaceFaceScan,
                "diagnosticSurfaceFields", surfaceFieldBuffer);
            tileCS.SetBuffer(kernelDoorwaySurfaceFaceScan,
                "diagnosticPreviousSurfaceFields", surfaceFieldWriteBuffer);
            tileCS.SetBuffer(kernelDoorwaySurfaceFaceScan,
                "doorwaySurfaceFaceScan", doorwaySurfaceFaceScanBuffer);
            tileCS.Dispatch(kernelDoorwaySurfaceFaceScan,
                Mathf.CeilToInt(bulkGridCountX / 8f),
                Mathf.CeilToInt(bulkGridCountZ / 8f),
                ActiveDeckSurfaceSlotCount * 2);
        }

        doorwaySurfaceDiagnosticReadbackPending = true;
            ComputeBuffer requestedBuffer = doorwaySurfaceDiagnosticBuffer;
            int requestedFrame = Time.frameCount;
            int requestedHistoryFrame = surfaceFieldHistoryFrames;
            AsyncGPUReadback.Request(requestedBuffer, request =>
            {
                doorwaySurfaceDiagnosticReadbackPending = false;
                if (this == null || !logDoorwaySurfaceDiagnostic || request.hasError)
                    return;
    
                NativeArray<DoorwaySurfaceDiagnosticData> entries =
                    request.GetData<DoorwaySurfaceDiagnosticData>();
                if (!entries.IsCreated || entries.Length == 0)
                    return;
    
                LogDoorwaySurfaceDiagnostic(
                    entries, requestedFrame, requestedHistoryFrame,
                    selectedX, selectedZ, radius);
        });

        doorwaySurfaceGapScanReadbackPending = true;
        ComputeBuffer requestedGapScanBuffer = doorwaySurfaceGapScanBuffer;
        AsyncGPUReadback.Request(requestedGapScanBuffer, request =>
        {
            doorwaySurfaceGapScanReadbackPending = false;
            if (this == null || !logDoorwaySurfaceDiagnostic || request.hasError)
                return;
            NativeArray<DoorwaySurfaceGapScanData> entries =
                request.GetData<DoorwaySurfaceGapScanData>();
            if (!entries.IsCreated || entries.Length == 0)
                return;
            LogDoorwaySurfaceGapScan(
                entries, requestedFrame, requestedHistoryFrame);
        });

        if (canScanDoorwayFaces)
        {
            doorwaySurfaceFaceScanReadbackPending = true;
            ComputeBuffer requestedFaceScanBuffer = doorwaySurfaceFaceScanBuffer;
        AsyncGPUReadback.Request(requestedFaceScanBuffer, request =>
        {
            doorwaySurfaceFaceScanReadbackPending = false;
            if (this == null || !logDoorwaySurfaceDiagnostic || request.hasError)
                return;
            NativeArray<DoorwaySurfaceFaceData> entries =
                request.GetData<DoorwaySurfaceFaceData>();
            if (!entries.IsCreated || entries.Length == 0)
                return;
            LogDoorwaySurfaceFaceScan(
                entries, requestedFrame, requestedHistoryFrame);
            });
        }
    }

    void LogDoorwaySurfaceFaceScan(
        NativeArray<DoorwaySurfaceFaceData> entries,
        int requestedFrame, int requestedHistoryFrame)
    {
        var candidates = new List<DoorwaySurfaceFaceData>();
        int openWetFaceSlots = 0;
        int activeEntryCount = Mathf.Min(entries.Length,
            bulkGridCountX * bulkGridCountZ * ActiveDeckSurfaceSlotCount * 2);
        for (int i = 0; i < activeEntryCount; i++)
        {
            int flags = Mathf.RoundToInt(entries[i].topology.x);
            if ((flags & 1) == 0)
                continue;
            openWetFaceSlots++;
            if ((flags & 6) != 6 || (flags & 8) == 0 ||
                (flags & 16) == 0 || (flags & 96) != 96)
                candidates.Add(entries[i]);
        }
        candidates.Sort((a, b) =>
        {
            int aFlags = Mathf.RoundToInt(a.topology.x);
            int bFlags = Mathf.RoundToInt(b.topology.x);
            int aRank = ((aFlags & 6) != 6 ? 1000 : 0) +
                ((aFlags & 16) == 0 ? 500 : 0) +
                ((aFlags & 8) == 0 ? 250 : 0) +
                ((aFlags & 96) != 96 ? 100 : 0);
            int bRank = ((bFlags & 6) != 6 ? 1000 : 0) +
                ((bFlags & 16) == 0 ? 500 : 0) +
                ((bFlags & 8) == 0 ? 250 : 0) +
                ((bFlags & 96) != 96 ? 100 : 0);
            return bRank.CompareTo(aRank);
        });
        var message = new System.Text.StringBuilder(4600);
        message.Append("[DoorwayFaceScan] frame=").Append(requestedFrame)
            .Append(" surfaceFrame=").Append(requestedHistoryFrame)
            .Append(" openWetFaceSlots=").Append(openWetFaceSlots)
            .Append(" candidates=").Append(candidates.Count)
            .Append(" directions=+X,+Z")
            .Append(" (open hydraulic faces with actual surface pairs; read-only)");
        int emitCount = Mathf.Min(candidates.Count, 12);
        for (int i = 0; i < emitCount; i++)
        {
            DoorwaySurfaceFaceData entry = candidates[i];
            int sourceX = Mathf.RoundToInt(entry.meta.x);
            int sourceZ = Mathf.RoundToInt(entry.meta.y);
            int sourceSlot = Mathf.RoundToInt(entry.meta.z);
            bool positiveX = Mathf.RoundToInt(entry.meta.w) == 0;
            int targetX = sourceX + (positiveX ? 1 : 0);
            int targetZ = sourceZ + (positiveX ? 0 : 1);
            int flags = Mathf.RoundToInt(entry.topology.x);
            message.Append('\n').Append(positiveX ? "+X" : "+Z")
                .Append(" A(").Append(sourceX).Append(',').Append(sourceZ)
                .Append(") s").Append(sourceSlot)
                .Append(" has/layer/fill/h=").Append(entry.sourceSurface.x.ToString("F0"))
                .Append('/').Append(entry.sourceSurface.y.ToString("F0"))
                .Append('/').Append(entry.sourceSurface.w.ToString("F3"))
                .Append('/').Append(entry.sourceSurface.z.ToString("F3"))
                .Append(" cov(now/prev)=").Append(entry.coverage.x.ToString("F3"))
                .Append('/').Append(entry.coverage.y.ToString("F3"))
                .Append(" -> B(").Append(targetX).Append(',').Append(targetZ)
                .Append(") s").Append(entry.targetSurface.y.ToString("F0"))
                .Append(" has/layer/fill/h=").Append(entry.targetSurface.x.ToString("F0"))
                .Append('/').Append(entry.targetSurface.z.ToString("F0"))
                .Append('/').Append(entry.targetSurface.w.ToString("F3"))
                .Append('/').Append(entry.heights.x.ToString("F3"))
                .Append(" cov(now/prev)=").Append(entry.coverage.z.ToString("F3"))
                .Append('/').Append(entry.coverage.w.ToString("F3"))
                .Append(" linkLayer=").Append(entry.heights.w.ToString("F0"))
                .Append(" delta/band=").Append(entry.heights.y.ToString("F3"))
                .Append('/').Append(entry.heights.z.ToString("F3"))
                .Append(" flow(net/Aout/Bout)=").Append(entry.flow.x.ToString("F4"))
                .Append('/').Append(entry.flow.y.ToString("F4"))
                .Append('/').Append(entry.flow.z.ToString("F4"))
                .Append(" faceOpen=1 linkFill(A/B)=")
                .Append(entry.state.x.ToString("F3"))
                .Append('/').Append(entry.state.y.ToString("F3"))
                .Append(" sameLayer=").Append((flags & 8) != 0 ? 1 : 0)
                .Append(" heightOK=").Append((flags & 16) != 0 ? 1 : 0)
                .Append(" visibleA/B=").Append((flags & 32) != 0 ? 1 : 0)
                .Append('/').Append((flags & 64) != 0 ? 1 : 0)
                .Append(" slotChanged=").Append((flags & 128) != 0 ? 1 : 0);
        }
        Debug.Log(message.ToString(), this);
    }
    void LogDoorwaySurfaceGapScan(
        NativeArray<DoorwaySurfaceGapScanData> entries,
        int requestedFrame, int requestedHistoryFrame)
    {
        var candidates = new List<DoorwaySurfaceGapScanData>();
        int activeEntryCount = Mathf.Min(entries.Length,
            bulkGridCountX * bulkGridCountZ * ActiveDeckSurfaceSlotCount);
        for (int i = 0; i < activeEntryCount; i++)
        {
            if (Mathf.RoundToInt(entries[i].topology.x) != 0)
                candidates.Add(entries[i]);
        }
        candidates.Sort((a, b) =>
        {
            int af = Mathf.RoundToInt(a.topology.x);
            int bf = Mathf.RoundToInt(b.topology.x);
            int aRank = ((af & 6) != 0 ? 1000 : 0) +
                ((af & 8) != 0 ? 300 : 0) + ((af & 64) != 0 ? 200 : 0) +
                CountDoorwayMaskBits(Mathf.RoundToInt(a.topology.z)) * 10;
            int bRank = ((bf & 6) != 0 ? 1000 : 0) +
                ((bf & 8) != 0 ? 300 : 0) + ((bf & 64) != 0 ? 200 : 0) +
                CountDoorwayMaskBits(Mathf.RoundToInt(b.topology.z)) * 10;
            return bRank.CompareTo(aRank);
        });

        int bracketedCount = 0;
        int lostCount = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            int flags = Mathf.RoundToInt(candidates[i].topology.x);
            if ((flags & 6) != 0) bracketedCount++;
            if ((flags & 64) != 0) lostCount++;
        }
        var message = new System.Text.StringBuilder(3000);
        message.Append("[DoorwayGapScan] frame=").Append(requestedFrame)
            .Append(" surfaceFrame=").Append(requestedHistoryFrame)
            .Append(" candidates=").Append(candidates.Count)
            .Append(" bracketed=").Append(bracketedCount)
            .Append(" justLost=").Append(lostCount)
            .Append(" maskOrder=+X,-X,+Z,-Z")
            .Append(" (read-only automatic whole-grid scan)");
        int emitCount = Mathf.Min(candidates.Count, 12);
        for (int i = 0; i < emitCount; i++)
        {
            DoorwaySurfaceGapScanData entry = candidates[i];
            int flags = Mathf.RoundToInt(entry.topology.x);
            string kind = (flags & 6) != 0 ? "BRACKETED" : "EDGE";
            message.Append('\n').Append(kind).Append(" (")
                .Append(Mathf.RoundToInt(entry.meta.x)).Append(',')
                .Append(Mathf.RoundToInt(entry.meta.y)).Append(") s")
                .Append(Mathf.RoundToInt(entry.meta.z))
                .Append(" layer=").Append(entry.target.w.ToString("F0"))
                .Append(" fill=").Append(entry.meta.w.ToString("F3"))
                .Append(" target/cut=").Append(entry.target.y.ToString("F3"))
                .Append('/').Append(entry.target.z.ToString("F3"))
                .Append(" ownNow/prev=").Append(entry.ownCoverage.x.ToString("F3"))
                .Append('/').Append(entry.ownCoverage.y.ToString("F3"))
                .Append(" currentNbr=").Append(FormatDoorwayCoverageVector(entry.currentNeighbours))
                .Append(" previousNbr=").Append(FormatDoorwayCoverageVector(entry.previousNeighbours))
                .Append(" wet=").Append(FormatDoorwayDirectionMask(Mathf.RoundToInt(entry.topology.y)))
                .Append(" visibleNow=").Append(FormatDoorwayDirectionMask(Mathf.RoundToInt(entry.topology.z)))
                .Append(" visiblePrev=").Append(FormatDoorwayDirectionMask(Mathf.RoundToInt(entry.topology.w)))
                .Append(" enclosed=").Append((flags & 16) != 0 ? 1 : 0)
                .Append(" allWet=").Append((flags & 32) != 0 ? 1 : 0)
                .Append(" justLost=").Append((flags & 64) != 0 ? 1 : 0);
        }
        Debug.Log(message.ToString(), this);
    }

    static int CountDoorwayMaskBits(int mask)
    {
        int count = 0;
        for (int bit = 0; bit < 4; bit++)
            if ((mask & (1 << bit)) != 0) count++;
        return count;
    }

    void LogDoorwaySurfaceDiagnostic(
        NativeArray<DoorwaySurfaceDiagnosticData> entries,
        int requestedFrame, int requestedHistoryFrame,
        int selectedX, int selectedZ, int radius)
    {
        string[] sampleNames = { "C", "+X", "-X", "+Z", "-Z" };
        int activeSlots = ActiveDeckSurfaceSlotCount;
        bool selectionInGrid = selectedX >= 0 && selectedZ >= 0 &&
            selectedX < bulkGridCountX && selectedZ < bulkGridCountZ;
        var message = new System.Text.StringBuilder(5200);
        message.Append("[DoorwaySurfaceDiagnostic] frame=").Append(requestedFrame)
            .Append(" surfaceFrame=").Append(requestedHistoryFrame)
            .Append(" selected=(").Append(selectedX).Append(',').Append(selectedZ).Append(')')
            .Append(" grid=(").Append(bulkGridCountX).Append(',').Append(bulkGridCountZ).Append(')')
            .Append(" slots=").Append(activeSlots)
            .Append(" radius=").Append(radius)
            .Append(" seed=").Append(requestedHistoryFrame <= Mathf.Clamp(surfaceFieldInitialSeedFrames, 2, 30) ? 1 : 0)
            .Append(" maskOrder=+X,-X,+Z,-Z");

        int count = Mathf.Min(entries.Length, DoorwaySurfaceDiagnosticEntryCount);
        int emittedRows = 0;
        for (int i = 0; i < count; i++)
        {
            int sample = i / MaxDeckSurfaceSlots;
            int slot = i % MaxDeckSurfaceSlots;
            if (sample >= sampleNames.Length || slot >= activeSlots)
                continue;

            DoorwaySurfaceDiagnosticData entry = entries[i];
            int x = Mathf.RoundToInt(entry.meta.x);
            int z = Mathf.RoundToInt(entry.meta.y);
            if (x < 0 || z < 0)
                continue;

            int flags = Mathf.RoundToInt(entry.topology.x);
            bool hasTarget = (flags & 1) != 0;
            if (!hasTarget && entry.outputCoverage.w <= 0.0001f)
                continue;

            string bracket = (flags & 8) != 0
                ? ((flags & 16) != 0 ? "XZ" : "X")
                : ((flags & 16) != 0 ? "Z" : "-");
            emittedRows++;
            message.Append('\n').Append(sampleNames[sample]).Append('(')
                .Append(x).Append(',').Append(z).Append(") s").Append(slot)
                .Append(" fill=").Append(entry.meta.w.ToString("F3"))
                .Append(" target(raw/eff/contour)=")
                .Append(entry.targetCoverage.x.ToString("F3")).Append('/')
                .Append(entry.targetCoverage.y.ToString("F3")).Append('/')
                .Append(entry.targetCoverage.z.ToString("F3"))
                .Append(" out(c/min/avg/max)=")
                .Append(entry.outputCoverage.x.ToString("F3")).Append('/')
                .Append(entry.outputCoverage.y.ToString("F3")).Append('/')
                .Append(entry.outputCoverage.z.ToString("F3")).Append('/')
                .Append(entry.outputCoverage.w.ToString("F3"))
                .Append(" flow=").Append(entry.targetCoverage.w.ToString("F3"))
                .Append(" direct=").Append((flags & 2) != 0 ? 1 : 0)
                .Append(" enclosed=").Append((flags & 4) != 0 ? 1 : 0)
                .Append(" bracket=").Append(bracket)
                .Append(" heightOK=").Append((flags & 512) != 0 ? 1 : 0)
                .Append(" h(target/current/diff/band)=")
                .Append(entry.heights.x.ToString("F3")).Append('/')
                .Append(entry.heights.y.ToString("F3")).Append('/')
                .Append(entry.heights.z.ToString("F3")).Append('/')
                .Append(entry.heights.w.ToString("F3"))
                .Append(" identity(layer/slotNow/slotPrev/switched)=")
                .Append(entry.identity.x.ToString("F0")).Append('/')
                .Append(entry.identity.y.ToString("F0")).Append('/')
                .Append(entry.identity.z.ToString("F0")).Append('/')
                .Append(entry.identity.w > 0.5f ? 1 : 0)
                .Append(" fieldNowPrev(base/cov)=")
                .Append(entry.frameIdentity.x.ToString("F3")).Append('/')
                .Append(entry.frameIdentity.y.ToString("F3")).Append('|')
                .Append(entry.frameIdentity.z.ToString("F3")).Append('/')
                .Append(entry.frameIdentity.w.ToString("F3"))
                .Append(" footprintNowPrev(nz/visible)=")
                .Append(entry.footprintCounts.x.ToString("F0")).Append('/')
                .Append(entry.footprintCounts.y.ToString("F0")).Append('|')
                .Append(entry.footprintCounts.z.ToString("F0")).Append('/')
                .Append(entry.footprintCounts.w.ToString("F0"))
                .Append(" donorState(+X,-X,+Z,-Z)=")
                .Append(FormatDoorwayDonorStates(
                    entry.donorAnyCoverage,
                    entry.donorCompatibleCoverage,
                    entry.donorPreferredCoverage,
                    entry.targetCoverage.z))
                .Append(" donorMax(compat/preferred)=")
                .Append(FormatDoorwayCoverageVector(entry.donorCompatibleCoverage))
                .Append('/')
                .Append(FormatDoorwayCoverageVector(entry.donorPreferredCoverage))
                .Append(" nearestVisible12Now=")
                .Append(FormatDoorwayNearestVisible(entry.nearestVisibleCurrent))
                .Append(" nearestVisible12Prev=")
                .Append(FormatDoorwayNearestVisible(entry.nearestVisiblePrevious))
                .Append(" wet=").Append(FormatDoorwayDirectionMask(Mathf.RoundToInt(entry.topology.y)))
                .Append(" solidNbr=").Append(FormatDoorwayDirectionMask(Mathf.RoundToInt(entry.topology.z)))
                .Append(" compatible=").Append(Mathf.RoundToInt(entry.topology.w))
                .Append(" wave(valid/eta/base/depth/layer)=")
                .Append((flags & 256) != 0 ? 1 : 0).Append('/')
                .Append(entry.wave.x.ToString("F3")).Append('/')
                .Append(entry.wave.y.ToString("F3")).Append('/')
                .Append(entry.wave.z.ToString("F3")).Append('/')
                .Append(entry.wave.w.ToString("F0"));
        }
        if (emittedRows == 0)
        {
            message.Append("\nNO_VALID_SAMPLES selectionInGrid=")
                .Append(selectionInGrid ? 1 : 0)
                .Append(" (choose a wet voxel coordinate inside grid)");
        }

        Debug.Log(message.ToString(), this);
    }

    static string FormatDoorwayDirectionMask(int mask)
    {
        if ((mask & 15) == 0)
            return "-";
        var directions = new System.Text.StringBuilder(12);
        if ((mask & 1) != 0) directions.Append("+X,");
        if ((mask & 2) != 0) directions.Append("-X,");
        if ((mask & 4) != 0) directions.Append("+Z,");
        if ((mask & 8) != 0) directions.Append("-Z,");
        directions.Length--;
        return directions.ToString();
    }

    static string FormatDoorwayDonorStates(
        Vector4 anyCoverage, Vector4 compatibleCoverage,
        Vector4 preferredCoverage, float visibleContour)
    {
        var states = new System.Text.StringBuilder(15);
        for (int direction = 0; direction < 4; direction++)
        {
            if (direction > 0)
                states.Append(',');
            float any = anyCoverage[direction];
            float compatible = compatibleCoverage[direction];
            float preferred = preferredCoverage[direction];
            if (any <= 0.0001f)
                states.Append('E'); // Empty neighbouring fine footprint.
            else if (compatible <= 0.0001f)
                states.Append('H'); // Coverage exists, but at an incompatible height.
            else if (preferred <= 0.0001f)
                states.Append('S'); // Compatible coverage exists only in another slot.
            else if (preferred < visibleContour)
                states.Append('B'); // Preferred-slot donor exists but is below contour.
            else
                states.Append('V'); // Visible compatible donor in the preferred slot.
        }
        return states.ToString();
    }

    static string FormatDoorwayCoverageVector(Vector4 coverage)
    {
        return coverage.x.ToString("F3") + ',' +
            coverage.y.ToString("F3") + ',' +
            coverage.z.ToString("F3") + ',' +
            coverage.w.ToString("F3");
    }

    static string FormatDoorwayNearestVisible(Vector4 nearest)
    {
        if (nearest.z < 0f)
            return "NONE";
        return "(dx=" + Mathf.RoundToInt(nearest.x) +
            ",dz=" + Mathf.RoundToInt(nearest.y) +
            ",dist=" + nearest.z.ToString("F2") +
            ",cov=" + nearest.w.ToString("F3") + ")";
    }

    void ApplySurfaceStats(NativeArray<TileData> cpuTiles, float readbackDeltaTime)
    {
        int wetCount = 0;
        int bodyCount = 0;
        int airCount = 0;
        float heightSum = 0f;
        float bodyHeightSum = 0f;
        int bodyHeightCount = 0;
        float maxHeight = boundsMin.y;
        float maxBodyParticleHeight = boundsMin.y;
        int minBodyX = tileCountX;
        int maxBodyX = -1;
        int minBodyZ = tileCountZ;
        int maxBodyZ = -1;

        int tileReadCount = Mathf.Min(totalTiles, cpuTiles.Length);
        for (int i = 0; i < tileReadCount; i++)
        {
            if (cpuTiles[i].active == 0)
                continue;

            wetCount++;
            heightSum += cpuTiles[i].height;
            if (cpuTiles[i].height > maxHeight)
                maxHeight = cpuTiles[i].height;

            if (cpuTiles[i].active == 1)
            {
                bodyCount++;
                int tileX = i % tileCountX;
                int tileZ = i / tileCountX;
                minBodyX = Mathf.Min(minBodyX, tileX);
                maxBodyX = Mathf.Max(maxBodyX, tileX);
                minBodyZ = Mathf.Min(minBodyZ, tileZ);
                maxBodyZ = Mathf.Max(maxBodyZ, tileZ);

                float rawParticleHeight = Mathf.Max(boundsMin.y, cpuTiles[i].padding.z);
                bodyHeightSum += rawParticleHeight;
                bodyHeightCount++;
                if (rawParticleHeight > maxBodyParticleHeight)
                    maxBodyParticleHeight = rawParticleHeight;
            }
            else if (cpuTiles[i].active == 2)
            {
                airCount++;
            }
        }

        wetTileCount = wetCount;
        mainBodyTileCount = bodyCount;
        airborneTileCount = airCount;
        mainBodyFootprintTileArea = bodyCount > 0
            ? (maxBodyX - minBodyX + 1) * (maxBodyZ - minBodyZ + 1)
            : 0;
        UpdateFirstLayerRiseGate(mainBodyFootprintTileArea, readbackDeltaTime);
        wetCoveragePercent = totalTiles > 0 ? (float)wetCount / totalTiles * 100f : 0f;
        maxTileSurfaceHeight = wetCount > 0 ? maxHeight : boundsMin.y;
        averageWetTileHeight = wetCount > 0 ? heightSum / wetCount : boundsMin.y;
        averageMainBodyParticleHeight = bodyHeightCount > 0 ? bodyHeightSum / bodyHeightCount : floorY;
        liveParticleMainBodyHeight = bodyHeightCount > 0 ? maxBodyParticleHeight : floorY;
        mainBodySurfaceLevel = averageMainBodyParticleHeight;
    }

    void UpdateBulkDebugReadback()
    {
        if (!showBulkDebugOverlay || bulkWaterBuffer == null)
            return;

        int bulkCellCount = Mathf.Max(1, bulkGridCountX * bulkGridCountZ);
        if (debugBulkCells == null || debugBulkCells.Length != bulkCellCount)
            debugBulkCells = new BulkWaterCellData[bulkCellCount];

        int readCount = Mathf.Min(bulkCellCount, bulkWaterBuffer.count);
        if (readCount <= 0)
            return;

        bulkWaterBuffer.GetData(debugBulkCells, 0, 0, readCount);
        for (int i = readCount; i < debugBulkCells.Length; i++)
            debugBulkCells[i] = BulkWaterCellData.MakeDisabled(boundsMin.y - 1000f);
    }

    void OnDrawGizmos()
    {
        if (!showBulkDebugOverlay || debugBulkCells == null || debugBulkCells.Length == 0)
            return;

        int countX = Mathf.Max(bulkGridCountX, 1);
        int countZ = Mathf.Max(bulkGridCountZ, 1);
        float coarseSizeX = Mathf.Max((boundsMax.x - boundsMin.x) / countX, 0.01f);
        float coarseSizeZ = Mathf.Max((boundsMax.z - boundsMin.z) / countZ, 0.01f);
        Vector3 cellSize = new Vector3(coarseSizeX * bulkDebugTileInset, 0.025f, coarseSizeZ * bulkDebugTileInset);
        int count = Mathf.Min(countX * countZ, debugBulkCells.Length);

        for (int i = 0; i < count; i++)
        {
            BulkWaterCellData bulkCell = debugBulkCells[i];
            bool activePbfTile = bulkCell.activeCount > 0;
            bool hasBulk = bulkCell.volume > 0.0001f;

            if (!activePbfTile && !hasBulk)
                continue;

            bool missingSurface = hasBulk && !activePbfTile;
            bool exposedCandidate = hasBulk &&
                !missingSurface &&
                bulkCell.activeCount <= bulkDebugExposedActiveThreshold &&
                bulkCell.surfaceY < mainBodySurfaceLevel - Mathf.Max(tileSize * 0.25f, 0.05f);

            Color color = hasBulk ? bulkDebugStoredVolumeColor : bulkDebugActivePbfColor;
            if (activePbfTile)
                color = bulkDebugActivePbfColor;
            if (exposedCandidate)
                color = bulkDebugExposedColor;
            if (missingSurface)
                color = bulkDebugMismatchColor;

            int cellX = i % countX;
            int cellZ = i / countX;
            float worldX = boundsMin.x + (cellX + 0.5f) * coarseSizeX;
            float worldZ = boundsMin.z + (cellZ + 0.5f) * coarseSizeZ;
            float drawY = hasBulk ? bulkCell.supportY : boundsMin.y;

            Gizmos.color = color;
            Gizmos.DrawCube(
                new Vector3(worldX, drawY + bulkDebugTileHeightOffset, worldZ),
                cellSize);
        }
    }

    void UpdateFirstLayerRiseGate(int footprintTileArea, float deltaTime)
    {
        if (!waitForStableFootprintBeforeRising)
        {
            firstLayerRiseGate = 1f;
            footprintStableTimer = 0f;
            firstLayerWaitTimer = 0f;
            previousMainBodyFootprintTileArea = footprintTileArea;
            return;
        }

        if (footprintTileArea <= 0)
        {
            firstLayerRiseGate = 0f;
            footprintStableTimer = 0f;
            firstLayerWaitTimer = 0f;
            previousMainBodyFootprintTileArea = 0;
            return;
        }

        firstLayerWaitTimer += deltaTime;

        bool footprintStillGrowing =
            footprintTileArea > previousMainBodyFootprintTileArea + footprintGrowthTileTolerance;

        if (footprintStillGrowing)
        {
            footprintStableTimer = 0f;
        }
        else
        {
            footprintStableTimer += deltaTime;
        }

        bool stableEnough = footprintStableTimer >= footprintStableSeconds;
        bool waitedTooLong = firstLayerMaxWaitSeconds > 0.001f
            && firstLayerWaitTimer >= firstLayerMaxWaitSeconds;

        if (stableEnough || waitedTooLong)
        {
            float riseStep = firstLayerRiseBlendSeconds > 0.001f
                ? deltaTime / firstLayerRiseBlendSeconds
                : 1f;
            firstLayerRiseGate = Mathf.MoveTowards(firstLayerRiseGate, 1f, riseStep);
        }
        else if (firstLayerRiseGate < 0.99f)
        {
            firstLayerRiseGate = 0f;
        }

        previousMainBodyFootprintTileArea = footprintTileArea;
    }

    void OnDestroy()
    {
        tileBuffer?.Release();
        tileWriteBuffer?.Release();
        tileFlowBuffer?.Release();
        surfaceFieldBuffer?.Release();
        surfaceFieldWriteBuffer?.Release();
        doorwaySurfaceDiagnosticBuffer?.Release();
        doorwaySurfaceGapScanBuffer?.Release();
        doorwaySurfaceFaceScanBuffer?.Release();
        surfaceVisualBaseDiagnosticBuffer?.Release();
        fallbackBulkWaterBuffer?.Release();
        fallbackPrimaryVoxelFlowBuffer?.Release();
        fallbackPrimarySurfaceWaveBuffer?.Release();
        fallbackPrimaryVoxelSolidBuffer?.Release();
        fallbackPrimaryVoxelFaceOpenBuffer?.Release();
        fallbackPrimaryVoxelFaceFluxBuffer?.Release();

        if (generatedDepthMesh != null)
            Destroy(generatedDepthMesh);
        if (generatedHeightmapMesh != null)
            Destroy(generatedHeightmapMesh);
        if (generatedVoxelCubeMesh != null)
            Destroy(generatedVoxelCubeMesh);
        if (generatedVoxelMaterial != null)
            Destroy(generatedVoxelMaterial);
        if (generatedMicroVolumeMaterial != null)
            Destroy(generatedMicroVolumeMaterial);        if (generatedSprayMaterial != null)
            Destroy(generatedSprayMaterial);
        sprayIndirectArgsBuffer?.Release();
    }

    Mesh GetHeightmapMesh()
    {
        int step = Mathf.Max(1, heightmapVertexStep);
        if (generatedHeightmapMesh == null
            || generatedHeightmapX != tileCountX
            || generatedHeightmapZ != tileCountZ
            || generatedHeightmapStep != step)
        {
            if (generatedHeightmapMesh != null)
                Destroy(generatedHeightmapMesh);

            generatedHeightmapMesh = CreateHeightmapSurfaceMesh(step);
            generatedHeightmapX = tileCountX;
            generatedHeightmapZ = tileCountZ;
            generatedHeightmapStep = step;
        }

        return generatedHeightmapMesh;
    }

    Mesh GetDrawMesh()
    {
        if (!useGeneratedDepthMesh)
            return tileMesh;

        int subdivisions = Mathf.Max(1, topSubdivisions);
        if (generatedDepthMesh == null || generatedDepthMeshSubdivisions != subdivisions)
        {
            if (generatedDepthMesh != null)
                Destroy(generatedDepthMesh);

            generatedDepthMesh = CreateOpenBoxTileMesh();
            generatedDepthMeshSubdivisions = subdivisions;
        }

        return generatedDepthMesh;
    }

    Mesh CreateOpenBoxTileMesh()
    {
        Mesh mesh = new Mesh();
        mesh.name = "Generated Water Tile Depth Mesh";

        int subdivisions = Mathf.Max(1, topSubdivisions);
        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<int> indices = new List<int>();

        for (int z = 0; z <= subdivisions; z++)
        {
            float vz = Mathf.Lerp(-0.5f, 0.5f, (float)z / subdivisions);
            for (int x = 0; x <= subdivisions; x++)
            {
                float vx = Mathf.Lerp(-0.5f, 0.5f, (float)x / subdivisions);
                vertices.Add(new Vector3(vx, 1f, vz));
                normals.Add(Vector3.up);
            }
        }

        int row = subdivisions + 1;
        for (int z = 0; z < subdivisions; z++)
        {
            for (int x = 0; x < subdivisions; x++)
            {
                int a = x + z * row;
                int b = x + (z + 1) * row;
                int c = x + 1 + (z + 1) * row;
                int d = x + 1 + z * row;
                indices.Add(a); indices.Add(b); indices.Add(c);
                indices.Add(a); indices.Add(c); indices.Add(d);
            }
        }

        AddQuad(vertices, normals, indices,
            new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 1f, -0.5f), new Vector3(0.5f, 1f, -0.5f), new Vector3(0.5f, 0f, -0.5f), Vector3.back);
        AddQuad(vertices, normals, indices,
            new Vector3(0.5f, 0f, -0.5f), new Vector3(0.5f, 1f, -0.5f), new Vector3(0.5f, 1f, 0.5f), new Vector3(0.5f, 0f, 0.5f), Vector3.right);
        AddQuad(vertices, normals, indices,
            new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 1f, 0.5f), new Vector3(-0.5f, 1f, 0.5f), new Vector3(-0.5f, 0f, 0.5f), Vector3.forward);
        AddQuad(vertices, normals, indices,
            new Vector3(-0.5f, 0f, 0.5f), new Vector3(-0.5f, 1f, 0.5f), new Vector3(-0.5f, 1f, -0.5f), new Vector3(-0.5f, 0f, -0.5f), Vector3.left);
        AddQuad(vertices, normals, indices,
            new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f), Vector3.down);

        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(indices, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    Mesh CreateHeightmapSurfaceMesh(int step)
    {
        int countX = Mathf.Max(2, Mathf.CeilToInt((Mathf.Max(tileCountX, 2) - 1) / (float)step) + 1);
        int countZ = Mathf.Max(2, Mathf.CeilToInt((Mathf.Max(tileCountZ, 2) - 1) / (float)step) + 1);

        Vector3[] vertices = new Vector3[countX * countZ];
        Vector3[] normals = new Vector3[vertices.Length];
        Vector2[] uv = new Vector2[vertices.Length];

        for (int z = 0; z < countZ; z++)
        {
            int tileZ = Mathf.Min(z * step, tileCountZ - 1);
            for (int x = 0; x < countX; x++)
            {
                int tileX = Mathf.Min(x * step, tileCountX - 1);
                int i = x + z * countX;
                vertices[i] = new Vector3(tileX, 0f, tileZ);
                normals[i] = Vector3.up;
                uv[i] = new Vector2(
                    tileCountX > 1 ? tileX / (float)(tileCountX - 1) : 0f,
                    tileCountZ > 1 ? tileZ / (float)(tileCountZ - 1) : 0f);
            }
        }

        int[] indices = new int[(countX - 1) * (countZ - 1) * 6];
        int t = 0;
        for (int z = 0; z < countZ - 1; z++)
        {
            for (int x = 0; x < countX - 1; x++)
            {
                int a = x + z * countX;
                int b = x + (z + 1) * countX;
                int c = x + 1 + (z + 1) * countX;
                int d = x + 1 + z * countX;
                // Alternate the diagonal split so interpolated height/coverage
                // contours cannot inherit one preferred world-space slant.
                if (((x + z) & 1) == 0)
                {
                    indices[t++] = a; indices[t++] = b; indices[t++] = c;
                    indices[t++] = a; indices[t++] = c; indices[t++] = d;
                }
                else
                {
                    indices[t++] = a; indices[t++] = b; indices[t++] = d;
                    indices[t++] = b; indices[t++] = c; indices[t++] = d;
                }
            }
        }

        Mesh mesh = new Mesh();
        mesh.name = "Generated Water Heightmap Surface";
        if (vertices.Length > 65000)
            mesh.indexFormat = IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uv;
        mesh.triangles = indices;
        mesh.bounds = new Bounds(
            (boundsMin + boundsMax) * 0.5f,
            boundsMax - boundsMin + Vector3.one * 10f);
        return mesh;
    }

    static void AddQuad(List<Vector3> vertices, List<Vector3> normals, List<int> indices,
                        Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
    {
        int start = vertices.Count;
        vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
        normals.Add(normal); normals.Add(normal); normals.Add(normal); normals.Add(normal);
        indices.Add(start); indices.Add(start + 1); indices.Add(start + 2);
        indices.Add(start); indices.Add(start + 2); indices.Add(start + 3);
    }

    struct TileData
    {
        public Vector3 worldPos;
        public float height;
        public int active;
        public Vector3 padding;
    }

    struct SurfaceFieldData
    {
        public float height;
        public float coverage;
        public Vector2 flow;
        public float thickness;
        public float confidence;
        public Vector2 padding;
    }

    struct SurfaceVisualBaseDiagnosticData
    {
        public Vector4 baseHeights;
        public Vector4 steps;
    }
    struct DoorwaySurfaceDiagnosticData
    {
        public Vector4 meta;
        public Vector4 targetCoverage;
        public Vector4 outputCoverage;
        public Vector4 heights;
        public Vector4 wave;
        public Vector4 topology;
        public Vector4 identity;
        public Vector4 frameIdentity;
        public Vector4 footprintCounts;
        public Vector4 donorAnyCoverage;
        public Vector4 donorCompatibleCoverage;
        public Vector4 donorPreferredCoverage;
        public Vector4 nearestVisibleCurrent;
        public Vector4 nearestVisiblePrevious;
    }

    struct DoorwaySurfaceGapScanData
    {
        public Vector4 meta;
        public Vector4 target;
        public Vector4 ownCoverage;
        public Vector4 currentNeighbours;
        public Vector4 previousNeighbours;
        public Vector4 topology;
    }

    struct DoorwaySurfaceFaceData
    {
        public Vector4 meta;
        public Vector4 sourceSurface;
        public Vector4 targetSurface;
        public Vector4 heights;
        public Vector4 coverage;
        public Vector4 flow;
        public Vector4 state;
        public Vector4 topology;
    }

    struct BulkWaterCellData
    {
        public float surfaceY;
        public float sphBottomY;
        public float supportY;
        public float volume;
        public int activeCount;
        public int bulkCount;
        public int spawnedThisStep;
        public int absorbedThisStep;
        public int activeBandCount;
        public int lowerBandCount;
        public int absorptionCreditMilliTokens;

        public static BulkWaterCellData MakeDisabled(float hiddenY)
        {
            return new BulkWaterCellData
            {
                surfaceY = hiddenY,
                sphBottomY = hiddenY,
                supportY = hiddenY,
                volume = 0f,
                activeCount = 0,
                bulkCount = 0,
                spawnedThisStep = 0,
                absorbedThisStep = 0,
                activeBandCount = 0,
                lowerBandCount = 0,
                absorptionCreditMilliTokens = 0
            };
        }
    }
}
