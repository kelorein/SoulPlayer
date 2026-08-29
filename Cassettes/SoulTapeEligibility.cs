using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SoulPlayer.Library;

namespace SoulPlayer.Cassettes
{
    /// <summary>
    /// Supplies the current mode-filtered collectible catalog. This is deliberately
    /// independent from raid anchor selection and permanent collection ownership.
    /// </summary>
    internal interface ISoulTapeEligibleCatalogProvider
    {
        SoulTapeEligibleCatalogSnapshot CurrentEligibleCatalog { get; }
        event Action EligibleCatalogChanged;
    }

    internal sealed class SoulTapeEligibleCatalogSnapshot
    {
        internal SoulTapeEligibleCatalogSnapshot(
            string profileId,
            SoulTapeMusicMode musicMode,
            IEnumerable<string> tapeIds)
        {
            ProfileId = profileId ?? string.Empty;
            MusicMode = musicMode;
            TapeIds = (tapeIds ?? Enumerable.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
        }

        internal string ProfileId { get; private set; }
        internal SoulTapeMusicMode MusicMode { get; private set; }
        internal IReadOnlyList<string> TapeIds { get; private set; }
    }

    internal static class SoulTapeTrackEligibility
    {
        internal static List<SoulTapeCatalogEntry> SelectEligibleTracks(
            IEnumerable<SoulTapeCatalogEntry> entries,
            SoulTapeMusicMode mode,
            string builtInRoot,
            IEnumerable<string> userRoots)
        {
            List<string> normalizedUserRoots = (userRoots ??
                    Enumerable.Empty<string>())
                .Select(NormalizeRoot)
                .Where(root => !string.IsNullOrEmpty(root))
                .Distinct(SoulPath.Comparer)
                .ToList();
            string normalizedBuiltInRoot = NormalizeRoot(builtInRoot);

            return (entries ?? Enumerable.Empty<SoulTapeCatalogEntry>())
                .Where(entry => entry != null &&
                                !string.IsNullOrWhiteSpace(entry.Id) &&
                                entry.Id != SoulTapeCatalog.StarterTapeId &&
                                entry.IsAudioAvailable)
                .Select(entry => new
                {
                    Entry = entry,
                    BuiltIn = IsInside(entry.Track.FilePath, normalizedBuiltInRoot),
                    User = normalizedUserRoots.Any(root =>
                        IsInside(entry.Track.FilePath, root))
                })
                .Where(candidate =>
                    mode == SoulTapeMusicMode.BuiltInOnly
                        ? candidate.BuiltIn
                        : mode == SoulTapeMusicMode.UserOnly
                            ? candidate.User
                            : candidate.BuiltIn || candidate.User)
                .GroupBy(candidate => candidate.Entry.Id, StringComparer.Ordinal)
                .Select(group => group.First().Entry)
                .OrderBy(entry => entry.Id, StringComparer.Ordinal)
                .ToList();
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
    }
}
