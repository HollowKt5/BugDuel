using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace BeetleArena.Campaign
{
    public sealed class CampaignSceneTransition : MonoBehaviour
    {
        public bool Busy { get; private set; }
        public bool Opening { get; private set; }
        public float OpenProgress { get; private set; }
        public float PhaseTime { get; private set; }
        public CampaignTransitionMode Mode { get; set; }
        public CampaignTransitionMode ActiveMode { get; private set; }
        public CampaignTransitionProfile Profile { get; private set; }
        CanvasGroup fade;
        Canvas overlay;
        UnityEngine.UI.Image cover;
        RectTransform upper,lower;
        bool overlayNeedsRaise;
        void Awake()
        {
            var authored=Resources.Load<CampaignTransitionProfile>(CampaignTransitionProfile.ResourceName);
            // A runtime copy allows previews without changing the authored asset or other sessions.
            Profile=authored?Instantiate(authored):ScriptableObject.CreateInstance<CampaignTransitionProfile>();
            Profile.hideFlags=HideFlags.DontSave;
            var canvas=new GameObject("SceneFade",typeof(Canvas),typeof(UnityEngine.UI.GraphicRaycaster));
            // Keep the overlay at the scene root; the transition service owns its lifetime.
            overlay=canvas.GetComponent<Canvas>();overlay.renderMode=RenderMode.ScreenSpaceOverlay;
            overlay.overrideSorting=true;overlay.sortingOrder=1000;
            var go=new GameObject("Fade",typeof(RectTransform),typeof(CanvasGroup)); go.transform.SetParent(canvas.transform,false);
            var rt=(RectTransform)go.transform; rt.anchorMin=Vector2.zero; rt.anchorMax=Vector2.one; rt.offsetMin=rt.offsetMax=Vector2.zero;
            fade=go.GetComponent<CanvasGroup>();
            cover=Shutter("Cover",rt,Vector2.zero,Vector2.one).GetComponent<UnityEngine.UI.Image>();
            upper=Shutter("UpperShutter",rt,new Vector2(0,.5f),Vector2.one);
            lower=Shutter("LowerShutter",rt,Vector2.zero,new Vector2(1,.5f));
            ResetFade();
        }
        static RectTransform Shutter(string name,Transform parent,Vector2 min,Vector2 max)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(UnityEngine.UI.Image));go.transform.SetParent(parent,false);
            var rt=(RectTransform)go.transform;rt.anchorMin=min;rt.anchorMax=max;rt.offsetMin=rt.offsetMax=Vector2.zero;
            go.GetComponent<UnityEngine.UI.Image>().color=Color.black;return rt;
        }
        public bool Go(CampaignPage page,Action<string> failed,CampaignTransitionMode? requestedMode=null)
        {
            if(Busy)return false; string target=CampaignApp.SceneName(page);
            if(!Application.CanStreamedLevelBeLoaded(target)) { failed("无法载入场景："+target); return false; }
            StartCoroutine(Load(target,failed,requestedMode??Mode)); return true;
        }
        IEnumerator Load(string target,Action<string> failed,CampaignTransitionMode mode)
        {
            RaiseOverlay();
            Busy=true; fade.blocksRaycasts=true;
            ActiveMode=mode==CampaignTransitionMode.BlackFade?CampaignTransitionMode.BlackFade:CampaignTransitionMode.Television;
            var camera=SceneManager.GetActiveScene().name==CampaignApp.SceneName(CampaignPage.MainMenu)?Camera.main:null;
            float size=camera?camera.orthographicSize:0;
            try
            {
                float darken=Profile.CloseSeconds,open=Profile.OpenSeconds;
                for(float t=0;t<darken;t+=AnimationStep())
                {
                    PhaseTime=t/darken;
                    fade.alpha=Mathf.Max(fade.alpha,CampaignTransitionProfile.Evaluate(Profile.closeCurve,PhaseTime));
                    if(camera)camera.orthographicSize=Mathf.Lerp(size,size*.88f,fade.alpha);
                    yield return null;
                }
                PhaseTime=1;fade.alpha=1;AsyncOperation operation=null;
                float blackSince=Time.realtimeSinceStartup;
                // Present a fully black frame before scene activation can stall or replace cameras.
                Canvas.ForceUpdateCanvases();yield return new WaitForEndOfFrame();
                DontDestroyOnLoad(overlay.gameObject);
                try { operation=SceneManager.LoadSceneAsync(target); }
                catch(Exception e) { failed("场景切换失败："+e.Message); }
                if(operation!=null)while(!operation.isDone)yield return null;
                // Start builds the page. Keep the cover until the world (and optional blur) has rendered.
                yield return null;
                var backdrop=FindFirstObjectByType<CampaignBackdrop>();
                float deadline=Time.realtimeSinceStartup+15;
                while(backdrop&&!backdrop.Ready)
                {
                    if(Time.realtimeSinceStartup>deadline) { failed("场景画面准备超时");yield break; }
                    yield return null;
                }
                while(Time.realtimeSinceStartup-blackSince<Profile.BlackHoldSeconds)yield return null;
                yield return new WaitForEndOfFrame();
                bool television=ActiveMode==CampaignTransitionMode.Television;
                upper.gameObject.SetActive(television);lower.gameObject.SetActive(television);cover.gameObject.SetActive(!television);Opening=true;
                Canvas.ForceUpdateCanvases();
                for(float t=0;t<open;t+=AnimationStep())
                {
                    PhaseTime=t/open;
                    // Do not let overshoot/backtracking in an authored curve re-close a revealed page.
                    OpenProgress=Mathf.Max(OpenProgress,CampaignTransitionProfile.Evaluate(Profile.openCurve,PhaseTime));
                    if(television)
                    {
                        float distance=((RectTransform)fade.transform).rect.height*.5f+2;
                        upper.anchoredPosition=new Vector2(0,distance*OpenProgress);
                        lower.anchoredPosition=new Vector2(0,-distance*OpenProgress);
                    }
                    else fade.alpha=1-OpenProgress;
                    yield return null;
                }
            }
            finally { if(camera)camera.orthographicSize=size; ResetFade(); }
        }
        // Loading or shader warm-up may produce a long frame. Do not skip most of the next animation.
        static float AnimationStep() => Mathf.Min(Time.unscaledDeltaTime,1f/30f);
        void ResetFade()
        {
            Busy=false;Opening=false;OpenProgress=0;PhaseTime=0;
            if(fade) { fade.alpha=0;fade.blocksRaycasts=false; }
            if(cover)cover.gameObject.SetActive(true);
            if(upper) { upper.gameObject.SetActive(false);upper.anchoredPosition=Vector2.zero; }
            if(lower) { lower.gameObject.SetActive(false);lower.anchoredPosition=Vector2.zero; }
        }
        void OnDisable() { SceneManager.sceneLoaded-=OnSceneLoaded;RenderPipelineManager.beginContextRendering-=BeforeRender;StopAllCoroutines(); ResetFade(); }
        void OnDestroy() { if(overlay)Destroy(overlay.gameObject);if(Profile)Destroy(Profile); }
        void OnEnable() { SceneManager.sceneLoaded+=OnSceneLoaded;RenderPipelineManager.beginContextRendering+=BeforeRender; }
        void OnSceneLoaded(Scene scene,LoadSceneMode mode)
        {
            // URP's scene camera must see the overlay in its own scene before the first
            // rendered frame. The service stays persistent; only its presentation root moves.
            if(overlay) { SceneManager.MoveGameObjectToScene(overlay.gameObject,scene);overlayNeedsRaise=true; }
        }
        void BeforeRender(ScriptableRenderContext context,List<Camera> cameras)
        {
            // New canvases are registered in Start, after sceneLoaded. Re-register the cover before
            // the first camera renders; waiting for the load coroutine exposes the page for one frame.
            if(!overlayNeedsRaise||!overlay)return;
            overlayNeedsRaise=false;RaiseOverlay();
        }
        void RaiseOverlay()
        {
            overlay.enabled=false;overlay.transform.SetAsLastSibling();
            overlay.sortingLayerID=0;overlay.overrideSorting=true;overlay.sortingOrder=1000;
            overlay.enabled=true;Canvas.ForceUpdateCanvases();
        }
    }
}
