using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SoulPlayer.Cassettes;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    public sealed class SoulTapePlacementAuthoringTests
    {
        [Fact]
        [Trait("Validation", "PlacementAuthoring")]
        public void HorizontalPlacementIsValidAndClearsTheSurface()
        {
            SoulTapePlacementValidationResult result = Solve(
                new SoulTapeVector3(0f, 0f, 0f),
                SoulTapePlacementMath.Up,
                0f,
                0f,
                delegate { return false; });

            Assert.True(result.IsValid);
            Assert.True(result.Position.Y > SoulTapePlacementSolver.CassetteHalfExtents.Y);
            Assert.True(SoulTapePlacementMath.QuaternionAngleDegrees(
                result.Rotation,
                SoulTapePlacementMath.LookRotation(
                    SoulTapePlacementMath.Forward,
                    SoulTapePlacementMath.Up)) < 0.01f);
        }

        [Fact]
        [Trait("Validation", "PlacementAuthoring")]
        public void InclinedPlacementAlignsCassetteUpToSurfaceNormal()
        {
            SoulTapeVector3 normal = SoulTapePlacementMath.NormalizeOr(
                new SoulTapeVector3(0f, 1f, 0.6f),
                SoulTapePlacementMath.Up);
            SoulTapePlacementValidationResult result = Solve(
                new SoulTapeVector3(2f, 3f, 4f),
                normal,
                0f,
                0f,
                delegate { return false; });

            Assert.True(result.IsValid);
            SoulTapeVector3 solvedUp = SoulTapePlacementMath.Rotate(
                result.Rotation,
                SoulTapePlacementMath.Up);
            Assert.True(VectorAngle(normal, solvedUp) < 0.01f);
        }

        [Fact]
        [Trait("Validation", "PlacementAuthoring")]
        public void NearVerticalSurfaceIsRejected()
        {
            SoulTapePlacementValidationResult result = Solve(
                new SoulTapeVector3(0f, 0f, 0f),
                new SoulTapeVector3(1f, 0.05f, 0f),
                0f,
                0f,
                delegate { return false; });

            Assert.False(result.IsValid);
            Assert.Contains("vertical", result.Reason);
        }

        [Fact]
        [Trait("Validation", "PlacementAuthoring")]
        public void SolverNudgesOutwardUntilBoundsStopClipping()
        {
            SoulTapePlacementValidationResult result = Solve(
                new SoulTapeVector3(0f, 0f, 0f),
                SoulTapePlacementMath.Up,
                0f,
                0f,
                (center, rotation, extents) => center.Y < 0.03f);

            Assert.True(result.IsValid);
            Assert.True(result.OutwardNudge > 0f);
            Assert.True(result.Position.Y >= 0.03f);
        }

        [Fact]
        [Trait("Validation", "PlacementAuthoring")]
        public void ManualOffsetRaisesSolvedTransformWithoutEmbedding()
        {
            SoulTapePlacementValidationResult baseline = Solve(
                new SoulTapeVector3(0f, 0f, 0f),
                SoulTapePlacementMath.Up,
                0f,
                0f,
                delegate { return false; });
            SoulTapePlacementValidationResult offset = Solve(
                new SoulTapeVector3(0f, 0f, 0f),
                SoulTapePlacementMath.Up,
                0f,
                0.025f,
                delegate { return false; });

            Assert.True(offset.IsValid);
            Assert.Equal(0.025f, offset.Position.Y - baseline.Position.Y, 4);
        }

        [Fact]
        [Trait("Validation", "PlacementAuthoring")]
        public void PersistentCollisionProducesInvalidPlacement()
        {
            SoulTapePlacementValidationResult result = Solve(
                new SoulTapeVector3(0f, 0f, 0f),
                SoulTapePlacementMath.Up,
                0f,
                0f,
                delegate { return true; });

            Assert.False(result.IsValid);
            Assert.Contains("clipped", result.Reason);
        }

        [Fact]
        [Trait("Validation", "PlacementAuthoring")]
        public void RotationAdjustmentIsDeterministicAroundSurfaceNormal()
        {
            SoulTapePlacementValidationResult first = Solve(
                new SoulTapeVector3(0f, 0f, 0f),
                SoulTapePlacementMath.Up,
                45f,
                0f,
                delegate { return false; });
            SoulTapePlacementValidationResult second = Solve(
                new SoulTapeVector3(0f, 0f, 0f),
                SoulTapePlacementMath.Up,
                45f,
                0f,
                delegate { return false; });
            SoulTapePlacementValidationResult rotated = Solve(
                new SoulTapeVector3(0f, 0f, 0f),
                SoulTapePlacementMath.Up,
                90f,
                0f,
                delegate { return false; });

            Assert.True(SoulTapePlacementMath.QuaternionAngleDegrees(
                first.Rotation,
                second.Rotation) < 0.001f);
            Assert.True(SoulTapePlacementMath.QuaternionAngleDegrees(
                first.Rotation,
                rotated.Rotation) > 40f);
            Assert.True(VectorAngle(
                SoulTapePlacementMath.Rotate(rotated.Rotation, SoulTapePlacementMath.Up),
                SoulTapePlacementMath.Up) < 0.01f);
        }

        [Fact]
        [Trait("Validation", "PlacementAuthoring")]
        public void DuplicateNearbyAnchorIsRejectedButDifferentMapIsAllowed()
        {
            SoulTapeSpawnAnchor existing = Anchor("existing", "bigmap", 1f, 2f, 3f);
            SoulTapeSpawnAnchor nearby = Anchor("nearby", "bigmap", 1.05f, 2f, 3f);
            SoulTapeSpawnAnchor otherMap = Anchor("other-map", "woods", 1.05f, 2f, 3f);

            Assert.True(SoulTapeSpawnAnchorStore.IsDuplicateNearby(
                new[] { existing },
                nearby,
                SoulTapeSpawnAnchorStore.DuplicateDistanceMetres));
            Assert.False(SoulTapeSpawnAnchorStore.IsDuplicateNearby(
                new[] { existing },
                otherMap,
                SoulTapeSpawnAnchorStore.DuplicateDistanceMetres));
        }

        [Fact]
        [Trait("Validation", "PlacementAuthoring")]
        public void PlacementToolsTogglePlayerBasedNoclip()
        {
            string repositoryRoot = FindRepositoryRoot();
            string marker = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "World",
                "DevelopmentSoulTapeSpawnMarker.cs")).Replace("\r\n", "\n");
            string plugin = File.ReadAllText(Path.Combine(repositoryRoot, "Plugin.cs"));
            string documentation = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "docs",
                "SOULTAPE-AUTHORING.md"));

            Assert.Contains("KeyCode.F8", marker);
            Assert.Contains("KeyCode.LeftControl", marker);
            Assert.Contains("KeyCode.LeftShift", marker);
            Assert.Contains("TryEnableNoclip();", marker);
            Assert.Contains("DisableNoclip(\"author toggle\", true);", marker);
            Assert.Contains("SoulPlayer Noclip: ON", marker);
            Assert.Contains("SoulPlayer Noclip: OFF", marker);
            Assert.DoesNotContain("SoulPlayer Authoring Freecam", marker);
            Assert.DoesNotContain("GamePlayerOwner.SetIgnoreInput", marker);
            Assert.Contains("SoulPlayer Hybrid Authoring Camera", marker);

            Assert.Contains("hybrid player/camera authoring noclip", plugin);
            Assert.Contains("Ctrl+Shift+F8", documentation);
            Assert.Contains("Hybrid", documentation);
        }

        [Fact]
        [Trait("Validation", "PlacementAuthoring")]
        public void PlayerNoclipAccumulatesHorizontalMovementAndTeleportsAtFixedRate()
        {
            string marker = ReadRepositoryFile(
                "World",
                "DevelopmentSoulTapeSpawnMarker.cs");

            Assert.Contains("private void UpdateMovementTarget()", marker);
            Assert.Contains("gameplayCamera.transform.forward", marker);
            Assert.Contains("gameplayCamera.transform.right", marker);
            Assert.Contains("ShiftHeld()", marker);
            Assert.Contains("forward.y = 0f", marker);
            Assert.Contains("right.y = 0f", marker);
            Assert.Contains("_movementTarget.x += horizontalStep.x", marker);
            Assert.Contains("_movementTarget.z += horizontalStep.z", marker);
            Assert.Contains("Time.unscaledDeltaTime", marker);
            Assert.Contains("private void FixedUpdate()", marker);
            Assert.Contains("MoveTowardsHorizontal", marker);
            Assert.Contains("_authoringPlayer.Teleport(next, false);", marker);
            Assert.Contains("_movementTargetDirty", marker);
            Assert.DoesNotContain("_authoringPlayer.transform.position =", marker);
            Assert.DoesNotContain("UpdatePlayerFlight", marker);
        }

        [Fact]
        [Trait("Validation", "PlacementAuthoring")]
        public void SpaceAndControlChangeOnlyClampedAuthoringCameraHeight()
        {
            string marker = ReadRepositoryFile(
                "World",
                "DevelopmentSoulTapeSpawnMarker.cs");

            Assert.Contains("KeyCode.Space", marker);
            Assert.Contains("ControlHeld()", marker);
            Assert.Contains("_verticalCameraOffset + verticalDirection * speed", marker);
            Assert.Contains("ClampVerticalCameraOffset", marker);
            Assert.Contains("MaximumVerticalCameraOffset = 25f", marker);
            Assert.Contains("Vector3.up * _verticalCameraOffset", marker);
            Assert.DoesNotContain("_movementTarget.y +=", marker);
        }

        [Fact]
        [Trait("Validation", "PlacementAuthoring")]
        public void PlayerNoclipRestoresMovementCollisionAndProtection()
        {
            string marker = ReadRepositoryFile(
                "World",
                "DevelopmentSoulTapeSpawnMarker.cs");

            Assert.Contains("_originalIgnoreDeltaMovement = _movementContext.IgnoreDeltaMovement", marker);
            Assert.Contains("_movementContext.IgnoreDeltaMovement = true", marker);
            Assert.Contains("_movementContext.IgnoreDeltaMovement = _originalIgnoreDeltaMovement", marker);
            Assert.Contains("GetComponentsInChildren<Collider>(true)", marker);
            Assert.Contains("collider.isTrigger || !collider.enabled", marker);
            Assert.Contains("state.Collider.enabled = state.WasEnabled", marker);
            Assert.Contains("_authoringPlayer.Teleport(_safeEntryPosition, false);", marker);

            Assert.Contains("_originalDamageCoefficient = _healthController.DamageCoeff", marker);
            Assert.Contains("_originalFallSafeHeight = _healthController.FallSafeHeight", marker);
            Assert.Contains("_healthController.SetDamageCoeff(0f)", marker);
            Assert.Contains("_healthController.FallSafeHeight = float.MaxValue", marker);
            Assert.Contains("_healthController.SetDamageCoeff(_originalDamageCoefficient)", marker);
            Assert.Contains("_healthController.FallSafeHeight = _originalFallSafeHeight", marker);
            Assert.DoesNotContain("RestoreFullHealth", marker);
            Assert.DoesNotContain("MaintainAuthorProtection", marker);
            Assert.Equal(
                1,
                marker.Split(
                    new[] { "ApplyAuthorProtection();" },
                    StringSplitOptions.None).Length - 1);
            Assert.Equal(
                1,
                marker.Split(
                    new[] { "GetComponentsInChildren<Collider>(true)" },
                    StringSplitOptions.None).Length - 1);

            Assert.Contains("OnIPlayerDeadOrUnspawn", marker);
            Assert.Contains("SceneManager.activeSceneChanged", marker);
            Assert.Contains("internal void Shutdown(string reason)", marker);
            Assert.Contains("OnApplicationQuit", marker);
            Assert.Contains("DisableNoclip(\"component destroyed\", true)", marker);
        }

        [Fact]
        [Trait("Validation", "PlacementAuthoring")]
        public void PlayerNoclipLeavesTriggerCollidersEnabled()
        {
            string marker = ReadRepositoryFile(
                "World",
                "DevelopmentSoulTapeSpawnMarker.cs");

            Assert.Contains(
                "collider == null || collider.isTrigger || !collider.enabled",
                marker);
            Assert.Contains("collider.enabled = false;", marker);
            Assert.Contains("state.Collider.enabled = state.WasEnabled", marker);
        }

        [Fact]
        [Trait("Validation", "PlacementAuthoring")]
        public void F9UsesOffsetCameraWithNoclipAndGameplayCameraWithoutIt()
        {
            string repositoryRoot = FindRepositoryRoot();
            string marker = ReadRepositoryFile(
                "World",
                "DevelopmentSoulTapeSpawnMarker.cs");
            string documentation = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "docs",
                "SOULTAPE-AUTHORING.md"));

            Assert.Contains("new KeyboardShortcut(KeyCode.F9)", marker);
            Assert.Contains("CaptureAndSaveAnchor();", marker);
            Assert.Contains("Camera main = Camera.main", marker);
            Assert.Contains("ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))", marker);
            Assert.Contains("if (_noclipEnabled)", marker);
            Assert.Contains("SyncAuthoringCamera()", marker);
            Assert.Contains("camera = _authoringCamera", marker);
            Assert.Contains("ray = _authoringCamera.ViewportPointToRay", marker);
            Assert.Contains("return TryGetGameplayScreenCenterRay(out camera, out ray);", marker);
            Assert.Contains("if (rayCamera == _authoringCamera)", marker);
            Assert.Contains("viewModelCamera = originalGameplayCamera", marker);
            Assert.Contains("QueryTriggerInteraction.Ignore", marker);
            Assert.Contains("Physics.RaycastAll(", marker);
            Assert.Contains("QueryTriggerInteraction.Collide", marker);
            Assert.Contains(
                "RejectWithoutPreview(\"No valid world surface is currently targeted.\")",
                marker);
            Assert.Contains("_preview = null;\n            SetGhostVisible(false);", marker);
            Assert.Contains("SavePreview(mapId);\n            ShowSolvedGhost();", marker);
            Assert.Contains("FeedbackDurationSeconds = 2f", marker);
            Assert.Contains("Cassette anchor rotation degrees", marker);
            Assert.Contains("Cassette anchor surface offset", marker);
            Assert.Contains("Press **F9**", documentation);
            Assert.Contains("normal Tarkov movement", documentation);
            Assert.Contains("F9 works in both modes", documentation);
            Assert.Contains("gameplay camera", documentation);
        }

        [Fact]
        [Trait("Validation", "PlacementAuthoring")]
        public void HybridNoclipCleanupRestoresNormalCamera()
        {
            string marker = ReadRepositoryFile(
                "World",
                "DevelopmentSoulTapeSpawnMarker.cs");

            Assert.Contains("_authoringCamera.CopyFrom(gameplayCamera)", marker);
            Assert.Contains("DestroyAuthoringCamera();", marker);
            Assert.Contains("_authoringCamera.enabled = false", marker);
            Assert.Contains("Destroy(_authoringCameraRoot)", marker);
            Assert.Contains("_verticalCameraOffset = 0f", marker);
        }

        [Fact]
        [Trait("Validation", "PlacementAuthoring")]
        public void SavedAnchorCountUsesCanonicalMapAndIgnoresCase()
        {
            SoulTapeSpawnAnchor[] anchors =
            {
                Anchor("one", "bigmap", 1f, 2f, 3f),
                Anchor("two", "BIGMAP", 4f, 5f, 6f),
                Anchor("three", "woods", 7f, 8f, 9f),
                null
            };

            Assert.Equal(2, SoulTapeSpawnAnchorStore.CountForMap(anchors, "bigmap"));
            Assert.Equal(1, SoulTapeSpawnAnchorStore.CountForMap(anchors, "woods"));
            Assert.Equal(0, SoulTapeSpawnAnchorStore.CountForMap(anchors, string.Empty));
            Assert.Equal(0, SoulTapeSpawnAnchorStore.CountForMap(null, "bigmap"));
        }

        [Fact]
        [Trait("Validation", "PlacementAuthoring")]
        public void AnchorDocumentSavesReloadsAndUsesInvariantJsonNumbers()
        {
            string folder = CreateTemporaryFolder();
            try
            {
                string path = Path.Combine(folder, "spawn-anchors.json");
                SoulTapeSpawnAnchorStore store = new SoulTapeSpawnAnchorStore(path);
                SoulTapeSpawnAnchor anchor = Anchor("anchor-1", "factory4_day", 12.5f, 1.25f, -8.75f);
                anchor.SemanticTag = "office-desk";

                string rejection;
                Assert.True(store.TryAppend(anchor, out rejection), rejection);
                SoulTapeAnchorLoadResult loaded = store.Load();

                Assert.Equal(SoulTapeAnchorLoadStatus.Loaded, loaded.Status);
                Assert.Single(loaded.Document.Anchors);
                Assert.Equal(12.5f, loaded.Document.Anchors[0].Position.X);
                string json = File.ReadAllText(path);
                Assert.Contains("12.5", json);
                Assert.DoesNotContain("12,5", json);

                SoulTapeSpawnAnchorDocument roundTrip =
                    JsonConvert.DeserializeObject<SoulTapeSpawnAnchorDocument>(json);
                Assert.Equal("office-desk", roundTrip.Anchors[0].SemanticTag);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Fact]
        [Trait("Validation", "PlacementAuthoring")]
        public void CorruptPrimaryRecoversPreviousAnchorDocumentFromBackup()
        {
            string folder = CreateTemporaryFolder();
            try
            {
                string path = Path.Combine(folder, "spawn-anchors.json");
                SoulTapeSpawnAnchorStore store = new SoulTapeSpawnAnchorStore(path);
                SoulTapeSpawnAnchorDocument first = new SoulTapeSpawnAnchorDocument();
                first.Anchors.Add(Anchor("anchor-1", "woods", 1f, 2f, 3f));
                store.Save(first);

                SoulTapeSpawnAnchorDocument second = new SoulTapeSpawnAnchorDocument();
                second.Anchors.AddRange(first.Anchors);
                second.Anchors.Add(Anchor("anchor-2", "woods", 4f, 5f, 6f));
                store.Save(second);
                File.WriteAllText(path, "{ corrupt authoring json");
                string goodBackup = File.ReadAllText(path + ".bak");

                SoulTapeAnchorLoadResult recovered = store.Load();

                Assert.Equal(SoulTapeAnchorLoadStatus.RecoveredFromBackup, recovered.Status);
                Assert.Single(recovered.Document.Anchors);
                Assert.Equal("anchor-1", recovered.Document.Anchors[0].Id);

                string rejection;
                Assert.True(store.TryAppend(
                    Anchor("anchor-3", "woods", 8f, 9f, 10f),
                    out rejection), rejection);
                Assert.Equal(goodBackup, File.ReadAllText(path + ".bak"));
                Assert.False(File.Exists(path + ".tmp"));
                SoulTapeAnchorLoadResult repaired = store.Load();
                Assert.Equal(SoulTapeAnchorLoadStatus.Loaded, repaired.Status);
                Assert.Equal(2, repaired.Document.Anchors.Count);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Fact]
        [Trait("Validation", "PlacementAuthoring")]
        public void MissingPrimaryRecoversValidBackup()
        {
            string folder = CreateTemporaryFolder();
            try
            {
                string path = Path.Combine(folder, "spawn-anchors.json");
                SoulTapeSpawnAnchorStore store = new SoulTapeSpawnAnchorStore(path);
                SoulTapeSpawnAnchorDocument first = new SoulTapeSpawnAnchorDocument();
                first.Anchors.Add(Anchor("anchor-1", "shoreline", 1f, 2f, 3f));
                store.Save(first);

                SoulTapeSpawnAnchorDocument second = new SoulTapeSpawnAnchorDocument();
                second.Anchors.AddRange(first.Anchors);
                second.Anchors.Add(Anchor("anchor-2", "shoreline", 4f, 5f, 6f));
                store.Save(second);
                File.Delete(path);

                SoulTapeAnchorLoadResult recovered = store.Load();

                Assert.Equal(SoulTapeAnchorLoadStatus.RecoveredFromBackup, recovered.Status);
                Assert.Single(recovered.Document.Anchors);
                Assert.Equal("anchor-1", recovered.Document.Anchors[0].Id);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Fact]
        [Trait("Validation", "PlacementAuthoring")]
        public void MissingBothCopiesIsMissingButUnusableCopiesAreUnreadable()
        {
            string folder = CreateTemporaryFolder();
            try
            {
                string path = Path.Combine(folder, "spawn-anchors.json");
                SoulTapeSpawnAnchorStore store = new SoulTapeSpawnAnchorStore(path);

                Assert.Equal(SoulTapeAnchorLoadStatus.Missing, store.Load().Status);

                File.WriteAllText(path, "{ bad primary");
                File.WriteAllText(path + ".bak", "{ bad backup");
                Assert.Equal(SoulTapeAnchorLoadStatus.Unreadable, store.Load().Status);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        private static SoulTapePlacementValidationResult Solve(
            SoulTapeVector3 hitPoint,
            SoulTapeVector3 normal,
            float rotationDegrees,
            float offset,
            SoulTapePlacementCollisionProbe collisionProbe)
        {
            return new SoulTapePlacementSolver().Solve(
                hitPoint,
                normal,
                SoulTapePlacementMath.Forward,
                rotationDegrees,
                offset,
                collisionProbe);
        }

        private static float VectorAngle(SoulTapeVector3 left, SoulTapeVector3 right)
        {
            SoulTapeVector3 normalizedLeft = SoulTapePlacementMath.NormalizeOr(
                left,
                SoulTapePlacementMath.Forward);
            SoulTapeVector3 normalizedRight = SoulTapePlacementMath.NormalizeOr(
                right,
                SoulTapePlacementMath.Forward);
            double dot = Math.Max(-1.0, Math.Min(
                1.0,
                SoulTapePlacementMath.Dot(normalizedLeft, normalizedRight)));
            return (float)(Math.Acos(dot) * 180.0 / Math.PI);
        }

        private static SoulTapeSpawnAnchor Anchor(
            string id,
            string mapId,
            float x,
            float y,
            float z)
        {
            return new SoulTapeSpawnAnchor
            {
                Id = id,
                MapId = mapId,
                Position = new SoulTapeVector3(x, y, z),
                RotationEuler = new SoulTapeVector3(0f, 0f, 0f),
                SurfaceNormal = new SoulTapeVector3(0f, 1f, 0f),
                Source = SoulTapeSpawnAnchorSource.Curated,
                Enabled = true
            };
        }

        private static string CreateTemporaryFolder()
        {
            string folder = Path.Combine(
                Path.GetTempPath(),
                "SoulTapeAuthoring-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            return folder;
        }

        private static string ReadRepositoryFile(params string[] relativeParts)
        {
            string path = relativeParts.Aggregate(
                FindRepositoryRoot(),
                Path.Combine);
            return File.ReadAllText(path).Replace("\r\n", "\n");
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "SoulPlayer.csproj")))
                {
                    return current.FullName;
                }
                current = current.Parent;
            }

            throw new DirectoryNotFoundException(
                "SoulPlayer repository root could not be located from the test output.");
        }
    }
}
