using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SoulPlayer.Library;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SoulPlayer.UI
{
    internal sealed class SoulPlayerWindow : MonoBehaviour
    {
        private const string ObjectName = "SoulPlayerWindow";
        private const int TracksPerPage = 8;

        private readonly List<GameObject> _trackRows = new List<GameObject>();
        private readonly List<GameObject> _folderRows = new List<GameObject>();
        private TMP_Text _styleSource;
        private GameObject _panel;
        private GameObject _trackArea;
        private GameObject _folderArea;
        private GameObject _folderModal;
        private GameObject _postRaidModal;
        private TMP_InputField _searchInput;
        private TMP_InputField _folderInput;
        private TMP_InputField _survivedFolderInput;
        private TMP_InputField _deathFolderInput;
        private TextMeshProUGUI _libraryCount;
        private TextMeshProUGUI _folderCount;
        private TextMeshProUGUI _pageLabel;
        private TextMeshProUGUI _statusLabel;
        private TextMeshProUGUI _modalStatus;
        private TextMeshProUGUI _nowTitle;
        private TextMeshProUGUI _nowArtist;
        private TextMeshProUGUI _timeLabel;
        private Button _playButton;
        private TextMeshProUGUI _shuffleLabel;
        private TextMeshProUGUI _repeatLabel;
        private TextMeshProUGUI _miniPlayerLabel;
        private TextMeshProUGUI _autoRaidLabel;
        private TextMeshProUGUI _modalAutoRaidLabel;
        private TextMeshProUGUI _postRaidStatus;
        private Slider _progressSlider;
        private Slider _volumeSlider;
        private List<MusicTrack> _filteredTracks = new List<MusicTrack>();
        private int _page;
        private bool _audioDirty = true;
        private bool _libraryDirty = true;
        private bool _updatingProgress;

        internal static SoulPlayerWindow Create(Transform parent, TMP_Text styleSource)
        {
            Transform existing = parent.Find(ObjectName);
            if (existing != null)
            {
                SoulPlayerWindow existingWindow = existing.GetComponent<SoulPlayerWindow>();
                if (existingWindow != null)
                {
                    return existingWindow;
                }
            }

            GameObject root = UIUtils.CreateUIObject(parent.gameObject, ObjectName);
            RectTransform rootRect = (RectTransform)root.transform;
            UIUtils.Stretch(rootRect, 0f, 0f, 0f, 0f);
            root.SetActive(false);

            Image dimmer = root.AddComponent<Image>();
            dimmer.color = new Color(0f, 0f, 0f, 0.72f);
            dimmer.raycastTarget = true;

            SoulPlayerWindow window = root.AddComponent<SoulPlayerWindow>();
            window._styleSource = styleSource;
            window.Build();

            Plugin.Log.LogInfo("SoulPlayer professional window created.");
            return window;
        }

        internal void Toggle()
        {
            bool show = !gameObject.activeSelf;
            gameObject.SetActive(show);
            if (show)
            {
                transform.SetAsLastSibling();
                _libraryDirty = true;
            }

            Plugin.Log.LogInfo(show ? "SoulPlayer window opened." : "SoulPlayer window closed.");
        }

        internal void Close()
        {
            Hide();
        }

        private void Build()
        {
            _panel = UIUtils.CreatePanel(
                gameObject,
                "PlayerPanel",
                UIUtils.Background,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(1320f, 720f),
                Vector2.zero);

            // Present the full library at a quieter, more compact scale without
            // disturbing the established internal layout and modal coordinates.
            _panel.transform.localScale = new Vector3(0.90f, 0.90f, 1f);

            Outline outline = _panel.AddComponent<Outline>();
            outline.effectColor = new Color(UIUtils.Accent.r, UIUtils.Accent.g, UIUtils.Accent.b, 0.8f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            BuildSidebar();
            BuildLibrary();
            BuildPlayerBar();
            BuildFolderModal();
            BuildPostRaidModal();

            Plugin.AudioPlayer.Changed += OnAudioChanged;
            Plugin.MusicLibrary.Changed += OnLibraryChanged;
        }

        private void BuildSidebar()
        {
            GameObject sidebar = UIUtils.CreatePanel(
                _panel,
                "Sidebar",
                new Color(0.035f, 0.038f, 0.041f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(250f, 628f),
                Vector2.zero);

            UIUtils.CreateLabel(
                sidebar,
                "Brand",
                "SOULPLAYER",
                _styleSource,
                28f,
                UIUtils.Accent,
                TextAlignmentOptions.Left,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(205f, 42f),
                new Vector2(24f, -22f));

            UIUtils.CreateLabel(
                sidebar,
                "Version",
                "LOCAL MUSIC  •  0.6.4",
                _styleSource,
                11f,
                UIUtils.MutedText,
                TextAlignmentOptions.Left,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(205f, 22f),
                new Vector2(25f, -58f));

            GameObject selected = UIUtils.CreatePanel(
                sidebar,
                "LibrarySelected",
                UIUtils.PanelLight,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(218f, 48f),
                new Vector2(16f, -94f));

            UIUtils.CreatePanel(
                selected,
                "Accent",
                UIUtils.Accent,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(3f, 48f),
                Vector2.zero);

            UIUtils.CreateLabel(
                selected,
                "Label",
                "YOUR LIBRARY",
                _styleSource,
                17f,
                UIUtils.Text,
                TextAlignmentOptions.MidlineLeft,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(180f, 42f),
                new Vector2(18f, 0f));

            _libraryCount = UIUtils.CreateLabel(
                sidebar,
                "LibraryCount",
                "SCANNING...",
                _styleSource,
                13f,
                UIUtils.MutedText,
                TextAlignmentOptions.Left,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(205f, 24f),
                new Vector2(25f, -151f));

            UIUtils.CreateLabel(
                sidebar,
                "FolderHeading",
                "MUSIC FOLDERS",
                _styleSource,
                12f,
                UIUtils.MutedText,
                TextAlignmentOptions.Left,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(190f, 24f),
                new Vector2(25f, -201f));

            _folderCount = UIUtils.CreateLabel(
                sidebar,
                "FolderCount",
                "0",
                _styleSource,
                12f,
                UIUtils.MutedText,
                TextAlignmentOptions.Right,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(40f, 24f),
                new Vector2(-22f, -201f));

            _folderArea = UIUtils.CreateUIObject(sidebar, "FolderRows");
            UIUtils.SetRect(
                (RectTransform)_folderArea.transform,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(218f, 250f),
                new Vector2(16f, -230f));

            UIUtils.CreateButton(
                sidebar,
                "AddFolder",
                "+  ADD MUSIC FOLDER",
                _styleSource,
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(218f, 42f),
                new Vector2(16f, 70f),
                ShowFolderModal,
                true,
                14f);

            UIUtils.CreateButton(
                sidebar,
                "Rescan",
                "RESCAN LIBRARY",
                _styleSource,
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(218f, 36f),
                new Vector2(16f, 24f),
                Rescan,
                false,
                13f);
        }

        private void BuildLibrary()
        {
            GameObject main = UIUtils.CreatePanel(
                _panel,
                "Library",
                UIUtils.Background,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(1070f, 628f),
                new Vector2(250f, 0f));

            UIUtils.CreateLabel(
                main,
                "Title",
                "YOUR LIBRARY",
                _styleSource,
                27f,
                UIUtils.Text,
                TextAlignmentOptions.Left,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(430f, 38f),
                new Vector2(30f, -22f));

            UIUtils.CreateLabel(
                main,
                "Subtitle",
                "Everything stays local. Nothing is uploaded.",
                _styleSource,
                13f,
                UIUtils.MutedText,
                TextAlignmentOptions.Left,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(460f, 24f),
                new Vector2(31f, -58f));

            _searchInput = UIUtils.CreateInput(
                main,
                "Search",
                "Search tracks, artists, albums...",
                _styleSource,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(420f, 42f),
                new Vector2(30f, -88f));
            _searchInput.onValueChanged.AddListener(OnSearchChanged);

            Button miniPlayer = UIUtils.CreateButton(
                main,
                "MiniPlayerToggle",
                "MINI ON",
                _styleSource,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(120f, 42f),
                new Vector2(-490f, -22f),
                ToggleMiniPlayer,
                false,
                11f);
            _miniPlayerLabel = miniPlayer.GetComponentInChildren<TextMeshProUGUI>();

            Button autoRaid = UIUtils.CreateButton(
                main,
                "AutoRaidToggle",
                "AUTOPLAY OFF",
                _styleSource,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(120f, 42f),
                new Vector2(-362f, -22f),
                TogglePostRaidAutoplay,
                false,
                11f);
            _autoRaidLabel = autoRaid.GetComponentInChildren<TextMeshProUGUI>();

            UIUtils.CreateButton(
                main,
                "PostRaidSettings",
                "RAID MUSIC",
                _styleSource,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(110f, 42f),
                new Vector2(-234f, -22f),
                ShowPostRaidModal,
                false,
                11f);

            UIUtils.CreateButton(
                main,
                "AddFolderTop",
                "ADD FOLDER",
                _styleSource,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(110f, 42f),
                new Vector2(-116f, -22f),
                ShowFolderModal,
                true,
                13f);

            UIUtils.CreateButton(
                main,
                "Close",
                "CLOSE",
                _styleSource,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(90f, 42f),
                new Vector2(-18f, -22f),
                Hide,
                false,
                13f);

            UIUtils.CreateDivider(
                main,
                "HeaderDivider",
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(1010f, 1f),
                new Vector2(0f, -145f),
                new Color(0.18f, 0.19f, 0.20f, 1f));

            UIUtils.CreateLabel(main, "NumberHeader", "#", _styleSource, 12f, UIUtils.MutedText,
                TextAlignmentOptions.Center, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(42f, 24f), new Vector2(30f, -153f));
            UIUtils.CreateLabel(main, "TitleHeader", "TITLE", _styleSource, 12f, UIUtils.MutedText,
                TextAlignmentOptions.Left, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(365f, 24f), new Vector2(82f, -153f));
            UIUtils.CreateLabel(main, "AlbumHeader", "ALBUM", _styleSource, 12f, UIUtils.MutedText,
                TextAlignmentOptions.Left, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(350f, 24f), new Vector2(465f, -153f));
            UIUtils.CreateLabel(main, "FormatHeader", "FORMAT", _styleSource, 12f, UIUtils.MutedText,
                TextAlignmentOptions.Center, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(80f, 24f), new Vector2(845f, -153f));
            UIUtils.CreateLabel(main, "SizeHeader", "SIZE", _styleSource, 12f, UIUtils.MutedText,
                TextAlignmentOptions.Right, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(80f, 24f), new Vector2(950f, -153f));

            _trackArea = UIUtils.CreateUIObject(main, "TrackRows");
            UIUtils.SetRect(
                (RectTransform)_trackArea.transform,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(1010f, 376f),
                new Vector2(30f, -183f));

            UIUtils.CreateButton(
                main,
                "PreviousPage",
                "<",
                _styleSource,
                new Vector2(0.5f, 0f),
                new Vector2(1f, 0f),
                new Vector2(38f, 32f),
                new Vector2(-50f, 25f),
                PreviousPage,
                false,
                17f);

            _pageLabel = UIUtils.CreateLabel(
                main,
                "Page",
                "1 / 1",
                _styleSource,
                13f,
                UIUtils.MutedText,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(120f, 32f),
                new Vector2(0f, 25f));

            UIUtils.CreateButton(
                main,
                "NextPage",
                ">",
                _styleSource,
                new Vector2(0.5f, 0f),
                new Vector2(0f, 0f),
                new Vector2(38f, 32f),
                new Vector2(50f, 25f),
                NextPage,
                false,
                17f);

            _statusLabel = UIUtils.CreateLabel(
                main,
                "Status",
                "Scanning your music folders...",
                _styleSource,
                12f,
                UIUtils.MutedText,
                TextAlignmentOptions.Left,
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(400f, 30f),
                new Vector2(30f, 25f));
        }

        private void BuildPlayerBar()
        {
            GameObject bar = UIUtils.CreatePanel(
                _panel,
                "PlayerBar",
                new Color(0.045f, 0.049f, 0.052f, 1f),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(1320f, 92f),
                Vector2.zero);

            UIUtils.CreatePanel(
                bar,
                "AlbumPlaceholder",
                UIUtils.AccentMuted,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(58f, 58f),
                new Vector2(22f, 0f));

            _nowTitle = UIUtils.CreateLabel(
                bar, "NowTitle", "Nothing playing", _styleSource, 17f, UIUtils.Text,
                TextAlignmentOptions.Left, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(345f, 26f), new Vector2(94f, 12f));

            _nowArtist = UIUtils.CreateLabel(
                bar, "NowArtist", "Choose a track from your library", _styleSource, 12f, UIUtils.MutedText,
                TextAlignmentOptions.Left, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(345f, 22f), new Vector2(94f, -14f));

            UIUtils.CreateTransportButton(
                bar, "Previous", TransportIcon.Previous,
                new Vector2(0.5f, 1f), new Vector2(1f, 1f), new Vector2(34f, 34f),
                new Vector2(-32f, -8f), Plugin.AudioPlayer.Previous);

            _playButton = UIUtils.CreateTransportButton(
                bar, "PlayPause", TransportIcon.PlayPause,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(42f, 42f),
                new Vector2(0f, -4f), Plugin.AudioPlayer.TogglePause, true);

            UIUtils.CreateTransportButton(
                bar, "Next", TransportIcon.Next,
                new Vector2(0.5f, 1f), new Vector2(0f, 1f), new Vector2(34f, 34f),
                new Vector2(32f, -8f), Plugin.AudioPlayer.Next);

            _shuffleLabel = UIUtils.CreateButton(
                bar, "Shuffle", "SHUFFLE", _styleSource,
                new Vector2(0.5f, 1f), new Vector2(1f, 1f), new Vector2(86f, 28f),
                new Vector2(-105f, -11f), ToggleShuffle, false, 11f)
                .GetComponentInChildren<TextMeshProUGUI>();

            _repeatLabel = UIUtils.CreateButton(
                bar, "Repeat", "REPEAT", _styleSource,
                new Vector2(0.5f, 1f), new Vector2(0f, 1f), new Vector2(86f, 28f),
                new Vector2(105f, -11f), CycleRepeat, false, 11f)
                .GetComponentInChildren<TextMeshProUGUI>();

            _progressSlider = UIUtils.CreateSlider(
                bar, "Progress", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(420f, 18f), new Vector2(0f, 15f), 0f);
            _progressSlider.onValueChanged.AddListener(OnProgressChanged);

            _timeLabel = UIUtils.CreateLabel(
                bar, "Time", "0:00 / 0:00", _styleSource, 11f, UIUtils.MutedText,
                TextAlignmentOptions.Right, new Vector2(0.5f, 0f), new Vector2(0f, 0f),
                new Vector2(110f, 22f), new Vector2(220f, 11f));

            UIUtils.CreateLabel(
                bar, "VolumeLabel", "VOLUME", _styleSource, 11f, UIUtils.MutedText,
                TextAlignmentOptions.Left, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(65f, 22f), new Vector2(-230f, 3f));

            _volumeSlider = UIUtils.CreateSlider(
                bar, "Volume", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(150f, 18f), new Vector2(-24f, 0f), Plugin.Settings.Volume);
            _volumeSlider.onValueChanged.AddListener(Plugin.AudioPlayer.SetVolume);
        }

        private void BuildFolderModal()
        {
            _folderModal = UIUtils.CreatePanel(
                _panel,
                "FolderModal",
                new Color(0f, 0f, 0f, 0.82f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(1320f, 720f),
                Vector2.zero);

            GameObject dialog = UIUtils.CreatePanel(
                _folderModal,
                "Dialog",
                UIUtils.Panel,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(680f, 270f),
                Vector2.zero);

            Outline outline = dialog.AddComponent<Outline>();
            outline.effectColor = UIUtils.Accent;
            outline.effectDistance = new Vector2(1f, -1f);

            UIUtils.CreateLabel(
                dialog, "Title", "ADD A MUSIC FOLDER", _styleSource, 24f, UIUtils.Text,
                TextAlignmentOptions.Left, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(530f, 35f), new Vector2(26f, -22f));

            UIUtils.CreateLabel(
                dialog, "Help", "Paste a Windows folder path. Subfolders are included automatically.",
                _styleSource, 13f, UIUtils.MutedText, TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(620f, 30f),
                new Vector2(27f, -61f));

            _folderInput = UIUtils.CreateInput(
                dialog, "FolderPath", @"Example: D:\Music", _styleSource,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(628f, 46f),
                new Vector2(26f, -104f));

            _modalStatus = UIUtils.CreateLabel(
                dialog, "Status", string.Empty, _styleSource, 12f, UIUtils.MutedText,
                TextAlignmentOptions.Left, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(400f, 25f), new Vector2(27f, 66f));

            UIUtils.CreateButton(
                dialog, "Cancel", "CANCEL", _styleSource,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(105f, 40f),
                new Vector2(-154f, 22f), HideFolderModal, false, 13f);

            UIUtils.CreateButton(
                dialog, "Add", "ADD FOLDER", _styleSource,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(130f, 40f),
                new Vector2(-18f, 22f), AddFolder, true, 13f);

            _folderModal.SetActive(false);
        }

        private void BuildPostRaidModal()
        {
            _postRaidModal = UIUtils.CreatePanel(
                _panel,
                "PostRaidModal",
                new Color(0f, 0f, 0f, 0.84f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(1320f, 720f),
                Vector2.zero);

            GameObject dialog = UIUtils.CreatePanel(
                _postRaidModal,
                "Dialog",
                UIUtils.Panel,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(790f, 410f),
                Vector2.zero);

            Outline outline = dialog.AddComponent<Outline>();
            outline.effectColor = UIUtils.Accent;
            outline.effectDistance = new Vector2(1f, -1f);

            UIUtils.CreateLabel(
                dialog, "Title", "POST-RAID MUSIC", _styleSource, 24f, UIUtils.Text,
                TextAlignmentOptions.Left, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(430f, 35f), new Vector2(26f, -22f));

            UIUtils.CreateLabel(
                dialog, "Help",
                "Choose separate playlists for successful and failed raids. Missing folders are created.",
                _styleSource, 13f, UIUtils.MutedText, TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(730f, 28f),
                new Vector2(27f, -60f));

            Button autoplay = UIUtils.CreateButton(
                dialog, "Autoplay", "AUTOPLAY OFF", _styleSource,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(145f, 38f),
                new Vector2(-24f, -22f), TogglePostRaidAutoplay, false, 11f);
            _modalAutoRaidLabel = autoplay.GetComponentInChildren<TextMeshProUGUI>();

            UIUtils.CreateLabel(
                dialog, "SurvivedLabel", "SURVIVED PLAYLIST FOLDER", _styleSource, 12f,
                UIUtils.Accent, TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(500f, 24f),
                new Vector2(27f, -104f));

            _survivedFolderInput = UIUtils.CreateInput(
                dialog, "SurvivedPath", @"D:\Music\PostRaid\Survived", _styleSource,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(736f, 44f),
                new Vector2(27f, -132f));

            UIUtils.CreateLabel(
                dialog, "DeathLabel", "FAILED RAID PLAYLIST FOLDER", _styleSource, 12f,
                new Color(1f, 0.42f, 0.33f, 1f), TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(500f, 24f),
                new Vector2(27f, -201f));

            _deathFolderInput = UIUtils.CreateInput(
                dialog, "DeathPath", @"D:\Music\PostRaid\Died", _styleSource,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(736f, 44f),
                new Vector2(27f, -229f));

            UIUtils.CreateLabel(
                dialog, "Fallback",
                "If a playlist is empty, SoulPlayer safely falls back to your full library.",
                _styleSource, 12f, UIUtils.MutedText, TextAlignmentOptions.Left,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(600f, 24f),
                new Vector2(27f, 79f));

            _postRaidStatus = UIUtils.CreateLabel(
                dialog, "Status", string.Empty, _styleSource, 12f, UIUtils.MutedText,
                TextAlignmentOptions.Left, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(430f, 25f), new Vector2(27f, 49f));

            UIUtils.CreateButton(
                dialog, "Cancel", "CANCEL", _styleSource,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(105f, 40f),
                new Vector2(-164f, 22f), HidePostRaidModal, false, 13f);

            UIUtils.CreateButton(
                dialog, "Save", "SAVE", _styleSource,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(130f, 40f),
                new Vector2(-24f, 22f), SavePostRaidSettings, true, 13f);

            _postRaidModal.SetActive(false);
        }

        private void Update()
        {
            ScanResult scan;
            if (Plugin.MusicLibrary.TryApplyCompletedScan(out scan))
            {
                _page = 0;
                RefreshEverything();
                _statusLabel.text = scan.Tracks.Count + " tracks ready  •  " +
                                    scan.DuplicateCount + " duplicates ignored";
            }

            if (_libraryDirty)
            {
                _page = 0;
                RefreshFolders();
                RefreshTracks();
                RefreshSettingsControls();
                _libraryDirty = false;
            }

            if (_audioDirty)
            {
                RefreshPlayer();
                _audioDirty = false;
            }

            UpdatePlaybackProgress();
        }

        private void RefreshEverything()
        {
            RefreshFolders();
            RefreshTracks();
            RefreshPlayer();
            RefreshSettingsControls();
        }

        private void RefreshSettingsControls()
        {
            if (_miniPlayerLabel != null)
            {
                _miniPlayerLabel.text = Plugin.Settings.ShowMiniPlayer ? "MINI ON" : "MINI OFF";
                _miniPlayerLabel.color = Plugin.Settings.ShowMiniPlayer ? UIUtils.Accent : UIUtils.Text;
            }

            string autoplayText = Plugin.Settings.AutoPlayAfterRaid ? "AUTOPLAY ON" : "AUTOPLAY OFF";
            Color autoplayColor = Plugin.Settings.AutoPlayAfterRaid ? UIUtils.Accent : UIUtils.Text;
            if (_autoRaidLabel != null)
            {
                _autoRaidLabel.text = autoplayText;
                _autoRaidLabel.color = autoplayColor;
            }

            if (_modalAutoRaidLabel != null)
            {
                _modalAutoRaidLabel.text = autoplayText;
                _modalAutoRaidLabel.color = autoplayColor;
            }
        }

        private void RefreshFolders()
        {
            foreach (GameObject row in _folderRows)
            {
                if (row != null)
                {
                    Destroy(row);
                }
            }
            _folderRows.Clear();

            IReadOnlyList<string> folders = Plugin.Settings.GetFolders();
            _folderCount.text = folders.Count.ToString();

            for (int index = 0; index < Math.Min(5, folders.Count); index++)
            {
                string folder = folders[index];
                GameObject row = UIUtils.CreatePanel(
                    _folderArea,
                    "Folder_" + index,
                    index % 2 == 0 ? new Color(0.055f, 0.06f, 0.064f, 0.55f) : Color.clear,
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(218f, 40f),
                    new Vector2(0f, -index * 43f));
                _folderRows.Add(row);

                UIUtils.CreateLabel(
                    row, "Path", ShortenPath(folder, 27), _styleSource, 12f, UIUtils.Text,
                    TextAlignmentOptions.MidlineLeft, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(170f, 36f), new Vector2(9f, 0f));

                string captured = folder;
                UIUtils.CreateButton(
                    row, "Remove", "X", _styleSource,
                    new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(30f, 28f),
                    new Vector2(-6f, 0f), () => RemoveFolder(captured), false, 11f);
            }

            if (folders.Count > 5)
            {
                TextMeshProUGUI more = UIUtils.CreateLabel(
                    _folderArea, "More", "+ " + (folders.Count - 5) + " more folders", _styleSource,
                    11f, UIUtils.MutedText, TextAlignmentOptions.Left,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(200f, 26f),
                    new Vector2(8f, -218f));
                _folderRows.Add(more.gameObject);
            }
        }

        private void RefreshTracks()
        {
            string query = _searchInput == null ? string.Empty : (_searchInput.text ?? string.Empty).Trim().ToLowerInvariant();
            IReadOnlyList<MusicTrack> all = Plugin.MusicLibrary.Tracks;
            _filteredTracks = string.IsNullOrWhiteSpace(query)
                ? all.ToList()
                : all.Where(track => track.SearchText.Contains(query)).ToList();

            int pages = Math.Max(1, (int)Math.Ceiling(_filteredTracks.Count / (double)TracksPerPage));
            _page = Math.Max(0, Math.Min(_page, pages - 1));
            _pageLabel.text = (_page + 1) + " / " + pages;
            _libraryCount.text = Plugin.MusicLibrary.IsScanning
                ? "SCANNING..."
                : all.Count + " TRACKS";

            foreach (GameObject row in _trackRows)
            {
                if (row != null)
                {
                    Destroy(row);
                }
            }
            _trackRows.Clear();

            List<MusicTrack> pageTracks = _filteredTracks
                .Skip(_page * TracksPerPage)
                .Take(TracksPerPage)
                .ToList();

            for (int index = 0; index < pageTracks.Count; index++)
            {
                MusicTrack track = pageTracks[index];
                int absoluteIndex = _page * TracksPerPage + index + 1;
                CreateTrackRow(track, absoluteIndex, index);
            }

            if (pageTracks.Count == 0)
            {
                TextMeshProUGUI empty = UIUtils.CreateLabel(
                    _trackArea, "Empty", Plugin.MusicLibrary.IsScanning
                        ? "Scanning your folders in the background..."
                        : "No tracks found. Add a music folder or change your search.",
                    _styleSource, 18f, UIUtils.MutedText, TextAlignmentOptions.Center,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(760f, 80f), Vector2.zero, true);
                _trackRows.Add(empty.gameObject);
            }

            _statusLabel.color = UIUtils.MutedText;
            _statusLabel.text = Plugin.MusicLibrary.IsScanning
                ? "Scanning in the background — you can keep using the menu."
                : _filteredTracks.Count + " matching tracks";
        }

        private void CreateTrackRow(MusicTrack track, int number, int rowIndex)
        {
            GameObject row = UIUtils.CreatePanel(
                _trackArea,
                "Track_" + number,
                rowIndex % 2 == 0 ? new Color(0.06f, 0.064f, 0.068f, 0.7f) : new Color(0.035f, 0.038f, 0.041f, 0.55f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(1010f, 44f),
                new Vector2(0f, -rowIndex * 46f));
            _trackRows.Add(row);

            Image image = row.GetComponent<Image>();
            Button button = row.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.12f, 1.02f, 1f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => PlayTrack(track));

            UIUtils.CreateLabel(row, "Number", number.ToString(), _styleSource, 12f, UIUtils.MutedText,
                TextAlignmentOptions.Center, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(42f, 38f), Vector2.zero);
            UIUtils.CreateLabel(row, "Title", track.Title, _styleSource, 14f, UIUtils.Text,
                TextAlignmentOptions.Left, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(365f, 21f), new Vector2(52f, 8f));
            UIUtils.CreateLabel(row, "Artist", track.Artist, _styleSource, 11f, UIUtils.MutedText,
                TextAlignmentOptions.Left, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(365f, 18f), new Vector2(52f, -10f));
            UIUtils.CreateLabel(row, "Album", track.Album, _styleSource, 12f, UIUtils.MutedText,
                TextAlignmentOptions.Left, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(350f, 36f), new Vector2(435f, 0f));
            UIUtils.CreateLabel(row, "Format", track.Extension, _styleSource, 11f,
                track.Extension == "FLAC" ? UIUtils.Accent : UIUtils.MutedText,
                TextAlignmentOptions.Center, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(80f, 36f), new Vector2(815f, 0f));
            UIUtils.CreateLabel(row, "Size", FormatSize(track.SizeBytes), _styleSource, 11f, UIUtils.MutedText,
                TextAlignmentOptions.Right, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(90f, 36f), new Vector2(910f, 0f));
        }

        private void RefreshPlayer()
        {
            MusicTrack track = Plugin.AudioPlayer.CurrentTrack;
            if (track == null)
            {
                _nowTitle.text = "Nothing playing";
                _nowArtist.text = "Choose a track from your library";
            }
            else
            {
                _nowTitle.text = track.Title;
                _nowArtist.text = Plugin.AudioPlayer.IsLoading
                    ? "Loading " + track.Extension + "..."
                    : track.Artist + "  •  " + track.Album;
            }

            UIUtils.SetPlayPauseState(_playButton, Plugin.AudioPlayer.IsPlaying);
            _shuffleLabel.text = Plugin.Settings.Shuffle ? "SHUFFLE ON" : "SHUFFLE";
            _shuffleLabel.color = Plugin.Settings.Shuffle ? UIUtils.Accent : UIUtils.Text;

            string repeat = Plugin.Settings.RepeatMode == 2
                ? "REPEAT ONE"
                : Plugin.Settings.RepeatMode == 1 ? "REPEAT ALL" : "REPEAT";
            _repeatLabel.text = repeat;
            _repeatLabel.color = Plugin.Settings.RepeatMode > 0 ? UIUtils.Accent : UIUtils.Text;

            if (!string.IsNullOrWhiteSpace(Plugin.AudioPlayer.LastError))
            {
                _statusLabel.text = Plugin.AudioPlayer.LastError;
                _statusLabel.color = new Color(1f, 0.42f, 0.33f, 1f);
            }
            else
            {
                _statusLabel.color = UIUtils.MutedText;
            }
        }

        private void UpdatePlaybackProgress()
        {
            float duration = Plugin.AudioPlayer.Duration;
            float current = Plugin.AudioPlayer.CurrentTime;
            _updatingProgress = true;
            _progressSlider.value = duration > 0.01f ? current / duration : 0f;
            _updatingProgress = false;
            _timeLabel.text = FormatTime(current) + " / " + FormatTime(duration);
        }

        private void PlayTrack(MusicTrack track)
        {
            Plugin.AudioPlayer.Play(track, _filteredTracks);
            _audioDirty = true;
        }

        private void OnSearchChanged(string value)
        {
            _page = 0;
            RefreshTracks();
        }

        private void PreviousPage()
        {
            if (_page > 0)
            {
                _page--;
                RefreshTracks();
            }
        }

        private void NextPage()
        {
            int pages = Math.Max(1, (int)Math.Ceiling(_filteredTracks.Count / (double)TracksPerPage));
            if (_page < pages - 1)
            {
                _page++;
                RefreshTracks();
            }
        }

        private void Rescan()
        {
            Plugin.MusicLibrary.BeginScan(Plugin.Settings.GetScanFolders());
            _statusLabel.color = UIUtils.MutedText;
            _statusLabel.text = "Scanning in the background — you can keep using the menu.";
            _libraryCount.text = "SCANNING...";
        }

        private void ShowFolderModal()
        {
            _folderInput.text = @"D:\soulseek_share";
            _modalStatus.text = string.Empty;
            _folderModal.SetActive(true);
            _folderModal.transform.SetAsLastSibling();
            _folderInput.Select();
            _folderInput.ActivateInputField();
        }

        private void HideFolderModal()
        {
            _folderModal.SetActive(false);
        }

        private void AddFolder()
        {
            string message;
            if (!Plugin.Settings.AddFolder(_folderInput.text, out message))
            {
                _modalStatus.color = new Color(1f, 0.42f, 0.33f, 1f);
                _modalStatus.text = message;
                return;
            }

            _folderModal.SetActive(false);
            Plugin.MusicLibrary.BeginScan(Plugin.Settings.GetScanFolders());
            RefreshFolders();
            _statusLabel.color = UIUtils.MutedText;
            _statusLabel.text = message;
            _libraryCount.text = "SCANNING...";
        }

        private void RemoveFolder(string folder)
        {
            if (Plugin.Settings.RemoveFolder(folder))
            {
                Plugin.MusicLibrary.BeginScan(Plugin.Settings.GetScanFolders());
                RefreshFolders();
                _libraryCount.text = "SCANNING...";
                _statusLabel.text = "Folder removed. Rebuilding your library...";
            }
        }

        private void ToggleShuffle()
        {
            Plugin.Settings.Shuffle = !Plugin.Settings.Shuffle;
            RefreshPlayer();
        }

        private void ToggleMiniPlayer()
        {
            Plugin.Settings.ShowMiniPlayer = !Plugin.Settings.ShowMiniPlayer;
            RefreshSettingsControls();
            SoulMiniPlayer.RefreshVisibility();
        }

        private void TogglePostRaidAutoplay()
        {
            Plugin.Settings.AutoPlayAfterRaid = !Plugin.Settings.AutoPlayAfterRaid;
            RefreshSettingsControls();
        }

        private void ShowPostRaidModal()
        {
            _survivedFolderInput.text = Plugin.Settings.SurvivedMusicFolder;
            _deathFolderInput.text = Plugin.Settings.DeathMusicFolder;
            _postRaidStatus.text = string.Empty;
            RefreshSettingsControls();
            _postRaidModal.SetActive(true);
            _postRaidModal.transform.SetAsLastSibling();
        }

        private void HidePostRaidModal()
        {
            _postRaidModal.SetActive(false);
        }

        private void SavePostRaidSettings()
        {
            string message;
            if (!Plugin.Settings.SetPostRaidFolders(
                    _survivedFolderInput.text,
                    _deathFolderInput.text,
                    out message))
            {
                _postRaidStatus.color = new Color(1f, 0.42f, 0.33f, 1f);
                _postRaidStatus.text = message;
                return;
            }

            _postRaidStatus.color = UIUtils.Accent;
            _postRaidStatus.text = message;
            Plugin.MusicLibrary.BeginScan(Plugin.Settings.GetScanFolders());
            _statusLabel.color = UIUtils.MutedText;
            _statusLabel.text = "Post-raid playlists saved. Scanning in the background...";
            _libraryCount.text = "SCANNING...";
        }

        private void CycleRepeat()
        {
            Plugin.Settings.RepeatMode = (Plugin.Settings.RepeatMode + 1) % 3;
            RefreshPlayer();
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
            _audioDirty = true;
        }

        private void OnLibraryChanged()
        {
            _libraryDirty = true;
        }

        private void Hide()
        {
            _folderModal.SetActive(false);
            _postRaidModal.SetActive(false);
            gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Plugin.AudioPlayer != null)
            {
                Plugin.AudioPlayer.Changed -= OnAudioChanged;
            }

            if (Plugin.MusicLibrary != null)
            {
                Plugin.MusicLibrary.Changed -= OnLibraryChanged;
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

        private static string FormatSize(long bytes)
        {
            return bytes >= 1024L * 1024L
                ? (bytes / (1024d * 1024d)).ToString("0.#") + " MB"
                : (bytes / 1024d).ToString("0") + " KB";
        }

        private static string ShortenPath(string path, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length <= maxLength)
            {
                return path;
            }

            string leaf = Path.GetFileName(path);
            if (leaf.Length >= maxLength - 3)
            {
                return "..." + leaf.Substring(leaf.Length - (maxLength - 3));
            }

            return path.Substring(0, Math.Min(3, path.Length)) + "...\\" + leaf;
        }
    }
}
