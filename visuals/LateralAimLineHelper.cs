using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class LateralAimLineHelper : MonoBehaviour
{
    [Header("References")]
    public swipeMouseBall swipeSource;

    [Header("Toggle")]
    public bool helperEnabled = true;
    public KeyCode toggleKey = KeyCode.L;
    public bool logToggle;

    [Header("Line")]
    public float distanceBeforeReticle = 0.75f;
    public float lineLength = 0.75f;
    public float lineWidth = 0.018f;
    public float heightAboveReticlePlane = 0.035f;
    public Color lineColor = new Color(0.05f, 1f, 0.95f, 0.95f);
    public Material lineMaterial;

    private LineRenderer lineRenderer;
    private Material runtimeMaterial;

    private void Awake()
    {
        EnsureLineRenderer();
    }

    private void Update()
    {
        if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey))
        {
            helperEnabled = !helperEnabled;
            if (logToggle)
                Debug.Log($"[Lateral Aim Helper] {(helperEnabled ? "ON" : "OFF")}");
        }
    }

    private void LateUpdate()
    {
        EnsureLineRenderer();

        if (!helperEnabled)
        {
            SetVisible(false);
            return;
        }

        if (swipeSource == null)
            swipeSource = FindFirstObjectByType<swipeMouseBall>();

        if (swipeSource == null || !TryBuildLine(out Vector3 start, out Vector3 end))
        {
            SetVisible(false);
            return;
        }

        ApplyLineStyle();
        lineRenderer.positionCount = 2;
        lineRenderer.SetPosition(0, start);
        lineRenderer.SetPosition(1, end);
        SetVisible(true);
    }

    private void OnDestroy()
    {
        if (runtimeMaterial != null)
            Destroy(runtimeMaterial);
    }

    private bool TryBuildLine(out Vector3 start, out Vector3 end)
    {
        start = Vector3.zero;
        end = Vector3.zero;

        if (!swipeSource.TryGetLiveLateralAim(out Vector3 origin, out Vector3 reticlePosition, out Vector3 aimDir, out _))
            return false;

        Vector3 reticleCourse = reticlePosition - origin;
        reticleCourse.y = 0f;
        if (reticleCourse.sqrMagnitude < 1e-6f)
            return false;
        reticleCourse.Normalize();

        aimDir.y = 0f;
        if (aimDir.sqrMagnitude < 1e-6f)
            return false;
        aimDir.Normalize();

        float totalDistance = Vector3.Distance(
            new Vector3(origin.x, 0f, origin.z),
            new Vector3(reticlePosition.x, 0f, reticlePosition.z)
        );
        float beforeDistance = Mathf.Clamp(
            Mathf.Abs(distanceBeforeReticle),
            0.05f,
            Mathf.Max(0.05f, totalDistance - 0.05f)
        );

        Vector3 reticlePlaneCenter = reticlePosition - reticleCourse * beforeDistance;
        float planeDistance = Vector3.Dot(reticlePlaneCenter - origin, reticleCourse);
        float aimAlongReticleCourse = Vector3.Dot(aimDir, reticleCourse);
        if (Mathf.Abs(aimAlongReticleCourse) < 0.05f)
            return false;

        float aimDistance = planeDistance / aimAlongReticleCourse;
        if (aimDistance < 0f)
            return false;

        Vector3 markerCenter = origin + aimDir * aimDistance;
        markerCenter.y = reticlePosition.y + Mathf.Max(0f, heightAboveReticlePlane);

        Vector3 lateralAxis = Vector3.Cross(Vector3.up, reticleCourse);
        if (lateralAxis.sqrMagnitude < 1e-6f)
            return false;
        lateralAxis.Normalize();

        float halfLength = Mathf.Max(0.01f, lineLength) * 0.5f;
        start = markerCenter - lateralAxis * halfLength;
        end = markerCenter + lateralAxis * halfLength;
        return true;
    }

    private void EnsureLineRenderer()
    {
        if (lineRenderer != null)
            return;

        GameObject lineObject = new GameObject("Lateral Aim Line");
        lineObject.transform.SetParent(transform, false);
        lineRenderer = lineObject.AddComponent<LineRenderer>();
        lineRenderer.useWorldSpace = true;
        lineRenderer.positionCount = 2;
        lineRenderer.numCapVertices = 2;
        lineRenderer.numCornerVertices = 2;
        lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        lineRenderer.textureMode = LineTextureMode.Stretch;
        lineRenderer.alignment = LineAlignment.View;
        ApplyLineStyle();
        SetVisible(false);
    }

    private void ApplyLineStyle()
    {
        if (lineRenderer == null)
            return;

        lineRenderer.startWidth = Mathf.Max(0.001f, lineWidth);
        lineRenderer.endWidth = Mathf.Max(0.001f, lineWidth);
        lineRenderer.startColor = lineColor;
        lineRenderer.endColor = lineColor;

        Material material = lineMaterial != null ? lineMaterial : GetRuntimeMaterial();
        if (material != null && lineRenderer.sharedMaterial != material)
            lineRenderer.sharedMaterial = material;
    }

    private Material GetRuntimeMaterial()
    {
        if (runtimeMaterial != null)
            return runtimeMaterial;

        Shader shader = Shader.Find("HDRP/Unlit");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        if (shader == null)
            return null;

        runtimeMaterial = new Material(shader)
        {
            name = "Runtime Lateral Aim Line Material"
        };

        if (runtimeMaterial.HasProperty("_BaseColor"))
            runtimeMaterial.SetColor("_BaseColor", Color.white);
        if (runtimeMaterial.HasProperty("_Color"))
            runtimeMaterial.SetColor("_Color", Color.white);

        return runtimeMaterial;
    }

    private void SetVisible(bool visible)
    {
        if (lineRenderer != null && lineRenderer.enabled != visible)
            lineRenderer.enabled = visible;
    }
}