using System;
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
        float TapePreparationLeadSeconds { get; }
        float TapeEjectionSeconds { get; }
        float TapeEjectionAudioStopSeconds { get; }
        float InteractionExitSeconds { get; }

        void OnInteractionEntered(Player player);
        void OnTapeInsertionStarted(MusicTrack tape);
        void OnTapeInserted(MusicTrack tape);
        void OnPlaybackChanged(bool isPlaying);
        void OnTapeEjectionStarted(MusicTrack tape);
        void OnTapeEjected(MusicTrack tape);
        void OnInteractionExited();
        void ForceReset();
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
        public float TapePreparationLeadSeconds { get { return 0.15f; } }
        public float TapeEjectionSeconds { get { return 0.25f; } }
        public float TapeEjectionAudioStopSeconds { get { return 0f; } }
        public float InteractionExitSeconds { get { return 0.01f; } }

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

        public void ForceReset()
        {
        }
    }

    /// <summary>
    /// Event/timing adapter for the native EFT usable-item controller. The native
    /// controller owns the held recorder and forwards these logical presentation
    /// events to its sidecar visual driver; gameplay state stays in
    /// SoulRecorderInteractionController.
    /// </summary>
    internal sealed class NativeControllerSoulRecorderHandsView : ISoulRecorderHandsView
    {
        internal static readonly NativeControllerSoulRecorderHandsView Instance =
            new NativeControllerSoulRecorderHandsView();

        private NativeControllerSoulRecorderHandsView()
        {
        }

        public float TapeInsertionSeconds
        {
            get { return SoulRecorderPresentationTuning.TapeInsertionSeconds; }
        }

        public float TapePreparationLeadSeconds
        {
            get { return Math.Min(TapeInsertionSeconds, 0.60f); }
        }

        public float TapeEjectionSeconds
        {
            get { return SoulRecorderPresentationTuning.TapeEjectionSeconds; }
        }

        public float TapeEjectionAudioStopSeconds
        {
            get
            {
                return SoulRecorderPresentationTuning.StopEjectionLeadInSeconds +
                    (SoulRecorderPresentationTuning.CassetteEjectionMotionSeconds * 0.45f);
            }
        }

        public float InteractionExitSeconds
        {
            get { return SoulRecorderPresentationTuning.ExitSeconds; }
        }

        public void OnInteractionEntered(Player player)
        {
            SoulRecorderNativePresentation.PresentInteractionEntered();
        }

        public void OnTapeInsertionStarted(MusicTrack tape)
        {
            SoulRecorderNativePresentation.PresentTapeInsertionStarted();
        }

        public void OnTapeInserted(MusicTrack tape)
        {
            SoulRecorderNativePresentation.PresentTapeInserted();
        }

        public void OnPlaybackChanged(bool isPlaying)
        {
            SoulRecorderNativePresentation.PresentPlaybackChanged(isPlaying);
        }

        public void OnTapeEjectionStarted(MusicTrack tape)
        {
            SoulRecorderNativePresentation.PresentTapeEjectionStarted();
        }

        public void OnTapeEjected(MusicTrack tape)
        {
            SoulRecorderNativePresentation.PresentTapeEjected();
        }

        public void OnInteractionExited()
        {
            SoulRecorderNativePresentation.PresentInteractionExited();
        }

        public void ForceReset()
        {
            SoulRecorderNativePresentation.ResetPresentation();
        }
    }
}
