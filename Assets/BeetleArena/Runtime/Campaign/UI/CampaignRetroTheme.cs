using BugDuel.Campaign;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BeetleArena.Campaign
{
    public enum CampaignUiStyle { Classic, ComicPaper }

    /// <summary>Presentation-only palette shared by the campaign windows and existing combat HUD.</summary>
    public static class CampaignRetroTheme
    {
        const string ModeKey="BugDuel.Retro.Dark";
        const string StyleKey="BugDuel.UI.Style";
        static readonly uint[] Frames={0x496151,0x75613e,0x765347,0x466171};
        static readonly uint[] DarkFrames={0x506b5b,0x806b47,0x7a574b,0x4e6a7b};
        static readonly uint[] TitleTops={0x5f7566,0x87704d,0x866459,0x5b7281};
        static readonly uint[] TitleBottoms={0x43594b,0x645236,0x61463e,0x3c5564};
        static readonly uint[] DarkTitleTops={0x3c5147,0x5e5037,0x5b433d,0x3c4f5c};
        static readonly uint[] DarkTitleBottoms={0x293b32,0x403724,0x3e2e29,0x293b46};
        static readonly uint[] DarkAccents={0xb3c8b6,0xdbc395,0xd8b3a3,0xadc4d5};
        static int appliedPalette=-1;
        public static event System.Action Changed;
        public static bool Dark => PlayerPrefs.GetInt(ModeKey,0)!=0;
        public static CampaignUiStyle Style => (CampaignUiStyle)Mathf.Clamp(PlayerPrefs.GetInt(StyleKey,1),0,1);
        public static bool Paper => Style==CampaignUiStyle.ComicPaper;
        public static string StyleName => Paper?"漫画纸板":"经典界面";
        public static int Season => SeasonFor(CampaignApp.Instance?.Session?.State.clock);
        // Calendar changes and loaded saves already refresh campaign views through App.Changed.
        // Without a journey, the main menu uses spring; system dates and old theme preferences do not apply.
        public static int SeasonFor(CampaignClock clock) => clock==null?0:(clock.Month-1)/3;
        public static void SetMode(bool dark)
        { PlayerPrefs.SetInt(ModeKey,dark?1:0);PlayerPrefs.Save();NotifyCurrentPalette(); }
        public static void SetStyle(CampaignUiStyle style)
        { PlayerPrefs.SetInt(StyleKey,Mathf.Clamp((int)style,0,1));PlayerPrefs.Save();NotifyCurrentPalette(); }
        public static void NotifyCurrentPalette()
        {
            int palette=(int)Style*8+Season*2+(Dark?1:0);
            if(appliedPalette==palette)return;
            appliedPalette=palette;Changed?.Invoke();
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset(){appliedPalette=-1;Changed=null;}
        public static Color Hex(uint rgb)=>new Color(((rgb>>16)&255)/255f,((rgb>>8)&255)/255f,(rgb&255)/255f,1);
        static Color Mode(uint light,uint dark)=>Hex(Dark?dark:light);
        public static Color Window=>Paper?Mode(0xf9f6ed,0x30312c):Mode(0xf3f1e8,0x252a28);
        public static Color Panel=>Paper?Mode(0xfffffa,0x252722):Mode(0xfffefa,0x1c211f);
        public static Color Ink=>Paper?Mode(0x232824,0xf7f1e4):Mode(0x28302d,0xeceee6);
        public static Color Muted=>Paper?Mode(0x656358,0xbcb6a5):Mode(0x67706a,0xa4ada5);
        public static Color Accent=>Dark?Hex(DarkAccents[Season]):Frame;
        public static Color Border=>Paper?PaperOutline:Mode(0xa3a397,0x555f57);
        public static Color Button=>Paper?Mode(0xe9dbbe,0x4a463a):Mode(0xe6e3d8,0x343c37);
        public static Color LightEdge=>Mode(0xfffffc,0x606b62);
        public static Color DarkEdge=>Mode(0x898d81,0x111814);
        public static Color Notice=>Paper?Mode(0xfff5d8,0x393629):Mode(0xf8f4e4,0x303126);
        public static Color Grid=>Paper?Mode(0xfffffc,0x22251f):Mode(0xfbfaf6,0x1a201c);
        public static Color Price=>Mode(0xa34838,0xe6a090);
        public static Color Positive=>Mode(0x41644c,0x9bc7a3);
        public static Color BrandInk=>Paper?Ink:Hex(DarkAccents[Season]);
        public static Color Frame=>Hex((Dark?DarkFrames:Frames)[Season]);
        public static Color TitleTop=>Paper?Frame:Hex((Dark?DarkTitleTops:TitleTops)[Season]);
        public static Color TitleBottom=>Paper?Frame:Hex((Dark?DarkTitleBottoms:TitleBottoms)[Season]);
        public static Color TitleInk=>Hex(0xfffdf5);
        public static Color TitleHighlight=>Color.Lerp(TitleTop,TitleInk,.25f);
        public static Color TitleShadow=>Color.Lerp(TitleBottom,Hex(0x17211a),.28f);
        public static Color Selection=>Frame;
        public static Color SelectionInk=>TitleInk;
        public static Color HoverTint=>Color.Lerp(Color.white,Hex(Frames[Season]),Dark?.045f:.09f);
        public static Color PressedTint=>Color.Lerp(Color.white,Hex(Frames[Season]),Dark?.16f:.22f);
        public static Color DisabledTint=>new Color(.76f,.76f,.74f,1);
        public static Color SurfaceInset=>Paper?Mode(0xe1d3b7,0x24251f):Mode(0xe3e3d8,0x171d19);
        public static Color SurfaceRaised=>Paper?Mode(0xfffffa,0x373830):Mode(0xfaf9f3,0x292f2b);
        public static Color Rule=>Paper?Mode(0xb5ac96,0x77715f):Mode(0xd6d7ca,0x414b43);
        public static Color InactiveTab=>Paper?Mode(0xd9c9a8,0x39382e):Mode(0xdcded0,0x202822);
        public static Color Danger=>Mode(0x964b3c,0x8b4c40);
        public static Color Thumbnail=>Mode(0x263329,0x141c17);
        public static Color PaperOutline=>Mode(0x232824,0xcfc5af);
        public static Color PaperBoard=>Mode(0xb79a6b,0x74634a);
        public static Color PaperShadow=>Mode(0x474a3f,0x111610);
        public static string SeasonName=>new[]{"春季·生机绿意","夏季·暖阳炽金","秋季·落叶枫红","冬季·纯白霜雪"}[Season];

        public static void ApplyHierarchy(Transform root)
        {
            // Keep progress bars and illustrations intact; restyle only rectangular surfaces/buttons.
            foreach(var image in root.GetComponentsInChildren<Image>(true))
            {
                if(image.type==Image.Type.Filled || image.color.a<.01f)continue;
                var toggle=image.GetComponentInParent<Toggle>(true);
                if(toggle&&toggle.graphic==image)
                {
                    image.color=Accent;
                    if(image.TryGetComponent<RetroBevel>(out var markEdge))markEdge.enabled=false;
                    continue;
                }
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
                colors.highlightedColor=HoverTint;colors.pressedColor=PressedTint;
                colors.selectedColor=HoverTint;colors.disabledColor=DisabledTint;button.colors=colors;
            }
            CampaignLocalization.BindHierarchy(root);
        }
    }
}
