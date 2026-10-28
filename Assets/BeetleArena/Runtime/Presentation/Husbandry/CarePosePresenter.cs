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
        float time, ambientTime, gait, roll, walking, eating, grooming, resting, inspecting,heldPhase,heldEffort;
        Quaternion headPose;
        bool dead;
        public CareFootPlant Feet { get; }
        public CareShellContact Shell { get; }
        public CarePosePresenter(BeetleProceduralAnimator rig,float groundHeight=0)
        {
            this.rig = rig; rig.enabled = false;
            if (rig.importedRig) rig.importedRig.ResetRig();
            bodyRest = rig.bodyPivot.localRotation; bodyPosition = rig.bodyPivot.localPosition;
            headRest = headPose = rig.headPivot.localRotation;
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
            time = ambientTime = gait = walking = eating = grooming = resting = inspecting = heldPhase = heldEffort = 0;
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
            float calm = dead || struggle ? 0 : (1-walking)*(1-eating)*(1-grooming);
            float shift = calm*(Mathf.Sin(time*.83f)+Mathf.Sin(time*.37f)*.4f);
            float bob = dead ? 0 : Mathf.Sin(gait*2) * .006f * walking - resting * .04f + shift*.0015f;
            float height = Mathf.Lerp(bodyPosition.y, .57f, roll / 180) + Mathf.Abs(Mathf.Sin(roll * Mathf.Deg2Rad)) * .4f;
            rig.bodyPivot.localPosition = new Vector3(bodyPosition.x, height + bob, bodyPosition.z);
            rig.bodyPivot.localRotation = bodyRest * Quaternion.Euler(state == CareState.Startled ? -5 : eating * 2 + shift*.16f, 0, roll + rocking + shift*.12f);
            headPose = headRest * Quaternion.Euler(dead ? 0 : eating * (5 + Mathf.Sin(time * 12) * 2) - resting * 3,
                dead ? 0 : inspecting * Mathf.Sin(time * 2) * 9, 0);
            rig.headPivot.localRotation = headPose;
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
            PresentAmbient(state,dt);
            if(roll>=12)Shell.Settle(rig.bodyPivot,Feet.GroundHeight,roll>120,Feet.SurfaceHeight);
            if(!dead&&roll<12)Feet.Present(state,dt,speed);else Feet.Reset();
        }

        // The calendar can pause transactions and travel while a living beetle still senses its surroundings.
        // Only the head and antennae advance here; planted feet, gait, food progress and the root stay untouched.
        public void PresentAmbient(CareState state,float dt)
        {
            if(dead||state==CareState.Dead||state==CareState.Unavailable||dt<=0)return;
            ambientTime+=dt;
            float quiet=state==CareState.Resting||state==CareState.Growing?.3f:1;
            float sensing=(1-eating)*(1-grooming)*quiet;
            rig.headPivot.localRotation=headPose*Quaternion.Euler(Mathf.Sin(ambientTime*.91f)*.65f*sensing,
                (Mathf.Sin(ambientTime*.67f)+Mathf.Sin(ambientTime*1.13f)*.3f)*2.2f*sensing,0);
            for(int i=0;i<antennae.Length;i++)
            {
                float sweep=Mathf.Sin(ambientTime*(1.31f+i*.23f)+i*2)+Mathf.Sin(ambientTime*.53f+i)*.35f;
                float lift=Mathf.Sin(ambientTime*.89f+i*1.4f)*1.4f*quiet;
                rig.antennae[i].localRotation=antennae[i]*Quaternion.Euler(lift,sweep*(6+inspecting*8)*quiet,0);
            }
        }
        public void PresentHeld(float dt,float struggle)
        {
            time+=dt;dead=false;roll=walking=eating=grooming=resting=inspecting=0;
            heldEffort=Mathf.Lerp(heldEffort,Mathf.Clamp01(struggle),1-Mathf.Exp(-dt*22));
            heldPhase+=dt*(12+heldEffort*14+Mathf.Sin(time*1.7f)*2);
            Feet.Reset();
            rig.bodyPivot.localPosition=bodyPosition;
            float twist=(Mathf.Sin(heldPhase*.59f)+Mathf.Sin(heldPhase*.93f+1.1f)*.35f)/1.35f;
            float buck=(Mathf.Sin(heldPhase*.73f+.5f)+Mathf.Sin(heldPhase*1.21f)*.25f)/1.25f;
            rig.bodyPivot.localRotation=bodyRest*Quaternion.Euler(3+buck*heldEffort*9,
                twist*heldEffort*7,twist*heldEffort*13);
            headPose=headRest*Quaternion.Euler(-2-heldEffort*(6+Mathf.Max(0,buck)*5),
                Mathf.Sin(heldPhase*.81f)*heldEffort*7,twist*heldEffort*3);
            for(int i=0;i<hips.Length;i++)
            {
                var leg=rig.legs[i];
                float phase=heldPhase*(.86f+i*.083f)+i*2.17f+Mathf.Sin(time*(1.2f+i*.17f)+i)*.45f;
                float reach=(Mathf.Sin(phase)+Mathf.Sin(phase*1.73f+i)*.3f)/1.3f;
                float kick=Mathf.Pow(Mathf.Max(0,Mathf.Sin(phase+.65f)),3);
                float twitch=Mathf.Sin(time*(2.1f+i*.31f)+i*1.9f)*1.5f;
                leg.hip.localRotation=hips[i]*Quaternion.Euler(6+twitch+heldEffort*(kick*44-reach*16),
                    leg.side*heldEffort*(reach*17+Mathf.Sin(phase*.61f)*6),0);
                leg.knee.localRotation=knees[i]*Quaternion.Euler(0,0,leg.side*(12+twitch+heldEffort*(20+reach*28)));
                leg.ankle.localRotation=ankles[i]*Quaternion.Euler(0,0,leg.side*heldEffort*(kick*24-reach*16));
            }
            PresentAmbient(CareState.Idle,dt);
            for(int i=0;i<antennae.Length;i++)
                rig.antennae[i].localRotation*=Quaternion.Euler(Mathf.Sin(heldPhase*(.7f+i*.13f)+i)*heldEffort*8,
                    Mathf.Sin(heldPhase*(.91f+i*.17f)+i*2)*heldEffort*13,0);
        }
    }
}
