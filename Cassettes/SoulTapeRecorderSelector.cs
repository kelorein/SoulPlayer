using System;
using System.Linq;

namespace SoulPlayer.Cassettes
{
    internal sealed class SoulTapeRecorderSelectionResult
    {
        internal SoulTapeRecorderSelectionResult(
            SoulTapeCatalogEntry tape,
            string explicitCassetteId,
            bool usedRuntimeFallback)
        {
            Tape = tape;
            ExplicitCassetteId = explicitCassetteId ?? string.Empty;
            UsedRuntimeFallback =
                Tape != null &&
                !string.IsNullOrEmpty(ExplicitCassetteId) &&
                usedRuntimeFallback;
        }

        internal SoulTapeCatalogEntry Tape { get; private set; }
        internal string ExplicitCassetteId { get; private set; }
        internal bool HasExplicitSelection
        {
            get { return !string.IsNullOrEmpty(ExplicitCassetteId); }
        }
        internal bool UsedRuntimeFallback { get; private set; }
    }

    internal static class SoulTapeRecorderSelector
    {
        internal static SoulTapeCatalogEntry Select(SoulTapeCollection collection)
        {
            return Resolve(collection).Tape;
        }

        internal static SoulTapeRecorderSelectionResult Resolve(
            SoulTapeCollection collection)
        {
            if (collection == null || !collection.IsLoaded)
            {
                return new SoulTapeRecorderSelectionResult(null, string.Empty, false);
            }

            var unlocked = collection.GetUnlockedTapes();
            string explicitId = collection.GetSelectedRecorderTapeId();
            if (!string.IsNullOrEmpty(explicitId))
            {
                SoulTapeCatalogEntry explicitTape = unlocked.FirstOrDefault(tape =>
                    string.Equals(tape.Id, explicitId, StringComparison.Ordinal) &&
                    tape.IsAudioAvailable);
                if (explicitTape != null)
                {
                    return new SoulTapeRecorderSelectionResult(
                        explicitTape,
                        explicitId,
                        false);
                }

                return new SoulTapeRecorderSelectionResult(
                    SelectLegacy(unlocked),
                    explicitId,
                    true);
            }

            return new SoulTapeRecorderSelectionResult(
                SelectLegacy(unlocked),
                string.Empty,
                false);
        }

        private static SoulTapeCatalogEntry SelectLegacy(
            System.Collections.Generic.IReadOnlyList<SoulTapeCatalogEntry> unlocked)
        {
            SoulTapeCatalogEntry starter = unlocked.FirstOrDefault(tape =>
                string.Equals(tape.Id, SoulTapeCatalog.StarterTapeId, StringComparison.Ordinal) &&
                tape.IsAudioAvailable);

            return starter ?? unlocked.FirstOrDefault(tape => tape.IsAudioAvailable);
        }
    }
}
