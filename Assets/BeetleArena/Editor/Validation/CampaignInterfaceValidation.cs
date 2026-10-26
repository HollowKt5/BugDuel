using System;
using System.IO;
using System.Linq;
using BugDuel.Campaign;
using BeetleArena.Campaign;
using TMPro;
using UnityEditor;
using UnityEngine;

public static class CampaignInterfaceValidation
{
    [MenuItem("Tools/甲壳虫斗/验证/战役流程/育种中心与界面资源", false, 573)]
    public static void Run()
    {
        const string report="Artifacts/InterfaceRework/Data.txt";
        Directory.CreateDirectory(Path.GetDirectoryName(report));File.WriteAllText(report,"");
        void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(label);File.AppendAllText(report,"PASS "+label+"\n");}
        var catalog=CampaignCatalog.Defaults();var original=CampaignSession.New(catalog,1987);
        original.State.beetles[0].id=original.State.selectedId="interface-father";
        original.State.coins=100000;uint random=original.State.random;string before=JsonUtility.ToJson(original.State);
        for(int week=1;week<=30;week++)
        {
            original.State.week=week;
            for(int i=0;i<6;i++)
            {
                var a=original.BreedingOffer("interface-father",i);var b=original.BreedingOffer("interface-father",i);
                if(a.FailurePercent>30||a.FailurePercent<0||a.Fee!=b.Fee||a.LuckAdjustment!=b.LuckAdjustment||a.Mother.id!=b.Mother.id)throw new InvalidOperationException("Unstable offer");
                if(i>=2&&a.Fee<=original.BreedingOffer("interface-father",i-2).Fee)throw new InvalidOperationException("Fee tiers");
            }
        }
        original.State.week=1;Check(original.State.random==random&&JsonUtility.ToJson(original.State)==before,"quoting is read-only, repeatable and bounded by 30%; fees rise with tier");
        bool success=false,failure=false;
        for(uint seed=1;seed<300&&(!success||!failure);seed++)
        {
            var s=new CampaignSession(CampaignApp.CloneState(original.State),catalog);s.State.random=unchecked(seed*2654435761u);
            var offer=s.BreedingOffer("interface-father",0);int count=s.State.beetles.Count;long funds=s.State.coins;
            bool result=s.HireBreeding("interface-father",0,1);
            Check(s.State.coins==funds-offer.Fee&&s.Beetle("interface-father").cooldownWeek==2,"service charges exactly once and rests the sire");
            Check(!s.State.beetles.Any(b=>b.id==offer.Mother.id)&&!s.State.storedBeetles.Any(b=>b.id==offer.Mother.id),"center mother never enters player ownership");
            if(result)
            {success=true;var egg=s.State.beetles.Last();Check(s.State.beetles.Count==count+1&&egg.stage==CampaignLifeStage.Egg&&egg.mother==offer.Mother.id&&egg.father=="interface-father","successful service creates one egg with center lineage");}
            else {failure=true;Check(s.State.beetles.Count==count,"failed service charges the displayed fee without creating an egg");}
            s.Validate();
        }
        Check(success&&failure,"both random outcomes exercised");
        foreach(int invalid in new[]{0,1,2,3})
        {
            var s=new CampaignSession(CampaignApp.CloneState(original.State),catalog);
            if(invalid==0)s.State.coins=0;if(invalid==1)s.Beetle("interface-father").female=true;
            if(invalid==2)s.Beetle("interface-father").cooldownWeek=2;
            string initial=JsonUtility.ToJson(s.State);bool refused=false;
            try{s.HireBreeding("interface-father",0,invalid==3?0:1);}catch(CampaignRuleException){refused=true;}
            Check(refused&&initial==JsonUtility.ToJson(s.State),"invalid service request leaves state untouched: "+invalid);
        }
        var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Localization/Fonts/English.asset");
        Check(font&&font.faceInfo.familyName=="Tahoma"&&font.HasCharacters("Betles Duel")&&font.atlasPopulationMode==AtlasPopulationMode.Static,"Tahoma Latin glyphs are baked and available without OS fonts at runtime");
        Check(EditorBuildSettings.scenes.Any(s=>s.enabled&&s.path.EndsWith("CampaignBreeding.unity")),"independent breeding scene included in build");
        File.AppendAllText(report,"COMPLETE PASS\n");
    }
}
