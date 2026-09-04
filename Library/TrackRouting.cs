using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace SoulPlayer.Library
{
    [Flags]
    internal enum TrackRoute
    {
        None = 0,
        Main = 1,
        Extract = 2,
        Death = 4,
        All = Main | Extract | Death
    }

    internal sealed class TrackRoutingRecord
    {
        public TrackRoutingRecord()
        {
            TrackId = string.Empty;
            Routes = TrackRoute.All;
            LastKnownPath = string.Empty;
            Artist = string.Empty;
            Title = string.Empty;
        }

        public string TrackId { get; set; }
        public TrackRoute Routes { get; set; }
        public string LastKnownPath { get; set; }
        public string Artist { get; set; }
        public string Title { get; set; }
    }

    internal sealed class TrackRoutingData
    {
        public TrackRoutingData()
        {
            Version = 1;
            Tracks = new List<TrackRoutingRecord>();
        }

        public int Version { get; set; }
        public List<TrackRoutingRecord> Tracks { get; set; }
    }

    internal sealed class TrackRoutingService
    {
        private readonly object _sync = new object();
        private readonly string _path;
        private readonly Action<string> _logWarning;
        private Dictionary<string, TrackRoutingRecord> _records;

        internal event Action Changed;

        internal TrackRoutingService(string path, Action<string> logWarning)
        {
            _path = SoulPath.NormalizeConfiguredPath(path, GetConfigPath());
            _logWarning = logWarning ?? delegate { };
            _records = Load();
        }

        internal static string GetDefaultPath()
        {
            return Path.Combine(
                GetConfigPath(),
                "SoulPlayer",
                "routing",
                "track-routes.json");
        }

        internal string PersistencePath
        {
            get { return _path; }
        }

        internal TrackRoute GetRoutes(MusicTrack track)
        {
            if (track == null)
            {
                return TrackRoute.All;
            }

            string id = GetTrackId(track);
            lock (_sync)
            {
                TrackRoutingRecord record;
                return _records.TryGetValue(id, out record)
                    ? Sanitize(record.Routes)
                    : TrackRoute.All;
            }
        }

        internal bool IsEligible(MusicTrack track, TrackRoute route)
        {
            return track != null && route != TrackRoute.None &&
                   (GetRoutes(track) & route) == route;
        }

        internal void SetRoute(MusicTrack track, TrackRoute route, bool enabled)
        {
            if (track == null || route == TrackRoute.None || route == TrackRoute.All)
            {
                return;
            }

            TrackRoute current = GetRoutes(track);
            SetRoutes(track, enabled ? current | route : current & ~route);
        }

        internal void SetRoutes(MusicTrack track, TrackRoute routes)
        {
            if (track == null)
            {
                return;
            }

            string id = GetTrackId(track);
            if (string.IsNullOrEmpty(id))
            {
                _logWarning("SoulPlayer could not persist routing for a track without a stable identity.");
                return;
            }

            TrackRoutingRecord record = new TrackRoutingRecord
            {
                TrackId = id,
                Routes = Sanitize(routes),
                LastKnownPath = SoulPath.NormalizeConfiguredPath(track.FilePath, string.Empty),
                Artist = track.Artist,
                Title = track.Title
            };

            lock (_sync)
            {
                _records[id] = record;
                try
                {
                    Save();
                }
                catch (Exception ex)
                {
                    _logWarning(
                        "SoulPlayer routing metadata could not be saved; the change is active for this session: " +
                        ex.Message);
                }
            }

            Action changed = Changed;
            if (changed != null)
            {
                changed();
            }
        }

        internal List<MusicTrack> SelectEligible(
            IEnumerable<MusicTrack> tracks,
            TrackRoute route)
        {
            return (tracks ?? Enumerable.Empty<MusicTrack>())
                .Where(track => IsEligible(track, route))
                .ToList();
        }

        internal static string GetTrackId(MusicTrack track)
        {
            if (track == null)
            {
                return string.Empty;
            }

            if (TrackFingerprint.IsValidSha256(track.AudioFingerprint))
            {
                return "sha256:" + track.AudioFingerprint.ToLowerInvariant();
            }

            return SoulPath.CreateStablePathIdentity(track.FilePath);
        }

        internal static bool AllowsDirectPlayback(MusicTrack track)
        {
            return track != null;
        }

        private Dictionary<string, TrackRoutingRecord> Load()
        {
            Dictionary<string, TrackRoutingRecord> empty =
                new Dictionary<string, TrackRoutingRecord>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(_path) || !File.Exists(_path))
            {
                return empty;
            }

            try
            {
                TrackRoutingData data = JsonConvert.DeserializeObject<TrackRoutingData>(
                    File.ReadAllText(_path, Encoding.UTF8));
                if (data == null || data.Version != 1)
                {
                    throw new InvalidDataException("Unsupported or empty routing document.");
                }

                return (data.Tracks ?? new List<TrackRoutingRecord>())
                    .Where(record => record != null && !string.IsNullOrWhiteSpace(record.TrackId))
                    .GroupBy(record => record.TrackId, StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group =>
                        {
                            TrackRoutingRecord record = group.Last();
                            record.Routes = Sanitize(record.Routes);
                            return record;
                        },
                        StringComparer.Ordinal);
            }
            catch (Exception ex)
            {
                _logWarning("SoulPlayer routing metadata could not be read; defaults are active: " + ex.Message);
                return empty;
            }
        }

        private void Save()
        {
            if (string.IsNullOrWhiteSpace(_path))
            {
                throw new InvalidOperationException("SoulPlayer routing persistence path is unavailable.");
            }

            string folder = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                Directory.CreateDirectory(folder);
            }

            TrackRoutingData data = new TrackRoutingData
            {
                Tracks = _records.Values
                    .OrderBy(record => record.TrackId, StringComparer.Ordinal)
                    .ToList()
            };
            string temporaryPath = _path + ".tmp";
            string backupPath = _path + ".bak";
            File.WriteAllText(
                temporaryPath,
                JsonConvert.SerializeObject(data, Formatting.Indented),
                new UTF8Encoding(false));
            try
            {
                if (File.Exists(_path))
                {
                    File.Copy(_path, backupPath, true);
                    File.Copy(temporaryPath, _path, true);
                    File.Delete(temporaryPath);
                }
                else
                {
                    File.Move(temporaryPath, _path);
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

        private static TrackRoute Sanitize(TrackRoute routes)
        {
            return routes & TrackRoute.All;
        }

        private static string GetConfigPath()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(BepInEx.Paths.ConfigPath))
                {
                    return BepInEx.Paths.ConfigPath;
                }
            }
            catch
            {
            }

            return AppDomain.CurrentDomain.BaseDirectory;
        }
    }
}
