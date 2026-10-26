#ifndef BUGDUEL_DITHER_CORE_INCLUDED
#define BUGDUEL_DITHER_CORE_INCLUDED
#include "UnityCG.cginc"
#include "UnityUI.cginc"

sampler2D _MainTex;
float4 _Color, _ClipRect;
float _PixelSize, _DitherStrength, _ColorLevels;
struct Attributes { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
struct Varyings { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; float4 local:TEXCOORD1; };
Varyings vert(Attributes i)
{
    Varyings o; o.local=i.vertex; o.vertex=UnityObjectToClipPos(i.vertex);
    o.uv=i.uv; o.color=i.color*_Color; return o;
}
float Bayer2(float2 p) { return p.x < .5 ? (p.y < .5 ? 0 : 3) : (p.y < .5 ? 2 : 1); }
float Bayer4(float2 cell)
{
    // [0,8,2,10; 12,4,14,6; 3,11,1,9; 15,7,13,5] / 16.
    // The pattern stays locked to the pixel grid, never animated noise.
    return (4*Bayer2(fmod(cell,2))+Bayer2(fmod(floor(cell*.5),2))+.5)/16;
}
half4 frag(Varyings i):SV_Target
{
    float2 screenPixel=max(fwidth(i.uv),float2(.000001,.000001));
    float2 cellUv=screenPixel*max(1,_PixelSize);
    float2 cell=floor(i.uv/cellUv);
    // Snapped UV derivatives cross cell boundaries. An explicit base level keeps
    // anisotropic filtering from sampling different footprints inside one pixel cell.
    float3 source=tex2Dlod(_MainTex,float4((cell+.5)*cellUv,0,0)).rgb;
    #ifndef UNITY_COLORSPACE_GAMMA
    source=LinearToGammaSpace(source);
    #endif
    // Each original RGB channel keeps its own value; adjacent quantized levels
    // are distributed by Bayer coverage, without a replacement palette or contrast curve.
    float steps=clamp(round(_ColorLevels),2,32)-1;
    float3 scaled=saturate(source)*steps;
    float3 color=(floor(scaled)+step(Bayer4(cell),frac(scaled)))/steps;
    color=lerp(source,color,saturate(_DitherStrength));
    #ifndef UNITY_COLORSPACE_GAMMA
    color=GammaToLinearSpace(color);
    #endif
    float alpha=i.color.a;
    #ifdef UNITY_UI_CLIP_RECT
    alpha*=UnityGet2DClipping(i.local.xy,_ClipRect);
    #endif
    return half4(color*i.color.rgb,alpha);
}
#endif
