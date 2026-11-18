using System;

namespace Wirenook.Locomotion
{
    /// <summary>
    /// Everything that distinguishes one way of walking from another. Plain
    /// data: the model reads it, the baker and controller generator read it,
    /// nothing here computes. Angles are in degrees because people tune these
    /// by hand; the model converts.
    /// </summary>
    [Serializable]
    public sealed class GaitProfile
    {
        // ---- Weight -------------------------------------------------------

        /// <summary>
        /// Master scale for how much the body gives way to its own mass.
        /// Multiplies the loading drop and its delay, pelvis sway, list and
        /// surge, the upper-body lag and the knee softness, and lengthens
        /// ground contact. 0 is a weightless, purely geometric gait; 1 is the
        /// default; 2 is ponderous. Unitless, 0 to 2.
        /// </summary>
        public float Weight = 1f;

        /// <summary>
        /// How far the pelvis sinks into each step beyond what straight legs
        /// would allow, which is what bends the knee after heel strike.
        /// Fraction of leg length, 0 to 0.05.
        /// </summary>
        public float LoadingDrop = 0.02f;

        /// <summary>
        /// How long after heel strike the weight takes to come fully down
        /// onto the leg; larger settles later and slower. Fraction of one
        /// step, 0.05 to 0.3.
        /// </summary>
        public float LoadingDelay = 0.16f;

        /// <summary>
        /// How far the whole pelvis path is lowered so the knees never lock.
        /// Fraction of leg length, 0 to 0.1.
        /// </summary>
        public float Crouch = 0.005f;

        /// <summary>
        /// How far the chest, head and arms trail the pelvis in time.
        /// Fraction of a gait cycle, 0 to 0.15.
        /// </summary>
        public float UpperBodyLag = 0.05f;

        /// <summary>
        /// Side-to-side travel of the pelvis toward the supporting foot, at a
        /// crawl; it narrows as speed rises. Fraction of hip width, 0 to 0.5.
        /// </summary>
        public float PelvisSway = 0.25f;

        /// <summary>Drop of the hip on the swinging leg's side. Degrees, 0 to 10.</summary>
        public float PelvisList = 4f;

        /// <summary>
        /// Rotation of the pelvis with the stride when one step is as long as
        /// the leg; scales with step length. Degrees, 0 to 12.
        /// </summary>
        public float PelvisYaw = 5f;

        /// <summary>
        /// Fore-aft surge of the pelvis as each step brakes and pushes.
        /// Fraction of leg length, 0 to 0.03.
        /// </summary>
        public float Surge = 0.012f;

        /// <summary>
        /// Vertical travel of a run relative to the ballistic bounce its
        /// contact and flight times imply. Unitless, 0.5 to 1.5.
        /// </summary>
        public float RunBounce = 1f;

        // ---- Stride -------------------------------------------------------

        /// <summary>
        /// Multiplies the stride length the speed relation gives; cadence
        /// changes to keep the speed. Unitless, 0.7 to 1.3.
        /// </summary>
        public float StrideScale = 1f;

        /// <summary>
        /// Speed at which walking gives way to running, as speed divided by
        /// the square root of gravity times leg length, so it adapts to body
        /// size. 0.8 is about 2.3 m/s for an adult. Unitless, 0.6 to 1.
        /// </summary>
        public float WalkRunTransition = 0.8f;

        /// <summary>Stride length when moving sideways relative to forward. Unitless, 0.4 to 1.</summary>
        public float LateralStride = 0.6f;

        /// <summary>Stride length when moving backward relative to forward. Unitless, 0.5 to 1.</summary>
        public float BackwardStride = 0.75f;

        /// <summary>
        /// How far the pelvis and feet turn toward the direction of travel
        /// when it is not straight ahead, as a share of that angle; the chest
        /// keeps facing forward. Unitless, 0 to 0.8.
        /// </summary>
        public float HipTurn = 0.45f;

        // ---- Feet ---------------------------------------------------------

        /// <summary>Distance between the two feet's tracks. Fraction of hip width, 0 to 1.5.</summary>
        public float StepWidth = 0.6f;

        /// <summary>
        /// Ground clearance of the swinging foot's lowest point when walking;
        /// running lifts higher with speed. Fraction of leg length, 0.02 to 0.2.
        /// </summary>
        public float StepHeight = 0.06f;

        /// <summary>Outward turn of each foot. Degrees, -5 to 20.</summary>
        public float ToeOut = 6f;

        /// <summary>Toe-up angle of the foot as the heel lands. Degrees, 0 to 30.</summary>
        public float HeelStrikeAngle = 18f;

        /// <summary>Heel-up angle of the foot as the toes leave. Degrees, 20 to 70.</summary>
        public float ToeOffAngle = 60f;

        // ---- Upper body ---------------------------------------------------

        /// <summary>
        /// Rotation of the chest against the pelvis, as a multiple of the
        /// pelvis's own rotation. Unitless, 0 to 1.5.
        /// </summary>
        public float SpineCounterRotation = 0.7f;

        /// <summary>
        /// Lean into the direction of travel per unit of dimensionless speed
        /// (see <see cref="WalkRunTransition"/>): 5 gives about 3 degrees at a
        /// brisk walk and 10 at a sprint. Degrees, 0 to 12.
        /// </summary>
        public float ForwardLean = 5f;

        /// <summary>Shoulder swing to each side at a brisk walk; grows with speed. Degrees, 0 to 40.</summary>
        public float ArmSwing = 22f;

        /// <summary>How far the arms hang away from the body. Degrees, 0 to 25.</summary>
        public float ArmSpread = 8f;

        /// <summary>Elbow bend carried while running. Degrees, 30 to 110.</summary>
        public float RunElbowBend = 80f;

        // ---- Crouching and the air ---------------------------------------------

        /// <summary>
        /// How far the pelvis comes down in a crouch. Not to be confused with
        /// <see cref="Crouch"/>, which only keeps the knees of an upright
        /// walk from locking. Fraction of leg length, 0.2 to 0.5.
        /// </summary>
        public float CrouchDepth = 0.36f;

        /// <summary>How far the chest tips forward over the knees in a crouch. Degrees, 0 to 45.</summary>
        public float CrouchLean = 26f;

        /// <summary>
        /// How far the arms go out for balance while falling; a long drop
        /// throws them about twice as wide, and the chest tips with them.
        /// Degrees, 0 to 60.
        /// </summary>
        public float AirReach = 30f;

        /// <summary>
        /// How far the knees give on landing from a jump; a hard landing
        /// sinks more than twice as deep. Fraction of leg length, 0.05 to 0.2.
        /// </summary>
        public float LandingGive = 0.12f;

        // ---- Idle ---------------------------------------------------------

        /// <summary>Length of the idle loop: one shift of weight to each side. Seconds, 4 to 16.</summary>
        public float IdlePeriod = 8f;

        /// <summary>Pelvis travel toward each foot while idling. Fraction of hip width, 0 to 0.4.</summary>
        public float IdleSway = 0.12f;

        /// <summary>Chest rise with each breath; two breaths per idle loop. Degrees, 0 to 4.</summary>
        public float BreathDepth = 1.5f;

        // ---- Inertia (read by the controller generator) ---------------------

        /// <summary>
        /// Time the body takes to catch up with a change of velocity: the
        /// time constant of the smoothed velocity. Seconds, 0.1 to 0.8.
        /// </summary>
        public float InertiaTime = 0.3f;

        /// <summary>
        /// Lean into acceleration per metre per second of difference between
        /// the actual and the smoothed velocity. Degrees per m/s, 0 to 5.
        /// </summary>
        public float AccelerationLean = 2.5f;

        /// <summary>Largest lean inertia may add. Degrees, 0 to 20.</summary>
        public float MaxLean = 12f;

        /// <summary>
        /// Bank into a turn taken at <see cref="TurnBankRate"/> while moving
        /// at <see cref="TurnBankSpeed"/> or faster. Degrees, 0 to 15.
        /// </summary>
        public float TurnBank = 6f;

        /// <summary>Turn rate that gives the full bank. Degrees per second, 60 to 360.</summary>
        public float TurnBankRate = 180f;

        /// <summary>Forward speed at which banking reaches full strength. Metres per second, 1 to 8.</summary>
        public float TurnBankSpeed = 4f;

        public GaitProfile Clone() => (GaitProfile)MemberwiseClone();

        /// <summary>
        /// The same gait for a body whose head and hands are tracked. In VR
        /// the head and hands follow the player, and IK bends the spine to
        /// reach them, so animated chest twist, lean and arm swing only fight
        /// the tracking; the legs and a calmer pelvis are what is left for the
        /// animation to drive.
        /// </summary>
        public GaitProfile ForTrackedUpperBody()
        {
            var tracked = Clone();
            tracked.ArmSwing = 0f;
            tracked.SpineCounterRotation = 0f;
            tracked.ForwardLean = 0f;
            tracked.UpperBodyLag = 0f;
            tracked.BreathDepth = 0f;
            // The pelvis hangs from a head that does not move with it: keep
            // enough motion to carry the legs, not enough to wag the torso.
            tracked.PelvisYaw *= 0.5f;
            tracked.PelvisList *= 0.5f;
            tracked.PelvisSway *= 0.5f;
            tracked.HipTurn *= 0.5f;
            tracked.IdleSway *= 0.5f;
            tracked.RunBounce *= 0.6f;
            // Crouched, airborne or landing, the head is still where the
            // player's is: the legs fold under an upright body.
            tracked.CrouchLean = 0f;
            tracked.AirReach = 0f;
            return tracked;
        }
    }
}
