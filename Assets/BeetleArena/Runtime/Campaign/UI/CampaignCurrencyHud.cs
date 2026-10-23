using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;

namespace BeetleArena.Campaign
{
    [DisallowMultipleComponent]
    public sealed class CampaignCurrencyHud : MonoBehaviour
    {
        const int FloatSlots=4,QueueLimit=24;
        sealed class FloatingAmount
        {
            public TMP_Text label;
            public RectTransform rect;
            public Tween tween;
            public long sequence;
            public bool active;
        }
        readonly Queue<CampaignCurrencyChange> pending=new Queue<CampaignCurrencyChange>();
        readonly FloatingAmount[] floats=new FloatingAmount[FloatSlots];
        CampaignApp owner;
        CanvasGroup group;
        TMP_Text balance;
        Tween balanceTween;
        Color ink,gain,loss;
        double shown;
        float nextFloat;
        bool built,subscribed,compact;
        public TMP_Text BalanceLabel => balance;
        public double DisplayedBalance => shown;
        public long TargetBalance { get; private set; }
        public long LastSequence { get; private set; }
        public int PendingFloats => pending.Count;
        public int ActiveFloatCount { get {int count=0;foreach(var item in floats)if(item!=null&&item.active)count++;return count;} }
        public int ReceivedCount { get; private set; }
        public int FloatsShown { get; private set; }
        public long ReceivedNetDelta { get; private set; }
        public bool BalanceTweenActive => balanceTween!=null&&balanceTween.IsActive()&&balanceTween.IsPlaying();

        // Default host size is 280 × 62; amounts stretch to narrower page layouts.
        public static CampaignCurrencyHud Attach(RectTransform host,TMP_FontAsset font,Color ink,Color gain,Color loss,bool compact=false)
        {
            var hud=host.GetComponent<CampaignCurrencyHud>()??host.gameObject.AddComponent<CampaignCurrencyHud>();
            hud.Bind(CampaignApp.Ensure(),font,ink,gain,loss,compact);return hud;
        }
        void Bind(CampaignApp app,TMP_FontAsset font,Color textColor,Color gainColor,Color lossColor,bool compactLayout)
        {
            ink=textColor;gain=gainColor;loss=lossColor;compact=compactLayout;
            if(!built)
            {
                group=gameObject.AddComponent<CanvasGroup>();group.interactable=false;group.blocksRaycasts=false;
                var image=gameObject.GetComponent<UnityEngine.UI.Image>()??gameObject.AddComponent<UnityEngine.UI.Image>();image.raycastTarget=false;
                var edge=gameObject.GetComponent<RetroBevel>()??gameObject.AddComponent<RetroBevel>();edge.Configure(2,CampaignRetroTheme.LightEdge,CampaignRetroTheme.DarkEdge);
                var coin=Text("CurrencyUnit",transform,12,12,32,38,26);CampaignLocalization.Bind(coin,"币");coin.alignment=TextAlignmentOptions.Center;
                balance=Text("CurrencyBalance",transform,50,11,218,39,30);balance.alignment=TextAlignmentOptions.MidlineRight;
                balance.rectTransform.anchorMax=new Vector2(1,1);balance.rectTransform.sizeDelta=new Vector2(-62,39);
                for(int i=0;i<floats.Length;i++)
                {
                    var label=Text("CurrencyDelta "+i,transform,55,61,212,28,20);label.alignment=TextAlignmentOptions.MidlineRight;
                    label.rectTransform.anchorMax=new Vector2(1,1);label.rectTransform.sizeDelta=new Vector2(-67,28);
                    label.gameObject.SetActive(false);floats[i]=new FloatingAmount{label=label,rect=label.rectTransform};
                }
                built=true;
            }
            GetComponent<UnityEngine.UI.Image>().color=CampaignRetroTheme.Window;
            GetComponent<RetroBevel>().Configure(2,CampaignRetroTheme.LightEdge,CampaignRetroTheme.DarkEdge);
            foreach(var text in GetComponentsInChildren<TMP_Text>(true))text.font=font;
            var unit=transform.Find("CurrencyUnit").GetComponent<TMP_Text>();unit.color=CampaignRetroTheme.Price;
            unit.rectTransform.anchoredPosition=new Vector2(compact?8:12,compact?-2:-12);
            unit.rectTransform.sizeDelta=new Vector2(compact?20:32,compact?21:38);
            unit.fontSize=unit.fontSizeMax=compact?16:26;unit.fontSizeMin=compact?14:18;
            float left=compact?34:50;
            balance.rectTransform.anchoredPosition=new Vector2(left,compact?-2:-11);
            balance.rectTransform.sizeDelta=new Vector2(-left-12,compact?21:39);
            balance.fontSize=balance.fontSizeMax=compact?16:30;balance.fontSizeMin=compact?14:18;
            foreach(var item in floats)
            {
                item.rect.sizeDelta=new Vector2(-(compact?34:55)-12,compact?24:28);
                item.label.fontSize=item.label.fontSizeMax=compact?17:20;item.label.fontSizeMin=compact?14:18;
            }
            balance.color=ink;
            if(owner==app&&subscribed)return;
            Unsubscribe();owner=app;Subscribe();ResetBalance(app.CurrencyBalance);
        }
        static TMP_Text Text(string name,Transform parent,float x,float y,float width,float height,int size)
        {
            var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rect.SetParent(parent,false);
            rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(width,height);
            var text=rect.gameObject.AddComponent<TextMeshProUGUI>();text.fontSize=size;text.fontSizeMin=18;text.fontSizeMax=size;
            text.enableAutoSizing=true;text.richText=false;text.textWrappingMode=TextWrappingModes.NoWrap;text.raycastTarget=false;return text;
        }
        void Subscribe()
        {
            if(!owner||subscribed)return;
            owner.CurrencyChanged+=OnCurrency;owner.CurrencyReset+=ResetBalance;subscribed=true;
        }
        void Unsubscribe()
        {
            if(owner&&subscribed){owner.CurrencyChanged-=OnCurrency;owner.CurrencyReset-=ResetBalance;}
            subscribed=false;
        }
        void ResetBalance(long value)
        {
            balanceTween?.Kill(false);balanceTween=null;pending.Clear();
            foreach(var item in floats)if(item!=null){item.tween?.Kill(false);item.active=false;item.label.gameObject.SetActive(false);}
            TargetBalance=value;shown=value;LastSequence=owner.CurrencySequence;ReceivedNetDelta=0;
            group.alpha=owner.Session==null?0:1;RenderBalance();
        }
        void RenderBalance()=>balance.text=Math.Round(shown).ToString("N0");
        void OnCurrency(CampaignCurrencyChange change)
        {
            if(change.Sequence<=LastSequence)return;
            LastSequence=change.Sequence;ReceivedCount++;ReceivedNetDelta+=change.Delta;TargetBalance=change.After;group.alpha=1;
            balanceTween?.Kill(false);
            balanceTween=DOTween.To(()=>shown,value=>{shown=value;RenderBalance();},(double)change.After,.48f)
                .SetEase(Ease.OutCubic).SetUpdate(true).SetTarget(this).OnComplete(()=>{shown=TargetBalance;RenderBalance();balanceTween=null;});
            // Back pressure shortens the oldest visual instead of dropping or merging transactions.
            while(pending.Count>=QueueLimit)ShowNext(true);
            pending.Enqueue(change);
            if(ActiveFloatCount==0){ShowNext(false);nextFloat=Time.unscaledTime+.16f;}
        }
        void ShowNext(bool recycle)
        {
            if(pending.Count==0)return;
            FloatingAmount slot=null;int lane=0;
            for(int i=0;i<floats.Length;i++)if(!floats[i].active){slot=floats[i];lane=i;break;}
            if(slot==null&&recycle)
                for(int i=0;i<floats.Length;i++)if(slot==null||floats[i].sequence<slot.sequence){slot=floats[i];lane=i;}
            if(slot==null)return;
            slot.tween?.Kill(false);
            var item=slot;var change=pending.Dequeue();item.sequence=change.Sequence;item.active=true;FloatsShown++;
            item.label.gameObject.SetActive(true);item.label.text=(change.Delta>0?"+":"−")+Math.Abs(change.Delta).ToString("N0");
            var color=change.Delta>0?gain:loss;item.label.color=color;
            float x=(compact?34:55)-lane*8,y=-(compact?28:61)-lane*9,travel=compact?-24:38;item.rect.anchoredPosition=new Vector2(x,y);
            float progress=0;
            item.tween=DOTween.To(()=>progress,value=>
            {
                progress=value;item.rect.anchoredPosition=new Vector2(x,y+travel*value);
                item.label.color=new Color(color.r,color.g,color.b,1-Mathf.Clamp01((value-.42f)/.58f));
            },1,.92f).SetEase(Ease.OutQuad).SetUpdate(true).SetTarget(this).OnComplete(()=>
            {item.active=false;item.tween=null;item.label.gameObject.SetActive(false);});
        }
        void Update()
        {
            if(!built)return;
            group.alpha=owner&&owner.Session!=null?1:0;
            if(pending.Count>0&&Time.unscaledTime>=nextFloat)
            {ShowNext(pending.Count>=QueueLimit/2);nextFloat=Time.unscaledTime+.16f;}
        }
        void OnEnable()
        {
            if(!built||!owner)return;Subscribe();ResetBalance(owner.CurrencyBalance);
        }
        void OnDisable()
        {
            Unsubscribe();balanceTween?.Kill(false);balanceTween=null;
            foreach(var item in floats)if(item!=null){item.tween?.Kill(false);item.active=false;}
        }
        void OnDestroy(){Unsubscribe();DOTween.Kill(this);}
    }
}
