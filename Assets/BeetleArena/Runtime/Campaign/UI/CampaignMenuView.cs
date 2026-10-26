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
            Label(root,"甲壳虫斗",94,226,780,92,64,new Color(1,.94f,.72f));
            if(Session!=null)Btn(root,"继续",100,370,440,58,()=>app.Go(CampaignPage.Greenhouse));
            Btn(root,"开始 / 存档",100,Session!=null?448:370,440,58,()=>app.Go(CampaignPage.Saves));
            Btn(root,"退出",100,Session!=null?526:448,440,58,()=>Confirm("保存当前进度并退出游戏？",()=>{if(Session==null||app.SaveSlot(0))Application.Quit();}));
            foreach(var button in root.GetComponentsInChildren<UI.Button>())
            {var label=button.GetComponentInChildren<TMPro.TMP_Text>();CampaignLocalization.SetFontSize(label,27,24);}
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
                    var owner=Label(p,"玩家："+player,22,266,320,27,18,accent);owner.enableAutoSizing=true;CampaignLocalization.SetFontSize(owner,18,12);owner.textWrappingMode=TMPro.TextWrappingModes.NoWrap;
                    Label(p,$"第 {save.state.week} 周  ·  {save.state.coins:N0} G\n栏位 {save.state.beetles.Count}/{save.state.capacity}\n最高联赛 Lv.{save.state.highestLeague}\n体长纪录 {save.state.recordLength:0.0} mm\n{save.savedAt}",22,301,320,132,18);
                }
                else Label(p,"存档不可读\n"+error,22,280,320,150,22,muted);
                Btn(p,"读取",20,445,155,48,()=>app.LoadSlot(index),save!=null);
                Btn(p,"覆盖",185,445,155,48,()=>Confirm("覆盖此槽位的存档？",()=>app.SaveSlot(index)),Session!=null);
                Btn(p,"删除",20,500,320,32,()=>Confirm("永久删除此槽位及备份？",()=>app.DeleteSlot(index)),save!=null||error!=null);
            }
            Btn(root,Session!=null?"返回温室":"返回主菜单",45,770,310,50,()=>app.Go(Session!=null?CampaignPage.Greenhouse:CampaignPage.MainMenu));
        }
    }
}
