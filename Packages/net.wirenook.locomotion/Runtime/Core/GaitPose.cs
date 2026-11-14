using System.Numerics;

namespace Wirenook.Locomotion
{
    public enum Side
    {
        Left = -1,
        Right = 1,
    }

    /// <summary>Which part of a foot is holding the ground.</summary>
    public enum FootContact
    {
        /// <summary>In the air.</summary>
        None,

        /// <summary>Rolling on the heel, toes raised.</summary>
        Heel,

        /// <summary>Whole sole down.</summary>
        Flat,

        /// <summary>Rolling on the ball of the foot, heel raised.</summary>
        Ball,
    }

    /// <summary>One foot's target in root space.</summary>
    public struct FootPose
    {
        /// <summary>Ankle joint position, metres.</summary>
        public Vector3 Ankle;

        /// <summary>Foot rotation; identity is a flat foot pointing forward.</summary>
        public Quaternion Rotation;

        /// <summary>Toe joint angle relative to the foot, radians; negative bends the toes up.</summary>
        public float ToeBend;

        public FootContact Contact;

        /// <summary>
        /// The point of the sole the foot is rolling on, in root space; it is
        /// the point that must not slide. Meaningless while the foot is in the air.
        /// </summary>
        public Vector3 ContactPoint;

        /// <summary>Horizontal direction the knee should point, in root space.</summary>
        public Vector3 KneeDirection;
    }

    /// <summary>One arm's target relative to the chest, radians.</summary>
    public struct ArmPose
    {
        /// <summary>Shoulder swing from hanging straight down; positive is forward.</summary>
        public float Swing;

        /// <summary>Angle the arm is held out from the body.</summary>
        public float Spread;

        /// <summary>Elbow flexion from straight.</summary>
        public float ElbowBend;
    }

    /// <summary>
    /// A whole-body target for an abstract biped at one instant, in root
    /// space: the root sits on the ground under the body, faces +Z and moves
    /// at the requested velocity, so a planted foot travels backward through
    /// this space at exactly that velocity.
    /// </summary>
    public struct GaitPose
    {
        /// <summary>Midpoint between the two hip joints, metres.</summary>
        public Vector3 PelvisPosition;

        public Quaternion PelvisRotation;

        public FootPose LeftFoot;

        public FootPose RightFoot;

        /// <summary>Orientation of the top of the spine, in root space.</summary>
        public Quaternion ChestRotation;

        /// <summary>Orientation of the head, in root space.</summary>
        public Quaternion HeadRotation;

        public ArmPose LeftArm;

        public ArmPose RightArm;

        public FootPose Foot(Side side) => side == Side.Left ? LeftFoot : RightFoot;

        public ArmPose Arm(Side side) => side == Side.Left ? LeftArm : RightArm;

        /// <summary>Position of one hip joint, given the distance between the two.</summary>
        public Vector3 HipJoint(Side side, float hipWidth)
        {
            return PelvisPosition + Rot.Apply(PelvisRotation, new Vector3((int)side * hipWidth * 0.5f, 0f, 0f));
        }
    }

    /// <summary>A looping motion that can be sampled at any phase.</summary>
    public interface IPoseCycle
    {
        /// <summary>Seconds per loop.</summary>
        float Duration { get; }

        /// <summary>Root velocity in metres per second; zero for motion in place.</summary>
        Vector3 RootVelocity { get; }

        /// <summary>The pose at a phase; any real number, wrapped into [0, 1).</summary>
        GaitPose Evaluate(float phase);
    }
}
