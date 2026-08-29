Shader "Hidden/Tennis/ExperimentalNetVisibleDistanceUnlit"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.0, 0.009, 0.0, 1.0)
        _DistanceThicken ("Distance Thicken", Range(0.0, 0.03)) = 0.008
        _ThickenStartDistance ("Thicken Start Distance", Float) = 8.0
        _ThickenFullDistance ("Thicken Full Distance", Float) = 28.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "HDRenderPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry+50"
        }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }

            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _DistanceThicken;
                float _ThickenStartDistance;
                float _ThickenFullDistance;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;

                float3 positionWS = TransformObjectToWorld(input.positionOS);
                float3 normalWS = normalize(TransformObjectToWorldNormal(input.normalOS));

                float3 cameraToPoint = positionWS - _WorldSpaceCameraPos.xyz;
                float distanceToCamera = length(cameraToPoint);
                float fade = saturate((distanceToCamera - _ThickenStartDistance) / max(0.001, _ThickenFullDistance - _ThickenStartDistance));

                float3 viewDir = normalize(cameraToPoint);
                float3 screenFacingNormal = normalWS - viewDir * dot(normalWS, viewDir);
                float normalLength = length(screenFacingNormal);
                screenFacingNormal = normalLength > 0.001 ? screenFacingNormal / normalLength : normalWS;

                positionWS += screenFacingNormal * (_DistanceThicken * fade);
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                return _BaseColor;
            }
            ENDHLSL
        }
    }
}
