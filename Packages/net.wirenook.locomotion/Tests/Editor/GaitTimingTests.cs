using System;
using System.Numerics;
using NUnit.Framework;
using static Wirenook.Locomotion.Tests.GaitTestSupport;

namespace Wirenook.Locomotion.Tests
{
    public class GaitTimingTests
    {
        private static GaitTiming Forward(float speed, GaitProfile profile = null)
        {
            return GaitTiming.Resolve(profile ?? new GaitProfile(), Body, new GaitRequest(speed, Vector2.UnitY));
        }

        [Test]
        public void SpeedChoosesTheRegime()
        {
            Assert.AreEqual(GaitRegime.Walk, Forward(1.4f).Regime);
            Assert.AreEqual(GaitRegime.Walk, Forward(2f).Regime, "VRChat's default walk speed is a walk for an adult");
            Assert.AreEqual(GaitRegime.Run, Forward(4f).Regime, "and its run speed a run");
            Assert.Greater(Forward(2f).DutyFactor, 0.5f);
            Assert.Less(Forward(4f).DutyFactor, 0.5f);

            // The switch is in body-relative speed: short legs break into a run sooner.
            var small = new BodyProportions(0.3f, 0.5f, 0.07f, 0.09f, 0.03f);
            var timing = GaitTiming.Resolve(new GaitProfile(), small, new GaitRequest(2f, Vector2.UnitY));
            Assert.AreEqual(GaitRegime.Run, timing.Regime);
        }

        [Test]
        public void StrideAndCadenceMatchTypicalAdults()
        {
            var walk = Forward(1.4f);
            Assert.AreEqual(1.5f, walk.StrideLength, 0.15f, "comfortable walking stride, metres");
            Assert.AreEqual(112f, 120f / walk.CycleDuration, 12f, "comfortable walking cadence, steps a minute");

            var run = Forward(4f);
            Assert.AreEqual(170f, 120f / run.CycleDuration, 15f, "running cadence, steps a minute");
        }

        [Test]
        public void StrideGrowsSlowerThanSpeedWhenWalkingAndCadenceMakesUpTheRest()
        {
            var slow = Forward(0.7f);
            var brisk = Forward(1.4f);
            float strideRatio = brisk.StrideLength / slow.StrideLength;
            float cadenceRatio = slow.CycleDuration / brisk.CycleDuration;
            Assert.Greater(strideRatio, 1.2f);
            Assert.Less(strideRatio, 1.7f, "doubling speed does not double the stride");
            Assert.Greater(cadenceRatio, 1.2f, "cadence rises too");
            Assert.AreEqual(2f, strideRatio * cadenceRatio, 1e-3f);
        }

        [Test]
        public void StrideOverDurationIsAlwaysTheSpeed()
        {
            foreach (var request in Requests())
            {
                var timing = GaitTiming.Resolve(new GaitProfile(), Body, request);
                Assert.AreEqual(request.Speed, timing.StrideLength / timing.CycleDuration, 1e-4f, Describe(request));
                Assert.AreEqual(timing.CycleDuration, timing.StanceDuration + timing.SwingDuration, 1e-5f);
            }
        }

        [Test]
        public void StandstillHasAFiniteCycleAndNoStride()
        {
            var timing = Forward(0f);
            Assert.AreEqual(0f, timing.StrideLength, 1e-6f);
            Assert.Greater(timing.CycleDuration, 0.5f);
            Assert.Less(timing.CycleDuration, 5f);
        }

        [Test]
        public void SidewaysAndBackwardStepsAreShorterAndQuicker()
        {
            var profile = new GaitProfile();
            var forward = GaitTiming.Resolve(profile, Body, new GaitRequest(1.4f, Vector2.UnitY));
            var backward = GaitTiming.Resolve(profile, Body, new GaitRequest(1.4f, -Vector2.UnitY));
            var sideways = GaitTiming.Resolve(profile, Body, new GaitRequest(1.4f, Vector2.UnitX));
            Assert.AreEqual(profile.BackwardStride, backward.StrideLength / forward.StrideLength, 1e-3f);
            Assert.AreEqual(profile.LateralStride, sideways.StrideLength / forward.StrideLength, 1e-3f);
            Assert.Less(sideways.CycleDuration, forward.CycleDuration);
        }

        [Test]
        public void WeightLengthensGroundContact()
        {
            Assert.Greater(Forward(1.4f, new GaitProfile { Weight = 2f }).DutyFactor, Forward(1.4f).DutyFactor);
            Assert.Greater(Forward(4f, new GaitProfile { Weight = 2f }).DutyFactor, Forward(4f).DutyFactor);
            Assert.Less(Forward(4f, new GaitProfile { Weight = 2f }).DutyFactor, 0.5f, "a heavy run still has flight");
        }

        [Test]
        public void RequestNormalisesItsDirection()
        {
            var request = GaitRequest.FromVelocity(3f, 4f);
            Assert.AreEqual(5f, request.Speed, 1e-5f);
            Assert.AreEqual(1f, request.Direction.Length(), 1e-5f);
            AreClose(new Vector3(3f, 0f, 4f), request.Velocity, 1e-5f, "velocity");
            Assert.AreEqual(MathF.PI / 2f, new GaitRequest(1f, Vector2.UnitX).Heading, 1e-5f, "right is a positive heading");
            Assert.AreEqual(Vector2.UnitY, new GaitRequest(0f, Vector2.Zero).Direction, "no direction means forward");
        }
    }
}
