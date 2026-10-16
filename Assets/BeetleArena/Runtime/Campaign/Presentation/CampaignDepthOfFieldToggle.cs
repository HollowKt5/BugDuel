using UnityEngine;
using UnityEngine.Rendering;

namespace BeetleArena.Campaign
{
    public sealed class CampaignDepthOfFieldToggle : MonoBehaviour
    {
        public Volume target;
        void Update(){if(target&&CampaignApp.Instance)target.weight=CampaignApp.Instance.Preferences.depthOfField?1:0;}
    }
}
