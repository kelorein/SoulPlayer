using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SPT.Reflection.Patching;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SoulPlayer.UI
{
    internal sealed class FolderSelectionResult
    {
        internal bool Success { get; private set; }
        internal string Message { get; private set; }

        private FolderSelectionResult(bool success, string message)
        {
            Success = success;
            Message = message ?? string.Empty;
        }

        internal static FolderSelectionResult Ok(string message)
        {
            return new FolderSelectionResult(true, message);
        }

        internal static FolderSelectionResult Error(string message)
        {
            return new FolderSelectionResult(false, message);
        }
    }

    internal sealed class SoulFolderBrowser : MonoBehaviour
    {
        private const string ObjectName = "SoulFolderBrowser";
        private const string ComputerPath = "";
        private const int RowsPerPage = 8;

        private sealed class BrowserEntry
        {
            internal string Path;
            internal string Title;
            internal string Detail;
        }

        private static string _lastBrowsedPath;

        private readonly Stack<string> _history = new Stack<string>();
        private readonly List<GameObject> _quickRows = new List<GameObject>();
        private readonly List<GameObject> _listRows = new List<GameObject>();
        private readonly List<GameObject> _breadcrumbRows = new List<GameObject>();

        private TMP_Text _styleSource;
        private GameObject _quickArea;
        private GameObject _listArea;
        private GameObject _breadcrumbArea;
        private TextMeshProUGUI _locationLabel;
        private TextMeshProUGUI _statusLabel;
        private TextMeshProUGUI _pageLabel;
        private Button _backButton;
        private Button _upButton;
        private Button _previousPageButton;
        private Button _nextPageButton;
        private Button _useButton;
        private string _currentPath = ComputerPath;
        private int _page;
        private Func<string, FolderSelectionResult> _onSelected;
        private Action _onAdvanced;

        internal static SoulFolderBrowser Open(
            SoulPlayerWindow owner,
            TMP_Text styleSource,
            string initialPath,
            Func<string, FolderSelectionResult> onSelected,
            Action onAdvanced)
        {
            Transform existing = owner.transform.Find(ObjectName);
            SoulFolderBrowser browser = existing == null
                ? null
                : existing.GetComponent<SoulFolderBrowser>();

            if (browser == null)
            {
                GameObject root = UIUtils.CreateUIObject(owner.gameObject, ObjectName);
                UIUtils.Stretch((RectTransform)root.transform, 0f, 0f, 0f, 0f);
                Image blocker = root.AddComponent<Image>();
                blocker.color = new Color(0f, 0f, 0f, 0.88f);
                blocker.raycastTarget = true;

                browser = root.AddComponent<SoulFolderBrowser>();
                browser._styleSource = styleSource;
                browser.Build();
            }
            else
            {
                browser._styleSource = styleSource;
            }

            browser._onSelected = onSelected;
            browser._onAdvanced = onAdvanced;
            browser._history.Clear();
            browser._page = 0;
            browser.gameObject.SetActive(true);
            browser.transform.SetAsLastSibling();
            browser.RefreshQuickAccess();

            string startingPath = FirstUsablePath(
                _lastBrowsedPath,
                initialPath,
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

            browser.NavigateTo(startingPath, false);
            Plugin.Log.LogInfo("SoulPlayer in-game folder browser opened.");
            return browser;
        }

        private void Build()
        {
            GameObject dialog = UIUtils.CreatePanel(
                gameObject,
                "Dialog",
                UIUtils.Background,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(1180f, 650f),
                Vector2.zero);

            Outline outline = dialog.AddComponent<Outline>();
            outline.effectColor = new Color(UIUtils.Accent.r, UIUtils.Accent.g, UIUtils.Accent.b, 0.85f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            UIUtils.CreateLabel(
                dialog, "Title", "CHOOSE A MUSIC FOLDER", _styleSource, 25f, UIUtils.Text,
                TextAlignmentOptions.Left, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(650f, 36f), new Vector2(26f, -20f));

            UIUtils.CreateLabel(
                dialog, "Help",
                "Open folders until you reach your music, then choose USE THIS FOLDER. No Windows popup is used.",
                _styleSource, 13f, UIUtils.MutedText, TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(850f, 25f),
                new Vector2(27f, -57f));

            UIUtils.CreateButton(
                dialog, "Close", "X", _styleSource,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(42f, 38f),
                new Vector2(-20f, -18f), Close, false, 15f);

            GameObject quickPanel = UIUtils.CreatePanel(
                dialog, "QuickPanel", new Color(0.025f, 0.029f, 0.032f, 1f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(220f, 500f),
                new Vector2(20f, -92f));

            UIUtils.CreateLabel(
                quickPanel, "QuickTitle", "QUICK ACCESS", _styleSource, 12f, UIUtils.Accent,
                TextAlignmentOptions.Left, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(180f, 22f), new Vector2(16f, -14f));

            _quickArea = UIUtils.CreateUIObject(quickPanel, "QuickRows");
            UIUtils.SetRect(
                (RectTransform)_quickArea.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(188f, 452f), new Vector2(16f, -42f));

            _backButton = UIUtils.CreateButton(
                dialog, "Back", "< BACK", _styleSource,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(92f, 36f),
                new Vector2(262f, -92f), NavigateBack, false, 12f);

            _upButton = UIUtils.CreateButton(
                dialog, "Up", "UP", _styleSource,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(70f, 36f),
                new Vector2(360f, -92f), NavigateUp, false, 12f);

            _breadcrumbArea = UIUtils.CreateUIObject(dialog, "Breadcrumbs");
            UIUtils.SetRect(
                (RectTransform)_breadcrumbArea.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(700f, 36f), new Vector2(440f, -92f));

            GameObject listPanel = UIUtils.CreatePanel(
                dialog, "FolderListPanel", new Color(0.018f, 0.021f, 0.023f, 1f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(898f, 390f),
                new Vector2(262f, -140f));

            _listArea = UIUtils.CreateUIObject(listPanel, "FolderRows");
            UIUtils.SetRect(
                (RectTransform)_listArea.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(870f, 366f), new Vector2(14f, -12f));

            _statusLabel = UIUtils.CreateLabel(
                dialog, "BrowserStatus", string.Empty, _styleSource, 12f, UIUtils.MutedText,
                TextAlignmentOptions.Left, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(600f, 22f), new Vector2(264f, 79f));

            UIUtils.CreateLabel(
                dialog, "LocationTitle", "CURRENT LOCATION", _styleSource, 10f, UIUtils.MutedText,
                TextAlignmentOptions.Left, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(180f, 18f), new Vector2(264f, 49f));

            _locationLabel = UIUtils.CreateLabel(
                dialog, "Location", "THIS PC", _styleSource, 14f, UIUtils.Text,
                TextAlignmentOptions.Left, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(610f, 25f), new Vector2(264f, 22f));

            _previousPageButton = UIUtils.CreateButton(
                dialog, "PreviousPage", "<", _styleSource,
                new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(38f, 30f),
                new Vector2(-18f, 23f), PreviousPage, false, 15f);

            _pageLabel = UIUtils.CreateLabel(
                dialog, "Page", "1 / 1", _styleSource, 11f, UIUtils.MutedText,
                TextAlignmentOptions.Center, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(90f, 30f), new Vector2(32f, 23f));

            _nextPageButton = UIUtils.CreateButton(
                dialog, "NextPage", ">", _styleSource,
                new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(38f, 30f),
                new Vector2(82f, 23f), NextPage, false, 15f);

            UIUtils.CreateButton(
                dialog, "Advanced", "TYPE PATH", _styleSource,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(105f, 38f),
                new Vector2(20f, 20f), OpenAdvanced, false, 11f);

            UIUtils.CreateButton(
                dialog, "Cancel", "CANCEL", _styleSource,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(100f, 42f),
                new Vector2(-176f, 18f), Close, false, 12f);

            _useButton = UIUtils.CreateButton(
                dialog, "UseFolder", "USE THIS FOLDER", _styleSource,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(150f, 42f),
                new Vector2(-18f, 18f), UseCurrentFolder, true, 12f);

            gameObject.SetActive(false);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
            }
            else if (Input.GetKeyDown(KeyCode.Backspace))
            {
                NavigateBack();
            }
        }

        private void RefreshQuickAccess()
        {
            ClearObjects(_quickRows);

            int row = 0;
            AddQuickRow("MUSIC", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), row++);
            AddQuickRow("DESKTOP", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), row++);

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            AddQuickRow("DOWNLOADS", string.IsNullOrWhiteSpace(userProfile)
                ? string.Empty
                : Path.Combine(userProfile, "Downloads"), row++);

            AddQuickRow("DOCUMENTS", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), row++);
            AddQuickRow("THIS PC", ComputerPath, row++);

            foreach (DriveInfo drive in GetReadyDrives().Take(4))
            {
                AddQuickRow(FormatDriveCaption(drive), drive.RootDirectory.FullName, row++);
            }
        }

        private void AddQuickRow(string caption, string path, int row)
        {
            if (path != ComputerPath && (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)))
            {
                return;
            }

            string captured = path ?? ComputerPath;
            Button button = UIUtils.CreateButton(
                _quickArea, "Quick_" + row, caption, _styleSource,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(188f, 38f),
                new Vector2(0f, -row * 42f), () => NavigateTo(captured, true), false, 12f);
            _quickRows.Add(button.gameObject);
        }

        private void NavigateTo(string path, bool pushHistory)
        {
            string normalized = NormalizeBrowserPath(path);
            if (pushHistory && !string.Equals(_currentPath, normalized, StringComparison.OrdinalIgnoreCase))
            {
                _history.Push(_currentPath ?? ComputerPath);
            }

            _currentPath = normalized;
            if (_currentPath != ComputerPath)
            {
                _lastBrowsedPath = _currentPath;
            }

            _page = 0;
            RefreshView();
        }

        private void NavigateBack()
        {
            if (_history.Count == 0)
            {
                return;
            }

            _currentPath = _history.Pop();
            _page = 0;
            RefreshView();
        }

        private void NavigateUp()
        {
            if (_currentPath == ComputerPath)
            {
                return;
            }

            try
            {
                DirectoryInfo parent = Directory.GetParent(_currentPath);
                NavigateTo(parent == null ? ComputerPath : parent.FullName, true);
            }
            catch
            {
                NavigateTo(ComputerPath, true);
            }
        }

        private void RefreshView()
        {
            _statusLabel.text = string.Empty;
            _statusLabel.color = UIUtils.MutedText;
            _locationLabel.text = _currentPath == ComputerPath ? "THIS PC" : _currentPath;
            _useButton.interactable = _currentPath != ComputerPath;
            _backButton.interactable = _history.Count > 0;
            _upButton.interactable = _currentPath != ComputerPath;

            RefreshBreadcrumbs();
            RefreshEntries();
        }

        private void RefreshBreadcrumbs()
        {
            ClearObjects(_breadcrumbRows);

            List<KeyValuePair<string, string>> crumbs = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("THIS PC", ComputerPath)
            };

            if (_currentPath != ComputerPath)
            {
                string root = Path.GetPathRoot(_currentPath) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(root))
                {
                    crumbs.Add(new KeyValuePair<string, string>(root.TrimEnd('\\'), root));
                    string remainder = _currentPath.Substring(Math.Min(root.Length, _currentPath.Length));
                    string current = root;
                    foreach (string segment in remainder.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        current = Path.Combine(current, segment);
                        crumbs.Add(new KeyValuePair<string, string>(segment, current));
                    }
                }
            }

            if (crumbs.Count > 5)
            {
                List<KeyValuePair<string, string>> compact = new List<KeyValuePair<string, string>>
                {
                    crumbs[0],
                    crumbs[1],
                    new KeyValuePair<string, string>("...", string.Empty),
                    crumbs[crumbs.Count - 2],
                    crumbs[crumbs.Count - 1]
                };
                crumbs = compact;
            }

            float x = 0f;
            for (int index = 0; index < crumbs.Count; index++)
            {
                KeyValuePair<string, string> crumb = crumbs[index];
                float width = Mathf.Clamp(58f + crumb.Key.Length * 7f, 78f, 150f);

                if (crumb.Key == "...")
                {
                    TextMeshProUGUI dots = UIUtils.CreateLabel(
                        _breadcrumbArea, "Dots", "...", _styleSource, 12f, UIUtils.MutedText,
                        TextAlignmentOptions.Center, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                        new Vector2(42f, 32f), new Vector2(x, 0f));
                    _breadcrumbRows.Add(dots.gameObject);
                    x += 48f;
                    continue;
                }

                string captured = crumb.Value;
                Button button = UIUtils.CreateButton(
                    _breadcrumbArea, "Crumb_" + index, crumb.Key, _styleSource,
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(width, 32f),
                    new Vector2(x, 0f), () => NavigateTo(captured, true), false, 11f);
                _breadcrumbRows.Add(button.gameObject);
                x += width + 7f;
            }
        }

        private void RefreshEntries()
        {
            ClearObjects(_listRows);

            List<BrowserEntry> entries;
            bool canRead = true;
            if (_currentPath == ComputerPath)
            {
                entries = GetReadyDrives()
                    .Select(CreateDriveEntry)
                    .ToList();
            }
            else
            {
                entries = GetFolderEntries(_currentPath, out canRead);
            }

            int pages = Math.Max(1, (int)Math.Ceiling(entries.Count / (double)RowsPerPage));
            _page = Math.Max(0, Math.Min(_page, pages - 1));
            _pageLabel.text = (_page + 1) + " / " + pages;
            _previousPageButton.interactable = _page > 0;
            _nextPageButton.interactable = _page < pages - 1;
            _useButton.interactable = _currentPath != ComputerPath && canRead;

            List<BrowserEntry> pageEntries = entries
                .Skip(_page * RowsPerPage)
                .Take(RowsPerPage)
                .ToList();

            for (int index = 0; index < pageEntries.Count; index++)
            {
                CreateEntryRow(pageEntries[index], index);
            }

            if (!canRead)
            {
                ShowStatus("SoulPlayer cannot open this folder. Choose another location.", true);
            }
            else if (pageEntries.Count == 0)
            {
                TextMeshProUGUI empty = UIUtils.CreateLabel(
                    _listArea, "Empty", _currentPath == ComputerPath
                        ? "No ready drives were found."
                        : "This folder has no visible subfolders. You can still use this folder.",
                    _styleSource, 16f, UIUtils.MutedText, TextAlignmentOptions.Center,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(760f, 70f), Vector2.zero, true);
                _listRows.Add(empty.gameObject);
            }
        }

        private void CreateEntryRow(BrowserEntry entry, int rowIndex)
        {
            GameObject row = UIUtils.CreatePanel(
                _listArea, "Entry_" + rowIndex,
                rowIndex % 2 == 0
                    ? new Color(0.055f, 0.06f, 0.064f, 0.80f)
                    : new Color(0.035f, 0.038f, 0.041f, 0.72f),
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(870f, 42f), new Vector2(0f, -rowIndex * 45f));
            _listRows.Add(row);

            Image image = row.GetComponent<Image>();
            Button button = row.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
            colors.pressedColor = new Color(0.72f, 0.76f, 0.75f, 1f);
            colors.fadeDuration = 0.07f;
            button.colors = colors;

            string captured = entry.Path;
            button.onClick.AddListener(() => NavigateTo(captured, true));

            UIUtils.CreateLabel(
                row, "FolderName", entry.Title, _styleSource, 14f, UIUtils.Text,
                TextAlignmentOptions.MidlineLeft, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(610f, 36f), new Vector2(16f, 0f));

            UIUtils.CreateLabel(
                row, "Detail", entry.Detail, _styleSource, 11f, UIUtils.MutedText,
                TextAlignmentOptions.MidlineRight, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(210f, 36f), new Vector2(-16f, 0f));
        }

        private void PreviousPage()
        {
            if (_page > 0)
            {
                _page--;
                RefreshEntries();
            }
        }

        private void NextPage()
        {
            _page++;
            RefreshEntries();
        }

        private void UseCurrentFolder()
        {
            if (_currentPath == ComputerPath || _onSelected == null)
            {
                return;
            }

            FolderSelectionResult result = _onSelected(_currentPath);
            if (result == null)
            {
                ShowStatus("SoulPlayer could not use that folder.", true);
                return;
            }

            if (!result.Success)
            {
                ShowStatus(result.Message, true);
                return;
            }

            _lastBrowsedPath = _currentPath;
            Close();
        }

        private void OpenAdvanced()
        {
            Action advanced = _onAdvanced;
            Close();
            if (advanced != null)
            {
                advanced();
            }
        }

        private void Close()
        {
            gameObject.SetActive(false);
        }

        internal void ShowStatus(string message, bool error)
        {
            _statusLabel.text = message ?? string.Empty;
            _statusLabel.color = error ? new Color(1f, 0.42f, 0.33f, 1f) : UIUtils.Accent;
        }

        private static List<BrowserEntry> GetFolderEntries(string path, out bool canRead)
        {
            canRead = true;
            try
            {
                return Directory.GetDirectories(path)
                    .Where(IsVisibleFolder)
                    .Select(folder => new BrowserEntry
                    {
                        Path = folder,
                        Title = SafeFolderName(folder),
                        Detail = "OPEN  >"
                    })
                    .OrderBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (UnauthorizedAccessException)
            {
                canRead = false;
                return new List<BrowserEntry>();
            }
            catch (IOException)
            {
                canRead = false;
                return new List<BrowserEntry>();
            }
            catch
            {
                canRead = false;
                return new List<BrowserEntry>();
            }
        }

        private static BrowserEntry CreateDriveEntry(DriveInfo drive)
        {
            string detail = "OPEN  >";
            try
            {
                detail = FormatBytes(drive.AvailableFreeSpace) + " FREE  >";
            }
            catch
            {
            }

            return new BrowserEntry
            {
                Path = drive.RootDirectory.FullName,
                Title = FormatDriveCaption(drive),
                Detail = detail
            };
        }

        private static string FormatDriveCaption(DriveInfo drive)
        {
            string rootPath = string.Empty;
            try
            {
                rootPath = drive.RootDirectory.FullName;
            }
            catch
            {
                rootPath = drive.Name;
            }

            string driveName = (rootPath ?? string.Empty).Trim().TrimEnd('\\', '/');
            if (driveName.Length >= 2 && char.IsLetter(driveName[0]) && driveName[1] == ':')
            {
                driveName = char.ToUpperInvariant(driveName[0]) + ":";
            }

            if (string.IsNullOrWhiteSpace(driveName))
            {
                driveName = "DRIVE";
            }

            string volumeLabel = string.Empty;
            try
            {
                volumeLabel = drive.VolumeLabel == null ? string.Empty : drive.VolumeLabel.Trim();
            }
            catch
            {
            }

            // Some Unity/Mono environments incorrectly expose the root path itself
            // as the volume label (for example "C:\\"). Never display path syntax
            // as a friendly drive name.
            string normalizedLabel = (volumeLabel ?? string.Empty).Trim().TrimEnd('\\', '/');
            bool labelLooksLikeDrivePath =
                normalizedLabel.IndexOf('\\') >= 0 ||
                normalizedLabel.IndexOf('/') >= 0 ||
                (normalizedLabel.Length >= 2 &&
                 char.IsLetter(normalizedLabel[0]) &&
                 normalizedLabel[1] == ':' &&
                 normalizedLabel.Length <= 3);

            if (string.IsNullOrWhiteSpace(normalizedLabel) ||
                labelLooksLikeDrivePath ||
                string.Equals(normalizedLabel, driveName, StringComparison.OrdinalIgnoreCase))
            {
                return driveName;
            }

            return normalizedLabel + " (" + driveName + ")";
        }

        private static IEnumerable<DriveInfo> GetReadyDrives()
        {
            try
            {
                return DriveInfo.GetDrives()
                    .Where(drive =>
                    {
                        try
                        {
                            return drive.IsReady;
                        }
                        catch
                        {
                            return false;
                        }
                    })
                    .OrderBy(drive => drive.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch
            {
                return new DriveInfo[0];
            }
        }

        private static bool IsVisibleFolder(string path)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                return (attributes & FileAttributes.Hidden) == 0 &&
                       (attributes & FileAttributes.System) == 0;
            }
            catch
            {
                return false;
            }
        }

        private static string SafeFolderName(string path)
        {
            try
            {
                DirectoryInfo info = new DirectoryInfo(path);
                return string.IsNullOrWhiteSpace(info.Name) ? path : info.Name;
            }
            catch
            {
                return path;
            }
        }

        private static string NormalizeBrowserPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return ComputerPath;
            }

            try
            {
                string full = Path.GetFullPath(path);
                return Directory.Exists(full) ? full : ComputerPath;
            }
            catch
            {
                return ComputerPath;
            }
        }

        private static string FirstUsablePath(params string[] paths)
        {
            foreach (string path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                try
                {
                    if (Directory.Exists(path))
                    {
                        return Path.GetFullPath(path);
                    }
                }
                catch
                {
                }
            }

            return ComputerPath;
        }

        private static string FormatBytes(long bytes)
        {
            const double gb = 1024d * 1024d * 1024d;
            const double mb = 1024d * 1024d;
            return bytes >= gb
                ? (bytes / gb).ToString("0.#") + " GB"
                : (bytes / mb).ToString("0") + " MB";
        }

        private static void ClearObjects(List<GameObject> objects)
        {
            foreach (GameObject obj in objects)
            {
                if (obj != null)
                {
                    obj.SetActive(false);
                    Destroy(obj);
                }
            }
            objects.Clear();
        }
    }
}

namespace SoulPlayer.Patches
{
    internal sealed class FolderBrowserPatch : ModulePatch
    {
        private static bool _allowOriginalFolderDialog;

        protected override MethodBase GetTargetMethod()
        {
            MethodInfo method = AccessTools.Method(typeof(SoulPlayer.UI.SoulPlayerWindow), "ShowFolderModal");
            if (method == null)
            {
                throw new MissingMethodException(typeof(SoulPlayer.UI.SoulPlayerWindow).FullName, "ShowFolderModal");
            }

            return method;
        }

        [PatchPrefix]
        private static bool PatchPrefix(SoulPlayer.UI.SoulPlayerWindow __instance)
        {
            if (_allowOriginalFolderDialog)
            {
                _allowOriginalFolderDialog = false;
                return true;
            }

            TMP_Text styleSource = Traverse.Create(__instance)
                .Field("_styleSource")
                .GetValue<TMP_Text>();

            IReadOnlyList<string> configuredFolders = Plugin.Settings.GetFolders();
            string initialPath = configuredFolders.Count > 0 ? configuredFolders[0] : string.Empty;

            SoulPlayer.UI.SoulFolderBrowser.Open(
                __instance,
                styleSource,
                initialPath,
                path => SelectLibraryFolder(__instance, path),
                () => OpenManualPath(__instance));

            return false;
        }

        private static SoulPlayer.UI.FolderSelectionResult SelectLibraryFolder(
            SoulPlayer.UI.SoulPlayerWindow owner,
            string path)
        {
            string message;
            if (!Plugin.Settings.AddFolder(path, out message))
            {
                return SoulPlayer.UI.FolderSelectionResult.Error(message);
            }

            Plugin.MusicLibrary.BeginScan(Plugin.Settings.GetScanFolders());

            try
            {
                AccessTools.Method(typeof(SoulPlayer.UI.SoulPlayerWindow), "RefreshFolders")
                    .Invoke(owner, null);

                TextMeshProUGUI status = Traverse.Create(owner)
                    .Field("_statusLabel")
                    .GetValue<TextMeshProUGUI>();
                TextMeshProUGUI count = Traverse.Create(owner)
                    .Field("_libraryCount")
                    .GetValue<TextMeshProUGUI>();

                if (status != null)
                {
                    status.color = SoulPlayer.UI.UIUtils.MutedText;
                    status.text = message;
                }

                if (count != null)
                {
                    count.text = "SCANNING...";
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Folder browser selected the folder, but immediate UI refresh failed: " + ex.Message);
            }

            return SoulPlayer.UI.FolderSelectionResult.Ok(message);
        }

        private static void OpenManualPath(SoulPlayer.UI.SoulPlayerWindow owner)
        {
            try
            {
                _allowOriginalFolderDialog = true;
                AccessTools.Method(typeof(SoulPlayer.UI.SoulPlayerWindow), "ShowFolderModal")
                    .Invoke(owner, null);
            }
            catch (Exception ex)
            {
                _allowOriginalFolderDialog = false;
                Plugin.Log.LogError("Could not open the advanced folder path dialog: " + ex);
            }
        }
    }
}
