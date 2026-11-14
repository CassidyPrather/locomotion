using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Wirenook.Locomotion.Editor
{
    /// <summary>
    /// Checks a bake the only way that settles it: turn the curves back into
    /// clips, sample them onto a fresh copy of the rig and measure where the
    /// feet actually are. That exercises the curve names, the muscle
    /// conversion, the key reduction and the clip settings together.
    /// </summary>
    internal static class BakeVerifier
    {
        /// <summary>How far a sampled ankle may be from where the gait model put it, metres.</summary>
        public const float AnkleTolerance = 0.02f;

        /// <summary>How far below the ground a heel or ball of a foot may dip, metres.</summary>
        public const float GroundTolerance = 0.01f;

        /// <summary>How far a flat foot may creep over the ground during one stance, metres.</summary>
        public const float SlipTolerance = 0.01f;

        private const int SamplesPerCycle = 60;

        public sealed class Result
        {
            public bool Passed = true;
            public float WorstAnkleError;
            public float LowestSole = float.MaxValue;
            public float WorstSlip;
            public readonly StringBuilder Report = new StringBuilder();
        }

        public static Result Verify(LocomotionBake bake, GameObject rigSource)
        {
            var result = new Result();
            using var rig = new HumanoidRig(rigSource);
            foreach (var set in new[] { bake.Desktop, bake.Tracked })
            {
                VerifyClip(set.Idle, bake.Body, rig, result);
                foreach (var moving in set.Moving) VerifyClip(moving.Clip, bake.Body, rig, result);
            }

            result.Report.Insert(0,
                (result.Passed ? "PASS" : "FAIL") + ": worst ankle error " + Millimetres(result.WorstAnkleError) +
                " (limit " + Millimetres(AnkleTolerance) + "), lowest sole point " + Millimetres(result.LowestSole) +
                " (limit " + Millimetres(-GroundTolerance) + "), worst stance slip " + Millimetres(result.WorstSlip) +
                " (limit " + Millimetres(SlipTolerance) + ")\n");
            return result;
        }

        private static void VerifyClip(BakedClip baked, BodyProportions body, HumanoidRig rig, Result result)
        {
            var clip = new AnimationClip { name = baked.Name, hideFlags = HideFlags.HideAndDontSave };
            try
            {
                baked.WriteTo(clip);
                float ankleError = 0f, lowestSole = float.MaxValue, slip = 0f;
                var velocity = baked.Cycle.RootVelocity.ToUnity();
                var planted = new Dictionary<Side, Vector3>();

                for (int i = 0; i < SamplesPerCycle; i++)
                {
                    // Offset from the keys, so the curves between them are what gets tested.
                    float phase = (i + 0.37f) / SamplesPerCycle;
                    float time = phase * baked.Duration;
                    rig.Sample(clip, time);
                    var expected = rig.Fit(baked.Cycle.Evaluate(phase), body);

                    foreach (var side in new[] { Side.Left, Side.Right })
                    {
                        var ankle = rig.Ankle(side);
                        var foot = expected.Foot(side);
                        ankleError = Mathf.Max(ankleError, (ankle - foot.Ankle.ToUnity()).magnitude);
                        lowestSole = Mathf.Min(lowestSole, rig.LowestSoleHeight(side));

                        // A flat foot is fixed to the ground: adding back the
                        // distance the root has travelled must give the same
                        // point for as long as the foot stays down.
                        if (foot.Contact != FootContact.Flat)
                        {
                            planted.Remove(side);
                            continue;
                        }

                        var onGround = ankle + velocity * time;
                        if (planted.TryGetValue(side, out var first))
                        {
                            slip = Mathf.Max(slip, (onGround - first).magnitude);
                        }
                        else
                        {
                            planted[side] = onGround;
                        }
                    }
                }

                bool passed = ankleError <= AnkleTolerance && lowestSole >= -GroundTolerance && slip <= SlipTolerance;
                result.Passed &= passed;
                result.WorstAnkleError = Mathf.Max(result.WorstAnkleError, ankleError);
                result.LowestSole = Mathf.Min(result.LowestSole, lowestSole);
                result.WorstSlip = Mathf.Max(result.WorstSlip, slip);
                result.Report.AppendLine(
                    (passed ? "  ok   " : "  FAIL ") + baked.Name.PadRight(30) + " ankle " + Millimetres(ankleError) +
                    "  sole " + Millimetres(lowestSole) + "  slip " + Millimetres(slip) + "  " +
                    baked.Duration.ToString("F2") + " s, " + baked.Curves.Values.Sum(curve => curve.length) + " keys");
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        private static string Millimetres(float metres) => (metres * 1000f).ToString("F1") + " mm";
    }
}
