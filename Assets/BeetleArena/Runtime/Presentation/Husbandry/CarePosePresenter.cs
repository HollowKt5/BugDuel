using BugDuel.Domain;
using UnityEngine;

namespace BeetleArena
{
    /// <summary>Uses the model's bindings only; never runs combat animation or simulation.</summary>
    public sealed class CarePosePresenter
    {
        readonly BeetleProceduralAnimator rig;
        readonly Quaternion bodyRest, headRest;
        readonly Vector3 bodyPosition;
        readonly Quaternion[] hips, knees, ankles, antennae;
        float time, gait, roll, walking, eating, grooming, resting, inspecting;
        bool dead;
        public CareFootPlant Feet { get; }
        public CareShellContact Shell { get; }
        public CarePosePresenter(BeetleProceduralAnimator rig,float groundHeight=0)
        {
            this.rig = rig; rig.enabled = false;
            if (rig.importedRig) rig.importedRig.ResetRig();
            bodyRest = rig.bodyPivot.localRotation; bodyPosition = rig.bodyPivot.localPosition;
            headRest = rig.headPivot.localRotation;
            int n = rig.legs.Length; hips = new Quaternion[n]; knees = new Quaternion[n]; ankles = new Quaternion[n];
            for (int i = 0; i < n; i++)
            {
                var leg = rig.legs[i]; hips[i] = leg.hip.localRotation;
                knees[i] = leg.knee.localRotation; ankles[i] = leg.ankle.localRotation;
            }
            antennae = new Quaternion[rig.antennae.Length];
            for (int i = 0; i < antennae.Length; i++) antennae[i] = rig.antennae[i].localRotation;
            Feet=new CareFootPlant(rig,groundHeight);
            Shell=new CareShellContact(rig);
        }
        public void Reset(bool overturned, bool isDead)
        {
            time = gait = walking = eating = grooming = resting = inspecting = 0;
            roll = overturned || isDead ? 180 : 0; dead = false;
            Feet.Reset();
            Present(isDead ? CareState.Dead : CareState.Idle, 0, 0);
        }
        public void Present(CareState state, float speed, float dt)
        {
            if (state == CareState.Dead && (dead || time > 0)) { dead = true; return; }
            dead = state == CareState.Dead;
            time += dt; gait += speed * dt * 23;
            float blend = 1 - Mathf.Exp(-dt * 9);
            bool struggle = state == CareState.Struggling;
            walking = Mathf.Lerp(walking, speed > .01f ? Mathf.Clamp01(speed / .2f) : 0, blend);
            eating = Mathf.Lerp(eating, state == CareState.Eating ? 1 : 0, blend);
            grooming = Mathf.Lerp(grooming, state == CareState.Grooming ? 1 : 0, blend);
            resting = Mathf.Lerp(resting, state == CareState.Resting || state == CareState.Growing ? 1 : 0, blend);
            inspecting = Mathf.Lerp(inspecting, state == CareState.Inspecting ? 1 : 0, blend);
            roll = Mathf.Lerp(roll, struggle ? 180 : state == CareState.Dead ? roll : 0, 1 - Mathf.Exp(-dt * 6));
            float rocking = struggle ? Mathf.Sin(time * 8) * 8 : 0;
            float bob = dead ? 0 : Mathf.Abs(Mathf.Sin(gait)) * .018f * walking - resting * .04f;
            float height = Mathf.Lerp(bodyPosition.y, .57f, roll / 180) + Mathf.Abs(Mathf.Sin(roll * Mathf.Deg2Rad)) * .4f;
            rig.bodyPivot.localPosition = new Vector3(bodyPosition.x, height + bob, bodyPosition.z);
            rig.bodyPivot.localRotation = bodyRest * Quaternion.Euler(state == CareState.Startled ? -5 : eating * 2, 0, roll + rocking);
            rig.headPivot.localRotation = headRest * Quaternion.Euler(dead ? 0 : eating * (5 + Mathf.Sin(time * 12) * 2) - resting * 3,
                dead ? 0 : inspecting * Mathf.Sin(time * 2) * 9, 0);
            for (int i = 0; i < hips.Length; i++)
            {
                var leg = rig.legs[i]; float wave = Mathf.Sin(gait + (leg.pair % 2 == 0 ? 0 : Mathf.PI) + (leg.side < 0 ? Mathf.PI : 0));
                float flail = Mathf.Sin(time * 11 + i * 1.7f);
                float clean = leg.pair == 0 ? grooming : 0;
                var hip = hips[i] * Quaternion.Euler(dead ? 0 : struggle ? flail * 24 : wave * walking * 13 + clean * (12 + Mathf.Sin(time * 7) * 8),
                    dead ? 0 : walking * wave * 5, 0);
                var knee = knees[i] * Quaternion.Euler(0, 0, dead ? 0 : leg.side *
                    (struggle ? 20 + flail * 22 : Mathf.Max(0, wave) * walking * 15 + clean * 12 + resting * 3));
                leg.hip.localRotation = hip; leg.knee.localRotation = knee;
                leg.ankle.localRotation = ankles[i] * Quaternion.Euler(0, 0, dead ? 0 : struggle ? flail * 16 : -wave * walking * 5);
            }
            for (int i = 0; i < antennae.Length; i++)
                rig.antennae[i].localRotation = antennae[i] * Quaternion.Euler(0, dead ? 0 :
                    Mathf.Sin(time * (struggle ? 7 : 2) + i * 2) * (struggle ? 10 : (4 + inspecting * 10) * (1 - resting * .8f)), 0);
            if(roll>=12)Shell.Settle(rig.bodyPivot,Feet.GroundHeight,roll>120,Feet.SurfaceHeight);
            if(!dead&&roll<12)Feet.Present(state,dt);else Feet.Reset();
        }
    }
}
