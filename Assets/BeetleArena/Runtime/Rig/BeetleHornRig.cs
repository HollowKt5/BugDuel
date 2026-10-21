using System;
using UnityEngine;

namespace BeetleArena
{
    /// <summary>Explicit horn anatomy. The cephalic horn follows the head; the thoracic horn follows the thorax.</summary>
    public sealed class BeetleHornRig : MonoBehaviour
    {
        [Serializable]
        public sealed class ContactBinding
        {
            public CapsuleCollider shape;
            public Vector3 a, b, restPosition, restScale;
            public Quaternion restRotation;
            public float radius, height;
        }

        public Transform head, lengthPivot;
        public Renderer cephalicSurface, thoracicSurface;
        public ContactBinding[] contacts = Array.Empty<ContactBinding>();
        Vector3[] groundRest;
        public bool Owns(Renderer surface) => surface == cephalicSurface || surface == thoracicSurface;

        // Called only when binding an individual, not every animation/physics step.
        // Keep the skull, eyes, mouth and antenna sockets at their original proportions.
        public void SetLength(float factor)
        {
            if (!head || !lengthPivot) return;
            factor = Mathf.Max(.01f, factor);
            head.localScale = Vector3.one;
            lengthPivot.localScale = new Vector3(1, 1, factor);
            float rootZ = lengthPivot.localPosition.z;
            var modelRig = GetComponent<BeetleModelRig>();
            if (modelRig && modelRig.headGroundPoints != null)
            {
                if (groundRest == null) groundRest = (Vector3[])modelRig.headGroundPoints.Clone();
                for (int i = 0; i < groundRest.Length; i++)
                {
                    var point = groundRest[i];
                    point.z += Mathf.Max(0, point.z - rootZ) * (factor - 1);
                    modelRig.headGroundPoints[i] = point;
                }
            }
            foreach (var binding in contacts)
            {
                var shape = binding.shape;
                if (!shape) continue;
                shape.transform.localScale = binding.restScale;
                shape.radius = binding.radius;
                if (Mathf.Approximately(factor, 1))
                {
                    shape.transform.localPosition = binding.restPosition;
                    shape.transform.localRotation = binding.restRotation;
                    shape.height = binding.height;
                    continue;
                }
                // Root-side contact endpoints stay on the head. Distal endpoints follow the
                // same longitudinal stretch as the visible horn; the shaft stays rigid in play.
                Vector3 a = binding.a, b = binding.b;
                a.z += Mathf.Max(0, a.z - rootZ) * (factor - 1);
                b.z += Mathf.Max(0, b.z - rootZ) * (factor - 1);
                shape.transform.localPosition = (a + b) * .5f;
                shape.transform.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
                shape.height = Vector3.Distance(a, b) + 2 * binding.radius;
            }
        }
    }
}
