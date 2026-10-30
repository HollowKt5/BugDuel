using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BeetleArena
{
    /// <summary>Scene-local canopy light and sparse dust. Owns and releases only its runtime resources.</summary>
    [DisallowMultipleComponent]
    public sealed class HusbandryAtmosphere : MonoBehaviour
    {
        public Light canopy;
        public Renderer soil;
        public Shader moteShader;
        [Range(0,.65f)] public float leafShade=.38f;
        [Range(0,.15f)] public float breezeSpeed=.035f;
        [Range(0,3)] public float dustPerSecond=1.4f;
        public Vector2 canopySize=new Vector2(13,11);
        Texture2D cookie;
        Texture previousCookie;
        Vector2 previousSize,previousOffset;
        UniversalAdditionalLightData lightData;
        GameObject dust;
        Material dustMaterial;
        float elapsed;
        public bool Ready => cookie&&canopy&&canopy.cookie==cookie;

        void OnEnable()
        {
            if(!canopy||!soil)return;
            elapsed=0;
            lightData=canopy.GetComponent<UniversalAdditionalLightData>();
            if(!lightData)lightData=canopy.gameObject.AddComponent<UniversalAdditionalLightData>();
            previousCookie=canopy.cookie;previousSize=lightData.lightCookieSize;previousOffset=lightData.lightCookieOffset;
            cookie=CreateCanopy(leafShade);canopy.cookie=cookie;
            lightData.lightCookieSize=canopySize;
            CreateDust();
        }

        void Update()
        {
            if(!Ready)return;
            elapsed+=Mathf.Min(Time.unscaledDeltaTime,.05f);
            // A bounded sway avoids a conspicuous scrolling texture and never moves the physical light.
            lightData.lightCookieOffset=previousOffset+new Vector2(Mathf.Sin(elapsed*.31f),Mathf.Sin(elapsed*.23f+.7f))*(breezeSpeed*3);
        }

        public static Texture2D CreateCanopy(float strength)
        {
            const int size=128,leaves=38;
            var centers=new Vector2[leaves];var radii=new Vector2[leaves];var angles=new float[leaves];
            var random=new System.Random(7613);
            for(int i=0;i<leaves;i++)
            {
                centers[i]=new Vector2((float)random.NextDouble(),(float)random.NextDouble());
                radii[i]=new Vector2(.024f+(float)random.NextDouble()*.025f,.055f+(float)random.NextDouble()*.04f);
                angles[i]=(float)random.NextDouble()*Mathf.PI*2;
            }
            var pixels=new Color32[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float shade=0;
                for(int i=0;i<leaves;i++)
                {
                    float dx=Mathf.Repeat((x+.5f)/size-centers[i].x+.5f,1)-.5f;
                    float dy=Mathf.Repeat((y+.5f)/size-centers[i].y+.5f,1)-.5f;
                    float s=Mathf.Sin(angles[i]),c=Mathf.Cos(angles[i]);
                    float u=(dx*c-dy*s)/radii[i].x,v=(dx*s+dy*c)/radii[i].y;
                    shade=Mathf.Max(shade,1-Mathf.SmoothStep(.35f,1.25f,u*u+v*v));
                }
                float uWindow=(x+.5f)/size,vWindow=(y+.5f)/size;
                float frame=1-Mathf.SmoothStep(.007f,.021f,Mathf.Min(Mathf.Abs(uWindow-.5f),Mathf.Abs(vWindow-.5f)));
                byte value=(byte)Mathf.RoundToInt(255*(1-Mathf.Clamp(strength,0,.65f)*shade)*(1-frame*.28f));
                pixels[y*size+x]=new Color32(value,value,value,255);
            }
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,false,true)
            { name="Husbandry soft canopy cookie",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Bilinear,hideFlags=HideFlags.DontSave };
            texture.SetPixels32(pixels);texture.Apply(false,true);return texture;
        }

        void CreateDust()
        {
            if(!moteShader||dustPerSecond<=0)return;
            dust=new GameObject("Canopy dust");dust.transform.SetParent(transform,false);
            var bounds=soil.bounds;dust.transform.position=new Vector3(bounds.center.x,bounds.max.y+.9f,bounds.center.z);
            var particles=dust.AddComponent<ParticleSystem>();particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.useAutoRandomSeed=false;particles.randomSeed=6173;
            var main=particles.main;main.loop=true;main.duration=16;main.prewarm=true;main.maxParticles=40;
            main.startLifetime=new ParticleSystem.MinMaxCurve(10,16);main.startSpeed=new ParticleSystem.MinMaxCurve(.018f,.045f);
            main.startSize=new ParticleSystem.MinMaxCurve(.018f,.035f);
            main.startColor=new Color(1,.88f,.65f,.26f);main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.useUnscaledTime=true;
            var emission=particles.emission;emission.rateOverTime=dustPerSecond;
            var shape=particles.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(bounds.size.x*.82f,1.5f,bounds.size.z*.82f);
            var noise=particles.noise;noise.enabled=true;noise.strength=.045f;noise.frequency=.25f;noise.scrollSpeed=.06f;noise.quality=ParticleSystemNoiseQuality.Low;
            var color=particles.colorOverLifetime;color.enabled=true;
            var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.2f),new GradientAlphaKey(1,.7f),new GradientAlphaKey(0,1)});
            color.color=gradient;
            dustMaterial=new Material(moteShader){name="Husbandry dust",hideFlags=HideFlags.DontSave};
            var renderer=particles.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=dustMaterial;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;renderer.lightProbeUsage=LightProbeUsage.Off;
            particles.Play();
        }

        void OnDisable()
        {
            if(canopy&&canopy.cookie==cookie)
            {
                canopy.cookie=previousCookie;
                if(lightData){lightData.lightCookieSize=previousSize;lightData.lightCookieOffset=previousOffset;}
            }
            if(dust)Destroy(dust);if(dustMaterial)Destroy(dustMaterial);if(cookie)Destroy(cookie);
            dust=null;dustMaterial=null;cookie=null;
        }
    }
}
