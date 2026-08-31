using System;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace SoulPlayer.Recorder
{
    internal enum SoulRecorderInputAction { None, StartStop, NextCassette }

    // The sole recorder key decision path. Delegates keep input policy testable
    // without an EFT/Unity process and are never polled while UI owns input.
    internal sealed class SoulRecorderInput
    {
        private bool _warnedConflict;
        private bool _haveBindings;
        private KeyboardShortcut _startBinding;
        private KeyboardShortcut _nextBinding;
        private bool _sameShortcut;
        private int _bindingRevision = -1;
        private KeyCode[] _startModifiers, _nextModifiers;
        internal int SignatureBuildCount { get; private set; }

        internal SoulRecorderInputAction Poll(
            KeyboardShortcut startStop, KeyboardShortcut next,
            bool inRaid, bool configurationManagerOpen, bool uiCapturing,
            Func<KeyCode, bool> keyDown, Func<KeyCode, bool> keyHeld,
            Action<string> warn, int bindingRevision = -1)
        {
#if SOULPLAYER_PERF
            SoulPlayer.Utils.RecurringWorkProfiler.Begin(SoulPlayer.Utils.RecurringWorkArea.Input);
            try
            {
#endif
            if (!inRaid || configurationManagerOpen || uiCapturing)
                return SoulRecorderInputAction.None;

            // Legacy callers without a revision need comparisons only on a key edge.
            // Runtime uses ConfigEntry change revisions: no Equals/Modifiers/LINQ when idle.
            if (bindingRevision < 0 && !keyDown(startStop.MainKey) && !keyDown(next.MainKey))
                return SoulRecorderInputAction.None;
            if (!_haveBindings || (bindingRevision >= 0 ? bindingRevision != _bindingRevision :
                !startStop.Equals(_startBinding) || !next.Equals(_nextBinding)))
            {
                _bindingRevision = bindingRevision;
                SignatureBuildCount++;
                SoulPlayer.Utils.RecurringWorkProfiler.Mark(SoulPlayer.Utils.RecurringWorkEvent.InputBindingRebuild);
                _startModifiers = startStop.Modifiers.ToArray();
                _nextModifiers = next.Modifiers.ToArray();
                _startBinding = startStop;
                _nextBinding = next;
                _haveBindings = true;
                _warnedConflict = false;
                _sameShortcut = startStop.MainKey != KeyCode.None &&
                    Signature(startStop) == Signature(next);
            }
            bool conflict = _sameShortcut;
            bool startPressed = Pressed(startStop.MainKey, _startModifiers, keyDown, keyHeld);
            bool nextPressed = Pressed(next.MainKey, _nextModifiers, keyDown, keyHeld);
            // Also resolve overlapping modifier combinations deterministically.
            conflict |= startPressed && nextPressed;
            if (conflict && !_warnedConflict)
            {
                _warnedConflict = true;
                if (warn != null)
                    warn("SoulRecorder hotkeys conflict; Start / Stop takes priority over Next cassette. Rebind one in F12.");
            }

            return startPressed ? SoulRecorderInputAction.StartStop :
                nextPressed ? SoulRecorderInputAction.NextCassette :
                SoulRecorderInputAction.None;
        #if SOULPLAYER_PERF
            }
            finally { SoulPlayer.Utils.RecurringWorkProfiler.End(SoulPlayer.Utils.RecurringWorkArea.Input); }
#endif
        }

        private static bool Pressed(KeyCode main, KeyCode[] modifiers,
            Func<KeyCode, bool> keyDown, Func<KeyCode, bool> keyHeld)
        {
            if (main == KeyCode.None || !keyDown(main)) return false;
            for (int i = 0; i < modifiers.Length; i++) if (!keyHeld(modifiers[i])) return false;
            return true;
        }

        private static string Signature(KeyboardShortcut shortcut)
        {
            return ((int)shortcut.MainKey) + ":" + string.Join(",",
                shortcut.Modifiers.Distinct().OrderBy(key => (int)key)
                    .Select(key => ((int)key).ToString()).ToArray());
        }
    }
}
