using UnityEngine;
using UnityEngine.Rendering;
using BugDuel.Campaign;

namespace BeetleArena.Campaign
{
    // One bounded render target + one separable blur for the whole screen; never a blur per card.
    public sealed class CampaignBackdrop : MonoBehaviour
    {
        public Camera sceneCamera;
        public Shader blurShader;
        public UnityEngine.UI.RawImage output;
        public RenderTexture Frame { get; private set; }
        RenderTexture blur, scratch;
        Material material;
        int width,height;
        void OnEnable() { RenderPipelineManager.endCameraRendering+=Rendered; }
        void Start() { material=new Material(blurShader); Resize(); }
        void LateUpdate() { if(Screen.width<1||Screen.height<1)return;if (width!=Mathf.Min(1280,Screen.width) || height!=Mathf.RoundToInt(Mathf.Min(1280,Screen.width)*(float)Screen.height/Mathf.Max(1,Screen.width))) Resize(); }
        void Resize()
        {
            Release(); width=Mathf.Max(1,Mathf.Min(1280,Screen.width)); height=Mathf.Max(1,Mathf.RoundToInt(width*(float)Screen.height/Mathf.Max(1,Screen.width)));
            Frame=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32) { name="Campaign world",antiAliasing=1 };
            blur=new RenderTexture(Mathf.Max(1,width/2),Mathf.Max(1,height/2),0); scratch=new RenderTexture(blur.width,blur.height,0);
            sceneCamera.targetTexture=Frame; if (output) output.texture=Frame;
        }
        void Rendered(ScriptableRenderContext context,Camera camera)
        {
            if (camera!=sceneCamera || !Frame || !material) return;
            float radius=20f*width/Mathf.Max(1,Screen.width)/6;
            material.SetVector("_Direction",new Vector4(radius,0)); Graphics.Blit(Frame,scratch,material);
            material.SetVector("_Direction",new Vector4(0,radius)); Graphics.Blit(scratch,blur,material); Shader.SetGlobalTexture("_CampaignBlurred",blur);
        }
        void Release()
        {
            if (sceneCamera) sceneCamera.targetTexture=null;
            foreach(var rt in new[]{Frame,blur,scratch}) if(rt) { rt.Release(); Destroy(rt); }
            Frame=blur=scratch=null;
        }
        void OnDisable() { RenderPipelineManager.endCameraRendering-=Rendered; }
        void OnDestroy() { Release(); if(material) Destroy(material); }
    }

}
