using TMPro;
using UnityEngine;

namespace BeetleArena.Campaign
{
    // Covers labels generated in code and frequently updated domain notices, without altering domain strings.
    [DisallowMultipleComponent,DefaultExecutionOrder(1000)]
    public sealed class CampaignLocalizedLabel:MonoBehaviour
    {
        TMP_Text label;
        string source,applied;
        void Awake()=>label=GetComponent<TMP_Text>();
        void OnEnable(){CampaignLocalization.Changed+=Refresh;if(label&&source!=null)Refresh();}
        void OnDisable()=>CampaignLocalization.Changed-=Refresh;
        public void SetSource(string value)
        {
            if(!label)label=GetComponent<TMP_Text>();
            if(source==value&&label.text==applied&&label.font==CampaignLocalization.Font)return;
            source=value;Refresh();
        }
        void LateUpdate()
        {
            if(!label)return;
            if(label.text!=applied){source=label.text;Refresh();}
        }
        internal void Refresh()
        {
            if(!label)return;
            var font=CampaignLocalization.Font;if(font&&label.font!=font)label.font=font;
            applied=CampaignLocalization.Text(source??label.text);label.text=applied;
        }
    }
}
