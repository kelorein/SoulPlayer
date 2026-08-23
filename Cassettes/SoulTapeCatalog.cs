using System;
using System.Collections.Generic;
using System.Linq;
using SoulPlayer.Library;

namespace SoulPlayer.Cassettes
{
    internal interface ISoulTapeCatalog
    {
        bool TryGetTape(string id, out SoulTapeCatalogEntry tape);
        IReadOnlyList<SoulTapeCatalogEntry> GetAllTapes();
    }

    /// <summary>
    /// Curated identity record. ExpectedAudioFingerprint is optional while the
    /// approved files are being finalized, but once populated it becomes required
    /// evidence before metadata can bind a library track to this stable ID.
    /// </summary>
    internal sealed class KnownSoulTapeDefinition
    {
        internal KnownSoulTapeDefinition(
            string id,
            string artist,
            string title,
            SoulTapeRarity rarity,
            string expectedAudioFingerprint)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Known cassette ID is required.", "id");
            }

            if (!string.IsNullOrWhiteSpace(expectedAudioFingerprint) &&
                !TrackFingerprint.IsValidSha256(expectedAudioFingerprint))
            {
                throw new ArgumentException(
                    "Known cassette fingerprints must be complete SHA-256 hex values.",
                    "expectedAudioFingerprint");
            }

            Id = id;
            Artist = artist ?? string.Empty;
            Title = title ?? string.Empty;
            Rarity = rarity;
            ExpectedAudioFingerprint = string.IsNullOrWhiteSpace(expectedAudioFingerprint)
                ? string.Empty
                : expectedAudioFingerprint.ToLowerInvariant();
            MatchKey = SoulTapeCatalog.NormalizeMetadata(Artist) + "|" +
                       SoulTapeCatalog.NormalizeMetadata(Title);
        }

        internal string Id { get; private set; }
        internal string Artist { get; private set; }
        internal string Title { get; private set; }
        internal SoulTapeRarity Rarity { get; private set; }
        internal string ExpectedAudioFingerprint { get; private set; }
        internal string MatchKey { get; private set; }

        internal SoulTapeCatalogEntry CreateEntry(MusicTrack track)
        {
            return new SoulTapeCatalogEntry(
                Id,
                Artist,
                Title,
                "default:" + Artist + " - " + Title,
                Rarity,
                null,
                track);
        }
    }

    internal sealed class SoulTapeCatalog : ISoulTapeCatalog
    {
        internal const string StarterTapeId = "soul-tape.scott-buckley.the-long-dark";
        private const string GeneratedIdPrefix = "soul-tape.audio.";

        // TODO before release packaging: record and populate the SHA-256 fingerprint
        // for each exact approved distributable audio file. Null preserves current
        // metadata matching only until those vetted hashes are available.
        private static readonly IReadOnlyList<KnownSoulTapeDefinition> DefaultKnownTapes =
            new List<KnownSoulTapeDefinition>
            {
                Known("soul-tape.anders.frostbite", "Anders", "Frostbite", SoulTapeRarity.Uncommon, null),
                Known("soul-tape.anders.false-awakenings", "Anders", "False Awakenings", SoulTapeRarity.Rare, null),
                Known("soul-tape.anders.into-world-unknown", "Anders", "Into World Unknown", SoulTapeRarity.Rare, null),
                Known("soul-tape.anders.ex-nihilo", "Anders", "Ex Nihilo", SoulTapeRarity.Epic, null),
                Known("soul-tape.scott-buckley.electric-dreams", "Scott Buckley", "Electric Dreams", SoulTapeRarity.Uncommon, null),
                Known("soul-tape.scott-buckley.resonance", "Scott Buckley", "Resonance", SoulTapeRarity.Rare, null),
                Known("soul-tape.scott-buckley.signal-to-noise", "Scott Buckley", "Signal to Noise", SoulTapeRarity.Rare, null),
                Known(StarterTapeId, "Scott Buckley", "The Long Dark", SoulTapeRarity.Common, null)
            };

        private readonly object _sync = new object();
        private readonly ISoulTapeLog _log;
        private readonly IReadOnlyList<KnownSoulTapeDefinition> _knownTapes;
        private Dictionary<string, SoulTapeCatalogEntry> _tapes;

        internal SoulTapeCatalog(ISoulTapeLog log)
            : this(log, DefaultKnownTapes)
        {
        }

        internal SoulTapeCatalog(
            ISoulTapeLog log,
            IEnumerable<KnownSoulTapeDefinition> knownTapes)
        {
            _log = log;
            _knownTapes = (knownTapes ?? Enumerable.Empty<KnownSoulTapeDefinition>()).ToList();
            _tapes = CreateKnownCatalog();
        }

        internal event Action Changed;

        internal void Refresh(IEnumerable<MusicTrack> tracks)
        {
            Dictionary<string, SoulTapeCatalogEntry> refreshed = CreateKnownCatalog();

            lock (_sync)
            {
                // Retain generated metadata for the rest of this session if a file
                // disappears during a rescan. Unlock IDs remain persisted even
                // across restarts by SoulTapeCollection.
                foreach (SoulTapeCatalogEntry existing in _tapes.Values)
                {
                    if (existing.Id.StartsWith(GeneratedIdPrefix, StringComparison.Ordinal))
                    {
                        refreshed[existing.Id] = existing.WithTrack(null);
                    }
                }
            }

            int availableCount = 0;
            int generatedCount = 0;
            int skippedFingerprintCount = 0;
            foreach (MusicTrack track in tracks ?? Enumerable.Empty<MusicTrack>())
            {
                KnownSoulTapeDefinition known = FindKnown(track.Artist, track.Title);
                if (known != null && CanBindKnownTape(known, track))
                {
                    refreshed[known.Id] = known.CreateEntry(track);
                    availableCount++;
                    continue;
                }

                if (!TrackFingerprint.IsValidSha256(track.AudioFingerprint))
                {
                    skippedFingerprintCount++;
                    _log.Warning(
                        "SoulTape catalog skipped persistent generated registration for " +
                        track.Artist + " - " + track.Title +
                        " because no valid SHA-256 audio fingerprint is available.");
                    continue;
                }

                string id = CreateGeneratedId(track.AudioFingerprint);
                if (refreshed.ContainsKey(id) && refreshed[id].Track != null)
                {
                    _log.Warning(
                        "SoulTape catalog ignored duplicate audio identity " + id +
                        " for " + track.Artist + " - " + track.Title + ".");
                    continue;
                }

                refreshed[id] = new SoulTapeCatalogEntry(
                    id,
                    track.Artist,
                    track.Title,
                    "library-sha256:" + track.AudioFingerprint.ToLowerInvariant(),
                    null,
                    null,
                    track);
                availableCount++;
                generatedCount++;
            }

            lock (_sync)
            {
                _tapes = refreshed;
            }

            _log.Info(
                "SoulTape catalog refreshed: " + refreshed.Count + " entries, " +
                availableCount + " audio files available, " + generatedCount +
                " generated library cassette IDs, " + skippedFingerprintCount +
                " tracks skipped without fingerprints.");

            RaiseChanged();
        }

        public bool TryGetTape(string id, out SoulTapeCatalogEntry tape)
        {
            tape = null;
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            lock (_sync)
            {
                return _tapes.TryGetValue(id, out tape);
            }
        }

        public IReadOnlyList<SoulTapeCatalogEntry> GetAllTapes()
        {
            lock (_sync)
            {
                return _tapes.Values
                    .OrderBy(tape => tape.Artist, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(tape => tape.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        internal static string NormalizeMetadata(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return new string(value
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
        }

        private bool CanBindKnownTape(KnownSoulTapeDefinition known, MusicTrack track)
        {
            if (string.IsNullOrEmpty(known.ExpectedAudioFingerprint))
            {
                return true;
            }

            string actual = track.AudioFingerprint ?? string.Empty;
            if (TrackFingerprint.IsValidSha256(actual) &&
                string.Equals(
                    known.ExpectedAudioFingerprint,
                    actual,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            _log.Warning(
                "SoulTape known cassette metadata matched " + known.Id + " (" +
                known.Artist + " - " + known.Title + ") but its audio fingerprint did not. " +
                "Expected " + known.ExpectedAudioFingerprint + ", actual " +
                (string.IsNullOrWhiteSpace(actual) ? "<missing>" : actual) +
                "; the known ID was not assigned.");
            return false;
        }

        private Dictionary<string, SoulTapeCatalogEntry> CreateKnownCatalog()
        {
            return _knownTapes.ToDictionary(
                definition => definition.Id,
                definition => definition.CreateEntry(null),
                StringComparer.Ordinal);
        }

        private KnownSoulTapeDefinition FindKnown(string artist, string title)
        {
            string key = NormalizeMetadata(artist) + "|" + NormalizeMetadata(title);
            return _knownTapes.FirstOrDefault(definition => definition.MatchKey == key);
        }

        private static string CreateGeneratedId(string fingerprint)
        {
            if (!TrackFingerprint.IsValidSha256(fingerprint))
            {
                throw new ArgumentException(
                    "A valid SHA-256 fingerprint is required for generated cassette IDs.",
                    "fingerprint");
            }

            return GeneratedIdPrefix + fingerprint.ToLowerInvariant().Substring(0, 32);
        }

        private static KnownSoulTapeDefinition Known(
            string id,
            string artist,
            string title,
            SoulTapeRarity rarity,
            string expectedAudioFingerprint)
        {
            return new KnownSoulTapeDefinition(
                id,
                artist,
                title,
                rarity,
                expectedAudioFingerprint);
        }

        private void RaiseChanged()
        {
            Action handler = Changed;
            if (handler == null)
            {
                return;
            }

            foreach (Delegate subscriber in handler.GetInvocationList())
            {
                try
                {
                    ((Action)subscriber)();
                }
                catch (Exception ex)
                {
                    _log.Warning(
                        "SoulTape catalog Changed subscriber failed and was isolated: " +
                        ex.GetBaseException().Message);
                }
            }
        }
    }
}
