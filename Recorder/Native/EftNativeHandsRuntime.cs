using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Diz.Skinning;
using EFT;
using EFT.Visual;
using RootMotion.FinalIK;
using UnityEngine;

namespace SoulPlayer.Recorder.Native
{
    internal enum SoulRecorderNativeHandsPhase
    {
        Hidden,
        Entering,
        Inserting,
        Holding,
        StopEntering,
        Ejecting,
        Exiting
    }

    /// <summary>
    /// Keeps the native EFT-arm phases aligned with the authored recorder sequence.
    /// The usable-item duration already includes Enter/StopEnter, so those phases must
    /// finish before the cassette hand advances to Insert/Eject.
    /// </summary>
    internal sealed class SoulRecorderNativePhaseTimeline
    {
        private bool _transitionPending;
        private SoulRecorderNativeHandsPhase _nextPhase;
        private float _transitionAt;

        internal SoulRecorderNativeHandsPhase Phase { get; private set; }
        internal float PhaseStartedAt { get; private set; }
        internal bool TransitionPending { get { return _transitionPending; } }
        internal SoulRecorderNativeHandsPhase NextPhase { get { return _nextPhase; } }
        internal float TransitionAt { get { return _transitionAt; } }

        internal void BeginInsertion(float now)
        {
            SetImmediate(SoulRecorderNativeHandsPhase.Entering, now);
            Schedule(
                SoulRecorderNativeHandsPhase.Inserting,
                now + ProceduralSoulRecorderHandsView.EnterClipSeconds);
        }

        internal void BeginEjection(float now)
        {
            SetImmediate(SoulRecorderNativeHandsPhase.StopEntering, now);
            Schedule(
                SoulRecorderNativeHandsPhase.Ejecting,
                now + ProceduralSoulRecorderHandsView.StopEnterClipSeconds);
        }

        internal void SetImmediate(SoulRecorderNativeHandsPhase phase, float now)
        {
            Phase = phase;
            PhaseStartedAt = now;
            _transitionPending = false;
            _nextPhase = SoulRecorderNativeHandsPhase.Hidden;
            _transitionAt = 0f;
        }

        internal bool Advance(float now)
        {
            if (!_transitionPending || now < _transitionAt)
            {
                return false;
            }

            SoulRecorderNativeHandsPhase phase = _nextPhase;
            float startedAt = _transitionAt;
            SetImmediate(phase, startedAt);
            return true;
        }

        internal void Reset()
        {
            SetImmediate(SoulRecorderNativeHandsPhase.Hidden, 0f);
        }

        internal static float EjectionTransferAt(float ejectionStartedAt)
        {
            return ejectionStartedAt +
                ProceduralSoulRecorderHandsView.StopEnterClipSeconds +
                ProceduralSoulRecorderHandsView.EjectTransferSeconds;
        }

        internal static float EjectionTransferProgress
        {
            get
            {
                return ProceduralSoulRecorderHandsView.EjectTransferSeconds /
                    ProceduralSoulRecorderHandsView.EjectClipSeconds;
            }
        }

        internal static float EjectionContactAmount(float phaseProgress)
        {
            float transfer = EjectionTransferProgress;
            if (phaseProgress <= transfer)
            {
                return SoulRecorderPresentationState.Smooth(
                    phaseProgress / transfer);
            }
            return 1f - SoulRecorderPresentationState.Smooth(
                (phaseProgress - transfer) / (1f - transfer));
        }

        private void Schedule(SoulRecorderNativeHandsPhase phase, float at)
        {
            _transitionPending = true;
            _nextPhase = phase;
            _transitionAt = at;
        }
    }

    internal struct SoulRecorderNativeRigidPose
    {
        internal SoulRecorderNativeRigidPose(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }

        internal Vector3 Position { get; private set; }
        internal Quaternion Rotation { get; private set; }
    }

    internal static class SoulRecorderNativeContactSolver
    {
        /// <summary>
        /// Solves W * R = S for the wrist pose W, where R is the captured rigid
        /// wrist-to-cassette-root transform and S is the live CassetteSlot pose.
        /// </summary>
        internal static SoulRecorderNativeRigidPose SolveWristContactPose(
            SoulRecorderNativeRigidPose cassetteSlot,
            SoulRecorderNativeRigidPose wristToCassetteRoot)
        {
            Quaternion wristRotation = cassetteSlot.Rotation *
                Quaternion.Inverse(wristToCassetteRoot.Rotation);
            Vector3 wristPosition = cassetteSlot.Position -
                (wristRotation * wristToCassetteRoot.Position);
            return new SoulRecorderNativeRigidPose(wristPosition, wristRotation);
        }
    }

    internal sealed class EftNativeHandsDiscoveryResult
    {
        private readonly Dictionary<string, Transform> _bones;

        internal EftNativeHandsDiscoveryResult(
            Player player,
            PlayerBody body,
            SkinnedMeshRenderer renderer,
            Skeleton skeleton,
            SoulRecorderNativeBoneResolution roles,
            Dictionary<string, Transform> bones)
        {
            Player = player;
            Body = body;
            Renderer = renderer;
            Skeleton = skeleton;
            Roles = roles;
            _bones = bones;
        }

        internal Player Player { get; private set; }
        internal PlayerBody Body { get; private set; }
        internal SkinnedMeshRenderer Renderer { get; private set; }
        internal Skeleton Skeleton { get; private set; }
        internal SoulRecorderNativeBoneResolution Roles { get; private set; }

        internal Transform Bone(string name)
        {
            Transform value;
            return !string.IsNullOrEmpty(name) && _bones.TryGetValue(name, out value)
                ? value
                : null;
        }

        internal IEnumerable<KeyValuePair<string, Transform>> Bones
        {
            get { return _bones; }
        }
    }

    internal static class EftNativeHandsDiscovery
    {
        internal static bool TryResolve(
            Player player,
            out EftNativeHandsDiscoveryResult result,
            out string reason)
        {
            result = null;
            reason = string.Empty;
            bool playerExists = !ReferenceEquals(player, null);
            string playerReason = SoulRecorderNativeDiscoveryPolicy.ValidatePlayer(
                playerExists,
                playerExists && player.IsYourPlayer,
                playerExists && player.PointOfView == EPointOfView.FirstPerson);
            if (!string.IsNullOrEmpty(playerReason))
            {
                reason = playerReason;
                return false;
            }

            PlayerBody body = player.PlayerBody;
            if (body == null)
            {
                reason = "PlayerBody is unavailable";
                return false;
            }
            Skeleton skeleton = body.SkeletonHands;
            if (skeleton == null || skeleton.Bones == null || skeleton.Bones.Count == 0)
            {
                reason = "PlayerBody.SkeletonHands has no bones";
                return false;
            }

            LoddedSkin handsSkin;
            if (body.BodySkins == null ||
                !body.BodySkins.TryGetValue(EBodyModelPart.Hands, out handsSkin) ||
                handsSkin == null)
            {
                reason = "PlayerBody.BodySkins[Hands] is unavailable";
                return false;
            }

            SkinnedMeshRenderer renderer = null;
            if (handsSkin._lods != null)
            {
                foreach (AbstractSkin skin in handsSkin._lods)
                {
                    if (skin != null && skin.SkinnedMeshRenderer != null)
                    {
                        renderer = skin.SkinnedMeshRenderer;
                        break;
                    }
                }
            }
            if (renderer == null)
            {
                reason = "native Hands skin has no SkinnedMeshRenderer";
                return false;
            }

            Dictionary<string, Transform> bones = new Dictionary<string, Transform>(
                StringComparer.Ordinal);
            foreach (KeyValuePair<string, Transform> pair in skeleton.Bones)
            {
                if (!string.IsNullOrEmpty(pair.Key) && pair.Value != null &&
                    !bones.ContainsKey(pair.Key))
                {
                    bones.Add(pair.Key, pair.Value);
                }
            }
            Transform searchRoot = body.MeshTransform == null
                ? body.transform
                : body.MeshTransform;
            foreach (Transform transform in
                searchRoot.GetComponentsInChildren<Transform>(true))
            {
                if (transform != null && !bones.ContainsKey(transform.name))
                {
                    bones.Add(transform.name, transform);
                }
            }

            List<SoulRecorderNativeBoneDescriptor> descriptors = bones.Values
                .Select(value => new SoulRecorderNativeBoneDescriptor(
                    value.name,
                    value.parent == null ? string.Empty : value.parent.name))
                .ToList();
            SoulRecorderNativeBoneResolution roles =
                SoulRecorderNativeBoneRoleResolver.Resolve(descriptors);
            if (!roles.IsComplete)
            {
                reason = "native arm roles could not be resolved from SkeletonHands";
                return false;
            }

            result = new EftNativeHandsDiscoveryResult(
                player, body, renderer, skeleton, roles, bones);
            return true;
        }
    }

    internal sealed class EftNativeHandsRuntime
    {
        private sealed class TransformSnapshot
        {
            internal Vector3 LocalPosition;
            internal Quaternion LocalRotation;
            internal Vector3 LocalScale;
        }

        private sealed class TransformStateHandle : ISoulRecorderNativeStateHandle
        {
            private readonly Transform _transform;

            internal TransformStateHandle(string key, Transform transform)
            {
                Key = key;
                _transform = transform;
            }

            public string Key { get; private set; }

            public object CaptureState()
            {
                return new TransformSnapshot
                {
                    LocalPosition = _transform.localPosition,
                    LocalRotation = _transform.localRotation,
                    LocalScale = _transform.localScale
                };
            }

            public void RestoreState(object state)
            {
                TransformSnapshot snapshot = state as TransformSnapshot;
                if (_transform == null || snapshot == null)
                {
                    return;
                }
                _transform.localPosition = snapshot.LocalPosition;
                _transform.localRotation = snapshot.LocalRotation;
                _transform.localScale = snapshot.LocalScale;
            }
        }

        private sealed class IkSnapshot
        {
            internal bool Enabled;
            internal Transform Target;
            internal Transform BendGoal;
            internal float PositionWeight;
            internal float RotationWeight;
            internal float BendWeight;
        }

        private sealed class IkStateHandle : ISoulRecorderNativeStateHandle
        {
            private readonly LimbIK _ik;

            internal IkStateHandle(string key, LimbIK ik)
            {
                Key = key;
                _ik = ik;
            }

            public string Key { get; private set; }

            public object CaptureState()
            {
                return new IkSnapshot
                {
                    Enabled = _ik.enabled,
                    Target = _ik.solver.target,
                    BendGoal = _ik.solver.bendGoal,
                    PositionWeight = _ik.solver.IKPositionWeight,
                    RotationWeight = _ik.solver.IKRotationWeight,
                    BendWeight = _ik.solver.bendModifierWeight
                };
            }

            public void RestoreState(object state)
            {
                IkSnapshot snapshot = state as IkSnapshot;
                if (_ik == null || snapshot == null)
                {
                    return;
                }
                _ik.solver.target = snapshot.Target;
                _ik.solver.bendGoal = snapshot.BendGoal;
                _ik.solver.IKPositionWeight = snapshot.PositionWeight;
                _ik.solver.IKRotationWeight = snapshot.RotationWeight;
                _ik.solver.bendModifierWeight = snapshot.BendWeight;
                _ik.enabled = snapshot.Enabled;
            }
        }

        private readonly SoulRecorderNativeStateGuard _stateGuard =
            new SoulRecorderNativeStateGuard();
        private readonly SoulRecorderNativePhaseTimeline _phaseTimeline =
            new SoulRecorderNativePhaseTimeline();
        private readonly List<Transform> _supportFingers = new List<Transform>();
        private readonly List<Transform> _cassetteFingers = new List<Transform>();
        private readonly Dictionary<Transform, Quaternion> _fingerBaseRotations =
            new Dictionary<Transform, Quaternion>();

        private EftNativeHandsDiscoveryResult _discovery;
        private LimbIK _leftIk;
        private LimbIK _rightIk;
        private bool _createdLeftIk;
        private bool _createdRightIk;
        private GameObject _supportTargetObject;
        private GameObject _cassetteTargetObject;
        private GameObject _supportBendObject;
        private GameObject _cassetteBendObject;
        private GameObject _supportGripObject;
        private GameObject _cassetteGripObject;
        private Transform _cameraTransform;
        private Transform _leftWrist;
        private Transform _rightWrist;
        private Transform _cassetteSlot;
        private Quaternion _supportWristBaselineCameraRotation = Quaternion.identity;
        private Quaternion _cassetteWristBaselineCameraRotation = Quaternion.identity;
        private bool _postIkSubscribed;
        private bool _diagnosticLogged;
        private bool _missingCassetteSlotLogged;

        internal bool IsAcquired { get { return _discovery != null; } }
        internal Transform SupportGripSocket
        {
            get { return _supportGripObject == null ? null : _supportGripObject.transform; }
        }
        internal Transform CassetteGripSocket
        {
            get { return _cassetteGripObject == null ? null : _cassetteGripObject.transform; }
        }

        internal bool TryAcquire(Player player, Camera camera, out string reason)
        {
            Cleanup("reacquire");
            reason = string.Empty;
            if (camera == null)
            {
                reason = "active full-screen gameplay camera is unavailable";
                return false;
            }

            EftNativeHandsDiscoveryResult discovery;
            if (!EftNativeHandsDiscovery.TryResolve(player, out discovery, out reason))
            {
                return false;
            }
            _discovery = discovery;

            try
            {
                SoulRecorderNativeArmBones left = discovery.Roles.Left;
                SoulRecorderNativeArmBones right = discovery.Roles.Right;
                _cameraTransform = camera.transform;
                Transform leftCollar = discovery.Bone(left.Collarbone);
                Transform rightCollar = discovery.Bone(right.Collarbone);
                _leftIk = ResolveOrCreateIk(
                    leftCollar,
                    discovery.Bone(left.UpperArm),
                    discovery.Bone(left.Forearm),
                    discovery.Bone(left.Wrist),
                    out _createdLeftIk);
                _rightIk = ResolveOrCreateIk(
                    rightCollar,
                    discovery.Bone(right.UpperArm),
                    discovery.Bone(right.Forearm),
                    discovery.Bone(right.Wrist),
                    out _createdRightIk);
                if (_leftIk == null || _rightIk == null)
                {
                    reason = "native LimbIK components could not be resolved or initialized";
                    Cleanup("IK acquisition failed");
                    return false;
                }

                _stateGuard.CaptureOnce(new IkStateHandle("ik:left", _leftIk));
                _stateGuard.CaptureOnce(new IkStateHandle("ik:right", _rightIk));
                CaptureFingerState(discovery, left.Fingers, _supportFingers, "left");
                CaptureFingerState(discovery, right.Fingers, _cassetteFingers, "right");

                _supportTargetObject = CreateCameraTarget(
                    "SoulRecorder Native Support Hand Target", camera.transform);
                _cassetteTargetObject = CreateCameraTarget(
                    "SoulRecorder Native Cassette Hand Target", camera.transform);
                _supportBendObject = CreateCameraTarget(
                    "SoulRecorder Native Support Elbow Goal", camera.transform);
                _cassetteBendObject = CreateCameraTarget(
                    "SoulRecorder Native Cassette Elbow Goal", camera.transform);

                Transform leftWrist = discovery.Bone(left.Wrist);
                Transform rightWrist = discovery.Bone(right.Wrist);
                Transform leftPalm = discovery.Bone(left.Palm);
                Transform rightPalm = discovery.Bone(right.Palm);
                _leftWrist = leftWrist;
                _rightWrist = rightWrist;
                CaptureEmptyHandsWristBaselines();
                _supportGripObject = CreateGripSocket(
                    "SoulRecorderGripSocket", leftPalm);
                _cassetteGripObject = CreateGripSocket(
                    "SoulTapeGripSocket", rightPalm);
                ApplyGripSocketTuning();

                ConfigureIk(
                    _leftIk, _supportTargetObject.transform, _supportBendObject.transform);
                ConfigureIk(
                    _rightIk, _cassetteTargetObject.transform, _cassetteBendObject.transform);
                SubscribePostIk();
                _phaseTimeline.SetImmediate(
                    SoulRecorderNativeHandsPhase.Entering, Time.unscaledTime);
                Tick(Time.unscaledTime);
                LogDiscoveryOnce();
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                Cleanup("native acquisition exception");
                return false;
            }
        }

        internal void SetPhase(SoulRecorderNativeHandsPhase phase, float now)
        {
            _phaseTimeline.SetImmediate(phase, now);
        }

        internal void BeginInsertion(float now)
        {
            _phaseTimeline.BeginInsertion(now);
        }

        internal void BeginEjection(float now)
        {
            _phaseTimeline.BeginEjection(now);
        }

        internal void ConfigureCassetteSlot(Transform cassetteSlot)
        {
            _cassetteSlot = cassetteSlot;
            _missingCassetteSlotLogged = false;
        }

        internal void Tick(float now)
        {
            _phaseTimeline.Advance(now);
            if (!IsAcquired || _supportTargetObject == null ||
                _cassetteTargetObject == null)
            {
                return;
            }
            float progress = PhaseProgress(now);
            ApplyGripSocketTuning();
            Vector3 supportWorking = ResolveNativeSupportTargetPosition();
            Vector3 cassetteWorking = ResolveNativeCassetteCarryTargetPosition();
            Vector3 supportHidden = supportWorking + ResolveNativeSupportHiddenOffset();
            Vector3 cassetteHidden = cassetteWorking + ResolveNativeCassetteHiddenOffset();
            Quaternion supportWorkingRotation =
                _supportWristBaselineCameraRotation * Quaternion.Euler(
                    ResolveNativeSupportWristRelativeRotation());
            Quaternion cassetteWorkingRotation =
                _cassetteWristBaselineCameraRotation * Quaternion.Euler(
                    ResolveNativeCassetteWristRelativeRotation());

            Vector3 supportPosition = supportWorking;
            Vector3 cassettePosition = cassetteWorking;
            Quaternion supportRotation = supportWorkingRotation;
            Quaternion cassetteRotation = cassetteWorkingRotation;
            Vector3 cassetteContactPosition;
            Quaternion cassetteContactRotation;
            bool hasCassetteContact = TryResolveCassetteContactTarget(
                out cassetteContactPosition,
                out cassetteContactRotation);
            if (!hasCassetteContact && !_missingCassetteSlotLogged &&
                (_phaseTimeline.Phase == SoulRecorderNativeHandsPhase.Inserting ||
                 _phaseTimeline.Phase == SoulRecorderNativeHandsPhase.Ejecting))
            {
                _missingCassetteSlotLogged = true;
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogError(
                        "SoulRecorder native cassette contact unavailable: the active " +
                        "recorder CassetteSlot could not be resolved; keeping the hand " +
                        "at its carry pose without using a guessed contact coordinate.");
                }
            }
            switch (_phaseTimeline.Phase)
            {
                case SoulRecorderNativeHandsPhase.Entering:
                    supportPosition = Vector3.Lerp(supportHidden, supportWorking, progress);
                    cassettePosition = Vector3.Lerp(cassetteHidden, cassetteWorking, progress);
                    supportRotation = Quaternion.Slerp(
                        _supportWristBaselineCameraRotation,
                        supportWorkingRotation,
                        progress);
                    cassetteRotation = Quaternion.Slerp(
                        _cassetteWristBaselineCameraRotation,
                        cassetteWorkingRotation,
                        progress);
                    break;
                case SoulRecorderNativeHandsPhase.Inserting:
                    if (hasCassetteContact)
                    {
                        cassettePosition = Vector3.Lerp(
                            cassetteWorking, cassetteContactPosition, progress);
                        cassetteRotation = Quaternion.Slerp(
                            cassetteWorkingRotation, cassetteContactRotation, progress);
                    }
                    break;
                case SoulRecorderNativeHandsPhase.StopEntering:
                    supportPosition = Vector3.Lerp(supportHidden, supportWorking, progress);
                    cassettePosition = Vector3.Lerp(cassetteHidden, cassetteWorking, progress);
                    supportRotation = Quaternion.Slerp(
                        _supportWristBaselineCameraRotation,
                        supportWorkingRotation,
                        progress);
                    cassetteRotation = Quaternion.Slerp(
                        _cassetteWristBaselineCameraRotation,
                        cassetteWorkingRotation,
                        progress);
                    break;
                case SoulRecorderNativeHandsPhase.Ejecting:
                    if (hasCassetteContact)
                    {
                        float contactAmount =
                            SoulRecorderNativePhaseTimeline.EjectionContactAmount(
                                PhaseLinearProgress(now));
                        cassettePosition = Vector3.Lerp(
                            cassetteWorking, cassetteContactPosition, contactAmount);
                        cassetteRotation = Quaternion.Slerp(
                            cassetteWorkingRotation, cassetteContactRotation, contactAmount);
                    }
                    break;
                case SoulRecorderNativeHandsPhase.Exiting:
                    supportPosition = Vector3.Lerp(supportWorking, supportHidden, progress);
                    cassettePosition = Vector3.Lerp(cassetteWorking, cassetteHidden, progress);
                    supportRotation = Quaternion.Slerp(
                        supportWorkingRotation,
                        _supportWristBaselineCameraRotation,
                        progress);
                    cassetteRotation = Quaternion.Slerp(
                        cassetteWorkingRotation,
                        _cassetteWristBaselineCameraRotation,
                        progress);
                    break;
            }

            SetLocalPose(_supportTargetObject.transform, supportPosition, supportRotation);
            SetLocalPose(_cassetteTargetObject.transform, cassettePosition, cassetteRotation);
            SetLocalPose(_supportBendObject.transform,
                ResolveNativeSupportElbowGoal(), Quaternion.identity);
            SetLocalPose(_cassetteBendObject.transform,
                ResolveNativeCassetteElbowGoal(), Quaternion.identity);
            ApplyFingerPoses();
        }

        internal void Cleanup(string reason)
        {
            UnsubscribePostIk();
            IReadOnlyList<Exception> failures = _stateGuard.RestoreAll();
            foreach (Exception failure in failures)
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogError(
                        "SoulRecorder native-hands restore failed: " + failure.Message);
                }
            }

            DestroyObject(_supportGripObject);
            DestroyObject(_cassetteGripObject);
            DestroyObject(_supportTargetObject);
            DestroyObject(_cassetteTargetObject);
            DestroyObject(_supportBendObject);
            DestroyObject(_cassetteBendObject);
            if (_createdLeftIk && _leftIk != null)
            {
                UnityEngine.Object.Destroy(_leftIk);
            }
            if (_createdRightIk && _rightIk != null)
            {
                UnityEngine.Object.Destroy(_rightIk);
            }

            _supportGripObject = null;
            _cassetteGripObject = null;
            _supportTargetObject = null;
            _cassetteTargetObject = null;
            _supportBendObject = null;
            _cassetteBendObject = null;
            _leftIk = null;
            _rightIk = null;
            _cameraTransform = null;
            _leftWrist = null;
            _rightWrist = null;
            _cassetteSlot = null;
            _supportWristBaselineCameraRotation = Quaternion.identity;
            _cassetteWristBaselineCameraRotation = Quaternion.identity;
            _missingCassetteSlotLogged = false;
            _createdLeftIk = false;
            _createdRightIk = false;
            _supportFingers.Clear();
            _cassetteFingers.Clear();
            _fingerBaseRotations.Clear();
            _discovery = null;
            _phaseTimeline.Reset();
        }

        private static LimbIK ResolveOrCreateIk(
            Transform collar,
            Transform upper,
            Transform forearm,
            Transform wrist,
            out bool created)
        {
            created = false;
            if (collar == null || upper == null || forearm == null || wrist == null)
            {
                return null;
            }
            LimbIK ik = collar.GetComponent<LimbIK>();
            if (ik != null)
            {
                return ik;
            }
            ik = collar.gameObject.AddComponent<LimbIK>();
            created = true;
            if (!ik.solver.SetChain(upper, forearm, wrist, collar))
            {
                UnityEngine.Object.Destroy(ik);
                created = false;
                return null;
            }
            ik.solver.Initiate(collar);
            return ik;
        }

        private static void ConfigureIk(LimbIK ik, Transform target, Transform bendGoal)
        {
            ik.solver.target = target;
            ik.solver.bendGoal = bendGoal;
            ik.solver.IKPositionWeight = 1f;
            ik.solver.IKRotationWeight = 1f;
            ik.solver.bendModifierWeight = 1f;
            ik.enabled = true;
        }

        private void CaptureFingerState(
            EftNativeHandsDiscoveryResult discovery,
            IReadOnlyList<string> names,
            List<Transform> destination,
            string side)
        {
            if (names == null)
            {
                return;
            }
            foreach (string name in names)
            {
                Transform transform = discovery.Bone(name);
                if (transform == null)
                {
                    continue;
                }
                destination.Add(transform);
                _fingerBaseRotations[transform] = transform.localRotation;
                _stateGuard.CaptureOnce(new TransformStateHandle(
                    "finger:" + side + ":" + name, transform));
            }
        }

        private void SubscribePostIk()
        {
            if (_postIkSubscribed)
            {
                return;
            }
            if (_leftIk != null && _leftIk.solver != null)
            {
                _leftIk.solver.OnPostUpdate += ApplyFingerPoses;
                _postIkSubscribed = true;
            }
            else if (_rightIk != null && _rightIk.solver != null)
            {
                _rightIk.solver.OnPostUpdate += ApplyFingerPoses;
                _postIkSubscribed = true;
            }
        }

        private void UnsubscribePostIk()
        {
            if (!_postIkSubscribed)
            {
                return;
            }
            if (_leftIk != null && _leftIk.solver != null)
            {
                _leftIk.solver.OnPostUpdate -= ApplyFingerPoses;
            }
            if (_rightIk != null && _rightIk.solver != null)
            {
                _rightIk.solver.OnPostUpdate -= ApplyFingerPoses;
            }
            _postIkSubscribed = false;
        }

        private void ApplyFingerPoses()
        {
            ApplyFingerCurl(_supportFingers, 17f, false);
            ApplyFingerCurl(_cassetteFingers, 9f, true);
        }

        private void ApplyFingerCurl(
            IEnumerable<Transform> fingers,
            float degrees,
            bool pinch)
        {
            foreach (Transform finger in fingers)
            {
                if (finger == null)
                {
                    continue;
                }
                string lower = finger.name.ToLowerInvariant();
                float amount = degrees;
                if (pinch && (lower.Contains("thumb") || lower.Contains("finger1") ||
                              lower.Contains("digit1") || lower.Contains("index") ||
                              lower.Contains("middle")))
                {
                    amount = 14f;
                }
                Quaternion original;
                if (_fingerBaseRotations.TryGetValue(finger, out original))
                {
                    finger.localRotation = original * Quaternion.Euler(amount, 0f, 0f);
                }
            }
        }

        private void CaptureEmptyHandsWristBaselines()
        {
            if (_cameraTransform == null || _leftWrist == null || _rightWrist == null)
            {
                return;
            }

            Quaternion cameraInverse = Quaternion.Inverse(_cameraTransform.rotation);
            _supportWristBaselineCameraRotation =
                cameraInverse * _leftWrist.rotation;
            _cassetteWristBaselineCameraRotation =
                cameraInverse * _rightWrist.rotation;
        }

        private void ApplyGripSocketTuning()
        {
            if (_supportGripObject != null)
            {
                SetLocalPose(
                    _supportGripObject.transform,
                    ResolveNativeRecorderGripPosition(),
                    Quaternion.Euler(ResolveNativeRecorderGripRotation()));
            }
            if (_cassetteGripObject != null)
            {
                SetLocalPose(
                    _cassetteGripObject.transform,
                    ResolveNativeCassetteGripPosition(),
                    Quaternion.Euler(ResolveNativeCassetteGripRotation()));
            }
        }

        private bool TryResolveCassetteContactTarget(
            out Vector3 cameraLocalPosition,
            out Quaternion cameraLocalRotation)
        {
            cameraLocalPosition = Vector3.zero;
            cameraLocalRotation = Quaternion.identity;
            if (_cameraTransform == null || _rightWrist == null ||
                _cassetteGripObject == null || _cassetteSlot == null)
            {
                return false;
            }

            Transform grip = _cassetteGripObject.transform;
            Vector3 cassetteRootWorldPosition = grip.TransformPoint(
                SoulRecorderPresentationTuning.NativeCassetteModelPosition);
            Quaternion cassetteRootWorldRotation = grip.rotation * Quaternion.Euler(
                SoulRecorderPresentationTuning.NativeCassetteModelRotationEuler);
            Quaternion wristInverse = Quaternion.Inverse(_rightWrist.rotation);
            SoulRecorderNativeRigidPose wristToCassetteRoot =
                new SoulRecorderNativeRigidPose(
                    wristInverse * (cassetteRootWorldPosition - _rightWrist.position),
                    wristInverse * cassetteRootWorldRotation);
            SoulRecorderNativeRigidPose slotPose = new SoulRecorderNativeRigidPose(
                _cassetteSlot.position,
                _cassetteSlot.rotation);
            SoulRecorderNativeRigidPose wristContact =
                SoulRecorderNativeContactSolver.SolveWristContactPose(
                    slotPose,
                    wristToCassetteRoot);

            cameraLocalPosition = _cameraTransform.InverseTransformPoint(
                wristContact.Position);
            cameraLocalRotation = Quaternion.Inverse(_cameraTransform.rotation) *
                wristContact.Rotation;
            return true;
        }

        private static Vector3 ResolveNativeSupportTargetPosition()
        {
#if SOULPLAYER_PLACEMENT_TOOLS
            return DevelopmentSoulRecorderPresentationTuner.NativeSupportTargetPosition(
                SoulRecorderPresentationTuning.NativeSupportTargetPosition);
#else
            return SoulRecorderPresentationTuning.NativeSupportTargetPosition;
#endif
        }

        private static Vector3 ResolveNativeCassetteCarryTargetPosition()
        {
#if SOULPLAYER_PLACEMENT_TOOLS
            return DevelopmentSoulRecorderPresentationTuner.NativeCassetteCarryTargetPosition(
                SoulRecorderPresentationTuning.NativeCassetteCarryTargetPosition);
#else
            return SoulRecorderPresentationTuning.NativeCassetteCarryTargetPosition;
#endif
        }

        private static Vector3 ResolveNativeSupportHiddenOffset()
        {
#if SOULPLAYER_PLACEMENT_TOOLS
            return DevelopmentSoulRecorderPresentationTuner.NativeSupportHiddenOffset(
                SoulRecorderPresentationTuning.NativeSupportHiddenOffset);
#else
            return SoulRecorderPresentationTuning.NativeSupportHiddenOffset;
#endif
        }

        private static Vector3 ResolveNativeCassetteHiddenOffset()
        {
#if SOULPLAYER_PLACEMENT_TOOLS
            return DevelopmentSoulRecorderPresentationTuner.NativeCassetteHiddenOffset(
                SoulRecorderPresentationTuning.NativeCassetteHiddenOffset);
#else
            return SoulRecorderPresentationTuning.NativeCassetteHiddenOffset;
#endif
        }

        private static Vector3 ResolveNativeSupportWristRelativeRotation()
        {
#if SOULPLAYER_PLACEMENT_TOOLS
            return DevelopmentSoulRecorderPresentationTuner.NativeSupportWristRelativeRotation(
                SoulRecorderPresentationTuning.NativeSupportWristRelativeRotationEuler);
#else
            return SoulRecorderPresentationTuning.NativeSupportWristRelativeRotationEuler;
#endif
        }

        private static Vector3 ResolveNativeCassetteWristRelativeRotation()
        {
#if SOULPLAYER_PLACEMENT_TOOLS
            return DevelopmentSoulRecorderPresentationTuner.NativeCassetteWristRelativeRotation(
                SoulRecorderPresentationTuning.NativeCassetteWristRelativeRotationEuler);
#else
            return SoulRecorderPresentationTuning.NativeCassetteWristRelativeRotationEuler;
#endif
        }

        private static Vector3 ResolveNativeSupportElbowGoal()
        {
#if SOULPLAYER_PLACEMENT_TOOLS
            return DevelopmentSoulRecorderPresentationTuner.NativeSupportElbowGoal(
                SoulRecorderPresentationTuning.NativeSupportElbowGoal);
#else
            return SoulRecorderPresentationTuning.NativeSupportElbowGoal;
#endif
        }

        private static Vector3 ResolveNativeCassetteElbowGoal()
        {
#if SOULPLAYER_PLACEMENT_TOOLS
            return DevelopmentSoulRecorderPresentationTuner.NativeCassetteElbowGoal(
                SoulRecorderPresentationTuning.NativeCassetteElbowGoal);
#else
            return SoulRecorderPresentationTuning.NativeCassetteElbowGoal;
#endif
        }

        private static Vector3 ResolveNativeRecorderGripPosition()
        {
#if SOULPLAYER_PLACEMENT_TOOLS
            return DevelopmentSoulRecorderPresentationTuner.NativeRecorderGripPosition(
                SoulRecorderPresentationTuning.NativeRecorderGripPosition);
#else
            return SoulRecorderPresentationTuning.NativeRecorderGripPosition;
#endif
        }

        private static Vector3 ResolveNativeRecorderGripRotation()
        {
#if SOULPLAYER_PLACEMENT_TOOLS
            return DevelopmentSoulRecorderPresentationTuner.NativeRecorderGripRotation(
                SoulRecorderPresentationTuning.NativeRecorderGripRotationEuler);
#else
            return SoulRecorderPresentationTuning.NativeRecorderGripRotationEuler;
#endif
        }

        private static Vector3 ResolveNativeCassetteGripPosition()
        {
#if SOULPLAYER_PLACEMENT_TOOLS
            return DevelopmentSoulRecorderPresentationTuner.NativeCassetteGripPosition(
                SoulRecorderPresentationTuning.NativeCassetteGripPosition);
#else
            return SoulRecorderPresentationTuning.NativeCassetteGripPosition;
#endif
        }

        private static Vector3 ResolveNativeCassetteGripRotation()
        {
#if SOULPLAYER_PLACEMENT_TOOLS
            return DevelopmentSoulRecorderPresentationTuner.NativeCassetteGripRotation(
                SoulRecorderPresentationTuning.NativeCassetteGripRotationEuler);
#else
            return SoulRecorderPresentationTuning.NativeCassetteGripRotationEuler;
#endif
        }

        private float PhaseProgress(float now)
        {
            return SoulRecorderPresentationState.Smooth(PhaseLinearProgress(now));
        }

        private float PhaseLinearProgress(float now)
        {
            float duration;
            switch (_phaseTimeline.Phase)
            {
                case SoulRecorderNativeHandsPhase.Entering:
                    duration = ProceduralSoulRecorderHandsView.EnterClipSeconds;
                    break;
                case SoulRecorderNativeHandsPhase.Inserting:
                    duration = ProceduralSoulRecorderHandsView.InsertClipSeconds;
                    break;
                case SoulRecorderNativeHandsPhase.StopEntering:
                    duration = ProceduralSoulRecorderHandsView.StopEnterClipSeconds;
                    break;
                case SoulRecorderNativeHandsPhase.Ejecting:
                    duration = ProceduralSoulRecorderHandsView.EjectClipSeconds;
                    break;
                case SoulRecorderNativeHandsPhase.Exiting:
                    duration = ProceduralSoulRecorderHandsView.ExitClipSeconds;
                    break;
                default:
                    return 1f;
            }
            return SoulRecorderPresentationState.Progress(
                now, _phaseTimeline.PhaseStartedAt, duration);
        }

        private void LogDiscoveryOnce()
        {
            if (_diagnosticLogged || _discovery == null || Plugin.Log == null)
            {
                return;
            }
            _diagnosticLogged = true;
            StringBuilder text = new StringBuilder();
            text.AppendLine("SoulRecorder native EFT hands resolved:");
            text.AppendLine("  player=" + _discovery.Player.name +
                ", pointOfView=" + _discovery.Player.PointOfView +
                ", emptyHands=" +
                (_discovery.Player.HandsController is Player.EmptyHandsController));
            text.AppendLine("  PlayerBody=" + _discovery.Body.name +
                ", SkeletonHands=" + _discovery.Skeleton.name);
            text.AppendLine("  renderer=" + _discovery.Renderer.name +
                ", mesh=" + (_discovery.Renderer.sharedMesh == null
                    ? "<null>"
                    : _discovery.Renderer.sharedMesh.name) +
                ", enabled=" + _discovery.Renderer.enabled +
                ", materials=" + _discovery.Renderer.sharedMaterials.Length);
            AppendArm(text, "left", _discovery.Roles.Left, _leftIk, _createdLeftIk);
            AppendArm(text, "right", _discovery.Roles.Right, _rightIk, _createdRightIk);
            text.AppendLine("  native materials/shaders are referenced in place and are not copied.");
            Plugin.Log.LogInfo(text.ToString());
        }

        private static void AppendArm(
            StringBuilder text,
            string label,
            SoulRecorderNativeArmBones bones,
            LimbIK ik,
            bool created)
        {
            text.AppendLine("  " + label + "=" + bones.Collarbone + " -> " +
                bones.UpperArm + " -> " + bones.Forearm + " -> " + bones.Wrist +
                " -> " + bones.Palm + ", fingers=" +
                (bones.Fingers == null ? 0 : bones.Fingers.Count) +
                ", LimbIK=" + (ik == null ? "missing" : ik.name) +
                ", source=" + (created ? "SoulPlayer temporary" : "EFT existing"));
        }

        private static GameObject CreateCameraTarget(string name, Transform camera)
        {
            GameObject result = new GameObject(name);
            result.transform.SetParent(camera, false);
            return result;
        }

        private static GameObject CreateGripSocket(string name, Transform palm)
        {
            GameObject result = new GameObject(name);
            result.transform.SetParent(palm, false);
            result.transform.localScale = Vector3.one;
            return result;
        }

        private static void SetLocalPose(
            Transform transform,
            Vector3 position,
            Quaternion rotation)
        {
            transform.localPosition = position;
            transform.localRotation = rotation;
        }

        private static void DestroyObject(GameObject value)
        {
            if (value != null)
            {
                UnityEngine.Object.Destroy(value);
            }
        }
    }
}
