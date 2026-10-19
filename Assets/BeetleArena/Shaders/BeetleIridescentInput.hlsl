#ifndef BEETLE_IRIDESCENT_INPUT_INCLUDED
#define BEETLE_IRIDESCENT_INPUT_INCLUDED
#define _CLEARCOAT 1
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// One layout for all passes; UI and combat materials do not use this shader.
CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor, _OutlineColor;
    float _Metallic, _Smoothness, _CoatStrength, _CoatSmoothness;
    float _Iridescence, _RampOffset, _AngleShift, _AnglePower, _RampRow;
    float _FilmVariation, _FilmScale, _Saturation, _DetailStrength, _OutlineWidth;
    float _HueShift, _Brightness;
CBUFFER_END
TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
TEXTURE2D(_IridescenceRamp); SAMPLER(sampler_IridescenceRamp);

struct ShellAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct ShellVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    half3 normalWS : TEXCOORD1;
    float2 uv : TEXCOORD2;
    float3 positionOS : TEXCOORD3;
    half fog : TEXCOORD4;
    half3 vertexLighting : TEXCOORD5;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};
ShellVaryings ShellVertex(ShellAttributes input)
{
    ShellVaryings output = (ShellVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
    output.positionCS = p.positionCS;
    output.positionWS = p.positionWS;
    output.positionOS = input.positionOS.xyz;
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
    output.fog = ComputeFogFactor(p.positionCS.z);
    output.vertexLighting = VertexLighting(p.positionWS, output.normalWS);
    return output;
}

half3 ShellReflectance(ShellVaryings input, half3 normal, half3 view)
{
    float grazing = pow(saturate(1 - dot(normal, view)), max(_AnglePower, 0.05));
    // Object-space variation stays on the shell when the animal moves. No time-driven rainbow scrolling.
    float3 p = input.positionOS * _FilmScale;
    float variation = sin(p.x * 2.3 + p.z * 1.7) * sin(p.y * 2.1 + p.z * 0.8);
    float u = saturate(_RampOffset + grazing * _AngleShift + variation * _FilmVariation);
    // The supplied JPEG has dark margins: sample only its central colored strip.
    half3 ramp = SAMPLE_TEXTURE2D(_IridescenceRamp, sampler_IridescenceRamp, float2(u, _RampRow)).rgb;
    half luminance = dot(ramp, half3(0.2126, 0.7152, 0.0722));
    ramp = saturate(lerp(luminance.xxx, ramp, _Saturation));
    // Rotate the whole palette without losing angle-dependent color differences.
    float3 hsv = RgbToHsv(ramp);
    hsv.x = frac(hsv.x + _HueShift / 360.0);
    ramp = saturate(HsvToRgb(hsv) * _Brightness);
    half detail = dot(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb, half3(0.2126, 0.7152, 0.0722));
    return saturate(_BaseColor.rgb * lerp(half3(1,1,1), ramp, _Iridescence) * lerp(1, detail, _DetailStrength));
}
#endif
