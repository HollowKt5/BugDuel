using System;
using System.IO;
using System.Linq;
using System.Text;
using BeetleArena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class HusbandryTerrariumValidation
{
    const string Folder="Assets/BeetleArena/Generated/Husbandry/";
    const string Textures="Assets/BeetleArena/Art/Husbandry/Textures/";
    const string Report="Artifacts/Terrarium/Data.txt";
    static readonly string[] Panes={"GlassLeft","GlassRight","GlassBack","GlassFront"};

    [MenuItem("Tools/甲壳虫斗/验证/养殖/玻璃养殖箱", false, 545)]
    public static void Run()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before terrarium checks.");
        var log=new StringBuilder();int checks=0;
        var previous=SceneManager.GetActiveScene();Scene preview=default;
        void Check(bool value,string detail)
        {if(!value)throw new InvalidOperationException(detail);log.AppendLine("PASS "+(++checks)+" "+detail);}
        log.AppendLine("Terrarium data validation "+DateTime.UtcNow.ToString("O"));
        try
        {
            var top=AssetDatabase.LoadAssetAtPath<Texture2D>(Textures+"SoilTop.png");
            var side=AssetDatabase.LoadAssetAtPath<Texture2D>(Textures+"SoilSide.png");
            Check(top&&side&&top!=side,"top and cut-side soil photographs are separate imported textures");
            var topMaterial=AssetDatabase.LoadAssetAtPath<Material>(Folder+"Materials/SoilTop.mat");
            var sideMaterial=AssetDatabase.LoadAssetAtPath<Material>(Folder+"Materials/SoilSide.mat");
            Check(topMaterial&&sideMaterial&&topMaterial.GetTexture("_BaseMap")==top&&sideMaterial.GetTexture("_BaseMap")==side,
                "persistent soil materials use their corresponding photographs");
            Check(Near(topMaterial.GetColor("_BaseColor").r,HusbandryTerrariumSetup.SoilTopBrightness)&&Near(topMaterial.GetFloat("_Smoothness"),0)&&
                topMaterial.IsKeywordEnabled("_SPECULARHIGHLIGHTS_OFF")&&topMaterial.IsKeywordEnabled("_ENVIRONMENTREFLECTIONS_OFF"),
                "soil top is darker and matte without altering scene lights");
            Check(Near(sideMaterial.GetColor("_BaseColor").r,HusbandryTerrariumSetup.SoilSideBrightness)&&Near(sideMaterial.GetFloat("_Smoothness"),0)&&
                sideMaterial.IsKeywordEnabled("_SPECULARHIGHLIGHTS_OFF")&&sideMaterial.IsKeywordEnabled("_ENVIRONMENTREFLECTIONS_OFF"),
                "soil cut sides retain their photograph with a darker matte tint");
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"GlassEnclosure.prefab");
            Check(prefab&&Panes.All(name=>prefab.transform.Find(name)?.GetComponent<MeshRenderer>()),"glass prefab contains all four separate panes");
            var glass=prefab.transform.Find(Panes[0]).GetComponent<MeshRenderer>().sharedMaterial;
            Check(glass&&glass.shader&&glass.shader.name=="BeetleArena/HusbandryGlass"&&glass.renderQueue>2500,
                "glass retains its transparent shader and queue");
            foreach(var shader in new[]{topMaterial.shader,sideMaterial.shader,glass.shader}.Distinct())
                Check(shader&&shader.isSupported&&!ShaderUtil.ShaderHasError(shader),"shader compiles: "+shader.name);

            foreach(var path in new[]{"Assets/Scenes/BeetleNursery.unity","Assets/Scenes/Campaign/CampaignGreenhouse.unity"})
            {
                preview=EditorSceneManager.OpenPreviewScene(path);
                var roots=preview.GetRootGameObjects();
                var floor=roots.SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>(true)).Single(r=>r.name=="SoilBed");
                var backdrop=roots.SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>(true)).Single(r=>r.name=="Backdrop");
                var rig=roots.SelectMany(g=>g.GetComponentsInChildren<BeetleProceduralAnimator>(true)).First();
                string sceneName=preview.name;
                var soilSize=HusbandryTerrariumSetup.SoilSize;
                Check(Near(soilSize.y,1.6f)&&floor.transform.parent.name=="Terrarium"&&Near(floor.transform.localPosition,new Vector3(0,-.02f-soilSize.y*.5f,0))&&
                    Near(floor.transform.localScale,soilSize),sceneName+": soil is 1.6 deep, twice the previous 0.8, and extends downward");
                var mesh=floor.GetComponent<MeshFilter>().sharedMesh;
                Check(mesh&&Near(mesh.bounds.center,Vector3.zero)&&Near(mesh.bounds.size,Vector3.one),sceneName+": unit mesh bounds preserved");
                Check(Near(floor.bounds.max.y,-.02f)&&Near(floor.bounds.size,soilSize),sceneName+": floor footprint and top height preserved");
                Check(backdrop.bounds.max.y<=floor.bounds.min.y-HusbandryTerrariumSetup.BackdropClearance+.0001f,
                    sceneName+": backdrop sits at least 0.10 below the soil bottom and cannot hide its added depth");
                Check(mesh.subMeshCount==2&&floor.sharedMaterials.Length==2&&floor.sharedMaterials[0]==topMaterial&&floor.sharedMaterials[1]==sideMaterial,
                    sceneName+": top and sides use their persistent material slots");
                var vertices=mesh.vertices;var topIndices=mesh.GetTriangles(0);var sideIndices=mesh.GetTriangles(1);
                Check(topIndices.Length==6&&topIndices.All(i=>Near(vertices[i].y,.5f))&&sideIndices.Length==30&&sideIndices.Any(i=>Near(vertices[i].y,-.5f)),
                    sceneName+": photo top covers the upward face; cut-side material covers sides and bottom");
                var enclosure=floor.transform.parent.Find("GlassEnclosure");
                Check(enclosure&&PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(enclosure.gameObject)==Folder+"GlassEnclosure.prefab",
                    sceneName+": saved scene references the glass prefab");
                Check(Near(enclosure.Find("BaseFront").position.y,floor.bounds.min.y)&&Near(enclosure.Find("TopFront").position.y,5.18f),
                    sceneName+": glass lower frame follows deeper soil while its upper edge stays fixed");
                var lowerRails=new[]{"BaseLeft","BaseRight","BaseBack","BaseFront"}.Select(name=>enclosure.Find(name).GetComponent<Renderer>()).ToArray();
                Check(lowerRails.All(rail=>rail.bounds.min.y>backdrop.bounds.max.y+.001f),
                    sceneName+": all four bottom rails remain entirely above the backdrop");
                var pool=roots.SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>(true)).Single(r=>r.name=="SpotlightPool");
                Check(Near(pool.transform.position.y,backdrop.bounds.max.y+.005f)&&pool.bounds.max.y<floor.bounds.min.y&&
                    lowerRails.All(rail=>pool.bounds.max.y<rail.bounds.min.y),
                    sceneName+": ground light projection follows the backdrop below both soil and bottom frame");
                foreach(string name in Panes)
                {
                    var pane=enclosure.Find(name).GetComponent<MeshRenderer>();var bounds=pane.bounds;
                    bool alongX=name=="GlassLeft"||name=="GlassRight";
                    float thickness=alongX?bounds.size.x:bounds.size.z;
                    bool outside=alongX?bounds.max.x<=floor.bounds.min.x+.001f||bounds.min.x>=floor.bounds.max.x-.001f:
                        bounds.max.z<=floor.bounds.min.z+.001f||bounds.min.z>=floor.bounds.max.z-.001f;
                    Check(pane.enabled&&pane.gameObject.activeInHierarchy&&pane.sharedMaterial==glass&&thickness>0&&thickness<.25f&&
                        bounds.max.y>floor.bounds.max.y&&outside,sceneName+": "+name+" is a thin visible pane outside the soil footprint");
                }
                var layout=new CareEnclosureLayout(floor,rig,CareEnclosureLayout.MeasureClearance(rig));
                Check(Near(layout.GroundHeight,-.02f)&&layout.Area.CanWalk&&layout.Area.WalkablePoints>=8&&layout.Area.IsClear(layout.Area.Spawn),
                    sceneName+": actual enclosure retains reachable living space and original ground height");
                log.AppendLine($"  reachable={layout.Area.WalkablePoints}; span={layout.Area.ReachableBounds.size}; obstacles={layout.Area.ObstacleCount}");
                EditorSceneManager.ClosePreviewScene(preview);preview=default;
            }
            log.AppendLine("PASS terrarium contracts: "+checks+" checks");
            Debug.Log("Husbandry terrarium PASS: "+checks+" checks. "+Report);
        }
        catch(Exception error){log.AppendLine("FAIL "+error);throw;}
        finally
        {
            if(preview.IsValid())EditorSceneManager.ClosePreviewScene(preview);
            if(previous.IsValid()&&previous.isLoaded&&SceneManager.GetActiveScene()!=previous)SceneManager.SetActiveScene(previous);
            Directory.CreateDirectory(Path.GetDirectoryName(Report));File.WriteAllText(Report,log.ToString());
        }
    }
    static bool Near(float a,float b)=>Mathf.Abs(a-b)<.0001f;
    static bool Near(Vector3 a,Vector3 b)=>(a-b).sqrMagnitude<.00000001f;
}
