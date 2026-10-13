using UnityEditor;

/// <summary>兼容原验证菜单；模型已真实切分，采用包含蒙皮运动与接缝检查的新验证。</summary>
public static class BeetleFeelerBindingValidation
{
    [MenuItem("Tools/Beetle Arena/Validate Feeler Binding")]
    public static void Run() => BeetleHeadSplitValidation.Run();
}
