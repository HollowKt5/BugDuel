using UnityEngine;
using UnityEngine.SceneManagement;

namespace BeetleArena
{
    public enum BeetleDisplayStyle { Clean=0, Crt=1, OrderedDither=2, Custom=3, DenseDither=4 }

    // Scene defaults live in WorldDisplay; player overrides affect only their named scene.
    public static class BeetleDisplayPreferences
    {
        public const string StyleKey = "BugDuel.Display.Style";
        public const string PixelSizeKey = "BugDuel.Display.PixelSize";
        public const string DensePixelSizeKey = "BugDuel.Display.DensePixelSize";
        static WorldDisplayProfile profile;
        static bool profileLoaded;
        public static WorldDisplayProfile Profile
        {
            get { if(!profileLoaded)ReloadProfile();return profile; }
        }
        public static void ReloadProfile(){profile=Resources.Load<WorldDisplayProfile>(WorldDisplayProfile.ResourceName);profileLoaded=true;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset(){profile=null;profileLoaded=false;}
        public static string SceneStyleKey(string scene)=>StyleKey+"."+scene;
        public static string ScenePixelSizeKey(string scene)=>PixelSizeKey+"."+scene;
        public static string SceneDensePixelSizeKey(string scene)=>DensePixelSizeKey+"."+scene;
        public static bool IsDither(BeetleDisplayStyle style)=>style==BeetleDisplayStyle.OrderedDither || style==BeetleDisplayStyle.DenseDither;
        public static float DitherPixelScale(int level)=>Mathf.Clamp(level,1,3)==3?2.5f:Mathf.Clamp(level,1,2);
        public static int DitherPixelLevel(float pixels)=>pixels<1.5f?1:pixels<2.25f?2:3;
        public static void NextPixelSize()=>PixelSize=PixelSize%3+1;
        public static string PixelSizeLabel=>PixelSize+" 档";
        static string PixelKey(string scene,BeetleDisplayStyle style)=>style==BeetleDisplayStyle.DenseDither?SceneDensePixelSizeKey(scene):ScenePixelSizeKey(scene);
        public static BeetleDisplayStyle GetStyle(string scene)
        {
            var authored=Profile?Profile.Find(scene):null;
            int fallback=authored!=null?(int)authored.style:PlayerPrefs.GetInt(StyleKey,(int)BeetleDisplayStyle.OrderedDither);
            return (BeetleDisplayStyle)PlayerPrefs.GetInt(SceneStyleKey(scene),fallback);
        }
        public static int GetPixelSize(string scene)
        {
            var authored=Profile?Profile.Find(scene):null;
            var style=GetStyle(scene);
            var material=IsDither(style)?authored?.DitherMaterial(style):authored?.customMaterial;
            int fallback=material&&material.HasProperty("_PixelSize")?(IsDither(style)?DitherPixelLevel(material.GetFloat("_PixelSize")):Mathf.RoundToInt(material.GetFloat("_PixelSize"))):
                authored?.pixelSize??PlayerPrefs.GetInt(PixelSizeKey,2);
            return Mathf.Clamp(PlayerPrefs.GetInt(PixelKey(scene,style),fallback),1,IsDither(style)?3:6);
        }
        public static Material GetCustomMaterial(string scene)=>Profile?Profile.Find(scene)?.customMaterial:null;
        public static Material GetDitherMaterial(string scene)=>Profile?Profile.Find(scene)?.DitherMaterial(GetStyle(scene)):null;
        public static void SetStyle(string scene,BeetleDisplayStyle style){PlayerPrefs.SetInt(SceneStyleKey(scene),(int)style);Save();}
        public static void SetPixelSize(string scene,int pixels){PlayerPrefs.SetInt(PixelKey(scene,GetStyle(scene)),Mathf.Clamp(pixels,1,IsDither(GetStyle(scene))?3:6));Save();}
        public static void UseSceneDefault(string scene)
        {PlayerPrefs.DeleteKey(SceneStyleKey(scene));PlayerPrefs.DeleteKey(ScenePixelSizeKey(scene));PlayerPrefs.DeleteKey(SceneDensePixelSizeKey(scene));Save();}

        // Convenience for settings on the active page. Renderers use their owning scene explicitly.
        public static BeetleDisplayStyle Style
        {
            get => GetStyle(SceneManager.GetActiveScene().name);
            set => SetStyle(SceneManager.GetActiveScene().name,value);
        }
        public static int PixelSize
        {
            get => GetPixelSize(SceneManager.GetActiveScene().name);
            set => SetPixelSize(SceneManager.GetActiveScene().name,value);
        }
        public static string StyleName(BeetleDisplayStyle style) => style == BeetleDisplayStyle.Custom?"自定义画面材质":style == BeetleDisplayStyle.Crt ? "显像管" : style == BeetleDisplayStyle.OrderedDither ? "有序抖动 · 柔和" : style == BeetleDisplayStyle.DenseDither ? "有序抖动 · 细密" : "原画";
        public static void Save() => PlayerPrefs.Save();
    }
}
