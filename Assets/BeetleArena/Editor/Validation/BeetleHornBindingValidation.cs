using System;
using System.IO;
using System.Linq;
using System.Text;
using BeetleArena;
using BeetleArena.Campaign;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class BeetleHornBindingValidation
{
    const string Output = "Artifacts/HornBinding";
    const string TemporaryScene = "Assets/BeetleArena/Generated/HornBindingInspection.unity";

    [MenuItem("Tools/Beetle Arena/Validate Horn Binding")]
    public static void Run()
    {
        Directory.CreateDirectory(Output);
        var report = new StringBuilder();
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BeetleArena/Generated/ArticulatedBeetle.prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            var anim = go.GetComponent<BeetleProceduralAnimator>();
            var horns = go.GetComponent<BeetleHornRig>();
            Require(horns && horns.cephalicSurface && horns.thoracicSurface, "explicit horn surfaces");
            Require(horns.lengthPivot.parent == anim.headPivot && horns.thoracicSurface.transform.parent == anim.bodyPivot,
                "head horn / thoracic horn parent separation");
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/bug.fbx");
            foreach (int id in new[] { 5, 3 })
            {
                var original = source.GetComponentsInChildren<MeshFilter>().Single(x => x.name == "tripo_part_" + id).sharedMesh;
                var converted = BeetleImportedModelBuilder.ConvertSourceMesh(original, id);
                try
                {
                    var shell = anim.model.GetComponentsInChildren<MeshFilter>().Single(x => x.name == "tripo_part_" + id).sharedMesh;
                    var horn = (id == 5 ? horns.cephalicSurface : horns.thoracicSurface).GetComponent<MeshFilter>().sharedMesh;
                    double error = Math.Abs(Area(converted) - Area(shell) - Area(horn));
                    Require(error < Area(converted) * 1e-5, "complete source surface for part " + id);
                    Require(shell.uv.Length == shell.vertexCount && horn.uv.Length == horn.vertexCount, "UV retention");
                    report.AppendLine($"PASS part {id}: source area preserved, error={error:E3}; shell={shell.triangles.Length / 3}, horn={horn.triangles.Length / 3} triangles");
                }
                finally { Object.DestroyImmediate(converted); }
            }
            var settings = AssetDatabase.LoadAssetAtPath<BeetleMatchSettings>(BeetleArenaSceneBuilder.AssetFolder + "/MatchSettings.asset");
            var traits = BeetleTraits.Create(1, new System.Random(17), settings);
            traits.hornLengthFactor = 1;
            anim.Initialize(traits, settings, Color.white);
            var fixedParts = anim.model.GetComponentsInChildren<MeshFilter>().Where(x =>
                x.name == "tripo_part_5" || x.name == "tripo_part_15" || x.name == "tripo_part_20" ||
                x.name.StartsWith("Mouth from part")).ToArray();
            var baseline = fixedParts.Select(Points).ToArray();
            var antennaSockets = anim.antennae.Select(x => x.position).ToArray();
            var hornFilter = horns.cephalicSurface.GetComponent<MeshFilter>();
            var originalHorn = Points(hornFilter);
            var originalThoracic = Points(horns.thoracicSurface.GetComponent<MeshFilter>());
            var seam = Enumerable.Range(0, hornFilter.sharedMesh.vertexCount)
                .Where(i => Mathf.Abs(hornFilter.sharedMesh.vertices[i].z - BeetleHornBindingUpgrade.CephalicRootZ) < 1e-5).ToArray();
            Require(seam.Length > 0, "cephalic root seam exists");
            foreach (float factor in new[] { .84f, 1.2f, 1f, .84f, 1f })
            {
                traits.hornLengthFactor = factor;
                anim.Initialize(traits, settings, Color.white);
                Require(anim.headPivot.localScale == Vector3.one, "head is not stretched");
                float skullError = 0, seamError = 0, tipMotion = 0;
                for (int i = 0; i < fixedParts.Length; i++) skullError = Mathf.Max(skullError, MaxError(baseline[i], Points(fixedParts[i])));
                Require(skullError < 1e-5 && MaxError(antennaSockets, anim.antennae.Select(x => x.position).ToArray()) < 1e-5,
                    "skull/eyes/mouth/antenna sockets stay fixed");
                var current = Points(hornFilter);
                foreach (int i in seam) seamError = Mathf.Max(seamError, Vector3.Distance(originalHorn[i], current[i]));
                tipMotion = MaxError(originalHorn, current);
                Require(seamError < 1e-5, "horn root remains joined");
                Require(factor == 1 ? tipMotion < 1e-5 : tipMotion > .05f, "isolated horn length and reset");
                Require(MaxError(originalThoracic, Points(horns.thoracicSurface.GetComponent<MeshFilter>())) < 1e-5,
                    "cephalic length does not move thoracic horn");
                foreach (var c in horns.contacts)
                {
                    var half = Vector3.up * (c.shape.height * .5f - c.shape.radius);
                    var a = anim.headPivot.InverseTransformPoint(c.shape.transform.TransformPoint(-half));
                    var b = anim.headPivot.InverseTransformPoint(c.shape.transform.TransformPoint(half));
                    var expectedA = c.a; var expectedB = c.b;
                    float z = horns.lengthPivot.localPosition.z;
                    expectedA.z += Mathf.Max(0, expectedA.z - z) * (factor - 1);
                    expectedB.z += Mathf.Max(0, expectedB.z - z) * (factor - 1);
                    Require(Vector3.Distance(a, expectedA) < 2e-5 && Vector3.Distance(b, expectedB) < 2e-5,
                        "capsule endpoints follow horn length");
                }
                report.AppendLine($"PASS length={factor:F2}: fixed anatomy error={skullError:E3}, root seam error={seamError:E3}, horn displacement={tipMotion:F5}; 7 contact segments aligned");
            }
            foreach (float pitch in new[] { -30f, 35f })
            {
                var before = Points(hornFilter);
                var oldMatrix = anim.headPivot.worldToLocalMatrix;
                anim.headPivot.localRotation = Quaternion.Euler(pitch, 12, 0);
                var expected = before.Select(p => anim.headPivot.TransformPoint(oldMatrix.MultiplyPoint3x4(p))).ToArray();
                Require(MaxError(expected, Points(hornFilter)) < 1e-5, "horn follows head rigidly");
                Require(MaxError(originalThoracic, Points(horns.thoracicSurface.GetComponent<MeshFilter>())) < 1e-5,
                    "thoracic horn does not follow neck motion");
                anim.headPivot.localRotation = Quaternion.identity;
            }
            report.AppendLine("PASS head pitch/yaw drives only cephalic horn; thoracic horn stays on thorax.");
            report.AppendLine("COMPLETE PASS");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); File.WriteAllText(Output + "/Validation.txt", report.ToString()); }
    }

    static Vector3[] Points(MeshFilter filter) => filter.sharedMesh.vertices.Select(filter.transform.TransformPoint).ToArray();
    static float MaxError(Vector3[] a, Vector3[] b) => a.Zip(b, Vector3.Distance).DefaultIfEmpty(0).Max();
    static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    static double Area(Mesh mesh)
    {
        var p = mesh.vertices; var t = mesh.triangles; double area = 0;
        for (int i = 0; i < t.Length; i += 3) area += Vector3.Cross(p[t[i + 1]] - p[t[i]], p[t[i + 2]] - p[t[i]]).magnitude * .5;
        return area;
    }

    [MenuItem("Tools/Beetle Arena/Inspect Horn Binding")]
    public static void Inspect()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before inspecting bindings.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save the open scene first.");
        Directory.CreateDirectory(Output);
        var setup = EditorSceneManager.GetSceneManagerSetup();
        var report = new StringBuilder();
        var materials = new System.Collections.Generic.List<Material>();
        try
        {
            var original = EditorSceneManager.OpenScene(CampaignIridescenceSetup.ScenePath, OpenSceneMode.Single);
            if (!EditorSceneManager.SaveScene(original, TemporaryScene, true)) throw new IOException("Cannot copy menu.");
            EditorSceneManager.OpenScene(TemporaryScene, OpenSceneMode.Single);
            var rig = Object.FindFirstObjectByType<CampaignSpecimen>().rig;
            var renderers = rig.model.GetComponentsInChildren<Renderer>();
            foreach (var r in renderers)
            {
                var filter = r.GetComponent<MeshFilter>();
                if (!filter) continue;
                var points = filter.sharedMesh.vertices.Select(v => rig.model.InverseTransformPoint(r.transform.TransformPoint(v))).ToArray();
                var bounds = new Bounds(points[0], Vector3.zero);
                foreach (var p in points) bounds.Encapsulate(p);
                report.AppendLine($"{r.name}: parent={r.transform.parent.name}, min={bounds.min:F4}, max={bounds.max:F4}, material={r.sharedMaterial.name}");
                File.WriteAllText(Output + "/" + r.name + ".csv", "x,y,z\n" +
                    string.Join("\n", points.Select(p => FormattableString.Invariant($"{p.x},{p.y},{p.z}"))));
            }
            report.AppendLine($"Head pivot in model={rig.model.InverseTransformPoint(rig.headPivot.position):F4}; scale={rig.headPivot.localScale}");
            foreach (var c in rig.GetComponent<BeetleContactRig>().shapes.Where(c => c.name.Contains("horn") || c.name.Contains("Horn")))
                report.AppendLine($"{c.name}: parent={c.transform.parent.name}, position={rig.model.InverseTransformPoint(c.transform.position):F4}");
            var camera = Object.FindFirstObjectByType<CampaignBackdrop>().sceneCamera;
            var focus = rig.model.TransformPoint(new Vector3(0, 1, 1.3f));
            float scale = rig.model.lossyScale.x;
            camera.transform.position = focus + rig.model.TransformDirection(new Vector3(5, 2.5f, 3)) * scale;
            camera.transform.LookAt(focus, rig.model.up);
            camera.orthographic = true; camera.orthographicSize = 1.8f * scale;
            camera.nearClipPlane = .01f;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Except(renderers)) r.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.13f, .15f, .18f);
            Capture(camera, "Materials.png");
            int[] ids = { 3, 5, 13, 21, 22 };
            Color[] colors = { Color.yellow, Color.red, Color.cyan, Color.green, Color.magenta };
            foreach (var r in renderers)
            {
                int index = Array.IndexOf(ids, int.TryParse(r.name.Replace("tripo_part_", ""), out int id) ? id : -1);
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.SetColor("_BaseColor", r.name == BeetleHornBindingUpgrade.CephalicName ? new Color(1, .25f, .05f) :
                    r.name == BeetleHornBindingUpgrade.ThoracicName ? new Color(1, .5f, .05f) :
                    index >= 0 ? colors[index] : new Color(.3f, .3f, .3f));
                materials.Add(material); r.sharedMaterial = material;
            }
            Capture(camera, "Parts.png");
            foreach (int id in ids)
            {
                foreach (var r in renderers) r.enabled = r.name == "tripo_part_" + id;
                Capture(camera, "Part" + id + ".png");
            }
            foreach (var r in renderers) r.enabled = true;
            rig.headPivot.localRotation = Quaternion.Euler(-30, 0, 0);
            Capture(camera, "HeadRaised.png");
            report.AppendLine("Inspection complete. Red=5, yellow=3, cyan=13, green=21, magenta=22.");
        }
        finally
        {
            File.WriteAllText(Output + "/Inspection.txt", report.ToString());
            EditorSceneManager.RestoreSceneManagerSetup(setup);
            AssetDatabase.DeleteAsset(TemporaryScene);
            foreach (var material in materials) Object.DestroyImmediate(material);
        }
    }

    static void Capture(Camera camera, string name)
    {
        var rt = new RenderTexture(1200, 900, 24);
        var image = new Texture2D(1200, 900, TextureFormat.RGB24, false);
        var previousTarget = camera.targetTexture;
        var previousActive = RenderTexture.active;
        try
        {
            camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); image.Apply();
            File.WriteAllBytes(Output + "/" + name, image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
            Object.DestroyImmediate(image); Object.DestroyImmediate(rt);
        }
    }
}
