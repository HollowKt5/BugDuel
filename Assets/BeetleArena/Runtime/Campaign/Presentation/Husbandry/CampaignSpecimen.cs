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
        [Tooltip("Floor of this enclosure. If unset, uses the nearest SoilBed in this scene.")]
        public Renderer enclosureFloor;
        [Tooltip("Optional override. Otherwise uses Resources/LifeStageVisuals, with runtime defaults if no asset is installed.")]
        public CampaignLifeStageVisualProfile lifeStageVisuals;
        public float phase;
        public CampaignCareController Care { get; private set; }
        public CareEnclosureLayout Enclosure { get; private set; }
        public CampaignLifeStageDisplay LifeStageDisplay { get; private set; }
        public bool ConstructionHidden { get; private set; }
        public float ConstructionClearance => clearance*rig.model.lossyScale.magnitude/initialScale;
        public Vector2 BodyFootprint => restingFootprint*rig.model.lossyScale.magnitude/initialScale;
        Vector2 MeasureBodyFootprint()
        {
            var low=Vector3.one*float.PositiveInfinity;var high=Vector3.one*float.NegativeInfinity;
            foreach(var renderer in renderers)
            {
                var b=renderer.localBounds;
                for(int i=0;i<8;i++)
                {
                    var corner=b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                    var p=Quaternion.Inverse(transform.rotation)*(renderer.transform.TransformPoint(corner)-transform.position);
                    low=Vector3.Min(low,p);high=Vector3.Max(high,p);
                }
            }
            return new Vector2(high.x-low.x,high.z-low.z);
        }
        Quaternion rest;
        Vector3 origin;
        CarePosePresenter pose;
        CareGroundSurface groundSurface;
        public CareFootPlant Feet => pose?.Feet;
        public CampaignBeetleGrabController Grab { get; internal set; }
        public float GroundOffset => rootGroundOffset;
        MaterialPropertyBlock block;
        string lastId,lastMutation;
        bool lastOverturned,lastDead;
        CampaignCatalog catalog;
        Renderer[] renderers;
        GameObject dish,food,shelter;
        public Renderer FeedingDish => dish&&dish.activeInHierarchy?dish.GetComponent<Renderer>():null;
        Material propMaterial;
        Matrix4x4 floorMatrix;
        Bounds floorBounds;
        Vector3 modelScale;
        Renderer boundFloor;
        float clearance,initialScale,rootGroundOffset;
        Vector2 restingFootprint;
        CampaignCatalog layoutCatalog;
        string layoutHabitat,layoutBeetle;

        void Start()
        {
            origin=transform.position;rest=transform.rotation;
            block=new MaterialPropertyBlock();renderers=GetComponentsInChildren<Renderer>();
            if(!enclosureFloor)
            {
                float nearest=float.PositiveInfinity;
                foreach(var candidate in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    if(candidate.name=="SoilBed"&&candidate.gameObject.scene==gameObject.scene)
                    { float d=(candidate.bounds.ClosestPoint(origin)-origin).sqrMagnitude;if(d<nearest){enclosureFloor=candidate;nearest=d;} }
            }
            float groundHeight=enclosureFloor?enclosureFloor.bounds.max.y:origin.y;
            rootGroundOffset=origin.y-groundHeight;
            if(selected&&enclosureFloor)
                foreach(var item in (enclosureFloor.transform.parent?enclosureFloor.transform.parent:enclosureFloor.transform).GetComponentsInChildren<Renderer>())
                    if(item.name=="FoodDish"||item.name=="FoodJelly")item.gameObject.SetActive(false);
            if(rig){pose=new CarePosePresenter(rig,groundHeight);pose.Reset(false,false,CareLocomotion.Seed(name+phase));}
            if(selected)
            {
                Care=gameObject.AddComponent<CampaignCareController>();
                if(rig){clearance=CareEnclosureLayout.MeasureClearance(rig);initialScale=Mathf.Max(.001f,rig.model.lossyScale.magnitude);restingFootprint=MeasureBodyFootprint();}
                RefreshEnclosure(null,null);
                propMaterial=new Material(Shader.Find("BeetleArena/Toon")) { name="Care observation props" };
                propMaterial.SetFloat("_SpecStrength",.035f);propMaterial.SetFloat("_RimStrength",.05f);
                propMaterial.SetFloat("_OutlineWidth",.65f);
                dish=Prop("CareFoodDish",new Vector3(.55f,.04f,.55f),new Color(.46f,.38f,.25f));
                food=Prop("CareFood",new Vector3(.3f,.06f,.3f),new Color(.8f,.45f,.12f));
                shelter=Prop("CareRestingLeaf",new Vector3(.7f,.015f,1),new Color(.22f,.35f,.12f));
                PlaceProps();
                LifeStageDisplay=gameObject.AddComponent<CampaignLifeStageDisplay>();
                LifeStageDisplay.Bind(this,lifeStageVisuals);
            }
        }
        bool RefreshEnclosure(CampaignBeetle beetle,CampaignCatalog content)
        {
            if(!selected||!enclosureFloor||!rig||!Care)return false;
            var matrix=enclosureFloor.localToWorldMatrix;var bounds=enclosureFloor.localBounds;var scale=rig.model.lossyScale;
            if(Enclosure!=null&&boundFloor==enclosureFloor&&floorMatrix==matrix&&floorBounds==bounds&&modelScale==scale&&layoutCatalog==content&&layoutHabitat==beetle?.habitat&&layoutBeetle==beetle?.id)return false;
            var session=CampaignApp.Instance?.Session;
            var surface=new Vector2(bounds.size.x*Mathf.Abs(enclosureFloor.transform.lossyScale.x),bounds.size.z*Mathf.Abs(enclosureFloor.transform.lossyScale.z))*.94f;
            var scenery=beetle!=null&&session!=null?CampaignConstructionHabitat.Blockers(session.DecorationSpace(beetle.id),surface):null;
            Enclosure=new CareEnclosureLayout(enclosureFloor,rig,ConstructionClearance,scenery);
            boundFloor=enclosureFloor;floorMatrix=matrix;floorBounds=bounds;modelScale=scale;layoutCatalog=content;layoutHabitat=beetle?.habitat;layoutBeetle=beetle?.id;
            Care.ConfigureArea(Enclosure.Area);
            groundSurface?.Dispose();groundSurface=new CareGroundSurface(Enclosure);
            if(Feet!=null){Feet.GroundHeight=Enclosure.GroundHeight;Feet.SurfaceHeight=groundSurface.HeightAt;Feet.Reset();}
            PlaceProps();return true;
        }
        public void RefreshConstructionArea()
        {
            var session=CampaignApp.Instance?.Session;
            if(!selected||session==null||LifeStageDisplay&&LifeStageDisplay.HasStageEnvironment)return;
            boundFloor=null;
            RefreshEnclosure(session.State.beetles.Find(b=>b.id==session.State.selectedId),session.Catalog);
        }
        public void SetConstructionHidden(bool hidden)
        {
            ConstructionHidden=hidden;
            if(!hidden)RefreshConstructionArea();
            var state=CampaignApp.Instance?.Session?.State;
            ApplyVisibility(state?.beetles.Find(b=>b.id==state.selectedId));
        }
        void ApplyVisibility(CampaignBeetle beetle)
        {
            foreach(var renderer in renderers)if(renderer)renderer.enabled=!selected||(!ConstructionHidden&&beetle!=null&&LifeStageDisplay.UsesAdultModel);
            LifeStageDisplay?.SetAnimalVisible(!ConstructionHidden);
            bool active=beetle!=null&&beetle.stage==CampaignLifeStage.ActiveAdult;
            if(dish)dish.SetActive(!ConstructionHidden&&active&&!beetle.dead);
            if(food)food.SetActive(!ConstructionHidden&&active&&(beetle.meal!=null||beetle.foodResidue)&&!beetle.dead);
            if(shelter)shelter.SetActive(!ConstructionHidden&&active);
        }
        void PlaceProps()
        {
            if(Enclosure==null)return;
            if(dish)dish.transform.position=Enclosure.ToWorld(Enclosure.Area.FoodPoint)+Vector3.up*.02f;
            if(food)food.transform.position=Enclosure.ToWorld(Enclosure.Area.FoodPoint)+Vector3.up*.07f;
            if(shelter)shelter.transform.position=Enclosure.ToWorld(Enclosure.Area.ShelterPoint)+Vector3.up*.0075f;
        }
        GameObject Prop(string name,Vector3 scale,Color color)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Sphere);go.name=name;
            // Siblings, not children: the dish and resting spot stay put while the animal walks.
            go.transform.SetParent(transform.parent,false);go.transform.position=origin;go.transform.localScale=scale;
            Destroy(go.GetComponent<Collider>());go.GetComponent<Renderer>().sharedMaterial=propMaterial;
            SetColor(go.GetComponent<Renderer>(),color);return go;
        }
        void SetColor(Renderer renderer,Color color)
        { block.Clear();block.SetColor("_BaseColor",color);block.SetColor("_Color",color);renderer.SetPropertyBlock(block); }

        void LateUpdate()
        {
            var app=CampaignApp.Instance;CampaignBeetle b=null;
            if(selected&&app?.Session!=null)b=app.Session.State.beetles.Find(x=>x.id==app.Session.State.selectedId);
            bool stageChanged=LifeStageDisplay&&LifeStageDisplay.Present(b,app?.Session,app&&app.CalendarRunning?Mathf.Min(Time.unscaledDeltaTime,.05f):0);
            bool activeStage=b==null||b.stage==CampaignLifeStage.ActiveAdult;
            if(stageChanged&&activeStage)boundFloor=null;
            bool layoutChanged=activeStage&&RefreshEnclosure(b,app?.Session?.Catalog);
            if(Grab)Grab.ValidateState();
            ApplyVisibility(b);
            if(selected&&b==null) { lastId=null;return; }
            bool changed=b!=null&&lastId!=b.id;
            bool pausedFlagsChanged=b!=null&&Care&&Care.Paused&&(lastOverturned!=b.overturned||lastDead!=b.dead);
            var resident=Care?Care.Find(b?.id):null;
            if(b!=null&&b.stage==CampaignLifeStage.DormantAdult)
            {
                // The emerged beetle keeps its original articulation, resting in the chamber without walking or feeding.
                transform.SetPositionAndRotation(LifeStageDisplay.AdultPosition+Vector3.up*rootGroundOffset,LifeStageDisplay.AdultRotation);
                if(changed||stageChanged||pausedFlagsChanged)
                {
                    if(Feet!=null){Feet.GroundHeight=LifeStageDisplay.GroundHeight;Feet.SurfaceHeight=null;Feet.Reset();}
                    pose?.Reset(false,b.dead,CareLocomotion.Seed(b.id));
                    if(!b.dead)pose?.Present(CareState.Resting,0,1);
                }
                if(!b.dead)pose?.PresentAmbient(CareState.Resting,Mathf.Min(Time.unscaledDeltaTime,.05f));
            }
            else if(resident!=null&&activeStage)
            {
                var displayState=b.dead?CareState.Dead:b.overturned?CareState.Struggling:resident.Brain.State;
                transform.position=Grab&&Grab.IsManipulating?Grab.HeldWorldPosition:Enclosure!=null?Enclosure.ToWorld(resident.Motor.Position)+Vector3.up*rootGroundOffset:
                    origin+new Vector3(resident.Motor.Position.x,0,resident.Motor.Position.y);
                transform.rotation=Quaternion.Euler(0,resident.Motor.Heading+(Enclosure?.Heading??0),0);
                if(changed||layoutChanged||stageChanged||pausedFlagsChanged)
                {
                    if(Feet!=null&&Enclosure!=null){Feet.GroundHeight=Enclosure.GroundHeight;Feet.SurfaceHeight=groundSurface.HeightAt;Feet.Reset();}
                    pose?.Reset(b.overturned,b.dead,CareLocomotion.Seed(b.id));
                }
                if(Grab&&Grab.IsManipulating)
                    pose?.PresentHeld(Mathf.Min(Time.unscaledDeltaTime,.05f),Grab.StruggleWeight);
                else if(!Care.Paused&&!app.Busy&&!app.Modal || changed||layoutChanged||stageChanged)
                    pose?.Present(displayState,resident.Motor.Speed,resident.PoseSeconds,resident.Motor.LookYaw);
                else
                    pose?.PresentAmbient(displayState,Mathf.Min(Time.unscaledDeltaTime,.05f));
                if(food&&b.meal!=null)
                {
                    float amount=1-(float)resident.Brain.MealProgress*.65f;
                    food.transform.localScale=new Vector3(.3f*amount,.06f,.3f*amount);
                    food.transform.position=Enclosure.ToWorld(Enclosure.Area.FoodPoint)+Vector3.up*.07f;
                    SetColor(food.GetComponent<Renderer>(),b.meal.food==0?new Color(.8f,.4f,.13f):b.meal.food==1?new Color(.9f,.74f,.22f):new Color(.68f,.18f,.1f));
                }
                else if(food&&b.foodResidue)
                {
                    food.transform.localScale=new Vector3(.17f,.014f,.13f);
                    food.transform.position=Enclosure.ToWorld(Enclosure.Area.FoodPoint)+Vector3.up*.043f;
                    SetColor(food.GetComponent<Renderer>(),new Color(.24f,.15f,.07f));
                }
            }
            else if(!selected)
            {
                transform.rotation=rest*Quaternion.Euler(0,Mathf.Sin((Time.time+phase)*.3f)*3,0);
                pose?.Present(CareState.Idle,0,Mathf.Min(Time.unscaledDeltaTime,.05f));
            }
            if(b!=null){lastOverturned=b.overturned;lastDead=b.dead;}
            if(b!=null&&(lastId!=b.id||lastMutation!=b.mutation||catalog!=app.Session.Catalog))
            {
                lastId=b.id;lastMutation=b.mutation;catalog=app.Session.Catalog;
                Color c=Color.HSVToRGB((float)catalog.Species(b.speciesId).hue,.58f,.8f);
                if(b.mutation=="纯金壳")c=new Color(.85f,.65f,.15f);
                if(b.mutation=="紫绀色")c=new Color(.45f,.15f,.65f);
                Tint(rig,c,block,true);
            }
        }
        internal void RestoreAfterGrab()
        {
            var app=CampaignApp.Instance;var b=app?.Session?.State.beetles.Find(value=>value.id==app.Session.State.selectedId);
            var resident=Care?Care.Find(b?.id):null;
            if(resident!=null&&Enclosure!=null)transform.position=Enclosure.ToWorld(resident.Motor.Position)+Vector3.up*rootGroundOffset;
            pose?.Reset(b?.overturned??false,b?.dead??false,CareLocomotion.Seed(b?.id));
        }
        void OnDestroy()
        { groundSurface?.Dispose();if(dish)Destroy(dish);if(food)Destroy(food);if(shelter)Destroy(shelter);if(propMaterial)Destroy(propMaterial); }
        public static void Tint(BeetleProceduralAnimator rig,Color color,MaterialPropertyBlock block=null,bool careLighting=false)
        {
            if(!rig||rig.shellRenderers==null)return;
            block=block??new MaterialPropertyBlock();block.Clear();block.SetColor("_BaseColor",color);block.SetColor("_Color",color);
            if(careLighting)
            {
                block.SetFloat("_AmbientStrength",.48f);block.SetFloat("_SpecStrength",.3f);
                block.SetFloat("_BandSoftness",.16f);block.SetFloat("_OutlineWidth",.7f);
                block.SetFloat("_SpecPower",48);block.SetFloat("_SpecSoftness",.17f);
                block.SetColor("_ShadowColor",new Color(.48f,.5f,.58f));
            }
            foreach(var r in rig.shellRenderers)if(r)r.SetPropertyBlock(block);
        }
    }
}
