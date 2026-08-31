using System;
using Comfort.Common;
using EFT;
using SoulPlayer.Cassettes;
using SoulPlayer.Configuration;
using SoulPlayer.Library;
using SoulPlayer.UI;
using SoulPlayer.Utils;
using UnityEngine;

namespace SoulPlayer.Recorder
{
    /// <summary>
    /// Raid-level input/lifetime host for the SoulRecorder usable-item controller.
    /// The configured shortcut enters/exits the interaction; tape transport lives on the
    /// usable-item controller and no longer borrows compass state.
    /// </summary>
    internal sealed class SoulRecorderController : MonoBehaviour
    {
        private readonly SoulRecorderInput _input = new SoulRecorderInput();
        private SoulPlayerSettings _settings;
        private SoulRecorderAudioPlayer _audioPlayer;
        private SoulRecorderInteractionController _usableItemController;
        private ISoulRecorderHandsControllerTransition _handsTransition;
        private bool _pendingEnter;
        private bool _pendingHandsAcquisition;
        private bool _wasInRaid;
        private bool _inputSuppressedForRaid;
        private bool _suppressHandsRestoration;
        private Player _raidPlayer;
        private string _statusTrack = string.Empty;
        private float _playingStatusUntil;
        private readonly SoulTapeRaidPlaybackPool _raidPlaybackPool =
            new SoulTapeRaidPlaybackPool();
        private readonly SoulRecorderRaidNextCoordinator _raidNextCoordinator =
            new SoulRecorderRaidNextCoordinator();
        private string _emptyFeedbackHeading = string.Empty;
        private string _emptyFeedbackDetail = string.Empty;
        private float _emptyFeedbackUntil;
        private readonly SoulRecorderStatusOverlayAnimation _statusAnimation =
            new SoulRecorderStatusOverlayAnimation();
        private GUIStyle _feedbackHeadingStyle;
        private GUIStyle _feedbackTrackStyle;
        private GUIStyle _feedbackStatusStyle;
        private GUIStyle _feedbackPanelStyle;
        private Texture2D _feedbackPanelTexture;
        private Texture2D _feedbackPixelTexture;

        internal bool IsActive
        {
            get
            {
                return _pendingEnter ||
                       _pendingHandsAcquisition ||
                       (_handsTransition != null &&
                        (_handsTransition.IsAcquiring || _handsTransition.IsOwned ||
                         _handsTransition.IsRestoring)) ||
                       (_audioPlayer != null &&
                        (_audioPlayer.IsLoading || _audioPlayer.IsPlaying)) ||
                       (_usableItemController != null &&
                        _usableItemController.IsInteractionActive);
            }
        }

        internal SoulRecorderState State
        {
            get
            {
                return _usableItemController == null
                    ? SoulRecorderState.Idle
                    : _usableItemController.RecorderState;
            }
        }

        internal MusicTrack ActiveTape
        {
            get
            {
                return _usableItemController == null
                    ? null
                    : _usableItemController.ActiveTape;
            }
        }

        internal void Initialize(SoulPlayerSettings settings)
        {
            _settings = settings;
            _audioPlayer = gameObject.AddComponent<SoulRecorderAudioPlayer>();
            _audioPlayer.Initialize(settings);

            _usableItemController = new SoulRecorderInteractionController();
            _usableItemController.Bind(_audioPlayer, CreateHandsView());
            _usableItemController.StateChanged += OnRecorderStateChanged;
            _usableItemController.PresentationOwnershipReleaseRequested +=
                OnPresentationOwnershipReleaseRequested;
            _handsTransition = new SoulRecorderScreenOverlayTransition();
            _handsTransition.InteractionReleased += OnHandsInteractionReleased;
            _settings.RaidCassettePlaybackModeChanged +=
                OnRaidCassettePlaybackModeChanged;

            if (Plugin.MusicLibrary != null)
            {
                Plugin.MusicLibrary.Changed += OnLibraryChanged;
            }

            if (Plugin.PostRaidCoordinator != null)
            {
                Plugin.PostRaidCoordinator.RaidResultQueued += OnRaidResultQueued;
            }

            Plugin.Log.LogInfo(
                "SoulRecorder screen-space controller ready: " + _settings.RecorderStartStopHotkey +
                " enters/exits the recorder interaction " +
                "without changing EFT hands; " +
                "raid cassette playback uses a non-repeating " +
                _settings.RaidCassettePlaybackMode + " shuffle bag.");
        }

        internal void Shutdown(string reason)
        {
            _inputSuppressedForRaid = true;
            ResetRecorder(reason, false);
        }

        private static readonly Func<KeyCode, bool> KeyDown = Input.GetKeyDown;
        private static readonly Func<KeyCode, bool> KeyHeld = Input.GetKey;
        private Action<string> _inputWarning;

        private void Update()
        {
#if SOULPLAYER_PERF
            SoulPlayer.Utils.RecurringWorkProfiler.Begin(SoulPlayer.Utils.RecurringWorkArea.Recorder);
            try
            {
#endif
            if (_handsTransition != null)
            {
                _handsTransition.ManualUpdate(Time.unscaledTime);
            }

            bool inRaid = GameState.IsInRaid();
            if (!inRaid)
            {
                if (_wasInRaid || IsActive)
                {
                    ResetRecorder("raid ended", false);
                }

                _wasInRaid = false;
                _inputSuppressedForRaid = false;
                ObserveRaidPlayer(null);
                return;
            }

            if (!_wasInRaid)
            {
                _raidPlaybackPool.Reset();
                Plugin.Log.LogInfo(
                    "SoulRecorder raid cassette shuffle bag started in " +
                    _settings.RaidCassettePlaybackMode + " mode.");
            }
            _wasInRaid = true;
            GameWorld world = Singleton<GameWorld>.Instance;
            Player player = world == null ? null : world.MainPlayer;
            ObserveRaidPlayer(player);

            if (_inputSuppressedForRaid || !IsLiveGameplayPlayer(player))
            {
                if (IsActive)
                {
                    ResetRecorder(
                        _inputSuppressedForRaid
                            ? "raid termination signaled"
                            : "local player unavailable or dead",
                        false);
                }
                return;
            }

            bool inputEdge = KeyDown(_settings.RecorderStartStopHotkey.MainKey) ||
                KeyDown(_settings.NextRaidCassetteHotkey.MainKey);
            if (_inputWarning == null) _inputWarning = ShowHotkeyConflictWarning;
            SoulRecorderInputAction inputAction = _input.Poll(
                _settings.RecorderStartStopHotkey, _settings.NextRaidCassetteHotkey,
                inRaid, inputEdge && (Input.GetKeyDown(KeyCode.F12) || SoulPlayerWindow.IsConfigurationManagerOpen()),
                SoulPlayerOverlayHost.Instance != null &&
                    SoulPlayerOverlayHost.Instance.CapturesKeyboardInput,
                KeyDown, KeyHeld, _inputWarning, _settings.RecorderInputRevision);
            if (inputAction == SoulRecorderInputAction.StartStop)
            {
                ToggleInteraction(player);
            }
            else if (inputAction == SoulRecorderInputAction.NextCassette)
            {
                QueueRaidNextCassette();
            }

            _usableItemController.ManualRecorderUpdate(Time.unscaledTime);
            ProcessQueuedRaidNext(player);
        #if SOULPLAYER_PERF
            }
            finally { SoulPlayer.Utils.RecurringWorkProfiler.End(SoulPlayer.Utils.RecurringWorkArea.Recorder); }
#endif
        }

        private void ShowHotkeyConflictWarning(string message)
        {
            Plugin.Log.LogWarning(message);
            _emptyFeedbackHeading = "HOTKEY CONFLICT";
            _emptyFeedbackDetail = "Start / Stop takes priority. Rebind in F12.";
            _emptyFeedbackUntil = Time.unscaledTime + 4f;
        }

        private void ToggleInteraction(Player player)
        {
            if (_pendingEnter)
            {
                _pendingEnter = false;
                Plugin.Log.LogInfo("SoulRecorder pending interaction cancelled (Start / Stop pressed).");
                return;
            }


            if (_pendingHandsAcquisition)
            {
                _pendingHandsAcquisition = false;
                _handsTransition.Restore("interaction cancelled while taking hands ownership");
                Plugin.Log.LogInfo(
                    "SoulRecorder pending hands-controller transition cancelled (Start / Stop pressed).");
                return;
            }

            if (_usableItemController.RecorderState == SoulRecorderState.Idle)
            {
                EnterInteraction(player);
                return;
            }

            if (_usableItemController.RecorderState == SoulRecorderState.Playing)
            {
                EnterStopInteraction(player, null);
                return;
            }

            if (_usableItemController.RecorderState == SoulRecorderState.LoadingTape)
            {
                _usableItemController.InterruptInsertion("Start / Stop pressed during tape insertion");
                return;
            }

            if (_usableItemController.RecorderState == SoulRecorderState.Ready)
            {
                _usableItemController.ExitInteraction();
            }
        }

        private void EnterInteraction(Player player)
        {
            if (!IsLiveGameplayPlayer(player) || _inputSuppressedForRaid)
            {
                return;
            }

            if (Plugin.TapeCollectionHost == null ||
                !Plugin.TapeCollectionHost.EnsureProfile(player))
            {
                Plugin.Log.LogWarning(
                    "SoulRecorder cannot enter until the profile cassette collection is loaded.");
                return;
            }

            SoulTapeRaidPlaybackSelection selection = ResolveUnlockedTape();
            SoulTapeCatalogEntry tape = selection.Tape;
            if (tape == null || tape.Track == null)
            {
                if (selection.EmptyReason ==
                        SoulTapeRaidPlaybackEmptyReason.NoAvailableAudio &&
                    Plugin.MusicLibrary != null && Plugin.MusicLibrary.IsScanning)
                {
                    _pendingEnter = true;
                    Plugin.Log.LogInfo(
                        "SoulRecorder is waiting for the music library scan before entering the interaction.");
                }
                else
                {
                    Plugin.Log.LogWarning(
                        "SoulRecorder raid playback pool is empty: " +
                        selection.EmptyReason + ".");
                    ShowEmptyPoolFeedback(selection);
                }
                return;
            }

            _pendingEnter = false;
            _emptyFeedbackUntil = 0f;
            AcquireHandsForStart(player, tape);
        }

        private void AcquireHandsForStart(Player player, SoulTapeCatalogEntry tape)
        {
            _pendingHandsAcquisition = true;
            _handsTransition.Acquire(player, succeeded =>
            {
                _pendingHandsAcquisition = false;
                if (!succeeded)
                {
                    return;
                }

                if (_inputSuppressedForRaid || _raidPlayer != player ||
                    !IsLiveGameplayPlayer(player))
                {
                    _handsTransition.Restore(
                        "raid/player changed before recorder insertion began");
                    return;
                }

                if (!_usableItemController.EnterInteraction(player, tape.Track))
                {
                    Plugin.Log.LogWarning(
                        "SoulRecorder could not enter from state " + State + ".");
                    _handsTransition.Restore("recorder interaction was rejected");
                    return;
                }

                // Recorder owns in-raid audio only; the pre-raid Main capture
                // survives preparation, cassette changes, ejection and cleanup.
                if (Plugin.AudioPlayer != null)
                    Plugin.AudioPlayer.PreserveMainForRecorder();
                _statusTrack = tape.Artist + " — " + tape.Title;
            });
        }

        private bool EnterStopInteraction(Player player, Action rejected)
        {
            if (_handsTransition.IsOwned && _handsTransition.Player == player)
            {
                if (!_usableItemController.BeginStopInteraction())
                {
                    _handsTransition.Restore("stop interaction was rejected");
                    rejected?.Invoke();
                    return false;
                }
                return true;
            }

            _pendingHandsAcquisition = true;
            _handsTransition.Acquire(player, succeeded =>
            {
                _pendingHandsAcquisition = false;
                if (!succeeded)
                {
                    rejected?.Invoke();
                    return;
                }

                if (_inputSuppressedForRaid || _raidPlayer != player ||
                    !IsLiveGameplayPlayer(player))
                {
                    _handsTransition.Restore(
                        "raid/player changed before recorder ejection began");
                    rejected?.Invoke();
                    return;
                }

                if (!_usableItemController.BeginStopInteraction())
                {
                    _handsTransition.Restore("recorder stop interaction was rejected");
                    rejected?.Invoke();
                }
            });
            return true;
        }

        private void ResetRecorder(string reason, bool restorePreviousHands = true)
        {
            _pendingEnter = false;
            _pendingHandsAcquisition = false;
            _playingStatusUntil = 0f;
            _emptyFeedbackUntil = 0f;
            _raidNextCoordinator.Reset();
            _statusAnimation.Reset();
            _suppressHandsRestoration = !restorePreviousHands;
            try
            {
                if (_usableItemController != null)
                {
                    _usableItemController.ForceReset(reason);
                }
                else if (_audioPlayer != null)
                {
                    _audioPlayer.Stop();
                }
            }
            finally
            {
                _suppressHandsRestoration = false;
            }

            if (_handsTransition != null)
            {
                if (restorePreviousHands)
                {
                    _handsTransition.Restore(reason);
                }
                else
                {
                    _handsTransition.Abandon(reason);
                }
            }
        }

        private void OnPresentationOwnershipReleaseRequested()
        {
            if (_handsTransition != null)
            {
                if (_suppressHandsRestoration)
                {
                    _handsTransition.Abandon("terminal recorder cleanup");
                }
                else
                {
                    _handsTransition.Restore("recorder presentation exited");
                }
            }
        }

        private void OnHandsInteractionReleased(bool restorationSucceeded, string detail)
        {
            SoulRecorderPlaybackState playback = _usableItemController == null
                ? SoulRecorderPlaybackState.Stopped
                : _usableItemController.PlaybackState;
            SoulRecorderInteractionPhase presentation = _usableItemController == null
                ? SoulRecorderInteractionPhase.Hidden
                : _usableItemController.PresentationPhase;
            bool busy = _handsTransition != null && _handsTransition.IsBusy;
            bool owned = _handsTransition != null && _handsTransition.IsOwned;
            Plugin.Log.LogInfo(
                "SoulRecorder PRESENTATION release state -> playback=" + playback +
                ", presentation=" + presentation +
                ", busy=" + busy +
                ", ownership=" + owned +
                ", restored=" + restorationSucceeded +
                ", detail=" + detail + ".");
        }

        private void ObserveRaidPlayer(Player player)
        {
            if (_raidPlayer == player)
            {
                return;
            }

            if (_raidPlayer != null)
            {
                _raidPlayer.OnIPlayerDeadOrUnspawn -= OnLocalPlayerDeadOrUnspawn;
            }

            _raidPlayer = player;
            if (_raidPlayer != null)
            {
                _inputSuppressedForRaid = false;
                _raidPlayer.OnIPlayerDeadOrUnspawn += OnLocalPlayerDeadOrUnspawn;
            }
        }

        private void OnLocalPlayerDeadOrUnspawn(IPlayer player)
        {
            _inputSuppressedForRaid = true;
            ResetRecorder("local player died or unspawned", false);
        }

        private void OnRaidResultQueued(ExitStatus outcome)
        {
            _inputSuppressedForRaid = true;
            ResetRecorder("post-raid result " + outcome, false);
        }

        private bool IsLiveGameplayPlayer(Player player)
        {
            bool hasPlayer = player != null;
            return SoulRecorderRaidInputGate.AllowsRecorderInput(
                GameState.IsInRaid(),
                hasPlayer,
                hasPlayer && player.gameObject.activeInHierarchy,
                hasPlayer && player.HealthController != null &&
                    player.HealthController.IsAlive,
                hasPlayer && player.HandsController != null,
                _inputSuppressedForRaid);
        }

        private SoulTapeRaidPlaybackSelection ResolveUnlockedTape()
        {
            if (Plugin.TapeCollection == null || !Plugin.TapeCollection.IsLoaded)
            {
                return new SoulTapeRaidPlaybackSelection(
                    null, SoulTapeRaidPlaybackEmptyReason.NoDiscoveredTapes);
            }

            SoulTapeRaidPlaybackSelection selection = _raidPlaybackPool.TakeNext(
                Plugin.TapeCollection,
                _settings.RaidCassettePlaybackMode);
            if (selection.Tape != null)
            {
                Plugin.Log.LogInfo(
                    "SoulRecorder raid shuffle selected " + selection.Tape.Id +
                    " (" + selection.Tape.Artist + " - " +
                    selection.Tape.Title + ") in " +
                    _settings.RaidCassettePlaybackMode + " mode.");
            }
            return selection;
        }

        private void QueueRaidNextCassette()
        {
            if (_raidNextCoordinator.Request())
            {
                Plugin.Log.LogInfo(
                    "SoulRecorder NEXT cassette requested; waiting for a safe " +
                    "recorder transition point.");
            }
            else
            {
                Plugin.Log.LogInfo(
                    "SoulRecorder NEXT cassette request ignored because one is " +
                    "already queued.");
            }
        }

        private void ProcessQueuedRaidNext(Player player)
        {
            if (!_raidNextCoordinator.IsPending ||
                !IsLiveGameplayPlayer(player) || _inputSuppressedForRaid)
            {
                return;
            }

            bool transitionBusy = _pendingEnter || _pendingHandsAcquisition ||
                (_handsTransition != null &&
                 (_handsTransition.IsBusy || _handsTransition.IsOwned));
            SoulRecorderRaidNextDecision decision =
                _raidNextCoordinator.Evaluate(
                    _usableItemController.RecorderState,
                    transitionBusy,
                    ResolveUnlockedTape);
            switch (decision.Action)
            {
                case SoulRecorderRaidNextAction.BeginEjection:
                    Plugin.Log.LogInfo(
                        "SoulRecorder NEXT reserved " + decision.Tape.Id +
                        "; beginning the controlled cassette ejection.");
                    if (!EnterStopInteraction(
                            player,
                            () => _raidNextCoordinator.RejectEjection()))
                    {
                        _raidNextCoordinator.RejectEjection();
                    }
                    break;

                case SoulRecorderRaidNextAction.BeginInsertion:
                    Plugin.Log.LogInfo(
                        "SoulRecorder NEXT ejection completed; preparing " +
                        decision.Tape.Id + " through the normal insertion flow.");
                    _emptyFeedbackUntil = 0f;
                    AcquireHandsForStart(player, decision.Tape);
                    break;

                case SoulRecorderRaidNextAction.Empty:
                    SoulTapeRaidPlaybackSelection empty = decision.Selection ??
                        new SoulTapeRaidPlaybackSelection(
                            null,
                            SoulTapeRaidPlaybackEmptyReason.NoDiscoveredTapes);
                    Plugin.Log.LogWarning(
                        "SoulRecorder NEXT kept the current playback state because " +
                        "the raid cassette pool is empty: " +
                        empty.EmptyReason + ".");
                    ShowEmptyPoolFeedback(empty);
                    break;
            }
        }

        private void ShowEmptyPoolFeedback(
            SoulTapeRaidPlaybackSelection selection)
        {
            _emptyFeedbackHeading = selection.EmptyHeading;
            _emptyFeedbackDetail = selection.EmptyDetail;
            _emptyFeedbackUntil = Time.unscaledTime + 3.2f;
        }

        private void OnRaidCassettePlaybackModeChanged(
            SoulTapeRaidPlaybackMode mode)
        {
            _raidPlaybackPool.Reset();
            _raidNextCoordinator.Reset();
            Plugin.Log.LogInfo(
                "SoulRecorder raid cassette shuffle mode changed to " + mode +
                "; the next M or raid-next interaction will start a fresh bag.");
        }

        private void OnRecorderStateChanged(
            SoulRecorderState previous,
            SoulRecorderState next)
        {
            if (next == SoulRecorderState.Playing && ActiveTape != null)
            {
                _statusTrack = ActiveTape.Artist + " — " + ActiveTape.Title;
                _playingStatusUntil = Time.unscaledTime + 2.5f;
            }
            else if (ActiveTape != null)
            {
                _statusTrack = ActiveTape.Artist + " — " + ActiveTape.Title;
            }
        }

        private SoulRecorderStatusOverlayState ResolveStatusOverlayState(
            float now)
        {
            if (!GameState.IsInRaid() || _usableItemController == null)
            {
                return SoulRecorderStatusOverlayState.Hidden;
            }

            if (now < _emptyFeedbackUntil)
            {
                return SoulRecorderStatusOverlayState.Ready;
            }

            switch (_usableItemController.RecorderState)
            {
                case SoulRecorderState.LoadingTape:
                    return _usableItemController.IsInsertionPresentationStarted
                        ? SoulRecorderStatusOverlayState.InsertingCassette
                        : SoulRecorderStatusOverlayState.PreparingAudio;
                case SoulRecorderState.Ready:
                    return SoulRecorderStatusOverlayState.Ready;
                case SoulRecorderState.Playing:
                    return now < _playingStatusUntil
                        ? SoulRecorderStatusOverlayState.Playing
                        : SoulRecorderStatusOverlayState.Hidden;
                case SoulRecorderState.Ejecting:
                    return SoulRecorderStatusOverlayState.Ejecting;
                default:
                    return SoulRecorderStatusOverlayState.Hidden;
            }
        }

        internal bool TryGetStatusScreenRect(float now, out SoulPlayerVolumeHudRect rect)
        {
            SoulRecorderStatusOverlayFrame frame = _statusAnimation.Sample(
                ResolveStatusOverlayState(now), now);
            if (frame.State == SoulRecorderStatusOverlayState.Hidden || frame.Alpha <= 0.001f)
            {
                rect = new SoulPlayerVolumeHudRect();
                return false;
            }
            SoulRecorderStatusOverlayRect panel = SoulRecorderStatusOverlayLayout.Calculate(
                Screen.width, Screen.height).Panel;
            rect = new SoulPlayerVolumeHudRect
            {
                X = panel.X, Y = panel.Y, Width = panel.Width,
                Height = panel.Height + SoulRecorderStatusOverlayLayout.SlideDistance
            };
            return frame.State != SoulRecorderStatusOverlayState.Hidden && frame.Alpha > 0.001f;
        }

        private void OnGUI()
        {
#if SOULPLAYER_PERF
            SoulPlayer.Utils.RecurringWorkProfiler.Begin(SoulPlayer.Utils.RecurringWorkArea.Overlay);
            try
            {
#endif
            float now = Time.unscaledTime;
            SoulRecorderStatusOverlayFrame frame = _statusAnimation.Sample(
                ResolveStatusOverlayState(now), now);
            if (frame.State == SoulRecorderStatusOverlayState.Hidden ||
                frame.Alpha <= 0.001f)
            {
                return;
            }

            EnsureFeedbackStyles();
            SoulRecorderStatusOverlayLayoutResult layout =
                SoulRecorderStatusOverlayLayout.Calculate(
                    Screen.width, Screen.height);
            float slide = frame.SlidePixels * layout.Scale;
            Color previousColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, frame.Alpha);

            GUI.Box(ToRect(layout.Panel, slide), GUIContent.none,
                _feedbackPanelStyle);
            DrawTintedRect(
                new Rect(layout.Panel.X, layout.Panel.Y + 12f * layout.Scale + slide,
                    2f * layout.Scale, layout.Panel.Height - 24f * layout.Scale),
                new Color(0.34f, 0.65f, 0.62f, 0.78f));
            bool showingEmptyFeedback =
                _emptyFeedbackUntil > 0f &&
                now < _emptyFeedbackUntil +
                    SoulRecorderStatusOverlayLayout.FadeOutSeconds;
            GUI.Label(ToRect(layout.Heading, slide),
                showingEmptyFeedback ? _emptyFeedbackHeading : "SOULRECORDER",
                _feedbackHeadingStyle);
            GUI.Label(ToRect(layout.Track, slide),
                showingEmptyFeedback ? _emptyFeedbackDetail : _statusTrack,
                _feedbackTrackStyle);
            GUI.Label(
                ToRect(layout.Status, slide),
                showingEmptyFeedback
                    ? string.Empty
                    : SoulRecorderStatusOverlayLayout.StatusText(frame.State),
                _feedbackStatusStyle);

            DrawTintedRect(ToRect(layout.ProgressTrack, slide),
                new Color(0.25f, 0.40f, 0.40f, 0.30f));
            if (SoulRecorderStatusOverlayLayout.ShowsProgress(frame.State))
            {
                float segmentWidth = layout.ProgressTrack.Width *
                    SoulRecorderStatusOverlayLayout.ProgressSegmentFraction;
                float phase = SoulRecorderStatusOverlayLayout
                    .IndeterminateProgressOffset(now);
                float travel = layout.ProgressTrack.Width - segmentWidth;
                DrawTintedRect(
                    new Rect(
                        layout.ProgressTrack.X + travel * phase,
                        layout.ProgressTrack.Y + slide,
                        segmentWidth,
                        layout.ProgressTrack.Height),
                    new Color(0.38f, 0.72f, 0.69f, 0.92f));
            }
            else
            {
                DrawTintedRect(
                    new Rect(
                        layout.ProgressTrack.X,
                        layout.ProgressTrack.Y + slide,
                        layout.ProgressTrack.Width,
                        layout.ProgressTrack.Height),
                    frame.State == SoulRecorderStatusOverlayState.Playing
                        ? new Color(0.38f, 0.72f, 0.69f, 0.72f)
                        : new Color(0.34f, 0.58f, 0.56f, 0.45f));
            }

            GUI.color = previousColor;
        #if SOULPLAYER_PERF
            }
            finally { SoulPlayer.Utils.RecurringWorkProfiler.End(SoulPlayer.Utils.RecurringWorkArea.Overlay); }
#endif
        }

        private void EnsureFeedbackStyles()
        {
            if (_feedbackHeadingStyle != null)
            {
                return;
            }

            _feedbackHeadingStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 11,
                fontStyle = FontStyle.Bold
            };
            _feedbackHeadingStyle.normal.textColor =
                new Color(0.48f, 0.73f, 0.69f, 1f);
            _feedbackTrackStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 16,
                fontStyle = FontStyle.Normal
            };
            _feedbackTrackStyle.clipping = TextClipping.Clip;
            _feedbackTrackStyle.normal.textColor =
                new Color(0.94f, 0.96f, 0.96f, 1f);
            _feedbackStatusStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 10,
                fontStyle = FontStyle.Normal
            };
            _feedbackStatusStyle.normal.textColor =
                new Color(0.60f, 0.67f, 0.67f, 1f);

            _feedbackPanelTexture = CreateRoundedPanelTexture();
            _feedbackPanelStyle = new GUIStyle
            {
                normal = { background = _feedbackPanelTexture },
                border = new RectOffset(12, 12, 12, 12)
            };
            _feedbackPixelTexture = new Texture2D(1, 1,
                TextureFormat.RGBA32, false, false)
            {
                name = "SoulRecorder Status Pixel",
                hideFlags = HideFlags.HideAndDontSave
            };
            _feedbackPixelTexture.SetPixel(0, 0, Color.white);
            _feedbackPixelTexture.Apply(false, true);
        }

        private void DrawTintedRect(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = new Color(
                color.r, color.g, color.b, color.a * previous.a);
            GUI.DrawTexture(rect, _feedbackPixelTexture,
                ScaleMode.StretchToFill, false);
            GUI.color = previous;
        }

        private static Rect ToRect(
            SoulRecorderStatusOverlayRect value,
            float slide)
        {
            return new Rect(
                value.X, value.Y + slide, value.Width, value.Height);
        }

        private static Texture2D CreateRoundedPanelTexture()
        {
            const int size = 32;
            const float radius = 8f;
            Texture2D texture = new Texture2D(
                size, size, TextureFormat.RGBA32, false, false)
            {
                name = "SoulRecorder Status Rounded Panel",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            Color fill = new Color(0.025f, 0.035f, 0.038f, 0.90f);
            Color border = new Color(0.27f, 0.47f, 0.46f, 0.64f);
            float half = (size - 1f) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(Mathf.Abs(x - half) -
                        (half - radius), 0f);
                    float dy = Mathf.Max(Mathf.Abs(y - half) -
                        (half - radius), 0f);
                    float distance = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                    float edgeAlpha = Mathf.Clamp01(0.75f - distance);
                    float borderBlend = Mathf.Clamp01(distance + 1.75f);
                    Color pixel = Color.Lerp(fill, border, borderBlend);
                    pixel.a *= edgeAlpha;
                    texture.SetPixel(x, y, pixel);
                }
            }
            texture.Apply(false, true);
            return texture;
        }

        private ISoulRecorderHandsView CreateHandsView()
        {
            try
            {
                SoulRecorderOverlayView overlay =
                    gameObject.AddComponent<SoulRecorderOverlayView>();
                overlay.Initialize(_settings);
                return overlay;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError(
                    "SoulRecorder 2D overlay could not initialize; using the safe " +
                    "headless presentation while preserving music interaction: " +
                    ex.Message);
                return HeadlessSoulRecorderHandsView.Instance;
            }
        }

        private void OnLibraryChanged()
        {
            if (_pendingEnter && GameState.IsInRaid())
            {
                EnterInteraction(_raidPlayer);
            }
        }

        private void OnDestroy()
        {
            if (_settings != null)
            {
                _settings.RaidCassettePlaybackModeChanged -=
                    OnRaidCassettePlaybackModeChanged;
            }

            if (Plugin.MusicLibrary != null)
            {
                Plugin.MusicLibrary.Changed -= OnLibraryChanged;
            }

            if (_usableItemController != null)
            {
                _usableItemController.StateChanged -= OnRecorderStateChanged;
                _usableItemController.PresentationOwnershipReleaseRequested -=
                    OnPresentationOwnershipReleaseRequested;
            }

            if (Plugin.PostRaidCoordinator != null)
            {
                Plugin.PostRaidCoordinator.RaidResultQueued -= OnRaidResultQueued;
            }

            if (_handsTransition != null)
            {
                _handsTransition.InteractionReleased -= OnHandsInteractionReleased;
            }

            ObserveRaidPlayer(null);

            _inputSuppressedForRaid = true;
            ResetRecorder("component destroyed", false);
            if (_feedbackPanelTexture != null)
            {
                Destroy(_feedbackPanelTexture);
                _feedbackPanelTexture = null;
            }
            if (_feedbackPixelTexture != null)
            {
                Destroy(_feedbackPixelTexture);
                _feedbackPixelTexture = null;
            }
        }
    }
}
