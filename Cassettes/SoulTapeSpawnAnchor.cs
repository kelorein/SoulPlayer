using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace SoulPlayer.Cassettes
{
    internal enum SoulTapeSpawnAnchorSource
    {
        Curated = 0,
        AutomaticLooseLootFallback = 1
    }

    internal sealed class SoulTapeVector3
    {
        public SoulTapeVector3()
        {
        }

        internal SoulTapeVector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        [JsonProperty("x")]
        public float X { get; set; }

        [JsonProperty("y")]
        public float Y { get; set; }

        [JsonProperty("z")]
        public float Z { get; set; }

        internal bool IsFinite
        {
            get
            {
                return !float.IsNaN(X) && !float.IsInfinity(X) &&
                       !float.IsNaN(Y) && !float.IsInfinity(Y) &&
                       !float.IsNaN(Z) && !float.IsInfinity(Z);
            }
        }

        internal float SqrMagnitude { get { return X * X + Y * Y + Z * Z; } }
    }

    /// <summary>
    /// Author-controlled placement data. It contains no EFT asset references and
    /// can be edited or reviewed without loading the game.
    /// </summary>
    internal sealed class SoulTapeSpawnAnchor
    {
        public SoulTapeSpawnAnchor()
        {
            Id = string.Empty;
            MapId = string.Empty;
            SemanticTag = string.Empty;
            Position = new SoulTapeVector3();
            RotationEuler = new SoulTapeVector3();
            SurfaceNormal = new SoulTapeVector3(0f, 1f, 0f);
            Source = SoulTapeSpawnAnchorSource.Curated;
            Enabled = true;
        }

        [JsonProperty("id", Order = 1)]
        public string Id { get; set; }

        [JsonProperty("mapId", Order = 2)]
        public string MapId { get; set; }

        [JsonProperty("semanticTag", Order = 3)]
        public string SemanticTag { get; set; }

        [JsonProperty("position", Order = 4)]
        public SoulTapeVector3 Position { get; set; }

        [JsonProperty("rotationEuler", Order = 5)]
        public SoulTapeVector3 RotationEuler { get; set; }

        [JsonProperty("surfaceNormal", Order = 6)]
        public SoulTapeVector3 SurfaceNormal { get; set; }

        [JsonProperty("source", Order = 7)]
        [JsonConverter(typeof(StringEnumConverter))]
        public SoulTapeSpawnAnchorSource Source { get; set; }

        [JsonProperty("enabled", Order = 8)]
        public bool Enabled { get; set; }
    }

    internal sealed class SoulTapeSpawnAnchorDocument
    {
        internal const int CurrentVersion = 1;

        public SoulTapeSpawnAnchorDocument()
        {
            Version = CurrentVersion;
            Anchors = new List<SoulTapeSpawnAnchor>();
        }

        [JsonProperty("version", Order = 1)]
        public int Version { get; set; }

        [JsonProperty("anchors", Order = 2)]
        public List<SoulTapeSpawnAnchor> Anchors { get; set; }
    }
}
