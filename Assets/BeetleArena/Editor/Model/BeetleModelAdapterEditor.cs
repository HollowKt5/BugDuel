using System;
using BeetleArena;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

[CustomEditor(typeof(BeetleModelAdapter))]
public sealed class BeetleModelAdapterEditor : Editor
{
    public override VisualElement CreateInspectorGUI()
    {
        var root = new VisualElement();
        root.Add(new HelpBox("每个模型保存自己的绑定。六个支撑腿按语义槽位识别，数组顺序不限；额外骨骼、翅鞘、后翅和口器使用 Beetle Appendage Rig。", HelpBoxMessageType.Info));
        InspectorElement.FillDefaultInspector(root, serializedObject, this);
        var result = new HelpBox("先调整为静止姿态，再记录并绑定。", HelpBoxMessageType.Info);
        root.Add(new Button(() =>
        {
            if (EditorApplication.isPlaying || AnimationMode.InAnimationMode())
            { result.text = "请先停止运行和动画预览。"; return; }
            serializedObject.ApplyModifiedProperties();
            var adapter = (BeetleModelAdapter)target;
            if (!adapter.Validate(out var error, false)) { result.text = error; return; }
            Undo.RecordObjects(new UnityEngine.Object[] { adapter, adapter.GetComponent<BeetleModelRig>(),
                adapter.GetComponent<BeetleProceduralAnimator>() }, "Bind beetle model");
            foreach (var node in adapter.GetComponentsInChildren<Transform>(true)) Undo.RecordObject(node, "Bind beetle model");
            adapter.CaptureRestPose(); adapter.ApplyBindings();
            foreach (var component in new Component[] { adapter, adapter.GetComponent<BeetleModelRig>(), adapter.GetComponent<BeetleProceduralAnimator>() })
            { EditorUtility.SetDirty(component); PrefabUtility.RecordPrefabInstancePropertyModifications(component); }
            serializedObject.Update(); result.text = "已记录静止姿态并绑定。请把配置保存到此模型的 Prefab。";
        }) { text = "记录当前静止姿态并绑定" });
        root.Add(new Button(() =>
        {
            serializedObject.ApplyModifiedProperties();
            result.text = ((BeetleModelAdapter)target).Validate(out var error) ? "模型绑定校验通过。" : error;
        }) { text = "校验模型绑定" });
        root.Add(result);
        return root;
    }
}

[CustomEditor(typeof(BeetleAppendageRig))]
public sealed class BeetleAppendageRigEditor : Editor
{
    BeetleAppendagePreview preview;
    BeetleAppendagePose pose;
    bool animate;
    double previous;
    VisualElement configuration;
    HelpBox result;

    void OnEnable()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += PlayChanged;
        AssemblyReloadEvents.beforeAssemblyReload += Stop;
        Undo.undoRedoPerformed += Stop;
    }

    void OnDisable()
    {
        Stop(); EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayChanged;
        AssemblyReloadEvents.beforeAssemblyReload -= Stop;
        Undo.undoRedoPerformed -= Stop;
    }

    void PlayChanged(PlayModeStateChange _) => Stop();

    public override VisualElement CreateInspectorGUI()
    {
        var root = new VisualElement();
        root.Add(new HelpBox("每个关节只填写一次，可叠加多个动作通道。轴向使用关节静止姿态的局部坐标，负角度可做左右镜像。预览结束自动还原。", HelpBoxMessageType.Info));
        configuration = new VisualElement(); root.Add(configuration);
        InspectorElement.FillDefaultInspector(configuration, serializedObject, this);
        configuration.Add(new Button(() =>
        {
            if (EditorApplication.isPlaying || AnimationMode.InAnimationMode()) { result.text = "请先停止运行和动画预览。"; return; }
            serializedObject.ApplyModifiedProperties();
            var rig = (BeetleAppendageRig)target;
            if (!rig.Validate(out var error, false)) { result.text = error; return; }
            Undo.RecordObject(rig, "Capture beetle appendage rest pose"); rig.CaptureRestPose();
            EditorUtility.SetDirty(rig); PrefabUtility.RecordPrefabInstancePropertyModifications(rig);
            serializedObject.Update(); result.text = "已记录当前闭合姿态。";
        }) { text = "记录当前闭合姿态" });
        configuration.Add(new Button(() =>
        {
            serializedObject.ApplyModifiedProperties();
            result.text = ((BeetleAppendageRig)target).Validate(out var error) ? "附属关节绑定校验通过。" : error;
        }) { text = "校验附属关节" });
        root.Add(new Button(() =>
        {
            Stop(); serializedObject.ApplyModifiedProperties();
            try
            {
                preview = new BeetleAppendagePreview((BeetleAppendageRig)target);
                configuration.SetEnabled(false); previous = EditorApplication.timeSinceStartup;
                preview.Sample(pose); result.text = "正在 Scene 视图预览；调整下面的滑块。";
            }
            catch (Exception ex) { result.text = ex.Message; }
        }) { text = "开始关节预览" });
        AddSlider(root, "展翅进度（先开鞘再展后翅）", 0, 1, v => pose.extension = v);
        AddSlider(root, "振翅幅度", 0, 1, v => pose.flapStrength = v);
        AddSlider(root, "振翅相位", 0, 1, v => pose.flapPhase = v);
        AddSlider(root, "口器开合", 0, 1, v => pose.jawOpen = v);
        AddSlider(root, "口器横摆", -1, 1, v => pose.jawSweep = v);
        AddSlider(root, "触角俯仰", -1, 1, v => pose.antennaPitch = v);
        AddSlider(root, "触角横摆", -1, 1, v => pose.antennaYaw = v);
        AddSlider(root, "自定义动作", -1, 1, v => pose.custom = v);
        var toggle = new Toggle("慢速循环振翅（2 Hz）"); toggle.RegisterValueChangedCallback(e => animate = e.newValue); root.Add(toggle);
        root.Add(new Button(Stop) { text = "停止预览并还原" });
        result = new HelpBox("预览只影响当前关节姿态，不改静止绑定或源模型。", HelpBoxMessageType.Info); root.Add(result);
        return root;
    }

    void AddSlider(VisualElement root, string label, float min, float max, Action<float> set)
    {
        var slider = new Slider(label, min, max);
        slider.RegisterValueChangedCallback(e => { set(e.newValue); preview?.Sample(pose); }); root.Add(slider);
    }

    void Tick()
    {
        if (preview == null) return;
        if (!target || !AnimationMode.InAnimationMode()) { Stop(); return; }
        double now = EditorApplication.timeSinceStartup;
        if (animate) { pose.flapPhase = Mathf.Repeat(pose.flapPhase + (float)(now - previous) * 2, 1); preview.Sample(pose); }
        previous = now;
    }

    void Stop()
    {
        preview?.Dispose(); preview = null; configuration?.SetEnabled(true);
        if (result != null) result.text = "预览已停止，关节姿态已还原。";
    }
}
