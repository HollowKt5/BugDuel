using System;
using System.Collections.Generic;
using UnityEngine;

namespace BeetleArena
{
    public enum BeetleLegSlot { LeftFront, LeftMiddle, LeftHind, RightFront, RightMiddle, RightHind }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(BeetleProceduralAnimator), typeof(BeetleModelRig))]
    [AddComponentMenu("Beetle Arena/Model/Beetle Model Adapter")]
    public sealed class BeetleModelAdapter : MonoBehaviour
    {
        [Serializable]
        public sealed class Leg
        {
            public BeetleLegSlot slot;
            public Transform hip, knee, ankle, tip;
            [Tooltip("Nominal foot placement adjustment in normalized model space. Does not change the mesh or bind pose.")]
            public Vector3 stanceOffset;
            [Min(0)] public float soleOffset;
            [HideInInspector] public BeetleModelRig.LegBinding captured;
            [HideInInspector] public Vector3 hipInBody, tipInModel;
        }

        [Tooltip("Normalized control frame: +Y up, +Z forward; put source orientation/scale below it.")]
        public Transform model;
        public Transform body, head;
        public Leg[] legs = {
            new Leg { slot = BeetleLegSlot.LeftFront }, new Leg { slot = BeetleLegSlot.LeftMiddle },
            new Leg { slot = BeetleLegSlot.LeftHind }, new Leg { slot = BeetleLegSlot.RightFront },
            new Leg { slot = BeetleLegSlot.RightMiddle }, new Leg { slot = BeetleLegSlot.RightHind }
        };
        public Renderer[] shellRenderers = Array.Empty<Renderer>();
        [HideInInspector] public Vector3 bodyRest;
        [HideInInspector] public Transform capturedModel, capturedBody, capturedHead;

        public bool Validate(out string error, bool requireRest = true)
            => ValidateBindings(out error, requireRest, true);

        bool ValidateBindings(out string error, bool requireRest, bool checkControlPose)
        {
            if (!model || !body || !head || model == transform || !model.IsChildOf(transform) ||
                body.parent != model || head.parent != body)
            { error = "Assign model > body > head control pivots below this object."; return false; }
            if (Quaternion.Angle(model.localRotation, Quaternion.identity) > .1f || (checkControlPose &&
                (Quaternion.Angle(body.localRotation, Quaternion.identity) > .1f ||
                Quaternion.Angle(head.localRotation, Quaternion.identity) > .1f)))
            { error = "Model/body/head control pivots need identity rest rotations. Keep source bone orientation below these pivots."; return false; }
            if (!UniformScale(model) || !UniformScale(body) || !UniformScale(head))
            { error = "The procedural rig requires positive uniform scale. Bake nonuniform or mirrored source scale before binding."; return false; }
            if (requireRest && (capturedModel != model || capturedBody != body || capturedHead != head))
            { error = "Capture the model rest pose after assigning or changing control pivots."; return false; }
            if (legs == null || legs.Length != 6)
            { error = "Assign six logical support legs (left/right × front/middle/hind). Mesh and accessory counts are unrestricted."; return false; }
            var slots = new HashSet<BeetleLegSlot>();
            var joints = new HashSet<Transform> { model, body, head };
            foreach (var leg in legs)
            {
                if (leg == null || (int)leg.slot < 0 || (int)leg.slot > 5 || !slots.Add(leg.slot))
                { error = "Each of the six logical leg slots must be assigned exactly once; array order is free."; return false; }
                if (!leg.hip || !leg.knee || !leg.ankle || !leg.tip || !leg.hip.IsChildOf(body) ||
                    !leg.knee.IsChildOf(leg.hip) || !leg.ankle.IsChildOf(leg.knee) || !leg.tip.IsChildOf(leg.ankle) ||
                    !joints.Add(leg.hip) || !joints.Add(leg.knee) || !joints.Add(leg.ankle) || !joints.Add(leg.tip))
                { error = $"{leg.slot}: assign a distinct hip > knee > ankle > tip chain under body."; return false; }
                for (var node = leg.tip; node && node != model; node = node.parent)
                    if (!UniformScale(node)) { error = $"{leg.slot}: joint chain has nonuniform or negative scale."; return false; }
                if (Vector3.Distance(leg.hip.position, leg.knee.position) < .00001f ||
                    Vector3.Distance(leg.knee.position, leg.ankle.position) < .00001f)
                { error = $"{leg.slot}: upper and lower leg lengths must be nonzero."; return false; }
                if (requireRest && (leg.captured == null || leg.captured.hip != leg.hip ||
                    leg.captured.knee != leg.knee || leg.captured.ankle != leg.ankle || leg.captured.tip != leg.tip))
                { error = $"{leg.slot}: capture rest pose after changing leg bindings."; return false; }
            }
            var appendages = GetComponent<BeetleAppendageRig>();
            if (appendages && appendages.joints != null) foreach (var joint in appendages.joints)
                if (joint != null && joint.pivot)
                {
                    foreach (var control in joints)
                        if (control.IsChildOf(joint.pivot))
                        { error = "An appendage cannot drive a control joint or an ancestor of an IK chain/contact tip."; return false; }
                    if (!joint.pivot.IsChildOf(model))
                    { error = "Appendages must be below model."; return false; }
                }
            if (shellRenderers != null) foreach (var renderer in shellRenderers)
                if (!renderer || !renderer.transform.IsChildOf(model))
                { error = "Shell renderers must belong to this model; remove empty entries."; return false; }
            error = null; return true;
        }

        static bool UniformScale(Transform node)
        {
            var scale = node.lossyScale;
            return scale.x > 0 && scale.y > 0 && scale.z > 0 &&
                Mathf.Abs(scale.x - scale.y) < scale.x * .001f && Mathf.Abs(scale.x - scale.z) < scale.x * .001f;
        }

        public void CaptureRestPose()
        {
            if (!Validate(out var error, false)) throw new InvalidOperationException(error);
            foreach (var leg in legs)
            {
                Vector3 h = model.InverseTransformPoint(leg.hip.position), k = model.InverseTransformPoint(leg.knee.position),
                    a = model.InverseTransformPoint(leg.ankle.position), t = model.InverseTransformPoint(leg.tip.position);
                Vector3 bend = Vector3.ProjectOnPlane(k - h, (a - h).normalized);
                if (bend.sqrMagnitude < .000001f) bend = Vector3.right * ((int)leg.slot < 3 ? -1 : 1);
                leg.captured = new BeetleModelRig.LegBinding
                {
                    hip = leg.hip, knee = leg.knee, ankle = leg.ankle, tip = leg.tip,
                    hipRest = leg.hip.localRotation, kneeRest = leg.knee.localRotation, ankleRest = leg.ankle.localRotation,
                    ankleInModel = Quaternion.Inverse(model.rotation) * leg.ankle.rotation,
                    bendInBody = body.InverseTransformDirection(model.TransformDirection(bend.normalized)),
                    footOffsetInModel = t - a, upperLength = Vector3.Distance(h, k), lowerLength = Vector3.Distance(k, a)
                };
                leg.hipInBody = body.InverseTransformPoint(leg.hip.position); leg.tipInModel = t;
            }
            bodyRest = body.localPosition;
            capturedModel = model; capturedBody = body; capturedHead = head;
        }

        // Rebuild only at the owner initialization boundary, before footplant and pose buffers are created.
        public void ApplyBindings()
        {
            if (!ValidateBindings(out var error, true, false)) throw new InvalidOperationException(error);
            var animator = GetComponent<BeetleProceduralAnimator>();
            var rig = GetComponent<BeetleModelRig>();
            var newLegs = new BeetleLegRig[legs.Length];
            var newBindings = new BeetleModelRig.LegBinding[legs.Length];
            for (int i = 0; i < legs.Length; i++)
            {
                var leg = legs[i]; var b = leg.captured;
                newBindings[i] = b;
                newLegs[i] = new BeetleLegRig
                {
                    side = (int)leg.slot < 3 ? -1 : 1, pair = (int)leg.slot % 3,
                    hip = b.hip, knee = b.knee, ankle = b.ankle, upper = b.hip, lower = b.knee, foot = b.ankle,
                    hipInBody = leg.hipInBody, restAnkle = leg.tipInModel + leg.stanceOffset,
                    upperLength = b.upperLength, lowerLength = b.lowerLength, soleOffset = leg.soleOffset,
                    claws = Array.Empty<Transform>(), teeth = Array.Empty<Transform>()
                };
            }
            animator.model = rig.model = model; animator.bodyPivot = rig.body = body; animator.headPivot = head;
            animator.legs = newLegs; animator.antennae = Array.Empty<Transform>();
            animator.shellRenderers = shellRenderers ?? Array.Empty<Renderer>(); animator.importedRig = rig;
            rig.bindings = newBindings; rig.bodyRest = bodyRest;
            body.localPosition = bodyRest; body.localRotation = head.localRotation = Quaternion.identity;
            rig.ResetRig();
        }
    }
}
