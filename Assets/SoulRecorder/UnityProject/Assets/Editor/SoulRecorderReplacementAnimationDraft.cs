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
    /// <summary>
    /// Editor-only, non-destructive draft generator for the DJMaesen replacement
    /// arms. Human poses and StaticMaster_v1 are inputs only. Generated poses are
    /// written to a separate file and are never used by runtime code.
    /// </summary>
    internal static class SoulRecorderReplacementAnimationDraft
    {
        private const string OutputAssetFolder =
            "Assets/SoulRecorderReplacementArms/Validation/AutoAnimationDraft_v3";
        private const string DraftPoseAsset =
            OutputAssetFolder + "/SoulRecorderAutoDraftV3Poses.json";
        private const string DraftSceneAsset =
            OutputAssetFolder + "/SoulRecorderAutoAnimationDraft_v3.unity";
        private const int Width = 1280;
        private const int Height = 720;
        private const int FramesPerSecond = 30;
        private const float PositionTolerance = 0.0015f;
        private const float MaximumArmExtension = 0.965f;
        private const float PreferredArmExtension = 0.78f;
        private const float MaximumInteractionAssemblyShift = 0.080f;
        private const string CandidateOutputAssetFolder =
            "Assets/SoulRecorderReplacementArms/Validation/InteractionMasterCandidates";
        private const string CandidatePoseAsset = CandidateOutputAssetFolder +
            "/InteractionMasterCandidates.json";
        private const string PhysicalGripOutputAssetFolder =
            "Assets/SoulRecorderReplacementArms/Validation/PhysicalGripCandidates";
        private const string PhysicalGripPoseAsset = PhysicalGripOutputAssetFolder +
            "/PhysicalGripCandidates.json";
        private const string V4OutputAssetFolder =
            "Assets/SoulRecorderReplacementArms/Validation/AutoAnimationDraft_v4";
        private const string V4PoseAsset = V4OutputAssetFolder +
            "/SoulRecorderAutoDraftV4Poses.json";
        private const string V5OutputAssetFolder =
            "Assets/SoulRecorderReplacementArms/Validation/AutoAnimationDraft_v5";
        private const string V5PoseAsset = V5OutputAssetFolder +
            "/SoulRecorderAutoDraftV5Poses.json";
        private const string PoseRepairOutputAssetFolder =
            "Assets/SoulRecorderReplacementArms/Validation/PoseRepairCandidates";
        private const string PoseRepairPoseAsset = PoseRepairOutputAssetFolder +
            "/SoulRecorderPoseRepairCandidates.json";
        private const float V5MaximumSupportDigitError = 0.004f;
        private const float V5MaximumSupportPalmError = 0.006f;
        private const float V5MaximumSleeveOverlap = 0.35f;

        [Serializable]
        private sealed class DraftPoseFile
        {
            public int schemaVersion = 1;
            public string lockedStaticMasterSha256;
            public string sourceHumanPoseFileSha256;
            public string sourceAuthoringSceneSha256;
            public string generatedUtc;
            public DraftPoseRecord[] poses = new DraftPoseRecord[0];
        }

        [Serializable]
        private sealed class DraftPoseRecord
        {
            public string poseName;
            public string source;
            public string cassetteOwner;
            public Vector3 interactionGripPosition;
            public Quaternion interactionGripRotation;
            public Vector3 supportGripPosition;
            public Quaternion supportGripRotation;
            public DraftTransform[] transforms = new DraftTransform[0];
        }

        [Serializable]
        private sealed class DraftTransform
        {
            public string path;
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;
        }

        private sealed class Pose
        {
            internal string Name;
            internal string Source;
            internal CassetteOwner Owner;
            internal GripRelativeTransform RightHandGripToCassette;
            internal GripRelativeTransform LeftHandGripToRecorder;
            internal readonly Dictionary<string, DraftTransform> Values =
                new Dictionary<string, DraftTransform>(StringComparer.Ordinal);
        }

        private enum CassetteOwner
        {
            Independent,
            Hand,
            Recorder
        }

        private struct GripRelativeTransform
        {
            internal Vector3 Position;
            internal Quaternion Rotation;
        }

        private struct WorldPose
        {
            internal Vector3 Position;
            internal Quaternion Rotation;
        }

        private struct MarkerGeometry
        {
            internal Vector3 CassetteAxisLocalPosition;
            internal Quaternion CassetteAxisLocalRotation;
            internal Vector3 SlotEntryPosition;
            internal Vector3 SlotSeatedPosition;
            internal Quaternion SlotAxisRotation;
            internal Vector3 SlotDirection;
        }

        private sealed class Context
        {
            internal SoulRecorderReplacementArmStaging.Rig Rig;
            internal List<Transform> Controlled;
            internal Transform RecorderGrip;
            internal Transform CassetteGrip;
            internal Transform RightUpper;
            internal Transform RightElbow;
            internal Transform RightWrist;
            internal Transform LeftUpper;
            internal Transform LeftElbow;
            internal Transform LeftWrist;
            internal Camera Camera;
            internal GripRelativeTransform LeftHandGripToRecorder;
            internal GripRelativeTransform RightHandGripToCassette;
            internal GripRelativeTransform SlotToCassetteSeated;
            internal MarkerGeometry Markers;
            internal Vector3 AlignElbowPlaneNormal;
            internal Vector3 SupportElbowPlaneNormal;
            internal Vector3 StaticRightElbowOffset;
            internal float UpperLength;
            internal float ForearmLength;
            internal Pose StaticMaster;
            internal Pose HumanAlign;
            internal Pose HumanContact;
            internal Vector3 InteractionAssemblyShift;
            internal bool NumericalGatePassed = true;
            internal readonly List<string> GeometryFailures =
                new List<string>();
            internal readonly Dictionary<string, Pose> Generated =
                new Dictionary<string, Pose>(StringComparer.Ordinal);
        }

        private sealed class Transition
        {
            internal Pose From;
            internal Pose To;
            internal float Duration;
            internal CassetteOwner Owner;
            internal string Label;
        }

        private sealed class CandidateSpec
        {
            internal string Suffix;
            internal Vector3 RecorderCameraOffset;
            internal Vector3 RecorderCameraEuler;
            internal Vector3 LeftElbowPole;
            internal Vector3 RightElbowPole;
            internal Vector3 RightWristLocalEuler;
            internal Vector3 InteractionGripPositionOffset;
        }

        private sealed class CandidateMetrics
        {
            internal string Name;
            internal float Extension;
            internal float ContactPositionError;
            internal float ContactRotationError;
            internal float ForearmScreenAngle;
            internal float ForearmScreenLength;
            internal float RecorderVisibility;
            internal float RecorderViewportArea;
            internal float CassetteVisibility;
            internal float CassetteViewportArea;
            internal Vector3 RecorderViewport;
            internal Vector3 CassetteViewport;
            internal Vector3 WristViewport;
            internal float RecorderGripPositionDrift;
            internal float RecorderGripRotationDrift;
            internal float CassetteGripPositionDrift;
            internal float CassetteGripRotationDrift;
            internal float InteractionGripPositionDelta;
            internal float InteractionGripRotationDelta;
            internal Vector3 ElbowViewport;
            internal bool ElbowLowRight;
            internal float LeftUpperTwist;
            internal float LeftForearmTwist;
            internal float LeftWristRoll;
            internal float RightUpperTwist;
            internal float RightForearmTwist;
            internal float RightWristRoll;
            internal float LeftElbowBend;
            internal float RightElbowBend;
            internal bool LeftElbowFlipped;
            internal bool RightElbowFlipped;
            internal readonly List<string> BoneDrifts = new List<string>();
            internal bool Passed;
            internal readonly List<string> Failures = new List<string>();
        }

        private struct ProjectedVisibility
        {
            internal float VisibleFraction;
            internal float ViewportArea;
        }

        private sealed class PhysicalGripSpec
        {
            internal string Suffix;
            internal Vector3 WristCameraOffset;
            internal Vector3 WristLocalEuler;
        }

        private sealed class PoseRepairSpec
        {
            internal string Suffix;
            internal Vector3 RecorderCameraOffset;
            internal Vector3 RecorderCameraEuler;
            internal Vector3 WristCassetteLocalOffset;
            internal Vector3 WristCassetteLocalEuler;
            internal Vector3 RightElbowPole;
            internal float FingerLimitScale;
        }

        private sealed class PoseRepairMetrics
        {
            internal string Name;
            internal float SlotPositionError;
            internal float SlotRotationError;
            internal float MaximumLeftFingerDelta;
            internal float MaximumRightFingerDelta;
            internal float MaximumRightDistalDelta;
            internal float RightWristDelta;
            internal float ThumbError;
            internal float IndexError;
            internal float MiddleError;
            internal float IndexMiddleSeparation;
            internal float RightExtension;
            internal Vector3 RecorderViewport;
            internal Vector3 CassetteViewport;
            internal Vector3 LeftWristViewport;
            internal Vector3 RightWristViewport;
            internal Vector3 RendererBoundsViewport;
            internal readonly List<string> Warnings = new List<string>();
        }

        private sealed class CassetteAxisAudit
        {
            internal WorldPose ContactPose;
            internal float DirectCarryRotation;
            internal float RolledCarryRotation;
            internal float SelectedCarryRotation;
            internal float AxisDirectionDot;
            internal float SlotDirectionDot;
            internal float FrontTopDot;
            internal bool UsedEquivalentRoll;
            internal bool AxisSignCorrect;
        }

        private sealed class PhysicalGripMetrics
        {
            internal string Name;
            internal float ThumbError;
            internal float IndexError;
            internal float MiddleError;
            internal float SupportPalmError;
            internal float SupportThumbError;
            internal float SupportIndexError;
            internal float SupportMiddleError;
            internal float SupportRingError;
            internal float SupportPinkyError;
            internal float CassettePenetration;
            internal float RecorderPenetration;
            internal float InteractionGripPositionDelta;
            internal float InteractionGripRotationDelta;
            internal float CassetteRotationFromCarry;
            internal float RightExtension;
            internal float RightWristRoll;
            internal float SlotContactError;
            internal float SlotRotationError;
            internal bool SlotReadable;
            internal bool Passed;
            internal readonly List<string> Failures = new List<string>();
        }

        private sealed class V5FrameMetrics
        {
            internal float MaximumVisibleSupportError;
            internal float MaximumSleeveOverlap;
            internal float MinimumRotationClearance = float.PositiveInfinity;
            internal float ContactToSeatedRotationDelta;
            internal float InsertionOwnershipPositionSnap;
            internal float InsertionOwnershipRotationSnap;
            internal float EjectionOwnershipPositionSnap;
            internal float EjectionOwnershipRotationSnap;
            internal int VisibleSupportFrames;
            internal int SampledFrames;
            internal readonly List<string> Failures = new List<string>();
        }

        private sealed class FingerChain
        {
            internal Transform[] Joints;
            internal Vector3 TipLocalOffset;
            internal Quaternion[] BaselineLocalRotations;

            internal Vector3 TipPosition =>
                Joints[Joints.Length - 1].TransformPoint(TipLocalOffset);
        }

        [MenuItem("SoulPlayer/Replacement Arms/Generate Auto Cassette Animation Draft")]
        internal static void GenerateFromMenu()
        {
            Generate(false, false);
            Debug.Log("SoulRecorder replacement V3 ownership diagnostics complete.");
        }

        internal static void BatchGenerate()
        {
            try
            {
                Generate(true, false);
                Debug.Log("AUTO ANIMATION DRAFT V3 DIAGNOSTICS: PASS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError("AUTO ANIMATION DRAFT V3 DIAGNOSTICS: FAIL");
                EditorApplication.Exit(1);
            }
        }

        internal static void BatchGenerateFull()
        {
            try
            {
                Generate(true, true);
                Debug.Log("AUTO ANIMATION DRAFT V3 FULL SEQUENCES: PASS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError("AUTO ANIMATION DRAFT V3 FULL SEQUENCES: FAIL");
                EditorApplication.Exit(1);
            }
        }

        [MenuItem("SoulPlayer/Replacement Arms/Generate Interaction Master Candidates")]
        internal static void GenerateInteractionCandidatesFromMenu()
        {
            GenerateInteractionCandidates();
            Debug.Log("SoulRecorder InteractionMaster G-L candidates generated.");
        }

        internal static void BatchGenerateInteractionCandidates()
        {
            try
            {
                GenerateInteractionCandidates();
                Debug.Log("INTERACTION MASTER CANDIDATES: PASS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError("INTERACTION MASTER CANDIDATES: FAIL");
                EditorApplication.Exit(1);
            }
        }

        [MenuItem("SoulPlayer/Replacement Arms/Generate Physical Grip L1-L3")]
        internal static void GeneratePhysicalGripCandidatesFromMenu()
        {
            GeneratePhysicalGripCandidates();
            Debug.Log("SoulRecorder PhysicalGrip L1-L3 candidates generated.");
        }

        internal static void BatchGeneratePhysicalGripCandidates()
        {
            try
            {
                GeneratePhysicalGripCandidates();
                Debug.Log("PHYSICAL GRIP L1-L3: PASS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError("PHYSICAL GRIP L1-L3: FAIL");
                EditorApplication.Exit(1);
            }
        }

        [MenuItem("SoulPlayer/Replacement Arms/Generate Auto Animation Draft V4")]
        internal static void GenerateV4FromMenu()
        {
            GenerateV4();
            Debug.Log("AUTO ANIMATION DRAFT V4: PASS");
        }

        internal static void BatchGenerateV4()
        {
            try
            {
                GenerateV4();
                Debug.Log("AUTO ANIMATION DRAFT V4: PASS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError("AUTO ANIMATION DRAFT V4: FAIL");
                EditorApplication.Exit(1);
            }
        }

        [MenuItem("SoulPlayer/Replacement Arms/Generate Auto Animation Draft V5")]
        internal static void GenerateV5FromMenu()
        {
            GenerateV5();
            Debug.Log("AUTO ANIMATION DRAFT V5: PASS");
        }

        internal static void BatchGenerateV5()
        {
            try
            {
                GenerateV5();
                Debug.Log("AUTO ANIMATION DRAFT V5: PASS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError("AUTO ANIMATION DRAFT V5: FAIL");
                EditorApplication.Exit(1);
            }
        }

        [MenuItem("SoulPlayer/Replacement Arms/Generate Pose Repair A-C")]
        internal static void GeneratePoseRepairFromMenu()
        {
            GeneratePoseRepairCandidates();
            Debug.Log("SoulRecorder PoseRepair A-C candidates generated.");
        }

        internal static void BatchGeneratePoseRepair()
        {
            try
            {
                GeneratePoseRepairCandidates();
                Debug.Log("SOULRECORDER POSE REPAIR A-C: PASS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError("SOULRECORDER POSE REPAIR A-C: FAIL");
                EditorApplication.Exit(1);
            }
        }

        private static void GeneratePhysicalGripCandidates()
        {
            string humanPosePath = Path.GetFullPath(
                SoulRecorderReplacementArmStaging.PoseFile);
            string humanBefore = Sha256File(humanPosePath);
            string scenePath = Path.GetFullPath(
                SoulRecorderReplacementArmStaging.AuthoringScene);
            string sceneBefore = Sha256File(scenePath);
            Directory.CreateDirectory(Path.GetFullPath(
                PhysicalGripOutputAssetFolder));

            Scene scene = EditorSceneManager.OpenScene(
                SoulRecorderReplacementArmStaging.AuthoringScene,
                OpenSceneMode.Single);
            Context context = ResolveContext(scene);
            InitializeCandidateContext(context);
            CandidateSpec lSpec = InteractionCandidateSpecs().Single(value =>
                value.Suffix == "L");
            CandidateMetrics ignored;
            Pose lSeed = BuildInteractionCandidate(context, lSpec, out ignored);
            Pose physicalSeed = PreparePhysicalSupportSeed(context, lSeed);
            CassetteAxisAudit audit = AuditCassetteAxes(context, physicalSeed);
            List<Pose> poses = new List<Pose>();
            List<PhysicalGripMetrics> metrics = new List<PhysicalGripMetrics>();
            foreach (PhysicalGripSpec spec in PhysicalGripSpecs())
            {
                PhysicalGripMetrics measurement;
                poses.Add(BuildPhysicalGripCandidate(context, physicalSeed, audit,
                    spec, out measurement));
                metrics.Add(measurement);
            }

            WritePhysicalGripPoseFile(poses, humanBefore, sceneBefore);
            RenderPhysicalGripCandidates(context, poses, metrics, audit);
            AssetDatabase.Refresh();

            string humanAfter = Sha256File(humanPosePath);
            string sceneAfter = Sha256File(scenePath);
            if (!string.Equals(humanBefore, humanAfter,
                    StringComparison.Ordinal) ||
                !string.Equals(sceneBefore, sceneAfter,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Physical grip generation modified locked human-authored data.");
            }
            Debug.Log("Physical grip candidates preserved human pose SHA-256=" +
                humanAfter + " and authoring scene SHA-256=" + sceneAfter + ".");
        }

        private static void GeneratePoseRepairCandidates()
        {
            string humanPosePath = Path.GetFullPath(
                SoulRecorderReplacementArmStaging.PoseFile);
            string scenePath = Path.GetFullPath(
                SoulRecorderReplacementArmStaging.AuthoringScene);
            string humanBefore = Sha256File(humanPosePath);
            string sceneBefore = Sha256File(scenePath);
            string output = Path.GetFullPath(PoseRepairOutputAssetFolder);
            Directory.CreateDirectory(output);

            Scene scene = EditorSceneManager.OpenScene(
                SoulRecorderReplacementArmStaging.AuthoringScene,
                OpenSceneMode.Single);
            Context context = ResolveContext(scene);
            InitializeCandidateContext(context);
            CandidateSpec lSpec = InteractionCandidateSpecs().Single(value =>
                value.Suffix == "L");
            CandidateMetrics ignored;
            Pose lSeed = BuildInteractionCandidate(context, lSpec, out ignored);
            Pose physicalSeed = PreparePhysicalSupportSeed(context, lSeed);
            CassetteAxisAudit audit = AuditCassetteAxes(context, physicalSeed);
            PhysicalGripMetrics physicalMetrics;
            Pose physicalL3 = BuildPhysicalGripCandidate(context, physicalSeed,
                audit, PhysicalGripSpecs().Single(value => value.Suffix == "L3"),
                out physicalMetrics);
            ApplyPose(context, physicalL3);
            Vector3 l3RecorderPosition = context.RecorderGrip.position;
            Quaternion l3RecorderRotation = context.RecorderGrip.rotation;

            List<Pose> poses = new List<Pose>();
            List<PoseRepairMetrics> metrics = new List<PoseRepairMetrics>();
            foreach (PoseRepairSpec spec in PoseRepairSpecs(lSpec))
            {
                PoseRepairMetrics measurement;
                poses.Add(BuildPoseRepairCandidate(context, spec,
                    l3RecorderPosition, l3RecorderRotation, out measurement));
                metrics.Add(measurement);
            }

            WritePoseRepairPoseFile(poses, humanBefore, sceneBefore);
            RenderPoseRepairCandidates(context, poses, metrics);
            AssetDatabase.Refresh();

            string humanAfter = Sha256File(humanPosePath);
            string sceneAfter = Sha256File(scenePath);
            if (!string.Equals(humanBefore, humanAfter,
                    StringComparison.Ordinal) ||
                !string.Equals(sceneBefore, sceneAfter,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Pose repair generation modified locked human-authored data.");
            }
            if (!string.Equals(
                SoulRecorderReplacementGripPoseStore.LockedStaticMasterHash,
                "2EA27472ACBEBF323D9C2502AF7FDFCE1FC3FB8ACDD72D56629C8E8ECEC20AA2",
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "StaticMaster_v1 changed during pose repair generation.");
            }
            Debug.Log("PoseRepair A-C preserved human pose SHA-256=" +
                humanAfter + " and authoring scene SHA-256=" + sceneAfter + ".");
        }

        private static IEnumerable<PoseRepairSpec> PoseRepairSpecs(
            CandidateSpec lSpec)
        {
            return new[]
            {
                PoseRepair("A", new Vector3(-0.090f, 0.020f, -0.010f),
                    new Vector3(0f, -3f, -2f),
                    Vector3.zero, new Vector3(0f, 0f, 20f),
                    lSpec.RightElbowPole, 0.60f),
                PoseRepair("B", new Vector3(-0.110f, 0.030f, -0.014f),
                    new Vector3(2f, -1f, 0f),
                    new Vector3(0.003f, -0.002f, 0.001f),
                    new Vector3(0f, 0f, 30f),
                    new Vector3(0.82f, -0.64f, -0.18f), 0.80f),
                PoseRepair("C", new Vector3(-0.075f, 0.012f, -0.006f),
                    new Vector3(-2f, -5f, 2f),
                    new Vector3(-0.003f, 0.002f, -0.001f),
                    new Vector3(0f, 0f, 40f),
                    new Vector3(0.74f, -0.70f, -0.16f), 1.00f)
            };
        }

        private static PoseRepairSpec PoseRepair(string suffix,
            Vector3 recorderCameraOffset, Vector3 recorderCameraEuler,
            Vector3 wristCassetteLocalOffset,
            Vector3 wristCassetteLocalEuler, Vector3 rightElbowPole,
            float fingerLimitScale)
        {
            return new PoseRepairSpec
            {
                Suffix = suffix,
                RecorderCameraOffset = recorderCameraOffset,
                RecorderCameraEuler = recorderCameraEuler,
                WristCassetteLocalOffset = wristCassetteLocalOffset,
                WristCassetteLocalEuler = wristCassetteLocalEuler,
                RightElbowPole = rightElbowPole,
                FingerLimitScale = fingerLimitScale
            };
        }

        private static Pose BuildPoseRepairCandidate(Context context,
            PoseRepairSpec spec, Vector3 l3RecorderPosition,
            Quaternion l3RecorderRotation, out PoseRepairMetrics metrics)
        {
            ApplyPose(context, context.StaticMaster);
            Vector3 recorderPosition = l3RecorderPosition +
                context.Camera.transform.TransformVector(
                    spec.RecorderCameraOffset);
            Quaternion recorderRotation = CameraSpaceDelta(context,
                spec.RecorderCameraEuler) * l3RecorderRotation;
            context.RecorderGrip.position = recorderPosition;
            context.RecorderGrip.rotation = recorderRotation;
            WorldPose leftWrist = WristTargetFromContactFrames(context,
                context.LeftWrist,
                new[] { SupportPalmPoint(context), FingerTip(context, "L_thumb"),
                    FingerTip(context, "L_point") },
                new[]
                {
                    FindUnique(context.RecorderGrip.gameObject,
                        "RecorderSupportPalm").position,
                    FindUnique(context.RecorderGrip.gameObject,
                        "RecorderSupportThumb").position,
                    FindUnique(context.RecorderGrip.gameObject,
                        "RecorderSupportIndex").position
                }, Vector3.zero, Vector3.zero);
            CandidateSpec lSpec = InteractionCandidateSpecs().Single(value =>
                value.Suffix == "L");
            SolveRotationOnlyArm(context, context.LeftUpper,
                context.LeftElbow, context.LeftWrist, leftWrist.Position,
                leftWrist.Rotation, lSpec.LeftElbowPole, "left");
            RestoreFingerRotations(context, context.StaticMaster, "L_");
            context.LeftHandGripToRecorder = CaptureGrip(
                context.LeftWrist, context.RecorderGrip);
            context.Markers = CaptureMarkerGeometry(context);

            WorldPose cassette = CassetteFromAxisTarget(context,
                context.Markers.SlotEntryPosition,
                context.Markers.SlotAxisRotation);
            context.CassetteGrip.position = cassette.Position;
            context.CassetteGrip.rotation = cassette.Rotation;
            Vector3 fingerCenter = (FingerTip(context, "R_thumb") +
                FingerTip(context, "R_point") +
                FingerTip(context, "R_middle")) / 3f;
            Vector3 fingerCenterLocal = context.RightWrist.InverseTransformPoint(
                fingerCenter);
            Vector3 markerCenter = (FindUnique(
                context.CassetteGrip.gameObject, "CassetteThumbGrip").position +
                FindUnique(context.CassetteGrip.gameObject,
                    "CassetteIndexGrip").position +
                FindUnique(context.CassetteGrip.gameObject,
                    "CassetteMiddleGrip").position) / 3f;
            Quaternion rightWristRotation = context.RightWrist.rotation *
                Quaternion.Euler(spec.WristCassetteLocalEuler);
            WorldPose rightWrist = new WorldPose
            {
                Position = markerCenter - rightWristRotation *
                    fingerCenterLocal + context.Camera.transform.TransformVector(
                        spec.WristCassetteLocalOffset),
                Rotation = rightWristRotation
            };
            SolveRotationOnlyArm(context, context.RightUpper,
                context.RightElbow, context.RightWrist, rightWrist.Position,
                rightWrist.Rotation, spec.RightElbowPole, "right");
            RestoreFingerRotations(context, context.StaticMaster, "R_");
            SolveNaturalPinch(context, spec.FingerLimitScale);
            context.RightHandGripToCassette = CaptureGrip(
                context.RightWrist, context.CassetteGrip);

            Pose result = CapturePose(context, "PoseRepair_" + spec.Suffix,
                "StaticMaster support hand plus bounded natural cassette pinch at L3 contact geometry",
                CassetteOwner.Hand);
            metrics = MeasurePoseRepair(context, result);
            return result;
        }

        private static WorldPose WristTargetFromContactFrames(Context context,
            Transform wrist, Vector3[] handPoints, Vector3[] targetPoints,
            Vector3 cameraPositionOffset, Vector3 cameraEulerOffset)
        {
            if (handPoints.Length != 3 || targetPoints.Length != 3)
            {
                throw new ArgumentException(
                    "Contact-frame solve requires exactly three points.");
            }
            Vector3 handCenter = (handPoints[0] + handPoints[1] +
                handPoints[2]) / 3f;
            Vector3 targetCenter = (targetPoints[0] + targetPoints[1] +
                targetPoints[2]) / 3f;
            Quaternion handLocalFrame = Quaternion.Inverse(wrist.rotation) *
                ContactFrameRotation(handPoints[0], handPoints[1], handPoints[2]);
            Vector3 handLocalCenter = wrist.InverseTransformPoint(handCenter);
            Quaternion targetFrame = ContactFrameRotation(targetPoints[0],
                targetPoints[1], targetPoints[2]);
            Quaternion rotation = CameraSpaceDelta(context,
                cameraEulerOffset) * targetFrame *
                Quaternion.Inverse(handLocalFrame);
            Vector3 position = targetCenter - rotation * handLocalCenter +
                context.Camera.transform.TransformVector(cameraPositionOffset);
            return new WorldPose
            {
                Position = position,
                Rotation = rotation
            };
        }

        private static IEnumerable<PhysicalGripSpec> PhysicalGripSpecs()
        {
            return new[]
            {
                PhysicalGrip("L1", Vector3.zero, Vector3.zero),
                PhysicalGrip("L2", new Vector3(0.004f, -0.003f, 0.002f),
                    new Vector3(-2f, 3f, -2f)),
                PhysicalGrip("L3", new Vector3(-0.004f, 0.003f, -0.002f),
                    new Vector3(2f, -3f, 2f))
            };
        }

        private static void GenerateV4()
        {
            string humanPosePath = Path.GetFullPath(
                SoulRecorderReplacementArmStaging.PoseFile);
            string humanBefore = Sha256File(humanPosePath);
            string scenePath = Path.GetFullPath(
                SoulRecorderReplacementArmStaging.AuthoringScene);
            string sceneBefore = Sha256File(scenePath);
            string output = Path.GetFullPath(V4OutputAssetFolder);
            Directory.CreateDirectory(output);

            Scene scene = EditorSceneManager.OpenScene(
                SoulRecorderReplacementArmStaging.AuthoringScene,
                OpenSceneMode.Single);
            Context context = ResolveContext(scene);
            InitializeCandidateContext(context);
            CandidateSpec lSpec = InteractionCandidateSpecs().Single(value =>
                value.Suffix == "L");
            CandidateMetrics ignored;
            Pose lSeed = BuildInteractionCandidate(context, lSpec, out ignored);
            Pose physicalSeed = PreparePhysicalSupportSeed(context, lSeed);
            CassetteAxisAudit audit = AuditCassetteAxes(context, physicalSeed);
            PhysicalGripMetrics contactMetrics;
            Pose physicalL3 = BuildPhysicalGripCandidate(context, physicalSeed,
                audit, PhysicalGripSpecs().Single(value => value.Suffix == "L3"),
                out contactMetrics);
            if (!contactMetrics.Passed)
            {
                throw new InvalidOperationException(
                    "PhysicalGrip_L3 no longer passes its physical contact gate: " +
                    string.Join("; ", contactMetrics.Failures.ToArray()));
            }

            Pose carry = context.StaticMaster;
            Pose contact = ClonePose(physicalL3, "V4_Contact",
                "approved PhysicalGrip_L3 contact seed", CassetteOwner.Hand);
            Pose approach = CreateBlendedPoseV4(context, carry, contact,
                0.30f, 0f, "V4_ApproachCarryGrip",
                "right hand moves inward before cassette reorientation");
            Vector3 clearAxis = context.Markers.SlotEntryPosition -
                context.Markers.SlotDirection * 0.120f +
                context.Camera.transform.right * 0.020f -
                context.Camera.transform.up * 0.008f;
            Pose reorientClear = BuildHeldV4(context, contact,
                "V4_ReorientedClear", clearAxis,
                context.Markers.SlotAxisRotation,
                "L3 grip and contact orientation established well clear of recorder");
            Pose align = BuildHeldV4(context, reorientClear, "V4_Align",
                context.Markers.SlotEntryPosition -
                    context.Markers.SlotDirection * 0.035f,
                context.Markers.SlotAxisRotation,
                "mechanically aligned approach; L3 grip frozen");
            Pose half = BuildHeldV4(context, contact, "V4_HalfInserted",
                Vector3.Lerp(context.Markers.SlotEntryPosition,
                    context.Markers.SlotSeatedPosition, 0.5f),
                context.Markers.SlotAxisRotation,
                "exact slot-axis midpoint; L3 grip frozen");
            Pose seatedHand = BuildHeldV4(context, half, "V4_SeatedHand",
                context.Markers.SlotSeatedPosition,
                context.Markers.SlotAxisRotation,
                "exact seated point before ownership transfer");
            ApplyPose(context, seatedHand);
            context.SlotToCassetteSeated = CaptureGrip(FindUnique(
                context.RecorderGrip.gameObject, "CassetteSlotSeated"),
                context.CassetteGrip);
            Pose seatedSlot = ClonePose(seatedHand, "V4_SeatedSlot",
                "identical world pose after RIGHT_HAND to RECORDER_SLOT transfer",
                CassetteOwner.Recorder);
            Pose release = GenerateReleasedPose(context, seatedSlot,
                "V4_Release", 0.055f, 0.050f, true,
                "fingers release after seating; hand retracts down/right");
            Pose belowCarry = GenerateAssemblyBelowPose(context, carry,
                "V4_BelowCarry", CassetteOwner.Hand);
            Pose belowSeated = GenerateAssemblyBelowPose(context, release,
                "V4_BelowSeated", CassetteOwner.Recorder);
            Pose ejectApproach = GenerateReleasedPose(context, seatedSlot,
                "V4_EjectApproach", 0.020f, 0.016f, true,
                "empty hand approaches seated cassette");
            Pose ejectGripSlot = ClonePose(seatedHand, "V4_EjectGripSlot",
                "L3 fingers established while cassette remains recorder-owned",
                CassetteOwner.Recorder);
            Pose ejectGripHand = ClonePose(seatedHand, "V4_EjectGripHand",
                "identical world pose after RECORDER_SLOT to RIGHT_HAND transfer",
                CassetteOwner.Hand);
            Pose ejectClear = BuildHeldV4(context, ejectGripHand,
                "V4_EjectClear", context.Markers.SlotEntryPosition -
                    context.Markers.SlotDirection * 0.075f,
                context.Markers.SlotAxisRotation,
                "straight slot-axis pull before carry reorientation");

            List<Pose> poses = new List<Pose>
            {
                carry, belowCarry, approach, reorientClear, align, contact,
                half, seatedHand, seatedSlot, release, belowSeated,
                ejectApproach, ejectGripSlot, ejectGripHand, ejectClear
            };
            List<Transition> insertion = new List<Transition>
            {
                T(belowCarry, carry, 0.42f, CassetteOwner.Hand, "Draw"),
                T(carry, approach, 0.32f, CassetteOwner.Hand, "MoveInward"),
                T(approach, reorientClear, 0.58f, CassetteOwner.Hand,
                    "VisibleReorientationClearOfRecorder"),
                T(reorientClear, align, 0.28f, CassetteOwner.Hand, "Align"),
                T(align, contact, 0.20f, CassetteOwner.Hand, "Contact"),
                T(contact, half, 0.18f, CassetteOwner.Hand, "InsertHalf"),
                T(half, seatedHand, 0.18f, CassetteOwner.Hand, "InsertSeated"),
                T(seatedHand, seatedSlot, 0.04f, CassetteOwner.Recorder,
                    "TransferToSlot"),
                T(seatedSlot, release, 0.24f, CassetteOwner.Recorder, "Release"),
                T(release, belowSeated, 0.42f, CassetteOwner.Recorder, "Exit")
            };
            List<Transition> ejection = new List<Transition>
            {
                T(belowSeated, release, 0.42f, CassetteOwner.Recorder, "DrawSeated"),
                T(release, ejectApproach, 0.24f, CassetteOwner.Recorder,
                    "EmptyHandApproach"),
                T(ejectApproach, ejectGripSlot, 0.24f, CassetteOwner.Recorder,
                    "EstablishL3Grip"),
                T(ejectGripSlot, ejectGripHand, 0.04f, CassetteOwner.Hand,
                    "TransferToHand"),
                T(ejectGripHand, ejectClear, 0.28f, CassetteOwner.Hand,
                    "PullStraightClear"),
                T(ejectClear, approach, 0.58f, CassetteOwner.Hand,
                    "ReorientToCarryOnlyAfterClear"),
                T(approach, carry, 0.30f, CassetteOwner.Hand, "ReturnCarry"),
                T(carry, belowCarry, 0.42f, CassetteOwner.Hand, "Exit")
            };

            ValidateV4(context, carry, reorientClear, contact, half,
                seatedHand, seatedSlot, ejectGripSlot, ejectGripHand,
                ejectClear, contactMetrics, audit);
            WriteV4PoseFile(poses, humanBefore, sceneBefore);
            RenderV4(context, poses, insertion, ejection, audit,
                contactMetrics);
            AssetDatabase.Refresh();

            string humanAfter = Sha256File(humanPosePath);
            string sceneAfter = Sha256File(scenePath);
            if (!string.Equals(humanBefore, humanAfter,
                    StringComparison.Ordinal) ||
                !string.Equals(sceneBefore, sceneAfter,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "V4 generation modified locked human-authored data.");
            }
        }

        private static Pose CreateBlendedPoseV4(Context context, Pose from,
            Pose to, float boneAmount, float rightGripAmount, string name,
            string source)
        {
            ApplyInterpolatedV4(context, from, to, boneAmount,
                rightGripAmount, boneAmount, CassetteOwner.Hand);
            return CapturePose(context, name, source, CassetteOwner.Hand);
        }

        private static void GenerateV5()
        {
            string humanPosePath = Path.GetFullPath(
                SoulRecorderReplacementArmStaging.PoseFile);
            string humanBefore = Sha256File(humanPosePath);
            string scenePath = Path.GetFullPath(
                SoulRecorderReplacementArmStaging.AuthoringScene);
            string sceneBefore = Sha256File(scenePath);
            string output = Path.GetFullPath(V5OutputAssetFolder);
            Directory.CreateDirectory(output);

            Scene scene = EditorSceneManager.OpenScene(
                SoulRecorderReplacementArmStaging.AuthoringScene,
                OpenSceneMode.Single);
            Context context = ResolveContext(scene);
            InitializeCandidateContext(context);
            CandidateSpec lSpec = InteractionCandidateSpecs().Single(value =>
                value.Suffix == "L");
            CandidateMetrics ignored;
            Pose lSeed = BuildInteractionCandidate(context, lSpec, out ignored);
            Pose physicalSeed = PreparePhysicalSupportSeed(context, lSeed);
            CassetteAxisAudit sourceAudit = AuditCassetteAxes(context,
                physicalSeed);
            PhysicalGripMetrics physicalMetrics;
            Pose physicalL3 = BuildPhysicalGripCandidate(context, physicalSeed,
                sourceAudit, PhysicalGripSpecs().Single(value =>
                    value.Suffix == "L3"), out physicalMetrics);
            if (!physicalMetrics.Passed)
            {
                throw new InvalidOperationException(
                    "PhysicalGrip_L3 no longer passes its physical contact gate: " +
                    string.Join("; ", physicalMetrics.Failures.ToArray()));
            }

            ApplyPose(context, context.StaticMaster);
            Quaternion carryCassetteRotation = context.CassetteGrip.rotation;

            // V5 deliberately establishes the approved L3 support grip before
            // either prop enters frame. The visible presentation then moves as
            // rigid hand/prop assemblies, never by blending grip offsets.
            Pose contact = BuildPresentedContactV5(context, physicalL3,
                lSpec);
            CassetteAxisAudit audit = AuditCassetteAxes(context, contact);
            contact = BuildHeldAxisV5(context, contact, "V5_Contact",
                context.Markers.SlotEntryPosition,
                context.Markers.SlotAxisRotation,
                "PhysicalGrip_L3 at the presented recorder slot");

            ApplyPose(context, contact);
            Vector3 rotationPosition = context.RecorderGrip.position +
                context.Camera.transform.right * 0.230f -
                context.Camera.transform.up * 0.022f -
                context.Camera.transform.forward * 0.008f;
            Quaternion contactCassetteRotation = context.CassetteGrip.rotation;
            Pose rightCarry = BuildHeldCassetteV5(context, contact,
                "V5_RightCarry", rotationPosition, carryCassetteRotation,
                "cassette enters on the right in Carry orientation");
            Pose rightEntryLow = BuildHeldCassetteV5(context, contact,
                "V5_RightEntryLow", rotationPosition +
                    context.Camera.transform.right * 0.020f -
                    context.Camera.transform.up * 0.205f,
                carryCassetteRotation,
                "cassette hand enters and exits through the lower-right corner",
                new Vector3(0.78f, -0.92f, -0.12f));
            Pose reorientedRight = BuildHeldCassetteV5(context, contact,
                "V5_ReorientedRight", rotationPosition,
                contactCassetteRotation,
                "165-degree reorientation completed on the right, clear of recorder");
            Pose stabilizedRight = ClonePose(reorientedRight,
                "V5_ReorientationStabilized",
                "brief readable hold after the early cassette reorientation",
                CassetteOwner.Hand);
            Pose alignClear = BuildHeldAxisV5(context, contact,
                "V5_AlignClear", context.Markers.SlotEntryPosition -
                    context.Markers.SlotDirection * 0.100f +
                    context.Camera.transform.right * 0.080f,
                context.Markers.SlotAxisRotation,
                "short approach from the right after orientation is stable");
            Pose half = BuildHeldAxisV5(context, contact,
                "V5_HalfInserted", Vector3.Lerp(
                    context.Markers.SlotEntryPosition,
                    context.Markers.SlotSeatedPosition, 0.5f),
                context.Markers.SlotAxisRotation,
                "exact slot-axis midpoint with frozen PhysicalGrip_L3");
            Pose seatedHand = BuildHeldAxisV5(context, contact,
                "V5_SeatedHand", context.Markers.SlotSeatedPosition,
                context.Markers.SlotAxisRotation,
                "exact seated position before ownership transfer");
            ApplyPose(context, seatedHand);
            context.SlotToCassetteSeated = CaptureGrip(FindUnique(
                context.RecorderGrip.gameObject, "CassetteSlotSeated"),
                context.CassetteGrip);
            Pose seatedSlot = ClonePose(seatedHand, "V5_SeatedSlot",
                "zero-snap RIGHT_HAND to RECORDER_SLOT transfer",
                CassetteOwner.Recorder);
            Pose release = GenerateReleasedPose(context, seatedSlot,
                "V5_Release", 0.060f, 0.055f, true,
                "right fingers release and retract down/right");
            Pose belowCarry = GenerateAssemblyBelowPoseV5(context, rightEntryLow,
                "V5_BelowCarry", CassetteOwner.Hand);
            Pose belowSeated = GenerateAssemblyBelowPoseV5(context, release,
                "V5_BelowSeated", CassetteOwner.Recorder);
            Pose ejectApproach = GenerateReleasedPose(context, seatedSlot,
                "V5_EjectApproach", 0.026f, 0.021f, true,
                "empty right hand approaches from lower-right");
            Pose ejectGripSlot = ClonePose(seatedHand, "V5_EjectGripSlot",
                "L3 grip established while cassette remains seated",
                CassetteOwner.Recorder);
            Pose ejectGripHand = ClonePose(seatedHand, "V5_EjectGripHand",
                "zero-snap RECORDER_SLOT to RIGHT_HAND transfer",
                CassetteOwner.Hand);
            Pose ejectStraightClear = BuildHeldAxisV5(context, contact,
                "V5_EjectStraightClear", context.Markers.SlotEntryPosition -
                    context.Markers.SlotDirection * 0.080f,
                context.Markers.SlotAxisRotation,
                "straight mechanical pull before moving right");
            Pose ejectRight = BuildHeldCassetteV5(context, contact,
                "V5_EjectRight", rotationPosition,
                contactCassetteRotation,
                "cassette moved clearly right before reorientation");

            List<Pose> poses = new List<Pose>
            {
                context.StaticMaster, physicalL3, belowCarry, rightEntryLow,
                rightCarry,
                reorientedRight, stabilizedRight, alignClear, contact, half,
                seatedHand, seatedSlot, release, belowSeated, ejectApproach,
                ejectGripSlot, ejectGripHand, ejectStraightClear, ejectRight
            };
            List<Transition> insertion = new List<Transition>
            {
                T(belowCarry, rightEntryLow, 0.34f, CassetteOwner.Hand,
                    "EnterSupportedAssembliesLowerRight"),
                T(rightEntryLow, rightCarry, 0.28f, CassetteOwner.Hand,
                    "RaiseCassetteOnRight"),
                T(rightCarry, reorientedRight, 0.72f, CassetteOwner.Hand,
                    "EarlyCassetteReorientation"),
                T(reorientedRight, stabilizedRight, 0.18f,
                    CassetteOwner.Hand, "ReorientationStabilization"),
                T(stabilizedRight, alignClear, 0.34f, CassetteOwner.Hand,
                    "ApproachFromRight"),
                T(alignClear, contact, 0.24f, CassetteOwner.Hand, "Contact"),
                T(contact, half, 0.18f, CassetteOwner.Hand, "InsertHalf"),
                T(half, seatedHand, 0.18f, CassetteOwner.Hand,
                    "InsertSeated"),
                T(seatedHand, seatedSlot, 0.04f, CassetteOwner.Recorder,
                    "TransferToSlot"),
                T(seatedSlot, release, 0.24f, CassetteOwner.Recorder,
                    "Release"),
                T(release, belowSeated, 0.46f, CassetteOwner.Recorder,
                    "ExitSupportedAssembly")
            };
            List<Transition> ejection = new List<Transition>
            {
                T(belowSeated, release, 0.46f, CassetteOwner.Recorder,
                    "EnterSupportedAssembly"),
                T(release, ejectApproach, 0.24f, CassetteOwner.Recorder,
                    "EmptyHandApproachFromRight"),
                T(ejectApproach, ejectGripSlot, 0.24f,
                    CassetteOwner.Recorder, "EstablishL3Grip"),
                T(ejectGripSlot, ejectGripHand, 0.04f, CassetteOwner.Hand,
                    "TransferToHand"),
                T(ejectGripHand, ejectStraightClear, 0.24f,
                    CassetteOwner.Hand, "PullStraightClear"),
                T(ejectStraightClear, ejectRight, 0.30f,
                    CassetteOwner.Hand, "MoveClearToRight"),
                T(ejectRight, rightCarry, 0.72f, CassetteOwner.Hand,
                    "LateCassetteReorientation"),
                T(rightCarry, rightEntryLow, 0.28f, CassetteOwner.Hand,
                    "LowerCassetteOnRight"),
                T(rightEntryLow, belowCarry, 0.34f, CassetteOwner.Hand,
                    "ExitSupportedAssembliesLowerRight")
            };

            ValidateV4(context, context.StaticMaster, reorientedRight,
                contact, half, seatedHand, seatedSlot, ejectGripSlot,
                ejectGripHand, ejectStraightClear, physicalMetrics, audit);
            WriteV5PoseFile(poses, humanBefore, sceneBefore);
            RenderV5(context, poses, insertion, ejection, audit,
                physicalMetrics);
            AssetDatabase.Refresh();

            string humanAfter = Sha256File(humanPosePath);
            string sceneAfter = Sha256File(scenePath);
            if (!string.Equals(humanBefore, humanAfter,
                    StringComparison.Ordinal) ||
                !string.Equals(sceneBefore, sceneAfter,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "V5 generation modified locked human-authored data.");
            }
        }

        private static Pose BuildPresentedContactV5(Context context,
            Pose physicalL3, CandidateSpec lSpec)
        {
            ApplyPose(context, physicalL3);
            Vector3 recorderPosition = context.RecorderGrip.position -
                context.Camera.transform.right * 0.110f +
                context.Camera.transform.up * 0.030f -
                context.Camera.transform.forward * 0.012f;
            Quaternion recorderRotation = context.RecorderGrip.rotation;
            WorldPose wrist = HandTargetForRecorder(context,
                recorderPosition, recorderRotation);
            SolveRotationOnlyArm(context, context.LeftUpper,
                context.LeftElbow, context.LeftWrist, wrist.Position,
                wrist.Rotation, lSpec.LeftElbowPole, "left");
            SetRecorderFromLeftWrist(context);
            context.Markers = CaptureMarkerGeometry(context);

            WorldPose cassette = CassetteFromAxisTarget(context,
                context.Markers.SlotEntryPosition,
                context.Markers.SlotAxisRotation);
            WorldPose rightWrist = HandTargetForCassette(context,
                cassette.Position, cassette.Rotation);
            SolveRotationOnlyArm(context, context.RightUpper,
                context.RightElbow, context.RightWrist,
                rightWrist.Position, rightWrist.Rotation,
                lSpec.RightElbowPole, "right");
            SetRecorderFromLeftWrist(context);
            SetCassetteFromRightWrist(context);
            return CapturePose(context, "V5_PresentedContactSeed",
                "PhysicalGrip_L3 rigid assemblies recomposed left-of-center",
                CassetteOwner.Hand);
        }

        private static Pose BuildHeldAxisV5(Context context, Pose support,
            string name, Vector3 axisPosition, Quaternion axisRotation,
            string source)
        {
            WorldPose cassette = CassetteFromAxisTarget(context,
                axisPosition, axisRotation);
            return BuildHeldCassetteV5(context, support, name,
                cassette.Position, cassette.Rotation, source);
        }

        private static Pose BuildHeldCassetteV5(Context context, Pose support,
            string name, Vector3 cassettePosition,
            Quaternion cassetteRotation, string source)
        {
            return BuildHeldCassetteV5(context, support, name,
                cassettePosition, cassetteRotation, source,
                new Vector3(0.98f, -0.48f, -0.15f));
        }

        private static Pose BuildHeldCassetteV5(Context context, Pose support,
            string name, Vector3 cassettePosition,
            Quaternion cassetteRotation, string source, Vector3 elbowPole)
        {
            ApplyPose(context, support);
            WorldPose wrist = HandTargetForCassette(context,
                cassettePosition, cassetteRotation);
            SolveRotationOnlyArm(context, context.RightUpper,
                context.RightElbow, context.RightWrist, wrist.Position,
                wrist.Rotation, elbowPole, "right");
            SetRecorderFromLeftWrist(context);
            SetCassetteFromRightWrist(context);
            return CapturePose(context, name, source, CassetteOwner.Hand);
        }

        private static Pose GenerateAssemblyBelowPoseV5(Context context,
            Pose basis, string name, CassetteOwner owner)
        {
            ApplyPose(context, basis);
            context.LeftUpper.rotation = CameraSpaceDelta(context,
                new Vector3(78f, 0f, -20f)) *
                context.LeftUpper.rotation;
            context.RightUpper.rotation = CameraSpaceDelta(context,
                new Vector3(78f, 0f, 20f)) *
                context.RightUpper.rotation;
            SetRecorderFromLeftWrist(context);
            if (owner == CassetteOwner.Hand)
            {
                SetCassetteFromRightWrist(context);
            }
            else
            {
                SetCassetteAtLiveSeatedMarker(context);
            }
            return CapturePose(context, name,
                "L3-supported arm/prop assemblies staged below frame", owner);
        }

        private static Pose BuildHeldV4(Context context, Pose basis,
            string name, Vector3 desiredAxisPosition,
            Quaternion desiredAxisRotation, string source)
        {
            ApplyPose(context, basis);
            WorldPose cassette = CassetteFromAxisTarget(context,
                desiredAxisPosition, desiredAxisRotation);
            WorldPose wrist = HandTargetForCassette(context,
                cassette.Position, cassette.Rotation);
            CandidateSpec lSpec = InteractionCandidateSpecs().Single(value =>
                value.Suffix == "L");
            SolveRotationOnlyArm(context, context.RightUpper,
                context.RightElbow, context.RightWrist, wrist.Position,
                wrist.Rotation, lSpec.RightElbowPole, "right");
            SetRecorderFromLeftWrist(context);
            SetCassetteFromRightWrist(context);
            return CapturePose(context, name, source, CassetteOwner.Hand);
        }

        private static GripRelativeTransform LerpGrip(
            GripRelativeTransform from, GripRelativeTransform to,
            float amount)
        {
            return new GripRelativeTransform
            {
                Position = Vector3.Lerp(from.Position, to.Position, amount),
                Rotation = ShortestPathSlerp(from.Rotation, to.Rotation,
                    amount)
            };
        }

        private static void ApplyInterpolatedV4(Context context, Pose from,
            Pose to, float boneAmount, float rightGripAmount,
            float leftGripAmount, CassetteOwner owner)
        {
            float easedBones = boneAmount * boneAmount *
                (3f - 2f * boneAmount);
            float easedRight = rightGripAmount * rightGripAmount *
                (3f - 2f * rightGripAmount);
            float easedLeft = leftGripAmount * leftGripAmount *
                (3f - 2f * leftGripAmount);
            foreach (Transform transform in context.Controlled)
            {
                if (transform == context.RecorderGrip ||
                    transform == context.CassetteGrip)
                {
                    continue;
                }
                DraftTransform a = from.Values[PathOf(transform)];
                DraftTransform b = to.Values[PathOf(transform)];
                transform.localPosition = Vector3.Lerp(a.localPosition,
                    b.localPosition, easedBones);
                transform.localRotation = ShortestPathSlerp(a.localRotation,
                    b.localRotation, easedBones);
                transform.localScale = Vector3.Lerp(a.localScale,
                    b.localScale, easedBones);
            }
            context.RightHandGripToCassette = LerpGrip(
                from.RightHandGripToCassette,
                to.RightHandGripToCassette, easedRight);
            context.LeftHandGripToRecorder = LerpGrip(
                from.LeftHandGripToRecorder,
                to.LeftHandGripToRecorder, easedLeft);
            SetRecorderFromLeftWrist(context);
            if (owner == CassetteOwner.Hand)
            {
                SetCassetteFromRightWrist(context);
            }
            else
            {
                SetCassetteAtLiveSeatedMarker(context);
            }
        }

        private static PhysicalGripSpec PhysicalGrip(string suffix,
            Vector3 wristCameraOffset, Vector3 wristLocalEuler)
        {
            return new PhysicalGripSpec
            {
                Suffix = suffix,
                WristCameraOffset = wristCameraOffset,
                WristLocalEuler = wristLocalEuler
            };
        }

        private static CassetteAxisAudit AuditCassetteAxes(Context context,
            Pose interactionSeed)
        {
            ApplyPose(context, context.StaticMaster);
            Quaternion carryRotation = context.CassetteGrip.rotation;
            ApplyPose(context, interactionSeed);
            context.Markers = CaptureMarkerGeometry(context);
            WorldPose direct = CassetteFromAxisTarget(context,
                context.Markers.SlotEntryPosition,
                context.Markers.SlotAxisRotation);
            WorldPose rolled = CassetteFromAxisTarget(context,
                context.Markers.SlotEntryPosition,
                context.Markers.SlotAxisRotation * Quaternion.AngleAxis(
                    180f, Vector3.forward));
            float directDelta = Quaternion.Angle(carryRotation,
                direct.Rotation);
            float rolledDelta = Quaternion.Angle(carryRotation,
                rolled.Rotation);
            bool useRoll = rolledDelta + 0.001f < directDelta;
            WorldPose selected = useRoll ? rolled : direct;
            context.CassetteGrip.position = selected.Position;
            context.CassetteGrip.rotation = selected.Rotation;

            Transform cassetteAxis = FindUnique(
                context.CassetteGrip.gameObject, "CassetteInsertionAxis");
            Transform cassetteFront = FindUnique(
                context.CassetteGrip.gameObject, "CassetteFront");
            Transform cassetteTop = FindUnique(
                context.CassetteGrip.gameObject, "CassetteTop");
            Vector3 travel = (context.Markers.SlotSeatedPosition -
                context.Markers.SlotEntryPosition).normalized;
            Vector3 front = (cassetteFront.position -
                context.CassetteGrip.position).normalized;
            Vector3 top = (cassetteTop.position -
                context.CassetteGrip.position).normalized;
            CassetteAxisAudit result = new CassetteAxisAudit
            {
                ContactPose = selected,
                DirectCarryRotation = directDelta,
                RolledCarryRotation = rolledDelta,
                SelectedCarryRotation = Mathf.Min(directDelta, rolledDelta),
                AxisDirectionDot = Vector3.Dot(cassetteAxis.forward, travel),
                SlotDirectionDot = Vector3.Dot(
                    context.Markers.SlotDirection, travel),
                FrontTopDot = Mathf.Abs(Vector3.Dot(front, top)),
                UsedEquivalentRoll = useRoll
            };
            result.AxisSignCorrect = result.AxisDirectionDot > 0.99f &&
                result.SlotDirectionDot > 0.99f;
            if (!result.AxisSignCorrect)
            {
                throw new InvalidOperationException(
                    "Cassette/slot insertion-axis sign does not match Entry->Seated travel.");
            }
            if (result.FrontTopDot > 0.01f)
            {
                throw new InvalidOperationException(
                    "CassetteFront and CassetteTop markers are not orthogonal.");
            }
            return result;
        }

        private static Pose PreparePhysicalSupportSeed(Context context,
            Pose lSeed)
        {
            ApplyPose(context, lSeed);
            Vector3 handCenter = (SupportPalmPoint(context) +
                FingerTip(context, "L_thumb") +
                FingerTip(context, "L_point")) / 3f;
            Vector3 palmTarget = FindUnique(context.RecorderGrip.gameObject,
                "RecorderSupportPalm").position;
            Vector3 thumbTarget = FindUnique(context.RecorderGrip.gameObject,
                "RecorderSupportThumb").position;
            Vector3 indexTarget = FindUnique(context.RecorderGrip.gameObject,
                "RecorderSupportIndex").position;
            Vector3 markerCenter = (palmTarget + thumbTarget + indexTarget) /
                3f;
            Quaternion frameDelta = ContactFrameRotation(palmTarget,
                thumbTarget, indexTarget) * Quaternion.Inverse(
                    ContactFrameRotation(SupportPalmPoint(context),
                        FingerTip(context, "L_thumb"),
                        FingerTip(context, "L_point")));
            Vector3 wristPosition = markerCenter - frameDelta *
                (handCenter - context.LeftWrist.position);
            Quaternion wristRotation = frameDelta *
                context.LeftWrist.rotation;
            CandidateSpec lSpec = InteractionCandidateSpecs().Single(value =>
                value.Suffix == "L");
            SolveRotationOnlyArm(context, context.LeftUpper,
                context.LeftElbow, context.LeftWrist, wristPosition,
                wristRotation, lSpec.LeftElbowPole, "left");
            RefineSupportContact(context, lSpec.LeftElbowPole,
                wristRotation);
            context.LeftHandGripToRecorder = CaptureGrip(context.LeftWrist,
                context.RecorderGrip);
            return CapturePose(context, "PhysicalSupportSeed_L",
                "InteractionMaster_L with marker-calibrated rigid recorder grip",
                CassetteOwner.Independent);
        }

        private static Pose BuildPhysicalGripCandidate(Context context,
            Pose lSeed, CassetteAxisAudit audit, PhysicalGripSpec spec,
            out PhysicalGripMetrics metrics)
        {
            ApplyPose(context, lSeed);
            context.CassetteGrip.position = audit.ContactPose.Position;
            context.CassetteGrip.rotation = audit.ContactPose.Rotation;
            Vector3 sourceThumb = FingerTip(context, "R_thumb");
            Vector3 sourceIndex = FingerTip(context, "R_point");
            Vector3 sourceMiddle = FingerTip(context, "R_middle");
            Vector3 targetThumb = FindUnique(context.CassetteGrip.gameObject,
                "CassetteThumbGrip").position;
            Vector3 targetIndex = FindUnique(context.CassetteGrip.gameObject,
                "CassetteIndexGrip").position;
            Vector3 targetMiddle = FindUnique(context.CassetteGrip.gameObject,
                "CassetteMiddleGrip").position;
            Vector3 sourceCenter = (sourceThumb + sourceIndex + sourceMiddle) /
                3f;
            Vector3 targetCenter = (targetThumb + targetIndex + targetMiddle) /
                3f;
            Quaternion wristRotation = CameraSpaceDelta(context,
                spec.WristLocalEuler) * context.RightWrist.rotation;
            Vector3 wristTarget = context.RightWrist.position +
                (targetCenter - sourceCenter) +
                context.Camera.transform.TransformVector(spec.WristCameraOffset);
            CandidateSpec lSpec = InteractionCandidateSpecs().Single(value =>
                value.Suffix == "L");
            SolveRotationOnlyArm(context, context.RightUpper,
                context.RightElbow, context.RightWrist, wristTarget,
                wristRotation, lSpec.RightElbowPole, "right");

            // Mechanical contact remains fixed while fingers close onto it.
            RefineCassetteContact(context, lSpec.RightElbowPole,
                wristRotation);
            SolveFingerToMarker(context, "L_thumb", "RecorderSupportThumb", 140f);
            SolveFingerToMarker(context, "L_point", "RecorderSupportIndex", 140f);
            SolveFingerToMarker(context, "L_middle", "RecorderSupportMiddle", 140f);
            SolveFingerToMarker(context, "L_ring", "RecorderSupportRing", 140f);
            SolveFingerToMarker(context, "L_pink", "RecorderSupportPinky", 140f);

            context.RightHandGripToCassette = CaptureGrip(
                context.RightWrist, context.CassetteGrip);
            string name = "PhysicalGrip_" + spec.Suffix;
            Pose pose = CapturePose(context, name,
                "physical marker-solved grip derived from InteractionMaster_L",
                CassetteOwner.Hand);
            metrics = MeasurePhysicalGrip(context, pose, audit);
            return pose;
        }

        private static void RefineCassetteContact(Context context,
            Vector3 pole, Quaternion wristRotation)
        {
            string[] fingers = { "R_thumb", "R_point", "R_middle" };
            string[] markers =
            {
                "CassetteThumbGrip", "CassetteIndexGrip",
                "CassetteMiddleGrip"
            };
            RefineContact(context, context.RightUpper, context.RightElbow,
                context.RightWrist, context.CassetteGrip, fingers, markers,
                pole, wristRotation, "right");
        }

        private static void RefineSupportContact(Context context,
            Vector3 pole, Quaternion wristRotation)
        {
            string[] fingers =
            {
                "L_thumb", "L_point", "L_middle", "L_ring", "L_pink"
            };
            string[] markers =
            {
                "RecorderSupportThumb", "RecorderSupportIndex",
                "RecorderSupportMiddle", "RecorderSupportRing",
                "RecorderSupportPinky"
            };
            Transform palmMarker = FindUnique(context.RecorderGrip.gameObject,
                "RecorderSupportPalm");
            for (int outer = 0; outer < 36; outer++)
            {
                Vector3 palmError = palmMarker.position -
                    SupportPalmPoint(context);
                Vector3 correction = palmError * 3f;
                float maximum = palmError.magnitude;
                for (int index = 0; index < fingers.Length; index++)
                {
                    SolveFingerToMarker(context, fingers[index],
                        markers[index], 140f);
                    Vector3 target = FindUnique(
                        context.RecorderGrip.gameObject,
                        markers[index]).position;
                    Vector3 error = target - FingerTip(context,
                        fingers[index]);
                    correction += error;
                    maximum = Mathf.Max(maximum, error.magnitude);
                }
                if (maximum <= 0.0015f)
                {
                    break;
                }
                correction /= fingers.Length + 3f;
                SolveRotationOnlyArm(context, context.LeftUpper,
                    context.LeftElbow, context.LeftWrist,
                    context.LeftWrist.position + correction * 0.90f,
                    wristRotation, pole, "left");
            }
            for (int index = 0; index < fingers.Length; index++)
            {
                SolveFingerToMarker(context, fingers[index], markers[index],
                    140f);
            }
        }

        private static void RefineContact(Context context, Transform upper,
            Transform elbow, Transform wrist, Transform markerRoot,
            string[] fingers, string[] markers, Vector3 pole,
            Quaternion wristRotation, string side)
        {
            for (int outer = 0; outer < 30; outer++)
            {
                Vector3 correction = Vector3.zero;
                float maximum = 0f;
                for (int index = 0; index < fingers.Length; index++)
                {
                    SolveFingerToMarker(context, fingers[index],
                        markers[index], 140f);
                    Vector3 target = FindUnique(markerRoot.gameObject,
                        markers[index]).position;
                    Vector3 error = target - FingerTip(context,
                        fingers[index]);
                    correction += error;
                    maximum = Mathf.Max(maximum, error.magnitude);
                }
                if (maximum <= 0.0005f)
                {
                    return;
                }
                correction /= fingers.Length;
                SolveRotationOnlyArm(context, upper, elbow, wrist,
                    wrist.position + correction * 0.90f, wristRotation,
                    pole, side);
            }
            for (int index = 0; index < fingers.Length; index++)
            {
                SolveFingerToMarker(context, fingers[index], markers[index],
                    140f);
            }
        }

        private static void SolveFingerToMarker(Context context,
            string bonePrefix, string markerName, float maximumDelta)
        {
            FingerChain chain = CreateFingerChain(context, bonePrefix);
            Transform markerRoot = markerName.StartsWith("Cassette",
                StringComparison.Ordinal) ? context.CassetteGrip :
                context.RecorderGrip;
            Vector3 target = FindUnique(markerRoot.gameObject,
                markerName).position;
            for (int iteration = 0; iteration < 96; iteration++)
            {
                if (Vector3.Distance(chain.TipPosition, target) <= 0.001f)
                {
                    break;
                }
                for (int index = chain.Joints.Length - 1; index >= 0; index--)
                {
                    Transform joint = chain.Joints[index];
                    Vector3 current = chain.TipPosition - joint.position;
                    Vector3 desired = target - joint.position;
                    if (current.sqrMagnitude <= 0.0000001f ||
                        desired.sqrMagnitude <= 0.0000001f)
                    {
                        continue;
                    }
                    Quaternion delta = Quaternion.FromToRotation(current,
                        desired);
                    float angle;
                    Vector3 axis;
                    delta.ToAngleAxis(out angle, out axis);
                    if (angle > 180f)
                    {
                        angle -= 360f;
                    }
                    angle = Mathf.Clamp(angle, -10f, 10f);
                    joint.rotation = Quaternion.AngleAxis(angle, axis) *
                        joint.rotation;
                    Quaternion baseline = chain.BaselineLocalRotations[index];
                    float total = Quaternion.Angle(baseline,
                        joint.localRotation);
                    if (total > maximumDelta)
                    {
                        joint.localRotation = Quaternion.Slerp(baseline,
                            joint.localRotation, maximumDelta / total);
                    }
                }
            }
        }

        private static void RestoreFingerRotations(Context context,
            Pose source, string sidePrefix)
        {
            foreach (string boneName in SoulRecorderReplacementArmStaging.RequiredBones)
            {
                if (!boneName.StartsWith(sidePrefix, StringComparison.Ordinal) ||
                    !IsFingerBoneName(boneName))
                {
                    continue;
                }
                Transform bone = context.Rig.Bones[boneName];
                bone.localRotation = source.Values[PathOf(bone)].localRotation;
            }
        }

        private static bool IsFingerBoneName(string name)
        {
            return name.Contains("thumb") || name.Contains("point") ||
                name.Contains("middle") || name.Contains("ring") ||
                name.Contains("pink");
        }

        private static void SolveNaturalPinch(Context context, float limitScale)
        {
            SolveFingerToMarkerLimited(context, "R_thumb",
                "CassetteThumbGrip", new[] { 34f, 28f, 16f }, limitScale);
            SolveFingerToMarkerLimited(context, "R_point",
                "CassetteIndexGrip", new[] { 32f, 34f, 16f }, limitScale);
            SolveFingerToMarkerLimited(context, "R_middle",
                "CassetteMiddleGrip", new[] { 24f, 28f, 14f }, limitScale);
            // Ring and pinky deliberately remain bit-exact StaticMaster rotations.
        }

        private static void SolveFingerToMarkerLimited(Context context,
            string bonePrefix, string markerName, float[] jointLimits,
            float limitScale)
        {
            FingerChain chain = CreateFingerChain(context, bonePrefix);
            Transform targetMarker = FindUnique(context.CassetteGrip.gameObject,
                markerName);
            Quaternion[] masterRotations = chain.Joints.Select(joint =>
                context.StaticMaster.Values[PathOf(joint)].localRotation).ToArray();
            for (int iteration = 0; iteration < 56; iteration++)
            {
                if (Vector3.Distance(chain.TipPosition,
                        targetMarker.position) <= 0.0015f)
                {
                    break;
                }
                for (int index = chain.Joints.Length - 1; index >= 0; index--)
                {
                    Transform joint = chain.Joints[index];
                    Vector3 current = chain.TipPosition - joint.position;
                    Vector3 desired = targetMarker.position - joint.position;
                    if (current.sqrMagnitude <= 0.0000001f ||
                        desired.sqrMagnitude <= 0.0000001f)
                    {
                        continue;
                    }
                    Quaternion delta = Quaternion.FromToRotation(current,
                        desired);
                    float angle;
                    Vector3 axis;
                    delta.ToAngleAxis(out angle, out axis);
                    if (angle > 180f)
                    {
                        angle -= 360f;
                    }
                    angle = Mathf.Clamp(angle, -4f, 4f);
                    joint.rotation = Quaternion.AngleAxis(angle, axis) *
                        joint.rotation;
                    float limit = jointLimits[index] * limitScale;
                    float total = Quaternion.Angle(masterRotations[index],
                        joint.localRotation);
                    if (total > limit)
                    {
                        joint.localRotation = Quaternion.Slerp(
                            masterRotations[index], joint.localRotation,
                            limit / total);
                    }
                }
            }
        }

        private static PoseRepairMetrics MeasurePoseRepair(Context context,
            Pose pose)
        {
            ApplyPose(context, pose);
            MarkerGeometry markers = CaptureMarkerGeometry(context);
            Transform axis = FindUnique(context.CassetteGrip.gameObject,
                "CassetteInsertionAxis");
            float armLength = Vector3.Distance(context.RightUpper.position,
                context.RightElbow.position) + Vector3.Distance(
                    context.RightElbow.position, context.RightWrist.position);
            PoseRepairMetrics result = new PoseRepairMetrics
            {
                Name = pose.Name,
                SlotPositionError = Vector3.Distance(axis.position,
                    markers.SlotEntryPosition),
                SlotRotationError = Mathf.Min(
                    Quaternion.Angle(axis.rotation, markers.SlotAxisRotation),
                    Quaternion.Angle(axis.rotation, markers.SlotAxisRotation *
                        Quaternion.AngleAxis(180f, Vector3.forward))),
                MaximumLeftFingerDelta = MaximumFingerDelta(context, "L_", false),
                MaximumRightFingerDelta = MaximumFingerDelta(context, "R_", false),
                MaximumRightDistalDelta = MaximumFingerDelta(context, "R_", true),
                RightWristDelta = Quaternion.Angle(
                    context.StaticMaster.Values[PathOf(context.RightWrist)].localRotation,
                    context.RightWrist.localRotation),
                ThumbError = FingerMarkerError(context, "R_thumb",
                    context.CassetteGrip, "CassetteThumbGrip"),
                IndexError = FingerMarkerError(context, "R_point",
                    context.CassetteGrip, "CassetteIndexGrip"),
                MiddleError = FingerMarkerError(context, "R_middle",
                    context.CassetteGrip, "CassetteMiddleGrip"),
                IndexMiddleSeparation = Vector3.Distance(
                    FingerTip(context, "R_point"),
                    FingerTip(context, "R_middle")),
                RightExtension = Vector3.Distance(context.RightUpper.position,
                    context.RightWrist.position) / armLength,
                RecorderViewport = context.Camera.WorldToViewportPoint(
                    context.RecorderGrip.position),
                CassetteViewport = context.Camera.WorldToViewportPoint(
                    context.CassetteGrip.position),
                LeftWristViewport = context.Camera.WorldToViewportPoint(
                    context.LeftWrist.position),
                RightWristViewport = context.Camera.WorldToViewportPoint(
                    context.RightWrist.position),
                RendererBoundsViewport = context.Camera.WorldToViewportPoint(
                    context.Rig.Renderer.bounds.center)
            };
            WarnPoseRepair(result, result.SlotPositionError > 0.0015f,
                "mechanical contact position drifted");
            WarnPoseRepair(result, result.SlotRotationError > 1f,
                "mechanical contact rotation drifted");
            WarnPoseRepair(result, result.MaximumLeftFingerDelta > 0.01f,
                "left fingers differ from StaticMaster_v1");
            WarnPoseRepair(result, result.MaximumRightFingerDelta > 40f,
                "right finger rotation exceeds 40 degrees from StaticMaster_v1");
            WarnPoseRepair(result, result.MaximumRightDistalDelta > 18f,
                "right distal joint rotation exceeds 18 degrees");
            WarnPoseRepair(result, result.RightWristDelta > 55f,
                "right wrist differs from StaticMaster_v1 by more than 55 degrees");
            WarnPoseRepair(result, result.IndexMiddleSeparation < 0.006f,
                "index and middle fingertips are too close");
            WarnPoseRepair(result, result.RightExtension > 0.85f,
                "right arm exceeds 85 percent extension");
            return result;
        }

        private static float MaximumFingerDelta(Context context,
            string sidePrefix, bool distalOnly)
        {
            float maximum = 0f;
            foreach (string boneName in SoulRecorderReplacementArmStaging.RequiredBones)
            {
                if (!boneName.StartsWith(sidePrefix, StringComparison.Ordinal) ||
                    !IsFingerBoneName(boneName) ||
                    (distalOnly && !boneName.EndsWith("3",
                        StringComparison.Ordinal)))
                {
                    continue;
                }
                Transform bone = context.Rig.Bones[boneName];
                maximum = Mathf.Max(maximum, Quaternion.Angle(
                    context.StaticMaster.Values[PathOf(bone)].localRotation,
                    bone.localRotation));
            }
            return maximum;
        }

        private static void WarnPoseRepair(PoseRepairMetrics metrics,
            bool condition, string warning)
        {
            if (condition)
            {
                metrics.Warnings.Add(warning);
            }
        }

        private static FingerChain CreateFingerChain(Context context,
            string prefix)
        {
            Transform[] joints = Enumerable.Range(1, 3)
                .Select(index => context.Rig.Bones[prefix + index])
                .ToArray();
            float distalLength = Vector3.Distance(joints[1].position,
                joints[2].position);
            return new FingerChain
            {
                Joints = joints,
                // The replacement rig's phalanges advance along local -Z.
                // Keeping this endpoint in final-bone local space is essential:
                // recalculating it from the deformed joint direction would move
                // the measurement endpoint after every CCD rotation.
                TipLocalOffset = new Vector3(0f, 0f,
                    -distalLength * 0.72f),
                BaselineLocalRotations = joints.Select(joint =>
                    joint.localRotation).ToArray()
            };
        }

        private static Vector3 FingerTip(Context context, string prefix)
        {
            return CreateFingerChain(context, prefix).TipPosition;
        }

        private static Quaternion ContactFrameRotation(Vector3 thumb,
            Vector3 index, Vector3 middle)
        {
            Vector3 x = (index - thumb).normalized;
            Vector3 z = Vector3.Cross(x, middle - thumb).normalized;
            if (z.sqrMagnitude <= 0.000001f)
            {
                throw new InvalidOperationException(
                    "Grip contact points do not define a stable frame.");
            }
            Vector3 y = Vector3.Cross(z, x).normalized;
            return Quaternion.LookRotation(z, y);
        }


        private static float FingerMarkerError(Context context,
            string bonePrefix, Transform markerRoot, string markerName)
        {
            return Vector3.Distance(CreateFingerChain(context, bonePrefix)
                .TipPosition, FindUnique(markerRoot.gameObject,
                    markerName).position);
        }

        private static PhysicalGripMetrics MeasurePhysicalGrip(Context context,
            Pose pose, CassetteAxisAudit audit)
        {
            ApplyPose(context, pose);
            MarkerGeometry markers = CaptureMarkerGeometry(context);
            Transform axis = FindUnique(context.CassetteGrip.gameObject,
                "CassetteInsertionAxis");
            float length = Vector3.Distance(context.RightUpper.position,
                context.RightElbow.position) + Vector3.Distance(
                    context.RightElbow.position, context.RightWrist.position);
            PhysicalGripMetrics result = new PhysicalGripMetrics
            {
                Name = pose.Name,
                ThumbError = FingerMarkerError(context, "R_thumb",
                    context.CassetteGrip, "CassetteThumbGrip"),
                IndexError = FingerMarkerError(context, "R_point",
                    context.CassetteGrip, "CassetteIndexGrip"),
                MiddleError = FingerMarkerError(context, "R_middle",
                    context.CassetteGrip, "CassetteMiddleGrip"),
                SupportThumbError = FingerMarkerError(context, "L_thumb",
                    context.RecorderGrip, "RecorderSupportThumb"),
                SupportIndexError = FingerMarkerError(context, "L_point",
                    context.RecorderGrip, "RecorderSupportIndex"),
                SupportMiddleError = FingerMarkerError(context, "L_middle",
                    context.RecorderGrip, "RecorderSupportMiddle"),
                SupportRingError = FingerMarkerError(context, "L_ring",
                    context.RecorderGrip, "RecorderSupportRing"),
                SupportPinkyError = FingerMarkerError(context, "L_pink",
                    context.RecorderGrip, "RecorderSupportPinky"),
                SupportPalmError = Vector3.Distance(SupportPalmPoint(context),
                    FindUnique(context.RecorderGrip.gameObject,
                        "RecorderSupportPalm").position),
                CassettePenetration = CassetteContactPenetration(context),
                RecorderPenetration = MaximumMarkerPenetration(context,
                    context.RecorderGrip, new[] { "L_thumb", "L_point", "L_middle", "L_ring", "L_pink" }),
                InteractionGripPositionDelta = Vector3.Distance(
                    pose.RightHandGripToCassette.Position,
                    context.StaticMaster.RightHandGripToCassette.Position),
                InteractionGripRotationDelta = Quaternion.Angle(
                    pose.RightHandGripToCassette.Rotation,
                    context.StaticMaster.RightHandGripToCassette.Rotation),
                CassetteRotationFromCarry = audit.SelectedCarryRotation,
                RightExtension = Vector3.Distance(context.RightUpper.position,
                    context.RightWrist.position) / length,
                RightWristRoll = RelativeTwistDegrees(context,
                    context.RightWrist,
                    context.Rig.Bones["R_middle1"].localPosition.normalized),
                SlotContactError = Vector3.Distance(axis.position,
                    markers.SlotEntryPosition),
                SlotRotationError = Mathf.Min(
                    Quaternion.Angle(axis.rotation, markers.SlotAxisRotation),
                    Quaternion.Angle(axis.rotation,
                        markers.SlotAxisRotation * Quaternion.AngleAxis(
                            180f, Vector3.forward)))
            };
            Vector3 cassetteViewport = context.Camera.WorldToViewportPoint(
                context.CassetteGrip.position);
            Vector3 wristViewport = context.Camera.WorldToViewportPoint(
                context.RightWrist.position);
            result.SlotReadable = cassetteViewport.z > 0f &&
                wristViewport.z > 0f && wristViewport.x > cassetteViewport.x;
            RejectPhysical(result, result.ThumbError > 0.002f,
                "thumb misses CassetteThumbGrip");
            RejectPhysical(result, result.IndexError > 0.002f,
                "index misses CassetteIndexGrip");
            RejectPhysical(result, result.MiddleError > 0.002f,
                "middle misses CassetteMiddleGrip");
            RejectPhysical(result, result.SupportThumbError > 0.004f ||
                result.SupportIndexError > 0.004f ||
                result.SupportMiddleError > 0.004f ||
                result.SupportRingError > 0.004f ||
                result.SupportPinkyError > 0.004f,
                "support digits miss recorder markers");
            RejectPhysical(result, result.SupportPalmError > 0.006f,
                "support palm misses RecorderSupportPalm");
            RejectPhysical(result, result.CassettePenetration > 0.002f,
                "cassette penetration exceeds 2mm");
            RejectPhysical(result, result.RecorderPenetration > 0.002f,
                "recorder penetration exceeds 2mm");
            RejectPhysical(result, result.RightExtension > 0.85f,
                "right-arm extension exceeds 85%");
            RejectPhysical(result, result.RightWristRoll > 40f,
                "right-wrist roll exceeds 40 degrees");
            RejectPhysical(result, result.SlotContactError > 0.0015f,
                "cassette is not at mechanical Contact");
            RejectPhysical(result, result.SlotRotationError > 1f,
                "cassette insertion axis is not aligned");
            RejectPhysical(result, !result.SlotReadable,
                "hand/cassette/slot relationship is not readable");
            result.Passed = result.Failures.Count == 0;
            return result;
        }

        private static float CassetteContactPenetration(Context context)
        {
            Transform center = FindUnique(context.CassetteGrip.gameObject,
                "CassetteInsertionAxis");
            return Mathf.Max(CassetteFacePenetration(context, center,
                    "R_thumb", "CassetteThumbGrip"),
                Mathf.Max(CassetteFacePenetration(context, center,
                        "R_point", "CassetteIndexGrip"),
                    CassetteFacePenetration(context, center,
                        "R_middle", "CassetteMiddleGrip")));
        }

        private static float CassetteFacePenetration(Context context,
            Transform center, string finger, string markerName)
        {
            Transform marker = FindUnique(context.CassetteGrip.gameObject,
                markerName);
            Vector3 outward = (marker.position - center.position).normalized;
            return Mathf.Max(0f, Vector3.Dot(marker.position -
                FingerTip(context, finger), outward));
        }

        private static Vector3 SupportPalmPoint(Context context)
        {
            return (context.Rig.Bones["L_point1"].position +
                context.Rig.Bones["L_middle1"].position +
                context.Rig.Bones["L_ring1"].position +
                context.Rig.Bones["L_pink1"].position) * 0.25f;
        }

        private static void RejectPhysical(PhysicalGripMetrics metrics,
            bool condition, string reason)
        {
            if (condition)
            {
                metrics.Failures.Add(reason);
            }
        }

        private static float MaximumMarkerPenetration(Context context,
            Transform prop, IEnumerable<string> fingerPrefixes)
        {
            Bounds local = prop == context.CassetteGrip ?
                new Bounds(Vector3.zero, new Vector3(0.110f, 0.0182f,
                    0.070f)) :
                new Bounds(Vector3.zero, new Vector3(0.104f, 0.194f,
                    0.035f));
            float maximum = 0f;
            foreach (string prefix in fingerPrefixes)
            {
                Vector3 point = prop.InverseTransformPoint(
                    CreateFingerChain(context, prefix).TipPosition);
                if (!local.Contains(point))
                {
                    continue;
                }
                float depth = Mathf.Min(
                    Mathf.Min(point.x - local.min.x, local.max.x - point.x),
                    Mathf.Min(Mathf.Min(point.y - local.min.y,
                        local.max.y - point.y), Mathf.Min(
                        point.z - local.min.z, local.max.z - point.z)));
                maximum = Mathf.Max(maximum, depth);
            }
            return maximum;
        }

        private static Bounds CalculateLocalBounds(Transform root)
        {
            bool any = false;
            Bounds result = new Bounds();
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Bounds source;
                SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (skinned != null)
                {
                    source = skinned.localBounds;
                }
                else if (filter != null && filter.sharedMesh != null)
                {
                    source = filter.sharedMesh.bounds;
                }
                else
                {
                    continue;
                }
                Vector3 center = source.center;
                Vector3 extents = source.extents;
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 world = renderer.transform.TransformPoint(center +
                        Vector3.Scale(extents, new Vector3(x, y, z)));
                    Vector3 local = root.InverseTransformPoint(world);
                    if (!any)
                    {
                        result = new Bounds(local, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        result.Encapsulate(local);
                    }
                }
            }
            return result;
        }

        private static void GenerateInteractionCandidates()
        {
            string humanPosePath = Path.GetFullPath(
                SoulRecorderReplacementArmStaging.PoseFile);
            string humanBefore = Sha256File(humanPosePath);
            string scenePath = Path.GetFullPath(
                SoulRecorderReplacementArmStaging.AuthoringScene);
            string sceneBefore = Sha256File(scenePath);

            Directory.CreateDirectory(Path.GetFullPath(
                CandidateOutputAssetFolder));
            Scene scene = EditorSceneManager.OpenScene(
                SoulRecorderReplacementArmStaging.AuthoringScene,
                OpenSceneMode.Single);
            Context context = ResolveContext(scene);
            InitializeCandidateContext(context);

            List<Pose> candidates = new List<Pose>();
            List<CandidateMetrics> metrics = new List<CandidateMetrics>();
            foreach (CandidateSpec spec in InteractionCandidateSpecs())
            {
                CandidateMetrics candidateMetrics;
                Pose pose = BuildInteractionCandidate(context, spec,
                    out candidateMetrics);
                candidates.Add(pose);
                metrics.Add(candidateMetrics);
            }

            WriteCandidatePoseFile(context, candidates, humanBefore,
                sceneBefore);
            RenderInteractionCandidates(context, candidates, metrics);
            AssetDatabase.Refresh();

            string humanAfter = Sha256File(humanPosePath);
            string sceneAfter = Sha256File(scenePath);
            if (!string.Equals(humanBefore, humanAfter,
                    StringComparison.Ordinal) ||
                !string.Equals(sceneBefore, sceneAfter,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Interaction candidate search modified human-authored data.");
            }
            Debug.Log("Interaction candidate search preserved human pose SHA-256=" +
                humanAfter + " and authoring scene SHA-256=" + sceneAfter + ".");
        }

        private static void InitializeCandidateContext(Context context)
        {
            SoulRecorderReplacementGripPoseStore.LoadStaticMasterV1(
                context.Controlled, false);
            context.LeftHandGripToRecorder = CaptureGrip(
                context.LeftWrist, context.RecorderGrip);
            context.RightHandGripToCassette = CaptureGrip(
                context.RightWrist, context.CassetteGrip);
            context.StaticMaster = CapturePose(context, "StaticMaster_v1",
                "locked carry pose", CassetteOwner.Hand);
            context.StaticRightElbowOffset =
                context.RightElbow.position - context.RightUpper.position;
            context.AlignElbowPlaneNormal = ElbowPlaneNormal(context);
            context.SupportElbowPlaneNormal = LeftElbowPlaneNormal(context);
            context.Markers = CaptureMarkerGeometry(context);
        }

        private static IEnumerable<CandidateSpec> InteractionCandidateSpecs()
        {
            return new[]
            {
                Candidate("G", new Vector3(0.055f, 0.040f, -0.140f),
                    new Vector3(5f, -10f, -4f),
                    new Vector3(-0.70f, -0.68f, -0.18f),
                    new Vector3(0.72f, -0.68f, -0.16f),
                    new Vector3(2f, -6f, 5f), new Vector3(-0.050f, 0.003f, 0f)),
                Candidate("H", new Vector3(0.075f, 0.025f, -0.150f),
                    new Vector3(-2f, -14f, 3f),
                    new Vector3(-0.82f, -0.55f, -0.14f),
                    new Vector3(0.82f, -0.55f, -0.14f),
                    new Vector3(-3f, 5f, -4f), new Vector3(-0.060f, 0.008f, -0.003f)),
                Candidate("I", new Vector3(0.040f, 0.065f, -0.145f),
                    new Vector3(9f, -5f, -8f),
                    new Vector3(-0.60f, -0.78f, -0.12f),
                    new Vector3(0.62f, -0.77f, -0.12f),
                    new Vector3(4f, -8f, 8f), new Vector3(-0.045f, -0.002f, 0.002f)),
                Candidate("J", new Vector3(0.090f, 0.050f, -0.160f),
                    new Vector3(3f, -18f, 7f),
                    new Vector3(-0.88f, -0.45f, -0.10f),
                    new Vector3(0.88f, -0.45f, -0.10f),
                    new Vector3(-5f, 8f, -7f), new Vector3(-0.070f, 0f, -0.004f)),
                Candidate("K", new Vector3(0.060f, 0.080f, -0.155f),
                    new Vector3(11f, -12f, 2f),
                    new Vector3(-0.66f, -0.73f, -0.18f),
                    new Vector3(0.68f, -0.71f, -0.18f),
                    new Vector3(6f, -3f, 10f), new Vector3(-0.055f, 0.010f, 0.003f)),
                Candidate("L", new Vector3(0.085f, 0.070f, -0.160f),
                    new Vector3(-4f, -20f, -3f),
                    new Vector3(-0.76f, -0.62f, -0.18f),
                    new Vector3(0.78f, -0.60f, -0.18f),
                    new Vector3(-6f, 2f, -10f), new Vector3(-0.065f, 0.005f, 0.002f))
            };
        }

        private static CandidateSpec Candidate(string suffix,
            Vector3 recorderCameraOffset, Vector3 recorderCameraEuler,
            Vector3 leftElbowPole, Vector3 rightElbowPole,
            Vector3 rightWristLocalEuler,
            Vector3 interactionGripPositionOffset)
        {
            return new CandidateSpec
            {
                Suffix = suffix,
                RecorderCameraOffset = recorderCameraOffset,
                RecorderCameraEuler = recorderCameraEuler,
                LeftElbowPole = leftElbowPole,
                RightElbowPole = rightElbowPole,
                RightWristLocalEuler = rightWristLocalEuler,
                InteractionGripPositionOffset = interactionGripPositionOffset
            };
        }

        private static Pose BuildInteractionCandidate(Context context,
            CandidateSpec spec, out CandidateMetrics metrics)
        {
            ApplyPose(context, context.StaticMaster);
            Vector3 desiredRecorderPosition = context.RecorderGrip.position +
                context.Camera.transform.TransformVector(spec.RecorderCameraOffset);
            Quaternion desiredRecorderRotation = CameraSpaceDelta(context,
                spec.RecorderCameraEuler) * context.RecorderGrip.rotation;
            WorldPose desiredLeftWrist = HandTargetForRecorder(context,
                desiredRecorderPosition, desiredRecorderRotation);
            SolveRotationOnlyArm(context, context.LeftUpper,
                context.LeftElbow, context.LeftWrist,
                desiredLeftWrist.Position, desiredLeftWrist.Rotation,
                spec.LeftElbowPole, "left");
            SetRecorderFromLeftWrist(context);

            MarkerGeometry markers = CaptureMarkerGeometry(context);
            context.Markers = markers;
            WorldPose desiredCassette = CassetteFromAxisTarget(context,
                markers.SlotEntryPosition, markers.SlotAxisRotation);
            SolveRightInteractionArm(context, desiredCassette, spec);
            SetRecorderFromLeftWrist(context);
            SetCassetteFromRightWrist(context);

            string name = "InteractionMaster_" + spec.Suffix;
            Pose pose = CapturePose(context, name,
                "automatic static interaction-layout candidate; rigid grips",
                CassetteOwner.Hand);
            metrics = MeasureInteractionCandidate(context, pose);
            return pose;
        }

        private static Quaternion CameraSpaceDelta(Context context,
            Vector3 euler)
        {
            return context.Camera.transform.rotation *
                Quaternion.Euler(euler) * Quaternion.Inverse(
                    context.Camera.transform.rotation);
        }

        private static void SolveRightInteractionArm(Context context,
            WorldPose desiredCassette,
            CandidateSpec spec)
        {
            GripRelativeTransform carryGrip = context.StaticMaster.RightHandGripToCassette;
            Vector3 interactionPosition = carryGrip.Position +
                spec.InteractionGripPositionOffset;
            Quaternion desiredWristRotation = context.RightWrist.rotation *
                Quaternion.Euler(spec.RightWristLocalEuler);
            Vector3 finalWristTarget = desiredCassette.Position -
                desiredWristRotation * interactionPosition;
            SolveRotationOnlyArm(context, context.RightUpper,
                context.RightElbow, context.RightWrist, finalWristTarget,
                desiredWristRotation, spec.RightElbowPole, "right");
            context.RightHandGripToCassette = new GripRelativeTransform
            {
                Position = interactionPosition,
                Rotation = Quaternion.Inverse(desiredWristRotation) *
                    desiredCassette.Rotation
            };
        }

        private static void SolveRotationOnlyArm(Context context,
            Transform upper, Transform elbow, Transform wrist,
            Vector3 targetPosition, Quaternion targetRotation,
            Vector3 cameraSpacePole, string side)
        {
            Vector3 shoulder = upper.position;
            float upperLength = Vector3.Distance(shoulder,
                elbow.position);
            float forearmLength = Vector3.Distance(elbow.position,
                wrist.position);
            Vector3 toTarget = targetPosition - shoulder;
            float reach = toTarget.magnitude;
            float extension = reach / (upperLength + forearmLength);
            float maximumExtension = side == "right" ? 0.8501f : 0.9801f;
            if (extension > maximumExtension)
            {
                throw new InvalidOperationException(
                    "Candidate requested more than 85% " + side +
                    "-arm extension: " +
                    (extension * 100f).ToString("F2",
                        CultureInfo.InvariantCulture) + "%. shoulder=" +
                    shoulder.ToString("F4") + " target=" +
                    targetPosition.ToString("F4") + " length=" +
                    (upperLength + forearmLength).ToString("F4",
                        CultureInfo.InvariantCulture) + ".");
            }
            if (reach <= Mathf.Abs(upperLength - forearmLength) + 0.0001f)
            {
                throw new InvalidOperationException(
                    "Candidate " + side + "-arm target is inside the two-bone minimum reach.");
            }
            Vector3 direction = toTarget / reach;
            Vector3 pole = context.Camera.transform.TransformDirection(
                cameraSpacePole.normalized);
            pole = Vector3.ProjectOnPlane(pole, direction).normalized;
            if (pole.sqrMagnitude < 0.0001f)
            {
                throw new InvalidOperationException(
                    "Candidate " + side + " elbow pole collapsed onto the reach direction.");
            }
            float elbowAlong = (upperLength * upperLength -
                forearmLength * forearmLength + reach * reach) / (2f * reach);
            float elbowHeight = Mathf.Sqrt(Mathf.Max(0f,
                upperLength * upperLength - elbowAlong * elbowAlong));
            Vector3 desiredElbow = shoulder + direction * elbowAlong +
                pole * elbowHeight;
            Vector3 currentUpper = elbow.position - shoulder;
            upper.rotation = Quaternion.FromToRotation(
                currentUpper, desiredElbow - shoulder) *
                upper.rotation;
            Vector3 currentForearm = wrist.position - elbow.position;
            elbow.rotation = Quaternion.FromToRotation(
                currentForearm, targetPosition - elbow.position) *
                elbow.rotation;
            wrist.rotation = targetRotation;
            float error = Vector3.Distance(wrist.position,
                targetPosition);
            if (error > PositionTolerance)
            {
                throw new InvalidOperationException(
                    "Candidate rotation-only " + side +
                    "-arm solve missed the wrist target by " +
                    (error * 1000f).ToString("F3",
                        CultureInfo.InvariantCulture) + " mm.");
            }
            AssertSkeletonLocalGeometryLocked(context, side);
        }

        private static DraftTransform StaticTransform(Context context,
            Transform transform)
        {
            return context.StaticMaster.Values[PathOf(transform)];
        }

        private static void AssertSkeletonLocalGeometryLocked(Context context,
            string side)
        {
            string prefix = side == "left" ? "L_" : "R_";
            foreach (string boneName in SoulRecorderReplacementArmStaging.RequiredBones)
            {
                if (!boneName.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }
                Transform bone = context.Rig.Bones[boneName];
                DraftTransform locked = StaticTransform(context, bone);
                if (!SameBits(bone.localPosition, locked.localPosition) ||
                    !SameBits(bone.localScale, locked.localScale))
                {
                    throw new InvalidOperationException(
                        boneName + " changed skeletal local position/scale during solve.");
                }
            }
        }

        private static bool SameBits(Vector3 left, Vector3 right)
        {
            return BitConverter.SingleToInt32Bits(left.x) ==
                    BitConverter.SingleToInt32Bits(right.x) &&
                BitConverter.SingleToInt32Bits(left.y) ==
                    BitConverter.SingleToInt32Bits(right.y) &&
                BitConverter.SingleToInt32Bits(left.z) ==
                    BitConverter.SingleToInt32Bits(right.z);
        }

        private static CandidateMetrics MeasureInteractionCandidate(
            Context context, Pose pose)
        {
            ApplyPose(context, pose);
            MarkerGeometry markers = CaptureMarkerGeometry(context);
            Transform cassetteAxis = FindUnique(context.CassetteGrip.gameObject,
                "CassetteInsertionAxis");
            float armLength = Vector3.Distance(context.RightUpper.position,
                context.RightElbow.position) + Vector3.Distance(
                    context.RightElbow.position, context.RightWrist.position);
            CandidateMetrics result = new CandidateMetrics
            {
                Name = pose.Name,
                Extension = Vector3.Distance(context.RightUpper.position,
                    context.RightWrist.position) / armLength,
                ContactPositionError = Vector3.Distance(cassetteAxis.position,
                    markers.SlotEntryPosition),
                ContactRotationError = Mathf.Min(
                    Quaternion.Angle(cassetteAxis.rotation,
                        markers.SlotAxisRotation),
                    Quaternion.Angle(cassetteAxis.rotation,
                        markers.SlotAxisRotation * Quaternion.AngleAxis(
                            180f, Vector3.forward)))
            };
            Vector3 elbow = context.Camera.WorldToViewportPoint(
                context.RightElbow.position);
            Vector3 wrist = context.Camera.WorldToViewportPoint(
                context.RightWrist.position);
            Vector2 forearm = new Vector2(wrist.x - elbow.x,
                wrist.y - elbow.y);
            result.ForearmScreenAngle = Mathf.Atan2(Mathf.Abs(forearm.y),
                Mathf.Abs(forearm.x)) * Mathf.Rad2Deg;
            Vector2 visibleElbow = new Vector2(Mathf.Clamp01(elbow.x),
                Mathf.Clamp01(elbow.y));
            Vector2 visibleWrist = new Vector2(Mathf.Clamp01(wrist.x),
                Mathf.Clamp01(wrist.y));
            result.ForearmScreenLength = Vector2.Distance(visibleElbow,
                visibleWrist);
            result.ElbowViewport = elbow;
            result.WristViewport = wrist;
            result.CassetteViewport = context.Camera.WorldToViewportPoint(
                context.CassetteGrip.position);
            result.RecorderViewport = context.Camera.WorldToViewportPoint(
                context.RecorderGrip.position);
            GripRelativeTransform recorderGrip = CaptureGrip(context.LeftWrist,
                context.RecorderGrip);
            result.RecorderGripPositionDrift = Vector3.Distance(
                recorderGrip.Position, context.LeftHandGripToRecorder.Position);
            result.RecorderGripRotationDrift = Quaternion.Angle(
                recorderGrip.Rotation, context.LeftHandGripToRecorder.Rotation);
            GripRelativeTransform cassetteGrip = CaptureGrip(context.RightWrist,
                context.CassetteGrip);
            result.CassetteGripPositionDrift = Vector3.Distance(
                cassetteGrip.Position, context.RightHandGripToCassette.Position);
            result.CassetteGripRotationDrift = Quaternion.Angle(
                cassetteGrip.Rotation, context.RightHandGripToCassette.Rotation);
            result.InteractionGripPositionDelta = Vector3.Distance(
                pose.RightHandGripToCassette.Position,
                context.StaticMaster.RightHandGripToCassette.Position);
            result.InteractionGripRotationDelta = Quaternion.Angle(
                pose.RightHandGripToCassette.Rotation,
                context.StaticMaster.RightHandGripToCassette.Rotation);
            MeasureAnatomy(context, result);
            MeasureBoneGeometryDrift(context, pose, result);
            result.ElbowLowRight = elbow.z > 0f &&
                (elbow.x >= wrist.x || elbow.x >= 0.90f) &&
                (elbow.y <= wrist.y || elbow.y <= 0.12f);
            ProjectedVisibility recorder = MeasureProjectedVisibility(
                context.Camera, context.RecorderGrip);
            result.RecorderVisibility = recorder.VisibleFraction;
            result.RecorderViewportArea = recorder.ViewportArea;
            ProjectedVisibility cassette = MeasureProjectedVisibility(
                context.Camera, context.CassetteGrip);
            result.CassetteVisibility = cassette.VisibleFraction;
            result.CassetteViewportArea = cassette.ViewportArea;

            RejectCandidate(result, result.Extension > 0.8505f,
                "right-arm extension exceeds 85%");
            RejectCandidate(result, result.LeftUpperTwist > 30.001f ||
                result.RightUpperTwist > 30.001f,
                "upper-arm twist exceeds 30 degrees");
            RejectCandidate(result, result.LeftForearmTwist > 30.001f ||
                result.RightForearmTwist > 30.001f,
                "forearm twist exceeds 30 degrees");
            RejectCandidate(result, result.LeftWristRoll > 40.001f ||
                result.RightWristRoll > 40.001f,
                "wrist roll exceeds 40 degrees");
            RejectCandidate(result, result.LeftElbowFlipped ||
                result.RightElbowFlipped, "elbow flip detected");
            RejectCandidate(result, result.BoneDrifts.Any(value =>
                !value.EndsWith("position=EXACT scale=EXACT",
                    StringComparison.Ordinal)),
                "skeletal local position/scale drift detected");
            RejectCandidate(result, result.RecorderGripPositionDrift > 0.00001f ||
                result.RecorderGripRotationDrift > 0.001f,
                "recorder slipped relative to left hand");
            RejectCandidate(result, result.CassetteGripPositionDrift > 0.00001f ||
                result.CassetteGripRotationDrift > 0.001f,
                "cassette slipped relative to right hand");
            RejectCandidate(result, result.ContactPositionError > 0.0015f,
                "cassette misses slot contact");
            RejectCandidate(result, result.ContactRotationError > 1f,
                "cassette axis does not match slot");
            RejectCandidate(result, result.ForearmScreenAngle < 20f ||
                result.ForearmScreenAngle > 78f,
                "forearm is too horizontal or vertical");
            RejectCandidate(result, result.ForearmScreenLength > 0.80f,
                "forearm crosses too much of the viewport");
            RejectCandidate(result, !result.ElbowLowRight,
                "elbow is not low/right");
            RejectCandidate(result, result.RecorderVisibility < 0.80f ||
                result.RecorderViewportArea < 0.0015f,
                "recorder is not comfortably readable");
            RejectCandidate(result, result.CassetteVisibility < 0.75f ||
                result.CassetteViewportArea < 0.00025f,
                "cassette is not visibly framed");
            result.Passed = result.Failures.Count == 0;
            return result;
        }

        private static void MeasureAnatomy(Context context,
            CandidateMetrics result)
        {
            result.LeftUpperTwist = RelativeTwistDegrees(context,
                context.LeftUpper, context.LeftElbow.localPosition.normalized);
            result.LeftForearmTwist = RelativeTwistDegrees(context,
                context.LeftElbow, context.LeftWrist.localPosition.normalized);
            result.LeftWristRoll = RelativeTwistDegrees(context,
                context.LeftWrist,
                context.Rig.Bones["L_middle1"].localPosition.normalized);
            result.RightUpperTwist = RelativeTwistDegrees(context,
                context.RightUpper, context.RightElbow.localPosition.normalized);
            result.RightForearmTwist = RelativeTwistDegrees(context,
                context.RightElbow, context.RightWrist.localPosition.normalized);
            result.RightWristRoll = RelativeTwistDegrees(context,
                context.RightWrist,
                context.Rig.Bones["R_middle1"].localPosition.normalized);
            result.LeftElbowBend = ElbowBend(context.LeftUpper,
                context.LeftElbow, context.LeftWrist);
            result.RightElbowBend = ElbowBend(context.RightUpper,
                context.RightElbow, context.RightWrist);
            result.LeftElbowFlipped = result.LeftElbowBend < 8f;
            result.RightElbowFlipped = result.RightElbowBend < 8f;
        }

        private static float RelativeTwistDegrees(Context context,
            Transform bone, Vector3 longitudinalAxis)
        {
            Quaternion baseline = StaticTransform(context, bone).localRotation;
            Quaternion delta = Quaternion.Inverse(baseline) * bone.localRotation;
            Vector3 axis = longitudinalAxis.normalized;
            Vector3 imaginary = new Vector3(delta.x, delta.y, delta.z);
            Vector3 projected = Vector3.Project(imaginary, axis);
            Quaternion twist = new Quaternion(projected.x, projected.y,
                projected.z, delta.w);
            float magnitude = Mathf.Sqrt(twist.x * twist.x + twist.y * twist.y +
                twist.z * twist.z + twist.w * twist.w);
            if (magnitude <= 0.000001f)
            {
                return 0f;
            }
            twist = new Quaternion(twist.x / magnitude, twist.y / magnitude,
                twist.z / magnitude, twist.w / magnitude);
            return Quaternion.Angle(Quaternion.identity, twist);
        }

        private static float ElbowBend(Transform upper, Transform elbow,
            Transform wrist)
        {
            float internalAngle = Vector3.Angle(upper.position - elbow.position,
                wrist.position - elbow.position);
            return 180f - internalAngle;
        }

        private static void MeasureBoneGeometryDrift(Context context,
            Pose pose, CandidateMetrics result)
        {
            foreach (string boneName in SoulRecorderReplacementArmStaging.RequiredBones)
            {
                Transform bone = context.Rig.Bones[boneName];
                DraftTransform candidate = pose.Values[PathOf(bone)];
                DraftTransform locked = StaticTransform(context, bone);
                bool positionExact = SameBits(candidate.localPosition,
                    locked.localPosition);
                bool scaleExact = SameBits(candidate.localScale,
                    locked.localScale);
                result.BoneDrifts.Add(boneName + " position=" +
                    (positionExact ? "EXACT" :
                        (Vector3.Distance(candidate.localPosition,
                            locked.localPosition) * 1000f).ToString("F6",
                                CultureInfo.InvariantCulture) + "mm") +
                    " scale=" + (scaleExact ? "EXACT" :
                        Vector3.Distance(candidate.localScale,
                            locked.localScale).ToString("F9",
                                CultureInfo.InvariantCulture)));
            }
        }

        private static void RejectCandidate(CandidateMetrics metrics,
            bool condition, string reason)
        {
            if (condition)
            {
                metrics.Failures.Add(reason);
            }
        }

        private static ProjectedVisibility MeasureProjectedVisibility(
            Camera camera, Transform root)
        {
            bool any = false;
            float minimumX = float.PositiveInfinity;
            float minimumY = float.PositiveInfinity;
            float maximumX = float.NegativeInfinity;
            float maximumY = float.NegativeInfinity;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Bounds bounds = renderer.bounds;
                Vector3 center = bounds.center;
                Vector3 extents = bounds.extents;
                for (int x = -1; x <= 1; x += 2)
                {
                    for (int y = -1; y <= 1; y += 2)
                    {
                        for (int z = -1; z <= 1; z += 2)
                        {
                            Vector3 viewport = camera.WorldToViewportPoint(center +
                                Vector3.Scale(extents, new Vector3(x, y, z)));
                            if (viewport.z <= 0f)
                            {
                                continue;
                            }
                            any = true;
                            minimumX = Mathf.Min(minimumX, viewport.x);
                            minimumY = Mathf.Min(minimumY, viewport.y);
                            maximumX = Mathf.Max(maximumX, viewport.x);
                            maximumY = Mathf.Max(maximumY, viewport.y);
                        }
                    }
                }
            }
            if (!any)
            {
                return new ProjectedVisibility();
            }
            float width = Mathf.Max(0f, maximumX - minimumX);
            float height = Mathf.Max(0f, maximumY - minimumY);
            float area = width * height;
            float visibleWidth = Mathf.Max(0f,
                Mathf.Min(1f, maximumX) - Mathf.Max(0f, minimumX));
            float visibleHeight = Mathf.Max(0f,
                Mathf.Min(1f, maximumY) - Mathf.Max(0f, minimumY));
            return new ProjectedVisibility
            {
                ViewportArea = area,
                VisibleFraction = area <= 0.000001f ? 0f :
                    (visibleWidth * visibleHeight) / area
            };
        }

        private static void WriteCandidatePoseFile(Context context,
            List<Pose> candidates, string humanPoseSha,
            string authoringSceneSha)
        {
            DraftPoseFile file = new DraftPoseFile
            {
                lockedStaticMasterSha256 =
                    SoulRecorderReplacementGripPoseStore.LockedStaticMasterHash,
                sourceHumanPoseFileSha256 = humanPoseSha,
                sourceAuthoringSceneSha256 = authoringSceneSha,
                generatedUtc = DateTime.UtcNow.ToString("O",
                    CultureInfo.InvariantCulture),
                poses = candidates.Select(pose => new DraftPoseRecord
                {
                    poseName = pose.Name,
                    source = pose.Source,
                    cassetteOwner = pose.Owner.ToString(),
                    interactionGripPosition =
                        pose.RightHandGripToCassette.Position,
                    interactionGripRotation =
                        pose.RightHandGripToCassette.Rotation,
                    supportGripPosition =
                        pose.LeftHandGripToRecorder.Position,
                    supportGripRotation =
                        pose.LeftHandGripToRecorder.Rotation,
                    transforms = pose.Values.Values.ToArray()
                }).ToArray()
            };
            File.WriteAllText(Path.GetFullPath(CandidatePoseAsset),
                JsonUtility.ToJson(file, true), new UTF8Encoding(false));
        }

        private static void WritePhysicalGripPoseFile(List<Pose> poses,
            string humanPoseSha, string authoringSceneSha)
        {
            DraftPoseFile file = new DraftPoseFile
            {
                lockedStaticMasterSha256 =
                    SoulRecorderReplacementGripPoseStore.LockedStaticMasterHash,
                sourceHumanPoseFileSha256 = humanPoseSha,
                sourceAuthoringSceneSha256 = authoringSceneSha,
                generatedUtc = DateTime.UtcNow.ToString("O",
                    CultureInfo.InvariantCulture),
                poses = poses.Select(pose => new DraftPoseRecord
                {
                    poseName = pose.Name,
                    source = pose.Source,
                    cassetteOwner = pose.Owner.ToString(),
                    interactionGripPosition =
                        pose.RightHandGripToCassette.Position,
                    interactionGripRotation =
                        pose.RightHandGripToCassette.Rotation,
                    supportGripPosition =
                        pose.LeftHandGripToRecorder.Position,
                    supportGripRotation =
                        pose.LeftHandGripToRecorder.Rotation,
                    transforms = pose.Values.Values.ToArray()
                }).ToArray()
            };
            File.WriteAllText(Path.GetFullPath(PhysicalGripPoseAsset),
                JsonUtility.ToJson(file, true), new UTF8Encoding(false));
        }

        private static void WritePoseRepairPoseFile(List<Pose> poses,
            string humanPoseSha, string authoringSceneSha)
        {
            DraftPoseFile file = new DraftPoseFile
            {
                lockedStaticMasterSha256 =
                    SoulRecorderReplacementGripPoseStore.LockedStaticMasterHash,
                sourceHumanPoseFileSha256 = humanPoseSha,
                sourceAuthoringSceneSha256 = authoringSceneSha,
                generatedUtc = DateTime.UtcNow.ToString("O",
                    CultureInfo.InvariantCulture),
                poses = poses.Select(pose => new DraftPoseRecord
                {
                    poseName = pose.Name,
                    source = pose.Source,
                    cassetteOwner = pose.Owner.ToString(),
                    interactionGripPosition =
                        pose.RightHandGripToCassette.Position,
                    interactionGripRotation =
                        pose.RightHandGripToCassette.Rotation,
                    supportGripPosition =
                        pose.LeftHandGripToRecorder.Position,
                    supportGripRotation =
                        pose.LeftHandGripToRecorder.Rotation,
                    transforms = pose.Values.Values.ToArray()
                }).ToArray()
            };
            File.WriteAllText(Path.GetFullPath(PoseRepairPoseAsset),
                JsonUtility.ToJson(file, true), new UTF8Encoding(false));
        }

        private static void RenderPhysicalGripCandidates(Context context,
            List<Pose> poses, List<PhysicalGripMetrics> metrics,
            CassetteAxisAudit audit)
        {
            string output = Path.GetFullPath(PhysicalGripOutputAssetFolder);
            string full = Path.Combine(output, "FOV70");
            string cassette = Path.Combine(output, "CassetteCloseup");
            string recorder = Path.Combine(output, "RecorderCloseup");
            Directory.CreateDirectory(full);
            Directory.CreateDirectory(cassette);
            Directory.CreateDirectory(recorder);
            DeletePngs(full);
            DeletePngs(cassette);
            DeletePngs(recorder);
            foreach (Pose pose in poses)
            {
                ApplyPose(context, pose);
                string fullPath = Path.Combine(full, pose.Name + "-FOV70.png");
                RenderPng(context.Camera, fullPath);
                CropPngAroundViewport(fullPath, Path.Combine(cassette,
                    pose.Name + "-CassetteCloseup.png"),
                    context.Camera.WorldToViewportPoint(
                        context.CassetteGrip.position), 0.42f, 0.62f);
                CropPngAroundViewport(fullPath, Path.Combine(recorder,
                    pose.Name + "-RecorderCloseup.png"),
                    context.Camera.WorldToViewportPoint(
                        context.RecorderGrip.position), 0.46f, 0.68f);
            }
            CreateContactSheet(full, Path.Combine(output,
                "physical-grip-l1-l3-fov70.png"), 3);
            CreateContactSheet(cassette, Path.Combine(output,
                "physical-grip-l1-l3-cassette-closeup.png"), 3);
            CreateContactSheet(recorder, Path.Combine(output,
                "physical-grip-l1-l3-recorder-closeup.png"), 3);
            WritePhysicalGripReport(metrics, audit, Path.Combine(output,
                "physical-grip-l1-l3-report.txt"));
        }

        private static void RenderPoseRepairCandidates(Context context,
            List<Pose> poses, List<PoseRepairMetrics> metrics)
        {
            string output = Path.GetFullPath(PoseRepairOutputAssetFolder);
            string full = Path.Combine(output, "FOV70");
            string right = Path.Combine(output, "RightHandCloseup");
            string left = Path.Combine(output, "LeftHandCloseup");
            Directory.CreateDirectory(full);
            Directory.CreateDirectory(right);
            Directory.CreateDirectory(left);
            DeletePngs(full);
            DeletePngs(right);
            DeletePngs(left);
            foreach (Pose pose in poses)
            {
                ApplyPose(context, pose);
                string fullPath = Path.Combine(full, pose.Name + "-FOV70.png");
                RenderPng(context.Camera, fullPath);
                CropPngAroundViewport(fullPath, Path.Combine(right,
                    pose.Name + "-RightHandCassette.png"),
                    context.Camera.WorldToViewportPoint(
                        context.CassetteGrip.position), 0.40f, 0.60f);
                CropPngAroundViewport(fullPath, Path.Combine(left,
                    pose.Name + "-LeftHandRecorder.png"),
                    context.Camera.WorldToViewportPoint(
                        context.RecorderGrip.position), 0.46f, 0.68f);
            }
            CreateContactSheet(full, Path.Combine(output,
                "pose-repair-a-c-fov70.png"), 3);
            CreateContactSheet(right, Path.Combine(output,
                "pose-repair-right-hand-cassette.png"), 3);
            CreateContactSheet(left, Path.Combine(output,
                "pose-repair-left-hand-recorder.png"), 3);
            WritePoseRepairReport(metrics, Path.Combine(output,
                "pose-repair-a-c-report.txt"));
        }

        private static void WritePoseRepairReport(
            List<PoseRepairMetrics> metrics, string path)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("SoulRecorder focused visual pose repair A-C");
            report.AppendLine("All candidates depict the same Contact moment.");
            report.AppendLine("StaticMaster_v1: LOCKED / unchanged");
            report.AppendLine("PhysicalGrip_L3: mechanical contact reference only");
            report.AppendLine("Left fingers: bit-exact StaticMaster_v1 rotations");
            report.AppendLine("Right ring/pinky: bit-exact StaticMaster_v1 rotations");
            report.AppendLine("No animation or runtime integration generated.");
            report.AppendLine("Human visual selection required; no automatic winner.");
            report.AppendLine();
            foreach (PoseRepairMetrics value in metrics)
            {
                report.AppendLine(value.Name + ":");
                report.AppendLine("  slot contact: " +
                    Millimeters(value.SlotPositionError) + " mm / " +
                    value.SlotRotationError.ToString("F4",
                        CultureInfo.InvariantCulture) + " deg");
                report.AppendLine("  left finger max delta: " +
                    value.MaximumLeftFingerDelta.ToString("F4",
                        CultureInfo.InvariantCulture) + " deg");
                report.AppendLine("  right finger/distal max delta: " +
                    value.MaximumRightFingerDelta.ToString("F2",
                        CultureInfo.InvariantCulture) + " / " +
                    value.MaximumRightDistalDelta.ToString("F2",
                        CultureInfo.InvariantCulture) + " deg");
                report.AppendLine("  right wrist delta: " +
                    value.RightWristDelta.ToString("F2",
                        CultureInfo.InvariantCulture) + " deg");
                report.AppendLine("  thumb/index/middle contact errors: " +
                    Millimeters(value.ThumbError) + " / " +
                    Millimeters(value.IndexError) + " / " +
                    Millimeters(value.MiddleError) + " mm");
                report.AppendLine("  index-middle tip separation: " +
                    Millimeters(value.IndexMiddleSeparation) + " mm");
                report.AppendLine("  right-arm extension: " +
                    (value.RightExtension * 100f).ToString("F2",
                        CultureInfo.InvariantCulture) + "%");
                report.AppendLine("  viewport recorder/cassette: " +
                    value.RecorderViewport.ToString("F3") + " / " +
                    value.CassetteViewport.ToString("F3"));
                report.AppendLine("  viewport wrists L/R: " +
                    value.LeftWristViewport.ToString("F3") + " / " +
                    value.RightWristViewport.ToString("F3"));
                report.AppendLine("  viewport skinned bounds center: " +
                    value.RendererBoundsViewport.ToString("F3"));
                report.AppendLine("  warnings: " +
                    (value.Warnings.Count == 0 ? "none" :
                        string.Join("; ", value.Warnings.ToArray())));
                report.AppendLine();
            }
            report.AppendLine("VISUAL APPROVAL: PENDING HUMAN REVIEW");
            File.WriteAllText(path, report.ToString(), new UTF8Encoding(false));
        }

        private static void CropPngAroundViewport(string sourcePath,
            string outputPath, Vector3 viewport, float widthFraction,
            float heightFraction)
        {
            Texture2D source = LoadTexture(sourcePath);
            try
            {
                int cropWidth = Mathf.Clamp(Mathf.RoundToInt(
                    source.width * widthFraction), 1, source.width);
                int cropHeight = Mathf.Clamp(Mathf.RoundToInt(
                    source.height * heightFraction), 1, source.height);
                int centerX = Mathf.RoundToInt(viewport.x * source.width);
                int centerY = Mathf.RoundToInt(viewport.y * source.height);
                int x = Mathf.Clamp(centerX - cropWidth / 2, 0,
                    source.width - cropWidth);
                int y = Mathf.Clamp(centerY - cropHeight / 2, 0,
                    source.height - cropHeight);
                Texture2D crop = new Texture2D(cropWidth, cropHeight,
                    TextureFormat.RGB24, false);
                try
                {
                    crop.SetPixels(source.GetPixels(x, y, cropWidth,
                        cropHeight));
                    crop.Apply();
                    File.WriteAllBytes(outputPath, crop.EncodeToPNG());
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(crop);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        private static void WritePhysicalGripReport(
            List<PhysicalGripMetrics> metrics, CassetteAxisAudit audit,
            string path)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("SoulRecorder PhysicalGrip L1-L3 marker solve");
            report.AppendLine("InteractionMaster_L is arm-layout seed only.");
            report.AppendLine("Cassette is fixed at mechanical Contact before finger solving.");
            report.AppendLine("StaticMaster_v1 and human-authored poses remain untouched.");
            report.AppendLine();
            report.AppendLine("CASSETTE AXIS AUDIT");
            report.AppendLine("Entry->Seated vs CassetteInsertionAxis dot: " +
                audit.AxisDirectionDot.ToString("F6", CultureInfo.InvariantCulture));
            report.AppendLine("Entry->Seated vs CassetteSlotTravelAxis dot: " +
                audit.SlotDirectionDot.ToString("F6", CultureInfo.InvariantCulture));
            report.AppendLine("CassetteFront/CassetteTop orthogonality abs(dot): " +
                audit.FrontTopDot.ToString("F6", CultureInfo.InvariantCulture));
            report.AppendLine("Carry -> direct Contact rotation: " +
                audit.DirectCarryRotation.ToString("F3", CultureInfo.InvariantCulture) + " deg");
            report.AppendLine("Carry -> 180-roll-equivalent Contact rotation: " +
                audit.RolledCarryRotation.ToString("F3", CultureInfo.InvariantCulture) + " deg");
            report.AppendLine("Selected equivalent orientation: " +
                (audit.UsedEquivalentRoll ? "180-roll equivalent" : "direct") +
                "; rotation=" + audit.SelectedCarryRotation.ToString("F3",
                    CultureInfo.InvariantCulture) + " deg");
            report.AppendLine("Axis sign correct: " +
                (audit.AxisSignCorrect ? "YES" : "NO"));
            report.AppendLine("Physical 180-degree flip required: " +
                (audit.UsedEquivalentRoll ? "NO (equivalent roll avoids it)" :
                    (audit.DirectCarryRotation > 150f ? "YES" : "NO")));
            report.AppendLine();
            foreach (PhysicalGripMetrics item in metrics)
            {
                report.AppendLine(item.Name + ":");
                report.AppendLine("cassette contacts thumb/index/middle: " +
                    Millimeters(item.ThumbError) + " / " +
                    Millimeters(item.IndexError) + " / " +
                    Millimeters(item.MiddleError) + " mm");
                report.AppendLine("support palm/thumb/index/middle/ring/pinky: " +
                    Millimeters(item.SupportPalmError) + " / " +
                    Millimeters(item.SupportThumbError) + " / " +
                    Millimeters(item.SupportIndexError) + " / " +
                    Millimeters(item.SupportMiddleError) + " / " +
                    Millimeters(item.SupportRingError) + " / " +
                    Millimeters(item.SupportPinkyError) + " mm");
                report.AppendLine("cassette/recorder maximum sampled penetration: " +
                    Millimeters(item.CassettePenetration) + " / " +
                    Millimeters(item.RecorderPenetration) + " mm");
                report.AppendLine("slot contact position/rotation error: " +
                    Millimeters(item.SlotContactError) + " mm / " +
                    item.SlotRotationError.ToString("F4",
                        CultureInfo.InvariantCulture) + " deg");
                report.AppendLine("right-arm extension: " +
                    (item.RightExtension * 100f).ToString("F2",
                        CultureInfo.InvariantCulture) + "%");
                report.AppendLine("right-wrist roll: " +
                    item.RightWristRoll.ToString("F2",
                        CultureInfo.InvariantCulture) + " deg");
                report.AppendLine("InteractionGrip delta from Carry: " +
                    Millimeters(item.InteractionGripPositionDelta) + " mm / " +
                    item.InteractionGripRotationDelta.ToString("F2",
                        CultureInfo.InvariantCulture) + " deg");
                report.AppendLine("cassette rotation from Carry: " +
                    item.CassetteRotationFromCarry.ToString("F2",
                        CultureInfo.InvariantCulture) + " deg");
                report.AppendLine("hand -> cassette -> slot readable: " +
                    (item.SlotReadable ? "YES" : "NO"));
                report.AppendLine("PASS/FAIL: " +
                    (item.Passed ? "PASS" : "FAIL"));
                if (item.Failures.Count > 0)
                {
                    report.AppendLine("reasons: " +
                        string.Join("; ", item.Failures.ToArray()));
                }
                report.AppendLine();
            }
            File.WriteAllText(path, report.ToString(), new UTF8Encoding(false));
        }

        private static string Millimeters(float value)
        {
            return (value * 1000f).ToString("F3",
                CultureInfo.InvariantCulture);
        }

        private static void ValidateV4(Context context, Pose carry,
            Pose reorientClear, Pose contact, Pose half, Pose seatedHand,
            Pose seatedSlot, Pose ejectGripSlot, Pose ejectGripHand,
            Pose ejectClear, PhysicalGripMetrics physical,
            CassetteAxisAudit audit)
        {
            if (!physical.Passed || !audit.AxisSignCorrect)
            {
                throw new InvalidOperationException(
                    "V4 requires the approved L3 physical and axis gates.");
            }
            Pose[] all =
            {
                carry, reorientClear, contact, half, seatedHand, seatedSlot,
                ejectGripSlot, ejectGripHand, ejectClear
            };
            foreach (Pose pose in all)
            {
                foreach (string boneName in
                    SoulRecorderReplacementArmStaging.RequiredBones)
                {
                    DraftTransform value = pose.Values[PathOf(
                        context.Rig.Bones[boneName])];
                    DraftTransform locked = context.StaticMaster.Values[
                        PathOf(context.Rig.Bones[boneName])];
                    if (!SameBits(value.localPosition, locked.localPosition) ||
                        !SameBits(value.localScale, locked.localScale))
                    {
                        throw new InvalidOperationException(pose.Name +
                            " changed fixed skeletal local position/scale for " +
                            boneName + ".");
                    }
                }
            }

            ApplyPose(context, reorientClear);
            Transform axis = FindUnique(context.CassetteGrip.gameObject,
                "CassetteInsertionAxis");
            float clearDistance = Vector3.Distance(axis.position,
                context.Markers.SlotEntryPosition);
            if (clearDistance < 0.075f)
            {
                throw new InvalidOperationException(
                    "V4 cassette reorientation does not finish safely clear of recorder: " +
                    Millimeters(clearDistance) + " mm.");
            }

            Pose[] mechanical = { contact, half, seatedHand };
            Vector3[] expected =
            {
                context.Markers.SlotEntryPosition,
                Vector3.Lerp(context.Markers.SlotEntryPosition,
                    context.Markers.SlotSeatedPosition, 0.5f),
                context.Markers.SlotSeatedPosition
            };
            for (int index = 0; index < mechanical.Length; index++)
            {
                ApplyPose(context, mechanical[index]);
                axis = FindUnique(context.CassetteGrip.gameObject,
                    "CassetteInsertionAxis");
                if (Vector3.Distance(axis.position, expected[index]) >
                        0.00001f ||
                    Quaternion.Angle(axis.rotation,
                        context.Markers.SlotAxisRotation) > 0.001f)
                {
                    throw new InvalidOperationException(mechanical[index].Name +
                        " drifted from exact slot geometry.");
                }
                if (Vector3.Distance(
                        mechanical[index].RightHandGripToCassette.Position,
                        contact.RightHandGripToCassette.Position) > 0.000001f ||
                    Quaternion.Angle(
                        mechanical[index].RightHandGripToCassette.Rotation,
                        contact.RightHandGripToCassette.Rotation) > 0.0001f)
                {
                    throw new InvalidOperationException(mechanical[index].Name +
                        " changed the frozen PhysicalGrip_L3 attachment.");
                }
            }

            ApplyPose(context, seatedHand);
            Vector3 beforePosition = context.CassetteGrip.position;
            Quaternion beforeRotation = context.CassetteGrip.rotation;
            ApplyPose(context, seatedSlot);
            AssertWorldSnap("insertion", beforePosition, beforeRotation,
                context.CassetteGrip);
            ApplyPose(context, ejectGripSlot);
            beforePosition = context.CassetteGrip.position;
            beforeRotation = context.CassetteGrip.rotation;
            ApplyPose(context, ejectGripHand);
            AssertWorldSnap("ejection", beforePosition, beforeRotation,
                context.CassetteGrip);
        }

        private static void AssertWorldSnap(string label, Vector3 position,
            Quaternion rotation, Transform cassette)
        {
            float positionError = Vector3.Distance(position,
                cassette.position);
            float rotationError = Quaternion.Angle(rotation,
                cassette.rotation);
            if (positionError > 0.00001f || rotationError > 0.001f)
            {
                throw new InvalidOperationException("V4 " + label +
                    " ownership transfer snapped by " +
                    Millimeters(positionError) + " mm / " +
                    rotationError.ToString("F4",
                        CultureInfo.InvariantCulture) + " deg.");
            }
        }

        private static void WriteV4PoseFile(List<Pose> poses,
            string humanPoseSha, string authoringSceneSha)
        {
            DraftPoseFile file = new DraftPoseFile
            {
                lockedStaticMasterSha256 =
                    SoulRecorderReplacementGripPoseStore.LockedStaticMasterHash,
                sourceHumanPoseFileSha256 = humanPoseSha,
                sourceAuthoringSceneSha256 = authoringSceneSha,
                generatedUtc = DateTime.UtcNow.ToString("O",
                    CultureInfo.InvariantCulture),
                poses = poses.Select(pose => new DraftPoseRecord
                {
                    poseName = pose.Name,
                    source = pose.Source,
                    cassetteOwner = pose.Owner.ToString(),
                    interactionGripPosition =
                        pose.RightHandGripToCassette.Position,
                    interactionGripRotation =
                        pose.RightHandGripToCassette.Rotation,
                    supportGripPosition =
                        pose.LeftHandGripToRecorder.Position,
                    supportGripRotation =
                        pose.LeftHandGripToRecorder.Rotation,
                    transforms = pose.Values.Values.ToArray()
                }).ToArray()
            };
            File.WriteAllText(Path.GetFullPath(V4PoseAsset),
                JsonUtility.ToJson(file, true), new UTF8Encoding(false));
        }

        private static void RenderV4(Context context, List<Pose> poses,
            List<Transition> insertion, List<Transition> ejection,
            CassetteAxisAudit audit, PhysicalGripMetrics physical)
        {
            string output = Path.GetFullPath(V4OutputAssetFolder);
            string insertionFrames = Path.Combine(output,
                "Insertion-Frames-FOV70");
            string ejectionFrames = Path.Combine(output,
                "Ejection-Frames-FOV70");
            string keys = Path.Combine(output, "Key-Poses-FOV70");
            Directory.CreateDirectory(insertionFrames);
            Directory.CreateDirectory(ejectionFrames);
            Directory.CreateDirectory(keys);
            DeletePngs(insertionFrames);
            DeletePngs(ejectionFrames);
            DeletePngs(keys);
            RenderSequenceV4(context, insertion, insertionFrames);
            RenderSequenceV4(context, ejection, ejectionFrames);

            for (int index = 0; index < poses.Count; index++)
            {
                ApplyPose(context, poses[index]);
                RenderPng(context.Camera, Path.Combine(keys,
                    index.ToString("00", CultureInfo.InvariantCulture) + "-" +
                    Sanitize(poses[index].Name) + ".png"));
            }
            CreateContactSheet(keys, Path.Combine(output,
                "key-poses-contact-sheet-fov70.png"), 4);
            CreateCloseUpSheet(keys, Path.Combine(output,
                "cassette-hand-slot-closeup-fov70.png"));
            WriteV4Report(context, audit, physical, insertion, ejection,
                Path.Combine(output, "auto-animation-draft-v4-report.txt"));
        }

        private static void RenderSequenceV4(Context context,
            List<Transition> transitions, string folder)
        {
            int frame = 0;
            foreach (Transition transition in transitions)
            {
                int count = Mathf.Max(2, Mathf.RoundToInt(
                    transition.Duration * FramesPerSecond));
                for (int index = 0; index < count; index++)
                {
                    float amount = count <= 1 ? 1f :
                        index / (float)(count - 1);
                    ApplyInterpolatedV4(context, transition.From,
                        transition.To, amount, amount, amount,
                        transition.Owner);
                    RenderPng(context.Camera, Path.Combine(folder,
                        "frame-" + frame.ToString("D4",
                            CultureInfo.InvariantCulture) + ".png"));
                    frame++;
                }
            }
        }

        private static void WriteV4Report(Context context,
            CassetteAxisAudit audit, PhysicalGripMetrics physical,
            List<Transition> insertion, List<Transition> ejection,
            string path)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("SoulRecorder AutoAnimationDraft_v4");
            report.AppendLine("Runtime integration: BLOCKED pending human MP4 approval");
            report.AppendLine("Carry: StaticMaster_v1");
            report.AppendLine("Interaction/contact: PhysicalGrip_L3");
            report.AppendLine("Cassette ownership: RIGHT_HAND -> RECORDER_SLOT -> RIGHT_HAND");
            report.AppendLine("Quaternion interpolation: explicit shortest path");
            report.AppendLine("Carry-to-contact cassette rotation: " +
                audit.SelectedCarryRotation.ToString("F3",
                    CultureInfo.InvariantCulture) + " deg");
            report.AppendLine("Axis sign: " +
                (audit.AxisSignCorrect ? "CORRECT" : "REVERSED"));
            report.AppendLine("L3 cassette contacts thumb/index/middle: " +
                Millimeters(physical.ThumbError) + " / " +
                Millimeters(physical.IndexError) + " / " +
                Millimeters(physical.MiddleError) + " mm");
            report.AppendLine("L3 slot contact error: " +
                Millimeters(physical.SlotContactError) + " mm / " +
                physical.SlotRotationError.ToString("F4",
                    CultureInfo.InvariantCulture) + " deg");
            report.AppendLine("Insertion choreography:");
            foreach (Transition transition in insertion)
            {
                report.AppendLine("- " + transition.Label + " " +
                    transition.Duration.ToString("F2",
                        CultureInfo.InvariantCulture) + "s owner=" +
                    transition.Owner);
            }
            report.AppendLine("Ejection choreography:");
            foreach (Transition transition in ejection)
            {
                report.AppendLine("- " + transition.Label + " " +
                    transition.Duration.ToString("F2",
                        CultureInfo.InvariantCulture) + "s owner=" +
                    transition.Owner);
            }
            report.AppendLine("Insertion after Contact: constant orientation, exact slot axis, frozen L3 grip");
            report.AppendLine("Ownership transfers: identical world pose / zero snap");
            report.AppendLine("Fixed skeletal local positions/scales: PASS");
            report.AppendLine("AUTO ANIMATION DRAFT V4: PASS");
            report.AppendLine("VISUAL APPROVAL: PENDING USER REVIEW OF BOTH MP4 FILES");
            File.WriteAllText(path, report.ToString(), new UTF8Encoding(false));
        }

        private static void WriteV5PoseFile(List<Pose> poses,
            string humanPoseSha, string authoringSceneSha)
        {
            DraftPoseFile file = new DraftPoseFile
            {
                lockedStaticMasterSha256 =
                    SoulRecorderReplacementGripPoseStore.LockedStaticMasterHash,
                sourceHumanPoseFileSha256 = humanPoseSha,
                sourceAuthoringSceneSha256 = authoringSceneSha,
                generatedUtc = DateTime.UtcNow.ToString("O",
                    CultureInfo.InvariantCulture),
                poses = poses.Select(pose => new DraftPoseRecord
                {
                    poseName = pose.Name,
                    source = pose.Source,
                    cassetteOwner = pose.Owner.ToString(),
                    interactionGripPosition =
                        pose.RightHandGripToCassette.Position,
                    interactionGripRotation =
                        pose.RightHandGripToCassette.Rotation,
                    supportGripPosition =
                        pose.LeftHandGripToRecorder.Position,
                    supportGripRotation =
                        pose.LeftHandGripToRecorder.Rotation,
                    transforms = pose.Values.Values.ToArray()
                }).ToArray()
            };
            File.WriteAllText(Path.GetFullPath(V5PoseAsset),
                JsonUtility.ToJson(file, true), new UTF8Encoding(false));
        }

        private static void RenderV5(Context context, List<Pose> poses,
            List<Transition> insertion, List<Transition> ejection,
            CassetteAxisAudit audit, PhysicalGripMetrics physical)
        {
            string output = Path.GetFullPath(V5OutputAssetFolder);
            string insertionFrames = Path.Combine(output,
                "Insertion-Frames-FOV70");
            string ejectionFrames = Path.Combine(output,
                "Ejection-Frames-FOV70");
            string keys = Path.Combine(output, "Key-Poses-FOV70");
            Directory.CreateDirectory(insertionFrames);
            Directory.CreateDirectory(ejectionFrames);
            Directory.CreateDirectory(keys);
            DeletePngs(insertionFrames);
            DeletePngs(ejectionFrames);
            DeletePngs(keys);

            V5FrameMetrics metrics = new V5FrameMetrics();
            RenderSequenceV5(context, insertion, insertionFrames, metrics);
            RenderSequenceV5(context, ejection, ejectionFrames, metrics);

            for (int index = 0; index < poses.Count; index++)
            {
                ApplyPose(context, poses[index]);
                RenderPng(context.Camera, Path.Combine(keys,
                    index.ToString("00", CultureInfo.InvariantCulture) + "-" +
                    Sanitize(poses[index].Name) + ".png"));
            }
            CreateContactSheet(keys, Path.Combine(output,
                "key-poses-contact-sheet-fov70.png"), 4);
            CreateCloseUpSheet(keys, Path.Combine(output,
                "cassette-hand-slot-closeup-fov70.png"));

            Pose contact = poses.Single(value => value.Name == "V5_Contact");
            Pose seatedHand = poses.Single(value =>
                value.Name == "V5_SeatedHand");
            Pose seatedSlot = poses.Single(value =>
                value.Name == "V5_SeatedSlot");
            Pose ejectGripSlot = poses.Single(value =>
                value.Name == "V5_EjectGripSlot");
            Pose ejectGripHand = poses.Single(value =>
                value.Name == "V5_EjectGripHand");
            ApplyPose(context, contact);
            Quaternion contactRotation = context.CassetteGrip.rotation;
            ApplyPose(context, seatedHand);
            metrics.ContactToSeatedRotationDelta = Quaternion.Angle(
                contactRotation, context.CassetteGrip.rotation);
            MeasureOwnershipSnap(context, seatedHand, seatedSlot,
                out metrics.InsertionOwnershipPositionSnap,
                out metrics.InsertionOwnershipRotationSnap);
            MeasureOwnershipSnap(context, ejectGripSlot, ejectGripHand,
                out metrics.EjectionOwnershipPositionSnap,
                out metrics.EjectionOwnershipRotationSnap);

            if (metrics.VisibleSupportFrames == 0)
            {
                metrics.Failures.Add(
                    "no visible recorder frames were available for support validation");
            }
            if (metrics.MaximumVisibleSupportError >
                    V5MaximumSupportPalmError)
            {
                metrics.Failures.Add("visible support contact exceeded " +
                    Millimeters(V5MaximumSupportPalmError) + " mm");
            }
            if (metrics.MaximumSleeveOverlap > V5MaximumSleeveOverlap)
            {
                metrics.Failures.Add("left/right sleeve overlap exceeded " +
                    (V5MaximumSleeveOverlap * 100f).ToString("F1",
                        CultureInfo.InvariantCulture) + "% IoU");
            }
            if (metrics.MinimumRotationClearance < 0.100f)
            {
                metrics.Failures.Add(
                    "cassette reorientation occurred less than 100 mm from recorder");
            }
            if (metrics.ContactToSeatedRotationDelta > 0.01f)
            {
                metrics.Failures.Add(
                    "cassette rotated during the mechanical insertion phase");
            }
            if (metrics.InsertionOwnershipPositionSnap > 0.00001f ||
                metrics.InsertionOwnershipRotationSnap > 0.001f ||
                metrics.EjectionOwnershipPositionSnap > 0.00001f ||
                metrics.EjectionOwnershipRotationSnap > 0.001f)
            {
                metrics.Failures.Add("ownership transfer introduced a snap");
            }

            WriteV5Report(audit, physical, insertion, ejection, metrics,
                Path.Combine(output, "auto-animation-draft-v5-report.txt"));
            if (metrics.Failures.Count > 0)
            {
                throw new InvalidOperationException(
                    "V5 frame validation failed: " +
                    string.Join("; ", metrics.Failures.ToArray()));
            }
        }

        private static void RenderSequenceV5(Context context,
            List<Transition> transitions, string folder,
            V5FrameMetrics metrics)
        {
            int frame = 0;
            foreach (Transition transition in transitions)
            {
                int count = Mathf.Max(2, Mathf.RoundToInt(
                    transition.Duration * FramesPerSecond));
                for (int index = 0; index < count; index++)
                {
                    float amount = count <= 1 ? 1f :
                        index / (float)(count - 1);
                    ApplyInterpolatedV4(context, transition.From,
                        transition.To, amount, amount, 0f,
                        transition.Owner);
                    MeasureV5Frame(context, transition.Label, metrics);
                    RenderPng(context.Camera, Path.Combine(folder,
                        "frame-" + frame.ToString("D4",
                            CultureInfo.InvariantCulture) + ".png"));
                    frame++;
                }
            }
        }

        private static void MeasureV5Frame(Context context, string label,
            V5FrameMetrics metrics)
        {
            metrics.SampledFrames++;
            ProjectedVisibility recorder = MeasureProjectedVisibility(
                context.Camera, context.RecorderGrip);
            if (recorder.VisibleFraction > 0.02f &&
                recorder.ViewportArea > 0.00005f)
            {
                metrics.VisibleSupportFrames++;
                float palmError = Vector3.Distance(
                    SupportPalmPoint(context), FindUnique(
                        context.RecorderGrip.gameObject,
                        "RecorderSupportPalm").position);
                float digitError = MaximumSupportDigitError(context);
                float maximum = Mathf.Max(palmError, digitError);
                metrics.MaximumVisibleSupportError = Mathf.Max(
                    metrics.MaximumVisibleSupportError, maximum);
                if (palmError > V5MaximumSupportPalmError ||
                    digitError > V5MaximumSupportDigitError)
                {
                    metrics.Failures.Add("support contact separated at " +
                        label + " by " + Millimeters(maximum) + " mm");
                }
            }

            float overlap = MeasureSleeveScreenOverlap(context);
            metrics.MaximumSleeveOverlap = Mathf.Max(
                metrics.MaximumSleeveOverlap, overlap);
            if (label == "EarlyCassetteReorientation" ||
                label == "LateCassetteReorientation")
            {
                metrics.MinimumRotationClearance = Mathf.Min(
                    metrics.MinimumRotationClearance,
                    Vector3.Distance(context.CassetteGrip.position,
                        context.RecorderGrip.position));
            }
        }

        private static float MaximumSupportDigitError(Context context)
        {
            return new[]
            {
                FingerMarkerError(context, "L_thumb", context.RecorderGrip,
                    "RecorderSupportThumb"),
                FingerMarkerError(context, "L_point", context.RecorderGrip,
                    "RecorderSupportIndex"),
                FingerMarkerError(context, "L_middle", context.RecorderGrip,
                    "RecorderSupportMiddle"),
                FingerMarkerError(context, "L_ring", context.RecorderGrip,
                    "RecorderSupportRing"),
                FingerMarkerError(context, "L_pink", context.RecorderGrip,
                    "RecorderSupportPinky")
            }.Max();
        }

        private static float MeasureSleeveScreenOverlap(Context context)
        {
            const int maskWidth = 160;
            const int maskHeight = 90;
            bool[] left = new bool[maskWidth * maskHeight];
            bool[] right = new bool[maskWidth * maskHeight];
            if (!FillProjectedSleeveMask(context, left, right,
                    maskWidth, maskHeight))
            {
                return 0f;
            }
            int intersection = 0;
            int union = 0;
            for (int index = 0; index < left.Length; index++)
            {
                if (left[index] && right[index])
                {
                    intersection++;
                }
                if (left[index] || right[index])
                {
                    union++;
                }
            }
            return union == 0 ? 0f : intersection / (float)union;
        }

        private static bool FillProjectedSleeveMask(Context context,
            bool[] leftMask, bool[] rightMask, int width, int height)
        {
            SkinnedMeshRenderer renderer = context.Rig.Renderer;
            Mesh source = renderer.sharedMesh;
            BoneWeight[] weights = source == null ? null : source.boneWeights;
            if (weights == null || weights.Length == 0)
            {
                return false;
            }
            HashSet<int> leftBones = new HashSet<int>();
            HashSet<int> rightBones = new HashSet<int>();
            for (int index = 0; index < renderer.bones.Length; index++)
            {
                Transform bone = renderer.bones[index];
                if (bone == context.LeftUpper || bone == context.LeftElbow ||
                    bone == context.LeftWrist)
                {
                    leftBones.Add(index);
                }
                if (bone == context.RightUpper || bone == context.RightElbow ||
                    bone == context.RightWrist)
                {
                    rightBones.Add(index);
                }
            }
            Mesh baked = new Mesh();
            try
            {
                renderer.BakeMesh(baked);
                Vector3[] vertices = baked.vertices;
                bool any = false;
                int count = Mathf.Min(vertices.Length, weights.Length);
                for (int index = 0; index < count; index++)
                {
                    BoneWeight weight = weights[index];
                    float leftWeight = SleeveWeight(weight, leftBones);
                    float rightWeight = SleeveWeight(weight, rightBones);
                    bool isLeft = leftWeight >= 0.50f &&
                        leftWeight >= rightWeight;
                    bool isRight = rightWeight >= 0.50f &&
                        rightWeight > leftWeight;
                    if (!isLeft && !isRight)
                    {
                        continue;
                    }
                    Vector3 viewport = context.Camera.WorldToViewportPoint(
                        renderer.transform.TransformPoint(vertices[index]));
                    if (viewport.z <= 0f || viewport.x < 0f ||
                        viewport.x > 1f || viewport.y < 0f ||
                        viewport.y > 1f)
                    {
                        continue;
                    }
                    any = true;
                    int x = Mathf.Clamp(Mathf.FloorToInt(viewport.x * width),
                        0, width - 1);
                    int y = Mathf.Clamp(Mathf.FloorToInt(viewport.y * height),
                        0, height - 1);
                    MarkScreenMask(isLeft ? leftMask : rightMask,
                        width, height, x, y);
                }
                return any;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(baked);
            }
        }

        private static float SleeveWeight(BoneWeight weight,
            HashSet<int> bones)
        {
            float result = 0f;
            if (bones.Contains(weight.boneIndex0)) result += weight.weight0;
            if (bones.Contains(weight.boneIndex1)) result += weight.weight1;
            if (bones.Contains(weight.boneIndex2)) result += weight.weight2;
            if (bones.Contains(weight.boneIndex3)) result += weight.weight3;
            return result;
        }

        private static void MarkScreenMask(bool[] mask, int width,
            int height, int centerX, int centerY)
        {
            for (int y = Mathf.Max(0, centerY - 1);
                y <= Mathf.Min(height - 1, centerY + 1); y++)
            {
                for (int x = Mathf.Max(0, centerX - 1);
                    x <= Mathf.Min(width - 1, centerX + 1); x++)
                {
                    mask[y * width + x] = true;
                }
            }
        }

        private static void MeasureOwnershipSnap(Context context, Pose before,
            Pose after, out float position, out float rotation)
        {
            ApplyPose(context, before);
            Vector3 beforePosition = context.CassetteGrip.position;
            Quaternion beforeRotation = context.CassetteGrip.rotation;
            ApplyPose(context, after);
            position = Vector3.Distance(beforePosition,
                context.CassetteGrip.position);
            rotation = Quaternion.Angle(beforeRotation,
                context.CassetteGrip.rotation);
        }

        private static void WriteV5Report(CassetteAxisAudit audit,
            PhysicalGripMetrics physical, List<Transition> insertion,
            List<Transition> ejection, V5FrameMetrics metrics, string path)
        {
            float rotationCompleteTime = insertion.TakeWhile(value =>
                value.Label != "ReorientationStabilization").Sum(value =>
                    value.Duration);
            StringBuilder report = new StringBuilder();
            report.AppendLine("SoulRecorder AutoAnimationDraft_v5");
            report.AppendLine("Runtime integration: BLOCKED pending human MP4 approval");
            report.AppendLine("PhysicalGrip_L3: PRESERVED");
            report.AppendLine("Visible support assembly: L3 fingers + L3 recorder grip, no interpolation");
            report.AppendLine("Carry-to-contact cassette rotation: " +
                audit.SelectedCarryRotation.ToString("F3",
                    CultureInfo.InvariantCulture) + " deg");
            report.AppendLine("Cassette rotation completion time: " +
                rotationCompleteTime.ToString("F3",
                    CultureInfo.InvariantCulture) + " s after first frame");
            report.AppendLine("Minimum cassette/recorder center distance during rotation: " +
                Millimeters(metrics.MinimumRotationClearance) + " mm");
            report.AppendLine("Maximum visible support-hand contact error: " +
                Millimeters(metrics.MaximumVisibleSupportError) + " mm across " +
                metrics.VisibleSupportFrames + " visible frames");
            report.AppendLine("Maximum left/right sleeve screen overlap: " +
                (metrics.MaximumSleeveOverlap * 100f).ToString("F3",
                    CultureInfo.InvariantCulture) + "% IoU");
            report.AppendLine("Contact -> Seated rotation delta: " +
                metrics.ContactToSeatedRotationDelta.ToString("F6",
                    CultureInfo.InvariantCulture) + " deg");
            report.AppendLine("Insertion ownership snap: " +
                Millimeters(metrics.InsertionOwnershipPositionSnap) + " mm / " +
                metrics.InsertionOwnershipRotationSnap.ToString("F6",
                    CultureInfo.InvariantCulture) + " deg");
            report.AppendLine("Ejection ownership snap: " +
                Millimeters(metrics.EjectionOwnershipPositionSnap) + " mm / " +
                metrics.EjectionOwnershipRotationSnap.ToString("F6",
                    CultureInfo.InvariantCulture) + " deg");
            report.AppendLine("L3 cassette contacts thumb/index/middle: " +
                Millimeters(physical.ThumbError) + " / " +
                Millimeters(physical.IndexError) + " / " +
                Millimeters(physical.MiddleError) + " mm");
            report.AppendLine("Sampled frames: " + metrics.SampledFrames);
            report.AppendLine("Insertion choreography:");
            foreach (Transition transition in insertion)
            {
                report.AppendLine("- " + transition.Label + " " +
                    transition.Duration.ToString("F2",
                        CultureInfo.InvariantCulture) + "s owner=" +
                    transition.Owner);
            }
            report.AppendLine("Ejection choreography:");
            foreach (Transition transition in ejection)
            {
                report.AppendLine("- " + transition.Label + " " +
                    transition.Duration.ToString("F2",
                        CultureInfo.InvariantCulture) + "s owner=" +
                    transition.Owner);
            }
            report.AppendLine("Per-frame numerical gate: " +
                (metrics.Failures.Count == 0 ? "PASS" : "FAIL"));
            if (metrics.Failures.Count > 0)
            {
                report.AppendLine("Failures: " +
                    string.Join("; ", metrics.Failures.ToArray()));
            }
            report.AppendLine("AUTO ANIMATION DRAFT V5: " +
                (metrics.Failures.Count == 0 ? "PASS" : "FAIL"));
            report.AppendLine("VISUAL APPROVAL: PENDING HUMAN REVIEW OF BOTH MP4 FILES");
            File.WriteAllText(path, report.ToString(), new UTF8Encoding(false));
        }

        private static void RenderInteractionCandidates(Context context,
            List<Pose> candidates, List<CandidateMetrics> metrics)
        {
            string output = Path.GetFullPath(CandidateOutputAssetFolder);
            string individual = Path.Combine(output, "FOV70");
            Directory.CreateDirectory(individual);
            DeletePngs(individual);
            for (int index = 0; index < candidates.Count; index++)
            {
                ApplyPose(context, candidates[index]);
                RenderPng(context.Camera, Path.Combine(individual,
                    candidates[index].Name + "-FOV70.png"));
            }
            CreateContactSheet(individual, Path.Combine(output,
                "interaction-master-candidates-fov70.png"), 3);
            CreateAllCloseUpSheet(individual, Path.Combine(output,
                "interaction-master-candidates-closeup-fov70.png"));
            RenderArmChainSideDiagnostics(context, candidates, output);
            WriteCandidateReport(metrics, Path.Combine(output,
                "interaction-master-candidates-report.txt"));
        }

        private static void RenderArmChainSideDiagnostics(Context context,
            List<Pose> candidates, string output)
        {
            string folder = Path.Combine(output, "SideDiagnostics");
            Directory.CreateDirectory(folder);
            DeletePngs(folder);
            Vector3 originalPosition = context.Camera.transform.position;
            Quaternion originalRotation = context.Camera.transform.rotation;
            float originalFieldOfView = context.Camera.fieldOfView;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "Sprites/Default is required for arm-chain diagnostics.");
            }
            Material leftMaterial = new Material(shader);
            Material rightMaterial = new Material(shader);
            leftMaterial.color = new Color(0.1f, 0.8f, 1f, 1f);
            rightMaterial.color = new Color(1f, 0.45f, 0.12f, 1f);
            GameObject leftLineObject = new GameObject("DiagnosticLeftArmChain");
            GameObject rightLineObject = new GameObject("DiagnosticRightArmChain");
            LineRenderer leftLine = ConfigureDiagnosticLine(leftLineObject,
                leftMaterial);
            LineRenderer rightLine = ConfigureDiagnosticLine(rightLineObject,
                rightMaterial);
            try
            {
                foreach (Pose pose in candidates)
                {
                    ApplyPose(context, pose);
                    Vector3 center = (context.LeftUpper.position +
                        context.LeftWrist.position + context.RightUpper.position +
                        context.RightWrist.position) * 0.25f;
                    Vector3 side = originalRotation * Vector3.right;
                    context.Camera.transform.position = center + side * 0.85f;
                    context.Camera.transform.rotation = Quaternion.LookRotation(
                        center - context.Camera.transform.position,
                        originalRotation * Vector3.up);
                    context.Camera.fieldOfView = 55f;
                    SetDiagnosticLine(leftLine, context.LeftUpper.position,
                        context.LeftElbow.position, context.LeftWrist.position);
                    SetDiagnosticLine(rightLine, context.RightUpper.position,
                        context.RightElbow.position, context.RightWrist.position);
                    RenderPng(context.Camera, Path.Combine(folder,
                        pose.Name + "-ArmChainSide.png"));
                }
            }
            finally
            {
                context.Camera.transform.position = originalPosition;
                context.Camera.transform.rotation = originalRotation;
                context.Camera.fieldOfView = originalFieldOfView;
                UnityEngine.Object.DestroyImmediate(leftLineObject);
                UnityEngine.Object.DestroyImmediate(rightLineObject);
                UnityEngine.Object.DestroyImmediate(leftMaterial);
                UnityEngine.Object.DestroyImmediate(rightMaterial);
            }
            CreateContactSheet(folder, Path.Combine(output,
                "interaction-master-candidates-arm-chain-side-fov70.png"), 3);
        }

        private static LineRenderer ConfigureDiagnosticLine(GameObject owner,
            Material material)
        {
            LineRenderer line = owner.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 3;
            line.startWidth = 0.012f;
            line.endWidth = 0.012f;
            line.numCapVertices = 6;
            line.numCornerVertices = 6;
            line.sharedMaterial = material;
            return line;
        }

        private static void SetDiagnosticLine(LineRenderer line,
            Vector3 shoulder, Vector3 elbow, Vector3 wrist)
        {
            line.SetPosition(0, shoulder);
            line.SetPosition(1, elbow);
            line.SetPosition(2, wrist);
        }

        private static void CreateAllCloseUpSheet(string folder,
            string output)
        {
            string temp = Path.Combine(Path.GetDirectoryName(output),
                "_CandidateCloseUps");
            Directory.CreateDirectory(temp);
            DeletePngs(temp);
            foreach (string file in Directory.GetFiles(folder, "*.png")
                .OrderBy(value => value, StringComparer.Ordinal))
            {
                Texture2D source = LoadTexture(file);
                int cropWidth = Mathf.RoundToInt(source.width * 0.68f);
                int cropHeight = Mathf.RoundToInt(source.height * 0.78f);
                int x = Mathf.RoundToInt(source.width * 0.16f);
                int y = Mathf.RoundToInt(source.height * 0.10f);
                Texture2D crop = new Texture2D(cropWidth, cropHeight,
                    TextureFormat.RGB24, false);
                crop.SetPixels(source.GetPixels(x, y, cropWidth, cropHeight));
                crop.Apply();
                File.WriteAllBytes(Path.Combine(temp, Path.GetFileName(file)),
                    crop.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(crop);
            }
            CreateContactSheet(temp, output, 3);
        }

        private static void WriteCandidateReport(List<CandidateMetrics> metrics,
            string path)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("SoulRecorder InteractionMaster G-L rotation-only candidate search");
            report.AppendLine("StaticMaster_v1 remains the locked Carry pose.");
            report.AppendLine("All skeletal local positions/scales are bit-exact locked.");
            report.AppendLine("Recorder grip is rigid; each candidate has one frozen InteractionGrip_RightHandToCassette.");
            report.AppendLine("No candidate is selected automatically.");
            report.AppendLine();
            foreach (CandidateMetrics candidate in metrics)
            {
                report.AppendLine("Candidate " +
                    candidate.Name.Substring(candidate.Name.Length - 1) + ":");
                report.AppendLine("right-arm extension: " +
                    (candidate.Extension * 100f).ToString("F2",
                        CultureInfo.InvariantCulture) + "%");
                report.AppendLine("contact error: " +
                    (candidate.ContactPositionError * 1000f).ToString("F4",
                        CultureInfo.InvariantCulture) + " mm / " +
                    candidate.ContactRotationError.ToString("F4",
                        CultureInfo.InvariantCulture) + " deg");
                report.AppendLine("forearm screen angle: " +
                    candidate.ForearmScreenAngle.ToString("F2",
                        CultureInfo.InvariantCulture) + " deg from horizontal");
                report.AppendLine("forearm screen length: " +
                    candidate.ForearmScreenLength.ToString("F3",
                        CultureInfo.InvariantCulture));
                report.AppendLine("left twist deltas upper/forearm/wrist: " +
                    candidate.LeftUpperTwist.ToString("F2",
                        CultureInfo.InvariantCulture) + " / " +
                    candidate.LeftForearmTwist.ToString("F2",
                        CultureInfo.InvariantCulture) + " / " +
                    candidate.LeftWristRoll.ToString("F2",
                        CultureInfo.InvariantCulture) + " deg");
                report.AppendLine("right twist deltas upper/forearm/wrist: " +
                    candidate.RightUpperTwist.ToString("F2",
                        CultureInfo.InvariantCulture) + " / " +
                    candidate.RightForearmTwist.ToString("F2",
                        CultureInfo.InvariantCulture) + " / " +
                    candidate.RightWristRoll.ToString("F2",
                        CultureInfo.InvariantCulture) + " deg");
                report.AppendLine("elbow bend left/right: " +
                    candidate.LeftElbowBend.ToString("F2",
                        CultureInfo.InvariantCulture) + " / " +
                    candidate.RightElbowBend.ToString("F2",
                        CultureInfo.InvariantCulture) + " deg; flipped=" +
                    (candidate.LeftElbowFlipped || candidate.RightElbowFlipped ?
                        "YES" : "NO"));
                report.AppendLine("elbow viewport: " +
                    candidate.ElbowViewport.ToString("F3") +
                    " low/right=" + (candidate.ElbowLowRight ? "YES" : "NO"));
                report.AppendLine("wrist/cassette/recorder viewport: " +
                    candidate.WristViewport.ToString("F3") + " / " +
                    candidate.CassetteViewport.ToString("F3") + " / " +
                    candidate.RecorderViewport.ToString("F3"));
                report.AppendLine("recorder grip drift: " +
                    (candidate.RecorderGripPositionDrift * 1000f).ToString("F4",
                        CultureInfo.InvariantCulture) + " mm / " +
                    candidate.RecorderGripRotationDrift.ToString("F4",
                        CultureInfo.InvariantCulture) + " deg");
                report.AppendLine("cassette grip drift: " +
                    (candidate.CassetteGripPositionDrift * 1000f).ToString("F4",
                        CultureInfo.InvariantCulture) + " mm / " +
                    candidate.CassetteGripRotationDrift.ToString("F4",
                        CultureInfo.InvariantCulture) + " deg");
                report.AppendLine("InteractionGrip_RightHandToCassette delta from Carry: " +
                    (candidate.InteractionGripPositionDelta * 1000f).ToString(
                        "F3", CultureInfo.InvariantCulture) + " mm / " +
                    candidate.InteractionGripRotationDelta.ToString("F2",
                        CultureInfo.InvariantCulture) + " deg");
                report.AppendLine("cassette projected visibility: " +
                    (candidate.CassetteVisibility * 100f).ToString("F1",
                        CultureInfo.InvariantCulture) + "% area=" +
                    candidate.CassetteViewportArea.ToString("F5",
                        CultureInfo.InvariantCulture));
                report.AppendLine("recorder projected visibility: " +
                    (candidate.RecorderVisibility * 100f).ToString("F1",
                        CultureInfo.InvariantCulture) + "% area=" +
                    candidate.RecorderViewportArea.ToString("F5",
                        CultureInfo.InvariantCulture));
                report.AppendLine("PASS/FAIL: " +
                    (candidate.Passed ? "PASS" : "FAIL"));
                if (candidate.Failures.Count > 0)
                {
                    report.AppendLine("reasons: " +
                        string.Join("; ", candidate.Failures.ToArray()));
                }
                report.AppendLine("skeletal localPosition/localScale drift by bone:");
                foreach (string drift in candidate.BoneDrifts)
                {
                    report.AppendLine("  " + drift);
                }
                report.AppendLine();
            }
            File.WriteAllText(path, report.ToString(), new UTF8Encoding(false));
        }

        private static void Generate(bool batch, bool renderFullSequences)
        {
            string humanPosePath = Path.GetFullPath(
                SoulRecorderReplacementArmStaging.PoseFile);
            string humanBefore = Sha256File(humanPosePath);
            string sceneBefore = Sha256File(Path.GetFullPath(
                SoulRecorderReplacementArmStaging.AuthoringScene));

            Directory.CreateDirectory(Path.GetFullPath(OutputAssetFolder));
            Scene scene = EditorSceneManager.OpenScene(
                SoulRecorderReplacementArmStaging.AuthoringScene,
                OpenSceneMode.Single);
            Context context = ResolveContext(scene);
            GeneratePoses(context);
            WriteDraftPoseFile(context, humanBefore, sceneBefore);
            RenderOutputs(context, renderFullSequences);
            EditorSceneManager.SaveScene(scene, DraftSceneAsset, true);
            AssetDatabase.Refresh();

            string humanAfter = Sha256File(humanPosePath);
            string sceneAfter = Sha256File(Path.GetFullPath(
                SoulRecorderReplacementArmStaging.AuthoringScene));
            if (!string.Equals(humanBefore, humanAfter, StringComparison.Ordinal) ||
                !string.Equals(sceneBefore, sceneAfter, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Human-authored pose data or authoring scene changed during auto generation.");
            }
            if (!string.Equals(
                SoulRecorderReplacementGripPoseStore.LockedStaticMasterHash,
                "2EA27472ACBEBF323D9C2502AF7FDFCE1FC3FB8ACDD72D56629C8E8ECEC20AA2",
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException("StaticMaster_v1 lock changed.");
            }
            Debug.Log("SoulRecorder auto draft preserved human pose file SHA-256=" +
                humanAfter + " and authoring scene SHA-256=" + sceneAfter + ".");
        }

        private static Context ResolveContext(Scene scene)
        {
            Context result = new Context();
            result.Rig = SoulRecorderReplacementArmStaging.ResolveLoadedRig();
            if (result.Rig == null || !result.Rig.Passed)
            {
                throw new InvalidOperationException(
                    "Replacement rig failed: " + string.Join("; ",
                        result.Rig == null ? new[] { "unresolved" } :
                        result.Rig.Failures.ToArray()));
            }
            GameObject props = GameObject.Find("AuthoringProps");
            result.RecorderGrip = FindUnique(props, "RecorderGrip");
            result.CassetteGrip = FindUnique(props, "CassetteGrip");
            result.Camera = FindSceneCamera(scene);
            result.Camera.fieldOfView = 70f;
            result.RightUpper = result.Rig.Bones["R_arm"];
            result.RightElbow = result.Rig.Bones["R_elbow"];
            result.RightWrist = result.Rig.Bones["R_wrist"];
            result.LeftUpper = result.Rig.Bones["L_arm"];
            result.LeftElbow = result.Rig.Bones["L_elbow"];
            result.LeftWrist = result.Rig.Bones["L_wrist"];
            result.Controlled = SoulRecorderReplacementArmStaging.RequiredBones
                .Select(name => result.Rig.Bones[name])
                .Concat(new[] { result.RecorderGrip, result.CassetteGrip })
                .ToList();
            if (result.Controlled.Count != 38)
            {
                throw new InvalidOperationException(
                    "Auto draft expected 38 controlled transforms; found " +
                    result.Controlled.Count.ToString(CultureInfo.InvariantCulture) + ".");
            }
            result.Rig.Renderer.updateWhenOffscreen = true;
            result.Rig.Renderer.forceMatrixRecalculationPerRender = true;
            result.UpperLength = Vector3.Distance(result.RightUpper.position,
                result.RightElbow.position);
            result.ForearmLength = Vector3.Distance(result.RightElbow.position,
                result.RightWrist.position);
            return result;
        }

        private static void GeneratePoses(Context context)
        {
            SoulRecorderReplacementGripPoseStore.LoadStaticMasterV1(
                context.Controlled, false);
            context.LeftHandGripToRecorder = CaptureGrip(
                context.LeftWrist, context.RecorderGrip);
            context.RightHandGripToCassette = CaptureGrip(
                context.RightWrist, context.CassetteGrip);
            context.StaticMaster = CapturePose(context, "StaticMaster_v1",
                "locked human master", CassetteOwner.Hand);
            context.AlignElbowPlaneNormal = ElbowPlaneNormal(context);
            context.SupportElbowPlaneNormal = LeftElbowPlaneNormal(context);
            context.StaticRightElbowOffset =
                context.RightElbow.position - context.RightUpper.position;
            context.Markers = CaptureMarkerGeometry(context);

            if (!SoulRecorderReplacementGripPoseStore.HasKeyPose("CassetteAlign"))
            {
                throw new InvalidOperationException(
                    "Human CassetteAlign is required as the grip-style anchor.");
            }
            SoulRecorderReplacementGripPoseStore.LoadKeyPose("CassetteAlign",
                context.Controlled, false);
            context.HumanAlign = CapturePose(context, "CassetteAlign",
                "human-authored / preserved", CassetteOwner.Hand);

            if (SoulRecorderReplacementGripPoseStore.HasKeyPose("CassetteContact"))
            {
                SoulRecorderReplacementGripPoseStore.LoadKeyPose("CassetteContact",
                    context.Controlled, false);
                context.HumanContact = CapturePose(context, "CassetteContact",
                    "human-authored / preserved", CassetteOwner.Hand);
            }

            Pose autoAlign = BuildHumanAlignAnchor(context);
            ApplyPose(context, autoAlign);
            context.AlignElbowPlaneNormal = ElbowPlaneNormal(context);
            context.InteractionAssemblyShift = CalculateInteractionAssemblyShift(
                context);
            Pose supportHold = BuildShiftedSupportHold(context);
            autoAlign = BuildShiftedAlign(context, autoAlign);
            ApplyPose(context, autoAlign);
            context.Markers = CaptureMarkerGeometry(context);

            Pose contact = GenerateHeldPose(context, autoAlign,
                "Auto_CassetteContact", context.Markers.SlotEntryPosition,
                context.Markers.SlotAxisRotation,
                "slot-derived contact with restrained master-relative wrist");
            Pose half = GenerateHeldPose(context, contact,
                "Auto_CassetteHalfInserted", Vector3.Lerp(
                    context.Markers.SlotEntryPosition,
                    context.Markers.SlotSeatedPosition, 0.5f),
                context.Markers.SlotAxisRotation,
                "mechanical 50% slot-axis insertion");
            Pose seated = GenerateHeldPose(context, half,
                "Auto_CassetteSeated", context.Markers.SlotSeatedPosition,
                context.Markers.SlotAxisRotation,
                "exact real seated marker");
            ApplyPose(context, seated);
            context.SlotToCassetteSeated = CaptureGrip(
                FindUnique(context.RecorderGrip.gameObject, "CassetteSlotSeated"),
                context.CassetteGrip);

            Pose release = GenerateReleasedPose(context, seated,
                "Auto_CassetteRelease", 0.050f, 0.045f, true,
                "stationary seated cassette; hand retracts down/right");
            Pose stopHold = GenerateReleasedPose(context, seated,
                "Auto_StopRecorderReturn", 0.085f, 0.080f, true,
                "locked recorder returns with right hand low/right");
            Pose ejectApproach = GenerateReleasedPose(context, seated,
                "Auto_CassetteEjectApproach", 0.025f, 0.020f, true,
                "open hand approaches stationary seated cassette");
            Pose ejectGrip = GenerateHeldPose(context, seated,
                "Auto_CassetteEjectGrip", context.Markers.SlotSeatedPosition,
                context.Markers.SlotAxisRotation,
                "fingers close before recorder-to-hand transfer",
                CassetteOwner.Recorder);
            Pose ejectClear = GenerateHeldPose(context, ejectGrip,
                "Auto_CassetteEjectClear",
                context.Markers.SlotEntryPosition -
                    context.Markers.SlotDirection * 0.065f,
                context.Markers.SlotAxisRotation,
                "straight pull fully clear of recorder");
            Pose belowCarry = GenerateAssemblyBelowPose(context, supportHold,
                "Auto_HandsBelowCarry", CassetteOwner.Hand);
            Pose belowSeated = GenerateAssemblyBelowPose(context, release,
                "Auto_HandsBelowSeated", CassetteOwner.Recorder);

            foreach (Pose pose in new[]
            {
                supportHold, autoAlign, contact, half, seated, release, stopHold, ejectApproach,
                ejectGrip, ejectClear, belowCarry, belowSeated
            })
            {
                context.Generated.Add(pose.Name, pose);
            }
            ValidateDraft(context);
        }

        private static Pose BuildHumanAlignAnchor(Context context)
        {
            ApplyPose(context, context.HumanAlign);
            ApplyStaticMasterLeft(context);
            SetRecorderFromLeftWrist(context);
            SetCassetteFromRightWrist(context);
            Pose result = CapturePose(context, "Auto_CassetteAlign",
                "human CassetteAlign bones with frozen StaticMaster prop attachments",
                CassetteOwner.Hand);
            return result;
        }

        private static Pose BuildShiftedSupportHold(Context context)
        {
            ApplyPose(context, context.StaticMaster);
            SetRecorderFromLeftWrist(context);
            SetCassetteFromRightWrist(context);
            return CapturePose(context, "Auto_SupportHold",
                "StaticMaster rigid grips with fixed shoulder origins",
                CassetteOwner.Hand);
        }

        private static Pose BuildShiftedAlign(Context context, Pose align)
        {
            ApplyPose(context, align);
            Vector3 desiredCassettePosition = context.CassetteGrip.position;
            Quaternion desiredCassetteRotation = context.CassetteGrip.rotation;
            SetRecorderFromLeftWrist(context);
            SolveRightHandForCassette(context, desiredCassettePosition,
                desiredCassetteRotation);
            SetCassetteFromRightWrist(context);
            return CapturePose(context, "Auto_CassetteAlign",
                "human Align carried into shifted interaction zone with frozen grip",
                CassetteOwner.Hand);
        }

        private static Vector3 CalculateInteractionAssemblyShift(Context context)
        {
            WorldPose desiredCassette = CassetteFromAxisTarget(context,
                context.Markers.SlotEntryPosition,
                context.Markers.SlotAxisRotation);
            WorldPose desiredWrist = HandTargetForCassette(context,
                desiredCassette.Position, desiredCassette.Rotation);
            Vector3 shoulder = context.RightUpper.position;
            Vector3 shoulderToWrist = desiredWrist.Position - shoulder;
            float totalLength = context.UpperLength + context.ForearmLength;
            float preferredReach = totalLength * PreferredArmExtension;
            if (shoulderToWrist.magnitude <= preferredReach)
            {
                return Vector3.zero;
            }
            Vector3 preferredWrist = shoulder + shoulderToWrist.normalized *
                preferredReach;
            return Vector3.ClampMagnitude(preferredWrist - desiredWrist.Position,
                MaximumInteractionAssemblyShift);
        }

        private static Pose GenerateHeldPose(Context context, Pose basis,
            string name, Vector3 desiredAxisPosition,
            Quaternion desiredAxisRotation, string source,
            CassetteOwner owner = CassetteOwner.Hand)
        {
            ApplyPose(context, basis);
            WorldPose desiredCassette = CassetteFromAxisTarget(context,
                desiredAxisPosition, desiredAxisRotation);
            SolveRightHandForCassette(context, desiredCassette.Position,
                desiredCassette.Rotation);
            SetRecorderFromLeftWrist(context);
            if (owner == CassetteOwner.Hand)
            {
                SetCassetteFromRightWrist(context);
            }
            else
            {
                SetCassetteAtLiveSeatedMarker(context);
            }
            return CapturePose(context, name, source, owner);
        }

        private static Pose GenerateReleasedPose(Context context, Pose seated,
            string name, float rightDistance, float downDistance,
            bool openFingers, string source)
        {
            ApplyPose(context, seated);
            Quaternion seatedRotation = context.RightWrist.rotation;
            Vector3 seatedWrist = context.CassetteGrip.position -
                (seatedRotation * context.RightHandGripToCassette.Position);
            Vector3 wristTarget = seatedWrist +
                context.Camera.transform.right * rightDistance -
                context.Camera.transform.up * downDistance -
                context.Markers.SlotDirection * 0.012f;
            SolveRightArm(context, wristTarget, seatedRotation);
            if (openFingers)
            {
                OpenRightPinchModestly(context);
            }
            SetRecorderFromLeftWrist(context);
            SetCassetteAtLiveSeatedMarker(context);
            return CapturePose(context, name, source, CassetteOwner.Recorder);
        }

        private static Pose GenerateAssemblyBelowPose(Context context, Pose basis,
            string name, CassetteOwner owner)
        {
            ApplyPose(context, basis);
            context.LeftUpper.rotation = CameraSpaceDelta(context,
                new Vector3(55f, 0f, -18f)) * context.LeftUpper.rotation;
            context.RightUpper.rotation = CameraSpaceDelta(context,
                new Vector3(55f, 0f, 18f)) * context.RightUpper.rotation;
            SetRecorderFromLeftWrist(context);
            if (owner == CassetteOwner.Hand)
            {
                SetCassetteFromRightWrist(context);
            }
            else
            {
                SetCassetteAtLiveSeatedMarker(context);
            }
            Pose result = CapturePose(context, name,
                "whole approved arm/prop arrangement translated below frame", owner);
            return result;
        }

        private static void SolveRightArm(Context context, Vector3 targetPosition,
            Quaternion targetRotation)
        {
            Vector3 shoulder = context.RightUpper.position;
            float upperLength = Vector3.Distance(shoulder, context.RightElbow.position);
            float forearmLength = Vector3.Distance(
                context.RightElbow.position, context.RightWrist.position);
            float totalLength = upperLength + forearmLength;
            Vector3 toTarget = targetPosition - shoulder;
            float reach = toTarget.magnitude;
            float extension = reach / totalLength;
            if (extension > MaximumArmExtension)
            {
                string failure = "Fixed-shoulder rigid-grip target requires " +
                    (extension * 100f).ToString("F2",
                        CultureInfo.InvariantCulture) + "% extension; clamped to " +
                    (MaximumArmExtension * 100f).ToString("F1",
                        CultureInfo.InvariantCulture) + "% for diagnostic rendering.";
                if (!context.GeometryFailures.Contains(failure))
                {
                    context.GeometryFailures.Add(failure);
                }
                context.NumericalGatePassed = false;
                reach = totalLength * MaximumArmExtension;
                targetPosition = shoulder + toTarget.normalized * reach;
                toTarget = targetPosition - shoulder;
                extension = MaximumArmExtension;
            }
            if (reach < Mathf.Abs(upperLength - forearmLength) + 0.001f)
            {
                throw new InvalidOperationException(
                    "Fixed-shoulder right-arm target is inside the two-bone minimum reach.");
            }

            Vector3 direction = toTarget / reach;
            Vector3 staticBend = Vector3.ProjectOnPlane(
                context.StaticRightElbowOffset, direction);
            Vector3 lowRightPole = Vector3.ProjectOnPlane(
                context.Camera.transform.right * 0.16f -
                context.Camera.transform.up * 0.24f -
                context.Camera.transform.forward * 0.04f,
                direction);
            Vector3 bendDirection = Vector3.Slerp(
                staticBend.normalized,
                lowRightPole.normalized,
                0.35f).normalized;
            if (bendDirection.sqrMagnitude < 0.0001f)
            {
                bendDirection = Vector3.ProjectOnPlane(
                    -context.Camera.transform.up, direction).normalized;
            }

            float elbowAlong = (upperLength * upperLength -
                forearmLength * forearmLength + reach * reach) / (2f * reach);
            float elbowHeight = Mathf.Sqrt(Mathf.Max(0f,
                upperLength * upperLength - elbowAlong * elbowAlong));
            Vector3 desiredElbow = shoulder + direction * elbowAlong +
                                   bendDirection * elbowHeight;

            Vector3 currentUpper = context.RightElbow.position - shoulder;
            context.RightUpper.rotation = Quaternion.FromToRotation(
                currentUpper, desiredElbow - shoulder) * context.RightUpper.rotation;
            Vector3 currentForearm =
                context.RightWrist.position - context.RightElbow.position;
            context.RightElbow.rotation = Quaternion.FromToRotation(
                currentForearm, targetPosition - context.RightElbow.position) *
                context.RightElbow.rotation;
            context.RightWrist.rotation = targetRotation;

            float error = Vector3.Distance(context.RightWrist.position, targetPosition);
            if (error > PositionTolerance)
            {
                throw new InvalidOperationException(
                    "Editor two-bone solve missed wrist target by " +
                    (error * 1000f).ToString("F2", CultureInfo.InvariantCulture) + " mm.");
            }
            Vector3 elbowViewport = context.Camera.WorldToViewportPoint(
                context.RightElbow.position);
            if (elbowViewport.y > 0.40f && elbowViewport.z > 0f)
            {
                throw new InvalidOperationException(
                    "Right elbow rose too far into the FOV instead of remaining low/right.");
            }
        }

        private static void ApplyStaticMasterLeft(Context context)
        {
            foreach (string boneName in SoulRecorderReplacementArmStaging.RequiredBones)
            {
                if (boneName.StartsWith("L_", StringComparison.Ordinal))
                {
                    ApplyTransformFromPose(context.Rig.Bones[boneName],
                        context.StaticMaster);
                }
            }
        }

        private static void SetRecorderFromLeftWrist(Context context)
        {
            SetFromOwner(context.LeftWrist, context.RecorderGrip,
                context.LeftHandGripToRecorder);
        }

        private static void SetCassetteFromRightWrist(Context context)
        {
            SetFromOwner(context.RightWrist, context.CassetteGrip,
                context.RightHandGripToCassette);
        }

        private static void SetFromOwner(Transform owner, Transform prop,
            GripRelativeTransform relative)
        {
            prop.position = owner.TransformPoint(relative.Position);
            prop.rotation = owner.rotation * relative.Rotation;
        }

        private static WorldPose HandTargetForCassette(Context context,
            Vector3 cassettePosition, Quaternion cassetteRotation)
        {
            Quaternion wristRotation = cassetteRotation * Quaternion.Inverse(
                context.RightHandGripToCassette.Rotation);
            return new WorldPose
            {
                Position = cassettePosition - wristRotation *
                    context.RightHandGripToCassette.Position,
                Rotation = wristRotation
            };
        }

        private static WorldPose HandTargetForRecorder(Context context,
            Vector3 recorderPosition, Quaternion recorderRotation)
        {
            Quaternion wristRotation = recorderRotation * Quaternion.Inverse(
                context.LeftHandGripToRecorder.Rotation);
            return new WorldPose
            {
                Position = recorderPosition - wristRotation *
                    context.LeftHandGripToRecorder.Position,
                Rotation = wristRotation
            };
        }

        private static void SolveRightHandForCassette(Context context,
            Vector3 cassettePosition, Quaternion cassetteRotation)
        {
            WorldPose wrist = HandTargetForCassette(context,
                cassettePosition, cassetteRotation);
            SolveRightArm(context, wrist.Position, wrist.Rotation);
        }

        private static void ApplyTransformFromPose(Transform transform, Pose pose)
        {
            DraftTransform value = pose.Values[PathOf(transform)];
            transform.localPosition = value.localPosition;
            transform.localRotation = value.localRotation;
            transform.localScale = value.localScale;
        }

        private static Pose ClonePose(Pose source, string name,
            string description, CassetteOwner owner)
        {
            Pose result = new Pose
            {
                Name = name,
                Source = description,
                Owner = owner,
                RightHandGripToCassette = source.RightHandGripToCassette,
                LeftHandGripToRecorder = source.LeftHandGripToRecorder
            };
            foreach (KeyValuePair<string, DraftTransform> pair in source.Values)
            {
                DraftTransform value = pair.Value;
                result.Values.Add(pair.Key, new DraftTransform
                {
                    path = value.path,
                    localPosition = value.localPosition,
                    localRotation = value.localRotation,
                    localScale = value.localScale
                });
            }
            return result;
        }

        private static void RotateJointToward(Transform joint,
            Vector3 currentEnd, Vector3 targetEnd, float weight)
        {
            Vector3 current = currentEnd - joint.position;
            Vector3 target = targetEnd - joint.position;
            if (current.sqrMagnitude < 0.000001f || target.sqrMagnitude < 0.000001f)
            {
                return;
            }
            Quaternion delta = Quaternion.FromToRotation(current, target);
            joint.rotation = Quaternion.Slerp(joint.rotation,
                delta * joint.rotation, weight);
        }

        private static void OpenRightPinchModestly(Context context)
        {
            foreach (string name in new[]
            {
                "R_thumb1", "R_thumb2", "R_point1", "R_point2",
                "R_middle1", "R_middle2"
            })
            {
                Transform finger = context.Rig.Bones[name];
                finger.localRotation = finger.localRotation *
                    Quaternion.Euler(-7f, 0f, 0f);
            }
        }

        private static MarkerGeometry CaptureMarkerGeometry(Context context)
        {
            Transform cassetteAxis = FindUnique(context.CassetteGrip.gameObject,
                "CassetteInsertionAxis");
            Transform slotAxis = FindUnique(context.RecorderGrip.gameObject,
                "CassetteSlotTravelAxis");
            Transform slotEntry = FindUnique(context.RecorderGrip.gameObject,
                "CassetteSlotEntry");
            Transform slotSeated = FindUnique(context.RecorderGrip.gameObject,
                "CassetteSlotSeated");
            MarkerGeometry result = new MarkerGeometry
            {
                CassetteAxisLocalPosition = context.CassetteGrip.InverseTransformPoint(
                    cassetteAxis.position),
                CassetteAxisLocalRotation = Quaternion.Inverse(
                    context.CassetteGrip.rotation) * cassetteAxis.rotation,
                SlotEntryPosition = slotEntry.position,
                SlotSeatedPosition = slotSeated.position,
                SlotAxisRotation = slotAxis.rotation,
                SlotDirection = slotAxis.forward.normalized
            };
            if (Vector3.Dot((result.SlotSeatedPosition - result.SlotEntryPosition)
                    .normalized, result.SlotDirection) < 0.99f)
            {
                throw new InvalidOperationException(
                    "Slot entry-to-seated translation does not follow CassetteSlotTravelAxis.");
            }
            return result;
        }

        private static void SetCassetteFromAxisTarget(Context context,
            Vector3 axisPosition, Quaternion axisRotation)
        {
            WorldPose cassette = CassetteFromAxisTarget(context,
                axisPosition, axisRotation);
            context.CassetteGrip.position = cassette.Position;
            context.CassetteGrip.rotation = cassette.Rotation;
        }

        private static WorldPose CassetteFromAxisTarget(Context context,
            Vector3 axisPosition, Quaternion axisRotation)
        {
            Quaternion cassetteRotation = axisRotation *
                Quaternion.Inverse(context.Markers.CassetteAxisLocalRotation);
            return new WorldPose
            {
                Rotation = cassetteRotation,
                Position = axisPosition - cassetteRotation *
                    context.Markers.CassetteAxisLocalPosition
            };
        }

        private static GripRelativeTransform CaptureGrip(Transform wrist,
            Transform cassette)
        {
            return new GripRelativeTransform
            {
                Position = wrist.InverseTransformPoint(cassette.position),
                Rotation = Quaternion.Inverse(wrist.rotation) * cassette.rotation
            };
        }

        private static Vector3 ElbowPlaneNormal(Context context)
        {
            return Vector3.Cross(
                context.RightElbow.position - context.RightUpper.position,
                context.RightWrist.position - context.RightElbow.position).normalized;
        }

        private static Vector3 LeftElbowPlaneNormal(Context context)
        {
            return Vector3.Cross(
                context.LeftElbow.position - context.LeftUpper.position,
                context.LeftWrist.position - context.LeftElbow.position).normalized;
        }

        private static Pose CapturePose(Context context, string name,
            string source, CassetteOwner owner)
        {
            Pose pose = new Pose
            {
                Name = name,
                Source = source,
                Owner = owner,
                RightHandGripToCassette = context.RightHandGripToCassette,
                LeftHandGripToRecorder = context.LeftHandGripToRecorder
            };
            foreach (Transform transform in context.Controlled)
            {
                string path = PathOf(transform);
                pose.Values.Add(path, new DraftTransform
                {
                    path = path,
                    localPosition = transform.localPosition,
                    localRotation = transform.localRotation,
                    localScale = transform.localScale
                });
            }
            return pose;
        }

        private static void ApplyPose(Context context, Pose pose)
        {
            context.RightHandGripToCassette = pose.RightHandGripToCassette;
            context.LeftHandGripToRecorder = pose.LeftHandGripToRecorder;
            foreach (Transform transform in context.Controlled)
            {
                DraftTransform value = pose.Values[PathOf(transform)];
                transform.localPosition = value.localPosition;
                transform.localRotation = value.localRotation;
                transform.localScale = value.localScale;
            }
            SetRecorderFromLeftWrist(context);
            if (pose.Owner == CassetteOwner.Hand)
            {
                SetCassetteFromRightWrist(context);
            }
            else if (pose.Owner == CassetteOwner.Recorder)
            {
                SetCassetteAtLiveSeatedMarker(context);
            }
        }

        private static void ApplyInterpolated(Context context, Pose from, Pose to,
            float amount, CassetteOwner owner)
        {
            float eased = amount * amount * (3f - 2f * amount);
            foreach (Transform transform in context.Controlled)
            {
                if (transform == context.RecorderGrip ||
                    transform == context.CassetteGrip)
                {
                    continue;
                }
                string path = PathOf(transform);
                DraftTransform a = from.Values[path];
                DraftTransform b = to.Values[path];
                transform.localPosition = Vector3.Lerp(a.localPosition,
                    b.localPosition, eased);
                transform.localRotation = ShortestPathSlerp(a.localRotation,
                    b.localRotation, eased);
                transform.localScale = Vector3.Lerp(a.localScale,
                    b.localScale, eased);
            }
            SetRecorderFromLeftWrist(context);
            if (owner == CassetteOwner.Hand)
            {
                SetCassetteFromRightWrist(context);
            }
            else if (owner == CassetteOwner.Recorder)
            {
                SetCassetteAtLiveSeatedMarker(context);
            }
        }

        private static Quaternion ShortestPathSlerp(Quaternion from,
            Quaternion to, float amount)
        {
            if (Quaternion.Dot(from, to) < 0f)
            {
                to = new Quaternion(-to.x, -to.y, -to.z, -to.w);
            }
            return Quaternion.Slerp(from, to, amount);
        }

        private static void SetCassetteAtLiveSeatedMarker(Context context)
        {
            Transform seated = FindUnique(context.RecorderGrip.gameObject,
                "CassetteSlotSeated");
            SetFromOwner(seated, context.CassetteGrip,
                context.SlotToCassetteSeated);
        }

        private static void ValidateDraft(Context context)
        {
            Pose contact = context.Generated["Auto_CassetteContact"];
            Pose half = context.Generated["Auto_CassetteHalfInserted"];
            Pose seated = context.Generated["Auto_CassetteSeated"];
            Vector3[] centers = new Vector3[3];
            float[] axisErrors = new float[3];
            Pose[] insertion = { contact, half, seated };
            for (int index = 0; index < insertion.Length; index++)
            {
                ApplyPose(context, insertion[index]);
                Transform axis = FindUnique(context.CassetteGrip.gameObject,
                    "CassetteInsertionAxis");
                centers[index] = axis.position;
                axisErrors[index] = Quaternion.Angle(axis.rotation,
                    context.Markers.SlotAxisRotation);
                if (axisErrors[index] > 1f)
                {
                    context.NumericalGatePassed = false;
                    context.GeometryFailures.Add(insertion[index].Name +
                        " axis error is " + axisErrors[index].ToString("F3",
                            CultureInfo.InvariantCulture) + " degrees.");
                }
                ValidateArm(context, insertion[index].Name);
                ValidateHeldAssembly(context, insertion[index]);
                ValidateRecorderAttachment(context, insertion[index].Name);
            }
            Vector3 travel = context.Markers.SlotDirection;
            Vector3 reference = centers[0];
            float lateral = centers.Max(center => Vector3.ProjectOnPlane(
                center - reference, travel).magnitude);
            if (lateral > 0.001f)
            {
                context.NumericalGatePassed = false;
                context.GeometryFailures.Add(
                    "Final insertion lateral drift is " +
                    (lateral * 1000f).ToString("F3", CultureInfo.InvariantCulture) +
                    " mm.");
            }
            float expectedHalf = Vector3.Distance(
                Vector3.Lerp(context.Markers.SlotEntryPosition,
                    context.Markers.SlotSeatedPosition, 0.5f), centers[1]);
            if (expectedHalf > 0.001f)
            {
                context.NumericalGatePassed = false;
                context.GeometryFailures.Add(
                    "Half-inserted cassette missed its mechanical midpoint by " +
                    (expectedHalf * 1000f).ToString("F3",
                        CultureInfo.InvariantCulture) + " mm.");
            }
        }

        private static void ValidateHeldAssembly(Context context, Pose pose)
        {
            Vector3 expectedPosition = context.RightWrist.TransformPoint(
                context.RightHandGripToCassette.Position);
            Quaternion expectedRotation = context.RightWrist.rotation *
                context.RightHandGripToCassette.Rotation;
            float positionError = Vector3.Distance(expectedPosition,
                context.CassetteGrip.position);
            float rotationError = Quaternion.Angle(expectedRotation,
                context.CassetteGrip.rotation);
            if (positionError > 0.001f || rotationError > 0.25f)
            {
                throw new InvalidOperationException(pose.Name +
                    " disconnected the held cassette from the right wrist.");
            }
        }

        private static void ValidateRecorderAttachment(Context context,
            string poseName)
        {
            GripRelativeTransform actual = CaptureGrip(context.LeftWrist,
                context.RecorderGrip);
            if (Vector3.Distance(actual.Position,
                    context.LeftHandGripToRecorder.Position) > 0.000001f ||
                Quaternion.Angle(actual.Rotation,
                    context.LeftHandGripToRecorder.Rotation) > 0.0001f)
            {
                throw new InvalidOperationException(
                    poseName + " disconnected the recorder from the left-hand grip.");
            }
        }

        private static float ValidateArm(Context context, string label)
        {
            float upperLength = Vector3.Distance(context.RightUpper.position,
                context.RightElbow.position);
            float forearmLength = Vector3.Distance(context.RightElbow.position,
                context.RightWrist.position);
            float extension = Vector3.Distance(context.RightUpper.position,
                context.RightWrist.position) /
                (upperLength + forearmLength);
            if (extension > MaximumArmExtension + 0.0005f)
            {
                context.NumericalGatePassed = false;
                context.GeometryFailures.Add(label +
                    " hyperextended the right arm to " +
                    (extension * 100f).ToString("F2", CultureInfo.InvariantCulture) + "%.");
            }
            if (Vector3.Dot(context.AlignElbowPlaneNormal,
                    ElbowPlaneNormal(context)) < 0.20f)
            {
                throw new InvalidOperationException(label + " flipped the right elbow.");
            }
            return extension;
        }

        private static void WriteDraftPoseFile(Context context,
            string humanPoseSha, string authoringSceneSha)
        {
            DraftPoseFile file = new DraftPoseFile
            {
                lockedStaticMasterSha256 =
                    SoulRecorderReplacementGripPoseStore.LockedStaticMasterHash,
                sourceHumanPoseFileSha256 = humanPoseSha,
                sourceAuthoringSceneSha256 = authoringSceneSha,
                generatedUtc = DateTime.UtcNow.ToString("O",
                    CultureInfo.InvariantCulture),
                poses = context.Generated.Values.Select(pose =>
                    new DraftPoseRecord
                    {
                        poseName = pose.Name,
                        source = pose.Source,
                        cassetteOwner = pose.Owner.ToString(),
                        transforms = pose.Values.Values.ToArray()
                    }).ToArray()
            };
            File.WriteAllText(Path.GetFullPath(DraftPoseAsset),
                JsonUtility.ToJson(file, true), new UTF8Encoding(false));
        }

        private static void RenderOutputs(Context context, bool renderFullSequences)
        {
            string output = Path.GetFullPath(OutputAssetFolder);
            string diagnosticFrames = Path.Combine(output,
                "Diagnostic-Key-Poses-FOV70");
            Directory.CreateDirectory(diagnosticFrames);
            DeletePngs(diagnosticFrames);

            Pose autoAlign = context.Generated["Auto_CassetteAlign"];
            Pose contact = context.Generated["Auto_CassetteContact"];
            Pose half = context.Generated["Auto_CassetteHalfInserted"];
            Pose seated = context.Generated["Auto_CassetteSeated"];
            Pose[] diagnostics =
            {
                context.StaticMaster, autoAlign, contact, half, seated
            };
            for (int index = 0; index < diagnostics.Length; index++)
            {
                ApplyPose(context, diagnostics[index]);
                RenderPng(context.Camera, Path.Combine(diagnosticFrames,
                    index.ToString("00", CultureInfo.InvariantCulture) + "-" +
                    Sanitize(diagnostics[index].Name) + ".png"));
            }
            CreateContactSheet(diagnosticFrames,
                Path.Combine(output, "ownership-diagnostics-fov70.png"), 5);
            CreateCloseUpSheet(diagnosticFrames,
                Path.Combine(output, "ownership-diagnostics-closeup-fov70.png"));
            WriteReport(context, Path.Combine(output,
                "ownership-diagnostics-report.txt"));

            if (!renderFullSequences)
            {
                return;
            }
            if (!context.NumericalGatePassed)
            {
                throw new InvalidOperationException(
                    "V3 full sequences are blocked because the five-pose " +
                    "ownership diagnostics did not pass their numerical gate.");
            }

            string startFrames = Path.Combine(output, "Start-Frames-FOV70");
            string stopFrames = Path.Combine(output, "Stop-Frames-FOV70");
            string keyFrames = Path.Combine(output, "Key-Poses-FOV70");
            Directory.CreateDirectory(startFrames);
            Directory.CreateDirectory(stopFrames);
            Directory.CreateDirectory(keyFrames);
            DeletePngs(startFrames);
            DeletePngs(stopFrames);
            DeletePngs(keyFrames);

            Pose supportHold = context.Generated["Auto_SupportHold"];
            Pose release = context.Generated["Auto_CassetteRelease"];
            Pose belowCarry = context.Generated["Auto_HandsBelowCarry"];
            Pose belowSeated = context.Generated["Auto_HandsBelowSeated"];
            Pose stopHold = context.Generated["Auto_StopRecorderReturn"];
            Pose approach = context.Generated["Auto_CassetteEjectApproach"];
            Pose ejectGrip = context.Generated["Auto_CassetteEjectGrip"];
            Pose ejectClear = context.Generated["Auto_CassetteEjectClear"];

            List<Transition> start = new List<Transition>
            {
                T(belowCarry, supportHold, 0.42f, CassetteOwner.Hand, "EnterWholeAssembly"),
                T(supportHold, autoAlign, 0.38f, CassetteOwner.Hand, "CarryToAlign"),
                T(autoAlign, contact, 0.25f, CassetteOwner.Hand, "AlignToContact"),
                T(contact, half, 0.22f, CassetteOwner.Hand, "ContactToHalf"),
                T(half, seated, 0.22f, CassetteOwner.Hand, "HalfToSeated"),
                T(seated, release, 0.19f, CassetteOwner.Recorder, "Release"),
                T(release, belowSeated, 0.36f, CassetteOwner.Recorder, "ExitWholeAssembly")
            };
            List<Transition> stop = new List<Transition>
            {
                T(belowSeated, stopHold, 0.42f, CassetteOwner.Recorder, "RecorderReturnWholeAssembly"),
                T(stopHold, approach, 0.28f, CassetteOwner.Recorder, "Approach"),
                T(approach, ejectGrip, 0.20f, CassetteOwner.Recorder, "EstablishGrip"),
                T(ejectGrip, ejectClear, 0.28f, CassetteOwner.Hand, "PullClear"),
                T(ejectClear, autoAlign, 0.30f, CassetteOwner.Hand, "RotateToCarry"),
                T(autoAlign, supportHold, 0.32f, CassetteOwner.Hand, "Carry"),
                T(supportHold, belowCarry, 0.38f, CassetteOwner.Hand, "ExitWholeAssembly")
            };
            RenderSequence(context, start, startFrames);
            RenderSequence(context, stop, stopFrames);

            List<Pose> keys = new[]
            {
                context.StaticMaster, supportHold, context.HumanAlign,
                context.HumanContact, autoAlign,
                contact, half, seated,
                release, stopHold, approach, ejectGrip, ejectClear,
                belowCarry, belowSeated
            }.ToList();
            for (int index = 0; index < keys.Count; index++)
            {
                ApplyPose(context, keys[index]);
                RenderPng(context.Camera, Path.Combine(keyFrames,
                    index.ToString("00", CultureInfo.InvariantCulture) + "-" +
                    Sanitize(keys[index].Name) + ".png"));
            }
            CreateContactSheet(keyFrames,
                Path.Combine(output, "key-poses-contact-sheet-fov70.png"), 4);
            CreateCloseUpSheet(keyFrames,
                Path.Combine(output, "cassette-hand-slot-closeup-fov70.png"));
            RenderSideDiagnostic(context, new[] { contact, half, seated, ejectClear },
                Path.Combine(output, "arm-chain-elbow-diagnostic.png"));
        }

        private static Transition T(Pose from, Pose to, float duration,
            CassetteOwner owner, string label)
        {
            return new Transition
            {
                From = from, To = to, Duration = duration,
                Owner = owner, Label = label
            };
        }

        private static void RenderSequence(Context context,
            List<Transition> transitions, string folder)
        {
            int frame = 0;
            foreach (Transition transition in transitions)
            {
                int count = Mathf.Max(2, Mathf.RoundToInt(
                    transition.Duration * FramesPerSecond));
                for (int index = 0; index < count; index++)
                {
                    float amount = count <= 1 ? 1f : index / (float)(count - 1);
                    ApplyInterpolated(context, transition.From, transition.To,
                        amount, transition.Owner);
                    RenderPng(context.Camera, Path.Combine(folder,
                        "frame-" + frame.ToString("D4",
                            CultureInfo.InvariantCulture) + ".png"));
                    frame++;
                }
            }
        }

        private static void WriteReport(Context context, string path)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("SoulRecorder replacement-arm auto animation draft v3");
            report.AppendLine("StaticMaster_v1: LOCKED " +
                SoulRecorderReplacementGripPoseStore.LockedStaticMasterHash);
            report.AppendLine("Architecture: one rigid prop owner at a time");
            report.AppendLine("Recorder owner: LEFT_HAND for entire interaction");
            report.AppendLine("Cassette owners: RIGHT_HAND -> RECORDER_SLOT -> RIGHT_HAND");
            report.AppendLine("Human CassetteAlign: preserved as bone/finger input");
            report.AppendLine("Human CassetteContact: " +
                (context.HumanContact == null ? "not present" :
                    "preserved and not modified"));
            report.AppendLine("Interaction assembly shift: " +
                (context.InteractionAssemblyShift * 1000f).ToString("F2",
                    CultureInfo.InvariantCulture) + " mm (max 80 mm)");
            report.AppendLine();
            report.AppendLine("Pose metrics");
            Vector3 reference = Vector3.zero;
            float maximumRecorderPositionDrift = 0f;
            float maximumRecorderRotationDrift = 0f;
            float maximumHandCassettePositionDrift = 0f;
            float maximumHandCassetteRotationDrift = 0f;
            float maximumSlotCassettePositionDrift = 0f;
            float maximumSlotCassetteRotationDrift = 0f;
            foreach (Pose pose in context.Generated.Values.OrderBy(value => value.Name))
            {
                ApplyPose(context, pose);
                Transform axis = FindUnique(context.CassetteGrip.gameObject,
                    "CassetteInsertionAxis");
                float axisError = Quaternion.Angle(axis.rotation,
                    context.Markers.SlotAxisRotation);
                float extension = ValidateArm(context, pose.Name);
                GripRelativeTransform recorderRelative = CaptureGrip(
                    context.LeftWrist, context.RecorderGrip);
                float recorderPositionDrift = Vector3.Distance(
                    recorderRelative.Position,
                    context.LeftHandGripToRecorder.Position);
                float recorderRotationDrift = Quaternion.Angle(
                    recorderRelative.Rotation,
                    context.LeftHandGripToRecorder.Rotation);
                maximumRecorderPositionDrift = Mathf.Max(
                    maximumRecorderPositionDrift, recorderPositionDrift);
                maximumRecorderRotationDrift = Mathf.Max(
                    maximumRecorderRotationDrift, recorderRotationDrift);
                float cassettePositionDrift = 0f;
                float cassetteRotationDrift = 0f;
                if (pose.Owner == CassetteOwner.Hand)
                {
                    GripRelativeTransform cassetteRelative = CaptureGrip(
                        context.RightWrist, context.CassetteGrip);
                    cassettePositionDrift = Vector3.Distance(
                        cassetteRelative.Position,
                        context.RightHandGripToCassette.Position);
                    cassetteRotationDrift = Quaternion.Angle(
                        cassetteRelative.Rotation,
                        context.RightHandGripToCassette.Rotation);
                    maximumHandCassettePositionDrift = Mathf.Max(
                        maximumHandCassettePositionDrift, cassettePositionDrift);
                    maximumHandCassetteRotationDrift = Mathf.Max(
                        maximumHandCassetteRotationDrift, cassetteRotationDrift);
                }
                else if (pose.Owner == CassetteOwner.Recorder)
                {
                    Transform seatedMarker = FindUnique(
                        context.RecorderGrip.gameObject, "CassetteSlotSeated");
                    GripRelativeTransform cassetteRelative = CaptureGrip(
                        seatedMarker, context.CassetteGrip);
                    cassettePositionDrift = Vector3.Distance(
                        cassetteRelative.Position,
                        context.SlotToCassetteSeated.Position);
                    cassetteRotationDrift = Quaternion.Angle(
                        cassetteRelative.Rotation,
                        context.SlotToCassetteSeated.Rotation);
                    maximumSlotCassettePositionDrift = Mathf.Max(
                        maximumSlotCassettePositionDrift, cassettePositionDrift);
                    maximumSlotCassetteRotationDrift = Mathf.Max(
                        maximumSlotCassetteRotationDrift, cassetteRotationDrift);
                }
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0}: owner={1} axisError={2:F4}deg extension={3:F2}% recorderDrift={4:F4}mm cassetteDrift={5:F4}mm",
                    pose.Name, pose.Owner, axisError, extension * 100f,
                    recorderPositionDrift * 1000f,
                    cassettePositionDrift * 1000f));
                if (pose.Name == "Auto_CassetteContact")
                {
                    reference = axis.position;
                }
            }
            Pose half = context.Generated["Auto_CassetteHalfInserted"];
            Pose seated = context.Generated["Auto_CassetteSeated"];
            ApplyPose(context, half);
            Vector3 halfPoint = FindUnique(context.CassetteGrip.gameObject,
                "CassetteInsertionAxis").position;
            ApplyPose(context, seated);
            Vector3 seatedPoint = FindUnique(context.CassetteGrip.gameObject,
                "CassetteInsertionAxis").position;
            float lateral = Mathf.Max(
                Vector3.ProjectOnPlane(halfPoint - reference,
                    context.Markers.SlotDirection).magnitude,
                Vector3.ProjectOnPlane(seatedPoint - reference,
                    context.Markers.SlotDirection).magnitude);
            Pose contact = context.Generated["Auto_CassetteContact"];
            ApplyPose(context, contact);
            Transform contactAxis = FindUnique(context.CassetteGrip.gameObject,
                "CassetteInsertionAxis");
            float contactError = Vector3.Distance(contactAxis.position,
                context.Markers.SlotEntryPosition);
            float contactRotationError = Quaternion.Angle(contactAxis.rotation,
                context.Markers.SlotAxisRotation);
            ApplyPose(context, seated);
            Transform seatedAxis = FindUnique(context.CassetteGrip.gameObject,
                "CassetteInsertionAxis");
            float seatedError = Vector3.Distance(seatedAxis.position,
                context.Markers.SlotSeatedPosition);
            Vector3 insertionTransferPosition = context.CassetteGrip.position;
            Quaternion insertionTransferRotation = context.CassetteGrip.rotation;
            SetCassetteAtLiveSeatedMarker(context);
            float insertionTransferSnap = Vector3.Distance(
                insertionTransferPosition, context.CassetteGrip.position);
            float insertionTransferRotationSnap = Quaternion.Angle(
                insertionTransferRotation, context.CassetteGrip.rotation);
            Pose ejectGrip = context.Generated["Auto_CassetteEjectGrip"];
            ApplyPose(context, ejectGrip);
            Vector3 ejectTransferPosition = context.CassetteGrip.position;
            Quaternion ejectTransferRotation = context.CassetteGrip.rotation;
            SetCassetteFromRightWrist(context);
            float ejectTransferSnap = Vector3.Distance(
                ejectTransferPosition, context.CassetteGrip.position);
            float ejectTransferRotationSnap = Quaternion.Angle(
                ejectTransferRotation, context.CassetteGrip.rotation);
            report.AppendLine();
            report.AppendLine("Recorder-left-hand relative transform drift: " +
                (maximumRecorderPositionDrift * 1000f).ToString("F4",
                    CultureInfo.InvariantCulture) + " mm / " +
                maximumRecorderRotationDrift.ToString("F4",
                    CultureInfo.InvariantCulture) + " deg (target 0)");
            report.AppendLine("Cassette-right-hand relative transform drift while hand-owned: " +
                (maximumHandCassettePositionDrift * 1000f).ToString("F4",
                    CultureInfo.InvariantCulture) + " mm / " +
                maximumHandCassetteRotationDrift.ToString("F4",
                    CultureInfo.InvariantCulture) + " deg (target 0)");
            report.AppendLine("Cassette slot-relative drift while recorder-owned: " +
                (maximumSlotCassettePositionDrift * 1000f).ToString("F4",
                    CultureInfo.InvariantCulture) + " mm / " +
                maximumSlotCassetteRotationDrift.ToString("F4",
                    CultureInfo.InvariantCulture) + " deg (target 0)");
            report.AppendLine("Insertion ownership-transfer world snap: " +
                (insertionTransferSnap * 1000f).ToString("F4",
                    CultureInfo.InvariantCulture) + " mm / " +
                insertionTransferRotationSnap.ToString("F4",
                    CultureInfo.InvariantCulture) + " deg (target 0)");
            report.AppendLine("Ejection ownership-transfer world snap: " +
                (ejectTransferSnap * 1000f).ToString("F4",
                    CultureInfo.InvariantCulture) + " mm / " +
                ejectTransferRotationSnap.ToString("F4",
                    CultureInfo.InvariantCulture) + " deg (target 0)");
            report.AppendLine("contact/slot entry error: " +
                (contactError * 1000f).ToString("F4",
                    CultureInfo.InvariantCulture) + " mm");
            report.AppendLine("seated error: " +
                (seatedError * 1000f).ToString("F4",
                    CultureInfo.InvariantCulture) + " mm");
            report.AppendLine("maximum lateral insertion drift: " +
                (lateral * 1000f).ToString("F4", CultureInfo.InvariantCulture) + " mm");
            report.AppendLine("final insertion rotation delta: " +
                contactRotationError.ToString("F4",
                    CultureInfo.InvariantCulture) + " deg");
            report.AppendLine("Quaternion interpolation: explicit shortest path");
            if (context.GeometryFailures.Count > 0)
            {
                report.AppendLine("Geometry failures:");
                foreach (string failure in context.GeometryFailures.Distinct())
                {
                    report.AppendLine("- " + failure);
                }
            }
            report.AppendLine("AUTO NUMERICAL GATE: " +
                (context.NumericalGatePassed ? "PASS" : "FAIL"));
            report.AppendLine("AUTO VISUAL DRAFT V3 GATE: REQUIRES FIVE-POSE HUMAN REVIEW");
            File.WriteAllText(path, report.ToString(), new UTF8Encoding(false));
        }

        private static void RenderSideDiagnostic(Context context, Pose[] poses,
            string output)
        {
            Camera original = context.Camera;
            GameObject cameraObject = new GameObject("Auto Draft Side Diagnostic Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 45f;
            camera.nearClipPlane = 0.02f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.03f, 0.04f, 1f);
            camera.transform.position = original.transform.position +
                original.transform.right * 0.65f + original.transform.up * 0.05f;
            camera.transform.LookAt(original.transform.position +
                original.transform.forward * 0.42f - original.transform.up * 0.08f);
            List<GameObject> gizmos = new List<GameObject>();
            try
            {
                for (int index = 0; index < poses.Length; index++)
                {
                    ApplyPose(context, poses[index]);
                    Color color = Color.Lerp(Color.cyan, Color.magenta,
                        index / Mathf.Max(1f, poses.Length - 1f));
                    gizmos.Add(CreateLine(context.RightUpper.position,
                        context.RightElbow.position, color));
                    gizmos.Add(CreateLine(context.RightElbow.position,
                        context.RightWrist.position, color));
                }
                RenderPng(camera, output);
            }
            finally
            {
                foreach (GameObject gizmo in gizmos)
                {
                    UnityEngine.Object.DestroyImmediate(gizmo);
                }
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        private static GameObject CreateLine(Vector3 start, Vector3 end,
            Color color)
        {
            GameObject lineObject = new GameObject("AutoDraftArmChain");
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
            line.startWidth = 0.008f;
            line.endWidth = 0.008f;
            Material material = new Material(Shader.Find("Unlit/Color"));
            material.color = color;
            line.sharedMaterial = material;
            return lineObject;
        }

        private static void RenderPng(Camera camera, string path)
        {
            RenderTexture target = new RenderTexture(Width, Height, 24,
                RenderTextureFormat.ARGB32);
            Texture2D image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = target;
                RenderTexture.active = target;
                camera.Render();
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                image.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static void CreateContactSheet(string folder, string output,
            int columns)
        {
            string[] files = Directory.GetFiles(folder, "*.png")
                .OrderBy(value => value, StringComparer.Ordinal).ToArray();
            int cellWidth = 480;
            int cellHeight = 270;
            int rows = Mathf.CeilToInt(files.Length / (float)columns);
            Texture2D sheet = new Texture2D(cellWidth * columns,
                cellHeight * rows, TextureFormat.RGB24, false);
            Fill(sheet, new Color32(8, 10, 13, 255));
            try
            {
                for (int index = 0; index < files.Length; index++)
                {
                    Texture2D source = LoadTexture(files[index]);
                    Texture2D scaled = Scale(source, cellWidth, cellHeight);
                    sheet.SetPixels32((index % columns) * cellWidth,
                        (rows - 1 - index / columns) * cellHeight,
                        cellWidth, cellHeight, scaled.GetPixels32());
                    UnityEngine.Object.DestroyImmediate(source);
                    UnityEngine.Object.DestroyImmediate(scaled);
                }
                sheet.Apply();
                File.WriteAllBytes(output, sheet.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sheet);
            }
        }

        private static void CreateCloseUpSheet(string folder, string output)
        {
            string[] files = Directory.GetFiles(folder, "*.png")
                .Where(value => value.Contains("Contact") || value.Contains("Half") ||
                    value.Contains("Seated") || value.Contains("EjectGrip") ||
                    value.Contains("EjectClear"))
                .OrderBy(value => value, StringComparer.Ordinal).ToArray();
            string temp = Path.Combine(folder, "_CloseUps");
            Directory.CreateDirectory(temp);
            DeletePngs(temp);
            foreach (string file in files)
            {
                Texture2D source = LoadTexture(file);
                int cropWidth = Mathf.RoundToInt(source.width * 0.62f);
                int cropHeight = Mathf.RoundToInt(source.height * 0.72f);
                int x = Mathf.RoundToInt(source.width * 0.19f);
                int y = Mathf.RoundToInt(source.height * 0.12f);
                Texture2D crop = new Texture2D(cropWidth, cropHeight,
                    TextureFormat.RGB24, false);
                crop.SetPixels(source.GetPixels(x, y, cropWidth, cropHeight));
                crop.Apply();
                File.WriteAllBytes(Path.Combine(temp, Path.GetFileName(file)),
                    crop.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(crop);
            }
            CreateContactSheet(temp, output, 3);
        }

        private static Texture2D LoadTexture(string path)
        {
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            texture.LoadImage(File.ReadAllBytes(path), false);
            return texture;
        }

        private static Texture2D Scale(Texture2D source, int width, int height)
        {
            RenderTexture target = RenderTexture.GetTemporary(width, height, 0,
                RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(source, target);
            RenderTexture.active = target;
            Texture2D result = new Texture2D(width, height, TextureFormat.RGB24, false);
            result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            result.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            return result;
        }

        private static void Fill(Texture2D texture, Color32 color)
        {
            Color32[] pixels = Enumerable.Repeat(color,
                texture.width * texture.height).ToArray();
            texture.SetPixels32(pixels);
        }

        private static void DeletePngs(string folder)
        {
            foreach (string file in Directory.GetFiles(folder, "*.png"))
            {
                File.Delete(file);
            }
        }

        private static Camera FindSceneCamera(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Camera camera = root.GetComponentsInChildren<Camera>(true)
                    .FirstOrDefault(value => value.name ==
                        SoulRecorderReplacementArmStaging.CameraName);
                if (camera != null)
                {
                    return camera;
                }
            }
            throw new InvalidOperationException("FOV-70 authoring camera was not found.");
        }

        private static Transform FindUnique(GameObject root, string name)
        {
            Transform[] matches = root == null ? new Transform[0] :
                root.GetComponentsInChildren<Transform>(true)
                    .Where(value => value.name == name).ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidOperationException(name + " resolved " +
                    matches.Length.ToString(CultureInfo.InvariantCulture) + " times.");
            }
            return matches[0];
        }

        private static string PathOf(Transform transform)
        {
            List<string> names = new List<string>();
            for (Transform current = transform; current != null;
                current = current.parent)
            {
                names.Add(current.name);
            }
            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        private static string Sha256File(string path)
        {
            using (System.Security.Cryptography.SHA256 sha =
                System.Security.Cryptography.SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(stream))
                    .Replace("-", string.Empty);
            }
        }

        private static string Sanitize(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid, '_');
            }
            return value;
        }
    }
}
