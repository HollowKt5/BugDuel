using System.Collections.Generic;
using UnityEngine;

namespace BeetleArena
{
    /// <summary>Flat enclosure space in world-sized X/Z coordinates. No scene, campaign or combat dependencies.</summary>
    public sealed class CareWalkArea
    {
        public Rect Floor { get; }
        public Rect Bounds { get; }
        public Rect ReachableBounds { get; }
        public float Clearance { get; }
        public Vector2 Spawn { get; }
        public Vector2 FeedingPoint { get; }
        public Vector2 FoodPoint { get; }
        public Vector2 ShelterPoint { get; }
        public int WalkablePoints { get; }
        public int ObstacleCount => obstacles.Count;
        public bool CanWalk => WalkablePoints > 1;
        readonly List<Rect> obstacles = new List<Rect>();
        readonly List<Vector2> cells = new List<Vector2>();
        readonly List<int>[] neighbors;
        readonly int[] previous, queue;

        public CareWalkArea(Rect floor, float clearance, IEnumerable<Rect> blockers = null)
        {
            Floor = floor; Clearance = Mathf.Max(0, clearance);
            var half = Vector2.Max(Vector2.zero, floor.size * .5f - Vector2.one * Clearance);
            Bounds = new Rect(floor.center - half, half * 2);
            if (blockers != null) obstacles.AddRange(blockers);
            // A coarse roadmap is built only when the enclosure changes. Actual movement follows smoothed, swept segments.
            int nx = Mathf.Clamp(Mathf.CeilToInt(Bounds.width / .3f) + 1, 2, 48);
            int nz = Mathf.Clamp(Mathf.CeilToInt(Bounds.height / .3f) + 1, 2, 48);
            var indices = new int[nx, nz];
            var links = new List<List<int>>();
            bool fits = floor.width > Clearance * 2 && floor.height > Clearance * 2;
            for (int z = 0; z < nz; z++) for (int x = 0; x < nx; x++)
            {
                indices[x,z] = -1;
                var p = new Vector2(Mathf.Lerp(Bounds.xMin, Bounds.xMax, x / (float)(nx - 1)),
                    Mathf.Lerp(Bounds.yMin, Bounds.yMax, z / (float)(nz - 1)));
                if (!fits || !IsClear(p)) continue;
                int index = cells.Count; indices[x,z] = index; cells.Add(p); links.Add(new List<int>(4));
                if (x > 0) Connect(index, indices[x-1,z], links);
                if (z > 0) Connect(index, indices[x,z-1], links);
            }
            neighbors = links.ToArray(); previous = new int[cells.Count]; queue = new int[cells.Count];
            // Ignore isolated pockets that cannot be reached without passing through a decoration.
            var visited = new bool[cells.Count]; var largest = new List<int>();
            for (int i = 0; i < cells.Count; i++)
            {
                if (visited[i]) continue;
                var component = new List<int>(); int count = Flood(i);
                for (int j = 0; j < count; j++) { visited[queue[j]] = true; component.Add(queue[j]); }
                if (component.Count > largest.Count) largest = component;
            }
            allowed = new bool[cells.Count]; foreach (int i in largest) allowed[i] = true;
            WalkablePoints = largest.Count;
            Vector2 low=Bounds.center,high=low;
            if(largest.Count>0)
            {
                low=high=cells[largest[0]];
                foreach(int i in largest){low=Vector2.Min(low,cells[i]);high=Vector2.Max(high,cells[i]);}
            }
            ReachableBounds=Rect.MinMaxRect(low.x,low.y,high.x,high.y);
            Spawn = Nearest(Bounds.center);
            FeedingPoint = Nearest(Bounds.center + Vector2.Scale(Bounds.size, new Vector2(.22f, -.22f)));
            ShelterPoint = Nearest(Bounds.center + Vector2.Scale(Bounds.size, new Vector2(-.28f, .28f)));
            var dishHalf=Vector2.Max(Vector2.zero,Floor.size*.5f-Vector2.one*.3f);
            var dish=FeedingPoint+new Vector2(.45f,-.75f);
            FoodPoint=new Vector2(Mathf.Clamp(dish.x,Floor.center.x-dishHalf.x,Floor.center.x+dishHalf.x),
                Mathf.Clamp(dish.y,Floor.center.y-dishHalf.y,Floor.center.y+dishHalf.y));
        }
        readonly bool[] allowed;
        void Connect(int a, int b, List<List<int>> links)
        {
            if (b < 0 || !SegmentClear(cells[a], cells[b])) return;
            links[a].Add(b); links[b].Add(a);
        }
        int Flood(int start)
        {
            for (int i = 0; i < previous.Length; i++) previous[i] = -1;
            int count = 1; queue[0] = start; previous[start] = start;
            for (int head = 0; head < count; head++) foreach (int next in neighbors[queue[head]])
            {
                if (previous[next] >= 0) continue;
                previous[next] = queue[head]; queue[count++] = next;
            }
            return count;
        }
        int NearestIndex(Vector2 point, bool visible)
        {
            int result = -1; float best = float.PositiveInfinity;
            for (int i = 0; i < cells.Count; i++)
            {
                if (!allowed[i]) continue;
                float d = (cells[i] - point).sqrMagnitude;
                if (d >= best || visible && !SegmentClear(point, cells[i])) continue;
                best = d; result = i;
            }
            return result;
        }
        public Vector2 Nearest(Vector2 point)
        { int index = NearestIndex(point, false); return index < 0 ? Bounds.center : cells[index]; }
        public Vector2 KeepInside(Vector2 point) => IsClear(point) && NearestIndex(point, true) >= 0 ? point : Nearest(point);
        public Vector2 WanderPoint(float choice)
        {
            int count = 0; foreach (bool valid in allowed) if (valid) count++;
            int pick = Mathf.Min(count - 1, Mathf.FloorToInt(choice * count));
            for (int i = 0; i < allowed.Length; i++) if (allowed[i] && pick-- == 0) return cells[i];
            return Spawn;
        }
        public bool IsClear(Vector2 p)
        {
            if (p.x < Bounds.xMin || p.x > Bounds.xMax || p.y < Bounds.yMin || p.y > Bounds.yMax) return false;
            foreach (var obstacle in obstacles)
                if ((p - new Vector2(Mathf.Clamp(p.x,obstacle.xMin,obstacle.xMax),Mathf.Clamp(p.y,obstacle.yMin,obstacle.yMax))).sqrMagnitude <= Clearance * Clearance) return false;
            return true;
        }
        public bool SegmentClear(Vector2 from, Vector2 to)
        {
            if (!IsClear(from) || !IsClear(to)) return false;
            var delta = to - from;
            foreach (var obstacle in obstacles)
            {
                float enter = 0, exit = 1;
                if (Slab(from.x, delta.x, obstacle.xMin, obstacle.xMax, ref enter, ref exit) &&
                    Slab(from.y, delta.y, obstacle.yMin, obstacle.yMax, ref enter, ref exit)) return false;
                // Swept circle, including rounded corners. A square expansion incorrectly seals diagonal passages.
                if (PointSegment(new Vector2(obstacle.xMin,obstacle.yMin),from,to) <= Clearance*Clearance ||
                    PointSegment(new Vector2(obstacle.xMax,obstacle.yMin),from,to) <= Clearance*Clearance ||
                    PointSegment(new Vector2(obstacle.xMin,obstacle.yMax),from,to) <= Clearance*Clearance ||
                    PointSegment(new Vector2(obstacle.xMax,obstacle.yMax),from,to) <= Clearance*Clearance) return false;
            }
            return true;
        }
        static float PointSegment(Vector2 p,Vector2 a,Vector2 b)
        {
            var d=b-a;float t=d.sqrMagnitude<1e-10f?0:Mathf.Clamp01(Vector2.Dot(p-a,d)/d.sqrMagnitude);
            return (p-a-d*t).sqrMagnitude;
        }
        static bool Slab(float p, float d, float min, float max, ref float enter, ref float exit)
        {
            if (Mathf.Abs(d) < .000001f) return p >= min && p <= max;
            float a = (min - p) / d, b = (max - p) / d;
            enter = Mathf.Max(enter, Mathf.Min(a,b)); exit = Mathf.Min(exit, Mathf.Max(a,b)); return enter <= exit;
        }
        public void FindPath(Vector2 start, Vector2 goal, List<Vector2> path)
        {
            path.Clear();
            if (!CanWalk) return;
            if (SegmentClear(start, goal)) { path.Add(goal); return; }
            int first = NearestIndex(start, true), last = NearestIndex(goal, false);
            if (first < 0 || last < 0) return;
            Flood(first); if (previous[last] < 0) return;
            for (int node = last;; node = previous[node]) { path.Add(cells[node]); if (node == first) break; }
            path.Reverse();
            // Pull the path taut only across unobstructed segments; never cut through corners.
            Vector2 anchor = start; int keep = 0;
            while (keep < path.Count)
            {
                int far = path.Count - 1;
                while (far > keep && !SegmentClear(anchor, path[far])) far--;
                if (far > keep) path.RemoveRange(keep, far - keep);
                anchor = path[keep++];
            }
        }
    }
}
