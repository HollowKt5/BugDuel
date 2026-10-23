using System;
using System.Linq;
using System.Reflection;
using BeetleArena.Campaign;
using TMPro;
using UnityEngine;

// Called by an existing Play acceptance run after the specimen room has bound its scene.
public static class SpecimenRoomGeometryChecks
{
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic;

    public static int Check(CampaignSpecimenRoom room)
    {
        if(!room)throw new ArgumentNullException(nameof(room));
        int checks=0;
        void Require(bool valid,string message)
        {if(!valid)throw new InvalidOperationException("Specimen room geometry: "+message);checks++;}
        T Read<T>(string name)=>(T)typeof(CampaignSpecimenRoom).GetField(name,Fields).GetValue(room);
        var camera=Read<Camera>("cameraView");
        Require(camera&&!camera.orthographic,"bound perspective camera is required");
        var shellNames=new[]{"backWall","leftWall","rightWall","frontWall","ceiling","roomFloor"};
        var shell=shellNames.Select(name=>Read<Transform>(name)).ToArray();
        Require(shell.All(t=>t&&t.gameObject.activeInHierarchy),"all six shell surfaces exist and are active");
        var surfaces=shell.Select(t=>t.GetComponent<Renderer>()).ToArray();
        Require(surfaces.All(r=>r&&r.enabled),"all six shell surfaces have enabled renderers");
        var mounts=Read<Transform[]>("mounts");
        var borders=Read<Renderer[][]>("borders");
        var plaques=Read<TMP_Text[]>("plaques");
        Require(mounts.Length==12&&borders.Length==12&&plaques.Length==12,"twelve exhibit slots remain available");
        for(int slot=0;slot<12;slot++)
        {
            Require(mounts[slot]&&mounts[slot].gameObject.activeInHierarchy&&
                (mounts[slot].localPosition-CampaignSpecimenRoom.SlotPosition(slot)).sqrMagnitude<.000001f,
                "mount position changed at slot "+slot);
            Require(borders[slot].Length==4&&borders[slot].All(r=>r&&r.enabled)&&plaques[slot]&&
                mounts[slot].Find("Shadow box")&&mounts[slot].Find("Linen backing"),
                "frame, backing or plaque missing at slot "+slot);
        }

        var stateFields=new[]{"focus","targetFocus","yaw","pitch","distance","targetDistance","overviewDistance","aspect"}
            .Select(name=>typeof(CampaignSpecimenRoom).GetField(name,Fields)).ToArray();
        var saved=stateFields.Select(field=>field.GetValue(room)).ToArray();
        void Set(string name,object value)=>stateFields.First(field=>field.Name==name).SetValue(room,value);
        var apply=typeof(CampaignSpecimenRoom).GetMethod("ApplyCamera",Fields);
        float originalAspect=camera.aspect;
        Vector3 originalPosition=camera.transform.position;
        Quaternion originalRotation=camera.transform.rotation;
        var nearCorners=new Vector3[4];
        var surfaceBounds=new Bounds[6];
        try
        {
            // Wide to narrow exercises each increasing enclosure, rather than pre-growing the
            // largest room before checking the ordinary landscape view.
            foreach(float aspect in new[]{2.4f,1.33f,.75f,.4f})
            {
                camera.aspect=aspect;room.ResetView();
                for(int i=0;i<surfaces.Length;i++)surfaceBounds[i]=surfaces[i].bounds;
                float farthest=Read<float>("overviewDistance")*1.3f;
                foreach(float yaw in new[]{-38f,0,38f})
                foreach(float pitch in new[]{-26f,0,26f})
                foreach(float panX in new[]{-5f,0,5f})
                foreach(float panY in new[]{-3.6f,.35f,4f})
                foreach(float zoom in new[]{3.5f,farthest})
                {
                    string pose=$"aspect={aspect:0.###}, yaw={yaw}, pitch={pitch}, pan=({panX},{panY}), distance={zoom:0.###}";
                    var focus=new Vector3(panX,panY,0);
                    Set("focus",focus);Set("targetFocus",focus);Set("yaw",yaw);Set("pitch",pitch);
                    Set("distance",zoom);Set("targetDistance",zoom);apply.Invoke(room,null);
                    camera.CalculateFrustumCorners(new Rect(0,0,1,1),camera.nearClipPlane,Camera.MonoOrStereoscopicEye.Mono,nearCorners);
                    for(int corner=0;corner<nearCorners.Length;corner++)
                    {
                        var world=camera.transform.TransformPoint(nearCorners[corner]);
                        Require(room.InteriorBounds.Contains(room.transform.InverseTransformPoint(world)),
                            $"near-plane corner {corner} leaves the interior; {pose}");
                    }
                    for(int y=0;y<=4;y++)for(int x=0;x<=4;x++)
                    {
                        var ray=camera.ViewportPointToRay(new Vector3(x*.25f,y*.25f,0));
                        float nearest=float.PositiveInfinity;
                        foreach(var bounds in surfaceBounds)
                            if(bounds.IntersectRay(ray,out float hit)&&hit>0)nearest=Mathf.Min(nearest,hit);
                        Require(nearest<camera.farClipPlane,
                            $"viewport ray ({x}/4,{y}/4) misses the shell before far clip (hit={nearest}); {pose}");
                    }
                }
            }
        }
        finally
        {
            camera.aspect=originalAspect;room.ResetView();
            for(int i=0;i<stateFields.Length;i++)stateFields[i].SetValue(room,saved[i]);
            apply.Invoke(room,null);
            camera.transform.SetPositionAndRotation(originalPosition,originalRotation);
            // Deliberately retain the expanded enclosure and its matching far clip plane.
        }
        return checks;
    }
}
