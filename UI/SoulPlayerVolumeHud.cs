using SoulPlayer.Configuration;
using SoulPlayer.Recorder;
using SoulPlayer.Utils;
using UnityEngine;

namespace SoulPlayer.UI
{
    internal sealed class SoulPlayerVolumeHud : MonoBehaviour
    {
        private const int SegmentCount = 4;

        private SoulPlayerSettings _settings;
        private readonly SoulPlayerVolumeHudAnimation _animation =
            new SoulPlayerVolumeHudAnimation();
        private GUIStyle _panelStyle;
        private GUIStyle _glyphStyle;
        private GUIStyle _headingStyle;
        private GUIStyle _valueStyle;
        private Texture2D _panelTexture;
        private Texture2D _pixelTexture;
        private readonly SoulOverlayLayoutRevision _layoutRevision = new SoulOverlayLayoutRevision();
        private SoulPlayerVolumeHudLayoutResult _layout;

        internal void Initialize(SoulPlayerSettings settings)
        {
            if (_settings != null)
            {
                _settings.VolumeChanged -= OnVolumeChanged;
            }
            _settings = settings;
            if (_settings != null)
            {
                _settings.VolumeChanged += OnVolumeChanged;
            }
        }

        private void OnVolumeChanged(float volume)
        {
            _animation.Show(volume, Time.unscaledTime);
        }

        private void OnGUI()
        {
#if SOULPLAYER_PERF
            SoulPlayer.Utils.RecurringWorkProfiler.Begin(SoulPlayer.Utils.RecurringWorkArea.Overlay);
            try
            {
#endif
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            SoulPlayerVolumeHudFrame frame =
                _animation.Sample(Time.unscaledTime);
            if (frame.Phase == SoulPlayerVolumeHudPhase.Hidden ||
                frame.Alpha <= 0.001f)
            {
                return;
            }

            EnsureStyles();
            float now = Time.unscaledTime;
            SoulPlayerVolumeHudPlacementContext placement =
                BuildPlacementContext(_settings, now, true);
            if (_layoutRevision.ShouldRebuild(true, Screen.width, Screen.height, 1f, 0, placement))
                _layout = SoulPlayerVolumeHudLayout.Calculate(Screen.width, Screen.height, placement);
            SoulPlayerVolumeHudLayoutResult layout = _layout;
            float slide = frame.SlidePixels * layout.Scale;
            Color previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, frame.Alpha);

            GUI.Box(ToRect(layout.Panel, slide), GUIContent.none, _panelStyle);
            GUI.Label(ToRect(layout.Glyph, slide),
                frame.Volume <= 0.001f ? "MUTE" : "VOL", _glyphStyle);
            GUI.Label(ToRect(layout.Heading, slide), "VOLUME", _headingStyle);
            GUI.Label(ToRect(layout.Value, slide),
                SoulPlayerVolumeHudLayout.VolumeText(frame.Volume), _valueStyle);
            DrawBar(layout.Bar, slide, frame.Volume, layout.Scale);

            GUI.color = previous;
        #if SOULPLAYER_PERF
            }
            finally { SoulPlayer.Utils.RecurringWorkProfiler.End(SoulPlayer.Utils.RecurringWorkArea.Overlay); }
#endif
        }

        internal static SoulPlayerVolumeHudPlacementContext BuildPlacementContext(
            SoulPlayerSettings settings, float now, bool includeMini)
        {
            SoulPlayerVolumeHudPlacementContext context =
                new SoulPlayerVolumeHudPlacementContext
                {
                    IsInRaid = GameState.IsInRaid()
                };

            SoulPlayerVolumeHudRect miniPlayer;
            if (includeMini && SoulMiniPlayer.TryGetScreenRect(out miniPlayer))
            {
                context.MiniPlayerVisible = true;
                context.MiniPlayer = miniPlayer;
            }

            if (Plugin.RecorderController != null &&
                Plugin.RecorderController.IsActive &&
                settings != null && settings.RecorderOverlayEnabled)
            {
                SoulRecorderOverlayPose recorder =
                    SoulRecorderOverlayTimeline.SampleInsertion(
                        Screen.width,
                        Screen.height,
                        SoulRecorderOverlayTimeline.InsertionSeconds(
                            settings.RecorderOverlaySettings.AnimationSpeed),
                        settings.RecorderOverlaySettings);
                context.RecorderOverlayVisible = true;
                context.RecorderOverlay = new SoulPlayerVolumeHudRect
                {
                    X = recorder.Recorder.X,
                    Y = recorder.Recorder.Y,
                    Width = recorder.Recorder.Width,
                    Height = recorder.Recorder.Height
                };
            }

            if (Plugin.RecorderController != null)
            {
                SoulPlayerVolumeHudRect status;
                if (Plugin.RecorderController.TryGetStatusScreenRect(now, out status))
                {
                    context.RecorderStatusVisible = true;
                    context.RecorderStatus = status;
                }
            }

            if (Plugin.WorldDiscoveryController != null &&
                Plugin.WorldDiscoveryController.IsNotificationVisible(now))
            {
                SoulRecorderStatusOverlayLayoutResult discovery =
                    SoulRecorderStatusOverlayLayout.Calculate(
                        Screen.width, Screen.height, 0.18f);
                context.DiscoveryOverlayVisible = true;
                context.DiscoveryOverlay = new SoulPlayerVolumeHudRect
                {
                    X = discovery.Panel.X,
                    Y = discovery.Panel.Y,
                    Width = discovery.Panel.Width,
                    Height = discovery.Panel.Height
                };
            }

            return context;
        }

        private void DrawBar(
            SoulPlayerVolumeHudRect bar,
            float slide,
            float volume,
            float scale)
        {
            float gap = 4f * scale;
            float width = (bar.Width - gap * (SegmentCount - 1)) /
                SegmentCount;
            int activeSegments = Mathf.CeilToInt(
                Mathf.Clamp01(volume) * SegmentCount - 0.001f);

            for (int i = 0; i < SegmentCount; i++)
            {
                Rect segment = new Rect(
                    bar.X + (width + gap) * i,
                    bar.Y + slide,
                    width,
                    bar.Height);
                DrawTintedRect(segment,
                    i < activeSegments
                        ? new Color(0.40f, 0.88f, 0.83f, 0.94f)
                        : new Color(0.24f, 0.36f, 0.37f, 0.58f));
            }
        }

        private void EnsureStyles()
        {
            if (_panelStyle != null)
            {
                return;
            }

            _panelTexture = CreateRoundedPanelTexture();
            _panelStyle = new GUIStyle
            {
                normal = { background = _panelTexture },
                border = new RectOffset(12, 12, 12, 12)
            };
            _glyphStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 9,
                fontStyle = FontStyle.Bold
            };
            _glyphStyle.normal.textColor =
                new Color(0.49f, 0.82f, 0.78f, 1f);
            _headingStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 11,
                fontStyle = FontStyle.Bold
            };
            _headingStyle.normal.textColor =
                new Color(0.62f, 0.73f, 0.72f, 1f);
            _valueStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleRight,
                fontSize = 16,
                fontStyle = FontStyle.Normal
            };
            _valueStyle.normal.textColor =
                new Color(0.95f, 0.98f, 0.98f, 1f);

            _pixelTexture = new Texture2D(1, 1,
                TextureFormat.RGBA32, false, false)
            {
                name = "SoulPlayer Volume HUD Pixel",
                hideFlags = HideFlags.HideAndDontSave
            };
            _pixelTexture.SetPixel(0, 0, Color.white);
            _pixelTexture.Apply(false, true);
        }

        private void DrawTintedRect(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = new Color(
                color.r, color.g, color.b, color.a * previous.a);
            GUI.DrawTexture(rect, _pixelTexture,
                ScaleMode.StretchToFill, false);
            GUI.color = previous;
        }

        private static Rect ToRect(
            SoulPlayerVolumeHudRect value,
            float slide)
        {
            return new Rect(
                value.X,
                value.Y + slide,
                value.Width,
                value.Height);
        }

        private static Texture2D CreateRoundedPanelTexture()
        {
            const int size = 32;
            const float radius = 8f;
            Texture2D texture = new Texture2D(
                size, size, TextureFormat.RGBA32, false, false)
            {
                name = "SoulPlayer Volume HUD Rounded Panel",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            Color fill = new Color(0.018f, 0.027f, 0.030f, 0.92f);
            Color border = new Color(0.25f, 0.50f, 0.48f, 0.62f);
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

        private void OnDestroy()
        {
            if (_settings != null)
            {
                _settings.VolumeChanged -= OnVolumeChanged;
            }
            if (_panelTexture != null)
            {
                Destroy(_panelTexture);
            }
            if (_pixelTexture != null)
            {
                Destroy(_pixelTexture);
            }
        }
    }
}
