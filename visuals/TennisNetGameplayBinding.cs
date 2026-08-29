using UnityEngine;

public static class TennisNetGameplayBinding
{
    private const string NewNetName = "tennisNetV4_TextureNet";
    private const string GameplayColliderName = "tennisNetV4_GameplayCollider";
    private const string OldNetName = "net";
    private const string DisabledOldNetName = "old_net_disabled";

    private const float OfficialCenterNetHeight = 0.914f;
    private const float OfficialPostNetHeight = 1.07f;
    private const float GameplayNetHeight = 1.07f;
    private const float GameplayNetThickness = 0.12f;
    private const int SagColliderSegments = 12;
    private const float SagColliderOverlap = 0.025f;
    private const float SagCurvePower = 1.85f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BindV4NetOnSceneLoad()
    {
        GameObject newNet = GameObject.Find(NewNetName);
        if (newNet == null)
            return;

        GameObject oldNet = GameObject.Find(OldNetName) ?? GameObject.Find(DisabledOldNetName);
        PhysicsMaterial netPhysicsMaterial = DisableOldNetAndGetPhysicsMaterial(oldNet);

        TennisNetSagColliderBuilder builder = newNet.GetComponent<TennisNetSagColliderBuilder>();
        if (builder == null)
            builder = newNet.AddComponent<TennisNetSagColliderBuilder>();
        builder.Rebuild();

        Transform gameplayCollider = EnsureGameplayCollider(newNet, netPhysicsMaterial);
        if (builder != null)
            gameplayCollider = builder.GetColliderRoot();

        BindCannonNetReferences(gameplayCollider);
    }

    private static PhysicsMaterial DisableOldNetAndGetPhysicsMaterial(GameObject oldNet)
    {
        if (oldNet == null)
            return null;

        PhysicsMaterial material = null;
        Collider[] colliders = oldNet.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null)
                continue;

            if (material == null)
                material = collider.sharedMaterial;
            collider.enabled = false;
        }

        Renderer[] renderers = oldNet.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = false;
        }

        oldNet.name = DisabledOldNetName;
        return material;
    }

    private static Transform EnsureGameplayCollider(GameObject newNet, PhysicsMaterial netPhysicsMaterial)
    {
        Transform colliderTransform = newNet.transform.Find(GameplayColliderName);
        GameObject colliderObject;
        if (colliderTransform == null)
        {
            colliderObject = new GameObject(GameplayColliderName);
            colliderTransform = colliderObject.transform;
            colliderTransform.SetParent(newNet.transform, true);
        }
        else
        {
            colliderObject = colliderTransform.gameObject;
        }

        if (!TryGetRendererBounds(newNet, out Bounds bounds))
            bounds = new Bounds(newNet.transform.position + Vector3.up * (GameplayNetHeight * 0.5f), new Vector3(GameplayNetThickness, GameplayNetHeight, 12.8f));

        float centerY = bounds.min.y + GameplayNetHeight * 0.5f;
        Vector3 center = new Vector3(bounds.center.x, centerY, bounds.center.z);

        colliderObject.layer = newNet.layer;
        colliderTransform.position = center;
        colliderTransform.rotation = Quaternion.identity;
        colliderTransform.localScale = Vector3.one;

        TennisNetImpactWave wave = newNet.GetComponent<TennisNetImpactWave>();
        if (wave == null)
            wave = newNet.AddComponent<TennisNetImpactWave>();

        DisableParentCollider(colliderObject);
        RebuildSagSegmentColliders(colliderTransform, bounds, newNet.layer, netPhysicsMaterial, wave);

        return colliderTransform;
    }

    private static void DisableParentCollider(GameObject colliderObject)
    {
        BoxCollider parentCollider = colliderObject.GetComponent<BoxCollider>();
        if (parentCollider != null)
            parentCollider.enabled = false;
    }

    private static void RebuildSagSegmentColliders(
        Transform parent,
        Bounds bounds,
        int layer,
        PhysicsMaterial netPhysicsMaterial,
        TennisNetImpactWave wave)
    {
        const string segmentPrefix = "SagSegment_";

        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Transform child = parent.GetChild(i);
            if (child != null && child.name.StartsWith(segmentPrefix))
                Object.Destroy(child.gameObject);
        }

        bool spanRunsOnX = bounds.size.x >= bounds.size.z;
        float spanMin = spanRunsOnX ? bounds.min.x : bounds.min.z;
        float spanLength = spanRunsOnX ? bounds.size.x : bounds.size.z;
        float fixedAxis = spanRunsOnX ? bounds.center.z : bounds.center.x;
        float segmentLength = spanLength / SagColliderSegments;
        float bottomY = bounds.min.y;

        for (int i = 0; i < SagColliderSegments; i++)
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
                ? new Vector3(segmentLength + SagColliderOverlap, colliderHeight, GameplayNetThickness)
                : new Vector3(GameplayNetThickness, colliderHeight, segmentLength + SagColliderOverlap);

            GameObject segmentObject = new GameObject($"{segmentPrefix}{i:00}");
            segmentObject.layer = layer;
            Transform segmentTransform = segmentObject.transform;
            segmentTransform.SetParent(parent, true);
            segmentTransform.position = position;
            segmentTransform.rotation = Quaternion.identity;
            segmentTransform.localScale = Vector3.one;

            BoxCollider segmentCollider = segmentObject.AddComponent<BoxCollider>();
            segmentCollider.isTrigger = false;
            segmentCollider.size = size;
            segmentCollider.center = Vector3.zero;
            if (netPhysicsMaterial != null)
                segmentCollider.sharedMaterial = netPhysicsMaterial;

            TennisNetImpactRelay relay = segmentObject.AddComponent<TennisNetImpactRelay>();
            relay.SetWave(wave);
        }
    }

    private static float NetTopHeightAtNormalizedSpan(float normalizedSpan)
    {
        float edgeAmount = Mathf.Pow(Mathf.Clamp01(Mathf.Abs(normalizedSpan)), SagCurvePower);
        return Mathf.Lerp(OfficialCenterNetHeight, OfficialPostNetHeight, edgeAmount);
    }

    private static bool TryGetRendererBounds(GameObject root, out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
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

    private static void BindCannonNetReferences(Transform gameplayCollider)
    {
        if (gameplayCollider == null)
            return;

        swipeMouseBall[] swipeControllers = Object.FindObjectsByType<swipeMouseBall>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < swipeControllers.Length; i++)
        {
            swipeMouseBall swipe = swipeControllers[i];
            if (swipe == null)
                continue;

            swipe.cannonNetPoint = gameplayCollider;
            swipe.cannonNetObjectName = GameplayColliderName;
            swipe.cannonAutoSetNetHeightFromRenderer = false;
            swipe.cannonFeedNetHeight = OfficialCenterNetHeight;
        }

        ballCannon[] cannons = Object.FindObjectsByType<ballCannon>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < cannons.Length; i++)
        {
            ballCannon cannon = cannons[i];
            if (cannon == null)
                continue;

            cannon.netPoint = gameplayCollider;
            cannon.netObjectName = GameplayColliderName;
            cannon.autoSetFeedNetHeightFromRenderer = false;
            cannon.feedNetHeight = OfficialCenterNetHeight;
        }
    }
}
