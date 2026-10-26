using System;
using System.Linq;
using BeetleArena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>Cleans up editor hierarchy; unit parent preserves world poses, object references, active states, and skeletal hierarchy.</summary>
public static class BeetleSceneHierarchy
{
    public static BeetleMatchController FindMatch(Scene scene) => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<BeetleMatchController>(true)).Single();

    [MenuItem("Tools/甲壳虫斗/场景与安装/整理场景层级", false, 205)]
    public static void OrganizeCurrent()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before organizing the scene.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != BeetleArenaSceneBuilder.ScenePath)
            throw new InvalidOperationException("Open BeetleArena.unity before organizing.");
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Organize Beetle Arena Hierarchy");
        Organize(FindMatch(scene));
        EditorSceneManager.MarkSceneDirty(scene);
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.SaveScene(scene);
    }

    public static void Organize(BeetleMatchController match)
    {
        var scene = match.gameObject.scene;
        // Verifies pre-organization snapshot against all existing GameObjects, ensuring inactive arenas, UI, or models never drift.
        var before = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
        var matrices = before.Select(t => t.localToWorldMatrix).ToArray();
        var active = before.Select(t => t.gameObject.activeSelf).ToArray();
        var systems = Folder(scene, null, "00 Systems", 0);
        var fighters = Folder(scene, null, "10 Fighters", 1);
        var arenas = Folder(scene, null, "20 Arenas", 2);
        var environment = Folder(scene, null, "30 Environment", 3);
        var presentation = Folder(scene, null, "40 Camera and Lighting", 4);
        var ui = Folder(scene, null, "50 UI", 5);
        var stones = Folder(scene, environment, "Forest Stones", 1);
        Move(match.transform, systems);
        if (match.audioFeedback) Move(match.audioFeedback.transform, systems);
        foreach (var actor in match.actors) if (actor) Move(actor.transform, fighters);
        if (match.spectatorCamera) Move(match.spectatorCamera.transform, presentation);
        if (match.hud) Move(match.hud.transform, ui);
        foreach (var t in before)
        {
            if (t.GetComponent<EventSystem>()) Move(t, ui);
            else if (t.GetComponent<Light>()) Move(t, presentation);
            else if (t.name == "Forest ground") { Move(t, environment); t.SetSiblingIndex(0); }
            else if (t.name.StartsWith("Forest stone ", StringComparison.Ordinal)) Move(t, stones);
        }
        foreach (var arena in match.arenas)
        {
            if (!arena) continue;
            Move(arena.transform, arenas);
            // Groups known stump decorations only. Rock arena retains MeshCollider/Renderer on original GameObjects.
            var parts = Enumerable.Range(0, arena.transform.childCount).Select(i => arena.transform.GetChild(i)).ToArray();
            if (!parts.Any(t => t.name == "Stump - ring-out surface")) continue;
            var surface = Folder(scene, arena.transform, "00 Surface and Collision", 0);
            var rings = Folder(scene, arena.transform, "10 Growth Rings", 1);
            var bark = Folder(scene, arena.transform, "20 Bark Ridges", 2);
            var rim = Folder(scene, arena.transform, "30 Rim Marks", 3);
            foreach (var t in parts)
            {
                if (t.name == "Stump - ring-out surface" || t.name == "Wood surface") Move(t, surface);
                else if (t.name.StartsWith("Annual growth ring ", StringComparison.Ordinal)) Move(t, rings);
                else if (t.name.StartsWith("Bark ridge ", StringComparison.Ordinal)) Move(t, bark);
                else if (t.name.StartsWith("Flush rim tick ", StringComparison.Ordinal)) Move(t, rim);
            }
        }
        for (int i = 0; i < before.Length; i++)
        {
            if (before[i].gameObject.activeSelf != active[i]) throw new Exception("Hierarchy changed active state: " + before[i].name);
            var after = before[i].localToWorldMatrix;
            for (int k = 0; k < 16; k++)
                if (Mathf.Abs(matrices[i][k] - after[k]) > .0001f)
                    throw new Exception("Hierarchy changed world pose: " + before[i].name);
        }
        EditorSceneManager.MarkSceneDirty(scene);
    }

    static Transform Folder(Scene scene, Transform parent, string name, int order)
    {
        var children = parent ? Enumerable.Range(0, parent.childCount).Select(i => parent.GetChild(i)) :
            scene.GetRootGameObjects().Select(g => g.transform);
        var result = children.SingleOrDefault(t => t.name == name);
        if (!result)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            Undo.RegisterCreatedObjectUndo(go, "Create hierarchy group");
            result = go.transform;
            if (parent) result.SetParent(parent, false);
        }
        // Grouping adds no extra scale/rotation layers; existing non-unit objects with identical names are not silently reset.
        if (result.localPosition.sqrMagnitude > .000001f || Quaternion.Angle(result.localRotation, Quaternion.identity) > .001f ||
            (result.localScale - Vector3.one).sqrMagnitude > .000001f || !result.gameObject.activeSelf)
            throw new InvalidOperationException("Hierarchy folder must be active with identity transform: " + name);
        result.SetSiblingIndex(order);
        return result;
    }
    static void Move(Transform target, Transform parent)
    {
        if (target.parent == parent) return;
        // Undo.SetTransformParent preserves world position/rotation/scale and supports Editor undo.
        Undo.SetTransformParent(target, parent, "Group scene object");
    }
}
