using System;
using System.IO;
using BepInEx.Configuration;
using SoulPlayer.Configuration;
using SoulPlayer.UI;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    [Trait("Validation", "MiniPlayerLayout")]
    public sealed class SoulMiniPlayerLayoutTests
    {
        [Fact]
        public void DefaultPreservesExistingBottomRightAndAllCornersPersist()
        {
            using (TestMusicFiles files = new TestMusicFiles())
            {
                string path = Path.Combine(files.Root, "mini.cfg");
                ConfigFile config = new ConfigFile(path, false);
                SoulPlayerSettings settings = new SoulPlayerSettings(config);
                Assert.Equal(SoulMiniPlayerCorner.BottomRight, settings.MiniPlayerPosition);
                int changed = 0;
                settings.MiniPlayerChanged += () => changed++;
                foreach (SoulMiniPlayerCorner corner in Enum.GetValues(typeof(SoulMiniPlayerCorner)))
                {
                    config[new ConfigDefinition("Interface", "Mini-player position")].BoxedValue = corner;
                    Assert.Equal(corner, settings.MiniPlayerPosition);
                    config.Save();
                    Assert.Equal(corner, new SoulPlayerSettings(new ConfigFile(path, false)).MiniPlayerPosition);
                }
                Assert.Equal(3, changed);
            }
        }

        [Theory]
        [InlineData(1920, 1080, 1f)]
        [InlineData(3440, 1440, 1.545604f)]
        [InlineData(1920, 1080, 0.8f)]
        [InlineData(1920, 1080, 1.5f)]
        [InlineData(3440, 1440, 2f)]
        public void AllCornersRemainInsideScaledSafeMargins(int width, int height, float scale)
        {
            foreach (SoulMiniPlayerCorner corner in Enum.GetValues(typeof(SoulMiniPlayerCorner)))
            {
                SoulPlayerVolumeHudRect panel = SoulMiniPlayerLayout.Calculate(width, height, scale,
                    corner, new SoulPlayerVolumeHudPlacementContext());
                Assert.Equal(380f * scale, panel.Width, 2);
                Assert.Equal(44f * scale, panel.Height, 2);
                Assert.True(panel.X >= 12f * scale - 0.01f);
                Assert.True(panel.Y >= 24f * scale - 0.01f);
                Assert.True(panel.X + panel.Width <= width - 12f * scale + 0.01f);
                Assert.True(panel.Y + panel.Height <= height - 24f * scale + 0.01f);
                bool left = corner == SoulMiniPlayerCorner.BottomLeft || corner == SoulMiniPlayerCorner.TopLeft;
                bool top = corner == SoulMiniPlayerCorner.TopLeft || corner == SoulMiniPlayerCorner.TopRight;
                Assert.Equal(left, panel.X < width / 2f);
                Assert.Equal(top, panel.Y < height / 2f);
            }
        }

        [Fact]
        public void BottomRightAtDefaultScaleIsPixelIdenticalToPreviousLayout()
        {
            SoulPlayerVolumeHudRect panel = Mini();
            Assert.Equal(1528f, panel.X);
            Assert.Equal(1000f, panel.Y);
            Assert.Equal(380f, panel.Width);
            Assert.Equal(44f, panel.Height);
        }

        [Theory]
        [InlineData(1920, 1080, 1f)]
        [InlineData(3440, 1440, 1.545604f)]
        [Trait("Validation", "OverlayCollision")]
        public void VolumeStacksAboveMiniAtBothResolutions(int width, int height, float scale)
        {
            SoulPlayerVolumeHudRect mini = SoulMiniPlayerLayout.Calculate(width, height, scale,
                SoulMiniPlayerCorner.BottomRight, new SoulPlayerVolumeHudPlacementContext());
            SoulPlayerVolumeHudRect volume = SoulPlayerVolumeHudLayout.Calculate(width, height,
                new SoulPlayerVolumeHudPlacementContext { MiniPlayerVisible = true, MiniPlayer = mini }).Panel;
            Assert.False(SoulPlayerVolumeHudLayout.Overlaps(mini, volume));
            Assert.True(volume.Y + volume.Height < mini.Y);
        }

        [Fact]
        [Trait("Validation", "OverlayCollision")]
        public void MiniAndVolumeAvoidRecorderStatusDiscoveryAndRecorderTogether()
        {
            SoulPlayerVolumeHudPlacementContext context = new SoulPlayerVolumeHudPlacementContext
            {
                RecorderOverlayVisible = true, RecorderOverlay = Rect(1500, 900, 400, 140),
                RecorderStatusVisible = true, RecorderStatus = Rect(1500, 760, 400, 90),
                DiscoveryOverlayVisible = true, DiscoveryOverlay = Rect(1500, 620, 400, 80)
            };
            SoulPlayerVolumeHudRect mini = SoulMiniPlayerLayout.Calculate(1920, 1080, 1f,
                SoulMiniPlayerCorner.BottomRight, context);
            context.MiniPlayerVisible = true;
            context.MiniPlayer = mini;
            SoulPlayerVolumeHudRect volume = SoulPlayerVolumeHudLayout.Calculate(1920, 1080, context).Panel;
            foreach (SoulPlayerVolumeHudRect occupied in SoulPlayerVolumeHudLayout.OccupiedRects(context, false))
            {
                Assert.False(SoulPlayerVolumeHudLayout.Overlaps(mini, occupied));
                Assert.False(SoulPlayerVolumeHudLayout.Overlaps(volume, occupied));
            }
            Assert.False(SoulPlayerVolumeHudLayout.Overlaps(mini, volume));
        }

        [Fact]
        [Trait("Validation", "OverlayCollision")]
        public void StackingRechecksEarlierObstaclesAndTopCornersStackDown()
        {
            SoulPlayerVolumeHudRect[] occupied = { Rect(1500, 810, 400, 70), Rect(1500, 940, 400, 90) };
            SoulPlayerVolumeHudRect panel = SoulPlayerVolumeHudLayout.StackAround(Mini(), occupied, 1f, 1080, false);
            foreach (SoulPlayerVolumeHudRect obstacle in occupied)
                Assert.False(SoulPlayerVolumeHudLayout.Overlaps(panel, obstacle));
            panel = SoulMiniPlayerLayout.Calculate(1920, 1080, 1f, SoulMiniPlayerCorner.TopLeft,
                new SoulPlayerVolumeHudPlacementContext
                { DiscoveryOverlayVisible = true, DiscoveryOverlay = Rect(0, 25, 500, 100) });
            Assert.Equal(135f, panel.Y);
        }

        [Fact]
        [Trait("Validation", "OverlayCollision")]
        public void HiddenMiniReservesNoSpaceAndResolutionOrScaleRecalculates()
        {
            SoulPlayerVolumeHudRect defaultVolume = SoulPlayerVolumeHudLayout.Calculate(1920, 1080).Panel;
            SoulPlayerVolumeHudRect hiddenVolume = SoulPlayerVolumeHudLayout.Calculate(1920, 1080,
                new SoulPlayerVolumeHudPlacementContext { MiniPlayerVisible = false, MiniPlayer = Mini() }).Panel;
            Assert.Equal(defaultVolume.Y, hiddenVolume.Y);
            SoulPlayerVolumeHudRect changed = SoulMiniPlayerLayout.Calculate(3440, 1440, 1.5f,
                SoulMiniPlayerCorner.BottomRight, new SoulPlayerVolumeHudPlacementContext());
            Assert.Equal(3440 - (380 + 12) * 1.5f, changed.X);
            Assert.Equal(1440 - (44 + 36) * 1.5f, changed.Y);
            string source = UxFixSource.Read("UI", "SoulMiniPlayer.cs");
            Assert.Contains("private void LateUpdate()", source);
            Assert.Contains("_canvas.scaleFactor", source);
            Assert.Contains("!instance._canvas.isActiveAndEnabled", source);
            Assert.Contains("MiniPlayerChanged += OnMiniPlayerChanged", source);
            Assert.Contains("MiniPlayerChanged -= OnMiniPlayerChanged", source);
        }

        private static SoulPlayerVolumeHudRect Mini()
        {
            return SoulMiniPlayerLayout.Calculate(1920, 1080, 1f, SoulMiniPlayerCorner.BottomRight,
                new SoulPlayerVolumeHudPlacementContext());
        }
        private static SoulPlayerVolumeHudRect Rect(float x, float y, float width, float height)
        {
            return new SoulPlayerVolumeHudRect { X = x, Y = y, Width = width, Height = height };
        }
    }
}
