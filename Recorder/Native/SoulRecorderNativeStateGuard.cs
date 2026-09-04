using System;
using System.Collections.Generic;

namespace SoulPlayer.Recorder.Native
{
    internal interface ISoulRecorderNativeStateHandle
    {
        string Key { get; }
        object CaptureState();
        void RestoreState(object state);
    }

    internal sealed class SoulRecorderNativeStateGuard
    {
        private sealed class Entry
        {
            internal ISoulRecorderNativeStateHandle Handle;
            internal object State;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly HashSet<string> _keys =
            new HashSet<string>(StringComparer.Ordinal);

        internal int Count { get { return _entries.Count; } }

        internal bool CaptureOnce(ISoulRecorderNativeStateHandle handle)
        {
            if (handle == null || string.IsNullOrWhiteSpace(handle.Key) ||
                _keys.Contains(handle.Key))
            {
                return false;
            }
            object state = handle.CaptureState();
            _keys.Add(handle.Key);
            _entries.Add(new Entry
            {
                Handle = handle,
                State = state
            });
            return true;
        }

        internal IReadOnlyList<Exception> RestoreAll()
        {
            List<Exception> failures = new List<Exception>();
            for (int index = _entries.Count - 1; index >= 0; index--)
            {
                Entry entry = _entries[index];
                try
                {
                    entry.Handle.RestoreState(entry.State);
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }
            }
            _entries.Clear();
            _keys.Clear();
            return failures;
        }
    }
}
