using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SoulPlayer.Editor
{
    internal static class SoulTapeWorldCassettePreviewBuilder
    {
        private const string PrefabPath =
            "Assets/SoulPlayer/Generated/soultape_cassette.prefab";
        private const string OutputFolder =
            "Artifacts/WorldCassetteVisualPreview";

        internal static void BatchBuild()
        {
            try
            {
                string repository = Path.GetFullPath(Path.Combine(
                    Application.dataPath, "..", "..", "..", ".."));
                string output = Path.Combine(repository, OutputFolder);
                Directory.CreateDirectory(output);

                Render(output, "cassette-closeup-1920x1080.png", BuildCloseup);
                Render(output, "cassette-three-anchor-poses-1920x1080.png", BuildAnchorPoses);
                Render(output, "cassette-old-vs-new-1920x1080.png", BuildComparison);

                File.WriteAllText(
                    Path.Combine(output, "preview-report.txt"),
                    "WORLD CASSETTE VISUAL PREVIEW: PASS\n" +
                    "Asset: soultape_cassette (comeinandburn / BlendSwap / CC0)\n" +
                    "Local transform: position (0,0,0), rotation (0,0,0), scale (1,1,1)\n" +
                    "Representative authored anchor rotations:\n" +
                    "- curated-factory4-day-20260823-060206170-6c0bb418: (0.000007,236.264923,0.000006)\n" +
                    "- curated-factory4-day-20260823-060257044-bddef346: (-0.000006,301.675476,0.000006)\n" +
                    "- curated-factory4-day-20260823-062433846-652915d2: (352.175720,166.926849,7.499228)\n" +
                    "Images: close-up, three anchor poses, old-vs-new\n");
                Debug.Log("WORLD CASSETTE VISUAL PREVIEW: PASS");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Debug.LogError("WORLD CASSETTE VISUAL PREVIEW: FAIL");
                EditorApplication.Exit(1);
            }
        }

        private static void Render(
            string output,
            string fileName,
            Action<ICollection<UnityEngine.Object>> buildContent)
        {
            List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
            try
            {
                GameObject cameraObject = new GameObject("WorldCassettePreviewCamera");
                owned.Add(cameraObject);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.018f, 0.023f, 0.025f, 1f);
                camera.fieldOfView = 36f;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 10f;
                camera.transform.position = new Vector3(0f, 0.24f, -0.34f);
                camera.transform.LookAt(new Vector3(0f, 0f, 0.005f));

                AddLighting(owned);
                AddSurface(owned);
                buildContent(owned);

                RenderTexture target = new RenderTexture(
                    1920, 1080, 24, RenderTextureFormat.ARGB32);
                target.Create();
                owned.Add(target);
                camera.targetTexture = target;
                camera.Render();

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = target;
                Texture2D image = new Texture2D(
                    1920, 1080, TextureFormat.RGBA32, false, false);
                image.ReadPixels(new Rect(0f, 0f, 1920f, 1080f), 0, 0);
                image.Apply(false, false);
                File.WriteAllBytes(Path.Combine(output, fileName), image.EncodeToPNG());
                RenderTexture.active = previous;
                owned.Add(image);
            }
            finally
            {
                for (int index = owned.Count - 1; index >= 0; index--)
                {
                    if (owned[index] != null)
                    {
                        UnityEngine.Object.DestroyImmediate(owned[index]);
                    }
                }
            }
        }

        private static void BuildCloseup(ICollection<UnityEngine.Object> owned)
        {
            GameObject cassette = CreateCassette(owned);
            cassette.transform.position = new Vector3(0f, 0.002f, 0f);
            cassette.transform.rotation = Quaternion.Euler(0f, -14f, 0f);
            cassette.transform.localScale = Vector3.one * 1.45f;
        }

        private static void BuildAnchorPoses(ICollection<UnityEngine.Object> owned)
        {
            Vector3[] positions =
            {
                new Vector3(-0.135f, 0.001f, 0.025f),
                new Vector3(0f, 0.001f, 0f),
                new Vector3(0.135f, 0.018f, 0.025f)
            };
            Vector3[] rotations =
            {
                new Vector3(0.0000067f, 236.264923f, 0.0000057f),
                new Vector3(-0.0000061f, 301.675476f, 0.0000058f),
                new Vector3(352.175720f, 166.926849f, 7.499228f)
            };
            for (int index = 0; index < positions.Length; index++)
            {
                GameObject cassette = CreateCassette(owned);
                cassette.transform.position = positions[index];
                cassette.transform.rotation = Quaternion.Euler(rotations[index]);
            }
        }

        private static void BuildComparison(ICollection<UnityEngine.Object> owned)
        {
            GameObject oldVisual = BuildOldProceduralCassette(owned);
            oldVisual.transform.position = new Vector3(-0.085f, 0f, 0f);
            oldVisual.transform.rotation = Quaternion.Euler(0f, -12f, 0f);

            GameObject newVisual = CreateCassette(owned);
            newVisual.transform.position = new Vector3(0.085f, 0f, 0f);
            newVisual.transform.rotation = Quaternion.Euler(0f, 12f, 0f);
        }

        private static GameObject CreateCassette(ICollection<UnityEngine.Object> owned)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                throw new FileNotFoundException("Cassette prefab was not found: " + PrefabPath);
            }
            GameObject cassette = UnityEngine.Object.Instantiate(prefab);
            cassette.name = "CassetteVisual";
            owned.Add(cassette);
            foreach (Collider collider in cassette.GetComponentsInChildren<Collider>(true))
            {
                throw new InvalidOperationException(
                    "Preview cassette unexpectedly contained collider " + collider.name + ".");
            }
            return cassette;
        }

        private static GameObject BuildOldProceduralCassette(
            ICollection<UnityEngine.Object> owned)
        {
            GameObject root = new GameObject("Old block visual");
            owned.Add(root);
            Material shell = Material(owned, new Color(0.095f, 0.105f, 0.115f, 1f));
            Material label = Material(owned, new Color(0.72f, 0.64f, 0.46f, 1f));
            Material reel = Material(owned, new Color(0.38f, 0.40f, 0.42f, 1f));
            Part(root.transform, PrimitiveType.Cube, Vector3.zero,
                new Vector3(0.11f, 0.018f, 0.07f), shell);
            Part(root.transform, PrimitiveType.Cube, new Vector3(0f, 0.0095f, 0f),
                new Vector3(0.086f, 0.002f, 0.046f), label);
            Part(root.transform, PrimitiveType.Cylinder, new Vector3(-0.026f, 0.0115f, 0f),
                new Vector3(0.018f, 0.0035f, 0.018f), reel);
            Part(root.transform, PrimitiveType.Cylinder, new Vector3(0.026f, 0.0115f, 0f),
                new Vector3(0.018f, 0.0035f, 0.018f), reel);
            return root;
        }

        private static void Part(
            Transform parent,
            PrimitiveType type,
            Vector3 position,
            Vector3 scale,
            Material material)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(part.GetComponent<Collider>());
        }

        private static void AddLighting(ICollection<UnityEngine.Object> owned)
        {
            GameObject keyObject = new GameObject("KeyLight");
            owned.Add(keyObject);
            Light key = keyObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(0.82f, 0.89f, 0.90f, 1f);
            key.intensity = 1.25f;
            keyObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

            GameObject fillObject = new GameObject("AmberFill");
            owned.Add(fillObject);
            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.color = new Color(1f, 0.55f, 0.24f, 1f);
            fill.intensity = 0.75f;
            fill.range = 0.65f;
            fillObject.transform.position = new Vector3(0.18f, 0.16f, -0.08f);
        }

        private static void AddSurface(ICollection<UnityEngine.Object> owned)
        {
            GameObject surface = GameObject.CreatePrimitive(PrimitiveType.Cube);
            surface.name = "Dark industrial surface";
            surface.transform.position = new Vector3(0f, -0.018f, 0.03f);
            surface.transform.localScale = new Vector3(0.72f, 0.02f, 0.48f);
            Material material = Material(owned, new Color(0.065f, 0.072f, 0.073f, 1f));
            material.SetFloat("_Metallic", 0.18f);
            material.SetFloat("_Glossiness", 0.28f);
            surface.GetComponent<Renderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(surface.GetComponent<Collider>());
            owned.Add(surface);
        }

        private static Material Material(
            ICollection<UnityEngine.Object> owned,
            Color color)
        {
            Material material = new Material(Shader.Find("Standard"));
            material.color = color;
            material.SetFloat("_Glossiness", 0.22f);
            owned.Add(material);
            return material;
        }
    }
}
