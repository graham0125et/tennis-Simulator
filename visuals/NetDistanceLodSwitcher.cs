using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public class NetDistanceLodSwitcher : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
    [SerializeField] private float farLodDistance = 18f;
    [SerializeField] private float hysteresis = 2f;
    [SerializeField] private bool autoRefreshRenderers = true;

    [Header("Name Matching")]
    [SerializeField] private string farLodNameToken = "net_lod_distance";
    [SerializeField] private string[] detailedGridNameTokens =
    {
        "net_unity_merged_round_cords",
        "net_unity_merged_distance_ribbons",
        "net_unity_merged_knots",
        "net_vertical_cord_",
        "net_horizontal_cord_",
        "net_distance_",
        "net_knot_"
    };

    private readonly List<Renderer> detailedGridRenderers = new List<Renderer>();
    private readonly List<Renderer> farLodRenderers = new List<Renderer>();
    private bool usingFarLod;

    private void OnEnable()
    {
        RefreshRenderers();
        ApplyLodState(false);
    }

    private void OnValidate()
    {
        farLodDistance = Mathf.Max(0.1f, farLodDistance);
        hysteresis = Mathf.Max(0f, hysteresis);
        RefreshRenderers();
        UpdateLod();
    }

    private void LateUpdate()
    {
        UpdateLod();
    }

    [ContextMenu("Refresh Net LOD Renderers")]
    public void RefreshRenderers()
    {
        detailedGridRenderers.Clear();
        farLodRenderers.Clear();

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            string searchText = BuildSearchText(renderer);
            if (ContainsToken(searchText, farLodNameToken))
            {
                farLodRenderers.Add(renderer);
                continue;
            }

            foreach (string token in detailedGridNameTokens)
            {
                if (ContainsToken(searchText, token))
                {
                    detailedGridRenderers.Add(renderer);
                    break;
                }
            }
        }
    }

    private void UpdateLod()
    {
        if (autoRefreshRenderers && (detailedGridRenderers.Count == 0 || farLodRenderers.Count == 0))
            RefreshRenderers();

        Camera cameraToUse = targetCamera != null ? targetCamera : Camera.main;
        if (cameraToUse == null)
            return;

        float distance = Vector3.Distance(cameraToUse.transform.position, transform.position);
        float enterFarDistance = farLodDistance;
        float exitFarDistance = Mathf.Max(0.1f, farLodDistance - hysteresis);

        bool shouldUseFar = usingFarLod
            ? distance > exitFarDistance
            : distance >= enterFarDistance;

        if (shouldUseFar != usingFarLod)
            ApplyLodState(shouldUseFar);
    }

    private void ApplyLodState(bool useFar)
    {
        usingFarLod = useFar;

        foreach (Renderer renderer in detailedGridRenderers)
        {
            if (renderer != null)
                renderer.enabled = !useFar;
        }

        foreach (Renderer renderer in farLodRenderers)
        {
            if (renderer != null)
                renderer.enabled = useFar;
        }
    }

    private string BuildSearchText(Renderer renderer)
    {
        string text = renderer.name + " " + renderer.gameObject.name;
        Transform parent = renderer.transform.parent;
        while (parent != null && parent != transform)
        {
            text += " " + parent.name;
            parent = parent.parent;
        }

        foreach (Material material in renderer.sharedMaterials)
        {
            if (material != null)
                text += " " + material.name;
        }

        return text.ToLowerInvariant();
    }

    private bool ContainsToken(string searchText, string token)
    {
        return !string.IsNullOrWhiteSpace(token)
            && searchText.Contains(token.ToLowerInvariant());
    }
}
