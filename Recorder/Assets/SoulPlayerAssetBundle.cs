using System;
using System.Collections.Generic;
using System.IO;
using SoulPlayer.Cassettes;
using UnityEngine;

namespace SoulPlayer.Recorder.Assets
{
    internal interface ISoulPlayerAssetBundleBackend
    {
        bool FileExists(string path);
        bool TryLoadBundle(string path, out object bundle, out string error);
        object LoadPrefab(object bundle, string logicalName);
        IReadOnlyList<string> GetTransformNames(object prefab);
        bool HasRenderer(object prefab, string transformName);
        object Instantiate(object prefab);
        void Unload(object bundle, bool unloadAllLoadedObjects);
    }

    internal sealed class SoulPlayerAssetBundle : IDisposable
    {
        private readonly ISoulPlayerAssetBundleBackend _backend;
        private readonly ISoulTapeLog _log;
        private object _bundle;
        private object _recorderPrefab;
        private object _cassettePrefab;
        private object _animatedHandsPrefab;
        private bool _loadAttempted;

        internal SoulPlayerAssetBundle(
            ISoulPlayerAssetBundleBackend backend,
            ISoulTapeLog log)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        internal bool IsLoaded { get { return _bundle != null; } }
        internal string BundlePath { get; private set; } = string.Empty;

        internal bool TryLoad(string path)
        {
            if (_loadAttempted)
            {
                return IsLoaded;
            }

            _loadAttempted = true;
            BundlePath = path ?? string.Empty;
            if (!_backend.FileExists(BundlePath))
            {
                _log.Warning(
                    "SoulRecorder asset bundle was not found at '" + BundlePath +
                    "'; using the procedural recorder presentation.");
                return false;
            }

            string error;
            if (!_backend.TryLoadBundle(BundlePath, out _bundle, out error) || _bundle == null)
            {
                _bundle = null;
                _log.Error(
                    "SoulRecorder asset bundle could not be loaded; using the procedural " +
                    "presentation: " + (error ?? "unknown bundle error"));
                return false;
            }

            _recorderPrefab = _backend.LoadPrefab(
                _bundle,
                SoulPlayerAssetContract.RecorderAssetName);
            _cassettePrefab = _backend.LoadPrefab(
                _bundle,
                SoulPlayerAssetContract.CassetteAssetName);
            object handsPrefab = _backend.LoadPrefab(
                _bundle,
                SoulPlayerAssetContract.AnimatedHandsAssetName);
            if (!ValidatePrefab(
                    _recorderPrefab,
                    SoulPlayerAssetContract.RecorderAssetName,
                    SoulPlayerAssetContract.RecorderRequiredTransforms,
                    "StatusLed") ||
                !ValidatePrefab(
                    _cassettePrefab,
                    SoulPlayerAssetContract.CassetteAssetName,
                    SoulPlayerAssetContract.CassetteRequiredTransforms))
            {
                Dispose();
                return false;
            }

            if (ValidatePrefab(
                    handsPrefab,
                    SoulPlayerAssetContract.AnimatedHandsAssetName,
                    SoulPlayerAssetContract.HandsRequiredTransforms,
                    null,
                    false))
            {
                _animatedHandsPrefab = handsPrefab;
            }
            else
            {
                _animatedHandsPrefab = null;
                _log.Warning(
                    "SoulRecorder BAMEN animated-hands prefab failed its basic contract; " +
                    "the packaged recorder-only fallback remains available.");
            }

            _log.Info(
                "SoulRecorder asset bundle loaded once from '" + BundlePath + "'.");
            return true;
        }

        internal object InstantiateRecorder()
        {
            return _recorderPrefab == null ? null : _backend.Instantiate(_recorderPrefab);
        }

        internal object InstantiateCassette()
        {
            return _cassettePrefab == null ? null : _backend.Instantiate(_cassettePrefab);
        }

        internal object InstantiateAnimatedHands()
        {
            return _animatedHandsPrefab == null
                ? null
                : _backend.Instantiate(_animatedHandsPrefab);
        }

        private bool ValidatePrefab(
            object prefab,
            string logicalName,
            IEnumerable<string> requiredTransforms,
            string requiredRendererTransform = null,
            bool logError = true)
        {
            if (prefab == null)
            {
                if (logError)
                {
                    _log.Error(
                        "SoulRecorder asset bundle is missing prefab '" + logicalName + "'.");
                }
                return false;
            }

            IReadOnlyList<string> missing = SoulPlayerAssetContract.GetMissingTransforms(
                _backend.GetTransformNames(prefab),
                requiredTransforms);
            if (missing.Count != 0)
            {
                if (logError)
                {
                    _log.Error(
                        "SoulRecorder prefab '" + logicalName +
                        "' failed its transform contract; missing or duplicated: " +
                        string.Join(", ", missing));
                }
                return false;
            }

            if (!string.IsNullOrEmpty(requiredRendererTransform) &&
                !_backend.HasRenderer(prefab, requiredRendererTransform))
            {
                if (logError)
                {
                    _log.Error(
                        "SoulRecorder prefab '" + logicalName + "' transform '" +
                        requiredRendererTransform + "' does not have a usable Renderer.");
                }
                return false;
            }

            return true;
        }

        public void Dispose()
        {
            if (_bundle != null)
            {
                _backend.Unload(_bundle, false);
            }

            _bundle = null;
            _recorderPrefab = null;
            _cassettePrefab = null;
            _animatedHandsPrefab = null;
        }
    }

    internal sealed class UnitySoulPlayerAssetBundleBackend : ISoulPlayerAssetBundleBackend
    {
        public bool FileExists(string path)
        {
            return File.Exists(path);
        }

        public bool TryLoadBundle(string path, out object bundle, out string error)
        {
            try
            {
                bundle = AssetBundle.LoadFromFile(path);
                error = bundle == null ? "Unity rejected the bundle or its format" : string.Empty;
                return bundle != null;
            }
            catch (Exception ex)
            {
                bundle = null;
                error = ex.Message;
                return false;
            }
        }

        public object LoadPrefab(object bundle, string logicalName)
        {
            AssetBundle unityBundle = bundle as AssetBundle;
            return unityBundle == null ? null : unityBundle.LoadAsset<GameObject>(logicalName);
        }

        public IReadOnlyList<string> GetTransformNames(object prefab)
        {
            GameObject root = prefab as GameObject;
            if (root == null)
            {
                return Array.Empty<string>();
            }

            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            string[] names = new string[transforms.Length];
            for (int index = 0; index < transforms.Length; index++)
            {
                names[index] = transforms[index].name;
            }
            return names;
        }

        public bool HasRenderer(object prefab, string transformName)
        {
            GameObject root = prefab as GameObject;
            if (root == null)
            {
                return false;
            }

            Transform found = null;
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            {
                if (!string.Equals(candidate.name, transformName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (found != null)
                {
                    return false;
                }
                found = candidate;
            }

            return found != null && found.GetComponent<Renderer>() != null;
        }

        public object Instantiate(object prefab)
        {
            GameObject source = prefab as GameObject;
            return source == null ? null : UnityEngine.Object.Instantiate(source);
        }

        public void Unload(object bundle, bool unloadAllLoadedObjects)
        {
            AssetBundle unityBundle = bundle as AssetBundle;
            if (unityBundle != null)
            {
                unityBundle.Unload(unloadAllLoadedObjects);
            }
        }
    }
}
