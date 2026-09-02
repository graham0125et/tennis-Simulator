using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(100)]
public class BoundaryParticleBuilder : MonoBehaviour
{
    [Header("References")]
    public FluidSimulator fluidSimulator;

    [Header("Collider Sources")]
    public bool useTaggedColliders = true;
    public string colliderTag = "ShipHull";
    public Collider[] explicitColliders;

    [Header("Boundary Settings")]
    public float particleSpacing = 0f;
    [Min(1)] public int layers = 2;
    public bool clampToFluidBounds = true;
    public int maxBoundaryParticles = 200000;

    [Header("Runtime")]
    public int generatedBoundaryParticles = 0;
    public int sampledColliderCount = 0;
    public bool built = false;

    ComputeBuffer boundaryParticleBuffer;
    ComputeBuffer boundaryCellHeadsBuffer;
    ComputeBuffer boundaryNextIndexBuffer;

    const int BoundaryParticleStride = sizeof(float) * 8;

    void Start()
    {
        RebuildBoundaryParticles();
    }

    [ContextMenu("Rebuild Boundary Particles")]
    public void RebuildBoundaryParticles()
    {
        ReleaseBuffers();
        built = false;
        generatedBoundaryParticles = 0;
        sampledColliderCount = 0;

        if (fluidSimulator == null)
            fluidSimulator = FindFirstObjectByType<FluidSimulator>();

        if (fluidSimulator == null)
        {
            Debug.LogWarning("BoundaryParticleBuilder: no FluidSimulator found.");
            return;
        }

        float spacing = particleSpacing > 0f ? particleSpacing : Mathf.Max(0.01f, fluidSimulator.particleRadius);
        List<Collider> colliders = CollectColliders();
        sampledColliderCount = colliders.Count;

        if (colliders.Count == 0)
        {
            Debug.LogWarning("BoundaryParticleBuilder: no colliders found. Add colliders manually or tag wall/floor/ceiling objects as ShipHull.");
            return;
        }

        List<BoundaryParticle> boundaryParticles = new List<BoundaryParticle>();
        HashSet<Vector3Int> occupied = new HashSet<Vector3Int>();

        for (int c = 0; c < colliders.Count; c++)
        {
            Collider col = colliders[c];
            if (col == null || !col.enabled)
                continue;

            SampleCollider(col, spacing, boundaryParticles, occupied);
            if (boundaryParticles.Count >= maxBoundaryParticles)
                break;
        }

        if (boundaryParticles.Count == 0)
        {
            Debug.LogWarning("BoundaryParticleBuilder: no boundary particles generated. Check collider thickness and bounds.");
            return;
        }

        generatedBoundaryParticles = boundaryParticles.Count;
        BuildBuffers(boundaryParticles, spacing);
        fluidSimulator.SetBoundaryBuffers(
            boundaryParticleBuffer,
            boundaryCellHeadsBuffer,
            boundaryNextIndexBuffer,
            generatedBoundaryParticles);

        built = true;
        Debug.Log($"BoundaryParticleBuilder: generated {generatedBoundaryParticles} solid boundary particles from {sampledColliderCount} colliders.");
    }

    List<Collider> CollectColliders()
    {
        List<Collider> colliders = new List<Collider>();

        if (explicitColliders != null)
        {
            for (int i = 0; i < explicitColliders.Length; i++)
            {
                if (explicitColliders[i] != null && !colliders.Contains(explicitColliders[i]))
                    colliders.Add(explicitColliders[i]);
            }
        }

        if (!useTaggedColliders || string.IsNullOrWhiteSpace(colliderTag))
            return colliders;

        try
        {
            GameObject[] taggedObjects = GameObject.FindGameObjectsWithTag(colliderTag);
            for (int i = 0; i < taggedObjects.Length; i++)
            {
                Collider[] objectColliders = taggedObjects[i].GetComponentsInChildren<Collider>();
                for (int j = 0; j < objectColliders.Length; j++)
                {
                    if (objectColliders[j] != null && !colliders.Contains(objectColliders[j]))
                        colliders.Add(objectColliders[j]);
                }
            }
        }
        catch (UnityException)
        {
            Debug.LogWarning($"BoundaryParticleBuilder: tag '{colliderTag}' does not exist yet. Create it, or assign colliders manually.");
        }

        return colliders;
    }

    void SampleCollider(Collider col, float spacing, List<BoundaryParticle> boundaryParticles, HashSet<Vector3Int> occupied)
    {
        Bounds bounds = col.bounds;
        int stepsX = Mathf.Max(1, Mathf.CeilToInt(bounds.size.x / spacing));
        int stepsY = Mathf.Max(1, Mathf.CeilToInt(bounds.size.y / spacing));
        int stepsZ = Mathf.Max(1, Mathf.CeilToInt(bounds.size.z / spacing));

        for (int ix = 0; ix <= stepsX; ix++)
        {
            float x = Mathf.Lerp(bounds.min.x, bounds.max.x, ix / (float)stepsX);
            for (int iy = 0; iy <= stepsY; iy++)
            {
                float y = Mathf.Lerp(bounds.min.y, bounds.max.y, iy / (float)stepsY);
                for (int iz = 0; iz <= stepsZ; iz++)
                {
                    if (boundaryParticles.Count >= maxBoundaryParticles)
                        return;

                    float z = Mathf.Lerp(bounds.min.z, bounds.max.z, iz / (float)stepsZ);
                    Vector3 p = new Vector3(x, y, z);

                    if (clampToFluidBounds && !InsideFluidBounds(p))
                        continue;

                    if (!PointInsideCollider(col, p))
                        continue;

                    if (!NearBoundsSurface(bounds, p, spacing * Mathf.Max(1, layers)))
                        continue;

                    Vector3Int key = Quantize(p, spacing);
                    if (!occupied.Add(key))
                        continue;

                    boundaryParticles.Add(new BoundaryParticle
                    {
                        pos = p,
                        density = fluidSimulator.restDensity,
                        pressure = 0f,
                        padding = Vector3.zero
                    });
                }
            }
        }
    }

    bool PointInsideCollider(Collider col, Vector3 point)
    {
        Vector3 closest = col.ClosestPoint(point);
        return (closest - point).sqrMagnitude <= 0.000001f;
    }

    bool NearBoundsSurface(Bounds bounds, Vector3 point, float shellDepth)
    {
        float dx = Mathf.Min(point.x - bounds.min.x, bounds.max.x - point.x);
        float dy = Mathf.Min(point.y - bounds.min.y, bounds.max.y - point.y);
        float dz = Mathf.Min(point.z - bounds.min.z, bounds.max.z - point.z);
        return Mathf.Min(dx, Mathf.Min(dy, dz)) <= shellDepth;
    }

    bool InsideFluidBounds(Vector3 p)
    {
        Vector3 min = fluidSimulator.boundsMin;
        Vector3 max = fluidSimulator.boundsMax;
        return p.x >= min.x && p.x <= max.x &&
               p.y >= min.y && p.y <= max.y &&
               p.z >= min.z && p.z <= max.z;
    }

    Vector3Int Quantize(Vector3 p, float spacing)
    {
        return new Vector3Int(
            Mathf.RoundToInt(p.x / spacing),
            Mathf.RoundToInt(p.y / spacing),
            Mathf.RoundToInt(p.z / spacing));
    }

    void BuildBuffers(List<BoundaryParticle> boundaryParticles, float spacing)
    {
        boundaryParticleBuffer = new ComputeBuffer(boundaryParticles.Count, BoundaryParticleStride);
        boundaryParticleBuffer.SetData(boundaryParticles.ToArray());

        int gridRes = fluidSimulator.gridResolution;
        int totalCells = fluidSimulator.TotalCells;
        float cellSize = fluidSimulator.CellSize;

        int[] heads = new int[totalCells];
        int[] next = new int[boundaryParticles.Count];

        for (int i = 0; i < heads.Length; i++)
            heads[i] = -1;

        for (int i = 0; i < next.Length; i++)
            next[i] = -1;

        for (int i = 0; i < boundaryParticles.Count; i++)
        {
            Vector3 rel = boundaryParticles[i].pos - fluidSimulator.boundsMin;
            int cx = Mathf.Clamp((int)(rel.x / cellSize), 0, gridRes - 1);
            int cy = Mathf.Clamp((int)(rel.y / cellSize), 0, gridRes - 1);
            int cz = Mathf.Clamp((int)(rel.z / cellSize), 0, gridRes - 1);
            int cell = cx + cy * gridRes + cz * gridRes * gridRes;

            next[i] = heads[cell];
            heads[cell] = i;
        }

        boundaryCellHeadsBuffer = new ComputeBuffer(totalCells, sizeof(int));
        boundaryNextIndexBuffer = new ComputeBuffer(boundaryParticles.Count, sizeof(int));
        boundaryCellHeadsBuffer.SetData(heads);
        boundaryNextIndexBuffer.SetData(next);
    }

    void OnDestroy()
    {
        ReleaseBuffers();
    }

    void ReleaseBuffers()
    {
        boundaryParticleBuffer?.Release();
        boundaryCellHeadsBuffer?.Release();
        boundaryNextIndexBuffer?.Release();

        boundaryParticleBuffer = null;
        boundaryCellHeadsBuffer = null;
        boundaryNextIndexBuffer = null;
    }
}
