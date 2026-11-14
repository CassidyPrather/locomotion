using System;
using System.Text;
using UnityEditor.Animations;
using UnityEngine;

namespace Wirenook.Locomotion.Editor
{
    /// <summary>
    /// Checks the generated controllers by running them: the Base and
    /// Additive controllers are stacked on a copy of the rig, VRChat's
    /// built-in parameters are driven by hand, and the resulting skeleton and
    /// parameters are measured frame by frame.
    /// </summary>
    internal sealed class ControllerVerifier : IDisposable
    {
        private const float Frame = 1f / 60f;

        private readonly LocomotionBake _bake;
        private readonly AnimatorController _base;
        private readonly AnimatorController _additive;
        private readonly HumanoidRig _rig;
        private readonly StringBuilder _report = new StringBuilder();
        private bool _passed = true;

        private ControllerVerifier(LocomotionBake bake, GameObject rigSource, AnimatorController baseController,
            AnimatorController additive)
        {
            _bake = bake;
            _base = baseController;
            _additive = additive;
            _rig = new HumanoidRig(rigSource);
        }

        public void Dispose() => _rig.Dispose();

        /// <summary>Runs every check. Returns whether all passed, with one line per check in the report.</summary>
        public static bool Verify(LocomotionBake bake, GameObject rigSource, AnimatorController baseController,
            AnimatorController additive, out string report)
        {
            using var verifier = new ControllerVerifier(bake, rigSource, baseController, additive);
            verifier.GaitsMatchTheModel();
            verifier.FeetHoldTheGroundBetweenSamples();
            verifier.CadenceRisesBeyondTheFastestGait();
            verifier.PostureFollowsUpright();
            verifier.JumpingAndFallingFollowTheDefaultController();
            verifier.InertiaIsFrameRateIndependent();
            verifier.LeanFollowsAccelerationAndSettles();
            verifier.BankFollowsTurns();
            report = (verifier._passed ? "PASS" : "FAIL") + ": controllers\n" + verifier._report;
            return verifier._passed;
        }

        /// <summary>The two checks that show a pair of controllers is alive: the gaits play and the body leans.</summary>
        public static bool VerifyGaitAndLean(LocomotionBake bake, GameObject rigSource, AnimatorController baseController,
            AnimatorController additive, out string report)
        {
            using var verifier = new ControllerVerifier(bake, rigSource, baseController, additive);
            verifier.GaitsMatchTheModel();
            verifier.LeanFollowsAccelerationAndSettles();
            report = verifier._report.ToString();
            return verifier._passed;
        }

        private LayerStackSimulator Start(bool withAdditive = true, float vrMode = 0f, float trackingType = 3f)
        {
            var simulator = new LayerStackSimulator(_rig.Animator, _base, withAdditive ? _additive : null);
            simulator.Set(LocomotionControllers.Grounded, 1f);
            simulator.Set(LocomotionControllers.Upright, 1f);
            simulator.Set(LocomotionControllers.VRMode, vrMode);
            simulator.Set(LocomotionControllers.TrackingType, trackingType);
            return simulator;
        }

        private static void Move(LayerStackSimulator simulator, float right, float forward)
        {
            simulator.Set(LocomotionControllers.VelocityX, right);
            simulator.Set(LocomotionControllers.VelocityZ, forward);
        }

        private void Check(string what, bool passed, string detail)
        {
            _passed &= passed;
            _report.AppendLine((passed ? "  ok   " : "  FAIL ") + what + ": " + detail);
        }

        private static string Millimetres(float metres) => (metres * 1000f).ToString("F1") + " mm";

        /// <summary>
        /// At a baked velocity the tree must play exactly that gait. Leftward
        /// velocities exercise the mirrored children, VR mode the tracked set.
        /// </summary>
        private void GaitsMatchTheModel()
        {
            var cases = new (float right, float forward, bool tracked)[]
            {
                (0f, 2f, false), (0f, -4f, false), (2f, 0f, false), (-2f, 0f, false), (-2f, 4f, false),
                (-2f, -2f, false), (0f, 6f, false), (0f, 2f, true), (-4f, 0f, true),
            };
            float worst = 0f;
            foreach (var (right, forward, tracked) in cases)
            {
                using var simulator = Start(false, tracked ? 1f : 0f);
                Move(simulator, right, forward);
                var style = tracked ? _bake.Profile.ForTrackedUpperBody() : _bake.Profile;
                var cycle = new GaitCycle(style, _bake.Body, GaitRequest.FromVelocity(right, forward));
                simulator.Run(0.5f, Frame);
                for (int i = 0; i < 90; i++)
                {
                    simulator.Step(Frame);
                    float phase = simulator.Base.GetCurrentAnimatorStateInfo(0).normalizedTime;
                    var expected = _rig.Fit(cycle.Evaluate(phase), _bake.Body);
                    worst = Mathf.Max(worst, (_rig.Ankle(Side.Left) - expected.LeftFoot.Ankle.ToUnity()).magnitude);
                    worst = Mathf.Max(worst, (_rig.Ankle(Side.Right) - expected.RightFoot.Ankle.ToUnity()).magnitude);
                }
            }

            Check("blend tree plays the baked gait at its own velocity, mirrored and tracked sets included",
                worst <= BakeVerifier.AnkleTolerance, "worst ankle error " + Millimetres(worst) + " over " + cases.Length + " velocities");
        }

        /// <summary>
        /// Between baked velocities the tree blends neighbours, and their
        /// stride lengths and cadences do not combine into exactly the
        /// requested speed. This measures how far off the planted foot is.
        /// </summary>
        private void FeetHoldTheGroundBetweenSamples()
        {
            var atSamples = new (float, float)[]
            {
                (0f, 0.7f), (0f, 1.2f), (0f, 2f), (0f, 4f), (0f, 6f), (2f, 0f), (-2f, 4f), (0f, -2f), (-4f, 0f),
            };
            var between = new (float, float)[]
            {
                (0f, 1f), (0f, 1.6f), (0f, 3f), (0f, 5f), (1.2f, 0f), (1.4f, 1.4f), (-1.4f, 2.8f), (0f, -3f), (1f, 3f),
            };
            float worstAtSamples = 0f, worstBetween = 0f;
            var detail = new StringBuilder();
            var sampleDetail = new StringBuilder();
            foreach (var (right, forward) in atSamples)
            {
                float error = FootSpeedError(right, forward);
                worstAtSamples = Mathf.Max(worstAtSamples, Mathf.Abs(error));
                sampleDetail.Append(" (" + right + ", " + forward + "): " + error.ToString("+0.0%;-0.0%"));
            }

            foreach (var (right, forward) in between)
            {
                float error = FootSpeedError(right, forward);
                worstBetween = Mathf.Max(worstBetween, Mathf.Abs(error));
                detail.Append(" (" + right + ", " + forward + "): " + error.ToString("+0%;-0%"));
            }

            Check("planted feet move at ground speed at baked velocities", worstAtSamples <= 0.05f,
                "worst error " + worstAtSamples.ToString("P1") + " of ground speed;" + sampleDetail);
            Check("planted feet stay near ground speed between baked velocities", worstBetween <= 0.25f,
                "worst error " + worstBetween.ToString("P1") + ";" + detail);
        }

        /// <summary>
        /// How fast the planted part of a foot passes under the root, relative
        /// to the speed the avatar is moving at: 0 is a foot that holds the
        /// ground, negative one that is too slow and so slides forward.
        /// </summary>
        private float FootSpeedError(float right, float forward)
        {
            return FootSpeed(right, forward) / new Vector2(right, forward).magnitude - 1f;
        }

        /// <summary>
        /// Speed, in m/s, at which the part of the left sole that is on the
        /// ground travels back through root space, averaged over its stance.
        /// The ball is used while it is down, since most of a stance and all
        /// of a runner's is spent on it, and the heel while only it is down.
        /// A point counts as down only in the middle of a run of low frames:
        /// a foot skimming in to land, or just peeling off, is low but moving.
        /// </summary>
        private float FootSpeed(float right, float forward)
        {
            const float frame = 1f / 120f;
            const float down = 0.006f;
            const int margin = 3;

            using var simulator = Start(false);
            Move(simulator, right, forward);
            var travel = new Vector3(right, 0f, forward).normalized;
            simulator.Run(0.5f, frame);

            // Whole cycles, so every part of the stance counts once.
            int frames = Mathf.RoundToInt(2f * simulator.Base.GetCurrentAnimatorStateInfo(0).length / frame);
            var heel = new Vector3[frames];
            var ball = new Vector3[frames];
            for (int i = 0; i < frames; i++)
            {
                simulator.Step(frame);
                heel[i] = _rig.Heel(Side.Left);
                ball[i] = _rig.Ball(Side.Left);
            }

            bool Planted(Vector3[] track, int at)
            {
                for (int i = at - margin; i <= at + margin + 1; i++)
                {
                    if (i < 0 || i >= frames || track[i].y > down) return false;
                }

                return true;
            }

            float distance = 0f, time = 0f;
            for (int i = 0; i + 1 < frames; i++)
            {
                var track = Planted(ball, i) ? ball : Planted(heel, i) ? heel : null;
                if (track == null) continue;
                distance -= Vector3.Dot(track[i + 1] - track[i], travel);
                time += frame;
            }

            return time > 0f ? distance / time : 0f;
        }

        private void CadenceRisesBeyondTheFastestGait()
        {
            float Cadence(float right, float forward)
            {
                using var simulator = Start(false);
                Move(simulator, right, forward);
                simulator.Run(0.2f, Frame);
                return simulator.Base.GetFloat(LocomotionControllers.Cadence);
            }

            float still = Cadence(0f, 0f), within = Cadence(0f, 3f), twiceSprint = Cadence(0f, 12f);
            float twiceStrafe = Cadence(-8f, 0f), fastBackward = Cadence(0f, -6f);
            bool passed = Mathf.Abs(still - 1f) < 0.02f && Mathf.Abs(within - 1f) < 0.02f &&
                          Mathf.Abs(twiceSprint - 2f) < 0.15f && Mathf.Abs(twiceStrafe - 2f) < 0.15f &&
                          Mathf.Abs(fastBackward - 1.5f) < 0.15f;
            float slip = Mathf.Abs(FootSpeedError(0f, 9f));
            Check("cadence multiplier is 1 within the baked speeds and proportional beyond them", passed && slip <= 0.05f,
                "still " + still.ToString("F2") + ", 3 m/s " + within.ToString("F2") + ", 12 m/s ahead " +
                twiceSprint.ToString("F2") + ", 8 m/s sideways " + twiceStrafe.ToString("F2") + ", 6 m/s backward " +
                fastBackward.ToString("F2") + "; foot speed error at 9 m/s ahead " + slip.ToString("P1"));
        }

        private void PostureFollowsUpright()
        {
            using var simulator = Start(false);
            var seen = new StringBuilder();
            bool passed = true;
            foreach (var (upright, state) in new[]
                     {
                         (1f, "Standing"), (0.69f, "Standing"), (0.6f, "Crouching"), (0.42f, "Crouching"),
                         (0.3f, "Prone"), (0.42f, "Prone"), (0.5f, "Crouching"), (0.69f, "Crouching"), (0.8f, "Standing"),
                     })
            {
                simulator.Set(LocomotionControllers.Upright, upright);
                simulator.Run(1f, Frame);
                bool there = simulator.IsInState(simulator.Base, 0, state);
                passed &= there;
                seen.Append(" " + upright + "->" + state + (there ? "" : "(!)"));
            }

            Check("Upright switches standing, crouching and prone at VRChat's thresholds", passed, seen.ToString());
        }

        private void JumpingAndFallingFollowTheDefaultController()
        {
            var seen = new StringBuilder();
            bool passed = true;

            void Expect(LayerStackSimulator simulator, float seconds, string state)
            {
                simulator.Run(seconds, Frame);
                bool there = simulator.IsInState(simulator.Base, 0, state);
                passed &= there;
                seen.Append(" " + state + (there ? "" : "(!)"));
            }

            using (var simulator = Start(false))
            {
                // A jump: up, over the top, down, land.
                simulator.Run(0.5f, Frame);
                simulator.Set(LocomotionControllers.Grounded, 0f);
                simulator.Set(LocomotionControllers.VelocityY, 3f);
                Expect(simulator, 0.3f, "SmallHop");
                simulator.Set(LocomotionControllers.VelocityY, -3f);
                Expect(simulator, 0.3f, "Short Fall");
                simulator.Set(LocomotionControllers.Grounded, 1f);
                simulator.Set(LocomotionControllers.VelocityY, 0f);
                // The way out of QuickLand waits for its clip to finish. The
                // SDK's proxy_land_quick is a zero-length placeholder for the
                // client's real clip, so in the editor it never does; the
                // hard landing below takes the same way back with a clip that ends.
                Expect(simulator, 0.15f, "QuickLand");
            }

            seen.Append(" |");
            using (var simulator = Start(false))
            {
                // A long drop, and a hard landing.
                simulator.Run(0.5f, Frame);
                simulator.Set(LocomotionControllers.Grounded, 0f);
                simulator.Set(LocomotionControllers.VelocityY, -25f);
                Expect(simulator, 0.5f, "LongFall");
                simulator.Set(LocomotionControllers.Grounded, 1f);
                simulator.Set(LocomotionControllers.VelocityY, 0f);
                Expect(simulator, 0.1f, "HardLand");
                Expect(simulator, 3f, "Standing");
            }

            seen.Append(" |");
            using (var simulator = Start(false))
            {
                // Leaving the ground while seated is a station moving, not a jump.
                simulator.Set(LocomotionControllers.Seated, 1f);
                simulator.Set(LocomotionControllers.Grounded, 0f);
                Expect(simulator, 0.5f, "Standing");
            }

            Check("jump, fall and landing states follow Grounded and VelocityY", passed, seen.ToString());
        }

        private void InertiaIsFrameRateIndependent()
        {
            float timeConstant = _bake.Profile.InertiaTime;
            float expected = 4f * (1f - Mathf.Exp(-1f));
            var detail = new StringBuilder();
            bool passed = true;
            foreach (float rate in new[] { 20f, 30f, 45f, 72f, 90f, 144f })
            {
                float frame = 1f / rate;
                using var simulator = Start();
                simulator.Run(1f, frame);
                Move(simulator, 0f, 4f);
                // Run to the first frame at or past one time constant, and
                // compare with the ideal filter at that same instant.
                int frames = Mathf.CeilToInt(timeConstant / frame - 1e-4f);
                for (int i = 0; i < frames; i++) simulator.Step(frame);
                float smoothed = simulator.Additive.GetFloat(LocomotionControllers.SmoothZ);
                float measured = simulator.Additive.GetFloat(LocomotionControllers.FrameTime);
                float exact = 4f * (1f - Mathf.Exp(-frames * frame / timeConstant));
                passed &= Mathf.Abs(smoothed - exact) <= 0.06f * 4f && Mathf.Abs(measured - frame) <= 0.02f * frame;
                detail.Append(" " + rate + " fps: " + smoothed.ToString("F2") + " (ideal " + exact.ToString("F2") + ")");
            }

            Check("smoothed velocity reaches the same value after one time constant at any frame rate", passed,
                "after a 0 to 4 m/s step, " + expected.ToString("F2") + " m/s at exactly one time constant;" + detail);
        }

        /// <summary>
        /// Stopping from a run pushes the body back and lets it settle; how
        /// far depends on who is watching from inside: all of it on desktop,
        /// a share with head and hands tracked, none with the body tracked.
        /// </summary>
        private void LeanFollowsAccelerationAndSettles()
        {
            float fullLean = Mathf.Min(_bake.Profile.AccelerationLean * 4f, _bake.Profile.MaxLean) * Mathf.Deg2Rad;
            // The lean carries the pelvis half its angle about the ankles.
            float expectedShift = (_bake.Body.LegLength + _bake.Body.AnkleHeight) * Mathf.Sin(0.5f * fullLean);

            float desktop = LeanAfterStopping(0f, 3f, out float settled, out float footShift);
            float tracked = LeanAfterStopping(1f, 3f, out _, out _);
            float bodyTracked = LeanAfterStopping(1f, 6f, out _, out _);

            Check("stopping from 4 m/s rocks the pelvis back by the configured lean and settles",
                Mathf.Abs(desktop - expectedShift) <= 0.25f * expectedShift && settled <= 0.003f && footShift <= 0.03f,
                "pelvis moved back " + Millimetres(desktop) + " (expected about " + Millimetres(expectedShift) +
                "), back within " + Millimetres(settled) + " of rest after 3 s, feet moved at most " + Millimetres(footShift));
            Check("lean is reduced with head and hands tracked and absent with the body tracked",
                Mathf.Abs(tracked - LocomotionBake.TrackedLeanShare * desktop) <= 0.3f * LocomotionBake.TrackedLeanShare * desktop + 0.002f &&
                bodyTracked <= 0.002f,
                "desktop " + Millimetres(desktop) + ", head and hands " + Millimetres(tracked) + ", full body " + Millimetres(bodyTracked));
        }

        /// <summary>How far back, in metres, the pelvis rocks just after stopping from 4 m/s.</summary>
        private float LeanAfterStopping(float vrMode, float trackingType, out float settled, out float footShift)
        {
            using var simulator = Start(true, vrMode, trackingType);
            // Idle sways sideways and breathes but does not move the pelvis fore and aft.
            simulator.Run(2f, Frame);
            float restZ = _rig.RootPosition(HumanBodyBones.Hips).z;
            var restAnkle = _rig.Ankle(Side.Left);

            Move(simulator, 0f, 4f);
            simulator.Run(3f, Frame);
            Move(simulator, 0f, 0f);

            float furthest = 0f;
            footShift = 0f;
            for (int i = 0; i < 20; i++)
            {
                simulator.Step(Frame);
                // Give the idle a few frames to take over from the run before measuring.
                if (i < 3) continue;
                furthest = Mathf.Max(furthest, restZ - _rig.RootPosition(HumanBodyBones.Hips).z);
                footShift = Mathf.Max(footShift, (_rig.Ankle(Side.Left) - restAnkle).magnitude);
            }

            simulator.Run(3f, Frame);
            settled = Mathf.Abs(_rig.RootPosition(HumanBodyBones.Hips).z - restZ);
            return furthest;
        }

        private void BankFollowsTurns()
        {
            float MeanPelvisX(float turnRate, float forward)
            {
                using var simulator = Start();
                Move(simulator, 0f, forward);
                simulator.Set(LocomotionControllers.AngularY, turnRate);
                simulator.Run(3f, Frame);
                // Average over whole strides so the gait's own sway cancels.
                float sum = 0f;
                int frames = 0;
                float startPhase = simulator.Base.GetCurrentAnimatorStateInfo(0).normalizedTime;
                while (simulator.Base.GetCurrentAnimatorStateInfo(0).normalizedTime < startPhase + 2f && frames < 600)
                {
                    simulator.Step(Frame);
                    sum += _rig.RootPosition(HumanBodyBones.Hips).x;
                    frames++;
                }

                return sum / frames;
            }

            var profile = _bake.Profile;
            float expected = (_bake.Body.LegLength + _bake.Body.AnkleHeight) * Mathf.Sin(0.5f * profile.TurnBank * Mathf.Deg2Rad);
            float right = MeanPelvisX(profile.TurnBankRate, profile.TurnBankSpeed);
            float left = MeanPelvisX(-profile.TurnBankRate, profile.TurnBankSpeed);
            float halfRate = MeanPelvisX(0.5f * profile.TurnBankRate, profile.TurnBankSpeed);
            float standing = MeanPelvisX(profile.TurnBankRate, 0f) - MeanPelvisX(0f, 0f);
            Check("turning while moving banks into the turn, in proportion, and not at a standstill",
                Mathf.Abs(right - expected) <= 0.3f * expected && Mathf.Abs(left + expected) <= 0.3f * expected &&
                Mathf.Abs(halfRate - 0.5f * expected) <= 0.3f * expected && Mathf.Abs(standing) <= 0.003f,
                "pelvis shift at full turn rate: right " + Millimetres(right) + ", left " + Millimetres(left) +
                " (expected ±" + Millimetres(expected) + "), half rate " + Millimetres(halfRate) +
                ", standing still " + Millimetres(standing));
        }
    }
}
