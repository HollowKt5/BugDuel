using System;
using System.Collections.Generic;
using System.Linq;
using BugDuel.Campaign;
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
#endif

public static class CampaignSpecimenLabelValidation
{
    static readonly List<string> checks=new List<string>();
    static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);checks.Add("PASS "+message);}
    static string Decode(string value)=>CampaignDisplayText.Localize(value,s=>s);
    static bool Protected(string value)=>value.StartsWith("\uE000",StringComparison.Ordinal);
    public static IReadOnlyList<string> RunDomain()
    {
        checks.Clear();var s=CampaignSession.New(CampaignCatalog.Defaults(),2841);s.State.coins=100000;
        var b=s.State.beetles[0];b.dead=true;b.hearts=0;
        var p=s.Specimen(s.PreserveDead(b.id,"第 1 年 01 月 01 日"));
        Check(!p.customLabelName&&!p.customSpeciesLabel&&!p.customDateLabel&&p.plaqueVersion==1,"automatic plaque fields remain eligible for translation");
        Check(CampaignDisplayText.Localize(CampaignDisplayText.SpecimenSpecies(p),v=>v==p.speciesLabel?"Translated species":v)=="Translated species","automatic species reaches the translation callback");
        s.EditSpecimen(p.id,p.labelName,p.speciesLabel,p.dateLabel,2,45);
        Check(!p.customLabelName&&!p.customSpeciesLabel&&!p.customDateLabel,"frame and rotation edits do not claim authorship of plaque text");
        s.EditSpecimen(p.id,"死亡",p.speciesLabel,p.dateLabel,2,45);
        Check(p.customLabelName&&!p.customSpeciesLabel&&!p.customDateLabel,"changing only the name leaves species and date automatic");
        Check(CampaignDisplayText.Localize(CampaignDisplayText.SpecimenName(p),v=>v.Replace("死亡","Dead"))=="死亡","a custom name equal to a system word remains literal");
        s.EditSpecimen(p.id,b.name,"自定种类",p.dateLabel,2,45);
        Check(p.customLabelName&&p.customSpeciesLabel&&!p.customDateLabel&&Protected(CampaignDisplayText.SpecimenName(p)),"restoring the default name does not remove prior player authorship");
        s.EditSpecimen(p.id,p.labelName,p.speciesLabel,"春天",2,45);
        var copy=CampaignSnapshots.Copy(s.State).specimens.Single();
        Check(copy.customLabelName&&copy.customSpeciesLabel&&copy.customDateLabel&&Decode(CampaignDisplayText.SpecimenDate(copy))=="春天","snapshots preserve all three independent flags and raw text");
        var legacy=CampaignSnapshots.Copy(s.State);var old=legacy.specimenStock[0];old.plaqueVersion=0;
        old.labelName=old.beetle.name;old.speciesLabel=s.Catalog.Species(old.beetle.speciesId).name;old.dateLabel="第 "+old.preparedWeek+" 周";
        var custom=legacy.specimens.Single();custom.plaqueVersion=0;custom.labelName="旧档自填";
        var restored=new CampaignSession(legacy,s.Catalog);
        Check(!old.customLabelName&&!old.customSpeciesLabel&&!old.customDateLabel&&old.plaqueVersion==1,"legacy generated plaques migrate as automatic");
        Check(custom.customLabelName&&custom.customSpeciesLabel&&custom.customDateLabel,"legacy fields differing from their source migrate as authored");
        var sale=restored.State.specimenStock[0];sale.customLabelName=true;sale.labelName="死亡";
        restored.BuySpecimen(sale.id);var attachment=restored.State.mail.Last().attachments.Single();
        Check(attachment.literalName&&attachment.specimens.Single().customLabelName&&Decode(CampaignDisplayText.Name(attachment))=="死亡","shop delivery preserves custom text and attachment provenance");
        restored.ClaimMail(restored.State.mail.Last().id);
        Check(restored.Specimen(sale.id).customLabelName,"claiming a specimen preserves its provenance");
        var lot=restored.State.marketAuctions.First(a=>a.Specimen!=null);lot.Specimen.customSpeciesLabel=true;lot.Specimen.speciesLabel="死亡";
        restored.BidMarketAuction(lot.id,lot.expectedPrice);while(!lot.closed)restored.AdvanceMarketAuction(lot.id);
        var award=restored.State.mail.Last().attachments.Single().specimens.Single();
        Check(lot.won&&award.customSpeciesLabel&&Decode(CampaignDisplayText.SpecimenSpecies(award))=="死亡","a settled auction delivers the same independent plaque provenance");
        restored.Validate();return checks;
    }
#if UNITY_EDITOR
    [MenuItem("Tools/甲壳虫斗/验证/本地化/标本铭牌来源", false, 642)]
    public static void Run()
    {
        RunDomain();var s=CampaignSession.New(CampaignCatalog.Defaults(),2841);
        var p=s.State.specimenStock[0];p.customLabelName=p.customSpeciesLabel=p.customDateLabel=true;p.labelName="死亡";p.speciesLabel="名字";p.dateLabel="春天";
        var loaded=JsonUtility.FromJson<CampaignState>(JsonUtility.ToJson(CampaignSnapshots.Copy(s.State)));
        var result=new CampaignSession(loaded,s.Catalog).State.specimenStock[0];
        Check(result.plaqueVersion==1&&result.customLabelName&&result.customSpeciesLabel&&result.customDateLabel&&Decode(CampaignDisplayText.SpecimenName(result))=="死亡","Unity JSON round trip preserves flags and player text without reclassification");
        Directory.CreateDirectory("Artifacts/SpecimenLabels");File.WriteAllLines("Artifacts/SpecimenLabels/Result.txt",checks);Debug.Log("Specimen label provenance PASS: "+checks.Count);
    }
#endif
}
