using System;
using System.Numerics;

namespace Wirenook.Locomotion
{
    public enum GaitRegime
    {
        /// <summary>Each foot is down for more than half the cycle, so both are down twice per stride.</summary>
        Walk,

        /// <summary>Each foot is down for less than half the cycle, so the body is airborne twice per stride.</summary>
        Run,
    }

    /// <summary>What to synthesise: how fast the root moves over the ground and which way.</summary>
    public readonly struct GaitRequest
    {
        /// <summary>Ground speed in metres per second.</summary>
        public readonly float Speed;

        /// <summary>
        /// Unit direction of travel in the ground plane, relative to where the
        /// body faces: X is right, Y is forward. The body keeps facing forward
        /// whatever this is, so (1, 0) is a strafe and (0, -1) a backpedal.
        /// </summary>
        public readonly Vector2 Direction;

        public GaitRequest(float speed, Vector2 direction)
        {
            Speed = MathF.Max(speed, 0f);
            float length = direction.Length();
            Direction = length > 1e-6f ? direction / length : Vector2.UnitY;
        }

        /// <summary>A request from velocity components in metres per second, as VRChat reports them.</summary>
        public static GaitRequest FromVelocity(float right, float forward)
        {
            var velocity = new Vector2(right, forward);
            return new GaitRequest(velocity.Length(), velocity);
        }

        /// <summary>Root velocity in metres per second.</summary>
        public Vector3 Velocity => new Vector3(Direction.X, 0f, Direction.Y) * Speed;

        /// <summary>Angle of travel from straight ahead, positive to the right, in (-pi, pi].</summary>
        public float Heading => MathF.Atan2(Direction.X, Direction.Y);
    }

    /// <summary>
    /// The rhythm of a gait: how long a stride is, how long it takes and how
    /// much of it each foot spends on the ground.
    /// </summary>
    public readonly struct GaitTiming
    {
        public const float Gravity = 9.81f;

        // Below this dimensionless speed the cadence stops falling; a real
        // walker at a crawl takes small steps rather than ever slower ones,
        // and it keeps the cycle length finite as speed goes to zero.
        private const float SlowestCadenceSpeed = 0.12f;

        // Stance cannot carry the foot further than the leg can span.
        private const float MaxStanceTravel = 1.3f;

        public readonly GaitRegime Regime;

        /// <summary>Distance the root covers in one full cycle (two steps), metres.</summary>
        public readonly float StrideLength;

        /// <summary>Seconds per cycle.</summary>
        public readonly float CycleDuration;

        /// <summary>Share of the cycle each foot spends on the ground.</summary>
        public readonly float DutyFactor;

        /// <summary>Speed divided by sqrt(gravity * leg length): the same value means the same gait at any body size.</summary>
        public readonly float DimensionlessSpeed;

        private GaitTiming(GaitRegime regime, float stride, float duration, float duty, float dimensionlessSpeed)
        {
            Regime = regime;
            StrideLength = stride;
            CycleDuration = duration;
            DutyFactor = duty;
            DimensionlessSpeed = dimensionlessSpeed;
        }

        public float StanceDuration => DutyFactor * CycleDuration;

        public float SwingDuration => (1f - DutyFactor) * CycleDuration;

        /// <summary>Walking: time with both feet down at each step. Zero when running.</summary>
        public float DoubleSupportDuration => MathF.Max(DutyFactor - 0.5f, 0f) * CycleDuration;

        /// <summary>Running: time with neither foot down at each step. Zero when walking.</summary>
        public float FlightDuration => MathF.Max(0.5f - DutyFactor, 0f) * CycleDuration;

        public static GaitTiming Resolve(GaitProfile profile, BodyProportions body, GaitRequest request)
        {
            float speedUnit = MathF.Sqrt(Gravity * body.LegLength);
            float dimensionless = request.Speed / speedUnit;
            var regime = dimensionless >= profile.WalkRunTransition ? GaitRegime.Run : GaitRegime.Walk;

            // Stride length does not scale linearly with speed. Walkers
            // lengthen the stride with about the square root of speed and
            // raise cadence for the rest; runners hold cadence nearly constant
            // and reach further. Both power laws are in leg lengths against
            // dimensionless speed and were fitted to typical adult figures:
            // 1.5 m strides at 1.4 m/s walking; about 160, 170 and 187 steps
            // a minute at 2.5, 4 and 6 m/s running.
            float cadenceSpeed = MathF.Max(dimensionless, SlowestCadenceSpeed);
            float relativeStride = regime == GaitRegime.Walk
                ? 2.45f * MathF.Pow(cadenceSpeed, 0.5f)
                : 2.4f * MathF.Pow(cadenceSpeed, 0.83f);

            float reach = DirectionStrideScale(profile, request);
            float stride = body.LegLength * relativeStride * profile.StrideScale * reach;
            float duty = DutyFactorFor(profile, regime, dimensionless, stride / (body.LegLength * reach));
            stride = MathF.Min(stride, MaxStanceTravel * body.LegLength / duty);

            // The cadence comes from the clamped speed; the stride is then
            // whatever keeps the feet from sliding at the real one.
            float duration = stride / (cadenceSpeed * speedUnit);
            return new GaitTiming(regime, request.Speed * duration, duration, duty, dimensionless);
        }

        /// <param name="relativeStride">Stride in leg lengths, before any shortening for direction.</param>
        private static float DutyFactorFor(GaitProfile profile, GaitRegime regime, float dimensionless,
            float relativeStride)
        {
            // A heavier body stays on the ground longer.
            float weight = profile.Weight - 1f;
            if (regime == GaitRegime.Walk)
            {
                // 0.66 at a crawl down to 0.58 at the fastest walk.
                float relativeSpeed = dimensionless / profile.WalkRunTransition;
                return Shaping.Clamp(0.66f - 0.08f * relativeSpeed + 0.03f * weight, 0.53f, 0.72f);
            }

            // A runner's foot is down for as long as the leg can sweep under
            // the body, a little under one leg length of travel whatever the
            // speed; longer strides are made in the air. That puts the duty
            // factor near 0.42 at a jog, 0.3 at 4 m/s and 0.21 at a sprint.
            float contactTravel = 0.95f * (1f + 0.1f * weight);
            return Shaping.Clamp(contactTravel / relativeStride, 0.15f, 0.47f);
        }

        private static float DirectionStrideScale(GaitProfile profile, GaitRequest request)
        {
            // The hip swings furthest fore and aft, less far behind, least to the side.
            float forward = request.Direction.Y;
            float sideways = request.Direction.X;
            float along = forward >= 0f ? 1f : profile.BackwardStride;
            return forward * forward * along + sideways * sideways * profile.LateralStride;
        }
    }
}
