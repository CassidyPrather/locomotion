using System.Collections.Generic;

namespace Wirenook.Locomotion
{
    /// <summary>A way of walking with a name, for choosing between.</summary>
    public sealed class GaitStyle
    {
        public string Name { get; }

        /// <summary>One line on what it looks like, for whoever is choosing.</summary>
        public string About { get; }

        public GaitProfile Profile { get; }

        public GaitStyle(string name, string about, GaitProfile profile)
        {
            Name = name;
            About = about;
            Profile = profile;
        }
    }

    /// <summary>
    /// Named starting points for a <see cref="GaitProfile"/>. Each is the
    /// default with a handful of numbers changed, and every one stays inside
    /// the ranges the profile documents. They exist to be tried side by side
    /// on an avatar and then tuned; the first is the profile's own defaults.
    /// </summary>
    public static class GaitStyles
    {
        public static IReadOnlyList<GaitStyle> All { get; } = new[]
        {
            new GaitStyle("Weighty", "The default: the body sinks into each step and the upper body trails it.",
                new GaitProfile()),

            new GaitStyle("Light", "Little weight in it: quick, upright, the feet come off the ground easily.",
                new GaitProfile
                {
                    Weight = 0.6f,
                    LoadingDrop = 0.012f,
                    UpperBodyLag = 0.03f,
                    PelvisSway = 0.18f,
                    StepHeight = 0.075f,
                    RunBounce = 1.1f,
                    ArmSwing = 26f,
                    ForwardLean = 4f,
                    InertiaTime = 0.2f,
                    AccelerationLean = 1.8f,
                    LandingGive = 0.09f,
                }),

            new GaitStyle("Sway", "From the hips: feet nearly in one line, the pelvis swinging and the shoulders answering.",
                new GaitProfile
                {
                    Weight = 0.9f,
                    PelvisSway = 0.4f,
                    PelvisList = 7f,
                    PelvisYaw = 8.5f,
                    StepWidth = 0.25f,
                    ToeOut = 3f,
                    StrideScale = 0.95f,
                    SpineCounterRotation = 1f,
                    ArmSwing = 18f,
                    ArmSpread = 13f,
                    IdleSway = 0.2f,
                }),

            new GaitStyle("Bouncy", "Springs off every step: high feet, a deep give at the knee, a run that leaves the ground.",
                new GaitProfile
                {
                    Weight = 0.75f,
                    LoadingDrop = 0.035f,
                    LoadingDelay = 0.12f,
                    StepHeight = 0.11f,
                    RunBounce = 1.35f,
                    ToeOffAngle = 68f,
                    StrideScale = 0.92f,
                    ForwardLean = 3.5f,
                    ArmSwing = 30f,
                    ArmSpread = 11f,
                    BreathDepth = 2f,
                    AirReach = 40f,
                    LandingGive = 0.15f,
                }),

            new GaitStyle("Swagger", "Takes its time and its room: long strides on a wide stance, toes out, arms held off the body.",
                new GaitProfile
                {
                    Weight = 1.35f,
                    StrideScale = 1.1f,
                    StepWidth = 1f,
                    ToeOut = 13f,
                    PelvisYaw = 7.5f,
                    PelvisSway = 0.32f,
                    UpperBodyLag = 0.08f,
                    SpineCounterRotation = 0.9f,
                    HeelStrikeAngle = 22f,
                    ArmSwing = 17f,
                    ArmSpread = 15f,
                    InertiaTime = 0.4f,
                }),

            new GaitStyle("Dainty", "Small neat steps on a narrow track, feet kept low, elbows in and hands out.",
                new GaitProfile
                {
                    Weight = 0.7f,
                    StrideScale = 0.84f,
                    StepWidth = 0.3f,
                    ToeOut = 2f,
                    StepHeight = 0.05f,
                    HeelStrikeAngle = 10f,
                    ToeOffAngle = 50f,
                    PelvisList = 5f,
                    PelvisSway = 0.2f,
                    PelvisYaw = 4f,
                    ForwardLean = 3f,
                    ArmSwing = 13f,
                    ArmSpread = 15f,
                    RunElbowBend = 95f,
                    LandingGive = 0.1f,
                }),
        };
    }
}
