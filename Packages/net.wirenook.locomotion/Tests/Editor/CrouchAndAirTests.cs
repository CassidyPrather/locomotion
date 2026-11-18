using System;
using System.Numerics;
using NUnit.Framework;
using static Wirenook.Locomotion.Tests.GaitTestSupport;

namespace Wirenook.Locomotion.Tests
{
    public class CrouchAndAirTests
    {
        private const int Samples = 120;

        /// <summary>The velocities the crouched tree is baked at, as (right, forward) in m/s.</summary>
        private static readonly (float right, float forward)[] CrouchVelocities =
        {
            (0f, 0.6f), (0f, 1.25f), (0f, 2f), (0f, -1.25f), (1.25f, 0f), (-1.25f, 0f), (1.25f, 1.25f), (1.25f, -1.25f),
        };

        private static float PitchOf(Quaternion rotation) => -Rot.Apply(rotation, Vector3.UnitZ).Y;

        [Test]
        public void CrouchedWalkIsLowerAndLeansOverTheKnees()
        {
            var profile = new GaitProfile();
            foreach (var (right, forward) in CrouchVelocities)
            {
                var request = GaitRequest.FromVelocity(right, forward);
                var upright = new GaitCycle(profile, Body, request);
                var crouched = new GaitCycle(profile, Body, request, crouched: true);
                for (int i = 0; i < Samples; i++)
                {
                    float phase = (float)i / Samples;
                    var high = upright.Evaluate(phase);
                    var low = crouched.Evaluate(phase);
                    string where = Describe(request) + " at " + phase;
                    Assert.Less(low.PelvisPosition.Y, high.PelvisPosition.Y - 0.25f * Body.LegLength, where + ": well below an upright walk");
                    Assert.Greater(PitchOf(low.ChestRotation), PitchOf(high.ChestRotation) + MathF.Sin(15f * Rot.Deg), where + ": chest forward");
                    Assert.Greater(low.LeftArm.ElbowBend, 45f * Rot.Deg, where + ": arms carried bent");
                }
            }
        }

        [Test]
        public void CrouchedWalkKeepsItsFeetOnTheGroundAndInReach()
        {
            var profile = new GaitProfile();
            foreach (var (right, forward) in CrouchVelocities)
            {
                var request = GaitRequest.FromVelocity(right, forward);
                var cycle = new GaitCycle(profile, Body, request, crouched: true);
                AreClose(cycle.Evaluate(0f).PelvisPosition, cycle.Evaluate(1f).PelvisPosition, 1e-5f, "loops");
                Assert.AreEqual(request.Speed, cycle.Timing.StrideLength / cycle.Duration, 1e-3f, "a stride per cycle covers the speed");
                for (int i = 0; i < Samples; i++)
                {
                    float phase = (float)i / Samples;
                    var pose = cycle.Evaluate(phase);
                    string where = Describe(request) + " at " + phase;
                    var frames = BipedFrames.Solve(pose, Body);
                    Assert.IsFalse(frames.LeftLeg.Clamped || frames.RightLeg.Clamped, where + ": legs reach");
                    Assert.Greater(frames.LeftLeg.KneeFlexion + frames.RightLeg.KneeFlexion, 60f * Rot.Deg, where + ": knees stay bent");
                    foreach (var side in new[] { Side.Left, Side.Right })
                    {
                        foreach (var point in SolePoints(pose.Foot(side)))
                        {
                            Assert.GreaterOrEqual(point.Y, -1e-4f, where + " " + side);
                        }
                    }
                }
            }
        }

        [Test]
        public void CrouchedWalkTakesShorterStepsAndRidesLeveller()
        {
            var profile = new GaitProfile();
            var upright = Cycle(0f, 1.25f, profile);
            var crouched = new GaitCycle(profile, Body, GaitRequest.FromVelocity(0f, 1.25f), crouched: true);
            Assert.Less(crouched.Timing.StrideLength, 0.9f * upright.Timing.StrideLength);

            float Travel(GaitCycle cycle)
            {
                float low = float.MaxValue, high = float.MinValue;
                for (int i = 0; i < Samples; i++)
                {
                    float y = cycle.Evaluate((float)i / Samples).PelvisPosition.Y;
                    low = MathF.Min(low, y);
                    high = MathF.Max(high, y);
                }

                return high - low;
            }

            Assert.Less(Travel(crouched), Travel(upright), "bent legs carry the pelvis more level");
        }

        [Test]
        public void CrouchedIdleMatchesTheCrouchedWalkAndLoops()
        {
            var profile = new GaitProfile();
            var standing = new StandingCycle(profile, Body);
            var idle = new StandingCycle(profile, Body, crouched: true);
            var walk = new GaitCycle(profile, Body, GaitRequest.FromVelocity(0f, 0.6f), crouched: true);
            AreClose(idle.Evaluate(0f).PelvisPosition, idle.Evaluate(1f).PelvisPosition, 1e-6f, "loops");

            float walkHeight = 0f;
            for (int i = 0; i < Samples; i++) walkHeight += walk.Evaluate((float)i / Samples).PelvisPosition.Y / Samples;

            for (int i = 0; i < Samples; i++)
            {
                float phase = (float)i / Samples;
                var pose = idle.Evaluate(phase);
                Assert.Less(pose.PelvisPosition.Y, standing.Evaluate(phase).PelvisPosition.Y - 0.3f * Body.LegLength, "low at " + phase);
                Assert.AreEqual(walkHeight, pose.PelvisPosition.Y, 0.05f * Body.LegLength, "starting to walk does not change the height at " + phase);
                Assert.Less(pose.PelvisPosition.Z, -0.05f, "hips back at " + phase);
                Assert.Greater(PitchOf(pose.ChestRotation), MathF.Sin(15f * Rot.Deg), "chest forward at " + phase);
                var frames = BipedFrames.Solve(pose, Body);
                Assert.IsFalse(frames.LeftLeg.Clamped || frames.RightLeg.Clamped, "legs reach at " + phase);
            }
        }

        [Test]
        public void TrackedBodyCrouchesWithoutTippingTheChest()
        {
            var tracked = new GaitProfile().ForTrackedUpperBody();
            var idle = new StandingCycle(tracked, Body, crouched: true);
            var walk = new GaitCycle(tracked, Body, GaitRequest.FromVelocity(0f, 1.25f), crouched: true);
            for (int i = 0; i < 40; i++)
            {
                AreClose(Quaternion.Identity, idle.Evaluate(i / 40f).ChestRotation, 0.02f, "idle chest stays square");
                AreClose(Quaternion.Identity, walk.Evaluate(i / 40f).ChestRotation, 0.02f, "walking chest stays square");
            }
        }

        [Test]
        public void AirborneFeetAreOffTheGroundAndTheLegsReachThem()
        {
            foreach (var style in GaitStyles.All)
            {
                foreach (AirMoment moment in Enum.GetValues(typeof(AirMoment)))
                {
                    var air = new AirCycle(style.Profile, Body, moment);
                    AreClose(air.Evaluate(0f).LeftFoot.Ankle, air.Evaluate(1f).LeftFoot.Ankle, 1e-5f, "loops");
                    AreClose(Vector3.Zero, air.RootVelocity, 0f, "the root carries the body; the pose goes nowhere");
                    for (int i = 0; i < Samples; i++)
                    {
                        float phase = (float)i / Samples;
                        var pose = air.Evaluate(phase);
                        string where = style.Name + " " + moment + " at " + phase;
                        var frames = BipedFrames.Solve(pose, Body);
                        Assert.IsFalse(frames.LeftLeg.Clamped || frames.RightLeg.Clamped, where + ": legs reach");
                        foreach (var side in new[] { Side.Left, Side.Right })
                        {
                            Assert.AreEqual(FootContact.None, pose.Foot(side).Contact, where);
                            Assert.Greater(pose.Foot(side).Ankle.Y, Body.AnkleHeight + 0.01f, where + ": " + side + " foot is lifted");
                        }
                    }
                }
            }
        }

        [Test]
        public void ALongDropThrowsTheLimbsWiderThanAHop()
        {
            var profile = new GaitProfile();
            var rising = new AirCycle(profile, Body, AirMoment.Rising).Evaluate(0.2f);
            var falling = new AirCycle(profile, Body, AirMoment.Falling).Evaluate(0.2f);
            var plunging = new AirCycle(profile, Body, AirMoment.Plunging).Evaluate(0.2f);
            Assert.Greater(falling.LeftArm.Spread, rising.LeftArm.Spread, "arms come out on the way down");
            Assert.Greater(plunging.LeftArm.Spread, falling.LeftArm.Spread + 20f * Rot.Deg, "and much further in a long drop");
            Assert.Greater(plunging.LeftFoot.Ankle.Y, falling.LeftFoot.Ankle.Y, "knees come up");
            Assert.Greater(plunging.RightFoot.Ankle.X - plunging.LeftFoot.Ankle.X,
                falling.RightFoot.Ankle.X - falling.LeftFoot.Ankle.X, "and apart");
            Assert.Greater(PitchOf(plunging.ChestRotation), PitchOf(falling.ChestRotation), "the body tips to see the ground");
        }

        [Test]
        public void TrackedBodyKeepsItsArmsAndChestInTheAir()
        {
            var tracked = new GaitProfile().ForTrackedUpperBody();
            foreach (AirMoment moment in Enum.GetValues(typeof(AirMoment)))
            {
                var pose = new AirCycle(tracked, Body, moment).Evaluate(0.3f);
                AreClose(Quaternion.Identity, pose.ChestRotation, 1e-4f, moment + ": chest stays square");
                Assert.AreEqual(tracked.ArmSpread * Rot.Deg, pose.LeftArm.Spread, 1e-5f, moment + ": arms stay down");
                Assert.Greater(pose.LeftFoot.Ankle.Y, Body.AnkleHeight, moment + ": the legs still tuck");
            }
        }

        [Test]
        public void LandingSinksOnceAndStandsUpAgain()
        {
            var profile = new GaitProfile();
            var standing = Posture.Stand(profile, Body, 0f, 0f);
            foreach (bool hard in new[] { false, true })
            {
                var landing = new LandingCycle(profile, Body, hard);
                AreClose(standing.PelvisPosition, landing.Evaluate(1f).PelvisPosition, 1e-5f, "ends standing");
                AreClose(standing.PelvisPosition, landing.Evaluate(0f).PelvisPosition, 1e-5f, "starts at standing height, at the instant of landing");
                AreClose(landing.Evaluate(1f).PelvisPosition, landing.Evaluate(1.5f).PelvisPosition, 0f, "holds its end");

                float lowest = float.MaxValue, lowestAt = 0f, previous = landing.Evaluate(0f).PelvisPosition.Y;
                bool rising = false;
                for (int i = 1; i <= 400; i++)
                {
                    float phase = i / 400f;
                    var pose = landing.Evaluate(phase);
                    float y = pose.PelvisPosition.Y;
                    if (y < lowest)
                    {
                        lowest = y;
                        lowestAt = phase;
                    }

                    // Down, then up: once it has started to rise it does not sink again.
                    if (y > previous + 1e-6f) rising = true;
                    if (rising) Assert.GreaterOrEqual(y, previous - 1e-6f, "no second dip at " + phase);
                    previous = y;

                    AreClose(standing.LeftFoot.Ankle, pose.LeftFoot.Ankle, 0f, "feet stay where they landed");
                    var frames = BipedFrames.Solve(pose, Body);
                    Assert.IsFalse(frames.LeftLeg.Clamped || frames.RightLeg.Clamped, "legs reach at " + phase);
                }

                Assert.Less(lowestAt, 0.4f, "the bottom comes early");
                float depth = standing.PelvisPosition.Y - lowest;
                if (hard) Assert.Greater(depth, 0.25f * Body.LegLength, "a hard landing sinks deep");
                else Assert.Less(depth, 0.15f * Body.LegLength, "a soft one only dips");
            }
        }

        [Test]
        public void AHardLandingTakesLongerAndStaysDown()
        {
            var profile = new GaitProfile();
            var soft = new LandingCycle(profile, Body, hard: false);
            var hard = new LandingCycle(profile, Body, hard: true);
            Assert.Greater(hard.Duration, 2f * soft.Duration);
            Assert.AreEqual(1f, hard.Sink(0.2f), 1e-6f, "held at the bottom");
            Assert.AreEqual(1f, hard.Sink(0.3f), 1e-6f, "held at the bottom");
            Assert.Less(soft.Sink(0.5f), 1f, "a soft landing is already on its way up");
        }
    }
}
