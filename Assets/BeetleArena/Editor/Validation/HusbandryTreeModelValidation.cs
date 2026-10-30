using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using BeetleArena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Exercises imported-tree bindings using transient objects; never writes a prefab or material asset.</summary>
public static class HusbandryTreeModelValidation
{
    public static string CheckLogic()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before imported-tree checks.");
        var scene=EditorSceneManager.NewPreviewScene();var report=new StringBuilder();int checks=0;
        var temporary=new List<UnityEngine.Object>();HusbandrySeasons seasons=null;
        void Check(bool valid,string message)
        {if(!valid)throw new InvalidOperationException("Imported tree: "+message);report.AppendLine("PASS tree model "+(++checks)+" "+message);}
        try
        {
            var litShader=Shader.Find("Universal Render Pipeline/Lit");var legacyShader=Shader.Find("Unlit/Color");
            Check(litShader&&legacyShader,"standard color shaders are available for imported model materials");
            var lit=new Material(litShader){name="Tree model validation Lit"};temporary.Add(lit);
            var legacy=new Material(legacyShader){name="Tree model validation Color"};temporary.Add(legacy);
            lit.SetColor("_BaseColor",new Color(.71f,.81f,.67f,.73f));legacy.SetColor("_Color",new Color(.65f,.75f,.61f,.67f));
            string litOriginal=EditorJsonUtility.ToJson(lit),legacyOriginal=EditorJsonUtility.ToJson(legacy);
            var mesh=new Mesh{name="Tree model validation mesh"};temporary.Add(mesh);
            mesh.vertices=new[]{Vector3.zero,Vector3.up,Vector3.right};mesh.uv=new[]{Vector2.zero,Vector2.up,Vector2.right};
            mesh.subMeshCount=2;mesh.SetTriangles(new[]{0,1,2},0);mesh.SetTriangles(new[]{0,1,2},1);
            mesh.bindposes=new[]{Matrix4x4.identity};
            mesh.boneWeights=new[]{new BoneWeight{boneIndex0=0,weight0=1},new BoneWeight{boneIndex0=0,weight0=1},new BoneWeight{boneIndex0=0,weight0=1}};
            mesh.RecalculateNormals();mesh.RecalculateBounds();
            var host=new GameObject("Season test host");SceneManager.MoveGameObjectToScene(host,scene);
            seasons=host.AddComponent<HusbandrySeasons>();seasons.previewDay=120.5f;
            var model=new GameObject("External asset A7");model.transform.SetParent(host.transform,false);
            model.transform.localPosition=new Vector3(2,3,4);model.transform.localRotation=Quaternion.Euler(8,39,2);model.transform.localScale=Vector3.one*1.7f;
            var adapter=model.AddComponent<HusbandryTreeModelAdapter>();
            Renderer Part(string name,bool skin,params Material[] materials)
            {
                var go=new GameObject(name);go.transform.SetParent(model.transform,false);
                Renderer renderer;
                if(skin)
                {
                    var value=go.AddComponent<SkinnedMeshRenderer>();var bone=new GameObject("Imported joint 42").transform;
                    bone.SetParent(go.transform,false);value.sharedMesh=mesh;value.bones=new[]{bone};value.rootBone=bone;renderer=value;
                }
                else{go.AddComponent<MeshFilter>().sharedMesh=mesh;renderer=go.AddComponent<MeshRenderer>();}
                renderer.sharedMaterials=materials;return renderer;
            }
            var leaves=Part("Node 08",false,lit,legacy);
            var skinned=Part("Geometry X",true,lit,lit);
            var hidden=Part("Unused author variant",false,lit,lit);hidden.enabled=false;
            var snowA=Part("Optional detail 1",false,lit,lit);snowA.enabled=false;
            var snowB=Part("Optional detail 2",true,lit,lit);snowB.enabled=false;
            var bud=Part("Optional detail 3",false,legacy,legacy);bud.enabled=false;
            var originalBlock=new MaterialPropertyBlock();originalBlock.SetFloat("_ImportedSentinel",.375f);
            originalBlock.SetColor("_BaseColor",new Color(.7f,.8f,.65f,.61f));leaves.SetPropertyBlock(originalBlock);
            var slotZero=new MaterialPropertyBlock();slotZero.SetFloat("_SlotSentinel",.625f);
            slotZero.SetColor("_BaseColor",new Color(.6f,.75f,.58f,.42f));leaves.SetPropertyBlock(slotZero,0);
            var slotOne=new MaterialPropertyBlock();slotOne.SetColor("_Color",new Color(.7f,.76f,.65f,.53f));leaves.SetPropertyBlock(slotOne,1);
            adapter.foliage=new[]{skinned,leaves,hidden};adapter.snowCaps=new[]{snowB,snowA};adapter.buds=new[]{bud};
            adapter.Bind(seasons);seasons.PresentDay(120.5);
            Check(seasons.foliage.SequenceEqual(adapter.foliage)&&seasons.snowCaps.SequenceEqual(adapter.snowCaps)&&seasons.buds.SequenceEqual(adapter.buds),
                "explicit semantic bindings accept arbitrary names and different part counts");
            Check(skinned is SkinnedMeshRenderer&&leaves is MeshRenderer&&leaves.enabled&&skinned.enabled&&!hidden.enabled,
                "static and skinned leaves both grow while an author-disabled variant stays hidden");
            var block=new MaterialPropertyBlock();leaves.GetPropertyBlock(block,0);Color summer=block.GetColor("_BaseColor");
            Check(summer.g>summer.r&&Mathf.Abs(summer.a-.42f)<.0001f&&block.GetFloat("_SlotSentinel")==.625f,
                "Lit foliage uses its original slot tint and preserves transparency and unrelated overrides");
            Check(!snowA.enabled&&!snowB.enabled&&!bud.enabled,"optional snow and buds disappear during summer");

            seasons.PresentDay(220.5);leaves.GetPropertyBlock(block,0);Color autumn=block.GetColor("_BaseColor");
            Check(leaves.enabled&&skinned.enabled&&autumn.r>autumn.g&&autumn!=summer,
                "imported Lit foliage changes from summer green to autumn color");
            leaves.GetPropertyBlock(block,1);Color legacyAutumn=block.GetColor("_Color");
            Check(legacyAutumn.r>legacyAutumn.g&&Mathf.Abs(legacyAutumn.a-.53f)<.0001f,
                "a second material slot using legacy color also changes season without losing alpha");
            skinned.GetPropertyBlock(block,0);
            Check(block.GetColor("_BaseColor").r>block.GetColor("_BaseColor").g,"skinned imported foliage receives the same seasonal treatment");

            seasons.PresentDay(305.5);
            Check(!leaves.enabled&&!skinned.enabled&&!hidden.enabled&&snowA.enabled&&snowB.enabled,
                "winter hides ordinary leaf meshes and enables every assigned snow part");
            Check(leaves.sharedMaterials[0]==lit&&leaves.sharedMaterials[1]==legacy&&skinned.sharedMaterials.All(m=>m==lit)&&
                EditorJsonUtility.ToJson(lit)==litOriginal&&EditorJsonUtility.ToJson(legacy)==legacyOriginal,
                "seasonal changes neither clone nor mutate shared imported materials");

            Disable(seasons);
            leaves.GetPropertyBlock(block);
            Check(leaves.enabled&&skinned.enabled&&!hidden.enabled&&!snowA.enabled&&!snowB.enabled&&!bud.enabled&&
                block.GetFloat("_ImportedSentinel")==.375f&&block.GetColor("_BaseColor")==originalBlock.GetColor("_BaseColor"),
                "disabling restores original renderer visibility and renderer-wide material overrides");
            leaves.GetPropertyBlock(block,0);
            Check(block.GetFloat("_SlotSentinel")==.625f&&block.GetColor("_BaseColor")==slotZero.GetColor("_BaseColor"),
                "disabling restores the original first material slot exactly");
            leaves.GetPropertyBlock(block,1);
            Check(block.GetColor("_Color")==slotOne.GetColor("_Color"),"disabling restores the original second material slot");
            skinned.GetPropertyBlock(block,0);Check(block.isEmpty,"disabling removes overrides from an originally empty material slot");

            seasons.enabled=true;seasons.PresentDay(120.5);
            Check(leaves.enabled&&skinned.enabled&&!hidden.enabled,"re-enabling regrows leaves without activating author-hidden variants");
            leaves.GetPropertyBlock(block,0);
            Check(block.GetColor("_BaseColor")==summer,"re-enabling does not compound seasonal tint");
            adapter.foliage=new[]{skinned};adapter.snowCaps=Array.Empty<Renderer>();adapter.buds=null;
            adapter.Bind(seasons);seasons.PresentDay(305.5);
            leaves.GetPropertyBlock(block,0);
            Check(seasons.foliage.Length==1&&!skinned.enabled&&leaves.enabled&&
                block.GetColor("_BaseColor")==slotZero.GetColor("_BaseColor")&&!hidden.enabled,
                "rebinding a one-part model releases old parts and restores their authored presentation");
            seasons.PresentDay(120.5);Check(skinned.enabled,"a single imported foliage renderer supports winter-to-summer recovery");
            adapter.foliage=Array.Empty<Renderer>();adapter.snowCaps=null;adapter.buds=Array.Empty<Renderer>();adapter.Bind(seasons);
            seasons.PresentDay(305.5);seasons.PresentDay(30.5);
            Check(seasons.foliage.Length==0&&seasons.snowCaps.Length==0&&seasons.buds.Length==0&&skinned.enabled,
                "models with no seasonal parts and null optional groups are valid");
            Check(model.transform.localPosition==new Vector3(2,3,4)&&Quaternion.Angle(model.transform.localRotation,Quaternion.Euler(8,39,2))<.001f&&
                model.transform.localScale==Vector3.one*1.7f,"binding leaves imported model placement, rotation and scale unchanged");
            Check(EditorJsonUtility.ToJson(lit)==litOriginal&&EditorJsonUtility.ToJson(legacy)==legacyOriginal,
                "shared materials remain unchanged after rebinding and removal");
            report.AppendLine("PASS imported tree model adaptation ("+checks+" assertions)");
            return report.ToString();
        }
        finally
        {
            if(seasons)Disable(seasons);
            EditorSceneManager.ClosePreviewScene(scene);
            foreach(var value in temporary)if(value)UnityEngine.Object.DestroyImmediate(value);
        }
    }

    static void Disable(HusbandrySeasons seasons)
    {
        seasons.enabled=false;
        // Preview-scene MonoBehaviours do not run player lifecycle callbacks. Exercise the same
        // cleanup boundary explicitly, including idempotency if an Editor invokes it already.
        typeof(HusbandrySeasons).GetMethod("OnDisable",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(seasons,null);
    }
}
