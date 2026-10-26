using UnityEngine;

namespace BeetleArena
{
    [CreateAssetMenu(menuName = "Beetle Arena/Match Settings")]
    // Central configuration asset: Inspector tuning for arena size, motion, pacing, encouragement, and throw parameters.
    // Mutating shared assets affects subsequent matches; defaults represent gameplay tuning, not empirical biological measurements.
    public sealed class BeetleMatchSettings : ScriptableObject
    {
        [Header("Arena / world units")]
        // Circular arena radius in world units; aligns with stump geometry and ring-out/tarsal support queries.
        [Min(3.5f)] public float arenaRadius = 5.2f;
        // Initial distance of each beetle from center; actual initial separation is twice this value.
        [Min(1f)] public float spawnDistance = 2.3f;
        // Base model visual scale, subsequently scaled by fighter size; recalibrate reach and collisions if modified.
        [Min(.1f)] public float visualScale = .70f;
        [Tooltip("Model-local nominal foot contacts, in left-front/middle/hind then right-front/middle/hind order.")]
        // Measured foot locations for production model. Empty array retains fallback stance layout for legacy models.
        public Vector3[] supportFootOffsets = new Vector3[0];
        // Root depth below arena rim before latching ring-out, preventing edge grazes or transient jumps from triggering premature loss.
        [Min(.1f)] public float fallDepth = .75f;
        // Out-of-ring floor Y elevation (arena rim is Y=0), used for settlement of fallen fighters and ground generation.
        public float groundHeight = -3.4f;
        [Header("Pluggable motion modules / order is stable selection order")]
        // Asset list acts as registry: removing reference or disabling module halts usage immediately; empty list reverts to baseline combat.
        public BeetleMotionModule[] motionModules = System.Array.Empty<BeetleMotionModule>();
        [Range(0, 1)] public float moduleActionChance = .42f;
        [Header("Round finish / normalized game rules")]
        [Range(0, 1)] public float forelegActionChance = .48f;
        // Vertical game gravity acceleration; uses standard gravity magnitudes, though world scale is not metric millimeter-based.
        [Min(1f)] public float gravity = 9.81f;
        [Header("Matched size variation")]
        // Lower bound for fighter size randomization range; 1 denotes baseline size.
        [Range(.85f, 1f)] public float minimumSize = .96f;
        // Upper bound for fighter size randomization range; pairing is additionally constrained by maximumSizeDifference.
        [Range(1f, 1.15f)] public float maximumSize = 1.04f;
        [Range(0, .15f), Tooltip("Maximum (larger/smaller - 1); 0.08 means 8%.")]
        // Upper limit for (LargerSize / SmallerSize) - 1; 0.08 caps size disparity between fighters at ~8%.
        public float maximumSizeDifference = .08f;
        // Baseline display body length (mm), scaled linearly with size; does not represent direct scene Transform dimension.
        [Min(1)] public float referenceLengthMm = 55f;
        // Baseline display mass (grams), scaled with size^3; simulation motion uses independent normalized kinematic mass.
        [Min(1)] public float referenceMassGrams = 8f;
        [Header("Locomotion and contest")]
        // Base planar movement speed; actual speed is scaled by agility, stamina, and action multipliers.
        [Min(.1f)] public float walkSpeed = .95f;
        // Base turn rate (degrees/sec); dampened further during committed actions.
        [Min(1)] public float turnSpeed = 145f;
        // Base push impulse, scaled by effort, concession factor, and match escalation multiplier.
        [Min(.1f)] public float shoveImpulse = 3.6f;
        // Base lifting strength; 4.3 is standard force; scales traction overcome without directly imparting backward push impulse.
        [Min(.1f)] public float pryImpulse = 4.3f;
        // Stamina recovery rate during full rest (points/sec); stance holding or retreat uses a fraction of this rate.
        [Min(.1f)] public float staminaRecovery = 16f;
        // Base fatigue accumulation rate during effort/threat (points/sec); amplified by match escalation.
        [Range(0, 2)] public float fatiguePerSecond = .12f;
        [Header("Bout pacing / seconds, not a forced result timer")]
        // Delay before late-match escalation begins; distinct from short-cycle probingSeconds.
        [Min(0)] public float openingSeconds = 12f;
        // Duration over which push force multiplier transitions from opening to late-match scale; not a match-ending countdown.
        [Min(10)] public float escalationSeconds = 55f;
        // Opening attack displacement multiplier for restrained early pushing; valid ring-out still immediately triggers match resolution.
        [Range(.05f, 1f)] public float openingDisplacement = .75f;
        // Late-match attack displacement multiplier, increasing ring-out opportunities without guaranteeing fixed-duration termination.
        [Range(1f, 3f)] public float finalDisplacement = 2.25f;
        [Header("Exchange rhythm / seconds")]
        // Duration of light probing contact during short-cycle pacing.
        [Min(2)] public float probingSeconds = 7f;
        // Duration of intense engagement in short-cycle pacing, permitting charges, clinches, and lifts.
        [Min(2)] public float clashSeconds = 9f;
        // Duration of regrouping window (does not force simultaneous rest); renewed contact exertion can exit early.
        [Min(.4f)] public float exchangeRecoverySeconds = 1.35f;
        [Header("Individual recovery / game tuning, not measured biology")]
        // Exits recovery once stamina reaches this fraction of current available ceiling, rather than waiting for 84%.
        [Range(.25f, .7f)] public float recoveryResumeFraction = .58f;
        // Minimum duration of brief unloading and hard cap for single continuous recovery; threat proximity prompts earlier stance holding.
        [Range(.2f, .8f)] public float recoveryMinimumSeconds = .35f;
        [Range(.8f, 4f)] public float recoveryMaximumSeconds = 2.2f;
        // Observes, braces, or approaches upon exit, preventing re-entering recovery within the same frame to bypass caps.
        [Range(.3f, 2f)] public float recoveryRetrySeconds = .85f;
        // Effort multiplier for light probe relative to standard push; probes do not evaluate throw transitions.
        [Range(.1f, .6f)] public float probeForceMultiplier = .3f;
        // Extra effort multiplier for heavy charge; still requires alignment, active window, and physical contact to resolve.
        [Range(1f, 2f)] public float chargeForceMultiplier = 1.35f;
        // Baseline charge windup duration, slightly perturbed by random variance during action selection.
        [Min(.2f)] public float chargeWindupSeconds = .65f;
        // Target duration of a horn clinch; exits early upon separation or opponent liftoff.
        [Min(.3f)] public float hornLockSeconds = 1.15f;
        [Range(.05f,.6f), Tooltip("Normalized pry phase at which actual contact may transfer the lifting impulse.")]
        // Long cephalic horn breaks contact rapidly upon lifting; active force window should open during initial elevation.
        public float pryContactStart = .16f;
        [Header("Horn-supported lift / seconds and degrees")]
        // Lift elevates anterior body around contact pivot, maintains load-bearing stance, accelerates into pitch snap and throw; failed entry retreats.
        [Range(.3f, 1f)] public float liftRaiseSeconds = .65f;
        [Range(.25f, 1.2f)] public float liftHoldSeconds = .9f;
        [Range(20, 55)] public float liftedBodyPitch = 42f;
        [Range(.5f, 4f)] public float liftFollowSpeed = 2.8f;
        // Accumulated time off ground, including horn support phase; initiates tarsal struggling past 0.7s.
        [Min(0)] public float liftStruggleDelay = .7f;
        // Frequency in cycles per second. Gradually accelerates from slow tactile probing after 0.7s of lost support.
        [Min(.1f)] public float struggleStartHz = .65f;
        [Min(.1f)] public float struggleMaxHz = 3.2f;
        [Min(.1f)] public float struggleRampSeconds = 1.1f;
        // Total contact separation displacement budget from animation (world units/sec); not a per-iteration cap.
        // Scales down step pose advance when exceeded and re-evaluates all colliders, rather than truncating push-out distance and leaving overlaps.
        [Range(1, 6)] public float contactMotionSpeed = 4f;
        // Retracts horn actively when lift is continually obstructed, avoiding freezing both fighters until lift duration expires.
        [Range(.1f, .4f)] public float liftBlockedReleaseSeconds = .16f;
        [Header("Ring-out recovery / artistic timing, not measured species constants")]
        // Briefly probes for ground upon footing loss, followed by out-of-phase leg swing; buffers landing before pushing up. Decoupled from 0.7s struggle threshold.
        [Range(.08f, .5f)] public float fallReachSeconds = .18f;
        [Range(.4f, 3f)] public float fallRecoverySeconds = 1.35f;
        // Used only with actual surface contact during post-landing sliding (table or forest).
        [Range(1, 12)] public float fallSlideDamping = 5.5f;
        [Header("Lever throw / normalized game units")]
        // Torque derives from muscle input force * input lever arm; actual output moment arm is measured from horn contact geometry.
        [Min(1)] public float throwMuscleForce = 34f;
        [Min(.05f)] public float throwInputArm = .42f;
        [Range(.1f,1)] public float throwEfficiency = .68f;
        [Min(1)] public float throwWorkLimit = 65f;
        [Min(.1f)] public float throwMinimumSpeed = 2.2f;
        [Min(1)] public float throwMaximumSpeed = 8f;
        [Tooltip("High-energy throw launch speed gain; stylized game scale, smoothly applied above Arc tier.")]
        [Range(1f, 3f)] public float throwHeavySpeedMultiplier = 2.2f;
        [Header("Throw inspection / temporary elevated selection frequency")]
        [Tooltip("Inspect throws more often: eligible pry selection 35-85%, extra burst 85%. Contact, support and energy gates remain active.")]
        public bool throwInspectionMode;
        [Header("Six throw tiers / nonuniform speed thresholds")]
        // First four tiers partitioned by velocity; final two tiers require meeting pitch angle / work thresholds with backward trajectory.
        [Min(.1f)] public float throwQuickSpeed = 3.15f;
        [Min(.1f)] public float throwArcSpeed = 4.35f;
        [Min(.1f)] public float throwHighArcSpeed = 5.65f;
        [Min(.1f)] public float throwPowerOverheadSpeed = 7.1f;
        [Range(50,130)] public float throwOverheadSweep = 82f;
        [Min(1)] public float throwOverheadWork = 32f;
        [Min(.1f)] public float throwOverheadAngularSpeed = 2.5f;
        [Range(.1f,.5f)] public float throwSnapSeconds = .22f;
        [Header("Tactical energy / game approximation")]
        [Range(.1f, 1)] public float exertionRecoveryDelay = .42f;
        [Range(.1f, 1)] public float contactRecoveryMultiplier = .55f;
        [Range(.1f, .8f)] public float burstDrainPerSecond = .34f;
        [Range(.1f, .8f)] public float burstRecoveryPerSecond = .28f;
        [Range(.15f, 1)] public float tacticalResponseSeconds = .30f;
        [Range(.2f, 1)] public float missedAttackExposure = .62f;
        [Range(.5f, 8)] public float failedPryRetrySeconds = 3.5f;
        [Header("Contest stamina")]
        // Stamina burn rate during heavy charge (points/sec), not a one-off fixed deduction.
        [Min(1)] public float chargeStaminaCost = 27f;
        // Stamina burn rate during continuous clinch (points/sec).
        [Min(1)] public float hornLockStaminaCost = 8f;
        [Header("Extra burst effort / all successful lifts already throw")]
        [Range(0, 1), Tooltip("One roll per eligible, connected pry; not a per-frame or per-match probability.")]
        // Evaluates probability roll once per valid lift satisfying all criteria; neither per-frame probability nor per-match incidence.
        // Setting to 0 only disables explosive burst probability; all lifts achieving genuine liftoff and completing work still throw.
        public float tossChance = .08f;
        // Minimum ratio of remaining stamina / initial endurance required for throw; must also afford fixed throw stamina cost.
        [Range(0, 1)] public float tossMinimumStamina = .35f;
        // Fixed stamina points deducted upon successful throw, distinct from per-second action burn rates.
        [Min(0)] public float tossStaminaCost = 12f;
        // Cooldown seconds granted to both fighters following a successful throw, preventing instant ping-pong tosses.
        [Min(0)] public float tossCooldownSeconds = 12f;
        [Header("Encouragement: intentionally a game mechanic")]
        // Minimum interval in seconds between allowed encouragement clicks.
        [Min(.1f)] public float cheerCooldown = 6f;
        // Duration of encouragement effort bonus in seconds, independent of button cooldown.
        [Min(.1f)] public float cheerDuration = 3.5f;
        // Multiplicative bonus of encouragement to offensive effort: 0.13 corresponds to x1.13.
        [Range(0, .3f)] public float cheerEffortBonus = .13f;
        // Stamina points instantly recovered upon accepted encouragement, capped at current fatigue-depressed stamina ceiling.
        [Range(0, 15)] public float cheerStamina = 5f;
        [Header("Match")]
        // Match opening countdown; normal AI combat and encouragement are disabled during countdown.
        [Min(0)] public float countdownSeconds = 3f;
        // Grace window after first ring-out waiting to see if opponent also falls, supporting near-simultaneous draw rulings.
        [Range(.5f, 3f)] public float doubleFallWindow = 1.2f;
        [Tooltip("0 chooses a fresh seed each session. A nonzero seed reproduces a pairing.")]
        // Seed 0 lets controller roll a new seed; non-zero reuses identical initial RNG starting point (not equivalent to full replay recording).
        public int initialSeed = 0;

        // Validates parameter bounds immediately upon Inspector mutation to prevent accidental invalid configurations.
        void OnValidate() => Validate();
        // Clamps parameters to ranges supported by current models and combat rules; provides code-level guards beyond Inspector Min/Range.
        // Callers also invoke this on match start; does not regenerate existing scene geometry.
        public void Validate()
        {
            forelegActionChance = Mathf.Clamp01(forelegActionChance);
            arenaRadius = Mathf.Max(3.5f, arenaRadius);
            spawnDistance = Mathf.Clamp(spawnDistance, 1.7f, arenaRadius - 1.2f);
            visualScale = Mathf.Clamp(visualScale, .4f, .9f);
            minimumSize = Mathf.Clamp(minimumSize, .85f, 1f);
            maximumSize = Mathf.Clamp(maximumSize, 1f, 1.15f);
            maximumSizeDifference = Mathf.Clamp(maximumSizeDifference, 0, .15f);
            referenceLengthMm = Mathf.Max(1, referenceLengthMm);
            referenceMassGrams = Mathf.Max(1, referenceMassGrams);
            walkSpeed = Mathf.Max(.1f, walkSpeed);
            turnSpeed = Mathf.Max(1, turnSpeed);
            shoveImpulse = Mathf.Max(.1f, shoveImpulse);
            pryImpulse = Mathf.Max(.1f, pryImpulse);
            staminaRecovery = Mathf.Max(.1f, staminaRecovery);
            fatiguePerSecond = Mathf.Clamp(fatiguePerSecond, 0, 2);
            openingSeconds = Mathf.Max(0, openingSeconds);
            escalationSeconds = Mathf.Max(10, escalationSeconds);
            openingDisplacement = Mathf.Clamp(openingDisplacement, .05f, 1f);
            finalDisplacement = Mathf.Clamp(finalDisplacement, 1f, 3f);
            probingSeconds = Mathf.Max(2, probingSeconds);
            clashSeconds = Mathf.Max(2, clashSeconds);
            exchangeRecoverySeconds = Mathf.Clamp(exchangeRecoverySeconds, .4f, 4f);
            recoveryResumeFraction = Mathf.Clamp(recoveryResumeFraction, .25f, .7f);
            recoveryMinimumSeconds = Mathf.Clamp(recoveryMinimumSeconds, .2f, .8f);
            recoveryMaximumSeconds = Mathf.Clamp(recoveryMaximumSeconds, recoveryMinimumSeconds + .4f, 4f);
            recoveryRetrySeconds = Mathf.Clamp(recoveryRetrySeconds, .3f, 2f);
            probeForceMultiplier = Mathf.Clamp(probeForceMultiplier, .1f, .6f);
            chargeForceMultiplier = Mathf.Clamp(chargeForceMultiplier, 1, 2);
            chargeWindupSeconds = Mathf.Max(.2f, chargeWindupSeconds);
            hornLockSeconds = Mathf.Max(.3f, hornLockSeconds);
            pryContactStart = Mathf.Clamp(pryContactStart,.05f,.6f);
            liftRaiseSeconds = Mathf.Clamp(liftRaiseSeconds, .3f, 1);
            liftHoldSeconds = Mathf.Clamp(liftHoldSeconds, .25f, 1.2f);
            liftedBodyPitch = Mathf.Clamp(liftedBodyPitch, 20, 55);
            liftFollowSpeed = Mathf.Clamp(liftFollowSpeed, .5f, 4);
            liftStruggleDelay = Mathf.Max(0, liftStruggleDelay);
            struggleStartHz = Mathf.Max(.1f, struggleStartHz);
            struggleMaxHz = Mathf.Max(struggleStartHz, struggleMaxHz);
            struggleRampSeconds = Mathf.Max(.1f, struggleRampSeconds);
            contactMotionSpeed = Mathf.Clamp(contactMotionSpeed, 1, 6);
            liftBlockedReleaseSeconds = Mathf.Clamp(liftBlockedReleaseSeconds, .1f, .4f);
            fallReachSeconds = Mathf.Clamp(fallReachSeconds, .08f, .5f);
            fallRecoverySeconds = Mathf.Clamp(fallRecoverySeconds, .4f, 3);
            fallSlideDamping = Mathf.Clamp(fallSlideDamping, 1, 12);
            throwHeavySpeedMultiplier = Mathf.Clamp(throwHeavySpeedMultiplier, 1, 3);
            throwMuscleForce = Mathf.Max(1, throwMuscleForce);
            throwInputArm = Mathf.Max(.05f, throwInputArm);
            throwEfficiency = Mathf.Clamp(throwEfficiency, .1f, 1);
            throwWorkLimit = Mathf.Max(1, throwWorkLimit);
            throwMinimumSpeed = Mathf.Max(.1f, throwMinimumSpeed);
            throwMaximumSpeed = Mathf.Max(throwMinimumSpeed, throwMaximumSpeed);
            // Preserves minimum tier segment widths; clamps thresholds on out-of-bounds parameters without mutating generated launch velocity.
            throwMaximumSpeed = Mathf.Max(throwMinimumSpeed + .4f, throwMaximumSpeed);
            throwQuickSpeed = Mathf.Clamp(throwQuickSpeed, throwMinimumSpeed + .1f, throwMaximumSpeed - .3f);
            throwArcSpeed = Mathf.Clamp(throwArcSpeed, throwQuickSpeed + .1f, throwMaximumSpeed - .2f);
            throwHighArcSpeed = Mathf.Clamp(throwHighArcSpeed, throwArcSpeed + .1f, throwMaximumSpeed - .1f);
            throwPowerOverheadSpeed = Mathf.Clamp(throwPowerOverheadSpeed, throwHighArcSpeed + .1f, throwMaximumSpeed);
            throwOverheadSweep = Mathf.Clamp(throwOverheadSweep, 50, 130);
            throwOverheadWork = Mathf.Max(1, throwOverheadWork);
            throwOverheadAngularSpeed = Mathf.Max(.1f, throwOverheadAngularSpeed);
            throwSnapSeconds = Mathf.Clamp(throwSnapSeconds, .1f, .5f);
            exertionRecoveryDelay = Mathf.Clamp(exertionRecoveryDelay, .1f, 1);
            contactRecoveryMultiplier = Mathf.Clamp(contactRecoveryMultiplier, .1f, 1);
            burstDrainPerSecond = Mathf.Clamp(burstDrainPerSecond, .1f, .8f);
            burstRecoveryPerSecond = Mathf.Clamp(burstRecoveryPerSecond, .1f, .8f);
            tacticalResponseSeconds = Mathf.Clamp(tacticalResponseSeconds, .15f, 1);
            missedAttackExposure = Mathf.Clamp(missedAttackExposure, .2f, 1);
            failedPryRetrySeconds = Mathf.Clamp(failedPryRetrySeconds, .5f, 8);
            chargeStaminaCost = Mathf.Max(1, chargeStaminaCost);
            hornLockStaminaCost = Mathf.Max(1, hornLockStaminaCost);
            tossChance = Mathf.Clamp01(tossChance);
            tossMinimumStamina = Mathf.Clamp01(tossMinimumStamina);
            tossStaminaCost = Mathf.Max(0, tossStaminaCost);
            tossCooldownSeconds = Mathf.Max(0, tossCooldownSeconds);
            fallDepth = Mathf.Max(.1f, fallDepth);
            groundHeight = Mathf.Min(-1.5f, groundHeight);
            moduleActionChance = Mathf.Clamp01(moduleActionChance);
            gravity = Mathf.Max(1, gravity);
            cheerCooldown = Mathf.Max(.1f, cheerCooldown);
            cheerDuration = Mathf.Max(.1f, cheerDuration);
            cheerEffortBonus = Mathf.Clamp(cheerEffortBonus, 0, .3f);
            cheerStamina = Mathf.Clamp(cheerStamina, 0, 15);
            countdownSeconds = Mathf.Max(0, countdownSeconds);
            doubleFallWindow = Mathf.Clamp(doubleFallWindow, .5f, 3f);
        }
    }
}
