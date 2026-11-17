using System.IO;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using Wirenook.Assets.Editor;

namespace Wirenook.Locomotion.Editor
{
    /// <summary>
    /// Generates the nap: what the avatar does while its wearer is away
    /// (VRChat's AFK), in place of VRChat's own kneel. The body stands
    /// asleep, nodding off (see <see cref="NapCycle"/>), and three Zs rise
    /// from the head.
    ///
    /// The pose goes into the Action playable layer, which is where VRChat
    /// plays its own AFK animation, as a layer added on top of whatever is
    /// there. VRChat's default Action controller already raises that playable
    /// layer and hands the body from tracking to animation while AFK is set,
    /// and this leaves all of that alone: it only adds a layer holding the
    /// nap, whose weight a second layer fades up while AFK is set and down
    /// again after. A layer at weight zero changes nothing, so emotes and
    /// everything else in the Action layer play as they did.
    /// </summary>
    public static class NapBuild
    {
        public const string Package = "Packages/net.wirenook.locomotion";
        public const string Prefab = Package + "/Prefabs/Wirenook Nap.prefab";
        private const string ActionController = Package + "/Generated/Nap Action.controller";
        private const string FxController = Package + "/Generated/nap-fx.controller";

        private const string Afk = "AFK";
        private const string PoseLayer = "Nap Pose";

        /// <summary>Seconds the nap takes to settle over VRChat's own AFK pose, and to lift off it.</summary>
        private const float FadeIn = 0.6f;
        private const float FadeOut = 0.25f;

        /// <summary>Seconds for the three Zs to rise once.</summary>
        private const float ZsPeriod = 3.2f;

        [MenuItem("Tools/Wirenook/Locomotion/Rebuild Nap")]
        public static void Rebuild()
        {
            var rigSource = AssetDatabase.LoadAssetAtPath<GameObject>(LocomotionBuild.DefaultRigPath);
            if (rigSource == null)
                throw new FileNotFoundException("The VRChat SDK's sample robot is missing; reinstall the Avatars SDK.", LocomotionBuild.DefaultRigPath);
            var profile = AssetDatabase.LoadAssetAtPath<GaitProfileAsset>(LocomotionBuild.DefaultProfilePath);
            if (profile == null)
                throw new FileNotFoundException("Rebuild the default bake first; the nap stands the way the gait profile stands.", LocomotionBuild.DefaultProfilePath);

            BakedClip baked;
            using (var rig = new HumanoidRig(rigSource))
            {
                var body = profile.measureBody ? rig.Measured : profile.body;
                baked = HumanoidClipBaker.Bake("nap", new NapCycle(profile.style, body), rig, body);
            }

            var action = ControllerBuilder.Rebuild(ActionController);
            action.Bool(Afk);
            // The control layer comes first and the pose second: the control's
            // behaviours name the pose layer by its place in this controller.
            var control = action.Layer("Nap");
            var awake = control.State("Awake");
            var dozing = control.State("Dozing");
            dozing.Behaviour<VRCAnimatorLayerControl>(fade => Weight(fade, 1f, FadeIn));
            awake.Behaviour<VRCAnimatorLayerControl>(fade => Weight(fade, 0f, FadeOut));
            awake.To(dozing).IfTrue(Afk);
            dozing.To(awake).IfFalse(Afk);

            // One state that never stops, so the nap has no first frame to
            // snap to whenever it is faded in.
            var pose = action.Layer(PoseLayer, weight: 0f);
            pose.State("Nap", action.Clip("nap", clip => baked.WriteTo(clip.Clip)));
            action.Save();

            FeaturePrefab.Edit(Prefab, root =>
            {
                var merge = root.GetComponent<ModularAvatarMergeAnimator>() ?? root.AddComponent<ModularAvatarMergeAnimator>();
                merge.animator = action.Controller;
                merge.layerType = VRCAvatarDescriptor.AnimLayerType.Action;
                // The clip animates humanoid muscles, which have no transform
                // path to make relative to this object.
                merge.pathMode = MergeAnimatorPathMode.Absolute;
                merge.matchAvatarWriteDefaults = true;
                merge.deleteAttachedAnimator = false;

                // The Zs are a feature of their own under the nap, so that
                // their animator can be merged into FX by the usual route.
                var zs = FeaturePrefab.Child(root, "Zs");
                var mount = FeaturePrefab.Child(zs, "Mount");
                FeaturePrefab.AttachToBone(mount, HumanBodyBones.Head);
                mount.GetComponent<ModularAvatarBoneProxy>().attachmentMode = BoneProxyAttachmentMode.AsChildKeepRotation;
                var place = FeaturePrefab.Child(mount, "Place");
                // Over the crown, off to the side the head is not tilted to.
                place.transform.localPosition = new Vector3(0.06f, 0.3f, 0.02f);
                var model = FeaturePrefab.Model(place, Package + "/Models/zzz.fbx");

                var fx = ControllerBuilder.Rebuild(FxController);
                fx.Bool(Afk);
                var layer = fx.Layer("Nap Zs");
                var letters = new[] { "Zzz.Z1", "Zzz.Z2", "Zzz.Z3" };
                var none = fx.Clip("nap zs off", clip =>
                {
                    foreach (var letter in letters)
                        PopToggle.Scale(clip, FeaturePrefab.PathTo(zs, FeaturePrefab.Bone(model, letter)), AnimationCurve.Constant(0f, 1f / 60f, 0f));
                });
                var rising = fx.Clip("nap zs rising", clip =>
                {
                    for (var i = 0; i < letters.Length; i++)
                    {
                        // Each letter swells up a beat after the one below
                        // it, and all three go together.
                        var start = 0.15f + 0.55f * i;
                        var swell = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(start, 0f), new Keyframe(start + 0.3f, 1.1f),
                            new Keyframe(start + 0.5f, 1f), new Keyframe(ZsPeriod - 0.45f, 1f), new Keyframe(ZsPeriod - 0.1f, 0f),
                            new Keyframe(ZsPeriod, 0f));
                        PopToggle.Scale(clip, FeaturePrefab.PathTo(zs, FeaturePrefab.Bone(model, letters[i])), swell);
                    }

                    clip.Loop();
                });
                var off = layer.State("Off", none);
                var on = layer.State("Rising", rising);
                off.To(on).IfTrue(Afk);
                on.To(off).IfFalse(Afk);
                fx.Save();

                FeaturePrefab.Merge(zs, fx.Controller);
                FeaturePrefab.Describe(root, "nap", "Nap while away", "nap", "", joinAvatarMesh: true);
            });
            AssetDatabase.SaveAssets();
            Debug.Log("[locomotion] Rebuilt the nap.");
        }

        private static void Weight(VRCAnimatorLayerControl fade, float goal, float seconds)
        {
            fade.playable = VRC_AnimatorLayerControl.BlendableLayer.Action;
            // The pose layer is the second layer of the nap's controller.
            // Modular Avatar moves this index along when it merges the
            // controller in after the layers the avatar already has.
            fade.layer = 1;
            fade.goalWeight = goal;
            fade.blendDuration = seconds;
        }
    }
}
