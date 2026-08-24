using System;
using System.Collections.Generic;
using EFT;
using SoulPlayer.Library;
using SoulPlayer.World;
using UnityEngine;

namespace SoulPlayer.Recorder
{
    /// <summary>
    /// Temporary SoulPlayer-owned first-person recorder presentation. The model,
    /// cassette, materials, and animation are all procedural and can be replaced
    /// without changing the usable-item controller.
    /// </summary>
    internal sealed class ProceduralSoulRecorderHandsView : MonoBehaviour, ISoulRecorderHandsView
    {
        private readonly SoulRecorderPresentationState _state =
            new SoulRecorderPresentationState();
        private readonly List<Material> _ownedMaterials = new List<Material>();

        private Player _player;
        private GameObject _root;
        private SoulTapeCassetteVisual _cassette;
        private Material _statusLedMaterial;
        private bool _usingHeadlessFallback;
        private bool _constructionFailureLogged;

        public float TapeInsertionSeconds
        {
            get
            {
                return _usingHeadlessFallback
                    ? HeadlessSoulRecorderHandsView.Instance.TapeInsertionSeconds
                    : SoulRecorderPresentationTuning.TapeInsertionSeconds;
            }
        }

        public float TapeEjectionSeconds
        {
            get
            {
                return _usingHeadlessFallback
                    ? HeadlessSoulRecorderHandsView.Instance.TapeEjectionSeconds
                    : _state == null
                        ? SoulRecorderPresentationTuning.TapeEjectionSeconds
                        : _state.EjectionTotalSeconds;
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
            _root.SetActive(false);
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
            if (_cassette != null)
            {
                _cassette.gameObject.SetActive(true);
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
            ApplyCassetteTransform(1f);
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
        }

        public void OnTapeEjected(MusicTrack tape)
        {
            if (_usingHeadlessFallback)
            {
                HeadlessSoulRecorderHandsView.Instance.OnTapeEjected(tape);
                return;
            }

            _state.HideCassette();
            if (_cassette != null)
            {
                _cassette.gameObject.SetActive(false);
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
            ApplyStatusLed(false);
            if (_cassette != null)
            {
                _cassette.gameObject.SetActive(false);
            }
            if (_root != null)
            {
                _root.SetActive(false);
            }
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
                _root.SetActive(false);
                return;
            }

            if (_root.transform.parent != view)
            {
                _root.transform.SetParent(view, false);
            }

            _root.SetActive(true);
            ApplyRecorderTransform(now);
            ApplyCassetteAnimation(now);
            AnimateReels();
        }

        private void ApplyRecorderTransform(float now)
        {
            float loweredAmount = _state.GetLoweredAmount(now);
            if (_state.IsExiting)
            {
                float progress = SoulRecorderPresentationState.Smooth(_state.GetExitProgress(now));
                if (progress >= 1f)
                {
                    _state.CompleteExit();
                    _root.SetActive(false);
                    return;
                }
            }

            _root.transform.localPosition =
                SoulRecorderPresentationTuning.HeldPosition +
                (SoulRecorderPresentationTuning.LoweredOffset * loweredAmount);
            Quaternion held = Quaternion.Euler(SoulRecorderPresentationTuning.HeldRotationEuler);
            Quaternion lowered = Quaternion.Euler(
                SoulRecorderPresentationTuning.HeldRotationEuler + new Vector3(15f, 2f, 7f));
            _root.transform.localRotation = Quaternion.Slerp(held, lowered, loweredAmount);
            _root.transform.localScale = SoulRecorderPresentationTuning.RecorderScale;
        }

        private void ApplyCassetteAnimation(float now)
        {
            if (_cassette == null)
            {
                return;
            }

            switch (_state.CassetteState)
            {
                case SoulRecorderCassetteVisualState.Hidden:
                    _cassette.gameObject.SetActive(false);
                    break;
                case SoulRecorderCassetteVisualState.Inserting:
                    _cassette.gameObject.SetActive(true);
                    ApplyCassetteTransform(
                        SoulRecorderPresentationState.Smooth(_state.GetInsertionProgress(now)));
                    break;
                case SoulRecorderCassetteVisualState.Seated:
                    _cassette.gameObject.SetActive(true);
                    ApplyCassetteTransform(1f);
                    break;
                case SoulRecorderCassetteVisualState.Ejecting:
                    _cassette.gameObject.SetActive(true);
                    ApplyCassetteTransform(_state.GetEjectionCassetteTravel(now));
                    break;
            }
        }

        private void ApplyCassetteTransform(float progress)
        {
            if (_cassette == null)
            {
                return;
            }

            _cassette.transform.localPosition = Vector3.Lerp(
                SoulRecorderPresentationTuning.CassetteInsertionStartPosition,
                SoulRecorderPresentationTuning.CassetteInsertionEndPosition,
                progress);
            _cassette.transform.localRotation = Quaternion.Slerp(
                Quaternion.Euler(SoulRecorderPresentationTuning.CassetteInsertionStartRotationEuler),
                Quaternion.Euler(SoulRecorderPresentationTuning.CassetteInsertionEndRotationEuler),
                progress);
        }

        private void AnimateReels()
        {
            if (!_state.IsPlaying || _cassette == null ||
                _state.PoseState == SoulRecorderVisualPoseState.Lowered)
            {
                return;
            }

            float degrees = SoulRecorderPresentationTuning.ReelDegreesPerSecond *
                            Time.unscaledDeltaTime;
            if (_cassette.LeftReel != null)
            {
                _cassette.LeftReel.Rotate(Vector3.up, degrees, Space.Self);
            }
            if (_cassette.RightReel != null)
            {
                _cassette.RightReel.Rotate(Vector3.up, -degrees, Space.Self);
            }
        }

        private bool EnsureModel()
        {
            if (_root != null)
            {
                return true;
            }

            DestroyOwnedMaterials();
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
                BuildModel(shader);
                return _root != null;
            }
            catch (Exception ex)
            {
                DestroyModel();
                LogConstructionFailure(ex.Message);
                return false;
            }
        }

        private void BuildModel(Shader shader)
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

            _cassette = SoulTapeCassetteVisual.Create(
                _root.transform,
                shader,
                "Inserted SoulTape cassette");
            if (_cassette == null)
            {
                throw new InvalidOperationException("procedural cassette construction returned no visual");
            }

            _cassette.gameObject.SetActive(false);
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
            if (_root != null)
            {
                Destroy(_root);
            }

            _root = null;
            _cassette = null;
            _statusLedMaterial = null;
            DestroyOwnedMaterials();
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
