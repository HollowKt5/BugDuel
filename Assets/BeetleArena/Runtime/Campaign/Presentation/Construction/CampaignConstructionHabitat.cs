using System.Collections.Generic;
using BugDuel.Campaign;
using UnityEngine;

namespace BeetleArena.Campaign
{
    /// <summary>Checks the real adult's walking clearance and full-grown stage models before accepting scenery.</summary>
    public sealed class CampaignConstructionHabitat
    {
        readonly CampaignSpecimen specimen;
        readonly Dictionary<CampaignLifeStage,Vector4> bodies=new Dictionary<CampaignLifeStage,Vector4>();
        public CampaignLifeStageVisualProfile Profile {get;}
        public int ProfileRevision {get;}
        public CampaignConstructionHabitat(CampaignSpecimen specimen)
        {
            this.specimen=specimen;
            Profile=specimen.LifeStageDisplay.VisualProfile;ProfileRevision=Profile.revision;
            foreach(var configuration in Profile.stages)
            {
                if(configuration.stage==CampaignLifeStage.ActiveAdult)continue;
                Vector2 size;
                if(configuration.stage==CampaignLifeStage.DormantAdult)
                    size=specimen.BodyFootprint;
                else
                {
                    var model=CampaignLifeStageDisplay.CreatePreservedBody(configuration.stage,null,Profile);
                    model.SetActive(false);
                    var bounds=new Bounds();bool first=true;
                    foreach(var renderer in model.GetComponentsInChildren<Renderer>(true))
                    {if(first){bounds=renderer.bounds;first=false;}else bounds.Encapsulate(renderer.bounds);}
                    size=new Vector2(bounds.size.x,bounds.size.z);
                    UnityEngine.Object.Destroy(model);
                }
                Vector2 surface=new Vector2(configuration.enclosureSize.x,configuration.enclosureSize.z)*.94f;
                if(IsCircular(configuration.stage))surface=Vector2.one*Mathf.Min(surface.x,surface.y);
                bodies.Add(configuration.stage,new Vector4((size.x+.12f)/surface.x*CampaignDecorations.Columns,(size.y+.12f)/surface.y*CampaignDecorations.Rows,
                    (size.y+.12f)/surface.x*CampaignDecorations.Columns,(size.x+.12f)/surface.y*CampaignDecorations.Rows));
            }
        }
        static bool IsCircular(CampaignLifeStage stage) => stage>=CampaignLifeStage.Larva1&&stage<=CampaignLifeStage.Larva3;
        public static List<Rect> Blockers(CampaignConstructionSpace space,Vector2 surface)
        {
            var result=new List<Rect>();
            foreach(var p in space.Footprints)result.Add(new Rect((p.x/(float)CampaignDecorations.Columns-.5f)*surface.x,
                (p.z/(float)CampaignDecorations.Rows-.5f)*surface.y,p.width*surface.x/CampaignDecorations.Columns,p.depth*surface.y/CampaignDecorations.Rows));
            return result;
        }
        public string Problem(CampaignConstructionSpace space)
        {
            var floor=specimen.enclosureFloor;
            var size=new Vector2(floor.localBounds.size.x*Mathf.Abs(floor.transform.lossyScale.x),floor.localBounds.size.z*Mathf.Abs(floor.transform.lossyScale.z));
            var walk=new CareWalkArea(new Rect(-size*.5f,size),specimen.ConstructionClearance,Blockers(space,size*.94f));
            if(walk.WalkablePoints<8||Mathf.Max(walk.ReachableBounds.width,walk.ReachableBounds.height)<.7f)
                return "生活空间不足：甲虫需要能转身并连续行走的空地";
            foreach(var stage in bodies.Keys)
                if(!TryRestingPosition(space,stage,out _,out _))return "生活空间不足：请为"+CampaignSession.StageName(stage)+"保留安全落脚区";
            return null;
        }
        public bool TryRestingPosition(CampaignConstructionSpace space,CampaignLifeStage stage,out Vector2 cell,out int rotation)
        {
            cell=new Vector2(6,5);rotation=0;
            if(!bodies.TryGetValue(stage,out var body))return false;
            double best=double.PositiveInfinity;
            for(int turn=0;turn<2;turn++)
            {
                double width=turn==0?body.x:body.z,depth=turn==0?body.y:body.w;
                if(!space.TryFindRoom(width,depth,IsCircular(stage),out double x,out double z))continue;
                double distance=(x-6)*(x-6)+(z-5)*(z-5);
                if(distance>=best)continue;
                best=distance;cell=new Vector2((float)x,(float)z);rotation=turn*90;
            }
            return !double.IsPositiveInfinity(best);
        }
    }
}
