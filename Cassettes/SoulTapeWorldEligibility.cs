using System.Collections.Generic;
using System.Linq;

namespace SoulPlayer.Cassettes
{
    internal static class SoulTapeWorldEligibility
    {
        internal static IReadOnlyList<SoulTapeCatalogEntry> GetEligibleTapes(
            ISoulTapeCatalog catalog,
            SoulTapeCollection collection)
        {
            if (catalog == null || collection == null || !collection.IsLoaded)
            {
                return new SoulTapeCatalogEntry[0];
            }

            return catalog.GetAllTapes()
                .Where(tape =>
                    tape != null &&
                    !string.IsNullOrWhiteSpace(tape.Id) &&
                    tape.Id != SoulTapeCatalog.StarterTapeId &&
                    tape.Rarity.HasValue &&
                    tape.IsAudioAvailable &&
                    !collection.IsUnlocked(tape.Id))
                .OrderBy(tape => tape.Id, System.StringComparer.Ordinal)
                .ToList();
        }

        internal static IReadOnlyList<SoulTapeSpawnPlanEntry> GetAssignedTapes(
            SoulTapeAssignmentSnapshot snapshot,
            IEnumerable<SoulTapeSpawnAnchor> anchors,
            ISoulTapeCatalog catalog,
            SoulTapeCollection collection)
        {
            if (snapshot == null || catalog == null || collection == null ||
                !collection.IsLoaded)
            {
                return new SoulTapeSpawnPlanEntry[0];
            }

            List<SoulTapeSpawnPlanEntry> result =
                new List<SoulTapeSpawnPlanEntry>();
            foreach (SoulTapeSpawnAnchor anchor in (anchors ??
                         Enumerable.Empty<SoulTapeSpawnAnchor>())
                     .Where(anchor => anchor != null && anchor.Enabled)
                     .OrderBy(anchor => anchor.Id, System.StringComparer.Ordinal))
            {
                string tapeId;
                SoulTapeCatalogEntry tape;
                if (!snapshot.TryGetTapeId(anchor.Id, out tapeId) ||
                    !catalog.TryGetTape(tapeId, out tape) ||
                    tape == null ||
                    !tape.IsAudioAvailable ||
                    collection.IsUnlocked(tape.Id))
                {
                    continue;
                }
                result.Add(new SoulTapeSpawnPlanEntry(tape, anchor));
            }
            return result;
        }

        internal static IReadOnlyList<SoulTapeCatalogEntry> GetModeEligibleTapes(
            SoulTapeEligibleCatalogSnapshot snapshot,
            ISoulTapeCatalog catalog,
            SoulTapeCollection collection)
        {
            if (snapshot == null || catalog == null || collection == null ||
                !collection.IsLoaded ||
                !string.Equals(
                    snapshot.ProfileId,
                    collection.ProfileId,
                    System.StringComparison.Ordinal))
            {
                return new SoulTapeCatalogEntry[0];
            }

            List<SoulTapeCatalogEntry> result =
                new List<SoulTapeCatalogEntry>();
            foreach (string id in snapshot.TapeIds)
            {
                SoulTapeCatalogEntry tape;
                if (catalog.TryGetTape(id, out tape) &&
                    tape != null && tape.IsAudioAvailable &&
                    tape.Id != SoulTapeCatalog.StarterTapeId)
                {
                    result.Add(tape);
                }
            }
            return result
                .GroupBy(tape => tape.Id, System.StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(tape => tape.Id, System.StringComparer.Ordinal)
                .ToList();
        }
    }
}
