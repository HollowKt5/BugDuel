using UnityEngine;

namespace BeetleArena.Campaign
{
    public sealed partial class CampaignView
    {
        [SerializeField,Tooltip("留空时自动使用 Resources/CampaignUiLayout。请修改共享资产以保存布局。")]
        CampaignUiLayoutProfile layoutProfile;
        CampaignUiLayoutProfile observedLayout,ownedLayout;
        int layoutRevision=-1;
        bool layoutChangePending;

        CampaignUiLayoutProfile Layout
        {
            get
            {
                if(layoutProfile)return layoutProfile;
                layoutProfile=Resources.Load<CampaignUiLayoutProfile>(CampaignUiLayoutProfile.ResourceName);
                if(!layoutProfile)
                {
                    ownedLayout=layoutProfile=ScriptableObject.CreateInstance<CampaignUiLayoutProfile>();
                    ownedLayout.hideFlags=HideFlags.HideAndDontSave;
                }
                layoutProfile.Validate();return layoutProfile;
            }
        }
        float SidebarFraction=>Layout.greenhouse.sidebarFraction;
        float ExpandedSidebarFraction=>SidebarFraction*(1+warehouseProgress*Layout.warehouse.detailWidthScale);

        void UpdateLayoutConfiguration()
        {
            var profile=Layout;
            if(observedLayout!=profile || layoutRevision!=profile.revision)
            {
                observedLayout=profile;layoutRevision=profile.revision;layoutChangePending=true;
            }
            if(!layoutChangePending || app.Busy || rebind!=null || CampaignDecorationDragSource.Dragging ||
               (dialogMotion && dialogMotion.IsAnimating))return;
            layoutChangePending=false;
            if(modal && dialogMotion)
            {
                if(settingsOpen)OpenSettings();
                else PlaceDialog(dialogMotion.Panel,dialogContent);
            }
            QueueRefresh();
        }
        void PlaceDialog(RectTransform panel,string content)
        {
            var offset=Layout.common.dialogOffset+(content==nameof(OpenSettings)?Layout.settings.offset:Vector2.zero);
            float margin=Layout.common.dialogMargin;
            float x=Mathf.Max(0,(canvas.rect.width-panel.rect.width)*.5f-margin);
            float y=Mathf.Max(0,(canvas.rect.height-panel.rect.height)*.5f-margin);
            panel.anchoredPosition=new Vector2(Mathf.Clamp(offset.x,-x,x),Mathf.Clamp(-offset.y,-y,y));
        }
        void PlaceFloatingWindow(RectTransform panel,Vector2 inset)
        {
            panel.anchorMin=panel.anchorMax=panel.pivot=Vector2.one;
            float x=Mathf.Clamp(inset.x,0,Mathf.Max(0,canvas.rect.width-panel.rect.width));
            float y=Mathf.Clamp(inset.y,0,Mathf.Max(0,canvas.rect.height-panel.rect.height));
            panel.anchoredPosition=new Vector2(-x,-y);
        }
    }
}
