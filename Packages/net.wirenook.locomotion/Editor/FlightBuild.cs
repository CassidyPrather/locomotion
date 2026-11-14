using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;
using Wirenook.Assets.Editor;

namespace Wirenook.Locomotion.Editor
{
    /// <summary>
    /// Generates collider flight: a platform under the wearer's feet that they
    /// stand on in mid-air.
    ///
    /// The platform is an ordinary collider on the avatar. It follows the
    /// wearer sideways, and its height is either latched in the world or
    /// following the feet. Three things move it, all read from parameters
    /// VRChat already provides, so flying needs no extra controls and no
    /// synced state beyond the on/off switch:
    ///
    ///   hover  the height is latched; the wearer stands and walks on it.
    ///   rise   while the wearer is moving upward (a jump), the latch is
    ///          released and the platform comes with them; it latches again
    ///          at the top, so every jump is a step up.
    ///   sink   while the wearer crouches, the platform is gone and they
    ///          fall; standing up catches them again.
    /// </summary>
    public static class FlightBuild
    {
        public const string Package = "Packages/net.wirenook.locomotion";
        public const string Prefab = Package + "/Prefabs/Wirenook Flight.prefab";
        private const string Controller = Package + "/Generated/flight-fx.controller";

        private const string Enabled = "WN/Flight";

        /// <summary>Upward speed, in m/s, that counts as jumping rather than drifting.</summary>
        private const float RiseSpeed = 0.6f;

        /// <summary>
        /// VRChat's Upright is 1 standing and falls as the wearer crouches;
        /// its own locomotion switches to the crouch animations near 0.6.
        /// Leaving a gap between sinking and catching keeps the platform from
        /// flickering when someone hovers around the threshold.
        /// </summary>
        private const float SinkBelow = 0.55f;
        private const float CatchAbove = 0.68f;

        [MenuItem("Tools/Wirenook/Locomotion/Rebuild Flight")]
        public static void Rebuild()
        {
            FeaturePrefab.Edit(Prefab, root =>
            {
                var platform = FeaturePrefab.Child(root, "Platform");
                // Only the height is constrained, so only the height is ever
                // latched: sideways the platform stays a child of the avatar
                // and walks with it.
                var constraint = FeaturePrefab.Ensure<VRCPositionConstraint>(platform);
                constraint.ClearSources();
                constraint.AddSource(new VRCConstraintSource { SourceTransform = root.transform, Weight = 1f });
                constraint.AffectsPositionX = false;
                constraint.AffectsPositionY = true;
                constraint.AffectsPositionZ = false;
                constraint.IsActive = true;
                constraint.Locked = true;
                constraint.FreezeToWorld = false;

                // Wide enough to walk about on without stepping off, with its
                // top face exactly at foot height.
                var collider = FeaturePrefab.Ensure<BoxCollider>(platform);
                collider.isTrigger = false;
                collider.size = new Vector3(1.4f, 0.1f, 1.4f);
                collider.center = new Vector3(0f, -0.05f, 0f);
                collider.enabled = false;
                // The player's own capsule collides with the Default layer.
                platform.layer = 0;

                var model = FeaturePrefab.Model(platform, Package + "/Models/flight-cloud.fbx");
                var cloud = FeaturePrefab.PathTo(root, FeaturePrefab.Bone(model, "FlightCloud"));
                var platformPath = FeaturePrefab.PathTo(root, platform.transform);

                var fx = ControllerBuilder.Rebuild(Controller);
                fx.Bool(Enabled).Bool("IsLocal").Float("VelocityY").Float("Upright", 1f);
                var layer = fx.Layer("Flight");

                AnimationClip Pose(string name, bool latched, bool solid, float cloudScale) => fx.Clip("Flight " + name, clip =>
                {
                    clip.Constant(platformPath, typeof(VRCPositionConstraint), "FreezeToWorld", latched ? 1f : 0f)
                        .Constant(platformPath, typeof(BoxCollider), "m_Enabled", solid ? 1f : 0f);
                    PopToggle.Scale(clip, cloud, AnimationCurve.Constant(0f, 1f / 60f, cloudScale));
                });

                var off = layer.State("Off", Pose("off", false, false, 0f));
                // Only the wearer's own copy is solid. Everyone else sees the
                // same cloud, but a collider on someone else's avatar has no
                // business being in the way.
                var hover = layer.State("Hover", Pose("on", true, true, 1f));
                var hoverRemote = layer.State("Hover (remote)", Pose("hover remote", true, false, 1f));
                var rise = layer.State("Rise", Pose("rise", false, false, 1f));
                var sink = layer.State("Sink", Pose("sink", false, false, 0.6f));

                off.To(hover).Fade(0.15f).IfTrue(Enabled).IfTrue("IsLocal");
                off.To(hoverRemote).Fade(0.15f).IfTrue(Enabled).IfFalse("IsLocal");
                foreach (var state in new[] { hover, hoverRemote, rise, sink })
                    state.To(off).Fade(0.15f).IfFalse(Enabled);

                foreach (var latched in new[] { hover, hoverRemote })
                {
                    latched.To(rise).Greater("VelocityY", RiseSpeed);
                    latched.To(sink).Less("Upright", SinkBelow);
                }

                rise.To(sink).Less("Upright", SinkBelow);
                foreach (var airborne in new[] { rise, sink })
                {
                    var settle = airborne == rise
                        ? airborne.To(hover).Less("VelocityY", 0.05f)
                        : airborne.To(hover).Greater("Upright", CatchAbove);
                    settle.IfTrue("IsLocal");
                    var settleRemote = airborne == rise
                        ? airborne.To(hoverRemote).Less("VelocityY", 0.05f)
                        : airborne.To(hoverRemote).Greater("Upright", CatchAbove);
                    settleRemote.IfFalse("IsLocal");
                }

                fx.Save();

                FeaturePrefab.Merge(root, fx.Controller);
                FeaturePrefab.Parameters(root, FeaturePrefab.SyncedBool(Enabled));
                FeaturePrefab.InstallMenu(root);
                FeaturePrefab.MenuToggle(root, "Flight", "flight", Enabled);
                FeaturePrefab.Describe(root, "flight", "Flight", "flight", "flight", 80, joinAvatarMesh: true);
            });
            AssetDatabase.SaveAssets();
            Debug.Log("[locomotion] Rebuilt flight.");
        }
    }
}
