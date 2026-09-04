using System;
using EFT;

namespace SoulPlayer.Recorder
{
    /// <summary>
    /// Logical presentation ownership for the 2D overlay. Acquisition is
    /// synchronous and deliberately performs no EFT hands/item operations.
    /// </summary>
    internal sealed class SoulRecorderScreenOverlayTransition :
        ISoulRecorderHandsControllerTransition
    {
        private Player _player;
        private bool _owned;

        public event Action<bool, string> InteractionReleased;
        public bool IsAcquiring { get { return false; } }
        public bool IsOwned { get { return _owned; } }
        public bool IsRestoring { get { return false; } }
        public bool IsBusy { get { return false; } }
        public Player Player { get { return _player; } }

        public void Acquire(Player player, Action<bool> completed)
        {
            if (player == null || completed == null)
            {
                completed?.Invoke(false);
                return;
            }
            _player = player;
            _owned = true;
            Plugin.Log.LogInfo(
                "SoulRecorder OVERLAY interaction acquired; EFT hands remain unchanged.");
            completed(true);
        }

        public void Restore(string reason)
        {
            Release(true, reason);
        }

        public void Abandon(string reason)
        {
            Release(true, reason);
        }

        public void ManualUpdate(float unscaledTime)
        {
        }

        private void Release(bool succeeded, string reason)
        {
            if (!_owned && _player == null) return;
            _owned = false;
            _player = null;
            Action<bool, string> released = InteractionReleased;
            if (released != null)
            {
                released(succeeded,
                    "screen-space overlay released (" + reason + ")");
            }
        }
    }
}
