using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SoulPlayer.Editor
{
    public static class SoulRecorderFirstPersonPreview
    {
        private const string HandsPrefabPath =
            "Assets/SoulPlayer/Generated/soulrecorder_animated_hands.prefab";
        private const string RecorderPrefabPath =
            "Assets/SoulPlayer/Generated/soulrecorder_fp.prefab";
        private const string CassettePrefabPath =
            "Assets/SoulPlayer/Generated/soultape_cassette.prefab";
        private const string PreviewScenePath =
            "Assets/SoulRecorderPreview/SoulRecorderFirstPersonPreview.unity";
        private const string ManualAuthoringScenePath =
            "Assets/SoulRecorderAuthoring/SoulRecorderManualGrip.unity";

        // These are the exact normal-Release values in SoulRecorderPresentationTuning.
        // Offline tests compare both sources so the preview cannot silently drift.
        private static readonly Vector3 RuntimeHeldPosition =
            new Vector3(0.060f, -1.460f, 0.360f);
        private static readonly Vector3 RuntimeHeldRotation =
            new Vector3(6f, -4f, -2f);
        private static readonly Vector3 RuntimePresentationScale =
            new Vector3(0.86f, 0.86f, 0.86f);
        private static readonly Vector3 RuntimeRecorderGripRotation =
            new Vector3(67.42075f, 198.49710f, 225.84430f);
        private static readonly Vector3 RuntimeRecorderGripPosition =
            new Vector3(-0.00667f, -0.01765f, -0.00283f);
        private static readonly Vector3 RuntimeCassetteGripRotation =
            new Vector3(18.48994f, 85.46006f, 57.73771f);
        private static readonly Vector3 StaticSupportWristOffset = Vector3.zero;
        private static readonly Vector3 StaticCassetteWristOffset = new Vector3(0f, 0f, 30f);
        private static readonly Vector3 RuntimeOffscreenOffset =
            new Vector3(0.11f, -0.36f, -0.04f);
        private static readonly Vector3 RuntimeExitRotationOffset =
            new Vector3(18f, 3f, 8f);
        private const float EjectTransferNormalized = 23f / 41f;
        private const float CutoffEnvelopeRadius = 0.045f;
        private const float MinimumCutoffViewportMargin = 0.03f;
        // The recorder is tapered and rounded. A full renderer AABB marks empty
        // corner space as solid, so the contact gate uses this authored inner
        // grip core and leaves the visible shell to screenshot review.
        private static readonly Vector3 RecorderGripCoreSize =
            new Vector3(0.104f, 0.194f, 0.035f);
        private static readonly float[] PreviewFovs = { 50f, 60f, 70f, 75f };

        private sealed class StateSpec
        {
            internal string Name;
            internal string Clip;
            internal float NormalizedTime;
            internal bool CassetteOnSlot;
            internal bool CassetteVisible = true;
            internal bool RequireReadable;
            internal bool EjectionAfterTransfer;
        }

        private sealed class StateMetrics
        {
            internal string Name;
            internal float Fov;
            internal Bounds Hands;
            internal Bounds Recorder;
            internal Bounds Cassette;
            internal Rect HandsViewport;
            internal Rect RecorderViewport;
            internal Rect CassetteViewport;
            internal Rect FullPresentationViewport;
            internal Rect VisiblePresentationViewport;
            internal bool CameraInsideHands;
            internal Vector3 SupportShoulderViewport;
            internal Vector3 SupportSleeveCutoffViewport;
            internal Vector3 SupportElbowViewport;
            internal Vector3 SupportHandViewport;
            internal Vector3 SupportWristViewport;
            internal Vector3 InteractionShoulderViewport;
            internal Vector3 InteractionSleeveCutoffViewport;
            internal Vector3 InteractionElbowViewport;
            internal Vector3 InteractionHandViewport;
            internal Vector3 InteractionWristViewport;
            internal float SupportForearmAngleDegrees;
            internal float InteractionForearmAngleDegrees;
            internal float SupportPalmDistance;
            internal float CassetteContactError;
            internal float CassetteBoundsContactError;
            internal float CassetteOrientationErrorDegrees;
            internal float CassetteHandContactError;
            internal float CassetteThumbContactError;
            internal float CassetteIndexContactError;
            internal float CassetteMiddleContactError;
            internal float CassetteOwnershipFrameSnap;
            internal float OwnershipTransferPositionSnap;
            internal float OwnershipTransferRotationSnapDegrees;
            internal bool CassetteAttachedToInteractionHand;
            internal string CassetteOwner;
            internal Vector3 CassetteWorldPosition;
            internal Quaternion CassetteWorldRotation;
            internal Vector3 RecorderWorldPosition;
            internal Quaternion RecorderWorldRotation;
            internal Vector3 CassetteSlotLocalOffset;
            internal float CassetteFrontFacing;
            internal float CassetteRecorderOverlapRatio;
            internal bool Passed;
            internal readonly List<string> Issues = new List<string>();
        }

        private sealed class MotionRange
        {
            internal float MaximumDisplacement;
            internal float PathLength;
        }

        private sealed class CutoffAuditResult
        {
            internal float Fov;
            internal bool SupportCutoffVisible;
            internal bool CassetteCutoffVisible;
            internal float SupportMinimumMargin = float.MaxValue;
            internal float CassetteMinimumMargin = float.MaxValue;
            internal string SupportWorstSample = string.Empty;
            internal string CassetteWorstSample = string.Empty;
            internal int Samples;
        }

        private sealed class CassetteGripPathMetrics
        {
            internal MotionRange InsertBeforeFinalPush;
            internal MotionRange InsertFullClip;
            internal MotionRange EjectFullClip;
        }

        private sealed class StaticGripMetrics
        {
            internal Vector3 RecorderLocalPosition;
            internal Vector3 RecorderLocalRotation;
            internal Vector3 CassetteLocalPosition;
            internal Vector3 CassetteLocalRotation;
            internal float SupportPalmError;
            internal float SupportThumbError;
            internal float SupportIndexError;
            internal float SupportMiddleError;
            internal float SupportRingError;
            internal float SupportPinkyError;
            internal float CassetteThumbError;
            internal float CassetteIndexError;
            internal float CassetteMiddleError;
            internal float RecorderPenetration;
            internal float CassettePenetration;
        }

        private sealed class PreviewRig : IDisposable
        {
            internal GameObject PresentationRoot;
            internal GameObject Hands;
            internal GameObject Recorder;
            internal GameObject Cassette;
            internal Animator Animator;
            internal Transform RecorderGrip;
            internal Transform CassetteGrip;
            internal Transform CassetteSlot;
            internal Transform CassetteAlignment;
            internal Vector3 InsertionSlotLocalPosition;
            internal Quaternion InsertionSlotLocalRotation;
            internal Vector3 InsertionSlotLocalScale;
            internal Vector3 EjectionGripLocalPosition;
            internal Quaternion EjectionGripLocalRotation;
            internal Vector3 EjectionGripLocalScale;

            public void Dispose()
            {
                if (PresentationRoot != null)
                {
                    UnityEngine.Object.DestroyImmediate(PresentationRoot);
                }
            }
        }

        [MenuItem("SoulPlayer/Render SoulRecorder First-Person Preview")]
        public static void RenderFromMenu()
        {
            Render();
        }

        [MenuItem("SoulPlayer/Prepare SoulRecorder Manual Grip Scene")]
        public static void PrepareManualAuthoringScene()
        {
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            Camera camera = CreateCamera();
            camera.fieldOfView = 70f;
            CreateLighting();
            PreviewRig rig = CreateExactRuntimeRig();
            StateSpec carry = States().Single(value => value.Name == "04-insert-carry");
            AnimationClip clip = rig.Animator.runtimeAnimatorController.animationClips
                .Single(value => string.Equals(
                    value.name, carry.Clip, StringComparison.Ordinal));
            clip.SampleAnimation(
                rig.Animator.gameObject,
                clip.length * carry.NormalizedTime);
            SetCassetteOnInsertionGrip(rig);
            rig.Cassette.SetActive(true);
            rig.Animator.enabled = false;
            bool poseRestored;
            string poseRestoreReport =
                SoulRecorderGripPoseAuthoringWindow.RestoreApprovedAuthoringPose(
                    out poseRestored);
            Directory.CreateDirectory(Path.GetDirectoryName(ManualAuthoringScenePath));
            EditorSceneManager.SaveScene(scene, ManualAuthoringScenePath);
            bool bindingPassed;
            string bindingReport =
                SoulRecorderGripPoseAuthoringWindow.GetLoadedRigBindingReport(
                    out bindingPassed);
            string bindingReportPath = Path.Combine(
                Path.GetDirectoryName(ManualAuthoringScenePath),
                "rig-binding-report.txt");
            File.WriteAllText(
                bindingReportPath,
                bindingReport + Environment.NewLine + poseRestoreReport + Environment.NewLine);
            AssetDatabase.Refresh();
            if (!bindingPassed || !poseRestored)
            {
                throw new InvalidOperationException(
                    "SoulRecorder manual rig binding/pose restoration failed. See " +
                    bindingReportPath + Environment.NewLine + bindingReport +
                    Environment.NewLine + poseRestoreReport);
            }
            Selection.activeGameObject = rig.PresentationRoot;
            if (!Application.isBatchMode)
            {
                SoulRecorderGripPoseAuthoringWindow.Open();
            }
            Debug.Log(
                "SoulRecorder manual static-grip scene ready at FOV 70: " +
                ManualAuthoringScenePath);
        }

        [MenuItem("SoulPlayer/Validate SoulRecorder Manual Arm Controls")]
        public static void ValidateManualArmControls()
        {
            if (!File.Exists(ManualAuthoringScenePath))
            {
                throw new FileNotFoundException(
                    "SoulRecorder manual authoring scene was not found.",
                    ManualAuthoringScenePath);
            }
            EditorSceneManager.OpenScene(ManualAuthoringScenePath, OpenSceneMode.Single);
            bool poseRestored;
            string poseRestoreReport =
                SoulRecorderGripPoseAuthoringWindow.RestoreApprovedAuthoringPose(
                    out poseRestored);
            bool bindingPassed;
            string bindingReport =
                SoulRecorderGripPoseAuthoringWindow.GetLoadedRigBindingReport(
                    out bindingPassed);
            string bindingReportPath = Path.Combine(
                Path.GetDirectoryName(ManualAuthoringScenePath),
                "rig-binding-report.txt");
            File.WriteAllText(
                bindingReportPath,
                bindingReport + Environment.NewLine + poseRestoreReport + Environment.NewLine);
            AssetDatabase.Refresh();
            if (!bindingPassed || !poseRestored)
            {
                throw new InvalidOperationException(
                    "SoulRecorder manual arm-control validation failed. See " +
                    bindingReportPath + Environment.NewLine + bindingReport +
                    Environment.NewLine + poseRestoreReport);
            }
            Debug.Log("[PASS] SoulRecorder manual arm controls" +
                Environment.NewLine + bindingReport + Environment.NewLine +
                poseRestoreReport);
        }

        public static void Render()
        {
            string output = CommandLineValue("-previewOutput");
            string baseline = CommandLineValue("-previewBaseline");
            if (string.IsNullOrWhiteSpace(output))
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string repositoryRoot = Path.GetFullPath(Path.Combine(projectRoot, "..", "..", ".."));
                output = Path.Combine(repositoryRoot, "Artifacts", "SoulRecorderPreview");
            }
            output = Path.GetFullPath(output);
            bool allowFailure = Environment.GetCommandLineArgs().Any(
                value => string.Equals(value, "-previewAllowFailure", StringComparison.Ordinal));
            Directory.CreateDirectory(output);

            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            Directory.CreateDirectory(Path.GetDirectoryName(PreviewScenePath));
            Camera camera = CreateCamera();
            CreateLighting();
            PreviewRig rig = CreateExactRuntimeRig();
            EditorSceneManager.SaveScene(scene, PreviewScenePath);

            List<StateMetrics> metrics = new List<StateMetrics>();
            List<string> generated = new List<string>();
            try
            {
                AnimationMode.StartAnimationMode();
                CalculateTransferPoses(rig);
                CassetteGripPathMetrics gripPaths = MeasureCassetteGripPaths(rig, camera);
                List<string> structuralIssues = ValidateTransformChain(rig, output);
                structuralIssues.AddRange(ValidateAnimationTranslationCurves(rig.Animator, output));
                structuralIssues.AddRange(ValidateRuntimeHandMaterials(rig, output));
                AuditSleeveCutoffs(camera, rig, output, structuralIssues);
                foreach (float fov in PreviewFovs)
                {
                    camera.fieldOfView = fov;
                    string fovFolder = Path.Combine(output, "FOV-" + fov.ToString("0"));
                    Directory.CreateDirectory(fovFolder);
                    foreach (StateSpec state in States())
                    {
                        ApplyState(rig, state);
                        StateMetrics row = Measure(camera, rig, state, fov);
                        row.VisiblePresentationViewport = MeasureRenderedViewport(camera);
                        metrics.Add(row);
                        string file = Path.Combine(fovFolder, state.Name + ".png");
                        RenderPng(camera, file);
                        generated.Add(file);
                    }
                }
                ApplyCrossStateQualityGates(metrics);
                ApplyAnimationQualityGates(metrics);
                ApplyCassetteGripPathQualityGates(metrics, gripPaths);
                ApplyScreenSpaceCompositionGates(metrics);
                camera.fieldOfView = 70f;
                WriteCassetteContactReport(output, rig, camera);
                WriteCassetteChoreographyReport(output, metrics, gripPaths);
                WriteScreenSpaceCompositionReport(output, metrics);
                WriteManualGripAuthoringStatus(output);
                WriteReport(output, rig, camera, metrics, generated, structuralIssues);
                CreateContactSheet(
                    Path.Combine(output, "FOV-70"),
                    Path.Combine(output, "contact-sheet-fov70.png"),
                    States().Select(value => value.Name).ToArray());
                CreateContactSheet(
                    Path.Combine(output, "FOV-60"),
                    Path.Combine(output, "contact-sheet-fov60.png"),
                    States().Select(value => value.Name).ToArray());
                CreateContactSheet(
                    Path.Combine(output, "FOV-75"),
                    Path.Combine(output, "contact-sheet-fov75.png"),
                    States().Select(value => value.Name).ToArray());
                string[] manipulationStates = CassetteManipulationStateNames();
                CreateContactSheet(
                    Path.Combine(output, "FOV-70"),
                    Path.Combine(output, "cassette-manipulation-fov70.png"),
                    manipulationStates);
                RenderArmSilhouetteDiagnostics(output, metrics);
                CopyOrientationEvidence(output);
                RenderCassetteCloseUps(
                    camera,
                    rig,
                    output,
                    manipulationStates);
                RenderSequentialPreview(
                    camera,
                    rig,
                    output,
                    "Sequential-Start",
                    "sequential-start-fov70.png",
                    new[]
                    {
                        "SoulRecorder_Enter", "SoulRecorder_Insert",
                        "SoulRecorder_StartExit", "SoulRecorder_Hold"
                    });
                RenderSequentialPreview(
                    camera,
                    rig,
                    output,
                    "Sequential-Stop",
                    "sequential-stop-fov70.png",
                    new[]
                    {
                        "SoulRecorder_StopEnter", "SoulRecorder_Eject",
                        "SoulRecorder_StopExit"
                    });
                CreateBaselineComparisons(baseline, output, structuralIssues);

                List<StateMetrics> failures = metrics.Where(value => !value.Passed).ToList();
                if ((failures.Count > 0 || structuralIssues.Count > 0) && !allowFailure)
                {
                    throw new InvalidOperationException(
                        "SoulRecorder first-person preview quality gates failed for " +
                        failures.Count + " state/FOV samples and " +
                        structuralIssues.Count + " structural checks. Report: " +
                        Path.Combine(output, "preview-report.txt"));
                }
                Debug.Log((failures.Count == 0 && structuralIssues.Count == 0 ? "[PASS] " : "[WARN] ") +
                    "SoulRecorder first-person preview: " + generated.Count +
                    " PNGs, " + failures.Count + " visual gate failures, " +
                    structuralIssues.Count + " structural failures, output=" + output);
            }
            finally
            {
                if (AnimationMode.InAnimationMode())
                {
                    AnimationMode.StopAnimationMode();
                }
                rig.Dispose();
            }
        }

        private static Camera CreateCamera()
        {
            GameObject root = new GameObject("SoulRecorder Preview Camera");
            Camera camera = root.AddComponent<Camera>();
            root.tag = "MainCamera";
            root.transform.position = Vector3.zero;
            root.transform.rotation = Quaternion.identity;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 10f;
            camera.aspect = 16f / 9f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.14f, 0.15f, 0.17f, 1f);
            camera.allowHDR = false;
            camera.allowMSAA = true;
            return camera;
        }

        private static void CreateLighting()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.44f, 0.47f, 0.52f);
            RenderSettings.ambientEquatorColor = new Color(0.20f, 0.22f, 0.25f);
            RenderSettings.ambientGroundColor = new Color(0.08f, 0.09f, 0.10f);
            GameObject lightRoot = new GameObject("SoulRecorder Preview Key Light");
            Light light = lightRoot.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.color = new Color(1f, 0.94f, 0.86f);
            lightRoot.transform.rotation = Quaternion.Euler(28f, -32f, 0f);
        }

        private static PreviewRig CreateExactRuntimeRig()
        {
            GameObject handsPrefab = RequirePrefab(HandsPrefabPath);
            GameObject recorderPrefab = RequirePrefab(RecorderPrefabPath);
            GameObject cassettePrefab = RequirePrefab(CassettePrefabPath);
            PreviewRig result = new PreviewRig();
            result.PresentationRoot = new GameObject("SoulRecorder First-Person Presentation");
            result.PresentationRoot.transform.position = RuntimeHeldPosition;
            result.PresentationRoot.transform.rotation = Quaternion.Euler(RuntimeHeldRotation);
            result.PresentationRoot.transform.localScale = RuntimePresentationScale;

            result.Hands = UnityEngine.Object.Instantiate(
                handsPrefab,
                result.PresentationRoot.transform);
            result.Hands.name = "soulrecorder_animated_hands";
            result.Hands.transform.localPosition = Vector3.zero;
            result.Hands.transform.localRotation = Quaternion.identity;
            result.Hands.transform.localScale = Vector3.one;
            foreach (SkinnedMeshRenderer skinned in
                result.Hands.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // AnimationMode sampling updates the bones immediately, but Unity's
                // batch renderer may otherwise reuse a stale skin matrix cache. Force
                // each evidence render to use the sampled pose just as gameplay does.
                skinned.updateWhenOffscreen = true;
                skinned.forceMatrixRecalculationPerRender = true;
            }
            result.Animator = result.Hands.GetComponentInChildren<Animator>(true);
            Transform recorderGrip = RequireUnique(result.Hands, "RecorderGrip");
            result.RecorderGrip = recorderGrip;
            result.CassetteGrip = RequireUnique(result.Hands, "CassetteGrip");

            result.Recorder = UnityEngine.Object.Instantiate(recorderPrefab, recorderGrip);
            result.Recorder.name = "soulrecorder_fp";
            result.Recorder.transform.localPosition = RuntimeRecorderGripPosition;
            result.Recorder.transform.localRotation = Quaternion.Euler(
                RuntimeRecorderGripRotation);
            result.Recorder.transform.localScale = Vector3.one;
            result.CassetteSlot = RequireUnique(result.Recorder, "CassetteSlot");
            result.CassetteAlignment = RequireUnique(result.Recorder, "CassetteAlignment");

            result.Cassette = UnityEngine.Object.Instantiate(
                cassettePrefab,
                result.CassetteGrip);
            result.Cassette.name = "soultape_cassette";
            result.Cassette.transform.localPosition = Vector3.zero;
            result.Cassette.transform.localRotation = Quaternion.Euler(
                RuntimeCassetteGripRotation);
            result.Cassette.transform.localScale = Vector3.one;
            if (result.Animator == null || result.Animator.runtimeAnimatorController == null)
            {
                result.Dispose();
                throw new InvalidOperationException("Packaged BAMEN Animator was unavailable.");
            }
            return result;
        }

        private static void CalculateTransferPoses(PreviewRig rig)
        {
            SampleClip(rig, "SoulRecorder_Insert", 1f);
            SetCassetteOnInsertionGrip(rig);
            rig.Cassette.transform.SetParent(rig.CassetteSlot, true);
            rig.InsertionSlotLocalPosition = rig.Cassette.transform.localPosition;
            rig.InsertionSlotLocalRotation = rig.Cassette.transform.localRotation;
            rig.InsertionSlotLocalScale = rig.Cassette.transform.localScale;

            SampleClip(rig, "SoulRecorder_Eject", EjectTransferNormalized);
            SetCassetteOnInsertionSlot(rig);
            rig.Cassette.transform.SetParent(rig.CassetteGrip, true);
            rig.EjectionGripLocalPosition = rig.Cassette.transform.localPosition;
            rig.EjectionGripLocalRotation = rig.Cassette.transform.localRotation;
            rig.EjectionGripLocalScale = rig.Cassette.transform.localScale;
            SetCassetteOnInsertionGrip(rig);
        }

        private static void SampleClip(PreviewRig rig, string clipName, float normalized)
        {
            AnimationClip clip = rig.Animator.runtimeAnimatorController.animationClips
                .Single(value => string.Equals(value.name, clipName, StringComparison.Ordinal));
            AnimationMode.BeginSampling();
            try
            {
                AnimationMode.SampleAnimationClip(
                    rig.Animator.gameObject,
                    clip,
                    clip.length * Mathf.Clamp01(normalized));
            }
            finally
            {
                AnimationMode.EndSampling();
            }
        }

        private static void SetCassetteOnInsertionGrip(PreviewRig rig)
        {
            rig.Cassette.transform.SetParent(rig.CassetteGrip, false);
            rig.Cassette.transform.localPosition = Vector3.zero;
            rig.Cassette.transform.localRotation = Quaternion.Euler(
                RuntimeCassetteGripRotation);
            rig.Cassette.transform.localScale = Vector3.one;
        }

        private static void SetCassetteOnInsertionSlot(PreviewRig rig)
        {
            rig.Cassette.transform.SetParent(rig.CassetteSlot, false);
            rig.Cassette.transform.localPosition = rig.InsertionSlotLocalPosition;
            rig.Cassette.transform.localRotation = rig.InsertionSlotLocalRotation;
            rig.Cassette.transform.localScale = rig.InsertionSlotLocalScale;
        }

        private static void SetCassetteOnEjectionGrip(PreviewRig rig)
        {
            rig.Cassette.transform.SetParent(rig.CassetteGrip, false);
            rig.Cassette.transform.localPosition = rig.EjectionGripLocalPosition;
            rig.Cassette.transform.localRotation = rig.EjectionGripLocalRotation;
            rig.Cassette.transform.localScale = rig.EjectionGripLocalScale;
        }

        private static StateSpec[] States()
        {
            return new[]
            {
                State("01-hidden-initial", "SoulRecorder_Enter", 0f, false, false, false),
                State("02-enter-midpoint", "SoulRecorder_Enter", 0.50f, false, true, true),
                State("03-enter-end", "SoulRecorder_Enter", 1f, false, true, true),
                State("04-insert-carry", "SoulRecorder_Insert", 14f / 46f, false, true, true),
                State("05-insert-approach", "SoulRecorder_Insert", 24f / 46f, false, true, true),
                State("06-insert-alignment", "SoulRecorder_Insert", 31f / 46f, false, true, true),
                State("07-insert-first-contact", "SoulRecorder_Insert", 36f / 46f, false, true, true),
                State("08-insert-half-push", "SoulRecorder_Insert", 40f / 46f, false, true, true),
                State("09-insert-push-end", "SoulRecorder_Insert", 42f / 46f, false, true, true),
                State("10-cassette-contact", "SoulRecorder_Insert", 1f, false, true, true),
                State("11-cassette-seated", "SoulRecorder_Hold", 0f, true, true, true),
                State("12-post-transfer-release", "SoulRecorder_StartExit", 7f / 23f, true, true, true),
                State("13-start-exit-midpoint", "SoulRecorder_StartExit", 0.50f, true, true, true),
                State("14-stop-enter-end", "SoulRecorder_StopEnter", 1f, true, true, true),
                State("15-eject-approach", "SoulRecorder_Eject", 7f / 41f, true, true, true),
                State("16-eject-fingers-closed", "SoulRecorder_Eject", 19f / 41f, true, true, true),
                State("17-eject-slot-hold", "SoulRecorder_Eject", 21f / 41f, true, true, true),
                State("18-eject-transfer-pre", "SoulRecorder_Eject", EjectTransferNormalized, true, true, true),
                State("19-eject-transfer-post", "SoulRecorder_Eject", EjectTransferNormalized, false, true, true, true),
                State("20-eject-clear", "SoulRecorder_Eject", 29f / 41f, false, true, true, true),
                State("21-eject-wrist-turn", "SoulRecorder_Eject", 35f / 41f, false, true, true, true),
                State("22-eject-end", "SoulRecorder_Eject", 1f, false, true, true, true),
                State("23-stop-exit-midpoint", "SoulRecorder_StopExit", 0.50f, false, true, true, true),
                State("24-cancel-insert", "SoulRecorder_CancelInsert", 0.50f, false, true, true)
            };
        }

        private static StateSpec State(
            string name,
            string clip,
            float normalized,
            bool cassetteOnSlot,
            bool visible,
            bool requireReadable,
            bool ejectionAfterTransfer = false)
        {
            return new StateSpec
            {
                Name = name,
                Clip = clip,
                NormalizedTime = normalized,
                CassetteOnSlot = cassetteOnSlot,
                CassetteVisible = visible,
                RequireReadable = requireReadable,
                EjectionAfterTransfer = ejectionAfterTransfer
            };
        }

        private static void ApplyState(PreviewRig rig, StateSpec state)
        {
            bool hidden = state.Name == "01-hidden-initial";
            rig.Hands.SetActive(!hidden);
            bool entering = state.Clip == "SoulRecorder_Enter" ||
                            state.Clip == "SoulRecorder_StopEnter";
            bool exiting = state.Clip == "SoulRecorder_StartExit" ||
                           state.Clip == "SoulRecorder_StopExit";
            float offscreenAmount = entering
                ? 0.25f * (1f - state.NormalizedTime)
                : exiting ? 0.25f * state.NormalizedTime : 0f;
            rig.PresentationRoot.transform.position =
                RuntimeHeldPosition + (RuntimeOffscreenOffset * offscreenAmount);
            rig.PresentationRoot.transform.rotation = Quaternion.Slerp(
                Quaternion.Euler(RuntimeHeldRotation),
                Quaternion.Euler(RuntimeHeldRotation + RuntimeExitRotationOffset),
                offscreenAmount);
            // Animator.Update(0) does not deterministically evaluate a frozen frame in
            // Unity batch edit mode. Sample the exact controller clip for screenshots;
            // the instantiated prefab, skeleton, sockets, meshes, and clip are unchanged.
            SampleClip(rig, state.Clip, state.NormalizedTime);
            if (state.CassetteOnSlot)
            {
                SetCassetteOnInsertionSlot(rig);
            }
            else if (state.EjectionAfterTransfer)
            {
                SetCassetteOnEjectionGrip(rig);
            }
            else
            {
                SetCassetteOnInsertionGrip(rig);
            }
            rig.Cassette.SetActive(state.CassetteVisible);
        }

        private static List<CutoffAuditResult> AuditSleeveCutoffs(
            Camera camera,
            PreviewRig rig,
            string output,
            List<string> structuralIssues)
        {
            Transform support = RequireUnique(rig.Hands, "SupportSleeveCutoff");
            Transform cassette = RequireUnique(rig.Hands, "CassetteSleeveCutoff");
            AnimationClip[] clips = rig.Animator.runtimeAnimatorController.animationClips
                .Where(value => value.name.StartsWith(
                    "SoulRecorder_", StringComparison.Ordinal))
                .GroupBy(value => value.name, StringComparer.Ordinal)
                .Select(value => value.First())
                .OrderBy(value => value.name, StringComparer.Ordinal)
                .ToArray();
            List<CutoffAuditResult> results = new List<CutoffAuditResult>();
            foreach (float fov in new[] { 60f, 70f, 75f })
            {
                camera.fieldOfView = fov;
                CutoffAuditResult result = new CutoffAuditResult { Fov = fov };
                foreach (AnimationClip clip in clips)
                {
                    int lastFrame = Mathf.Max(1, Mathf.RoundToInt(clip.length * 60f));
                    for (int frame = 0; frame <= lastFrame; frame++)
                    {
                        float normalized = frame / (float)lastFrame;
                        ApplyState(rig, new StateSpec
                        {
                            Name = "cutoff-audit-" + clip.name,
                            Clip = clip.name,
                            NormalizedTime = normalized,
                            CassetteVisible = true,
                            EjectionAfterTransfer = clip.name == "SoulRecorder_Eject" &&
                                normalized >= EjectTransferNormalized
                        });
                        string sample = clip.name + " frame " + frame + "/" + lastFrame;
                        float supportMargin = CutoffEnvelopeMargin(
                            camera, support.position, CutoffEnvelopeRadius);
                        float cassetteMargin = CutoffEnvelopeMargin(
                            camera, cassette.position, CutoffEnvelopeRadius);
                        if (supportMargin < result.SupportMinimumMargin)
                        {
                            result.SupportMinimumMargin = supportMargin;
                            result.SupportWorstSample = sample;
                        }
                        if (cassetteMargin < result.CassetteMinimumMargin)
                        {
                            result.CassetteMinimumMargin = cassetteMargin;
                            result.CassetteWorstSample = sample;
                        }
                        result.SupportCutoffVisible |= supportMargin <= 0f;
                        result.CassetteCutoffVisible |= cassetteMargin <= 0f;
                        result.Samples++;
                    }
                }
                if (result.SupportCutoffVisible ||
                    result.SupportMinimumMargin < MinimumCutoffViewportMargin)
                {
                    structuralIssues.Add(
                        "FPS support sleeve termination lacks the required screen-edge margin at FOV " +
                        fov.ToString("F0", CultureInfo.InvariantCulture));
                }
                if (result.CassetteCutoffVisible ||
                    result.CassetteMinimumMargin < MinimumCutoffViewportMargin)
                {
                    structuralIssues.Add(
                        "FPS cassette sleeve termination lacks the required screen-edge margin at FOV " +
                        fov.ToString("F0", CultureInfo.InvariantCulture));
                }
                results.Add(result);
            }

            StringBuilder report = new StringBuilder();
            report.AppendLine("SoulRecorder frame-by-frame FPS sleeve cutoff audit");
            report.AppendLine("Envelope radius: " +
                CutoffEnvelopeRadius.ToString("F3", CultureInfo.InvariantCulture) + "m");
            report.AppendLine("Required outside margin: " +
                MinimumCutoffViewportMargin.ToString("F3", CultureInfo.InvariantCulture));
            foreach (CutoffAuditResult result in results)
            {
                report.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "FOV={0:F0} samples={1} supportCutoffVisible={2} " +
                    "supportMinimumScreenEdgeMargin={3:F6} supportWorst={4} " +
                    "cassetteCutoffVisible={5} cassetteMinimumScreenEdgeMargin={6:F6} " +
                    "cassetteWorst={7}",
                    result.Fov,
                    result.Samples,
                    result.SupportCutoffVisible.ToString().ToLowerInvariant(),
                    result.SupportMinimumMargin,
                    result.SupportWorstSample,
                    result.CassetteCutoffVisible.ToString().ToLowerInvariant(),
                    result.CassetteMinimumMargin,
                    result.CassetteWorstSample));
            }
            File.WriteAllText(Path.Combine(output, "arm-cutoff-report.txt"), report.ToString());
            return results;
        }

        private static float CutoffEnvelopeMargin(
            Camera camera,
            Vector3 center,
            float radius)
        {
            Vector3 right = camera.transform.right * radius;
            Vector3 up = camera.transform.up * radius;
            Vector3 diagonalA = (right + up) * 0.70710678f;
            Vector3 diagonalB = (right - up) * 0.70710678f;
            Vector3[] offsets =
            {
                Vector3.zero, right, -right, up, -up,
                diagonalA, -diagonalA, diagonalB, -diagonalB
            };
            float result = float.MaxValue;
            foreach (Vector3 offset in offsets)
            {
                result = Mathf.Min(
                    result,
                    SignedOutsideViewportMargin(
                        camera.WorldToViewportPoint(center + offset)));
            }
            return result;
        }

        private static float SignedOutsideViewportMargin(Vector3 viewport)
        {
            if (viewport.z <= 0f)
            {
                return 1f;
            }
            bool inside = viewport.x >= 0f && viewport.x <= 1f &&
                          viewport.y >= 0f && viewport.y <= 1f;
            if (inside)
            {
                return -Mathf.Min(
                    Mathf.Min(viewport.x, 1f - viewport.x),
                    Mathf.Min(viewport.y, 1f - viewport.y));
            }
            float dx = viewport.x < 0f
                ? -viewport.x
                : viewport.x > 1f ? viewport.x - 1f : 0f;
            float dy = viewport.y < 0f
                ? -viewport.y
                : viewport.y > 1f ? viewport.y - 1f : 0f;
            return Mathf.Sqrt((dx * dx) + (dy * dy));
        }

        private static void ApplyCrossStateQualityGates(List<StateMetrics> metrics)
        {
            foreach (float fov in PreviewFovs)
            {
                List<StateMetrics> readable = metrics.Where(value =>
                    value.Fov == fov &&
                    value.Name != "01-hidden-initial" &&
                    value.Hands.size.sqrMagnitude > 0f).ToList();
                StateMetrics smallest = readable.OrderBy(value =>
                    value.Hands.size.magnitude).First();
                StateMetrics largest = readable.OrderByDescending(value =>
                    value.Hands.size.magnitude).First();
                float expansion = largest.Hands.size.magnitude /
                    Mathf.Max(0.0001f, smallest.Hands.size.magnitude);
                if (expansion > 2.5f)
                {
                    largest.Issues.Add("animated hands bounds expand " +
                        expansion.ToString("F3", CultureInfo.InvariantCulture) +
                        "x across sampled clip states");
                    largest.Passed = false;
                }

                StateMetrics contact = metrics.Single(value =>
                    value.Fov == fov && value.Name == "10-cassette-contact");
                float gap = contact.CassetteContactError;
                if (gap > 0.010f)
                {
                    contact.Issues.Add("cassette contact is " +
                        gap.ToString("F4", CultureInfo.InvariantCulture) +
                        "m from its seated target");
                    contact.Passed = false;
                }
                if (contact.CassetteBoundsContactError > 0.010f)
                {
                    contact.Issues.Add("cassette visible bounds center is " +
                        contact.CassetteBoundsContactError.ToString(
                            "F4", CultureInfo.InvariantCulture) +
                        "m from the slot opening");
                    contact.Passed = false;
                }
                if (contact.CassetteOrientationErrorDegrees > 3f)
                {
                    contact.Issues.Add("cassette orientation differs from slot by " +
                        contact.CassetteOrientationErrorDegrees.ToString(
                            "F2", CultureInfo.InvariantCulture) + " degrees");
                    contact.Passed = false;
                }
                if (contact.CassetteHandContactError > 0.015f)
                {
                    contact.Issues.Add("cassette hand-contact marker is " +
                        contact.CassetteHandContactError.ToString(
                            "F4", CultureInfo.InvariantCulture) +
                        "m from the cassette body");
                    contact.Passed = false;
                }
                float ownershipFrameSnap = Vector3.Distance(
                    contact.Cassette.center,
                    metrics.Single(value =>
                        value.Fov == fov && value.Name == "11-cassette-seated")
                        .Cassette.center);
                contact.CassetteOwnershipFrameSnap = ownershipFrameSnap;
                if (ownershipFrameSnap > 0.010f)
                {
                    contact.Issues.Add("contact-to-seated ownership frame differs by " +
                        ownershipFrameSnap.ToString("F4", CultureInfo.InvariantCulture) +
                        "m");
                    contact.Passed = false;
                }
                if (!contact.CassetteAttachedToInteractionHand)
                {
                    contact.Issues.Add("cassette left CassetteGrip before ownership transfer");
                    contact.Passed = false;
                }
                if (contact.OwnershipTransferPositionSnap > 0.0001f ||
                    contact.OwnershipTransferRotationSnapDegrees > 0.01f)
                {
                    contact.Issues.Add("preserve-world ownership transfer introduced a snap");
                    contact.Passed = false;
                }
            }
            ApplyCassetteChoreographyGates(metrics);
        }

        private static void ApplyCassetteChoreographyGates(List<StateMetrics> metrics)
        {
            const string prefix = "CASSETTE CHOREOGRAPHY — ";
            Func<string, StateMetrics> at70 = name => metrics.Single(value =>
                value.Fov == 70f && value.Name == name);
            StateMetrics carry = at70("04-insert-carry");
            StateMetrics approach = at70("05-insert-approach");
            StateMetrics alignment = at70("06-insert-alignment");
            StateMetrics firstContact = at70("07-insert-first-contact");
            StateMetrics halfPush = at70("08-insert-half-push");
            StateMetrics pushEnd = at70("09-insert-push-end");
            StateMetrics seatedContact = at70("10-cassette-contact");
            StateMetrics release = at70("12-post-transfer-release");

            float approachCurve = DistanceFromLineSegment(
                approach.CassetteWorldPosition,
                carry.CassetteWorldPosition,
                alignment.CassetteWorldPosition);
            RequireRange(
                approach,
                approachCurve,
                0.005f,
                0.050f,
                prefix + "insertion approach is not a controlled curved path");
            float pushTravel = Vector3.Distance(
                firstContact.CassetteWorldPosition,
                seatedContact.CassetteWorldPosition);
            RequireRange(
                seatedContact,
                pushTravel,
                0.020f,
                0.035f,
                prefix + "insertion push travel is outside 20-35mm");
            float halfTravel = Vector3.Distance(
                firstContact.CassetteWorldPosition,
                halfPush.CassetteWorldPosition);
            RequireRange(
                halfPush,
                halfTravel,
                0.008f,
                0.022f,
                prefix + "half-push pose does not visibly advance the cassette");
            float seatSettleTravel = Vector3.Distance(
                pushEnd.CassetteWorldPosition,
                seatedContact.CassetteWorldPosition);
            RequireRange(
                seatedContact,
                seatSettleTravel,
                0.002f,
                0.010f,
                prefix + "seat/click finish is not a distinct 2-10mm beat");
            if (alignment.CassetteOrientationErrorDegrees > 15f ||
                firstContact.CassetteOrientationErrorDegrees > 10f ||
                seatedContact.CassetteOrientationErrorDegrees > 3f)
            {
                seatedContact.Issues.Add(prefix +
                    "cassette does not progressively align with the slot");
                seatedContact.Passed = false;
            }
            RequireCassettePinch(firstContact, prefix + "first-contact pinch is floating");
            RequireCassettePinch(seatedContact, prefix + "seat-frame pinch is floating");
            float releasedFingerDistance = (
                release.CassetteThumbContactError +
                release.CassetteIndexContactError +
                release.CassetteMiddleContactError) / 3f;
            float seatedFingerDistance = (
                seatedContact.CassetteThumbContactError +
                seatedContact.CassetteIndexContactError +
                seatedContact.CassetteMiddleContactError) / 3f;
            if (releasedFingerDistance < seatedFingerDistance + 0.008f)
            {
                release.Issues.Add(prefix +
                    "fingers do not visibly release after recorder ownership begins");
                release.Passed = false;
            }
            float insertSampledPath = Vector3.Distance(
                carry.CassetteWorldPosition,
                seatedContact.CassetteWorldPosition);
            if (insertSampledPath < 0.050f)
            {
                seatedContact.Issues.Add(prefix +
                    "Insert sampled CassetteGrip path is static or too small");
                seatedContact.Passed = false;
            }

            StateMetrics ejectGrip = at70("16-eject-fingers-closed");
            StateMetrics ejectSlotHold = at70("17-eject-slot-hold");
            StateMetrics ejectPre = at70("18-eject-transfer-pre");
            StateMetrics ejectPost = at70("19-eject-transfer-post");
            StateMetrics ejectClear = at70("20-eject-clear");
            StateMetrics ejectEnd = at70("22-eject-end");
            RequireCassettePinch(ejectGrip, prefix + "ejection grasp is floating");
            if (ejectGrip.CassetteOwner != "CassetteSlot" ||
                ejectSlotHold.CassetteOwner != "CassetteSlot" ||
                ejectPre.CassetteOwner != "CassetteSlot")
            {
                ejectPre.Issues.Add(prefix +
                    "cassette left CassetteSlot before the authored transfer event");
                ejectPre.Passed = false;
            }
            float recorderOwnedDrift = Vector3.Distance(
                ejectGrip.CassetteSlotLocalOffset,
                ejectPre.CassetteSlotLocalOffset);
            if (recorderOwnedDrift > 0.0001f)
            {
                ejectPre.Issues.Add(prefix +
                    "recorder-owned cassette drifted relative to CassetteSlot");
                ejectPre.Passed = false;
            }
            float ejectTransferSnap = Vector3.Distance(
                ejectPre.CassetteWorldPosition,
                ejectPost.CassetteWorldPosition);
            float ejectTransferRotationSnap = Quaternion.Angle(
                ejectPre.CassetteWorldRotation,
                ejectPost.CassetteWorldRotation);
            if (ejectTransferSnap > 0.0001f || ejectTransferRotationSnap > 0.01f)
            {
                ejectPost.Issues.Add(prefix +
                    "ejection preserve-world ownership transfer introduced a snap");
                ejectPost.Passed = false;
            }
            if (ejectPost.CassetteOwner != "CassetteGrip" ||
                ejectClear.CassetteOwner != "CassetteGrip")
            {
                ejectClear.Issues.Add(prefix +
                    "interaction hand does not retain cassette after ejection transfer");
                ejectClear.Passed = false;
            }
            float ejectSampledPath = Vector3.Distance(
                ejectPost.CassetteWorldPosition,
                ejectEnd.CassetteWorldPosition);
            if (ejectSampledPath < 0.030f)
            {
                ejectEnd.Issues.Add(prefix +
                    "Eject sampled CassetteGrip path is static or too small");
                ejectEnd.Passed = false;
            }

            float recorderResponse = Vector3.Distance(
                firstContact.RecorderWorldPosition,
                pushEnd.RecorderWorldPosition);
            float recorderResponseDegrees = Quaternion.Angle(
                firstContact.RecorderWorldRotation,
                pushEnd.RecorderWorldRotation);
            RequireRange(
                pushEnd,
                recorderResponse,
                0.002f,
                0.005f,
                prefix + "recorder counter-motion is outside 2-5mm");
            RequireRange(
                pushEnd,
                recorderResponseDegrees,
                0.5f,
                1.5f,
                prefix + "recorder counter-rotation is outside 0.5-1.5 degrees");
        }

        private static void RequireCassettePinch(StateMetrics state, string issue)
        {
            if (state.CassetteThumbContactError > 0.018f ||
                state.CassetteIndexContactError > 0.018f ||
                state.CassetteMiddleContactError > 0.022f)
            {
                state.Issues.Add(issue);
                state.Passed = false;
            }
        }

        private static void RequireRange(
            StateMetrics state,
            float value,
            float minimum,
            float maximum,
            string issue)
        {
            if (value < minimum || value > maximum)
            {
                state.Issues.Add(issue + " (" +
                    value.ToString("F6", CultureInfo.InvariantCulture) + ")");
                state.Passed = false;
            }
        }

        private static float DistanceFromLineSegment(
            Vector3 point,
            Vector3 start,
            Vector3 end)
        {
            Vector3 segment = end - start;
            float denominator = segment.sqrMagnitude;
            if (denominator <= 0.00000001f)
            {
                return Vector3.Distance(point, start);
            }
            float amount = Mathf.Clamp01(Vector3.Dot(point - start, segment) / denominator);
            return Vector3.Distance(point, start + (segment * amount));
        }

        private static float RectOverlapRatio(Rect subject, Rect blocker)
        {
            float left = Mathf.Max(subject.xMin, blocker.xMin);
            float right = Mathf.Min(subject.xMax, blocker.xMax);
            float bottom = Mathf.Max(subject.yMin, blocker.yMin);
            float top = Mathf.Min(subject.yMax, blocker.yMax);
            float intersection = Mathf.Max(0f, right - left) * Mathf.Max(0f, top - bottom);
            float subjectArea = Mathf.Max(0.000001f, subject.width * subject.height);
            return intersection / subjectArea;
        }

        private static void ApplyAnimationQualityGates(List<StateMetrics> metrics)
        {
            const string prefix = "ANIMATION QUALITY — ";
            StateMetrics working = metrics.Single(value =>
                value.Fov == 70f && value.Name == "03-enter-end");
            if (working.RecorderViewport.width < 0.050f ||
                working.RecorderViewport.height < 0.10f)
            {
                working.Issues.Add(prefix + "recorder working pose is edge-on or unreadable");
                working.Passed = false;
            }
            if (working.RecorderViewport.width < 0.12f ||
                working.RecorderViewport.width > 0.25f ||
                working.RecorderViewport.height < 0.22f ||
                working.RecorderViewport.height > 0.40f)
            {
                working.Issues.Add(prefix +
                    "recorder is not staged at a readable first-person inspection scale");
                working.Passed = false;
            }
            Vector2 recorderCenter = working.RecorderViewport.center;
            if (recorderCenter.x < 0.40f || recorderCenter.x > 0.58f ||
                recorderCenter.y < 0.22f || recorderCenter.y > 0.48f)
            {
                working.Issues.Add(prefix +
                    "recorder working pose is outside the lower-middle inspection area");
                working.Passed = false;
            }
            if (working.SupportPalmDistance > 0.030f)
            {
                working.Issues.Add(prefix + "support palm is not in recorder contact");
                working.Passed = false;
            }

            foreach (float fov in new[] { 60f, 70f, 75f })
            {
                foreach (string name in new[]
                {
                    "03-enter-end", "06-insert-alignment", "07-insert-first-contact",
                    "10-cassette-contact", "11-cassette-seated", "14-stop-enter-end",
                    "16-eject-fingers-closed", "18-eject-transfer-pre", "20-eject-clear",
                    "22-eject-end"
                })
                {
                    StateMetrics pose = metrics.Single(value =>
                        value.Fov == fov && value.Name == name);
                    if (pose.SupportSleeveCutoffViewport.y > -0.02f)
                    {
                        pose.Issues.Add(prefix +
                            "support sleeve cutoff enters the rendered viewport at FOV " +
                            fov.ToString("F0", CultureInfo.InvariantCulture));
                        pose.Passed = false;
                    }
                    if (pose.SupportSleeveCutoffViewport.x > 0.46f)
                    {
                        pose.Issues.Add(prefix +
                            "support arm does not originate from the lower-left corner");
                        pose.Passed = false;
                    }
                    if (pose.SupportSleeveCutoffViewport.x > 0.22f)
                    {
                        pose.Issues.Add(prefix +
                            "support sleeve does not enter close to the lower-left edge");
                        pose.Passed = false;
                    }
                    if (pose.SupportForearmAngleDegrees < 22f)
                    {
                        pose.Issues.Add(prefix +
                            "support forearm is too vertical for a lower-corner FPS pose");
                        pose.Passed = false;
                    }
                }
                foreach (string name in new[]
                {
                    "04-insert-carry", "06-insert-alignment", "07-insert-first-contact",
                    "10-cassette-contact", "16-eject-fingers-closed",
                    "18-eject-transfer-pre", "20-eject-clear"
                })
                {
                    StateMetrics pose = metrics.Single(value =>
                        value.Fov == fov && value.Name == name);
                    if (pose.InteractionSleeveCutoffViewport.y > -0.02f)
                    {
                        pose.Issues.Add(prefix +
                            "cassette sleeve cutoff enters the rendered viewport at FOV " +
                            fov.ToString("F0", CultureInfo.InvariantCulture));
                        pose.Passed = false;
                    }
                    if (pose.InteractionSleeveCutoffViewport.x < 0.54f)
                    {
                        pose.Issues.Add(prefix +
                            "cassette arm does not originate from the lower-right corner");
                        pose.Passed = false;
                    }
                    if (pose.InteractionSleeveCutoffViewport.x < 0.78f)
                    {
                        pose.Issues.Add(prefix +
                            "cassette sleeve does not enter close to the lower-right edge");
                        pose.Passed = false;
                    }
                    if (pose.InteractionForearmAngleDegrees < 14f)
                    {
                        pose.Issues.Add(prefix +
                            "cassette forearm is too vertical for a corner-origin FPS pose");
                        pose.Passed = false;
                    }
                }
            }

            foreach (string name in new[]
            {
                "06-insert-alignment", "07-insert-first-contact",
                "10-cassette-contact", "16-eject-fingers-closed",
                "18-eject-transfer-pre", "20-eject-clear"
            })
            {
                StateMetrics pose = metrics.Single(value =>
                    value.Fov == 70f && value.Name == name);
                if (pose.InteractionElbowViewport.y >
                    pose.InteractionHandViewport.y + 0.08f)
                {
                    pose.Issues.Add(prefix +
                        "interaction forearm rises above the manipulating hand");
                    pose.Passed = false;
                }
                if (pose.InteractionElbowViewport.x > 0.32f &&
                    pose.InteractionElbowViewport.x < 0.68f &&
                    pose.InteractionElbowViewport.y > 0.48f)
                {
                    pose.Issues.Add(prefix + "interaction elbow enters central viewport");
                    pose.Passed = false;
                }
                if (pose.InteractionShoulderViewport.y > 0.28f ||
                    pose.SupportShoulderViewport.y > 0.28f)
                {
                    pose.Issues.Add(prefix + "upper-arm origin occupies the working area");
                    pose.Passed = false;
                }
            }

            StateMetrics alignment = metrics.Single(value =>
                value.Fov == 70f && value.Name == "06-insert-alignment");
            StateMetrics contact = metrics.Single(value =>
                value.Fov == 70f && value.Name == "10-cassette-contact");
            if (alignment.InteractionHandViewport.y >
                contact.InteractionHandViewport.y + 0.16f)
            {
                alignment.Issues.Add(prefix + "cassette hand makes a large upward arc");
                alignment.Passed = false;
            }
        }

        private static CassetteGripPathMetrics MeasureCassetteGripPaths(
            PreviewRig rig,
            Camera camera)
        {
            CassetteGripPathMetrics result = new CassetteGripPathMetrics
            {
                InsertBeforeFinalPush = MeasureGripMotion(
                    rig,
                    camera,
                    "SoulRecorder_Insert",
                    0f,
                    36f / 46f,
                    121),
                InsertFullClip = MeasureGripMotion(
                    rig,
                    camera,
                    "SoulRecorder_Insert",
                    0f,
                    1f,
                    161),
                EjectFullClip = MeasureGripMotion(
                    rig,
                    camera,
                    "SoulRecorder_Eject",
                    0f,
                    1f,
                    161)
            };
            SampleClip(rig, "SoulRecorder_Enter", 0f);
            return result;
        }

        private static MotionRange MeasureGripMotion(
            PreviewRig rig,
            Camera camera,
            string clip,
            float startNormalized,
            float endNormalized,
            int samples)
        {
            MotionRange result = new MotionRange();
            Vector3 start = Vector3.zero;
            Vector3 previous = Vector3.zero;
            for (int index = 0; index < samples; index++)
            {
                float amount = index / (float)(samples - 1);
                float normalized = Mathf.Lerp(startNormalized, endNormalized, amount);
                SampleClip(rig, clip, normalized);
                Vector3 current = camera.transform.InverseTransformPoint(
                    rig.CassetteGrip.position);
                if (index == 0)
                {
                    start = current;
                }
                else
                {
                    result.PathLength += Vector3.Distance(previous, current);
                }
                result.MaximumDisplacement = Mathf.Max(
                    result.MaximumDisplacement,
                    Vector3.Distance(start, current));
                previous = current;
            }
            return result;
        }

        private static void ApplyCassetteGripPathQualityGates(
            List<StateMetrics> metrics,
            CassetteGripPathMetrics paths)
        {
            const string prefix = "ANIMATION QUALITY — ";
            StateMetrics insert = metrics.Single(value =>
                value.Fov == 70f && value.Name == "10-cassette-contact");
            StateMetrics eject = metrics.Single(value =>
                value.Fov == 70f && value.Name == "22-eject-end");
            RequireRange(
                insert,
                paths.InsertBeforeFinalPush.MaximumDisplacement,
                0.10f,
                0.25f,
                prefix + "camera-local Insert grip displacement before final push " +
                "is outside the compact 0.10-0.25m target");
            RequireRange(
                insert,
                paths.InsertFullClip.PathLength,
                0.10f,
                0.35f,
                prefix + "camera-local Insert grip path is not compact");
            RequireRange(
                eject,
                paths.EjectFullClip.MaximumDisplacement,
                0.08f,
                0.25f,
                prefix + "camera-local Eject grip displacement is outside the compact target");
            RequireRange(
                eject,
                paths.EjectFullClip.PathLength,
                0.10f,
                0.65f,
                prefix + "camera-local Eject grip path is not compact");
        }

        private static void ApplyScreenSpaceCompositionGates(List<StateMetrics> metrics)
        {
            const string prefix = "SCREEN COMPOSITION — ";
            Func<string, StateMetrics> at70 = name => metrics.Single(value =>
                value.Fov == 70f && value.Name == name);
            foreach (string name in new[]
            {
                "04-insert-carry", "06-insert-alignment", "07-insert-first-contact",
                "10-cassette-contact", "11-cassette-seated", "20-eject-clear"
            })
            {
                StateMetrics state = at70(name);
                Rect visible = state.VisiblePresentationViewport;
                if (visible.width < 0.34f || visible.width > 0.60f ||
                    visible.height < 0.28f || visible.height > 0.58f)
                {
                    state.Issues.Add(prefix +
                        "full interaction does not occupy the intended lower-middle " +
                        "34-60% working region");
                    state.Passed = false;
                }
                // Runtime feedback favored Tarkov's lower, chest-level tool
                // staging. Keep enough of the presentation above the bottom
                // edge to remain readable without forcing it back toward the
                // unnaturally high pre-feedback pose.
                if (visible.yMax < 0.46f || visible.center.y < 0.22f)
                {
                    state.Issues.Add(prefix + "working pose remains too low in the viewport");
                    state.Passed = false;
                }
            }

            StateMetrics carry = at70("04-insert-carry");
            StateMetrics approach = at70("05-insert-approach");
            StateMetrics alignment = at70("06-insert-alignment");
            StateMetrics ejectClear = at70("20-eject-clear");
            foreach (StateMetrics readableCassette in new[]
            {
                carry, approach, alignment, ejectClear
            })
            {
                Rect cassette = ClampedViewportRect(readableCassette.CassetteViewport);
                if (cassette.width < 0.055f || cassette.height < 0.055f)
                {
                    readableCassette.Issues.Add(prefix +
                        "cassette occupies too few pixels to read clearly");
                    readableCassette.Passed = false;
                }
            }
            if (carry.CassetteFrontFacing < 0.45f)
            {
                carry.Issues.Add(prefix + "carry cassette broad face is too edge-on");
                carry.Passed = false;
            }
            if (carry.CassetteRecorderOverlapRatio > 0.15f ||
                approach.CassetteRecorderOverlapRatio > 0.25f)
            {
                approach.Issues.Add(prefix +
                    "cassette passes behind the recorder before alignment");
                approach.Passed = false;
            }
            if (carry.CassetteViewport.center.x <= carry.RecorderViewport.center.x)
            {
                carry.Issues.Add(prefix +
                    "carry cassette is not staged on the interaction side of the recorder");
                carry.Passed = false;
            }
        }

        private static Rect ClampedViewportRect(Rect value)
        {
            return Rect.MinMaxRect(
                Mathf.Clamp01(value.xMin),
                Mathf.Clamp01(value.yMin),
                Mathf.Clamp01(value.xMax),
                Mathf.Clamp01(value.yMax));
        }

        private static StateMetrics Measure(
            Camera camera,
            PreviewRig rig,
            StateSpec state,
            float fov)
        {
            StateMetrics row = new StateMetrics
            {
                Name = state.Name,
                Fov = fov,
                Hands = CameraLocalBounds(camera, rig.Hands.GetComponentsInChildren<Renderer>(true)),
                Recorder = CameraLocalBounds(camera, rig.Recorder.GetComponentsInChildren<Renderer>(true)),
                Cassette = CameraLocalBounds(camera, rig.Cassette.GetComponentsInChildren<Renderer>(true))
            };
            row.HandsViewport = ViewportRect(camera, rig.Hands.GetComponentsInChildren<Renderer>(true));
            row.RecorderViewport = ViewportRect(
                camera,
                rig.Recorder.GetComponentsInChildren<Renderer>(true));
            row.CassetteViewport = ViewportRect(
                camera,
                rig.Cassette.GetComponentsInChildren<Renderer>(true));
            row.FullPresentationViewport = ViewportRect(
                camera,
                rig.PresentationRoot.GetComponentsInChildren<Renderer>(true));
            row.SupportShoulderViewport = camera.WorldToViewportPoint(
                RequireUnique(rig.Hands, "Arm_1.L").position);
            row.SupportSleeveCutoffViewport = camera.WorldToViewportPoint(
                RequireUnique(rig.Hands, "SupportSleeveCutoff").position);
            row.SupportElbowViewport = camera.WorldToViewportPoint(
                RequireUnique(rig.Hands, "Arm_2.L").position);
            row.SupportHandViewport = camera.WorldToViewportPoint(
                RequireUnique(rig.Hands, "Hand_1.L").position);
            row.SupportWristViewport = camera.WorldToViewportPoint(
                RequireUnique(rig.Hands, "Hand_2.L").position);
            row.InteractionShoulderViewport = camera.WorldToViewportPoint(
                RequireUnique(rig.Hands, "Arm_1.R").position);
            row.InteractionSleeveCutoffViewport = camera.WorldToViewportPoint(
                RequireUnique(rig.Hands, "CassetteSleeveCutoff").position);
            row.InteractionElbowViewport = camera.WorldToViewportPoint(
                RequireUnique(rig.Hands, "Arm_2.R").position);
            row.InteractionHandViewport = camera.WorldToViewportPoint(
                RequireUnique(rig.Hands, "Hand_1.R").position);
            row.InteractionWristViewport = camera.WorldToViewportPoint(
                RequireUnique(rig.Hands, "Hand_2.R").position);
            row.SupportForearmAngleDegrees = ScreenAngleFromVertical(
                row.SupportElbowViewport,
                row.SupportHandViewport);
            row.InteractionForearmAngleDegrees = ScreenAngleFromVertical(
                row.InteractionElbowViewport,
                row.InteractionHandViewport);
            Bounds recorderWorldBounds = WorldRendererBounds(
                rig.Recorder.GetComponentsInChildren<Renderer>(true));
            Vector3 supportPalm = RequireUnique(rig.Hands, "Hand_2.L").position;
            row.SupportPalmDistance = Vector3.Distance(
                supportPalm,
                recorderWorldBounds.ClosestPoint(supportPalm));
            row.CassetteWorldPosition = rig.Cassette.transform.position;
            row.CassetteWorldRotation = rig.Cassette.transform.rotation;
            row.RecorderWorldPosition = rig.Recorder.transform.position;
            row.RecorderWorldRotation = rig.Recorder.transform.rotation;
            row.CassetteOwner = rig.Cassette.transform.parent == rig.CassetteSlot
                ? "CassetteSlot"
                : rig.Cassette.transform.parent == rig.CassetteGrip
                    ? "CassetteGrip"
                    : rig.Cassette.transform.parent == null
                        ? "<none>"
                        : rig.Cassette.transform.parent.name;
            row.CassetteSlotLocalOffset = rig.CassetteSlot.InverseTransformPoint(
                rig.Cassette.transform.position);
            Bounds cassetteWorldBounds = WorldRendererBounds(
                rig.Cassette.GetComponentsInChildren<Renderer>(true));
            Vector3 cassetteToCamera = (
                camera.transform.position - cassetteWorldBounds.center).normalized;
            // The packaged cassette contract is X=width, Y=thickness/front normal,
            // Z=height. The +Y face carries the readable broad-face artwork.
            row.CassetteFrontFacing = Mathf.Abs(Vector3.Dot(
                rig.Cassette.transform.TransformDirection(Vector3.up),
                cassetteToCamera));
            row.CassetteRecorderOverlapRatio = RectOverlapRatio(
                row.CassetteViewport,
                row.RecorderViewport);
            row.CassetteOrientationErrorDegrees = Quaternion.Angle(
                rig.Cassette.transform.rotation,
                rig.CassetteSlot.rotation);
            Transform contactMarker = RequireUnique(rig.Hands, "CassetteContact");
            row.CassetteHandContactError = Vector3.Distance(
                contactMarker.position,
                cassetteWorldBounds.ClosestPoint(contactMarker.position));
            row.CassetteThumbContactError = DistanceToBounds(
                RequireUnique(rig.Hands, "Finger_1_4.R").position,
                cassetteWorldBounds);
            row.CassetteIndexContactError = DistanceToBounds(
                RequireUnique(rig.Hands, "Finger_2_3.R").position,
                cassetteWorldBounds);
            row.CassetteMiddleContactError = DistanceToBounds(
                RequireUnique(rig.Hands, "Finger_3_3.R").position,
                cassetteWorldBounds);
            row.CameraInsideHands = row.Hands.size.sqrMagnitude > 0f &&
                ContainsCameraOrigin(row.Hands);
            if (state.Name == "10-cassette-contact")
            {
                row.CassetteContactError = Vector3.Distance(
                    rig.Cassette.transform.position,
                    rig.CassetteSlot.position);
                row.CassetteBoundsContactError = Vector3.Distance(
                    cassetteWorldBounds.center,
                    rig.CassetteSlot.position);
                row.CassetteAttachedToInteractionHand =
                    rig.Cassette.transform.parent == rig.CassetteGrip;
                MeasureOwnershipTransferSnap(
                    rig,
                    out row.OwnershipTransferPositionSnap,
                    out row.OwnershipTransferRotationSnapDegrees);
            }
            if (row.CameraInsideHands)
            {
                row.Issues.Add("camera origin intersects hands bounds");
            }
            if (row.Hands.center.z <= 0f && state.RequireReadable)
            {
                row.Issues.Add("hands renderer center is behind camera");
            }
            // Do not use raw renderer bounds as a screen-coverage proxy. The
            // intentionally off-screen FPS sleeve continuations make those
            // bounds much larger than the pixels actually rendered. The
            // FOV-70 composition gate evaluates rendered-pixel coverage after
            // each frame instead.
            if (state.RequireReadable && row.Recorder.center.z <= camera.nearClipPlane)
            {
                row.Issues.Add("recorder is behind/intersecting the near plane");
            }
            if (state.RequireReadable &&
                (row.Recorder.size.x < 0.04f || row.Recorder.size.y < 0.07f ||
                 row.Recorder.size.x > 0.30f || row.Recorder.size.y > 0.35f))
            {
                row.Issues.Add("recorder camera-local dimensions are implausible");
            }
            row.Passed = row.Issues.Count == 0;
            return row;
        }

        private static float ScreenAngleFromVertical(Vector3 from, Vector3 to)
        {
            Vector2 delta = new Vector2(to.x - from.x, to.y - from.y);
            return Mathf.Atan2(Mathf.Abs(delta.x), Mathf.Abs(delta.y)) * Mathf.Rad2Deg;
        }

        private static float DistanceToBounds(Vector3 point, Bounds bounds)
        {
            return Vector3.Distance(point, bounds.ClosestPoint(point));
        }

        private static void MeasureOwnershipTransferSnap(
            PreviewRig rig,
            out float positionSnap,
            out float rotationSnapDegrees)
        {
            Transform cassette = rig.Cassette.transform;
            Transform originalParent = cassette.parent;
            Vector3 originalLocalPosition = cassette.localPosition;
            Quaternion originalLocalRotation = cassette.localRotation;
            Vector3 originalLocalScale = cassette.localScale;
            Vector3 worldPosition = cassette.position;
            Quaternion worldRotation = cassette.rotation;
            try
            {
                cassette.SetParent(rig.CassetteSlot, true);
                positionSnap = Vector3.Distance(worldPosition, cassette.position);
                rotationSnapDegrees = Quaternion.Angle(worldRotation, cassette.rotation);
            }
            finally
            {
                cassette.SetParent(originalParent, false);
                cassette.localPosition = originalLocalPosition;
                cassette.localRotation = originalLocalRotation;
                cassette.localScale = originalLocalScale;
            }
        }

        private static void WriteCassetteContactReport(
            string output,
            PreviewRig rig,
            Camera camera)
        {
            StateSpec contact = States().Single(value =>
                value.Name == "10-cassette-contact");
            ApplyState(rig, contact);
            Bounds visible = WorldRendererBounds(
                rig.Cassette.GetComponentsInChildren<Renderer>(true));
            Transform contactMarker = RequireUnique(rig.Hands, "CassetteContact");
            Transform alignment = RequireUnique(rig.Recorder, "CassetteAlignment");
            Transform handsRig = RequireUnique(rig.Hands, "SoulRecorderHandsRig");
            StringBuilder report = new StringBuilder();
            report.AppendLine("SoulRecorder cassette contact audit");
            report.AppendLine("All positions are presentation-local unless identified otherwise.");
            AppendTransform(report, rig.PresentationRoot.transform, rig.RecorderGrip);
            AppendTransform(report, rig.PresentationRoot.transform, rig.CassetteGrip);
            AppendTransform(report, rig.PresentationRoot.transform, contactMarker);
            foreach (string boneName in new[]
            {
                "Arm_1.L", "Arm_2.L", "Hand_1.L", "Hand_2.L",
                "Arm_1.R", "Arm_2.R", "Hand_1.R", "Hand_2.R",
                "SupportSleeveCutoff", "CassetteSleeveCutoff",
                "Finger_2_1.L", "Finger_2_3.L",
                "Finger_2_1.R", "Finger_2_3.R"
            })
            {
                AppendTransform(
                    report,
                    rig.PresentationRoot.transform,
                    RequireUnique(rig.Hands, boneName));
                Vector3 viewport = camera.WorldToViewportPoint(
                    RequireUnique(rig.Hands, boneName).position);
                report.AppendLine("  viewport=" + Format(viewport));
            }
            AppendTransform(report, rig.PresentationRoot.transform, rig.Cassette.transform);
            AppendTransform(report, rig.PresentationRoot.transform, alignment);
            AppendTransform(report, rig.PresentationRoot.transform, rig.CassetteSlot);
            report.AppendLine("CassetteGrip in HandsRig local: position=" +
                Format(handsRig.InverseTransformPoint(rig.CassetteGrip.position)) +
                ", rotation=" +
                Format(Quaternion.Inverse(handsRig.rotation) * rig.CassetteGrip.rotation));
            report.AppendLine("CassetteSlot in HandsRig local: position=" +
                Format(handsRig.InverseTransformPoint(rig.CassetteSlot.position)) +
                ", rotation=" +
                Format(Quaternion.Inverse(handsRig.rotation) * rig.CassetteSlot.rotation));
            report.AppendLine("cassette visible bounds center=" +
                Format(rig.PresentationRoot.transform.InverseTransformPoint(visible.center)) +
                ", size=" + Format(visible.size / RuntimePresentationScale.x));
            report.AppendLine("cassette origin-to-slot error=" +
                Vector3.Distance(rig.Cassette.transform.position, rig.CassetteSlot.position)
                    .ToString("F6", CultureInfo.InvariantCulture) + "m");
            report.AppendLine("cassette bounds-center-to-slot error=" +
                Vector3.Distance(visible.center, rig.CassetteSlot.position)
                    .ToString("F6", CultureInfo.InvariantCulture) + "m");
            report.AppendLine("cassette-to-slot orientation error=" +
                Quaternion.Angle(rig.Cassette.transform.rotation, rig.CassetteSlot.rotation)
                    .ToString("F4", CultureInfo.InvariantCulture) + " degrees");
            report.AppendLine("required CassetteGrip local correction for slot orientation=" +
                Format(Quaternion.Inverse(rig.CassetteGrip.rotation) *
                    rig.CassetteSlot.rotation));
            report.AppendLine("CassetteContact-to-cassette-center=" +
                Vector3.Distance(contactMarker.position, visible.center)
                    .ToString("F6", CultureInfo.InvariantCulture) + "m");
            report.AppendLine("CassetteContact-to-cassette-body=" +
                Vector3.Distance(
                    contactMarker.position,
                    visible.ClosestPoint(contactMarker.position))
                    .ToString("F6", CultureInfo.InvariantCulture) + "m");
            foreach (string fingertipName in new[]
            {
                "Finger_1_4.R", "Finger_2_3.R", "Finger_3_3.R",
                "Finger_4_3.R", "Finger_5_3.R"
            })
            {
                Transform fingertip = RequireUnique(rig.Hands, fingertipName);
                report.AppendLine(fingertipName + "-to-cassette-body=" +
                    Vector3.Distance(
                        fingertip.position,
                        visible.ClosestPoint(fingertip.position))
                        .ToString("F6", CultureInfo.InvariantCulture) + "m");
            }
            float transferPosition;
            float transferRotation;
            MeasureOwnershipTransferSnap(
                rig,
                out transferPosition,
                out transferRotation);
            report.AppendLine("preserve-world transfer position snap=" +
                transferPosition.ToString("F8", CultureInfo.InvariantCulture) + "m");
            report.AppendLine("preserve-world transfer rotation snap=" +
                transferRotation.ToString("F6", CultureInfo.InvariantCulture) + " degrees");
            File.WriteAllText(
                Path.Combine(output, "cassette-contact-report.txt"),
                report.ToString());
        }

        private static string[] CassetteManipulationStateNames()
        {
            return new[]
            {
                "04-insert-carry",
                "05-insert-approach",
                "06-insert-alignment",
                "07-insert-first-contact",
                "08-insert-half-push",
                "09-insert-push-end",
                "10-cassette-contact",
                "11-cassette-seated",
                "12-post-transfer-release",
                "15-eject-approach",
                "16-eject-fingers-closed",
                "17-eject-slot-hold",
                "18-eject-transfer-pre",
                "19-eject-transfer-post",
                "20-eject-clear",
                "21-eject-wrist-turn",
                "22-eject-end"
            };
        }

        private static void WriteCassetteChoreographyReport(
            string output,
            List<StateMetrics> metrics,
            CassetteGripPathMetrics gripPaths)
        {
            Func<string, StateMetrics> at70 = name => metrics.Single(value =>
                value.Fov == 70f && value.Name == name);
            StateMetrics carry = at70("04-insert-carry");
            StateMetrics approach = at70("05-insert-approach");
            StateMetrics alignment = at70("06-insert-alignment");
            StateMetrics firstContact = at70("07-insert-first-contact");
            StateMetrics halfPush = at70("08-insert-half-push");
            StateMetrics pushEnd = at70("09-insert-push-end");
            StateMetrics contact = at70("10-cassette-contact");
            StateMetrics release = at70("12-post-transfer-release");
            StateMetrics ejectGrip = at70("16-eject-fingers-closed");
            StateMetrics ejectPre = at70("18-eject-transfer-pre");
            StateMetrics ejectPost = at70("19-eject-transfer-post");
            StateMetrics ejectClear = at70("20-eject-clear");
            StateMetrics ejectEnd = at70("22-eject-end");
            float pushTravel = Vector3.Distance(
                firstContact.CassetteWorldPosition,
                contact.CassetteWorldPosition);
            float seatTravel = Vector3.Distance(
                pushEnd.CassetteWorldPosition,
                contact.CassetteWorldPosition);
            float recorderOwnedDrift = Vector3.Distance(
                ejectGrip.CassetteSlotLocalOffset,
                ejectPre.CassetteSlotLocalOffset);
            float ejectionSampledPath = Vector3.Distance(
                ejectPost.CassetteWorldPosition,
                ejectEnd.CassetteWorldPosition);
            float transferSnap = Vector3.Distance(
                ejectPre.CassetteWorldPosition,
                ejectPost.CassetteWorldPosition);
            float transferRotationSnap = Quaternion.Angle(
                ejectPre.CassetteWorldRotation,
                ejectPost.CassetteWorldRotation);
            float recorderResponse = Vector3.Distance(
                firstContact.RecorderWorldPosition,
                pushEnd.RecorderWorldPosition);
            float recorderResponseDegrees = Quaternion.Angle(
                firstContact.RecorderWorldRotation,
                pushEnd.RecorderWorldRotation);
            List<string> issues = metrics.SelectMany(value => value.Issues)
                .Where(value => value.StartsWith(
                    "CASSETTE CHOREOGRAPHY — ",
                    StringComparison.Ordinal))
                .Distinct()
                .ToList();
            StringBuilder report = new StringBuilder();
            report.AppendLine("SoulRecorder cassette choreography audit (60 fps)");
            report.AppendLine("RESULT=" + (issues.Count == 0 ? "PASS" : "FAIL"));
            report.AppendLine("Insertion clip: carry=0.2333s, approach=0.4000s, " +
                "alignment=0.5167s, firstContact=0.6000s, halfPush=0.6667s, " +
                "pushEnd=0.7000s, seat/transfer=0.7667s");
            report.AppendLine("Alignment dwell=0.0833s (5 frames)");
            report.AppendLine("Seat/click settle=0.0667s (4 frames)");
            report.AppendLine("Insertion ownership transfer: master frame 72, " +
                "CassetteGrip -> CassetteSlot, preserve world pose");
            report.AppendLine("Insert camera-local CassetteGrip maximum displacement " +
                "before final push=" + gripPaths.InsertBeforeFinalPush.MaximumDisplacement
                    .ToString("F6", CultureInfo.InvariantCulture) + "m");
            report.AppendLine("Insert camera-local CassetteGrip path length before " +
                "final push=" + gripPaths.InsertBeforeFinalPush.PathLength
                    .ToString("F6", CultureInfo.InvariantCulture) + "m");
            report.AppendLine("Insert camera-local CassetteGrip full-clip path length=" +
                gripPaths.InsertFullClip.PathLength.ToString(
                    "F6", CultureInfo.InvariantCulture) + "m");
            report.AppendLine("Insertion curved-path deviation=" +
                DistanceFromLineSegment(
                    approach.CassetteWorldPosition,
                    carry.CassetteWorldPosition,
                    alignment.CassetteWorldPosition)
                    .ToString("F6", CultureInfo.InvariantCulture) + "m");
            report.AppendLine("Insertion push travel=" +
                pushTravel.ToString("F6", CultureInfo.InvariantCulture) + "m");
            report.AppendLine("Insertion half-push travel=" +
                Vector3.Distance(
                    firstContact.CassetteWorldPosition,
                    halfPush.CassetteWorldPosition)
                    .ToString("F6", CultureInfo.InvariantCulture) + "m");
            report.AppendLine("Insertion seat finish travel=" +
                seatTravel.ToString("F6", CultureInfo.InvariantCulture) + "m");
            report.AppendLine("Insertion orientation alignment/contact/seat=" +
                alignment.CassetteOrientationErrorDegrees.ToString(
                    "F4", CultureInfo.InvariantCulture) + "/" +
                firstContact.CassetteOrientationErrorDegrees.ToString(
                    "F4", CultureInfo.InvariantCulture) + "/" +
                contact.CassetteOrientationErrorDegrees.ToString(
                    "F4", CultureInfo.InvariantCulture) + " degrees");
            report.AppendLine("Insertion contact thumb/index/middle=" +
                contact.CassetteThumbContactError.ToString(
                    "F6", CultureInfo.InvariantCulture) + "/" +
                contact.CassetteIndexContactError.ToString(
                    "F6", CultureInfo.InvariantCulture) + "/" +
                contact.CassetteMiddleContactError.ToString(
                    "F6", CultureInfo.InvariantCulture) + "m");
            report.AppendLine("Post-transfer release thumb/index/middle=" +
                release.CassetteThumbContactError.ToString(
                    "F6", CultureInfo.InvariantCulture) + "/" +
                release.CassetteIndexContactError.ToString(
                    "F6", CultureInfo.InvariantCulture) + "/" +
                release.CassetteMiddleContactError.ToString(
                    "F6", CultureInfo.InvariantCulture) + "m");
            report.AppendLine("Recorder response=" +
                recorderResponse.ToString("F6", CultureInfo.InvariantCulture) + "m / " +
                recorderResponseDegrees.ToString("F4", CultureInfo.InvariantCulture) +
                " degrees");
            report.AppendLine("Eject clip: approach=0.1167s, alignment=0.2167s, " +
                "gripClosed=0.3167s, slotHold=0.3500s, transfer=0.3833s, " +
                "clear=0.4833s, wristTurn=0.5833s, removed=0.6833s");
            report.AppendLine("Ejection ownership transfer: master frame 157, " +
                "CassetteSlot -> CassetteGrip, preserve world pose");
            report.AppendLine("Eject camera-local CassetteGrip maximum displacement=" +
                gripPaths.EjectFullClip.MaximumDisplacement.ToString(
                    "F6", CultureInfo.InvariantCulture) + "m");
            report.AppendLine("Eject camera-local CassetteGrip full-clip path length=" +
                gripPaths.EjectFullClip.PathLength.ToString(
                    "F6", CultureInfo.InvariantCulture) + "m");
            report.AppendLine("Ejection recorder-owned slot drift=" +
                recorderOwnedDrift.ToString("F8", CultureInfo.InvariantCulture) + "m");
            report.AppendLine("Ejection sampled CassetteGrip path after transfer=" +
                ejectionSampledPath.ToString("F6", CultureInfo.InvariantCulture) + "m");
            report.AppendLine("Ejection transfer snap=" +
                transferSnap.ToString("F8", CultureInfo.InvariantCulture) + "m / " +
                transferRotationSnap.ToString("F6", CultureInfo.InvariantCulture) +
                " degrees");
            report.AppendLine("Ejection owners before/after/clear=" +
                ejectPre.CassetteOwner + "/" + ejectPost.CassetteOwner + "/" +
                ejectClear.CassetteOwner);
            foreach (string issue in issues)
            {
                report.AppendLine("- " + issue);
            }
            File.WriteAllText(
                Path.Combine(output, "cassette-choreography-report.txt"),
                report.ToString());
        }

        private static void WriteScreenSpaceCompositionReport(
            string output,
            List<StateMetrics> metrics)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("SoulRecorder FOV-70 screen-space composition");
            report.AppendLine("Cassette local orientation contract:");
            report.AppendLine("  broad readable front normal = local +Y");
            report.AppendLine("  top direction = local +Z");
            report.AppendLine("  insertion edge direction = local -Z");
            report.AppendLine("Viewport rectangles are x,y,width,height; coverage is width/height/area.");
            foreach (string name in new[]
            {
                "04-insert-carry", "05-insert-approach", "06-insert-alignment",
                "07-insert-first-contact",
                "11-cassette-seated", "20-eject-clear"
            })
            {
                StateMetrics state = metrics.Single(value =>
                    value.Fov == 70f && value.Name == name);
                report.AppendLine();
                report.AppendLine(name);
                AppendViewportCoverage(
                    report, "full presentation rendered pixels", state.VisiblePresentationViewport);
                report.AppendLine("  raw renderer projection=" +
                    Format(state.FullPresentationViewport));
                AppendViewportCoverage(report, "recorder", state.RecorderViewport);
                AppendViewportCoverage(report, "cassette", state.CassetteViewport);
                report.AppendLine("  cassette camera-local rotation=" +
                    Format(state.CassetteWorldRotation.eulerAngles));
                report.AppendLine("  cassette broad-face score=" +
                    state.CassetteFrontFacing.ToString("F4", CultureInfo.InvariantCulture));
                report.AppendLine("  cassette/recorder overlap=" +
                    (state.CassetteRecorderOverlapRatio * 100f).ToString(
                        "F2", CultureInfo.InvariantCulture) + "%");
            }
            File.WriteAllText(
                Path.Combine(output, "screen-space-composition-report.txt"),
                report.ToString());
        }

        private static void AppendViewportCoverage(
            StringBuilder report,
            string label,
            Rect viewport)
        {
            Rect visible = ClampedViewportRect(viewport);
            report.AppendLine("  " + label + " rect=" + Format(viewport) +
                ", visible width=" +
                (visible.width * 100f).ToString("F2", CultureInfo.InvariantCulture) +
                "%, height=" +
                (visible.height * 100f).ToString("F2", CultureInfo.InvariantCulture) +
                "%, area=" +
                (visible.width * visible.height * 100f).ToString(
                    "F2", CultureInfo.InvariantCulture) + "%");
        }

        private static void CopyOrientationEvidence(string output)
        {
            string fov70 = Path.Combine(output, "FOV-70");
            foreach (KeyValuePair<string, string> evidence in
                new Dictionary<string, string>
                {
                    { "04-insert-carry", "orientation-carry-fov70.png" },
                    { "06-insert-alignment", "orientation-alignment-fov70.png" },
                    { "07-insert-first-contact", "orientation-contact-fov70.png" },
                    { "11-cassette-seated", "orientation-seated-fov70.png" }
                })
            {
                File.Copy(
                    Path.Combine(fov70, evidence.Key + ".png"),
                    Path.Combine(output, evidence.Value),
                    true);
            }
        }

        private static void AppendTransform(
            StringBuilder report,
            Transform reference,
            Transform value)
        {
            report.AppendLine(value.name + ": position=" +
                Format(reference.InverseTransformPoint(value.position)) +
                ", rotation=" +
                Format(Quaternion.Inverse(reference.rotation) * value.rotation) +
                ", localScale=" + Format(value.localScale));
        }

        private static List<string> ValidateTransformChain(PreviewRig rig, string output)
        {
            StringBuilder report = new StringBuilder();
            List<string> issues = new List<string>();
            report.AppendLine("SoulRecorder packaged transform chain:");
            Transform[] chain =
            {
                rig.PresentationRoot.transform,
                rig.Hands.transform,
                RequireUnique(rig.Hands, "SoulRecorderAnimatedHandsModel"),
                RequireUnique(rig.Hands, "SoulRecorderArms"),
                RequireUnique(rig.Hands, "SoulRecorderHandsRig"),
                RequireUnique(rig.Hands, "SoulRecorderHandsRoot"),
                RequireUnique(rig.Hands, "Arm_1.L"),
                RequireUnique(rig.Hands, "Arm_2.L"),
                RequireUnique(rig.Hands, "Hand_1.L"),
                RequireUnique(rig.Hands, "Hand_2.L")
            };
            foreach (Transform value in chain)
            {
                report.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: localPosition={1}, localRotation={2}, localScale={3}, lossyScale={4}",
                    value.name,
                    Format(value.localPosition),
                    Format(value.localRotation.eulerAngles),
                    Format(value.localScale),
                    Format(value.lossyScale)));
                Vector3 scale = value.localScale;
                float minimum = Mathf.Min(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                float maximum = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                if (minimum <= 0f || maximum / minimum > 1.02f || maximum > 4f || minimum < 0.25f)
                {
                    issues.Add("unsafe transform scale at " + value.name + ": " + Format(scale));
                }
            }
            AnimationClip hold = rig.Animator.runtimeAnimatorController.animationClips.Single(
                value => string.Equals(value.name, "SoulRecorder_Hold", StringComparison.Ordinal));
            hold.SampleAnimation(rig.Animator.gameObject, 0f);
            Quaternion desiredWorld = rig.PresentationRoot.transform.rotation;
            Quaternion recorderCorrection = Quaternion.Inverse(rig.RecorderGrip.rotation) * desiredWorld;
            Quaternion cassetteCorrection = Quaternion.Inverse(rig.CassetteGrip.rotation) * desiredWorld;
            report.AppendLine("Hold-pose recorder local correction for camera-facing root: " +
                Format(recorderCorrection.eulerAngles));
            report.AppendLine("Hold-pose cassette local correction for camera-facing root: " +
                Format(cassetteCorrection.eulerAngles));
            report.AppendLine("Hold-pose cassette local correction rotated 90 degrees about local X: " +
                Format((cassetteCorrection * Quaternion.Euler(90f, 0f, 0f)).eulerAngles));
            File.WriteAllText(Path.Combine(output, "transform-chain.txt"), report.ToString());
            Debug.Log(report.ToString());
            return issues;
        }

        private static List<string> ValidateRuntimeHandMaterials(
            PreviewRig rig,
            string output)
        {
            const string requiredShader = "SoulPlayer/Recorder Hands PBR";
            List<string> issues = new List<string>();
            StringBuilder report = new StringBuilder();
            SkinnedMeshRenderer[] renderers =
                rig.Hands.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length != 1)
            {
                issues.Add("preview hands do not contain exactly one skinned renderer");
            }
            else
            {
                Material[] materials = renderers[0].sharedMaterials;
                if (materials.Length != 2)
                {
                    issues.Add("preview hands do not contain exactly two material slots");
                }
                foreach (Material material in materials)
                {
                    string name = material == null ? "<null>" : material.name;
                    string shader = material == null || material.shader == null
                        ? "<null>"
                        : material.shader.name;
                    Texture albedo = material == null ? null : material.GetTexture("_MainTex");
                    Texture normal = material == null ? null : material.GetTexture("_BumpMap");
                    Texture pbr = material == null
                        ? null
                        : material.GetTexture("_MetallicGlossMap");
                    Color color = material != null && material.HasProperty("_Color")
                        ? material.GetColor("_Color")
                        : Color.clear;
                    report.AppendLine(name + ": shader=" + shader +
                        ", albedo=" + (albedo == null ? "<null>" : albedo.name) +
                        ", normal=" + (normal == null ? "<null>" : normal.name) +
                        ", metallicSmoothness=" +
                        (pbr == null ? "<null>" : pbr.name) +
                        ", color=" + string.Format(
                            CultureInfo.InvariantCulture,
                            "({0:F3},{1:F3},{2:F3},{3:F3})",
                            color.r, color.g, color.b, color.a));
                    if (!string.Equals(shader, requiredShader, StringComparison.Ordinal) ||
                        albedo == null || normal == null || pbr == null)
                    {
                        issues.Add(name +
                            " does not use the packaged SoulPlayer PBR texture contract");
                    }
                }
            }
            File.WriteAllText(Path.Combine(output, "material-report.txt"), report.ToString());
            return issues;
        }

        private static List<string> ValidateAnimationTranslationCurves(
            Animator animator,
            string output)
        {
            List<string> issues = new List<string>();
            StringBuilder audit = new StringBuilder();
            foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
            {
                audit.AppendLine(clip.name + " length=" +
                    clip.length.ToString("F6", CultureInfo.InvariantCulture));
                foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                {
                    AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve == null)
                    {
                        continue;
                    }
                    float minimum = curve.keys.Length == 0
                        ? 0f
                        : curve.keys.Min(key => key.value);
                    float maximum = curve.keys.Length == 0
                        ? 0f
                        : curve.keys.Max(key => key.value);
                    float excursion = maximum - minimum;
                    if (binding.path.IndexOf("Arm_", StringComparison.Ordinal) >= 0 ||
                        binding.path.IndexOf("HandsRoot", StringComparison.Ordinal) >= 0)
                    {
                        audit.AppendLine(string.Format(
                            CultureInfo.InvariantCulture,
                            "  {0} {1}: keys={2}, time={3:F4}..{4:F4}, value={5:F5}..{6:F5}",
                            binding.path,
                            binding.propertyName,
                            curve.keys.Length,
                            curve.keys.Length == 0 ? 0f : curve.keys.Min(key => key.time),
                            curve.keys.Length == 0 ? 0f : curve.keys.Max(key => key.time),
                            minimum,
                            maximum));
                    }
                    if (binding.propertyName.StartsWith(
                            "m_LocalPosition.", StringComparison.Ordinal) &&
                        excursion > 0.75f)
                    {
                        issues.Add(
                            clip.name + " drives " + binding.path + "/" +
                            binding.propertyName + " through " + excursion.ToString("F4") +
                            "m; meter-scale animation translation is rejected.");
                    }
                }
            }
            File.WriteAllText(Path.Combine(output, "animation-curves.txt"), audit.ToString());
            return issues;
        }

        private static Bounds CameraLocalBounds(Camera camera, Renderer[] renderers)
        {
            bool hasBounds = false;
            Bounds result = new Bounds();
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }
                foreach (Vector3 corner in Corners(renderer.bounds))
                {
                    Vector3 local = camera.transform.InverseTransformPoint(corner);
                    if (!hasBounds)
                    {
                        result = new Bounds(local, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        result.Encapsulate(local);
                    }
                }
            }
            return result;
        }

        private static Bounds WorldRendererBounds(Renderer[] renderers)
        {
            bool hasBounds = false;
            Bounds result = new Bounds();
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }
                if (!hasBounds)
                {
                    result = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    result.Encapsulate(renderer.bounds);
                }
            }
            return result;
        }

        private static Rect ViewportRect(Camera camera, Renderer[] renderers)
        {
            float minimumX = float.PositiveInfinity;
            float minimumY = float.PositiveInfinity;
            float maximumX = float.NegativeInfinity;
            float maximumY = float.NegativeInfinity;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }
                foreach (Vector3 corner in Corners(renderer.bounds))
                {
                    Vector3 point = camera.WorldToViewportPoint(corner);
                    if (point.z <= 0f)
                    {
                        continue;
                    }
                    minimumX = Mathf.Min(minimumX, point.x);
                    minimumY = Mathf.Min(minimumY, point.y);
                    maximumX = Mathf.Max(maximumX, point.x);
                    maximumY = Mathf.Max(maximumY, point.y);
                }
            }
            if (float.IsInfinity(minimumX))
            {
                return new Rect();
            }
            return Rect.MinMaxRect(minimumX, minimumY, maximumX, maximumY);
        }

        private static IEnumerable<Vector3> Corners(Bounds bounds)
        {
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        yield return center + Vector3.Scale(
                            extents,
                            new Vector3(x, y, z));
                    }
                }
            }
        }

        private static bool ContainsCameraOrigin(Bounds cameraLocalBounds)
        {
            Vector3 minimum = cameraLocalBounds.min;
            Vector3 maximum = cameraLocalBounds.max;
            return minimum.x <= 0f && maximum.x >= 0f &&
                   minimum.y <= 0f && maximum.y >= 0f &&
                   minimum.z <= 0f && maximum.z >= 0f;
        }

        private static void RenderSequentialPreview(
            Camera camera,
            PreviewRig rig,
            string output,
            string folderName,
            string sheetName,
            string[] clips)
        {
            const int samplesPerClip = 21;
            string folder = Path.Combine(output, folderName);
            Directory.CreateDirectory(folder);
            List<string> frameNames = new List<string>();
            int sequence = 0;
            foreach (string clip in clips)
            {
                for (int sample = 0; sample < samplesPerClip; sample++)
                {
                    float normalized = sample / (float)(samplesPerClip - 1);
                    bool onSlot = clip == "SoulRecorder_Hold" ||
                                  clip == "SoulRecorder_StartExit" ||
                                  clip == "SoulRecorder_StopEnter" ||
                                  (clip == "SoulRecorder_Eject" &&
                                   normalized <= EjectTransferNormalized);
                    bool ejectionAfterTransfer = clip == "SoulRecorder_Eject" &&
                        normalized > EjectTransferNormalized;
                    bool cassetteVisible = !(clip == "SoulRecorder_Enter" && sample == 0);
                    string name = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:D2}-{1}-{2:D2}",
                        ++sequence,
                        clip.Replace("SoulRecorder_", string.Empty).ToLowerInvariant(),
                        sample);
                    StateSpec state = State(
                        name,
                        clip,
                        normalized,
                        onSlot,
                        cassetteVisible,
                        false,
                        ejectionAfterTransfer);
                    ApplyState(rig, state);
                    RenderPng(camera, Path.Combine(folder, name + ".png"));
                    frameNames.Add(name);
                }
            }
            CreateContactSheet(
                folder,
                Path.Combine(output, sheetName),
                frameNames.ToArray());
        }

        private static void CreateBaselineComparisons(
            string baseline,
            string output,
            List<string> structuralIssues)
        {
            if (string.IsNullOrWhiteSpace(baseline))
            {
                return;
            }
            baseline = Path.GetFullPath(baseline);
            foreach (string name in new[]
            {
                "cassette-manipulation-fov70.png",
                "sequential-start-fov70.png",
                "sequential-stop-fov70.png"
            })
            {
                string oldPath = Path.Combine(baseline, name);
                string newPath = Path.Combine(output, name);
                if (!File.Exists(oldPath) || !File.Exists(newPath))
                {
                    structuralIssues.Add(
                        "Old/new preview comparison source was missing: " + name);
                    continue;
                }
                CreateSideBySideImage(
                    oldPath,
                    newPath,
                    Path.Combine(output, "old-vs-new-" + name));
            }
        }

        private static void CreateSideBySideImage(
            string oldPath,
            string newPath,
            string output)
        {
            Texture2D oldImage = new Texture2D(2, 2, TextureFormat.RGB24, false);
            Texture2D newImage = new Texture2D(2, 2, TextureFormat.RGB24, false);
            Texture2D comparison = null;
            try
            {
                oldImage.LoadImage(File.ReadAllBytes(oldPath));
                newImage.LoadImage(File.ReadAllBytes(newPath));
                if (oldImage.height != newImage.height)
                {
                    throw new InvalidOperationException(
                        "Old/new preview heights differ for " + Path.GetFileName(newPath));
                }
                comparison = new Texture2D(
                    oldImage.width + newImage.width,
                    oldImage.height,
                    TextureFormat.RGB24,
                    false);
                comparison.SetPixels32(
                    0,
                    0,
                    oldImage.width,
                    oldImage.height,
                    oldImage.GetPixels32());
                comparison.SetPixels32(
                    oldImage.width,
                    0,
                    newImage.width,
                    newImage.height,
                    newImage.GetPixels32());
                comparison.Apply();
                File.WriteAllBytes(output, comparison.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(oldImage);
                UnityEngine.Object.DestroyImmediate(newImage);
                if (comparison != null)
                {
                    UnityEngine.Object.DestroyImmediate(comparison);
                }
            }
        }

        private static void RenderCassetteCloseUps(
            Camera camera,
            PreviewRig rig,
            string output,
            string[] stateNames)
        {
            const int cropWidth = 960;
            const int cropHeight = 540;
            string folder = Path.Combine(output, "Cassette-Closeups");
            Directory.CreateDirectory(folder);
            foreach (string name in stateNames)
            {
                StateSpec state = States().Single(value => value.Name == name);
                ApplyState(rig, state);
                Vector3 viewport = camera.WorldToViewportPoint(
                    WorldRendererBounds(
                        rig.Cassette.GetComponentsInChildren<Renderer>(true)).center);
                int cropX = Mathf.Clamp(
                    Mathf.RoundToInt(viewport.x * 1920f) - (cropWidth / 2),
                    0,
                    1920 - cropWidth);
                int cropY = Mathf.Clamp(
                    Mathf.RoundToInt(viewport.y * 1080f) - (cropHeight / 2),
                    0,
                    1080 - cropHeight);
                byte[] sourceBytes = File.ReadAllBytes(Path.Combine(
                    output,
                    "FOV-70",
                    name + ".png"));
                Texture2D source = new Texture2D(2, 2, TextureFormat.RGB24, false);
                Texture2D crop = new Texture2D(
                    cropWidth,
                    cropHeight,
                    TextureFormat.RGB24,
                    false);
                try
                {
                    source.LoadImage(sourceBytes);
                    crop.SetPixels(source.GetPixels(cropX, cropY, cropWidth, cropHeight));
                    crop.Apply();
                    File.WriteAllBytes(
                        Path.Combine(folder, name + ".png"),
                        crop.EncodeToPNG());
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(source);
                    UnityEngine.Object.DestroyImmediate(crop);
                }
            }
            CreateContactSheet(
                folder,
                Path.Combine(output, "cassette-manipulation-closeups-fov70.png"),
                stateNames);
        }

        private static void RenderPng(Camera camera, string path)
        {
            RenderTexture target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            target.antiAliasing = 4;
            Texture2D image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0f, 0f, 1920f, 1080f), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(image);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static Rect MeasureRenderedViewport(Camera camera)
        {
            const int width = 960;
            const int height = 540;
            RenderTexture target = new RenderTexture(
                width, height, 24, RenderTextureFormat.ARGB32);
            Texture2D image = new Texture2D(width, height, TextureFormat.RGBA32, false);
            RenderTexture previous = RenderTexture.active;
            Color previousBackground = camera.backgroundColor;
            try
            {
                camera.backgroundColor = new Color(
                    previousBackground.r,
                    previousBackground.g,
                    previousBackground.b,
                    0f);
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                image.Apply();
                Color32[] pixels = image.GetPixels32();
                int minX = width;
                int minY = height;
                int maxX = -1;
                int maxY = -1;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        if (pixels[(y * width) + x].a <= 16)
                        {
                            continue;
                        }
                        minX = Math.Min(minX, x);
                        minY = Math.Min(minY, y);
                        maxX = Math.Max(maxX, x);
                        maxY = Math.Max(maxY, y);
                    }
                }
                if (maxX < minX || maxY < minY)
                {
                    return Rect.zero;
                }
                return Rect.MinMaxRect(
                    minX / (float)width,
                    minY / (float)height,
                    (maxX + 1) / (float)width,
                    (maxY + 1) / (float)height);
            }
            finally
            {
                camera.targetTexture = null;
                camera.backgroundColor = previousBackground;
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(image);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static void RenderStaticGripReview(
            Camera camera,
            PreviewRig rig,
            string output,
            List<string> structuralIssues)
        {
            StateSpec carry = States().Single(value => value.Name == "04-insert-carry");
            ApplyState(rig, carry);
            StaticGripMetrics metrics = ApplyMarkerSolvedStaticGrip(
                rig,
                StaticSupportWristOffset,
                StaticCassetteWristOffset);
            camera.fieldOfView = 70f;
            RenderPng(camera, Path.Combine(output, "static-grip-review-fov70.png"));

            List<GameObject> gizmos = CreateContactRigGizmos(rig);
            try
            {
                RenderPng(camera, Path.Combine(output, "static-grip-gizmos-fov70.png"));
            }
            finally
            {
                foreach (GameObject gizmo in gizmos)
                {
                    UnityEngine.Object.DestroyImmediate(gizmo);
                }
            }

            StringBuilder report = new StringBuilder();
            report.AppendLine("SoulRecorder marker-solved static grip audit");
            report.AppendLine("Pose=SupportHold + CassetteCarry");
            report.AppendLine("Recorder local position=" + Format(metrics.RecorderLocalPosition));
            report.AppendLine("Recorder local rotation=" + Format(metrics.RecorderLocalRotation));
            report.AppendLine("Cassette local position=" + Format(metrics.CassetteLocalPosition));
            report.AppendLine("Cassette local rotation=" + Format(metrics.CassetteLocalRotation));
            report.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "Support marker errors palm/thumb/index/middle/ring/pinky=" +
                "{0:F6}/{1:F6}/{2:F6}/{3:F6}/{4:F6}/{5:F6}m",
                metrics.SupportPalmError,
                metrics.SupportThumbError,
                metrics.SupportIndexError,
                metrics.SupportMiddleError,
                metrics.SupportRingError,
                metrics.SupportPinkyError));
            report.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "Cassette marker errors thumb/index/middle={0:F6}/{1:F6}/{2:F6}m",
                metrics.CassetteThumbError,
                metrics.CassetteIndexError,
                metrics.CassetteMiddleError));
            report.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "Maximum material penetration recorder/cassette={0:F6}/{1:F6}m",
                metrics.RecorderPenetration,
                metrics.CassettePenetration));
            report.AppendLine("Support samples in recorder local palm/thumb/index/middle/ring/pinky=" +
                Format(rig.Recorder.transform.InverseTransformPoint(
                    RequireUnique(rig.Hands, "Hand_2.L").position)) + "/" +
                Format(rig.Recorder.transform.InverseTransformPoint(
                    RequireUnique(rig.Hands, "Finger_1_4.L").position)) + "/" +
                Format(rig.Recorder.transform.InverseTransformPoint(
                    RequireUnique(rig.Hands, "Finger_2_3.L").position)) + "/" +
                Format(rig.Recorder.transform.InverseTransformPoint(
                    RequireUnique(rig.Hands, "Finger_3_3.L").position)) + "/" +
                Format(rig.Recorder.transform.InverseTransformPoint(
                    RequireUnique(rig.Hands, "Finger_4_3.L").position)) + "/" +
                Format(rig.Recorder.transform.InverseTransformPoint(
                    RequireUnique(rig.Hands, "Finger_5_3.L").position)));
            report.AppendLine("Cassette samples in cassette local thumb/index/middle=" +
                Format(rig.Cassette.transform.InverseTransformPoint(
                    RequireUnique(rig.Hands, "Finger_1_4.R").position)) + "/" +
                Format(rig.Cassette.transform.InverseTransformPoint(
                    RequireUnique(rig.Hands, "Finger_2_3.R").position)) + "/" +
                Format(rig.Cassette.transform.InverseTransformPoint(
                    RequireUnique(rig.Hands, "Finger_3_3.R").position)));
            report.AppendLine("Deepest recorder penetration=" +
                DescribeDeepestPenetration(
                    rig.Hands,
                    rig.Recorder.transform,
                    RecorderGripCoreSize,
                    new[] { "Finger_1_", "Finger_2_", "Finger_3_", "Finger_4_", "Finger_5_" },
                    ".L"));
            report.AppendLine("Deepest cassette penetration=" +
                DescribeDeepestPenetration(
                    rig.Hands,
                    rig.Cassette.transform,
                    new Vector3(0.110000f, 0.018200f, 0.070000f),
                    new[] { "Finger_1_", "Finger_2_", "Finger_3_" },
                    ".R"));
            RenderStaticGripCandidates(camera, rig, output, carry);

            const float supportMarkerTolerance = 0.005f;
            const float cassetteMarkerTolerance = 0.002f;
            const float penetrationTolerance = 0.002f;
            bool supportContactsPass =
                metrics.SupportPalmError <= 0.003f &&
                metrics.SupportThumbError <= supportMarkerTolerance &&
                metrics.SupportIndexError <= supportMarkerTolerance &&
                metrics.SupportMiddleError <= supportMarkerTolerance &&
                metrics.SupportRingError <= supportMarkerTolerance &&
                metrics.SupportPinkyError <= supportMarkerTolerance;
            bool cassetteContactsPass =
                metrics.CassetteThumbError <= cassetteMarkerTolerance &&
                metrics.CassetteIndexError <= cassetteMarkerTolerance &&
                metrics.CassetteMiddleError <= cassetteMarkerTolerance;
            bool penetrationPass =
                metrics.RecorderPenetration <= penetrationTolerance &&
                metrics.CassettePenetration <= penetrationTolerance;
            report.AppendLine("Support contact gate=" +
                (supportContactsPass ? "PASS" : "FAIL"));
            report.AppendLine("Cassette pinch gate=" +
                (cassetteContactsPass ? "PASS" : "FAIL"));
            report.AppendLine("Penetration gate=" +
                (penetrationPass ? "PASS" : "FAIL"));
            report.AppendLine("RESULT=" +
                (supportContactsPass && cassetteContactsPass && penetrationPass
                    ? "PASS"
                    : "FAIL"));
            File.WriteAllText(Path.Combine(output, "static-grip-report.txt"), report.ToString());

            if (!supportContactsPass)
            {
                structuralIssues.Add(
                    "STATIC GRIP — support hand does not satisfy recorder contact markers");
            }
            if (!cassetteContactsPass)
            {
                structuralIssues.Add(
                    "STATIC GRIP — cassette pinch does not satisfy its three grip markers");
            }
            if (metrics.RecorderPenetration > penetrationTolerance)
            {
                structuralIssues.Add(
                    "STATIC GRIP — support phalange penetrates recorder by more than 2mm");
            }
            if (metrics.CassettePenetration > penetrationTolerance)
            {
                structuralIssues.Add(
                    "STATIC GRIP — cassette phalange penetrates cassette by more than 2mm");
            }
        }

        private static void WriteManualGripAuthoringStatus(string output)
        {
            File.WriteAllText(
                Path.Combine(output, "manual-grip-authoring-status.txt"),
                "SoulRecorder manual static-grip authoring\n" +
                "SupportHold=PENDING HUMAN APPROVAL\n" +
                "CassetteCarry=PENDING HUMAN APPROVAL\n" +
                "Legacy contact markers are non-authoritative.\n" +
                "Insertion/ejection authoring is blocked until both poses are approved.\n" +
                "RESULT=PENDING\n");
        }

        private static StaticGripMetrics ApplyMarkerSolvedStaticGrip(
            PreviewRig rig,
            Vector3 supportWristOffset,
            Vector3 cassetteWristOffset)
        {
            StaticGripMetrics result = new StaticGripMetrics();
            Transform supportWrist = RequireUnique(rig.Hands, "Hand_1.L");
            supportWrist.localRotation *= Quaternion.Euler(supportWristOffset);
            Transform cassetteWrist = RequireUnique(rig.Hands, "Hand_1.R");
            cassetteWrist.localRotation *= Quaternion.Euler(cassetteWristOffset);
            Transform supportPalm = RequireUnique(rig.Hands, "Hand_2.L");
            Transform supportThumb = RequireUnique(rig.Hands, "Finger_1_4.L");
            Transform supportIndex = RequireUnique(rig.Hands, "Finger_2_3.L");
            // The recorder's readable first-person orientation is authored; the
            // palm anchor solves translation exactly. Individual support digits
            // must then be posed onto their own markers instead of rotating the
            // entire recorder to fit a bad generic finger curl.
            SolveOriginWithFixedRotation(
                rig.RecorderGrip,
                rig.Recorder.transform,
                RequireUnique(rig.Recorder, "RecorderSupportPalm"),
                supportPalm.position,
                Quaternion.Euler(RuntimeRecorderGripRotation));
            SolveFingerToMarker(rig.Hands, 1, ".L", RequireUnique(
                rig.Recorder, "RecorderSupportThumb").position);
            SolveFingerToMarker(rig.Hands, 2, ".L", RequireUnique(
                rig.Recorder, "RecorderSupportIndex").position);
            SolveFingerToMarker(rig.Hands, 3, ".L", RequireUnique(
                rig.Recorder, "RecorderSupportMiddle").position);
            SolveFingerToMarker(rig.Hands, 4, ".L", RequireUnique(
                rig.Recorder, "RecorderSupportRing").position);
            SolveFingerToMarker(rig.Hands, 5, ".L", RequireUnique(
                rig.Recorder, "RecorderSupportPinky").position);
            result.RecorderLocalPosition = rig.Recorder.transform.localPosition;
            result.RecorderLocalRotation = rig.Recorder.transform.localRotation.eulerAngles;

            // Establish a deliberate pinch before deriving the cassette frame.
            // The sampled clip supplies the native/rest baseline. These are
            // small per-digit offsets, not one generic curl: index/middle form
            // the opposing jaw while ring/pinky fold clear of the cassette.
            ApplyFingerJointOffsets(rig.Hands, 1, ".R", new[] { 8f, 12f, 9f, 5f });
            ApplyFingerJointOffsets(rig.Hands, 2, ".R", new[] { 35f, 40f, 15f });
            ApplyFingerJointOffsets(rig.Hands, 3, ".R", new[] { 28f, 35f, 15f });
            ApplyFingerJointOffsets(rig.Hands, 4, ".R", new[] { 45f, 50f, 30f });
            ApplyFingerJointOffsets(rig.Hands, 5, ".R", new[] { 50f, 55f, 35f });
            Transform cassetteThumb = RequireUnique(rig.Hands, "Finger_1_4.R");
            Transform cassetteIndex = RequireUnique(rig.Hands, "Finger_2_3.R");
            Transform cassetteMiddle = RequireUnique(rig.Hands, "Finger_3_3.R");
            SolveContactFrame(
                rig.CassetteGrip,
                rig.Cassette.transform,
                RequireUnique(rig.Cassette, "CassetteThumbGrip"),
                RequireUnique(rig.Cassette, "CassetteIndexGrip"),
                RequireUnique(rig.Cassette, "CassetteMiddleGrip"),
                cassetteThumb.position,
                cassetteIndex.position,
                cassetteMiddle.position);
            SolveFingerToMarker(rig.Hands, 1, ".R", RequireUnique(
                rig.Cassette, "CassetteThumbGrip").position);
            SolveFingerToMarker(rig.Hands, 2, ".R", RequireUnique(
                rig.Cassette, "CassetteIndexGrip").position);
            SolveFingerToMarker(rig.Hands, 3, ".R", RequireUnique(
                rig.Cassette, "CassetteMiddleGrip").position);
            result.CassetteLocalPosition = rig.Cassette.transform.localPosition;
            result.CassetteLocalRotation = rig.Cassette.transform.localRotation.eulerAngles;

            result.SupportPalmError = Vector3.Distance(
                supportPalm.position,
                RequireUnique(rig.Recorder, "RecorderSupportPalm").position);
            result.SupportThumbError = MarkerError(rig.Recorder, "RecorderSupportThumb", supportThumb);
            result.SupportIndexError = MarkerError(rig.Recorder, "RecorderSupportIndex", supportIndex);
            result.SupportMiddleError = MarkerError(
                rig.Recorder, "RecorderSupportMiddle", RequireUnique(rig.Hands, "Finger_3_3.L"));
            result.SupportRingError = MarkerError(
                rig.Recorder, "RecorderSupportRing", RequireUnique(rig.Hands, "Finger_4_3.L"));
            result.SupportPinkyError = MarkerError(
                rig.Recorder, "RecorderSupportPinky", RequireUnique(rig.Hands, "Finger_5_3.L"));
            result.CassetteThumbError = MarkerError(
                rig.Cassette, "CassetteThumbGrip", cassetteThumb);
            result.CassetteIndexError = MarkerError(
                rig.Cassette, "CassetteIndexGrip", cassetteIndex);
            result.CassetteMiddleError = MarkerError(
                rig.Cassette, "CassetteMiddleGrip", cassetteMiddle);
            result.RecorderPenetration = MaximumFingerPenetration(
                rig.Hands,
                rig.Recorder.transform,
                RecorderGripCoreSize,
                new[] { "Finger_1_", "Finger_2_", "Finger_3_", "Finger_4_", "Finger_5_" },
                ".L");
            result.CassettePenetration = MaximumFingerPenetration(
                rig.Hands,
                rig.Cassette.transform,
                new Vector3(0.110000f, 0.018200f, 0.070000f),
                new[] { "Finger_1_", "Finger_2_", "Finger_3_" },
                ".R");
            return result;
        }

        private static void RenderStaticGripCandidates(
            Camera camera,
            PreviewRig rig,
            string output,
            StateSpec carry)
        {
            string folder = Path.Combine(output, "Static-Grip-Candidates");
            Directory.CreateDirectory(folder);
            KeyValuePair<string, Vector3>[] candidates =
            {
                new KeyValuePair<string, Vector3>("00-baseline", Vector3.zero),
                new KeyValuePair<string, Vector3>("01-roll-minus-30", new Vector3(0f, 0f, -30f)),
                new KeyValuePair<string, Vector3>("02-roll-plus-30", new Vector3(0f, 0f, 30f)),
                new KeyValuePair<string, Vector3>("03-yaw-minus-30", new Vector3(0f, -30f, 0f)),
                new KeyValuePair<string, Vector3>("04-yaw-plus-30", new Vector3(0f, 30f, 0f)),
                new KeyValuePair<string, Vector3>("05-pitch-minus-20-roll-minus-25", new Vector3(-20f, 0f, -25f)),
                new KeyValuePair<string, Vector3>("06-pitch-plus-20-roll-plus-25", new Vector3(20f, 0f, 25f)),
                new KeyValuePair<string, Vector3>("07-yaw-minus-20-roll-minus-35", new Vector3(0f, -20f, -35f))
            };
            foreach (KeyValuePair<string, Vector3> candidate in candidates)
            {
                ApplyState(rig, carry);
                ApplyMarkerSolvedStaticGrip(rig, Vector3.zero, candidate.Value);
                RenderPng(camera, Path.Combine(folder, candidate.Key + ".png"));
            }
            CreateContactSheet(
                folder,
                Path.Combine(output, "static-grip-candidates-fov70.png"),
                candidates.Select(value => value.Key).ToArray());
        }

        private static void SolveContactFrame(
            Transform owner,
            Transform model,
            Transform sourceOrigin,
            Transform sourceAxisPoint,
            Transform sourcePlanePoint,
            Vector3 targetOrigin,
            Vector3 targetAxisPoint,
            Vector3 targetPlanePoint)
        {
            Vector3 sourceOriginLocal = model.InverseTransformPoint(sourceOrigin.position);
            Vector3 sourceAxisLocal = model.InverseTransformPoint(sourceAxisPoint.position);
            Vector3 sourcePlaneLocal = model.InverseTransformPoint(sourcePlanePoint.position);
            Quaternion sourceFrame = ContactFrame(
                sourceOriginLocal,
                sourceAxisLocal,
                sourcePlaneLocal);
            Quaternion targetFrame = ContactFrame(
                targetOrigin,
                targetAxisPoint,
                targetPlanePoint);
            Quaternion modelWorldRotation = targetFrame * Quaternion.Inverse(sourceFrame);
            Quaternion modelLocalRotation = Quaternion.Inverse(owner.rotation) * modelWorldRotation;
            model.localRotation = modelLocalRotation;
            model.localPosition = owner.InverseTransformPoint(targetOrigin) -
                (modelLocalRotation * sourceOriginLocal);
        }

        private static void SolveOriginWithFixedRotation(
            Transform owner,
            Transform model,
            Transform sourceOrigin,
            Vector3 targetOrigin,
            Quaternion localRotation)
        {
            Vector3 sourceOriginLocal = model.InverseTransformPoint(sourceOrigin.position);
            model.localRotation = localRotation;
            model.localPosition = owner.InverseTransformPoint(targetOrigin) -
                (localRotation * sourceOriginLocal);
        }

        private static void SolveFingerToMarker(
            GameObject hands,
            int finger,
            string side,
            Vector3 target)
        {
            int segments = finger == 1 ? 4 : 3;
            Transform[] chain = Enumerable.Range(1, segments)
                .Select(segment => RequireUnique(
                    hands,
                    "Finger_" + finger + "_" + segment + side))
                .ToArray();
            for (int iteration = 0; iteration < 18; iteration++)
            {
                Vector3 tip = chain[chain.Length - 1].position;
                if (Vector3.Distance(tip, target) <= 0.001f)
                {
                    break;
                }
                for (int index = chain.Length - 2; index >= 0; index--)
                {
                    Transform joint = chain[index];
                    Vector3 toTip = chain[chain.Length - 1].position - joint.position;
                    Vector3 toTarget = target - joint.position;
                    if (toTip.sqrMagnitude < 0.0000001f ||
                        toTarget.sqrMagnitude < 0.0000001f)
                    {
                        continue;
                    }
                    Quaternion delta = Quaternion.FromToRotation(toTip, toTarget);
                    float angle;
                    Vector3 axis;
                    delta.ToAngleAxis(out angle, out axis);
                    if (angle > 18f)
                    {
                        delta = Quaternion.AngleAxis(18f, axis);
                    }
                    joint.rotation = delta * joint.rotation;
                }
            }
        }

        private static void ApplyFingerJointOffsets(
            GameObject hands,
            int finger,
            string side,
            float[] bendDegrees)
        {
            int segments = finger == 1 ? 4 : 3;
            if (bendDegrees == null || bendDegrees.Length != segments)
            {
                throw new ArgumentException(
                    "A distinct bend offset is required for every finger joint.",
                    nameof(bendDegrees));
            }
            for (int segment = 1; segment <= segments; segment++)
            {
                Transform joint = RequireUnique(
                    hands,
                    "Finger_" + finger + "_" + segment + side);
                joint.localRotation *= Quaternion.Euler(
                    bendDegrees[segment - 1], 0f, 0f);
            }
        }

        private static Quaternion ContactFrame(
            Vector3 origin,
            Vector3 axisPoint,
            Vector3 planePoint)
        {
            Vector3 x = (axisPoint - origin).normalized;
            Vector3 plane = planePoint - origin;
            Vector3 y = (plane - (Vector3.Dot(plane, x) * x)).normalized;
            if (x.sqrMagnitude < 0.9f || y.sqrMagnitude < 0.9f)
            {
                throw new InvalidOperationException("Contact rig markers were degenerate.");
            }
            Vector3 z = Vector3.Cross(x, y).normalized;
            y = Vector3.Cross(z, x).normalized;
            Matrix4x4 matrix = Matrix4x4.identity;
            matrix.SetColumn(0, new Vector4(x.x, x.y, x.z, 0f));
            matrix.SetColumn(1, new Vector4(y.x, y.y, y.z, 0f));
            matrix.SetColumn(2, new Vector4(z.x, z.y, z.z, 0f));
            return matrix.rotation;
        }

        private static float MarkerError(GameObject model, string marker, Transform sample)
        {
            return Vector3.Distance(RequireUnique(model, marker).position, sample.position);
        }

        private static float MaximumFingerPenetration(
            GameObject hands,
            Transform model,
            Vector3 size,
            string[] prefixes,
            string suffix)
        {
            float maximum = 0f;
            foreach (Transform bone in hands.GetComponentsInChildren<Transform>(true))
            {
                if (!prefixes.Any(prefix => bone.name.StartsWith(prefix, StringComparison.Ordinal)) ||
                    !bone.name.EndsWith(suffix, StringComparison.Ordinal) ||
                    IsFingerPalmRoot(bone.name, suffix))
                {
                    continue;
                }
                Vector3 point = model.InverseTransformPoint(bone.position);
                Vector3 half = size * 0.5f;
                if (Mathf.Abs(point.x) >= half.x ||
                    Mathf.Abs(point.y) >= half.y ||
                    Mathf.Abs(point.z) >= half.z)
                {
                    continue;
                }
                float depth = Mathf.Min(
                    half.x - Mathf.Abs(point.x),
                    Mathf.Min(
                        half.y - Mathf.Abs(point.y),
                        half.z - Mathf.Abs(point.z)));
                maximum = Mathf.Max(maximum, depth);
            }
            return maximum;
        }

        private static string DescribeDeepestPenetration(
            GameObject hands,
            Transform model,
            Vector3 size,
            string[] prefixes,
            string suffix)
        {
            float maximum = 0f;
            string deepest = "none";
            Vector3 deepestPoint = Vector3.zero;
            Vector3 half = size * 0.5f;
            foreach (Transform bone in hands.GetComponentsInChildren<Transform>(true))
            {
                if (!prefixes.Any(prefix => bone.name.StartsWith(prefix, StringComparison.Ordinal)) ||
                    !bone.name.EndsWith(suffix, StringComparison.Ordinal) ||
                    IsFingerPalmRoot(bone.name, suffix))
                {
                    continue;
                }
                Vector3 point = model.InverseTransformPoint(bone.position);
                if (Mathf.Abs(point.x) >= half.x ||
                    Mathf.Abs(point.y) >= half.y ||
                    Mathf.Abs(point.z) >= half.z)
                {
                    continue;
                }
                float depth = Mathf.Min(
                    half.x - Mathf.Abs(point.x),
                    Mathf.Min(
                        half.y - Mathf.Abs(point.y),
                        half.z - Mathf.Abs(point.z)));
                if (depth > maximum)
                {
                    maximum = depth;
                    deepest = bone.name;
                    deepestPoint = point;
                }
            }
            return deepest + " depth=" + maximum.ToString("F6", CultureInfo.InvariantCulture) +
                "m local=" + Format(deepestPoint);
        }

        private static bool IsFingerPalmRoot(string name, string suffix)
        {
            return name.EndsWith("_1" + suffix, StringComparison.Ordinal);
        }

        private static List<GameObject> CreateContactRigGizmos(PreviewRig rig)
        {
            List<GameObject> result = new List<GameObject>();
            AddMarkerGizmos(
                result,
                rig.Recorder,
                new[]
                {
                    "RecorderSupportPalm", "RecorderSupportThumb",
                    "RecorderSupportIndex", "RecorderSupportMiddle",
                    "RecorderSupportRing", "RecorderSupportPinky",
                    "CassetteSlotEntry", "CassetteSlotSeated"
                },
                Color.cyan);
            AddMarkerGizmos(
                result,
                rig.Cassette,
                new[]
                {
                    "CassetteThumbGrip", "CassetteIndexGrip",
                    "CassetteMiddleGrip", "CassetteFront", "CassetteTop"
                },
                Color.magenta);
            AddMarkerGizmos(
                result,
                rig.Hands,
                new[]
                {
                    "Hand_2.L", "Finger_1_4.L", "Finger_2_3.L",
                    "Finger_3_3.L", "Finger_4_3.L", "Finger_5_3.L",
                    "Finger_1_4.R", "Finger_2_3.R", "Finger_3_3.R"
                },
                Color.yellow);
            result.Add(CreateGizmoLine(
                RequireUnique(rig.Recorder, "CassetteSlotEntry").position,
                RequireUnique(rig.Recorder, "CassetteSlotSeated").position,
                Color.green));
            Transform cassetteAxis = RequireUnique(rig.Cassette, "CassetteInsertionAxis");
            result.Add(CreateGizmoLine(
                cassetteAxis.position,
                cassetteAxis.position + (cassetteAxis.forward * 0.06f),
                new Color(1f, 0.5f, 0f, 1f)));
            return result;
        }

        private static void AddMarkerGizmos(
            List<GameObject> output,
            GameObject root,
            string[] names,
            Color color)
        {
            foreach (string name in names)
            {
                Transform marker = RequireUnique(root, name);
                GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = "Gizmo_" + name;
                sphere.transform.position = marker.position;
                sphere.transform.localScale = Vector3.one * 0.016f;
                Collider collider = sphere.GetComponent<Collider>();
                if (collider != null)
                {
                    UnityEngine.Object.DestroyImmediate(collider);
                }
                Renderer renderer = sphere.GetComponent<Renderer>();
                renderer.sharedMaterial = GizmoMaterial(color);
                output.Add(sphere);
            }
        }

        private static GameObject CreateGizmoLine(Vector3 start, Vector3 end, Color color)
        {
            GameObject root = new GameObject("ContactRigAxis");
            LineRenderer line = root.AddComponent<LineRenderer>();
            line.sharedMaterial = GizmoMaterial(color);
            line.startWidth = 0.006f;
            line.endWidth = 0.006f;
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
            return root;
        }

        private static Material GizmoMaterial(Color color)
        {
            Shader shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            if (shader == null)
            {
                throw new InvalidOperationException("Contact-rig gizmo shader was unavailable.");
            }
            Material material = new Material(shader);
            material.color = color;
            return material;
        }

        private static void CreateContactSheet(string folder, string output, string[] states)
        {
            const int cellWidth = 480;
            const int cellHeight = 270;
            const int columns = 4;
            int rows = Mathf.CeilToInt(states.Length / (float)columns);
            Texture2D sheet = new Texture2D(
                cellWidth * columns,
                cellHeight * rows,
                TextureFormat.RGB24,
                false);
            Color32[] background = Enumerable.Repeat(
                new Color32(10, 12, 14, 255),
                sheet.width * sheet.height).ToArray();
            sheet.SetPixels32(background);
            try
            {
                for (int index = 0; index < states.Length; index++)
                {
                    byte[] data = File.ReadAllBytes(Path.Combine(folder, states[index] + ".png"));
                    Texture2D source = new Texture2D(2, 2, TextureFormat.RGB24, false);
                    try
                    {
                        source.LoadImage(data);
                        RenderTexture target = RenderTexture.GetTemporary(cellWidth, cellHeight);
                        Graphics.Blit(source, target);
                        RenderTexture previous = RenderTexture.active;
                        RenderTexture.active = target;
                        Texture2D scaled = new Texture2D(
                            cellWidth, cellHeight, TextureFormat.RGB24, false);
                        try
                        {
                            scaled.ReadPixels(new Rect(0, 0, cellWidth, cellHeight), 0, 0);
                            scaled.Apply();
                            int x = (index % columns) * cellWidth;
                            int y = (rows - 1 - (index / columns)) * cellHeight;
                            sheet.SetPixels32(x, y, cellWidth, cellHeight, scaled.GetPixels32());
                        }
                        finally
                        {
                            UnityEngine.Object.DestroyImmediate(scaled);
                            RenderTexture.active = previous;
                            RenderTexture.ReleaseTemporary(target);
                        }
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(source);
                    }
                }
                sheet.Apply();
                File.WriteAllBytes(output, sheet.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sheet);
            }
        }

        private static void RenderArmSilhouetteDiagnostics(
            string output,
            List<StateMetrics> metrics)
        {
            string folder = Path.Combine(output, "Arm-Silhouette-FOV70");
            Directory.CreateDirectory(folder);
            string[] names =
            {
                "04-insert-carry", "06-insert-alignment",
                "10-cassette-contact", "20-eject-clear"
            };
            StringBuilder report = new StringBuilder();
            report.AppendLine("SoulRecorder FOV-70 arm silhouette diagnostic");
            report.AppendLine("cyan=line, magenta=cutoff, orange=elbow, yellow=wrist, green=hand");
            foreach (string name in names)
            {
                StateMetrics state = metrics.Single(value =>
                    value.Fov == 70f && value.Name == name);
                string sourcePath = Path.Combine(output, "FOV-70", name + ".png");
                string targetPath = Path.Combine(folder, name + ".png");
                Texture2D image = new Texture2D(2, 2, TextureFormat.RGB24, false);
                try
                {
                    image.LoadImage(File.ReadAllBytes(sourcePath));
                    DrawArmDiagnostic(
                        image,
                        state.SupportSleeveCutoffViewport,
                        state.SupportElbowViewport,
                        state.SupportWristViewport,
                        state.SupportHandViewport);
                    DrawArmDiagnostic(
                        image,
                        state.InteractionSleeveCutoffViewport,
                        state.InteractionElbowViewport,
                        state.InteractionWristViewport,
                        state.InteractionHandViewport);
                    DrawLine(image, 0, 1, image.width - 1, 1, Color.white, 3);
                    image.Apply();
                    File.WriteAllBytes(targetPath, image.EncodeToPNG());
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(image);
                }
                report.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} supportAngleFromVertical={1:F3}deg " +
                    "cassetteAngleFromVertical={2:F3}deg support cutoff/elbow/wrist/hand={3}/{4}/{5}/{6} " +
                    "cassette cutoff/elbow/wrist/hand={7}/{8}/{9}/{10}",
                    name,
                    state.SupportForearmAngleDegrees,
                    state.InteractionForearmAngleDegrees,
                    Format(state.SupportSleeveCutoffViewport),
                    Format(state.SupportElbowViewport),
                    Format(state.SupportWristViewport),
                    Format(state.SupportHandViewport),
                    Format(state.InteractionSleeveCutoffViewport),
                    Format(state.InteractionElbowViewport),
                    Format(state.InteractionWristViewport),
                    Format(state.InteractionHandViewport)));
            }
            CreateContactSheet(
                folder,
                Path.Combine(output, "arm-silhouette-diagnostic-fov70.png"),
                names);
            File.WriteAllText(
                Path.Combine(output, "arm-silhouette-report.txt"),
                report.ToString());
        }

        private static void DrawArmDiagnostic(
            Texture2D image,
            Vector3 cutoff,
            Vector3 elbow,
            Vector3 wrist,
            Vector3 hand)
        {
            Vector2Int cutoffPixel = ViewportPixel(image, cutoff);
            Vector2Int elbowPixel = ViewportPixel(image, elbow);
            Vector2Int wristPixel = ViewportPixel(image, wrist);
            Vector2Int handPixel = ViewportPixel(image, hand);
            DrawLine(image, cutoffPixel.x, cutoffPixel.y, elbowPixel.x, elbowPixel.y,
                Color.cyan, 3);
            DrawLine(image, elbowPixel.x, elbowPixel.y, wristPixel.x, wristPixel.y,
                Color.cyan, 3);
            DrawLine(image, wristPixel.x, wristPixel.y, handPixel.x, handPixel.y,
                Color.cyan, 3);
            DrawDisc(image, cutoffPixel.x, cutoffPixel.y, 10, Color.magenta);
            DrawDisc(image, elbowPixel.x, elbowPixel.y, 9, new Color(1f, 0.45f, 0f));
            DrawDisc(image, wristPixel.x, wristPixel.y, 8, Color.yellow);
            DrawDisc(image, handPixel.x, handPixel.y, 7, Color.green);
        }

        private static Vector2Int ViewportPixel(Texture2D image, Vector3 viewport)
        {
            return new Vector2Int(
                Mathf.RoundToInt(viewport.x * (image.width - 1)),
                Mathf.RoundToInt(viewport.y * (image.height - 1)));
        }

        private static void DrawDisc(
            Texture2D image,
            int centerX,
            int centerY,
            int radius,
            Color color)
        {
            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    if ((x * x) + (y * y) > radius * radius)
                    {
                        continue;
                    }
                    int targetX = centerX + x;
                    int targetY = centerY + y;
                    if (targetX >= 0 && targetX < image.width &&
                        targetY >= 0 && targetY < image.height)
                    {
                        image.SetPixel(targetX, targetY, color);
                    }
                }
            }
        }

        private static void DrawLine(
            Texture2D image,
            int x0,
            int y0,
            int x1,
            int y1,
            Color color,
            int thickness)
        {
            int dx = Math.Abs(x1 - x0);
            int sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0);
            int sy = y0 < y1 ? 1 : -1;
            int error = dx + dy;
            while (true)
            {
                DrawDisc(image, x0, y0, thickness, color);
                if (x0 == x1 && y0 == y1)
                {
                    break;
                }
                int twice = 2 * error;
                if (twice >= dy)
                {
                    error += dy;
                    x0 += sx;
                }
                if (twice <= dx)
                {
                    error += dx;
                    y0 += sy;
                }
            }
        }

        private static void WriteReport(
            string output,
            PreviewRig rig,
            Camera camera,
            List<StateMetrics> metrics,
            List<string> generated,
            List<string> structuralIssues)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("SoulRecorder Offline First-Person Preview");
            report.AppendLine("Resolution: 1920x1080 (16:9)");
            report.AppendLine("FOVs: 50, 60, 70, 75");
            report.AppendLine("Near/Far: " + camera.nearClipPlane + "/" + camera.farClipPlane);
            report.AppendLine("Presentation position: " + Format(RuntimeHeldPosition));
            report.AppendLine("Presentation rotation: " + Format(RuntimeHeldRotation));
            report.AppendLine("Presentation scale: " + Format(RuntimePresentationScale));
            report.AppendLine("Structural checks: " +
                (structuralIssues.Count == 0 ? "PASS" : "FAIL"));
            foreach (string issue in structuralIssues)
            {
                report.AppendLine("  - " + issue);
            }
            List<string> animationQualityIssues = metrics
                .SelectMany(value => value.Issues)
                .Where(value => value.StartsWith(
                    "ANIMATION QUALITY — ",
                    StringComparison.Ordinal))
                .Distinct()
                .ToList();
            report.AppendLine("Animation quality checks: " +
                (animationQualityIssues.Count == 0 ? "PASS" : "FAIL"));
            foreach (string issue in animationQualityIssues)
            {
                report.AppendLine("  - " + issue);
            }
            List<string> choreographyIssues = metrics
                .SelectMany(value => value.Issues)
                .Where(value => value.StartsWith(
                    "CASSETTE CHOREOGRAPHY — ",
                    StringComparison.Ordinal))
                .Distinct()
                .ToList();
            report.AppendLine("Cassette choreography checks: " +
                (choreographyIssues.Count == 0 ? "PASS" : "FAIL"));
            foreach (string issue in choreographyIssues)
            {
                report.AppendLine("  - " + issue);
            }
            report.AppendLine();
            foreach (StateMetrics row in metrics)
            {
                report.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} FOV={1:F0} PASS={2} hands center={3} size={4} viewport={5} " +
                    "visiblePresentation={6} recorder center={7} size={8} cassette center={9} size={10}",
                    row.Name,
                    row.Fov,
                    row.Passed,
                    Format(row.Hands.center),
                    Format(row.Hands.size),
                    Format(row.HandsViewport),
                    Format(row.VisiblePresentationViewport),
                    Format(row.Recorder.center),
                    Format(row.Recorder.size),
                    Format(row.Cassette.center),
                    Format(row.Cassette.size)));
                if (row.Fov == 70f)
                {
                    report.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "  animation recorderViewport={0} supportPalm={1:F5}m " +
                        "support shoulder/cutoff/elbow/hand={2}/{3}/{4}/{5} " +
                        "interaction shoulder/cutoff/elbow/hand={6}/{7}/{8}/{9} " +
                        "forearmAngles support/interaction={10:F3}/{11:F3}deg",
                        Format(row.RecorderViewport),
                        row.SupportPalmDistance,
                        Format(row.SupportShoulderViewport),
                        Format(row.SupportSleeveCutoffViewport),
                        Format(row.SupportElbowViewport),
                        Format(row.SupportHandViewport),
                        Format(row.InteractionShoulderViewport),
                        Format(row.InteractionSleeveCutoffViewport),
                        Format(row.InteractionElbowViewport),
                        Format(row.InteractionHandViewport),
                        row.SupportForearmAngleDegrees,
                        row.InteractionForearmAngleDegrees));
                }
                if (row.Name == "10-cassette-contact")
                {
                    report.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "  contact originError={0:F6}m boundsError={1:F6}m " +
                        "orientationError={2:F4}deg handContactError={3:F6}m " +
                        "ownershipFrameSnap={4:F6}m attachedToHand={5} " +
                        "transferSnap={6:F8}m/{7:F6}deg",
                        row.CassetteContactError,
                        row.CassetteBoundsContactError,
                        row.CassetteOrientationErrorDegrees,
                        row.CassetteHandContactError,
                        row.CassetteOwnershipFrameSnap,
                        row.CassetteAttachedToInteractionHand,
                        row.OwnershipTransferPositionSnap,
                        row.OwnershipTransferRotationSnapDegrees));
                }
                if (row.Fov == 70f &&
                    CassetteManipulationStateNames().Contains(row.Name))
                {
                    report.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "  choreography owner={0} orientation={1:F4}deg " +
                        "thumb/index/middle={2:F6}/{3:F6}/{4:F6}m",
                        row.CassetteOwner,
                        row.CassetteOrientationErrorDegrees,
                        row.CassetteThumbContactError,
                        row.CassetteIndexContactError,
                        row.CassetteMiddleContactError));
                }
                foreach (string issue in row.Issues)
                {
                    report.AppendLine("  - " + issue);
                }
            }
            report.AppendLine();
            report.AppendLine("Generated PNGs: " + generated.Count);
            File.WriteAllText(Path.Combine(output, "preview-report.txt"), report.ToString());
        }

        private static GameObject RequirePrefab(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                throw new FileNotFoundException("Preview prefab was unavailable: " + path);
            }
            return prefab;
        }

        private static Transform RequireUnique(GameObject root, string name)
        {
            Transform found = null;
            foreach (Transform value in root.GetComponentsInChildren<Transform>(true))
            {
                if (!string.Equals(value.name, name, StringComparison.Ordinal))
                {
                    continue;
                }
                if (found != null)
                {
                    throw new InvalidOperationException("Duplicate preview transform " + name);
                }
                found = value;
            }
            if (found == null)
            {
                throw new InvalidOperationException("Missing preview transform " + name);
            }
            return found;
        }

        private static string CommandLineValue(string name)
        {
            string[] values = Environment.GetCommandLineArgs();
            for (int index = 0; index < values.Length - 1; index++)
            {
                if (string.Equals(values[index], name, StringComparison.OrdinalIgnoreCase))
                {
                    return values[index + 1];
                }
            }
            return string.Empty;
        }

        private static string Format(Vector3 value)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "({0:F5},{1:F5},{2:F5})",
                value.x, value.y, value.z);
        }

        private static string Format(Rect value)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "({0:F4},{1:F4},{2:F4},{3:F4})",
                value.x, value.y, value.width, value.height);
        }

        private static string Format(Quaternion value)
        {
            return Format(value.eulerAngles);
        }
    }
}
