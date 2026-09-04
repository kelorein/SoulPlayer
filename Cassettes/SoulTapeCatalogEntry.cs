using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SoulPlayer.Library;

namespace SoulPlayer.Cassettes
{
    internal enum SoulTapeRarity
    {
        Common = 0,
        Uncommon = 1,
        Rare = 2,
        Epic = 3,
        Legendary = 4
    }

    /// <summary>
    /// Optional placement hint consumed by the future world-spawn layer.
    /// SpawnGroup is a semantic tag (for example "industrial" or "marked-room"),
    /// not a reference to a BSG asset.
    /// </summary>
    internal sealed class SoulTapeSpawnHint
    {
        internal SoulTapeSpawnHint(string mapId, string spawnGroup, float weight)
        {
            MapId = mapId ?? string.Empty;
            SpawnGroup = spawnGroup ?? string.Empty;
            Weight = Math.Max(0f, weight);
        }

        internal string MapId { get; private set; }
        internal string SpawnGroup { get; private set; }
        internal float Weight { get; private set; }
    }

    internal sealed class SoulTapeCatalogEntry
    {
        private readonly IReadOnlyList<SoulTapeSpawnHint> _spawnHints;

        internal SoulTapeCatalogEntry(
            string id,
            string artist,
            string title,
            string audioReference,
            SoulTapeRarity? rarity,
            IEnumerable<SoulTapeSpawnHint> spawnHints,
            MusicTrack track)
        {
            Id = id ?? string.Empty;
            Artist = artist ?? string.Empty;
            Title = title ?? string.Empty;
            AudioReference = audioReference ?? string.Empty;
            Rarity = rarity;
            _spawnHints = (spawnHints ?? Enumerable.Empty<SoulTapeSpawnHint>()).ToList();
            Track = track;
        }

        internal string Id { get; private set; }
        internal string Artist { get; private set; }
        internal string Title { get; private set; }
        internal string AudioReference { get; private set; }
        internal SoulTapeRarity? Rarity { get; private set; }
        internal IReadOnlyList<SoulTapeSpawnHint> SpawnHints { get { return _spawnHints; } }
        internal MusicTrack Track { get; private set; }

        internal bool IsAudioAvailable
        {
            get
            {
                return Track != null &&
                       !string.IsNullOrWhiteSpace(Track.FilePath) &&
                       File.Exists(Track.FilePath);
            }
        }

        internal SoulTapeCatalogEntry WithTrack(MusicTrack track)
        {
            return new SoulTapeCatalogEntry(
                Id,
                Artist,
                Title,
                AudioReference,
                Rarity,
                _spawnHints,
                track);
        }
    }
}
