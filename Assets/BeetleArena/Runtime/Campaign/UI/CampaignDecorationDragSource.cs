using UnityEngine;
using UnityEngine.EventSystems;

namespace BeetleArena.Campaign
{
    public sealed class CampaignDecorationDragSource:MonoBehaviour,IBeginDragHandler,IDragHandler,IEndDragHandler
    {
        static CampaignDecorationDragSource active;
        public static bool Dragging=>active;
        CampaignConstructionController controller;string itemId;
        public void Bind(CampaignConstructionController target,string item){controller=target;itemId=item;}
        public void OnBeginDrag(PointerEventData e){if(e.button==PointerEventData.InputButton.Left){controller.BeginPlace(itemId);if(controller.HasPreview)active=this;}}
        public void OnDrag(PointerEventData e){if(e.button==PointerEventData.InputButton.Left)controller.PreviewAtScreenPoint(e.position);}
        public void OnEndDrag(PointerEventData e){if(e.button==PointerEventData.InputButton.Left){active=null;controller.PreviewAtScreenPoint(e.position);controller.Confirm();}}
        void OnDisable(){if(active==this)active=null;}
    }
}
