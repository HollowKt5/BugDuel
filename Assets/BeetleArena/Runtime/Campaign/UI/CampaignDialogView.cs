using System;
using System.Linq;
using System.Runtime.CompilerServices;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UI=UnityEngine.UI;

namespace BeetleArena.Campaign
{
    public sealed partial class CampaignView
    {
        TMP_Text dialogNotice;
        CampaignModalBackdrop modalBackdrop;
        CampaignDialogMotion dialogMotion,closingDialog;
        string dialogContent;

        void Confirm(string text,Action action)
        {
            var panel=OpenModal(670,290);
            Label(panel,text,32,32,606,140,25);
            Btn(panel,"确定",32,205,290,52,()=>{CloseModal();action();});
            Btn(panel,"取消",340,205,290,52,CloseModal);
        }
        // Blur the existing composed page behind the window; the window itself stays sharp.
        RectTransform OpenModal(float w,float h,bool titleBar=true,bool blurBackground=true,[CallerMemberName]string content=null)
        {
            ink=CampaignRetroTheme.Ink;muted=CampaignRetroTheme.Muted;accent=CampaignRetroTheme.Accent;
            bool sameWindow=modal && dialogMotion && dialogContent==content && dialogMotion.Panel.sizeDelta==new Vector2(w,h);
            if(closingDialog){closingDialog.Discard();closingDialog=null;}
            // Keep the page capture across window replacements (including the black
            // player-name page), so returning through a black transition cannot capture it.
            CloseModal(true);
            focusBeforeModal=EventSystem.current?EventSystem.current.currentSelectedGameObject:null;
            app.Modal=true;
            if(pageInteraction)
            {
                pageInteraction.interactable=false;
                pageInteraction.blocksRaycasts=false;
            }
            if(blurBackground && !modalBackdrop)
            {
                var background=Rect("DialogBackdrop",canvas,0,0,1600,900);Stretch(background);
                modalBackdrop=background.gameObject.AddComponent<CampaignModalBackdrop>();
                modalBackdrop.Initialize(backdrop?backdrop.blurShader:Shader.Find("Hidden/BugDuel/CampaignBlur"));
            }
            if(blurBackground)modalBackdrop.transform.SetAsLastSibling();
            if(EventSystem.current)EventSystem.current.SetSelectedGameObject(null);
            modal=Rect("Modal",canvas,0,0,1600,900);
            Stretch(modal);
            var blocker=modal.gameObject.AddComponent<UI.Image>();
            blocker.color=Color.clear;
            blocker.raycastTarget=true;
            var foreground=modal.gameObject.AddComponent<CanvasGroup>();
            if(blurBackground)modalBackdrop.Show(foreground);
            var panel=Panel(modal,0,0,w,h);
            panel.name="DialogPanel";
            panel.anchorMin=panel.anchorMax=panel.pivot=new Vector2(.5f,.5f);
            panel.anchoredPosition=Vector2.zero;
            if(titleBar)
            {
                var title=Panel(panel,3,3,w-6,18);
                title.name="DialogTitleBar";
                title.GetComponent<UI.Image>().color=CampaignRetroTheme.Frame;
                Component<RetroBevel>(title).Configure(0,Color.clear,Color.clear,true,CampaignRetroTheme.TitleTop,CampaignRetroTheme.TitleBottom);
            }
            dialogContent=content;
            dialogMotion=modal.gameObject.AddComponent<CampaignDialogMotion>();
            dialogMotion.Open(panel,foreground,blurBackground?modalBackdrop:null,!sameWindow);
            return panel;
        }
        void CloseModal()=>CloseModal(false);
        void CloseModal(bool keepBackdrop)
        {
            if(modalBackdrop)
            {
                if(keepBackdrop)modalBackdrop.Retain();
                else modalBackdrop.Hide(isActiveAndEnabled);
            }
            modalBack=null;
            storageOverlay=0;
            dialogNotice=null;
            closingModal=true;
            var operation=rebind;
            rebind=null;
            operation?.Cancel();
            operation?.Dispose();
            if(settingsOpen && app)
            {
                settingsOpen=false;
                BeetleDisplayPreferences.Save();
                app.SavePreferences();
            }
            if(modal)
            {
                if(keepBackdrop || !isActiveAndEnabled || !modal.gameObject.activeInHierarchy)dialogMotion.Discard();
                else {closingDialog=dialogMotion;closingDialog.Close();}
                dialogMotion=null;dialogContent=null;
                modal=null;
            }
            if(app)app.Modal=false;
            if(pageInteraction){pageInteraction.interactable=!app.Busy;pageInteraction.blocksRaycasts=true;}
            if(focusBeforeModal && focusBeforeModal.activeInHierarchy && EventSystem.current)EventSystem.current.SetSelectedGameObject(focusBeforeModal);
            focusBeforeModal=null;closingModal=false;
            if(page==CampaignPage.Greenhouse&&root)ApplySidebar();
        }
        static void SelectFirst(Transform parent) { var b=parent.GetComponentsInChildren<UI.Button>().FirstOrDefault(x=>x.interactable);if(b&&EventSystem.current)EventSystem.current.SetSelectedGameObject(b.gameObject); }
    }
}
