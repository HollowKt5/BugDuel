using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BeetleArena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BeetleFeedbackValidation
{
    const string Folder = "Artifacts/ContactFeedback";
    static void Require(bool value, string why) { if (!value) throw new Exception(why); }
    [MenuItem("Tools/Beetle Arena/Validate Contact Feedback")]
    public static void Run()
    {
        if(EditorApplication.isPlaying) throw new Exception("Stop Play before validation");
        Directory.CreateDirectory(Folder);
        using var fixture = new BeetleValidationScene("FeedbackValidation");
        var report=new StringBuilder("CONTACT FEEDBACK VALIDATION\n"+DateTimeOffset.Now.ToString("o")+"\n");
        bool hadPreference=PlayerPrefs.HasKey(HornClashSparks.PreferenceKey);
        int oldPreference=PlayerPrefs.GetInt(HornClashSparks.PreferenceKey,1);
        try
        {
            var m=fixture.Match; var f=m.contactFeedback; var sparks=f.sparks;
            m.audioFeedback.volume=0; m.hud.Bind(m); sparks.Initialize();
            Require(!ShaderUtil.ShaderHasError(sparks.sparkMaterial.shader),"Spark shader compile error");
            // 实际 UI 监听/偏好写入/关闭清除均走组件入口，而非直接改布尔字段。
            var ui=m.hud.GetComponent<BeetleFeedbackSettings>();
            typeof(BeetleFeedbackSettings).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(ui,null);
            ui.openButton.onClick.Invoke(); Require(ui.panel.activeSelf,"Settings button failed");
            ui.sparksToggle.isOn=true; sparks.SetEnabled(true,false);
            sparks.Burst(new Vector3(0,1,0),Vector3.right,.8f); Require(sparks.LiveParticles>0,"Burst emitted nothing");
            ui.sparksToggle.isOn=false; Require(!sparks.SparksEnabled && sparks.LiveParticles==0,"Toggle did not clear sparks");
            int count=sparks.BurstCount; sparks.Burst(Vector3.zero,Vector3.right,1);
            Require(count==sparks.BurstCount && PlayerPrefs.GetInt(HornClashSparks.PreferenceKey,1)==0,"Off not suppressed/persisted");
            // 新组件重新读偏好，模拟重开时的持久化读取。
            var testGo=new GameObject("Preference reload test");
            var reloaded=testGo.AddComponent<HornClashSparks>(); reloaded.sparkMaterial=sparks.sparkMaterial;
            Require(!reloaded.SparksEnabled,"Preference did not survive new instance"); reloaded.ReleaseResources(); UnityEngine.Object.DestroyImmediate(testGo);
            ui.sparksToggle.isOn=true; Require(sparks.SparksEnabled,"Toggle on failed");
            Shot(m,"Settings.png",false);
            ui.closeButton.onClick.Invoke(); Require(!ui.panel.activeSelf,"Settings close failed");
            report.AppendLine("PASS: settings open/close, toggle on/off, immediate clear, suppressed emission, preference reload.");
            for(int arena=0;arena<2;arena++)
            {
                m.SelectArena(arena); m.StartMatch(71); int beforeSounds=m.audioFeedback.PlayedContacts, beforeSparks=sparks.BurstCount;
                int captured=0;
                for(int tick=0;tick<3000 && m.Simulation.Phase!=MatchPhase.Finished;tick++)
                {
                    foreach(var actor in m.actors) actor.BeforeStep(); m.Simulation.Step(.02f);
                    var a=m.Simulation.Fighters[0]; var b=m.Simulation.Fighters[1];
                    Vector2 p=a.Position,q=b.Position,v=a.Velocity,w=b.Velocity;
                    float balance=a.Balance,elapsed=m.Simulation.Elapsed;
                    var random=UnityEngine.Random.state; int bursts=sparks.BurstCount;
                    f.Tick(m.Simulation,.02f);
                    Require(a.Position==p && b.Position==q && a.Velocity==v && b.Velocity==w && a.Balance==balance && m.Simulation.Elapsed==elapsed && UnityEngine.Random.state.Equals(random),"Presentation changed simulation/random");
                    if(sparks.BurstCount>bursts && captured==0)
                    { sparks.Tick(.055f); Shot(m,"RealHornClash-"+arena+".png",true); captured++; }
                }
                int sounds=m.audioFeedback.PlayedContacts-beforeSounds, burstsNow=sparks.BurstCount-beforeSparks;
                report.AppendLine($"arena={arena}, contacts={f.ObservedContacts}, sounds={sounds}, sparkBursts={burstsNow}, elapsed={m.Simulation.Elapsed:F2}");
                Require(sounds>0 && burstsNow>0,"Natural horn contacts never produced feedback on arena "+arena);
            }
            // 已分离且固定步继续时不能凭空播放；暂停 Tick(0) 也不能推进粒子。
            m.StartMatch(71); int soundStart=m.audioFeedback.PlayedContacts, sparkStart=sparks.BurstCount;
            for(int i=0;i<25;i++) f.Tick(m.Simulation,.02f);
            Require(soundStart==m.audioFeedback.PlayedContacts && sparkStart==sparks.BurstCount,"Separated/countdown fighters emit feedback");
            sparks.Burst(Vector3.zero,Vector3.right,1); int live=sparks.LiveParticles; f.Tick(m.Simulation,0);
            Require(live==sparks.LiveParticles,"Zero delta advances sparks"); sparks.Tick(.5f); Require(sparks.LiveParticles==0,"Sparks do not expire");
            for(int variant=0;variant<6;variant++)
            {
                var data=ArenaAudio.Synthesize(variant,false);
                Require(data.All(x=>!float.IsNaN(x) && Mathf.Abs(x)<=.73f),"Invalid/clipped sound");
                Require(Mathf.Abs(data[0])<.00001f && Mathf.Abs(data[data.Length-1])<.001f,"Audio edge discontinuity");
            }
            Wave(Folder+"/WoodyContactPreview.wav");
            report.AppendLine("PASS: no-contact silence, simulation/random isolation, zero-delta pause, spark expiry, finite/clipping-free synthesized samples.");
            report.AppendLine("ALL CONTACT FEEDBACK CHECKS PASSED; subjective sound/visual acceptance remains with user.");
        }
        finally
        {
            if(hadPreference) PlayerPrefs.SetInt(HornClashSparks.PreferenceKey,oldPreference); else PlayerPrefs.DeleteKey(HornClashSparks.PreferenceKey);
            PlayerPrefs.Save(); File.WriteAllText(Folder+"/Validation.txt",report.ToString());
        }
    }
    static void Shot(BeetleMatchController m,string name,bool closeup)
    {
        var camera=m.spectatorCamera.GetComponent<Camera>(); var oldP=camera.transform.position; var oldQ=camera.transform.rotation;
        float oldSize=camera.orthographicSize; int oldMask=camera.cullingMask; bool oldHud=m.hud.gameObject.activeSelf;
        var objects=m.gameObject.scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Select(t=>t.gameObject).ToArray();
        var layers=objects.Select(g=>g.layer).ToArray();
        var target=new RenderTexture(1600,900,24); var pixels=new Texture2D(1600,900,TextureFormat.RGB24,false);
        var oldTarget=camera.targetTexture; var oldActive=RenderTexture.active;
        try
        {
            foreach(var g in objects) g.layer=31; camera.cullingMask=1<<31;
            if(closeup) { m.hud.gameObject.SetActive(false); var p=m.contactFeedback.LastContactPoint; camera.transform.position=p+new Vector3(1.2f,2,6); camera.transform.LookAt(p); camera.orthographicSize=1.8f; }
            Canvas.ForceUpdateCanvases(); camera.targetTexture=target; camera.Render(); RenderTexture.active=target;
            pixels.ReadPixels(new Rect(0,0,1600,900),0,0); pixels.Apply(); File.WriteAllBytes(Folder+"/"+name,pixels.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture=oldTarget; RenderTexture.active=oldActive; camera.cullingMask=oldMask;
            camera.transform.SetPositionAndRotation(oldP,oldQ); camera.orthographicSize=oldSize; m.hud.gameObject.SetActive(oldHud);
            for(int i=0;i<objects.Length;i++) objects[i].layer=layers[i];
            UnityEngine.Object.DestroyImmediate(pixels); UnityEngine.Object.DestroyImmediate(target);
        }
    }
    static void Wave(string path)
    {
        const int rate=44100; float[] result=new float[rate*4];
        for(int i=0;i<5;i++)
        {
            var data=ArenaAudio.Synthesize(i%3,i==4); int start=(int)((.2f+i*.65f)*rate);
            for(int j=0;j<data.Length;j++) result[start+j]=data[j]*(i==4?.4f:.6f);
        }
        using(var writer=new BinaryWriter(File.Create(path)))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36+result.Length*2); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(rate);writer.Write(rate*2);writer.Write((short)2);writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(result.Length*2);foreach(float v in result) writer.Write((short)(Mathf.Clamp(v,-1,1)*32767));
        }
    }
}
