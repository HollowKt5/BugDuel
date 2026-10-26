using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BeetleArena.EditorTools
{
 /// <summary>Scene view drawing only; spawns no scene GameObjects, adds no colliders, writes no Gizmo assets.</summary>
    [InitializeOnLoad]
    public static class BeetleDebugSceneOverlay
    {
        static BeetleDebugSceneOverlay()=>SceneView.duringSceneGui+=Draw;
        static void Draw(SceneView view)
        {
            var match=BeetleDebugWorkbenchService.Match;if(!match || match.Simulation==null)return;
            int actorIndex=BeetleDebugWorkbenchService.SelectedActor,selectedLeg=BeetleDebugWorkbenchService.SelectedLeg;
            var actor=match.actors[actorIndex];var anim=actor.animator;
            if(BeetleDebugWorkbenchService.DrawIK && !BeetleDebugWorkbenchService.IsViewingClip)
                for(int i=0;i<anim.legs.Length;i++)
                {
                    if(selectedLeg>=0 && i!=selectedLeg)continue;
                    var leg=anim.legs[i];var binding=anim.importedRig.bindings[i];Vector3 foot=binding.tip.position;
                    Vector3 target=i==selectedLeg && BeetleDebugWorkbenchService.FootOverride.HasValue?BeetleDebugWorkbenchService.FootOverride.Value:leg.planted;
                    Handles.color=Color.yellow;Handles.DrawAAPolyLine(3,binding.hip.position,binding.knee.position,binding.ankle.position,foot);
                    Handles.color=Color.cyan;Handles.SphereHandleCap(0,target,Quaternion.identity,.07f,EventType.Repaint);
                    Handles.color=Color.green;Handles.SphereHandleCap(0,foot,Quaternion.identity,.05f,EventType.Repaint);
                    Handles.color=Color.red;Handles.DrawLine(target,foot);
                    var bend=anim.bodyPivot.TransformDirection(binding.bendInBody).normalized;
                    Handles.color=Color.magenta;Handles.DrawLine(binding.hip.position,binding.hip.position+bend*.4f);
                    Handles.Label(target+Vector3.up*.12f,$"{i} {(leg.stepping?"摆动":"支撑")} phase={leg.stepTime:F2} solve-error={binding.targetError:F3}");
                    if(BeetleDebugWorkbenchService.MoveFoot && BeetleDebugWorkbenchService.OwnsSandbox && match.IsPaused && !BeetleDebugWorkbenchService.Playing && i==selectedLeg)
                    {
                        EditorGUI.BeginChangeCheck();Vector3 moved=Handles.PositionHandle(target,Quaternion.identity);
                        if(EditorGUI.EndChangeCheck())BeetleDebugWorkbenchService.SetFootTarget(actorIndex,i,moved);
                    }
                }
            if(BeetleDebugWorkbenchService.DrawContacts && !BeetleDebugWorkbenchService.IsViewingClip)
            {
                var entry=BeetleDebugWorkbenchService.Trace.LastOrDefault(t=>(string)t["kind"]=="contact-attempt");
                if(entry!=null)
                    for(int i=0;i<2;i++)
                    {
                        var a=entry["proposal"][i];var b=entry["corrected"][i];
                        Vector3 p=new Vector3((float)a[0],(float)a[1],(float)a[2]),q=new Vector3((float)b[0],(float)b[1],(float)b[2]);
                        Handles.color=new Color(1,.45f,.1f);Handles.DrawAAPolyLine(4,p,q);Handles.Label(q+Vector3.up*.4f,$"候选修正 t={(float)entry["time"]:F2} Δ={Vector3.Distance(p,q):F3}");
                    }
            }
        }
    }
}
