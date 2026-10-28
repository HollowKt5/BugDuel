using System;
using System.Collections.Generic;
using BugDuel.Campaign;
using UnityEngine;

namespace BeetleArena.Campaign
{
    /// <summary>Projects the animal's articulated renderer bounds onto the construction grid.</summary>
    public sealed class CampaignConstructionOccupancy
    {
        readonly bool[,] occupied=new bool[CampaignDecorations.Columns,CampaignDecorations.Rows];
        readonly List<Vector2Int> cells=new List<Vector2Int>();
        readonly List<Renderer> renderers=new List<Renderer>();
        readonly List<Vector2> points=new List<Vector2>(8),hull=new List<Vector2>(16);
        public IReadOnlyList<Vector2Int> Cells=>cells;
        public bool Contains(int x,int z)=>x>=0&&z>=0&&x<CampaignDecorations.Columns&&z<CampaignDecorations.Rows&&occupied[x,z];

        public void Capture(Transform animal,Vector3 center,Vector2 size,Quaternion rotation)
        {
            Array.Clear(occupied,0,occupied.Length);cells.Clear();renderers.Clear();
            if(!animal||size.x<=0||size.y<=0)return;
            animal.GetComponentsInChildren(true,renderers);
            var worldToSurface=Matrix4x4.TRS(center,rotation,Vector3.one).inverse;
            foreach(var renderer in renderers)
            {
                // Construction deliberately hides the renderers; their actual articulated pose remains valid.
                if(!renderer||!renderer.gameObject.activeInHierarchy)continue;
                points.Clear();var bounds=renderer.localBounds;var transform=worldToSurface*renderer.localToWorldMatrix;
                for(int i=0;i<8;i++)
                {
                    var corner=bounds.center+Vector3.Scale(bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                    var p=transform.MultiplyPoint3x4(corner);
                    points.Add(new Vector2((p.x/size.x+.5f)*CampaignDecorations.Columns,(p.z/size.y+.5f)*CampaignDecorations.Rows));
                }
                BuildHull();if(hull.Count<3)continue;
                Vector2 min=hull[0],max=min;
                foreach(var point in hull){min=Vector2.Min(min,point);max=Vector2.Max(max,point);}
                for(int z=Mathf.Max(0,Mathf.FloorToInt(min.y));z<Mathf.Min(CampaignDecorations.Rows,Mathf.CeilToInt(max.y));z++)
                    for(int x=Mathf.Max(0,Mathf.FloorToInt(min.x));x<Mathf.Min(CampaignDecorations.Columns,Mathf.CeilToInt(max.x));x++)
                        if(!occupied[x,z]&&IntersectsCell(x,z))occupied[x,z]=true;
            }
            for(int z=0;z<CampaignDecorations.Rows;z++)for(int x=0;x<CampaignDecorations.Columns;x++)
                if(occupied[x,z])cells.Add(new Vector2Int(x,z));
        }

        public bool Overlaps(string item,int x,int z,int rotation)
        {
            int width=CampaignDecorations.Width(item,rotation),depth=CampaignDecorations.Depth(item,rotation);
            for(int dz=0;dz<depth;dz++)for(int dx=0;dx<width;dx++)if(Contains(x+dx,z+dz))return true;
            return false;
        }

        void BuildHull()
        {
            points.Sort((a,b)=>a.x==b.x?a.y.CompareTo(b.y):a.x.CompareTo(b.x));hull.Clear();
            foreach(var point in points)
            {
                while(hull.Count>=2&&Cross(hull[hull.Count-1]-hull[hull.Count-2],point-hull[hull.Count-1])<=0)hull.RemoveAt(hull.Count-1);
                hull.Add(point);
            }
            int lower=hull.Count;
            for(int i=points.Count-2;i>=0;i--)
            {
                var point=points[i];
                while(hull.Count>lower&&Cross(hull[hull.Count-1]-hull[hull.Count-2],point-hull[hull.Count-1])<=0)hull.RemoveAt(hull.Count-1);
                hull.Add(point);
            }
            if(hull.Count>1)hull.RemoveAt(hull.Count-1);
        }

        bool IntersectsCell(int x,int z)
        {
            var center=new Vector2(x+.5f,z+.5f);
            if(Separated(Vector2.right,center)||Separated(Vector2.up,center))return false;
            for(int i=0;i<hull.Count;i++)
            {
                var edge=hull[(i+1)%hull.Count]-hull[i];
                if(Separated(new Vector2(-edge.y,edge.x),center))return false;
            }
            return true;
        }
        bool Separated(Vector2 axis,Vector2 center)
        {
            if(axis.sqrMagnitude<.000001f)return false;
            float min=float.PositiveInfinity,max=float.NegativeInfinity;
            foreach(var p in hull){float dot=Vector2.Dot(p,axis);min=Mathf.Min(min,dot);max=Mathf.Max(max,dot);}
            float middle=Vector2.Dot(center,axis),radius=(Mathf.Abs(axis.x)+Mathf.Abs(axis.y))*.5f;
            float tolerance=.0001f*axis.magnitude;
            return max<=middle-radius+tolerance||min>=middle+radius-tolerance;
        }
        static float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
    }
}
