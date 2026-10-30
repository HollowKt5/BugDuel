using BeetleArena.Campaign;
using UnityEngine;
using UnityEngine.Rendering;

namespace BeetleArena
{
    /// <summary>Reads the campaign clock. Sky, window reflections and room lighting share the same solar phase.</summary>
    public sealed class HusbandryDaylight:MonoBehaviour
    {
        public Light sunlight,fill;
        public Renderer sky;
        public Renderer[] panes;
        public ReflectionProbe roomReflection;
        public Vector3 windowCenter=new Vector3(0,4.3f,11.8f);
        [Range(0,24)] public float previewHour=12;
        public float Hour {get;private set;}
        public float Daylight {get;private set;}
        public double ElapsedDays {get;private set;}
        MaterialPropertyBlock skyProperties,glassProperties;
        float lastHour=-1,lastProbeHour=-100;
        Color originalSky,originalEquator,originalGround;
        float sunIntensity,fillIntensity;Color sunColor,fillColor;Quaternion sunRotation;
        int probeRequest=-1;
        BeetleHusbandryController nursery;
        float nurseryElapsed;
        void OnEnable()
        {
            originalSky=RenderSettings.ambientSkyColor;originalEquator=RenderSettings.ambientEquatorColor;originalGround=RenderSettings.ambientGroundColor;
            if(sunlight){sunIntensity=sunlight.intensity;sunColor=sunlight.color;sunRotation=sunlight.transform.rotation;}
            if(fill){fillIntensity=fill.intensity;fillColor=fill.color;}
            skyProperties=new MaterialPropertyBlock();glassProperties=new MaterialPropertyBlock();lastHour=-1;lastProbeHour=-100;
            nursery=FindFirstObjectByType<BeetleHusbandryController>();nurseryElapsed=0;
            Update();
        }
        void Update()
        {
            var clock=CampaignApp.Instance?.Session?.State.clock;
            if(clock==null&&nursery&&!nursery.IsPaused)nurseryElapsed+=Time.unscaledDeltaTime;
            ElapsedDays=clock!=null?clock.elapsedDays:previewHour/24d+(nursery?nurseryElapsed/Mathf.Max(1,nursery.secondsPerGameDay):0);
            float hour=(float)(ElapsedDays-System.Math.Floor(ElapsedDays))*24;
            hour=Mathf.Repeat(hour,24);
            if(Mathf.Abs(hour-lastHour)>.002f)PresentHour(hour);
            // One low-resolution, time-sliced room probe, refreshed after a significant lighting change.
            if(roomReflection&&Mathf.Abs(Mathf.DeltaAngle(lastProbeHour*15,Hour*15))>7.5f&&
                (probeRequest<0||roomReflection.IsFinishedRendering(probeRequest)))
            {probeRequest=roomReflection.RenderProbe();lastProbeHour=Hour;}
        }
        public void PresentHour(float hour)
        {
            Hour=Mathf.Repeat(hour,24);lastHour=Hour;
            float solar=Mathf.Sin((Hour-6)/12*Mathf.PI);
            Daylight=Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.16f,.2f,solar));
            float golden=(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.05f,.65f,solar)))*Daylight;
            if(sunlight)
            {
                sunlight.color=Color.Lerp(new Color(.4f,.56f,1),Color.Lerp(new Color(1,.95f,.84f),new Color(1,.55f,.27f),golden),Daylight);
                sunlight.intensity=Mathf.Lerp(.16f,1.45f,Daylight)*(1-golden*.3f);
                sunlight.transform.rotation=Quaternion.LookRotation(new Vector3(Mathf.Cos((Hour-6)/12*Mathf.PI)*.35f,-(.35f+Mathf.Abs(solar)*.8f),-.8f));
            }
            if(fill){fill.intensity=Mathf.Lerp(.22f,.58f,Daylight);fill.color=Color.Lerp(new Color(.35f,.48f,.82f),new Color(.76f,.85f,1),Daylight);}
            RenderSettings.ambientSkyColor=Color.Lerp(new Color(.09f,.12f,.23f),new Color(.49f,.57f,.66f),Daylight);
            RenderSettings.ambientEquatorColor=Color.Lerp(new Color(.13f,.12f,.14f),new Color(.34f,.38f,.33f),Daylight);
            RenderSettings.ambientGroundColor=Color.Lerp(new Color(.09f,.065f,.04f),new Color(.2f,.15f,.1f),Daylight);
            if(sky){skyProperties.SetFloat("_Hour",Hour);skyProperties.SetFloat("_Daylight",Daylight);sky.SetPropertyBlock(skyProperties);}
            glassProperties.SetVector("_WindowPosition",windowCenter);glassProperties.SetVector("_WindowDirection",Vector3.forward);
            glassProperties.SetFloat("_WindowWidth",5.8f);glassProperties.SetFloat("_WindowHeight",4.4f);
            glassProperties.SetFloat("_WindowSoftness",.035f);glassProperties.SetFloat("_WindowStrength",.12f*Daylight);
            glassProperties.SetColor("_WindowColor",Color.Lerp(new Color(.15f,.22f,.4f),new Color(.9f,.96f,1),Daylight));
            foreach(var pane in panes)if(pane)pane.SetPropertyBlock(glassProperties);
        }
        void OnDisable()
        {
            if(sunlight){sunlight.intensity=sunIntensity;sunlight.color=sunColor;sunlight.transform.rotation=sunRotation;}
            if(fill){fill.intensity=fillIntensity;fill.color=fillColor;}
            RenderSettings.ambientSkyColor=originalSky;RenderSettings.ambientEquatorColor=originalEquator;RenderSettings.ambientGroundColor=originalGround;
            if(sky)sky.SetPropertyBlock(null);foreach(var pane in panes)if(pane)pane.SetPropertyBlock(null);
        }
    }
}
