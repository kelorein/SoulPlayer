#if SOULPLAYER_PLACEMENT_TOOLS
using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using SoulPlayer.Cassettes;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SoulPlayer.World
{
    /// <summary>
    /// Development-only curated-anchor authoring. F9 always captures from the
    /// current authoring view. Hybrid noclip moves the real EFT player only
    /// across X/Z while a SoulPlayer camera supplies a bounded vertical offset.
    /// </summary>
    internal sealed class DevelopmentSoulTapeSpawnMarker : MonoBehaviour
    {
        private const float MaximumRayDistance = 100f;
        private const float MinimumWorldHitDistance = 0.5f;
        private const float MaximumTeleportStep = 0.75f;
        private const float MovementTargetTolerance = 0.001f;
        private const float MaximumVerticalCameraOffset = 25f;
        private const float MaximumManualOffset = 0.2f;
        private const float FeedbackDurationSeconds = 2f;
        private const float DefaultFlySpeed = 4f;
        private const float DefaultSpeedBoost = 3f;

        private readonly SoulTapePlacementSolver _solver = new SoulTapePlacementSolver();
        private readonly List<ColliderState> _colliders = new List<ColliderState>();

        private ConfigEntry<KeyboardShortcut> _toggleNoclipHotkey;
        private ConfigEntry<KeyboardShortcut> _saveHotkey;
        private ConfigEntry<string> _semanticTag;
        private ConfigEntry<float> _rotationDegrees;
        private ConfigEntry<float> _surfaceOffset;
        private ConfigEntry<float> _flySpeed;
        private ConfigEntry<float> _speedBoost;
        private SoulTapeSpawnAnchorStore _store;
        private Player _authoringPlayer;
        private MovementContext _movementContext;
        private ActiveHealthController _healthController;
        private Camera _authoringCamera;
        private GameObject _authoringCameraRoot;
        private GameObject _ghost;
        private Material _ghostMaterial;
        private SoulTapePlacementValidationResult _preview;
        private SoulTapeSpawnAnchorDocument _authoringDocument =
            new SoulTapeSpawnAnchorDocument();
        private string _currentMapId = string.Empty;
        private string _authoringDataError = string.Empty;
        private string _feedbackTitle = string.Empty;
        private string _feedbackDetail = string.Empty;
        private Color _feedbackColor = Color.white;
        private float _feedbackUntil;
        private float _previewUntil;
        private int _savedAnchorCount;
        private GUIStyle _summaryStyle;
        private GUIStyle _statusStyle;
        private GUIStyle _feedbackStyle;
        private GUIStyle _noclipStyle;
        private bool _ghostCreationFailed;
        private bool _noclipEnabled;
        private bool _originalIgnoreDeltaMovement;
        private float _originalDamageCoefficient;
        private float _originalFallSafeHeight;
        private Vector3 _safeEntryPosition;
        private Vector3 _movementTarget;
        private bool _movementTargetDirty;
        private float _verticalCameraOffset;
        private string _noclipFeedback = string.Empty;
        private float _noclipFeedbackUntil;

        internal void Initialize(ConfigFile config)
        {
            _toggleNoclipHotkey = config.Bind(
                "Development",
                "Authoring noclip toggle",
                new KeyboardShortcut(
                    KeyCode.F8,
                    KeyCode.LeftControl,
                    KeyCode.LeftShift),
                "Development build only. Toggles hybrid player/camera authoring noclip.");
            _saveHotkey = config.Bind(
                "Development",
                "Save solved cassette anchor",
                new KeyboardShortcut(KeyCode.F9),
                "Development build only. Raycasts from the gameplay crosshair, " +
                "solves the cassette pose, and immediately saves a valid curated anchor.");
            _semanticTag = config.Bind(
                "Development",
                "Cassette anchor semantic tag",
                string.Empty,
                "Optional tag assigned to newly saved anchors, such as office-desk or industrial-shelf.");
            _rotationDegrees = config.Bind(
                "Development",
                "Cassette anchor rotation degrees",
                0f,
                new ConfigDescription(
                    "Optional rotation around the solved surface normal used by the next F9 capture.",
                    new AcceptableValueRange<float>(0f, 360f)));
            _surfaceOffset = config.Bind(
                "Development",
                "Cassette anchor surface offset",
                0f,
                new ConfigDescription(
                    "Optional extra outward surface offset in metres used by the next F9 capture.",
                    new AcceptableValueRange<float>(0f, MaximumManualOffset)));
            _flySpeed = config.Bind(
                "Development",
                "Authoring noclip speed",
                DefaultFlySpeed,
                new ConfigDescription(
                    "Base horizontal-player and vertical-camera authoring speed in metres per second.",
                    new AcceptableValueRange<float>(0.5f, 25f)));
            _speedBoost = config.Bind(
                "Development",
                "Authoring noclip speed boost",
                DefaultSpeedBoost,
                new ConfigDescription(
                    "Shift multiplier for hybrid SoulPlayer authoring movement.",
                    new AcceptableValueRange<float>(1f, 10f)));

            string outputPath = Path.Combine(
                BepInEx.Paths.ConfigPath,
                "SoulPlayer",
                "authoring",
                "spawn-anchors.json");
            _store = new SoulTapeSpawnAnchorStore(outputPath);
            Plugin.Log.LogWarning(
                "SoulTape one-shot anchor authoring is active (development build only): " +
                "F9 saves from the authoring view; Ctrl+Shift+F8 toggles hybrid noclip.");
            Plugin.Log.LogInfo("Curated anchor output: " + _store.PathDescription);
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        private void Update()
        {
            if (_toggleNoclipHotkey != null &&
                ShortcutPressed(_toggleNoclipHotkey.Value))
            {
                if (_noclipEnabled)
                {
                    DisableNoclip("author toggle", true);
                }
                else
                {
                    TryEnableNoclip();
                }
            }

            if (_noclipEnabled)
            {
                if (!IsAuthoringPlayerUsable())
                {
                    DisableNoclip("raid player unavailable", false);
                }
                else
                {
                    UpdateMovementTarget();
                }
            }

            if (_saveHotkey != null && ShortcutPressed(_saveHotkey.Value))
            {
                CaptureAndSaveAnchor();
            }

            if (_ghost != null && Time.unscaledTime >= _previewUntil)
            {
                SetGhostVisible(false);
            }
        }

        private void LateUpdate()
        {
            if (_noclipEnabled)
            {
                SyncAuthoringCamera();
            }
        }

        private void FixedUpdate()
        {
            if (!_noclipEnabled || !_movementTargetDirty ||
                !IsAuthoringPlayerUsable())
            {
                return;
            }

            try
            {
                Vector3 current = _authoringPlayer.Position;
                Vector3 next = ToUnity(SoulTapeAuthoringMovementMath.MoveTowardsHorizontal(
                    ToData(current),
                    ToData(_movementTarget),
                    MaximumTeleportStep));
                _authoringPlayer.Teleport(next, false);
                Vector2 remaining = new Vector2(
                    _movementTarget.x - next.x,
                    _movementTarget.z - next.z);
                _movementTargetDirty = remaining.sqrMagnitude >
                    MovementTargetTolerance * MovementTargetTolerance;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError(
                    "SoulPlayer Noclip player reposition failed: " + ex);
                DisableNoclip("player reposition failed", false);
            }
        }

        private void TryEnableNoclip()
        {
            GameWorld world = Singleton<GameWorld>.Instance;
            Player player = world == null ? null : world.MainPlayer;
            Camera gameplayCamera;
            Ray ignored;
            if (player == null || !player.gameObject.activeInHierarchy ||
                player.HealthController == null || !player.HealthController.IsAlive)
            {
                ShowNoclipFeedback(false,
                    "SoulPlayer Noclip requires a living local raid player.");
                return;
            }
            if (player.ActiveHealthController == null)
            {
                ShowNoclipFeedback(false,
                    "SoulPlayer Noclip could not capture player protection state.");
                return;
            }
            if (player.MovementContext == null)
            {
                ShowNoclipFeedback(false,
                    "SoulPlayer Noclip could not capture player movement state.");
                return;
            }
            if (!TryGetGameplayScreenCenterRay(out gameplayCamera, out ignored))
            {
                ShowNoclipFeedback(false,
                    "SoulPlayer Noclip could not resolve the gameplay camera.");
                return;
            }

            try
            {
                _authoringPlayer = player;
                _safeEntryPosition = player.Position;
                _movementTarget = _safeEntryPosition;
                _movementTargetDirty = false;
                _verticalCameraOffset = 0f;
                _movementContext = player.MovementContext;
                _originalIgnoreDeltaMovement = _movementContext.IgnoreDeltaMovement;
                _movementContext.IgnoreDeltaMovement = true;
                _healthController = player.ActiveHealthController;
                _originalDamageCoefficient = _healthController.DamageCoeff;
                _originalFallSafeHeight = _healthController.FallSafeHeight;
                CaptureAndDisableMovementColliders(player);
                ApplyAuthorProtection();
                CreateAuthoringCamera(gameplayCamera);
                _authoringPlayer.OnIPlayerDeadOrUnspawn +=
                    OnAuthoringPlayerDeadOrUnspawn;
                _noclipEnabled = true;
                ShowNoclipFeedback(true, "SoulPlayer Noclip: ON");
                Plugin.Log.LogInfo(
                    "SoulPlayer Hybrid Noclip controls: WASD moves the player " +
                    "horizontally, Space/Ctrl adjusts camera height, Shift boosts, " +
                    "normal EFT mouse look, F9 save, Ctrl+Shift+F8 exit. Horizontal " +
                    "player reposition runs in FixedUpdate at " +
                    GetFixedUpdateRateText() + " with a maximum " +
                    MaximumTeleportStep.ToString("0.00") + " metre step; camera " +
                    "height is clamped to +/-" +
                    MaximumVerticalCameraOffset.ToString("0") + " metres.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("SoulPlayer Noclip could not start: " + ex);
                DisableNoclip("enable failed", false);
            }
        }

        private void DisableNoclip(string reason, bool returnToEntryPosition)
        {
            bool wasEnabled = _noclipEnabled || _authoringPlayer != null ||
                              _movementContext != null || _colliders.Count > 0 ||
                              _authoringCameraRoot != null;
            _noclipEnabled = false;

            if (_authoringPlayer != null)
            {
                try
                {
                    _authoringPlayer.OnIPlayerDeadOrUnspawn -=
                        OnAuthoringPlayerDeadOrUnspawn;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning(
                        "SoulPlayer Noclip could not detach its player cleanup hook: " +
                        ex.Message);
                }
            }

            if (returnToEntryPosition && _authoringPlayer != null)
            {
                try
                {
                    _authoringPlayer.Teleport(_safeEntryPosition, false);
                    Plugin.Log.LogInfo(
                        "SoulPlayer Noclip returned the author to the safe entry " +
                        "position before restoring collision.");
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning(
                        "SoulPlayer Noclip could not return to its safe entry " +
                        "position: " + ex.Message);
                }
            }

            RestoreAuthorProtection();
            RestoreMovementState();
            RestoreMovementColliders();
            DestroyAuthoringCamera();
            _authoringPlayer = null;
            _movementContext = null;
            _healthController = null;
            _movementTargetDirty = false;
            _verticalCameraOffset = 0f;

            if (wasEnabled)
            {
                ShowNoclipFeedback(true, "SoulPlayer Noclip: OFF");
                Plugin.Log.LogInfo(
                    "SoulPlayer Noclip: OFF (" + reason + "). Player movement, " +
                    "collision, and author protection were restored.");
            }
        }

        private void UpdateMovementTarget()
        {
            Camera gameplayCamera;
            Ray ignored;
            if (!TryGetGameplayScreenCenterRay(out gameplayCamera, out ignored))
            {
                return;
            }

            Vector3 forward = gameplayCamera.transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.0001f
                ? forward.normalized
                : Vector3.forward;
            Vector3 right = gameplayCamera.transform.right;
            right.y = 0f;
            right = right.sqrMagnitude > 0.0001f
                ? right.normalized
                : Vector3.right;

            Vector3 horizontalDirection = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) horizontalDirection += forward;
            if (Input.GetKey(KeyCode.S)) horizontalDirection -= forward;
            if (Input.GetKey(KeyCode.D)) horizontalDirection += right;
            if (Input.GetKey(KeyCode.A)) horizontalDirection -= right;
            float verticalDirection = 0f;
            if (Input.GetKey(KeyCode.Space)) verticalDirection += 1f;
            if (ControlHeld()) verticalDirection -= 1f;

            float speed = _flySpeed == null ? DefaultFlySpeed : _flySpeed.Value;
            if (ShiftHeld())
            {
                speed *= _speedBoost == null
                    ? DefaultSpeedBoost
                    : _speedBoost.Value;
            }
            if (horizontalDirection.sqrMagnitude > 0.0001f)
            {
                Vector3 horizontalStep = horizontalDirection.normalized * speed *
                    Time.unscaledDeltaTime;
                _movementTarget.x += horizontalStep.x;
                _movementTarget.z += horizontalStep.z;
                _movementTargetDirty = true;
            }
            if (Mathf.Abs(verticalDirection) > 0.001f)
            {
                _verticalCameraOffset =
                    SoulTapeAuthoringMovementMath.ClampVerticalCameraOffset(
                        _verticalCameraOffset + verticalDirection * speed *
                        Time.unscaledDeltaTime,
                        MaximumVerticalCameraOffset);
            }

            SyncAuthoringCamera(gameplayCamera);
        }

        private void CreateAuthoringCamera(Camera gameplayCamera)
        {
            DestroyAuthoringCamera();
            _authoringCameraRoot = new GameObject(
                "SoulPlayer Hybrid Authoring Camera Root");
            _authoringCameraRoot.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(_authoringCameraRoot);
            _authoringCamera = _authoringCameraRoot.AddComponent<Camera>();
            _authoringCamera.name = "SoulPlayer Hybrid Authoring Camera";
            SyncAuthoringCamera(gameplayCamera);
        }

        private bool SyncAuthoringCamera()
        {
            Camera gameplayCamera;
            Ray ignored;
            return TryGetGameplayScreenCenterRay(out gameplayCamera, out ignored) &&
                   SyncAuthoringCamera(gameplayCamera);
        }

        private bool SyncAuthoringCamera(Camera gameplayCamera)
        {
            if (_authoringCamera == null || gameplayCamera == null ||
                !gameplayCamera.enabled ||
                !gameplayCamera.gameObject.activeInHierarchy)
            {
                return false;
            }

            _authoringCamera.CopyFrom(gameplayCamera);
            _authoringCamera.targetTexture = null;
            _authoringCamera.depth = gameplayCamera.depth + 100f;
            _authoringCamera.enabled = true;
            _authoringCamera.transform.SetPositionAndRotation(
                gameplayCamera.transform.position +
                    Vector3.up * _verticalCameraOffset,
                gameplayCamera.transform.rotation);
            return true;
        }

        private void DestroyAuthoringCamera()
        {
            if (_authoringCamera != null)
            {
                _authoringCamera.enabled = false;
            }
            if (_authoringCameraRoot != null)
            {
                Destroy(_authoringCameraRoot);
            }
            _authoringCamera = null;
            _authoringCameraRoot = null;
        }

        private bool IsAuthoringPlayerUsable()
        {
            GameWorld world = Singleton<GameWorld>.Instance;
            Player player = world == null ? null : world.MainPlayer;
            return player != null && player == _authoringPlayer &&
                   player.gameObject.activeInHierarchy &&
                   player.HealthController != null &&
                   player.HealthController.IsAlive;
        }

        private void ApplyAuthorProtection()
        {
            if (_healthController == null)
            {
                return;
            }
            _healthController.SetDamageCoeff(0f);
            _healthController.FallSafeHeight = float.MaxValue;
        }

        private void RestoreAuthorProtection()
        {
            if (_healthController == null)
            {
                return;
            }
            try
            {
                _healthController.SetDamageCoeff(_originalDamageCoefficient);
                _healthController.FallSafeHeight = _originalFallSafeHeight;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning(
                    "SoulPlayer Noclip could not restore health safety state: " +
                    ex.Message);
            }
        }

        private void RestoreMovementState()
        {
            if (_movementContext == null)
            {
                return;
            }
            try
            {
                _movementContext.IgnoreDeltaMovement = _originalIgnoreDeltaMovement;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning(
                    "SoulPlayer Noclip could not restore EFT movement state: " +
                    ex.Message);
            }
        }

        private void CaptureAndDisableMovementColliders(Player player)
        {
            _colliders.Clear();
            foreach (Collider collider in player.GetComponentsInChildren<Collider>(true))
            {
                if (collider == null || collider.isTrigger || !collider.enabled)
                {
                    continue;
                }

                _colliders.Add(new ColliderState(collider, collider.enabled));
                collider.enabled = false;
            }
        }

        private void RestoreMovementColliders()
        {
            foreach (ColliderState state in _colliders)
            {
                try
                {
                    if (state.Collider != null)
                    {
                        state.Collider.enabled = state.WasEnabled;
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning(
                        "SoulPlayer Noclip could not restore a player collider: " +
                        ex.Message);
                }
            }
            _colliders.Clear();
        }

        private static string GetFixedUpdateRateText()
        {
            return Time.fixedDeltaTime > 0f
                ? (1f / Time.fixedDeltaTime).ToString("0.#") + " Hz"
                : "the EFT physics rate";
        }

        private void ShowNoclipFeedback(bool info, string message)
        {
            _noclipFeedback = message ?? string.Empty;
            _noclipFeedbackUntil = Time.unscaledTime + FeedbackDurationSeconds;
            if (info)
            {
                Plugin.Log.LogInfo(_noclipFeedback);
            }
            else
            {
                Plugin.Log.LogWarning(_noclipFeedback);
            }
        }

        private void CaptureAndSaveAnchor()
        {
            _preview = null;
            SetGhostVisible(false);

            GameWorld world = Singleton<GameWorld>.Instance;
            Player player = world == null ? null : world.MainPlayer;
            if (player == null || !player.gameObject.activeInHierarchy)
            {
                RejectWithoutPreview(
                    "SoulTape anchor authoring requires an active local raid player.");
                return;
            }

            string mapId = world.LocationId ?? string.Empty;
            RefreshAuthoringData(mapId);

            Camera gameplayCamera;
            Ray screenCenterRay;
            if (!TryGetAuthoringScreenCenterRay(out gameplayCamera, out screenCenterRay))
            {
                RejectWithoutPreview(
                    "The active full-screen gameplay camera is unavailable.");
                return;
            }

            RaycastHit hit;
            if (!TryGetNearestValidWorldHit(
                    player,
                    gameplayCamera,
                    screenCenterRay,
                    out hit))
            {
                RejectWithoutPreview("No valid world surface is currently targeted.");
                return;
            }

            _preview = _solver.Solve(
                ToData(hit.point),
                ToData(hit.normal),
                ToData(screenCenterRay.direction),
                _rotationDegrees.Value,
                _surfaceOffset.Value,
                IsCassetteClipping);

            if (string.IsNullOrWhiteSpace(mapId))
            {
                RejectPreview("Canonical map ID is unavailable.");
            }
            else if (!string.IsNullOrWhiteSpace(_authoringDataError))
            {
                RejectPreview(
                    "Existing authoring data is unreadable and was left untouched: " +
                    _authoringDataError);
            }
            else if (_preview.IsValid && IsPreviewDuplicate(mapId))
            {
                RejectPreview(
                    "A curated anchor already exists within " +
                    SoulTapeSpawnAnchorStore.DuplicateDistanceMetres.ToString("0.00") +
                    " metres on this map.");
            }

            SavePreview(mapId);
            ShowSolvedGhost();
            Plugin.Log.LogInfo(
                "SoulTape F9 authoring ray used authoring camera " +
                gameplayCamera.name + " (instance " + gameplayCamera.GetInstanceID() + ").");
        }

        private bool TryGetNearestValidWorldHit(
            Player player,
            Camera rayCamera,
            Ray ray,
            out RaycastHit selectedHit)
        {
            Camera viewModelCamera = rayCamera;
            if (rayCamera == _authoringCamera)
            {
                Camera originalGameplayCamera;
                Ray ignored;
                if (TryGetGameplayScreenCenterRay(
                        out originalGameplayCamera,
                        out ignored))
                {
                    viewModelCamera = originalGameplayCamera;
                }
            }

            RaycastHit[] hits = Physics.RaycastAll(
                ray,
                MaximumRayDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Collide);
            var candidates = new List<SoulTapeAuthoringHitCandidate>(hits.Length);
            for (int index = 0; index < hits.Length; index++)
            {
                RaycastHit hit = hits[index];
                Collider collider = hit.collider;
                Transform hitTransform = collider == null ? null : collider.transform;
                candidates.Add(new SoulTapeAuthoringHitCandidate
                {
                    SourceIndex = index,
                    Distance = hit.distance,
                    IsLocalPlayer = IsInHierarchy(
                                        hitTransform,
                                        player.Transform == null
                                            ? null
                                            : player.Transform.Original) ||
                                    IsPlayerBodyHit(hitTransform, player),
                    IsHandsOrHeldItem = IsHandsOrHeldItemHit(
                        hitTransform,
                        player,
                        viewModelCamera),
                    IsSoulPlayerPreview = IsSoulPlayerAuthoringObject(hitTransform),
                    IsTrigger = collider == null || collider.isTrigger,
                    IsNearCamera = hit.distance < MinimumWorldHitDistance
                });
            }

            int selectedIndex;
            if (SoulTapeAuthoringHitSelector.TrySelectNearestWorldHit(
                    candidates,
                    out selectedIndex))
            {
                selectedHit = hits[selectedIndex];
                return true;
            }

            selectedHit = new RaycastHit();
            return false;
        }

        private static bool IsPlayerBodyHit(Transform hit, Player player)
        {
            return player != null && player.PlayerBody != null &&
                   IsInHierarchy(hit, player.PlayerBody.transform);
        }

        private static bool IsHandsOrHeldItemHit(
            Transform hit,
            Player player,
            Camera gameplayCamera)
        {
            if (hit == null)
            {
                return false;
            }
            if (gameplayCamera != null &&
                IsInHierarchy(hit, gameplayCamera.transform))
            {
                return true;
            }

            Player.AbstractHandsController hands =
                player == null ? null : player.HandsController;
            if (hands == null)
            {
                return false;
            }
            if (IsInHierarchy(hit, hands.transform) ||
                IsInHierarchy(hit, hands.WeaponRoot))
            {
                return true;
            }

            GameObject controllerObject = hands.ControllerGameObject;
            return controllerObject != null &&
                   IsInHierarchy(hit, controllerObject.transform);
        }

        private bool IsSoulPlayerAuthoringObject(Transform hit)
        {
            if (IsInHierarchy(hit, _ghost == null ? null : _ghost.transform))
            {
                return true;
            }

            for (Transform current = hit; current != null; current = current.parent)
            {
                string objectName = current.gameObject.name ?? string.Empty;
                if (objectName.StartsWith(
                        "SoulTape Curated Anchor Ghost",
                        StringComparison.Ordinal) ||
                    objectName.StartsWith(
                        "SoulTape Existing Anchor",
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsInHierarchy(Transform candidate, Transform root)
        {
            return candidate != null && root != null &&
                   (candidate == root || candidate.IsChildOf(root));
        }

        private bool TryGetAuthoringScreenCenterRay(out Camera camera, out Ray ray)
        {
            if (_noclipEnabled)
            {
                if (SyncAuthoringCamera() && _authoringCamera != null &&
                    _authoringCamera.enabled &&
                    _authoringCamera.gameObject.activeInHierarchy)
                {
                    camera = _authoringCamera;
                    ray = _authoringCamera.ViewportPointToRay(
                        new Vector3(0.5f, 0.5f, 0f));
                    return true;
                }

                camera = null;
                ray = new Ray();
                return false;
            }

            return TryGetGameplayScreenCenterRay(out camera, out ray);
        }

        private bool TryGetGameplayScreenCenterRay(
            out Camera camera,
            out Ray ray)
        {
            Camera main = Camera.main;
            if (IsVerifiedGameplayCamera(main, false))
            {
                camera = main;
                ray = main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                return true;
            }

            Camera fallback = null;
            foreach (Camera candidate in Camera.allCameras)
            {
                if (candidate == _authoringCamera ||
                    !IsVerifiedGameplayCamera(candidate, true))
                {
                    continue;
                }

                if (fallback == null || candidate.depth < fallback.depth)
                {
                    fallback = candidate;
                }
            }

            if (fallback != null)
            {
                camera = fallback;
                ray = fallback.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                return true;
            }

            camera = null;
            ray = new Ray();
            return false;
        }

        private static bool IsVerifiedGameplayCamera(
            Camera camera,
            bool requireWorldLayer)
        {
            if (camera == null || !camera.enabled ||
                !camera.gameObject.activeInHierarchy || camera.targetTexture != null ||
                camera.orthographic)
            {
                return false;
            }

            Rect rect = camera.rect;
            if (rect.x > 0.05f || rect.y > 0.05f ||
                rect.width < 0.9f || rect.height < 0.9f ||
                SoulTapeInteractionTargeting.LooksLikeAuxiliaryCameraName(camera.name))
            {
                return false;
            }

            if (!requireWorldLayer)
            {
                return true;
            }

            int defaultLayer = LayerMask.NameToLayer("Default");
            return defaultLayer >= 0 &&
                   (camera.cullingMask & (1 << defaultLayer)) != 0;
        }

        private void SavePreview(string mapId)
        {
            if (_preview == null || !_preview.IsValid)
            {
                string reason = _preview == null
                    ? "No surface is currently targeted."
                    : _preview.Reason;
                Plugin.Log.LogWarning("SoulTape anchor was not saved: " + reason);
                ShowSaveFeedback(false, reason);
                return;
            }

            Vector3 euler = ToUnity(_preview.Rotation).eulerAngles;
            SoulTapeSpawnAnchor anchor = new SoulTapeSpawnAnchor
            {
                Id = CreateMarkerId(mapId),
                MapId = mapId,
                SemanticTag = (_semanticTag.Value ?? string.Empty).Trim(),
                Position = Copy(_preview.Position),
                RotationEuler = ToData(euler),
                SurfaceNormal = Copy(_preview.SurfaceNormal),
                Source = SoulTapeSpawnAnchorSource.Curated,
                Enabled = true
            };

            string rejectionReason;
            try
            {
                if (!_store.TryAppend(anchor, out rejectionReason))
                {
                    RejectPreview(rejectionReason);
                    Plugin.Log.LogWarning("SoulTape anchor was not saved: " + rejectionReason);
                    ShowSaveFeedback(false, rejectionReason);
                    return;
                }

                RefreshAuthoringData(mapId);
                ShowSaveFeedback(true, mapId + " — Anchor #" + _savedAnchorCount);
                Plugin.Log.LogInfo(
                    "SoulTape solved curated anchor saved: " + anchor.Id +
                    " on canonical location " + mapId + " -> " + _store.PathDescription);
            }
            catch (Exception ex)
            {
                RejectPreview(ex.GetBaseException().Message);
                Plugin.Log.LogError("SoulTape anchor save failed: " + ex);
                ShowSaveFeedback(false, ex.GetBaseException().Message);
            }
        }

        private void RefreshAuthoringData(string mapId)
        {
            _currentMapId = mapId ?? string.Empty;
            SoulTapeAnchorLoadResult loaded = _store.Load();
            if (loaded.Status == SoulTapeAnchorLoadStatus.Unreadable)
            {
                _authoringDocument = new SoulTapeSpawnAnchorDocument();
                _authoringDataError = loaded.Error;
                _savedAnchorCount = 0;
                Plugin.Log.LogError(
                    "SoulTape anchor authoring could not read curated anchors: " + loaded.Error);
                return;
            }

            _authoringDocument = loaded.Document ?? new SoulTapeSpawnAnchorDocument();
            _authoringDataError = string.Empty;
            _savedAnchorCount = SoulTapeSpawnAnchorStore.CountForMap(
                _authoringDocument.Anchors,
                _currentMapId);

            if (loaded.Status == SoulTapeAnchorLoadStatus.RecoveredFromBackup)
            {
                Plugin.Log.LogWarning(
                    "SoulTape anchor authoring loaded curated anchors from the backup copy.");
            }
        }

        private bool IsPreviewDuplicate(string mapId)
        {
            SoulTapeSpawnAnchor candidate = new SoulTapeSpawnAnchor
            {
                MapId = mapId,
                Position = Copy(_preview.Position)
            };
            return SoulTapeSpawnAnchorStore.IsDuplicateNearby(
                _authoringDocument.Anchors,
                candidate,
                SoulTapeSpawnAnchorStore.DuplicateDistanceMetres);
        }

        private void RejectPreview(string reason)
        {
            if (_preview == null)
            {
                return;
            }

            _preview = new SoulTapePlacementValidationResult(
                false,
                reason,
                _preview.Position,
                _preview.Rotation,
                _preview.SurfaceNormal,
                _preview.OutwardNudge);
        }

        private void RejectWithoutPreview(string reason)
        {
            _preview = null;
            SetGhostVisible(false);
            ShowSaveFeedback(false, reason);
            Plugin.Log.LogWarning("SoulTape anchor was not saved: " + reason);
        }

        private void ShowSolvedGhost()
        {
            if (_preview == null || !EnsureGhost())
            {
                return;
            }

            _ghost.transform.SetPositionAndRotation(
                ToUnity(_preview.Position),
                ToUnity(_preview.Rotation));
            SetGhostColor(_preview.IsValid
                ? new Color(0.02f, 1f, 0.08f, 0.9f)
                : new Color(1f, 0.02f, 0.02f, 0.9f));
            _previewUntil = Time.unscaledTime + FeedbackDurationSeconds;
            SetGhostVisible(true);
        }

        private void ShowSaveFeedback(bool saved, string detail)
        {
            _feedbackTitle = saved ? "✓ ANCHOR SAVED" : "✗ NOT SAVED";
            _feedbackDetail = detail ?? string.Empty;
            _feedbackColor = saved
                ? new Color(0.15f, 1f, 0.2f, 1f)
                : new Color(1f, 0.2f, 0.15f, 1f);
            _feedbackUntil = Time.unscaledTime + FeedbackDurationSeconds;
        }

        private void OnGUI()
        {
            bool showPreview = _preview != null && Time.unscaledTime < _previewUntil;
            bool showFeedback = !string.IsNullOrEmpty(_feedbackTitle) &&
                                Time.unscaledTime < _feedbackUntil;
            bool showNoclip = !string.IsNullOrEmpty(_noclipFeedback) &&
                              Time.unscaledTime < _noclipFeedbackUntil;
            if (!showPreview && !showFeedback && !showNoclip)
            {
                return;
            }

            EnsureHudStyles();
            string mapDisplay = string.IsNullOrWhiteSpace(_currentMapId)
                ? "unavailable"
                : _currentMapId;
            GUI.Box(new Rect(18f, 18f, 330f, 92f), GUIContent.none);
            GUI.Label(
                new Rect(30f, 27f, 306f, 72f),
                "SoulTape Anchor Authoring\nMap: " + mapDisplay +
                "\nSaved anchors on this map: " + _savedAnchorCount,
                _summaryStyle);

            if (showPreview)
            {
                _statusStyle.normal.textColor = _preview.IsValid
                    ? new Color(0.1f, 1f, 0.15f, 1f)
                    : new Color(1f, 0.2f, 0.15f, 1f);
                const float statusWidth = 620f;
                float statusLeft = (Screen.width - statusWidth) * 0.5f;
                float statusTop = Screen.height - 112f;
                GUI.Box(new Rect(statusLeft, statusTop, statusWidth, 52f), GUIContent.none);
                GUI.Label(
                    new Rect(statusLeft + 12f, statusTop + 8f,
                        statusWidth - 24f, 36f),
                    GetPlacementStatusText(_preview),
                    _statusStyle);
            }

            if (showFeedback)
            {
                const float feedbackWidth = 520f;
                float feedbackLeft = (Screen.width - feedbackWidth) * 0.5f;
                float feedbackTop = Screen.height * 0.22f;
                _feedbackStyle.normal.textColor = _feedbackColor;
                GUI.Box(new Rect(feedbackLeft, feedbackTop, feedbackWidth, 86f),
                    GUIContent.none);
                GUI.Label(
                    new Rect(feedbackLeft + 12f, feedbackTop + 8f,
                        feedbackWidth - 24f, 70f),
                    _feedbackTitle + "\n" + _feedbackDetail,
                    _feedbackStyle);
            }

            if (showNoclip)
            {
                const float noclipWidth = 330f;
                float noclipLeft = (Screen.width - noclipWidth) * 0.5f;
                float noclipTop = Screen.height * 0.12f;
                GUI.Box(new Rect(noclipLeft, noclipTop, noclipWidth, 46f),
                    GUIContent.none);
                GUI.Label(
                    new Rect(noclipLeft + 10f, noclipTop + 5f,
                        noclipWidth - 20f, 36f),
                    _noclipFeedback,
                    _noclipStyle);
            }
        }

        private void EnsureHudStyles()
        {
            if (_summaryStyle != null)
            {
                return;
            }

            _summaryStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                wordWrap = false
            };
            _summaryStyle.normal.textColor = Color.white;
            _statusStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                wordWrap = false
            };
            _feedbackStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 21,
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };
            _noclipStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                wordWrap = false
            };
            _noclipStyle.normal.textColor = Color.white;
        }

        private static string GetPlacementStatusText(
            SoulTapePlacementValidationResult preview)
        {
            if (preview == null)
            {
                return "INVALID — no surface targeted";
            }
            if (preview.IsValid)
            {
                return "VALID PLACEMENT — saved by F9";
            }

            string reason = preview.Reason ?? string.Empty;
            if (reason.IndexOf("vertical", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "INVALID — surface too steep";
            }
            if (reason.IndexOf("clipped", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("clipping", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "INVALID — cassette clipping";
            }
            if (reason.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("duplicate", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "INVALID — duplicate anchor nearby";
            }
            if (reason.IndexOf("map ID", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "INVALID — map ID unavailable";
            }
            if (reason.IndexOf("unreadable", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "INVALID — authoring data unreadable";
            }

            reason = reason.Trim().TrimEnd('.');
            return "INVALID — " +
                   (string.IsNullOrWhiteSpace(reason) ? "unknown reason" : reason);
        }

        private static bool IsCassetteClipping(
            SoulTapeVector3 center,
            SoulTapeQuaternion rotation,
            SoulTapeVector3 halfExtents)
        {
            return Physics.CheckBox(
                ToUnity(center),
                ToUnity(halfExtents),
                ToUnity(rotation),
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
        }

        private bool EnsureGhost()
        {
            if (_ghost != null)
            {
                return true;
            }
            if (_ghostCreationFailed)
            {
                return false;
            }

            Shader shader = Shader.Find("Legacy Shaders/Transparent/Diffuse") ??
                            Shader.Find("Sprites/Default") ??
                            Shader.Find("Unlit/Color");
            if (shader == null)
            {
                _ghostCreationFailed = true;
                Plugin.Log.LogError(
                    "SoulTape anchor preview is disabled because no supported ghost shader is available.");
                return false;
            }

            try
            {
                _ghostMaterial = new Material(shader);
                _ghost = new GameObject("SoulTape Curated Anchor Ghost");
                DontDestroyOnLoad(_ghost);
                AddPrimitive(
                    PrimitiveType.Cube,
                    "Cassette body",
                    Vector3.zero,
                    ToUnity(SoulTapePlacementSolver.CassetteHalfExtents) * 2f,
                    Quaternion.identity);
                AddPrimitive(
                    PrimitiveType.Cylinder,
                    "Left reel",
                    new Vector3(-0.025f, 0.011f, 0f),
                    new Vector3(0.018f, 0.004f, 0.018f),
                    Quaternion.identity);
                AddPrimitive(
                    PrimitiveType.Cylinder,
                    "Right reel",
                    new Vector3(0.025f, 0.011f, 0f),
                    new Vector3(0.018f, 0.004f, 0.018f),
                    Quaternion.identity);
                SetGhostVisible(false);
                return true;
            }
            catch (Exception ex)
            {
                _ghostCreationFailed = true;
                SetGhostVisible(false);
                if (_ghost != null)
                {
                    Destroy(_ghost);
                    _ghost = null;
                }
                if (_ghostMaterial != null)
                {
                    Destroy(_ghostMaterial);
                    _ghostMaterial = null;
                }
                Plugin.Log.LogError(
                    "SoulTape anchor preview is disabled because ghost construction failed: " + ex);
                return false;
            }
        }

        private void AddPrimitive(
            PrimitiveType primitiveType,
            string objectName,
            Vector3 localPosition,
            Vector3 localScale,
            Quaternion localRotation)
        {
            GameObject part = GameObject.CreatePrimitive(primitiveType);
            part.name = objectName;
            part.transform.SetParent(_ghost.transform, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = localRotation;
            part.transform.localScale = localScale;
            Collider collider = part.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                Destroy(collider);
            }

            Renderer renderer = part.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = _ghostMaterial;
            }
        }

        private void SetGhostColor(Color color)
        {
            if (_ghostMaterial != null)
            {
                _ghostMaterial.color = color;
            }
        }

        private void SetGhostVisible(bool visible)
        {
            if (_ghost != null && _ghost.activeSelf != visible)
            {
                _ghost.SetActive(visible);
            }
        }

        private static bool ShortcutPressed(KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None || !Input.GetKeyDown(shortcut.MainKey))
            {
                return false;
            }

            foreach (KeyCode modifier in shortcut.Modifiers)
            {
                if (!ModifierHeld(modifier))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool ModifierHeld(KeyCode modifier)
        {
            switch (modifier)
            {
                case KeyCode.LeftControl:
                case KeyCode.RightControl:
                    return ControlHeld();
                case KeyCode.LeftShift:
                case KeyCode.RightShift:
                    return ShiftHeld();
                case KeyCode.LeftAlt:
                case KeyCode.RightAlt:
                    return Input.GetKey(KeyCode.LeftAlt) ||
                           Input.GetKey(KeyCode.RightAlt);
                default:
                    return Input.GetKey(modifier);
            }
        }

        private static bool ControlHeld()
        {
            return Input.GetKey(KeyCode.LeftControl) ||
                   Input.GetKey(KeyCode.RightControl);
        }

        private static bool ShiftHeld()
        {
            return Input.GetKey(KeyCode.LeftShift) ||
                   Input.GetKey(KeyCode.RightShift);
        }

        private static SoulTapeVector3 ToData(Vector3 value)
        {
            return new SoulTapeVector3(value.x, value.y, value.z);
        }

        private static Vector3 ToUnity(SoulTapeVector3 value)
        {
            return new Vector3(value.X, value.Y, value.Z);
        }

        private static Quaternion ToUnity(SoulTapeQuaternion value)
        {
            return new Quaternion(value.X, value.Y, value.Z, value.W);
        }

        private static SoulTapeVector3 Copy(SoulTapeVector3 value)
        {
            return new SoulTapeVector3(value.X, value.Y, value.Z);
        }

        private static string CreateMarkerId(string mapId)
        {
            return "curated-" + SafeIdPart(mapId) + "-" +
                   DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") + "-" +
                   Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        private static string SafeIdPart(string value)
        {
            char[] characters = (value ?? string.Empty).Trim().ToLowerInvariant().ToCharArray();
            for (int index = 0; index < characters.Length; index++)
            {
                if (!char.IsLetterOrDigit(characters[index]))
                {
                    characters[index] = '-';
                }
            }

            string safe = new string(characters).Trim('-');
            return string.IsNullOrWhiteSpace(safe) ? "unknown-map" : safe;
        }

        private void OnAuthoringPlayerDeadOrUnspawn(IPlayer player)
        {
            DisableNoclip("player died, extracted, or unspawned", false);
        }

        private void OnActiveSceneChanged(Scene previous, Scene next)
        {
            if (_noclipEnabled || _authoringPlayer != null ||
                _movementContext != null || _colliders.Count > 0 ||
                _authoringCameraRoot != null)
            {
                DisableNoclip(
                    "scene changed from " + previous.name + " to " + next.name,
                    false);
            }
        }

        internal void Shutdown(string reason)
        {
            DisableNoclip(string.IsNullOrWhiteSpace(reason)
                ? "plugin shutdown"
                : reason,
                true);
        }

        private void OnApplicationQuit()
        {
            DisableNoclip("application quit", false);
        }

        private void OnDestroy()
        {
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            DisableNoclip("component destroyed", true);
            SetGhostVisible(false);
            if (_ghost != null)
            {
                Destroy(_ghost);
            }
            if (_ghostMaterial != null)
            {
                Destroy(_ghostMaterial);
            }
        }

        private sealed class ColliderState
        {
            internal ColliderState(Collider collider, bool wasEnabled)
            {
                Collider = collider;
                WasEnabled = wasEnabled;
            }

            internal Collider Collider { get; private set; }
            internal bool WasEnabled { get; private set; }
        }
    }
}
#endif
