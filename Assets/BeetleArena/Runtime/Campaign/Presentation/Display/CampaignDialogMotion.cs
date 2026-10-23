using System.Collections;
using DG.Tweening;
using UnityEngine;

namespace BeetleArena.Campaign
{
    // The backdrop owns the outer alpha during capture; only the sharp window animates in.
    public sealed class CampaignDialogMotion : MonoBehaviour
    {
        CanvasGroup foreground,window;
        Sequence motion;
        Coroutine waiting;
        public RectTransform Panel { get; private set; }
        public float Alpha=>foreground.alpha*window.alpha;
        public bool IsAnimating=>waiting!=null || motion!=null && motion.IsActive();
        public bool Closing { get; private set; }

        public void Open(RectTransform panel,CanvasGroup root,CampaignModalBackdrop backdrop,bool animate)
        {
            Panel=panel;foreground=root;
            window=panel.gameObject.AddComponent<CanvasGroup>();
            window.alpha=animate?0:1;
            panel.localScale=Vector3.one*(animate ? .94f : 1);
            if(!animate)return;
            if(backdrop && !backdrop.Ready)waiting=StartCoroutine(WaitForCapture(backdrop));
            else Reveal();
        }
        IEnumerator WaitForCapture(CampaignModalBackdrop backdrop)
        {
            while(!backdrop.Ready)yield return null;
            waiting=null;Reveal();
        }
        void Reveal()
        {
            motion=DOTween.Sequence().SetUpdate(true).SetTarget(this);
            motion.Append(DOTween.To(()=>window.alpha,value=>window.alpha=value,1,.20f).SetEase(Ease.OutQuad));
            motion.Join(Panel.DOScale(1,.24f).SetEase(Ease.OutBack,1.15f));
            motion.OnComplete(()=>motion=null);
        }
        public void Close()
        {
            StopMotion();Closing=true;
            gameObject.name="ClosingDialog";
            foreground.interactable=false;foreground.blocksRaycasts=false;
            motion=DOTween.Sequence().SetUpdate(true).SetTarget(this);
            // Fade the outer group too: the player-name window has a full-screen black stage.
            motion.Append(DOTween.To(()=>foreground.alpha,value=>foreground.alpha=value,0,.14f).SetEase(Ease.InQuad));
            motion.Join(Panel.DOScale(.94f,.16f).SetEase(Ease.InCubic));
            motion.OnComplete(()=>{motion=null;Discard();});
        }
        public void Discard()
        {
            StopMotion();gameObject.SetActive(false);Destroy(gameObject);
        }
        void StopMotion()
        {
            if(waiting!=null){StopCoroutine(waiting);waiting=null;}
            motion?.Kill();motion=null;
        }
        void OnDisable()=>StopMotion();
        void OnDestroy()=>StopMotion();
    }
}
