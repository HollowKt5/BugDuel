Shader "BeetleArena/HusbandrySpotlightPool"
{
    Properties
    {
        [HDR] _Color("Pool Color", Color) = (1, 0.9, 0.68, 1)
        _Intensity("Pool Brightness", Range(0, 0.1)) = 0.024
        [HideInInspector] _InnerRadius("Inner Radius", Float) = 0.7
        [HideInInspector] _Fade("Fade", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-20" "RenderType"="Transparent" }
        Pass
        {
            Name "GroundLight"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Intensity, _InnerRadius, _Fade;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv * 2 - 1;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float radius = length(input.uv);
                half pool = 1 - smoothstep(_InnerRadius, 1, radius);
                // A fixed ground projection, depth-tested below the soil and insect.
                return half4(_Color.rgb * (_Intensity * pool * _Fade), 0);
            }
            ENDHLSL
        }
    }
}
