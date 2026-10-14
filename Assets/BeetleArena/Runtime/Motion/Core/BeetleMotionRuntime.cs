using System;
using System.Collections.Generic;
using UnityEngine;

namespace BeetleArena
{
    /// <summary>每只虫独立的模块宿主：固定步时钟、冷却、抢占/退出和单次命中锁。</summary>
    public sealed class BeetleMotionRuntime
    {
        readonly Dictionary<BeetleMotionModule, float> readyAt = new Dictionary<BeetleMotionModule, float>();
        // 每步刷新注册表一次，去空/去重保留原顺序；脚端/触角采样不再重复扫描完整配置。
        readonly List<BeetleMotionModule> modules = new List<BeetleMotionModule>(8);
        public BeetleMotionModule Active { get; private set; }
        public BeetleMotionContext Context { get; private set; }
        public float Elapsed { get; private set; }
        public float Phase => Active ? Mathf.Clamp01(Elapsed / activeDuration) : 0;
        public int Starts { get; private set; }
        public int Hits { get; private set; }
        public float ForelegSide { get; private set; }
        float activeDuration;
        public float Duration => activeDuration;
        bool hitUsed;
        BeetleMotionModule previousModule;
        float previousEnd, raisedDriveReady;
        public bool HasActive => Active;

        public void Refresh(BeetleSimulation sim, BeetleState self)
        {
            modules.Clear();
            var configured = sim.Settings.motionModules;
            if (configured != null)
                for (int i = 0; i < configured.Length; i++)
                    if (configured[i] && !modules.Contains(configured[i])) modules.Add(configured[i]);
            Context = new BeetleMotionContext(sim, self);
        }
        bool Listed(BeetleMotionModule module)
        {
            if (!module || !module.enabledInMatch) return false;
            for (int i = 0; i < modules.Count; i++) if (modules[i] == module) return true;
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
        public bool TryStart(float choice, BeetleMotionFamily? family = null)
        {
            if (Active) return false;
            float total = 0;
            for (int i = 0; i < modules.Count; i++) if ((!family.HasValue || modules[i].Family == family.Value) && Eligible(modules[i])) total += MixWeight(modules[i]);
            if (total <= 0) return false;
            float pick = Mathf.Clamp01(choice) * total;
            for (int i = 0; i < modules.Count; i++)
            {
                var module = modules[i]; if ((family.HasValue && module.Family != family.Value) || !Eligible(module)) continue;
                pick -= MixWeight(module);
                if (pick > 0) continue;
                return Start(module);
            }
            return false;
        }
        bool Eligible(BeetleMotionModule module)
        {
            if (!module || !module.enabledInMatch || module.Channel != BeetleMotionChannel.Combat || module.weight <= 0 || !module.CanStart(Context)) return false;
            if (module.Family == BeetleMotionFamily.RaisedDrive && Context.SimulationTime < raisedDriveReady) return false;
            return !readyAt.TryGetValue(module, out float time) || Context.SimulationTime >= time;
        }
        float MixWeight(BeetleMotionModule module)
        {
            // Conditional weights are not match-wide percentages. A recent repetition is
            // less attractive, but remains possible when geometry excludes alternatives.
            float variety = module == previousModule
                ? Mathf.Lerp(.35f, 1, Mathf.Clamp01((Context.SimulationTime - previousEnd) / 5)) : 1;
            return Mathf.Max(0, module.SelectionWeight(Context)) * variety;
        }
        bool Start(BeetleMotionModule module)
        {
            ForelegSide = Context.ForelegSide;
            Active = module; activeDuration = Mathf.Clamp(module.duration / Mathf.Max(.5f, Context.Tempo), .25f, 3);
            Elapsed = 0; hitUsed = false; Starts++;
            readyAt[module] = Context.SimulationTime + activeDuration + Mathf.Max(.2f, module.cooldown);
            if (module.Family == BeetleMotionFamily.RaisedDrive)
                raisedDriveReady = Context.SimulationTime + activeDuration + 1.4f;
            return true;
        }
        public void Cancel()
        {
            if (Active) { previousModule = Active; previousEnd = Context.SimulationTime; }
            Active = null; Elapsed = 0; hitUsed = false;
        }
        public bool CanHit => Active && !hitUsed;
        public void MarkHit() { if (CanHit) { hitUsed = true; Hits++; } }
        public BeetleMotionSample CombatSample => Listed(Active) ? Active.Sample(Context, Phase) : default;

        public BeetleMotionSample PoseSample()
        {
            var result = CombatSample;
            for (int i = 0; i < modules.Count; i++)
            {
                var m = modules[i]; if (!Overlay(m)) continue;
                var s = m.Sample(Context, 0);
                result.BodyPitch += s.BodyPitch; result.BodyRoll += s.BodyRoll;
                result.HeadPitch += s.HeadPitch; result.HeadYaw += s.HeadYaw;
                result.BodyHeight += s.BodyHeight; result.Strain += s.Strain;
                result.PoseResponse = Mathf.Max(result.PoseResponse, s.PoseResponse);
            }
            return result;
        }
        bool Overlay(BeetleMotionModule module) => module && module.enabledInMatch &&
            module.Channel == BeetleMotionChannel.SupportOverlay && module.CanStart(Context);
        // 与脚端一样，主动作优先；删除触角模块只恢复静息姿态，不影响比赛随机数。
        public bool SampleAntenna(int slot, out Vector3 angles)
        {
            if (Listed(Active) && Active.SampleAntenna(Context, Phase, slot, out angles)) return true;
            for (int i = 0; i < modules.Count; i++)
                if (Overlay(modules[i]) && modules[i].SampleAntenna(Context, 0, slot, out angles)) return true;
            angles = Vector3.zero; return false;
        }
        public bool SampleLeg(int slot, out Vector3 offset)
        {
            // Bracing both rear feet is support, not a request to swing both hind legs.
            if (PlantSupportLeg(slot)) { offset = Vector3.zero; return false; }
            // 后足作为同一支撑对仲裁：主动作驱动一侧时，覆盖层不能同时抬另一侧。
            if (slot == 2 || slot == 5)
            {
                if (Listed(Active))
                {
                    bool selected = SampleHind(Active, Phase, slot, out offset, out bool owned);
                    if (owned) return selected;
                }
                for (int i = 0; i < modules.Count; i++)
                {
                    if (!Overlay(modules[i])) continue;
                    bool selected = SampleHind(modules[i], 0, slot, out offset, out bool pairOwned);
                    if (pairOwned) return selected;
                }
                offset = Vector3.zero; return false;
            }
            // 主动作拥有该腿时，低优先级调整不叠加争夺同一脚端。
            if (Listed(Active) && Active.SampleLeg(Context, Phase, slot, out offset)) return true;
            for (int i = 0; i < modules.Count; i++)
                if (Overlay(modules[i]) && modules[i].SampleLeg(Context, 0, slot, out offset)) return true;
            offset = Vector3.zero; return false;
        }
        public float LegSpeed(int slot) => Listed(Active) && Active.SampleLeg(Context, Phase, slot, out _)
            ? Mathf.Clamp(Active.LegSpeed(Context, Phase, slot), 1, 10) : 3;
        public bool PlantSupportLeg(int slot) => (slot == 2 || slot == 5) && Context.Grounded && !Context.CatchingBalance && CombatSample.PlantRearFeet;
        bool SampleHind(BeetleMotionModule module, float phase, int slot, out Vector3 offset, out bool owned)
        {
            bool left = module.SampleLeg(Context, phase, 2, out Vector3 leftOffset);
            bool right = module.SampleLeg(Context, phase, 5, out Vector3 rightOffset);
            owned = left || right;
            // 单个模块意外请求双后足时也保持一侧支撑；正常模块自己安排左右交替。
            bool selected = slot == 2 ? left : right && !left;
            offset = selected ? (slot == 2 ? leftOffset : rightOffset) : Vector3.zero;
            return selected;
        }
#if UNITY_EDITOR
        // 只读解释资格；不调用 TryStart，不消耗比赛随机数，也不改变计时/冷却。
        public string EditorEligibility(BeetleMotionModule module)
        {
            if(!module)return "missing";
            if(!modules.Contains(module))return "not-registered";
            if(!module.enabledInMatch)return "disabled";
            if(module.Channel!=BeetleMotionChannel.Combat)return "support-overlay";
            if(Active)return "another-module-active";
            if(module.weight<=0)return "zero-weight";
            if(module.Family==BeetleMotionFamily.RaisedDrive && Context.SimulationTime<raisedDriveReady)return "raised-drive recovery: "+(raisedDriveReady-Context.SimulationTime).ToString("F2")+"s";
            if(readyAt.TryGetValue(module,out float time) && Context.SimulationTime<time)return "cooldown: "+(time-Context.SimulationTime).ToString("F2")+"s";
            return module.CanStart(Context)?"eligible (not guaranteed selection)":"CanStart=false (see module conditions)";
        }
        // 只用于隔离的编辑器预览。跳过随机选择/资格；沿用实际动作采样和物理反馈。
        public void EditorPreviewStart(BeetleMotionModule module)
        {
            if(!Listed(module) || module.Channel!=BeetleMotionChannel.Combat)throw new InvalidOperationException("Preview needs a registered, enabled combat module.");
            Cancel();Start(module);
        }
#endif
    }
}
