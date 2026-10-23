using System;
using System.Collections.Generic;
using System.Linq;
using BugDuel.Campaign;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using SpecimenRecord=BugDuel.Campaign.CampaignSpecimen;

namespace BeetleArena.Campaign
{
    // The exhibition is presentation only. Placement and ownership are committed by CampaignApp.
    public sealed class CampaignSpecimenRoom : MonoBehaviour
    {
        [Min(.1f), Tooltip("1 = previous wheel speed; 10 = ten times faster.")]
        public float zoomSpeedMultiplier = 10f;
        public GameObject beetlePrefab;
        public TMP_FontAsset font;
        public Action<int> SlotClicked;
        public Action<string,int> MoveRequested;
        public bool Arranging { get; private set; }
        public Bounds InteriorBounds { get; private set; }
        public const int Columns=4, Rows=3;
        const float MaxYaw=38,MaxPitch=26,MaxPanX=5,MinPanY=-3.6f,MaxPanY=4,MaxZoom=1.3f;
        const float FloorY=-5.325f,WallZ=.235f,WallThickness=.45f;
        readonly Transform[] mounts=new Transform[12];
        readonly Renderer[][] borders=new Renderer[12][];
        readonly TMP_Text[] plaques=new TMP_Text[12];
        readonly GameObject[] models=new GameObject[12];
        readonly Vector3[] modelSizes=new Vector3[12];
        readonly string[] ids=new string[12];
        readonly List<Material> materials=new List<Material>();
        readonly List<Transform> wallBattens=new List<Transform>();
        Transform roomShell,backWall,leftWall,rightWall,frontWall,ceiling,roomFloor,backSkirting,leftSkirting,rightSkirting;
        MaterialPropertyBlock tint;
        CampaignBackdrop backdrop;
        Camera cameraView;
        CampaignApp app;
        CampaignState shownState;
        Material wood,brass,ebony,linen;
        Vector3 focus,targetFocus;
        float yaw,pitch,distance,targetDistance,overviewDistance,aspect;
        float roomDistance;
        int selectedSlot=-1,dragSlot=-1,hoverSlot=-1;
        Vector2 pressPoint;
        bool dragged;

        public void Bind(CampaignBackdrop world)
        {
            backdrop=world;cameraView=world.sceneCamera;app=CampaignApp.Ensure();tint=new MaterialPropertyBlock();
            cameraView.orthographic=false;cameraView.fieldOfView=46;cameraView.nearClipPlane=.08f;
            cameraView.backgroundColor=new Color(.07f,.09f,.105f);
            BuildRoom();app.Changed+=Refresh;ResetView();Refresh();
        }
        public static Vector3 SlotPosition(int slot) => new Vector3((slot%Columns-1.5f)*2.85f,(1-slot/Columns)*3.3f+.45f,0);
        Material Finish(string name,Color color,float metallic=0)
        {
            var material=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name};
            material.SetColor("_BaseColor",color);material.SetFloat("_Metallic",metallic);material.SetFloat("_Smoothness",.28f);
            materials.Add(material);return material;
        }
        GameObject Box(string name,Transform parent,Vector3 position,Vector3 size,Material material)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);
            go.transform.localPosition=position;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=material;
            Destroy(go.GetComponent<Collider>());return go;
        }
        void BuildRoom()
        {
            var room=new GameObject("Specimen gallery · 12 places").transform;room.SetParent(transform,false);
            wood=Finish("Walnut frame",new Color(.24f,.115f,.055f));
            brass=Finish("Brushed brass",new Color(.63f,.43f,.17f),.65f);
            ebony=Finish("Ebony frame",new Color(.045f,.055f,.06f),.15f);
            linen=Finish("Warm archival linen",new Color(.77f,.73f,.59f));
            var wall=Finish("Midnight gallery wall",new Color(.105f,.16f,.17f));
            var floor=Finish("Gallery oak floor",new Color(.19f,.125f,.075f));
            roomShell=new GameObject("Continuous gallery interior").transform;roomShell.SetParent(room,false);
            backWall=Box("Exhibition wall",roomShell,Vector3.zero,Vector3.one,wall).transform;
            leftWall=Box("Left wall",roomShell,Vector3.zero,Vector3.one,wall).transform;
            rightWall=Box("Right wall",roomShell,Vector3.zero,Vector3.one,wall).transform;
            frontWall=Box("Wall behind camera",roomShell,Vector3.zero,Vector3.one,wall).transform;
            ceiling=Box("Ceiling",roomShell,Vector3.zero,Vector3.one,wall).transform;
            roomFloor=Box("Floor",roomShell,Vector3.zero,Vector3.one,floor).transform;
            // The gallery uses directional studio lights; the enclosure receives exhibit shadows
            // without its new ceiling blocking that existing lighting rig.
            foreach(var surface in new[]{backWall,leftWall,rightWall,frontWall,ceiling,roomFloor})
                surface.GetComponent<Renderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            backSkirting=Box("Skirting",roomShell,Vector3.zero,Vector3.one,wood).transform;
            leftSkirting=Box("Left skirting",roomShell,Vector3.zero,Vector3.one,wood).transform;
            rightSkirting=Box("Right skirting",roomShell,Vector3.zero,Vector3.one,wood).transform;
            for(int i=0;i<12;i++)
            {
                var mount=new GameObject("Exhibit "+(i+1).ToString("00")).transform;mount.SetParent(room,false);mount.localPosition=SlotPosition(i);mounts[i]=mount;
                Box("Shadow box",mount,new Vector3(0,0,.06f),new Vector3(2.48f,2.64f,.22f),ebony);
                Box("Linen backing",mount,new Vector3(0,0,-.065f),new Vector3(2.24f,2.4f,.04f),linen);
                borders[i]=new[] {
                    Box("Frame left",mount,new Vector3(-1.19f,0,-.14f),new Vector3(.16f,2.64f,.22f),wood).GetComponent<Renderer>(),
                    Box("Frame right",mount,new Vector3(1.19f,0,-.14f),new Vector3(.16f,2.64f,.22f),wood).GetComponent<Renderer>(),
                    Box("Frame top",mount,new Vector3(0,1.24f,-.14f),new Vector3(2.24f,.16f,.22f),wood).GetComponent<Renderer>(),
                    Box("Frame bottom",mount,new Vector3(0,-1.24f,-.14f),new Vector3(2.24f,.16f,.22f),wood).GetComponent<Renderer>() };
                Box("Engraved plaque",mount,new Vector3(0,-1.59f,-.09f),new Vector3(2.35f,.56f,.08f),brass);
                var label=new GameObject("Species · date",typeof(TextMeshPro));label.transform.SetParent(mount,false);
                label.transform.localPosition=new Vector3(0,-1.59f,-.137f);
                var text=label.GetComponent<TextMeshPro>();text.font=font;text.fontSize=1.7f;text.enableAutoSizing=true;text.fontSizeMin=1;text.fontSizeMax=1.7f;
                text.color=new Color(.065f,.045f,.025f);text.alignment=TextAlignmentOptions.Center;text.richText=false;
                text.textWrappingMode=TextWrappingModes.Normal;text.rectTransform.sizeDelta=new Vector2(2.2f,.52f);plaques[i]=text;
            }
        }
        void FitRoomToCamera()
        {
            // The complete camera envelope stays inside a closed room, including narrow windows.
            // Keep the larger envelope during a resize so the smoothed camera never crosses a wall.
            roomDistance=Mathf.Max(roomDistance,distance,overviewDistance*MaxZoom);
            float halfWidth=Mathf.Max(18,MaxPanX+Mathf.Sin(MaxYaw*Mathf.Deg2Rad)*roomDistance+2);
            float top=Mathf.Max(14,MaxPanY+Mathf.Sin(MaxPitch*Mathf.Deg2Rad)*roomDistance+2);
            float front=-roomDistance-2;
            float width=halfWidth*2,height=top-FloorY,depth=WallZ-front;
            float centerY=(top+FloorY)*.5f,centerZ=(WallZ+front)*.5f,t=WallThickness;
            InteriorBounds=new Bounds(new Vector3(0,centerY,centerZ),new Vector3(width,height,depth));
            SetBox(backWall,new Vector3(0,centerY,WallZ+t*.5f),new Vector3(width+t*2,height+t*2,t));
            SetBox(frontWall,new Vector3(0,centerY,front-t*.5f),new Vector3(width+t*2,height+t*2,t));
            SetBox(leftWall,new Vector3(-halfWidth-t*.5f,centerY,centerZ),new Vector3(t,height+t*2,depth));
            SetBox(rightWall,new Vector3(halfWidth+t*.5f,centerY,centerZ),new Vector3(t,height+t*2,depth));
            SetBox(ceiling,new Vector3(0,top+t*.5f,centerZ),new Vector3(width+t*2,t,depth+t*2));
            SetBox(roomFloor,new Vector3(0,FloorY-.125f,centerZ),new Vector3(width+t*2,.25f,depth+t*2));
            SetBox(backSkirting,new Vector3(0,FloorY+.11f,WallZ-.15f),new Vector3(width,.22f,.3f));
            SetBox(leftSkirting,new Vector3(-halfWidth+.15f,FloorY+.11f,centerZ),new Vector3(.3f,.22f,depth));
            SetBox(rightSkirting,new Vector3(halfWidth-.15f,FloorY+.11f,centerZ),new Vector3(.3f,.22f,depth));
            int pairs=Mathf.CeilToInt(halfWidth/1.5f)-1,count=pairs*2+1;
            while(wallBattens.Count<count)
                wallBattens.Add(Box("Wall batten",roomShell,Vector3.zero,Vector3.one,brass).transform);
            for(int i=0;i<count;i++)
                SetBox(wallBattens[i],new Vector3((i-pairs)*1.5f,(FloorY+.22f+top)*.5f,.17f),new Vector3(.035f,height-.22f,.08f));
            // Every view ray must reach its room surface before the far plane.
            cameraView.farClipPlane=Mathf.Max(100,InteriorBounds.size.magnitude+1);
        }
        static void SetBox(Transform box,Vector3 position,Vector3 size)
        {box.localPosition=position;box.localScale=size;}
        public void Refresh()
        {
            if(app.Session==null)return;
            shownState=app.Session.State;
            for(int i=0;i<12;i++)
            {
                var record=shownState.specimens.FirstOrDefault(s=>s.wallSlot==i);
                if(ids[i]!=record?.id)
                {
                    if(models[i]){models[i].SetActive(false);Destroy(models[i]);}
                    models[i]=record==null?null:MountModel(record,mounts[i],out modelSizes[i]);ids[i]=record?.id;
                }
                if(models[i])
                {
                    float angle=(float)record.rotation*Mathf.Deg2Rad,c=Mathf.Abs(Mathf.Cos(angle)),s=Mathf.Abs(Mathf.Sin(angle));
                    var size=modelSizes[i];float scale=Mathf.Min(1,2.02f/(c*size.x+s*size.y),2.16f/(s*size.x+c*size.y));
                    models[i].transform.localRotation=Quaternion.Euler(0,0,(float)record.rotation);
                    models[i].transform.localScale=Vector3.one*scale;
                    models[i].transform.localPosition=new Vector3(0,0,-.12f-size.z*scale*.5f);
                }
                var finish=record==null||record.frameStyle==0?wood:record.frameStyle==1?brass:ebony;
                foreach(var renderer in borders[i])renderer.sharedMaterial=finish;
                string plaqueSize=record==null?"":record.beetle.stage<CampaignLifeStage.DormantAdult?CampaignSession.StageName(record.beetle.stage):$"{record.beetle.length:0.0} mm";
                CampaignLocalization.Bind(plaques[i],record==null?$"{i+1:00}   /   留给下一份收藏":$"{CampaignDisplayText.SpecimenName(record)}\n{CampaignDisplayText.SpecimenSpecies(record)}  ·  {plaqueSize}\n{CampaignDisplayText.SpecimenDate(record)}");
            }
            Highlight();
        }
        GameObject MountModel(SpecimenRecord record,Transform mount,out Vector3 modelSize)
        {
            var pivot=new GameObject("Pinned "+record.labelName);pivot.transform.SetParent(mount,false);pivot.transform.localPosition=new Vector3(0,0,-.30f);
            bool adult=record.beetle.stage==CampaignLifeStage.ActiveAdult||record.beetle.stage==CampaignLifeStage.DormantAdult;
            var bug=adult?Instantiate(beetlePrefab,pivot.transform):CampaignLifeStageDisplay.CreatePreservedBody(record.beetle.stage,pivot.transform,female:record.beetle.female);bug.name=record.labelName;
            foreach(var behaviour in bug.GetComponentsInChildren<MonoBehaviour>())behaviour.enabled=false;
            foreach(var collider in bug.GetComponentsInChildren<Collider>())collider.enabled=false;
            var actor=bug.GetComponent<BeetleActor>();if(actor&&actor.teamMarker)actor.teamMarker.gameObject.SetActive(false);
            var rig=bug.GetComponentInChildren<BeetleProceduralAnimator>();
            if(rig&&rig.importedRig)rig.importedRig.ResetRig();
            bug.transform.localPosition=Vector3.zero;bug.transform.localRotation=Quaternion.Euler(-90,0,0);bug.transform.localScale=Vector3.one;
            var renderers=bug.GetComponentsInChildren<Renderer>().Where(r=>r.gameObject.activeInHierarchy).ToArray();
            var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
            float scale=Mathf.Min(1.85f/bounds.size.x,2.03f/bounds.size.y);
            modelSize=bounds.size*scale;
            Vector3 center=pivot.transform.InverseTransformPoint(bounds.center);
            bug.transform.localScale*=scale;bug.transform.localPosition=-center*scale;
            var color=Color.HSVToRGB((float)app.Session.Catalog.Species(record.beetle.speciesId).hue,.65f,.65f);
            if(record.beetle.mutation=="纯金壳")color=new Color(.85f,.65f,.15f);
            if(record.beetle.mutation=="紫绀色")color=new Color(.45f,.15f,.65f);
            CampaignSpecimen.Tint(rig,color);
            return pivot;
        }
        public void Select(string id)
        {
            selectedSlot=app.Session.State.specimens.FirstOrDefault(s=>s.id==id)?.wallSlot??-1;Highlight();
        }
        public void SetArrange(bool value) { Arranging=value;dragSlot=-1;hoverSlot=-1;Highlight(); }
        public void Focus(string id)
        {
            Select(id);if(selectedSlot<0)return;
            targetFocus=SlotPosition(selectedSlot);targetDistance=4.6f;yaw=pitch=0;
        }
        public void ResetView()
        {
            aspect=cameraView.aspect;
            float vertical=cameraView.fieldOfView*.5f*Mathf.Deg2Rad;
            overviewDistance=Mathf.Max(5.5f/Mathf.Tan(vertical),6.3f/(Mathf.Tan(vertical)*aspect));
            targetFocus=new Vector3(0,.35f,0);targetDistance=overviewDistance;yaw=pitch=0;
            FitRoomToCamera();
            if(distance<=0){distance=targetDistance;focus=targetFocus;ApplyCamera();}
        }
        void LateUpdate()
        {
            if(!cameraView)return;
            if(shownState!=app.Session?.State)Refresh();
            if(!Mathf.Approximately(aspect,cameraView.aspect)){ResetView();}
            ReadMouse();float blend=1-Mathf.Exp(-12*Mathf.Min(Time.unscaledDeltaTime,.1f));
            focus=Vector3.Lerp(focus,targetFocus,blend);distance=Mathf.Lerp(distance,targetDistance,blend);ApplyCamera();
        }
        void ApplyCamera()
        {
            var rotation=Quaternion.Euler(pitch,yaw,0);
            var position=focus+rotation*new Vector3(0,0,-distance);position.y=Mathf.Max(-4.85f,position.y);
            cameraView.transform.SetPositionAndRotation(position,Quaternion.LookRotation(focus-position,Vector3.up));
        }
        int PickSlot(Vector2 screen)
        {
            var viewport=backdrop.Viewport;var local=new Vector3((screen.x/Screen.width-viewport.x)/viewport.width,screen.y/Screen.height,0);
            var ray=cameraView.ViewportPointToRay(local);var plane=new Plane(Vector3.back,Vector3.zero);
            if(!plane.Raycast(ray,out float t))return -1;var p=ray.GetPoint(t);
            int column=Mathf.RoundToInt(p.x/2.85f+1.5f),row=Mathf.RoundToInt(1-(p.y-.45f)/3.3f);
            if(column<0||column>=Columns||row<0||row>=Rows)return -1;
            int slot=row*Columns+column;var delta=p-SlotPosition(slot);
            return Mathf.Abs(delta.x)<1.3f&&Mathf.Abs(delta.y)<1.7f?slot:-1;
        }
        void ReadMouse()
        {
            var mouse=Mouse.current;if(mouse==null||!app||app.Busy||app.Modal){dragSlot=-1;return;}
            Vector2 position=mouse.position.ReadValue();bool inside=position.x>=Screen.width*backdrop.Viewport.x&&position.x<=Screen.width&&position.y>=0&&position.y<=Screen.height;
            if(mouse.leftButton.wasReleasedThisFrame)
            {
                int target=inside?PickSlot(position):-1;
                if(Arranging&&dragged&&dragSlot>=0&&ids[dragSlot]!=null&&target>=0&&target!=dragSlot)MoveRequested?.Invoke(ids[dragSlot],target);
                else if(!dragged&&dragSlot>=0&&target==dragSlot)SlotClicked?.Invoke(target);
                dragSlot=-1;dragged=false;hoverSlot=-1;Highlight();
            }
            if(!inside)return;
            if(mouse.leftButton.wasPressedThisFrame){dragSlot=PickSlot(position);pressPoint=position;dragged=false;}
            if(mouse.leftButton.isPressed&&dragSlot>=0&&(position-pressPoint).sqrMagnitude>36)dragged=true;
            int hover=Arranging&&dragged?PickSlot(position):-1;if(hoverSlot!=hover){hoverSlot=hover;Highlight();}
            Vector2 delta=mouse.delta.ReadValue();
            if(mouse.rightButton.isPressed){yaw=Mathf.Clamp(yaw+delta.x*.18f,-MaxYaw,MaxYaw);pitch=Mathf.Clamp(pitch-delta.y*.15f,-MaxPitch,MaxPitch);}
            if(mouse.middleButton.isPressed)
            {
                targetFocus-=cameraView.transform.right*delta.x*distance*.0011f+cameraView.transform.up*delta.y*distance*.0011f;
                targetFocus.x=Mathf.Clamp(targetFocus.x,-MaxPanX,MaxPanX);targetFocus.y=Mathf.Clamp(targetFocus.y,MinPanY,MaxPanY);
            }
            targetDistance=Mathf.Clamp(targetDistance*Mathf.Exp(-mouse.scroll.ReadValue().y*.0013f*zoomSpeedMultiplier),3.5f,overviewDistance*MaxZoom);
        }
        void Highlight()
        {
            for(int i=0;i<12;i++)foreach(var r in borders[i])
            {
                tint.Clear();if(i==selectedSlot||i==hoverSlot)tint.SetColor("_BaseColor",i==hoverSlot?new Color(.34f,.77f,.68f):new Color(.93f,.68f,.24f));r.SetPropertyBlock(tint);
            }
        }
        void OnDestroy() { if(app)app.Changed-=Refresh;foreach(var material in materials)if(material)Destroy(material); }
    }
}
