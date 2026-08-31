using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using SoulPlayer.World;
using UnityEngine;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    public sealed class SoulTapeWorldVisualSwapTests
    {
        private const string AuthoredAnchorSha256 =
            "48AFE778AC0091E63B181BF0B4200201761F43BA40B9EB2C0975965BB4AF1E42";

        [Fact]
        [Trait("Validation", "WorldCassetteVisual")]
        public void AuthoredAnchorIdsPositionsRotationsAndEnabledStateAreByteUnchanged()
        {
            string path = RepoPath("Data", "SoulTape", "SpawnAnchors", "all-maps.json");

            Assert.Equal(AuthoredAnchorSha256, Sha256(path));
        }

        [Fact]
        [Trait("Validation", "WorldCassetteVisual")]
        public void WorldRootStillReceivesTheAuthoredAnchorTransformDirectly()
        {
            string pickup = ReadSource("World", "SoulTapeWorldPickup.cs");

            Assert.Contains("root.transform.SetPositionAndRotation(", pickup);
            Assert.Contains("ToUnity(planned.Anchor.Position)", pickup);
            Assert.Contains("Quaternion.Euler(ToUnity(planned.Anchor.RotationEuler))", pickup);
            Assert.DoesNotContain("planned.Anchor.Position", ReadSource(
                "World", "SoulTapeWorldVisualAssetProvider.cs"));
        }

        [Fact]
        [Trait("Validation", "WorldCassetteVisual")]
        public void VisualChildUsesOneSharedIdentityLocalTransform()
        {
            Assert.Equal(Vector3.zero,
                SoulTapeWorldVisualTuning.CassetteVisualLocalPosition);
            Assert.Equal(Vector3.zero,
                SoulTapeWorldVisualTuning.CassetteVisualLocalRotation);
            Assert.Equal(Vector3.one,
                SoulTapeWorldVisualTuning.CassetteVisualLocalScale);
            Assert.Equal("CassetteVisual", SoulTapeWorldVisualTuning.VisualChildName);

            string provider = ReadSource(
                "World", "SoulTapeWorldVisualAssetProvider.cs");
            Assert.Contains("visual.transform.SetParent(parent, false)", provider);
            Assert.Contains("SoulTapeWorldVisualTuning.Apply(visual.transform)", provider);
        }

        [Fact]
        [Trait("Validation", "WorldCassetteVisual")]
        public void PickupAndDiscoveryComponentsRemainOnStableWorldRoot()
        {
            string pickup = ReadSource("World", "SoulTapeWorldPickup.cs");

            Assert.Contains("root.AddComponent<SoulTapeWorldPickup>()", pickup);
            Assert.Contains("pickup.Initialize(planned.Cassette)", pickup);
            Assert.Contains("pickup.CreateInteractionTarget(log)", pickup);
            Assert.Contains("root.transform", pickup);
            Assert.DoesNotContain("AddComponent<SoulTapeWorldPickup>", ReadSource(
                "World", "SoulTapeWorldVisualAssetProvider.cs"));
        }

        [Fact]
        [Trait("Validation", "WorldCassetteVisual")]
        public void RootInteractionColliderContractIsUnchanged()
        {
            Assert.Equal(new Vector3(0.28f, 0.14f, 0.20f),
                SoulTapeWorldPickup.InteractionTargetSize);
            string pickup = ReadSource("World", "SoulTapeWorldPickup.cs");

            Assert.Contains("SoulTape Authoritative Interaction Target", pickup);
            Assert.Contains("target.transform.SetParent(transform, false)", pickup);
            Assert.Contains("InteractionTarget = target.AddComponent<BoxCollider>()", pickup);
            Assert.Contains("InteractionTarget.size = InteractionTargetSize", pickup);
            Assert.Contains("InteractionTarget.isTrigger = true", pickup);
        }

        [Fact]
        [Trait("Validation", "WorldCassetteVisual")]
        public void VisualBundleAndRuntimeChildIntroduceNoCollider()
        {
            string provider = ReadSource(
                "World", "SoulTapeWorldVisualAssetProvider.cs");
            string builder = ReadSource(
                "Assets", "SoulRecorder", "UnityProject", "Assets", "Editor",
                "SoulRecorderAssetBundleBuilder.cs");

            Assert.Contains("prefab.GetComponentsInChildren<Collider>(true).Length != 0", provider);
            Assert.Contains("RemoveColliders(visual)", provider);
            Assert.Contains("World cassette visual contained a collider", builder);
            Assert.Contains("[PASS] soultape_world.bundle contains no colliders", builder);
        }

        [Fact]
        [Trait("Validation", "WorldCassetteVisual")]
        public void AssignmentCollectionPlanningAndTargetingRulesAreUnchanged()
        {
            Dictionary<string, string> expected = new Dictionary<string, string>
            {
                { "Cassettes/SoulTapeAssignment.cs", "F270618C95B9C2FC2270BB82D51C21301919C351843B95DC4A4577CAF0E7EF06" },
                { "Cassettes/SoulTapeAssignmentStore.cs", "3FAC9E094CB5D3990CD3FC9B682F92C05653B0C32327B68352EABAA68E61715B" },
                { "Cassettes/SoulTapeCollection.cs", "3DAAAEE62BF975FDF0C4BE2988173BA8E609B9F222196C1473322DCBAC03DC5F" },
                { "Cassettes/SoulTapeCollectionStore.cs", "B42772C3ECD099F077DC6F4EC4230EB550B1D8EA844F23A8373E99B238219B7D" },
                { "Cassettes/SoulTapeSpawnPlanner.cs", "48256B207C10FDB8B72CC22AAB765FD98B1411C5678B8D1D2242B18FB7DF0B2C" },
                { "World/SoulTapeInteractionTargeting.cs", "89073C6F2804F701023E26B7D04F8B862D6EFB0DCED1822FA529AC07962B4C7F" }
            };

            foreach (KeyValuePair<string, string> item in expected)
            {
                Assert.Equal(item.Value, Sha256(RepoPath(
                    item.Key.Split('/'))));
            }
            // Discovery's frame scheduler/buffers are now optimized; preserve its
            // behavioral handoff instead of freezing the entire implementation.
            string discovery = ReadSource("World", "SoulTapeWorldDiscoveryController.cs");
            Assert.Contains("_discovery.Discover(pickup.Cassette.Id)", discovery);
            Assert.Contains("_planner.CreateRaidSelection(", discovery);
            Assert.Contains("SoulTapeDiscoveryResult.NewUnlock", discovery);
            Assert.Contains("QueryTriggerInteraction.Ignore", discovery);
        }

        [Fact]
        [Trait("Validation", "WorldCassetteVisual")]
        public void PackagedReplacementIsRecordedAsRedistributionSafeCc0()
        {
            string manifestPath = RepoPath(
                "Assets", "SoulRecorder", "source-manifest.json");
            JObject manifest = JObject.Parse(File.ReadAllText(manifestPath));
            JObject cassette = manifest["Assets"]
                .Children<JObject>()
                .Single(asset => (string)asset["LogicalName"] == "soultape_cassette");
            string licensePath = RepoPath(
                ((string)cassette["LicenseRecordPath"]).Split('/'));

            Assert.Equal("CC0", (string)cassette["License"]);
            Assert.True((bool)cassette["Redistributed"]);
            Assert.Equal(
                "4E1CA41FFE39921B7B10B4CB0CD8BCC43FECB79D97D2DEACC5A6439216BA6246",
                (string)cassette["SourceArchiveSha256"]);
            Assert.True(File.Exists(licensePath));
            Assert.Contains("comeinandburn", File.ReadAllText(licensePath));
            Assert.True(File.Exists(RepoPath(
                "Assets", "SoulRecorder", "bundle", "soultape_world.bundle")));
        }

        private static string ReadSource(params string[] parts)
        {
            return File.ReadAllText(RepoPath(parts));
        }

        private static string RepoPath(params string[] parts)
        {
            string path = FindRepositoryRoot();
            foreach (string part in parts)
            {
                path = Path.Combine(path, part);
            }
            return path;
        }

        private static string Sha256(string path)
        {
            using (SHA256 hash = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return BitConverter.ToString(hash.ComputeHash(stream))
                    .Replace("-", string.Empty);
            }
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "SoulPlayer.csproj")))
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("SoulPlayer repository root not found.");
        }
    }
}
