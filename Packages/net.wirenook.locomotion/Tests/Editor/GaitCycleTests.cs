using System;
using System.Numerics;
using NUnit.Framework;
using static Wirenook.Locomotion.Tests.GaitTestSupport;

namespace Wirenook.Locomotion.Tests
{
    public class GaitCycleTests
    {
        private const int Samples = 400;

        [Test]
        public void PoseAtPhaseOneEqualsPhaseZero()
        {
            foreach (var request in Requests())
            {
                var cycle = new GaitCycle(new GaitProfile(), Body, request);
                AssertSamePose(cycle.Evaluate(0f), cycle.Evaluate(1f), 1e-6f, 1e-6f, Describe(request));
                AssertSamePose(cycle.Evaluate(0.25f), cycle.Evaluate(-0.75f), 1e-5f, 1e-5f, Describe(request));
            }
        }

        [Test]
        public void CycleIsContinuousAcrossTheSeamAndEverywhereElse()
        {
            foreach (var request in Requests())
            {
                var cycle = new GaitCycle(new GaitProfile(), Body, request);
                // Nothing in a body moves faster than a sprinting foot swings,
                // so a step of 1/4000 of a cycle can only move things a little.
                const int steps = 4000;
                float allowed = 40f * cycle.Duration / steps;
                var previous = cycle.Evaluate(-1f / steps);
                for (int i = 0; i <= steps; i++)
                {
                    var pose = cycle.Evaluate((float)i / steps);
                    AssertSamePose(previous, pose, allowed, 0.02f, Describe(request) + " at step " + i);
                    previous = pose;
                }
            }
        }

        [Test]
        public void PlantedFootDoesNotSlide()
        {
            foreach (var request in Requests())
            {
                var cycle = new GaitCycle(new GaitProfile(), Body, request);
                float dt = 1e-3f * cycle.Duration;
                var slip = -cycle.RootVelocity * dt;
                int checkedSamples = 0;
                for (int i = 0; i < Samples; i++)
                {
                    float phase = (float)i / Samples;
                    var now = cycle.Evaluate(phase);
                    var next = cycle.Evaluate(phase + 1e-3f);
                    foreach (var side in new[] { Side.Left, Side.Right })
                    {
                        var a = now.Foot(side);
                        var b = next.Foot(side);
                        // Compare only within one kind of contact: the point
                        // legitimately changes when the foot rolls from heel to sole to ball.
                        if (a.Contact == FootContact.None || a.Contact != b.Contact) continue;
                        AreClose(a.ContactPoint + slip, b.ContactPoint, 2e-5f,
                            Describe(request) + " " + side + " contact at " + phase);
                        Assert.AreEqual(0f, a.ContactPoint.Y, 1e-5f, "contact point is on the ground");
                        if (a.Contact == FootContact.Flat)
                        {
                            AreClose(a.Ankle + slip, b.Ankle, 2e-5f, Describe(request) + " " + side + " ankle at " + phase);
                        }

                        checkedSamples++;
                    }
                }

                // Two feet, each down for the duty factor's share of the samples,
                // less the few that straddle a change of contact.
                Assert.Greater(checkedSamples, (int)(1.5f * cycle.Timing.DutyFactor * Samples), "stance was sampled");
            }
        }

        [Test]
        public void ContactPointIsTheSolePointTheFootRollsOn()
        {
            var cycle = Cycle(0f, 1.4f);
            for (int i = 0; i < Samples; i++)
            {
                var foot = cycle.Evaluate((float)i / Samples).LeftFoot;
                var sole = SolePoints(foot);
                if (foot.Contact == FootContact.Heel) AreClose(sole[0], foot.ContactPoint, 1e-5f, "heel");
                if (foot.Contact == FootContact.Ball) AreClose(sole[1], foot.ContactPoint, 1e-5f, "ball");
            }
        }

        [Test]
        public void FeetNeverGoBelowTheGround()
        {
            foreach (var request in Requests())
            {
                var cycle = new GaitCycle(new GaitProfile(), Body, request);
                for (int i = 0; i < Samples; i++)
                {
                    float phase = (float)i / Samples;
                    var pose = cycle.Evaluate(phase);
                    foreach (var side in new[] { Side.Left, Side.Right })
                    {
                        foreach (var point in SolePoints(pose.Foot(side)))
                        {
                            Assert.GreaterOrEqual(point.Y, -1e-4f, Describe(request) + " " + side + " at " + phase);
                        }
                    }
                }
            }
        }

        [Test]
        public void SwingJoinsStanceWithoutAJumpInSpeedOverTheGround()
        {
            foreach (var request in Requests())
            {
                var cycle = new GaitCycle(new GaitProfile(), Body, request);
                float swingSpeed = cycle.Timing.StrideLength / cycle.Timing.SwingDuration;
                foreach (float junction in new[] { 0f, cycle.Timing.DutyFactor })
                {
                    // The foot starts and stops briskly, so its speed changes
                    // fast near the junction, but it does not jump: looked at
                    // more closely, the change across the junction shrinks.
                    // A jump would stay the same size at every scale.
                    var coarse = VelocityChangeAcross(cycle, junction, 4e-4f);
                    var fine = VelocityChangeAcross(cycle, junction, 2.5e-5f);
                    string where = Describe(request) + " at phase " + junction;
                    Assert.Less(Horizontal(coarse), 0.2f * MathF.Max(swingSpeed, 1f), where);
                    Assert.Less(Horizontal(fine), 0.5f * Horizontal(coarse) + 0.02f, where);

                    // Vertically it does jump, by design: a foot comes down
                    // onto the ground and lifts off it at a definite speed,
                    // a fraction of the speed it swings at, not asymptotically.
                    Assert.Less(MathF.Abs(coarse.Y), 0.6f * MathF.Max(swingSpeed, 1f), where + " vertical");
                }
            }
        }

        /// <summary>Change in the left ankle's velocity, in m/s, between just before and just after a phase.</summary>
        private static Vector3 VelocityChangeAcross(GaitCycle cycle, float phase, float h)
        {
            var before = cycle.Evaluate(phase - h).LeftFoot.Ankle - cycle.Evaluate(phase - 2f * h).LeftFoot.Ankle;
            var after = cycle.Evaluate(phase + 2f * h).LeftFoot.Ankle - cycle.Evaluate(phase + h).LeftFoot.Ankle;
            return (after - before) / (h * cycle.Duration);
        }

        private static float Horizontal(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);

        [Test]
        public void LegsAreHalfACycleApartAndMirrorEachOther()
        {
            foreach (var request in Requests())
            {
                var cycle = new GaitCycle(new GaitProfile(), Body, request);
                // The mirror image of travelling right is travelling left.
                var mirrored = new GaitCycle(new GaitProfile(), Body,
                    new GaitRequest(request.Speed, new Vector2(-request.Direction.X, request.Direction.Y)));
                for (int i = 0; i < 50; i++)
                {
                    float phase = i / 50f;
                    var pose = cycle.Evaluate(phase);
                    var other = mirrored.Evaluate(phase + 0.5f);
                    string where = Describe(request) + " at " + phase;
                    AreClose(Mirror(pose.LeftFoot.Ankle), other.RightFoot.Ankle, 1e-4f, where + " ankle");
                    AreClose(Mirror(pose.LeftFoot.Rotation), other.RightFoot.Rotation, 1e-3f, where + " foot rotation");
                    AreClose(Mirror(pose.RightFoot.Ankle), other.LeftFoot.Ankle, 1e-4f, where + " other ankle");
                    AreClose(Mirror(pose.PelvisPosition), other.PelvisPosition, 1e-4f, where + " pelvis");
                    AreClose(Mirror(pose.PelvisRotation), other.PelvisRotation, 1e-3f, where + " pelvis rotation");
                    AreClose(Mirror(pose.ChestRotation), other.ChestRotation, 1e-3f, where + " chest");
                    Assert.AreEqual(pose.LeftArm.Swing, other.RightArm.Swing, 1e-4f, where + " arm swing");
                    Assert.AreEqual(pose.LeftFoot.Contact, other.RightFoot.Contact, where + " contact");
                }
            }
        }

        [Test]
        public void StrideCoversTheRequestedSpeed()
        {
            foreach (var request in Requests())
            {
                var cycle = new GaitCycle(new GaitProfile(), Body, request);
                var timing = cycle.Timing;
                Assert.AreEqual(request.Speed, timing.StrideLength / timing.CycleDuration, 1e-4f, Describe(request));

                // Measured rather than declared: follow the planted foot from
                // landing to lift-off. It must travel back by ground speed
                // times stance time, plus the length of sole it rolled along.
                float duty = timing.DutyFactor;
                var landed = cycle.Evaluate(1e-4f).LeftFoot;
                var leaving = cycle.Evaluate(duty - 1e-4f).LeftFoot;
                var mid = cycle.Evaluate(0.3f * duty).LeftFoot;
                var midLater = cycle.Evaluate(0.3f * duty + 0.01f).LeftFoot;
                Assert.AreEqual(FootContact.Flat, mid.Contact, Describe(request) + " is flat in mid-stance");
                var measuredVelocity = (midLater.Ankle - mid.Ankle) / (0.01f * timing.CycleDuration);
                AreClose(-cycle.RootVelocity, measuredVelocity, 1e-3f * MathF.Max(request.Speed, 1f), Describe(request));
                Assert.AreNotEqual(FootContact.None, landed.Contact, "foot is down just after phase 0");
                Assert.AreNotEqual(FootContact.None, leaving.Contact, "foot is down until the duty factor");
            }
        }

        [Test]
        public void WalkingHasDoubleSupportAndRunningHasFlight()
        {
            var walk = Cycle(0f, 1.4f);
            var run = Cycle(0f, 4f);
            Assert.AreEqual(GaitRegime.Walk, walk.Timing.Regime);
            Assert.AreEqual(GaitRegime.Run, run.Timing.Regime);
            Assert.Greater(walk.Timing.DoubleSupportDuration, 0.05f);
            Assert.Greater(run.Timing.FlightDuration, 0.05f);

            int walkBothDown = 0, walkBothUp = 0, runBothDown = 0, runBothUp = 0;
            for (int i = 0; i < Samples; i++)
            {
                Count(walk.Evaluate((float)i / Samples), ref walkBothDown, ref walkBothUp);
                Count(run.Evaluate((float)i / Samples), ref runBothDown, ref runBothUp);
            }

            Assert.Greater(walkBothDown, 0, "walking has double support");
            Assert.AreEqual(0, walkBothUp, "walking never leaves the ground");
            Assert.Greater(runBothUp, 0, "running has a flight phase");
            Assert.AreEqual(0, runBothDown, "running never has both feet down");
        }

        [Test]
        public void LegsAlwaysReachTheirFootTargets()
        {
            foreach (var request in Requests())
            {
                foreach (float weight in new[] { 0f, 1f, 2f })
                {
                    var cycle = new GaitCycle(new GaitProfile { Weight = weight }, Body, request);
                    for (int i = 0; i < Samples; i++)
                    {
                        var pose = cycle.Evaluate((float)i / Samples);
                        var frames = BipedFrames.Solve(pose, Body);
                        string where = Describe(request) + " weight " + weight + " at " + (float)i / Samples;
                        Assert.IsFalse(frames.LeftLeg.Clamped, where + " left");
                        Assert.IsFalse(frames.RightLeg.Clamped, where + " right");
                        AreClose(pose.LeftFoot.Ankle, frames.LeftLeg.Ankle, 1e-4f, where);
                        // Hips stay above the knees' reach of the ground and the pelvis above the feet.
                        Assert.Greater(pose.PelvisPosition.Y, 0.5f * Body.LegLength, where);
                    }
                }
            }
        }

        [Test]
        public void EveryHeadingMovesTheFeetAgainstTheTravel()
        {
            foreach (var request in Requests())
            {
                var cycle = new GaitCycle(new GaitProfile(), Body, request);
                Assert.AreEqual(request.Speed, cycle.RootVelocity.Length(), 1e-5f);
                var travel = Vector3.Normalize(cycle.RootVelocity);
                float duty = cycle.Timing.DutyFactor;

                // The foot lands ahead of where it leaves, along the travel direction.
                var landing = cycle.Evaluate(0f).LeftFoot.ContactPoint;
                var leaving = cycle.Evaluate(duty - 1e-4f).LeftFoot.ContactPoint;
                Assert.Greater(Vector3.Dot(landing - leaving, travel), 0.5f * cycle.Timing.StrideLength * duty,
                    Describe(request));

                // And each foot treads its own track: flat on the ground, the
                // two ankles ride two lines parallel to the travel, one on each side.
                var across = Vector3.Cross(Vector3.UnitY, travel);
                float leftTrack = Vector3.Dot(cycle.Evaluate(0.3f * duty).LeftFoot.Ankle, across);
                float rightTrack = Vector3.Dot(cycle.Evaluate(0.5f + 0.3f * duty).RightFoot.Ankle, across);
                Assert.Greater(MathF.Abs(leftTrack - rightTrack), 0.04f, Describe(request) + " tracks keep apart");
                Assert.AreEqual(-leftTrack, rightTrack, 1e-4f, Describe(request) + " tracks straddle the root");
                for (int i = 0; i < 100; i++)
                {
                    var pose = cycle.Evaluate(i / 100f);
                    if (pose.LeftFoot.Contact == FootContact.Flat)
                    {
                        Assert.AreEqual(leftTrack, Vector3.Dot(pose.LeftFoot.Ankle, across), 1e-4f,
                            Describe(request) + " left foot keeps its track");
                    }

                    if (pose.RightFoot.Contact == FootContact.Flat)
                    {
                        Assert.AreEqual(rightTrack, Vector3.Dot(pose.RightFoot.Ankle, across), 1e-4f,
                            Describe(request) + " right foot keeps its track");
                    }
                }
            }
        }

        [Test]
        public void HeadingChangesHowTheFootLandsAndWhereThePelvisTurns()
        {
            Assert.AreEqual(FootContact.Heel, Cycle(0f, 1.4f).Evaluate(0.01f).LeftFoot.Contact, "forward lands on the heel");
            Assert.AreEqual(FootContact.Ball, Cycle(0f, -1.4f).Evaluate(0.01f).LeftFoot.Contact, "backward lands on the ball");

            Assert.Greater(MeanPelvisYaw(Cycle(2f, 0f)), 20f * Rot.Deg, "strafing right turns the pelvis right");
            Assert.Less(MeanPelvisYaw(Cycle(-2f, 0f)), -20f * Rot.Deg, "strafing left turns the pelvis left");
            Assert.AreEqual(0f, MeanPelvisYaw(Cycle(0f, 2f)), 1e-3f, "forward keeps it square");
            Assert.AreEqual(0f, MeanPelvisYaw(Cycle(0f, -2f)), 1e-3f, "backward keeps it square");
            // Backing up to the right, the pelvis turns away from the travel, not toward it.
            Assert.Less(MeanPelvisYaw(Cycle(1.4f, -1.4f)), -10f * Rot.Deg);

            // Whatever the legs do, the chest and head keep facing forward on average.
            foreach (var request in Requests())
            {
                var cycle = new GaitCycle(new GaitProfile(), Body, request);
                var forward = Vector3.Zero;
                for (int i = 0; i < 40; i++) forward += Rot.Apply(cycle.Evaluate(i / 40f).ChestRotation, Vector3.UnitZ);
                Assert.AreEqual(0f, forward.X / 40f, 1e-3f, Describe(request) + " chest heading");
                Assert.AreEqual(0f, Rot.Apply(cycle.Evaluate(0.3f).HeadRotation, Vector3.UnitZ).X, 0.2f, "head heading");
            }
        }

        [Test]
        public void WeightDeepensTheDropAndBendsTheLoadedKnee()
        {
            float lastRange = -1f, lastYield = -1f;
            foreach (float weight in new[] { 0f, 1f, 2f })
            {
                var cycle = Cycle(0f, 1.4f, new GaitProfile { Weight = weight });
                float low = float.MaxValue, high = float.MinValue, yield = 0f;
                float duty = cycle.Timing.DutyFactor;
                for (int i = 0; i < Samples; i++)
                {
                    float phase = (float)i / Samples;
                    var pose = cycle.Evaluate(phase);
                    low = MathF.Min(low, pose.PelvisPosition.Y);
                    high = MathF.Max(high, pose.PelvisPosition.Y);
                    // Loading response: the first third of the left foot's stance.
                    if (phase < duty / 3f) yield = MathF.Max(yield, BipedFrames.Solve(pose, Body).LeftLeg.KneeFlexion);
                }

                Assert.Greater(high - low, lastRange, "pelvis travel grows with weight");
                Assert.Greater(yield, lastYield, "knee yield grows with weight");
                lastRange = high - low;
                lastYield = yield;
            }

            Assert.Greater(lastRange, 0.03f, "a heavy walk bobs by centimetres");
        }

        [Test]
        public void WalkingPelvisDropsIntoLoadingAndRisesOverMidStance()
        {
            var cycle = Cycle(0f, 1.4f);
            float duty = cycle.Timing.DutyFactor;
            float atStrike = cycle.Evaluate(0f).PelvisPosition.Y;
            float lowest = float.MaxValue, lowestPhase = 0f;
            for (int i = 0; i < Samples / 2; i++)
            {
                float y = cycle.Evaluate((float)i / Samples).PelvisPosition.Y;
                if (y < lowest)
                {
                    lowest = y;
                    lowestPhase = (float)i / Samples;
                }
            }

            float overFoot = cycle.Evaluate(0.5f * duty).PelvisPosition.Y;
            Assert.Greater(lowestPhase, 0f, "the low point comes after heel strike");
            Assert.Less(lowestPhase, 0.5f * duty, "and before mid-stance");
            Assert.Less(lowest, atStrike - 1e-4f, "the pelvis is still sinking as the heel lands");
            Assert.Greater(overFoot, lowest + 0.01f, "it rises over the planted foot");

            // The knee is nearly straight on landing and gives way afterwards.
            float kneeAtStrike = BipedFrames.Solve(cycle.Evaluate(0f), Body).LeftLeg.KneeFlexion;
            float kneeLoaded = BipedFrames.Solve(cycle.Evaluate(lowestPhase), Body).LeftLeg.KneeFlexion;
            Assert.Greater(kneeLoaded, kneeAtStrike + 3f * Rot.Deg, "the knee yields after heel strike");
        }

        [Test]
        public void RunningPelvisIsLowestOnTheGroundAndHighestInFlight()
        {
            var cycle = Cycle(0f, 4f);
            float duty = cycle.Timing.DutyFactor;
            float midStance = cycle.Evaluate(0.5f * duty).PelvisPosition.Y;
            float midFlight = cycle.Evaluate(0.5f * (duty + 0.5f)).PelvisPosition.Y;
            Assert.Greater(midFlight - midStance, 0.03f);
            Assert.Less(midFlight - midStance, 0.15f);
        }

        [Test]
        public void UpperBodyLagsThePelvis()
        {
            float lag = 0.1f;
            var profile = new GaitProfile { UpperBodyLag = lag };
            var cycle = Cycle(0f, 1.4f, profile);
            // The pelvis is turned furthest right as the left foot lands; the
            // chest reaches its own extreme (the other way) that much later.
            float best = 0f, bestPhase = 0f;
            for (int i = 0; i < Samples; i++)
            {
                float phase = (float)i / Samples - 0.25f;
                float yaw = -Yaw(cycle.Evaluate(phase).ChestRotation);
                if (yaw > best)
                {
                    best = yaw;
                    bestPhase = phase;
                }
            }

            Assert.AreEqual(lag, bestPhase, 0.01f);
            Assert.Greater(best, 1f * Rot.Deg);
        }

        [Test]
        public void FasterGaitsLeanFurtherAndSwingTheArmsMore()
        {
            float lastLean = -1f, lastSwing = -1f;
            foreach (float speed in new[] { 0.7f, 1.4f, 2f, 4f, 6f })
            {
                var cycle = Cycle(0f, speed);
                float lean = Rot.Apply(cycle.Evaluate(0.2f).ChestRotation, Vector3.UnitY).Z;
                float swing = 0f;
                for (int i = 0; i < 100; i++) swing = MathF.Max(swing, cycle.Evaluate(i / 100f).LeftArm.Swing);
                Assert.Greater(lean, lastLean, "lean at " + speed);
                Assert.Greater(swing, lastSwing, "arm swing at " + speed);
                lastLean = lean;
                lastSwing = swing;
            }

            // Backing up leans back, less far.
            float back = Rot.Apply(Cycle(0f, -2f).Evaluate(0.2f).ChestRotation, Vector3.UnitY).Z;
            Assert.Less(back, 0f);
        }

        [Test]
        public void ArmsSwingAgainstTheLegs()
        {
            var pose = Cycle(0f, 1.4f, new GaitProfile { UpperBodyLag = 0f }).Evaluate(0f);
            Assert.Greater(pose.LeftFoot.Ankle.Z, pose.RightFoot.Ankle.Z, "left foot leads at phase 0");
            Assert.Less(pose.LeftArm.Swing, 0f, "so the left arm is back");
            Assert.Greater(pose.RightArm.Swing, 0f, "and the right arm forward");
        }

        [Test]
        public void TrackedUpperBodyKeepsTheLegsAndStillsTheTorso()
        {
            var full = Cycle(0f, 2f);
            var tracked = Cycle(0f, 2f, new GaitProfile().ForTrackedUpperBody());
            for (int i = 0; i < 20; i++)
            {
                float phase = i / 20f;
                var a = full.Evaluate(phase);
                var b = tracked.Evaluate(phase);
                AreClose(a.LeftFoot.Ankle, b.LeftFoot.Ankle, 1e-5f, "feet are unchanged");
                AreClose(Quaternion.Identity, b.ChestRotation, 0.02f, "chest stays square");
                Assert.AreEqual(0f, b.LeftArm.Swing, 1e-6f, "arms do not swing");
            }
        }

        private static void Count(GaitPose pose, ref int bothDown, ref int bothUp)
        {
            bool left = pose.LeftFoot.Contact != FootContact.None;
            bool right = pose.RightFoot.Contact != FootContact.None;
            if (left && right) bothDown++;
            if (!left && !right) bothUp++;
        }

        private static float Yaw(Quaternion rotation)
        {
            var forward = Rot.Apply(rotation, Vector3.UnitZ);
            return MathF.Atan2(forward.X, forward.Z);
        }

        private static float MeanPelvisYaw(GaitCycle cycle)
        {
            float sum = 0f;
            for (int i = 0; i < 40; i++) sum += Yaw(cycle.Evaluate(i / 40f).PelvisRotation);
            return sum / 40f;
        }

        private static void AssertSamePose(GaitPose a, GaitPose b, float distance, float angle, string message)
        {
            AreClose(a.PelvisPosition, b.PelvisPosition, distance, message + " pelvis");
            AreClose(a.PelvisRotation, b.PelvisRotation, angle, message + " pelvis rotation");
            AreClose(a.LeftFoot.Ankle, b.LeftFoot.Ankle, distance, message + " left ankle");
            AreClose(a.RightFoot.Ankle, b.RightFoot.Ankle, distance, message + " right ankle");
            AreClose(a.LeftFoot.Rotation, b.LeftFoot.Rotation, angle, message + " left foot");
            AreClose(a.RightFoot.Rotation, b.RightFoot.Rotation, angle, message + " right foot");
            AreClose(a.ChestRotation, b.ChestRotation, angle, message + " chest");
            AreClose(a.HeadRotation, b.HeadRotation, angle, message + " head");
            Assert.AreEqual(a.LeftArm.Swing, b.LeftArm.Swing, angle, message + " left arm");
            Assert.AreEqual(a.RightArm.Swing, b.RightArm.Swing, angle, message + " right arm");
            Assert.AreEqual(a.LeftFoot.ToeBend, b.LeftFoot.ToeBend, 3f * angle, message + " left toes");
        }
    }
}
