using System;
using System.Linq;
using System.Reflection;
using EFT;
using EFT.InventoryLogic;
using SoulPlayer.Recorder.Assets;
using UnityEngine;

namespace SoulPlayer.Recorder
{
    /// <summary>
    /// Public controller type required by EFT's generic item-hands factory.
    /// The installed rangefinder prefab supplies the native draw/idle/holster
    /// operations; SoulPlayer only specializes controller identity and mounts
    /// its own recorder presentation after EFT initializes the controller.
    /// </summary>
    public sealed class SoulRecorderNativeHandsController : Player.UsableItemController
    {
    }

    /// <summary>
    /// Presentation bridge for EFT's native usable-item controller. SoulPlayer
    /// creates a public recorder controller through ItemHandsController's native
    /// generic factory, then mounts its
    /// recorder sidecar when EFT initializes that native controller.
    ///
    /// The transient donor item supplies only a compatible native hands prefab at
    /// runtime. SoulPlayer never packages or redistributes that prefab or any EFT
    /// asset; the visible held tool is instantiated from SoulPlayer's sidecar.
    /// </summary>
    internal static class SoulRecorderNativePresentation
    {
        internal const string NativeHandsDonorTemplateId =
            "61605e13ffa6e502ac5e7eef"; // Vortex Ranger 1500

        private static Item _armedItem;
        private static Player.UsableItemController _activeController;
        private static SoulRecorderPackagedVisual _visual;
        private static SoulRecorderNativeVisualDriver _visualDriver;

        internal static void Arm(Item transientItem)
        {
            if (transientItem == null)
            {
                throw new ArgumentNullException(nameof(transientItem));
            }

            Release("arming native presentation");
            _armedItem = transientItem;
        }

        internal static void TryAttach(
            Player.UsableItemController controller,
            Player player,
            WeaponPrefab weaponPrefab)
        {
            if (controller == null || player == null || _armedItem == null ||
                controller.Item == null ||
                !ReferenceEquals(controller.Item, _armedItem))
            {
                return;
            }

            if (ReferenceEquals(_activeController, controller) &&
                _visualDriver != null)
            {
                return;
            }

            _activeController = controller;
            try
            {
                MountSoulRecorderVisual(controller, weaponPrefab);
            }
            catch (Exception ex)
            {
                DestroyVisual();
                Plugin.Log.LogError(
                    "SoulRecorder NATIVE visual setup failed; controller/audio will continue " +
                    "with BSG arms only: " + ex);
            }
            if (_visualDriver != null)
            {
                Plugin.Log.LogInfo(
                    "SoulRecorder NATIVE presentation attached to EFT's stock " +
                    "usable-item controller (type=" + controller.GetType().FullName +
                    ", donor template=" +
                    NativeHandsDonorTemplateId + ").");
            }
        }

        /// <summary>
        /// The native factory can finish controller spawn without dispatching the
        /// base InitializeController postfix on every SPT build. Before gameplay
        /// continues, resolve the live WeaponPrefab from the controller/player
        /// and attach the SoulRecorder sidecar explicitly. This also guarantees
        /// that cassette events cannot run ahead of visual initialization.
        /// </summary>
        internal static bool EnsureAttached(
            Player.AbstractHandsController controller,
            Player player)
        {
            Player.UsableItemController usableController =
                controller as Player.UsableItemController;
            if (usableController == null || player == null || _armedItem == null ||
                usableController.Item == null ||
                !ReferenceEquals(usableController.Item, _armedItem))
            {
                return false;
            }

            if (ReferenceEquals(_activeController, usableController) &&
                _visualDriver != null)
            {
                return true;
            }

            WeaponPrefab weaponPrefab = ResolveWeaponPrefab(usableController, player);
            if (weaponPrefab == null)
            {
                Plugin.Log.LogInfo(
                    "SoulRecorder NATIVE stock WeaponPrefab was not exposed; using the " +
                    "controller-owned native mount fallback.");
            }

            TryAttach(usableController, player, weaponPrefab);
            return ReferenceEquals(_activeController, usableController) &&
                _visualDriver != null;
        }

        internal static bool MatchesActiveHands(
            Player.AbstractHandsController controller,
            Item transientItem)
        {
            return controller != null && transientItem != null &&
                ReferenceEquals(controller.Item, transientItem);
        }

        internal static void Release(string reason)
        {
            bool hadPresentation = _activeController != null || _visual != null ||
                _visualDriver != null;
            DestroyVisual();
            _activeController = null;
            _armedItem = null;
            if (hadPresentation)
            {
                Plugin.Log.LogInfo(
                    "SoulRecorder NATIVE presentation released (" + reason + ").");
            }
        }

        internal static void PresentInteractionEntered()
        {
            if (_visualDriver != null)
            {
                _visualDriver.EnterInteraction();
            }
        }

        internal static void PresentTapeInsertionStarted()
        {
            if (_visualDriver != null)
            {
                _visualDriver.StartInsertion();
            }
        }

        internal static void PresentTapeInserted()
        {
            if (_visualDriver != null)
            {
                _visualDriver.SeatCassette();
            }
        }

        internal static void PresentPlaybackChanged(bool isPlaying)
        {
            if (_visualDriver != null)
            {
                _visualDriver.SetPlayback(isPlaying);
            }
        }

        internal static void PresentTapeEjectionStarted()
        {
            if (_visualDriver != null)
            {
                _visualDriver.StartEjection();
            }
        }

        internal static void PresentTapeEjected()
        {
            if (_visualDriver != null)
            {
                _visualDriver.HideCassette();
            }
        }

        internal static void PresentInteractionExited()
        {
            if (_visualDriver != null)
            {
                _visualDriver.ExitInteraction();
            }
        }

        internal static void ResetPresentation()
        {
            if (_visualDriver != null)
            {
                _visualDriver.ResetPresentation();
            }
        }

        internal static void ManualPresentationUpdate(
            float unscaledTime,
            float unscaledDeltaTime)
        {
            if (_visualDriver != null)
            {
                _visualDriver.Tick(unscaledTime, unscaledDeltaTime);
            }
        }

        private static void MountSoulRecorderVisual(
            Player.UsableItemController controller,
            WeaponPrefab weaponPrefab)
        {
            if (controller == null || Plugin.RecorderAssets == null ||
                !Plugin.RecorderAssets.TryCreateRecorderVisual(out _visual, false))
            {
                throw new InvalidOperationException(
                    "SoulRecorder sidecar visual was unavailable for the native hands controller");
            }

            GameObject controllerObject = controller.ControllerGameObject;
            Renderer[] donorRenderers = ResolveDonorRenderers(
                weaponPrefab,
                controllerObject);
            Renderer donorToolRenderer = SelectDonorToolRenderer(donorRenderers);
            Transform recorder = _visual.Root.transform;
            Transform mount = donorToolRenderer == null
                ? ResolveNativeMount(controller, weaponPrefab, controllerObject)
                : donorToolRenderer.transform;
            if (mount == null)
            {
                throw new InvalidOperationException(
                    "native usable-item controller exposed no safe recorder mount transform");
            }
            recorder.SetParent(mount, false);
            recorder.localPosition = SoulRecorderPresentationTuning.NativeRecorderModelPosition;
            recorder.localRotation = Quaternion.Euler(
                SoulRecorderPresentationTuning.NativeRecorderModelRotationEuler);
            recorder.localScale = ResolveNativeRecorderModelScale();

            try
            {
                SoulRecorderNativeVisualDriver driver =
                    new SoulRecorderNativeVisualDriver();
                driver.Initialize(_visual);
                _visualDriver = driver;
            }
            catch (Exception ex)
            {
                _visualDriver = null;
                Plugin.Log.LogWarning(
                    "SoulRecorder NATIVE cassette visual disabled; recorder, " +
                    "controller, and audio remain active: " + ex);
            }

            // Preserve the native skinned first-person hands. Only hide the
            // donor tool after the recorder itself has mounted successfully.
            foreach (Renderer renderer in donorRenderers.Where(IsDonorToolRenderer))
            {
                renderer.enabled = false;
            }

            Plugin.Log.LogInfo(
                "SoulRecorder NATIVE held visual mounted at " + mount.name +
                " with recorder scale " + FormatVector(recorder.localScale) +
                "; native skinned hands retained and donor tool geometry hidden.");
        }

        private static Renderer[] ResolveDonorRenderers(
            WeaponPrefab weaponPrefab,
            GameObject controllerObject)
        {
            if (weaponPrefab != null)
            {
                return weaponPrefab.Renderers ??
                    weaponPrefab.GetComponentsInChildren<Renderer>(true);
            }

            // ControllerGameObject is scoped to the transient usable item. Never
            // enumerate renderers from Player.WeaponRoot as that hierarchy may
            // contain native arms or unrelated player presentation objects.
            return controllerObject == null
                ? Array.Empty<Renderer>()
                : controllerObject.GetComponentsInChildren<Renderer>(true);
        }

        private static Transform ResolveNativeMount(
            Player.UsableItemController controller,
            WeaponPrefab weaponPrefab,
            GameObject controllerObject)
        {
            if (weaponPrefab != null)
            {
                return weaponPrefab.transform;
            }
            if (controllerObject != null)
            {
                return controllerObject.transform;
            }
            return controller.WeaponRoot;
        }

        private static Vector3 ResolveNativeRecorderModelScale()
        {
#if SOULPLAYER_PLACEMENT_TOOLS
            return DevelopmentSoulRecorderPresentationTuner.NativeRecorderModelScale(
                SoulRecorderPresentationTuning.NativeRecorderModelScale);
#else
            return SoulRecorderPresentationTuning.NativeRecorderModelScale;
#endif
        }

        private static WeaponPrefab ResolveWeaponPrefab(
            Player.UsableItemController controller,
            Player player)
        {
            WeaponPrefab controllerObjectPrefab =
                FindWeaponPrefabNear(controller.ControllerGameObject == null
                    ? null
                    : controller.ControllerGameObject.transform);
            if (controllerObjectPrefab != null)
            {
                return controllerObjectPrefab;
            }

            WeaponPrefab weaponRootPrefab = FindWeaponPrefabNear(controller.WeaponRoot);
            if (weaponRootPrefab != null)
            {
                return weaponRootPrefab;
            }

            WeaponPrefab reflected = FindWeaponPrefabMember(controller);
            if (reflected != null)
            {
                return reflected;
            }

            try
            {
                WeaponPrefab[] candidates =
                    player.GetComponentsInChildren<WeaponPrefab>(true);
                if (candidates == null || candidates.Length == 0)
                {
                    return null;
                }

                return candidates.FirstOrDefault(candidate =>
                           candidate != null && candidate.gameObject.activeInHierarchy) ??
                       candidates.FirstOrDefault(candidate => candidate != null);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning(
                    "SoulRecorder NATIVE WeaponPrefab fallback search failed: " +
                    ex.Message);
                return null;
            }
        }

        private static WeaponPrefab FindWeaponPrefabNear(Transform origin)
        {
            if (origin == null)
            {
                return null;
            }

            WeaponPrefab direct = origin.GetComponent<WeaponPrefab>();
            if (direct != null)
            {
                return direct;
            }

            WeaponPrefab child = origin.GetComponentInChildren<WeaponPrefab>(true);
            if (child != null)
            {
                return child;
            }

            return origin.GetComponentInParent<WeaponPrefab>();
        }

        private static WeaponPrefab FindWeaponPrefabMember(object instance)
        {
            if (instance == null)
            {
                return null;
            }

            const BindingFlags flags = BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly;
            for (Type type = instance.GetType(); type != null; type = type.BaseType)
            {
                foreach (FieldInfo field in type.GetFields(flags))
                {
                    if (!typeof(WeaponPrefab).IsAssignableFrom(field.FieldType))
                    {
                        continue;
                    }
                    try
                    {
                        WeaponPrefab value = field.GetValue(instance) as WeaponPrefab;
                        if (value != null)
                        {
                            return value;
                        }
                    }
                    catch
                    {
                        // Continue through obfuscated/inaccessible candidates.
                    }
                }

                foreach (PropertyInfo property in type.GetProperties(flags))
                {
                    if (!property.CanRead || property.GetIndexParameters().Length != 0 ||
                        !typeof(WeaponPrefab).IsAssignableFrom(property.PropertyType))
                    {
                        continue;
                    }
                    try
                    {
                        WeaponPrefab value = property.GetValue(instance, null) as WeaponPrefab;
                        if (value != null)
                        {
                            return value;
                        }
                    }
                    catch
                    {
                        // Continue through obfuscated/inaccessible candidates.
                    }
                }
            }

            return null;
        }


        private static string FormatVector(Vector3 value)
        {
            return string.Format("({0:F3}, {1:F3}, {2:F3})", value.x, value.y, value.z);
        }

        private static Renderer SelectDonorToolRenderer(Renderer[] renderers)
        {
            if (renderers == null)
            {
                return null;
            }

            return renderers
                .Where(IsDonorToolRenderer)
                .OrderByDescending(renderer => renderer.bounds.size.sqrMagnitude)
                .FirstOrDefault();
        }

        private static bool IsDonorToolRenderer(Renderer renderer)
        {
            return renderer != null && !(renderer is SkinnedMeshRenderer);
        }

        private static void DestroyVisual()
        {
            if (_visual != null)
            {
                if (_visual.Root != null)
                {
                    UnityEngine.Object.Destroy(_visual.Root);
                }
                if (_visual.CassetteRoot != null &&
                    (_visual.Root == null ||
                     !_visual.CassetteRoot.transform.IsChildOf(_visual.Root.transform)))
                {
                    UnityEngine.Object.Destroy(_visual.CassetteRoot);
                }
            }
            _visual = null;
            _visualDriver = null;
        }
    }

    /// <summary>
    /// Visual-only state driver owned by the native controller. It never changes
    /// EFT arm bones, IK, input, or controller state and does not require Unity to
    /// add a MonoBehaviour while the BSG controller is spawning.
    /// </summary>
    internal sealed class SoulRecorderNativeVisualDriver
    {
        private struct WorldPose
        {
            internal Vector3 Position;
            internal Quaternion Rotation;
        }

        private readonly SoulRecorderPresentationState _state =
            new SoulRecorderPresentationState();
        private SoulRecorderPackagedVisual _visual;
        private Transform _cassetteParent;

        internal void Initialize(SoulRecorderPackagedVisual visual)
        {
            _visual = visual;
            _cassetteParent = visual == null || visual.CassetteSlot == null
                ? null
                : visual.CassetteSlot.parent;
            if (_visual != null && _visual.CassetteRoot != null)
            {
                if (_cassetteParent != null)
                {
                    _visual.CassetteRoot.transform.SetParent(_cassetteParent, false);
                }
                _visual.CassetteRoot.transform.localScale =
                    SoulRecorderPresentationTuning.NativeCassetteModelScale;
                _visual.CassetteRoot.SetActive(false);
            }
            _state.Reset();
        }

        internal void EnterInteraction()
        {
            _state.Enter(Time.unscaledTime);
        }

        internal void StartInsertion()
        {
            _state.StartInsertion(Time.unscaledTime);
            ApplyCassette(Time.unscaledTime);
        }

        internal void SeatCassette()
        {
            _state.SeatCassette();
            ApplyCassette(Time.unscaledTime);
        }

        internal void SetPlayback(bool isPlaying)
        {
            _state.SetPlayback(isPlaying, Time.unscaledTime);
        }

        internal void StartEjection()
        {
            _state.StartEjection(Time.unscaledTime);
            ApplyCassette(Time.unscaledTime);
        }

        internal void HideCassette()
        {
            _state.HideCassette();
            ApplyCassette(Time.unscaledTime);
        }

        internal void ExitInteraction()
        {
            _state.Exit(Time.unscaledTime);
        }

        internal void ResetPresentation()
        {
            _state.Reset();
            ApplyCassette(Time.unscaledTime);
        }

        internal void Tick(float unscaledTime, float unscaledDeltaTime)
        {
            _state.Advance(unscaledTime);
            ApplyCassette(unscaledTime);
            AnimateReels(unscaledDeltaTime);
        }

        private void ApplyCassette(float now)
        {
            if (_visual == null || _visual.CassetteRoot == null ||
                _cassetteParent == null)
            {
                return;
            }

            switch (_state.CassetteState)
            {
                case SoulRecorderCassetteVisualState.Hidden:
                    _visual.CassetteRoot.SetActive(false);
                    return;

                case SoulRecorderCassetteVisualState.Inserting:
                    _visual.CassetteRoot.SetActive(true);
                    ApplyCassetteTravel(
                        SoulRecorderPresentationState.Smooth(
                            _state.GetInsertionProgress(now)),
                        false,
                        _state.GetInsertionSettleAmount(now));
                    return;

                case SoulRecorderCassetteVisualState.Seated:
                    _visual.CassetteRoot.SetActive(true);
                    ApplyCassetteTravel(1f, false, 0f);
                    return;

                case SoulRecorderCassetteVisualState.Ejecting:
                    _visual.CassetteRoot.SetActive(true);
                    ApplyCassetteTravel(
                        _state.GetEjectionCassetteTravel(now), true, 0f);
                    return;
            }
        }

        private void ApplyCassetteTravel(
            float progress,
            bool ejecting,
            float settleAmount)
        {
            WorldPose exterior = ResolveExteriorPose(ejecting);
            WorldPose alignment = MarkerPose(
                _visual.CassetteAlignmentPosition,
                _visual.CassetteAlignmentRotation);
            WorldPose inserted = MarkerPose(
                _visual.CassetteEndPosition,
                _visual.CassetteEndRotation);
            float split = SoulRecorderPresentationTuning.CassetteAlignmentProgress;
            Vector3 position;
            Quaternion rotation;
            if (progress <= split)
            {
                float amount = split <= 0f ? 1f : progress / split;
                position = Vector3.Lerp(exterior.Position, alignment.Position, amount);
                rotation = Quaternion.Slerp(exterior.Rotation, alignment.Rotation, amount);
            }
            else
            {
                float amount = (progress - split) / (1f - split);
                position = Vector3.Lerp(alignment.Position, inserted.Position, amount);
                rotation = Quaternion.Slerp(alignment.Rotation, inserted.Rotation, amount);
            }

            Transform cassette = _visual.CassetteRoot.transform;
            cassette.position = position;
            cassette.rotation = rotation;
            cassette.localScale = SoulRecorderPresentationTuning.NativeCassetteModelScale;
            if (settleAmount > 0f)
            {
                float settleMeters =
                    SoulRecorderPresentationTuning.CassetteContactSettleMillimeters * 0.001f;
                cassette.position += inserted.Rotation *
                    new Vector3(0f, -settleMeters * 0.35f, settleMeters) * settleAmount;
            }
        }

        private WorldPose ResolveExteriorPose(bool ejecting)
        {
            return MarkerPose(
                ejecting
                    ? _visual.CassetteEjectPosition
                    : _visual.CassetteStartPosition,
                ejecting
                    ? _visual.CassetteEjectRotation
                    : _visual.CassetteStartRotation);
        }

        private WorldPose MarkerPose(Vector3 localPosition, Quaternion localRotation)
        {
            return new WorldPose
            {
                Position = _cassetteParent.TransformPoint(localPosition),
                Rotation = _cassetteParent.rotation * localRotation
            };
        }

        private void AnimateReels(float unscaledDeltaTime)
        {
            if (!_state.IsPlaying || _visual == null)
            {
                return;
            }
            float degrees = SoulRecorderPresentationTuning.ReelDegreesPerSecond *
                unscaledDeltaTime;
            if (_visual.ReelLeft != null)
            {
                _visual.ReelLeft.Rotate(Vector3.up, degrees, Space.Self);
            }
            if (_visual.ReelRight != null)
            {
                _visual.ReelRight.Rotate(Vector3.up, -degrees, Space.Self);
            }
        }
    }
}
