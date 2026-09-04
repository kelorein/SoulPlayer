using System;
using System.IO;
using System.Reflection;
using EFT;
using SoulPlayer.Configuration;
using SoulPlayer.Library;
using UnityEngine;

namespace SoulPlayer.Recorder
{
    internal enum SoulRecorderOverlayCue
    {
        RecorderAppear,
        CassetteHandle,
        InsertClick,
        SeatedClick,
        EjectClick,
        CassetteClear
    }

    /// <summary>
    /// Fully screen-space SoulRecorder presentation. It never creates a world
    /// object, touches an EFT hands controller, or depends on the archived arm rig.
    /// </summary>
    internal sealed class SoulRecorderOverlayView : MonoBehaviour,
        ISoulRecorderHandsView
    {
        private const string RecorderResource =
            "SoulPlayer.Assets.SoulRecorder.Overlay.recorder.png";
        private const string CassetteResource =
            "SoulPlayer.Assets.SoulRecorder.Overlay.cassette.png";

        private enum Sequence
        {
            Hidden,
            Insertion,
            Playing,
            Ejection
        }

        private SoulRecorderOverlaySettings _settings;
        private Texture2D _recorder;
        private Texture2D _cassette;
        private Sequence _sequence;
        private float _sequenceStartedAt;
        private float _exitStartedAt = -1f;
        private bool _enabled;
        private bool _isPlaying;
        private bool _loadFailureLogged;
        private SoulRecorderOverlayState _lastState =
            SoulRecorderOverlayState.Hidden;

        internal event Action<SoulRecorderOverlayCue> CueRaised;

        public float TapeInsertionSeconds
        {
            get
            {
                return SoulRecorderOverlayTimeline.InsertionSeconds(
                    _settings.AnimationSpeed);
            }
        }

        public float TapePreparationLeadSeconds
        {
            get { return TapeInsertionSeconds; }
        }

        public float TapeEjectionSeconds
        {
            get
            {
                return SoulRecorderOverlayTimeline.EjectionMotionSeconds(
                    _settings.AnimationSpeed);
            }
        }

        public float TapeEjectionAudioStopSeconds
        {
            get
            {
                return SoulRecorderOverlayTimeline.EjectionAudioStopSeconds(
                    _settings.AnimationSpeed);
            }
        }

        public float InteractionExitSeconds
        {
            get
            {
                float seconds = _sequence == Sequence.Ejection
                    ? SoulRecorderOverlayTimeline.EjectExitSeconds
                    : SoulRecorderOverlayTimeline.PresentationFadeSeconds;
                return seconds / Mathf.Clamp(_settings.AnimationSpeed, 0.5f, 2f);
            }
        }

        internal bool AssetsAvailable { get { return _enabled; } }

        internal void Initialize(SoulPlayerSettings settings)
        {
            _settings = settings == null
                ? SoulRecorderOverlaySettings.Default
                : settings.RecorderOverlaySettings;
            _enabled = settings == null || settings.RecorderOverlayEnabled;
            if (_enabled)
            {
                _enabled = TryLoadAssets();
            }
            _sequence = Sequence.Hidden;
        }

        public void OnInteractionEntered(Player player)
        {
            _sequence = _isPlaying ? Sequence.Ejection : Sequence.Insertion;
            _sequenceStartedAt = Time.unscaledTime;
            _exitStartedAt = -1f;
        }

        public void OnTapeInsertionStarted(MusicTrack tape)
        {
            _sequence = Sequence.Insertion;
            _sequenceStartedAt = Time.unscaledTime;
            _exitStartedAt = -1f;
        }

        public void OnTapeInserted(MusicTrack tape)
        {
            _sequence = Sequence.Playing;
            _sequenceStartedAt = Time.unscaledTime;
        }

        public void OnPlaybackChanged(bool isPlaying)
        {
            _isPlaying = isPlaying;
            if (isPlaying && _sequence != Sequence.Ejection)
            {
                _sequence = Sequence.Playing;
            }
        }

        public void OnTapeEjectionStarted(MusicTrack tape)
        {
            _sequence = Sequence.Ejection;
            _sequenceStartedAt = Time.unscaledTime;
            _exitStartedAt = -1f;
        }

        public void OnTapeEjected(MusicTrack tape)
        {
            _sequence = Sequence.Ejection;
            _sequenceStartedAt = Time.unscaledTime -
                SoulRecorderOverlayTimeline.EjectionMotionSeconds(
                    _settings.AnimationSpeed);
        }

        public void OnInteractionExited()
        {
            if (_sequence == Sequence.Ejection)
            {
                return;
            }
            _exitStartedAt = Time.unscaledTime;
        }

        public void ForceReset()
        {
            _sequence = Sequence.Hidden;
            _isPlaying = false;
            _exitStartedAt = -1f;
            _lastState = SoulRecorderOverlayState.Hidden;
        }

        private void Update()
        {
#if SOULPLAYER_PERF
            SoulPlayer.Utils.RecurringWorkProfiler.Begin(SoulPlayer.Utils.RecurringWorkArea.Overlay);
            try
            {
#endif
            if (!_enabled || _sequence == Sequence.Hidden)
            {
                return;
            }
            SoulRecorderOverlayPose pose = Evaluate(Time.unscaledTime);
            if (pose.State != _lastState)
            {
                RaiseCueForState(pose.State);
                _lastState = pose.State;
            }
            if (pose.State == SoulRecorderOverlayState.Hidden)
            {
                _sequence = Sequence.Hidden;
            }
        #if SOULPLAYER_PERF
            }
            finally { SoulPlayer.Utils.RecurringWorkProfiler.End(SoulPlayer.Utils.RecurringWorkArea.Overlay); }
#endif
        }

        private SoulRecorderOverlayPose Evaluate(float now)
        {
            SoulRecorderOverlayPose pose;
            switch (_sequence)
            {
                case Sequence.Insertion:
                    pose = SoulRecorderOverlayTimeline.SampleInsertion(
                        Screen.width, Screen.height,
                        now - _sequenceStartedAt, _settings);
                    break;
                case Sequence.Playing:
                    pose = SoulRecorderOverlayTimeline.SampleInsertion(
                        Screen.width, Screen.height,
                        SoulRecorderOverlayTimeline.InsertionSeconds(
                            _settings.AnimationSpeed), _settings);
                    break;
                case Sequence.Ejection:
                    pose = SoulRecorderOverlayTimeline.SampleEjection(
                        Screen.width, Screen.height,
                        now - _sequenceStartedAt, _settings);
                    break;
                default:
                    return new SoulRecorderOverlayPose
                    {
                        State = SoulRecorderOverlayState.Hidden
                    };
            }
            if (_exitStartedAt >= 0f)
            {
                pose = SoulRecorderOverlayTimeline.ApplyExitFade(
                    pose, now - _exitStartedAt, _settings.AnimationSpeed);
            }
            return pose;
        }

        private void OnGUI()
        {
#if SOULPLAYER_PERF
            SoulPlayer.Utils.RecurringWorkProfiler.Begin(SoulPlayer.Utils.RecurringWorkArea.Overlay);
            try
            {
#endif
            if (!_enabled || _sequence == Sequence.Hidden ||
                Event.current.type != EventType.Repaint)
            {
                return;
            }
            SoulRecorderOverlayPose pose = Evaluate(Time.unscaledTime);
            if (pose.State == SoulRecorderOverlayState.Hidden)
            {
                return;
            }

            Color previous = GUI.color;
            Rect cassetteRect = ToRect(pose.Cassette);
            GUI.color = new Color(0f, 0f, 0f, pose.CassetteAlpha * 0.24f);
            DrawRotatedTexture(new Rect(cassetteRect.x + 5f,
                cassetteRect.y + 7f, cassetteRect.width, cassetteRect.height),
                _cassette, pose.CassetteRotationDegrees);
            GUI.color = new Color(1f, 1f, 1f, pose.CassetteAlpha);
            DrawRotatedTexture(cassetteRect, _cassette,
                pose.CassetteRotationDegrees);

            GUI.color = new Color(1f, 1f, 1f, pose.RecorderAlpha);
            Rect recorderRect = ScaleFromCenter(ToRect(pose.Recorder),
                pose.RecorderScale);
            GUI.DrawTexture(recorderRect, _recorder, ScaleMode.ScaleToFit, true);
            if (pose.LedOn)
            {
                GUI.color = new Color(0.25f, 1f, 0.45f,
                    pose.RecorderAlpha * 0.92f);
                float led = Mathf.Max(3f, recorderRect.width * 0.018f);
                GUI.DrawTexture(new Rect(
                    recorderRect.x + recorderRect.width * 0.79f,
                    recorderRect.y + recorderRect.height * 0.72f,
                    led, led), Texture2D.whiteTexture);
            }
            GUI.color = previous;
        #if SOULPLAYER_PERF
            }
            finally { SoulPlayer.Utils.RecurringWorkProfiler.End(SoulPlayer.Utils.RecurringWorkArea.Overlay); }
#endif
        }

        private bool TryLoadAssets()
        {
            try
            {
                _recorder = LoadTexture(RecorderResource, "SoulRecorder overlay recorder");
                _cassette = LoadTexture(CassetteResource, "SoulRecorder overlay cassette");
                Plugin.Log.LogInfo(
                    "SoulRecorder 2D overlay assets loaded; runtime presentation is screen-space only.");
                return true;
            }
            catch (Exception ex)
            {
                if (!_loadFailureLogged)
                {
                    _loadFailureLogged = true;
                    Plugin.Log.LogError(
                        "SoulRecorder 2D overlay disabled because its images could not load; " +
                        "music interaction remains available: " + ex.Message);
                }
                DestroyTexture(_recorder);
                DestroyTexture(_cassette);
                _recorder = null;
                _cassette = null;
                return false;
            }
        }

        private static Texture2D LoadTexture(string resourceName, string name)
        {
            Assembly assembly = typeof(SoulRecorderOverlayView).Assembly;
            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    throw new FileNotFoundException(
                        "embedded resource was not found", resourceName);
                }
                byte[] bytes = new byte[stream.Length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read <= 0) break;
                    offset += read;
                }
                if (offset != bytes.Length)
                {
                    throw new EndOfStreamException(resourceName);
                }
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32,
                    false, false) { name = name, hideFlags = HideFlags.HideAndDontSave };
                if (!texture.LoadImage(bytes, false) ||
                    texture.width <= 2 || texture.height <= 2)
                {
                    UnityEngine.Object.Destroy(texture);
                    throw new InvalidDataException(resourceName + " is not a usable PNG image");
                }
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                return texture;
            }
        }

        private void RaiseCueForState(SoulRecorderOverlayState state)
        {
            SoulRecorderOverlayCue? cue = null;
            switch (state)
            {
                case SoulRecorderOverlayState.InsertEnter:
                    cue = SoulRecorderOverlayCue.RecorderAppear; break;
                case SoulRecorderOverlayState.InsertRotate:
                    cue = SoulRecorderOverlayCue.CassetteHandle; break;
                case SoulRecorderOverlayState.InsertHalf:
                    cue = SoulRecorderOverlayCue.InsertClick; break;
                case SoulRecorderOverlayState.InsertSeated:
                    cue = SoulRecorderOverlayCue.SeatedClick; break;
                case SoulRecorderOverlayState.EjectStart:
                    cue = SoulRecorderOverlayCue.EjectClick; break;
                case SoulRecorderOverlayState.EjectClear:
                    cue = SoulRecorderOverlayCue.CassetteClear; break;
            }
            if (!cue.HasValue || CueRaised == null) return;
            foreach (Action<SoulRecorderOverlayCue> subscriber in
                CueRaised.GetInvocationList())
            {
                try { subscriber(cue.Value); }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning(
                        "SoulRecorder overlay cue subscriber failed: " + ex.Message);
                }
            }
        }

        private static Rect ToRect(SoulRecorderOverlayRect value)
        {
            return new Rect(value.X, value.Y, value.Width, value.Height);
        }

        private static Rect ScaleFromCenter(Rect value, float scale)
        {
            float width = value.width * scale;
            float height = value.height * scale;
            return new Rect(value.center.x - width * 0.5f,
                value.center.y - height * 0.5f, width, height);
        }

        private static void DrawRotatedTexture(Rect rect, Texture texture,
            float degrees)
        {
            Matrix4x4 previous = GUI.matrix;
            GUIUtility.RotateAroundPivot(degrees, rect.center);
            GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true);
            GUI.matrix = previous;
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture != null) UnityEngine.Object.Destroy(texture);
        }

        private void OnDestroy()
        {
            DestroyTexture(_recorder);
            DestroyTexture(_cassette);
        }
    }
}
