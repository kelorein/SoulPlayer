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
    /// Isolated intake and validation for the CC-BY DJMaesen FPS arms.  Nothing
    /// in this class replaces the packaged BAMEN fallback or edits animation.
    /// </summary>
    internal static class SoulRecorderReplacementArmStaging
    {
        internal const string SourceFbx =
            "Assets/SoulRecorderReplacementArms/Source/fpsarms.fbx";
        internal const string ValidationScene =
            "Assets/SoulRecorderReplacementArms/Scenes/DJMaesenRigValidation.unity";
        internal const string AuthoringScene =
            "Assets/SoulRecorderReplacementArms/Scenes/SoulRecorderReplacementStaticPose.unity";
        internal const string PoseFile =
            "Assets/SoulRecorderAuthoring/SoulRecorderReplacementGripPoses.json";
        internal const string ValidationReport =
            "Assets/SoulRecorderReplacementArms/Validation/rig-validation-report.txt";
        internal const string ValidationImage =
            "Assets/SoulRecorderReplacementArms/Validation/rig-articulation-fov70.png";
        internal const string FpsImage =
            "Assets/SoulRecorderReplacementArms/Validation/rig-fps-rest-fov70.png";
        internal const string AuthoringImage =
            "Assets/SoulRecorderReplacementArms/Validation/manual-static-start-fov70.png";
        private const string MaterialPath =
            "Assets/SoulRecorderReplacementArms/Materials/DJMaesenFirstPersonArms.mat";
        internal const string RootName = "SoulRecorderReplacementArms";
        internal const string CameraName = "SoulRecorder Replacement FOV70 Camera";

        internal static readonly string[] RequiredBones =
        {
            "L_arm", "L_elbow", "L_wrist",
            "R_arm", "R_elbow", "R_wrist",
            "L_thumb1", "L_thumb2", "L_thumb3",
            "L_point1", "L_point2", "L_point3",
            "L_middle1", "L_middle2", "L_middle3",
            "L_ring1", "L_ring2", "L_ring3",
            "L_pink1", "L_pink2", "L_pink3",
            "R_thumb1", "R_thumb2", "R_thumb3",
            "R_point1", "R_point2", "R_point3",
            "R_middle1", "R_middle2", "R_middle3",
            "R_ring1", "R_ring2", "R_ring3",
            "R_pink1", "R_pink2", "R_pink3"
        };

        internal sealed class Rig
        {
            internal GameObject Root;
            internal SkinnedMeshRenderer Renderer;
            internal readonly Dictionary<string, Transform> Bones =
                new Dictionary<string, Transform>(StringComparer.Ordinal);
            internal readonly List<string> Failures = new List<string>();
            internal bool Passed => Failures.Count == 0;
        }

        private struct PropPlacement
        {
            internal bool Found;
            internal Vector3 GripCameraPosition;
            internal Quaternion GripCameraRotation;
            internal Vector3 ModelLocalPosition;
            internal Quaternion ModelLocalRotation;
        }

        [MenuItem("SoulPlayer/Replacement Arms/1 - Validate Prop-Free Rig")]
        internal static void ValidatePropFreeRig()
        {
            try
            {
                RunValidation();
                Debug.Log("SoulRecorder replacement-arm validation PASS. Props attached: NO.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                throw;
            }
        }

        [MenuItem("SoulPlayer/Replacement Arms/2 - Prepare Manual Static Pose")]
        internal static void PrepareManualStaticPose()
        {
            Rig validated = RunValidation();
            if (!validated.Passed)
            {
                throw new InvalidOperationException(
                    "Replacement arm validation must pass before prop staging.");
            }

            CreateAuthoringScene();
            SoulRecorderReplacementArmAuthoringWindow.Open();
        }

        // Batch entry used by offline validation. Unity exits nonzero on failure.
        internal static void BatchValidateAndPrepare()
        {
            try
            {
                RunValidation();
                CreateAuthoringScene();
                AuditSavedAuthoringScene();
                AssetDatabase.SaveAssets();
                Debug.Log("NEW ARM RIG VALIDATION: PASS");
                Debug.Log("READY FOR MANUAL STATIC POSE: YES");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError("NEW ARM RIG VALIDATION: FAIL");
                Debug.LogError("READY FOR MANUAL STATIC POSE: NO");
                EditorApplication.Exit(1);
            }
        }

        internal static Rig ResolveLoadedRig()
        {
            GameObject root = GameObject.Find(RootName);
            return root == null ? Failed("Scene root was not found: " + RootName) : Audit(root);
        }

        private static Rig RunValidation()
        {
            EnsureFolders();
            ConfigureSourceImport();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            scene.name = "DJMaesenRigValidation";
            GameObject root = InstantiateSource(scene);
            Rig rig = Audit(root);
            if (!rig.Passed)
            {
                WriteReport(rig, false);
                throw new InvalidOperationException(string.Join("\n", rig.Failures));
            }

            Camera fpsCamera = CreateFpsCamera(rig, scene);
            CreateLighting(scene);
            Render(fpsCamera, FpsImage);

            ApplyValidationPose(rig);
            Camera externalCamera = CreateExternalCamera(rig, scene);
            Render(externalCamera, ValidationImage);
            externalCamera.enabled = false;
            fpsCamera.enabled = true;

            EditorSceneManager.SaveScene(scene, ValidationScene);
            WriteReport(rig, true);
            AssetDatabase.Refresh();
            return rig;
        }

        private static void CreateAuthoringScene()
        {
            PropPlacement recorder = CaptureApprovedPlacement("RecorderGrip");
            PropPlacement cassette = CaptureApprovedPlacement("CassetteGrip");

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            scene.name = "SoulRecorderReplacementStaticPose";
            GameObject root = InstantiateSource(scene);
            Rig rig = Audit(root);
            if (!rig.Passed)
            {
                throw new InvalidOperationException(string.Join("\n", rig.Failures));
            }

            Camera camera = CreateFpsCamera(rig, scene);
            CreateLighting(scene);

            GameObject props = new GameObject("AuthoringProps");
            SceneManager.MoveGameObjectToScene(props, scene);
            Transform recorderGrip = NewAnchor("RecorderGrip", props.transform);
            Transform cassetteGrip = NewAnchor("CassetteGrip", props.transform);

            ApplyPropPlacement(camera, recorderGrip, recorder,
                new Vector3(-0.10f, -0.08f, 0.48f));
            ApplyPropPlacement(camera, cassetteGrip, cassette,
                new Vector3(0.16f, -0.06f, 0.43f));
            InstantiateProp("Assets/SoulPlayer/Generated/soulrecorder_fp.prefab",
                "SoulRecorderModel", recorderGrip, recorder);
            InstantiateProp("Assets/SoulPlayer/Generated/soultape_cassette.prefab",
                "SoulTapeCassette", cassetteGrip, cassette);

            // Props deliberately remain a root-level sibling of the skeleton.
            if (recorderGrip.IsChildOf(root.transform) ||
                cassetteGrip.IsChildOf(root.transform))
            {
                throw new InvalidOperationException(
                    "Authoring props must not be children of the arm skeleton.");
            }

            Render(camera, AuthoringImage);
            EditorSceneManager.SaveScene(scene, AuthoringScene);
            Selection.activeGameObject = root;
            AssetDatabase.Refresh();
        }

        private static void AuditSavedAuthoringScene()
        {
            Scene scene = EditorSceneManager.OpenScene(AuthoringScene, OpenSceneMode.Single);
            Rig rig = ResolveLoadedRig();
            if (!rig.Passed)
            {
                throw new InvalidOperationException(
                    "Saved replacement authoring rig did not reopen cleanly: " +
                    string.Join("; ", rig.Failures));
            }
            Transform props = FindInScene(scene, "AuthoringProps");
            Transform recorderGrip = FindInScene(scene, "RecorderGrip");
            Transform cassetteGrip = FindInScene(scene, "CassetteGrip");
            Camera camera = FindInScene(scene, CameraName)?.GetComponent<Camera>();
            if (props == null || recorderGrip == null || cassetteGrip == null)
            {
                throw new InvalidOperationException(
                    "Saved authoring prop anchors did not persist.");
            }
            if (recorderGrip.IsChildOf(rig.Root.transform) ||
                cassetteGrip.IsChildOf(rig.Root.transform))
            {
                throw new InvalidOperationException(
                    "Saved authoring props reopened under the arm skeleton.");
            }
            if (camera == null || Mathf.Abs(camera.fieldOfView - 70f) > 0.01f)
            {
                throw new InvalidOperationException(
                    "Saved authoring FOV-70 reference camera did not persist.");
            }
            if (rig.Renderer.sharedMaterial == null ||
                rig.Renderer.sharedMaterial.mainTexture == null)
            {
                throw new InvalidOperationException(
                    "Replacement arm material or albedo did not persist after scene reopen.");
            }
            Debug.Log("SoulRecorder replacement authoring scene reopen audit PASS.");
        }

        private static Rig Audit(GameObject root)
        {
            Rig rig = new Rig { Root = root };
            SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<
                SkinnedMeshRenderer>(true);
            if (renderers.Length != 1)
            {
                rig.Failures.Add("Expected exactly one skinned arm renderer; found " +
                    renderers.Length.ToString(CultureInfo.InvariantCulture) + ".");
            }
            else
            {
                rig.Renderer = renderers[0];
                rig.Renderer.name = "DJMaesenFirstPersonArmsRenderer";
            }

            Dictionary<string, List<Transform>> byName = root
                .GetComponentsInChildren<Transform>(true)
                .GroupBy(transform => transform.name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToList(),
                    StringComparer.Ordinal);
            foreach (string name in RequiredBones)
            {
                List<Transform> matches;
                if (!byName.TryGetValue(name, out matches) || matches.Count != 1)
                {
                    rig.Failures.Add(name + " resolved " +
                        (matches == null ? "0" : matches.Count.ToString(
                            CultureInfo.InvariantCulture)) + " times.");
                    continue;
                }
                rig.Bones[name] = matches[0];
            }

            ValidateChain(rig, "L_arm", "L_elbow", "L_wrist");
            ValidateChain(rig, "R_arm", "R_elbow", "R_wrist");
            if (rig.Renderer != null)
            {
                HashSet<Transform> weighted = new HashSet<Transform>(rig.Renderer.bones);
                foreach (KeyValuePair<string, Transform> bone in rig.Bones)
                {
                    if (!weighted.Contains(bone.Value))
                    {
                        rig.Failures.Add(bone.Key + " is not bound to the visible arm mesh.");
                    }
                }
                ValidateVisibleDeformation(rig);
            }
            return rig;
        }

        private static void ValidateChain(Rig rig, string upperName,
            string forearmName, string wristName)
        {
            Transform upper;
            Transform forearm;
            Transform wrist;
            if (!rig.Bones.TryGetValue(upperName, out upper) ||
                !rig.Bones.TryGetValue(forearmName, out forearm) ||
                !rig.Bones.TryGetValue(wristName, out wrist))
            {
                return;
            }
            if (forearm.parent != upper)
            {
                rig.Failures.Add(forearmName + " is not directly parented to " + upperName + ".");
            }
            if (wrist.parent != forearm)
            {
                rig.Failures.Add(wristName + " is not directly parented to " + forearmName + ".");
            }
            float upperSpan = Vector3.Distance(upper.position, forearm.position);
            float forearmSpan = Vector3.Distance(forearm.position, wrist.position);
            float shoulderToWrist = Vector3.Distance(upper.position, wrist.position);
            if (upperSpan < 0.12f || upperSpan > 0.35f)
            {
                rig.Failures.Add(upperName + " pivot span is implausible: " + upperSpan + "m.");
            }
            if (forearmSpan < 0.12f || forearmSpan > 0.35f)
            {
                rig.Failures.Add(forearmName + " pivot span is implausible: " + forearmSpan + "m.");
            }
            if (shoulderToWrist < 0.25f)
            {
                rig.Failures.Add(upperName + " pivot is too close to the hand.");
            }
        }

        private static void ValidateVisibleDeformation(Rig rig)
        {
            Mesh before = new Mesh();
            Mesh after = new Mesh();
            try
            {
                foreach (string name in new[]
                {
                    "L_arm", "L_elbow", "L_wrist", "L_thumb1", "L_point1",
                    "L_middle1", "L_ring1", "L_pink1", "R_arm", "R_elbow",
                    "R_wrist", "R_thumb1", "R_point1", "R_middle1", "R_ring1",
                    "R_pink1"
                })
                {
                    Transform bone = rig.Bones[name];
                    Quaternion original = bone.localRotation;
                    rig.Renderer.BakeMesh(before);
                    bone.localRotation = original * Quaternion.Euler(0f, 0f, 12f);
                    rig.Renderer.BakeMesh(after);
                    bone.localRotation = original;
                    Vector3[] a = before.vertices;
                    Vector3[] b = after.vertices;
                    float maximum = 0f;
                    for (int index = 0; index < Math.Min(a.Length, b.Length); index++)
                    {
                        maximum = Mathf.Max(maximum, Vector3.Distance(a[index], b[index]));
                    }
                    if (maximum < 0.0001f)
                    {
                        rig.Failures.Add(name + " does not visibly deform the arm mesh.");
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(before);
                UnityEngine.Object.DestroyImmediate(after);
            }
        }

        private static void ApplyValidationPose(Rig rig)
        {
            RotateRelative(rig.Bones["L_arm"], 0f, -20f, 0f);
            RotateRelative(rig.Bones["R_arm"], 0f, 20f, 0f);
            RotateRelative(rig.Bones["L_elbow"], -60f, 0f, 0f);
            RotateRelative(rig.Bones["R_elbow"], -60f, 0f, 0f);
            RotateRelative(rig.Bones["L_wrist"], 0f, 0f, 15f);
            RotateRelative(rig.Bones["R_wrist"], 0f, 0f, -15f);
            foreach (string side in new[] { "L", "R" })
            {
                float sign = side == "L" ? 1f : -1f;
                foreach (string digit in new[] { "thumb", "point", "middle", "ring", "pink" })
                {
                    RotateRelative(rig.Bones[side + "_" + digit + "1"], 0f, 0f, sign * 18f);
                    RotateRelative(rig.Bones[side + "_" + digit + "2"], 0f, 0f, sign * 32f);
                    RotateRelative(rig.Bones[side + "_" + digit + "3"], 0f, 0f, sign * 42f);
                }
            }
        }

        private static void RotateRelative(Transform transform, float x, float y, float z)
        {
            transform.localRotation = transform.localRotation * Quaternion.Euler(x, y, z);
        }

        private static GameObject InstantiateSource(Scene scene)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(SourceFbx);
            if (source == null)
            {
                throw new FileNotFoundException("Replacement arm source FBX was not imported.", SourceFbx);
            }
            GameObject root = UnityEngine.Object.Instantiate(source);
            root.name = RootName;
            SceneManager.MoveGameObjectToScene(root, scene);
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true).ToArray())
            {
                if (transform == root.transform)
                {
                    continue;
                }
                if (transform.GetComponent<Camera>() != null ||
                    transform.GetComponent<Light>() != null ||
                    string.Equals(transform.name, "Cube", StringComparison.OrdinalIgnoreCase))
                {
                    UnityEngine.Object.DestroyImmediate(transform.gameObject);
                }
            }
            ApplyRuntimeMaterial(root);
            return root;
        }

        private static void ConfigureSourceImport()
        {
            ModelImporter importer = AssetImporter.GetAtPath(SourceFbx) as ModelImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(SourceFbx, ImportAssetOptions.ForceSynchronousImport);
                importer = AssetImporter.GetAtPath(SourceFbx) as ModelImporter;
            }
            if (importer == null)
            {
                throw new InvalidOperationException("Unity did not create a ModelImporter for " + SourceFbx);
            }
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();

            TextureImporter normalImporter = AssetImporter.GetAtPath(
                "Assets/SoulRecorderReplacementArms/Source/armnormal.png") as TextureImporter;
            if (normalImporter != null && normalImporter.textureType != TextureImporterType.NormalMap)
            {
                normalImporter.textureType = TextureImporterType.NormalMap;
                normalImporter.SaveAndReimport();
            }
        }

        private static void ApplyRuntimeMaterial(GameObject root)
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null)
            {
                throw new InvalidOperationException("Unity Standard shader is unavailable.");
            }
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                Directory.CreateDirectory(Path.GetFullPath(
                    "Assets/SoulRecorderReplacementArms/Materials"));
                material = new Material(shader)
                {
                    name = "DJMaesenFirstPersonArms_RuntimeEquivalent"
                };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.shader = shader;
            material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/SoulRecorderReplacementArms/Source/armColor.png"));
            Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/SoulRecorderReplacementArms/Source/armnormal.png");
            if (normal != null)
            {
                material.SetTexture("_BumpMap", normal);
                material.EnableKeyword("_NORMALMAP");
            }
            material.SetTexture("_OcclusionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/SoulRecorderReplacementArms/Source/armAO.png"));
            material.SetFloat("_Glossiness", 0.28f);
            EditorUtility.SetDirty(material);
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                renderer.sharedMaterial = material;
            }
        }

        private static Camera CreateFpsCamera(Rig rig, Scene scene)
        {
            Vector3 shoulder = (rig.Bones["L_arm"].position + rig.Bones["R_arm"].position) * 0.5f;
            Vector3 wrists = (rig.Bones["L_wrist"].position + rig.Bones["R_wrist"].position) * 0.5f;
            Vector3 forward = (wrists - shoulder).normalized;
            if (forward.sqrMagnitude < 0.5f)
            {
                forward = Vector3.forward;
            }
            GameObject go = new GameObject(CameraName);
            SceneManager.MoveGameObjectToScene(go, scene);
            Camera camera = go.AddComponent<Camera>();
            camera.fieldOfView = 70f;
            camera.nearClipPlane = 0.01f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.04f, 0.05f);
            // The camera sits between shoulder cutoffs and wrists, as an FPS
            // camera would.  This keeps the open sleeve ends behind the view.
            go.transform.position = shoulder + forward * 0.12f + Vector3.up * 0.025f;
            go.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            return camera;
        }

        private static Camera CreateExternalCamera(Rig rig, Scene scene)
        {
            Bounds bounds = rig.Renderer.bounds;
            GameObject go = new GameObject("Rig Articulation Proof Camera");
            SceneManager.MoveGameObjectToScene(go, scene);
            Camera camera = go.AddComponent<Camera>();
            camera.fieldOfView = 70f;
            camera.nearClipPlane = 0.01f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.04f, 0.05f);
            Vector3 direction = new Vector3(0.55f, 0.35f, -1f).normalized;
            float distance = Mathf.Max(0.48f, bounds.extents.magnitude /
                Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f) * 0.90f);
            go.transform.position = bounds.center - direction * distance;
            go.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            return camera;
        }

        private static void CreateLighting(Scene scene)
        {
            GameObject key = new GameObject("Validation Key Light");
            SceneManager.MoveGameObjectToScene(key, scene);
            Light light = key.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            key.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.46f, 0.49f, 0.55f);
        }

        private static void Render(Camera camera, string assetPath)
        {
            RenderTexture target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            Texture2D image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                target.Create();
                camera.targetTexture = target;
                RenderTexture.active = target;
                camera.Render();
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                image.Apply();
                string fullPath = Path.GetFullPath(assetPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                File.WriteAllBytes(fullPath, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(image);
            }
        }

        private static PropPlacement CaptureApprovedPlacement(string gripName)
        {
            const string oldScene = "Assets/SoulRecorderAuthoring/SoulRecorderManualGrip.unity";
            if (!File.Exists(Path.GetFullPath(oldScene)))
            {
                return new PropPlacement();
            }
            Scene scene = EditorSceneManager.OpenScene(oldScene, OpenSceneMode.Additive);
            try
            {
                Camera camera = FindInScene(scene, "SoulRecorder Preview Camera")?.GetComponent<Camera>();
                Transform grip = FindInScene(scene, gripName);
                if (camera == null || grip == null)
                {
                    return new PropPlacement();
                }
                // Preserve the authored prefab-root offset, not a deeply nested
                // mesh renderer's local transform.
                Transform model = grip.Cast<Transform>()
                    .FirstOrDefault(child =>
                        child.GetComponentsInChildren<Renderer>(true).Length > 0);
                return new PropPlacement
                {
                    Found = true,
                    GripCameraPosition = camera.transform.InverseTransformPoint(grip.position),
                    GripCameraRotation = Quaternion.Inverse(camera.transform.rotation) * grip.rotation,
                    ModelLocalPosition = model == null ? Vector3.zero : model.localPosition,
                    ModelLocalRotation = model == null ? Quaternion.identity : model.localRotation
                };
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static Transform FindInScene(Scene scene, string name)
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(transform => transform.name == name);
        }

        private static Transform NewAnchor(string name, Transform parent)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static void ApplyPropPlacement(Camera camera, Transform grip,
            PropPlacement placement, Vector3 fallbackCameraPosition)
        {
            Vector3 localPosition = placement.Found ? placement.GripCameraPosition : fallbackCameraPosition;
            Quaternion localRotation = placement.Found ? placement.GripCameraRotation : Quaternion.identity;
            grip.position = camera.transform.TransformPoint(localPosition);
            grip.rotation = camera.transform.rotation * localRotation;
        }

        private static void InstantiateProp(string path, string name,
            Transform grip, PropPlacement placement)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                throw new FileNotFoundException("Authored prop prefab was not found.", path);
            }
            GameObject instance = UnityEngine.Object.Instantiate(prefab, grip);
            instance.name = name;
            instance.transform.localPosition = placement.Found ?
                placement.ModelLocalPosition : Vector3.zero;
            instance.transform.localRotation = placement.Found ?
                placement.ModelLocalRotation : Quaternion.identity;
        }

        private static void WriteReport(Rig rig, bool passed)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("DJMaesen First Person arms - Unity prop-free rig validation");
            text.AppendLine("NEW ARM RIG VALIDATION: " + (passed ? "PASS" : "FAIL"));
            text.AppendLine("Props attached: NO");
            text.AppendLine("Renderer: " + (rig.Renderer == null ? "UNRESOLVED" : rig.Renderer.name));
            text.AppendLine("Left: L_arm -> L_elbow -> L_wrist");
            text.AppendLine("Right: R_arm -> R_elbow -> R_wrist");
            text.AppendLine("Finger chains: thumb / point(index) / middle / ring / pink, joints 1-3");
            foreach (string failure in rig.Failures)
            {
                text.AppendLine("FAIL: " + failure);
            }
            string fullPath = Path.GetFullPath(ValidationReport);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, text.ToString());
        }

        private static void EnsureFolders()
        {
            foreach (string folder in new[]
            {
                "Assets/SoulRecorderReplacementArms/Scenes",
                "Assets/SoulRecorderReplacementArms/Validation",
                "Assets/SoulRecorderReplacementArms/Authoring",
                "Assets/SoulRecorderReplacementArms/Materials"
            })
            {
                Directory.CreateDirectory(Path.GetFullPath(folder));
            }
            AssetDatabase.Refresh();
        }

        private static Rig Failed(string failure)
        {
            Rig rig = new Rig();
            rig.Failures.Add(failure);
            return rig;
        }
    }
}
