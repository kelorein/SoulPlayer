using System;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using SoulPlayer.Audio;
using SoulPlayer.Configuration;
using SoulPlayer.UI;
using UnityEngine;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    public sealed class SoulPlayerVolumePresetHudTests
    {
        [Theory]
        [InlineData(true, false, false, false, false, 0f)]
        [InlineData(false, true, false, false, false, 0.25f)]
        [InlineData(false, false, true, false, false, 0.50f)]
        [InlineData(false, false, false, true, false, 0.75f)]
        [InlineData(false, false, false, false, true, 1f)]
        [Trait("Validation", "AudioPlayback")]
        public void EveryPresetResolvesToItsExpectedVolume(
            bool muted,
            bool quarter,
            bool half,
            bool threeQuarters,
            bool full,
            float expected)
        {
            float actual;
            Assert.True(SoulPlayerVolumePresetResolver.TryResolve(
                muted, quarter, half, threeQuarters, full, out actual));
            Assert.Equal(expected, actual, 3);
        }

        [Fact]
        [Trait("Validation", "AudioPlayback")]
        public void PresetInputIsIndependentWhenNoPresetWasPressed()
        {
            float volume;
            Assert.False(SoulPlayerVolumePresetResolver.TryResolve(
                false, false, false, false, false, out volume));
            Assert.Equal(0f, volume);
        }

        [Fact]
        [Trait("Validation", "AudioPlayback")]
        public void ConfigOrderIsZeroTwentyFiveFiftySeventyFiveOneHundred()
        {
            string path = Path.Combine(Path.GetTempPath(),
                "soulplayer-volume-order-" + Guid.NewGuid() + ".cfg");
            try
            {
                ConfigFile config = new ConfigFile(path, false);
                new SoulPlayerSettings(config);
                string[] actual = config.ConfigDefinitions
                    .Where(definition => definition.Section == "Volume presets")
                    .OrderByDescending(definition => ConfigOrder(
                        config[definition]))
                    .Select(definition => ConfigDisplayName(
                        config[definition]) ?? definition.Key)
                    .ToArray();

                Assert.Equal(new[]
                {
                    "0% / Muted hotkey",
                    "25% hotkey",
                    "50% hotkey",
                    "75% hotkey",
                    "100% hotkey"
                }, actual);
                Assert.Equal(KeyCode.Keypad0, Shortcut(config,
                    "Muted hotkey").MainKey);
                Assert.Equal(KeyCode.Keypad1, Shortcut(config,
                    "25% hotkey").MainKey);
                Assert.Equal(KeyCode.Keypad2, Shortcut(config,
                    "50% hotkey").MainKey);
                Assert.Equal(KeyCode.Keypad3, Shortcut(config,
                    "75% hotkey").MainKey);
                Assert.Equal(KeyCode.Keypad4, Shortcut(config,
                    "100% hotkey").MainKey);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Fact]
        [Trait("Validation", "AudioPlayback")]
        public void HotkeyAndF12SliderUseTheSameVolumeEventPath()
        {
            string input = Read("Audio",
                "SoulPlayerVolumePresetController.cs");
            string player = Read("Audio", "SoulAudioPlayer.cs");
            string window = Read("UI", "SoulPlayerWindow.cs");
            string hud = Read("UI", "SoulPlayerVolumeHud.cs");

            Assert.Contains("_settings.Volume = volume;", input);
            Assert.Contains("_settings.Volume = volume;", player);
            Assert.Contains("Plugin.AudioPlayer.SetVolume", window);
            Assert.Contains("_settings.VolumeChanged += OnVolumeChanged;", hud);
            Assert.DoesNotContain("Next()", input);
            Assert.DoesNotContain("Previous()", input);
        }

        [Fact]
        [Trait("Validation", "AudioPlayback")]
        public void HudSubscriptionIsDuplicateSafeAndRemovedOnDestroy()
        {
            string hud = Read("UI", "SoulPlayerVolumeHud.cs");
            Assert.Equal(2, Count(hud,
                "_settings.VolumeChanged -= OnVolumeChanged;"));
            Assert.Equal(1, Count(hud,
                "_settings.VolumeChanged += OnVolumeChanged;"));
        }

        [Fact]
        [Trait("Validation", "AudioPlayback")]
        public void RecorderVolumeChangeRetainsPrimedMuteGuard()
        {
            SoulPlayerVolumeState state = new SoulPlayerVolumeState(0.65f);
            state.MarkPrimedMuted();
            state.UpdateTarget(0.75f);

            Assert.Equal(0.75f, state.TargetVolume, 3);
            Assert.True(state.MustRemainMuted);
            Assert.Contains("if (_volumeState.MustRemainMuted)",
                Read("Recorder", "SoulRecorderAudioPlayer.cs"));
        }

        [Fact]
        [Trait("Validation", "AudioPlayback")]
        public void HudTransitionsThroughEnterHoldFadeAndHidden()
        {
            SoulPlayerVolumeHudAnimation animation =
                new SoulPlayerVolumeHudAnimation();
            animation.Show(0.75f, 10f);

            SoulPlayerVolumeHudFrame entering = animation.Sample(10f);
            SoulPlayerVolumeHudFrame holding = animation.Sample(
                10f + SoulPlayerVolumeHudLayout.FadeInSeconds);
            SoulPlayerVolumeHudFrame exiting = animation.Sample(
                10f + SoulPlayerVolumeHudLayout.HoldSeconds +
                SoulPlayerVolumeHudLayout.FadeOutSeconds * 0.5f);
            SoulPlayerVolumeHudFrame hidden = animation.Sample(
                10f + SoulPlayerVolumeHudLayout.HoldSeconds +
                SoulPlayerVolumeHudLayout.FadeOutSeconds);

            Assert.Equal(SoulPlayerVolumeHudPhase.Entering, entering.Phase);
            Assert.Equal(0f, entering.Alpha, 3);
            Assert.True(entering.SlidePixels > 0f);
            Assert.Equal(SoulPlayerVolumeHudPhase.Holding, holding.Phase);
            Assert.Equal(1f, holding.Alpha, 3);
            Assert.Equal(SoulPlayerVolumeHudPhase.Exiting, exiting.Phase);
            Assert.InRange(exiting.Alpha, 0.49f, 0.51f);
            Assert.Equal(SoulPlayerVolumeHudPhase.Hidden, hidden.Phase);
            Assert.Equal(0f, hidden.Alpha, 3);
        }

        [Fact]
        [Trait("Validation", "AudioPlayback")]
        public void RepeatedSliderChangesRefreshHoldWithoutFlashing()
        {
            SoulPlayerVolumeHudAnimation animation =
                new SoulPlayerVolumeHudAnimation();
            animation.Show(0.25f, 5f);
            animation.Sample(5f + SoulPlayerVolumeHudLayout.FadeInSeconds);
            animation.Show(0.50f, 5.5f);

            SoulPlayerVolumeHudFrame refreshed = animation.Sample(5.5f);
            SoulPlayerVolumeHudFrame stillHolding = animation.Sample(
                5.5f + SoulPlayerVolumeHudLayout.HoldSeconds - 0.01f);

            Assert.Equal(1f, refreshed.Alpha, 3);
            Assert.Equal(0.50f, refreshed.Volume, 3);
            Assert.Equal(SoulPlayerVolumeHudPhase.Holding,
                stillHolding.Phase);
        }

        [Fact]
        [Trait("Validation", "AudioPlayback")]
        public void HudIsCompactAndBottomRightAtSixteenNineAndUltrawide()
        {
            SoulPlayerVolumeHudLayoutResult normal =
                SoulPlayerVolumeHudLayout.Calculate(1920, 1080);
            SoulPlayerVolumeHudLayoutResult ultrawide =
                SoulPlayerVolumeHudLayout.Calculate(3440, 1440);

            Assert.Equal(276f, normal.Panel.Width, 2);
            Assert.Equal(68f, normal.Panel.Height, 2);
            Assert.True(normal.Panel.X > 1920f * 0.75f);
            Assert.True(normal.Panel.Y > 1080f * 0.80f);
            Assert.True(ultrawide.Panel.X > 3440f * 0.80f);
            Assert.True(ultrawide.Panel.Y > 1440f * 0.80f);
            Assert.True(ultrawide.Panel.X + ultrawide.Panel.Width < 3440f);
            Assert.True(ultrawide.Panel.Y + ultrawide.Panel.Height < 1440f);
            Assert.Equal("Muted", SoulPlayerVolumeHudLayout.VolumeText(0f));
            Assert.Equal("75%", SoulPlayerVolumeHudLayout.VolumeText(0.75f));
        }

        [Fact]
        [Trait("Validation", "AudioPlayback")]
        public void HudStacksImmediatelyAboveVisibleMiniPlayer()
        {
            SoulPlayerVolumeHudRect mini = Rect(1528f, 1000f, 380f, 44f);
            SoulPlayerVolumeHudPlacementContext context =
                new SoulPlayerVolumeHudPlacementContext
                {
                    MiniPlayerVisible = true,
                    MiniPlayer = mini
                };
            SoulPlayerVolumeHudLayoutResult layout =
                SoulPlayerVolumeHudLayout.Calculate(1920, 1080, context);

            Assert.False(SoulPlayerVolumeHudLayout.Overlaps(
                layout.Panel, mini));
            Assert.Equal(mini.Y - SoulPlayerVolumeHudLayout.StackGap -
                layout.Panel.Height, layout.Panel.Y, 2);
        }

        [Fact]
        [Trait("Validation", "AudioPlayback")]
        public void HiddenMiniPlayerKeepsNormalBottomRightPlacement()
        {
            SoulPlayerVolumeHudLayoutResult expected =
                SoulPlayerVolumeHudLayout.Calculate(1920, 1080);
            SoulPlayerVolumeHudLayoutResult actual =
                SoulPlayerVolumeHudLayout.Calculate(1920, 1080,
                    new SoulPlayerVolumeHudPlacementContext
                    {
                        MiniPlayerVisible = false,
                        MiniPlayer = Rect(1528f, 1000f, 380f, 44f)
                    });

            Assert.Equal(expected.Panel.X, actual.Panel.X, 3);
            Assert.Equal(expected.Panel.Y, actual.Panel.Y, 3);
        }

        [Fact]
        [Trait("Validation", "AudioPlayback")]
        public void RaidWithoutCompetingOverlayKeepsNormalPlacement()
        {
            SoulPlayerVolumeHudLayoutResult expected =
                SoulPlayerVolumeHudLayout.Calculate(1920, 1080);
            SoulPlayerVolumeHudLayoutResult raid =
                SoulPlayerVolumeHudLayout.Calculate(1920, 1080,
                    new SoulPlayerVolumeHudPlacementContext
                    {
                        IsInRaid = true
                    });

            Assert.Equal(expected.Panel.X, raid.Panel.X, 3);
            Assert.Equal(expected.Panel.Y, raid.Panel.Y, 3);
        }

        [Fact]
        [Trait("Validation", "AudioPlayback")]
        public void UltrawideMiniPlayerPlacementRemainsSafe()
        {
            SoulPlayerVolumeHudRect mini =
                Rect(2960f, 1324f, 456f, 53f);
            SoulPlayerVolumeHudLayoutResult layout =
                SoulPlayerVolumeHudLayout.Calculate(3440, 1440,
                    new SoulPlayerVolumeHudPlacementContext
                    {
                        MiniPlayerVisible = true,
                        MiniPlayer = mini
                    });

            Assert.False(SoulPlayerVolumeHudLayout.Overlaps(
                layout.Panel, mini));
            Assert.True(layout.Panel.X >= 0f);
            Assert.True(layout.Panel.Y >= 0f);
            Assert.True(layout.Panel.X + layout.Panel.Width <= 3440f);
            Assert.True(layout.Panel.Y + layout.Panel.Height <= 1440f);
        }

        [Fact]
        [Trait("Validation", "AudioPlayback")]
        public void SimultaneousSoulPlayerOverlaysStackWithoutCollision()
        {
            SoulPlayerVolumeHudRect mini = Rect(1528f, 1000f, 380f, 44f);
            SoulPlayerVolumeHudRect recorder =
                Rect(1580f, 606f, 300f, 434f);
            SoulPlayerVolumeHudRect discovery =
                Rect(1600f, 420f, 300f, 100f);
            SoulPlayerVolumeHudLayoutResult layout =
                SoulPlayerVolumeHudLayout.Calculate(1920, 1080,
                    new SoulPlayerVolumeHudPlacementContext
                    {
                        IsInRaid = true,
                        MiniPlayerVisible = true,
                        MiniPlayer = mini,
                        RecorderOverlayVisible = true,
                        RecorderOverlay = recorder,
                        DiscoveryOverlayVisible = true,
                        DiscoveryOverlay = discovery
                    });

            Assert.False(SoulPlayerVolumeHudLayout.Overlaps(
                layout.Panel, mini));
            Assert.False(SoulPlayerVolumeHudLayout.Overlaps(
                layout.Panel, recorder));
            Assert.False(SoulPlayerVolumeHudLayout.Overlaps(
                layout.Panel, discovery));
        }

        [Fact]
        [Trait("Validation", "AudioPlayback")]
        public void PreviewArtifactsCoverBothRequiredResolutions()
        {
            RequireNonEmpty("Artifacts", "SoulPlayerVolumeHudPreview",
                "soulplayer-volume-hud-1920x1080.png");
            RequireNonEmpty("Artifacts", "SoulPlayerVolumeHudPreview",
                "soulplayer-volume-hud-3440x1440.png");
        }

        private static string Read(params string[] parts)
        {
            string[] all = new string[parts.Length + 1];
            all[0] = FindRepositoryRoot();
            Array.Copy(parts, 0, all, 1, parts.Length);
            return File.ReadAllText(Path.Combine(all));
        }

        private static int ConfigOrder(ConfigEntryBase entry)
        {
            object tag = entry.Description.Tags.Single(item =>
                item.GetType().Name == "ConfigurationManagerAttributes");
            object value = tag.GetType().GetField("Order").GetValue(tag);
            return Convert.ToInt32(value);
        }

        private static string ConfigDisplayName(ConfigEntryBase entry)
        {
            object tag = entry.Description.Tags.Single(item =>
                item.GetType().Name == "ConfigurationManagerAttributes");
            return (string)tag.GetType().GetField("DispName").GetValue(tag);
        }

        private static KeyboardShortcut Shortcut(
            ConfigFile config,
            string key)
        {
            return (KeyboardShortcut)config[
                new ConfigDefinition("Volume presets", key)].BoxedValue;
        }

        private static SoulPlayerVolumeHudRect Rect(
            float x,
            float y,
            float width,
            float height)
        {
            return new SoulPlayerVolumeHudRect
            {
                X = x,
                Y = y,
                Width = width,
                Height = height
            };
        }

        private static void RequireNonEmpty(params string[] parts)
        {
            string[] all = new string[parts.Length + 1];
            all[0] = FindRepositoryRoot();
            Array.Copy(parts, 0, all, 1, parts.Length);
            FileInfo file = new FileInfo(Path.Combine(all));
            Assert.True(file.Exists && file.Length > 0,
                "Required preview artifact is missing or empty: " +
                file.FullName);
        }

        private static int Count(string value, string token)
        {
            int count = 0;
            int offset = 0;
            while ((offset = value.IndexOf(token, offset,
                StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += token.Length;
            }
            return count;
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(
                AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName,
                    "SoulPlayer.csproj")))
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException(
                "SoulPlayer repository root not found.");
        }
    }
}
