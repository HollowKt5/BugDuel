using System;
using System.IO;
using System.Threading.Tasks;
using BugDuel.Campaign;
using UnityEngine;

namespace BeetleArena.Campaign
{
    [Serializable] public sealed class CampaignSave
    {
        public int version = 1;
        public string savedAt, thumbnail;
        public CampaignState state;
        public CampaignCatalog catalog;
        public bool hasBattle, hasAuction, hasPupaAuction;
    }

    // Callers supply detached snapshots; the worker never reads live state or Unity objects.
    public sealed class CampaignSaveRepository
    {
        public string DirectoryPath { get; }
        public CampaignSaveRepository(string directory) { DirectoryPath=directory; Directory.CreateDirectory(directory); }
        public string SlotPath(int slot)
        {
            if(slot<0 || slot>3) throw new ArgumentOutOfRangeException(nameof(slot));
            return Path.Combine(DirectoryPath,"slot-"+slot+".json");
        }
        public CampaignSave Read(int slot)
        {
            string path=SlotPath(slot); if(!File.Exists(path)) return null;
            if(new FileInfo(path).Length>8000000) throw new CampaignDataException("存档过大");
            var save=CampaignJson.Read<CampaignSave>(path);
            if(save.version!=1 || save.state==null || save.catalog==null) throw new CampaignDataException("不支持或损坏的存档");
            // Unity inline serialization does not preserve absent optional objects.
            if(!save.hasBattle)save.state.battle=null;
            if(!save.hasAuction)save.state.auction=null;
            if(!save.hasPupaAuction)save.state.pupaAuction=null;
            if(save.state.beetles!=null)foreach(var b in save.state.beetles)RestoreMeal(b);
            if(save.state.stock!=null)foreach(var b in save.state.stock)RestoreMeal(b);
            if(save.state.storedBeetles!=null)foreach(var b in save.state.storedBeetles)RestoreMeal(b);
            if(save.state.specimens!=null)foreach(var s in save.state.specimens)RestoreMeal(s.beetle);
            if(save.state.specimenStock!=null)foreach(var s in save.state.specimenStock)RestoreMeal(s.beetle);
            if(save.state.marketAuctions!=null)foreach(var a in save.state.marketAuctions){RestoreMeal(a.Beetle);RestoreMeal(a.Specimen?.beetle);}
            if(save.state.mail!=null)foreach(var m in save.state.mail)if(m?.attachments!=null)foreach(var a in m.attachments)
            {
                if(a?.beetles!=null)foreach(var b in a.beetles)RestoreMeal(b);
                if(a?.specimens!=null)foreach(var s in a.specimens)RestoreMeal(s.beetle);
            }
            RestoreMeal(save.state.battle?.opponent);RestoreMeal(save.state.pupaAuction?.beetle);
            new CampaignSession(save.state,save.catalog).Validate(); return save;
        }
        static void RestoreMeal(CampaignBeetle b) { if(b!=null&&!b.hasMeal)b.meal=null; }
        public Task WriteAsync(int slot,CampaignSave snapshot)
        {
            string path=SlotPath(slot); return Task.Run(()=>AtomicWrite(path,JsonUtility.ToJson(snapshot)));
        }
        public void Write(int slot,CampaignSave snapshot) => AtomicWrite(SlotPath(slot),JsonUtility.ToJson(snapshot));
        public void Delete(int slot)
        {
            string path=SlotPath(slot); if(File.Exists(path))File.Delete(path); if(File.Exists(path+".bak"))File.Delete(path+".bak");
        }
        public static void AtomicWrite(string path,string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)); string temp=path+".tmp";
            try
            {
                using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None))
                { var bytes=System.Text.Encoding.UTF8.GetBytes(text); stream.Write(bytes,0,bytes.Length); stream.Flush(true); }
                if(File.Exists(path))File.Replace(temp,path,path+".bak"); else File.Move(temp,path);
            }
            finally { if(File.Exists(temp))File.Delete(temp); }
        }
    }

    // JsonUtility reports malformed external JSON as ArgumentException. Classify it
    // here, never by catching ArgumentException around a complete game operation.
    internal static class CampaignJson
    {
        public static T Read<T>(string path) where T : class
        {
            string json=File.ReadAllText(path);
            T data;
            try { data=JsonUtility.FromJson<T>(json); }
            catch(ArgumentException error) { throw new CampaignDataException("JSON 格式无效",error); }
            return data ?? throw new CampaignDataException("配置或存档内容为空");
        }
    }
}
