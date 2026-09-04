namespace SoulPlayer.Audio
{
    // No engine inspection before an outcome; coalesce all consumers in one frame.
    internal sealed class RaidReadinessPollGate
    {
        private int _lastFrame = -1;
        internal int Inspections { get; private set; }
        internal bool ShouldInspect(bool outcomePending, int frame)
        {
            if (!outcomePending || frame == _lastFrame) return false;
            _lastFrame = frame;
            Inspections++;
            SoulPlayer.Utils.RecurringWorkProfiler.Mark(SoulPlayer.Utils.RecurringWorkEvent.ReadinessInspection);
            return true;
        }
        internal void Invalidate() { _lastFrame = -1; }
    }
}
