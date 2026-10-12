using System;
using System.Collections.Generic;
using UnityEngine;

namespace BeetleArena
{
    /// <summary>每只虫独立的模块宿主：固定步时钟、冷却、抢占/退出和单次命中锁。</summary>
    public sealed class BeetleMotionRuntime
    {
        readonly Dictionary<BeetleMotionModule, float> readyAt = new Dictionary<BeetleMotionModule, float>();
        BeetleMotionModule[] modules = Array.Empty<BeetleMotionModule>();
        public BeetleMotionModule Active { get; private set; }
        public BeetleMotionContext Context { get; private set; }
        public float Elapsed { get; private set; }
        public float Phase => Active ? Mathf.Clamp01(Elapsed / activeDuration) : 0;
        public int Starts { get; private set; }
        public int Hits { get; private set; }
        float activeDuration;
        bool hitUsed;
        public bool HasActive => Active;

        public void Refresh(BeetleSimulation sim, BeetleState self)
        {
            modules = sim.Settings.motionModules ?? Array.Empty<BeetleMotionModule>();
            Context = new BeetleMotionContext(sim, self);
        }
        bool Listed(BeetleMotionModule module)
        {
            if (!module || !module.enabledInMatch) return false;
            for (int i = 0; i < modules.Length; i++) if (modules[i] == module) return true;
            return false;
        }
        public void Tick(float dt)
        {
            if (!Active) return;
            // 配置中移除、禁用、资产删除、腾空或判负都能中断；不留下孤立的 Module 状态。
            if (!Listed(Active) || !Active.CanContinue(Context)) { Cancel(); return; }
            Elapsed += dt;
            if (Elapsed >= activeDuration) Cancel();
        }
        public bool TryStart(float choice)
        {
            if (Active) return false;
            float total = 0;
            for (int i = 0; i < modules.Length; i++) if (Eligible(modules[i], i)) total += modules[i].weight;
            if (total <= 0) return false;
            float pick = Mathf.Clamp01(choice) * total;
            for (int i = 0; i < modules.Length; i++)
            {
                var module = modules[i]; if (!Eligible(module, i)) continue;
                pick -= module.weight;
                if (pick > 0) continue;
                return Start(module);
            }
            return false;
        }
        bool Eligible(BeetleMotionModule module, int index)
        {
            if (!Listed(module) || module.Channel != BeetleMotionChannel.Combat || module.weight <= 0 || !module.CanStart(Context)) return false;
            // 重复引用同一资产只算一次，不会悄悄提升抽取权重或叠加效果。
            for (int i = 0; i < index; i++) if (modules[i] == module) return false;
            return !readyAt.TryGetValue(module, out float time) || Context.Time >= time;
        }
        bool Start(BeetleMotionModule module)
        {
            Active = module; activeDuration = Mathf.Clamp(module.duration, .25f, 3);
            Elapsed = 0; hitUsed = false; Starts++;
            readyAt[module] = Context.Time + activeDuration + Mathf.Max(.2f, module.cooldown);
            return true;
        }
        public void Cancel() { Active = null; Elapsed = 0; hitUsed = false; }
        public bool CanHit => Active && !hitUsed;
        public void MarkHit() { if (CanHit) { hitUsed = true; Hits++; } }
        public BeetleMotionSample CombatSample => Listed(Active) ? Active.Sample(Context, Phase) : default;

        public BeetleMotionSample PoseSample()
        {
            var result = CombatSample;
            for (int i = 0; i < modules.Length; i++)
            {
                var m = modules[i]; if (!Overlay(m, i)) continue;
                var s = m.Sample(Context, 0);
                result.BodyPitch += s.BodyPitch; result.BodyRoll += s.BodyRoll;
                result.HeadPitch += s.HeadPitch; result.HeadYaw += s.HeadYaw;
                result.BodyHeight += s.BodyHeight; result.Strain += s.Strain;
            }
            return result;
        }
        bool Overlay(BeetleMotionModule module, int index)
        {
            if (!Listed(module) || module.Channel != BeetleMotionChannel.SupportOverlay || !module.CanStart(Context)) return false;
            for (int i = 0; i < index; i++) if (modules[i] == module) return false;
            return true;
        }
        public bool SampleLeg(int slot, out Vector3 offset)
        {
            // 主动作拥有该腿时，低优先级调整不叠加争夺同一脚端。
            if (Listed(Active) && Active.SampleLeg(Context, Phase, slot, out offset)) return true;
            for (int i = 0; i < modules.Length; i++)
                if (Overlay(modules[i], i) && modules[i].SampleLeg(Context, 0, slot, out offset)) return true;
            offset = Vector3.zero; return false;
        }
    }
}
