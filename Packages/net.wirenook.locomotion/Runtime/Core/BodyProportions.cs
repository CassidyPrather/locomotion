using System;

namespace Wirenook.Locomotion
{
    /// <summary>
    /// The few lengths the gait needs from a body, in metres. The baker
    /// measures them from a rig; tests and tools can state them directly.
    /// </summary>
    [Serializable]
    public struct BodyProportions
    {
        /// <summary>Hip joint to ankle joint with the leg straight.</summary>
        public float LegLength;

        /// <summary>Share of <see cref="LegLength"/> above the knee, 0.4 to 0.6.</summary>
        public float ThighFraction;

        /// <summary>Distance between the two hip joints.</summary>
        public float HipWidth;

        /// <summary>Heel to toe tip.</summary>
        public float FootLength;

        /// <summary>Ankle joint above the sole.</summary>
        public float AnkleHeight;

        // Where the ankle sits along the sole, as shares of the foot length:
        // on an adult foot the heel's contact point is about a quarter of a
        // foot behind the ankle and the ball (the toe joint) half a foot ahead.
        private const float HeelShare = 0.25f;
        private const float BallShare = 0.5f;

        public BodyProportions(float legLength, float thighFraction, float hipWidth, float footLength, float ankleHeight)
        {
            LegLength = legLength;
            ThighFraction = thighFraction;
            HipWidth = hipWidth;
            FootLength = footLength;
            AnkleHeight = ankleHeight;
        }

        /// <summary>An adult of about 1.7 m, for tests and as a fallback.</summary>
        public static BodyProportions Adult => new BodyProportions(0.84f, 0.5f, 0.18f, 0.25f, 0.08f);

        public float ThighLength => LegLength * ThighFraction;

        public float ShinLength => LegLength * (1f - ThighFraction);

        /// <summary>Horizontal distance from the ankle back to the heel's contact point.</summary>
        public float HeelBack => FootLength * HeelShare;

        /// <summary>Horizontal distance from the ankle forward to the ball of the foot.</summary>
        public float BallForward => FootLength * BallShare;

        /// <summary>Ball of the foot to the toe tip.</summary>
        public float ToeLength => FootLength * (1f - HeelShare - BallShare);

        /// <summary>The foot length whose ball sits <paramref name="ballForward"/> ahead of the ankle.</summary>
        public static float FootLengthFromBall(float ballForward) => ballForward / BallShare;

        public bool IsValid =>
            LegLength > 0f && HipWidth > 0f && FootLength > 0f && AnkleHeight > 0f &&
            ThighFraction > 0.2f && ThighFraction < 0.8f;
    }
}
