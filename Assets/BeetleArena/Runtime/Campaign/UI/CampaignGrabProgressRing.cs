using UnityEngine;

namespace BeetleArena.Campaign
{
    [DisallowMultipleComponent,RequireComponent(typeof(CanvasRenderer))]
    public sealed class CampaignGrabProgressRing : UnityEngine.UI.MaskableGraphic
    {
        float progress;
        public float Progress
        {
            get=>progress;
            set
            {
                value=Mathf.Clamp01(value);
                if(Mathf.Approximately(progress,value))return;
                progress=value;SetVerticesDirty();
            }
        }
        protected override void Awake()
        {
            base.Awake();raycastTarget=false;maskable=false;color=Color.white;
        }
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
        {
            mesh.Clear();
            var rect=rectTransform.rect;float outer=Mathf.Min(rect.width,rect.height)*.5f;
            float inner=Mathf.Max(0,outer-10);
            AddArc(mesh,rect.center,inner,outer,1,new Color(1,1,1,.22f));
            AddArc(mesh,rect.center,inner,outer,progress,Color.white);
        }
        static void AddArc(UnityEngine.UI.VertexHelper mesh,Vector2 center,float inner,float outer,float fraction,Color tint)
        {
            if(fraction<=0)return;
            int segments=Mathf.Max(1,Mathf.CeilToInt(fraction*72));
            for(int i=0;i<segments;i++)
            {
                float first=fraction*i/segments*Mathf.PI*2,last=fraction*(i+1)/segments*Mathf.PI*2;
                var a=new Vector2(Mathf.Sin(first),Mathf.Cos(first));
                var b=new Vector2(Mathf.Sin(last),Mathf.Cos(last));
                int index=mesh.currentVertCount;
                mesh.AddVert(center+a*outer,tint,Vector2.zero);mesh.AddVert(center+b*outer,tint,Vector2.zero);
                mesh.AddVert(center+b*inner,tint,Vector2.zero);mesh.AddVert(center+a*inner,tint,Vector2.zero);
                mesh.AddTriangle(index,index+1,index+2);mesh.AddTriangle(index,index+2,index+3);
            }
        }
    }
}
