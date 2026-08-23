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

        internal SoulTapeCollectionProjection(
            ISoulTapeCatalog catalog,
            SoulTapeCollection collection)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _collection = collection ?? throw new ArgumentNullException(nameof(collection));
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

            List<SoulTapeCollectionCard> all = _catalog.GetAllTapes()
                .Where(tape => tape != null && tape.Rarity.HasValue)
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
