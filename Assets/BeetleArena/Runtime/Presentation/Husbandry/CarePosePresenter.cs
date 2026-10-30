using BugDuel.Domain;
using UnityEngine;

namespace BeetleArena
{
    /// <summary>Uses the model's bindings only; never runs combat animation or simulation.</summary>
    public sealed class CarePosePresenter
    {
        readonly BeetleProceduralAnimator rig;
        readonly BeetleJawRig jaws;
        readonly Quaternion bodyRest, headRest;
        readonly Vector3 bodyPosition;
        readonly Quaternion[] hips, knees, ankles, antennae;
        float time, ambientTime, gait, roll, walking, eating, grooming, resting,heldPhase,heldEffort,supportLoad;
        float focusYaw,probeTime,probeDuration,probeTarget,antennaYaw,headYaw,strokeDuration=.32f;
        float personalityPhase,tempo=1,stateTime,startled,inspecting,bite;
        CareState previousState;
        int strokeSide=1;
        uint random=0x1a237u;
        bool held,followingRoute;
        Quaternion headPose;
        bool dead;
        public CareFootPlant Feet { get; }
        public CareShellContact Shell { get; }
        public CarePosePresenter(BeetleProceduralAnimator rig,float groundHeight=0)
        {
            this.rig = rig; rig.enabled = false;
            rig.PrepareModel();
            jaws=rig.GetComponentInChildren<BeetleJawRig>(true);
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
        public void Reset(bool overturned, bool isDead,uint seed=0x1a237u)
        {
            time = ambientTime = gait = walking = eating = grooming = resting = heldPhase = heldEffort = supportLoad = 0;
            focusYaw=probeTime=probeDuration=probeTarget=antennaYaw=headYaw=0;
            strokeDuration=.32f;strokeSide=1;held=followingRoute=false;random=seed==0?1:seed;
            personalityPhase=Roll()*Mathf.PI*2;tempo=.88f+Roll()*.24f;
            stateTime=startled=inspecting=bite=0;previousState=CareState.Idle;
            if(jaws)jaws.ResetPose();
            if(rig.appendages)rig.appendages.ResetPose();
            roll = overturned || isDead ? 180 : 0; dead = false;
            Feet.Reset();
            Present(isDead ? CareState.Dead : CareState.Idle, 0, 0);
        }
        public void Present(CareState state, float speed, float dt,float lookYaw=0)
        {
            if (state == CareState.Dead && (dead || time > 0)) { dead = true; return; }
            dead = state == CareState.Dead;
            if(state!=previousState){stateTime=0;previousState=state;}
            stateTime+=dt;
            held=false;focusYaw=Mathf.Clamp(lookYaw,-55,55);
            followingRoute=state==CareState.Wandering||state==CareState.SeekingFood||state==CareState.SeekingShelter;
            time += dt; gait += speed * dt * 12.5f;
            float blend = 1 - Mathf.Exp(-dt * 9);
            bool struggle = state == CareState.Struggling;
            walking = Mathf.Lerp(walking, speed > .01f ? Mathf.Clamp01(speed / .2f) : 0, 1-Mathf.Exp(-dt*6));
            eating = Mathf.Lerp(eating, state == CareState.Eating ? 1 : 0, blend);
            grooming = Mathf.Lerp(grooming, state == CareState.Grooming ? 1 : 0, blend);
            resting = Mathf.Lerp(resting, state == CareState.Resting || state == CareState.Growing ? 1 : 0, blend);
            startled=Mathf.Lerp(startled,state==CareState.Startled?1:0,1-Mathf.Exp(-dt*12));
            inspecting=Mathf.Lerp(inspecting,state==CareState.Inspecting?1:0,blend);
            // Small feeding bouts have a lift, two mouthfuls and a pause to swallow.
            float mealPhase=Mathf.Repeat(stateTime*tempo,1.65f);
            bite=eating*(Mathf.Max(0,Stroke(mealPhase/.58f))+Mathf.Max(0,Stroke((mealPhase-.55f)/.58f)));
            roll = Mathf.Lerp(roll, struggle ? 180 : state == CareState.Dead ? roll : 0, 1 - Mathf.Exp(-dt * 6));
            float effort=Stroke(Mathf.Repeat(time,1.55f)/.95f);
            float rocking = struggle ? effort * Mathf.Sin(time*3.1f) * 11 : 0;
            float calm = dead || struggle ? 0 : (1-walking)*(1-eating)*(1-grooming);
            float shift = calm*(Mathf.Sin(time*.83f+personalityPhase)+Mathf.Sin(time*.37f+personalityPhase)*.4f);
            // Settle a little when the returning tripod accepts weight. The shell no longer
            // bounces on an independent sine wave while its feet are trying to stay planted.
            supportLoad=Mathf.Lerp(supportLoad,Feet.SupportCount>=5?1:0,1-Mathf.Exp(-dt*6));
            float bob = dead ? 0 : -supportLoad*.002f*walking - resting*.04f + shift*.0007f;
            float height = Mathf.Lerp(bodyPosition.y, .57f, roll / 180) + Mathf.Abs(Mathf.Sin(roll * Mathf.Deg2Rad)) * .4f;
            rig.bodyPivot.localPosition = new Vector3(bodyPosition.x, height + bob, bodyPosition.z);
            rig.bodyPivot.localRotation = bodyRest * Quaternion.Euler(-startled*4+eating*2+shift*.07f, 0, roll + rocking + shift*.05f);
            headPose = headRest * Quaternion.Euler(dead ? 0 : eating*3+bite*3-resting*3-startled*6-
                inspecting*(1.5f+Mathf.Sin(stateTime*tempo*2)*1.2f),0,0);
            rig.headPivot.localRotation = headPose;
            if(jaws&&!dead)jaws.SetPose(bite*.72f,bite*.24f);
            if(rig.appendages&&!dead)
            {
                rig.appendages.SetMouth(bite*.72f,bite*.24f);
                rig.appendages.Advance(dt);
            }
            for (int i = 0; i < hips.Length; i++)
            {
                var leg = rig.legs[i]; float wave = Mathf.Sin(gait + (leg.pair % 2 == 0 ? 0 : Mathf.PI) + (leg.side < 0 ? Mathf.PI : 0));
                float flail = Stroke(Mathf.Repeat(time,1.55f)/.95f-leg.pair*.1f-(leg.side<0?.14f:0));
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
            ambientTime+=dt*tempo;
            float quiet=state==CareState.Resting||state==CareState.Growing?.3f:1;
            float sensing=(1-eating)*(1-grooming)*quiet;
            probeTime+=dt;
            if(probeTime>=probeDuration)
            {
                probeTime=0;probeDuration=.85f+Roll()*1.1f;
                probeTarget=(Roll()*2-1)*(state==CareState.Inspecting?28:13);
            }
            float aim=followingRoute?focusYaw:probeTarget;
            float reach=1-Mathf.SmoothStep(0,1,Mathf.Clamp01((probeTime/probeDuration-.55f)/.45f));
            if(!followingRoute)aim*=reach;
            antennaYaw=Mathf.Lerp(antennaYaw,aim,1-Mathf.Exp(-dt*16));
            if(probeTime>.12f||followingRoute)
                headYaw=Mathf.Lerp(headYaw,antennaYaw*(followingRoute?.42f:.32f),1-Mathf.Exp(-dt*5));
            rig.headPivot.localRotation=headPose*Quaternion.Euler(Mathf.Sin(ambientTime*.91f)*.65f*sensing,
                headYaw*sensing,0);
            for(int i=0;i<antennae.Length;i++)
            {
                float sweep=Mathf.Sin(ambientTime*(1.31f+i*.23f)+i*2+personalityPhase)*2;
                float lift=Mathf.Sin(ambientTime*.89f+i*1.4f+personalityPhase)*1.4f*quiet;
                float side=i%2==0?-1:1;
                float probe=Stroke(probeTime/.7f-(i%2)*.11f);
                rig.antennae[i].localRotation=antennae[i]*Quaternion.Euler(lift-probe*3*quiet-startled*9,
                    (antennaYaw*(side*antennaYaw>0?1:.7f)+sweep+side*probe*4)*quiet+side*startled*12,0);
            }
            if(rig.appendages)
            {
                rig.appendages.SetSensing((Mathf.Sin(ambientTime*.89f)*1.4f-startled*9)/30f,antennaYaw*quiet/45f);
                rig.appendages.Apply(rig.appendages.Pose);
            }
        }
        public void PresentHeld(float dt,float struggle)
        {
            if(!held){heldPhase=heldEffort=0;strokeDuration=.32f;strokeSide=1;held=true;}
            time+=dt;dead=false;roll=walking=eating=grooming=resting=0;
            focusYaw=0;followingRoute=false;
            heldEffort=Mathf.Lerp(heldEffort,Mathf.Clamp01(struggle),1-Mathf.Exp(-dt*22));
            if(heldEffort>.01f)heldPhase+=dt;else heldPhase=0;
            if(heldPhase>=strokeDuration)
            { heldPhase%=strokeDuration;strokeDuration=.26f+Roll()*.14f;strokeSide=-strokeSide; }
            float phase=heldPhase/strokeDuration;
            Feet.Reset();
            if(jaws)jaws.SetPose(heldEffort*.2f);
            if(rig.appendages)
            {
                rig.appendages.SetMouth(heldEffort*.2f);
                rig.appendages.Advance(dt);
            }
            rig.bodyPivot.localPosition=bodyPosition;
            float twist=Stroke((phase-.08f)/.92f)*strokeSide;
            float buck=Stroke((phase-.15f)/.85f);
            rig.bodyPivot.localRotation=bodyRest*Quaternion.Euler(3+buck*heldEffort*9,
                twist*heldEffort*7,twist*heldEffort*13);
            headPose=headRest*Quaternion.Euler(-2-heldEffort*(6+Mathf.Max(0,buck)*5),
                twist*heldEffort*7,twist*heldEffort*3);
            for(int i=0;i<hips.Length;i++)
            {
                var leg=rig.legs[i];
                float offset=leg.pair*.075f+(leg.side==strokeSide?0:.12f);
                float kick=Stroke((phase-offset)/(1-offset)),reach=Stroke((phase-offset-.09f)/(.91f-offset));
                float direction=leg.pair==2?-1:1;
                leg.hip.localRotation=hips[i]*Quaternion.Euler(6+heldEffort*(kick*44*direction-reach*12),
                    leg.side*heldEffort*(reach*23),0);
                leg.knee.localRotation=knees[i]*Quaternion.Euler(0,0,leg.side*(12+heldEffort*(8+reach*36)));
                leg.ankle.localRotation=ankles[i]*Quaternion.Euler(0,0,leg.side*heldEffort*(kick*24-reach*16));
            }
            PresentAmbient(CareState.Idle,dt);
            for(int i=0;i<antennae.Length;i++)
                rig.antennae[i].localRotation*=Quaternion.Euler(-buck*heldEffort*8,twist*heldEffort*13,0);
        }
        // Brief preload, quick exertion, slower recovery, then rest; not a continuous sine cycle.
        static float Stroke(float t)
        {
            if(t<=0||t>=1)return 0;
            if(t<.18f)return -.22f*Mathf.SmoothStep(0,1,t/.18f);
            if(t<.36f)return Mathf.Lerp(-.22f,1,Mathf.SmoothStep(0,1,(t-.18f)/.18f));
            return 1-Mathf.SmoothStep(0,1,(t-.36f)/.64f);
        }
        float Roll() { uint x=random;x^=x<<13;x^=x>>17;x^=x<<5;random=x;return x/((float)uint.MaxValue+1); }
    }
}
