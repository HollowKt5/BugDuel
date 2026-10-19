using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BeetleArena.Campaign
{
    // Reconcile page nodes in place. Buttons/ScrollRects retain identity, focus and scroll inertia.
    // Modal trees are short-lived and deliberately excluded from page reconciliation.
    sealed class CampaignUiTree
    {
        readonly Dictionary<string,RectTransform> nodes=new Dictionary<string,RectTransform>();
        readonly Dictionary<int,int> cursors=new Dictionary<int,int>();
        readonly HashSet<string> used=new HashSet<string>();
        public bool Building { get; private set; }
        public void Begin() { used.Clear(); cursors.Clear(); Building=true; }
        public RectTransform Take(string kind,Transform parent,out bool created)
        {
            int id=parent.GetInstanceID(); cursors.TryGetValue(id,out int index); cursors[id]=index+1;
            string key=id+":"+index+":"+kind; used.Add(key);
            created=!nodes.TryGetValue(key,out var rt) || !rt;
            if(created) { rt=new GameObject(kind,typeof(RectTransform)).GetComponent<RectTransform>(); rt.SetParent(parent,false); nodes[key]=rt; }
            rt.gameObject.SetActive(true); rt.SetSiblingIndex(index); return rt;
        }
        public void End()
        {
            Building=false;
            foreach(var key in nodes.Keys.Where(k=>!used.Contains(k)).ToArray())
            { var rt=nodes[key]; if(rt) { rt.gameObject.SetActive(false); Object.Destroy(rt.gameObject); } nodes.Remove(key); }
        }
    }
}
