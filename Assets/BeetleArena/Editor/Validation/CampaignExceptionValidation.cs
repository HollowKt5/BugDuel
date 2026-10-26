using System;
using System.Collections.Generic;
using BeetleArena.Campaign;
using BugDuel.Campaign;
using UnityEditor;
using UnityEngine;

// Exercises the real transaction boundary without opening scenes or writing player saves.
public static class CampaignExceptionValidation
{
    sealed class CapturedLogs : ILogHandler
    {
        public readonly List<Exception> Exceptions=new List<Exception>();
        public int OtherLogs;
        public void LogException(Exception exception,UnityEngine.Object context) => Exceptions.Add(exception);
        public void LogFormat(LogType type,UnityEngine.Object context,string format,params object[] args) => OtherLogs++;
    }

    [MenuItem("Tools/甲壳虫斗/验证/战役流程/异常恢复", false, 572)]
    public static void Run()
    {
        var owner=new GameObject("Campaign exception validation") { hideFlags=HideFlags.HideAndDontSave };
        var captured=new CapturedLogs();
        var previous=Debug.unityLogger.logHandler;
        try
        {
            owner.SetActive(false); // Awake must not create storage, inputs, or a singleton.
            var app=owner.AddComponent<CampaignApp>();
            var original=CampaignSession.New(CampaignCatalog.Defaults(),7163);
            original.RenamePlayer("验收玩家");
            typeof(CampaignApp).GetProperty(nameof(CampaignApp.Session)).GetSetMethod(true).Invoke(app,new object[]{original});
            string before=JsonUtility.ToJson(original.State);
            Debug.unityLogger.logHandler=captured;
            Check(!app.Execute(s=>s.Spend(long.MaxValue)),"rule refusal rejects command");
            Check(app.Message=="金币不足"&&captured.Exceptions.Count==0&&captured.OtherLogs==0,"expected rule stays a player message");

            foreach(var error in new Exception[] { new NullReferenceException("private-null-detail"),new InvalidOperationException("private-operation-detail"),new ArgumentException("private-argument-detail") })
            {
                int count=captured.Exceptions.Count;
                Check(!app.Execute(s=>throw error),"unexpected exception rejects command");
                Check(captured.Exceptions.Count==count+1&&ReferenceEquals(captured.Exceptions[count],error)&&!string.IsNullOrEmpty(error.StackTrace),"original exception and full stack reach LogException");
                Check(!app.Message.Contains(error.Message)&&app.Message.Contains("内部错误"),"implementation details stay out of player message");
            }
            int recorded=captured.Exceptions.Count;
            Check(!app.Execute(s=>s.Earn(-1))&&captured.Exceptions.Count==recorded+1&&captured.Exceptions[recorded] is ArgumentOutOfRangeException,"negative internal money is not an insufficient-funds rule");
            recorded=captured.Exceptions.Count;
            Check(!app.Execute(s=>s.State.coins=-1),"invalid post-command state rejects transaction");
            Check(captured.Exceptions.Count==recorded+1&&captured.Exceptions[recorded] is CampaignDataException,"transaction invariant failure is logged as a code defect");
            Check(ReferenceEquals(app.Session,original)&&JsonUtility.ToJson(original.State)==before&&!app.Busy,"rejected commands preserve original state and queue no save");
        }
        finally
        {
            Debug.unityLogger.logHandler=previous;
            UnityEngine.Object.DestroyImmediate(owner);
        }
        Debug.Log("Campaign exception contracts PASS: business refusal, original exception/stack, safe player message, invalid arguments, post-command invariants, rollback.");
    }

    static void Check(bool valid,string message) { if(!valid)throw new InvalidOperationException("Campaign exception contract failed: "+message); }
}
