using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Wirenook.Assets.Editor;

namespace Wirenook.Locomotion.Editor
{
    /// <summary>One moving motion of a custom set, and the velocity it is made for.</summary>
    public sealed class CustomGait
    {
        public string Name;

        /// <summary>Metres per second to the right; only zero and rightward ones are needed, leftward is mirrored.</summary>
        public float Right;

        /// <summary>Metres per second ahead.</summary>
        public float Forward;

        public IPoseCycle Cycle;

        public CustomGait(string name, float right, float forward, IPoseCycle cycle)
        {
            Name = name;
            Right = right;
            Forward = forward;
            Cycle = cycle;
        }
    }

    /// <summary>
    /// Everything one audience plays (a desktop player, or one whose head
    /// and hands are tracked), as motions of the caller's own making. The
    /// package's own walk is a <see cref="GaitCycle"/> at every velocity;
    /// this is for a body that should move some other way.
    ///
    /// A motion that is the same object in both audiences' sets is baked
    /// and stored once, under the name the desktop set gives it.
    /// </summary>
    public sealed class CustomMotions
    {
        public IPoseCycle Idle;
        public readonly List<CustomGait> Moving = new List<CustomGait>();
        public IPoseCycle CrouchIdle;
        public readonly List<CustomGait> Crouching = new List<CustomGait>();

        /// <summary>Lying down, whatever the speed. Null plays VRChat's own (see <see cref="CustomLocomotionOptions.ProneLiesStill"/>).</summary>
        public IPoseCycle Prone;

        /// <summary>Off the ground: going up, coming down, and a long drop. Loops.</summary>
        public IPoseCycle Rising;
        public IPoseCycle Falling;
        public IPoseCycle Plunging;

        /// <summary>Coming down onto the feet. Each plays once, and the way out of it waits for it.</summary>
        public IPoseCycle SoftLanding;
        public IPoseCycle HardLanding;

        /// <summary>
        /// A loop laid over standing still, as its difference from
        /// <see cref="Idle"/> at phase 0. Only a desktop player's is used. May be null.
        /// </summary>
        public IPoseCycle Fidget;
    }

    public sealed class CustomLocomotionOptions
    {
        /// <summary>The humanoid model or prefab the motions are baked on.</summary>
        public string RigPath;

        /// <summary>Where the controllers go.</summary>
        public string Folder;

        public string PrefabPath;

        /// <summary>The feature's id and label, as <see cref="FeaturePrefab.Describe"/> takes them.</summary>
        public string Id = "locomotion";
        public string Label = "Locomotion";

        /// <summary>See <see cref="LocomotionBake.Blend"/>.</summary>
        public BlendTreeType Blend = BlendTreeType.FreeformDirectional2D;

        /// <summary>
        /// With no prone motion of its own: whether the body lies in VRChat's
        /// still prone pose at any speed, and so slides, or crawls as VRChat's default does.
        /// </summary>
        public bool ProneLiesStill;

        /// <summary>Velocities (x right, y ahead) at which to report how well a planted foot holds the ground.</summary>
        public readonly List<Vector2> FootSpeedsAt = new List<Vector2>();
    }

    /// <summary>
    /// Locomotion from motions of the caller's own: baked on a rig with the
    /// package's baker and played by the package's state machine, which is
    /// VRChat's default one (standing, crouching, prone, the hop, the
    /// falls, the landings, and what each hands to tracking). What it
    /// leaves out is everything that makes the package's own walk a
    /// person's: the gait model, the cadence that rises with speed and the
    /// lean of the Additive layer, and the checks that hold those to it.
    /// </summary>
    public static class CustomLocomotion
    {
        public const string BaseFile = LocomotionControllers.BaseFile;
        public const string AdditiveFile = LocomotionControllers.AdditiveFile;

        /// <summary>The lengths of a rig's legs and feet, which motions are written against.</summary>
        public static BodyProportions Measure(string rigPath)
        {
            using var rig = new HumanoidRig(LoadRig(rigPath));
            var body = rig.Measured;
            if (!body.IsValid) throw new InvalidOperationException("Could not measure usable body proportions from " + rigPath + ".");
            return body;
        }

        /// <summary>
        /// Bakes both sets, checks what was baked, writes the two
        /// controllers and the prefab that installs them, and checks the
        /// controllers by running them. Returns a report; throws, having
        /// written nothing a build would use, when a check fails.
        /// </summary>
        public static string Build(CustomMotions desktop, CustomMotions tracked, CustomLocomotionOptions options)
        {
            var rigSource = LoadRig(options.RigPath);
            LocomotionBake bake;
            BakedClip fidget;
            using (var rig = new HumanoidRig(rigSource))
            {
                var body = rig.Measured;
                var shared = new Dictionary<IPoseCycle, BakedClip>();
                bake = new LocomotionBake
                {
                    Body = body,
                    Blend = options.Blend,
                    ScaleCadence = false,
                    ProneLiesStill = options.ProneLiesStill,
                    Desktop = BakeSet("", desktop, rig, body, shared),
                    Tracked = BakeSet("tracked ", tracked, rig, body, shared),
                };
                fidget = desktop.Fidget != null ? HumanoidClipBaker.Bake("fidget", desktop.Fidget, rig, body) : null;
            }

            // Where the feet are, whether they go through the ground and
            // whether a planted one creeps: true of any motion, not of a person's.
            var baked = BakeVerifier.Verify(bake, rigSource);
            if (!baked.Passed)
            {
                throw new InvalidOperationException("The bake for " + options.RigPath + " did not pass its checks; nothing was written.\n" + baked.Report);
            }

            string folder = options.Folder.Replace('\\', '/').TrimEnd('/');
            AssetFolders.Ensure(folder);
            var baseController = LocomotionControllers.BuildBase(bake, folder + "/" + BaseFile);
            var additive = LocomotionControllers.BuildFidgetAdditive(bake.Desktop.Lean.Neutral, fidget, folder + "/" + AdditiveFile);
            FeaturePrefab.Edit(options.PrefabPath, root =>
            {
                FeaturePrefab.Describe(root, options.Id, options.Label, "locomotion", "");
                Replace(root, VRCAvatarDescriptor.AnimLayerType.Base, baseController);
                Replace(root, VRCAvatarDescriptor.AnimLayerType.Additive, additive);
            });
            AssetDatabase.SaveAssets();

            if (!ControllerVerifier.VerifyStates(rigSource, baseController, out string states))
            {
                throw new InvalidOperationException("The Base controller written to " + folder + " did not pass its checks; do not ship it.\n" + states);
            }

            var report = new StringBuilder("Baked " + rigSource.name + " into " + folder + "\n");
            report.AppendLine("body: leg " + bake.Body.LegLength.ToString("F3") + " m (thigh " + bake.Body.ThighFraction.ToString("F2") +
                              "), hips " + bake.Body.HipWidth.ToString("F3") + " m, foot " + bake.Body.FootLength.ToString("F3") +
                              " m, ankle height " + bake.Body.AnkleHeight.ToString("F3") + " m");
            string clips = baked.Report.ToString();
            report.Append(clips.Substring(0, clips.IndexOf('\n') + 1)).Append(states);
            if (fidget != null)
            {
                float Travel(float forward, bool vr) =>
                    ControllerVerifier.HandTravel(rigSource, baseController, additive, forward, vr, fidget.Duration + 1f);
                float still = Travel(0f, false), walking = Travel(1f, false), inVr = Travel(0f, true);
                bool passed = still > 0.01f && walking < 0.002f && inVr < 0.002f;
                report.AppendLine((passed ? "  ok   " : "  FAIL ") + "the fidget plays standing still at a desktop and nowhere else: the hand strays " +
                                  (still * 1000f).ToString("F1") + " mm there, " + (walking * 1000f).ToString("F1") + " mm walking, " +
                                  (inVr * 1000f).ToString("F1") + " mm in VR");
                if (!passed) throw new InvalidOperationException("The Additive controller written to " + folder + " did not pass its check.\n" + report);
            }

            if (options.FootSpeedsAt.Count > 0)
            {
                var shares = ControllerVerifier.FootSpeedShares(rigSource, baseController, options.FootSpeedsAt);
                report.Append("planted foot's speed over the ground's:");
                for (int i = 0; i < shares.Length; i++)
                {
                    report.Append(" (" + options.FootSpeedsAt[i].x.ToString("0.##") + ", " + options.FootSpeedsAt[i].y.ToString("0.##") + ") " +
                                  (shares[i] == 0f ? "none planted" : shares[i].ToString("P1")));
                }

                report.AppendLine();
            }

            return report + "prefab: " + options.PrefabPath;
        }

        /// <summary>The per-clip lines of the bake's check, for a closer look than <see cref="Build"/> reports.</summary>
        public static string Describe(CustomMotions desktop, CustomMotions tracked, string rigPath)
        {
            var rigSource = LoadRig(rigPath);
            LocomotionBake bake;
            using (var rig = new HumanoidRig(rigSource))
            {
                var shared = new Dictionary<IPoseCycle, BakedClip>();
                bake = new LocomotionBake
                {
                    Body = rig.Measured,
                    Desktop = BakeSet("", desktop, rig, rig.Measured, shared),
                    Tracked = BakeSet("tracked ", tracked, rig, rig.Measured, shared),
                };
            }

            return BakeVerifier.Verify(bake, rigSource).Report.ToString();
        }

        private static ClipSet BakeSet(string prefix, CustomMotions motions, HumanoidRig rig, BodyProportions body,
            Dictionary<IPoseCycle, BakedClip> shared)
        {
            BakedClip Baked(string name, IPoseCycle cycle, bool loop)
            {
                if (shared.TryGetValue(Needed(cycle, name), out var already)) return already;
                var baked = loop
                    ? HumanoidClipBaker.Bake(prefix + name, cycle, rig, body)
                    : HumanoidClipBaker.BakeOnce(prefix + name, cycle, rig, body);
                shared[cycle] = baked;
                return baked;
            }

            BakedClip Loop(string name, IPoseCycle cycle) => Baked(name, cycle, true);
            BakedClip Once(string name, IPoseCycle cycle) => Baked(name, cycle, false);

            var set = new ClipSet
            {
                Idle = Loop("idle", motions.Idle),
                CrouchIdle = Loop("crouch idle", motions.CrouchIdle),
                Prone = motions.Prone != null ? Loop("prone", motions.Prone) : null,
                Rising = Loop("air rising", motions.Rising),
                Falling = Loop("air falling", motions.Falling),
                Plunging = Loop("air plunging", motions.Plunging),
                SoftLanding = Once("landing", motions.SoftLanding),
                HardLanding = Once("hard landing", motions.HardLanding),
                // The pose the layer rests in while it hands tracking back, and the Additive layer's reference.
                Lean = new LeanClips { Neutral = HumanoidClipBaker.BakePose(prefix + "still", motions.Idle.Evaluate(0f), rig, body) },
            };
            foreach (var gait in motions.Moving)
            {
                set.Moving.Add(new MovingClip { Clip = Loop(gait.Name, gait.Cycle), Velocity = new Vector2(gait.Right, gait.Forward) });
            }

            foreach (var gait in motions.Crouching)
            {
                set.Crouching.Add(new MovingClip { Clip = Loop(gait.Name, gait.Cycle), Velocity = new Vector2(gait.Right, gait.Forward) });
            }

            return set;
        }

        private static IPoseCycle Needed(IPoseCycle cycle, string name)
        {
            return cycle ?? throw new ArgumentException("A custom set needs a motion for \"" + name + "\".");
        }

        private static GameObject LoadRig(string path)
        {
            var rig = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (rig == null) throw new FileNotFoundException("No model or prefab at " + path + ".", path);
            return rig;
        }

        private static void Replace(GameObject root, VRCAvatarDescriptor.AnimLayerType layer, RuntimeAnimatorController controller)
        {
            var merge = root.GetComponents<ModularAvatarMergeAnimator>().FirstOrDefault(m => m.layerType == layer)
                        ?? root.AddComponent<ModularAvatarMergeAnimator>();
            merge.animator = controller;
            merge.layerType = layer;
            merge.mergeAnimatorMode = MergeAnimatorMode.Replace;
            merge.pathMode = MergeAnimatorPathMode.Absolute;
            merge.matchAvatarWriteDefaults = false;
            merge.deleteAttachedAnimator = false;
        }
    }
}
