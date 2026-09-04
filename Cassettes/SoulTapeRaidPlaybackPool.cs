using System;
using System.Collections.Generic;
using System.Linq;

namespace SoulPlayer.Cassettes
{
    internal enum SoulTapeRaidPlaybackMode
    {
        FavoritesOnly = 0,
        Discovered = 1,
        FavoritesFirst = 2
    }

    internal enum SoulTapeRaidPlaybackEmptyReason
    {
        None = 0,
        NoFavoriteTapes = 1,
        NoDiscoveredTapes = 2,
        NoAvailableAudio = 3
    }

    internal sealed class SoulTapeRaidPlaybackSelection
    {
        internal SoulTapeRaidPlaybackSelection(
            SoulTapeCatalogEntry tape,
            SoulTapeRaidPlaybackEmptyReason emptyReason)
        {
            Tape = tape;
            EmptyReason = tape == null ? emptyReason :
                SoulTapeRaidPlaybackEmptyReason.None;
        }

        internal SoulTapeCatalogEntry Tape { get; private set; }
        internal SoulTapeRaidPlaybackEmptyReason EmptyReason { get; private set; }

        internal string EmptyHeading
        {
            get
            {
                switch (EmptyReason)
                {
                    case SoulTapeRaidPlaybackEmptyReason.NoFavoriteTapes:
                        return "NO FAVORITE TAPES";
                    case SoulTapeRaidPlaybackEmptyReason.NoDiscoveredTapes:
                        return "NO TAPES DISCOVERED";
                    case SoulTapeRaidPlaybackEmptyReason.NoAvailableAudio:
                        return "NO TAPES AVAILABLE";
                    default:
                        return string.Empty;
                }
            }
        }

        internal string EmptyDetail
        {
            get
            {
                switch (EmptyReason)
                {
                    case SoulTapeRaidPlaybackEmptyReason.NoFavoriteTapes:
                        return "Favorite discovered cassettes in Collection";
                    case SoulTapeRaidPlaybackEmptyReason.NoDiscoveredTapes:
                        return "Find SoulTapes during raids";
                    case SoulTapeRaidPlaybackEmptyReason.NoAvailableAudio:
                        return "Restore the missing cassette audio files";
                    default:
                        return string.Empty;
                }
            }
        }
    }

    /// <summary>
    /// An in-memory, per-raid shuffle bag. It reconciles collection/favorite/audio
    /// changes only when the recorder asks for its next tape, so a pool update can
    /// never interrupt the cassette that is already playing.
    /// </summary>
    internal sealed class SoulTapeRaidPlaybackPool
    {
        private readonly Random _random;
        private readonly List<string> _remaining = new List<string>();
        private readonly HashSet<string> _servedInCycle =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _cycleFavorites =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _cycleNonFavorites =
            new HashSet<string>(StringComparer.Ordinal);
        private SoulTapeRaidPlaybackMode _cycleMode;
        private bool _hasCycleMode;
        private string _lastCassetteId = string.Empty;

        internal SoulTapeRaidPlaybackPool()
            : this(new Random())
        {
        }

        internal SoulTapeRaidPlaybackPool(Random random)
        {
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        internal SoulTapeRaidPlaybackSelection TakeNext(
            SoulTapeCollection collection,
            SoulTapeRaidPlaybackMode mode)
        {
            if (collection == null || !collection.IsLoaded)
            {
                return new SoulTapeRaidPlaybackSelection(
                    null, SoulTapeRaidPlaybackEmptyReason.NoDiscoveredTapes);
            }

            IReadOnlyList<SoulTapeCatalogEntry> discovered =
                collection.GetUnlockedTapes();
            List<SoulTapeCatalogEntry> available = discovered
                .Where(tape => tape != null && tape.IsAudioAvailable)
                .GroupBy(tape => tape.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(tape => tape.Id, StringComparer.Ordinal)
                .ToList();
            List<SoulTapeCatalogEntry> favorite = available
                .Where(tape => collection.IsFavorite(tape.Id))
                .ToList();

            List<SoulTapeCatalogEntry> favoritePhase;
            List<SoulTapeCatalogEntry> nonFavoritePhase;
            switch (mode)
            {
                case SoulTapeRaidPlaybackMode.FavoritesOnly:
                    favoritePhase = favorite;
                    nonFavoritePhase = new List<SoulTapeCatalogEntry>();
                    break;
                case SoulTapeRaidPlaybackMode.Discovered:
                    favoritePhase = new List<SoulTapeCatalogEntry>();
                    nonFavoritePhase = available;
                    break;
                default:
                    favoritePhase = favorite;
                    HashSet<string> favoriteIds = new HashSet<string>(
                        favorite.Select(tape => tape.Id),
                        StringComparer.Ordinal);
                    nonFavoritePhase = available
                        .Where(tape => !favoriteIds.Contains(tape.Id))
                        .ToList();
                    break;
            }

            if (favoritePhase.Count == 0 && nonFavoritePhase.Count == 0)
            {
                SoulTapeRaidPlaybackEmptyReason reason =
                    mode == SoulTapeRaidPlaybackMode.FavoritesOnly
                        ? SoulTapeRaidPlaybackEmptyReason.NoFavoriteTapes
                        : discovered.Count == 0
                            ? SoulTapeRaidPlaybackEmptyReason.NoDiscoveredTapes
                            : SoulTapeRaidPlaybackEmptyReason.NoAvailableAudio;
                ClearCycle(false);
                return new SoulTapeRaidPlaybackSelection(null, reason);
            }

            Dictionary<string, SoulTapeCatalogEntry> byId = favoritePhase
                .Concat(nonFavoritePhase)
                .ToDictionary(tape => tape.Id, StringComparer.Ordinal);
            Reconcile(mode, favoritePhase, nonFavoritePhase);
            if (_remaining.Count == 0)
            {
                StartNewCycle(favoritePhase, nonFavoritePhase);
            }

            string selectedId = _remaining[0];
            _remaining.RemoveAt(0);
            _servedInCycle.Add(selectedId);
            _lastCassetteId = selectedId;
            return new SoulTapeRaidPlaybackSelection(
                byId[selectedId], SoulTapeRaidPlaybackEmptyReason.None);
        }

        internal void Reset()
        {
            ClearCycle(true);
        }

        internal IReadOnlyList<string> RemainingIds
        {
            get { return _remaining.ToList(); }
        }

        private void Reconcile(
            SoulTapeRaidPlaybackMode mode,
            IEnumerable<SoulTapeCatalogEntry> favoritePhase,
            IEnumerable<SoulTapeCatalogEntry> nonFavoritePhase)
        {
            HashSet<string> favorites = new HashSet<string>(
                (favoritePhase ?? Enumerable.Empty<SoulTapeCatalogEntry>())
                    .Select(tape => tape.Id),
                StringComparer.Ordinal);
            HashSet<string> nonFavorites = new HashSet<string>(
                (nonFavoritePhase ?? Enumerable.Empty<SoulTapeCatalogEntry>())
                    .Select(tape => tape.Id),
                StringComparer.Ordinal);
            HashSet<string> eligible = new HashSet<string>(favorites,
                StringComparer.Ordinal);
            eligible.UnionWith(nonFavorites);

            bool changed = !_hasCycleMode || _cycleMode != mode ||
                !_cycleFavorites.SetEquals(favorites) ||
                !_cycleNonFavorites.SetEquals(nonFavorites);
            if (!changed)
            {
                return;
            }

            if (!_hasCycleMode || _cycleMode != mode)
            {
                _servedInCycle.Clear();
            }
            else
            {
                _servedInCycle.RemoveWhere(id => !eligible.Contains(id));
            }

            _cycleMode = mode;
            _hasCycleMode = true;
            ReplaceSet(_cycleFavorites, favorites);
            ReplaceSet(_cycleNonFavorites, nonFavorites);
            RebuildUnserved(favorites, nonFavorites);
            if (_servedInCycle.Count == 0)
            {
                AvoidImmediateCycleBoundaryRepeat();
            }
        }

        private void StartNewCycle(
            IEnumerable<SoulTapeCatalogEntry> favoritePhase,
            IEnumerable<SoulTapeCatalogEntry> nonFavoritePhase)
        {
            _servedInCycle.Clear();
            RebuildUnserved(
                new HashSet<string>(favoritePhase.Select(tape => tape.Id),
                    StringComparer.Ordinal),
                new HashSet<string>(nonFavoritePhase.Select(tape => tape.Id),
                    StringComparer.Ordinal));
            AvoidImmediateCycleBoundaryRepeat();
        }

        private void RebuildUnserved(
            IEnumerable<string> favoriteIds,
            IEnumerable<string> nonFavoriteIds)
        {
            List<string> favorites = favoriteIds
                .Where(id => !_servedInCycle.Contains(id))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
            List<string> nonFavorites = nonFavoriteIds
                .Where(id => !_servedInCycle.Contains(id))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
            Shuffle(favorites);
            Shuffle(nonFavorites);
            _remaining.Clear();
            _remaining.AddRange(favorites);
            _remaining.AddRange(nonFavorites);
        }

        private void AvoidImmediateCycleBoundaryRepeat()
        {
            if (_remaining.Count <= 1 || !string.Equals(
                    _remaining[0], _lastCassetteId, StringComparison.Ordinal))
            {
                return;
            }

            // Preserve FavoritesFirst's phase guarantee. If the favorite phase
            // contains only the last-played tape, a non-favorite cannot be moved
            // ahead of it without violating the selected mode.
            int firstPhaseCount = _cycleFavorites.Count > 0
                ? _cycleFavorites.Count
                : _remaining.Count;
            if (firstPhaseCount <= 1)
            {
                return;
            }

            int swap = 1 + _random.Next(firstPhaseCount - 1);
            string first = _remaining[0];
            _remaining[0] = _remaining[swap];
            _remaining[swap] = first;
        }

        private void ClearCycle(bool clearLastSelection)
        {
            _remaining.Clear();
            _servedInCycle.Clear();
            _cycleFavorites.Clear();
            _cycleNonFavorites.Clear();
            _hasCycleMode = false;
            if (clearLastSelection)
            {
                _lastCassetteId = string.Empty;
            }
        }

        private static void ReplaceSet(
            HashSet<string> destination,
            IEnumerable<string> values)
        {
            destination.Clear();
            destination.UnionWith(values);
        }

        private void Shuffle(IList<string> values)
        {
            for (int index = values.Count - 1; index > 0; index--)
            {
                int swap = _random.Next(index + 1);
                string current = values[index];
                values[index] = values[swap];
                values[swap] = current;
            }
        }
    }
}
