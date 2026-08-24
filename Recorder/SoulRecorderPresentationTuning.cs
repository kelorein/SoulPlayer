using UnityEngine;

namespace SoulPlayer.Recorder
{
    /// <summary>
    /// Central prototype tuning. Values intentionally remain code-owned until the
    /// original recorder and hand art replaces this procedural presentation.
    /// </summary>
    internal static class SoulRecorderPresentationTuning
    {
        internal const float EnterSeconds = 0.25f;
        internal const float ExitSeconds = 0.22f;
        internal const float TapeInsertionSeconds = 0.62f;
        internal const float TapeEjectionSeconds = 0.55f;
        internal const float PlaybackVisibleSeconds = 1.0f;
        internal const float AutoLowerSeconds = 0.25f;
        internal const float RaiseForEjectSeconds = 0.23f;
        internal const float ReelDegreesPerSecond = 210f;

        internal const float RecorderBodyDepth = 0.055f;
        internal const float CassetteBayFrameZ = -0.044f;
        internal const float CassetteBayFrameDepth = 0.006f;
        internal const float CassetteBayOpeningWidth = 0.102f;
        internal const float CassetteBayOpeningHeight = 0.052f;

        internal static readonly Vector3 HeldPosition = new Vector3(0.19f, -0.17f, 0.47f);
        internal static readonly Vector3 HeldRotationEuler = new Vector3(6f, -14f, 4f);
        internal static readonly Vector3 RecorderScale = new Vector3(0.92f, 0.92f, 0.92f);
        internal static readonly Vector3 LoweredOffset = new Vector3(0.08f, -0.34f, -0.04f);

        internal static readonly Vector3 CassetteInsertionStartPosition =
            new Vector3(0.13f, -0.12f, -0.105f);
        internal static readonly Vector3 CassetteInsertionEndPosition =
            new Vector3(0f, 0.018f, -0.0305f);
        internal static readonly Vector3 CassetteInsertionStartRotationEuler =
            new Vector3(-72f, 24f, 18f);
        internal static readonly Vector3 CassetteInsertionEndRotationEuler =
            new Vector3(-90f, 0f, 0f);
    }
}
