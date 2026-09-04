using System;
using System.Collections.Generic;
using System.IO;
using SoulPlayer.Recorder;
using UnityEditor;
using UnityEngine;

namespace SoulPlayer.Editor
{
    internal static class SoulRecorderStatusOverlayPreviewBuilder
    {
        private const string OutputFolder =
            "Artifacts/SoulRecorderStatusOverlayPreview";
        private const string DiscoveryOutputFolder =
            "Artifacts/SoulTapeDiscoveryOverlayPreview";

        internal static void BatchBuild()
        {
            try
            {
                string repository = Path.GetFullPath(Path.Combine(
                    Application.dataPath, "..", "..", "..", ".."));
                string output = Path.Combine(repository, OutputFolder);
                Directory.CreateDirectory(output);
                Render(repository, output, 1920, 1080,
                    "soulrecorder-status-1920x1080.png");
                Render(repository, output, 3440, 1440,
                    "soulrecorder-status-3440x1440.png");
                File.WriteAllText(Path.Combine(output, "preview-report.txt"),
                    "SoulRecorder status overlay preview: PASS\n" +
                    "Renderer: exact shared runtime status layout/math\n" +
                    "State: InsertingCassette\n" +
                    "Resolutions: 1920x1080, 3440x1440\n");
                Debug.Log("SOULRECORDER STATUS OVERLAY PREVIEW: PASS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError("SOULRECORDER STATUS OVERLAY PREVIEW: FAIL");
                EditorApplication.Exit(1);
            }
        }

        internal static void BatchBuildDiscovery()
        {
            try
            {
                string repository = Path.GetFullPath(Path.Combine(
                    Application.dataPath, "..", "..", "..", ".."));
                string output = Path.Combine(repository, DiscoveryOutputFolder);
                Directory.CreateDirectory(output);
                RenderDiscovery(output, 1920, 1080,
                    "soultape-discovered-1920x1080.png");
                RenderDiscovery(output, 3440, 1440,
                    "soultape-discovered-3440x1440.png");
                File.WriteAllText(Path.Combine(output, "preview-report.txt"),
                    "SoulTape discovery overlay preview: PASS\n" +
                    "Renderer: exact shared runtime status layout/math\n" +
                    "Notification: SOULTAPE DISCOVERED\n" +
                    "Resolutions: 1920x1080, 3440x1440\n");
                Debug.Log("SOULTAPE DISCOVERY OVERLAY PREVIEW: PASS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError("SOULTAPE DISCOVERY OVERLAY PREVIEW: FAIL");
                EditorApplication.Exit(1);
            }
        }

        private static void Render(
            string repository,
            string output,
            int width,
            int height,
            string fileName)
        {
            List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
            GameObject cameraObject = new GameObject("StatusPreviewCamera");
            owned.Add(cameraObject);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.045f, 0.055f, 0.060f, 1f);
            camera.orthographic = true;
            camera.orthographicSize = height * 0.5f;
            camera.aspect = width / (float)height;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.transform.rotation = Quaternion.identity;

            RenderTexture target = new RenderTexture(
                width, height, 24, RenderTextureFormat.ARGB32);
            target.Create();
            owned.Add(target);
            camera.targetTexture = target;

            AddBackdrop(width, height, owned);
            AddRecorderArtwork(repository, width, height, owned);
            AddStatusCard(width, height, owned);
            camera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            Texture2D image = new Texture2D(
                width, height, TextureFormat.RGBA32, false, false);
            image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            image.Apply(false, false);
            File.WriteAllBytes(Path.Combine(output, fileName), image.EncodeToPNG());
            RenderTexture.active = previous;
            owned.Add(image);

            foreach (UnityEngine.Object item in owned)
            {
                if (item != null)
                {
                    UnityEngine.Object.DestroyImmediate(item);
                }
            }
        }

        private static void RenderDiscovery(
            string output,
            int width,
            int height,
            string fileName)
        {
            List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
            GameObject cameraObject = new GameObject("DiscoveryPreviewCamera");
            owned.Add(cameraObject);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.052f, 0.063f, 0.066f, 1f);
            camera.orthographic = true;
            camera.orthographicSize = height * 0.5f;
            camera.aspect = width / (float)height;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.transform.rotation = Quaternion.identity;

            RenderTexture target = new RenderTexture(
                width, height, 24, RenderTextureFormat.ARGB32);
            target.Create();
            owned.Add(target);
            camera.targetTexture = target;

            AddSolidSprite("PreviewSky", 0f, -height * 0.20f,
                width, height * 0.62f,
                new Color(0.085f, 0.105f, 0.108f, 1f), 0, owned);
            AddSolidSprite("PreviewGround", 0f, height * 0.30f,
                width, height * 0.55f,
                new Color(0.021f, 0.028f, 0.030f, 1f), 1, owned);
            AddSolidSprite("PreviewStructure", -width * 0.17f, 20f,
                width * 0.32f, height * 0.28f,
                new Color(0.055f, 0.068f, 0.068f, 1f), 2, owned);
            AddDiscoveryCard(width, height, owned);
            camera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            Texture2D image = new Texture2D(
                width, height, TextureFormat.RGBA32, false, false);
            image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            image.Apply(false, false);
            File.WriteAllBytes(Path.Combine(output, fileName), image.EncodeToPNG());
            RenderTexture.active = previous;
            owned.Add(image);

            foreach (UnityEngine.Object item in owned)
            {
                if (item != null)
                {
                    UnityEngine.Object.DestroyImmediate(item);
                }
            }
        }

        private static void AddDiscoveryCard(
            int width,
            int height,
            ICollection<UnityEngine.Object> owned)
        {
            SoulRecorderStatusOverlayLayoutResult layout =
                SoulRecorderStatusOverlayLayout.Calculate(width, height, 0.18f);
            Texture2D panelTexture = CreateRoundedPanelTexture(64, 16f);
            owned.Add(panelTexture);
            Sprite panelSprite = Sprite.Create(panelTexture,
                new Rect(0f, 0f, panelTexture.width, panelTexture.height),
                new Vector2(0.5f, 0.5f), 1f, 0,
                SpriteMeshType.FullRect, new Vector4(14f, 14f, 14f, 14f));
            owned.Add(panelSprite);
            GameObject panelObject = new GameObject("DiscoveryPanel");
            owned.Add(panelObject);
            SpriteRenderer panel = panelObject.AddComponent<SpriteRenderer>();
            panel.sprite = panelSprite;
            panel.drawMode = SpriteDrawMode.Sliced;
            panel.size = new Vector2(layout.Panel.Width, layout.Panel.Height);
            panel.sortingOrder = 20;
            panelObject.transform.position = ScreenCenter(
                layout.Panel, width, height);

            AddSolidSprite("DiscoveryAccent",
                layout.Panel.X + 1f * layout.Scale,
                layout.Panel.Y + layout.Panel.Height * 0.5f,
                2f * layout.Scale,
                layout.Panel.Height - 24f * layout.Scale,
                new Color(0.34f, 0.65f, 0.62f, 0.78f), 21, owned, true,
                width, height);
            AddText("SOULTAPE DISCOVERED", layout.Heading,
                11f * layout.Scale,
                new Color(0.48f, 0.73f, 0.69f, 1f), true,
                width, height, 24, owned);
            AddText("Scott Buckley — The Long Dark", layout.Track,
                16f * layout.Scale,
                new Color(0.94f, 0.96f, 0.96f, 1f), false,
                width, height, 24, owned);
            AddText("Added to collection", layout.Status,
                10f * layout.Scale,
                new Color(0.60f, 0.67f, 0.67f, 1f), false,
                width, height, 24, owned);
            AddSolidSprite("DiscoveryRule",
                layout.ProgressTrack.X + layout.ProgressTrack.Width * 0.5f,
                layout.ProgressTrack.Y + layout.ProgressTrack.Height * 0.5f,
                layout.ProgressTrack.Width,
                layout.ProgressTrack.Height,
                new Color(0.34f, 0.58f, 0.56f, 0.45f), 22, owned, true,
                width, height);
        }

        private static void AddBackdrop(
            int width,
            int height,
            ICollection<UnityEngine.Object> owned)
        {
            AddSolidSprite("BackdropLower", 0f, height * 0.62f,
                width, height * 0.76f,
                new Color(0.018f, 0.024f, 0.027f, 1f), 0, owned);
        }

        private static void AddRecorderArtwork(
            string repository,
            int width,
            int height,
            ICollection<UnityEngine.Object> owned)
        {
            string folder = Path.Combine(repository,
                "Assets", "SoulRecorder", "Overlay");
            Texture2D recorder = LoadTexture(Path.Combine(folder,
                "soulrecorder-overlay-recorder.png"));
            Texture2D cassette = LoadTexture(Path.Combine(folder,
                "soulrecorder-overlay-cassette.png"));
            owned.Add(recorder);
            owned.Add(cassette);
            SoulRecorderOverlayPose pose =
                SoulRecorderOverlayTimeline.SampleInsertion(
                    width,
                    height,
                    SoulRecorderOverlayTimeline.InsertEnterSeconds +
                    SoulRecorderOverlayTimeline.InsertRotateSeconds + 0.16f,
                    SoulRecorderOverlaySettings.Default);
            AddTextureSprite("Cassette", cassette, pose.Cassette,
                -pose.CassetteRotationDegrees, pose.CassetteAlpha, 4, owned,
                width, height);
            AddTextureSprite("Recorder", recorder, pose.Recorder,
                0f, pose.RecorderAlpha, 5, owned, width, height);
        }

        private static void AddStatusCard(
            int width,
            int height,
            ICollection<UnityEngine.Object> owned)
        {
            SoulRecorderStatusOverlayLayoutResult layout =
                SoulRecorderStatusOverlayLayout.Calculate(width, height);
            Texture2D panelTexture = CreateRoundedPanelTexture(64, 16f);
            owned.Add(panelTexture);
            Sprite panelSprite = Sprite.Create(panelTexture,
                new Rect(0f, 0f, panelTexture.width, panelTexture.height),
                new Vector2(0.5f, 0.5f), 1f, 0,
                SpriteMeshType.FullRect, new Vector4(14f, 14f, 14f, 14f));
            owned.Add(panelSprite);
            GameObject panelObject = new GameObject("StatusPanel");
            owned.Add(panelObject);
            SpriteRenderer panel = panelObject.AddComponent<SpriteRenderer>();
            panel.sprite = panelSprite;
            panel.drawMode = SpriteDrawMode.Sliced;
            panel.size = new Vector2(layout.Panel.Width, layout.Panel.Height);
            panel.sortingOrder = 20;
            panelObject.transform.position = ScreenCenter(layout.Panel,
                width, height);

            AddSolidSprite("StatusAccent",
                layout.Panel.X + 1f * layout.Scale,
                layout.Panel.Y + layout.Panel.Height * 0.5f,
                2f * layout.Scale,
                layout.Panel.Height - 24f * layout.Scale,
                new Color(0.34f, 0.65f, 0.62f, 0.78f), 21, owned, true,
                width, height);
            AddText("SOULRECORDER", layout.Heading, 11f * layout.Scale,
                new Color(0.48f, 0.73f, 0.69f, 1f), true,
                width, height, 24, owned);
            AddText("Scott Buckley — The Long Dark", layout.Track,
                16f * layout.Scale,
                new Color(0.94f, 0.96f, 0.96f, 1f), false,
                width, height, 24, owned);
            AddText("INSERTING CASSETTE", layout.Status, 10f * layout.Scale,
                new Color(0.60f, 0.67f, 0.67f, 1f), false,
                width, height, 24, owned);
            AddSolidSprite("ProgressTrack",
                layout.ProgressTrack.X + layout.ProgressTrack.Width * 0.5f,
                layout.ProgressTrack.Y + layout.ProgressTrack.Height * 0.5f,
                layout.ProgressTrack.Width,
                layout.ProgressTrack.Height,
                new Color(0.25f, 0.40f, 0.40f, 0.30f), 22, owned, true,
                width, height);
            float segment = layout.ProgressTrack.Width *
                SoulRecorderStatusOverlayLayout.ProgressSegmentFraction;
            AddSolidSprite("ProgressSegment",
                layout.ProgressTrack.X + layout.ProgressTrack.Width * 0.55f +
                    segment * 0.5f,
                layout.ProgressTrack.Y + layout.ProgressTrack.Height * 0.5f,
                segment,
                layout.ProgressTrack.Height,
                new Color(0.38f, 0.72f, 0.69f, 0.92f), 23, owned, true,
                width, height);
        }

        private static void AddText(
            string value,
            SoulRecorderStatusOverlayRect rect,
            float pixelSize,
            Color color,
            bool bold,
            int width,
            int height,
            int sortingOrder,
            ICollection<UnityEngine.Object> owned)
        {
            GameObject item = new GameObject("Text-" + value);
            owned.Add(item);
            TextMesh text = item.AddComponent<TextMesh>();
            Font font = Font.CreateDynamicFontFromOSFont(
                bold
                    ? new[] { "Segoe UI Semibold", "Segoe UI", "Arial" }
                    : new[] { "Segoe UI", "Arial" },
                64);
            owned.Add(font);
            text.font = font;
            text.fontSize = 64;
            // TextMesh glyph units are substantially smaller than pixel units at
            // characterSize 1. This calibrated factor reproduces runtime IMGUI's
            // 10/11/16-pixel hierarchy in the orthographic pixel-space preview.
            text.characterSize = pixelSize / 12f;
            text.anchor = TextAnchor.UpperLeft;
            text.alignment = TextAlignment.Left;
            text.color = color;
            text.text = value;
            MeshRenderer renderer = item.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = font.material;
            renderer.sortingOrder = sortingOrder;
            item.transform.position = ScreenPoint(
                rect.X, rect.Y, width, height);
        }

        private static Texture2D LoadTexture(string path)
        {
            Texture2D texture = new Texture2D(
                2, 2, TextureFormat.RGBA32, false, false);
            if (!texture.LoadImage(File.ReadAllBytes(path), false))
            {
                throw new InvalidDataException(path);
            }
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            return texture;
        }

        private static void AddTextureSprite(
            string name,
            Texture2D texture,
            SoulRecorderOverlayRect rect,
            float rotation,
            float alpha,
            int order,
            ICollection<UnityEngine.Object> owned,
            int screenWidth,
            int screenHeight)
        {
            Sprite sprite = Sprite.Create(texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 1f);
            owned.Add(sprite);
            GameObject item = new GameObject(name);
            owned.Add(item);
            SpriteRenderer renderer = item.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = new Color(1f, 1f, 1f, alpha);
            renderer.sortingOrder = order;
            item.transform.position = ScreenCenter(rect,
                screenWidth, screenHeight);
            float sx = rect.Width / sprite.bounds.size.x;
            float sy = rect.Height / sprite.bounds.size.y;
            item.transform.localScale = new Vector3(sx, sy, 1f);
            item.transform.rotation = Quaternion.Euler(0f, 0f, rotation);
        }

        private static void AddSolidSprite(
            string name,
            float centerX,
            float centerY,
            float width,
            float height,
            Color color,
            int order,
            ICollection<UnityEngine.Object> owned,
            bool topLeftCoordinates = false,
            int screenWidth = 0,
            int screenHeight = 0)
        {
            Texture2D texture = new Texture2D(1, 1,
                TextureFormat.RGBA32, false, false);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply(false, true);
            owned.Add(texture);
            Sprite sprite = Sprite.Create(texture,
                new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            owned.Add(sprite);
            GameObject item = new GameObject(name);
            owned.Add(item);
            SpriteRenderer renderer = item.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = order;
            item.transform.localScale = new Vector3(width, height, 1f);
            item.transform.position = topLeftCoordinates
                ? ScreenPoint(centerX, centerY, screenWidth, screenHeight)
                : new Vector3(centerX, -centerY, 0f);
        }

        private static Texture2D CreateRoundedPanelTexture(int size, float radius)
        {
            Texture2D texture = new Texture2D(size, size,
                TextureFormat.RGBA32, false, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            Color fill = new Color(0.025f, 0.035f, 0.038f, 0.90f);
            Color border = new Color(0.27f, 0.47f, 0.46f, 0.64f);
            float half = (size - 1f) * 0.5f;
            for (int y = 0; y < size; y++)
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
            texture.Apply(false, false);
            return texture;
        }

        private static Vector3 ScreenCenter(
            SoulRecorderStatusOverlayRect rect,
            int width,
            int height)
        {
            return ScreenPoint(rect.X + rect.Width * 0.5f,
                rect.Y + rect.Height * 0.5f, width, height);
        }

        private static Vector3 ScreenCenter(
            SoulRecorderOverlayRect rect,
            int width,
            int height)
        {
            return ScreenPoint(rect.CenterX, rect.CenterY, width, height);
        }

        private static Vector3 ScreenPoint(
            float x,
            float y,
            int width,
            int height)
        {
            return new Vector3(x - width * 0.5f,
                height * 0.5f - y, 0f);
        }
    }
}
