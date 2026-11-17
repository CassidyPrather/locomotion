using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDKBase;
using Wirenook.Assets.Editor;

namespace Wirenook.Locomotion.Editor
{
    /// <summary>
    /// Generates the stepladder: something to stand on when the wearer has
    /// lifted themselves off the floor by moving their play space.
    ///
    /// VRChat keeps an avatar's root on the ground under the player while
    /// tracking carries the body wherever the headset is, so someone who has
    /// dragged their play space up half a metre is seen hovering. The ladder
    /// fills the gap: a base on the ground under the feet, a platform under
    /// the soles, and steps spread evenly between the two.
    ///
    /// Nothing about its shape is animated or synced. Each step is held by a
    /// constraint at a fixed share of the way from the base to the platform,
    /// and both ends follow the feet as every viewer sees them, so it is the
    /// right height on every client by construction.
    ///
    /// Whether it is out is one synced bit, decided on the wearer's client:
    /// a contact on the ground reads how high the feet are, and the ladder
    /// comes out when they have been above one height for a moment and goes
    /// away when they drop below a lower one. The gap between the two keeps
    /// it from flickering at the edge, and deciding in one place keeps every
    /// viewer in agreement.
    /// </summary>
    public static class StepladderBuild
    {
        public const string Package = "Packages/net.wirenook.locomotion";
        public const string Prefab = Package + "/Prefabs/Wirenook Stepladder.prefab";
        private const string Controller = Package + "/Generated/stepladder-fx.controller";

        /// <summary>Synced: the ladder is out.</summary>
        private const string Shown = "WN/Ladder";

        /// <summary>The menu switch: the ladder may come out by itself.</summary>
        private const string Auto = "WN/LadderAuto";

        /// <summary>The ground contact's reading; its height part is the one used.</summary>
        private const string Reading = "WN/LadderFeet";
        private const string Height = Reading + "_SenderLocalPositionY";
        private const string Tag = "Wirenook/Stepladder/Feet";

        /// <summary>Steps in the model, counting the platform.</summary>
        private const int Steps = 8;

        /// <summary>Metres the soles must be off the ground for the ladder to come out, and to go away again.</summary>
        private const float RaiseAbove = 0.30f;
        private const float LowerBelow = 0.18f;

        /// <summary>Seconds the feet must stay up first, so a hop does not count.</summary>
        private const float DwellSeconds = 0.4f;

        /// <summary>How far below the ankle joint the sole is, for a figure about 1.7 m tall.</summary>
        private const float AnkleHeight = 0.09f;

        [MenuItem("Tools/Wirenook/Locomotion/Rebuild Stepladder")]
        public static void Rebuild()
        {
            FeaturePrefab.Edit(Prefab, root =>
            {
                // The point midway between the ankles, and the soles under it.
                var feet = FeaturePrefab.Child(root, "Feet");
                var between = Follow(feet);
                foreach (var (name, bone) in new[] { ("Left Foot", HumanBodyBones.LeftFoot), ("Right Foot", HumanBodyBones.RightFoot) })
                {
                    var point = FeaturePrefab.Child(root, name);
                    FeaturePrefab.AttachToBone(point, bone);
                    between.AddSource(new VRCConstraintSource { SourceTransform = point.transform, Weight = 1f });
                }

                var sole = FeaturePrefab.Child(feet, "Sole");
                sole.transform.localPosition = new Vector3(0f, -AnkleHeight, 0f);
                var sender = FeaturePrefab.Ensure<VRCContactSender>(sole);
                sender.shapeType = ContactBase.ShapeType.Sphere;
                sender.radius = 0.05f;
                sender.localOnly = true;
                sender.collisionTags = new List<string> { Tag };

                // The root of an avatar is on the ground and upright, so a
                // contact there reads the soles' height as its own Y.
                var ground = FeaturePrefab.Child(root, "Ground");
                var receiver = FeaturePrefab.Ensure<VRCContactReceiver>(ground);
                receiver.shapeType = ContactBase.ShapeType.Sphere;
                // As far as a contact reaches; beyond it the reading stops
                // and the ladder goes away.
                receiver.radius = 3f;
                receiver.localOnly = true;
                receiver.allowSelf = true;
                receiver.allowOthers = false;
                receiver.collisionTags = new List<string> { Tag };
                receiver.receiverType = ContactReceiver.ReceiverType.Constant;
                receiver.parameter = Reading;
                receiver.advancedParameters = ContactReceiver.AdvancedParameter.SenderLocalPosition;

                // The base stays on the ground and follows the feet sideways.
                var foot = FeaturePrefab.Child(root, "Base");
                var under = Follow(foot);
                under.AddSource(new VRCConstraintSource { SourceTransform = sole.transform, Weight = 1f });
                under.AffectsPositionY = false;

                var model = FeaturePrefab.Model(foot, Package + "/Models/ladder.fbx");
                var parts = new List<string> { FeaturePrefab.PathTo(root, FeaturePrefab.Bone(model, "Ladder.Base")) };
                for (var step = 1; step <= Steps; step++)
                {
                    var bone = FeaturePrefab.Bone(model, "Ladder.S" + step);
                    var share = (float)step / Steps;
                    var hold = Follow(bone.gameObject);
                    hold.AddSource(new VRCConstraintSource { SourceTransform = foot.transform, Weight = 1f - share });
                    hold.AddSource(new VRCConstraintSource { SourceTransform = sole.transform, Weight = share });
                    parts.Add(FeaturePrefab.PathTo(root, bone));
                }

                var fx = ControllerBuilder.Rebuild(Controller);
                fx.Bool(Shown).Bool(Auto, true).Bool("IsLocal").Bool("Grounded", true).Bool("Seated").Float(Height);

                // Every part is scaled by itself. Scaling the bone above them
                // would put the steps' constraints under a parent of no size,
                // where a position in the world has no answer.
                AnimationClip Parts(string name, AnimationCurve scale) => fx.Clip("Stepladder " + name, clip =>
                {
                    foreach (var part in parts) PopToggle.Scale(clip, part, scale);
                });
                var layer = fx.Layer("Stepladder");
                var off = layer.State("Off", Parts("off", AnimationCurve.Constant(0f, 1f / 60f, 0f)));
                var appear = layer.State("Appear", Parts("appear", PopToggle.Overshoot(0f, 1f, PopToggle.AppearSeconds)));
                var on = layer.State("On", Parts("on", AnimationCurve.Constant(0f, 1f / 60f, 1f)));
                var disappear = layer.State("Disappear", Parts("disappear", PopToggle.Overshoot(1f, 0f, PopToggle.DisappearSeconds)));
                off.To(appear).IfTrue(Shown);
                appear.To(on).AfterTime(1f);
                on.To(disappear).IfFalse(Shown);
                disappear.To(off).AfterTime(1f);

                // The wearer's client decides. A hop leaves the ground, and
                // a seat lifts the feet without lifting the play space, so
                // neither counts.
                var watch = fx.Layer("Stepladder Watch");
                var down = watch.State("Down");
                var rising = watch.State("Rising", fx.Clip("Stepladder dwell", clip => clip.Hold(DwellSeconds)));
                var up = watch.State("Up");
                down.Behaviour<VRCAvatarParameterDriver>(driver => SetLocally(driver, 0f));
                up.Behaviour<VRCAvatarParameterDriver>(driver => SetLocally(driver, 1f));
                down.To(rising).IfTrue("IsLocal").IfTrue(Auto).IfTrue("Grounded").IfFalse("Seated").Greater(Height, RaiseAbove);
                rising.To(down).Less(Height, RaiseAbove).Or().IfFalse("Grounded").Or().IfFalse(Auto).Or().IfTrue("Seated");
                rising.To(up).AfterTime(1f);
                up.To(down).Less(Height, LowerBelow).Or().IfFalse(Auto).Or().IfTrue("Seated");
                fx.Save();

                FeaturePrefab.Merge(root, fx.Controller);
                FeaturePrefab.Parameters(root,
                    FeaturePrefab.SyncedBool(Shown),
                    FeaturePrefab.SyncedBool(Auto, true),
                    new ParameterConfig { nameOrPrefix = Height, syncType = ParameterSyncType.NotSynced, localOnly = true });
                FeaturePrefab.InstallMenu(root);
                FeaturePrefab.MenuToggle(root, "Stepladder", "ladder", Auto);
                FeaturePrefab.Describe(root, "stepladder", "Stepladder", "ladder", "stepladder", 20, joinAvatarMesh: true);
            });
            AssetDatabase.SaveAssets();
            Debug.Log("[locomotion] Rebuilt the stepladder.");
        }

        private static VRCPositionConstraint Follow(GameObject target)
        {
            var constraint = FeaturePrefab.Ensure<VRCPositionConstraint>(target);
            constraint.ClearSources();
            constraint.IsActive = true;
            // Unlocked constraints re-measure their offset instead of moving the object.
            constraint.Locked = true;
            constraint.FreezeToWorld = false;
            constraint.PositionOffset = Vector3.zero;
            return constraint;
        }

        private static void SetLocally(VRCAvatarParameterDriver driver, float value)
        {
            driver.localOnly = true;
            driver.parameters = new List<VRC_AvatarParameterDriver.Parameter>
            {
                new VRC_AvatarParameterDriver.Parameter { type = VRC_AvatarParameterDriver.ChangeType.Set, name = Shown, value = value },
            };
        }
    }
}
