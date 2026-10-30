Shader "BeetleArena/HusbandryWeather"
{
    Properties
    {
        [MainTexture] _BaseMap("Particle detail",2D)="white"{}
        [MainColor] _BaseColor("Tint",Color)=(1,1,1,1)
        _Daylight("Daylight",Range(0,1))=1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;half4 _BaseColor;float _Daylight;
            CBUFFER_END
            TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
            struct Attributes {float4 positionOS:POSITION;half4 color:COLOR;float2 uv:TEXCOORD0;};
            struct Varyings {float4 positionCS:SV_POSITION;half4 color:COLOR;float2 uv:TEXCOORD0;};
            Varyings Vert(Attributes input)
            {
                Varyings o;o.positionCS=TransformObjectToHClip(input.positionOS.xyz);o.color=input.color*_BaseColor;
                o.uv=TRANSFORM_TEX(input.uv,_BaseMap);return o;
            }
            half4 Frag(Varyings input):SV_Target
            {
                half4 color=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv)*input.color;
                color.rgb*=lerp(half3(.22,.29,.43),half3(1,1,1),saturate(_Daylight));
                return color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
