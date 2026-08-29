using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace SoulPlayer.Cassettes
{
    internal enum SoulTapeAssignmentLoadStatus
    {
        Missing = 0,
        Loaded = 1,
        RecoveredFromBackup = 2,
        Failed = 3
    }

    internal sealed class SoulTapeAssignmentData
    {
        internal const int CurrentVersion = 1;

        public SoulTapeAssignmentData()
        {
            Version = CurrentVersion;
            ProfileId = string.Empty;
            MusicMode = SoulTapeMusicMode.MergeBuiltInAndUser;
            LibraryFingerprint = string.Empty;
            AnchorFingerprint = string.Empty;
            Assignments = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        [JsonProperty("version", Order = 1)]
        public int Version { get; set; }

        [JsonProperty("profileId", Order = 2)]
        public string ProfileId { get; set; }

        [JsonProperty("musicMode", Order = 3)]
        [JsonConverter(typeof(StringEnumConverter))]
        public SoulTapeMusicMode MusicMode { get; set; }

        [JsonProperty("libraryFingerprint", Order = 4)]
        public string LibraryFingerprint { get; set; }

        [JsonProperty("anchorFingerprint", Order = 5)]
        public string AnchorFingerprint { get; set; }

        [JsonProperty("assignments", Order = 6)]
        public Dictionary<string, string> Assignments { get; set; }
    }

    internal sealed class SoulTapeAssignmentLoadResult
    {
        internal SoulTapeAssignmentLoadResult(
            SoulTapeAssignmentLoadStatus status,
            SoulTapeAssignmentData data,
            string error)
        {
            Status = status;
            Data = data;
            Error = error ?? string.Empty;
        }

        internal SoulTapeAssignmentLoadStatus Status { get; private set; }
        internal SoulTapeAssignmentData Data { get; private set; }
        internal string Error { get; private set; }
    }

    internal interface ISoulTapeAssignmentStore
    {
        SoulTapeAssignmentLoadResult Load(string profileId);
        void Save(string profileId, SoulTapeAssignmentData data);
        string Describe(string profileId);
    }

    internal sealed class JsonSoulTapeAssignmentStore : ISoulTapeAssignmentStore
    {
        private readonly string _rootFolder;

        internal JsonSoulTapeAssignmentStore(string rootFolder)
        {
            _rootFolder = rootFolder;
        }

        public SoulTapeAssignmentLoadResult Load(string profileId)
        {
            string path = GetPath(profileId);
            string backupPath = path + ".bak";
            bool primaryExists = File.Exists(path);
            bool backupExists = File.Exists(backupPath);
            if (!primaryExists && !backupExists)
            {
                return new SoulTapeAssignmentLoadResult(
                    SoulTapeAssignmentLoadStatus.Missing, null, string.Empty);
            }

            string primaryError = string.Empty;
            if (primaryExists)
            {
                SoulTapeAssignmentData primary = TryRead(
                    path, profileId, out primaryError);
                if (primary != null)
                {
                    return new SoulTapeAssignmentLoadResult(
                        SoulTapeAssignmentLoadStatus.Loaded, primary, string.Empty);
                }
            }

            string backupError = string.Empty;
            if (backupExists)
            {
                SoulTapeAssignmentData backup = TryRead(
                    backupPath, profileId, out backupError);
                if (backup != null)
                {
                    TryRestoreBackup(backupPath, path);
                    return new SoulTapeAssignmentLoadResult(
                        SoulTapeAssignmentLoadStatus.RecoveredFromBackup,
                        backup,
                        primaryError);
                }
            }

            if (primaryExists)
            {
                TryArchiveCorrupt(path);
            }
            string error = string.Join(" | ", new[]
            {
                primaryError,
                backupError
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
            return new SoulTapeAssignmentLoadResult(
                SoulTapeAssignmentLoadStatus.Failed, null, error);
        }

        public void Save(string profileId, SoulTapeAssignmentData data)
        {
            Directory.CreateDirectory(_rootFolder);
            string path = GetPath(profileId);
            string backupPath = path + ".bak";
            string temporaryPath = path + ".tmp";
            string json = JsonConvert.SerializeObject(data, Formatting.Indented);

            try
            {
                File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    File.Copy(path, backupPath, true);
                    try
                    {
                        File.Replace(temporaryPath, path, null);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Copy(temporaryPath, path, true);
                        File.Delete(temporaryPath);
                    }
                    catch (IOException)
                    {
                        File.Copy(temporaryPath, path, true);
                        File.Delete(temporaryPath);
                    }
                }
                else
                {
                    File.Move(temporaryPath, path);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        public string Describe(string profileId)
        {
            return GetPath(profileId);
        }

        private static SoulTapeAssignmentData TryRead(
            string path,
            string expectedProfileId,
            out string error)
        {
            error = string.Empty;
            try
            {
                SoulTapeAssignmentData data =
                    JsonConvert.DeserializeObject<SoulTapeAssignmentData>(
                        File.ReadAllText(path, Encoding.UTF8));
                if (data == null)
                {
                    throw new InvalidDataException("Assignment document is empty.");
                }
                if (data.Version != SoulTapeAssignmentData.CurrentVersion)
                {
                    throw new InvalidDataException(
                        "Unsupported assignment version " + data.Version + ".");
                }
                if (!string.Equals(
                        data.ProfileId,
                        expectedProfileId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Assignment profile ID does not match its file.");
                }

                data.Assignments = data.Assignments ??
                    new Dictionary<string, string>(StringComparer.Ordinal);
                data.Assignments = data.Assignments
                    .Where(pair =>
                        !string.IsNullOrWhiteSpace(pair.Key) &&
                        !string.IsNullOrWhiteSpace(pair.Value))
                    .ToDictionary(
                        pair => pair.Key.Trim(),
                        pair => pair.Value.Trim(),
                        StringComparer.Ordinal);
                data.LibraryFingerprint = data.LibraryFingerprint ?? string.Empty;
                data.AnchorFingerprint = data.AnchorFingerprint ?? string.Empty;
                return data;
            }
            catch (Exception ex)
            {
                error = Path.GetFileName(path) + ": " +
                        ex.GetBaseException().Message;
                return null;
            }
        }

        private static void TryRestoreBackup(string backupPath, string path)
        {
            try
            {
                File.Copy(backupPath, path, true);
            }
            catch
            {
                // Valid recovery data is still returned in memory. A later save
                // retries the primary write without risking the backup.
            }
        }

        private static void TryArchiveCorrupt(string path)
        {
            try
            {
                string archivePath = path + ".corrupt";
                if (!File.Exists(archivePath))
                {
                    File.Copy(path, archivePath, false);
                }
            }
            catch
            {
                // Archiving is best effort and must never block safe recovery.
            }
        }

        private string GetPath(string profileId)
        {
            string safeProfileId = new string((profileId ?? string.Empty)
                .Where(character =>
                    char.IsLetterOrDigit(character) ||
                    character == '-' || character == '_')
                .ToArray());
            if (string.IsNullOrWhiteSpace(safeProfileId))
            {
                throw new ArgumentException(
                    "A valid profile ID is required.", "profileId");
            }
            return Path.Combine(_rootFolder, safeProfileId + ".json");
        }
    }
}
