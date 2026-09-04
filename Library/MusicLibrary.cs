using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SoulPlayer.Library
{
    internal sealed class MusicLibrary
    {
        private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(
            new[] { ".flac", ".mp3", ".ogg", ".wav" },
            StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> IgnoredDirectoryNames = new HashSet<string>(
            new[] { "downloading", "incomplete", "partial", "temp", "tmp", ".stfolder" },
            StringComparer.OrdinalIgnoreCase);

        private readonly object _sync = new object();
        private readonly Action<string> _logInfo;
        private readonly Action<string> _logError;
        private IReadOnlyList<MusicTrack> _tracks = Array.AsReadOnly(new MusicTrack[0]);
        private Task<ScanResult> _scanTask;
        private List<string> _pendingRoots;
        private bool _hasAppliedScan;

        internal event Action Changed;
        internal event Action ScanStateChanged;
        internal int Revision { get; private set; }
        internal bool IsReady { get { return _hasAppliedScan && _scanTask == null && _pendingRoots == null; } }

        internal MusicLibrary()
            : this(
                message => Plugin.Log.LogInfo(message),
                message => Plugin.Log.LogError(message))
        {
        }

        internal MusicLibrary(Action<string> logInfo, Action<string> logError)
        {
            _logInfo = logInfo ?? delegate { };
            _logError = logError ?? delegate { };
        }

        internal bool IsScanning
        {
            get { return _scanTask != null && !_scanTask.IsCompleted; }
        }

        internal bool HasAppliedScan
        {
            get { return _hasAppliedScan; }
        }

        internal IReadOnlyList<MusicTrack> Tracks
        {
            get
            {
                lock (_sync)
                {
                    return _tracks;
                }
            }
        }

        internal void BeginScan(IEnumerable<string> folders)
        {
            List<string> roots = new List<string>();
            foreach (string folder in folders ?? Enumerable.Empty<string>())
            {
                string normalized = SoulPath.NormalizeConfiguredPath(
                    folder,
                    AppDomain.CurrentDomain.BaseDirectory);
                if (string.IsNullOrWhiteSpace(normalized) || !Directory.Exists(normalized))
                {
                    _logInfo("SoulPlayer skipped a missing or inaccessible music folder: " + folder);
                    continue;
                }

                if (!roots.Contains(normalized, SoulPath.Comparer))
                {
                    roots.Add(normalized);
                }
            }

            if (_scanTask != null)
            {
                _pendingRoots = roots;
                return;
            }

            _hasAppliedScan = false;
            _scanTask = Task.Run(() => Scan(roots));
            _logInfo("SoulPlayer library scan started: roots=" + roots.Count + " libraryReady=False.");
            NotifySubscribers(ScanStateChanged, "ScanStateChanged");
        }

        internal bool TryApplyCompletedScan(out ScanResult result)
        {
#if SOULPLAYER_PERF
            SoulPlayer.Utils.RecurringWorkProfiler.Begin(SoulPlayer.Utils.RecurringWorkArea.Library);
            try
            {
#endif
            result = null;
            Task<ScanResult> task = _scanTask;
            if (task == null || !task.IsCompleted) return false;
            _scanTask = null;
            try
            {
                result = task.Result;
                lock (_sync) { _tracks = Array.AsReadOnly(result.Tracks.ToArray()); }
                SoulPlayer.Utils.RecurringWorkProfiler.Mark(SoulPlayer.Utils.RecurringWorkEvent.LibraryPublication);
                _hasAppliedScan = true;
                Revision++;
                _logInfo("SoulPlayer library scan finished: " + result.Tracks.Count +
                    " tracks, " + result.DuplicateCount + " duplicates ignored, " +
                    result.InaccessibleCount + " inaccessible folders skipped.");
                // Publish readiness before Changed. Subscribers see the new objects
                // and applied-scan state together, including pending follow-up scans.
                _logInfo("SoulPlayer library Changed: revision=" + Revision +
                    " tracks=" + result.Tracks.Count + " libraryReady=" + IsReady + ".");
                NotifySubscribers(Changed, "Changed");
            }
            catch (Exception ex)
            {
                result = new ScanResult(new List<MusicTrack>(), 0, 1, ex.Message);
                _hasAppliedScan = true; // Previous usable tracks remain available.
                _logError("SoulPlayer library scan failed: " + ex);
            }
            if (_pendingRoots != null)
            {
                List<string> pending = _pendingRoots;
                _pendingRoots = null;
                BeginScan(pending);
            }
            NotifySubscribers(ScanStateChanged, "ScanStateChanged");
            return true;
        #if SOULPLAYER_PERF
            }
            finally { SoulPlayer.Utils.RecurringWorkProfiler.End(SoulPlayer.Utils.RecurringWorkArea.Library); }
#endif
        }

        private void NotifySubscribers(Action handlers, string name)
        {
            if (handlers == null) return;
            foreach (Action handler in handlers.GetInvocationList())
            {
                try { handler(); }
                catch (Exception ex) { _logError("SoulPlayer library " + name + " subscriber failed: " + ex.Message); }
            }
        }

        internal static ScanResult Scan(IReadOnlyList<string> roots)
        {
            List<MusicTrack> tracks = new List<MusicTrack>();
            HashSet<string> duplicateKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int duplicateCount = 0;
            int inaccessibleCount = 0;

            foreach (string root in roots)
            {
                Stack<string> pending = new Stack<string>();
                pending.Push(root);

                while (pending.Count > 0)
                {
                    string directory = pending.Pop();
                    try
                    {
                        foreach (string child in Directory.EnumerateDirectories(directory))
                        {
                            string name = Path.GetFileName(child);
                            if (!IgnoredDirectoryNames.Contains(name))
                            {
                                pending.Push(child);
                            }
                        }

                        foreach (string file in Directory.EnumerateFiles(directory))
                        {
                            if (!SupportedExtensions.Contains(Path.GetExtension(file)))
                            {
                                continue;
                            }

                            FileInfo info = new FileInfo(file);
                            if (!info.Exists || info.Length <= 0)
                            {
                                continue;
                            }

                            MusicTrack track = new MusicTrack(info.FullName, info.Length);
                            if (!duplicateKeys.Add(track.DuplicateKey))
                            {
                                duplicateCount++;
                                continue;
                            }

                            tracks.Add(track);
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        inaccessibleCount++;
                    }
                    catch (IOException)
                    {
                        inaccessibleCount++;
                    }
                }
            }

            tracks = tracks
                .OrderBy(track => track.Artist, StringComparer.OrdinalIgnoreCase)
                .ThenBy(track => track.Album, StringComparer.OrdinalIgnoreCase)
                .ThenBy(track => track.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new ScanResult(tracks, duplicateCount, inaccessibleCount, string.Empty);
        }
    }

    internal sealed class ScanResult
    {
        internal ScanResult(
            List<MusicTrack> tracks,
            int duplicateCount,
            int inaccessibleCount,
            string error)
        {
            Tracks = tracks;
            DuplicateCount = duplicateCount;
            InaccessibleCount = inaccessibleCount;
            Error = error;
        }

        internal List<MusicTrack> Tracks { get; private set; }
        internal int DuplicateCount { get; private set; }
        internal int InaccessibleCount { get; private set; }
        internal string Error { get; private set; }
    }
}
