using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BeetleArena;
using UnityEditor;
using UnityEngine;

public static class BeetleForelegValidation
{
    public static void Pilot() => Batch(false);
    public static void Final() => Batch(true);
    public static void ContactsOnly() => Batch(false, true);
    static void Require(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    static void Batch(bool full, bool requireHits = false)
    {
        var report = new StringBuilder("FORELEG PUSH / STRIKE VALIDATION\n" + DateTimeOffset.Now.ToString("o") + "\n");
        Directory.CreateDirectory("Artifacts/ForelegReview"); Directory.CreateDirectory("Artifacts/ModularActions");
        try
        {
            BeetleCombatUpgrade.Apply();
            using (var fixture = new BeetleValidationScene("ForelegValidation"))
            {
                var match = fixture.Match; var all = fixture.Settings.motionModules;
                SupportRules(match, report);
                var modules = all.Where(m => m is ForelegPushMotion || m is ForelegStrikeMotion).ToArray();
                Require(modules.Length == 2, "New actions not installed");
                foreach (var module in modules)
                {
                    match.settings.motionModules = all; match.StartMatch(71);
                    while (match.Simulation.Phase == MatchPhase.Countdown) match.AdvanceFixedStep();
                    var sim = match.Simulation; var a = sim.Fighters[0];
                    Require(!match.Contacts.HasMotionContact(0, BeetleMotionContact.Foreleg, 0) &&
                        !match.Contacts.HasMotionContact(0, BeetleMotionContact.Foreleg, 3), "Separated fighters produce a foreleg hit");
                    a.Motion.Refresh(sim, a); a.Motion.EditorPreviewStart(module);
                    float side = a.Motion.Context.ForelegSide;
                    a.CircleDirection *= -1; sim.Fighters[1].Position = a.Position + a.Forward * 3 + new Vector2(a.Forward.y, -a.Forward.x) * -side;
                    a.Motion.Refresh(sim, a);
                    Require(a.Motion.Context.ForelegSide == side, "Attack switches limbs mid-stroke");
                    var c = a.Motion.Context;
                    for (int tick = 0; tick <= 100; tick++)
                    {
                        float p = tick / 100f; int owned = 0, selected = -1;
                        for (int slot = 0; slot < 6; slot++) if (module.SampleLeg(c, p, slot, out var offset))
                        { owned++; selected = slot; Require(slot == 0 || slot == 3, "Foreleg action owns another leg"); Require(offset.magnitude < 1.6f, "Excessive limb target"); }
                        var sample = module.Sample(c, p);
                        bool groundedPush = module is ForelegPushMotion && (!c.ForebodyRaised || c.RearSupport <= .4f || p < .26f);
                        if (groundedPush) Require(owned == 0 && sample.Contact == BeetleMotionContact.None, "Push lifted a foot before accepted rear-up");
                        else Require(owned == 1 && sample.ContactLegPlusOne - 1 == selected, "Visual/contact limb mismatch");
                        Require(p > 0 && p < 1 || sample.Contact == BeetleMotionContact.None, "Contact at rest");
                    }
                    a.Motion.MarkHit(); a.Motion.MarkHit(); Require(a.Motion.Hits == 1, "Repeated contact reward");
                    Require(!match.Contacts.HasMotionContact(0, BeetleMotionContact.Foreleg, 2), "Hind leg counted as foreleg");
                    report.AppendLine(module.name + ": PASS fixed limb, single front leg ownership, bounded reach, contact window, one-hit lock.");
                    int hits = 0;
                    for (int variant = 0; variant < 6; variant++) hits += ContactTrial(match, module, variant, report);
                    report.AppendLine(module.name + " real hits=" + hits);
                    if (full || requireHits) Require(hits > 0, module.name + " never reaches real contact");
                }
                match.settings.motionModules = all;
                int drives = 0;
                var drive = all.OfType<DrivingPushMotion>().Single();
                for (int variant = 0; variant < 6; variant++) drives += ContactTrial(match, drive, variant, report);
                if (full || requireHits) Require(drives > 0, "Raised-body drive never contacts");
            }
            if (full) { BeetleMotionValidation.Run(); BeetleCombatRefinementValidation.Run(); BeetleContactEscapeValidation.Run(); }
            report.AppendLine(full ? "ALL FORELEG CHECKS PASSED" : requireHits ? "FORELEG CONTACT / SUPPORT CHECKS PASSED" : "PILOT COMPLETE; inspect hit counts before acceptance.");
            File.WriteAllText("Artifacts/ForelegReview/Validation.txt", report.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        catch (Exception e)
        { report.AppendLine(e.ToString()); File.WriteAllText("Artifacts/ForelegReview/Validation.txt", report.ToString()); if (Application.isBatchMode) { Debug.LogException(e); EditorApplication.Exit(1); } else throw; }
    }
    sealed class SupportProbe : IBeetleContactSolver, IBeetleMotionContacts, IBeetleDriveSupport, IBeetleForebodySupport
    {
        public float Grip;
        public bool Raised;
        public bool ForebodyRaised(int attacker) => Raised;
        public void Solve(BeetleSimulation sim, float dt) { }
        public bool HasMotionContact(int attacker, BeetleMotionContact kind, int slot) => true;
        public float RearSupport(int attacker, BeetleSimulation sim) => Grip;
    }
    static void SupportRules(BeetleMatchController match, StringBuilder report)
    {
        match.SelectArena(0); match.StartMatch(71);
        while (match.Simulation.Phase == MatchPhase.Countdown) match.AdvanceFixedStep();
        var sim = match.Simulation; var a = sim.Fighters[0];
        var actor = match.actors[0]; Vector3 old = actor.transform.position;
        float grounded = match.Contacts.RearSupport(0, sim);
        actor.transform.position += Vector3.up;
        Require(match.Contacts.RearSupport(0, sim) == 0, "Floating rear feet count as support");
        actor.transform.position = old;
        Require(grounded > .4f, "Grounded fixture has no rear support");
        var modules = match.settings.motionModules.Where(m => m is ForelegPushMotion || m is DrivingPushMotion).ToArray();
        foreach (var module in modules)
        {
            sim.Fighters[1].Position = a.Position + a.Forward * 3;
            var probe = new SupportProbe(); sim.ContactSolver = probe;
            a.Action = BeetleAction.Module; a.Motion.Refresh(sim, a); a.Motion.EditorPreviewStart(module);
            a.Motion.Tick(a.Motion.Duration * .5f);
            var sample = a.Motion.CombatSample;
            Require(sample.RequiresRearSupport && sample.PlantRearFeet && Mathf.Abs(sample.BodyPitch) < .001f, "Unsupported push rears up");
            int hits = a.Motion.Hits; Vector2 before = sim.Fighters[1].Velocity;
            typeof(BeetleSimulation).GetMethod("ApplyModuleContacts", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(sim, null);
            Require(a.Motion.Hits == hits && sim.Fighters[1].Velocity == before, "Unsupported push creates impulse");
            probe.Grip = 1; a.Motion.Refresh(sim, a);
            if (module is ForelegPushMotion)
            {
                Require(!a.Motion.SampleLeg(0, out _) && !a.Motion.SampleLeg(3, out _) && a.Motion.CombatSample.Contact == BeetleMotionContact.None,
                    "A blocked chest rise still lifts the pushing foot");
            }
            probe.Raised = true; a.Motion.Refresh(sim, a);
            var pose = a.Motion.CombatSample;
            if (module is ForelegPushMotion)
                Require(pose.BodyPitch < -10 && pose.BodyPitch >= -15 && pose.HeadPitch < -8 && pose.HeadPitch >= -22,
                    "Foreleg push must rear on planted hind feet before extending");
            else Require(pose.BodyPitch < -10 && pose.HeadPitch < -20, "Strong supported drive lost its half-rise");
            typeof(BeetleSimulation).GetMethod("ApplyModuleContacts", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(sim, null);
            Require(a.Motion.Hits == hits + 1 && sim.Fighters[1].Velocity != before, "Supported contact has no drive");
            a.Motion.Cancel();
        }
        sim.ContactSolver = match.Contacts;
        report.AppendLine($"PASS: rear support ground={grounded:F3}, airborne feet=0; absent rear grip suppresses rearing/impulse, restored grip enables drive.");
    }
    static int ContactTrial(BeetleMatchController match, BeetleMotionModule module, int variant, StringBuilder report)
    {
        match.settings.motionModules = new[] { module }; match.SelectArena(variant == 3 ? 1 : 0); match.StartMatch(71);
        var sim = match.Simulation;
        while (sim.Phase == MatchPhase.Countdown) match.AdvanceFixedStep();
        var a = sim.Fighters[0]; var b = sim.Fighters[1];
        float gap = variant == 0 ? 3.65f : 3.1f;
        a.Position = new Vector2(-gap * .5f, 0); b.Position = new Vector2(gap * .5f, 0);
        a.Heading = 90; b.Heading = variant == 1 ? 30 : variant == 2 ? -30 : -90;
        if (variant == 5) a.CircleDirection *= -1;
        if (module is DrivingPushMotion) a.CombatPhase = ExchangePhase.Clash;
        a.Action = b.Action = BeetleAction.Brace; a.ActionTime = b.ActionTime = 0; a.ActionDuration = b.ActionDuration = 20;
        sim.SetGround(match.ActiveSurface);
        for (int i = 0; i < 2; i++) match.actors[i].Bind(sim.Fighters[i], match.settings, match.ActiveSurface);
        match.Contacts.Solve(sim, 0); a.Motion.Refresh(sim, a);
        if (!a.Motion.TryStart(.2f)) { report.AppendLine(module.name + " variant=" + variant + " ineligible"); return 0; }
        a.Action = BeetleAction.Module; a.ActionDuration = a.Motion.Duration; a.ActionTime = 0;
        float overlap = 0, excess = 0, minGap = 100, raisedBody = 0, anchorSlip = 0; string nearest = "";
        var ra = match.actors[0].GetComponent<BeetleContactRig>(); var rb = match.actors[1].GetComponent<BeetleContactRig>();
        for (int tick = 0; tick < 80; tick++)
        {
            if (!a.Motion.HasActive) { a.Action = BeetleAction.Brace; a.ActionDuration = 20; a.ActionTime = 0; }
            var old = new Vector3(a.Position.x, a.Height, a.Position.y);
            var anim = match.actors[0].animator;
            bool anchored = anim.legs[2].supportAnchored && anim.legs[5].supportAnchored;
            Vector3 leftPlant = anim.legs[2].planted, rightPlant = anim.legs[5].planted;
            match.AdvanceFixedStep();
            if (anchored && anim.legs[2].supportAnchored && anim.legs[5].supportAnchored) anchorSlip = Mathf.Max(anchorSlip,
                Vector3.Distance(leftPlant, anim.legs[2].planted), Vector3.Distance(rightPlant, anim.legs[5].planted));
            if (a.Motion.HasActive) raisedBody = Mathf.Min(raisedBody, Mathf.DeltaAngle(0, anim.bodyPivot.localEulerAngles.x));
            overlap = Mathf.Max(overlap, match.Contacts.MeasurePenetration());
            excess = Mathf.Max(excess, Vector3.Distance(old, new Vector3(a.Position.x, a.Height, a.Position.y)) - match.Contacts.MotionBudget(0));
            if (a.Motion.HasActive && a.Motion.CombatSample.Contact == BeetleMotionContact.Foreleg)
            {
                ra.CacheBounds(); rb.CacheBounds();
                int slot = a.Motion.CombatSample.ContactLegPlusOne - 1;
                for (int i = 0; i < ra.shapes.Length; i++) if (ra.LegIndex(i) == slot)
                    for (int j = 0; j < rb.shapes.Length; j++)
                    {
                        Vector3 p = rb.shapes[j].ClosestPoint(ra.bounds[i].center);
                        float d = Vector3.Distance(p, ra.shapes[i].ClosestPoint(p));
                        if (d < minGap) { minGap = d; nearest = ra.shapes[i].name + " / " + rb.shapes[j].name + " phase=" + a.Motion.Phase.ToString("F2") + " gapVector=" + (p - ra.shapes[i].ClosestPoint(p)).ToString("F3"); }
                    }
            }
            int hits = a.Motion.Hits; match.Present(.75f, sim.MotionTime, .02f);
            Require(hits == a.Motion.Hits, "Display creates a hit");
            if (variant == 4 && (tick == 4 || tick == 10 || tick == 15 || tick == 23 || tick == 34))
            {
                var cam = match.spectatorCamera.GetComponent<Camera>();
                cam.orthographicSize = 3.7f;
                Vector3 focus = (match.actors[0].transform.position + match.actors[1].transform.position) * .5f;
                cam.transform.position = focus + new Vector3(0, 5, -8); cam.transform.LookAt(focus);
                match.hud.gameObject.SetActive(false);
                typeof(BeetleCombatRefinementValidation).GetMethod("Shot", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, new object[] { match, "../ForelegReview/" + module.name + "-" + tick + ".png" });
            }
        }
        report.AppendLine($"{module.name} variant={variant}: hits={a.Motion.Hits}, overlap={overlap:F6}, excess={excess:F6}, raisedBody={raisedBody:F2}, rearPlantSlip={anchorSlip:F6}, nearest={minGap:F3} {nearest}");
        File.WriteAllText("Artifacts/ForelegReview/Progress.txt", report.ToString());
        Require(a.Motion.Hits <= 1 && !a.Motion.HasActive && overlap < .006f && excess < .0003f && anchorSlip < .001f, "Contact/continuity/anchored rear foot regression");
        return a.Motion.Hits;
    }
}
