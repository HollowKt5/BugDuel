using UnityEngine;

namespace BeetleArena
{
    /// <summary>Optional replacement for the generated tree, positioned relative to WindowTreeExterior.</summary>
    [CreateAssetMenu(fileName="HusbandryTreeProfile",menuName="BugDuel/Presentation/Husbandry Tree Profile")]
    public sealed class HusbandryTreeProfile:ScriptableObject
    {
        [Tooltip("留空保留当前程序巨树。替换模型应保存为Prefab，并在根节点添加HusbandryTreeModelAdapter映射树叶、积雪和芽；不会替换模型自带材质。")]
        public GameObject treePrefab;
        [Tooltip("相对于WindowTreeExterior的局部位置。建议Prefab原点位于树根；默认参考点在窗外左下方，按模型尺寸调整。")]
        public Vector3 localPosition=new Vector3(-5.8f,-10.5f,12);
        [Tooltip("相对于WindowTreeExterior的局部欧拉角（度）。")]
        public Vector3 localEulerAngles;
        [Tooltip("模型实例的局部缩放，三个分量必须为正；配置不会修改源Prefab。")]
        public Vector3 localScale=Vector3.one;
    }
}
