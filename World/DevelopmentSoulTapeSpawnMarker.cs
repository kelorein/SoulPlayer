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

namespace SoulPlayer.World
{
    /// <summary>
    /// Opt-in in-raid authoring workflow. The complete type is excluded from
    /// normal builds unless SoulPlayerPlacementTools=true.
    /// </summary>
    internal sealed class DevelopmentSoulTapeSpawnMarker : MonoBehaviour
    {
        private const float MaximumRayDistance = 100f;
        private const float RotationStepDegrees = 5f;
        private const float OffsetStepMetres = 0.005f;
        private const float MaximumManualOffset = 0.2f;
        private const float FeedbackDurationSeconds = 2f;

        private readonly SoulTapePlacementSolver _solver = new SoulTapePlacementSolver();
        private readonly List<ColliderState> _colliders = new List<ColliderState>();
        private readonly List<GameObject> _savedAnchorMarkers = new List<GameObject>();

        private ConfigEntry<KeyboardShortcut> _toggleHotkey;
        private ConfigEntry<KeyboardShortcut> _saveHotkey;
        private ConfigEntry<string> _semanticTag;
        private ConfigEntry<float> _flySpeed;
        private ConfigEntry<float> _speedBoost;
        private SoulTapeSpawnAnchorStore _store;
        private Player _player;
        private MovementContext _movementContext;
        private ActiveHealthController _healthController;
        private GameObject _ghost;
        private Material _ghostMaterial;
        private GameObject _savedAnchorMarkerRoot;
        private Material _savedAnchorMaterial;
        private SoulTapePlacementValidationResult _preview;
        private SoulTapeSpawnAnchorDocument _authoringDocument =
            new SoulTapeSpawnAnchorDocument();
        private string _currentMapId = string.Empty;
        private string _authoringDataError = string.Empty;
        private string _feedbackTitle = string.Empty;
        private string _feedbackDetail = string.Empty;
        private Color _feedbackColor = Color.white;
        private float _feedbackUntil;
        private int _savedAnchorCount;
        private GUIStyle _summaryStyle;
        private GUIStyle _statusStyle;
        private GUIStyle _feedbackStyle;
        private Vector3 _flyPosition;
        private Vector3 _safeEntryPosition;
        private float _rotationDegrees;
        private float _manualSurfaceOffset;
        private float _originalDamageCoefficient;
        private float _originalFallSafeHeight;
        private bool _originalIgnoreDeltaMovement;
        private bool _placementMode;
        private bool _ghostCreationFailed;

        internal void Initialize(ConfigFile config)
        {
            _toggleHotkey = config.Bind(
                "Development",
                "Placement mode toggle",
                new KeyboardShortcut(
                    KeyCode.F8,
                    KeyCode.LeftControl,
                    KeyCode.LeftShift),
                "Development build only. Toggles cassette placement flight and preview mode.");
            _saveHotkey = config.Bind(
                "Development",
                "Save solved cassette anchor",
                new KeyboardShortcut(KeyCode.F9),
                "Development build only. Saves the currently valid solved cassette anchor.");
            _semanticTag = config.Bind(
                "Development",
                "Cassette anchor semantic tag",
                string.Empty,
                "Optional tag assigned to newly saved anchors, such as office-desk or industrial-shelf.");
            _flySpeed = config.Bind(
                "Development",
                "Placement flight speed",
                4f,
                new ConfigDescription(
                    "Base player-root flight speed in metres per second.",
                    new AcceptableValueRange<float>(0.5f, 25f)));
            _speedBoost = config.Bind(
                "Development",
                "Placement flight speed boost",
                3f,
                new ConfigDescription(
                    "Shift multiplier for placement flight.",
                    new AcceptableValueRange<float>(1f, 10f)));

            string outputPath = Path.Combine(
                BepInEx.Paths.ConfigPath,
                "SoulPlayer",
                "authoring",
                "spawn-anchors.json");
            _store = new SoulTapeSpawnAnchorStore(outputPath);
        }

        private void Update()
        {
            if (_toggleHotkey != null && ShortcutPressed(_toggleHotkey.Value))
            {
                if (_placementMode)
                {
                    DisablePlacementMode("author toggle", true);
                }
                else
                {
                    TryEnablePlacementMode();
                }

                return;
            }

            if (!_placementMode)
            {
                SetGhostVisible(false);
                return;
            }

            GameWorld world = Singleton<GameWorld>.Instance;
            Player currentPlayer = world == null ? null : world.MainPlayer;
            if (currentPlayer == null || currentPlayer != _player ||
                !currentPlayer.gameObject.activeInHierarchy)
            {
                DisablePlacementMode("raid player unavailable", false);
                return;
            }

            Transform view = GetViewTransform(_player);
            if (view == null)
            {
                _preview = null;
                SetGhostVisible(false);
                return;
            }

            string mapId = world.LocationId ?? string.Empty;
            if (!string.Equals(_currentMapId, mapId, StringComparison.Ordinal))
            {
                RefreshAuthoringData(mapId);
            }

            bool savePressed = _saveHotkey != null && ShortcutPressed(_saveHotkey.Value);
            MaintainAuthorSafety();
            if (!savePressed)
            {
                UpdatePlayerFlight(view);
            }

            UpdateAuthorAdjustments();
            UpdatePreview(view, mapId);
            if (savePressed)
            {
                SavePreview(mapId);
            }
        }

        private void TryEnablePlacementMode()
        {
            GameWorld world = Singleton<GameWorld>.Instance;
            Player player = world == null ? null : world.MainPlayer;
            if (player == null || !player.gameObject.activeInHierarchy)
            {
                Plugin.Log.LogWarning(
                    "SoulTape Placement Mode requires an active local raid player.");
                return;
            }

            _player = player;
            _safeEntryPosition = player.Position;
            _flyPosition = _safeEntryPosition;
            _movementContext = player.MovementContext;
            if (_movementContext != null)
            {
                _originalIgnoreDeltaMovement = _movementContext.IgnoreDeltaMovement;
                _movementContext.IgnoreDeltaMovement = true;
            }

            _colliders.Clear();
            foreach (Collider collider in player.GetComponentsInChildren<Collider>(true))
            {
                if (collider == null)
                {
                    continue;
                }

                _colliders.Add(new ColliderState(collider, collider.enabled));
                collider.enabled = false;
            }

            _healthController = player.ActiveHealthController;
            if (_healthController != null)
            {
                _originalDamageCoefficient = _healthController.DamageCoeff;
                _originalFallSafeHeight = _healthController.FallSafeHeight;
                _healthController.SetDamageCoeff(0f);
                _healthController.FallSafeHeight = float.MaxValue;
            }

            _placementMode = true;
            EnsureGhost();
            RefreshAuthoringData(world.LocationId ?? string.Empty);
            Plugin.Log.LogWarning("SoulTape Placement Mode ENABLED (development build only).");
            Plugin.Log.LogInfo(
                "Placement controls: WASD fly, Space up, Ctrl down, Shift speed boost, " +
                "mouse wheel rotate, Shift+wheel offset, F9 save, Ctrl+Shift+F8 exit.");
            Plugin.Log.LogInfo("Curated anchor output: " + _store.PathDescription);
        }

        private void DisablePlacementMode(string reason, bool returnToEntryPosition)
        {
            if (!_placementMode && _player == null)
            {
                return;
            }

            _placementMode = false;
            SetGhostVisible(false);
            SetSavedAnchorMarkersVisible(false);

            if (returnToEntryPosition && _player != null)
            {
                try
                {
                    _player.Teleport(_safeEntryPosition, false);
                    Plugin.Log.LogInfo(
                        "SoulTape Placement Mode returned the author to the safe entry position before restoring collisions.");
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError(
                        "SoulTape Placement Mode could not return the author to the safe entry position: " + ex);
                }
            }

            if (_movementContext != null)
            {
                try
                {
                    _movementContext.IgnoreDeltaMovement = _originalIgnoreDeltaMovement;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning(
                        "SoulTape Placement Mode could not restore movement state: " + ex.Message);
                }
            }

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
                        "SoulTape Placement Mode could not restore a player collider: " + ex.Message);
                }
            }
            _colliders.Clear();

            if (_healthController != null)
            {
                try
                {
                    _healthController.SetDamageCoeff(_originalDamageCoefficient);
                    _healthController.FallSafeHeight = _originalFallSafeHeight;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning(
                        "SoulTape Placement Mode could not restore author health safety settings: " +
                        ex.Message);
                }
            }

            _movementContext = null;
            _healthController = null;
            _player = null;
            _preview = null;
            _feedbackTitle = string.Empty;
            _feedbackDetail = string.Empty;
            Plugin.Log.LogWarning("SoulTape Placement Mode DISABLED: " + reason + ".");
        }

        private void MaintainAuthorSafety()
        {
            if (_movementContext != null)
            {
                _movementContext.IgnoreDeltaMovement = true;
            }

            if (_healthController != null)
            {
                _healthController.SetDamageCoeff(0f);
                _healthController.FallSafeHeight = float.MaxValue;
            }
        }

        private void UpdatePlayerFlight(Transform view)
        {
            Vector3 direction = Vector3.zero;
            if (Input.GetKey(KeyCode.W))
            {
                direction += view.forward;
            }
            if (Input.GetKey(KeyCode.S))
            {
                direction -= view.forward;
            }
            if (Input.GetKey(KeyCode.D))
            {
                direction += view.right;
            }
            if (Input.GetKey(KeyCode.A))
            {
                direction -= view.right;
            }
            if (Input.GetKey(KeyCode.Space))
            {
                direction += Vector3.up;
            }
            if (ControlHeld())
            {
                direction -= Vector3.up;
            }

            float speed = _flySpeed.Value;
            if (ShiftHeld())
            {
                speed *= _speedBoost.Value;
            }

            if (direction.sqrMagnitude > 0.0001f)
            {
                _flyPosition += direction.normalized * speed * Time.unscaledDeltaTime;
            }

            _player.Teleport(_flyPosition, false);
        }

        private void UpdateAuthorAdjustments()
        {
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) < 0.001f)
            {
                return;
            }

            if (ShiftHeld())
            {
                _manualSurfaceOffset = Mathf.Clamp(
                    _manualSurfaceOffset + wheel * OffsetStepMetres,
                    0f,
                    MaximumManualOffset);
            }
            else
            {
                _rotationDegrees = Mathf.Repeat(
                    _rotationDegrees + wheel * RotationStepDegrees,
                    360f);
            }
        }

        private void UpdatePreview(Transform view, string mapId)
        {
            RaycastHit hit;
            if (!Physics.Raycast(
                    new Ray(view.position, view.forward),
                    out hit,
                    MaximumRayDistance,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore))
            {
                _preview = null;
                SetGhostVisible(false);
                return;
            }

            _preview = _solver.Solve(
                ToData(hit.point),
                ToData(hit.normal),
                ToData(view.forward),
                _rotationDegrees,
                _manualSurfaceOffset,
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

            if (!EnsureGhost())
            {
                RejectPreview(
                    "Placement preview is unavailable because no supported ghost shader could be created.");
                SetGhostVisible(false);
                return;
            }
            _ghost.transform.SetPositionAndRotation(
                ToUnity(_preview.Position),
                ToUnity(_preview.Rotation));
            SetGhostColor(_preview.IsValid
                ? new Color(0.02f, 1f, 0.08f, 0.9f)
                : new Color(1f, 0.02f, 0.02f, 0.9f));
            SetGhostVisible(true);
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
                ShowSaveFeedback(
                    true,
                    mapId + " \u2014 Anchor #" + _savedAnchorCount);
                Plugin.Log.LogInfo(
                    "SoulTape solved curated anchor saved: " + anchor.Id +
                    " on canonical location " + mapId + " -> " + _store.PathDescription);
            }
            catch (Exception ex)
            {
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
                ClearSavedAnchorMarkers();
                Plugin.Log.LogError(
                    "SoulTape Placement Mode could not read curated anchors: " + loaded.Error);
                return;
            }

            _authoringDocument = loaded.Document ?? new SoulTapeSpawnAnchorDocument();
            _authoringDataError = string.Empty;
            _savedAnchorCount = SoulTapeSpawnAnchorStore.CountForMap(
                _authoringDocument.Anchors,
                _currentMapId);
            RebuildSavedAnchorMarkers();

            if (loaded.Status == SoulTapeAnchorLoadStatus.RecoveredFromBackup)
            {
                Plugin.Log.LogWarning(
                    "SoulTape Placement Mode loaded curated anchors from the backup copy.");
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

        private void ShowSaveFeedback(bool saved, string detail)
        {
            _feedbackTitle = saved
                ? "\u2713 ANCHOR SAVED"
                : "\u2717 NOT SAVED";
            _feedbackDetail = detail ?? string.Empty;
            _feedbackColor = saved
                ? new Color(0.15f, 1f, 0.2f, 1f)
                : new Color(1f, 0.2f, 0.15f, 1f);
            _feedbackUntil = Time.unscaledTime + FeedbackDurationSeconds;
        }

        private void OnGUI()
        {
            if (!_placementMode)
            {
                return;
            }

            EnsureHudStyles();

            string mapDisplay = string.IsNullOrWhiteSpace(_currentMapId)
                ? "unavailable"
                : _currentMapId;
            GUI.Box(new Rect(18f, 18f, 310f, 92f), GUIContent.none);
            GUI.Label(
                new Rect(30f, 27f, 286f, 72f),
                "Placement Mode\nMap: " + mapDisplay +
                "\nSaved anchors on this map: " + _savedAnchorCount,
                _summaryStyle);

            bool valid = _preview != null && _preview.IsValid;
            _statusStyle.normal.textColor = valid
                ? new Color(0.1f, 1f, 0.15f, 1f)
                : new Color(1f, 0.2f, 0.15f, 1f);
            const float statusWidth = 620f;
            float statusLeft = (Screen.width - statusWidth) * 0.5f;
            float statusTop = Screen.height - 112f;
            GUI.Box(new Rect(statusLeft, statusTop, statusWidth, 52f), GUIContent.none);
            GUI.Label(
                new Rect(statusLeft + 12f, statusTop + 8f, statusWidth - 24f, 36f),
                GetPlacementStatusText(_preview),
                _statusStyle);

            if (!string.IsNullOrEmpty(_feedbackTitle) &&
                Time.unscaledTime < _feedbackUntil)
            {
                const float feedbackWidth = 520f;
                float feedbackLeft = (Screen.width - feedbackWidth) * 0.5f;
                float feedbackTop = Screen.height * 0.22f;
                _feedbackStyle.normal.textColor = _feedbackColor;
                GUI.Box(
                    new Rect(feedbackLeft, feedbackTop, feedbackWidth, 86f),
                    GUIContent.none);
                GUI.Label(
                    new Rect(feedbackLeft + 12f, feedbackTop + 8f, feedbackWidth - 24f, 70f),
                    _feedbackTitle + "\n" + _feedbackDetail,
                    _feedbackStyle);
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
        }

        private static string GetPlacementStatusText(
            SoulTapePlacementValidationResult preview)
        {
            if (preview == null)
            {
                return "INVALID \u2014 no surface targeted";
            }

            if (preview.IsValid)
            {
                return "VALID PLACEMENT \u2014 F9 to save";
            }

            string reason = preview.Reason ?? string.Empty;
            if (reason.IndexOf("vertical", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "INVALID \u2014 surface too steep";
            }
            if (reason.IndexOf("clipped", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("clipping", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "INVALID \u2014 cassette clipping";
            }
            if (reason.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("duplicate", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "INVALID \u2014 duplicate anchor nearby";
            }
            if (reason.IndexOf("map ID", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "INVALID \u2014 map ID unavailable";
            }
            if (reason.IndexOf("unreadable", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "INVALID \u2014 authoring data unreadable";
            }
            if (reason.IndexOf("shader", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "INVALID \u2014 preview unavailable";
            }

            reason = reason.Trim().TrimEnd('.');
            return "INVALID \u2014 " +
                   (string.IsNullOrWhiteSpace(reason) ? "unknown reason" : reason);
        }

        private void RebuildSavedAnchorMarkers()
        {
            ClearSavedAnchorMarkers();
            if (!_placementMode || string.IsNullOrWhiteSpace(_currentMapId) ||
                !EnsureGhost() || _ghostMaterial == null)
            {
                return;
            }

            try
            {
                _savedAnchorMaterial = new Material(_ghostMaterial.shader);
                _savedAnchorMaterial.color = new Color(0.05f, 0.9f, 1f, 0.8f);
                _savedAnchorMarkerRoot =
                    new GameObject("SoulTape Existing Curated Anchor Markers");
                DontDestroyOnLoad(_savedAnchorMarkerRoot);

                foreach (SoulTapeSpawnAnchor anchor in _authoringDocument.Anchors)
                {
                    if (anchor == null || !anchor.Enabled || anchor.Position == null ||
                        anchor.RotationEuler == null ||
                        !string.Equals(
                            anchor.MapId,
                            _currentMapId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    marker.name = "Saved SoulTape anchor " + anchor.Id;
                    marker.transform.SetParent(_savedAnchorMarkerRoot.transform, false);
                    marker.transform.SetPositionAndRotation(
                        ToUnity(anchor.Position),
                        Quaternion.Euler(ToUnity(anchor.RotationEuler)));
                    marker.transform.localScale =
                        ToUnity(SoulTapePlacementSolver.CassetteHalfExtents) * 2f;
                    Collider collider = marker.GetComponent<Collider>();
                    if (collider != null)
                    {
                        collider.enabled = false;
                        Destroy(collider);
                    }

                    Renderer renderer = marker.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        renderer.sharedMaterial = _savedAnchorMaterial;
                    }
                    _savedAnchorMarkers.Add(marker);
                }
            }
            catch (Exception ex)
            {
                ClearSavedAnchorMarkers();
                Plugin.Log.LogError(
                    "SoulTape Placement Mode could not visualize saved anchors: " + ex);
            }
        }

        private void SetSavedAnchorMarkersVisible(bool visible)
        {
            if (_savedAnchorMarkerRoot != null &&
                _savedAnchorMarkerRoot.activeSelf != visible)
            {
                _savedAnchorMarkerRoot.SetActive(visible);
            }
        }

        private void ClearSavedAnchorMarkers()
        {
            _savedAnchorMarkers.Clear();
            if (_savedAnchorMarkerRoot != null)
            {
                Destroy(_savedAnchorMarkerRoot);
                _savedAnchorMarkerRoot = null;
            }
            if (_savedAnchorMaterial != null)
            {
                Destroy(_savedAnchorMaterial);
                _savedAnchorMaterial = null;
            }
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
                    "SoulTape Placement Mode preview is disabled because no supported ghost shader is available.");
                return false;
            }

            try
            {
                _ghostMaterial = new Material(shader);
                _ghost = new GameObject("SoulTape Curated Placement Ghost");
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
                    "SoulTape Placement Mode preview is disabled because ghost construction failed: " + ex);
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

        private static Transform GetViewTransform(Player player)
        {
            if (player != null && player.CameraPosition != null)
            {
                return player.CameraPosition;
            }

            Camera mainCamera = Camera.main;
            return mainCamera == null ? null : mainCamera.transform;
        }

        private static bool ShortcutPressed(KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None || !Input.GetKeyDown(shortcut.MainKey))
            {
                return false;
            }

            foreach (KeyCode modifier in shortcut.Modifiers)
            {
                if (!Input.GetKey(modifier))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ShiftHeld()
        {
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }

        private static bool ControlHeld()
        {
            return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
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

        private void OnDestroy()
        {
            DisablePlacementMode("tool destroyed", true);
            ClearSavedAnchorMarkers();
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
