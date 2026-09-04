#if SOULPLAYER_PLACEMENT_TOOLS
using System;
using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;

namespace SoulPlayer.Recorder
{
    /// <summary>
    /// Placement-tools-only config bridge for first-person visual tuning. Values
    /// are applied directly by the presentation view; Ctrl+Shift+F7 reloads the
    /// config and emits one concise snapshot for copy-back into source defaults.
    /// </summary>
    internal sealed class DevelopmentSoulRecorderPresentationTuner : MonoBehaviour
    {
        private const string Section = "SoulRecorder Presentation Tools";

        private static ConfigEntry<string> _presentationPosition;
        private static ConfigEntry<string> _presentationRotation;
        private static ConfigEntry<string> _recorderPosition;
        private static ConfigEntry<string> _recorderRotation;
        private static ConfigEntry<string> _handsPosition;
        private static ConfigEntry<string> _handsRotation;
        private static ConfigEntry<string> _cassetteStartPosition;
        private static ConfigEntry<string> _cassetteStartRotation;
        private static ConfigEntry<string> _cassetteAlignmentPosition;
        private static ConfigEntry<string> _cassetteAlignmentRotation;
        private static ConfigEntry<string> _cassetteInsertedPosition;
        private static ConfigEntry<string> _cassetteInsertedRotation;
        private static ConfigEntry<string> _cassetteEjectPosition;
        private static ConfigEntry<string> _cassetteEjectRotation;
        private static ConfigEntry<string> _nativeSupportTargetPosition;
        private static ConfigEntry<string> _nativeCassetteCarryTargetPosition;
        private static ConfigEntry<string> _nativeSupportHiddenOffset;
        private static ConfigEntry<string> _nativeCassetteHiddenOffset;
        private static ConfigEntry<string> _nativeSupportWristRelativeRotation;
        private static ConfigEntry<string> _nativeCassetteWristRelativeRotation;
        private static ConfigEntry<string> _nativeSupportElbowGoal;
        private static ConfigEntry<string> _nativeCassetteElbowGoal;
        private static ConfigEntry<string> _nativeRecorderGripPosition;
        private static ConfigEntry<string> _nativeRecorderGripRotation;
        private static ConfigEntry<string> _nativeRecorderModelScale;
        private static ConfigEntry<string> _nativeCassetteGripPosition;
        private static ConfigEntry<string> _nativeCassetteGripRotation;

        private ConfigFile _config;
        private ConfigEntry<KeyboardShortcut> _reloadHotkey;

        internal void Initialize(ConfigFile config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _presentationPosition = BindVector(
                "Presentation root position",
                SoulRecorderPresentationTuning.HeldPosition);
            _presentationRotation = BindVector(
                "Presentation root rotation",
                SoulRecorderPresentationTuning.HeldRotationEuler);
            _recorderPosition = BindVector(
                "Recorder local position",
                SoulRecorderPresentationTuning.PrefabLocalPosition);
            _recorderRotation = BindVector(
                "Recorder local rotation",
                SoulRecorderPresentationTuning.PrefabLocalRotationEuler);
            _handsPosition = BindVector(
                "Hands local position",
                SoulRecorderPresentationTuning.HandsLocalPosition);
            _handsRotation = BindVector(
                "Hands local rotation",
                SoulRecorderPresentationTuning.HandsLocalRotationEuler);
            _cassetteStartPosition = BindVector(
                "Cassette start position",
                SoulRecorderPresentationTuning.CassetteInsertionStartPosition);
            _cassetteStartRotation = BindVector(
                "Cassette start rotation",
                SoulRecorderPresentationTuning.CassetteInsertionStartRotationEuler);
            _cassetteAlignmentPosition = BindVector(
                "Cassette alignment position",
                SoulRecorderPresentationTuning.CassetteAlignmentPosition);
            _cassetteAlignmentRotation = BindVector(
                "Cassette alignment rotation",
                SoulRecorderPresentationTuning.CassetteAlignmentRotationEuler);
            _cassetteInsertedPosition = BindVector(
                "Cassette inserted position",
                SoulRecorderPresentationTuning.CassetteInsertionEndPosition);
            _cassetteInsertedRotation = BindVector(
                "Cassette inserted rotation",
                SoulRecorderPresentationTuning.CassetteInsertionEndRotationEuler);
            _cassetteEjectPosition = BindVector(
                "Cassette eject position",
                SoulRecorderPresentationTuning.CassetteEjectPosition);
            _cassetteEjectRotation = BindVector(
                "Cassette eject rotation",
                SoulRecorderPresentationTuning.CassetteEjectRotationEuler);
            _nativeSupportTargetPosition = BindVector(
                "Native support target position",
                SoulRecorderPresentationTuning.NativeSupportTargetPosition);
            _nativeCassetteCarryTargetPosition = BindVector(
                "Native cassette carry target position",
                SoulRecorderPresentationTuning.NativeCassetteCarryTargetPosition);
            _nativeSupportHiddenOffset = BindVector(
                "Native support hidden offset",
                SoulRecorderPresentationTuning.NativeSupportHiddenOffset);
            _nativeCassetteHiddenOffset = BindVector(
                "Native cassette hidden offset",
                SoulRecorderPresentationTuning.NativeCassetteHiddenOffset);
            _nativeSupportWristRelativeRotation = BindVector(
                "Native support wrist relative rotation",
                SoulRecorderPresentationTuning.NativeSupportWristRelativeRotationEuler);
            _nativeCassetteWristRelativeRotation = BindVector(
                "Native cassette wrist relative rotation",
                SoulRecorderPresentationTuning.NativeCassetteWristRelativeRotationEuler);
            _nativeSupportElbowGoal = BindVector(
                "Native support elbow goal",
                SoulRecorderPresentationTuning.NativeSupportElbowGoal);
            _nativeCassetteElbowGoal = BindVector(
                "Native cassette elbow goal",
                SoulRecorderPresentationTuning.NativeCassetteElbowGoal);
            _nativeRecorderGripPosition = BindVector(
                "Native recorder palm grip position",
                SoulRecorderPresentationTuning.NativeRecorderGripPosition);
            _nativeRecorderGripRotation = BindVector(
                "Native recorder palm grip rotation",
                SoulRecorderPresentationTuning.NativeRecorderGripRotationEuler);
            _nativeRecorderModelScale = BindVector(
                "Native recorder model scale",
                SoulRecorderPresentationTuning.NativeRecorderModelScale);
            _nativeCassetteGripPosition = BindVector(
                "Native cassette palm grip position",
                SoulRecorderPresentationTuning.NativeCassetteGripPosition);
            _nativeCassetteGripRotation = BindVector(
                "Native cassette palm grip rotation",
                SoulRecorderPresentationTuning.NativeCassetteGripRotationEuler);
            _reloadHotkey = _config.Bind(
                Section,
                "Reload and log hotkey",
                new KeyboardShortcut(
                    KeyCode.F7,
                    KeyCode.LeftControl,
                    KeyCode.LeftShift),
                "Reload the BepInEx config and log the active camera-local tuning values.");
        }

        internal static Vector3 PresentationPosition(Vector3 fallback)
        {
            return Parse(_presentationPosition, fallback);
        }

        internal static Vector3 PresentationRotation(Vector3 fallback)
        {
            return Parse(_presentationRotation, fallback);
        }

        internal static Vector3 RecorderPosition(Vector3 fallback)
        {
            return Parse(_recorderPosition, fallback);
        }

        internal static Vector3 RecorderRotation(Vector3 fallback)
        {
            return Parse(_recorderRotation, fallback);
        }

        internal static Vector3 HandsPosition(Vector3 fallback)
        {
            return Parse(_handsPosition, fallback);
        }

        internal static Vector3 HandsRotation(Vector3 fallback)
        {
            return Parse(_handsRotation, fallback);
        }

        internal static Vector3 CassetteStartPosition(Vector3 fallback)
        {
            return Parse(_cassetteStartPosition, fallback);
        }

        internal static Vector3 CassetteStartRotation(Vector3 fallback)
        {
            return Parse(_cassetteStartRotation, fallback);
        }

        internal static Vector3 CassetteAlignmentPosition(Vector3 fallback)
        {
            return Parse(_cassetteAlignmentPosition, fallback);
        }

        internal static Vector3 CassetteAlignmentRotation(Vector3 fallback)
        {
            return Parse(_cassetteAlignmentRotation, fallback);
        }

        internal static Vector3 CassetteInsertedPosition(Vector3 fallback)
        {
            return Parse(_cassetteInsertedPosition, fallback);
        }

        internal static Vector3 CassetteInsertedRotation(Vector3 fallback)
        {
            return Parse(_cassetteInsertedRotation, fallback);
        }

        internal static Vector3 CassetteEjectPosition(Vector3 fallback)
        {
            return Parse(_cassetteEjectPosition, fallback);
        }

        internal static Vector3 CassetteEjectRotation(Vector3 fallback)
        {
            return Parse(_cassetteEjectRotation, fallback);
        }

        internal static Vector3 NativeSupportTargetPosition(Vector3 fallback)
        {
            return Parse(_nativeSupportTargetPosition, fallback);
        }

        internal static Vector3 NativeCassetteCarryTargetPosition(Vector3 fallback)
        {
            return Parse(_nativeCassetteCarryTargetPosition, fallback);
        }

        internal static Vector3 NativeSupportHiddenOffset(Vector3 fallback)
        {
            return Parse(_nativeSupportHiddenOffset, fallback);
        }

        internal static Vector3 NativeCassetteHiddenOffset(Vector3 fallback)
        {
            return Parse(_nativeCassetteHiddenOffset, fallback);
        }

        internal static Vector3 NativeSupportWristRelativeRotation(Vector3 fallback)
        {
            return Parse(_nativeSupportWristRelativeRotation, fallback);
        }

        internal static Vector3 NativeCassetteWristRelativeRotation(Vector3 fallback)
        {
            return Parse(_nativeCassetteWristRelativeRotation, fallback);
        }

        internal static Vector3 NativeSupportElbowGoal(Vector3 fallback)
        {
            return Parse(_nativeSupportElbowGoal, fallback);
        }

        internal static Vector3 NativeCassetteElbowGoal(Vector3 fallback)
        {
            return Parse(_nativeCassetteElbowGoal, fallback);
        }

        internal static Vector3 NativeRecorderGripPosition(Vector3 fallback)
        {
            return Parse(_nativeRecorderGripPosition, fallback);
        }

        internal static Vector3 NativeRecorderGripRotation(Vector3 fallback)
        {
            return Parse(_nativeRecorderGripRotation, fallback);
        }

        internal static Vector3 NativeRecorderModelScale(Vector3 fallback)
        {
            return Parse(_nativeRecorderModelScale, fallback);
        }

        internal static Vector3 NativeCassetteGripPosition(Vector3 fallback)
        {
            return Parse(_nativeCassetteGripPosition, fallback);
        }

        internal static Vector3 NativeCassetteGripRotation(Vector3 fallback)
        {
            return Parse(_nativeCassetteGripRotation, fallback);
        }

        private ConfigEntry<string> BindVector(string key, Vector3 value)
        {
            return _config.Bind(
                Section,
                key,
                Format(value),
                "Comma-separated camera/recorder-local X,Y,Z. Placement-tools builds only.");
        }

        private void Update()
        {
            if (_reloadHotkey == null || !_reloadHotkey.Value.IsDown())
            {
                return;
            }

            _config.Reload();
            Plugin.Log.LogInfo(
                "SoulRecorder presentation tuning reloaded (camera/local X,Y,Z): " +
                "presentation=" + Format(PresentationPosition(
                    SoulRecorderPresentationTuning.HeldPosition)) +
                ", recorder=" + Format(RecorderPosition(
                    SoulRecorderPresentationTuning.PrefabLocalPosition)) +
                ", hands=" + Format(HandsPosition(
                    SoulRecorderPresentationTuning.HandsLocalPosition)) +
                ", cassette start/alignment/inserted/eject=" +
                Format(CassetteStartPosition(
                    SoulRecorderPresentationTuning.CassetteInsertionStartPosition)) + "/" +
                Format(CassetteAlignmentPosition(
                    SoulRecorderPresentationTuning.CassetteAlignmentPosition)) + "/" +
                Format(CassetteInsertedPosition(
                    SoulRecorderPresentationTuning.CassetteInsertionEndPosition)) + "/" +
                Format(CassetteEjectPosition(
                    SoulRecorderPresentationTuning.CassetteEjectPosition)) +
                ", native targets=" +
                Format(NativeSupportTargetPosition(
                    SoulRecorderPresentationTuning.NativeSupportTargetPosition)) + "/" +
                Format(NativeCassetteCarryTargetPosition(
                    SoulRecorderPresentationTuning.NativeCassetteCarryTargetPosition)) +
                ", native wrist offsets=" +
                Format(NativeSupportWristRelativeRotation(
                    SoulRecorderPresentationTuning.NativeSupportWristRelativeRotationEuler)) +
                "/" + Format(NativeCassetteWristRelativeRotation(
                    SoulRecorderPresentationTuning.NativeCassetteWristRelativeRotationEuler)) +
                ", native elbows=" + Format(NativeSupportElbowGoal(
                    SoulRecorderPresentationTuning.NativeSupportElbowGoal)) + "/" +
                Format(NativeCassetteElbowGoal(
                    SoulRecorderPresentationTuning.NativeCassetteElbowGoal)) +
                ", native palm grips=" + Format(NativeRecorderGripPosition(
                    SoulRecorderPresentationTuning.NativeRecorderGripPosition)) + "/" +
                Format(NativeCassetteGripPosition(
                    SoulRecorderPresentationTuning.NativeCassetteGripPosition)) +
                ", native recorder scale=" + Format(NativeRecorderModelScale(
                    SoulRecorderPresentationTuning.NativeRecorderModelScale)) + ".");
        }

        private static Vector3 Parse(ConfigEntry<string> entry, Vector3 fallback)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Value))
            {
                return fallback;
            }
            string[] parts = entry.Value.Split(',');
            float x;
            float y;
            float z;
            if (parts.Length != 3 ||
                !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) ||
                !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z))
            {
                return fallback;
            }
            return new Vector3(x, y, z);
        }

        private static string Format(Vector3 value)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:F4},{1:F4},{2:F4}",
                value.x,
                value.y,
                value.z);
        }
    }
}
#endif
