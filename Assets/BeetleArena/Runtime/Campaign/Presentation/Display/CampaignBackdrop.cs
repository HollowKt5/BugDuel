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
        public bool DeferViewportResize { get; set; }
        public Rect Viewport { get; private set; } = new Rect(0,0,1,1);
        RenderTexture blur,scratch,prefilter;
        Material blurMaterial;
        bool blurReady;
        int width,height;
        Camera uiCamera;
        bool menuFraming;
        float menuHalfWidth,menuHalfHeight,menuZoom=1;
        readonly Vector3[] outputCorners=new Vector3[4];
        void OnEnable() { RenderPipelineManager.endCameraRendering+=Rendered; }
        public void ConfigureMenuFraming()
        {
            if(!sceneCamera||!sceneCamera.orthographic||menuFraming)return;
            menuFraming=true;
            // Preserve the authored 16:9 composition, then include the complete visible terrarium.
            menuHalfHeight=sceneCamera.orthographicSize;menuHalfWidth=menuHalfHeight*16f/9f;
            foreach(var root in gameObject.scene.GetRootGameObjects())
                foreach(var renderer in root.GetComponentsInChildren<Renderer>())
                {
                    // The oversized ground backdrop deliberately extends beyond the composition.
                    if(!renderer.enabled||renderer.name=="Backdrop"||renderer is ParticleSystemRenderer)continue;
                    var bounds=renderer.bounds;
                    for(int i=0;i<8;i++)
                    {
                        var corner=bounds.center+Vector3.Scale(bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                        var local=sceneCamera.transform.InverseTransformPoint(corner);
                        menuHalfWidth=Mathf.Max(menuHalfWidth,Mathf.Abs(local.x));
                        menuHalfHeight=Mathf.Max(menuHalfHeight,Mathf.Abs(local.y));
                    }
                }
            sceneCamera.rect=new Rect(0,0,1,1);
            ApplyMenuFraming();
        }
        public void SetMenuTransitionProgress(float progress)
        {
            menuZoom=Mathf.Lerp(1,1.12f,Mathf.Clamp01(progress));
            ApplyMenuFraming();
        }
        void ApplyMenuFraming()
        {
            if(!menuFraming||!sceneCamera)return;
            sceneCamera.orthographicSize=Mathf.Max(menuHalfHeight,menuHalfWidth/Mathf.Max(.01f,sceneCamera.aspect))*1.06f*menuZoom;
        }
        /// <summary>The UI rectangle, not Camera.rect, owns the offscreen world's screen area.</summary>
        public void SetViewport(Rect normalizedViewport)
        {
            float x=Mathf.Clamp(normalizedViewport.x,0,.99f),y=Mathf.Clamp(normalizedViewport.y,0,.99f);
            Viewport=new Rect(x,y,Mathf.Clamp(normalizedViewport.width,.01f,1-x),Mathf.Clamp(normalizedViewport.height,.01f,1-y));
            ApplyViewport();
            if(isActiveAndEnabled&&sceneCamera)
            {
                if(DeferViewportResize)sceneCamera.aspect=Screen.width*Viewport.width/(Screen.height*Viewport.height);
                else Resize();
            }
        }
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
            var go=new GameObject("UICamera",typeof(Camera));go.transform.SetParent(transform,false);
            uiCamera=go.GetComponent<Camera>();uiCamera.cullingMask=0;uiCamera.clearFlags=CameraClearFlags.SolidColor;
            uiCamera.backgroundColor=new Color(.025f,.055f,.047f);uiCamera.depth=sceneCamera.depth+1;
            uiCamera.allowHDR=false;uiCamera.allowMSAA=false;
        }
        void LateUpdate()
        {
            if(Screen.width<1||Screen.height<1||!sceneCamera||DeferViewportResize)return;
            TargetSize(out int w,out int h);if(width!=w||height!=h)Resize();
        }
        void ApplyViewport()
        {
            if(!output)return;
            var rt=output.rectTransform;rt.anchorMin=Viewport.min;rt.anchorMax=Viewport.max;
            rt.offsetMin=rt.offsetMax=Vector2.zero;
        }
        void TargetSize(out int w,out int h)
        {
            float pixelsWide=Screen.width*Viewport.width,pixelsHigh=Screen.height*Viewport.height;
            if(output)
            {
                var canvas=output.canvas;var eventCamera=canvas&&canvas.renderMode!=RenderMode.ScreenSpaceOverlay?canvas.worldCamera:null;
                output.rectTransform.GetWorldCorners(outputCorners);
                Vector2 bottomLeft=RectTransformUtility.WorldToScreenPoint(eventCamera,outputCorners[0]);
                Vector2 topRight=RectTransformUtility.WorldToScreenPoint(eventCamera,outputCorners[2]);
                if(topRight.x>bottomLeft.x&&topRight.y>bottomLeft.y)
                { pixelsWide=topRight.x-bottomLeft.x;pixelsHigh=topRight.y-bottomLeft.y; }
            }
            float scale=Mathf.Min(1,1280/Mathf.Max(1,pixelsWide),1280/Mathf.Max(1,pixelsHigh));
            w=Mathf.Max(1,Mathf.RoundToInt(pixelsWide*scale));h=Mathf.Max(1,Mathf.RoundToInt(pixelsHigh*scale));
        }
        void Resize()
        {
            TargetSize(out int w,out int h);
            if(Frame&&width==w&&height==h){ApplyMenuFraming();BindOutput();return;}
            Release();width=w;height=h;
            Frame=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32) { name="Campaign world",antiAliasing=1 };
            Frame.filterMode=FilterMode.Bilinear;Frame.wrapMode=TextureWrapMode.Clamp;
            Frame.Create();var active=RenderTexture.active;
            try { RenderTexture.active=Frame;GL.Clear(true,true,Color.black); }
            finally { RenderTexture.active=active; }
            sceneCamera.rect=new Rect(0,0,1,1);sceneCamera.targetTexture=Frame;sceneCamera.aspect=(float)width/height;ApplyMenuFraming();BindOutput();
        }
        void Rendered(ScriptableRenderContext context,Camera camera)
        {
            if(camera!=sceneCamera || !Frame)return;
            if(!UiFocused || !blurShader || !blurShader.isSupported) { Ready=true;return; }
            if(!blurMaterial)blurMaterial=new Material(blurShader) { name="Campaign background focus",hideFlags=HideFlags.HideAndDontSave };
            if(!blur)
            {
                // Filter each 2:1 reduction before the Gaussian; never skip straight to quarter size.
                int halfWidth=Mathf.Max(1,(width+1)/2),halfHeight=Mathf.Max(1,(height+1)/2);
                int w=Mathf.Max(1,(halfWidth+1)/2),h=Mathf.Max(1,(halfHeight+1)/2);
                prefilter=new RenderTexture(halfWidth,halfHeight,0,RenderTextureFormat.ARGB32) { name="Campaign focus prefilter",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp };
                blur=new RenderTexture(w,h,0,RenderTextureFormat.ARGB32) { name="Campaign focused background",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp };
                scratch=new RenderTexture(w,h,0,RenderTextureFormat.ARGB32) { name="Campaign focus scratch",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp };
            }
            var active=RenderTexture.active;
            try
            {
                Graphics.Blit(Frame,prefilter,blurMaterial,1);
                Graphics.Blit(prefilter,blur,blurMaterial,1);
                blurMaterial.SetVector("_Direction",new Vector4(1,0,0,0));Graphics.Blit(blur,scratch,blurMaterial,0);
                blurMaterial.SetVector("_Direction",new Vector4(0,1,0,0));Graphics.Blit(scratch,blur,blurMaterial,0);
                blurReady=true;BindOutput();Ready=true;
            }
            finally { RenderTexture.active=active; }
        }
        void BindOutput()
        {
            if(!output)return;
            ApplyViewport();
            output.texture=UiFocused&&blurReady?blur:Frame;
            output.color=UiFocused?new Color(.86f,.86f,.86f,1):Color.white;
        }
        void ReleaseBlur()
        {
            blurReady=false;
            if(output)output.texture=Frame;
            if(blur) { blur.Release();Destroy(blur);blur=null; }
            if(scratch) { scratch.Release();Destroy(scratch);scratch=null; }
            if(prefilter) { prefilter.Release();Destroy(prefilter);prefilter=null; }
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
