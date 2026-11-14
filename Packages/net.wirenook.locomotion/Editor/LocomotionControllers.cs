using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Wirenook.Assets.Editor;
using Tracking = VRC.SDKBase.VRC_AnimatorTrackingControl.TrackingType;

namespace Wirenook.Locomotion.Editor
{
    /// <summary>
    /// Generates the two animator controllers that replace VRChat's default
    /// locomotion: the Base layer, which plays the baked gaits, and the
    /// Additive layer, which leans the body with its own inertia.
    /// </summary>
    internal static class LocomotionControllers
    {
        public const string BaseFile = "Locomotion Base.controller";
        public const string AdditiveFile = "Locomotion Additive.controller";

        // VRChat's built-in parameters. Their names are case-sensitive and
        // VRChat converts each to whatever type the controller declares.
        public const string VelocityX = "VelocityX";
        public const string VelocityY = "VelocityY";
        public const string VelocityZ = "VelocityZ";
        public const string AngularY = "AngularY";
        public const string Upright = "Upright";
        public const string Grounded = "Grounded";
        public const string Seated = "Seated";
        public const string Afk = "AFK";
        public const string VRMode = "VRMode";
        public const string TrackingType = "TrackingType";

        // Parameters the controllers compute for themselves. They are not in
        // any expression parameter list, so they are neither synced nor
        // shared between playable layers; each controller that needs one
        // derives it locally, on every client, from the built-ins.
        private const string Prefix = "Wirenook/Locomotion/";
        public const string Cadence = Prefix + "Cadence";
        public const string One = Prefix + "One";
        public const string Clock = Prefix + "Clock";
        public const string LastClock = Prefix + "LastClock";
        public const string FrameTime = Prefix + "FrameTime";
        public const string SmoothX = Prefix + "SmoothX";
        public const string SmoothZ = Prefix + "SmoothZ";
        public const string SmoothTurn = Prefix + "SmoothTurn";
        public const string PushX = Prefix + "PushX";
        public const string PushZ = Prefix + "PushZ";

        // VRChat substitutes its own copy for any clip called proxy_* from
        // this folder, so these cost the avatar nothing to reference.
        private const string ProxyFolder =
            "Packages/com.vrchat.avatars/Samples/AV3 Demo Assets/Animation/ProxyAnim";

        // TrackingType 3 is head and hands; 4 and up add hips or feet.
        private const int HeadAndHands = 3;

        // The clock counts seconds from 0 to this and starts over. Short
        // enough that a float still resolves a frame time to a fraction of a percent.
        private const float ClockPeriod = 10f;

        // Past the fastest baked gait the cycle plays faster in proportion,
        // up to this multiple of it.
        private const float FastestCadence = 4f;

        // Widest inputs the smoothing passes through unclipped.
        private const float VelocityRange = 32f;
        private const float TurnRange = 720f;

        public static AnimatorController BuildBase(LocomotionBake bake, string path)
        {
            var builder = ControllerBuilder.Rebuild(path);
            builder.Float(VelocityX).Float(VelocityY).Float(VelocityZ).Float(Upright, 1f)
                .Bool(Grounded, true).Bool(Seated).Bool(Afk)
                // VRMode is an integer in VRChat. Declared as a float it can
                // drive a blend tree, which picks the clip set without adding states.
                .Float(VRMode)
                .Float(Cadence, 1f);
            var trees = new Trees(builder);

            var standing = trees.Between("Standing", VRMode,
                0f, GaitTree(builder, trees, bake.Desktop, "Standing (desktop)"),
                1f, GaitTree(builder, trees, bake.Tracked, "Standing (tracked)"));

            BuildLocomotionLayer(builder, standing, CrouchingTree(trees), ProneTree(trees));
            builder.Layer("Cadence").State("Cadence", CadenceTree(builder, trees, bake.Desktop));
            builder.Save();
            return builder.Controller;
        }

        public static AnimatorController BuildAdditive(LocomotionBake bake, string path)
        {
            var builder = ControllerBuilder.Rebuild(path);
            builder.Float(VelocityX).Float(VelocityZ).Float(AngularY).Int(VRMode).Int(TrackingType)
                .Float(One, 1f).Float(Clock).Float(LastClock).Float(FrameTime)
                .Float(SmoothX).Float(SmoothZ).Float(SmoothTurn).Float(PushX).Float(PushZ);
            var trees = new Trees(builder);

            // A clip alone in a state plays in real time, which a clip inside
            // a direct blend tree does not: such a tree runs its children
            // slower by the sum of their weights.
            builder.Layer("Clock").State("Clock", builder.Clip("clock", clip => clip
                .Parameter(Clock, AnimationCurve.Linear(0f, 0f, ClockPeriod, ClockPeriod)).Loop()));
            builder.Layer("Inertia").State("Inertia", InertiaTree(builder, trees, bake.Profile));

            var desktop = LeanClips(builder, bake.Desktop.Lean);
            var tracked = LeanClips(builder, bake.Tracked.Lean);
            var profile = bake.Profile;

            // Lean into acceleration: the difference between where the
            // velocity is and where the smoothed copy has got to.
            float fullPush = profile.MaxLean / Mathf.Max(profile.AccelerationLean, 0.01f);
            BlendTree Lean(string name, Dictionary<string, AnimationClip> clips) => trees.Blend2D(name,
                BlendTreeType.SimpleDirectional2D, PushX, PushZ, new[]
                {
                    new TreeChild(clips["neutral"], 0f, 0f),
                    new TreeChild(clips["forward"], 0f, fullPush),
                    new TreeChild(clips["back"], 0f, -fullPush),
                    new TreeChild(clips["right"], fullPush, 0f),
                    new TreeChild(clips["left"], -fullPush, 0f),
                });

            // Bank into turns: proportional to turn rate times forward speed,
            // which is what the corners of this grid interpolate to.
            BlendTree Bank(string name, Dictionary<string, AnimationClip> clips) => trees.Blend2D(name,
                BlendTreeType.FreeformCartesian2D, SmoothTurn, SmoothZ, new[]
                {
                    new TreeChild(clips["neutral"], -profile.TurnBankRate, 0f),
                    new TreeChild(clips["neutral"], 0f, 0f),
                    new TreeChild(clips["neutral"], profile.TurnBankRate, 0f),
                    new TreeChild(clips["bank left"], -profile.TurnBankRate, profile.TurnBankSpeed),
                    new TreeChild(clips["neutral"], 0f, profile.TurnBankSpeed),
                    new TreeChild(clips["bank right"], profile.TurnBankRate, profile.TurnBankSpeed),
                });

            BuildAudienceLayer(builder, "Lean", Lean("Lean (desktop)", desktop), Lean("Lean (tracked)", tracked),
                desktop["neutral"]);
            BuildAudienceLayer(builder, "Bank", Bank("Bank (desktop)", desktop), Bank("Bank (tracked)", tracked),
                desktop["neutral"]);
            builder.Save();
            return builder.Controller;
        }

        // ---- Base ---------------------------------------------------------

        /// <summary>
        /// The standing blend tree for one clip set: every baked gait at the
        /// velocity it was baked for, on VRChat's own velocity parameters.
        /// </summary>
        private static BlendTree GaitTree(ControllerBuilder builder, Trees trees, ClipSet set, string name)
        {
            var children = new List<TreeChild> { new TreeChild(Store(builder, set.Idle), 0f, 0f) };
            foreach (var moving in set.Moving)
            {
                var clip = Store(builder, moving.Clip);
                children.Add(new TreeChild(clip, moving.Velocity.x, moving.Velocity.y));
                if (moving.Velocity.x > 0f)
                {
                    children.Add(new TreeChild(clip, -moving.Velocity.x, moving.Velocity.y, mirrored: true));
                }
            }

            return trees.Blend2D(name, BlendTreeType.FreeformDirectional2D, VelocityX, VelocityZ, children);
        }

        /// <summary>
        /// A playback-speed multiplier: 1 inside the fastest baked speeds,
        /// rising in proportion to speed beyond them. A blend tree cannot
        /// extrapolate, so without this a world that moves players faster
        /// than the fastest clip would slide the feet; with it the fastest
        /// clip's cadence rises to keep up.
        ///
        /// The tree is a grid over velocity with the multiplier at each
        /// corner, which a Cartesian blend interpolates linearly along the
        /// grid lines: exact for travel straight ahead, back or sideways, an
        /// overestimate of up to a third on fast diagonals.
        /// </summary>
        private static BlendTree CadenceTree(ControllerBuilder builder, Trees trees, ClipSet set)
        {
            float ahead = set.Moving.Max(moving => moving.Velocity.y);
            float behind = -set.Moving.Min(moving => moving.Velocity.y);
            float sideways = set.Moving.Max(moving => moving.Velocity.x);
            var normal = builder.Clip("cadence x1", clip => clip.Parameter(Cadence, 1f));
            var fastest = builder.Clip("cadence x" + FastestCadence, clip => clip.Parameter(Cadence, FastestCadence));

            float[] across = { -FastestCadence * sideways, -sideways, 0f, sideways, FastestCadence * sideways };
            float[] along = { -FastestCadence * behind, -behind, 0f, ahead, FastestCadence * ahead };
            var children = new List<TreeChild>();
            for (int x = 0; x < across.Length; x++)
            {
                for (int z = 0; z < along.Length; z++)
                {
                    bool beyond = x == 0 || x == across.Length - 1 || z == 0 || z == along.Length - 1;
                    children.Add(new TreeChild(beyond ? fastest : normal, across[x], along[z]));
                }
            }

            return trees.Blend2D("Cadence", BlendTreeType.FreeformCartesian2D, VelocityX, VelocityZ, children);
        }

        /// <summary>
        /// The state machine of VRChat's default locomotion controller, with
        /// the same thresholds and timings, around the generated standing
        /// tree. Its jump-and-fall sub-machine is flattened into the layer; an
        /// exit from it there is a transition to Standing here.
        /// </summary>
        private static void BuildLocomotionLayer(ControllerBuilder builder, Motion standingTree, Motion crouchingTree,
            Motion proneTree)
        {
            var layer = builder.Layer("Locomotion");
            var standing = layer.State("Standing", standingTree);
            standing.State.speedParameterActive = true;
            standing.State.speedParameter = Cadence;
            var crouching = layer.State("Crouching", crouchingTree);
            var prone = layer.State("Prone", proneTree);

            var smallHop = layer.State("SmallHop", Proxy("fall_short"));
            var shortFall = layer.State("Short Fall", Proxy("fall_short"));
            var longFall = layer.State("LongFall", Proxy("fall_long"));
            var quickLand = layer.State("QuickLand", Proxy("land_quick"));
            var hardLand = layer.State("HardLand", Proxy("landing"));
            var restoreTracking = layer.State("RestoreTracking", Proxy("stand_still"));
            var restoreToHop = layer.State("RestoreToHop", Proxy("fall_short"));

            // What the default controller hands to animation while airborne,
            // and gives back on the way out. Standing, crouching and prone
            // never touch tracking: the avatar's Force Locomotion Animations
            // option decides whether the gaits play in full-body tracking.
            Track(smallHop, Tracking.Tracking, Tracking.Tracking, Tracking.Tracking);
            Track(shortFall, Tracking.Tracking, Tracking.Tracking, Tracking.Animation);
            Track(longFall, Tracking.Animation, Tracking.Animation, Tracking.Animation);
            Track(quickLand, Tracking.Animation, Tracking.NoChange, Tracking.Animation);
            Track(hardLand, Tracking.Animation, Tracking.NoChange, Tracking.Animation);
            Track(restoreTracking, Tracking.Tracking, Tracking.Tracking, Tracking.Tracking);
            Track(restoreToHop, Tracking.Tracking, Tracking.Tracking, Tracking.Tracking);

            standing.To(crouching).Fade(0.5f).Less(Upright, 0.68f);
            crouching.To(standing).Fade(0.2f).Greater(Upright, 0.7f);
            crouching.To(prone).Fade(0.5f).Less(Upright, 0.41f);
            prone.To(crouching).Fade(0.5f).Greater(Upright, 0.43f);

            standing.To(smallHop).Fade(0.1f)
                .IfFalse(Grounded).Greater(VelocityY, -2.001f).IfFalse(Seated).IfFalse(Afk);
            standing.To(shortFall).Fade(0.1f)
                .IfFalse(Grounded).Greater(VelocityY, -4f).Less(VelocityY, -2f).IfFalse(Seated).IfFalse(Afk);
            standing.To(longFall).Fade(0.2f)
                .IfFalse(Grounded).Less(VelocityY, -20f).IfFalse(Seated).IfFalse(Afk);

            smallHop.To(standing).Fade(0.25f).IfTrue(Grounded).Or().IfTrue(Seated).Or().IfTrue(Afk);
            smallHop.To(shortFall).Less(VelocityY, -2f);

            shortFall.To(restoreTracking).Fade(0.1f).IfTrue(Seated).Or().IfTrue(Afk);
            shortFall.To(longFall).Less(VelocityY, -20f);
            shortFall.To(restoreToHop).Greater(VelocityY, 0.01f);
            shortFall.To(quickLand).Fade(0.1f).IfTrue(Grounded);

            longFall.To(hardLand).IfTrue(Grounded);
            longFall.To(shortFall).Greater(VelocityY, -20f);
            longFall.To(restoreTracking).Fade(0.1f).IfTrue(Afk).Or().IfTrue(Seated);

            hardLand.To(restoreTracking).Fade(0.2f).AfterTime(0.6f);
            quickLand.To(restoreTracking).Fade(0.1f).AfterTime(1f);
            restoreTracking.To(standing).Fade(0.25f).AfterTime(0f);

            restoreToHop.To(shortFall).Less(VelocityY, -2f);
            restoreToHop.To(standing).Fade(0.25f).IfTrue(Afk).Or().IfTrue(Seated).Or().IfTrue(Grounded);
        }

        private static void Track(StateBuilder state, Tracking head, Tracking hands, Tracking hipAndFeet)
        {
            state.Behaviour<VRCAnimatorTrackingControl>(control =>
            {
                control.trackingHead = head;
                control.trackingLeftHand = hands;
                control.trackingRightHand = hands;
                control.trackingHip = hipAndFeet;
                control.trackingLeftFoot = hipAndFeet;
                control.trackingRightFoot = hipAndFeet;
            });
        }

        // Crouching and prone are VRChat's own trees, rebuilt child for child.
        private static BlendTree CrouchingTree(Trees trees)
        {
            var forward = Proxy("crouch_walk_forward");
            var right = Proxy("crouch_walk_right");
            var right45 = Proxy("crouch_walk_right_45");
            var right135 = Proxy("crouch_walk_right_135");
            return trees.Blend2D("Crouching", BlendTreeType.FreeformDirectional2D, VelocityX, VelocityZ, new[]
            {
                new TreeChild(Proxy("crouch_still"), 0f, 0f),
                new TreeChild(forward, 0f, 1.78f),
                new TreeChild(forward, 0f, 1.25f),
                new TreeChild(forward, 0f, -1.25f, -1f, mirrored: true),
                new TreeChild(right, 1.25f, 0f),
                new TreeChild(right, -1.25f, 0f, mirrored: true),
                new TreeChild(right45, 1.25f, 1.25f),
                new TreeChild(right45, -1.25f, 1.25f, mirrored: true),
                new TreeChild(right135, 1.25f, -1.25f),
                new TreeChild(right135, -1.25f, -1.25f, mirrored: true),
            });
        }

        private static BlendTree ProneTree(Trees trees)
        {
            var forward = Proxy("low_crawl_forward");
            var right = Proxy("low_crawl_right");
            return trees.Blend2D("Prone", BlendTreeType.FreeformDirectional2D, VelocityX, VelocityZ, new[]
            {
                new TreeChild(Proxy("low_crawl_still"), 0f, 0f),
                new TreeChild(forward, 0f, 0.1f, 0.5f),
                new TreeChild(forward, 0f, 1f, 2f),
                new TreeChild(forward, 0f, -0.1f, -0.5f, mirrored: true),
                new TreeChild(forward, 0f, -1f, -2f, mirrored: true),
                new TreeChild(right, 0.1f, 0f, 0.5f),
                new TreeChild(right, -0.1f, 0f, 0.5f, mirrored: true),
                new TreeChild(right, 1f, 0f, 2f),
                new TreeChild(right, -1f, 0f, 2f, mirrored: true),
            });
        }

        private static AnimationClip Proxy(string name)
        {
            string path = ProxyFolder + "/proxy_" + name + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                throw new InvalidOperationException(
                    "The VRChat Avatars SDK's sample animation " + path + " is missing; reinstall com.vrchat.avatars.");
            }

            return clip;
        }

        private static AnimationClip Store(ControllerBuilder builder, BakedClip baked, AnimationClip additiveReference = null)
        {
            return builder.Clip(baked.Name, clip => baked.WriteTo(clip.Clip, additiveReference));
        }

        // ---- Additive -----------------------------------------------------

        /// <summary>
        /// One blend tree that keeps the controller's derived parameters up to
        /// date, each frame, using nothing but animation: a clip can animate
        /// an animator parameter, and a blend tree reads parameters as they
        /// were at the end of the previous frame. Blending "the raw value"
        /// with "my own previous value" therefore moves a parameter part of
        /// the way toward its target every frame, which is a low-pass filter.
        ///
        /// The share taken per frame decides the filter's time constant, and
        /// a fixed share would make that constant depend on frame rate (three
        /// times longer at 30 fps than at 90). So the tree first measures the
        /// frame time, as the difference between the clock parameter and the
        /// copy of it made one frame earlier, and takes
        /// frameTime / timeConstant of the way. That is one Euler step of the
        /// filter's differential equation: the time constant holds to first
        /// order at any frame rate, the share is capped at 1 when a frame is
        /// longer than the time constant, and the one frame in which the
        /// clock wraps reads as negative, clamps to zero and simply holds.
        /// </summary>
        private static BlendTree InertiaTree(ControllerBuilder builder, Trees trees, GaitProfile profile)
        {
            AnimationClip Hold(string parameter, float value) => builder.Clip(
                parameter.Substring(Prefix.Length) + " = " + value, clip => clip.Parameter(parameter, value));

            // Copies one parameter into another, optionally negated: a 1D
            // tree is the identity function when its two ends write their own thresholds.
            BlendTree Copy(string name, string from, string to, float low, float high, float sign = 1f) =>
                trees.Between(name, from, low, Hold(to, sign * low), high, Hold(to, sign * high));

            BlendTree Smooth(string name, string raw, string smoothed, float range) => trees.Between(
                "smooth " + name, FrameTime,
                0f, Copy("keep " + name, smoothed, smoothed, -range, range),
                profile.InertiaTime, Copy("follow " + name, raw, smoothed, -range, range));

            // Children of a direct tree at full weight add up, so two copies
            // into one parameter, one of them negated, make a difference.
            return trees.Sum("Inertia", One,
                Copy("remember clock", Clock, LastClock, 0f, ClockPeriod),
                Copy("frame time: clock", Clock, FrameTime, 0f, ClockPeriod),
                Copy("frame time: minus last clock", LastClock, FrameTime, 0f, ClockPeriod, -1f),
                Smooth("sideways", VelocityX, SmoothX, VelocityRange),
                Smooth("forward", VelocityZ, SmoothZ, VelocityRange),
                Smooth("turn", AngularY, SmoothTurn, TurnRange),
                Copy("push sideways: velocity", VelocityX, PushX, -VelocityRange, VelocityRange),
                Copy("push sideways: minus smoothed", SmoothX, PushX, -VelocityRange, VelocityRange, -1f),
                Copy("push forward: velocity", VelocityZ, PushZ, -VelocityRange, VelocityRange),
                Copy("push forward: minus smoothed", SmoothZ, PushZ, -VelocityRange, VelocityRange, -1f));
        }

        private static Dictionary<string, AnimationClip> LeanClips(ControllerBuilder builder, LeanClips lean)
        {
            // An additive layer applies each clip's difference from its
            // reference pose. Without one, a clip is compared with its own
            // first frame, and a held pose differs from itself by nothing.
            var neutral = Store(builder, lean.Neutral);
            return new Dictionary<string, AnimationClip>
            {
                ["neutral"] = neutral,
                ["forward"] = Store(builder, lean.Forward, neutral),
                ["back"] = Store(builder, lean.Back, neutral),
                ["left"] = Store(builder, lean.Left, neutral),
                ["right"] = Store(builder, lean.Right, neutral),
                ["bank left"] = Store(builder, lean.BankLeft, neutral),
                ["bank right"] = Store(builder, lean.BankRight, neutral),
            };
        }

        /// <summary>
        /// An additive layer with one state per audience. Desktop players get
        /// the whole lean. With head and hands tracked it is cut down to what
        /// the spine can absorb. With hips or feet tracked there is none: the
        /// player's own body is doing the leaning.
        /// </summary>
        private static void BuildAudienceLayer(ControllerBuilder builder, string name, Motion desktopMotion,
            Motion trackedMotion, Motion still)
        {
            var layer = builder.Layer(name, 1f, null, AnimatorLayerBlendingMode.Additive);
            var desktop = layer.State("Desktop", desktopMotion);
            var tracked = layer.State("Head and hands tracked", trackedMotion);
            var fullBody = layer.State("Body tracked", still);

            // TrackingType passes through 0 to 2 while an avatar loads; those
            // values match no condition here, so the layer stays where it is.
            const float fade = 0.25f;
            desktop.To(tracked).Fade(fade).Equal(VRMode, 1).Equal(TrackingType, HeadAndHands);
            desktop.To(fullBody).Fade(fade).Equal(VRMode, 1).Greater(TrackingType, HeadAndHands);
            tracked.To(desktop).Fade(fade).Equal(VRMode, 0);
            tracked.To(fullBody).Fade(fade).Equal(VRMode, 1).Greater(TrackingType, HeadAndHands);
            fullBody.To(desktop).Fade(fade).Equal(VRMode, 0);
            fullBody.To(tracked).Fade(fade).Equal(VRMode, 1).Equal(TrackingType, HeadAndHands);
        }
    }
}
