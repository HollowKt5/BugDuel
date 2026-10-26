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

/// <summary>Installs the reusable open glass enclosure without changing the care floor's identity or bounds.</summary>
public static class HusbandryTerrariumSetup
{
    public const string Folder="Assets/BeetleArena/Generated/Husbandry";
    public const string PrefabPath=Folder+"/GlassEnclosure.prefab";
    public const string SoilMeshPath=Folder+"/SoilCube.asset";
    public const string MaterialFolder=Folder+"/Materials";
    public const string SoilTopMaterialPath=MaterialFolder+"/SoilTop.mat";
    public const string SoilSideMaterialPath=MaterialFolder+"/SoilSide.mat";
    public const string GlassShaderName="BeetleArena/HusbandryGlass";
    public const string GlassLowShaderName="BeetleArena/HusbandryGlassLow";
    const string TextureFolder="Assets/BeetleArena/Art/Husbandry/Textures";
    const string GreenhouseScenePath="Assets/Scenes/Campaign/CampaignGreenhouse.unity";
    public static readonly Vector3 SoilSize=new Vector3(9.4f,.4f,7.4f);
    public const float GlassHeight=HusbandryGlassEnclosure.DefaultHeight;

    [MenuItem("Tools/甲壳虫斗/画面与资源/养殖表现/应用玻璃养殖箱", false, 320)]
    public static void Apply()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play before changing husbandry terrariums.");
        for(int i=0;i<SceneManager.sceneCount;i++)
        {
            var open=SceneManager.GetSceneAt(i);
            if(open.isDirty||string.IsNullOrEmpty(open.path))throw new InvalidOperationException("Save all open scenes before applying husbandry terrariums.");
        }
        var setup=EditorSceneManager.GetSceneManagerSetup();
        try
        {
            EnsureAssets();
            foreach(string path in new[]{BeetleNurseryNavigation.NurseryScenePath,GreenhouseScenePath})
            {
                var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
                var terrarium=scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<Transform>(true)).Single(t=>t.name=="Terrarium");
                ApplyToTerrarium(terrarium);
                foreach(var camera in scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<Camera>(true)))
                    if(camera.cullingMask!=0)camera.GetUniversalAdditionalCameraData();
                EditorSceneManager.MarkSceneDirty(scene);
                if(!EditorSceneManager.SaveScene(scene))throw new IOException("Could not save husbandry terrarium: "+path);
            }
            AssetDatabase.SaveAssets();
        }
        finally {EditorSceneManager.RestoreSceneManagerSetup(setup);}
        Debug.Log("Glass terrariums installed in BeetleNursery and CampaignGreenhouse; existing SoilBed bounds preserved.");
    }

    public static void EnsureAssets()
    {
        EnsureFolder(MaterialFolder);
        var top=AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder+"/SoilTop.png");
        var side=AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder+"/SoilSide.png");
        if(!top||!side)throw new InvalidOperationException("Import SoilTop.png and SoilSide.png in "+TextureFolder+" before installing the terrarium.");
        var lit=Shader.Find("Universal Render Pipeline/Lit");
        var glassShader=Shader.Find(GlassShaderName);
        if(!lit||!glassShader)throw new InvalidOperationException("The URP Lit and husbandry glass shaders must be imported first.");
        SoilMaterial(SoilTopMaterialPath,lit,top);SoilMaterial(SoilSideMaterialPath,lit,side);
        var glass=AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder+"/Glass.mat");
        if(!glass){glass=new Material(glassShader){name="Husbandry Glass"};AssetDatabase.CreateAsset(glass,MaterialFolder+"/Glass.mat");}
        var low=AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder+"/GlassLow.mat");
        if(!low){low=new Material(Shader.Find(GlassLowShaderName)){name="Husbandry Glass Low"};AssetDatabase.CreateAsset(low,MaterialFolder+"/GlassLow.mat");}
        var frame=AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder+"/Frame.mat");
        if(!frame)
        {
            frame=new Material(lit){name="Terrarium Frame"};frame.SetColor("_BaseColor",new Color(.12f,.15f,.14f));
            BeetleArenaToonSetup.Configure(frame);frame.SetFloat("_OutlineWidth",0);AssetDatabase.CreateAsset(frame,MaterialFolder+"/Frame.mat");
        }
        if(!AssetDatabase.LoadAssetAtPath<Mesh>(SoilMeshPath))CreateSoilMesh();
        if(!AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath))CreateEnclosure(glass,frame);
        else EnsureHeightControl();
    }

    public static void ApplyToTerrarium(Transform root)
    {
        if(!root)throw new ArgumentNullException(nameof(root));
        var soil=root.Find("SoilBed");
        if(!soil||!soil.TryGetComponent<MeshFilter>(out var filter)||!soil.TryGetComponent<MeshRenderer>(out var renderer))
            throw new InvalidOperationException("The existing Terrarium/SoilBed cube is required.");
        EnsureAssets();
        filter.sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(SoilMeshPath);
        renderer.sharedMaterials=new[]{AssetDatabase.LoadAssetAtPath<Material>(SoilTopMaterialPath),AssetDatabase.LoadAssetAtPath<Material>(SoilSideMaterialPath)};
        EditorUtility.SetDirty(filter);EditorUtility.SetDirty(renderer);
        PrefabUtility.RecordPrefabInstancePropertyModifications(filter);PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        foreach(string name in new[]{"LeftRim","RightRim","BackRim","FrontRim"})
        {
            var rim=root.Find(name);if(rim)UnityEngine.Object.DestroyImmediate(rim.gameObject);
        }
        var enclosure=root.Find("GlassEnclosure");
        if(!enclosure)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            enclosure=((GameObject)PrefabUtility.InstantiatePrefab(prefab,root)).transform;
        }
        enclosure.localPosition=soil.localPosition;enclosure.localRotation=soil.localRotation;enclosure.localScale=Vector3.one;
        PrefabUtility.RecordPrefabInstancePropertyModifications(enclosure);
    }

    public static bool IsTerrariumMaterial(Material material)
    {
        if(!material)return false;
        if(material.shader&&(material.shader.name==GlassShaderName||material.shader.name==GlassLowShaderName))return true;
        string path=AssetDatabase.GetAssetPath(material);
        return path==SoilTopMaterialPath||path==SoilSideMaterialPath||path==HusbandryLightingSetup.PoolMaterialPath;
    }

    static void SoilMaterial(string path,Shader shader,Texture2D texture)
    {
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material)return;
        material=new Material(shader){name=Path.GetFileNameWithoutExtension(path)};AssetDatabase.CreateAsset(material,path);
        float brightness=path==SoilTopMaterialPath?.64f:.74f;
        material.shader=shader;material.SetTexture("_BaseMap",texture);material.SetColor("_BaseColor",new Color(brightness,brightness,brightness));
        material.SetTextureScale("_BaseMap",Vector2.one);material.SetTextureOffset("_BaseMap",Vector2.zero);
        material.SetFloat("_Metallic",0);material.SetFloat("_Smoothness",.08f);EditorUtility.SetDirty(material);
    }

    static void CreateSoilMesh()
    {
        var temporary=GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            var mesh=UnityEngine.Object.Instantiate(temporary.GetComponent<MeshFilter>().sharedMesh);mesh.name="SoilCube";
            var vertices=mesh.vertices;var normals=mesh.normals;var uv=new Vector2[vertices.Length];
            for(int i=0;i<vertices.Length;i++)
            {
                var v=vertices[i];var n=normals[i];
                uv[i]=Mathf.Abs(n.y)>.5f?new Vector2((v.x+.5f)*SoilSize.x/2.4f,(v.z+.5f)*SoilSize.z/2.4f):
                    new Vector2((Mathf.Abs(n.x)>.5f?v.z+.5f:v.x+.5f)*(Mathf.Abs(n.x)>.5f?SoilSize.z:SoilSize.x)/2.4f,v.y+.5f);
            }
            var top=new List<int>();var sides=new List<int>();var triangles=mesh.triangles;
            for(int i=0;i<triangles.Length;i+=3)
            {
                var target=normals[triangles[i]].y>.5f?top:sides;
                target.Add(triangles[i]);target.Add(triangles[i+1]);target.Add(triangles[i+2]);
            }
            mesh.uv=uv;mesh.subMeshCount=2;mesh.SetTriangles(top,0,false);mesh.SetTriangles(sides,1,false);mesh.RecalculateTangents();
            AssetDatabase.CreateAsset(mesh,SoilMeshPath);
        }
        finally {UnityEngine.Object.DestroyImmediate(temporary);}
    }

    static void CreateEnclosure(Material glass,Material frame)
    {
        var preview=EditorSceneManager.NewPreviewScene();var go=new GameObject("GlassEnclosure");
        SceneManager.MoveGameObjectToScene(go,preview);
        try
        {
            var root=go.transform;float x=SoilSize.x*.5f+.045f,z=SoilSize.z*.5f+.045f;
            float bottom=-SoilSize.y*.5f,top=bottom+GlassHeight,middle=(bottom+top)*.5f;
            Cube(root,"GlassLeft",new Vector3(-x,middle,0),new Vector3(.04f,GlassHeight,z*2),glass);
            Cube(root,"GlassRight",new Vector3(x,middle,0),new Vector3(.04f,GlassHeight,z*2),glass);
            Cube(root,"GlassBack",new Vector3(0,middle,z),new Vector3(x*2,GlassHeight,.04f),glass);
            Cube(root,"GlassFront",new Vector3(0,middle,-z),new Vector3(x*2,GlassHeight,.04f),glass);
            foreach(bool upper in new[]{false,true})
            {
                string edge=upper?"Top":"Base";float y=upper?top:bottom;float width=upper?.045f:.075f;
                Cube(root,edge+"Left",new Vector3(-x,y,0),new Vector3(width,width,z*2+width),frame);
                Cube(root,edge+"Right",new Vector3(x,y,0),new Vector3(width,width,z*2+width),frame);
                Cube(root,edge+"Back",new Vector3(0,y,z),new Vector3(x*2,width,width),frame);
                Cube(root,edge+"Front",new Vector3(0,y,-z),new Vector3(x*2,width,width),frame);
            }
            for(int side=0;side<4;side++)Cube(root,"CornerSeam"+side,new Vector3(side<2?-x:x,middle,side%2==0?-z:z),new Vector3(.028f,GlassHeight,.028f),frame);
            BindHeightControl(go);
            BindSurface(go);
            if(!PrefabUtility.SaveAsPrefabAsset(go,PrefabPath))throw new IOException("Could not save the glass enclosure prefab.");
        }
        finally {EditorSceneManager.ClosePreviewScene(preview);}
    }

    static void EnsureHeightControl()
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if(prefab.GetComponent<HusbandryGlassEnclosure>()&&prefab.GetComponent<HusbandryGlassSurface>())return;
        var root=PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            if(!root.GetComponent<HusbandryGlassEnclosure>())BindHeightControl(root);
            BindSurface(root);PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
        }
        finally {PrefabUtility.UnloadPrefabContents(root);}
    }

    static void BindSurface(GameObject root)
    {
        var surface=root.GetComponent<HusbandryGlassSurface>();
        if(!surface)surface=root.AddComponent<HusbandryGlassSurface>();
        surface.Configure(AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder+"/Glass.mat"),
            AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder+"/GlassLow.mat"),
            new[]{"GlassLeft","GlassRight","GlassBack","GlassFront"}.Select(n=>root.transform.Find(n).GetComponent<MeshRenderer>()).ToArray());
    }

    static void BindHeightControl(GameObject root)
    {
        var size=root.AddComponent<HusbandryGlassEnclosure>();
        size.Configure(new[]{"GlassLeft","GlassRight","GlassBack","GlassFront"}.Select(n=>root.transform.Find(n)).ToArray(),
            new[]{"TopLeft","TopRight","TopBack","TopFront"}.Select(n=>root.transform.Find(n)).ToArray(),
            Enumerable.Range(0,4).Select(i=>root.transform.Find("CornerSeam"+i)).ToArray());
    }

    static void Cube(Transform parent,string name,Vector3 position,Vector3 scale,Material material)
    {
        var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.name=name;cube.transform.SetParent(parent,false);
        cube.transform.localPosition=position;cube.transform.localScale=scale;
        UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
        var renderer=cube.GetComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;
    }

    static void EnsureFolder(string path)
    {
        if(AssetDatabase.IsValidFolder(path))return;
        string parent=Path.GetDirectoryName(path).Replace('\\','/');EnsureFolder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
    }
}
