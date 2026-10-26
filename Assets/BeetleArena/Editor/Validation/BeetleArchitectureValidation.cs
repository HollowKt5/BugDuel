using System;
using System.IO;
using System.Linq;
using System.Text;
using BeetleArena;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Architectural boundary regression: shared dispatch, pausing, presentation lifecycle, metadata invalidation, and validation scene isolation.</summary>
public static class BeetleArchitectureValidation
{
    static void Require(bool value, string why) { if (!value) throw new Exception(why); }

    [MenuItem("Tools/甲壳虫斗/验证/架构与性能/架构契约", false, 740)]
    public static void Run()
    {
        var previous = SceneManager.GetActiveScene();
        bool dirty = previous.isDirty;
        int sceneCount = SceneManager.sceneCount;
        var report = new StringBuilder("ARCHITECTURE CONTRACTS\n" + DateTimeOffset.Now.ToString("o") + "\n");
        Directory.CreateDirectory("Artifacts/ArchitectureReview");
        try
        {
            using (var fixture = new BeetleValidationScene("ArchitectureValidation"))
            {
                var m = fixture.Match;
                m.audioFeedback.volume = 0;
                Require(!AssetDatabase.Contains(m.settings), "Validation edits a persistent settings asset");
                m.SelectArena(0); m.StartMatch(71);
                // Traverses production entry point, covering countdown and physical horn contacts; legacy fast-forward skipped presentation updates.
                for (int i = 0; i < 1500; i++)
                {
                    m.AdvanceFixedStep();
                    m.Present(1, m.Simulation.MotionTime, .02f);
                }
                Require(m.contactFeedback.ObservedContacts > 0 && m.audioFeedback.PlayedContacts > 0,
                    "Common fixed-step entry omitted contact feedback");
                // Active combat can terminate naturally within 30s; pause contracts must not depend on a random match happening to stay unfinished.
                if (m.Simulation.Phase == MatchPhase.Finished) m.StartMatch(71);
                m.TogglePause(); Require(m.IsPaused, "Could not pause fixture");
                float time = m.Simulation.MotionTime;
                var transforms = m.actors.SelectMany(a => a.GetComponentsInChildren<Transform>(true)).ToArray();
                var positions = transforms.Select(t => t.position).ToArray();
                var rotations = transforms.Select(t => t.rotation).ToArray();
                int observed = m.contactFeedback.ObservedContacts;
                for (int i = 0; i < 10; i++) { m.AdvanceFixedStep(); m.Present(.37f, time + 1, .02f); }
                Require(m.Simulation.MotionTime == time && observed == m.contactFeedback.ObservedContacts, "Pause advanced state/feedback");
                for (int i = 0; i < transforms.Length; i++)
                    Require(transforms[i].position == positions[i] && transforms[i].rotation == rotations[i], "Pause changed visible pose");
                m.SwitchArena(); Require(!m.IsPaused && m.ArenaIndex == 1, "Switch arena failed to reset pause");
                time = m.Simulation.MotionTime; m.AdvanceFixedStep();
                Require(m.Simulation.MotionTime > time, "New arena remained paused");
                report.AppendLine("PASS: shared fixed-step/presentation entry, real feedback, paused state and full pose, switch while paused.");

                // Edit Mode validates explicit idempotent disposal; Play Mode verifies real lifecycle via OnDisable/OnDestroy.
                var go = new GameObject("Architecture lifecycle fixture");
                try
                {
                    var sound = go.AddComponent<ArenaAudio>(); sound.volume = 0; sound.Initialize();
                    var voices = go.GetComponentsInChildren<AudioSource>();
                    Require(voices.Length == 4, "Audio pool not initialized");
                    sound.enabled = false; int sounds = sound.PlayedContacts;
                    sound.PlayContact(Vector3.zero, 1, false);
                    Require(sound.PlayedContacts == sounds, "Disabled audio accepted explicit call");
                    sound.enabled = true; sound.PlayContact(Vector3.zero, 1, false);
                    var playedClip = voices.Select(v => v.clip).First(c => c);
                    sound.ReleaseResources(); sound.ReleaseResources();
                    UnityEngine.Object.DestroyImmediate(sound);
                    Require(!playedClip && voices.All(v => !v), "Removing audio component leaked clip/voice objects");
                    var sparks = go.AddComponent<HornClashSparks>();
                    sparks.sparkMaterial = m.contactFeedback.sparks.sparkMaterial;
                    sparks.SetEnabled(true, false); sparks.Burst(Vector3.zero, Vector3.right, 1);
                    Require(sparks.LiveParticles > 0, "Particle fixture did not emit");
                    sparks.enabled = false; sparks.Clear(); int bursts = sparks.BurstCount;
                    sparks.Burst(Vector3.zero, Vector3.right, 1);
                    Require(sparks.LiveParticles == 0 && sparks.BurstCount == bursts, "Disabled sparks remain active");
                    var particles = sparks.Particles;
                    sparks.ReleaseResources(); sparks.ReleaseResources();
                    UnityEngine.Object.DestroyImmediate(sparks);
                    Require(!particles && go.transform.childCount == 0, "Removing spark component leaked child object");
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
                report.AppendLine("PASS: disabled explicit calls, particle clear, explicit idempotent audio/clip/particle cleanup (Edit Mode).");

                var rig = m.actors[0].GetComponent<BeetleContactRig>();
                var shapes = rig.shapes;
                try
                {
                    rig.CacheBounds();
                    // Array replacement of identical length must reclassify; Length alone cannot validate cache freshness.
                    rig.shapes = shapes.Reverse().ToArray(); rig.CacheBounds();
                    for (int i = 0; i < rig.shapes.Length; i++)
                    {
                        string n = rig.shapes[i].name;
                        bool horn = n.StartsWith("Cephalic horn", StringComparison.Ordinal) || n.StartsWith("Horn fork", StringComparison.Ordinal)
                            || n.StartsWith("Horn tip", StringComparison.Ordinal) || n == "Thoracic horn";
                        Require(rig.IsHorn(i) == horn, "Same-length array replacement kept stale metadata");
                    }
                    var first = rig.shapes[0]; string oldName = first.name;
                    try
                    {
                        first.name = "Thoracic horn"; rig.InvalidateShapeMetadata(); rig.CacheBounds();
                        Require(rig.IsHorn(0) && rig.Role(0) == BeetleContactRole.Body && rig.LegIndex(0) == -1,
                            "Explicit metadata invalidation failed");
                    }
                    finally { first.name = oldName; }
                }
                finally { rig.shapes = shapes; rig.InvalidateShapeMetadata(); rig.CacheBounds(); }
                var noArena = new BeetleContactSolver(m.actors, null, .006f);
                Require(float.IsFinite(noArena.MeasurePenetration()), "Null environment query failed");
                report.AppendLine("PASS: metadata invalidation, same-length replacement, optional environment query.");
            }
            // Immediate normal exit serves as baseline: distinguishes clean teardown of freshly opened scenes from exception unwinding.
            string immediatePath;
            using (var immediate = new BeetleValidationScene("ArchitectureImmediateCleanup"))
                immediatePath = immediate.Scene.path;
            Require(!AssetDatabase.LoadAssetAtPath<SceneAsset>(immediatePath) && !File.Exists(immediatePath)
                && !File.Exists(immediatePath + ".meta"), "Immediate normal cleanup left a temporary asset: " + immediatePath);
            // using statement must restore scene even when test body throws; throws bespoke exception deliberately, without masking setup/teardown errors.
            string temporary = null;
            bool probeCaught = false;
            try
            {
                using (var fault = new BeetleValidationScene("ArchitectureFaultCleanup"))
                {
                    temporary = fault.Scene.path;
                    throw new ValidationProbeException(temporary);
                }
            }
            catch (ValidationProbeException probe)
            {
                probeCaught = true;
                Require(probe.Path == temporary, "Unexpected fault-cleanup probe path");
            }
            Require(probeCaught, "Fault cleanup did not execute the intentional exception");
            Require(!AssetDatabase.LoadAssetAtPath<SceneAsset>(temporary) && !File.Exists(temporary) && !File.Exists(temporary + ".meta"),
                "Failed validation left a temporary scene asset: " + temporary);
            Require(SceneManager.sceneCount == sceneCount && SceneManager.GetActiveScene() == previous
                && previous.isLoaded && previous.isDirty == dirty, "Validation changed user's scene state");
            report.AppendLine("PASS: settings isolation, exception cleanup, original scene/dirty state preserved.");
            report.AppendLine("ALL ARCHITECTURE CHECKS PASSED");
        }
        finally { File.WriteAllText("Artifacts/ArchitectureReview/Contracts.txt", report.ToString()); }
    }
    sealed class ValidationProbeException : Exception
    {
        public readonly string Path;
        public ValidationProbeException(string path) { Path = path; }
    }
}
