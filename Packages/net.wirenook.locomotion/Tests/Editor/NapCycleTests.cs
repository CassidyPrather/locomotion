using System;
using System.Numerics;
using NUnit.Framework;
using static Wirenook.Locomotion.Tests.GaitTestSupport;

namespace Wirenook.Locomotion.Tests
{
    public class NapCycleTests
    {
        [Test]
        public void LoopsWithoutASeam()
        {
            var nap = new NapCycle(new GaitProfile(), Body);
            var first = nap.Evaluate(0f);
            var last = nap.Evaluate(0.9995f);
            AreClose(first.PelvisPosition, nap.Evaluate(1f).PelvisPosition, 1e-6f, "loops");
            AreClose(Rot.Apply(first.HeadRotation, Vector3.UnitZ), Rot.Apply(last.HeadRotation, Vector3.UnitZ), 5e-3f, "head seam");
            AreClose(Rot.Apply(first.ChestRotation, Vector3.UnitZ), Rot.Apply(last.ChestRotation, Vector3.UnitZ), 5e-3f, "chest seam");
            AreClose(Vector3.Zero, nap.RootVelocity, 0f, "a nap goes nowhere");
        }

        [Test]
        public void StandsSlumpedWithTheFeetPlanted()
        {
            var profile = new GaitProfile();
            var nap = new NapCycle(profile, Body);
            var upright = Posture.Stand(profile, Body, 0f, 0f);
            var planted = nap.Evaluate(0f);
            for (int i = 0; i < 220; i++)
            {
                var pose = nap.Evaluate(i / 220f);
                AreClose(planted.LeftFoot.Ankle, pose.LeftFoot.Ankle, 0f, "feet stay planted");
                AreClose(planted.RightFoot.Ankle, pose.RightFoot.Ankle, 0f, "feet stay planted");
                Assert.Less(pose.PelvisPosition.Y, upright.PelvisPosition.Y, "the knees give at " + i);
                var frames = BipedFrames.Solve(pose, Body);
                Assert.IsFalse(frames.LeftLeg.Clamped || frames.RightLeg.Clamped, "legs reach at " + i);

                // Forward is +Z and pitching forward tips it down.
                float chest = -Rot.Apply(pose.ChestRotation, Vector3.UnitZ).Y;
                float head = -Rot.Apply(pose.HeadRotation, Vector3.UnitZ).Y;
                Assert.Greater(chest, MathF.Sin(8f * Rot.Deg), "the chest slumps at " + i);
                Assert.Greater(head, chest + 0.2f, "the head hangs lower than the chest at " + i);
                Assert.Less(head, MathF.Sin(65f * Rot.Deg), "but not onto it at " + i);

                // Plumb arms: swung forward of the chest by as much as the chest tips.
                Assert.AreEqual(MathF.Asin(chest), pose.LeftArm.Swing, 1e-3f, "arms hang plumb at " + i);
                Assert.AreEqual(pose.LeftArm.Swing, pose.RightArm.Swing, 0f, "both alike at " + i);
            }
        }

        [Test]
        public void TheHeadSinksSlowlyAndIsCaughtQuickly()
        {
            Assert.AreEqual(0f, NapCycle.Sink(0f), 1e-6f);
            Assert.AreEqual(NapCycle.Sink(0f), NapCycle.Sink(1f), 1e-6f, "meets itself");

            float lowestAt = 0f, lowest = 0f;
            for (int i = 0; i <= 1000; i++)
            {
                float sink = NapCycle.Sink(i / 1000f);
                Assert.GreaterOrEqual(sink, 0f);
                Assert.LessOrEqual(sink, 1f);
                if (sink > lowest)
                {
                    lowest = sink;
                    lowestAt = i / 1000f;
                }
            }

            Assert.AreEqual(1f, lowest, 1e-3f, "reaches the bottom");
            Assert.Greater(lowestAt, 0.75f, "most of the loop is the sinking");
        }
    }
}
