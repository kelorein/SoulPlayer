using System;
using System.Collections.Generic;
using System.Linq;
using SoulPlayer.Cassettes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SoulPlayer.UI
{
    internal sealed class SoulTapeCollectionPage : MonoBehaviour
    {
        private const int CardsPerPage = 8;
        private const int CardColumns = 4;

        private readonly List<GameObject> _cards = new List<GameObject>();
        private readonly Dictionary<SoulTapeCollectionFilter, Button> _filterButtons =
            new Dictionary<SoulTapeCollectionFilter, Button>();

        private TMP_Text _styleSource;
        private SoulTapeCatalog _catalog;
        private SoulTapeCollection _collection;
        private ISoulTapeEligibleCatalogProvider _eligibleCatalog;
        private SoulTapeCollectionProjection _projection;
        private GameObject _cardArea;
        private TextMeshProUGUI _progressLabel;
        private TextMeshProUGUI _pageLabel;
        private TextMeshProUGUI _statusLabel;
        private SoulTapeCollectionFilter _filter = SoulTapeCollectionFilter.All;
        private int _page;
        private bool _dirty = true;
        private bool _initialized;

        internal static SoulTapeCollectionPage Create(
            GameObject panel,
            TMP_Text styleSource,
            SoulTapeCatalog catalog,
            SoulTapeCollection collection,
            ISoulTapeEligibleCatalogProvider eligibleCatalog,
            Action close)
        {
            GameObject root = UIUtils.CreatePanel(
                panel,
                "Collection",
                UIUtils.Background,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(1070f, 628f),
                new Vector2(250f, 0f));
            SoulTapeCollectionPage page = root.AddComponent<SoulTapeCollectionPage>();
            page.Initialize(
                styleSource, catalog, collection, eligibleCatalog, close);
            root.SetActive(false);
            return page;
        }

        internal void Show()
        {
            gameObject.SetActive(true);
            RefreshNow();
        }

        internal void Hide()
        {
            gameObject.SetActive(false);
        }

        private void Initialize(
            TMP_Text styleSource,
            SoulTapeCatalog catalog,
            SoulTapeCollection collection,
            ISoulTapeEligibleCatalogProvider eligibleCatalog,
            Action close)
        {
            _styleSource = styleSource;
            _catalog = catalog;
            _collection = collection;
            _eligibleCatalog = eligibleCatalog;
            _projection = new SoulTapeCollectionProjection(
                catalog, collection, eligibleCatalog);
            Build(close);
            _catalog.Changed += OnSourceChanged;
            _collection.Changed += OnSourceChanged;
            if (_eligibleCatalog != null)
            {
                _eligibleCatalog.EligibleCatalogChanged += OnSourceChanged;
            }
            _initialized = true;
        }

        private void Build(Action close)
        {
            UIUtils.CreateLabel(
                gameObject,
                "Title",
                "SOULTAPE COLLECTION",
                _styleSource,
                27f,
                UIUtils.Text,
                TextAlignmentOptions.Left,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(460f, 38f),
                new Vector2(30f, -22f));

            UIUtils.CreateLabel(
                gameObject,
                "Subtitle",
                "Find cassettes during raids to permanently unlock their music.",
                _styleSource,
                13f,
                UIUtils.MutedText,
                TextAlignmentOptions.Left,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(500f, 24f),
                new Vector2(31f, -58f));

            _progressLabel = UIUtils.CreateLabel(
                gameObject,
                "Progress",
                "DISCOVERED -- / --",
                _styleSource,
                15f,
                UIUtils.Accent,
                TextAlignmentOptions.Right,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(300f, 38f),
                new Vector2(-124f, -24f));

            UIUtils.CreateButton(
                gameObject,
                "Close",
                "CLOSE",
                _styleSource,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(90f, 42f),
                new Vector2(-18f, -22f),
                () => close(),
                false,
                13f);

            CreateFilterButton(SoulTapeCollectionFilter.All, "ALL", 30f, 92f);
            CreateFilterButton(SoulTapeCollectionFilter.Discovered, "DISCOVERED", 142f, 128f);
            CreateFilterButton(SoulTapeCollectionFilter.Favorites, "FAVORITES", 282f, 118f);
            CreateFilterButton(SoulTapeCollectionFilter.Undiscovered, "UNDISCOVERED", 412f, 140f);

            UIUtils.CreateDivider(
                gameObject,
                "HeaderDivider",
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(1010f, 1f),
                new Vector2(0f, -145f),
                new Color(0.18f, 0.19f, 0.20f, 1f));

            _cardArea = UIUtils.CreateUIObject(gameObject, "CassetteCards");
            UIUtils.SetRect(
                (RectTransform)_cardArea.transform,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(1010f, 362f),
                new Vector2(30f, -163f));

            UIUtils.CreateButton(
                gameObject,
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
                gameObject,
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
                gameObject,
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
                gameObject,
                "Status",
                string.Empty,
                _styleSource,
                12f,
                UIUtils.MutedText,
                TextAlignmentOptions.Left,
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(430f, 30f),
                new Vector2(30f, 25f));
        }

        private void CreateFilterButton(
            SoulTapeCollectionFilter filter,
            string label,
            float x,
            float width)
        {
            Button button = UIUtils.CreateButton(
                gameObject,
                "Filter" + filter,
                label,
                _styleSource,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(width, 34f),
                new Vector2(x, -96f),
                () => SetFilter(filter),
                false,
                11f);
            _filterButtons[filter] = button;
        }

        private void Update()
        {
            if (_dirty)
            {
                RefreshNow();
            }
        }

        private void OnEnable()
        {
            if (_initialized)
            {
                RefreshNow();
            }
        }

        private void OnSourceChanged()
        {
            _dirty = true;
        }

        private void RefreshNow()
        {
            if (!_initialized && _projection == null)
            {
                return;
            }

            _dirty = false;
            SoulTapeCollectionSnapshot snapshot = _projection.Project(_filter);
            UpdateFilterButtons(snapshot.IsAvailable);
            ClearCards();

            if (!snapshot.IsAvailable)
            {
                _page = 0;
                _progressLabel.text = "DISCOVERED -- / --";
                _pageLabel.text = "-- / --";
                _statusLabel.color = UIUtils.MutedText;
                _statusLabel.text = "COLLECTION UNAVAILABLE";
                CreateEmptyState(
                    "COLLECTION UNAVAILABLE\nWaiting for the active SPT profile.");
                return;
            }

            _progressLabel.text = "DISCOVERED " + snapshot.DiscoveredCount +
                                  " / " + snapshot.TotalCount;
            int pageCount = Math.Max(
                1,
                (int)Math.Ceiling(snapshot.Entries.Count / (double)CardsPerPage));
            _page = Math.Max(0, Math.Min(_page, pageCount - 1));
            _pageLabel.text = (_page + 1) + " / " + pageCount;

            List<SoulTapeCollectionCard> pageCards = snapshot.Entries
                .Skip(_page * CardsPerPage)
                .Take(CardsPerPage)
                .ToList();
            for (int index = 0; index < pageCards.Count; index++)
            {
                CreateCard(pageCards[index], index);
            }

            if (pageCards.Count == 0)
            {
                CreateEmptyState(GetEmptyMessage(_filter));
            }

            _statusLabel.color = UIUtils.MutedText;
            _statusLabel.text = snapshot.Entries.Count + " cassette slots in this view";
        }

        private void CreateCard(SoulTapeCollectionCard card, int index)
        {
            const float cardWidth = 242f;
            const float cardHeight = 166f;
            const float columnGap = 14f;
            const float rowGap = 14f;
            int column = index % CardColumns;
            int row = index / CardColumns;
            Vector2 position = new Vector2(
                column * (cardWidth + columnGap),
                -row * (cardHeight + rowGap));
            Color background = card.IsDiscovered
                ? new Color(0.052f, 0.058f, 0.062f, 1f)
                : new Color(0.031f, 0.034f, 0.037f, 1f);
            GameObject panel = UIUtils.CreatePanel(
                _cardArea,
                "CassetteCard_" + index,
                background,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(cardWidth, cardHeight),
                position);
            _cards.Add(panel);
            UIUtils.CreateLabel(
                panel,
                "Brand",
                "SOULTAPE",
                _styleSource,
                11f,
                card.IsDiscovered ? UIUtils.Accent : UIUtils.MutedText,
                TextAlignmentOptions.Left,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(140f, 22f),
                new Vector2(14f, -12f));

            if (!card.IsDiscovered)
            {
                CreateLockedCard(panel);
                return;
            }

            SoulTapeRarity rarity = card.Rarity ?? SoulTapeRarity.Common;
            Color rarityColor = SoulTapeRarityPresentation.GetAccent(rarity);
            GameObject accent = UIUtils.CreatePanel(
                panel,
                "RarityAccent",
                rarityColor,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(cardWidth, 3f),
                Vector2.zero);
            accent.GetComponent<Image>().raycastTarget = false;

            UIUtils.CreateLabel(
                panel,
                "Artist",
                card.Artist,
                _styleSource,
                13f,
                UIUtils.MutedText,
                TextAlignmentOptions.Left,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(212f, 22f),
                new Vector2(14f, -45f));
            UIUtils.CreateLabel(
                panel,
                "Title",
                card.Title,
                _styleSource,
                17f,
                UIUtils.Text,
                TextAlignmentOptions.Left,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(212f, 28f),
                new Vector2(14f, -70f));
            UIUtils.CreateLabel(
                panel,
                "Rarity",
                SoulTapeRarityPresentation.GetLabel(rarity),
                _styleSource,
                11f,
                rarityColor,
                TextAlignmentOptions.Right,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(104f, 22f),
                new Vector2(-14f, -12f));

            if (!card.IsAudioAvailable)
            {
                UIUtils.CreateLabel(
                    panel,
                    "AudioMissing",
                    "AUDIO MISSING",
                    _styleSource,
                    10f,
                    new Color(0.88f, 0.48f, 0.38f, 1f),
                    TextAlignmentOptions.Right,
                    new Vector2(1f, 0f),
                    new Vector2(1f, 0f),
                    new Vector2(112f, 22f),
                    new Vector2(-14f, 43f));
            }

            string cassetteId = card.Id;
            bool favorite = card.IsFavorite;
            Button favoriteButton = UIUtils.CreateButton(
                panel,
                "Favorite",
                favorite ? "FAV ON" : "FAVORITE",
                _styleSource,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(92f, 28f),
                new Vector2(-12f, 9f),
                () => ToggleFavorite(cassetteId, favorite),
                favorite,
                10f);
            TextMeshProUGUI favoriteLabel = favoriteButton.GetComponentInChildren<TextMeshProUGUI>();
            if (favoriteLabel != null)
            {
                favoriteLabel.color = favorite ? rarityColor : UIUtils.Text;
            }
        }

        private void CreateLockedCard(GameObject panel)
        {
            UIUtils.CreateLabel(
                panel,
                "Unknown",
                "???",
                _styleSource,
                25f,
                new Color(0.56f, 0.59f, 0.60f, 1f),
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(180f, 38f),
                new Vector2(0f, 7f));
            UIUtils.CreateLabel(
                panel,
                "Undiscovered",
                "UNDISCOVERED",
                _styleSource,
                11f,
                UIUtils.MutedText,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(180f, 25f),
                new Vector2(0f, 18f));
        }

        private void CreateEmptyState(string message)
        {
            TextMeshProUGUI empty = UIUtils.CreateLabel(
                _cardArea,
                "Empty",
                message,
                _styleSource,
                18f,
                UIUtils.MutedText,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(720f, 90f),
                Vector2.zero,
                true);
            _cards.Add(empty.gameObject);
        }

        private void ClearCards()
        {
            foreach (GameObject card in _cards)
            {
                if (card != null)
                {
                    Destroy(card);
                }
            }
            _cards.Clear();
        }

        private void SetFilter(SoulTapeCollectionFilter filter)
        {
            if (_filter == filter)
            {
                return;
            }

            _filter = filter;
            _page = 0;
            RefreshNow();
        }

        private void UpdateFilterButtons(bool enabled)
        {
            foreach (KeyValuePair<SoulTapeCollectionFilter, Button> pair in _filterButtons)
            {
                Button button = pair.Value;
                button.interactable = enabled;
                Image image = button.GetComponent<Image>();
                TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
                bool selected = pair.Key == _filter;
                if (image != null)
                {
                    image.color = selected ? UIUtils.AccentMuted : UIUtils.PanelLight;
                }
                if (label != null)
                {
                    label.color = selected ? UIUtils.Accent : UIUtils.Text;
                }
            }
        }

        private void ToggleFavorite(string id, bool wasFavorite)
        {
            bool changed = _collection.SetFavorite(id, !wasFavorite);
            RefreshNow();
            if (changed)
            {
                _statusLabel.color = UIUtils.Accent;
                _statusLabel.text = wasFavorite
                    ? "Removed from favorites"
                    : "Saved to favorites";
            }
            else
            {
                _statusLabel.color = new Color(0.88f, 0.48f, 0.38f, 1f);
                _statusLabel.text = "FAVORITE NOT SAVED — collection state was unchanged";
            }
        }

        private void PreviousPage()
        {
            if (_page > 0)
            {
                _page--;
                RefreshNow();
            }
        }

        private void NextPage()
        {
            SoulTapeCollectionSnapshot snapshot = _projection.Project(_filter);
            int pageCount = Math.Max(
                1,
                (int)Math.Ceiling(snapshot.Entries.Count / (double)CardsPerPage));
            if (_page < pageCount - 1)
            {
                _page++;
                RefreshNow();
            }
        }

        private static string GetEmptyMessage(SoulTapeCollectionFilter filter)
        {
            switch (filter)
            {
                case SoulTapeCollectionFilter.Discovered:
                    return "No discovered cassettes yet.";
                case SoulTapeCollectionFilter.Favorites:
                    return "No favorite cassettes yet.";
                case SoulTapeCollectionFilter.Undiscovered:
                    return "Every collectible cassette has been discovered.";
                default:
                    return "No curated SoulTape cassettes are currently cataloged.";
            }
        }

        private void OnDestroy()
        {
            if (_catalog != null)
            {
                _catalog.Changed -= OnSourceChanged;
            }
            if (_collection != null)
            {
                _collection.Changed -= OnSourceChanged;
            }
            if (_eligibleCatalog != null)
            {
                _eligibleCatalog.EligibleCatalogChanged -= OnSourceChanged;
            }
        }
    }
}
