using System;

namespace SoulPlayer.Recorder
{
    public enum SoulRecorderStatusOverlayState
    {
        Hidden = 0,
        PreparingAudio = 1,
        InsertingCassette = 2,
        Ready = 3,
        Playing = 4,
        Ejecting = 5
    }

    public struct SoulRecorderStatusOverlayRect
    {
        public float X;
        public float Y;
        public float Width;
        public float Height;
    }

    public struct SoulRecorderStatusOverlayLayoutResult
    {
        public float Scale;
        public SoulRecorderStatusOverlayRect Panel;
        public SoulRecorderStatusOverlayRect Heading;
        public SoulRecorderStatusOverlayRect Track;
        public SoulRecorderStatusOverlayRect Status;
        public SoulRecorderStatusOverlayRect ProgressTrack;
    }

    public struct SoulRecorderStatusOverlayFrame
    {
        public SoulRecorderStatusOverlayState State;
        public float Alpha;
        public float SlidePixels;
    }

    /// <summary>
    /// Pure screen-space layout and transition math shared by runtime IMGUI and
    /// the Unity preview builder. Coordinates use a top-left origin.
    /// </summary>
    public static class SoulRecorderStatusOverlayLayout
    {
        public const float BaseWidth = 404f;
        public const float BaseHeight = 88f;
        public const float BaseTopFraction = 0.70f;
        public const float FadeInSeconds = 0.16f;
        public const float FadeOutSeconds = 0.22f;
        public const float SlideDistance = 7f;
        public const float ProgressSegmentFraction = 0.31f;

        public static SoulRecorderStatusOverlayLayoutResult Calculate(
            int screenWidth,
            int screenHeight)
        {
            return Calculate(screenWidth, screenHeight, BaseTopFraction);
        }

        public static SoulRecorderStatusOverlayLayoutResult Calculate(
            int screenWidth,
            int screenHeight,
            float topFraction)
        {
            float scale = Clamp(screenHeight / 1080f, 0.90f, 1.20f);
            float width = BaseWidth * scale;
            float height = BaseHeight * scale;
            float x = (screenWidth - width) * 0.5f;
            float y = screenHeight * Clamp(topFraction, 0f, 0.90f);
            float padding = 16f * scale;

            return new SoulRecorderStatusOverlayLayoutResult
            {
                Scale = scale,
                Panel = Rect(x, y, width, height),
                Heading = Rect(x + padding, y + 8f * scale,
                    width - padding * 2f, 17f * scale),
                Track = Rect(x + padding, y + 25f * scale,
                    width - padding * 2f, 27f * scale),
                Status = Rect(x + padding, y + 54f * scale,
                    width - padding * 2f, 16f * scale),
                ProgressTrack = Rect(x + padding, y + 76f * scale,
                    width - padding * 2f, 2f * scale)
            };
        }

        public static string StatusText(SoulRecorderStatusOverlayState state)
        {
            switch (state)
            {
                case SoulRecorderStatusOverlayState.PreparingAudio:
                    return "PREPARING AUDIO";
                case SoulRecorderStatusOverlayState.InsertingCassette:
                    return "INSERTING CASSETTE";
                case SoulRecorderStatusOverlayState.Ready:
                    return "READY";
                case SoulRecorderStatusOverlayState.Playing:
                    return "PLAYING";
                case SoulRecorderStatusOverlayState.Ejecting:
                    return "EJECTING";
                default:
                    return string.Empty;
            }
        }

        public static bool ShowsProgress(
            SoulRecorderStatusOverlayState state)
        {
            return state == SoulRecorderStatusOverlayState.PreparingAudio ||
                   state == SoulRecorderStatusOverlayState.InsertingCassette;
        }

        public static float IndeterminateProgressOffset(float unscaledTime)
        {
            float value = unscaledTime * 0.72f;
            return value - (float)Math.Floor(value);
        }

        private static SoulRecorderStatusOverlayRect Rect(
            float x,
            float y,
            float width,
            float height)
        {
            return new SoulRecorderStatusOverlayRect
            {
                X = x,
                Y = y,
                Width = width,
                Height = height
            };
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }

    public sealed class SoulRecorderStatusOverlayAnimation
    {
        private SoulRecorderStatusOverlayState _desired;
        private SoulRecorderStatusOverlayState _visible;
        private float _changedAt;

        public SoulRecorderStatusOverlayFrame Sample(
            SoulRecorderStatusOverlayState desired,
            float now)
        {
            if (desired != _desired)
            {
                bool wasVisible = _desired !=
                    SoulRecorderStatusOverlayState.Hidden;
                _desired = desired;
                _changedAt = now;
                if (desired != SoulRecorderStatusOverlayState.Hidden)
                {
                    _visible = desired;
                    // A lifecycle label change must not flash the whole panel.
                    // Only Hidden -> visible performs the entrance fade.
                    if (wasVisible)
                    {
                        _changedAt = now -
                            SoulRecorderStatusOverlayLayout.FadeInSeconds;
                    }
                }
            }

            float elapsed = Math.Max(0f, now - _changedAt);
            float alpha;
            if (_desired == SoulRecorderStatusOverlayState.Hidden)
            {
                alpha = 1f - Progress(elapsed,
                    SoulRecorderStatusOverlayLayout.FadeOutSeconds);
                if (alpha <= 0f)
                {
                    _visible = SoulRecorderStatusOverlayState.Hidden;
                }
            }
            else
            {
                alpha = Progress(elapsed,
                    SoulRecorderStatusOverlayLayout.FadeInSeconds);
            }

            return new SoulRecorderStatusOverlayFrame
            {
                State = _visible,
                Alpha = alpha,
                SlidePixels = (1f - alpha) *
                    SoulRecorderStatusOverlayLayout.SlideDistance
            };
        }

        public void Reset()
        {
            _desired = SoulRecorderStatusOverlayState.Hidden;
            _visible = SoulRecorderStatusOverlayState.Hidden;
            _changedAt = 0f;
        }

        private static float Progress(float elapsed, float duration)
        {
            if (duration <= 0f)
            {
                return 1f;
            }
            float value = elapsed / duration;
            return Math.Max(0f, Math.Min(1f, value));
        }
    }
}
