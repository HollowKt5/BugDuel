using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BeetleArena;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

public static class CombatDisplaySettingsValidation
{
    const string Folder="Artifacts/CombatDisplaySettings";
    static void Require(bool condition,string message) { if(!condition)throw new InvalidOperationException(message); }

    [MenuItem("Tools/甲壳虫斗/验证/界面与画面/战斗显示与设置", false, 604)]
    public static string Run()
    {
        Require(Application.isPlaying,"Open the audio settings in Play mode, allow a rendered frame, then run this validation.");
        var slider=UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Slider>(FindObjectsSortMode.None)
            .FirstOrDefault(s=>s.name=="Slider 主音量"&&s.IsActive()&&s.IsInteractable());
        Require(slider,"Open the campaign audio settings before validating the rendered slider.");
        return Run(slider);
    }

    public static string Run(UnityEngine.UI.Slider slider)
    {
        Require(Application.isPlaying,"Pointer validation requires a rendered Play mode canvas.");
        Directory.CreateDirectory(Folder);
        ValidateSlider(slider);
        ValidateStyleSwitching();
        RenderDither();
        const string report="PASS volume slider: raycast hit, track click, drag to 0/100%, keyboard adjustment and fill bounds.\nPASS exclusive clean / CRT / dither material selection and original material restoration.\nPASS dither shader compiled; original RGB colors preserved within 3.5% per-channel local-average error; frozen input produces identical pixels; coarse cells are uniform.\n";
        File.WriteAllText(Folder+"/Validation.txt",report);
        return report;
    }

    static void ValidateSlider(UnityEngine.UI.Slider slider)
    {
        // Validate the actual visible control, including its modal ancestors and full raycast
        // ordering. Newly constructed edit-mode canvases have depth=-1 until a render pass.
        var events=EventSystem.current;
        Require(events&&slider&&slider.IsActive()&&slider.IsInteractable(),"The live slider/EventSystem is unavailable.");
        var root=(RectTransform)slider.transform;var canvas=slider.GetComponentInParent<Canvas>();
        var raycaster=canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>();
        var eventCamera=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
        var selected=events.currentSelectedGameObject;float previousValue=slider.value,changed=-1;
        UnityEngine.Events.UnityAction<float> listener=v=>changed=v;slider.onValueChanged.AddListener(listener);
        try
        {
            slider.SetValueWithoutNotify(.8f);
            Canvas.ForceUpdateCanvases();
            var area=(RectTransform)slider.handleRect.parent;
            Vector2 Position(float fraction)=>RectTransformUtility.WorldToScreenPoint(eventCamera,area.TransformPoint(new Vector3(Mathf.Lerp(area.rect.xMin,area.rect.xMax,fraction),0,0)));
            var pointer=new PointerEventData(events){button=PointerEventData.InputButton.Left,position=Position(.5f)};
            var hits=new List<RaycastResult>();raycaster.Raycast(pointer,hits);
            var graphic=slider.GetComponent<UnityEngine.UI.Image>();
            Require(hits.Count>0&&hits[0].gameObject==root.gameObject,$"Slider raycast: hits={hits.Count}, first={(hits.Count>0?hits[0].gameObject.name:"none")}, root depth={graphic.depth}, culled={graphic.canvasRenderer.cull}.");
            pointer.pointerPressRaycast=hits[0];
            ExecuteEvents.Execute(root.gameObject,pointer,ExecuteEvents.pointerDownHandler);
            Require(Mathf.Abs(changed-.5f)<.01f,"Track click did not set 50%.");
            pointer.position=Position(0)-Vector2.right*8;ExecuteEvents.Execute(root.gameObject,pointer,ExecuteEvents.dragHandler);
            Require(slider.value==0,"Drag cannot reach mute.");
            pointer.position=Position(1)+Vector2.right*8;ExecuteEvents.Execute(root.gameObject,pointer,ExecuteEvents.dragHandler);
            Require(slider.value==1,"Drag cannot reach full volume.");
            slider.value=.5f;slider.OnMove(new AxisEventData(events){moveDir=MoveDirection.Right});
            Require(slider.value>.5f,"Keyboard/right navigation did not adjust volume.");
            slider.value=.2f;Canvas.ForceUpdateCanvases();
            Require(Mathf.Abs(slider.fillRect.rect.width-((RectTransform)slider.fillRect.parent).rect.width*.2f)<.01f,"Fill overflows the track due to a retained sizeDelta.");
        }
        finally
        {
            slider.onValueChanged.RemoveListener(listener);slider.value=previousValue;
            events.SetSelectedGameObject(selected);
        }
    }

    static void ValidateStyleSwitching()
    {
        var preferences=new DisplayPreferencesSnapshot();
        var go=new GameObject("Style validation",typeof(RectTransform),typeof(UnityEngine.UI.RawImage));
        var profile=ScriptableObject.CreateInstance<BeetleCrtProfile>();
        Material dither=null,crt=null;
        try
        {
            var image=go.GetComponent<UnityEngine.UI.RawImage>();var originalMaterial=image.material;
            var effect=BeetleCombatCrt.Attach(image);effect.profile=profile;
            var tick=typeof(BeetleCombatCrt).GetMethod("LateUpdate",BindingFlags.NonPublic|BindingFlags.Instance);
            BeetleDisplayPreferences.Style=BeetleDisplayStyle.OrderedDither;tick.Invoke(effect,null);
            dither=image.material;
            Require(effect.DitherActive&&!effect.CrtActive&&image.material.shader.name=="BugDuel/Combat Ordered Dither","Dither did not own the surface exclusively.");
            BeetleDisplayPreferences.Style=BeetleDisplayStyle.Crt;tick.Invoke(effect,null);
            crt=image.material;
            Require(effect.CrtActive&&!effect.DitherActive&&image.material.shader.name=="BugDuel/Combat CRT","CRT did not replace dither.");
            BeetleDisplayPreferences.Style=BeetleDisplayStyle.Clean;tick.Invoke(effect,null);
            Require(!effect.EffectActive&&image.material==originalMaterial,"Clean mode failed to restore the original material.");
        }
        finally
        {
            if(dither)UnityEngine.Object.DestroyImmediate(dither);
            if(crt)UnityEngine.Object.DestroyImmediate(crt);
            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(profile);
            preferences.Dispose();
        }
    }

    static void RenderDither()
    {
        var shader=Resources.Load<Shader>("BeetleCombatDither");
        Require(shader&&shader.isSupported&&!ShaderUtil.ShaderHasError(shader),"Ordered dither shader did not compile.");
        var source=new Texture2D(640,360,TextureFormat.RGB24,false);
        var pixels=new Color[640*360];
        for(int y=0;y<360;y++)for(int x=0;x<640;x++)
            pixels[y*640+x]=y<180?Color.Lerp(Color.black,Color.white,x/639f):Color.HSVToRGB(x/639f,.8f,.75f);
        source.SetPixels(pixels);source.Apply();
        var material=new Material(shader);var target=RenderTexture.GetTemporary(1280,720,0,RenderTextureFormat.ARGB32);
        var capture=new Texture2D(1280,720,TextureFormat.RGB24,false);var previous=RenderTexture.active;
        try
        {
            Require(Mathf.Approximately(material.GetFloat("_DitherStrength"),.5f),"Default dither strength must be softened to 0.5.");
            // Compare against the same GPU path without the effect, so the assertion is valid
            // in both linear and gamma projects and does not assume a render-target transfer curve.
            Graphics.Blit(source,target);RenderTexture.active=target;
            capture.ReadPixels(new Rect(0,0,1280,720),0,0);capture.Apply();var baseline=capture.GetPixels32();
            material.SetFloat("_PixelSize",4);material.SetFloat("_DitherStrength",1);Graphics.Blit(source,target,material);
            RenderTexture.active=target;capture.ReadPixels(new Rect(0,0,1280,720),0,0);capture.Apply();var first=capture.GetPixels32();
            var colors=new HashSet<int>();
            foreach(var p in first)colors.Add(p.r|(p.g<<8)|(p.b<<16));
            Require(colors.Count>32,"Dither discarded the original scene color range.");
            File.WriteAllBytes(Folder+"/DitherCalibration.png",capture.EncodeToPNG());
            Graphics.Blit(source,target,material);capture.ReadPixels(new Rect(0,0,1280,720),0,0);capture.Apply();var second=capture.GetPixels32();
            for(int i=0;i<first.Length;i++)Require(first[i].Equals(second[i]),"Stationary dither changed between identical frames.");
            for(int y=8;y<712;y+=4)for(int x=8;x<1272;x+=4)
            {
                var color=first[y*1280+x];
                for(int dy=0;dy<4;dy++)for(int dx=0;dx<4;dx++)
                    Require(first[(y+dy)*1280+x+dx].Equals(color),"Pixelation failed to hold one color per 4x4 cell.");
            }
            // Each 16x16 output region includes a complete 4x4 Bayer pattern of 4px cells.
            // Their average must retain the corresponding source red, green and blue levels.
            for(int y=16;y<704;y+=16)for(int x=16;x<1264;x+=16)
            {
                if(y>=336&&y<384)continue; // Avoid the calibration image's hard midline.
                Vector3 original=Vector3.zero,rendered=Vector3.zero;
                for(int dy=0;dy<16;dy++)for(int dx=0;dx<16;dx++)
                {
                    int index=(y+dy)*1280+x+dx;var a=baseline[index];var b=first[index];
                    original+=new Vector3(a.r,a.g,a.b);rendered+=new Vector3(b.r,b.g,b.b);
                }
                var difference=(rendered-original)/(256f*255);
                Require(Mathf.Max(Mathf.Abs(difference.x),Mathf.Abs(difference.y),Mathf.Abs(difference.z))<.035f,
                    $"Dither recolored source region {x},{y}: mean RGB change {difference}.");
            }
            material.SetFloat("_DitherStrength",0);Graphics.Blit(source,target,material);
            capture.ReadPixels(new Rect(0,0,1280,720),0,0);capture.Apply();var zero=capture.GetPixels32();
            material.SetFloat("_DitherStrength",.5f);Graphics.Blit(source,target,material);
            capture.ReadPixels(new Rect(0,0,1280,720),0,0);capture.Apply();var half=capture.GetPixels32();
            double fullError=0,halfError=0;
            for(int i=0;i<zero.Length;i++)
            {
                fullError+=Math.Abs(first[i].r-zero[i].r)+Math.Abs(first[i].g-zero[i].g)+Math.Abs(first[i].b-zero[i].b);
                halfError+=Math.Abs(half[i].r-zero[i].r)+Math.Abs(half[i].g-zero[i].g)+Math.Abs(half[i].b-zero[i].b);
            }
            Require(fullError>1000&&halfError/fullError>.3&&halfError/fullError<.7,
                "Half strength must reduce RGB dithering deviation without removing the effect.");
            File.WriteAllBytes(Folder+"/DitherCalibration-Soft.png",capture.EncodeToPNG());
            File.WriteAllText(Folder+"/DitherStrength.txt",$"PASS default=0.5; half/full RGB deviation ratio={halfError/fullError:F4}; original palette and stable pixel cells retained.");
        }
        finally
        {
            RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(capture);
        }
    }
}
