using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BeetleArena.Campaign
{
    /// <summary>Classic opaque window/button edges, generated in the existing Image mesh without textures.</summary>
    [DisallowMultipleComponent]
    public sealed class RetroBevel : BaseMeshEffect, IPointerDownHandler, IPointerUpHandler
    {
        float thickness=1;
        Color light=Color.white,dark=Color.gray,top,bottom;
        bool gradient,pressed;
        public void Configure(float width,Color highlight,Color shadow,bool useGradient=false,Color gradientTop=default,Color gradientBottom=default)
        {
            thickness=width;light=highlight;dark=shadow;gradient=useGradient;top=gradientTop;bottom=gradientBottom;
            if(graphic)graphic.SetVerticesDirty();
        }
        public void OnPointerDown(PointerEventData e) { if(e.button==PointerEventData.InputButton.Left){pressed=true;graphic.SetVerticesDirty();} }
        public void OnPointerUp(PointerEventData e) { pressed=false;graphic.SetVerticesDirty(); }
        protected override void OnDisable(){pressed=false;base.OnDisable();}
        public override void ModifyMesh(VertexHelper mesh)
        {
            if(!IsActive()||!graphic)return;
            var r=((RectTransform)transform).rect;mesh.Clear();
            Quad(mesh,r.xMin,r.yMin,r.xMax,r.yMax,gradient?bottom:graphic.color,gradient?top:graphic.color);
            float b=Mathf.Min(thickness,Mathf.Min(r.width,r.height)*.5f);
            Color hi=pressed?dark:light,lo=pressed?light:dark;
            Quad(mesh,r.xMin,r.yMax-b,r.xMax,r.yMax,hi,hi);
            Quad(mesh,r.xMin,r.yMin,r.xMin+b,r.yMax,hi,hi);
            Quad(mesh,r.xMin+b,r.yMin,r.xMax,r.yMin+b,lo,lo);
            Quad(mesh,r.xMax-b,r.yMin,r.xMax,r.yMax-b,lo,lo);
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
