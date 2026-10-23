using System;
using System.Collections.Generic;
using UnityEngine;

namespace BeetleArena
{
    [Serializable]
    public sealed class SceneDisplaySetting
    {
        public string sceneName;
        public BeetleDisplayStyle style=BeetleDisplayStyle.OrderedDither;
        [Range(1,6)] public int pixelSize=3;
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
        public List<SceneDisplaySetting> scenes=new List<SceneDisplaySetting>();
        public SceneDisplaySetting Find(string sceneName)=>scenes.Find(entry=>entry.sceneName==sceneName);
    }
}
