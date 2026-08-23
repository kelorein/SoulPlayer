using System;
using SoulPlayer.Library;

namespace SoulPlayer.Cassettes
{
    /// <summary>
    /// Pure orchestration layer shared by the Unity runtime adapter and offline
    /// integration tests. It owns no Unity or EFT lifecycle state.
    /// </summary>
    internal sealed class SoulTapeCollectionHost : IDisposable
    {
        private readonly MusicLibrary _musicLibrary;
        private readonly SoulTapeCatalog _catalog;
        private readonly SoulTapeCollection _collection;
        private readonly IProfileIdProvider _profileIdProvider;
        private bool _initialized;

        internal SoulTapeCollectionHost(
            MusicLibrary musicLibrary,
            SoulTapeCatalog catalog,
            SoulTapeCollection collection,
            IProfileIdProvider profileIdProvider)
        {
            _musicLibrary = musicLibrary;
            _catalog = catalog;
            _collection = collection;
            _profileIdProvider = profileIdProvider;
        }

        internal void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            _musicLibrary.Changed += OnLibraryChanged;
            _catalog.Refresh(_musicLibrary.Tracks);
            RefreshProfile(string.Empty);
        }

        internal bool RefreshProfile(string fallbackProfileId)
        {
            string profileId = _profileIdProvider.GetActiveProfileId(fallbackProfileId);
            return !string.IsNullOrWhiteSpace(profileId) && _collection.BindProfile(profileId);
        }

        public void Dispose()
        {
            if (!_initialized)
            {
                return;
            }

            _initialized = false;
            _musicLibrary.Changed -= OnLibraryChanged;
        }

        private void OnLibraryChanged()
        {
            _catalog.Refresh(_musicLibrary.Tracks);
        }
    }
}
