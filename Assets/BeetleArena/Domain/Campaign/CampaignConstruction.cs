using System;
using System.Collections.Generic;
using System.Linq;

namespace BugDuel.Campaign
{
    [Serializable] public sealed class CampaignDecorationPlacement
    {
        public string id, beetleId, itemId;
        // The lower-left cell of the rotated footprint; rotation is 0, 90, 180 or 270 degrees.
        public int x, z, rotation;
    }

    public sealed class CampaignDecorationDefinition
    {
        public readonly string id, name, description;
        public readonly long price;
        public readonly int width, depth;
        public CampaignDecorationDefinition(string id,string name,long price,int width,int depth,string description)
        {this.id=id;this.name=name;this.price=price;this.width=width;this.depth=depth;this.description=description;}
    }

    public static class CampaignDecorations
    {
        public const int Columns=12, Rows=10;
        public const string Stones="decor-stones", Fern="decor-fern", Driftwood="decor-driftwood", Moss="decor-moss",
            Water="decor-water", Arch="decor-arch", Leaf="decor-leaf", Lamp="decor-lamp";
        public static readonly IReadOnlyList<CampaignDecorationDefinition> All=Array.AsReadOnly(new[] {
            new CampaignDecorationDefinition(Stones,"卵石组",100,1,1,"一组圆润的小石头，为虫箱增添自然层次。"),
            new CampaignDecorationDefinition(Fern,"蕨叶",70,1,1,"小巧的蕨叶景观，适合布置在虫箱边缘。"),
            new CampaignDecorationDefinition(Driftwood,"枯木",140,3,1,"横卧的枯木，可旋转后沿边缘摆放。"),
            new CampaignDecorationDefinition(Moss,"苔藓垫",60,1,1,"柔和的绿色苔藓景观。"),
            new CampaignDecorationDefinition(Water,"饮水盆",90,2,1,"浅水景观盆；不替代食物，也不改变饱食。"),
            new CampaignDecorationDefinition(Arch,"拱木",200,3,2,"留有拱洞的木质景观，适合较宽敞的箱角。"),
            new CampaignDecorationDefinition(Leaf,"枯叶堆",40,1,1,"一小堆暖色落叶，点缀底材。"),
            new CampaignDecorationDefinition(Lamp,"景观灯",120,1,1,"暖色小灯，只作景观，不改变温控或成长速度。") });
        public static CampaignDecorationDefinition Find(string id) => All.FirstOrDefault(x=>x.id==id);
        public static CampaignDecorationDefinition Get(string id) => Find(id)??throw new CampaignRuleException("景观用品不存在");
        public static bool Known(string id) => Find(id)!=null;
        public static string Name(string id) => Get(id).name;
        public static long Price(string id) => Get(id).price;
        public static bool ValidRotation(int rotation) => rotation==0||rotation==90||rotation==180||rotation==270;
        public static int Width(string id,int rotation=0) => rotation==90||rotation==270?Get(id).depth:Get(id).width;
        public static int Depth(string id,int rotation=0) => rotation==90||rotation==270?Get(id).width:Get(id).depth;
        public static bool IsReservedCell(int x,int z)
        {
            if(x<=0||x>=Columns-1||z<=0||z>=Rows-1)return true;
            // All life stages share the same safe footprint, including the circular larval vessel.
            double u=Math.Max(Math.Abs((double)x/Columns-.5),Math.Abs((double)(x+1)/Columns-.5))*2;
            double v=Math.Max(Math.Abs((double)z/Rows-.5),Math.Abs((double)(z+1)/Rows-.5))*2;
            return u*u+v*v>1;
        }
    }

    public sealed partial class CampaignSession
    {
        void UpgradeConstruction()
        {
            if(State.constructionVersion!=0)return;
            State.decorations=State.decorations??new List<CampaignDecorationPlacement>();
            foreach(string item in new[]{CampaignDecorations.Stones,CampaignDecorations.Fern,CampaignDecorations.Moss,CampaignDecorations.Driftwood})AddDecorationInventory(item,1);
            State.constructionVersion=1;
        }
        void AddDecorationInventory(string itemId,int quantity)
        {
            var stack=State.inventory.FirstOrDefault(x=>x.itemId==itemId);
            if(stack==null){stack=new CampaignInventoryStack {itemId=itemId};State.inventory.Add(stack);}
            stack.quantity+=quantity;
        }
        long OwnedSupplyCount(string itemId) => (long)InventoryCount(itemId)+State.decorations.Count(x=>x.itemId==itemId);
        public CampaignDecorationPlacement Decoration(string beetleId,string id) => State.decorations.FirstOrDefault(x=>x.id==id&&x.beetleId==beetleId)??throw new CampaignRuleException("这个养殖箱中没有该景观");

        // Preview queries only inspect ownership and geometry. Inventory is consumed by PlaceDecoration.
        public string DecorationPlacementProblem(string beetleId,string itemId,int x,int z,int rotation,string ignoreId=null)
        {
            string problem=DecorationGeometryProblem(beetleId,itemId,x,z,rotation,ignoreId);
            if(problem!=null)return problem;
            var space=DecorationSpace(beetleId,itemId,x,z,rotation,ignoreId);
            return space.HasLivingSpace?null:"生活空间不足：需保留至少30格相连空地与4×4格落脚区";
        }
        string DecorationGeometryProblem(string beetleId,string itemId,int x,int z,int rotation,string ignoreId)
        {
            if(!State.beetles.Concat(State.storedBeetles).Any(b=>b.id==beetleId))return "只能布置自己的甲虫养殖箱";
            var item=CampaignDecorations.Find(itemId);if(item==null)return "景观用品不存在";
            if(!CampaignDecorations.ValidRotation(rotation))return "旋转角度只能为0、90、180或270度";
            if(ignoreId!=null&&!State.decorations.Any(p=>p.id==ignoreId&&p.beetleId==beetleId&&p.itemId==itemId))return "移动的景观不属于这个养殖箱";
            int width=CampaignDecorations.Width(itemId,rotation),depth=CampaignDecorations.Depth(itemId,rotation);
            if(x<0||z<0||x>CampaignDecorations.Columns-width||z>CampaignDecorations.Rows-depth)return "景观超出养殖箱边界";
            for(int column=x;column<x+width;column++)for(int row=z;row<z+depth;row++)
                if(CampaignDecorations.IsReservedCell(column,row))return "景观不能越过箱沿或圆形容器边界";
            foreach(var placed in State.decorations.Where(p=>p.beetleId==beetleId&&p.id!=ignoreId))
                if(x<placed.x+CampaignDecorations.Width(placed.itemId,placed.rotation)&&x+width>placed.x&&
                   z<placed.z+CampaignDecorations.Depth(placed.itemId,placed.rotation)&&z+depth>placed.z)return "这里已有其他景观";
            return null;
        }
        public CampaignConstructionSpace DecorationSpace(string beetleId,string itemId=null,int x=0,int z=0,int rotation=0,string ignoreId=null)
        {
            var records=State.decorations.Where(p=>p.beetleId==beetleId&&p.id!=ignoreId);
            return new CampaignConstructionSpace(itemId==null?records:records.Concat(new[]{new CampaignDecorationPlacement {itemId=itemId,x=x,z=z,rotation=rotation}}));
        }
        public bool CanPlaceDecoration(string beetleId,string itemId,int x,int z,int rotation,string ignoreId=null) => DecorationPlacementProblem(beetleId,itemId,x,z,rotation,ignoreId)==null;
        public string PlaceDecoration(string beetleId,string itemId,int x,int z,int rotation)
        {
            string problem=DecorationPlacementProblem(beetleId,itemId,x,z,rotation);Require(problem==null,problem);
            ConsumeItem(itemId);
            var placed=new CampaignDecorationPlacement {id=Id(),beetleId=beetleId,itemId=itemId,x=x,z=z,rotation=rotation};State.decorations.Add(placed);
            Tell(CampaignDecorations.Name(itemId)+"已放入养殖箱。");return placed.id;
        }
        public void MoveDecoration(string beetleId,string id,int x,int z,int rotation)
        {
            var placed=Decoration(beetleId,id);string problem=DecorationPlacementProblem(beetleId,placed.itemId,x,z,rotation,id);Require(problem==null,problem);
            placed.x=x;placed.z=z;placed.rotation=rotation;Tell("景观位置已保存。");
        }
        public void ReturnDecoration(string beetleId,string id)
        {
            var placed=Decoration(beetleId,id);AddDecorationInventory(placed.itemId,1);State.decorations.Remove(placed);
            Tell(CampaignDecorations.Name(placed.itemId)+"已收回仓库。");
        }
        public void SellDecoration(string beetleId,string id)
        {
            var placed=Decoration(beetleId,id);long price=SupplySellPrice(placed.itemId);State.decorations.Remove(placed);Earn(price);
            Tell(CampaignDecorations.Name(placed.itemId)+"已售出，收到 "+price+" G。");
        }
        void ReclaimDecorations(string beetleId)
        {
            foreach(var group in State.decorations.Where(p=>p.beetleId==beetleId).GroupBy(p=>p.itemId))AddDecorationInventory(group.Key,group.Count());
            State.decorations.RemoveAll(p=>p.beetleId==beetleId);
        }
        void ValidateConstruction()
        {
            RequireData(State.constructionVersion==1&&State.decorations!=null,"养殖箱景观数据缺失");var ids=new HashSet<string>();
            foreach(var placed in State.decorations)
                RequireData(placed!=null&&!string.IsNullOrWhiteSpace(placed.id)&&ids.Add(placed.id)&&CampaignDecorations.Known(placed.itemId)&&CampaignDecorations.ValidRotation(placed.rotation),"景观记录无效");
            foreach(var placed in State.decorations)
                RequireData(DecorationGeometryProblem(placed.beetleId,placed.itemId,placed.x,placed.z,placed.rotation,placed.id)==null,"景观归属、占位或保留区域无效");
            foreach(string beetleId in State.decorations.Select(p=>p.beetleId).Distinct())
                RequireData(DecorationSpace(beetleId).HasLivingSpace,"景观归属、占位或保留区域无效");
            foreach(var item in CampaignDecorations.All)RequireData(OwnedSupplyCount(item.id)<=1000000,"景观库存与已摆放数量超出上限");
        }
    }
}
