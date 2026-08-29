using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using System;
using System.IO;
using System.Globalization;

[DisallowMultipleComponent]
public class AimingController : MonoBehaviour
{
    /*
        OVERVIEW — WHAT THIS SCRIPT DOES

        AimingController is the high-level “aim preview brain”.

        Its responsibilities:
            • Read the current contact position (racket/ball height)
            • Read the reticle position (target X)
            • Ask DragShotSolver for a feasible shot (v0, theta) that:
                  - lands near the reticle
                  - clears the net with a margin
            • Cache solutions so small movements don’t constantly re-solve
            • Drive PreviewArcGenerator to draw the predicted trajectory
            • Expose GetShotParameters() so HitController / ShotComputationSolver
              can query the last solver result.

        With Magnus enabled, it also:
            • Computes a preview spin vector based on the current shot type
              and modifier from HitController
            • Passes that spin into DragShotSolver so the preview arc matches
              the spin-aware trajectory model.
    */

    // ------------------------------------------------------------
    // 1. References and configuration
    // ------------------------------------------------------------
    [Header("References")]
    public PreviewArcGenerator previewArc;
    public Transform reticle;
    public Transform contactPoint;      // Fallback contact transform
    public hitController hitCtrl;       // Reference to HitController (assign in Inspector)

    [Header("Net Settings")]
    public float netX = 0f;
    public float netHeight = 0.914f;

    [Header("Solver")]
    public ShotSolverComponent solverComponent;

    [Header("Debug")]
    public bool debugLogs = false;

    // Last solver result (used by GetShotParameters)
    private float lastV0 = float.NaN;
    private float lastTheta = float.NaN;

    // Logging state
    private float _lastSolverV0 = float.NaN;
    private float _lastSolverTheta = float.NaN;
    private const float _solverLogEpsilon = 0.01f;
    private int _solverLogCount = 0;
    private int _solverLogMax = 5;

    [Header("Preview Cache")]
    public float solverInterval = 0.25f;
    public float reticleXCacheStep = 0.35f;
    public float reticleZCacheStep = 0.35f;
    public float contactXCacheStep = 0.25f;
    public float contactYCacheStep = 0.05f;
    public float spinCacheStep = 0.25f;
    public int maxPreviewCacheEntries = 256;
    public bool redrawCachedArcWithLiveReticle = false;
    public float cachedArcRedrawInterval = 0.12f;
    public bool computeDebugLandingX = false;
    public bool logCacheStats = false;

    [Header("Preview Contact Source")]
    public bool useLiveBallForPreviewStart = false;
    public bool hidePreviewWhileBallOnPlayerSide = false;

    [Header("Preview Arc Visibility")]
    public bool previewArcVisibleByDefault = false;
    public KeyCode previewArcToggleKey = KeyCode.Keypad0;
    public bool previewArcOnlyWhenBallStationary = true;
    public float previewArcStationarySpeedThreshold = 0.15f;
    public bool disableStartupPrewarmWhenPreviewArcDefaultOff = true;
    public bool logPreviewArcToggle = false;

    [Header("Hit-Time Solver")]
    public bool solveNeutralShotSilentlyWhenPreviewHidden = true;
    public bool neutralShotUsesCurrentSpin = false;
    public bool logSilentNeutralSolve = false;
    public int liveFixedAngleSpeedIterations = 10;
    public int liveFixedAngleMaxExtendedSteps = 8;
    private bool hasShotSolveSpinOverride = false;
    private Vector3 shotSolveSpinOverride = Vector3.zero;

    [Header("Preview Cache Prewarm")]
    public bool prewarmPreviewCacheOnStart = true;
    public UIWorldReticle reticleBoundsSource;
    public Transform prewarmTargetMinBound;
    public Transform prewarmTargetMaxBound;
    public Vector2 prewarmFallbackTargetXRange = new Vector2(0.5f, 13f);
    public Vector2 prewarmFallbackTargetZRange = new Vector2(-5f, 5f);
    public float prewarmTargetXStep = 1f;
    public float prewarmTargetZStep = 1f;
    public int prewarmSolvesPerFrame = 1;
    public int prewarmMaxSolvesPerFrame = 1;
    public int prewarmMaxStartupCells = 160;
    public int prewarmCacheCapacity = 2048;
    public bool prewarmPauseWhileReticleMoving = true;
    public float prewarmReticleMoveThreshold = 0.025f;
    public float prewarmIdleSecondsBeforeSolving = 0.25f;
    public float prewarmAbortIfSingleSolveExceedsMs = 40f;
    public bool logPrewarmProgress = false;

    [Header("Preview Cache File")]
    public bool useFileBackedPreviewCache = true;
    public bool loadPreviewCacheOnStart = true;
    public bool savePreviewCacheAfterPrewarm = true;
    public bool skipPrewarmWhenFileCacheLoaded = true;
    public string previewCacheFileName = "aiming_preview_cache_v1.bin";
    public bool logPreviewCacheFile = false;

    [SerializeField] private int fileCacheLoadedEntries;
    [SerializeField] private int fileCacheSavedEntries;

    [Header("Live Shot Cache File")]
    public bool useFileBackedLiveShotCache = true;
    public bool loadLiveShotCacheOnStart = true;
    public bool saveLiveShotCacheOnDisable = true;
    public string liveShotCacheFileName = "live_shot_solver_cache_v1.bin";
    public int maxLiveShotCacheEntriesPerMode = 8192;
    public bool logLiveShotCacheFile = false;

    [Header("Live Shot Cache Prewarm")]
    public bool prewarmLiveShotCacheOnStart = true;
    public bool skipLiveShotPrewarmWhenFileCacheLoaded = true;
    public bool prewarmLiveShotFullSolves = true;
    public bool prewarmLiveShotFixedAngles = true;
    public bool prewarmLiveShotAllBaseTypes = true;
    public bool prewarmLiveShotAllModifiers = true;
    public int livePrewarmMaxStartupCells = 160;
    public int livePrewarmSolvesPerFrame = 4;
    public int livePrewarmMaxSolvesPerFrame = 4;
    public float livePrewarmAbortIfSingleSolveExceedsMs = 0f;
    public bool logLiveShotPrewarmProgress = false;

    [SerializeField] private int liveShotCacheLoadedEntries;
    [SerializeField] private int liveShotCacheSavedEntries;
    [SerializeField] private int livePrewarmBuiltCount;
    [SerializeField] private int livePrewarmSkippedCount;

    private float nextSolveTime = 0f;
    private float nextCachedArcRedrawTime = 0f;
    private const float MinCachedArcRedrawInterval = 0.12f;
    private const int PreviewCacheFileMagic = 0x54534143; // TSAC
    private const int PreviewCacheFileVersion = 1;
    private const int LiveShotCacheFileMagic = 0x54534C43; // TSLC
    private const int LiveShotCacheFileVersion = 1;
    private Coroutine prewarmCoroutine;
    private Coroutine livePrewarmCoroutine;
    private bool prewarmInProgress;
    private int prewarmBuiltCount;
    private int prewarmSkippedCount;
    private bool previewArcManuallyVisible;
    private bool liveShotCacheDirty;
    private PreviewCacheKey lastPreviewKey;
    private bool hasLastPreviewKey;
    private readonly Dictionary<PreviewCacheKey, PreviewCacheEntry> previewCache =
        new Dictionary<PreviewCacheKey, PreviewCacheEntry>();
    private readonly Queue<PreviewCacheKey> previewCacheOrder = new Queue<PreviewCacheKey>();
    private readonly Dictionary<FullShotCacheKey, (float v0, float theta)> fullShotCache =
        new Dictionary<FullShotCacheKey, (float v0, float theta)>();
    private readonly Queue<FullShotCacheKey> fullShotCacheOrder = new Queue<FullShotCacheKey>();
    private readonly Dictionary<FixedSpeedCacheKey, (float v0, float theta)> fixedSpeedCache =
        new Dictionary<FixedSpeedCacheKey, (float v0, float theta)>();
    private readonly Queue<FixedSpeedCacheKey> fixedSpeedCacheOrder = new Queue<FixedSpeedCacheKey>();
    private readonly Dictionary<FixedAngleCacheKey, (float v0, float theta)> fixedAngleCache =
        new Dictionary<FixedAngleCacheKey, (float v0, float theta)>();
    private readonly Queue<FixedAngleCacheKey> fixedAngleCacheOrder = new Queue<FixedAngleCacheKey>();

    [Header("Debug Overlay")]
    public bool showDebugOverlay = false;
    private string debugOverlay = "";
    private string lastLiveShotSolveSource = "none";
    private bool lastLiveShotSolveUsedCache;

    public enum FixedAngleRejectReason
    {
        None,
        Invalid,
        NetClipped,
        NetTooLow,
        NetTooHigh,
        SpeedTooHigh,
        SpeedTooLow
    }

    public string LastLiveShotSolveSource => lastLiveShotSolveSource;
    public bool LastLiveShotSolveUsedCache => lastLiveShotSolveUsedCache;
    public FixedAngleRejectReason LastFixedAngleRejectReason { get; private set; } = FixedAngleRejectReason.None;

    private void MarkLiveShotSolve(string source, bool usedCache)
    {
        lastLiveShotSolveSource = string.IsNullOrEmpty(source) ? "none" : source;
        lastLiveShotSolveUsedCache = usedCache;
    }

    private struct PreviewCacheKey : IEquatable<PreviewCacheKey>
    {
        public int startX;
        public int startY;
        public int targetX;
        public int targetZ;
        public int spinX;
        public int spinY;
        public int spinZ;
        public int baseType;
        public int modifier;

        public bool Equals(PreviewCacheKey other)
        {
            return startX == other.startX &&
                   startY == other.startY &&
                   targetX == other.targetX &&
                   targetZ == other.targetZ &&
                   spinX == other.spinX &&
                   spinY == other.spinY &&
                   spinZ == other.spinZ &&
                   baseType == other.baseType &&
                   modifier == other.modifier;
        }

        public override bool Equals(object obj)
        {
            return obj is PreviewCacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = startX;
                hash = (hash * 397) ^ startY;
                hash = (hash * 397) ^ targetX;
                hash = (hash * 397) ^ targetZ;
                hash = (hash * 397) ^ spinX;
                hash = (hash * 397) ^ spinY;
                hash = (hash * 397) ^ spinZ;
                hash = (hash * 397) ^ baseType;
                hash = (hash * 397) ^ modifier;
                return hash;
            }
        }
    }

    private struct FullShotCacheKey : IEquatable<FullShotCacheKey>
    {
        public PreviewCacheKey previewKey;
        public int clearance;

        public bool Equals(FullShotCacheKey other)
        {
            return clearance == other.clearance &&
                   previewKey.Equals(other.previewKey);
        }

        public override bool Equals(object obj)
        {
            return obj is FullShotCacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = previewKey.GetHashCode();
                hash = (hash * 397) ^ clearance;
                return hash;
            }
        }
    }

    private struct FixedSpeedCacheKey : IEquatable<FixedSpeedCacheKey>
    {
        public PreviewCacheKey previewKey;
        public int fixedV0;
        public int clearance;

        public bool Equals(FixedSpeedCacheKey other)
        {
            return fixedV0 == other.fixedV0 &&
                   clearance == other.clearance &&
                   previewKey.Equals(other.previewKey);
        }

        public override bool Equals(object obj)
        {
            return obj is FixedSpeedCacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = previewKey.GetHashCode();
                hash = (hash * 397) ^ fixedV0;
                hash = (hash * 397) ^ clearance;
                return hash;
            }
        }
    }

    private struct FixedAngleCacheKey : IEquatable<FixedAngleCacheKey>
    {
        public PreviewCacheKey previewKey;
        public int preferredThetaDeg;
        public int minThetaDeg;
        public int maxThetaDeg;
        public int clearance;
        public int maxClearance;

        public bool Equals(FixedAngleCacheKey other)
        {
            return preferredThetaDeg == other.preferredThetaDeg &&
                   minThetaDeg == other.minThetaDeg &&
                   maxThetaDeg == other.maxThetaDeg &&
                   clearance == other.clearance &&
                   maxClearance == other.maxClearance &&
                   previewKey.Equals(other.previewKey);
        }

        public override bool Equals(object obj)
        {
            return obj is FixedAngleCacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = previewKey.GetHashCode();
                hash = (hash * 397) ^ preferredThetaDeg;
                hash = (hash * 397) ^ minThetaDeg;
                hash = (hash * 397) ^ maxThetaDeg;
                hash = (hash * 397) ^ clearance;
                hash = (hash * 397) ^ maxClearance;
                return hash;
            }
        }
    }

    private class PreviewCacheEntry
    {
        public float v0;
        public float theta;
        public float landingX;
        public Vector3[] arcPoints;
        public int arcPointCount;
    }

    private enum LivePrewarmResult
    {
        Built,
        Skipped,
        Failed
    }

    private static readonly BaseShotType[] AllBaseShotTypesForLivePrewarm =
    {
        BaseShotType.Flat,
        BaseShotType.Topspin,
        BaseShotType.Slice,
        BaseShotType.Lob,
        BaseShotType.Drop
    };

    private static readonly ShotModifier[] AllModifiersForLivePrewarm =
    {
        ShotModifier.Normal,
        ShotModifier.Fast,
        ShotModifier.Slow
    };

    // ------------------------------------------------------------
    // 2. Initialisation
    // ------------------------------------------------------------
    void Awake()
    {
        if (previewArcToggleKey == KeyCode.A || previewArcToggleKey == KeyCode.Alpha0)
            previewArcToggleKey = KeyCode.Keypad0;

        if (solverComponent == null)
        {
            Debug.LogError("Assign ShotSolverComponent in the Inspector.");
            enabled = false;
            return;
        }

        if (previewArc != null)
            previewArc.Initialise(solverComponent.traj);

        hasLastPreviewKey = false;
    }

    void Start()
    {
        AutoFindPrewarmBounds();
        previewArcManuallyVisible = previewArcVisibleByDefault && IsBallStationaryForPreview();

        bool loadedPreviewFileCache = useFileBackedPreviewCache &&
                                      loadPreviewCacheOnStart &&
                                      TryLoadPreviewCacheFile();

        bool loadedLiveShotCache = useFileBackedLiveShotCache &&
                                   loadLiveShotCacheOnStart &&
                                   TryLoadLiveShotCacheFile();

        bool allowStartupPrewarm = prewarmPreviewCacheOnStart &&
                                   (!disableStartupPrewarmWhenPreviewArcDefaultOff || previewArcManuallyVisible);

        if (allowStartupPrewarm &&
            !(loadedPreviewFileCache && skipPrewarmWhenFileCacheLoaded))
        {
            prewarmCoroutine = StartCoroutine(PrewarmPreviewCacheCoroutine());
        }
        else if (loadedPreviewFileCache && logPreviewCacheFile)
        {
            Debug.Log($"[Aiming Cache File] Loaded {fileCacheLoadedEntries} preview entries, startup prewarm skipped.");
        }

        if (loadedLiveShotCache && logLiveShotCacheFile)
            Debug.Log($"[Live Shot Cache File] Loaded {liveShotCacheLoadedEntries} shot entries.");

        bool allowLiveShotPrewarm = useFileBackedLiveShotCache &&
                                    prewarmLiveShotCacheOnStart &&
                                    !(loadedLiveShotCache && skipLiveShotPrewarmWhenFileCacheLoaded);

        if (allowLiveShotPrewarm)
            livePrewarmCoroutine = StartCoroutine(PrewarmLiveShotCacheCoroutine());
        else if (loadedLiveShotCache && skipLiveShotPrewarmWhenFileCacheLoaded && logLiveShotPrewarmProgress)
            Debug.Log($"[Live Shot Cache] Prewarm skipped: loaded {liveShotCacheLoadedEntries} cached entries.");

        if (!previewArcManuallyVisible)
            ClearPreviewArcState();
    }

    void OnDisable()
    {
        if (prewarmCoroutine != null)
        {
            StopCoroutine(prewarmCoroutine);
            prewarmCoroutine = null;
        }

        if (livePrewarmCoroutine != null)
        {
            StopCoroutine(livePrewarmCoroutine);
            livePrewarmCoroutine = null;
        }

        prewarmInProgress = false;

        if (saveLiveShotCacheOnDisable)
            TrySaveLiveShotCacheFile();
    }

    void OnApplicationQuit()
    {
        if (saveLiveShotCacheOnDisable)
            TrySaveLiveShotCacheFile();
    }

    // ------------------------------------------------------------
    // 3. Helper: dynamic contact position
    //    Uses ball height when in hitting zone, otherwise fallback
    // ------------------------------------------------------------
    private Vector3 GetCurrentContactPos()
    {
        if (hitCtrl != null && hitCtrl.ballIsInHittingZone && hitCtrl.ball != null)
            return hitCtrl.ball.transform.position;

        if (contactPoint != null)
            return contactPoint.position;

        return transform.position;
    }

    private Vector3 GetPreviewContactPos()
    {
        if (useLiveBallForPreviewStart)
            return GetCurrentContactPos();

        if (contactPoint != null)
            return contactPoint.position;

        return transform.position;
    }

    private bool ShouldHidePreviewForLiveBall()
    {
        return hidePreviewWhileBallOnPlayerSide &&
               hitCtrl != null &&
               hitCtrl.ballIsInHittingZone &&
               hitCtrl.ball != null;
    }

    private void ClearPreviewArcState()
    {
        hasLastPreviewKey = false;
        lastV0 = float.NaN;
        lastTheta = float.NaN;
        previewArc?.ClearArc();
    }

    private bool IsBallStationaryForPreview()
    {
        if (!previewArcOnlyWhenBallStationary)
            return true;

        Rigidbody ballRb = hitCtrl != null ? hitCtrl.ball : null;
        if (ballRb == null)
            return true;

        float threshold = Mathf.Max(0f, previewArcStationarySpeedThreshold);
        return ballRb.linearVelocity.sqrMagnitude <= threshold * threshold;
    }

    private bool CanShowPreviewArcNow()
    {
        return previewArcManuallyVisible && IsBallStationaryForPreview();
    }

    private void HandlePreviewArcToggleInput()
    {
        if (previewArcToggleKey == KeyCode.None || !Input.GetKeyDown(previewArcToggleKey))
            return;

        if (previewArcManuallyVisible)
        {
            previewArcManuallyVisible = false;
            ClearPreviewArcState();

            if (logPreviewArcToggle)
                Debug.Log("[Aiming Preview] Arc hidden.");

            return;
        }

        if (!IsBallStationaryForPreview())
        {
            ClearPreviewArcState();

            if (logPreviewArcToggle)
                Debug.Log("[Aiming Preview] Arc can only be shown while the ball is stationary/out of play.");

            return;
        }

        previewArcManuallyVisible = true;
        hasLastPreviewKey = false;

        if (logPreviewArcToggle)
            Debug.Log("[Aiming Preview] Arc shown.");
    }

    // ------------------------------------------------------------
    // 4. Update loop — decide when to recompute the preview
    // ------------------------------------------------------------
    void Update()
    {
        HandlePreviewArcToggleInput();

        if (!IsSetupValid())
        {
            ClearPreviewArcState();
            return;
        }

        if (ShouldHidePreviewForLiveBall() || !CanShowPreviewArcNow())
        {
            if (previewArcManuallyVisible && !IsBallStationaryForPreview())
                previewArcManuallyVisible = false;

            ClearPreviewArcState();
            return;
        }

        Vector3 currentContactPos = GetPreviewContactPos();
        GetCanonicalSolverCoordinates(
            currentContactPos,
            reticle.position,
            out Vector2 startPos2D,
            out float xTarget,
            out _
        );

        if (!IsTargetValid(startPos2D, xTarget))
        {
            ClearPreviewArcState();
            return;
        }

        Vector3 previewSpin = ComputePreviewSpin();
        PreviewCacheKey key = BuildPreviewCacheKey(currentContactPos, reticle.position, previewSpin);

        if (hasLastPreviewKey && key.Equals(lastPreviewKey))
        {
            if (redrawCachedArcWithLiveReticle &&
                Time.time >= nextCachedArcRedrawTime &&
                previewCache.TryGetValue(key, out PreviewCacheEntry sameKeyEntry))
            {
                RefreshEntryArc(sameKeyEntry, currentContactPos, startPos2D, previewSpin);
                ApplyPreviewEntry(sameKeyEntry, "cache redraw");
                nextCachedArcRedrawTime = Time.time + GetCachedArcRedrawInterval();
            }

            return;
        }

        if (previewCache.TryGetValue(key, out PreviewCacheEntry cached))
        {
            RefreshEntryArc(cached, currentContactPos, startPos2D, previewSpin);
            ApplyPreviewEntry(cached, "cache");
            lastPreviewKey = key;
            hasLastPreviewKey = true;
            return;
        }

        if (Time.time < nextSolveTime)
            return;

        nextSolveTime = Time.time + Mathf.Max(0.01f, solverInterval);
        UpdateAimingPreview(currentContactPos, startPos2D, previewSpin, key);
    }

    // ------------------------------------------------------------
    // 5. Compute preview spin (for Magnus-aware solver)
    // ------------------------------------------------------------
    // This mirrors ShotComputationSolver’s spin logic at a high level:
    //   • Query BaseShotLibrary + ModifierLibrary
    //   • Combine their spin values
    //   • Map to a 3D spin axis (here: ±Z for topspin/backspin)
    public void SetShotSolveSpinOverride(Vector3 spinRadPerSecond)
    {
        hasShotSolveSpinOverride = true;
        shotSolveSpinOverride = spinRadPerSecond;
    }

    public void ClearShotSolveSpinOverride()
    {
        hasShotSolveSpinOverride = false;
        shotSolveSpinOverride = Vector3.zero;
    }
    private Vector3 ComputePreviewSpin()
    {
        if (hasShotSolveSpinOverride)
            return shotSolveSpinOverride;

        if (hitCtrl == null)
            return Vector3.zero;

        BaseShotType baseType = hitCtrl.currentBaseShotType;
        ShotModifier modifier = hitCtrl.currentModifier;

        ShotIntent modIntent = ModifierLibrary.Get(modifier);
        float spinIntent = Mathf.Clamp01(hitCtrl.currentSpinIntent);
        float contactHeight = GetCurrentContactPos().y;
        float heightSpinMultiplier = (baseType == BaseShotType.Slice || baseType == BaseShotType.Drop)
            ? 1f
            : BaseShotLibrary.GetHeightSpinMultiplier(contactHeight);
        float spinRpm = (BaseShotLibrary.GetSpinRpm(baseType, spinIntent) + modIntent.spinRpm) * heightSpinMultiplier;

        // Convention:
        //   Positive spinAmount ? topspin (-Z axis)
        //   Negative spinAmount ? backspin (+Z axis)
        // Velocity is in X/Y plane, so ? × v gives vertical + sideways Magnus.
        return new Vector3(0f, 0f, -BaseShotLibrary.RpmToRadPerSecond(spinRpm));
    }

    private PreviewCacheKey BuildPreviewCacheKey(Vector3 contactWorld, Vector3 targetWorld, Vector3 spin)
    {
        BaseShotType baseType = hitCtrl != null ? hitCtrl.currentBaseShotType : BaseShotType.Flat;
        ShotModifier modifier = hitCtrl != null ? hitCtrl.currentModifier : ShotModifier.Normal;
        return BuildPreviewCacheKey(contactWorld, targetWorld, spin, baseType, modifier);
    }

    private PreviewCacheKey BuildPreviewCacheKey(
        Vector3 contactWorld,
        Vector3 targetWorld,
        Vector3 spin,
        BaseShotType baseType,
        ShotModifier modifier)
    {
        return new PreviewCacheKey
        {
            startX = Quantize(contactWorld.x, contactXCacheStep),
            startY = Quantize(contactWorld.y, contactYCacheStep),
            targetX = Quantize(targetWorld.x, reticleXCacheStep),
            targetZ = Quantize(targetWorld.z, reticleZCacheStep),
            spinX = Quantize(spin.x, spinCacheStep),
            spinY = Quantize(spin.y, spinCacheStep),
            spinZ = Quantize(spin.z, spinCacheStep),
            baseType = (int)baseType,
            modifier = (int)modifier
        };
    }

    private FullShotCacheKey BuildFullShotCacheKey(PreviewCacheKey previewKey, float desiredNetClearance)
    {
        return new FullShotCacheKey
        {
            previewKey = previewKey,
            clearance = Quantize(ResolveNetClearance(desiredNetClearance), 0.05f)
        };
    }

    private FixedSpeedCacheKey BuildFixedSpeedCacheKey(PreviewCacheKey previewKey, float fixedV0, float desiredNetClearance)
    {
        return new FixedSpeedCacheKey
        {
            previewKey = previewKey,
            fixedV0 = Quantize(fixedV0, 0.25f),
            clearance = Quantize(ResolveNetClearance(desiredNetClearance), 0.05f)
        };
    }

    private FixedAngleCacheKey BuildFixedAngleCacheKey(
        PreviewCacheKey previewKey,
        float preferredTheta,
        float minTheta,
        float maxTheta,
        float desiredNetClearance,
        float maxNetClearance)
    {
        return new FixedAngleCacheKey
        {
            previewKey = previewKey,
            preferredThetaDeg = Quantize(preferredTheta * Mathf.Rad2Deg, 0.25f),
            minThetaDeg = Quantize(minTheta * Mathf.Rad2Deg, 0.25f),
            maxThetaDeg = Quantize(maxTheta * Mathf.Rad2Deg, 0.25f),
            clearance = Quantize(ResolveNetClearance(desiredNetClearance), 0.05f),
            maxClearance = float.IsFinite(maxNetClearance) ? Quantize(maxNetClearance, 0.05f) : -1
        };
    }

    private static int Quantize(float value, float step)
    {
        float safeStep = Mathf.Max(0.0001f, Mathf.Abs(step));
        return Mathf.RoundToInt(value / safeStep);
    }

    private float ResolveNetClearance(float desiredNetClearance)
    {
        if (float.IsFinite(desiredNetClearance) && desiredNetClearance >= 0f)
            return desiredNetClearance;

        return solverComponent != null ? Mathf.Max(0f, solverComponent.netMargin) : 0.25f;
    }

    private float GetCachedArcRedrawInterval()
    {
        return Mathf.Max(MinCachedArcRedrawInterval, cachedArcRedrawInterval);
    }

    private int GetMaxPreviewCacheEntries()
    {
        int maxEntries = Mathf.Max(8, maxPreviewCacheEntries);

        if (prewarmPreviewCacheOnStart || useFileBackedPreviewCache)
            maxEntries = Mathf.Max(maxEntries, prewarmCacheCapacity);

        if (useFileBackedPreviewCache)
            maxEntries = Mathf.Max(maxEntries, 2048);

        return maxEntries;
    }

    private void AutoFindPrewarmBounds()
    {
        if (reticleBoundsSource == null && reticle != null)
            reticleBoundsSource = reticle.GetComponent<UIWorldReticle>() ??
                                  reticle.GetComponentInParent<UIWorldReticle>();

        if (reticleBoundsSource == null)
            return;

        if (prewarmTargetMinBound == null)
            prewarmTargetMinBound = reticleBoundsSource.minBound;

        if (prewarmTargetMaxBound == null)
            prewarmTargetMaxBound = reticleBoundsSource.maxBound;
    }

    private bool CanBuildPreviewCacheEntries()
    {
        return previewArc != null &&
               reticle != null &&
               contactPoint != null &&
               solverComponent != null &&
               solverComponent.solver != null &&
               solverComponent.traj != null &&
               previewArc.enabledInGame;
    }

    private Vector2 GetPrewarmXRange()
    {
        if (prewarmTargetMinBound != null && prewarmTargetMaxBound != null)
        {
            float min = Mathf.Min(prewarmTargetMinBound.position.x, prewarmTargetMaxBound.position.x);
            float max = Mathf.Max(prewarmTargetMinBound.position.x, prewarmTargetMaxBound.position.x);
            return new Vector2(min, max);
        }

        return new Vector2(
            Mathf.Min(prewarmFallbackTargetXRange.x, prewarmFallbackTargetXRange.y),
            Mathf.Max(prewarmFallbackTargetXRange.x, prewarmFallbackTargetXRange.y)
        );
    }

    private Vector2 GetPrewarmZRange()
    {
        if (prewarmTargetMinBound != null && prewarmTargetMaxBound != null)
        {
            float min = Mathf.Min(prewarmTargetMinBound.position.z, prewarmTargetMaxBound.position.z);
            float max = Mathf.Max(prewarmTargetMinBound.position.z, prewarmTargetMaxBound.position.z);
            return new Vector2(min, max);
        }

        return new Vector2(
            Mathf.Min(prewarmFallbackTargetZRange.x, prewarmFallbackTargetZRange.y),
            Mathf.Max(prewarmFallbackTargetZRange.x, prewarmFallbackTargetZRange.y)
        );
    }

    private static int CountPrewarmCells(Vector2 range, float step)
    {
        float span = Mathf.Max(0f, range.y - range.x);
        return Mathf.Max(1, Mathf.FloorToInt(span / Mathf.Max(0.0001f, step)) + 1);
    }

    private void ApplyPrewarmCellBudget(Vector2 xRange, Vector2 zRange, ref float xStep, ref float zStep)
    {
        ApplyPrewarmCellBudget(xRange, zRange, ref xStep, ref zStep, prewarmMaxStartupCells);
    }

    private void ApplyPrewarmCellBudget(Vector2 xRange, Vector2 zRange, ref float xStep, ref float zStep, int maxStartupCells)
    {
        int maxCells = Mathf.Max(1, maxStartupCells);
        int xCells = CountPrewarmCells(xRange, xStep);
        int zCells = CountPrewarmCells(zRange, zStep);
        int totalCells = xCells * zCells;

        if (totalCells <= maxCells)
            return;

        float scale = Mathf.Sqrt(totalCells / (float)maxCells);
        xStep *= scale;
        zStep *= scale;
    }

    private bool ShouldPausePrewarmForReticleMotion(
        ref Vector3 lastReticlePosition,
        ref float lastReticleMoveTime)
    {
        if (!prewarmPauseWhileReticleMoving || reticle == null)
            return false;

        float threshold = Mathf.Max(0.0001f, prewarmReticleMoveThreshold);
        if ((reticle.position - lastReticlePosition).sqrMagnitude > threshold * threshold)
        {
            lastReticlePosition = reticle.position;
            lastReticleMoveTime = Time.realtimeSinceStartup;
            return true;
        }

        return Time.realtimeSinceStartup - lastReticleMoveTime < Mathf.Max(0f, prewarmIdleSecondsBeforeSolving);
    }

    private IEnumerator PrewarmPreviewCacheCoroutine()
    {
        prewarmInProgress = true;
        prewarmBuiltCount = 0;
        prewarmSkippedCount = 0;

        AutoFindPrewarmBounds();

        const float maxWaitSeconds = 2f;
        float waitStart = Time.realtimeSinceStartup;
        while (!CanBuildPreviewCacheEntries() &&
               Time.realtimeSinceStartup - waitStart < maxWaitSeconds)
        {
            yield return null;
        }

        if (!CanBuildPreviewCacheEntries())
        {
            prewarmInProgress = false;
            prewarmCoroutine = null;

            if (logPrewarmProgress)
                Debug.Log("[Aiming Cache] Prewarm skipped: preview solver setup is not ready.");

            yield break;
        }

        Vector3 contactWorld = GetPreviewContactPos();
        Vector3 previewSpin = ComputePreviewSpin();
        Vector2 xRange = GetPrewarmXRange();
        Vector2 zRange = GetPrewarmZRange();
        float xStep = Mathf.Max(0.05f, Mathf.Abs(prewarmTargetXStep));
        float zStep = Mathf.Max(0.05f, Mathf.Abs(prewarmTargetZStep));
        ApplyPrewarmCellBudget(xRange, zRange, ref xStep, ref zStep);

        int xCells = CountPrewarmCells(xRange, xStep);
        int zCells = CountPrewarmCells(zRange, zStep);
        int targetCells = xCells * zCells;
        int solvesPerFrame = Mathf.Clamp(
            prewarmSolvesPerFrame,
            1,
            Mathf.Max(1, prewarmMaxSolvesPerFrame)
        );
        int solvesThisFrame = 0;
        Vector3 lastReticlePosition = reticle.position;
        float lastReticleMoveTime = Time.realtimeSinceStartup;

        if (logPrewarmProgress)
        {
            Debug.Log(
                $"[Aiming Cache] Prewarm started: targetCells~={targetCells}, solvesPerFrame={solvesPerFrame}, " +
                $"x={xRange.x:F2}..{xRange.y:F2}, z={zRange.x:F2}..{zRange.y:F2}, step=({xStep:F2},{zStep:F2})"
            );
        }

        for (float x = xRange.x; x <= xRange.y + xStep * 0.5f; x += xStep)
        {
            Vector3 targetAtX = new Vector3(x, contactWorld.y, contactWorld.z);
            GetCanonicalSolverCoordinates(
                contactWorld,
                targetAtX,
                out Vector2 startPos2D,
                out float canonicalTargetX,
                out _
            );

            if (!IsTargetValid(startPos2D, canonicalTargetX))
                continue;

            for (float z = zRange.x; z <= zRange.y + zStep * 0.5f; z += zStep)
            {
                while (ShouldPausePrewarmForReticleMotion(ref lastReticlePosition, ref lastReticleMoveTime))
                    yield return null;

                Vector3 targetWorld = new Vector3(x, reticle.position.y, z);
                PreviewCacheKey key = BuildPreviewCacheKey(contactWorld, targetWorld, previewSpin);

                if (previewCache.ContainsKey(key))
                {
                    prewarmSkippedCount++;
                    continue;
                }

                float solveStartTime = Time.realtimeSinceStartup;
                bool builtEntry = TryBuildPreviewCacheEntry(contactWorld, startPos2D, targetWorld, previewSpin, key, out _);
                float solveMs = (Time.realtimeSinceStartup - solveStartTime) * 1000f;

                if (builtEntry)
                    prewarmBuiltCount++;
                else
                    prewarmSkippedCount++;

                if (prewarmAbortIfSingleSolveExceedsMs > 0f &&
                    solveMs > prewarmAbortIfSingleSolveExceedsMs)
                {
                    prewarmInProgress = false;
                    prewarmCoroutine = null;

                    if (logPrewarmProgress)
                    {
                        Debug.LogWarning(
                            $"[Aiming Cache] Prewarm aborted: one solve took {solveMs:F1}ms, " +
                            $"limit={prewarmAbortIfSingleSolveExceedsMs:F1}ms. Built={prewarmBuiltCount}, skipped={prewarmSkippedCount}."
                        );
                    }

                    yield break;
                }

                solvesThisFrame++;
                if (solvesThisFrame >= solvesPerFrame)
                {
                    solvesThisFrame = 0;
                    yield return null;
                }
            }
        }

        prewarmInProgress = false;
        prewarmCoroutine = null;

        if (useFileBackedPreviewCache && savePreviewCacheAfterPrewarm)
            TrySavePreviewCacheFile();

        if (useFileBackedLiveShotCache)
            TrySaveLiveShotCacheFile();

        if (logPrewarmProgress)
        {
            Debug.Log(
                $"[Aiming Cache] Prewarm complete: built={prewarmBuiltCount}, skipped={prewarmSkippedCount}, cache={previewCache.Count}, " +
                $"x={xRange.x:F2}..{xRange.y:F2}, z={zRange.x:F2}..{zRange.y:F2}, step=({xStep:F2},{zStep:F2})"
            );
        }
    }

    private IEnumerator PrewarmLiveShotCacheCoroutine()
    {
        livePrewarmBuiltCount = 0;
        livePrewarmSkippedCount = 0;

        AutoFindPrewarmBounds();

        const float maxWaitSeconds = 2f;
        float waitStart = Time.realtimeSinceStartup;
        while (!IsShotSolverSetupValid() && Time.realtimeSinceStartup - waitStart < maxWaitSeconds)
            yield return null;

        if (!IsShotSolverSetupValid())
        {
            livePrewarmCoroutine = null;

            if (logLiveShotPrewarmProgress)
                Debug.Log("[Live Shot Cache] Prewarm skipped: live shot solver setup is not ready.");

            yield break;
        }

        Vector3 contactWorld = GetLivePrewarmContactPos();
        Vector2 xRange = GetPrewarmXRange();
        Vector2 zRange = GetPrewarmZRange();
        float xStep = Mathf.Max(0.05f, Mathf.Abs(prewarmTargetXStep));
        float zStep = Mathf.Max(0.05f, Mathf.Abs(prewarmTargetZStep));
        ApplyPrewarmCellBudget(xRange, zRange, ref xStep, ref zStep, livePrewarmMaxStartupCells);

        BaseShotType[] baseTypes = GetLivePrewarmBaseTypes();
        ShotModifier[] modifiers = GetLivePrewarmModifiers();
        int modesPerCell = Mathf.Max(1, baseTypes.Length * modifiers.Length) *
                           ((prewarmLiveShotFullSolves ? 1 : 0) + (prewarmLiveShotFixedAngles ? 1 : 0));
        int xCells = CountPrewarmCells(xRange, xStep);
        int zCells = CountPrewarmCells(zRange, zStep);
        int targetCells = xCells * zCells;
        int solvesPerFrame = Mathf.Clamp(
            livePrewarmSolvesPerFrame,
            1,
            Mathf.Max(1, livePrewarmMaxSolvesPerFrame)
        );
        int solvesThisFrame = 0;

        if (modesPerCell <= 0)
        {
            livePrewarmCoroutine = null;
            yield break;
        }

        if (logLiveShotPrewarmProgress)
        {
            Debug.Log(
                $"[Live Shot Cache] Prewarm started: targetCells~={targetCells}, modesPerCell={modesPerCell}, " +
                $"solvesPerFrame={solvesPerFrame}, x={xRange.x:F2}..{xRange.y:F2}, z={zRange.x:F2}..{zRange.y:F2}, " +
                $"step=({xStep:F2},{zStep:F2})"
            );
        }

        for (float x = xRange.x; x <= xRange.y + xStep * 0.5f; x += xStep)
        {
            Vector3 targetAtX = new Vector3(x, contactWorld.y, contactWorld.z);
            GetCanonicalSolverCoordinates(
                contactWorld,
                targetAtX,
                out Vector2 startPos2D,
                out float canonicalTargetX,
                out _
            );

            if (!IsTargetValid(startPos2D, canonicalTargetX))
                continue;

            for (float z = zRange.x; z <= zRange.y + zStep * 0.5f; z += zStep)
            {
                Vector3 targetWorld = new Vector3(x, reticle != null ? reticle.position.y : 0f, z);

                for (int typeIndex = 0; typeIndex < baseTypes.Length; typeIndex++)
                {
                    BaseShotType baseType = baseTypes[typeIndex];
                    float clearance = GetLivePrewarmClearance(baseType);

                    for (int modifierIndex = 0; modifierIndex < modifiers.Length; modifierIndex++)
                    {
                        ShotModifier modifier = modifiers[modifierIndex];
                        Vector3 shotSpin = ComputeLivePrewarmSpin(baseType, modifier, contactWorld.y);

                        if (prewarmLiveShotFullSolves)
                        {
                            Vector3 neutralSpin = neutralShotUsesCurrentSpin ? shotSpin : Vector3.zero;
                            LivePrewarmResult result = TryPrewarmLiveFullShot(contactWorld, targetWorld, neutralSpin, baseType, modifier, clearance, out float solveMs);
                            if (ShouldAbortLivePrewarm(result, solveMs))
                                yield break;

                            solvesThisFrame++;
                            if (solvesThisFrame >= solvesPerFrame)
                            {
                                solvesThisFrame = 0;
                                yield return null;
                            }
                        }

                        if (prewarmLiveShotFixedAngles)
                        {
                            LivePrewarmResult result = TryPrewarmLiveFixedAngleShot(contactWorld, targetWorld, shotSpin, baseType, modifier, clearance, out float solveMs);
                            if (ShouldAbortLivePrewarm(result, solveMs))
                                yield break;

                            solvesThisFrame++;
                            if (solvesThisFrame >= solvesPerFrame)
                            {
                                solvesThisFrame = 0;
                                yield return null;
                            }
                        }
                    }
                }
            }
        }

        livePrewarmCoroutine = null;

        if (useFileBackedLiveShotCache)
            TrySaveLiveShotCacheFile();

        if (logLiveShotPrewarmProgress)
        {
            Debug.Log(
                $"[Live Shot Cache] Prewarm complete: built={livePrewarmBuiltCount}, skipped={livePrewarmSkippedCount}, " +
                $"full={fullShotCache.Count}, fixedAngle={fixedAngleCache.Count}, fixedSpeed={fixedSpeedCache.Count}"
            );
        }
    }

    private Vector3 GetLivePrewarmContactPos()
    {
        if (contactPoint != null)
            return contactPoint.position;

        return transform.position;
    }

    private BaseShotType[] GetLivePrewarmBaseTypes()
    {
        if (prewarmLiveShotAllBaseTypes)
            return AllBaseShotTypesForLivePrewarm;

        return new[] { hitCtrl != null ? hitCtrl.currentBaseShotType : BaseShotType.Flat };
    }

    private ShotModifier[] GetLivePrewarmModifiers()
    {
        if (prewarmLiveShotAllModifiers)
            return AllModifiersForLivePrewarm;

        return new[] { hitCtrl != null ? hitCtrl.currentModifier : ShotModifier.Normal };
    }

    private float GetLivePrewarmClearance(BaseShotType baseType)
    {
        if (hitCtrl == null || !hitCtrl.useSituationDefaultNetClearance)
            return ResolveNetClearance(-1f);

        ShotClearanceProfile profile;
        switch (baseType)
        {
            case BaseShotType.Topspin:
                profile = hitCtrl.topspinClearance;
                break;
            case BaseShotType.Slice:
                profile = hitCtrl.sliceClearance;
                break;
            case BaseShotType.Lob:
                profile = hitCtrl.lobClearance;
                break;
            case BaseShotType.Drop:
                profile = hitCtrl.dropClearance;
                break;
            case BaseShotType.Flat:
            default:
                profile = hitCtrl.flatClearance;
                break;
        }

        float minClearance = Mathf.Max(0f, profile.minClearance);
        float maxClearance = Mathf.Max(minClearance, profile.maxClearance);
        float clearance = Mathf.Clamp(profile.baseClearance, minClearance, maxClearance);
        return float.IsFinite(clearance) && clearance > 0f ? clearance : ResolveNetClearance(-1f);
    }

    private Vector3 ComputeLivePrewarmSpin(BaseShotType baseType, ShotModifier modifier, float contactHeight)
    {
        ShotIntent modIntent = ModifierLibrary.Get(modifier);
        float spinIntent = hitCtrl != null ? Mathf.Clamp01(hitCtrl.currentSpinIntent) : 0f;
        float heightSpinMultiplier = (baseType == BaseShotType.Slice || baseType == BaseShotType.Drop)
            ? 1f
            : BaseShotLibrary.GetHeightSpinMultiplier(contactHeight);
        float spinRpm = (BaseShotLibrary.GetSpinRpm(baseType, spinIntent) + modIntent.spinRpm) * heightSpinMultiplier;
        return new Vector3(0f, 0f, -BaseShotLibrary.RpmToRadPerSecond(spinRpm));
    }

    private LivePrewarmResult TryPrewarmLiveFullShot(
        Vector3 contactWorld,
        Vector3 targetWorld,
        Vector3 spin,
        BaseShotType baseType,
        ShotModifier modifier,
        float clearance,
        out float solveMs)
    {
        solveMs = 0f;
        GetCanonicalSolverCoordinates(
            contactWorld,
            targetWorld,
            out Vector2 startPos2D,
            out float xTarget,
            out _
        );

        if (!IsTargetValid(startPos2D, xTarget))
            return LivePrewarmResult.Failed;

        PreviewCacheKey previewKey = BuildPreviewCacheKey(contactWorld, targetWorld, spin, baseType, modifier);
        FullShotCacheKey fullKey = BuildFullShotCacheKey(previewKey, clearance);

        if (fullShotCache.ContainsKey(fullKey))
            return LivePrewarmResult.Skipped;

        float solveStartTime = Time.realtimeSinceStartup;
        var result = solverComponent.solver.SolveForReticle(
            startPos2D,
            xTarget,
            netX,
            netHeight,
            ResolveNetClearance(clearance),
            spin
        );
        solveMs = (Time.realtimeSinceStartup - solveStartTime) * 1000f;

        if (!float.IsFinite(result.v0) || !float.IsFinite(result.theta))
            return LivePrewarmResult.Failed;

        CacheFullShotResult(fullKey, result.v0, result.theta);
        return LivePrewarmResult.Built;
    }

    private LivePrewarmResult TryPrewarmLiveFixedAngleShot(
        Vector3 contactWorld,
        Vector3 targetWorld,
        Vector3 spin,
        BaseShotType baseType,
        ShotModifier modifier,
        float clearance,
        out float solveMs)
    {
        solveMs = 0f;
        float requiredClearance = ResolveNetClearance(clearance);
        ShotHeightRange heightRange = BaseShotLibrary.GetClearanceDrivenHeightRange(
            baseType,
            contactWorld.y,
            netHeight,
            Mathf.Abs(netX - contactWorld.x),
            requiredClearance
        );
        float defaultAngleDeg = heightRange.fallbackDefaultAngleDeg;
        float preferredTheta = defaultAngleDeg * Mathf.Deg2Rad;
        float minTheta = heightRange.MinAngleDeg(defaultAngleDeg) * Mathf.Deg2Rad;
        float maxTheta = heightRange.MaxAngleDeg(defaultAngleDeg) * Mathf.Deg2Rad;
        const float emergencyLiftExtraAngleDeg = 10f;
        float emergencyMaxTheta = Mathf.Min(
            Mathf.Max(heightRange.MaxAngleDeg(defaultAngleDeg), defaultAngleDeg + emergencyLiftExtraAngleDeg),
            35f
        ) * Mathf.Deg2Rad;

        PreviewCacheKey previewKey = BuildPreviewCacheKey(contactWorld, targetWorld, spin, baseType, modifier);
        FixedAngleCacheKey fixedKey = BuildFixedAngleCacheKey(
            previewKey,
            preferredTheta,
            minTheta,
            emergencyMaxTheta,
            requiredClearance,
            heightRange.maxNetClearance
        );

        if (fixedAngleCache.ContainsKey(fixedKey))
            return LivePrewarmResult.Skipped;

        float solveStartTime = Time.realtimeSinceStartup;
        var result = GetShotParametersForAngleRangeCached(
            contactWorld,
            targetWorld,
            spin,
            baseType,
            modifier,
            preferredTheta,
            minTheta,
            emergencyMaxTheta,
            requiredClearance,
            heightRange.maxNetClearance
        );
        solveMs = (Time.realtimeSinceStartup - solveStartTime) * 1000f;

        return float.IsFinite(result.v0) && float.IsFinite(result.theta)
            ? LivePrewarmResult.Built
            : LivePrewarmResult.Failed;
    }

    private bool ShouldAbortLivePrewarm(LivePrewarmResult result, float solveMs)
    {
        if (result == LivePrewarmResult.Built)
            livePrewarmBuiltCount++;
        else
            livePrewarmSkippedCount++;

        float abortMs = livePrewarmAbortIfSingleSolveExceedsMs;
        if (abortMs <= 0f || solveMs <= abortMs)
            return false;

        livePrewarmCoroutine = null;

        if (logLiveShotPrewarmProgress)
        {
            Debug.LogWarning(
                $"[Live Shot Cache] Prewarm aborted: one solve took {solveMs:F1}ms, " +
                $"limit={abortMs:F1}ms. Built={livePrewarmBuiltCount}, skipped={livePrewarmSkippedCount}."
            );
        }

        return true;
    }
    private string GetPreviewCacheFilePath()
    {
        string fileName = string.IsNullOrWhiteSpace(previewCacheFileName)
            ? "aiming_preview_cache_v1.bin"
            : previewCacheFileName;

        return Path.Combine(Application.persistentDataPath, fileName);
    }

    private string GetLiveShotCacheFilePath()
    {
        string fileName = string.IsNullOrWhiteSpace(liveShotCacheFileName)
            ? "live_shot_solver_cache_v1.bin"
            : liveShotCacheFileName;

        return Path.Combine(Application.persistentDataPath, fileName);
    }

    private int GetMaxLiveShotCacheEntries()
    {
        return Mathf.Max(64, maxLiveShotCacheEntriesPerMode);
    }

    private bool CanUseFileBackedLiveShotCache()
    {
        return useFileBackedLiveShotCache &&
               solverComponent != null &&
               solverComponent.traj != null &&
               solverComponent.solver != null;
    }

    private bool CanUseFileBackedPreviewCache()
    {
        return useFileBackedPreviewCache &&
               !useLiveBallForPreviewStart &&
               previewArc != null &&
               contactPoint != null &&
               solverComponent != null &&
               solverComponent.traj != null &&
               solverComponent.solver != null;
    }

    private static string F(float value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string V2(Vector2 value)
    {
        return $"{F(value.x)},{F(value.y)}";
    }

    private static string V3(Vector3 value)
    {
        return $"{F(value.x)},{F(value.y)},{F(value.z)}";
    }

    private string BuildPreviewCacheSignature()
    {
        AutoFindPrewarmBounds();

        Vector3 contact = contactPoint != null ? contactPoint.position : transform.position;
        Vector2 xRange = GetPrewarmXRange();
        Vector2 zRange = GetPrewarmZRange();
        DragBallistics drag = solverComponent.traj != null ? solverComponent.traj.phys : null;
        MagnusBallistics magnus = solverComponent.traj != null ? solverComponent.traj.magnus : null;

        string dragSignature = drag == null
            ? "drag=null"
            : $"drag={F(drag.gravity)},{F(drag.airDensity)},{F(drag.Cd)},{F(drag.radius)},{F(drag.mass)},{F(drag.k)}";

        string magnusSignature = magnus == null
            ? "magnus=null"
            : $"magnus={F(magnus.airDensity)},{F(magnus.ballRadius)},{F(magnus.mass)},{F(magnus.magnusCoefficient)},{F(magnus.spinDecayRate)},{F(magnus.baseCd)},{F(magnus.spinDragCoefficient)}";

        return
            $"fileVersion={PreviewCacheFileVersion}|" +
            $"arcRes={(previewArc != null ? previewArc.MaxPointCount : 0)}|" +
            $"reticleStep={F(reticleXCacheStep)},{F(reticleZCacheStep)}|" +
            $"contactStep={F(contactXCacheStep)},{F(contactYCacheStep)}|" +
            $"spinStep={F(spinCacheStep)}|" +
            $"prewarmStep={F(prewarmTargetXStep)},{F(prewarmTargetZStep)}|" +
            $"prewarmMaxCells={prewarmMaxStartupCells}|" +
            $"contact={V3(contact)}|" +
            $"xRange={V2(xRange)}|zRange={V2(zRange)}|" +
            $"net={F(netX)},{F(netHeight)},{F(solverComponent.netMargin)}|" +
            $"solver={F(solverComponent.minSpeed)},{F(solverComponent.maxSpeed)},{F(solverComponent.minAngleDeg)},{F(solverComponent.maxAngleDeg)}|" +
            $"{dragSignature}|{magnusSignature}";
    }

    private bool TryLoadPreviewCacheFile()
    {
        fileCacheLoadedEntries = 0;

        if (!CanUseFileBackedPreviewCache())
        {
            if (logPreviewCacheFile && useFileBackedPreviewCache && useLiveBallForPreviewStart)
                Debug.Log("[Aiming Cache File] Skipped load: file cache is disabled while live preview contact is enabled.");

            return false;
        }

        string path = GetPreviewCacheFilePath();
        if (!File.Exists(path))
        {
            if (logPreviewCacheFile)
                Debug.Log($"[Aiming Cache File] No cache file found at {path}");

            return false;
        }

        try
        {
            using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                int magic = reader.ReadInt32();
                int version = reader.ReadInt32();
                string savedSignature = reader.ReadString();
                string currentSignature = BuildPreviewCacheSignature();

                if (magic != PreviewCacheFileMagic || version != PreviewCacheFileVersion)
                {
                    if (logPreviewCacheFile)
                        Debug.LogWarning($"[Aiming Cache File] Ignored cache with unsupported header at {path}");

                    return false;
                }

                if (!string.Equals(savedSignature, currentSignature, StringComparison.Ordinal))
                {
                    if (logPreviewCacheFile)
                        Debug.Log("[Aiming Cache File] Ignored cache because settings changed. It will rebuild with current settings.");

                    return false;
                }

                int entryCount = Mathf.Clamp(reader.ReadInt32(), 0, GetMaxPreviewCacheEntries());
                previewCache.Clear();
                previewCacheOrder.Clear();

                for (int i = 0; i < entryCount; i++)
                {
                    PreviewCacheKey key = ReadPreviewCacheKey(reader);
                    PreviewCacheEntry entry = ReadPreviewCacheEntry(reader);

                    if (entry != null)
                        AddPreviewEntry(key, entry);
                }
            }

            fileCacheLoadedEntries = previewCache.Count;

            if (logPreviewCacheFile)
                Debug.Log($"[Aiming Cache File] Loaded {fileCacheLoadedEntries} entries from {path}");

            return fileCacheLoadedEntries > 0;
        }
        catch (Exception ex)
        {
            if (logPreviewCacheFile)
                Debug.LogWarning($"[Aiming Cache File] Failed to load cache: {ex.Message}");

            return false;
        }
    }

    private bool TrySavePreviewCacheFile()
    {
        fileCacheSavedEntries = 0;

        if (!CanUseFileBackedPreviewCache() || previewCache.Count == 0)
            return false;

        string path = GetPreviewCacheFilePath();

        try
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            List<KeyValuePair<PreviewCacheKey, PreviewCacheEntry>> entries =
                new List<KeyValuePair<PreviewCacheKey, PreviewCacheEntry>>();

            foreach (KeyValuePair<PreviewCacheKey, PreviewCacheEntry> pair in previewCache)
            {
                if (IsSerializablePreviewEntry(pair.Value))
                    entries.Add(pair);
            }

            if (entries.Count == 0)
                return false;

            using (FileStream stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(PreviewCacheFileMagic);
                writer.Write(PreviewCacheFileVersion);
                writer.Write(BuildPreviewCacheSignature());
                writer.Write(entries.Count);

                foreach (KeyValuePair<PreviewCacheKey, PreviewCacheEntry> pair in entries)
                {
                    WritePreviewCacheKey(writer, pair.Key);
                    WritePreviewCacheEntry(writer, pair.Value);
                }
            }

            fileCacheSavedEntries = entries.Count;

            if (logPreviewCacheFile)
                Debug.Log($"[Aiming Cache File] Saved {fileCacheSavedEntries} entries to {path}");

            return fileCacheSavedEntries > 0;
        }
        catch (Exception ex)
        {
            if (logPreviewCacheFile)
                Debug.LogWarning($"[Aiming Cache File] Failed to save cache: {ex.Message}");

            return false;
        }
    }

    private bool IsSerializablePreviewEntry(PreviewCacheEntry entry)
    {
        return entry != null &&
               entry.arcPoints != null &&
               entry.arcPointCount > 1 &&
               entry.arcPointCount <= entry.arcPoints.Length &&
               float.IsFinite(entry.v0) &&
               float.IsFinite(entry.theta);
    }

    private void WritePreviewCacheKey(BinaryWriter writer, PreviewCacheKey key)
    {
        writer.Write(key.startX);
        writer.Write(key.startY);
        writer.Write(key.targetX);
        writer.Write(key.targetZ);
        writer.Write(key.spinX);
        writer.Write(key.spinY);
        writer.Write(key.spinZ);
        writer.Write(key.baseType);
        writer.Write(key.modifier);
    }

    private PreviewCacheKey ReadPreviewCacheKey(BinaryReader reader)
    {
        return new PreviewCacheKey
        {
            startX = reader.ReadInt32(),
            startY = reader.ReadInt32(),
            targetX = reader.ReadInt32(),
            targetZ = reader.ReadInt32(),
            spinX = reader.ReadInt32(),
            spinY = reader.ReadInt32(),
            spinZ = reader.ReadInt32(),
            baseType = reader.ReadInt32(),
            modifier = reader.ReadInt32()
        };
    }

    private void WritePreviewCacheEntry(BinaryWriter writer, PreviewCacheEntry entry)
    {
        writer.Write(entry.v0);
        writer.Write(entry.theta);
        writer.Write(entry.landingX);
        writer.Write(entry.arcPointCount);

        for (int i = 0; i < entry.arcPointCount; i++)
        {
            Vector3 point = entry.arcPoints[i];
            writer.Write(point.x);
            writer.Write(point.y);
            writer.Write(point.z);
        }
    }

    private PreviewCacheEntry ReadPreviewCacheEntry(BinaryReader reader)
    {
        float v0 = reader.ReadSingle();
        float theta = reader.ReadSingle();
        float landingX = reader.ReadSingle();
        int arcPointCount = reader.ReadInt32();

        int maxPointCount = previewArc != null ? previewArc.MaxPointCount : Mathf.Max(2, arcPointCount);
        Vector3[] arcPoints = new Vector3[maxPointCount];
        int safeCount = Mathf.Clamp(arcPointCount, 0, maxPointCount);

        for (int i = 0; i < arcPointCount; i++)
        {
            Vector3 point = new Vector3(
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle()
            );

            if (i < safeCount)
                arcPoints[i] = point;
        }

        if (safeCount <= 1 || !float.IsFinite(v0) || !float.IsFinite(theta))
            return null;

        return new PreviewCacheEntry
        {
            v0 = v0,
            theta = theta,
            landingX = landingX,
            arcPoints = arcPoints,
            arcPointCount = safeCount
        };
    }

    private string BuildLiveShotCacheSignature()
    {
        DragBallistics drag = solverComponent != null && solverComponent.traj != null ? solverComponent.traj.phys : null;
        MagnusBallistics magnus = solverComponent != null && solverComponent.traj != null ? solverComponent.traj.magnus : null;

        string dragSignature = drag == null
            ? "drag=null"
            : $"drag={F(drag.gravity)},{F(drag.airDensity)},{F(drag.Cd)},{F(drag.radius)},{F(drag.mass)},{F(drag.k)}";

        string magnusSignature = magnus == null
            ? "magnus=null"
            : $"magnus={F(magnus.airDensity)},{F(magnus.ballRadius)},{F(magnus.mass)},{F(magnus.magnusCoefficient)},{F(magnus.spinDecayRate)},{F(magnus.baseCd)},{F(magnus.spinDragCoefficient)}";

        return
            $"fileVersion={LiveShotCacheFileVersion}|" +
            $"reticleStep={F(reticleXCacheStep)},{F(reticleZCacheStep)}|" +
            $"contactStep={F(contactXCacheStep)},{F(contactYCacheStep)}|" +
            $"spinStep={F(spinCacheStep)}|" +
            "fixedV0Step=0.25|angleStep=0.25|clearanceStep=0.05|" +
            $"net={F(netX)},{F(netHeight)},{F(solverComponent.netMargin)}|" +
            $"solver={F(solverComponent.minSpeed)},{F(solverComponent.maxSpeed)},{F(solverComponent.minAngleDeg)},{F(solverComponent.maxAngleDeg)}|" +
            $"neutralCurrentSpin={neutralShotUsesCurrentSpin}|" +
            $"{dragSignature}|{magnusSignature}";
    }

    private bool TryLoadLiveShotCacheFile()
    {
        liveShotCacheLoadedEntries = 0;

        if (!CanUseFileBackedLiveShotCache())
            return false;

        string path = GetLiveShotCacheFilePath();
        if (!File.Exists(path))
        {
            if (logLiveShotCacheFile)
                Debug.Log($"[Live Shot Cache File] No cache file found at {path}");

            return false;
        }

        try
        {
            using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                int magic = reader.ReadInt32();
                int version = reader.ReadInt32();
                string savedSignature = reader.ReadString();
                string currentSignature = BuildLiveShotCacheSignature();

                if (magic != LiveShotCacheFileMagic || version != LiveShotCacheFileVersion)
                {
                    if (logLiveShotCacheFile)
                        Debug.LogWarning($"[Live Shot Cache File] Ignored cache with unsupported header at {path}");

                    return false;
                }

                if (!string.Equals(savedSignature, currentSignature, StringComparison.Ordinal))
                {
                    if (logLiveShotCacheFile)
                        Debug.LogWarning("[Live Shot Cache File] Ignored cache because solver settings changed. It will rebuild with current settings.");

                    return false;
                }

                ClearLiveShotCaches();
                int maxEntries = GetMaxLiveShotCacheEntries();

                int fullCount = Mathf.Max(0, reader.ReadInt32());
                for (int i = 0; i < fullCount; i++)
                {
                    FullShotCacheKey key = ReadFullShotCacheKey(reader);
                    bool valid = TryReadShotSolution(reader, out var result);
                    if (valid && fullShotCache.Count < maxEntries)
                        AddFullShotCacheResult(key, result, false);
                }

                int fixedAngleCount = Mathf.Max(0, reader.ReadInt32());
                for (int i = 0; i < fixedAngleCount; i++)
                {
                    FixedAngleCacheKey key = ReadFixedAngleCacheKey(reader);
                    bool valid = TryReadShotSolution(reader, out var result);
                    if (valid && fixedAngleCache.Count < maxEntries)
                        AddFixedAngleCacheResult(key, result, false);
                }

                int fixedSpeedCount = Mathf.Max(0, reader.ReadInt32());
                for (int i = 0; i < fixedSpeedCount; i++)
                {
                    FixedSpeedCacheKey key = ReadFixedSpeedCacheKey(reader);
                    bool valid = TryReadShotSolution(reader, out var result);
                    if (valid && fixedSpeedCache.Count < maxEntries)
                        AddFixedSpeedCacheResult(key, result, false);
                }
            }

            liveShotCacheLoadedEntries = fullShotCache.Count + fixedAngleCache.Count + fixedSpeedCache.Count;
            liveShotCacheDirty = false;

            if (logLiveShotCacheFile)
                Debug.Log($"[Live Shot Cache File] Loaded {liveShotCacheLoadedEntries} entries from {path}");

            return liveShotCacheLoadedEntries > 0;
        }
        catch (Exception ex)
        {
            if (logLiveShotCacheFile)
                Debug.LogWarning($"[Live Shot Cache File] Failed to load cache: {ex.Message}");

            return false;
        }
    }

    private bool TrySaveLiveShotCacheFile(bool force = false)
    {
        liveShotCacheSavedEntries = 0;

        if (!CanUseFileBackedLiveShotCache() || (!force && !liveShotCacheDirty))
            return false;

        int totalEntries = fullShotCache.Count + fixedAngleCache.Count + fixedSpeedCache.Count;
        if (totalEntries == 0)
            return false;

        string path = GetLiveShotCacheFilePath();

        try
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using (FileStream stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(LiveShotCacheFileMagic);
                writer.Write(LiveShotCacheFileVersion);
                writer.Write(BuildLiveShotCacheSignature());

                writer.Write(fullShotCache.Count);
                foreach (KeyValuePair<FullShotCacheKey, (float v0, float theta)> pair in fullShotCache)
                {
                    WriteFullShotCacheKey(writer, pair.Key);
                    WriteShotSolution(writer, pair.Value);
                }

                writer.Write(fixedAngleCache.Count);
                foreach (KeyValuePair<FixedAngleCacheKey, (float v0, float theta)> pair in fixedAngleCache)
                {
                    WriteFixedAngleCacheKey(writer, pair.Key);
                    WriteShotSolution(writer, pair.Value);
                }

                writer.Write(fixedSpeedCache.Count);
                foreach (KeyValuePair<FixedSpeedCacheKey, (float v0, float theta)> pair in fixedSpeedCache)
                {
                    WriteFixedSpeedCacheKey(writer, pair.Key);
                    WriteShotSolution(writer, pair.Value);
                }
            }

            liveShotCacheSavedEntries = totalEntries;
            liveShotCacheDirty = false;

            if (logLiveShotCacheFile)
                Debug.Log($"[Live Shot Cache File] Saved {liveShotCacheSavedEntries} entries to {path}");

            return liveShotCacheSavedEntries > 0;
        }
        catch (Exception ex)
        {
            if (logLiveShotCacheFile)
                Debug.LogWarning($"[Live Shot Cache File] Failed to save cache: {ex.Message}");

            return false;
        }
    }

    private void ClearLiveShotCaches()
    {
        fullShotCache.Clear();
        fullShotCacheOrder.Clear();
        fixedAngleCache.Clear();
        fixedAngleCacheOrder.Clear();
        fixedSpeedCache.Clear();
        fixedSpeedCacheOrder.Clear();
    }

    private void WriteFullShotCacheKey(BinaryWriter writer, FullShotCacheKey key)
    {
        WritePreviewCacheKey(writer, key.previewKey);
        writer.Write(key.clearance);
    }

    private FullShotCacheKey ReadFullShotCacheKey(BinaryReader reader)
    {
        return new FullShotCacheKey
        {
            previewKey = ReadPreviewCacheKey(reader),
            clearance = reader.ReadInt32()
        };
    }

    private void WriteFixedSpeedCacheKey(BinaryWriter writer, FixedSpeedCacheKey key)
    {
        WritePreviewCacheKey(writer, key.previewKey);
        writer.Write(key.fixedV0);
        writer.Write(key.clearance);
    }

    private FixedSpeedCacheKey ReadFixedSpeedCacheKey(BinaryReader reader)
    {
        return new FixedSpeedCacheKey
        {
            previewKey = ReadPreviewCacheKey(reader),
            fixedV0 = reader.ReadInt32(),
            clearance = reader.ReadInt32()
        };
    }

    private void WriteFixedAngleCacheKey(BinaryWriter writer, FixedAngleCacheKey key)
    {
        WritePreviewCacheKey(writer, key.previewKey);
        writer.Write(key.preferredThetaDeg);
        writer.Write(key.minThetaDeg);
        writer.Write(key.maxThetaDeg);
        writer.Write(key.clearance);
        writer.Write(key.maxClearance);
    }

    private FixedAngleCacheKey ReadFixedAngleCacheKey(BinaryReader reader)
    {
        return new FixedAngleCacheKey
        {
            previewKey = ReadPreviewCacheKey(reader),
            preferredThetaDeg = reader.ReadInt32(),
            minThetaDeg = reader.ReadInt32(),
            maxThetaDeg = reader.ReadInt32(),
            clearance = reader.ReadInt32(),
            maxClearance = reader.ReadInt32()
        };
    }

    private void WriteShotSolution(BinaryWriter writer, (float v0, float theta) result)
    {
        writer.Write(result.v0);
        writer.Write(result.theta);
    }

    private bool TryReadShotSolution(BinaryReader reader, out (float v0, float theta) result)
    {
        result = (reader.ReadSingle(), reader.ReadSingle());
        return IsValidShotSolution(result);
    }

    private bool IsValidShotSolution((float v0, float theta) result)
    {
        return float.IsFinite(result.v0) && float.IsFinite(result.theta);
    }

    private (float v0, float theta) CacheFullShotResult(FullShotCacheKey key, float v0, float theta)
    {
        (float v0, float theta) result = (v0, theta);
        AddFullShotCacheResult(key, result, true);
        return result;
    }

    private void AddFullShotCacheResult(FullShotCacheKey key, (float v0, float theta) result, bool markDirty)
    {
        if (!IsValidShotSolution(result))
            return;

        if (!fullShotCache.ContainsKey(key))
            fullShotCacheOrder.Enqueue(key);

        fullShotCache[key] = result;
        TrimFullShotCache();

        if (markDirty)
            liveShotCacheDirty = true;
    }

    private (float v0, float theta) CacheFixedSpeedResult(FixedSpeedCacheKey key, float v0, float theta)
    {
        (float v0, float theta) result = (v0, theta);
        AddFixedSpeedCacheResult(key, result, true);
        return result;
    }

    private void AddFixedSpeedCacheResult(FixedSpeedCacheKey key, (float v0, float theta) result, bool markDirty)
    {
        if (!IsValidShotSolution(result))
            return;

        if (!fixedSpeedCache.ContainsKey(key))
            fixedSpeedCacheOrder.Enqueue(key);

        fixedSpeedCache[key] = result;
        TrimFixedSpeedCache();

        if (markDirty)
            liveShotCacheDirty = true;
    }

    private void AddFixedAngleCacheResult(FixedAngleCacheKey key, (float v0, float theta) result, bool markDirty)
    {
        if (!IsValidShotSolution(result))
            return;

        if (!fixedAngleCache.ContainsKey(key))
            fixedAngleCacheOrder.Enqueue(key);

        fixedAngleCache[key] = result;
        TrimFixedAngleCache();

        if (markDirty)
            liveShotCacheDirty = true;
    }

    private void TrimFullShotCache()
    {
        int maxEntries = GetMaxLiveShotCacheEntries();
        while (fullShotCache.Count > maxEntries && fullShotCacheOrder.Count > 0)
            fullShotCache.Remove(fullShotCacheOrder.Dequeue());
    }

    private void TrimFixedSpeedCache()
    {
        int maxEntries = GetMaxLiveShotCacheEntries();
        while (fixedSpeedCache.Count > maxEntries && fixedSpeedCacheOrder.Count > 0)
            fixedSpeedCache.Remove(fixedSpeedCacheOrder.Dequeue());
    }

    private void TrimFixedAngleCache()
    {
        int maxEntries = GetMaxLiveShotCacheEntries();
        while (fixedAngleCache.Count > maxEntries && fixedAngleCacheOrder.Count > 0)
            fixedAngleCache.Remove(fixedAngleCacheOrder.Dequeue());
    }

    [ContextMenu("Save Live Shot Cache File Now")]
    private void SaveLiveShotCacheFileNow()
    {
        TrySaveLiveShotCacheFile(true);
    }

    [ContextMenu("Load Live Shot Cache File Now")]
    private void LoadLiveShotCacheFileNow()
    {
        TryLoadLiveShotCacheFile();
    }

    [ContextMenu("Delete Live Shot Cache File")]
    private void DeleteLiveShotCacheFile()
    {
        string path = GetLiveShotCacheFilePath();

        if (File.Exists(path))
            File.Delete(path);

        ClearLiveShotCaches();
        liveShotCacheDirty = false;
        liveShotCacheLoadedEntries = 0;
        liveShotCacheSavedEntries = 0;

        if (logLiveShotCacheFile)
            Debug.Log($"[Live Shot Cache File] Deleted cache file at {path}");
    }

    [ContextMenu("Save Preview Cache File Now")]
    private void SavePreviewCacheFileNow()
    {
        TrySavePreviewCacheFile();
    }

    [ContextMenu("Load Preview Cache File Now")]
    private void LoadPreviewCacheFileNow()
    {
        TryLoadPreviewCacheFile();
    }

    [ContextMenu("Delete Preview Cache File")]
    private void DeletePreviewCacheFile()
    {
        string path = GetPreviewCacheFilePath();

        if (File.Exists(path))
            File.Delete(path);

        fileCacheLoadedEntries = 0;
        fileCacheSavedEntries = 0;

        if (logPreviewCacheFile)
            Debug.Log($"[Aiming Cache File] Deleted cache file at {path}");
    }

    private void AddPreviewEntry(PreviewCacheKey key, PreviewCacheEntry entry)
    {
        if (!previewCache.ContainsKey(key))
            previewCacheOrder.Enqueue(key);

        previewCache[key] = entry;

        int maxEntries = GetMaxPreviewCacheEntries();
        while (previewCache.Count > maxEntries && previewCacheOrder.Count > 0)
        {
            PreviewCacheKey oldest = previewCacheOrder.Dequeue();
            previewCache.Remove(oldest);
        }
    }

    private void ApplyPreviewEntry(PreviewCacheEntry entry, string source)
    {
        lastV0 = entry.v0;
        lastTheta = entry.theta;
        previewArc.DrawCachedArc(entry.arcPoints, entry.arcPointCount);

        if (logCacheStats)
            Debug.Log($"[Aiming Cache] {source}: v0={entry.v0:F2}, theta={entry.theta:F3}, points={entry.arcPointCount}, cache={previewCache.Count}");

        debugOverlay =
            "Aiming Debug:\n" +
            $"Source: {source}\n" +
            $"Solver v0: {entry.v0:F2}\n" +
            $"Solver theta: {entry.theta:F3} rad\n" +
            $"Landing X: {(float.IsFinite(entry.landingX) ? entry.landingX.ToString("F2") : "cached off")}\n" +
            $"Cache entries: {previewCache.Count}\n" +
            (prewarmInProgress ? $"Prewarm: {prewarmBuiltCount} built, {prewarmSkippedCount} skipped\n" : "");
    }

    private void RefreshEntryArc(
        PreviewCacheEntry entry,
        Vector3 contactWorld,
        Vector2 startPos2D,
        Vector3 previewSpin)
    {
        if (!redrawCachedArcWithLiveReticle || entry == null || previewArc == null)
            return;

        int maxPointCount = previewArc.MaxPointCount;
        if (entry.arcPoints == null || entry.arcPoints.Length != maxPointCount)
            entry.arcPoints = new Vector3[maxPointCount];

        entry.arcPointCount = previewArc.BuildArcPoints(
            contactWorld,
            startPos2D,
            reticle.position,
            entry.v0,
            entry.theta,
            previewSpin,
            entry.arcPoints
        );
    }

    private bool TryBuildPreviewCacheEntry(
        Vector3 contactWorld,
        Vector2 startPos2D,
        Vector3 targetWorld,
        Vector3 previewSpin,
        PreviewCacheKey key,
        out PreviewCacheEntry entry)
    {
        entry = null;

        if (!CanBuildPreviewCacheEntries())
            return false;

        GetCanonicalSolverCoordinates(
            contactWorld,
            targetWorld,
            out startPos2D,
            out float xTarget,
            out float directionSign
        );

        if (!IsTargetValid(startPos2D, xTarget))
            return false;

        var solver = solverComponent.solver;

        var (v0, theta) = solver.SolveForReticle(
            startPos2D,
            xTarget,
            netX,
            netHeight,
            solverComponent.netMargin,
            previewSpin
        );

        if (!float.IsFinite(v0) || !float.IsFinite(theta))
            return false;

        float canonicalLandingX = computeDebugLandingX
            ? solver.PredictLandingX(startPos2D, v0, theta, previewSpin)
            : float.NaN;
        float landingX = float.IsFinite(canonicalLandingX)
            ? FromCanonicalSolverX(canonicalLandingX, directionSign)
            : float.NaN;

        Vector3[] arcPoints = new Vector3[previewArc.MaxPointCount];
        int arcPointCount = previewArc.BuildArcPoints(
            contactWorld,
            startPos2D,
            targetWorld,
            v0,
            theta,
            previewSpin,
            arcPoints
        );

        if (arcPointCount <= 1)
            return false;

        entry = new PreviewCacheEntry
        {
            v0 = v0,
            theta = theta,
            landingX = landingX,
            arcPoints = arcPoints,
            arcPointCount = arcPointCount
        };

        AddPreviewEntry(key, entry);
        CacheFullShotResult(BuildFullShotCacheKey(key, solverComponent.netMargin), v0, theta);
        return true;
    }

    // ------------------------------------------------------------
    // 6. Core: ask solver for a shot and drive the preview arc
    // ------------------------------------------------------------
    void UpdateAimingPreview(
        Vector3 contactWorld,
        Vector2 startPos2D,
        Vector3 previewSpin,
        PreviewCacheKey key)
    {
        if (solverComponent.solver == null)
        {
            Debug.LogWarning("[Aiming] solverComponent.solver is NULL — ShotSolverComponent.Awake() has not run yet.");
            ClearPreviewArcState();
            return;
        }

        if (!TryBuildPreviewCacheEntry(contactWorld, startPos2D, reticle.position, previewSpin, key, out PreviewCacheEntry entry))
        {
            hasLastPreviewKey = false;
            debugOverlay =
                "Aiming Debug:\n" +
                $"Source: solve failed\n" +
                $"Reticle X: {reticle.position.x:F2}\n" +
                $"Start pos: {startPos2D.x:F2}, {startPos2D.y:F2}\n" +
                "FALLBACK ACTIVE";
            ClearPreviewArcState();
            return;
        }

        ApplyPreviewEntry(entry, "solve");
        lastPreviewKey = key;
        hasLastPreviewKey = true;
        nextCachedArcRedrawTime = Time.time + GetCachedArcRedrawInterval();

    }

    // ------------------------------------------------------------
    // 7. Validation helpers
    // ------------------------------------------------------------
    bool IsSetupValid()
    {
        return previewArc != null &&
               reticle != null &&
               contactPoint != null &&
               solverComponent != null &&
               previewArc.enabledInGame;
    }

    bool IsShotSolverSetupValid()
    {
        return reticle != null &&
               solverComponent != null &&
               solverComponent.solver != null &&
               solverComponent.traj != null;
    }

    // The 2D trajectory solvers integrate forward along increasing X. World-space
    // shots can travel in either direction, so reflect negative-X shots around the
    // net plane before validating or solving. The net remains at netX, while the
    // contact is always behind the target in this canonical solver space.
    private void GetCanonicalSolverCoordinates(
        Vector3 contactWorld,
        Vector3 targetWorld,
        out Vector2 startPos2D,
        out float xTarget,
        out float directionSign)
    {
        directionSign = targetWorld.x >= contactWorld.x ? 1f : -1f;
        startPos2D = new Vector2(ToCanonicalSolverX(contactWorld.x, directionSign), contactWorld.y);
        xTarget = ToCanonicalSolverX(targetWorld.x, directionSign);
    }

    private float ToCanonicalSolverX(float worldX, float directionSign)
    {
        return netX + (worldX - netX) * directionSign;
    }

    private float FromCanonicalSolverX(float solverX, float directionSign)
    {
        // Reflection around netX is its own inverse.
        return netX + (solverX - netX) * directionSign;
    }

    bool IsTargetValid(Vector2 startPos, float xTarget)
    {
        if (xTarget <= startPos.x)
            return false;

        float dx = Mathf.Abs(xTarget - startPos.x);
        return dx >= 0.3f;
    }

    // ------------------------------------------------------------
    // 8. Solver logging (only when values change)
    // ------------------------------------------------------------
    void LogIfChanged(float v0, float theta)
    {
        if (!debugLogs) return;
        if (_solverLogCount >= _solverLogMax) return;

        bool changed =
            float.IsNaN(_lastSolverV0) ||
            Mathf.Abs(v0 - _lastSolverV0) > _solverLogEpsilon ||
            Mathf.Abs(theta - _lastSolverTheta) > (_solverLogEpsilon * 0.1f);

        if (!changed) return;

        Debug.Log($"Solver result: v0={v0:F2}, theta={theta:F7}  (log {(_solverLogCount + 1)}/{_solverLogMax})");

        _lastSolverV0 = v0;
        _lastSolverTheta = theta;
        _solverLogCount++;
    }

    // ------------------------------------------------------------
    // 9. Public API — queried by ShotComputationSolver / HitController
    // ------------------------------------------------------------
    public (float v0, float theta) GetShotParameters()
    {
        if (float.IsFinite(lastV0) && float.IsFinite(lastTheta))
        {
            MarkLiveShotSolve("last-result-cache", true);
            return (lastV0, lastTheta);
        }

        if (!solveNeutralShotSilentlyWhenPreviewHidden)
        {
            MarkLiveShotSolve("last-result-missing", false);
            return (lastV0, lastTheta);
        }

        return SolveNeutralShotSilently(-1f);
    }

    public (float v0, float theta) GetFreshShotParameters(float desiredNetClearance = -1f)
    {
        return SolveNeutralShotSilently(desiredNetClearance);
    }

    public (float v0, float theta) GetFreshShotParametersAtTarget(
        Vector3 contactWorld,
        Vector3 targetWorld,
        float desiredNetClearance = -1f)
    {
        return SolveNeutralShotSilently(contactWorld, targetWorld, desiredNetClearance);
    }

    private (float v0, float theta) SolveNeutralShotSilently(float desiredNetClearance)
    {
        Vector3 targetWorld = reticle != null ? reticle.position : Vector3.zero;
        return SolveNeutralShotSilently(GetCurrentContactPos(), targetWorld, desiredNetClearance);
    }

    private (float v0, float theta) SolveNeutralShotSilently(
        Vector3 contactWorld,
        Vector3 targetWorld,
        float desiredNetClearance)
    {
        if (!IsShotSolverSetupValid())
        {
            MarkLiveShotSolve("full-invalid-setup", false);
            return (float.NaN, float.NaN);
        }

        GetCanonicalSolverCoordinates(
            contactWorld,
            targetWorld,
            out Vector2 startPos2D,
            out float xTarget,
            out _
        );

        if (!IsTargetValid(startPos2D, xTarget))
        {
            MarkLiveShotSolve("full-invalid-target", false);
            return (float.NaN, float.NaN);
        }

        Vector3 spin = neutralShotUsesCurrentSpin ? ComputePreviewSpin() : Vector3.zero;
        float requiredClearance = ResolveNetClearance(desiredNetClearance);
        PreviewCacheKey previewKey = BuildPreviewCacheKey(contactWorld, targetWorld, spin);
        FullShotCacheKey fullKey = BuildFullShotCacheKey(previewKey, requiredClearance);

        if (fullShotCache.TryGetValue(fullKey, out var cached))
        {
            lastV0 = cached.v0;
            lastTheta = cached.theta;
            MarkLiveShotSolve("full-cache", true);
            return cached;
        }

        var result = solverComponent.solver.SolveForReticle(
            startPos2D,
            xTarget,
            netX,
            netHeight,
            requiredClearance,
            spin
        );

        if (float.IsFinite(result.v0) && float.IsFinite(result.theta))
        {
            lastV0 = result.v0;
            lastTheta = result.theta;
            CacheFullShotResult(fullKey, result.v0, result.theta);
            MarkLiveShotSolve("full-live", false);

            if (logSilentNeutralSolve)
                Debug.Log($"[Aiming Silent Solve] v0={result.v0:F2}, theta={result.theta * Mathf.Rad2Deg:F2}deg");
        }
        else
        {
            MarkLiveShotSolve("full-live-failed", false);
        }

        return result;
    }
    public (float v0, float theta) GetShotParametersForAngle(float fixedTheta, float desiredNetClearance = -1f)
    {
        return GetShotParametersForAngleRange(
            fixedTheta,
            fixedTheta,
            fixedTheta,
            desiredNetClearance,
            -1f
        );
    }

    public (float v0, float theta) GetShotParametersForAngleRange(
        float preferredTheta,
        float minAllowedTheta,
        float maxAllowedTheta,
        float desiredNetClearance = -1f,
        float maxNetClearance = -1f,
        bool bypassCache = false,
        int maxExtendedAngleSteps = -1)
    {
        if (!IsShotSolverSetupValid() || !float.IsFinite(preferredTheta))
        {
            MarkLiveShotSolve("fixed-angle-invalid-setup", false);
            return (float.NaN, float.NaN);
        }

        Vector3 contactWorld = GetCurrentContactPos();
        Vector3 previewSpin = ComputePreviewSpin();
        BaseShotType baseType = hitCtrl != null ? hitCtrl.currentBaseShotType : BaseShotType.Flat;
        ShotModifier modifier = hitCtrl != null ? hitCtrl.currentModifier : ShotModifier.Normal;

        return GetShotParametersForAngleRangeCached(
            contactWorld,
            reticle.position,
            previewSpin,
            baseType,
            modifier,
            preferredTheta,
            minAllowedTheta,
            maxAllowedTheta,
            desiredNetClearance,
            maxNetClearance,
            bypassCache,
            maxExtendedAngleSteps
        );
    }

    // Solves against an explicit landing point without moving the visible reticle.
    // Used when a requested short target is physically impossible and the shot
    // must be extended deeper along the same lateral trajectory.
    public (float v0, float theta) GetShotParametersForAngleRangeAtTarget(
        Vector3 targetWorld,
        float preferredTheta,
        float minAllowedTheta,
        float maxAllowedTheta,
        float desiredNetClearance = -1f,
        float maxNetClearance = -1f,
        bool bypassCache = false,
        int maxExtendedAngleSteps = -1)
    {
        Vector3 contactWorld = GetCurrentContactPos();
        BaseShotType baseType = hitCtrl != null ? hitCtrl.currentBaseShotType : BaseShotType.Flat;
        ShotModifier modifier = hitCtrl != null ? hitCtrl.currentModifier : ShotModifier.Normal;

        return GetShotParametersForAngleRangeAtTarget(
            contactWorld,
            targetWorld,
            baseType,
            modifier,
            preferredTheta,
            minAllowedTheta,
            maxAllowedTheta,
            desiredNetClearance,
            maxNetClearance,
            bypassCache,
            maxExtendedAngleSteps
        );
    }

    // Runtime hits pass the accepted ball-contact position explicitly. This avoids
    // using this shared controller's player contact transform for an AI shot.
    public (float v0, float theta) GetShotParametersForAngleRangeAtTarget(
        Vector3 contactWorld,
        Vector3 targetWorld,
        BaseShotType baseType,
        ShotModifier modifier,
        float preferredTheta,
        float minAllowedTheta,
        float maxAllowedTheta,
        float desiredNetClearance = -1f,
        float maxNetClearance = -1f,
        bool bypassCache = false,
        int maxExtendedAngleSteps = -1)
    {
        if (!IsShotSolverSetupValid() || !float.IsFinite(preferredTheta))
        {
            LastFixedAngleRejectReason = FixedAngleRejectReason.Invalid;
            MarkLiveShotSolve("fixed-angle-invalid-setup", false);
            return (float.NaN, float.NaN);
        }

        Vector3 previewSpin = ComputePreviewSpin();

        return GetShotParametersForAngleRangeCached(
            contactWorld,
            targetWorld,
            previewSpin,
            baseType,
            modifier,
            preferredTheta,
            minAllowedTheta,
            maxAllowedTheta,
            desiredNetClearance,
            maxNetClearance,
            bypassCache,
            maxExtendedAngleSteps
        );
    }

    private (float v0, float theta) GetShotParametersForAngleRangeCached(
        Vector3 contactWorld,
        Vector3 targetWorld,
        Vector3 previewSpin,
        BaseShotType baseType,
        ShotModifier modifier,
        float preferredTheta,
        float minAllowedTheta,
        float maxAllowedTheta,
        float desiredNetClearance = -1f,
        float maxNetClearance = -1f,
        bool bypassCache = false,
        int maxExtendedAngleSteps = -1)
    {
        if (!IsShotSolverSetupValid() || !float.IsFinite(preferredTheta))
        {
            LastFixedAngleRejectReason = FixedAngleRejectReason.Invalid;
            MarkLiveShotSolve("fixed-angle-invalid-setup", false);
            return (float.NaN, float.NaN);
        }

        GetCanonicalSolverCoordinates(
            contactWorld,
            targetWorld,
            out Vector2 startPos2D,
            out float xTarget,
            out _
        );

        if (!IsTargetValid(startPos2D, xTarget))
        {
            LastFixedAngleRejectReason = FixedAngleRejectReason.Invalid;
            MarkLiveShotSolve("fixed-angle-invalid-target", false);
            return (float.NaN, float.NaN);
        }

        float requiredClearance = ResolveNetClearance(desiredNetClearance);
        float rawMin = float.IsFinite(minAllowedTheta) ? minAllowedTheta : preferredTheta;
        float rawMax = float.IsFinite(maxAllowedTheta) ? maxAllowedTheta : preferredTheta;
        float minTheta = Mathf.Min(rawMin, rawMax);
        float maxTheta = Mathf.Max(rawMin, rawMax);

        if (!float.IsFinite(minTheta) || !float.IsFinite(maxTheta) || minTheta > maxTheta)
        {
            LastFixedAngleRejectReason = FixedAngleRejectReason.Invalid;
            MarkLiveShotSolve("fixed-angle-invalid-range", false);
            return (float.NaN, float.NaN);
        }

        float theta = Mathf.Clamp(preferredTheta, minTheta, maxTheta);

        PreviewCacheKey previewKey = BuildPreviewCacheKey(contactWorld, targetWorld, previewSpin, baseType, modifier);
        FixedAngleCacheKey fixedKey = BuildFixedAngleCacheKey(
            previewKey,
            theta,
            minTheta,
            maxTheta,
            requiredClearance,
            maxNetClearance
        );

        if (!bypassCache && fixedAngleCache.TryGetValue(fixedKey, out var cached))
        {
            LastFixedAngleRejectReason = FixedAngleRejectReason.None;
            MarkLiveShotSolve("fixed-angle-cache", true);
            return cached;
        }

        int searchLimit = maxExtendedAngleSteps >= 0
            ? maxExtendedAngleSteps
            : Mathf.Max(0, liveFixedAngleMaxExtendedSteps);

        if (TrySolveFixedAngleCandidate(startPos2D, xTarget, previewSpin, theta, requiredClearance, maxNetClearance, out float v0, out FixedAngleRejectReason reason))
        {
            LastFixedAngleRejectReason = FixedAngleRejectReason.None;
            MarkLiveShotSolve("fixed-angle-live", false);
            return CacheFixedAngleResult(fixedKey, v0, theta);
        }

        LastFixedAngleRejectReason = reason;

        if (searchLimit <= 0)
        {
            MarkLiveShotSolve("fixed-angle-live-failed", false);
            return (float.NaN, float.NaN);
        }

        const float angleStep = 0.5f * Mathf.Deg2Rad;

        if (reason == FixedAngleRejectReason.NetTooLow || reason == FixedAngleRejectReason.SpeedTooLow)
        {
            if (TrySearchFixedAngleDirection(startPos2D, xTarget, previewSpin, theta, maxTheta, angleStep, requiredClearance, maxNetClearance, searchLimit, out var raised))
            {
                LastFixedAngleRejectReason = FixedAngleRejectReason.None;
                MarkLiveShotSolve("fixed-angle-live-raised", false);
                return CacheFixedAngleResult(fixedKey, raised.v0, raised.theta);
            }
        }
        else if (reason == FixedAngleRejectReason.SpeedTooHigh || reason == FixedAngleRejectReason.NetTooHigh)
        {
            if (TrySearchFixedAngleDirection(startPos2D, xTarget, previewSpin, theta, minTheta, -angleStep, requiredClearance, maxNetClearance, searchLimit, out var lowered))
            {
                LastFixedAngleRejectReason = FixedAngleRejectReason.None;
                MarkLiveShotSolve("fixed-angle-live-lowered", false);
                return CacheFixedAngleResult(fixedKey, lowered.v0, lowered.theta);
            }
        }

        if (TrySearchClosestFixedAngle(startPos2D, xTarget, previewSpin, theta, minTheta, maxTheta, angleStep, requiredClearance, maxNetClearance, searchLimit, out var closest))
        {
            LastFixedAngleRejectReason = FixedAngleRejectReason.None;
            MarkLiveShotSolve("fixed-angle-live-closest", false);
            return CacheFixedAngleResult(fixedKey, closest.v0, closest.theta);
        }

        MarkLiveShotSolve("fixed-angle-live-failed", false);
        return (float.NaN, float.NaN);
    }    private bool TrySolveFixedAngleCandidate(
        Vector2 startPos2D,
        float xTarget,
        Vector3 previewSpin,
        float theta,
        float requiredClearance,
        float maxNetClearance,
        out float v0,
        out FixedAngleRejectReason reason)
    {
        v0 = solverComponent.solver.SolveSpeedForFixedAngle(startPos2D, xTarget, theta, previewSpin, liveFixedAngleSpeedIterations);
        reason = FixedAngleRejectReason.Invalid;

        if (!float.IsFinite(v0))
            return false;

        float yAtNet = solverComponent.traj.GetHeightAtX(startPos2D, v0, theta, netX, previewSpin);
        if (!float.IsFinite(yAtNet))
            return false;

        if (yAtNet < netHeight)
        {
            reason = FixedAngleRejectReason.NetClipped;
            return false;
        }

        if (yAtNet < netHeight + requiredClearance)
        {
            reason = FixedAngleRejectReason.NetTooLow;
            return false;
        }

        if (float.IsFinite(maxNetClearance) && maxNetClearance > requiredClearance &&
            yAtNet > netHeight + maxNetClearance)
        {
            reason = FixedAngleRejectReason.NetTooHigh;
            return false;
        }

        if (v0 > solverComponent.maxSpeed)
        {
            reason = FixedAngleRejectReason.SpeedTooHigh;
            return false;
        }

        if (v0 < solverComponent.minSpeed)
        {
            reason = FixedAngleRejectReason.SpeedTooLow;
            return false;
        }

        reason = FixedAngleRejectReason.None;
        return true;
    }

    private bool TrySearchFixedAngleDirection(
        Vector2 startPos2D,
        float xTarget,
        Vector3 previewSpin,
        float startTheta,
        float endTheta,
        float step,
        float requiredClearance,
        float maxNetClearance,
        int maxSearchSteps,
        out (float v0, float theta) result)
    {
        result = (float.NaN, float.NaN);

        if (Mathf.Approximately(step, 0f))
            return false;

        int maxSteps = Mathf.CeilToInt(Mathf.Abs((endTheta - startTheta) / step));
        if (maxSearchSteps >= 0)
            maxSteps = Mathf.Min(maxSteps, maxSearchSteps);
        for (int i = 1; i <= maxSteps; i++)
        {
            float theta = startTheta + step * i;
            if ((step > 0f && theta > endTheta) || (step < 0f && theta < endTheta))
                theta = endTheta;

            if (TrySolveFixedAngleCandidate(startPos2D, xTarget, previewSpin, theta, requiredClearance, maxNetClearance, out float v0, out _))
            {
                result = (v0, theta);
                return true;
            }
        }

        return false;
    }

    private bool TrySearchClosestFixedAngle(
        Vector2 startPos2D,
        float xTarget,
        Vector3 previewSpin,
        float preferredTheta,
        float minTheta,
        float maxTheta,
        float step,
        float requiredClearance,
        float maxNetClearance,
        int maxSearchSteps,
        out (float v0, float theta) result)
    {
        result = (float.NaN, float.NaN);
        int maxSteps = Mathf.CeilToInt((maxTheta - minTheta) / Mathf.Max(step, 0.001f));
        if (maxSearchSteps >= 0)
            maxSteps = Mathf.Min(maxSteps, maxSearchSteps);

        for (int i = 1; i <= maxSteps; i++)
        {
            float downTheta = preferredTheta - step * i;
            if (downTheta >= minTheta &&
                TrySolveFixedAngleCandidate(startPos2D, xTarget, previewSpin, downTheta, requiredClearance, maxNetClearance, out float downV0, out _))
            {
                result = (downV0, downTheta);
                return true;
            }

            float upTheta = preferredTheta + step * i;
            if (upTheta <= maxTheta &&
                TrySolveFixedAngleCandidate(startPos2D, xTarget, previewSpin, upTheta, requiredClearance, maxNetClearance, out float upV0, out _))
            {
                result = (upV0, upTheta);
                return true;
            }
        }

        return false;
    }

    private (float v0, float theta) CacheFixedAngleResult(FixedAngleCacheKey fixedKey, float v0, float theta)
    {
        (float v0, float theta) result = (v0, theta);
        AddFixedAngleCacheResult(fixedKey, result, true);
        return result;
    }

    public (float v0, float theta) GetShotParametersForV0(float fixedV0, float desiredNetClearance = -1f)
    {
        if (!IsShotSolverSetupValid() || !float.IsFinite(fixedV0) || fixedV0 <= 0f)
        {
            MarkLiveShotSolve("fixed-speed-invalid-setup", false);
            return (float.NaN, float.NaN);
        }

        Vector3 contactWorld = GetCurrentContactPos();
        GetCanonicalSolverCoordinates(
            contactWorld,
            reticle.position,
            out Vector2 startPos2D,
            out float xTarget,
            out _
        );

        if (!IsTargetValid(startPos2D, xTarget))
        {
            MarkLiveShotSolve("fixed-speed-invalid-target", false);
            return (float.NaN, float.NaN);
        }

        Vector3 previewSpin = ComputePreviewSpin();
        float requiredClearance = ResolveNetClearance(desiredNetClearance);
        PreviewCacheKey previewKey = BuildPreviewCacheKey(contactWorld, reticle.position, previewSpin);
        FixedSpeedCacheKey fixedKey = BuildFixedSpeedCacheKey(previewKey, fixedV0, requiredClearance);

        if (fixedSpeedCache.TryGetValue(fixedKey, out var cached))
        {
            MarkLiveShotSolve("fixed-speed-cache", true);
            return cached;
        }

        float minTheta = solverComponent.minAngleDeg * Mathf.Deg2Rad;
        float maxTheta = solverComponent.maxAngleDeg * Mathf.Deg2Rad;
        const float groundY = 0f;
        const int samples = 80;

        float bestTheta = float.NaN;
        float bestScore = float.PositiveInfinity;
        const float maxSampledTargetHeightError = 0.2f;

        float previousTheta = minTheta;
        float previousHeight = solverComponent.traj.GetHeightAtX(startPos2D, fixedV0, previousTheta, xTarget, previewSpin) - groundY;

        for (int i = 1; i <= samples; i++)
        {
            float t = i / (float)samples;
            float theta = Mathf.Lerp(minTheta, maxTheta, t);
            float height = solverComponent.traj.GetHeightAtX(startPos2D, fixedV0, theta, xTarget, previewSpin) - groundY;

            if (!float.IsFinite(height))
            {
                previousTheta = theta;
                previousHeight = height;
                continue;
            }

            float sampledError = Mathf.Abs(height);
            if (sampledError <= maxSampledTargetHeightError)
                TryCandidate(theta, fixedV0, sampledError, startPos2D, previewSpin, requiredClearance, ref bestTheta, ref bestScore);

            if (float.IsFinite(previousHeight) &&
                Mathf.Sign(previousHeight) != Mathf.Sign(height))
            {
                float refinedTheta = RefineThetaForTargetHeight(
                    startPos2D,
                    fixedV0,
                    previousTheta,
                    theta,
                    xTarget,
                    previewSpin,
                    groundY
                );

                TryCandidate(refinedTheta, fixedV0, 0f, startPos2D, previewSpin, requiredClearance, ref bestTheta, ref bestScore);
            }

            previousTheta = theta;
            previousHeight = height;
        }

        if (!float.IsFinite(bestTheta))
        {
            MarkLiveShotSolve("fixed-speed-live-failed", false);
            return (float.NaN, float.NaN);
        }

        MarkLiveShotSolve("fixed-speed-live", false);
        return CacheFixedSpeedResult(fixedKey, fixedV0, bestTheta);
    }
    private float RefineThetaForTargetHeight(
        Vector2 startPos2D,
        float fixedV0,
        float lowTheta,
        float highTheta,
        float xTarget,
        Vector3 spin,
        float groundY)
    {
        float lowHeight = solverComponent.traj.GetHeightAtX(startPos2D, fixedV0, lowTheta, xTarget, spin) - groundY;

        for (int i = 0; i < 24; i++)
        {
            float midTheta = 0.5f * (lowTheta + highTheta);
            float midHeight = solverComponent.traj.GetHeightAtX(startPos2D, fixedV0, midTheta, xTarget, spin) - groundY;

            if (!float.IsFinite(midHeight))
                break;

            if (Mathf.Sign(lowHeight) == Mathf.Sign(midHeight))
            {
                lowTheta = midTheta;
                lowHeight = midHeight;
            }
            else
            {
                highTheta = midTheta;
            }
        }

        return 0.5f * (lowTheta + highTheta);
    }

    private void TryCandidate(
        float theta,
        float fixedV0,
        float targetHeightError,
        Vector2 startPos2D,
        Vector3 previewSpin,
        float requiredClearance,
        ref float bestTheta,
        ref float bestScore)
    {
        if (!float.IsFinite(theta))
            return;

        float yAtNet = solverComponent.traj.GetHeightAtX(startPos2D, fixedV0, theta, netX, previewSpin);
        if (!float.IsFinite(yAtNet))
            return;

        if (yAtNet < netHeight + requiredClearance)
            return;

        float anglePenalty = theta * 0.02f;
        float score = targetHeightError + anglePenalty;

        if (score < bestScore)
        {
            bestScore = score;
            bestTheta = theta;
        }
    }

    public void ResetSolverLogCount()
    {
        _solverLogCount = 0;
        _lastSolverV0 = float.NaN;
        _lastSolverTheta = float.NaN;
        lastV0 = float.NaN;
        lastTheta = float.NaN;
        previewCache.Clear();
        previewCacheOrder.Clear();
        ClearLiveShotCaches();
        liveShotCacheDirty = false;
        hasLastPreviewKey = false;

        if (prewarmPreviewCacheOnStart && isActiveAndEnabled)
        {
            if (prewarmCoroutine != null)
                StopCoroutine(prewarmCoroutine);

            if (livePrewarmCoroutine != null)
                StopCoroutine(livePrewarmCoroutine);

            prewarmCoroutine = StartCoroutine(PrewarmPreviewCacheCoroutine());
            if (useFileBackedLiveShotCache && prewarmLiveShotCacheOnStart)
                livePrewarmCoroutine = StartCoroutine(PrewarmLiveShotCacheCoroutine());
        }
    }

    // ------------------------------------------------------------
    // 10. Debug overlay
    // ------------------------------------------------------------
    void OnGUI()
    {
        if (!showDebugOverlay) return;

        GUI.color = Color.white;
        GUI.Label(new Rect(20, 20, 600, 300), debugOverlay);
    }
}
