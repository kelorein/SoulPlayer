using System;
using System.Collections.Generic;
using System.Reflection;
using SoulPlayer.Cassettes;
using UnityEngine;

namespace SoulPlayer.Recorder.Assets
{
    internal static class SoulRecorderAttachmentTransfer
    {
        internal static void ReparentPreservingWorldPose(Transform child, Transform parent)
        {
            if (child == null || parent == null || child == parent)
            {
                return;
            }
            Vector3 worldPosition = child.position;
            Quaternion worldRotation = child.rotation;
            Vector3 worldScale = child.lossyScale;
            child.SetParent(parent, true);
            child.position = worldPosition;
            child.rotation = worldRotation;
            Vector3 parentScale = parent.lossyScale;
            child.localScale = new Vector3(
                SafeScale(worldScale.x, parentScale.x),
                SafeScale(worldScale.y, parentScale.y),
                SafeScale(worldScale.z, parentScale.z));
        }

        private static float SafeScale(float world, float parent)
        {
            return Mathf.Abs(parent) <= 0.000001f ? 1f : world / parent;
        }
    }

    internal sealed class SoulRecorderPackagedVisual
    {
        internal GameObject Root { get; set; }
        internal GameObject AnimatedHandsRoot { get; set; }
        internal Animator HandsAnimator { get; set; }
        internal Transform RecorderGrip { get; set; }
        internal Transform CassetteGrip { get; set; }
        internal Transform CassetteContact { get; set; }
        internal Transform CassetteSlot { get; set; }
        internal bool HasAnimatedHands { get; set; }
        internal GameObject CassetteRoot { get; set; }
        internal Transform ReelLeft { get; set; }
        internal Transform ReelRight { get; set; }
        internal Renderer StatusLedRenderer { get; set; }
        internal Renderer[] RecorderModelRenderers { get; set; }
        internal Renderer[] CassetteModelRenderers { get; set; }
        internal Vector3 CassetteStartPosition { get; set; }
        internal Quaternion CassetteStartRotation { get; set; }
        internal Vector3 CassetteAlignmentPosition { get; set; }
        internal Quaternion CassetteAlignmentRotation { get; set; }
        internal Vector3 CassetteEndPosition { get; set; }
        internal Quaternion CassetteEndRotation { get; set; }
        internal Vector3 CassetteEjectPosition { get; set; }
        internal Quaternion CassetteEjectRotation { get; set; }
    }

    internal sealed class SoulRecorderAssetProvider : IDisposable
    {
        private readonly SoulPlayerAssetBundle _bundle;
        private readonly ISoulTapeLog _log;
        private bool _instanceFailureLogged;
        private bool _animatedHandsFailureLogged;

        internal SoulRecorderAssetProvider(ISoulTapeLog log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _bundle = new SoulPlayerAssetBundle(
                new UnitySoulPlayerAssetBundleBackend(),
                log);
        }

        internal bool IsLoaded { get { return _bundle.IsLoaded; } }

        internal bool Prewarm()
        {
            string location = Assembly.GetExecutingAssembly().Location;
            return _bundle.TryLoad(SoulPlayerAssetContract.GetBundlePath(location));
        }

        internal bool TryCreateRecorderVisual(out SoulRecorderPackagedVisual visual)
        {
            return TryCreateRecorderVisual(out visual, true);
        }

        internal bool TryCreateRecorderVisual(
            out SoulRecorderPackagedVisual visual,
            bool includePackagedAnimatedHands)
        {
            visual = null;
            if (!_bundle.IsLoaded)
            {
                return false;
            }

            GameObject recorder = _bundle.InstantiateRecorder() as GameObject;
            GameObject cassette = _bundle.InstantiateCassette() as GameObject;
            GameObject hands = includePackagedAnimatedHands
                ? _bundle.InstantiateAnimatedHands() as GameObject
                : null;
            if (recorder == null || cassette == null)
            {
                DestroyOwned(recorder, cassette, hands);
                LogInstanceFailure("a prefab could not be instantiated");
                return false;
            }

            try
            {
                Transform start = FindUnique(recorder.transform, "CassetteInsertionStart");
                Transform alignment = FindUnique(recorder.transform, "CassetteAlignment");
                Transform slot = FindUnique(recorder.transform, "CassetteSlot");
                Transform eject = FindUnique(recorder.transform, "CassetteEject");
                Transform led = FindUnique(recorder.transform, "StatusLed");
                Transform model = FindUnique(recorder.transform, "SoulRecorderModel");
                Transform cassetteModel = FindUnique(cassette.transform, "SoulTapeCassette");
                Transform left = FindUnique(cassette.transform, "ReelLeft");
                Transform right = FindUnique(cassette.transform, "ReelRight");
                if (start == null || alignment == null || slot == null || eject == null ||
                    led == null || model == null ||
                    cassetteModel == null || left == null || right == null ||
                    start.parent != slot.parent || alignment.parent != slot.parent ||
                    eject.parent != slot.parent)
                {
                    throw new InvalidOperationException(
                        "required instance transforms were missing or cassette markers did not share a parent");
                }

                Renderer statusLedRenderer = led.GetComponent<Renderer>();
                if (statusLedRenderer == null)
                {
                    throw new InvalidOperationException(
                        "StatusLed did not have a usable Renderer");
                }

                SetLayerRecursively(recorder, 0);
                SetLayerRecursively(cassette, 0);
                Renderer[] modelRenderers = model.GetComponentsInChildren<Renderer>(true);
                if (modelRenderers.Length == 0)
                {
                    throw new InvalidOperationException(
                        "SoulRecorderModel did not contain any Renderer geometry");
                }

                int usableRendererCount = 0;
                foreach (Renderer renderer in modelRenderers)
                {
                    if (IsUsableVisibleRenderer(renderer))
                    {
                        usableRendererCount++;
                    }
                }
                if (usableRendererCount == 0)
                {
                    throw new InvalidOperationException(
                        "SoulRecorderModel did not contain an enabled, active, non-zero " +
                        "Renderer with a valid shader");
                }
                if (!HaveExpectedRecorderTextures(modelRenderers))
                {
                    throw new InvalidOperationException(
                        "SoulRecorderModel did not retain its explicit textured body/flap materials");
                }

                Renderer[] cassetteRenderers =
                    cassetteModel.GetComponentsInChildren<Renderer>(true);
                int usableCassetteRendererCount = 0;
                foreach (Renderer renderer in cassetteRenderers)
                {
                    if (IsUsableVisibleRenderer(renderer))
                    {
                        usableCassetteRendererCount++;
                    }
                }
                if (cassetteRenderers.Length == 0 || usableCassetteRendererCount == 0)
                {
                    throw new InvalidOperationException(
                        "SoulTapeCassette did not contain enabled, active, non-zero " +
                        "Renderer geometry with a valid shader");
                }

                cassette.transform.SetParent(slot.parent, false);
                cassette.transform.localPosition = start.localPosition;
                cassette.transform.localRotation = start.localRotation;
                DisableAndRemoveColliders(recorder);
                DisableAndRemoveColliders(cassette);

                Animator handsAnimator = null;
                Transform recorderGrip = null;
                Transform cassetteGrip = null;
                Transform cassetteContact = null;
                bool hasAnimatedHands = includePackagedAnimatedHands &&
                    TryValidateAnimatedHands(
                        hands,
                        out handsAnimator,
                        out recorderGrip,
                        out cassetteGrip,
                        out cassetteContact);
                if (includePackagedAnimatedHands && !hasAnimatedHands)
                {
                    DestroyOwned(hands);
                    hands = null;
                    LogAnimatedHandsFailureOnce(
                        "the prefab, full rig, sockets, materials, or Animator clip contract was invalid");
                }

                visual = new SoulRecorderPackagedVisual
                {
                    Root = recorder,
                    AnimatedHandsRoot = hands,
                    HandsAnimator = handsAnimator,
                    RecorderGrip = recorderGrip,
                    CassetteGrip = cassetteGrip,
                    CassetteContact = cassetteContact,
                    CassetteSlot = slot,
                    HasAnimatedHands = hasAnimatedHands,
                    CassetteRoot = cassette,
                    ReelLeft = left,
                    ReelRight = right,
                    StatusLedRenderer = statusLedRenderer,
                    RecorderModelRenderers = modelRenderers,
                    CassetteModelRenderers = cassetteRenderers,
                    CassetteStartPosition = start.localPosition,
                    CassetteStartRotation = start.localRotation,
                    CassetteAlignmentPosition = alignment.localPosition,
                    CassetteAlignmentRotation = alignment.localRotation,
                    CassetteEndPosition = slot.localPosition,
                    CassetteEndRotation = slot.localRotation,
                    CassetteEjectPosition = eject.localPosition,
                    CassetteEjectRotation = eject.localRotation
                };
                return true;
            }
            catch (Exception ex)
            {
                DestroyOwned(recorder, cassette, hands);
                LogInstanceFailure(ex.Message);
                return false;
            }
        }

        private static Transform FindUnique(Transform root, string name)
        {
            Transform found = null;
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            {
                if (!string.Equals(candidate.name, name, StringComparison.Ordinal))
                {
                    continue;
                }

                if (found != null)
                {
                    return null;
                }
                found = candidate;
            }
            return found;
        }

        private static bool TryValidateAnimatedHands(
            GameObject hands,
            out Animator animator,
            out Transform recorderGrip,
            out Transform cassetteGrip,
            out Transform cassetteContact)
        {
            animator = null;
            recorderGrip = null;
            cassetteGrip = null;
            cassetteContact = null;
            if (hands == null ||
                FindUnique(hands.transform,
                    SoulPlayerAssetContract.RejectedLegacyHandsAssetName) != null)
            {
                return false;
            }

            Transform commonRoot = FindUnique(hands.transform, "SoulRecorderHandsRoot");
            recorderGrip = FindUnique(hands.transform, "RecorderGrip");
            cassetteGrip = FindUnique(hands.transform, "CassetteGrip");
            cassetteContact = FindUnique(hands.transform, "CassetteContact");
            SkinnedMeshRenderer[] renderers =
                hands.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (commonRoot == null || recorderGrip == null || cassetteGrip == null ||
                cassetteContact == null || renderers.Length != 1 ||
                recorderGrip.parent == null || recorderGrip.parent.name != "Hand_2.L" ||
                cassetteGrip.parent == null || cassetteGrip.parent.name != "Hand_1.R" ||
                cassetteContact.parent == null ||
                cassetteContact.parent.name != "Finger_2_2.R")
            {
                return false;
            }

            SkinnedMeshRenderer renderer = renderers[0];
            int bindPoseCount = renderer.sharedMesh == null
                ? 0
                : renderer.sharedMesh.bindposes.Length;
            if (!IsPackagedHandRigSafe(
                    renderer.rootBone == null ? null : renderer.rootBone.name,
                    renderer.bones.Length,
                    bindPoseCount) ||
                !ArePackagedHandBoundsSafe(renderer.bounds.size))
            {
                return false;
            }
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null || material.shader == null ||
                    !string.Equals(
                        material.shader.name,
                        SoulPlayerAssetContract.AnimatedHandsShaderName,
                        StringComparison.Ordinal) ||
                    material.GetTexture("_MainTex") == null ||
                    material.GetTexture("_BumpMap") == null ||
                    material.GetTexture("_MetallicGlossMap") == null)
                {
                    return false;
                }
            }

            animator = hands.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                return false;
            }
            HashSet<string> clips = new HashSet<string>(StringComparer.Ordinal);
            foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
            {
                if (clip == null || clip.length <= 0f)
                {
                    return false;
                }
                clips.Add(clip.name);
            }
            foreach (string required in SoulPlayerAssetContract.RequiredHandsAnimationClips)
            {
                if (!clips.Contains(required))
                {
                    return false;
                }
            }
            SetLayerRecursively(hands, 0);
            DisableAndRemoveColliders(hands);
            return true;
        }

        private static void DisableAndRemoveColliders(GameObject root)
        {
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
                UnityEngine.Object.Destroy(collider);
            }
        }

        private static bool IsUsableVisibleRenderer(Renderer renderer)
        {
            return renderer != null && renderer.enabled &&
                   renderer.gameObject.activeInHierarchy &&
                   renderer.sharedMaterial != null &&
                   renderer.sharedMaterial.shader != null &&
                   renderer.bounds.size.sqrMagnitude > 0.00000001f;
        }

        private static bool HaveExpectedRecorderTextures(Renderer[] renderers)
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
                        return false;
                    }
                    names.Add(material.name.Replace(" (Instance)", string.Empty));
                }
            }
            return names.Contains("SoulPlayerRecorderBody") &&
                   names.Contains("SoulPlayerRecorderFlap");
        }

        private static bool HasMainTexture(Renderer[] renderers)
        {
            foreach (Renderer renderer in renderers)
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material != null && material.GetTexture("_MainTex") != null)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        internal static bool IsPackagedHandRigSafe(
            string rootBoneName,
            int boneCount,
            int bindPoseCount)
        {
            return string.Equals(
                       rootBoneName,
                       "SoulRecorderHandsRoot",
                       StringComparison.Ordinal) &&
                   boneCount >= 40 && bindPoseCount == boneCount;
        }

        internal static bool ArePackagedHandBoundsSafe(Vector3 size)
        {
            return IsFinitePositive(size.x) && IsFinitePositive(size.y) &&
                   IsFinitePositive(size.z) &&
                   size.x <= 1.30f && size.y <= 1.30f && size.z <= 1.30f;
        }

        private static bool IsFinitePositive(float value)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static string FormatVector(Vector3 value)
        {
            return string.Format(
                "({0:F4}, {1:F4}, {2:F4})",
                value.x,
                value.y,
                value.z);
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                transform.gameObject.layer = layer;
            }
        }

        private static void DestroyOwned(params GameObject[] objects)
        {
            foreach (GameObject value in objects)
            {
                if (value != null)
                {
                    UnityEngine.Object.Destroy(value);
                }
            }
        }

        private void LogInstanceFailure(string detail)
        {
            if (_instanceFailureLogged)
            {
                return;
            }

            _instanceFailureLogged = true;
            _log.Error(
                "SoulRecorder packaged prefab instance failed; using the procedural " +
                "presentation: " + detail);
        }

        private void LogAnimatedHandsFailureOnce(string detail)
        {
            if (_animatedHandsFailureLogged)
            {
                return;
            }
            _animatedHandsFailureLogged = true;
            _log.Warning(
                "SoulRecorder BAMEN animated hands unavailable; using packaged " +
                "recorder/cassette only: " + detail);
        }

        public void Dispose()
        {
            _bundle.Dispose();
        }
    }
}
