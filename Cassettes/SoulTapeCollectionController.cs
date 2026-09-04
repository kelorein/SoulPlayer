using System.IO;
using EFT;
using SoulPlayer.Configuration;
using SoulPlayer.Library;
using UnityEngine;

namespace SoulPlayer.Cassettes
{
    /// <summary>
    /// Connects library scans and the active SPT profile to the pure catalog and
    /// collection data layers. It does not create world items or recorder visuals.
    /// </summary>
    internal sealed class SoulTapeCollectionController : MonoBehaviour,
        ISoulTapeEligibleCatalogProvider
    {
        private const float ProfileCheckInterval = 2f;

        private SoulTapeCollectionHost _host;
        private SoulPlayerSettings _settings;
        private MusicLibrary _musicLibrary;
        private SoulTapeCatalog _catalog;
        private SoulTapeCollection _collection;
        private float _nextProfileCheck;

        public event System.Action EligibleCatalogChanged;
        public SoulTapeEligibleCatalogSnapshot CurrentEligibleCatalog { get; private set; }

        internal void Initialize(
            SoulPlayerSettings settings,
            MusicLibrary musicLibrary,
            SoulTapeCatalog catalog,
            SoulTapeCollection collection,
            IProfileIdProvider profileIdProvider)
        {
            _settings = settings;
            _musicLibrary = musicLibrary;
            _catalog = catalog;
            _collection = collection;
            _host = new SoulTapeCollectionHost(
                musicLibrary,
                catalog,
                collection,
                profileIdProvider);
            _host.Initialize();
            _musicLibrary.Changed += OnLibraryChanged;
            _settings.CassetteMusicModeChanged += OnCassetteMusicModeChanged;
            RefreshEligibleCatalogIfReady();
        }

        internal bool EnsureProfile(Player raidPlayer)
        {
            string fallbackProfileId = raidPlayer == null ? string.Empty : raidPlayer.ProfileId;
            return EnsureProfileId(fallbackProfileId);
        }

        internal bool EnsureProfileId(string fallbackProfileId)
        {
            if (_host == null || !_host.RefreshProfile(fallbackProfileId))
            {
                return false;
            }
            if (CurrentEligibleCatalog == null ||
                !string.Equals(
                    CurrentEligibleCatalog.ProfileId,
                    _collection.ProfileId,
                    System.StringComparison.Ordinal) ||
                CurrentEligibleCatalog.MusicMode != _settings.CassetteMusicMode)
            {
                RefreshEligibleCatalogIfReady();
            }
            return true;
        }

        internal static string GetDefaultCollectionFolder()
        {
            return Path.Combine(BepInEx.Paths.ConfigPath, "SoulPlayer", "collections");
        }

        internal static string GetDefaultAssignmentFolder()
        {
            // Legacy location retained only so older tests/tools can inspect or
            // migrate files. Runtime raid selection never reads this folder.
            return Path.Combine(BepInEx.Paths.ConfigPath, "SoulPlayer", "assignments");
        }

        private void Update()
        {
#if SOULPLAYER_PERF
            SoulPlayer.Utils.RecurringWorkProfiler.Begin(SoulPlayer.Utils.RecurringWorkArea.Collection);
            try
            {
#endif
            if (Time.unscaledTime < _nextProfileCheck)
            {
                return;
            }

            _nextProfileCheck = Time.unscaledTime + ProfileCheckInterval;
            if (_host != null)
            {
                string previousProfile = _collection == null
                    ? string.Empty
                    : _collection.ProfileId;
                if (_host.RefreshProfile(string.Empty) &&
                    !string.Equals(
                        previousProfile,
                        _collection.ProfileId,
                        System.StringComparison.Ordinal))
                {
                    RefreshEligibleCatalogIfReady();
                }
            }
        #if SOULPLAYER_PERF
            }
            finally { SoulPlayer.Utils.RecurringWorkProfiler.End(SoulPlayer.Utils.RecurringWorkArea.Collection); }
#endif
        }

        private void OnDestroy()
        {
            if (_musicLibrary != null)
            {
                _musicLibrary.Changed -= OnLibraryChanged;
            }
            if (_settings != null)
            {
                _settings.CassetteMusicModeChanged -= OnCassetteMusicModeChanged;
            }
            if (_host != null)
            {
                _host.Dispose();
                _host = null;
            }
        }

        private void OnLibraryChanged()
        {
            RefreshEligibleCatalogIfReady();
        }

        private void OnCassetteMusicModeChanged(
            SoulTapeMusicMode ignored)
        {
            RefreshEligibleCatalogIfReady();
        }

        private void RefreshEligibleCatalogIfReady()
        {
            if (_collection == null || !_collection.IsLoaded ||
                _musicLibrary == null ||
                !_musicLibrary.HasAppliedScan)
            {
                return;
            }

            System.Collections.Generic.List<SoulTapeCatalogEntry> eligible =
                SoulTapeTrackEligibility.SelectEligibleTracks(
                _catalog.GetAllTapes(),
                _settings.CassetteMusicMode,
                _settings.DefaultMusicFolder,
                _settings.GetFolders());
            CurrentEligibleCatalog = new SoulTapeEligibleCatalogSnapshot(
                _collection.ProfileId,
                _settings.CassetteMusicMode,
                eligible.ConvertAll(tape => tape.Id));
            _collection.RefreshDiscoveredMetadata();
            Plugin.Log.LogInfo(
                "SoulTape eligible catalog refreshed: profile=" +
                _collection.ProfileId + ", mode=" + _settings.CassetteMusicMode +
                ", tracks=" + eligible.Count +
                ". Legacy persistent anchor assignments are ignored.");
            RaiseEligibleCatalogChanged();
        }

        private void RaiseEligibleCatalogChanged()
        {
            System.Action handler = EligibleCatalogChanged;
            if (handler == null)
            {
                return;
            }
            foreach (System.Delegate subscriber in handler.GetInvocationList())
            {
                try
                {
                    ((System.Action)subscriber)();
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogWarning(
                        "SoulTape eligible-catalog Changed subscriber failed and was isolated: " +
                        ex.GetBaseException().Message);
                }
            }
        }
    }
}
