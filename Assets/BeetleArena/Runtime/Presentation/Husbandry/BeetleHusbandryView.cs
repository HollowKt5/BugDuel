using BugDuel.Domain;
using UnityEngine;

namespace BeetleArena
{
    /// <summary>养殖专用姿态表现。只读个体快照；不用比赛状态模拟养殖，也不由骨骼决定死亡。</summary>
    public sealed class BeetleHusbandryView : MonoBehaviour
    {
        public BeetleHusbandryController controller;
        public BeetleProceduralAnimator sourceRig;
        Quaternion[] hips, knees, ankles, antennae;
        Quaternion headRest;
        Vector3 bodyRest;
        string displayedId;
        float time, roll, eating;
        double previousNutrition;
        bool initialized;

        void Initialize()
        {
            if (initialized || !sourceRig) return;
            sourceRig.enabled = false;
            if (sourceRig.importedRig) sourceRig.importedRig.ResetRig();
            bodyRest = sourceRig.bodyPivot.localPosition;
            headRest = sourceRig.headPivot.localRotation;
            int count = sourceRig.legs.Length;
            hips = new Quaternion[count]; knees = new Quaternion[count]; ankles = new Quaternion[count];
            for (int i = 0; i < count; i++)
            {
                var leg = sourceRig.legs[i];
                hips[i] = leg.hip.localRotation; knees[i] = leg.knee.localRotation; ankles[i] = leg.ankle.localRotation;
            }
            antennae = new Quaternion[sourceRig.antennae.Length];
            for (int i = 0; i < antennae.Length; i++) antennae[i] = sourceRig.antennae[i].localRotation;
            initialized = true;
        }

        void LateUpdate() => Present(Time.unscaledDeltaTime);

        public void Present(float dt)
        {
            if (!controller || !sourceRig || !float.IsFinite(dt) || dt < 0) return;
            BeetleIndividual individual = controller.Selected;
            if (individual == null) return;
            Initialize();
            bool changed = displayedId != individual.Id;
            if (changed)
            {
                displayedId = individual.Id; time = eating = 0;
                roll = individual.IsOverturned ? 180 : 0;
                previousNutrition = individual.Condition.Nutrition;
                for (int i = 0; i < hips.Length; i++)
                {
                    sourceRig.legs[i].hip.localRotation = hips[i];
                    sourceRig.legs[i].knee.localRotation = knees[i];
                    sourceRig.legs[i].ankle.localRotation = ankles[i];
                }
            }
            if (!changed && controller.IsPaused) return;
            bool dead = individual.Stage == LifeStage.Dead;
            if (!changed && dead) return; // 死亡冻结最后姿态，不能继续播放挣扎。
            if (individual.Condition.Nutrition > previousNutrition + .05) eating = 2;
            previousNutrition = individual.Condition.Nutrition;
            time += dt;
            eating = Mathf.Max(0, eating - dt);
            bool struggling = individual.IsStruggling;
            roll = Mathf.Lerp(roll, individual.IsOverturned ? 180 : 0, 1 - Mathf.Exp(-dt * 6));
            float rocking = struggling ? Mathf.Sin(time * 7.5f) * 8 : 0;
            sourceRig.bodyPivot.localRotation = Quaternion.Euler(struggling ? Mathf.Sin(time * 5) * 3 : 0, 0, roll + rocking);
            // 翻倒时由背壳接近土面支撑；扶正过程保留一点抬起，防身体转动穿入地面。
            float turn = Mathf.Abs(Mathf.Sin(roll * Mathf.Deg2Rad));
            float settledHeight = Mathf.Lerp(bodyRest.y, .57f, Mathf.Clamp01(roll / 180));
            sourceRig.bodyPivot.localPosition = new Vector3(bodyRest.x, settledHeight + turn * .4f, bodyRest.z);
            if (dead) return;
            for (int i = 0; i < hips.Length; i++)
            {
                var leg = sourceRig.legs[i];
                float phase = time * (struggling ? 10 : 1.2f) + i * 1.7f;
                float wave = Mathf.Sin(phase), amplitude = struggling ? 24 : 1.2f;
                leg.hip.localRotation = hips[i] * Quaternion.Euler(wave * amplitude, Mathf.Cos(phase) * amplitude * .35f, 0);
                leg.knee.localRotation = knees[i] * Quaternion.Euler(0, 0, leg.side * (struggling ? 20 + 22 * Mathf.Sin(phase + .8f) : 0));
                leg.ankle.localRotation = ankles[i] * Quaternion.Euler(0, 0, struggling ? wave * 16 : 0);
            }
            sourceRig.headPivot.localRotation = headRest * Quaternion.Euler(eating > 0 ? 5 + 3 * Mathf.Sin(time * 15) : 0, 0, 0);
            for (int i = 0; i < antennae.Length; i++)
                sourceRig.antennae[i].localRotation = antennae[i] * Quaternion.Euler(0, Mathf.Sin(time * 2 + i * 2) * (struggling ? 10 : 4), 0);
        }
    }
}
