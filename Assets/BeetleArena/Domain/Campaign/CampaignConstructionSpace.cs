using System;
using System.Collections.Generic;
using System.Linq;

namespace BugDuel.Campaign
{
    /// <summary>Occupied grid cells and connected living space, independent of model or renderer state.</summary>
    public sealed class CampaignConstructionSpace
    {
        public const int MinimumLivingCells=30;
        public readonly struct Footprint
        {
            public readonly int x,z,width,depth;
            public Footprint(CampaignDecorationPlacement p)
            {x=p.x;z=p.z;width=CampaignDecorations.Width(p.itemId,p.rotation);depth=CampaignDecorations.Depth(p.itemId,p.rotation);}
        }
        public IReadOnlyList<Footprint> Footprints {get;}
        public int LivingCells {get;}
        public bool HasLivingSpace => LivingCells>=MinimumLivingCells&&TryFindRoom(4,4,false,out _,out _);
        readonly bool[,] occupied=new bool[CampaignDecorations.Columns,CampaignDecorations.Rows];

        public CampaignConstructionSpace(IEnumerable<CampaignDecorationPlacement> placements)
        {
            Footprints=placements.Select(p=>new Footprint(p)).ToArray();
            foreach(var p in Footprints)
                for(int x=Math.Max(0,p.x);x<Math.Min(CampaignDecorations.Columns,p.x+p.width);x++)
                    for(int z=Math.Max(0,p.z);z<Math.Min(CampaignDecorations.Rows,p.z+p.depth);z++)occupied[x,z]=true;
            var visited=new bool[CampaignDecorations.Columns,CampaignDecorations.Rows];
            var queue=new Queue<(int x,int z)>();
            for(int z=1;z<CampaignDecorations.Rows-1;z++)for(int x=1;x<CampaignDecorations.Columns-1;x++)
            {
                if(visited[x,z]||occupied[x,z]||CampaignDecorations.IsReservedCell(x,z))continue;
                int count=0;Visit(x,z);
                while(queue.Count>0)
                {var cell=queue.Dequeue();count++;Visit(cell.x-1,cell.z);Visit(cell.x+1,cell.z);Visit(cell.x,cell.z-1);Visit(cell.x,cell.z+1);}
                LivingCells=Math.Max(LivingCells,count);
            }
            void Visit(int x,int z)
            {
                if(CampaignDecorations.IsReservedCell(x,z)||visited[x,z]||occupied[x,z])return;
                visited[x,z]=true;queue.Enqueue((x,z));
            }
        }

        public bool RoomClear(double centerX,double centerZ,double width,double depth,bool circular)
        {
            double left=centerX-width*.5,right=centerX+width*.5,bottom=centerZ-depth*.5,top=centerZ+depth*.5;
            if(left<0||right>CampaignDecorations.Columns||bottom<0||top>CampaignDecorations.Rows)return false;
            if(circular)
            {
                double u=Math.Max(Math.Abs(left/6-1),Math.Abs(right/6-1));
                double v=Math.Max(Math.Abs(bottom/5-1),Math.Abs(top/5-1));
                if(u*u+v*v>1)return false;
            }
            foreach(var p in Footprints)
                if(left<p.x+p.width&&right>p.x&&bottom<p.z+p.depth&&top>p.z)return false;
            return true;
        }

        // Quarter-cell candidates keep small position changes possible without allowing narrow diagonal gaps.
        public bool TryFindRoom(double width,double depth,bool circular,out double centerX,out double centerZ)
        {
            centerX=6;centerZ=5;double best=double.PositiveInfinity;
            for(int z=0;z<=CampaignDecorations.Rows*4;z++)for(int x=0;x<=CampaignDecorations.Columns*4;x++)
            {
                double px=x*.25,pz=z*.25,distance=(px-6)*(px-6)+(pz-5)*(pz-5);
                if(distance>=best||!RoomClear(px,pz,width,depth,circular))continue;
                best=distance;centerX=px;centerZ=pz;
            }
            return !double.IsPositiveInfinity(best);
        }
    }
}
