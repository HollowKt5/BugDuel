using System;
using System.IO;
using System.Reflection;
using System.Text;
using BeetleArena.Campaign;
using BugDuel.Campaign;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CampaignSaveCommitValidation
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    const string Output="Artifacts/SaveCommit";
    static readonly FieldInfo Thumbnail=typeof(CampaignApp).GetField("latestThumbnail",Private);
    static readonly MethodInfo FinishCommit=typeof(CampaignApp).GetMethod("FinishCommit",Private);

    [MenuItem("Tools/甲壳虫斗/验证/存档与数据/存档提交隔离", false, 662)]
    public static void Run()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before isolated save validation.");
        Directory.CreateDirectory(Output);
        string folder=Path.GetFullPath(Path.Combine(Output,"Save-"+Guid.NewGuid().ToString("N")));
        var repository=new CampaignSaveRepository(folder);
        var report=new StringBuilder("SAVE COMMIT ISOLATION\n"+DateTimeOffset.Now.ToString("o")+"\n"+folder+"\n");
        var previous=SceneManager.GetActiveScene();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            // An inactive host does not initialize the live app, audio, input or player save directory.
            var host=new GameObject("Isolated save commit app");host.SetActive(false);
            var app=host.AddComponent<CampaignApp>();
            typeof(CampaignApp).GetField("repository",Private).SetValue(app,repository);
            var first=Session("Save A",501);var second=Session("Save B",502);
            const string firstImage="thumbnail-A",secondImage="thumbnail-B";
            typeof(CampaignApp).GetProperty("Session").SetValue(app,first);
            Thumbnail.SetValue(app,firstImage);
            repository.Write(0,Snapshot(first,firstImage));repository.Write(1,Snapshot(second,secondImage));
            int changes=0;app.Changed+=()=>changes++;

            using(var locked=new FileStream(repository.SlotPath(0)+".tmp",FileMode.Create,FileAccess.ReadWrite,FileShare.None))
            {
                app.LoadSlot(1);
                Check(app.Busy&&ReferenceEquals(app.Session,first)&&(string)Thumbnail.GetValue(app)==firstImage,
                    "an accepted load keeps the active session and thumbnail unchanged before commit",report);
                // Wait for the real disk write but omit navigation: this fixture tests storage, not scene transitions.
                FinishCommit.Invoke(app,new object[]{false});
                Check(!app.Busy&&ReferenceEquals(app.Session,first)&&(string)Thumbnail.GetValue(app)==firstImage&&changes==0&&app.Message.StartsWith("保存失败",StringComparison.Ordinal),
                    "a locked temporary file rejects the load without publishing its session, image or change event",report);
                var auto=repository.Read(0);
                Check(auto.state.playerName==first.State.playerName&&auto.thumbnail==firstImage,
                    "the failed load also preserves the existing automatic save",report);
            }

            Check(app.SaveSlot(2),"a manual save succeeds after the lock is released",report);
            var manual=repository.Read(2);
            Check(manual.state.playerName==first.State.playerName&&manual.thumbnail==firstImage,
                "saving outside the greenhouse after a failed load retains A's matching image",report);

            app.LoadSlot(1);FinishCommit.Invoke(app,new object[]{false});
            var loaded=repository.Read(0);
            Check(!app.Busy&&app.Session.State.playerName==second.State.playerName&&(string)Thumbnail.GetValue(app)==secondImage&&changes==1,
                "a successful load publishes B and its thumbnail together",report);
            Check(loaded.state.playerName==second.State.playerName&&loaded.thumbnail==secondImage,
                "the successful load writes B's image into the automatic slot",report);

            repository.Write(1,Snapshot(second,null));app.LoadSlot(1);FinishCommit.Invoke(app,new object[]{false});
            Check(!app.Busy&&string.IsNullOrEmpty((string)Thumbnail.GetValue(app))&&string.IsNullOrEmpty(repository.Read(0).thumbnail),
                "loading an older save without an image clears the prior journey image",report);
            report.AppendLine("ALL SAVE COMMIT CHECKS PASSED");
        }
        finally
        {
            if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(scene,true);
            File.WriteAllText(Output+"/Validation.txt",report.ToString());
        }
        Debug.Log(report.ToString());
    }
    static CampaignSession Session(string name,uint seed)
    {
        var session=CampaignSession.New(CampaignCatalog.Defaults(),seed);session.RenamePlayer(name);session.Validate();return session;
    }
    static CampaignSave Snapshot(CampaignSession session,string thumbnail)=>new CampaignSave
    {state=CampaignSnapshots.Copy(session.State),catalog=session.Catalog,thumbnail=thumbnail};
    static void Check(bool condition,string message,StringBuilder report)
    {
        if(!condition)throw new InvalidOperationException(message);
        report.AppendLine("PASS "+message);
    }
}
