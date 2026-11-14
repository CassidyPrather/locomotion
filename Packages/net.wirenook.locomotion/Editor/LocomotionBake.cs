using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Wirenook.Locomotion.Editor
{
    /// <summary>A baked gait and the velocity, in m/s, at which its feet match the ground.</summary>
    internal sealed class MovingClip
    {
        public BakedClip Clip;

        /// <summary>X is rightward speed, Y forward speed: the blend tree position.</summary>
        public Vector2 Velocity;
    }

    /// <summary>Standing poses for the additive lean: neutral, and leaning each way by the full amount.</summary>
    internal sealed class LeanClips
    {
        public BakedClip Neutral;
        public BakedClip Forward;
        public BakedClip Back;
        public BakedClip Left;
        public BakedClip Right;
        public BakedClip BankLeft;
        public BakedClip BankRight;

        public IEnumerable<BakedClip> All => new[] { Neutral, Forward, Back, Left, Right, BankLeft, BankRight };
    }

    /// <summary>Everything one audience (desktop, or a tracked upper body) plays.</summary>
    internal sealed class ClipSet
    {
        public BakedClip Idle;
        public readonly List<MovingClip> Moving = new List<MovingClip>();
        public LeanClips Lean;
    }

    /// <summary>The whole bake for one rig.</summary>
    internal sealed class LocomotionBake
    {
        /// <summary>
        /// How much of the full lean a body with tracked head and hands gets.
        /// The chest hangs between a pelvis the animation moves and a head it
        /// cannot, so only a hint survives without bending the spine oddly.
        /// </summary>
        public const float TrackedLeanShare = 0.35f;

        // Velocities baked for travel ahead, behind and to the right, in m/s.
        // Leftward travel is the same clips mirrored half a cycle later, which
        // the gait's symmetry makes exact, so it is not baked twice.
        //
        // VRChat's defaults are 2 m/s walking, 4 running and 2 strafing, and
        // the strafe speed caps sideways motion even while running, so the
        // diagonals a desktop player actually produces are (2, 2) and (2, 4).
        // 0.7 m/s anchors the bottom of the range: stride length is far from
        // proportional to speed there, and blending a 2 m/s walk with the idle
        // would shuffle. Straight ahead, where a thumbstick spends most of its
        // time, 1.2 m/s halves the gap again. 4 m/s sideways and 6 m/s ahead
        // cover worlds that raise the limits.
        public static readonly (string name, float right, float forward)[] Samples =
        {
            ("stroll forward", 0f, 0.7f),
            ("stroll backward", 0f, -0.7f),
            ("stroll right", 0.7f, 0f),
            ("amble forward", 0f, 1.2f),
            ("walk forward", 0f, 2f),
            ("walk backward", 0f, -2f),
            ("walk right", 2f, 0f),
            ("walk forward right", 2f, 2f),
            ("walk backward right", 2f, -2f),
            ("run forward", 0f, 4f),
            ("run backward", 0f, -4f),
            ("run right", 4f, 0f),
            ("run forward right", 2f, 4f),
            ("run backward right", 2f, -4f),
            ("sprint forward", 0f, 6f),
        };

        public BodyProportions Body;
        public GaitProfile Profile;
        public ClipSet Desktop;
        public ClipSet Tracked;

        /// <summary>
        /// Bakes every clip for a humanoid model or prefab. The body is
        /// measured from the rig unless the profile states one.
        /// </summary>
        public static LocomotionBake Bake(GameObject rigSource, GaitProfileAsset profile)
        {
            using var rig = new HumanoidRig(rigSource);
            var body = profile.measureBody ? rig.Measured : profile.body;
            if (!body.IsValid)
            {
                throw new InvalidOperationException(
                    "Could not get usable body proportions for " + rigSource.name + ": leg " + body.LegLength +
                    " m, hips " + body.HipWidth + " m, foot " + body.FootLength + " m, ankle height " + body.AnkleHeight + " m.");
            }

            var style = profile.style;
            return new LocomotionBake
            {
                Body = body,
                Profile = style,
                Desktop = BakeSet("", style, 1f, rig, body),
                Tracked = BakeSet("tracked ", style.ForTrackedUpperBody(), TrackedLeanShare, rig, body),
            };
        }

        private static ClipSet BakeSet(string prefix, GaitProfile style, float leanShare, HumanoidRig rig,
            BodyProportions body)
        {
            var set = new ClipSet
            {
                Idle = HumanoidClipBaker.Bake(prefix + "idle", new StandingCycle(style, body), rig, body),
            };

            foreach (var (name, right, forward) in Samples)
            {
                var cycle = new GaitCycle(style, body, GaitRequest.FromVelocity(right, forward));
                set.Moving.Add(new MovingClip
                {
                    Clip = HumanoidClipBaker.Bake(prefix + name, cycle, rig, body),
                    Velocity = new Vector2(right, forward),
                });
            }

            float lean = style.MaxLean * Rot.Deg * leanShare;
            float bank = style.TurnBank * Rot.Deg * leanShare;
            BakedClip Pose(string name, float forward, float right)
            {
                return HumanoidClipBaker.BakePose(prefix + name, Posture.Stand(style, body, forward, right), rig, body);
            }

            set.Lean = new LeanClips
            {
                Neutral = Pose("lean neutral", 0f, 0f),
                Forward = Pose("lean forward", lean, 0f),
                Back = Pose("lean back", -lean, 0f),
                Left = Pose("lean left", 0f, -lean),
                Right = Pose("lean right", 0f, lean),
                BankLeft = Pose("bank left", 0f, -bank),
                BankRight = Pose("bank right", 0f, bank),
            };
            return set;
        }

        public IEnumerable<BakedClip> AllClips()
        {
            foreach (var set in new[] { Desktop, Tracked })
            {
                yield return set.Idle;
                foreach (var moving in set.Moving) yield return moving.Clip;
                foreach (var lean in set.Lean.All) yield return lean;
            }
        }

        public string Describe()
        {
            var text = new StringBuilder();
            text.AppendLine("body: leg " + Body.LegLength.ToString("F3") + " m (thigh " + Body.ThighFraction.ToString("F2") +
                            "), hips " + Body.HipWidth.ToString("F3") + " m, foot " + Body.FootLength.ToString("F3") +
                            " m, ankle height " + Body.AnkleHeight.ToString("F3") + " m");
            int keys = AllClips().Sum(clip => clip.Curves.Values.Sum(curve => curve.length));
            text.AppendLine(AllClips().Count() + " clips, " + keys + " keys");
            return text.ToString();
        }
    }
}
