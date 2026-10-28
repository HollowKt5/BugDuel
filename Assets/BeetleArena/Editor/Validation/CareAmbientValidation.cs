using System;
using System.IO;
using System.Text;
using BeetleArena;
using BugDuel.Domain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CareAmbientValidation
{
    [MenuItem("Tools/甲壳虫斗/验证/养殖/静止感知与暂停", false, 546)]
    public static void Run()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before pose validation.");
        var scene=EditorSceneManager.NewPreviewScene();var report=new StringBuilder();int count=0;
        void Check(bool condition,string message)
        {
            if(!condition)throw new InvalidOperationException(message);
            report.AppendLine("PASS "+(++count)+" "+message);
        }
        try
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BeetleArena/Generated/ArticulatedBeetle.prefab");
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);
            instance.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            var rig=instance.GetComponentInChildren<BeetleProceduralAnimator>();
            var pose=new CarePosePresenter(rig);pose.Reset(false,false);
            var head=rig.headPivot.localRotation;var antenna=rig.antennae[0].localRotation;
            float headMotion=0,antennaMotion=0,contactError=0;int support=6;
            for(int frame=0;frame<300;frame++)
            {
                pose.Present(CareState.Idle,0,1f/60);
                headMotion=Mathf.Max(headMotion,Quaternion.Angle(head,rig.headPivot.localRotation));
                antennaMotion=Mathf.Max(antennaMotion,Quaternion.Angle(antenna,rig.antennae[0].localRotation));
                contactError=Mathf.Max(contactError,pose.Feet.MaximumError);support=Mathf.Min(support,pose.Feet.SupportCount);
            }
            Check(headMotion>.5f&&antennaMotion>3,"living idle beetles move their head and antennae");
            Check(contactError<.02f&&support>=3,"idle weight shifts retain grounded support feet");
            pose.Present(CareState.Eating,0,.1f);
            var rootPosition=instance.transform.position;var bodyPosition=rig.bodyPivot.localPosition;var bodyRotation=rig.bodyPivot.localRotation;
            var feet=new Vector3[rig.legs.Length];for(int i=0;i<feet.Length;i++)feet[i]=pose.Feet.Foot(i);
            head=rig.headPivot.localRotation;antenna=rig.antennae[0].localRotation;
            headMotion=antennaMotion=0;
            for(int frame=0;frame<180;frame++)
            {
                pose.PresentAmbient(CareState.Eating,1f/60);
                headMotion=Mathf.Max(headMotion,Quaternion.Angle(head,rig.headPivot.localRotation));
                antennaMotion=Mathf.Max(antennaMotion,Quaternion.Angle(antenna,rig.antennae[0].localRotation));
            }
            float footMotion=0;for(int i=0;i<feet.Length;i++)footMotion=Mathf.Max(footMotion,Vector3.Distance(feet[i],pose.Feet.Foot(i)));
            Check(antennaMotion>3,"a paused living beetle still senses its surroundings");
            Check(instance.transform.position==rootPosition&&rig.bodyPivot.localPosition==bodyPosition&&
                Quaternion.Angle(bodyRotation,rig.bodyPivot.localRotation)<.001f&&footMotion<.0001f,
                "paused sensing does not move the body, feet or root");
            pose.Reset(false,true);head=rig.headPivot.localRotation;antenna=rig.antennae[0].localRotation;
            bodyPosition=rig.bodyPivot.localPosition;bodyRotation=rig.bodyPivot.localRotation;
            for(int frame=0;frame<180;frame++){pose.PresentAmbient(CareState.Dead,1f/60);pose.Present(CareState.Dead,0,1f/60);}
            Check(Quaternion.Angle(head,rig.headPivot.localRotation)<.001f&&Quaternion.Angle(antenna,rig.antennae[0].localRotation)<.001f&&
                rig.bodyPivot.localPosition==bodyPosition&&Quaternion.Angle(bodyRotation,rig.bodyPivot.localRotation)<.001f,
                "dead beetles remain completely still");
            report.AppendLine("COMPLETE PASS");Debug.Log("Care ambient PASS: "+count+" checks.");
        }
        catch(Exception error){report.AppendLine("FAIL "+error);throw;}
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            Directory.CreateDirectory("Artifacts/Campaign/Care");
            File.WriteAllText("Artifacts/Campaign/Care/Ambient.txt",report.ToString());
        }
    }
}
