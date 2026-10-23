using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BeetleArena.Campaign;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class RetroPresentationDriver
{
    IEnumerator RunBilingual()
    {
        yield return null;app=CampaignApp.Ensure();yield return Page(CampaignPage.MainMenu);
        Check(CampaignLocalization.Font.faceInfo.familyName=="LiSu","Chinese UI selects LiSu font from asset table");
        yield return LanguageCapture("L01-Menu-ZH");
        var view=FindFirstObjectByType<CampaignView>();view.SendMessage("OpenSettings");yield return null;
        var language=FindObjectsByType<Button>(FindObjectsSortMode.None).First(b=>b.GetComponentInChildren<TMP_Text>()?.text=="简体中文");
        language.onClick.Invoke();yield return null;yield return null;
        Check(CampaignLocalization.English,"actual settings language button selects English locale");
        var prefs=JsonUtility.FromJson<CampaignPreferences>(File.ReadAllText(Path.Combine(app.SaveDirectory,"settings.json")));
        Check(prefs.language==1,"language selection persisted immediately");
        yield return LanguageCapture("L02-Settings-EN");
        for(int i=1;i<4;i++)
        {
            typeof(CampaignView).GetField("settingsTab",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(view,i);
            view.SendMessage("OpenSettings");yield return LanguageCapture("L02-Settings-EN-"+i);
        }
        view.SendMessage("CloseModal");yield return null;yield return LanguageCapture("L03-Menu-EN");
        // Re-initialization reads the saved preference, as a later game launch would.
        CampaignLocalization.Initialize(prefs.language);Check(CampaignLocalization.English,"saved English preference survives initialization");
        app.NewGame(0,"验收玩家");yield return Page(CampaignPage.Greenhouse);yield return Pause(.3f);
        string state=JsonUtility.ToJson(app.Session.State);
        CampaignLocalization.Select(0);CampaignLocalization.Select(1);
        Check(state==JsonUtility.ToJson(app.Session.State),"locale changes do not modify game state");
        yield return LanguageCapture("L04-Greenhouse-EN");
        view=FindFirstObjectByType<CampaignView>();
        for(int i=1;i<3;i++)
        {
            typeof(CampaignView).GetField("greenhouseTab",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(view,i);
            view.SendMessage("QueueRefresh");yield return null;yield return LanguageCapture("L04-Greenhouse-EN-"+i);
        }
        view.SendMessage("OpenGreenhouseRoster");yield return LanguageCapture("L04-Roster-EN");view.SendMessage("CloseModal");
        foreach(var page in new[]{CampaignPage.Catching,CampaignPage.Market,CampaignPage.League,CampaignPage.Auction,CampaignPage.Saves})
        {
            app.Go(page);yield return Page(page);yield return LanguageCapture("L05-"+page+"-EN");
            if(page!=CampaignPage.Market)continue;
            view=FindFirstObjectByType<CampaignView>();
            for(int category=1;category<5;category++)
            {
                typeof(CampaignView).GetField("marketCategory",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(view,category);
                view.SendMessage("QueueRefresh");yield return null;yield return LanguageCapture("L05-Market-EN-"+category);
            }
            view.SendMessage("OpenMarketHelp");yield return LanguageCapture("L05-MarketHelp-EN");view.SendMessage("CloseModal");
            typeof(CampaignView).GetField("marketTab",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(view,1);
            view.SendMessage("QueueRefresh");yield return null;yield return LanguageCapture("L05-BBS-EN");
        }
        app.Go(CampaignPage.MainMenu);yield return Page(CampaignPage.MainMenu);
        view=FindFirstObjectByType<CampaignView>();view.SendMessage("OpenSettings");yield return null;
        // The tab was stored per page; the main menu starts on the Game tab.
        language=FindObjectsByType<Button>(FindObjectsSortMode.None).First(b=>b.GetComponentInChildren<TMP_Text>()?.text=="English");
        language.onClick.Invoke();yield return null;yield return null;
        Check(!CampaignLocalization.English&&CampaignLocalization.Font.faceInfo.familyName=="LiSu","switching back restores Chinese and LiSu immediately");
        yield return LanguageCapture("L06-Settings-ZH");view.SendMessage("CloseModal");
        Check(errors.Count==0,"no runtime errors or missing glyph warnings in either locale");
    }
    IEnumerator LanguageCapture(string name)
    {
        yield return null;yield return null;yield return Capture(name);
        var labels=FindObjectsByType<TMP_Text>(FindObjectsSortMode.None).Where(t=>t.isActiveAndEnabled).ToArray();
        var remaining=CampaignLocalization.English?labels.Where(t=>Regex.IsMatch(t.text,@"[\u4e00-\u9fff]")&&!t.text.Contains("界面语言")&&t.text!="简体中文").Select(t=>t.text.Replace("\n"," / ")).ToArray():Array.Empty<string>();
        Directory.CreateDirectory("Artifacts/Localization");
        File.WriteAllLines("Artifacts/Localization/"+name+"-Untranslated.txt",remaining);
        File.WriteAllLines("Artifacts/Localization/"+name+"-Overflow.txt",labels.Where(t=>t.isTextTruncated).Select(t=>t.text.Replace("\n"," / ")));
    }
}
