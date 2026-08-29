using System;
using System.Collections.Generic;
using SoulPlayer.Library;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SoulPlayer.UI
{
    internal sealed class SoulMiniPlayer : MonoBehaviour
    {
        private const string ObjectName = "SoulMiniPlayer";
        private const float MarqueeSpeed = 28f;
        private static readonly List<SoulMiniPlayer> Instances = new List<SoulMiniPlayer>();
        private static bool _inRaid;

        private TMP_Text _styleSource;
        private CanvasGroup _canvasGroup;
        private RectTransform _marqueeViewport;
        private TextMeshProUGUI _marqueeText;
        private TextMeshProUGUI _elapsedLabel;
        private TextMeshProUGUI _remainingLabel;
        private Slider _progress;
        private Button _playButton;
        private bool _updatingProgress;
        private bool _dirty = true;
        private float _nextRefresh;
        private string _marqueeValue = string.Empty;
        private float _marqueeOffset;
        private float _marqueeOverflow;
        private float _marqueePauseUntil;
        private int _marqueeDirection = 1;

        internal static SoulMiniPlayer Create(Transform parent, TMP_Text styleSource)
        {
            Transform existing = parent.Find(ObjectName);
            if (existing != null)
            {
                SoulMiniPlayer current = existing.GetComponent<SoulMiniPlayer>();
                if (current != null)
                {
                    current.ApplyVisibility();
                    return current;
                }
            }

            GameObject root = UIUtils.CreateUIObject(parent.gameObject, ObjectName);
            SoulMiniPlayer mini = root.AddComponent<SoulMiniPlayer>();
            mini._styleSource = styleSource;
            mini.Build();
            root.transform.SetAsLastSibling();
            Plugin.Log.LogInfo("SoulPlayer compact mini player created.");
            return mini;
        }

        internal static void RefreshVisibility()
        {
            Instances.RemoveAll(instance => instance == null);
            foreach (SoulMiniPlayer instance in Instances)
            {
                instance.ApplyVisibility();
                instance._dirty = true;
            }
        }

        internal static void SetRaidMode(bool inRaid)
        {
            _inRaid = inRaid;
            Instances.RemoveAll(instance => instance == null);
            foreach (SoulMiniPlayer instance in Instances)
            {
                instance.ApplyVisibility();
                if (!inRaid)
                {
                    instance._dirty = true;
                }
            }
        }

        internal static bool TryGetScreenRect(
            out SoulPlayerVolumeHudRect screenRect)
        {
            Instances.RemoveAll(instance => instance == null);
            foreach (SoulMiniPlayer instance in Instances)
            {
                if (!instance.gameObject.activeInHierarchy ||
                    instance._canvasGroup == null ||
                    instance._canvasGroup.alpha <= 0.001f)
                {
                    continue;
                }

                Vector3[] corners = new Vector3[4];
                ((RectTransform)instance.transform).GetWorldCorners(corners);
                screenRect = new SoulPlayerVolumeHudRect
                {
                    X = corners[0].x,
                    Y = Screen.height - corners[2].y,
                    Width = corners[2].x - corners[0].x,
                    Height = corners[2].y - corners[0].y
                };
                return screenRect.Width > 0f && screenRect.Height > 0f;
            }

            screenRect = new SoulPlayerVolumeHudRect();
            return false;
        }

        private void Awake()
        {
            Instances.Add(this);
        }

        private void Build()
        {
            UIUtils.SetRect(
                (RectTransform)transform,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(380f, 44f),
                new Vector2(-12f, 36f));

            Image background = gameObject.AddComponent<Image>();
            background.color = new Color(0.008f, 0.010f, 0.012f, 0.52f);

            _canvasGroup = gameObject.AddComponent<CanvasGroup>();

            GameObject viewport = UIUtils.CreateUIObject(gameObject, "MarqueeViewport");
            _marqueeViewport = (RectTransform)viewport.transform;
            UIUtils.SetRect(
                _marqueeViewport,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(240f, 20f),
                new Vector2(10f, -2f));
            viewport.AddComponent<RectMask2D>();

            Image marqueeHitArea = viewport.AddComponent<Image>();
            marqueeHitArea.color = new Color(1f, 1f, 1f, 0.001f);
            Button openButton = viewport.AddComponent<Button>();
            openButton.targetGraphic = marqueeHitArea;
            openButton.transition = Selectable.Transition.None;
            openButton.navigation = new Navigation { mode = Navigation.Mode.None };
            openButton.onClick.AddListener(OpenFullPlayer);

            _marqueeText = UIUtils.CreateLabel(
                viewport,
                "RollingTitle",
                "Nothing playing",
                _styleSource,
                12.5f,
                UIUtils.Text,
                TextAlignmentOptions.MidlineLeft,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(240f, 20f),
                Vector2.zero);

            _elapsedLabel = UIUtils.CreateLabel(
                gameObject,
                "ElapsedTime",
                "0:00",
                _styleSource,
                8.5f,
                UIUtils.MutedText,
                TextAlignmentOptions.BottomLeft,
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(48f, 10f),
                new Vector2(10f, 0f));

            _remainingLabel = UIUtils.CreateLabel(
                gameObject,
                "RemainingTime",
                "-0:00",
                _styleSource,
                8.5f,
                UIUtils.MutedText,
                TextAlignmentOptions.BottomRight,
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(48f, 10f),
                new Vector2(202f, 0f));

            _progress = UIUtils.CreateSlider(
                gameObject,
                "Progress",
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(240f, 7f),
                new Vector2(10f, 10f),
                0f);
            _progress.onValueChanged.AddListener(OnProgressChanged);
            if (_progress.handleRect != null)
            {
                _progress.handleRect.gameObject.SetActive(false);
            }

            UIUtils.CreateTransportButton(
                gameObject,
                "Previous",
                TransportIcon.Previous,
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(24f, 24f),
                new Vector2(-82f, 0f),
                Plugin.AudioPlayer.Previous);

            _playButton = UIUtils.CreateTransportButton(
                gameObject,
                "PlayPause",
                TransportIcon.PlayPause,
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(30f, 30f),
                new Vector2(-48f, 0f),
                Plugin.AudioPlayer.TogglePause);

            UIUtils.CreateTransportButton(
                gameObject,
                "Next",
                TransportIcon.Next,
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(24f, 24f),
                new Vector2(-14f, 0f),
                Plugin.AudioPlayer.Next);

            Plugin.AudioPlayer.Changed += OnAudioChanged;
            ApplyVisibility();
            RefreshContent();
        }

        private void Update()
        {
            if (_dirty || Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 0.2f;
                RefreshContent();
                _dirty = false;
            }

            UpdateMarquee();
        }

        private void RefreshContent()
        {
            MusicTrack track = Plugin.AudioPlayer.DisplayTrack;
            string marquee;
            if (track == null)
            {
                marquee = Plugin.MusicLibrary.IsScanning
                    ? "Scanning music library..."
                    : "Nothing playing  •  press play to shuffle your library";
            }
            else
            {
                marquee = Plugin.AudioPlayer.IsLoading
                    ? "Loading " + track.Extension + "  •  " + track.Title
                    : track.Title + "  •  " + track.Artist;
            }

            SetMarquee(marquee);
            UIUtils.SetPlayPauseState(_playButton, Plugin.AudioPlayer.IsPlaying);

            float duration = Plugin.AudioPlayer.Duration;
            float current = Plugin.AudioPlayer.CurrentTime;
            _updatingProgress = true;
            _progress.value = duration > 0.01f ? current / duration : 0f;
            _updatingProgress = false;
            _elapsedLabel.text = FormatTime(current);
            _remainingLabel.text = "-" + FormatTime(Math.Max(0f, duration - current));
        }

        private void SetMarquee(string value)
        {
            if (string.Equals(_marqueeValue, value, StringComparison.Ordinal))
            {
                return;
            }

            _marqueeValue = value;
            _marqueeText.text = value;
            _marqueeText.ForceMeshUpdate();
            float width = Math.Max(_marqueeViewport.rect.width, _marqueeText.preferredWidth + 10f);
            RectTransform textRect = (RectTransform)_marqueeText.transform;
            UIUtils.SetRect(
                textRect,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(width, _marqueeViewport.rect.height),
                Vector2.zero);

            _marqueeOverflow = Math.Max(0f, width - _marqueeViewport.rect.width);
            _marqueeOffset = 0f;
            _marqueeDirection = 1;
            _marqueePauseUntil = Time.unscaledTime + 1.4f;
        }

        private void UpdateMarquee()
        {
            if (_marqueeOverflow <= 0.5f || Time.unscaledTime < _marqueePauseUntil)
            {
                return;
            }

            _marqueeOffset += MarqueeSpeed * Time.unscaledDeltaTime * _marqueeDirection;
            if (_marqueeOffset >= _marqueeOverflow)
            {
                _marqueeOffset = _marqueeOverflow;
                _marqueeDirection = -1;
                _marqueePauseUntil = Time.unscaledTime + 1.2f;
            }
            else if (_marqueeOffset <= 0f)
            {
                _marqueeOffset = 0f;
                _marqueeDirection = 1;
                _marqueePauseUntil = Time.unscaledTime + 1.4f;
            }

            ((RectTransform)_marqueeText.transform).anchoredPosition =
                new Vector2(-_marqueeOffset, 0f);
        }

        private void ApplyVisibility()
        {
            bool visible = !_inRaid && Plugin.Settings != null && Plugin.Settings.ShowMiniPlayer;
            gameObject.SetActive(visible);
            _canvasGroup.alpha = visible ? 1f : 0f;
            _canvasGroup.interactable = visible;
            _canvasGroup.blocksRaycasts = visible;
        }

        private void OpenFullPlayer()
        {
            Transform windowTransform = transform.parent.Find("SoulPlayerWindow");
            if (windowTransform == null)
            {
                return;
            }

            SoulPlayerWindow window = windowTransform.GetComponent<SoulPlayerWindow>();
            if (window != null && !window.gameObject.activeSelf)
            {
                window.Toggle();
            }
        }

        private void OnProgressChanged(float value)
        {
            if (!_updatingProgress)
            {
                Plugin.AudioPlayer.SetProgress(value);
            }
        }

        private void OnAudioChanged()
        {
            _dirty = true;
        }

        private void OnDestroy()
        {
            Instances.Remove(this);
            if (Plugin.AudioPlayer != null)
            {
                Plugin.AudioPlayer.Changed -= OnAudioChanged;
            }
        }

        private static string FormatTime(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f)
            {
                seconds = 0f;
            }

            TimeSpan value = TimeSpan.FromSeconds(seconds);
            return value.TotalHours >= 1d
                ? ((int)value.TotalHours) + ":" + value.Minutes.ToString("00") + ":" + value.Seconds.ToString("00")
                : value.Minutes + ":" + value.Seconds.ToString("00");
        }
    }
}
