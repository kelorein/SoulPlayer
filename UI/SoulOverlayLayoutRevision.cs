namespace SoulPlayer.UI
{
    // A cheap value-key check, not a layout pass. Hidden bounds do not dirty it.
    internal sealed class SoulOverlayLayoutRevision
    {
        private bool _valid;
        private int _width, _height, _position;
        private float _scale;
        private SoulPlayerVolumeHudPlacementContext _context;
        internal int RebuildCount { get; private set; }

        internal void Invalidate() { _valid = false; }

        internal bool ShouldRebuild(bool visible, int width, int height, float scale,
            int position, SoulPlayerVolumeHudPlacementContext context)
        {
            if (!visible) { _valid = false; return false; }
            if (_valid && width == _width && height == _height && scale == _scale &&
                position == _position && _context.Equals(context)) return false;
            _valid = true;
            _width = width; _height = height; _scale = scale; _position = position; _context = context;
            RebuildCount++;
            SoulPlayer.Utils.RecurringWorkProfiler.Mark(SoulPlayer.Utils.RecurringWorkEvent.LayoutRebuild);
            return true;
        }
    }
}
