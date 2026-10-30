using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BeetleArena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>Authored room dressing lives outside the terrarium's navigation and camera bounds.</summary>
public static class HusbandryRoomSetup
{
    public const string Folder="Assets/BeetleArena/Generated/Husbandry/Room";
    public const string TextureFolder="Assets/BeetleArena/Art/Husbandry/Textures";
    public const string SoftShader="BeetleArena/HusbandrySoftToon";
    const float DeskWallOverlap=.025f;
    [MenuItem("Tools/甲壳虫斗/画面与资源/养殖表现/布置室内饲育桌",false,322)]
    public static void Apply()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play before installing the room.");
        for(int i=0;i<SceneManager.sceneCount;i++)
            if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save open scene edits before installing the room.");
        HusbandryWindowTreeSetup.ValidateConfiguration();
        if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder(HusbandryTerrariumSetup.Folder,"Room");
        var setup=EditorSceneManager.GetSceneManagerSetup();
        try
        {
            foreach(string path in new[]{"Assets/Scenes/Campaign/CampaignGreenhouse.unity",BeetleNurseryNavigation.NurseryScenePath})
            {
                var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
                Configure(scene);EditorSceneManager.MarkSceneDirty(scene);
                if(!EditorSceneManager.SaveScene(scene))throw new IOException("Could not save room: "+path);
            }
            AssetDatabase.SaveAssets();
        }
        finally{EditorSceneManager.RestoreSceneManagerSetup(setup);}
    }

    /// <summary>Removes the legacy plank dividers without rebuilding or changing the room's authored props.</summary>
    public static int ApplyContinuousDesktop()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play before updating the desktop.");
        for(int i=0;i<SceneManager.sceneCount;i++)
            if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save open scene edits before updating the desktop.");
        var setup=EditorSceneManager.GetSceneManagerSetup();int removed=0;
        try
        {
            foreach(string path in new[]{"Assets/Scenes/Campaign/CampaignGreenhouse.unity",BeetleNurseryNavigation.NurseryScenePath})
            {
                var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
                var room=scene.GetRootGameObjects().SingleOrDefault(root=>root.name=="HusbandryRoom");
                if(!room)throw new InvalidOperationException("Missing husbandry room: "+path);
                int sceneRemoved=0;
                // These are decorative strips on top of one continuous Backdrop mesh, not structural boards.
                for(int i=1;i<10;i++)
                {
                    var seam=room.transform.Find("Desktop seam "+i);
                    if(!seam)continue;
                    UnityEngine.Object.DestroyImmediate(seam.gameObject);sceneRemoved++;
                }
                if(sceneRemoved==0)continue;
                EditorSceneManager.MarkSceneDirty(scene);
                if(!EditorSceneManager.SaveScene(scene))throw new IOException("Could not save continuous desktop: "+path);
                removed+=sceneRemoved;
            }
        }
        finally{EditorSceneManager.RestoreSceneManagerSetup(setup);}
        return removed;
    }

    [MenuItem("Tools/甲壳虫斗/画面与资源/养殖表现/长桌贴合左墙",false,326)]
    public static void ApplyDesktopWallFit()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play before aligning the desk.");
        for(int i=0;i<SceneManager.sceneCount;i++)
            if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save open scene edits before aligning the desk.");
        var setup=EditorSceneManager.GetSceneManagerSetup();
        try
        {
            foreach(string path in new[]{"Assets/Scenes/Campaign/CampaignGreenhouse.unity",BeetleNurseryNavigation.NurseryScenePath})
            {
                var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
                var roots=scene.GetRootGameObjects();
                var room=roots.Single(root=>root.name=="HusbandryRoom").transform;
                var tabletop=roots.SelectMany(root=>root.GetComponentsInChildren<Renderer>(true)).Single(r=>r.name=="Backdrop");
                AlignDeskWithLeftWall(room,tabletop);
                EditorSceneManager.MarkSceneDirty(scene);
                if(!EditorSceneManager.SaveScene(scene))throw new IOException("Could not save wall-fitted desk: "+path);
            }
        }
        finally{EditorSceneManager.RestoreSceneManagerSetup(setup);}
    }

    static void AlignDeskWithLeftWall(Transform room,Renderer tabletop)
    {
        var wall=room.GetComponentsInChildren<Renderer>(true).Single(r=>r.name=="Side plaster");
        var roots=room.gameObject.scene.GetRootGameObjects();
        var soil=roots.SelectMany(root=>root.GetComponentsInChildren<Renderer>(true)).Single(r=>r.name=="SoilBed");
        float centerX=wall.bounds.max.x-DeskWallOverlap+tabletop.bounds.extents.x;
        var displacement=Vector3.right*(centerX-soil.bounds.center.x);
        // Enclosure coordinates are local to SoilBed. Move the authored specimen, camera and lights
        // with that root so the same local navigation and initial composition remain valid.
        var moving=new HashSet<Transform>{soil.transform.root};
        foreach(var root in roots)
            if(root.GetComponentInChildren<Camera>(true)||root.GetComponentInChildren<Light>(true)||
               root.GetComponentInChildren<BeetleArena.Campaign.CampaignSpecimen>(true)||root.GetComponentInChildren<BeetleHusbandryView>(true))
                moving.Add(root.transform);
        foreach(var root in moving)
            if(root!=room)Move(root,displacement);

        var wallPosition=wall.transform.position;
        var floor=room.Find("Room floor");var floorPosition=floor.position;
        var roomDisplacement=Vector3.right*(centerX-room.position.x);
        Move(room,roomDisplacement);
        // The fixed room corner is the anchor; furniture, window, exterior and props move together.
        Move(wall.transform,wallPosition-wall.transform.position);
        Move(floor,floorPosition-floor.position);
        var daylight=room.GetComponent<HusbandryDaylight>();
        daylight.windowCenter+=roomDisplacement;EditorUtility.SetDirty(daylight);
        // Full room rebuilds recreate the slab at x=0 while retaining the translated enclosure.
        // Set its final center explicitly to make both rebuild and repeated alignment idempotent.
        Move(tabletop.transform,Vector3.right*(centerX-tabletop.bounds.center.x));
        ExtendRoomWalls(room,tabletop.bounds.max.y);
        foreach(var spotlight in roots.SelectMany(root=>root.GetComponentsInChildren<HusbandrySpotlight>(true)))spotlight.Apply();
    }

    static void Move(Transform part,Vector3 displacement)
    {
        if(displacement.sqrMagnitude<.00000001f)return;
        part.position+=displacement;EditorUtility.SetDirty(part);
        if(PrefabUtility.IsPartOfPrefabInstance(part))PrefabUtility.RecordPrefabInstancePropertyModifications(part);
    }

    static void ExtendRoomWalls(Transform room,float top)
    {
        var window=room.Find("Window wall");
        // Keep the existing opening, but cover the full permitted orbit and wide viewports.
        // Large simple wall panels avoid exposing a finite set edge behind the desk.
        void PanelSize(string name,Vector3 position,Vector3 size)
        {
            var part=window.Find(name);part.localPosition=position;part.localScale=size;EditorUtility.SetDirty(part);
        }
        PanelSize("Back plaster left",new Vector3(-86,top+4,12),new Vector3(160,120,1));
        PanelSize("Back plaster right",new Vector3(86,top+4,12),new Vector3(160,120,1));
        PanelSize("Back plaster lintel",new Vector3(0,top+40.5f,12),new Vector3(12,60,1));
        PanelSize("Back plaster below sill",new Vector3(0,top-28.5f,12),new Vector3(12,60,1));
        PanelSize("Lower wainscot",new Vector3(0,top-3.8f,11.98f),new Vector3(332,5.2f,1));
        PanelSize("Picture rail",new Vector3(0,top-1.15f,11.93f),new Vector3(332,.14f,.1f));
        var side=window.Find("Side plaster");
        var sidePosition=side.position;sidePosition.y=top+4;sidePosition.z=-58;
        side.position=sidePosition;side.localScale=new Vector3(130,120,1);EditorUtility.SetDirty(side);
        // Ultrawide corner rays can run almost parallel to the back wall. Close the room at
        // the right instead of pushing the backdrop and far clip to an artificial infinity.
        var right=window.Find("Right plaster");
        if(!right)right=Panel(window,"Right plaster",Vector3.zero,Vector2.one,side.GetComponent<Renderer>().sharedMaterial).transform;
        PanelSize("Right plaster",new Vector3(44,top+4,-53),new Vector3(130,120,1));
        right.localRotation=Quaternion.Euler(0,90,0);
        var rightPaint=window.Find("Right wainscot");
        if(!rightPaint)rightPaint=Panel(window,"Right wainscot",Vector3.zero,Vector2.one,window.Find("Lower wainscot").GetComponent<Renderer>().sharedMaterial).transform;
        PanelSize("Right wainscot",new Vector3(43.98f,top-3.8f,-53),new Vector3(130,5.2f,1));
        rightPaint.localRotation=Quaternion.Euler(0,90,0);
        if(!window.Find("Right picture rail"))Box(window,"Right picture rail",Vector3.zero,Vector3.one,window.Find("Picture rail").GetComponent<Renderer>().sharedMaterial);
        PanelSize("Right picture rail",new Vector3(43.93f,top-1.15f,-53),new Vector3(.1f,.14f,130));
    }

    static void Configure(Scene scene)
    {
        var roots=scene.GetRootGameObjects();
        var renderers=roots.SelectMany(root=>root.GetComponentsInChildren<Renderer>(true)).ToArray();
        var soil=renderers.Single(r=>r.name=="SoilBed");var tabletop=renderers.Single(r=>r.name=="Backdrop");
        var existing=roots.FirstOrDefault(root=>root.name=="HusbandryRoom");
        // Rebuild only this tool's generated dressing; the enclosure, beetle and authored UI remain intact.
        if(existing)UnityEngine.Object.DestroyImmediate(existing);
        var room=new GameObject("HusbandryRoom").transform;SceneManager.MoveGameObjectToScene(room.gameObject,scene);
        var camera=roots.Where(root=>root).SelectMany(root=>root.GetComponentsInChildren<Camera>(true)).Single(c=>c.CompareTag("MainCamera"));
        var cameraData=camera.GetUniversalAdditionalCameraData();
        cameraData.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        cameraData.antialiasingQuality=AntialiasingQuality.High;cameraData.renderPostProcessing=true;
        var wood=Surface("Honey oak",new Color(.9f,.85f,.76f),.16f,true);
        var edge=Surface("Oak edge",new Color(.59f,.48f,.36f),.13f,true);
        var wall=Surface("Warm plaster",new Color(.67f,.71f,.63f));
        var green=Surface("Sage paint",new Color(.29f,.4f,.33f));
        var metal=Surface("Graphite steel",new Color(.065f,.085f,.08f),.3f);
        var ivory=Surface("Ivory ceramic",new Color(.83f,.79f,.66f),.26f);
        var terra=Surface("Terracotta",new Color(.57f,.33f,.22f));
        var foliage=Surface("Plant leaves",new Color(.26f,.39f,.19f),.12f);
        var paper=Surface("Notebook paper",new Color(.77f,.73f,.58f));
        float top=soil.bounds.min.y-HusbandryTerrariumSetup.BackdropClearance;
        tabletop.transform.SetPositionAndRotation(new Vector3(0,top-.22f,.3f),Quaternion.identity);
        tabletop.transform.localScale=new Vector3(28,.44f,13);tabletop.sharedMaterial=wood;
        // Keep the single continuous slab and its Backdrop reference; the wood grain supplies the surface detail.
        Box(room,"Front apron",new Vector3(0,top-.7f,-5.95f),new Vector3(27.2f,.9f,.3f),edge);
        Box(room,"Back apron",new Vector3(0,top-.7f,6.55f),new Vector3(27.2f,.9f,.3f),edge);
        foreach(float x in new[]{-12.8f,12.8f})foreach(float z in new[]{-5.4f,5.6f})
            Box(room,"Desk leg",new Vector3(x,top-3.3f,z),new Vector3(.55f,6.2f,.55f),edge);
        Box(room,"Long desk stretcher",new Vector3(0,top-4.8f,5.6f),new Vector3(26,.3f,.3f),edge);
        Box(room,"Room floor",new Vector3(0,top-6.55f,0),new Vector3(90,.2f,90),Surface("Floorboards",new Color(.31f,.28f,.22f),.12f,true));
        // Bring the wall to the desk, preserving every existing enclosure and navigation coordinate.
        var window=new GameObject("Window wall").transform;window.SetParent(room,false);window.localPosition=new Vector3(0,0,-5);
        // Single-sided wall dressing faces the room's viewing area.
        Panel(window,"Back plaster left",new Vector3(-14,top+4,12),new Vector2(16,21),wall);
        Panel(window,"Back plaster right",new Vector3(14,top+4,12),new Vector2(16,21),wall);
        Panel(window,"Back plaster lintel",new Vector3(0,top+13,12),new Vector2(12,5),wall);
        Panel(window,"Back plaster below sill",new Vector3(0,top-2.5f,12),new Vector2(12,8),wall);
        Panel(window,"Lower wainscot",new Vector3(0,top-3.8f,11.98f),new Vector2(44,5.2f),green);
        Box(window,"Picture rail",new Vector3(0,top-1.15f,11.93f),new Vector3(44,.14f,.1f),ivory);
        var side=Panel(window,"Side plaster",new Vector3(-18,top+4,-3),new Vector2(30,21),wall);
        side.transform.rotation=Quaternion.Euler(0,-90,0);
        // The wall has an actual opening. The landscape lies beyond the sill, with parallax while orbiting.
        var sky=Surface("Window daylight",Color.white);sky.shader=Shader.Find("BeetleArena/HusbandryWindowSky");EditorUtility.SetDirty(sky);
        string vistaPath=TextureFolder+"/GardenVista-SoftToon.png";
        var importer=AssetImporter.GetAtPath(vistaPath) as TextureImporter;
        if(importer){importer.alphaSource=TextureImporterAlphaSource.FromInput;importer.alphaIsTransparency=true;importer.maxTextureSize=2048;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();}
        sky.SetTexture("_GardenTex",AssetDatabase.LoadAssetAtPath<Texture2D>(vistaPath));
        sky.SetFloat("_GardenSkyMatte",1);
        var skyView=Panel(window,"Window sky",new Vector3(0,top+3.7f,26),new Vector2(22,15),sky).GetComponent<Renderer>();
        foreach(float x in new[]{-6f,0,6f})Box(window,"Window vertical",new Vector3(x,top+6,11.65f),new Vector3(.17f,9.2f,.35f),ivory);
        foreach(float y in new[]{1.5f,6f,10.5f})Box(window,"Window horizontal",new Vector3(0,top+y,11.65f),new Vector3(12.2f,.17f,.35f),ivory);
        Box(window,"Window sill",new Vector3(0,top+1.38f,11.35f),new Vector3(13,.24f,1.3f),ivory);
        foreach(float x in new[]{-.28f,.28f})Box(window,"Window latch",new Vector3(x,top+5.8f,11.38f),new Vector3(.07f,.4f,.09f),metal);
        RoomDetails(room,window,top,wood,edge,metal,ivory,green,paper);

        // Props stay outside the glass and never create navigation obstacles or pointer colliders.
        var book=Box(room,"Field notebook cover",new Vector3(6.65f,top+.06f,-3.5f),new Vector3(2.3f,.12f,3.1f),green);
        book.transform.rotation=Quaternion.Euler(0,-13,0);
        var pages=Box(room,"Field notebook pages",new Vector3(6.65f,top+.15f,-3.5f),new Vector3(2.14f,.08f,2.93f),paper);pages.transform.rotation=book.transform.rotation;
        for(int i=0;i<7;i++)
        {
            var line=Box(room,"Notebook rule",pages.transform.TransformPoint(new Vector3(0,.55f,-.32f+i*.09f)),new Vector3(1.5f,.003f,.012f),green);
            line.transform.rotation=book.transform.rotation;
        }
        var pencil=Box(room,"Graphite pencil",new Vector3(7.95f,top+.07f,-3.2f),new Vector3(.07f,.07f,2.5f),terra);pencil.transform.rotation=Quaternion.Euler(0,12,0);
        Cylinder(room,"Misting bottle",new Vector3(-6.5f,top+.65f,-1.8f),new Vector3(.85f,.65f,.85f),green);
        Cylinder(room,"Bottle neck",new Vector3(-6.5f,top+1.38f,-1.8f),new Vector3(.32f,.16f,.32f),metal);
        Box(room,"Sprayer",new Vector3(-6.3f,top+1.6f,-1.8f),new Vector3(.85f,.2f,.3f),metal);
        Box(room,"Bottle label",new Vector3(-6.5f,top+.65f,-2.235f),new Vector3(.5f,.55f,.015f),paper);
        for(int i=0;i<2;i++)
        {
            var tweezers=Box(room,"Feeding tweezers",new Vector3(6.4f+i*.18f,top+.055f,.15f),new Vector3(.065f,.06f,2.6f),metal);
            tweezers.transform.rotation=Quaternion.Euler(0,-20+i*4,0);
        }
        Cylinder(room,"Plant pot",new Vector3(-6.9f,top+.55f,4.6f),new Vector3(1.6f,.55f,1.6f),terra);
        Cylinder(room,"Pot soil",new Vector3(-6.9f,top+1.1f,4.6f),new Vector3(1.4f,.04f,1.4f),edge);
        for(int i=0;i<9;i++)
        {
            float angle=i*137.5f*Mathf.Deg2Rad;
            var leaf=Primitive(room,"Plant leaf",PrimitiveType.Sphere,new Vector3(-6.9f+Mathf.Sin(angle)*.65f,top+1.5f+(i%3)*.3f,4.6f+Mathf.Cos(angle)*.65f),new Vector3(.55f,.09f,1.65f),foliage);
            leaf.transform.rotation=Quaternion.Euler(-25-(i%3)*12,i*137.5f,15);
        }
        Box(room,"Care tray",new Vector3(6.7f,top+.05f,4.7f),new Vector3(2.7f,.1f,1.8f),ivory);
        for(int i=0;i<3;i++)Cylinder(room,"Jelly cup",new Vector3(5.9f+i*.8f,top+.22f,4.7f),new Vector3(.6f,.17f,.6f),terra);

        var lights=roots.Where(root=>root).SelectMany(root=>root.GetComponentsInChildren<Light>(true)).ToArray();
        var main=lights.Single(l=>l.name=="WarmCanopy");var fill=lights.Single(l=>l.name=="SoftFill");
        main.type=LightType.Directional;main.intensity=1.45f;main.color=new Color(1,.91f,.76f);
        main.transform.rotation=Quaternion.LookRotation(new Vector3(.5f,-1,-.6f));main.shadows=LightShadows.Soft;
        main.shadowStrength=.82f;main.shadowBias=.035f;main.shadowNormalBias=.24f;main.shadowResolution=LightShadowResolution.High;
        fill.intensity=.55f;fill.color=new Color(.64f,.78f,1);fill.shadows=LightShadows.None;
        fill.transform.rotation=Quaternion.LookRotation(new Vector3(-.2f,-.4f,1));
        var atmosphere=room.gameObject.AddComponent<HusbandryAtmosphere>();atmosphere.canopy=main;atmosphere.soil=soil;
        atmosphere.moteShader=Shader.Find("BeetleArena/HusbandryDust");
        RenderSettings.sun=main;RenderSettings.ambientMode=AmbientMode.Trilight;
        RenderSettings.ambientSkyColor=new Color(.48f,.54f,.6f);RenderSettings.ambientEquatorColor=new Color(.34f,.37f,.32f);
        RenderSettings.ambientGroundColor=new Color(.19f,.14f,.095f);RenderSettings.fog=false;
        var spotlight=roots.Where(root=>root).SelectMany(root=>root.GetComponentsInChildren<HusbandrySpotlight>(true)).Single();
        spotlight.poolDiameterLimit=13.8f;spotlight.Apply();
        var reflection=new GameObject("Room reflection").AddComponent<ReflectionProbe>();reflection.transform.SetParent(room,false);reflection.transform.position=new Vector3(0,1,0);
        reflection.mode=ReflectionProbeMode.Realtime;reflection.refreshMode=ReflectionProbeRefreshMode.ViaScripting;
        reflection.timeSlicingMode=ReflectionProbeTimeSlicingMode.IndividualFaces;reflection.resolution=128;reflection.boxProjection=true;
        reflection.size=new Vector3(32,22,36);reflection.intensity=1;reflection.shadowDistance=0;reflection.farClipPlane=85;
        var daylight=room.gameObject.AddComponent<HusbandryDaylight>();daylight.sunlight=main;daylight.fill=fill;
        daylight.sky=skyView;daylight.windowCenter=window.TransformPoint(new Vector3(0,top+6,11.8f));daylight.roomReflection=reflection;
        daylight.panes=soil.transform.parent.GetComponentsInChildren<HusbandryGlassSurface>(true)
            .SelectMany(surface=>surface.GetComponentsInChildren<MeshRenderer>(true)).Where(r=>r.sharedMaterial&&
                (r.sharedMaterial.shader.name==HusbandryTerrariumSetup.GlassShaderName||r.sharedMaterial.shader.name==HusbandryTerrariumSetup.GlassLowShaderName)).Cast<Renderer>().ToArray();
        HusbandryWindowTreeSetup.Configure(room);
        FinishRoom(soil);
        AlignDeskWithLeftWall(room,tabletop);
    }

    static void FinishRoom(Renderer soil)
    {
        foreach(string name in new[]{"Glass","GlassLow"})
        {
            var glass=AssetDatabase.LoadAssetAtPath<Material>(HusbandryTerrariumSetup.MaterialFolder+"/"+name+".mat");
            glass.SetColor("_TransmissionColor",new Color(.91f,.985f,.96f));glass.SetFloat("_AbsorptionDensity",.35f);
            glass.SetFloat("_ReflectionStrength",1);glass.SetFloat("_Smoothness",.96f);glass.SetFloat("_HighlightStrength",.35f);
            glass.SetFloat("_WindowStrength",0);glass.SetFloat("_WindowSoftness",.035f);
            if(glass.HasProperty("_RefractionStrength")){glass.SetFloat("_RefractionStrength",.16f);glass.SetFloat("_MaxRefractionPixels",.6f);}
            EditorUtility.SetDirty(glass);
        }
        var frame=AssetDatabase.LoadAssetAtPath<Material>(HusbandryTerrariumSetup.MaterialFolder+"/Frame.mat");
        frame.shader=Shader.Find("Universal Render Pipeline/Lit");frame.SetColor("_BaseColor",new Color(.19f,.23f,.21f));
        frame.SetFloat("_Metallic",.28f);frame.SetFloat("_Smoothness",.55f);EditorUtility.SetDirty(frame);
        var floor=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Floorboards.mat");floor.SetTextureScale("_BaseMap",new Vector2(14,14));EditorUtility.SetDirty(floor);
        var top=Surface("Painted substrate top",new Color(.68f,.66f,.62f));
        var sides=Surface("Painted substrate sides",new Color(.48f,.45f,.41f));
        var substrate=PaintedTexture("Substrate-SoftToon.png");
        foreach(var material in new[]{top,sides})
        {
            material.SetTexture("_BaseMap",substrate);material.SetFloat("_SpecStrength",0);
            material.SetFloat("_Stylization",.25f);EditorUtility.SetDirty(material);
        }
        top.SetTextureScale("_BaseMap",new Vector2(.7f,.7f));sides.SetTextureScale("_BaseMap",new Vector2(.7f,.35f));
        soil.sharedMaterials=new[]{top,sides};EditorUtility.SetDirty(soil);PrefabUtility.RecordPrefabInstancePropertyModifications(soil);
    }

    static void RoomDetails(Transform room,Transform window,float top,Material wood,Material edge,Material metal,Material ivory,Material green,Material paper)
    {
        var cloth=Surface("Linen curtains",new Color(.61f,.59f,.47f));cloth.SetFloat("_Cull",0);EditorUtility.SetDirty(cloth);
        var rod=Cylinder(window,"Curtain rod",new Vector3(0,top+11,11.1f),new Vector3(.09f,8.2f,.09f),metal);rod.transform.rotation=Quaternion.Euler(0,0,90);
        foreach(int side in new[]{-1,1})
        {
            var go=new GameObject("Pleated linen curtain",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(window,false);
            go.transform.localPosition=new Vector3(side*7.15f,top+10.8f,11.05f);go.transform.localScale=new Vector3(side,1,1);
            go.GetComponent<MeshFilter>().sharedMesh=CurtainMesh();go.GetComponent<MeshRenderer>().sharedMaterial=cloth;
            go.GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.TwoSided;
            for(int i=0;i<6;i++)Cylinder(window,"Curtain hanger",new Vector3(side*(6.3f+i*.33f),top+10.85f,11.05f),new Vector3(.07f,.15f,.07f),ivory);
            Box(window,"Curtain tie",new Vector3(side*7.55f,top+5.7f,10.87f),new Vector3(1.1f,.17f,.12f),green);
            Box(room,"Desk drawer",new Vector3(side*8.8f,top-.75f,-6.15f),new Vector3(7.1f,.72f,.16f),wood);
            Box(room,"Drawer handle",new Vector3(side*8.8f,top-.74f,-6.32f),new Vector3(.8f,.1f,.13f),metal);
            for(int i=0;i<2;i++)
                Primitive(room,"Drawer screw",PrimitiveType.Sphere,new Vector3(side*8.8f-.3f+i*.6f,top-.74f,-6.4f),Vector3.one*.045f,ivory);
        }
        Box(window,"Field guide shelf",new Vector3(10,top+5.5f,11.2f),new Vector3(5,.18f,1.2f),edge);
        for(int i=0;i<6;i++)
        {
            float height=1.4f+(i%3)*.24f;
            Box(window,"Field guide spine",new Vector3(8.25f+i*.5f,top+5.6f+height*.5f,11.2f),new Vector3(.38f,height,.8f),i%2==0?green:wood);
            Box(window,"Spine title",new Vector3(8.25f+i*.5f,top+6.2f,10.79f),new Vector3(.23f,.25f,.015f),paper);
        }
        // A framed pressed-leaf study beside the window adds detail without small unreadable UI text.
        Box(window,"Botanical study frame",new Vector3(-10.2f,top+5.2f,11.7f),new Vector3(3.1f,4,.12f),edge);
        Box(window,"Botanical study paper",new Vector3(-10.2f,top+5.2f,11.62f),new Vector3(2.8f,3.7f,.025f),paper);
        Box(window,"Study stem",new Vector3(-10.2f,top+5.2f,11.59f),new Vector3(.025f,2.8f,.012f),green);
        for(int i=0;i<7;i++)
        {
            float side=i%2==0?-1:1;
            var leaf=Primitive(window,"Pressed leaf",PrimitiveType.Sphere,new Vector3(-10.2f+side*.3f,top+4+i*.34f,11.57f),new Vector3(.63f,.22f,.018f),green);
            leaf.transform.rotation=Quaternion.Euler(0,0,side*27);
        }
    }
    static Mesh CurtainMesh()
    {
        string path=Folder+"/PleatedCurtain.asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(mesh)return mesh;
        const int columns=40,rows=20;var vertices=new Vector3[(columns+1)*(rows+1)];var uv=new Vector2[vertices.Length];var triangles=new int[columns*rows*6];int index=0;
        for(int y=0;y<=rows;y++)for(int x=0;x<=columns;x++)
        {
            float u=(float)x/columns,v=(float)y/rows;
            float tie=Mathf.Exp(-Mathf.Pow((v-.52f)*5,2));
            vertices[y*(columns+1)+x]=new Vector3((u-.5f)*Mathf.Lerp(2.2f,1.1f,tie)+tie*.45f,-v*9.7f,Mathf.Sin(u*Mathf.PI*12)*.13f*(1-tie*.6f));
            uv[y*(columns+1)+x]=new Vector2(u,v);
            if(x<columns&&y<rows)
            {
                int a=y*(columns+1)+x,b=a+1,c=a+columns+1,d=c+1;
                triangles[index++]=a;triangles[index++]=c;triangles[index++]=b;triangles[index++]=b;triangles[index++]=c;triangles[index++]=d;
            }
        }
        mesh=new Mesh{name="Pleated linen curtain",vertices=vertices,uv=uv,triangles=triangles};mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,path);return mesh;
    }

    static Material Surface(string name,Color color,float smoothness=0,bool wood=false)
    {
        string path=Folder+"/"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        bool create=!material;
        if(create)material=new Material(Shader.Find(SoftShader)){name=name};
        else material.shader=Shader.Find(SoftShader);
        material.SetColor("_BaseColor",color);material.SetFloat("_Smoothness",smoothness);
        material.SetFloat("_Stylization",.35f);material.SetFloat("_BandSoftness",.18f);
        material.SetFloat("_AmbientStrength",1.1f);material.SetFloat("_SpecStrength",smoothness*.18f);
        if(wood)material.SetTexture("_BaseMap",PaintedTexture("Oak-SoftToon.png"));
        if(create)AssetDatabase.CreateAsset(material,path);else EditorUtility.SetDirty(material);
        return material;
    }
    static Texture2D PaintedTexture(string name)
    {
        string path=TextureFolder+"/"+name;
        var importer=AssetImporter.GetAtPath(path) as TextureImporter;
        if(!importer)throw new FileNotFoundException("Import the painted husbandry texture first.",path);
        if(!importer.mipmapEnabled||importer.wrapMode!=TextureWrapMode.Repeat||importer.anisoLevel!=4||importer.maxTextureSize!=2048||importer.textureCompression!=TextureImporterCompression.CompressedHQ)
        {
            importer.mipmapEnabled=true;importer.wrapMode=TextureWrapMode.Repeat;importer.anisoLevel=4;
            importer.maxTextureSize=2048;importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
    static GameObject Box(Transform parent,string name,Vector3 p,Vector3 s,Material m)=>Primitive(parent,name,PrimitiveType.Cube,p,s,m);
    static GameObject Cylinder(Transform parent,string name,Vector3 p,Vector3 s,Material m)=>Primitive(parent,name,PrimitiveType.Cylinder,p,s,m);
    static GameObject Panel(Transform parent,string name,Vector3 p,Vector2 size,Material m)=>Primitive(parent,name,PrimitiveType.Quad,p,new Vector3(size.x,size.y,1),m);
    static GameObject Primitive(Transform parent,string name,PrimitiveType type,Vector3 position,Vector3 scale,Material material)
    {
        var go=GameObject.CreatePrimitive(type);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localScale=scale;
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());go.GetComponent<Renderer>().sharedMaterial=material;return go;
    }
}
