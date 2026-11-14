using System;
using System.Numerics;

namespace Wirenook.Locomotion
{
    /// <summary>
    /// Rotation helpers in the package's one coordinate convention, which is
    /// Unity's: X right, Y up, Z forward, angles in radians. A positive yaw
    /// turns forward toward the right, a positive pitch tips forward toward
    /// the ground, a positive roll raises the right side. Quaternion
    /// components mean the same thing in System.Numerics and UnityEngine, so
    /// editor code converts them field by field.
    /// </summary>
    public static class Rot
    {
        public const float Deg = MathF.PI / 180f;

        public static Quaternion Yaw(float radians) => Quaternion.CreateFromAxisAngle(Vector3.UnitY, radians);

        public static Quaternion Pitch(float radians) => Quaternion.CreateFromAxisAngle(Vector3.UnitX, radians);

        public static Quaternion Roll(float radians) => Quaternion.CreateFromAxisAngle(Vector3.UnitZ, radians);

        /// <summary>Yaw, then pitch, then roll, each about the axis the previous turn produced.</summary>
        public static Quaternion Euler(float yaw, float pitch, float roll) => Yaw(yaw) * Pitch(pitch) * Roll(roll);

        public static Vector3 Apply(Quaternion rotation, Vector3 vector) => Vector3.Transform(vector, rotation);

        /// <summary>
        /// The rotation that carries the X, Y and Z axes onto the given
        /// orthonormal basis.
        /// </summary>
        public static Quaternion FromBasis(Vector3 x, Vector3 y, Vector3 z)
        {
            // Shepperd's method: build from the largest of the four candidate
            // components so the division never comes near zero.
            float trace = x.X + y.Y + z.Z;
            Quaternion q;
            if (trace > 0f)
            {
                float s = MathF.Sqrt(trace + 1f) * 2f;
                q = new Quaternion((y.Z - z.Y) / s, (z.X - x.Z) / s, (x.Y - y.X) / s, 0.25f * s);
            }
            else if (x.X > y.Y && x.X > z.Z)
            {
                float s = MathF.Sqrt(1f + x.X - y.Y - z.Z) * 2f;
                q = new Quaternion(0.25f * s, (y.X + x.Y) / s, (z.X + x.Z) / s, (y.Z - z.Y) / s);
            }
            else if (y.Y > z.Z)
            {
                float s = MathF.Sqrt(1f + y.Y - x.X - z.Z) * 2f;
                q = new Quaternion((y.X + x.Y) / s, 0.25f * s, (z.Y + y.Z) / s, (z.X - x.Z) / s);
            }
            else
            {
                float s = MathF.Sqrt(1f + z.Z - x.X - y.Y) * 2f;
                q = new Quaternion((z.X + x.Z) / s, (z.Y + y.Z) / s, 0.25f * s, (x.Y - y.X) / s);
            }

            return Quaternion.Normalize(q);
        }
    }
}
