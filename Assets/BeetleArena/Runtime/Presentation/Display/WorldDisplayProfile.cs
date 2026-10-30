using System;
using System.Collections.Generic;
using UnityEngine;

namespace BeetleArena
{
    [Serializable]
    public sealed class WorldToonSettings
    {
        [InspectorName("启用卡通叠加")] public bool enabled=true;
        [InspectorName("叠加强度"),Range(0,1)] public float strength=.65f;
        [InspectorName("明暗层数"),Range(3,8)] public int bands=5;
        [InspectorName("分层柔和度"),Range(.02f,.45f)] public float softness=.16f;
        [InspectorName("暗部提亮"),Range(0,.3f)] public float shadowLift=.08f;
        [InspectorName("纹理细节保留"),Range(0,1)] public float detailRetention=.65f;
        [InspectorName("描边强度"),Range(0,1)] public float outlineStrength=.25f;
        [InspectorName("描边宽度（像素）"),Range(.5f,3)] public float outlineWidth=1;
        [InspectorName("边缘阈值"),Range(.02f,.8f)] public float edgeThreshold=.18f;
        [InspectorName("描边颜色")] public Color outlineColor=new Color(.055f,.065f,.05f,1);
        public bool Active=>enabled&&strength>0;
    }

    [Serializable]
    public sealed class SceneDisplaySetting
    {
        public string sceneName;
        public BeetleDisplayStyle style=BeetleDisplayStyle.OrderedDither;
        [Range(1,6)] public int pixelSize=2;
        public Material ditherMaterial;
        public Material denseDitherMaterial;
        public Material customMaterial;
        public Material DitherMaterial(BeetleDisplayStyle selected)=>selected==BeetleDisplayStyle.DenseDither?denseDitherMaterial:ditherMaterial;
    }

    // Resources keeps authored screen shaders/materials referenced in player builds.
    [CreateAssetMenu(menuName="Beetle Arena/World Display Profile")]
    public sealed class WorldDisplayProfile:ScriptableObject
    {
        public const string ResourceName="WorldDisplay";
        [Header("所有场景共用 · 卡通叠加")]
        public WorldToonSettings toon=new WorldToonSettings();
        [Header("各场景后续滤镜")]
        public List<SceneDisplaySetting> scenes=new List<SceneDisplaySetting>();
        public SceneDisplaySetting Find(string sceneName)=>scenes.Find(entry=>entry.sceneName==sceneName);
    }
}
