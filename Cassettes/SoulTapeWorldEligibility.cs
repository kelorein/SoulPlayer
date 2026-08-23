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
    }
}
