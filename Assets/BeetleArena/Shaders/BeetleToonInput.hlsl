#ifndef BEETLE_TOON_INPUT_INCLUDED
#define BEETLE_TOON_INPUT_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// 所有通道共用完全相同的常量缓冲布局，保持 SRP Batcher 兼容。
// _BaseColor 沿用现有程序动画的 MaterialPropertyBlock 接口，可给两只甲虫分别上色。
CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor, _ShadowColor, _MidColor, _LightColor;
    half4 _SpecColor, _RimColor, _OutlineColor;
    float _ShadowThreshold, _LightThreshold, _BandSoftness, _ShadowStrength, _AmbientStrength;
    float _SpecStrength, _SpecPower, _SpecThreshold, _SpecSoftness;
    float _RimStrength, _RimThreshold, _OutlineWidth;
CBUFFER_END
TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);
struct ToonAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct ToonVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    half3 normalWS : TEXCOORD1;
    float2 uv : TEXCOORD2;
    half fog : TEXCOORD3;
    half3 vertexLighting : TEXCOORD4;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};
ToonVaryings ToonVertex(ToonAttributes input)
{
    ToonVaryings output = (ToonVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
    output.positionCS = positions.positionCS;
    output.positionWS = positions.positionWS;
    // 正确变换非均匀缩放下的法线，适应当前扁椭球鞘翅和细长胶囊体腿部。
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
    output.fog = ComputeFogFactor(positions.positionCS.z);
    output.vertexLighting = VertexLighting(output.positionWS, output.normalWS);
    return output;
}
#endif
