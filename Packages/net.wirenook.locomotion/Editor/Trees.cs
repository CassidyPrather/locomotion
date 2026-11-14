using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Wirenook.Assets.Editor;

namespace Wirenook.Locomotion.Editor
{
    /// <summary>One motion in a two-dimensional blend tree.</summary>
    internal readonly struct TreeChild
    {
        public readonly Motion Motion;
        public readonly Vector2 Position;
        public readonly float TimeScale;

        /// <summary>
        /// Plays the motion as its left-right mirror image, half a cycle
        /// late so the mirrored footfalls line up with everyone else's.
        /// </summary>
        public readonly bool Mirrored;

        public TreeChild(Motion motion, float x, float y, float timeScale = 1f, bool mirrored = false)
        {
            Motion = motion;
            Position = new Vector2(x, y);
            TimeScale = timeScale;
            Mirrored = mirrored;
        }
    }

    /// <summary>
    /// Blend trees stored inside a generated controller, of the kinds
    /// <see cref="ControllerBuilder.Tree"/> does not make: two-dimensional
    /// ones and direct ones.
    /// </summary>
    internal sealed class Trees
    {
        private readonly ControllerBuilder _builder;
        private readonly string _path;

        public Trees(ControllerBuilder builder)
        {
            _builder = builder;
            _path = AssetDatabase.GetAssetPath(builder.Controller);
        }

        public BlendTree Blend2D(string name, BlendTreeType type, string x, string y, IEnumerable<TreeChild> children)
        {
            var tree = Create(name, type);
            tree.blendParameter = x;
            tree.blendParameterY = y;
            var motions = new List<ChildMotion>();
            foreach (var child in children)
            {
                motions.Add(new ChildMotion
                {
                    motion = child.Motion,
                    position = child.Position,
                    timeScale = child.TimeScale,
                    mirror = child.Mirrored,
                    cycleOffset = child.Mirrored ? 0.5f : 0f,
                });
            }

            tree.children = motions.ToArray();
            return tree;
        }

        /// <summary>
        /// A direct tree whose children all play at full weight, which adds
        /// their outputs together. The weight is a parameter that is always 1
        /// because a direct tree cannot take a constant.
        /// </summary>
        public BlendTree Sum(string name, string one, params Motion[] children)
        {
            var tree = Create(name, BlendTreeType.Direct);
            var motions = new List<ChildMotion>();
            foreach (var child in children)
            {
                motions.Add(new ChildMotion { motion = child, timeScale = 1f, directBlendParameter = one });
            }

            tree.children = motions.ToArray();
            return tree;
        }

        /// <summary>A one-dimensional tree between two motions.</summary>
        public BlendTree Between(string name, string parameter, float from, Motion atFrom, float to, Motion atTo)
        {
            return _builder.Tree(name, parameter, (from, atFrom), (to, atTo));
        }

        private BlendTree Create(string name, BlendTreeType type)
        {
            var tree = new BlendTree
            {
                name = name,
                blendType = type,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            AssetDatabase.AddObjectToAsset(tree, _path);
            return tree;
        }
    }
}
