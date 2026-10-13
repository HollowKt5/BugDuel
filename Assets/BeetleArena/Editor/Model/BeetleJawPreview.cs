using BeetleArena;
using UnityEditor;
using UnityEngine;

/// <summary>组件右键菜单的编辑器姿态验收；记录 Undo，可还原，不启动任何进食行为。</summary>
public static class BeetleJawPreview
{
    [MenuItem("CONTEXT/BeetleJawRig/Preview Open Jaw")]
    static void Open(MenuCommand command) => Pose(command,1);
    [MenuItem("CONTEXT/BeetleJawRig/Restore Closed Jaw")]
    static void Close(MenuCommand command) => Pose(command,0);
    static void Pose(MenuCommand command,float opening)
    {
        var rig=(BeetleJawRig)command.context;
        if(EditorApplication.isPlayingOrWillChangePlaymode || !rig.left || !rig.right)return;
        Undo.RecordObjects(new Object[]{rig.left,rig.right},"Preview jaw pose");
        rig.SetPose(opening);
        foreach(var pivot in new[]{rig.left,rig.right})
        {
            EditorUtility.SetDirty(pivot);
            if(PrefabUtility.IsPartOfPrefabInstance(pivot))PrefabUtility.RecordPrefabInstancePropertyModifications(pivot);
        }
        SceneView.RepaintAll();
    }
}
