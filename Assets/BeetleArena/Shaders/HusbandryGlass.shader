Shader "BeetleArena/HusbandryGlass"
{
    Properties
    {
        [MainColor] _TransmissionColor("Transmission Through One World Unit", Color) = (0.84,0.97,0.93,1)
        _AbsorptionDensity("Glass Absorption Density", Range(0,4)) = 1
        _IOR("Index Of Refraction", Range(1,1.8)) = 1.5
        _Smoothness("Reflection Smoothness", Range(0.5,0.98)) = 0.9
        _ReflectionStrength("Environment Reflection", Range(0,1)) = 0.2
        _HighlightStrength("Main Light Highlight", Range(0,2)) = 1
        [Header(Soft Window Reflection)]
        [HDR] _WindowColor("Window Light Color", Color) = (0.92,0.97,1,1)
        _WindowStrength("Soft Window Strength", Range(0,2)) = 0.28
        _WindowMinimumFill("Window Fill With Bright Environment", Range(0,1)) = 0.35
        _WindowPosition("Window Center (World Space)", Vector) = (-4,-4,-8,0)
        _WindowDirection("Window Plane Direction (World Space)", Vector) = (-0.5,-0.65,-0.57,0)
        _WindowWidth("Window Half Width (World Units)", Range(0.1,8)) = 1.5
        _WindowHeight("Window Half Height (World Units)", Range(0.1,8)) = 1.8
        _WindowSoftness("Window Soft Edge", Range(0.02,0.8)) = 0.32
        [Header(Thin Pane Refraction)]
        _RefractionStrength("Refraction Displacement", Range(0,1)) = 0.5
        _MaxRefractionPixels("Maximum Displacement (Screen Pixels)", Range(0,3)) = 1.25
        _DepthRejection("Depth Edge Rejection (World Units)", Range(0.02,0.5)) = 0.15
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "GlassForward"
            Tags { "LightMode"="UniversalForwardOnly" }
            // One visible skin of the solid Cube represents the complete thin pane.
            Cull Back
            ZWrite Off
            ZTest LEqual
            Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex GlassVertex
            #pragma fragment GlassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #define _SURFACE_TYPE_TRANSPARENT 1
            #define HUSBANDRY_GLASS_HIGH 1
            #include "HusbandryGlassCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float GlassEyeDepth(float2 uv)
            {
                float raw=SampleSceneDepth(uv);
                return IsPerspectiveProjection()?LinearEyeDepth(raw,_ZBufferParams):LinearDepthToEyeDepth(raw);
            }
            half3 GlassRefraction(GlassVaryings input,float3 transmitted,float pathLength,float2 uv,out half3 background)
            {
                background=SampleSceneColor(uv);
                // A parallel thin pane displaces the ray at its exit, rather than bending
                // the entire scene as a water volume would. Use that exit's projection.
                float4 start=ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                float4 exit=ComputeScreenPos(TransformWorldToHClip(input.positionWS+transmitted*pathLength));
                float2 pixels=(exit.xy/exit.w-start.xy/start.w)*GetScaledScreenParams().xy*_RefractionStrength;
                pixels*=min(1,_MaxRefractionPixels/max(length(pixels),0.0001));
                float2 shiftedUV=uv+pixels/GetScaledScreenParams().xy;
                float2 edgePixels=min(shiftedUV,1-shiftedUV)*GetScaledScreenParams().xy;
                float onScreen=smoothstep(1,3,min(edgePixels.x,edgePixels.y));
                float sceneDepth=GlassEyeDepth(uv),shiftedDepth=GlassEyeDepth(shiftedUV);
                float paneDepth=-TransformWorldToView(input.positionWS).z;
                float behindPane=smoothstep(0.002,0.02,shiftedDepth-paneDepth);
                float sameSurface=1-smoothstep(_DepthRejection*.5,_DepthRejection,abs(shiftedDepth-sceneDepth));
                // Add only the opaque difference. Previously drawn rear glass remains
                // in the destination buffer, attenuated by the ordinary Fresnel alpha.
                return (SampleSceneColor(shiftedUV)-background)*(onScreen*behindPane*sameSurface);
            }
            half4 GlassFragment(GlassVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 normal=NormalizeNormalPerPixel(input.normalWS);
                half3 view=GetWorldSpaceNormalizeViewDir(input.positionWS);
                half f0=GlassF0(),fresnel=GlassFresnel(dot(normal,view),f0);
                float3 transmitted=refract(-view,normal,1.0/_IOR);
                float pathLength=GlassPathLength(input.positionOS,transmitted);
                half3 transmission=GlassTransmittance(pathLength);
                half scalarTransmission=min(transmission.r,min(transmission.g,transmission.b));
                half opacity=1-(1-fresnel)*scalarTransmission;
                float2 uv=GetNormalizedScreenSpaceUV(input.positionCS);
                half3 background;
                half3 displacement=GlassRefraction(input,transmitted,pathLength,uv,background);
                half3 reflected=reflect(-view,normal);
                half3 environment=GlossyEnvironmentReflection(reflected,input.positionWS,1-_Smoothness,1,uv);
                half3 color=GlassEnvironment(environment,input.positionWS,reflected)*fresnel;
                // Restore only the RGB channels removed by scalar absorption. A black
                // background receives no tint; the glass cannot emit a green base color.
                color+=(1-fresnel)*((transmission-scalarTransmission)*background+transmission*displacement);
                Light main=GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                BRDFData brdf;
                half unusedAlpha=1;
                InitializeBRDFDataDirect(half3(0,0,0),half3(0,0,0),half3(f0,f0,f0),f0,1-f0,_Smoothness,unusedAlpha,brdf);
                half highlight=DirectBRDFSpecular(brdf,normal,main.direction,view)*f0;
                color+=main.color*(_HighlightStrength*highlight*saturate(dot(normal,main.direction))*main.distanceAttenuation*main.shadowAttenuation);
                return GlassOutput(color,opacity,input.fog);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
