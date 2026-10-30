using System;
using UnityEngine;

namespace BeetleArena
{
    // Runs once on the completed world texture, before focus blur and the selected display filter.
    public sealed class WorldToonLayer : IDisposable
    {
        Material material;
        RenderTexture frame;
        static readonly int Bands=Shader.PropertyToID("_Bands");
        static readonly int Outline=Shader.PropertyToID("_Outline");
        static readonly int Ink=Shader.PropertyToID("_Ink");
        static readonly int Tone=Shader.PropertyToID("_Tone");
        public static WorldToonSettings Settings=>BeetleDisplayPreferences.Profile?BeetleDisplayPreferences.Profile.toon:null;
        public static bool Enabled=>Settings!=null&&Settings.Active;

        public RenderTexture Render(RenderTexture source)=>Render(source,Settings);

        public RenderTexture Render(RenderTexture source,WorldToonSettings settings)
        {
            if(!source||settings==null||!settings.Active)
            { Dispose();return source; }
            if(!material)
            {
                var shader=Resources.Load<Shader>("BeetleWorldToon");
                if(!shader||!shader.isSupported)return source;
                material=new Material(shader){name="World toon overlay (runtime)",hideFlags=HideFlags.HideAndDontSave};
            }
            if(!frame||frame.width!=source.width||frame.height!=source.height||frame.graphicsFormat!=source.graphicsFormat)
            {
                ReleaseFrame();
                var descriptor=source.descriptor;
                descriptor.depthBufferBits=0;descriptor.msaaSamples=1;descriptor.bindMS=false;
                descriptor.useMipMap=false;descriptor.autoGenerateMips=false;descriptor.enableRandomWrite=false;
                frame=new RenderTexture(descriptor){name="World toon layer",filterMode=FilterMode.Bilinear,
                    wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};
            }
            if(!frame.IsCreated()&&!frame.Create()){Dispose();return source;}
            material.SetVector(Bands,new Vector4(Mathf.Clamp(settings.bands,3,8),Mathf.Clamp(settings.softness,.02f,.45f),Mathf.Clamp01(settings.strength),0));
            material.SetVector(Outline,new Vector4(Mathf.Clamp01(settings.outlineStrength),Mathf.Clamp(settings.outlineWidth,.5f,3),Mathf.Clamp(settings.edgeThreshold,.02f,.8f),0));
            material.SetColor(Ink,settings.outlineColor);
            material.SetVector(Tone,new Vector4(Mathf.Clamp(settings.shadowLift,0,.3f),Mathf.Clamp01(settings.detailRetention),0,0));
            var active=RenderTexture.active;
            bool write=GL.sRGBWrite;
            try
            {
                GL.sRGBWrite=QualitySettings.activeColorSpace==ColorSpace.Linear&&frame.sRGB;
                Graphics.Blit(source,frame,material,0);
            }
            finally { RenderTexture.active=active;GL.sRGBWrite=write; }
            return frame;
        }

        void ReleaseFrame()
        {
            if(frame){frame.Release();Destroy(frame);frame=null;}
        }
        static void Destroy(UnityEngine.Object value)
        {
            if(Application.isPlaying)UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
        public void Dispose()
        {
            ReleaseFrame();
            if(material){Destroy(material);material=null;}
        }
    }
}
