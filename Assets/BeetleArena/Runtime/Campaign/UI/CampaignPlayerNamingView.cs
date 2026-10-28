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
            var dialog=OpenModal(650,338,titleBar:false,blurBackground:false);
            modal.GetComponent<UnityEngine.UI.Image>().color=CampaignRetroTheme.Window;
            dialog.name="PlayerNaming";
            dialog.GetComponent<UnityEngine.UI.Image>().color=CampaignRetroTheme.Window;
            dialog.GetComponent<RetroBevel>().enabled=false;
            Label(dialog,creating?"玩家名字":required?"补充玩家名字":"修改玩家名字",26,30,598,40,28,accent);
            var input=EditableField(dialog,"玩家名字",creating?"":State.playerName??"",86,CampaignSession.BeetleNameLimit);
            Label(dialog,"1–24 个字符",26,148,598,38,17,muted);
            dialogNotice=Label(dialog,"",26,193,598,57,17,muted);
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
            Btn(dialog,creating?"开始旅程":required?"保存并继续":"保存名字",26,267,291,45,Save);
            Btn(dialog,required?"返回主菜单":"取消",329,267,291,45,Cancel);
            input.onSubmit.AddListener(_=>Save());input.Select();input.ActivateInputField();
        }
    }
}
