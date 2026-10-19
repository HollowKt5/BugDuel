using System.IO;
using BeetleArena;
using BeetleArena.Campaign;
using BugDuel.Domain;
using UnityEngine;

// Runs after the real specimen's LateUpdate, including pauses, switching individuals and hot updates.
public sealed class CareContactMonitor
{
    public int Samples { get; private set; }
    public string Failure { get; private set; }
    float maxError,maxSole;
    int minSupport=6;
    public void Sample()
    {
        var specimen=Object.FindFirstObjectByType<CampaignSpecimen>();var app=CampaignApp.Instance;
        if(!specimen||!specimen.selected||!specimen.Care||app?.Session==null||Failure!=null)return;
        var resident=specimen.Care.Find(app.Session.State.selectedId);var feet=specimen.Feet;
        if(resident==null||feet==null||feet.SupportCount==0)return;
        if(resident.Brain.State==CareState.Struggling||resident.Brain.State==CareState.Recovering||resident.Brain.State==CareState.Dead)return;
        Samples++;minSupport=Mathf.Min(minSupport,feet.SupportCount);maxError=Mathf.Max(maxError,feet.MaximumError);
        var soil=GameObject.Find("SoilBed").GetComponent<Renderer>().bounds;
        for(int i=0;i<6;i++)
        {
            Vector3 foot=feet.Foot(i);
            if(feet.IsSupport(i))maxSole=Mathf.Max(maxSole,Mathf.Abs(foot.y-feet.SoleHeight(i)));
            if(foot.x<soil.min.x||foot.x>soil.max.x||foot.z<soil.min.z||foot.z>soil.max.z)Failure="Foot left soil bed: "+foot;
        }
        if(feet.SupportCount<3||feet.MaximumError>.02f||maxSole>.015f)
            Failure=$"state={resident.Brain.State}; support={feet.SupportCount}; IK={feet.MaximumError}; sole={maxSole}";
    }
    public void SaveReport()
    {
        Directory.CreateDirectory("Artifacts/Campaign/Care");
        File.WriteAllText("Artifacts/Campaign/Care/PlayContact.txt",$"samples={Samples}; min support={minSupport}; max IK={maxError:F6}; max sole={maxSole:F6}\n"+(Failure??"PASS"));
    }
}
