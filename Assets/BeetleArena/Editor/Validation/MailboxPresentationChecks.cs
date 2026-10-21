using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BeetleArena.Campaign;
using BugDuel.Campaign;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class RetroPresentationDriver
{
    void MailClick(string name)
    {
        var button=FindObjectsByType<Button>(FindObjectsSortMode.None).First(b=>b.isActiveAndEnabled&&b.name==name);
        Check(button.IsInteractable(),"button is interactable: "+name);button.onClick.Invoke();
    }
    IEnumerator MailFrame(string name)
    {
        yield return Capture(name);
        var view=FindFirstObjectByType<CampaignView>();var modal=(RectTransform)typeof(CampaignView).GetField("modal",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view);
        var labels=modal.GetComponentsInChildren<TMP_Text>().Where(t=>t.isActiveAndEnabled).ToArray();
        string[] truncated=labels.Where(t=>t.isTextTruncated).Select(t=>t.text).ToArray();
        string[] untranslated=CampaignLocalization.English?labels.Where(t=>Regex.IsMatch(t.text,@"[\u4e00-\u9fff]")).Select(t=>t.text).ToArray():Array.Empty<string>();
        Directory.CreateDirectory("Artifacts/Mailbox");File.WriteAllLines("Artifacts/Mailbox/"+name+"-Layout.txt",truncated.Concat(untranslated));
        Check(truncated.Length==0&&untranslated.Length==0,"mail window has no clipped or untranslated text: "+name);
    }
    IEnumerator RunMailbox()
    {
        yield return null;app=CampaignApp.Ensure();yield return Page(CampaignPage.MainMenu);
        app.NewGame(0);yield return Page(CampaignPage.Greenhouse);
        app.Execute(s=>{foreach(var b in s.State.beetles)s.House(b.id,"recovery");});yield return Idle();
        app.Go(CampaignPage.Market);yield return Page(CampaignPage.Market);
        var view=FindFirstObjectByType<CampaignView>();
        typeof(CampaignView).GetField("marketCategory",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(view,4);view.SendMessage("QueueRefresh");yield return null;yield return null;
        long coins=app.Session.State.coins;
        // Open the first food card, then submit the actual market purchase button.
        var details=FindObjectsByType<Button>(FindObjectsSortMode.None).Where(b=>b.isActiveAndEnabled&&b.name=="查看参数档案...").OrderBy(b=>b.transform.parent.parent.GetSiblingIndex()).First();details.onClick.Invoke();yield return null;
        MailClick("购买并寄送");yield return Idle();
        Check(app.Session.State.coins==coins-15&&app.Session.InventoryCount(CampaignItems.Food(0))==0&&app.Session.State.mail.Last().Pending,"real market food purchase waits for mail claim");
        string mailId=app.Session.State.mail.Last().id;
        app.Go(CampaignPage.Greenhouse);yield return Page(CampaignPage.Greenhouse);
        var mailButton=FindObjectsByType<Button>(FindObjectsSortMode.None).First(b=>b.isActiveAndEnabled&&b.name.StartsWith("邮箱 ("));mailButton.onClick.Invoke();yield return null;
        MailClick("查看 · 有附件");yield return Idle();yield return null;
        yield return MailFrame("M01-Mail-Unclaimed-ZH");
        var before=app.Session;string temporary=app.SlotPath(0)+".tmp";
        using(var locked=new FileStream(temporary,FileMode.Create,FileAccess.ReadWrite,FileShare.None))
        {
            MailClick("领取全部附件");Check(!app.Execute(s=>s.ClaimMail(mailId)),"repeated claim blocked while commit is pending");yield return Idle();
            Check(app.Session==before&&app.Session.InventoryCount(CampaignItems.Food(0))==0&&app.Session.State.mail.Last().Pending&&app.Message.Contains("未提交"),"failed disk commit preserves both attachment and inventory");
        }
        if(File.Exists(temporary))File.Delete(temporary);
        MailClick("领取全部附件");yield return Idle();yield return null;
        Check(app.Session.InventoryCount(CampaignItems.Food(0))==1&&app.Message.Contains("已加入仓库"),"claim shows storage receipt and grants one item");
        yield return MailFrame("M02-Mail-Claimed-ZH");
        var saved=new CampaignSaveRepository(app.SaveDirectory).Read(0);
        Check(saved.state.mail.First(m=>m.id==mailId).claimed&&saved.state.inventory.Single().quantity==1,"claim and item persist in the same save");
        MailClick("打开仓库");yield return null;
        yield return MailFrame("M03-Storage-ZH");
        coins=app.Session.State.coins;MailClick("投喂当前甲虫");yield return Idle();yield return null;
        Check(app.Session.InventoryCount(CampaignItems.Food(0))==0&&app.Session.State.coins==coins&&app.Session.Beetle(app.Session.State.selectedId).meal!=null,"warehouse food use consumes one item without payment");
        MailClick("打开邮箱");yield return null;MailClick("联系人");yield return null;
        CampaignLocalization.Select(1);view=FindFirstObjectByType<CampaignView>();view.SendMessage("OpenMailbox");yield return null;
        yield return MailFrame("M04-Contacts-EN");
        MailClick("收件箱");yield return null;
        // The selected receipt remains saved across folder changes and page reloads.
        typeof(CampaignView).GetField("selectedMail",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(view,mailId);view.SendMessage("OpenMailbox");yield return null;
        yield return MailFrame("M05-Mail-EN");
        MailClick("删除邮件");yield return Idle();yield return null;MailClick("已删除");yield return null;MailClick("查看邮件");yield return Idle();yield return null;
        Check(app.Session.State.mail.First(m=>m.id==mailId).deleted,"delete moves receipt into Deleted folder");
        MailClick("恢复到收件箱");yield return Idle();yield return null;
        Check(!app.Session.State.mail.First(m=>m.id==mailId).deleted,"deleted message can be restored");
        view.SendMessage("CloseModal");CampaignLocalization.Select(0);
        Check(errors.Count==0,"no mailbox runtime errors or missing glyphs");
    }
}
