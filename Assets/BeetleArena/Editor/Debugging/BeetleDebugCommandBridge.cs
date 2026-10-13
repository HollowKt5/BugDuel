using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace BeetleArena.EditorTools
{
    /// <summary>AI命令文件桥：写临时文件后原子重命名为request，读匹配id的response；同一时间只处理一个请求。</summary>
    [InitializeOnLoad]
    public static class BeetleDebugCommandBridge
    {
        static double nextPoll;
        static BeetleDebugCommandBridge()=>EditorApplication.update+=Poll;
        static void Poll()
        {
            if(EditorApplication.timeSinceStartup<nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating)return;
            nextPoll=EditorApplication.timeSinceStartup+.25;
            const string request="Temp/BugDuelDebug.request.json",response="Temp/BugDuelDebug.response.json";
            if(!File.Exists(request))return;
            string json;
            try{json=File.ReadAllText(request);JObject.Parse(json);File.Delete(request);}
            catch(IOException){return;}
            catch(Exception error){File.Delete(request);File.WriteAllText(response,new JObject{{"ok",false},{"error",error.Message}}.ToString());return;}
            string result=BeetleDebugWorkbenchService.ExecuteJson(json);
            File.WriteAllText(response+".tmp",result);
            if(File.Exists(response))File.Replace(response+".tmp",response,null);else File.Move(response+".tmp",response);
        }
    }
}
