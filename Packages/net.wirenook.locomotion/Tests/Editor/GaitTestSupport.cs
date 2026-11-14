using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;

namespace Wirenook.Locomotion.Tests
{
    /// <summary>Shared fixtures and comparisons for the gait tests.</summary>
    internal static class GaitTestSupport
    {
        public static readonly BodyProportions Body = BodyProportions.Adult;

        /// <summary>Every heading the blend tree samples, as (right, forward) velocities in m/s.</summary>
        public static IEnumerable<GaitRequest> Requests()
        {
            float[] speeds = { 0.5f, 1.4f, 2f, 4f, 6f };
            Vector2[] directions =
            {
                new Vector2(0f, 1f), new Vector2(0f, -1f), new Vector2(1f, 0f), new Vector2(-1f, 0f),
                new Vector2(1f, 1f), new Vector2(-1f, 1f), new Vector2(1f, -1f), new Vector2(-1f, -1f),
            };
            foreach (float speed in speeds)
            {
                foreach (var direction in directions) yield return new GaitRequest(speed, direction);
            }
        }

        public static GaitCycle Cycle(float right, float forward, GaitProfile profile = null)
        {
            return new GaitCycle(profile ?? new GaitProfile(), Body, GaitRequest.FromVelocity(right, forward));
        }

        public static string Describe(GaitRequest request)
        {
            return request.Speed + " m/s toward (" + request.Direction.X + ", " + request.Direction.Y + ")";
        }

        public static void AreClose(Vector3 expected, Vector3 actual, float tolerance, string message)
        {
            Assert.LessOrEqual((expected - actual).Length(), tolerance,
                message + ": expected " + expected + " but was " + actual);
        }

        /// <summary>Angle between two rotations in radians, treating q and -q as the same.</summary>
        public static float Angle(Quaternion a, Quaternion b)
        {
            float dot = MathF.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b)));
            return 2f * MathF.Acos(MathF.Min(dot, 1f));
        }

        public static void AreClose(Quaternion expected, Quaternion actual, float toleranceRadians, string message)
        {
            Assert.LessOrEqual(Angle(expected, actual), toleranceRadians, message);
        }

        /// <summary>Reflection in the body's left-right plane.</summary>
        public static Vector3 Mirror(Vector3 v) => new Vector3(-v.X, v.Y, v.Z);

        public static Quaternion Mirror(Quaternion q) => new Quaternion(q.X, -q.Y, -q.Z, q.W);

        /// <summary>Heel, ball and toe tip of a posed foot, in root space.</summary>
        public static Vector3[] SolePoints(FootPose foot)
        {
            var shape = new FootShape(Body);
            return new[]
            {
                foot.Ankle + Rot.Apply(foot.Rotation, shape.Heel),
                foot.Ankle + Rot.Apply(foot.Rotation, shape.Ball),
                foot.Ankle + Rot.Apply(foot.Rotation, shape.ToeTip(foot.ToeBend)),
            };
        }
    }
}
