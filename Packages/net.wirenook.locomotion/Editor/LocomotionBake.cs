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

        public BakedClip CrouchIdle;
        public readonly List<MovingClip> Crouching = new List<MovingClip>();

        /// <summary>Off the ground: going up, coming down, and a long drop. Loops.</summary>
        public BakedClip Rising;
        public BakedClip Falling;
        public BakedClip Plunging;

        /// <summary>Coming down onto the feet. Each plays once.</summary>
        public BakedClip SoftLanding;
        public BakedClip HardLanding;

        public LeanClips Lean;

        /// <summary>Every clip that stands or walks on the ground, upright or crouched.</summary>
        public IEnumerable<BakedClip> OnTheGround =>
            new[] { Idle }.Concat(Moving.Select(moving => moving.Clip))
                .Concat(new[] { CrouchIdle }).Concat(Crouching.Select(moving => moving.Clip))
                .Concat(new[] { SoftLanding, HardLanding });

        public IEnumerable<BakedClip> InTheAir => new[] { Rising, Falling, Plunging };
    }

    /// <summary>The whole bake of one style for one rig.</summary>
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
        // A thumbstick produces everything between, and a blend tree only
        // gets the speed right at its samples: between two of them it mixes
        // their stride lengths and their cadences each in a straight line,
        // and the product of the two is not one. So the samples are closest
        // where stride and cadence change fastest with speed.
        //
        // 0.7 m/s anchors the bottom of the range in every direction: stride
        // length is far from proportional to speed there, and blending a
        // 2 m/s walk with the idle would shuffle. 1.2 m/s halves the gap to
        // the walk again, ahead, behind and across. 3 m/s sits between the
        // walk and the run, which are different gaits (one has both feet
        // down twice a stride, the other neither) and mix worst of all; it
        // is not baked sideways, where the strafe speed stops at 2 m/s.
        // 4 m/s sideways and 6 m/s ahead cover worlds that raise the limits.
        public static readonly (string name, float right, float forward)[] Samples =
        {
            ("stroll forward", 0f, 0.7f),
            ("stroll backward", 0f, -0.7f),
            ("stroll right", 0.7f, 0f),
            ("stroll forward right", 0.5f, 0.5f),
            ("stroll backward right", 0.5f, -0.5f),
            ("amble forward", 0f, 1.2f),
            ("amble backward", 0f, -1.2f),
            ("amble right", 1.2f, 0f),
            ("walk forward", 0f, 2f),
            ("walk backward", 0f, -2f),
            ("walk right", 2f, 0f),
            ("walk forward right", 2f, 2f),
            ("walk backward right", 2f, -2f),
            ("jog forward", 0f, 3f),
            ("jog backward", 0f, -3f),
            ("jog forward right", 2f, 3f),
            ("run forward", 0f, 4f),
            ("run backward", 0f, -4f),
            ("run right", 4f, 0f),
            ("run forward right", 2f, 4f),
            ("run backward right", 2f, -4f),
            ("sprint forward", 0f, 6f),
        };

        // The crouched tree, at the velocities VRChat's own crouching tree
        // has its clips: 1.25 m/s every way and a faster one straight ahead,
        // plus a creep so that a slow start is not a blend with standing still.
        public static readonly (string name, float right, float forward)[] CrouchSamples =
        {
            ("crouch creep forward", 0f, 0.6f),
            ("crouch forward", 0f, 1.25f),
            ("crouch fast forward", 0f, 2f),
            ("crouch backward", 0f, -1.25f),
            ("crouch right", 1.25f, 0f),
            ("crouch forward right", 1.25f, 1.25f),
            ("crouch backward right", 1.25f, -1.25f),
        };

        /// <summary>What the style is called; empty when a bake holds only one.</summary>
        public string Name = "";

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
            return Bake(rig, BodyFor(rig, rigSource.name, profile), profile.style, "");
        }

        /// <summary>The body a profile asks for on a rig: measured from it, or the one the asset states.</summary>
        public static BodyProportions BodyFor(HumanoidRig rig, string rigName, GaitProfileAsset profile)
        {
            var body = profile.measureBody ? rig.Measured : profile.body;
            if (!body.IsValid)
            {
                throw new InvalidOperationException(
                    "Could not get usable body proportions for " + rigName + ": leg " + body.LegLength +
                    " m, hips " + body.HipWidth + " m, foot " + body.FootLength + " m, ankle height " + body.AnkleHeight + " m.");
            }

            return body;
        }

        /// <summary>
        /// Bakes one style on a rig that is already set up. A name sets the
        /// style apart from others stored in the same controller.
        /// </summary>
        public static LocomotionBake Bake(HumanoidRig rig, BodyProportions body, GaitProfile style, string name)
        {
            string prefix = string.IsNullOrEmpty(name) ? "" : name.ToLowerInvariant() + ": ";
            return new LocomotionBake
            {
                Name = name,
                Body = body,
                Profile = style,
                Desktop = BakeSet(prefix, style, 1f, rig, body),
                Tracked = BakeSet(prefix + "tracked ", style.ForTrackedUpperBody(), TrackedLeanShare, rig, body),
            };
        }

        private static ClipSet BakeSet(string prefix, GaitProfile style, float leanShare, HumanoidRig rig,
            BodyProportions body)
        {
            BakedClip Loop(string name, IPoseCycle cycle) => HumanoidClipBaker.Bake(prefix + name, cycle, rig, body);
            BakedClip Once(string name, IPoseCycle cycle) => HumanoidClipBaker.BakeOnce(prefix + name, cycle, rig, body);

            var set = new ClipSet
            {
                Idle = Loop("idle", new StandingCycle(style, body)),
                CrouchIdle = Loop("crouch idle", new StandingCycle(style, body, crouched: true)),
                Rising = Loop("air rising", new AirCycle(style, body, AirMoment.Rising)),
                Falling = Loop("air falling", new AirCycle(style, body, AirMoment.Falling)),
                Plunging = Loop("air plunging", new AirCycle(style, body, AirMoment.Plunging)),
                SoftLanding = Once("landing", new LandingCycle(style, body, hard: false)),
                HardLanding = Once("hard landing", new LandingCycle(style, body, hard: true)),
            };

            foreach (var (name, right, forward) in Samples)
            {
                var cycle = new GaitCycle(style, body, GaitRequest.FromVelocity(right, forward));
                set.Moving.Add(new MovingClip { Clip = Loop(name, cycle), Velocity = new Vector2(right, forward) });
            }

            foreach (var (name, right, forward) in CrouchSamples)
            {
                var cycle = new GaitCycle(style, body, GaitRequest.FromVelocity(right, forward), crouched: true);
                set.Crouching.Add(new MovingClip { Clip = Loop(name, cycle), Velocity = new Vector2(right, forward) });
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
                foreach (var clip in set.OnTheGround) yield return clip;
                foreach (var clip in set.InTheAir) yield return clip;
                foreach (var lean in set.Lean.All) yield return lean;
            }
        }

        public string Describe()
        {
            var text = new StringBuilder();
            if (!string.IsNullOrEmpty(Name)) text.Append(Name + ": ");
            text.AppendLine("body: leg " + Body.LegLength.ToString("F3") + " m (thigh " + Body.ThighFraction.ToString("F2") +
                            "), hips " + Body.HipWidth.ToString("F3") + " m, foot " + Body.FootLength.ToString("F3") +
                            " m, ankle height " + Body.AnkleHeight.ToString("F3") + " m");
            int keys = AllClips().Sum(clip => clip.Curves.Values.Sum(curve => curve.length));
            text.AppendLine(AllClips().Count() + " clips, " + keys + " keys");
            return text.ToString();
        }
    }
}
