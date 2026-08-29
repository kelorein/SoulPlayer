using System;

namespace SoulPlayer.UI
{
    public enum SoulPlayerVolumeHudPhase
    {
        Hidden = 0,
        Entering = 1,
        Holding = 2,
        Exiting = 3
    }

    public struct SoulPlayerVolumeHudRect
    {
        public float X;
        public float Y;
        public float Width;
        public float Height;
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

    public struct SoulPlayerVolumeHudPlacementContext
    {
        public bool IsInRaid;
        public bool MiniPlayerVisible;
        public SoulPlayerVolumeHudRect MiniPlayer;
        public bool RecorderOverlayVisible;
        public SoulPlayerVolumeHudRect RecorderOverlay;
        public bool DiscoveryOverlayVisible;
        public SoulPlayerVolumeHudRect DiscoveryOverlay;
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

            if (context.MiniPlayerVisible)
            {
                y = StackAbove(candidate, context.MiniPlayer,
                    y, scale, screenHeight);
                candidate.Y = y;
            }
            if (context.RecorderOverlayVisible)
            {
                y = StackAbove(candidate, context.RecorderOverlay,
                    y, scale, screenHeight);
                candidate.Y = y;
            }
            if (context.DiscoveryOverlayVisible)
            {
                y = StackAbove(candidate, context.DiscoveryOverlay,
                    y, scale, screenHeight);
            }

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

        private static float StackAbove(
            SoulPlayerVolumeHudRect candidate,
            SoulPlayerVolumeHudRect occupied,
            float currentY,
            float scale,
            int screenHeight)
        {
            if (occupied.Width <= 0f || occupied.Height <= 0f ||
                candidate.X >= occupied.X + occupied.Width ||
                candidate.X + candidate.Width <= occupied.X)
            {
                return currentY;
            }

            float gap = StackGap * scale;
            if (candidate.Y >= occupied.Y + occupied.Height + gap ||
                candidate.Y + candidate.Height <= occupied.Y - gap)
            {
                return currentY;
            }
            float stackedY = occupied.Y - gap - candidate.Height;
            float topMargin = Math.Min(24f * scale,
                Math.Max(0f, screenHeight - candidate.Height));
            return Math.Max(topMargin, Math.Min(currentY, stackedY));
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
