using System;

namespace SoulPlayer.Cassettes
{
    internal delegate bool SoulTapePlacementCollisionProbe(
        SoulTapeVector3 center,
        SoulTapeQuaternion rotation,
        SoulTapeVector3 halfExtents);

    internal sealed class SoulTapeQuaternion
    {
        internal SoulTapeQuaternion(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        internal float X { get; private set; }
        internal float Y { get; private set; }
        internal float Z { get; private set; }
        internal float W { get; private set; }
    }

    internal sealed class SoulTapePlacementValidationResult
    {
        internal SoulTapePlacementValidationResult(
            bool isValid,
            string reason,
            SoulTapeVector3 position,
            SoulTapeQuaternion rotation,
            SoulTapeVector3 surfaceNormal,
            float outwardNudge)
        {
            IsValid = isValid;
            Reason = reason ?? string.Empty;
            Position = position;
            Rotation = rotation;
            SurfaceNormal = surfaceNormal;
            OutwardNudge = outwardNudge;
        }

        internal bool IsValid { get; private set; }
        internal string Reason { get; private set; }
        internal SoulTapeVector3 Position { get; private set; }
        internal SoulTapeQuaternion Rotation { get; private set; }
        internal SoulTapeVector3 SurfaceNormal { get; private set; }
        internal float OutwardNudge { get; private set; }
    }

    /// <summary>
    /// Engine-independent transform solver with an injected collision probe. It
    /// owns no Unity scene or EFT state and runs under the plain .NET test host.
    /// </summary>
    internal sealed class SoulTapePlacementSolver
    {
        internal static readonly SoulTapeVector3 CassetteHalfExtents =
            new SoulTapeVector3(0.055f, 0.009f, 0.035f);

        private const float SurfaceClearance = 0.003f;
        private const float MaximumSurfaceSlopeDegrees = 65f;
        private const float NudgeStep = 0.005f;
        private const float MaximumCollisionNudge = 0.05f;

        internal SoulTapePlacementValidationResult Solve(
            SoulTapeVector3 hitPoint,
            SoulTapeVector3 hitNormal,
            SoulTapeVector3 viewForward,
            float rotationDegrees,
            float additionalSurfaceOffset,
            SoulTapePlacementCollisionProbe collisionProbe)
        {
            SoulTapeVector3 normal = SoulTapePlacementMath.NormalizeOr(
                hitNormal,
                SoulTapePlacementMath.Up);
            SoulTapeVector3 surfaceForward = SoulTapePlacementMath.ProjectOnPlane(
                viewForward,
                normal);
            surfaceForward = SoulTapePlacementMath.NormalizeOr(
                surfaceForward,
                SoulTapePlacementMath.ProjectOnPlane(
                    SoulTapePlacementMath.Forward,
                    normal));
            surfaceForward = SoulTapePlacementMath.NormalizeOr(
                surfaceForward,
                SoulTapePlacementMath.ProjectOnPlane(
                    SoulTapePlacementMath.Right,
                    normal));
            surfaceForward = SoulTapePlacementMath.RotateAroundAxis(
                surfaceForward,
                normal,
                rotationDegrees);

            SoulTapeQuaternion rotation = SoulTapePlacementMath.LookRotation(
                surfaceForward,
                normal);
            float requestedOffset = Math.Max(0f, additionalSurfaceOffset);
            float baseOffset = CassetteHalfExtents.Y + SurfaceClearance + requestedOffset;
            SoulTapeVector3 position = SoulTapePlacementMath.Add(
                hitPoint,
                SoulTapePlacementMath.Scale(normal, baseOffset));

            float minimumUpDot = (float)Math.Cos(
                MaximumSurfaceSlopeDegrees * Math.PI / 180.0);
            if (SoulTapePlacementMath.Dot(normal, SoulTapePlacementMath.Up) < minimumUpDot)
            {
                return new SoulTapePlacementValidationResult(
                    false,
                    "Surface is too close to vertical for cassette placement.",
                    position,
                    rotation,
                    normal,
                    0f);
            }

            if (collisionProbe == null)
            {
                return new SoulTapePlacementValidationResult(
                    true,
                    string.Empty,
                    position,
                    rotation,
                    normal,
                    0f);
            }

            float nudge = 0f;
            while (collisionProbe(position, rotation, CassetteHalfExtents))
            {
                nudge += NudgeStep;
                if (nudge > MaximumCollisionNudge + 0.0001f)
                {
                    return new SoulTapePlacementValidationResult(
                        false,
                        "Cassette bounds remain clipped after the maximum outward nudge.",
                        position,
                        rotation,
                        normal,
                        nudge - NudgeStep);
                }

                position = SoulTapePlacementMath.Add(
                    hitPoint,
                    SoulTapePlacementMath.Scale(normal, baseOffset + nudge));
            }

            return new SoulTapePlacementValidationResult(
                true,
                string.Empty,
                position,
                rotation,
                normal,
                nudge);
        }
    }

    internal static class SoulTapePlacementMath
    {
        internal static readonly SoulTapeVector3 Up = new SoulTapeVector3(0f, 1f, 0f);
        internal static readonly SoulTapeVector3 Forward = new SoulTapeVector3(0f, 0f, 1f);
        internal static readonly SoulTapeVector3 Right = new SoulTapeVector3(1f, 0f, 0f);

        internal static SoulTapeVector3 Add(SoulTapeVector3 left, SoulTapeVector3 right)
        {
            return new SoulTapeVector3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
        }

        internal static SoulTapeVector3 Scale(SoulTapeVector3 value, float scale)
        {
            return new SoulTapeVector3(value.X * scale, value.Y * scale, value.Z * scale);
        }

        internal static float Dot(SoulTapeVector3 left, SoulTapeVector3 right)
        {
            return left.X * right.X + left.Y * right.Y + left.Z * right.Z;
        }

        internal static SoulTapeVector3 Cross(SoulTapeVector3 left, SoulTapeVector3 right)
        {
            return new SoulTapeVector3(
                left.Y * right.Z - left.Z * right.Y,
                left.Z * right.X - left.X * right.Z,
                left.X * right.Y - left.Y * right.X);
        }

        internal static SoulTapeVector3 NormalizeOr(
            SoulTapeVector3 value,
            SoulTapeVector3 fallback)
        {
            if (value == null || value.SqrMagnitude < 0.0000001f)
            {
                value = fallback;
            }

            float magnitude = (float)Math.Sqrt(value.SqrMagnitude);
            return magnitude < 0.00001f
                ? new SoulTapeVector3(0f, 0f, 1f)
                : Scale(value, 1f / magnitude);
        }

        internal static SoulTapeVector3 ProjectOnPlane(
            SoulTapeVector3 value,
            SoulTapeVector3 normal)
        {
            return Add(value, Scale(normal, -Dot(value, normal)));
        }

        internal static SoulTapeVector3 RotateAroundAxis(
            SoulTapeVector3 value,
            SoulTapeVector3 axis,
            float degrees)
        {
            SoulTapeVector3 normalizedAxis = NormalizeOr(axis, Up);
            double radians = degrees * Math.PI / 180.0;
            float cosine = (float)Math.Cos(radians);
            float sine = (float)Math.Sin(radians);
            return Add(
                Add(
                    Scale(value, cosine),
                    Scale(Cross(normalizedAxis, value), sine)),
                Scale(normalizedAxis, Dot(normalizedAxis, value) * (1f - cosine)));
        }

        internal static SoulTapeQuaternion LookRotation(
            SoulTapeVector3 forward,
            SoulTapeVector3 up)
        {
            SoulTapeVector3 normalizedForward = NormalizeOr(forward, Forward);
            SoulTapeVector3 normalizedUp = NormalizeOr(up, Up);
            SoulTapeVector3 right = NormalizeOr(Cross(normalizedUp, normalizedForward), Right);
            normalizedUp = NormalizeOr(Cross(normalizedForward, right), normalizedUp);

            float m00 = right.X;
            float m01 = normalizedUp.X;
            float m02 = normalizedForward.X;
            float m10 = right.Y;
            float m11 = normalizedUp.Y;
            float m12 = normalizedForward.Y;
            float m20 = right.Z;
            float m21 = normalizedUp.Z;
            float m22 = normalizedForward.Z;
            float trace = m00 + m11 + m22;

            float x;
            float y;
            float z;
            float w;
            if (trace > 0f)
            {
                float scale = (float)Math.Sqrt(trace + 1f) * 2f;
                w = 0.25f * scale;
                x = (m21 - m12) / scale;
                y = (m02 - m20) / scale;
                z = (m10 - m01) / scale;
            }
            else if (m00 > m11 && m00 > m22)
            {
                float scale = (float)Math.Sqrt(1f + m00 - m11 - m22) * 2f;
                w = (m21 - m12) / scale;
                x = 0.25f * scale;
                y = (m01 + m10) / scale;
                z = (m02 + m20) / scale;
            }
            else if (m11 > m22)
            {
                float scale = (float)Math.Sqrt(1f + m11 - m00 - m22) * 2f;
                w = (m02 - m20) / scale;
                x = (m01 + m10) / scale;
                y = 0.25f * scale;
                z = (m12 + m21) / scale;
            }
            else
            {
                float scale = (float)Math.Sqrt(1f + m22 - m00 - m11) * 2f;
                w = (m10 - m01) / scale;
                x = (m02 + m20) / scale;
                y = (m12 + m21) / scale;
                z = 0.25f * scale;
            }

            float magnitude = (float)Math.Sqrt(x * x + y * y + z * z + w * w);
            return new SoulTapeQuaternion(
                x / magnitude,
                y / magnitude,
                z / magnitude,
                w / magnitude);
        }

        internal static SoulTapeVector3 Rotate(
            SoulTapeQuaternion rotation,
            SoulTapeVector3 value)
        {
            SoulTapeVector3 quaternionVector = new SoulTapeVector3(
                rotation.X,
                rotation.Y,
                rotation.Z);
            SoulTapeVector3 firstCross = Cross(quaternionVector, value);
            SoulTapeVector3 secondCross = Cross(quaternionVector, firstCross);
            return Add(
                value,
                Add(
                    Scale(firstCross, 2f * rotation.W),
                    Scale(secondCross, 2f)));
        }

        internal static float QuaternionAngleDegrees(
            SoulTapeQuaternion left,
            SoulTapeQuaternion right)
        {
            double dot = Math.Abs(
                left.X * right.X + left.Y * right.Y + left.Z * right.Z + left.W * right.W);
            dot = Math.Min(1.0, dot);
            return (float)(2.0 * Math.Acos(dot) * 180.0 / Math.PI);
        }
    }
}
