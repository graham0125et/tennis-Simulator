using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

[ExecuteAlways]
[RequireComponent(typeof(BoxCollider))]
public class WaterFogVolumeFollower : MonoBehaviour
{
    [Header("Water Source")]
    public SurfaceTileRenderer waterTiles;
    public bool useAverageHeight = true;
    public float surfaceOffset = 0f;

    [Header("Volume Bounds")]
    public Vector3 boundsMin = new Vector3(-20, -2, -20);
    public Vector3 boundsMax = new Vector3(20, 6, 20);
    public float minVisibleDepth = 0.05f;
    public bool previewInEditMode = true;

    [Header("Runtime")]
    public float currentSurfaceY;
    public float currentDepth;
    public bool volumeActive;
    public bool hasLocalVolumetricFog;
    public Vector3 appliedVolumeSize;

    BoxCollider boxCollider;
    LocalVolumetricFog localFog;

    void OnEnable()
    {
        boxCollider = GetComponent<BoxCollider>();
        boxCollider.isTrigger = true;
        localFog = GetComponent<LocalVolumetricFog>();
        UpdateVolume();
    }

    void LateUpdate()
    {
        UpdateVolume();
    }

    void UpdateVolume()
    {
        if (boxCollider == null)
            boxCollider = GetComponent<BoxCollider>();

        if (localFog == null)
            localFog = GetComponent<LocalVolumetricFog>();

        hasLocalVolumetricFog = localFog != null;

        if (waterTiles == null)
            waterTiles = FindFirstObjectByType<SurfaceTileRenderer>();

        if (waterTiles == null)
        {
            volumeActive = false;
            SetChildRenderers(false);
            SetLocalFogEnabled(false);
            return;
        }

        float targetSurface = useAverageHeight
            ? waterTiles.averageWetTileHeight
            : waterTiles.maxTileSurfaceHeight;

        currentSurfaceY = Mathf.Clamp(targetSurface + surfaceOffset, boundsMin.y, boundsMax.y);
        currentDepth = Mathf.Max(0f, currentSurfaceY - boundsMin.y);
        bool hasEnoughDepth = currentDepth >= minVisibleDepth;
        bool hasWetTiles = waterTiles.wetTileCount > 0;
        volumeActive = hasEnoughDepth && (hasWetTiles || (previewInEditMode && !Application.isPlaying));

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
        appliedVolumeSize = size;

        transform.position = center;
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.one;

        boxCollider.center = Vector3.zero;
        boxCollider.size = size;

        if (localFog != null)
        {
            LocalVolumetricFogArtistParameters parameters = localFog.parameters;
            parameters.size = size;
            parameters.scaleMode = LocalVolumetricFogScaleMode.ScaleInvariant;
            localFog.parameters = parameters;
        }

        SetChildRenderers(volumeActive);
        SetLocalFogEnabled(volumeActive);
    }

    void SetChildRenderers(bool enabledState)
    {
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
            renderer.enabled = enabledState;
    }

    void SetLocalFogEnabled(bool enabledState)
    {
        if (localFog != null)
            localFog.enabled = enabledState;
    }
}
