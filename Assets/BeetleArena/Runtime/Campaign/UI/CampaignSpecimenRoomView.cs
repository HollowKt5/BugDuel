using System;
using System.Linq;
using BugDuel.Campaign;
using TMPro;
using UnityEngine;
using SpecimenRecord=BugDuel.Campaign.CampaignSpecimen;

namespace BeetleArena.Campaign
{
    public sealed partial class CampaignView
    {
        CampaignSpecimenRoom specimenRoom;
        string selectedSpecimenId;
        void PrepareSpecimen(string beetleId)
        {
            Confirm($"花费 {CampaignSession.SpecimenPreparationCost} G 整姿、装框并制作铭牌？\n制作后移除遗体，可在标本室布展或出售。",()=>
            {
                app.Execute(s=>s.PreserveDead(beetleId,$"第 {s.State.clock.Year} 年 {s.State.clock.Month:00} 月 {s.State.clock.Day:00} 日"),()=>app.Go(CampaignPage.SpecimenRoom));
            });
        }
        void SelectWallSlot(int slot)
        {
            var placed=State.specimens.FirstOrDefault(s=>s.wallSlot==slot);
            var selected=State.specimens.FirstOrDefault(s=>s.id==selectedSpecimenId);
            if(specimenRoom.Arranging&&selected!=null&&!selected.listed&&selected.wallSlot!=slot)
                app.Execute(s=>s.PlaceSpecimen(selected.id,slot));
            else if(placed!=null){selectedSpecimenId=placed.id;specimenRoom.Select(placed.id);QueueRefresh();}
        }
        void DrawSpecimenRoom()
        {
            float width=root.rect.width,height=root.rect.height,margin=12,inner=width-24,gap=6,half=(inner-gap)/2;
            Panel(root,0,0,width,height);
            var title=Panel(root,4,4,width-8,76);
            title.GetComponent<UnityEngine.UI.Image>().color=CampaignRetroTheme.Frame;
            Component<RetroBevel>(title).Configure(0,Color.clear,Color.clear,true,CampaignRetroTheme.TitleTop,CampaignRetroTheme.TitleBottom);
            Label(title,"标本室",12,9,width-32,32,23,Color.white);
            Label(title,"虫舍博物馆 · 三行四列的收藏墙",12,43,width-32,25,15,Color.white);
            Btn(root,"返回温室",margin,90,half,36,()=>app.Go(CampaignPage.Greenhouse));
            Btn(root,"前往市场",margin+half+gap,90,half,36,()=>app.Go(CampaignPage.Market));
            Label(root,$"收藏 {State.specimens.Count} 件  ·  上墙 {State.specimens.Count(s=>s.wallSlot>=0)}/12  ·  {State.coins:N0} G",margin,135,inner,29,17);
            Btn(root,specimenRoom.Arranging?"✓ 布展模式":"进入布展模式",margin,173,half,35,()=>{specimenRoom.SetArrange(!specimenRoom.Arranging);QueueRefresh();});
            Btn(root,"全墙 / 重置镜头",margin+half+gap,173,half,35,()=>specimenRoom.ResetView());
            var flow=Column(margin,220,inner,Mathf.Max(160,height-305));
            var selected=State.specimens.FirstOrDefault(s=>s.id==selectedSpecimenId);
            if(selected==null){selected=State.specimens.LastOrDefault();selectedSpecimenId=selected?.id;}
            specimenRoom.Select(selectedSpecimenId);
            var hint=Card(flow,82);
            Label(hint,specimenRoom.Arranging?"点选库存后点击墙位放置；拖动已有相框换位。\n右键转动 · 滚轮缩放 · 中键平移":"点击相框选择；右键转动、滚轮缩放、中键平移。\n开启布展，可换位、调整装框与铭牌。",10,10,inner-32,65,16,muted);
            if(selected!=null)DrawSpecimenInspector(flow,selected);
            var heading=Card(flow,43);Label(heading,"收藏库 · 选择一件标本",10,7,inner-32,30,20,accent);
            foreach(var record in State.specimens)
            {
                var s=record;var card=Card(flow,91);
                Label(card,CampaignDisplayText.SpecimenName(s),11,7,inner-34,27,19,s.id==selectedSpecimenId?accent:ink);
                Label(card,$"{CampaignDisplayText.SpecimenSpecies(s)} · {(s.listed?"市场挂牌中":s.wallSlot<0?"收藏库":$"墙位 {s.wallSlot+1:00}")}",11,38,inner-140,37,15,muted);
                Btn(card,s.id==selectedSpecimenId?"已选":"选择",inner-119,43,96,33,()=>{selectedSpecimenId=s.id;specimenRoom.Select(s.id);QueueRefresh();});
            }
            if(State.specimens.Count==0)
            {
                var empty=Card(flow,156);Label(empty,"收藏墙还在等待第一位住客。\n死亡甲虫可在温室制作成标本；\n也可从市场购买或参与竞拍。",12,14,inner-36,125,21,muted);
            }
            notice=Label(root,"",margin+8,height-66,inner-16,46,15);
        }
        void DrawSpecimenInspector(Flow flow,SpecimenRecord specimen)
        {
            var s=specimen;float w=flow.width-6,pad=10,inside=w-20,gap=6,half=(inside-gap)/2;
            var card=Card(flow,461);
            Label(card,CampaignDisplayText.SpecimenName(s),pad,10,inside,31,24,accent);
            string size=s.beetle.stage<CampaignLifeStage.DormantAdult?CampaignSession.StageName(s.beetle.stage):$"{s.beetle.length:0.0} mm";
            Label(card,$"{CampaignDisplayText.SpecimenSpecies(s)} · {size}\n铭牌日期 {CampaignDisplayText.SpecimenDate(s)} · 制作于第 {s.preparedWeek} 周",pad,49,inside,56,17);
            Btn(card,"近看标本",pad,113,half,34,()=>specimenRoom.Focus(s.id),s.wallSlot>=0);
            Btn(card,"编辑铭牌",pad+half+gap,113,half,34,()=>EditSpecimenPlaque(s),!s.listed);
            string[] frames={"胡桃木","黄铜","乌木"};
            Btn(card,"相框："+frames[s.frameStyle],pad,156,half,34,()=>app.Execute(session=>session.EditSpecimen(s.id,s.labelName,s.speciesLabel,s.dateLabel,(s.frameStyle+1)%3,s.rotation)),!s.listed);
            Btn(card,"旋转 +15°",pad+half+gap,156,half,34,()=>app.Execute(session=>session.EditSpecimen(s.id,s.labelName,s.speciesLabel,s.dateLabel,s.frameStyle,s.rotation>=165?-180:s.rotation+15)),!s.listed);
            Label(card,"墙位 · 点击移动 / 与原位置交换",pad,202,inside,25,16,muted);
            float cell=(inside-18)/4;
            for(int i=0;i<12;i++)
            {
                int slot=i;bool filled=State.specimens.Any(x=>x.wallSlot==slot);
                Btn(card,$"{i+1:00}{(s.wallSlot==i?" ◂":filled?" ·":"")}",pad+(i%4)*(cell+6),236+(i/4)*36,cell,30,
                    ()=>app.Execute(session=>session.PlaceSpecimen(s.id,slot)),!s.listed&&s.wallSlot!=slot);
            }
            Btn(card,"收回收藏库",pad,350,half,34,()=>app.Execute(session=>session.UnmountSpecimen(s.id)),s.wallSlot>=0&&!s.listed);
            Btn(card,s.listed?"撤回挂牌":"市场挂牌",pad+half+gap,350,half,34,()=>app.Execute(session=>{if(s.listed)session.UnlistSpecimen(s.id);else session.ListSpecimen(s.id);}),s.listed||Session.ListedCount<State.cabinet);
            long price=Session.SpecimenSellPrice(s);
            Btn(card,$"直接出售 · 到手 {price:N0} G",pad,393,inside,38,()=>Confirm($"出售「{CampaignDisplayText.SpecimenName(s)}」，收到 {price:N0} G？\n成交后将从收藏库与墙上移除。",()=>app.Execute(session=>session.SellSpecimen(s.id))),!s.listed);
        }
        void EditSpecimenPlaque(SpecimenRecord s)
        {
            var dialog=OpenModal(650,450);
            Label(dialog,"编辑藏品铭牌",26,30,598,40,28,accent);
            var name=EditableField(dialog,"藏品名称",s.labelName,92,40);
            var species=EditableField(dialog,"种类名称",s.speciesLabel,170,60);
            var date=EditableField(dialog,"铭牌日期",s.dateLabel,248,40);
            dialogNotice=Label(dialog,"铭牌可自由填写，原始物种和制作周数会保留。",26,323,598,45,17,muted);
            Btn(dialog,"保存铭牌",26,377,291,45,()=>app.Execute(session=>session.EditSpecimen(s.id,name.text,species.text,date.text,s.frameStyle,s.rotation),CloseModal));
            Btn(dialog,"取消",329,377,291,45,CloseModal);
        }
        TMP_InputField EditableField(Transform parent,string label,string value,float y,int limit)
        {
            Label(parent,label,26,y,125,38,18);
            var field=Panel(parent,157,y,467,48);field.GetComponent<UnityEngine.UI.Image>().color=CampaignRetroTheme.Panel;
            var input=Component<TMP_InputField>(field);input.targetGraphic=field.GetComponent<UnityEngine.UI.Image>();input.characterLimit=limit;input.lineType=TMP_InputField.LineType.SingleLine;
            var viewport=Rect("Input viewport",field,9,5,449,38);Component<UnityEngine.UI.RectMask2D>(viewport);
            var text=Component<TextMeshProUGUI>(Rect("Editable text",viewport,0,0,449,38));text.font=font;text.fontSize=20;text.color=ink;text.raycastTarget=false;text.richText=false;text.textWrappingMode=TextWrappingModes.NoWrap;
            input.textViewport=viewport;input.textComponent=text;input.richText=false;input.text=value;return input;
        }
    }
}
