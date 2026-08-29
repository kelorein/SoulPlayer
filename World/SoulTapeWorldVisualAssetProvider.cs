using System;
using System.IO;
using System.Linq;
using System.Reflection;
using SoulPlayer.Cassettes;
using UnityEngine;

namespace SoulPlayer.World
{
    internal static class SoulTapeWorldVisualTuning
    {
        internal const string VisualChildName = "CassetteVisual";
        internal const string BundleFileName = "soultape_world.bundle";
        internal const string AssetName = "soultape_cassette";

        internal static readonly Vector3 CassetteVisualLocalPosition = Vector3.zero;
        internal static readonly Vector3 CassetteVisualLocalRotation = Vector3.zero;
        internal static readonly Vector3 CassetteVisualLocalScale = Vector3.one;

        internal static void Apply(Transform visual)
        {
            if (visual == null)
            {
                return;
            }

            visual.localPosition = CassetteVisualLocalPosition;
            visual.localRotation = Quaternion.Euler(CassetteVisualLocalRotation);
            visual.localScale = CassetteVisualLocalScale;
        }
    }

    internal sealed class SoulTapeWorldVisualAssetProvider : IDisposable
    {
        private readonly ISoulTapeLog _log;
        private AssetBundle _bundle;
        private GameObject _cassettePrefab;
        private bool _loadAttempted;

        internal SoulTapeWorldVisualAssetProvider(ISoulTapeLog log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        internal bool IsLoaded
        {
            get { return _bundle != null && _cassettePrefab != null; }
        }

        internal string BundlePath { get; private set; } = string.Empty;

        internal bool Prewarm()
        {
            if (_loadAttempted)
            {
                return IsLoaded;
            }

            _loadAttempted = true;
            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            string folder = Path.GetDirectoryName(assemblyPath) ?? string.Empty;
            BundlePath = Path.Combine(
                folder,
                SoulTapeWorldVisualTuning.BundleFileName);
            if (!File.Exists(BundlePath))
            {
                _log.Warning(
                    "SoulTape world cassette bundle was not found at '" +
                    BundlePath + "'; using the collider-free procedural fallback.");
                return false;
            }

            try
            {
                _bundle = AssetBundle.LoadFromFile(BundlePath);
                _cassettePrefab = _bundle == null
                    ? null
                    : _bundle.LoadAsset<GameObject>(
                        SoulTapeWorldVisualTuning.AssetName);
                string failure;
                if (!ValidatePrefab(_cassettePrefab, out failure))
                {
                    Dispose();
                    _log.Error(
                        "SoulTape world cassette bundle failed its visual-only " +
                        "contract; using the procedural fallback: " + failure);
                    return false;
                }

                _log.Info(
                    "SoulTape world cassette visual loaded from the CC0 " +
                    "soultape_cassette asset at '" + BundlePath + "'.");
                return true;
            }
            catch (Exception ex)
            {
                Dispose();
                _log.Error(
                    "SoulTape world cassette bundle could not be loaded; using " +
                    "the procedural fallback: " + ex.GetBaseException().Message);
                return false;
            }
        }

        internal bool TryCreate(Transform parent, out GameObject visual)
        {
            visual = null;
            if (parent == null || !IsLoaded)
            {
                return false;
            }

            try
            {
                visual = UnityEngine.Object.Instantiate(_cassettePrefab);
                visual.name = SoulTapeWorldVisualTuning.VisualChildName;
                visual.transform.SetParent(parent, false);
                SoulTapeWorldVisualTuning.Apply(visual.transform);
                SetLayerRecursively(visual, parent.gameObject.layer);
                RemoveColliders(visual);
                return true;
            }
            catch (Exception ex)
            {
                if (visual != null)
                {
                    UnityEngine.Object.Destroy(visual);
                    visual = null;
                }
                _log.Error(
                    "SoulTape world cassette visual could not be instantiated; " +
                    "using the procedural fallback: " +
                    ex.GetBaseException().Message);
                return false;
            }
        }

        internal static bool ValidatePrefab(GameObject prefab, out string failure)
        {
            failure = string.Empty;
            if (prefab == null)
            {
                failure = "prefab was missing";
                return false;
            }

            string[] required = { "SoulTapeCassette", "Shell", "Label", "ReelLeft", "ReelRight" };
            Transform[] transforms = prefab.GetComponentsInChildren<Transform>(true);
            foreach (string name in required)
            {
                if (transforms.Count(item => string.Equals(
                        item.name,
                        name,
                        StringComparison.Ordinal)) != 1)
                {
                    failure = "required visual transform was missing or duplicated: " + name;
                    return false;
                }
            }

            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0 || renderers.All(renderer =>
                    renderer == null || renderer.sharedMaterial == null ||
                    renderer.sharedMaterial.shader == null))
            {
                failure = "prefab had no usable renderer/material";
                return false;
            }

            if (prefab.GetComponentsInChildren<Collider>(true).Length != 0)
            {
                failure = "visual prefab contained a collider";
                return false;
            }

            return true;
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.layer = layer;
            }
        }

        private static void RemoveColliders(GameObject root)
        {
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
                UnityEngine.Object.Destroy(collider);
            }
        }

        public void Dispose()
        {
            _cassettePrefab = null;
            if (_bundle != null)
            {
                _bundle.Unload(false);
                _bundle = null;
            }
        }
    }
}
