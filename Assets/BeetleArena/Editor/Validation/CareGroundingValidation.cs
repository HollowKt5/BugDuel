using System;
using System.IO;
using System.Text;
using BeetleArena;
using BugDuel.Domain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CareGroundingValidation
{
    [MenuItem("Tools/甲壳虫斗/验证/养殖/足部与甲壳接地", false, 544)]
    public static void Run()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before rig validation.");
        var previous=SceneManager.GetActiveScene();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BeetleArena/Generated/ArticulatedBeetle.prefab");
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);
            instance.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            var rig=instance.GetComponentInChildren<BeetleProceduralAnimator>();
            var pose=new CarePosePresenter(rig);pose.Reset(false,false);
            var feet=pose.Feet;float maxError=0,maxSlip=0,maxSole=0,minShell=float.PositiveInfinity;int minSupport=6,samples=0,stable=0;
            Vector3[] last=new Vector3[rig.legs.Length];bool[] support=new bool[last.Length];
            var report=new StringBuilder();
            foreach(var renderer in rig.shellRenderers)
            {
                var mf=renderer.GetComponent<MeshFilter>();
                report.AppendLine($"shell={renderer.name}; type={renderer.GetType().Name}; mesh={(mf?mf.sharedMesh.name:"none")}; readable={(mf&&mf.sharedMesh?mf.sharedMesh.isReadable:false)}");
            }
            foreach(var leg in rig.legs)
            {
                var b=rig.importedRig.bindings[(leg.side<0?0:3)+leg.pair];
                report.AppendLine($"leg {leg.side}/{leg.pair}: lengths={b.upperLength:F4}/{b.lowerLength:F4}, sole={leg.soleOffset:F4}, rest={leg.restAnkle}");
            }
            foreach(int rate in new[]{20,60,144})
            foreach(int turnDirection in new[]{-1,1})
            {
                instance.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                pose.Reset(false,false);Array.Clear(support,0,support.Length);
                for(int frame=0;frame<rate*50;frame++)
                {
                float dt=1f/rate,t=frame*dt;int section=(int)t/10;
                var state=section==0||section==4?CareState.Wandering:section==1?CareState.Eating:section==2?CareState.Resting:CareState.Grooming;
                float speed=state==CareState.Wandering?(section==4?.35f:.24f):0;
                if(speed>0)
                { instance.transform.Rotate(0,turnDirection*40*dt,0);instance.transform.position+=instance.transform.forward*speed*dt; }
                pose.Present(state,speed,dt);samples++;
                minShell=Mathf.Min(minShell,pose.Shell.LowestPoint());
                if(Supported(feet,rig.bodyPivot.position))stable++;
                minSupport=Mathf.Min(minSupport,feet.SupportCount);maxError=Mathf.Max(maxError,feet.MaximumError);
                for(int i=0;i<last.Length;i++)
                {
                    var foot=feet.Foot(i);
                    if(feet.IsSupport(i))maxSole=Mathf.Max(maxSole,Mathf.Abs(foot.y-feet.SoleHeight(i)));
                    if(feet.IsSupport(i)&&support[i])maxSlip=Mathf.Max(maxSlip,Vector3.Distance(last[i],foot));
                    last[i]=foot;support[i]=feet.IsSupport(i);
                }
                if(feet.SupportCount<3||feet.MaximumError>.02f)
                {
                    report.AppendLine($"rate={rate}, turn={turnDirection}, frame={frame}, state={state}, support={feet.SupportCount}, IK error={feet.MaximumError:F5}; modelScale={rig.model.lossyScale}; body={rig.bodyPivot.position}");
                    for(int i=0;i<last.Length;i++)
                    {
                        var b=rig.importedRig.bindings[(rig.legs[i].side<0?0:3)+rig.legs[i].pair];
                        report.AppendLine($"foot {i}: target={feet.Target(i):F4}, actual={feet.Foot(i):F4}, offset={b.footOffsetInModel:F4}; upper actual={Vector3.Distance(b.hip.position,b.knee.position):F4}; lower actual={Vector3.Distance(b.knee.position,b.ankle.position):F4}");
                    }
                    Directory.CreateDirectory("Artifacts/Campaign/Care");File.WriteAllText("Artifacts/Campaign/Care/GroundContact.txt",report.ToString());
                    throw new InvalidOperationException(report.ToString());
                }
                }
                report.AppendLine($"completed rate={rate}; turn={turnDirection}; frames={rate*50}");
            }
            report.AppendLine($"samples={samples}; min support={minSupport}; stable support polygon frames={stable}; min shell height={minShell:F6}; max IK error={maxError:F6}; max planted slip={maxSlip:F6}; max sole error={maxSole:F6}");
            float shellError=0;
            for(int i=0;i<180;i++)
            {
                pose.Present(CareState.Struggling,0,1f/60);
                if(i>90)shellError=Mathf.Max(shellError,Mathf.Abs(pose.Shell.LowestPoint()-.003f));
            }
            report.AppendLine($"overturned shell ground error={shellError:F6}");
            Directory.CreateDirectory("Artifacts/Campaign/Care");File.WriteAllText("Artifacts/Campaign/Care/GroundContact.txt",report.ToString());
            if(maxSlip>.01f||maxSole>.015f||shellError>.005f||stable<samples||minShell<-.005f)throw new InvalidOperationException(report.ToString());
            Debug.Log("Husbandry ground contact PASS. "+report);
        }
        finally { SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true); }
    }
    static bool Supported(CareFootPlant feet,Vector3 center)
    {
        for(int a=0;a<6;a++)for(int b=a+1;b<6;b++)for(int c=b+1;c<6;c++)
        {
            if(!feet.IsSupport(a)||!feet.IsSupport(b)||!feet.IsSupport(c))continue;
            var p=feet.Foot(a);var q=feet.Foot(b);var r=feet.Foot(c);
            float x=Cross(q-p,center-p),y=Cross(r-q,center-q),z=Cross(p-r,center-r);
            if(x>=-.0001f&&y>=-.0001f&&z>=-.0001f||x<=.0001f&&y<=.0001f&&z<=.0001f)return true;
        }
        return false;
    }
    static float Cross(Vector3 a,Vector3 b)=>a.x*b.z-a.z*b.x;
}
