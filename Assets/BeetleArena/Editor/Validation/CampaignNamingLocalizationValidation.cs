using System;
using System.Collections.Generic;
using BeetleArena.Campaign;
using BugDuel.Campaign;
using TMPro;
using UnityEditor;
using UnityEngine;

public static class CampaignNamingLocalizationValidation
{
    [MenuItem("Tools/甲壳虫斗/验证/本地化/玩家与甲虫姓名", false, 641)]
    public static void Run()
    {
        bool previousEnglish=CampaignLocalization.English;
        GameObject inputOwner=null;
        try
        {
            CampaignLocalization.Initialize(1);
            Check(CampaignLocalization.Text("死亡")=="Dead","system word translates normally");
            var beetle=new CampaignBeetle {name="死亡",customName=true};
            string displayName=CampaignLocalization.Name(beetle);
            Check(CampaignLocalization.Text(displayName)=="死亡","custom name is not translated");
            Check(CampaignLocalization.Text(displayName+"已售予甲虫商店，收到 123 G。")=="死亡 sold to the beetle store for 123 G.","English surrounding sentence preserves name");
            Check(CampaignLocalization.Text(displayName+" / "+displayName+" / 死亡")=="死亡 / 死亡 / Dead","repeated names do not protect matching system words");
            Check(CampaignLocalization.Text(CampaignLocalization.Literal("{0} / 死亡 / 你")+"已发送至邮箱，请领取附件。")=="{0} / 死亡 / 你 has arrived in your mailbox. Claim the attachment.","punctuation and template-like names remain literal");
            Check(CampaignLocalization.Text(CampaignLocalization.Literal("你"))+" / "+CampaignLocalization.Text("你")=="你 / You","literal and ordinary cache keys remain distinct");
            beetle.customName=false;
            Check(CampaignLocalization.Text(CampaignLocalization.Name(beetle))=="Dead","default names may still localize");
            beetle.customName=true;

            var session=CampaignSession.New(CampaignCatalog.Defaults(),47321);
            session.RenameBeetle(session.State.beetles[0].id,"死亡");
            session.State.hall.Add(new HallRecord {name="死亡",customName=true,speciesId="red",length=48,price=100,week=1});
            session.State.mail.Add(new CampaignMail {id="naming-test",senderId="store",subject="活体配送 · "+displayName,body="订单已完成",week=1,claimed=true,
                attachments=new List<CampaignAttachment> {new CampaignAttachment {itemId=CampaignItems.Beetle,name="死亡",literalName=true}}});
            var copy=CampaignSnapshots.Copy(session.State);
            var saved=JsonUtility.FromJson<CampaignState>(JsonUtility.ToJson(copy));
            Check(saved.beetles[0].customName&&saved.beetles[0].name=="死亡","raw name and flag survive snapshot and JSON");
            Check(CampaignLocalization.Text(CampaignLocalization.Name(saved.beetles[0]))=="死亡","loaded beetle name stays literal");
            var hall=saved.hall[0];Check(hall.customName&&hall.name=="死亡"&&CampaignLocalization.Text(CampaignLocalization.Name(hall))=="死亡","hall snapshot retains raw name and identity");
            var attachment=saved.mail[saved.mail.Count-1].attachments[0];
            Check(attachment.literalName&&attachment.name=="死亡"&&attachment.beetles.Count==0&&CampaignLocalization.Text(CampaignLocalization.Name(attachment))=="死亡","claimed attachment preserves identity after its asset list is cleared");
            Check(!CampaignLocalization.Text(saved.mail[saved.mail.Count-1].subject).Contains("\uE000"),"saved message markers never reach display");
            string unusual="A\uE000\uE001\uE0022\uE003 / <size=8>";
            Check(CampaignDisplayText.Localize(CampaignDisplayText.Literal(unusual),s=>s.Replace("size","SIZE"))==unusual,"marker-like and markup-like plaque text round trips without nesting");

            inputOwner=new GameObject("Naming input validation",typeof(RectTransform));inputOwner.SetActive(false);
            var input=inputOwner.AddComponent<TMP_InputField>();
            var value=new GameObject("Input value",typeof(RectTransform),typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();value.transform.SetParent(inputOwner.transform,false);value.text="死亡";value.richText=false;
            var placeholder=new GameObject("Input placeholder",typeof(RectTransform),typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();placeholder.transform.SetParent(inputOwner.transform,false);placeholder.text="甲虫名字";
            input.textComponent=value;input.placeholder=placeholder;
            CampaignLocalization.BindHierarchy(inputOwner.transform);
            Check(!value.GetComponent<CampaignLocalizedLabel>()&&!placeholder.GetComponent<CampaignLocalizedLabel>(),"hierarchy localization skips input value and placeholder");
            Check(value.text=="死亡"&&placeholder.text=="甲虫名字","input text is never rewritten by localization");

            CampaignLocalization.Select(0);
            Check(CampaignLocalization.Text(displayName+"已售予甲虫商店，收到 123 G。")=="死亡已售予甲虫商店，收到 123 金币。","Chinese path translates currency and preserves player text");
            CampaignLocalization.Select(1);
            Check(CampaignLocalization.Text(displayName)=="死亡","locale switching and cache preserve names");
        }
        finally
        {
            if(inputOwner)UnityEngine.Object.DestroyImmediate(inputOwner);
            CampaignLocalization.Select(previousEnglish?1:0);
        }
        Debug.Log("Naming localization PASS: explicit literals, surrounding English, repeated/system-word names, raw names through clone and JSON, claimed mail, hall records, input ownership and locale switching.");
    }
    static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException("Naming localization failed: "+message);}
}
