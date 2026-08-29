using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using SoulPlayer.Library;

namespace SoulPlayer.Cassettes
{
    internal enum SoulTapeAnchorLoadStatus
    {
        Missing = 0,
        Loaded = 1,
        RecoveredFromBackup = 2,
        Unreadable = 3
    }

    internal sealed class SoulTapeAnchorLoadResult
    {
        internal SoulTapeAnchorLoadResult(
            SoulTapeAnchorLoadStatus status,
            SoulTapeSpawnAnchorDocument document,
            string error)
        {
            Status = status;
            Document = document;
            Error = error ?? string.Empty;
        }

        internal SoulTapeAnchorLoadStatus Status { get; private set; }
        internal SoulTapeSpawnAnchorDocument Document { get; private set; }
        internal string Error { get; private set; }
    }

    internal sealed class SoulTapeSpawnAnchorStore
    {
        internal const float DuplicateDistanceMetres = 0.15f;

        private static readonly JsonSerializerSettings JsonSettings =
            new JsonSerializerSettings
            {
                Culture = CultureInfo.InvariantCulture,
                Formatting = Formatting.Indented
            };

        private readonly string _path;

        internal SoulTapeSpawnAnchorStore(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A spawn-anchor path is required.", "path");
            }

            _path = SoulPath.NormalizeConfiguredPath(
                path,
                AppDomain.CurrentDomain.BaseDirectory);
            if (string.IsNullOrWhiteSpace(_path))
            {
                throw new ArgumentException("The spawn-anchor path is invalid.", "path");
            }
        }

        internal string PathDescription { get { return _path; } }

        internal SoulTapeAnchorLoadResult Load()
        {
            bool primaryExists = File.Exists(_path);
            string primaryError;
            SoulTapeSpawnAnchorDocument primary = TryRead(_path, out primaryError);
            if (primary != null)
            {
                return new SoulTapeAnchorLoadResult(
                    SoulTapeAnchorLoadStatus.Loaded,
                    primary,
                    string.Empty);
            }

            string backupPath = _path + ".bak";
            bool backupExists = File.Exists(backupPath);
            string backupError;
            SoulTapeSpawnAnchorDocument backup = TryRead(backupPath, out backupError);
            if (backup != null)
            {
                return new SoulTapeAnchorLoadResult(
                    SoulTapeAnchorLoadStatus.RecoveredFromBackup,
                    backup,
                    primaryError);
            }

            if (!primaryExists && !backupExists)
            {
                return new SoulTapeAnchorLoadResult(
                    SoulTapeAnchorLoadStatus.Missing,
                    new SoulTapeSpawnAnchorDocument(),
                    string.Empty);
            }

            return new SoulTapeAnchorLoadResult(
                SoulTapeAnchorLoadStatus.Unreadable,
                null,
                "Primary: " + primaryError + " Backup: " + backupError);
        }

        internal bool TryAppend(
            SoulTapeSpawnAnchor anchor,
            out string rejectionReason)
        {
            rejectionReason = string.Empty;
            if (anchor == null)
            {
                rejectionReason = "The solved anchor is missing.";
                return false;
            }

            SoulTapeAnchorLoadResult loaded = Load();
            if (loaded.Status == SoulTapeAnchorLoadStatus.Unreadable)
            {
                rejectionReason =
                    "Existing authoring data is unreadable and was left untouched: " + loaded.Error;
                return false;
            }

            SoulTapeSpawnAnchorDocument document = loaded.Document ??
                                                   new SoulTapeSpawnAnchorDocument();
            if (IsDuplicateNearby(document.Anchors, anchor, DuplicateDistanceMetres))
            {
                rejectionReason =
                    "A curated anchor already exists within " +
                    DuplicateDistanceMetres.ToString("0.00", CultureInfo.InvariantCulture) +
                    " metres on this map.";
                return false;
            }

            document.Anchors.Add(anchor);
            Save(document, loaded.Status == SoulTapeAnchorLoadStatus.RecoveredFromBackup);
            return true;
        }

        internal void Save(SoulTapeSpawnAnchorDocument document)
        {
            Save(document, false);
        }

        private void Save(
            SoulTapeSpawnAnchorDocument document,
            bool preserveExistingBackup)
        {
            if (document == null)
            {
                throw new ArgumentNullException("document");
            }

            string folder = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                Directory.CreateDirectory(folder);
            }

            string temporaryPath = _path + ".tmp";
            try
            {
                File.WriteAllText(
                    temporaryPath,
                    JsonConvert.SerializeObject(document, JsonSettings),
                    new UTF8Encoding(false));

                if (File.Exists(_path))
                {
                    File.Replace(
                        temporaryPath,
                        _path,
                        preserveExistingBackup ? null : _path + ".bak",
                        true);
                }
                else
                {
                    File.Move(temporaryPath, _path);
                }
            }
            finally
            {
                TryDeleteTemporaryFile(temporaryPath);
            }
        }

        internal static bool IsDuplicateNearby(
            IEnumerable<SoulTapeSpawnAnchor> existing,
            SoulTapeSpawnAnchor candidate,
            float distanceMetres)
        {
            if (candidate == null || candidate.Position == null || existing == null)
            {
                return false;
            }

            float distanceSquared = Math.Max(0f, distanceMetres) *
                                    Math.Max(0f, distanceMetres);
            return existing.Any(anchor =>
                anchor != null &&
                anchor.Position != null &&
                string.Equals(anchor.MapId, candidate.MapId, StringComparison.OrdinalIgnoreCase) &&
                DistanceSquared(anchor.Position, candidate.Position) <= distanceSquared);
        }

        internal static int CountForMap(
            IEnumerable<SoulTapeSpawnAnchor> anchors,
            string mapId)
        {
            if (anchors == null || string.IsNullOrWhiteSpace(mapId))
            {
                return 0;
            }

            return anchors.Count(anchor =>
                anchor != null &&
                string.Equals(anchor.MapId, mapId, StringComparison.OrdinalIgnoreCase));
        }

        private static SoulTapeSpawnAnchorDocument TryRead(
            string path,
            out string error)
        {
            error = string.Empty;
            if (!File.Exists(path))
            {
                error = "File does not exist.";
                return null;
            }

            try
            {
                SoulTapeSpawnAnchorDocument document =
                    JsonConvert.DeserializeObject<SoulTapeSpawnAnchorDocument>(
                        File.ReadAllText(path, Encoding.UTF8),
                        JsonSettings);
                if (document == null ||
                    document.Version != SoulTapeSpawnAnchorDocument.CurrentVersion ||
                    document.Anchors == null)
                {
                    throw new InvalidDataException(
                        "Document is missing or uses an unsupported version.");
                }

                return document;
            }
            catch (Exception ex)
            {
                error = ex.GetBaseException().Message;
                return null;
            }
        }

        private static float DistanceSquared(SoulTapeVector3 left, SoulTapeVector3 right)
        {
            float x = left.X - right.X;
            float y = left.Y - right.Y;
            float z = left.Z - right.Z;
            return x * x + y * y + z * z;
        }

        private static void TryDeleteTemporaryFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Preserve the original save exception. A later save will safely
                // overwrite the same task-specific temporary file.
            }
        }
    }
}
