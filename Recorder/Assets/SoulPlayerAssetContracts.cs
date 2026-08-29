using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using SoulPlayer.Library;

namespace SoulPlayer.Recorder.Assets
{
    internal static class SoulPlayerAssetContract
    {
        internal const string ManifestResourceName =
            "SoulPlayer.Assets.SoulRecorder.source-manifest.json";
        internal const string BundleFileName = "soulplayer_assets.bundle";
        internal const string RequiredUnityVersion = "2022.3.43f1";
        internal const string RecorderAssetName = "soulrecorder_fp";
        internal const string CassetteAssetName = "soultape_cassette";
        // Retained only as a rejection sentinel. The old DevMops three-bone
        // prefab is never loaded or instantiated by the runtime.
        internal const string RejectedLegacyHandsAssetName = "soulrecorder_hands";
        internal const string AnimatedHandsAssetName = "soulrecorder_animated_hands";
        internal const string AnimatedHandsShaderName =
            "SoulPlayer/Recorder Hands PBR";

        internal static readonly string[] RecorderRequiredTransforms =
        {
            "SoulRecorderModel",
            "CassetteInsertionStart",
            "CassetteAlignment",
            "CassetteSlot",
            "RecorderSupportPalm",
            "RecorderSupportThumb",
            "RecorderSupportIndex",
            "RecorderSupportMiddle",
            "RecorderSupportRing",
            "RecorderSupportPinky",
            "CassetteSlotEntry",
            "CassetteSlotSeated",
            "CassetteSlotTravelAxis",
            "CassetteEject",
            "CassetteWindow",
            "StatusLed",
            "ReelWindowLeft",
            "ReelWindowRight"
        };

        internal static readonly string[] CassetteRequiredTransforms =
        {
            "SoulTapeCassette",
            "Shell",
            "Label",
            "ReelLeft",
            "ReelRight",
            "CassetteThumbGrip",
            "CassetteIndexGrip",
            "CassetteMiddleGrip",
            "CassetteFront",
            "CassetteTop",
            "CassetteInsertionAxis"
        };

        internal static readonly string[] HandsRequiredTransforms =
        {
            "SoulRecorderAnimatedHandsModel",
            "SoulRecorderHandsRig",
            "SoulRecorderHandsRoot",
            "SoulRecorderArms",
            "Arm_1.L", "Arm_2.L", "Hand_1.L", "Hand_2.L",
            "Arm_1.R", "Arm_2.R", "Hand_1.R", "Hand_2.R",
            "Finger_1_1.L", "Finger_2_3.L",
            "Finger_1_1.R", "Finger_2_3.R",
            "RecorderGrip", "CassetteGrip", "CassetteContact",
            "SupportSleeveCutoff", "CassetteSleeveCutoff"
        };

        internal static readonly string[] RequiredHandsAnimationClips =
        {
            "SoulRecorder_Enter",
            "SoulRecorder_Insert",
            "SoulRecorder_StartExit",
            "SoulRecorder_Hold",
            "SoulRecorder_StopEnter",
            "SoulRecorder_Eject",
            "SoulRecorder_StopExit",
            "SoulRecorder_CancelInsert"
        };

        internal static IReadOnlyList<string> GetMissingTransforms(
            IEnumerable<string> availableNames,
            IEnumerable<string> requiredNames)
        {
            Dictionary<string, int> available = (availableNames ?? Enumerable.Empty<string>())
                .GroupBy(name => name ?? string.Empty, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            return (requiredNames ?? Enumerable.Empty<string>())
                .Where(required => !available.ContainsKey(required) || available[required] != 1)
                .ToArray();
        }

        internal static string GetBundlePath(string assemblyLocation)
        {
            if (string.IsNullOrWhiteSpace(assemblyLocation))
            {
                return BundleFileName;
            }

            string normalized = SoulPath.NormalizeConfiguredPath(
                assemblyLocation,
                AppDomain.CurrentDomain.BaseDirectory);
            string folder = Path.GetDirectoryName(normalized);
            return Path.Combine(folder ?? string.Empty, BundleFileName);
        }
    }

    internal sealed class SoulPlayerAssetSourceManifest
    {
        public int SchemaVersion { get; set; }
        public string RequiredUnityVersion { get; set; }
        public string BundleFileName { get; set; }
        public string BuildTarget { get; set; }
        public string TexturePolicy { get; set; }
        public List<SoulPlayerAssetSourceEntry> Assets { get; set; }

        internal static SoulPlayerAssetSourceManifest Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidDataException("SoulPlayer asset provenance manifest is empty.");
            }

            SoulPlayerAssetSourceManifest manifest =
                JsonConvert.DeserializeObject<SoulPlayerAssetSourceManifest>(json);
            if (manifest == null || manifest.SchemaVersion != 1 || manifest.Assets == null)
            {
                throw new InvalidDataException("SoulPlayer asset provenance manifest is invalid.");
            }

            return manifest;
        }

        internal static SoulPlayerAssetSourceManifest LoadEmbedded(Assembly assembly)
        {
            Assembly source = assembly ?? typeof(SoulPlayerAssetSourceManifest).Assembly;
            using (Stream stream = source.GetManifestResourceStream(
                SoulPlayerAssetContract.ManifestResourceName))
            {
                if (stream == null)
                {
                    throw new InvalidDataException(
                        "Embedded SoulPlayer asset provenance manifest was not found.");
                }

                using (StreamReader reader = new StreamReader(stream))
                {
                    return Parse(reader.ReadToEnd());
                }
            }
        }
    }

    internal sealed class SoulPlayerAssetSourceEntry
    {
        public string LogicalName { get; set; }
        public string UpstreamSource { get; set; }
        public string Author { get; set; }
        public string License { get; set; }
        public string LicenseRecordPath { get; set; }
        public string ExternalSourcePath { get; set; }
        public string SourceArchiveSha256 { get; set; }
        public string DerivedOutputName { get; set; }
        public bool Redistributed { get; set; }
        public List<string> Transformations { get; set; }
        public SoulPlayerAssetInspection Inspection { get; set; }
    }

    internal sealed class SoulPlayerAssetInspection
    {
        public string BlendVersion { get; set; }
        public List<string> ObjectNamesObserved { get; set; }
        public List<string> MaterialNamesObserved { get; set; }
        public int TextureCount { get; set; }
        public string TextureDimensions { get; set; }
        public string GeometryAuditStatus { get; set; }
    }
}
