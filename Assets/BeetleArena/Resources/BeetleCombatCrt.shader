Shader "BugDuel/Combat CRT"
{
    Properties
    {
        [PerRendererData] _MainTex ("World", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Tube ("Curvature, Vignette, RGB Pixels, Scan Strength", Vector) = (.025,.12,.85,.12)
        _Signal ("Scan Count, Static Grain, Flicker, Time", Vector) = (300,.002,0,0)
        _Phosphor ("RGB Phosphor Strength", Range(0,.2)) = .06
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "BugDuelScreenEffect"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            Name "CRT"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize, _Color, _ClipRect, _Tube, _Signal;
            float _Phosphor;
            struct Attributes { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct Varyings { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; float4 local:TEXCOORD1; };
            Varyings vert(Attributes i)
            {
                Varyings o; o.local=i.vertex; o.vertex=UnityObjectToClipPos(i.vertex);
                o.uv=i.uv; o.color=i.color*_Color; return o;
            }
            half4 frag(Varyings i):SV_Target
            {
                float2 centered=i.uv*2-1;
                float radius2=dot(centered,centered);
                float2 uv=(centered*(1+_Tube.x*radius2))*.5+.5;
                // Use output pixels, not source texels: campaign and standalone captures have different sizes.
                float2 pixelUv=max(fwidth(i.uv),float2(.000001,.000001));
                float edgeWeight=smoothstep(.1,1.4,radius2);
                float2 separation=float2(pixelUv.x*_Tube.z*lerp(.2,1,edgeWeight),0);
                half3 color;
                color.r=tex2D(_MainTex,uv+separation).r;
                color.g=tex2D(_MainTex,uv).g;
                color.b=tex2D(_MainTex,uv-separation).b;

                // The screen texture is fixed to the glass, independent of the scene and lens warp.
                // At least three output pixels per line; fading filters narrow views before aliasing.
                float row=i.uv.y*min(_Signal.x,1/(3*pixelUv.y));
                float stripeFade=1-smoothstep(.35,1,fwidth(row));
                float scan=.5+.5*cos(row*6.2831853);
                float scanStrength=_Tube.w*stripeFade;
                color*=(1-scanStrength*scan)/max(.5,1-.5*scanStrength);

                // Soft stationary RGB phosphor triads; zero-mean modulation preserves exposure.
                float triad=i.uv.x/(pixelUv.x*6);
                float3 phosphor=cos(triad*6.2831853+float3(0,2.0943951,4.1887902));
                color*=1+_Phosphor*phosphor;
                color*=1-_Tube.y*saturate(radius2*.48);

                // No animated snow and no additive haze in dark areas. Flicker is opt-in (default zero).
                float grain=frac(sin(dot(floor(i.vertex.xy),float2(12.9898,78.233)))*43758.5453)-.5;
                color*=1+grain*_Signal.y;
                color*=1+sin(_Signal.w*6.2831853)*_Signal.z;
                // The warped tube perimeter is opaque black; no background bleeds through.
                float edge=min(min(uv.x,uv.y),min(1-uv.x,1-uv.y));
                float border=smoothstep(0,max(fwidth(edge),.0001),edge);
                float alpha=i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                alpha*=UnityGet2DClipping(i.local.xy,_ClipRect);
                #endif
                return half4(max(0,color)*i.color.rgb*border,alpha);
            }
            ENDHLSL
        }
    }
}
