using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SoulPlayer.UI
{
    internal enum TransportIcon
    {
        Previous,
        PlayPause,
        Next
    }

    internal static class UIUtils
    {
        internal static readonly Color Background = new Color(0.014f, 0.016f, 0.018f, 0.995f);
        internal static readonly Color Panel = new Color(0.030f, 0.034f, 0.037f, 1f);
        internal static readonly Color PanelLight = new Color(0.058f, 0.064f, 0.068f, 1f);
        internal static readonly Color Accent = new Color(0.54f, 0.67f, 0.64f, 1f);
        internal static readonly Color AccentMuted = new Color(0.085f, 0.135f, 0.13f, 1f);
        internal static readonly Color Text = new Color(0.88f, 0.90f, 0.90f, 1f);
        internal static readonly Color MutedText = new Color(0.44f, 0.48f, 0.49f, 1f);

        private static Sprite _playTriangle;

        internal static GameObject CreateUIObject(GameObject parent, string name)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform));
            obj.layer = parent.layer;
            obj.transform.SetParent(parent.transform, false);
            ResetRectTransform((RectTransform)obj.transform);
            return obj;
        }

        internal static void ResetRectTransform(RectTransform rect)
        {
            rect.localPosition = Vector3.zero;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        internal static void SetRect(
            RectTransform rect,
            Vector2 anchor,
            Vector2 pivot,
            Vector2 size,
            Vector2 position)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        internal static void Stretch(RectTransform rect, float left, float right, float bottom, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        internal static GameObject CreatePanel(
            GameObject parent,
            string name,
            Color color,
            Vector2 anchor,
            Vector2 pivot,
            Vector2 size,
            Vector2 position)
        {
            GameObject obj = CreateUIObject(parent, name);
            SetRect((RectTransform)obj.transform, anchor, pivot, size, position);
            Image image = obj.AddComponent<Image>();
            image.color = color;
            return obj;
        }

        internal static TextMeshProUGUI CreateLabel(
            GameObject parent,
            string name,
            string value,
            TMP_Text styleSource,
            float fontSize,
            Color color,
            TextAlignmentOptions alignment,
            Vector2 anchor,
            Vector2 pivot,
            Vector2 size,
            Vector2 position,
            bool wrap = false)
        {
            GameObject obj = CreateUIObject(parent, name);
            SetRect((RectTransform)obj.transform, anchor, pivot, size, position);

            TextMeshProUGUI label = obj.AddComponent<TextMeshProUGUI>();
            ApplyFont(label, styleSource);
            label.text = value;
            label.fontSize = fontSize;
            label.color = color;
            label.alignment = alignment;
            label.enableWordWrapping = wrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            return label;
        }

        internal static Button CreateButton(
            GameObject parent,
            string name,
            string caption,
            TMP_Text styleSource,
            Vector2 anchor,
            Vector2 pivot,
            Vector2 size,
            Vector2 position,
            UnityAction onClick,
            bool accent = false,
            float fontSize = 16f)
        {
            GameObject obj = CreateUIObject(parent, name);
            SetRect((RectTransform)obj.transform, anchor, pivot, size, position);

            Image image = obj.AddComponent<Image>();
            image.color = accent ? AccentMuted : PanelLight;

            Button button = obj.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = accent
                ? new Color(1.28f, 1.28f, 1.28f, 1f)
                : new Color(1.22f, 1.22f, 1.22f, 1f);
            colors.pressedColor = accent
                ? new Color(0.72f, 0.78f, 0.77f, 1f)
                : new Color(0.72f, 0.72f, 0.72f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(onClick);

            CreateLabel(
                obj,
                "Label",
                caption,
                styleSource,
                fontSize,
                accent ? Accent : Text,
                TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                size,
                Vector2.zero);

            return button;
        }

        internal static Button CreateTransportButton(
            GameObject parent,
            string name,
            TransportIcon icon,
            Vector2 anchor,
            Vector2 pivot,
            Vector2 size,
            Vector2 position,
            UnityAction onClick,
            bool primary = false)
        {
            GameObject root = CreateUIObject(parent, name);
            SetRect((RectTransform)root.transform, anchor, pivot, size, position);

            Image background = root.AddComponent<Image>();
            background.color = primary
                ? new Color(0.82f, 0.85f, 0.84f, 1f)
                : new Color(1f, 1f, 1f, 0.001f);

            Button button = root.AddComponent<Button>();
            button.targetGraphic = background;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = primary
                ? new Color(1.08f, 1.08f, 1.08f, 1f)
                : new Color(0.70f, 0.76f, 0.75f, 1f);
            colors.pressedColor = new Color(0.72f, 0.76f, 0.75f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.fadeDuration = 0.07f;
            button.colors = colors;
            button.onClick.AddListener(onClick);

            if (primary)
            {
                Outline outline = root.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.45f);
                outline.effectDistance = new Vector2(1f, -1f);
            }

            Color symbolColor = primary ? Background : Text;
            if (icon == TransportIcon.PlayPause)
            {
                CreatePlaySymbol(root, symbolColor);
                CreatePauseSymbol(root, symbolColor);
                SetPlayPauseState(button, false);
            }
            else if (icon == TransportIcon.Previous)
            {
                CreateSkipSymbol(root, symbolColor, true);
            }
            else
            {
                CreateSkipSymbol(root, symbolColor, false);
            }

            return button;
        }

        internal static void SetPlayPauseState(Button button, bool isPlaying)
        {
            if (button == null)
            {
                return;
            }

            Transform play = button.transform.Find("PlaySymbol");
            Transform pause = button.transform.Find("PauseSymbol");
            if (play != null)
            {
                play.gameObject.SetActive(!isPlaying);
            }

            if (pause != null)
            {
                pause.gameObject.SetActive(isPlaying);
            }
        }

        private static void CreatePlaySymbol(GameObject parent, Color color)
        {
            GameObject symbol = CreateUIObject(parent, "PlaySymbol");
            SetRect((RectTransform)symbol.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(18f, 20f), new Vector2(1.5f, 0f));
            Image image = symbol.AddComponent<Image>();
            image.sprite = GetPlayTriangle();
            image.color = color;
            image.raycastTarget = false;
        }

        private static void CreatePauseSymbol(GameObject parent, Color color)
        {
            GameObject symbol = CreateUIObject(parent, "PauseSymbol");
            SetRect((RectTransform)symbol.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(18f, 20f), Vector2.zero);
            CreateShape(symbol, "Left", color, new Vector2(5f, 18f), new Vector2(-4.5f, 0f));
            CreateShape(symbol, "Right", color, new Vector2(5f, 18f), new Vector2(4.5f, 0f));
        }

        private static void CreateSkipSymbol(GameObject parent, Color color, bool previous)
        {
            GameObject symbol = CreateUIObject(parent, previous ? "PreviousSymbol" : "NextSymbol");
            SetRect((RectTransform)symbol.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(22f, 22f), Vector2.zero);

            GameObject triangle = CreateUIObject(symbol, "Triangle");
            SetRect((RectTransform)triangle.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(14f, 16f),
                new Vector2(previous ? 2f : -2f, 0f));
            triangle.transform.localRotation = Quaternion.Euler(0f, 0f, previous ? 180f : 0f);
            Image triangleImage = triangle.AddComponent<Image>();
            triangleImage.sprite = GetPlayTriangle();
            triangleImage.color = color;
            triangleImage.raycastTarget = false;

            CreateShape(
                symbol,
                "Bar",
                color,
                new Vector2(3f, 16f),
                new Vector2(previous ? -7f : 7f, 0f));
        }

        private static void CreateShape(
            GameObject parent,
            string name,
            Color color,
            Vector2 size,
            Vector2 position)
        {
            GameObject shape = CreatePanel(
                parent, name, color, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), size, position);
            shape.GetComponent<Image>().raycastTarget = false;
        }

        private static Sprite GetPlayTriangle()
        {
            if (_playTriangle != null)
            {
                return _playTriangle;
            }

            const int size = 24;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "SoulPlayerPlayTriangle";
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.hideFlags = HideFlags.HideAndDontSave;

            Color32 clear = new Color32(255, 255, 255, 0);
            Color32 solid = new Color32(255, 255, 255, 255);
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                int distance = Math.Abs(y - 12);
                int maxX = 19 - (distance * 3 / 2);
                for (int x = 0; x < size; x++)
                {
                    pixels[y * size + x] = x >= 6 && x <= maxX ? solid : clear;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            _playTriangle = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                24f);
            _playTriangle.name = "SoulPlayerPlayTriangleSprite";
            _playTriangle.hideFlags = HideFlags.HideAndDontSave;
            return _playTriangle;
        }

        internal static TMP_InputField CreateInput(
            GameObject parent,
            string name,
            string placeholder,
            TMP_Text styleSource,
            Vector2 anchor,
            Vector2 pivot,
            Vector2 size,
            Vector2 position)
        {
            GameObject root = CreateUIObject(parent, name);
            SetRect((RectTransform)root.transform, anchor, pivot, size, position);
            Image background = root.AddComponent<Image>();
            background.color = new Color(0.025f, 0.028f, 0.03f, 1f);

            TMP_InputField input = root.AddComponent<TMP_InputField>();
            input.targetGraphic = background;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.contentType = TMP_InputField.ContentType.Standard;
            input.caretColor = Accent;
            input.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.35f);

            GameObject viewport = CreateUIObject(root, "Text Area");
            RectTransform viewportRect = (RectTransform)viewport.transform;
            Stretch(viewportRect, 14f, 14f, 4f, 4f);
            viewport.AddComponent<RectMask2D>();

            TextMeshProUGUI value = CreateLabel(
                viewport,
                "Text",
                string.Empty,
                styleSource,
                17f,
                Text,
                TextAlignmentOptions.MidlineLeft,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(size.x - 28f, size.y - 8f),
                Vector2.zero);
            RectTransform valueRect = (RectTransform)value.transform;
            Stretch(valueRect, 0f, 0f, 0f, 0f);

            TextMeshProUGUI hint = CreateLabel(
                viewport,
                "Placeholder",
                placeholder,
                styleSource,
                17f,
                MutedText,
                TextAlignmentOptions.MidlineLeft,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(size.x - 28f, size.y - 8f),
                Vector2.zero);
            Stretch((RectTransform)hint.transform, 0f, 0f, 0f, 0f);

            input.textViewport = viewportRect;
            input.textComponent = value;
            input.placeholder = hint;
            return input;
        }

        internal static Slider CreateSlider(
            GameObject parent,
            string name,
            Vector2 anchor,
            Vector2 pivot,
            Vector2 size,
            Vector2 position,
            float value)
        {
            GameObject root = CreateUIObject(parent, name);
            SetRect((RectTransform)root.transform, anchor, pivot, size, position);

            Slider slider = root.AddComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = value;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };

            GameObject track = CreateUIObject(root, "Track");
            RectTransform trackRect = (RectTransform)track.transform;
            Stretch(trackRect, 0f, 0f, size.y / 2f - 2f, size.y / 2f - 2f);
            track.AddComponent<Image>().color = new Color(0.25f, 0.27f, 0.28f, 1f);

            GameObject fillArea = CreateUIObject(root, "Fill Area");
            RectTransform fillAreaRect = (RectTransform)fillArea.transform;
            Stretch(fillAreaRect, 0f, 0f, size.y / 2f - 2f, size.y / 2f - 2f);

            GameObject fill = CreateUIObject(fillArea, "Fill");
            RectTransform fillRect = (RectTransform)fill.transform;
            Stretch(fillRect, 0f, 0f, 0f, 0f);
            fill.AddComponent<Image>().color = Accent;
            slider.fillRect = fillRect;

            GameObject handleArea = CreateUIObject(root, "Handle Slide Area");
            RectTransform handleAreaRect = (RectTransform)handleArea.transform;
            Stretch(handleAreaRect, 0f, 0f, 0f, 0f);

            GameObject handle = CreateUIObject(handleArea, "Handle");
            RectTransform handleRect = (RectTransform)handle.transform;
            SetRect(
                handleRect,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(12f, 12f),
                Vector2.zero);
            Image handleImage = handle.AddComponent<Image>();
            handleImage.color = Text;
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImage;

            return slider;
        }

        internal static GameObject CreateDivider(
            GameObject parent,
            string name,
            Vector2 anchor,
            Vector2 pivot,
            Vector2 size,
            Vector2 position,
            Color color)
        {
            return CreatePanel(parent, name, color, anchor, pivot, size, position);
        }

        internal static Button CreateFullClickTarget(GameObject parent, UnityAction onClick)
        {
            GameObject target = CreateUIObject(parent, "SoulPlayerClickTarget");
            RectTransform rect = (RectTransform)target.transform;
            Stretch(rect, 0f, 0f, 0f, 0f);
            target.transform.SetAsLastSibling();

            Image image = target.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.002f);
            image.raycastTarget = true;

            Button button = target.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(onClick);
            return button;
        }

        internal static void ApplyFont(TMP_Text target, TMP_Text styleSource)
        {
            if (target != null && styleSource != null && styleSource.font != null)
            {
                target.font = styleSource.font;
            }
        }
    }
}
