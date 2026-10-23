#ifndef BEETLE_HUSBANDRY_GLASS_INCLUDED
#define BEETLE_HUSBANDRY_GLASS_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
CBUFFER_START(UnityPerMaterial)
    half4 _TransmissionColor;
    half4 _WindowColor;
    float4 _WindowPosition;
    float4 _WindowDirection;
    half _AbsorptionDensity, _IOR, _Smoothness, _ReflectionStrength, _HighlightStrength;
    half _WindowStrength, _WindowMinimumFill;
    float _WindowWidth, _WindowHeight, _WindowSoftness;
    #if defined(HUSBANDRY_GLASS_HIGH)
        float _RefractionStrength, _MaxRefractionPixels, _DepthRejection;
    #endif
CBUFFER_END
struct GlassAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct GlassVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    half3 normalWS : TEXCOORD1;
    float3 positionOS : TEXCOORD2;
    half fog : TEXCOORD3;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};
GlassVaryings GlassVertex(GlassAttributes input)
{
    GlassVaryings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input,output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    VertexPositionInputs position=GetVertexPositionInputs(input.positionOS.xyz);
    output.positionCS=position.positionCS;
    output.positionWS=position.positionWS;
    output.positionOS=input.positionOS.xyz;
    output.normalWS=TransformObjectToWorldNormal(input.normalOS);
    output.fog=ComputeFogFactor(position.positionCS.z);
    return output;
}
half GlassF0()
{
    half ratio=(_IOR-1)/(_IOR+1);
    return ratio*ratio;
}
half GlassFresnel(half cosine,half f0)
{
    half grazing=1-saturate(cosine),square=grazing*grazing;
    return f0+(1-f0)*square*square*grazing;
}
// Unity unit Cube scaled into a thin pane. The unnormalized transformed ray keeps
// distance in world units; a cut edge has a longer absorbing path than the face.
float GlassPathLength(float3 positionOS,float3 transmittedWS)
{
    float3 directionOS=mul((float3x3)GetWorldToObjectMatrix(),transmittedWS);
    float3 toExit=(0.5-sign(directionOS)*positionOS)/max(abs(directionOS),0.00001);
    return max(0,min(toExit.x,min(toExit.y,toExit.z)));
}
half3 GlassTransmittance(float pathLength)
{
    return pow(clamp(_TransmissionColor.rgb,0.001h,1),pathLength*_AbsorptionDensity);
}
half GlassWindow(float3 positionWS,float3 reflected)
{
    float3 direction=SafeNormalize(_WindowDirection.xyz);
    float3 axis=abs(direction.y)<0.98 ? float3(0,1,0) : float3(0,0,1);
    float3 right=normalize(cross(axis,direction)),up=cross(direction,right);
    float facing=dot(reflected,direction);
    // Intersect a finite virtual window. Position matters even when an orthographic
    // camera gives every point on a pane the same reflection direction.
    float distance=dot(_WindowPosition.xyz-positionWS,direction)/max(facing,0.001);
    float3 offset=positionWS+reflected*distance-_WindowPosition.xyz;
    float2 uv=float2(dot(offset,right),dot(offset,up));
    uv/=float2(_WindowWidth,_WindowHeight);
    float radius=max(abs(uv.x),abs(uv.y)),softness=max(_WindowSoftness,fwidth(radius));
    half window=(1-smoothstep(1-softness,1+softness,radius))*step(0.001,facing)*step(0,distance);
    return window*lerp(0.72,1,smoothstep(0.025,0.07,abs(uv.x)));
}
half3 GlassEnvironment(half3 environment,float3 positionWS,float3 reflected)
{
    half brightness=dot(environment,half3(0.2126,0.7152,0.0722));
    half fill=lerp(1,_WindowMinimumFill,smoothstep(0.03h,0.25h,brightness));
    return environment*_ReflectionStrength+_WindowColor.rgb*(_WindowStrength*fill*GlassWindow(positionWS,reflected));
}
half4 GlassOutput(half3 color,half opacity,half fog)
{
    return half4(MixFogColor(color,unity_FogColor.rgb*opacity,fog),opacity);
}
#endif
