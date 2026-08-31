using System;

namespace SoulPlayer.UI
{
    public enum SoulMiniPlayerCorner
    {
        BottomRight = 0,
        BottomLeft = 1,
        TopLeft = 2,
        TopRight = 3
    }

    public static class SoulMiniPlayerLayout
    {
        public const float Width = 380f;
        public const float Height = 44f;

        // Pixel coordinates, top-left origin. Use the live canvas scale rather
        // than guessing it from aspect ratio; the full window is never changed.
        public static SoulPlayerVolumeHudRect Calculate(int screenWidth,
            int screenHeight, float canvasScale, SoulMiniPlayerCorner corner,
            SoulPlayerVolumeHudPlacementContext context)
        {
            float scale = Math.Max(0.01f, canvasScale);
            bool left = corner == SoulMiniPlayerCorner.BottomLeft ||
                corner == SoulMiniPlayerCorner.TopLeft;
            bool top = corner == SoulMiniPlayerCorner.TopLeft ||
                corner == SoulMiniPlayerCorner.TopRight;
            float width = Math.Min(Width * scale, screenWidth - 24f * scale);
            float height = Height * scale;
            SoulPlayerVolumeHudRect panel = new SoulPlayerVolumeHudRect
            {
                X = left ? 12f * scale : screenWidth - 12f * scale - width,
                Y = top ? 36f * scale : screenHeight - 36f * scale - height,
                Width = width,
                Height = height
            };
            return SoulPlayerVolumeHudLayout.StackAround(panel, context, false,
                scale, screenHeight, top);
        }
    }
}
