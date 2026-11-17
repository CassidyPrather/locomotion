using System;
using System.Numerics;

namespace Wirenook.Locomotion
{
    /// <summary>
    /// Asleep on one's feet: what the body does while its owner is away.
    ///
    /// The stance is the ordinary stand let go. The chest slumps, the arms
    /// hang plumb from it instead of being held, the knees give a little, and
    /// the head hangs forward and to one side. Over one loop the breathing is
    /// slow and deep, and the head sinks further and further until it is
    /// caught with a small start and begins to sink again, the way people
    /// nod off sitting up.
    /// </summary>
    public sealed class NapCycle : IPoseCycle
    {
        private const float Tau = 2f * MathF.PI;
        private const int BreathsPerLoop = 3;

        /// <summary>Seconds per loop: three breaths and one nod.</summary>
        public const float Period = 11f;

        /// <summary>Forward lean of the whole stance, from the ankles, in degrees.</summary>
        private const float Lean = 3f;

        /// <summary>How far the chest slumps forward, in degrees.</summary>
        private const float Slump = 11f;

        /// <summary>How far the head hangs forward of the chest: just caught, and at its lowest.</summary>
        private const float HeadCaught = 24f;
        private const float HeadSunk = 37f;

        /// <summary>The head's tilt to one side, in degrees.</summary>
        private const float HeadTilt = 7f;

        /// <summary>Share of the loop the head spends sinking; the rest is the start that catches it.</summary>
        private const float SinkShare = 0.86f;

        /// <summary>How much of the leg's length the knees give.</summary>
        private const float Sag = 0.012f;

        private readonly GaitProfile _profile;
        private readonly BodyProportions _body;

        public NapCycle(GaitProfile profile, BodyProportions body)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (!body.IsValid) throw new ArgumentException("Body proportions must all be positive.", nameof(body));
            _profile = profile;
            _body = body;
        }

        public float Duration => Period;

        public Vector3 RootVelocity => Vector3.Zero;

        public GaitPose Evaluate(float phase)
        {
            phase = Shaping.Wrap01(phase);
            float breath = MathF.Sin(Tau * BreathsPerLoop * phase);
            float sink = Sink(phase);

            var pose = Posture.Stand(_profile, _body, Lean * Rot.Deg, 0f);
            pose.PelvisPosition.Y -= Sag * _body.LegLength;

            // The in-breath lifts the chest a little out of its slump, and a
            // sinking head drags the chest after it.
            float chest = (Slump + 2.5f * sink - 2f * breath) * Rot.Deg;
            pose.ChestRotation = Rot.Euler(0f, chest, 0f);
            float head = chest + Shaping.Lerp(HeadCaught, HeadSunk, sink) * Rot.Deg;
            pose.HeadRotation = Rot.Euler(0f, head, -HeadTilt * Rot.Deg * (0.6f + 0.4f * sink));

            // Arms are given relative to the chest. Hanging plumb from a
            // chest tipped forward is a swing forward by the same angle.
            var arm = new ArmPose { Swing = chest, Spread = 0.4f * _profile.ArmSpread * Rot.Deg, ElbowBend = 5f * Rot.Deg };
            pose.LeftArm = arm;
            pose.RightArm = arm;
            return pose;
        }

        /// <summary>
        /// 0 with the head just caught, 1 at its lowest: a slow ease down over
        /// most of the loop and a quick one back, meeting smoothly at both ends.
        /// </summary>
        public static float Sink(float phase)
        {
            phase = Shaping.Wrap01(phase);
            return phase < SinkShare
                ? Shaping.Smooth(phase / SinkShare)
                : 1f - Shaping.Smooth((phase - SinkShare) / (1f - SinkShare));
        }
    }
}
