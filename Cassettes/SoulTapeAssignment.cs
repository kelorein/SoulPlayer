using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SoulPlayer.Library;

namespace SoulPlayer.Cassettes
{
    internal interface ISoulTapeAssignmentSnapshotProvider
    {
        SoulTapeAssignmentSnapshot CurrentAssignments { get; }
        event Action AssignmentChanged;
    }

    internal enum SoulTapeMusicMode
    {
        BuiltInOnly = 0,
        UserOnly = 1,
        MergeBuiltInAndUser = 2
    }

    internal sealed class SoulTapeAssignmentSnapshot
    {
        internal SoulTapeAssignmentSnapshot(SoulTapeAssignmentData data)
        {
            ProfileId = data == null ? string.Empty : data.ProfileId;
            MusicMode = data == null
                ? SoulTapeMusicMode.MergeBuiltInAndUser
                : data.MusicMode;
            LibraryFingerprint = data == null
                ? string.Empty
                : data.LibraryFingerprint;
            AnchorFingerprint = data == null
                ? string.Empty
                : data.AnchorFingerprint;
            Assignments = data == null || data.Assignments == null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(
                    data.Assignments, StringComparer.Ordinal);
        }

        internal string ProfileId { get; private set; }
        internal SoulTapeMusicMode MusicMode { get; private set; }
        internal string LibraryFingerprint { get; private set; }
        internal string AnchorFingerprint { get; private set; }
        internal IReadOnlyDictionary<string, string> Assignments { get; private set; }

        internal bool TryGetTapeId(string anchorId, out string tapeId)
        {
            tapeId = string.Empty;
            return !string.IsNullOrWhiteSpace(anchorId) &&
                   Assignments.TryGetValue(anchorId, out tapeId);
        }
    }

    internal sealed class SoulTapeAssignmentRefreshResult
    {
        internal SoulTapeAssignmentRefreshResult(
            SoulTapeAssignmentSnapshot snapshot,
            int eligibleTracks,
            int enabledAnchors,
            int preserved,
            int added,
            int removed)
        {
            Snapshot = snapshot;
            EligibleTracks = eligibleTracks;
            EnabledAnchors = enabledAnchors;
            PreservedAssignments = preserved;
            NewAssignments = added;
            RemovedInvalidAssignments = removed;
        }

        internal SoulTapeAssignmentSnapshot Snapshot { get; private set; }
        internal int EligibleTracks { get; private set; }
        internal int EnabledAnchors { get; private set; }
        internal int PreservedAssignments { get; private set; }
        internal int NewAssignments { get; private set; }
        internal int RemovedInvalidAssignments { get; private set; }
    }

    internal sealed class SoulTapeAssignmentService
    {
        private readonly ISoulTapeAssignmentStore _store;
        private readonly ISoulTapeLog _log;

        internal SoulTapeAssignmentService(
            ISoulTapeAssignmentStore store,
            ISoulTapeLog log)
        {
            _store = store;
            _log = log;
        }

        internal SoulTapeAssignmentRefreshResult Refresh(
            string profileId,
            SoulTapeMusicMode mode,
            IEnumerable<SoulTapeCatalogEntry> catalogEntries,
            IEnumerable<SoulTapeSpawnAnchor> anchors,
            string builtInRoot,
            IEnumerable<string> userRoots)
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                return EmptyResult();
            }

            List<SoulTapeCatalogEntry> tracks = SelectEligibleTracks(
                catalogEntries, mode, builtInRoot, userRoots);
            List<SoulTapeSpawnAnchor> enabledAnchors = SelectEnabledAnchors(anchors);
            string libraryFingerprint = StableFingerprint(
                tracks.Select(track => track.Id));
            string anchorFingerprint = StableFingerprint(
                enabledAnchors.Select(anchor =>
                    anchor.MapId.ToLowerInvariant() + "|" + anchor.Id));

            SoulTapeAssignmentLoadResult load;
            try
            {
                load = _store.Load(profileId);
            }
            catch (Exception ex)
            {
                _log.Warning(
                    "SoulTape assignment load failed for profile " + profileId +
                    "; rebuilding safely: " + ex.GetBaseException().Message);
                load = new SoulTapeAssignmentLoadResult(
                    SoulTapeAssignmentLoadStatus.Failed, null,
                    ex.GetBaseException().Message);
            }

            if (load.Status == SoulTapeAssignmentLoadStatus.RecoveredFromBackup)
            {
                _log.Warning(
                    "SoulTape assignments recovered from backup for profile " +
                    profileId + ". Primary error: " + load.Error);
            }
            else if (load.Status == SoulTapeAssignmentLoadStatus.Failed)
            {
                _log.Warning(
                    "SoulTape assignments were unreadable for profile " +
                    profileId + "; collection ownership was untouched. " + load.Error);
            }

            Dictionary<string, string> previous = load.Data == null ||
                                                   load.Data.Assignments == null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(
                    load.Data.Assignments, StringComparer.Ordinal);
            HashSet<string> validAnchorIds = new HashSet<string>(
                enabledAnchors.Select(anchor => anchor.Id),
                StringComparer.Ordinal);
            HashSet<string> validTrackIds = new HashSet<string>(
                tracks.Select(track => track.Id),
                StringComparer.Ordinal);
            HashSet<string> usedTracks = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, string> assignments =
                new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, string> pair in previous
                         .OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (validAnchorIds.Contains(pair.Key) &&
                    validTrackIds.Contains(pair.Value) &&
                    usedTracks.Add(pair.Value))
                {
                    assignments[pair.Key] = pair.Value;
                }
            }

            int preserved = assignments.Count;
            int removed = previous.Count - preserved;
            List<string> freeAnchors = enabledAnchors
                .Select(anchor => anchor.Id)
                .Where(id => !assignments.ContainsKey(id))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
            List<string> freeTracks = tracks
                .Select(track => track.Id)
                .Where(id => !usedTracks.Contains(id))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();

            SoulTapeDeterministicRandom random =
                SoulTapeDeterministicRandom.Create(
                    profileId + "\n" + mode + "\n" +
                    libraryFingerprint + "\n" + anchorFingerprint);
            Shuffle(freeAnchors, random);
            Shuffle(freeTracks, random);
            int added = Math.Min(freeAnchors.Count, freeTracks.Count);
            for (int index = 0; index < added; index++)
            {
                assignments[freeAnchors[index]] = freeTracks[index];
            }

            SoulTapeAssignmentData data = new SoulTapeAssignmentData
            {
                ProfileId = profileId,
                MusicMode = mode,
                LibraryFingerprint = libraryFingerprint,
                AnchorFingerprint = anchorFingerprint,
                Assignments = assignments
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value,
                        StringComparer.Ordinal)
            };

            bool changed = load.Data == null ||
                           load.Data.MusicMode != mode ||
                           !string.Equals(
                               load.Data.LibraryFingerprint,
                               libraryFingerprint,
                               StringComparison.Ordinal) ||
                           !string.Equals(
                               load.Data.AnchorFingerprint,
                               anchorFingerprint,
                               StringComparison.Ordinal) ||
                           !SameAssignments(load.Data.Assignments, assignments);
            if (changed)
            {
                try
                {
                    _store.Save(profileId, data);
                }
                catch (Exception ex)
                {
                    _log.Error(
                        "SoulTape assignment save failed for profile " + profileId +
                        ": " + ex.GetBaseException().Message);
                }
            }

            _log.Info(
                "SoulTape assignment refresh: profile=" + profileId +
                ", mode=" + mode +
                ", eligibleTracks=" + tracks.Count +
                ", enabledAnchors=" + enabledAnchors.Count +
                ", preserved=" + preserved +
                ", new=" + added +
                ", removedInvalid=" + removed +
                ", totalAssigned=" + assignments.Count +
                ", source=" + _store.Describe(profileId) + ".");

            if (tracks.Count == 0)
            {
                _log.Info("SoulTape assignment has no eligible tracks; zero cassettes are assigned.");
            }
            else if (enabledAnchors.Count == 0)
            {
                _log.Info("SoulTape assignment has no enabled curated anchors; zero cassettes are assigned.");
            }

            return new SoulTapeAssignmentRefreshResult(
                new SoulTapeAssignmentSnapshot(data),
                tracks.Count,
                enabledAnchors.Count,
                preserved,
                added,
                removed);
        }

        /// <summary>
        /// Compatibility entry point for the deprecated persistent assignment
        /// format. Runtime raid selection uses SoulTapeTrackEligibility directly.
        /// </summary>
        internal static List<SoulTapeCatalogEntry> SelectEligibleTracks(
            IEnumerable<SoulTapeCatalogEntry> entries,
            SoulTapeMusicMode mode,
            string builtInRoot,
            IEnumerable<string> userRoots)
        {
            return SoulTapeTrackEligibility.SelectEligibleTracks(
                entries, mode, builtInRoot, userRoots);
        }

        internal static string StableFingerprint(IEnumerable<string> values)
        {
            string canonical = string.Join("\n", (values ??
                    Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal));
            using (SHA256 sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical)));
            }
        }

        private static List<SoulTapeSpawnAnchor> SelectEnabledAnchors(
            IEnumerable<SoulTapeSpawnAnchor> anchors)
        {
            return (anchors ?? Enumerable.Empty<SoulTapeSpawnAnchor>())
                .Where(anchor => anchor != null &&
                                 anchor.Enabled &&
                                 anchor.Source == SoulTapeSpawnAnchorSource.Curated &&
                                 !string.IsNullOrWhiteSpace(anchor.Id) &&
                                 !string.IsNullOrWhiteSpace(anchor.MapId))
                .GroupBy(anchor => anchor.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(anchor => anchor.Id, StringComparer.Ordinal)
                .ToList();
        }

        private static bool SameAssignments(
            IDictionary<string, string> left,
            IDictionary<string, string> right)
        {
            if (left == null || right == null || left.Count != right.Count)
            {
                return false;
            }
            return left.All(pair => right.TryGetValue(pair.Key, out string value) &&
                                    string.Equals(pair.Value, value, StringComparison.Ordinal));
        }

        private static string NormalizeRoot(string root)
        {
            return SoulPath.NormalizeConfiguredPath(
                root,
                AppDomain.CurrentDomain.BaseDirectory);
        }

        private static bool IsInside(string path, string root)
        {
            return SoulPath.IsInside(path, root);
        }

        private static void Shuffle<T>(
            IList<T> values,
            SoulTapeDeterministicRandom random)
        {
            for (int index = values.Count - 1; index > 0; index--)
            {
                int swap = random.Next(index + 1);
                T temporary = values[index];
                values[index] = values[swap];
                values[swap] = temporary;
            }
        }

        private static string ToHex(byte[] bytes)
        {
            StringBuilder builder = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes)
            {
                builder.Append(value.ToString("x2"));
            }
            return builder.ToString();
        }

        private static SoulTapeAssignmentRefreshResult EmptyResult()
        {
            return new SoulTapeAssignmentRefreshResult(
                new SoulTapeAssignmentSnapshot(null), 0, 0, 0, 0, 0);
        }
    }

    internal sealed class SoulTapeDeterministicRandom
    {
        private ulong _state;

        private SoulTapeDeterministicRandom(ulong state)
        {
            _state = state == 0UL ? 0x9e3779b97f4a7c15UL : state;
        }

        internal static SoulTapeDeterministicRandom Create(string seed)
        {
            byte[] hash;
            using (SHA256 sha = SHA256.Create())
            {
                hash = sha.ComputeHash(Encoding.UTF8.GetBytes(seed ?? string.Empty));
            }
            ulong state = 0UL;
            for (int index = 0; index < 8; index++)
            {
                state |= ((ulong)hash[index]) << (index * 8);
            }
            return new SoulTapeDeterministicRandom(state);
        }

        internal int Next(int exclusiveMaximum)
        {
            if (exclusiveMaximum <= 0)
            {
                throw new ArgumentOutOfRangeException("exclusiveMaximum");
            }
            _state ^= _state >> 12;
            _state ^= _state << 25;
            _state ^= _state >> 27;
            ulong value = _state * 2685821657736338717UL;
            return (int)(value % (uint)exclusiveMaximum);
        }
    }
}
