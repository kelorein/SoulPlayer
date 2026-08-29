using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace SoulPlayer.Editor
{
    public static class SoulRecorderAssetBundleBuilder
    {
        private const string RecorderModelPath =
            "Assets/SoulPlayer/Processed/soulrecorder_fp.fbx";
        private const string CassetteModelPath =
            "Assets/SoulPlayer/Processed/soultape_cassette.fbx";
        private const string HandsModelPath =
            "Assets/SoulPlayer/Processed/soulrecorder_bamen_rig.fbx";
        private const string TextureFolder = "Assets/SoulPlayer/Processed/textures";
        private const string RecorderBaseColorPath =
            TextureFolder + "/soulrecorder_body_basecolor.png";
        private const string RecorderFlapColorPath =
            TextureFolder + "/soulrecorder_flap_basecolor.png";
        private const string RecorderNormalPath =
            TextureFolder + "/soulrecorder_body_normal.png";
        private const string RecorderMetallicSmoothnessPath =
            TextureFolder + "/soulrecorder_body_metallic_smoothness.png";
        private const string ArmsBaseColorPath =
            TextureFolder + "/soulrecorder_arms_basecolor.png";
        private const string ArmsNormalPath =
            TextureFolder + "/soulrecorder_arms_normal.png";
        private const string ArmsMetallicSmoothnessPath =
            TextureFolder + "/soulrecorder_arms_metallic_smoothness.png";
        private const string HandsBaseColorPath =
            TextureFolder + "/soulrecorder_hands_basecolor.png";
        private const string HandsNormalPath =
            TextureFolder + "/soulrecorder_hands_normal.png";
        private const string HandsMetallicSmoothnessPath =
            TextureFolder + "/soulrecorder_hands_metallic_smoothness.png";
        private const string HandsShaderPath =
            "Assets/SoulPlayer/Shaders/SoulPlayerRecorderHands.shader";
        private const string HandsShaderName = "SoulPlayer/Recorder Hands PBR";
        private const string GeneratedFolder = "Assets/SoulPlayer/Generated";
        private const string RecorderPrefabPath = GeneratedFolder + "/soulrecorder_fp.prefab";
        private const string CassettePrefabPath = GeneratedFolder + "/soultape_cassette.prefab";
        private const string WorldCassetteBundleFileName = "soultape_world.bundle";
        private const string HandsPrefabPath =
            GeneratedFolder + "/soulrecorder_animated_hands.prefab";
        private const string HandsAnimatorPath =
            GeneratedFolder + "/SoulRecorderAnimatedHands.controller";
        private const string RecorderBodyMaterialPath =
            GeneratedFolder + "/SoulPlayerRecorderBody.mat";
        private const string RecorderFlapMaterialPath =
            GeneratedFolder + "/SoulPlayerRecorderFlap.mat";
        private const string ArmsMaterialPath =
            GeneratedFolder + "/SoulPlayerRecorderArms.mat";
        private const string HandsMaterialPath =
            GeneratedFolder + "/SoulPlayerRecorderHands.mat";
        private static readonly Vector3 ExpectedRecorderBounds =
            new Vector3(0.109333f, 0.200000f, 0.041281f);
        private static readonly Vector3 ExpectedCassetteBounds =
            new Vector3(0.110000f, 0.018200f, 0.070000f);
        private static readonly string[] RequiredAnimationClips =
        {
            "SoulRecorder_Enter", "SoulRecorder_Insert", "SoulRecorder_StartExit",
            "SoulRecorder_Hold", "SoulRecorder_StopEnter", "SoulRecorder_Eject",
            "SoulRecorder_StopExit", "SoulRecorder_CancelInsert"
        };
        private static readonly Quaternion RecorderModelCorrection =
            Quaternion.Euler(-90f, 0f, 0f);
        private static readonly Quaternion CassetteModelCorrection =
            Quaternion.Euler(-90f, 0f, 0f);

        public static void Build()
        {
            RequireEditorVersion();
            ConfigureWindowsPlayerTarget();
            Directory.CreateDirectory(GeneratedFolder);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureHandsModelImporter();

            BuildMaterials();
            BuildRecorderPrefab();
            BuildCassettePrefab();
            BuildHandsPrefab();
            AssetDatabase.SaveAssets();
            ValidateTextureDependencies(
                RecorderPrefabPath,
                new[]
                {
                    RecorderBaseColorPath,
                    RecorderFlapColorPath,
                    RecorderNormalPath,
                    RecorderMetallicSmoothnessPath
                },
                "soulrecorder_fp");
            ValidateTextureDependencies(
                HandsPrefabPath,
                new[]
                {
                    ArmsBaseColorPath, ArmsNormalPath, ArmsMetallicSmoothnessPath,
                    HandsBaseColorPath, HandsNormalPath, HandsMetallicSmoothnessPath
                },
                "soulrecorder_animated_hands");

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string output = Path.GetFullPath(Path.Combine(projectRoot, "..", "bundle"));
            Directory.CreateDirectory(output);
            AssetBundleBuild build = new AssetBundleBuild
            {
                assetBundleName = "soulplayer_assets.bundle",
                assetNames = new[] { RecorderPrefabPath, CassettePrefabPath, HandsPrefabPath },
                addressableNames = new[]
                {
                    "soulrecorder_fp",
                    "soultape_cassette",
                    "soulrecorder_animated_hands"
                }
            };
            BuildAssetBundleOptions options =
                BuildAssetBundleOptions.ChunkBasedCompression |
                BuildAssetBundleOptions.ForceRebuildAssetBundle |
                BuildAssetBundleOptions.StrictMode |
                BuildAssetBundleOptions.DeterministicAssetBundle;
            Debug.Log("SoulPlayer AssetBundle options: " + options);
            AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
                output,
                new[] { build },
                options,
                BuildTarget.StandaloneWindows64);
            string finalBundlePath = Path.Combine(output, "soulplayer_assets.bundle");
            if (manifest == null || !File.Exists(finalBundlePath))
            {
                throw new InvalidOperationException("Unity did not produce soulplayer_assets.bundle.");
            }

            ValidateBuiltBundle(finalBundlePath);
            BuildAndValidateWorldCassetteBundle(output, options);
        }

        private static void BuildAndValidateWorldCassetteBundle(
            string output,
            BuildAssetBundleOptions options)
        {
            string temporary = Path.Combine(output, "world-cassette-build");
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, true);
            }
            Directory.CreateDirectory(temporary);

            try
            {
                AssetBundleBuild build = new AssetBundleBuild
                {
                    assetBundleName = WorldCassetteBundleFileName,
                    assetNames = new[] { CassettePrefabPath },
                    addressableNames = new[] { "soultape_cassette" }
                };
                AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
                    temporary,
                    new[] { build },
                    options,
                    BuildTarget.StandaloneWindows64);
                string temporaryBundle = Path.Combine(
                    temporary,
                    WorldCassetteBundleFileName);
                if (manifest == null || !File.Exists(temporaryBundle))
                {
                    throw new InvalidOperationException(
                        "Unity did not produce " + WorldCassetteBundleFileName + ".");
                }

                ValidateWorldCassetteBundle(temporaryBundle);
                File.Copy(
                    temporaryBundle,
                    Path.Combine(output, WorldCassetteBundleFileName),
                    true);
                File.Copy(
                    temporaryBundle + ".manifest",
                    Path.Combine(output, WorldCassetteBundleFileName + ".manifest"),
                    true);
            }
            finally
            {
                if (Directory.Exists(temporary))
                {
                    Directory.Delete(temporary, true);
                }
            }
        }

        private static void ValidateWorldCassetteBundle(string bundlePath)
        {
            AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
            if (bundle == null)
            {
                throw new InvalidOperationException(
                    "World cassette bundle failed its same-editor load test.");
            }

            try
            {
                GameObject prefab = bundle.LoadAsset<GameObject>("soultape_cassette");
                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        "World cassette bundle did not contain soultape_cassette.");
                }

                GameObject cassette = UnityEngine.Object.Instantiate(prefab);
                try
                {
                    RequireLoadedTransform(cassette, "SoulTapeCassette", "soultape_world.bundle");
                    RequireLoadedTransform(cassette, "Shell", "soultape_world.bundle");
                    RequireLoadedTransform(cassette, "Label", "soultape_world.bundle");
                    RequireLoadedTransform(cassette, "ReelLeft", "soultape_world.bundle");
                    RequireLoadedTransform(cassette, "ReelRight", "soultape_world.bundle");
                    Renderer[] renderers = cassette.GetComponentsInChildren<Renderer>(true);
                    if (CountUsableRenderers(renderers) == 0)
                    {
                        throw new InvalidOperationException(
                            "World cassette bundle had no usable renderer geometry.");
                    }
                    if (cassette.GetComponentsInChildren<Collider>(true).Length != 0)
                    {
                        throw new InvalidOperationException(
                            "World cassette visual contained a collider.");
                    }
                    Bounds bounds = CalculateLocalRendererBounds(
                        cassette.transform,
                        renderers);
                    ValidateCassetteBounds(bounds.size);
                    Debug.Log("[PASS] soultape_world.bundle exact visual-only prefab");
                    Debug.Log("[PASS] soultape_world.bundle contains no colliders");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(cassette);
                }
            }
            finally
            {
                bundle.Unload(true);
            }
        }

        private static void RequireEditorVersion()
        {
            if (!Application.unityVersion.StartsWith("2022.3.43f1", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "SoulPlayer bundles must be built with Unity 2022.3.43f1; current editor is " +
                    Application.unityVersion + ".");
            }
        }

        private static void ConfigureWindowsPlayerTarget()
        {
            const BuildTargetGroup targetGroup = BuildTargetGroup.Standalone;
            const BuildTarget target = BuildTarget.StandaloneWindows64;
            if (!BuildPipeline.IsBuildTargetSupported(targetGroup, target))
            {
                throw new InvalidOperationException(
                    "Unity Windows Standalone support is not installed for " + target + ".");
            }

            if (EditorUserBuildSettings.activeBuildTarget != target ||
                EditorUserBuildSettings.selectedStandaloneTarget != target)
            {
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(targetGroup, target))
                {
                    throw new InvalidOperationException(
                        "Unity could not switch to StandaloneWindows64.");
                }
            }

            EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
            PlayerSettings.stripEngineCode = false;
            if (EditorUserBuildSettings.activeBuildTarget != target ||
                EditorUserBuildSettings.selectedStandaloneTarget != target ||
                EditorUserBuildSettings.standaloneBuildSubtarget !=
                    StandaloneBuildSubtarget.Player ||
                PlayerSettings.stripEngineCode)
            {
                throw new InvalidOperationException(
                    "Unity did not retain the required StandaloneWindows64 Player target " +
                    "with engine-code stripping disabled.");
            }

            Debug.Log("SoulPlayer AssetBundle build environment:");
            Debug.Log("Application.unityVersion: " + Application.unityVersion);
            Debug.Log("EditorUserBuildSettings.activeBuildTarget: " +
                EditorUserBuildSettings.activeBuildTarget);
            Debug.Log("EditorUserBuildSettings.selectedStandaloneTarget: " +
                EditorUserBuildSettings.selectedStandaloneTarget);
            Debug.Log("EditorUserBuildSettings.standaloneBuildSubtarget: " +
                EditorUserBuildSettings.standaloneBuildSubtarget);
            Debug.Log("PlayerSettings.stripEngineCode: " + PlayerSettings.stripEngineCode);
            Debug.Log("SystemInfo.graphicsDeviceType: " + SystemInfo.graphicsDeviceType);
        }

        private static void ConfigureHandsModelImporter()
        {
            ModelImporter importer = AssetImporter.GetAtPath(HandsModelPath) as ModelImporter;
            if (importer == null)
            {
                throw new FileNotFoundException(
                    "BAMEN animated-hands model importer was unavailable: " + HandsModelPath);
            }

            ModelImporterClipAnimation[] defaults = importer.defaultClipAnimations;
            string takeName = defaults != null && defaults.Length > 0
                ? defaults[0].takeName
                : "SoulRecorder_Master";
            importer.importAnimation = true;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.importConstraints = false;
            // Blender's FBX exporter records its meter-to-centimeter and axis conversion
            // as 100x/-90-degree hierarchy nodes. Bake that conversion into the imported
            // model so bones, sockets, and attached props all retain a unit-scale chain.
            importer.bakeAxisConversion = true;
            importer.clipAnimations = new[]
            {
                Clip(takeName, "SoulRecorder_Enter", 0, 25, false),
                Clip(takeName, "SoulRecorder_Insert", 26, 72, false),
                Clip(takeName, "SoulRecorder_StartExit", 73, 96, false),
                Clip(takeName, "SoulRecorder_Hold", 97, 109, true),
                Clip(takeName, "SoulRecorder_StopEnter", 110, 133, false),
                Clip(takeName, "SoulRecorder_Eject", 134, 175, false),
                Clip(takeName, "SoulRecorder_StopExit", 176, 199, false),
                Clip(takeName, "SoulRecorder_CancelInsert", 200, 218, false)
            };
            importer.SaveAndReimport();
        }

        private static ModelImporterClipAnimation Clip(
            string takeName,
            string name,
            int firstFrame,
            int lastFrame,
            bool loop)
        {
            return new ModelImporterClipAnimation
            {
                takeName = takeName,
                name = name,
                firstFrame = firstFrame,
                lastFrame = lastFrame,
                loopTime = loop,
                loopPose = loop,
                keepOriginalOrientation = true,
                keepOriginalPositionY = true,
                keepOriginalPositionXZ = true,
                lockRootRotation = true,
                lockRootHeightY = true,
                lockRootPositionXZ = true
            };
        }

        private static void BuildMaterials()
        {
            ConfigureNormalMap(RecorderNormalPath);
            ConfigureNormalMap(ArmsNormalPath);
            ConfigureNormalMap(HandsNormalPath);
            ConfigureLinearDataMap(RecorderMetallicSmoothnessPath);
            ConfigureLinearDataMap(ArmsMetallicSmoothnessPath);
            ConfigureLinearDataMap(HandsMetallicSmoothnessPath);

            Shader standard = Shader.Find("Standard");
            if (standard == null)
            {
                throw new InvalidOperationException(
                    "Unity Standard shader was unavailable for SoulRecorder materials.");
            }
            Shader handsShader = AssetDatabase.LoadAssetAtPath<Shader>(HandsShaderPath);
            if (handsShader == null ||
                !string.Equals(handsShader.name, HandsShaderName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "SoulPlayer-owned recorder-hands PBR shader was unavailable: " +
                    HandsShaderPath);
            }

            Texture2D bodyColor = RequireTexture(RecorderBaseColorPath);
            Texture2D flapColor = RequireTexture(RecorderFlapColorPath);
            Texture2D normal = RequireTexture(RecorderNormalPath);
            Texture2D metallicSmoothness = RequireTexture(
                RecorderMetallicSmoothnessPath);
            Texture2D armsColor = RequireTexture(ArmsBaseColorPath);
            Texture2D armsNormal = RequireTexture(ArmsNormalPath);
            Texture2D armsMetallicSmoothness = RequireTexture(
                ArmsMetallicSmoothnessPath);
            Texture2D handsColor = RequireTexture(HandsBaseColorPath);
            Texture2D handsNormal = RequireTexture(HandsNormalPath);
            Texture2D handsMetallicSmoothness = RequireTexture(
                HandsMetallicSmoothnessPath);

            Material body = CreateStandardMaterial(
                RecorderBodyMaterialPath,
                "SoulPlayer Recorder Body",
                standard,
                bodyColor,
                normal,
                metallicSmoothness);
            body.SetFloat("_GlossMapScale", 0.82f);
            body.SetFloat("_Metallic", 1f);

            Material flap = CreateStandardMaterial(
                RecorderFlapMaterialPath,
                "SoulPlayer Recorder Flap",
                standard,
                flapColor,
                normal,
                metallicSmoothness);
            ConfigureStandardTransparency(flap);

            Material arms = CreateStandardMaterial(
                ArmsMaterialPath,
                "SoulPlayer Recorder Arms",
                handsShader,
                armsColor,
                armsNormal,
                armsMetallicSmoothness);
            // The derived arm atlas is already neutralized into a tactical sleeve
            // palette. Keep the runtime multiplier neutral so daylight cannot turn
            // it back into the brown bare-arm appearance seen in EFT acceptance.
            arms.SetColor("_Color", new Color(0.92f, 0.94f, 0.94f, 1f));
            arms.SetFloat("_GlossMapScale", 0.28f);
            arms.SetFloat("_BumpScale", 0.85f);
            arms.SetFloat("_Metallic", 0f);

            Material hands = CreateStandardMaterial(
                HandsMaterialPath,
                "SoulPlayer Recorder Hands",
                handsShader,
                handsColor,
                handsNormal,
                handsMetallicSmoothness);
            hands.SetFloat("_GlossMapScale", 0.32f);
            hands.SetFloat("_BumpScale", 1f);
            hands.SetFloat("_Metallic", 0f);
            AssetDatabase.SaveAssets();
        }

        private static void ConfigureNormalMap(string path)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                throw new FileNotFoundException(
                    "Normal texture importer was unavailable: " + path);
            }
            if (importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }
        }

        private static void ConfigureLinearDataMap(string path)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                throw new FileNotFoundException(
                    "Metallic/smoothness texture importer was unavailable: " + path);
            }
            if (importer.sRGBTexture)
            {
                importer.sRGBTexture = false;
                importer.SaveAndReimport();
            }
        }

        private static Material CreateStandardMaterial(
            string path,
            string name,
            Shader shader,
            Texture2D color,
            Texture2D normal,
            Texture2D metallicSmoothness)
        {
            AssetDatabase.DeleteAsset(path);
            Material material = new Material(shader)
            {
                name = name,
                color = Color.white
            };
            material.SetTexture("_MainTex", color);
            if (normal != null)
            {
                material.SetTexture("_BumpMap", normal);
                material.EnableKeyword("_NORMALMAP");
            }
            if (metallicSmoothness != null)
            {
                material.SetTexture("_MetallicGlossMap", metallicSmoothness);
                material.EnableKeyword("_METALLICGLOSSMAP");
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void ConfigureStandardTransparency(Material material)
        {
            material.SetFloat("_Mode", 3f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt(
                "_SrcBlend",
                (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt(
                "_DstBlend",
                (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        private static Texture2D RequireTexture(string path)
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                throw new FileNotFoundException(
                    "Required SoulRecorder texture was not imported: " + path);
            }
            return texture;
        }

        private static void ValidateTextureDependencies(
            string prefabPath,
            IEnumerable<string> requiredPaths,
            string assetName)
        {
            HashSet<string> dependencies = new HashSet<string>(
                AssetDatabase.GetDependencies(prefabPath, true),
                StringComparer.Ordinal);
            foreach (string required in requiredPaths)
            {
                if (!dependencies.Contains(required))
                {
                    throw new InvalidOperationException(
                        assetName + " does not retain required texture dependency " +
                        required + ".");
                }
            }
            Debug.Log("[PASS] " + assetName + " texture dependencies");
        }

        private static void ValidateBuiltBundle(string finalBundlePath)
        {
            AssetBundle bundle = AssetBundle.LoadFromFile(finalBundlePath);
            if (bundle == null)
            {
                throw new InvalidOperationException(
                    "Unity could not self-load the generated SoulPlayer AssetBundle.");
            }

            try
            {
                Debug.Log("[PASS] AssetBundle editor self-load");
                GameObject recorderPrefab = bundle.LoadAsset<GameObject>("soulrecorder_fp");
                if (recorderPrefab == null)
                {
                    throw new InvalidOperationException(
                        "Generated bundle could not load soulrecorder_fp.");
                }
                GameObject recorder = UnityEngine.Object.Instantiate(recorderPrefab);
                try
                {
                    RequireLoadedTransform(recorder, "CassetteInsertionStart", "soulrecorder_fp");
                    RequireLoadedTransform(recorder, "CassetteAlignment", "soulrecorder_fp");
                    RequireLoadedTransform(recorder, "CassetteSlot", "soulrecorder_fp");
                    RequireLoadedTransform(recorder, "CassetteEject", "soulrecorder_fp");
                    RequireLoadedTransform(recorder, "CassetteWindow", "soulrecorder_fp");
                    Transform statusLed = RequireLoadedTransform(
                        recorder,
                        "StatusLed",
                        "soulrecorder_fp");
                    if (statusLed.GetComponent<Renderer>() == null)
                    {
                        throw new InvalidOperationException(
                            "soulrecorder_fp StatusLed does not have a Renderer.");
                    }
                    RequireLoadedTransform(recorder, "ReelWindowLeft", "soulrecorder_fp");
                    RequireLoadedTransform(recorder, "ReelWindowRight", "soulrecorder_fp");

                    Transform model = RequireLoadedTransform(
                        recorder,
                        "SoulRecorderModel",
                        "soulrecorder_fp");
                    Renderer[] modelRenderers = model.GetComponentsInChildren<Renderer>(true);
                    int activeRendererCount = CountUsableRenderers(modelRenderers);
                    if (modelRenderers.Length == 0 || activeRendererCount == 0)
                    {
                        throw new InvalidOperationException(
                            "soulrecorder_fp has no active visible model Renderer.");
                    }

                    Bounds modelBounds = CalculateLocalRendererBounds(
                        recorder.transform,
                        modelRenderers);
                    ValidateRecorderBounds(modelBounds.size);
                    ValidateRecorderRuntimeMaterials(modelRenderers);
                    Debug.Log("SoulRecorder editor visibility self-test:");
                    Debug.Log("Renderer count: " + modelRenderers.Length);
                    Debug.Log("Active renderer count: " + activeRendererCount);
                    foreach (Renderer modelRenderer in modelRenderers)
                    {
                        Material modelMaterial = modelRenderer.sharedMaterial;
                        string shaderName = modelMaterial == null || modelMaterial.shader == null
                            ? "<null>"
                            : modelMaterial.shader.name;
                        Debug.Log("Model renderer: name=" + modelRenderer.gameObject.name +
                            ", layer=" + modelRenderer.gameObject.layer +
                            ", enabled=" + modelRenderer.enabled +
                            ", active=" + modelRenderer.gameObject.activeInHierarchy +
                            ", shader=" + shaderName +
                            ", bounds=" + FormatVector(modelRenderer.bounds.size));
                    }
                    Debug.Log("SoulRecorderModel local rotation: " +
                        model.localRotation.eulerAngles);
                    Debug.Log("SoulRecorderModel local scale: " + model.localScale);
                    Debug.Log("Final oriented combined bounds: center=" +
                        FormatVector(modelBounds.center) + ", size=" +
                        FormatVector(modelBounds.size));
                    Debug.Log("Recorder front direction: local -Z");
                    Debug.Log("[PASS] soulrecorder_fp visibility bounds");
                    Debug.Log("[PASS] soulrecorder_fp runtime materials");
                    Debug.Log("[PASS] soulrecorder_fp prefab");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(recorder);
                }

                GameObject cassettePrefab = bundle.LoadAsset<GameObject>("soultape_cassette");
                if (cassettePrefab == null)
                {
                    throw new InvalidOperationException(
                        "Generated bundle could not load soultape_cassette.");
                }
                GameObject cassette = UnityEngine.Object.Instantiate(cassettePrefab);
                try
                {
                    RequireLoadedTransform(cassette, "Shell", "soultape_cassette");
                    RequireLoadedTransform(cassette, "Label", "soultape_cassette");
                    RequireLoadedTransform(cassette, "ReelLeft", "soultape_cassette");
                    RequireLoadedTransform(cassette, "ReelRight", "soultape_cassette");
                    Transform cassetteModel = RequireLoadedTransform(
                        cassette,
                        "SoulTapeCassette",
                        "soultape_cassette");
                    Renderer[] cassetteRenderers =
                        cassetteModel.GetComponentsInChildren<Renderer>(true);
                    int activeCassetteRenderers = CountUsableRenderers(cassetteRenderers);
                    if (cassetteRenderers.Length == 0 || activeCassetteRenderers == 0)
                    {
                        throw new InvalidOperationException(
                            "soultape_cassette has no active visible model Renderer.");
                    }
                    Bounds cassetteBounds = CalculateLocalRendererBounds(
                        cassette.transform,
                        cassetteRenderers);
                    ValidateCassetteBounds(cassetteBounds.size);
                    Debug.Log("SoulTape cassette editor visibility self-test:");
                    Debug.Log("Cassette renderer count: " + cassetteRenderers.Length);
                    Debug.Log("Active cassette renderer count: " + activeCassetteRenderers);
                    Debug.Log("SoulTapeCassette local scale: " + cassetteModel.localScale);
                    Debug.Log("Cassette combined bounds: center=" +
                        FormatVector(cassetteBounds.center) + ", size=" +
                        FormatVector(cassetteBounds.size));
                    Debug.Log("[PASS] soultape_cassette visibility bounds");
                    Debug.Log("[PASS] soultape_cassette prefab");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(cassette);
                }

                GameObject handsPrefab = bundle.LoadAsset<GameObject>(
                    "soulrecorder_animated_hands");
                if (handsPrefab == null)
                {
                    throw new InvalidOperationException(
                        "Generated bundle could not load soulrecorder_animated_hands.");
                }
                GameObject hands = UnityEngine.Object.Instantiate(handsPrefab);
                try
                {
                    RequireLoadedTransform(hands, "SoulRecorderHandsRig",
                        "soulrecorder_animated_hands");
                    RequireLoadedTransform(hands, "RecorderGrip",
                        "soulrecorder_animated_hands");
                    RequireLoadedTransform(hands, "CassetteGrip",
                        "soulrecorder_animated_hands");
                    RequireLoadedTransform(hands, "CassetteContact",
                        "soulrecorder_animated_hands");
                    RequireLoadedTransform(hands, "SupportSleeveCutoff",
                        "soulrecorder_animated_hands");
                    RequireLoadedTransform(hands, "CassetteSleeveCutoff",
                        "soulrecorder_animated_hands");
                    Transform handsModel = RequireLoadedTransform(
                        hands,
                        "SoulRecorderAnimatedHandsModel",
                        "soulrecorder_animated_hands");
                    Renderer[] handRenderers =
                        handsModel.GetComponentsInChildren<Renderer>(true);
                    SkinnedMeshRenderer[] skinned =
                        handsModel.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    if (skinned.Length != 1 || CountUsableRenderers(handRenderers) == 0)
                    {
                        throw new InvalidOperationException(
                            "soulrecorder_animated_hands did not retain one active skinned mesh.");
                    }
                    ValidateHandsRig(skinned[0], hands, "loaded bundle");
                    Bounds handBounds = CalculateLocalRendererBounds(
                        hands.transform,
                        handRenderers);
                    ValidateHandsBounds(handBounds.size);
                    ValidateHandsRuntimeMaterials(skinned[0].sharedMaterials);
                    Animator animator = hands.GetComponentInChildren<Animator>(true);
                    if (animator == null || animator.runtimeAnimatorController == null)
                    {
                        List<string> componentTypes = new List<string>();
                        foreach (Component component in
                            hands.GetComponentsInChildren<Component>(true))
                        {
                            componentTypes.Add(component == null
                                ? "<missing>"
                                : component.GetType().FullName + "@" + component.gameObject.name);
                        }
                        throw new InvalidOperationException(
                            "soulrecorder_animated_hands did not retain its Animator. Components: " +
                            string.Join(", ", componentTypes));
                    }
                    if (animator.gameObject != handsModel.gameObject)
                    {
                        throw new InvalidOperationException(
                            "soulrecorder_animated_hands Animator was not attached to the " +
                            "FBX clip-binding root SoulRecorderAnimatedHandsModel.");
                    }
                    HashSet<string> clipNames = new HashSet<string>(StringComparer.Ordinal);
                    foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
                    {
                        if (clip.length <= 0f)
                        {
                            throw new InvalidOperationException(
                                "Bundled BAMEN animation clip had no duration: " +
                                clip.name);
                        }
                        clipNames.Add(clip.name);
                    }
                    foreach (string required in RequiredAnimationClips)
                    {
                        if (!clipNames.Contains(required))
                        {
                            throw new InvalidOperationException(
                                "Bundled BAMEN Animator omitted " + required + ".");
                        }
                        if (!animator.HasState(
                                0,
                                Animator.StringToHash("Base Layer." + required)))
                        {
                            throw new InvalidOperationException(
                                "Bundled BAMEN Animator did not expose the bound state " +
                                required + " on Base Layer.");
                        }
                    }
                    Debug.Log("SoulRecorder hands editor visibility self-test:");
                    Debug.Log("Hands renderer count: " + handRenderers.Length);
                    Debug.Log("Hands combined bounds: center=" +
                        FormatVector(handBounds.center) + ", size=" +
                        FormatVector(handBounds.size));
                    Debug.Log("[PASS] soulrecorder_animated_hands texture dependencies");
                    Debug.Log("[PASS] soulrecorder_animated_hands runtime materials");
                    Debug.Log("[PASS] soulrecorder_animated_hands visibility bounds");
                    Debug.Log("[PASS] soulrecorder_animated_hands rig contract");
                    Debug.Log("[PASS] soulrecorder_animated_hands animation clips");
                    Debug.Log("[PASS] soulrecorder_animated_hands prefab");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(hands);
                }
            }
            finally
            {
                bundle.Unload(true);
            }
        }

        private static Transform RequireLoadedTransform(
            GameObject root,
            string name,
            string assetName)
        {
            Transform found = null;
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (!string.Equals(transform.name, name, StringComparison.Ordinal))
                {
                    continue;
                }
                if (found != null)
                {
                    throw new InvalidOperationException(
                        assetName + " contains duplicate " + name + " transforms.");
                }
                found = transform;
            }
            if (found == null)
            {
                throw new InvalidOperationException(
                    assetName + " is missing required transform " + name + ".");
            }
            return found;
        }

        private static void BuildRecorderPrefab()
        {
            GameObject model = RequireModel(RecorderModelPath);
            GameObject root = new GameObject("soulrecorder_fp");
            try
            {
                GameObject instance = UnityEngine.Object.Instantiate(model, root.transform);
                instance.name = "SoulRecorderModel";
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
                SetLayerRecursively(instance, 0);

                Renderer[] modelRenderers = instance.GetComponentsInChildren<Renderer>(true);
                if (modelRenderers.Length == 0 || CountUsableRenderers(modelRenderers) == 0)
                {
                    throw new InvalidOperationException(
                        "Imported recorder model has no active visible Renderer geometry.");
                }
                ApplyRecorderMaterials(modelRenderers);

                Bounds importedBounds = CalculateLocalRendererBounds(
                    root.transform,
                    modelRenderers);
                Debug.Log("SoulRecorder imported bounds before correction: center=" +
                    FormatVector(importedBounds.center) + ", size=" +
                    FormatVector(importedBounds.size));
                if (IsRecorderAlreadyOriented(importedBounds.size))
                {
                    Debug.Log("SoulRecorder imported model already satisfies the local axis contract.");
                }
                else if (NeedsRecorderAxisCorrection(importedBounds.size))
                {
                    instance.transform.localRotation = RecorderModelCorrection;
                    Debug.Log("SoulRecorder model axis correction: -90 degrees around local X.");
                }
                else
                {
                    throw new InvalidOperationException(
                        "Imported recorder bounds do not match the expected width/depth/height axes: " +
                        FormatVector(importedBounds.size) + ".");
                }

                float longestImportedDimension = Mathf.Max(
                    importedBounds.size.x,
                    Mathf.Max(importedBounds.size.y, importedBounds.size.z));
                float modelScale = ExpectedRecorderBounds.y / longestImportedDimension;
                if (modelScale < 0.5f || modelScale > 200f)
                {
                    throw new InvalidOperationException(
                        "Imported recorder unit scale was outside the supported normalization " +
                        "range: " + modelScale + ".");
                }
                instance.transform.localScale = Vector3.one * modelScale;
                Debug.Log("SoulRecorder model unit-scale correction: " +
                    modelScale.ToString("F6") + " uniformly.");

                Bounds orientedBounds = CalculateLocalRendererBounds(
                    root.transform,
                    modelRenderers);
                instance.transform.localPosition -= orientedBounds.center;
                Bounds finalBounds = CalculateLocalRendererBounds(
                    root.transform,
                    modelRenderers);
                ValidateRecorderBounds(finalBounds.size);
                Debug.Log("SoulRecorder final bounds after correction: center=" +
                    FormatVector(finalBounds.center) + ", size=" +
                    FormatVector(finalBounds.size));
                Debug.Log("SoulRecorderModel final local rotation: " +
                    instance.transform.localRotation.eulerAngles);
                Debug.Log("SoulRecorder front direction: local -Z.");

                Marker(root.transform, "CassetteInsertionStart",
                    new Vector3(0.105f, -0.080f, -0.125f),
                    Quaternion.Euler(-74f, 18f, 12f));
                Marker(root.transform, "CassetteAlignment",
                    new Vector3(0.012f, 0.012f, -0.062f),
                    Quaternion.Euler(-90f, 0f, 0f));
                Marker(root.transform, "CassetteSlot",
                    new Vector3(0f, 0.018f, -0.008f),
                    Quaternion.Euler(-90f, 0f, 0f));
                // Contact-rig anchors are part of the authored prefab contract.
                // They describe the physical grip and slot; hand poses are solved
                // against these transforms instead of guessed camera-space offsets.
                Marker(root.transform, "RecorderSupportPalm",
                    new Vector3(-0.032f, -0.060f, 0.025f));
                Marker(root.transform, "RecorderSupportThumb",
                    new Vector3(-0.046f, -0.028f, -0.024f));
                Marker(root.transform, "RecorderSupportIndex",
                    new Vector3(-0.032f, -0.103f, -0.022f));
                Marker(root.transform, "RecorderSupportMiddle",
                    new Vector3(-0.030f, -0.084f, -0.024f));
                Marker(root.transform, "RecorderSupportRing",
                    new Vector3(-0.030f, -0.070f, -0.024f));
                Marker(root.transform, "RecorderSupportPinky",
                    new Vector3(-0.058f, -0.047f, 0.007f));
                Marker(root.transform, "CassetteSlotEntry",
                    new Vector3(0.030f, 0.018f, -0.008f),
                    Quaternion.LookRotation(Vector3.left, Vector3.up));
                Marker(root.transform, "CassetteSlotSeated",
                    new Vector3(0f, 0.018f, -0.008f),
                    Quaternion.LookRotation(Vector3.left, Vector3.up));
                Marker(root.transform, "CassetteSlotTravelAxis",
                    new Vector3(0.015f, 0.018f, -0.008f),
                    Quaternion.LookRotation(Vector3.left, Vector3.up));
                Marker(root.transform, "CassetteEject",
                    new Vector3(0.095f, -0.065f, -0.115f),
                    Quaternion.Euler(-78f, 12f, 8f));
                Marker(root.transform, "CassetteWindow", new Vector3(0f, 0.018f, -0.022f));
                Marker(root.transform, "ReelWindowLeft", new Vector3(-0.026f, 0.018f, -0.022f));
                Marker(root.transform, "ReelWindowRight", new Vector3(0.026f, 0.018f, -0.022f));
                AddStatusLed(root.transform);
                RemoveColliders(root);
                PrefabUtility.SaveAsPrefabAsset(root, RecorderPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void BuildCassettePrefab()
        {
            GameObject model = RequireModel(CassetteModelPath);
            GameObject root = new GameObject("soultape_cassette");
            try
            {
                GameObject instance = UnityEngine.Object.Instantiate(model, root.transform);
                instance.name = "SoulTapeCassette";
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
                SetLayerRecursively(instance, 0);
                Renderer[] cassetteRenderers = instance.GetComponentsInChildren<Renderer>(true);
                if (cassetteRenderers.Length == 0 || CountUsableRenderers(cassetteRenderers) == 0)
                {
                    throw new InvalidOperationException(
                        "Imported SoulTape cassette has no active visible Renderer geometry.");
                }

                Bounds importedBounds = CalculateLocalRendererBounds(
                    root.transform,
                    cassetteRenderers);
                Debug.Log("SoulTape cassette imported bounds before correction: center=" +
                    FormatVector(importedBounds.center) + ", size=" +
                    FormatVector(importedBounds.size));
                if (!(importedBounds.size.x > importedBounds.size.y &&
                      importedBounds.size.y > importedBounds.size.z))
                {
                    throw new InvalidOperationException(
                        "Imported SoulTape cassette axes did not match X-width/Y-thickness/" +
                        "Z-height: " + FormatVector(importedBounds.size) + ".");
                }
                instance.transform.localRotation = CassetteModelCorrection;
                Debug.Log("SoulTape cassette axis correction: -90 degrees around local X.");

                float cassetteScale = ExpectedCassetteBounds.x / importedBounds.size.x;
                if (cassetteScale < 0.5f || cassetteScale > 200f)
                {
                    throw new InvalidOperationException(
                        "Imported SoulTape cassette unit scale was outside the supported " +
                        "normalization range: " + cassetteScale + ".");
                }
                instance.transform.localScale = Vector3.one * cassetteScale;
                Bounds scaledBounds = CalculateLocalRendererBounds(
                    root.transform,
                    cassetteRenderers);
                instance.transform.localPosition -= scaledBounds.center;
                Bounds finalBounds = CalculateLocalRendererBounds(
                    root.transform,
                    cassetteRenderers);
                ValidateCassetteBounds(finalBounds.size);
                Debug.Log("SoulTape cassette unit-scale correction: " +
                    cassetteScale.ToString("F6") + " uniformly.");
                Debug.Log("SoulTape cassette final bounds: center=" +
                    FormatVector(finalBounds.center) + ", size=" +
                    FormatVector(finalBounds.size));
                RequireUniqueTransform(root, "Shell");
                RequireUniqueTransform(root, "Label");
                RequireUniqueTransform(root, "ReelLeft");
                RequireUniqueTransform(root, "ReelRight");
                Marker(root.transform, "CassetteThumbGrip",
                    new Vector3(-0.024f, 0.018f, 0.006f));
                Marker(root.transform, "CassetteIndexGrip",
                    new Vector3(-0.012f, -0.014f, 0.013f));
                Marker(root.transform, "CassetteMiddleGrip",
                    new Vector3(0.018f, -0.014f, 0.012f));
                Marker(root.transform, "CassetteFront",
                    new Vector3(-0.055f, 0f, 0f));
                Marker(root.transform, "CassetteTop",
                    new Vector3(0f, 0f, 0.035f));
                Marker(root.transform, "CassetteInsertionAxis",
                    Vector3.zero,
                    Quaternion.LookRotation(Vector3.left, Vector3.up));
                RemoveColliders(root);
                PrefabUtility.SaveAsPrefabAsset(root, CassettePrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void BuildHandsPrefab()
        {
            GameObject model = RequireModel(HandsModelPath);
            GameObject root = new GameObject("soulrecorder_animated_hands");
            try
            {
                GameObject instance = UnityEngine.Object.Instantiate(model, root.transform);
                instance.name = "SoulRecorderAnimatedHandsModel";
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
                SetLayerRecursively(instance, 0);

                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                SkinnedMeshRenderer[] skinned =
                    instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (renderers.Length == 0 || skinned.Length != 1 ||
                    CountUsableRenderers(renderers) == 0)
                {
                    throw new InvalidOperationException(
                        "Imported hands must contain exactly one active SkinnedMeshRenderer.");
                }
                skinned[0].updateWhenOffscreen = true;
                RequireUniqueTransform(root, "SoulRecorderHandsRig");
                RequireUniqueTransform(root, "SoulRecorderHandsRoot");
                RequireUniqueTransform(root, "Arm_1.L");
                RequireUniqueTransform(root, "Arm_2.L");
                RequireUniqueTransform(root, "Hand_1.L");
                RequireUniqueTransform(root, "Hand_2.L");
                RequireUniqueTransform(root, "Arm_1.R");
                RequireUniqueTransform(root, "Arm_2.R");
                RequireUniqueTransform(root, "Hand_1.R");
                RequireUniqueTransform(root, "Hand_2.R");
                RequireUniqueTransform(root, "Finger_1_1.L");
                RequireUniqueTransform(root, "Finger_2_3.L");
                RequireUniqueTransform(root, "Finger_1_1.R");
                RequireUniqueTransform(root, "Finger_2_3.R");
                RequireUniqueTransform(root, "RecorderGrip");
                RequireUniqueTransform(root, "CassetteGrip");
                RequireUniqueTransform(root, "CassetteContact");
                RequireUniqueTransform(root, "SupportSleeveCutoff");
                RequireUniqueTransform(root, "CassetteSleeveCutoff");
                ValidateHandsRig(skinned[0], root, "imported prefab");

                Bounds importedBounds = CalculateLocalRendererBounds(
                    root.transform,
                    renderers);
                Debug.Log("SoulRecorder hands imported bounds before correction: center=" +
                    FormatVector(importedBounds.center) + ", size=" +
                    FormatVector(importedBounds.size));
                Material armsMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                    ArmsMaterialPath);
                Material handMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                    HandsMaterialPath);
                if (armsMaterial == null || handMaterial == null)
                {
                    throw new InvalidOperationException(
                        "Explicit BAMEN arm/hand materials were unavailable.");
                }
                foreach (Renderer renderer in renderers)
                {
                    Material[] assigned = new Material[renderer.sharedMaterials.Length];
                    for (int index = 0; index < assigned.Length; index++)
                    {
                        string sourceName = renderer.sharedMaterials[index] == null
                            ? string.Empty
                            : renderer.sharedMaterials[index].name;
                        if (sourceName.IndexOf(
                                "FPS Hand", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            assigned[index] = handMaterial;
                        }
                        else if (sourceName.IndexOf(
                                     "FPS Arm", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            assigned[index] = armsMaterial;
                        }
                        else
                        {
                            throw new InvalidOperationException(
                                "Unknown BAMEN material slot '" + sourceName +
                                "'; refusing to guess arm/hand texture identity.");
                        }
                    }
                    renderer.sharedMaterials = assigned;
                }
                ValidateHandsRuntimeMaterials(skinned[0].sharedMaterials);

                AnimatorController controller = BuildHandsAnimatorController();
                // FBX clip bindings begin at SoulRecorderHandsRig. Keep the Animator
                // on the imported-model root so those paths resolve directly instead
                // of being hidden below the SoulRecorderAnimatedHandsModel wrapper.
                Animator animator = instance.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.updateMode = AnimatorUpdateMode.UnscaledTime;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                Bounds finalBounds = CalculateLocalRendererBounds(
                    root.transform,
                    renderers);
                ValidateHandsBounds(finalBounds.size);
                Debug.Log("SoulRecorder BAMEN hands import normalization: baked axis conversion, unit prefab scale.");
                Debug.Log("SoulRecorder hands final bounds: center=" +
                    FormatVector(finalBounds.center) + ", size=" +
                    FormatVector(finalBounds.size));
                RemoveColliders(root);
                PrefabUtility.SaveAsPrefabAsset(root, HandsPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static AnimatorController BuildHandsAnimatorController()
        {
            Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>(
                StringComparer.Ordinal);
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(HandsModelPath))
            {
                AnimationClip clip = asset as AnimationClip;
                if (clip == null || clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                {
                    continue;
                }
                clips[clip.name] = clip;
            }
            foreach (string required in RequiredAnimationClips)
            {
                AnimationClip clip;
                if (!clips.TryGetValue(required, out clip) || clip.length <= 0f ||
                    AnimationUtility.GetCurveBindings(clip).Length == 0)
                {
                    throw new InvalidOperationException(
                        "BAMEN hands clip was missing, empty, or had no real curves: " + required);
                }
            }

            AssetDatabase.DeleteAsset(HandsAnimatorPath);
            AnimatorController controller =
                AnimatorController.CreateAnimatorControllerAtPath(HandsAnimatorPath);
            AnimatorStateMachine states = controller.layers[0].stateMachine;
            foreach (string name in RequiredAnimationClips)
            {
                AnimatorState state = states.AddState(name);
                state.motion = clips[name];
                state.speed = 1f;
                if (name == "SoulRecorder_Enter")
                {
                    states.defaultState = state;
                }
            }
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static GameObject RequireModel(string path)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null)
            {
                throw new FileNotFoundException("Processed model was not imported: " + path);
            }
            return model;
        }

        private static void ApplyRecorderMaterials(Renderer[] renderers)
        {
            Material body = AssetDatabase.LoadAssetAtPath<Material>(
                RecorderBodyMaterialPath);
            Material flap = AssetDatabase.LoadAssetAtPath<Material>(
                RecorderFlapMaterialPath);
            if (body == null || flap == null)
            {
                throw new InvalidOperationException(
                    "Explicit SoulRecorder body/flap materials were unavailable.");
            }

            bool usedBody = false;
            bool usedFlap = false;
            foreach (Renderer renderer in renderers)
            {
                Material[] imported = renderer.sharedMaterials;
                if (imported == null || imported.Length == 0)
                {
                    throw new InvalidOperationException(
                        "Recorder renderer did not expose imported material slots.");
                }

                Material[] assigned = new Material[imported.Length];
                for (int index = 0; index < imported.Length; index++)
                {
                    string importedName = imported[index] == null
                        ? string.Empty
                        : imported[index].name;
                    bool isFlap = importedName.IndexOf(
                        "flap",
                        StringComparison.OrdinalIgnoreCase) >= 0;
                    assigned[index] = isFlap ? flap : body;
                    usedFlap |= isFlap;
                    usedBody |= !isFlap;
                }
                renderer.sharedMaterials = assigned;
            }

            if (!usedBody || !usedFlap)
            {
                throw new InvalidOperationException(
                    "Recorder imported material slots did not include distinct body and flap surfaces.");
            }
        }

        private static Transform Marker(
            Transform parent,
            string name,
            Vector3 position,
            Quaternion? rotation = null)
        {
            GameObject marker = new GameObject(name);
            marker.transform.SetParent(parent, false);
            marker.transform.localPosition = position;
            marker.transform.localRotation = rotation ?? Quaternion.identity;
            return marker.transform;
        }

        private static void AddStatusLed(Transform parent)
        {
            GameObject led = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            led.name = "StatusLed";
            led.transform.SetParent(parent, false);
            // Keep the indicator on the recorder face. The old X=0.080 position
            // exceeded the normalized recorder half-width and rendered as a loose
            // black/brown button floating beside the body.
            led.transform.localPosition = new Vector3(0.047f, 0.052f, -0.036f);
            led.transform.localScale = new Vector3(0.010f, 0.010f, 0.005f);
            Renderer renderer = led.GetComponent<Renderer>();
            Shader shader = Shader.Find("Standard");
            if (renderer == null || shader == null)
            {
                throw new InvalidOperationException("Standard status LED material could not be created.");
            }
            Material material = new Material(shader)
            {
                name = "SoulPlayer Status LED",
                color = new Color(0.13f, 0.075f, 0.025f, 1f)
            };
            string materialPath = GeneratedFolder + "/SoulPlayerStatusLed.mat";
            AssetDatabase.DeleteAsset(materialPath);
            AssetDatabase.CreateAsset(material, materialPath);
            renderer.sharedMaterial = material;
        }

        private static void RequireUniqueTransform(GameObject root, string name)
        {
            int matches = 0;
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(transform.name, name, StringComparison.Ordinal))
                {
                    matches++;
                }
            }
            if (matches != 1)
            {
                throw new InvalidOperationException(
                    "Processed cassette must contain exactly one " + name + " transform.");
            }
        }

        private static void ValidateHandsRig(
            SkinnedMeshRenderer renderer,
            GameObject root,
            string stage)
        {
            Transform commonRoot = RequireLoadedTransform(
                root,
                "SoulRecorderHandsRoot",
                "soulrecorder_animated_hands");
            Transform recorderGrip = RequireLoadedTransform(
                root, "RecorderGrip", "soulrecorder_animated_hands");
            Transform cassetteGrip = RequireLoadedTransform(
                root, "CassetteGrip", "soulrecorder_animated_hands");
            Transform cassetteContact = RequireLoadedTransform(
                root, "CassetteContact", "soulrecorder_animated_hands");
            Transform supportCutoff = RequireLoadedTransform(
                root, "SupportSleeveCutoff", "soulrecorder_animated_hands");
            Transform cassetteCutoff = RequireLoadedTransform(
                root, "CassetteSleeveCutoff", "soulrecorder_animated_hands");
            int bindPoseCount = renderer.sharedMesh == null
                ? 0
                : renderer.sharedMesh.bindposes.Length;
            if (renderer.rootBone != commonRoot || renderer.bones.Length < 40 ||
                bindPoseCount != renderer.bones.Length ||
                recorderGrip.parent == null || recorderGrip.parent.name != "Hand_2.L" ||
                cassetteGrip.parent == null || cassetteGrip.parent.name != "Hand_1.R" ||
                cassetteContact.parent == null ||
                cassetteContact.parent.name != "Finger_2_2.R" ||
                supportCutoff.parent != commonRoot || cassetteCutoff.parent != commonRoot)
            {
                throw new InvalidOperationException(
                    "soulrecorder_animated_hands " + stage +
                    " rig must preserve the full BAMEN articulated skeleton and socket " +
                    "parents; root=" +
                    (renderer.rootBone == null ? "<null>" : renderer.rootBone.name) +
                    ", bones=" + renderer.bones.Length +
                    ", bindposes=" + bindPoseCount + ".");
            }
        }

        private static void RemoveColliders(GameObject root)
        {
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }
        }

        private static bool IsRecorderAlreadyOriented(Vector3 size)
        {
            return size.y > size.x && size.y > size.z && size.z < size.x;
        }

        private static bool NeedsRecorderAxisCorrection(Vector3 size)
        {
            return size.z > size.x && size.z > size.y && size.y < size.x;
        }

        private static void ValidateRecorderBounds(Vector3 size)
        {
            if (!(size.y > size.x && size.y > size.z && size.z < size.x) ||
                Mathf.Abs(size.x - ExpectedRecorderBounds.x) > 0.035f ||
                Mathf.Abs(size.y - ExpectedRecorderBounds.y) > 0.050f ||
                Mathf.Abs(size.z - ExpectedRecorderBounds.z) > 0.025f)
            {
                throw new InvalidOperationException(
                    "Recorder model does not satisfy the X-width/Y-height/Z-depth bounds " +
                    "contract. Expected approximately " +
                    FormatVector(ExpectedRecorderBounds) + ", actual " +
                    FormatVector(size) + ".");
            }
        }

        private static bool IsCassetteOriented(Vector3 size)
        {
            return size.x > size.z && size.z > size.y;
        }

        private static void ValidateCassetteBounds(Vector3 size)
        {
            if (!IsCassetteOriented(size) ||
                Mathf.Abs(size.x - ExpectedCassetteBounds.x) > 0.010f ||
                Mathf.Abs(size.y - ExpectedCassetteBounds.y) > 0.005f ||
                Mathf.Abs(size.z - ExpectedCassetteBounds.z) > 0.010f)
            {
                throw new InvalidOperationException(
                    "SoulTape cassette does not satisfy the X-width/Y-thickness/Z-height " +
                    "bounds contract. Expected approximately " +
                    FormatVector(ExpectedCassetteBounds) + ", actual " +
                    FormatVector(size) + ".");
            }
        }

        private static void ValidateHandsBounds(Vector3 size)
        {
            if (!(size.x > 0.05f && size.y > 0.05f && size.z > 0.05f) ||
                size.x > 1.30f || size.y > 1.30f || size.z > 1.30f)
            {
                throw new InvalidOperationException(
                    "SoulRecorder BAMEN hands have invalid first-person bounds: " +
                    FormatVector(size) + ".");
            }
        }

        private static void ValidateRecorderRuntimeMaterials(Renderer[] renderers)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            foreach (Renderer renderer in renderers)
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null || material.shader == null ||
                        !string.Equals(material.shader.name, "Standard", StringComparison.Ordinal) ||
                        material.GetTexture("_MainTex") == null ||
                        material.GetTexture("_BumpMap") == null ||
                        material.GetTexture("_MetallicGlossMap") == null)
                    {
                        throw new InvalidOperationException(
                            "Recorder runtime material was missing its Standard shader or " +
                            "required base-color/normal/metallic-smoothness textures.");
                    }
                    names.Add(material.name.Replace(" (Instance)", string.Empty));
                }
            }
            if (!names.Contains("SoulPlayerRecorderBody") ||
                !names.Contains("SoulPlayerRecorderFlap"))
            {
                throw new InvalidOperationException(
                    "Recorder runtime prefab did not retain distinct explicit body/flap materials.");
            }
        }

        private static void ValidateHandsRuntimeMaterials(Material[] materials)
        {
            if (materials == null || materials.Length != 2)
            {
                throw new InvalidOperationException(
                    "SoulRecorder animated hands must retain exactly two material slots.");
            }
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            foreach (Material material in materials)
            {
                if (material == null || material.shader == null ||
                    !string.Equals(material.shader.name, HandsShaderName,
                        StringComparison.Ordinal) ||
                    material.GetTexture("_MainTex") == null ||
                    material.GetTexture("_BumpMap") == null ||
                    material.GetTexture("_MetallicGlossMap") == null)
                {
                    throw new InvalidOperationException(
                        "SoulRecorder animated hands material was missing the " +
                        "SoulPlayer PBR shader or required albedo/normal/" +
                        "metallic-smoothness maps.");
                }
                names.Add(material.name.Replace(" (Instance)", string.Empty));
            }
            if (!names.Contains("SoulPlayerRecorderArms") ||
                !names.Contains("SoulPlayerRecorderHands"))
            {
                throw new InvalidOperationException(
                    "SoulRecorder animated hands did not retain distinct arm/hand materials.");
            }
        }

        private static int CountUsableRenderers(Renderer[] renderers)
        {
            int count = 0;
            foreach (Renderer renderer in renderers)
            {
                if (renderer != null && renderer.enabled &&
                    renderer.gameObject.activeInHierarchy &&
                    renderer.sharedMaterial != null &&
                    renderer.sharedMaterial.shader != null &&
                    renderer.bounds.size.sqrMagnitude > 0.00000001f)
                {
                    count++;
                }
            }
            return count;
        }

        private static Bounds CalculateLocalRendererBounds(
            Transform relativeTo,
            Renderer[] renderers)
        {
            bool initialized = false;
            Bounds combined = new Bounds();
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || renderer.bounds.size.sqrMagnitude <= 0.00000001f)
                {
                    continue;
                }

                Bounds world = renderer.bounds;
                Vector3 min = world.min;
                Vector3 max = world.max;
                for (int x = 0; x < 2; x++)
                {
                    for (int y = 0; y < 2; y++)
                    {
                        for (int z = 0; z < 2; z++)
                        {
                            Vector3 corner = new Vector3(
                                x == 0 ? min.x : max.x,
                                y == 0 ? min.y : max.y,
                                z == 0 ? min.z : max.z);
                            Vector3 local = relativeTo.InverseTransformPoint(corner);
                            if (!initialized)
                            {
                                combined = new Bounds(local, Vector3.zero);
                                initialized = true;
                            }
                            else
                            {
                                combined.Encapsulate(local);
                            }
                        }
                    }
                }
            }

            if (!initialized)
            {
                throw new InvalidOperationException(
                    "Model renderer bounds were empty.");
            }
            return combined;
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                transform.gameObject.layer = layer;
            }
        }

        private static string FormatVector(Vector3 value)
        {
            return string.Format(
                "({0:F6}, {1:F6}, {2:F6})",
                value.x,
                value.y,
                value.z);
        }
    }
}
