using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
[DisallowMultipleComponent]
public class NetVisibleDistanceMaterial : MonoBehaviour
{
    [Header("Target Matching")]
    [SerializeField] private bool includeChildren = true;
    [SerializeField] private string[] darkNetNameTokens =
    {
        "cord",
        "knot",
        "ribbon"
    };
    [SerializeField] private string[] excludedNameTokens =
    {
        "white",
        "tape",
        "strap",
        "wood",
        "post",
        "stick",
        "metal",
        "anchor",
        "foot"
    };

    [Header("Material")]
    [SerializeField] private Material overrideMaterial;
    [SerializeField] private Color visibleNetColor = new Color(0.0f, 0.009f, 0.0f, 1f);
    [SerializeField] private Color emissionColor = new Color(0.0f, 0.015f, 0.0f, 1f);
    [SerializeField] private bool useExperimentalThickeningShader = false;
    [SerializeField] private float distanceThicken = 0.008f;
    [SerializeField] private float thickenStartDistance = 8f;
    [SerializeField] private float thickenFullDistance = 28f;
    [SerializeField] private float alpha = 1f;
    [SerializeField] private int renderQueue = 2450;

    [Header("Renderer Settings")]
    [SerializeField] private bool disableShadows = true;
    [SerializeField] private bool disableLightProbes = true;
    [SerializeField] private bool disableMotionVectors = true;

    private Material generatedMaterial;

    private void OnEnable()
    {
        Apply();
    }

    private void OnValidate()
    {
        alpha = Mathf.Clamp01(alpha);
        distanceThicken = Mathf.Max(0f, distanceThicken);
        thickenStartDistance = Mathf.Max(0f, thickenStartDistance);
        thickenFullDistance = Mathf.Max(thickenStartDistance + 0.001f, thickenFullDistance);
        Apply();
    }

    [ContextMenu("Apply Visible Net Material")]
    public void Apply()
    {
        Renderer[] renderers = includeChildren
            ? GetComponentsInChildren<Renderer>(true)
            : GetComponents<Renderer>();

        Material material = overrideMaterial != null ? overrideMaterial : GetOrCreateGeneratedMaterial();
        ConfigureMaterial(material);

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !ShouldApplyToRenderer(renderer))
                continue;

            int materialCount = Mathf.Max(1, renderer.sharedMaterials.Length);
            Material[] materials = new Material[materialCount];
            for (int i = 0; i < materialCount; i++)
                materials[i] = material;

            renderer.sharedMaterials = materials;

            if (disableShadows)
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            if (disableLightProbes)
            {
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }

            if (disableMotionVectors)
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }
    }

    private bool ShouldApplyToRenderer(Renderer renderer)
    {
        string rendererName = renderer.name.ToLowerInvariant();
        string materialNames = "";
        foreach (Material material in renderer.sharedMaterials)
        {
            if (material != null)
                materialNames += " " + material.name.ToLowerInvariant();
        }

        string searchText = rendererName + materialNames;

        foreach (string token in excludedNameTokens)
        {
            if (!string.IsNullOrWhiteSpace(token) && searchText.Contains(token.ToLowerInvariant()))
                return false;
        }

        foreach (string token in darkNetNameTokens)
        {
            if (!string.IsNullOrWhiteSpace(token) && searchText.Contains(token.ToLowerInvariant()))
                return true;
        }

        return false;
    }

    private Material GetOrCreateGeneratedMaterial()
    {
        if (generatedMaterial != null)
            return generatedMaterial;

        Shader shader = null;
        if (useExperimentalThickeningShader)
            shader = Shader.Find("Hidden/Tennis/ExperimentalNetVisibleDistanceUnlit");
        if (shader == null)
            shader = Shader.Find("HDRP/Unlit");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Standard");

        generatedMaterial = new Material(shader)
        {
            name = "Generated_Net_Visible_Distance_Material"
        };

        return generatedMaterial;
    }

    private void ConfigureMaterial(Material material)
    {
        Color baseColor = visibleNetColor;
        baseColor.a = alpha;
        Color glowColor = emissionColor;
        glowColor.a = alpha;

        SetColorIfPresent(material, "_BaseColor", baseColor);
        SetColorIfPresent(material, "_Color", baseColor);
        SetColorIfPresent(material, "_UnlitColor", baseColor);
        SetColorIfPresent(material, "_EmissionColor", glowColor);
        SetColorIfPresent(material, "_EmissiveColor", glowColor);

        SetFloatIfPresent(material, "_DistanceThicken", distanceThicken);
        SetFloatIfPresent(material, "_ThickenStartDistance", thickenStartDistance);
        SetFloatIfPresent(material, "_ThickenFullDistance", thickenFullDistance);

        SetFloatIfPresent(material, "_SurfaceType", alpha < 0.999f ? 1f : 0f);
        SetFloatIfPresent(material, "_AlphaCutoffEnable", 0f);
        SetFloatIfPresent(material, "_TransparentCullMode", 0f);
        SetFloatIfPresent(material, "_CullMode", 0f);
        SetFloatIfPresent(material, "_CullModeForward", 0f);
        SetFloatIfPresent(material, "_DoubleSidedEnable", 1f);
        SetFloatIfPresent(material, "_EnableFogOnTransparent", 0f);
        SetFloatIfPresent(material, "_ReceivesSSR", 0f);
        SetFloatIfPresent(material, "_ReceivesSSRTransparent", 0f);
        SetFloatIfPresent(material, "_ZWrite", alpha < 0.999f ? 0f : 1f);

        material.renderQueue = alpha < 0.999f ? Mathf.Max(renderQueue, 3000) : renderQueue;
        material.enableInstancing = true;

        if (alpha < 0.999f)
        {
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
        else
        {
            material.SetOverrideTag("RenderType", "Opaque");
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
    }

    private void SetColorIfPresent(Material material, string property, Color color)
    {
        if (material.HasProperty(property))
            material.SetColor(property, color);
    }

    private void SetFloatIfPresent(Material material, string property, float value)
    {
        if (material.HasProperty(property))
            material.SetFloat(property, value);
    }
}
