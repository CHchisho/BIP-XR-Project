using UnityEngine;

namespace SmartHouse
{
    /// <summary>
    /// Measures how far a controller is twisted around its pointing direction, like turning a knob.
    /// Turning the wrist clockwise (to the right, seen from behind the hand) is positive.
    /// </summary>
    public static class WristTwist
    {
        /// <summary>Wrist turn, in degrees, that changes an adjustable value over its full range.</summary>
        public const float FullRangeDegrees = 180f;
        /// <summary>Turn needed before a press becomes a twist, so a slightly shaky press still toggles.</summary>
        public const float DeadZoneDegrees = 12f;

        /// <summary>Signed roll, in degrees, from <paramref name="from"/> to <paramref name="to"/> around the hand's forward axis.</summary>
        public static float Delta(Quaternion from, Quaternion to)
        {
            Vector3 forward = to * Vector3.forward;
            Vector3 a = Vector3.ProjectOnPlane(from * Vector3.up, forward);
            Vector3 b = Vector3.ProjectOnPlane(to * Vector3.up, forward);
            if (a.sqrMagnitude < 1e-6f || b.sqrMagnitude < 1e-6f) return 0f;
            // Unity is left-handed: a positive angle around forward is counter-clockwise seen from behind.
            return -Vector3.SignedAngle(a, b, forward);
        }
    }
}
