namespace Wirenook.Locomotion
{
    /// <summary>
    /// What a crouch changes about standing and walking, beyond how low it
    /// is (<see cref="GaitProfile.CrouchDepth"/>) and how far the chest tips
    /// (<see cref="GaitProfile.CrouchLean"/>). The walk and the idle both
    /// read these, so a crouched figure that starts to move does not change shape.
    /// </summary>
    public static class Crouching
    {
        /// <summary>How far the hips go back, as a share of how far they come down, to stay over the feet.</summary>
        public const float BackShare = 0.3f;

        /// <summary>
        /// How much of a walker's rise over each planted leg is left. Upright,
        /// the pelvis vaults over a straight leg; on bent legs it is carried level.
        /// </summary>
        public const float Vault = 0.35f;

        /// <summary>Stride length relative to the same speed upright: bent legs take shorter, quicker steps.</summary>
        public const float Stride = 0.8f;

        /// <summary>Extra outward turn of each foot and knee, degrees: the knees part to make room for the body.</summary>
        public const float KneesOut = 8f;

        /// <summary>How far forward of hanging the upper arms are carried, degrees.</summary>
        public const float HandsForward = 18f;

        /// <summary>Elbow bend the arms are carried with, degrees.</summary>
        public const float ElbowBend = 55f;

        /// <summary>How much of the upright arm swing is left.</summary>
        public const float ArmSwing = 0.5f;
    }
}
