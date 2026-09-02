using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds a deliberately simple three-deck ship section from box colliders.
/// Every generated structural part is tagged ShipHull, so FluidSimulator picks
/// it up through its existing solid-collider voxelisation path.
/// </summary>
[ExecuteAlways]
public sealed class ShipSectionBuilder : MonoBehaviour
{
    [Header("Build")]
    [Tooltip("Build the layout when this object awakens in Play mode.")]
    public bool buildOnAwake = true;
    [Tooltip("Also generate the layout in the editor so this scene is visible and editable before Play.")]
    public bool buildInEditMode = true;
    [Tooltip("The legacy flat test geometry to hide once this section is built.")]
    public GameObject legacyTestArea;
    public bool disableLegacyTestArea = true;

    [Header("Section Dimensions (metres)")]
    [Min(8f)] public float beam = 28f;
    [Min(8f)] public float length = 28f;
    public float keelY = -1.75f;

    [Header("Initial Draft (world ocean at Y = 0)")]
    [Tooltip("Initial keel depth below the fixed world ocean surface. The generated ship root is placed at -desiredInitialKeelDraft - keelY.")]
    [Min(0f)] public float desiredInitialKeelDraft = 2f;

    [Min(2f)] public float deckClearHeight = 4.5f;
    [Range(2, 4)] public int deckCount = 3;
    [Range(0.1f, 1f)] public float wallThickness = 0.5f;
    [Range(0.1f, 1f)] public float deckThickness = 0.5f;

    [Header("Compartments and openings")]
    [Range(2, 6)] public int compartmentCount = 4;
    [Min(1f)] public float doorwayWidth = 3f;
    [Min(0.5f)] public float doorwayHeight = 2f;
    [Tooltip("The open vertical stairwell is placed in the forward-port corner.")]
    [Min(1f)] public float stairwellSize = 3f;
    [Tooltip("Generated at the centre of the first deck's stairwell opening. FluidSimulator uses it for an on-demand vertical voxel-face connectivity snapshot.")]
    public Transform verticalOpeningProbe;
    [Tooltip("Generated invisible trigger volumes defining the only deck-to-deck voxel riser routes. They are ignored by solid voxelisation and are used only by FluidSimulator's conservative hatch transfer.")]
    public BoxCollider[] verticalRiserVolumes;

    [Header("Observation wall")]
    [Tooltip("Transparent HDRP material used for the forward end wall. Its collider remains solid to water.")]
    public Material observationWallMaterial;
    [Range(0.02f, 0.8f)] public float observationWallOpacity = 0.18f;
    [Header("Deck debug visibility")]
    [Tooltip("Makes the selected generated deck visually transparent for inspection. The deck MeshCollider remains enabled and remains solid to the fluid solver.")]
    public bool transparentMiddleDeckForDebug = false;
    [Tooltip("Generated deck number to show transparently. For HatchRiserTest, 1 is the deck above the lower compartment.")]
    [Range(1, 3)] public int transparentMiddleDeckNumber = 1;
    [Range(0.02f, 0.8f)] public float middleDeckDebugOpacity = 0.18f;
    [Tooltip("Optional transparent material override. If empty, the observation-wall material is copied.")]
    public Material middleDeckDebugMaterial;

    [Header("Breach / inflow")]
    public Transform breachPoint;
    [Tooltip("The breach is just inside the starboard hull, pointing into the aft lower compartment.")]
    public bool positionBreachPoint = true;
    public float breachHeightAboveKeel = 0.8f;
    public float breachAftOffset = 0.7f;

    const string GeneratedRootName = "Generated Ship Section";
    const string HullTag = "ShipHull";

    public Transform GeneratedShipRoot => transform.Find(GeneratedRootName);
    Material generatedObservationMaterial;
    Material generatedMiddleDeckMaterial;
    readonly Dictionary<MeshRenderer, Material> middleDeckOriginalMaterials =
        new Dictionary<MeshRenderer, Material>();

    void Awake()
    {
        if (Application.isPlaying && buildOnAwake)
            BuildLayout();
    }

    void OnEnable()
    {
        if (!Application.isPlaying && buildInEditMode)
            BuildLayout();
    }
    void OnValidate()
    {
        if (Application.isPlaying || !buildInEditMode)
            return;
        Transform generated = transform.Find(GeneratedRootName);
        if (generated != null)
            ApplyMiddleDeckDebugVisibility(generated);
    }

    [ContextMenu("Build Ship Section")]
    public void BuildLayout()
    {
        ClearGeneratedLayout();

        if (disableLegacyTestArea && legacyTestArea != null)
            legacyTestArea.SetActive(false);

        Transform root = new GameObject(GeneratedRootName).transform;
        // The fluid component is not necessarily at world origin. Keep this
        // generated geometry in simulation/world coordinates regardless of
        // which convenient scene object owns the builder component.
        root.SetParent(transform, true);
        float initialRootY = -Mathf.Max(desiredInitialKeelDraft, 0f) - keelY;
        root.SetPositionAndRotation(new Vector3(0f, initialRootY, 0f), Quaternion.identity);
        root.localScale = Vector3.one;

        float halfBeam = beam * 0.5f;
        float halfLength = length * 0.5f;
        float fullHeight = deckCount * deckClearHeight + deckThickness;
        float middleY = keelY + fullHeight * 0.5f;

        // Watertight lower hull: bottom, sides, and end bulkheads.
        AddHullBox(root, "Keel", Vector3.zero + Vector3.up * (keelY - deckThickness * 0.5f),
            new Vector3(beam + wallThickness * 2f, deckThickness, length + wallThickness * 2f));
        AddHullBox(root, "Port Hull", new Vector3(-halfBeam - wallThickness * 0.5f, middleY, 0f),
            new Vector3(wallThickness, fullHeight, length + wallThickness * 2f));
        AddHullBox(root, "Starboard Hull", new Vector3(halfBeam + wallThickness * 0.5f, middleY, 0f),
            new Vector3(wallThickness, fullHeight, length + wallThickness * 2f));
        AddHullBox(root, "Aft End Bulkhead", new Vector3(0f, middleY, -halfLength - wallThickness * 0.5f),
            new Vector3(beam, fullHeight, wallThickness));
        GameObject observationWall = AddHullBox(root, "Forward Observation Wall",
            new Vector3(0f, middleY, halfLength + wallThickness * 0.5f),
            new Vector3(beam, fullHeight, wallThickness));
        ApplyObservationMaterial(observationWall);

        BuildDecks(root, halfBeam, halfLength);
        ApplyMiddleDeckDebugVisibility(root);
        BuildTransverseBulkheads(root, halfBeam, halfLength);

        if (positionBreachPoint && breachPoint != null)
        {
            // The breach belongs to the ship so a later draft/rigid-body motion
            // never separates hydraulic forcing from the damaged hull.
            breachPoint.SetParent(root, false);
            breachPoint.localPosition = new Vector3(
                halfBeam - wallThickness - 0.15f,
                keelY + breachHeightAboveKeel,
                -halfLength * breachAftOffset);
            breachPoint.localRotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
        }
    }

    void BuildDecks(Transform root, float halfBeam, float halfLength)
    {
        // The open stairwell lets a filled lower compartment progress upward.
        float stairCenterX = -halfBeam + stairwellSize * 0.75f;
        float stairCenterZ = halfLength - stairwellSize * 0.75f;
        float leftEdge = stairCenterX - stairwellSize * 0.5f;
        float rightEdge = stairCenterX + stairwellSize * 0.5f;
        float aftEdge = stairCenterZ - stairwellSize * 0.5f;

        if (verticalOpeningProbe == null)
        {
            GameObject probe = new GameObject("Vertical Opening Probe");
            probe.transform.SetParent(root, false);
            verticalOpeningProbe = probe.transform;
        }
        verticalOpeningProbe.name = "Vertical Opening Probe";
        verticalOpeningProbe.SetParent(root, false);
        // The diagnostic point and each trigger sit exactly on the open face
        // through a deck, rather than at the deck slab centre.
        verticalOpeningProbe.localPosition = new Vector3(stairCenterX,
            keelY + deckClearHeight - deckThickness * 0.5f, stairCenterZ);
        verticalOpeningProbe.localRotation = Quaternion.identity;
        verticalRiserVolumes = new BoxCollider[Mathf.Max(deckCount - 1, 0)];
        for (int deck = 1; deck < deckCount; deck++)
        {
            float y = keelY + deck * deckClearHeight;
            // Four slabs leave a continuous open stairwell between decks.
            AddHullBox(root, "Deck " + deck + " Port", new Vector3((leftEdge - halfBeam) * 0.5f, y, 0f),
                new Vector3(leftEdge + halfBeam, deckThickness, length));
            AddHullBox(root, "Deck " + deck + " Starboard", new Vector3((rightEdge + halfBeam) * 0.5f, y, 0f),
                new Vector3(halfBeam - rightEdge, deckThickness, length));
            AddHullBox(root, "Deck " + deck + " Stair Aft", new Vector3(stairCenterX, y, (aftEdge - halfLength) * 0.5f),
                new Vector3(stairwellSize, deckThickness, aftEdge + halfLength));
            AddHullBox(root, "Deck " + deck + " Stair Forward", new Vector3(stairCenterX, y, (stairCenterZ + stairwellSize * 0.5f + halfLength) * 0.5f),
                new Vector3(stairwellSize, deckThickness, halfLength - (stairCenterZ + stairwellSize * 0.5f)));

            // This is deliberately a trigger, so it never becomes a water-solid.
            // It marks the large real stairwell/hatch as an explicit upward route.
            // The trigger covers the deck face and the first open voxel layer
            // above it.  With a 0.5 m voxel grid, a deck-plane-only trigger
            // can fill the throat cell but leave it surrounded by deck voxels,
            // unable to reach the upper room.  This remains a short hatch
            // passage, not a route through any ordinary deck.
            verticalRiserVolumes[deck - 1] = AddVerticalRiserVolume(root,
                $"Hatch Riser Deck {deck} To {deck + 1}",
                new Vector3(stairCenterX, y, stairCenterZ),
                new Vector3(Mathf.Max(stairwellSize - 0.05f, 0.1f),
                    Mathf.Max(deckThickness + 0.75f, 1.25f),
                    Mathf.Max(stairwellSize - 0.05f, 0.1f)));
        }
    }

    BoxCollider AddVerticalRiserVolume(Transform root, string partName, Vector3 localPosition, Vector3 size)
    {
        GameObject volume = new GameObject(partName);
        volume.transform.SetParent(root, false);
        volume.transform.localPosition = localPosition;
        volume.transform.localRotation = Quaternion.identity;
        volume.transform.localScale = Vector3.one;
        BoxCollider trigger = volume.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = size;
        return trigger;
    }

    void BuildTransverseBulkheads(Transform root, float halfBeam, float halfLength)
    {
        int dividers = Mathf.Max(0, compartmentCount - 1);
        float span = length / compartmentCount;
        float doorwayHalf = doorwayWidth * 0.5f;

        for (int divider = 1; divider <= dividers; divider++)
        {
            float z = -halfLength + span * divider;
            for (int deck = 0; deck < deckCount; deck++)
            {
                float bottom = keelY + deck * deckClearHeight;
                float doorwayTop = Mathf.Min(bottom + doorwayHeight, bottom + deckClearHeight - deckThickness);
                float upperHeight = Mathf.Max(0.05f, bottom + deckClearHeight - doorwayTop);

                AddHullBox(root, $"Bulkhead {divider} Deck {deck + 1} Port", new Vector3((-halfBeam - doorwayHalf) * 0.5f, bottom + deckClearHeight * 0.5f, z),
                    new Vector3(halfBeam - doorwayHalf, deckClearHeight, wallThickness));
                AddHullBox(root, $"Bulkhead {divider} Deck {deck + 1} Starboard", new Vector3((halfBeam + doorwayHalf) * 0.5f, bottom + deckClearHeight * 0.5f, z),
                    new Vector3(halfBeam - doorwayHalf, deckClearHeight, wallThickness));
                AddHullBox(root, $"Bulkhead {divider} Deck {deck + 1} Door Header", new Vector3(0f, doorwayTop + upperHeight * 0.5f, z),
                    new Vector3(doorwayWidth, upperHeight, wallThickness));
            }
        }
    }

    GameObject AddHullBox(Transform root, string partName, Vector3 localPosition, Vector3 size)
    {
        if (size.x <= 0.01f || size.y <= 0.01f || size.z <= 0.01f)
            return null;

        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = partName;
        part.tag = HullTag;
        part.transform.SetParent(root, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = size;
        part.isStatic = true;
        return part;
    }

    void ApplyMiddleDeckDebugVisibility(Transform root)
    {
        if (root == null)
            return;

        string deckPrefix = "Deck " + Mathf.Clamp(transparentMiddleDeckNumber, 1, 3) + " ";
        List<MeshRenderer> renderers = new List<MeshRenderer>();
        foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer != null && renderer.gameObject.name.StartsWith(deckPrefix))
                renderers.Add(renderer);
        }

        foreach (MeshRenderer renderer in renderers)
        {
            if (!middleDeckOriginalMaterials.ContainsKey(renderer))
                middleDeckOriginalMaterials.Add(renderer, renderer.sharedMaterial);
        }

        if (!transparentMiddleDeckForDebug)
        {
            foreach (MeshRenderer renderer in renderers)
            {
                Material original;
                if (middleDeckOriginalMaterials.TryGetValue(renderer, out original))
                    renderer.sharedMaterial = original;
            }
            DestroyGeneratedMiddleDeckMaterial();
            return;
        }

        Material source = middleDeckDebugMaterial != null
            ? middleDeckDebugMaterial : observationWallMaterial;
        if (source == null)
            return;

        DestroyGeneratedMiddleDeckMaterial();
        generatedMiddleDeckMaterial = new Material(source)
        {
            name = "Generated Middle Deck Debug Glass"
        };
        Color tint = generatedMiddleDeckMaterial.HasProperty("_BaseColor")
            ? generatedMiddleDeckMaterial.GetColor("_BaseColor") : Color.white;
        tint.a = Mathf.Clamp(middleDeckDebugOpacity, 0.02f, 0.8f);
        if (generatedMiddleDeckMaterial.HasProperty("_BaseColor"))
            generatedMiddleDeckMaterial.SetColor("_BaseColor", tint);
        if (generatedMiddleDeckMaterial.HasProperty("_Color"))
            generatedMiddleDeckMaterial.SetColor("_Color", tint);

        foreach (MeshRenderer renderer in renderers)
            renderer.sharedMaterial = generatedMiddleDeckMaterial;
    }

    void DestroyGeneratedMiddleDeckMaterial()
    {
        if (generatedMiddleDeckMaterial == null)
            return;
        if (Application.isPlaying)
            Destroy(generatedMiddleDeckMaterial);
        else
            DestroyImmediate(generatedMiddleDeckMaterial);
        generatedMiddleDeckMaterial = null;
    }

    void ApplyObservationMaterial(GameObject observationWall)
    {
        if (observationWall == null || observationWallMaterial == null)
            return;

        if (generatedObservationMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(generatedObservationMaterial);
            else
                DestroyImmediate(generatedObservationMaterial);
        }

        generatedObservationMaterial = new Material(observationWallMaterial)
        {
            name = "Generated Observation Glass"
        };
        Color tint = generatedObservationMaterial.HasProperty("_BaseColor")
            ? generatedObservationMaterial.GetColor("_BaseColor")
            : Color.white;
        tint.a = observationWallOpacity;
        if (generatedObservationMaterial.HasProperty("_BaseColor"))
            generatedObservationMaterial.SetColor("_BaseColor", tint);
        if (generatedObservationMaterial.HasProperty("_Color"))
            generatedObservationMaterial.SetColor("_Color", tint);
        observationWall.GetComponent<MeshRenderer>().sharedMaterial = generatedObservationMaterial;
    }

    [ContextMenu("Clear Generated Ship Section")]
    public void ClearGeneratedLayout()
    {
        if (generatedObservationMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(generatedObservationMaterial);
            else
                DestroyImmediate(generatedObservationMaterial);
            generatedObservationMaterial = null;
        }
        DestroyGeneratedMiddleDeckMaterial();
        middleDeckOriginalMaterials.Clear();
        Transform generated = transform.Find(GeneratedRootName);
        if (generated == null)
            return;

        // BreachPoint is authored outside the generated hierarchy. Preserve it
        // while replacing the generated hull, then reattach it during BuildLayout.
        if (breachPoint != null && breachPoint.IsChildOf(generated))
            breachPoint.SetParent(transform, true);

        if (Application.isPlaying)
            Destroy(generated.gameObject);
        else
            DestroyImmediate(generated.gameObject);
    }

    void OnDrawGizmos()
    {
        if (breachPoint == null)
            return;

        Gizmos.color = new Color(1f, 0.2f, 0.05f, 1f);
        Gizmos.DrawSphere(breachPoint.position, 0.25f);
        Gizmos.DrawRay(breachPoint.position, breachPoint.forward * 1.5f);
    }
}
