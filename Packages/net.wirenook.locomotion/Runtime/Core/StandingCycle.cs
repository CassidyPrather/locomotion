using System;
using System.Numerics;

namespace Wirenook.Locomotion
{
    /// <summary>
    /// Standing still, which is not the same as not moving: over one loop the
    /// weight drifts onto one foot and back onto the other, the unloaded hip
    /// sags a little, and the chest rises and falls with two breaths.
    /// </summary>
    public sealed class StandingCycle : IPoseCycle
    {
        private const float Tau = 2f * MathF.PI;
        private const int BreathsPerLoop = 2;

        private readonly GaitProfile _profile;
        private readonly BodyProportions _body;
        private readonly float _height;

        public StandingCycle(GaitProfile profile, BodyProportions body)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (!body.IsValid) throw new ArgumentException("Body proportions must all be positive.", nameof(body));
            _profile = profile;
            _body = body;

            // Shifting sideways asks one leg to reach further. Ride at the
            // height the most stretched instant allows, so the pelvis path
            // stays smooth instead of tracing the legs' limit.
            float height = float.MaxValue;
            const int samples = 32;
            for (int i = 0; i < samples; i++)
            {
                var pose = Shifted((float)i / samples, out _);
                height = MathF.Min(height, LegReach.MaxPelvisHeight(body, pose.PelvisPosition, pose.PelvisRotation,
                    pose.LeftFoot.Ankle, pose.RightFoot.Ankle));
            }

            _height = height - profile.Crouch * profile.Weight * body.LegLength;
        }

        public float Duration => _profile.IdlePeriod;

        public Vector3 RootVelocity => Vector3.Zero;

        public GaitPose Evaluate(float phase)
        {
            phase = Shaping.Wrap01(phase);
            var pose = Shifted(phase, out float shift);
            // Settling onto one leg lets the body down a few millimetres.
            pose.PelvisPosition.Y = _height - 0.003f * _profile.Weight * shift * shift;
            return pose;
        }

        /// <summary>The pose at a phase with the pelvis height left at zero.</summary>
        private GaitPose Shifted(float phase, out float shift)
        {
            float weight = _profile.Weight;

            // -1 fully on the left foot, +1 on the right.
            shift = Shaping.Dwell(MathF.Sin(Tau * phase));
            float breath = MathF.Sin(Tau * BreathsPerLoop * phase);

            var pose = Posture.Stand(_profile, _body, 0f, 0f);
            pose.PelvisPosition = new Vector3(shift * _profile.IdleSway * _body.HipWidth * weight, 0f, 0f);
            // The hip over the loaded leg rides high and the other sags.
            float list = shift * 0.35f * _profile.PelvisList * Rot.Deg * weight;
            pose.PelvisRotation = Rot.Roll(list);

            // The chest stays nearly level over the tilted pelvis and opens on the in-breath.
            float breathPitch = -_profile.BreathDepth * Rot.Deg * breath;
            pose.ChestRotation = Rot.Euler(0f, breathPitch, -0.3f * list);
            pose.HeadRotation = Rot.Pitch(-0.3f * breathPitch);

            // Breathing in lifts the shoulders, which carries the arms out a touch.
            float spread = _profile.ArmSpread * Rot.Deg + 0.3f * _profile.BreathDepth * Rot.Deg * breath;
            pose.LeftArm.Spread = spread;
            pose.RightArm.Spread = spread;
            return pose;
        }
    }

    /// <summary>Static standing poses: the neutral stance, and the same stance leaning.</summary>
    public static class Posture
    {
        /// <summary>
        /// A balanced stand, leaning by the given angles in radians (positive
        /// forward, positive to the right). The lean starts at the ankles:
        /// the pelvis moves over the toes and tips a little, the chest tips
        /// the full angle, and the head holds back most of it so the gaze
        /// stays level. Feet do not move.
        /// </summary>
        public static GaitPose Stand(GaitProfile profile, BodyProportions body, float leanForward, float leanRight)
        {
            float halfStance = MathF.Max(profile.StepWidth, 0.6f) * body.HipWidth * 0.5f;
            var pose = new GaitPose
            {
                LeftFoot = PlantedFoot(profile, body, Side.Left, halfStance),
                RightFoot = PlantedFoot(profile, body, Side.Right, halfStance),
            };

            // Half the lean comes from the ankles, carrying the pelvis with it.
            float standingHeight = body.AnkleHeight + body.LegLength;
            var pelvis = new Vector3(
                standingHeight * MathF.Sin(0.5f * leanRight), 0f, standingHeight * MathF.Sin(0.5f * leanForward));
            pose.PelvisRotation = Rot.Euler(0f, 0.4f * leanForward, -0.4f * leanRight);
            pelvis.Y = LegReach.MaxPelvisHeight(body, pelvis, pose.PelvisRotation, pose.LeftFoot.Ankle,
                           pose.RightFoot.Ankle)
                       - profile.Crouch * profile.Weight * body.LegLength;
            pose.PelvisPosition = pelvis;

            pose.ChestRotation = Rot.Euler(0f, leanForward, -leanRight);
            pose.HeadRotation = Rot.Euler(0f, 0.3f * leanForward, -0.3f * leanRight);

            var arm = new ArmPose { Swing = 0f, Spread = profile.ArmSpread * Rot.Deg, ElbowBend = 8f * Rot.Deg };
            pose.LeftArm = arm;
            pose.RightArm = arm;
            return pose;
        }

        private static FootPose PlantedFoot(GaitProfile profile, BodyProportions body, Side side, float halfStance)
        {
            var heading = Rot.Yaw((int)side * profile.ToeOut * Rot.Deg);
            var ankle = new Vector3((int)side * halfStance, body.AnkleHeight, 0f);
            return new FootPose
            {
                Ankle = ankle,
                Rotation = heading,
                ToeBend = 0f,
                Contact = FootContact.Flat,
                ContactPoint = new Vector3(ankle.X, 0f, ankle.Z),
                KneeDirection = Rot.Apply(heading, Vector3.UnitZ),
            };
        }
    }
}
