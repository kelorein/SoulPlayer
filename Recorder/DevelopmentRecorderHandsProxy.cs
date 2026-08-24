#if SOULPLAYER_RECORDER_DEV_PROXY
using System;
using System.Reflection;
using EFT;
using SoulPlayer.Library;

namespace SoulPlayer.Recorder
{
    /// <summary>
    /// Development-only compatibility probe retained from the audio prototype.
    /// It is excluded from normal builds and is not part of the recorder design.
    /// </summary>
    internal sealed class DevelopmentRecorderHandsProxy : ISoulRecorderHandsView
    {
        private Player _player;
        private bool _raised;
        private bool _loggedMissingProxy;

        public float TapeInsertionSeconds { get { return 0.25f; } }
        public float TapeEjectionSeconds { get { return 0.25f; } }

        public void OnInteractionEntered(Player player)
        {
            _player = player;
            _loggedMissingProxy = false;
        }

        public void OnTapeInsertionStarted(MusicTrack tape)
        {
        }

        public void OnTapeInserted(MusicTrack tape)
        {
        }

        public void OnPlaybackChanged(bool isPlaying)
        {
            TrySetState(isPlaying);
        }

        public void OnTapeEjectionStarted(MusicTrack tape)
        {
            TrySetState(false);
        }

        public void OnTapeEjected(MusicTrack tape)
        {
        }

        public void OnInteractionExited()
        {
            TrySetState(false);
            _player = null;
        }

        public void ForceReset()
        {
            TrySetState(false);
            _player = null;
        }

        private void TrySetState(bool raised)
        {
            try
            {
                object hands = _player == null ? null : _player.HandsController;
                if (hands == null)
                {
                    LogMissing(raised, "no local hands controller is available");
                    return;
                }

                RadioTransmitterController radio = hands as RadioTransmitterController;
                if (radio != null)
                {
                    radio.SetAim(raised);
                    _raised = raised;
                    _loggedMissingProxy = false;
                    return;
                }

                if ((_raised != raised || !raised) &&
                    TryInvokeBoolMethod(hands, "SetCompassState", raised))
                {
                    _raised = raised;
                    _loggedMissingProxy = false;
                    return;
                }

                LogMissing(
                    raised,
                    "current hands controller '" + hands.GetType().FullName +
                    "' has no development radio/compass proxy path");
            }
            catch (TargetInvocationException ex)
            {
                Exception inner = ex.InnerException ?? ex;
                LogMissing(raised, inner.Message);
            }
            catch (Exception ex)
            {
                LogMissing(raised, ex.Message);
            }
        }

        private static bool TryInvokeBoolMethod(object target, string methodName, bool value)
        {
            Type type = target.GetType();
            Type[] signature = { typeof(bool) };

            while (type != null)
            {
                MethodInfo method = type.GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null,
                    signature,
                    null);

                if (method != null)
                {
                    method.Invoke(target, new object[] { value });
                    return true;
                }

                type = type.BaseType;
            }

            return false;
        }

        private void LogMissing(bool raised, string detail)
        {
            if (!raised || _loggedMissingProxy)
            {
                return;
            }

            _loggedMissingProxy = true;
            Plugin.Log.LogInfo(
                "SoulRecorder development hands proxy unavailable: " + detail + ".");
        }
    }
}
#endif
