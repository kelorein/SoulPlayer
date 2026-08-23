using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;

namespace SoulPlayer.Cassettes
{
    internal sealed class SoulTapeSpawnAnchorCatalog
    {
        internal const string FactoryDayResourceName =
            "SoulPlayer.Data.SoulTape.SpawnAnchors.factory4_day.json";

        private readonly Dictionary<string, IReadOnlyList<SoulTapeSpawnAnchor>> _byMap =
            new Dictionary<string, IReadOnlyList<SoulTapeSpawnAnchor>>(
                StringComparer.OrdinalIgnoreCase);

        internal SoulTapeSpawnAnchorCatalog(Assembly assembly, ISoulTapeLog log)
        {
            LoadResource(assembly, FactoryDayResourceName, log);
        }

        internal IReadOnlyList<SoulTapeSpawnAnchor> GetForMap(string mapId)
        {
            IReadOnlyList<SoulTapeSpawnAnchor> anchors;
            if (!string.IsNullOrWhiteSpace(mapId) &&
                _byMap.TryGetValue(mapId, out anchors))
            {
                return anchors.ToList();
            }

            return new SoulTapeSpawnAnchor[0];
        }

        internal static SoulTapeSpawnAnchorDocument Parse(string json)
        {
            SoulTapeSpawnAnchorDocument document =
                JsonConvert.DeserializeObject<SoulTapeSpawnAnchorDocument>(json);
            if (document == null ||
                document.Version != SoulTapeSpawnAnchorDocument.CurrentVersion ||
                document.Anchors == null)
            {
                throw new InvalidDataException(
                    "SoulTape anchor data is empty or uses an unsupported version.");
            }

            return document;
        }

        private void LoadResource(
            Assembly assembly,
            string resourceName,
            ISoulTapeLog log)
        {
            try
            {
                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        throw new FileNotFoundException(
                            "Embedded SoulTape anchor resource was not found.",
                            resourceName);
                    }

                    string json;
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        json = reader.ReadToEnd();
                    }

                    SoulTapeSpawnAnchorDocument document = Parse(json);
                    foreach (IGrouping<string, SoulTapeSpawnAnchor> group in document.Anchors
                                 .Where(anchor =>
                                     anchor != null &&
                                     anchor.Enabled &&
                                     anchor.Source == SoulTapeSpawnAnchorSource.Curated &&
                                     !string.IsNullOrWhiteSpace(anchor.MapId))
                                 .GroupBy(anchor => anchor.MapId, StringComparer.OrdinalIgnoreCase))
                    {
                        _byMap[group.Key] = group.ToList();
                    }

                    log.Info(
                        "SoulTape loaded " + document.Anchors.Count +
                        " committed curated anchors from " + resourceName + ".");
                }
            }
            catch (Exception ex)
            {
                log.Error(
                    "SoulTape committed anchor data could not be loaded: " +
                    ex.GetBaseException().Message);
            }
        }
    }
}
