using System;
using System.Numerics;
using NUnit.Framework;
using static Wirenook.Locomotion.Tests.GaitTestSupport;

namespace Wirenook.Locomotion.Tests
{
    public class TwoBoneIkTests
    {
        private const float Upper = 0.42f;
        private const float Lower = 0.4f;
        private static readonly Vector3 Hip = new Vector3(0.1f, 0.9f, 0.05f);

        [Test]
        public void ReachesEveryReachableTarget()
        {
            var random = new Random(7);
            for (int i = 0; i < 500; i++)
            {
                var direction = Vector3.Normalize(new Vector3(Next(random), Next(random) - 1.2f, Next(random)));
                float distance = 0.1f + 0.7f * (float)random.NextDouble();
                var target = Hip + direction * distance;
                var result = TwoBoneIk.Solve(Hip, target, Vector3.UnitZ, Upper, Lower);

                Assert.IsFalse(result.Clamped, "target " + i);
                AreClose(target, result.End, 1e-5f, "end reaches the target");
                Assert.AreEqual(Upper, (result.Middle - Hip).Length(), 1e-4f, "upper segment keeps its length");
                Assert.AreEqual(Lower, (result.End - result.Middle).Length(), 1e-4f, "lower segment keeps its length");

                // The knee is on the pole's side of the hip-to-ankle line.
                var axis = Vector3.Normalize(target - Hip);
                var offLine = result.Middle - Hip - axis * Vector3.Dot(result.Middle - Hip, axis);
                var pole = Vector3.UnitZ - axis * Vector3.Dot(Vector3.UnitZ, axis);
                Assert.GreaterOrEqual(Vector3.Dot(offLine, pole), 0f, "knee bends toward the pole");
            }
        }

        [Test]
        public void SegmentRotationsPointAlongTheSegments()
        {
            var target = Hip + new Vector3(0.05f, -0.6f, 0.2f);
            var result = TwoBoneIk.Solve(Hip, target, Vector3.UnitZ, Upper, Lower);
            // By convention each frame's Y axis runs up the segment.
            AreClose(Vector3.Normalize(Hip - result.Middle), Rot.Apply(result.UpperRotation, Vector3.UnitY), 1e-4f, "thigh");
            AreClose(Vector3.Normalize(result.Middle - result.End), Rot.Apply(result.LowerRotation, Vector3.UnitY), 1e-4f, "shin");
            // And both turn about one hinge axis.
            AreClose(Rot.Apply(result.UpperRotation, Vector3.UnitX), Rot.Apply(result.LowerRotation, Vector3.UnitX), 1e-4f, "hinge");
            Assert.Greater(result.Flexion, 0.1f);
        }

        [Test]
        public void StraightDownWithKneeForwardIsTheIdentity()
        {
            var result = TwoBoneIk.Solve(Hip, Hip - Vector3.UnitY * 0.8f, Vector3.UnitZ, Upper, Lower);
            AreClose(Quaternion.Identity, result.UpperRotation, 0.25f, "thigh is near identity");
            AreClose(Vector3.UnitX, Rot.Apply(result.UpperRotation, Vector3.UnitX), 1e-4f, "hinge is the X axis");
            Assert.Greater(result.Middle.Z, Hip.Z, "knee is forward");
        }

        [Test]
        public void ClampsTargetsBeyondReach()
        {
            var direction = Vector3.Normalize(new Vector3(0.3f, -1f, 0.4f));
            var result = TwoBoneIk.Solve(Hip, Hip + direction * 5f, Vector3.UnitZ, Upper, Lower);
            Assert.IsTrue(result.Clamped);
            AssertFinite(result);
            Assert.AreEqual(Upper + Lower, (result.End - Hip).Length(), 1e-3f, "leg is at full stretch");
            AreClose(direction, Vector3.Normalize(result.End - Hip), 1e-4f, "and points at the target");
            Assert.Less(result.Flexion, 0.05f, "nearly straight");
        }

        [Test]
        public void DegenerateInputsStayFinite()
        {
            AssertFinite(TwoBoneIk.Solve(Hip, Hip, Vector3.UnitZ, Upper, Lower));
            AssertFinite(TwoBoneIk.Solve(Hip, Hip + Vector3.UnitZ * 0.5f, Vector3.UnitZ, Upper, Lower));
            AssertFinite(TwoBoneIk.Solve(Hip, Hip - Vector3.UnitY * 0.5f, Vector3.Zero, Upper, Lower));
            AssertFinite(TwoBoneIk.Solve(Hip, Hip + Vector3.UnitY * 1e-9f, -Vector3.UnitY, Upper, Lower));
            // Equal segments folded completely: the target sits on the hip.
            var folded = TwoBoneIk.Solve(Hip, Hip, Vector3.UnitZ, 0.4f, 0.4f);
            AssertFinite(folded);
            Assert.IsTrue(folded.Clamped);
        }

        private static float Next(Random random) => (float)(random.NextDouble() * 2.0 - 1.0);

        private static void AssertFinite(TwoBoneIkResult result)
        {
            foreach (float value in new[]
                     {
                         result.Middle.X, result.Middle.Y, result.Middle.Z, result.End.X, result.End.Y, result.End.Z,
                         result.Flexion, result.UpperRotation.X, result.UpperRotation.Y, result.UpperRotation.Z,
                         result.UpperRotation.W, result.LowerRotation.X, result.LowerRotation.Y,
                         result.LowerRotation.Z, result.LowerRotation.W,
                     })
            {
                Assert.IsFalse(float.IsNaN(value) || float.IsInfinity(value), "result is finite");
            }
        }
    }
}
