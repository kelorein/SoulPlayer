using System;
using EFT;
using SoulPlayer.Library;
using SoulPlayer.Recorder.Native;
using UnityEngine;

namespace SoulPlayer.Recorder
{
    /// <summary>
    /// Preferred runtime presentation for SPT 4.1.x. It drives the local player's
    /// already-installed first-person arm skeleton while the existing recorder-only
    /// presentation owns SoulPlayer's recorder and cassette models.
    /// </summary>
    internal sealed class EftNativeSoulRecorderHandsView : MonoBehaviour,
        ISoulRecorderHandsView
    {
        private readonly EftNativeHandsRuntime _nativeHands =
            new EftNativeHandsRuntime();
        private ProceduralSoulRecorderHandsView _models;
        private bool _nativeUnavailableLogged;
        private bool _restorePending;
        private float _restoreAt;

        internal bool NativeModeActive { get { return _nativeHands.IsAcquired; } }

        public float TapeInsertionSeconds
        {
            get { return Models.TapeInsertionSeconds; }
        }

        public float TapePreparationLeadSeconds
        {
            get { return Models.TapePreparationLeadSeconds; }
        }

        public float TapeEjectionSeconds
        {
            get { return Models.TapeEjectionSeconds; }
        }

        public float TapeEjectionAudioStopSeconds
        {
            get { return Models.TapeEjectionAudioStopSeconds; }
        }

        public float InteractionExitSeconds
        {
            get { return Models.InteractionExitSeconds; }
        }

        public void OnInteractionEntered(Player player)
        {
            CancelPendingRestore();
            Camera camera = ResolveGameplayCamera();
            string reason;
            bool acquired = _nativeHands.TryAcquire(player, camera, out reason);
            Models.OnInteractionEntered(player);
            if (acquired)
            {
                Models.ConfigureNativeAttachments(
                    _nativeHands.SupportGripSocket,
                    _nativeHands.CassetteGripSocket);
                Plugin.Log.LogInfo(
                    "SoulRecorder using native EFT first-person arms with SoulPlayer " +
                    "recorder/cassette assets.");
            }
            else
            {
                Models.ClearNativeAttachments();
                if (!_nativeUnavailableLogged)
                {
                    _nativeUnavailableLogged = true;
                    Plugin.Log.LogWarning(
                        "SoulRecorder native EFT arms unavailable; using recorder/cassette-only " +
                        "fallback: " + reason);
                }
            }
            if (acquired)
            {
                _nativeHands.ConfigureCassetteSlot(Models.CassetteSlotTransform);
            }
        }

        public void OnTapeInsertionStarted(MusicTrack tape)
        {
            _nativeHands.BeginInsertion(Time.unscaledTime);
            Models.OnTapeInsertionStarted(tape);
        }

        public void OnTapeInserted(MusicTrack tape)
        {
            Models.OnTapeInserted(tape);
            _nativeHands.SetPhase(
                SoulRecorderNativeHandsPhase.Holding, Time.unscaledTime);
        }

        public void OnPlaybackChanged(bool isPlaying)
        {
            Models.OnPlaybackChanged(isPlaying);
        }

        public void OnTapeEjectionStarted(MusicTrack tape)
        {
            _nativeHands.BeginEjection(Time.unscaledTime);
            Models.OnTapeEjectionStarted(tape);
        }

        public void OnTapeEjected(MusicTrack tape)
        {
            Models.OnTapeEjected(tape);
        }

        public void OnInteractionExited()
        {
            Models.OnInteractionExited();
            if (_nativeHands.IsAcquired)
            {
                _nativeHands.SetPhase(
                    SoulRecorderNativeHandsPhase.Exiting, Time.unscaledTime);
                _restorePending = true;
                _restoreAt = Time.unscaledTime + InteractionExitSeconds;
            }
        }

        public void ForceReset()
        {
            CancelPendingRestore();
            if (_models != null)
            {
                _models.ForceReset();
                _models.ClearNativeAttachments();
            }
            _nativeHands.Cleanup("presentation force reset");
        }

        private ProceduralSoulRecorderHandsView Models
        {
            get
            {
                if (_models == null)
                {
                    _models = gameObject.AddComponent<ProceduralSoulRecorderHandsView>();
                    _models.ConfigurePackagedAnimatedHands(false);
                }
                return _models;
            }
        }

        private void Awake()
        {
            ProceduralSoulRecorderHandsView unused = Models;
        }

        private void LateUpdate()
        {
            _nativeHands.Tick(Time.unscaledTime);
            if (_restorePending && Time.unscaledTime >= _restoreAt)
            {
                _restorePending = false;
                _restoreAt = 0f;
                Models.ClearNativeAttachments();
                _nativeHands.Cleanup("normal interaction exit");
                Plugin.Log.LogInfo(
                    "SoulRecorder native EFT arm/IK/finger state restored after interaction.");
            }
        }

        private void OnDisable()
        {
            ForceReset();
        }

        private void OnDestroy()
        {
            ForceReset();
        }

        private void CancelPendingRestore()
        {
            _restorePending = false;
            _restoreAt = 0f;
        }

        private static Camera ResolveGameplayCamera()
        {
            Camera main = Camera.main;
            if (IsGameplayCamera(main))
            {
                return main;
            }
            Camera best = null;
            foreach (Camera candidate in Camera.allCameras)
            {
                if (!IsGameplayCamera(candidate))
                {
                    continue;
                }
                if (best == null || candidate.depth < best.depth)
                {
                    best = candidate;
                }
            }
            return best;
        }

        private static bool IsGameplayCamera(Camera camera)
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
    }
}
