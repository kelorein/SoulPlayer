using System;
using System.Collections.Generic;
using System.Text;
using EFT;
using SoulPlayer.Library;
using SoulPlayer.Recorder.Assets;
using SoulPlayer.World;
using UnityEngine;

namespace SoulPlayer.Recorder
{
    /// <summary>
    /// SoulPlayer-owned first-person presentation. It prefers the packaged recorder and
    /// cassette with authored markers, while retaining the procedural/headless fallback
    /// chain without changing the usable-item controller. Packaged custom arms are not
    /// instantiated by the runtime presentation.
    /// </summary>
    internal sealed class ProceduralSoulRecorderHandsView : MonoBehaviour, ISoulRecorderHandsView
    {
        private readonly SoulRecorderPresentationState _state =
            new SoulRecorderPresentationState();
        private readonly SoulRecorderCassetteTransformAuthority _cassetteAuthority =
            new SoulRecorderCassetteTransformAuthority();
        private readonly List<Material> _ownedMaterials = new List<Material>();

        private Player _player;
        private GameObject _presentationRoot;
        private GameObject _root;
        private GameObject _recorderRoot;
        private GameObject _cassetteRoot;
        private Animator _handsAnimator;
        private Transform _recorderGrip;
        private Transform _cassetteGrip;
        private Transform _cassetteSlot;
        private Transform _reelLeft;
        private Transform _reelRight;
        private Material _statusLedMaterial;
        private Renderer[] _packagedRecorderRenderers;
        private Vector3 _cassetteStartPosition;
        private Vector3 _cassetteEndPosition;
        private Vector3 _cassetteAlignmentPosition;
        private Vector3 _cassetteEjectPosition;
        private Quaternion _cassetteStartRotation;
        private Quaternion _cassetteEndRotation;
        private Quaternion _cassetteAlignmentRotation;
        private Quaternion _cassetteEjectRotation;
        private bool _usingHeadlessFallback;
        private bool _constructionFailureLogged;
        private bool _packagedModelLogged;
        private bool _proceduralModelLogged;
        private bool _usingPackagedModel;
        private bool _packagedVisibilityLogged;
        private bool _nativePresentationUnavailableLogged;
        private bool _usingAnimatedHands;
        private bool _allowPackagedAnimatedHands = true;
        private bool _usingNativeAttachments;
        private Transform _nativeSupportGrip;
        private Transform _nativeCassetteGrip;
        private bool _stopSequence;
        private bool _ejectionTransferPending;
        private float _followupClipAt;
        private float _ejectionTransferAt;
        private string _followupClip;
#if SOULPLAYER_PLACEMENT_TOOLS
        private readonly HashSet<string> _cassetteDiagnosticSamples =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _cassetteDiagnosticTransitions =
            new HashSet<string>(StringComparer.Ordinal);
        private bool _cassetteDiagnosticActive;
        private bool _cassetteDiagnosticComplete;
#endif

        internal const float EnterClipSeconds = 25f / 60f;
        internal const float InsertClipSeconds = 46f / 60f;
        internal const float ExitClipSeconds = 23f / 60f;
        internal const float StopEnterClipSeconds = 23f / 60f;
        internal const float EjectClipSeconds = 41f / 60f;
        internal const float EjectTransferSeconds = 23f / 60f;
        internal const float EjectAudioStopSeconds = 17f / 60f;

        public float TapeInsertionSeconds
        {
            get
            {
                return _usingHeadlessFallback
                    ? HeadlessSoulRecorderHandsView.Instance.TapeInsertionSeconds
                    : HasSocketDrivenHands
                        ? EnterClipSeconds + InsertClipSeconds
                        : SoulRecorderPresentationTuning.TapeInsertionSeconds;
            }
        }

        public float TapePreparationLeadSeconds
        {
            get { return Mathf.Min(TapeInsertionSeconds, 0.60f); }
        }

        public float TapeEjectionSeconds
        {
            get
            {
                return _usingHeadlessFallback
                    ? HeadlessSoulRecorderHandsView.Instance.TapeEjectionSeconds
                    : HasSocketDrivenHands
                        ? StopEnterClipSeconds + EjectClipSeconds
                        : _state == null
                        ? SoulRecorderPresentationTuning.TapeEjectionSeconds
                        : _state.EjectionTotalSeconds;
            }
        }

        public float InteractionExitSeconds
        {
            get
            {
                return _usingHeadlessFallback
                    ? HeadlessSoulRecorderHandsView.Instance.InteractionExitSeconds
                    : HasSocketDrivenHands
                        ? ExitClipSeconds
                        : SoulRecorderPresentationTuning.ExitSeconds;
            }
        }

        public float TapeEjectionAudioStopSeconds
        {
            get
            {
                return HasSocketDrivenHands
                    ? StopEnterClipSeconds + EjectAudioStopSeconds
                    : 0f;
            }
        }

        private bool HasSocketDrivenHands
        {
            get { return _usingAnimatedHands || _usingNativeAttachments; }
        }

        internal Transform CassetteSlotTransform
        {
            get { return _cassetteSlot; }
        }

        internal void ConfigurePackagedAnimatedHands(bool enabled)
        {
            _allowPackagedAnimatedHands = enabled;
        }

        internal void ConfigureNativeAttachments(
            Transform supportGrip,
            Transform cassetteGrip)
        {
            _nativeSupportGrip = supportGrip;
            _nativeCassetteGrip = cassetteGrip;
            _usingNativeAttachments = supportGrip != null && cassetteGrip != null;
            if (_usingNativeAttachments && _root != null)
            {
                BindNativeModelAttachments();
            }
        }

        internal void ClearNativeAttachments()
        {
            if (_cassetteRoot != null && _cassetteSlot != null &&
                (_cassetteRoot.transform.parent == _nativeCassetteGrip ||
                 _cassetteRoot.transform.parent == _cassetteGrip))
            {
                _cassetteRoot.transform.SetParent(_cassetteSlot.parent, false);
                _cassetteRoot.transform.localPosition = _cassetteStartPosition;
                _cassetteRoot.transform.localRotation = _cassetteStartRotation;
                _cassetteRoot.transform.localScale = Vector3.one;
            }
            if (_root != null && _root.transform.parent == _nativeSupportGrip)
            {
                _root.transform.SetParent(null, true);
            }
            _usingNativeAttachments = false;
            _nativeSupportGrip = null;
            _nativeCassetteGrip = null;
            if (!_usingAnimatedHands)
            {
                _recorderGrip = null;
                _cassetteGrip = null;
                _cassetteAuthority.UseProceduralFallback();
            }
        }

        public void OnInteractionEntered(Player player)
        {
            _player = player;
            _usingHeadlessFallback = !EnsureModel();
            if (_usingHeadlessFallback)
            {
                HeadlessSoulRecorderHandsView.Instance.OnInteractionEntered(player);
                return;
            }

            _state.Enter(Time.unscaledTime);
            SetPresentationActive(false);
            ApplyStatusLed(false);
            UpdatePresentation(Time.unscaledTime);
        }

        public void OnTapeInsertionStarted(MusicTrack tape)
        {
            if (_usingHeadlessFallback)
            {
                HeadlessSoulRecorderHandsView.Instance.OnTapeInsertionStarted(tape);
                return;
            }

            _state.StartInsertion(Time.unscaledTime);
            if (HasSocketDrivenHands)
            {
                _stopSequence = false;
#if SOULPLAYER_PLACEMENT_TOOLS
                BeginCassetteAuthorityDiagnostic("insertion");
#endif
                BindCassetteToGripPose();
                PlayHandsClip("SoulRecorder_Enter");
                _followupClip = "SoulRecorder_Insert";
                _followupClipAt = Time.unscaledTime + EnterClipSeconds;
            }
            if (_cassetteRoot != null)
            {
                _cassetteRoot.SetActive(true);
            }
        }

        public void OnTapeInserted(MusicTrack tape)
        {
            if (_usingHeadlessFallback)
            {
                HeadlessSoulRecorderHandsView.Instance.OnTapeInserted(tape);
                return;
            }

            _state.SeatCassette();
            if (HasSocketDrivenHands)
            {
                TransferCassetteTo(
                    _cassetteSlot,
                    "Insert contact CassetteGrip -> CassetteSlot");
                PlayHandsClip("SoulRecorder_Hold");
            }
            else
            {
                ApplyCassetteTransform(1f, false, 0f);
            }
        }

        public void OnPlaybackChanged(bool isPlaying)
        {
            if (_usingHeadlessFallback)
            {
                HeadlessSoulRecorderHandsView.Instance.OnPlaybackChanged(isPlaying);
                return;
            }

            _state.SetPlayback(isPlaying, Time.unscaledTime);
            ApplyStatusLed(isPlaying);
        }

        public void OnTapeEjectionStarted(MusicTrack tape)
        {
            if (_usingHeadlessFallback)
            {
                HeadlessSoulRecorderHandsView.Instance.OnTapeEjectionStarted(tape);
                return;
            }

            _state.StartEjection(Time.unscaledTime);
            ApplyStatusLed(false);
            if (HasSocketDrivenHands)
            {
                _stopSequence = true;
#if SOULPLAYER_PLACEMENT_TOOLS
                BeginCassetteAuthorityDiagnostic("ejection");
#endif
                EnsureCassetteOnSlot();
                PlayHandsClip("SoulRecorder_StopEnter");
                _followupClip = "SoulRecorder_Eject";
                _followupClipAt = Time.unscaledTime + StopEnterClipSeconds;
                _ejectionTransferAt = _usingNativeAttachments
                    ? Native.SoulRecorderNativePhaseTimeline.EjectionTransferAt(
                        Time.unscaledTime)
                    : _followupClipAt + EjectTransferSeconds;
                _ejectionTransferPending = true;
            }
        }

        public void OnTapeEjected(MusicTrack tape)
        {
            if (_usingHeadlessFallback)
            {
                HeadlessSoulRecorderHandsView.Instance.OnTapeEjected(tape);
                return;
            }

            if (!HasSocketDrivenHands)
            {
                _state.HideCassette();
                if (_cassetteRoot != null)
                {
                    _cassetteRoot.SetActive(false);
                }
            }
        }

        public void OnInteractionExited()
        {
            if (_usingHeadlessFallback)
            {
                HeadlessSoulRecorderHandsView.Instance.OnInteractionExited();
                _usingHeadlessFallback = false;
                _player = null;
                return;
            }

            _state.Exit(Time.unscaledTime);
            if (_usingAnimatedHands)
            {
                PlayHandsClip(_stopSequence
                    ? "SoulRecorder_StopExit"
                    : "SoulRecorder_StartExit");
            }
            _player = null;
        }

        public void ForceReset()
        {
            if (_usingHeadlessFallback)
            {
                HeadlessSoulRecorderHandsView.Instance.ForceReset();
            }

            _usingHeadlessFallback = false;
            _player = null;
            _state.Reset();
            ResetAnimatedSequence();
            ApplyStatusLed(false);
            if (_cassetteRoot != null)
            {
                _cassetteRoot.SetActive(false);
            }
            if (_root != null)
            {
                _root.SetActive(false);
            }
            SetPresentationActive(false);
        }

        private void Update()
        {
            if (_usingHeadlessFallback || !_state.IsVisible)
            {
                return;
            }

            if (!_state.IsExiting && _player == null)
            {
                ForceReset();
                return;
            }

            if (!EnsureModel())
            {
                ForceReset();
                return;
            }

            UpdatePresentation(Time.unscaledTime);
        }

        private void UpdatePresentation(float now)
        {
            if (_root == null)
            {
                return;
            }

            _state.Advance(now);
            Transform view = ResolveViewTransform();
            if (view == null)
            {
                SetPresentationActive(false);
                return;
            }

            EnsurePresentationRoot(view);
            SetPresentationActive(true);
            AdvanceAnimatedHands(now);
            ApplyRecorderTransform(now);
            ApplyCassetteAnimation(now);
#if SOULPLAYER_PLACEMENT_TOOLS
            LogCassetteAuthorityFrame();
#endif
            AnimateReels();
            LogPackagedVisibilityOnce(view.GetComponent<Camera>());
        }

        private void ApplyRecorderTransform(float now)
        {
            if (_usingNativeAttachments)
            {
                if (_state.IsExiting && _state.GetExitProgress(now) >= 1f)
                {
                    _state.CompleteExit();
                    SetPresentationActive(false);
                }
                return;
            }
            if (_usingAnimatedHands)
            {
                if (_state.IsExiting && _state.GetExitProgress(now) >= 1f)
                {
                    _state.CompleteExit();
                    SetPresentationActive(false);
                    return;
                }
                float animatedOffscreenAmount = _state.GetOffscreenAmount(now);
                _presentationRoot.transform.localPosition =
                    ResolvePresentationPosition() +
                    (SoulRecorderPresentationTuning.OffscreenOffset * animatedOffscreenAmount);
                Vector3 animatedHeldRotation = ResolvePresentationRotation();
                _presentationRoot.transform.localRotation = Quaternion.Slerp(
                    Quaternion.Euler(animatedHeldRotation),
                    Quaternion.Euler(
                        animatedHeldRotation +
                        SoulRecorderPresentationTuning.ExitRotationOffsetEuler),
                    animatedOffscreenAmount);
                _presentationRoot.transform.localScale =
                    SoulRecorderPresentationTuning.RecorderScale;
                return;
            }

            float offscreenAmount = _state.GetOffscreenAmount(now);
            float enterProgress = _state.GetEnterProgress(now);
            float enterSettle = _state.IsEntering
                ? SoulRecorderPresentationState.SettlePulse(
                    enterProgress,
                    SoulRecorderPresentationTuning.RecorderEnterSettleStart)
                : 0f;
            if (_state.IsExiting)
            {
                float progress = SoulRecorderPresentationState.Smooth(_state.GetExitProgress(now));
                if (progress >= 1f)
                {
                    _state.CompleteExit();
                    SetPresentationActive(false);
                    return;
                }
            }

            _presentationRoot.transform.localPosition =
                ResolvePresentationPosition() +
                (SoulRecorderPresentationTuning.OffscreenOffset * offscreenAmount) +
                (SoulRecorderPresentationTuning.EnterSettleOffset * enterSettle);
            Vector3 heldRotation = ResolvePresentationRotation();
            Quaternion held = Quaternion.Euler(heldRotation);
            Quaternion offscreen = Quaternion.Euler(
                heldRotation + SoulRecorderPresentationTuning.ExitRotationOffsetEuler);
            _presentationRoot.transform.localRotation =
                Quaternion.Slerp(held, offscreen, offscreenAmount);
            _presentationRoot.transform.localScale =
                SoulRecorderPresentationTuning.RecorderScale;
        }

        private void ApplyCassetteAnimation(float now)
        {
            if (_cassetteRoot == null)
            {
                return;
            }
            if (!_cassetteAuthority.ProceduralTransportAllowed)
            {
                return;
            }

            switch (_state.CassetteState)
            {
                case SoulRecorderCassetteVisualState.Hidden:
                    _cassetteRoot.SetActive(false);
                    break;
                case SoulRecorderCassetteVisualState.Inserting:
                    _cassetteRoot.SetActive(true);
                    ApplyCassetteTransform(
                        SoulRecorderPresentationState.Smooth(_state.GetInsertionProgress(now)),
                        false,
                        _state.GetInsertionSettleAmount(now));
                    break;
                case SoulRecorderCassetteVisualState.Seated:
                    _cassetteRoot.SetActive(true);
                    ApplyCassetteTransform(1f, false, 0f);
                    break;
                case SoulRecorderCassetteVisualState.Ejecting:
                    _cassetteRoot.SetActive(true);
                    ApplyCassetteTransform(
                        _state.GetEjectionCassetteTravel(now),
                        true,
                        0f);
                    break;
            }
        }

        private void AdvanceAnimatedHands(float now)
        {
            if (!HasSocketDrivenHands)
            {
                return;
            }
            if (!string.IsNullOrEmpty(_followupClip) && now >= _followupClipAt)
            {
                string clip = _followupClip;
                _followupClip = null;
                _followupClipAt = 0f;
                PlayHandsClip(clip);
            }
            if (_ejectionTransferPending && now >= _ejectionTransferAt)
            {
                _ejectionTransferPending = false;
                _ejectionTransferAt = 0f;
                TransferCassetteTo(
                    _cassetteGrip,
                    "Eject grasp CassetteSlot -> CassetteGrip");
            }
        }

        private void PlayHandsClip(string name)
        {
            if (_handsAnimator == null || string.IsNullOrEmpty(name))
            {
                return;
            }
            _handsAnimator.speed = 1f;
            _handsAnimator.Play("Base Layer." + name, 0, 0f);
            _handsAnimator.Update(0f);
#if SOULPLAYER_PLACEMENT_TOOLS
            LogAnimatorTransition(name);
#endif
        }

        private void BindCassetteToGripPose()
        {
            if (!HasSocketDrivenHands || _cassetteRoot == null || _cassetteGrip == null)
            {
                return;
            }
            _cassetteRoot.transform.SetParent(_cassetteGrip, false);
            _cassetteRoot.transform.localPosition = _usingNativeAttachments
                ? SoulRecorderPresentationTuning.NativeCassetteModelPosition
                : Vector3.zero;
            _cassetteRoot.transform.localRotation = Quaternion.Euler(
                ResolveCassetteGripModelRotation(_usingNativeAttachments));
            _cassetteRoot.transform.localScale = Vector3.one;
            _cassetteAuthority.BindToGrip();
            _cassetteRoot.SetActive(true);
#if SOULPLAYER_PLACEMENT_TOOLS
            LogCassetteOwnershipEvent("Insertion bind pose -> CassetteGrip");
#endif
        }

        internal static Vector3 ResolveCassetteGripModelRotation(bool nativeAttachments)
        {
            return nativeAttachments
                ? SoulRecorderPresentationTuning.NativeCassetteModelRotationEuler
                : SoulRecorderPresentationTuning.AnimatedCassetteGripRotationEuler;
        }

        private void EnsureCassetteOnSlot()
        {
            if (_cassetteRoot == null || _cassetteSlot == null)
            {
                return;
            }
            if (_cassetteRoot.transform.parent != _cassetteSlot)
            {
                TransferCassetteTo(
                    _cassetteSlot,
                    "Eject precondition -> CassetteSlot");
                return;
            }
            _cassetteAuthority.BindToSlot();
        }

        private void TransferCassetteTo(Transform parent, string operation)
        {
            if (!HasSocketDrivenHands || _cassetteRoot == null || parent == null)
            {
                return;
            }
            SoulRecorderAttachmentTransfer.ReparentPreservingWorldPose(
                _cassetteRoot.transform,
                parent);
            if (parent == _cassetteSlot)
            {
                _cassetteAuthority.BindToSlot();
            }
            else if (parent == _cassetteGrip)
            {
                _cassetteAuthority.BindToGrip();
            }
            _cassetteRoot.SetActive(true);
#if SOULPLAYER_PLACEMENT_TOOLS
            LogCassetteOwnershipEvent(operation);
#endif
        }

#if SOULPLAYER_PLACEMENT_TOOLS
        private void BeginCassetteAuthorityDiagnostic(string interaction)
        {
            if (_cassetteDiagnosticComplete)
            {
                return;
            }
            if (!_cassetteDiagnosticActive)
            {
                _cassetteDiagnosticActive = true;
                Plugin.Log.LogInfo(
                    "SoulRecorder CASSETTE diagnostic started for one start/stop cycle (" +
                    interaction + ").");
            }
        }

        private void LogAnimatorTransition(string requestedState)
        {
            if (!_cassetteDiagnosticActive || _handsAnimator == null ||
                !_cassetteDiagnosticTransitions.Add(requestedState))
            {
                return;
            }
            AnimatorStateInfo info = _handsAnimator.GetCurrentAnimatorStateInfo(0);
            Plugin.Log.LogInfo(
                "SoulRecorder CASSETTE Animator transition: requested=" + requestedState +
                ", active=" + ResolveAnimatorStateName(info) +
                ", normalized=" + info.normalizedTime.ToString("F4") + ".");
        }

        private void LogCassetteOwnershipEvent(string operation)
        {
            if (!_cassetteDiagnosticActive)
            {
                return;
            }
            Plugin.Log.LogInfo(
                "SoulRecorder CASSETTE ownership transfer: " + operation +
                ", authority=" + _cassetteAuthority.Current +
                ", parent=" + CassetteParentName() +
                ", world=" + FormatVector(_cassetteRoot.transform.position) +
                ", local=" + FormatVector(_cassetteRoot.transform.localPosition) + ".");
        }

        private void LogCassetteAuthorityFrame()
        {
            if (!_cassetteDiagnosticActive || _cassetteDiagnosticComplete ||
                _handsAnimator == null || _cassetteRoot == null)
            {
                return;
            }
            AnimatorStateInfo info = _handsAnimator.GetCurrentAnimatorStateInfo(0);
            string state = ResolveAnimatorStateName(info);
            float normalized = info.normalizedTime;
            float[] thresholds = DiagnosticThresholds(state);
            foreach (float threshold in thresholds)
            {
                if (normalized + 0.0001f < threshold)
                {
                    continue;
                }
                string key = state + "@" + threshold.ToString("F2");
                if (!_cassetteDiagnosticSamples.Add(key))
                {
                    continue;
                }
                Transform owner = _cassetteAuthority.IsHandOwned
                    ? _cassetteGrip
                    : _cassetteAuthority.IsRecorderOwned
                        ? _cassetteSlot
                        : null;
                float ownerDistance = owner == null
                    ? -1f
                    : Vector3.Distance(_cassetteRoot.transform.position, owner.position);
                Plugin.Log.LogInfo(
                    "SoulRecorder CASSETTE frame: state=" + state +
                    ", normalized=" + normalized.ToString("F4") +
                    ", authority=" + _cassetteAuthority.Current +
                    ", parent=" + CassetteParentName() +
                    ", world=" + FormatVector(_cassetteRoot.transform.position) +
                    ", local=" + FormatVector(_cassetteRoot.transform.localPosition) +
                    ", CassetteGrip=" + PositionOrMissing(_cassetteGrip) +
                    ", CassetteSlot=" + PositionOrMissing(_cassetteSlot) +
                    ", distanceToOwner=" + ownerDistance.ToString("F6") +
                    ", proceduralTransportActive=" +
                    _cassetteAuthority.ProceduralTransportAllowed +
                    ", lastWriter=" + _cassetteAuthority.WriterLabel + ".");
                if (state == "SoulRecorder_StopExit" && threshold >= 0.85f)
                {
                    _cassetteDiagnosticComplete = true;
                    _cassetteDiagnosticActive = false;
                    Plugin.Log.LogInfo(
                        "SoulRecorder CASSETTE diagnostic complete; further cassette " +
                        "frame logging is disabled for this plugin session.");
                }
            }
        }

        private static float[] DiagnosticThresholds(string state)
        {
            switch (state)
            {
                case "SoulRecorder_Enter":
                    return new[] { 0f, 0.75f };
                case "SoulRecorder_Insert":
                    return new[] { 0f, 0.25f, 0.62f, 0.90f };
                case "SoulRecorder_StartExit":
                    return new[] { 0f, 0.80f };
                case "SoulRecorder_StopEnter":
                    return new[] { 0f, 0.80f };
                case "SoulRecorder_Eject":
                    return new[] { 0f, 0.45f, 0.56f, 0.80f };
                case "SoulRecorder_StopExit":
                    return new[] { 0f, 0.85f };
                default:
                    return new float[0];
            }
        }

        private static string ResolveAnimatorStateName(AnimatorStateInfo info)
        {
            foreach (string state in new[]
            {
                "SoulRecorder_Enter", "SoulRecorder_Insert",
                "SoulRecorder_StartExit", "SoulRecorder_Hold",
                "SoulRecorder_StopEnter", "SoulRecorder_Eject",
                "SoulRecorder_StopExit", "SoulRecorder_CancelInsert"
            })
            {
                if (info.IsName("Base Layer." + state) || info.IsName(state))
                {
                    return state;
                }
            }
            return "unknown(" + info.fullPathHash + ")";
        }

        private string CassetteParentName()
        {
            return _cassetteRoot == null || _cassetteRoot.transform.parent == null
                ? "<none>"
                : _cassetteRoot.transform.parent.name;
        }

        private static string PositionOrMissing(Transform value)
        {
            return value == null ? "<missing>" : FormatVector(value.position);
        }
#endif

        private void ResetAnimatedSequence()
        {
            _followupClip = null;
            _followupClipAt = 0f;
            _ejectionTransferAt = 0f;
            _ejectionTransferPending = false;
            _stopSequence = false;
            if (_handsAnimator != null)
            {
                _handsAnimator.Rebind();
                _handsAnimator.Update(0f);
            }
        }

        private void ApplyCassetteTransform(
            float progress,
            bool ejecting,
            float settleAmount)
        {
            if (_cassetteRoot == null)
            {
                return;
            }

            Vector3 exteriorPosition = ejecting
                ? ResolveCassetteEjectPosition()
                : ResolveCassetteStartPosition();
            Quaternion exteriorRotation = ejecting
                ? ResolveCassetteEjectRotation()
                : ResolveCassetteStartRotation();
            Vector3 alignmentPosition = ResolveCassetteAlignmentPosition();
            Quaternion alignmentRotation = ResolveCassetteAlignmentRotation();
            Vector3 insertedPosition = ResolveCassetteInsertedPosition();
            Quaternion insertedRotation = ResolveCassetteInsertedRotation();
            Vector3 currentPosition;
            Quaternion currentRotation;
            if (progress <= SoulRecorderPresentationTuning.CassetteAlignmentProgress)
            {
                float alignmentProgress = progress /
                    SoulRecorderPresentationTuning.CassetteAlignmentProgress;
                currentPosition = Vector3.Lerp(
                    exteriorPosition,
                    alignmentPosition,
                    alignmentProgress);
                currentRotation = Quaternion.Slerp(
                    exteriorRotation,
                    alignmentRotation,
                    alignmentProgress);
            }
            else
            {
                float insertionProgress =
                    (progress - SoulRecorderPresentationTuning.CassetteAlignmentProgress) /
                    (1f - SoulRecorderPresentationTuning.CassetteAlignmentProgress);
                currentPosition = Vector3.Lerp(
                    alignmentPosition,
                    insertedPosition,
                    insertionProgress);
                currentRotation = Quaternion.Slerp(
                    alignmentRotation,
                    insertedRotation,
                    insertionProgress);
            }

            _cassetteRoot.transform.localPosition = currentPosition;
            _cassetteRoot.transform.localRotation = currentRotation;
            if (settleAmount > 0f)
            {
                float settleMeters =
                    SoulRecorderPresentationTuning.CassetteContactSettleMillimeters * 0.001f;
                _cassetteRoot.transform.localPosition +=
                    new Vector3(0f, -settleMeters * 0.35f, settleMeters) * settleAmount;
            }
        }

        private void AnimateReels()
        {
            if (!_state.IsPlaying || _cassetteRoot == null)
            {
                return;
            }

            float degrees = SoulRecorderPresentationTuning.ReelDegreesPerSecond *
                            Time.unscaledDeltaTime;
            if (_reelLeft != null)
            {
                _reelLeft.Rotate(Vector3.up, degrees, Space.Self);
            }
            if (_reelRight != null)
            {
                _reelRight.Rotate(Vector3.up, -degrees, Space.Self);
            }
        }

        private bool EnsureModel()
        {
            if (_root != null)
            {
                return true;
            }

            DestroyOwnedMaterials();
            if (!_nativePresentationUnavailableLogged)
            {
                _nativePresentationUnavailableLogged = true;
                Plugin.Log.LogWarning(
                    "SoulRecorder native presentation unavailable: SPT 4.1.x exposes no " +
                    "supported Audio Recorder template/controller/prefab contract.");
            }
            SoulRecorderPackagedVisual packaged;
            if (Plugin.RecorderAssets != null &&
                Plugin.RecorderAssets.TryCreateRecorderVisual(
                    out packaged,
                    _allowPackagedAnimatedHands))
            {
                BindPackagedModel(packaged);
                return true;
            }

            Shader shader = SoulPlayerVisualShader.FindOpaque();
            if (shader == null)
            {
                LogConstructionFailure(
                    "no supported Unity shader was available (Standard, Legacy Diffuse, " +
                    "Unlit/Color, or Sprites/Default)");
                return false;
            }

            try
            {
                BuildProceduralModel(shader);
                if (_root != null && !_proceduralModelLogged)
                {
                    _proceduralModelLogged = true;
                    Plugin.Log.LogInfo(
                        "SoulRecorder is using the procedural presentation fallback.");
                }
                return _root != null;
            }
            catch (Exception ex)
            {
                DestroyModel();
                LogConstructionFailure(ex.Message);
                return false;
            }
        }

        private void BindPackagedModel(SoulRecorderPackagedVisual packaged)
        {
            _recorderRoot = packaged.Root;
            _usingAnimatedHands = packaged.HasAnimatedHands &&
                packaged.AnimatedHandsRoot != null && packaged.HandsAnimator != null;
            _root = _usingAnimatedHands
                ? packaged.AnimatedHandsRoot
                : packaged.Root;
            _cassetteRoot = packaged.CassetteRoot;
            _handsAnimator = packaged.HandsAnimator;
            _recorderGrip = packaged.RecorderGrip;
            _cassetteGrip = packaged.CassetteGrip;
            _cassetteSlot = packaged.CassetteSlot;
            _reelLeft = packaged.ReelLeft;
            _reelRight = packaged.ReelRight;
            _cassetteStartPosition = packaged.CassetteStartPosition;
            _cassetteStartRotation = packaged.CassetteStartRotation;
            _cassetteAlignmentPosition = packaged.CassetteAlignmentPosition;
            _cassetteAlignmentRotation = packaged.CassetteAlignmentRotation;
            _cassetteEndPosition = packaged.CassetteEndPosition;
            _cassetteEndRotation = packaged.CassetteEndRotation;
            _cassetteEjectPosition = packaged.CassetteEjectPosition;
            _cassetteEjectRotation = packaged.CassetteEjectRotation;
            if (packaged.StatusLedRenderer != null)
            {
                _statusLedMaterial = packaged.StatusLedRenderer.material;
                _ownedMaterials.Add(_statusLedMaterial);
            }
            _packagedRecorderRenderers = packaged.RecorderModelRenderers;
            _usingPackagedModel = true;

            if (_usingNativeAttachments)
            {
                BindNativeModelAttachments();
            }
            else if (_usingAnimatedHands)
            {
                _recorderRoot.transform.SetParent(_recorderGrip, false);
                _recorderRoot.transform.localPosition =
                    SoulRecorderPresentationTuning.AnimatedRecorderGripPosition;
                _recorderRoot.transform.localRotation = Quaternion.Euler(
                    SoulRecorderPresentationTuning.AnimatedRecorderGripRotationEuler);
                _recorderRoot.transform.localScale = Vector3.one;
                _cassetteRoot.transform.SetParent(_cassetteGrip, false);
                _cassetteRoot.transform.localPosition = Vector3.zero;
                _cassetteRoot.transform.localRotation = Quaternion.Euler(
                    SoulRecorderPresentationTuning.AnimatedCassetteGripRotationEuler);
                _cassetteRoot.transform.localScale = Vector3.one;
                _cassetteAuthority.BindToGrip();
                _recorderRoot.SetActive(true);
            }
            else
            {
                _cassetteAuthority.UseProceduralFallback();
            }

            _cassetteRoot.SetActive(false);
            _root.SetActive(false);
            if (!_packagedModelLogged)
            {
                _packagedModelLogged = true;
                Plugin.Log.LogInfo(_usingAnimatedHands
                    ? "SoulRecorder using BAMEN articulated animated-hands presentation."
                    : _usingNativeAttachments
                        ? "SoulRecorder using native EFT arms with packaged recorder/cassette."
                    : "SoulRecorder using packaged recorder-only fallback.");
            }
        }

        private void BindNativeModelAttachments()
        {
            if (_root == null || _cassetteRoot == null || _cassetteSlot == null ||
                _nativeSupportGrip == null || _nativeCassetteGrip == null)
            {
                return;
            }
            _usingNativeAttachments = true;
            _recorderGrip = _nativeSupportGrip;
            _cassetteGrip = _nativeCassetteGrip;
            _root.transform.SetParent(_recorderGrip, false);
            _root.transform.localPosition =
                SoulRecorderPresentationTuning.NativeRecorderModelPosition;
            _root.transform.localRotation = Quaternion.Euler(
                SoulRecorderPresentationTuning.NativeRecorderModelRotationEuler);
            _root.transform.localScale = Vector3.one;
            _cassetteRoot.transform.SetParent(_cassetteGrip, false);
            _cassetteRoot.transform.localPosition =
                SoulRecorderPresentationTuning.NativeCassetteModelPosition;
            _cassetteRoot.transform.localRotation = Quaternion.Euler(
                SoulRecorderPresentationTuning.NativeCassetteModelRotationEuler);
            _cassetteRoot.transform.localScale = Vector3.one;
            _cassetteAuthority.BindToGrip();
        }

        private void BuildProceduralModel(Shader shader)
        {
            _root = new GameObject("SoulPlayer procedural first-person recorder");

            Material body = CreateMaterial(shader, new Color(0.075f, 0.085f, 0.09f, 1f));
            Material edge = CreateMaterial(shader, new Color(0.15f, 0.16f, 0.16f, 1f));
            Material dark = CreateMaterial(shader, new Color(0.025f, 0.03f, 0.032f, 1f));
            Material amber = CreateMaterial(shader, new Color(0.49f, 0.31f, 0.12f, 1f));
            _statusLedMaterial = CreateMaterial(shader, new Color(0.13f, 0.075f, 0.025f, 1f));

            AddPart(PrimitiveType.Cube, "Recorder charcoal body", Vector3.zero,
                new Vector3(0.20f, 0.14f, SoulRecorderPresentationTuning.RecorderBodyDepth),
                Quaternion.identity, body);
            AddPart(PrimitiveType.Cube, "Recorder edge plate", new Vector3(0f, 0f, -0.029f),
                new Vector3(0.188f, 0.128f, 0.004f), Quaternion.identity, edge);
            AddPart(PrimitiveType.Cube, "Cassette bay recess", new Vector3(0f, 0.018f, -0.032f),
                new Vector3(0.132f, 0.082f, 0.004f), Quaternion.identity, dark);
            AddCassetteBayFrame(edge, amber);
            AddPart(PrimitiveType.Cube, "SOULPLAYER amber stripe", new Vector3(0f, 0.064f, -0.035f),
                new Vector3(0.126f, 0.006f, 0.004f), Quaternion.identity, amber);

            for (int index = 0; index < 6; index++)
            {
                AddPart(
                    PrimitiveType.Cube,
                    "Speaker grille " + (index + 1),
                    new Vector3(-0.074f + (index * 0.010f), -0.045f, -0.034f),
                    new Vector3(0.004f, 0.035f, 0.004f),
                    Quaternion.identity,
                    dark);
            }

            AddPart(PrimitiveType.Cube, "Play control", new Vector3(0.051f, -0.047f, -0.034f),
                new Vector3(0.026f, 0.022f, 0.007f), Quaternion.identity, amber);
            AddPart(PrimitiveType.Cube, "Stop control", new Vector3(0.082f, -0.047f, -0.034f),
                new Vector3(0.022f, 0.022f, 0.007f), Quaternion.identity, dark);
            AddPart(PrimitiveType.Sphere, "Status LED", new Vector3(0.080f, 0.052f, -0.036f),
                new Vector3(0.010f, 0.010f, 0.005f), Quaternion.identity, _statusLedMaterial);

            SoulTapeCassetteVisual cassette = SoulTapeCassetteVisual.Create(
                _root.transform,
                shader,
                "Inserted SoulTape cassette");
            if (cassette == null)
            {
                throw new InvalidOperationException("procedural cassette construction returned no visual");
            }

            _cassetteRoot = cassette.gameObject;
            _reelLeft = cassette.LeftReel;
            _reelRight = cassette.RightReel;
            _cassetteStartPosition = SoulRecorderPresentationTuning.CassetteInsertionStartPosition;
            _cassetteStartRotation = Quaternion.Euler(
                SoulRecorderPresentationTuning.CassetteInsertionStartRotationEuler);
            _cassetteAlignmentPosition =
                SoulRecorderPresentationTuning.CassetteAlignmentPosition;
            _cassetteAlignmentRotation = Quaternion.Euler(
                SoulRecorderPresentationTuning.CassetteAlignmentRotationEuler);
            _cassetteEndPosition = SoulRecorderPresentationTuning.CassetteInsertionEndPosition;
            _cassetteEndRotation = Quaternion.Euler(
                SoulRecorderPresentationTuning.CassetteInsertionEndRotationEuler);
            _cassetteEjectPosition = SoulRecorderPresentationTuning.CassetteEjectPosition;
            _cassetteEjectRotation = Quaternion.Euler(
                SoulRecorderPresentationTuning.CassetteEjectRotationEuler);
            _cassetteAuthority.UseProceduralFallback();
            _cassetteRoot.SetActive(false);
            _root.SetActive(false);
        }

        private void AddCassetteBayFrame(Material frame, Material amber)
        {
            const float outerWidth = 0.132f;
            const float outerHeight = 0.082f;
            float verticalWidth =
                (outerWidth - SoulRecorderPresentationTuning.CassetteBayOpeningWidth) * 0.5f;
            float horizontalHeight =
                (outerHeight - SoulRecorderPresentationTuning.CassetteBayOpeningHeight) * 0.5f;
            float verticalX =
                (SoulRecorderPresentationTuning.CassetteBayOpeningWidth + verticalWidth) * 0.5f;
            float horizontalY =
                (SoulRecorderPresentationTuning.CassetteBayOpeningHeight + horizontalHeight) * 0.5f;
            float z = SoulRecorderPresentationTuning.CassetteBayFrameZ;
            float depth = SoulRecorderPresentationTuning.CassetteBayFrameDepth;

            AddPart(PrimitiveType.Cube, "Cassette bay left frame",
                new Vector3(-verticalX, 0.018f, z),
                new Vector3(verticalWidth, outerHeight, depth), Quaternion.identity, frame);
            AddPart(PrimitiveType.Cube, "Cassette bay right frame",
                new Vector3(verticalX, 0.018f, z),
                new Vector3(verticalWidth, outerHeight, depth), Quaternion.identity, frame);
            AddPart(PrimitiveType.Cube, "Cassette bay top frame",
                new Vector3(0f, 0.018f + horizontalY, z),
                new Vector3(outerWidth, horizontalHeight, depth), Quaternion.identity, frame);
            AddPart(PrimitiveType.Cube, "Cassette bay lower frame",
                new Vector3(0f, 0.018f - horizontalY, z),
                new Vector3(outerWidth, horizontalHeight, depth), Quaternion.identity, frame);
            AddPart(PrimitiveType.Cube, "Cassette bay amber sill",
                new Vector3(0f, 0.018f - horizontalY + 0.002f, z - 0.0035f),
                new Vector3(SoulRecorderPresentationTuning.CassetteBayOpeningWidth, 0.004f, 0.002f),
                Quaternion.identity, amber);
        }

        private void AddPart(
            PrimitiveType primitive,
            string name,
            Vector3 position,
            Vector3 scale,
            Quaternion rotation,
            Material material)
        {
            SoulTapeCassetteVisual.AddPart(
                _root.transform,
                primitive,
                name,
                position,
                scale,
                rotation,
                material);
        }

        private Material CreateMaterial(Shader shader, Color color)
        {
            Material material = new Material(shader);
            material.color = color;
            _ownedMaterials.Add(material);
            return material;
        }

        private void ApplyStatusLed(bool isPlaying)
        {
            if (_statusLedMaterial != null)
            {
                _statusLedMaterial.color = isPlaying
                    ? new Color(0.96f, 0.48f, 0.10f, 1f)
                    : new Color(0.13f, 0.075f, 0.025f, 1f);
            }
        }

        private Transform ResolveViewTransform()
        {
            Camera main = Camera.main;
            if (IsUsableGameplayCamera(main))
            {
                return main.transform;
            }

            Camera[] cameras = Camera.allCameras;
            Camera best = null;
            foreach (Camera camera in cameras)
            {
                if (!IsUsableGameplayCamera(camera))
                {
                    continue;
                }

                if (best == null || camera.depth < best.depth)
                {
                    best = camera;
                }
            }

            if (best != null)
            {
                return best.transform;
            }

            return _player == null ? null : _player.CameraPosition;
        }

        private static bool IsUsableGameplayCamera(Camera camera)
        {
            if (camera == null || !camera.enabled || !camera.gameObject.activeInHierarchy ||
                camera.targetTexture != null || camera.orthographic)
            {
                return false;
            }

            Rect rect = camera.rect;
            return rect.x <= 0.01f && rect.y <= 0.01f &&
                   rect.width >= 0.98f && rect.height >= 0.98f;
        }

        private void LogPackagedVisibilityOnce(Camera camera)
        {
            if (!_usingPackagedModel || _packagedVisibilityLogged || _root == null)
            {
                return;
            }

            _packagedVisibilityLogged = true;
            StringBuilder report = new StringBuilder();
            report.AppendLine("SoulRecorder packaged presentation visibility diagnostic:");
            if (camera == null)
            {
                report.AppendLine(
                    "Selected gameplay camera: unavailable; using the player camera-position fallback.");
            }
            else
            {
                report.AppendLine("Selected gameplay camera:");
                report.AppendLine("  name=" + camera.name);
                report.AppendLine("  depth=" + camera.depth);
                report.AppendLine("  near/far=" + camera.nearClipPlane + "/" +
                    camera.farClipPlane);
                report.AppendLine("  cullingMask=" + camera.cullingMask);
            }

            Transform rootTransform = _root.transform;
            if (_presentationRoot != null)
            {
                Transform presentation = _presentationRoot.transform;
                report.AppendLine("First-person presentation root:");
                report.AppendLine("  activeSelf=" + _presentationRoot.activeSelf);
                report.AppendLine("  activeInHierarchy=" +
                    _presentationRoot.activeInHierarchy);
                report.AppendLine("  localPosition=" +
                    FormatVector(presentation.localPosition));
                report.AppendLine("  localRotation=" +
                    FormatVector(presentation.localRotation.eulerAngles));
                report.AppendLine("  localScale=" +
                    FormatVector(presentation.localScale));
                report.AppendLine("  worldPosition=" +
                    FormatVector(presentation.position));
            }
            report.AppendLine("Recorder root:");
            report.AppendLine("  activeSelf=" + _root.activeSelf);
            report.AppendLine("  activeInHierarchy=" + _root.activeInHierarchy);
            report.AppendLine("  layer=" + _root.layer);
            report.AppendLine("  localPosition=" + FormatVector(rootTransform.localPosition));
            report.AppendLine("  localRotation=" +
                FormatVector(rootTransform.localRotation.eulerAngles));
            report.AppendLine("  localScale=" + FormatVector(rootTransform.localScale));
            report.AppendLine("  worldPosition=" + FormatVector(rootTransform.position));
            report.AppendLine("  packaged recorder model renderer count=" +
                (_packagedRecorderRenderers == null ? 0 : _packagedRecorderRenderers.Length));
            if (camera != null)
            {
                report.AppendLine("Camera-local presentation anchors:");
                report.AppendLine("  recorder=" + FormatVector(
                    camera.transform.InverseTransformPoint(rootTransform.position)));
                report.AppendLine("  cassetteStart=" + FormatVector(
                    camera.transform.InverseTransformPoint(
                        rootTransform.TransformPoint(ResolveCassetteStartPosition()))));
                report.AppendLine("  cassetteAlignment=" + FormatVector(
                    camera.transform.InverseTransformPoint(
                        rootTransform.TransformPoint(ResolveCassetteAlignmentPosition()))));
                report.AppendLine("  cassetteInserted=" + FormatVector(
                    camera.transform.InverseTransformPoint(
                        rootTransform.TransformPoint(ResolveCassetteInsertedPosition()))));
                report.AppendLine("  cassetteEject=" + FormatVector(
                    camera.transform.InverseTransformPoint(
                        rootTransform.TransformPoint(ResolveCassetteEjectPosition()))));
            }

            Renderer[] renderers = _root.GetComponentsInChildren<Renderer>(true);
            bool hasCombinedBounds = false;
            Bounds combinedBounds = new Bounds();
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                Bounds bounds = renderer.bounds;
                if (renderer.enabled && renderer.gameObject.activeInHierarchy &&
                    bounds.size.sqrMagnitude > 0.00000001f)
                {
                    if (!hasCombinedBounds)
                    {
                        combinedBounds = bounds;
                        hasCombinedBounds = true;
                    }
                    else
                    {
                        combinedBounds.Encapsulate(bounds);
                    }
                }

                Vector3 viewport = camera == null
                    ? Vector3.zero
                    : camera.WorldToViewportPoint(bounds.center);
                bool layerRendered = camera == null ||
                    (camera.cullingMask & (1 << renderer.gameObject.layer)) != 0;
                report.AppendLine("Renderer:");
                report.AppendLine("  name=" + renderer.gameObject.name);
                report.AppendLine("  enabled=" + renderer.enabled);
                report.AppendLine("  activeInHierarchy=" +
                    renderer.gameObject.activeInHierarchy);
                report.AppendLine("  layer=" + renderer.gameObject.layer);
                report.AppendLine("  type=" + renderer.GetType().Name);
                Material[] materials = renderer.sharedMaterials;
                for (int materialIndex = 0; materialIndex < materials.Length;
                     materialIndex++)
                {
                    Material material = materials[materialIndex];
                    report.AppendLine("  material[" + materialIndex + "]=" +
                        (material == null ? "<null>" : material.name));
                    report.AppendLine("    shader=" +
                        (material == null || material.shader == null
                            ? "<null>"
                            : material.shader.name));
                    report.AppendLine("    albedo=" + TextureName(material, "_MainTex"));
                    report.AppendLine("    normal=" + TextureName(material, "_BumpMap"));
                    report.AppendLine("    metallicSmoothness=" +
                        TextureName(material, "_MetallicGlossMap"));
                    report.AppendLine("    color=" +
                        (material == null || !material.HasProperty("_Color")
                            ? "<unavailable>"
                            : material.GetColor("_Color").ToString("F3")));
                }
                report.AppendLine("  boundsCenter=" + FormatVector(bounds.center));
                report.AppendLine("  boundsSize=" + FormatVector(bounds.size));
                if (camera != null)
                {
                    report.AppendLine("  viewport=" + FormatVector(viewport));
                    report.AppendLine("  behindCamera=" + (viewport.z <= 0f));
                    report.AppendLine("  centerOutsideViewport=" +
                        (viewport.x < 0f || viewport.x > 1f ||
                         viewport.y < 0f || viewport.y > 1f));
                    report.AppendLine("  cameraRendersLayer=" + layerRendered);
                }
            }

            report.AppendLine("Combined active renderer bounds: " +
                (hasCombinedBounds
                    ? "center=" + FormatVector(combinedBounds.center) +
                      ", size=" + FormatVector(combinedBounds.size)
                    : "none"));
            Plugin.Log.LogInfo(report.ToString());
        }

        private static string TextureName(Material material, string property)
        {
            if (material == null || !material.HasProperty(property))
            {
                return "<unavailable>";
            }
            Texture texture = material.GetTexture(property);
            return texture == null ? "<null>" : texture.name;
        }

        private static string FormatVector(Vector3 value)
        {
            return string.Format("({0:F3}, {1:F3}, {2:F3})", value.x, value.y, value.z);
        }

        private static Vector3 ResolvePresentationPosition()
        {
#if SOULPLAYER_PLACEMENT_TOOLS
            return DevelopmentSoulRecorderPresentationTuner.PresentationPosition(
                SoulRecorderPresentationTuning.HeldPosition);
#else
            return SoulRecorderPresentationTuning.HeldPosition;
#endif
        }

        private static Vector3 ResolvePresentationRotation()
        {
#if SOULPLAYER_PLACEMENT_TOOLS
            return DevelopmentSoulRecorderPresentationTuner.PresentationRotation(
                SoulRecorderPresentationTuning.HeldRotationEuler);
#else
            return SoulRecorderPresentationTuning.HeldRotationEuler;
#endif
        }

        private static Vector3 ResolveRecorderPosition()
        {
#if SOULPLAYER_PLACEMENT_TOOLS
            return DevelopmentSoulRecorderPresentationTuner.RecorderPosition(
                SoulRecorderPresentationTuning.PrefabLocalPosition);
#else
            return SoulRecorderPresentationTuning.PrefabLocalPosition;
#endif
        }

        private static Vector3 ResolveRecorderRotation()
        {
#if SOULPLAYER_PLACEMENT_TOOLS
            return DevelopmentSoulRecorderPresentationTuner.RecorderRotation(
                SoulRecorderPresentationTuning.PrefabLocalRotationEuler);
#else
            return SoulRecorderPresentationTuning.PrefabLocalRotationEuler;
#endif
        }

        private Vector3 ResolveCassetteStartPosition()
        {
#if SOULPLAYER_PLACEMENT_TOOLS
            return DevelopmentSoulRecorderPresentationTuner.CassetteStartPosition(
                _cassetteStartPosition);
#else
            return _cassetteStartPosition;
#endif
        }

        private Quaternion ResolveCassetteStartRotation()
        {
#if SOULPLAYER_PLACEMENT_TOOLS
            return Quaternion.Euler(
                DevelopmentSoulRecorderPresentationTuner.CassetteStartRotation(
                    _cassetteStartRotation.eulerAngles));
#else
            return _cassetteStartRotation;
#endif
        }

        private Vector3 ResolveCassetteAlignmentPosition()
        {
#if SOULPLAYER_PLACEMENT_TOOLS
            return DevelopmentSoulRecorderPresentationTuner.CassetteAlignmentPosition(
                _cassetteAlignmentPosition);
#else
            return _cassetteAlignmentPosition;
#endif
        }

        private Quaternion ResolveCassetteAlignmentRotation()
        {
#if SOULPLAYER_PLACEMENT_TOOLS
            return Quaternion.Euler(
                DevelopmentSoulRecorderPresentationTuner.CassetteAlignmentRotation(
                    _cassetteAlignmentRotation.eulerAngles));
#else
            return _cassetteAlignmentRotation;
#endif
        }

        private Vector3 ResolveCassetteInsertedPosition()
        {
#if SOULPLAYER_PLACEMENT_TOOLS
            return DevelopmentSoulRecorderPresentationTuner.CassetteInsertedPosition(
                _cassetteEndPosition);
#else
            return _cassetteEndPosition;
#endif
        }

        private Quaternion ResolveCassetteInsertedRotation()
        {
#if SOULPLAYER_PLACEMENT_TOOLS
            return Quaternion.Euler(
                DevelopmentSoulRecorderPresentationTuner.CassetteInsertedRotation(
                    _cassetteEndRotation.eulerAngles));
#else
            return _cassetteEndRotation;
#endif
        }

        private Vector3 ResolveCassetteEjectPosition()
        {
#if SOULPLAYER_PLACEMENT_TOOLS
            return DevelopmentSoulRecorderPresentationTuner.CassetteEjectPosition(
                _cassetteEjectPosition);
#else
            return _cassetteEjectPosition;
#endif
        }

        private Quaternion ResolveCassetteEjectRotation()
        {
#if SOULPLAYER_PLACEMENT_TOOLS
            return Quaternion.Euler(
                DevelopmentSoulRecorderPresentationTuner.CassetteEjectRotation(
                    _cassetteEjectRotation.eulerAngles));
#else
            return _cassetteEjectRotation;
#endif
        }

        private void LogConstructionFailure(string detail)
        {
            if (_constructionFailureLogged)
            {
                return;
            }

            _constructionFailureLogged = true;
            Plugin.Log.LogError(
                "SoulRecorder procedural presentation failed and is using the safe headless " +
                "presentation; playback remains available: " + detail);
        }

        private void DestroyModel()
        {
            if (_presentationRoot != null)
            {
                Destroy(_presentationRoot);
            }
            else if (_root != null)
            {
                Destroy(_root);
            }

            _presentationRoot = null;
            _root = null;
            _recorderRoot = null;
            _cassetteRoot = null;
            _handsAnimator = null;
            _recorderGrip = null;
            _cassetteGrip = null;
            _cassetteSlot = null;
            _reelLeft = null;
            _reelRight = null;
            _statusLedMaterial = null;
            _packagedRecorderRenderers = null;
            _usingPackagedModel = false;
            _usingAnimatedHands = false;
            _usingNativeAttachments = false;
            _nativeSupportGrip = null;
            _nativeCassetteGrip = null;
            _cassetteAuthority.Clear();
            ResetAnimatedSequence();
            DestroyOwnedMaterials();
        }

        private void EnsurePresentationRoot(Transform view)
        {
            if (_usingNativeAttachments)
            {
                if (_root.transform.parent != _nativeSupportGrip)
                {
                    BindNativeModelAttachments();
                }
                return;
            }
            if (_presentationRoot == null)
            {
                _presentationRoot = new GameObject(
                    SoulRecorderPresentationTuning.PresentationRootName);
                _presentationRoot.layer = 0;
            }

            if (_presentationRoot.transform.parent != view)
            {
                _presentationRoot.transform.SetParent(view, false);
            }
            if (_root.transform.parent != _presentationRoot.transform)
            {
                _root.transform.SetParent(_presentationRoot.transform, false);
            }

            _root.transform.localPosition =
                ResolveRecorderPosition();
            _root.transform.localRotation = Quaternion.Euler(
                ResolveRecorderRotation());
            _root.transform.localScale = SoulRecorderPresentationTuning.PrefabLocalScale;
        }

        private void SetPresentationActive(bool active)
        {
            if (_root != null)
            {
                _root.SetActive(active);
            }
            if (_recorderRoot != null && _usingAnimatedHands)
            {
                _recorderRoot.SetActive(active);
            }
            if (_presentationRoot != null)
            {
                _presentationRoot.SetActive(active);
            }
        }

        private void DestroyOwnedMaterials()
        {
            foreach (Material material in _ownedMaterials)
            {
                if (material != null)
                {
                    Destroy(material);
                }
            }

            _ownedMaterials.Clear();
        }

        private void OnDisable()
        {
            ForceReset();
        }

        private void OnDestroy()
        {
            _state.Reset();
            DestroyModel();
        }
    }
}
