using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UI=UnityEngine.UI;

namespace BeetleArena.Campaign
{
    public sealed partial class CampaignView
    {
        void DrawMenu()
        {
            var layout=Layout.menu;
            if(CampaignRetroTheme.Paper)
            {
                var title=Panel(root,layout.titlePosition.x,layout.titlePosition.y,layout.titleSize.x,layout.titleSize.y,name:"PaperTitle");
                title.GetComponent<UI.Image>().color=CampaignRetroTheme.Panel;
                var label=Label(title,"甲壳虫斗",22,10,layout.titleSize.x-50,layout.titleSize.y-20,64,CampaignRetroTheme.BrandInk);
                if(layout.fitTitle)
                {
                    float width=Mathf.Clamp(Mathf.Ceil(label.GetPreferredValues(label.text,float.PositiveInfinity,layout.titleSize.y-20).x)+50,Mathf.Min(320,layout.titleSize.x),layout.titleSize.x);
                    title.sizeDelta=new Vector2(width,layout.titleSize.y);label.rectTransform.sizeDelta=new Vector2(width-50,layout.titleSize.y-20);
                }
            }
            else Label(root,"甲壳虫斗",layout.titlePosition.x+4,layout.titlePosition.y+8,layout.titleSize.x+40,layout.titleSize.y-20,64,CampaignRetroTheme.BrandInk);
            float y=layout.buttonsPosition.y;
            void MenuButton(string text,Action action)
            {Btn(root,text,layout.buttonsPosition.x,y,layout.buttonSize.x,layout.buttonSize.y,action);y+=layout.buttonSize.y+layout.buttonGap;}
            if(Session!=null)MenuButton("继续",()=>app.Go(CampaignPage.Greenhouse));
            MenuButton("开始 / 存档",()=>app.Go(CampaignPage.Saves));
            MenuButton("设置",OpenSettings);
            MenuButton("退出",()=>Confirm("保存当前进度并退出游戏？",()=>{if(Session==null||app.SaveSlot(0))Application.Quit();}));
            foreach(var button in root.GetComponentsInChildren<UI.Button>())
            {var label=button.GetComponentInChildren<TMPro.TMP_Text>();CampaignLocalization.SetFontSize(label,27,24);}
        }
        void DrawSaves()
        {
            var layout=Layout.saves;
            var window=Panel(root,layout.position.x,layout.position.y,layout.size.x,layout.size.y,name:"SavesWindow");
            float width=window.rect.width,padding=layout.padding,cardPadding=layout.cardPadding,cardWidth=(width-padding*2-layout.cardGap*3)/4;
            Label(window,"选择存档",padding,17,width-padding*2,60,38);
            for(int slot=0;slot<4;slot++)
            {
                int index=slot;var p=Panel(window,padding+slot*(cardWidth+layout.cardGap),layout.cardsTop,cardWidth,layout.cardHeight,name:"SaveSlot"+slot);
                p.GetComponent<UI.Image>().color=CampaignRetroTheme.Panel;
                float innerWidth=cardWidth-cardPadding*2;
                float buttonsY=layout.cardHeight-layout.buttonsBottomPadding-87;
                float thumbnailHeight=Mathf.Min(innerWidth*9/16,buttonsY-263);
                Label(p,slot==0?"自动存档":$"手动存档 {slot}",cardPadding,20,innerWidth,32,23,accent);
                CampaignSave save=null;string error=null;
                try { save=app.ReadSlot(slot); }
                catch(Exception e) when(CampaignApp.IsExternalDataFailure(e))
                { Debug.LogWarning("存档槽 "+slot+" 读取失败：\n"+e,this); error=CampaignApp.ExternalFailureMessage(e); }
                catch(Exception e)
                { Debug.LogException(e,this); error="发生内部错误，详情已记录到控制台。"; }
                if(save==null && error==null)
                {
                    var create=Btn(p,"新建存档",62.5f,242,240,56,()=>
                    {
                        if(Session==null)app.NewGame(index);
                        else Confirm("在此空槽开始新的旅程？已有手动存档保留，后续自动保存会记录新旅程。",()=>app.NewGame(index));
                    });
                    var center=(RectTransform)create.transform;center.anchorMin=center.anchorMax=center.pivot=new Vector2(.5f,.5f);center.anchoredPosition=Vector2.zero;
                    continue;
                }
                bool hasThumbnail=false;
                if(!string.IsNullOrEmpty(save?.thumbnail))
                {
                    Texture2D texture=null;
                    try
                    {
                        var bytes=Convert.FromBase64String(save.thumbnail); texture=new Texture2D(2,2);
                        if(texture.LoadImage(bytes)&&HasSaveThumbnailContent(texture))
                        {
                            float thumbnailWidth=thumbnailHeight*16/9;
                            var thumbnail=Rect("Thumbnail",p,cardPadding+(innerWidth-thumbnailWidth)*.5f,70,thumbnailWidth,thumbnailHeight);
                            var image=Component<UI.RawImage>(thumbnail);image.texture=texture;image.color=Color.white;image.raycastTarget=false;
                            thumbnails.Add(texture);texture=null;hasThumbnail=true;
                        }
                    }
                    catch(FormatException e) { Debug.LogWarning("存档槽 "+slot+" 的缩略图编码无效，已隐藏预览：\n"+e,this); }
                    catch(Exception e) { Debug.LogException(e,this); }
                    finally { if(texture)Destroy(texture); }
                }
                if(save!=null)
                {
                    string player=string.IsNullOrEmpty(save.state.playerName)?"尚未命名":BugDuel.Campaign.CampaignDisplayText.Literal(save.state.playerName);
                    float detailsY=hasThumbnail?86+thumbnailHeight:82;
                    var owner=Label(p,"玩家："+player,cardPadding,detailsY,innerWidth,27,18,accent);owner.enableAutoSizing=true;CampaignLocalization.SetFontSize(owner,18,12);owner.textWrappingMode=TMPro.TextWrappingModes.NoWrap;
                    Label(p,$"第 {save.state.week} 周  ·  {save.state.coins:N0} G\n栏位 {save.state.beetles.Count}/{save.state.capacity}\n最高联赛 Lv.{save.state.highestLeague}\n体长纪录 {save.state.recordLength:0.0} mm\n{save.savedAt}",cardPadding,detailsY+35,innerWidth,132,18);
                }
                else Label(p,"存档不可读\n"+error,cardPadding,82,innerWidth,280,22,muted);
                float buttonWidth=(innerWidth-10)/2;
                Btn(p,"读取",cardPadding,buttonsY,buttonWidth,48,()=>app.LoadSlot(index),save!=null);
                Btn(p,"覆盖",cardPadding+10+buttonWidth,buttonsY,buttonWidth,48,()=>Confirm("覆盖此槽位的存档？",()=>app.SaveSlot(index)),Session!=null);
                Btn(p,"删除",cardPadding,buttonsY+55,innerWidth,32,()=>Confirm("永久删除此槽位及备份？",()=>app.DeleteSlot(index)),save!=null||error!=null);
            }
            Btn(window,Session!=null?"返回温室":"返回主菜单",padding,layout.size.y-117,310,50,()=>app.Go(Session!=null?CampaignPage.Greenhouse:CampaignPage.MainMenu));
        }
        static bool HasSaveThumbnailContent(Texture2D texture)
        {
            var pixels=texture.GetPixels32();if(pixels.Length==0)return false;
            Color32 first=pixels[0];
            for(int i=0;i<pixels.Length;i+=Mathf.Max(1,pixels.Length/512))
            {
                var pixel=pixels[i];
                if(Mathf.Abs(pixel.r-first.r)>8||Mathf.Abs(pixel.g-first.g)>8||Mathf.Abs(pixel.b-first.b)>8)return true;
            }
            return false;
        }
    }
}
