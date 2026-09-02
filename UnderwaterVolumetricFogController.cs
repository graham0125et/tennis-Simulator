using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

[ExecuteAlways]
public class UnderwaterVolumetricFogController : MonoBehaviour
{
    [Header("Water Source")]
    public SurfaceTileRenderer waterTiles;
    public bool useAverageHeight = true;
    public float surfaceOffset = -0.02f;

    [Header("Volume Bounds")]
    public Vector3 boundsMin = new Vector3(-20, -2, -20);
    public Vector3 boundsMax = new Vector3(20, 6, 20);
    public float minVisibleDepth = 0.05f;

    [Header("Fog Look")]
    public Color albedo = new Color(0.28f, 0.85f, 0.9f, 1f);
    [Min(0.05f)] public float meanFreePath = 6f;
    [Range(-1f, 1f)] public float anisotropy = 0.15f;
    public Vector3 positiveFade = new Vector3(0.15f, 0.25f, 0.15f);
    public Vector3 negativeFade = new Vector3(0.15f, 0.05f, 0.15f);

    [Header("Density Texture")]
    public Texture volumeMask;
    public Vector3 textureTiling = new Vector3(1f, 1f, 1f);
    public Vector3 textureScrollSpeed = new Vector3(0.03f, 0.0f, 0.02f);

    [Header("Runtime")]
    public float currentSurfaceY;
    public float currentDepth;
    public bool fogActive;

    LocalVolumetricFog fog;

    void OnEnable()
    {
        EnsureFog();
        UpdateFog();
    }

    void LateUpdate()
    {
        UpdateFog();
    }

    void EnsureFog()
    {
        fog = GetComponent<LocalVolumetricFog>();
        if (fog == null)
            fog = gameObject.AddComponent<LocalVolumetricFog>();
    }

    void UpdateFog()
    {
        EnsureFog();

        if (waterTiles == null)
            waterTiles = FindFirstObjectByType<SurfaceTileRenderer>();

        if (waterTiles == null || fog == null)
        {
            fogActive = false;
            if (fog != null) fog.enabled = false;
            return;
        }

        currentSurfaceY = useAverageHeight
            ? waterTiles.averageWetTileHeight
            : waterTiles.maxTileSurfaceHeight;

        currentSurfaceY += surfaceOffset;
        currentDepth = Mathf.Max(0f, currentSurfaceY - boundsMin.y);
        fogActive = waterTiles.wetTileCount > 0 && currentDepth >= minVisibleDepth;
        fog.enabled = fogActive;

        if (!fogActive)
            return;

        Vector3 center = new Vector3(
            (boundsMin.x + boundsMax.x) * 0.5f,
            (boundsMin.y + currentSurfaceY) * 0.5f,
            (boundsMin.z + boundsMax.z) * 0.5f
        );

        Vector3 size = new Vector3(
            Mathf.Max(0.001f, boundsMax.x - boundsMin.x),
            Mathf.Max(0.001f, currentDepth),
            Mathf.Max(0.001f, boundsMax.z - boundsMin.z)
        );

        transform.position = center;
        transform.rotation = Quaternion.identity;

        LocalVolumetricFogArtistParameters p = fog.parameters;
        p.albedo = albedo;
        p.meanFreePath = meanFreePath;
        p.anisotropy = anisotropy;
        p.size = size;
        p.scaleMode = LocalVolumetricFogScaleMode.ScaleInvariant;
        p.positiveFade = positiveFade;
        p.negativeFade = negativeFade;
        p.volumeMask = volumeMask;
        p.textureTiling = textureTiling;
        p.textureScrollingSpeed = textureScrollSpeed;
        p.blendingMode = LocalVolumetricFogBlendingMode.Additive;
        p.falloffMode = LocalVolumetricFogFalloffMode.Exponential;
        p.maskMode = volumeMask != null
            ? LocalVolumetricFogMaskMode.Texture
            : LocalVolumetricFogMaskMode.Texture;

        fog.parameters = p;
    }
}
