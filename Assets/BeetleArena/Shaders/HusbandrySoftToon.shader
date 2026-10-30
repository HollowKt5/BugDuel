Shader "BeetleArena/HusbandrySoftToon"
{
    Properties
    {
        [MainTexture] _BaseMap("Painted albedo",2D)="white"{}
        [MainColor] _BaseColor("Color",Color)=(.7,.7,.7,1)
        _Stylization("Soft light bands",Range(0,1))=.35
        _BandSoftness("Band softness",Range(.05,.3))=.18
        _AmbientStrength("Ambient fill",Range(0,2))=1
        _Smoothness("Highlight focus",Range(0,1))=.2
        _SpecStrength("Restrained highlight",Range(0,.5))=.04
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Culling",Float)=2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            float _Stylization,_BandSoftness,_AmbientStrength,_Smoothness,_SpecStrength,_Cull;
        CBUFFER_END
        TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
        struct SoftAttributes
        {
            float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct SoftVaryings
        {
            float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;
            half3 normalWS:TEXCOORD1;float2 uv:TEXCOORD2;half fog:TEXCOORD3;
            half3 vertexLighting:TEXCOORD4;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
        SoftVaryings SoftVertex(SoftAttributes input)
        {
            SoftVaryings output=(SoftVaryings)0;
            UNITY_SETUP_INSTANCE_ID(input);UNITY_TRANSFER_INSTANCE_ID(input,output);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            VertexPositionInputs p=GetVertexPositionInputs(input.positionOS.xyz);
            output.positionCS=p.positionCS;output.positionWS=p.positionWS;
            output.normalWS=TransformObjectToWorldNormal(input.normalOS);
            output.uv=TRANSFORM_TEX(input.uv,_BaseMap);output.fog=ComputeFogFactor(p.positionCS.z);
            output.vertexLighting=VertexLighting(p.positionWS,output.normalWS);return output;
        }
        ENDHLSL
        Pass
        {
            Name "SoftForward"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull [_Cull] ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SoftVertex
            #pragma fragment SoftFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            half3 SoftLight(Light light,half3 normal,half3 view,half3 albedo)
            {
                half diffuse=saturate(dot(normal,light.direction));
                half edge=max(_BandSoftness,fwidth(diffuse));
                half bands=.45*smoothstep(.24-edge,.24+edge,diffuse)+.55*smoothstep(.68-edge,.68+edge,diffuse);
                // Blend only the light response; texture detail and the real day/night light energy remain intact.
                half response=lerp(diffuse,bands,_Stylization);
                half3 energy=light.color*light.distanceAttenuation*light.shadowAttenuation;
                half specular=pow(saturate(dot(normal,SafeNormalize(view+light.direction))),lerp(12,80,_Smoothness));
                specular=lerp(specular,smoothstep(.22,.62,specular),_Stylization)*_SpecStrength*smoothstep(0,.15,diffuse);
                return (albedo*response+specular)*energy;
            }
            half4 SoftFragment(SoftVaryings input,FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 normal=NormalizeNormalPerPixel(input.normalWS)*IS_FRONT_VFACE(face,1,-1);
                half3 view=GetWorldSpaceNormalizeViewDir(input.positionWS);
                half3 albedo=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv).rgb*_BaseColor.rgb;
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord=ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                    float4 shadowCoord=TransformWorldToShadowCoord(input.positionWS);
                #endif
                half3 color=albedo*max(0,SampleSH(normal))*_AmbientStrength;
                color+=SoftLight(GetMainLight(shadowCoord,input.positionWS,half4(1,1,1,1)),normal,view,albedo);
                #if defined(_ADDITIONAL_LIGHTS)
                    InputData inputData=(InputData)0;inputData.positionWS=input.positionWS;
                    inputData.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(input.positionCS);
                    #if USE_CLUSTER_LIGHT_LOOP
                        UNITY_LOOP for(uint lightIndex=0;lightIndex<min(URP_FP_DIRECTIONAL_LIGHTS_COUNT,MAX_VISIBLE_LIGHTS);lightIndex++)
                        {
                            CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
                            color+=SoftLight(GetAdditionalLight(lightIndex,input.positionWS,half4(1,1,1,1)),normal,view,albedo);
                        }
                    #endif
                    uint lightCount=GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(lightCount)
                        color+=SoftLight(GetAdditionalLight(lightIndex,input.positionWS,half4(1,1,1,1)),normal,view,albedo);
                    LIGHT_LOOP_END
                #elif defined(_ADDITIONAL_LIGHTS_VERTEX)
                    color+=albedo*input.vertexLighting;
                #endif
                return half4(MixFog(color,input.fog),1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            Cull [_Cull] ZWrite On ZTest LEqual ColorMask 0
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
            Cull [_Cull] ZWrite On ColorMask R
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
            Cull [_Cull] ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SoftVertex
            #pragma fragment SoftNormals
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 SoftNormals(SoftVaryings input,FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 normal=NormalizeNormalPerPixel(input.normalWS)*IS_FRONT_VFACE(face,1,-1);
                #if defined(_GBUFFER_NORMALS_OCT)
                    return half4(PackFloat2To888(saturate(PackNormalOctQuadEncode(normal)*.5+.5)),0);
                #else
                    return half4(normal,0);
                #endif
            }
            ENDHLSL
        }
    }
    Fallback Off
}
