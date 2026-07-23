using System.Runtime.InteropServices;

namespace SoulPlayer.Utils
{
    internal static class MediaKeyInput
    {
        private const int MediaNextTrack = 0xB0;
        private const int MediaPreviousTrack = 0xB1;
        private const int MediaStop = 0xB2;
        private const int MediaPlayPause = 0xB3;

        private static bool _nextWasDown;
        private static bool _previousWasDown;
        private static bool _stopWasDown;
        private static bool _playPauseWasDown;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        internal static bool NextPressed()
        {
            return Poll(MediaNextTrack, ref _nextWasDown);
        }

        internal static bool PreviousPressed()
        {
            return Poll(MediaPreviousTrack, ref _previousWasDown);
        }

        internal static bool StopPressed()
        {
            return Poll(MediaStop, ref _stopWasDown);
        }

        internal static bool PlayPausePressed()
        {
            return Poll(MediaPlayPause, ref _playPauseWasDown);
        }

        private static bool Poll(int virtualKey, ref bool wasDown)
        {
            bool isDown = (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
            bool pressed = isDown && !wasDown;
            wasDown = isDown;
            return pressed;
        }
    }
}
