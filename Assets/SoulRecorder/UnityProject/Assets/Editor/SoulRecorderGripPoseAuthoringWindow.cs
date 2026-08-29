using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SoulPlayer.Editor
{
    /// <summary>
    /// Human-authored static grip tool. Controls are populated from the loaded
    /// SoulRecorderAnimatedHandsModel hierarchy. No IK, marker solver, animation,
    /// or generic curl runs after manual authoring begins.
    /// </summary>
    internal sealed class SoulRecorderGripPoseAuthoringWindow : EditorWindow
    {
        private const string PoseFilePath =
            "Assets/SoulRecorderAuthoring/SoulRecorderGripPoses.json";
        private const string ReferenceCameraName = "SoulRecorder Preview Camera";
        private const string AnimatedHandsModelName = "SoulRecorderAnimatedHandsModel";
        internal const string ManualAuthoringSceneName = "SoulRecorderManualGrip";
        private const string ArmRendererName = "SoulRecorderArms";
        private const float ReferenceFov = 70f;

        private static readonly string[] ApprovedPoseNames =
        {
            "SupportHold", "CassetteCarry"
        };

        private static readonly string[] DigitNames =
        {
            "Thumb", "Index", "Middle", "Ring", "Pinky"
        };

        private static readonly string[] LegacyMarkerNames =
        {
            "RecorderSupportPalm", "RecorderSupportThumb",
            "RecorderSupportIndex", "RecorderSupportMiddle",
            "RecorderSupportRing", "RecorderSupportPinky",
            "CassetteSlotEntry", "CassetteSlotSeated", "CassetteSlotTravelAxis",
            "CassetteThumbGrip", "CassetteIndexGrip", "CassetteMiddleGrip",
            "CassetteFront", "CassetteTop", "CassetteInsertionAxis"
        };

        [Serializable]
        private sealed class PoseFile
        {
            public int schemaVersion = 1;
            public Pose[] poses = new Pose[0];
        }

        [Serializable]
        private sealed class Pose
        {
            public string name;
            public bool humanAuthored;
            public TransformPose[] transforms;
        }

        [Serializable]
        private sealed class TransformPose
        {
            public string name;
            public Vector3 localPosition;
            public Vector3 localEulerAngles;
        }

        private sealed class BoundControl
        {
            internal string Label;
            internal Transform Transform;
        }

        private sealed class SideBinding
        {
            internal string DisplayName;
            internal string Suffix;
            internal Transform UpperArm;
            internal Transform Forearm;
            internal Transform Wrist;
            internal Transform Palm;
            internal readonly List<Transform>[] Digits =
                Enumerable.Range(0, 5).Select(_ => new List<Transform>()).ToArray();
        }

        private sealed class RigBinding
        {
            internal Transform ModelRoot;
            internal SkinnedMeshRenderer ArmRenderer;
            internal SideBinding Left;
            internal SideBinding Right;
            internal Transform Recorder;
            internal Transform Cassette;
            internal readonly List<string> Missing = new List<string>();
            internal readonly List<string> Ambiguous = new List<string>();
            internal readonly List<string> ArmValidation = new List<string>();
            internal readonly List<string> ArmValidationFailures = new List<string>();
            internal readonly List<BoundControl> LeftArmControls = new List<BoundControl>();
            internal readonly List<BoundControl> RightArmControls = new List<BoundControl>();
            internal readonly List<BoundControl> SupportControls = new List<BoundControl>();
            internal readonly List<BoundControl> CassetteControls = new List<BoundControl>();
            internal bool Passed => Missing.Count == 0 && Ambiguous.Count == 0 &&
                ArmValidationFailures.Count == 0;
        }

        private sealed class PoseRestoreResult
        {
            internal bool Attempted;
            internal bool Passed;
            internal int Applied;
            internal int Expected;
            internal string Detail = "The manual authoring scene has not been restored yet.";
        }

        private readonly Dictionary<string, TransformPose> _working =
            new Dictionary<string, TransformPose>(StringComparer.Ordinal);
        private readonly Dictionary<string, TransformPose> _reset =
            new Dictionary<string, TransformPose>(StringComparer.Ordinal);

        private RigBinding _binding;
        private PoseRestoreResult _poseRestore = new PoseRestoreResult();
        private Vector2 _scroll;
        private int _supportSelection;
        private int _cassetteSelection;
        private int _loadSelection;
        private bool _showLegacyMarkers;
        private bool _supportExpanded = true;
        private bool _cassetteExpanded = true;
        private bool _leftArmExpanded = true;
        private bool _rightArmExpanded = true;
        private bool _leftUpperArmExpanded;
        private bool _leftForearmExpanded;
        private bool _leftWristExpanded;
        private bool _rightUpperArmExpanded;
        private bool _rightForearmExpanded;
        private bool _rightWristExpanded;
        private RenderTexture _mirror;
        private double _nextMirrorRepaint;

        [MenuItem("SoulPlayer/SoulRecorder Grip Pose Authoring")]
        internal static void Open()
        {
            SoulRecorderGripPoseAuthoringWindow window = GetWindow<
                SoulRecorderGripPoseAuthoringWindow>("SoulRecorder Grip Pose");
            window.minSize = new Vector2(600f, 800f);
            window.Show();
            window.InitializeFromScene();
        }

        internal static string GetLoadedRigBindingReport(out bool passed)
        {
            RigBinding binding = ResolveLoadedRig();
            passed = binding.Passed;
            return FormatBindingReport(binding);
        }

        internal void InitializeFromScene()
        {
            InitializeFromScene(false);
        }

        private void InitializeFromScene(bool restoreApprovedPose)
        {
            DisableSceneAnimator();
            _binding = ResolveLoadedRig();
            if (_binding.Passed && IsManualAuthoringScene())
            {
                _poseRestore = restoreApprovedPose
                    ? RestoreApprovedCompositePose(_binding)
                    : InspectApprovedCompositePose(_binding);
                ExpandResolvedBoneHierarchy(_binding);
            }
            else
            {
                _poseRestore = new PoseRestoreResult
                {
                    Attempted = false,
                    Passed = false,
                    Detail = _binding.Passed
                        ? "Open SoulRecorderManualGrip.unity to restore the authoring pose."
                        : "Rig binding must pass before a saved pose can be restored."
                };
            }
            _working.Clear();
            _reset.Clear();
            if (_binding.Passed)
            {
                CaptureInto(_working, AllControls(_binding));
                CaptureInto(_reset, AllControls(_binding));
            }
            EnsureReferenceCamera();
            Repaint();
            SceneView.RepaintAll();
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += DrawSceneGizmos;
            EditorApplication.update += Tick;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorApplication.delayCall += InitializeAfterWindowEnable;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= DrawSceneGizmos;
            EditorApplication.update -= Tick;
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorApplication.delayCall -= InitializeAfterWindowEnable;
            if (_mirror != null)
            {
                _mirror.Release();
                DestroyImmediate(_mirror);
                _mirror = null;
            }
        }

        private void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            EditorApplication.delayCall += () => InitializeFromScene(
                string.Equals(scene.name, ManualAuthoringSceneName, StringComparison.Ordinal));
        }

        private void InitializeAfterWindowEnable()
        {
            Scene scene = SceneManager.GetActiveScene();
            bool restore = scene.IsValid() && !scene.isDirty && string.Equals(
                scene.name, ManualAuthoringSceneName, StringComparison.Ordinal);
            InitializeFromScene(restore);
        }

        private void Tick()
        {
            if (EditorApplication.timeSinceStartup < _nextMirrorRepaint)
            {
                return;
            }
            _nextMirrorRepaint = EditorApplication.timeSinceStartup + (1.0 / 15.0);
            Repaint();
        }

        private void OnGUI()
        {
            if (_binding == null)
            {
                _binding = ResolveLoadedRig();
            }
            EditorGUILayout.HelpBox(
                FormatBindingReport(_binding),
                _binding.Passed ? MessageType.Info : MessageType.Error);
            EditorGUILayout.LabelField(
                "READY FOR MANUAL AUTHORING: " + (_binding.Passed ? "YES" : "NO"),
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "AUTHORING POSE RESTORED: " + (_poseRestore.Passed ? "YES" : "NO"),
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                _poseRestore.Detail,
                _poseRestore.Passed ? MessageType.Info : MessageType.Warning);
            EditorGUILayout.HelpBox(
                "HUMAN AUTHORING MODE — controls use only resolved transforms from " +
                "the loaded skeleton. No automatic pose operation runs here.",
                MessageType.None);

            DrawReferenceMirror();
            DrawSceneButtons();

            using (new EditorGUI.DisabledScope(!_binding.Passed))
            {
                DrawPoseButtons();
                DrawArmSkeletonControls();
                _showLegacyMarkers = EditorGUILayout.ToggleLeft(
                    "Show legacy contact markers (reference only; not authoritative)",
                    _showLegacyMarkers);
                EditorGUILayout.HelpBox(
                    "Position steps: 1 mm / 5 mm. Rotation steps: 0.5° / 1°. " +
                    "Only the selected, resolved transform is changed.",
                    MessageType.None);

                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                DrawControlGroup(
                    "LEFT — approved palm/fingers/recorder grip",
                    _binding.SupportControls,
                    ref _supportExpanded,
                    ref _supportSelection);
                EditorGUILayout.Space(10f);
                DrawControlGroup(
                    "RIGHT — approved palm/fingers/cassette grip",
                    _binding.CassetteControls,
                    ref _cassetteExpanded,
                    ref _cassetteSelection);
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawSceneButtons()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Prepare / Refresh Manual Rig", GUILayout.Height(28f)))
            {
                if (_working.Count == 0 || EditorUtility.DisplayDialog(
                    "Refresh SoulRecorder manual rig?",
                    "This recreates the authoring scene and discards unsaved pose edits.",
                    "Refresh",
                    "Cancel"))
                {
                    SoulRecorderFirstPersonPreview.PrepareManualAuthoringScene();
                    GUIUtility.ExitGUI();
                }
            }
            using (new EditorGUI.DisabledScope(!_binding.Passed || _reset.Count == 0))
            {
                if (GUILayout.Button("Reset Pose", GUILayout.Height(28f)))
                {
                    ResetPose();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawPoseButtons()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Save SupportHold"))
            {
                SaveNamedPose("SupportHold", SupportPoseControls(_binding));
            }
            if (GUILayout.Button("Save CassetteCarry"))
            {
                SaveNamedPose("CassetteCarry", CassettePoseControls(_binding));
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            _loadSelection = EditorGUILayout.Popup(
                "Saved pose", _loadSelection, new[] { "SupportHold", "CassetteCarry" });
            if (GUILayout.Button("Load Saved Pose", GUILayout.Width(150f)))
            {
                LoadNamedPose(_loadSelection == 0 ? "SupportHold" : "CassetteCarry");
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawArmSkeletonControls()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("MANUAL ARM SKELETON", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "These controls edit the actual skinned arm bones directly. Rotating or " +
                "moving a parent naturally carries its wrist, approved fingers, and held " +
                "prop through the existing hierarchy. Child local poses are not rewritten, " +
                "and no IK or solver correction runs.",
                MessageType.Info);
            DrawArmSide(
                "LEFT — SUPPORT ARM",
                _binding.LeftArmControls,
                ref _leftArmExpanded,
                ref _leftUpperArmExpanded,
                ref _leftForearmExpanded,
                ref _leftWristExpanded);
            DrawArmSide(
                "RIGHT — CASSETTE ARM",
                _binding.RightArmControls,
                ref _rightArmExpanded,
                ref _rightUpperArmExpanded,
                ref _rightForearmExpanded,
                ref _rightWristExpanded);
        }

        private void DrawArmSide(
            string title,
            IList<BoundControl> controls,
            ref bool expanded,
            ref bool upperExpanded,
            ref bool forearmExpanded,
            ref bool wristExpanded)
        {
            expanded = EditorGUILayout.Foldout(expanded, title, true);
            if (!expanded || controls.Count != 3)
            {
                return;
            }
            DrawArmBone(controls[0], ref upperExpanded);
            DrawArmBone(controls[1], ref forearmExpanded);
            DrawArmBone(controls[2], ref wristExpanded);
        }

        private void DrawArmBone(BoundControl control, ref bool expanded)
        {
            expanded = EditorGUILayout.Foldout(expanded, control.Label, true);
            if (expanded)
            {
                EditorGUI.indentLevel++;
                DrawTransformEditor(control);
                EditorGUI.indentLevel--;
            }
        }

        private void DrawControlGroup(
            string title,
            IList<BoundControl> controls,
            ref bool expanded,
            ref int selection)
        {
            expanded = EditorGUILayout.Foldout(expanded, title, true);
            if (!expanded || controls.Count == 0)
            {
                return;
            }
            selection = Mathf.Clamp(selection, 0, controls.Count - 1);
            string[] labels = controls.Select(value => value.Label).ToArray();
            selection = EditorGUILayout.Popup("Resolved control", selection, labels);
            DrawTransformEditor(controls[selection]);
        }

        private void DrawReferenceMirror()
        {
            Camera camera = EnsureReferenceCamera();
            float mirrorWidth = Mathf.Clamp(position.width - 20f, 320f, 640f);
            float mirrorHeight = mirrorWidth * (9f / 16f);
            Rect rect = GUILayoutUtility.GetRect(
                mirrorWidth,
                mirrorHeight,
                GUILayout.ExpandWidth(false));
            if (camera == null)
            {
                EditorGUI.HelpBox(rect, "Prepare the manual rig to create the FOV-70 camera.",
                    MessageType.Warning);
                return;
            }
            EnsureMirrorTexture(
                Mathf.Max(640, Mathf.RoundToInt(rect.width)),
                Mathf.Max(360, Mathf.RoundToInt(rect.height)));
            RenderTexture previous = camera.targetTexture;
            try
            {
                camera.fieldOfView = ReferenceFov;
                camera.targetTexture = _mirror;
                camera.Render();
            }
            finally
            {
                camera.targetTexture = previous;
                camera.fieldOfView = ReferenceFov;
            }
            GUI.DrawTexture(rect, _mirror, ScaleMode.ScaleToFit, false);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 6f, 250f, 22f),
                "PLAYER CAMERA REFERENCE — FOV 70");
        }

        private void DrawTransformEditor(BoundControl control)
        {
            TransformPose item;
            if (!_working.TryGetValue(control.Transform.name, out item))
            {
                item = Capture(control.Transform);
                _working[item.name] = item;
            }
            EditorGUILayout.LabelField(
                control.Label + "  [" + HierarchyPath(control.Transform) + "]",
                EditorStyles.boldLabel);
            Vector3 position = item.localPosition;
            Vector3 rotation = NormalizeEuler(item.localEulerAngles);
            EditorGUI.BeginChangeCheck();
            position = DrawVectorControls("Local position", position, true);
            rotation = DrawVectorControls("Local rotation", rotation, false);
            if (EditorGUI.EndChangeCheck())
            {
                item.localPosition = position;
                item.localEulerAngles = rotation;
                ApplyTransform(control.Transform, item);
            }
        }

        private static Vector3 DrawVectorControls(string label, Vector3 value, bool position)
        {
            EditorGUILayout.LabelField(label);
            value.x = DrawAxis("X", value.x, position);
            value.y = DrawAxis("Y", value.y, position);
            value.z = DrawAxis("Z", value.z, position);
            return value;
        }

        private static float DrawAxis(string axis, float value, bool position)
        {
            float small = position ? 0.001f : 0.5f;
            float large = position ? 0.005f : 1f;
            float minimum = position ? -1f : -180f;
            float maximum = position ? 1f : 180f;
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(axis, GUILayout.Width(18f));
            value = EditorGUILayout.Slider(value, minimum, maximum);
            if (GUILayout.Button(position ? "-5mm" : "-1°", GUILayout.Width(48f)))
            {
                value -= large;
            }
            if (GUILayout.Button(position ? "-1mm" : "-0.5°", GUILayout.Width(48f)))
            {
                value -= small;
            }
            if (GUILayout.Button(position ? "+1mm" : "+0.5°", GUILayout.Width(48f)))
            {
                value += small;
            }
            if (GUILayout.Button(position ? "+5mm" : "+1°", GUILayout.Width(48f)))
            {
                value += large;
            }
            EditorGUILayout.EndHorizontal();
            return position ? value : Mathf.DeltaAngle(0f, value);
        }

        private void ResetPose()
        {
            DisableSceneAnimator();
            _working.Clear();
            foreach (BoundControl control in AllControls(_binding))
            {
                TransformPose reset;
                if (!_reset.TryGetValue(control.Transform.name, out reset))
                {
                    continue;
                }
                TransformPose copy = Clone(reset);
                _working[copy.name] = copy;
                ApplyTransform(control.Transform, copy);
            }
            SceneView.RepaintAll();
        }

        private void SaveNamedPose(string name, IEnumerable<BoundControl> controls)
        {
            DisableSceneAnimator();
            List<BoundControl> values = controls.ToList();
            CaptureInto(_working, values);
            PoseFile document = ReadPoseFile();
            List<Pose> poses = document.poses == null
                ? new List<Pose>()
                : document.poses.ToList();
            poses.RemoveAll(value => string.Equals(value.name, name, StringComparison.Ordinal));
            poses.Add(new Pose
            {
                name = name,
                humanAuthored = true,
                transforms = values.Select(value => Capture(value.Transform)).ToArray()
            });
            document.poses = poses.OrderBy(value => value.name, StringComparer.Ordinal).ToArray();
            string absolute = Path.GetFullPath(PoseFilePath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute));
            File.WriteAllText(absolute, JsonUtility.ToJson(document, true));
            AssetDatabase.Refresh();
            Debug.Log("SoulRecorder human-authored pose saved: " + name + " -> " + absolute);
        }

        private void LoadNamedPose(string name)
        {
            PoseFile document = ReadPoseFile();
            Pose pose = (document.poses ?? new Pose[0]).FirstOrDefault(value =>
                string.Equals(value.name, name, StringComparison.Ordinal));
            if (pose == null || !pose.humanAuthored || pose.transforms == null)
            {
                ShowNotification(new GUIContent("No saved human-authored " + name + " pose."));
                return;
            }
            Dictionary<string, Transform> resolved = AllControls(_binding)
                .GroupBy(value => value.Transform.name, StringComparer.Ordinal)
                .Where(value => value.Count() == 1)
                .ToDictionary(
                    value => value.Key,
                    value => value.Single().Transform,
                    StringComparer.Ordinal);
            DisableSceneAnimator();
            foreach (TransformPose stored in pose.transforms)
            {
                Transform target;
                if (!resolved.TryGetValue(stored.name, out target))
                {
                    Debug.LogWarning(
                        "SoulRecorder saved pose transform is unavailable in the loaded rig: " +
                        stored.name);
                    continue;
                }
                TransformPose copy = Clone(stored);
                _working[copy.name] = copy;
                ApplyTransform(target, copy);
            }
            SceneView.RepaintAll();
        }

        private static RigBinding ResolveLoadedRig()
        {
            RigBinding result = new RigBinding();
            Transform[] sceneTransforms = FindObjectsOfType<Transform>(true)
                .Where(value => value.gameObject.scene.IsValid())
                .ToArray();
            Transform[] roots = sceneTransforms.Where(value => string.Equals(
                value.name, AnimatedHandsModelName, StringComparison.Ordinal)).ToArray();
            if (roots.Length != 1)
            {
                AddResolutionFailure(
                    result,
                    AnimatedHandsModelName,
                    roots.Length,
                    true);
                return result;
            }
            result.ModelRoot = roots[0];
            Transform[] skeleton = result.ModelRoot.GetComponentsInChildren<Transform>(true);
            SkinnedMeshRenderer[] armRenderers = result.ModelRoot
                .GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(value => string.Equals(
                    value.name, ArmRendererName, StringComparison.Ordinal))
                .ToArray();
            if (armRenderers.Length == 1)
            {
                result.ArmRenderer = armRenderers[0];
            }
            else
            {
                AddResolutionFailure(result, "SoulRecorderArms skinned renderer",
                    armRenderers.Length, false);
            }
            result.Left = ResolveSide(result, skeleton, "Left", "L");
            result.Right = ResolveSide(result, skeleton, "Right", "R");
            result.Recorder = ResolveSceneObject(
                result, sceneTransforms, "recorder model", "soulrecorder_fp");
            result.Cassette = ResolveSceneObject(
                result, sceneTransforms, "cassette model", "soultape_cassette");
            PopulateArmControls(result.LeftArmControls, result.Left);
            PopulateArmControls(result.RightArmControls, result.Right);
            PopulateGripControls(result.SupportControls, result.Left, result.Recorder, "Recorder");
            PopulateGripControls(result.CassetteControls, result.Right, result.Cassette, "Cassette");
            ValidateArmSkeletonControls(result);
            return result;
        }

        private static SideBinding ResolveSide(
            RigBinding owner,
            Transform[] skeleton,
            string displayName,
            string suffix)
        {
            SideBinding result = new SideBinding
            {
                DisplayName = displayName,
                Suffix = suffix
            };
            Transform[] side = skeleton.Where(value => IsSide(value.name, suffix)).ToArray();
            result.UpperArm = ResolveRole(
                owner, side, displayName + " upper arm", value =>
                    IsSemantic(value.name, "upperarm") || IsNumberedBone(value.name, "arm", 1));
            result.Forearm = ResolveRole(
                owner, side, displayName + " forearm", value =>
                    IsSemantic(value.name, "forearm") ||
                    IsSemantic(value.name, "lowerarm") ||
                    IsNumberedBone(value.name, "arm", 2));
            result.Wrist = ResolveRole(
                owner, side, displayName + " wrist", value =>
                    IsSemantic(value.name, "wrist") || IsNumberedBone(value.name, "hand", 1));
            result.Palm = ResolveRole(
                owner, side, displayName + " palm", value =>
                    IsSemantic(value.name, "palm") || IsNumberedBone(value.name, "hand", 2));

            for (int digit = 1; digit <= 5; digit++)
            {
                int expected = digit == 1 ? 4 : 3;
                List<Transform> joints = ResolveDigit(side, suffix, digit);
                if (joints.Count != expected)
                {
                    owner.Missing.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} {1} joints ({2}/{3})",
                        displayName,
                        DigitNames[digit - 1],
                        joints.Count,
                        expected));
                }
                result.Digits[digit - 1].AddRange(joints);
            }
            return result;
        }

        private static Transform ResolveRole(
            RigBinding owner,
            IEnumerable<Transform> candidates,
            string role,
            Func<Transform, bool> predicate)
        {
            Transform[] matches = candidates.Where(predicate).ToArray();
            if (matches.Length == 1)
            {
                return matches[0];
            }
            AddResolutionFailure(owner, role, matches.Length, false);
            return null;
        }

        private static List<Transform> ResolveDigit(
            IEnumerable<Transform> side,
            string suffix,
            int digit)
        {
            Regex numbered = new Regex(
                "^Finger[_ .-]?" + digit + "[_ .-]?(?<joint>[1-9])(?:[._ -]?(?:" +
                suffix + "|" + (suffix == "L" ? "Left" : "Right") + "))?$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            string semantic = DigitNames[digit - 1].ToLowerInvariant();
            List<KeyValuePair<int, Transform>> matches = new List<KeyValuePair<int, Transform>>();
            foreach (Transform value in side.Where(value =>
                value.name.IndexOf("end", StringComparison.OrdinalIgnoreCase) < 0))
            {
                Match match = numbered.Match(value.name);
                int joint;
                if (match.Success && int.TryParse(
                    match.Groups["joint"].Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out joint))
                {
                    matches.Add(new KeyValuePair<int, Transform>(joint, value));
                    continue;
                }
                if (Normalize(value.name).Contains(semantic))
                {
                    matches.Add(new KeyValuePair<int, Transform>(HierarchyDepth(value), value));
                }
            }
            return matches
                .OrderBy(value => value.Key)
                .ThenBy(value => value.Value.name, StringComparer.Ordinal)
                .Select(value => value.Value)
                .Distinct()
                .ToList();
        }

        private static void PopulateArmControls(
            ICollection<BoundControl> output,
            SideBinding side)
        {
            AddControl(output, "Upper arm", side?.UpperArm);
            AddControl(output, "Forearm / elbow", side?.Forearm);
            AddControl(output, "Wrist", side?.Wrist);
        }

        private static void PopulateGripControls(
            ICollection<BoundControl> output,
            SideBinding side,
            Transform heldObject,
            string heldObjectLabel)
        {
            AddControl(output, "Palm", side?.Palm);
            if (side != null)
            {
                for (int digit = 0; digit < side.Digits.Length; digit++)
                {
                    for (int joint = 0; joint < side.Digits[digit].Count; joint++)
                    {
                        AddControl(
                            output,
                            DigitNames[digit] + " joint " + (joint + 1),
                            side.Digits[digit][joint]);
                    }
                }
            }
            AddControl(output, heldObjectLabel + " local transform", heldObject);
        }

        private static void ValidateArmSkeletonControls(RigBinding binding)
        {
            if (binding.ModelRoot == null || binding.ArmRenderer == null ||
                binding.Left == null || binding.Right == null)
            {
                return;
            }
            ValidateHierarchyChain(binding, binding.Left);
            ValidateHierarchyChain(binding, binding.Right);
            ValidateArmSide(
                binding,
                binding.Left,
                binding.LeftArmControls,
                binding.Recorder,
                binding.ArmRenderer);
            ValidateArmSide(
                binding,
                binding.Right,
                binding.RightArmControls,
                binding.Cassette,
                binding.ArmRenderer);

            bool rendererWasExposedAsControl = AllControls(binding).Any(value =>
                value.Transform == binding.ArmRenderer.transform);
            string rendererStatus = "SoulRecorderArms pose-control exclusion: " +
                (rendererWasExposedAsControl ? "FAIL" : "PASS");
            binding.ArmValidation.Add(rendererStatus);
            if (rendererWasExposedAsControl)
            {
                binding.ArmValidationFailures.Add(rendererStatus);
            }
        }

        private static void ValidateHierarchyChain(RigBinding binding, SideBinding side)
        {
            bool armChain = side.Forearm != null && side.Forearm.parent == side.UpperArm &&
                side.Wrist != null && side.Wrist.parent == side.Forearm;
            bool handChain = side.Palm != null && side.Palm.IsChildOf(side.Wrist) &&
                side.Digits.SelectMany(value => value).All(value =>
                    value != null && value.IsChildOf(side.Wrist));
            string status = string.Format(
                CultureInfo.InvariantCulture,
                "{0} expandable hierarchy: {1} — {2} -> {3} -> {4}; palm/fingers below wrist={5}",
                side.DisplayName,
                armChain && handChain ? "PASS" : "FAIL",
                NameOf(side.UpperArm),
                NameOf(side.Forearm),
                NameOf(side.Wrist),
                YesNo(handChain));
            binding.ArmValidation.Add(status);
            if (!armChain || !handChain)
            {
                binding.ArmValidationFailures.Add(status);
            }
        }

        private static void ValidateArmSide(
            RigBinding binding,
            SideBinding side,
            IEnumerable<BoundControl> controls,
            Transform heldObject,
            SkinnedMeshRenderer renderer)
        {
            foreach (BoundControl control in controls)
            {
                Transform bone = control.Transform;
                bool skinnedBone = renderer.bones != null && renderer.bones.Contains(bone);
                Transform probe = heldObject != null && heldObject.IsChildOf(bone)
                    ? heldObject
                    : side.Palm != null && side.Palm.IsChildOf(bone)
                        ? side.Palm
                        : null;
                bool hierarchyCarriesApprovedPose = probe != null;
                bool temporaryRotationMovedProbe = false;
                bool childLocalPosePreserved = false;
                bool exactRestore = false;

                if (probe != null)
                {
                    Quaternion boneLocalRotation = bone.localRotation;
                    bool boneHadChanged = bone.hasChanged;
                    Vector3 probeWorldPosition = probe.position;
                    Quaternion probeWorldRotation = probe.rotation;
                    Vector3 probeLocalPosition = probe.localPosition;
                    Quaternion probeLocalRotation = probe.localRotation;
                    Vector3 probeLocalScale = probe.localScale;
                    try
                    {
                        bone.localRotation = boneLocalRotation *
                            Quaternion.AngleAxis(1f, Vector3.right);
                        temporaryRotationMovedProbe =
                            Vector3.Distance(probeWorldPosition, probe.position) > 0.000001f ||
                            Quaternion.Angle(probeWorldRotation, probe.rotation) > 0.001f;
                        childLocalPosePreserved =
                            Vector3.Distance(probeLocalPosition, probe.localPosition) < 0.0000001f &&
                            Quaternion.Angle(probeLocalRotation, probe.localRotation) < 0.00001f &&
                            Vector3.Distance(probeLocalScale, probe.localScale) < 0.0000001f;
                    }
                    finally
                    {
                        bone.localRotation = boneLocalRotation;
                        bone.hasChanged = boneHadChanged;
                    }
                    exactRestore =
                        Quaternion.Angle(boneLocalRotation, bone.localRotation) < 0.00001f &&
                        Vector3.Distance(probeWorldPosition, probe.position) < 0.000001f &&
                        Quaternion.Angle(probeWorldRotation, probe.rotation) < 0.00001f;
                }

                bool passed = skinnedBone && hierarchyCarriesApprovedPose &&
                    temporaryRotationMovedProbe && childLocalPosePreserved && exactRestore;
                string status = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} {1} ({2}): {3} — visibleMesh={4}, carriesApprovedPose={5}, " +
                    "temporaryRotation={6}, childLocalPosePreserved={7}, exactRestore={8}",
                    side.DisplayName,
                    control.Label.Split('—')[0].Trim(),
                    bone.name,
                    passed ? "PASS" : "FAIL",
                    YesNo(skinnedBone),
                    YesNo(hierarchyCarriesApprovedPose),
                    YesNo(temporaryRotationMovedProbe),
                    YesNo(childLocalPosePreserved),
                    YesNo(exactRestore));
                binding.ArmValidation.Add(status);
                if (!passed)
                {
                    binding.ArmValidationFailures.Add(status);
                }
            }
        }

        private static string YesNo(bool value)
        {
            return value ? "yes" : "no";
        }

        private static void AddControl(
            ICollection<BoundControl> output,
            string role,
            Transform transform)
        {
            if (transform == null)
            {
                return;
            }
            output.Add(new BoundControl
            {
                Label = role + " — " + transform.name,
                Transform = transform
            });
        }

        private static Transform ResolveSceneObject(
            RigBinding owner,
            IEnumerable<Transform> scene,
            string role,
            string exactName)
        {
            Transform[] matches = scene.Where(value => string.Equals(
                value.name, exactName, StringComparison.Ordinal)).ToArray();
            if (matches.Length == 1)
            {
                return matches[0];
            }
            AddResolutionFailure(owner, role, matches.Length, false);
            return null;
        }

        private static void AddResolutionFailure(
            RigBinding result,
            string role,
            int count,
            bool root)
        {
            string text = role + " (found " + count + ")";
            if (count == 0)
            {
                result.Missing.Add(text);
            }
            else
            {
                result.Ambiguous.Add(text);
            }
        }

        private static string FormatBindingReport(RigBinding binding)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("SoulRecorder manual rig binding: " +
                (binding.Passed ? "PASS" : "FAIL"));
            report.AppendLine("Visible arm renderer: " +
                (binding.ArmRenderer == null ? "UNRESOLVED" : binding.ArmRenderer.name) +
                " (pose control: NO)");
            AppendSideReport(report, binding.Left);
            AppendSideReport(report, binding.Right);
            if (binding.ArmValidation.Count > 0)
            {
                report.AppendLine("Arm skeleton control validation:");
                foreach (string validation in binding.ArmValidation)
                {
                    report.AppendLine("  " + validation);
                }
            }
            if (binding.Missing.Count > 0)
            {
                report.AppendLine("Missing: " + string.Join(", ", binding.Missing));
            }
            if (binding.Ambiguous.Count > 0)
            {
                report.AppendLine("Ambiguous: " + string.Join(", ", binding.Ambiguous));
            }
            report.Append("READY FOR MANUAL AUTHORING: ");
            report.Append(binding.Passed ? "YES" : "NO");
            return report.ToString();
        }

        private static void AppendSideReport(StringBuilder report, SideBinding side)
        {
            if (side == null)
            {
                return;
            }
            report.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0}: upper={1}; forearm={2}; wrist={3}; palm={4}",
                side.DisplayName,
                NameOf(side.UpperArm),
                NameOf(side.Forearm),
                NameOf(side.Wrist),
                NameOf(side.Palm)));
            report.AppendLine(
                "  digits: " + string.Join(
                    "; ",
                    Enumerable.Range(0, 5).Select(index =>
                        DigitNames[index] + "=" +
                        string.Join(
                            "/",
                            side.Digits[index].Select(value => value.name)))));
        }

        private static string NameOf(Transform value)
        {
            return value == null ? "UNRESOLVED" : value.name;
        }

        private static bool IsSide(string name, string suffix)
        {
            string normalized = Normalize(name);
            string semantic = suffix == "L" ? "left" : "right";
            return name.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("_" + suffix, StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith(semantic, StringComparison.Ordinal) ||
                normalized.EndsWith(suffix.ToLowerInvariant(), StringComparison.Ordinal);
        }

        private static bool IsSemantic(string name, string role)
        {
            return Normalize(name).Contains(role);
        }

        private static bool IsNumberedBone(string name, string family, int number)
        {
            Match match = Regex.Match(
                name,
                "^" + family + "[_ .-]?" + number + "(?:[._ -]?(?:L|R|Left|Right))?$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return match.Success;
        }

        private static string Normalize(string value)
        {
            return new string((value ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
        }

        private static int HierarchyDepth(Transform value)
        {
            int depth = 0;
            while (value != null)
            {
                depth++;
                value = value.parent;
            }
            return depth;
        }

        private static IEnumerable<BoundControl> AllControls(RigBinding binding)
        {
            return binding.LeftArmControls
                .Concat(binding.RightArmControls)
                .Concat(binding.SupportControls)
                .Concat(binding.CassetteControls)
                .GroupBy(value => value.Transform.GetInstanceID())
                .Select(value => value.First());
        }

        private static IEnumerable<BoundControl> SupportPoseControls(RigBinding binding)
        {
            return binding.LeftArmControls.Concat(binding.SupportControls);
        }

        private static IEnumerable<BoundControl> CassettePoseControls(RigBinding binding)
        {
            return binding.RightArmControls.Concat(binding.CassetteControls);
        }

        private static void CaptureInto(
            IDictionary<string, TransformPose> destination,
            IEnumerable<BoundControl> controls)
        {
            foreach (BoundControl control in controls)
            {
                destination[control.Transform.name] = Capture(control.Transform);
            }
        }

        private static TransformPose Capture(Transform value)
        {
            return new TransformPose
            {
                name = value.name,
                localPosition = value.localPosition,
                localEulerAngles = NormalizeEuler(value.localEulerAngles)
            };
        }

        private static void ApplyTransform(Transform target, TransformPose pose)
        {
            Undo.RecordObject(target, "Pose SoulRecorder grip");
            target.localPosition = pose.localPosition;
            target.localRotation = Quaternion.Euler(pose.localEulerAngles);
            EditorUtility.SetDirty(target);
        }

        internal static string RestoreApprovedAuthoringPose(out bool passed)
        {
            DisableSceneAnimator();
            RigBinding binding = ResolveLoadedRig();
            PoseRestoreResult result = binding.Passed
                ? RestoreApprovedCompositePose(binding)
                : new PoseRestoreResult
                {
                    Attempted = true,
                    Passed = false,
                    Detail = "Rig binding failed before the saved authoring pose could be restored."
                };
            if (binding.Passed)
            {
                ExpandResolvedBoneHierarchy(binding);
            }
            passed = result.Passed;
            return FormatPoseRestoreReport(result);
        }

        private static PoseRestoreResult RestoreApprovedCompositePose(RigBinding binding)
        {
            PoseRestoreResult current = InspectApprovedCompositePose(binding);
            if (current.Passed)
            {
                current.Detail = string.Format(
                    CultureInfo.InvariantCulture,
                    "Approved SupportHold + CassetteCarry already restored ({0}/{1} transforms); scene was not rewritten.",
                    current.Applied,
                    current.Expected);
                return current;
            }
            PoseRestoreResult result = ApplyApprovedCompositePose(binding, true);
            if (result.Passed)
            {
                Scene scene = SceneManager.GetActiveScene();
                if (scene.IsValid())
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                }
            }
            return result;
        }

        private static PoseRestoreResult InspectApprovedCompositePose(RigBinding binding)
        {
            return ApplyApprovedCompositePose(binding, false);
        }

        private static PoseRestoreResult ApplyApprovedCompositePose(
            RigBinding binding,
            bool apply)
        {
            PoseRestoreResult result = new PoseRestoreResult { Attempted = true };
            PoseFile document = ReadPoseFile();
            Dictionary<string, Transform> controls = AllControls(binding)
                .GroupBy(value => value.Transform.name, StringComparer.Ordinal)
                .Where(value => value.Count() == 1)
                .ToDictionary(
                    value => value.Key,
                    value => value.Single().Transform,
                    StringComparer.Ordinal);
            List<string> failures = new List<string>();
            foreach (string poseName in ApprovedPoseNames)
            {
                Pose pose = (document.poses ?? new Pose[0]).FirstOrDefault(value =>
                    string.Equals(value.name, poseName, StringComparison.Ordinal));
                if (pose == null || !pose.humanAuthored || pose.transforms == null)
                {
                    failures.Add(poseName + " is missing or is not human-authored");
                    continue;
                }
                foreach (TransformPose stored in pose.transforms)
                {
                    result.Expected++;
                    Transform target;
                    if (!controls.TryGetValue(stored.name, out target))
                    {
                        failures.Add(poseName + " transform is unresolved: " + stored.name);
                        continue;
                    }
                    if (apply)
                    {
                        ApplyTransform(target, Clone(stored));
                    }
                    bool matches = Vector3.Distance(
                        target.localPosition, stored.localPosition) <= 0.000001f &&
                        Quaternion.Angle(
                            target.localRotation,
                            Quaternion.Euler(stored.localEulerAngles)) <= 0.001f;
                    if (!matches)
                    {
                        failures.Add(poseName + " differs at " + stored.name);
                        continue;
                    }
                    result.Applied++;
                }
            }
            result.Passed = failures.Count == 0 && result.Expected > 0 &&
                result.Applied == result.Expected;
            result.Detail = result.Passed
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "Approved SupportHold + CassetteCarry restored from JSON ({0}/{1} transforms).",
                    result.Applied,
                    result.Expected)
                : "Saved authoring pose was not fully restored: " +
                    string.Join("; ", failures.Take(8).ToArray());
            return result;
        }

        private static string FormatPoseRestoreReport(PoseRestoreResult result)
        {
            return "AUTHORING POSE RESTORED: " + (result.Passed ? "YES" : "NO") +
                Environment.NewLine + result.Detail;
        }

        private static bool IsManualAuthoringScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            return scene.IsValid() && string.Equals(
                scene.name, ManualAuthoringSceneName, StringComparison.Ordinal);
        }

        private static void ExpandResolvedBoneHierarchy(RigBinding binding)
        {
            EditorApplication.delayCall += () =>
            {
                Type hierarchyType = typeof(EditorWindow).Assembly.GetType(
                    "UnityEditor.SceneHierarchyWindow");
                MethodInfo expand = hierarchyType == null
                    ? null
                    : hierarchyType.GetMethod(
                        "SetExpandedRecursive",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (hierarchyType == null || expand == null)
                {
                    Debug.LogWarning(
                        "SoulRecorder could not automatically expand the Unity Hierarchy; " +
                        "the resolved arm bones remain available in the pose window.");
                    return;
                }
                UnityEngine.Object[] windows = Resources.FindObjectsOfTypeAll(hierarchyType);
                foreach (UnityEngine.Object window in windows)
                {
                    expand.Invoke(window, new object[]
                    {
                        binding.ModelRoot.gameObject.GetInstanceID(), true
                    });
                }
                EditorApplication.RepaintHierarchyWindow();
            };
        }

        private static PoseFile ReadPoseFile()
        {
            string absolute = Path.GetFullPath(PoseFilePath);
            if (!File.Exists(absolute))
            {
                return new PoseFile();
            }
            PoseFile result = JsonUtility.FromJson<PoseFile>(File.ReadAllText(absolute));
            return result ?? new PoseFile();
        }

        private static TransformPose Clone(TransformPose value)
        {
            return new TransformPose
            {
                name = value.name,
                localPosition = value.localPosition,
                localEulerAngles = value.localEulerAngles
            };
        }

        private static void DisableSceneAnimator()
        {
            Animator animator = FindObjectsOfType<Animator>(true).FirstOrDefault(value =>
                value.gameObject.scene.IsValid() &&
                string.Equals(
                    value.gameObject.name,
                    AnimatedHandsModelName,
                    StringComparison.Ordinal));
            if (animator != null && animator.enabled)
            {
                animator.enabled = false;
                EditorUtility.SetDirty(animator);
            }
        }

        private static Camera EnsureReferenceCamera()
        {
            Camera camera = FindObjectsOfType<Camera>(true).FirstOrDefault(value =>
                value.gameObject.scene.IsValid() &&
                string.Equals(value.name, ReferenceCameraName, StringComparison.Ordinal));
            if (camera != null)
            {
                camera.fieldOfView = ReferenceFov;
            }
            return camera;
        }

        private void EnsureMirrorTexture(int width, int height)
        {
            if (_mirror != null && _mirror.width == width && _mirror.height == height)
            {
                return;
            }
            if (_mirror != null)
            {
                _mirror.Release();
                DestroyImmediate(_mirror);
            }
            _mirror = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "SoulRecorder Manual Grip FOV70 Mirror",
                antiAliasing = 2,
                hideFlags = HideFlags.HideAndDontSave
            };
            _mirror.Create();
        }

        private void DrawSceneGizmos(SceneView sceneView)
        {
            if (!_showLegacyMarkers)
            {
                return;
            }
            foreach (string name in LegacyMarkerNames)
            {
                Transform marker = FindObjectsOfType<Transform>(true).FirstOrDefault(value =>
                    value.gameObject.scene.IsValid() &&
                    string.Equals(value.name, name, StringComparison.Ordinal));
                if (marker == null)
                {
                    continue;
                }
                Handles.color = name.StartsWith("Recorder", StringComparison.Ordinal) ||
                    name.StartsWith("CassetteSlot", StringComparison.Ordinal)
                    ? Color.cyan
                    : Color.magenta;
                float size = HandleUtility.GetHandleSize(marker.position) * 0.035f;
                Handles.SphereHandleCap(0, marker.position, Quaternion.identity, size,
                    EventType.Repaint);
                if (name.EndsWith("Axis", StringComparison.Ordinal))
                {
                    Handles.DrawLine(marker.position, marker.position +
                        (marker.forward * size * 5f));
                }
                Handles.Label(marker.position, name + " (legacy)");
            }
        }

        private static string HierarchyPath(Transform value)
        {
            Stack<string> names = new Stack<string>();
            while (value != null)
            {
                names.Push(value.name);
                value = value.parent;
            }
            return string.Join("/", names.ToArray());
        }

        private static Vector3 NormalizeEuler(Vector3 value)
        {
            return new Vector3(
                Mathf.DeltaAngle(0f, value.x),
                Mathf.DeltaAngle(0f, value.y),
                Mathf.DeltaAngle(0f, value.z));
        }
    }

    /// <summary>
    /// Scene-level persistence hook. Unity stores scene transforms but not Hierarchy
    /// expansion state, and the manual scene may be opened while its authoring window
    /// is closed. A cleanly opened manual scene therefore restores the two approved
    /// human-authored pose halves directly from JSON before editing resumes.
    /// </summary>
    [InitializeOnLoad]
    internal static class SoulRecorderManualAuthoringScenePersistence
    {
        static SoulRecorderManualAuthoringScenePersistence()
        {
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorApplication.delayCall += RestoreCleanLoadedManualScene;
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            if (!string.Equals(
                scene.name,
                SoulRecorderGripPoseAuthoringWindow.ManualAuthoringSceneName,
                StringComparison.Ordinal))
            {
                return;
            }
            EditorApplication.delayCall += RestoreCleanLoadedManualScene;
        }

        private static void RestoreCleanLoadedManualScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.isDirty || !string.Equals(
                scene.name,
                SoulRecorderGripPoseAuthoringWindow.ManualAuthoringSceneName,
                StringComparison.Ordinal))
            {
                return;
            }
            bool restored;
            string report = SoulRecorderGripPoseAuthoringWindow
                .RestoreApprovedAuthoringPose(out restored);
            if (restored)
            {
                Debug.Log("SoulRecorder manual authoring scene reopened. " + report);
            }
            else
            {
                Debug.LogError("SoulRecorder manual authoring scene restore failed. " + report);
            }
        }
    }
}
