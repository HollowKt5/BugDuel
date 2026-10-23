using System.Collections;
using DG.Tweening;
using UnityEngine;

namespace BeetleArena.Campaign
{
    // A modal pauses the page. Capture its actual world + HUD once, then reuse that
    // picture while switching tabs. No world shader is applied to the foreground UI.
    public sealed class CampaignModalBackdrop : MonoBehaviour
    {
        UnityEngine.UI.RawImage image;
        Material blurMaterial;
        RenderTexture blurred;
        CanvasGroup dialog;
        Coroutine capture;
        Tween fade;
        bool hiding;
        int capturedWidth,capturedHeight;
        public bool Ready=>blurred && capture==null;

        public void Initialize(Shader shader)
        {
            image=gameObject.AddComponent<UnityEngine.UI.RawImage>();image.raycastTarget=false;
            // Screen capture keeps the backbuffer's top-origin layout on D3D/Metal.
            image.uvRect=SystemInfo.graphicsUVStartsAtTop?new Rect(0,1,1,-1):new Rect(0,0,1,1);
            image.color=Color.clear;
            blurMaterial=new Material(shader) {name="Dialog Gaussian blur",hideFlags=HideFlags.HideAndDontSave};
        }
        public void Show(CanvasGroup foreground)
        {
            // A newly opened window needs the current page, not the picture of a closing one.
            if(hiding){StopFade();ReleaseCapture();hiding=false;}
            gameObject.SetActive(true);dialog=foreground;
            if(Ready)return;
            dialog.alpha=0;
            if(capture==null)capture=StartCoroutine(CapturePage());
        }
        IEnumerator CapturePage()
        {
            // The dialog is invisible for this render, so its text never enters the blur.
            yield return new WaitForEndOfFrame();
            capturedWidth=Screen.width;capturedHeight=Screen.height;
            var source=RenderTexture.GetTemporary(Screen.width,Screen.height,0,RenderTextureFormat.ARGB32);
            int w=Mathf.Max(1,Screen.width/2),h=Mathf.Max(1,Screen.height/2);
            var scratch=RenderTexture.GetTemporary(w,h,0,RenderTextureFormat.ARGB32);
            var previous=RenderTexture.active;
            try
            {
                ScreenCapture.CaptureScreenshotIntoRenderTexture(source);
                blurred=new RenderTexture(w,h,0,RenderTextureFormat.ARGB32)
                    {name="Dialog page blur",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
                scratch.filterMode=FilterMode.Bilinear;scratch.wrapMode=TextureWrapMode.Clamp;
                Graphics.Blit(source,blurred,blurMaterial,1);
                blurMaterial.SetVector("_Direction",new Vector4(1,0,0,0));Graphics.Blit(blurred,scratch,blurMaterial,0);
                blurMaterial.SetVector("_Direction",new Vector4(0,1,0,0));Graphics.Blit(scratch,blurred,blurMaterial,0);
                image.texture=blurred;image.color=Color.clear;
                fade=DOTween.To(()=>image.color.a,SetAlpha,1,.18f).SetEase(Ease.InOutSine).SetUpdate(true)
                    .SetTarget(this).OnComplete(()=>fade=null);
                if(dialog)dialog.alpha=1;
            }
            finally
            {
                RenderTexture.active=previous;RenderTexture.ReleaseTemporary(source);RenderTexture.ReleaseTemporary(scratch);
                capture=null;
            }
        }
        void Update()
        {
            if(!Ready || !dialog || hiding)return;
            if((capturedWidth!=Screen.width||capturedHeight!=Screen.height) && !(CampaignApp.Instance && CampaignApp.Instance.Busy))
            {
                StopFade();ReleaseCapture();
                dialog.alpha=0;capture=StartCoroutine(CapturePage());
            }
        }
        // A black naming page may temporarily cover this cached picture. Its old window
        // is destroyed, so stop watching that foreground until another window calls Show.
        public void Retain()
        {
            StopCapture();dialog=null;
        }
        public void Hide(bool animate=true)
        {
            StopCapture();StopFade();
            if(dialog)dialog.alpha=1;dialog=null;
            if(!animate || !blurred || !gameObject.activeInHierarchy){FinishHide();return;}
            hiding=true;
            fade=DOTween.To(()=>image.color.a,SetAlpha,0,.16f).SetEase(Ease.InQuad).SetUpdate(true)
                .SetTarget(this).OnComplete(()=>{fade=null;FinishHide();});
        }
        void SetAlpha(float alpha)=>image.color=new Color(1,1,1,alpha);
        void StopCapture()
        {
            if(capture!=null){StopCoroutine(capture);capture=null;}
        }
        void StopFade(){fade?.Kill();fade=null;}
        void ReleaseCapture()
        {
            image.texture=null;image.color=Color.clear;
            if(blurred){blurred.Release();Destroy(blurred);blurred=null;}
        }
        void FinishHide()
        {
            hiding=false;ReleaseCapture();
            gameObject.SetActive(false);
        }
        void OnDisable(){StopCapture();StopFade();}
        void OnDestroy()
        {
            StopCapture();StopFade();
            if(blurred){blurred.Release();Destroy(blurred);}
            if(blurMaterial)Destroy(blurMaterial);
        }
    }
}
