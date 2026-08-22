using EFT;
using SoulPlayer.Library;

namespace SoulPlayer.Recorder
{
    /// <summary>
    /// Presentation seam for the future first-person recorder prefab and its
    /// cassette animations. The interaction controller owns all tape/audio state;
    /// a view only renders the hooks below.
    /// </summary>
    internal interface ISoulRecorderHandsView
    {
        float TapeInsertionSeconds { get; }
        float TapeEjectionSeconds { get; }

        void OnInteractionEntered(Player player);
        void OnTapeInsertionStarted(MusicTrack tape);
        void OnTapeInserted(MusicTrack tape);
        void OnPlaybackChanged(bool isPlaying);
        void OnTapeEjectionStarted(MusicTrack tape);
        void OnTapeEjected(MusicTrack tape);
        void OnInteractionExited();
    }

    /// <summary>
    /// Default release presentation until distributable recorder/cassette assets
    /// are available. Non-zero timings keep loading/ready/ejecting as real states
    /// instead of collapsing the entire interaction into one frame.
    /// </summary>
    internal sealed class HeadlessSoulRecorderHandsView : ISoulRecorderHandsView
    {
        internal static readonly HeadlessSoulRecorderHandsView Instance =
            new HeadlessSoulRecorderHandsView();

        private HeadlessSoulRecorderHandsView()
        {
        }

        public float TapeInsertionSeconds { get { return 0.25f; } }
        public float TapeEjectionSeconds { get { return 0.25f; } }

        public void OnInteractionEntered(Player player)
        {
        }

        public void OnTapeInsertionStarted(MusicTrack tape)
        {
        }

        public void OnTapeInserted(MusicTrack tape)
        {
        }

        public void OnPlaybackChanged(bool isPlaying)
        {
        }

        public void OnTapeEjectionStarted(MusicTrack tape)
        {
        }

        public void OnTapeEjected(MusicTrack tape)
        {
        }

        public void OnInteractionExited()
        {
        }
    }
}
