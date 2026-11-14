using System;

namespace Wirenook.Locomotion
{
    /// <summary>Scalar easing and wrapping used to give the gait its timing.</summary>
    public static class Shaping
    {
        public static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;

        public static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;

        public static float Lerp(float from, float to, float t) => from + (to - from) * t;

        /// <summary>Wraps any phase into [0, 1).</summary>
        public static float Wrap01(float phase)
        {
            float wrapped = phase - MathF.Floor(phase);
            return wrapped >= 1f ? 0f : wrapped;
        }

        /// <summary>Cubic ease in and out; zero slope at both ends.</summary>
        public static float Smooth(float t)
        {
            t = Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Progress of a movement that starts and ends at rest but spends
        /// little time near either: speed follows a half-circle, the integral
        /// of sqrt(t (1 - t)). A swinging foot moves like this. It breaks away
        /// and arrives briskly, so it neither trails far behind the body
        /// after leaving the ground nor floats far ahead before landing, as a
        /// polynomial ease makes it do.
        /// </summary>
        public static float Glide(float t)
        {
            t = Clamp01(t);
            float centred = 2f * t - 1f;
            return 0.5f + (MathF.Asin(centred) + 2f * centred * MathF.Sqrt(t * (1f - t))) / MathF.PI;
        }

        /// <summary>
        /// A half-sine arch from 0 up to 1 and back over t in [0, 1]. It
        /// leaves and returns at a slope, which is how a foot leaves and
        /// meets the ground: an arch that eased in would have the foot
        /// skimming the floor for the last fifth of every step.
        /// </summary>
        public static float Arch(float t) => MathF.Sin(MathF.PI * Clamp01(t));

        /// <summary>
        /// Reshapes a sinusoid in [-1, 1] so it lingers near its extremes and
        /// crosses the middle faster: weight that settles over a foot rather
        /// than gliding past it.
        /// </summary>
        public static float Dwell(float sine) => sine * (1.5f - 0.5f * sine * sine);

        /// <summary>
        /// A periodic dip with period 1: zero until <paramref name="start"/>,
        /// eased down to 1 at <paramref name="low"/>, eased back to zero at
        /// <paramref name="end"/> and zero for the rest of the period. The
        /// three phases must come in that order within one period.
        /// </summary>
        public static float Dip(float phase, float start, float low, float end)
        {
            float fall = Wrap01(low - start);
            float rise = Wrap01(end - low);
            float sinceStart = Wrap01(phase - start);
            if (sinceStart < fall) return Smooth(sinceStart / fall);
            if (sinceStart < fall + rise) return 1f - Smooth((sinceStart - fall) / rise);
            return 0f;
        }
    }
}
