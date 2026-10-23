using BugDuel.Campaign;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BeetleArena.Campaign
{
    /// <summary>Presentation-only palette shared by the campaign windows and existing combat HUD.</summary>
    public static class CampaignRetroTheme
    {
        const string ModeKey="BugDuel.Retro.Dark";
        public static bool Dark => PlayerPrefs.GetInt(ModeKey,0)!=0;
        public static int Season => SeasonFor(CampaignApp.Instance?.Session?.State.clock);
        // Calendar changes and loaded saves already refresh campaign views through App.Changed.
        // Without a journey, the main menu uses spring; system dates and old theme preferences do not apply.
        public static int SeasonFor(CampaignClock clock) => clock==null?0:(clock.Month-1)/3;
        public static void SetMode(bool dark)
        { PlayerPrefs.SetInt(ModeKey,dark?1:0);PlayerPrefs.Save(); }
        public static Color Hex(uint rgb)=>new Color(((rgb>>16)&255)/255f,((rgb>>8)&255)/255f,(rgb&255)/255f,1);
        static Color Mode(uint light,uint dark)=>Hex(Dark?dark:light);
        public static Color Window=>Mode(0xece9d8,0x1f2327);
        public static Color Panel=>Mode(0xffffff,0x181c20);
        public static Color Ink=>Mode(0x000000,0xe5e7eb);
        public static Color Muted=>Mode(0x666666,0x9ca3af);
        public static Color Accent=>Mode(0x003399,0x60a5fa);
        public static Color Border=>Mode(0x7f9db9,0x374151);
        public static Color Button=>Mode(0xece9d8,0x2b3138);
        public static Color LightEdge=>Mode(0xffffff,0x4b5563);
        public static Color DarkEdge=>Mode(0x808080,0x1a1e22);
        public static Color Notice=>Mode(0xffffe1,0x27281c);
        public static Color Grid=>Mode(0xfbfbfd,0x14171a);
        public static Color Price=>Mode(0xcc0000,0xf87171);
        public static Color Frame=>Hex(new uint[]{0x059669,0xd97706,0xc2410c,0x0284c7}[Season]);
        public static Color TitleTop=>Hex((Dark?new uint[]{0x047857,0xb45309,0x9a3412,0x0369a1}:new uint[]{0x10b981,0xf59e0b,0xea580c,0x38bdf8})[Season]);
        public static Color TitleBottom=>Hex((Dark?new uint[]{0x022c22,0x451a03,0x431407,0x082f49}:new uint[]{0x047857,0xb45309,0x9a3412,0x0369a1})[Season]);
        public static string SeasonName=>new[]{"春季·生机绿意","夏季·暖阳炽金","秋季·落叶枫红","冬季·纯白霜雪"}[Season];

        public static void ApplyHierarchy(Transform root)
        {
            // Keep progress bars and illustrations intact; restyle only rectangular surfaces/buttons.
            foreach(var image in root.GetComponentsInChildren<Image>(true))
            {
                if(image.type==Image.Type.Filled || image.color.a<.01f)continue;
                image.sprite=null;image.material=null;image.type=Image.Type.Simple;
                image.color=image.GetComponent<Button>()?Button:Window;
                var edge=image.GetComponent<RetroBevel>()??image.gameObject.AddComponent<RetroBevel>();
                edge.Configure(image.GetComponent<Button>()?2:1,LightEdge,DarkEdge);
            }
            var font=Resources.Load<TMP_FontAsset>("RetroUiFont");
            foreach(var text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                bool backed=false;
                for(var parent=text.transform.parent;parent;parent=parent.parent)
                {
                    var background=parent.GetComponent<Image>();
                    if(background&&background.color.a>.95f){backed=true;break;}
                    if(parent==root)break;
                }
                text.color=backed?Ink:Color.white;if(font)text.font=font;
            }
            foreach(var button in root.GetComponentsInChildren<Button>(true))
            {
                var colors=button.colors;colors.normalColor=Color.white;
                colors.highlightedColor=Hex(0xe3ebfa);colors.pressedColor=Hex(0xc4d5f2);
                colors.selectedColor=Hex(0xe3ebfa);colors.disabledColor=new Color(.75f,.75f,.75f,1);button.colors=colors;
            }
            CampaignLocalization.BindHierarchy(root);
        }
    }
}
