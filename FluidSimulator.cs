using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

public class FluidSimulator : MonoBehaviour
{
    // ------------------------------------------------------------------------
    // SECTION: Scene references
    // These are optional runtime hookups to the particle debug renderer and
    // the tile-based water surface renderer.
    // ------------------------------------------------------------------------
    [Header("References")]
    public GPUFluidRenderer gpuRenderer;
    public SurfaceTileRenderer surfaceTileRenderer;

    [Header("Profiling Toggles")]
    [Tooltip("Skips WaterTiles compute dispatch/draw and surface-particle rendering so only the particle sim/debug renderer runs.")]
    public bool disableSurfaceTileRendererForProfiling = false;

    // ------------------------------------------------------------------------
    // SECTION: World-space simulation bounds
    // Particles are clamped inside this AABB in the compute shader.
    // ------------------------------------------------------------------------
    [Header("Simulation Bounds")]
    public Vector3 boundsMin = new Vector3(-20, -2, -20);
    public Vector3 boundsMax = new Vector3(20, 6, 20);

    [Header("Debug Check Bounds")]
    public bool useCustomDebugBounds = false;
    public Vector3 debugBoundsMin = new Vector3(-20, -2, -20);
    public Vector3 debugBoundsMax = new Vector3(20, 6, 20);

    // ------------------------------------------------------------------------
    // SECTION: Particle allocation
    // maxParticles is the GPU buffer capacity. activeParticles is how many of
    // those slots are currently in use by live particles.
    // ------------------------------------------------------------------------
    [Header("Particle Budget")]
    public int maxParticles = 8000;
    public int activeParticles = 0;

    [Header("Runtime Counts")]
    public int liveParticles = 0;
    public int activeParticleCount = 0;
    public int dormantParticleCount = 0;
    public int bulkParticleCount = 0;
    public int bulkSpawnedLastStep = 0;
    public int bulkAbsorbedLastStep = 0;
    public int bulkBelowBandCandidatesLastStep = 0;
    public int bulkStableCandidatesLastStep = 0;
    public int bulkLocalSafetyBlockedLastStep = 0;
    public int bulkSpeedBlockedLastStep = 0;
    public int bulkDensityBlockedLastStep = 0;
    public int bulkNeighbourBlockedLastStep = 0;
    public int bulkSweepBlockedLastStep = 0;
    public int bulkPositionBlockedLastStep = 0;
    public int bulkWaitingForStabilityLastStep = 0;
    public int bulkReadyToCommitLastStep = 0;
    // Legacy bulk diagnostics are retained only for compatibility.
    [HideInInspector] public int bulkWetCellCount = 0;
    [HideInInspector] public int bulkActiveActivityCellCount = 0;
    [HideInInspector] public float bulkMaxHeadGradient = 0f;
    [HideInInspector] public float bulkAcceptedFluxVolume = 0f;
    [HideInInspector] public int bulkWokeLastStep = 0;
    // Legacy bulk voxel diagnostics are retained only for compatibility.
    [HideInInspector] public int voxelExpectedTokenCount = 0;
    [HideInInspector] public int voxelActualTokenCount = 0;
    [HideInInspector] public float voxelExpectedVolume = 0f;
    [HideInInspector] public float voxelActualVolume = 0f;
    [HideInInspector] public float voxelVolumeError = 0f;
    [Header("Primary Voxel Conservation Diagnostics")]
    public float primaryVoxelCumulativeInjectedVolume = 0f;
    public float primaryVoxelCumulativeAcceptedVolume = 0f;
    public float primaryVoxelCumulativeBoundaryOutflow = 0f;
    public float primaryVoxelCumulativeRejectedVolume = 0f;
    public float primaryVoxelCurrentVolume = 0f;
    public float primaryVoxelRetainedSurfaceVolume = 0f;
    public float primaryVoxelAcceptedInflowLastStep = 0f;
    public float primaryVoxelRejectedInflowLastStep = 0f;
    public int primaryVoxelHighestWetLayer = 0;
    public float primaryVoxelMaxCellFill = 0f;
    public float primaryVoxelCurrentSurfaceHeight = 0f;
    [Header("Primary Voxel Flow Diagnostics")]
    [Tooltip("Optional point inside the source pool. Used only to report its local surface and connected wet-basin range.")]
    public Transform primaryVoxelUpstreamProbe;
    [Tooltip("Optional point inside the downstream area. Used only to report its local surface and connected wet-basin range.")]
    public Transform primaryVoxelDownstreamProbe;
    [Tooltip("Optional point on the wall/overflow. Used to derive the local solid sill height for diagnostics.")]
    public Transform primaryVoxelOverflowProbe;
    [Tooltip("Use a known overflow crest height instead of deriving it from nearby solid voxels.")]
    public bool usePrimaryVoxelOverflowSillOverride = false;
    public float primaryVoxelOverflowSillHeight = 0f;
    public float primaryVoxelRequestedHorizontalFluxLastStep = 0f;
    public float primaryVoxelAcceptedHorizontalFluxLastStep = 0f;
    public float primaryVoxelAcceptedHorizontalFluxPerSecond = 0f;
    public float primaryVoxelOpenFaceHeadGradient = 0f;
    public float primaryVoxelBlockedFaceHeadGradient = 0f;
    public float primaryVoxelUpstreamSurfaceHeight = 0f;
    public float primaryVoxelDownstreamSurfaceHeight = 0f;
    public float primaryVoxelDerivedOverflowSillHeight = 0f;
    public int primaryVoxelOvertoppingFaceCountLastStep = 0;
    [Tooltip("Face-transfer events accumulated over the final voxel simulation step. This is intentionally separate from the unique live crest-face count.")]
    public int primaryVoxelOvertoppingFaceEventsLastStep = 0;
    public float primaryVoxelOvertoppingVolumeLastStep = 0f;
    public float primaryVoxelOvertoppingVolumePerSecond = 0f;
    public float primaryVoxelUpstreamBasinMinSurface = 0f;
    public float primaryVoxelUpstreamBasinMaxSurface = 0f;
    public float primaryVoxelDownstreamBasinMinSurface = 0f;
    public float primaryVoxelDownstreamBasinMaxSurface = 0f;
    public float primaryVoxelDeferredInflowVolume = 0f;
    [Header("Primary Voxel Main Console Diagnostics")]
    [Tooltip("Writes the two compact primary-voxel reports only after the existing asynchronous GPU readbacks complete. It never synchronises the GPU or stalls simulation.")]
    public bool logPrimaryVoxelMainDetails = false;
    [Tooltip("Real-time interval between the paired [Inflow Pressure Draft Details] and [Core Voxel Statistics] reports.")]
    [Min(0.5f)] public float primaryVoxelMainDetailsLogInterval = 2f;
    // Primary-voxel statistics above remain visible. Legacy particle/SPH diagnostics below are retained only for internal compatibility.
    [Tooltip("Wet means a non-solid primary voxel with fill above 0.01. These are live asynchronous readback values, not Inspector tuning settings.")]
    public int primaryVoxelWetCellCount = 0;
    public float primaryVoxelAverageWetFill = 0f;
    public float primaryVoxelAverageCompressionPressure = 0f;
    public float primaryVoxelMaximumCompressionPressure = 0f;
    public float primaryVoxelAverageGridSpeed = 0f;
    public float primaryVoxelMaximumGridSpeed = 0f;
    [Tooltip("Accepted directional volume transported by the final conservative voxel flux pass, in cubic metres per second. It is separate from the all-pass horizontal aggregate below.")]
    public float primaryVoxelAcceptedHorizontalFluxLastPassPerSecond = 0f;
    public float primaryVoxelAcceptedVerticalFluxLastPassPerSecond = 0f;
    public float estimatedSurfaceLevel = 0f;
    public float estimatedActiveBandBottom = 0f;
    [HideInInspector] public int outOfBoundsParticleCount = 0;
    [HideInInspector] public int insideDebugBoundsParticleCount = 0;
    [HideInInspector] public int outsideDebugBoundsParticleCount = 0;
    [HideInInspector] public int overlappingParticlePairCount = 0;
    [HideInInspector] public float averageOverlapDistance = 0f;
    [HideInInspector] public float averageCompressionPercent = 0f;
    [HideInInspector] public float worstOverlapDistance = 0f;
    [HideInInspector] public float worstCompressionPercent = 0f;
    [HideInInspector] public int overlapPairsAbove10Percent = 0;
    [HideInInspector] public int overlapPairsAbove25Percent = 0;
    [HideInInspector] public int overlapPairsAbove50Percent = 0;
    [HideInInspector] public int verticalOverlapPairCount = 0;
    [HideInInspector] public float averageVerticalOverlapDistance = 0f;
    [HideInInspector] public float averageVerticalCompressionPercent = 0f;
    [HideInInspector] public float worstVerticalOverlapDistance = 0f;
    [HideInInspector] public float worstVerticalCompressionPercent = 0f;
    [HideInInspector] public float estimatedParticleSpacingFromOverlap = 0f;
    [HideInInspector] public int estimatedParticlesToFillBoundsAtCurrentOverlap = 0;
    [HideInInspector] public float averageLiveDensity = 0f;
    [HideInInspector] public float averageLivePressure = 0f;
    [HideInInspector] public float averageDensityMinusRest = 0f;
    [HideInInspector] public float densityStandardDeviation = 0f;
    [HideInInspector] public float densityStdDevPercentOfAverage = 0f;
    [HideInInspector] public float densityStdDevPercentOfRest = 0f;
    [HideInInspector] public float averagePressureForceMagnitude = 0f;
    [HideInInspector] public float averageViscosityForceMagnitude = 0f;
    [HideInInspector] public float averageCohesionForceMagnitude = 0f;
    [HideInInspector] public float averageMergeSplitForceMagnitude = 0f;
    [HideInInspector] public float averagePressureDeltaVelocity = 0f;
    [HideInInspector] public float averageViscosityDeltaVelocity = 0f;
    [HideInInspector] public float averageCohesionDeltaVelocity = 0f;
    [HideInInspector] public float averageMergeSplitDeltaVelocity = 0f;
    [HideInInspector] public float maxPressureDeltaVelocity = 0f;
    [HideInInspector] public float maxViscosityDeltaVelocity = 0f;
    [HideInInspector] public float maxCohesionDeltaVelocity = 0f;
    [HideInInspector] public float maxMergeSplitDeltaVelocity = 0f;
    [HideInInspector] public float maxPressureForceMagnitude = 0f;
    [HideInInspector] public float maxViscosityForceMagnitude = 0f;
    [HideInInspector] public float maxCohesionForceMagnitude = 0f;
    [HideInInspector] public float maxMergeSplitForceMagnitude = 0f;
    [HideInInspector] public Vector3 averageActiveVelocity = Vector3.zero;
    [HideInInspector] public float averageActiveSpeed = 0f;

    [Header("Simulation Timing")]
    [Tooltip("How many fluid simulation steps to run per second, independent of render FPS.")]
    [Min(1f)] public float simulationRateHz = 120f;
    [Tooltip("Safety cap so a slow frame does not try to run unlimited catch-up simulation steps.")]
    [Range(1, 16)] public int maxSimulationStepsPerFrame = 8;
    public int lastSimulationStepsThisFrame = 0;
    public float currentSimulationDeltaTime = 0f;

    const int NormalSimulationStepsPerFrame = 2;

    // ------------------------------------------------------------------------
    // SECTION: Inflow / spawning
    // New particles are emitted from spawnPoint with random jitter inside a
    // small sphere so the inflow is not perfectly uniform.
    // ------------------------------------------------------------------------
    [Header("Inflow Settings")]
    public Transform spawnPoint;
    public float spawnRate = 200f;
    public float inflowRadius = 0.05f;
    public float inflowSpeed = 2.0f;
    public bool continuousSpawn = true;

    [Header("External Sea Breach Inflow")]
    [Tooltip("Uses a constant-head external sea and a finite physical breach. The orifice flux is limited by pressure head and reachable receiver capacity; water rejected by a full compartment remains outside.")]
    public bool usePrimaryVoxelBreachInflow = true;
    [Header("Breach Validation Controls")]
    [Tooltip("Runtime inlet state. Press 0 to close the sea reservoir and 1 to resume it without pausing the simulation.")]
    public bool primaryVoxelBreachOpen = true;
    [Tooltip("Enables 0 = close breach and 1 = resume breach during Play. When enabled, 1 no longer changes playback speed.")]
    public bool enablePrimaryVoxelBreachKeyboardControls = true;
    [Tooltip("World-space height of the replenished external sea surface. Inflow stops once the connected receiving water reaches this level.")]
    public float primaryVoxelExternalWaterLevel = 6f;
    [Header("Sea Level Reference")]
    [Tooltip("Optional scene sea-level Transform used as the base external pressure height.")]
    public Transform seaLevel;
    [Tooltip("When enabled, external pressure uses seaLevel.position.y plus the configured offset.")]
    public bool useSeaLevelTransformForPressure = false;
    [Tooltip("Height added to the referenced seaLevel Transform. Use this when the scene object is a volume/ground marker rather than a surface plane.")]
    public float seaLevelHeightOffset = 0f;
    [Tooltip("Physical width of the hull breach in metres. Area sets the maximum inflow rate.")]
    [Min(0.05f)] public float primaryVoxelBreachWidth = 0.6f;
    [Tooltip("Physical height of the hull breach in metres. Area sets the maximum inflow rate.")]
    [Min(0.05f)] public float primaryVoxelBreachHeight = 0.8f;
    [Tooltip("Orifice discharge coefficient. Around 0.6 is a useful default for a sharp damaged opening.")]
    [Range(0.05f, 1f)] public float primaryVoxelBreachDischargeCoefficient = 0.62f;
    [Tooltip("Safety cap on the injected jet velocity. The hydraulic discharge remains controlled by head and breach area.")]
    [Min(0.1f)] public float primaryVoxelBreachMaxJetSpeed = 8f;
    [Header("Flood Draft Coupling")]
    [Tooltip("Legacy fixed-ship approximation: raises the external pressure level by the computed flood draft. Disable this when using Kinematic Ship Draft Motion.")]
    public bool enablePrimaryVoxelFloodDraftCoupling = true;
    [Header("Kinematic Ship Draft Motion")]
    [Tooltip("Keeps the sea and voxel domain fixed in world space, then lowers Generated Ship Section by the flood-volume draft.")]
    public bool enablePrimaryVoxelShipDraftMotion = false;
    [Tooltip("Optional explicit ship root. If empty, the generated ship root is discovered after each ShipSectionBuilder rebuild.")]
    public Transform primaryVoxelShipRoot;
    [Tooltip("Cumulative vertical ship motion required before collider OBBs and voxel-solid topology are rebuilt. This is independent of rendered FPS.")]
    [Min(0.001f)] public float primaryVoxelShipColliderRebuildDistance = 0.025f;
    [Tooltip("World Y of the ship root before flood draft is applied. Captured automatically when the generated root is available.")]
    public float primaryVoxelInitialShipRootY;
    public float primaryVoxelCurrentShipRootY;
    public int primaryVoxelShipTopologyRefreshCount;
    [Tooltip("Approximate horizontal area of the floating hull at the waterline, in square metres. A 28 m by 28 m prototype section starts at 784.")]
    [Min(1f)] public float primaryVoxelWaterplaneArea = 784f;
    [Tooltip("Density used only to display the authoritative flooded-water mass. The voxel field itself remains volume based.")]
    [Min(1f)] public float primaryVoxelFloodwaterDensity = 1025f;
    [Tooltip("Filters the visual/hydraulic draft response to the low-rate asynchronous volume sample. This does not alter water mass.")]
    [Range(0f, 10f)] public float primaryVoxelDraftResponseSeconds = 0.75f;
    [Tooltip("Safety limit for the prototype's added draft. Set high enough that it never clips a legitimate sinking test.")]
    [Min(0f)] public float primaryVoxelMaximumAdditionalDraft = 12f;
    [Tooltip("How often the tiny authoritative-volume buffer is read back for draft coupling. This never reads the full voxel field.")]
    [Range(0.1f, 5f)] public float primaryVoxelDraftReadbackInterval = 0.5f;
    public float primaryVoxelFloodwaterMassKg = 0f;
    public float primaryVoxelAdditionalDraft = 0f;
    public float primaryVoxelEffectiveExternalWaterLevel = 0f;

    [Header("Primary Voxel Vertical Opening Check")]
    [Tooltip("Optional point centred in a hatch or stair opening, exactly on the deck plane. Use the FluidSimulator context menu to capture an asynchronous one-shot connectivity/flux snapshot.")]
    public Transform primaryVoxelVerticalOpeningProbe;
    public bool primaryVoxelVerticalOpeningProbeValid = false;
    public bool primaryVoxelVerticalOpeningFaceOpen = false;
    public bool primaryVoxelVerticalOpeningWetBelow = false;
    public bool primaryVoxelVerticalOpeningWetAbove = false;
    public float primaryVoxelVerticalOpeningUpwardFlux = 0f;

    [Header("Detached Spray")]
    [Tooltip("Enables a bounded cosmetic droplet pool. Spray never participates in PBF density or bulk volume.")]
    public bool enableSpray = true;
    [Range(128, 8192)] public int sprayParticleCapacity = 2048;
    [Min(0.01f)] public float spraySpawnSpeedThreshold = 2.5f;
    [Min(0f)] public float spraySpawnPressureThreshold = 0.15f;
    [Min(0f)] public float spraySpawnFluxThreshold = 0.05f;
    [Min(0f)] public float spraySpawnHeight = 0.35f;
    [Min(0.05f)] public float sprayLifetime = 0.8f;
    [Min(0f)] public float sprayGravityScale = 1f;
    [Min(0f)] public float sprayLaunchUpward = 1.5f;
    [Min(0f)] public float sprayLaunchLateral = 0.35f;
    [Min(0.001f)] public float sprayMinRadius = 0.035f;
    [Min(0.001f)] public float sprayMaxRadius = 0.09f;
    [Min(0f)] public float sprayReabsorbDepth = 0.08f;
    [Min(0.001f)] public float sprayFadeSeconds = 0.2f;

    [Header("Painted Voxel Current Splats")]
    [Tooltip("Visual-only soft splats selected from moving, filled primary voxels. They never affect water mass or the solver.")]
    public bool drawPrimaryVoxelCurrentSplats = true;
    [Tooltip("Reserved for the next phase. Keep off while validating the dense PBF-to-Gaussian surface layer.")]
    public bool drawPrimaryVoxelUnderwaterCurrentSplats = true;
    [Header("Gaussian Diagnostics")]
    [Tooltip("Logs non-blocking Gaussian surface movement counters to the Console.")]
    public bool logPrimaryVoxelSurfaceGaussianDiagnostics = true;
    [Min(0.25f)] public float primaryVoxelSurfaceGaussianDiagnosticsInterval = 1f;
    [Range(256, 4096)] public int primaryVoxelCurrentSplatCapacity = 4096;
    [Tooltip("Maximum number of independent underwater current tracers. They are visual-only and are retired as soon as their local flow falls below the threshold.")]
    [Range(64, 2048)] public int primaryVoxelUnderwaterSplatCount = 768;
    [Range(0.01f, 4f)] public float primaryVoxelCurrentSplatSpeedThreshold = 0.28f;
    [Tooltip("Speed at which the surface colour reaches white. Zero automatically uses Primary Voxel Max Grid Speed, giving a smooth blue-to-white grade across the usable flow range.")]
    [Range(0f, 16f)] public float primaryVoxelCurrentSplatWhiteSpeed = 0f;
    [Tooltip("Fraction of exposed wet voxel columns receiving a painted surface mark. Mark colour and brightness grade continuously from deep blue (slow) to bright turquoise (fast).")]
    [Range(0.05f, 1f)] public float primaryVoxelSurfaceSplatCoverage = 0.82f;
    [Tooltip("Sparse underwater selection fraction. It is capped independently from the broad surface coverage.")]
    [Range(0.001f, 0.05f)] public float primaryVoxelUnderwaterSplatCoverage = 0.006f;
    [Range(1, 8)] public int primaryVoxelCurrentSplatUpdateEveryNFrames = 1;
    [Range(0.05f, 1.5f)] public float primaryVoxelCurrentSplatRadius = 0.42f;
    [Range(0f, 1f)] public float primaryVoxelCurrentSplatOpacity = 0.30f;
    [Tooltip("Extra footprint overlap for the connected painted surface. Increase only slightly if visible gaps remain between neighbouring caps.")]
    [Range(1f, 1.6f)] public float primaryVoxelSurfaceSplatOverlap = 1.16f;
    [Tooltip("GPU-only horizontal smoothing time for a surface cap following its PBF support. It removes PBF/voxel frame jitter without delaying vertical contact with the water.")]
    [Range(0.02f, 0.6f)] public float primaryVoxelSurfaceSplatPositionSmoothing = 0.12f;
    [Tooltip("GPU-only flow smoothing time for surface colour, stretch and orientation.")]
    [Range(0.02f, 0.8f)] public float primaryVoxelSurfaceSplatVelocitySmoothing = 0.18f;
    [Tooltip("Flows below this speed keep their stable painted orientation instead of reacting to noisy near-zero directions.")]
    [Range(0.01f, 1f)] public float primaryVoxelSurfaceSplatCalmSpeed = 0.15f;
    [Tooltip("Maximum distance from the visual PBF skin at which an exposed-voxel mark is allowed. Marks wait for the skin instead of floating at an ahead-of-skin voxel level.")]
    [Range(0.05f, 1f)] public float primaryVoxelCurrentSurfacePbfMaxOffset = 0.35f;
    [Tooltip("Clearance of the painted Gaussian shell above the highest connected PBF support in its local surface slot. Cosmetic only; raise this if a PBF crest covers the shell.")]
    [Range(0.02f, 1f)] public float primaryVoxelCurrentSurfaceLift = 0.06f;
    [Tooltip("Physical visual height of each surface Gaussian cap above its PBF contact plane. This is render-only: it gives the painted surface a shallow 3D profile without changing the fluid solve.")]
    [Range(0.02f, 0.75f)] public float primaryVoxelCurrentSurfaceSplatHeight = 0.18f;
    public Color primaryVoxelCurrentSplatColor = new Color(0.05f, 0.72f, 0.72f, 1f);
    public Color primaryVoxelSurfaceSplatColor = new Color(0.68f, 0.95f, 0.98f, 1f);
    public Shader currentSplatShader;

    [Header("Primary Voxel Volume (New Hybrid Path)")]
    [Tooltip("Experimental clean split: a double-buffered 3D voxel field owns water mass and PBF is only a bounded exposed surface skin. Leave off to retain the legacy particle-to-bulk path.")]
    public bool usePrimaryVoxelVolume = true;
    [Tooltip("Vertical resolution of the authoritative main water volume. This is independent of the render-only microvoxel field.")]
    [Range(4, 32)] public int primaryVoxelLayerCount = 16;
    [Tooltip("World-space height of one main-volume voxel layer.")]
    [Min(0.05f)] public float primaryVoxelHeight = 0.5f;
    [Tooltip("Maximum number of PBF slots reserved for the exposed skin. The volume field stays cheap regardless of total water mass.")]
    [Range(256, 45000)] public int primarySurfaceParticleBudget = 12288;
    [Tooltip("How many PBF layers are seeded over each wet voxel column. This is a skin thickness, not a second mass store.")]
    [Range(1, 3)] public int primarySurfacePbfLayers = 3;
    [Tooltip("Maximum separately exposed water surfaces represented in one X/Z column. Scene 2 uses three slots with one PBF layer each, so flooded decks retain independent skins without increasing the particle budget.")]
    [Range(1, 3)] public int primarySurfaceSlotsPerColumn = 1;
    [Tooltip("Minimum open air gap, in primary voxel layers, required to split an unobstructed vertical water column into separate surfaces. Solid deck boundaries always remain separate.")]
    [Range(1, 4)] public int primarySurfaceMinimumVerticalGapLayers = 2;
    [Tooltip("Maximum ordinary crest height of the connected PBF skin above its own local voxel surface. Faster sparse crests can still detach into spray.")]
    [Range(0.05f, 1.5f)] public float primarySurfaceTopBandHeight = 0.5f;
    [Tooltip("Maximum fraction of a voxel's fill that may move over one voxel face per solver step.")]
    [Range(0.01f, 0.9f)] public float primaryVoxelFlowFraction = 0.28f;
    [Tooltip("Lateral equalisation speed for the authoritative voxel volume. Raise this to remove slow-motion bulk flow without increasing PBF cost.")]
    [Min(0f)] public float primaryVoxelLateralFlow = 5f;
    [Tooltip("Fraction of existing horizontal voxel momentum used as conservative face advection. This lets breach jets clear their aperture without forcing water into full cells.")]
    [Range(0f, 1f)] public float primaryVoxelMomentumAdvection = 0.6f;
    [Tooltip("Enable crest-based free-surface discharge across an open face above a local wall or step. This never passes mass through solid faces.")]
    public bool enablePrimaryVoxelWeirOverflow = true;
    [Tooltip("Coefficient for the donor/receiver-limited crest discharge. Keep this modest; ordinary open-water equalisation remains controlled by Lateral Flow.")]
    [Min(0f)] public float primaryVoxelWeirOverflowFlow = 2f;
    [Tooltip("Downward gravity transfer strength for the authoritative voxel volume.")]
    [Min(0f)] public float primaryVoxelGravityFlow = 12f;
    [Tooltip("Extra upward transfer from locally overfull voxels, keeping the field mass-conserving without a hard per-cell clamp.")]
    [Min(0f)] public float primaryVoxelCompressionFlow = 8f;

    [Header("Primary Voxel Hatch Riser Flow")]
    [Tooltip("Allows a conservative upward transfer only through the explicit trigger volumes generated for open stairwells/hatches. It does not make ordinary vertical voxel faces flow upward.")]
    public bool enablePrimaryVoxelHatchRiserFlow = true;
    [Tooltip("Optional explicit hatch triggers. Left empty, the volumes generated by ShipSectionBuilder are used automatically.")]
    public BoxCollider[] primaryVoxelHatchRiserVolumes;
    [Tooltip("Only used to prevent a downward fallback while the lower hatch voxel is this full; upward transfer starts from any non-empty open lower voxel.")]
    [Range(0.5f, 1f)] public float primaryVoxelHatchRiserMinimumSourceFill = 0.95f;
    [Tooltip("Effective discharge coefficient for each coarse-grid hatch face. The full 3.5 m stairwell has many faces, so this is intentionally much lower than the hull-breach coefficient.")]
    [Range(0.001f, 0.25f)] public float primaryVoxelHatchRiserDischargeCoefficient = 0.04f;

    [Header("Primary Voxel Hatch Probe (T Key)")]
    public bool enablePrimaryVoxelHatchProbe = true;
    public BoxCollider primaryVoxelHatchProbeVolume;
    [Range(.05f, 1f)] public float primaryVoxelHatchProbeFill = .5f;
    [Min(.1f)] public float primaryVoxelHatchProbeUpwardSpeed = 5f;
    [Range(.5f, 10f)] public float primaryVoxelHatchProbeLifetime = 3f;

    [Header("Primary Voxel Hatch Gizmos")]
    [Tooltip("Draws the effective external sea level plus a green/red Scene-view box for each configured hatch-riser trigger.")]
    public bool drawPrimaryVoxelHatchGizmos = true;
    [Tooltip("Sample density used only by the Scene-view hatch-clear check. Green means at least one vertical route through the trigger is not blocked by ShipHull colliders.")]
    [Range(1, 5)] public int primaryVoxelHatchGizmoSamples = 3;
    [Tooltip("Volume represented by one spawn-rate unit. Leave at zero to use the PBF particle mass / shallow rest density.")]
    [Min(0f)] public float primaryVoxelSpawnVolumePerUnit = 0f;
    [Tooltip("Horizontal radius for legacy/local inflow packets. External sea breaches instead use their physical aperture width and height to select overlap-weighted receiver cells.")]
    [Range(0, 2)] public int primaryVoxelInjectionFootprintRadius = 1;
    [Tooltip("Number of cheap authoritative-voxel flow passes per PBF step. This does not increase PBF work.")]
    [Range(1, 8)] public int primaryVoxelFlowSubsteps = 3;
    [Tooltip("Extra conservative lateral pressure equalisation passes after ordinary voxel flow. They use only genuinely open faces and never cross solid walls.")]
    [Range(0, 4)] public int primaryVoxelPressureIterations = 2;
    [Tooltip("Donor-volume fraction allowed by each under-relaxed pressure pass. Keep this small so constrained gaps level faster without a pressure blast.")]
    [Range(0.01f, 0.25f)] public float primaryVoxelPressureRelaxation = 0.06f;
    [Header("Primary Voxel Basin Levelling")]
    [Tooltip("Runs extra conservative hydrostatic passes through wet, open side faces. Use this for large rooms connected by doors so their free surfaces settle together after inflow stops.")]
    public bool enablePrimaryVoxelBasinLevelling = true;
    [Tooltip("Additional Jacobi pressure passes after the normal flow solve. One strong pass per simulation tick is normally enough; use more only for unusually long compartment chains.")]
    [Range(0, 4)] public int primaryVoxelBasinPressureIterations = 1;
    [Tooltip("Donor fraction for each basin pass. This is receiver-limited and remains conservative; it only controls how quickly a connected basin settles.")]
    [Range(0.01f, 0.25f)] public float primaryVoxelBasinPressureRelaxation = 0.25f;
    [Tooltip("Pressure-flow multiplier used only by the basin pass. This replaces repeated full-grid passes with one stronger, conservative doorway equalisation pass.")]
    [Range(1f, 12f)] public float primaryVoxelBasinFlowMultiplier = 8f;
    [Tooltip("Conservative post-flow passes that reconnect a one-voxel low/dry trough only when wetter open voxels bracket it on opposite sides. This does not expand an exposed water front or cross solids.")]
    [Range(0, 4)] public int primaryVoxelContinuityIterations = 2;
    [Tooltip("Maximum donor fraction used by each interior-gap repair pass. Mass is moved through the normal face-flux and receiver-capacity solve, never created.")]
    [Range(0.01f, 0.25f)] public float primaryVoxelContinuityRelaxation = 0.18f;
    [Tooltip("Small velocity projection from accepted voxel face flux. Kept low because the scalar volume solve is already pressure driven.")]
    [Range(0f, 0.25f)] public float primaryVoxelDivergenceCorrection = 0.08f;
    [Range(0f, 1f)] public float primaryVoxelParticlePICBlend = 0.15f;
    [Tooltip("Maximum temporary compression stored in one voxel before its pressure drives water to neighbours.")]
    [Range(1f, 4f)] public float primaryVoxelMaxFill = 1.5f;
    [Tooltip("Hard speed cap for velocity reconstructed from coarse voxel face flux. This does not cap the PBF particle's own wave velocity.")]
    [Min(0.1f)] public float primaryVoxelMaxGridSpeed = 4f;
    [Tooltip("Maximum grid-derived FLIP/PIC velocity change given to a surface particle in one PBF step.")]
    [Min(0.1f)] public float primaryVoxelMaxParticleDeltaVelocity = 1.5f;
    [Tooltip("Top-layer PBF slots may leave the connected surface by this height before a short spray grace period and recycle.")]
    [Min(0.1f)] public float primarySurfaceRecycleHeight = 1.5f;

    [Header("Primary Surface Waves")]
    [Tooltip("Adds conservative long surface waves over exposed primary-voxel surfaces. The voxel field remains the only owner of water volume.")]
    public bool enablePrimarySurfaceWaves = true;
    [Tooltip("Maximum number of CFL-limited GPU wave substeps performed during one primary voxel step.")]
    [Range(1, 8)] public int primarySurfaceWaveMaxSubsteps = 4;
    [Tooltip("CFL safety factor for the explicit surface-wave flux update.")]
    [Range(0.1f, 0.45f)] public float primarySurfaceWaveCfl = 0.32f;
    [Tooltip("Minimum depth used by the wave model. Shallower exposed cells stay wet but do not become numerically stiff.")]
    [Range(0.02f, 1f)] public float primarySurfaceWaveMinimumDepth = 0.12f;
    [Tooltip("Scales long-wave crest travel speed while retaining CFL-controlled, frame-rate-independent substeps. 1 is physical sqrt(gH); values above 1 make compartment surges travel faster without adding water.")]
    [Range(0.5f, 3f)] public float primarySurfaceWaveTravelSpeedScale = 1.75f;
    [Tooltip("Caps depth-averaged wave speed as |q| / H ≤ Froude × travel-speed-scaled √(gH). Applied after every momentum/forcing update before the face flux is used.")]
    [Range(0.25f, 1.5f)] public float primarySurfaceWaveMaxFroude = 1.25f;
    [Tooltip("Base per-second damping of wave discharge. The effective value below is applied exponentially per CFL substep, never once per rendered frame.")]
    [Range(0f, 8f)] public float primarySurfaceWaveDamping = 0.7f;
    [Tooltip("Long-wave damping multiplier. The default turns the legacy 0.7/s base into a 0.245/s effective damping so crests can cross a compartment before dying out.")]
    [Range(0.05f, 1f)] public float primarySurfaceWaveDampingMultiplier = 0.35f;
    [Tooltip("Effective per-second damping sent to the GPU after applying the multiplier.")]
    public float primarySurfaceWaveEffectiveDamping = 0.245f;
    [Tooltip("Limits only the visual surface displacement above or below the voxel-owned base surface.")]
    [Range(0.02f, 2f)] public float primarySurfaceWaveMaxDisplacement = 0.55f;
    [Tooltip("Display-only response for wave elevation. It smooths sampled wave η without changing conservative wave flux or voxel water volume.")]
    [Range(0.5f, 20f)] public float primarySurfaceWaveVisualResponse = 2.5f;
    [Tooltip("Display-only maximum vertical speed for wave elevation. It prevents one-frame full-height changes when an exposed voxel layer changes.")]
    [Range(0.1f, 6f)] public float primarySurfaceWaveVisualMaximumVerticalSpeed = 0.35f;
    [Tooltip("Display-only spatial smoothing for cell-scale wave ripple. It averages only across open wet voxel faces.")]
    [Range(0f, 1f)] public float primarySurfaceWaveVisualSpatialSmoothing = 0.75f;
    [Tooltip("Optional display-only ripple envelope. Disable to let each small ripple use the physical wave solver timing directly.")]
    [InspectorName("Use Artificial Ripple Timing")]
    public bool usePrimarySurfaceWaveArtificialRippleLifecycle = false;
    [Tooltip("Full rise–crest–fall time used only while Artificial Ripple Timing is enabled. It has no effect when disabled; physical waves then determine their own timing.")]
    [InspectorName("Artificial Ripple Cycle Time")]
    [Range(0.1f, 2f)] public float primarySurfaceWaveVisualRippleDuration = 0.6f;
    [Tooltip("Wave eta at or below this size uses the visual ripple lifecycle. Larger waves retain their direct motion.")]
    [Range(0.01f, 0.5f)] public float primarySurfaceWaveVisualRippleMaximumAmplitude = 0.12f;
    [Tooltip("Smallest sampled eta that starts a visual ripple event.")]
    [Range(0.001f, 0.05f)] public float primarySurfaceWaveVisualRippleActivationAmplitude = 0.008f;
    [Tooltip("Pressure-gradient coupling from the voxel field into the conservative wave momentum.")]
    [Range(0f, 4f)] public float primarySurfaceWavePressureForcing = 0.7f;
    [Tooltip("Coupling from actual voxel face flux into surface-wave momentum at energetic breaches and impacts.")]
    [Range(0f, 4f)] public float primarySurfaceWaveFluxForcing = 0.45f;
    [Tooltip("One-shot coupling from a depth-weighted voxel-column velocity change into exposed-surface wave momentum. It transfers breach/impact starts and stops, but steady bulk flow adds no continuing wave force.")]
    [Range(0f, 2f)] public float primarySurfaceWaveColumnVelocityForcing = 0.5f;
    [Tooltip("Coupling from explicit hatch links. Only changes in hatch transfer become equal-and-opposite surface pressure impulses on the linked decks.")]
    [Range(0f, 4f)] public float primarySurfaceWaveHatchForcing = 0.35f;    // Retained only as a zeroed compatibility input for the PBF skin. The former
    // visual flow-map advection and colour/dye controls have been removed.
    const bool enablePrimarySurfaceFlowMap = false;
    const int primarySurfaceFlowResolutionScale = 1;
    const int primarySurfaceFlowMaxSubsteps = 1;
    const float primarySurfaceFlowCfl = 0.35f;
    const float primarySurfaceFlowDamping = 0f;
    const float primarySurfaceFlowFoamDecay = 0f;
    const float primarySurfaceFlowResidualInjection = 0f;
    const float primarySurfaceFlowMaxResidualSpeed = 0f;
    const float primarySurfaceFlowPbfBlend = 0f;
    const bool primarySurfaceFlowVisualiseDye = false;    [Header("Primary Surface Wave Diagnostics")]
    [Tooltip("Area-weighted signed wave displacement across all exposed slots. It should remain close to zero in a closed basin; the visual cap never removes this physical displacement.")]
    public float primarySurfaceWaveSignedDisplacement = 0f;
    public int primarySurfaceWaveActiveCellCount = 0;
    public float primarySurfaceWaveMaxSpeed = 0f;
    [Tooltip("Mean physically connected voxel-water depth across active surface-wave cells. Values near the minimum depth while a room is full indicate a depth/topology issue.")]
    public float primarySurfaceWaveMeanDepth = 0f;
    [Tooltip("Deepest physically connected voxel-water column sampled by the wave solver.")]
    public float primarySurfaceWaveMaxDepth = 0f;
    [Tooltip("Estimated phase speed sqrt(g × mean H) multiplied by the configured wave travel-speed scale.")]
    public float primarySurfaceWaveEstimatedCelerity = 0f;
    public int primarySurfaceWaveTopologyResets = 0;
    [Tooltip("Number of wave cells whose display/PBF height is currently visually capped. This never changes eta in the solver.")]
    public int primarySurfaceWaveVisualClampHitCount = 0;
    [Tooltip("Total displaced volume hidden only by the visual height cap, in cubic metres.")]
    public float primarySurfaceWaveVisualClampExcessVolume = 0f;
    [Tooltip("Normal discharge that struck solid wave boundaries during the most recent wave solve.")]
    public float primarySurfaceWaveWallImpactImpulse = 0f;
    [Tooltip("Signed displacement per exposed surface slot. Each slot is deck-local; distinct decks at the same XZ coordinate stay separate.")]
    public Vector3 primarySurfaceWaveDeckSignedDisplacement = Vector3.zero;
    [Tooltip("Number of disconnected wet surface basins measured from the same GPU face-open flags used by the solver.")]
    public int primarySurfaceWaveBasinCount = 0;
    [Tooltip("Signed displacement of the largest connected wet basin, in cubic metres.")]
    public float primarySurfaceWaveLargestBasinSignedDisplacement = 0f;
    [Tooltip("Largest absolute signed displacement among connected wet basins, in cubic metres.")]
    public float primarySurfaceWaveLargestBasinAbsoluteDisplacement = 0f;
    [Header("Primary Surface Wave Topology Reprojection")]
    [Tooltip("When wet surface topology changes, removes only each connected basin's uniform η offset. The voxel base surface remains the owner of actual water volume.")]
    public bool enablePrimarySurfaceWaveBasinMeanReprojection = true;
    [Tooltip("Smallest basin signed displacement (m³) worth correcting after a topology change. This avoids uploads for floating-point noise.")]
    [Min(0f)] public float primarySurfaceWaveMeanReprojectionVolumeThreshold = 0.01f;
    public int primarySurfaceWaveMeanReprojectionCount = 0;
    public float primarySurfaceWaveLargestMeanReprojection = 0f;
    [Header("Primary Surface Wave Settle Test")]
    [Tooltip("Real-time duration sampled by the settle monitor after you stop the flood/breach. It is independent of rendering frame rate.")]
    [Min(0.5f)] public float primarySurfaceWaveSettlingTestDuration = 8f;
    public bool primarySurfaceWaveSettlingTestRunning = false;
    public float primarySurfaceWaveSettlingElapsed = 0f;
    public float primarySurfaceWaveSettlingStartSpeed = 0f;
    public float primarySurfaceWaveSettlingEndSpeed = 0f;
    [Header("Primary Surface Wave Console Diagnostics")]
    [Tooltip("Writes a compact [Wave Details] report using already-completed asynchronous GPU readbacks. It never forces a GPU readback or simulation sync.")]
    public bool logPrimarySurfaceWaveDetails = false;
    [Tooltip("Real-time interval between [Wave Details] reports. Keep this at one second or higher to avoid console overhead.")]
    [Min(0.5f)] public float primarySurfaceWaveDetailsLogInterval = 2f;
    public int primarySurfaceWaveLastSubstepCount = 0;
    public float primarySurfaceWaveLastStableDt = 0f;
    public float primarySurfaceWaveLastSubstepDt = 0f;

    [Header("Wave Flicker Diagnostic")]
    [Tooltip("Collects event records from every primary surface-wave X/Z/slot. Read-only: it never changes wave, voxel, or render state.")]
    public bool logPrimarySurfaceWaveFlickerDiagnostic = false;
    [Tooltip("Maximum suspicious all-wave events retained between asynchronous readbacks. Oldest events are retained first in each interval.")]
    [Range(256, 16384)] public int primarySurfaceWaveFlickerDiagnosticEventCapacity = 4096;
    [Range(0.1f, 2f)] public float primarySurfaceWaveFlickerDiagnosticReadbackInterval = 0.35f;
    [Tooltip("Minimum combined external forcing impulse that is recorded as a potential re-excitation event.")]
    [Min(0f)] public float primarySurfaceWaveFlickerForcingEventThreshold = 0.003f;
    [Range(8, 512)] public int primarySurfaceWaveFlickerMaxLogsPerReadback = 96;    // ------------------------------------------------------------------------
    // SECTION: Base fluid behaviour
    // These are the same global fluid parameters sent to the compute shader.
    // ------------------------------------------------------------------------
    // Internal PBF motion constants; no longer exposed as SPH tuning.
    [HideInInspector] public Vector3 gravity = new Vector3(0, -9.81f, 0);
    [HideInInspector] public float particleRadius = 0.5f;
    [HideInInspector] public float restDensity = 1000f;
    [HideInInspector] public float stiffness = 0.2f;
    [HideInInspector] public float viscosity = 0.2f;
    [HideInInspector] public float cohesion = 0.05f;
    [HideInInspector] public int gridResolution = 64;

    [Header("Merge / Split Behaviour")]
    [Tooltip("Extra bridge force that helps nearby water masses join before ordinary pressure/cohesion fully overlaps them.")]
    [Range(0f, 2f)] public float mergeSplitStrength = 0.28f;
    [Tooltip("How strongly nearly joined water masses inherit each other's velocity while merging.")]
    [Range(0f, 2f)] public float mergeVelocityMatching = 0.22f;
    [Tooltip("Where inside the smoothing radius the bridge force starts. Higher values only pull at the outer edge.")]
    [Range(0.1f, 0.95f)] public float mergeOuterStart = 0.58f;
    [Tooltip("Relative separating speed where thin necks release instead of staying glued together.")]
    [HideInInspector] public float splitReleaseSpeed = 0.9f;
    [Tooltip("How much low-density thin necks are allowed to tear apart.")]
    [Range(0f, 1f)] public float thinNeckRelease = 0.55f;

    [Header("Pressure Anti-Compression")]
    [Range(0f, 1f)] public float pressureCompressionBoost = 0.35f;
    [HideInInspector] public float pressureBoostStartRatio = 0.25f;
    [HideInInspector] public float pressureBoostExponent = 2f;
    [HideInInspector] public float pressureBoostMaxMultiplier = 3f;

    [Header("Vertical Stack Support")]
    [Tooltip("One-sided upward support from particles underneath. Helps calm vertically compressed settled stacks.")]
    [HideInInspector] public float verticalSupportStrength = 12f;
    [Tooltip("Horizontal column width, as a fraction of particle radius, used for vertical support.")]
    [Range(0.1f, 2f)] public float verticalSupportColumnRadiusScale = 0.65f;
    [Tooltip("Caps the upward velocity change from vertical support in one solver step. Set 0 to disable.")]
    [HideInInspector] public float maxVerticalSupportVelocityChangePerStep = 0.08f;

    [Header("Bulk Heightmap Phase 1")]
    [Tooltip("Keeps only the upper water band in SPH and treats deeper settled water as a cheap heightmap support volume.")]
    [HideInInspector] public bool useBulkHeightmap = false;
    [Tooltip("Lets settled particles below the active/transition buffer use the cheap dormant update while the bulk interface supplies support.")]
    [HideInInspector] public bool enableHybridDormantBuffer = true;
    [Tooltip("Independent coarse physics grid used by the conserved deep-water field. This is intentionally separate from the high-resolution render tiles.")]
    [Min(1)] public int bulkGridCountX = 64;
    [Tooltip("Independent coarse physics grid used by the conserved deep-water field. This is intentionally separate from the high-resolution render tiles.")]
    [Min(1)] public int bulkGridCountZ = 64;
    [Tooltip("Disables the older particle-column support while the bulk heightmap is providing support.")]
    [HideInInspector] public bool disableVerticalSupportWhenBulkHeightmap = true;
    [Tooltip("How many particle layers below the surface stay in the active SPH band.")]
    [Range(1, 8)] public int sphSurfaceLayers = 3;
    [Tooltip("World-space depth of the active PBF layer above the bulk heightmap. Deeper water is stored as bulk.")]
    [HideInInspector] public float pbfLayerDepthMeters = 1.25f;
    [Tooltip("Particles below this depth beneath the local surface are captured into bulk even while moving. The active and transition bands still impose a minimum capture depth.")]
    [HideInInspector] public float deepBulkCaptureDepthMeters = 1.5f;
    [Tooltip("When depth-based bulk is active, reserve at least this much lower water depth for the heightmap once the local column is deep enough. This makes the 3m PBF layer a maximum instead of blocking all bulk in shallower pools.")]
    [HideInInspector] public float minimumBulkStorageDepthMeters = 1f;
    [Tooltip("How fast the bulk/PBF split height may rise in metres per second. Prevents visual height spikes from pulling bulk upward.")]
    [HideInInspector] public float bulkSurfaceRiseSpeed = 0.1f;
    [Tooltip("Multiplier applied to the mass-consistent bulk token volume (unit particle mass divided by the solver rest density).")]
    [HideInInspector] public float bulkParticleVolumeScale = 1f;
    [Tooltip("Stores absorbed deep water in vertical voxel layers under the active PBF surface band.")]
    [HideInInspector] public bool useBulkVoxels = false;
    [Tooltip("Vertical voxel layers per surface tile for the deep-water reservoir.")]
    [Range(1, 32)] public int bulkVoxelLayerCount = 16;
    [Tooltip("Voxel layer height in metres for the full vertical diagnostic field.")]
    [HideInInspector] public float bulkVoxelHeight = 0.5f;
    [Tooltip("Approximate absorbed particle tokens that fill one voxel cell. Set 0 to derive from voxel volume.")]
    [HideInInspector] public int bulkVoxelTokensPerCell = 0;
    [Header("Fixed Three-Zone Bulk Test")]
    [Tooltip("Uses a deterministic level-wide split: PBF above the transition, immediate stored bulk below it. This bypasses the slow dormant and legacy bulk-admission gates.")]
    [HideInInspector] public bool useFixedBulkBands = true;
    [Tooltip("Reference top of the PBF transition band in world Y. This is a classification level only; it never creates water or volume by itself.")]
    [HideInInspector] public float fixedBulkSurfaceY = 2.5f;
    [Tooltip("Reserved visual/support blend depth at the PBF-to-bulk handoff. It is not an additional simulated PBF layer.")]
    [Min(0.05f)] public float fixedBulkTransitionHeight = 1f;
    [Tooltip("Optional explicit PBF-skin depth. Leave at 0 to retain exactly sphSurfaceLayers particle layers (normally 1-3) above the bulk transition.")]
    [Min(0f)] public float fixedActivePbfDepthMeters = 0f;
    [Tooltip("Minimum time newly emitted water remains active PBF before the fixed-band classifier may store it as bulk. This lets the breach/inflow spread laterally before entering the static bulk field.")]
    [Min(0f)] public float fixedBulkMinimumActiveAge = 2f;
    [Tooltip("Distributes a newly stored fixed-band bulk token across nearby lower/equal support cells. This runs only on entry, not as a full-grid bulk update.")]
    [HideInInspector] public bool spreadNewFixedBulkTokens = true;
    [Tooltip("Maximum coarse-cell hops a newly stored token may take while finding nearby available bulk volume. Kept small so the inflow spreads without a GPU-wide transport dispatch.")]
    [Range(1, 4)] public int fixedBulkFeedSpreadHops = 3;
    [Header("Continuous Bulk Flow")]
    [Tooltip("Uses a double-buffered, mass-conserving coarse volume field. Flux is computed from the completed old grid and becomes visible only after the next grid is finalized.")]
    [HideInInspector] public bool enableContinuousBulkFlow = true;
    [Tooltip("Maximum bulk support-height travel per second. This caps outgoing volume independently of the pressure/head difference.")]
    [Min(0f)] public float bulkFlowSpeed = 0.8f;
    [Tooltip("Maximum fraction of one cell's stored volume allowed to leave in a simulation step.")]
    [Range(0.005f, 0.25f)] public float bulkFlowMaxFractionPerStep = 0.06f;
    [Tooltip("Minimum support-height difference needed before volume flows to a neighbour.")]
    [Min(0f)] public float bulkFlowMinimumHead = 0.01f;
    [Header("Bulk Activity Wake")]
    [Tooltip("Deposits active-particle motion into coarse cells and wakes a small amount of stored bulk when motion reaches it.")]
    [HideInInspector] public bool enableBulkActivityWake = true;
    [Tooltip("Activity level in metres/second needed to wake stored bulk in a cell.")]
    [Min(0f)] public float bulkWakeActivityThreshold = 1.5f;
    [Tooltip("Exponential decay rate for the event-driven bulk activity field.")]
    [Min(0f)] public float bulkActivityDecayPerSecond = 2.5f;
    [Tooltip("How far below the local support surface a wake may rehydrate a stored token.")]
    [Min(0.1f)] public float bulkWakeDepth = 1f;
    [Tooltip("Maximum stored particles woken per simulation step.")]
    [Min(0)] public int maxBulkWakesPerStep = 32;
    [Tooltip("Maximum stored particles woken from one coarse cell per step.")]
    [Min(0)] public int maxBulkWakesPerCell = 1;
    [Tooltip("Vertical probes used when converting scene colliders into symmetric coarse-cell face openings. More samples resolve door sills more accurately; this pass runs only when topology changes.")]
    [Range(2, 16)] public int bulkTopologyVerticalSamples = 8;
    [Tooltip("Shows the actual stored bulk-token voxels below the PBF transition. This is independent of the unfinished microvolume and isosurface paths.")]
    [HideInInspector] public bool showFixedBulkVoxelField = true;
    [Header("Microvoxel Underwater Volume")]
    [Tooltip("Build the higher-resolution display-only microvoxel field.")]
    public bool useMicroVoxelVolume = true;
    [Tooltip("Microvoxel subdivisions per coarse bulk cell edge. 2 gives eight subcells per coarse cell.")]
    [Range(1, 3)] public int microVoxelScale = 2;
    [Tooltip("Temporal response of the smoothed microvoxel display field.")]
    [Range(0.02f, 1f)] public float microVoxelTemporalResponse = 0.22f;
    [Range(16, 128)] public int microVoxelRaymarchSteps = 64;
    public bool enableBulkSupportForce = false;
    [Tooltip("Only calm, settled particles are allowed to enter the bulk heightmap.")]
    public bool requireSettledBeforeBulk = true;
    [Tooltip("Allows calm below-band particles to be absorbed into the bulk grid.")]
    public bool enableBulkAbsorption = false;
    [Tooltip("Particles must stay below this speed before they can enter bulk.")]
    public float bulkEnterSpeedThreshold = 0.5f;
    [Tooltip("Particles must move less than this per simulation step before they can enter bulk.")]
    public float bulkEnterPositionChangeThreshold = 0.025f;
    [Tooltip("How long a below-band particle must remain settled before becoming bulk.")]
    public float bulkEnterDelay = 1.5f;
    [Tooltip("Stops creating more bulk once the last readback sees this fraction of live particles in bulk.")]
    [Range(0f, 0.9f)] public float maxBulkParticleFraction = 0.35f;
    [Tooltip("Caps how many particles can be absorbed into bulk in one simulation step. Set 0 for no cap.")]
    public int maxBulkAbsorbsPerStep = 4;
    [Tooltip("Caps absorption in each coarse bulk cell per simulation step. One is the safest setting for a particle-diameter cell.")]
    [Range(1, 4)] public int maxBulkAbsorbsPerCellPerStep = 1;
    [Tooltip("Uses depth as the primary bulk rule for PBF: particles below the PBF layer become heightmap bulk.")]
    public bool useDepthBasedBulkAbsorption = false;
    [Tooltip("Prevents bulk absorption from thinning out tiles that no longer have enough active SPH coverage.")]
    public bool useLocalBulkSafety = true;
    [Tooltip("Minimum active particles that must remain in a tile before it can absorb more particles into bulk.")]
    public int minBulkActiveTileParticles = 10;
    [Tooltip("Minimum active particles inside the visible SPH band for a tile to keep absorbing into bulk.")]
    public int minBulkActiveBandParticles = 6;
    [Tooltip("Minimum active particles near the bottom of the SPH band so the bulk has a pressure-bearing boundary.")]
    public int minBulkLowerBandParticles = 2;
    [Tooltip("Per-tile bulk cap. This lets deep well-covered regions go bulk while thin exposed regions stay active.")]
    [Range(0.1f, 0.9f)] public float maxLocalBulkParticleFraction = 0.85f;
    [Tooltip("Minimum active SPH layers kept above the heightmap when local bulk safety is enabled.")]
    [Range(1, 8)] public int minBulkSafetySurfaceLayers = 3;
    [Tooltip("Extra fully simulated layers between the requested PBF band and the coarse bulk conversion front.")]
    [Range(1, 4)] public int bulkTransitionLayers = 2;
    [Tooltip("Extra vertical hysteresis at the bulk conversion front, in particle radii.")]
    [Range(0f, 2f)] public float bulkTransitionHysteresisRadiusScale = 0.5f;
    [Tooltip("Allows exposed bulk grid cells to rehydrate stored particles back into SPH.")]
    public bool enableBulkRespawnFromExposedCells = true;
    [Tooltip("Caps how many bulk particles can rehydrate in one simulation step. Set 0 for no cap.")]
    public int maxBulkRespawnsPerStep = 32;
    [Tooltip("Caps rehydration per surface tile in one simulation step. Set 0 for no per-cell cap.")]
    public int maxBulkRespawnsPerCellPerStep = 1;
    [Tooltip("A bulk cell is exposed when it has this many or fewer active particles in its tile.")]
    public int bulkRespawnActiveCountThreshold = 0;
    [Tooltip("A cell needs at least this many stored bulk tokens before exposed-cell respawn can use it.")]
    public int bulkRespawnMinimumStoredParticles = 4;
    [Tooltip("Respawn only when the local bulk cell would create a visible surface below the main waterline.")]
    public bool bulkRespawnOnlyCreatesSurface = true;
    [Tooltip("Local cell surface must be this far below the main waterline before bulk respawn is allowed.")]
    public float bulkRespawnSurfaceDrop = 0.5f;
    [Tooltip("Upward support applied by the bulk heightmap to particles in the bottom of the SPH band.")]
    public float bulkSupportStrength = 18f;
    [Tooltip("Support band thickness as a multiple of one particle diameter.")]
    [Range(0.25f, 4f)] public float bulkSupportBandDiameterScale = 1f;
    [Tooltip("Caps the upward velocity change from bulk support in one solver step. Set 0 to disable.")]
    public float maxBulkSupportVelocityChangePerStep = 0.08f;
    [Tooltip("Requires below-band particles to remain calm, density-stable, and away from compressed neighbours before entering bulk.")]
    public bool useBulkStabilityWindow = true;
    [Tooltip("How long a below-band particle must satisfy all bulk stability tests before entering bulk.")]
    public float bulkStableDuration = 1.5f;
    [Tooltip("Bulk density tolerance. PBF uses this as an over-compression guard; WCSPH uses it as a near-rest density window.")]
    [Range(0.01f, 0.5f)] public float bulkStableDensityTolerance = 0.3f;
    [Tooltip("Uses bulkEnterSpeedThreshold instead of sleepSpeedThreshold for stable bulk entry. Leave off for the strictest rule.")]
    public bool useBulkSpeedForStableWindow = true;
    [Tooltip("Blocks bulk absorption if a compressed active neighbour was seen within this many smoothing radii last frame.")]
    [Range(0.5f, 2f)] public float bulkCompressedNeighbourRadiusScale = 1f;
    [Tooltip("When enabled, a particle cannot enter bulk while nearby active particles were compressed last frame.")]
    public bool requireNoCompressedNeighboursForBulk = false;

    [Header("Adaptive SPH Stability")]
    public bool useAdaptiveForces = true;
    [Tooltip("Speed that counts as fast for local pressure, viscosity, and cohesion adaptation.")]
    public float adaptiveSpeedReference = 5f;
    [Tooltip("Extra pressure only when a particle is both fast and compressed.")]
    [Range(0f, 4f)] public float pressureSpeedCompressionBoost = 1.2f;
    [Tooltip("Extra viscosity when neighbouring particles have very different velocities.")]
    [Range(0f, 5f)] public float viscosityRelativeVelocityBoost = 1.5f;
    [Tooltip("Extra damping when particles are locally compressed.")]
    [Range(0f, 5f)] public float viscosityCompressionBoost = 1f;
    [Tooltip("Reduces cohesion in fast flow so jets and impacts do not glue together.")]
    [Range(0f, 1f)] public float cohesionSpeedDrop = 0.85f;
    [Tooltip("Caps the velocity change caused by SPH forces in one solver step. Set 0 to disable.")]
    public float maxForceVelocityChangePerStep = 0.45f;

    [Header("Solver Mode")]
    public FluidSolverMode solverMode = FluidSolverMode.WCSPH;
    [Tooltip("Position constraint iterations for the PBF/XPBD solver. Higher means less compression and more cost.")]
    [Range(1, 8)] public int pbfIterations = 4;
    [Tooltip("Scales shallowRestDensity for PBF constraints. The project density kernel normally lives around this range.")]
    public float pbfRestDensityScale = 1f;
    [Tooltip("XPBD compliance. 0 is stiffest; slightly higher values soften the constraint.")]
    public float pbfCompliance = 0.00001f;
    [Tooltip("How strongly each PBF iteration applies its position correction.")]
    [Range(0f, 1f)] public float pbfCorrectionStrength = 0.85f;
    [Tooltip("Caps each particle's correction distance per PBF iteration. Set 0 to disable.")]
    public float pbfMaxCorrectionPerIteration = 0.06f;
    [Tooltip("Small artificial pressure term that helps keep PBF particles from clumping.")]
    [Range(0f, 0.2f)] public float pbfArtificialPressure = 0.02f;
    [Tooltip("Artificial pressure sample radius as a fraction of smoothing radius.")]
    [Range(0.05f, 0.8f)] public float pbfArtificialPressureRadius = 0.3f;
    [Tooltip("Velocity damping applied after PBF projection.")]
    [Range(0f, 0.2f)] public float pbfVelocityDamping = 0.01f;
    [Tooltip("XSPH-style velocity smoothing after PBF projection.")]
    [Range(0f, 1f)] public float pbfViscosity = 0.08f;
    [Tooltip("Caps only the velocity reconstructed from PBF position projection. Position constraints may still resolve penetration without launching particles.")]
    public float pbfMaxProjectionVelocity = 3f;
    [Tooltip("Mass weight of the analytic fixed ghost layer at the PBF/bulk interface.")]
    [Range(0f, 2f)] public float pbfBulkGhostWeight = 0.45f;

    [Header("Depth-Aware Rest Density")]
    public float shallowRestDensity = 40f;
    [Min(1)] public int fullRestDensityDepthLayers = 3;

    [Header("Collision Friction")]
    [Range(0f, 1f)] public float boundsBounceDamping = 0.15f;
    public float boundsImpactSpeedLoss = 1.0f;
    public float wallContactSpeedLossPerSecond = 3.0f;
    public float shipHullSurfaceFrictionPerSecond = 4.0f;
    public float shipHullNormalBounceLossPerSecond = 20.0f;
    [Range(1, 8)] public int collisionSubsteps = 2;

    [Header("Solid Scene Colliders")]
    public bool useTaggedShipHullColliders = true;
    public string shipHullColliderTag = "ShipHull";
    public Collider[] explicitSolidColliders;
    public float solidColliderSkin = 0.005f;
    public bool autoRebuildSolidCollidersEachFrame = false;
    public int solidColliderCount = 0;

    // ------------------------------------------------------------------------
    // SECTION: Active vs dormant particle behaviour
    // The long-term goal is to run full SPH-like work only on the particles
    // that are moving / near the surface, while deep calm particles use a
    // cheaper update path.
    // ------------------------------------------------------------------------
    // Retained PBF-skin compatibility state; no longer exposed in the Inspector.
    [Tooltip("Keeps deep water as retained dormant particles instead of converting it into bulk tokens. Only the shallow exposed skin runs full PBF.")]
    [HideInInspector] public bool useDormantParticleReservoir = true;
    [Tooltip("Number of particle-diameter layers around an exposed water/air interface that remain fully PBF simulated.")]
    [Range(1, 6)] public int dormantActiveSurfaceLayers = 3;
    [Tooltip("Time window used for the dormant-entry speed average. This prevents one quiet frame from sleeping a moving particle.")]
    [Range(0.05f, 3f)] public float dormantSpeedAverageTime = 0.45f;
    [Tooltip("Static density weight supplied by a dormant neighbour to the active PBF skin.")]
    [Range(0f, 1.5f)] public float dormantPbfSupportWeight = 0.8f;
    [Tooltip("Probe distance, in particle diameters, used to wake dormant water beside a new air opening.")]
    [Range(0.5f, 3f)] public float dormantExposureProbeDiameterScale = 1.25f;
    [Tooltip("Wake dormant particles when a non-solid neighbouring direction becomes empty, allowing a lower doorway or breach to make a new local surface.")]
    [HideInInspector] public bool wakeDormantAtExposedFaces = true;
    [Tooltip("Checks one interleaved share of dormant particles per simulation step for newly exposed faces. Four gives a local wake delay of only a few solver steps without scanning the whole reservoir at once.")]
    [Range(1, 8)] public int dormantExposureProbeStride = 4;
    [HideInInspector] public float wakeSpeedThreshold = 0.5f;
    [HideInInspector] public float sleepSpeedThreshold = 0.1f;
    [Tooltip("Below-surface particles at or below this speed are treated as visually settled enough to sleep.")]
    [HideInInspector] public float dormantSettledSpeedThreshold = 0.35f;
    [HideInInspector] public float sleepDelay = 0.5f;
    [HideInInspector] public float activeHeightThreshold = 1.0f;
    [Tooltip("Particles below the local water surface by at least this distance can become dormant when slow.")]
    [HideInInspector] public float dormantSurfaceClearance = 0.2f;
    [Tooltip("Below-surface particles moving less than this distance per simulation step count as settled.")]
    [HideInInspector] public float dormantPositionChangeThreshold = 0.015f;
    [Tooltip("Dormant particles only wake from active neighbours inside this radius, and only when those neighbours are moving.")]
    [HideInInspector] public float dormantNeighborWakeRadius = 0.5f;
    [Tooltip("Active neighbours must move at least this fast before they wake nearby dormant particles.")]
    [HideInInspector] public float dormantNeighborWakeSpeedThreshold = 0.45f;
    [HideInInspector] public float inflowWakeRadius = 2f;
    [Range(0f, 1f)] public float dormantDamping = 0.95f;

    [Header("Debug Readback")]
    public bool enableStateReadback = true;
    public float stateReadbackInterval = 0.5f;

    // ------------------------------------------------------------------------
    // SECTION: Compute shader asset
    // This shader owns the particle simulation kernels.
    // ------------------------------------------------------------------------
    [Header("Compute")]
    public ComputeShader fluidCS;

    // ------------------------------------------------------------------------
    // SECTION: GPU buffers
    // particles      = all particle state
    // cellHeads      = spatial hash linked-list heads
    // nextIndex      = linked-list "next" pointer for each particle
    // ------------------------------------------------------------------------
    ComputeBuffer particleBuffer;
    ComputeBuffer previousPositionBuffer;
    ComputeBuffer particleSurfaceAgeBuffer;
    ComputeBuffer particleDormantSpeedAverageBuffer;
    ComputeBuffer cellHeadsBuffer;
    ComputeBuffer nextIndexBuffer;
    ComputeBuffer debugForceBuffer;
    ComputeBuffer pbfPreviousPositionBuffer;
    ComputeBuffer pbfPredictedPositionBuffer;
    ComputeBuffer pbfPredictedVelocityBuffer;
    ComputeBuffer pbfLambdaBuffer;
    ComputeBuffer pbfPositionDeltaBuffer;
    ComputeBuffer solidColliderBuffer;
    ComputeBuffer fallbackSurfaceTileBuffer;
    ComputeBuffer bulkWaterBuffer;
    ComputeBuffer bulkWaterNextBuffer;
    ComputeBuffer bulkFluxBuffer;
    ComputeBuffer bulkReceiveScaleBuffer;
    ComputeBuffer bulkFaceSillBuffer;
    ComputeBuffer bulkTokenDeltaBuffer;
    ComputeBuffer bulkTokenFluxResidualBuffer;
    ComputeBuffer bulkTokenMoveQuotaBuffer;
    ComputeBuffer bulkActivityBuffer;
    ComputeBuffer bulkWakeCounterBuffer;
    ComputeBuffer bulkVoxelBuffer;
    ComputeBuffer bulkPhase2CounterBuffer;
    ComputeBuffer bulkVoxelCounterBuffer;
    ComputeBuffer microVoxelRawBuffer;
    ComputeBuffer microVoxelDisplayBufferA;
    ComputeBuffer microVoxelDisplayBufferB;
    ComputeBuffer primaryVoxelBuffer;
    ComputeBuffer primaryVoxelNextBuffer;
    ComputeBuffer primaryVoxelPreviousFrameBuffer;
    ComputeBuffer primaryVoxelFluxBuffer;
    ComputeBuffer primaryVoxelReceiveScaleBuffer;
    ComputeBuffer primaryVoxelReservoirBoundaryBuffer;
    ComputeBuffer primaryVoxelSolidBuffer;
    ComputeBuffer primaryVoxelFaceOpenBuffer;
    ComputeBuffer primaryVoxelInflowBuffer;
    ComputeBuffer primaryVoxelDeferredInflowBuffer;
    ComputeBuffer primaryVoxelDiagnosticsBuffer;
    ComputeBuffer primaryVoxelHatchProbeBuffer;
    ComputeBuffer primaryVoxelHatchDebugBuffer;
    ComputeBuffer primarySurfaceWaveBuffer;
    ComputeBuffer primarySurfaceWaveNextBuffer;
    ComputeBuffer primarySurfaceWaveFaceFluxBuffer;
    ComputeBuffer primarySurfaceWaveFaceFluxNextBuffer;
    ComputeBuffer primarySurfaceWaveDiagnosticsBuffer;
    ComputeBuffer primarySurfaceWaveMeanCorrectionBuffer;
    ComputeBuffer primarySurfaceWaveFlickerEventBuffer;
    ComputeBuffer primarySurfaceWaveFlickerBuildStateBuffer;
    ComputeBuffer primarySurfaceWaveFlickerForcingStateBuffer;
    ComputeBuffer primarySurfaceWaveFlickerEventCountBuffer;
    ComputeBuffer primarySurfaceFlowBuffer;
    ComputeBuffer primarySurfaceFlowNextBuffer;
    bool primaryVoxelHatchFaceDebugReadbackPending;
    float primaryVoxelHatchFaceDebugTimer;
    float primaryVoxelHatchFaceDebugLastLogTime = Mathf.NegativeInfinity;
    bool primaryVoxelHatchFaceDebugProbeWarningLogged;

    const float PrimaryVoxelHatchFaceDebugQueryIntervalSeconds = 0.25f;
    const float PrimaryVoxelHatchFaceDebugLogIntervalSeconds = 1f;

    const int MaxPrimaryVoxelHatchRiserVolumes = 4;
    readonly Vector4[] primaryVoxelHatchRiserBounds =
        new Vector4[MaxPrimaryVoxelHatchRiserVolumes * 2];
    ComputeBuffer boundaryParticleBuffer;
    ComputeBuffer boundaryCellHeadsBuffer;
    ComputeBuffer boundaryNextIndexBuffer;
    ComputeBuffer sprayParticleBuffer;
    ComputeBuffer primaryVoxelCurrentSplatBuffer;
    ComputeBuffer primaryVoxelCurrentSplatArgsBuffer;
    ComputeBuffer primaryVoxelUnderwaterSplatStateBuffer;
    ComputeBuffer primaryVoxelSurfaceGaussianBuffer;
    ComputeBuffer primaryVoxelSurfaceGaussianArgsBuffer;
    ComputeBuffer primaryVoxelSurfaceGaussianHistoryBuffer;
    ComputeBuffer primaryVoxelSurfaceGaussianDiagnosticsBuffer;
    bool primaryVoxelSurfaceGaussianDiagnosticsReadbackPending;
    float primaryVoxelSurfaceGaussianDiagnosticsTimer;
    Material generatedCurrentSplatMaterial;
    Material generatedUnderwaterSplatMaterial;
    Mesh generatedCurrentSplatMesh;
    Mesh generatedSurfaceGaussianMesh;

    // ------------------------------------------------------------------------
    // SECTION: Internal cached state
    // spawnAccumulator stores fractional spawn timing so spawnRate can be a
    // smooth "particles per second" value.
    // cellSize / totalCells are derived from the grid setup.
    // ------------------------------------------------------------------------
    float spawnAccumulator;
    float simulationAccumulator;
    float stateReadbackTimer;
    float forceLogTimer;
    float voxelReadbackTimer;
    float primaryVoxelDraftReadbackTimer;
    float primaryVoxelDraftVelocity;
    bool primaryVoxelDraftReadbackInFlight;
    bool primaryVoxelShipRootInitialised;
    bool primaryVoxelShipDraftHasApplied;
    int primaryVoxelShipRootInstanceId;
    float primaryVoxelLastColliderRefreshY;
    float bulkDiagnosticsReadbackTimer;
    float bulkParticleVolumeForDiagnostics;
    // Must remain below the uint overflow limit for the complete voxel field.
    // 1e4 retains 0.0001 volume precision and supports >400,000 volume units.
    const float PrimaryVoxelDiagnosticsScale = 10000f;
    readonly uint[] primaryVoxelDiagnosticsReadback = new uint[PrimaryVoxelDiagnosticsCount];
    PrimaryVoxelCellGPU[] primaryVoxelDiagnosticCells;
    PrimaryVoxelFaceFluxGPU[] primaryVoxelDiagnosticFlux;
    float[] primaryVoxelDiagnosticReceiveScales;
    uint[] primaryVoxelDiagnosticSolids;
    SolidColliderDataGPU[] primaryVoxelDiagnosticColliders;
    bool primaryVoxelDiagnosticsReadbackInFlight;
    bool primaryVoxelFlowReadbackInFlight;
    int primaryVoxelFlowReadbackPending;
    bool primaryVoxelFlowReadbackFailed;
    int primaryVoxelFlowReadbackColliderCount;
    float primaryVoxelMainDetailsLastLogTime = float.NegativeInfinity;
    readonly Vector4[] primaryVoxelDeferredInflowReadback = new Vector4[1];
    bool microDisplayPing;
    float shaderDeltaTime;
    int simulationStepCounter;
    bool continuousBulkFieldInitialized;
    bool bulkTopologyDirty = true;
    bool bulkTopologySignatureValid;
    int bulkWaterGridCountX;
    int bulkWaterGridCountZ;
    Vector3 lastBulkTopologyBoundsMin;
    Vector3 lastBulkTopologyBoundsMax;
    float lastBulkTopologyCutoffY;
    float lastBulkTopologyParticleRadius;
    int lastBulkTopologyVerticalSamples;
    int lastBulkTopologyGridCountX;
    int lastBulkTopologyGridCountZ;
    float cellSize;
    int totalCells;
    BulkWaterCellGPU[] bulkDiagnosticsCells;
    Vector4[] bulkDiagnosticsFlux;
    float[] bulkDiagnosticsReceiveScales;
    uint[] bulkDiagnosticsActivity;

    // ------------------------------------------------------------------------
    // SECTION: Kernel IDs
    // Cached once at startup so Update() only dispatches work and does not
    // repeatedly search for kernels by name.
    // ------------------------------------------------------------------------
    int kernelClassifyParticles;
    int kernelBuildBulkGrid;
    int kernelFinalizeBulkGrid;
    int kernelCountBulkGrid;
    int kernelClearBulkGridCounts;
    int kernelClearBulkTokenDeltas;
    int kernelSeedBulkVolumeFromTokenCounts;
    int kernelApplyBulkTokenDelta;
    int kernelBuildBulkTopology;
    int kernelComputeBulkFlux;
    int kernelComputeBulkReceiveScale;
    int kernelApplyBulkFlux;
    int kernelAccumulateBulkTokenFlux;
    int kernelClearBulkTokenReconciliation;
    int kernelReconcileBulkTokens;
    int kernelSpawnFromExposedBulk;
    int kernelDecayBulkActivity;
    int kernelAccumulateBulkActivity;
    int kernelWakeBulkActivity;
    int kernelClearHash;
    int kernelBuildHash;
    int kernelWakeDormantFromActive;
    int kernelDensityActive;
    int kernelForcesActive;
    int kernelIntegrateActive;
    int kernelPBFPredictActive;
    int kernelPBFComputeLambda;
    int kernelPBFComputeDelta;
    int kernelPBFApplyDelta;
    int kernelPBFUpdateVelocity;
    int kernelUpdateDormant;
    int kernelClearVoxelField;
    int kernelScatterParticleVoxels;
    int kernelFinalizeVoxelField;
    int kernelClearMicroVoxelField;
    int kernelScatterMicroVoxelParticles;
    int kernelFinalizeMicroVoxelField;
    int kernelSmoothMicroVoxelField;
    int kernelClearPrimaryVoxelVolume;
    int kernelClearPrimaryVoxelReservoirBoundary;
    int kernelInjectPrimaryVoxelVolume;
    int kernelDebugPrimaryVoxelHatchProbe;
    int kernelQueryPrimaryVoxelHatchDebug;
    int kernelBuildPrimaryVoxelSolids;
    int kernelCopyPrimaryVoxelFrameState;
    int kernelComputePrimaryVoxelFlux;
    int kernelComputePrimaryVoxelReceiveScale;
    int kernelApplyPrimaryVoxelFlux;
    int kernelProjectPrimaryVoxelColumns;
    int kernelSeedPrimaryVoxelSurface;
    int kernelConstrainPrimaryVoxelSurface;
    int kernelCopyPrimaryVoxelDisplay;
    int kernelClearPrimaryVoxelDiagnostics;
    int kernelCollectPrimaryVoxelDiagnostics;
    int kernelClearSpray;
    int kernelSpawnSpray;
    int kernelUpdateSpray;
    int kernelBuildPrimaryVoxelCurrentSplats;
    int kernelUpdatePrimaryVoxelUnderwaterSplats;
    int kernelBuildPrimaryVoxelSurfaceGaussians;
    int kernelClearPrimaryVoxelSurfaceGaussianDiagnostics;
    int kernelBuildPrimarySurfaceWaveState;
    int kernelComputePrimarySurfaceWaveFlux;
    int kernelUpdatePrimarySurfaceWave;
    int kernelCollectPrimarySurfaceWaveFlickerDiagnostic;
    int kernelBuildPrimarySurfaceFlowState;
    int kernelAdvectPrimarySurfaceFlow;

    const int ParticleStride = sizeof(float) * 12;
    const int PreviousPositionStride = sizeof(float) * 3;
    const int SurfaceAgeStride = sizeof(float);
    const int DormantSpeedAverageStride = sizeof(float);
    const int ForceDebugStride = sizeof(float) * 4;
    const int PBFDeltaStride = sizeof(float) * 4;
    const int SolidColliderStride = sizeof(float) * 20;
    const int SurfaceTileStride = sizeof(float) * 8;
    const int BulkWaterCellStride = sizeof(float) * 4 + sizeof(int) * 7;
    const int BulkFluxStride = sizeof(float) * 4;
    const int BulkVoxelCellStride = sizeof(float) * 3 + sizeof(int) * 2;
    const int MicroVoxelCellStride = sizeof(float) * 5 + sizeof(int) * 3;
    const int PrimaryVoxelCellStride = sizeof(float) * 8;
    const int PrimaryVoxelFluxStride = sizeof(float) * 8;
    const int PrimaryVoxelReservoirBoundaryStride = sizeof(float) * 4;
    const int PrimaryVoxelInflowStride = sizeof(float) * 12;
    const int PrimaryVoxelDeferredInflowStride = sizeof(float) * 4;
    const int PrimaryVoxelDiagnosticsStride = sizeof(uint);
    const int PrimaryVoxelDiagnosticsCount = 21;
    const float PrimaryVoxelRuntimeDiagnosticsScale = 1000f;
    const int PrimaryVoxelHatchProbeStride = sizeof(float) * 8;
    const int PrimaryVoxelHatchDebugStride = sizeof(float) * 16;
    const int PrimarySurfaceWaveCellStride = sizeof(float) * 8;
    const int PrimarySurfaceWaveFaceFluxStride = sizeof(float) * 8;
    const int PrimarySurfaceWaveDiagnosticsCount = 10;
    const int PrimarySurfaceWaveMeanCorrectionStride = sizeof(float) * 2;
    const int PrimarySurfaceWaveFlickerEventVectorCount = 10;
    const int PrimarySurfaceWaveFlickerEventStride = sizeof(float) * 4 * PrimarySurfaceWaveFlickerEventVectorCount;
    const int PrimarySurfaceWaveFlickerBuildStateVectorCount = 5;
    const int PrimarySurfaceFlowCellStride = sizeof(float) * 8;
    const int BulkVoxelCounterStride = sizeof(int);
    const int BoundaryParticleStride = sizeof(float) * 8;
    const int SprayParticleStride = sizeof(float) * 8;
    const int CurrentSplatStride = sizeof(float) * 8;
    const int UnderwaterSplatStateStride = sizeof(float) * 8;
    const int SurfaceGaussianHistoryStride = sizeof(float) * 8;
    const int PrimaryVoxelSurfaceGaussianDiagnosticsCount = 10;
    const float PrimaryVoxelSurfaceGaussianDiagnosticsScale = 1000f;
    int bulkWaterCellCount;
    int bulkVoxelCellCount;
    int microVoxelCellCount;
    int primaryVoxelCellCount;
    int primaryVoxelBufferLayerCount;
    int primarySurfaceWaveEntryCount;
    int primarySurfaceFlowEntryCount;
    bool primarySurfaceWaveDiagnosticsReadbackPending;
    bool primarySurfaceWaveBasinReadbackPending;
    bool primarySurfaceWaveMeanReprojectionRequested;
    bool primarySurfaceWaveMeanReprojectionPending;
    float primarySurfaceWaveDiagnosticsTimer;
    float primarySurfaceWaveBasinDiagnosticsTimer;
    float primarySurfaceWaveDetailsLastLogTime = Mathf.NegativeInfinity;
    bool primarySurfaceWaveFlickerReadbackPending;
    float primarySurfaceWaveFlickerReadbackTimer;
    int primarySurfaceWaveFlickerSequence;
    float primarySurfaceWaveSettlingStartRealtime;
    bool primarySurfaceWaveSettlingWaitingForFirstSample;
    bool primaryVoxelTopologyDirty = true;
    bool primaryVoxelInitialized;
    bool boundaryBindingsAttempted;
    bool boundaryBuffersSupported;

    public float CellSize => cellSize > 0f ? cellSize : particleRadius * 2f;
    public int TotalCells => totalCells > 0 ? totalCells : gridResolution * gridResolution * gridResolution;
    public int BulkGridCountX => Mathf.Max(bulkGridCountX, 1);
    public int BulkGridCountZ => Mathf.Max(bulkGridCountZ, 1);
    int PrimaryVoxelLayerCount => Mathf.Clamp(primaryVoxelLayerCount, 4, 32);
    int PrimarySurfaceParticleCount => usePrimaryVoxelVolume
        ? Mathf.Clamp(primarySurfaceParticleBudget, 1, maxParticles)
        : activeParticles;
    int SprayParticleCount => Mathf.Clamp(sprayParticleCapacity, 128, 8192);

    void Start()
    {
        // If the tile renderer was not assigned in the Inspector, try to find
        // one in the scene automatically so the sim still wires itself up.
        if (surfaceTileRenderer == null)
            surfaceTileRenderer = FindFirstObjectByType<SurfaceTileRenderer>();

        // The grid cell size matches the interaction diameter. This keeps the
        // 27-cell neighbour search reasonable for the current particle radius.
        cellSize = particleRadius * 2f;
        totalCells = gridResolution * gridResolution * gridResolution;

        // Startup order:
        // 1. cache compute kernels
        // 2. allocate buffers
        // 3. fill particle data
        // 4. bind buffers to all kernels
        // 5. wire render systems to the created buffers
        CacheKernels();
        AllocateBuffers();
        InitializeParticles();
        BindAllBuffers();
        RebuildSolidColliderBuffer();
        WireRuntimeReferences();
    }

    void CacheKernels()
    {
        // These names must exist in fluid.compute once the new active/dormant
        // pipeline is implemented there.
        kernelClassifyParticles = fluidCS.FindKernel("CSClassifyParticles");
        kernelBuildBulkGrid = fluidCS.FindKernel("CSBuildBulkGrid");
        kernelFinalizeBulkGrid = fluidCS.FindKernel("CSFinalizeBulkGrid");
        kernelCountBulkGrid = fluidCS.FindKernel("CSCountBulkGrid");
        kernelClearBulkGridCounts = fluidCS.FindKernel("CSClearBulkGridCounts");
        kernelClearBulkTokenDeltas = fluidCS.FindKernel("CSClearBulkTokenDeltas");
        kernelSeedBulkVolumeFromTokenCounts = fluidCS.FindKernel("CSSeedBulkVolumeFromTokenCounts");
        kernelApplyBulkTokenDelta = fluidCS.FindKernel("CSApplyBulkTokenDelta");
        kernelBuildBulkTopology = fluidCS.FindKernel("CSBuildBulkTopology");
        kernelComputeBulkFlux = fluidCS.FindKernel("CSComputeBulkFlux");
        kernelComputeBulkReceiveScale = fluidCS.FindKernel("CSComputeBulkReceiveScale");
        kernelApplyBulkFlux = fluidCS.FindKernel("CSApplyBulkFlux");
        kernelAccumulateBulkTokenFlux = fluidCS.FindKernel("CSAccumulateBulkTokenFlux");
        kernelClearBulkTokenReconciliation = fluidCS.FindKernel("CSClearBulkTokenReconciliation");
        kernelReconcileBulkTokens = fluidCS.FindKernel("CSReconcileBulkTokens");
        kernelSpawnFromExposedBulk = fluidCS.FindKernel("CSSpawnFromExposedBulk");
        kernelDecayBulkActivity = fluidCS.FindKernel("CSDecayBulkActivity");
        kernelAccumulateBulkActivity = fluidCS.FindKernel("CSAccumulateBulkActivity");
        kernelWakeBulkActivity = fluidCS.FindKernel("CSWakeBulkActivity");
        kernelClearHash = fluidCS.FindKernel("CSClearHash");
        kernelBuildHash = fluidCS.FindKernel("CSBuildHash");
        kernelWakeDormantFromActive = fluidCS.FindKernel("CSWakeDormantFromActive");
        kernelDensityActive = fluidCS.FindKernel("CSDensityActive");
        kernelForcesActive = fluidCS.FindKernel("CSForcesActive");
        kernelIntegrateActive = fluidCS.FindKernel("CSIntegrateActive");
        kernelPBFPredictActive = fluidCS.FindKernel("CSPBFPredictActive");
        kernelPBFComputeLambda = fluidCS.FindKernel("CSPBFComputeLambda");
        kernelPBFComputeDelta = fluidCS.FindKernel("CSPBFComputeDelta");
        kernelPBFApplyDelta = fluidCS.FindKernel("CSPBFApplyDelta");
        kernelPBFUpdateVelocity = fluidCS.FindKernel("CSPBFUpdateVelocity");
        kernelUpdateDormant = fluidCS.FindKernel("CSUpdateDormant");
        kernelClearVoxelField = fluidCS.FindKernel("CSClearVoxelField");
        kernelScatterParticleVoxels = fluidCS.FindKernel("CSScatterParticleVoxels");
        kernelFinalizeVoxelField = fluidCS.FindKernel("CSFinalizeVoxelField");
        kernelClearMicroVoxelField = fluidCS.FindKernel("CSClearMicroVoxelField");
        kernelScatterMicroVoxelParticles = fluidCS.FindKernel("CSScatterMicroVoxelParticles");
        kernelFinalizeMicroVoxelField = fluidCS.FindKernel("CSFinalizeMicroVoxelField");
        kernelSmoothMicroVoxelField = fluidCS.FindKernel("CSSmoothMicroVoxelField");
        kernelClearPrimaryVoxelVolume = fluidCS.FindKernel("CSClearPrimaryVoxelVolume");
        kernelClearPrimaryVoxelReservoirBoundary = fluidCS.FindKernel("CSClearPrimaryVoxelReservoirBoundary");
        kernelInjectPrimaryVoxelVolume = fluidCS.FindKernel("CSInjectPrimaryVoxelVolume");
        kernelDebugPrimaryVoxelHatchProbe = fluidCS.FindKernel("CSDebugPrimaryVoxelHatchProbe");
        kernelQueryPrimaryVoxelHatchDebug = fluidCS.FindKernel("CSQueryPrimaryVoxelHatchDebug");
        kernelBuildPrimaryVoxelSolids = fluidCS.FindKernel("CSBuildPrimaryVoxelSolids");
        kernelCopyPrimaryVoxelFrameState = fluidCS.FindKernel("CSCopyPrimaryVoxelFrameState");
        kernelComputePrimaryVoxelFlux = fluidCS.FindKernel("CSComputePrimaryVoxelFlux");
        kernelComputePrimaryVoxelReceiveScale = fluidCS.FindKernel("CSComputePrimaryVoxelReceiveScale");
        kernelApplyPrimaryVoxelFlux = fluidCS.FindKernel("CSApplyPrimaryVoxelFlux");
        kernelProjectPrimaryVoxelColumns = fluidCS.FindKernel("CSProjectPrimaryVoxelColumns");
        kernelSeedPrimaryVoxelSurface = fluidCS.FindKernel("CSSeedPrimaryVoxelSurface");
        kernelConstrainPrimaryVoxelSurface = fluidCS.FindKernel("CSConstrainPrimaryVoxelSurface");
        kernelCopyPrimaryVoxelDisplay = fluidCS.FindKernel("CSCopyPrimaryVoxelDisplay");
        kernelClearPrimaryVoxelDiagnostics = fluidCS.FindKernel("CSClearPrimaryVoxelDiagnostics");
        kernelCollectPrimaryVoxelDiagnostics = fluidCS.FindKernel("CSCollectPrimaryVoxelDiagnostics");
        kernelClearSpray = fluidCS.FindKernel("CSClearSpray");
        kernelSpawnSpray = fluidCS.FindKernel("CSSpawnSpray");
        kernelUpdateSpray = fluidCS.FindKernel("CSUpdateSpray");
        kernelBuildPrimaryVoxelCurrentSplats = fluidCS.FindKernel("CSBuildPrimaryVoxelCurrentSplats");
        kernelUpdatePrimaryVoxelUnderwaterSplats = fluidCS.FindKernel("CSUpdatePrimaryVoxelUnderwaterSplats");
        kernelBuildPrimaryVoxelSurfaceGaussians = fluidCS.FindKernel("CSBuildPrimaryVoxelSurfaceGaussians");
        kernelClearPrimaryVoxelSurfaceGaussianDiagnostics = fluidCS.FindKernel("CSClearPrimaryVoxelSurfaceGaussianDiagnostics");
        kernelBuildPrimarySurfaceWaveState = fluidCS.FindKernel("CSBuildPrimarySurfaceWaveState");
        kernelComputePrimarySurfaceWaveFlux = fluidCS.FindKernel("CSComputePrimarySurfaceWaveFlux");
        kernelUpdatePrimarySurfaceWave = fluidCS.FindKernel("CSUpdatePrimarySurfaceWave");
        kernelCollectPrimarySurfaceWaveFlickerDiagnostic = fluidCS.FindKernel("CSCollectPrimarySurfaceWaveFlickerDiagnostic");
        kernelBuildPrimarySurfaceFlowState = fluidCS.FindKernel("CSBuildPrimarySurfaceFlowState");
        kernelAdvectPrimarySurfaceFlow = fluidCS.FindKernel("CSAdvectPrimarySurfaceFlow");    }

    void AllocateBuffers()
    {
        // Particle buffer stores all particle state for maxParticles slots.
        particleBuffer = new ComputeBuffer(maxParticles, ParticleStride);
        previousPositionBuffer = new ComputeBuffer(maxParticles, PreviousPositionStride);
        particleSurfaceAgeBuffer = new ComputeBuffer(maxParticles, SurfaceAgeStride);
        particleDormantSpeedAverageBuffer = new ComputeBuffer(maxParticles, DormantSpeedAverageStride);

        // Hash buffers support linked-list style spatial hashing.
        cellHeadsBuffer = new ComputeBuffer(totalCells, sizeof(int));
        nextIndexBuffer = new ComputeBuffer(maxParticles, sizeof(int));
        debugForceBuffer = new ComputeBuffer(maxParticles, ForceDebugStride);
        pbfPreviousPositionBuffer = new ComputeBuffer(maxParticles, PreviousPositionStride);
        pbfPredictedPositionBuffer = new ComputeBuffer(maxParticles, PreviousPositionStride);
        pbfPredictedVelocityBuffer = new ComputeBuffer(maxParticles, PreviousPositionStride);
        pbfLambdaBuffer = new ComputeBuffer(maxParticles, sizeof(float));
        pbfPositionDeltaBuffer = new ComputeBuffer(maxParticles, PBFDeltaStride);
        bulkPhase2CounterBuffer = new ComputeBuffer(12, sizeof(int));
        bulkPhase2CounterBuffer.SetData(new int[12]);
        bulkWakeCounterBuffer = new ComputeBuffer(1, sizeof(int));
        bulkWakeCounterBuffer.SetData(new int[1]);
        bulkVoxelCounterBuffer = new ComputeBuffer(3, BulkVoxelCounterStride);
        bulkVoxelCounterBuffer.SetData(new int[3]);
        EnsureBulkWaterBuffer(BulkGridCountX * BulkGridCountZ);
        EnsureBulkVoxelBuffer(BulkGridCountX * BulkGridCountZ);
        EnsurePrimaryVoxelBuffers();
        EnsurePrimarySurfaceWaveBuffers();
        EnsureMicroVoxelBuffers();
        EnsureSolidColliderBuffer();
        EnsureFallbackSurfaceTileBuffer();
        sprayParticleBuffer = new ComputeBuffer(SprayParticleCount, SprayParticleStride);
        sprayParticleBuffer.SetData(new SprayParticleGPU[SprayParticleCount]);
        EnsurePrimaryVoxelCurrentSplatResources();
    }

    void InitializeParticles()
    {
        // Pre-fill the whole particle buffer. Particles start positioned at the
        // inflow, but activeParticles stays at 0 until they are officially
        // spawned into the live set.
        GPUParticle[] init = new GPUParticle[maxParticles];
        Vector3[] previousPositions = new Vector3[maxParticles];
        Vector3[] predictedVelocities = new Vector3[maxParticles];

        for (int i = 0; i < maxParticles; i++)
        {
            Vector3 origin = spawnPoint != null ? spawnPoint.position : Vector3.zero;
            Vector3 forward = spawnPoint != null ? spawnPoint.forward : Vector3.forward;
            Vector3 jitter = Random.insideUnitSphere * inflowRadius;

            init[i].pos = origin + jitter;
            init[i].vel = forward * inflowSpeed;
            init[i].invMass = 1f;
            init[i].density = 0f;

            // New particles start as active so they fully participate in the
            // more expensive top-layer simulation path when emitted.
            init[i].state = usePrimaryVoxelVolume
                ? (int)ParticleState.Bulk
                : (int)ParticleState.Active;
            init[i].sleepTimer = 0f;
            init[i].padding = Vector2.zero;
            previousPositions[i] = init[i].pos;
            predictedVelocities[i] = init[i].vel;
        }

        particleBuffer.SetData(init);
        previousPositionBuffer.SetData(previousPositions);
        particleSurfaceAgeBuffer.SetData(new float[maxParticles]);
        particleDormantSpeedAverageBuffer.SetData(new float[maxParticles]);
        pbfPreviousPositionBuffer.SetData(previousPositions);
        pbfPredictedPositionBuffer.SetData(previousPositions);
        pbfPredictedVelocityBuffer.SetData(predictedVelocities);
        pbfLambdaBuffer.SetData(new float[maxParticles]);
        pbfPositionDeltaBuffer.SetData(new Vector4[maxParticles]);
        activeParticles = 0;
        liveParticles = 0;
        activeParticleCount = 0;
        dormantParticleCount = 0;
        bulkParticleCount = 0;
        bulkSpawnedLastStep = 0;
        bulkAbsorbedLastStep = 0;
        estimatedSurfaceLevel = boundsMin.y;
        estimatedActiveBandBottom = boundsMin.y;
        outOfBoundsParticleCount = 0;
        insideDebugBoundsParticleCount = 0;
        outsideDebugBoundsParticleCount = 0;
        overlappingParticlePairCount = 0;
        averageOverlapDistance = 0f;
        averageCompressionPercent = 0f;
        worstOverlapDistance = 0f;
        worstCompressionPercent = 0f;
        overlapPairsAbove10Percent = 0;
        overlapPairsAbove25Percent = 0;
        overlapPairsAbove50Percent = 0;
        averageLiveDensity = 0f;
        averageLivePressure = 0f;
        averageDensityMinusRest = 0f;
        densityStandardDeviation = 0f;
        densityStdDevPercentOfAverage = 0f;
        densityStdDevPercentOfRest = 0f;
        averagePressureForceMagnitude = 0f;
        averageViscosityForceMagnitude = 0f;
        averageCohesionForceMagnitude = 0f;
        averageMergeSplitForceMagnitude = 0f;
        averagePressureDeltaVelocity = 0f;
        averageViscosityDeltaVelocity = 0f;
        averageCohesionDeltaVelocity = 0f;
        averageMergeSplitDeltaVelocity = 0f;
        maxPressureDeltaVelocity = 0f;
        maxViscosityDeltaVelocity = 0f;
        maxCohesionDeltaVelocity = 0f;
        maxMergeSplitDeltaVelocity = 0f;
        maxPressureForceMagnitude = 0f;
        maxViscosityForceMagnitude = 0f;
        maxCohesionForceMagnitude = 0f;
        maxMergeSplitForceMagnitude = 0f;
    }

    void BindAllBuffers()
    {
        // Every kernel works on the same particle and hash buffers.
        BindBuffers(kernelClassifyParticles);
        BindBuffers(kernelCountBulkGrid);
        BindBuffers(kernelClearBulkGridCounts);
        BindBuffers(kernelClearBulkTokenDeltas);
        BindBuffers(kernelSeedBulkVolumeFromTokenCounts);
        BindBuffers(kernelApplyBulkTokenDelta);
        BindBuffers(kernelBuildBulkTopology);
        BindBuffers(kernelComputeBulkFlux);
        BindBuffers(kernelComputeBulkReceiveScale);
        BindBuffers(kernelApplyBulkFlux);
        BindBuffers(kernelAccumulateBulkTokenFlux);
        BindBuffers(kernelClearBulkTokenReconciliation);
        BindBuffers(kernelReconcileBulkTokens);
        BindBuffers(kernelSpawnFromExposedBulk);
        BindBuffers(kernelDecayBulkActivity);
        BindBuffers(kernelAccumulateBulkActivity);
        BindBuffers(kernelWakeBulkActivity);
        BindBuffers(kernelClearHash);
        BindBuffers(kernelBuildHash);
        BindBuffers(kernelWakeDormantFromActive);
        BindBuffers(kernelDensityActive);
        BindBuffers(kernelForcesActive);
        BindBuffers(kernelIntegrateActive);
        BindBuffers(kernelPBFPredictActive);
        BindBuffers(kernelPBFComputeLambda);
        BindBuffers(kernelPBFComputeDelta);
        BindBuffers(kernelPBFApplyDelta);
        BindBuffers(kernelPBFUpdateVelocity);
        BindBuffers(kernelUpdateDormant);
        BindBuffers(kernelClearVoxelField);
        BindBuffers(kernelScatterParticleVoxels);
        BindBuffers(kernelFinalizeVoxelField);
        BindBuffers(kernelClearMicroVoxelField);
        BindBuffers(kernelScatterMicroVoxelParticles);
        BindBuffers(kernelFinalizeMicroVoxelField);
        BindBuffers(kernelSmoothMicroVoxelField);
        BindBuffers(kernelSeedPrimaryVoxelSurface);
        BindBuffers(kernelConstrainPrimaryVoxelSurface);
        BindPrimaryVoxelBuffers();
        BindPrimarySurfaceWaveBuffers();
        BindSolidColliderBuffer();
        TryBindBoundaryBuffers();
        TryBindSurfaceTilesToFluid();
    }

    void BindPrimaryVoxelBuffers()
    {
        EnsurePrimaryVoxelBuffers();
        fluidCS.SetBuffer(kernelClearPrimaryVoxelVolume, "primaryVoxelCells", primaryVoxelBuffer);
        fluidCS.SetBuffer(kernelClearPrimaryVoxelVolume, "primaryVoxelDeferredInflow",
            primaryVoxelDeferredInflowBuffer);
        fluidCS.SetBuffer(kernelClearPrimaryVoxelReservoirBoundary, "primaryVoxelReservoirBoundaryWrite", primaryVoxelReservoirBoundaryBuffer);
        fluidCS.SetBuffer(kernelInjectPrimaryVoxelVolume, "primaryVoxelCells", primaryVoxelBuffer);
        fluidCS.SetBuffer(kernelInjectPrimaryVoxelVolume, "primaryVoxelInflow", primaryVoxelInflowBuffer);
        fluidCS.SetBuffer(kernelInjectPrimaryVoxelVolume, "primaryVoxelSolidRead", primaryVoxelSolidBuffer);
        fluidCS.SetBuffer(kernelInjectPrimaryVoxelVolume, "primaryVoxelReservoirBoundaryWrite", primaryVoxelReservoirBoundaryBuffer);
        fluidCS.SetBuffer(kernelInjectPrimaryVoxelVolume, "primaryVoxelDeferredInflow",
            primaryVoxelDeferredInflowBuffer);
        fluidCS.SetBuffer(kernelInjectPrimaryVoxelVolume, "primaryVoxelDiagnostics", primaryVoxelDiagnosticsBuffer);
        fluidCS.SetBuffer(kernelDebugPrimaryVoxelHatchProbe, "primaryVoxelHatchProbe", primaryVoxelHatchProbeBuffer);
        fluidCS.SetBuffer(kernelDebugPrimaryVoxelHatchProbe, "primaryVoxelSolidRead", primaryVoxelSolidBuffer);
        fluidCS.SetBuffer(kernelQueryPrimaryVoxelHatchDebug, "primaryVoxelHatchDebug", primaryVoxelHatchDebugBuffer);
        fluidCS.SetBuffer(kernelBuildPrimaryVoxelSolids, "primaryVoxelSolid", primaryVoxelSolidBuffer);
        fluidCS.SetBuffer(kernelBuildPrimaryVoxelSolids, "primaryVoxelFaceOpen", primaryVoxelFaceOpenBuffer);
        fluidCS.SetBuffer(kernelCopyPrimaryVoxelFrameState, "primaryVoxelCellsRead", primaryVoxelBuffer);
        fluidCS.SetBuffer(kernelCopyPrimaryVoxelFrameState, "primaryVoxelCellsFrameCopy",
            primaryVoxelPreviousFrameBuffer);
        fluidCS.SetBuffer(kernelComputePrimaryVoxelFlux, "primaryVoxelSolidRead", primaryVoxelSolidBuffer);
        fluidCS.SetBuffer(kernelComputePrimaryVoxelFlux, "primaryVoxelFaceFluxWrite",
            primaryVoxelFluxBuffer);
        fluidCS.SetBuffer(kernelComputePrimaryVoxelReceiveScale, "primaryVoxelSolidRead",
            primaryVoxelSolidBuffer);
        fluidCS.SetBuffer(kernelComputePrimaryVoxelReceiveScale, "primaryVoxelFaceFluxRead",
            primaryVoxelFluxBuffer);
        fluidCS.SetBuffer(kernelComputePrimaryVoxelReceiveScale, "primaryVoxelReservoirBoundaryRead",
            primaryVoxelReservoirBoundaryBuffer);
        fluidCS.SetBuffer(kernelComputePrimaryVoxelReceiveScale, "primaryVoxelReceiveScaleWrite",
            primaryVoxelReceiveScaleBuffer);
        fluidCS.SetBuffer(kernelApplyPrimaryVoxelFlux, "primaryVoxelSolidRead", primaryVoxelSolidBuffer);
        fluidCS.SetBuffer(kernelApplyPrimaryVoxelFlux, "primaryVoxelFaceFluxRead",
            primaryVoxelFluxBuffer);
        fluidCS.SetBuffer(kernelApplyPrimaryVoxelFlux, "primaryVoxelReceiveScaleRead",
            primaryVoxelReceiveScaleBuffer);
        fluidCS.SetBuffer(kernelApplyPrimaryVoxelFlux, "primaryVoxelReservoirBoundaryRead",
            primaryVoxelReservoirBoundaryBuffer);
        fluidCS.SetBuffer(kernelApplyPrimaryVoxelFlux, "primaryVoxelDiagnostics",
            primaryVoxelDiagnosticsBuffer);
        BindPrimaryVoxelFlowPingPongBuffers();
        BindPrimaryVoxelReadConsumers();
    }

    void BindPrimaryVoxelFlowPingPongBuffers()
    {
        fluidCS.SetBuffer(kernelComputePrimaryVoxelFlux, "primaryVoxelCellsRead", primaryVoxelBuffer);
        fluidCS.SetBuffer(kernelComputePrimaryVoxelReceiveScale, "primaryVoxelCellsRead",
            primaryVoxelBuffer);
        fluidCS.SetBuffer(kernelApplyPrimaryVoxelFlux, "primaryVoxelCellsRead", primaryVoxelBuffer);
        fluidCS.SetBuffer(kernelApplyPrimaryVoxelFlux, "primaryVoxelCellsNext", primaryVoxelNextBuffer);
        fluidCS.SetBuffer(kernelDebugPrimaryVoxelHatchProbe, "primaryVoxelCells", primaryVoxelBuffer);
        fluidCS.SetBuffer(kernelQueryPrimaryVoxelHatchDebug, "primaryVoxelCellsRead", primaryVoxelBuffer);
    }

    void BindPrimaryVoxelReadConsumers()
    {
        fluidCS.SetBuffer(kernelQueryPrimaryVoxelHatchDebug, "primaryVoxelCellsRead", primaryVoxelBuffer);
        fluidCS.SetBuffer(kernelQueryPrimaryVoxelHatchDebug, "primaryVoxelSolidRead", primaryVoxelSolidBuffer);
        fluidCS.SetBuffer(kernelQueryPrimaryVoxelHatchDebug, "primaryVoxelFaceFluxRead", primaryVoxelFluxBuffer);
        fluidCS.SetBuffer(kernelProjectPrimaryVoxelColumns, "primaryVoxelCellsRead", primaryVoxelBuffer);
        fluidCS.SetBuffer(kernelProjectPrimaryVoxelColumns, "primaryVoxelSolidRead", primaryVoxelSolidBuffer);
        fluidCS.SetBuffer(kernelProjectPrimaryVoxelColumns, "bulkWaterCells", bulkWaterBuffer);
        fluidCS.SetBuffer(kernelProjectPrimaryVoxelColumns, "primaryVoxelDiagnostics", primaryVoxelDiagnosticsBuffer);
        fluidCS.SetBuffer(kernelSeedPrimaryVoxelSurface, "primaryVoxelCellsRead", primaryVoxelBuffer);
        fluidCS.SetBuffer(kernelSeedPrimaryVoxelSurface, "primaryVoxelSolidRead", primaryVoxelSolidBuffer);
        fluidCS.SetBuffer(kernelSeedPrimaryVoxelSurface, "bulkWaterCells", bulkWaterBuffer);
        fluidCS.SetBuffer(kernelCopyPrimaryVoxelDisplay, "primaryVoxelCellsRead", primaryVoxelBuffer);
        fluidCS.SetBuffer(kernelCopyPrimaryVoxelDisplay, "primaryVoxelSolidRead", primaryVoxelSolidBuffer);
        fluidCS.SetBuffer(kernelCopyPrimaryVoxelDisplay, "bulkWaterCells", bulkWaterBuffer);
        fluidCS.SetBuffer(kernelCopyPrimaryVoxelDisplay, "bulkVoxelCells", bulkVoxelBuffer);
        fluidCS.SetBuffer(kernelCopyPrimaryVoxelDisplay, "bulkVoxelCounters", bulkVoxelCounterBuffer);
        fluidCS.SetBuffer(kernelClearPrimaryVoxelDiagnostics, "primaryVoxelDiagnostics", primaryVoxelDiagnosticsBuffer);
        fluidCS.SetBuffer(kernelCollectPrimaryVoxelDiagnostics, "primaryVoxelCellsRead", primaryVoxelBuffer);
        fluidCS.SetBuffer(kernelCollectPrimaryVoxelDiagnostics, "primaryVoxelSolidRead", primaryVoxelSolidBuffer);
        fluidCS.SetBuffer(kernelCollectPrimaryVoxelDiagnostics, "primaryVoxelFaceFluxRead", primaryVoxelFluxBuffer);
        fluidCS.SetBuffer(kernelCollectPrimaryVoxelDiagnostics, "primaryVoxelReceiveScaleRead", primaryVoxelReceiveScaleBuffer);
        fluidCS.SetBuffer(kernelCollectPrimaryVoxelDiagnostics, "primaryVoxelDiagnostics", primaryVoxelDiagnosticsBuffer);
        fluidCS.SetBuffer(kernelPBFPredictActive, "primaryVoxelCellsRead", primaryVoxelBuffer);
        fluidCS.SetBuffer(kernelPBFPredictActive, "primaryVoxelCellsPreviousFrame",
            primaryVoxelPreviousFrameBuffer);
        fluidCS.SetBuffer(kernelPBFPredictActive, "primaryVoxelSolidRead", primaryVoxelSolidBuffer);
        // The deck-aware bulk support sampler is also used by the PBF
        // projection kernels. Keep their primary-volume reads explicitly
        // bound; otherwise Unity dispatches them with null SRVs.
        fluidCS.SetBuffer(kernelPBFComputeLambda, "primaryVoxelCellsRead", primaryVoxelBuffer);
        fluidCS.SetBuffer(kernelPBFComputeLambda, "primaryVoxelSolidRead", primaryVoxelSolidBuffer);
        fluidCS.SetBuffer(kernelPBFComputeDelta, "primaryVoxelCellsRead", primaryVoxelBuffer);
        fluidCS.SetBuffer(kernelPBFComputeDelta, "primaryVoxelSolidRead", primaryVoxelSolidBuffer);
        fluidCS.SetBuffer(kernelPBFApplyDelta, "primaryVoxelCellsRead", primaryVoxelBuffer);
        fluidCS.SetBuffer(kernelPBFApplyDelta, "primaryVoxelSolidRead", primaryVoxelSolidBuffer);
        fluidCS.SetBuffer(kernelPBFUpdateVelocity, "primaryVoxelCellsRead", primaryVoxelBuffer);
        fluidCS.SetBuffer(kernelPBFUpdateVelocity, "primaryVoxelSolidRead", primaryVoxelSolidBuffer);
        fluidCS.SetBuffer(kernelIntegrateActive, "primaryVoxelCellsRead", primaryVoxelBuffer);
        fluidCS.SetBuffer(kernelIntegrateActive, "primaryVoxelSolidRead", primaryVoxelSolidBuffer);
        fluidCS.SetBuffer(kernelConstrainPrimaryVoxelSurface, "primaryVoxelCellsRead", primaryVoxelBuffer);
        fluidCS.SetBuffer(kernelConstrainPrimaryVoxelSurface, "primaryVoxelSolidRead", primaryVoxelSolidBuffer);
        fluidCS.SetBuffer(kernelConstrainPrimaryVoxelSurface, "bulkWaterCells", bulkWaterBuffer);
        fluidCS.SetBuffer(kernelSpawnSpray, "primaryVoxelCellsRead", primaryVoxelBuffer);
        fluidCS.SetBuffer(kernelSpawnSpray, "primaryVoxelSolidRead", primaryVoxelSolidBuffer);
    }

    void SwapPrimaryVoxelBuffers()
    {
        ComputeBuffer swap = primaryVoxelBuffer;
        primaryVoxelBuffer = primaryVoxelNextBuffer;
        primaryVoxelNextBuffer = swap;
        // A substep changes only the ping-pong identities. Rebinding every
        // simulation resource here was a measurable CPU cost at high FPS.
        BindPrimaryVoxelFlowPingPongBuffers();
    }

    void TryBindSurfaceTilesToFluid()
    {
        EnsureFallbackSurfaceTileBuffer();

        ComputeBuffer sourceBuffer = fallbackSurfaceTileBuffer;
        int countX = 1;
        int countZ = 1;
        float tileSizeForShader = Mathf.Max(boundsMax.x - boundsMin.x, 0.0001f);

        SurfaceTileRenderer activeSurfaceTileRenderer = disableSurfaceTileRendererForProfiling ? null : surfaceTileRenderer;
        if (activeSurfaceTileRenderer != null && activeSurfaceTileRenderer.HasTileBuffer)
        {
            sourceBuffer = activeSurfaceTileRenderer.TileBuffer;
            countX = Mathf.Max(activeSurfaceTileRenderer.TileCountX, 1);
            countZ = Mathf.Max(activeSurfaceTileRenderer.TileCountZ, 1);
            tileSizeForShader = Mathf.Max(activeSurfaceTileRenderer.TileSize, 0.0001f);
        }

        EnsureBulkWaterBuffer(BulkGridCountX * BulkGridCountZ);
        EnsureBulkVoxelBuffer(BulkGridCountX * BulkGridCountZ);
        EnsureMicroVoxelBuffers();
        if (activeSurfaceTileRenderer != null)
        {
            activeSurfaceTileRenderer.bulkWaterBuffer = bulkWaterBuffer;
            activeSurfaceTileRenderer.bulkGridCountX = BulkGridCountX;
            activeSurfaceTileRenderer.bulkGridCountZ = BulkGridCountZ;
            activeSurfaceTileRenderer.primaryVoxelFlowBuffer = usePrimaryVoxelVolume ? primaryVoxelBuffer : null;
            activeSurfaceTileRenderer.primarySurfaceWaveBuffer =
                usePrimaryVoxelVolume && enablePrimarySurfaceWaves ? primarySurfaceWaveBuffer : null;
            activeSurfaceTileRenderer.primaryVoxelSolidBuffer = usePrimaryVoxelVolume ? primaryVoxelSolidBuffer : null;
            activeSurfaceTileRenderer.primaryVoxelFaceOpenBuffer = usePrimaryVoxelVolume ? primaryVoxelFaceOpenBuffer : null;
            activeSurfaceTileRenderer.primaryVoxelFaceFluxBuffer = usePrimaryVoxelVolume ? primaryVoxelFluxBuffer : null;
            activeSurfaceTileRenderer.primaryVoxelFlowLayerCount = usePrimaryVoxelVolume ? PrimaryVoxelLayerCount : 1;
            activeSurfaceTileRenderer.primaryVoxelFlowHeight = usePrimaryVoxelVolume
                ? Mathf.Max(primaryVoxelHeight, 0.05f) : 1f;
            activeSurfaceTileRenderer.primarySurfaceWaveMaxDisplacement =
                Mathf.Max(primarySurfaceWaveMaxDisplacement, 0.001f);
            activeSurfaceTileRenderer.primarySurfaceWaveVisualResponse =
                Mathf.Max(primarySurfaceWaveVisualResponse, 0.01f);
            activeSurfaceTileRenderer.primarySurfaceWaveVisualMaximumVerticalSpeed =
                Mathf.Max(primarySurfaceWaveVisualMaximumVerticalSpeed, 0.01f);
            activeSurfaceTileRenderer.primarySurfaceWaveVisualSpatialSmoothing =
                Mathf.Clamp01(primarySurfaceWaveVisualSpatialSmoothing);
            activeSurfaceTileRenderer.usePrimarySurfaceWaveArtificialRippleLifecycle =
                usePrimarySurfaceWaveArtificialRippleLifecycle;
            activeSurfaceTileRenderer.primarySurfaceWaveVisualRippleDuration =
                Mathf.Max(primarySurfaceWaveVisualRippleDuration, 0.01f);
            activeSurfaceTileRenderer.primarySurfaceWaveVisualRippleMaximumAmplitude =
                Mathf.Max(primarySurfaceWaveVisualRippleMaximumAmplitude, 0.001f);
            activeSurfaceTileRenderer.primarySurfaceWaveVisualRippleActivationAmplitude =
                Mathf.Max(primarySurfaceWaveVisualRippleActivationAmplitude, 0.0001f);
            activeSurfaceTileRenderer.primaryVoxelSurfaceSlotCount =
                Mathf.Clamp(primarySurfaceSlotsPerColumn, 1, 3);
            activeSurfaceTileRenderer.primaryVoxelSurfaceMinimumGapLayers =
                Mathf.Clamp(primarySurfaceMinimumVerticalGapLayers, 1, 4);
        }

        fluidCS.SetBuffer(kernelBuildBulkGrid, "bulkWaterCells", bulkWaterBuffer);
        fluidCS.SetBuffer(kernelBuildBulkGrid, "bulkPhase2Counters", bulkPhase2CounterBuffer);
        fluidCS.SetBuffer(kernelFinalizeBulkGrid, "bulkWaterCells", bulkWaterBuffer);
        fluidCS.SetBuffer(kernelClearBulkTokenDeltas, "bulkTokenDeltas", bulkTokenDeltaBuffer);
        fluidCS.SetBuffer(kernelSeedBulkVolumeFromTokenCounts, "bulkWaterCells", bulkWaterBuffer);
        fluidCS.SetBuffer(kernelApplyBulkTokenDelta, "bulkWaterCells", bulkWaterBuffer);
        fluidCS.SetBuffer(kernelApplyBulkTokenDelta, "bulkTokenDeltas", bulkTokenDeltaBuffer);
        fluidCS.SetBuffer(kernelBuildBulkTopology, "bulkFaceSillY", bulkFaceSillBuffer);
        fluidCS.SetBuffer(kernelComputeBulkFlux, "bulkWaterCells", bulkWaterBuffer);
        fluidCS.SetBuffer(kernelComputeBulkFlux, "bulkFluxCells", bulkFluxBuffer);
        fluidCS.SetBuffer(kernelComputeBulkFlux, "bulkFaceSillYRead", bulkFaceSillBuffer);
        fluidCS.SetBuffer(kernelComputeBulkReceiveScale, "bulkWaterCells", bulkWaterBuffer);
        fluidCS.SetBuffer(kernelComputeBulkReceiveScale, "bulkFluxCells", bulkFluxBuffer);
        fluidCS.SetBuffer(kernelComputeBulkReceiveScale, "bulkReceiveScales", bulkReceiveScaleBuffer);
        fluidCS.SetBuffer(kernelApplyBulkFlux, "bulkWaterCells", bulkWaterBuffer);
        fluidCS.SetBuffer(kernelApplyBulkFlux, "bulkWaterCellsNext", bulkWaterNextBuffer);
        fluidCS.SetBuffer(kernelApplyBulkFlux, "bulkFluxCells", bulkFluxBuffer);
        fluidCS.SetBuffer(kernelApplyBulkFlux, "bulkReceiveScales", bulkReceiveScaleBuffer);
        fluidCS.SetBuffer(kernelAccumulateBulkTokenFlux, "bulkFluxCells", bulkFluxBuffer);
        fluidCS.SetBuffer(kernelAccumulateBulkTokenFlux, "bulkReceiveScales", bulkReceiveScaleBuffer);
        fluidCS.SetBuffer(kernelAccumulateBulkTokenFlux, "bulkTokenFluxResiduals", bulkTokenFluxResidualBuffer);
        fluidCS.SetBuffer(kernelAccumulateBulkTokenFlux, "bulkTokenMoveQuotas", bulkTokenMoveQuotaBuffer);
        fluidCS.SetBuffer(kernelClearBulkTokenReconciliation, "bulkTokenFluxResiduals", bulkTokenFluxResidualBuffer);
        fluidCS.SetBuffer(kernelClearBulkTokenReconciliation, "bulkTokenMoveQuotas", bulkTokenMoveQuotaBuffer);
        fluidCS.SetBuffer(kernelReconcileBulkTokens, "bulkWaterCells", bulkWaterBuffer);
        fluidCS.SetBuffer(kernelReconcileBulkTokens, "bulkTokenMoveQuotas", bulkTokenMoveQuotaBuffer);
        fluidCS.SetBuffer(kernelReconcileBulkTokens, "bulkFaceSillYRead", bulkFaceSillBuffer);
        fluidCS.SetBuffer(kernelDecayBulkActivity, "bulkActivityCells", bulkActivityBuffer);
        fluidCS.SetBuffer(kernelAccumulateBulkActivity, "bulkActivityCells", bulkActivityBuffer);
        fluidCS.SetBuffer(kernelWakeBulkActivity, "bulkWaterCells", bulkWaterBuffer);
        fluidCS.SetBuffer(kernelWakeBulkActivity, "bulkActivityCellsRead", bulkActivityBuffer);
        fluidCS.SetBuffer(kernelWakeBulkActivity, "bulkTokenDeltas", bulkTokenDeltaBuffer);
        fluidCS.SetBuffer(kernelWakeBulkActivity, "bulkPhase2Counters", bulkPhase2CounterBuffer);
        fluidCS.SetBuffer(kernelWakeBulkActivity, "bulkWakeCounter", bulkWakeCounterBuffer);
        fluidCS.SetBuffer(kernelClearVoxelField, "bulkVoxelCells", bulkVoxelBuffer);
        fluidCS.SetBuffer(kernelClearVoxelField, "bulkVoxelCounters", bulkVoxelCounterBuffer);
        fluidCS.SetBuffer(kernelScatterParticleVoxels, "bulkVoxelCells", bulkVoxelBuffer);
        fluidCS.SetBuffer(kernelScatterParticleVoxels, "bulkVoxelCounters", bulkVoxelCounterBuffer);
        fluidCS.SetBuffer(kernelScatterParticleVoxels, "surfaceTiles", sourceBuffer);
        fluidCS.SetBuffer(kernelScatterParticleVoxels, "bulkWaterCells", bulkWaterBuffer);
        fluidCS.SetBuffer(kernelFinalizeVoxelField, "bulkVoxelCells", bulkVoxelBuffer);
        fluidCS.SetBuffer(kernelFinalizeVoxelField, "bulkVoxelCounters", bulkVoxelCounterBuffer);
        fluidCS.SetBuffer(kernelFinalizeVoxelField, "surfaceTiles", sourceBuffer);
        fluidCS.SetBuffer(kernelFinalizeVoxelField, "bulkWaterCells", bulkWaterBuffer);
        fluidCS.SetBuffer(kernelClearMicroVoxelField, "microVoxelRaw", microVoxelRawBuffer);
        fluidCS.SetBuffer(kernelScatterMicroVoxelParticles, "microVoxelRaw", microVoxelRawBuffer);
        fluidCS.SetBuffer(kernelScatterMicroVoxelParticles, "surfaceTiles", sourceBuffer);
        fluidCS.SetBuffer(kernelScatterMicroVoxelParticles, "bulkWaterCells", bulkWaterBuffer);
        fluidCS.SetBuffer(kernelFinalizeMicroVoxelField, "microVoxelRaw", microVoxelRawBuffer);
        fluidCS.SetBuffer(kernelFinalizeMicroVoxelField, "surfaceTiles", sourceBuffer);
        fluidCS.SetBuffer(kernelFinalizeMicroVoxelField, "bulkWaterCells", bulkWaterBuffer);
        fluidCS.SetBuffer(kernelSmoothMicroVoxelField, "microVoxelRaw", microVoxelRawBuffer);
        fluidCS.SetBuffer(kernelClassifyParticles, "surfaceTiles", sourceBuffer);
        fluidCS.SetBuffer(kernelClassifyParticles, "bulkWaterCells", bulkWaterBuffer);
        fluidCS.SetBuffer(kernelClassifyParticles, "bulkPhase2Counters", bulkPhase2CounterBuffer);
        fluidCS.SetBuffer(kernelClassifyParticles, "bulkTokenDeltas", bulkTokenDeltaBuffer);
        fluidCS.SetBuffer(kernelClassifyParticles, "bulkFaceSillYRead", bulkFaceSillBuffer);
        fluidCS.SetBuffer(kernelClassifyParticles, "bulkActivityCellsRead", bulkActivityBuffer);
        fluidCS.SetBuffer(kernelCountBulkGrid, "surfaceTiles", sourceBuffer);
        fluidCS.SetBuffer(kernelCountBulkGrid, "bulkWaterCells", bulkWaterBuffer);
        fluidCS.SetBuffer(kernelClearBulkGridCounts, "bulkWaterCells", bulkWaterBuffer);
        // CSSpawnFromExposedBulk samples the local rendered footprint to
        // distinguish a genuine lower opening from an ordinary covered bulk
        // column. Keep the same tile source bound on that kernel.
        fluidCS.SetBuffer(kernelSpawnFromExposedBulk, "surfaceTiles", sourceBuffer);
        fluidCS.SetBuffer(kernelSpawnFromExposedBulk, "bulkWaterCells", bulkWaterBuffer);
        fluidCS.SetBuffer(kernelSpawnFromExposedBulk, "bulkPhase2Counters", bulkPhase2CounterBuffer);
        fluidCS.SetBuffer(kernelSpawnFromExposedBulk, "bulkTokenDeltas", bulkTokenDeltaBuffer);
        fluidCS.SetBuffer(kernelSpawnFromExposedBulk, "bulkFaceSillYRead", bulkFaceSillBuffer);
        fluidCS.SetBuffer(kernelForcesActive, "surfaceTiles", sourceBuffer);
        fluidCS.SetBuffer(kernelForcesActive, "bulkWaterCells", bulkWaterBuffer);
        fluidCS.SetBuffer(kernelForcesActive, "bulkFaceSillYRead", bulkFaceSillBuffer);
        fluidCS.SetBuffer(kernelPBFPredictActive, "bulkWaterCells", bulkWaterBuffer);
        fluidCS.SetBuffer(kernelPBFPredictActive, "bulkFaceSillYRead", bulkFaceSillBuffer);
        fluidCS.SetBuffer(kernelPBFComputeLambda, "bulkWaterCells", bulkWaterBuffer);
        fluidCS.SetBuffer(kernelPBFComputeLambda, "bulkFaceSillYRead", bulkFaceSillBuffer);
        fluidCS.SetBuffer(kernelPBFComputeDelta, "bulkWaterCells", bulkWaterBuffer);
        fluidCS.SetBuffer(kernelPBFComputeDelta, "bulkFaceSillYRead", bulkFaceSillBuffer);
        fluidCS.SetBuffer(kernelPBFApplyDelta, "bulkWaterCells", bulkWaterBuffer);
        fluidCS.SetBuffer(kernelPBFApplyDelta, "bulkFaceSillYRead", bulkFaceSillBuffer);
        fluidCS.SetBuffer(kernelPBFUpdateVelocity, "bulkWaterCells", bulkWaterBuffer);
        fluidCS.SetBuffer(kernelPBFUpdateVelocity, "bulkFaceSillYRead", bulkFaceSillBuffer);
        fluidCS.SetBuffer(kernelIntegrateActive, "bulkWaterCells", bulkWaterBuffer);
        fluidCS.SetBuffer(kernelIntegrateActive, "bulkFaceSillYRead", bulkFaceSillBuffer);
        fluidCS.SetBuffer(kernelUpdateDormant, "bulkWaterCells", bulkWaterBuffer);
        fluidCS.SetBuffer(kernelUpdateDormant, "bulkFaceSillYRead", bulkFaceSillBuffer);
        fluidCS.SetBuffer(kernelClearSpray, "sprayParticles", sprayParticleBuffer);
        fluidCS.SetBuffer(kernelSpawnSpray, "sprayParticles", sprayParticleBuffer);
        fluidCS.SetBuffer(kernelSpawnSpray, "particles", particleBuffer);
        fluidCS.SetBuffer(kernelSpawnSpray, "surfaceTiles", sourceBuffer);
        fluidCS.SetBuffer(kernelSpawnSpray, "bulkWaterCells", bulkWaterBuffer);
        fluidCS.SetBuffer(kernelSpawnSpray, "bulkFluxCells", bulkFluxBuffer);
        fluidCS.SetBuffer(kernelUpdateSpray, "sprayParticles", sprayParticleBuffer);
        fluidCS.SetBuffer(kernelUpdateSpray, "surfaceTiles", sourceBuffer);
        fluidCS.SetBuffer(kernelUpdateSpray, "bulkWaterCells", bulkWaterBuffer);
        fluidCS.SetInt("_SurfaceTileCountX", countX);
        fluidCS.SetInt("_SurfaceTileCountZ", countZ);
        fluidCS.SetFloat("_SurfaceTileSize", tileSizeForShader);
        fluidCS.SetInt("_BulkGridCountX", BulkGridCountX);
        fluidCS.SetInt("_BulkGridCountZ", BulkGridCountZ);
        fluidCS.SetInt("_SprayCount", SprayParticleCount);
    }

    void BindMicroDisplayBuffers()
    {
        if (microVoxelRawBuffer == null || microVoxelDisplayBufferA == null ||
            microVoxelDisplayBufferB == null)
            return;

        ComputeBuffer previous = microDisplayPing ? microVoxelDisplayBufferB : microVoxelDisplayBufferA;
        ComputeBuffer next = microDisplayPing ? microVoxelDisplayBufferA : microVoxelDisplayBufferB;
        fluidCS.SetBuffer(kernelSmoothMicroVoxelField, "microVoxelDisplayPrevious", previous);
        fluidCS.SetBuffer(kernelSmoothMicroVoxelField, "microVoxelDisplayNext", next);
        if (surfaceTileRenderer != null)
        {
            surfaceTileRenderer.microVoxelBuffer = next;
        }
    }

    void BindBuffers(int kernel)
    {
        // Bind the shared simulation buffers to a specific kernel.
        fluidCS.SetBuffer(kernel, "particles", particleBuffer);
        if (kernel == kernelClassifyParticles || kernel == kernelSpawnFromExposedBulk ||
            kernel == kernelWakeBulkActivity || kernel == kernelSeedPrimaryVoxelSurface ||
            kernel == kernelConstrainPrimaryVoxelSurface)
            fluidCS.SetBuffer(kernel, "previousPositions", previousPositionBuffer);
        // Both classification and lower-opening respawn write the age buffer.
        // Keep the binding on the respawn kernel as well; otherwise Unity aborts
        // the dispatch before voxel accumulation can run.
        if (kernel == kernelClassifyParticles || kernel == kernelSpawnFromExposedBulk ||
            kernel == kernelWakeBulkActivity || kernel == kernelSeedPrimaryVoxelSurface ||
            kernel == kernelConstrainPrimaryVoxelSurface)
            fluidCS.SetBuffer(kernel, "particleSurfaceAges", particleSurfaceAgeBuffer);
        if (kernel == kernelClassifyParticles || kernel == kernelWakeBulkActivity ||
            kernel == kernelSeedPrimaryVoxelSurface ||
            kernel == kernelConstrainPrimaryVoxelSurface)
            fluidCS.SetBuffer(kernel, "particleDormantSpeedAverages", particleDormantSpeedAverageBuffer);
        if (kernel == kernelPBFPredictActive || kernel == kernelPBFComputeLambda || kernel == kernelPBFComputeDelta || kernel == kernelPBFApplyDelta || kernel == kernelPBFUpdateVelocity)
        {
            fluidCS.SetBuffer(kernel, "pbfPreviousPositions", pbfPreviousPositionBuffer);
            fluidCS.SetBuffer(kernel, "pbfLambdas", pbfLambdaBuffer);
            fluidCS.SetBuffer(kernel, "pbfPositionDeltas", pbfPositionDeltaBuffer);
        }
        if (kernel == kernelPBFPredictActive || kernel == kernelPBFUpdateVelocity)
        {
            fluidCS.SetBuffer(kernel, "pbfPredictedPositions", pbfPredictedPositionBuffer);
            fluidCS.SetBuffer(kernel, "pbfPredictedVelocities", pbfPredictedVelocityBuffer);
        }
        fluidCS.SetBuffer(kernel, "cellHeadsRead", cellHeadsBuffer);
        fluidCS.SetBuffer(kernel, "nextIndexRead", nextIndexBuffer);
        if (kernel == kernelClearHash || kernel == kernelBuildHash)
            fluidCS.SetBuffer(kernel, "cellHeads", cellHeadsBuffer);
        if (kernel == kernelBuildHash)
            fluidCS.SetBuffer(kernel, "nextIndex", nextIndexBuffer);
        if (kernel == kernelForcesActive || kernel == kernelPBFPredictActive || kernel == kernelPBFUpdateVelocity)
            fluidCS.SetBuffer(kernel, "debugForceData", debugForceBuffer);
    }

    void EnsureBoundaryBuffers()
    {
        if (boundaryParticleBuffer == null)
        {
            BoundaryParticle[] disabled = new BoundaryParticle[1];
            disabled[0].pos = Vector3.zero;
            disabled[0].density = 0f;
            disabled[0].pressure = 0f;
            disabled[0].padding = Vector3.zero;
            boundaryParticleBuffer = new ComputeBuffer(1, BoundaryParticleStride);
            boundaryParticleBuffer.SetData(disabled);
        }

        if (boundaryCellHeadsBuffer == null || boundaryCellHeadsBuffer.count != totalCells)
        {
            boundaryCellHeadsBuffer?.Release();
            boundaryCellHeadsBuffer = new ComputeBuffer(totalCells, sizeof(int));
            int[] heads = new int[totalCells];
            for (int i = 0; i < heads.Length; i++) heads[i] = -1;
            boundaryCellHeadsBuffer.SetData(heads);
        }

        if (boundaryNextIndexBuffer == null)
        {
            boundaryNextIndexBuffer = new ComputeBuffer(1, sizeof(int));
            boundaryNextIndexBuffer.SetData(new[] { -1 });
        }
    }

    void EnsureFallbackSurfaceTileBuffer()
    {
        if (fallbackSurfaceTileBuffer != null)
            return;

        SurfaceTileDataGPU[] fallback = new SurfaceTileDataGPU[1];
        float inactiveHeight = boundsMin.y - 1000f;
        fallback[0].worldPos = new Vector3(
            (boundsMin.x + boundsMax.x) * 0.5f,
            inactiveHeight,
            (boundsMin.z + boundsMax.z) * 0.5f);
        fallback[0].height = inactiveHeight;
        fallback[0].active = 0;
        fallback[0].padding = Vector3.zero;

        fallbackSurfaceTileBuffer = new ComputeBuffer(1, SurfaceTileStride);
        fallbackSurfaceTileBuffer.SetData(fallback);
    }

    void EnsureBulkWaterBuffer(int requestedCells)
    {
        int count = Mathf.Max(requestedCells, 1);
        bool buffersMatch = bulkWaterBuffer != null && bulkWaterBuffer.count == count &&
            bulkWaterGridCountX == BulkGridCountX &&
            bulkWaterGridCountZ == BulkGridCountZ &&
            bulkWaterBuffer.stride == BulkWaterCellStride &&
            bulkWaterNextBuffer != null && bulkWaterNextBuffer.count == count &&
            bulkWaterNextBuffer.stride == BulkWaterCellStride &&
            bulkFluxBuffer != null && bulkFluxBuffer.count == count &&
            bulkFluxBuffer.stride == BulkFluxStride &&
            bulkReceiveScaleBuffer != null && bulkReceiveScaleBuffer.count == count &&
            bulkReceiveScaleBuffer.stride == sizeof(float) &&
            bulkFaceSillBuffer != null && bulkFaceSillBuffer.count == count &&
            bulkFaceSillBuffer.stride == BulkFluxStride &&
            bulkTokenDeltaBuffer != null && bulkTokenDeltaBuffer.count == count &&
            bulkTokenDeltaBuffer.stride == sizeof(int) &&
            bulkTokenFluxResidualBuffer != null && bulkTokenFluxResidualBuffer.count == count &&
            bulkTokenFluxResidualBuffer.stride == BulkFluxStride &&
            bulkTokenMoveQuotaBuffer != null && bulkTokenMoveQuotaBuffer.count == count * 4 &&
            bulkTokenMoveQuotaBuffer.stride == sizeof(int) &&
            bulkActivityBuffer != null && bulkActivityBuffer.count == count &&
            bulkActivityBuffer.stride == sizeof(uint);
        if (buffersMatch)
        {
            bulkWaterCellCount = count;
            if (bulkDiagnosticsCells == null || bulkDiagnosticsCells.Length != count)
                bulkDiagnosticsCells = new BulkWaterCellGPU[count];
            if (bulkDiagnosticsFlux == null || bulkDiagnosticsFlux.Length != count)
                bulkDiagnosticsFlux = new Vector4[count];
            if (bulkDiagnosticsReceiveScales == null || bulkDiagnosticsReceiveScales.Length != count)
                bulkDiagnosticsReceiveScales = new float[count];
            if (bulkDiagnosticsActivity == null || bulkDiagnosticsActivity.Length != count)
                bulkDiagnosticsActivity = new uint[count];
            return;
        }

        bulkWaterBuffer?.Release();
        bulkWaterNextBuffer?.Release();
        bulkFluxBuffer?.Release();
        bulkReceiveScaleBuffer?.Release();
        bulkFaceSillBuffer?.Release();
        bulkTokenDeltaBuffer?.Release();
        bulkTokenFluxResidualBuffer?.Release();
        bulkTokenMoveQuotaBuffer?.Release();
        bulkActivityBuffer?.Release();
        bulkWaterBuffer = new ComputeBuffer(count, BulkWaterCellStride);
        bulkWaterNextBuffer = new ComputeBuffer(count, BulkWaterCellStride);
        bulkFluxBuffer = new ComputeBuffer(count, BulkFluxStride);
        bulkReceiveScaleBuffer = new ComputeBuffer(count, sizeof(float));
        bulkFaceSillBuffer = new ComputeBuffer(count, BulkFluxStride);
        bulkTokenDeltaBuffer = new ComputeBuffer(count, sizeof(int));
        bulkTokenFluxResidualBuffer = new ComputeBuffer(count, BulkFluxStride);
        bulkTokenMoveQuotaBuffer = new ComputeBuffer(count * 4, sizeof(int));
        bulkActivityBuffer = new ComputeBuffer(count, sizeof(uint));
        BulkWaterCellGPU[] disabled = new BulkWaterCellGPU[count];
        for (int i = 0; i < disabled.Length; i++)
            disabled[i] = BulkWaterCellGPU.MakeDisabled(boundsMin.y - 1000f);

        bulkWaterBuffer.SetData(disabled);
        bulkWaterNextBuffer.SetData(disabled);
        bulkFluxBuffer.SetData(new Vector4[count]);
        float[] receiveScales = new float[count];
        Vector4[] openFaces = new Vector4[count];
        for (int i = 0; i < count; i++)
        {
            receiveScales[i] = 1f;
            openFaces[i] = new Vector4(boundsMin.y, boundsMin.y, boundsMin.y, boundsMin.y);
        }
        bulkReceiveScaleBuffer.SetData(receiveScales);
        bulkFaceSillBuffer.SetData(openFaces);
        bulkTokenDeltaBuffer.SetData(new int[count]);
        bulkTokenFluxResidualBuffer.SetData(new Vector4[count]);
        bulkTokenMoveQuotaBuffer.SetData(new int[count * 4]);
        bulkActivityBuffer.SetData(new uint[count]);
        bulkDiagnosticsCells = new BulkWaterCellGPU[count];
        bulkDiagnosticsFlux = new Vector4[count];
        bulkDiagnosticsReceiveScales = new float[count];
        bulkDiagnosticsActivity = new uint[count];
        bulkWaterCellCount = count;
        bulkWaterGridCountX = BulkGridCountX;
        bulkWaterGridCountZ = BulkGridCountZ;
        continuousBulkFieldInitialized = false;
        bulkTopologyDirty = true;
    }

    void EnsureBulkVoxelBuffer(int requestedTileCells)
    {
        int layers = usePrimaryVoxelVolume ? PrimaryVoxelLayerCount : Mathf.Max(bulkVoxelLayerCount, 1);
        int count = Mathf.Max(requestedTileCells, 1) * layers;
        if (bulkVoxelBuffer != null && bulkVoxelBuffer.count == count && bulkVoxelBuffer.stride == BulkVoxelCellStride)
        {
            bulkVoxelCellCount = count;
            return;
        }

        bulkVoxelBuffer?.Release();
        bulkVoxelBuffer = new ComputeBuffer(count, BulkVoxelCellStride);
        BulkVoxelCellGPU[] empty = new BulkVoxelCellGPU[count];
        bulkVoxelBuffer.SetData(empty);
        bulkVoxelCellCount = count;
    }

    void EnsurePrimaryVoxelBuffers()
    {
        int count = Mathf.Max(BulkGridCountX * BulkGridCountZ * PrimaryVoxelLayerCount, 1);
        // A released ComputeBuffer can still compare non-null in Unity, but
        // reading count/stride on it throws. Treat disposed handles as invalid
        // and recreate the complete set atomically.
        bool valid = false;
        try
        {
            valid = primaryVoxelBuffer != null && primaryVoxelBuffer.count == count &&
                primaryVoxelBuffer.stride == PrimaryVoxelCellStride &&
                primaryVoxelNextBuffer != null && primaryVoxelNextBuffer.count == count &&
                primaryVoxelNextBuffer.stride == PrimaryVoxelCellStride &&
                primaryVoxelPreviousFrameBuffer != null &&
                primaryVoxelPreviousFrameBuffer.count == count &&
                primaryVoxelPreviousFrameBuffer.stride == PrimaryVoxelCellStride &&
                primaryVoxelFluxBuffer != null && primaryVoxelFluxBuffer.count == count &&
                primaryVoxelFluxBuffer.stride == PrimaryVoxelFluxStride &&
                primaryVoxelReceiveScaleBuffer != null &&
                primaryVoxelReceiveScaleBuffer.count == count &&
                primaryVoxelReceiveScaleBuffer.stride == sizeof(float) &&
                primaryVoxelReservoirBoundaryBuffer != null &&
                primaryVoxelReservoirBoundaryBuffer.count == count &&
                primaryVoxelReservoirBoundaryBuffer.stride == PrimaryVoxelReservoirBoundaryStride &&
                primaryVoxelSolidBuffer != null && primaryVoxelSolidBuffer.count == count &&
                primaryVoxelFaceOpenBuffer != null && primaryVoxelFaceOpenBuffer.count == count &&
                primaryVoxelInflowBuffer != null && primaryVoxelInflowBuffer.count == 1 &&
                primaryVoxelInflowBuffer.stride == PrimaryVoxelInflowStride &&
                primaryVoxelDeferredInflowBuffer != null &&
                primaryVoxelDeferredInflowBuffer.count == 1 &&
                primaryVoxelDeferredInflowBuffer.stride == PrimaryVoxelDeferredInflowStride &&
                primaryVoxelDiagnosticsBuffer != null && primaryVoxelDiagnosticsBuffer.count == PrimaryVoxelDiagnosticsCount &&
                primaryVoxelDiagnosticsBuffer.stride == PrimaryVoxelDiagnosticsStride &&
                primaryVoxelHatchProbeBuffer != null && primaryVoxelHatchProbeBuffer.count == 1 &&
                primaryVoxelHatchProbeBuffer.stride == PrimaryVoxelHatchProbeStride &&
                primaryVoxelHatchDebugBuffer != null && primaryVoxelHatchDebugBuffer.count == 1 &&
                primaryVoxelHatchDebugBuffer.stride == PrimaryVoxelHatchDebugStride &&
                primaryVoxelBufferLayerCount == PrimaryVoxelLayerCount;
        }
        catch (System.Exception)
        {
            valid = false;
        }
        if (valid)
        {
            primaryVoxelCellCount = count;
            return;
        }

        if (primaryVoxelBuffer != null) primaryVoxelBuffer.Release();
        if (primaryVoxelNextBuffer != null) primaryVoxelNextBuffer.Release();
        if (primaryVoxelPreviousFrameBuffer != null) primaryVoxelPreviousFrameBuffer.Release();
        if (primaryVoxelFluxBuffer != null) primaryVoxelFluxBuffer.Release();
        if (primaryVoxelReceiveScaleBuffer != null) primaryVoxelReceiveScaleBuffer.Release();
        if (primaryVoxelReservoirBoundaryBuffer != null) primaryVoxelReservoirBoundaryBuffer.Release();
        if (primaryVoxelSolidBuffer != null) primaryVoxelSolidBuffer.Release();
        if (primaryVoxelFaceOpenBuffer != null) primaryVoxelFaceOpenBuffer.Release();
        if (primaryVoxelInflowBuffer != null) primaryVoxelInflowBuffer.Release();
        if (primaryVoxelDeferredInflowBuffer != null) primaryVoxelDeferredInflowBuffer.Release();
        if (primaryVoxelDiagnosticsBuffer != null) primaryVoxelDiagnosticsBuffer.Release();
        if (primaryVoxelHatchProbeBuffer != null) primaryVoxelHatchProbeBuffer.Release();
        if (primaryVoxelHatchDebugBuffer != null) primaryVoxelHatchDebugBuffer.Release();
        primaryVoxelBuffer = null;
        primaryVoxelNextBuffer = null;
        primaryVoxelPreviousFrameBuffer = null;
        primaryVoxelFluxBuffer = null;
        primaryVoxelReceiveScaleBuffer = null;
        primaryVoxelReservoirBoundaryBuffer = null;
        primaryVoxelSolidBuffer = null;
        primaryVoxelFaceOpenBuffer = null;
        primaryVoxelInflowBuffer = null;
        primaryVoxelDeferredInflowBuffer = null;
        primaryVoxelDiagnosticsBuffer = null;
        primaryVoxelHatchProbeBuffer = null;
        primaryVoxelHatchDebugBuffer = null;
        primaryVoxelBuffer = new ComputeBuffer(count, PrimaryVoxelCellStride);
        primaryVoxelNextBuffer = new ComputeBuffer(count, PrimaryVoxelCellStride);
        primaryVoxelPreviousFrameBuffer = new ComputeBuffer(count, PrimaryVoxelCellStride);
        primaryVoxelFluxBuffer = new ComputeBuffer(count, PrimaryVoxelFluxStride);
        primaryVoxelReceiveScaleBuffer = new ComputeBuffer(count, sizeof(float));
        primaryVoxelReservoirBoundaryBuffer = new ComputeBuffer(count, PrimaryVoxelReservoirBoundaryStride);
        primaryVoxelSolidBuffer = new ComputeBuffer(count, sizeof(uint));
        primaryVoxelFaceOpenBuffer = new ComputeBuffer(count, sizeof(uint));
        primaryVoxelInflowBuffer = new ComputeBuffer(1, PrimaryVoxelInflowStride);
        primaryVoxelDeferredInflowBuffer = new ComputeBuffer(1,
            PrimaryVoxelDeferredInflowStride);
        primaryVoxelDiagnosticsBuffer = new ComputeBuffer(PrimaryVoxelDiagnosticsCount, PrimaryVoxelDiagnosticsStride);
        primaryVoxelHatchProbeBuffer = new ComputeBuffer(1, PrimaryVoxelHatchProbeStride);
        primaryVoxelHatchDebugBuffer = new ComputeBuffer(1, PrimaryVoxelHatchDebugStride);
        primaryVoxelBuffer.SetData(new PrimaryVoxelCellGPU[count]);
        primaryVoxelNextBuffer.SetData(new PrimaryVoxelCellGPU[count]);
        primaryVoxelPreviousFrameBuffer.SetData(new PrimaryVoxelCellGPU[count]);
        primaryVoxelFluxBuffer.SetData(new PrimaryVoxelFaceFluxGPU[count]);
        float[] receiveScales = new float[count];
        for (int i = 0; i < receiveScales.Length; i++)
            receiveScales[i] = 1f;
        primaryVoxelReceiveScaleBuffer.SetData(receiveScales);
        primaryVoxelReservoirBoundaryBuffer.SetData(new Vector4[count]);
        primaryVoxelSolidBuffer.SetData(new uint[count]);
        primaryVoxelFaceOpenBuffer.SetData(new uint[count]);
        primaryVoxelInflowBuffer.SetData(new[] { PrimaryVoxelInflowGPU.Disabled });
        primaryVoxelDeferredInflowBuffer.SetData(new[] { Vector4.zero });
        primaryVoxelDiagnosticsBuffer.SetData(new uint[PrimaryVoxelDiagnosticsCount]);
        primaryVoxelHatchProbeBuffer.SetData(new[] { PrimaryVoxelHatchProbeGPU.Disabled });
        primaryVoxelHatchDebugBuffer.SetData(new[] { PrimaryVoxelHatchDebugGPU.Disabled });
        primaryVoxelCellCount = count;
        primaryVoxelBufferLayerCount = PrimaryVoxelLayerCount;
        primaryVoxelTopologyDirty = true;
        primaryVoxelInitialized = false;
    }

    int PrimarySurfaceWaveEntryCount => Mathf.Max(
        BulkGridCountX * BulkGridCountZ * Mathf.Clamp(primarySurfaceSlotsPerColumn, 1, 3), 1);

    void EnsurePrimarySurfaceWaveBuffers()
    {
        int count = PrimarySurfaceWaveEntryCount;
        bool valid = false;
        try
        {
            valid = primarySurfaceWaveBuffer != null && primarySurfaceWaveBuffer.count == count &&
                primarySurfaceWaveBuffer.stride == PrimarySurfaceWaveCellStride &&
                primarySurfaceWaveNextBuffer != null && primarySurfaceWaveNextBuffer.count == count &&
                primarySurfaceWaveNextBuffer.stride == PrimarySurfaceWaveCellStride &&
                primarySurfaceWaveFaceFluxBuffer != null && primarySurfaceWaveFaceFluxBuffer.count == count &&
                primarySurfaceWaveFaceFluxBuffer.stride == PrimarySurfaceWaveFaceFluxStride &&
                primarySurfaceWaveFaceFluxNextBuffer != null && primarySurfaceWaveFaceFluxNextBuffer.count == count &&
                primarySurfaceWaveFaceFluxNextBuffer.stride == PrimarySurfaceWaveFaceFluxStride &&
                primarySurfaceWaveDiagnosticsBuffer != null &&
                primarySurfaceWaveDiagnosticsBuffer.count == PrimarySurfaceWaveDiagnosticsCount &&
                primarySurfaceWaveMeanCorrectionBuffer != null &&
                primarySurfaceWaveMeanCorrectionBuffer.count == count &&
                primarySurfaceWaveMeanCorrectionBuffer.stride == PrimarySurfaceWaveMeanCorrectionStride;
        }
        catch (System.Exception)
        {
            valid = false;
        }
        if (valid)
        {
            primarySurfaceWaveEntryCount = count;
            return;
        }

        primarySurfaceWaveBuffer?.Release();
        primarySurfaceWaveNextBuffer?.Release();
        primarySurfaceWaveFaceFluxBuffer?.Release();
        primarySurfaceWaveFaceFluxNextBuffer?.Release();
        primarySurfaceWaveDiagnosticsBuffer?.Release();
        primarySurfaceWaveMeanCorrectionBuffer?.Release();
        primarySurfaceWaveFlickerEventBuffer?.Release();
        primarySurfaceWaveFlickerBuildStateBuffer?.Release();
        primarySurfaceWaveFlickerForcingStateBuffer?.Release();
        primarySurfaceWaveFlickerEventCountBuffer?.Release();
        primarySurfaceWaveBuffer = new ComputeBuffer(count, PrimarySurfaceWaveCellStride);
        primarySurfaceWaveNextBuffer = new ComputeBuffer(count, PrimarySurfaceWaveCellStride);
        primarySurfaceWaveFaceFluxBuffer = new ComputeBuffer(count, PrimarySurfaceWaveFaceFluxStride);
        primarySurfaceWaveFaceFluxNextBuffer = new ComputeBuffer(count, PrimarySurfaceWaveFaceFluxStride);
        primarySurfaceWaveDiagnosticsBuffer = new ComputeBuffer(
            PrimarySurfaceWaveDiagnosticsCount, sizeof(int));
        primarySurfaceWaveMeanCorrectionBuffer = new ComputeBuffer(count,
            PrimarySurfaceWaveMeanCorrectionStride);
        // SurfaceWaveCell is two float4 values. A zero flag means that the
        // topology kernel must seed this slot from the current voxel surface.
        Vector4[] emptyCells = new Vector4[count * 2];
        primarySurfaceWaveBuffer.SetData(emptyCells);
        primarySurfaceWaveNextBuffer.SetData(emptyCells);
        primarySurfaceWaveFaceFluxBuffer.SetData(new Vector4[count * 2]);
        primarySurfaceWaveFaceFluxNextBuffer.SetData(new Vector4[count * 2]);
        primarySurfaceWaveDiagnosticsBuffer.SetData(new int[PrimarySurfaceWaveDiagnosticsCount]);
        primarySurfaceWaveMeanCorrectionBuffer.SetData(new Vector2[count]);
        primarySurfaceWaveEntryCount = count;
        primarySurfaceWaveMeanReprojectionRequested = false;
        primarySurfaceWaveMeanReprojectionPending = false;
    }

    void EnsurePrimarySurfaceWaveFlickerDiagnosticBuffers()
    {
        int eventCapacity = Mathf.Clamp(primarySurfaceWaveFlickerDiagnosticEventCapacity, 256, 16384);
        int buildStateCount = Mathf.Max(primarySurfaceWaveEntryCount *
            PrimarySurfaceWaveFlickerBuildStateVectorCount, 1);
        bool eventValid = primarySurfaceWaveFlickerEventBuffer != null &&
            primarySurfaceWaveFlickerEventBuffer.count == eventCapacity &&
            primarySurfaceWaveFlickerEventBuffer.stride == PrimarySurfaceWaveFlickerEventStride &&
            primarySurfaceWaveFlickerBuildStateBuffer != null &&
            primarySurfaceWaveFlickerBuildStateBuffer.count == buildStateCount &&
            primarySurfaceWaveFlickerBuildStateBuffer.stride == sizeof(float) * 4 &&
            primarySurfaceWaveFlickerForcingStateBuffer != null &&
            primarySurfaceWaveFlickerForcingStateBuffer.count == primarySurfaceWaveEntryCount * 2 &&
            primarySurfaceWaveFlickerForcingStateBuffer.stride == sizeof(float) * 4 &&
            primarySurfaceWaveFlickerEventCountBuffer != null &&
            primarySurfaceWaveFlickerEventCountBuffer.count == 1;
        if (eventValid)
            return;

        primarySurfaceWaveFlickerEventBuffer?.Release();
        primarySurfaceWaveFlickerBuildStateBuffer?.Release();
        primarySurfaceWaveFlickerForcingStateBuffer?.Release();
        primarySurfaceWaveFlickerEventCountBuffer?.Release();
        primarySurfaceWaveFlickerEventBuffer = new ComputeBuffer(eventCapacity,
            PrimarySurfaceWaveFlickerEventStride, ComputeBufferType.Append);
        primarySurfaceWaveFlickerBuildStateBuffer = new ComputeBuffer(buildStateCount,
            sizeof(float) * 4);
        primarySurfaceWaveFlickerForcingStateBuffer = new ComputeBuffer(
            Mathf.Max(primarySurfaceWaveEntryCount * 2, 1), sizeof(float) * 4);
        primarySurfaceWaveFlickerEventCountBuffer = new ComputeBuffer(1, sizeof(uint),
            ComputeBufferType.Raw);
        primarySurfaceWaveFlickerEventBuffer.SetCounterValue(0);
        primarySurfaceWaveFlickerBuildStateBuffer.SetData(new Vector4[buildStateCount]);
        primarySurfaceWaveFlickerForcingStateBuffer.SetData(
            new Vector4[Mathf.Max(primarySurfaceWaveEntryCount * 2, 1)]);
        primarySurfaceWaveFlickerSequence = 0;
    }
    int PrimarySurfaceFlowCountX => Mathf.Max(BulkGridCountX *
        Mathf.Clamp(primarySurfaceFlowResolutionScale, 1, 4), 1);
    int PrimarySurfaceFlowCountZ => Mathf.Max(BulkGridCountZ *
        Mathf.Clamp(primarySurfaceFlowResolutionScale, 1, 4), 1);
    int PrimarySurfaceFlowEntryCount => Mathf.Max(PrimarySurfaceFlowCountX *
        PrimarySurfaceFlowCountZ * Mathf.Clamp(primarySurfaceSlotsPerColumn, 1, 3), 1);

    void EnsurePrimarySurfaceFlowBuffers()
    {
        int count = PrimarySurfaceFlowEntryCount;
        bool valid = false;
        try
        {
            valid = primarySurfaceFlowBuffer != null && primarySurfaceFlowBuffer.count == count &&
                primarySurfaceFlowBuffer.stride == PrimarySurfaceFlowCellStride &&
                primarySurfaceFlowNextBuffer != null && primarySurfaceFlowNextBuffer.count == count &&
                primarySurfaceFlowNextBuffer.stride == PrimarySurfaceFlowCellStride;
        }
        catch (System.Exception)
        {
            valid = false;
        }
        if (valid)
        {
            primarySurfaceFlowEntryCount = count;
            return;
        }

        primarySurfaceFlowBuffer?.Release();
        primarySurfaceFlowNextBuffer?.Release();
        primarySurfaceFlowBuffer = new ComputeBuffer(count, PrimarySurfaceFlowCellStride);
        primarySurfaceFlowNextBuffer = new ComputeBuffer(count, PrimarySurfaceFlowCellStride);
        Vector4[] empty = new Vector4[count * 2];
        primarySurfaceFlowBuffer.SetData(empty);
        primarySurfaceFlowNextBuffer.SetData(empty);
        primarySurfaceFlowEntryCount = count;
    }

    void BindPrimarySurfaceFlowBuffers()
    {
        EnsurePrimarySurfaceFlowBuffers();

        fluidCS.SetBuffer(kernelBuildPrimarySurfaceFlowState, "primaryVoxelCellsRead", primaryVoxelBuffer);
        fluidCS.SetBuffer(kernelBuildPrimarySurfaceFlowState, "primaryVoxelSolidRead", primaryVoxelSolidBuffer);
        fluidCS.SetBuffer(kernelBuildPrimarySurfaceFlowState, "primaryVoxelFaceFluxRead", primaryVoxelFluxBuffer);
        fluidCS.SetBuffer(kernelBuildPrimarySurfaceFlowState, "surfaceWaveCellsRead", primarySurfaceWaveBuffer);
        fluidCS.SetBuffer(kernelBuildPrimarySurfaceFlowState, "surfaceWaveFaceFluxRead", primarySurfaceWaveFaceFluxBuffer);
        fluidCS.SetBuffer(kernelBuildPrimarySurfaceFlowState, "surfaceFlowCellsRead", primarySurfaceFlowBuffer);
        fluidCS.SetBuffer(kernelBuildPrimarySurfaceFlowState, "surfaceFlowCellsWrite", primarySurfaceFlowNextBuffer);

        fluidCS.SetBuffer(kernelAdvectPrimarySurfaceFlow, "primaryVoxelSolidRead", primaryVoxelSolidBuffer);
        fluidCS.SetBuffer(kernelAdvectPrimarySurfaceFlow, "surfaceWaveCellsRead", primarySurfaceWaveBuffer);
        fluidCS.SetBuffer(kernelAdvectPrimarySurfaceFlow, "surfaceFlowCellsRead", primarySurfaceFlowBuffer);
        fluidCS.SetBuffer(kernelAdvectPrimarySurfaceFlow, "surfaceFlowCellsWrite", primarySurfaceFlowNextBuffer);

        fluidCS.SetBuffer(kernelPBFUpdateVelocity, "surfaceFlowCellsRead", primarySurfaceFlowBuffer);
        fluidCS.SetBuffer(kernelBuildPrimaryVoxelSurfaceGaussians, "surfaceFlowCellsRead",
            primarySurfaceFlowBuffer);
    }

    void SwapPrimarySurfaceFlowBuffers()
    {
        ComputeBuffer swap = primarySurfaceFlowBuffer;
        primarySurfaceFlowBuffer = primarySurfaceFlowNextBuffer;
        primarySurfaceFlowNextBuffer = swap;
        BindPrimarySurfaceFlowBuffers();
    }

    void RunPrimarySurfaceFlowStep(float stepDeltaTime)
    {
        if (!enablePrimarySurfaceFlowMap || !enablePrimarySurfaceWaves)
            return;

        BindPrimarySurfaceFlowBuffers();
        int groups = Mathf.CeilToInt(Mathf.Max(primarySurfaceFlowEntryCount, 1) / 256f);
        fluidCS.SetFloat("_DeltaTime", stepDeltaTime);
        fluidCS.Dispatch(kernelBuildPrimarySurfaceFlowState, Mathf.Max(groups, 1), 1, 1);
        SwapPrimarySurfaceFlowBuffers();

        float visualCellSize = Mathf.Min(
            Mathf.Max((boundsMax.x - boundsMin.x) / PrimarySurfaceFlowCountX, 0.0001f),
            Mathf.Max((boundsMax.z - boundsMin.z) / PrimarySurfaceFlowCountZ, 0.0001f));
        float maximumFlowSpeed = Mathf.Max(primaryVoxelMaxGridSpeed, 0.1f) +
            Mathf.Max(primarySurfaceFlowMaxResidualSpeed, 0.01f) +
            Mathf.Max(primarySurfaceWaveMaxSpeed, 0f);
        float stableDt = Mathf.Max(primarySurfaceFlowCfl, 0.1f) * visualCellSize /
            Mathf.Max(maximumFlowSpeed, 0.0001f);
        int substeps = Mathf.Clamp(Mathf.CeilToInt(stepDeltaTime / stableDt), 1,
            Mathf.Clamp(primarySurfaceFlowMaxSubsteps, 1, 8));
        float flowDt = stepDeltaTime / substeps;
        for (int i = 0; i < substeps; i++)
        {
            fluidCS.SetFloat("_DeltaTime", flowDt);
            fluidCS.Dispatch(kernelAdvectPrimarySurfaceFlow, Mathf.Max(groups, 1), 1, 1);
            SwapPrimarySurfaceFlowBuffers();
        }
    }
    void BindPrimarySurfaceWaveBuffers()
    {
        EnsurePrimarySurfaceWaveBuffers();
        EnsurePrimarySurfaceWaveFlickerDiagnosticBuffers();
        fluidCS.SetBuffer(kernelBuildPrimarySurfaceWaveState, "primaryVoxelCellsRead", primaryVoxelBuffer);
        fluidCS.SetBuffer(kernelBuildPrimarySurfaceWaveState, "primaryVoxelSolidRead", primaryVoxelSolidBuffer);
        fluidCS.SetBuffer(kernelBuildPrimarySurfaceWaveState, "surfaceWaveCellsRead", primarySurfaceWaveBuffer);
        fluidCS.SetBuffer(kernelBuildPrimarySurfaceWaveState, "surfaceWaveCellsWrite", primarySurfaceWaveNextBuffer);
        fluidCS.SetBuffer(kernelBuildPrimarySurfaceWaveState, "surfaceWaveDiagnostics", primarySurfaceWaveDiagnosticsBuffer);
        fluidCS.SetBuffer(kernelBuildPrimarySurfaceWaveState, "surfaceWaveMeanCorrection", primarySurfaceWaveMeanCorrectionBuffer);
        fluidCS.SetBuffer(kernelBuildPrimarySurfaceWaveState, "surfaceWaveFlickerBuildState", primarySurfaceWaveFlickerBuildStateBuffer);

        fluidCS.SetBuffer(kernelComputePrimarySurfaceWaveFlux, "primaryVoxelCellsRead", primaryVoxelBuffer);
        fluidCS.SetBuffer(kernelComputePrimarySurfaceWaveFlux, "primaryVoxelSolidRead", primaryVoxelSolidBuffer);
        fluidCS.SetBuffer(kernelComputePrimarySurfaceWaveFlux, "primaryVoxelFaceFluxRead", primaryVoxelFluxBuffer);
        fluidCS.SetBuffer(kernelComputePrimarySurfaceWaveFlux, "surfaceWaveCellsRead", primarySurfaceWaveBuffer);
        fluidCS.SetBuffer(kernelComputePrimarySurfaceWaveFlux, "surfaceWaveFaceFluxRead", primarySurfaceWaveFaceFluxBuffer);
        fluidCS.SetBuffer(kernelComputePrimarySurfaceWaveFlux, "surfaceWaveFaceFluxWrite", primarySurfaceWaveFaceFluxNextBuffer);
        fluidCS.SetBuffer(kernelComputePrimarySurfaceWaveFlux, "surfaceWaveDiagnostics", primarySurfaceWaveDiagnosticsBuffer);
        fluidCS.SetBuffer(kernelComputePrimarySurfaceWaveFlux, "surfaceWaveFlickerForcingState", primarySurfaceWaveFlickerForcingStateBuffer);
        fluidCS.SetBuffer(kernelComputePrimarySurfaceWaveFlux, "primaryVoxelCellsPreviousFrame", primaryVoxelPreviousFrameBuffer);

        fluidCS.SetBuffer(kernelUpdatePrimarySurfaceWave, "primaryVoxelCellsRead", primaryVoxelBuffer);
        fluidCS.SetBuffer(kernelUpdatePrimarySurfaceWave, "primaryVoxelSolidRead", primaryVoxelSolidBuffer);
        fluidCS.SetBuffer(kernelUpdatePrimarySurfaceWave, "primaryVoxelFaceFluxRead", primaryVoxelFluxBuffer);
        fluidCS.SetBuffer(kernelUpdatePrimarySurfaceWave, "surfaceWaveCellsRead", primarySurfaceWaveBuffer);
        fluidCS.SetBuffer(kernelUpdatePrimarySurfaceWave, "surfaceWaveCellsWrite", primarySurfaceWaveNextBuffer);
        fluidCS.SetBuffer(kernelUpdatePrimarySurfaceWave, "surfaceWaveFaceFluxRead", primarySurfaceWaveFaceFluxBuffer);
        fluidCS.SetBuffer(kernelUpdatePrimarySurfaceWave, "surfaceWaveDiagnostics", primarySurfaceWaveDiagnosticsBuffer);
        fluidCS.SetBuffer(kernelCollectPrimarySurfaceWaveFlickerDiagnostic, "primaryVoxelCellsRead", primaryVoxelBuffer);
        fluidCS.SetBuffer(kernelCollectPrimarySurfaceWaveFlickerDiagnostic, "primaryVoxelCellsPreviousFrame", primaryVoxelPreviousFrameBuffer);
        fluidCS.SetBuffer(kernelCollectPrimarySurfaceWaveFlickerDiagnostic, "primaryVoxelFaceFluxRead", primaryVoxelFluxBuffer);
        fluidCS.SetBuffer(kernelCollectPrimarySurfaceWaveFlickerDiagnostic, "surfaceWaveCellsRead", primarySurfaceWaveBuffer);
        fluidCS.SetBuffer(kernelCollectPrimarySurfaceWaveFlickerDiagnostic, "surfaceWaveFaceFluxRead", primarySurfaceWaveFaceFluxBuffer);
        fluidCS.SetBuffer(kernelCollectPrimarySurfaceWaveFlickerDiagnostic, "surfaceWaveMeanCorrection", primarySurfaceWaveMeanCorrectionBuffer);
        fluidCS.SetBuffer(kernelCollectPrimarySurfaceWaveFlickerDiagnostic, "surfaceWaveFlickerBuildState", primarySurfaceWaveFlickerBuildStateBuffer);
        fluidCS.SetBuffer(kernelCollectPrimarySurfaceWaveFlickerDiagnostic, "surfaceWaveFlickerForcingState", primarySurfaceWaveFlickerForcingStateBuffer);
        fluidCS.SetBuffer(kernelCollectPrimarySurfaceWaveFlickerDiagnostic, "surfaceWaveFlickerEvents", primarySurfaceWaveFlickerEventBuffer);

        // Surface consumers always receive a valid buffer. The shader gate
        // makes this a no-op when waves are disabled.
        fluidCS.SetBuffer(kernelPBFPredictActive, "surfaceWaveCellsRead", primarySurfaceWaveBuffer);
        fluidCS.SetBuffer(kernelPBFUpdateVelocity, "surfaceWaveCellsRead", primarySurfaceWaveBuffer);
        fluidCS.SetBuffer(kernelPBFUpdateVelocity, "surfaceWaveFaceFluxRead", primarySurfaceWaveFaceFluxBuffer);
        fluidCS.SetBuffer(kernelSeedPrimaryVoxelSurface, "surfaceWaveCellsRead", primarySurfaceWaveBuffer);
        fluidCS.SetBuffer(kernelConstrainPrimaryVoxelSurface, "surfaceWaveCellsRead", primarySurfaceWaveBuffer);
        fluidCS.SetBuffer(kernelConstrainPrimaryVoxelSurface, "surfaceWaveFaceFluxRead", primarySurfaceWaveFaceFluxBuffer);
        BindPrimarySurfaceFlowBuffers();
    }

    void QueuePrimarySurfaceWaveDiagnosticsReadback()
    {
        QueuePrimarySurfaceWaveBasinDiagnosticsReadback();
        if (primarySurfaceWaveDiagnosticsReadbackPending || primarySurfaceWaveDiagnosticsBuffer == null)
            return;
        // Diagnostics run on elapsed real time, not rendered frame count.
        primarySurfaceWaveDiagnosticsTimer += Time.unscaledDeltaTime;
        if (primarySurfaceWaveDiagnosticsTimer < 0.5f)
            return;
        primarySurfaceWaveDiagnosticsTimer = 0f;
        primarySurfaceWaveDiagnosticsReadbackPending = true;
        AsyncGPUReadback.Request(primarySurfaceWaveDiagnosticsBuffer, request =>
        {
            primarySurfaceWaveDiagnosticsReadbackPending = false;
            if (request.hasError || request.GetData<int>().Length < PrimarySurfaceWaveDiagnosticsCount)
                return;
            var data = request.GetData<int>();
            // Build resets the counters once per voxel step and Update adds
            // these conservative quantities once per wave substep. Normalise
            // the per-state values for Inspector and [Wave Details] output.
            int diagnosticSubsteps = Mathf.Max(primarySurfaceWaveLastSubstepCount, 1);
            primarySurfaceWaveActiveCellCount = data[0] / diagnosticSubsteps;
            primarySurfaceWaveSignedDisplacement = data[1] / 1000000f / diagnosticSubsteps;
            primarySurfaceWaveMaxSpeed = data[2] / 1000f;
            primarySurfaceWaveTopologyResets = data[3];
            if (enablePrimarySurfaceWaveBasinMeanReprojection && data[3] > 0)
                primarySurfaceWaveMeanReprojectionRequested = true;
            primarySurfaceWaveVisualClampHitCount = data[4];
            primarySurfaceWaveVisualClampExcessVolume = data[5] / 1000000f / diagnosticSubsteps;
            primarySurfaceWaveDeckSignedDisplacement = new Vector3(
                data[6] / 1000000f / diagnosticSubsteps,
                data[7] / 1000000f / diagnosticSubsteps,
                data[8] / 1000000f / diagnosticSubsteps);
            primarySurfaceWaveWallImpactImpulse = data[9] / 1000f;
            LogPrimarySurfaceWaveDetailsIfDue();
            if (primarySurfaceWaveSettlingTestRunning)
            {
                primarySurfaceWaveSettlingElapsed = Mathf.Max(
                    Time.realtimeSinceStartup - primarySurfaceWaveSettlingStartRealtime, 0f);
                if (primarySurfaceWaveSettlingWaitingForFirstSample)
                {
                    primarySurfaceWaveSettlingStartSpeed = primarySurfaceWaveMaxSpeed;
                    primarySurfaceWaveSettlingWaitingForFirstSample = false;
                }
                primarySurfaceWaveSettlingEndSpeed = primarySurfaceWaveMaxSpeed;
                if (primarySurfaceWaveSettlingElapsed >= primarySurfaceWaveSettlingTestDuration)
                {
                    primarySurfaceWaveSettlingTestRunning = false;
                    Debug.Log($"Surface-wave settle test: {primarySurfaceWaveSettlingStartSpeed:F3} m/s -> " +
                        $"{primarySurfaceWaveSettlingEndSpeed:F3} m/s in {primarySurfaceWaveSettlingElapsed:F2} s. " +
                        "Run this only after stopping breach/inflow so sustained forcing is excluded.");
                }
            }
        });
    }
    void QueuePrimarySurfaceWaveFlickerDiagnosticReadback()
    {
        if (!logPrimarySurfaceWaveFlickerDiagnostic || primarySurfaceWaveFlickerReadbackPending ||
            primarySurfaceWaveFlickerEventBuffer == null || primarySurfaceWaveFlickerEventCountBuffer == null)
            return;

        primarySurfaceWaveFlickerReadbackTimer += Time.unscaledDeltaTime;
        if (primarySurfaceWaveFlickerReadbackTimer <
            Mathf.Max(primarySurfaceWaveFlickerDiagnosticReadbackInterval, 0.1f))
            return;

        primarySurfaceWaveFlickerReadbackTimer = 0f;
        primarySurfaceWaveFlickerReadbackPending = true;
        ComputeBuffer.CopyCount(primarySurfaceWaveFlickerEventBuffer,
            primarySurfaceWaveFlickerEventCountBuffer, 0);
        AsyncGPUReadback.Request(primarySurfaceWaveFlickerEventCountBuffer, countRequest =>
        {
            if (countRequest.hasError || countRequest.GetData<uint>().Length == 0)
            {
                primarySurfaceWaveFlickerReadbackPending = false;
                return;
            }

            int eventCount = Mathf.Clamp((int)countRequest.GetData<uint>()[0], 0,
                Mathf.Clamp(primarySurfaceWaveFlickerDiagnosticEventCapacity, 256, 16384));
            if (eventCount == 0)
            {
                primarySurfaceWaveFlickerEventBuffer.SetCounterValue(0);
                primarySurfaceWaveFlickerReadbackPending = false;
                return;
            }

            AsyncGPUReadback.Request(primarySurfaceWaveFlickerEventBuffer, eventRequest =>
            {
                primarySurfaceWaveFlickerReadbackPending = false;
                if (!eventRequest.hasError)
                {
                    var records = eventRequest.GetData<PrimarySurfaceWaveFlickerEventGPU>();
                    int printed = Mathf.Min(eventCount,
                        Mathf.Clamp(primarySurfaceWaveFlickerMaxLogsPerReadback, 8, 512));
                    for (int i = 0; i < printed && i < records.Length; i++)
                        Debug.Log(FormatPrimarySurfaceWaveFlickerEvent(records[i]));
                    if (eventCount > printed)
                        Debug.Log($"[WaveFlickerDiagnostic] captured={eventCount} events; printed={printed}. " +
                            "Raise Max Logs Per Readback only when needed.");
                }
                primarySurfaceWaveFlickerEventBuffer.SetCounterValue(0);
            });
        });
    }

    string FormatPrimarySurfaceWaveFlickerEvent(PrimarySurfaceWaveFlickerEventGPU e)
    {
        int x = Mathf.RoundToInt(e.identity.x);
        int z = Mathf.RoundToInt(e.identity.y);
        int slot = Mathf.RoundToInt(e.identity.z);
        int step = Mathf.RoundToInt(e.identity.w);
        string flags = $"reset={e.flags.x:F0} clamp={e.flags.y:F0} reproj={e.flags.z:F0} baseStep={e.flags.w:F0}";
        return $"[WaveFlickerDiagnostic] step={step} cell=({x},{z}) slot={slot} {flags} " +
            $"state(active/prev/same/layer)={e.state.x:F0}/{e.state.y:F0}/{e.state.z:F0}/{e.state.w:F0} " +
            $"base(prev/direct/render/fill)={e.baseAndFill.x:F3}/{e.baseAndFill.y:F3}/{e.baseAndFill.z:F3}/{e.baseAndFill.w:F3} " +
            $"eta(pre/mean/raw/visual)={e.eta.x:F4}/{e.eta.y:F4}/{e.eta.z:F4}/{e.eta.w:F4} " +
            $"correction(value/baseY/applied)={e.correction.x:F4}/{e.correction.y:F3}/{e.correction.z:F0} " +
            $"forcing(Px/Pz/Fx/Fz)={e.pressureFlux.x:F5}/{e.pressureFlux.y:F5}/{e.pressureFlux.z:F5}/{e.pressureFlux.w:F5} " +
            $"forcing(Vx/Vz/Hx/Hz)={e.columnHatch.x:F5}/{e.columnHatch.y:F5}/{e.columnHatch.z:F5}/{e.columnHatch.w:F5} " +
            $"q(+X/+Z/-X/-Z)={e.discharge.x:F4}/{e.discharge.y:F4}/{e.discharge.z:F4}/{e.discharge.w:F4} " +
            $"finalY(now/prev)={e.finalHeight.x:F3}/{e.finalHeight.y:F3}";
    }
    void LogPrimarySurfaceWaveDetailsIfDue()
    {
        if (!logPrimarySurfaceWaveDetails || !enablePrimarySurfaceWaves || !usePrimaryVoxelVolume)
            return;

        float now = Time.unscaledTime;
        if (now - primarySurfaceWaveDetailsLastLogTime <
            Mathf.Max(primarySurfaceWaveDetailsLogInterval, 0.5f))
            return;
        primarySurfaceWaveDetailsLastLogTime = now;

        Debug.Log($"[Wave Details] active={primarySurfaceWaveActiveCellCount} basins={primarySurfaceWaveBasinCount} " +
            $"H(mean/max)={primarySurfaceWaveMeanDepth:F3}/{primarySurfaceWaveMaxDepth:F3}m " +
            $"c(est)={primarySurfaceWaveEstimatedCelerity:F3}m/s q/H(max)={primarySurfaceWaveMaxSpeed:F3}m/s " +
            $"eta(sum/decks)={primarySurfaceWaveSignedDisplacement:F5}m3/" +
            $"({primarySurfaceWaveDeckSignedDisplacement.x:F5},{primarySurfaceWaveDeckSignedDisplacement.y:F5},{primarySurfaceWaveDeckSignedDisplacement.z:F5})m3 " +
            $"basin(abs/max)={primarySurfaceWaveLargestBasinAbsoluteDisplacement:F5}/{primarySurfaceWaveLargestBasinSignedDisplacement:F5}m3 " +
            $"clamp(hits/excess)={primarySurfaceWaveVisualClampHitCount}/{primarySurfaceWaveVisualClampExcessVolume:F5}m3 " +
            $"wall={primarySurfaceWaveWallImpactImpulse:F4} " +
            $"speedScale={primarySurfaceWaveTravelSpeedScale:F2} FrMax={primarySurfaceWaveMaxFroude:F2} reprojection(count/maxMean)={primarySurfaceWaveMeanReprojectionCount}/{primarySurfaceWaveLargestMeanReprojection:F5}m " +
            $"damping(effective)={primarySurfaceWaveEffectiveDamping:F3}/s " +
            $"Hmin={primarySurfaceWaveMinimumDepth:F3}m CFL={primarySurfaceWaveCfl:F2} " +
            $"substeps={primarySurfaceWaveLastSubstepCount} dt(stable/sub)={primarySurfaceWaveLastStableDt:F4}/{primarySurfaceWaveLastSubstepDt:F4}s " +
            $"forcing(P/F/V/H)={primarySurfaceWavePressureForcing:F2}/{primarySurfaceWaveFluxForcing:F2}/" +
            $"{primarySurfaceWaveColumnVelocityForcing:F2}/{primarySurfaceWaveHatchForcing:F2} " +
            $"flowMap={enablePrimarySurfaceFlowMap} flowScale={primarySurfaceFlowResolutionScale} flowPBF={primarySurfaceFlowPbfBlend:F2}");
    }

    void QueuePrimarySurfaceWaveBasinDiagnosticsReadback()
    {
        if (primarySurfaceWaveBasinReadbackPending || primarySurfaceWaveBuffer == null ||
            primarySurfaceWaveFaceFluxBuffer == null)
            return;

        primarySurfaceWaveBasinDiagnosticsTimer += Time.unscaledDeltaTime;
        if (primarySurfaceWaveBasinDiagnosticsTimer < 1f)
            return;
        primarySurfaceWaveBasinDiagnosticsTimer = 0f;
        primarySurfaceWaveBasinReadbackPending = true;

        int expectedCellVectors = primarySurfaceWaveEntryCount * 2;
        int expectedFaceVectors = primarySurfaceWaveEntryCount * 2;
        Vector4[] cellData = null;
        Vector4[] faceData = null;
        bool hasError = false;
        int remaining = 2;
        System.Action finish = () =>
        {
            remaining--;
            if (remaining != 0)
                return;
            primarySurfaceWaveBasinReadbackPending = false;
            if (!hasError && cellData != null && faceData != null &&
                cellData.Length == expectedCellVectors && faceData.Length == expectedFaceVectors)
                UpdatePrimarySurfaceWaveBasinDiagnostics(cellData, faceData);
        };

        AsyncGPUReadback.Request(primarySurfaceWaveBuffer, request =>
        {
            if (request.hasError)
                hasError = true;
            else
            {
                cellData = new Vector4[expectedCellVectors];
                request.GetData<Vector4>().CopyTo(cellData);
            }
            finish();
        });
        AsyncGPUReadback.Request(primarySurfaceWaveFaceFluxBuffer, request =>
        {
            if (request.hasError)
                hasError = true;
            else
            {
                faceData = new Vector4[expectedFaceVectors];
                request.GetData<Vector4>().CopyTo(faceData);
            }
            finish();
        });
    }

    void UpdatePrimarySurfaceWaveBasinDiagnostics(Vector4[] cellData, Vector4[] faceData)
    {
        int countX = BulkGridCountX;
        int countZ = BulkGridCountZ;
        int slotCount = Mathf.Clamp(primarySurfaceSlotsPerColumn, 1, 3);
        int layerArea = countX * countZ;
        int entryCount = layerArea * slotCount;
        if (cellData.Length < entryCount * 2 || faceData.Length < entryCount * 2)
            return;

        bool[] visited = new bool[entryCount];
        int[] queue = new int[entryCount];
        float cellArea = Mathf.Max((boundsMax.x - boundsMin.x) / countX, 0.0001f) *
            Mathf.Max((boundsMax.z - boundsMin.z) / countZ, 0.0001f);
        int basinCount = 0;
        int largestBasinCellCount = 0;
        float largestBasinSigned = 0f;
        float largestBasinAbsolute = 0f;
        float totalDepth = 0f;
        float maxDepth = 0f;
        int wetDepthCount = 0;
        // This analysis already arrives through asynchronous GPU readback. Reuse
        // its exact face connectivity so re-centering stays per basin and does
        // not add a synchronous GPU operation to the simulation.
        bool prepareMeanCorrection = enablePrimarySurfaceWaveBasinMeanReprojection &&
            primarySurfaceWaveMeanReprojectionRequested &&
            primarySurfaceWaveMeanCorrectionBuffer != null &&
            primarySurfaceWaveMeanCorrectionBuffer.count == entryCount;
        Vector2[] meanCorrections = prepareMeanCorrection ? new Vector2[entryCount] : null;
        int correctedBasinCount = 0;
        float largestMeanCorrection = 0f;
        for (int i = 0; i < entryCount; i++)
        {
            if (cellData[i * 2 + 1].w <= 0.5f)
                continue;
            float depth = Mathf.Max(cellData[i * 2].z, 0f);
            totalDepth += depth;
            maxDepth = Mathf.Max(maxDepth, depth);
            wetDepthCount++;
        }
        primarySurfaceWaveMeanDepth = wetDepthCount > 0 ? totalDepth / wetDepthCount : 0f;
        primarySurfaceWaveMaxDepth = maxDepth;
        primarySurfaceWaveEstimatedCelerity = Mathf.Sqrt(Mathf.Max(-gravity.y, 0.01f) *
            primarySurfaceWaveMeanDepth) * Mathf.Max(primarySurfaceWaveTravelSpeedScale, 0.01f);

        for (int start = 0; start < entryCount; start++)
        {
            if (visited[start] || cellData[start * 2 + 1].w <= 0.5f)
                continue;

            basinCount++;
            int head = 0;
            int tail = 0;
            queue[tail++] = start;
            visited[start] = true;
            int basinCellCount = 0;
            float basinSigned = 0f;

            while (head < tail)
            {
                int current = queue[head++];
                basinCellCount++;
                basinSigned += cellData[current * 2].x * cellArea;
                int slot = current / layerArea;
                int local = current - slot * layerArea;
                int z = local / countX;
                int x = local - z * countX;

                int plusX = current + 1;
                if (x + 1 < countX && faceData[current * 2 + 1].x > 0.5f &&
                    !visited[plusX] && cellData[plusX * 2 + 1].w > 0.5f)
                {
                    visited[plusX] = true;
                    queue[tail++] = plusX;
                }

                int minusX = current - 1;
                if (x > 0 && faceData[minusX * 2 + 1].x > 0.5f &&
                    !visited[minusX] && cellData[minusX * 2 + 1].w > 0.5f)
                {
                    visited[minusX] = true;
                    queue[tail++] = minusX;
                }

                int plusZ = current + countX;
                if (z + 1 < countZ && faceData[current * 2 + 1].y > 0.5f &&
                    !visited[plusZ] && cellData[plusZ * 2 + 1].w > 0.5f)
                {
                    visited[plusZ] = true;
                    queue[tail++] = plusZ;
                }

                int minusZ = current - countX;
                if (z > 0 && faceData[minusZ * 2 + 1].y > 0.5f &&
                    !visited[minusZ] && cellData[minusZ * 2 + 1].w > 0.5f)
                {
                    visited[minusZ] = true;
                    queue[tail++] = minusZ;
                }
            }

            if (prepareMeanCorrection && Mathf.Abs(basinSigned) >=
                Mathf.Max(primarySurfaceWaveMeanReprojectionVolumeThreshold, 0f))
            {
                float meanEta = basinSigned / Mathf.Max(basinCellCount * cellArea, 0.000001f);
                for (int componentIndex = 0; componentIndex < tail; componentIndex++)
                {
                    int cellIndex = queue[componentIndex];
                    // The saved base Y makes the later GPU application reject a
                    // stale correction if this slot changed surface/deck meanwhile.
                    meanCorrections[cellIndex] = new Vector2(meanEta,
                        cellData[cellIndex * 2].y);
                }
                correctedBasinCount++;
                largestMeanCorrection = Mathf.Max(largestMeanCorrection, Mathf.Abs(meanEta));
            }

            if (basinCellCount > largestBasinCellCount)
            {
                largestBasinCellCount = basinCellCount;
                largestBasinSigned = basinSigned;
            }
            largestBasinAbsolute = Mathf.Max(largestBasinAbsolute, Mathf.Abs(basinSigned));
        }

        if (prepareMeanCorrection)
        {
            primarySurfaceWaveMeanReprojectionRequested = false;
            primarySurfaceWaveLargestMeanReprojection = largestMeanCorrection;
            if (correctedBasinCount > 0)
            {
                primarySurfaceWaveMeanCorrectionBuffer.SetData(meanCorrections);
                primarySurfaceWaveMeanReprojectionPending = true;
            }
        }

        primarySurfaceWaveBasinCount = basinCount;
        primarySurfaceWaveLargestBasinSignedDisplacement = largestBasinSigned;
        primarySurfaceWaveLargestBasinAbsoluteDisplacement = largestBasinAbsolute;
    }
    [ContextMenu("Start Primary Surface Wave Settle Test")]
    public void StartPrimarySurfaceWaveSettleTest()
    {
        primarySurfaceWaveSettlingTestRunning = true;
        primarySurfaceWaveSettlingWaitingForFirstSample = true;
        primarySurfaceWaveSettlingStartRealtime = Time.realtimeSinceStartup;
        primarySurfaceWaveSettlingElapsed = 0f;
        primarySurfaceWaveSettlingStartSpeed = 0f;
        primarySurfaceWaveSettlingEndSpeed = 0f;
        Debug.Log("Surface-wave settle monitor armed. Stop breach/inflow, then let the configured duration elapse.");
    }
    void SwapPrimarySurfaceWaveFaceFluxBuffers()
    {
        ComputeBuffer swap = primarySurfaceWaveFaceFluxBuffer;
        primarySurfaceWaveFaceFluxBuffer = primarySurfaceWaveFaceFluxNextBuffer;
        primarySurfaceWaveFaceFluxNextBuffer = swap;
        BindPrimarySurfaceWaveBuffers();
    }
    void SwapPrimarySurfaceWaveBuffers()
    {
        ComputeBuffer swap = primarySurfaceWaveBuffer;
        primarySurfaceWaveBuffer = primarySurfaceWaveNextBuffer;
        primarySurfaceWaveNextBuffer = swap;
        BindPrimarySurfaceWaveBuffers();
    }

    void RunPrimarySurfaceWaveStep(float stepDeltaTime, int groupsSurfaceWaves)
    {
        if (!enablePrimarySurfaceWaves)
            return;

        BindPrimarySurfaceWaveBuffers();
        bool collectWaveFlicker = logPrimarySurfaceWaveFlickerDiagnostic &&
            !primarySurfaceWaveFlickerReadbackPending;
        fluidCS.SetInt("_EnablePrimarySurfaceWaveFlickerDiagnostic", collectWaveFlicker ? 1 : 0);
        if (collectWaveFlicker)
        {
            primarySurfaceWaveFlickerSequence++;
            fluidCS.SetInt("_PrimarySurfaceWaveFlickerDiagnosticSequence", primarySurfaceWaveFlickerSequence);
            fluidCS.SetFloat("_PrimarySurfaceWaveFlickerForcingEventThreshold",
                Mathf.Max(primarySurfaceWaveFlickerForcingEventThreshold, 0f));
            fluidCS.SetFloat("_PrimarySurfaceWaveFlickerBaseStepThreshold", 0.002f);
        }
        fluidCS.SetFloat("_DeltaTime", stepDeltaTime);
        bool applyMeanCorrection = primarySurfaceWaveMeanReprojectionPending;
        fluidCS.Dispatch(kernelBuildPrimarySurfaceWaveState, groupsSurfaceWaves, 1, 1);
        if (applyMeanCorrection)
        {
            primarySurfaceWaveMeanReprojectionPending = false;
            primarySurfaceWaveMeanReprojectionCount++;
        }
        SwapPrimarySurfaceWaveBuffers();

        float cellSize = Mathf.Min(
            Mathf.Max((boundsMax.x - boundsMin.x) / BulkGridCountX, 0.0001f),
            Mathf.Max((boundsMax.z - boundsMin.z) / BulkGridCountZ, 0.0001f));
        float maxDepth = Mathf.Max(PrimaryVoxelLayerCount * primaryVoxelHeight,
            primarySurfaceWaveMinimumDepth);
        float maximumWaveSpeed = Mathf.Max(primaryVoxelMaxGridSpeed, 0.1f) +
            Mathf.Sqrt(Mathf.Max(-gravity.y, 0.01f) * maxDepth) *
            Mathf.Max(primarySurfaceWaveTravelSpeedScale, 0.01f);
        float stableDt = Mathf.Max(primarySurfaceWaveCfl, 0.1f) * cellSize /
            Mathf.Max(maximumWaveSpeed, 0.0001f);
        int substeps = Mathf.Clamp(Mathf.CeilToInt(stepDeltaTime / stableDt), 1,
            Mathf.Clamp(primarySurfaceWaveMaxSubsteps, 1, 8));
        float waveDt = stepDeltaTime / substeps;
        primarySurfaceWaveLastStableDt = stableDt;
        primarySurfaceWaveLastSubstepCount = substeps;
        primarySurfaceWaveLastSubstepDt = waveDt;
        for (int i = 0; i < substeps; i++)
        {
            fluidCS.SetFloat("_DeltaTime", waveDt);
            fluidCS.Dispatch(kernelComputePrimarySurfaceWaveFlux, groupsSurfaceWaves, 1, 1);
            SwapPrimarySurfaceWaveFaceFluxBuffers();
            fluidCS.Dispatch(kernelUpdatePrimarySurfaceWave, groupsSurfaceWaves, 1, 1);
            SwapPrimarySurfaceWaveBuffers();
        }
        if (collectWaveFlicker)
        {
            fluidCS.Dispatch(kernelCollectPrimarySurfaceWaveFlickerDiagnostic,
                Mathf.Max(groupsSurfaceWaves, 1), 1, 1);
            QueuePrimarySurfaceWaveFlickerDiagnosticReadback();
        }
        QueuePrimarySurfaceWaveDiagnosticsReadback();
    }
    void EnsureMicroVoxelBuffers()
    {
        int scale = Mathf.Clamp(microVoxelScale, 1, 3);
        int countX = Mathf.Max(BulkGridCountX * scale, 1);
        int countZ = Mathf.Max(BulkGridCountZ * scale, 1);
        int layers = Mathf.Max(bulkVoxelLayerCount * scale, 1);
        int count = countX * countZ * layers;
        bool valid = microVoxelRawBuffer != null && microVoxelRawBuffer.count == count &&
            microVoxelRawBuffer.stride == MicroVoxelCellStride &&
            microVoxelDisplayBufferA != null && microVoxelDisplayBufferA.count == count &&
            microVoxelDisplayBufferA.stride == MicroVoxelCellStride &&
            microVoxelDisplayBufferB != null && microVoxelDisplayBufferB.count == count &&
            microVoxelDisplayBufferB.stride == MicroVoxelCellStride;
        if (valid)
        {
            microVoxelCellCount = count;
            return;
        }

        microVoxelRawBuffer?.Release();
        microVoxelDisplayBufferA?.Release();
        microVoxelDisplayBufferB?.Release();
        microVoxelRawBuffer = new ComputeBuffer(count, MicroVoxelCellStride);
        microVoxelDisplayBufferA = new ComputeBuffer(count, MicroVoxelCellStride);
        microVoxelDisplayBufferB = new ComputeBuffer(count, MicroVoxelCellStride);
        MicroVoxelCellGPU[] empty = new MicroVoxelCellGPU[count];
        microVoxelRawBuffer.SetData(empty);
        microVoxelDisplayBufferA.SetData(empty);
        microVoxelDisplayBufferB.SetData(empty);
        microVoxelCellCount = count;
        microDisplayPing = false;
    }

    void TryBindBoundaryBuffers()
    {
        if (boundaryBindingsAttempted || fluidCS == null)
            return;

        boundaryBindingsAttempted = true;
        EnsureBoundaryBuffers();

        try
        {
            fluidCS.SetBuffer(kernelDensityActive, "boundaryParticles", boundaryParticleBuffer);
            fluidCS.SetBuffer(kernelDensityActive, "boundaryCellHeads", boundaryCellHeadsBuffer);
            fluidCS.SetBuffer(kernelDensityActive, "boundaryNextIndex", boundaryNextIndexBuffer);
            fluidCS.SetBuffer(kernelForcesActive, "boundaryParticles", boundaryParticleBuffer);
            fluidCS.SetBuffer(kernelForcesActive, "boundaryCellHeads", boundaryCellHeadsBuffer);
            fluidCS.SetBuffer(kernelForcesActive, "boundaryNextIndex", boundaryNextIndexBuffer);
            fluidCS.SetInt("_NumBoundaryParticles", 0);
            fluidCS.SetFloat("_BoundaryDensityScale", 1f);
            fluidCS.SetFloat("_BoundaryPressureScale", 1f);
            boundaryBuffersSupported = true;
        }
        catch (System.Exception)
        {
            // Compute variant does not use boundary buffers.
            boundaryBuffersSupported = false;
        }
    }

    void EnsureSolidColliderBuffer()
    {
        if (solidColliderBuffer != null)
            return;

        solidColliderBuffer = new ComputeBuffer(1, SolidColliderStride);
        solidColliderBuffer.SetData(new[] { SolidColliderDataGPU.MakeDisabled() });
        solidColliderCount = 0;
    }

    void BindSolidColliderBuffer()
    {
        if (fluidCS == null)
            return;

        EnsureSolidColliderBuffer();
        fluidCS.SetBuffer(kernelIntegrateActive, "solidColliders", solidColliderBuffer);
        fluidCS.SetBuffer(kernelWakeDormantFromActive, "solidColliders", solidColliderBuffer);
        fluidCS.SetBuffer(kernelPBFPredictActive, "solidColliders", solidColliderBuffer);
        fluidCS.SetBuffer(kernelPBFApplyDelta, "solidColliders", solidColliderBuffer);
        fluidCS.SetBuffer(kernelUpdateDormant, "solidColliders", solidColliderBuffer);
        fluidCS.SetBuffer(kernelClassifyParticles, "solidColliders", solidColliderBuffer);
        fluidCS.SetBuffer(kernelBuildBulkTopology, "solidColliders", solidColliderBuffer);
        fluidCS.SetBuffer(kernelBuildPrimaryVoxelSolids, "solidColliders", solidColliderBuffer);
        // The topology diagnostic classifies each displayed voxel face with the
        // same collider-aware blockage test used by the primary voxel solver.
        fluidCS.SetBuffer(kernelCopyPrimaryVoxelDisplay, "solidColliders", solidColliderBuffer);
        // The breach uses the same face test as the conservative flux solver,
        // so it also needs collider geometry when checking its sealed local
        // receiver compartment.
        fluidCS.SetBuffer(kernelInjectPrimaryVoxelVolume, "solidColliders", solidColliderBuffer);
        // The geometry-aware primary-voxel passes test the physical midpoint of
        // every proposed face transfer.  Each dispatch therefore needs the same
        // collider buffer as the topology pass; missing these bindings makes
        // Unity abort the flow kernels at runtime.
        fluidCS.SetBuffer(kernelComputePrimaryVoxelFlux, "solidColliders", solidColliderBuffer);
        fluidCS.SetBuffer(kernelComputePrimaryVoxelReceiveScale, "solidColliders", solidColliderBuffer);
        fluidCS.SetBuffer(kernelApplyPrimaryVoxelFlux, "solidColliders", solidColliderBuffer);
        fluidCS.SetBuffer(kernelDebugPrimaryVoxelHatchProbe, "solidColliders", solidColliderBuffer);
        fluidCS.SetBuffer(kernelQueryPrimaryVoxelHatchDebug, "solidColliders", solidColliderBuffer);
        fluidCS.SetBuffer(kernelSeedPrimaryVoxelSurface, "solidColliders", solidColliderBuffer);
        fluidCS.SetBuffer(kernelConstrainPrimaryVoxelSurface, "solidColliders", solidColliderBuffer);
        fluidCS.SetBuffer(kernelPBFComputeLambda, "solidColliders", solidColliderBuffer);
        fluidCS.SetBuffer(kernelPBFComputeDelta, "solidColliders", solidColliderBuffer);
        fluidCS.SetBuffer(kernelPBFUpdateVelocity, "solidColliders", solidColliderBuffer);
        fluidCS.SetBuffer(kernelUpdatePrimaryVoxelUnderwaterSplats, "solidColliders", solidColliderBuffer);
        fluidCS.SetBuffer(kernelBuildPrimaryVoxelSurfaceGaussians, "solidColliders", solidColliderBuffer);
        // Surface-wave topology and flux both use the same voxel face-path
        // tests, which in turn query collider geometry for thin walls/decks.
        fluidCS.SetBuffer(kernelBuildPrimarySurfaceWaveState, "solidColliders", solidColliderBuffer);
        fluidCS.SetBuffer(kernelComputePrimarySurfaceWaveFlux, "solidColliders", solidColliderBuffer);
        fluidCS.SetBuffer(kernelUpdatePrimarySurfaceWave, "solidColliders", solidColliderBuffer);
        fluidCS.SetBuffer(kernelBuildPrimarySurfaceFlowState, "solidColliders", solidColliderBuffer);
        fluidCS.SetBuffer(kernelAdvectPrimarySurfaceFlow, "solidColliders", solidColliderBuffer);
        fluidCS.SetInt("_NumSolidColliders", solidColliderCount);
    }

    [ContextMenu("Rebuild Solid Collider Buffer")]
    public void RebuildSolidColliderBuffer()
    {
        List<Collider> colliders = CollectSolidColliders();
        primaryVoxelTopologyDirty = true;
        List<SolidColliderDataGPU> packed = new List<SolidColliderDataGPU>();

        for (int i = 0; i < colliders.Count; i++)
        {
            Collider col = colliders[i];
            if (col == null || !col.enabled || col.isTrigger)
                continue;

            if (col is BoxCollider box)
            {
                Vector3 absScale = AbsVector3(box.transform.lossyScale);
                Vector3 half = Vector3.Scale(box.size * 0.5f, absScale) + Vector3.one * solidColliderSkin;
                Vector3 center = box.transform.TransformPoint(box.center);
                packed.Add(SolidColliderDataGPU.FromObb(
                    center,
                    half,
                    box.transform.right.normalized,
                    box.transform.up.normalized,
                    box.transform.forward.normalized));
            }
            else
            {
                Bounds b = col.bounds;
                if (b.size.sqrMagnitude <= 0.0f)
                    continue;

                Vector3 half = b.extents + Vector3.one * solidColliderSkin;
                packed.Add(SolidColliderDataGPU.FromObb(
                    b.center,
                    half,
                    Vector3.right,
                    Vector3.up,
                    Vector3.forward));
            }
        }

        solidColliderCount = packed.Count;
        int bufferCount = Mathf.Max(1, solidColliderCount);
        if (solidColliderBuffer == null || solidColliderBuffer.count != bufferCount)
        {
            solidColliderBuffer?.Release();
            solidColliderBuffer = new ComputeBuffer(bufferCount, SolidColliderStride);
        }

        if (solidColliderCount == 0)
        {
            solidColliderBuffer.SetData(new[] { SolidColliderDataGPU.MakeDisabled() });
        }
        else
        {
            solidColliderBuffer.SetData(packed.ToArray());
        }

        if (fluidCS != null && particleBuffer != null)
            BindSolidColliderBuffer();

        // The continuous field must not reuse openings derived from stale
        // collider geometry. The inexpensive cell topology pass will run once
        // at the next simulation step (or each rebuild when explicitly asked).
        bulkTopologyDirty = true;
    }

    List<Collider> CollectSolidColliders()
    {
        List<Collider> colliders = new List<Collider>();

        if (explicitSolidColliders != null)
        {
            for (int i = 0; i < explicitSolidColliders.Length; i++)
            {
                Collider col = explicitSolidColliders[i];
                if (col != null && !colliders.Contains(col))
                    colliders.Add(col);
            }
        }

        if (!useTaggedShipHullColliders || string.IsNullOrWhiteSpace(shipHullColliderTag))
            return colliders;

        try
        {
            GameObject[] taggedObjects = GameObject.FindGameObjectsWithTag(shipHullColliderTag);
            for (int i = 0; i < taggedObjects.Length; i++)
            {
                Collider[] objectColliders = taggedObjects[i].GetComponentsInChildren<Collider>(true);
                for (int j = 0; j < objectColliders.Length; j++)
                {
                    Collider col = objectColliders[j];
                    if (col != null && !colliders.Contains(col))
                        colliders.Add(col);
                }
            }
        }
        catch (UnityException)
        {
            // Tag not defined yet, ignore and rely on explicit colliders.
        }

        return colliders;
    }

    public void SetBoundaryBuffers(ComputeBuffer particles,
                                   ComputeBuffer cellHeads,
                                   ComputeBuffer nextIndex,
                                   int count)
    {
        Debug.LogWarning("FluidSimulator: boundary-particle collisions are disabled. Use solid colliders instead.");
    }

    static Vector3 AbsVector3(Vector3 v)
    {
        return new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    }

    float GetFixedBulkActiveSkinDepth()
    {
        float particleDiameter = Mathf.Max(particleRadius * 2f, 0.0001f);
        // The hybrid's expensive system is a surface skin, not a second
        // deep-water body. A zero override means the existing 1-3 layer
        // setting is authoritative; a positive override remains available
        // for deliberately thicker close-up simulations.
        float requestedDepth = fixedActivePbfDepthMeters > 0.0001f
            ? fixedActivePbfDepthMeters
            : Mathf.Clamp(sphSurfaceLayers, 1, 3) * particleDiameter;
        return Mathf.Clamp(requestedDepth, particleDiameter, particleDiameter * 3f);
    }

    void ConfigureFixedBulkVoxelPresentation(SurfaceTileRenderer renderer)
    {
        if (renderer == null)
            return;

        bool fixedBandsActive = (useFixedBulkBands && solverMode == FluidSolverMode.PBF_XPBD) ||
            usePrimaryVoxelVolume;
        // Active PBF particles already own the smooth liquid surface. In this
        // diagnostic the cube field therefore shows only stored bulk tokens.
        renderer.renderCoarseBulkVoxelField = fixedBandsActive && showFixedBulkVoxelField;
        renderer.voxelShowBulkOnly = fixedBandsActive;
        // Stored volume already has a per-cell support height. A global clip
        // hid valid bulk below locally lower surfaces and made the lower body
        // appear empty, so render the real filled cell columns instead.
        renderer.voxelUseTopClip = false;
        renderer.voxelClipTopY = boundsMax.y;
    }

    void WireRuntimeReferences()
    {
        // Tell the tile renderer which buffers the fluid sim owns so it can
        // derive a visible water surface from the current particles.
        SurfaceTileRenderer activeSurfaceTileRenderer = disableSurfaceTileRendererForProfiling ? null : surfaceTileRenderer;
        if (activeSurfaceTileRenderer != null)
        {
            activeSurfaceTileRenderer.InitBuffers(
                particleBuffer,
                cellHeadsBuffer,
                nextIndexBuffer,
                cellSize,
                particleRadius * 2f,
                gridResolution,
                bulkWaterBuffer,
                BulkGridCountX,
                BulkGridCountZ
            );
            activeSurfaceTileRenderer.sprayParticleBuffer = sprayParticleBuffer;
            activeSurfaceTileRenderer.sprayParticleCapacity = SprayParticleCount;
            activeSurfaceTileRenderer.bulkVoxelBuffer = bulkVoxelBuffer;
            activeSurfaceTileRenderer.bulkVoxelGridCountX = BulkGridCountX;
            activeSurfaceTileRenderer.bulkVoxelGridCountZ = BulkGridCountZ;
            activeSurfaceTileRenderer.bulkVoxelLayerCount = usePrimaryVoxelVolume
                ? PrimaryVoxelLayerCount : Mathf.Max(bulkVoxelLayerCount, 1);
            activeSurfaceTileRenderer.bulkVoxelHeight = Mathf.Max(usePrimaryVoxelVolume
                ? primaryVoxelHeight
                : (bulkVoxelHeight > 0f ? bulkVoxelHeight : particleRadius * 2f), 0.0001f);
            activeSurfaceTileRenderer.primaryVoxelFlowBuffer = usePrimaryVoxelVolume ? primaryVoxelBuffer : null;
            activeSurfaceTileRenderer.primarySurfaceWaveBuffer =
                usePrimaryVoxelVolume && enablePrimarySurfaceWaves ? primarySurfaceWaveBuffer : null;
            activeSurfaceTileRenderer.primaryVoxelSolidBuffer = usePrimaryVoxelVolume ? primaryVoxelSolidBuffer : null;
            activeSurfaceTileRenderer.primaryVoxelFaceOpenBuffer = usePrimaryVoxelVolume ? primaryVoxelFaceOpenBuffer : null;
            activeSurfaceTileRenderer.primaryVoxelFaceFluxBuffer = usePrimaryVoxelVolume ? primaryVoxelFluxBuffer : null;
            activeSurfaceTileRenderer.primaryVoxelFlowLayerCount = usePrimaryVoxelVolume ? PrimaryVoxelLayerCount : 1;
            activeSurfaceTileRenderer.primaryVoxelFlowHeight = usePrimaryVoxelVolume
                ? Mathf.Max(primaryVoxelHeight, 0.05f) : 1f;
            activeSurfaceTileRenderer.primarySurfaceWaveMaxDisplacement =
                Mathf.Max(primarySurfaceWaveMaxDisplacement, 0.001f);
            activeSurfaceTileRenderer.primarySurfaceWaveVisualResponse =
                Mathf.Max(primarySurfaceWaveVisualResponse, 0.01f);
            activeSurfaceTileRenderer.primarySurfaceWaveVisualMaximumVerticalSpeed =
                Mathf.Max(primarySurfaceWaveVisualMaximumVerticalSpeed, 0.01f);
            activeSurfaceTileRenderer.primarySurfaceWaveVisualSpatialSmoothing =
                Mathf.Clamp01(primarySurfaceWaveVisualSpatialSmoothing);
            activeSurfaceTileRenderer.usePrimarySurfaceWaveArtificialRippleLifecycle =
                usePrimarySurfaceWaveArtificialRippleLifecycle;
            activeSurfaceTileRenderer.primarySurfaceWaveVisualRippleDuration =
                Mathf.Max(primarySurfaceWaveVisualRippleDuration, 0.01f);
            activeSurfaceTileRenderer.primarySurfaceWaveVisualRippleMaximumAmplitude =
                Mathf.Max(primarySurfaceWaveVisualRippleMaximumAmplitude, 0.001f);
            activeSurfaceTileRenderer.primarySurfaceWaveVisualRippleActivationAmplitude =
                Mathf.Max(primarySurfaceWaveVisualRippleActivationAmplitude, 0.0001f);
            activeSurfaceTileRenderer.primaryVoxelSurfaceSlotCount =
                Mathf.Clamp(primarySurfaceSlotsPerColumn, 1, 3);
            activeSurfaceTileRenderer.primaryVoxelSurfaceMinimumGapLayers =
                Mathf.Clamp(primarySurfaceMinimumVerticalGapLayers, 1, 4);
            ConfigureFixedBulkVoxelPresentation(activeSurfaceTileRenderer);
            activeSurfaceTileRenderer.microVoxelBuffer = microVoxelDisplayBufferA;
            activeSurfaceTileRenderer.microVoxelRawBuffer = microVoxelRawBuffer;
            activeSurfaceTileRenderer.microVoxelGridCountX = Mathf.Max(BulkGridCountX * Mathf.Clamp(microVoxelScale, 1, 3), 1);
            activeSurfaceTileRenderer.microVoxelGridCountZ = Mathf.Max(BulkGridCountZ * Mathf.Clamp(microVoxelScale, 1, 3), 1);
            activeSurfaceTileRenderer.microVoxelLayerCount = Mathf.Max(bulkVoxelLayerCount * Mathf.Clamp(microVoxelScale, 1, 3), 1);
            activeSurfaceTileRenderer.microVoxelHeight = Mathf.Max(
                (bulkVoxelHeight > 0f ? bulkVoxelHeight : particleRadius * 2f) /
                Mathf.Clamp(microVoxelScale, 1, 3), 0.0001f);
            activeSurfaceTileRenderer.microVolumeRaymarchSteps = Mathf.Clamp(microVoxelRaymarchSteps, 8, 128);
        }

        // The particle renderer is only a visual debug view of raw particles.
        if (gpuRenderer != null)
        {
            gpuRenderer.particleBuffer = particleBuffer;
            gpuRenderer.particleRadius = particleRadius;
            gpuRenderer.smoothingRadius = particleRadius * 2f;
            gpuRenderer.boundsMin = boundsMin;
            gpuRenderer.boundsMax = boundsMax;
            gpuRenderer.mainBodySurfaceY = activeSurfaceTileRenderer != null
                ? activeSurfaceTileRenderer.mainBodySurfaceLevel
                : boundsMin.y;
        }
    }

    void UpdatePrimaryVoxelBreachControls()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !enablePrimaryVoxelBreachKeyboardControls)
            return;

        bool closeRequested = keyboard.digit0Key.wasPressedThisFrame ||
            keyboard.numpad0Key.wasPressedThisFrame;
        bool openRequested = keyboard.digit1Key.wasPressedThisFrame ||
            keyboard.numpad1Key.wasPressedThisFrame;
        if (closeRequested && primaryVoxelBreachOpen)
        {
            primaryVoxelBreachOpen = false;
            Debug.Log("[Fluid] Breach reservoir closed (0): inflow is disabled; voxel, draft, and wave settling remain live.");
        }
        else if (openRequested && !primaryVoxelBreachOpen)
        {
            primaryVoxelBreachOpen = true;
            Debug.Log("[Fluid] Breach reservoir reopened (1): physical orifice inflow resumed.");
        }
    }

    void UpdatePrimaryVoxelHatchProbeControls()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.tKey.wasPressedThisFrame || !usePrimaryVoxelVolume || !enablePrimaryVoxelHatchProbe) return;
        BoxCollider hatch = primaryVoxelHatchProbeVolume;
        if (hatch == null && primaryVoxelHatchRiserVolumes != null)
            for (int i = 0; i < primaryVoxelHatchRiserVolumes.Length; i++) if (primaryVoxelHatchRiserVolumes[i] != null && primaryVoxelHatchRiserVolumes[i].enabled) { hatch = primaryVoxelHatchRiserVolumes[i]; break; }
        if (hatch == null) { ShipSectionBuilder builder = FindFirstObjectByType<ShipSectionBuilder>(); if (builder != null && builder.verticalRiserVolumes != null) for (int i = 0; i < builder.verticalRiserVolumes.Length; i++) if (builder.verticalRiserVolumes[i] != null && builder.verticalRiserVolumes[i].enabled) { hatch = builder.verticalRiserVolumes[i]; break; } }
        if (hatch == null) { Debug.LogWarning("[Fluid] Hatch probe needs an active hatch trigger."); return; }
        Bounds b = hatch.bounds; float h = Mathf.Max(primaryVoxelHeight, .05f);
        fluidCS.SetInt("_PrimaryVoxelHatchProbeCommand", 1);
        fluidCS.SetVector("_PrimaryVoxelHatchProbeLaunchPositionAndAmount", new Vector4(b.center.x, b.min.y - h * .25f, b.center.z, Mathf.Clamp(primaryVoxelHatchProbeFill, .05f, 1f)));
        fluidCS.SetFloat("_PrimaryVoxelHatchProbeLifetime", Mathf.Clamp(primaryVoxelHatchProbeLifetime, .5f, 10f));
        fluidCS.SetFloat("_PrimaryVoxelHatchProbeUpwardSpeed", Mathf.Max(primaryVoxelHatchProbeUpwardSpeed, .1f));
        fluidCS.Dispatch(kernelDebugPrimaryVoxelHatchProbe, 1, 1, 1);
        Debug.Log("[Fluid] Hatch probe fired through " + hatch.name + ".");
    }

    void DispatchPrimaryVoxelHatchProbeAdvance(float stepDeltaTime)
    {
        if (!enablePrimaryVoxelHatchProbe || primaryVoxelHatchProbeBuffer == null) return;
        fluidCS.SetInt("_PrimaryVoxelHatchProbeCommand", 2);
        fluidCS.SetFloat("_DeltaTime", stepDeltaTime);
        fluidCS.SetFloat("_PrimaryVoxelHatchProbeLifetime", Mathf.Clamp(primaryVoxelHatchProbeLifetime, .5f, 10f));
        fluidCS.SetFloat("_PrimaryVoxelHatchProbeUpwardSpeed", Mathf.Max(primaryVoxelHatchProbeUpwardSpeed, .1f));
        fluidCS.Dispatch(kernelDebugPrimaryVoxelHatchProbe, 1, 1, 1);
    }
    void Update()
    {
        if (fluidCS == null || particleBuffer == null || cellHeadsBuffer == null || nextIndexBuffer == null)
            return;

        if (autoRebuildSolidCollidersEachFrame)
            RebuildSolidColliderBuffer();

        // The ship remains spatially fixed during this prototype pass. Raising
        // the sea relative to that fixed reference produces the same hydraulic
        // head as constrained vertical sinking, without moving colliders under
        // the authoritative voxel grid.
        UpdatePrimaryVoxelFloodDraftCoupling();

        UpdatePrimaryVoxelBreachControls();
        float stepDeltaTime = 1f / Mathf.Max(simulationRateHz, 1f);
        int playbackStepBudget = Mathf.Clamp(NormalSimulationStepsPerFrame, 1,
            Mathf.Max(maxSimulationStepsPerFrame, 1));
        // Fast-forward advances more ordinary fixed steps; it never enlarges
        // DeltaTime, so the voxel/PBF stability settings remain unchanged.
        simulationAccumulator += Time.deltaTime;

        int stepsThisFrame = 0;
        while (simulationAccumulator >= stepDeltaTime && stepsThisFrame < playbackStepBudget)
        {
            RunSimulationStep(stepDeltaTime);
            simulationAccumulator -= stepDeltaTime;
            stepsThisFrame++;
        }

        // Avoid a permanent catch-up spiral if the requested Hz is too expensive.
        if (stepsThisFrame >= playbackStepBudget && simulationAccumulator >= stepDeltaTime)
            simulationAccumulator = Mathf.Min(simulationAccumulator, stepDeltaTime);

        lastSimulationStepsThisFrame = stepsThisFrame;
        currentSimulationDeltaTime = stepDeltaTime;

        // Update the tile surface after the particles have moved so the visible
        // water surface reflects the latest simulation state this frame.
        SurfaceTileRenderer activeSurfaceTileRenderer = disableSurfaceTileRendererForProfiling ? null : surfaceTileRenderer;
        if (activeSurfaceTileRenderer != null)
        {
            // Primary-volume transport ping-pongs its authoritative buffer on
            // every flux pass. The reference installed before a simulation
            // step can therefore be the penultimate buffer after an odd number
            // of passes. Publish the final owner immediately before the render
            // surface samples fill/velocity so it never chases stale voxels.
            activeSurfaceTileRenderer.primaryVoxelFlowBuffer = usePrimaryVoxelVolume
                ? primaryVoxelBuffer : null;
            activeSurfaceTileRenderer.primarySurfaceWaveBuffer =
                usePrimaryVoxelVolume && enablePrimarySurfaceWaves ? primarySurfaceWaveBuffer : null;
            activeSurfaceTileRenderer.primaryVoxelSolidBuffer = usePrimaryVoxelVolume
                ? primaryVoxelSolidBuffer : null;
            activeSurfaceTileRenderer.primaryVoxelFaceOpenBuffer = usePrimaryVoxelVolume
                ? primaryVoxelFaceOpenBuffer : null;
            activeSurfaceTileRenderer.primaryVoxelFaceFluxBuffer = usePrimaryVoxelVolume
                ? primaryVoxelFluxBuffer : null;
            // Surface tile extraction needs the reserved slot range; inactive
            // skin slots are explicitly filtered by particle state on GPU.
            activeSurfaceTileRenderer.numParticles = PrimarySurfaceParticleCount;
            ConfigureFixedBulkVoxelPresentation(activeSurfaceTileRenderer);            activeSurfaceTileRenderer.DispatchTiles();
        }

        UpdateAndDrawPrimaryVoxelCurrentSplats();

        // Keep the debug particle renderer in sync with the live particle count
        // and current radius settings while Play mode values are being tuned.
        if (gpuRenderer != null)
        {
            gpuRenderer.activeParticles = PrimarySurfaceParticleCount;
            gpuRenderer.particleRadius = particleRadius;
            gpuRenderer.smoothingRadius = particleRadius * 2f;
            gpuRenderer.boundsMin = boundsMin;
            gpuRenderer.boundsMax = boundsMax;
            gpuRenderer.mainBodySurfaceY = activeSurfaceTileRenderer != null
                ? activeSurfaceTileRenderer.mainBodySurfaceLevel
                : boundsMin.y;
        }

        UpdateRuntimeCounts();
        // Queues the compact primary diagnostics and its asynchronous flow snapshot.
        // This is rate-limited internally and never performs a synchronous GPU readback.
        UpdateVoxelVolumeReadback();
        QueuePrimaryVoxelFloodDraftReadback();
    }

    bool UsesContinuousBulkFlow()
    {
        return enableContinuousBulkFlow && useFixedBulkBands &&
            solverMode == FluidSolverMode.PBF_XPBD;
    }

    void SwapBulkWaterBuffers()
    {
        ComputeBuffer previous = bulkWaterBuffer;
        bulkWaterBuffer = bulkWaterNextBuffer;
        bulkWaterNextBuffer = previous;
    }

    void RunSimulationStep(float stepDeltaTime)
    {
        simulationStepCounter++;
        // Classification uses the previous rendered tile heights as the local
        // surface estimate above each particle.
        TryBindSurfaceTilesToFluid();

        if (usePrimaryVoxelVolume)
        {
            RunPrimaryVoxelVolumeStep(stepDeltaTime);
            RunSprayStep();
            return;
        }

        SpawnParticles(stepDeltaTime);

        shaderDeltaTime = stepDeltaTime;
        PushShaderParameters();

        int groupsParticles = Mathf.CeilToInt(Mathf.Max(activeParticles, 1) / 256f);
        int groupsCells = Mathf.CeilToInt(totalCells / 256f);
        int groupsBulkCells = Mathf.CeilToInt(Mathf.Max(bulkWaterCellCount, 1) / 256f);
        bool continuousBulkFlow = UsesContinuousBulkFlow();

        // Activity is a one-frame memory of motion, not a second volume
        // source. Decay it before classification; active PBF motion is
        // deposited after the solver for the next step.
        fluidCS.Dispatch(kernelDecayBulkActivity, groupsBulkCells, 1, 1);

        float topologyActiveDepth = GetFixedBulkActiveSkinDepth();
        float topologyCutoffY = Mathf.Clamp(
            fixedBulkSurfaceY - topologyActiveDepth,
            boundsMin.y, boundsMax.y);
        int topologySamples = Mathf.Clamp(bulkTopologyVerticalSamples, 2, 16);
        if (!bulkTopologySignatureValid ||
            lastBulkTopologyBoundsMin != boundsMin ||
            lastBulkTopologyBoundsMax != boundsMax ||
            !Mathf.Approximately(lastBulkTopologyCutoffY, topologyCutoffY) ||
            !Mathf.Approximately(lastBulkTopologyParticleRadius, particleRadius) ||
            lastBulkTopologyVerticalSamples != topologySamples ||
            lastBulkTopologyGridCountX != BulkGridCountX ||
            lastBulkTopologyGridCountZ != BulkGridCountZ)
        {
            bulkTopologyDirty = true;
            bulkTopologySignatureValid = true;
            lastBulkTopologyBoundsMin = boundsMin;
            lastBulkTopologyBoundsMax = boundsMax;
            lastBulkTopologyCutoffY = topologyCutoffY;
            lastBulkTopologyParticleRadius = particleRadius;
            lastBulkTopologyVerticalSamples = topologySamples;
            lastBulkTopologyGridCountX = BulkGridCountX;
            lastBulkTopologyGridCountZ = BulkGridCountZ;
        }

        if (continuousBulkFlow && bulkTopologyDirty)
        {
            // Topology is derived per coarse face, never per particle. The
            // pass scans the existing OBB collider buffer only when the grid
            // or scene collider set changes.
            fluidCS.Dispatch(kernelBuildBulkTopology, groupsBulkCells, 1, 1);
            bulkTopologyDirty = false;
        }
        else if (!continuousBulkFlow)
        {
            // Re-entering continuous mode must seed from the then-current
            // token set instead of trusting a field maintained by legacy mode.
            continuousBulkFieldInitialized = false;
        }

        fluidCS.Dispatch(kernelBuildBulkGrid, groupsBulkCells, 1, 1);
        // The first count pass also records the maximum active-particle height
        // per coarse bulk column. Later count passes leave spawnedThisStep free
        // for its normal respawn-reservation role.
        fluidCS.SetInt("_CollectBulkActiveSurface", 1);
        fluidCS.Dispatch(kernelCountBulkGrid, groupsParticles, 1, 1);
        fluidCS.SetInt("_CollectBulkActiveSurface", 0);
        if (continuousBulkFlow)
        {
            if (!continuousBulkFieldInitialized)
            {
                fluidCS.Dispatch(kernelSeedBulkVolumeFromTokenCounts, groupsBulkCells, 1, 1);
                fluidCS.Dispatch(kernelClearBulkTokenReconciliation, groupsBulkCells, 1, 1);
                continuousBulkFieldInitialized = true;
            }
            fluidCS.Dispatch(kernelClearBulkTokenDeltas, groupsBulkCells, 1, 1);
        }
        fluidCS.Dispatch(kernelClassifyParticles, groupsParticles, 1, 1);
        if (continuousBulkFlow)
            fluidCS.Dispatch(kernelApplyBulkTokenDelta, groupsBulkCells, 1, 1);
        fluidCS.Dispatch(kernelClearBulkGridCounts, groupsBulkCells, 1, 1);
        fluidCS.Dispatch(kernelCountBulkGrid, groupsParticles, 1, 1);
        if (!continuousBulkFlow)
        {
            fluidCS.Dispatch(kernelFinalizeBulkGrid, groupsBulkCells, 1, 1);
        }
        // Respawn vacancy checks need an up-to-date active-particle hash after
        // classification has converted any newly settled particles to bulk.
        fluidCS.Dispatch(kernelClearHash, groupsCells, 1, 1);
        fluidCS.Dispatch(kernelBuildHash, groupsParticles, 1, 1);
        fluidCS.Dispatch(kernelSpawnFromExposedBulk, groupsParticles, 1, 1);
        if (continuousBulkFlow)
            fluidCS.Dispatch(kernelApplyBulkTokenDelta, groupsBulkCells, 1, 1);
        // Recount after respawn for diagnostics and legacy volume rebuilding.
        fluidCS.Dispatch(kernelClearBulkGridCounts, groupsBulkCells, 1, 1);
        fluidCS.Dispatch(kernelCountBulkGrid, groupsParticles, 1, 1);
        if (continuousBulkFlow)
        {
            // Raw flux reads immutable A. Destination acceptance is computed
            // separately, then the gather writes B. Thus every accepted face
            // amount is subtracted and added exactly once without atomics.
            fluidCS.Dispatch(kernelComputeBulkFlux, groupsBulkCells, 1, 1);
            fluidCS.Dispatch(kernelComputeBulkReceiveScale, groupsBulkCells, 1, 1);
            fluidCS.Dispatch(kernelApplyBulkFlux, groupsBulkCells, 1, 1);
            fluidCS.Dispatch(kernelAccumulateBulkTokenFlux, groupsBulkCells, 1, 1);
            SwapBulkWaterBuffers();
            TryBindSurfaceTilesToFluid();
            fluidCS.Dispatch(kernelReconcileBulkTokens, groupsParticles, 1, 1);

            // Token counts are now diagnostic/bookkeeping only. Fractional
            // flux stays in per-face residuals until it becomes a whole-token
            // move quota; support and rendering read only finalized volume B.
            fluidCS.Dispatch(kernelClearBulkGridCounts, groupsBulkCells, 1, 1);
            fluidCS.Dispatch(kernelCountBulkGrid, groupsParticles, 1, 1);
        }
        else
        {
            fluidCS.Dispatch(kernelFinalizeBulkGrid, groupsBulkCells, 1, 1);
        }

        if (continuousBulkFlow && enableBulkActivityWake)
        {
            // Rehydrate only a bounded top cohort in cells whose activity was
            // raised by active PBF motion. The signed delta is applied before
            // prediction so an exposed lower surface is solved immediately.
            if (bulkWakeCounterBuffer != null)
                bulkWakeCounterBuffer.SetData(new[] { 0 });
            fluidCS.Dispatch(kernelClearBulkTokenDeltas, groupsBulkCells, 1, 1);
            fluidCS.Dispatch(kernelWakeBulkActivity, groupsParticles, 1, 1);
            fluidCS.Dispatch(kernelApplyBulkTokenDelta, groupsBulkCells, 1, 1);
            fluidCS.Dispatch(kernelClearBulkGridCounts, groupsBulkCells, 1, 1);
            fluidCS.Dispatch(kernelCountBulkGrid, groupsParticles, 1, 1);
        }

        if (solverMode == FluidSolverMode.PBF_XPBD)
        {
            // Wake before prediction so water revealed by a new doorway or
            // breach receives gravity, collision and a full PBF pass in this
            // same step rather than remaining frozen for one extra frame.
            fluidCS.Dispatch(kernelClearHash, groupsCells, 1, 1);
            fluidCS.Dispatch(kernelBuildHash, groupsParticles, 1, 1);
            fluidCS.Dispatch(kernelWakeDormantFromActive, groupsParticles, 1, 1);
            fluidCS.Dispatch(kernelPBFPredictActive, groupsParticles, 1, 1);

            for (int i = 0; i < Mathf.Max(pbfIterations, 1); i++)
            {
                fluidCS.Dispatch(kernelClearHash, groupsCells, 1, 1);
                fluidCS.Dispatch(kernelBuildHash, groupsParticles, 1, 1);
                fluidCS.Dispatch(kernelPBFComputeLambda, groupsParticles, 1, 1);
                fluidCS.Dispatch(kernelPBFComputeDelta, groupsParticles, 1, 1);
                fluidCS.Dispatch(kernelPBFApplyDelta, groupsParticles, 1, 1);
            }

            fluidCS.Dispatch(kernelClearHash, groupsCells, 1, 1);
            fluidCS.Dispatch(kernelBuildHash, groupsParticles, 1, 1);
            fluidCS.Dispatch(kernelPBFUpdateVelocity, groupsParticles, 1, 1);
        }
        else
        {
            fluidCS.Dispatch(kernelClearHash, groupsCells, 1, 1);
            fluidCS.Dispatch(kernelBuildHash, groupsParticles, 1, 1);
            fluidCS.Dispatch(kernelWakeDormantFromActive, groupsParticles, 1, 1);
            fluidCS.Dispatch(kernelDensityActive, groupsParticles, 1, 1);
            fluidCS.Dispatch(kernelForcesActive, groupsParticles, 1, 1);
            fluidCS.Dispatch(kernelIntegrateActive, groupsParticles, 1, 1);
        }

        fluidCS.Dispatch(kernelUpdateDormant, groupsParticles, 1, 1);
        if (continuousBulkFlow && enableBulkActivityWake)
            fluidCS.Dispatch(kernelAccumulateBulkActivity, groupsParticles, 1, 1);

        // Build a visualization-only voxel field after the particle step. The
        // field spans the full vertical bounds, combining active/dormant PBF
        // particles above the bulk interface with stored bulk tokens below it.
        int groupsVoxels = Mathf.CeilToInt(Mathf.Max(bulkVoxelCellCount, 1) / 256f);
        fluidCS.Dispatch(kernelClearVoxelField, groupsVoxels, 1, 1);
        fluidCS.Dispatch(kernelScatterParticleVoxels, groupsParticles, 1, 1);
        fluidCS.Dispatch(kernelFinalizeVoxelField, groupsVoxels, 1, 1);

        if (useMicroVoxelVolume)
        {
            EnsureMicroVoxelBuffers();
            BindMicroDisplayBuffers();
            int groupsMicroVoxels = Mathf.CeilToInt(Mathf.Max(microVoxelCellCount, 1) / 256f);
            fluidCS.Dispatch(kernelClearMicroVoxelField, groupsMicroVoxels, 1, 1);
            fluidCS.Dispatch(kernelScatterMicroVoxelParticles, groupsParticles, 1, 1);
            fluidCS.Dispatch(kernelFinalizeMicroVoxelField, groupsMicroVoxels, 1, 1);
            fluidCS.Dispatch(kernelSmoothMicroVoxelField, groupsMicroVoxels, 1, 1);
            microDisplayPing = !microDisplayPing;
        }

        RunSprayStep();
    }

    void RunPrimaryVoxelVolumeStep(float stepDeltaTime)
    {
        // The main voxel field is the only water-mass authority in this path.
        // No particle classification, token reconciliation, or legacy bulk
        // flux is dispatched here; PBF starts only after the voxel support has
        // been projected to the compatibility surface columns.
        EnsurePrimaryVoxelBuffers();
        EnsureBulkWaterBuffer(BulkGridCountX * BulkGridCountZ);
        EnsureBulkVoxelBuffer(BulkGridCountX * BulkGridCountZ);
        BindPrimaryVoxelBuffers();
        shaderDeltaTime = stepDeltaTime;
        PushShaderParameters();

        int groupsPrimary = Mathf.CeilToInt(Mathf.Max(primaryVoxelCellCount, 1) / 256f);
        int groupsColumns = Mathf.CeilToInt(Mathf.Max(bulkWaterCellCount, 1) / 256f);
        int particleCount = PrimarySurfaceParticleCount;
        int groupsParticles = Mathf.CeilToInt(Mathf.Max(particleCount, 1) / 256f);
        int groupsHash = Mathf.CeilToInt(totalCells / 256f);

        if (!primaryVoxelInitialized)
        {
            fluidCS.Dispatch(kernelClearPrimaryVoxelVolume, groupsPrimary, 1, 1);
            primaryVoxelInitialized = true;
            primaryVoxelTopologyDirty = true;
        }
        if (primaryVoxelTopologyDirty)
        {
            fluidCS.Dispatch(kernelBuildPrimaryVoxelSolids, groupsPrimary, 1, 1);
            primaryVoxelTopologyDirty = false;
        }

        // Snapshot the authoritative field once for the whole PBF step. FLIP
        // must compare whole-frame states, not adjacent voxel substeps.
        fluidCS.Dispatch(kernelCopyPrimaryVoxelFrameState, groupsPrimary, 1, 1);

        // Clear per-step counters while retaining cumulative accepted/rejected
        // inflow in slots 6 and 7.
        fluidCS.Dispatch(kernelClearPrimaryVoxelDiagnostics, 1, 1, 1);
        int voxelFlowSubsteps = Mathf.Clamp(primaryVoxelFlowSubsteps, 1, 8);
        float voxelFlowDeltaTime = stepDeltaTime / voxelFlowSubsteps;
        float wholeStepFlowFraction = Mathf.Clamp(primaryVoxelFlowFraction, 0.01f, 0.9f);
        float substepFlowFraction = 1f - Mathf.Pow(1f - wholeStepFlowFraction,
            1f / voxelFlowSubsteps);
        for (int flowStep = 0; flowStep < voxelFlowSubsteps; flowStep++)
        {
            fluidCS.SetFloat("_DeltaTime", voxelFlowDeltaTime);
            fluidCS.SetFloat("_PrimaryVoxelFlowFraction", substepFlowFraction);
            fluidCS.Dispatch(kernelClearPrimaryVoxelReservoirBoundary, groupsPrimary, 1, 1);
            QueuePrimaryVoxelInflow(voxelFlowDeltaTime);
            fluidCS.Dispatch(kernelInjectPrimaryVoxelVolume, 1, 1, 1);
            fluidCS.Dispatch(kernelComputePrimaryVoxelFlux, groupsPrimary, 1, 1);
            fluidCS.Dispatch(kernelComputePrimaryVoxelReceiveScale, groupsPrimary, 1, 1);
            fluidCS.Dispatch(kernelApplyPrimaryVoxelFlux, groupsPrimary, 1, 1);
            SwapPrimaryVoxelBuffers();
        }

        // The reservoir source belongs only to the true flow substeps above;
        // pressure and basin correction passes must not replay the last inlet.
        fluidCS.Dispatch(kernelClearPrimaryVoxelReservoirBoundary, groupsPrimary, 1, 1);

        // Two cheap, conservative pressure-only passes accelerate levelling
        // through actual side gaps.  The shader rejects vertical, blocked and
        // dry-segment faces here, so this cannot tunnel through a wall or add
        // a second free-surface overflow path.
        int pressureIterations = Mathf.Clamp(primaryVoxelPressureIterations, 0, 4);
        float pressureRelaxation = Mathf.Clamp(primaryVoxelPressureRelaxation, 0.01f, 0.25f);
        fluidCS.SetInt("_PrimaryVoxelPressureOnlyPass", 1);
        for (int pressurePass = 0; pressurePass < pressureIterations; pressurePass++)
        {
            fluidCS.SetFloat("_DeltaTime", voxelFlowDeltaTime);
            fluidCS.SetFloat("_PrimaryVoxelFlowFraction", pressureRelaxation);
            fluidCS.Dispatch(kernelComputePrimaryVoxelFlux, groupsPrimary, 1, 1);
            fluidCS.Dispatch(kernelComputePrimaryVoxelReceiveScale, groupsPrimary, 1, 1);
            fluidCS.Dispatch(kernelApplyPrimaryVoxelFlux, groupsPrimary, 1, 1);
            SwapPrimaryVoxelBuffers();
        }

        // Large rooms joined by small doorways need more than the two gentle
        // correction passes used by the open test pool. This pass uses the
        // same donor/receiver-limited face flux and the same segment-surface
        // head test, so it can only level water through already wet, genuinely
        // open side gaps. Decks, closed bulkheads and dry routes remain solid.
        int basinIterations = enablePrimaryVoxelBasinLevelling
            ? Mathf.Clamp(primaryVoxelBasinPressureIterations, 0, 4)
            : 0;
        float basinRelaxation = Mathf.Clamp(primaryVoxelBasinPressureRelaxation, 0.01f, 0.25f);
        fluidCS.SetInt("_PrimaryVoxelPressureOnlyPass", 2);
        for (int basinPass = 0; basinPass < basinIterations; basinPass++)
        {
            fluidCS.SetFloat("_DeltaTime", voxelFlowDeltaTime);
            fluidCS.SetFloat("_PrimaryVoxelFlowFraction", basinRelaxation);
            fluidCS.Dispatch(kernelComputePrimaryVoxelFlux, groupsPrimary, 1, 1);
            fluidCS.Dispatch(kernelComputePrimaryVoxelReceiveScale, groupsPrimary, 1, 1);
            fluidCS.Dispatch(kernelApplyPrimaryVoxelFlux, groupsPrimary, 1, 1);
            SwapPrimaryVoxelBuffers();
        }

        // Dry cells are intentionally excluded from the pressure-only passes
        // above. Repair only a one-cell interior trough bracketed by water on
        // opposite open faces, so a split body rejoins without dilating its
        // exposed leading edge. This reuses the conservative face-flux solve.
        int continuityIterations = Mathf.Clamp(primaryVoxelContinuityIterations, 0, 4);
        float continuityRelaxation = Mathf.Clamp(primaryVoxelContinuityRelaxation, 0.01f, 0.25f);
        fluidCS.SetInt("_PrimaryVoxelPressureOnlyPass", 3);
        for (int continuityPass = 0; continuityPass < continuityIterations; continuityPass++)
        {
            fluidCS.SetFloat("_DeltaTime", voxelFlowDeltaTime);
            fluidCS.SetFloat("_PrimaryVoxelFlowFraction", continuityRelaxation);
            fluidCS.Dispatch(kernelComputePrimaryVoxelFlux, groupsPrimary, 1, 1);
            fluidCS.Dispatch(kernelComputePrimaryVoxelReceiveScale, groupsPrimary, 1, 1);
            fluidCS.Dispatch(kernelApplyPrimaryVoxelFlux, groupsPrimary, 1, 1);
            SwapPrimaryVoxelBuffers();
        }
        fluidCS.SetInt("_PrimaryVoxelPressureOnlyPass", 0);
        int groupsSurfaceWaves = Mathf.CeilToInt(Mathf.Max(primarySurfaceWaveEntryCount, 1) / 256f);
        RunPrimarySurfaceWaveStep(stepDeltaTime, Mathf.Max(groupsSurfaceWaves, 1));
        RunPrimarySurfaceFlowStep(stepDeltaTime);
        QueuePrimaryVoxelHatchFaceDebugReadback(stepDeltaTime);
        // PBF and surface projection use the original simulation timestep.
        fluidCS.SetFloat("_DeltaTime", stepDeltaTime);
        fluidCS.SetFloat("_PrimaryVoxelFlowFraction", wholeStepFlowFraction);
        BindPrimaryVoxelReadConsumers();
        fluidCS.Dispatch(kernelProjectPrimaryVoxelColumns, groupsColumns, 1, 1);
        fluidCS.Dispatch(kernelCollectPrimaryVoxelDiagnostics, groupsPrimary, 1, 1);

        // A fixed set of PBF slots becomes active only where a wet voxel
        // column exposes a top face. Slots retain their motion between steps;
        // inactive columns are seeded as water reaches them.
        fluidCS.Dispatch(kernelSeedPrimaryVoxelSurface, groupsParticles, 1, 1);
        fluidCS.Dispatch(kernelClearHash, groupsHash, 1, 1);
        fluidCS.Dispatch(kernelBuildHash, groupsParticles, 1, 1);

        if (solverMode == FluidSolverMode.PBF_XPBD)
        {
            fluidCS.Dispatch(kernelPBFPredictActive, groupsParticles, 1, 1);
            for (int i = 0; i < Mathf.Max(pbfIterations, 1); i++)
            {
                fluidCS.Dispatch(kernelClearHash, groupsHash, 1, 1);
                fluidCS.Dispatch(kernelBuildHash, groupsParticles, 1, 1);
                fluidCS.Dispatch(kernelPBFComputeLambda, groupsParticles, 1, 1);
                fluidCS.Dispatch(kernelPBFComputeDelta, groupsParticles, 1, 1);
                fluidCS.Dispatch(kernelPBFApplyDelta, groupsParticles, 1, 1);
            }
            fluidCS.Dispatch(kernelClearHash, groupsHash, 1, 1);
            fluidCS.Dispatch(kernelBuildHash, groupsParticles, 1, 1);
            fluidCS.Dispatch(kernelPBFUpdateVelocity, groupsParticles, 1, 1);
        }
        else
        {
            fluidCS.Dispatch(kernelDensityActive, groupsParticles, 1, 1);
            fluidCS.Dispatch(kernelForcesActive, groupsParticles, 1, 1);
            fluidCS.Dispatch(kernelIntegrateActive, groupsParticles, 1, 1);
        }

        // Fixed PBF slots are a moving skin, not another mass store. Keep them
        // near their owning wet columns, while giving true top-layer crests a
        // short grace period for the separate spray system to clone them.
        fluidCS.Dispatch(kernelConstrainPrimaryVoxelSurface, groupsParticles, 1, 1);

        // The display volume comes straight from the authoritative 3D field;
        // it never depends on a sparse particle scatter or on surface tiles.
        bulkVoxelCounterBuffer.SetData(new int[3]);
        fluidCS.Dispatch(kernelCopyPrimaryVoxelDisplay, groupsPrimary, 1, 1);
    }

    bool TryGetPrimaryVoxelHatchFaceDebugPosition(out Vector3 position)
    {
        position = Vector3.zero;
        if (primaryVoxelVerticalOpeningProbe != null)
        {
            position = primaryVoxelVerticalOpeningProbe.position;
            return true;
        }

        ShipSectionBuilder builder = FindFirstObjectByType<ShipSectionBuilder>();
        if (builder != null && builder.verticalOpeningProbe != null)
        {
            position = builder.verticalOpeningProbe.position;
            return true;
        }

        BoxCollider hatch = primaryVoxelHatchProbeVolume;
        if (hatch == null && primaryVoxelHatchRiserVolumes != null)
        {
            for (int i = 0; i < primaryVoxelHatchRiserVolumes.Length; i++)
            {
                if (primaryVoxelHatchRiserVolumes[i] != null &&
                    primaryVoxelHatchRiserVolumes[i].enabled &&
                    primaryVoxelHatchRiserVolumes[i].isTrigger)
                {
                    hatch = primaryVoxelHatchRiserVolumes[i];
                    break;
                }
            }
        }

        if (hatch == null && builder != null && builder.verticalRiserVolumes != null)
        {
            for (int i = 0; i < builder.verticalRiserVolumes.Length; i++)
            {
                if (builder.verticalRiserVolumes[i] != null &&
                    builder.verticalRiserVolumes[i].enabled &&
                    builder.verticalRiserVolumes[i].isTrigger)
                {
                    hatch = builder.verticalRiserVolumes[i];
                    break;
                }
            }
        }

        if (hatch == null)
            return false;

        position = hatch.bounds.center;
        return true;
    }

    void QueuePrimaryVoxelHatchFaceDebugReadback(float stepDeltaTime)
    {
        if (!usePrimaryVoxelVolume || primaryVoxelHatchFaceDebugReadbackPending ||
            primaryVoxelHatchDebugBuffer == null)
            return;

        primaryVoxelHatchFaceDebugTimer += Mathf.Max(stepDeltaTime, 0f);
        if (primaryVoxelHatchFaceDebugTimer < PrimaryVoxelHatchFaceDebugQueryIntervalSeconds)
            return;
        primaryVoxelHatchFaceDebugTimer = 0f;

        if (!TryGetPrimaryVoxelHatchFaceDebugPosition(out Vector3 hatchPosition))
        {
            if (!primaryVoxelHatchFaceDebugProbeWarningLogged)
            {
                primaryVoxelHatchFaceDebugProbeWarningLogged = true;
                Debug.LogWarning(
                    "[HatchFaceDebug] No hatch probe or generated vertical opening probe was found.",
                    this);
            }
            return;
        }

        Vector2 cellSize = new Vector2(
            Mathf.Max(boundsMax.x - boundsMin.x, 0.0001f) / Mathf.Max(BulkGridCountX, 1),
            Mathf.Max(boundsMax.z - boundsMin.z, 0.0001f) / Mathf.Max(BulkGridCountZ, 1));
        int x = Mathf.FloorToInt((hatchPosition.x - boundsMin.x) / cellSize.x);
        int z = Mathf.FloorToInt((hatchPosition.z - boundsMin.z) / cellSize.y);
        int lowerY = Mathf.FloorToInt(
            (hatchPosition.y - boundsMin.y) / Mathf.Max(primaryVoxelHeight, 0.0001f)) - 1;

        // Evaluate the same normal (non-pressure-only) transfer formula used
        // by the real flow pass. These uniforms are restored by the caller
        // before projection continues.
        fluidCS.SetInt("_PrimaryVoxelPressureOnlyPass", 0);
        fluidCS.SetFloat("_DeltaTime",
            stepDeltaTime / Mathf.Clamp(primaryVoxelFlowSubsteps, 1, 8));
        fluidCS.SetFloat("_PrimaryVoxelFlowFraction",
            Mathf.Clamp(primaryVoxelFlowFraction, 0.01f, 0.9f));
        fluidCS.SetInt("_PrimaryVoxelHatchDebugX", x);
        fluidCS.SetInt("_PrimaryVoxelHatchDebugLowerY", lowerY);
        fluidCS.SetInt("_PrimaryVoxelHatchDebugZ", z);
        fluidCS.Dispatch(kernelQueryPrimaryVoxelHatchDebug, 1, 1, 1);
        primaryVoxelHatchFaceDebugReadbackPending = true;
        AsyncGPUReadback.Request(
            primaryVoxelHatchDebugBuffer,
            OnPrimaryVoxelHatchFaceDebugReadback);
    }

    void OnPrimaryVoxelHatchFaceDebugReadback(AsyncGPUReadbackRequest request)
    {
        primaryVoxelHatchFaceDebugReadbackPending = false;
        if (request.hasError)
        {
            Debug.LogWarning("[HatchFaceDebug] Async GPU readback failed.", this);
            return;
        }

        var data = request.GetData<PrimaryVoxelHatchDebugGPU>();
        if (data.Length == 0)
            return;

        PrimaryVoxelHatchDebugGPU result = data[0];
        string faceState = result.throat.x > 0.5f ? "SOLID/BLOCKED" : "OPEN";
        if (Time.unscaledTime < primaryVoxelHatchFaceDebugLastLogTime +
            PrimaryVoxelHatchFaceDebugLogIntervalSeconds)
            return;

        primaryVoxelHatchFaceDebugLastLogTime = Time.unscaledTime;
        Debug.Log(
            $"[HatchFaceDebug] voxel X={result.coord.x:0} Z={result.coord.z:0} | " +
            $"Layer 1 lower Y={result.coord.y:0}: fill={result.lower.x:0.000}, " +
            $"solid={result.lower.y > 0.5f}, valid={result.lower.z > 0.5f} | " +
            $"Layer 2 throat face={faceState}, upFlux={result.throat.y:0.000000}, " +
            $"downFlux={result.throat.z:0.000000}, faceY={result.throat.w:0.000} | " +
            $"Layer 3 upper Y={result.coord.w:0}: fill={result.upper.x:0.000}, " +
            $"solid={result.upper.y > 0.5f}, valid={result.upper.z > 0.5f}",
            this);
    }

    void EnsurePrimaryVoxelCurrentSplatResources()
    {
        if (generatedCurrentSplatMesh == null)
        {
            generatedCurrentSplatMesh = new Mesh { name = "Generated Painted Current Splat Quad" };
            generatedCurrentSplatMesh.vertices = new[]
            {
                new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f),
                new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f)
            };
            generatedCurrentSplatMesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 1f), new Vector2(0f, 1f)
            };
            generatedCurrentSplatMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            generatedCurrentSplatMesh.RecalculateBounds();
        }
        if (generatedSurfaceGaussianMesh == null)
        {
            // A very small low-poly dome rather than another flat card. Its
            // outer ring rests on the PBF contact plane; the shader scales
            // its y coordinate by the inspector-controlled cap height.
            // Six sides / eighteen triangles keeps a dense painted surface
            // comfortably cheaper than a particle-volume representation.
            const int sides = 6;
            const float shoulderRadius = 0.56f;
            const float shoulderHeight = 0.46f;
            Vector3[] vertices = new Vector3[sides * 2 + 1];
            Vector2[] uv = new Vector2[vertices.Length];
            for (int i = 0; i < sides; i++)
            {
                float angle = Mathf.PI * 2f * i / sides;
                float x = Mathf.Cos(angle);
                float z = Mathf.Sin(angle);
                vertices[i] = new Vector3(x, 0f, z);
                uv[i] = new Vector2(0.5f + x * 0.5f, 0.5f + z * 0.5f);
                vertices[sides + i] = new Vector3(x * shoulderRadius, shoulderHeight, z * shoulderRadius);
                uv[sides + i] = new Vector2(
                    0.5f + x * shoulderRadius * 0.5f,
                    0.5f + z * shoulderRadius * 0.5f);
            }
            int apex = sides * 2;
            vertices[apex] = new Vector3(0f, 1f, 0f);
            uv[apex] = new Vector2(0.5f, 0.5f);
            int[] triangles = new int[sides * 9];
            int triangle = 0;
            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;
                int shoulder = sides + i;
                int nextShoulder = sides + next;
                triangles[triangle++] = i;
                triangles[triangle++] = nextShoulder;
                triangles[triangle++] = next;
                triangles[triangle++] = i;
                triangles[triangle++] = shoulder;
                triangles[triangle++] = nextShoulder;
                triangles[triangle++] = shoulder;
                triangles[triangle++] = apex;
                triangles[triangle++] = nextShoulder;
            }
            generatedSurfaceGaussianMesh = new Mesh { name = "Generated Painted Surface Gaussian Cap" };
            generatedSurfaceGaussianMesh.vertices = vertices;
            generatedSurfaceGaussianMesh.uv = uv;
            generatedSurfaceGaussianMesh.triangles = triangles;
            generatedSurfaceGaussianMesh.RecalculateBounds();
        }
        int capacity = Mathf.Clamp(primaryVoxelCurrentSplatCapacity, 256, 4096);
        if (primaryVoxelCurrentSplatBuffer == null || primaryVoxelCurrentSplatBuffer.count != capacity)
        {
            primaryVoxelCurrentSplatBuffer?.Release();
            primaryVoxelCurrentSplatBuffer = new ComputeBuffer(capacity, CurrentSplatStride,
                ComputeBufferType.Append);
        }
        if (primaryVoxelUnderwaterSplatStateBuffer == null ||
            primaryVoxelUnderwaterSplatStateBuffer.count != capacity)
        {
            primaryVoxelUnderwaterSplatStateBuffer?.Release();
            primaryVoxelUnderwaterSplatStateBuffer = new ComputeBuffer(capacity,
                UnderwaterSplatStateStride);
            primaryVoxelUnderwaterSplatStateBuffer.SetData(
                new UnderwaterSplatStateGPU[capacity]);
        }
        if (primaryVoxelCurrentSplatArgsBuffer == null)
        {
            primaryVoxelCurrentSplatArgsBuffer = new ComputeBuffer(1, sizeof(uint) * 5,
                ComputeBufferType.IndirectArguments);
            primaryVoxelCurrentSplatArgsBuffer.SetData(new uint[] { 6, 0, 0, 0, 0 });
        }
        int surfaceCapacity = Mathf.Max(PrimarySurfaceParticleCount, 1);
        if (primaryVoxelSurfaceGaussianBuffer == null || primaryVoxelSurfaceGaussianBuffer.count != surfaceCapacity)
        {
            primaryVoxelSurfaceGaussianBuffer?.Release();
            primaryVoxelSurfaceGaussianBuffer = new ComputeBuffer(surfaceCapacity, CurrentSplatStride,
                ComputeBufferType.Append);
        }
        if (primaryVoxelSurfaceGaussianHistoryBuffer == null ||
            primaryVoxelSurfaceGaussianHistoryBuffer.count != surfaceCapacity)
        {
            primaryVoxelSurfaceGaussianHistoryBuffer?.Release();
            primaryVoxelSurfaceGaussianHistoryBuffer = new ComputeBuffer(surfaceCapacity,
                SurfaceGaussianHistoryStride);
            // A history entry is owned by one stable PBF support slot. It is
            // render-only and starts invalid (all zero), so no readback or
            // solver-side state is introduced.
            primaryVoxelSurfaceGaussianHistoryBuffer.SetData(
                new SurfaceGaussianHistoryGPU[surfaceCapacity]);
        }
        if (primaryVoxelSurfaceGaussianDiagnosticsBuffer == null ||
            primaryVoxelSurfaceGaussianDiagnosticsBuffer.count != PrimaryVoxelSurfaceGaussianDiagnosticsCount)
        {
            primaryVoxelSurfaceGaussianDiagnosticsBuffer?.Release();
            primaryVoxelSurfaceGaussianDiagnosticsBuffer = new ComputeBuffer(
                PrimaryVoxelSurfaceGaussianDiagnosticsCount, sizeof(uint));
            primaryVoxelSurfaceGaussianDiagnosticsBuffer.SetData(
                new uint[PrimaryVoxelSurfaceGaussianDiagnosticsCount]);
        }
        if (primaryVoxelSurfaceGaussianArgsBuffer == null)
        {
            primaryVoxelSurfaceGaussianArgsBuffer = new ComputeBuffer(1, sizeof(uint) * 5,
                ComputeBufferType.IndirectArguments);
            primaryVoxelSurfaceGaussianArgsBuffer.SetData(new uint[]
            {
                // Sparse shallow mound mesh from the working backup look.
                (uint)generatedSurfaceGaussianMesh.GetIndexCount(0), 0, 0, 0, 0
            });
        }
        if (generatedCurrentSplatMaterial == null)
        {
            Shader shader = currentSplatShader != null
                ? currentSplatShader : Shader.Find("Custom/FluidCurrentSplats");
            if (shader != null && shader.isSupported)
                generatedCurrentSplatMaterial = new Material(shader) { name = "Generated Painted Current Splat Material" };
        }
        if (generatedUnderwaterSplatMaterial == null)
        {
            Shader shader = currentSplatShader != null
                ? currentSplatShader : Shader.Find("Custom/FluidCurrentSplats");
            if (shader != null && shader.isSupported)
                generatedUnderwaterSplatMaterial = new Material(shader) { name = "Generated Underwater Current Splat Material" };
        }
    }

    void UpdateAndDrawPrimaryVoxelCurrentSplats()
    {
        if (!drawPrimaryVoxelCurrentSplats || !usePrimaryVoxelVolume || fluidCS == null ||
            primaryVoxelBuffer == null)
            return;

        EnsurePrimaryVoxelCurrentSplatResources();
        if (generatedCurrentSplatMaterial == null || generatedCurrentSplatMesh == null ||
            generatedSurfaceGaussianMesh == null)
            return;
        if (drawPrimaryVoxelUnderwaterCurrentSplats && generatedUnderwaterSplatMaterial == null)
            return;

        // Surface PBF supports can change every frame, so the phase-one
        // surface buffer is refreshed every frame. The future underwater
        // population may still use a lower update rate.
        int capacity = Mathf.Clamp(primaryVoxelCurrentSplatCapacity, 256, 4096);
        primaryVoxelSurfaceGaussianBuffer.SetCounterValue(0);
        // CopyCount updates only argument[1]. Force argument[0] to the shallow
        // mound mesh every frame so hot reload cannot retain a stale header
        // cannot make Unity silently skip the indirect draw after hot reload.
        primaryVoxelSurfaceGaussianArgsBuffer.SetData(new uint[]
        {
            (uint)generatedSurfaceGaussianMesh.GetIndexCount(0), 0, 0, 0, 0
        });
        fluidCS.SetBuffer(kernelClearPrimaryVoxelSurfaceGaussianDiagnostics,
            "primaryVoxelSurfaceGaussianDiagnostics",
            primaryVoxelSurfaceGaussianDiagnosticsBuffer);
        fluidCS.Dispatch(kernelClearPrimaryVoxelSurfaceGaussianDiagnostics, 1, 1, 1);
        bool restrictToShipInterior = TryGetShipInteriorSplatBounds(out Bounds interiorBounds);
        if (drawPrimaryVoxelUnderwaterCurrentSplats)
        {
                primaryVoxelCurrentSplatBuffer.SetCounterValue(0);
                fluidCS.SetBuffer(kernelUpdatePrimaryVoxelUnderwaterSplats, "primaryVoxelCellsRead", primaryVoxelBuffer);
                fluidCS.SetBuffer(kernelUpdatePrimaryVoxelUnderwaterSplats, "primaryVoxelSolidRead", primaryVoxelSolidBuffer);
                fluidCS.SetBuffer(kernelUpdatePrimaryVoxelUnderwaterSplats, "particles", particleBuffer);
                fluidCS.SetBuffer(kernelUpdatePrimaryVoxelUnderwaterSplats, "primaryVoxelCurrentSplats", primaryVoxelCurrentSplatBuffer);
                fluidCS.SetBuffer(kernelUpdatePrimaryVoxelUnderwaterSplats, "primaryVoxelUnderwaterSplatStates", primaryVoxelUnderwaterSplatStateBuffer);
                fluidCS.SetFloat("_PrimaryVoxelCurrentSplatSpeedThreshold", Mathf.Max(primaryVoxelCurrentSplatSpeedThreshold, 0.01f));
                fluidCS.SetFloat("_PrimaryVoxelCurrentSplatRadius", Mathf.Max(primaryVoxelCurrentSplatRadius, 0.05f));
                fluidCS.SetInt("_PrimaryVoxelUnderwaterSplatCount", Mathf.Clamp(primaryVoxelUnderwaterSplatCount, 64, capacity));
                fluidCS.SetInt("_PrimaryVoxelUnderwaterSplatFrame", Time.frameCount);
                fluidCS.SetFloat("_PrimaryVoxelSurfaceSplatVisualDeltaTime", Mathf.Clamp(Time.deltaTime, 0.001f, 0.05f));
                fluidCS.SetInt("_PrimaryVoxelGaussianInteriorOnly", restrictToShipInterior ? 1 : 0);
                if (restrictToShipInterior)
                {
                    fluidCS.SetVector("_PrimaryVoxelGaussianInteriorMin", interiorBounds.min);
                    fluidCS.SetVector("_PrimaryVoxelGaussianInteriorMax", interiorBounds.max);
                }
                int groups = Mathf.CeilToInt(Mathf.Clamp(primaryVoxelUnderwaterSplatCount, 64, capacity) / 256f);
                fluidCS.Dispatch(kernelUpdatePrimaryVoxelUnderwaterSplats, Mathf.Max(groups, 1), 1, 1);
                ComputeBuffer.CopyCount(primaryVoxelCurrentSplatBuffer, primaryVoxelCurrentSplatArgsBuffer, sizeof(uint));
        }

            // Phase 1 Gaussian prototype: convert every active top PBF
            // support to a dense surface stamp and sample voxel velocity at
            // that support. The PBF point keeps the Gaussian tied to the
            // visual water, while voxel velocity supplies its flow direction.
            fluidCS.SetBuffer(kernelBuildPrimaryVoxelSurfaceGaussians, "particles", particleBuffer);
            fluidCS.SetBuffer(kernelBuildPrimaryVoxelSurfaceGaussians, "primaryVoxelCellsRead", primaryVoxelBuffer);
            fluidCS.SetBuffer(kernelBuildPrimaryVoxelSurfaceGaussians, "primaryVoxelSolidRead", primaryVoxelSolidBuffer);
            fluidCS.SetBuffer(kernelBuildPrimaryVoxelSurfaceGaussians, "surfaceWaveCellsRead", primarySurfaceWaveBuffer);
            fluidCS.SetBuffer(kernelBuildPrimaryVoxelSurfaceGaussians, "surfaceWaveFaceFluxRead", primarySurfaceWaveFaceFluxBuffer);
            fluidCS.SetBuffer(kernelBuildPrimaryVoxelSurfaceGaussians, "surfaceFlowCellsRead", primarySurfaceFlowBuffer);
            fluidCS.SetBuffer(kernelBuildPrimaryVoxelSurfaceGaussians, "primaryVoxelSurfaceGaussians", primaryVoxelSurfaceGaussianBuffer);
            fluidCS.SetBuffer(kernelBuildPrimaryVoxelSurfaceGaussians,
                "primaryVoxelSurfaceGaussianHistory", primaryVoxelSurfaceGaussianHistoryBuffer);
            fluidCS.SetBuffer(kernelBuildPrimaryVoxelSurfaceGaussians,
                "primaryVoxelSurfaceGaussianDiagnostics", primaryVoxelSurfaceGaussianDiagnosticsBuffer);
            fluidCS.SetInt("_PrimaryVoxelGaussianInteriorOnly", restrictToShipInterior ? 1 : 0);
            if (restrictToShipInterior)
            {
                fluidCS.SetVector("_PrimaryVoxelGaussianInteriorMin", interiorBounds.min);
                fluidCS.SetVector("_PrimaryVoxelGaussianInteriorMax", interiorBounds.max);
            }
            fluidCS.SetFloat("_PrimaryVoxelCurrentSurfaceLift",
                Mathf.Clamp(primaryVoxelCurrentSurfaceLift, 0.02f, 1f));
            fluidCS.SetFloat("_PrimaryVoxelCurrentSplatRadius", Mathf.Max(primaryVoxelCurrentSplatRadius, particleRadius * 1.7f));
            fluidCS.SetFloat("_PrimaryVoxelCurrentSplatSpeedThreshold",
                Mathf.Max(primaryVoxelCurrentSplatSpeedThreshold, 0.05f));
            fluidCS.SetFloat("_PrimaryVoxelSurfaceSplatOverlap",
                Mathf.Clamp(primaryVoxelSurfaceSplatOverlap, 1f, 1.6f));
            fluidCS.SetFloat("_PrimaryVoxelSurfaceSplatChance",
                Mathf.Clamp(primaryVoxelSurfaceSplatCoverage, 0.05f, 1f));
            fluidCS.SetFloat("_PrimaryVoxelSurfaceSplatPositionSmoothing",
                Mathf.Max(primaryVoxelSurfaceSplatPositionSmoothing, 0.02f));
            fluidCS.SetFloat("_PrimaryVoxelSurfaceSplatVelocitySmoothing",
                Mathf.Max(primaryVoxelSurfaceSplatVelocitySmoothing, 0.02f));
            fluidCS.SetFloat("_PrimaryVoxelSurfaceSplatVisualDeltaTime",
                Mathf.Clamp(Time.deltaTime, 0.001f, 0.05f));
            int surfaceGroups = Mathf.CeilToInt(PrimarySurfaceParticleCount / 256f);
            fluidCS.Dispatch(kernelBuildPrimaryVoxelSurfaceGaussians, Mathf.Max(surfaceGroups, 1), 1, 1);
            ComputeBuffer.CopyCount(primaryVoxelSurfaceGaussianBuffer, primaryVoxelSurfaceGaussianArgsBuffer, sizeof(uint));
            QueuePrimaryVoxelSurfaceGaussianDiagnosticsReadback();

        generatedCurrentSplatMaterial.SetBuffer("_CurrentSplats", primaryVoxelSurfaceGaussianBuffer);
        // Retained for the shader's optional direct-PBF path; the phase-one
        // renderer uses the dedicated converted Gaussian buffer above.
        generatedCurrentSplatMaterial.SetBuffer("_PbfSurfaceParticles", particleBuffer);
        generatedCurrentSplatMaterial.SetColor("_UnderwaterCurrentColor", primaryVoxelCurrentSplatColor);
        generatedCurrentSplatMaterial.SetColor("_SurfaceCurrentColor", primaryVoxelSurfaceSplatColor);
        generatedCurrentSplatMaterial.SetFloat("_CurrentSplatOpacity", Mathf.Clamp01(primaryVoxelCurrentSplatOpacity));
        generatedCurrentSplatMaterial.SetFloat("_CurrentSplatMaxSpeed", Mathf.Max(primaryVoxelMaxGridSpeed, 0.1f));
        float splatGradientMaxSpeed = primaryVoxelCurrentSplatWhiteSpeed > 0.001f
            ? primaryVoxelCurrentSplatWhiteSpeed : primaryVoxelMaxGridSpeed;
        generatedCurrentSplatMaterial.SetFloat("_CurrentSplatWhiteSpeed",
            Mathf.Max(splatGradientMaxSpeed, 0.1f));
        generatedCurrentSplatMaterial.SetFloat("_SurfaceSplatCalmSpeed",
            Mathf.Max(primaryVoxelSurfaceSplatCalmSpeed, 0.01f));
        generatedCurrentSplatMaterial.SetFloat("_SurfaceGaussianHeight",
            Mathf.Clamp(primaryVoxelCurrentSurfaceSplatHeight, 0.02f, 0.75f));
        bool clipGaussiansToMappedSurface = surfaceTileRenderer != null &&
            surfaceTileRenderer.renderAsHeightmapSurface &&
            surfaceTileRenderer.SurfaceFieldBuffer != null;
        if (clipGaussiansToMappedSurface)
            generatedCurrentSplatMaterial.SetBuffer(
                "_SurfaceFields", surfaceTileRenderer.SurfaceFieldBuffer);
        generatedCurrentSplatMaterial.SetInt(
            "_UseMappedSurfaceClip", clipGaussiansToMappedSurface ? 1 : 0);
        if (surfaceTileRenderer != null)
        {
            generatedCurrentSplatMaterial.SetInt(
                "_SurfaceFieldCountX", Mathf.Max(surfaceTileRenderer.TileCountX, 1));
            generatedCurrentSplatMaterial.SetInt(
                "_SurfaceFieldCountZ", Mathf.Max(surfaceTileRenderer.TileCountZ, 1));
            generatedCurrentSplatMaterial.SetInt(
                "_SurfaceFieldSlotCount", surfaceTileRenderer.SurfaceFieldSlotCount);
            generatedCurrentSplatMaterial.SetVector(
                "_MappedSurfaceBoundsMin", surfaceTileRenderer.boundsMin);
            generatedCurrentSplatMaterial.SetVector(
                "_MappedSurfaceBoundsMax", surfaceTileRenderer.boundsMax);
            generatedCurrentSplatMaterial.SetFloat(
                "_SurfaceFieldVisibleContour", surfaceTileRenderer.SurfaceVisibleContour);
        }
        generatedCurrentSplatMaterial.SetFloat(
            "_MappedSurfaceVerticalTolerance",
            Mathf.Max(
                Mathf.Max(primaryVoxelHeight, 0.05f) * 1.5f,
                particleRadius * 4f + primaryVoxelCurrentSurfaceLift +
                    primaryVoxelCurrentSurfaceSplatHeight));
        if (TryGetShipInteriorSplatBounds(out Bounds renderInteriorBounds))
        {
            generatedCurrentSplatMaterial.SetInt("_UseSplatInteriorBounds", 1);
            generatedCurrentSplatMaterial.SetVector("_SplatInteriorMin", renderInteriorBounds.min);
            generatedCurrentSplatMaterial.SetVector("_SplatInteriorMax", renderInteriorBounds.max);
        }
        else
        {
            // The original test scene has no ShipHull tagged colliders, so it
            // retains its unconstrained fluid visualisation.
            generatedCurrentSplatMaterial.SetInt("_UseSplatInteriorBounds", 0);
        }
        Bounds bounds = new Bounds((boundsMin + boundsMax) * 0.5f, boundsMax - boundsMin + Vector3.one * 8f);
        generatedCurrentSplatMaterial.SetInt("_RenderPbfSurfaceLayer", 0);
        generatedCurrentSplatMaterial.SetInt("_RenderSurfaceGaussianVolume", 1);
        Graphics.DrawMeshInstancedIndirect(generatedSurfaceGaussianMesh, 0, generatedCurrentSplatMaterial,
            bounds, primaryVoxelSurfaceGaussianArgsBuffer);
        if (drawPrimaryVoxelUnderwaterCurrentSplats)
        {
            // Indirect draws are queued. They must not share a mutable
            // material: changing the buffer/shape mode here previously made
            // the surface dome draw read underwater cards, producing the
            // giant exterior turquoise sheet.
            generatedUnderwaterSplatMaterial.CopyPropertiesFromMaterial(generatedCurrentSplatMaterial);
            generatedUnderwaterSplatMaterial.SetBuffer("_CurrentSplats", primaryVoxelCurrentSplatBuffer);
            generatedUnderwaterSplatMaterial.SetInt("_RenderSurfaceGaussianVolume", 0);
            Graphics.DrawMeshInstancedIndirect(generatedCurrentSplatMesh, 0, generatedUnderwaterSplatMaterial,
                bounds, primaryVoxelCurrentSplatArgsBuffer);
        }
    }

    void QueuePrimaryVoxelSurfaceGaussianDiagnosticsReadback()
    {
        if (!logPrimaryVoxelSurfaceGaussianDiagnostics ||
            primaryVoxelSurfaceGaussianDiagnosticsBuffer == null ||
            primaryVoxelSurfaceGaussianDiagnosticsReadbackPending)
            return;

        primaryVoxelSurfaceGaussianDiagnosticsTimer += Mathf.Max(Time.deltaTime, 0f);
        float interval = Mathf.Max(primaryVoxelSurfaceGaussianDiagnosticsInterval, 0.25f);
        if (primaryVoxelSurfaceGaussianDiagnosticsTimer < interval)
            return;

        primaryVoxelSurfaceGaussianDiagnosticsTimer = 0f;
        primaryVoxelSurfaceGaussianDiagnosticsReadbackPending = true;
        AsyncGPUReadback.Request(primaryVoxelSurfaceGaussianDiagnosticsBuffer, request =>
        {
            primaryVoxelSurfaceGaussianDiagnosticsReadbackPending = false;
            if (request.hasError)
            {
                Debug.LogWarning("[GaussianDebug] Async readback failed.", this);
                return;
            }

            uint[] data = new uint[PrimaryVoxelSurfaceGaussianDiagnosticsCount];
            request.GetData<uint>().CopyTo(data);
            float active = data[0];
            float averageSpeed = active > 0f
                ? data[5] / PrimaryVoxelSurfaceGaussianDiagnosticsScale / active
                : 0f;
            float averageMovement = active > 0f
                ? data[6] / PrimaryVoxelSurfaceGaussianDiagnosticsScale / active
                : 0f;
            Debug.Log(
                $"[GaussianDebug] active={data[0]} moved={data[1]} " +
                $"avgOwnerSampleSpeed={averageSpeed:F4}m/s " +
                $"avgMovementPerFrame={averageMovement:F5}m " +
                $"zeroVelocity={data[2]} blockedComponents={data[3]} " +
                $"surfaceLost={data[4]} lateralBlocked={data[7]} " +
                $"verticalBlocked={data[8]} verticalTransitions={data[9]}",
                this);
        });
    }
    bool TryGetShipInteriorSplatBounds(out Bounds interiorBounds)
    {
        GameObject[] hullParts = GameObject.FindGameObjectsWithTag("ShipHull");
        if (hullParts == null || hullParts.Length == 0)
        {
            interiorBounds = default;
            return false;
        }

        bool hasBounds = false;
        Bounds hullBounds = default;
        foreach (GameObject part in hullParts)
        {
            Collider hullCollider = part.GetComponent<Collider>();
            if (hullCollider == null)
                continue;
            if (!hasBounds)
            {
                hullBounds = hullCollider.bounds;
                hasBounds = true;
            }
            else
            {
                hullBounds.Encapsulate(hullCollider.bounds);
            }
        }

        if (!hasBounds)
        {
            interiorBounds = default;
            return false;
        }

        // The generated hull is 0.5 m thick. Shrinking its overall bounds by
        // that amount lands just inside the port/starboard/end walls, keeping
        // the exterior reservoir's PBF skin out of this interior-only effect.
        hullBounds.Expand(-0.5f);
        interiorBounds = hullBounds;
        return true;
    }

    void RunSprayStep()
    {
        if (sprayParticleBuffer == null || fluidCS == null)
            return;

        int groups = Mathf.CeilToInt(Mathf.Max(SprayParticleCount, 1) / 256f);
        // Update first so a droplet that has returned to the liquid is removed
        // before the spawn pass can reuse its slot. This is a cosmetic pool;
        // no particle or bulk token is created or destroyed here.
        fluidCS.Dispatch(kernelUpdateSpray, groups, 1, 1);
        if (enableSpray)
            fluidCS.Dispatch(kernelSpawnSpray, groups, 1, 1);
    }

    void PushPrimaryVoxelHatchRiserVolumes()
    {
        System.Array.Clear(primaryVoxelHatchRiserBounds, 0, primaryVoxelHatchRiserBounds.Length);
        if (primaryVoxelHatchRiserVolumes == null || primaryVoxelHatchRiserVolumes.Length == 0)
        {
            ShipSectionBuilder builder = FindFirstObjectByType<ShipSectionBuilder>();
            if (builder != null)
                primaryVoxelHatchRiserVolumes = builder.verticalRiserVolumes;
        }

        int count = 0;
        if (enablePrimaryVoxelHatchRiserFlow && primaryVoxelHatchRiserVolumes != null)
        {
            for (int i = 0; i < primaryVoxelHatchRiserVolumes.Length && count < MaxPrimaryVoxelHatchRiserVolumes; i++)
            {
                BoxCollider trigger = primaryVoxelHatchRiserVolumes[i];
                if (trigger == null || !trigger.enabled || !trigger.isTrigger ||
                    !trigger.gameObject.activeInHierarchy)
                    continue;

                Bounds bounds = trigger.bounds;
                primaryVoxelHatchRiserBounds[count * 2] = new Vector4(bounds.center.x,
                    bounds.center.y, bounds.center.z, bounds.extents.x);
                primaryVoxelHatchRiserBounds[count * 2 + 1] = new Vector4(bounds.extents.y,
                    bounds.extents.z, 0f, 0f);
                count++;
            }
        }

        fluidCS.SetInt("_PrimaryVoxelHatchRiserCount", count);
        fluidCS.SetVectorArray("_PrimaryVoxelHatchRiserBounds", primaryVoxelHatchRiserBounds);
        fluidCS.SetFloat("_PrimaryVoxelHatchRiserMinimumSourceFill",
            Mathf.Clamp01(primaryVoxelHatchRiserMinimumSourceFill));
        fluidCS.SetFloat("_PrimaryVoxelHatchRiserDischargeCoefficient",
            Mathf.Max(primaryVoxelHatchRiserDischargeCoefficient, 0f));
        fluidCS.SetFloat("_PrimaryVoxelHatchRiserExternalWaterLevel",
            GetPrimaryVoxelEffectiveExternalWaterLevel());
    }

    float GetPrimaryVoxelEffectiveExternalWaterLevel()
    {
        float baseExternalLevel = primaryVoxelExternalWaterLevel;
        if (useSeaLevelTransformForPressure && seaLevel != null)
            baseExternalLevel = seaLevel.position.y + seaLevelHeightOffset;

        // Kinematic ship motion already changes relative immersion. Adding this
        // legacy sea lift at the same time would double-count flood draft.
        bool useLegacySeaLift = enablePrimaryVoxelFloodDraftCoupling &&
            !enablePrimaryVoxelShipDraftMotion;
        return baseExternalLevel + (useLegacySeaLift
            ? Mathf.Max(primaryVoxelAdditionalDraft, 0f) : 0f);
    }

    bool IsPrimaryVoxelDraftCouplingActive()
    {
        return usePrimaryVoxelVolume && (enablePrimaryVoxelFloodDraftCoupling ||
            enablePrimaryVoxelShipDraftMotion);
    }

    bool TryResolvePrimaryVoxelShipRoot()
    {
        if (primaryVoxelShipRoot == null)
        {
            ShipSectionBuilder builder = FindFirstObjectByType<ShipSectionBuilder>();
            if (builder != null)
                primaryVoxelShipRoot = builder.GeneratedShipRoot;
        }

        if (primaryVoxelShipRoot == null)
            return false;

        int rootInstanceId = primaryVoxelShipRoot.GetInstanceID();
        if (!primaryVoxelShipRootInitialised || rootInstanceId != primaryVoxelShipRootInstanceId)
        {
            primaryVoxelShipRootInitialised = true;
            primaryVoxelShipRootInstanceId = rootInstanceId;
            primaryVoxelInitialShipRootY = primaryVoxelShipRoot.position.y;
            primaryVoxelCurrentShipRootY = primaryVoxelInitialShipRootY;
            primaryVoxelLastColliderRefreshY = primaryVoxelInitialShipRootY;
        }

        return true;
    }

    void ApplyPrimaryVoxelKinematicShipDraft()
    {
        // If the feature is turned off after use, return the generated ship to
        // its captured initial draft exactly once and leave the fixed world grid alone.
        if (!enablePrimaryVoxelShipDraftMotion && !primaryVoxelShipDraftHasApplied)
            return;
        if (!TryResolvePrimaryVoxelShipRoot())
            return;

        float appliedDraft = enablePrimaryVoxelShipDraftMotion
            ? Mathf.Max(primaryVoxelAdditionalDraft, 0f) : 0f;
        Vector3 position = primaryVoxelShipRoot.position;
        position.y = primaryVoxelInitialShipRootY - appliedDraft;
        primaryVoxelShipRoot.position = position;
        primaryVoxelCurrentShipRootY = position.y;

        if (!enablePrimaryVoxelShipDraftMotion)
        {
            primaryVoxelShipDraftHasApplied = false;
            primaryVoxelLastColliderRefreshY = position.y;
            RebuildSolidColliderBuffer();
            primaryVoxelShipTopologyRefreshCount++;
            return;
        }

        primaryVoxelShipDraftHasApplied = true;
        float refreshDistance = Mathf.Max(primaryVoxelShipColliderRebuildDistance, 0.001f);
        if (Mathf.Abs(position.y - primaryVoxelLastColliderRefreshY) < refreshDistance)
            return;

        // The ocean, waves, and voxel coordinates stay fixed in world space.
        // Only rebuilt collider OBBs and the solid-cell topology follow the hull.
        RebuildSolidColliderBuffer();
        primaryVoxelLastColliderRefreshY = position.y;
        primaryVoxelShipTopologyRefreshCount++;
    }

    void UpdatePrimaryVoxelFloodDraftCoupling()
    {
        if (!IsPrimaryVoxelDraftCouplingActive())
        {
            primaryVoxelFloodwaterMassKg = Mathf.Max(primaryVoxelCurrentVolume, 0f) *
                Mathf.Max(primaryVoxelFloodwaterDensity, 1f);
            primaryVoxelAdditionalDraft = 0f;
            primaryVoxelDraftVelocity = 0f;
            primaryVoxelEffectiveExternalWaterLevel = GetPrimaryVoxelEffectiveExternalWaterLevel();
            ApplyPrimaryVoxelKinematicShipDraft();
            return;
        }

        primaryVoxelFloodwaterMassKg = Mathf.Max(primaryVoxelCurrentVolume, 0f) *
            Mathf.Max(primaryVoxelFloodwaterDensity, 1f);
        float targetDraft = Mathf.Clamp(
            Mathf.Max(primaryVoxelCurrentVolume, 0f) / Mathf.Max(primaryVoxelWaterplaneArea, 1f),
            0f, Mathf.Max(primaryVoxelMaximumAdditionalDraft, 0f));
        float response = Mathf.Max(primaryVoxelDraftResponseSeconds, 0f);
        primaryVoxelAdditionalDraft = response > 0.0001f
            ? Mathf.SmoothDamp(primaryVoxelAdditionalDraft, targetDraft,
                ref primaryVoxelDraftVelocity, response, Mathf.Infinity, Time.deltaTime)
            : targetDraft;
        primaryVoxelEffectiveExternalWaterLevel = GetPrimaryVoxelEffectiveExternalWaterLevel();
        ApplyPrimaryVoxelKinematicShipDraft();
    }

    void QueuePrimaryVoxelFloodDraftReadback()
    {
        if (!IsPrimaryVoxelDraftCouplingActive() || primaryVoxelDraftReadbackInFlight ||
            primaryVoxelDiagnosticsBuffer == null)
            return;

        primaryVoxelDraftReadbackTimer += Time.deltaTime;
        if (primaryVoxelDraftReadbackTimer < Mathf.Max(primaryVoxelDraftReadbackInterval, 0.1f))
            return;

        primaryVoxelDraftReadbackTimer = 0f;
        primaryVoxelDraftReadbackInFlight = true;
        // This is a 12-uint asynchronous readback, not the full 3D voxel
        // field. It is deliberately isolated from optional flow diagnostics.
        AsyncGPUReadback.Request(primaryVoxelDiagnosticsBuffer, OnPrimaryVoxelFloodDraftReadback);
    }

    void OnPrimaryVoxelFloodDraftReadback(AsyncGPUReadbackRequest request)
    {
        primaryVoxelDraftReadbackInFlight = false;
        if (request.hasError || request.GetData<uint>().Length == 0)
            return;

        primaryVoxelCurrentVolume = request.GetData<uint>()[0] / PrimaryVoxelDiagnosticsScale;
    }

    [ContextMenu("Request Primary Voxel Vertical Opening Snapshot")]
    public void RequestPrimaryVoxelVerticalOpeningSnapshot()
    {
        if (primaryVoxelVerticalOpeningProbe == null)
        {
            ShipSectionBuilder builder = FindFirstObjectByType<ShipSectionBuilder>();
            if (builder != null)
                primaryVoxelVerticalOpeningProbe = builder.verticalOpeningProbe;
        }
        QueuePrimaryVoxelFlowDiagnosticsReadback();
    }
    void QueuePrimaryVoxelInflow(float stepDeltaTime)
    {
        PrimaryVoxelInflowGPU inflow = PrimaryVoxelInflowGPU.Disabled;
        bool breachInflowActive = usePrimaryVoxelBreachInflow && primaryVoxelBreachOpen;
        // A breach test must not fall back to the legacy spawn-rate source
        // when the external reservoir is closed. That would hide volume creep.
        bool legacyInflowActive = !usePrimaryVoxelBreachInflow && spawnRate > 0f;
        if (continuousSpawn && spawnPoint != null &&
            (breachInflowActive || legacyInflowActive))
        {
            if (breachInflowActive)
            {
                // The compute shader measures the present interior water level
                // at this opening and calculates the orifice discharge there.
                // Crucially, rejected water stays in the external sea instead
                // of accumulating as a deferred burst inside the simulator.
                inflow.positionVolume = new Vector4(spawnPoint.position.x, spawnPoint.position.y,
                    spawnPoint.position.z, stepDeltaTime);
                inflow.reservoirHole = new Vector4(
                    GetPrimaryVoxelEffectiveExternalWaterLevel(),
                    Mathf.Max(primaryVoxelBreachWidth, 0.05f),
                    Mathf.Max(primaryVoxelBreachHeight, 0.05f),
                    Mathf.Clamp(primaryVoxelBreachDischargeCoefficient, 0.05f, 1f));
                Vector3 direction = spawnPoint.forward.sqrMagnitude > 0.0001f
                    ? spawnPoint.forward.normalized
                    : Vector3.forward;
                inflow.velocityEnabled = new Vector4(
                    direction.x * Mathf.Max(primaryVoxelBreachMaxJetSpeed, 0.1f),
                    direction.y * Mathf.Max(primaryVoxelBreachMaxJetSpeed, 0.1f),
                    direction.z * Mathf.Max(primaryVoxelBreachMaxJetSpeed, 0.1f),
                    2f);
            }
            else
            {
            float fallbackParticleVolume = 1f / Mathf.Max(
                solverMode == FluidSolverMode.PBF_XPBD
                    ? shallowRestDensity * Mathf.Max(pbfRestDensityScale, 0.0001f)
                    : restDensity,
                0.0001f);
            float volumePerSpawnUnit = primaryVoxelSpawnVolumePerUnit > 0f
                ? primaryVoxelSpawnVolumePerUnit
                : fallbackParticleVolume;
            inflow.positionVolume = new Vector4(spawnPoint.position.x, spawnPoint.position.y,
                spawnPoint.position.z, spawnRate * stepDeltaTime * volumePerSpawnUnit);
            primaryVoxelCumulativeInjectedVolume += Mathf.Max(inflow.positionVolume.w, 0f);
            Vector3 velocity = spawnPoint.forward * inflowSpeed;
            inflow.velocityEnabled = new Vector4(velocity.x, velocity.y, velocity.z, 1f);
            }
        }
        primaryVoxelInflowBuffer.SetData(new[] { inflow });
    }

    void SpawnParticles(float deltaTime)
    {
        // If inflow is disabled, buffer is full, or there is no spawn point,
        // there is nothing to emit this frame.
        if (!continuousSpawn || activeParticles >= maxParticles || spawnPoint == null || particleBuffer == null)
            return;

        // Accumulate fractional spawn amount so spawnRate behaves smoothly over
        // time even when deltaTime varies.
        spawnAccumulator += spawnRate * deltaTime;
        int toSpawn = Mathf.FloorToInt(spawnAccumulator);
        spawnAccumulator -= toSpawn;

        int spawnCount = Mathf.Min(toSpawn, maxParticles - activeParticles);
        if (spawnCount <= 0)
            return;

        GPUParticle[] newParticles = new GPUParticle[spawnCount];
        Vector3[] newPreviousPositions = new Vector3[spawnCount];
        float[] newSurfaceAges = new float[spawnCount];
        float[] newDormantSpeedAverages = new float[spawnCount];

        for (int i = 0; i < spawnCount; i++)
        {
            // Spawn with a little jitter to avoid a perfectly uniform stream.
            Vector3 jitter = Random.insideUnitSphere * inflowRadius;
            Vector3 spawnPosition = spawnPoint.position + jitter;

            newParticles[i].pos = spawnPosition;
            newParticles[i].vel = spawnPoint.forward * inflowSpeed;
            newParticles[i].invMass = 1f;
            newParticles[i].density = 0f;
            newParticles[i].state = (int)ParticleState.Active;
            newParticles[i].sleepTimer = 0f;
            newParticles[i].padding = Vector2.zero;
            newPreviousPositions[i] = spawnPosition;
        }

        // Upload only the slice for the newly spawned particles.
        particleBuffer.SetData(newParticles, 0, activeParticles, spawnCount);
        previousPositionBuffer.SetData(newPreviousPositions, 0, activeParticles, spawnCount);
        particleSurfaceAgeBuffer.SetData(newSurfaceAges, 0, activeParticles, spawnCount);
        particleDormantSpeedAverageBuffer.SetData(newDormantSpeedAverages, 0, activeParticles, spawnCount);
        activeParticles += spawnCount;
        liveParticles = activeParticles;
    }

    void PushShaderParameters()
    {
        // Core simulation counts and timing.
        fluidCS.SetInt("_NumParticles", PrimarySurfaceParticleCount);
        fluidCS.SetFloat("_DeltaTime", shaderDeltaTime > 0f ? shaderDeltaTime : Time.deltaTime);
        fluidCS.SetVector("_Gravity", gravity);
        fluidCS.SetInt("_SprayEnabled", enableSpray ? 1 : 0);
        fluidCS.SetInt("_SprayCount", SprayParticleCount);
        fluidCS.SetInt("_SprayPhase", simulationStepCounter);
        fluidCS.SetFloat("_SpraySpawnSpeedThreshold", Mathf.Max(spraySpawnSpeedThreshold, 0.01f));
        fluidCS.SetFloat("_SpraySpawnPressureThreshold", Mathf.Max(spraySpawnPressureThreshold, 0f));
        fluidCS.SetFloat("_SpraySpawnFluxThreshold", Mathf.Max(spraySpawnFluxThreshold, 0f));
        fluidCS.SetFloat("_SpraySpawnHeight", Mathf.Max(spraySpawnHeight, particleRadius));
        fluidCS.SetFloat("_SprayLifetime", Mathf.Max(sprayLifetime, 0.05f));
        fluidCS.SetFloat("_SprayGravityScale", Mathf.Max(sprayGravityScale, 0f));
        fluidCS.SetFloat("_SprayLaunchUpward", Mathf.Max(sprayLaunchUpward, 0f));
        fluidCS.SetFloat("_SprayLaunchLateral", Mathf.Max(sprayLaunchLateral, 0f));
        fluidCS.SetFloat("_SprayMinRadius", Mathf.Max(sprayMinRadius, 0.001f));
        fluidCS.SetFloat("_SprayMaxRadius", Mathf.Max(sprayMaxRadius, sprayMinRadius));
        fluidCS.SetFloat("_SprayReabsorbDepth", Mathf.Max(sprayReabsorbDepth, 0f));
        fluidCS.SetFloat("_SprayFadeSeconds", Mathf.Max(sprayFadeSeconds, 0.001f));

        // World bounds and grid settings.
        fluidCS.SetVector("_BoundsMin", boundsMin);
        fluidCS.SetVector("_BoundsMax", boundsMax);

        fluidCS.SetInt("_GridResolution", gridResolution);
        fluidCS.SetInt("_TotalCells", totalCells);
        fluidCS.SetFloat("_CellSize", cellSize);

        fluidCS.SetFloat("_ParticleRadius", particleRadius);
        fluidCS.SetFloat("_SmoothingRadius", particleRadius * 2f);
        float particleDiameter = particleRadius * 2f;
        fluidCS.SetInt("_UsePrimaryVoxelVolume", usePrimaryVoxelVolume ? 1 : 0);
        fluidCS.SetInt("_PrimaryVoxelLayerCount", PrimaryVoxelLayerCount);
        fluidCS.SetFloat("_PrimaryVoxelHeight", Mathf.Max(primaryVoxelHeight, 0.05f));
        fluidCS.SetFloat("_PrimaryVoxelFlowFraction", Mathf.Clamp(primaryVoxelFlowFraction, 0.01f, 0.9f));
        fluidCS.SetInt("_PrimaryVoxelPressureOnlyPass", 0);
        fluidCS.SetFloat("_PrimaryVoxelLateralFlow", Mathf.Max(primaryVoxelLateralFlow, 0f));
        fluidCS.SetFloat("_PrimaryVoxelMomentumAdvection", Mathf.Clamp01(primaryVoxelMomentumAdvection));
        bool breachApertureOpen = usePrimaryVoxelBreachInflow && spawnPoint != null;
        Vector3 breachAperturePosition = breachApertureOpen ? spawnPoint.position : Vector3.zero;
        Vector3 breachApertureDirection = breachApertureOpen && spawnPoint.forward.sqrMagnitude > 0.0001f
            ? spawnPoint.forward.normalized : Vector3.forward;
        breachApertureDirection.y = 0f;
        if (breachApertureDirection.sqrMagnitude <= 0.0001f)
            breachApertureDirection = Vector3.forward;
        else
            breachApertureDirection.Normalize();
        fluidCS.SetInt("_PrimaryVoxelBreachApertureEnabled", breachApertureOpen ? 1 : 0);
        fluidCS.SetVector("_PrimaryVoxelBreachAperturePositionHeight", new Vector4(
            breachAperturePosition.x, breachAperturePosition.y, breachAperturePosition.z,
            Mathf.Max(primaryVoxelBreachHeight, 0.05f)));
        fluidCS.SetVector("_PrimaryVoxelBreachApertureDirectionWidth", new Vector4(
            breachApertureDirection.x, breachApertureDirection.y, breachApertureDirection.z,
            Mathf.Max(primaryVoxelBreachWidth, 0.05f)));
        fluidCS.SetFloat("_PrimaryVoxelBasinFlowMultiplier",
            Mathf.Clamp(primaryVoxelBasinFlowMultiplier, 1f, 12f));
        fluidCS.SetInt("_PrimaryVoxelEnableWeirOverflow", enablePrimaryVoxelWeirOverflow ? 1 : 0);
        fluidCS.SetFloat("_PrimaryVoxelWeirOverflowFlow", Mathf.Max(primaryVoxelWeirOverflowFlow, 0f));
        fluidCS.SetFloat("_PrimaryVoxelGravityFlow", Mathf.Max(primaryVoxelGravityFlow, 0f));
        fluidCS.SetFloat("_PrimaryVoxelCompressionFlow", Mathf.Max(primaryVoxelCompressionFlow, 0f));
        PushPrimaryVoxelHatchRiserVolumes();
        fluidCS.SetFloat("_PrimaryVoxelMaxFill", Mathf.Clamp(primaryVoxelMaxFill, 1f, 4f));
        fluidCS.SetInt("_PrimaryVoxelInjectionFootprint", Mathf.Clamp(primaryVoxelInjectionFootprintRadius, 0, 2));
        fluidCS.SetFloat("_PrimaryVoxelDivergenceCorrection",
            Mathf.Clamp(primaryVoxelDivergenceCorrection, 0f, 0.10f));
        fluidCS.SetFloat("_PrimaryVoxelParticlePICBlend", Mathf.Clamp01(primaryVoxelParticlePICBlend));
        fluidCS.SetFloat("_PrimaryVoxelMaxGridSpeed",
            Mathf.Max(primaryVoxelMaxGridSpeed, 0.1f));
        fluidCS.SetFloat("_PrimaryVoxelMaxParticleDeltaVelocity",
            Mathf.Max(primaryVoxelMaxParticleDeltaVelocity, 0.1f));
        fluidCS.SetFloat("_PrimarySurfaceRecycleHeight",
            Mathf.Max(primarySurfaceRecycleHeight, particleDiameter));
        bool hasOverflowProbe = usePrimaryVoxelOverflowSillOverride || primaryVoxelOverflowProbe != null;
        float overflowSill = usePrimaryVoxelOverflowSillOverride
            ? primaryVoxelOverflowSillHeight
            : (primaryVoxelOverflowProbe != null ? primaryVoxelOverflowProbe.position.y : boundsMin.y);
        fluidCS.SetInt("_PrimaryVoxelOverflowDiagnosticsEnabled", hasOverflowProbe ? 1 : 0);
        fluidCS.SetFloat("_PrimaryVoxelOverflowDiagnosticsSill", overflowSill);
        fluidCS.SetInt("_PrimarySurfacePbfLayers", Mathf.Clamp(primarySurfacePbfLayers, 1, 3));
        fluidCS.SetInt("_PrimarySurfaceSlotsPerColumn",
            Mathf.Clamp(primarySurfaceSlotsPerColumn, 1, 3));
        fluidCS.SetInt("_PrimarySurfaceMinimumVerticalGapLayers",
            Mathf.Clamp(primarySurfaceMinimumVerticalGapLayers, 1, 4));
        fluidCS.SetFloat("_PrimarySurfaceTopBandHeight",
            Mathf.Max(primarySurfaceTopBandHeight, particleRadius * 0.5f));
        fluidCS.SetFloat("_PrimaryVoxelPbfSkinDepth", Mathf.Clamp(
            Mathf.Clamp(primarySurfacePbfLayers, 1, 3) * particleDiameter,
            particleDiameter, particleDiameter * 3f));
        fluidCS.SetInt("_UsePrimarySurfaceWaves", enablePrimarySurfaceWaves && usePrimaryVoxelVolume ? 1 : 0);
        fluidCS.SetFloat("_PrimarySurfaceWaveMinimumDepth", Mathf.Max(primarySurfaceWaveMinimumDepth, 0.001f));
        fluidCS.SetFloat("_PrimarySurfaceWaveTravelSpeedScale",
            Mathf.Max(primarySurfaceWaveTravelSpeedScale, 0.01f));
        fluidCS.SetFloat("_PrimarySurfaceWaveMaxFroude",
            Mathf.Clamp(primarySurfaceWaveMaxFroude, 0.25f, 1.5f));
        fluidCS.SetInt("_PrimarySurfaceWaveApplyMeanCorrection",
            primarySurfaceWaveMeanReprojectionPending ? 1 : 0);
        fluidCS.SetFloat("_PrimarySurfaceWaveMeanCorrectionBaseYTolerance",
            Mathf.Max(primaryVoxelHeight * 0.55f, 0.02f));
        primarySurfaceWaveEffectiveDamping = Mathf.Max(primarySurfaceWaveDamping, 0f) *
            Mathf.Clamp(primarySurfaceWaveDampingMultiplier, 0.05f, 1f);
        fluidCS.SetFloat("_PrimarySurfaceWaveDamping", primarySurfaceWaveEffectiveDamping);
        fluidCS.SetFloat("_PrimarySurfaceWaveMaxDisplacement", Mathf.Max(primarySurfaceWaveMaxDisplacement, 0.001f));
        fluidCS.SetFloat("_PrimarySurfaceWavePressureForcing", Mathf.Max(primarySurfaceWavePressureForcing, 0f));
        fluidCS.SetFloat("_PrimarySurfaceWaveFluxForcing", Mathf.Max(primarySurfaceWaveFluxForcing, 0f));
        fluidCS.SetFloat("_PrimarySurfaceWaveColumnVelocityForcing",
            Mathf.Max(primarySurfaceWaveColumnVelocityForcing, 0f));
        fluidCS.SetFloat("_PrimarySurfaceWaveHatchForcing", Mathf.Max(primarySurfaceWaveHatchForcing, 0f));
        fluidCS.SetInt("_UsePrimarySurfaceFlowMap",
            enablePrimarySurfaceFlowMap && enablePrimarySurfaceWaves && usePrimaryVoxelVolume ? 1 : 0);
        fluidCS.SetInt("_PrimarySurfaceFlowResolutionScale",
            Mathf.Clamp(primarySurfaceFlowResolutionScale, 1, 4));
        fluidCS.SetInt("_PrimarySurfaceFlowCountX", PrimarySurfaceFlowCountX);
        fluidCS.SetInt("_PrimarySurfaceFlowCountZ", PrimarySurfaceFlowCountZ);
        fluidCS.SetFloat("_PrimarySurfaceFlowDamping", Mathf.Max(primarySurfaceFlowDamping, 0f));
        fluidCS.SetFloat("_PrimarySurfaceFlowFoamDecay", Mathf.Max(primarySurfaceFlowFoamDecay, 0f));
        fluidCS.SetFloat("_PrimarySurfaceFlowResidualInjection",
            Mathf.Max(primarySurfaceFlowResidualInjection, 0f));
        fluidCS.SetFloat("_PrimarySurfaceFlowPbfBlend",
            Mathf.Clamp01(primarySurfaceFlowPbfBlend));
        fluidCS.SetFloat("_PrimarySurfaceFlowMaxResidualSpeed",
            Mathf.Max(primarySurfaceFlowMaxResidualSpeed, 0.01f));
        fluidCS.SetInt("_PrimarySurfaceFlowVisualiseDye",
            primarySurfaceFlowVisualiseDye ? 1 : 0);

        // Fluid material behaviour.
        fluidCS.SetFloat("_RestDensity", restDensity);
        fluidCS.SetFloat("_RestDensityShallow", shallowRestDensity);
        fluidCS.SetFloat("_RestDensityBlendDepth", fullRestDensityDepthLayers * (particleRadius * 2f));
        fluidCS.SetFloat("_Stiffness", stiffness);
        fluidCS.SetFloat("_PressureCompressionBoost", pressureCompressionBoost);
        fluidCS.SetFloat("_PressureBoostStartRatio", pressureBoostStartRatio);
        fluidCS.SetFloat("_PressureBoostExponent", pressureBoostExponent);
        fluidCS.SetFloat("_PressureBoostMaxMultiplier", pressureBoostMaxMultiplier);
        // Fixed bands take precedence over the retained-particle reservoir.
        // They keep only the configured local PBF skin while the continuous
        // bulk field owns the deep body and its lateral transport.
        bool usingPrimaryVoxel = usePrimaryVoxelVolume;
        bool usingFixedBulkBands = !usingPrimaryVoxel && useFixedBulkBands && solverMode == FluidSolverMode.PBF_XPBD;
        bool usingContinuousBulkFlow = usingPrimaryVoxel || (usingFixedBulkBands && enableContinuousBulkFlow);
        bool usingDormantParticleReservoir = !usingFixedBulkBands && useDormantParticleReservoir &&
            enableHybridDormantBuffer && solverMode == FluidSolverMode.PBF_XPBD;
        float fixedSurfaceY = Mathf.Clamp(fixedBulkSurfaceY, boundsMin.y, boundsMax.y);
        float fixedTransitionHeight = Mathf.Clamp(fixedBulkTransitionHeight, 0.05f,
            Mathf.Max(boundsMax.y - boundsMin.y, 0.05f));
        float fixedActivePbfDepth = GetFixedBulkActiveSkinDepth();
        float fixedBulkCutoffY = Mathf.Clamp(fixedSurfaceY - fixedActivePbfDepth,
            boundsMin.y, boundsMax.y);
        float effectiveVerticalSupportStrength = (useBulkHeightmap || usingFixedBulkBands) &&
            !usingDormantParticleReservoir && disableVerticalSupportWhenBulkHeightmap
            ? 0f
            : verticalSupportStrength;
        fluidCS.SetFloat("_VerticalSupportStrength", effectiveVerticalSupportStrength);
        fluidCS.SetFloat("_VerticalSupportColumnRadiusScale", verticalSupportColumnRadiusScale);
        fluidCS.SetFloat("_MaxVerticalSupportVelocityChangePerStep", maxVerticalSupportVelocityChangePerStep);
        // In retained-particle mode the old bulk grid must not remain a second
        // mass/support source beneath the same water. Existing bulk entries
        // are restored to dormant particles by classification and the grid is
        // cleared by this step's bulk-grid pass.
        bool useBulkHeightmapThisStep = usingPrimaryVoxel || (useBulkHeightmap || usingFixedBulkBands) &&
            !usingDormantParticleReservoir;
        fluidCS.SetInt("_UseBulkHeightmap", useBulkHeightmapThisStep ? 1 : 0);
        fluidCS.SetInt("_UseFixedBulkBands", usingFixedBulkBands ? 1 : 0);
        fluidCS.SetFloat("_FixedBulkSurfaceY", fixedSurfaceY);
        fluidCS.SetFloat("_FixedBulkTransitionHeight", fixedTransitionHeight);
        fluidCS.SetFloat("_FixedBulkCutoffY", fixedBulkCutoffY);
        fluidCS.SetFloat("_FixedActivePbfDepth", fixedActivePbfDepth);
        fluidCS.SetFloat("_FixedBulkMinimumActiveAge", Mathf.Max(fixedBulkMinimumActiveAge, 0f));
        // Continuous cell flux owns lateral transport. Disable the earlier
        // entry-time token walk so hidden bookkeeping cannot disturb support.
        fluidCS.SetInt("_SpreadNewFixedBulkTokens",
            spreadNewFixedBulkTokens && !usingContinuousBulkFlow ? 1 : 0);
        fluidCS.SetInt("_FixedBulkFeedSpreadHops", Mathf.Clamp(fixedBulkFeedSpreadHops, 1, 4));
        fluidCS.SetInt("_UseContinuousBulkFlow", usingContinuousBulkFlow ? 1 : 0);
        fluidCS.SetFloat("_BulkFlowSpeed", Mathf.Max(bulkFlowSpeed, 0f));
        fluidCS.SetFloat("_BulkFlowMaxFractionPerStep",
            Mathf.Clamp(bulkFlowMaxFractionPerStep, 0.005f, 0.25f));
        fluidCS.SetFloat("_BulkFlowMinimumHead", Mathf.Max(bulkFlowMinimumHead, 0f));
        fluidCS.SetInt("_BulkFlowPhase", simulationStepCounter);
        fluidCS.SetInt("_EnableBulkActivityWake", enableBulkActivityWake && usingContinuousBulkFlow ? 1 : 0);
        fluidCS.SetFloat("_BulkWakeActivityThreshold", Mathf.Max(bulkWakeActivityThreshold, 0f));
        fluidCS.SetFloat("_BulkActivityDecayPerSecond", Mathf.Max(bulkActivityDecayPerSecond, 0f));
        fluidCS.SetFloat("_BulkWakeDepth", Mathf.Max(bulkWakeDepth, particleDiameter));
        fluidCS.SetInt("_MaxBulkWakesPerStep", Mathf.Max(maxBulkWakesPerStep, 0));
        fluidCS.SetInt("_MaxBulkWakesPerCell", Mathf.Max(maxBulkWakesPerCell, 0));
        fluidCS.SetInt("_BulkTopologyVerticalSamples",
            Mathf.Clamp(bulkTopologyVerticalSamples, 2, 16));
        fluidCS.SetInt("_EnableHybridDormantBuffer", enableHybridDormantBuffer ? 1 : 0);
        fluidCS.SetInt("_UseDormantParticleReservoir", usingDormantParticleReservoir ? 1 : 0);
        fluidCS.SetInt("_DormantActiveSurfaceLayers", Mathf.Max(dormantActiveSurfaceLayers, 1));
        fluidCS.SetFloat("_DormantSpeedAverageTime", Mathf.Max(dormantSpeedAverageTime, 0.01f));
        fluidCS.SetFloat("_DormantPbfSupportWeight", Mathf.Max(dormantPbfSupportWeight, 0f));
        fluidCS.SetFloat("_DormantExposureProbeDistance",
            Mathf.Max(particleDiameter * dormantExposureProbeDiameterScale, particleRadius));
        fluidCS.SetInt("_WakeDormantAtExposedFaces", wakeDormantAtExposedFaces ? 1 : 0);
        fluidCS.SetInt("_DormantExposureProbeStride", Mathf.Clamp(dormantExposureProbeStride, 1, 8));
        fluidCS.SetInt("_DormantExposureProbePhase", simulationStepCounter);
        int effectiveSurfaceLayers = usingDormantParticleReservoir
            ? Mathf.Max(dormantActiveSurfaceLayers, 1)
            : (useLocalBulkSafety
            ? Mathf.Max(sphSurfaceLayers, minBulkSafetySurfaceLayers, 3)
            : Mathf.Max(1, sphSurfaceLayers));
        float layerDepthFromParticles = effectiveSurfaceLayers * particleDiameter;
        float pbfLayerDepth = usingFixedBulkBands
            ? fixedActivePbfDepth
            : (usingDormantParticleReservoir
            ? layerDepthFromParticles
            : (useDepthBasedBulkAbsorption
            ? Mathf.Max(pbfLayerDepthMeters, particleDiameter)
            : layerDepthFromParticles));
        fluidCS.SetFloat("_SPHBandDepth", pbfLayerDepth);
        fluidCS.SetFloat("_MinimumBulkStorageDepth", Mathf.Max(minimumBulkStorageDepthMeters, 0f));
        fluidCS.SetFloat("_BulkSurfaceRiseSpeed", Mathf.Max(bulkSurfaceRiseSpeed, 0f));
        // GPUParticle instances use unit inverse mass, so a particle's
        // mass-consistent volume is mass / rest density. The former d^3
        // estimate made one token raise a 64x64 cell by a full diameter,
        // over-counting this project's PBF mass by about 2.44x.
        float bulkRestDensity = solverMode == FluidSolverMode.PBF_XPBD
            ? shallowRestDensity * Mathf.Max(pbfRestDensityScale, 0.0001f)
            : restDensity;
        float bulkParticleVolume = (1f / Mathf.Max(bulkRestDensity, 0.0001f)) *
            Mathf.Max(bulkParticleVolumeScale, 0.01f);
        bulkParticleVolumeForDiagnostics = bulkParticleVolume;
        fluidCS.SetFloat("_BulkParticleVolume", bulkParticleVolume);
        fluidCS.SetFloat("_VoxelRestDensity", Mathf.Max(restDensity, 0.0001f));
        float effectiveBulkVoxelHeight = usePrimaryVoxelVolume
            ? Mathf.Max(primaryVoxelHeight, 0.05f)
            : (bulkVoxelHeight > 0f ? bulkVoxelHeight : particleDiameter);
        float bulkCellSizeX = Mathf.Max((boundsMax.x - boundsMin.x) / BulkGridCountX, 0.0001f);
        float bulkCellSizeZ = Mathf.Max((boundsMax.z - boundsMin.z) / BulkGridCountZ, 0.0001f);
        float bulkCellArea = bulkCellSizeX * bulkCellSizeZ;
        float voxelVolume = bulkCellArea * Mathf.Max(effectiveBulkVoxelHeight, 0.0001f);
        int derivedVoxelTokenCap = Mathf.Max(1, Mathf.CeilToInt(voxelVolume / Mathf.Max(bulkParticleVolume, 0.000001f)));
        fluidCS.SetInt("_BulkGridCountX", BulkGridCountX);
        fluidCS.SetInt("_BulkGridCountZ", BulkGridCountZ);
        fluidCS.SetFloat("_BulkCellArea", bulkCellArea);
        fluidCS.SetInt("_UseBulkVoxels", (useBulkVoxels || usingFixedBulkBands) ? 1 : 0);
        fluidCS.SetInt("_BulkVoxelLayerCount", usePrimaryVoxelVolume
            ? PrimaryVoxelLayerCount : Mathf.Max(bulkVoxelLayerCount, 1));
        fluidCS.SetFloat("_BulkVoxelHeight", Mathf.Max(effectiveBulkVoxelHeight, 0.0001f));
        fluidCS.SetInt("_BulkVoxelTokensPerCell", bulkVoxelTokensPerCell > 0 ? bulkVoxelTokensPerCell : derivedVoxelTokenCap);
        int effectiveMicroScale = Mathf.Clamp(microVoxelScale, 1, 3);
        int microCountX = Mathf.Max(BulkGridCountX * effectiveMicroScale, 1);
        int microCountZ = Mathf.Max(BulkGridCountZ * effectiveMicroScale, 1);
        int microLayerCount = Mathf.Max(bulkVoxelLayerCount * effectiveMicroScale, 1);
        float microHeight = Mathf.Max(effectiveBulkVoxelHeight / effectiveMicroScale, 0.0001f);
        float microCellArea = Mathf.Max(bulkCellArea / (effectiveMicroScale * effectiveMicroScale), 0.000001f);
        float microCellVolume = microCellArea * microHeight;
        int derivedMicroTokenCap = Mathf.Max(1, Mathf.CeilToInt(
            microCellVolume / Mathf.Max(bulkParticleVolume, 0.000001f)));
        fluidCS.SetInt("_MicroGridCountX", microCountX);
        fluidCS.SetInt("_MicroGridCountZ", microCountZ);
        fluidCS.SetInt("_MicroLayerCount", microLayerCount);
        fluidCS.SetInt("_MicroScale", effectiveMicroScale);
        fluidCS.SetInt("_MicroVoxelCount", microCountX * microCountZ * microLayerCount);
        fluidCS.SetFloat("_MicroVoxelHeight", microHeight);
        fluidCS.SetFloat("_MicroVoxelCellArea", microCellArea);
        fluidCS.SetInt("_MicroVoxelTokensPerCell", derivedMicroTokenCap);
        fluidCS.SetFloat("_MicroTemporalResponse", Mathf.Clamp01(microVoxelTemporalResponse));        float bulkFraction = liveParticles > 0 ? (float)bulkParticleCount / liveParticles : 0f;
        float effectiveMaxBulkFraction = Mathf.Clamp(maxBulkParticleFraction, 0f, 0.95f);
        bool allowBulkConversion = usingFixedBulkBands || (!usingDormantParticleReservoir &&
            (!useBulkHeightmap || bulkFraction < effectiveMaxBulkFraction));
        fluidCS.SetInt("_AllowBulkConversion", allowBulkConversion ? 1 : 0);
        fluidCS.SetInt("_EnableBulkAbsorption", (enableBulkAbsorption || usingFixedBulkBands) && !usingDormantParticleReservoir ? 1 : 0);
        // A fixed-band token is intentionally not immediately rehydrated;
        // lower-opening rehydration will be a separate, local-surface pass.
        fluidCS.SetInt("_EnableBulkRespawn", enableBulkRespawnFromExposedCells &&
            !usingDormantParticleReservoir && usingContinuousBulkFlow ? 1 : 0);
        fluidCS.SetInt("_RequireSettledBeforeBulk", usingFixedBulkBands ? 0 : (requireSettledBeforeBulk ? 1 : 0));
        fluidCS.SetInt("_UseDepthBasedBulkAbsorption", usingFixedBulkBands ? 0 : (useDepthBasedBulkAbsorption ? 1 : 0));
        fluidCS.SetFloat("_DeepBulkCaptureDepth", Mathf.Max(deepBulkCaptureDepthMeters, particleDiameter));
        fluidCS.SetFloat("_BulkEnterSpeedThreshold", Mathf.Max(bulkEnterSpeedThreshold, 0.0001f));
        fluidCS.SetFloat("_BulkEnterPositionChangeThreshold", Mathf.Max(bulkEnterPositionChangeThreshold, 0.0001f));
        fluidCS.SetFloat("_BulkEnterDelay", Mathf.Max(bulkEnterDelay, 0f));
        fluidCS.SetInt("_UseBulkStabilityWindow", useBulkStabilityWindow ? 1 : 0);
        fluidCS.SetFloat("_BulkStableDuration", Mathf.Max(bulkStableDuration, 0f));
        fluidCS.SetFloat("_BulkStableDensityTolerance", Mathf.Clamp(bulkStableDensityTolerance, 0.01f, 0.5f));
        fluidCS.SetInt("_UseBulkSpeedForStableWindow", useBulkSpeedForStableWindow ? 1 : 0);
        fluidCS.SetFloat("_BulkCompressedNeighbourRadius", Mathf.Max(particleRadius * 2f * bulkCompressedNeighbourRadiusScale, particleRadius));
        fluidCS.SetInt("_RequireNoCompressedNeighboursForBulk", requireNoCompressedNeighboursForBulk ? 1 : 0);
        fluidCS.SetInt("_MaxBulkAbsorbsPerStep", Mathf.Max(maxBulkAbsorbsPerStep, 0));
        fluidCS.SetInt("_MaxBulkAbsorbsPerCellPerStep", Mathf.Max(maxBulkAbsorbsPerCellPerStep, 1));
        fluidCS.SetInt("_BulkTransitionLayers", Mathf.Max(bulkTransitionLayers, 1));
        fluidCS.SetFloat("_BulkTransitionHysteresis", Mathf.Max(particleRadius * bulkTransitionHysteresisRadiusScale, 0f));
        fluidCS.SetInt("_MaxBulkRespawnsPerStep", Mathf.Max(maxBulkRespawnsPerStep, 0));
        fluidCS.SetInt("_MaxBulkRespawnsPerCellPerStep", Mathf.Max(maxBulkRespawnsPerCellPerStep, 0));
        fluidCS.SetInt("_BulkRespawnActiveCountThreshold", Mathf.Max(bulkRespawnActiveCountThreshold, 0));
        fluidCS.SetInt("_BulkRespawnMinimumStoredParticles", Mathf.Max(bulkRespawnMinimumStoredParticles, 1));
        float mainSurfaceY = surfaceTileRenderer != null ? surfaceTileRenderer.mainBodySurfaceLevel : estimatedSurfaceLevel;
        fluidCS.SetInt("_BulkRespawnOnlyCreatesSurface", bulkRespawnOnlyCreatesSurface ? 1 : 0);
        fluidCS.SetFloat("_BulkRespawnMainSurfaceY", mainSurfaceY);
        fluidCS.SetFloat("_BulkRespawnSurfaceDrop", Mathf.Max(bulkRespawnSurfaceDrop, particleRadius));
        fluidCS.SetInt("_UseLocalBulkSafety", useLocalBulkSafety ? 1 : 0);
        fluidCS.SetInt("_MinBulkActiveTileParticles", Mathf.Max(minBulkActiveTileParticles, 1));
        fluidCS.SetInt("_MinBulkActiveBandParticles", Mathf.Max(minBulkActiveBandParticles, 1));
        fluidCS.SetInt("_MinBulkLowerBandParticles", Mathf.Max(minBulkLowerBandParticles, 0));
        fluidCS.SetFloat("_MaxLocalBulkFraction", Mathf.Clamp(maxLocalBulkParticleFraction, 0.1f, 0.9f));
        float effectiveBulkSupportStrength = (useBulkHeightmap || usingFixedBulkBands) &&
            enableBulkSupportForce && !usingDormantParticleReservoir
            ? bulkSupportStrength
            : 0f;
        fluidCS.SetFloat("_BulkSupportStrength", effectiveBulkSupportStrength);
        fluidCS.SetFloat("_BulkSupportBand", Mathf.Max(particleDiameter * bulkSupportBandDiameterScale, particleRadius * 0.5f));
        fluidCS.SetFloat("_MaxBulkSupportVelocityChangePerStep", maxBulkSupportVelocityChangePerStep);
        fluidCS.SetFloat("_Viscosity", viscosity);
        fluidCS.SetFloat("_Cohesion", cohesion);
        fluidCS.SetInt("_UseAdaptiveForces", useAdaptiveForces ? 1 : 0);
        fluidCS.SetFloat("_AdaptiveSpeedReference", Mathf.Max(adaptiveSpeedReference, 0.0001f));
        fluidCS.SetFloat("_PressureSpeedCompressionBoost", pressureSpeedCompressionBoost);
        fluidCS.SetFloat("_ViscosityRelativeVelocityBoost", viscosityRelativeVelocityBoost);
        fluidCS.SetFloat("_ViscosityCompressionBoost", viscosityCompressionBoost);
        fluidCS.SetFloat("_CohesionSpeedDrop", cohesionSpeedDrop);
        fluidCS.SetFloat("_MaxForceVelocityChangePerStep", maxForceVelocityChangePerStep);
        fluidCS.SetInt("_UsePBF", solverMode == FluidSolverMode.PBF_XPBD ? 1 : 0);
        fluidCS.SetInt("_PBFIterations", Mathf.Max(pbfIterations, 1));
        fluidCS.SetFloat("_PBFRestDensityScale", Mathf.Max(pbfRestDensityScale, 0.0001f));
        fluidCS.SetFloat("_PBFCompliance", Mathf.Max(pbfCompliance, 0f));
        fluidCS.SetFloat("_PBFCorrectionStrength", Mathf.Clamp01(pbfCorrectionStrength));
        fluidCS.SetFloat("_PBFMaxCorrectionPerIteration", Mathf.Max(pbfMaxCorrectionPerIteration, 0f));
        fluidCS.SetFloat("_PBFArtificialPressure", Mathf.Max(pbfArtificialPressure, 0f));
        fluidCS.SetFloat("_PBFArtificialPressureRadius", Mathf.Clamp(pbfArtificialPressureRadius, 0.05f, 0.8f));
        fluidCS.SetFloat("_PBFVelocityDamping", Mathf.Clamp01(pbfVelocityDamping));
        fluidCS.SetFloat("_PBFViscosity", Mathf.Clamp01(pbfViscosity));
        fluidCS.SetFloat("_PBFMaxProjectionVelocity", Mathf.Max(pbfMaxProjectionVelocity, 0f));
        fluidCS.SetFloat("_PBFBulkGhostWeight", Mathf.Max(pbfBulkGhostWeight, 0f));
        fluidCS.SetFloat("_MergeSplitStrength", mergeSplitStrength);
        fluidCS.SetFloat("_MergeVelocityMatching", mergeVelocityMatching);
        fluidCS.SetFloat("_MergeOuterStart", mergeOuterStart);
        fluidCS.SetFloat("_SplitReleaseSpeed", splitReleaseSpeed);
        fluidCS.SetFloat("_ThinNeckRelease", thinNeckRelease);
        fluidCS.SetFloat("_BoundsBounceDamping", boundsBounceDamping);
        fluidCS.SetFloat("_BoundsImpactSpeedLoss", boundsImpactSpeedLoss);
        fluidCS.SetFloat("_WallContactSpeedLossPerSecond", wallContactSpeedLossPerSecond);
        fluidCS.SetFloat("_ShipHullSurfaceFrictionPerSecond", shipHullSurfaceFrictionPerSecond);
        fluidCS.SetFloat("_ShipHullNormalBounceLossPerSecond", shipHullNormalBounceLossPerSecond);
        fluidCS.SetInt("_CollisionSubsteps", collisionSubsteps);
        fluidCS.SetInt("_NumSolidColliders", solidColliderCount);

        // Active/dormant state thresholds.
        fluidCS.SetFloat("_WakeSpeedThreshold", wakeSpeedThreshold);
        fluidCS.SetFloat("_SleepSpeedThreshold", sleepSpeedThreshold);
        fluidCS.SetFloat("_DormantSettledSpeedThreshold", dormantSettledSpeedThreshold);
        fluidCS.SetFloat("_SleepDelay", sleepDelay);
        fluidCS.SetFloat("_ActiveHeightThreshold", activeHeightThreshold);
        fluidCS.SetFloat("_DormantSurfaceClearance", dormantSurfaceClearance);
        fluidCS.SetFloat("_DormantPositionChangeThreshold", dormantPositionChangeThreshold);
        fluidCS.SetFloat("_DormantNeighborWakeRadius", dormantNeighborWakeRadius);
        fluidCS.SetFloat("_DormantNeighborWakeSpeedThreshold", dormantNeighborWakeSpeedThreshold);
        fluidCS.SetFloat("_InflowWakeRadius", inflowWakeRadius);
        fluidCS.SetFloat("_DormantDamping", dormantDamping);
        // Inflow position is sent so the compute shader can wake particles near
        // the entry point even if they are deep in the water body.
        Vector3 spawnPos = spawnPoint != null ? spawnPoint.position : Vector3.zero;
        fluidCS.SetVector("_SpawnPoint", spawnPos);

        if (boundaryBuffersSupported)
        {
            fluidCS.SetInt("_NumBoundaryParticles", 0);
            fluidCS.SetFloat("_BoundaryDensityScale", 1f);
            fluidCS.SetFloat("_BoundaryPressureScale", 1f);
        }
    }

    void UpdateRuntimeCounts()
    {
        liveParticles = PrimarySurfaceParticleCount;
        // Do not read GPU diagnostic buffers during the hybrid voxel/PBF sim.
        // Even asynchronous requests can contend with this large field on some
        // drivers; the diagnostics are optional and must never hitch gameplay.
        bool allowGpuStateReadback = enableStateReadback && !usePrimaryVoxelVolume;

        if (!allowGpuStateReadback || liveParticles <= 0)
        {
            activeParticleCount = usePrimaryVoxelVolume ? 0 : liveParticles;
            dormantParticleCount = 0;
            bulkParticleCount = 0;
            bulkSpawnedLastStep = 0;
            bulkAbsorbedLastStep = 0;
            bulkBelowBandCandidatesLastStep = 0;
            bulkStableCandidatesLastStep = 0;
            bulkLocalSafetyBlockedLastStep = 0;
            bulkSpeedBlockedLastStep = 0;
            bulkDensityBlockedLastStep = 0;
            bulkNeighbourBlockedLastStep = 0;
            bulkSweepBlockedLastStep = 0;
            bulkPositionBlockedLastStep = 0;
            bulkWaitingForStabilityLastStep = 0;
            bulkReadyToCommitLastStep = 0;
            estimatedSurfaceLevel = usePrimaryVoxelVolume
                ? primaryVoxelCurrentSurfaceHeight
                : boundsMin.y;
            estimatedActiveBandBottom = usePrimaryVoxelVolume
                ? primaryVoxelCurrentSurfaceHeight - activeHeightThreshold
                : boundsMin.y + activeHeightThreshold;
            outOfBoundsParticleCount = 0;
            insideDebugBoundsParticleCount = 0;
            outsideDebugBoundsParticleCount = 0;
            overlappingParticlePairCount = 0;
            averageOverlapDistance = 0f;
            averageCompressionPercent = 0f;
            worstOverlapDistance = 0f;
            worstCompressionPercent = 0f;
            overlapPairsAbove10Percent = 0;
            overlapPairsAbove25Percent = 0;
            overlapPairsAbove50Percent = 0;
            verticalOverlapPairCount = 0;
            averageVerticalOverlapDistance = 0f;
            averageVerticalCompressionPercent = 0f;
            worstVerticalOverlapDistance = 0f;
            worstVerticalCompressionPercent = 0f;
            estimatedParticleSpacingFromOverlap = 0f;
            estimatedParticlesToFillBoundsAtCurrentOverlap = 0;
            averageLiveDensity = 0f;
            averageLivePressure = 0f;
            averageDensityMinusRest = 0f;
            densityStandardDeviation = 0f;
            densityStdDevPercentOfAverage = 0f;
            densityStdDevPercentOfRest = 0f;
            averagePressureForceMagnitude = 0f;
            averageViscosityForceMagnitude = 0f;
            averageCohesionForceMagnitude = 0f;
            averageMergeSplitForceMagnitude = 0f;
            averagePressureDeltaVelocity = 0f;
            averageViscosityDeltaVelocity = 0f;
            averageCohesionDeltaVelocity = 0f;
            averageMergeSplitDeltaVelocity = 0f;
            maxPressureDeltaVelocity = 0f;
            maxViscosityDeltaVelocity = 0f;
            maxCohesionDeltaVelocity = 0f;
            maxMergeSplitDeltaVelocity = 0f;
            maxPressureForceMagnitude = 0f;
            maxViscosityForceMagnitude = 0f;
            maxCohesionForceMagnitude = 0f;
            maxMergeSplitForceMagnitude = 0f;
            averageActiveVelocity = Vector3.zero;
            averageActiveSpeed = 0f;
            return;
        }

        stateReadbackTimer += Time.deltaTime;
        if (stateReadbackTimer < stateReadbackInterval)
            return;

        stateReadbackTimer = 0f;

        GPUParticle[] cpuCopy = new GPUParticle[liveParticles];
        particleBuffer.GetData(cpuCopy, 0, 0, liveParticles);
        Vector4[] forceCopy = new Vector4[liveParticles];
        debugForceBuffer.GetData(forceCopy, 0, 0, liveParticles);
        if (bulkPhase2CounterBuffer != null)
        {
            int[] phase2Counters = new int[12];
            bulkPhase2CounterBuffer.GetData(phase2Counters);
            bulkSpawnedLastStep = phase2Counters[0];
            bulkAbsorbedLastStep = phase2Counters[1];
            bulkBelowBandCandidatesLastStep = phase2Counters[2];
            bulkStableCandidatesLastStep = phase2Counters[3];
            bulkLocalSafetyBlockedLastStep = phase2Counters[4];
            bulkSpeedBlockedLastStep = phase2Counters[5];
            bulkDensityBlockedLastStep = phase2Counters[6];
            bulkNeighbourBlockedLastStep = phase2Counters[7];
            bulkSweepBlockedLastStep = phase2Counters[8];
            bulkPositionBlockedLastStep = phase2Counters[9];
            bulkWaitingForStabilityLastStep = phase2Counters[10];
            bulkReadyToCommitLastStep = phase2Counters[11];
        }
        if (bulkWakeCounterBuffer != null)
        {
            int[] wakeCounter = new int[1];
            bulkWakeCounterBuffer.GetData(wakeCounter);
            bulkWokeLastStep = Mathf.Max(wakeCounter[0], 0);
        }
        bulkDiagnosticsReadbackTimer += Mathf.Max(stateReadbackInterval, 0.01f);
        if (bulkDiagnosticsReadbackTimer >= 1f)
        {
            bulkDiagnosticsReadbackTimer = 0f;
            UpdateBulkFlowDiagnostics();
        }

        int activeCount = 0;
        int bulkCount = 0;
        float maxY = boundsMin.y;
        int outOfBoundsCount = 0;
        int insideDebugBoundsCount = 0;
        int outsideDebugBoundsCount = 0;
        int overlapPairs = 0;
        float overlapSum = 0f;
        float maxOverlap = 0f;
        float particleDiameter = particleRadius * 2f;
        int overlap10 = 0;
        int overlap25 = 0;
        int overlap50 = 0;
        int verticalOverlapPairs = 0;
        float verticalOverlapSum = 0f;
        float maxVerticalOverlap = 0f;
        float verticalColumnRadius = Mathf.Max(particleRadius * 0.35f, 0.0001f);
        float verticalColumnRadiusSq = verticalColumnRadius * verticalColumnRadius;
        float densitySum = 0f;
        float densitySquaredSum = 0f;
        float pressureSum = 0f;
        float densityMinusRestSum = 0f;
        float pressureDeltaVelocitySum = 0f;
        float viscosityDeltaVelocitySum = 0f;
        float cohesionDeltaVelocitySum = 0f;
        float mergeSplitDeltaVelocitySum = 0f;
        float pressureDeltaVelocityMax = 0f;
        float viscosityDeltaVelocityMax = 0f;
        float cohesionDeltaVelocityMax = 0f;
        float mergeSplitDeltaVelocityMax = 0f;
        Vector3 velocitySum = Vector3.zero;
        float speedSum = 0f;
        Vector3 checkBoundsMin = useCustomDebugBounds ? debugBoundsMin : boundsMin;
        Vector3 checkBoundsMax = useCustomDebugBounds ? debugBoundsMax : boundsMax;

        for (int i = 0; i < liveParticles; i++)
        {
            if (cpuCopy[i].state == (int)ParticleState.Active)
            {
                activeCount++;
                float d = cpuCopy[i].density;
                float p = stiffness * Mathf.Max(d - restDensity, 0f);
                densitySum += d;
                densitySquaredSum += d * d;
                pressureSum += p;
                densityMinusRestSum += (d - restDensity);
                pressureDeltaVelocitySum += forceCopy[i].x;
                viscosityDeltaVelocitySum += forceCopy[i].y;
                cohesionDeltaVelocitySum += forceCopy[i].z;
                mergeSplitDeltaVelocitySum += forceCopy[i].w;
                pressureDeltaVelocityMax = Mathf.Max(pressureDeltaVelocityMax, forceCopy[i].x);
                viscosityDeltaVelocityMax = Mathf.Max(viscosityDeltaVelocityMax, forceCopy[i].y);
                cohesionDeltaVelocityMax = Mathf.Max(cohesionDeltaVelocityMax, forceCopy[i].z);
                mergeSplitDeltaVelocityMax = Mathf.Max(mergeSplitDeltaVelocityMax, forceCopy[i].w);
                velocitySum += cpuCopy[i].vel;
                speedSum += cpuCopy[i].vel.magnitude;
            }
            else if (cpuCopy[i].state == (int)ParticleState.Bulk)
            {
                bulkCount++;
            }

            // In the primary-volume path unused skin slots deliberately stay
            // in the Bulk state. They are not stored water and must not pollute
            // the legacy particle counts or bounds diagnostics.
            if (usePrimaryVoxelVolume && cpuCopy[i].state != (int)ParticleState.Active)
                continue;

            if (cpuCopy[i].pos.y > maxY)
                maxY = cpuCopy[i].pos.y;

            Vector3 pos = cpuCopy[i].pos;
            if (pos.x < boundsMin.x || pos.x > boundsMax.x ||
                pos.y < boundsMin.y || pos.y > boundsMax.y ||
                pos.z < boundsMin.z || pos.z > boundsMax.z)
            {
                outOfBoundsCount++;
            }

            if (pos.x < checkBoundsMin.x || pos.x > checkBoundsMax.x ||
                pos.y < checkBoundsMin.y || pos.y > checkBoundsMax.y ||
                pos.z < checkBoundsMin.z || pos.z > checkBoundsMax.z)
            {
                outsideDebugBoundsCount++;
            }
            else
            {
                insideDebugBoundsCount++;
            }
        }

        // Brute-force overlap check. This is intentionally low-frequency debug
        // work, not per-frame runtime logic. We cap the checked particle count
        // so the inspector remains useful without becoming too expensive.
        int maxCheckedParticles = Mathf.Min(liveParticles, 2048);
        for (int i = 0; i < maxCheckedParticles; i++)
        {
            Vector3 posA = cpuCopy[i].pos;
            for (int j = i + 1; j < maxCheckedParticles; j++)
            {
                float distance = Vector3.Distance(posA, cpuCopy[j].pos);
                float overlap = particleDiameter - distance;
                if (overlap > 0f)
                {
                    overlapPairs++;
                    overlapSum += overlap;
                    if (overlap > maxOverlap)
                        maxOverlap = overlap;

                    float compressionPercent = particleDiameter > 0f ? (overlap / particleDiameter) * 100f : 0f;
                    if (compressionPercent >= 10f) overlap10++;
                    if (compressionPercent >= 25f) overlap25++;
                    if (compressionPercent >= 50f) overlap50++;
                }

                float dx = posA.x - cpuCopy[j].pos.x;
                float dz = posA.z - cpuCopy[j].pos.z;
                float xzDistanceSq = dx * dx + dz * dz;
                if (xzDistanceSq <= verticalColumnRadiusSq)
                {
                    float verticalDistance = Mathf.Abs(posA.y - cpuCopy[j].pos.y);
                    float verticalOverlap = particleDiameter - verticalDistance;
                    if (verticalOverlap > 0f)
                    {
                        verticalOverlapPairs++;
                        verticalOverlapSum += verticalOverlap;
                        if (verticalOverlap > maxVerticalOverlap)
                            maxVerticalOverlap = verticalOverlap;
                    }
                }
            }
        }

        activeParticleCount = activeCount;
        bulkParticleCount = usePrimaryVoxelVolume ? 0 : bulkCount;
        dormantParticleCount = usePrimaryVoxelVolume ? 0 : Mathf.Max(liveParticles - activeCount - bulkCount, 0);
        if (usePrimaryVoxelVolume)
            liveParticles = activeCount;
        if (activeCount > 0)
        {
            averageLiveDensity = densitySum / activeCount;
            averageLivePressure = pressureSum / activeCount;
            averageDensityMinusRest = densityMinusRestSum / activeCount;
            float densityVariance = Mathf.Max(0f, densitySquaredSum / activeCount - averageLiveDensity * averageLiveDensity);
            densityStandardDeviation = Mathf.Sqrt(densityVariance);
            densityStdDevPercentOfAverage = Mathf.Abs(averageLiveDensity) > 0.0001f
                ? densityStandardDeviation / Mathf.Abs(averageLiveDensity) * 100f
                : 0f;
            densityStdDevPercentOfRest = Mathf.Abs(restDensity) > 0.0001f
                ? densityStandardDeviation / Mathf.Abs(restDensity) * 100f
                : 0f;
            averagePressureDeltaVelocity = pressureDeltaVelocitySum / activeCount;
            averageViscosityDeltaVelocity = viscosityDeltaVelocitySum / activeCount;
            averageCohesionDeltaVelocity = cohesionDeltaVelocitySum / activeCount;
            averageMergeSplitDeltaVelocity = mergeSplitDeltaVelocitySum / activeCount;
            maxPressureDeltaVelocity = pressureDeltaVelocityMax;
            maxViscosityDeltaVelocity = viscosityDeltaVelocityMax;
            maxCohesionDeltaVelocity = cohesionDeltaVelocityMax;
            maxMergeSplitDeltaVelocity = mergeSplitDeltaVelocityMax;
            float forceMagnitudeTime = Mathf.Max(currentSimulationDeltaTime, 0.000001f);
            averagePressureForceMagnitude = averagePressureDeltaVelocity / forceMagnitudeTime;
            averageViscosityForceMagnitude = averageViscosityDeltaVelocity / forceMagnitudeTime;
            averageCohesionForceMagnitude = averageCohesionDeltaVelocity / forceMagnitudeTime;
            averageMergeSplitForceMagnitude = averageMergeSplitDeltaVelocity / forceMagnitudeTime;
            maxPressureForceMagnitude = maxPressureDeltaVelocity / forceMagnitudeTime;
            maxViscosityForceMagnitude = maxViscosityDeltaVelocity / forceMagnitudeTime;
            maxCohesionForceMagnitude = maxCohesionDeltaVelocity / forceMagnitudeTime;
            maxMergeSplitForceMagnitude = maxMergeSplitDeltaVelocity / forceMagnitudeTime;
            averageActiveVelocity = velocitySum / activeCount;
            averageActiveSpeed = speedSum / activeCount;
        }
        else
        {
            averageLiveDensity = 0f;
            averageLivePressure = 0f;
            averageDensityMinusRest = 0f;
            densityStandardDeviation = 0f;
            densityStdDevPercentOfAverage = 0f;
            densityStdDevPercentOfRest = 0f;
            averagePressureForceMagnitude = 0f;
            averageViscosityForceMagnitude = 0f;
            averageCohesionForceMagnitude = 0f;
            averageMergeSplitForceMagnitude = 0f;
            averagePressureDeltaVelocity = 0f;
            averageViscosityDeltaVelocity = 0f;
            averageCohesionDeltaVelocity = 0f;
            averageMergeSplitDeltaVelocity = 0f;
            maxPressureDeltaVelocity = 0f;
            maxViscosityDeltaVelocity = 0f;
            maxCohesionDeltaVelocity = 0f;
            maxMergeSplitDeltaVelocity = 0f;
            maxPressureForceMagnitude = 0f;
            maxViscosityForceMagnitude = 0f;
            maxCohesionForceMagnitude = 0f;
            maxMergeSplitForceMagnitude = 0f;
            averageActiveVelocity = Vector3.zero;
            averageActiveSpeed = 0f;
        }
        estimatedSurfaceLevel = maxY;
        estimatedActiveBandBottom = estimatedSurfaceLevel - activeHeightThreshold;
        outOfBoundsParticleCount = outOfBoundsCount;
        insideDebugBoundsParticleCount = insideDebugBoundsCount;
        outsideDebugBoundsParticleCount = outsideDebugBoundsCount;
        overlappingParticlePairCount = overlapPairs;
        averageOverlapDistance = overlapPairs > 0 ? overlapSum / overlapPairs : 0f;
        averageCompressionPercent = particleDiameter > 0f ? (averageOverlapDistance / particleDiameter) * 100f : 0f;
        worstOverlapDistance = maxOverlap;
        worstCompressionPercent = particleDiameter > 0f ? (maxOverlap / particleDiameter) * 100f : 0f;
        overlapPairsAbove10Percent = overlap10;
        overlapPairsAbove25Percent = overlap25;
        overlapPairsAbove50Percent = overlap50;
        verticalOverlapPairCount = verticalOverlapPairs;
        averageVerticalOverlapDistance = verticalOverlapPairs > 0 ? verticalOverlapSum / verticalOverlapPairs : 0f;
        averageVerticalCompressionPercent = particleDiameter > 0f ? (averageVerticalOverlapDistance / particleDiameter) * 100f : 0f;
        worstVerticalOverlapDistance = maxVerticalOverlap;
        worstVerticalCompressionPercent = particleDiameter > 0f ? (maxVerticalOverlap / particleDiameter) * 100f : 0f;
        estimatedParticleSpacingFromOverlap = Mathf.Max(particleDiameter - averageOverlapDistance, 0.0001f);
        Vector3 fillSize = boundsMax - boundsMin;
        float fillVolume = Mathf.Max(fillSize.x * fillSize.y * fillSize.z, 0f);
        float effectiveParticleVolume = estimatedParticleSpacingFromOverlap * estimatedParticleSpacingFromOverlap * estimatedParticleSpacingFromOverlap;
        estimatedParticlesToFillBoundsAtCurrentOverlap = effectiveParticleVolume > 0f
            ? Mathf.CeilToInt(fillVolume / effectiveParticleVolume)
            : 0;

        forceLogTimer += stateReadbackInterval;
        if (forceLogTimer >= 10f && activeCount > 0)
        {
            forceLogTimer = 0f;
            Debug.Log(
                $"[FluidSim] Active: {activeCount} | Dormant: {dormantParticleCount} | Bulk: {bulkParticleCount} | AvgDensity: {averageLiveDensity:F2} | " +
                $"BulkSpawnedStep: {bulkSpawnedLastStep} | BulkAbsorbedStep: {bulkAbsorbedLastStep} | " +
                $"BulkBelowBand: {bulkBelowBandCandidatesLastStep} | BulkEligible: {bulkStableCandidatesLastStep} | " +
                $"BulkBlockedLocal: {bulkLocalSafetyBlockedLastStep} | BulkBlockedSpeed: {bulkSpeedBlockedLastStep} | " +
                $"BulkBlockedDensity: {bulkDensityBlockedLastStep} | BulkBlockedNeighbour: {bulkNeighbourBlockedLastStep} | " +
                $"BulkBlockedSweep: {bulkSweepBlockedLastStep} | BulkBlockedPosition: {bulkPositionBlockedLastStep} | " +
                $"BulkWaiting: {bulkWaitingForStabilityLastStep} | BulkReady: {bulkReadyToCommitLastStep} | " +
                $"BulkWetCells: {bulkWetCellCount} | BulkHeadGradient: {bulkMaxHeadGradient:F3} | " +
                $"BulkAcceptedFlux: {bulkAcceptedFluxVolume:F4} | BulkActivityCells: {bulkActiveActivityCellCount} | " +
                $"BulkWoke: {bulkWokeLastStep} | " +
                $"Avg(density-rest): {averageDensityMinusRest:F2} | AvgPressureScalar: {averageLivePressure:F3} | " +
                $"AvgPressureMag: {averagePressureForceMagnitude:F4} | MaxPressureMag: {maxPressureForceMagnitude:F4} | " +
                $"AvgViscosityMag: {averageViscosityForceMagnitude:F4} | MaxViscosityMag: {maxViscosityForceMagnitude:F4} | " +
                $"AvgCohesionMag: {averageCohesionForceMagnitude:F4} | MaxCohesionMag: {maxCohesionForceMagnitude:F4} | " +
                $"AvgMergeSplitMag: {averageMergeSplitForceMagnitude:F4} | MaxMergeSplitMag: {maxMergeSplitForceMagnitude:F4} | " +
                $"AvgPressureDeltaV: {averagePressureDeltaVelocity:F4} | MaxPressureDeltaV: {maxPressureDeltaVelocity:F4} | " +
                $"AvgViscosityDeltaV: {averageViscosityDeltaVelocity:F4} | MaxViscosityDeltaV: {maxViscosityDeltaVelocity:F4} | " +
                $"AvgCohesionDeltaV: {averageCohesionDeltaVelocity:F4} | MaxCohesionDeltaV: {maxCohesionDeltaVelocity:F4} | " +
                $"AvgMergeSplitDeltaV: {averageMergeSplitDeltaVelocity:F4} | MaxMergeSplitDeltaV: {maxMergeSplitDeltaVelocity:F4} | " +
                $"AvgVel: ({averageActiveVelocity.x:F3}, {averageActiveVelocity.y:F3}, {averageActiveVelocity.z:F3}) | " +
                $"AvgSpeed: {averageActiveSpeed:F3} | " +
                $"VoxelVolume: {voxelActualVolume:F4}/{voxelExpectedVolume:F4} | " +
                $"VoxelError: {voxelVolumeError:F4} | " +
                $"PrimaryInjected: {primaryVoxelCumulativeInjectedVolume:F4} | " +
                $"PrimaryAccepted: {primaryVoxelCumulativeAcceptedVolume:F4} | " +
                $"PrimaryOutflow: {primaryVoxelCumulativeBoundaryOutflow:F4} | " +
                $"PrimaryRejected: {primaryVoxelCumulativeRejectedVolume:F4} | " +
                $"PrimaryWetLayer: {primaryVoxelHighestWetLayer} | " +
                $"PrimaryMaxFill: {primaryVoxelMaxCellFill:F3} | " +
                $"PrimaryInflowAcceptedStep: {primaryVoxelAcceptedInflowLastStep:F4} | " +
                $"PrimaryInflowRejectedStep: {primaryVoxelRejectedInflowLastStep:F4} | " +
                $"PrimarySurface: {primaryVoxelCurrentSurfaceHeight:F4} | " +
                $"PrimaryFluxReq: {primaryVoxelRequestedHorizontalFluxLastStep:F4} | " +
                $"PrimaryFluxAccepted: {primaryVoxelAcceptedHorizontalFluxLastStep:F4} | " +
                $"PrimaryFluxPerSec: {primaryVoxelAcceptedHorizontalFluxPerSecond:F4} | " +
                $"PrimaryOpenHead: {primaryVoxelOpenFaceHeadGradient:F3} | " +
                $"PrimaryBlockedHead: {primaryVoxelBlockedFaceHeadGradient:F3} | " +
                $"PrimaryUpstreamSurface: {primaryVoxelUpstreamSurfaceHeight:F3} | " +
                $"PrimaryDownstreamSurface: {primaryVoxelDownstreamSurfaceHeight:F3} | " +
                $"PrimaryOverflowSill: {primaryVoxelDerivedOverflowSillHeight:F3} | " +
                $"PrimaryOvertopFaces: {primaryVoxelOvertoppingFaceCountLastStep} | " +
                $"PrimaryOvertopEvents: {primaryVoxelOvertoppingFaceEventsLastStep} | " +
                $"PrimaryOvertopVolumeStep: {primaryVoxelOvertoppingVolumeLastStep:F4} | " +
                $"PrimaryOvertopVolumePerSec: {primaryVoxelOvertoppingVolumePerSecond:F4} | " +
                $"PrimaryDeferred: {primaryVoxelDeferredInflowVolume:F4}");
        }
    }

    void UpdateVoxelVolumeReadback()
    {
        voxelReadbackTimer += Time.deltaTime;
        if (voxelReadbackTimer < 1.0f)
            return;

        voxelReadbackTimer = 0.0f;

        if (usePrimaryVoxelVolume && primaryVoxelDiagnosticsBuffer != null)
        {
            QueuePrimaryVoxelDiagnosticsReadback();
            return;
        }

        if (bulkVoxelCounterBuffer == null || bulkParticleVolumeForDiagnostics <= 0.0f)
            return;

        int[] counters = new int[3];
        bulkVoxelCounterBuffer.GetData(counters);
        voxelExpectedTokenCount = Mathf.Max(counters[0], 0) + Mathf.Max(counters[1], 0);
        voxelActualTokenCount = Mathf.Max(counters[2], 0);
        voxelExpectedVolume = voxelExpectedTokenCount * bulkParticleVolumeForDiagnostics;
        voxelActualVolume = voxelActualTokenCount * bulkParticleVolumeForDiagnostics;
        voxelVolumeError = voxelActualVolume - voxelExpectedVolume;
        }

    // These diagnostics used to call ComputeBuffer.GetData on the full voxel
    // field every second.  GetData is synchronous, so it forced the CPU to wait
    // for the GPU and caused a visible hitch.  Keep all readback asynchronous.
    void QueuePrimaryVoxelDiagnosticsReadback()
    {
        if (primaryVoxelDiagnosticsReadbackInFlight || primaryVoxelDiagnosticsBuffer == null)
            return;
        primaryVoxelDiagnosticsReadbackInFlight = true;
        AsyncGPUReadback.Request(primaryVoxelDiagnosticsBuffer, OnPrimaryVoxelDiagnosticsReadback);
    }

    void OnPrimaryVoxelDiagnosticsReadback(AsyncGPUReadbackRequest request)
    {
        primaryVoxelDiagnosticsReadbackInFlight = false;
        if (request.hasError || request.GetData<uint>().Length < primaryVoxelDiagnosticsReadback.Length)
            return;
        request.GetData<uint>().CopyTo(primaryVoxelDiagnosticsReadback);

        float inverseScale = 1f / PrimaryVoxelDiagnosticsScale;
        primaryVoxelCurrentVolume = primaryVoxelDiagnosticsReadback[0] * inverseScale;
        primaryVoxelHighestWetLayer = (int)primaryVoxelDiagnosticsReadback[1];
        primaryVoxelMaxCellFill = primaryVoxelDiagnosticsReadback[2] * inverseScale;
        primaryVoxelAcceptedInflowLastStep = primaryVoxelDiagnosticsReadback[3] * inverseScale;
        primaryVoxelRejectedInflowLastStep = primaryVoxelDiagnosticsReadback[4] * inverseScale;
        primaryVoxelCurrentSurfaceHeight = boundsMin.y + primaryVoxelDiagnosticsReadback[5] * inverseScale;
        primaryVoxelCumulativeAcceptedVolume = primaryVoxelDiagnosticsReadback[6] * inverseScale;
        primaryVoxelCumulativeRejectedVolume = primaryVoxelDiagnosticsReadback[7] * inverseScale;
        float primaryCellVolume = Mathf.Max(
            ((boundsMax.x - boundsMin.x) / BulkGridCountX) *
            ((boundsMax.z - boundsMin.z) / BulkGridCountZ) * Mathf.Max(primaryVoxelHeight, 0.0001f), 0.000001f);
        primaryVoxelRequestedHorizontalFluxLastStep = primaryVoxelDiagnosticsReadback[8] * inverseScale * primaryCellVolume;
        primaryVoxelAcceptedHorizontalFluxLastStep = primaryVoxelDiagnosticsReadback[9] * inverseScale * primaryCellVolume;
        primaryVoxelOvertoppingFaceEventsLastStep = (int)primaryVoxelDiagnosticsReadback[10];
        primaryVoxelOvertoppingVolumeLastStep = primaryVoxelDiagnosticsReadback[11] * inverseScale * primaryCellVolume;

        float inverseRuntimeScale = 1f / PrimaryVoxelRuntimeDiagnosticsScale;
        primaryVoxelWetCellCount = (int)primaryVoxelDiagnosticsReadback[12];
        primaryVoxelAverageWetFill = primaryVoxelWetCellCount > 0
            ? primaryVoxelDiagnosticsReadback[13] * inverseRuntimeScale / primaryVoxelWetCellCount : 0f;
        primaryVoxelAverageCompressionPressure = primaryVoxelWetCellCount > 0
            ? primaryVoxelDiagnosticsReadback[14] * inverseRuntimeScale / primaryVoxelWetCellCount : 0f;
        primaryVoxelMaximumCompressionPressure = primaryVoxelDiagnosticsReadback[15] * inverseRuntimeScale;
        primaryVoxelAverageGridSpeed = primaryVoxelWetCellCount > 0
            ? primaryVoxelDiagnosticsReadback[16] * inverseRuntimeScale / primaryVoxelWetCellCount : 0f;
        primaryVoxelMaximumGridSpeed = primaryVoxelDiagnosticsReadback[17] * inverseRuntimeScale;
        float finalPassSeconds = Mathf.Max(shaderDeltaTime /
            Mathf.Clamp(primaryVoxelFlowSubsteps, 1, 8), 0.000001f);
        primaryVoxelAcceptedHorizontalFluxLastPassPerSecond = primaryVoxelDiagnosticsReadback[18] *
            inverseRuntimeScale * primaryCellVolume / finalPassSeconds;
        primaryVoxelAcceptedVerticalFluxLastPassPerSecond = primaryVoxelDiagnosticsReadback[19] *
            inverseRuntimeScale * primaryCellVolume / finalPassSeconds;
        if (primaryVoxelDiagnosticsReadback[20] > 0u)
            primaryVoxelUpstreamSurfaceHeight = boundsMin.y + primaryVoxelDiagnosticsReadback[20] * inverseRuntimeScale;

        float stepSeconds = Mathf.Max(shaderDeltaTime, 0.000001f);
        primaryVoxelAcceptedHorizontalFluxPerSecond = primaryVoxelAcceptedHorizontalFluxLastStep / stepSeconds;
        primaryVoxelOvertoppingVolumePerSecond = primaryVoxelOvertoppingVolumeLastStep / stepSeconds;
        primaryVoxelCumulativeBoundaryOutflow = 0f;
        primaryVoxelRetainedSurfaceVolume = 0f;
        voxelExpectedVolume = primaryVoxelCumulativeAcceptedVolume;
        voxelActualVolume = primaryVoxelCurrentVolume;
        voxelVolumeError = voxelActualVolume - voxelExpectedVolume;
        estimatedSurfaceLevel = primaryVoxelCurrentSurfaceHeight;
        estimatedActiveBandBottom = primaryVoxelCurrentSurfaceHeight - activeHeightThreshold;
        // Full cells/flux/solids snapshots are manual-only because DX11 can
        // stall when their 3D buffers are read back every second.
        LogPrimaryVoxelMainDetails();
    }

    void QueuePrimaryVoxelFlowDiagnosticsReadback()
    {
        if (primaryVoxelFlowReadbackInFlight || primaryVoxelBuffer == null || primaryVoxelFluxBuffer == null ||
            primaryVoxelSolidBuffer == null || primaryVoxelCellCount <= 0)
            return;

        int count = primaryVoxelCellCount;
        if (primaryVoxelDiagnosticCells == null || primaryVoxelDiagnosticCells.Length != count)
        {
            primaryVoxelDiagnosticCells = new PrimaryVoxelCellGPU[count];
            primaryVoxelDiagnosticFlux = new PrimaryVoxelFaceFluxGPU[count];
            primaryVoxelDiagnosticReceiveScales = new float[count];
            primaryVoxelDiagnosticSolids = new uint[count];
        }
        primaryVoxelFlowReadbackColliderCount = Mathf.Max(solidColliderCount, 0);
        if (primaryVoxelDiagnosticColliders == null || primaryVoxelDiagnosticColliders.Length != primaryVoxelFlowReadbackColliderCount)
            primaryVoxelDiagnosticColliders = new SolidColliderDataGPU[primaryVoxelFlowReadbackColliderCount];

        primaryVoxelFlowReadbackInFlight = true;
        primaryVoxelFlowReadbackFailed = false;
        bool readDeferredInflow = primaryVoxelDeferredInflowBuffer != null;
        primaryVoxelFlowReadbackPending = 4 + (readDeferredInflow ? 1 : 0) +
            (primaryVoxelFlowReadbackColliderCount > 0 && solidColliderBuffer != null ? 1 : 0);
        AsyncGPUReadback.Request(primaryVoxelBuffer, OnPrimaryVoxelCellsReadback);
        AsyncGPUReadback.Request(primaryVoxelFluxBuffer, OnPrimaryVoxelFluxReadback);
        AsyncGPUReadback.Request(primaryVoxelReceiveScaleBuffer, OnPrimaryVoxelReceiveScalesReadback);
        AsyncGPUReadback.Request(primaryVoxelSolidBuffer, OnPrimaryVoxelSolidsReadback);
        if (readDeferredInflow)
            AsyncGPUReadback.Request(primaryVoxelDeferredInflowBuffer, OnPrimaryVoxelDeferredReadback);
        if (primaryVoxelFlowReadbackColliderCount > 0 && solidColliderBuffer != null)
            AsyncGPUReadback.Request(solidColliderBuffer, OnPrimaryVoxelCollidersReadback);
    }

    void FinishPrimaryVoxelFlowReadback(bool hasError)
    {
        primaryVoxelFlowReadbackFailed |= hasError;
        primaryVoxelFlowReadbackPending--;
        if (primaryVoxelFlowReadbackPending > 0)
            return;
        primaryVoxelFlowReadbackInFlight = false;
        if (!primaryVoxelFlowReadbackFailed)
            UpdatePrimaryVoxelFlowDiagnostics();
    }

    void OnPrimaryVoxelCellsReadback(AsyncGPUReadbackRequest request)
    {
        bool error = request.hasError;
        if (!error) request.GetData<PrimaryVoxelCellGPU>().CopyTo(primaryVoxelDiagnosticCells);
        FinishPrimaryVoxelFlowReadback(error);
    }
    void OnPrimaryVoxelFluxReadback(AsyncGPUReadbackRequest request)
    {
        bool error = request.hasError;
        if (!error) request.GetData<PrimaryVoxelFaceFluxGPU>().CopyTo(primaryVoxelDiagnosticFlux);
        FinishPrimaryVoxelFlowReadback(error);
    }
    void OnPrimaryVoxelReceiveScalesReadback(AsyncGPUReadbackRequest request)
    {
        bool error = request.hasError;
        if (!error) request.GetData<float>().CopyTo(primaryVoxelDiagnosticReceiveScales);
        FinishPrimaryVoxelFlowReadback(error);
    }
    void OnPrimaryVoxelSolidsReadback(AsyncGPUReadbackRequest request)
    {
        bool error = request.hasError;
        if (!error) request.GetData<uint>().CopyTo(primaryVoxelDiagnosticSolids);
        FinishPrimaryVoxelFlowReadback(error);
    }
    void OnPrimaryVoxelDeferredReadback(AsyncGPUReadbackRequest request)
    {
        bool error = request.hasError;
        if (!error) request.GetData<Vector4>().CopyTo(primaryVoxelDeferredInflowReadback);
        FinishPrimaryVoxelFlowReadback(error);
    }
    void OnPrimaryVoxelCollidersReadback(AsyncGPUReadbackRequest request)
    {
        bool error = request.hasError;
        if (!error) request.GetData<SolidColliderDataGPU>().CopyTo(primaryVoxelDiagnosticColliders);
        FinishPrimaryVoxelFlowReadback(error);
    }

    void UpdatePrimaryVoxelFlowDiagnostics()
    {
        if (!usePrimaryVoxelVolume || primaryVoxelBuffer == null || primaryVoxelFluxBuffer == null ||
            primaryVoxelReceiveScaleBuffer == null || primaryVoxelSolidBuffer == null ||
            primaryVoxelCellCount <= 0)
            return;

        int countX = BulkGridCountX;
        int countZ = BulkGridCountZ;
        int layers = PrimaryVoxelLayerCount;
        int layerArea = countX * countZ;
        int count = primaryVoxelCellCount;
        if (primaryVoxelDiagnosticCells == null || primaryVoxelDiagnosticCells.Length != count ||
            primaryVoxelDiagnosticFlux == null || primaryVoxelDiagnosticReceiveScales == null ||
            primaryVoxelDiagnosticSolids == null)
            return;
        int colliderCount = primaryVoxelFlowReadbackColliderCount;
        primaryVoxelDeferredInflowVolume = Mathf.Max(primaryVoxelDeferredInflowReadback[0].w, 0f);

        float cellWidth = Mathf.Max((boundsMax.x - boundsMin.x) / countX, 0.0001f);
        float cellDepth = Mathf.Max((boundsMax.z - boundsMin.z) / countZ, 0.0001f);
        float voxelHeight = Mathf.Max(primaryVoxelHeight, 0.0001f);
        float cellVolume = cellWidth * cellDepth * voxelHeight;
        float minY = boundsMin.y - 100f;
        float[] columnSurface = new float[layerArea];
        bool[] columnWet = new bool[layerArea];
        for (int column = 0; column < layerArea; column++)
            columnSurface[column] = minY;

        int Index(int x, int y, int z) => x + z * countX + y * layerArea;
        bool Valid(int x, int y, int z) => x >= 0 && x < countX && y >= 0 && y < layers && z >= 0 && z < countZ;
        float Fill(int index) => primaryVoxelDiagnosticSolids[index] == 0u
            ? Mathf.Max(primaryVoxelDiagnosticCells[index].fillAndFlags.x, 0f) : 0f;
        float Surface(int column) => column >= 0 && column < layerArea && columnWet[column]
            ? columnSurface[column] : minY;

        int wetCellCount = 0;
        float wetFillSum = 0f;
        float compressionPressureSum = 0f;
        float compressionPressureMax = 0f;
        float gridSpeedSum = 0f;
        float gridSpeedMax = 0f;
        for (int y = 0; y < layers; y++)
        {
            for (int z = 0; z < countZ; z++)
            {
                for (int x = 0; x < countX; x++)
                {
                    int index = Index(x, y, z);
                    float fill = Fill(index);
                    if (fill <= 0.01f)
                        continue;
                    PrimaryVoxelCellGPU cell = primaryVoxelDiagnosticCells[index];
                    float compressionPressure = Mathf.Max(cell.velocityPressure.w, 0f);
                    float gridSpeed = new Vector3(cell.velocityPressure.x,
                        cell.velocityPressure.y, cell.velocityPressure.z).magnitude;
                    wetCellCount++;
                    wetFillSum += fill;
                    compressionPressureSum += compressionPressure;
                    compressionPressureMax = Mathf.Max(compressionPressureMax, compressionPressure);
                    gridSpeedSum += gridSpeed;
                    gridSpeedMax = Mathf.Max(gridSpeedMax, gridSpeed);
                    int column = x + z * countX;
                    columnWet[column] = true;
                    columnSurface[column] = boundsMin.y + (y + Mathf.Min(fill, 1f)) * voxelHeight;
                }
            }
        }

        int ProbeColumn(Transform probe)
        {
            if (probe == null)
                return -1;
            int x = Mathf.Clamp(Mathf.FloorToInt((probe.position.x - boundsMin.x) / cellWidth), 0, countX - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt((probe.position.z - boundsMin.z) / cellDepth), 0, countZ - 1);
            return x + z * countX;
        }

        int upstreamColumn = ProbeColumn(primaryVoxelUpstreamProbe != null ? primaryVoxelUpstreamProbe : spawnPoint);
        int downstreamColumn = ProbeColumn(primaryVoxelDownstreamProbe);
        primaryVoxelUpstreamSurfaceHeight = Surface(upstreamColumn);
        primaryVoxelDownstreamSurfaceHeight = Surface(downstreamColumn);

        float derivedSill = minY;
        if (usePrimaryVoxelOverflowSillOverride)
        {
            derivedSill = primaryVoxelOverflowSillHeight;
        }
        else if (primaryVoxelOverflowProbe != null)
        {
            int probeX = Mathf.Clamp(Mathf.FloorToInt((primaryVoxelOverflowProbe.position.x - boundsMin.x) / cellWidth), 0, countX - 1);
            int probeZ = Mathf.Clamp(Mathf.FloorToInt((primaryVoxelOverflowProbe.position.z - boundsMin.z) / cellDepth), 0, countZ - 1);
            for (int y = 0; y < layers; y++)
            {
                int index = Index(probeX, y, probeZ);
                if (primaryVoxelDiagnosticSolids[index] != 0u)
                    derivedSill = Mathf.Max(derivedSill, boundsMin.y + (y + 1) * voxelHeight);
            }
        }
        primaryVoxelDerivedOverflowSillHeight = derivedSill;

        float openGradient = 0f;
        float blockedGradient = 0f;
        int[] visitStamp = new int[layerArea];
        int visitId = 0;
        void BasinRange(int startColumn, out float minSurface, out float maxSurface)
        {
            minSurface = minY;
            maxSurface = minY;
            if (startColumn < 0 || !columnWet[startColumn]) return;
            visitId++;
            Queue<int> queue = new Queue<int>();
            queue.Enqueue(startColumn);
            visitStamp[startColumn] = visitId;
            bool any = false;
            while (queue.Count > 0)
            {
                int column = queue.Dequeue();
                float surface = Surface(column);
                if (!any) { minSurface = surface; maxSurface = surface; any = true; }
                else { minSurface = Mathf.Min(minSurface, surface); maxSurface = Mathf.Max(maxSurface, surface); }
                int x = column % countX;
                int z = column / countX;
                foreach (Vector2Int offset in new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down })
                {
                    int nx = x + offset.x;
                    int nz = z + offset.y;
                    if (nx < 0 || nx >= countX || nz < 0 || nz >= countZ) continue;
                    int neighbour = nx + nz * countX;
                    if (!columnWet[neighbour] || visitStamp[neighbour] == visitId) continue;
                    bool open = false;
                    for (int y = 0; y < layers; y++)
                    {
                        int a = Index(x, y, z);
                        int b = Index(nx, y, nz);
                        if (primaryVoxelDiagnosticSolids[a] == 0u && primaryVoxelDiagnosticSolids[b] == 0u &&
                            (Fill(a) > 0.01f || Fill(b) > 0.01f)) { open = true; break; }
                    }
                    if (!open) continue;
                    visitStamp[neighbour] = visitId;
                    queue.Enqueue(neighbour);
                }
            }
        }
        BasinRange(upstreamColumn, out primaryVoxelUpstreamBasinMinSurface, out primaryVoxelUpstreamBasinMaxSurface);
        BasinRange(downstreamColumn, out primaryVoxelDownstreamBasinMinSurface, out primaryVoxelDownstreamBasinMaxSurface);

        Vector3 CellCenter(int x, int y, int z) => new Vector3(
            boundsMin.x + (x + 0.5f) * cellWidth,
            boundsMin.y + (y + 0.5f) * voxelHeight,
            boundsMin.z + (z + 0.5f) * cellDepth);
        bool PointInsidePackedSolid(Vector3 point)
        {
            for (int i = 0; i < colliderCount; i++)
            {
                SolidColliderDataGPU collider = primaryVoxelDiagnosticColliders[i];
                Vector3 half = new Vector3(collider.halfExtents.x, collider.halfExtents.y, collider.halfExtents.z);
                if (half.x <= 0f || half.y <= 0f || half.z <= 0f)
                    continue;
                Vector3 relative = point - new Vector3(collider.center.x, collider.center.y, collider.center.z);
                Vector3 axisX = new Vector3(collider.axisX.x, collider.axisX.y, collider.axisX.z);
                Vector3 axisY = new Vector3(collider.axisY.x, collider.axisY.y, collider.axisY.z);
                Vector3 axisZ = new Vector3(collider.axisZ.x, collider.axisZ.y, collider.axisZ.z);
                if (Mathf.Abs(Vector3.Dot(relative, axisX)) <= half.x &&
                    Mathf.Abs(Vector3.Dot(relative, axisY)) <= half.y &&
                    Mathf.Abs(Vector3.Dot(relative, axisZ)) <= half.z)
                    return true;
            }
            return false;
        }
        bool FaceBlocked(int x, int y, int z, int nx, int ny, int nz)
        {
            int source = Index(x, y, z);
            int target = Index(nx, ny, nz);
            return primaryVoxelDiagnosticSolids[source] != 0u || primaryVoxelDiagnosticSolids[target] != 0u ||
                PointInsidePackedSolid((CellCenter(x, y, z) + CellCenter(nx, ny, nz)) * 0.5f);
        }
        float SegmentSurface(int x, int y, int z)
        {
            if (!Valid(x, y, z) || primaryVoxelDiagnosticSolids[Index(x, y, z)] != 0u)
                return minY;
            int lower = y;
            while (lower > 0 && primaryVoxelDiagnosticSolids[Index(x, lower - 1, z)] == 0u)
                lower--;
            int upper = y;
            while (upper + 1 < layers && primaryVoxelDiagnosticSolids[Index(x, upper + 1, z)] == 0u)
                upper++;
            for (int scanY = lower; scanY <= upper; scanY++)
            {
                float fill = Fill(Index(x, scanY, z));
                if (fill > 0.01f)
                    return boundsMin.y + (scanY + Mathf.Min(fill, 1f)) * voxelHeight;
            }
            return minY;
        }

        int uniqueOvertoppingFaces = 0;
        float acceptedHorizontalFillLastPass = 0f;
        float acceptedVerticalFillLastPass = 0f;
        void SumAcceptedFace(int targetX, int targetY, int targetZ, float rawFlux, bool vertical)
        {
            if (!Valid(targetX, targetY, targetZ) || rawFlux <= 0.000001f)
                return;
            float accepted = rawFlux * Mathf.Clamp01(primaryVoxelDiagnosticReceiveScales[
                Index(targetX, targetY, targetZ)]);
            if (vertical) acceptedVerticalFillLastPass += accepted;
            else acceptedHorizontalFillLastPass += accepted;
        }
        void MeasureDirectedFace(int x, int y, int z, int nx, int ny, int nz, float rawFlux)
        {
            float sourceSurface = SegmentSurface(x, y, z);
            float targetSurface = SegmentSurface(nx, ny, nz);
            if (sourceSurface <= minY + 0.001f || targetSurface <= minY + 0.001f)
                return;

            float gradient = Mathf.Abs(sourceSurface - targetSurface);
            bool blocked = FaceBlocked(x, y, z, nx, ny, nz);
            if (blocked)
            {
                blockedGradient = Mathf.Max(blockedGradient, gradient);
                return;
            }

            // "Open" means the solver actually requested a transfer through this
            // geometric face in the final voxel step, not merely that its cells
            // happen not to be marked solid.
            if (rawFlux <= 0.000001f)
                return;
            openGradient = Mathf.Max(openGradient, gradient);

            bool horizontal = y == ny;
            bool sourceBelowSolid = horizontal && y > 0 && primaryVoxelDiagnosticSolids[Index(x, y - 1, z)] != 0u;
            bool targetBelowSolid = horizontal && y > 0 && primaryVoxelDiagnosticSolids[Index(nx, ny - 1, nz)] != 0u;
            if (enablePrimaryVoxelWeirOverflow && horizontal && (sourceBelowSolid != targetBelowSolid))
            {
                float crest = boundsMin.y + y * voxelHeight;
                if (sourceSurface > crest + 0.0001f && sourceSurface > targetSurface + 0.0001f)
                    uniqueOvertoppingFaces++;
            }
        }

        for (int y = 0; y < layers; y++)
        {
            for (int z = 0; z < countZ; z++)
            {
                for (int x = 0; x < countX; x++)
                {
                    int source = Index(x, y, z);
                    PrimaryVoxelFaceFluxGPU flux = primaryVoxelDiagnosticFlux[source];
                    SumAcceptedFace(x + 1, y, z, flux.lateral.x, false);
                    SumAcceptedFace(x - 1, y, z, flux.lateral.y, false);
                    SumAcceptedFace(x, y, z + 1, flux.lateral.z, false);
                    SumAcceptedFace(x, y, z - 1, flux.lateral.w, false);
                    SumAcceptedFace(x, y - 1, z, flux.vertical.x, true);
                    SumAcceptedFace(x, y + 1, z, flux.vertical.y, true);
                    if (Valid(x + 1, y, z))
                    {
                        int target = Index(x + 1, y, z);
                        MeasureDirectedFace(x, y, z, x + 1, y, z, flux.lateral.x);
                        MeasureDirectedFace(x + 1, y, z, x, y, z, primaryVoxelDiagnosticFlux[target].lateral.y);
                    }
                    if (Valid(x, y, z + 1))
                    {
                        int target = Index(x, y, z + 1);
                        MeasureDirectedFace(x, y, z, x, y, z + 1, flux.lateral.z);
                        MeasureDirectedFace(x, y, z + 1, x, y, z, primaryVoxelDiagnosticFlux[target].lateral.w);
                    }
                }
            }
        }

        primaryVoxelVerticalOpeningProbeValid = false;
        primaryVoxelVerticalOpeningFaceOpen = false;
        primaryVoxelVerticalOpeningWetBelow = false;
        primaryVoxelVerticalOpeningWetAbove = false;
        primaryVoxelVerticalOpeningUpwardFlux = 0f;
        if (primaryVoxelVerticalOpeningProbe != null)
        {
            Vector3 probePosition = primaryVoxelVerticalOpeningProbe.position;
            int probeX = Mathf.FloorToInt((probePosition.x - boundsMin.x) / cellWidth);
            int probeZ = Mathf.FloorToInt((probePosition.z - boundsMin.z) / cellDepth);
            int upperY = Mathf.FloorToInt((probePosition.y - boundsMin.y) / voxelHeight);
            int lowerY = upperY - 1;
            if (Valid(probeX, lowerY, probeZ) && Valid(probeX, upperY, probeZ))
            {
                primaryVoxelVerticalOpeningProbeValid = true;
                primaryVoxelVerticalOpeningFaceOpen = !FaceBlocked(probeX, lowerY, probeZ,
                    probeX, upperY, probeZ);
                primaryVoxelVerticalOpeningWetBelow = Fill(Index(probeX, lowerY, probeZ)) > 0.01f;
                primaryVoxelVerticalOpeningWetAbove = Fill(Index(probeX, upperY, probeZ)) > 0.01f;
                if (primaryVoxelVerticalOpeningFaceOpen)
                {
                    PrimaryVoxelFaceFluxGPU lowerFlux = primaryVoxelDiagnosticFlux[
                        Index(probeX, lowerY, probeZ)];
                    PrimaryVoxelFaceFluxGPU upperFlux = primaryVoxelDiagnosticFlux[
                        Index(probeX, upperY, probeZ)];
                    primaryVoxelVerticalOpeningUpwardFlux = Mathf.Max(lowerFlux.vertical.y,
                        upperFlux.vertical.x);
                }
            }
        }
        float readbackSeconds = Mathf.Max(shaderDeltaTime, 0.000001f);
        float finalPassSeconds = Mathf.Max(shaderDeltaTime /
            Mathf.Clamp(primaryVoxelFlowSubsteps, 1, 8), 0.000001f);
        primaryVoxelWetCellCount = wetCellCount;
        primaryVoxelAverageWetFill = wetCellCount > 0 ? wetFillSum / wetCellCount : 0f;
        primaryVoxelAverageCompressionPressure = wetCellCount > 0
            ? compressionPressureSum / wetCellCount : 0f;
        primaryVoxelMaximumCompressionPressure = compressionPressureMax;
        primaryVoxelAverageGridSpeed = wetCellCount > 0 ? gridSpeedSum / wetCellCount : 0f;
        primaryVoxelMaximumGridSpeed = gridSpeedMax;
        primaryVoxelAcceptedHorizontalFluxLastPassPerSecond = acceptedHorizontalFillLastPass *
            cellVolume / finalPassSeconds;
        primaryVoxelAcceptedVerticalFluxLastPassPerSecond = acceptedVerticalFillLastPass *
            cellVolume / finalPassSeconds;
        primaryVoxelAcceptedHorizontalFluxPerSecond = primaryVoxelAcceptedHorizontalFluxLastStep / readbackSeconds;
        primaryVoxelOpenFaceHeadGradient = openGradient;
        primaryVoxelBlockedFaceHeadGradient = blockedGradient;
        primaryVoxelOvertoppingFaceCountLastStep = uniqueOvertoppingFaces;
        primaryVoxelOvertoppingVolumePerSecond = primaryVoxelOvertoppingVolumeLastStep / readbackSeconds;
        LogPrimaryVoxelMainDetails();
    }

    void LogPrimaryVoxelMainDetails()
    {
        if (!logPrimaryVoxelMainDetails)
            return;
        float now = Time.unscaledTime;
        if (now - primaryVoxelMainDetailsLastLogTime <
            Mathf.Max(primaryVoxelMainDetailsLogInterval, 0.5f))
            return;
        primaryVoxelMainDetailsLastLogTime = now;

        float liveSeaLevel = useSeaLevelTransformForPressure && seaLevel != null
            ? seaLevel.position.y + seaLevelHeightOffset
            : primaryVoxelExternalWaterLevel;
        float effectiveSeaLevel = GetPrimaryVoxelEffectiveExternalWaterLevel();
        float breachY = spawnPoint != null ? spawnPoint.position.y : boundsMin.y;
        float drySentinel = boundsMin.y - 50f;
        float interiorSurface = primaryVoxelUpstreamSurfaceHeight > drySentinel
            ? primaryVoxelUpstreamSurfaceHeight : breachY;
        float hydraulicInteriorLevel = Mathf.Max(breachY, interiorSurface);
        float head = Mathf.Max(effectiveSeaLevel - hydraulicInteriorLevel, 0f);
        float waterDensity = Mathf.Max(primaryVoxelFloodwaterDensity, 1f);
        float gravityMagnitude = Mathf.Max(-gravity.y, 0.01f);
        float inletPressureKPa = waterDensity * gravityMagnitude * head * 0.001f;
        float breachArea = Mathf.Max(primaryVoxelBreachWidth, 0.05f) *
            Mathf.Max(primaryVoxelBreachHeight, 0.05f);
        bool breachActive = continuousSpawn && spawnPoint != null && usePrimaryVoxelBreachInflow && primaryVoxelBreachOpen;
        float commandedRate = breachActive
            ? Mathf.Clamp(primaryVoxelBreachDischargeCoefficient, 0.05f, 1f) * breachArea *
                Mathf.Sqrt(2f * gravityMagnitude * head)
            : 0f;
        float stepSeconds = Mathf.Max(shaderDeltaTime, 0.000001f);
        float acceptedRate = primaryVoxelAcceptedInflowLastStep / stepSeconds;
        float rejectedRate = primaryVoxelRejectedInflowLastStep / stepSeconds;
        Debug.Log($"[Inflow Pressure Draft Details] sea(live/effective)={liveSeaLevel:F3}/{effectiveSeaLevel:F3}m " +
            $"draft={primaryVoxelAdditionalDraft:F3}m shipY(initial/live)={primaryVoxelInitialShipRootY:F3}/{primaryVoxelCurrentShipRootY:F3}m " +
            $"topologyRefreshes={primaryVoxelShipTopologyRefreshCount} floodMass={primaryVoxelFloodwaterMassKg:F0}kg " +
            $"volume={primaryVoxelCurrentVolume:F3}m3 | breach(active/area/Y)={breachActive}/{breachArea:F2}m2/{breachY:F3}m " +
            $"interior={interiorSurface:F3}m head={head:F3}m pressure={inletPressureKPa:F2}kPa | " +
            $"Q(commanded/accepted/rejected)={commandedRate:F3}/{acceptedRate:F3}/{rejectedRate:F3}m3/s " +
            $"massRate={acceptedRate * waterDensity:F0}kg/s");

        Debug.Log($"[Core Voxel Statistics] wet={primaryVoxelWetCellCount}/{primaryVoxelCellCount} " +
            $"fill(mean/max)={primaryVoxelAverageWetFill:F3}/{primaryVoxelMaxCellFill:F3} " +
            $"compressionPressure(mean/max)={primaryVoxelAverageCompressionPressure:F3}/{primaryVoxelMaximumCompressionPressure:F3} " +
            $"gridSpeed(mean/max)={primaryVoxelAverageGridSpeed:F3}/{primaryVoxelMaximumGridSpeed:F3}m/s | " +
            $"grossInternalFlux(finalPass H/V)={primaryVoxelAcceptedHorizontalFluxLastPassPerSecond:F3}/{primaryVoxelAcceptedVerticalFluxLastPassPerSecond:F3}m3/s " +
            $"grossInternalFlux(allPass H)={primaryVoxelAcceptedHorizontalFluxPerSecond:F3}m3/s | " +
            $"PBF skin pressure/viscosity/cohesion are intentionally not sampled here: " +
            $"they are separate from authoritative voxel transport.");
    }
    void UpdateBulkFlowDiagnostics()
    {
        if (bulkWaterBuffer == null || bulkFluxBuffer == null ||
            bulkReceiveScaleBuffer == null || bulkWaterCellCount <= 0)
            return;

        if (bulkDiagnosticsCells == null || bulkDiagnosticsCells.Length != bulkWaterCellCount)
            bulkDiagnosticsCells = new BulkWaterCellGPU[bulkWaterCellCount];
        if (bulkDiagnosticsFlux == null || bulkDiagnosticsFlux.Length != bulkWaterCellCount)
            bulkDiagnosticsFlux = new Vector4[bulkWaterCellCount];
        if (bulkDiagnosticsReceiveScales == null || bulkDiagnosticsReceiveScales.Length != bulkWaterCellCount)
            bulkDiagnosticsReceiveScales = new float[bulkWaterCellCount];

        bulkWaterBuffer.GetData(bulkDiagnosticsCells);
        bulkFluxBuffer.GetData(bulkDiagnosticsFlux);
        bulkReceiveScaleBuffer.GetData(bulkDiagnosticsReceiveScales);
        if (bulkActivityBuffer != null)
        {
            if (bulkDiagnosticsActivity == null || bulkDiagnosticsActivity.Length != bulkWaterCellCount)
                bulkDiagnosticsActivity = new uint[bulkWaterCellCount];
            bulkActivityBuffer.GetData(bulkDiagnosticsActivity);
        }

        float cellSizeX = Mathf.Max((boundsMax.x - boundsMin.x) / Mathf.Max(BulkGridCountX, 1), 0.0001f);
        float cellSizeZ = Mathf.Max((boundsMax.z - boundsMin.z) / Mathf.Max(BulkGridCountZ, 1), 0.0001f);
        float cellArea = Mathf.Max(cellSizeX * cellSizeZ, 0.000001f);
        float particleVolume = Mathf.Max(bulkParticleVolumeForDiagnostics, 0.000001f);
        int wet = 0;
        int activityCells = 0;
        float maxGradient = 0f;
        float acceptedFlux = 0f;
        float activityThreshold = Mathf.Max(bulkWakeActivityThreshold, 0f);

        for (int z = 0; z < BulkGridCountZ; z++)
        {
            for (int x = 0; x < BulkGridCountX; x++)
            {
                int index = x + z * BulkGridCountX;
                if (index >= bulkDiagnosticsCells.Length)
                    continue;

                BulkWaterCellGPU cell = bulkDiagnosticsCells[index];
                if (cell.volume > particleVolume * 0.5f)
                    wet++;
                if (bulkDiagnosticsActivity != null && index < bulkDiagnosticsActivity.Length &&
                    bulkDiagnosticsActivity[index] * 0.001f >= activityThreshold)
                    activityCells++;

                float head = boundsMin.y + Mathf.Max(cell.volume, 0f) / cellArea;
                if (x + 1 < BulkGridCountX)
                {
                    float neighbourHead = boundsMin.y + Mathf.Max(
                        bulkDiagnosticsCells[index + 1].volume, 0f) / cellArea;
                    maxGradient = Mathf.Max(maxGradient, Mathf.Abs(head - neighbourHead));
                    acceptedFlux += Mathf.Max(bulkDiagnosticsFlux[index].x, 0f) *
                        Mathf.Clamp01(bulkDiagnosticsReceiveScales[index + 1]);
                }
                if (z + 1 < BulkGridCountZ)
                {
                    float neighbourHead = boundsMin.y + Mathf.Max(
                        bulkDiagnosticsCells[index + BulkGridCountX].volume, 0f) / cellArea;
                    maxGradient = Mathf.Max(maxGradient, Mathf.Abs(head - neighbourHead));
                    acceptedFlux += Mathf.Max(bulkDiagnosticsFlux[index].z, 0f) *
                        Mathf.Clamp01(bulkDiagnosticsReceiveScales[index + BulkGridCountX]);
                }
            }
        }

        bulkWetCellCount = wet;
        bulkActiveActivityCellCount = activityCells;
        bulkMaxHeadGradient = maxGradient;
        bulkAcceptedFluxVolume = acceptedFlux;
    }

    void DrawPrimaryVoxelSeaAndHatchGizmos()
    {
        if (!drawPrimaryVoxelHatchGizmos)
            return;

        float seaLevel = GetPrimaryVoxelEffectiveExternalWaterLevel();
        Vector3 seaCenter = new Vector3(
            (boundsMin.x + boundsMax.x) * 0.5f,
            seaLevel,
            (boundsMin.z + boundsMax.z) * 0.5f);
        Vector3 seaSize = new Vector3(
            Mathf.Max(boundsMax.x - boundsMin.x, 0.01f),
            0.035f,
            Mathf.Max(boundsMax.z - boundsMin.z, 0.01f));
        Gizmos.color = new Color(0.05f, 0.85f, 1f, 0.14f);
        Gizmos.DrawCube(seaCenter, seaSize);
        Gizmos.color = new Color(0.05f, 0.9f, 1f, 0.9f);
        Gizmos.DrawWireCube(seaCenter, seaSize);

        List<BoxCollider> riserVolumes = new List<BoxCollider>();
        CollectPrimaryVoxelHatchRiserVolumesForGizmos(riserVolumes);
        foreach (BoxCollider trigger in riserVolumes)
        {
            Bounds hatchBounds = trigger.bounds;
            bool clear = IsPrimaryVoxelHatchRiserGizmoClear(trigger);
            Color fill = clear
                ? new Color(0.08f, 1f, 0.15f, 0.20f)
                : new Color(1f, 0.12f, 0.08f, 0.20f);
            Color outline = clear
                ? new Color(0.08f, 1f, 0.15f, 1f)
                : new Color(1f, 0.12f, 0.08f, 1f);

            Gizmos.color = fill;
            Gizmos.DrawCube(hatchBounds.center, hatchBounds.size);
            Gizmos.color = outline;
            Gizmos.DrawWireCube(hatchBounds.center, hatchBounds.size);
            Gizmos.DrawLine(hatchBounds.center - Vector3.up * hatchBounds.extents.y,
                hatchBounds.center + Vector3.up * hatchBounds.extents.y);
        }
    }

    void CollectPrimaryVoxelHatchRiserVolumesForGizmos(List<BoxCollider> result)
    {
        result.Clear();
        if (primaryVoxelHatchRiserVolumes != null)
        {
            foreach (BoxCollider trigger in primaryVoxelHatchRiserVolumes)
            {
                if (trigger != null)
                    result.Add(trigger);
            }
        }

        if (result.Count > 0)
            return;

        ShipSectionBuilder builder = FindFirstObjectByType<ShipSectionBuilder>();
        if (builder == null || builder.verticalRiserVolumes == null)
            return;

        foreach (BoxCollider trigger in builder.verticalRiserVolumes)
        {
            if (trigger != null)
                result.Add(trigger);
        }
    }

    bool IsPrimaryVoxelHatchRiserGizmoClear(BoxCollider trigger)
    {
        if (trigger == null || !trigger.enabled || !trigger.isTrigger ||
            !trigger.gameObject.activeInHierarchy)
            return false;

        Bounds hatchBounds = trigger.bounds;
        int sampleCount = Mathf.Max(primaryVoxelHatchGizmoSamples, 1);
        float rayStartY = hatchBounds.min.y + 0.02f;
        float rayLength = Mathf.Max(hatchBounds.size.y - 0.04f, 0.01f);
        bool hasSample = false;

        for (int z = 0; z < sampleCount; z++)
        {
            float zFraction = sampleCount == 1 ? 0.5f : (z + 0.5f) / sampleCount;
            for (int x = 0; x < sampleCount; x++)
            {
                float xFraction = sampleCount == 1 ? 0.5f : (x + 0.5f) / sampleCount;
                Vector3 rayStart = new Vector3(
                    Mathf.Lerp(hatchBounds.min.x, hatchBounds.max.x, xFraction),
                    rayStartY,
                    Mathf.Lerp(hatchBounds.min.z, hatchBounds.max.z, zFraction));
                hasSample = true;
                bool blocked = false;
                RaycastHit[] hits = Physics.RaycastAll(rayStart, Vector3.up, rayLength,
                    ~0, QueryTriggerInteraction.Ignore);
                foreach (RaycastHit hit in hits)
                {
                    Collider hitCollider = hit.collider;
                    if (hitCollider != null && hitCollider.enabled &&
                        hitCollider.CompareTag("ShipHull"))
                    {
                        blocked = true;
                        break;
                    }
                }

                if (!blocked)
                    return true;
            }
        }

        return !hasSample;
    }

    void OnDrawGizmos()
    {
        DrawPrimaryVoxelSeaAndHatchGizmos();
    }

    void OnDestroy()
    {
        // Always release GPU buffers owned by this script.
        particleBuffer?.Release();
        previousPositionBuffer?.Release();
        particleSurfaceAgeBuffer?.Release();
        particleDormantSpeedAverageBuffer?.Release();
        cellHeadsBuffer?.Release();
        nextIndexBuffer?.Release();
        debugForceBuffer?.Release();
        pbfPreviousPositionBuffer?.Release();
        pbfPredictedPositionBuffer?.Release();
        pbfPredictedVelocityBuffer?.Release();
        pbfLambdaBuffer?.Release();
        pbfPositionDeltaBuffer?.Release();
        solidColliderBuffer?.Release();
        fallbackSurfaceTileBuffer?.Release();
        bulkWaterBuffer?.Release();
        bulkWaterNextBuffer?.Release();
        bulkFluxBuffer?.Release();
        bulkReceiveScaleBuffer?.Release();
        bulkFaceSillBuffer?.Release();
        bulkTokenDeltaBuffer?.Release();
        bulkTokenFluxResidualBuffer?.Release();
        bulkTokenMoveQuotaBuffer?.Release();
        bulkActivityBuffer?.Release();
        bulkWakeCounterBuffer?.Release();
        bulkVoxelBuffer?.Release();
        bulkVoxelCounterBuffer?.Release();
        // Primary voxel volume buffers are allocated separately from the
        // legacy bulk buffers. Release every owned handle explicitly so
        // Unity's native ComputeBuffer leak detector retains nothing.
        primaryVoxelBuffer?.Release();
        primaryVoxelNextBuffer?.Release();
        primaryVoxelPreviousFrameBuffer?.Release();
        primaryVoxelFluxBuffer?.Release();
        primaryVoxelReceiveScaleBuffer?.Release();
        primaryVoxelReservoirBoundaryBuffer?.Release();
        primaryVoxelSolidBuffer?.Release();
        primaryVoxelFaceOpenBuffer?.Release();
        primaryVoxelInflowBuffer?.Release();
        primaryVoxelDeferredInflowBuffer?.Release();
        primaryVoxelDiagnosticsBuffer?.Release();
        primaryVoxelHatchProbeBuffer?.Release();
        primaryVoxelHatchDebugBuffer?.Release();
        primarySurfaceWaveBuffer?.Release();
        primarySurfaceWaveNextBuffer?.Release();
        primarySurfaceWaveFaceFluxBuffer?.Release();
        primarySurfaceWaveFaceFluxNextBuffer?.Release();
        primarySurfaceWaveDiagnosticsBuffer?.Release();
        primarySurfaceWaveMeanCorrectionBuffer?.Release();
        primarySurfaceWaveFlickerEventBuffer?.Release();
        primarySurfaceWaveFlickerBuildStateBuffer?.Release();
        primarySurfaceWaveFlickerForcingStateBuffer?.Release();
        primarySurfaceWaveFlickerEventCountBuffer?.Release();
        primarySurfaceFlowBuffer?.Release();
        primarySurfaceFlowNextBuffer?.Release();
        microVoxelRawBuffer?.Release();
        microVoxelDisplayBufferA?.Release();
        microVoxelDisplayBufferB?.Release();
        bulkPhase2CounterBuffer?.Release();
        boundaryParticleBuffer?.Release();
        boundaryCellHeadsBuffer?.Release();
        boundaryNextIndexBuffer?.Release();
        sprayParticleBuffer?.Release();
        primaryVoxelCurrentSplatBuffer?.Release();
        primaryVoxelCurrentSplatArgsBuffer?.Release();
        primaryVoxelUnderwaterSplatStateBuffer?.Release();
        primaryVoxelSurfaceGaussianBuffer?.Release();
        primaryVoxelSurfaceGaussianArgsBuffer?.Release();
        primaryVoxelSurfaceGaussianHistoryBuffer?.Release();
        primaryVoxelSurfaceGaussianDiagnosticsBuffer?.Release();
        if (generatedCurrentSplatMaterial != null) Destroy(generatedCurrentSplatMaterial);
        if (generatedUnderwaterSplatMaterial != null) Destroy(generatedUnderwaterSplatMaterial);
        if (generatedCurrentSplatMesh != null) Destroy(generatedCurrentSplatMesh);
        if (generatedSurfaceGaussianMesh != null) Destroy(generatedSurfaceGaussianMesh);
    }
}

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
struct PrimarySurfaceWaveFlickerEventGPU
{
    public Vector4 identity;
    public Vector4 state;
    public Vector4 baseAndFill;
    public Vector4 eta;
    public Vector4 correction;
    public Vector4 pressureFlux;
    public Vector4 columnHatch;
    public Vector4 discharge;
    public Vector4 finalHeight;
    public Vector4 flags;
}
struct SurfaceGaussianHistoryGPU
{
    public Vector4 positionAndValid;
    public Vector4 velocity;
}

struct UnderwaterSplatStateGPU
{
    public Vector4 positionAndAlive;
    public Vector4 velocityAndSeed;
}

// ------------------------------------------------------------------------
// SECTION: Particle state enum
// The compute shader uses the same integer values, so keep them in sync.
// ------------------------------------------------------------------------
public enum ParticleState
{
    Dormant = 0,
    Active = 1,
    Bulk = 2
}

public enum FluidSolverMode
{
    WCSPH = 0,
    PBF_XPBD = 1
}

// ------------------------------------------------------------------------
// SECTION: CPU mirror of the GPU particle struct
// Layout must match the HLSL GPUParticle layout exactly once fluid.compute is
// updated, including the extra state/sleep fields and padding.
// ------------------------------------------------------------------------
public struct GPUParticle
{
    public Vector3 pos;
    public Vector3 vel;
    public float invMass;
    public float density;
    public int state;
    public float sleepTimer;
    public Vector2 padding;
}

public struct SolidColliderDataGPU
{
    public Vector4 center;
    public Vector4 halfExtents;
    public Vector4 axisX;
    public Vector4 axisY;
    public Vector4 axisZ;

    public static SolidColliderDataGPU FromObb(Vector3 centerPos, Vector3 half, Vector3 x, Vector3 y, Vector3 z)
    {
        SolidColliderDataGPU data = new SolidColliderDataGPU
        {
            center = new Vector4(centerPos.x, centerPos.y, centerPos.z, 0f),
            halfExtents = new Vector4(Mathf.Max(half.x, 0f), Mathf.Max(half.y, 0f), Mathf.Max(half.z, 0f), 0f),
            axisX = new Vector4(x.x, x.y, x.z, 0f),
            axisY = new Vector4(y.x, y.y, y.z, 0f),
            axisZ = new Vector4(z.x, z.y, z.z, 0f)
        };

        return data;
    }

    public static SolidColliderDataGPU MakeDisabled()
    {
        return FromObb(Vector3.zero, Vector3.zero, Vector3.right, Vector3.up, Vector3.forward);
    }
}

public struct BoundaryParticle
{
    public Vector3 pos;
    public float density;
    public float pressure;
    public Vector3 padding;
}

public struct SurfaceTileDataGPU
{
    public Vector3 worldPos;
    public float height;
    public int active;
    public Vector3 padding;
}

public struct BulkWaterCellGPU
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

    public static BulkWaterCellGPU MakeDisabled(float hiddenY)
    {
        return new BulkWaterCellGPU
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

public struct BulkVoxelCellGPU
{
    public float fill;
    public float density;
    public float pressure;
    public int tokenCount;
    public int flags;
}

// The authoritative 3D volume uses two float4s for an unambiguous 32-byte
// StructuredBuffer layout on every supported Unity/DX11 backend.
public struct PrimaryVoxelCellGPU
{
    public Vector4 velocityPressure;
    public Vector4 fillAndFlags;
}

public struct PrimaryVoxelHatchProbeGPU
{
    public Vector4 positionAndAmount;
    public Vector4 timingAndStatus;
    public static PrimaryVoxelHatchProbeGPU Disabled => new PrimaryVoxelHatchProbeGPU { positionAndAmount = Vector4.zero, timingAndStatus = Vector4.zero };
}

public struct PrimaryVoxelHatchDebugGPU
{
    public Vector4 lower;
    public Vector4 throat;
    public Vector4 upper;
    public Vector4 coord;
    public static PrimaryVoxelHatchDebugGPU Disabled => new PrimaryVoxelHatchDebugGPU();
}

public struct PrimaryVoxelFaceFluxGPU
{
    public Vector4 lateral;
    public Vector2 vertical;
    public Vector2 padding;
}

public struct PrimaryVoxelInflowGPU
{
    public Vector4 positionVolume;
    public Vector4 velocityEnabled;
    public Vector4 reservoirHole;

    public static PrimaryVoxelInflowGPU Disabled => new PrimaryVoxelInflowGPU
    {
        positionVolume = Vector4.zero,
        velocityEnabled = Vector4.zero,
        reservoirHole = Vector4.zero
    };
}

public struct SprayParticleGPU
{
    public Vector3 position;
    public float life;
    public Vector3 velocity;
    public float radius;
}

public struct MicroVoxelCellGPU
{
    public uint particleTokens;
    public uint bulkTokens;
    public float fill;
    public float density;
    public float pressure;
    public float surfaceFraction;
    public float pbfWeight;
    public uint flags;
}




