Shader "BeetleArena/HornSpark"
{
    Properties { _Tint("Tint", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            CBUFFER_START(UnityPerMaterial)
            half4 _Tint;
            CBUFFER_END
            Varyings vert(Attributes v)
            { Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz); o.uv=v.uv; o.color=v.color*_Tint; return o; }
            half4 frag(Varyings i):SV_Target
            {
                float2 d=abs(i.uv*2-1);
                half a=pow(saturate(1-d.x),1.4)*pow(saturate(1-d.y),.6);
                return half4(i.color.rgb*1.5, i.color.a*a);
            }
            ENDHLSL
        }
    }
}
