using System.Collections.Generic;
using UnityEngine;

namespace BeetleArena
{
    /// <summary>Adapts the actual soil and decoration geometry to a flat, yaw-rotated care enclosure.</summary>
    public sealed class CareEnclosureLayout
    {
        public CareWalkArea Area { get; }
        public float GroundHeight { get; }
        public float Heading { get; }
        public readonly List<Renderer> GroundDetails = new List<Renderer>();
        readonly Vector3 center, right, forward;
        public CareEnclosureLayout(Renderer floor, BeetleProceduralAnimator rig, float clearance, IEnumerable<Rect> scenery = null)
        {
            GroundHeight = floor.bounds.max.y;
            center = floor.transform.TransformPoint(floor.localBounds.center); center.y = GroundHeight;
            right = Vector3.ProjectOnPlane(floor.transform.right, Vector3.up).normalized;
            forward = Vector3.Cross(right, Vector3.up);
            Heading = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            Rect floorRect = Project(floor);
            var blockers = new List<Rect>();
            if (scenery != null) blockers.AddRange(scenery);
            foreach (var decoration in (floor.transform.parent ? floor.transform.parent : floor.transform).GetComponentsInChildren<Renderer>())
            {
                if (decoration == floor || !decoration.enabled || decoration.transform.IsChildOf(rig.transform) ||
                    decoration.name == "Backdrop" || decoration.name.StartsWith("Care") || decoration.bounds.max.y <= GroundHeight + .005f) continue;
                Rect bounds = Project(decoration);
                if ((decoration.name.StartsWith("Substrate") || decoration.name.StartsWith("Leaf")) && decoration.bounds.max.y <= GroundHeight + .16f)
                { GroundDetails.Add(decoration); continue; }
                if (scenery == null && bounds.Overlaps(floorRect)) blockers.Add(bounds);
            }
            Area = new CareWalkArea(floorRect, clearance, blockers);
        }
        public Vector2 ToLocal(Vector3 world)
        { var d = world - center; return new Vector2(Vector3.Dot(d, right), Vector3.Dot(d, forward)); }
        public Vector3 ToWorld(Vector2 local) => center + right * local.x + forward * local.y;
        Rect Project(Renderer renderer)
        {
            var b = renderer.localBounds; Vector2 min = Vector2.one * float.PositiveInfinity, max = Vector2.one * float.NegativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                var p = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var point = ToLocal(renderer.transform.TransformPoint(p)); min = Vector2.Min(min, point); max = Vector2.Max(max, point);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
        public static float MeasureClearance(BeetleProceduralAnimator rig)
        {
            float radius = 0; Vector3 origin = rig.transform.position;
            foreach (var renderer in rig.GetComponentsInChildren<Renderer>())
            {
                var b = renderer.localBounds;
                for (int i = 0; i < 8; i++)
                {
                    var p = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var d = renderer.transform.TransformPoint(p) - origin;
                    radius = Mathf.Max(radius, new Vector2(d.x, d.z).magnitude);
                }
            }
            foreach (var leg in rig.legs)
            {
                var d = rig.model.TransformPoint(leg.restAnkle) - origin;
                radius = Mathf.Max(radius, new Vector2(d.x, d.z).magnitude);
            }
            return radius + .18f; // Toe reach, step lag and a small wall gap, in world units.
        }
    }
}
