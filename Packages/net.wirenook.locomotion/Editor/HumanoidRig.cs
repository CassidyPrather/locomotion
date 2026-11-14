using System;
using System.Collections.Generic;
using UnityEngine;
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;

namespace Wirenook.Locomotion.Editor
{
    /// <summary>
    /// A throwaway copy of a humanoid's skeleton, held in its T-pose
    /// reference so the abstract biped's segment orientations can be laid
    /// onto its real bones. Only the transforms and the Animator are copied:
    /// an avatar's other components (dynamics, constraints, build tooling)
    /// would react to being instantiated in the editor, and none of them
    /// matter to where the bones are. The copy is hidden, never saved, and
    /// destroyed on dispose.
    /// </summary>
    internal sealed class HumanoidRig : IDisposable
    {
        private readonly GameObject _instance;
        private readonly Transform _root;
        private readonly Animator _animator;
        private readonly HumanPoseHandler _poseHandler;

        // For each driven bone: the rotation from the abstract segment's frame
        // to the bone's own axes, so bone = segment * offset in root space.
        private readonly Dictionary<HumanBodyBones, Quaternion> _offsets = new Dictionary<HumanBodyBones, Quaternion>();
        private readonly List<HumanBodyBones> _spine = new List<HumanBodyBones>();

        // From the midpoint of the hip joints to the hips bone, in the T-pose.
        private Vector3 _hipsFromPelvis;

        // The heel and the ball of each foot in the foot bone's local space,
        // for checking a sampled pose against the ground.
        private readonly Dictionary<Side, Vector3> _heelLocal = new Dictionary<Side, Vector3>();
        private readonly Dictionary<Side, Vector3> _ballLocal = new Dictionary<Side, Vector3>();

        public BodyProportions Measured { get; }

        public Animator Animator => _animator;

        public float HumanScale => _animator.humanScale;

        /// <summary>Copies the skeleton of a humanoid model or prefab and puts the copy in its avatar's T-pose at the origin.</summary>
        public HumanoidRig(GameObject source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var sourceAnimator = source.GetComponentInChildren<Animator>(true);
            if (sourceAnimator == null || sourceAnimator.avatar == null || !sourceAnimator.avatar.isHuman)
            {
                throw new ArgumentException(source.name + " has no humanoid Animator.", nameof(source));
            }

            _instance = CopyTransforms(sourceAnimator.transform, null);
            _instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _root = _instance.transform;
            _animator = _instance.AddComponent<Animator>();
            _animator.avatar = sourceAnimator.avatar;
            _animator.applyRootMotion = false;
            EnforceTPose();

            Measured = Measure();
            CaptureReference();
            _poseHandler = new HumanPoseHandler(_animator.avatar, _root);
        }

        public void Dispose()
        {
            _poseHandler?.Dispose();
            if (_instance != null) UnityEngine.Object.DestroyImmediate(_instance);
        }

        /// <summary>
        /// A pose made for <paramref name="body"/> as this skeleton will take
        /// it. When the body was measured from this rig nothing changes. When
        /// it was stated by hand, positions scale by the ratio of leg lengths,
        /// which is also how Unity retargets a humanoid clip between avatars.
        /// </summary>
        public GaitPose Fit(GaitPose pose, BodyProportions body)
        {
            float scale = Measured.LegLength / body.LegLength;
            pose.PelvisPosition *= scale;
            pose.LeftFoot.Ankle *= scale;
            pose.RightFoot.Ankle *= scale;
            return pose;
        }

        /// <summary>Poses the skeleton to match a gait pose made for <paramref name="body"/>.</summary>
        public void Apply(GaitPose pose, BodyProportions body)
        {
            pose = Fit(pose, body);
            var pelvis = pose.PelvisRotation.ToUnity();
            var hips = Bone(HumanBodyBones.Hips);
            hips.SetPositionAndRotation(
                _root.TransformPoint(pose.PelvisPosition.ToUnity() + pelvis * _hipsFromPelvis),
                _root.rotation * pelvis * _offsets[HumanBodyBones.Hips]);

            // The legs are solved from where this skeleton's hip joints really
            // are, which need not be exactly where the abstract biped's are.
            var frames = BipedFrames.Solve(pose, Measured,
                RootPosition(HumanBodyBones.LeftUpperLeg).ToNumerics(),
                RootPosition(HumanBodyBones.RightUpperLeg).ToNumerics());

            // Parents before children: each world rotation is set against a parent already in place.
            for (int i = 0; i < _spine.Count; i++)
            {
                SetRotation(_spine[i], frames.Spine((i + 1f) / _spine.Count));
            }

            SetRotation(HumanBodyBones.Neck, frames.Neck);
            SetRotation(HumanBodyBones.Head, frames.Head);

            SetRotation(HumanBodyBones.LeftUpperArm, frames.LeftArm.UpperArm);
            SetRotation(HumanBodyBones.LeftLowerArm, frames.LeftArm.Forearm);
            SetRotation(HumanBodyBones.RightUpperArm, frames.RightArm.UpperArm);
            SetRotation(HumanBodyBones.RightLowerArm, frames.RightArm.Forearm);

            ApplyLeg(frames.LeftLeg, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg,
                HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes);
            ApplyLeg(frames.RightLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg,
                HumanBodyBones.RightFoot, HumanBodyBones.RightToes);
        }

        /// <summary>The skeleton's current pose in Unity's muscle space.</summary>
        public HumanPose ReadHumanPose()
        {
            var pose = new HumanPose();
            _poseHandler.GetHumanPose(ref pose);
            return pose;
        }

        /// <summary>Poses the skeleton from a clip at a time in seconds.</summary>
        public void Sample(AnimationClip clip, float time)
        {
            clip.SampleAnimation(_animator.gameObject, time);
        }

        /// <summary>A bone's position in root space, metres.</summary>
        public Vector3 RootPosition(HumanBodyBones bone)
        {
            return _root.InverseTransformPoint(Bone(bone).position);
        }

        public Vector3 Ankle(Side side)
        {
            return RootPosition(side == Side.Left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
        }

        /// <summary>The heel's contact point, in root space.</summary>
        public Vector3 Heel(Side side) => SolePoint(side, _heelLocal[side]);

        /// <summary>The ball of the foot's contact point, in root space.</summary>
        public Vector3 Ball(Side side) => SolePoint(side, _ballLocal[side]);

        /// <summary>Height above the ground of the lower of a foot's heel and ball.</summary>
        public float LowestSoleHeight(Side side) => Mathf.Min(Heel(side).y, Ball(side).y);

        private Vector3 SolePoint(Side side, Vector3 local)
        {
            var foot = Bone(side == Side.Left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
            return _root.InverseTransformPoint(foot.TransformPoint(local));
        }

        private static GameObject CopyTransforms(Transform source, Transform parent)
        {
            var copy = new GameObject(source.name) { hideFlags = HideFlags.HideAndDontSave };
            copy.transform.SetParent(parent, false);
            copy.transform.localPosition = source.localPosition;
            copy.transform.localRotation = source.localRotation;
            copy.transform.localScale = source.localScale;
            foreach (Transform child in source) CopyTransforms(child, copy.transform);
            return copy;
        }

        private Transform Bone(HumanBodyBones bone) => _animator.GetBoneTransform(bone);

        private bool Has(HumanBodyBones bone) => Bone(bone) != null;

        private void SetRotation(HumanBodyBones bone, NQuaternion segment)
        {
            if (!_offsets.TryGetValue(bone, out var offset)) return;
            Bone(bone).rotation = _root.rotation * segment.ToUnity() * offset;
        }

        private void ApplyLeg(LegFrames leg, HumanBodyBones thigh, HumanBodyBones shin, HumanBodyBones foot,
            HumanBodyBones toes)
        {
            SetRotation(thigh, leg.Thigh);
            SetRotation(shin, leg.Shin);
            SetRotation(foot, leg.Foot);
            SetRotation(toes, leg.Toes);
        }

        /// <summary>
        /// Puts the copy in the pose its Avatar was configured in. A prefab is
        /// usually saved that way already, but nothing guarantees it, and every
        /// measurement below assumes it.
        /// </summary>
        private void EnforceTPose()
        {
            var reference = new Dictionary<string, SkeletonBone>();
            foreach (var bone in _animator.avatar.humanDescription.skeleton) reference[bone.name] = bone;

            foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone) continue;
                // Every transform from a human bone up to the root takes part in where that bone ends up.
                for (var t = Bone(bone); t != null && t != _root; t = t.parent)
                {
                    if (!reference.TryGetValue(t.name, out var pose)) continue;
                    t.localPosition = pose.position;
                    t.localRotation = pose.rotation;
                    t.localScale = pose.scale;
                }
            }
        }

        private BodyProportions Measure()
        {
            float thigh = Distance(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg);
            float shin = Distance(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot);
            var ankle = RootPosition(HumanBodyBones.LeftFoot);

            // The toe joint is the ball of the foot. A rig without toe bones
            // gets an average foot for its leg.
            float footLength = 0.3f * (thigh + shin);
            if (Has(HumanBodyBones.LeftToes))
            {
                var ball = RootPosition(HumanBodyBones.LeftToes) - ankle;
                footLength = BodyProportions.FootLengthFromBall(new Vector2(ball.x, ball.z).magnitude);
            }

            return new BodyProportions(
                thigh + shin,
                thigh / (thigh + shin),
                Distance(HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg),
                footLength,
                // The T-pose stands on the root's ground plane.
                ankle.y);
        }

        private float Distance(HumanBodyBones from, HumanBodyBones to)
        {
            return (RootPosition(to) - RootPosition(from)).magnitude;
        }

        private void CaptureReference()
        {
            var upright = Quaternion.identity;
            Capture(HumanBodyBones.Hips, upright);
            foreach (var bone in new[] { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest })
            {
                if (!Has(bone)) continue;
                Capture(bone, upright);
                _spine.Add(bone);
            }

            Capture(HumanBodyBones.Neck, upright);
            Capture(HumanBodyBones.Head, upright);

            var hipMidpoint = 0.5f * (RootPosition(HumanBodyBones.LeftUpperLeg) + RootPosition(HumanBodyBones.RightUpperLeg));
            _hipsFromPelvis = RootPosition(HumanBodyBones.Hips) - hipMidpoint;

            CaptureLimb(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot);
            CaptureLimb(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot);
            CaptureLimb(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
            CaptureLimb(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);

            CaptureFoot(Side.Left, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes);
            CaptureFoot(Side.Right, HumanBodyBones.RightFoot, HumanBodyBones.RightToes);
        }

        private void Capture(HumanBodyBones bone, Quaternion segmentFrame)
        {
            if (!Has(bone)) return;
            var boneInRoot = Quaternion.Inverse(_root.rotation) * Bone(bone).rotation;
            _offsets[bone] = Quaternion.Inverse(segmentFrame) * boneInRoot;
        }

        /// <summary>
        /// The two segments of a limb. Their reference frames follow the
        /// solver's convention (Y up the segment toward the body, Z the way
        /// the middle joint bends), built from where the joints actually are
        /// in the T-pose: knees and elbows both bend toward the front.
        /// </summary>
        private void CaptureLimb(HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones end)
        {
            Capture(upper, SegmentFrame(RootPosition(upper), RootPosition(lower)));
            Capture(lower, SegmentFrame(RootPosition(lower), RootPosition(end)));
        }

        private static Quaternion SegmentFrame(Vector3 near, Vector3 far)
        {
            var up = (near - far).normalized.ToNumerics();
            var hinge = NVector3.Normalize(NVector3.Cross(up, NVector3.UnitZ));
            return Rot.FromBasis(hinge, up, NVector3.Cross(hinge, up)).ToUnity();
        }

        private void CaptureFoot(Side side, HumanBodyBones foot, HumanBodyBones toes)
        {
            var ankle = RootPosition(foot);
            // A flat foot's frame is its heading: the way the toes point in the T-pose.
            float heading = 0f;
            if (Has(toes))
            {
                var toward = RootPosition(toes) - ankle;
                heading = Mathf.Atan2(toward.x, toward.z);
            }

            var frame = Rot.Yaw(heading).ToUnity();
            Capture(foot, frame);
            Capture(toes, frame);

            var footBone = Bone(foot);
            var forward = frame * Vector3.forward;
            var ground = new Vector3(ankle.x, 0f, ankle.z);
            _heelLocal[side] = footBone.InverseTransformPoint(_root.TransformPoint(ground - forward * Measured.HeelBack));
            _ballLocal[side] = footBone.InverseTransformPoint(_root.TransformPoint(ground + forward * Measured.BallForward));
        }
    }
}
