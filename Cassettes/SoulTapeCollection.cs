using System;
using System.Collections.Generic;
using System.Linq;

namespace SoulPlayer.Cassettes
{
    internal sealed class SoulTapeCollection
    {
        private readonly object _sync = new object();
        private readonly ISoulTapeCatalog _catalog;
        private readonly ISoulTapeCollectionStore _store;
        private readonly ISoulTapeLog _log;
        private HashSet<string> _unlocked = new HashSet<string>(StringComparer.Ordinal);
        private HashSet<string> _favorites = new HashSet<string>(StringComparer.Ordinal);
        private string _profileId = string.Empty;
        private bool _loaded;

        internal SoulTapeCollection(
            ISoulTapeCatalog catalog,
            ISoulTapeCollectionStore store,
            ISoulTapeLog log)
        {
            _catalog = catalog;
            _store = store;
            _log = log;
        }

        internal event Action Changed;

        internal bool IsLoaded { get { lock (_sync) { return _loaded; } } }
        internal string ProfileId { get { lock (_sync) { return _profileId; } } }

        internal bool BindProfile(string profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                return false;
            }

            lock (_sync)
            {
                if (_loaded && string.Equals(_profileId, profileId, StringComparison.Ordinal))
                {
                    return true;
                }

                SoulTapeLoadResult result;
                try
                {
                    result = _store.Load(profileId);
                }
                catch (Exception ex)
                {
                    _loaded = false;
                    _profileId = profileId;
                    _log.Error(
                        "SoulTape collection load failed for profile " + profileId + ": " +
                        ex.GetBaseException().Message);
                    return false;
                }

                if (result.Status == SoulTapeLoadStatus.Failed)
                {
                    _loaded = false;
                    _profileId = profileId;
                    _unlocked.Clear();
                    _favorites.Clear();
                    _log.Error(
                        "SoulTape collection is unreadable and was left untouched for profile " +
                        profileId + ": " + result.Error);
                    return false;
                }

                _profileId = profileId;
                _unlocked = new HashSet<string>(StringComparer.Ordinal);
                _favorites = new HashSet<string>(StringComparer.Ordinal);

                if (result.Data != null)
                {
                    AddValidIds(_unlocked, result.Data.UnlockedCassetteIds);
                    AddValidIds(_favorites, result.Data.FavoriteCassetteIds);
                    _favorites.IntersectWith(_unlocked);
                }

                _loaded = true;

                if (result.Status == SoulTapeLoadStatus.Missing)
                {
                    _unlocked.Add(SoulTapeCatalog.StarterTapeId);
                    _log.Info(
                        "SoulTape granted starter cassette " + SoulTapeCatalog.StarterTapeId +
                        " to new profile " + profileId + ".");
                    if (!SaveLocked("new collection"))
                    {
                        _loaded = false;
                        return false;
                    }
                }
                else if (result.Status == SoulTapeLoadStatus.RecoveredFromBackup)
                {
                    _log.Warning(
                        "SoulTape collection recovered from backup for profile " + profileId +
                        ". Primary error: " + result.Error);
                }

                int unresolved = _unlocked.Count(id => !_catalog.TryGetTape(id, out _));
                _log.Info(
                    "SoulTape collection loaded for profile " + profileId + ": " +
                    _unlocked.Count + " unlocked, " + _favorites.Count + " favorites, " +
                    unresolved + " currently missing from the catalog. Source: " +
                    _store.Describe(profileId));
            }

            RaiseChanged();
            return true;
        }

        internal bool UnlockTape(string id)
        {
            SoulTapeCatalogEntry ignored;
            lock (_sync)
            {
                if (!_loaded)
                {
                    _log.Warning("SoulTape unlock ignored because no profile collection is loaded.");
                    return false;
                }

                if (!_catalog.TryGetTape(id, out ignored))
                {
                    _log.Warning("SoulTape unlock rejected unknown cassette ID '" + id + "'.");
                    return false;
                }

                if (!_unlocked.Add(id))
                {
                    _log.Info("SoulTape cassette already unlocked: " + id + ".");
                    return false;
                }

                if (!SaveLocked("unlock " + id))
                {
                    _unlocked.Remove(id);
                    return false;
                }

                _log.Info("SoulTape UNLOCK -> " + id + " for profile " + _profileId + ".");
            }

            RaiseChanged();
            return true;
        }

        internal bool IsUnlocked(string id)
        {
            lock (_sync)
            {
                return _loaded && !string.IsNullOrWhiteSpace(id) && _unlocked.Contains(id);
            }
        }

        internal bool SetFavorite(string id, bool favorite)
        {
            lock (_sync)
            {
                if (!_loaded)
                {
                    _log.Warning("SoulTape favorite change ignored because no profile collection is loaded.");
                    return false;
                }

                if (!_unlocked.Contains(id))
                {
                    _log.Warning("SoulTape cannot favorite a locked cassette: " + id + ".");
                    return false;
                }

                bool changed = favorite ? _favorites.Add(id) : _favorites.Remove(id);
                if (!changed)
                {
                    _log.Info(
                        "SoulTape favorite unchanged: " + id + " = " + favorite + ".");
                    return false;
                }

                if (!SaveLocked("favorite " + id + " = " + favorite))
                {
                    if (favorite)
                    {
                        _favorites.Remove(id);
                    }
                    else
                    {
                        _favorites.Add(id);
                    }
                    return false;
                }

                _log.Info(
                    "SoulTape FAVORITE -> " + id + " = " + favorite +
                    " for profile " + _profileId + ".");
            }

            RaiseChanged();
            return true;
        }

        internal bool IsFavorite(string id)
        {
            lock (_sync)
            {
                return _loaded && !string.IsNullOrWhiteSpace(id) && _favorites.Contains(id);
            }
        }

        internal IReadOnlyList<SoulTapeCatalogEntry> GetUnlockedTapes()
        {
            lock (_sync)
            {
                return ResolveLocked(_unlocked);
            }
        }

        internal IReadOnlyList<SoulTapeCatalogEntry> GetFavoriteTapes()
        {
            lock (_sync)
            {
                return ResolveLocked(_favorites);
            }
        }

        private List<SoulTapeCatalogEntry> ResolveLocked(IEnumerable<string> ids)
        {
            List<SoulTapeCatalogEntry> result = new List<SoulTapeCatalogEntry>();
            foreach (string id in ids)
            {
                SoulTapeCatalogEntry tape;
                if (_catalog.TryGetTape(id, out tape))
                {
                    result.Add(tape);
                }
            }

            return result
                .OrderBy(tape => tape.Artist, StringComparer.OrdinalIgnoreCase)
                .ThenBy(tape => tape.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private bool SaveLocked(string operation)
        {
            SoulTapeCollectionData data = new SoulTapeCollectionData
            {
                ProfileId = _profileId,
                UnlockedCassetteIds = _unlocked.OrderBy(id => id, StringComparer.Ordinal).ToList(),
                FavoriteCassetteIds = _favorites.OrderBy(id => id, StringComparer.Ordinal).ToList()
            };

            try
            {
                _store.Save(_profileId, data);
                _log.Info(
                    "SoulTape collection saved (" + operation + "): " +
                    data.UnlockedCassetteIds.Count + " unlocked, " +
                    data.FavoriteCassetteIds.Count + " favorites -> " +
                    _store.Describe(_profileId));
                return true;
            }
            catch (Exception ex)
            {
                _log.Error(
                    "SoulTape collection save failed (" + operation + ") for profile " +
                    _profileId + ": " + ex.GetBaseException().Message);
                return false;
            }
        }

        private static void AddValidIds(HashSet<string> target, IEnumerable<string> ids)
        {
            foreach (string id in ids ?? Enumerable.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(id))
                {
                    target.Add(id.Trim());
                }
            }
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
                        "SoulTape collection Changed subscriber failed and was isolated: " +
                        ex.GetBaseException().Message);
                }
            }
        }
    }
}
