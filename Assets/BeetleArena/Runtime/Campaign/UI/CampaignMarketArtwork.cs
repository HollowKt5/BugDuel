using System.Collections.Generic;
using UnityEngine;

namespace BeetleArena.Campaign
{
    /// <summary>Small vector thumbnails translated from the reference's 100x100 SVG drawings.
    /// Geometry is rebuilt only when UI geometry changes; there are no cameras or generated textures.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CampaignMarketArtwork : UnityEngine.UI.MaskableGraphic
    {
        public enum Kind { Horn, Stag, Rainbow, Container, Wood, Lamp, Sap, Potato, Banana, Apple }
        Kind kind;
        UnityEngine.UI.VertexHelper mesh;
        Rect bounds;
        readonly List<Vector2> path=new List<Vector2>();
        public void SetKind(Kind value){kind=value;raycastTarget=false;SetVerticesDirty();}
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
        {
            vh.Clear();mesh=vh;bounds=rectTransform.rect;
            switch(kind)
            {
                case Kind.Horn:
                    Box(0,0,100,100,0x1b281f);Move(50,15);Cubic(55,35,75,40,75,65);Cubic(75,85,62,92,50,92);Cubic(38,92,25,85,25,65);Cubic(25,40,45,35,50,15);Fill(0x2d4a32);
                    Move(50,10);Line(50,45);Stroke(0xf59e0b,3);Move(50,20);Quad(70,10,75,5);Stroke(0xf59e0b,3);Move(50,30);Quad(30,18,25,15);Stroke(0xf59e0b,3);Ellipse(50,55,18,18,0xeab308);break;
                case Kind.Rainbow:
                    Box(0,0,100,100,0x182e20);Move(50,25);Cubic(65,35,70,55,68,80);Cubic(68,90,50,92,50,92);Cubic(50,92,32,90,32,80);Cubic(30,55,35,35,50,25);Fill(0x10b981);
                    Move(42,28);Quad(35,10,40,5);Quad(46,15,47,25);Fill(0xf59e0b);Move(58,28);Quad(65,10,60,5);Quad(54,15,53,25);Fill(0xf59e0b);Move(50,35);Line(50,88);Stroke(0x047857,2);break;
                case Kind.Stag:
                    Box(0,0,100,100,0x141416);Move(38,32);Quad(32,10,40,4);Quad(44,14,44,32);Fill(0x0f172a);Stroke(0x475569,1.5f,true);Move(62,32);Quad(68,10,60,4);Quad(56,14,56,32);Fill(0x0f172a);Stroke(0x475569,1.5f,true);
                    Box(35,32,30,22,0x1e293b);Move(35,55);Cubic(35,85,65,85,65,55);Fill(0x0f172a);Stroke(0x334155,2,true);break;
                case Kind.Container:
                    Box(0,0,100,100,0x152433);Box(18,25,64,55,0x38bdf8);Box(21,28,58,49,0x152433);Box(22,29,56,10,0x026292);Box(25,44,50,32,0x1a394e);Box(18,23,64,4,0x94a3b8);break;
                case Kind.Wood:
                    Box(0,0,100,100,0x1c1917);Move(20,75);Quad(40,50,55,45);Quad(70,40,85,25);Quad(82,32,68,48);Quad(55,58,35,80);Fill(0x92400e);Stroke(0x78350f,2,true);Move(48,48);Quad(55,35,62,30);Stroke(0x78350f,3);break;
                case Kind.Potato:
                    Box(0,0,100,100,0x261b14);Ellipse(50,50,35,22,0xea580c,-15);Ellipse(50,50,28,16,0xf97316,-15);Ellipse(45,48,4,4,0xfed7aa);break;
                case Kind.Banana:
                    Box(0,0,100,100,0x241e10);Move(25,65);Quad(50,25,75,35);Quad(60,55,45,78);Fill(0xfacc15);Stroke(0xeab308,2,true);Move(22,65);Quad(26,62,25,68);Stroke(0x713f12,3);Move(75,35);Quad(80,32,78,40);Stroke(0x713f12,3);break;
                case Kind.Apple:
                    Box(0,0,100,100,0x261b14);Move(50,37);Cubic(15,22,17,74,38,82);Quad(50,88,50,81);Quad(57,89,68,81);Cubic(85,68,87,23,50,37);Fill(0xdc2626);Ellipse(36,47,5,10,0xf87171,-25);Move(50,38);Quad(49,27,55,22);Stroke(0x854d0e,4);Move(53,27);Quad(75,15,69,30);Quad(60,34,53,27);Fill(0x65a30d);break;
                case Kind.Lamp:
                    Box(0,0,100,100,0x171a2e);Box(25,19,50,67,0x475569);Box(29,23,42,56,0x1e1b4b);Box(37,28,8,44,0xc4b5fd);Box(55,28,8,44,0xc4b5fd);Box(32,79,36,7,0x94a3b8);Move(39,19);Quad(50,3,61,19);Stroke(0x94a3b8,3);break;
                case Kind.Sap:
                    Box(0,0,100,100,0x231d15);Box(31,24,38,10,0xa16207);Box(27,34,46,47,0xca8a04);Box(30,37,40,40,0x78350f);Box(32,48,36,21,0xfef3c7);Ellipse(50,59,7,7,0xd97706);break;
            }
        }
        void Move(float x,float y){path.Clear();path.Add(new Vector2(x,y));}
        void Line(float x,float y)=>path.Add(new Vector2(x,y));
        void Quad(float cx,float cy,float x,float y)
        {
            var a=path[path.Count-1];var b=new Vector2(cx,cy);var c=new Vector2(x,y);for(int i=1;i<=12;i++){float t=i/12f;path.Add((1-t)*(1-t)*a+2*(1-t)*t*b+t*t*c);}
        }
        void Cubic(float ax,float ay,float bx,float by,float x,float y)
        {
            var a=path[path.Count-1];var b=new Vector2(ax,ay);var c=new Vector2(bx,by);var d=new Vector2(x,y);for(int i=1;i<=16;i++){float t=i/16f,u=1-t;path.Add(u*u*u*a+3*u*u*t*b+3*u*t*t*c+t*t*t*d);}
        }
        void Box(float x,float y,float w,float h,uint rgb){Move(x,y);Line(x+w,y);Line(x+w,y+h);Line(x,y+h);Fill(rgb);}
        void Ellipse(float x,float y,float rx,float ry,uint rgb,float angle=0)
        {
            path.Clear();float c=Mathf.Cos(angle*Mathf.Deg2Rad),s=Mathf.Sin(angle*Mathf.Deg2Rad);for(int i=0;i<40;i++){float a=i*Mathf.PI*2/40,px=Mathf.Cos(a)*rx,py=Mathf.Sin(a)*ry;path.Add(new Vector2(x+c*px-s*py,y+s*px+c*py));}Fill(rgb);
        }
        Vector3 Local(Vector2 p)=>new Vector3(bounds.xMin+p.x*.01f*bounds.width,bounds.yMax-p.y*.01f*bounds.height);
        void Fill(uint rgb)
        {
            if(path.Count<3)return;
            var points=new List<Vector2>(path);if((points[0]-points[points.Count-1]).sqrMagnitude<.001f)points.RemoveAt(points.Count-1);
            float signedArea=0;for(int i=0;i<points.Count;i++)signedArea+=Cross(points[i],points[(i+1)%points.Count]);if(signedArea<0)points.Reverse();
            int start=mesh.currentVertCount;foreach(var p in points)mesh.AddVert(Local(p),CampaignRetroTheme.Hex(rgb),Vector2.zero);
            var order=new List<int>();for(int i=0;i<points.Count;i++)order.Add(i);
            int attempts=points.Count*points.Count;
            while(order.Count>2&&attempts-->0)
            {
                bool clipped=false;for(int i=0;i<order.Count;i++)
                {
                    int a=order[(i+order.Count-1)%order.Count],b=order[i],c=order[(i+1)%order.Count];if(Cross(points[b]-points[a],points[c]-points[b])<=.00001f)continue;
                    bool inside=false;foreach(int j in order){if(j==a||j==b||j==c)continue;if(InTriangle(points[j],points[a],points[b],points[c])){inside=true;break;}}
                    if(inside)continue;mesh.AddTriangle(start+a,start+b,start+c);order.RemoveAt(i);clipped=true;break;
                }
                if(!clipped)break;
            }
        }
        static float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
        static bool InTriangle(Vector2 p,Vector2 a,Vector2 b,Vector2 c)=>Cross(b-a,p-a)>.00001f&&Cross(c-b,p-b)>.00001f&&Cross(a-c,p-c)>.00001f;
        void Stroke(uint rgb,float width,bool closed=false)
        {
            int count=closed?path.Count:path.Count-1;var tint=CampaignRetroTheme.Hex(rgb);
            for(int i=0;i<count;i++)
            {
                var a=path[i];var b=path[(i+1)%path.Count];var direction=b-a;if(direction.sqrMagnitude<.00001f)continue;var n=new Vector2(-direction.y,direction.x).normalized*(width*.5f);int first=mesh.currentVertCount;
                mesh.AddVert(Local(a+n),tint,Vector2.zero);mesh.AddVert(Local(b+n),tint,Vector2.zero);mesh.AddVert(Local(b-n),tint,Vector2.zero);mesh.AddVert(Local(a-n),tint,Vector2.zero);mesh.AddTriangle(first,first+1,first+2);mesh.AddTriangle(first,first+2,first+3);
            }
        }
    }
}
