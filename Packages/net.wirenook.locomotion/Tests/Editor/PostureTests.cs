using System;
using System.Numerics;
using NUnit.Framework;
using static Wirenook.Locomotion.Tests.GaitTestSupport;

namespace Wirenook.Locomotion.Tests
{
    public class PostureTests
    {
        [Test]
        public void RotationConventionsMatchUnity()
        {
            AreClose(Vector3.UnitX, Rot.Apply(Rot.Yaw(MathF.PI / 2f), Vector3.UnitZ), 1e-6f, "yaw turns forward to the right");
            AreClose(-Vector3.UnitY, Rot.Apply(Rot.Pitch(MathF.PI / 2f), Vector3.UnitZ), 1e-6f, "pitch tips forward down");
            AreClose(Vector3.UnitY, Rot.Apply(Rot.Roll(MathF.PI / 2f), Vector3.UnitX), 1e-6f, "roll raises the right side");

            // Yaw is applied last: a pitched-then-yawed forward vector stays tipped down by the pitch.
            var forward = Rot.Apply(Rot.Euler(MathF.PI / 2f, 0.3f, 0f), Vector3.UnitZ);
            Assert.AreEqual(-MathF.Sin(0.3f), forward.Y, 1e-6f);
            Assert.AreEqual(MathF.Cos(0.3f), forward.X, 1e-6f);
        }

        [Test]
        public void FromBasisRecoversAnyRotation()
        {
            var random = new Random(3);
            for (int i = 0; i < 200; i++)
            {
                var q = Quaternion.Normalize(new Quaternion((float)random.NextDouble() - 0.5f,
                    (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f));
                var rebuilt = Rot.FromBasis(Rot.Apply(q, Vector3.UnitX), Rot.Apply(q, Vector3.UnitY), Rot.Apply(q, Vector3.UnitZ));
                AreClose(q, rebuilt, 1e-3f, "rotation " + i);
            }
        }

        [Test]
        public void IdleLoopsAndIsNotAStatue()
        {
            var idle = new StandingCycle(new GaitProfile(), Body);
            AreClose(idle.Evaluate(0f).PelvisPosition, idle.Evaluate(1f).PelvisPosition, 1e-6f, "loops");
            AreClose(idle.Evaluate(0.999f).PelvisPosition, idle.Evaluate(0f).PelvisPosition, 1e-3f, "seam is continuous");
            AreClose(Vector3.Zero, idle.RootVelocity, 0f, "idle goes nowhere");

            float left = float.MaxValue, right = float.MinValue, breathLow = float.MaxValue, breathHigh = float.MinValue;
            var planted = idle.Evaluate(0f);
            for (int i = 0; i < 200; i++)
            {
                var pose = idle.Evaluate(i / 200f);
                left = MathF.Min(left, pose.PelvisPosition.X);
                right = MathF.Max(right, pose.PelvisPosition.X);
                float chestPitch = Rot.Apply(pose.ChestRotation, Vector3.UnitY).Z;
                breathLow = MathF.Min(breathLow, chestPitch);
                breathHigh = MathF.Max(breathHigh, chestPitch);

                AreClose(planted.LeftFoot.Ankle, pose.LeftFoot.Ankle, 0f, "feet stay planted");
                AreClose(planted.RightFoot.Ankle, pose.RightFoot.Ankle, 0f, "feet stay planted");
                var frames = BipedFrames.Solve(pose, Body);
                Assert.IsFalse(frames.LeftLeg.Clamped || frames.RightLeg.Clamped, "legs reach at " + i);
            }

            Assert.Greater(right - left, 0.02f, "weight shifts by centimetres");
            Assert.AreEqual(-left, right, 1e-4f, "evenly to both sides");
            Assert.Greater(breathHigh - breathLow, 2f * Rot.Deg, "the chest breathes");
        }

        [Test]
        public void LeaningKeepsTheFeetAndTipsTheBody()
        {
            var profile = new GaitProfile();
            var neutral = Posture.Stand(profile, Body, 0f, 0f);
            var forward = Posture.Stand(profile, Body, 10f * Rot.Deg, 0f);
            var right = Posture.Stand(profile, Body, 0f, 10f * Rot.Deg);

            AreClose(neutral.LeftFoot.Ankle, forward.LeftFoot.Ankle, 0f, "feet do not move");
            AreClose(neutral.RightFoot.Ankle, right.RightFoot.Ankle, 0f, "feet do not move");
            AreClose(Quaternion.Identity, neutral.ChestRotation, 1e-6f, "neutral is upright");

            Assert.Greater(forward.PelvisPosition.Z, neutral.PelvisPosition.Z + 0.03f, "the pelvis moves over the toes");
            Assert.AreEqual(MathF.Sin(10f * Rot.Deg), Rot.Apply(forward.ChestRotation, Vector3.UnitY).Z, 1e-4f, "chest tips forward");
            Assert.Greater(right.PelvisPosition.X, neutral.PelvisPosition.X + 0.03f);
            Assert.Greater(Rot.Apply(right.ChestRotation, Vector3.UnitY).X, 0.1f, "chest tips right");
            // The head takes less of the lean than the chest does.
            Assert.Less(Rot.Apply(forward.HeadRotation, Vector3.UnitY).Z, 0.5f * Rot.Apply(forward.ChestRotation, Vector3.UnitY).Z);

            foreach (var pose in new[] { neutral, forward, right })
            {
                var frames = BipedFrames.Solve(pose, Body);
                Assert.IsFalse(frames.LeftLeg.Clamped || frames.RightLeg.Clamped);
            }
        }

        [Test]
        public void ArmsHangDownSwingForwardAndSpreadOutward()
        {
            var pose = Posture.Stand(new GaitProfile { ArmSpread = 10f }, Body, 0f, 0f);
            pose.RightArm.Swing = 30f * Rot.Deg;
            pose.RightArm.ElbowBend = 45f * Rot.Deg;
            var frames = BipedFrames.Solve(pose, Body);

            // A segment's -Y axis runs down it, from shoulder to elbow or elbow to wrist.
            var leftUpper = Rot.Apply(frames.LeftArm.UpperArm, -Vector3.UnitY);
            var rightUpper = Rot.Apply(frames.RightArm.UpperArm, -Vector3.UnitY);
            var rightFore = Rot.Apply(frames.RightArm.Forearm, -Vector3.UnitY);
            Assert.Less(leftUpper.Y, -0.9f, "left arm hangs down");
            Assert.Less(leftUpper.X, -0.1f, "and out to the left");
            Assert.AreEqual(0f, leftUpper.Z, 1e-5f);
            Assert.Greater(rightUpper.X, 0.1f, "right arm is out to the right");
            Assert.Greater(rightUpper.Z, 0.4f, "and swung forward");
            Assert.Greater(rightFore.Z, rightUpper.Z + 0.2f, "the elbow bends the forearm further forward");
        }

        [Test]
        public void SpineInterpolatesFromPelvisToChest()
        {
            var pose = Posture.Stand(new GaitProfile(), Body, 20f * Rot.Deg, 0f);
            var frames = BipedFrames.Solve(pose, Body);
            AreClose(frames.Pelvis, frames.Spine(0f), 1e-5f, "bottom of the spine");
            AreClose(frames.Chest, frames.Spine(1f), 1e-5f, "top of the spine");
            float half = Angle(frames.Pelvis, frames.Spine(0.5f));
            Assert.AreEqual(0.5f * Angle(frames.Pelvis, frames.Chest), half, 1e-4f);
        }
    }
}
