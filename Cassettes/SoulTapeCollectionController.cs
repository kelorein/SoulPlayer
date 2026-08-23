using System.IO;
using EFT;
using SoulPlayer.Library;
using UnityEngine;

namespace SoulPlayer.Cassettes
{
    /// <summary>
    /// Connects library scans and the active SPT profile to the pure catalog and
    /// collection data layers. It does not create world items or recorder visuals.
    /// </summary>
    internal sealed class SoulTapeCollectionController : MonoBehaviour
    {
        private const float ProfileCheckInterval = 2f;

        private SoulTapeCollectionHost _host;
        private float _nextProfileCheck;

        internal void Initialize(
            MusicLibrary musicLibrary,
            SoulTapeCatalog catalog,
            SoulTapeCollection collection,
            IProfileIdProvider profileIdProvider)
        {
            _host = new SoulTapeCollectionHost(
                musicLibrary,
                catalog,
                collection,
                profileIdProvider);
            _host.Initialize();
        }

        internal bool EnsureProfile(Player raidPlayer)
        {
            string fallbackProfileId = raidPlayer == null ? string.Empty : raidPlayer.ProfileId;
            return _host != null && _host.RefreshProfile(fallbackProfileId);
        }

        internal static string GetDefaultCollectionFolder()
        {
            return Path.Combine(BepInEx.Paths.ConfigPath, "SoulPlayer", "collections");
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextProfileCheck)
            {
                return;
            }

            _nextProfileCheck = Time.unscaledTime + ProfileCheckInterval;
            if (_host != null)
            {
                _host.RefreshProfile(string.Empty);
            }
        }

        private void OnDestroy()
        {
            if (_host != null)
            {
                _host.Dispose();
                _host = null;
            }
        }
    }
}
