using System;
using System.Collections.Generic;
using System.Linq;

namespace SoulPlayer.Cassettes
{
    internal enum SoulTapeCollectionFilter
    {
        All = 0,
        Discovered = 1,
        Favorites = 2,
        Undiscovered = 3
    }

    /// <summary>
    /// UI-safe cassette projection. Locked entries deliberately retain only their
    /// stable internal ID; presentation metadata is never copied into the card.
    /// </summary>
    internal sealed class SoulTapeCollectionCard
    {
        internal SoulTapeCollectionCard(
            string id,
            bool isDiscovered,
            bool isFavorite,
            bool isRecorderSelected,
            bool isAudioAvailable,
            string artist,
            string title,
            SoulTapeRarity? rarity)
        {
            Id = id ?? string.Empty;
            IsDiscovered = isDiscovered;
            IsFavorite = isDiscovered && isFavorite;
            IsRecorderSelected = isDiscovered && isRecorderSelected;
            IsAudioAvailable = isDiscovered && isAudioAvailable;
            Artist = isDiscovered ? artist ?? string.Empty : string.Empty;
            Title = isDiscovered ? title ?? string.Empty : string.Empty;
            Rarity = isDiscovered ? rarity : null;
        }

        internal string Id { get; private set; }
        internal bool IsDiscovered { get; private set; }
        internal bool IsFavorite { get; private set; }
        internal bool IsRecorderSelected { get; private set; }
        internal bool IsAudioAvailable { get; private set; }
        internal string Artist { get; private set; }
        internal string Title { get; private set; }
        internal SoulTapeRarity? Rarity { get; private set; }
    }

    internal sealed class SoulTapeCollectionSnapshot
    {
        internal SoulTapeCollectionSnapshot(
            bool isAvailable,
            SoulTapeCollectionFilter filter,
            int discoveredCount,
            int totalCount,
            bool hasRecorderSelection,
            string recorderArtist,
            string recorderTitle,
            IReadOnlyList<SoulTapeCollectionCard> entries)
        {
            IsAvailable = isAvailable;
            Filter = filter;
            DiscoveredCount = discoveredCount;
            TotalCount = totalCount;
            HasRecorderSelection = hasRecorderSelection;
            RecorderArtist = recorderArtist ?? string.Empty;
            RecorderTitle = recorderTitle ?? string.Empty;
            Entries = entries ?? new List<SoulTapeCollectionCard>();
        }

        internal bool IsAvailable { get; private set; }
        internal SoulTapeCollectionFilter Filter { get; private set; }
        internal int DiscoveredCount { get; private set; }
        internal int TotalCount { get; private set; }
        internal bool HasRecorderSelection { get; private set; }
        internal string RecorderArtist { get; private set; }
        internal string RecorderTitle { get; private set; }
        internal bool HasVisibleRecorderSelection
        {
            get
            {
                return HasRecorderSelection &&
                       !string.IsNullOrEmpty(RecorderArtist) &&
                       !string.IsNullOrEmpty(RecorderTitle);
            }
        }
        internal IReadOnlyList<SoulTapeCollectionCard> Entries { get; private set; }
    }

    internal sealed class SoulTapeCollectionProjection
    {
        private readonly ISoulTapeCatalog _catalog;
        private readonly SoulTapeCollection _collection;
        private readonly ISoulTapeEligibleCatalogProvider _eligibleCatalog;
        private readonly ISoulTapeAssignmentSnapshotProvider _assignments;

        internal SoulTapeCollectionProjection(
            ISoulTapeCatalog catalog,
            SoulTapeCollection collection)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _collection = collection ?? throw new ArgumentNullException(nameof(collection));
        }

        internal SoulTapeCollectionProjection(
            ISoulTapeCatalog catalog,
            SoulTapeCollection collection,
            ISoulTapeEligibleCatalogProvider eligibleCatalog)
            : this(catalog, collection)
        {
            _eligibleCatalog = eligibleCatalog;
        }

        /// <summary>
        /// Compatibility constructor for the deprecated persistent assignment
        /// projection. New runtime UI uses ISoulTapeEligibleCatalogProvider.
        /// </summary>
        internal SoulTapeCollectionProjection(
            ISoulTapeCatalog catalog,
            SoulTapeCollection collection,
            ISoulTapeAssignmentSnapshotProvider assignments)
            : this(catalog, collection)
        {
            _assignments = assignments;
        }

        internal SoulTapeCollectionSnapshot Project(SoulTapeCollectionFilter filter)
        {
            if (!_collection.IsLoaded)
            {
                return new SoulTapeCollectionSnapshot(
                    false,
                    filter,
                    0,
                    0,
                    false,
                    string.Empty,
                    string.Empty,
                    new List<SoulTapeCollectionCard>());
            }

            List<SoulTapeCollectionCard> all = GetEffectiveCatalog()
                .Select(ProjectCard)
                .OrderBy(card => card.IsDiscovered ? 0 : 1)
                .ThenBy(card => card.Id, StringComparer.Ordinal)
                .ToList();

            int discoveredCount = all.Count(card => card.IsDiscovered);
            string selectedId = _collection.GetSelectedRecorderTapeId();
            SoulTapeCollectionCard selectedCard = all.FirstOrDefault(card =>
                card.IsRecorderSelected);
            IEnumerable<SoulTapeCollectionCard> filtered = all;
            switch (filter)
            {
                case SoulTapeCollectionFilter.Discovered:
                    filtered = all.Where(card => card.IsDiscovered);
                    break;
                case SoulTapeCollectionFilter.Favorites:
                    filtered = all.Where(card => card.IsDiscovered && card.IsFavorite);
                    break;
                case SoulTapeCollectionFilter.Undiscovered:
                    filtered = all.Where(card => !card.IsDiscovered);
                    break;
            }

            return new SoulTapeCollectionSnapshot(
                true,
                filter,
                discoveredCount,
                all.Count,
                !string.IsNullOrEmpty(selectedId),
                selectedCard == null ? string.Empty : selectedCard.Artist,
                selectedCard == null ? string.Empty : selectedCard.Title,
                filtered.ToList());
        }

        private IReadOnlyList<SoulTapeCatalogEntry> GetEffectiveCatalog()
        {
            if (_eligibleCatalog == null && _assignments == null)
            {
                // Compatibility path for callers that deliberately project the
                // legacy curated catalog without a profile assignment provider.
                return _catalog.GetAllTapes()
                    .Where(tape => tape != null && tape.Rarity.HasValue)
                    .ToList();
            }

            HashSet<string> ids = new HashSet<string>(
                _collection.GetUnlockedTapeIds(), StringComparer.Ordinal);
            SoulTapeEligibleCatalogSnapshot eligible =
                _eligibleCatalog == null
                    ? null
                    : _eligibleCatalog.CurrentEligibleCatalog;
            if (eligible != null && string.Equals(
                    eligible.ProfileId,
                    _collection.ProfileId,
                    StringComparison.Ordinal))
            {
                ids.UnionWith(eligible.TapeIds);
            }

            SoulTapeAssignmentSnapshot snapshot = _assignments == null
                ? null
                : _assignments.CurrentAssignments;
            if (snapshot != null && string.Equals(
                    snapshot.ProfileId,
                    _collection.ProfileId,
                    StringComparison.Ordinal))
            {
                ids.UnionWith(snapshot.Assignments.Values);
            }

            List<SoulTapeCatalogEntry> result =
                new List<SoulTapeCatalogEntry>();
            foreach (string id in ids.OrderBy(value => value, StringComparer.Ordinal))
            {
                SoulTapeCatalogEntry tape;
                if (_catalog.TryGetTape(id, out tape) ||
                    _collection.TryResolveHistoricalTape(id, out tape))
                {
                    result.Add(tape);
                }
            }
            return result;
        }

        private SoulTapeCollectionCard ProjectCard(SoulTapeCatalogEntry tape)
        {
            bool discovered = _collection.IsUnlocked(tape.Id);
            return new SoulTapeCollectionCard(
                tape.Id,
                discovered,
                discovered && _collection.IsFavorite(tape.Id),
                discovered && _collection.IsRecorderTape(tape.Id),
                discovered && tape.IsAudioAvailable,
                tape.Artist,
                tape.Title,
                tape.Rarity);
        }
    }
}
