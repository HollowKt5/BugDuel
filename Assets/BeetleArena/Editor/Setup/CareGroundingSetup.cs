using System;
using BeetleArena;
using UnityEditor;
using UnityEngine;

public static class CareGroundingSetup
{
    [MenuItem("Tools/甲壳虫斗/画面与资源/养殖表现/生成甲壳接地资源", false, 321)]
    public static void Apply()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before preparing model geometry.");
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BeetleArena/Generated/ArticulatedBeetle.prefab");
        var rig=prefab.GetComponentInChildren<BeetleProceduralAnimator>();
        // Only the three small rigid carapace meshes need a CPU copy; leg render meshes stay unchanged.
        foreach(var renderer in rig.shellRenderers)
        {
            var filter=renderer.GetComponent<MeshFilter>();if(!filter||!filter.sharedMesh)continue;
            var mesh=filter.sharedMesh;if(mesh.isReadable)continue;
            var serialized=new SerializedObject(mesh);var readable=serialized.FindProperty("m_IsReadable");
            if(readable==null)throw new InvalidOperationException("Mesh read access is unavailable: "+mesh.name);
            readable.boolValue=true;serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssetIfDirty(mesh);AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(mesh),ImportAssetOptions.ForceUpdate);
        }
        Debug.Log("Husbandry shell contact geometry ready.");
    }
}
