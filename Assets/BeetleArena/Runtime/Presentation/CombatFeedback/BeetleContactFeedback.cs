using UnityEngine;

namespace BeetleArena
{
    /// <summary>
    /// 只读表现采样器：固定步求解完成后检查真实角部代理，不改碰撞、状态、速度或随机源。
    /// 文案事件不再触发声音；持续贴住时去抖，只有相对运动才补摩擦或重新敲击。
    /// </summary>
    public sealed class BeetleContactFeedback : MonoBehaviour
    {
        public ArenaAudio audioFeedback;
        public HornClashSparks sparks;
        BeetleContactRig a, b;
        Vector3[] previousA, previousB;
        float soundCooldown, sparkCooldown, separated, previousClosing;
        bool touching, history;
        public int ObservedContacts { get; private set; }
        public Vector3 LastContactPoint { get; private set; }
        public bool LastHornClash { get; private set; }
        public void Bind(BeetleActor[] actors)
        {
            a = actors[0].GetComponent<BeetleContactRig>(); b = actors[1].GetComponent<BeetleContactRig>();
            previousA = new Vector3[a.shapes.Length]; previousB = new Vector3[b.shapes.Length];
            soundCooldown = sparkCooldown = separated = previousClosing = 0;
            history = touching = false; ObservedContacts = 0;
            LastContactPoint = Vector3.zero; LastHornClash = false;
            if (audioFeedback) audioFeedback.StopAll(); if (sparks) sparks.Clear();
        }
        // 显式 Tick 不受 Unity enabled 自动拦截，停用组件时主动清理并丢弃速度历史。
        void OnDisable()
        {
            history = touching = false;
            if (audioFeedback) audioFeedback.StopAll();
            if (sparks) sparks.Clear();
        }
        static bool Horn(BeetleContactRig rig, int i) => rig.IsHorn(i);
        public void Tick(BeetleSimulation sim, float dt)
        {
            if (!isActiveAndEnabled || dt <= 0 || !a || !b) return;
            if (sparks) sparks.Tick(dt);
            soundCooldown = Mathf.Max(0, soundCooldown - dt); sparkCooldown = Mathf.Max(0, sparkCooldown - dt);
            // 求解器已同步 PhysX；更新最终姿态缓存而不移动任何 Collider。
            a.CacheBounds(); b.CacheBounds();
            bool found = false, hornClash = false;
            Vector3 point = default, normal = Vector3.right;
            float closing = 0, sliding = 0, best = -1;
            if (history && sim.Phase == MatchPhase.Fighting && !sim.Fighters[0].HasFallen && !sim.Fighters[1].HasFallen)
            {
                var bounds = a.worldBounds; bounds.Expand(.06f);
                if (bounds.Intersects(b.worldBounds))
                    for (int i = 0; i < a.shapes.Length; i++)
                        for (int j = 0; j < b.shapes.Length; j++)
                        {
                            bool ha = Horn(a, i), hb = Horn(b, j);
                            // 只采角-角/角-身体；腿碰木台不是兵器碰撞。
                            if ((!ha && !hb) || a.Role(i) == BeetleContactRole.Leg || b.Role(j) == BeetleContactRole.Leg) continue;
                            var ba = a.bounds[i]; ba.Expand(.05f); if (!ba.Intersects(b.bounds[j])) continue;
                            var ca = a.shapes[i]; var cb = b.shapes[j];
                            Vector3 toward = (cb.ClosestPoint(a.bounds[i].center) - a.bounds[i].center).normalized;
                            if (!Physics.ComputePenetration(ca, ca.transform.position + toward * .02f, ca.transform.rotation,
                                cb, cb.transform.position, cb.transform.rotation, out var away, out _)) continue;
                            Vector3 n = -away;
                            Vector3 pa = ca.ClosestPoint(b.bounds[j].center), pb = cb.ClosestPoint(pa);
                            pa = ca.ClosestPoint(pb); pb = cb.ClosestPoint(pa);
                            Vector3 relative = ((a.bounds[i].center - previousA[i]) - (b.bounds[j].center - previousB[j])) / dt;
                            float c = Mathf.Max(0, Vector3.Dot(relative, n));
                            float s = Vector3.ProjectOnPlane(relative, n).magnitude;
                            float score = c + s * .35f + (ha && hb ? .03f : 0);
                            if (score <= best) continue;
                            best = score; found = true; hornClash = ha && hb;
                            point = (pa + pb) * .5f; normal = n; closing = c; sliding = s;
                        }
            }
            for (int i = 0; i < previousA.Length; i++) previousA[i] = a.bounds[i].center;
            for (int i = 0; i < previousB.Length; i++) previousB[i] = b.bounds[i].center;
            history = true;
            if (!found)
            {
                separated += dt;
                if (separated > .08f) { touching = false; previousClosing = 0; }
                return;
            }
            ObservedContacts++; LastContactPoint = point; LastHornClash = hornClash;
            bool onset = !touching; touching = true; separated = 0;
            bool knock = (onset && closing + sliding > .06f) || (closing > .24f && closing > previousClosing + .12f);
            float strength = Mathf.Clamp01(.18f + closing * .60f + sliding * .12f);
            if (soundCooldown <= 0 && (knock || sliding > .10f))
            {
                if (audioFeedback) audioFeedback.PlayContact(point, strength, !knock);
                soundCooldown = knock ? .09f : .17f;
            }
            // 火星不是每个固定步一团：真正角-角接触且存在较明显相对运动，最短间隔 0.16s。
            if (hornClash && sparkCooldown <= 0 && (knock || sliding > .32f) && closing + sliding > .18f)
            { if (sparks) sparks.Burst(point, normal, strength); sparkCooldown = .16f; }
            previousClosing = closing;
        }
    }
}
