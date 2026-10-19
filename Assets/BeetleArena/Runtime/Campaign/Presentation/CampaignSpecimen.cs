using UnityEngine;
using BugDuel.Campaign;
using BugDuel.Domain;

namespace BeetleArena.Campaign
{
    /// <summary>Model presentation only. CampaignCareController owns decisions; domain owns health and food.</summary>
    public sealed class CampaignSpecimen : MonoBehaviour
    {
        public bool selected;
        public BeetleProceduralAnimator rig;
        public float phase;
        public CampaignCareController Care { get; private set; }
        Quaternion rest;
        Vector3 origin;
        CarePosePresenter pose;
        public CareFootPlant Feet => pose?.Feet;
        MaterialPropertyBlock block;
        string lastId,lastMutation;
        CampaignCatalog catalog;
        Renderer[] renderers;
        GameObject dish,food,shelter;
        Material propMaterial;

        void Start()
        {
            origin=transform.position;rest=transform.rotation;
            block=new MaterialPropertyBlock();renderers=GetComponentsInChildren<Renderer>();
            var soil=GameObject.Find("SoilBed");
            float groundHeight=soil?soil.GetComponent<Renderer>().bounds.max.y:origin.y;
            if(selected&&soil&&rig)
            {
                var bounds=soil.GetComponent<Renderer>().bounds;
                float radius=0;
                foreach(var leg in rig.legs)
                { var point=rig.model.TransformPoint(leg.restAnkle)-origin;radius=Mathf.Max(radius,new Vector2(point.x,point.z).magnitude); }
                radius+=.6f; // Footprint in any heading, plus the bounded observation motion.
                Vector3 safe=origin;safe.x=Mathf.Clamp(safe.x,bounds.min.x+radius,bounds.max.x-radius);
                safe.z=Mathf.Clamp(safe.z,bounds.min.z+radius,bounds.max.z-radius);
                var backdrop=FindFirstObjectByType<CampaignBackdrop>();
                if(backdrop&&backdrop.sceneCamera)backdrop.sceneCamera.transform.position+=safe-origin;
                origin=safe;transform.position=origin;
                // This small presentation pen is intentionally flat: decorative pebbles are not walkable terrain.
                foreach(var renderer in soil.transform.parent.GetComponentsInChildren<Renderer>())
                {
                    if(!renderer.name.StartsWith("Substrate"))continue;
                    Vector3 nearest=renderer.bounds.ClosestPoint(origin);
                    if(new Vector2(nearest.x-origin.x,nearest.z-origin.z).magnitude<radius)renderer.gameObject.SetActive(false);
                }
                var oldDish=GameObject.Find("FoodDish");if(oldDish)oldDish.SetActive(false);
                var oldJelly=GameObject.Find("FoodJelly");if(oldJelly)oldJelly.SetActive(false);
            }
            if(rig)pose=new CarePosePresenter(rig,groundHeight);
            if(selected)
            {
                Care=gameObject.AddComponent<CampaignCareController>();
                propMaterial=new Material(Shader.Find("BeetleArena/Toon")) { name="Care observation props" };
                dish=Prop("CareFoodDish",new Vector3(CareLocomotion.FoodPoint.x,groundHeight-origin.y+.02f,CareLocomotion.FoodPoint.y),new Vector3(.55f,.04f,.55f),new Color(.46f,.38f,.25f));
                food=Prop("CareFood",new Vector3(CareLocomotion.FoodPoint.x,groundHeight-origin.y+.07f,CareLocomotion.FoodPoint.y),new Vector3(.3f,.06f,.3f),new Color(.8f,.45f,.12f));
                shelter=Prop("CareRestingLeaf",new Vector3(CareLocomotion.ShelterPoint.x,groundHeight-origin.y+.0075f,CareLocomotion.ShelterPoint.y),new Vector3(.7f,.015f,1),new Color(.22f,.35f,.12f));
            }
        }
        GameObject Prop(string name,Vector3 offset,Vector3 scale,Color color)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Sphere);go.name=name;
            // Siblings, not children: the dish and resting spot stay put while the animal walks.
            go.transform.SetParent(transform.parent,false);go.transform.position=origin+offset;go.transform.localScale=scale;
            Destroy(go.GetComponent<Collider>());go.GetComponent<Renderer>().sharedMaterial=propMaterial;
            SetColor(go.GetComponent<Renderer>(),color);return go;
        }
        void SetColor(Renderer renderer,Color color)
        { block.Clear();block.SetColor("_BaseColor",color);block.SetColor("_Color",color);renderer.SetPropertyBlock(block); }

        void LateUpdate()
        {
            var app=CampaignApp.Instance;CampaignBeetle b=null;
            if(selected&&app?.Session!=null)b=app.Session.State.beetles.Find(x=>x.id==app.Session.State.selectedId);
            foreach(var r in renderers)if(r)r.enabled=!selected||b!=null;
            if(dish)dish.SetActive(b!=null&&!b.dead&&b.Adult(app.Session.State.week));
            if(food)food.SetActive(b?.meal!=null&&!b.dead);
            if(shelter)shelter.SetActive(b!=null);
            if(selected&&b==null) { lastId=null;return; }
            bool changed=b!=null&&lastId!=b.id;
            var resident=Care?Care.Find(b?.id):null;
            if(resident!=null)
            {
                transform.position=origin+new Vector3(resident.Motor.Position.x,0,resident.Motor.Position.y);
                transform.rotation=Quaternion.Euler(0,resident.Motor.Heading,0);
                if(changed)pose?.Reset(b.overturned,b.dead);
                if(!Care.Paused&&!app.Busy&&!app.Modal || changed)
                    pose?.Present(resident.Brain.State,resident.Motor.Speed,resident.PoseSeconds);
                if(food&&b.meal!=null)
                {
                    float amount=1-(float)resident.Brain.MealProgress*.65f;
                    food.transform.localScale=new Vector3(.3f*amount,.06f,.3f*amount);
                    SetColor(food.GetComponent<Renderer>(),b.meal.food==0?new Color(.8f,.4f,.13f):b.meal.food==1?new Color(.9f,.74f,.22f):new Color(.68f,.18f,.1f));
                }
            }
            else if(!selected)
            {
                transform.rotation=rest*Quaternion.Euler(0,Mathf.Sin((Time.time+phase)*.3f)*3,0);
                pose?.Present(CareState.Idle,0,Mathf.Min(Time.unscaledDeltaTime,.05f));
            }
            if(b!=null&&(lastId!=b.id||lastMutation!=b.mutation||catalog!=app.Session.Catalog))
            {
                lastId=b.id;lastMutation=b.mutation;catalog=app.Session.Catalog;
                Color c=Color.HSVToRGB((float)catalog.Species(b.speciesId).hue,.65f,.65f);
                if(b.mutation=="纯金壳")c=new Color(.85f,.65f,.15f);
                if(b.mutation=="紫绀色")c=new Color(.45f,.15f,.65f);
                Tint(rig,c,block);
            }
        }
        void OnDestroy()
        { if(dish)Destroy(dish);if(food)Destroy(food);if(shelter)Destroy(shelter);if(propMaterial)Destroy(propMaterial); }
        public static void Tint(BeetleProceduralAnimator rig,Color color,MaterialPropertyBlock block=null)
        {
            if(!rig||rig.shellRenderers==null)return;
            block=block??new MaterialPropertyBlock();block.Clear();block.SetColor("_BaseColor",color);block.SetColor("_Color",color);
            foreach(var r in rig.shellRenderers)if(r)r.SetPropertyBlock(block);
        }
    }
}
