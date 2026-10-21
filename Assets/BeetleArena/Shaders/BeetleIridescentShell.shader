Shader "BeetleArena/IridescentShell"
{
    Properties
    {
        [MainTexture] _BaseMap("Shell Detail Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Reflectance Tint", Color) = (1,1,1,1)
        _DetailStrength("Original Texture Luminance", Range(0,1)) = 0.12
        [Header(Iridescent Film)]
        [NoScaleOffset] _IridescenceRamp("Rainbow Strip", 2D) = "white" {}
        _Iridescence("Iridescence Strength", Range(0,1)) = 1
        _RampOffset("Facing Color Position", Range(0,1)) = 0.32
        _AngleShift("Grazing Color Shift", Range(-1,1)) = 0.3
        _AnglePower("Angle Response", Range(0.1,4)) = 0.7
        _RampRow("Strip Sample Row", Range(0,1)) = 0.5
        _FilmVariation("Film Variation", Range(0,0.25)) = 0.045
        _FilmScale("Film Variation Scale", Range(0.1,8)) = 1.2
        [Header(Color Tuning)]
        _HueShift("Rainbow Hue (Degrees)", Range(-180,180)) = 0
        _Saturation("Rainbow Saturation", Range(0,2)) = 1.15
        _Brightness("Reflectance Brightness", Range(0.25,2)) = 1
        _ReflectanceLift("Ramp Reflectance Recovery", Range(0,1)) = 0.65
        [Header(Metal and Clear Coat)]
        _Metallic("Metallic", Range(0,1)) = 1
        _Smoothness("Metal Smoothness", Range(0,0.95)) = 0.88
        _CoatStrength("Clear Coat", Range(0,1)) = 0.18
        _CoatSmoothness("Coat Smoothness", Range(0,0.95)) = 0.88
        [Header(Toon Silhouette)]
        _OutlineColor("Outline Color", Color) = (0.018,0.028,0.022,1)
        _OutlineWidth("Outline Width (Pixels)", Range(0,5)) = 0.85
        [HideInInspector] _MetalFinishVersion("Metal Finish Version", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "BeetleIridescentInput.hlsl"
        ENDHLSL
        Pass
        {
            Name "ShellOutline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front ZWrite Off ZTest LEqual
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex OutlineVertex
            #pragma fragment OutlineFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            ShellVaryings OutlineVertex(ShellAttributes input)
            {
                ShellVaryings output = ShellVertex(input);
                float2 direction = mul((float3x3)UNITY_MATRIX_VP, TransformObjectToWorldNormal(input.normalOS)).xy * _ScaledScreenParams.xy;
                direction *= rsqrt(max(dot(direction, direction), 1e-8));
                output.positionCS.xy += direction * (2 * _OutlineWidth / _ScaledScreenParams.xy) * output.positionCS.w;
                return output;
            }
            half4 OutlineFragment(ShellVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                clip(_OutlineWidth - 0.001);
                return half4(MixFog(_OutlineColor.rgb, input.fog), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShellForward"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Back ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShellVertex
            #pragma fragment ShellFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            half4 ShellFragment(ShellVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                InputData lighting = (InputData)0;
                lighting.positionWS = input.positionWS;
                lighting.positionCS = input.positionCS;
                lighting.normalWS = NormalizeNormalPerPixel(input.normalWS);
                lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    lighting.shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                    lighting.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                lighting.bakedGI = SampleSH(lighting.normalWS);
                lighting.vertexLighting = input.vertexLighting;
                lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                lighting.shadowMask = half4(1,1,1,1);
                SurfaceData surface = (SurfaceData)0;
                surface.albedo = ShellReflectance(input, lighting.normalWS, lighting.viewDirectionWS);
                surface.metallic = _Metallic;
                surface.smoothness = _Smoothness;
                surface.normalTS = half3(0,0,1);
                surface.occlusion = 1;
                surface.alpha = 1;
                surface.clearCoatMask = _CoatStrength;
                surface.clearCoatSmoothness = _CoatSmoothness;
                // Standard URP GGX, Fresnel, reflection probes and shadowed lights; no emissive rainbow.
                half4 color = UniversalFragmentPBR(lighting, surface);
                color.rgb = MixFog(color.rgb, input.fog);
                return color;
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShellVertex
            #pragma fragment ShellDepthNormals
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 ShellDepthNormals(ShellVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 normal = normalize(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    return half4(PackFloat2To888(saturate(PackNormalOctQuadEncode(normal) * 0.5 + 0.5)), 0);
                #else
                    return half4(normal, 0);
                #endif
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
