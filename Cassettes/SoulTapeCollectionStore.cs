using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace SoulPlayer.Cassettes
{
    internal enum SoulTapeLoadStatus
    {
        Missing = 0,
        Loaded = 1,
        RecoveredFromBackup = 2,
        Failed = 3
    }

    internal sealed class SoulTapeCollectionData
    {
        public SoulTapeCollectionData()
        {
            Version = 1;
            ProfileId = string.Empty;
            UnlockedCassetteIds = new List<string>();
            FavoriteCassetteIds = new List<string>();
        }

        public int Version { get; set; }
        public string ProfileId { get; set; }
        public List<string> UnlockedCassetteIds { get; set; }
        public List<string> FavoriteCassetteIds { get; set; }
    }

    internal sealed class SoulTapeLoadResult
    {
        internal SoulTapeLoadResult(
            SoulTapeLoadStatus status,
            SoulTapeCollectionData data,
            string error)
        {
            Status = status;
            Data = data;
            Error = error ?? string.Empty;
        }

        internal SoulTapeLoadStatus Status { get; private set; }
        internal SoulTapeCollectionData Data { get; private set; }
        internal string Error { get; private set; }
    }

    internal interface ISoulTapeCollectionStore
    {
        SoulTapeLoadResult Load(string profileId);
        void Save(string profileId, SoulTapeCollectionData data);
        string Describe(string profileId);
    }

    internal sealed class JsonSoulTapeCollectionStore : ISoulTapeCollectionStore
    {
        private readonly string _rootFolder;

        internal JsonSoulTapeCollectionStore(string rootFolder)
        {
            _rootFolder = rootFolder;
        }

        public SoulTapeLoadResult Load(string profileId)
        {
            string path = GetPath(profileId);
            string backupPath = path + ".bak";
            bool primaryExists = File.Exists(path);
            bool backupExists = File.Exists(backupPath);

            if (!primaryExists && !backupExists)
            {
                return new SoulTapeLoadResult(SoulTapeLoadStatus.Missing, null, string.Empty);
            }

            string primaryError = string.Empty;
            if (primaryExists)
            {
                SoulTapeCollectionData primary = TryRead(path, profileId, out primaryError);
                if (primary != null)
                {
                    return new SoulTapeLoadResult(SoulTapeLoadStatus.Loaded, primary, string.Empty);
                }
            }

            string backupError = string.Empty;
            if (backupExists)
            {
                SoulTapeCollectionData backup = TryRead(backupPath, profileId, out backupError);
                if (backup != null)
                {
                    try
                    {
                        Directory.CreateDirectory(_rootFolder);
                        File.Copy(backupPath, path, true);
                    }
                    catch
                    {
                        // Recovery data is still returned in memory. The next
                        // successful collection mutation will retry the save.
                    }

                    return new SoulTapeLoadResult(
                        SoulTapeLoadStatus.RecoveredFromBackup,
                        backup,
                        primaryError);
                }
            }

            string error = string.Join(
                " | ",
                new[] { primaryError, backupError }.Where(value => !string.IsNullOrWhiteSpace(value)));
            return new SoulTapeLoadResult(SoulTapeLoadStatus.Failed, null, error);
        }

        public void Save(string profileId, SoulTapeCollectionData data)
        {
            Directory.CreateDirectory(_rootFolder);
            string path = GetPath(profileId);
            string backupPath = path + ".bak";
            string temporaryPath = path + ".tmp";
            string json = JsonConvert.SerializeObject(data, Formatting.Indented);

            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            try
            {
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

        private SoulTapeCollectionData TryRead(
            string path,
            string expectedProfileId,
            out string error)
        {
            error = string.Empty;
            try
            {
                SoulTapeCollectionData data = JsonConvert.DeserializeObject<SoulTapeCollectionData>(
                    File.ReadAllText(path, Encoding.UTF8));
                if (data == null)
                {
                    throw new InvalidDataException("Collection document is empty.");
                }

                if (data.Version != 1)
                {
                    throw new InvalidDataException("Unsupported collection version " + data.Version + ".");
                }

                if (!string.Equals(data.ProfileId, expectedProfileId, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Collection profile ID does not match its file.");
                }

                data.UnlockedCassetteIds = data.UnlockedCassetteIds ?? new List<string>();
                data.FavoriteCassetteIds = data.FavoriteCassetteIds ?? new List<string>();
                return data;
            }
            catch (Exception ex)
            {
                error = Path.GetFileName(path) + ": " + ex.GetBaseException().Message;
                return null;
            }
        }

        private string GetPath(string profileId)
        {
            string safeProfileId = new string((profileId ?? string.Empty)
                .Where(character => char.IsLetterOrDigit(character) || character == '-' || character == '_')
                .ToArray());

            if (string.IsNullOrWhiteSpace(safeProfileId))
            {
                throw new ArgumentException("A valid profile ID is required.", "profileId");
            }

            return Path.Combine(_rootFolder, safeProfileId + ".json");
        }
    }
}
