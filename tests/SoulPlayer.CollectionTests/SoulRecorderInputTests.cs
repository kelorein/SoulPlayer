using System;
using System.IO;
using BepInEx.Configuration;
using SoulPlayer.Configuration;
using SoulPlayer.Recorder;
using UnityEngine;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    [Trait("Validation", "RecorderInput")]
    public sealed class SoulRecorderInputTests
    {
        [Fact]
        public void DefaultsToMAndRebindingIsLiveAndPersistentWithoutOldMPath()
        {
            using (TestMusicFiles files = new TestMusicFiles())
            {
                string path = Path.Combine(files.Root, "hotkeys.cfg");
                ConfigFile config = new ConfigFile(path, false);
                SoulPlayerSettings settings = new SoulPlayerSettings(config);
                SoulRecorderInput input = new SoulRecorderInput();
                Assert.Equal(KeyCode.M, settings.RecorderStartStopHotkey.MainKey);
                Assert.Equal(KeyCode.N, settings.NextRaidCassetteHotkey.MainKey);
                Assert.Equal(SoulRecorderInputAction.StartStop, Poll(input, settings, KeyCode.M));
                config[new ConfigDefinition("SoulTape discovery", "SoulRecorder start / stop hotkey")]
                    .BoxedValue = new KeyboardShortcut(KeyCode.J, KeyCode.LeftControl);
                Assert.Equal(SoulRecorderInputAction.None, Poll(input, settings, KeyCode.M));
                Assert.Equal(SoulRecorderInputAction.StartStop, Poll(input, settings, KeyCode.J));
                Assert.Equal(SoulRecorderInputAction.NextCassette, Poll(input, settings, KeyCode.N));
                config.Save();
                SoulPlayerSettings restarted = new SoulPlayerSettings(new ConfigFile(path, false));
                Assert.Equal(KeyCode.J, restarted.RecorderStartStopHotkey.MainKey);
                Assert.Contains(KeyCode.LeftControl, restarted.RecorderStartStopHotkey.Modifiers);
                Assert.Equal(KeyCode.None, restarted.PlayPauseHotkey.MainKey);
                Assert.Equal(KeyCode.N, restarted.NextRaidCassetteHotkey.MainKey);
            }
        }

        [Theory]
        [InlineData(false, false, false)]
        [InlineData(true, true, false)]
        [InlineData(true, false, true)]
        public void InputPriorityPreventsPolling(bool inRaid, bool f12, bool ui)
        {
            SoulRecorderInput input = new SoulRecorderInput();
            Assert.Equal(SoulRecorderInputAction.None, input.Poll(
                new KeyboardShortcut(KeyCode.M), new KeyboardShortcut(KeyCode.N),
                inRaid, f12, ui, key => throw new Exception("Must not poll"),
                key => throw new Exception("Must not poll"), null));
        }

        [Fact]
        public void ModifiersAreRequiredAndNoneDisablesStartStop()
        {
            SoulRecorderInput input = new SoulRecorderInput();
            Assert.Equal(SoulRecorderInputAction.None, input.Poll(
                new KeyboardShortcut(KeyCode.M, KeyCode.LeftShift), new KeyboardShortcut(KeyCode.N),
                true, false, false, key => key == KeyCode.M, key => false, null));
            Assert.Equal(SoulRecorderInputAction.None, input.Poll(
                new KeyboardShortcut(KeyCode.None), new KeyboardShortcut(KeyCode.N),
                true, false, false, key => key == KeyCode.M, key => true, null));
        }

        [Fact]
        public void DuplicateKeysExecuteOnlyStartStopAndWarnOncePerBindingChange()
        {
            SoulRecorderInput input = new SoulRecorderInput();
            int warnings = 0;
            KeyboardShortcut shortcut = new KeyboardShortcut(KeyCode.J, KeyCode.LeftControl);
            for (int frame = 0; frame < 20; frame++)
                Assert.Equal(SoulRecorderInputAction.StartStop, input.Poll(shortcut, shortcut,
                    true, false, false, key => key == KeyCode.J, key => true, message => warnings++));
            Assert.Equal(1, warnings);
            shortcut = new KeyboardShortcut(KeyCode.K);
            input.Poll(shortcut, shortcut, true, false, false,
                key => key == KeyCode.K, key => true, message => warnings++);
            Assert.Equal(2, warnings);
        }

        [Fact]
        public void OverlappingModifierShortcutsAlsoWarnOnlyOnceAcrossKeyReleases()
        {
            SoulRecorderInput input = new SoulRecorderInput();
            int warnings = 0;
            for (int frame = 0; frame < 10; frame++)
            {
                bool pressed = frame % 2 == 0;
                SoulRecorderInputAction action = input.Poll(new KeyboardShortcut(KeyCode.M),
                    new KeyboardShortcut(KeyCode.M, KeyCode.LeftShift), true, false, false,
                    key => pressed && key == KeyCode.M, key => true, message => warnings++);
                Assert.Equal(pressed ? SoulRecorderInputAction.StartStop : SoulRecorderInputAction.None, action);
            }
            Assert.Equal(1, warnings);
        }

        [Fact]
        public void RuntimeUsesSingleGuardedInputPathAndRetainsTransport()
        {
            string source = UxFixSource.Read("Recorder", "SoulRecorderController.cs");
            Assert.DoesNotContain("KeyCode.M", source);
            Assert.DoesNotContain("RecorderHotkey", source);
            Assert.Contains("SoulPlayerWindow.IsConfigurationManagerOpen()", source);
            Assert.Contains("Input.GetKeyDown(KeyCode.F12)", source);
            Assert.Contains("SoulPlayerOverlayHost.Instance.CapturesKeyboardInput", source);
            Assert.Contains("ToggleInteraction(player)", source);
            Assert.Contains("QueueRaidNextCassette()", source);
            Assert.Contains("_usableItemController.ManualRecorderUpdate", source);
            Assert.Contains("KeyDown, KeyHeld, _inputWarning, _settings.RecorderInputRevision", source);
            Assert.Contains("inputEdge &&", source);
            Assert.Contains("_emptyFeedbackHeading = \"HOTKEY CONFLICT\"", source);
        }

        private static SoulRecorderInputAction Poll(SoulRecorderInput input,
            SoulPlayerSettings settings, KeyCode pressed)
        {
            return input.Poll(settings.RecorderStartStopHotkey, settings.NextRaidCassetteHotkey,
                true, false, false, key => key == pressed, key => true, null);
        }
    }

    internal static class UxFixSource
    {
        internal static string Read(params string[] parts)
        {
            DirectoryInfo root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "SoulPlayer.csproj")))
                root = root.Parent;
            if (root == null) throw new DirectoryNotFoundException();
            string path = root.FullName;
            foreach (string part in parts) path = Path.Combine(path, part);
            return File.ReadAllText(path);
        }

        internal static string Method(string source, string start, string end)
        {
            int offset = source.IndexOf(start, StringComparison.Ordinal);
            Assert.True(offset >= 0, start);
            int next = source.IndexOf(end, offset + start.Length, StringComparison.Ordinal);
            Assert.True(next > offset, end);
            return source.Substring(offset, next - offset);
        }
    }
}
