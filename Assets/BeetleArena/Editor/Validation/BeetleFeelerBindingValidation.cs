using UnityEditor;

/// <summary>Backwards-compatible menu entry; model is physically partitioned, using upgraded validation with skinning and seam checks.</summary>
public static class BeetleFeelerBindingValidation
{
    [MenuItem("Tools/甲壳虫斗/验证/模型与动作/触角绑定", false, 700)]
    public static void Run() => BeetleHeadSplitValidation.Run();
}
