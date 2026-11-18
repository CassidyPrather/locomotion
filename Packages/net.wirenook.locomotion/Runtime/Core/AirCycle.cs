using System;
using System.Numerics;

namespace Wirenook.Locomotion
{
    /// <summary>The three things a body does off the ground.</summary>
    public enum AirMoment
    {
        /// <summary>Going up, or barely off the ground: stretched out, one knee a little ahead.</summary>
        Rising,

        /// <summary>Coming down from a jump: legs gathering under the body, arms out for balance.</summary>
        Falling,

        /// <summary>A long drop: knees up and apart, arms flung wide, the body tipped to see the ground.</summary>
        Plunging,
    }

    /// <summary>
    /// A body in the air. Nothing holds it, so there is no gait: a held
    /// shape for each <see cref="AirMoment"/>, with the limbs working a
    /// little so that it is not a statue falling.
    ///
    /// The pelvis stays where it is when standing and the legs are drawn up
    /// under it, which is what leaving the ground looks like from a root
    /// that travels with the body.
    /// </summary>
    public sealed class AirCycle : IPoseCycle
    {
        private const float Tau = 2f * MathF.PI;

        // The arm spread the shapes below were drawn at; the profile's
        // AirReach scales the whole upper body from there.
        private const float ReferenceReach = 30f;

        private readonly GaitProfile _profile;
        private readonly BodyProportions _body;
        private readonly AirMoment _moment;

        public AirCycle(GaitProfile profile, BodyProportions body, AirMoment moment)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (!body.IsValid) throw new ArgumentException("Body proportions must all be positive.", nameof(body));
            _profile = profile;
            _body = body;
            _moment = moment;
        }

        /// <summary>A long drop flails faster than a hop drifts.</summary>
        public float Duration => _moment == AirMoment.Plunging ? 0.7f : 1.4f;

        public Vector3 RootVelocity => Vector3.Zero;

        public GaitPose Evaluate(float phase)
        {
            phase = Shaping.Wrap01(phase);
            float leg = _body.LegLength;
            float reach = _profile.AirReach / ReferenceReach;
            // Two slow wobbles a quarter turn apart, so left and right never move together.
            float wobble = MathF.Sin(Tau * phase);
            float counter = MathF.Cos(Tau * phase);

            // Per moment: how far each ankle is drawn up and back (fractions
            // of leg length), how far the feet part, how the toes point, and
            // where the arms, chest and head go (degrees).
            float tuckLeft, tuckRight, back, apart, toes, spread, swing, elbow, chest, head, life;
            switch (_moment)
            {
                case AirMoment.Rising:
                    tuckLeft = 0.04f;
                    tuckRight = 0.10f;
                    back = 0.04f;
                    apart = 0f;
                    toes = 30f;
                    spread = 0.6f * _profile.AirReach;
                    swing = -10f;
                    elbow = 25f;
                    chest = -4f;
                    head = 0f;
                    life = 0.5f;
                    break;
                case AirMoment.Falling:
                    tuckLeft = 0.09f;
                    tuckRight = 0.05f;
                    back = -0.03f;
                    apart = 0.1f;
                    toes = 10f;
                    spread = _profile.AirReach;
                    swing = 8f;
                    elbow = 35f;
                    chest = 6f;
                    head = 2f;
                    life = 0.7f;
                    break;
                default:
                    tuckLeft = 0.16f;
                    tuckRight = 0.22f;
                    back = 0.06f;
                    apart = 0.45f;
                    toes = 20f;
                    spread = 2.2f * _profile.AirReach;
                    swing = 15f;
                    elbow = 45f;
                    chest = 14f;
                    head = 8f;
                    life = 1f;
                    break;
            }

            var pose = Posture.Stand(_profile, _body, 0f, 0f);
            pose.LeftFoot = Lifted(pose.LeftFoot, Side.Left, (tuckLeft + 0.015f * life * wobble) * leg,
                (back + 0.02f * life * counter) * leg, apart, toes);
            pose.RightFoot = Lifted(pose.RightFoot, Side.Right, (tuckRight - 0.015f * life * wobble) * leg,
                (back - 0.02f * life * counter) * leg, apart, toes);

            float pitch = chest * reach * Rot.Deg;
            pose.PelvisRotation = Rot.Pitch(0.35f * pitch);
            pose.ChestRotation = Rot.Euler(0f, pitch, 0.02f * life * reach * wobble);
            pose.HeadRotation = Rot.Pitch(head * reach * Rot.Deg);

            pose.LeftArm = Arm(spread + 5f * life * reach * wobble, swing * reach + 4f * life * reach * counter, elbow);
            pose.RightArm = Arm(spread - 5f * life * reach * wobble, swing * reach - 4f * life * reach * counter, elbow);
            return pose;
        }

        private FootPose Lifted(FootPose planted, Side side, float up, float back, float apart, float toesDown)
        {
            var ankle = planted.Ankle + new Vector3((int)side * apart * 0.5f * _body.HipWidth, up, -back);
            return new FootPose
            {
                Ankle = ankle,
                // Positive pitch points the toes down; a foot with nothing under it hangs that way.
                Rotation = planted.Rotation * Rot.Pitch(toesDown * Rot.Deg),
                ToeBend = 0f,
                Contact = FootContact.None,
                ContactPoint = ankle,
                KneeDirection = planted.KneeDirection,
            };
        }

        private ArmPose Arm(float spread, float swing, float elbow)
        {
            return new ArmPose
            {
                Swing = swing * Rot.Deg,
                Spread = MathF.Max(spread, _profile.ArmSpread) * Rot.Deg,
                ElbowBend = elbow * Rot.Deg,
            };
        }
    }

    /// <summary>
    /// Coming down onto the feet, once: the knees take the fall, the hips go
    /// back and the chest forward over them, and the body stands up again.
    /// A soft landing is a dip; a hard one sinks deep, stays there a moment
    /// and takes longer to rise.
    ///
    /// Not a loop. Phase 0 is the instant of landing, phase 1 is standing,
    /// and anything outside is held at the nearer end.
    /// </summary>
    public sealed class LandingCycle : IPoseCycle
    {
        // The crouch lean the shapes below were drawn at.
        private const float ReferenceLean = 26f;

        private readonly GaitProfile _profile;
        private readonly BodyProportions _body;

        public bool Hard { get; }

        public LandingCycle(GaitProfile profile, BodyProportions body, bool hard)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (!body.IsValid) throw new ArgumentException("Body proportions must all be positive.", nameof(body));
            _profile = profile;
            _body = body;
            Hard = hard;
        }

        public float Duration => Hard ? 0.9f : 0.4f;

        public Vector3 RootVelocity => Vector3.Zero;

        /// <summary>How far the body is down, as a share of its deepest: 0 at both ends.</summary>
        public float Sink(float phase)
        {
            phase = Shaping.Clamp01(phase);
            float down = Hard ? 0.14f : 0.3f;
            float hold = Hard ? 0.34f : 0.3f;
            float up = Hard ? 0.88f : 1f;
            if (phase < down) return Shaping.Smooth(phase / down);
            if (phase < hold) return 1f;
            return 1f - Shaping.Smooth((phase - hold) / (up - hold));
        }

        public GaitPose Evaluate(float phase)
        {
            float sink = Sink(phase);
            float weight = Shaping.Clamp(_profile.Weight, 0.5f, 1.5f);
            float depth = (Hard ? 2.6f : 1f) * _profile.LandingGive * weight * _body.LegLength * sink;
            float lean = (Hard ? 30f : 10f) * MathF.Min(_profile.CrouchLean / ReferenceLean, 1.5f) * Rot.Deg * sink;

            var pose = Posture.Stand(_profile, _body, 0f, 0f);
            pose.PelvisPosition.Y -= depth;
            pose.PelvisPosition.Z -= Crouching.BackShare * depth;
            pose.PelvisRotation = Rot.Pitch(0.35f * lean);
            pose.ChestRotation = Rot.Pitch(lean);
            pose.HeadRotation = Rot.Pitch(0.3f * lean);

            // The arms come out and forward to catch the balance.
            var arm = new ArmPose
            {
                Swing = (Hard ? 22f : 8f) * Rot.Deg * sink,
                Spread = (_profile.ArmSpread + (Hard ? 22f : 8f) * sink) * Rot.Deg,
                ElbowBend = (8f + (Hard ? 40f : 15f) * sink) * Rot.Deg,
            };
            pose.LeftArm = arm;
            pose.RightArm = arm;
            return pose;
        }
    }
}
