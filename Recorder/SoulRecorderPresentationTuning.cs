using UnityEngine;

namespace SoulPlayer.Recorder
{
    /// <summary>
    /// Central first-person tuning. Import correction stays inside packaged prefabs;
    /// these values only control camera/recorder-local presentation and transport.
    /// </summary>
    internal static class SoulRecorderPresentationTuning
    {
        internal const string PresentationRootName =
            "SoulRecorder First-Person Presentation";
        internal const float EnterSeconds = 0.38f;
        internal const float ExitSeconds = 0.34f;
        internal const float CassetteInsertionLeadInSeconds = 0.28f;
        internal const float CassetteInsertionMotionSeconds = 0.72f;
        internal const float CassetteInsertionSettleSeconds = 0.12f;
        internal const float TapeInsertionSeconds =
            CassetteInsertionLeadInSeconds + CassetteInsertionMotionSeconds +
            CassetteInsertionSettleSeconds;
        internal const float StopEjectionLeadInSeconds = 0.34f;
        internal const float InterruptedEjectionLeadInSeconds = 0.05f;
        internal const float CassetteEjectionMotionSeconds = 0.68f;
        internal const float TapeEjectionSeconds =
            StopEjectionLeadInSeconds + CassetteEjectionMotionSeconds;
        internal const float ReelDegreesPerSecond = 210f;

        internal const float RecorderBodyDepth = 0.055f;
        internal const float CassetteBayFrameZ = -0.044f;
        internal const float CassetteBayFrameDepth = 0.006f;
        internal const float CassetteBayOpeningWidth = 0.102f;
        internal const float CassetteBayOpeningHeight = 0.052f;

        internal const float CassetteAlignmentProgress = 0.54f;
        internal const float CassetteHandFollow = 0.94f;
        internal const float CassetteHandRotationFollow = 0.82f;
        internal const float RecorderEnterSettleStart = 0.72f;
        internal const float CassetteContactSettleMillimeters = 3.0f;

        // The articulated presentation is staged like Tarkov's normal first-person
        // hands: close enough to read, slightly left of centre, and low enough that
        // both sleeves imply shoulders below the camera rather than detached arms.
        internal static readonly Vector3 HeldPosition = new Vector3(0.060f, -1.460f, 0.360f);
        internal static readonly Vector3 HeldRotationEuler = new Vector3(6f, -4f, -2f);
        internal static readonly Vector3 RecorderScale = new Vector3(0.86f, 0.86f, 0.86f);
        internal static readonly Vector3 OffscreenOffset = new Vector3(0.11f, -0.36f, -0.04f);
        internal static readonly Vector3 EnterSettleOffset = new Vector3(-0.008f, 0.012f, -0.006f);
        internal static readonly Vector3 ExitRotationOffsetEuler = new Vector3(18f, 3f, 8f);
        internal static readonly Vector3 PrefabLocalPosition = Vector3.zero;
        internal static readonly Vector3 PrefabLocalRotationEuler = Vector3.zero;
        internal static readonly Vector3 PrefabLocalScale = Vector3.one;
        internal static readonly Vector3 HandsLocalPosition = Vector3.zero;
        internal static readonly Vector3 HandsLocalRotationEuler = Vector3.zero;
        internal static readonly Vector3 HandsLocalScale = Vector3.one;
        internal static readonly Vector3 AnimatedRecorderGripRotationEuler =
            new Vector3(67.42075f, 198.49710f, 225.84430f);
        internal static readonly Vector3 AnimatedRecorderGripPosition =
            new Vector3(-0.00667f, -0.01765f, -0.00283f);
        internal static readonly Vector3 AnimatedCassetteGripRotationEuler =
            new Vector3(18.48994f, 85.46006f, 57.73771f);
        // Native EFT hand tuning. Targets are camera-local; wrist rotations are small
        // offsets composed after the empty-hands wrist baselines captured at runtime.
        internal static readonly Vector3 NativeSupportTargetPosition =
            new Vector3(-0.120f, -0.175f, 0.405f);
        internal static readonly Vector3 NativeCassetteCarryTargetPosition =
            new Vector3(0.135f, -0.180f, 0.395f);
        internal static readonly Vector3 NativeSupportHiddenOffset =
            new Vector3(-0.050f, -0.320f, -0.035f);
        internal static readonly Vector3 NativeCassetteHiddenOffset =
            new Vector3(0.065f, -0.335f, -0.035f);
        internal static readonly Vector3 NativeSupportWristRelativeRotationEuler =
            new Vector3(4f, -8f, 8f);
        internal static readonly Vector3 NativeCassetteWristRelativeRotationEuler =
            new Vector3(3f, 10f, -10f);
        internal static readonly Vector3 NativeSupportElbowGoal =
            new Vector3(-0.300f, -0.255f, 0.235f);
        internal static readonly Vector3 NativeCassetteElbowGoal =
            new Vector3(0.320f, -0.265f, 0.240f);

        // These sockets are palm-relative, not wrist-relative. Keeping the model close
        // to the palm avoids the detached 5-6 cm wrist standoff seen in runtime footage.
        internal static readonly Vector3 NativeRecorderGripPosition =
            new Vector3(-0.010f, 0.006f, 0.022f);
        internal static readonly Vector3 NativeRecorderGripRotationEuler =
            new Vector3(72f, 188f, 214f);
        internal static readonly Vector3 NativeCassetteGripPosition =
            new Vector3(0.004f, 0.004f, 0.016f);
        // This is the sole native cassette orientation. The cassette model remains
        // identity-relative to this wrist socket, so BAMEN's grip correction is not
        // composed on top of the native calibration.
        internal static readonly Vector3 NativeCassetteGripRotationEuler =
            new Vector3(-10f, 62f, 18f);
        internal static readonly Vector3 NativeRecorderModelPosition = Vector3.zero;
        internal static readonly Vector3 NativeRecorderModelRotationEuler = Vector3.zero;
        // The packaged recorder was authored at real-world scale. A small reduction
        // keeps it inside the native rangefinder grip instead of inheriting the
        // donor mesh transform/scale (which varied with the renderer hierarchy).
        internal static readonly Vector3 NativeRecorderModelScale =
            new Vector3(0.82f, 0.82f, 0.82f);
        internal static readonly Vector3 NativeCassetteModelPosition = Vector3.zero;
        internal static readonly Vector3 NativeCassetteModelRotationEuler = Vector3.zero;
        internal static readonly Vector3 NativeCassetteModelScale = Vector3.one;
        internal static readonly Vector3 HoldingHandBraceOffset =
            new Vector3(-0.002f, 0.001f, 0.002f);
        internal static readonly Vector3 CassetteHandExteriorRotationOffsetEuler =
            new Vector3(8f, -6f, 5f);

        internal static readonly Vector3 CassetteInsertionStartPosition =
            new Vector3(0.105f, -0.080f, -0.125f);
        internal static readonly Vector3 CassetteAlignmentPosition =
            new Vector3(0.012f, 0.012f, -0.062f);
        internal static readonly Vector3 CassetteInsertionEndPosition =
            new Vector3(0f, 0.018f, -0.008f);
        internal static readonly Vector3 CassetteEjectPosition =
            new Vector3(0.095f, -0.065f, -0.115f);
        internal static readonly Vector3 CassetteInsertionStartRotationEuler =
            new Vector3(-74f, 18f, 12f);
        internal static readonly Vector3 CassetteAlignmentRotationEuler =
            new Vector3(-90f, 0f, 0f);
        internal static readonly Vector3 CassetteInsertionEndRotationEuler =
            new Vector3(-90f, 0f, 0f);
        internal static readonly Vector3 CassetteEjectRotationEuler =
            new Vector3(-78f, 12f, 8f);
    }
}
