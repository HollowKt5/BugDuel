using UnityEngine;

namespace BeetleArena.Campaign
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RetroRoundBadge : UnityEngine.UI.MaskableGraphic
    {
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
        {
            mesh.Clear();const int segments=48;
            var rect=rectTransform.rect;var center=rect.center;
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
    }
}
