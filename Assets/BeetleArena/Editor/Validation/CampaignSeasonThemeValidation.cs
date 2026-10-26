using System;
using System.Collections.Generic;
using System.IO;
using BeetleArena.Campaign;
using BugDuel.Campaign;
using UnityEditor;
using UnityEngine;

// Clock-only fixtures: no scene, live campaign or player preference is changed.
public static class CampaignSeasonThemeValidation
{
    public const string Output="Artifacts/SeasonTheme/Result.txt";

    [MenuItem("Tools/甲壳虫斗/验证/界面与画面/日历季节主题", false, 602)]
    public static void Run()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Output));
        try
        {
            var checks=RunData();
            File.WriteAllLines(Output,checks);
            File.AppendAllText(Output,"COMPLETE PASS: "+checks.Count+" calendar theme contracts.\n");
            Debug.Log("Calendar season theme PASS: "+checks.Count+" checks; "+Output);
        }
        catch(Exception error) { File.WriteAllText(Output,"FAILED "+error);throw; }
    }

    public static IReadOnlyList<string> RunData()
    {
        var checks=new List<string>();
        void Check(bool valid,string label)
        { if(!valid)throw new InvalidOperationException(label);checks.Add("PASS "+label); }

        Check(CampaignRetroTheme.SeasonFor(null)==0,"without a campaign clock, the menu defaults to spring");
        var expected=new[]{0,0,0,1,1,1,2,2,2,3,3,3};
        var clock=new CampaignClock();
        for(int month=1;month<=12;month++)
        {
            clock.elapsedDays=(month-1)*30+15.5;
            Check(clock.Month==month&&CampaignRetroTheme.SeasonFor(clock)==expected[month-1],
                "game month "+month+" selects season "+expected[month-1]);
        }
        for(int boundary=1;boundary<=4;boundary++)
        {
            clock.elapsedDays=boundary*90-.001;
            int before=CampaignRetroTheme.SeasonFor(clock);
            clock.elapsedDays=boundary*90;
            Check(before==boundary-1&&CampaignRetroTheme.SeasonFor(clock)==boundary%4,
                "season changes exactly at elapsed day "+boundary*90);
        }
        Check(clock.Year==2&&clock.Month==1,"the winter-to-spring boundary follows the 360-day game year");

        var state=new CampaignState();
        foreach(int season in new[]{0,1,2,3})
        {
            state.clock.elapsedDays=360+season*90+12.75;
            state.clock.realSecondsPerDay=720;state.clock.speed=32;
            var snapshot=CampaignSnapshots.Copy(state);
            var loaded=JsonUtility.FromJson<CampaignClock>(JsonUtility.ToJson(snapshot.clock));
            Check(loaded.Year==2&&CampaignRetroTheme.SeasonFor(loaded)==season&&loaded.elapsedDays==state.clock.elapsedDays&&
                loaded.realSecondsPerDay==720&&loaded.speed==32,
                "snapshot and saved-clock JSON preserve calendar season "+season+" independently of time speed");
        }
        var active=CampaignApp.Instance?.Session?.State.clock;
        Check(CampaignRetroTheme.Season==CampaignRetroTheme.SeasonFor(active),"the live palette reads the active campaign clock");
        return checks;
    }
}
