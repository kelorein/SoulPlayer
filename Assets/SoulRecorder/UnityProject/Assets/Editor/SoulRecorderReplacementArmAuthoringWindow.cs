using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SoulPlayer.Editor
{
    /// <summary>
    /// Direct, solver-free controls for the replacement FPS skeleton.  The
    /// recorder and cassette are independent scene anchors, never finger children.
    /// </summary>
    internal sealed class SoulRecorderReplacementArmAuthoringWindow : EditorWindow
    {
        private SoulRecorderReplacementArmStaging.Rig _rig;
        private Transform _recorderGrip;
        private Transform _cassetteGrip;
        private Vector2 _scroll;
        private readonly Dictionary<string, Vector3> _bindPositions =
            new Dictionary<string, Vector3>(StringComparer.Ordinal);
        private readonly Dictionary<string, Quaternion> _bindRotations =
            new Dictionary<string, Quaternion>(StringComparer.Ordinal);
        private readonly Dictionary<string, Vector3> _bindScales =
            new Dictionary<string, Vector3>(StringComparer.Ordinal);
        private string _activePoseName;
        private bool _activePoseStartedFromStaticMaster;

        [MenuItem("SoulPlayer/Replacement Arms/Manual Static Pose Authoring")]
        internal static void Open()
        {
            SoulRecorderReplacementArmAuthoringWindow window = GetWindow<
                SoulRecorderReplacementArmAuthoringWindow>("Replacement Arm Pose");
            window.minSize = new Vector2(560f, 760f);
            window.Show();
            window.Bind();
        }

        private void OnEnable()
        {
            EditorSceneManager.sceneOpened += SceneOpened;
            SceneView.duringSceneGui += DrawCassetteContactGizmos;
            EditorApplication.delayCall += Bind;
        }

        private void OnDisable()
        {
            EditorSceneManager.sceneOpened -= SceneOpened;
            SceneView.duringSceneGui -= DrawCassetteContactGizmos;
            EditorApplication.delayCall -= Bind;
        }

        private void SceneOpened(UnityEngine.SceneManagement.Scene scene,
            OpenSceneMode mode)
        {
            Bind();
        }

        private void Bind()
        {
            _rig = SoulRecorderReplacementArmStaging.ResolveLoadedRig();
            GameObject props = GameObject.Find("AuthoringProps");
            _recorderGrip = Find(props, "RecorderGrip");
            _cassetteGrip = Find(props, "CassetteGrip");
            _bindPositions.Clear();
            _bindRotations.Clear();
            _bindScales.Clear();
            if (_rig != null && _rig.Passed)
            {
                foreach (Transform transform in ControlledTransforms())
                {
                    _bindPositions[PathOf(transform)] = transform.localPosition;
                    _bindRotations[PathOf(transform)] = transform.localRotation;
                    _bindScales[PathOf(transform)] = transform.localScale;
                }
            }
            Repaint();
            SceneView.RepaintAll();
        }

        private void OnGUI()
        {
            bool propsIndependent = PropsIndependent();
            bool passed = _rig != null && _rig.Passed;
            bool ready = passed && propsIndependent && _recorderGrip != null &&
                _cassetteGrip != null;
            string poseLibraryStatus = ready ? string.Empty :
                "replacement rig or prop anchors are unresolved";
            bool poseLibraryReady = ready &&
                SoulRecorderReplacementGripPoseStore.TryValidateLibrary(
                    out poseLibraryStatus);

            EditorGUILayout.HelpBox("NEW ARM RIG VALIDATION: " +
                (passed ? "PASS" : "FAIL"), passed ? MessageType.Info : MessageType.Error);
            EditorGUILayout.HelpBox("READY FOR MANUAL STATIC POSE: " +
                (ready ? "YES" : "NO"), ready ? MessageType.Info : MessageType.Error);
            EditorGUILayout.HelpBox("READY FOR CASSETTE KEY-POSE AUTHORING: " +
                (poseLibraryReady ? "YES" : "NO") +
                (poseLibraryReady ? string.Empty : " — " + poseLibraryStatus),
                poseLibraryReady ? MessageType.Info : MessageType.Error);
            EditorGUILayout.LabelField("FOV reference", "70 degrees");
            EditorGUILayout.LabelField("Pose system", "Direct bone transforms; IK/solvers disabled");
            EditorGUILayout.LabelField("Props", propsIndependent ?
                "Independent AuthoringProps anchors" : "FAIL - prop is under skeleton");
            EditorGUILayout.LabelField("StaticMaster_v1", "LOCKED / load-only");
            EditorGUILayout.LabelField("Active key pose",
                string.IsNullOrEmpty(_activePoseName) ? "None" : _activePoseName);

            if (!poseLibraryReady)
            {
                if (_rig != null)
                {
                    foreach (string failure in _rig.Failures)
                    {
                        EditorGUILayout.HelpBox(failure, MessageType.Error);
                    }
                }
                if (GUILayout.Button("Rebind Loaded Scene"))
                {
                    Bind();
                }
                return;
            }

            EditorGUILayout.Space();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (GUILayout.Button("Reset Pose"))
            {
                ResetPose();
            }
            if (GUILayout.Button("Load StaticMaster_v1 (LOCKED)"))
            {
                LoadStaticMaster();
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("CASSETTE KEY POSES", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Start each new pose from locked StaticMaster_v1. No IK or automatic pose solving is applied.",
                MessageType.Info);
            foreach (string poseName in SoulRecorderReplacementGripPoseStore.KeyPoseNames)
            {
                DrawKeyPoseControls(poseName);
            }

            DrawSide("LEFT / RECORDER HAND", "L");
            DrawSide("RIGHT / CASSETTE HAND", "R");
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("INDEPENDENT PROP ANCHORS", EditorStyles.boldLabel);
            DrawTransform("RecorderGrip", _recorderGrip, true);
            DrawTransform("CassetteGrip", _cassetteGrip, true);
            EditorGUILayout.EndScrollView();
        }

        private void DrawSide(string title, string side)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            DrawTransform("Upper Arm", _rig.Bones[side + "_arm"], false);
            DrawTransform("Forearm / Elbow", _rig.Bones[side + "_elbow"], false);
            DrawTransform("Wrist / Hand", _rig.Bones[side + "_wrist"], false);
            foreach (KeyValuePair<string, string> digit in new Dictionary<string, string>
            {
                { "Thumb", "thumb" }, { "Index", "point" },
                { "Middle", "middle" }, { "Ring", "ring" }, { "Pinky", "pink" }
            })
            {
                EditorGUILayout.LabelField(digit.Key, EditorStyles.miniBoldLabel);
                for (int joint = 1; joint <= 3; joint++)
                {
                    DrawTransform("  Joint " + joint,
                        _rig.Bones[side + "_" + digit.Value + joint], false);
                }
            }
        }

        private static void DrawTransform(string label, Transform transform,
            bool position)
        {
            EditorGUI.BeginChangeCheck();
            if (position)
            {
                Vector3 localPosition = EditorGUILayout.Vector3Field(
                    label + " Position", transform.localPosition);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(transform, "Pose " + label);
                    transform.localPosition = localPosition;
                    EditorUtility.SetDirty(transform);
                    SceneView.RepaintAll();
                }
                EditorGUI.BeginChangeCheck();
            }
            Vector3 euler = EditorGUILayout.Vector3Field(
                label + " Rotation", Normalize(transform.localEulerAngles));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(transform, "Pose " + label);
                transform.localRotation = Quaternion.Euler(euler);
                EditorUtility.SetDirty(transform);
                SceneView.RepaintAll();
            }
        }

        private void ResetPose()
        {
            foreach (Transform transform in ControlledTransforms())
            {
                string path = PathOf(transform);
                Vector3 position;
                Quaternion rotation;
                Vector3 scale;
                if (_bindPositions.TryGetValue(path, out position) &&
                    _bindRotations.TryGetValue(path, out rotation) &&
                    _bindScales.TryGetValue(path, out scale))
                {
                    Undo.RecordObject(transform, "Reset replacement arm pose");
                    transform.localPosition = position;
                    transform.localRotation = rotation;
                    transform.localScale = scale;
                }
            }
            MarkSceneDirty();
        }

        private void DrawKeyPoseControls(string poseName)
        {
            bool saved = SoulRecorderReplacementGripPoseStore.HasKeyPose(poseName);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(poseName,
                string.Equals(_activePoseName, poseName, StringComparison.Ordinal)
                    ? "ACTIVE" : saved ? "Saved" : "Not saved");
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Start " + poseName + " from StaticMaster_v1"))
            {
                StartKeyPose(poseName);
            }
            EditorGUI.BeginDisabledGroup(
                !string.Equals(_activePoseName, poseName, StringComparison.Ordinal) ||
                !_activePoseStartedFromStaticMaster);
            if (GUILayout.Button("Save " + poseName))
            {
                SaveKeyPose(poseName);
            }
            EditorGUI.EndDisabledGroup();
            EditorGUI.BeginDisabledGroup(!saved);
            if (GUILayout.Button("Load " + poseName))
            {
                LoadKeyPose(poseName);
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Capture Current Scene as " + poseName))
            {
                CaptureCurrentSceneAs(poseName, saved);
            }
            if (string.Equals(poseName, "CassetteContact", StringComparison.Ordinal))
            {
                EditorGUILayout.LabelField("Contact gizmos",
                    string.Equals(_activePoseName, poseName, StringComparison.Ordinal)
                        ? GetCassetteContactGizmoStatus() : "Shown when CassetteContact is active");
            }
            EditorGUILayout.EndVertical();
        }

        private void CaptureCurrentSceneAs(string poseName, bool overwriting)
        {
            if (overwriting && !EditorUtility.DisplayDialog(
                "Overwrite " + poseName + "?",
                "Capture the exact current scene transforms and overwrite " +
                poseName + "?\n\nNo pose will be loaded and StaticMaster_v1 will remain locked.",
                "Overwrite " + poseName, "Cancel"))
            {
                return;
            }

            List<Transform> current = ControlledTransforms().ToList();
            SoulRecorderReplacementGripPoseStore.ExactSnapshot unchanged =
                SoulRecorderReplacementGripPoseStore.Capture(current);
            string hash = SoulRecorderReplacementGripPoseStore.SaveKeyPose(
                poseName, current);
            SoulRecorderReplacementGripPoseStore.AssertExact(unchanged, current);
            Debug.Log("SoulRecorder captured the current scene as " + poseName +
                ": SHA-256=" + hash +
                ". No scene transforms were loaded or modified; StaticMaster_v1 remains locked.");
            Repaint();
        }

        private void LoadStaticMaster()
        {
            if (!File.Exists(Path.GetFullPath(
                SoulRecorderReplacementArmStaging.PoseFile)))
            {
                EditorUtility.DisplayDialog("SoulRecorder", "No replacement static pose has been saved yet.", "OK");
                return;
            }
            SoulRecorderReplacementGripPoseStore.LoadStaticMasterV1(
                ControlledTransforms(), true);
            _activePoseName = null;
            _activePoseStartedFromStaticMaster = false;
            MarkSceneDirty();
        }

        private void StartKeyPose(string poseName)
        {
            SoulRecorderReplacementGripPoseStore.LoadStaticMasterV1(
                ControlledTransforms(), true);
            _activePoseName = poseName;
            _activePoseStartedFromStaticMaster = true;
            MarkSceneDirty();
            Debug.Log("SoulRecorder " + poseName +
                " authoring started from locked StaticMaster_v1.");
        }

        private void SaveKeyPose(string poseName)
        {
            if (!string.Equals(_activePoseName, poseName, StringComparison.Ordinal) ||
                !_activePoseStartedFromStaticMaster)
            {
                throw new InvalidOperationException(poseName +
                    " must be started from StaticMaster_v1 before it can be saved.");
            }
            string hash = SoulRecorderReplacementGripPoseStore.SaveKeyPose(
                poseName, ControlledTransforms());
            Debug.Log("SoulRecorder replacement " + poseName + " saved: " +
                Path.GetFullPath(SoulRecorderReplacementArmStaging.PoseFile) +
                " SHA-256=" + hash + ". StaticMaster_v1 remains locked.");
            Repaint();
        }

        private void LoadKeyPose(string poseName)
        {
            SoulRecorderReplacementGripPoseStore.LoadKeyPose(
                poseName, ControlledTransforms(), true);
            _activePoseName = poseName;
            _activePoseStartedFromStaticMaster = true;
            MarkSceneDirty();
        }

        internal static void BatchAuditLockedStaticMasterV1RoundTrip()
        {
            try
            {
                EditorSceneManager.OpenScene(
                    SoulRecorderReplacementArmStaging.AuthoringScene,
                    OpenSceneMode.Single);
                SoulRecorderReplacementArmStaging.Rig rig =
                    SoulRecorderReplacementArmStaging.ResolveLoadedRig();
                if (!rig.Passed)
                {
                    throw new InvalidOperationException(string.Join("; ", rig.Failures));
                }
                GameObject props = GameObject.Find("AuthoringProps");
                Transform recorderGrip = Find(props, "RecorderGrip");
                Transform cassetteGrip = Find(props, "CassetteGrip");
                if (recorderGrip == null || cassetteGrip == null)
                {
                    throw new InvalidOperationException(
                        "Replacement authoring prop anchors were not found.");
                }
                List<Transform> controlled =
                    SoulRecorderReplacementArmStaging.RequiredBones
                        .Select(name => rig.Bones[name])
                        .Concat(new[] { recorderGrip, cassetteGrip })
                        .ToList();
                if (controlled.Count != 38)
                {
                    throw new InvalidOperationException(
                        "StaticMaster_v1 expected 38 transforms; resolved " +
                        controlled.Count.ToString(CultureInfo.InvariantCulture) + ".");
                }

                SoulRecorderReplacementGripPoseStore.ExactSnapshot expected =
                    SoulRecorderReplacementGripPoseStore.Capture(controlled);
                SoulRecorderReplacementGripPoseStore.DisturbForRoundTrip(controlled);
                string loadedHash =
                    SoulRecorderReplacementGripPoseStore.LoadStaticMasterV1(controlled, false);
                SoulRecorderReplacementGripPoseStore.AssertExact(expected, controlled);
                if (!string.Equals(
                    SoulRecorderReplacementGripPoseStore.LockedStaticMasterHash,
                    loadedHash, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "StaticMaster_v1 checksum changed during round trip.");
                }

                Debug.Log("STATIC MASTER LOCKED AUDIT: PASS");
                Debug.Log("StaticMaster_v1 transform count: " + controlled.Count);
                Debug.Log("StaticMaster_v1 pose SHA-256: " + loadedHash);
                Debug.Log("StaticMaster_v1 round-trip exact restore: PASS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError("STATIC MASTER LOCKED AUDIT: FAIL");
                EditorApplication.Exit(1);
            }
        }

        private void DrawCassetteContactGizmos(SceneView sceneView)
        {
            if (!string.Equals(_activePoseName, "CassetteContact",
                StringComparison.Ordinal) || _recorderGrip == null ||
                _cassetteGrip == null)
            {
                return;
            }

            Transform cassetteAxis = Find(_cassetteGrip.gameObject,
                "CassetteInsertionAxis");
            Transform slotAxis = Find(_recorderGrip.gameObject,
                "CassetteSlotTravelAxis");
            Transform slotEntry = Find(_recorderGrip.gameObject,
                "CassetteSlotEntry");
            Transform seated = Find(_recorderGrip.gameObject,
                "CassetteSlotSeated");
            Vector3 cassetteCenter;
            bool hasCenter = TryGetRendererCenter(_cassetteGrip, out cassetteCenter);

            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            if (cassetteAxis != null)
            {
                DrawAxis(cassetteAxis.position, cassetteAxis.forward,
                    new Color(1f, 0.75f, 0.05f, 1f), "Cassette insertion axis");
            }
            if (slotAxis != null)
            {
                DrawAxis(slotAxis.position, slotAxis.forward,
                    new Color(0.1f, 0.95f, 1f, 1f), "Slot insertion axis");
            }
            if (hasCenter)
            {
                DrawPoint(cassetteCenter, Color.yellow, "Cassette center");
            }
            if (slotEntry != null)
            {
                DrawPoint(slotEntry.position, Color.cyan, "Slot entry");
            }
            if (seated != null)
            {
                DrawPoint(seated.position, Color.green, "Seated position");
            }
        }

        private string GetCassetteContactGizmoStatus()
        {
            string[] required =
            {
                "CassetteInsertionAxis", "CassetteSlotTravelAxis",
                "CassetteSlotEntry", "CassetteSlotSeated"
            };
            foreach (string marker in required)
            {
                GameObject root = marker == "CassetteInsertionAxis"
                    ? _cassetteGrip.gameObject : _recorderGrip.gameObject;
                if (Find(root, marker) == null)
                {
                    return "FAIL - missing " + marker;
                }
            }
            Vector3 ignored;
            return TryGetRendererCenter(_cassetteGrip, out ignored)
                ? "PASS - five guides visible" : "FAIL - cassette renderer missing";
        }

        private static void DrawAxis(Vector3 origin, Vector3 direction,
            Color color, string label)
        {
            float length = 0.10f;
            Handles.color = color;
            Handles.ArrowHandleCap(0, origin,
                Quaternion.LookRotation(direction.normalized), length,
                EventType.Repaint);
            Handles.Label(origin + direction.normalized * length, label);
        }

        private static void DrawPoint(Vector3 position, Color color, string label)
        {
            Handles.color = color;
            float size = HandleUtility.GetHandleSize(position) * 0.025f;
            Handles.SphereHandleCap(0, position, Quaternion.identity, size,
                EventType.Repaint);
            Handles.Label(position + Vector3.up * size * 1.5f, label);
        }

        private static bool TryGetRendererCenter(Transform root, out Vector3 center)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                center = root.position;
                return false;
            }
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }
            center = bounds.center;
            return true;
        }

        private IEnumerable<Transform> ControlledTransforms()
        {
            if (_rig == null || !_rig.Passed)
            {
                yield break;
            }
            foreach (string name in SoulRecorderReplacementArmStaging.RequiredBones)
            {
                yield return _rig.Bones[name];
            }
            if (_recorderGrip != null)
            {
                yield return _recorderGrip;
            }
            if (_cassetteGrip != null)
            {
                yield return _cassetteGrip;
            }
        }

        private bool PropsIndependent()
        {
            return _rig != null && _rig.Root != null && _recorderGrip != null &&
                _cassetteGrip != null && !_recorderGrip.IsChildOf(_rig.Root.transform) &&
                !_cassetteGrip.IsChildOf(_rig.Root.transform);
        }

        private static Transform Find(GameObject root, string name)
        {
            return root == null ? null : root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(transform => transform.name == name);
        }

        private static string PathOf(Transform transform)
        {
            List<string> names = new List<string>();
            for (Transform current = transform; current != null; current = current.parent)
            {
                names.Add(current.name);
            }
            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        private static Vector3 Normalize(Vector3 euler)
        {
            return new Vector3(Normalize(euler.x), Normalize(euler.y), Normalize(euler.z));
        }

        private static float Normalize(float angle)
        {
            return angle > 180f ? angle - 360f : angle;
        }

        private static void MarkSceneDirty()
        {
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            SceneView.RepaintAll();
        }
    }

    internal static class SoulRecorderReplacementGripPoseStore
    {
        private const string StaticMasterName = "StaticMaster_v1";
        internal const string LockedStaticMasterHash =
            "2EA27472ACBEBF323D9C2502AF7FDFCE1FC3FB8ACDD72D56629C8E8ECEC20AA2";
        internal static readonly string[] KeyPoseNames =
        {
            "CassetteAlign", "CassetteContact", "CassetteHalfInserted",
            "CassetteSeated", "CassetteEjectGrip", "CassetteEjectClear"
        };

        [Serializable]
        private sealed class PoseFile
        {
            public int schemaVersion = 1;
            public string poseName = StaticMasterName;
            public int transformCount;
            public string poseSha256;
            public TransformPose[] transforms = new TransformPose[0];
            public PoseEntry[] keyPoses = new PoseEntry[0];
        }

        [Serializable]
        private sealed class PoseEntry
        {
            public string poseName;
            public int transformCount;
            public string poseSha256;
            public TransformPose[] transforms = new TransformPose[0];
        }

        [Serializable]
        internal sealed class TransformPose
        {
            public string path;
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;
            public string[] localPositionBits = new string[0];
            public string[] localRotationBits = new string[0];
            public string[] localScaleBits = new string[0];
        }

        internal sealed class ExactSnapshot
        {
            private readonly Dictionary<string, TransformPose> _transforms =
                new Dictionary<string, TransformPose>(StringComparer.Ordinal);

            internal void Add(string path, TransformPose pose)
            {
                _transforms.Add(path, pose);
            }

            internal bool TryGetValue(string path, out TransformPose pose)
            {
                return _transforms.TryGetValue(path, out pose);
            }
        }

        internal static bool HasKeyPose(string poseName)
        {
            ValidateKeyPoseName(poseName);
            PoseFile file = ReadAndValidateLibrary();
            return (file.keyPoses ?? new PoseEntry[0]).Any(entry =>
                string.Equals(entry.poseName, poseName, StringComparison.Ordinal));
        }

        internal static bool TryValidateLibrary(out string status)
        {
            try
            {
                ReadAndValidateLibrary();
                status = "locked StaticMaster_v1 verified";
                return true;
            }
            catch (Exception exception)
            {
                status = exception.Message;
                return false;
            }
        }

        internal static string LoadStaticMasterV1(IEnumerable<Transform> transforms,
            bool recordUndo)
        {
            PoseFile file = ReadAndValidateLibrary();
            ApplyPose(StaticMasterName, file.transforms, file.transformCount,
                transforms, recordUndo);
            return file.poseSha256;
        }

        internal static string SaveKeyPose(string poseName,
            IEnumerable<Transform> transforms)
        {
            ValidateKeyPoseName(poseName);
            string fullPath = FullPosePath();
            string originalText = File.ReadAllText(fullPath);
            PoseFile file = ReadAndValidateLibrary(originalText);
            TransformPose[] poses = transforms.Select(CaptureTransform).ToArray();
            EnsureUniquePaths(poses);
            if (poses.Length != file.transformCount)
            {
                throw new InvalidDataException(poseName +
                    " transform count does not match locked StaticMaster_v1.");
            }
            PoseEntry entry = new PoseEntry
            {
                poseName = poseName,
                transformCount = poses.Length,
                transforms = poses
            };
            entry.poseSha256 = ComputePoseHash(file.schemaVersion, entry.poseName,
                entry.transformCount, entry.transforms);

            List<PoseEntry> entries = (file.keyPoses ?? new PoseEntry[0]).ToList();
            int existing = entries.FindIndex(candidate => string.Equals(
                candidate.poseName, poseName, StringComparison.Ordinal));
            if (existing >= 0)
            {
                entries[existing] = entry;
            }
            else
            {
                entries.Add(entry);
            }
            file.keyPoses = KeyPoseNames
                .Select(name => entries.FirstOrDefault(candidate => string.Equals(
                    candidate.poseName, name, StringComparison.Ordinal)))
                .Where(candidate => candidate != null)
                .ToArray();

            string updatedText = ReplaceKeyPoseSectionPreservingStaticMaster(
                originalText, file.keyPoses);
            File.WriteAllText(fullPath, updatedText, new UTF8Encoding(false));
            AssetDatabase.Refresh();

            // Re-read after writing so corruption can never be reported as success.
            ReadAndValidateLibrary(updatedText);
            return entry.poseSha256;
        }

        internal static string LoadKeyPose(string poseName,
            IEnumerable<Transform> transforms, bool recordUndo)
        {
            ValidateKeyPoseName(poseName);
            PoseFile file = ReadAndValidateLibrary();
            PoseEntry entry = (file.keyPoses ?? new PoseEntry[0]).FirstOrDefault(
                candidate => string.Equals(candidate.poseName, poseName,
                    StringComparison.Ordinal));
            if (entry == null)
            {
                throw new InvalidDataException("Replacement key pose is not saved: " +
                    poseName);
            }
            ApplyPose(poseName, entry.transforms, entry.transformCount,
                transforms, recordUndo);
            return entry.poseSha256;
        }

        internal static ExactSnapshot Capture(IEnumerable<Transform> transforms)
        {
            ExactSnapshot snapshot = new ExactSnapshot();
            foreach (Transform transform in transforms)
            {
                TransformPose pose = CaptureTransform(transform);
                snapshot.Add(pose.path, pose);
            }
            return snapshot;
        }

        internal static void DisturbForRoundTrip(IEnumerable<Transform> transforms)
        {
            int index = 1;
            foreach (Transform transform in transforms)
            {
                float amount = index * 0.0001f;
                transform.localPosition += new Vector3(amount, -amount, amount * 0.5f);
                transform.localRotation = transform.localRotation *
                    Quaternion.Euler(index * 0.1f, -index * 0.05f, index * 0.025f);
                transform.localScale = Vector3.Scale(transform.localScale,
                    new Vector3(1f + amount, 1f - amount * 0.5f, 1f + amount * 0.25f));
                index++;
            }
        }

        internal static void AssertExact(ExactSnapshot expected,
            IEnumerable<Transform> transforms)
        {
            foreach (Transform transform in transforms)
            {
                string path = PathOf(transform);
                TransformPose pose;
                if (!expected.TryGetValue(path, out pose))
                {
                    throw new InvalidOperationException(
                        "Round-trip transform was unexpected: " + path);
                }
                AssertVectorExact(path + " position", pose.localPosition,
                    transform.localPosition);
                AssertQuaternionExact(path + " rotation", pose.localRotation,
                    transform.localRotation);
                AssertVectorExact(path + " scale", pose.localScale,
                    transform.localScale);
            }
        }

        private static string FullPosePath()
        {
            return Path.GetFullPath(SoulRecorderReplacementArmStaging.PoseFile);
        }

        private static PoseFile ReadAndValidateLibrary()
        {
            return ReadAndValidateLibrary(File.ReadAllText(FullPosePath()));
        }

        private static PoseFile ReadAndValidateLibrary(string text)
        {
            PoseFile file = JsonUtility.FromJson<PoseFile>(text);
            if (file == null || file.schemaVersion != 1 ||
                !string.Equals(file.poseName, StaticMasterName,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Replacement StaticMaster_v1 pose header is invalid.");
            }
            TransformPose[] master = file.transforms ?? new TransformPose[0];
            if (file.transformCount != master.Length || file.transformCount != 38)
            {
                throw new InvalidDataException(
                    "Locked StaticMaster_v1 transform count is invalid.");
            }
            EnsureUniquePaths(master);
            string calculatedMaster = ComputePoseHash(file.schemaVersion,
                file.poseName, file.transformCount, master);
            if (!string.Equals(file.poseSha256, LockedStaticMasterHash,
                    StringComparison.Ordinal) ||
                !string.Equals(calculatedMaster, LockedStaticMasterHash,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Locked StaticMaster_v1 changed or failed its SHA-256 check.");
            }

            HashSet<string> poseNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (PoseEntry entry in file.keyPoses ?? new PoseEntry[0])
            {
                ValidateKeyPoseName(entry.poseName);
                if (!poseNames.Add(entry.poseName))
                {
                    throw new InvalidDataException(
                        "Replacement pose library contains a duplicate key pose: " +
                        entry.poseName);
                }
                TransformPose[] poses = entry.transforms ?? new TransformPose[0];
                if (entry.transformCount != poses.Length ||
                    entry.transformCount != file.transformCount)
                {
                    throw new InvalidDataException(entry.poseName +
                        " transform count is invalid.");
                }
                EnsureUniquePaths(poses);
                string calculated = ComputePoseHash(file.schemaVersion,
                    entry.poseName, entry.transformCount, poses);
                if (!string.Equals(entry.poseSha256, calculated,
                    StringComparison.Ordinal))
                {
                    throw new InvalidDataException(entry.poseName +
                        " pose SHA-256 mismatch.");
                }
            }
            return file;
        }

        private static void ValidateKeyPoseName(string poseName)
        {
            if (!KeyPoseNames.Contains(poseName, StringComparer.Ordinal))
            {
                throw new InvalidDataException(
                    "Unsupported replacement cassette key pose: " + poseName);
            }
        }

        private static void ApplyPose(string poseName, TransformPose[] poseValues,
            int expectedCount, IEnumerable<Transform> transforms, bool recordUndo)
        {
            TransformPose[] poses = poseValues ?? new TransformPose[0];
            if (poses.Length != expectedCount)
            {
                throw new InvalidDataException(poseName +
                    " pose transform count is invalid.");
            }
            EnsureUniquePaths(poses);
            Dictionary<string, Transform> byPath = transforms.ToDictionary(
                PathOf, transform => transform, StringComparer.Ordinal);
            if (byPath.Count != poses.Length)
            {
                throw new InvalidDataException(
                    "Loaded rig transform count does not match " + poseName + ".");
            }
            foreach (TransformPose pose in poses)
            {
                Transform transform;
                if (!byPath.TryGetValue(pose.path, out transform))
                {
                    throw new InvalidDataException(poseName +
                        " transform was not found: " + pose.path);
                }
                if (recordUndo)
                {
                    Undo.RecordObject(transform, "Load " + poseName);
                }
                transform.localPosition = VectorFromBits(
                    pose.localPositionBits, pose.path + " localPosition");
                transform.localRotation = QuaternionFromBits(
                    pose.localRotationBits, pose.path + " localRotation");
                transform.localScale = VectorFromBits(
                    pose.localScaleBits, pose.path + " localScale");
                EditorUtility.SetDirty(transform);
            }
        }

        private static string ReplaceKeyPoseSectionPreservingStaticMaster(
            string originalText, PoseEntry[] keyPoses)
        {
            const string marker = "\"keyPoses\"";
            int originalMarker = originalText.IndexOf(marker,
                StringComparison.Ordinal);
            if (originalMarker < 0)
            {
                throw new InvalidDataException(
                    "Replacement pose library has no keyPoses section.");
            }
            int originalLine = originalText.LastIndexOf('\n', originalMarker);
            if (originalLine < 0)
            {
                throw new InvalidDataException(
                    "Replacement pose library keyPoses formatting is invalid.");
            }

            PoseFile library = ReadAndValidateLibrary(originalText);
            library.keyPoses = keyPoses ?? new PoseEntry[0];
            string regenerated = JsonUtility.ToJson(library, true);
            int generatedMarker = regenerated.IndexOf(marker,
                StringComparison.Ordinal);
            int generatedLine = regenerated.LastIndexOf('\n', generatedMarker);
            if (generatedMarker < 0 || generatedLine < 0)
            {
                throw new InvalidDataException(
                    "Replacement pose library keyPoses could not be serialized.");
            }
            string preservedMaster = originalText.Substring(0, originalLine + 1);
            string generatedKeyPoses = regenerated.Substring(generatedLine + 1);
            return preservedMaster + generatedKeyPoses;
        }

        private static TransformPose CaptureTransform(Transform transform)
        {
            return new TransformPose
            {
                path = PathOf(transform),
                localPosition = transform.localPosition,
                localRotation = transform.localRotation,
                localScale = transform.localScale,
                localPositionBits = VectorBits(transform.localPosition),
                localRotationBits = QuaternionBits(transform.localRotation),
                localScaleBits = VectorBits(transform.localScale)
            };
        }

        private static string ComputePoseHash(int schemaVersion, string poseName,
            int transformCount, TransformPose[] transforms)
        {
            StringBuilder canonical = new StringBuilder();
            canonical.Append("schema=").Append(schemaVersion)
                .Append("\nname=").Append(poseName)
                .Append("\ncount=").Append(transformCount).Append('\n');
            foreach (TransformPose pose in transforms ?? new TransformPose[0])
            {
                canonical.Append(pose.path.Length).Append(':').Append(pose.path).Append('|');
                AppendBits(canonical, pose.localPositionBits, 3,
                    pose.path + " localPosition");
                AppendBits(canonical, pose.localRotationBits, 4,
                    pose.path + " localRotation");
                AppendBits(canonical, pose.localScaleBits, 3,
                    pose.path + " localScale");
                canonical.Append('\n');
            }
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(
                    Encoding.UTF8.GetBytes(canonical.ToString())))
                    .Replace("-", string.Empty);
            }
        }

        private static string[] VectorBits(Vector3 value)
        {
            return new[] { FloatBits(value.x), FloatBits(value.y), FloatBits(value.z) };
        }

        private static string[] QuaternionBits(Quaternion value)
        {
            return new[]
            {
                FloatBits(value.x), FloatBits(value.y), FloatBits(value.z),
                FloatBits(value.w)
            };
        }

        private static string FloatBits(float value)
        {
            int bits = BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
            return bits.ToString("X8", CultureInfo.InvariantCulture);
        }

        private static void AppendBits(StringBuilder text, string[] values,
            int expectedCount, string label)
        {
            if (values == null || values.Length != expectedCount)
            {
                throw new InvalidDataException(label + " raw bit count is invalid.");
            }
            foreach (string value in values)
            {
                uint parsed;
                if (value == null || value.Length != 8 ||
                    !uint.TryParse(value, NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out parsed))
                {
                    throw new InvalidDataException(label + " contains invalid raw float bits.");
                }
                text.Append(value.ToUpperInvariant()).Append('|');
            }
        }

        private static Vector3 VectorFromBits(string[] values, string label)
        {
            ValidateBits(values, 3, label);
            return new Vector3(FloatFromBits(values[0]), FloatFromBits(values[1]),
                FloatFromBits(values[2]));
        }

        private static Quaternion QuaternionFromBits(string[] values, string label)
        {
            ValidateBits(values, 4, label);
            return new Quaternion(FloatFromBits(values[0]), FloatFromBits(values[1]),
                FloatFromBits(values[2]), FloatFromBits(values[3]));
        }

        private static void ValidateBits(string[] values, int count, string label)
        {
            if (values == null || values.Length != count)
            {
                throw new InvalidDataException(label + " raw bit count is invalid.");
            }
            foreach (string value in values)
            {
                uint parsed;
                if (value == null || value.Length != 8 ||
                    !uint.TryParse(value, NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out parsed))
                {
                    throw new InvalidDataException(label + " contains invalid raw float bits.");
                }
            }
        }

        private static float FloatFromBits(string value)
        {
            uint parsed = uint.Parse(value, NumberStyles.HexNumber,
                CultureInfo.InvariantCulture);
            return BitConverter.ToSingle(BitConverter.GetBytes(parsed), 0);
        }

        private static void EnsureUniquePaths(IEnumerable<TransformPose> poses)
        {
            HashSet<string> paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (TransformPose pose in poses)
            {
                if (string.IsNullOrWhiteSpace(pose.path) || !paths.Add(pose.path))
                {
                    throw new InvalidDataException(
                        "Replacement pose contains an empty or duplicate transform path.");
                }
            }
        }

        private static string PathOf(Transform transform)
        {
            List<string> names = new List<string>();
            for (Transform current = transform; current != null; current = current.parent)
            {
                names.Add(current.name);
            }
            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        private static void AssertVectorExact(string label, Vector3 expected,
            Vector3 actual)
        {
            AssertFloatExact(label + ".x", expected.x, actual.x);
            AssertFloatExact(label + ".y", expected.y, actual.y);
            AssertFloatExact(label + ".z", expected.z, actual.z);
        }

        private static void AssertQuaternionExact(string label,
            Quaternion expected, Quaternion actual)
        {
            AssertFloatExact(label + ".x", expected.x, actual.x);
            AssertFloatExact(label + ".y", expected.y, actual.y);
            AssertFloatExact(label + ".z", expected.z, actual.z);
            AssertFloatExact(label + ".w", expected.w, actual.w);
        }

        private static void AssertFloatExact(string label, float expected,
            float actual)
        {
            int expectedBits = BitConverter.ToInt32(BitConverter.GetBytes(expected), 0);
            int actualBits = BitConverter.ToInt32(BitConverter.GetBytes(actual), 0);
            if (expectedBits != actualBits)
            {
                throw new InvalidOperationException(label +
                    " was not restored bit-for-bit.");
            }
        }
    }
}
