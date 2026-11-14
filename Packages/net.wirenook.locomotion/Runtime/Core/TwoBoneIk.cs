using System;
using System.Numerics;

namespace Wirenook.Locomotion
{
    /// <summary>Where a two-segment limb ends up when its end is asked to reach a target.</summary>
    public struct TwoBoneIkResult
    {
        /// <summary>Middle joint (knee) position.</summary>
        public Vector3 Middle;

        /// <summary>End joint (ankle) position: the target, or the nearest point the limb can reach.</summary>
        public Vector3 End;

        /// <summary>Flexion of the middle joint in radians; zero is a straight limb.</summary>
        public float Flexion;

        /// <summary>True when the target was out of reach and <see cref="End"/> is not on it.</summary>
        public bool Clamped;

        /// <summary>
        /// Orientation of the upper segment: its Y axis runs from the middle
        /// joint up to the root joint and its Z axis is the way the middle
        /// joint bends outward, so a limb hanging straight down with its knee
        /// forward has the identity.
        /// </summary>
        public Quaternion UpperRotation;

        /// <summary>Orientation of the lower segment, by the same convention.</summary>
        public Quaternion LowerRotation;
    }

    /// <summary>
    /// Closed-form inverse kinematics for a limb of two rigid segments: the
    /// three joints lie in the plane through the root, the target and the
    /// pole direction, and the law of cosines places the middle joint.
    /// </summary>
    public static class TwoBoneIk
    {
        /// <param name="root">Root joint (hip).</param>
        /// <param name="target">Where the end joint (ankle) should be.</param>
        /// <param name="pole">Direction the middle joint (knee) should point; need not be exact.</param>
        /// <param name="upperLength">Root to middle joint.</param>
        /// <param name="lowerLength">Middle to end joint.</param>
        public static TwoBoneIkResult Solve(Vector3 root, Vector3 target, Vector3 pole, float upperLength,
            float lowerLength)
        {
            var toTarget = target - root;
            float distance = toTarget.Length();
            // With nothing to aim at, hang straight down.
            var axis = distance > 1e-6f ? toTarget / distance : -Vector3.UnitY;

            // A limb can neither stretch past its length nor fold shorter than
            // the difference of its segments. The margin keeps the triangle
            // from degenerating, where the middle joint's side is undefined.
            float longest = (upperLength + lowerLength) * 0.9999f;
            float shortest = MathF.Abs(upperLength - lowerLength) + 1e-4f * (upperLength + lowerLength);
            float reach = Shaping.Clamp(distance, shortest, longest);

            // The bend direction is the pole with its component along the limb
            // removed. If the pole points along the limb, any perpendicular
            // will do, as long as it is always the same one.
            var bend = pole - axis * Vector3.Dot(pole, axis);
            if (bend.LengthSquared() < 1e-10f)
            {
                var other = MathF.Abs(axis.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
                bend = other - axis * Vector3.Dot(other, axis);
            }

            bend = Vector3.Normalize(bend);

            float along = (upperLength * upperLength - lowerLength * lowerLength + reach * reach) / (2f * reach);
            float outward = MathF.Sqrt(MathF.Max(upperLength * upperLength - along * along, 0f));
            var middle = root + axis * along + bend * outward;
            var end = root + axis * reach;

            // Both segments turn about the same hinge axis, the normal of the limb's plane.
            var hinge = Vector3.Normalize(Vector3.Cross(-axis, bend));
            var upperUp = Vector3.Normalize(root - middle);
            var lowerUp = Vector3.Normalize(middle - end);

            return new TwoBoneIkResult
            {
                Middle = middle,
                End = end,
                Flexion = MathF.Acos(Shaping.Clamp(Vector3.Dot(upperUp, lowerUp), -1f, 1f)),
                Clamped = MathF.Abs(reach - distance) > 1e-5f,
                UpperRotation = Rot.FromBasis(hinge, upperUp, Vector3.Cross(hinge, upperUp)),
                LowerRotation = Rot.FromBasis(hinge, lowerUp, Vector3.Cross(hinge, lowerUp)),
            };
        }
    }
}
