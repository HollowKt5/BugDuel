Shader "BeetleArena/HusbandryGlassLow"
{
    Properties
    {
        [MainColor] _TransmissionColor("Transmission Through One World Unit", Color) = (0.84,0.97,0.93,1)
        _AbsorptionDensity("Glass Absorption Density", Range(0,4)) = 1
        _IOR("Index Of Refraction", Range(1,1.8)) = 1.5
        _Smoothness("Reflection Smoothness", Range(0.5,0.98)) = 0.9
        _ReflectionStrength("Environment Reflection", Range(0,1)) = 0.16
        _HighlightStrength("Main Light Highlight", Range(0,2)) = 0.65
        [Header(Soft Window Reflection)]
        [HDR] _WindowColor("Window Light Color", Color) = (0.92,0.97,1,1)
        _WindowStrength("Soft Window Strength", Range(0,2)) = 0.22
        _WindowMinimumFill("Window Fill With Bright Environment", Range(0,1)) = 0.35
        _WindowPosition("Window Center (World Space)", Vector) = (-4,-4,-8,0)
        _WindowDirection("Window Plane Direction (World Space)", Vector) = (-0.5,-0.65,-0.57,0)
        _WindowWidth("Window Half Width (World Units)", Range(0.1,8)) = 1.5
        _WindowHeight("Window Half Height (World Units)", Range(0.1,8)) = 1.8
        _WindowSoftness("Window Soft Edge", Range(0.02,0.8)) = 0.32
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "GlassLowForward"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Back
            ZWrite Off
            ZTest LEqual
            Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex GlassVertex
            #pragma fragment GlassLowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "HusbandryGlassCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ImageBasedLighting.hlsl"
            half4 GlassLowFragment(GlassVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 normal=NormalizeNormalPerPixel(input.normalWS);
                half3 view=GetWorldSpaceNormalizeViewDir(input.positionWS);
                half f0=GlassF0(),fresnel=GlassFresnel(dot(normal,view),f0);
                float3 transmitted=refract(-view,normal,1.0/_IOR);
                half3 transmission=GlassTransmittance(GlassPathLength(input.positionOS,transmitted));
                half scalarTransmission=min(transmission.r,min(transmission.g,transmission.b));
                half opacity=1-(1-fresnel)*scalarTransmission;
                // One cubemap lookup, with no clustered probes or scene/depth/shadow textures.
                half3 reflected=reflect(-view,normal);
                half mip=PerceptualRoughnessToMipmapLevel(1-_Smoothness);
                half4 encoded=SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0,samplerunity_SpecCube0,reflected,mip);
                half3 environment=DecodeHDREnvironment(encoded,unity_SpecCube0_HDR);
                half3 color=GlassEnvironment(environment,input.positionWS,reflected)*fresnel;
                // Without scene color, absorption darkens transmission neutrally; the
                // slight green cut-edge tint affects reflected light only, never emission.
                color*=transmission/max(max(transmission.r,transmission.g),max(transmission.b,0.001h));
                half3 lightDirection=half3(_MainLightPosition.xyz);
                half3 halfway=SafeNormalize(lightDirection+view);
                half power=lerp(16,128,_Smoothness*_Smoothness);
                half highlight=pow(saturate(dot(normal,halfway)),power)*(power+8)/(8*PI);
                highlight*=GlassFresnel(dot(lightDirection,halfway),f0)*saturate(dot(normal,lightDirection));
                color+=_MainLightColor.rgb*(_HighlightStrength*highlight);
                return GlassOutput(color,opacity,input.fog);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
