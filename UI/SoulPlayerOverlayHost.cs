using SoulPlayer.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SoulPlayer.UI
{
    internal sealed class SoulPlayerOverlayHost : MonoBehaviour
    {
        private const string ObjectName = "SoulPlayerPersistentOverlay";
        private Canvas _canvas;
        private GraphicRaycaster _raycaster;
        private SoulPlayerWindow _window;
        private bool _wasInRaid;
        private float _nextStateCheck;

        internal static SoulPlayerOverlayHost Instance { get; private set; }

        internal bool CapturesKeyboardInput
        {
            get { return _window != null && _window.CapturesKeyboardInput; }
        }

        internal static SoulPlayerOverlayHost Create(TMP_Text styleSource)
        {
            if (Instance != null)
            {
                return Instance;
            }

            GameObject root = new GameObject(
                ObjectName,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));

            if (styleSource != null)
            {
                root.layer = styleSource.gameObject.layer;
            }

            DontDestroyOnLoad(root);

            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 5000;

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            SoulPlayerOverlayHost host = root.AddComponent<SoulPlayerOverlayHost>();
            host._canvas = canvas;
            host._raycaster = root.GetComponent<GraphicRaycaster>();
            host.Build(styleSource);
            Instance = host;

            Plugin.Log.LogInfo("SoulPlayer persistent overlay created.");
            return host;
        }

        internal void ToggleWindow()
        {
            if (_window != null && !GameState.ShouldSuspendMenuMusic())
            {
                _window.Toggle();
            }
        }

        private void Build(TMP_Text styleSource)
        {
            _window = SoulPlayerWindow.Create(transform, styleSource);
            SoulMiniPlayer.Create(transform, styleSource);
            ApplyRaidState(GameState.ShouldSuspendMenuMusic());
        }

        private void Update()
        {
#if SOULPLAYER_PERF
            SoulPlayer.Utils.RecurringWorkProfiler.Begin(SoulPlayer.Utils.RecurringWorkArea.Overlay);
            try
            {
#endif
            if (Time.unscaledTime < _nextStateCheck)
            {
                return;
            }

            _nextStateCheck = Time.unscaledTime + 0.35f;
            bool inRaid = GameState.ShouldSuspendMenuMusic();
            if (inRaid != _wasInRaid)
            {
                ApplyRaidState(inRaid);
            }
        #if SOULPLAYER_PERF
            }
            finally { SoulPlayer.Utils.RecurringWorkProfiler.End(SoulPlayer.Utils.RecurringWorkArea.Overlay); }
#endif
        }

        private void ApplyRaidState(bool inRaid)
        {
            _wasInRaid = inRaid;
            if (inRaid && _window != null && _window.gameObject.activeSelf)
            {
                _window.Close();
            }

            // A disabled Canvas still allows child MonoBehaviours to Update.
            // Explicitly deactivate the mini player so its text, slider and
            // marquee do no work while the overlay is hidden during a raid.
            SoulMiniPlayer.SetRaidMode(inRaid);

            if (_canvas != null)
            {
                _canvas.enabled = !inRaid;
            }

            if (_raycaster != null)
            {
                _raycaster.enabled = !inRaid;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
