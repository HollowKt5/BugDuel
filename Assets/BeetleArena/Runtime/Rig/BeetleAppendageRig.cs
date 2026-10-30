using System;
using System.Collections.Generic;
using UnityEngine;

namespace BeetleArena
{
    public enum BeetlePoseChannel { ShellOpen, WingDeploy, WingFlap, JawOpen, JawSweep, AntennaPitch, AntennaYaw, Custom }

    [Serializable]
    public struct BeetleAppendagePose
    {
        [Range(0, 1)] public float extension, flapStrength, jawOpen;
        public float flapPhase;
        [Range(-1, 1)] public float jawSweep, antennaPitch, antennaYaw, custom;

        public float Sample(BeetlePoseChannel channel, float phaseOffset = 0)
        {
            float e = Unit(extension);
            switch (channel)
            {
                case BeetlePoseChannel.ShellOpen: return Mathf.SmoothStep(0, 1, e / .4f);
                case BeetlePoseChannel.WingDeploy: return Mathf.SmoothStep(0, 1, (e - .4f) / .6f);
                case BeetlePoseChannel.WingFlap: return Mathf.Sin((Finite(flapPhase) + Finite(phaseOffset)) * Mathf.PI * 2) *
                    Unit(flapStrength) * Mathf.SmoothStep(0, 1, (e - .95f) / .05f);
                case BeetlePoseChannel.JawOpen: return Unit(jawOpen);
                case BeetlePoseChannel.JawSweep: return Signed(jawSweep);
                case BeetlePoseChannel.AntennaPitch: return Signed(antennaPitch);
                case BeetlePoseChannel.AntennaYaw: return Signed(antennaYaw);
                default: return Signed(custom);
            }
        }

        internal static float Finite(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0 : value;
        internal static float Unit(float value) => Mathf.Clamp01(Finite(value));
        internal static float Signed(float value) => Mathf.Clamp(Finite(value), -1, 1);
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("Beetle Arena/Model/Beetle Appendage Rig")]
    public sealed class BeetleAppendageRig : MonoBehaviour
    {
        [Serializable]
        public sealed class Rotation
        {
            public BeetlePoseChannel channel;
            public Vector3 localAxis = Vector3.right;
            public float degrees = 45;
            [Tooltip("WingFlap phase offset in cycles. Use signed degrees to mirror a joint.")]
            public float phaseOffset;
        }

        [Serializable]
        public sealed class Joint
        {
            public string label;
            public Transform pivot;
            [HideInInspector] public Transform capturedPivot;
            [HideInInspector] public Quaternion rest = Quaternion.identity;
            public Rotation[] rotations = Array.Empty<Rotation>();

            public Quaternion Evaluate(BeetleAppendagePose pose)
            {
                Quaternion result = rest;
                if (rotations != null) foreach (var rotation in rotations)
                    if (rotation != null && rotation.localAxis.sqrMagnitude > .000001f)
                        result *= Quaternion.AngleAxis(pose.Sample(rotation.channel, rotation.phaseOffset) *
                            BeetleAppendagePose.Finite(rotation.degrees), rotation.localAxis.normalized);
                return result;
            }
        }

        public Joint[] joints = Array.Empty<Joint>();
        [Min(.01f)] public float extensionPerSecond = 1.8f;
        BeetleAppendagePose pose;
        float targetExtension, targetFlap, frequency;
        public BeetleAppendagePose Pose => pose;

        // Called by the presentation owner, never by Update/LateUpdate. Render interpolation owns the same transforms between steps.
        public void SetFlight(float extension, float flapStrength = 0, float frequencyHz = 12)
        {
            targetExtension = BeetleAppendagePose.Unit(extension);
            targetFlap = BeetleAppendagePose.Unit(flapStrength);
            frequency = Mathf.Clamp(BeetleAppendagePose.Finite(frequencyHz), 0, 60);
        }

        public void SetMouth(float opening, float sweep = 0)
        { pose.jawOpen = BeetleAppendagePose.Unit(opening); pose.jawSweep = BeetleAppendagePose.Signed(sweep); }

        public void SetSensing(float pitch, float yaw)
        { pose.antennaPitch = BeetleAppendagePose.Signed(pitch); pose.antennaYaw = BeetleAppendagePose.Signed(yaw); }

        public void SetCustom(float value) => pose.custom = BeetleAppendagePose.Signed(value);

        public void Advance(float dt)
        {
            dt = Mathf.Max(0, BeetleAppendagePose.Finite(dt));
            pose.extension = Mathf.MoveTowards(pose.extension, targetExtension, dt * Mathf.Max(.01f, extensionPerSecond));
            pose.flapStrength = targetFlap;
            pose.flapPhase = Mathf.Repeat(pose.flapPhase + dt * frequency, 1);
            Apply(pose);
        }

        public void Apply(BeetleAppendagePose value)
        {
            if (joints == null) return;
            foreach (var joint in joints)
                if (joint != null && joint.pivot && joint.capturedPivot == joint.pivot)
                    joint.pivot.localRotation = joint.Evaluate(value);
        }

        public void ResetPose()
        {
            pose = default;
            targetExtension = targetFlap = frequency = 0;
            Apply(pose);
        }

        public bool Validate(out string error, bool requireRest = true)
        {
            var used = new HashSet<Transform>();
            if (joints != null) for (int i = 0; i < joints.Length; i++)
            {
                var joint = joints[i];
                if (joint == null || !joint.pivot) { error = $"Joint {i}: assign a pivot or remove the unused entry."; return false; }
                if (joint.pivot == transform || !joint.pivot.IsChildOf(transform) || !used.Add(joint.pivot))
                { error = $"Joint {i}: pivot must be a unique descendant of this rig."; return false; }
                if (requireRest && joint.capturedPivot != joint.pivot)
                { error = $"Joint {i}: capture the closed rest pose after changing the pivot."; return false; }
                if (joint.rotations != null) foreach (var rotation in joint.rotations)
                    if (rotation == null || !ValidAxis(rotation.localAxis) ||
                        !float.IsFinite(rotation.degrees) || !float.IsFinite(rotation.phaseOffset))
                    { error = $"Joint {i}: each rotation needs a finite angle and nonzero local axis."; return false; }
            }
            var animator = GetComponent<BeetleProceduralAnimator>();
            if (animator && animator.model)
            {
                foreach (var pivot in used)
                {
                    if (!animator.model || !pivot.IsChildOf(animator.model) || pivot == animator.model ||
                        pivot == animator.bodyPivot || pivot == animator.headPivot)
                    { error = "Appendages must be below the model and cannot own the body/head control pivots."; return false; }
                    if (animator.legs != null) foreach (var leg in animator.legs)
                        if (leg != null && ((leg.hip && leg.hip.IsChildOf(pivot)) || (leg.knee && leg.knee.IsChildOf(pivot)) ||
                            (leg.ankle && leg.ankle.IsChildOf(pivot))))
                        { error = "An appendage and leg IK cannot drive the same joint."; return false; }
                    if (animator.importedRig && animator.importedRig.bindings != null)
                        foreach (var leg in animator.importedRig.bindings)
                            if (leg != null && leg.tip && leg.tip.IsChildOf(pivot))
                            { error = "An appendage cannot alter the IK contact chain."; return false; }
                    if (animator.antennae != null && Array.IndexOf(animator.antennae, pivot) >= 0)
                    { error = "An appendage and legacy antenna animation cannot drive the same joint."; return false; }
                    var jaw = GetComponentInChildren<BeetleJawRig>(true);
                    if (jaw && (pivot == jaw.left || pivot == jaw.right))
                    { error = "An appendage and legacy jaw animation cannot drive the same joint."; return false; }
                }
            }
            error = null; return true;
        }

        static bool ValidAxis(Vector3 axis) => float.IsFinite(axis.x) && float.IsFinite(axis.y) && float.IsFinite(axis.z) && axis.sqrMagnitude > .000001f;

        public void CaptureRestPose()
        {
            if (!Validate(out var error, false)) throw new InvalidOperationException(error);
            if (joints != null) foreach (var joint in joints)
            { joint.rest = joint.pivot.localRotation; joint.capturedPivot = joint.pivot; }
        }
    }
}
