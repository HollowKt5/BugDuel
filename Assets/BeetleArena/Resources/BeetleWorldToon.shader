Shader "Hidden/BugDuel/World Toon Layer"
{
    Properties
    {
        _MainTex ("World", 2D) = "white" {}
        _Bands ("Bands / Softness / Strength", Vector) = (5,0.16,0.65,0)
        _Outline ("Strength / Width / Threshold", Vector) = (0.25,1,0.18,0)
        _Ink ("Ink", Color) = (0.055,0.065,0.05,1)
        _Tone ("Shadow Lift / Detail Retention", Vector) = (0.08,0.65,0,0)
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        Pass
        {
            Name "WORLD_TOON_LAYER"
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize, _Bands, _Outline, _Ink, _Tone;

            float3 Perceptual(float3 color)
            {
                #if defined(UNITY_COLORSPACE_GAMMA)
                    return color;
                #else
                    return lerp(1.055*pow(max(color,0),1.0/2.4)-.055,color*12.92,step(color,.0031308));
                #endif
            }

            float Luma(float3 color) { return dot(color,float3(.2126,.7152,.0722)); }

            float4 frag(v2f_img input) : SV_Target
            {
                float4 source=tex2D(_MainTex,input.uv);
                float3 color=Perceptual(source.rgb);
                float luminance=Luma(color);
                float2 offset=abs(_MainTex_TexelSize.xy)*_Outline.y;
                float3 left=Perceptual(tex2D(_MainTex,input.uv-float2(offset.x,0)).rgb);
                float3 right=Perceptual(tex2D(_MainTex,input.uv+float2(offset.x,0)).rgb);
                float3 down=Perceptual(tex2D(_MainTex,input.uv-float2(0,offset.y)).rgb);
                float3 up=Perceptual(tex2D(_MainTex,input.uv+float2(0,offset.y)).rgb);
                float broad=Luma((left+right+down+up+color*4)*.125);
                float steps=max(2,_Bands.x-1);
                float scaled=saturate(broad)*steps;
                float softness=max(_Bands.y,min(.45,fwidth(scaled)));
                float level=(floor(scaled)+smoothstep(.5-softness,.5+softness,frac(scaled)))/steps;
                // Bound darkening; the original material already contains light and shadow.
                float shadeMask=smoothstep(.035,.2,broad)*(1-smoothstep(.65,1,broad));
                float delta=clamp(level-broad,-broad*.06,min(broad*.08,(1-broad)*.08))*shadeMask;
                float detail=(luminance-broad)*(1-_Tone.y)*.2*shadeMask;
                float lift=_Tone.x*broad*(1-smoothstep(.12,.55,broad));
                float target=clamp(luminance+_Bands.z*(delta+lift-detail),luminance*.94,luminance*1.12);
                color*=target/max(luminance,.0001);
                float gradient=(abs(Luma(right)-Luma(left))+abs(Luma(up)-Luma(down)))*.5;
                float edge=smoothstep(_Outline.z,_Outline.z*2,gradient)*_Outline.x*_Bands.z;
                edge*=smoothstep(.06,.22,broad)*(1-smoothstep(.78,1,luminance));
                edge=min(edge,.18);
                color=lerp(color,min(color,Perceptual(_Ink.rgb)),edge);
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    color=lerp(pow(max((color+.055)/1.055,0),2.4),color/12.92,step(color,.04045));
                #endif
                return float4(color,source.a);
            }
            ENDHLSL
        }
    }
}
