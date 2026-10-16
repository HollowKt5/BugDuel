using UnityEngine;
using BugDuel.Campaign;
namespace BeetleArena.Campaign
{
    public sealed class CampaignSpecimen : MonoBehaviour
    {
        public bool selected;
        public BeetleProceduralAnimator rig;
        public float phase;
        Quaternion rest,headRest,bodyRotation;
        Vector3 bodyPosition;
        Quaternion[] legs,knees,ankles;
        MaterialPropertyBlock block;
        string lastId,lastMutation;
        int revision;
        float roll,eating;
        double nutrition;
        Renderer[] renderers;
        void Start()
        {
            rest=transform.localRotation; block=new MaterialPropertyBlock();renderers=GetComponentsInChildren<Renderer>();
            if(rig && rig.importedRig)rig.importedRig.ResetRig();
            if(rig && rig.headPivot)headRest=rig.headPivot.localRotation;
            if(rig && rig.bodyPivot){bodyPosition=rig.bodyPivot.localPosition;bodyRotation=rig.bodyPivot.localRotation;}
            if (rig && rig.legs != null)
            {
                legs=new Quaternion[rig.legs.Length];knees=new Quaternion[legs.Length];ankles=new Quaternion[legs.Length];
                for(int i=0;i<legs.Length;i++){var leg=rig.legs[i];if(leg.hip)legs[i]=leg.hip.localRotation;if(leg.knee)knees[i]=leg.knee.localRotation;if(leg.ankle)ankles[i]=leg.ankle.localRotation;}
            }
        }
        void Update()
        {
            var app=CampaignApp.Instance; CampaignBeetle b=null;
            if (selected && app?.Session!=null) b=app.Session.State.beetles.Find(x=>x.id==app.Session.State.selectedId);
            foreach(var r in renderers)if(r)r.enabled=!selected||b!=null;
            bool changed=b!=null&&lastId!=b.id;
            if(changed){nutrition=b.nutrition;roll=b.overturned||b.dead?180:0;eating=0;}
            if(b!=null&&b.nutrition>nutrition+.1)eating=2.5f;
            if(b!=null)nutrition=b.nutrition;
            eating=Mathf.Max(0,eating-Time.deltaTime);
            float t=Time.time+phase;
            bool dead=b!=null&&b.dead,struggling=b!=null&&b.overturned;
            transform.localRotation=rest*Quaternion.Euler(0,dead?0:Mathf.Sin(t*.3f)*3,0);
            roll=Mathf.Lerp(roll,struggling||dead?180:0,1-Mathf.Exp(-Time.deltaTime*6));
            if(rig && rig.bodyPivot)
            {
                rig.bodyPivot.localRotation=bodyRotation*Quaternion.Euler(0,0,roll+(struggling?Mathf.Sin(t*8)*8:0));
                float y=Mathf.Lerp(bodyPosition.y,.57f,roll/180)+Mathf.Abs(Mathf.Sin(roll*Mathf.Deg2Rad))*.4f;
                rig.bodyPivot.localPosition=new Vector3(bodyPosition.x,y,bodyPosition.z);
            }
            if (rig && rig.headPivot) rig.headPivot.localRotation=headRest*Quaternion.Euler(dead?0:eating>0?5+Mathf.Sin(t*15)*3:Mathf.Sin(t*1.2f)*3,0,0);
            if (rig && legs!=null) for(int i=0;i<legs.Length;i++) if(rig.legs[i].hip)
            {
                var leg=rig.legs[i];float wave=Mathf.Sin(t*(struggling?12:1.5f)+i);
                leg.hip.localRotation=legs[i]*Quaternion.Euler(dead?0:wave*(struggling?24:2),0,0);
                if(leg.knee)leg.knee.localRotation=knees[i]*Quaternion.Euler(0,0,dead?20:struggling?leg.side*(20+22*wave):0);
                if(leg.ankle)leg.ankle.localRotation=ankles[i]*Quaternion.Euler(0,0,struggling?wave*16:0);
            }
            if(b!=null && (lastId!=b.id || lastMutation!=b.mutation || revision!=app.Session.Catalog.revision))
            {
                lastId=b.id;lastMutation=b.mutation;revision=app.Session.Catalog.revision;
                Color c=Color.HSVToRGB((float)app.Session.Catalog.Species(b.speciesId).hue,.65f,.65f);
                if(b.mutation=="纯金壳") c=new Color(.85f,.65f,.15f);
                if(b.mutation=="紫绀色") c=new Color(.45f,.15f,.65f);
                Tint(rig,c,block);
            }
        }
        public static void Tint(BeetleProceduralAnimator rig,Color color,MaterialPropertyBlock block=null)
        {
            if(!rig || rig.shellRenderers==null) return; block=block??new MaterialPropertyBlock(); block.SetColor("_BaseColor",color);block.SetColor("_Color",color);
            foreach(var r in rig.shellRenderers) if(r) r.SetPropertyBlock(block);
        }
    }
}
