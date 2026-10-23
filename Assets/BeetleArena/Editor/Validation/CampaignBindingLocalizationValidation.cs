using System;
using System.IO;
using System.Reflection;
using System.Text;
using BeetleArena.Campaign;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

public static class CampaignBindingLocalizationValidation
{
    [MenuItem("Tools/Beetle Arena/Validate Control Binding Localization")]
    public static void Run()
    {
        bool previousEnglish=CampaignLocalization.English;
        var report=new StringBuilder();int checks=0;
        var formatter=typeof(CampaignView).GetMethod("FormatBinding",BindingFlags.Static|BindingFlags.NonPublic);
        using(var action=new InputAction("BindingLocalization",InputActionType.Button,"<Keyboard>/h"))
        {
            string Label(string path)
            {
                action.ApplyBindingOverride(0,path);
                return CampaignLocalization.Text((string)formatter.Invoke(null,new object[]{action,0}));
            }
            void Check(bool valid,string detail)
            {
                if(!valid)throw new InvalidOperationException("Control binding localization: "+detail);
                report.AppendLine("PASS "+(++checks)+" "+detail);
            }
            try
            {
                CampaignLocalization.Initialize(0);
                Check(Label("<Keyboard>/g")=="G","Chinese G key keeps its physical legend");
                Check(CampaignLocalization.Text("G")=="金币","currency still translates independently");
                Check(Label("<Mouse>/leftButton")=="鼠标左键","Chinese mouse left button");
                Check(Label("<Mouse>/rightButton")=="鼠标右键","Chinese mouse right button");
                Check(Label("<Gamepad>/dpad/up")=="十字键上","Chinese directional pad up");
                Check(Label("<Gamepad>/leftShoulder")=="左肩键","Chinese gamepad shoulder");
                Check(Label("<Gamepad>/buttonEast")=="B","gamepad face button keeps its physical legend");
                Check(Label("<Keyboard>/numpad7")=="小键盘 7","Chinese numpad distinguishes number row");
                action.ApplyBindingOverride(0,"<Joystick>/trigger");
                string unknown=action.GetBindingDisplayString(0);
                Check(Label("<Joystick>/trigger")==unknown,"unknown device label remains recognizable in Chinese");

                CampaignLocalization.Select(1);
                Check(Label("<Keyboard>/g")=="G","English G key remains G");
                Check(Label("<Mouse>/leftButton")=="Left Mouse Button","English mouse left button");
                Check(Label("<Mouse>/rightButton")=="Right Mouse Button","English mouse right button");
                Check(Label("<Gamepad>/dpad/up")=="D-Pad Up","English directional pad up");
                Check(Label("<Gamepad>/leftShoulder")=="Left Shoulder","English gamepad shoulder");
                Check(Label("<Keyboard>/space")=="Space","English space key");
                Check(Label("<Keyboard>/numpad7")=="Numpad 7","English numpad key");
                Check(Label("<Joystick>/trigger")==unknown,"unknown device label remains recognizable in English");
                Check(action.bindings[0].effectivePath=="<Joystick>/trigger","display formatting leaves binding override unchanged");
            }
            finally
            {
                CampaignLocalization.Select(previousEnglish?1:0);
                Directory.CreateDirectory("Artifacts/Localization");
                File.WriteAllText("Artifacts/Localization/ControlBindings.txt",report.ToString());
            }
        }
        Debug.Log("Control binding localization PASS: "+checks+" checks. Letters, currency, mouse, D-pad, gamepad, numpad and unknown device labels.");
    }
}
