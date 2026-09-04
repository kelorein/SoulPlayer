using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace SoulPlayer.Utils
{
    internal enum RecurringWorkArea
    {
        Input, MiniPlayer, Overlay, GameState, Readiness, Library, World,
        Audio, Recorder, Collection, Window, Other, Count
    }

    internal enum RecurringWorkEvent
    {
        ReadinessInspection, LayoutRebuild, InputBindingRebuild, LibraryPublication,
        CameraResolve, ConfigurationLookup, CountdownSceneSearch, MixerScan, Count
    }

    // No instrumentation call sites, timers or fields exist in normal Release.
    // Inclusive category times may nest; Total measures outermost work only.
    internal static class RecurringWorkProfiler
    {
#if SOULPLAYER_PERF
        private static readonly long[] Calls = new long[(int)RecurringWorkArea.Count];
        private static readonly long[] Ticks = new long[(int)RecurringWorkArea.Count];
        private static readonly long[] Starts = new long[64];
        private static readonly long[] Events = new long[(int)RecurringWorkEvent.Count];
        private static int _depth;
        private static long _totalCalls, _totalTicks;
        private static long _nextSummary = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 5;
#endif
        [Conditional("SOULPLAYER_PERF")]
        internal static void Begin(RecurringWorkArea area)
        {
#if SOULPLAYER_PERF
            Starts[_depth++] = Stopwatch.GetTimestamp();
#endif
        }

        [Conditional("SOULPLAYER_PERF")]
        internal static void End(RecurringWorkArea area)
        {
#if SOULPLAYER_PERF
            long elapsed = Stopwatch.GetTimestamp() - Starts[--_depth];
            Calls[(int)area]++;
            Ticks[(int)area] += elapsed;
            if (_depth == 0) { _totalCalls++; _totalTicks += elapsed; }
#endif
        }

        [Conditional("SOULPLAYER_PERF")]
        internal static void Mark(RecurringWorkEvent item)
        {
#if SOULPLAYER_PERF
            Events[(int)item]++;
#endif
        }

        [Conditional("SOULPLAYER_PERF")]
        internal static void Flush()
        {
#if SOULPLAYER_PERF
            long now = Stopwatch.GetTimestamp();
            if (now < _nextSummary) return;
            _nextSummary = now + Stopwatch.Frequency * 5;
            StringBuilder line = new StringBuilder("SoulPlayer PERF aggregate (>=5s; inclusive areas, exclusive total): ");
            for (int i = 0; i < Calls.Length; i++)
            {
                line.Append((RecurringWorkArea)i).Append(" calls=").Append(Calls[i])
                    .Append(" ms=").Append((Ticks[i] * 1000d / Stopwatch.Frequency).ToString("0.000", CultureInfo.InvariantCulture)).Append("; ");
                Calls[i] = Ticks[i] = 0;
            }
            line.Append("Total calls=").Append(_totalCalls).Append(" ms=")
                .Append((_totalTicks * 1000d / Stopwatch.Frequency).ToString("0.000", CultureInfo.InvariantCulture));
            for (int i = 0; i < Events.Length; i++)
            {
                line.Append("; ").Append((RecurringWorkEvent)i).Append('=').Append(Events[i]);
                Events[i] = 0;
            }
            _totalCalls = _totalTicks = 0;
            Plugin.Log.LogInfo(line.ToString());
#endif
        }
    }
}
