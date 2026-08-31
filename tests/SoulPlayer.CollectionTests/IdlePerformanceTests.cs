using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using BepInEx.Configuration;
using SoulPlayer.Audio;
using SoulPlayer.Configuration;
using SoulPlayer.Library;
using SoulPlayer.Recorder;
using SoulPlayer.UI;
using SoulPlayer.Utils;
using UnityEngine;
using Xunit;
using Xunit.Abstractions;

namespace SoulPlayer.CollectionTests
{
    [Trait("Validation", "Performance")]
    public sealed class IdlePerformanceTests
    {
        private readonly ITestOutputHelper _output;
        public IdlePerformanceTests(ITestOutputHelper output) { _output = output; }

        [Fact]
        public void ActiveRaidHasZeroReadinessInspectionsAndTransitionsCoalescePerFrame()
        {
            RaidReadinessPollGate gate = new RaidReadinessPollGate();
            for (int frame = 0; frame < 100000; frame++)
            {
                Assert.False(gate.ShouldInspect(false, frame));
                Assert.False(gate.ShouldInspect(false, frame)); // Audio + coordinator.
            }
            Assert.Equal(0, gate.Inspections);
            Assert.True(gate.ShouldInspect(true, 100001));
            Assert.False(gate.ShouldInspect(true, 100001));
            gate.Invalidate(); // A real lifecycle change can request a same-frame refresh.
            Assert.True(gate.ShouldInspect(true, 100001));
            Assert.Equal(2, gate.Inspections);
        }

        [Theory]
        [InlineData(false, 0)]
        [InlineData(true, 1)]
        public void StaticOrHiddenMiniNeverRebuildsLayoutPerFrame(bool visible, int expected)
        {
            SoulOverlayLayoutRevision cache = new SoulOverlayLayoutRevision();
            for (int frame = 0; frame < 100000; frame++)
                cache.ShouldRebuild(visible, 1920, 1080, 1f, 0, new SoulPlayerVolumeHudPlacementContext());
            Assert.Equal(expected, cache.RebuildCount);
        }

        [Fact]
        public void HiddenOverlayBoundsDoNotCauseLayoutChurnButVisibilityAndRealChangesDo()
        {
            SoulOverlayLayoutRevision cache = new SoulOverlayLayoutRevision();
            SoulPlayerVolumeHudPlacementContext context = new SoulPlayerVolumeHudPlacementContext();
            Assert.True(cache.ShouldRebuild(true, 1920, 1080, 1f, 0, context));
            for (int frame = 0; frame < 1000; frame++)
            {
                context.RecorderOverlay.X = context.RecorderStatus.X = context.DiscoveryOverlay.X = frame;
                Assert.False(cache.ShouldRebuild(true, 1920, 1080, 1f, 0, context));
            }
            context.DiscoveryOverlayVisible = true;
            Assert.True(cache.ShouldRebuild(true, 1920, 1080, 1f, 0, context));
            context.DiscoveryOverlay.X++;
            Assert.True(cache.ShouldRebuild(true, 1920, 1080, 1f, 0, context));
            Assert.True(cache.ShouldRebuild(true, 3440, 1440, 1f, 0, context));
            Assert.True(cache.ShouldRebuild(true, 3440, 1440, 1.5f, 0, context));
            Assert.True(cache.ShouldRebuild(true, 3440, 1440, 1.5f, 2, context));
            Assert.False(cache.ShouldRebuild(false, 3440, 1440, 1.5f, 2, context));
            Assert.True(cache.ShouldRebuild(true, 3440, 1440, 1.5f, 2, context));
        }

        [Fact]
        public void ShortcutSignaturesRebuildOnlyForBindingRevisionAndUnchangedConflictLogsOnce()
        {
            SoulRecorderInput input = new SoulRecorderInput();
            KeyboardShortcut both = new KeyboardShortcut(KeyCode.M);
            int warnings = 0;
            Func<KeyCode, bool> none = key => false;
            Action<string> warn = line => warnings++;
            for (int frame = 0; frame < 10000; frame++)
                input.Poll(both, both, true, false, false, none, none, warn, 0);
            Assert.Equal(1, input.SignatureBuildCount);
            Assert.Equal(1, warnings);
            input.Poll(both, both, true, false, false, none, none, warn, 1);
            Assert.Equal(2, input.SignatureBuildCount);
            Assert.Equal(2, warnings);
        }

        [Fact]
        public void BindingRevisionChangesForEitherHotkeyIncludingModifierOnlyEdits()
        {
            using (TestMusicFiles files = new TestMusicFiles())
            {
                ConfigFile config = new ConfigFile(System.IO.Path.Combine(files.Root, "perf.cfg"), false);
                SoulPlayerSettings settings = new SoulPlayerSettings(config);
                int revision = settings.RecorderInputRevision;
                config[new ConfigDefinition("SoulTape discovery", "SoulRecorder start / stop hotkey")].BoxedValue =
                    new KeyboardShortcut(KeyCode.M, KeyCode.LeftShift);
                Assert.Equal(revision + 1, settings.RecorderInputRevision);
                ConfigEntryBase next = config.Keys.Select(key => config[key]).First(entry =>
                    entry.BoxedValue is KeyboardShortcut && ((KeyboardShortcut)entry.BoxedValue).MainKey == KeyCode.N);
                next.BoxedValue = new KeyboardShortcut(KeyCode.N, KeyCode.LeftControl);
                Assert.Equal(revision + 2, settings.RecorderInputRevision);
            }
        }

        [Fact]
        public void LibrarySnapshotIsImmutableAndReusedUntilARealScanPublication()
        {
            using (TestMusicFiles files = new TestMusicFiles())
            {
                files.Create("Unquote - Dopamine.mp3", 91);
                MusicLibrary library = new MusicLibrary(delegate { }, delegate { });
                library.BeginScan(new[] { files.Root });
                ScanResult result;
                Assert.True(SpinWait.SpinUntil(() => library.TryApplyCompletedScan(out result), TimeSpan.FromSeconds(5)));
                IReadOnlyList<MusicTrack> snapshot = library.Tracks;
                Assert.True(((IList<MusicTrack>)snapshot).IsReadOnly);
                for (int frame = 0; frame < 10000; frame++) Assert.Same(snapshot, library.Tracks);
                library.BeginScan(new[] { files.Root });
                Assert.True(SpinWait.SpinUntil(() => library.TryApplyCompletedScan(out result), TimeSpan.FromSeconds(5)));
                Assert.NotSame(snapshot, library.Tracks);
                Assert.Equal(snapshot[0].AudioFingerprint, library.Tracks[0].AudioFingerprint);
            }
        }

        [Fact]
        public void WarmIdleInputReadinessLayoutAndLibraryAccessAllocateZeroBytes()
        {
            SoulRecorderInput input = new SoulRecorderInput();
            RaidReadinessPollGate readiness = new RaidReadinessPollGate();
            SoulOverlayLayoutRevision layout = new SoulOverlayLayoutRevision();
            MusicLibrary library = new MusicLibrary(delegate { }, delegate { });
            KeyboardShortcut start = new KeyboardShortcut(KeyCode.M), next = new KeyboardShortcut(KeyCode.N);
            Func<KeyCode, bool> none = key => false;
            Action tick = () =>
            {
                input.Poll(start, next, true, false, false, none, none, null, 0);
                readiness.ShouldInspect(false, 123);
                layout.ShouldRebuild(true, 1920, 1080, 1f, 0, new SoulPlayerVolumeHudPlacementContext());
                var tracks = library.Tracks;
            };
            for (int i = 0; i < 1000; i++) tick();
            MethodInfo method = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(method);
            Func<long> allocated = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), method);
            long before = allocated();
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int i = 0; i < 100000; i++) tick();
            long bytes = allocated() - before;
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000d / System.Diagnostics.Stopwatch.Frequency;
            _output.WriteLine("Optimized idle composite: calls=100000 elapsedMs=" + ms.ToString("0.000") + " allocatedBytes=" + bytes +
                " readinessInspections=" + readiness.Inspections + " layouts=" + layout.RebuildCount + " bindings=" + input.SignatureBuildCount);
            Assert.Equal(0, bytes);
            Assert.Equal(0, readiness.Inspections);
            Assert.Equal(1, layout.RebuildCount);
            Assert.Equal(1, input.SignatureBuildCount);
        }

        [Fact]
        public void ReleaseContainsNoProfilerStateOrUpdateAndRuntimeUsesTheGuards()
        {
            Assert.Null(typeof(Plugin).Assembly.GetType("SoulPlayer.Utils.RecorderDiagnostics"));
            Assert.Empty(typeof(RecurringWorkProfiler).GetFields(BindingFlags.NonPublic | BindingFlags.Static));
            Assert.Null(typeof(Plugin).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance));
            string audio = UxFixSource.Read("Audio", "SoulAudioPlayer.cs");
            string readiness = UxFixSource.Method(audio, "internal void RefreshRaidReadiness", "private void LogRaidState");
            Assert.True(readiness.IndexOf("_readinessPoll.ShouldInspect", StringComparison.Ordinal) <
                readiness.IndexOf("ReadEvidence()", StringComparison.Ordinal));
            string coordinator = UxFixSource.Method(UxFixSource.Read("Audio", "PostRaidCoordinator.cs"), "private void LateUpdate", "private void OnDestroy");
            Assert.True(coordinator.IndexOf("NeedsRaidReadinessInspection", StringComparison.Ordinal) <
                coordinator.IndexOf("EftScreenManager.Instance", StringComparison.Ordinal));
            string context = UxFixSource.Read("Audio", "StableRaidMenuContext.cs");
            Assert.Contains("if (_preloader != preloader)", context);
            Assert.Contains("if (_result != result)", context);
            Assert.Contains("StableRaidMenuContext.Invalidate()", UxFixSource.Read("Audio", "PostRaidCoordinator.cs"));
            string startClip = UxFixSource.Method(audio, "private void StartClip", "private void StopPlayback");
            Assert.True(startClip.IndexOf("_readinessPoll.Invalidate()", StringComparison.Ordinal) <
                startClip.IndexOf("CanStart(_loadingIntent)", StringComparison.Ordinal));
            string input = UxFixSource.Read("Recorder", "SoulRecorderController.cs");
            Assert.Contains("inputEdge &&", input);
            Assert.Contains("KeyDown, KeyHeld, _inputWarning, _settings.RecorderInputRevision", input);
            string f12 = UxFixSource.Method(UxFixSource.Read("UI", "SoulPlayerWindow.cs"),
                "internal static bool IsConfigurationManagerOpen", "private void HideImmediately");
            Assert.DoesNotContain("FindObjectOfType", f12);
            Assert.DoesNotContain(".GetValue(", f12);
            Assert.Contains("Chainloader.PluginInfos.Values", f12);
            string mini = UxFixSource.Method(UxFixSource.Read("UI", "SoulMiniPlayer.cs"), "private void ApplyLayout", "private void Update()");
            Assert.True(mini.IndexOf("!gameObject.activeInHierarchy", StringComparison.Ordinal) <
                mini.IndexOf("BuildPlacementContext", StringComparison.Ordinal));
            Assert.True(mini.IndexOf("_layoutRevision.ShouldRebuild", StringComparison.Ordinal) <
                mini.IndexOf("SoulMiniPlayerLayout.Calculate", StringComparison.Ordinal));
        }

        [Fact]
        public void WorldTargetingRetainsOcclusionCorrectnessAndUsesCachedReferences()
        {
            string world = UxFixSource.Read("World", "SoulTapeWorldDiscoveryController.cs");
            string targeting = UxFixSource.Method(world, "private void UpdateInteraction", "private SoulTapeWorldPickup FindTargetedPickup");
            Assert.True(targeting.IndexOf("_pickups.Count == 0", StringComparison.Ordinal) < targeting.IndexOf("FindTargetedPickup", StringComparison.Ordinal));
            Assert.Contains("_nextProximityCheck", targeting);
            Assert.Contains("!collectPressed", targeting); // Collection key bypasses proximity throttle.
            string camera = UxFixSource.Method(world, "private Camera ResolveGameplayCamera", "private static bool IsVerifiedFullScreenGameplayCamera");
            Assert.True(camera.IndexOf("return _gameplayCamera", StringComparison.Ordinal) < camera.IndexOf("Camera.main", StringComparison.Ordinal));
            Assert.Contains("Physics.RaycastNonAlloc", world);
            Assert.Contains("count == VisibilityHits.Length ? Physics.RaycastAll", world);
            Assert.Contains("hit.distance < nearest", world);
            Assert.Contains("QueryTriggerInteraction.Ignore", world);
            Assert.Contains("_evaluations.TryGetValue", world);
        }
    }
}
