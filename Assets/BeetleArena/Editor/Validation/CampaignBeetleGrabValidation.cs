using System;
using System.IO;
using System.Text;
using BeetleArena;
using BeetleArena.Campaign;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CampaignBeetleGrabValidation
{
    [MenuItem("Tools/甲壳虫斗/验证/养殖/提起与安全落地", false, 547)]
    public static void Run()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before grab validation.");
        var report=new StringBuilder();int checks=0;
        void Check(bool value,string detail)
        {if(!value)throw new InvalidOperationException(detail);report.AppendLine("PASS "+(++checks)+" "+detail);}
        var preview=EditorSceneManager.NewPreviewScene();
        try
        {
            var area=new CareWalkArea(new Rect(-5,-4,10,8),.6f,new[]{new Rect(-.5f,-1,1,2)});
            var start=new Vector2(-2,0);
            var blocked=CampaignBeetleGrabController.ResolveTarget(area,start,new Vector2(2,0));
            Check(area.IsClear(blocked)&&area.SegmentClear(start,blocked)&&blocked.x<-.5f,
                "dragging across a decoration stops before its body-clearance boundary");
            var current=start;
            foreach(var goal in new[]{new Vector2(-2,2.2f),new Vector2(2,2.2f),new Vector2(2,0)})
            {
                var next=CampaignBeetleGrabController.ResolveTarget(area,current,goal);
                Check(area.IsClear(next)&&area.SegmentClear(current,next),"dragging around scenery follows clear swept segments");current=next;
            }
            Check(Vector2.Distance(current,new Vector2(2,0))<.001f,"a route around the obstacle reaches the requested position");
            var edge=CampaignBeetleGrabController.ResolveTarget(area,current,new Vector2(1000,-1000));
            Check(area.IsClear(edge)&&area.SegmentClear(current,edge),"off-screen drops remain safely inside the enclosure");
            var invalid=CampaignBeetleGrabController.ResolveTarget(area,edge,new Vector2(float.NaN,0));
            Check(invalid==edge,"invalid pointer coordinates preserve the last safe position");
            var motor=new CareLocomotion(12,area);motor.PlaceAt(new Vector2(.1f,0));
            Check(area.IsClear(motor.Position)&&motor.Speed==0,"placing a beetle on an occupied point resolves to safe stationary ground");

            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BeetleArena/Generated/ArticulatedBeetle.prefab");
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,preview);
            instance.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            var rig=instance.GetComponentInChildren<BeetleProceduralAnimator>();var pose=new CarePosePresenter(rig);pose.Reset(false,false);
            instance.transform.position=Vector3.up*.72f;pose.PresentHeld(1f/60,0);
            var calm=rig.legs[0].hip.localRotation;float motion=0,minFoot=float.PositiveInfinity;
            for(int i=0;i<120;i++)
            {
                pose.PresentHeld(1f/60,Mathf.Sin(i/120f*Mathf.PI));
                motion=Mathf.Max(motion,Quaternion.Angle(calm,rig.legs[0].hip.localRotation));
                for(int leg=0;leg<rig.legs.Length;leg++)minFoot=Mathf.Min(minFoot,pose.Feet.Foot(leg).y);
            }
            Check(motion>5&&minFoot>0&&pose.Feet.SupportCount==0,"airborne struggle moves dangling legs without pretending they support the ground");
            instance.transform.position=Vector3.zero;pose.Reset(false,false);
            Check(pose.Feet.SupportCount==6&&pose.Feet.MaximumError<.02f,"landing resets foot anchors and restores grounded support");
            report.AppendLine("COMPLETE PASS");Debug.Log("Beetle grab PASS: "+checks+" checks.");
        }
        catch(Exception error){report.AppendLine("FAIL "+error);throw;}
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);Directory.CreateDirectory("Artifacts/Campaign/Care");
            File.WriteAllText("Artifacts/Campaign/Care/Grab.txt",report.ToString());
        }
    }
}
