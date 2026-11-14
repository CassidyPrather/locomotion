using System;
using System.IO;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Wirenook.Assets.Editor;

namespace Wirenook.Locomotion.Editor
{
    /// <summary>
    /// Entry points: regenerate the bake that ships with the package, or bake
    /// a set fitted to one avatar. Both return a short text report. Clips are
    /// checked before anything is written, so a bad bake never replaces a good
    /// one; the controllers can only be checked once they exist, and a
    /// failure there throws with the report.
    /// </summary>
    public static class LocomotionBuild
    {
        public const string PackageRoot = "Packages/net.wirenook.locomotion";
        public const string GeneratedFolder = PackageRoot + "/Generated";
        public const string PrefabPath = PackageRoot + "/Prefabs/Wirenook Locomotion.prefab";
        public const string DefaultProfilePath = PackageRoot + "/Profiles/Weighty Gait.asset";

        private const string PrefabFile = "Wirenook Locomotion.prefab";
        private const string ProfileFile = "Gait Profile.asset";

        /// <summary>
        /// The humanoid the shipped bake is made on: the robot from the
        /// VRChat SDK's samples, which every project with the SDK already has.
        /// </summary>
        public const string DefaultRigPath =
            "Packages/com.vrchat.avatars/Samples/Dynamics/Robot Avatar/Tutorial_Robot_Avatar_Dynamics_Demo_v1.fbx";

        /// <summary>Regenerates the default bake, its controllers and the drop-in prefab.</summary>
        public static string Rebuild()
        {
            var profile = AssetFolders.LoadOrCreate(DefaultProfilePath, ScriptableObject.CreateInstance<GaitProfileAsset>);
            return Build(DefaultRigPath, profile, GeneratedFolder, PrefabPath);
        }

        /// <summary>
        /// Bakes clips, controllers and a prefab fitted to one avatar's
        /// proportions into <paramref name="outputFolder"/>. The folder gets
        /// its own copy of the gait profile on the first bake; edit it and
        /// bake again to restyle that avatar alone.
        /// </summary>
        public static string BakeFor(string avatarPrefabAssetPath, string outputFolder)
        {
            outputFolder = outputFolder.Replace('\\', '/').TrimEnd('/');
            var profile = AssetFolders.LoadOrCreate(outputFolder + "/" + ProfileFile, () =>
            {
                var shipped = AssetDatabase.LoadAssetAtPath<GaitProfileAsset>(DefaultProfilePath);
                return shipped != null
                    ? UnityEngine.Object.Instantiate(shipped)
                    : ScriptableObject.CreateInstance<GaitProfileAsset>();
            });
            return Build(avatarPrefabAssetPath, profile, outputFolder, outputFolder + "/" + PrefabFile);
        }

        /// <summary>
        /// Checks the shipped bake without changing it: bakes the default rig
        /// again in memory and measures the clips, runs the generated
        /// controllers as they are on disk and measures those, then installs
        /// the prefab on a throwaway avatar through a real build.
        /// </summary>
        public static string Verify()
        {
            var rig = LoadRig(DefaultRigPath);
            var profile = AssetDatabase.LoadAssetAtPath<GaitProfileAsset>(DefaultProfilePath);
            var baseController = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                GeneratedFolder + "/" + LocomotionControllers.BaseFile);
            var additive = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                GeneratedFolder + "/" + LocomotionControllers.AdditiveFile);
            if (profile == null || baseController == null || additive == null)
            {
                throw new InvalidOperationException("There is no default bake to verify; run Rebuild first.");
            }

            var bake = LocomotionBake.Bake(rig, profile);
            ControllerVerifier.Verify(bake, rig, baseController, additive, out string controllers);
            InstallVerifier.Verify(rig, LoadRig(PrefabPath), bake, out string install);
            return bake.Describe() + BakeVerifier.Verify(bake, rig).Report + controllers + install;
        }

        private static string Build(string rigPath, GaitProfileAsset profile, string folder, string prefabPath)
        {
            var rig = LoadRig(rigPath);
            var bake = LocomotionBake.Bake(rig, profile);
            var checkedBake = BakeVerifier.Verify(bake, rig);
            if (!checkedBake.Passed)
            {
                throw new InvalidOperationException(
                    "The bake for " + rigPath + " did not pass its checks; nothing was written.\n" + checkedBake.Report);
            }

            AssetFolders.Ensure(folder);
            var baseController = LocomotionControllers.BuildBase(bake, folder + "/" + LocomotionControllers.BaseFile);
            var additive = LocomotionControllers.BuildAdditive(bake, folder + "/" + LocomotionControllers.AdditiveFile);
            BuildPrefab(prefabPath, baseController, additive);
            AssetDatabase.SaveAssets();

            if (!ControllerVerifier.Verify(bake, rig, baseController, additive, out string controllers))
            {
                throw new InvalidOperationException(
                    "The controllers written to " + folder + " did not pass their checks; do not ship them.\n" + controllers);
            }

            string clips = checkedBake.Report.ToString();
            return "Baked " + rig.name + " into " + folder + "\n" + bake.Describe() +
                   clips.Substring(0, clips.IndexOf('\n') + 1) + controllers + "prefab: " + prefabPath;
        }

        private static GameObject LoadRig(string path)
        {
            var rig = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (rig == null) throw new FileNotFoundException("No model or prefab at " + path + ".", path);
            return rig;
        }

        /// <summary>
        /// The drop-in prefab: a child for any avatar that swaps the avatar's
        /// Base and Additive playable layers for the generated controllers at
        /// build time. Edited in place so its GUID and the ids of its
        /// components survive a rebuild.
        /// </summary>
        private static void BuildPrefab(string path, AnimatorController baseController, AnimatorController additive)
        {
            FeaturePrefab.Edit(path, root =>
            {
                // No search tag: nobody looks for an avatar by how it walks.
                FeaturePrefab.Describe(root, "locomotion", "Weighty locomotion", "locomotion", "");
                Replace(root, VRCAvatarDescriptor.AnimLayerType.Base, baseController);
                Replace(root, VRCAvatarDescriptor.AnimLayerType.Additive, additive);
            });
        }

        private static void Replace(GameObject root, VRCAvatarDescriptor.AnimLayerType layer,
            RuntimeAnimatorController controller)
        {
            var merge = root.GetComponents<ModularAvatarMergeAnimator>().FirstOrDefault(m => m.layerType == layer)
                        ?? root.AddComponent<ModularAvatarMergeAnimator>();
            merge.animator = controller;
            merge.layerType = layer;
            // Replace discards whatever the avatar had on this layer,
            // VRChat's default included, before anything else merges into it.
            merge.mergeAnimatorMode = MergeAnimatorMode.Replace;
            // The clips animate humanoid muscles and animator parameters,
            // which have no transform path to make relative to this object.
            merge.pathMode = MergeAnimatorPathMode.Absolute;
            merge.matchAvatarWriteDefaults = false;
            merge.deleteAttachedAnimator = false;
        }

        [MenuItem("Tools/Wirenook/Locomotion/Rebuild Default Bake")]
        private static void RebuildMenu() => Debug.Log(Rebuild());

        [MenuItem("Tools/Wirenook/Locomotion/Verify Default Bake")]
        private static void VerifyMenu() => Debug.Log(Verify());

        [MenuItem("Tools/Wirenook/Locomotion/Bake For Selected Avatar")]
        private static void BakeForSelectionMenu()
        {
            string path = SelectedAvatarPath();
            string name = Path.GetFileNameWithoutExtension(path);
            Debug.Log(BakeFor(path, "Assets/Wirenook Locomotion/" + name));
        }

        [MenuItem("Tools/Wirenook/Locomotion/Bake For Selected Avatar", true)]
        private static bool CanBakeForSelection() => SelectedAvatarPath() != null;

        /// <summary>The prefab or model asset behind the selection, whether it is selected in the Project or in a scene.</summary>
        private static string SelectedAvatarPath()
        {
            var selected = Selection.activeGameObject;
            if (selected == null) return null;
            string path = AssetDatabase.Contains(selected)
                ? AssetDatabase.GetAssetPath(selected)
                : PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(selected);
            return string.IsNullOrEmpty(path) ? null : path;
        }
    }
}
