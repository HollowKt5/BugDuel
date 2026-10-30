using System;
using UnityEngine;

namespace BeetleArena.Campaign
{
    [CreateAssetMenu(menuName="Beetle Arena/Campaign UI Layout",fileName=ResourceName)]
    public sealed class CampaignUiLayoutProfile : ScriptableObject
    {
        public const string ResourceName="CampaignUiLayout";
        [Tooltip("位置、间距使用 1600×900 设计坐标；X 向右、Y 向下。修改资产可保存，运行时会刷新当前页面。")]
        [InspectorName("通用布局")] public CommonLayout common=new CommonLayout();
        [InspectorName("主菜单")] public MenuLayout menu=new MenuLayout();
        [InspectorName("存档页面")] public SavesLayout saves=new SavesLayout();
        [InspectorName("养殖侧栏")] public GreenhouseLayout greenhouse=new GreenhouseLayout();
        [InspectorName("甲虫信息浮窗")] public InfoLayout info=new InfoLayout();
        [InspectorName("喂食盆浮窗")] public FloatingLayout feeding=new FloatingLayout(new Vector2(440,325));
        [InspectorName("仓库")] public WarehouseLayout warehouse=new WarehouseLayout();
        [InspectorName("市场")] public MarketLayout market=new MarketLayout();
        [InspectorName("设置窗口")] public SettingsLayout settings=new SettingsLayout();
        [InspectorName("操作提示")] public HintLayout hint=new HintLayout();
        [NonSerialized] public int revision;

        void OnValidate(){Validate();revision++;}
        public void Validate()
        {
            common??=new CommonLayout();menu??=new MenuLayout();saves??=new SavesLayout();
            greenhouse??=new GreenhouseLayout();info??=new InfoLayout();feeding??=new FloatingLayout(new Vector2(440,325));
            warehouse??=new WarehouseLayout();market??=new MarketLayout();settings??=new SettingsLayout();hint??=new HintLayout();
            common.Validate();menu.Validate();saves.Validate();greenhouse.Validate();info.Validate();
            feeding.Validate(new Vector2(400,325));warehouse.Validate();market.Validate();settings.Validate();hint.Validate();
        }
        internal static Vector2 AtLeast(Vector2 value,float x,float y)=>new Vector2(Mathf.Max(x,value.x),Mathf.Max(y,value.y));

        [Serializable] public sealed class CommonLayout
        {
            [InspectorName("固定页面整体偏移")] public Vector2 pageOffset;
            [Tooltip("相对屏幕中心，X 向右、Y 向下；通用弹窗会限制在屏幕边缘以内。")]
            [InspectorName("通用弹窗偏移")] public Vector2 dialogOffset;
            [Min(0),InspectorName("弹窗安全边距")] public float dialogMargin=12;
            [Min(0),InspectorName("普通列表卡片间距")] public float listSpacing=14;
            [Min(0),InspectorName("普通列表右留白")] public float listRightInset=6;
            [Range(.20f,.40f),InspectorName("标本室侧栏宽度比例")] public float specimenSidebar=.28f;
            public void Validate(){dialogMargin=Mathf.Max(0,dialogMargin);listSpacing=Mathf.Max(0,listSpacing);listRightInset=Mathf.Clamp(listRightInset,0,50);specimenSidebar=Mathf.Clamp(specimenSidebar,.2f,.4f);}
        }
        [Serializable] public sealed class MenuLayout
        {
            [InspectorName("标题左上位置")] public Vector2 titlePosition=new Vector2(90,218);
            [InspectorName("标题尺寸")] public Vector2 titleSize=new Vector2(740,112);
            [Tooltip("纸板标题按文字收紧，标题尺寸中的宽度作为上限。")]
            [InspectorName("标题底板贴合文字")] public bool fitTitle=true;
            [InspectorName("按钮组左上位置")] public Vector2 buttonsPosition=new Vector2(100,370);
            [InspectorName("按钮尺寸")] public Vector2 buttonSize=new Vector2(440,58);
            [Min(0),InspectorName("按钮间距")] public float buttonGap=20;
            public void Validate(){titleSize=AtLeast(titleSize,300,80);buttonSize=AtLeast(buttonSize,180,40);buttonGap=Mathf.Max(0,buttonGap);}
        }
        [Serializable] public sealed class SavesLayout
        {
            [InspectorName("窗口左上位置")] public Vector2 position=new Vector2(32,54);
            [InspectorName("窗口尺寸")] public Vector2 size=new Vector2(1536,792);
            [Min(12),InspectorName("窗口内边距")] public float padding=24;
            [Min(80),InspectorName("卡片起始高度")] public float cardsTop=107;
            [Min(0),InspectorName("卡片间距")] public float cardGap=20;
            [Min(540),InspectorName("卡片高度")] public float cardHeight=540;
            [Range(10,32),InspectorName("卡片内边距")] public float cardPadding=20;
            [Range(12,48),InspectorName("按钮组底部留白")] public float buttonsBottomPadding=26;
            public void Validate(){padding=Mathf.Clamp(padding,12,60);cardsTop=Mathf.Max(80,cardsTop);cardGap=Mathf.Clamp(cardGap,0,60);cardHeight=Mathf.Max(540,cardHeight);cardPadding=Mathf.Clamp(cardPadding,10,32);buttonsBottomPadding=Mathf.Clamp(buttonsBottomPadding,12,48);size=AtLeast(size,4*(cardPadding*2+240)+padding*2+cardGap*3,cardsTop+cardHeight+145);}
        }
        [Serializable] public sealed class GreenhouseLayout
        {
            [Tooltip("占屏幕宽度的比例。仓库展开、收起动效和场景视口共用此值。")]
            [Range(.14f,.28f),InspectorName("侧栏宽度比例")] public float sidebarFraction=1f/6f;
            [Range(4,24),InspectorName("左右内边距")] public float padding=7;
            [Min(40),InspectorName("时间栏顶部")] public float clockTop=44;
            [Min(30),InspectorName("时间栏高度")] public float clockHeight=34;
            [Min(80),InspectorName("导航顶部")] public float navigationTop=86;
            [Min(38),InspectorName("导航按钮高度")] public float buttonHeight=48;
            [Min(0),InspectorName("导航按钮间距")] public float buttonGap=7;
            [Min(46),InspectorName("底栏高度")] public float footerHeight=46;
            public void Validate(){sidebarFraction=Mathf.Clamp(sidebarFraction,.14f,.28f);padding=Mathf.Clamp(padding,4,24);clockTop=Mathf.Max(40,clockTop);clockHeight=Mathf.Max(30,clockHeight);navigationTop=Mathf.Max(clockTop+clockHeight+8,navigationTop);buttonHeight=Mathf.Max(38,buttonHeight);buttonGap=Mathf.Max(0,buttonGap);footerHeight=Mathf.Max(46,footerHeight);}
        }
        [Serializable] public class FloatingLayout
        {
            [Tooltip("距离屏幕右边、上边的距离，正数向屏幕内移动。")]
            [InspectorName("右上角边距")] public Vector2 inset=new Vector2(24,48);
            [InspectorName("窗口尺寸")] public Vector2 size;
            public FloatingLayout(Vector2 size){this.size=size;}
            public void Validate(Vector2 minimum){inset=AtLeast(inset,0,0);size=AtLeast(size,minimum.x,minimum.y);}
        }
        [Serializable] public sealed class InfoLayout : FloatingLayout
        {
            [Range(6,24),InspectorName("内容左右内边距")] public float padding=10;
            [Min(0),InspectorName("内容卡片间距")] public float cardGap=7;
            public InfoLayout():base(new Vector2(480,610)){}
            public void Validate(){base.Validate(new Vector2(480,360));padding=Mathf.Clamp(padding,6,24);cardGap=Mathf.Max(0,cardGap);}
        }
        [Serializable] public sealed class WarehouseLayout
        {
            [Range(.8f,2f),InspectorName("物品栏相对侧栏宽度")] public float detailWidthScale=1;
            [Range(4,30),InspectorName("物品区内边距")] public float padding=12;
            [Range(4,24),InspectorName("卡片内边距")] public float cardPadding=12;
            [Min(0),InspectorName("卡片间距")] public float gap=10;
            [Min(108),InspectorName("卡片最小宽度")] public float minCardWidth=108;
            [Min(140),InspectorName("卡片高度")] public float cardHeight=174;
            [Min(40),InspectorName("图片区域高度")] public float thumbnailHeight=86;
            [Min(32),InspectorName("分类按钮高度")] public float categoryHeight=43;
            [Min(0),InspectorName("分类按钮间距")] public float categoryGap=1;
            public float TileHeight=>Mathf.Max(cardHeight,cardPadding*2+thumbnailHeight+6+24+4+22);
            public float MinimumWidth=>Mathf.Max(minCardWidth,cardPadding*2+64);
            public void Validate(){detailWidthScale=Mathf.Clamp(detailWidthScale,.8f,2);padding=Mathf.Clamp(padding,4,30);cardPadding=Mathf.Clamp(cardPadding,4,24);gap=Mathf.Clamp(gap,0,60);minCardWidth=Mathf.Max(108,minCardWidth);cardHeight=Mathf.Max(140,cardHeight);thumbnailHeight=Mathf.Max(40,thumbnailHeight);categoryHeight=Mathf.Max(32,categoryHeight);categoryGap=Mathf.Max(0,categoryGap);}
        }
        [Serializable] public sealed class MarketLayout
        {
            [Tooltip("整个市场窗口的左上和右下留白；默认零，铺满屏幕。窗口至少保留1280×600，避免顶栏按钮与金币重叠。")]
            [InspectorName("窗口左上留白")] public Vector2 topLeft;
            [InspectorName("窗口右下留白")] public Vector2 bottomRight;
            [Range(220,420),InspectorName("商品分类栏宽度")] public float categoryWidth=280;
            [Range(4,32),InspectorName("商品区内边距")] public int gridPadding=12;
            [InspectorName("商品卡片间距")] public Vector2 gap=new Vector2(10,10);
            [Min(320),InspectorName("商品卡片最小宽度")] public float minCardWidth=390;
            [Min(230),InspectorName("商品卡片高度")] public float cardHeight=230;
            [Range(8,24),InspectorName("商品卡片内边距")] public float cardPadding=12;
            [Range(60,160),InspectorName("商品图片尺寸")] public float thumbnailSize=94;
              [Min(300),InspectorName("拍卖卡片高度")] public float auctionCardHeight=308;
            [Header("商品详情窗口")]
            [InspectorName("详情窗口尺寸")] public Vector2 detailSize=new Vector2(820,560);
            [Range(20,40),InspectorName("详情内边距")] public float detailPadding=28;
            [Range(96,176),InspectorName("详情图片尺寸")] public float detailArtworkSize=128;
            [Range(52,76),InspectorName("详情属性行步长")] public float detailRowHeight=58;
            public float DetailHeight=>Mathf.Max(detailSize.y,130+detailRowHeight*5+detailPadding+112);
            public float CardHeight=>Mathf.Max(cardHeight,30+cardPadding+Mathf.Max(96,thumbnailSize+2)+108);
            public float MinimumWidth=>Mathf.Max(minCardWidth,cardPadding*3+thumbnailSize+180);
              public float AuctionCardHeight=>Mathf.Max(auctionCardHeight,30+cardPadding+Mathf.Max(112,thumbnailSize)+132);
            public void Validate(){topLeft=AtLeast(topLeft,0,0);bottomRight=AtLeast(bottomRight,0,0);categoryWidth=Mathf.Clamp(categoryWidth,220,420);gridPadding=Mathf.Clamp(gridPadding,4,32);gap=AtLeast(gap,0,0);minCardWidth=Mathf.Max(320,minCardWidth);cardHeight=Mathf.Max(230,cardHeight);cardPadding=Mathf.Clamp(cardPadding,8,24);thumbnailSize=Mathf.Clamp(thumbnailSize,60,160);auctionCardHeight=Mathf.Max(300,auctionCardHeight);detailSize=AtLeast(detailSize,720,520);detailPadding=Mathf.Clamp(detailPadding,20,40);detailArtworkSize=Mathf.Clamp(detailArtworkSize,96,176);detailRowHeight=Mathf.Clamp(detailRowHeight,52,76);}
        }
        [Serializable] public sealed class SettingsLayout
        {
            [Tooltip("在通用弹窗偏移基础上叠加，X 向右、Y 向下。")]
            [InspectorName("窗口偏移")] public Vector2 offset;
            [InspectorName("窗口尺寸")] public Vector2 size=new Vector2(1050,760);
            [Range(30,60),InspectorName("内容左右内边距")] public float padding=40;
            [Range(.35f,.6f),InspectorName("文字列宽度比例")] public float labelFraction=500f/970f;
            [Range(12,50),InspectorName("文字与按钮间距")] public float columnGap=20;
            [Min(170),InspectorName("常规设置行顶部")] public float rowsTop=170;
            [Min(60),InspectorName("常规设置行步长")] public float rowStep=60;
            public float RowY(float original)=>rowsTop+(original-170)*rowStep/60;
            public float Height=>Mathf.Max(size.y,RowY(590)+170);
            public void Validate(){size=AtLeast(size,1050,760);padding=Mathf.Clamp(padding,30,60);labelFraction=Mathf.Clamp(labelFraction,.35f,.6f);columnGap=Mathf.Clamp(columnGap,12,50);rowsTop=Mathf.Max(170,rowsTop);rowStep=Mathf.Max(60,rowStep);}
        }
        [Serializable] public sealed class HintLayout
        {
            [InspectorName("右下角边距")] public Vector2 inset=new Vector2(24,22);
            [InspectorName("提示框尺寸")] public Vector2 size=new Vector2(480,164);
            [Range(12,30),InspectorName("内容内边距")] public float padding=16;
            public void Validate(){inset=AtLeast(inset,0,0);size=AtLeast(size,360,164);padding=Mathf.Clamp(padding,12,30);}
        }
    }
}
