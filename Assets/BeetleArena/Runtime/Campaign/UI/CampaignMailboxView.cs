using System;
using System.Linq;
using BugDuel.Campaign;
using TMPro;
using UnityEngine;

namespace BeetleArena.Campaign
{
    public sealed partial class CampaignView
    {
        int mailFolder,storageOverlay; // 0 inbox, 1 unread, 2 attachments, 3 deleted, 4 contacts
        string selectedMail,selectedContact="keeper",contactFilter;

        void OpenMailbox()
        {
            if(Session==null)return;
            var p=MarketDialog("虫舍邮箱",1420,800);storageOverlay=1;
            string[] tabs={"收件箱","未读邮件","待领取","已删除","联系人"};
            for(int i=0;i<tabs.Length;i++)
            {
                int folder=i;MButton(p,tabs[i],16+i*155,48,145,32,()=>{mailFolder=folder;contactFilter=null;selectedMail=null;OpenMailbox();},mailFolder!=i,17);
            }
            MButton(p,"打开仓库",1198,48,202,32,OpenWarehouse,true,17);
            MText(p,$"未读 {Session.UnreadMail} 封  ·  待领取 {Session.PendingMail} 封",18,90,900,28,17,CampaignRetroTheme.Muted);
            MBox("MessageList",p,16,126,408,600,CampaignRetroTheme.Panel,CampaignRetroTheme.Border,1);
            MBox("MessageDetails",p,436,126,968,600,CampaignRetroTheme.Panel,CampaignRetroTheme.Border,1);
            if(mailFolder==4)DrawMailContacts(p);else DrawMailInbox(p);
            dialogNotice=MStatus(p,26,749,1368,29,17,app.Message);
            SelectFirst(p);
        }
        void DrawMailInbox(RectTransform p)
        {
            var mails=State.mail.Where(m=>mailFolder==3?m.deleted:!m.deleted)
                .Where(m=>mailFolder!=1||!m.read).Where(m=>mailFolder!=2||m.Pending)
                .Where(m=>contactFilter==null||m.senderId==contactFilter).Reverse().ToArray();
            string heading=contactFilter==null?$"邮件列表 · {mails.Length} 封":CampaignMailContacts.Find(contactFilter).name;
            MText(p,heading,28,139,382,32,20,accent,true);
            var list=Column(26,181,388,531,p);
            foreach(var mail in mails)
            {
                var m=mail;var row=Panel(list.content,0,list.y,370,112,true);list.y+=120;list.content.sizeDelta=new Vector2(388,list.y);
                if(m.id==selectedMail)row.GetComponent<UnityEngine.UI.Image>().color=CampaignRetroTheme.Notice;
                MText(row,(m.read?"":"● ")+m.subject,12,7,346,30,18,ink,!m.read);
                MText(row,CampaignMailContacts.Find(m.senderId).name+$" · 第 {m.week} 周",12,39,346,26,15,muted);
                MButton(row,m.Pending?"查看 · 有附件":"查看邮件",12,74,346,29,()=>SelectMail(m.id),true,16);
            }
            if(mails.Length==0)MText(p,"暂无邮件",38,222,362,90,22,muted);
            // Keep the open message visible when marking it read removes it from the Unread filter.
            var current=State.mail.FirstOrDefault(m=>m.id==selectedMail&&(mailFolder==3?m.deleted:!m.deleted));
            if(current==null)
            {
                MText(p,"选择一封邮件",478,380,884,55,32,accent,true);return;
            }
            var contact=CampaignMailContacts.Find(current.senderId);
            MText(p,current.subject,458,143,924,50,27,accent,true);
            MText(p,$"发件人：{contact.name}  <{CampaignLocalization.Literal(contact.address)}>\n收件人：{Session.PlayerSignature}    ·    第 {current.week} 周",458,204,924,66,18,muted);
            MBox("MailDivider",p,458,284,924,1,CampaignRetroTheme.Border);
            var detail=Column(458,299,924,332,p);
            string message=Session.MailBody(current);
            var body=MText(detail.content,message,4,4,892,166,20);
            float bodyHeight=Mathf.Max(100,body.GetPreferredValues(CampaignLocalization.Text(message),892,0).y+16);
            body.rectTransform.sizeDelta=new Vector2(892,bodyHeight);detail.y=bodyHeight+20;
            foreach(var attachment in current.attachments)
            {
                var a=attachment;var card=Card(detail,94);
                MText(card,$"{CampaignLocalization.Name(a)} ×{a.quantity}",16,12,866,30,22,accent,true);
                MText(card,a.itemId==CampaignItems.Specimen?(current.claimed?"已领取 · 标本已存入标本室收藏库":"标本附件 · 领取后前往标本室布展"):current.claimed?"已领取 · 附件已存入仓库":a.itemId==CampaignItems.Beetle?"活体附件 · 领取后从仓库安排入住温室":"物品附件 · 领取后存入仓库",16,52,866,27,17,muted);
            }
            if(current.attachments.Count==0){MText(detail.content,"无附件",4,detail.y,892,42,19,muted);detail.y+=54;}
            detail.content.sizeDelta=new Vector2(924,Mathf.Max(detail.y,244));
            if(current.deleted)
            {
                MButton(p,"恢复到收件箱",458,667,440,39,()=>MailboxCommand(s=>s.RestoreMail(current.id)),true,18);
                MButton(p,"永久删除",912,667,470,39,()=>MailboxCommand(s=>s.PurgeMail(current.id)),!current.Pending,18);
                if(current.Pending)MText(p,"未领取的附件受保护：恢复邮件后即可领取。",458,633,924,27,17,muted);
            }
            else
            {
                MButton(p,current.claimed?"附件已领取":"领取全部附件",458,667,440,39,()=>MailboxCommand(s=>s.ClaimMail(current.id)),current.Pending,18);
                MButton(p,"删除邮件",912,667,470,39,()=>MailboxCommand(s=>s.DeleteMail(current.id)),true,18);
            }
        }
        void SelectMail(string id)
        {
            selectedMail=id;var mail=State.mail.First(m=>m.id==id);
            if(mail.read)OpenMailbox();else MailboxCommand(s=>s.MarkMailRead(id));
        }
        void DrawMailContacts(RectTransform p)
        {
            MText(p,"通讯录",28,139,382,32,20,accent,true);
            var list=Column(26,181,388,531,p);
            foreach(var contact in CampaignMailContacts.All)
            {
                var c=contact;var card=Card(list,90);
                MButton(card,c.name,10,10,350,36,()=>{selectedContact=c.id;OpenMailbox();},true,20);
                MText(card,CampaignLocalization.Literal(c.address),12,54,346,25,14,muted);
            }
            var person=CampaignMailContacts.Find(selectedContact)??CampaignMailContacts.All[0];
            MText(p,person.name,470,164,908,58,32,accent,true);
            MText(p,CampaignLocalization.Literal(person.address),470,242,908,35,22,muted);
            MText(p,$"往来邮件：{State.mail.Count(m=>m.senderId==person.id&&!m.deleted)} 封",470,316,908,40,22,muted);
            MButton(p,"查看往来邮件",470,392,908,52,()=>{contactFilter=person.id;mailFolder=0;selectedMail=null;OpenMailbox();},true,20);
        }
        void MailboxCommand(Action<CampaignSession> command)
        {
            int overlay=storageOverlay;
            app.Execute(command,()=>
            {
                // Closing a window during the disk commit must not reopen it behind the player.
                if(!this||!modal||storageOverlay!=overlay)return;
                if(overlay==1)OpenMailbox();else OpenWarehouse();
            });
        }
    }
}
