using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BeetleArena;
using BeetleArena.Campaign;
using BugDuel.Domain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CareEnclosureValidation
{
    const string Report = "Artifacts/Campaign/Care/Enclosure.txt";
    [MenuItem("Tools/Beetle Arena/Validate Enclosure Movement")]
    public static void Run()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before enclosure checks.");
        var log=new StringBuilder();var previous=SceneManager.GetActiveScene();Scene scene=default;
        try
        {
            GeometryChecks(log);
            // Preview scene is isolated even when the user already has the greenhouse open.
            scene=EditorSceneManager.OpenPreviewScene("Assets/Scenes/Campaign/CampaignGreenhouse.unity");
            var specimen=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CampaignSpecimen>(true)).First(s=>s.selected);
            var floor=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Renderer>(true)).First(r=>r.name=="SoilBed");
            foreach(var r in floor.transform.parent.GetComponentsInChildren<Renderer>())if(r.name=="FoodDish"||r.name=="FoodJelly")r.gameObject.SetActive(false);
            var rig=specimen.rig;float rootOffset=specimen.transform.position.y-floor.bounds.max.y;
            var pose=new CarePosePresenter(rig,floor.bounds.max.y);pose.Reset(false,false);
            float clearance=CareEnclosureLayout.MeasureClearance(rig);
            var layout=new CareEnclosureLayout(floor,rig,clearance);var area=layout.Area;
            using var ground=new CareGroundSurface(layout);pose.Feet.SurfaceHeight=ground.HeightAt;
            log.AppendLine($"Actual enclosure: floor={area.Floor}; safe centers={area.Bounds}; reachable={area.ReachableBounds}; clearance={clearance:F4}; obstacles={area.ObstacleCount}; reachable points={area.WalkablePoints}");
            Check(area.CanWalk,"actual enclosure permits walking",log);
            Check(layout.GroundDetails.Any(r=>ground.HeightAt(r.bounds.center)>layout.GroundHeight+.015f),"low decorative meshes provide real foot contact heights",log);
            var motor=new CareLocomotion(107,area);var brain=new CareStateMachine(new CareBehaviorProfile(),107);
            var observation=new CareObservation{Nutrition=40,HealthFraction=1,Hour=21,HabitatActivity=1};
            Vector2 min=motor.Position,max=min;float maxError=0,maxSole=0;int samples=0;
            specimen.transform.position=layout.ToWorld(motor.Position)+Vector3.up*rootOffset;pose.Reset(false,false);
            Action step=()=>
            {
                var before=motor.Position;observation.AtGoal=motor.AtGoal;brain.Step(.05,observation);motor.Tick(brain,.05f);
                if(!area.SegmentClear(before,motor.Position))throw new InvalidOperationException("Movement cut an obstacle or wall.");
                specimen.transform.SetPositionAndRotation(layout.ToWorld(motor.Position)+Vector3.up*rootOffset,Quaternion.Euler(0,layout.Heading+motor.Heading,0));
                pose.Present(brain.State,motor.Speed,.05f);samples++;
                maxError=Mathf.Max(maxError,pose.Feet.MaximumError);
                for(int i=0;i<rig.legs.Length;i++)
                {
                    var foot=pose.Feet.Foot(i);var local=floor.transform.InverseTransformPoint(foot);var b=floor.localBounds;
                    if(local.x<b.min.x||local.x>b.max.x||local.z<b.min.z||local.z>b.max.z)throw new InvalidOperationException("Foot left actual soil geometry.");
                    if(pose.Feet.IsSupport(i))maxSole=Mathf.Max(maxSole,Mathf.Abs(foot.y-pose.Feet.SoleHeight(i)));
                }
                if(pose.Feet.SupportCount<3||maxError>.02f||maxSole>.015f)throw new InvalidOperationException($"Ground contact: {pose.Feet.SupportCount} supports, IK={maxError}, sole={maxSole}");
                min=Vector2.Min(min,motor.Position);max=Vector2.Max(max,motor.Position);
            };
            for(int i=0;i<7200;i++)step();
            log.AppendLine($"Actual movement span={max-min}; samples={samples}; IK={maxError:F6}; sole error={maxSole:F6}");
            Check(max.x-min.x>area.ReachableBounds.width*.6f&&max.y-min.y>area.ReachableBounds.height*.6f&&max.x-min.x>.8f&&max.y-min.y>.6f,
                "roaming spans the reachable enclosure around obstacles, beyond the old observation patch",log);
            observation.FoodAvailable=true;
            for(int i=0;i<3000&&!brain.MealReady;i++)step();
            Check(brain.MealReady&&motor.Speed==0&&Vector2.Distance(motor.Position,motor.FeedingPoint)<.03f,"food reached and eaten after a long route",log);
            observation.FoodAvailable=false;observation.HealthFraction=.2;
            for(int i=0;i<3000&&brain.State!=CareState.Resting;i++)step();
            Check(brain.State==CareState.Resting,"shelter remains reachable",log);
            var parent=floor.transform.parent;parent.rotation=Quaternion.Euler(0,37,0);parent.localScale=Vector3.one*1.4f;
            var rotated=new CareEnclosureLayout(floor,rig,clearance);
            Check(rotated.Area.Bounds.width>area.Bounds.width&&rotated.Area.Bounds.height>area.Bounds.height,"actual larger box expands walking limits",log);
            foreach(var p in new[]{rotated.Area.Spawn,rotated.Area.FeedingPoint,rotated.Area.ShelterPoint})
            {
                var local=floor.transform.InverseTransformPoint(rotated.ToWorld(p));var b=floor.localBounds;
                Check(local.x>=b.min.x&&local.x<=b.max.x&&local.z>=b.min.z&&local.z<=b.max.z,"rotated enclosure uses its own axes",log);
            }
            log.AppendLine("PASS enclosure movement and ground contact");
        }
        catch(Exception ex){log.AppendLine("FAIL "+ex);throw;}
        finally
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Report));File.WriteAllText(Report,log.ToString());
            if(previous.IsValid())SceneManager.SetActiveScene(previous);
            if(scene.IsValid())EditorSceneManager.ClosePreviewScene(scene);
        }
    }
    static void GeometryChecks(StringBuilder log)
    {
        var area=new CareWalkArea(new Rect(-5,-4,10,8),.7f,new[]{new Rect(-.5f,-2,1,4)});
        var path=new List<Vector2>();Vector2 start=new Vector2(-3,0),goal=new Vector2(3,0);
        area.FindPath(start,goal,path);
        Check(path.Count>1,"route goes around the expanded obstacle",log);
        Vector2 from=start;
        foreach(var p in path){Check(area.SegmentClear(from,p),"each route segment clears obstacles",log);from=p;}
        Check(Vector2.Distance(from,goal)<.2f,"route reaches the opposite side",log);
        var small=new CareWalkArea(new Rect(-2,-1.5f,4,3),.7f);
        var motor=new CareLocomotion(17,area);motor.Configure(small);
        Check(small.IsClear(motor.Position)&&small.Bounds.width<area.Bounds.width,"resizing rebinds the motor to the new bounds",log);
        var tiny=new CareWalkArea(new Rect(-.2f,-.2f,.4f,.4f),.7f);
        Check(!tiny.CanWalk&&!float.IsNaN(tiny.Spawn.x),"undersized enclosure never produces inverted bounds or invalid motion",log);
        // Exercise approaches from distinct headings, including the previous near-food turn deadlock.
        for(uint seed=1;seed<=4;seed++)
        {
            motor=new CareLocomotion(seed,area);var brain=new CareStateMachine(new CareBehaviorProfile(),seed);
            var o=new CareObservation{Nutrition=40,HealthFraction=1,Hour=21,HabitatActivity=1};
            for(int i=0;i<600;i++){o.AtGoal=motor.AtGoal;brain.Step(.05,o);motor.Tick(brain,.05f);}
            o.FoodAvailable=true;
            for(int i=0;i<3000&&!brain.MealReady;i++){o.AtGoal=motor.AtGoal;brain.Step(.05,o);motor.Tick(brain,.05f);}
            Check(brain.MealReady&&motor.Speed==0,"food approach succeeds for seed "+seed,log);
            brain.InvalidateArrival();motor.Configure(small);
            Check(brain.State==CareState.SeekingFood&&!brain.MealReady&&!motor.AtGoal,"moving the feeding point requires arrival again",log);
        }
    }
    static void Check(bool value,string message,StringBuilder log)
    { if(!value)throw new InvalidOperationException(message);log.AppendLine("PASS "+message); }
}
