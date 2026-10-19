using UnityEngine;
using UnityEngine.Rendering;

namespace BeetleArena.Campaign
{
    // UI focus affects only the world texture. The clean Frame remains available for save thumbnails.
    public sealed class CampaignBackdrop : MonoBehaviour
    {
        public Camera sceneCamera;
        public Shader blurShader;
        public UnityEngine.UI.RawImage output;
        public RenderTexture Frame { get; private set; }
        public bool UiFocused { get; private set; }
        public bool Ready { get; private set; }
        RenderTexture blur,scratch;
        Material blurMaterial;
        bool blurReady;
        int width,height;
        Camera uiCamera;
        void OnEnable() { RenderPipelineManager.endCameraRendering+=Rendered; }
        public void SetUiFocus(bool focused)
        {
            if(UiFocused!=focused)Ready=false;
            UiFocused=focused;
            if(!focused)ReleaseBlur();
            BindOutput();
        }
        void Start()
        {
            Resize();
            // URP needs an on-screen camera to composite ScreenSpaceOverlay canvases.
            // The world camera renders offscreen; without this camera Game View stays black.
            var go=new GameObject("Campaign UI Camera",typeof(Camera));go.transform.SetParent(transform,false);
            uiCamera=go.GetComponent<Camera>();uiCamera.cullingMask=0;uiCamera.clearFlags=CameraClearFlags.SolidColor;
            uiCamera.backgroundColor=new Color(.025f,.055f,.047f);uiCamera.depth=sceneCamera.depth+1;
            uiCamera.allowHDR=false;uiCamera.allowMSAA=false;
        }
        void LateUpdate() { if(Screen.width<1||Screen.height<1)return;if (width!=Mathf.Min(1280,Screen.width) || height!=Mathf.RoundToInt(Mathf.Min(1280,Screen.width)*(float)Screen.height/Mathf.Max(1,Screen.width))) Resize(); }
        void Resize()
        {
            Release(); width=Mathf.Max(1,Mathf.Min(1280,Screen.width)); height=Mathf.Max(1,Mathf.RoundToInt(width*(float)Screen.height/Mathf.Max(1,Screen.width)));
            Frame=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32) { name="Campaign world",antiAliasing=1 };
            Frame.filterMode=FilterMode.Bilinear;Frame.wrapMode=TextureWrapMode.Clamp;
            Frame.Create();var active=RenderTexture.active;
            try { RenderTexture.active=Frame;GL.Clear(true,true,Color.black); }
            finally { RenderTexture.active=active; }
            sceneCamera.targetTexture=Frame;BindOutput();
        }
        void Rendered(ScriptableRenderContext context,Camera camera)
        {
            if(camera!=sceneCamera || !Frame)return;
            if(!UiFocused || !blurShader || !blurShader.isSupported) { Ready=true;return; }
            if(!blurMaterial)blurMaterial=new Material(blurShader) { name="Campaign background focus",hideFlags=HideFlags.HideAndDontSave };
            if(!blur)
            {
                // Two quarter-size targets shared by the page, allocated only while focus is needed.
                int w=Mathf.Max(1,width/4),h=Mathf.Max(1,height/4);
                blur=new RenderTexture(w,h,0,RenderTextureFormat.ARGB32) { name="Campaign focused background",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp };
                scratch=new RenderTexture(w,h,0,RenderTextureFormat.ARGB32) { name="Campaign focus scratch",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp };
            }
            var active=RenderTexture.active;
            try
            {
                blurMaterial.SetVector("_Direction",new Vector4(3f*width/scratch.width,0,0,0));Graphics.Blit(Frame,scratch,blurMaterial);
                blurMaterial.SetVector("_Direction",new Vector4(0,3,0,0));Graphics.Blit(scratch,blur,blurMaterial);
                blurReady=true;BindOutput();Ready=true;
            }
            finally { RenderTexture.active=active; }
        }
        void BindOutput()
        {
            if(!output)return;
            output.texture=UiFocused&&blurReady?blur:Frame;
            output.color=UiFocused?new Color(.82f,.86f,.82f,1):Color.white;
        }
        void ReleaseBlur()
        {
            blurReady=false;
            if(output)output.texture=Frame;
            if(blur) { blur.Release();Destroy(blur);blur=null; }
            if(scratch) { scratch.Release();Destroy(scratch);scratch=null; }
            if(blurMaterial) { Destroy(blurMaterial);blurMaterial=null; }
        }
        void Release()
        {
            Ready=false;
            ReleaseBlur();
            if(output)output.texture=null;
            if(sceneCamera && sceneCamera.targetTexture==Frame)sceneCamera.targetTexture=null;
            if(Frame) { Frame.Release(); Destroy(Frame); }
            Frame=null;
        }
        void OnDisable() { RenderPipelineManager.endCameraRendering-=Rendered;Ready=false;ReleaseBlur(); }
        void OnDestroy() { Release(); }
    }

}
