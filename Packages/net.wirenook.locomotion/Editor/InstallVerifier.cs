using System.Linq;
using System.Text;
using nadena.dev.ndmf;
using nadena.dev.ndmf.platform;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Wirenook.Locomotion.Editor
{
    /// <summary>
    /// Checks the drop-in prefab end to end: puts it on a throwaway avatar
    /// made from a rig, runs the same non-destructive build an upload runs,
    /// and looks at what the avatar's Base and Additive layers ended up as.
    /// </summary>
    internal static class InstallVerifier
    {
        private const string AvatarName = "Wirenook Locomotion install test";

        // Where NDMF saves what an automated build generates: a folder per avatar name.
        private const string BuildAssetsFolder = "Packages/nadena.dev.ndmf/__Generated/" + AvatarName;

        public static bool Verify(GameObject rigSource, GameObject prefab, LocomotionBake bake, out string report)
        {
            var text = new StringBuilder();
            bool passed = true;
            void Check(string what, bool ok, string detail)
            {
                passed &= ok;
                text.AppendLine((ok ? "  ok   " : "  FAIL ") + what + ": " + detail);
            }

            var avatar = UnityEngine.Object.Instantiate(rigSource);
            avatar.name = AvatarName;
            avatar.hideFlags = HideFlags.DontSave;
            try
            {
                // A bare descriptor: every playable layer is VRChat's default,
                // which is the situation the prefab is for.
                var descriptor = avatar.AddComponent<VRCAvatarDescriptor>();
                // A plain copy, as an upload makes: the build refuses to
                // process an avatar that still contains prefab instances.
                UnityEngine.Object.Instantiate(prefab, avatar.transform);
                AvatarProcessor.ProcessAvatar(avatar, AmbientPlatform.CurrentPlatform);

                var baseLayer = Layer(descriptor, VRCAvatarDescriptor.AnimLayerType.Base);
                var additiveLayer = Layer(descriptor, VRCAvatarDescriptor.AnimLayerType.Additive);
                var baseController = baseLayer.animatorController as AnimatorController;
                var additive = additiveLayer.animatorController as AnimatorController;

                Check("the build replaces the default Base layer",
                    descriptor.customizeAnimationLayers && !baseLayer.isDefault && baseController != null &&
                    LayerNames(baseController) == "Locomotion, Cadence",
                    baseController != null ? "layers: " + LayerNames(baseController) : "no controller");
                Check("the build replaces the default Additive layer",
                    !additiveLayer.isDefault && additive != null && LayerNames(additive) == "Clock, Inertia, Lean, Bank",
                    additive != null ? "layers: " + LayerNames(additive) : "no controller");
                Check("the prefab's build-time components are gone from the result",
                    avatar.GetComponentsInChildren<nadena.dev.modular_avatar.core.ModularAvatarMergeAnimator>(true).Length == 0,
                    "no Merge Animator components remain");

                if (baseController != null && additive != null)
                {
                    bool writeDefaults = baseController.layers.Concat(additive.layers)
                        .SelectMany(layer => layer.stateMachine.states).All(state => state.state.writeDefaultValues);
                    Check("every state has Write Defaults on", writeDefaults, "after Modular Avatar's merge");

                    // The built controllers are copies; run them like the originals.
                    passed &= ControllerVerifier.VerifyGaitAndLean(bake, rigSource, baseController, additive, out string ran);
                    text.Append(ran);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(avatar);
                // Only this avatar's folder: the build framework's own
                // clean-up would also take any other avatar's that someone is inspecting.
                AssetDatabase.DeleteAsset(BuildAssetsFolder);
            }

            report = (passed ? "PASS" : "FAIL") + ": install through Modular Avatar\n" + text;
            return passed;
        }

        private static VRCAvatarDescriptor.CustomAnimLayer Layer(VRCAvatarDescriptor descriptor,
            VRCAvatarDescriptor.AnimLayerType type)
        {
            return descriptor.baseAnimationLayers.FirstOrDefault(layer => layer.type == type);
        }

        private static string LayerNames(AnimatorController controller)
        {
            return string.Join(", ", controller.layers.Select(layer => layer.name));
        }
    }
}
