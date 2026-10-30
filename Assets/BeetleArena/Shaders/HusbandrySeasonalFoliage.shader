Shader "BeetleArena/HusbandrySeasonalFoliage"
{
    Properties
    {
        [MainTexture] _BaseMap("Painted leaf veins",2D)="white"{}
        _SeasonTint("Season leaf color",Color)=(.48,.7,.27,1)
        _LeafCoverage("Leaf coverage",Range(0,1))=1
        _SnowAmount("Snow",Range(0,1))=0
        _WindStrength("Breeze",Range(0,.5))=.12
        _WindTime("Breeze time",Float)=0
        _Daylight("Daylight",Range(0,1))=1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _SeasonTint;
            float _LeafCoverage,_SnowAmount,_WindStrength,_WindTime,_Daylight;
        CBUFFER_END
        TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
        struct Attributes { float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;float2 seed:TEXCOORD1; };
        struct Varyings
        {
            float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;
            half3 normalWS:TEXCOORD1;float2 uv:TEXCOORD2;float seed:TEXCOORD3;half fog:TEXCOORD4;
        };
        float3 LeafPosition(Attributes input)
        {
            float3 p=TransformObjectToWorld(input.positionOS.xyz);
            float sway=sin(_WindTime*.73+p.y*.24)+.32*sin(_WindTime*1.51+p.x*.67);
            float flutter=sin(_WindTime*3.7+input.seed.x*41)*input.uv.y;
            p.x+=(sway*.65+flutter*.2)*_WindStrength;
            p.y+=flutter*_WindStrength*.19;
            p.z+=sway*_WindStrength*.14;
            return p;
        }
        Varyings Vert(Attributes input)
        {
            Varyings o;
            o.positionWS=LeafPosition(input);o.positionCS=TransformWorldToHClip(o.positionWS);
            o.normalWS=TransformObjectToWorldNormal(input.normalOS);
            o.uv=TRANSFORM_TEX(input.uv,_BaseMap);o.seed=input.seed.x;
            o.fog=ComputeFogFactor(o.positionCS.z);return o;
        }
        void Coverage(Varyings input){clip(_LeafCoverage-max(.0001,input.seed));}
        ENDHLSL
        Pass
        {
            Name "FoliageForward" Tags { "LightMode"="UniversalForwardOnly" }
            Cull Off ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            half4 Frag(Varyings input,FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC):SV_Target
            {
                Coverage(input);
                half3 normal=NormalizeNormalPerPixel(input.normalWS)*IS_FRONT_VFACE(face,1,-1);
                half3 painted=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv).rgb;
                half3 albedo=painted*_SeasonTint.rgb*lerp(.84,1.15,input.seed);
                half snow=_SnowAmount*saturate(normal.y)*smoothstep(.25,.65,input.uv.y);
                albedo=lerp(albedo,half3(.84,.91,.94),snow);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadow=ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                    float4 shadow=TransformWorldToShadowCoord(input.positionWS);
                #endif
                Light sun=GetMainLight(shadow);
                half n=dot(normal,sun.direction);
                half softLight=lerp(.34,1,smoothstep(-.35,.85,n));
                half3 ambient=max(SampleSH(normal),lerp(half3(.055,.075,.12),half3(.22,.27,.2),_Daylight));
                half3 color=albedo*(ambient+sun.color*softLight*lerp(.48,1,sun.shadowAttenuation)*.8);
                // Gentle transmitted light keeps the thin painted leaves luminous without hard toon bands.
                color+=albedo*sun.color*pow(saturate(-n),2)*.12*_Daylight;
                return half4(MixFog(color,input.fog),1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster" Tags { "LightMode"="ShadowCaster" }
            Cull Off ZWrite On ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment DepthFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection,_LightPosition;
            Varyings ShadowVert(Attributes input)
            {
                Varyings o=Vert(input);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 direction=normalize(_LightPosition-o.positionWS);
                #else
                    float3 direction=_LightDirection;
                #endif
                o.positionCS=ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(o.positionWS,o.normalWS,direction)));
                return o;
            }
            half4 DepthFrag(Varyings input):SV_Target {Coverage(input);return 0;}
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly" Tags { "LightMode"="DepthOnly" }
            Cull Off ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment DepthFrag
            half4 DepthFrag(Varyings input):SV_Target {Coverage(input);return input.positionCS.z;}
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals" Tags { "LightMode"="DepthNormalsOnly" }
            Cull Off ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment NormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 NormalsFrag(Varyings input,FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC):SV_Target
            {
                Coverage(input);
                float3 normal=NormalizeNormalPerPixel(input.normalWS)*IS_FRONT_VFACE(face,1,-1);
                #if defined(_GBUFFER_NORMALS_OCT)
                    return half4(PackFloat2To888(saturate(PackNormalOctQuadEncode(normal)*.5+.5)),0);
                #else
                    return half4(normal,0);
                #endif
            }
            ENDHLSL
        }
    }
    Fallback Off
}
