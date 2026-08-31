using System;
using System.Diagnostics;
using System.Reflection;
using BepInEx.Configuration;
using SoulPlayer.Recorder;
using SoulPlayer.UI;
using UnityEngine;
using Xunit;
using Xunit.Abstractions;

namespace SoulPlayer.CollectionTests
{
    [Trait("Validation", "Performance")]
    public sealed class RecurringWorkBenchmarkTests
    {
        private readonly ITestOutputHelper _output;
        public RecurringWorkBenchmarkTests(ITestOutputHelper output) { _output = output; }

        [Fact]
        public void MeasureStableShortcutAndLayoutCosts()
        {
            SoulRecorderInput input = new SoulRecorderInput();
            KeyboardShortcut start = new KeyboardShortcut(KeyCode.M);
            KeyboardShortcut next = new KeyboardShortcut(KeyCode.N);
            Func<KeyCode, bool> none = key => false;
            Action<string> warn = message => { throw new InvalidOperationException(message); };
            Action poll = () => input.Poll(start, next, true, false, false, none, none, warn);
            SoulPlayerVolumeHudRect last = new SoulPlayerVolumeHudRect();
            Action layout = () => last = SoulMiniPlayerLayout.Calculate(1920, 1080, 1f,
                SoulMiniPlayerCorner.BottomRight, new SoulPlayerVolumeHudPlacementContext());
            Measure("unchanged input", poll);
            Measure("layout calculation", layout);
            Assert.True(last.Width > 0f);
        }

        private void Measure(string name, Action action)
        {
            const int iterations = 100000;
            for (int i = 0; i < 1000; i++) action();
            MethodInfo allocationMethod = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread",
                BindingFlags.Public | BindingFlags.Static);
            Func<long> allocated = allocationMethod == null ? (Func<long>)(() => 0L) :
                (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), allocationMethod);
            long bytes = allocated();
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++) action();
            double elapsed = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
            bytes = allocated() - bytes;
            _output.WriteLine(name + ": calls=" + iterations + " elapsedMs=" + elapsed.ToString("0.000") +
                " allocatedBytes=" + bytes + " allocationCounter=" + (allocationMethod != null));
        }
    }
}
