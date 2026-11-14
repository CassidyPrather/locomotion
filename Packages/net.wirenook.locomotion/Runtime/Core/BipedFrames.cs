using System;
using System.Numerics;

namespace Wirenook.Locomotion
{
    /// <summary>One leg, solved: joint positions and segment orientations in root space.</summary>
    public struct LegFrames
    {
        public Vector3 Hip;
        public Vector3 Knee;
        public Vector3 Ankle;

        /// <summary>Thigh and shin by <see cref="TwoBoneIkResult"/>'s convention: identity hangs straight down, knee forward.</summary>
        public Quaternion Thigh;

        public Quaternion Shin;

        /// <summary>Identity is a flat foot pointing forward.</summary>
        public Quaternion Foot;

        public Quaternion Toes;

        /// <summary>Knee flexion in radians; zero is a straight leg.</summary>
        public float KneeFlexion;

        /// <summary>True when the foot target was out of the leg's reach.</summary>
        public bool Clamped;
    }

    /// <summary>One arm's segment orientations in root space; identity hangs straight down, elbow bending forward.</summary>
    public struct ArmFrames
    {
        public Quaternion UpperArm;
        public Quaternion Forearm;
    }

    /// <summary>
    /// A <see cref="GaitPose"/> turned into an orientation for every body
    /// segment, which is what a skeleton needs. Everything is in root space
    /// and relative to a neutral standing figure, so a rig only has to know
    /// how its own bones sit in that neutral pose.
    /// </summary>
    public struct BipedFrames
    {
        /// <summary>Midpoint between the hip joints.</summary>
        public Vector3 PelvisPosition;

        public Quaternion Pelvis;
        public Quaternion Chest;
        public Quaternion Head;
        public LegFrames LeftLeg;
        public LegFrames RightLeg;
        public ArmFrames LeftArm;
        public ArmFrames RightArm;

        public LegFrames Leg(Side side) => side == Side.Left ? LeftLeg : RightLeg;

        public ArmFrames Arm(Side side) => side == Side.Left ? LeftArm : RightArm;

        /// <summary>Orientation of a point along the spine: 0 at the pelvis, 1 at the chest.</summary>
        public Quaternion Spine(float t) => Quaternion.Slerp(Pelvis, Chest, t);

        /// <summary>Orientation of the neck, halfway between chest and head.</summary>
        public Quaternion Neck => Quaternion.Slerp(Chest, Head, 0.5f);

        /// <summary>Solves with the hip joints where the pose's pelvis and the body's hip width put them.</summary>
        public static BipedFrames Solve(GaitPose pose, BodyProportions body)
        {
            return Solve(pose, body, pose.HipJoint(Side.Left, body.HipWidth), pose.HipJoint(Side.Right, body.HipWidth));
        }

        /// <summary>
        /// Solves with the hip joints at given positions, for a skeleton whose
        /// hips are not exactly where the abstract biped has them.
        /// </summary>
        public static BipedFrames Solve(GaitPose pose, BodyProportions body, Vector3 leftHip, Vector3 rightHip)
        {
            // Arms hang from the shoulders under gravity: they follow the way
            // the chest faces but not its lean.
            var chestForward = Rot.Apply(pose.ChestRotation, Vector3.UnitZ);
            var shoulders = Rot.Yaw(MathF.Atan2(chestForward.X, chestForward.Z));

            return new BipedFrames
            {
                PelvisPosition = pose.PelvisPosition,
                Pelvis = pose.PelvisRotation,
                Chest = pose.ChestRotation,
                Head = pose.HeadRotation,
                LeftLeg = SolveLeg(pose.LeftFoot, body, leftHip),
                RightLeg = SolveLeg(pose.RightFoot, body, rightHip),
                LeftArm = SolveArm(shoulders, pose.LeftArm, Side.Left),
                RightArm = SolveArm(shoulders, pose.RightArm, Side.Right),
            };
        }

        private static LegFrames SolveLeg(FootPose foot, BodyProportions body, Vector3 hip)
        {
            var solved = TwoBoneIk.Solve(hip, foot.Ankle, foot.KneeDirection, body.ThighLength, body.ShinLength);
            return new LegFrames
            {
                Hip = hip,
                Knee = solved.Middle,
                Ankle = solved.End,
                Thigh = solved.UpperRotation,
                Shin = solved.LowerRotation,
                Foot = foot.Rotation,
                Toes = foot.Rotation * Rot.Pitch(foot.ToeBend),
                KneeFlexion = solved.Flexion,
                Clamped = solved.Clamped,
            };
        }

        private static ArmFrames SolveArm(Quaternion shoulders, ArmPose arm, Side side)
        {
            // Out from the body first, then swung about the shoulder line;
            // a negative pitch carries the hand forward.
            var upper = shoulders * Rot.Pitch(-arm.Swing) * Rot.Roll((int)side * arm.Spread);
            return new ArmFrames
            {
                UpperArm = upper,
                Forearm = upper * Rot.Pitch(-arm.ElbowBend),
            };
        }
    }
}
