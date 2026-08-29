using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

[ExecuteAlways]
public class TennisSkyboxController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Volume targetVolume;
    [SerializeField] private Light sunLight;
    [SerializeField] private Texture2D cloudMap;

    [Header("Sky Look")]
    [SerializeField, Range(8000f, 40000f)] private float skyLux = 18000f;
    [SerializeField, Range(0f, 0.03f)] private float hazeDensity = 0.0075f;
    [SerializeField, Range(0.75f, 1.2f)] private float skySaturation = 1.04f;
    [SerializeField] private Color airTint = new Color(0.9f, 0.965f, 1f, 1f);
    [SerializeField] private Color horizonTint = new Color(0.78f, 0.88f, 1f, 1f);
    [SerializeField] private Color zenithTint = new Color(0.56f, 0.72f, 1f, 1f);
    [SerializeField] private Color groundTint = new Color(0.07f, 0.082f, 0.086f, 1f);

    [Header("Cloud Layer")]
    [SerializeField] private bool enableCloudLayer = true;
    [SerializeField, Range(0f, 1f)] private float cloudOpacity = 0.42f;
    [SerializeField, Range(0f, 1f)] private float cloudDensity = 0.35f;
    [SerializeField, Range(2, 12)] private int cloudLightingSteps = 4;
    [SerializeField, Range(500f, 6000f)] private float cloudAltitude = 2400f;
    [SerializeField, Range(0f, 360f)] private float cloudRotation = 16f;
    [SerializeField] private Color cloudTint = new Color(0.94f, 0.975f, 1f, 1f);
    [SerializeField, Range(-2f, 2f)] private float cloudExposure = 0.15f;
    [SerializeField, Range(0f, 60f)] private float windSpeedKph = 6f;
    [SerializeField, Range(0f, 360f)] private float windOrientationDeg = 28f;

    [Header("Sun")]
    [SerializeField, Range(20000f, 140000f)] private float sunLux = 100000f;
    [SerializeField, Range(4500f, 7500f)] private float sunTemperature = 5900f;

    private void Reset()
    {
        targetVolume = GetComponent<Volume>();
        if (sunLight == null)
        {
            Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (Light candidate in lights)
            {
                if (candidate.type == LightType.Directional)
                {
                    sunLight = candidate;
                    break;
                }
            }
        }
    }

    private void OnEnable()
    {
        ApplySettings();
    }

    private void OnValidate()
    {
        ApplySettings();
    }

    [ContextMenu("Apply Skybox Settings")]
    public void ApplySettings()
    {
        if (targetVolume == null)
            targetVolume = GetComponent<Volume>();

        VolumeProfile profile = targetVolume != null ? targetVolume.sharedProfile : null;
        if (profile == null)
            return;

        if (profile.TryGet(out VisualEnvironment environment))
        {
            environment.skyType.overrideState = true;
            environment.skyType.value = (int)SkyType.PhysicallyBased;
            environment.cloudType.overrideState = true;
            environment.cloudType.value = enableCloudLayer ? (int)CloudType.CloudLayer : 0;
            environment.windOrientation.overrideState = true;
            environment.windOrientation.value = windOrientationDeg;
            environment.windSpeed.overrideState = true;
            environment.windSpeed.value = windSpeedKph;
        }

        if (profile.TryGet(out PhysicallyBasedSky sky))
        {
            sky.desiredLuxValue.overrideState = true;
            sky.desiredLuxValue.value = skyLux;
            sky.airTint.overrideState = true;
            sky.airTint.value = airTint;
            sky.aerosolDensity.overrideState = true;
            sky.aerosolDensity.value = hazeDensity;
            sky.aerosolTint.overrideState = true;
            sky.aerosolTint.value = new Color(0.86f, 0.91f, 0.98f, 1f);
            sky.groundTint.overrideState = true;
            sky.groundTint.value = groundTint;
            sky.colorSaturation.overrideState = true;
            sky.colorSaturation.value = skySaturation;
            sky.horizonTint.overrideState = true;
            sky.horizonTint.value = horizonTint;
            sky.zenithTint.overrideState = true;
            sky.zenithTint.value = zenithTint;
        }

        if (profile.TryGet(out CloudLayer clouds))
        {
            clouds.active = enableCloudLayer;
            clouds.opacity.overrideState = true;
            clouds.opacity.value = cloudOpacity;
            clouds.upperHemisphereOnly.overrideState = true;
            clouds.upperHemisphereOnly.value = true;
            clouds.layers.overrideState = true;
            clouds.layers.value = CloudMapMode.Single;
            clouds.resolution.overrideState = true;
            clouds.resolution.value = CloudResolution.CloudResolution1024;
            clouds.shadowMultiplier.overrideState = true;
            clouds.shadowMultiplier.value = 0f;

            clouds.layerA.cloudMap.overrideState = true;
            if (cloudMap != null)
                clouds.layerA.cloudMap.value = cloudMap;
            clouds.layerA.opacityR.overrideState = true;
            clouds.layerA.opacityR.value = 1f;
            clouds.layerA.opacityG.overrideState = true;
            clouds.layerA.opacityG.value = 0f;
            clouds.layerA.opacityB.overrideState = true;
            clouds.layerA.opacityB.value = 0f;
            clouds.layerA.opacityA.overrideState = true;
            clouds.layerA.opacityA.value = 0f;
            clouds.layerA.altitude.overrideState = true;
            clouds.layerA.altitude.value = cloudAltitude;
            clouds.layerA.rotation.overrideState = true;
            clouds.layerA.rotation.value = cloudRotation;
            clouds.layerA.tint.overrideState = true;
            clouds.layerA.tint.value = cloudTint;
            clouds.layerA.exposure.overrideState = true;
            clouds.layerA.exposure.value = cloudExposure;
            clouds.layerA.distortionMode.overrideState = true;
            clouds.layerA.distortionMode.value = CloudDistortionMode.Procedural;
            clouds.layerA.scrollOrientation.overrideState = true;
            clouds.layerA.scrollOrientation.value = MakeWindValue(windOrientationDeg);
            clouds.layerA.scrollSpeed.overrideState = true;
            clouds.layerA.scrollSpeed.value = MakeWindValue(windSpeedKph);
            clouds.layerA.lighting.overrideState = true;
            clouds.layerA.lighting.value = true;
            clouds.layerA.steps.overrideState = true;
            clouds.layerA.steps.value = cloudLightingSteps;
            clouds.layerA.thickness.overrideState = true;
            clouds.layerA.thickness.value = cloudDensity;
            clouds.layerA.ambientProbeDimmer.overrideState = true;
            clouds.layerA.ambientProbeDimmer.value = 0.85f;
            clouds.layerA.castShadows.overrideState = true;
            clouds.layerA.castShadows.value = false;
        }

        if (sunLight != null)
        {
            sunLight.intensity = sunLux;
            sunLight.useColorTemperature = true;
            sunLight.colorTemperature = sunTemperature;
        }
    }

    private static WindParameter.WindParamaterValue MakeWindValue(float value)
    {
        return new WindParameter.WindParamaterValue
        {
            mode = WindParameter.WindOverrideMode.Custom,
            customValue = value,
            additiveValue = 0f,
            multiplyValue = 1f,
        };
    }
}
