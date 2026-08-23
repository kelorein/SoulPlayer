using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Comfort.Common;
using EFT;
using SoulPlayer.Cassettes;
using SoulPlayer.Configuration;
using SoulPlayer.Library;
using SoulPlayer.Utils;
using UnityEngine;

namespace SoulPlayer.World
{
    internal sealed class SoulTapeWorldDiscoveryController : MonoBehaviour
    {
        private const float NotificationDurationSeconds = 3.5f;

        private readonly SoulTapeSpawnPlanner _planner = new SoulTapeSpawnPlanner();
        private readonly List<SoulTapeWorldPickup> _pickups =
            new List<SoulTapeWorldPickup>();

        private SoulPlayerSettings _settings;
        private MusicLibrary _library;
        private SoulTapeCatalog _catalog;
        private SoulTapeCollection _collection;
        private SoulTapeCollectionController _collectionHost;
        private SoulTapeSpawnAnchorCatalog _anchors;
        private SoulTapeDiscoveryService _discovery;
        private ISoulTapeLog _log;
        private GameWorld _activeWorld;
        private Camera _gameplayCamera;
        private string _cameraSelectionSource = "unavailable";
        private string _mainCameraAudit = "not evaluated";
        private int _activeCameraCount;
        private SoulTapeWorldPickup _targetedPickup;
        private PickupEvaluation _nearestPickupEvaluation;
        private bool _spawnPending;
        private bool _spawnCompleted;
        private int _raidSeed;
        private string _notificationTitle = string.Empty;
        private string _notificationDetail = string.Empty;
        private float _notificationUntil;
        private GUIStyle _promptStyle;
        private GUIStyle _notificationStyle;
        private GUIStyle _diagnosticStyle;
        private Texture2D _interactionEyeTexture;

        internal void Initialize(
            SoulPlayerSettings settings,
            MusicLibrary library,
            SoulTapeCatalog catalog,
            SoulTapeCollection collection,
            SoulTapeCollectionController collectionHost,
            SoulTapeSpawnAnchorCatalog anchors,
            ISoulTapeLog log)
        {
            _settings = settings;
            _library = library;
            _catalog = catalog;
            _collection = collection;
            _collectionHost = collectionHost;
            _anchors = anchors;
            _log = log;
            _discovery = new SoulTapeDiscoveryService(catalog, collection, log);
            _discovery.Discovered += OnDiscovered;
            _catalog.Changed += OnCatalogChanged;
        }

        private void Update()
        {
            GameWorld world = Singleton<GameWorld>.Instance;
            if (!GameState.IsInRaid() || world == null)
            {
                if (_activeWorld != null)
                {
                    EndRaid("raid ended");
                }
                return;
            }

            if (world != _activeWorld)
            {
                if (_activeWorld != null)
                {
                    EndRaid("GameWorld changed");
                }
                BeginRaid(world);
            }

            Player player = world.MainPlayer;
            if (player == null || !player.gameObject.activeInHierarchy)
            {
                _targetedPickup = null;
                _nearestPickupEvaluation = null;
                return;
            }

            if (_spawnPending && !_spawnCompleted)
            {
                TrySpawnForRaid(world, player);
            }

            UpdateInteraction(player);
        }

        private void BeginRaid(GameWorld world)
        {
            _activeWorld = world;
            _raidSeed = unchecked(Environment.TickCount * 397 ^ world.GetInstanceID());
            _spawnPending = true;
            _spawnCompleted = false;
            _gameplayCamera = null;
            _cameraSelectionSource = "unavailable";
            _mainCameraAudit = "not evaluated";
            _activeCameraCount = 0;
            _log.Info(
                "SoulTape World Discovery pending for canonical map " +
                (world.LocationId ?? "<unavailable>") + ", seed " + _raidSeed + ".");
        }

        private void TrySpawnForRaid(GameWorld world, Player player)
        {
            string mapId = world.LocationId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(mapId))
            {
                return;
            }

            IReadOnlyList<SoulTapeSpawnAnchor> mapAnchors = _anchors.GetForMap(mapId);
            if (mapAnchors.Count == 0)
            {
                CompleteSpawnAttempt(
                    "SoulTape World Discovery: no committed curated anchors for " + mapId + ".");
                return;
            }

            if (!_collectionHost.EnsureProfile(player) || !_collection.IsLoaded)
            {
                return;
            }

            // A zero-audio catalog is not final while the asynchronous scan is active.
            if (_library.IsScanning || !_library.HasAppliedScan)
            {
                return;
            }

            IReadOnlyList<SoulTapeCatalogEntry> eligible =
                SoulTapeWorldEligibility.GetEligibleTapes(_catalog, _collection);
            if (eligible.Count == 0)
            {
                _spawnPending = false;
                _log.Info(
                    "SoulTape World Discovery remains pending: no locked, available " +
                    "curated-rarity tapes are currently eligible; a later catalog " +
                    "refresh will retry this raid.");
                return;
            }

            SoulTapeSpawnPlan plan = _planner.CreatePlan(
                mapId,
                eligible,
                mapAnchors,
                _raidSeed);
            foreach (SoulTapeSpawnPlanEntry planned in plan.Entries)
            {
                SoulTapeWorldPickup pickup = SoulTapeWorldPickupFactory.Create(planned, _log);
                if (pickup != null)
                {
                    _pickups.Add(pickup);
                }
            }

            _spawnPending = false;
            _spawnCompleted = true;
            _log.Info(
                "SoulTape World Discovery spawned " + _pickups.Count +
                " cassette pickups on " + mapId + " using seed " + _raidSeed + ".");
        }

        private void CompleteSpawnAttempt(string message)
        {
            _spawnPending = false;
            _spawnCompleted = true;
            _log.Info(message);
        }

        private void OnCatalogChanged()
        {
            if (_activeWorld != null && !_spawnCompleted)
            {
                _spawnPending = true;
                _log.Info(
                    "SoulTape World Discovery catalog refreshed while raid spawning was pending.");
            }
        }

        private void UpdateInteraction(Player player)
        {
            _pickups.RemoveAll(candidate => candidate == null);
            _targetedPickup = FindTargetedPickup(player);
            if (_targetedPickup == null ||
                !ShortcutPressed(_settings.CollectCassetteHotkey))
            {
                return;
            }

            SoulTapeWorldPickup pickup = _targetedPickup;
            SoulTapeDiscoveryResult result = _discovery.Discover(pickup.Cassette.Id);
            if (result == SoulTapeDiscoveryResult.NewUnlock ||
                result == SoulTapeDiscoveryResult.AlreadyUnlocked)
            {
                _pickups.Remove(pickup);
                _targetedPickup = null;
                Destroy(pickup.gameObject);
            }
        }

        private SoulTapeWorldPickup FindTargetedPickup(Player player)
        {
            _nearestPickupEvaluation = null;
            Ray targetingRay;
            Camera camera;
            if (!TryGetScreenCenterRay(player, out camera, out targetingRay))
            {
                return null;
            }

            SoulTapeWorldPickup closest = null;
            float closestAngle = float.MaxValue;
            foreach (SoulTapeWorldPickup pickup in _pickups)
            {
                if (pickup == null || pickup.Cassette == null)
                {
                    continue;
                }

                bool isCurrent = pickup == _targetedPickup;
                PickupEvaluation evaluation = EvaluatePickup(
                    pickup,
                    player,
                    camera,
                    targetingRay,
                    isCurrent);
                if (_nearestPickupEvaluation == null ||
                    evaluation.Angle < _nearestPickupEvaluation.Angle)
                {
                    _nearestPickupEvaluation = evaluation;
                }

                if (evaluation.IsTargetable && evaluation.Angle < closestAngle)
                {
                    closest = pickup;
                    closestAngle = evaluation.Angle;
                }
            }

            if (_nearestPickupEvaluation != null)
            {
                _nearestPickupEvaluation.IsFinalTarget =
                    _nearestPickupEvaluation.Pickup == closest;
            }
            return closest;
        }

        private PickupEvaluation EvaluatePickup(
            SoulTapeWorldPickup pickup,
            Player player,
            Camera camera,
            Ray screenCenterRay,
            bool isCurrentTarget)
        {
            PickupEvaluation evaluation = new PickupEvaluation
            {
                Pickup = pickup,
                IsCurrentTarget = isCurrentTarget,
                AllowedAngle = SoulTapeInteractionTargeting.GetAllowedAngle(isCurrentTarget),
                Angle = float.MaxValue,
                Viewport = new Vector3(float.NaN, float.NaN, float.NaN)
            };
            Vector3 focus = pickup.InteractionFocusPoint;
            Vector3 toFocus = focus - screenCenterRay.origin;
            float distance = toFocus.magnitude;
            evaluation.Distance = distance;
            evaluation.WithinDistance = distance > 0.001f &&
                                        distance <= _settings.CassetteInteractionDistance;
            if (distance <= 0.001f)
            {
                return evaluation;
            }

            Vector3 direction = toFocus / distance;
            evaluation.InFront = Vector3.Dot(screenCenterRay.direction, direction) > 0f;

            if (camera != null)
            {
                Vector3 viewport = camera.WorldToViewportPoint(focus);
                evaluation.Viewport = viewport;
                evaluation.InViewport = viewport.z > 0f &&
                                        viewport.x >= 0f && viewport.x <= 1f &&
                                        viewport.y >= 0f && viewport.y <= 1f;
            }
            else
            {
                evaluation.InViewport = true;
            }

            evaluation.Angle = Vector3.Angle(screenCenterRay.direction, direction);
            evaluation.InsideAcquireCone =
                evaluation.Angle <= SoulTapeInteractionTargeting.AcquireAngleDegrees;
            evaluation.InsideAllowedCone = evaluation.Angle <= evaluation.AllowedAngle;

            if (evaluation.WithinDistance && evaluation.InFront && evaluation.InViewport)
            {
                LineOfSightEvaluation lineOfSight = EvaluateLineOfSight(
                    player,
                    pickup,
                    camera,
                    screenCenterRay.origin);
                evaluation.LineOfSightClear = lineOfSight.IsClear;
                evaluation.VisibilitySamplesTested = lineOfSight.SamplesTested;
                evaluation.VisibilitySampleName = lineOfSight.SampleName;
                evaluation.Blocker = lineOfSight.Blocker;
            }
            evaluation.IsTargetable = evaluation.WithinDistance &&
                                      evaluation.InFront &&
                                      evaluation.InViewport &&
                                      evaluation.InsideAllowedCone &&
                                      evaluation.LineOfSightClear;
            return evaluation;
        }

        private static LineOfSightEvaluation EvaluateLineOfSight(
            Player player,
            SoulTapeWorldPickup pickup,
            Camera camera,
            Vector3 origin)
        {
            LineOfSightEvaluation evaluation = new LineOfSightEvaluation();
            for (int index = 0; index < SoulTapeWorldPickup.VisibilitySampleCount; index++)
            {
                Vector3 sample = pickup.GetVisibilitySamplePoint(index);
                Vector3 toSample = sample - origin;
                float sampleDistance = toSample.magnitude;
                if (sampleDistance <= 0.001f)
                {
                    continue;
                }

                evaluation.SamplesTested++;
                string sampleName = SoulTapeWorldPickup.GetVisibilitySampleName(index);
                RaycastHit blocker;
                if (!TryFindFirstWorldBlocker(
                        player,
                        pickup,
                        camera,
                        origin,
                        toSample / sampleDistance,
                        sampleDistance,
                        out blocker))
                {
                    evaluation.IsClear = true;
                    evaluation.SampleName = sampleName;
                    evaluation.Blocker = null;
                    return evaluation;
                }

                if (evaluation.Blocker == null)
                {
                    Collider collider = blocker.collider;
                    Transform colliderTransform = collider == null ? null : collider.transform;
                    evaluation.SampleName = sampleName;
                    evaluation.Blocker = new BlockingHitDiagnostic
                    {
                        SampleName = sampleName,
                        GameObjectName = collider == null ? "<destroyed>" : collider.gameObject.name,
                        ColliderType = collider == null ? "<unavailable>" : collider.GetType().Name,
                        Layer = collider == null ? -1 : collider.gameObject.layer,
                        LayerName = collider == null
                            ? "<unavailable>"
                            : LayerMask.LayerToName(collider.gameObject.layer),
                        HitDistance = blocker.distance,
                        FocusDistance = sampleDistance,
                        ParentName = colliderTransform == null || colliderTransform.parent == null
                            ? "<none>"
                            : colliderTransform.parent.name,
                        RootName = colliderTransform == null || colliderTransform.root == null
                            ? "<none>"
                            : colliderTransform.root.name
                    };
                }
            }

            return evaluation;
        }

        private static bool TryFindFirstWorldBlocker(
            Player player,
            SoulTapeWorldPickup pickup,
            Camera camera,
            Vector3 origin,
            Vector3 direction,
            float distance,
            out RaycastHit blocker)
        {
            RaycastHit[] hits = Physics.RaycastAll(
                origin,
                direction,
                distance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            Array.Sort(hits, CompareRaycastHits);
            foreach (RaycastHit hit in hits)
            {
                Collider collider = hit.collider;
                if (ShouldIgnoreLineOfSightHit(collider, player, pickup, camera))
                {
                    continue;
                }

                blocker = hit;
                return true;
            }

            blocker = new RaycastHit();
            return false;
        }

        private static int CompareRaycastHits(RaycastHit left, RaycastHit right)
        {
            return left.distance.CompareTo(right.distance);
        }

        private static bool ShouldIgnoreLineOfSightHit(
            Collider collider,
            Player player,
            SoulTapeWorldPickup pickup,
            Camera camera)
        {
            if (collider == null || collider.isTrigger)
            {
                return true;
            }

            Transform hitTransform = collider.transform;
            if (pickup != null && hitTransform.IsChildOf(pickup.transform))
            {
                return true;
            }

            if (player != null)
            {
                Transform playerTransform = player.Transform == null
                    ? null
                    : player.Transform.Original;
                if (playerTransform != null && hitTransform.IsChildOf(playerTransform))
                {
                    return true;
                }

                Player hitPlayer = collider.GetComponentInParent<Player>();
                if (hitPlayer == player)
                {
                    return true;
                }
            }

            return camera != null && hitTransform.IsChildOf(camera.transform);
        }

        private bool TryGetScreenCenterRay(
            Player player,
            out Camera camera,
            out Ray ray)
        {
            camera = ResolveGameplayCamera();
            if (IsUsableGameplayCamera(camera))
            {
                ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                return true;
            }

            Transform fallback = player == null ? null : player.CameraPosition;
            if (fallback != null)
            {
                camera = null;
                _gameplayCamera = null;
                _cameraSelectionSource = "Player.CameraPosition fallback";
                ray = new Ray(fallback.position, fallback.forward);
                return true;
            }

            camera = null;
            _gameplayCamera = null;
            _cameraSelectionSource = "unavailable";
            ray = new Ray();
            return false;
        }

        private Camera ResolveGameplayCamera()
        {
            // Correctness-first acceptance path: resolve Camera.main for every
            // targeting update instead of accepting whichever camera rendered last.
            Camera main = Camera.main;
            string mainRejection = GetCameraRejectionReason(main, false);
            _mainCameraAudit = string.IsNullOrEmpty(mainRejection)
                ? "accepted"
                : "rejected: " + mainRejection;
            _activeCameraCount = Camera.allCamerasCount;
            if (string.IsNullOrEmpty(mainRejection))
            {
                _gameplayCamera = main;
                _cameraSelectionSource = "Camera.main";
                return _gameplayCamera;
            }

            Camera fallback = null;
            foreach (Camera candidate in Camera.allCameras)
            {
                if (!IsVerifiedFullScreenGameplayCamera(candidate, true))
                {
                    continue;
                }

                // The primary world camera normally renders before weapon/UI
                // overlays, so prefer the lowest-depth verified fallback.
                if (fallback == null || candidate.depth < fallback.depth)
                {
                    fallback = candidate;
                }
            }

            _gameplayCamera = fallback;
            _cameraSelectionSource = fallback == null
                ? "no verified full-screen camera"
                : "verified full-screen fallback";
            return _gameplayCamera;
        }

        private static bool IsVerifiedFullScreenGameplayCamera(
            Camera camera,
            bool requireWorldLayers)
        {
            return string.IsNullOrEmpty(
                GetCameraRejectionReason(camera, requireWorldLayers));
        }

        private static string GetCameraRejectionReason(
            Camera camera,
            bool requireWorldLayers)
        {
            if (camera == null)
            {
                return "null";
            }
            if (!camera.enabled)
            {
                return "disabled";
            }
            if (!camera.gameObject.activeInHierarchy)
            {
                return "inactive GameObject";
            }
            if (camera.targetTexture != null)
            {
                return "renders to targetTexture";
            }
            if (camera.orthographic)
            {
                return "orthographic/UI-style projection";
            }

            Rect rect = camera.rect;
            bool fullScreen = rect.x <= 0.05f && rect.y <= 0.05f &&
                              rect.width >= 0.9f && rect.height >= 0.9f;
            if (!fullScreen)
            {
                return "not full-screen";
            }

            if (SoulTapeInteractionTargeting.LooksLikeAuxiliaryCameraName(camera.name))
            {
                return "auxiliary camera name";
            }

            if (!requireWorldLayers)
            {
                return string.Empty;
            }

            int defaultLayer = LayerMask.NameToLayer("Default");
            bool rendersDefaultWorldLayer = defaultLayer >= 0 &&
                                            (camera.cullingMask & (1 << defaultLayer)) != 0;
            return rendersDefaultWorldLayer
                ? string.Empty
                : "does not render the Default world layer";
        }

        private static bool IsUsableGameplayCamera(Camera camera)
        {
            return camera != null &&
                   camera.enabled &&
                   camera.gameObject.activeInHierarchy &&
                   camera.targetTexture == null;
        }

        private void OnDiscovered(SoulTapeDiscoveryEvent discovered)
        {
            switch (discovered.Result)
            {
                case SoulTapeDiscoveryResult.NewUnlock:
                    _notificationTitle = "SOULTAPE DISCOVERED";
                    _notificationDetail = discovered.Artist + " \u2014 " + discovered.Title +
                                          "\n" + discovered.Rarity;
                    break;
                case SoulTapeDiscoveryResult.AlreadyUnlocked:
                    _notificationTitle = "Cassette already discovered";
                    _notificationDetail = discovered.Artist + " \u2014 " + discovered.Title;
                    break;
                case SoulTapeDiscoveryResult.SaveFailed:
                    _notificationTitle = "CASSETTE NOT SAVED";
                    _notificationDetail = "Collection save failed. Aim at the cassette and retry.";
                    break;
                default:
                    _notificationTitle = "CASSETTE NOT COLLECTED";
                    _notificationDetail = "The cassette ID is not in the current catalog.";
                    break;
            }

            _notificationUntil = Time.unscaledTime + NotificationDurationSeconds;
        }

        private void OnGUI()
        {
            if (_activeWorld == null)
            {
                return;
            }

            EnsureGuiStyles();
            if (_settings.ShowSoulTapeTargetingDiagnostics && _pickups.Count > 0)
            {
                DrawTargetingDiagnostics();
            }

            if (_targetedPickup != null && _targetedPickup.Cassette != null)
            {
                EnsureInteractionEyeTexture();
                if (_interactionEyeTexture != null)
                {
                    const float eyeSize = 8f;
                    GUI.DrawTexture(
                        new Rect(
                            (Screen.width - eyeSize) * 0.5f,
                            (Screen.height - eyeSize) * 0.5f,
                            eyeSize,
                            eyeSize),
                        _interactionEyeTexture);
                }

                const float width = 500f;
                float left = (Screen.width - width) * 0.5f;
                float top = Screen.height - 175f;
                GUI.Box(new Rect(left, top, width, 70f), GUIContent.none);
                GUI.Label(
                    new Rect(left + 10f, top + 7f, width - 20f, 56f),
                    "[" + _settings.CollectCassetteHotkey.MainKey + "] Collect cassette\n" +
                    _targetedPickup.Cassette.Artist + " \u2014 " +
                    _targetedPickup.Cassette.Title,
                    _promptStyle);
            }

            if (!string.IsNullOrEmpty(_notificationTitle) &&
                Time.unscaledTime < _notificationUntil)
            {
                const float width = 520f;
                float left = (Screen.width - width) * 0.5f;
                float top = Screen.height * 0.18f;
                GUI.Box(new Rect(left, top, width, 94f), GUIContent.none);
                GUI.Label(
                    new Rect(left + 10f, top + 8f, width - 20f, 78f),
                    _notificationTitle + "\n" + _notificationDetail,
                    _notificationStyle);
            }
        }

        private void EnsureGuiStyles()
        {
            if (_promptStyle != null)
            {
                return;
            }

            _promptStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };
            _promptStyle.normal.textColor = Color.white;
            _notificationStyle = new GUIStyle(_promptStyle)
            {
                fontSize = 20
            };
            _notificationStyle.normal.textColor = new Color(0.95f, 0.78f, 0.3f, 1f);
            _diagnosticStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 14,
                fontStyle = FontStyle.Normal,
                wordWrap = false
            };
            _diagnosticStyle.normal.textColor = Color.white;
        }

        private void DrawTargetingDiagnostics()
        {
            List<string> lines = new List<string>
            {
                "SoulTape Targeting Diagnostic",
                "Camera source: " + _cameraSelectionSource,
                "Camera.main audit: " + _mainCameraAudit +
                "  active cameras=" + _activeCameraCount
            };

            Camera camera = _gameplayCamera;
            if (camera == null)
            {
                lines.Add("Camera: <none; Player.CameraPosition fallback if available>");
            }
            else
            {
                Rect rect = camera.rect;
                Rect pixelRect = camera.pixelRect;
                lines.Add(
                    "Camera: " + camera.name + "  id=" + camera.GetInstanceID() +
                    "  tag=" + camera.tag);
                lines.Add(
                    "enabled=" + camera.enabled +
                    "  active=" + camera.gameObject.activeInHierarchy +
                    "  targetTexture=" + (camera.targetTexture == null ? "no" : "yes"));
                lines.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "FOV={0:F2}  depth={1:F2}  rect=({2:F2},{3:F2},{4:F2},{5:F2})",
                    camera.fieldOfView,
                    camera.depth,
                    rect.x,
                    rect.y,
                    rect.width,
                    rect.height));
                lines.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "pixelRect=({0:F0},{1:F0},{2:F0},{3:F0})",
                    pixelRect.x,
                    pixelRect.y,
                    pixelRect.width,
                    pixelRect.height));
            }

            PickupEvaluation evaluation = _nearestPickupEvaluation;
            if (evaluation == null || evaluation.Pickup == null)
            {
                lines.Add("Nearest-to-center cassette: <none>");
            }
            else
            {
                lines.Add(
                    "Nearest-to-center: " + evaluation.Pickup.Cassette.Artist +
                    " - " + evaluation.Pickup.Cassette.Title);
                string viewport = float.IsNaN(evaluation.Viewport.x)
                    ? "n/a"
                    : string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:F3}/{1:F3}",
                        evaluation.Viewport.x,
                        evaluation.Viewport.y);
                lines.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "distance={0:F2}m  viewport={1}  angle={2:F2}deg",
                    evaluation.Distance,
                    viewport,
                    evaluation.Angle));
                lines.Add(
                    "inside acquire=" + YesNo(evaluation.InsideAcquireCone) +
                    "  allowed=" + evaluation.AllowedAngle.ToString("F1", CultureInfo.InvariantCulture) +
                    "deg  LOS clear=" + YesNo(evaluation.LineOfSightClear));
                lines.Add(
                    "in front=" + YesNo(evaluation.InFront) +
                    "  in viewport=" + YesNo(evaluation.InViewport) +
                    "  in range=" + YesNo(evaluation.WithinDistance) +
                    "  final target=" + YesNo(evaluation.IsFinalTarget));
                if (evaluation.VisibilitySamplesTested > 0)
                {
                    lines.Add(
                        "visibility samples tested=" + evaluation.VisibilitySamplesTested +
                        "  sample=" + (evaluation.VisibilitySampleName ?? "<none>"));
                }
                if (!evaluation.LineOfSightClear && evaluation.Blocker != null)
                {
                    BlockingHitDiagnostic blocker = evaluation.Blocker;
                    lines.Add(
                        "blocker sample=" + blocker.SampleName +
                        "  object=" + blocker.GameObjectName +
                        "  collider=" + blocker.ColliderType);
                    lines.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "layer={0} ({1})  hit={2:F2}m  focus={3:F2}m  parent={4}  root={5}",
                        blocker.Layer,
                        string.IsNullOrWhiteSpace(blocker.LayerName)
                            ? "<unnamed>"
                            : blocker.LayerName,
                        blocker.HitDistance,
                        blocker.FocusDistance,
                        blocker.ParentName,
                        blocker.RootName));
                }
            }

            const float width = 760f;
            float height = 24f + lines.Count * 19f;
            GUI.Box(new Rect(16f, 126f, width, height), GUIContent.none);
            GUI.Label(
                new Rect(28f, 134f, width - 24f, height - 12f),
                string.Join("\n", lines.ToArray()),
                _diagnosticStyle);
        }

        private static string YesNo(bool value)
        {
            return value ? "YES" : "NO";
        }

        private void EnsureInteractionEyeTexture()
        {
            if (_interactionEyeTexture != null)
            {
                return;
            }

            _interactionEyeTexture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            _interactionEyeTexture.name = "SoulTape Interaction Eye";
            _interactionEyeTexture.filterMode = FilterMode.Bilinear;
            _interactionEyeTexture.wrapMode = TextureWrapMode.Clamp;
            Color transparent = new Color(1f, 1f, 1f, 0f);
            Color white = new Color(1f, 1f, 1f, 0.95f);
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    float dx = x - 3.5f;
                    float dy = y - 3.5f;
                    _interactionEyeTexture.SetPixel(
                        x,
                        y,
                        dx * dx + dy * dy <= 8.5f ? white : transparent);
                }
            }
            _interactionEyeTexture.Apply(false, true);
        }

        private static bool ShortcutPressed(BepInEx.Configuration.KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None || !Input.GetKeyDown(shortcut.MainKey))
            {
                return false;
            }

            return shortcut.Modifiers.All(Input.GetKey);
        }

        private void EndRaid(string reason)
        {
            foreach (SoulTapeWorldPickup pickup in _pickups)
            {
                if (pickup != null)
                {
                    Destroy(pickup.gameObject);
                }
            }
            _pickups.Clear();
            _targetedPickup = null;
            _nearestPickupEvaluation = null;
            _activeWorld = null;
            _gameplayCamera = null;
            _cameraSelectionSource = "unavailable";
            _mainCameraAudit = "not evaluated";
            _activeCameraCount = 0;
            _spawnPending = false;
            _spawnCompleted = false;
            _notificationTitle = string.Empty;
            _notificationDetail = string.Empty;
            if (_log != null && !string.IsNullOrWhiteSpace(reason))
            {
                _log.Info("SoulTape World Discovery cleaned up: " + reason + ".");
            }
        }

        private void OnDestroy()
        {
            if (_catalog != null)
            {
                _catalog.Changed -= OnCatalogChanged;
            }
            if (_discovery != null)
            {
                _discovery.Discovered -= OnDiscovered;
            }
            if (_interactionEyeTexture != null)
            {
                Destroy(_interactionEyeTexture);
                _interactionEyeTexture = null;
            }
            EndRaid("controller destroyed");
        }

        private sealed class PickupEvaluation
        {
            internal SoulTapeWorldPickup Pickup { get; set; }
            internal float Distance { get; set; }
            internal Vector3 Viewport { get; set; }
            internal float Angle { get; set; }
            internal float AllowedAngle { get; set; }
            internal bool IsCurrentTarget { get; set; }
            internal bool WithinDistance { get; set; }
            internal bool InFront { get; set; }
            internal bool InViewport { get; set; }
            internal bool InsideAcquireCone { get; set; }
            internal bool InsideAllowedCone { get; set; }
            internal bool LineOfSightClear { get; set; }
            internal bool IsTargetable { get; set; }
            internal bool IsFinalTarget { get; set; }
            internal int VisibilitySamplesTested { get; set; }
            internal string VisibilitySampleName { get; set; }
            internal BlockingHitDiagnostic Blocker { get; set; }
        }

        private sealed class LineOfSightEvaluation
        {
            internal bool IsClear { get; set; }
            internal int SamplesTested { get; set; }
            internal string SampleName { get; set; }
            internal BlockingHitDiagnostic Blocker { get; set; }
        }

        private sealed class BlockingHitDiagnostic
        {
            internal string SampleName { get; set; }
            internal string GameObjectName { get; set; }
            internal string ColliderType { get; set; }
            internal int Layer { get; set; }
            internal string LayerName { get; set; }
            internal float HitDistance { get; set; }
            internal float FocusDistance { get; set; }
            internal string ParentName { get; set; }
            internal string RootName { get; set; }
        }
    }
}
