using System;
using BugDuel.Campaign;
using UnityEngine;

namespace BeetleArena.Campaign
{
    public sealed partial class CampaignView
    {
        void HandlePlayerIdentityPrompt()
        {
            if(app.Busy||modal)return;
            if(app.PlayerNameRequestSlot.HasValue)
            {
                int slot=app.PlayerNameRequestSlot.Value;app.CancelNewGameNaming();OpenPlayerNaming(slot);return;
            }
            if(app.NeedsPlayerName&&page!=CampaignPage.MainMenu&&page!=CampaignPage.Saves)OpenPlayerNaming();
        }

        void OpenPlayerNaming(int? newSlot=null,Action returnTo=null)
        {
            bool creating=newSlot.HasValue,required=!creating&&app.NeedsPlayerName;
            var dialog=OpenModal(650,392,titleBar:false,blurBackground:false);
            modal.GetComponent<UnityEngine.UI.Image>().color=Color.black;
            dialog.name="PlayerNaming";
            dialog.GetComponent<UnityEngine.UI.Image>().color=Color.black;
            dialog.GetComponent<RetroBevel>().enabled=false;
            // Identity has its own black stage, independent of the current window theme.
            ink=Color.white;muted=new Color(.72f,.76f,.8f);accent=new Color(.45f,.7f,1);
            Label(dialog,creating?"为新旅程起名":required?"补充玩家名字":"修改玩家名字",26,30,598,40,28,accent);
            Label(dialog,required?"这份旧存档还没有玩家名字。请补充后继续。":"这个名字将用于邮件称呼、挂牌和竞拍署名。",26,79,598,39,18,muted);
            var input=EditableField(dialog,"玩家名字",creating?"":State.playerName??"",130,CampaignSession.BeetleNameLimit);
            Label(dialog,"1–24 个字符 · 可在设置中修改 · 与甲虫名字分别保存",26,192,598,38,17,muted);
            dialogNotice=Label(dialog,"",26,237,598,57,17,muted);
            void Done()
            {
                // NewGame already fades into the greenhouse. Keep this screen visible
                // until its scene unloads instead of flashing the save selector underneath.
                if(creating)return;
                app.RevealAfterPlayerNaming(()=>
                {
                    if(returnTo!=null)returnTo();else CloseModal();
                    QueueRefresh();
                });
            }
            void Cancel()
            {
                CloseModal();
                if(required)app.Go(CampaignPage.MainMenu);else returnTo?.Invoke();
            }
            void Save()
            {
                if(creating)app.NewGame(newSlot.Value,input.text,Done);
                else app.Execute(session=>session.RenamePlayer(input.text),Done);
            }
            modalBack=Cancel;
            Btn(dialog,creating?"开始旅程":required?"保存并继续":"保存名字",26,321,291,45,Save);
            Btn(dialog,required?"返回主菜单":"取消",329,321,291,45,Cancel);
            foreach(var control in dialog.GetComponentsInChildren<UnityEngine.UI.Selectable>())
                control.targetGraphic.color=new Color(.1f,.13f,.17f);
            input.onSubmit.AddListener(_=>Save());input.Select();input.ActivateInputField();
        }
    }
}
