using System;
using System.Collections.Generic;
using SoulPlayer.Cassettes;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    public sealed class SoulTapeAuthoringSafetyTests
    {
        [Fact]
        [Trait("Validation", "PlacementAuthoring")]
        public void AuthoringRayIgnoresLocalPlayerCollider()
        {
            int selected;
            Assert.True(SoulTapeAuthoringHitSelector.TrySelectNearestWorldHit(
                new[]
                {
                    Hit(0, 0.75f, localPlayer: true),
                    Hit(1, 2f)
                },
                out selected));
            Assert.Equal(1, selected);
        }

        [Fact]
        [Trait("Validation", "PlacementAuthoring")]
        public void AuthoringRayIgnoresHandsAndHeldItemCollider()
        {
            int selected;
            Assert.True(SoulTapeAuthoringHitSelector.TrySelectNearestWorldHit(
                new[]
                {
                    Hit(0, 0.8f, handsOrHeldItem: true),
                    Hit(1, 1.8f)
                },
                out selected));
            Assert.Equal(1, selected);
        }

        [Fact]
        [Trait("Validation", "PlacementAuthoring")]
        public void AuthoringRayIgnoresSoulPlayerGhostCollider()
        {
            int selected;
            Assert.True(SoulTapeAuthoringHitSelector.TrySelectNearestWorldHit(
                new[]
                {
                    Hit(0, 1f, preview: true),
                    Hit(1, 2.5f)
                },
                out selected));
            Assert.Equal(1, selected);
        }

        [Fact]
        [Trait("Validation", "PlacementAuthoring")]
        public void AuthoringRayChoosesNearestRemainingValidWorldHit()
        {
            int selected;
            Assert.True(SoulTapeAuthoringHitSelector.TrySelectNearestWorldHit(
                new[]
                {
                    Hit(4, 5f),
                    Hit(0, 0.2f, nearCamera: true),
                    Hit(2, 3f),
                    Hit(1, 1f, trigger: true)
                },
                out selected));
            Assert.Equal(2, selected);
        }

        [Fact]
        [Trait("Validation", "PlacementAuthoring")]
        public void NoValidWorldHitReturnsNoSelection()
        {
            int selected;
            Assert.False(SoulTapeAuthoringHitSelector.TrySelectNearestWorldHit(
                new[]
                {
                    Hit(0, 0.1f, nearCamera: true),
                    Hit(1, 0.8f, localPlayer: true),
                    Hit(2, 1.2f, handsOrHeldItem: true),
                    Hit(3, 1.8f, preview: true),
                    Hit(4, 2.2f, trigger: true)
                },
                out selected));
            Assert.Equal(-1, selected);
        }

        [Fact]
        [Trait("Validation", "PlacementAuthoring")]
        public void ForegroundHeldItemCannotMakeHorizontalWorldSurfaceLookSteep()
        {
            int selected;
            Assert.True(SoulTapeAuthoringHitSelector.TrySelectNearestWorldHit(
                new[]
                {
                    Hit(0, 0.7f, handsOrHeldItem: true),
                    Hit(1, 2f)
                },
                out selected));
            Assert.Equal(1, selected);

            SoulTapePlacementValidationResult horizontal =
                new SoulTapePlacementSolver().Solve(
                    new SoulTapeVector3(0f, 0f, 0f),
                    SoulTapePlacementMath.Up,
                    SoulTapePlacementMath.Forward,
                    0f,
                    0f,
                    delegate { return false; });
            Assert.True(horizontal.IsValid, horizontal.Reason);
        }

        [Fact]
        [Trait("Validation", "PlacementAuthoring")]
        public void NoclipMovementStepNeverExceedsConfiguredMaximum()
        {
            const float maximumStep = 0.75f;
            SoulTapeVector3 next = SoulTapeAuthoringMovementMath.MoveTowardsHorizontal(
                new SoulTapeVector3(0f, 7f, 0f),
                new SoulTapeVector3(10f, 100f, 0f),
                maximumStep);

            Assert.InRange(next.X, maximumStep - 0.00001f, maximumStep + 0.00001f);
            Assert.Equal(7f, next.Y);
            Assert.Equal(0f, next.Z);
        }

        [Theory]
        [Trait("Validation", "PlacementAuthoring")]
        [InlineData(40f, 25f)]
        [InlineData(-40f, -25f)]
        [InlineData(12.5f, 12.5f)]
        public void VerticalAuthoringCameraOffsetIsClamped(
            float requested,
            float expected)
        {
            Assert.Equal(
                expected,
                SoulTapeAuthoringMovementMath.ClampVerticalCameraOffset(
                    requested,
                    25f));
        }

        private static SoulTapeAuthoringHitCandidate Hit(
            int sourceIndex,
            float distance,
            bool localPlayer = false,
            bool handsOrHeldItem = false,
            bool preview = false,
            bool trigger = false,
            bool nearCamera = false)
        {
            return new SoulTapeAuthoringHitCandidate
            {
                SourceIndex = sourceIndex,
                Distance = distance,
                IsLocalPlayer = localPlayer,
                IsHandsOrHeldItem = handsOrHeldItem,
                IsSoulPlayerPreview = preview,
                IsTrigger = trigger,
                IsNearCamera = nearCamera
            };
        }
    }
}
