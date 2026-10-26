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
        float pointSize,minimumSize,maximumSize;
        bool initialized;
        void Awake()=>EnsureLabel();
        void EnsureLabel()
        {
            if(initialized)return;
            label=GetComponent<TMP_Text>();pointSize=label.fontSize;minimumSize=label.fontSizeMin;maximumSize=label.fontSizeMax;initialized=true;
        }
        internal void SetFontSize(float size,float minimum)
        {EnsureLabel();pointSize=maximumSize=size;minimumSize=minimum;ApplyFontSize();}
        void ApplyFontSize()
        {
            label.fontSize=CampaignLocalization.PointSize(pointSize);
            label.fontSizeMin=CampaignLocalization.PointSize(minimumSize);label.fontSizeMax=CampaignLocalization.PointSize(maximumSize);
        }
        void OnEnable(){CampaignLocalization.Changed+=Refresh;if(label&&source!=null)Refresh();}
        void OnDisable()=>CampaignLocalization.Changed-=Refresh;
        public void SetSource(string value)
        {
            EnsureLabel();
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
            ApplyFontSize();
            applied=CampaignLocalization.Text(source??label.text);label.text=applied;
        }
    }
}
