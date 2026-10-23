using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BeetleArena
{
    // Small persistent windows and dropdowns share the same unscaled open/close motion.
    public sealed class BeetlePanelMotion : MonoBehaviour
    {
        CanvasGroup group;
        Sequence motion;
        Vector3 restScale;
        bool visibilityKnown;
        public RectTransform Panel { get; private set; }
        public bool Visible { get; private set; }
        public bool IsAnimating=>motion!=null && motion.IsActive();
        public float Alpha=>group.alpha;

        public static BeetlePanelMotion Attach(GameObject panel)
        {
            if(!panel.TryGetComponent<BeetlePanelMotion>(out var result))result=panel.AddComponent<BeetlePanelMotion>();
            result.Initialize();return result;
        }
        void Initialize()
        {
            if(Panel)return;
            Panel=(RectTransform)transform;restScale=Panel.localScale;
            if(!TryGetComponent<CanvasGroup>(out group))group=gameObject.AddComponent<CanvasGroup>();
        }
        public void SetVisible(bool visible,bool animate=true,Action hidden=null)
        {
            Initialize();
            if(visibilityKnown && Visible==visible && animate)return;
            bool reversing=IsAnimating;
            StopMotion();visibilityKnown=true;Visible=visible;
            group.interactable=visible;group.blocksRaycasts=visible;
            if(visible)
            {
                gameObject.SetActive(true);
                if(!reversing){group.alpha=0;Panel.localScale=restScale*.94f;}
            }
            else if(EventSystem.current && EventSystem.current.currentSelectedGameObject &&
                EventSystem.current.currentSelectedGameObject.transform.IsChildOf(transform))
                EventSystem.current.SetSelectedGameObject(null);
            // Editor validation and inactive campaign copies only need the resulting state.
            if(!animate || !Application.isPlaying || !gameObject.activeInHierarchy)
            {
                group.alpha=visible?1:0;Panel.localScale=restScale;gameObject.SetActive(visible);
                if(!visible)hidden?.Invoke();return;
            }
            motion=DOTween.Sequence().SetUpdate(true).SetTarget(this);
            motion.Append(DOTween.To(()=>group.alpha,value=>group.alpha=value,visible?1:0,visible ? .20f : .14f)
                .SetEase(visible?Ease.OutQuad:Ease.InQuad));
            var scale=Panel.DOScale(restScale*(visible?1:.94f),visible ? .24f : .16f);
            if(visible)scale.SetEase(Ease.OutBack,1.15f);else scale.SetEase(Ease.InCubic);
            motion.Join(scale).OnComplete(()=>
            {
                motion=null;
                if(!visible){gameObject.SetActive(false);hidden?.Invoke();}
            });
        }
        void StopMotion(){motion?.Kill();motion=null;}
        void OnDisable()
        {
            StopMotion();
            if(!Panel)return;
            group.alpha=Visible?1:0;Panel.localScale=restScale;
            if(!Visible && gameObject.activeSelf)gameObject.SetActive(false);
        }
        void OnDestroy()=>StopMotion();
    }
}
