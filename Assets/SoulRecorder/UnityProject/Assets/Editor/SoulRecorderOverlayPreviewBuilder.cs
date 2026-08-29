using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SoulPlayer.Recorder;
using UnityEditor;
using UnityEngine;

namespace SoulPlayer.Editor
{
    internal static class SoulRecorderOverlayPreviewBuilder
    {
        private const string RecorderPrefab =
            "Assets/SoulPlayer/Generated/soulrecorder_fp.prefab";
        private const string CassettePrefab =
            "Assets/SoulPlayer/Generated/soultape_cassette.prefab";
        private const int PreviewWidth = 1920;
        private const int PreviewHeight = 1080;
        private const int FramesPerSecond = 30;

        [MenuItem("SoulPlayer/SoulRecorder/Build 2D Overlay Preview")]
        internal static void BuildFromMenu()
        {
            Build();
            Debug.Log("SoulRecorder 2D overlay preview: PASS");
        }

        internal static void BatchBuild()
        {
            try
            {
                Build();
                Debug.Log("SOULRECORDER 2D OVERLAY PREVIEW: PASS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError("SOULRECORDER 2D OVERLAY PREVIEW: FAIL");
                EditorApplication.Exit(1);
            }
        }

        private static void Build()
        {
            string repository = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "..", "..", ".."));
            string assetFolder = Path.Combine(repository, "Assets",
                "SoulRecorder", "Overlay");
            string output = Path.Combine(repository, "Artifacts",
                "SoulRecorderOverlayPreview");
            Directory.CreateDirectory(assetFolder);
            Directory.CreateDirectory(output);

            string recorderPath = Path.Combine(assetFolder,
                "soulrecorder-overlay-recorder.png");
            string cassettePath = Path.Combine(assetFolder,
                "soulrecorder-overlay-cassette.png");
            // The generated recorder prefab presents its readable face toward
            // camera local -Z. The cassette's broad label face is local +Y,
            // with local +Z as image up. Capture each asset from that authored
            // presentation axis instead of assuming every prefab faces +Z.
            RenderPrefabAsset(RecorderPrefab, recorderPath, 768, 1024, 0.10f,
                Vector3.forward, Vector3.back, Vector3.up);
            RenderPrefabAsset(CassettePrefab, cassettePath, 768, 512, 0.15f,
                Vector3.up, Vector3.down, Vector3.forward);

            Texture2D recorder = LoadPng(recorderPath);
            Texture2D cassette = LoadPng(cassettePath);
            Material material = CreateTextureMaterial();
            try
            {
                string insertion = Path.Combine(output, "InsertionFrames");
                string ejection = Path.Combine(output, "EjectionFrames");
                RecreateFolder(insertion);
                RecreateFolder(ejection);
                SoulRecorderOverlaySettings settings =
                    SoulRecorderOverlaySettings.Default;
                float insertionSeconds =
                    SoulRecorderOverlayTimeline.InsertionSeconds(1f) +
                    SoulRecorderOverlayTimeline.PresentationFadeSeconds;
                float ejectionSeconds =
                    SoulRecorderOverlayTimeline.EjectionTotalSeconds(1f);
                RenderSequence(recorder, cassette, material, insertion,
                    insertionSeconds, true, settings);
                RenderSequence(recorder, cassette, material, ejection,
                    ejectionSeconds, false, settings);

                float reviewTime = SoulRecorderOverlayTimeline.InsertEnterSeconds +
                    SoulRecorderOverlayTimeline.InsertRotateSeconds +
                    SoulRecorderOverlayTimeline.InsertApproachSeconds * 0.72f;
                RenderFrame(recorder, cassette, material, PreviewWidth,
                    PreviewHeight, SoulRecorderOverlayTimeline.SampleInsertion(
                        PreviewWidth, PreviewHeight, reviewTime, settings),
                    Path.Combine(output, "soulrecorder-overlay-16x9.png"));
                RenderFrame(recorder, cassette, material, 3440, 1440,
                    SoulRecorderOverlayTimeline.SampleInsertion(
                        3440, 1440, reviewTime, settings),
                    Path.Combine(output, "soulrecorder-overlay-21x9.png"));
                RenderKeySheet(recorder, cassette, material, output, settings);
                WriteReport(output, insertionSeconds, ejectionSeconds,
                    recorder, cassette);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(recorder);
                UnityEngine.Object.DestroyImmediate(cassette);
            }
            AssetDatabase.Refresh();
        }

        private static void RenderSequence(Texture2D recorder,
            Texture2D cassette, Material material, string folder,
            float seconds, bool insertion, SoulRecorderOverlaySettings settings)
        {
            int frames = Mathf.CeilToInt(seconds * FramesPerSecond) + 1;
            float insertionMotion =
                SoulRecorderOverlayTimeline.InsertionSeconds(1f);
            for (int frame = 0; frame < frames; frame++)
            {
                float time = frame / (float)FramesPerSecond;
                SoulRecorderOverlayPose pose;
                if (insertion)
                {
                    pose = SoulRecorderOverlayTimeline.SampleInsertion(
                        PreviewWidth, PreviewHeight,
                        Mathf.Min(time, insertionMotion), settings);
                    if (time > insertionMotion)
                    {
                        pose = SoulRecorderOverlayTimeline.ApplyExitFade(pose,
                            time - insertionMotion, 1f);
                    }
                }
                else
                {
                    pose = SoulRecorderOverlayTimeline.SampleEjection(
                        PreviewWidth, PreviewHeight, time, settings);
                }
                RenderFrame(recorder, cassette, material, PreviewWidth,
                    PreviewHeight, pose, Path.Combine(folder,
                        "frame-" + frame.ToString("D4",
                            CultureInfo.InvariantCulture) + ".png"));
            }
        }

        private static void RenderKeySheet(Texture2D recorder,
            Texture2D cassette, Material material, string output,
            SoulRecorderOverlaySettings settings)
        {
            float[] times =
            {
                0.14f,
                SoulRecorderOverlayTimeline.InsertEnterSeconds + 0.18f,
                SoulRecorderOverlayTimeline.InsertEnterSeconds +
                    SoulRecorderOverlayTimeline.InsertRotateSeconds + 0.14f,
                SoulRecorderOverlayTimeline.InsertEnterSeconds +
                    SoulRecorderOverlayTimeline.InsertRotateSeconds +
                    SoulRecorderOverlayTimeline.InsertApproachSeconds + 0.08f,
                SoulRecorderOverlayTimeline.InsertionSeconds(1f),
                SoulRecorderOverlayTimeline.EjectStartSeconds + 0.08f,
                SoulRecorderOverlayTimeline.EjectStartSeconds +
                    SoulRecorderOverlayTimeline.EjectPopSeconds + 0.14f,
                SoulRecorderOverlayTimeline.EjectionTotalSeconds(1f) - 0.12f
            };
            const int columns = 4;
            const int rows = 2;
            const int sheetWidth = 1920;
            const int sheetHeight = 1080;
            RenderTexture target = NewTarget(sheetWidth, sheetHeight);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            GL.Clear(true, true, Color.black);
            for (int index = 0; index < times.Length; index++)
            {
                bool insertion = index < 5;
                SoulRecorderOverlayPose pose = insertion
                    ? SoulRecorderOverlayTimeline.SampleInsertion(
                        PreviewWidth, PreviewHeight, times[index], settings)
                    : SoulRecorderOverlayTimeline.SampleEjection(
                        PreviewWidth, PreviewHeight, times[index], settings);
                int column = index % columns;
                int row = index / columns;
                DrawKeyPose(material, recorder, cassette, pose,
                    column, row, columns, rows, sheetWidth, sheetHeight);
            }
            SaveActiveRenderTexture(target, sheetWidth, sheetHeight,
                Path.Combine(output, "soulrecorder-overlay-key-states.png"));
            RenderTexture.active = previous;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }

        private static void DrawKeyPose(Material material, Texture2D recorder,
            Texture2D cassette, SoulRecorderOverlayPose pose, int column,
            int row, int columns, int rows, int sheetWidth, int sheetHeight)
        {
            // Crop the unchanged 1920x1080 runtime composition to its lower-right
            // authoring region, then fit that crop into a sheet cell. This keeps
            // the exact timeline coordinates while making cassette motion readable.
            const float cropX = 1200f;
            const float cropY = 410f;
            const float cropWidth = 720f;
            const float cropHeight = 670f;
            float cellWidth = sheetWidth / (float)columns;
            float cellHeight = sheetHeight / (float)rows;
            float scale = Mathf.Min(cellWidth / cropWidth,
                cellHeight / cropHeight);
            float offsetX = column * cellWidth +
                (cellWidth - cropWidth * scale) * 0.5f - cropX * scale;
            float offsetY = row * cellHeight +
                (cellHeight - cropHeight * scale) * 0.5f - cropY * scale;
            DrawPose(material, recorder, cassette, pose, offsetX, offsetY,
                scale, scale, sheetWidth, sheetHeight);
        }

        private static void RenderFrame(Texture2D recorder,
            Texture2D cassette, Material material, int width, int height,
            SoulRecorderOverlayPose pose, string path)
        {
            RenderTexture target = NewTarget(width, height);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            GL.Clear(true, true, Color.black);
            DrawPose(material, recorder, cassette, pose, 0f, 0f, 1f, 1f,
                width, height);
            SaveActiveRenderTexture(target, width, height, path);
            RenderTexture.active = previous;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }

        private static void DrawPose(Material material, Texture2D recorder,
            Texture2D cassette, SoulRecorderOverlayPose pose,
            float offsetX, float offsetY, float scaleX, float scaleY,
            int targetWidth, int targetHeight)
        {
            SoulRecorderOverlayRect cassetteRect = Transform(pose.Cassette,
                offsetX, offsetY, scaleX, scaleY);
            SoulRecorderOverlayRect recorderRect = Transform(pose.Recorder,
                offsetX, offsetY, scaleX, scaleY);
            DrawTexture(material, cassette, cassetteRect,
                pose.CassetteRotationDegrees, pose.CassetteAlpha,
                targetWidth, targetHeight);
            DrawTexture(material, recorder, recorderRect, 0f,
                pose.RecorderAlpha, targetWidth, targetHeight);
        }

        private static SoulRecorderOverlayRect Transform(
            SoulRecorderOverlayRect value, float x, float y, float sx, float sy)
        {
            value.X = x + value.X * sx;
            value.Y = y + value.Y * sy;
            value.Width *= sx;
            value.Height *= sy;
            return value;
        }

        private static void DrawTexture(Material material, Texture texture,
            SoulRecorderOverlayRect rect, float degrees, float alpha,
            int width, int height)
        {
            if (alpha <= 0.001f) return;
            material.mainTexture = texture;
            material.color = new Color(1f, 1f, 1f, alpha);
            material.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, width, height, 0f);
            GL.Begin(GL.QUADS);
            GL.Color(new Color(1f, 1f, 1f, alpha));
            Vector2 center = new Vector2(rect.CenterX, rect.CenterY);
            Vector2[] points =
            {
                new Vector2(rect.X, rect.Y),
                new Vector2(rect.X + rect.Width, rect.Y),
                new Vector2(rect.X + rect.Width, rect.Y + rect.Height),
                new Vector2(rect.X, rect.Y + rect.Height)
            };
            float radians = degrees * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            for (int index = 0; index < points.Length; index++)
            {
                Vector2 delta = points[index] - center;
                points[index] = center + new Vector2(
                    delta.x * cosine - delta.y * sine,
                    delta.x * sine + delta.y * cosine);
            }
            Vector2[] uv =
            {
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(1f, 0f), new Vector2(0f, 0f)
            };
            for (int index = 0; index < 4; index++)
            {
                GL.TexCoord2(uv[index].x, uv[index].y);
                GL.Vertex3(points[index].x, points[index].y, 0f);
            }
            GL.End();
            GL.PopMatrix();
        }

        private static void RenderPrefabAsset(string prefabPath,
            string outputPath, int width, int height, float padding,
            Vector3 cameraDirection, Vector3 cameraForward, Vector3 cameraUp)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) throw new FileNotFoundException(prefabPath);
            GameObject root = UnityEngine.Object.Instantiate(prefab);
            GameObject cameraObject = new GameObject("OverlayAssetCamera");
            GameObject lightObject = new GameObject("OverlayAssetLight");
            try
            {
                Bounds bounds = BoundsOf(root);
                root.transform.position -= bounds.center;
                bounds = BoundsOf(root);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                camera.orthographic = true;
                camera.transform.position = cameraDirection.normalized * 2f;
                camera.transform.rotation = Quaternion.LookRotation(
                    cameraForward.normalized, cameraUp.normalized);
                float aspect = width / (float)height;
                camera.orthographicSize = Mathf.Max(bounds.extents.y,
                    bounds.extents.x / aspect) * (1f + padding);
                DirectionalLight(lightObject);
                RenderTexture target = NewTarget(width, height);
                camera.targetTexture = target;
                camera.Render();
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = target;
                SaveActiveRenderTexture(target, width, height, outputPath);
                RenderTexture.active = previous;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(lightObject);
            }
        }

        private static void DirectionalLight(GameObject lightObject)
        {
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.color = new Color(1f, 0.96f, 0.90f);
            light.transform.rotation = Quaternion.Euler(35f, -35f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.38f, 0.40f, 0.44f);
        }

        private static Bounds BoundsOf(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException(
                root.name + " has no renderers");
            Bounds result = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
                result.Encapsulate(renderers[index].bounds);
            return result;
        }

        private static Material CreateTextureMaterial()
        {
            Shader shader = Shader.Find("Unlit/Transparent") ??
                Shader.Find("Sprites/Default");
            if (shader == null) throw new InvalidOperationException(
                "No transparent preview shader is available.");
            return new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        private static RenderTexture NewTarget(int width, int height)
        {
            RenderTexture target = new RenderTexture(width, height, 24,
                RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1,
                hideFlags = HideFlags.HideAndDontSave
            };
            target.Create();
            return target;
        }

        private static void SaveActiveRenderTexture(RenderTexture target,
            int width, int height, string path)
        {
            Texture2D image = new Texture2D(width, height,
                TextureFormat.RGBA32, false, false);
            try
            {
                image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                image.Apply(false, false);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(image);
            }
        }

        private static Texture2D LoadPng(string path)
        {
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32,
                false, false);
            if (!texture.LoadImage(File.ReadAllBytes(path), false))
                throw new InvalidDataException(path);
            return texture;
        }

        private static void RecreateFolder(string path)
        {
            Directory.CreateDirectory(path);
            foreach (string file in Directory.GetFiles(path, "*.png"))
                File.Delete(file);
        }

        private static void WriteReport(string output, float insertion,
            float ejection, Texture2D recorder, Texture2D cassette)
        {
            string text = "SoulRecorder 2D Overlay Preview\n" +
                "Runtime and preview math: SoulRecorderOverlayTimeline\n" +
                "Hands/arms/IK/world-space dependencies: NONE\n" +
                "Insertion motion seconds: " +
                    SoulRecorderOverlayTimeline.InsertionSeconds(1f).ToString("F3",
                    CultureInfo.InvariantCulture) + "\n" +
                "Insertion including exit fade: " + insertion.ToString("F3",
                    CultureInfo.InvariantCulture) + "\n" +
                "Ejection seconds: " + ejection.ToString("F3",
                    CultureInfo.InvariantCulture) + "\n" +
                "Recorder PNG: " + recorder.width + "x" + recorder.height + "\n" +
                "Cassette PNG: " + cassette.width + "x" + cassette.height + "\n" +
                "Preview resolutions: 1920x1080 and 3440x1440\n";
            File.WriteAllText(Path.Combine(output, "overlay-preview-report.txt"),
                text);
        }
    }
}
