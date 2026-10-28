using UnityEngine;

namespace BeetleArena
{
    /// <summary>Resizes the glass and its trim together while keeping the soil and lower edge fixed.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class HusbandryGlassEnclosure : MonoBehaviour
    {
        public const float DefaultHeight = 5.6f;
        [SerializeField, Min(.1f), Tooltip("玻璃底缘到上沿的总高度；原高度 2.8，当前默认 5.6（两倍）。土层与底框不移动。")]
        float height = DefaultHeight;
        [SerializeField, HideInInspector] float bottomY = -.2f;
        [SerializeField, HideInInspector] Transform[] panes = new Transform[0];
        [SerializeField, HideInInspector] Transform[] topRails = new Transform[0];
        [SerializeField, HideInInspector] Transform[] cornerSeams = new Transform[0];
        [SerializeField, HideInInspector] Transform[] baseRails = new Transform[0];

        public float Height => height;
        public float BottomY => bottomY;
        public int Revision { get; private set; }

        public void SetHeight(float value)
        {
            height = Mathf.Max(.1f, value);
            ApplyHeight();
        }

        public void Configure(Transform[] glassPanes, Transform[] upperRails, Transform[] seams, Transform[] lowerRails=null)
        {
            panes = glassPanes; topRails = upperRails; cornerSeams = seams;
            if(lowerRails!=null)baseRails=lowerRails;
            ApplyHeight();
        }

        public void SetBottom(float value)
        {
            // A deeper substrate extends the lower glass without moving the existing upper edge.
            float top=bottomY+height;
            bottomY=Mathf.Min(value,top-.1f);height=top-bottomY;
            ApplyHeight();
        }

        void OnEnable() => ApplyHeight();
        void OnValidate() => SetHeight(height);

        void ApplyHeight()
        {
            foreach (var pane in panes) ResizeVertical(pane);
            foreach (var seam in cornerSeams) ResizeVertical(seam);
            foreach (var rail in topRails)
            {
                var position = rail.localPosition; position.y = bottomY + height; rail.localPosition = position;
            }
            foreach(var rail in baseRails)
            {
                var position=rail.localPosition;position.y=bottomY;rail.localPosition=position;
            }
            Revision++;
        }

        void ResizeVertical(Transform part)
        {
            var position = part.localPosition; position.y = bottomY + height * .5f; part.localPosition = position;
            var scale = part.localScale; scale.y = height; part.localScale = scale;
        }
    }
}
