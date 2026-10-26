using UnityEngine;

namespace BeetleArena
{
    /// <summary>
    /// Discrete mandible control interface for current imported models. Owns dual jaw pivots only; does not write head, antenna, or physical root.
    /// No autonomous Update: future feeding modules call SetPose prior to fixed-step pose capture to prevent race conditions with visual interpolation.
    /// Tangents, rotation axes, and angles are tailored to current art assets, not empirical insect anatomy measurements.
    /// </summary>
    public sealed class BeetleJawRig : MonoBehaviour
    {
        public Transform left, right;
        public Quaternion leftRest = Quaternion.identity, rightRest = Quaternion.identity;
        [Range(0, 25)] public float openingDegrees = 12;
        [Range(0, 15)] public float sweepDegrees = 6;

        /// <param name="opening">0 = default closed morphology, 1 = maximum lateral flare; over-closure is disallowed.</param>
        /// <param name="sweep">-1..1, shared subtle anterior-posterior sweeping, driven in the future by feeding cadence.</param>
        public void SetPose(float opening, float sweep = 0)
        {
            // Resets to safe resting values on invalid animation input, preventing writing NaN into Transforms.
            if (float.IsNaN(opening) || float.IsInfinity(opening)) opening = 0;
            if (float.IsNaN(sweep) || float.IsInfinity(sweep)) sweep = 0;
            float yaw = Mathf.Clamp01(opening) * Mathf.Clamp(openingDegrees, 0, 25);
            float pitch = Mathf.Clamp(sweep, -1, 1) * Mathf.Clamp(sweepDegrees, 0, 15);
            if (left) left.localRotation = leftRest * Quaternion.Euler(pitch, -yaw, 0);
            if (right) right.localRotation = rightRest * Quaternion.Euler(pitch, yaw, 0);
        }
        public void ResetPose() => SetPose(0);
    }
}
