using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BeetleArena.Campaign
{
    /// <summary>Scalable classic or paper surfaces in the existing Image mesh.</summary>
    [DisallowMultipleComponent]
    public sealed class RetroBevel : BaseMeshEffect, IPointerDownHandler, IPointerUpHandler
    {
        float thickness=1;
        Color light=Color.white,dark=Color.gray,top,bottom;
        bool gradient,pressed,foldCorner=true;
        public void Configure(float width,Color highlight,Color shadow,bool useGradient=false,Color gradientTop=default,Color gradientBottom=default,bool paperFold=true)
        {
            thickness=width;light=highlight;dark=shadow;gradient=useGradient;top=gradientTop;bottom=gradientBottom;foldCorner=paperFold;
            if(graphic)graphic.SetVerticesDirty();
        }
        public void OnPointerDown(PointerEventData e) { if(e.button==PointerEventData.InputButton.Left){pressed=true;graphic.SetVerticesDirty();} }
        public void OnPointerUp(PointerEventData e) { pressed=false;graphic.SetVerticesDirty(); }
        protected override void OnEnable(){base.OnEnable();CampaignRetroTheme.Changed+=Refresh;}
        protected override void OnDisable(){CampaignRetroTheme.Changed-=Refresh;pressed=false;base.OnDisable();}
        void Refresh(){if(graphic)graphic.SetVerticesDirty();}
        public override void ModifyMesh(VertexHelper mesh)
        {
            if(!IsActive()||!graphic)return;
            var r=((RectTransform)transform).rect;mesh.Clear();
            if(r.width<=0||r.height<=0)return;
            if(CampaignRetroTheme.Paper){Paper(mesh,r);return;}
            Quad(mesh,r.xMin,r.yMin,r.xMax,r.yMax,gradient?bottom:graphic.color,gradient?top:graphic.color);
            float b=Mathf.Min(thickness,Mathf.Min(r.width,r.height)*.5f);
            Color hi=pressed?dark:light,lo=pressed?light:dark;
            Quad(mesh,r.xMin,r.yMax-b,r.xMax,r.yMax,hi,hi);
            Quad(mesh,r.xMin,r.yMin,r.xMin+b,r.yMax,hi,hi);
            Quad(mesh,r.xMin+b,r.yMin,r.xMax,r.yMin+b,lo,lo);
            Quad(mesh,r.xMax-b,r.yMin,r.xMax,r.yMax-b,lo,lo);
        }
        void Paper(VertexHelper mesh,Rect rect)
        {
            Color surface=gradient?top:graphic.color;
            // Rules, masks and unframed content stay flat. No extra GameObjects or raycast targets.
            bool button=TryGetComponent<Button>(out var control);
            if(Mathf.Min(rect.width,rect.height)<18)
            {
                if(thickness>0&&Mathf.Min(rect.width,rect.height)>=6)
                {
                    var edge=CampaignRetroTheme.PaperOutline;edge.a=surface.a;
                    Quad(mesh,rect.xMin,rect.yMin,rect.xMax,rect.yMax,edge,edge);
                    var inset=Inset(rect,1.5f);Quad(mesh,inset.xMin,inset.yMin,inset.xMax,inset.yMax,surface,surface);
                }
                else Quad(mesh,rect.xMin,rect.yMin,rect.xMax,rect.yMax,surface,surface);
                return;
            }
            if(!gradient&&thickness<=0&&!button)
            { Quad(mesh,rect.xMin,rect.yMin,rect.xMax,rect.yMax,surface,surface);return; }

            float depth=Mathf.Min(gradient?2:5,rect.height*.09f);
            float line=Mathf.Min(2.5f,Mathf.Max(1.5f,rect.height*.065f));
            float corner=Mathf.Min(7,rect.height*.14f);
            bool down=pressed&&button&&control.IsInteractable();
            Color outline=CampaignRetroTheme.PaperOutline,board=CampaignRetroTheme.PaperBoard,shadow=CampaignRetroTheme.PaperShadow;
            outline.a=board.a=shadow.a=surface.a;
            Polygon(mesh,new Rect(rect.xMin+depth,rect.yMin,rect.width-depth,rect.height-depth),corner,shadow);
            var sheet=new Rect(rect.xMin,rect.yMin+depth,rect.width-depth,rect.height-depth);
            Polygon(mesh,sheet,corner,outline);
            Polygon(mesh,Inset(sheet,line),Mathf.Max(0,corner-line),board);
            float pressOffset=down?depth*.55f:0;
            sheet.position+=new Vector2(pressOffset,depth*.8f-pressOffset);
            sheet.height-=depth*.8f;
            Polygon(mesh,sheet,corner,outline);
            var face=Inset(sheet,line);
            Polygon(mesh,face,Mathf.Max(0,corner-line),surface);

            // Small folded corner stays inside the card's padding; text and illustrations keep their space.
            if(foldCorner&&!button&&!gradient&&rect.width>180&&rect.height>110)
            {
                float fold=Mathf.Min(14,rect.height*.08f);
                var a=new Vector2(face.xMax-fold,face.yMin);
                var b=new Vector2(face.xMax,face.yMin+fold);
                int n=mesh.currentVertCount;
                mesh.AddVert(a,board,Vector2.zero);mesh.AddVert(b,board,Vector2.zero);
                mesh.AddVert(new Vector2(face.xMax,face.yMin),board,Vector2.zero);
                mesh.AddTriangle(n,n+1,n+2);
                Stroke(mesh,a,b,1.2f,outline);
            }
        }
        static Rect Inset(Rect rect,float inset)=>new Rect(rect.xMin+inset,rect.yMin+inset,Mathf.Max(0,rect.width-inset*2),Mathf.Max(0,rect.height-inset*2));
        static void Polygon(VertexHelper mesh,Rect rect,float corner,Color color)
        {
            if(rect.width<=0||rect.height<=0)return;
            corner=Mathf.Min(corner,Mathf.Min(rect.width,rect.height)*.25f);
            int n=mesh.currentVertCount;
            mesh.AddVert(rect.center,color,Vector2.zero);
            for(int i=0;i<8;i++)mesh.AddVert(Corner(rect,corner,i),color,Vector2.zero);
            for(int i=0;i<8;i++)mesh.AddTriangle(n,n+1+i,n+1+(i+1)%8);
        }
        static Vector2 Corner(Rect r,float c,int i)
        {
            switch(i)
            {
                case 0:return new Vector2(r.xMin+c,r.yMin);
                case 1:return new Vector2(r.xMin,r.yMin+c);
                case 2:return new Vector2(r.xMin,r.yMax-c*.6f);
                case 3:return new Vector2(r.xMin+c*.6f,r.yMax);
                case 4:return new Vector2(r.xMax-c,r.yMax);
                case 5:return new Vector2(r.xMax,r.yMax-c);
                case 6:return new Vector2(r.xMax,r.yMin+c*.7f);
                default:return new Vector2(r.xMax-c*.7f,r.yMin);
            }
        }
        static void Stroke(VertexHelper mesh,Vector2 a,Vector2 b,float width,Color color)
        {
            var d=b-a;var normal=new Vector2(-d.y,d.x).normalized*(width*.5f);int n=mesh.currentVertCount;
            mesh.AddVert(a-normal,color,Vector2.zero);mesh.AddVert(a+normal,color,Vector2.zero);
            mesh.AddVert(b+normal,color,Vector2.zero);mesh.AddVert(b-normal,color,Vector2.zero);
            mesh.AddTriangle(n,n+1,n+2);mesh.AddTriangle(n,n+2,n+3);
        }
        static void Quad(VertexHelper mesh,float x0,float y0,float x1,float y1,Color lower,Color upper)
        {
            int n=mesh.currentVertCount;
            mesh.AddVert(new Vector3(x0,y0),lower,Vector2.zero);mesh.AddVert(new Vector3(x0,y1),upper,Vector2.up);
            mesh.AddVert(new Vector3(x1,y1),upper,Vector2.one);mesh.AddVert(new Vector3(x1,y0),lower,Vector2.right);
            mesh.AddTriangle(n,n+1,n+2);mesh.AddTriangle(n,n+2,n+3);
        }
    }
}
