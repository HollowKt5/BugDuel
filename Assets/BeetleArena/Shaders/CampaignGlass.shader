Shader "BugDuel/CampaignGlass"
{
    Properties {
        [PerRendererData] _MainTex ("Sprite",2D)="white"{}
        _Color("Tint",Color)=(1,1,1,1)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode] Blend SrcAlpha OneMinusSrcAlpha ColorMask [_ColorMask]
        Pass {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            sampler2D _MainTex, _CampaignBlurred; float4 _Color, _ClipRect;
            struct a { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct v { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; float4 screen:TEXCOORD1; float4 color:COLOR; float4 world:TEXCOORD2; };
            v vert(a i) { v o; o.world=i.vertex; o.vertex=UnityObjectToClipPos(i.vertex); o.screen=ComputeScreenPos(o.vertex); o.uv=i.uv; o.color=i.color*_Color; return o; }
            fixed4 frag(v i):SV_Target {
                float mask=tex2D(_MainTex,i.uv).a;
                float3 bg=tex2D(_CampaignBlurred,i.screen.xy/i.screen.w).rgb;
                float3 rgb=lerp(bg,i.color.rgb,.78);
                float border=saturate(1-abs(mask-.5)*2)*.18;
                float alpha=mask*i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                alpha*=UnityGet2DClipping(i.world.xy,_ClipRect);
                #endif
                return float4(rgb+border,alpha);
            }
            ENDHLSL
        }
    }
}
