using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public class CourtDecalSystem : MonoBehaviour
{
    [Header("Decal Materials")]
    public Material baselineScuffMaterial;
    public Material serviceBoxWearMaterial;
    public Material shoeMarksMaterial;
    public Material dustPatchMaterial;

    [Header("Court Measurements")]
    public float courtLength = 23.77f;
    public float doublesWidth = 10.97f;
    public float singlesWidth = 8.23f;
    public float serviceLineDistanceFromNet = 6.40f;

    [Header("Projector Defaults")]
    public float projectorHeight = 0.60f;
    public float projectorDepth = 1.25f;
    public float drawDistance = 95f;
    public bool autoCreateMissingInEditor = true;
    public bool createAtRuntimeIfEmpty = false;

#if UNITY_EDITOR
    private bool editorBuildQueued;
#endif

    private void Awake()
    {
        if (Application.isPlaying && createAtRuntimeIfEmpty && transform.childCount == 0)
            CreateMissingDefaultDecals();
    }

#if UNITY_EDITOR
    private void OnEnable()
    {
        QueueEditorBuild();
    }

    private void OnValidate()
    {
        QueueEditorBuild();
    }

    private void QueueEditorBuild()
    {
        if (Application.isPlaying || !autoCreateMissingInEditor || editorBuildQueued)
            return;

        editorBuildQueued = true;
        EditorApplication.delayCall += DelayedEditorBuild;
    }

    private void DelayedEditorBuild()
    {
        editorBuildQueued = false;
        if (this == null || Application.isPlaying || !autoCreateMissingInEditor)
            return;

        CreateMissingDefaultDecals();
    }
#endif

    [ContextMenu("Create Missing Default Decals")]
    public void CreateMissingDefaultDecals()
    {
        BuildDefaultDecals(false);
    }

    [ContextMenu("Reset Default Decals")]
    public void ResetDefaultDecals()
    {
        BuildDefaultDecals(true);
    }

    public void BuildDefaultDecals(bool resetExisting)
    {
        if (resetExisting)
            ClearGeneratedDecals();

        float halfLength = courtLength * 0.5f;
        float halfDoubles = doublesWidth * 0.5f;
        float halfSingles = singlesWidth * 0.5f;
        float serviceHalfX = serviceLineDistanceFromNet * 0.5f;
        float serviceQuarterZ = halfSingles * 0.5f;

        CreateDecal("BaselineScuff_Near", baselineScuffMaterial,
            new Vector3(-halfLength + 0.45f, projectorHeight, 0f),
            new Vector2(0.95f, doublesWidth + 0.8f), 0f, 0.55f, resetExisting);

        CreateDecal("BaselineScuff_Far", baselineScuffMaterial,
            new Vector3(halfLength - 0.45f, projectorHeight, 0f),
            new Vector2(0.95f, doublesWidth + 0.8f), 180f, 0.48f, resetExisting);

        CreateDecal("ServiceBoxWear_Near_Left", serviceBoxWearMaterial,
            new Vector3(-serviceHalfX, projectorHeight, -serviceQuarterZ),
            new Vector2(serviceLineDistanceFromNet * 0.82f, halfSingles * 0.82f), -6f, 0.42f, resetExisting);

        CreateDecal("ServiceBoxWear_Near_Right", serviceBoxWearMaterial,
            new Vector3(-serviceHalfX, projectorHeight, serviceQuarterZ),
            new Vector2(serviceLineDistanceFromNet * 0.82f, halfSingles * 0.82f), 5f, 0.38f, resetExisting);

        CreateDecal("ServiceBoxWear_Far_Left", serviceBoxWearMaterial,
            new Vector3(serviceHalfX, projectorHeight, -serviceQuarterZ),
            new Vector2(serviceLineDistanceFromNet * 0.78f, halfSingles * 0.80f), 8f, 0.34f, resetExisting);

        CreateDecal("ServiceBoxWear_Far_Right", serviceBoxWearMaterial,
            new Vector3(serviceHalfX, projectorHeight, serviceQuarterZ),
            new Vector2(serviceLineDistanceFromNet * 0.78f, halfSingles * 0.80f), -7f, 0.34f, resetExisting);

        CreateDecal("ShoeMarks_Near_Left", shoeMarksMaterial,
            new Vector3(-halfLength + 2.0f, projectorHeight, -2.35f),
            new Vector2(2.0f, 1.35f), 14f, 0.45f, resetExisting);

        CreateDecal("ShoeMarks_Near_Right", shoeMarksMaterial,
            new Vector3(-halfLength + 2.2f, projectorHeight, 2.25f),
            new Vector2(2.2f, 1.25f), -18f, 0.40f, resetExisting);

        CreateDecal("ShoeMarks_Far_Left", shoeMarksMaterial,
            new Vector3(halfLength - 2.0f, projectorHeight, -2.45f),
            new Vector2(1.9f, 1.25f), 166f, 0.32f, resetExisting);

        CreateDecal("ShoeMarks_Far_Right", shoeMarksMaterial,
            new Vector3(halfLength - 2.1f, projectorHeight, 2.40f),
            new Vector2(2.1f, 1.35f), -162f, 0.32f, resetExisting);

        CreateDecal("DustPatch_Near_Backcourt", dustPatchMaterial,
            new Vector3(-halfLength + 4.0f, projectorHeight, 0.4f),
            new Vector2(4.8f, doublesWidth * 0.78f), 2f, 0.28f, resetExisting);

        CreateDecal("DustPatch_Far_Backcourt", dustPatchMaterial,
            new Vector3(halfLength - 4.2f, projectorHeight, -0.3f),
            new Vector2(4.2f, doublesWidth * 0.72f), -4f, 0.24f, resetExisting);

        CreateDecal("DustPatch_CenterCourt", dustPatchMaterial,
            new Vector3(0f, projectorHeight, 0f),
            new Vector2(5.6f, doublesWidth * 0.62f), 0f, 0.18f, resetExisting);
    }

    private void CreateDecal(string objectName, Material material, Vector3 position, Vector2 size, float inPlaneRotationDeg, float fade, bool resetExisting)
    {
        Transform child = FindDirectChild(objectName);
        bool created = child == null;

        if (created)
        {
            GameObject go = new GameObject(objectName);
            child = go.transform;
            child.SetParent(transform, false);
        }

        DecalProjector projector = child.GetComponent<DecalProjector>();
        if (projector == null)
            projector = child.gameObject.AddComponent<DecalProjector>();

        if (created || resetExisting)
        {
            child.SetPositionAndRotation(position, CourtDecalRotation(inPlaneRotationDeg));
            child.localScale = Vector3.one;
            projector.size = new Vector3(size.x, size.y, projectorDepth);
            projector.pivot = Vector3.zero;
            projector.drawDistance = drawDistance;
            projector.fadeScale = 0.92f;
            projector.fadeFactor = Mathf.Clamp01(fade);
        }

        if (material != null && projector.material != material)
            projector.material = material;
    }

    private Transform FindDirectChild(string objectName)
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child.name == objectName)
                return child;
        }

        return null;
    }

    private Quaternion CourtDecalRotation(float inPlaneRotationDeg)
    {
        return Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(0f, 0f, inPlaneRotationDeg);
    }

    private void ClearGeneratedDecals()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child.GetComponent<DecalProjector>() == null)
                continue;

            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }
    }
}
