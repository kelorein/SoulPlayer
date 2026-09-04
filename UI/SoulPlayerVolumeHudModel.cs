using System;
using System.Collections.Generic;

namespace SoulPlayer.UI
{
    public enum SoulPlayerVolumeHudPhase
    {
        Hidden = 0,
        Entering = 1,
        Holding = 2,
        Exiting = 3
    }

    public struct SoulPlayerVolumeHudRect : IEquatable<SoulPlayerVolumeHudRect>
    {
        public float X;
        public float Y;
        public float Width;
        public float Height;
        public bool Equals(SoulPlayerVolumeHudRect other)
        {
            return X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
        }
    }

    public struct SoulPlayerVolumeHudLayoutResult
    {
        public float Scale;
        public SoulPlayerVolumeHudRect Panel;
        public SoulPlayerVolumeHudRect Glyph;
        public SoulPlayerVolumeHudRect Heading;
        public SoulPlayerVolumeHudRect Value;
        public SoulPlayerVolumeHudRect Bar;
    }

    public struct SoulPlayerVolumeHudPlacementContext : IEquatable<SoulPlayerVolumeHudPlacementContext>
    {
        public bool IsInRaid;
        public bool MiniPlayerVisible;
        public SoulPlayerVolumeHudRect MiniPlayer;
        public bool RecorderOverlayVisible;
        public SoulPlayerVolumeHudRect RecorderOverlay;
        public bool RecorderStatusVisible;
        public SoulPlayerVolumeHudRect RecorderStatus;
        public bool DiscoveryOverlayVisible;
        public SoulPlayerVolumeHudRect DiscoveryOverlay;
        public bool Equals(SoulPlayerVolumeHudPlacementContext other)
        {
            return IsInRaid == other.IsInRaid &&
                MiniPlayerVisible == other.MiniPlayerVisible && (!MiniPlayerVisible || MiniPlayer.Equals(other.MiniPlayer)) &&
                RecorderOverlayVisible == other.RecorderOverlayVisible && (!RecorderOverlayVisible || RecorderOverlay.Equals(other.RecorderOverlay)) &&
                RecorderStatusVisible == other.RecorderStatusVisible && (!RecorderStatusVisible || RecorderStatus.Equals(other.RecorderStatus)) &&
                DiscoveryOverlayVisible == other.DiscoveryOverlayVisible && (!DiscoveryOverlayVisible || DiscoveryOverlay.Equals(other.DiscoveryOverlay));
        }
    }

    public struct SoulPlayerVolumeHudFrame
    {
        public SoulPlayerVolumeHudPhase Phase;
        public float Volume;
        public float Alpha;
        public float SlidePixels;
    }

    public static class SoulPlayerVolumeHudLayout
    {
        public const float BaseWidth = 276f;
        public const float BaseHeight = 68f;
        public const float FadeInSeconds = 0.14f;
        public const float HoldSeconds = 0.80f;
        public const float FadeOutSeconds = 0.22f;
        public const float SlideDistance = 8f;
        public const float StackGap = 10f;

        public static SoulPlayerVolumeHudLayoutResult Calculate(
            int screenWidth,
            int screenHeight)
        {
            return Calculate(screenWidth, screenHeight,
                new SoulPlayerVolumeHudPlacementContext());
        }

        public static SoulPlayerVolumeHudLayoutResult Calculate(
            int screenWidth,
            int screenHeight,
            SoulPlayerVolumeHudPlacementContext context)
        {
            float scale = Clamp(screenHeight / 1080f, 0.90f, 1.20f);
            float width = BaseWidth * scale;
            float height = BaseHeight * scale;
            float marginRight = 34f * scale;
            float marginBottom = 42f * scale;
            float x = screenWidth - marginRight - width;
            float y = screenHeight - marginBottom - height;
            SoulPlayerVolumeHudRect candidate = Rect(x, y, width, height);

            y = StackAround(candidate, context, true,
                scale, screenHeight, false).Y;

            return new SoulPlayerVolumeHudLayoutResult
            {
                Scale = scale,
                Panel = Rect(x, y, width, height),
                Glyph = Rect(x + 14f * scale, y + 15f * scale,
                    34f * scale, 38f * scale),
                Heading = Rect(x + 56f * scale, y + 10f * scale,
                    96f * scale, 20f * scale),
                Value = Rect(x + width - 76f * scale, y + 9f * scale,
                    60f * scale, 22f * scale),
                Bar = Rect(x + 57f * scale, y + 40f * scale,
                    width - 75f * scale, 9f * scale)
            };
        }

        public static bool Overlaps(
            SoulPlayerVolumeHudRect first,
            SoulPlayerVolumeHudRect second)
        {
            return first.X < second.X + second.Width &&
                   first.X + first.Width > second.X &&
                   first.Y < second.Y + second.Height &&
                   first.Y + first.Height > second.Y;
        }

        public static string VolumeText(float volume)
        {
            float clamped = Clamp(volume, 0f, 1f);
            if (clamped <= 0.001f)
            {
                return "Muted";
            }
            return Math.Round(clamped * 100f).ToString("0") + "%";
        }

        private static SoulPlayerVolumeHudRect Rect(
            float x,
            float y,
            float width,
            float height)
        {
            return new SoulPlayerVolumeHudRect
            {
                X = x,
                Y = y,
                Width = width,
                Height = height
            };
        }

        public static SoulPlayerVolumeHudRect[] OccupiedRects(
            SoulPlayerVolumeHudPlacementContext context, bool includeMini)
        {
            List<SoulPlayerVolumeHudRect> occupied = new List<SoulPlayerVolumeHudRect>();
            if (includeMini && context.MiniPlayerVisible) occupied.Add(context.MiniPlayer);
            if (context.RecorderOverlayVisible) occupied.Add(context.RecorderOverlay);
            if (context.RecorderStatusVisible) occupied.Add(context.RecorderStatus);
            if (context.DiscoveryOverlayVisible) occupied.Add(context.DiscoveryOverlay);
            return occupied.ToArray();
        }

        // The runtime overload uses four value slots, never List/ToArray.
        public static SoulPlayerVolumeHudRect StackAround(SoulPlayerVolumeHudRect candidate,
            SoulPlayerVolumeHudPlacementContext context, bool includeMini,
            float scale, int screenHeight, bool down)
        {
            float gap = StackGap * scale;
            float margin = Math.Min(24f * scale, Math.Max(0f, screenHeight - candidate.Height));
            int count = (includeMini && context.MiniPlayerVisible ? 1 : 0) +
                (context.RecorderOverlayVisible ? 1 : 0) + (context.RecorderStatusVisible ? 1 : 0) +
                (context.DiscoveryOverlayVisible ? 1 : 0);
            for (int pass = 0; pass <= count; pass++)
            {
                float oldY = candidate.Y;
                for (int i = 0; i < 4; i++)
                {
                    SoulPlayerVolumeHudRect occupied = i == 0 ? (includeMini && context.MiniPlayerVisible ? context.MiniPlayer : default(SoulPlayerVolumeHudRect)) :
                        i == 1 ? (context.RecorderOverlayVisible ? context.RecorderOverlay : default(SoulPlayerVolumeHudRect)) :
                        i == 2 ? (context.RecorderStatusVisible ? context.RecorderStatus : default(SoulPlayerVolumeHudRect)) :
                        (context.DiscoveryOverlayVisible ? context.DiscoveryOverlay : default(SoulPlayerVolumeHudRect));
                    if (occupied.Width <= 0f || occupied.Height <= 0f ||
                        candidate.X >= occupied.X + occupied.Width || candidate.X + candidate.Width <= occupied.X ||
                        candidate.Y >= occupied.Y + occupied.Height + gap || candidate.Y + candidate.Height <= occupied.Y - gap)
                        continue;
                    candidate.Y = down ? occupied.Y + occupied.Height + gap : occupied.Y - gap - candidate.Height;
                }
                candidate.Y = Clamp(candidate.Y, margin, Math.Max(margin, screenHeight - margin - candidate.Height));
                if (candidate.Y == oldY) break;
            }
            return candidate;
        }

        // Shared vertical stacking, rechecking every obstacle after a move so
        // caller order cannot place a panel back over a previously checked one.
        public static SoulPlayerVolumeHudRect StackAround(
            SoulPlayerVolumeHudRect candidate,
            SoulPlayerVolumeHudRect[] occupiedRects,
            float scale,
            int screenHeight,
            bool down)
        {
            float gap = StackGap * scale;
            float margin = Math.Min(24f * scale,
                Math.Max(0f, screenHeight - candidate.Height));
            for (int pass = 0; pass <= occupiedRects.Length; pass++)
            {
                float oldY = candidate.Y;
                foreach (SoulPlayerVolumeHudRect occupied in occupiedRects)
                {
                    if (occupied.Width <= 0f || occupied.Height <= 0f ||
                        candidate.X >= occupied.X + occupied.Width ||
                        candidate.X + candidate.Width <= occupied.X ||
                        candidate.Y >= occupied.Y + occupied.Height + gap ||
                        candidate.Y + candidate.Height <= occupied.Y - gap)
                        continue;
                    candidate.Y = down ? occupied.Y + occupied.Height + gap :
                        occupied.Y - gap - candidate.Height;
                }
                candidate.Y = Clamp(candidate.Y, margin,
                    Math.Max(margin, screenHeight - margin - candidate.Height));
                if (candidate.Y == oldY) break;
            }
            return candidate;
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }

    public sealed class SoulPlayerVolumeHudAnimation
    {
        private bool _hasValue;
        private float _volume;
        private float _shownAt;
        private float _hideAt;

        public void Show(float volume, float now)
        {
            SoulPlayerVolumeHudFrame current = Sample(now);
            _volume = Math.Max(0f, Math.Min(1f, volume));
            _shownAt = current.Alpha > 0f
                ? now - SoulPlayerVolumeHudLayout.FadeInSeconds
                : now;
            _hideAt = now + SoulPlayerVolumeHudLayout.HoldSeconds;
            _hasValue = true;
        }

        public SoulPlayerVolumeHudFrame Sample(float now)
        {
            if (!_hasValue)
            {
                return HiddenFrame();
            }

            if (now < _hideAt)
            {
                float alpha = Progress(now - _shownAt,
                    SoulPlayerVolumeHudLayout.FadeInSeconds);
                return Frame(
                    alpha < 1f
                        ? SoulPlayerVolumeHudPhase.Entering
                        : SoulPlayerVolumeHudPhase.Holding,
                    alpha);
            }

            float fade = Progress(now - _hideAt,
                SoulPlayerVolumeHudLayout.FadeOutSeconds);
            float exitingAlpha = 1f - fade;
            if (exitingAlpha <= 0f)
            {
                _hasValue = false;
                return HiddenFrame();
            }
            return Frame(SoulPlayerVolumeHudPhase.Exiting, exitingAlpha);
        }

        public void Reset()
        {
            _hasValue = false;
            _volume = 0f;
            _shownAt = 0f;
            _hideAt = 0f;
        }

        private SoulPlayerVolumeHudFrame Frame(
            SoulPlayerVolumeHudPhase phase,
            float alpha)
        {
            return new SoulPlayerVolumeHudFrame
            {
                Phase = phase,
                Volume = _volume,
                Alpha = alpha,
                SlidePixels = (1f - alpha) *
                    SoulPlayerVolumeHudLayout.SlideDistance
            };
        }

        private static SoulPlayerVolumeHudFrame HiddenFrame()
        {
            return new SoulPlayerVolumeHudFrame
            {
                Phase = SoulPlayerVolumeHudPhase.Hidden,
                Alpha = 0f,
                SlidePixels = SoulPlayerVolumeHudLayout.SlideDistance
            };
        }

        private static float Progress(float elapsed, float duration)
        {
            if (duration <= 0f)
            {
                return 1f;
            }
            return Math.Max(0f, Math.Min(1f, elapsed / duration));
        }
    }
}
