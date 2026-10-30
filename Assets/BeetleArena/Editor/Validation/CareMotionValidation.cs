using System;
using System.Reflection;
using System.Text;
using BeetleArena;
using BeetleArena.Campaign;
using BugDuel.Domain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CareMotionValidation
{
    [MenuItem("Tools/甲壳虫斗/验证/养殖/支撑联动与动作节奏",false,548)]
    public static void Run() => Debug.Log(CheckLogic());

    public static string CheckLogic()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before motion validation.");
        var scene=EditorSceneManager.NewPreviewScene();var log=new StringBuilder();int checks=0;
        void Check(bool ok,string message)
        { if(!ok)throw new InvalidOperationException(message);log.AppendLine("PASS "+(++checks)+" "+message); }
        try
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BeetleArena/Generated/ArticulatedBeetle.prefab");
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);instance.SetActive(false);
            instance.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            var rig=instance.GetComponentInChildren<BeetleProceduralAnimator>(true);
            var pose=new CarePosePresenter(rig);pose.Reset(false,false);var feet=pose.Feet;
            float flatHeight=rig.bodyPivot.position.y;
            feet.SurfaceHeight=p=>.10f+p.x*.03f+p.z*.02f;
            pose.Reset(false,false);
            float error=0,slip=0;var previous=new Vector3[rig.legs.Length];
            for(int i=0;i<previous.Length;i++)previous[i]=feet.Foot(i);
            for(int frame=0;frame<240;frame++)
            {
                pose.Present(CareState.Idle,0,1f/60);error=Mathf.Max(error,feet.MaximumError);
                for(int i=0;i<previous.Length;i++)
                { slip=Mathf.Max(slip,Vector3.Distance(previous[i],feet.Foot(i)));previous[i]=feet.Foot(i); }
            }
            Check(Vector3.Angle(feet.SupportNormal,new Vector3(-.03f,1,-.02f))<.15f,
                "support plane follows the actual grounded feet in pitch and roll");
            Check(rig.bodyPivot.position.y>flatHeight+.04f&&instance.transform.position==Vector3.zero,
                "raised ground lifts the thorax without moving the navigation root");
            Check(error<.02f&&slip<.002f,"terrain adaptation preserves planted feet and measured limb reach");
            feet.SurfaceHeight=null;
            foreach(int rate in new[]{20,60,144})
            {
                instance.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);pose.Reset(false,false);
                var wasSupport=new bool[previous.Length];int minSupport=6;error=slip=0;
                int separateLandings=0;float largestBodyChange=0;Quaternion lastBody=rig.bodyPivot.localRotation;string firstReachFailure="";
                for(int frame=0;frame<rate*9;frame++)
                {
                    float dt=1f/rate,t=frame*dt;
                    float speed=t<1?0:t<2?(t-1)*.24f:t<5?.24f:t<6?(6-t)*.24f:0;
                    if(t>=2&&t<5)instance.transform.Rotate(0,62*dt,0);
                    instance.transform.position+=instance.transform.forward*speed*dt;
                    pose.Present(speed>0?CareState.Wandering:CareState.Idle,speed,dt,25);
                    if(feet.MaximumError>=.02f&&firstReachFailure.Length==0)firstReachFailure=ReachDetails(rig,feet,frame);
                    minSupport=Mathf.Min(minSupport,feet.SupportCount);error=Mathf.Max(error,feet.MaximumError);
                    largestBodyChange=Mathf.Max(largestBodyChange,Quaternion.Angle(lastBody,rig.bodyPivot.localRotation));lastBody=rig.bodyPivot.localRotation;
                    int landings=0;
                    for(int i=0;i<previous.Length;i++)
                    {
                        if(feet.IsSupport(i)&&wasSupport[i])slip=Mathf.Max(slip,Vector3.Distance(previous[i],feet.Foot(i)));
                        if(feet.IsSupport(i)&&!wasSupport[i])landings++;
                        previous[i]=feet.Foot(i);wasSupport[i]=feet.IsSupport(i);
                    }
                    if(landings==1||landings==2)separateLandings++;
                }
                Check(minSupport>=3&&error<.02f&&slip<.002f,$"start/62-degree turn/stop retain support at {rate} Hz (IK {error:F5}, slip {slip:F5})"+firstReachFailure);
                Check(feet.SupportCount==6&&largestBodyChange<3,$"stopping settles all six feet without a body snap at {rate} Hz");
                if(rate>=60)Check(separateLandings>2,$"tripod footfalls are staggered at {rate} Hz");
            }
            CheckWeightedWalk(instance.transform,rig,pose,Check);
            instance.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);pose.Reset(false,false);
            var antenna=rig.antennae[0].localRotation;var head=rig.headPivot.localRotation;
            pose.Present(CareState.SeekingFood,0,1f/60,45);
            Check(Quaternion.Angle(antenna,rig.antennae[0].localRotation)>Quaternion.Angle(head,rig.headPivot.localRotation)*3,
                "antennae acquire the intended route before the head follows");
            for(int i=0;i<40;i++)pose.Present(CareState.SeekingFood,0,1f/60,45);
            Check(Quaternion.Angle(head,rig.headPivot.localRotation)>8,"head turns toward the actual travel intent");

            var area=new CareWalkArea(new Rect(-5,-4,10,8),.6f);
            var motor=new CareLocomotion(61,area);var brain=new CareStateMachine(new CareBehaviorProfile(),61);
            var observation=new CareObservation { FoodAvailable=true,HealthFraction=1,Nutrition=40,Hour=21,HabitatActivity=1 };
            var origin=motor.Position;float heading=motor.Heading;
            for(int i=0;i<3;i++){brain.Step(.05,observation);motor.Tick(brain,.05f);}
            Check(motor.Anticipating&&motor.Position==origin&&motor.Heading==heading&&Mathf.Abs(motor.LookYaw)>1,
                "a new route has a sensing lead before the body starts turning or moving");
            for(int i=0;i<3000&&!brain.MealReady;i++)
            { observation.AtGoal=motor.AtGoal;brain.Step(.05,observation);motor.Tick(brain,.05f); }
            Check(brain.MealReady&&motor.Speed==0,"anticipation does not prevent reaching and eating food");

            var host=new GameObject("GrabLogicFixture");host.SetActive(false);SceneManager.MoveGameObjectToScene(host,scene);
            var grab=host.AddComponent<CampaignBeetleGrabController>();
            var type=typeof(CampaignBeetleGrabController);const BindingFlags fields=BindingFlags.Instance|BindingFlags.NonPublic;
            void Set(string name,object value)=>type.GetField(name,fields).SetValue(grab,value);
            void Holding(bool value)=>type.GetProperty("IsHolding").GetSetMethod(true).Invoke(grab,new object[]{value});
            Holding(true);Set("random",21u);Set("struggleDuration",1f);
            for(int i=0;i<60;i++)grab.AdvancePresentation(1f/60);
            Check(grab.StruggleWeight==0,"a stationary hold never starts a struggle");
            Set("pointerMoved",true);grab.AdvancePresentation(1f/60);
            float duration=(float)type.GetField("struggleDuration",fields).GetValue(grab);
            for(int i=0;i<Mathf.RoundToInt(duration*.45f*60);i++)grab.AdvancePresentation(1f/60);
            Check(grab.StruggleWeight>.2f,"stopping the pointer does not cut off the current struggle bout");
            for(int i=0;i<180;i++)grab.AdvancePresentation(1f/60);
            Check(grab.StruggleWeight==0,"one completed bout stops without new pointer movement");
            Set("pointerMoved",true);
            for(int i=0;i<12;i++)grab.AdvancePresentation(1f/60);
            Check(grab.StruggleWeight>.2f,"moving again starts a fresh bout after the previous one finishes");
            Holding(false);
            Check(CampaignBeetleGrabController.HoldSeconds==.7f,"pickup still requires the 0.7-second charge");
            log.AppendLine("COMPLETE PASS");
            return log.ToString();
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    static string ReachDetails(BeetleProceduralAnimator rig,CareFootPlant feet,int frame)
    {
        var details=new StringBuilder($"\nFirst unreachable step at frame {frame}; scale {rig.model.lossyScale:F4}; root {rig.transform.position:F4}; body {rig.bodyPivot.position:F4}\n");
        for(int i=0;i<rig.legs.Length;i++)
        {
            var binding=rig.importedRig.BindingFor(rig.legs[i]);
            details.AppendLine($"leg {i} ({rig.legs[i].side}/{rig.legs[i].pair}) support={feet.IsSupport(i)} error={Vector3.Distance(feet.Foot(i),feet.Target(i)):F6}; target={feet.Target(i):F5}; tip={feet.Foot(i):F5}; hip={binding.hip.position:F5}; lengths={binding.upperLength:F4}/{binding.lowerLength:F4}");
        }
        return details.ToString();
    }

    static void CheckWeightedWalk(Transform root,BeetleProceduralAnimator rig,CarePosePresenter pose,Action<bool,string> check)
    {
        var feet=pose.Feet;int count=rig.legs.Length;
        foreach(int rate in new[]{20,60,144})foreach(float speed in new[]{.10f,.16f})
        {
            root.SetPositionAndRotation(Vector3.zero,Quaternion.identity);pose.Reset(false,false,119);
            var support=new bool[count];var positions=new Vector3[count];var liftedAt=new float[count];var lifts=new int[count];
            var peakLift=new float[count];float completedLift=0;
            for(int i=0;i<count;i++){support[i]=feet.IsSupport(i);positions[i]=feet.Foot(i);liftedAt[i]=-1;}
            int samples=0,allDown=0,swings=0,minSupport=count;float swingSeconds=0,error=0,slip=0;
            float minY=float.PositiveInfinity,maxY=float.NegativeInfinity,maxVerticalSpeed=0,lastY=rig.bodyPivot.position.y;
            for(int frame=0;frame<rate*9;frame++)
            {
                float dt=1f/rate,t=(frame+1)*dt;
                root.position+=root.forward*speed*dt;pose.Present(CareState.Wandering,speed,dt);
                error=Mathf.Max(error,feet.MaximumError);minSupport=Mathf.Min(minSupport,feet.SupportCount);
                for(int i=0;i<count;i++)
                {
                    bool down=feet.IsSupport(i);var position=feet.Foot(i);
                    if(down&&support[i])slip=Mathf.Max(slip,Vector3.Distance(position,positions[i]));
                    if(!down&&support[i]){liftedAt[i]=t;peakLift[i]=0;if(t>=1)lifts[i]++;}
                    if(!down)peakLift[i]=Mathf.Max(peakLift[i],position.y-feet.SoleHeight(i));
                    if(down&&!support[i]&&liftedAt[i]>=1){swingSeconds+=t-liftedAt[i];completedLift+=peakLift[i];swings++;liftedAt[i]=-1;}
                    support[i]=down;positions[i]=position;
                }
                if(t>=1)
                {
                    samples++;if(feet.SupportCount==count)allDown++;
                    minY=Mathf.Min(minY,rig.bodyPivot.position.y);maxY=Mathf.Max(maxY,rig.bodyPivot.position.y);
                    maxVerticalSpeed=Mathf.Max(maxVerticalSpeed,Mathf.Abs(rig.bodyPivot.position.y-lastY)/dt);
                }
                lastY=rig.bodyPivot.position.y;
            }
            float meanSwing=swings>0?swingSeconds/swings:0,dwell=(float)allDown/samples;
            int maxLifts=0,minLifts=int.MaxValue;foreach(int value in lifts){maxLifts=Mathf.Max(maxLifts,value);minLifts=Mathf.Min(minLifts,value);}
            string context=$"at {rate} Hz, speed {speed:F2}";
            check(minSupport>=3&&error<.02f&&slip<.002f,$"weighted walk preserves support and planted anchors {context} (IK {error:F5}, slip {slip:F5})");
            check(swings>=12&&minLifts>=2&&maxLifts/8f<1.5f&&meanSwing>=.27f,
                $"weighted walk has deliberate recoveries {context} (mean swing {meanSwing:F3}s, max per-leg cadence {maxLifts/8f:F2}/s)");
            check(swings>0&&completedLift/swings>.018f*Mathf.Abs(rig.model.lossyScale.x),
                $"feet visibly clear the ground during recovery {context} (mean lift {completedLift/Mathf.Max(1,swings):F4})");
            check(dwell>.06f,$"each tripod transfer includes visible planted dwell {context} ({dwell:P1})");
            check(maxY-minY<.018f&&maxVerticalSpeed<.14f,
                $"thorax remains quiet through the push {context} (Y range {maxY-minY:F5}, speed {maxVerticalSpeed:F5})");

            // Pausing the calendar advances sensing only. Freeze during an airborne step,
            // then resume the same step without dropping, restarting or sliding its feet.
            for(int frame=0;frame<rate*2&&feet.SupportCount==count;frame++)
            { root.position+=root.forward*speed/rate;pose.Present(CareState.Wandering,speed,1f/rate); }
            check(feet.SupportCount<count,"pause fixture catches an airborne foot "+context);
            var frozenRoot=root.position;var frozenBody=rig.bodyPivot.position;
            for(int i=0;i<count;i++){positions[i]=feet.Foot(i);support[i]=feet.IsSupport(i);}
            for(int frame=0;frame<rate;frame++)pose.PresentAmbient(CareState.Wandering,1f/rate);
            bool frozen=root.position==frozenRoot&&Vector3.Distance(rig.bodyPivot.position,frozenBody)<.00001f;
            for(int i=0;i<count;i++)frozen&=support[i]==feet.IsSupport(i)&&Vector3.Distance(positions[i],feet.Foot(i))<.00001f;
            check(frozen,"calendar pause preserves the suspended step and body "+context);
            float resumeSlip=0,resumeError=0;
            for(int frame=0;frame<rate*3;frame++)
            {
                float nextSpeed=frame<rate?speed:0;
                root.position+=root.forward*nextSpeed/rate;pose.Present(nextSpeed>0?CareState.Wandering:CareState.Idle,nextSpeed,1f/rate);
                resumeError=Mathf.Max(resumeError,feet.MaximumError);
                for(int i=0;i<count;i++)
                {
                    bool down=feet.IsSupport(i);var position=feet.Foot(i);
                    if(down&&support[i])resumeSlip=Mathf.Max(resumeSlip,Vector3.Distance(position,positions[i]));
                    positions[i]=position;support[i]=down;
                }
            }
            check(feet.SupportCount==count&&resumeError<.02f&&resumeSlip<.002f,
                $"resume completes its step and stops on six anchored feet {context} (IK {resumeError:F5}, slip {resumeSlip:F5})");
            bool quietlyPlanted=true;
            for(int frame=0;frame<rate*3;frame++)
            { pose.Present(CareState.Idle,0,1f/rate);quietlyPlanted&=feet.SupportCount==count; }
            check(quietlyPlanted,"idle sensing does not restart corrective foot tapping "+context);
        }
    }
}
