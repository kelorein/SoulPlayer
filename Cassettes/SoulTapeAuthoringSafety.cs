using System;
using System.Collections.Generic;

namespace SoulPlayer.Cassettes
{
    internal sealed class SoulTapeAuthoringHitCandidate
    {
        internal int SourceIndex { get; set; }
        internal float Distance { get; set; }
        internal bool IsLocalPlayer { get; set; }
        internal bool IsHandsOrHeldItem { get; set; }
        internal bool IsSoulPlayerPreview { get; set; }
        internal bool IsTrigger { get; set; }
        internal bool IsNearCamera { get; set; }

        internal bool IsValidWorldHit
        {
            get
            {
                return Distance >= 0f &&
                       !IsLocalPlayer &&
                       !IsHandsOrHeldItem &&
                       !IsSoulPlayerPreview &&
                       !IsTrigger &&
                       !IsNearCamera;
            }
        }
    }

    internal static class SoulTapeAuthoringHitSelector
    {
        internal static bool TrySelectNearestWorldHit(
            IEnumerable<SoulTapeAuthoringHitCandidate> candidates,
            out int sourceIndex)
        {
            sourceIndex = -1;
            float nearestDistance = float.MaxValue;
            if (candidates == null)
            {
                return false;
            }

            foreach (SoulTapeAuthoringHitCandidate candidate in candidates)
            {
                if (candidate == null || !candidate.IsValidWorldHit ||
                    candidate.Distance >= nearestDistance)
                {
                    continue;
                }

                nearestDistance = candidate.Distance;
                sourceIndex = candidate.SourceIndex;
            }

            return sourceIndex >= 0;
        }
    }

    internal static class SoulTapeAuthoringMovementMath
    {
        internal static SoulTapeVector3 MoveTowardsHorizontal(
            SoulTapeVector3 current,
            SoulTapeVector3 target,
            float maximumStep)
        {
            float safeStep = Math.Max(0f, maximumStep);
            float deltaX = target.X - current.X;
            float deltaZ = target.Z - current.Z;
            float distance = (float)Math.Sqrt(
                deltaX * deltaX + deltaZ * deltaZ);
            if (distance <= safeStep || distance <= 0.000001f)
            {
                return new SoulTapeVector3(target.X, current.Y, target.Z);
            }

            float scale = safeStep / distance;
            return new SoulTapeVector3(
                current.X + deltaX * scale,
                current.Y,
                current.Z + deltaZ * scale);
        }

        internal static float ClampVerticalCameraOffset(
            float requestedOffset,
            float maximumAbsoluteOffset)
        {
            float limit = Math.Max(0f, maximumAbsoluteOffset);
            return Math.Max(-limit, Math.Min(limit, requestedOffset));
        }
    }
}
