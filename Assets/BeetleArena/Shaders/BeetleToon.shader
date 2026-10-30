Shader "BeetleArena/Toon"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (0.7,0.3,0.1,1)
        [Header(Cel Shading)]
        _ShadowColor("Shadow Tint", Color) = (0.38,0.40,0.55,1)
        _MidColor("Midtone Tint", Color) = (0.73,0.79,0.87,1)
        _LightColor("Lit Tint", Color) = (1,0.96,0.86,1)
        _ShadowThreshold("Shadow Band", Range(0,1)) = 0.28
        _LightThreshold("Lit Band", Range(0,1)) = 0.68
        _BandSoftness("Band Edge Softness", Range(0.001,0.25)) = 0.035
        _ShadowStrength("Received Shadow Strength", Range(0,1)) = 0.9
        _AmbientStrength("Ambient Fill", Range(0,1)) = 0.3
        [Header(Colored Highlight)]
        [HDR] _SpecColor("Highlight Color", Color) = (1,0.8,0.45,1)
        _SpecStrength("Highlight Strength", Range(0,3)) = 0.65
        _SpecPower("Highlight Focus", Range(4,128)) = 42
        _SpecThreshold("Highlight Cutoff", Range(0.02,0.95)) = 0.3
        _SpecSoftness("Highlight Edge Softness", Range(0.001,0.3)) = 0.04
        [Header(Rim Light)]
        [HDR] _RimColor("Rim Color", Color) = (0.38,0.65,0.65,1)
        _RimStrength("Rim Strength", Range(0,2)) = 0.18
        _RimThreshold("Rim Cutoff", Range(0,1)) = 0.74
        [Header(Outline)]
        _OutlineColor("Outline Color", Color) = (0.025,0.035,0.04,1)
        _OutlineWidth("Outline Width (Pixels)", Range(0,5)) = 1.6
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "BeetleToonInput.hlsl"
        ENDHLSL

        // 外描边：反向外壳只画背面，沿投影法线扩张；宽度按像素计，不改变碰撞体。
        // URP 的 SRPDefaultUnlit 额外通道搭配 UniversalForwardOnly 表面通道。
        // https://docs.unity3d.com/6000.0/Documentation/Manual/urp/urp-shaders/urp-shaderlab-pass-tags.html
        Pass
        {
            Name "ToonOutline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front ZWrite Off ZTest LEqual
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex OutlineVertex
            #pragma fragment OutlineFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            ToonVaryings OutlineVertex(ToonAttributes input)
            {
                ToonVaryings output = ToonVertex(input);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float2 projected = mul((float3x3)UNITY_MATRIX_VP, normalWS).xy;
                // 在像素空间归一化，避免宽高比导致横向/纵向线宽不同。
                float2 pixelDirection = projected * _ScaledScreenParams.xy;
                pixelDirection *= rsqrt(max(dot(pixelDirection, pixelDirection), 1e-8));
                output.positionCS.xy += pixelDirection * (2.0 * _OutlineWidth / _ScaledScreenParams.xy) * output.positionCS.w;
                return output;
            }
            half4 OutlineFragment(ToonVaryings input) : SV_Target
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
            Name "ToonForward"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Back ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ToonVertex
            #pragma fragment ToonFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            // 用少量附加光补充轮廓，不把多盏光的三档阴影反复相加，保留主光的层次。
            half3 ExtraLight(Light light, half3 normal, half3 albedo)
            {
                half nDotL = saturate(dot(normal, light.direction));
                return albedo * light.color * light.distanceAttenuation * light.shadowAttenuation
                    * smoothstep(0.15, 0.35, nDotL) * 0.16;
            }
            half4 ToonFragment(ToonVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 normal = normalize(input.normalWS);
                half3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                Light main = GetMainLight(shadowCoord, input.positionWS, half4(1,1,1,1));
                half shadow = lerp(1.0h, main.shadowAttenuation, _ShadowStrength);
                half nDotL = saturate(dot(normal, main.direction));
                half illumination = nDotL * shadow;
                // fwidth 给分层边缘最小抗锯齿宽度，减轻远距离阶梯边界闪烁。
                half edge = max(_BandSoftness, fwidth(illumination));
                half middle = smoothstep(_ShadowThreshold-edge, _ShadowThreshold+edge, illumination);
                half bright = smoothstep(_LightThreshold-edge, _LightThreshold+edge, illumination);
                half3 tint = lerp(_ShadowColor.rgb, _MidColor.rgb, middle);
                tint = lerp(tint, _LightColor.rgb, bright);
                half3 color = albedo * tint * (0.3h + min(main.color * main.distanceAttenuation, 1.4h) * 0.7h);
                color += albedo * max(SampleSH(normal), 0) * _AmbientStrength;

                // 半程向量高光经阈值化形成色块；受光面和实时阴影都参与遮罩，阴影里不会无故闪亮。
                half3 halfway = SafeNormalize(main.direction + view);
                half specular = pow(saturate(dot(normal, halfway)), _SpecPower);
                half specEdge = max(_SpecSoftness, fwidth(specular));
                half specMask = smoothstep(_SpecThreshold-specEdge, _SpecThreshold+specEdge, specular);
                color += _SpecColor.rgb * _SpecStrength * specMask * shadow * smoothstep(0, 0.15, nDotL);
                half rim = 1 - saturate(dot(normal, view));
                // 阈值拉到最大也保留非零区间，避免 smoothstep(1,1,x) 的除零结果。
                half rimStart = min(_RimThreshold, 0.999h);
                color += _RimColor.rgb * _RimStrength * smoothstep(rimStart, min(1, rimStart+0.12), rim) * (0.3+0.7*illumination);

                // 与项目 URP 17.3 的 Forward / Forward+ 灯光遍历约定一致。
                #if defined(_ADDITIONAL_LIGHTS)
                    InputData inputData = (InputData)0;
                    inputData.positionWS = input.positionWS;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                    #if USE_CLUSTER_LIGHT_LOOP
                        UNITY_LOOP for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                        {
                            CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
                            color += ExtraLight(GetAdditionalLight(lightIndex, input.positionWS, half4(1,1,1,1)), normal, albedo);
                        }
                    #endif
                    uint lightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(lightCount)
                        color += ExtraLight(GetAdditionalLight(lightIndex, input.positionWS, half4(1,1,1,1)), normal, albedo);
                    LIGHT_LOOP_END
                #elif defined(_ADDITIONAL_LIGHTS_VERTEX)
                    color += albedo * input.vertexLighting * 0.16;
                #endif
                return half4(MixFog(color, input.fog), 1);
            }
            ENDHLSL
        }
        // 复用 URP 的投影偏移/近裁剪逻辑，投影使用原模型，不让扩张描边放大阴影。
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
            #pragma vertex ToonVertex
            #pragma fragment ToonDepthNormals
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 ToonDepthNormals(ToonVaryings input) : SV_Target
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
