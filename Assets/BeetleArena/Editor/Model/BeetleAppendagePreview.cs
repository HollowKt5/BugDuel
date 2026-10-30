using System;
using System.Globalization;
using BeetleArena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class BeetleAppendagePreview : IDisposable
{
    readonly BeetleAppendageRig rig;
    readonly PropertyModification[][] originals;
    readonly EditorCurveBinding[][] bindings;
    bool active;

    public BeetleAppendagePreview(BeetleAppendageRig rig)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || AnimationMode.InAnimationMode())
            throw new InvalidOperationException("Stop Play and other animation previews first.");
        if (EditorUtility.IsPersistent(rig)) throw new InvalidOperationException("Open the model in Prefab Mode or select a scene instance to preview.");
        if (!rig.Validate(out var error)) throw new InvalidOperationException(error);
        this.rig = rig;
        int count = rig.joints?.Length ?? 0;
        originals = new PropertyModification[count][]; bindings = new EditorCurveBinding[count][];
        for (int i = 0; i < count; i++)
        {
            originals[i] = new PropertyModification[4]; bindings[i] = new EditorCurveBinding[4];
            var pivot = rig.joints[i].pivot; var rotation = pivot.localRotation;
            for (int axis = 0; axis < 4; axis++)
            {
                string property = "m_LocalRotation." + "xyzw"[axis];
                originals[i][axis] = new PropertyModification { target = pivot, propertyPath = property,
                    value = rotation[axis].ToString("R", CultureInfo.InvariantCulture) };
                bindings[i][axis] = EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(pivot, rig.transform), typeof(Transform), property);
            }
        }
        AnimationMode.StartAnimationMode(); active = true;
        EditorSceneManager.sceneSaving += SceneSaving;
    }

    public void Sample(BeetleAppendagePose pose)
    {
        if (!active || !rig || !AnimationMode.InAnimationMode()) return;
        AnimationMode.BeginSampling();
        try
        {
            for (int i = 0; i < originals.Length; i++)
                for (int axis = 0; axis < 4; axis++)
                    AnimationMode.AddPropertyModification(bindings[i][axis], originals[i][axis], true);
            rig.Apply(pose);
        }
        finally { AnimationMode.EndSampling(); }
        SceneView.RepaintAll();
    }

    void SceneSaving(Scene scene, string path) => Dispose();

    public void Dispose()
    {
        EditorSceneManager.sceneSaving -= SceneSaving;
        if (!active) return;
        active = false;
        if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
        SceneView.RepaintAll();
    }
}
