using System;
using System.Collections.Generic;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Wirenook.Locomotion.Editor
{
    /// <summary>
    /// Runs the generated controllers on a rig in edit mode, stacked the way
    /// VRChat stacks an avatar's playable layers (Base, then Additive mixed
    /// in additively), and stepped by hand so a test can choose the frame
    /// rate. It stands in for the parts of the client that can be reproduced
    /// with Unity alone; tracking and VRChat's own IK are not among them.
    /// </summary>
    internal sealed class LayerStackSimulator : IDisposable
    {
        private readonly PlayableGraph _graph;
        private readonly AnimatorControllerPlayable[] _controllers;

        public AnimatorControllerPlayable Base => _controllers[0];

        public AnimatorControllerPlayable Additive => _controllers[1];

        /// <param name="additive">Null to run the Base layer alone.</param>
        public LayerStackSimulator(Animator animator, AnimatorController baseController, AnimatorController additive)
        {
            _graph = PlayableGraph.Create("Wirenook locomotion simulation");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);

            var controllers = new List<AnimatorControllerPlayable>
            {
                AnimatorControllerPlayable.Create(_graph, baseController),
            };
            if (additive != null) controllers.Add(AnimatorControllerPlayable.Create(_graph, additive));
            _controllers = controllers.ToArray();

            var mixer = AnimationLayerMixerPlayable.Create(_graph, _controllers.Length);
            for (int i = 0; i < _controllers.Length; i++)
            {
                _graph.Connect(_controllers[i], 0, mixer, i);
                mixer.SetInputWeight(i, 1f);
            }

            if (_controllers.Length > 1) mixer.SetLayerAdditive(1, true);

            var output = AnimationPlayableOutput.Create(_graph, "Animation", animator);
            output.SetSourcePlayable(mixer);
            _graph.Play();
        }

        public void Dispose()
        {
            if (_graph.IsValid()) _graph.Destroy();
        }

        /// <summary>
        /// Sets a built-in parameter on every controller that declares it,
        /// converted to the type each one declared, as VRChat does.
        /// </summary>
        public void Set(string name, float value)
        {
            foreach (var controller in _controllers)
            {
                for (int i = 0; i < controller.GetParameterCount(); i++)
                {
                    var parameter = controller.GetParameter(i);
                    if (parameter.name != name) continue;
                    switch (parameter.type)
                    {
                        case AnimatorControllerParameterType.Float:
                            controller.SetFloat(name, value);
                            break;
                        case AnimatorControllerParameterType.Int:
                            controller.SetInteger(name, Mathf.RoundToInt(value));
                            break;
                        default:
                            controller.SetBool(name, value != 0f);
                            break;
                    }
                }
            }
        }

        /// <summary>Advances the whole stack by one frame of the given length in seconds.</summary>
        public void Step(float deltaTime)
        {
            _graph.Evaluate(deltaTime);
        }

        public void Run(float seconds, float deltaTime)
        {
            for (float t = 0f; t < seconds - 1e-6f; t += deltaTime) Step(deltaTime);
        }

        public bool IsInState(AnimatorControllerPlayable controller, int layer, string state)
        {
            return controller.GetCurrentAnimatorStateInfo(layer).IsName(state);
        }
    }
}
