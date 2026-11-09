Shader "BeetleArena/HusbandryOutdoorGround"
{
    Properties
    {
        _MainTex("Ground Path (Anime Cel)", 2D) = "white" {}
        _Hour("Hour", Range(0, 24)) = 12
        _Daylight("Daylight", Range(0, 1)) = 1
        _Color("Base Tint", Color) = (1, 1, 1, 1)
        _OutdoorFog("Morning mist", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Cull Off ZWrite On
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Hour, _Daylight, _OutdoorFog;
                float4 _Color;
                float4 _MainTex_ST;
            CBUFFER_END

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = v.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                
                // Day/night and dusk solar tint matching HusbandryWindowSky
                float solar = sin((_Hour - 6.0) / 12.0 * PI);
                float dusk = (1.0 - smoothstep(0.05, 0.65, solar)) * _Daylight;
                
                // Distant treeline transition: gently blend far edge into mountain forest base
                float forestFade = smoothstep(0.88, 1.0, input.uv.y);
                float3 forestShadow = float3(0.098, 0.215, 0.149);
                tex.rgb = lerp(tex.rgb, forestShadow, forestFade * 0.75);
                
                float3 dayGround = tex.rgb * lerp(float3(0.95, 0.98, 1.0), float3(1.0, 0.70, 0.45), dusk * 0.6);
                float3 nightGround = tex.rgb * float3(0.09, 0.14, 0.25);
                float3 color = lerp(nightGround, dayGround, _Daylight);

                // Valley morning mist matching window sky
                if (_OutdoorFog > 0.001)
                {
                    float3 fogColor = lerp(float3(0.025, 0.039, 0.075), lerp(float3(0.7, 0.79, 0.83), float3(0.9, 0.69, 0.53), dusk * 0.45), _Daylight);
                    color = lerp(color, fogColor, saturate(_OutdoorFog * 0.4));
                }

                return half4(color * _Color.rgb, 1.0);
            }
            ENDHLSL
        }
    }
}
