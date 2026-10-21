using System;
using System.Collections.Generic;
using System.Linq;

namespace BugDuel.Campaign
{
    [Serializable] public sealed class CampaignMail
    {
        public string id, senderId, subject, body;
        public int week;
        public bool read, deleted, claimed;
        public List<CampaignAttachment> attachments = new List<CampaignAttachment>();
        public bool Pending => !claimed && attachments.Count > 0;
    }
    [Serializable] public sealed class CampaignAttachment
    {
        public string itemId, name;
        public int quantity = 1;
        // Lists preserve absence through Unity inline serialization, unlike optional class fields.
        public List<CampaignBeetle> beetles = new List<CampaignBeetle>();
    }
    [Serializable] public sealed class CampaignInventoryStack { public string itemId; public int quantity; }

    public sealed class CampaignContact
    {
        public readonly string id, name, address, description;
        public CampaignContact(string id,string name,string address,string description)
        { this.id=id;this.name=name;this.address=address;this.description=description; }
    }
    public static class CampaignMailContacts
    {
        public static readonly IReadOnlyList<CampaignContact> All = Array.AsReadOnly(new[] {
            new CampaignContact("keeper","虫舍管理员","keeper@bugduel.local","虫舍来信与使用指引。这里的联系人仅用于游戏内邮件。"),
            new CampaignContact("store","甲虫商店","store@bugduel.local","负责甲虫现货订单。活体领取后暂存在仓库，再安排入住温室。"),
            new CampaignContact("supplies","养殖用品店","supplies@bugduel.local","配送红薯、水果、捕虫道具和展柜扩展包。"),
            new CampaignContact("auction","集市拍卖员","auction@bugduel.local","负责神秘蛹盲拍成交后的附件配送。"),
            new CampaignContact("market","交易市场","market@bugduel.local","负责市场交易的实物赠礼配送。") });
        public static CampaignContact Find(string id) => All.FirstOrDefault(c=>c.id==id);
    }
    public static class CampaignItems
    {
        public const string Beetle="beetle", Sap="sap", Lamp="lamp", Cabinet="cabinet-upgrade";
        public static string Food(int index)
        {
            CampaignSession.Require(index>=0&&index<3,"食物不存在");
            return new[]{"food-potato","food-banana","food-apple"}[index];
        }
        public static int FoodIndex(string id)=>Array.IndexOf(new[]{"food-potato","food-banana","food-apple"},id);
        public static bool Known(string id)=>id==Sap||id==Lamp||id==Cabinet||FoodIndex(id)>=0;
        public static string Name(string id)
        {
            switch(id) { case "food-potato":return "红薯";case "food-banana":return "香蕉";case "food-apple":return "苹果";
                case Sap:return "发酵树液蜜";case Lamp:return "高功率紫外灯";case Cabinet:return "展柜扩展包";default:return id; }
        }
    }

    // Mail delivery and inventory are domain state. Scenes only submit durable application commands.
    public sealed partial class CampaignSession
    {
        void UpgradeMailbox()
        {
            if(State==null||State.mailboxVersion!=0)return;
            State.mail=State.mail??new List<CampaignMail>();State.inventory=State.inventory??new List<CampaignInventoryStack>();
            State.storedBeetles=State.storedBeetles??new List<CampaignBeetle>();State.mailboxVersion=1;
            State.mail.Add(new CampaignMail {id=Id(),senderId="keeper",week=State.week,subject="欢迎使用虫舍邮箱",body="市场购买的甲虫、食物和道具会通过邮件送达。\n\n打开邮件领取附件后，物品才会加入仓库。食物从仓库投喂，活体从仓库安排入住温室。\n\n邮件可以移入已删除并恢复；带有未领取附件的邮件不能永久删除。"});
        }
        public int UnreadMail => State.mail.Count(m=>!m.deleted&&!m.read);
        public int PendingMail => State.mail.Count(m=>m.Pending);
        public int InventoryCount(string id)=>id==CampaignItems.Sap?State.sap:id==CampaignItems.Lamp?State.lamp:State.inventory.FirstOrDefault(x=>x.itemId==id)?.quantity??0;
        public bool CabinetOrdered=>State.cabinet>=6||InventoryCount(CampaignItems.Cabinet)>0||State.mail.Any(m=>m.Pending&&m.attachments.Any(a=>a.itemId==CampaignItems.Cabinet));
        CampaignMail Mail(string id)=>State.mail.FirstOrDefault(m=>m.id==id)??throw new InvalidOperationException("邮件不存在");
        void Deliver(string sender,string subject,CampaignAttachment attachment)
        {
            State.mail.Add(new CampaignMail {id=Id(),senderId=sender,week=State.week,subject=subject,
                body="感谢光临，订单已完成。请领取下方附件，物品会存入仓库，不会自动使用。",attachments=new List<CampaignAttachment>{attachment}});
            Tell(attachment.name+"已发送至邮箱，请领取附件。");
        }
        void DeliverBeetle(string sender,CampaignBeetle beetle)
        {
            Deliver(sender,"活体配送 · "+beetle.name,new CampaignAttachment {itemId=CampaignItems.Beetle,name=beetle.name,beetles=new List<CampaignBeetle>{beetle}});
        }
        void DeliverItem(string id)
        {
            Deliver("supplies","订单配送 · "+CampaignItems.Name(id),new CampaignAttachment {itemId=id,name=CampaignItems.Name(id)});
        }
        public void BuyFood(int food)
        {
            string id=CampaignItems.Food(food);Spend(food==0?15:20);DeliverItem(id);
        }
        public void MarkMailRead(string id) { Mail(id).read=true; }
        public void DeleteMail(string id) { Mail(id).deleted=true;Tell("邮件已移至已删除，可随时恢复。"); }
        public void RestoreMail(string id) { Mail(id).deleted=false;Tell("邮件已恢复到收件箱。"); }
        public void PurgeMail(string id)
        {
            var m=Mail(id);Require(m.deleted,"请先将邮件移至已删除");Require(!m.Pending,"请先恢复邮件并领取附件");State.mail.Remove(m);Tell("邮件已永久删除。");
        }
        public void ClaimMail(string id)
        {
            var m=Mail(id);Require(!m.deleted,"请先恢复邮件再领取附件");Require(m.Pending,"附件已领取或邮件没有附件");
            // Validate every attachment before mutating anything; the app also clones and atomically commits.
            foreach(var a in m.attachments)
            {
                Require(a.quantity>0,"附件数量无效");
                if(a.itemId==CampaignItems.Beetle)Require(a.beetles.Count==a.quantity,"活体附件无效");
                else Require(CampaignItems.Known(a.itemId)&&(long)InventoryCount(a.itemId)+a.quantity<=1000000,"仓库物品或数量无效");
            }
            foreach(var a in m.attachments)
            {
                if(a.itemId==CampaignItems.Beetle){State.storedBeetles.AddRange(a.beetles);a.beetles.Clear();}
                else if(a.itemId==CampaignItems.Sap)State.sap+=a.quantity;
                else if(a.itemId==CampaignItems.Lamp)State.lamp+=a.quantity;
                else
                {
                    var stack=State.inventory.FirstOrDefault(x=>x.itemId==a.itemId);
                    if(stack==null){stack=new CampaignInventoryStack {itemId=a.itemId};State.inventory.Add(stack);}
                    stack.quantity+=a.quantity;
                }
            }
            m.claimed=true;m.read=true;
            Tell(string.Join("、",m.attachments.Select(a=>a.name+" ×"+a.quantity))+"已加入仓库");
        }
        void ConsumeItem(string id)
        {
            Require(InventoryCount(id)>0,"仓库没有"+CampaignItems.Name(id)+"，请先从邮箱领取。");
            var stack=State.inventory.First(x=>x.itemId==id);stack.quantity--;if(stack.quantity==0)State.inventory.Remove(stack);
        }
        public void HouseStoredBeetle(string id)
        {
            var b=State.storedBeetles.FirstOrDefault(x=>x.id==id);Require(b!=null,"仓库中没有这只甲虫");Space(1);
            State.storedBeetles.Remove(b);Add(b);Tell(b.name+"已从仓库入住温室。");
        }
        public void InstallCabinetUpgrade()
        {
            Require(State.cabinet==3,"展柜已满级");ConsumeItem(CampaignItems.Cabinet);State.cabinet=6;Tell("挂牌展柜扩展到6格");
        }
        void ValidateMailbox()
        {
            Require(State.mailboxVersion==1&&State.mail!=null&&State.inventory!=null&&State.storedBeetles!=null,"邮箱或仓库数据缺失");
            var ids=new HashSet<string>();
            foreach(var m in State.mail)
            {
                Require(m!=null&&!string.IsNullOrWhiteSpace(m.id)&&ids.Add(m.id)&&CampaignMailContacts.Find(m.senderId)!=null&&m.week>0&&m.week<=State.week&&!string.IsNullOrEmpty(m.subject)&&m.body!=null&&m.attachments!=null,"邮件数据无效");
                var items=new HashSet<string>();
                foreach(var a in m.attachments)
                {
                    Require(a!=null&&!string.IsNullOrWhiteSpace(a.name)&&items.Add(a.itemId)&&a.quantity>0&&a.quantity<=1000000&&a.beetles!=null,"邮件附件无效");
                    Require(a.itemId==CampaignItems.Beetle?a.beetles.Count==(m.claimed?0:a.quantity):CampaignItems.Known(a.itemId)&&a.beetles.Count==0,"附件内容无效");
                }
            }
            ids.Clear();
            foreach(var item in State.inventory)Require(item!=null&&CampaignItems.Known(item.itemId)&&item.itemId!=CampaignItems.Sap&&item.itemId!=CampaignItems.Lamp&&ids.Add(item.itemId)&&item.quantity>0&&item.quantity<=1000000,"仓库记录无效");
        }
        IEnumerable<CampaignBeetle> MailBeetles()=>State.mail.SelectMany(m=>m.attachments).SelectMany(a=>a.beetles);
    }
}
