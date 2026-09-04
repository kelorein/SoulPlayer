using System;

namespace SoulPlayer.Cassettes
{
    internal enum SoulTapeDiscoveryResult
    {
        NewUnlock = 0,
        AlreadyUnlocked = 1,
        InvalidTape = 2,
        SaveFailed = 3
    }

    internal sealed class SoulTapeDiscoveryEvent
    {
        internal SoulTapeDiscoveryEvent(
            string cassetteId,
            string artist,
            string title,
            SoulTapeRarity? rarity,
            SoulTapeDiscoveryResult result)
        {
            CassetteId = cassetteId ?? string.Empty;
            Artist = artist ?? string.Empty;
            Title = title ?? string.Empty;
            Rarity = rarity;
            Result = result;
        }

        internal string CassetteId { get; private set; }
        internal string Artist { get; private set; }
        internal string Title { get; private set; }
        internal SoulTapeRarity? Rarity { get; private set; }
        internal SoulTapeDiscoveryResult Result { get; private set; }
        internal bool IsNewUnlock { get { return Result == SoulTapeDiscoveryResult.NewUnlock; } }
    }

    internal sealed class SoulTapeDiscoveryService
    {
        private readonly ISoulTapeCatalog _catalog;
        private readonly SoulTapeCollection _collection;
        private readonly ISoulTapeLog _log;

        internal SoulTapeDiscoveryService(
            ISoulTapeCatalog catalog,
            SoulTapeCollection collection,
            ISoulTapeLog log)
        {
            _catalog = catalog;
            _collection = collection;
            _log = log;
        }

        internal event Action<SoulTapeDiscoveryEvent> Discovered;

        internal SoulTapeDiscoveryResult Discover(string cassetteId)
        {
            SoulTapeCatalogEntry tape;
            if (string.IsNullOrWhiteSpace(cassetteId) ||
                !_catalog.TryGetTape(cassetteId, out tape))
            {
                RaiseDiscovered(new SoulTapeDiscoveryEvent(
                    cassetteId,
                    string.Empty,
                    string.Empty,
                    null,
                    SoulTapeDiscoveryResult.InvalidTape));
                return SoulTapeDiscoveryResult.InvalidTape;
            }

            SoulTapeDiscoveryResult result;
            if (_collection.IsUnlocked(cassetteId))
            {
                result = SoulTapeDiscoveryResult.AlreadyUnlocked;
            }
            else if (_collection.UnlockTape(cassetteId))
            {
                result = SoulTapeDiscoveryResult.NewUnlock;
            }
            else if (_collection.IsUnlocked(cassetteId))
            {
                result = SoulTapeDiscoveryResult.AlreadyUnlocked;
            }
            else
            {
                result = SoulTapeDiscoveryResult.SaveFailed;
            }

            RaiseDiscovered(new SoulTapeDiscoveryEvent(
                tape.Id,
                tape.Artist,
                tape.Title,
                tape.Rarity,
                result));
            return result;
        }

        private void RaiseDiscovered(SoulTapeDiscoveryEvent payload)
        {
            Action<SoulTapeDiscoveryEvent> handler = Discovered;
            if (handler == null)
            {
                return;
            }

            foreach (Delegate subscriber in handler.GetInvocationList())
            {
                try
                {
                    ((Action<SoulTapeDiscoveryEvent>)subscriber)(payload);
                }
                catch (Exception ex)
                {
                    _log.Warning(
                        "SoulTape discovery subscriber failed and was isolated: " +
                        ex.GetBaseException().Message);
                }
            }
        }
    }
}
