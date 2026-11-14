namespace Wirenook.Locomotion.Editor
{
    /// <summary>
    /// Conversions between the engine-free core's System.Numerics types and
    /// Unity's. Both use the same component layout and the same handedness
    /// convention for the package (see <see cref="Rot"/>), so these are copies.
    /// </summary>
    internal static class Numeric
    {
        public static UnityEngine.Vector3 ToUnity(this System.Numerics.Vector3 v)
        {
            return new UnityEngine.Vector3(v.X, v.Y, v.Z);
        }

        public static UnityEngine.Quaternion ToUnity(this System.Numerics.Quaternion q)
        {
            return new UnityEngine.Quaternion(q.X, q.Y, q.Z, q.W);
        }

        public static System.Numerics.Vector3 ToNumerics(this UnityEngine.Vector3 v)
        {
            return new System.Numerics.Vector3(v.x, v.y, v.z);
        }
    }
}
