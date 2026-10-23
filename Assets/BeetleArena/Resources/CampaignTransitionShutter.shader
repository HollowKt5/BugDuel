Shader "UI/BugDuel/CampaignTransitionShutter"
{
    Properties
    {
        [HideInInspector] _MainTex("Sprite Texture",2D)="white" {}
        _Reveal("Reveal",Range(0,1))=0
        _Softness("Soft edge",Range(0,.15))=.025
        _Upper("Upper half",Float)=1
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            struct Attributes { float4 vertex:POSITION;float2 uv:TEXCOORD0;fixed4 color:COLOR; };
            struct Varyings { float4 position:SV_POSITION;float2 uv:TEXCOORD0;fixed4 color:COLOR; };
            float _Reveal,_Softness,_Upper;
            Varyings Vert(Attributes input)
            {
                Varyings output;output.position=UnityObjectToClipPos(input.vertex);output.uv=input.uv;output.color=input.color;return output;
            }
            fixed4 Frag(Varyings input):SV_Target
            {
                float distance=lerp(1-input.uv.y,input.uv.y,_Upper)*.5;
                float edge=lerp(-_Softness,.5+_Softness,_Reveal);
                float opacity=_Softness>0?smoothstep(edge-_Softness,edge+_Softness,distance):step(edge,distance);
                // Exact endpoints keep the center seam black and fully reveal the outermost pixels.
                if(_Reveal<=0)opacity=1;else if(_Reveal>=1)opacity=0;
                return fixed4(0,0,0,opacity*input.color.a);
            }
            ENDHLSL
        }
    }
}
