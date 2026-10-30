Shader "BeetleArena/HusbandryWindowSky"
{
    Properties
    {
        _Hour("Hour",Range(0,24))=12 _Daylight("Daylight",Range(0,1))=1
        _GardenTex("Garden vista",2D)="black"{}
        [Toggle] _GardenSkyMatte("Composite neutral sky backing",Float)=0
        [Toggle] _GardenEnabled("Painted garden backdrop",Float)=1
        _SkyUVTransform("Sky framing: scale and offset",Vector)=(1,1,0,0)
        _CloudTex("Painted animation cloud density",2D)="black"{}
        [Toggle] _CloudTextureEnabled("Painted cloud layer",Float)=0
        _MountainTex("Painted distant mountains",2D)="black"{}
        [Toggle] _MountainEnabled("Distant mountain vista",Float)=0
        _OutdoorFog("Morning valley mist",Range(0,1))=0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Cull Off ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Hour,_Daylight,_GardenSkyMatte,_GardenEnabled,_CloudTextureEnabled,_MountainEnabled,_OutdoorFog;
                float4 _SkyUVTransform;
            CBUFFER_END
            TEXTURE2D(_GardenTex);SAMPLER(sampler_GardenTex);
            TEXTURE2D(_CloudTex);SAMPLER(sampler_CloudTex);
            TEXTURE2D(_MountainTex);SAMPLER(sampler_MountainTex);
            struct Attributes { float4 positionOS:POSITION;float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0; };
            Varyings Vert(Attributes v)
            {
                Varyings o;o.positionWS=TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS);return o;
            }
            float2 DistantViewUV(float3 positionWS)
            {
                // The quad provides coverage, not a nearby poster: angular coordinates do not acquire
                // the unbounded horizontal stretch of a planar projection near an oblique window view.
                // Preserve the old center framing and its local scale for a straight-on view.
                float3 origin=TransformObjectToWorld(float3(0,0,0));
                float3 axisX=mul((float3x3)unity_ObjectToWorld,float3(1,0,0));
                float3 axisY=mul((float3x3)unity_ObjectToWorld,float3(0,1,0));
                float2 size=max(float2(length(axisX),length(axisY)),float2(.001,.001));
                float3 right=axisX/size.x,up=axisY/size.y;
                float3 forward=normalize(cross(right,up));
                float3 cameraOffset=_WorldSpaceCameraPos-origin;
                float depth=-dot(cameraOffset,forward);
                forward*=depth<0?-1:1;
                float3 ray=normalize(positionWS-_WorldSpaceCameraPos);
                float x=dot(ray,right),y=dot(ray,up),z=max(.001,dot(ray,forward));
                float2 angles=float2(atan2(x,z),atan2(y,length(float2(x,z))));
                float2 eyeOnPlane=float2(dot(cameraOffset,right),dot(cameraOffset,up))/size+.5;
                return (eyeOnPlane+angles*max(abs(depth),1)/size)*_SkyUVTransform.xy+_SkyUVTransform.zw;
            }
            float MirrorPlate(float coordinate)
            {
                // Reflect adjacent copies of a non-panoramic painting instead of stretching its edge
                // column or joining unrelated left/right borders. Ease the turn to remove a hard crease.
                float mirrored=1-abs(frac(coordinate*.5)*2-1);
                const float edge=.045;
                if(mirrored<edge)
                {
                    float t=mirrored/edge;return edge*t*t*(2-t);
                }
                if(mirrored>1-edge)
                {
                    float t=(1-mirrored)/edge;return 1-edge*t*t*(2-t);
                }
                return mirrored;
            }
            float PlateHeightMask(float height)
            {
                // A bounded painted cloud/garden layer blends back into the procedural sky before
                // leaving the image. No clamped top/bottom pixel row remains visible outside its domain.
                return smoothstep(0,.075,height)*(1-smoothstep(.925,1,height));
            }
            float Hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
            float Noise(float2 p)
            {
                float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);
                return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y);
            }
            float Fbm(float2 p){return Noise(p)*.57+Noise(p*2.03)*.28+Noise(p*4.1)*.15;}
            half4 Frag(Varyings input):SV_Target
            {
                float2 uv=DistantViewUV(input.positionWS);float elevation=saturate(uv.y);
                float solar=sin((_Hour-6)/12*PI);
                float dusk=(1-smoothstep(.05,.65,solar))*_Daylight;
                float3 horizon=lerp(float3(.012,.022,.055),lerp(float3(.72,.84,.9),float3(1,.49,.25),dusk),_Daylight);
                float3 zenith=lerp(float3(.004,.009,.032),float3(.075,.34,.65),_Daylight);
                float3 color=lerp(horizon,zenith,smoothstep(.16,1,elevation));
                float2 sun=float2(.5+cos((_Hour-6)/12*PI)*.28,.25+max(0,solar)*.48);
                float2 d=(uv-sun)*float2(1.65,1);float radius=length(d);
                color+=float3(1,.78,.4)*(exp(-radius*18)*.22+1-smoothstep(.017,.024,radius))*_Daylight;
                float2 moon=float2(.66,.67);float2 moonDelta=(uv-moon)*float2(1.65,1);
                float disk=1-smoothstep(.023,.027,length(moonDelta));
                float crescent=smoothstep(.022,.026,length(moonDelta-float2(.014,.006)));
                color+=float3(.7,.8,1)*disk*crescent*(1-_Daylight);
                float2 stars=uv*float2(240,155);float2 cell=floor(stars);
                float star=(1-smoothstep(.02,.14,length(frac(stars)-.5)))*step(.985,Hash(cell));
                color+=star*(1-_Daylight)*smoothstep(.27,.5,uv.y)*.75;
                float2 cloudUV=uv+float2(sin(_Time.y*.012)*.025,0);
                // Preserve the painted central cloud formations, then hand off to non-repeating
                // procedural clouds before either image edge. Mirroring cloud curls creates conspicuous arches.
                float paintedCloud=SAMPLE_TEXTURE2D(_CloudTex,sampler_CloudTex,saturate(cloudUV)).r;
                float cloudWidthMask=smoothstep(0,.16,cloudUV.x)*(1-smoothstep(.84,1,cloudUV.x));
                float proceduralCloud=smoothstep(.5,.77,Fbm(uv*float2(6,9)+float2(_Time.y*.002,0)));
                float cloudDensity=lerp(proceduralCloud,paintedCloud,_CloudTextureEnabled*cloudWidthMask*PlateHeightMask(cloudUV.y));
                float cloudMask=smoothstep(.035,.22,cloudDensity)*.88;
                float3 dayCloud=lerp(float3(.58,.71,.83),float3(.97,.98,.96),smoothstep(.15,.88,cloudDensity));
                dayCloud=lerp(dayCloud,dayCloud*float3(1,.69,.47),dusk*.55);
                float3 nightCloud=lerp(float3(.018,.028,.06),float3(.07,.1,.17),smoothstep(.15,.88,cloudDensity));
                color=lerp(color,lerp(nightCloud,dayCloud,_Daylight),cloudMask);
                // Painted forest and rock details replace the old flat procedural hill bands.
                // A black sky backing leaves the independently animated sky, clouds and moon visible.
                // Continue only the lower forest strip below the painting, never an upside-down ridge.
                // Above the painting the mountain matte becomes zero and the animated sky remains.
                float mountainV=uv.y<0?.28*MirrorPlate(-uv.y/.28):min(uv.y,1);
                half3 mountain=SAMPLE_TEXTURE2D(_MountainTex,sampler_MountainTex,float2(MirrorPlate(uv.x),mountainV)).rgb;
                float mountainEdge=smoothstep(.0015,.006,max(mountain.r,max(mountain.g,mountain.b)));
                // Below the ridge band the image is solid forest; preserve its deepest tree shadows.
                float mountainMatte=lerp(1,mountainEdge,smoothstep(.36,.46,uv.y))*(1-smoothstep(.94,1,uv.y))*_MountainEnabled;
                mountain=min(mountain/max(mountainMatte,.01),1);
                float3 dayMountain=mountain*lerp(float3(.91,.96,1),float3(1,.67,.43),dusk*.6);
                float3 nightMountain=mountain*float3(.09,.14,.25);
                color=lerp(color,lerp(nightMountain,dayMountain,_Daylight),mountainMatte);
                float valley=1-smoothstep(.13,.63,uv.y);
                float mistNoise=Fbm(uv*float2(5,19)+float2(_Time.y*.009,-_Time.y*.001));
                float wisps=smoothstep(.24,.76,mistNoise);
                float mist=_OutdoorFog*valley*(.38+wisps*.57)*lerp(.35,1,mountainMatte);
                float3 fogColor=lerp(float3(.025,.039,.075),lerp(float3(.7,.79,.83),float3(.9,.69,.53),dusk*.45),_Daylight);
                color=lerp(color,fogColor,saturate(mist));
                float2 gardenUV=uv;
                gardenUV.x+=sin(uv.y*23+_Hour*4)*.0007*smoothstep(.4,.7,uv.y);
                half4 garden=SAMPLE_TEXTURE2D(_GardenTex,sampler_GardenTex,float2(MirrorPlate(gardenUV.x),saturate(gardenUV.y)));
                garden.a*=PlateHeightMask(gardenUV.y);
                // The painted RGB plate uses a neutral backing. Key only bright neutral pixels
                // above the horizon, preserving pale flowers and gravel in the foreground.
                float highest=max(garden.r,max(garden.g,garden.b));
                float lowest=min(garden.r,min(garden.g,garden.b));
                float chroma=(highest-lowest)/max(highest,.001);
                float skyMatte=(1-smoothstep(.05,.18,chroma))*smoothstep(.28,.42,highest)*smoothstep(.43,.48,uv.y);
                // Remove the neutral backing from partially covered leaf-edge pixels as well,
                // otherwise their daytime white fringe remains visible against the night sky.
                float matte=skyMatte*_GardenSkyMatte;
                garden.rgb=min(garden.rgb,max(0,(garden.rgb-matte*.6)/max(1-matte,.01)));
                garden.a*=1-skyMatte*_GardenSkyMatte;
                float3 dayGarden=garden.rgb*lerp(float3(1,1,1),float3(1,.7,.43),dusk*.6);
                float3 nightGarden=garden.rgb*float3(.09,.14,.24);
                color=lerp(color,lerp(nightGarden,dayGarden,_Daylight),garden.a*_GardenEnabled);
                return half4(color,1);
            }
            ENDHLSL
        }
    }
}
