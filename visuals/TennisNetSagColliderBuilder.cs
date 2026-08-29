using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public sealed class TennisNetSagColliderBuilder : MonoBehaviour
{
    private const string GameplayColliderName = "tennisNetV4_GameplayCollider";
    private const string SegmentPrefix = "SagSegment_";

    [SerializeField] private int segmentCount = 12;
    [SerializeField] private float centerHeight = 0.914f;
    [SerializeField] private float postHeight = 1.07f;
    [SerializeField] private float thickness = 0.12f;
    [SerializeField] private float overlap = 0.025f;
    [SerializeField] private float sagCurvePower = 1.85f;
    [SerializeField] private PhysicsMaterial netPhysicsMaterial;
    [SerializeField] private bool rebuildOnValidate = true;

    private bool isRebuilding;
#if UNITY_EDITOR
    private bool editorRebuildQueued;
#endif

    private void OnEnable()
    {
        if (Application.isPlaying)
            Rebuild();
        else
            RequestEditorRebuild();
    }

    private void OnValidate()
    {
        ClampSettings();

        if (rebuildOnValidate)
            RequestEditorRebuild();
    }

    [ContextMenu("Rebuild Sag Colliders")]
    public void Rebuild()
    {
        if (isRebuilding || gameObject == null)
            return;

        isRebuilding = true;

        ClampSettings();

        if (TryGetRendererBounds(out Bounds bounds))
        {
            Transform parent = EnsureColliderParent(bounds);
            RebuildSegments(parent, bounds);
        }

        isRebuilding = false;
    }

    private void RequestEditorRebuild()
    {
#if UNITY_EDITOR
        if (editorRebuildQueued)
            return;

        editorRebuildQueued = true;
        EditorApplication.delayCall += RebuildFromEditorDelay;
#else
        Rebuild();
#endif
    }

#if UNITY_EDITOR
    private void RebuildFromEditorDelay()
    {
        editorRebuildQueued = false;

        if (this == null || gameObject == null)
            return;

        Rebuild();
    }
#endif

    private void ClampSettings()
    {
        segmentCount = Mathf.Clamp(segmentCount, 1, 32);
        thickness = Mathf.Max(0.01f, thickness);
        overlap = Mathf.Max(0f, overlap);
        centerHeight = Mathf.Max(0.1f, centerHeight);
        postHeight = Mathf.Max(centerHeight, postHeight);
        sagCurvePower = Mathf.Max(0.1f, sagCurvePower);
    }

    public Transform GetColliderRoot()
    {
        Transform parent = transform.Find(GameplayColliderName);
        return parent != null ? parent : transform;
    }

    private Transform EnsureColliderParent(Bounds bounds)
    {
        Transform parent = transform.Find(GameplayColliderName);
        if (parent == null)
        {
            GameObject parentObject = new GameObject(GameplayColliderName);
            parent = parentObject.transform;
            parent.SetParent(transform, true);
        }

        float parentCenterY = bounds.min.y + postHeight * 0.5f;
        parent.gameObject.layer = gameObject.layer;
        parent.position = new Vector3(bounds.center.x, parentCenterY, bounds.center.z);
        parent.rotation = Quaternion.identity;
        parent.localScale = Vector3.one;

        BoxCollider oldParentCollider = parent.GetComponent<BoxCollider>();
        if (oldParentCollider != null)
            oldParentCollider.enabled = false;

        return parent;
    }

    private void RebuildSegments(Transform parent, Bounds bounds)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Transform child = parent.GetChild(i);
            if (child != null && child.name.StartsWith(SegmentPrefix))
                DestroyObject(child.gameObject);
        }

        bool spanRunsOnX = bounds.size.x >= bounds.size.z;
        float spanMin = spanRunsOnX ? bounds.min.x : bounds.min.z;
        float spanLength = spanRunsOnX ? bounds.size.x : bounds.size.z;
        float fixedAxis = spanRunsOnX ? bounds.center.z : bounds.center.x;
        float segmentLength = spanLength / segmentCount;
        float bottomY = bounds.min.y;

        TennisNetImpactWave wave = GetComponent<TennisNetImpactWave>();
        if (wave == null)
            wave = gameObject.AddComponent<TennisNetImpactWave>();

        for (int i = 0; i < segmentCount; i++)
        {
            float start = spanMin + segmentLength * i;
            float end = start + segmentLength;
            float centerOnSpan = (start + end) * 0.5f;
            float normalizedCenter = Mathf.InverseLerp(spanMin, spanMin + spanLength, centerOnSpan) * 2f - 1f;
            float topHeight = NetTopHeightAtNormalizedSpan(normalizedCenter);
            float colliderHeight = Mathf.Max(0.1f, topHeight);

            Vector3 position = spanRunsOnX
                ? new Vector3(centerOnSpan, bottomY + colliderHeight * 0.5f, fixedAxis)
                : new Vector3(fixedAxis, bottomY + colliderHeight * 0.5f, centerOnSpan);

            Vector3 size = spanRunsOnX
                ? new Vector3(segmentLength + overlap, colliderHeight, thickness)
                : new Vector3(thickness, colliderHeight, segmentLength + overlap);

            GameObject segmentObject = new GameObject($"{SegmentPrefix}{i:00}");
            segmentObject.layer = gameObject.layer;
            Transform segmentTransform = segmentObject.transform;
            segmentTransform.SetParent(parent, true);
            segmentTransform.position = position;
            segmentTransform.rotation = Quaternion.identity;
            segmentTransform.localScale = Vector3.one;

            BoxCollider collider = segmentObject.AddComponent<BoxCollider>();
            collider.isTrigger = false;
            collider.size = size;
            collider.center = Vector3.zero;
            if (netPhysicsMaterial != null)
                collider.sharedMaterial = netPhysicsMaterial;

            TennisNetImpactRelay relay = segmentObject.AddComponent<TennisNetImpactRelay>();
            relay.SetWave(wave);
        }
    }

    private float NetTopHeightAtNormalizedSpan(float normalizedSpan)
    {
        float edgeAmount = Mathf.Pow(Mathf.Clamp01(Mathf.Abs(normalizedSpan)), sagCurvePower);
        return Mathf.Lerp(centerHeight, postHeight, edgeAmount);
    }

    private bool TryGetRendererBounds(out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds;
    }

    private static void DestroyObject(GameObject target)
    {
        if (target == null)
            return;

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }
}
