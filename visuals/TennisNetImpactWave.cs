using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class TennisNetImpactWave : MonoBehaviour
{
    [SerializeField] private string[] sheetNameTokens = { "texture_sheet" };
    [SerializeField] private float waveRadius = 1.15f;
    [SerializeField] private float maxAmplitude = 0.075f;
    [SerializeField] private float speedToAmplitude = 0.004f;
    [SerializeField] private float frequency = 14f;
    [SerializeField] private float damping = 4.2f;
    [SerializeField] private float duration = 1.15f;

    private readonly List<SheetState> sheets = new List<SheetState>();
    private bool loggedUnreadableMeshWarning;

    private void Awake()
    {
        CacheSheets();
    }

    private void LateUpdate()
    {
        float now = Time.time;
        for (int i = 0; i < sheets.Count; i++)
        {
            sheets[i].UpdateMesh(now, waveRadius, frequency, damping, duration);
        }
    }

    public void RegisterImpact(Vector3 worldPoint, Vector3 worldVelocity)
    {
        if (sheets.Count == 0)
            CacheSheets();

        float speed = worldVelocity.magnitude;
        float amplitude = Mathf.Min(maxAmplitude, Mathf.Max(0.018f, speed * speedToAmplitude));
        Vector3 pushDirection = worldVelocity.sqrMagnitude > 0.0001f ? worldVelocity.normalized : transform.forward;

        for (int i = 0; i < sheets.Count; i++)
        {
            sheets[i].StartWave(worldPoint, pushDirection, amplitude);
        }
    }

    [ContextMenu("Refresh Net Impact Sheets")]
    private void CacheSheets()
    {
        sheets.Clear();

        MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter filter = meshFilters[i];
            if (filter == null || filter.sharedMesh == null || !IsWaveSheet(filter.name))
                continue;

            if (!filter.sharedMesh.isReadable)
            {
                if (!loggedUnreadableMeshWarning)
                {
                    Debug.LogWarning(
                        $"[TennisNetImpactWave] Mesh '{filter.sharedMesh.name}' is not readable, so net deformation is disabled for it. " +
                        "Enable Read/Write in the model import settings."
                    );
                    loggedUnreadableMeshWarning = true;
                }

                continue;
            }

            Mesh meshInstance = Instantiate(filter.sharedMesh);
            meshInstance.name = $"{filter.sharedMesh.name}_ImpactWaveInstance";
            filter.sharedMesh = meshInstance;
            sheets.Add(new SheetState(filter.transform, meshInstance));
        }
    }

    private bool IsWaveSheet(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
            return false;

        for (int i = 0; i < sheetNameTokens.Length; i++)
        {
            string token = sheetNameTokens[i];
            if (!string.IsNullOrEmpty(token) &&
                objectName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private sealed class SheetState
    {
        private readonly Transform transform;
        private readonly Mesh mesh;
        private readonly Vector3[] baseVertices;
        private readonly Vector3[] workingVertices;

        private bool active;
        private float startTime;
        private float amplitude;
        private Vector3 localImpact;
        private Vector3 localPushDirection;

        public SheetState(Transform transform, Mesh mesh)
        {
            this.transform = transform;
            this.mesh = mesh;
            baseVertices = mesh.vertices;
            workingVertices = new Vector3[baseVertices.Length];
            Array.Copy(baseVertices, workingVertices, baseVertices.Length);
        }

        public void StartWave(Vector3 worldPoint, Vector3 worldPushDirection, float waveAmplitude)
        {
            active = true;
            startTime = Time.time;
            amplitude = waveAmplitude;
            localImpact = transform.InverseTransformPoint(worldPoint);
            localPushDirection = transform.InverseTransformDirection(worldPushDirection).normalized;

            if (localPushDirection.sqrMagnitude < 0.0001f)
                localPushDirection = Vector3.forward;
        }

        public void UpdateMesh(float now, float radius, float frequency, float damping, float duration)
        {
            if (!active)
                return;

            float age = now - startTime;
            if (age >= duration)
            {
                active = false;
                mesh.vertices = baseVertices;
                mesh.RecalculateBounds();
                return;
            }

            float wave = Mathf.Sin(age * frequency) * Mathf.Exp(-age * damping);
            float radiusSq = radius * radius;

            for (int i = 0; i < baseVertices.Length; i++)
            {
                Vector3 vertex = baseVertices[i];
                Vector3 delta = vertex - localImpact;
                float distSq = delta.sqrMagnitude;
                if (distSq > radiusSq)
                {
                    workingVertices[i] = vertex;
                    continue;
                }

                float normalizedDistance = Mathf.Sqrt(distSq) / Mathf.Max(0.0001f, radius);
                float falloff = Mathf.SmoothStep(1f, 0f, normalizedDistance);
                workingVertices[i] = vertex + localPushDirection * (amplitude * falloff * wave);
            }

            mesh.vertices = workingVertices;
            mesh.RecalculateBounds();
        }
    }
}
