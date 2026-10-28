using UnityEngine;

namespace BeetleArena.Campaign
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RetroRoundBadge : UnityEngine.UI.MaskableGraphic
    {
        protected override void OnEnable(){base.OnEnable();CampaignRetroTheme.Changed+=SetVerticesDirty;}
        protected override void OnDisable(){CampaignRetroTheme.Changed-=SetVerticesDirty;base.OnDisable();}
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
        {
            mesh.Clear();const int segments=48;
            var rect=rectTransform.rect;var center=rect.center;
            if(CampaignRetroTheme.Paper)
            {
                float outer=Mathf.Min(rect.width,rect.height)*.5f;
                Disc(mesh,center,outer,Color.white);
                Disc(mesh,center,Mathf.Max(0,outer-1),CampaignRetroTheme.Hex(0x232824));
                Disc(mesh,center,Mathf.Max(0,outer-2.5f),color);
                return;
            }
            float radius=Mathf.Min(rect.width,rect.height)*.5f,inner=Mathf.Max(0,radius-1.25f);
            mesh.AddVert(center,color,Vector2.zero);
            for(int i=0;i<segments;i++)
            {
                float angle=i*Mathf.PI*2/segments;var direction=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
                mesh.AddVert(center+direction*inner,color,Vector2.zero);
                mesh.AddVert(center+direction*radius,Color.white,Vector2.zero);
            }
            for(int i=0;i<segments;i++)
            {
                int current=1+i*2,next=1+(i+1)%segments*2;
                mesh.AddTriangle(0,next,current);
                mesh.AddTriangle(current,next,next+1);mesh.AddTriangle(current,next+1,current+1);
            }
        }
        static void Disc(UnityEngine.UI.VertexHelper mesh,Vector2 center,float radius,Color tint)
        {
            const int segments=48;int n=mesh.currentVertCount;
            mesh.AddVert(center,tint,Vector2.zero);
            for(int i=0;i<segments;i++)
            {
                float angle=i*Mathf.PI*2/segments;
                mesh.AddVert(center+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius,tint,Vector2.zero);
            }
            for(int i=0;i<segments;i++)mesh.AddTriangle(n,n+1+(i+1)%segments,n+1+i);
        }
    }
}
