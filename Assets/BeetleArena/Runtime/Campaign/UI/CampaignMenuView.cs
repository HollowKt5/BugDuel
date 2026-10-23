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
            var window=Panel(root,64,140,510,602);
            var title=Panel(window,3,3,504,28);title.GetComponent<UI.Image>().color=CampaignRetroTheme.Frame;
            Component<RetroBevel>(title).Configure(0,Color.clear,Color.clear,true,CampaignRetroTheme.TitleTop,CampaignRetroTheme.TitleBottom);
            Label(title,"甲虫秘境",8,3,480,22,15,Color.white);
            Label(window,"甲虫决斗",28,63,460,72,52,accent);
            Label(window,"甲虫决斗 · 虫舍日记",30,147,450,32,23);
            Label(window,"收集  /  养育  /  竞技",30,186,450,26,16,muted);
            Btn(window,"开始",30,240,450,53,()=>app.Go(CampaignPage.Saves));
            Btn(window,"设置",30,307,450,53,OpenSettings);
            Btn(window,"游玩指南",30,374,450,53,OpenIntro);
            Btn(window,"退出",30,441,450,53,()=>Confirm("保存当前进度并退出游戏？",()=>{if(Session==null||app.SaveSlot(0))Application.Quit();}));
            Label(window,"离线世界  |  "+CampaignRetroTheme.SeasonName,30,518,450,28,15,muted);
            notice=Label(window,"",30,552,450,38,14,muted);
        }
        void DrawSaves()
        {
            Panel(root,32,28,1536,852);
            Label(root,"虫舍日记 / 选择存档",45,45,1200,60,38);
            Label(root,"1个自动槽 + 3个手动槽。点击空槽新建存档，或读取已有旅程。",48,120,1480,45,21,muted);
            for(int slot=0;slot<4;slot++)
            {
                int index=slot;var p=Panel(root,40+slot*390,205,365,540);
                Label(p,slot==0?"自动存档":$"手动存档 {slot}",22,20,320,32,23,accent);
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
                var thumbnail=Rect("Thumbnail",p,22.5f,70,320,180);var image=Component<UI.RawImage>(thumbnail);image.texture=null;image.color=new Color(.24f,.31f,.23f);image.raycastTarget=false;
                if(!string.IsNullOrEmpty(save?.thumbnail))
                {
                    Texture2D texture=null;
                    try
                    {
                        var bytes=Convert.FromBase64String(save.thumbnail); texture=new Texture2D(2,2);
                        if(texture.LoadImage(bytes))
                        { image.texture=texture;image.color=Color.white;thumbnails.Add(texture);texture=null; }
                        else Debug.LogWarning("存档槽 "+slot+" 的缩略图格式无效，已使用默认背景。",this);
                    }
                    catch(FormatException e) { Debug.LogWarning("存档槽 "+slot+" 的缩略图编码无效，已使用默认背景：\n"+e,this); }
                    catch(Exception e) { Debug.LogException(e,this); }
                    finally { if(texture)Destroy(texture); }
                }
                if(save!=null)
                {
                    string player=string.IsNullOrEmpty(save.state.playerName)?"尚未命名":BugDuel.Campaign.CampaignDisplayText.Literal(save.state.playerName);
                    var owner=Label(p,"玩家："+player,22,266,320,27,18,accent);owner.enableAutoSizing=true;owner.fontSizeMin=12;owner.fontSizeMax=18;owner.textWrappingMode=TMPro.TextWrappingModes.NoWrap;
                    Label(p,$"第 {save.state.week} 周  ·  {save.state.coins:N0} G\n栏位 {save.state.beetles.Count}/{save.state.capacity}\n最高联赛 Lv.{save.state.highestLeague}\n体长纪录 {save.state.recordLength:0.0} mm\n{save.savedAt}",22,301,320,132,18);
                }
                else Label(p,"存档不可读\n"+error,22,280,320,150,22,muted);
                Btn(p,"读取",20,445,155,48,()=>app.LoadSlot(index),save!=null);
                Btn(p,"覆盖",185,445,155,48,()=>Confirm("覆盖此槽位的存档？",()=>app.SaveSlot(index)),Session!=null);
                Btn(p,"删除",20,500,320,32,()=>Confirm("永久删除此槽位及备份？",()=>app.DeleteSlot(index)),save!=null||error!=null);
            }
            Btn(root,Session!=null?"返回温室":"返回主菜单",45,770,310,50,()=>app.Go(Session!=null?CampaignPage.Greenhouse:CampaignPage.MainMenu));
        }
        void OpenIntro()
        {
            var p=OpenModal(1450,535);Label(p,"从小虫舍到世界虫王",30,28,1360,50,34);
            string[] titles={"01  捕虫 / 买虫","02  喂养 / 冲体长","03  联赛 / 拍卖"};
            string[] bodies={"派遣队伍去四大栖息地。\n在市场买虫、竞拍神秘蛹。\n别忘了为回程预留栏位。","红薯和水果补充饱食与健康。\n幼虫选饲料瓶和温控方案。\n甲虫翻不过身时，要及时帮忙！","体长决定联赛级别。\n三连胜夺冠，身价大涨！\n卖冠军或首配权，收血统分红。"};
            for(int i=0;i<3;i++){var c=Panel(p,30+i*470,110,450,310,true);Label(c,titles[i],24,25,400,50,29,accent);Label(c,bodies[i],24,100,400,190,24);}
            Btn(p,"出发吧！",520,450,410,55,CloseModal);SelectFirst(p);
        }
    }
}
