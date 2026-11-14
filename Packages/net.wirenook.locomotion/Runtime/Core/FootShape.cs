using System;
using System.Numerics;

namespace Wirenook.Locomotion
{
    /// <summary>
    /// The sole as three points in the foot's own frame (origin at the ankle,
    /// Z along the foot): the heel and the ball, which are the two places a
    /// foot can roll on, and the toe tip, which hinges at the ball.
    /// </summary>
    public readonly struct FootShape
    {
        public readonly Vector3 Heel;
        public readonly Vector3 Ball;
        public readonly float ToeLength;

        public FootShape(BodyProportions body)
        {
            Heel = new Vector3(0f, -body.AnkleHeight, -body.HeelBack);
            Ball = new Vector3(0f, -body.AnkleHeight, body.BallForward);
            ToeLength = body.ToeLength;
        }

        /// <summary>Toe tip in the foot's frame for a toe joint angle in radians.</summary>
        public Vector3 ToeTip(float toeBend)
        {
            return Ball + Rot.Apply(Rot.Pitch(toeBend), new Vector3(0f, 0f, ToeLength));
        }

        /// <summary>
        /// Where the ankle sits relative to its flat-footed position when the
        /// foot has pitched by <paramref name="pitch"/> radians about a sole
        /// point that stays put on the ground.
        /// </summary>
        public static Vector3 AnkleShift(Vector3 pivot, float pitch)
        {
            return pivot - Rot.Apply(Rot.Pitch(pitch), pivot);
        }

        /// <summary>
        /// Height of the lowest sole point relative to <paramref name="reference"/>
        /// (a point in the foot's frame) once the foot has pitched; zero or
        /// negative when the reference lies on the sole line.
        /// </summary>
        public float LowestBelow(Vector3 reference, float pitch, float toeBend)
        {
            var rotation = Rot.Pitch(pitch);
            float heel = Rot.Apply(rotation, Heel - reference).Y;
            float ball = Rot.Apply(rotation, Ball - reference).Y;
            float toe = Rot.Apply(rotation, ToeTip(toeBend) - reference).Y;
            return MathF.Min(heel, MathF.Min(ball, toe));
        }
    }

    /// <summary>How high the legs let the pelvis ride.</summary>
    public static class LegReach
    {
        /// <summary>
        /// Longest the hip-to-ankle distance may get, as a share of leg
        /// length. Just short of straight: a locked knee pops visibly when it
        /// unlocks, and leaves the leg solver no room for rounding error.
        /// </summary>
        public const float MaxExtension = 0.998f;

        /// <summary>
        /// The highest the pelvis (hip-joint midpoint) can be, given where it
        /// is in plan, how it is turned and where both ankles are, with
        /// neither leg stretched past <see cref="MaxExtension"/>.
        /// </summary>
        public static float MaxPelvisHeight(BodyProportions body, Vector3 pelvis, Quaternion pelvisRotation,
            Vector3 leftAnkle, Vector3 rightAnkle)
        {
            return MathF.Min(
                MaxForLeg(body, pelvis, pelvisRotation, Side.Left, leftAnkle),
                MaxForLeg(body, pelvis, pelvisRotation, Side.Right, rightAnkle));
        }

        private static float MaxForLeg(BodyProportions body, Vector3 pelvis, Quaternion pelvisRotation, Side side,
            Vector3 ankle)
        {
            var hipOffset = Rot.Apply(pelvisRotation, new Vector3((int)side * body.HipWidth * 0.5f, 0f, 0f));
            float dx = pelvis.X + hipOffset.X - ankle.X;
            float dz = pelvis.Z + hipOffset.Z - ankle.Z;
            float reach = MaxExtension * body.LegLength;
            float vertical = MathF.Sqrt(MathF.Max(reach * reach - dx * dx - dz * dz, 0f));
            return ankle.Y + vertical - hipOffset.Y;
        }
    }
}
