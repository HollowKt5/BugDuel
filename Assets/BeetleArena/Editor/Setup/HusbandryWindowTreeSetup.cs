using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BeetleArena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>A real, partly cropped outdoor tree. Its generated assets never enter the enclosure's navigation hierarchy.</summary>
public static class HusbandryWindowTreeSetup
{
    public const string Folder=HusbandryRoomSetup.Folder+"/Tree";
    public const string ProfilePath=Folder+"/TreeModelProfile.asset";
    public const string ExteriorName="WindowTreeExterior";
    public const string FoliageShader="BeetleArena/HusbandrySeasonalFoliage";
    public const string WeatherShader="BeetleArena/HusbandryWeather";

    [MenuItem("Tools/甲壳虫斗/画面与资源/养殖表现/布置窗外四季大树",false,323)]
    public static void Apply()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play before installing the outdoor tree.");
        for(int i=0;i<SceneManager.sceneCount;i++)
            if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save open scene edits before installing the outdoor tree.");
        ValidateConfiguration();
        var setup=EditorSceneManager.GetSceneManagerSetup();
        try
        {
            foreach(string path in new[]{"Assets/Scenes/Campaign/CampaignGreenhouse.unity",BeetleNurseryNavigation.NurseryScenePath})
            {
                var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
                var room=scene.GetRootGameObjects().SingleOrDefault(go=>go.name=="HusbandryRoom");
                if(!room)throw new InvalidOperationException("Install the husbandry room before its outdoor tree: "+path);
                Configure(room.transform);EditorSceneManager.MarkSceneDirty(scene);
                if(!EditorSceneManager.SaveScene(scene))throw new IOException("Could not save outdoor tree: "+path);
            }
            AssetDatabase.SaveAssets();
        }
        finally{EditorSceneManager.RestoreSceneManagerSetup(setup);}
    }

    [MenuItem("Tools/甲壳虫斗/画面与资源/养殖表现/配置窗外树模型",false,324)]
    public static void SelectProfile()
    {
        var profile=EnsureProfile();Selection.activeObject=profile;EditorGUIUtility.PingObject(profile);
    }

    [MenuItem("Tools/甲壳虫斗/画面与资源/养殖表现/重建窗外树（应用配置）",false,325)]
    public static void ApplyTreeProfile()=>Apply();

    /// <summary>Call before a full room rebuild as well as targeted tree replacement.</summary>
    public static void ValidateConfiguration()
    {
        var profile=EnsureProfile();ValidateProfile(profile);ValidateResources(!profile.treePrefab);
    }

    public static HusbandryTreeProfile EnsureProfile()
    {
        EnsureFolder(Folder);var profile=AssetDatabase.LoadAssetAtPath<HusbandryTreeProfile>(ProfilePath);
        if(profile)return profile;
        if(AssetDatabase.LoadMainAssetAtPath(ProfilePath))throw new InvalidOperationException("Tree model profile path is occupied by a different asset: "+ProfilePath);
        profile=ScriptableObject.CreateInstance<HusbandryTreeProfile>();profile.name="Tree Model Profile";
        AssetDatabase.CreateAsset(profile,ProfilePath);return profile;
    }

    public static void ValidateProfile(HusbandryTreeProfile profile)
    {
        if(!profile)throw new ArgumentNullException(nameof(profile));
        if(!profile.treePrefab)return;
        if(!PrefabUtility.IsPartOfPrefabAsset(profile.treePrefab)||profile.treePrefab.transform.parent)
            throw new InvalidOperationException("窗外树配置必须引用Prefab资产的根节点，请先把模型保存为Prefab。");
        var adapter=profile.treePrefab.GetComponent<HusbandryTreeModelAdapter>();
        if(!adapter)
            throw new InvalidOperationException("窗外树Prefab根节点缺少HusbandryTreeModelAdapter。请添加组件并映射树叶、积雪和芽，然后再重建；当前场景尚未修改。");
        foreach(var renderer in (adapter.foliage??Array.Empty<Renderer>()).Concat(adapter.snowCaps??Array.Empty<Renderer>()).Concat(adapter.buds??Array.Empty<Renderer>()))
            if(renderer&&!renderer.transform.IsChildOf(profile.treePrefab.transform))
                throw new InvalidOperationException("窗外树的季节映射只能引用同一Prefab内的Renderer；当前场景尚未修改。");
        if(!Finite(profile.localPosition)||!Finite(profile.localEulerAngles)||!Finite(profile.localScale)||
            profile.localScale.x<=0||profile.localScale.y<=0||profile.localScale.z<=0)
            throw new InvalidOperationException("窗外树配置的位置、角度和缩放必须为有限数值，缩放分量必须大于0；当前场景尚未修改。");
    }

    static bool Finite(Vector3 value)=>float.IsFinite(value.x)&&float.IsFinite(value.y)&&float.IsFinite(value.z);

    public static void Configure(Transform room)
    {
        if(!room)throw new ArgumentNullException(nameof(room));
        var profile=EnsureProfile();ValidateProfile(profile);ValidateResources(!profile.treePrefab);
        var daylight=room.GetComponent<HusbandryDaylight>();
        if(!daylight||!daylight.sky)throw new InvalidOperationException("The room needs its daylight and window sky before the tree is installed.");
        if(!daylight.sky.sharedMaterial.HasProperty("_GardenEnabled"))
            throw new InvalidOperationException("The window sky shader needs its _GardenEnabled toggle before installing the tree.");
        var leafTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(HusbandryRoomSetup.TextureFolder+"/TreeLeafVeins-SoftToon.png");
        if(leafTexture)leafTexture=Texture("TreeLeafVeins-SoftToon.png",false);
        var weather=Material("Outdoor weather",WeatherShader,Color.white);
        var leafWeather=Material("Falling leaf weather",WeatherShader,Color.white);
        if(leafTexture)leafWeather.SetTexture("_BaseMap",leafTexture);
        EditorUtility.SetDirty(weather);EditorUtility.SetDirty(leafWeather);

        // This replacement is deliberately scoped to one generated child, not the authored room or enclosure.
        var previous=room.Find(ExteriorName);if(previous)UnityEngine.Object.DestroyImmediate(previous.gameObject);
        var exterior=new GameObject(ExteriorName).transform;exterior.SetParent(room,false);
        exterior.localPosition=room.InverseTransformPoint(daylight.windowCenter)-new Vector3(0,4.28f,6.8f);
        Renderer[] treeFoliage=Array.Empty<Renderer>(),treeSnow=Array.Empty<Renderer>(),treeBuds=Array.Empty<Renderer>();
        HusbandryTreeModelAdapter adapter=null;
        if(profile.treePrefab)
        {
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(profile.treePrefab,exterior);
            instance.transform.localPosition=profile.localPosition;
            instance.transform.localRotation=Quaternion.Euler(profile.localEulerAngles);instance.transform.localScale=profile.localScale;
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
            adapter=instance.GetComponent<HusbandryTreeModelAdapter>();
        }
        else BuildProceduralTree(exterior,leafTexture,out treeFoliage,out treeSnow,out treeBuds);

        var singleLeaf=new Geometry();AddLeaf(singleLeaf,new Vector3(0,-.5f,0),Quaternion.identity,new Vector2(.78f,1),.5f);
        var leafMesh=SaveMesh("Falling leaf",singleLeaf.Build("Falling leaf silhouette"));
        var flakeMesh=SaveMesh("Snowflake",Disk("Low polygon snowflake",8));
        var petalMesh=SaveMesh("Petal",Disk("Soft petal",6));
        var fallingLeaves=Weather(exterior,"Falling leaves",leafMesh,leafWeather,150,new Vector3(-3,10,13),new Vector3(12,5,8),
            new Vector2(.24f,.43f),new Vector2(.5f,.85f),new Color(.86f,.48f,.14f,.85f),4811);
        var snowflakes=Weather(exterior,"Winter snow",flakeMesh,weather,260,new Vector3(0,11,13),new Vector3(18,7,8),
            new Vector2(.035f,.085f),new Vector2(.45f,.8f),new Color(.91f,.96f,1,.85f),4812);
        var petals=Weather(exterior,"Spring petals",petalMesh,weather,50,new Vector3(-3,10,13),new Vector3(12,5,8),
            new Vector2(.08f,.16f),new Vector2(.35f,.6f),new Color(1,.77f,.7f,.82f),4813);
        var seasons=exterior.gameObject.AddComponent<HusbandrySeasons>();
        seasons.daylight=daylight;seasons.foliage=treeFoliage;seasons.snowCaps=treeSnow;seasons.buds=treeBuds;
        seasons.fallingLeaves=fallingLeaves;seasons.snowflakes=snowflakes;seasons.petals=petals;seasons.previewDay=20;
        if(adapter)adapter.Bind(seasons);

        ConfigureSky(daylight,room,exterior);
    }

    static void BuildProceduralTree(Transform exterior,Texture2D leafTexture,out Renderer[] treeFoliage,out Renderer[] treeSnow,out Renderer[] treeBuds)
    {
        var barkTexture=Texture("TreeBark-SoftToon.png",true);
        var bark=Material("Painted bark",HusbandryRoomSetup.SoftShader,new Color(.69f,.62f,.51f));
        bark.SetTexture("_BaseMap",barkTexture);bark.SetFloat("_Stylization",.12f);bark.SetFloat("_SpecStrength",.015f);
        var foliage=Material("Seasonal leaves",FoliageShader,Color.white);
        foliage.SetTexture("_BaseMap",leafTexture);foliage.SetColor("_SeasonTint",new Color(.6f,.76f,.35f));
        foliage.SetFloat("_LeafCoverage",1);foliage.SetFloat("_SnowAmount",0);foliage.SetFloat("_WindStrength",.1f);
        foliage.SetFloat("_WindTime",0);foliage.SetFloat("_Daylight",1);
        var snow=Material("Branch snow",HusbandryRoomSetup.SoftShader,new Color(.88f,.94f,.96f));
        var buds=Material("Spring buds",HusbandryRoomSetup.SoftShader,new Color(.93f,.72f,.61f));
        foreach(var material in new[]{bark,foliage,snow,buds})EditorUtility.SetDirty(material);
        var structure=new GameObject("Tree structure").transform;structure.SetParent(exterior,false);
        // The window crops one very large tree: trunk at its left edge, roots below, crown extending above the room.
        structure.localPosition=new Vector3(5.1f,-3,0);structure.localScale=new Vector3(1.6f,1.5f,1);
        var branches=Branches();var barkGeometry=new Geometry();
        for(int branchIndex=0;branchIndex<branches.Count;branchIndex++)
        {
            float girth=branchIndex==0?2.8f:branchIndex<6?Mathf.Lerp(2.25f,2f,(branchIndex-1)/4f):1.3f;
            var branch=branches[branchIndex];for(int i=0;i<branch.Radii.Length;i++)branch.Radii[i]*=girth;
        }
        foreach(var branch in branches)AddTube(barkGeometry,branch,branch.Radii[0]>.5f?12:9);
        MeshObject(structure,"Curved trunk and branches",SaveMesh("Trunk and branches",barkGeometry.Build("Curved tapered bark")),bark);

        var crowns=new[]{
            new Crown(new Vector3(-9.3f,6.9f,13.1f),new Vector3(2.5f,2.1f,1.8f),180),
            new Crown(new Vector3(-6.5f,7.6f,12.2f),new Vector3(2.6f,2.1f,1.7f),220),
            new Crown(new Vector3(-4.0f,8.7f,12.6f),new Vector3(2.35f,2.2f,1.75f),210),
            new Crown(new Vector3(-1.9f,9.9f,13.5f),new Vector3(1.9f,2.15f,1.65f),170),
            new Crown(new Vector3(-8.5f,11.1f,13.7f),new Vector3(2.8f,2.25f,1.9f),190),
            new Crown(new Vector3(-5.8f,12.5f,13.2f),new Vector3(2.6f,2.4f,1.9f),200),
            new Crown(new Vector3(-3.0f,13.1f,14.1f),new Vector3(2.3f,2.35f,1.8f),170),
            new Crown(new Vector3(-10.3f,10.1f,14.3f),new Vector3(2.1f,2.2f,1.7f),150)
        };
        var leaves=new List<Renderer>();var random=new System.Random(84271);
        for(int i=0;i<crowns.Length;i++)
        {
            var geometry=new Geometry();var crown=crowns[i];
            for(int leaf=0;leaf<Mathf.RoundToInt(crown.Count*2.3f);leaf++)
            {
                Vector3 sample=Ball(random);
                // Irregular overlapping sprays have open gaps and no opaque sphere under their leaves.
                Vector3 position=crown.Center+Vector3.Scale(sample,crown.Radius);
                float size=Range(random,.48f,.94f)*.6f*(1-Mathf.Abs(sample.y)*.12f);
                var rotation=Quaternion.Euler(Range(random,-55,55),Range(random,110,250),Range(random,-170,170));
                AddLeaf(geometry,position,rotation,new Vector2(size*.76f,size),Range(random,.001f,.999f));
            }
            leaves.Add(MeshObject(structure,"Foliage spray "+(i+1),SaveMesh("Foliage "+(i+1),geometry.Build("Fine almond leaves "+(i+1))),foliage));
        }

        var snowGeometry=new Geometry();var budGeometry=new Geometry();
        for(int i=1;i<branches.Count;i++)
        {
            AddSnowCap(snowGeometry,branches[i]);
            var branch=branches[i];Vector3 tip=branch.Points[branch.Points.Length-1];
            for(int b=0;b<3;b++)AddBud(budGeometry,tip+new Vector3((b-1)*.12f,.08f+b*.06f,-b*.04f),.09f+b*.012f);
        }
        var snowRenderer=MeshObject(structure,"Snow on branch shoulders",SaveMesh("Branch snow",snowGeometry.Build("Branch snow shoulders")),snow);
        var budRenderer=MeshObject(structure,"Sparse spring buds",SaveMesh("Spring buds",budGeometry.Build("Pointed spring buds")),buds);
        snowRenderer.enabled=false;budRenderer.enabled=false;
        treeFoliage=leaves.ToArray();treeSnow=new Renderer[]{snowRenderer};treeBuds=new Renderer[]{budRenderer};
    }

    static void ConfigureSky(HusbandryDaylight daylight,Transform room,Transform exterior)
    {
        // Keep the existing procedural sky, sun, moon and clock. The old painted garden is no longer composited.
        daylight.sky.sharedMaterial.SetFloat("_GardenEnabled",0);
        // The large quad covers oblique views; sky features retain a 26 x 18 world-unit composition centered at (0, 1).
        daylight.sky.sharedMaterial.SetVector("_SkyUVTransform",new Vector4(180f/26,100f/18,.5f-90f/26,.5f-43f/18));
        if(daylight.sky.sharedMaterial.HasProperty("_CloudTextureEnabled"))
        {
            const string cloudName="AnimeCloudMask.png";string cloudPath=HusbandryRoomSetup.TextureFolder+"/"+cloudName;
            daylight.sky.sharedMaterial.SetFloat("_CloudTextureEnabled",0);
            if(AssetDatabase.LoadAssetAtPath<Texture2D>(cloudPath))
            {
                Texture(cloudName,false);
                var cloudImporter=(TextureImporter)AssetImporter.GetAtPath(cloudPath);
                if(cloudImporter.sRGBTexture){cloudImporter.sRGBTexture=false;cloudImporter.SaveAndReimport();}
                daylight.sky.sharedMaterial.SetTexture("_CloudTex",AssetDatabase.LoadAssetAtPath<Texture2D>(cloudPath));
                daylight.sky.sharedMaterial.SetFloat("_CloudTextureEnabled",1);
            }
        }
        if(daylight.sky.sharedMaterial.HasProperty("_MountainEnabled"))
        {
            const string mountainName="AnimeMountainVista.png";string mountainPath=HusbandryRoomSetup.TextureFolder+"/"+mountainName;
            daylight.sky.sharedMaterial.SetFloat("_MountainEnabled",0);
            if(AssetDatabase.LoadAssetAtPath<Texture2D>(mountainPath))
            {
                Texture(mountainName,false);
                var mountainImporter=(TextureImporter)AssetImporter.GetAtPath(mountainPath);
                if(!mountainImporter.sRGBTexture){mountainImporter.sRGBTexture=true;mountainImporter.SaveAndReimport();}
                daylight.sky.sharedMaterial.SetTexture("_MountainTex",AssetDatabase.LoadAssetAtPath<Texture2D>(mountainPath));
                daylight.sky.sharedMaterial.SetFloat("_MountainEnabled",1);
            }
        }
        EditorUtility.SetDirty(daylight.sky.sharedMaterial);
        daylight.sky.transform.position=room.TransformPoint(exterior.localPosition+new Vector3(0,8,26));
        daylight.sky.transform.localScale=new Vector3(180,100,1);
    }

    static void ValidateResources(bool procedural)
    {
        foreach(string texture in procedural?new[]{"TreeBark-SoftToon.png","TreeLeafVeins-SoftToon.png"}:Array.Empty<string>())
            if(!AssetDatabase.LoadAssetAtPath<Texture2D>(HusbandryRoomSetup.TextureFolder+"/"+texture))
                throw new FileNotFoundException("Import the generated outdoor tree texture before installing the tree.",HusbandryRoomSetup.TextureFolder+"/"+texture);
        foreach(string shader in procedural?new[]{HusbandryRoomSetup.SoftShader,FoliageShader,WeatherShader}:new[]{WeatherShader})
            if(!Shader.Find(shader))throw new InvalidOperationException("Outdoor tree shader is missing: "+shader);
    }

    static Texture2D Texture(string name,bool repeat)
    {
        string path=HusbandryRoomSetup.TextureFolder+"/"+name;var importer=AssetImporter.GetAtPath(path) as TextureImporter;
        if(!importer)throw new FileNotFoundException("Import the generated outdoor tree texture first.",path);
        var wrap=repeat?TextureWrapMode.Repeat:TextureWrapMode.Clamp;
        if(!importer.mipmapEnabled||importer.wrapMode!=wrap||importer.anisoLevel!=4||importer.maxTextureSize!=2048||importer.textureCompression!=TextureImporterCompression.CompressedHQ)
        {
            importer.mipmapEnabled=true;importer.wrapMode=wrap;importer.anisoLevel=4;importer.maxTextureSize=2048;
            importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void EnsureFolder(string path)
    {
        if(AssetDatabase.IsValidFolder(path))return;
        string parent=Path.GetDirectoryName(path).Replace('\\','/');EnsureFolder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
    }

    static Material Material(string name,string shaderName,Color color)
    {
        string path=Folder+"/"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        var updated=new Material(Shader.Find(shaderName)){name=name};
        if(updated.HasProperty("_BaseColor"))updated.SetColor("_BaseColor",color);
        if(updated.HasProperty("_Stylization"))
        {
            updated.SetFloat("_Stylization",.35f);updated.SetFloat("_BandSoftness",.18f);updated.SetFloat("_AmbientStrength",1.1f);
            updated.SetFloat("_Smoothness",.08f);updated.SetFloat("_SpecStrength",.01f);updated.SetFloat("_Cull",0);
        }
        if(material){EditorUtility.CopySerialized(updated,material);UnityEngine.Object.DestroyImmediate(updated);EditorUtility.SetDirty(material);}
        else{material=updated;AssetDatabase.CreateAsset(material,path);}
        return material;
    }

    static Mesh SaveMesh(string name,Mesh generated)
    {
        string path=Folder+"/"+name+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(mesh){EditorUtility.CopySerialized(generated,mesh);UnityEngine.Object.DestroyImmediate(generated);EditorUtility.SetDirty(mesh);return mesh;}
        AssetDatabase.CreateAsset(generated,path);return generated;
    }

    static MeshRenderer MeshObject(Transform parent,string name,Mesh mesh,Material material)
    {
        var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent,false);
        go.GetComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.GetComponent<MeshRenderer>();renderer.sharedMaterial=material;
        renderer.shadowCastingMode=ShadowCastingMode.TwoSided;renderer.receiveShadows=true;return renderer;
    }

    sealed class Branch
    {
        public readonly Vector3[] Points;public readonly float[] Radii;
        public Branch(Vector3[] points,float[] radii){Points=points;Radii=radii;}
    }
    readonly struct Crown
    {
        public readonly Vector3 Center,Radius;public readonly int Count;
        public Crown(Vector3 center,Vector3 radius,int count){Center=center;Radius=radius;Count=count;}
    }
    static List<Branch> Branches()
    {
        var result=new List<Branch>{
            new Branch(new[]{new Vector3(-6.8f,-5,12),new Vector3(-6.6f,-1,12),new Vector3(-6.2f,3,12.3f),new Vector3(-6,7,12.6f),new Vector3(-5.6f,11,13.1f),new Vector3(-5.9f,15.5f,13.7f)},new[]{1.05f,.91f,.76f,.62f,.37f,.04f}),
            new Branch(new[]{new Vector3(-6.4f,1.8f,12.2f),new Vector3(-5.2f,3.7f,11.9f),new Vector3(-3.4f,5.2f,12.3f),new Vector3(-1.5f,7.3f,13),new Vector3(-.4f,9.4f,13.3f)},new[]{.53f,.4f,.27f,.14f,.025f}),
            new Branch(new[]{new Vector3(-6.1f,4.5f,12.4f),new Vector3(-7.7f,5.7f,12.8f),new Vector3(-9.5f,6.4f,13.1f),new Vector3(-11.6f,8.3f,13.6f)},new[]{.46f,.32f,.17f,.025f}),
            new Branch(new[]{new Vector3(-6,6.8f,12.5f),new Vector3(-4.5f,8.3f,12.9f),new Vector3(-2.8f,9.3f,13.4f),new Vector3(-1.6f,11.9f,14.1f)},new[]{.43f,.31f,.16f,.025f}),
            new Branch(new[]{new Vector3(-5.8f,8.6f,12.8f),new Vector3(-7.8f,10,13.1f),new Vector3(-9.5f,11.2f,13.8f),new Vector3(-10.9f,13.7f,14.3f)},new[]{.39f,.27f,.13f,.025f}),
            new Branch(new[]{new Vector3(-5.7f,10.2f,13),new Vector3(-4.3f,11.9f,12.7f),new Vector3(-3.2f,13.3f,13.7f),new Vector3(-1.8f,15.2f,14.1f)},new[]{.3f,.22f,.12f,.02f})
        };
        // Alternating thin forks belong to actual branch ends, making the winter silhouette readable.
        int primary=result.Count;
        for(int i=1;i<primary;i++)
        {
            var source=result[i];
            for(int fork=0;fork<3;fork++)
            {
                int segment=Mathf.Min(source.Points.Length-2,fork+1);Vector3 start=source.Points[segment];float side=(i+fork)%2==0?-1:1;
                result.Add(new Branch(new[]{start,start+new Vector3(side*.8f,1,.25f*side),start+new Vector3(side*1.4f,2.15f,.55f*side)},
                    new[]{source.Radii[segment]*.46f,.07f,.012f}));
            }
        }
        return result;
    }

    static void Sample(Branch branch,int segment,float t,out Vector3 point,out float radius)
    {
        var p=branch.Points;Vector3 a=p[Mathf.Max(0,segment-1)],b=p[segment],c=p[segment+1],d=p[Mathf.Min(p.Length-1,segment+2)];
        point=.5f*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t);
        radius=Mathf.Lerp(branch.Radii[segment],branch.Radii[segment+1],t);
    }
    static void AddTube(Geometry geometry,Branch branch,int sides)
    {
        const int steps=6;int rings=(branch.Points.Length-1)*steps+1,first=geometry.Vertices.Count;float length=0;
        Vector3 previous=branch.Points[0];
        for(int ring=0;ring<rings;ring++)
        {
            int segment=Mathf.Min(branch.Points.Length-2,ring/steps);float t=(float)(ring-segment*steps)/steps;
            Sample(branch,segment,t,out var point,out float radius);
            Sample(branch,segment,Mathf.Max(0,t-.025f),out var before,out _);Sample(branch,segment,Mathf.Min(1,t+.025f),out var after,out _);
            Vector3 tangent=(after-before).normalized,x=Vector3.Cross(tangent,Vector3.forward).normalized,y=Vector3.Cross(tangent,x).normalized;
            length+=Vector3.Distance(previous,point);previous=point;
            for(int side=0;side<=sides;side++)
            {
                float angle=(float)side/sides*Mathf.PI*2;
                float rough=1+.045f*Mathf.Sin(angle*3+ring*.4f)+.025f*Mathf.Cos(angle*5-ring*.22f);
                geometry.Vertex(point+(x*Mathf.Cos(angle)+y*Mathf.Sin(angle))*radius*rough,new Vector2((float)side/sides,length*.32f),Vector2.zero);
                if(ring<rings-1&&side<sides)
                {
                    int a=first+ring*(sides+1)+side,b=a+1,c=a+sides+1,d=c+1;
                    geometry.Triangle(a,b,c);geometry.Triangle(b,d,c);
                }
            }
        }
        int bottom=geometry.Vertex(branch.Points[0],new Vector2(.5f,0),Vector2.zero);
        int top=geometry.Vertex(branch.Points[branch.Points.Length-1],new Vector2(.5f,1),Vector2.zero);
        for(int side=0;side<sides;side++)
        {geometry.Triangle(bottom,first+side+1,first+side);geometry.Triangle(top,first+(rings-1)*(sides+1)+side,first+(rings-1)*(sides+1)+side+1);}
    }

    static void AddLeaf(Geometry geometry,Vector3 center,Quaternion rotation,Vector2 size,float seed)
    {
        const int rows=19;int first=geometry.Vertices.Count;
        for(int row=0;row<rows;row++)
        {
            float t=(float)row/(rows-1),fold=Mathf.Max(0,Mathf.Sin(t*Mathf.PI));
            float width=.46f*Mathf.Pow(fold,.8f)*(1-.12f*t);
            for(int column=-1;column<=1;column++)
            {
                var point=new Vector3(column*width*size.x,t*size.y,fold*(column==0?.035f:-.012f)*size.y);
                geometry.Vertex(center+rotation*point,new Vector2(.5f+column*width,t),new Vector2(seed,t));
            }
            if(row<rows-1)for(int column=0;column<2;column++)
            {int a=first+row*3+column;geometry.Triangle(a,a+1,a+3);geometry.Triangle(a+1,a+4,a+3);}
        }
    }

    static void AddSnowCap(Geometry geometry,Branch branch)
    {
        const int steps=8,across=8,stride=(across+1)*2;
        for(int segment=0;segment<branch.Points.Length-1;segment++)
        {
            Vector3 direction=(branch.Points[segment+1]-branch.Points[segment]).normalized;
            if(Mathf.Abs(direction.y)>.88f)continue;
            int first=geometry.Vertices.Count;
            for(int row=0;row<=steps;row++)
            {
                float t=(float)row/steps;Sample(branch,segment,t,out var point,out float radius);
                Sample(branch,segment,Mathf.Max(0,t-.025f),out var before,out _);
                Sample(branch,segment,Mathf.Min(1,t+.025f),out var after,out _);
                Vector3 tangent=(after-before).normalized;
                Vector3 up=Vector3.ProjectOnPlane(Vector3.up,tangent).normalized,side=Vector3.Cross(tangent,up).normalized;
                float along=(segment+t)/(branch.Points.Length-1);
                float thickness=(.03f+radius*.14f)*Mathf.Lerp(.4f,1,Mathf.Sin(along*Mathf.PI));
                for(int column=0;column<=across;column++)
                {
                    float u=(float)column/across,angle=Mathf.Lerp(.30f,.70f,u)*Mathf.PI;
                    Vector3 radial=side*Mathf.Cos(angle)+up*Mathf.Sin(angle);
                    // A closed upper shell rests on the actual curved bark. The extended rim and
                    // rounded shoulder stay visibly outside its 7% surface irregularities.
                    Vector3 outer=point+radial*(radius*1.09f)+side*Mathf.Cos(angle)*(thickness*.15f)+up*(Mathf.Sin(angle)*thickness+.012f);
                    Vector3 inner=point+radial*(radius*1.02f);
                    geometry.Vertex(outer,new Vector2(u,t),Vector2.zero);
                    geometry.Vertex(inner,new Vector2(u,t),Vector2.zero);
                    if(row<steps&&column<across)
                    {
                        int a=first+row*stride+column*2,b=a+2,c=a+stride,d=c+2;
                        geometry.Triangle(a,c,b);geometry.Triangle(b,c,d);
                        geometry.Triangle(a+1,b+1,c+1);geometry.Triangle(b+1,d+1,c+1);
                    }
                }
                if(row<steps)
                {
                    int front=first+row*stride,back=front+across*2;
                    geometry.Triangle(front,front+1,front+stride);geometry.Triangle(front+1,front+stride+1,front+stride);
                    geometry.Triangle(back,back+stride,back+1);geometry.Triangle(back+1,back+stride,back+stride+1);
                }
            }
            // Seal both ends so the front edge reads as a soft layer with depth, not a white line.
            for(int column=0;column<across;column++)
            {
                int a=first+column*2,b=a+2,end=a+steps*stride,endNext=end+2;
                geometry.Triangle(a,b,a+1);geometry.Triangle(b,b+1,a+1);
                geometry.Triangle(end,end+1,endNext);geometry.Triangle(endNext,end+1,endNext+1);
            }
        }
    }

    static void AddBud(Geometry geometry,Vector3 point,float radius)
    {
        int first=geometry.Vertices.Count;
        foreach(var offset in new[]{Vector3.up*radius*2.1f,Vector3.right*radius,Vector3.forward*radius,Vector3.left*radius,Vector3.back*radius,Vector3.down*radius*.7f})
            geometry.Vertex(point+offset,new Vector2(.5f,.5f),Vector2.zero);
        for(int i=0;i<4;i++){int a=first+1+i,b=first+1+(i+1)%4;geometry.Triangle(first,a,b);geometry.Triangle(first+5,b,a);}
    }

    static Mesh Disk(string name,int sides)
    {
        var geometry=new Geometry();geometry.Vertex(Vector3.zero,new Vector2(.5f,.5f),Vector2.zero);
        for(int i=0;i<sides;i++)
        {
            float angle=i*Mathf.PI*2/sides;Vector3 point=new Vector3(Mathf.Cos(angle),Mathf.Sin(angle),0)*.5f;
            geometry.Vertex(point,new Vector2(point.x+.5f,point.y+.5f),Vector2.zero);
        }
        for(int i=0;i<sides;i++)geometry.Triangle(0,1+i,1+(i+1)%sides);return geometry.Build(name);
    }

    static ParticleSystem Weather(Transform parent,string name,Mesh mesh,Material material,int maximum,Vector3 center,Vector3 volume,
        Vector2 size,Vector2 fallSpeed,Color color,uint seed)
    {
        var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.localPosition=center;
        var particles=go.AddComponent<ParticleSystem>();particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        particles.useAutoRandomSeed=false;particles.randomSeed=seed;
        var main=particles.main;main.loop=true;main.duration=14;main.playOnAwake=true;main.maxParticles=maximum;
        main.simulationSpace=ParticleSystemSimulationSpace.Local;main.startSpeed=0;main.gravityModifier=0;
        main.startLifetime=new ParticleSystem.MinMaxCurve(9,14);main.startSize=new ParticleSystem.MinMaxCurve(size.x,size.y);main.startColor=color;
        main.startRotation3D=true;main.startRotationX=new ParticleSystem.MinMaxCurve(0,Mathf.PI*2);
        main.startRotationY=new ParticleSystem.MinMaxCurve(0,Mathf.PI*2);main.startRotationZ=new ParticleSystem.MinMaxCurve(0,Mathf.PI*2);
        var emission=particles.emission;emission.enabled=true;emission.rateOverTime=0;
        var shape=particles.shape;shape.enabled=true;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=volume;
        var velocity=particles.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.Local;
        velocity.x=new ParticleSystem.MinMaxCurve(.06f,.22f);velocity.y=new ParticleSystem.MinMaxCurve(-fallSpeed.y,-fallSpeed.x);velocity.z=new ParticleSystem.MinMaxCurve(0,0);
        var noise=particles.noise;noise.enabled=false;
        var rotation=particles.rotationOverLifetime;rotation.enabled=true;rotation.separateAxes=true;
        rotation.x=new ParticleSystem.MinMaxCurve(-.45f,.65f);rotation.y=new ParticleSystem.MinMaxCurve(-.35f,.35f);rotation.z=new ParticleSystem.MinMaxCurve(-.55f,.75f);
        var fading=particles.colorOverLifetime;fading.enabled=true;var gradient=new Gradient();
        gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
            new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.12f),new GradientAlphaKey(1,.72f),new GradientAlphaKey(0,1)});
        fading.color=gradient;
        var renderer=particles.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.Mesh;renderer.mesh=mesh;
        renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        return particles;
    }

    static float Range(System.Random random,float min,float max)=>Mathf.Lerp(min,max,(float)random.NextDouble());
    static Vector3 Ball(System.Random random)
    {
        Vector3 sample;do{sample=new Vector3(Range(random,-1,1),Range(random,-1,1),Range(random,-1,1));}while(sample.sqrMagnitude>1);
        return sample;
    }
    sealed class Geometry
    {
        public readonly List<Vector3> Vertices=new List<Vector3>();
        readonly List<Vector2> uv=new List<Vector2>(),uv2=new List<Vector2>();readonly List<int> triangles=new List<int>();
        public int Vertex(Vector3 point,Vector2 texture,Vector2 seasonal)
        {int index=Vertices.Count;Vertices.Add(point);uv.Add(texture);uv2.Add(seasonal);return index;}
        public void Triangle(int a,int b,int c){triangles.Add(a);triangles.Add(b);triangles.Add(c);}
        public Mesh Build(string name)
        {
            var mesh=new Mesh{name=name,indexFormat=Vertices.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};
            mesh.SetVertices(Vertices);mesh.SetUVs(0,uv);mesh.SetUVs(1,uv2);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
    }
}
