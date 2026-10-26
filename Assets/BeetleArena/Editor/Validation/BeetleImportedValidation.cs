using System;
using System.IO;
using System.Linq;
using BeetleArena;
using UnityEditor;
using UnityEngine;

/// <summary>Validates physical mesh bone lengths, contacts, and full matches on dual maps; generates render screenshots for inspection.</summary>
public static class BeetleImportedValidation
{
    static float worstDepth;
    static string worstPart;
    public static string WorstVisiblePart => worstPart;
    public static void ResetVisibleMeasurement() { worstDepth = 0; worstPart = "none"; }
    public static string Run(int seed = 7319, int arena = 0, float seconds = 240)
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before validation.");
        using var fixture = new BeetleValidationScene("ImportedValidation");
        {
            worstDepth=0;worstPart="none";
            var match = fixture.Match;
            match.SelectArena(arena);
            var sim = new BeetleSimulation(match.settings, seed); sim.SetGround(match.ActiveSurface);
            foreach(var actor in match.actors) actor.Bind(sim.Fighters[Array.IndexOf(match.actors,actor)],match.settings,match.ActiveSurface);
            var contacts = new BeetleContactSolver(match.actors,match.arenaContacts,.006f);sim.ContactSolver=contacts;
            contacts.Solve(sim,0);
            var timer=System.Diagnostics.Stopwatch.StartNew();
            float maxBoneError=0, maxPenetration=0, maxFrameDisplacement=0, sumFootError=0, maxVisibleGroundDepth=0; int samples=0;
            var previous=match.actors.Select(a=>a.transform.position).ToArray();
            string prefix="Artifacts/Migration/"+seed+"-arena"+arena;
            Capture(match, prefix+"-start.png");
            for(int tick=0;tick<Mathf.CeilToInt(seconds/.02f);tick++) {
                foreach(var actor in match.actors) actor.BeforeStep();
                sim.Step(.02f);
                foreach(var actor in match.actors) actor.Render(.5f,sim.Elapsed,.02f);
                contacts.ProjectView(sim);
                foreach(var actor in match.actors) {
                    int i=Array.IndexOf(match.actors,actor);float move=Vector3.Distance(previous[i],actor.transform.position);previous[i]=actor.transform.position;
                    if(!actor.State.Airborne && !actor.State.HasFallen) maxFrameDisplacement=Mathf.Max(maxFrameDisplacement,move);
                    if(!float.IsFinite(actor.transform.position.sqrMagnitude)) throw new Exception("Invalid root position");
                    var rig=actor.animator.importedRig;float scale=rig.model.lossyScale.x;
                    foreach(var leg in rig.bindings) {
                        maxBoneError=Mathf.Max(maxBoneError,Mathf.Abs(Vector3.Distance(leg.hip.position,leg.knee.position)-leg.upperLength*scale));
                        maxBoneError=Mathf.Max(maxBoneError,Mathf.Abs(Vector3.Distance(leg.knee.position,leg.ankle.position)-leg.lowerLength*scale));
                        if(!actor.State.Airborne&&!actor.State.HasFallen) {sumFootError+=leg.targetError;samples++;}
                    }
                }
                if(tick%25==0) maxPenetration=Mathf.Max(maxPenetration,contacts.MeasurePenetration());
                if(tick%250==0) maxVisibleGroundDepth=Mathf.Max(maxVisibleGroundDepth,MeasureRenderedMesh(match));
                if(tick==499 || tick==1499) Capture(match,prefix+"-"+tick+".png");
                if(sim.Phase==MatchPhase.Finished) break;
            }
            Capture(match,prefix+"-end.png");
            var result=$"seed={seed}; arena={arena}; phase={sim.Phase}; elapsed={sim.Elapsed:F2}; winner={sim.Winner}; contacts={contacts.ContactFrames}; events={sim.EventCount}; maxBoneError={maxBoneError:F6}; maxPenetration={maxPenetration:F4}; maxGroundedStep={maxFrameDisplacement:F4}; meanIKError={sumFootError/Mathf.Max(1,samples):F4}; maxVisibleGroundDepth={maxVisibleGroundDepth:F4}; cpuSeconds={timer.Elapsed.TotalSeconds:F2}\n"+
                string.Join("\n",sim.Fighters.Select(f=>$"{f.Index}: {f.Action}; position={f.Position}; height={f.Height:F3}; probes={f.ProbeTouches}; shoves={f.Shoves}; pries={f.Pries}; charges={f.Charges}; tosses={f.Tosses}"))+"\nWorst visible ground sample: "+worstPart;
            Directory.CreateDirectory("Artifacts/Migration");File.WriteAllText(prefix+"-validation.txt",result);
            if(maxBoneError>.002f)throw new Exception("Bone lengths changed: "+result);
            return result;
        }
    }

    // Inspects final skinned vertices; collider clearance alone does not guarantee zero penetration across visible meshes.
    public static float MeasureRenderedMesh(BeetleMatchController match)
    {
        float maximum = 0; var baked = new Mesh();
        try {
            foreach(var actor in match.actors) {
                if(actor.State.Airborne || actor.State.HasFallen) continue;
                foreach(var renderer in actor.animator.model.GetComponentsInChildren<Renderer>()) {
                    Mesh mesh; var skinned = renderer as SkinnedMeshRenderer;
                    if(skinned) { skinned.BakeMesh(baked); mesh = baked; }
                    else { var filter=renderer.GetComponent<MeshFilter>(); if(!filter)continue; mesh=filter.sharedMesh; }
                    var vertices=mesh.vertices;
                    for(int i=0;i<vertices.Length;i+=3) {
                        // BakeMesh inherently accounts for scale; avoids compounding scale via renderer transform.
                        Vector3 v=skinned ? renderer.transform.position + renderer.transform.rotation * vertices[i] : renderer.transform.TransformPoint(vertices[i]);
                        if(!float.IsFinite(v.sqrMagnitude))throw new Exception("Nonfinite skinned vertex: "+renderer.name);
                        if(match.ActiveSurface.Sample(new Vector2(v.x,v.z),out var hit)) {
                            float depth=hit.height-v.y; maximum=Mathf.Max(maximum,depth);
                            if(depth>worstDepth) {worstDepth=depth;worstPart=renderer.name+" actor="+actor.State.Index+" action="+actor.State.Action+" pos="+v+" height="+actor.State.Height+" surface="+actor.State.SurfaceHeight;}
                        }
                    }
                }
            }
        } finally { UnityEngine.Object.DestroyImmediate(baked); }
        return maximum;
    }

    public static void Capture(BeetleMatchController match,string path)
    {
        var camera=match.spectatorCamera.GetComponent<Camera>();
        var oldPos=camera.transform.position;var oldRot=camera.transform.rotation;
        camera.transform.position=new Vector3(7,9,-10);camera.transform.LookAt(new Vector3(0,.2f,0));
        var rt=new RenderTexture(1500,1000,24);var tex=new Texture2D(1500,1000,TextureFormat.RGB24,false);
        var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;
        try {camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1500,1000),0,0);tex.Apply();Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,tex.EncodeToPNG());}
        finally {camera.targetTexture=oldTarget;RenderTexture.active=oldActive;UnityEngine.Object.DestroyImmediate(tex);UnityEngine.Object.DestroyImmediate(rt);camera.transform.SetPositionAndRotation(oldPos,oldRot);}
    }
}

