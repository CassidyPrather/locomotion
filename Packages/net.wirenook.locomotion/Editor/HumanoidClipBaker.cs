using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Wirenook.Locomotion.Editor
{
    /// <summary>One baked clip: the motion it came from and the humanoid curves that reproduce it.</summary>
    internal sealed class BakedClip
    {
        public string Name;
        public IPoseCycle Cycle;
        public float Duration;
        public bool Loop;

        /// <summary>Animator float curves by property name, as they go into a humanoid clip.</summary>
        public readonly Dictionary<string, AnimationCurve> Curves = new Dictionary<string, AnimationCurve>();

        /// <summary>Writes the curves and the settings a humanoid locomotion clip needs.</summary>
        public void WriteTo(AnimationClip clip, AnimationClip additiveReference = null)
        {
            clip.ClearCurves();
            clip.frameRate = HumanoidClipBaker.FrameRate;
            foreach (var curve in Curves)
            {
                AnimationUtility.SetEditorCurve(clip,
                    EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), curve.Key), curve.Value);
            }

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = Loop;
            // The baked loop already closes on itself; Unity's own loop
            // blending would bend the last frames toward the first.
            settings.loopBlend = false;
            // Keep the body exactly where the curves put it relative to the
            // avatar's root. Without "bake into pose" Unity treats the pelvis's
            // sway and surge as root motion and subtracts them from the pose,
            // and VRChat, which moves the player itself, then throws that
            // motion away: the feet would slide by however far the pelvis moved.
            settings.loopBlendOrientation = true;
            settings.loopBlendPositionY = true;
            settings.loopBlendPositionXZ = true;
            settings.keepOriginalOrientation = true;
            settings.keepOriginalPositionY = true;
            settings.keepOriginalPositionXZ = true;
            settings.heightFromFeet = false;
            settings.level = 0f;
            settings.cycleOffset = 0f;
            settings.mirror = false;
            settings.hasAdditiveReferencePose = additiveReference != null;
            settings.additiveReferencePoseClip = additiveReference;
            settings.additiveReferencePoseTime = 0f;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
        }
    }

    /// <summary>
    /// Turns a pose cycle into humanoid animation curves by posing a real
    /// skeleton at each sample and reading back the muscle values Unity
    /// derives from it.
    /// </summary>
    internal static class HumanoidClipBaker
    {
        public const float FrameRate = 60f;

        // Curve names for the body's position and orientation in a humanoid
        // clip. These, like the muscle names, were checked by sampling baked
        // clips back onto the rig (see BakeVerifier), not taken on trust.
        private static readonly string[] RootPosition = { "RootT.x", "RootT.y", "RootT.z" };
        private static readonly string[] RootRotation = { "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };

        // Largest error key reduction may introduce: muscles are fractions of
        // a joint's range (0.002 is about a tenth of a degree), the body
        // position is in avatar-normalised metres, the rotation a quaternion component.
        private const float MuscleTolerance = 0.002f;
        private const float PositionTolerance = 0.0004f;
        private const float RotationTolerance = 0.0004f;

        /// <summary>Bakes a loop: phase 1 is phase 0 again, and the curves close on themselves.</summary>
        public static BakedClip Bake(string name, IPoseCycle cycle, HumanoidRig rig, BodyProportions body)
        {
            return Bake(name, cycle, rig, body, true);
        }

        /// <summary>
        /// Bakes a motion that plays once, from phase 0 to phase 1, and holds
        /// its last pose: a landing.
        /// </summary>
        public static BakedClip BakeOnce(string name, IPoseCycle cycle, HumanoidRig rig, BodyProportions body)
        {
            return Bake(name, cycle, rig, body, false);
        }

        private static BakedClip Bake(string name, IPoseCycle cycle, HumanoidRig rig, BodyProportions body, bool loop)
        {
            // About one sample per frame at 60 fps; a slow idle needs far fewer
            // than its length suggests, a sprint needs a minimum to hold its shape.
            // A loop's last sample is one step short of its first; a clip that
            // plays once needs one at each end.
            int samples = Mathf.Clamp(Mathf.RoundToInt(cycle.Duration * FrameRate), 40, 96) + (loop ? 0 : 1);
            int steps = loop ? samples : samples - 1;
            var muscles = DrivenMuscles();
            var tracks = new float[RootPosition.Length + RootRotation.Length + muscles.Count][];
            for (int i = 0; i < tracks.Length; i++) tracks[i] = new float[samples];

            var previous = Quaternion.identity;
            for (int s = 0; s < samples; s++)
            {
                rig.Apply(cycle.Evaluate((float)s / steps), body);
                var pose = rig.ReadHumanPose();

                // q and -q are the same rotation but interpolate differently;
                // keep each sample on the same side as the one before.
                var rotation = pose.bodyRotation;
                if (s > 0 && Quaternion.Dot(previous, rotation) < 0f)
                {
                    rotation = new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);
                }

                previous = rotation;

                int track = 0;
                for (int axis = 0; axis < 3; axis++) tracks[track++][s] = pose.bodyPosition[axis];
                for (int axis = 0; axis < 4; axis++) tracks[track++][s] = rotation[axis];
                foreach (int muscle in muscles) tracks[track++][s] = pose.muscles[muscle];
            }

            var clip = new BakedClip { Name = name, Cycle = cycle, Duration = cycle.Duration, Loop = loop };
            int index = 0;
            foreach (string property in RootPosition)
            {
                clip.Curves[property] = ReducedCurve(tracks[index++], cycle.Duration, PositionTolerance, loop);
            }

            foreach (string property in RootRotation)
            {
                clip.Curves[property] = ReducedCurve(tracks[index++], cycle.Duration, RotationTolerance, loop);
            }

            foreach (int muscle in muscles)
            {
                clip.Curves[HumanTrait.MuscleName[muscle]] = ReducedCurve(tracks[index++], cycle.Duration, MuscleTolerance, loop);
            }

            return clip;
        }

        /// <summary>A clip that holds one pose, for blend-tree corners and additive references.</summary>
        public static BakedClip BakePose(string name, GaitPose pose, HumanoidRig rig, BodyProportions body)
        {
            return Bake(name, new HeldPose(pose), rig, body);
        }

        /// <summary>
        /// The muscles locomotion owns: spine, neck, head, arms down to the
        /// wrists, and legs. Fingers belong to the gesture layer, eyes and jaw
        /// to the face; a locomotion clip that keyed them would hold them still.
        /// </summary>
        private static List<int> DrivenMuscles()
        {
            var owned = new HashSet<int>();
            for (int bone = (int)HumanBodyBones.Hips; bone <= (int)HumanBodyBones.RightToes; bone++)
            {
                for (int dof = 0; dof < 3; dof++)
                {
                    int muscle = HumanTrait.MuscleFromBone(bone, dof);
                    if (muscle >= 0) owned.Add(muscle);
                }
            }

            // UpperChest was added to the enum after the fingers.
            for (int dof = 0; dof < 3; dof++)
            {
                int muscle = HumanTrait.MuscleFromBone((int)HumanBodyBones.UpperChest, dof);
                if (muscle >= 0) owned.Add(muscle);
            }

            var sorted = new List<int>(owned);
            sorted.Sort();
            return sorted;
        }

        /// <summary>
        /// A curve through evenly spaced samples, with as few keys as
        /// reproduce every sample within the tolerance. A loop's samples
        /// cover one period and the curve closes on itself; otherwise the
        /// first and last samples are the two ends.
        /// </summary>
        private static AnimationCurve ReducedCurve(float[] samples, float duration, float tolerance, bool loop)
        {
            int count = samples.Length;
            int last = loop ? count : count - 1;
            float step = duration / last;
            float Value(int i) => loop ? samples[(i % count + count) % count] : samples[Mathf.Clamp(i, 0, count - 1)];
            // Slopes from the neighbours on both sides. A loop wraps around,
            // so the curve leaves its last key the way it enters its first;
            // an end with no neighbour beyond it takes the slope of its one side.
            float Slope(int i)
            {
                if (loop || (i > 0 && i < last)) return (Value(i + 1) - Value(i - 1)) / (2f * step);
                return i == 0 ? (Value(1) - Value(0)) / step : (Value(last) - Value(last - 1)) / step;
            }

            var keep = new SortedSet<int> { 0, last };
            Refine(0, last);

            var keys = new List<Keyframe>();
            foreach (int i in keep) keys.Add(new Keyframe(i * step, Value(i), Slope(i), Slope(i)));
            return new AnimationCurve(keys.ToArray());

            // Keep the worst sample between two kept keys until the cubic
            // through what is kept matches everything it skipped.
            void Refine(int from, int to)
            {
                int worst = -1;
                float worstError = tolerance;
                float span = (to - from) * step;
                for (int i = from + 1; i < to; i++)
                {
                    float t = (float)(i - from) / (to - from);
                    float error = Mathf.Abs(Hermite(Value(from), Slope(from), Value(to), Slope(to), span, t) - Value(i));
                    if (error > worstError)
                    {
                        worstError = error;
                        worst = i;
                    }
                }

                if (worst < 0) return;
                keep.Add(worst);
                Refine(from, worst);
                Refine(worst, to);
            }
        }

        /// <summary>The cubic Unity draws between two keys with these values and slopes.</summary>
        private static float Hermite(float a, float slopeA, float b, float slopeB, float span, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return (2f * t3 - 3f * t2 + 1f) * a + (t3 - 2f * t2 + t) * span * slopeA
                   + (-2f * t3 + 3f * t2) * b + (t3 - t2) * span * slopeB;
        }

        private sealed class HeldPose : IPoseCycle
        {
            private readonly GaitPose _pose;

            public HeldPose(GaitPose pose)
            {
                _pose = pose;
            }

            // Long enough to be a clip, short enough that nothing waits on it.
            public float Duration => 1f;

            public System.Numerics.Vector3 RootVelocity => System.Numerics.Vector3.Zero;

            public GaitPose Evaluate(float phase) => _pose;
        }
    }
}
