using System;
using System.Linq;

namespace SoulPlayer.Cassettes
{
    internal static class SoulTapeRecorderSelector
    {
        internal static SoulTapeCatalogEntry Select(SoulTapeCollection collection)
        {
            if (collection == null || !collection.IsLoaded)
            {
                return null;
            }

            var unlocked = collection.GetUnlockedTapes();
            SoulTapeCatalogEntry starter = unlocked.FirstOrDefault(tape =>
                string.Equals(tape.Id, SoulTapeCatalog.StarterTapeId, StringComparison.Ordinal) &&
                tape.IsAudioAvailable);

            return starter ?? unlocked.FirstOrDefault(tape => tape.IsAudioAvailable);
        }
    }
}
