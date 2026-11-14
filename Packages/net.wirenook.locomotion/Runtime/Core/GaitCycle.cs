using System;
using System.Numerics;

namespace Wirenook.Locomotion
{
    /// <summary>
    /// One walking or running cycle for a given body, style, speed and
    /// direction. Phase 0 is the instant the left foot lands; the right foot
    /// runs half a cycle behind.
    ///
    /// The feet are laid out first, as a pure function of phase: each one is
    /// planted for the duty factor's share of the cycle, during which the
    /// point it rolls on stays fixed to the ground, and swings forward for the
    /// rest. The pelvis then rides as high as the legs can reach, less
    /// whatever the body's weight takes off, so the knees bend where that
    /// weight says they should and never have to stretch past straight.
    /// </summary>
    public sealed class GaitCycle : IPoseCycle
    {
        private const int HeightSamples = 192;

        // The walking pelvis follows the legs' reach only as far as its first
        // few harmonics: enough to rise over each leg and dip between them,
        // not enough to trace the corners where one leg takes over from the other.
        private const int HeightHarmonics = 4;
        private const float Tau = 2f * MathF.PI;

        // The brisk-walk speed that style amplitudes are quoted at.
        private const float ReferenceSpeed = 0.67f;

        private readonly GaitProfile _profile;
        private readonly BodyProportions _body;
        private readonly FootShape _foot;
        private readonly Vector3 _travel;
        private readonly Vector3 _trackNormal;
        private readonly float _trackHalfWidth;
        private readonly float _turn;
        private readonly float _strikeAhead;
        private readonly float _strikePitch;
        private readonly float _peakPitch;
        private readonly float _heelRockerEnd;
        private readonly float _toeRockerStart;
        private readonly float _pitchPeakPhase;
        private readonly float _clearance;
        private readonly float _strideYaw;
        private readonly float _leanPitch;
        private readonly float _leanRoll;
        private readonly float[] _reachCos = new float[HeightHarmonics + 1];
        private readonly float[] _reachSin = new float[HeightHarmonics + 1];
        private readonly float _top;

        public GaitRequest Request { get; }

        public GaitTiming Timing { get; }

        public float Duration => Timing.CycleDuration;

        public Vector3 RootVelocity => Request.Velocity;

        public GaitCycle(GaitProfile profile, BodyProportions body, GaitRequest request)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (!body.IsValid) throw new ArgumentException("Body proportions must all be positive.", nameof(body));

            _profile = profile;
            _body = body;
            _foot = new FootShape(body);
            Request = request;
            Timing = GaitTiming.Resolve(profile, body, request);

            float heading = request.Heading;
            float sideways = MathF.Abs(request.Direction.X);
            bool backward = MathF.Abs(heading) > MathF.PI / 2f;
            bool running = Timing.Regime == GaitRegime.Run;
            float beyondTransition = MathF.Max(Timing.DimensionlessSpeed - profile.WalkRunTransition, 0f);
            // Below a brisk walk the steps shrink toward a shuffle, and the
            // foot roll, lift and pelvis list shrink with them.
            float fullGait = MathF.Min(Timing.DimensionlessSpeed / ReferenceSpeed, 1f);

            _travel = new Vector3(request.Direction.X, 0f, request.Direction.Y);

            // Each foot keeps to its own track, a line parallel to the travel
            // direction, so the feet can never collide whatever the heading.
            // The tracks' normal is the perpendicular that keeps the right
            // foot on the right; moving backward that is the other one, which
            // is why a pure strafe is where the two families of gait meet.
            _trackNormal = backward
                ? new Vector3(-request.Direction.Y, 0f, request.Direction.X)
                : new Vector3(request.Direction.Y, 0f, -request.Direction.X);

            // Sideways, one leg passes in front of the other, and the tracks
            // must be a foot apart for the shins to clear.
            float forwardTrack = profile.StepWidth * body.HipWidth * (running ? 0.3f : 0.5f);
            float sidewaysTrack = MathF.Max(forwardTrack, 0.45f * body.FootLength);
            _trackHalfWidth = Shaping.Lerp(forwardTrack, sidewaysTrack, sideways);

            // The pelvis and feet turn part of the way toward the travel
            // direction (or, backing up, toward its opposite).
            float folded = backward ? heading - MathF.Sign(heading) * MathF.PI : heading;
            _turn = profile.HipTurn * folded;

            // How much of the travel the feet see as forward: 1 walking
            // ahead, -1 backing up, in between when they move across.
            float along = MathF.Cos(heading - _turn);

            // A walker lands a little ahead of the hips and pushes off well
            // behind them; a runner lands closer under the body still.
            _strikeAhead = Timing.StrideLength * Timing.DutyFactor * (running ? 0.36f : 0.44f);

            // Forward, the heel lands first and the toes leave last. Backward
            // it is the ball that lands. Runners land flatter the faster they go.
            float heelStrike = profile.HeelStrikeAngle * Rot.Deg * (running ? MathF.Max(0.7f - beyondTransition, 0f) : 1f);
            _strikePitch = fullGait * (along >= 0f ? -heelStrike * along : 0.6f * heelStrike * -along);
            float liftPitch = fullGait * profile.ToeOffAngle * Rot.Deg * (0.3f + 0.7f * MathF.Max(along, 0f));

            float duty = Timing.DutyFactor;
            _heelRockerEnd = 0.15f * duty;
            _toeRockerStart = (running ? 0.35f : 0.55f) * duty;
            _pitchPeakPhase = duty + 0.15f * (1f - duty);
            // The heel is still rising as the toes leave; the pitch curve
            // peaks just after, scaled so it passes through the lift angle at toe-off.
            _peakPitch = liftPitch / Shaping.Smooth((duty - _toeRockerStart) / (_pitchPeakPhase - _toeRockerStart));

            // Even a shuffle has to clear the floor.
            _clearance = (0.4f + 0.6f * fullGait) * profile.StepHeight * body.LegLength *
                         (running ? 2f + 2f * beyondTransition : 1f);

            _strideYaw = profile.PelvisYaw * Rot.Deg * (0.5f * Timing.StrideLength / body.LegLength) * along;

            float lean = MathF.Min(profile.ForwardLean * Timing.DimensionlessSpeed, 25f) * Rot.Deg;
            _leanPitch = lean * request.Direction.Y * (request.Direction.Y < 0f ? 0.5f : 1f);
            _leanRoll = -lean * request.Direction.X * 0.5f;

            // With the feet and the pelvis's plan position fixed, find how
            // high the legs allow the pelvis at each instant.
            var ceiling = new float[HeightSamples];
            for (int i = 0; i < HeightSamples; i++)
            {
                float phase = (float)i / HeightSamples;
                PelvisPlan(phase, out var position, out var rotation);
                ceiling[i] = LegReach.MaxPelvisHeight(body, position, rotation,
                    Foot(Side.Left, phase).Ankle, Foot(Side.Right, phase).Ankle);
            }

            if (!running)
            {
                for (int k = 0; k <= HeightHarmonics; k++)
                {
                    for (int i = 0; i < HeightSamples; i++)
                    {
                        float angle = Tau * k * i / HeightSamples;
                        float scale = (k == 0 ? 1f : 2f) / HeightSamples;
                        _reachCos[k] += scale * ceiling[i] * MathF.Cos(angle);
                        _reachSin[k] += scale * ceiling[i] * MathF.Sin(angle);
                    }
                }
            }

            // Then hang the path as high under that ceiling as it will go.
            float top = float.MaxValue;
            for (int i = 0; i < HeightSamples; i++)
            {
                top = MathF.Min(top, ceiling[i] - HeightBelowTop((float)i / HeightSamples));
            }

            _top = top - profile.Crouch * profile.Weight * body.LegLength;
        }

        public GaitPose Evaluate(float phase)
        {
            phase = Shaping.Wrap01(phase);
            PelvisPlan(phase, out var pelvis, out var pelvisRotation);
            pelvis.Y = _top + HeightBelowTop(phase);

            // The upper body answers the pelvis late, like a load carried on it.
            float lagged = phase - _profile.UpperBodyLag * _profile.Weight;
            float counter = MathF.Cos(Tau * lagged);
            float chestYaw = -_profile.SpineCounterRotation * _strideYaw * counter;
            float chestRoll = _leanRoll - 0.3f * ListAt(lagged);

            return new GaitPose
            {
                PelvisPosition = pelvis,
                PelvisRotation = pelvisRotation,
                LeftFoot = Foot(Side.Left, phase),
                RightFoot = Foot(Side.Right, phase),
                ChestRotation = Rot.Euler(chestYaw, _leanPitch, chestRoll),
                // The head is what the eyes ride on: it holds its heading and
                // takes only a little of the lean.
                HeadRotation = Rot.Euler(0f, 0.3f * _leanPitch, 0.3f * _leanRoll),
                LeftArm = Arm(Side.Left, counter),
                RightArm = Arm(Side.Right, counter),
            };
        }

        /// <summary>The pelvis's position in plan (Y left at zero) and its rotation.</summary>
        private void PelvisPlan(float phase, out Vector3 position, out Quaternion rotation)
        {
            float duty = Timing.DutyFactor;
            float weight = _profile.Weight;
            float speed = Timing.DimensionlessSpeed;

            // Toward the supporting foot: left at the middle of left stance.
            float swayAmplitude = _profile.PelvisSway * _body.HipWidth * weight / (1f + 2f * speed);
            float sway = -swayAmplitude * Shaping.Dwell(MathF.Cos(Tau * (phase - 0.5f * duty)));

            // Each step brakes the body as the foot lands ahead of it and
            // drives it on as the foot pushes away behind: slowest over the
            // planted foot, fastest between steps. This is that speed ripple, integrated.
            float surgeAmplitude = _profile.Surge * _body.LegLength * weight * MathF.Min(speed / ReferenceSpeed, 1.5f);
            float surge = surgeAmplitude * MathF.Sin(2f * Tau * (phase - 0.5f * (duty - 0.5f)));

            var lateral = Rot.Apply(Rot.Yaw(_turn), Vector3.UnitX);
            position = _travel * surge + lateral * sway;

            // The hip of the leg that is reaching forward leads.
            float yaw = _turn + _strideYaw * MathF.Cos(Tau * phase);
            rotation = Rot.Euler(yaw, 0.35f * _leanPitch, 0.35f * _leanRoll + ListAt(phase));
        }

        /// <summary>Pelvis roll that drops the hip of the unsupported side; strongest early in stance.</summary>
        private float ListAt(float phase)
        {
            float amplitude = _profile.PelvisList * Rot.Deg * _profile.Weight *
                              MathF.Min(Timing.DimensionlessSpeed / ReferenceSpeed, 1f);
            return -amplitude * MathF.Cos(Tau * (phase - 0.35f * Timing.DutyFactor));
        }

        /// <summary>Pelvis height before the whole path is shifted to fit under the legs' reach.</summary>
        private float HeightBelowTop(float phase)
        {
            float step = Shaping.Wrap01(2f * phase);
            float duty = Timing.DutyFactor;
            if (Timing.Regime == GaitRegime.Walk)
            {
                // A walker vaults over each planted leg, so the reach of the
                // legs is the shape of the path.
                float reach = 0f;
                for (int k = 0; k <= HeightHarmonics; k++)
                {
                    reach += _reachCos[k] * MathF.Cos(Tau * k * phase) + _reachSin[k] * MathF.Sin(Tau * k * phase);
                }

                // Weight adds a sag on top. It starts as the foot comes down,
                // bottoms out once the load has settled onto the leg, and is
                // gone by the time the body is over it, so the knee lands
                // nearly straight and then gives.
                float low = MathF.Min(_profile.LoadingDelay * _profile.Weight, 0.8f * duty);
                float sag = _profile.LoadingDrop * _profile.Weight * _body.LegLength;
                return reach - sag * Shaping.Dip(step, -0.08f, low, duty);
            }

            // Running is a bouncing mass: on the ground the leg pushes with a
            // half-sine of force that averages out gravity over the step, in
            // the air the body is in free fall. Integrating both gives the
            // path, which is why a longer contact sinks deeper.
            const float g = GaitTiming.Gravity;
            float stepTime = 0.5f * Timing.CycleDuration;
            float contact = Timing.StanceDuration;
            float flight = stepTime - contact;
            float t = step * stepTime;
            float apex = -g * contact * contact / 8f + g * flight * flight / 8f;
            float y;
            if (t < contact)
            {
                float push = g * MathF.PI * stepTime / (2f * contact);
                float fromMiddle = t - 0.5f * contact;
                y = -push * contact * contact / (MathF.PI * MathF.PI) * MathF.Sin(MathF.PI * t / contact)
                    - 0.5f * g * fromMiddle * fromMiddle;
            }
            else
            {
                float airborne = t - contact;
                y = -g * contact * contact / 8f + 0.5f * g * flight * airborne - 0.5f * g * airborne * airborne;
            }

            return _profile.RunBounce * (y - apex);
        }

        private ArmPose Arm(Side side, float counter)
        {
            float speed = Timing.DimensionlessSpeed;
            // Arms swing against the legs when moving ahead, and loosely with
            // them when moving across.
            float drive = Request.Direction.Y + 0.4f * MathF.Abs(Request.Direction.X);
            float amplitude = _profile.ArmSwing * Rot.Deg * MathF.Sqrt(speed / ReferenceSpeed) * drive;
            // The left arm is back when the left foot lands in front.
            float forward = (int)side * counter;
            float carried = Timing.Regime == GaitRegime.Run
                ? _profile.RunElbowBend * Rot.Deg
                : 12f * Rot.Deg * MathF.Min(speed / ReferenceSpeed, 1f) + 8f * Rot.Deg;
            return new ArmPose
            {
                Swing = amplitude * forward,
                Spread = _profile.ArmSpread * Rot.Deg,
                // The elbow closes a little on the way forward and opens on the way back.
                ElbowBend = carried + 0.4f * MathF.Abs(amplitude) * (0.5f + 0.5f * forward),
            };
        }

        private FootPose Foot(Side side, float cyclePhase)
        {
            float phase = side == Side.Left ? cyclePhase : Shaping.Wrap01(cyclePhase + 0.5f);
            float duty = Timing.DutyFactor;
            float stride = Timing.StrideLength;
            float yaw = _turn + (int)side * _profile.ToeOut * Rot.Deg;
            var heading = Rot.Yaw(yaw);
            float pitch = FootPitch(phase);
            var track = _trackNormal * ((int)side * _trackHalfWidth) + Vector3.UnitY * _body.AnkleHeight;

            var pose = new FootPose
            {
                Rotation = heading * Rot.Pitch(pitch),
                KneeDirection = Rot.Apply(heading, Vector3.UnitZ),
            };

            if (phase < duty)
            {
                // Planted. The flat-footed ankle position moves back at ground
                // speed; while the foot rolls on its heel or its ball, that
                // point is what stays on the ground and the ankle arcs over it.
                var flat = track + _travel * (_strikeAhead - stride * phase);
                if (MathF.Abs(pitch) < 1e-5f)
                {
                    pose.Ankle = flat;
                    pose.Contact = FootContact.Flat;
                    pose.ContactPoint = flat - Vector3.UnitY * _body.AnkleHeight;
                }
                else
                {
                    var pivot = pitch < 0f ? _foot.Heel : _foot.Ball;
                    pose.Ankle = flat + Rot.Apply(heading, FootShape.AnkleShift(pivot, pitch));
                    pose.Contact = pitch < 0f ? FootContact.Heel : FootContact.Ball;
                    pose.ContactPoint = flat + Rot.Apply(heading, pivot);
                }

                // The toes stay flat on the ground while the heel lifts.
                pose.ToeBend = -MathF.Max(pitch, 0f);
                return pose;
            }

            // Swinging. Carry a point of the sole from where the foot left the
            // ground to where it will land. That point slides from the pivot
            // the foot leaves on (the ball) to the one it lands on, so over
            // the ground the ankle's motion joins the roll on either side
            // without a jump in speed.
            float swing = (phase - duty) / (1f - duty);
            var liftPivot = _foot.Ball;
            var strikePivot = _strikePitch < 0f ? _foot.Heel : _foot.Ball;
            var from = track + _travel * (_strikeAhead - stride * duty) + Rot.Apply(heading, liftPivot);
            var to = track + _travel * _strikeAhead + Rot.Apply(heading, strikePivot);
            float progress = Shaping.Glide(swing);

            // Over the ground the point goes from rest to rest, covering one
            // stride; seen from the moving root that is the same path minus
            // the root's own advance.
            var carriedPoint = from + (to - from) * progress + _travel * (stride * (1f - duty) * (progress - swing));
            var reference = Vector3.Lerp(liftPivot, strikePivot, Shaping.Smooth(swing));

            // Toes relax after leaving, and flatten again for a ball-first landing.
            float toeHold = 1f - Shaping.Smooth(swing / 0.35f);
            if (_strikePitch > 0f) toeHold += Shaping.Smooth((swing - 0.65f) / 0.35f);
            pose.ToeBend = -MathF.Max(pitch, 0f) * toeHold;

            // Raise the foot by the arc, plus however far its lowest point
            // hangs below the carried point at this pitch, so no part of the
            // sole can dip under the ground.
            // The arch peaks a little before the middle of the swing: the foot
            // comes up quickly behind the body and is let down ahead of it.
            float lift = _clearance * Shaping.Arch(swing * (1.4f - 0.4f * swing))
                         - _foot.LowestBelow(reference, pitch, pose.ToeBend);
            carriedPoint.Y = lift;

            pose.Ankle = carriedPoint - Rot.Apply(pose.Rotation, reference);
            pose.Contact = FootContact.None;
            pose.ContactPoint = carriedPoint;
            return pose;
        }

        /// <summary>
        /// Foot pitch over one foot's own cycle, radians, positive toes-down:
        /// lands at the strike angle, rolls flat, stays flat through
        /// mid-stance, peels up onto the ball, keeps rising a moment after
        /// leaving, then comes back around to the strike angle.
        /// </summary>
        private float FootPitch(float phase)
        {
            if (phase < _heelRockerEnd)
            {
                return _strikePitch * (1f - Shaping.Smooth(phase / _heelRockerEnd));
            }

            if (phase < _toeRockerStart) return 0f;

            if (phase < _pitchPeakPhase)
            {
                return _peakPitch * Shaping.Smooth((phase - _toeRockerStart) / (_pitchPeakPhase - _toeRockerStart));
            }

            return Shaping.Lerp(_peakPitch, _strikePitch,
                Shaping.Smooth((phase - _pitchPeakPhase) / (1f - _pitchPeakPhase)));
        }
    }
}
