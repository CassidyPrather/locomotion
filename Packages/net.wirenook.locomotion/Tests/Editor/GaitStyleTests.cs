using System.Linq;
using NUnit.Framework;
using static Wirenook.Locomotion.Tests.GaitTestSupport;

namespace Wirenook.Locomotion.Tests
{
    public class GaitStyleTests
    {
        private const int Samples = 96;

        [Test]
        public void StylesAreNamedApartAndTheFirstIsTheDefault()
        {
            Assert.GreaterOrEqual(GaitStyles.All.Count, 2, "a choice needs at least two");
            Assert.AreEqual(GaitStyles.All.Count, GaitStyles.All.Select(style => style.Name).Distinct().Count(), "names are unique");
            foreach (var style in GaitStyles.All)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(style.Name));
                Assert.IsFalse(string.IsNullOrWhiteSpace(style.About), style.Name + " says what it is");
            }

            var first = GaitStyles.All[0].Profile;
            var plain = new GaitProfile();
            foreach (var field in typeof(GaitProfile).GetFields())
            {
                Assert.AreEqual(field.GetValue(plain), field.GetValue(first), field.Name + " of the first style is the default");
            }
        }

        [Test]
        public void EveryStyleStaysInsideTheRangesTheProfileDocuments()
        {
            // (field, lowest, highest), as the summaries in GaitProfile state them.
            var ranges = new (string field, float low, float high)[]
            {
                ("Weight", 0f, 2f), ("LoadingDrop", 0f, 0.05f), ("LoadingDelay", 0.05f, 0.3f), ("Crouch", 0f, 0.1f),
                ("UpperBodyLag", 0f, 0.15f), ("PelvisSway", 0f, 0.5f), ("PelvisList", 0f, 10f), ("PelvisYaw", 0f, 12f),
                ("Surge", 0f, 0.03f), ("RunBounce", 0.5f, 1.5f), ("StrideScale", 0.7f, 1.3f), ("WalkRunTransition", 0.6f, 1f),
                ("LateralStride", 0.4f, 1f), ("BackwardStride", 0.5f, 1f), ("HipTurn", 0f, 0.8f), ("StepWidth", 0f, 1.5f),
                ("StepHeight", 0.02f, 0.2f), ("ToeOut", -5f, 20f), ("HeelStrikeAngle", 0f, 30f), ("ToeOffAngle", 20f, 70f),
                ("SpineCounterRotation", 0f, 1.5f), ("ForwardLean", 0f, 12f), ("ArmSwing", 0f, 40f), ("ArmSpread", 0f, 25f),
                ("RunElbowBend", 30f, 110f), ("CrouchDepth", 0.2f, 0.5f), ("CrouchLean", 0f, 45f), ("AirReach", 0f, 60f),
                ("LandingGive", 0.05f, 0.2f), ("IdlePeriod", 4f, 16f), ("IdleSway", 0f, 0.4f), ("BreathDepth", 0f, 4f),
                ("InertiaTime", 0.1f, 0.8f), ("AccelerationLean", 0f, 5f), ("MaxLean", 0f, 20f), ("TurnBank", 0f, 15f),
                ("TurnBankRate", 60f, 360f), ("TurnBankSpeed", 1f, 8f),
            };
            Assert.AreEqual(typeof(GaitProfile).GetFields().Length, ranges.Length, "every field of the profile has a range here");
            foreach (var style in GaitStyles.All)
            {
                foreach (var (name, low, high) in ranges)
                {
                    float value = (float)typeof(GaitProfile).GetField(name).GetValue(style.Profile);
                    Assert.GreaterOrEqual(value, low, style.Name + "." + name);
                    Assert.LessOrEqual(value, high, style.Name + "." + name);
                }
            }
        }

        [Test]
        public void EveryStyleWalksAndRunsWithItsFeetOnTheGroundAndInReach()
        {
            foreach (var style in GaitStyles.All)
            {
                foreach (var profile in new[] { style.Profile, style.Profile.ForTrackedUpperBody() })
                {
                    foreach (var request in Requests())
                    {
                        foreach (bool crouched in new[] { false, true })
                        {
                            // Nobody sprints in a crouch.
                            if (crouched && request.Speed > 2f) continue;
                            var cycle = new GaitCycle(profile, Body, request, crouched);
                            string what = style.Name + (crouched ? " crouched " : " ") + Describe(request);
                            AreClose(cycle.Evaluate(0f).PelvisPosition, cycle.Evaluate(1f).PelvisPosition, 1e-5f, what + " loops");
                            for (int i = 0; i < Samples; i++)
                            {
                                float phase = (float)i / Samples;
                                var pose = cycle.Evaluate(phase);
                                var frames = BipedFrames.Solve(pose, Body);
                                Assert.IsFalse(frames.LeftLeg.Clamped || frames.RightLeg.Clamped, what + ": legs reach at " + phase);
                                foreach (var side in new[] { Side.Left, Side.Right })
                                {
                                    foreach (var point in SolePoints(pose.Foot(side)))
                                    {
                                        Assert.GreaterOrEqual(point.Y, -1e-4f, what + " " + side + " at " + phase);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        [Test]
        public void EveryStyleStandsAndIdlesInReach()
        {
            foreach (var style in GaitStyles.All)
            {
                foreach (bool crouched in new[] { false, true })
                {
                    var idle = new StandingCycle(style.Profile, Body, crouched);
                    for (int i = 0; i < Samples; i++)
                    {
                        var frames = BipedFrames.Solve(idle.Evaluate((float)i / Samples), Body);
                        Assert.IsFalse(frames.LeftLeg.Clamped || frames.RightLeg.Clamped,
                            style.Name + (crouched ? " crouched" : "") + " idle: legs reach at " + i);
                    }
                }

                foreach (bool hard in new[] { false, true })
                {
                    var landing = new LandingCycle(style.Profile, Body, hard);
                    for (int i = 0; i <= Samples; i++)
                    {
                        var pose = landing.Evaluate((float)i / Samples);
                        var frames = BipedFrames.Solve(pose, Body);
                        Assert.IsFalse(frames.LeftLeg.Clamped || frames.RightLeg.Clamped, style.Name + " landing: legs reach at " + i);
                        Assert.Greater(pose.PelvisPosition.Y, 0.4f * Body.LegLength, style.Name + " landing does not sit on the floor");
                    }
                }
            }
        }

        [Test]
        public void StylesDifferWhereTheySayTheyDo()
        {
            GaitProfile Named(string name) => GaitStyles.All.First(style => style.Name == name).Profile;

            float Width(GaitProfile profile)
            {
                float widest = 0f;
                var cycle = Cycle(0f, 2f, profile);
                for (int i = 0; i < Samples; i++)
                {
                    var at = cycle.Evaluate((float)i / Samples);
                    widest = System.MathF.Max(widest, at.RightFoot.Ankle.X - at.LeftFoot.Ankle.X);
                }

                return widest;
            }

            float PelvisTravel(GaitProfile profile)
            {
                var cycle = Cycle(0f, 1.4f, profile);
                float left = float.MaxValue, right = float.MinValue;
                for (int i = 0; i < Samples; i++)
                {
                    float x = cycle.Evaluate((float)i / Samples).PelvisPosition.X;
                    left = System.MathF.Min(left, x);
                    right = System.MathF.Max(right, x);
                }

                return right - left;
            }

            float Lift(GaitProfile profile)
            {
                var cycle = Cycle(0f, 2f, profile);
                float highest = 0f;
                for (int i = 0; i < Samples; i++)
                {
                    highest = System.MathF.Max(highest, cycle.Evaluate((float)i / Samples).LeftFoot.Ankle.Y);
                }

                return highest;
            }

            var plain = new GaitProfile();
            Assert.Greater(Width(Named("Swagger")), 1.3f * Width(plain), "swagger walks wide");
            Assert.Less(Width(Named("Sway")), 0.7f * Width(plain), "sway walks a line");
            Assert.Greater(PelvisTravel(Named("Sway")), 1.2f * PelvisTravel(plain), "and swings the hips over it");
            Assert.Greater(Lift(Named("Bouncy")), Lift(plain) + 0.02f, "bouncy lifts its feet");
            Assert.Less(Cycle(0f, 2f, Named("Dainty")).Timing.StrideLength, 0.9f * Cycle(0f, 2f, plain).Timing.StrideLength,
                "dainty takes small steps");
            Assert.Greater(Cycle(0f, 2f, Named("Swagger")).Timing.StrideLength, 1.05f * Cycle(0f, 2f, plain).Timing.StrideLength,
                "swagger takes long ones");
        }
    }
}
