using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using SoulPlayer.Recorder;
using SoulPlayer.Recorder.Assets;
using Xunit;
using UnityEngine;

namespace SoulPlayer.CollectionTests
{
    public sealed class SoulRecorderAssetPipelineTests
    {
        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void EmbeddedSourceManifestParsesAndDeclaresRequiredAssets()
        {
            SoulPlayerAssetSourceManifest manifest =
                SoulPlayerAssetSourceManifest.LoadEmbedded(
                    typeof(SoulPlayerAssetSourceManifest).Assembly);

            Assert.Equal(1, manifest.SchemaVersion);
            Assert.Equal(SoulPlayerAssetContract.RequiredUnityVersion, manifest.RequiredUnityVersion);
            Assert.Equal(SoulPlayerAssetContract.BundleFileName, manifest.BundleFileName);
            Assert.Equal("StandaloneWindows64", manifest.BuildTarget);
            Assert.Contains(manifest.Assets, asset =>
                asset.LogicalName == SoulPlayerAssetContract.RecorderAssetName);
            Assert.Contains(manifest.Assets, asset =>
                asset.LogicalName == SoulPlayerAssetContract.CassetteAssetName);
            Assert.Contains(manifest.Assets, asset =>
                asset.LogicalName == SoulPlayerAssetContract.AnimatedHandsAssetName);
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void ApprovedSourceHashesAreStableSha256Values()
        {
            SoulPlayerAssetSourceManifest manifest =
                SoulPlayerAssetSourceManifest.LoadEmbedded(
                    typeof(SoulPlayerAssetSourceManifest).Assembly);
            Dictionary<string, string> expected = new Dictionary<string, string>
            {
                [SoulPlayerAssetContract.RecorderAssetName] =
                    "C6B84808C8AFF29E1798022BCF0C50AFD02E5838E394BC19BC381CF6B97467A2",
                [SoulPlayerAssetContract.CassetteAssetName] =
                    "4E1CA41FFE39921B7B10B4CB0CD8BCC43FECB79D97D2DEACC5A6439216BA6246",
                [SoulPlayerAssetContract.AnimatedHandsAssetName] =
                    "8585C328BF9E0872DBE28E08D7B72BF675411CCF8C49F26F804CF97096592BBE"
            };

            foreach (SoulPlayerAssetSourceEntry asset in manifest.Assets)
            {
                Assert.Equal(64, asset.SourceArchiveSha256.Length);
                Assert.True(asset.SourceArchiveSha256.All(Uri.IsHexDigit));
                Assert.Equal(expected[asset.LogicalName], asset.SourceArchiveSha256);
                Assert.True(asset.License == "CC0" || asset.License == "CC BY 4.0");
                Assert.False(Path.IsPathRooted(asset.LicenseRecordPath));
                Assert.True(asset.Redistributed);
                Assert.False(Path.IsPathRooted(asset.ExternalSourcePath));
            }
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void BundleNameAndInstalledPathAreDeterministicAndResourceIndependent()
        {
            string assembly = Path.Combine(
                "C:\\SPT",
                "BepInEx",
                "plugins",
                "SoulPlayer",
                "Soulplayer.dll");

            string bundle = SoulPlayerAssetContract.GetBundlePath(assembly);

            Assert.Equal(
                Path.Combine(Path.GetDirectoryName(assembly), "soulplayer_assets.bundle"),
                bundle);
            Assert.DoesNotContain("Resources", bundle);
            Assert.DoesNotContain("reference-projects", bundle);
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void PrefabTransformContractsAreCentralizedAndComplete()
        {
            Assert.Equal(18, SoulPlayerAssetContract.RecorderRequiredTransforms.Length);
            Assert.Contains("SoulRecorderModel", SoulPlayerAssetContract.RecorderRequiredTransforms);
            Assert.Contains("CassetteSlot", SoulPlayerAssetContract.RecorderRequiredTransforms);
            Assert.Contains("CassetteInsertionStart", SoulPlayerAssetContract.RecorderRequiredTransforms);
            Assert.Contains("CassetteAlignment", SoulPlayerAssetContract.RecorderRequiredTransforms);
            Assert.Contains("CassetteEject", SoulPlayerAssetContract.RecorderRequiredTransforms);
            Assert.Contains("StatusLed", SoulPlayerAssetContract.RecorderRequiredTransforms);
            Assert.Contains("RecorderSupportPalm", SoulPlayerAssetContract.RecorderRequiredTransforms);
            Assert.Contains("RecorderSupportThumb", SoulPlayerAssetContract.RecorderRequiredTransforms);
            Assert.Contains("RecorderSupportIndex", SoulPlayerAssetContract.RecorderRequiredTransforms);
            Assert.Contains("RecorderSupportMiddle", SoulPlayerAssetContract.RecorderRequiredTransforms);
            Assert.Contains("RecorderSupportRing", SoulPlayerAssetContract.RecorderRequiredTransforms);
            Assert.Contains("RecorderSupportPinky", SoulPlayerAssetContract.RecorderRequiredTransforms);
            Assert.Contains("CassetteSlotEntry", SoulPlayerAssetContract.RecorderRequiredTransforms);
            Assert.Contains("CassetteSlotSeated", SoulPlayerAssetContract.RecorderRequiredTransforms);
            Assert.Contains("CassetteSlotTravelAxis", SoulPlayerAssetContract.RecorderRequiredTransforms);
            Assert.Equal(11, SoulPlayerAssetContract.CassetteRequiredTransforms.Length);
            Assert.Contains("SoulTapeCassette", SoulPlayerAssetContract.CassetteRequiredTransforms);
            Assert.Contains("ReelLeft", SoulPlayerAssetContract.CassetteRequiredTransforms);
            Assert.Contains("ReelRight", SoulPlayerAssetContract.CassetteRequiredTransforms);
            Assert.Contains("CassetteThumbGrip", SoulPlayerAssetContract.CassetteRequiredTransforms);
            Assert.Contains("CassetteIndexGrip", SoulPlayerAssetContract.CassetteRequiredTransforms);
            Assert.Contains("CassetteMiddleGrip", SoulPlayerAssetContract.CassetteRequiredTransforms);
            Assert.Contains("CassetteFront", SoulPlayerAssetContract.CassetteRequiredTransforms);
            Assert.Contains("CassetteTop", SoulPlayerAssetContract.CassetteRequiredTransforms);
            Assert.Contains("CassetteInsertionAxis", SoulPlayerAssetContract.CassetteRequiredTransforms);
            Assert.True(SoulPlayerAssetContract.HandsRequiredTransforms.Length >= 15);
            Assert.Contains("SoulRecorderHandsRig", SoulPlayerAssetContract.HandsRequiredTransforms);
            Assert.Contains("SoulRecorderHandsRoot", SoulPlayerAssetContract.HandsRequiredTransforms);
            Assert.Contains("Hand_2.L", SoulPlayerAssetContract.HandsRequiredTransforms);
            Assert.Contains("Hand_2.R", SoulPlayerAssetContract.HandsRequiredTransforms);
            Assert.Contains("Finger_1_1.L", SoulPlayerAssetContract.HandsRequiredTransforms);
            Assert.Contains("Finger_2_3.R", SoulPlayerAssetContract.HandsRequiredTransforms);
            Assert.Contains("RecorderGrip", SoulPlayerAssetContract.HandsRequiredTransforms);
            Assert.Contains("CassetteGrip", SoulPlayerAssetContract.HandsRequiredTransforms);
            Assert.Contains("CassetteContact", SoulPlayerAssetContract.HandsRequiredTransforms);
            Assert.Contains("SupportSleeveCutoff", SoulPlayerAssetContract.HandsRequiredTransforms);
            Assert.Contains("CassetteSleeveCutoff", SoulPlayerAssetContract.HandsRequiredTransforms);
            Assert.Equal(8, SoulPlayerAssetContract.RequiredHandsAnimationClips.Length);
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void ReleasePackagingUsesCurrentSptLayoutAndPluginVersion()
        {
            string repositoryRoot = FindRepositoryRoot();
            string packageScript = File.ReadAllText(
                Path.Combine(repositoryRoot, "scripts", "Package-Release.ps1"));
            string readme = File.ReadAllText(
                Path.Combine(repositoryRoot, "packaging", "README.txt"));
            string plugin = File.ReadAllText(Path.Combine(repositoryRoot, "Plugin.cs"));
            string assemblyInfo = File.ReadAllText(
                Path.Combine(repositoryRoot, "Properties", "AssemblyInfo.cs"));

            Match versionMatch = Regex.Match(
                plugin,
                "BepInPlugin\\([^,]+,\\s*\\\"SoulPlayer\\\",\\s*\\\"(?<version>\\d+\\.\\d+\\.\\d+)\\\"\\)");
            Assert.True(versionMatch.Success);
            string pluginVersion = versionMatch.Groups["version"].Value;

            Assert.Equal("0.9.1", pluginVersion);
            Assert.Contains("SPT_Runtime\\SPT.Server.exe", packageScript);
            Assert.DoesNotContain("Join-Path $resolvedSptRoot 'SPT.Server.exe'", packageScript);
            Assert.Contains("$pluginVersion = $pluginVersionMatch.Groups['version'].Value", packageScript);
            Assert.Contains("SoulPlayer " + pluginVersion + " for SPT 4.1.x", readme);
            Assert.Contains("https://github.com/kelorein/SoulPlayer", readme);
            Assert.DoesNotContain("kelorein7", readme);
            Assert.Contains("AssemblyVersion(\"" + pluginVersion + ".0\")", assemblyInfo);
            Assert.DoesNotContain("0.6.4", packageScript + readme);
            Assert.DoesNotContain("0.6.5", packageScript + readme);
            Assert.DoesNotContain("SPT 3.9.8", packageScript + readme);
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void UnityBuildWrapperWaitsChecksExitCodeAndRejectsStaleOutput()
        {
            string repositoryRoot = FindRepositoryRoot();
            string buildScript = File.ReadAllText(
                Path.Combine(repositoryRoot, "tools", "Build-SoulRecorderAssets.ps1"));

            int staleRemoval = buildScript.IndexOf(
                "Remove-Item -LiteralPath $stalePath",
                StringComparison.Ordinal);
            int unityLaunch = buildScript.IndexOf(
                "Start-Process -FilePath $UnityEditorPath",
                StringComparison.Ordinal);

            Assert.True(staleRemoval >= 0);
            Assert.True(unityLaunch > staleRemoval);
            Assert.Contains("$bundlePath,", buildScript);
            Assert.Contains("$bundleManifestPath,", buildScript);
            Assert.Contains("$worldCassetteBundlePath,", buildScript);
            Assert.Contains("$worldCassetteManifestPath,", buildScript);
            Assert.Contains("$unityLogPath", buildScript);
            Assert.Contains("-PassThru", buildScript);
            Assert.Contains("$unityProcess.WaitForExit()", buildScript);
            Assert.Contains("$unityProcess.ExitCode -ne 0", buildScript);
            Assert.Contains("unity-build.log", buildScript);
            Assert.Contains("'-force-d3d11'", buildScript);
            Assert.DoesNotContain("'-nographics'", buildScript);
            Assert.Contains("module\\s+AssetBundle\\s+is\\s+disabled", buildScript);
            Assert.Contains("[PASS] AssetBundle editor self-load", buildScript);
            Assert.Contains("[PASS] soulrecorder_fp visibility bounds", buildScript);
            Assert.Contains("[PASS] soulrecorder_fp texture dependencies", buildScript);
            Assert.Contains("[PASS] soulrecorder_fp runtime materials", buildScript);
            Assert.Contains("[PASS] soulrecorder_fp prefab", buildScript);
            Assert.Contains("[PASS] soultape_cassette visibility bounds", buildScript);
            Assert.Contains("[PASS] soultape_cassette prefab", buildScript);
            Assert.Contains("[PASS] soultape_world.bundle exact visual-only prefab", buildScript);
            Assert.Contains("[PASS] soultape_world.bundle contains no colliders", buildScript);
            Assert.Contains("[PASS] soulrecorder_animated_hands texture dependencies", buildScript);
            Assert.Contains("[PASS] soulrecorder_animated_hands runtime materials", buildScript);
            Assert.Contains("[PASS] soulrecorder_animated_hands rig contract", buildScript);
            Assert.Contains("[PASS] soulrecorder_animated_hands animation clips", buildScript);
            Assert.Contains("[PASS] soulrecorder_animated_hands prefab", buildScript);
            Assert.Contains("Invalid output was removed", buildScript);
            Assert.Contains("$size -le 0", buildScript);
            Assert.Contains("Get-FileHash -LiteralPath $bundlePath -Algorithm SHA256", buildScript);
            Assert.Contains("Get-FileHash -LiteralPath $worldCassetteBundlePath -Algorithm SHA256", buildScript);
            Assert.DoesNotContain("& $UnityEditorPath", buildScript);
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void UnityBuilderUsesStrictWindowsPlayerTargetAndSelfValidatesBundle()
        {
            string repositoryRoot = FindRepositoryRoot();
            string builder = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "Assets",
                "SoulRecorder",
                "UnityProject",
                "Assets",
                "Editor",
                "SoulRecorderAssetBundleBuilder.cs"));
            string packageManifest = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "Assets",
                "SoulRecorder",
                "UnityProject",
                "Packages",
                "manifest.json"));

            Assert.Contains("BuildTargetGroup.Standalone", builder);
            Assert.Contains("BuildTarget.StandaloneWindows64", builder);
            Assert.Contains("StandaloneBuildSubtarget.Player", builder);
            Assert.Contains("SwitchActiveBuildTarget", builder);
            Assert.Contains("PlayerSettings.stripEngineCode = false", builder);
            Assert.Contains("PlayerSettings.stripEngineCode", builder);
            Assert.Contains("BuildAssetBundleOptions.ChunkBasedCompression", builder);
            Assert.Contains("BuildAssetBundleOptions.ForceRebuildAssetBundle", builder);
            Assert.Contains("BuildAssetBundleOptions.StrictMode", builder);
            Assert.Contains("BuildAssetBundleOptions.DeterministicAssetBundle", builder);
            Assert.DoesNotContain("AssetBundleStripUnityVersion", builder);
            Assert.Contains("AssetBundle.LoadFromFile(finalBundlePath)", builder);
            Assert.Contains("LoadAsset<GameObject>(\"soulrecorder_fp\")", builder);
            Assert.Contains("LoadAsset<GameObject>(\"soultape_cassette\")", builder);
            Assert.Contains("LoadAsset<GameObject>(\n                    \"soulrecorder_animated_hands\")", builder);
            Assert.Contains("[PASS] AssetBundle editor self-load", builder);
            Assert.Contains("[PASS] soulrecorder_fp visibility bounds", builder);
            Assert.Contains("[PASS] soulrecorder_fp prefab", builder);
            Assert.Contains("[PASS] soultape_cassette visibility bounds", builder);
            Assert.Contains("[PASS] soultape_cassette prefab", builder);
            Assert.Contains("[PASS] soulrecorder_animated_hands rig contract", builder);
            Assert.Contains("[PASS] soulrecorder_animated_hands animation clips", builder);
            Assert.Contains("[PASS] soulrecorder_animated_hands prefab", builder);
            Assert.Contains("statusLed.GetComponent<Renderer>()", builder);
            Assert.Contains("CassetteInsertionStart", builder);
            Assert.Contains("CassetteAlignment", builder);
            Assert.Contains("CassetteEject", builder);
            Assert.Contains("CassetteSlot", builder);
            Assert.Contains("CassetteWindow", builder);
            Assert.Contains("ReelWindowLeft", builder);
            Assert.Contains("ReelWindowRight", builder);
            Assert.Contains("Shell", builder);
            Assert.Contains("Label", builder);
            Assert.Contains("ReelLeft", builder);
            Assert.Contains("ReelRight", builder);
            Assert.Contains("bundle.Unload(true)", builder);
            Assert.Contains("Quaternion.Euler(-90f, 0f, 0f)", builder);
            Assert.Contains("CassetteModelCorrection", builder);
            Assert.Contains("SoulTape cassette axis correction", builder);
            Assert.Contains("ExpectedRecorderBounds", builder);
            Assert.Contains("ExpectedCassetteBounds", builder);
            Assert.Contains("ExpectedRecorderBounds.y / longestImportedDimension", builder);
            Assert.Contains("Vector3.one * modelScale", builder);
            Assert.Contains("CalculateLocalRendererBounds", builder);
            Assert.Contains("ValidateRecorderBounds", builder);
            Assert.Contains("ValidateCassetteBounds", builder);
            Assert.Contains("ValidateHandsBounds", builder);
            Assert.Contains("updateWhenOffscreen = true", builder);
            Assert.Contains("ValidateTextureDependencies", builder);
            Assert.Contains("RecorderBaseColorPath", builder);
            Assert.Contains("RecorderFlapColorPath", builder);
            Assert.Contains("RecorderMetallicSmoothnessPath", builder);
            Assert.Contains("HandsBaseColorPath", builder);
            Assert.Contains("SoulRecorder imported bounds before correction", builder);
            Assert.Contains("SoulRecorder final bounds after correction", builder);
            Assert.Contains("SoulTape cassette final bounds", builder);
            Assert.Contains("Recorder front direction: local -Z", builder);
            Assert.Contains("\"com.unity.modules.assetbundle\": \"1.0.0\"", packageManifest);
            Assert.Contains("\"com.unity.modules.animation\": \"1.0.0\"", packageManifest);
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void AnimatedHandsUseUnitScaleBindingRootAndExactOfflinePreview()
        {
            string repositoryRoot = FindRepositoryRoot();
            string blender = File.ReadAllText(Path.Combine(
                repositoryRoot, "Assets", "SoulRecorder", "Blender",
                "prepare_soulplayer_assets.py"));
            string builder = File.ReadAllText(Path.Combine(
                repositoryRoot, "Assets", "SoulRecorder", "UnityProject", "Assets",
                "Editor", "SoulRecorderAssetBundleBuilder.cs"));
            string preview = File.ReadAllText(Path.Combine(
                repositoryRoot, "Assets", "SoulRecorder", "UnityProject", "Assets",
                "Editor", "SoulRecorderFirstPersonPreview.cs"));
            string previewWrapper = File.ReadAllText(Path.Combine(
                repositoryRoot, "tools", "Preview-SoulRecorderAssets.ps1"));
            string runtimeView = File.ReadAllText(Path.Combine(
                repositoryRoot, "Recorder", "ProceduralSoulRecorderHandsView.cs"));

            Assert.Contains("apply_scale_options=\"FBX_SCALE_ALL\"", blender);
            Assert.Contains("importer.bakeAxisConversion = true", builder);
            Assert.Contains("instance.transform.localScale = Vector3.one", builder);
            Assert.DoesNotContain("Vector3.one * 0.58f", builder);
            Assert.DoesNotContain("HandsModelCorrection", builder);
            Assert.Contains("Animator animator = instance.AddComponent<Animator>()", builder);
            Assert.DoesNotContain("Animator animator = root.AddComponent<Animator>()", builder);
            Assert.Contains("animator.gameObject != handsModel.gameObject", builder);
            Assert.Contains("Animator.StringToHash(\"Base Layer.\" + required)", builder);

            Assert.Contains("soulrecorder_animated_hands.prefab", preview);
            Assert.Contains("soulrecorder_fp.prefab", preview);
            Assert.Contains("soultape_cassette.prefab", preview);
            Assert.Contains("1920, 1080", preview);
            Assert.Contains("50f, 60f, 70f, 75f", preview);
            Assert.Contains("AnimationMode.StartAnimationMode()", preview);
            Assert.Contains("AnimationMode.BeginSampling()", preview);
            Assert.Contains("rig.Animator.gameObject", preview);
            Assert.Contains("transform-chain.txt", preview);
            Assert.Contains("animation-curves.txt", preview);
            Assert.Contains("cassette contact is", preview);
            Assert.Contains("cassette-contact-report.txt", preview);
            Assert.Contains("CassetteHandContactError", preview);
            Assert.Contains("forceMatrixRecalculationPerRender = true", preview);
            Assert.Contains("animated hands bounds expand", preview);
            Assert.Contains("Animation quality checks", preview);
            Assert.Contains("sequential-start-fov70.png", preview);
            Assert.Contains("sequential-stop-fov70.png", preview);
            Assert.Contains("cassette-manipulation-fov70.png", preview);
            Assert.Contains("cassette-manipulation-closeups-fov70.png", preview);
            Assert.Contains("cassette-choreography-report.txt", preview);
            Assert.Contains("screen-space-composition-report.txt", preview);
            Assert.Contains("cassette camera-local rotation", preview);
            Assert.Contains("orientation-carry-fov70.png", preview);
            Assert.Contains("orientation-alignment-fov70.png", preview);
            Assert.Contains("orientation-contact-fov70.png", preview);
            Assert.Contains("orientation-seated-fov70.png", preview);
            Assert.Contains("manual-grip-authoring-status.txt", preview);
            Assert.Contains("SupportHold=PENDING HUMAN APPROVAL", preview);
            Assert.Contains("CassetteCarry=PENDING HUMAN APPROVAL", preview);
            Assert.Contains("SolveContactFrame", preview);
            Assert.Contains("SolveFingerToMarker", preview);
            Assert.Contains("ApplyFingerJointOffsets", preview);
            Assert.Contains("RecorderGripCoreSize", preview);
            Assert.Contains("RESULT=", preview);
            Assert.Contains("$compositionReport", previewWrapper);
            Assert.Contains("$orientationEvidence", previewWrapper);
            Assert.Contains("$cutoffReport", previewWrapper);
            Assert.Contains("$silhouetteReport", previewWrapper);
            Assert.Contains("$silhouetteDiagnostic", previewWrapper);
            Assert.Contains("$manualGripStatus", previewWrapper);
            Assert.Contains("CameraLocalBounds", preview);
            Assert.Contains("215", previewWrapper);
            Assert.Contains("Start-Process -FilePath $UnityEditorPath", previewWrapper);
            Assert.Contains("-Wait", previewWrapper);
            Assert.Contains("$process.ExitCode -ne 0", previewWrapper);
            Assert.Contains("--python-exit-code 1", File.ReadAllText(Path.Combine(
                repositoryRoot, "tools", "Prepare-SoulRecorderAssets.ps1")));
            Assert.Contains("\"CassetteGrip\", \"Hand_1.R\"", blender);
            Assert.Contains("\"CassetteContact\", \"Finger_2_2.R\"", blender);

            Assert.Contains("\"Base Layer.\" + name", runtimeView);
            Assert.Contains("animatedOffscreenAmount", runtimeView);
            Assert.Contains("AnimatedRecorderGripRotationEuler", runtimeView);
            Assert.Contains("AnimatedRecorderGripPosition", runtimeView);
            Assert.Contains("AnimatedCassetteGripRotationEuler", runtimeView);

            string handShader = File.ReadAllText(Path.Combine(
                repositoryRoot, "Assets", "SoulRecorder", "UnityProject", "Assets",
                "SoulPlayer", "Shaders", "SoulPlayerRecorderHands.shader"));
            Assert.Contains("Shader \"SoulPlayer/Recorder Hands PBR\"", handShader);
            Assert.Contains("tex2D(_MainTex", handShader);
            Assert.Contains("UnpackScaleNormal", handShader);
            Assert.Contains("tex2D(_MetallicGlossMap", handShader);
            Assert.Contains("material-report.txt", preview);
            Assert.Contains("SoulPlayer/Recorder Hands PBR", preview);
            Assert.Contains("ConfigureLinearDataMap", builder);
            Assert.Contains("FPS Arm", builder);
            Assert.Contains("FPS Hand", builder);
            Assert.Contains("refusing to guess arm/hand texture identity", builder);
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void StaticGripAuthoringExposesNamedPosesMarkersAndLiveSave()
        {
            string repositoryRoot = FindRepositoryRoot();
            string authoring = File.ReadAllText(Path.Combine(
                repositoryRoot, "Assets", "SoulRecorder", "UnityProject", "Assets",
                "Editor", "SoulRecorderGripPoseAuthoringWindow.cs"));
            string preview = File.ReadAllText(Path.Combine(
                repositoryRoot, "Assets", "SoulRecorder", "UnityProject", "Assets",
                "Editor", "SoulRecorderFirstPersonPreview.cs"));

            Assert.Contains("Save SupportHold", authoring);
            Assert.Contains("Save CassetteCarry", authoring);
            Assert.Contains("Load Saved Pose", authoring);
            Assert.Contains("Reset Pose", authoring);
            Assert.Contains("SoulRecorderGripPoses.json", authoring);
            Assert.Contains("PLAYER CAMERA REFERENCE — FOV 70", authoring);
            Assert.Contains("ReferenceFov = 70f", authoring);
            Assert.Contains("Mathf.Clamp(position.width - 20f, 320f, 640f)", authoring);
            Assert.Contains("0.001f", authoring);
            Assert.Contains("0.005f", authoring);
            Assert.Contains("0.5f", authoring);
            Assert.Contains("-1mm", authoring);
            Assert.Contains("+5mm", authoring);
            Assert.Contains("-0.5°", authoring);
            Assert.Contains("+1°", authoring);
            Assert.Contains("ApplyTransform(control.Transform, item)", authoring);
            Assert.Contains("animator.enabled = false", authoring);
            Assert.DoesNotContain("SolveFingerToMarker", authoring);
            Assert.DoesNotContain("SolveContactFrame", authoring);
            Assert.Contains("SceneView.duringSceneGui", authoring);
            Assert.Contains("EditorSceneManager.sceneOpened += OnSceneOpened", authoring);
            Assert.Contains("EditorSceneManager.sceneOpened -= OnSceneOpened", authoring);
            Assert.Contains("EditorApplication.delayCall += InitializeAfterWindowEnable", authoring);
            Assert.Contains("InitializeAfterWindowEnable", authoring);
            Assert.Contains("!scene.isDirty", authoring);
            Assert.Contains("CassetteSlotTravelAxis", authoring);
            Assert.Contains("CassetteInsertionAxis", authoring);
            Assert.Contains("AnimatedHandsModelName = \"SoulRecorderAnimatedHandsModel\"", authoring);
            Assert.Contains("GetComponentsInChildren<Transform>(true)", authoring);
            Assert.Contains("ResolveLoadedRig()", authoring);
            Assert.Contains("ResolveSide(result, skeleton, \"Left\", \"L\")", authoring);
            Assert.Contains("ResolveSide(result, skeleton, \"Right\", \"R\")", authoring);
            Assert.Contains("ResolveDigit(side, suffix, digit)", authoring);
            Assert.Contains("displayName + \" upper arm\"", authoring);
            Assert.Contains("displayName + \" forearm\"", authoring);
            Assert.Contains("displayName + \" wrist\"", authoring);
            Assert.Contains("displayName + \" palm\"", authoring);
            Assert.Contains("MANUAL ARM SKELETON", authoring);
            Assert.Contains("LEFT — SUPPORT ARM", authoring);
            Assert.Contains("RIGHT — CASSETTE ARM", authoring);
            Assert.Contains("Forearm / elbow", authoring);
            Assert.Contains("LeftArmControls", authoring);
            Assert.Contains("RightArmControls", authoring);
            Assert.Contains("SupportPoseControls(_binding)", authoring);
            Assert.Contains("CassettePoseControls(_binding)", authoring);
            Assert.Contains("ValidateArmSkeletonControls(result)", authoring);
            Assert.Contains("renderer.bones.Contains(bone)", authoring);
            Assert.Contains("Quaternion.AngleAxis(1f, Vector3.right)", authoring);
            Assert.Contains("childLocalPosePreserved", authoring);
            Assert.Contains("bone.localRotation = boneLocalRotation", authoring);
            Assert.Contains("exactRestore", authoring);
            Assert.Contains("no IK or solver correction runs", authoring);
            Assert.Contains("READY FOR MANUAL AUTHORING: ", authoring);
            Assert.Contains("AUTHORING POSE RESTORED: ", authoring);
            Assert.Contains("Approved SupportHold + CassetteCarry restored from JSON", authoring);
            Assert.Contains("scene was not rewritten", authoring);
            Assert.Contains("RestoreApprovedAuthoringPose", authoring);
            Assert.Contains("EditorSceneManager.MarkSceneDirty", authoring);
            Assert.Contains("SetExpandedRecursive", authoring);
            Assert.Contains("EditorApplication.RepaintHierarchyWindow", authoring);
            Assert.Contains("[InitializeOnLoad]", authoring);
            Assert.Contains("SoulRecorderManualAuthoringScenePersistence", authoring);
            Assert.Contains("RestoreCleanLoadedManualScene", authoring);
            Assert.Contains("binding.Passed", authoring);
            Assert.Contains("IndexOf(\"end\"", authoring);
            Assert.Contains("controls.Select", authoring);
            Assert.DoesNotContain("Scene transform was not found:", authoring);
            Assert.DoesNotContain("\"Arm_1L\"", authoring);
            Assert.DoesNotContain("\"Arm_1R\"", authoring);
            Assert.Contains("ArmRendererName = \"SoulRecorderArms\"", authoring);
            Assert.Contains("SoulRecorderArms pose-control exclusion", authoring);
            Assert.Contains("side.Forearm.parent == side.UpperArm", authoring);
            Assert.Contains("side.Wrist.parent == side.Forearm", authoring);
            Assert.Contains("renderer.bones.Contains(bone)", authoring);
            Assert.Contains("Prepare SoulRecorder Manual Grip Scene", preview);
            Assert.Contains("SoulRecorderManualGrip.unity", preview);
            Assert.Contains("rig-binding-report.txt", preview);
            Assert.Contains("GetLoadedRigBindingReport", preview);
            Assert.Contains("RestoreApprovedAuthoringPose", preview);
            Assert.Contains("if (!bindingPassed || !poseRestored)", preview);
            Assert.Contains("if (!bindingPassed || !poseRestored)", preview);
            Assert.Contains("Validate SoulRecorder Manual Arm Controls", preview);
            Assert.Contains("OpenScene(ManualAuthoringScenePath, OpenSceneMode.Single)", preview);
            Assert.Contains("camera.fieldOfView = 70f", preview);
            Assert.Contains("rig.Animator.enabled = false", preview);
            int manualStart = preview.IndexOf(
                "PrepareManualAuthoringScene", StringComparison.Ordinal);
            int manualEnd = preview.IndexOf(
                "public static void Render()", manualStart, StringComparison.Ordinal);
            Assert.True(manualStart >= 0 && manualEnd > manualStart);
            Assert.DoesNotContain(
                "ApplyMarkerSolvedStaticGrip",
                preview.Substring(manualStart, manualEnd - manualStart));
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void CassetteChoreographyHasExplicitContactPushAndPreserveWorldEjectBeats()
        {
            string repositoryRoot = FindRepositoryRoot();
            string blender = File.ReadAllText(Path.Combine(
                repositoryRoot, "Assets", "SoulRecorder", "Blender",
                "prepare_soulplayer_assets.py"));
            string preview = File.ReadAllText(Path.Combine(
                repositoryRoot, "Assets", "SoulRecorder", "UnityProject", "Assets",
                "Editor", "SoulRecorderFirstPersonPreview.cs"));
            string runtimeView = File.ReadAllText(Path.Combine(
                repositoryRoot, "Recorder", "ProceduralSoulRecorderHandsView.cs"));
            string previewWrapper = File.ReadAllText(Path.Combine(
                repositoryRoot, "tools", "Preview-SoulRecorderAssets.ps1"));

            Assert.Contains("CASSETTE_APPROACH_TARGET", blender);
            Assert.Contains("CASSETTE_ALIGNMENT_TARGET", blender);
            Assert.Contains("CASSETTE_FIRST_CONTACT_TARGET", blender);
            Assert.Contains("CASSETTE_HALF_PUSH_TARGET", blender);
            Assert.Contains("rig, 57", blender);
            Assert.Contains("rig, 62", blender);
            Assert.Contains("rig, 66", blender);
            Assert.Contains("rig, 68", blender);
            Assert.Contains("rig, 72", blender);
            Assert.Contains("rig, 153", blender);
            Assert.Contains("rig, 157", blender);
            Assert.Contains("cassette_grip=0.72", blender);
            Assert.Contains("cassette_grip=1.0", blender);
            Assert.Contains("bake_cassette_grip_pose", blender);
            Assert.Contains("aligned_rotation = evaluated_rotations[72]", blender);
            Assert.Contains("57 <= frame <= 80", blender);

            Assert.Contains("EjectTransferSeconds = 23f / 60f", runtimeView);
            Assert.Contains("EjectAudioStopSeconds = 17f / 60f", runtimeView);
            Assert.Contains("StopEnterClipSeconds + EjectAudioStopSeconds", runtimeView);
            Assert.Contains("ReparentPreservingWorldPose", runtimeView);
            Assert.Contains("BindCassetteToGripPose", runtimeView);
            Assert.Contains("_cassetteAuthority.ProceduralTransportAllowed", runtimeView);
            Assert.DoesNotContain("_ejectionOutwardWorldDirection", runtimeView);
            Assert.DoesNotContain("EjectPreTransferTravelMeters", runtimeView);

            Assert.Contains("10-cassette-contact", preview);
            Assert.Contains("18-eject-transfer-pre", preview);
            Assert.Contains("19-eject-transfer-post", preview);
            Assert.Contains("CASSETTE CHOREOGRAPHY — ", preview);
            Assert.Contains("insertion push travel is outside 20-35mm", preview);
            Assert.Contains("recorder-owned cassette drifted relative to CassetteSlot", preview);
            Assert.Contains("Insert sampled CassetteGrip path is static or too small", preview);
            Assert.Contains("Eject sampled CassetteGrip path is static or too small", preview);
            Assert.Contains("MeasureCassetteGripPaths", preview);
            Assert.Contains("camera-local Insert grip displacement before final push", preview);
            Assert.Contains("camera-local Eject grip displacement", preview);
            Assert.Contains("const int samplesPerClip = 21", preview);
            Assert.Contains("-previewBaseline", previewWrapper);
            Assert.Contains("ejection preserve-world ownership transfer introduced a snap", preview);
            Assert.Contains("cassette-manipulation-closeups-fov70.png", preview);
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void RuntimeAnimatedCassetteUsesOneSocketAuthorityAtATime()
        {
            string repositoryRoot = FindRepositoryRoot();
            string view = File.ReadAllText(Path.Combine(
                repositoryRoot, "Recorder", "ProceduralSoulRecorderHandsView.cs"))
                .Replace("\r\n", "\n");

            Assert.Contains("BindCassetteToGripPose();", view);
            Assert.Contains("transform.SetParent(_cassetteGrip, false)", view);
            Assert.Contains("transform.localPosition = Vector3.zero", view);
            Assert.Contains("AnimatedCassetteGripRotationEuler", view);
            Assert.Contains("Insert contact CassetteGrip -> CassetteSlot", view);
            Assert.Contains("Eject grasp CassetteSlot -> CassetteGrip", view);
            Assert.Contains("_cassetteAuthority.ProceduralTransportAllowed", view);
            Assert.DoesNotContain("_cassetteRoot.transform.position =", view);
            Assert.DoesNotContain("_ejectionOutwardWorldDirection", view);
            Assert.Contains("SoulRecorder CASSETTE Animator transition", view);
            Assert.Contains("proceduralTransportActive=", view);
            Assert.Contains("lastWriter=", view);
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void BamenRuntimeMeshIsDerivedForFpsAndCutoffsAreCameraGated()
        {
            string repositoryRoot = FindRepositoryRoot();
            string blender = File.ReadAllText(Path.Combine(
                repositoryRoot, "Assets", "SoulRecorder", "Blender",
                "prepare_soulplayer_assets.py"));
            string builder = File.ReadAllText(Path.Combine(
                repositoryRoot, "Assets", "SoulRecorder", "UnityProject", "Assets",
                "Editor", "SoulRecorderAssetBundleBuilder.cs"));
            string preview = File.ReadAllText(Path.Combine(
                repositoryRoot, "Assets", "SoulRecorder", "UnityProject", "Assets",
                "Editor", "SoulRecorderFirstPersonPreview.cs"));

            Assert.Contains("FPS_ARM_FOREARM_CROP_FRACTION = 0.22", blender);
            Assert.Contains("forearm_projection", blender);
            Assert.Contains("elbows and upper arms removed", blender);
            Assert.Contains("derive_fps_cropped_arm_mesh", blender);
            Assert.Contains("sourceMeshUntouched", blender);
            Assert.Contains("original BAMEN weights unchanged", blender);
            Assert.Contains("bmesh.ops.holes_fill", blender);
            Assert.Contains("SupportSleeveCutoff", blender);
            Assert.Contains("CassetteSleeveCutoff", blender);
            Assert.Contains("supportCutoff.parent != commonRoot", builder);
            Assert.Contains("cassetteCutoff.parent != commonRoot", builder);
            Assert.Contains("AuditSleeveCutoffs", preview);
            Assert.Contains("arm-cutoff-report.txt", preview);
            Assert.Contains("arm-silhouette-diagnostic-fov70.png", preview);
            Assert.Contains("MeasureRenderedViewport", preview);
            Assert.Contains("InteractionForearmAngleDegrees", preview);
            Assert.Contains("MinimumCutoffViewportMargin", preview);
            Assert.Contains("supportCutoffVisible", preview);
            Assert.Contains("SupportSleeveCutoffViewport.y > -0.02f", preview);
            Assert.Contains("InteractionSleeveCutoffViewport.y > -0.02f", preview);
            Assert.Contains("pose.SupportSleeveCutoffViewport.x > 0.46f", preview);
            Assert.Contains("support arm does not originate from the lower-left corner", preview);
            Assert.Contains("pose.SupportSleeveCutoffViewport.x > 0.22f", preview);
            Assert.Contains("pose.InteractionSleeveCutoffViewport.x < 0.54f", preview);
            Assert.Contains("cassette arm does not originate from the lower-right corner", preview);
            Assert.Contains("pose.InteractionSleeveCutoffViewport.x < 0.78f", preview);
            Assert.Contains("cassette forearm is too vertical", preview);
            Assert.Contains("visible.yMax < 0.46f", preview);
            Assert.Contains("visible.center.y < 0.22f", preview);
            Assert.Contains("new Color(0.92f, 0.94f, 0.94f, 1f)", builder);
            Assert.Contains("new Vector3(0.047f, 0.052f, -0.036f)", builder);
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void PackagedRecorderInstanceValidatesAnimatedHandsAndKeepsRecorderFallback()
        {
            string repositoryRoot = FindRepositoryRoot();
            string provider = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "Recorder",
                "Assets",
                "SoulRecorderAssetProvider.cs"));

            Assert.Contains("FindUnique(recorder.transform, \"SoulRecorderModel\")", provider);
            Assert.Contains("model.GetComponentsInChildren<Renderer>(true)", provider);
            Assert.Contains("IsUsableVisibleRenderer", provider);
            Assert.Contains("renderer.enabled", provider);
            Assert.Contains("renderer.gameObject.activeInHierarchy", provider);
            Assert.Contains("renderer.sharedMaterial.shader != null", provider);
            Assert.Contains("renderer.bounds.size.sqrMagnitude", provider);
            Assert.Contains("SetLayerRecursively(recorder, 0)", provider);
            Assert.Contains("SetLayerRecursively(cassette, 0)", provider);
            Assert.Contains("FindUnique(cassette.transform, \"SoulTapeCassette\")", provider);
            Assert.Contains("cassetteModel.GetComponentsInChildren<Renderer>(true)", provider);
            Assert.Contains("HaveExpectedRecorderTextures", provider);
            Assert.Contains("DestroyOwned(recorder, cassette, hands)", provider);
            Assert.Contains("InstantiateAnimatedHands", provider);
            Assert.Contains("TryValidateAnimatedHands", provider);
            Assert.Contains("SoulPlayerAssetContract.AnimatedHandsShaderName", provider);
            Assert.Contains("RecorderGrip", provider);
            Assert.Contains("CassetteGrip", provider);
            Assert.Contains("recorder/cassette only", provider);
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void RejectedLegacyHandsCannotBeInstantiatedByRuntimeBundleApi()
        {
            MethodInfo instantiateHands = typeof(SoulPlayerAssetBundle).GetMethod(
                "InstantiateHands",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.Null(instantiateHands);
            Assert.NotNull(typeof(SoulPlayerAssetBundle).GetMethod(
                "InstantiateAnimatedHands",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void PackagedVisibilityDiagnosticIsOnceOnlyAndReportsCameraRendererCompatibility()
        {
            string repositoryRoot = FindRepositoryRoot();
            string view = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "Recorder",
                "ProceduralSoulRecorderHandsView.cs"));

            Assert.Contains("_packagedVisibilityLogged", view);
            Assert.Contains("LogPackagedVisibilityOnce", view);
            Assert.Contains("camera.cullingMask", view);
            Assert.Contains("WorldToViewportPoint", view);
            Assert.Contains("behindCamera", view);
            Assert.Contains("centerOutsideViewport", view);
            Assert.Contains("cameraRendersLayer", view);
            Assert.Contains("Combined active renderer bounds", view);
            Assert.Contains("renderer.sharedMaterials", view);
            Assert.Contains("metallicSmoothness", view);
            Assert.Contains("TextureName(material, \"_BumpMap\")", view);
            Assert.Contains("Camera-local presentation anchors", view);
            Assert.Contains("cassetteAlignment", view);
            Assert.Contains("cassetteInserted", view);
            Assert.DoesNotContain("packaged hands renderer count", view);
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void AssetPipelineDoesNotDependOnEftAssetFiles()
        {
            string repositoryRoot = FindRepositoryRoot();
            string pipelineSource = string.Join("\n", new[]
            {
                File.ReadAllText(Path.Combine(
                    repositoryRoot,
                    "Assets",
                    "SoulRecorder",
                    "source-manifest.json")),
                File.ReadAllText(Path.Combine(
                    repositoryRoot,
                    "Assets",
                    "SoulRecorder",
                    "UnityProject",
                    "Assets",
                    "Editor",
                    "SoulRecorderAssetBundleBuilder.cs")),
                File.ReadAllText(Path.Combine(
                    repositoryRoot,
                    "Assets",
                    "SoulRecorder",
                    "Blender",
                    "prepare_soulplayer_assets.py"))
            });

            Assert.DoesNotContain("EscapeFromTarkov_Data", pipelineSource);
            Assert.DoesNotContain("Battlestate", pipelineSource);
            Assert.DoesNotContain("GameAssembly.dll", pipelineSource);
            Assert.DoesNotContain("global-metadata.dat", pipelineSource);
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void BamenHandsPipelinePreservesFullRigSocketsTexturesAndRealClips()
        {
            string repositoryRoot = FindRepositoryRoot();
            string preparation = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "Assets",
                "SoulRecorder",
                "Blender",
                "prepare_soulplayer_assets.py"));
            string manifest = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "Assets",
                "SoulRecorder",
                "source-manifest.json"));

            Assert.Contains("296d30fc705b4dff85c2c8a2d2724e7f", manifest);
            Assert.Contains("BAMEN", manifest);
            Assert.Contains("CC BY 4.0", manifest);
            Assert.Contains("8585C328BF9E0872DBE28E08D7B72BF6", manifest);
            Assert.DoesNotContain("DevMops", manifest);
            Assert.DoesNotContain("opengameart-low-poly-arms", manifest);
            Assert.Contains("prepare_hands", preparation);
            Assert.Contains("HANDS_REQUIRED_BONES", preparation);
            Assert.Contains("SoulRecorderHandsRig", preparation);
            Assert.Contains("SoulRecorderHandsRoot", preparation);
            Assert.Contains("RecorderGrip", preparation);
            Assert.Contains("CassetteGrip", preparation);
            Assert.Contains("CassetteContact", preparation);
            Assert.Contains("SoulRecorder_Enter", preparation);
            Assert.Contains("SoulRecorder_CancelInsert", preparation);
            Assert.Contains("bake_anim=animated", preparation);
            Assert.Contains("bake_anim_simplify_factor=0.0", preparation);
            Assert.Contains("soulrecorder_arms_normal.png", preparation);
            Assert.Contains("soulrecorder_hands_normal.png", preparation);
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void BamenAttributionPinsSourceFbxAndModificationNotice()
        {
            string repositoryRoot = FindRepositoryRoot();
            string notice = File.ReadAllText(Path.Combine(
                repositoryRoot, "Assets", "SoulRecorder", "licenses",
                "BAMEN-FPS-Arms-CC-BY-4.0.txt"));

            Assert.Contains("Creative Commons Attribution 4.0 International", notice);
            Assert.Contains("4E1FC02D9EBB61C6C5CC59BCD02B63DA09F95C8DB3C0F7457724C75BCA89B14A", notice);
            Assert.Contains("Sketchfab model ID: 296d30fc705b4dff85c2c8a2d2724e7f", notice);
            Assert.Contains("SoulPlayer changes:", notice);
            Assert.Contains("authored original SoulRecorder first-person poses", notice);
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void RecorderMaterialsUseExplicitResolvableUnityTextureDependencies()
        {
            string repositoryRoot = FindRepositoryRoot();
            string builder = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "Assets",
                "SoulRecorder",
                "UnityProject",
                "Assets",
                "Editor",
                "SoulRecorderAssetBundleBuilder.cs"));
            string preparation = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "Assets",
                "SoulRecorder",
                "Blender",
                "prepare_soulplayer_assets.py"));

            Assert.Contains("soulrecorder_body_basecolor.png", builder);
            Assert.Contains("soulrecorder_flap_basecolor.png", builder);
            Assert.Contains("soulrecorder_body_normal.png", builder);
            Assert.Contains("soulrecorder_body_metallic_smoothness.png", builder);
            Assert.Contains("_MainTex", builder);
            Assert.Contains("_BumpMap", builder);
            Assert.Contains("_MetallicGlossMap", builder);
            Assert.Contains("AssetDatabase.GetDependencies", builder);
            Assert.Contains("1.0 - roughness_pixels[index]", preparation);
            Assert.Contains("opacity_pixels[index]", preparation);
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void SptUnityExposesRequiredAssetBundleLoadContract()
        {
            MethodInfo loadFromFile = typeof(AssetBundle).GetMethod(
                "LoadFromFile",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null);

            Assert.NotNull(loadFromFile);
            Assert.Equal(typeof(AssetBundle), loadFromFile.ReturnType);
            Assert.NotNull(typeof(AssetBundle).GetMethod(
                "Unload",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(bool) },
                null));
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void MissingBundleIsLoggedOnceAndLeavesProceduralFallbackReachable()
        {
            FakeAssetBundleBackend backend = new FakeAssetBundleBackend { Exists = false };
            OfflineTestLog log = new OfflineTestLog();
            SoulPlayerAssetBundle bundle = new SoulPlayerAssetBundle(backend, log);

            Assert.False(bundle.TryLoad("missing.bundle"));
            Assert.False(bundle.TryLoad("missing.bundle"));

            Assert.False(bundle.IsLoaded);
            Assert.Equal(0, backend.LoadCalls);
            Assert.Single(log.Warnings);
            Assert.NotNull(typeof(ProceduralSoulRecorderHandsView));
            Assert.True(HeadlessSoulRecorderHandsView.Instance.TapeInsertionSeconds > 0f);
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void InvalidRecorderPrefabContractIsRejectedAndBundleUnloaded()
        {
            FakeAssetBundleBackend backend = ValidBackend();
            backend.Transforms[SoulPlayerAssetContract.RecorderAssetName] =
                new[] { "CassetteSlot", "StatusLed" };
            OfflineTestLog log = new OfflineTestLog();
            SoulPlayerAssetBundle bundle = new SoulPlayerAssetBundle(backend, log);

            Assert.False(bundle.TryLoad("invalid.bundle"));

            Assert.False(bundle.IsLoaded);
            Assert.Equal(1, backend.UnloadCalls);
            Assert.False(backend.LastUnloadAllLoadedObjects.Value);
            Assert.Contains(log.Errors, message =>
                message.Contains("failed its transform contract") &&
                message.Contains("CassetteInsertionStart"));
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void RecorderWithoutStatusLedRendererIsRejectedSafely()
        {
            FakeAssetBundleBackend backend = ValidBackend();
            backend.Renderers[SoulPlayerAssetContract.RecorderAssetName] =
                Array.Empty<string>();
            OfflineTestLog log = new OfflineTestLog();
            SoulPlayerAssetBundle bundle = new SoulPlayerAssetBundle(backend, log);

            Assert.False(bundle.TryLoad("missing-led-renderer.bundle"));

            Assert.False(bundle.IsLoaded);
            Assert.Equal(1, backend.UnloadCalls);
            Assert.False(backend.LastUnloadAllLoadedObjects.Value);
            Assert.Contains(log.Errors, message =>
                message.Contains("StatusLed") && message.Contains("usable Renderer"));
            Assert.NotNull(typeof(ProceduralSoulRecorderHandsView));
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void ValidBundleLoadsOnceAndCanInstantiateAnimatedHands()
        {
            FakeAssetBundleBackend backend = ValidBackend();
            OfflineTestLog log = new OfflineTestLog();
            SoulPlayerAssetBundle bundle = new SoulPlayerAssetBundle(backend, log);

            Assert.True(bundle.TryLoad("valid.bundle"));
            Assert.True(bundle.TryLoad("ignored-second-path.bundle"));

            Assert.True(bundle.IsLoaded);
            Assert.Equal(1, backend.LoadCalls);
            Assert.NotNull(bundle.InstantiateRecorder());
            Assert.NotNull(bundle.InstantiateCassette());
            Assert.NotNull(bundle.InstantiateAnimatedHands());
            Assert.Equal(3, backend.InstantiateCalls);
            bundle.Dispose();
            Assert.Equal(1, backend.UnloadCalls);
            Assert.False(backend.LastUnloadAllLoadedObjects.Value);
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void InvalidAnimatedHandsFallsBackWithoutRejectingRecorderBundle()
        {
            FakeAssetBundleBackend backend = ValidBackend();
            backend.Transforms[SoulPlayerAssetContract.AnimatedHandsAssetName] =
                new[] { "SoulRecorderHandsRoot", "RecorderGrip" };
            OfflineTestLog log = new OfflineTestLog();
            SoulPlayerAssetBundle bundle = new SoulPlayerAssetBundle(backend, log);

            Assert.True(bundle.TryLoad("recorder-with-invalid-hands.bundle"));
            Assert.True(bundle.IsLoaded);
            Assert.NotNull(bundle.InstantiateRecorder());
            Assert.NotNull(bundle.InstantiateCassette());
            Assert.Null(bundle.InstantiateAnimatedHands());
            Assert.Contains(log.Warnings, message =>
                message.Contains("packaged recorder-only fallback"));
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void RejectedDevMopsPrefabHasNoRuntimeActivationPath()
        {
            string repositoryRoot = FindRepositoryRoot();
            string bundleSource = File.ReadAllText(Path.Combine(
                repositoryRoot, "Recorder", "Assets", "SoulPlayerAssetBundle.cs"));
            string providerSource = File.ReadAllText(Path.Combine(
                repositoryRoot, "Recorder", "Assets", "SoulRecorderAssetProvider.cs"));

            Assert.NotEqual(
                SoulPlayerAssetContract.RejectedLegacyHandsAssetName,
                SoulPlayerAssetContract.AnimatedHandsAssetName);
            Assert.DoesNotContain(
                "LoadPrefab(\n                _bundle,\n                SoulPlayerAssetContract.RejectedLegacyHandsAssetName",
                bundleSource);
            Assert.DoesNotContain("InstantiateHands()", bundleSource + providerSource);
            Assert.Contains("InstantiateAnimatedHands()", bundleSource + providerSource);
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void CassetteTransferCapturesAndRestoresWorldPose()
        {
            string repositoryRoot = FindRepositoryRoot();
            string provider = File.ReadAllText(Path.Combine(
                repositoryRoot, "Recorder", "Assets", "SoulRecorderAssetProvider.cs"));
            string view = File.ReadAllText(Path.Combine(
                repositoryRoot, "Recorder", "ProceduralSoulRecorderHandsView.cs"))
                .Replace("\r\n", "\n");

            Assert.Contains("Vector3 worldPosition = child.position", provider);
            Assert.Contains("Quaternion worldRotation = child.rotation", provider);
            Assert.Contains("Vector3 worldScale = child.lossyScale", provider);
            Assert.Contains("child.SetParent(parent, true)", provider);
            Assert.Contains("child.position = worldPosition", provider);
            Assert.Contains("child.rotation = worldRotation", provider);
            Assert.Contains("ReparentPreservingWorldPose", view);
            Assert.Contains("TransferCassetteTo(\n                    _cassetteSlot", view);
            Assert.Contains("TransferCassetteTo(\n                    _cassetteGrip", view);
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void ReplacementFpsArmStagingIsLicensedIsolatedAndSolverFree()
        {
            string repositoryRoot = FindRepositoryRoot();
            string staging = File.ReadAllText(Path.Combine(
                repositoryRoot, "Assets", "SoulRecorder", "UnityProject", "Assets",
                "Editor", "SoulRecorderReplacementArmStaging.cs"));
            string authoring = File.ReadAllText(Path.Combine(
                repositoryRoot, "Assets", "SoulRecorder", "UnityProject", "Assets",
                "Editor", "SoulRecorderReplacementArmAuthoringWindow.cs"));
            string license = File.ReadAllText(Path.Combine(
                repositoryRoot, "Assets", "SoulRecorder", "ReplacementArms", "Staging",
                "DJMaesenFirstPersonArms", "SOURCE-AND-LICENSE.md"));
            string report = File.ReadAllText(Path.Combine(
                repositoryRoot, "Assets", "SoulRecorder", "UnityProject", "Assets",
                "SoulRecorderReplacementArms", "Validation", "rig-validation-report.txt"));
            string source = Path.Combine(repositoryRoot, "Assets", "SoulRecorder",
                "UnityProject", "Assets", "SoulRecorderReplacementArms", "Source",
                "fpsarms.fbx");

            Assert.True(File.Exists(source));
            using (System.Security.Cryptography.SHA256 sha =
                System.Security.Cryptography.SHA256.Create())
            using (FileStream stream = File.OpenRead(source))
            {
                Assert.Equal(
                    "03CABD1797A90993F630544D1C7794E842EA074C221CCC8EE89511FB6DD61A76",
                    BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty));
            }
            Assert.Contains("DJMaesen", license);
            Assert.Contains("Creative Commons Attribution", license);
            Assert.Contains("e3c42c05b22944e5839deb8e003f0987", license);
            Assert.Contains("L_arm\", \"L_elbow\", \"L_wrist", staging);
            Assert.Contains("R_arm\", \"R_elbow\", \"R_wrist", staging);
            Assert.Contains("Props deliberately remain a root-level sibling", staging);
            Assert.Contains("Direct bone transforms; IK/solvers disabled", authoring);
            Assert.DoesNotContain("LimbIK", authoring);
            Assert.DoesNotContain("Arm_1.L", staging + authoring);
            Assert.Contains("NEW ARM RIG VALIDATION: PASS", report);
            Assert.Contains("Props attached: NO", report);
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void ReplacementStaticMasterUsesExactSeparateChecksummedPoseStore()
        {
            string repositoryRoot = FindRepositoryRoot();
            string staging = File.ReadAllText(Path.Combine(
                repositoryRoot, "Assets", "SoulRecorder", "UnityProject", "Assets",
                "Editor", "SoulRecorderReplacementArmStaging.cs"));
            string authoring = File.ReadAllText(Path.Combine(
                repositoryRoot, "Assets", "SoulRecorder", "UnityProject", "Assets",
                "Editor", "SoulRecorderReplacementArmAuthoringWindow.cs"));
            string posePath = Path.Combine(repositoryRoot, "Assets", "SoulRecorder",
                "UnityProject", "Assets", "SoulRecorderAuthoring",
                "SoulRecorderReplacementGripPoses.json");
            JObject pose = JObject.Parse(File.ReadAllText(posePath));

            Assert.Contains(
                "Assets/SoulRecorderAuthoring/SoulRecorderReplacementGripPoses.json",
                staging);
            Assert.DoesNotContain("GUILayout.Button(\"Save StaticMaster_v1\")",
                authoring);
            Assert.Contains("Load StaticMaster_v1 (LOCKED)", authoring);
            Assert.Contains("LockedStaticMasterHash", authoring);
            Assert.Contains(
                "2EA27472ACBEBF323D9C2502AF7FDFCE1FC3FB8ACDD72D56629C8E8ECEC20AA2",
                authoring);
            Assert.Contains("public Quaternion localRotation", authoring);
            Assert.Contains("public Vector3 localScale", authoring);
            Assert.Contains("public string poseSha256", authoring);
            Assert.Contains("ComputePoseHash", authoring);
            Assert.Contains("DisturbForRoundTrip", authoring);
            Assert.Contains("AssertExact(expected, controlled)", authoring);
            Assert.Contains("StaticMaster_v1 remains locked", authoring);
            Assert.DoesNotContain(
                "Assets/SoulRecorderAuthoring/SoulRecorderGripPoses.json",
                staging + authoring);

            Assert.Equal("StaticMaster_v1", pose.Value<string>("poseName"));
            Assert.Equal(38, pose.Value<int>("transformCount"));
            Assert.Equal(
                "2EA27472ACBEBF323D9C2502AF7FDFCE1FC3FB8ACDD72D56629C8E8ECEC20AA2",
                pose.Value<string>("poseSha256"));
            Assert.NotNull((JArray)pose["keyPoses"]);
            JArray transforms = (JArray)pose["transforms"];
            Assert.Equal(38, transforms.Count);
            Assert.All(transforms, transform =>
            {
                Assert.NotNull(transform["localPosition"]);
                Assert.NotNull(transform["localRotation"]);
                Assert.NotNull(transform["localScale"]);
                Assert.Equal(3, ((JArray)transform["localPositionBits"]).Count);
                Assert.Equal(4, ((JArray)transform["localRotationBits"]).Count);
                Assert.Equal(3, ((JArray)transform["localScaleBits"]).Count);
                Assert.Null(transform["localEulerAngles"]);
            });

            StringBuilder canonical = new StringBuilder();
            canonical.Append("schema=1\nname=StaticMaster_v1\ncount=38\n");
            foreach (JToken transform in transforms)
            {
                string path = transform.Value<string>("path");
                canonical.Append(path.Length).Append(':').Append(path).Append('|');
                foreach (string component in new[]
                {
                    "localPositionBits", "localRotationBits", "localScaleBits"
                })
                {
                    foreach (JToken bits in (JArray)transform[component])
                    {
                        canonical.Append(bits.Value<string>().ToUpperInvariant())
                            .Append('|');
                    }
                }
                canonical.Append('\n');
            }
            using (SHA256 sha = SHA256.Create())
            {
                string calculated = BitConverter.ToString(sha.ComputeHash(
                    Encoding.UTF8.GetBytes(canonical.ToString())))
                    .Replace("-", string.Empty);
                Assert.Equal(pose.Value<string>("poseSha256"), calculated);
            }
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void ReplacementCassetteKeyPoseAuthoringIsHumanDrivenAndMasterLocked()
        {
            string repositoryRoot = FindRepositoryRoot();
            string authoring = File.ReadAllText(Path.Combine(
                repositoryRoot, "Assets", "SoulRecorder", "UnityProject", "Assets",
                "Editor", "SoulRecorderReplacementArmAuthoringWindow.cs"));
            string posePath = Path.Combine(repositoryRoot, "Assets", "SoulRecorder",
                "UnityProject", "Assets", "SoulRecorderAuthoring",
                "SoulRecorderReplacementGripPoses.json");
            JObject pose = JObject.Parse(File.ReadAllText(posePath));

            string[] keyPoses =
            {
                "CassetteAlign", "CassetteContact", "CassetteHalfInserted",
                "CassetteSeated", "CassetteEjectGrip", "CassetteEjectClear"
            };
            foreach (string keyPose in keyPoses)
            {
                Assert.Contains("\"" + keyPose + "\"", authoring);
                Assert.Contains("Start \" + poseName + \" from StaticMaster_v1",
                    authoring);
                Assert.Contains("Save \" + poseName", authoring);
                Assert.Contains("Load \" + poseName", authoring);
            }

            Assert.Contains("Capture Current Scene as \" + poseName", authoring);
            Assert.Contains("CaptureCurrentSceneAs(poseName, saved)", authoring);
            Assert.Contains("EditorUtility.DisplayDialog(", authoring);
            Assert.Contains("Overwrite \" + poseName", authoring);
            Assert.Contains("SaveKeyPose(", authoring);
            Assert.Contains("poseName, current);", authoring);
            Assert.Contains("AssertExact(unchanged, current)", authoring);
            Assert.Contains("No scene transforms were loaded or modified", authoring);
            Assert.Contains("LoadStaticMasterV1", authoring);
            Assert.Contains("must be started from StaticMaster_v1", authoring);
            Assert.Contains("ReplaceKeyPoseSectionPreservingStaticMaster", authoring);
            Assert.Contains("No IK or automatic pose solving is applied", authoring);
            Assert.Contains("SceneView.duringSceneGui", authoring);
            Assert.Contains("CassetteInsertionAxis", authoring);
            Assert.Contains("CassetteSlotTravelAxis", authoring);
            Assert.Contains("CassetteSlotEntry", authoring);
            Assert.Contains("CassetteSlotSeated", authoring);
            Assert.Contains("Cassette center", authoring);
            Assert.Equal("StaticMaster_v1", pose.Value<string>("poseName"));
            Assert.Equal(
                "2EA27472ACBEBF323D9C2502AF7FDFCE1FC3FB8ACDD72D56629C8E8ECEC20AA2",
                pose.Value<string>("poseSha256"));
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void ReplacementAutoAnimationDraftIsNonDestructiveMarkerDrivenAndComplete()
        {
            string repositoryRoot = FindRepositoryRoot();
            string unityRoot = Path.Combine(repositoryRoot, "Assets", "SoulRecorder",
                "UnityProject", "Assets");
            string generatorPath = Path.Combine(unityRoot, "Editor",
                "SoulRecorderReplacementAnimationDraft.cs");
            string generator = File.ReadAllText(generatorPath);
            string humanPosePath = Path.Combine(unityRoot, "SoulRecorderAuthoring",
                "SoulRecorderReplacementGripPoses.json");
            string authoringScenePath = Path.Combine(unityRoot,
                "SoulRecorderReplacementArms", "Scenes",
                "SoulRecorderReplacementStaticPose.unity");
            string outputRoot = Path.Combine(unityRoot,
                "SoulRecorderReplacementArms", "Validation", "AutoAnimationDraft");
            JObject draft = JObject.Parse(File.ReadAllText(Path.Combine(outputRoot,
                "SoulRecorderAutoDraftPoses.json")));
            string report = File.ReadAllText(Path.Combine(outputRoot,
                "auto-animation-report.txt"));

            Assert.Contains("EditorSceneManager.OpenScene(", generator);
            Assert.Contains("SoulRecorderReplacementArmStaging.AuthoringScene", generator);
            Assert.Contains("EditorSceneManager.SaveScene(scene, DraftSceneAsset, true)",
                generator);
            Assert.Contains("Human-authored pose data or authoring scene changed",
                generator);
            Assert.Contains("GripRelativeTransform", generator);
            Assert.Contains("SolveRightArm", generator);
            Assert.Contains("SetCassetteFromRightWrist", generator);
            Assert.Contains("SetCassetteAtLiveSeatedMarker", generator);
            Assert.Contains("CassetteSlotEntry", generator);
            Assert.Contains("CassetteSlotSeated", generator);
            Assert.Contains("CassetteSlotTravelAxis", generator);
            Assert.DoesNotContain("AssetDatabase.SaveAssets()", generator);

            Assert.Equal(1, draft.Value<int>("schemaVersion"));
            Assert.Equal(
                "2EA27472ACBEBF323D9C2502AF7FDFCE1FC3FB8ACDD72D56629C8E8ECEC20AA2",
                draft.Value<string>("lockedStaticMasterSha256"));
            Assert.Equal(FileSha256(humanPosePath),
                draft.Value<string>("sourceHumanPoseFileSha256"));
            Assert.Equal(FileSha256(authoringScenePath),
                draft.Value<string>("sourceAuthoringSceneSha256"));

            string[] expectedPoses =
            {
                "Auto_SupportHold", "Auto_CassetteAlign", "Auto_CassetteContact",
                "Auto_CassetteHalfInserted", "Auto_CassetteSeated",
                "Auto_CassetteRelease", "Auto_StopRecorderReturn",
                "Auto_CassetteEjectApproach", "Auto_CassetteEjectGrip",
                "Auto_CassetteEjectClear", "Auto_HandsExit"
            };
            JArray poses = (JArray)draft["poses"];
            Assert.Equal(expectedPoses.Length, poses.Count);
            foreach (string expectedPose in expectedPoses)
            {
                JObject pose = poses.Children<JObject>().Single(item =>
                    item.Value<string>("poseName") == expectedPose);
                Assert.Equal(38, ((JArray)pose["transforms"]).Count);
            }

            Assert.Contains("contact/slot entry error: 0.000 mm", report);
            Assert.Contains("seated error: 0.000 mm", report);
            Assert.Contains("ownership transfer snap: 0.000 mm", report);
            Assert.Contains("finger collision result: VISUAL DRAFT PASS", report);
            Assert.Contains("AUTO NUMERICAL GATE: PASS", report);
            Assert.Contains("AUTO VISUAL DRAFT GATE: PASS", report);
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void ReplacementAutoAnimationDraftV2IsIsolatedLockedAndHonestlyFailsVisualQa()
        {
            string repositoryRoot = FindRepositoryRoot();
            string unityRoot = Path.Combine(repositoryRoot, "Assets", "SoulRecorder",
                "UnityProject", "Assets");
            string generator = File.ReadAllText(Path.Combine(unityRoot, "Editor",
                "SoulRecorderReplacementAnimationDraft.cs"));
            string outputRoot = Path.Combine(unityRoot,
                "SoulRecorderReplacementArms", "Validation", "AutoAnimationDraft_v2");
            JObject draft = JObject.Parse(File.ReadAllText(Path.Combine(outputRoot,
                "SoulRecorderAutoDraftV2Poses.json")));
            string report = File.ReadAllText(Path.Combine(outputRoot,
                "auto-animation-report.txt"));
            string humanPosePath = Path.Combine(unityRoot, "SoulRecorderAuthoring",
                "SoulRecorderReplacementGripPoses.json");
            string authoringScenePath = Path.Combine(unityRoot,
                "SoulRecorderReplacementArms", "Scenes",
                "SoulRecorderReplacementStaticPose.unity");

            Assert.Contains("AutoAnimationDraft_v3", generator);
            Assert.DoesNotContain("SolveLeftArm(", generator);
            Assert.DoesNotContain("SoulRecorderReplacementAnimationDraft.cs",
                File.ReadAllText(Path.Combine(repositoryRoot, "SoulPlayer.csproj")));

            Assert.True(Directory.Exists(Path.Combine(unityRoot,
                "SoulRecorderReplacementArms", "Validation", "AutoAnimationDraft")));
            Assert.Equal(
                "2EA27472ACBEBF323D9C2502AF7FDFCE1FC3FB8ACDD72D56629C8E8ECEC20AA2",
                draft.Value<string>("lockedStaticMasterSha256"));
            Assert.Equal(FileSha256(humanPosePath),
                draft.Value<string>("sourceHumanPoseFileSha256"));
            Assert.Equal(FileSha256(authoringScenePath),
                draft.Value<string>("sourceAuthoringSceneSha256"));
            Assert.Equal(
                "ED77D8E9452F1F8B1E78B97D4CC7875563CEE724F050B1D34A3556DE7951C373",
                FileSha256(humanPosePath));
            Assert.Equal(
                "E86AD4B9C74067507C56C79794E1923D0FED5C267EBDB97094D8C3831F4CDCD0",
                FileSha256(authoringScenePath));

            string[] expectedPoses =
            {
                "Auto_SupportHold", "Auto_CassetteAlign", "Auto_CassetteContact",
                "Auto_CassetteHalfInserted", "Auto_CassetteSeated",
                "Auto_CassetteRelease", "Auto_StopRecorderReturn",
                "Auto_CassetteEjectApproach", "Auto_CassetteEjectGrip",
                "Auto_CassetteEjectClear", "Auto_HandsBelowCarry",
                "Auto_HandsBelowSeated"
            };
            JArray poses = (JArray)draft["poses"];
            Assert.Equal(expectedPoses.Length, poses.Count);
            foreach (string expectedPose in expectedPoses)
            {
                JObject pose = poses.Children<JObject>().Single(item =>
                    item.Value<string>("poseName") == expectedPose);
                Assert.Equal(38, ((JArray)pose["transforms"]).Count);
            }

            Assert.Contains("contact/slot entry error: 0.000 mm", report);
            Assert.Contains("seated error: 0.000 mm", report);
            Assert.Contains("maximum lateral insertion drift: 0.000", report);
            Assert.Contains("left arm / recorder lock: PASS", report);
            Assert.Contains("AUTO NUMERICAL GATE: PASS", report);
            Assert.Contains("right-arm silhouette: FAIL", report);
            Assert.Contains("finger/cassette readability: FAIL", report);
            Assert.Contains("AUTO VISUAL DRAFT V2 GATE: FAIL", report);
            Assert.True(File.Exists(Path.Combine(outputRoot,
                "soulrecorder-auto-insert-v2-fov70.mp4")));
            Assert.True(File.Exists(Path.Combine(outputRoot,
                "soulrecorder-auto-eject-v2-fov70.mp4")));
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void ReplacementAutoAnimationDraftV3UsesRigidOwnersAndBlocksFailedVisualSequence()
        {
            string repositoryRoot = FindRepositoryRoot();
            string unityRoot = Path.Combine(repositoryRoot, "Assets", "SoulRecorder",
                "UnityProject", "Assets");
            string generator = File.ReadAllText(Path.Combine(unityRoot, "Editor",
                "SoulRecorderReplacementAnimationDraft.cs"));
            string outputRoot = Path.Combine(unityRoot,
                "SoulRecorderReplacementArms", "Validation", "AutoAnimationDraft_v3");
            string posePath = Path.Combine(outputRoot,
                "SoulRecorderAutoDraftV3Poses.json");
            string reportPath = Path.Combine(outputRoot,
                "ownership-diagnostics-report.txt");
            JObject draft = JObject.Parse(File.ReadAllText(posePath));
            string report = File.ReadAllText(reportPath);
            string humanPosePath = Path.Combine(unityRoot, "SoulRecorderAuthoring",
                "SoulRecorderReplacementGripPoses.json");
            string authoringScenePath = Path.Combine(unityRoot,
                "SoulRecorderReplacementArms", "Scenes",
                "SoulRecorderReplacementStaticPose.unity");

            Assert.Contains("LeftHandGripToRecorder", generator);
            Assert.Contains("RightHandGripToCassette", generator);
            Assert.Contains("SlotToCassetteSeated", generator);
            Assert.Contains("SetRecorderFromLeftWrist(context)", generator);
            Assert.Contains("SetCassetteFromRightWrist(context)", generator);
            Assert.Contains("HandTargetForCassette", generator);
            Assert.Contains("transform == context.RecorderGrip", generator);
            Assert.Contains("transform == context.CassetteGrip", generator);
            Assert.Contains("ShortestPathSlerp", generator);
            Assert.Contains("Quaternion.Dot(from, to) < 0f", generator);
            Assert.Contains("V3 full sequences are blocked", generator);
            Assert.DoesNotContain("SolveLeftArm(", generator);
            Assert.DoesNotContain("SoulRecorderReplacementAnimationDraft.cs",
                File.ReadAllText(Path.Combine(repositoryRoot, "SoulPlayer.csproj")));

            Assert.True(Directory.Exists(Path.Combine(unityRoot,
                "SoulRecorderReplacementArms", "Validation", "AutoAnimationDraft")));
            Assert.True(Directory.Exists(Path.Combine(unityRoot,
                "SoulRecorderReplacementArms", "Validation", "AutoAnimationDraft_v2")));
            Assert.Equal(
                "2EA27472ACBEBF323D9C2502AF7FDFCE1FC3FB8ACDD72D56629C8E8ECEC20AA2",
                draft.Value<string>("lockedStaticMasterSha256"));
            Assert.Equal(FileSha256(humanPosePath),
                draft.Value<string>("sourceHumanPoseFileSha256"));
            Assert.Equal(FileSha256(authoringScenePath),
                draft.Value<string>("sourceAuthoringSceneSha256"));
            Assert.Equal(
                "ED77D8E9452F1F8B1E78B97D4CC7875563CEE724F050B1D34A3556DE7951C373",
                FileSha256(humanPosePath));
            Assert.Equal(
                "E86AD4B9C74067507C56C79794E1923D0FED5C267EBDB97094D8C3831F4CDCD0",
                FileSha256(authoringScenePath));

            JArray poses = (JArray)draft["poses"];
            Assert.Equal(12, poses.Count);
            Assert.All(poses.Children<JObject>(), pose =>
                Assert.Equal(38, ((JArray)pose["transforms"]).Count));

            string diagnosticRoot = Path.Combine(outputRoot,
                "Diagnostic-Key-Poses-FOV70");
            string[] expectedDiagnostics =
            {
                "00-StaticMaster_v1.png",
                "01-Auto_CassetteAlign.png",
                "02-Auto_CassetteContact.png",
                "03-Auto_CassetteHalfInserted.png",
                "04-Auto_CassetteSeated.png"
            };
            Assert.All(expectedDiagnostics, file =>
                Assert.True(File.Exists(Path.Combine(diagnosticRoot, file)), file));
            Assert.True(File.Exists(Path.Combine(outputRoot,
                "ownership-diagnostics-fov70.png")));
            Assert.Contains("Recorder-left-hand relative transform drift: 0.000",
                report);
            Assert.Contains("Cassette-right-hand relative transform drift while hand-owned: 0.000",
                report);
            Assert.Contains("Cassette slot-relative drift while recorder-owned: 0.000",
                report);
            Assert.Contains("Insertion ownership-transfer world snap: 0.000",
                report);
            Assert.Contains("Ejection ownership-transfer world snap: 0.000",
                report);
            Assert.Contains("AUTO NUMERICAL GATE: FAIL", report);
            Assert.Contains("AUTO VISUAL DRAFT V3 GATE: REQUIRES FIVE-POSE HUMAN REVIEW",
                report);
            Assert.False(Directory.Exists(Path.Combine(outputRoot,
                "Start-Frames-FOV70")));
            Assert.False(Directory.Exists(Path.Combine(outputRoot,
                "Stop-Frames-FOV70")));
            Assert.Empty(Directory.GetFiles(outputRoot, "*.mp4"));
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void InteractionMasterCandidateSearchIsRotationOnlyRigidBoundedAndNonDestructive()
        {
            string repositoryRoot = FindRepositoryRoot();
            string unityRoot = Path.Combine(repositoryRoot, "Assets", "SoulRecorder",
                "UnityProject", "Assets");
            string generator = File.ReadAllText(Path.Combine(unityRoot, "Editor",
                "SoulRecorderReplacementAnimationDraft.cs"));
            string outputRoot = Path.Combine(unityRoot,
                "SoulRecorderReplacementArms", "Validation",
                "InteractionMasterCandidates");
            JObject candidates = JObject.Parse(File.ReadAllText(Path.Combine(
                outputRoot, "InteractionMasterCandidates.json")));
            string report = File.ReadAllText(Path.Combine(outputRoot,
                "interaction-master-candidates-report.txt"));
            string humanPosePath = Path.Combine(unityRoot,
                "SoulRecorderAuthoring", "SoulRecorderReplacementGripPoses.json");
            string authoringScenePath = Path.Combine(unityRoot,
                "SoulRecorderReplacementArms", "Scenes",
                "SoulRecorderReplacementStaticPose.unity");

            Assert.Contains("BatchGenerateInteractionCandidates", generator);
            Assert.Contains("SolveRotationOnlyArm", generator);
            Assert.Contains("AssertSkeletonLocalGeometryLocked", generator);
            Assert.Contains("InteractionGrip_RightHandToCassette", report);
            Assert.Contains("LeftHandGripToRecorder", generator);
            Assert.Contains("RightHandGripToCassette", generator);
            Assert.Contains("right-arm extension exceeds 85%", generator);
            Assert.DoesNotContain("context.LeftUpper.position +=", generator);
            Assert.DoesNotContain("context.RightUpper.position +=", generator);
            Assert.DoesNotContain("context.LeftUpper.position =", generator);
            Assert.DoesNotContain("context.RightUpper.position =", generator);
            Assert.Contains("No candidate is selected automatically.", report);
            Assert.DoesNotContain("SoulRecorderReplacementAnimationDraft.cs",
                File.ReadAllText(Path.Combine(repositoryRoot, "SoulPlayer.csproj")));

            Assert.Equal(
                "2EA27472ACBEBF323D9C2502AF7FDFCE1FC3FB8ACDD72D56629C8E8ECEC20AA2",
                candidates.Value<string>("lockedStaticMasterSha256"));
            Assert.Equal(FileSha256(humanPosePath),
                candidates.Value<string>("sourceHumanPoseFileSha256"));
            Assert.Equal(FileSha256(authoringScenePath),
                candidates.Value<string>("sourceAuthoringSceneSha256"));
            Assert.Equal(
                "ED77D8E9452F1F8B1E78B97D4CC7875563CEE724F050B1D34A3556DE7951C373",
                FileSha256(humanPosePath));
            Assert.Equal(
                "E86AD4B9C74067507C56C79794E1923D0FED5C267EBDB97094D8C3831F4CDCD0",
                FileSha256(authoringScenePath));

            JArray poses = (JArray)candidates["poses"];
            Assert.Equal(6, poses.Count);
            string[] expectedNames = Enumerable.Range('G', 6)
                .Select(value => "InteractionMaster_" + (char)value).ToArray();
            JObject staticMaster = JObject.Parse(File.ReadAllText(humanPosePath));
            Dictionary<string, JObject> lockedBones = ((JArray)staticMaster["transforms"])
                .Children<JObject>()
                .Where(value => !value.Value<string>("path").EndsWith(
                    "/RecorderGrip", StringComparison.Ordinal) &&
                    !value.Value<string>("path").EndsWith(
                        "/CassetteGrip", StringComparison.Ordinal))
                .ToDictionary(value => value.Value<string>("path"),
                    value => value, StringComparer.Ordinal);
            foreach (string expectedName in expectedNames)
            {
                JObject pose = poses.Children<JObject>().Single(value =>
                    value.Value<string>("poseName") == expectedName);
                Assert.Equal("Hand", pose.Value<string>("cassetteOwner"));
                Assert.Equal(38, ((JArray)pose["transforms"]).Count);
                Assert.NotNull(pose["interactionGripPosition"]);
                Assert.NotNull(pose["interactionGripRotation"]);
                foreach (JObject transform in ((JArray)pose["transforms"])
                    .Children<JObject>())
                {
                    string path = transform.Value<string>("path");
                    JObject locked;
                    if (!lockedBones.TryGetValue(path, out locked))
                    {
                        continue;
                    }
                    AssertVectorFloatBitsEqual(transform["localPosition"],
                        locked["localPosition"], path + " localPosition");
                    AssertVectorFloatBitsEqual(transform["localScale"],
                        locked["localScale"], path + " localScale");
                }
                Assert.Contains("Candidate " +
                    expectedName.Substring(expectedName.Length - 1) + ":", report);
            }
            Assert.Equal(6, report.Split(new[] { "PASS/FAIL: PASS" },
                StringSplitOptions.None).Length - 1);
            Assert.DoesNotContain("PASS/FAIL: FAIL", report);
            Assert.Contains("recorder grip drift: 0.000", report);
            Assert.Contains("cassette grip drift: 0.000", report);
            Assert.Contains("upper/forearm/wrist", report);
            Assert.Contains("skeletal localPosition/localScale drift by bone:",
                report);
            Assert.Contains("position=EXACT scale=EXACT", report);
            Assert.Equal(6, Directory.GetFiles(Path.Combine(outputRoot, "FOV70"),
                "*.png").Length);
            Assert.True(File.Exists(Path.Combine(outputRoot,
                "interaction-master-candidates-fov70.png")));
            Assert.True(File.Exists(Path.Combine(outputRoot,
                "interaction-master-candidates-closeup-fov70.png")));
            Assert.True(File.Exists(Path.Combine(outputRoot,
                "interaction-master-candidates-arm-chain-side-fov70.png")));
            Assert.Equal(6, Directory.GetFiles(Path.Combine(outputRoot,
                "SideDiagnostics"), "*.png").Length);
            Assert.Empty(Directory.GetFiles(outputRoot, "*.mp4",
                SearchOption.AllDirectories));
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void PhysicalGripCandidatesAreMarkerSolvedNonDestructiveAndContactGated()
        {
            string repositoryRoot = FindRepositoryRoot();
            string unityRoot = Path.Combine(repositoryRoot, "Assets",
                "SoulRecorder", "UnityProject", "Assets");
            string outputRoot = Path.Combine(unityRoot,
                "SoulRecorderReplacementArms", "Validation",
                "PhysicalGripCandidates");
            string posePath = Path.Combine(outputRoot,
                "PhysicalGripCandidates.json");
            string reportPath = Path.Combine(outputRoot,
                "physical-grip-l1-l3-report.txt");
            string humanPosePath = Path.Combine(unityRoot,
                "SoulRecorderAuthoring", "SoulRecorderReplacementGripPoses.json");
            string scenePath = Path.Combine(unityRoot,
                "SoulRecorderReplacementArms", "Scenes",
                "SoulRecorderReplacementStaticPose.unity");
            string generatorPath = Path.Combine(unityRoot, "Editor",
                "SoulRecorderReplacementAnimationDraft.cs");

            Assert.True(File.Exists(posePath));
            Assert.True(File.Exists(reportPath));
            JObject generated = JObject.Parse(File.ReadAllText(posePath));
            string report = File.ReadAllText(reportPath);
            string generator = File.ReadAllText(generatorPath);
            Assert.Equal(FileSha256(humanPosePath),
                generated.Value<string>("sourceHumanPoseFileSha256"));
            Assert.Equal(FileSha256(scenePath),
                generated.Value<string>("sourceAuthoringSceneSha256"));
            Assert.Equal(
                "ED77D8E9452F1F8B1E78B97D4CC7875563CEE724F050B1D34A3556DE7951C373",
                FileSha256(humanPosePath));
            Assert.Equal(
                "E86AD4B9C74067507C56C79794E1923D0FED5C267EBDB97094D8C3831F4CDCD0",
                FileSha256(scenePath));

            JArray poses = (JArray)generated["poses"];
            Assert.Equal(3, poses.Count);
            foreach (string name in new[]
            {
                "PhysicalGrip_L1", "PhysicalGrip_L2", "PhysicalGrip_L3"
            })
            {
                JObject pose = poses.Children<JObject>().Single(value =>
                    value.Value<string>("poseName") == name);
                Assert.Equal("Hand", pose.Value<string>("cassetteOwner"));
                Assert.Equal(38, ((JArray)pose["transforms"]).Count);
                Assert.NotNull(pose["interactionGripPosition"]);
                Assert.NotNull(pose["interactionGripRotation"]);
                Assert.NotNull(pose["supportGripPosition"]);
                Assert.NotNull(pose["supportGripRotation"]);
            }
            Assert.Contains("Axis sign correct: YES", report);
            Assert.Contains("CassetteInsertionAxis dot: 1.000000", report);
            Assert.Contains("CassetteSlotTravelAxis dot: 1.000000", report);
            Assert.Contains("Cassette is fixed at mechanical Contact", report);
            Assert.Equal(3, report.Split(new[] { "PASS/FAIL: PASS" },
                StringSplitOptions.None).Length - 1);
            Assert.DoesNotContain("PASS/FAIL: FAIL", report);
            Assert.Contains("context.RightWrist, context.CassetteGrip",
                generator);
            Assert.Contains("RefineCassetteContact", generator);
            Assert.Contains("CassetteThumbGrip", generator);
            Assert.Contains("RecorderSupportPalm", generator);
            Assert.Equal(3, Directory.GetFiles(Path.Combine(outputRoot,
                "FOV70"), "*.png").Length);
            Assert.Equal(3, Directory.GetFiles(Path.Combine(outputRoot,
                "CassetteCloseup"), "*.png").Length);
            Assert.Equal(3, Directory.GetFiles(Path.Combine(outputRoot,
                "RecorderCloseup"), "*.png").Length);
            Assert.True(File.Exists(Path.Combine(outputRoot,
                "physical-grip-l1-l3-fov70.png")));
            Assert.True(File.Exists(Path.Combine(outputRoot,
                "physical-grip-l1-l3-cassette-closeup.png")));
            Assert.True(File.Exists(Path.Combine(outputRoot,
                "physical-grip-l1-l3-recorder-closeup.png")));
            Assert.Empty(Directory.GetFiles(outputRoot, "*.mp4",
                SearchOption.AllDirectories));
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void PoseRepairCandidatesPreserveLockedDataAndRemainStaticPreviewOnly()
        {
            string repositoryRoot = FindRepositoryRoot();
            string unityRoot = Path.Combine(repositoryRoot, "Assets",
                "SoulRecorder", "UnityProject", "Assets");
            string outputRoot = Path.Combine(unityRoot,
                "SoulRecorderReplacementArms", "Validation",
                "PoseRepairCandidates");
            string humanPosePath = Path.Combine(unityRoot,
                "SoulRecorderAuthoring", "SoulRecorderReplacementGripPoses.json");
            string scenePath = Path.Combine(unityRoot,
                "SoulRecorderReplacementArms", "Scenes",
                "SoulRecorderReplacementStaticPose.unity");
            string generator = File.ReadAllText(Path.Combine(unityRoot,
                "Editor", "SoulRecorderReplacementAnimationDraft.cs"));
            JObject generated = JObject.Parse(File.ReadAllText(Path.Combine(
                outputRoot, "SoulRecorderPoseRepairCandidates.json")));
            string report = File.ReadAllText(Path.Combine(outputRoot,
                "pose-repair-a-c-report.txt"));

            Assert.Contains("BatchGeneratePoseRepair", generator);
            Assert.Contains("WristTargetFromContactFrames", generator);
            Assert.Contains("SolveNaturalPinch", generator);
            Assert.Contains("RestoreFingerRotations(context, context.StaticMaster, \"L_\")",
                generator);
            Assert.Equal(
                "2EA27472ACBEBF323D9C2502AF7FDFCE1FC3FB8ACDD72D56629C8E8ECEC20AA2",
                generated.Value<string>("lockedStaticMasterSha256"));
            Assert.Equal(FileSha256(humanPosePath),
                generated.Value<string>("sourceHumanPoseFileSha256"));
            Assert.Equal(FileSha256(scenePath),
                generated.Value<string>("sourceAuthoringSceneSha256"));

            JArray poses = (JArray)generated["poses"];
            Assert.Equal(3, poses.Count);
            foreach (string name in new[]
            {
                "PoseRepair_A", "PoseRepair_B", "PoseRepair_C"
            })
            {
                JObject pose = poses.Children<JObject>().Single(value =>
                    value.Value<string>("poseName") == name);
                Assert.Equal("Hand", pose.Value<string>("cassetteOwner"));
                Assert.Equal(38, ((JArray)pose["transforms"]).Count);
                Assert.NotNull(pose["interactionGripPosition"]);
                Assert.NotNull(pose["supportGripPosition"]);
            }

            Assert.Contains("StaticMaster_v1: LOCKED / unchanged", report);
            Assert.Contains("PhysicalGrip_L3: mechanical contact reference only",
                report);
            Assert.Equal(3, report.Split(new[]
            {
                "slot contact: 0.000 mm / 0.0000 deg"
            }, StringSplitOptions.None).Length - 1);
            Assert.Equal(3, report.Split(new[]
            {
                "left finger max delta: 0.0000 deg"
            }, StringSplitOptions.None).Length - 1);
            Assert.Contains("Right ring/pinky: bit-exact StaticMaster_v1 rotations",
                report);
            Assert.Contains("No animation or runtime integration generated", report);
            Assert.Contains("Human visual selection required; no automatic winner",
                report);
            Assert.True(File.Exists(Path.Combine(outputRoot,
                "pose-repair-a-c-fov70.png")));
            Assert.True(File.Exists(Path.Combine(outputRoot,
                "pose-repair-right-hand-cassette.png")));
            Assert.True(File.Exists(Path.Combine(outputRoot,
                "pose-repair-left-hand-recorder.png")));
            Assert.Equal(3, Directory.GetFiles(Path.Combine(outputRoot,
                "FOV70"), "*.png").Length);
            Assert.Empty(Directory.GetFiles(outputRoot, "*.mp4",
                SearchOption.AllDirectories));
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void AutoAnimationDraftV4UsesApprovedL3AndRemainsPreviewOnly()
        {
            string repositoryRoot = FindRepositoryRoot();
            string unityRoot = Path.Combine(repositoryRoot, "Assets",
                "SoulRecorder", "UnityProject", "Assets");
            string outputRoot = Path.Combine(unityRoot,
                "SoulRecorderReplacementArms", "Validation",
                "AutoAnimationDraft_v4");
            string posePath = Path.Combine(outputRoot,
                "SoulRecorderAutoDraftV4Poses.json");
            string reportPath = Path.Combine(outputRoot,
                "auto-animation-draft-v4-report.txt");
            string insertionMovie = Path.Combine(outputRoot,
                "soulrecorder-insertion-fov70.mp4");
            string ejectionMovie = Path.Combine(outputRoot,
                "soulrecorder-ejection-fov70.mp4");
            string humanPosePath = Path.Combine(unityRoot,
                "SoulRecorderAuthoring", "SoulRecorderReplacementGripPoses.json");
            string scenePath = Path.Combine(unityRoot,
                "SoulRecorderReplacementArms", "Scenes",
                "SoulRecorderReplacementStaticPose.unity");

            Assert.True(File.Exists(posePath));
            Assert.True(File.Exists(reportPath));
            Assert.True(new FileInfo(insertionMovie).Length > 0);
            Assert.True(new FileInfo(ejectionMovie).Length > 0);
            JObject generated = JObject.Parse(File.ReadAllText(posePath));
            JArray poses = (JArray)generated["poses"];
            Assert.Equal(15, poses.Count);
            foreach (string name in new[]
            {
                "StaticMaster_v1", "V4_ApproachCarryGrip",
                "V4_ReorientedClear", "V4_Align", "V4_Contact",
                "V4_HalfInserted", "V4_SeatedHand", "V4_SeatedSlot",
                "V4_EjectGripSlot", "V4_EjectGripHand", "V4_EjectClear"
            })
            {
                Assert.Single(poses.Children<JObject>().Where(value =>
                    value.Value<string>("poseName") == name));
            }
            foreach (JObject pose in poses.Children<JObject>())
            {
                Assert.Equal(38, ((JArray)pose["transforms"]).Count);
                Assert.NotNull(pose["interactionGripPosition"]);
                Assert.NotNull(pose["interactionGripRotation"]);
                Assert.NotNull(pose["supportGripPosition"]);
                Assert.NotNull(pose["supportGripRotation"]);
            }
            Assert.Equal(FileSha256(humanPosePath),
                generated.Value<string>("sourceHumanPoseFileSha256"));
            Assert.Equal(FileSha256(scenePath),
                generated.Value<string>("sourceAuthoringSceneSha256"));
            Assert.Equal(
                "ED77D8E9452F1F8B1E78B97D4CC7875563CEE724F050B1D34A3556DE7951C373",
                FileSha256(humanPosePath));
            Assert.Equal(
                "E86AD4B9C74067507C56C79794E1923D0FED5C267EBDB97094D8C3831F4CDCD0",
                FileSha256(scenePath));

            string report = File.ReadAllText(reportPath);
            Assert.Contains("Interaction/contact: PhysicalGrip_L3", report);
            Assert.Contains("Carry-to-contact cassette rotation: 165.371 deg",
                report);
            Assert.Contains("VisibleReorientationClearOfRecorder", report);
            Assert.Contains("ReorientToCarryOnlyAfterClear", report);
            Assert.Contains("constant orientation, exact slot axis, frozen L3 grip",
                report);
            Assert.Contains("Ownership transfers: identical world pose / zero snap",
                report);
            Assert.Contains("AUTO ANIMATION DRAFT V4: PASS", report);
            Assert.Contains("VISUAL APPROVAL: PENDING", report);
            Assert.True(Directory.GetFiles(Path.Combine(outputRoot,
                "Insertion-Frames-FOV70"), "*.png").Length >= 80);
            Assert.True(Directory.GetFiles(Path.Combine(outputRoot,
                "Ejection-Frames-FOV70"), "*.png").Length >= 70);
            Assert.True(File.Exists(Path.Combine(outputRoot,
                "key-poses-contact-sheet-fov70.png")));
            Assert.True(File.Exists(Path.Combine(outputRoot,
                "cassette-hand-slot-closeup-fov70.png")));

            string documentation = File.ReadAllText(Path.Combine(repositoryRoot,
                "docs", "SOULRECORDER-ASSET-PIPELINE.md"));
            Assert.Contains("Both the insertion and ejection MP4s", documentation);
            Assert.Contains("explicitly approved by the user", documentation);
            Assert.DoesNotContain("AutoAnimationDraft_v4",
                File.ReadAllText(Path.Combine(repositoryRoot,
                    "SoulPlayer.csproj")));
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void AutoAnimationDraftV5KeepsSupportPhysicalAndReorientsClearOfRecorder()
        {
            string repositoryRoot = FindRepositoryRoot();
            string unityRoot = Path.Combine(repositoryRoot, "Assets",
                "SoulRecorder", "UnityProject", "Assets");
            string outputRoot = Path.Combine(unityRoot,
                "SoulRecorderReplacementArms", "Validation",
                "AutoAnimationDraft_v5");
            string posePath = Path.Combine(outputRoot,
                "SoulRecorderAutoDraftV5Poses.json");
            string reportPath = Path.Combine(outputRoot,
                "auto-animation-draft-v5-report.txt");
            string humanPosePath = Path.Combine(unityRoot,
                "SoulRecorderAuthoring", "SoulRecorderReplacementGripPoses.json");
            string scenePath = Path.Combine(unityRoot,
                "SoulRecorderReplacementArms", "Scenes",
                "SoulRecorderReplacementStaticPose.unity");

            Assert.True(File.Exists(posePath));
            Assert.True(File.Exists(reportPath));
            Assert.True(new FileInfo(Path.Combine(outputRoot,
                "soulrecorder-insertion-fov70.mp4")).Length > 0);
            Assert.True(new FileInfo(Path.Combine(outputRoot,
                "soulrecorder-ejection-fov70.mp4")).Length > 0);
            JObject generated = JObject.Parse(File.ReadAllText(posePath));
            JArray poses = (JArray)generated["poses"];
            Assert.Equal(19, poses.Count);
            foreach (string name in new[]
            {
                "PhysicalGrip_L3", "V5_BelowCarry", "V5_RightEntryLow",
                "V5_RightCarry", "V5_ReorientedRight",
                "V5_ReorientationStabilized", "V5_AlignClear",
                "V5_Contact", "V5_HalfInserted", "V5_SeatedHand",
                "V5_SeatedSlot", "V5_EjectGripSlot", "V5_EjectGripHand",
                "V5_EjectStraightClear", "V5_EjectRight"
            })
            {
                Assert.Single(poses.Children<JObject>().Where(value =>
                    value.Value<string>("poseName") == name));
            }
            foreach (JObject pose in poses.Children<JObject>())
            {
                Assert.Equal(38, ((JArray)pose["transforms"]).Count);
            }
            Assert.Equal(FileSha256(humanPosePath),
                generated.Value<string>("sourceHumanPoseFileSha256"));
            Assert.Equal(FileSha256(scenePath),
                generated.Value<string>("sourceAuthoringSceneSha256"));

            string report = File.ReadAllText(reportPath);
            Assert.Contains("PhysicalGrip_L3: PRESERVED", report);
            Assert.Contains("no interpolation", report);
            Assert.Contains("Carry-to-contact cassette rotation: 165.371 deg",
                report);
            Assert.Contains("Maximum visible support-hand contact error: 1.527 mm",
                report);
            Assert.Contains("Contact -> Seated rotation delta: 0.000000 deg",
                report);
            Assert.Contains("Insertion ownership snap: 0.000 mm / 0.000000 deg",
                report);
            Assert.Contains("Ejection ownership snap: 0.000 mm / 0.000000 deg",
                report);
            Assert.Contains("Per-frame numerical gate: PASS", report);
            Assert.Contains("AUTO ANIMATION DRAFT V5: PASS", report);
            Assert.Contains("VISUAL APPROVAL: PENDING", report);
            Assert.True(Directory.GetFiles(Path.Combine(outputRoot,
                "Insertion-Frames-FOV70"), "*.png").Length >= 90);
            Assert.True(Directory.GetFiles(Path.Combine(outputRoot,
                "Ejection-Frames-FOV70"), "*.png").Length >= 80);
            Assert.True(File.Exists(Path.Combine(outputRoot,
                "key-poses-contact-sheet-fov70.png")));
            Assert.True(File.Exists(Path.Combine(outputRoot,
                "cassette-hand-slot-closeup-fov70.png")));
            Assert.DoesNotContain("AutoAnimationDraft_v5",
                File.ReadAllText(Path.Combine(repositoryRoot,
                    "SoulPlayer.csproj")));
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void ReplacementStaticMasterRoundTripRestoresEveryRawComponentExactly()
        {
            string repositoryRoot = FindRepositoryRoot();
            string posePath = Path.Combine(repositoryRoot, "Assets", "SoulRecorder",
                "UnityProject", "Assets", "SoulRecorderAuthoring",
                "SoulRecorderReplacementGripPoses.json");
            JObject saved = JObject.Parse(File.ReadAllText(posePath));
            string temporary = Path.GetTempFileName();
            try
            {
                File.WriteAllText(temporary, saved.ToString());
                Dictionary<string, string[]> expected = ((JArray)saved["transforms"])
                    .ToDictionary(
                        transform => transform.Value<string>("path"),
                        transform => new[]
                        {
                            "localPositionBits", "localRotationBits", "localScaleBits"
                        }.SelectMany(component => ((JArray)transform[component])
                            .Values<string>()).ToArray(),
                        StringComparer.Ordinal);
                Dictionary<string, string[]> disturbed = expected.ToDictionary(
                    pair => pair.Key,
                    pair => Enumerable.Repeat("DEADBEEF", pair.Value.Length).ToArray(),
                    StringComparer.Ordinal);

                JObject loaded = JObject.Parse(File.ReadAllText(temporary));
                foreach (JToken transform in (JArray)loaded["transforms"])
                {
                    string path = transform.Value<string>("path");
                    disturbed[path] = new[]
                    {
                        "localPositionBits", "localRotationBits", "localScaleBits"
                    }.SelectMany(component => ((JArray)transform[component])
                        .Values<string>()).ToArray();
                }

                Assert.Equal(38, disturbed.Count);
                foreach (KeyValuePair<string, string[]> pair in expected)
                {
                    Assert.Equal(pair.Value, disturbed[pair.Key]);
                }
            }
            finally
            {
                File.Delete(temporary);
            }
        }

        private static void AssertVectorFloatBitsEqual(JToken actual,
            JToken expected, string label)
        {
            foreach (string component in new[] { "x", "y", "z" })
            {
                float actualValue = actual.Value<float>(component);
                float expectedValue = expected.Value<float>(component);
                int actualBits = BitConverter.ToInt32(
                    BitConverter.GetBytes(actualValue), 0);
                int expectedBits = BitConverter.ToInt32(
                    BitConverter.GetBytes(expectedValue), 0);
                Assert.True(actualBits == expectedBits,
                    label + "." + component + " drifted");
            }
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "SoulPlayer.csproj")))
                {
                    return current.FullName;
                }
                current = current.Parent;
            }

            throw new DirectoryNotFoundException(
                "SoulPlayer repository root could not be located from the test output.");
        }

        private static string FileSha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(stream))
                    .Replace("-", string.Empty);
            }
        }

        private static FakeAssetBundleBackend ValidBackend()
        {
            FakeAssetBundleBackend backend = new FakeAssetBundleBackend
            {
                Exists = true,
                LoadSucceeds = true
            };
            backend.Prefabs[SoulPlayerAssetContract.RecorderAssetName] = new object();
            backend.Prefabs[SoulPlayerAssetContract.CassetteAssetName] = new object();
            backend.Prefabs[SoulPlayerAssetContract.AnimatedHandsAssetName] = new object();
            backend.Transforms[SoulPlayerAssetContract.RecorderAssetName] =
                SoulPlayerAssetContract.RecorderRequiredTransforms;
            backend.Transforms[SoulPlayerAssetContract.CassetteAssetName] =
                SoulPlayerAssetContract.CassetteRequiredTransforms;
            backend.Transforms[SoulPlayerAssetContract.AnimatedHandsAssetName] =
                SoulPlayerAssetContract.HandsRequiredTransforms;
            backend.Renderers[SoulPlayerAssetContract.RecorderAssetName] =
                new[] { "StatusLed" };
            return backend;
        }

        private sealed class FakeAssetBundleBackend : ISoulPlayerAssetBundleBackend
        {
            private readonly object _bundle = new object();
            internal bool Exists { get; set; }
            internal bool LoadSucceeds { get; set; }
            internal int LoadCalls { get; private set; }
            internal int InstantiateCalls { get; private set; }
            internal int UnloadCalls { get; private set; }
            internal bool? LastUnloadAllLoadedObjects { get; private set; }
            internal Dictionary<string, object> Prefabs { get; } =
                new Dictionary<string, object>(StringComparer.Ordinal);
            internal Dictionary<string, IReadOnlyList<string>> Transforms { get; } =
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            internal Dictionary<string, IReadOnlyList<string>> Renderers { get; } =
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

            public bool FileExists(string path)
            {
                return Exists;
            }

            public bool TryLoadBundle(string path, out object bundle, out string error)
            {
                LoadCalls++;
                bundle = LoadSucceeds ? _bundle : null;
                error = LoadSucceeds ? string.Empty : "simulated invalid bundle";
                return LoadSucceeds;
            }

            public object LoadPrefab(object bundle, string logicalName)
            {
                object prefab;
                return Prefabs.TryGetValue(logicalName, out prefab) ? prefab : null;
            }

            public IReadOnlyList<string> GetTransformNames(object prefab)
            {
                string logicalName = Prefabs.First(pair => ReferenceEquals(pair.Value, prefab)).Key;
                IReadOnlyList<string> names;
                return Transforms.TryGetValue(logicalName, out names)
                    ? names
                    : Array.Empty<string>();
            }

            public bool HasRenderer(object prefab, string transformName)
            {
                string logicalName = Prefabs.First(pair => ReferenceEquals(pair.Value, prefab)).Key;
                IReadOnlyList<string> renderers;
                return Renderers.TryGetValue(logicalName, out renderers) &&
                       renderers.Count(name => string.Equals(
                           name,
                           transformName,
                           StringComparison.Ordinal)) == 1;
            }

            public object Instantiate(object prefab)
            {
                InstantiateCalls++;
                return new object();
            }

            public void Unload(object bundle, bool unloadAllLoadedObjects)
            {
                UnloadCalls++;
                LastUnloadAllLoadedObjects = unloadAllLoadedObjects;
            }
        }
    }
}
