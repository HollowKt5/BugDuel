using System;
using System.Reflection;
using BeetleArena;
using BeetleArena.Campaign;
using BeetleArena.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.UIElements;

public static class WorldToonValidation
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    [MenuItem("Tools/甲壳虫斗/验证/界面与画面/统一卡通层逻辑",false,609)]
    public static void Run()=>Debug.Log(CheckLogic());

    public static string CheckLogic()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before toon logic validation.");
        int checks=0;
        void Check(bool ok,string message)
        {if(!ok)throw new InvalidOperationException(message);checks++;}
        var shader=Resources.Load<Shader>("BeetleWorldToon");
        Check(shader&&shader.isSupported&&!ShaderUtil.ShaderHasError(shader),"Toon shader must compile and be included in Resources.");
        Check(!WorldDisplayEditorService.IsScreenShader(shader),"The additive layer must not replace a selected display filter.");
        var tree=new VisualElement();
        AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/BeetleArena/Editor/Presentation/WorldDisplayWindow.uxml").CloneTree(tree);
        Check(tree.Q<UnityEngine.UIElements.Button>("inspect-toon")!=null,"Shared settings entry must exist.");
        var profile=BeetleDisplayPreferences.Profile;
        Check(profile&&profile.toon!=null,"Shared configuration must be available in builds.");
        foreach(var scenePath in WorldDisplayEditorService.ScenePaths())
        {
            string sceneText=System.IO.File.ReadAllText(scenePath);
            Check(sceneText.Contains("BeetleArena.Campaign.CampaignBackdrop")||sceneText.Contains("m_TagString: MainCamera"),
                "Scene must provide a campaign backdrop or standalone main camera: "+scenePath);
        }

        var original=profile.toon;
        var settings=new WorldToonSettings();
        var scene=EditorSceneManager.NewPreviewScene();
        var active=RenderTexture.active;
        var source=Texture(64,48);var resized=Texture(83,57);
        var crt=ScriptableObject.CreateInstance<BeetleCrtProfile>();
        var fixture=new SceneDisplaySetting{sceneName="ToonLogic-"+Guid.NewGuid().ToString("N")};
        CampaignBackdrop backdrop=null;BeetleCombatDisplay standalone=null;
        using(var layer=new WorldToonLayer())
        try
        {
            profile.toon=settings;profile.scenes.Add(fixture);
            foreach(var style in new[]{BeetleDisplayStyle.Clean,BeetleDisplayStyle.Crt,BeetleDisplayStyle.OrderedDither,BeetleDisplayStyle.DenseDither,BeetleDisplayStyle.Custom})
            {
                fixture.style=style;settings.enabled=true;crt.effectEnabled=false;
                Check(BeetleCombatDisplay.RequiresSurface(fixture.sceneName,crt),"Toon must remain active with "+style);
                settings.enabled=false;
                Check(BeetleCombatDisplay.RequiresSurface(fixture.sceneName,crt)==BeetleDisplayPreferences.IsDither(style),"Disabled toon must respect the selected filter: "+style);
            }
            fixture.style=BeetleDisplayStyle.Crt;crt.effectEnabled=true;crt.intensity=1;
            Check(BeetleCombatDisplay.RequiresSurface(fixture.sceneName,crt),"CRT must still work without toon.");
            settings.enabled=true;
            RenderTexture.active=resized;
            var first=layer.Render(source,settings);
            Check(first&&first!=source&&first.width==64&&first.height==48&&first.depth==0,"Toon must own a separate color-only target.");
            Check(RenderTexture.active==resized,"Rendering must restore the active render target.");
            Check(layer.Render(source,settings)==first,"Steady frames must reuse the same allocation.");
            var second=layer.Render(resized,settings);
            Check(!first&&second.width==83&&second.height==57,"Resize must release the old target and match the world.");
            settings.strength=0;
            Check(layer.Render(source,settings)==source&&!second,"Zero strength must bypass and release the extra target.");
            Check(source.IsCreated()&&resized.IsCreated(),"The layer must not release source textures.");
            settings.strength=.65f;
            var third=layer.Render(source,settings);layer.Dispose();
            Check(!third&&source.IsCreated(),"Dispose must release only owned resources.");

            var host=new GameObject("Toon logic fixture");host.SetActive(false);SceneManager.MoveGameObjectToScene(host,scene);
            var camera=host.AddComponent<Camera>();
            backdrop=host.AddComponent<CampaignBackdrop>();backdrop.sceneCamera=camera;
            var world=Texture(64,48);camera.targetTexture=world;
            typeof(CampaignBackdrop).GetProperty("Frame").SetValue(backdrop,world);
            Set(backdrop,"width",64);Set(backdrop,"height",48);
            backdrop.blurShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/BeetleArena/Shaders/CampaignBlur.shader");
            backdrop.output=Image(host.transform);backdrop.previewOutput=Image(host.transform);
            Call(backdrop,"Rendered",default(ScriptableRenderContext),camera);
            Check(backdrop.Ready&&backdrop.WorldFrame!=world&&backdrop.output.texture==backdrop.WorldFrame,"Campaign world must display the processed frame.");
            Check(backdrop.Frame==world&&camera.targetTexture==world,"The world camera and save-thumbnail source must retain the original frame.");
            backdrop.SetUiFocus(true);Call(backdrop,"Rendered",default(ScriptableRenderContext),camera);
            Check(backdrop.output.texture!=backdrop.WorldFrame&&backdrop.previewOutput.texture==backdrop.WorldFrame,"Focused backgrounds must blur after toon; settings preview must remain sharp.");
            backdrop.SetUiFocus(false);
            Check(backdrop.output.texture==backdrop.WorldFrame,"Closing a modal must restore the processed world immediately.");
            settings.enabled=false;Call(backdrop,"Rendered",default(ScriptableRenderContext),camera);
            Check(backdrop.WorldFrame==world&&backdrop.previewOutput.texture==world,"Disabling toon must update the world and preview together.");
            settings.enabled=true;Call(backdrop,"Rendered",default(ScriptableRenderContext),camera);
            var campaignTarget=backdrop.WorldFrame;Call(backdrop,"OnDisable");
            Check(!campaignTarget&&backdrop.output.texture==world,"Disabling the backdrop must release toon resources and restore bindings.");
            Call(backdrop,"Release");

            standalone=host.AddComponent<BeetleCombatDisplay>();var display=Image(host.transform);
            var arenaFrame=Texture(64,48);camera.targetTexture=arenaFrame;
            Set(standalone,"source",camera);Set(standalone,"frame",arenaFrame);Set(standalone,"image",display);
            Call(standalone,"Rendered",default(ScriptableRenderContext),null);
            Check(display.texture==null,"Other cameras must not process the arena frame.");
            Call(standalone,"Rendered",default(ScriptableRenderContext),camera);
            Check(display.texture&&display.texture!=arenaFrame,"Standalone scenes must use the same toon layer.");
            var arenaTarget=display.texture;Call(standalone,"OnDisable");
            Check(!arenaTarget&&display.texture==null&&camera.targetTexture==null,"Standalone shutdown must release targets and unbind its camera.");
            Check(!ShaderUtil.ShaderHasError(shader),"Rendering must not produce shader compilation errors.");
            return "World toon logic: "+checks+" checks passed; no Play session or screenshots.";
        }
        finally
        {
            profile.toon=original;profile.scenes.Remove(fixture);
            RenderTexture.active=active;
            if(standalone)Call(standalone,"OnDisable");
            if(backdrop)Call(backdrop,"Release");
            EditorSceneManager.ClosePreviewScene(scene);
            UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(resized);UnityEngine.Object.DestroyImmediate(crt);
        }
    }

    static RenderTexture Texture(int width,int height)
    {
        var texture=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32){hideFlags=HideFlags.HideAndDontSave};texture.Create();
        var previous=RenderTexture.active;
        try {RenderTexture.active=texture;GL.Clear(true,true,new Color(.3f,.4f,.2f,1));}
        finally {RenderTexture.active=previous;}
        return texture;
    }

    public static string CheckToneValues()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before synthetic color checks.");
        int checks=0;float worstDrop=0;
        void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);checks++;}
        var active=RenderTexture.active;bool write=GL.sRGBWrite;
        var colors=new[]{Color.black,new Color(.04f,.04f,.04f),new Color(.08f,.08f,.08f),new Color(.18f,.18f,.18f),
            new Color(.3f,.3f,.3f),new Color(.5f,.5f,.5f),new Color(.75f,.75f,.75f),Color.white,
            new Color(.9f,.15f,.1f),new Color(.1f,.2f,.8f),new Color(.12f,.45f,.08f),new Color(.9f,.7f,.1f)};
        int width=colors.Length*16;
        var input=new Texture2D(width,16,TextureFormat.RGBA32,false,false);
        var read=new Texture2D(width,16,TextureFormat.RGBAFloat,false,true);
        for(int x=0;x<width;x++)for(int y=0;y<16;y++)input.SetPixel(x,y,colors[x/16]);input.Apply();
        Color[] Sample(RenderTexture texture)
        {
            RenderTexture.active=texture;read.ReadPixels(new Rect(0,0,width,16),0,0);read.Apply();
            var values=new Color[colors.Length];
            for(int i=0;i<values.Length;i++)
            {
                var c=read.GetPixel(i*16+8,8);
                values[i]=QualitySettings.activeColorSpace==ColorSpace.Linear&&!texture.sRGB?c.gamma:c;
            }
            return values;
        }
        float Luma(Color c)=>c.r*.2126f+c.g*.7152f+c.b*.0722f;
        try
        {
            foreach(var mode in new[]{RenderTextureReadWrite.sRGB,RenderTextureReadWrite.Linear})
            {
                var source=new RenderTexture(width,16,0,RenderTextureFormat.ARGB32,mode);
                using(var layer=new WorldToonLayer())
                try
                {
                    source.Create();GL.sRGBWrite=QualitySettings.activeColorSpace==ColorSpace.Linear&&source.sRGB;Graphics.Blit(input,source);
                    var baseline=Sample(source);
                    foreach(bool inheritedWrite in new[]{false,true})
                    {
                        GL.sRGBWrite=inheritedWrite;
                        var result=layer.Render(source,new WorldToonSettings{strength=.00001f,outlineStrength=0});
                        Check(GL.sRGBWrite==inheritedWrite,"The layer must restore the caller's sRGB write state.");
                        var identity=Sample(result);
                        for(int i=0;i<colors.Length;i++)
                            Check(Mathf.Abs(Luma(identity[i])-Luma(baseline[i]))<.012f,"Near-zero strength must preserve brightness regardless of previous render state: "+mode+" patch "+i);
                    }
                    var output=Sample(layer.Render(source,new WorldToonSettings{strength=1,outlineStrength=0}));
                    for(int i=0;i<colors.Length;i++)
                    {
                        float before=Luma(baseline[i]),after=Luma(output[i]);
                        if(before>.02f)worstDrop=Mathf.Max(worstDrop,(before-after)/before);
                        Check(after>=before*.93f-.006f,"Toon shading must retain dark detail: "+mode+" patch "+i);
                        Check(after<=before*1.15f+.012f,"Toon shading must not wash out the source: "+mode+" patch "+i);
                    }
                    Check(Luma(output[0])<.002f&&Luma(output[7])>.99f,"Black and white endpoints must remain intact.");
                }
                finally{UnityEngine.Object.DestroyImmediate(source);}
            }
            return "PASS: "+checks+" synthetic shader color checks; maximum sampled luminance reduction "+worstDrop.ToString("P1")+". No scene captures.";
        }
        finally
        {
            RenderTexture.active=active;GL.sRGBWrite=write;
            UnityEngine.Object.DestroyImmediate(input);UnityEngine.Object.DestroyImmediate(read);
        }
    }
    static RawImage Image(Transform parent)
    {
        var go=new GameObject("World surface",typeof(RectTransform),typeof(RawImage));go.transform.SetParent(parent,false);
        var image=go.GetComponent<RawImage>();image.raycastTarget=false;return image;
    }
    static void Set(object target,string name,object value)=>target.GetType().GetField(name,Private).SetValue(target,value);
    static void Call(object target,string name,params object[] args)=>target.GetType().GetMethod(name,Private).Invoke(target,args);
}
