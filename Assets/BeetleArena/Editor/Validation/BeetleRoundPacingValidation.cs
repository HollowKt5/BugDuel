using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BeetleArena;
using UnityEditor;
using UnityEngine;

public static class BeetleRoundPacingValidation
{
    const string Folder = "Artifacts/RoundPacing";
    public static void Pilot() => Batch(false);
    public static void Final() => Batch(true);
    public static void DiagnoseReach()
    {
        var report = new StringBuilder("NATURAL FORELEG REACH\n");
        using var fixture = new BeetleValidationScene("ForelegReachDiagnosis");
        var m = fixture.Match; m.StartMatch(7319);
        var recorded = new int[] { -1, -1 }; int count = 0;
        for (int tick = 0; tick < 4000 && count < 12; tick++)
        {
            m.AdvanceFixedStep();
            foreach (var b in m.Simulation.Fighters)
            {
                if (!(b.Motion.Active is ForelegPushMotion) || b.Motion.Phase < .67f || b.Motion.Phase > .72f || recorded[b.Index] == b.Motion.Starts) continue;
                recorded[b.Index] = b.Motion.Starts; count++;
                int slot = b.Motion.ForelegSide < 0 ? 0 : 3; var anim = m.actors[b.Index].animator;
                var own = m.actors[b.Index].GetComponent<BeetleContactRig>(); var other = m.actors[1-b.Index].GetComponent<BeetleContactRig>();
                own.CacheBounds(); other.CacheBounds(); float gap = 100; string pair = "";
                for (int i = 0; i < own.shapes.Length; i++) if (own.LegIndex(i) == slot)
                foreach (var shape in other.shapes)
                {
                    var p = shape.ClosestPoint(own.bounds[i].center); float d = Vector3.Distance(p, own.shapes[i].ClosestPoint(p));
                    if (d < gap) { gap = d; pair = own.shapes[i].name + "/" + shape.name; }
                }
                report.AppendLine($"t={m.Simulation.Elapsed:F2} side={b.Index}/{slot} phase={b.Motion.Phase:F2} centerDistance={b.Motion.Context.Distance:F2} aimDistance={b.Motion.Context.ForelegTargetDistance:F3} gap={gap:F3} IKerror={anim.importedRig.bindings[slot].targetError:F3} rearSupport={b.Motion.Context.RearSupport:F3} accepted={m.Contacts.AcceptedMotionFraction:F3} correction={b.Motion.Context.ForelegAimCorrection} pair={pair}");
                b.Motion.SampleLeg(slot, out var offset);
                var target = anim.MotionFootBase(slot, b) + anim.model.TransformVector(offset);
                report.AppendLine($"  desired={anim.model.InverseTransformPoint(target)} foot={anim.model.InverseTransformPoint(anim.legs[slot].lastFoot)} tip={anim.model.InverseTransformPoint(anim.importedRig.bindings[slot].tip.position)} gapToTarget={Vector3.Distance(target, anim.importedRig.bindings[slot].tip.position):F3}");
                File.WriteAllText(Folder + "/ReachDiagnosis.txt", report.ToString());
            }
        }
    }
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Preview() => Batch(false, true);
    static void Batch(bool final, bool preview = false)
    {
        string label = preview ? "Preview" : final ? "Final" : "Pilot";
        Directory.CreateDirectory(Folder);
        var report = new StringBuilder("ROUND DURATION / FORELEG VISIBILITY\nDuration includes countdown + conservative 3s finish presentation, excludes user pause.\n");
        var csv = new StringBuilder("seed,arena,fightSeconds,roundSeconds,result,winner,forelegStarts,hornStarts,forelegSeconds,attackSeconds,forelegHits,contactStrength,contactsPerMinute,longestContactGap,meanRetreatAfterContact\n");
        try
        {
            BeetleCombatUpgrade.Apply();
            using (var fixture = new BeetleValidationScene("RoundPacingValidation"))
            {
                var match = fixture.Match;
                fixture.Settings.throwInspectionMode = !final;
                report.AppendLine($"throwInspectionMode={fixture.Settings.throwInspectionMode}, heavySpeedGain={fixture.Settings.throwHeavySpeedMultiplier:F2}, finalDisplacement={fixture.Settings.finalDisplacement:F2}, escalationSeconds={fixture.Settings.escalationSeconds:F1}");
                Rules(fixture.Settings, report);
                match.StartMatch(71); match.TogglePause(); match.AdvanceRoundClock(300);
                Require(match.RoundClock == 0 && match.Simulation.Phase == MatchPhase.Countdown, "Pause consumes real round budget");
                match.TogglePause(); match.AdvanceRoundClock(181);
                Require(match.Simulation.Phase == MatchPhase.Finished && match.Simulation.Winner == -1 && !match.IsDefeatSlowMotion, "Wall-clock deadline failed after frame stall");
                report.AppendLine("PASS real-time deadline after stall; user pause excluded.");
                int[] seeds = preview ? new[] { 71 } : final ? new[] { 71, 7319, 1014, 2026, 419, 991 } : new[] { 71, 7319 };
                double seconds = 0; int matches = 0, totalForeleg = 0, totalHorn = 0, hits = 0, timeout = 0;
                float max = 0, min = 180; double forelegTime = 0, attackTime = 0;
                foreach (int seed in seeds)
                for (int arena = 0; arena < (preview ? 1 : 4); arena++)
                {
                    match.SelectArena(arena); match.StartMatch(seed);
                    var sim = match.Simulation;
                    int clipStart=-1, clipFrames=0;
                    if(preview){Directory.CreateDirectory("Artifacts/LiftCatch/Frames");match.hud.gameObject.SetActive(false);}
                    int front = 0, horn = 0, pushes = 0, strikes = 0, grapples = 0, pryStarts = 0, liftStarts = 0, flightRingouts = 0, strongThrows = 0, heavyThrows = 0; float frontSeconds = 0, attackingSeconds = 0;
                    int previousContacts = 0, retreatSamples = 0; float quiet = 0, longestQuiet = 0, retreat = 0;
                    var retreatTime = new float[2]; var retreatOrigin = new Vector2[2]; var retreatDirection = new Vector2[2];
                    var oldScores = new float[2];
                    var oldThrows = new int[2]; var oldFallen = new bool[2]; var oldAirborne = new bool[2];
                    var oldActions = new[] { BeetleAction.Assess, BeetleAction.Assess }; var starts = new int[2];
                    for (int tick = 0; tick < 9100 && sim.Phase != MatchPhase.Finished; tick++)
                    {
                        match.AdvanceFixedStep();
                        if(preview)
                        {
                            match.Present(1,sim.MotionTime,.02f);
                            if(clipStart<0 && sim.Fighters.Any(b=>b.Lift.Supported))clipStart=tick;
                            if(clipStart>=0 && (tick-clipStart)%3==0)
                                BeetleLiftCatchValidation.Shot(match,$"Artifacts/LiftCatch/Frames/Throw-{clipFrames++:D3}.png");
                        }
                        if (sim.Phase == MatchPhase.Fighting)
                        {
                            int contacts = sim.Fighters.Sum(b => b.Contacts) / 2;
                            quiet = contacts > previousContacts ? 0 : quiet + .02f;
                            longestQuiet = Mathf.Max(longestQuiet, quiet); previousContacts = contacts;
                            foreach (var b in sim.Fighters)
                            {
                                int target = 1 - b.Index;
                                if (b.AttackScore > oldScores[b.Index] && retreatTime[target] <= 0)
                                {
                                    retreatTime[target] = .4f; retreatOrigin[target] = sim.Fighters[target].Position;
                                    retreatDirection[target] = (retreatOrigin[target] - b.Position).normalized;
                                }
                                oldScores[b.Index] = b.AttackScore;
                                if (retreatTime[b.Index] <= 0) continue;
                                retreatTime[b.Index] -= .02f;
                                if (retreatTime[b.Index] <= 0)
                                { retreat += Mathf.Max(0, Vector2.Dot(b.Position - retreatOrigin[b.Index], retreatDirection[b.Index])); retreatSamples++; }
                            }
                        }
                        foreach (var b in sim.Fighters)
                        {
                            if (b.HasFallen && !oldFallen[b.Index] && oldAirborne[b.Index] && b.Lift.ThrowKind != BeetleThrowKind.None) flightRingouts++;
                            if (b.Throws > oldThrows[b.Index]) {
                                int tier = BeetleThrowMechanics.TierNumber(sim.Fighters[1-b.Index].Lift.ThrowKind);
                                if(preview){var lift=sim.Fighters[1-b.Index].Lift;report.AppendLine($"RECORDED LAUNCH kind={lift.ThrowKind} velocity={lift.LaunchVelocity} work={lift.Work:F2} burst={lift.BurstEffort}");}
                                if (tier >= 3) strongThrows++; if (tier >= 4) heavyThrows++;
                            }
                            oldThrows[b.Index] = b.Throws; oldFallen[b.Index] = b.HasFallen; oldAirborne[b.Index] = b.Airborne;
                            if (oldActions[b.Index] != b.Action && b.Action == BeetleAction.PryWindup) pryStarts++;
                            if (oldActions[b.Index] != b.Action && b.Action == BeetleAction.Lifting) liftStarts++;
                            bool isForeleg = b.Motion.Active && b.Motion.Active.Family == BeetleMotionFamily.Foreleg;
                            bool moduleStart = b.Motion.Starts != starts[b.Index];
                            if (moduleStart)
                            {
                                if (isForeleg) front++; else horn++;
                                if (b.Motion.Active is ForelegPushMotion) pushes++;
                                if (b.Motion.Active is ForelegStrikeMotion) strikes++;
                                if (b.Motion.Active is ForelegGrappleMotion) grapples++;
                            }
                            else if (oldActions[b.Index] != b.Action && (b.Action == BeetleAction.ShoveWindup || b.Action == BeetleAction.ChargeWindup || b.Action == BeetleAction.PryWindup)) horn++;
                            if (isForeleg) frontSeconds += .02f;
                            if (b.Committed) attackingSeconds += .02f;
                            oldActions[b.Index] = b.Action; starts[b.Index] = b.Motion.Starts;
                        }
                    }
                    Require(sim.Phase == MatchPhase.Finished, "Round did not terminate: " + seed + "/" + arena);
                    if(preview)
                    {
                        Require(clipFrames>0 && sim.Fighters.Sum(b=>b.Throws)>0 && sim.ResultReason==BeetleResultReason.RingOut,"Preview requires an actual AI throw and ring-out");
                        report.AppendLine($"Natural inspection clip: {clipFrames} frames at 0.06 seconds/frame; actual AI decisions, unchanged traits, no forced actions.");
                    }
                    float duration = sim.Elapsed + Mathf.Clamp(match.settings.countdownSeconds, 0, 10) + BeetleSimulation.FinishPresentationSeconds;
                    Require(duration <= 180.001f, "Exceeded hard round limit");
                    Require(sim.ResultReason != BeetleResultReason.TimeLimit || sim.Winner == -1, "Timeout produced a winner");
                    Require(sim.Winner < 0 || sim.Fighters[1-sim.Winner].HasFallen ||
                        sim.ResultReason == BeetleResultReason.UnableToRight &&
                        sim.Fighters[1-sim.Winner].OverturnedSeconds + .00001f >= BeetleSimulation.RightingTimeLimit,
                        "Winner without a ring-out or five-second righting failure");
                    int fh = sim.Fighters.Sum(b => b.ForelegHits);
                    matches++; seconds += duration; min = Mathf.Min(min, duration); max = Mathf.Max(max, duration);
                    totalForeleg += front; totalHorn += horn; hits += fh; forelegTime += frontSeconds; attackTime += attackingSeconds;
                    if (sim.ResultReason == BeetleResultReason.TimeLimit) timeout++;
                    float contactsPerMinute = sim.Fighters.Sum(b => b.Contacts) * .5f * 60 / Mathf.Max(.02f, sim.Elapsed);
                    float meanRetreat = retreat / Math.Max(1, retreatSamples);
                    report.AppendLine($"seed={seed} arena={arena} round={duration:F2}s result={sim.ResultReason} front={front} (push={pushes},strike={strikes},grapple={grapples}) horn={horn} frontTime={frontSeconds:F2}/{attackingSeconds:F2}s frontHits={fh} contacts/min={contactsPerMinute:F2} maxContactGap={longestQuiet:F2}s retreat0.4s={meanRetreat:F3} pryStarts={pryStarts} pries={sim.Fighters.Sum(x=>x.Pries)} lifts={liftStarts} throws={sim.Fighters.Sum(x=>x.Throws)} strongThrows={strongThrows} heavyThrows={heavyThrows} burstOpportunities={sim.Fighters.Sum(x=>x.TossOpportunities)} flightRingouts={flightRingouts} lastThrow={sim.Fighters[0].Lift.ThrowKind}/{sim.Fighters[1].Lift.ThrowKind}");
                    csv.AppendLine($"{seed},{arena},{sim.Elapsed:F2},{duration:F2},{sim.ResultReason},{sim.Winner},{front},{horn},{frontSeconds:F2},{attackingSeconds:F2},{fh},{sim.Fighters.Sum(b=>b.AttackScore):F2},{contactsPerMinute:F2},{longestQuiet:F2},{meanRetreat:F3}");
                    File.WriteAllText(Folder + "/" + label + ".txt", report.ToString());
                    File.WriteAllText(Folder + "/" + label + ".csv", csv.ToString());
                }
                double mean = seconds / matches, share = 100.0 * totalForeleg / Math.Max(1, totalForeleg + totalHorn);
                report.AppendLine($"SUMMARY matches={matches} mean={mean:F2}s min={min:F2}s max={max:F2}s timeouts={timeout} frontStarts={share:F2}% frontAttackTime={100*forelegTime/Math.Max(.01,attackTime):F2}% frontHits={hits}");
                if (final)
                {
                    Require(mean >= 80 && mean <= 100, "Mean outside 90 +/- 10 seconds tuning target");
                    Require(share >= 30 && hits >= matches, "Foreleg actions still suppressed or never contact");
                }
                // Existing HUD fields should fit actual result text, including new reasons.
                match.hud.Refresh(); Canvas.ForceUpdateCanvases();
                var result = match.hud.resultText;
                result.ForceMeshUpdate(true);
                report.AppendLine($"HUD result preferred={result.preferredWidth:F1}x{result.preferredHeight:F1} bounds={result.rectTransform.rect.size}");
                Require(result.preferredWidth <= result.rectTransform.rect.width && result.preferredHeight <= result.rectTransform.rect.height, "Result text does not fit");
                if (final)
                {
                    match.ToggleDefeatSlowMotion(); match.hud.Refresh();
                    typeof(BeetleCombatRefinementValidation).GetMethod("Shot", BindingFlags.NonPublic | BindingFlags.Static)
                        .Invoke(null, new object[] { match, "../RoundPacing/Result.png" });
                }
            }
            report.AppendLine(final ? "ALL ROUND PACING CHECKS PASSED" : "PILOT COMPLETE");
            File.WriteAllText(Folder + "/" + label + ".txt", report.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        catch (Exception e)
        { File.WriteAllText(Folder + "/" + label + ".txt", report.AppendLine(e.ToString()).ToString()); if (Application.isBatchMode) { Debug.LogException(e); EditorApplication.Exit(1); } else throw; }
    }
    static void Rules(BeetleMatchSettings source, StringBuilder report)
    {
        var settings = UnityEngine.Object.Instantiate(source);
        try
        {
            settings.countdownSeconds = 0; settings.motionModules = Array.Empty<BeetleMotionModule>();
            BeetleSimulation NewSim()
            { var s = new BeetleSimulation(settings, 71); s.Step(.02f); return s; }
            void Deadline(BeetleSimulation s) => typeof(BeetleSimulation).GetProperty("Elapsed").SetValue(s, s.FightTimeLimit - .01f);
            var tie = NewSim(); Deadline(tie); tie.Step(.02f);
            Require(tie.Phase == MatchPhase.Finished && tie.Winner == -1 && tie.ResultReason == BeetleResultReason.TimeLimit, "Timeout tie failed");
            var points = NewSim(); points.Fighters[1].AttackScore = 2; Deadline(points); points.Step(.02f);
            Require(points.Winner == -1 && points.ResultReason == BeetleResultReason.TimeLimit, "Timeout must draw regardless of contact statistics");
            float elapsed = points.Elapsed, score = points.Fighters[1].AttackScore;
            for (int i = 0; i < 10; i++) points.Step(.02f);
            Require(points.Elapsed == elapsed && points.Fighters[1].AttackScore == score, "Finished match still scores/time advances");
            var airborne = points.Fighters[0]; airborne.Airborne = true; airborne.Height = 1; airborne.VerticalVelocity = 0;
            airborne.Lift.Pitch = -30; airborne.ImpactVisual = 1;
            for (int i = 0; i < 100; i++) points.Step(.02f);
            Require(!airborne.Airborne && Mathf.Abs(airborne.Lift.Pitch) < .001f && airborne.ImpactVisual == 0 && airborne.Action == BeetleAction.Subdued,
                "Deadline leaves the loser frozen in a lift/impact pose");
            var exhausted = NewSim(); foreach (var b in exhausted.Fighters) { b.Stamina = 0; b.BurstReserve = 0; b.AttackScore = 100; }
            exhausted.Step(.02f);
            Require(exhausted.Phase == MatchPhase.Fighting, "Exhaustion or contact statistics must not end a round");
            var missed = NewSim(); var mover = missed.Fighters[0];
            mover.FailedAttacks = 2; mover.ActionDuration = 0; mover.TouchingOpponent = false;
            missed.Step(.02f);
            Require(mover.Action == BeetleAction.Approach && mover.DecisionTime > .3f && mover.FailedAttacks == 0, "Repeated misses do not commit to closing distance");
            var simultaneous = NewSim(); foreach (var b in simultaneous.Fighters) b.HasFallen = true; simultaneous.Step(.02f);
            Require(simultaneous.Winner == -1 && simultaneous.ResultReason == BeetleResultReason.RingOut, "Simultaneous ring-out order bias");
            var ring = NewSim(); ring.Fighters[0].HasFallen = true; Deadline(ring); ring.Step(.02f);
            Require(ring.Winner == 1 && ring.ResultReason == BeetleResultReason.RingOut, "Deadline extends fall window or overrides ring-out");
            report.AppendLine("PASS hard deadline, timeout always draws, exhaustion never ends a round, simultaneous ring-out, ring-out priority, post-finish settling/frozen statistics.");
        }
        finally { UnityEngine.Object.DestroyImmediate(settings); }
    }
}
