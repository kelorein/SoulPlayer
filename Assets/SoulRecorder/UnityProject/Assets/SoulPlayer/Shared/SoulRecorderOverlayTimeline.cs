using System;

namespace SoulPlayer.Recorder
{
    public enum SoulRecorderOverlayState
    {
        Hidden = 0, Idle = 1, InsertEnter = 2, InsertRotate = 3,
        InsertApproach = 4, InsertHalf = 5, InsertSeated = 6, Playing = 7,
        EjectStart = 8, EjectPop = 9, EjectClear = 10, EjectExit = 11
    }

    public enum SoulRecorderOverlayCorner
    {
        BottomRight = 0
    }

    public struct SoulRecorderOverlaySettings
    {
        public SoulRecorderOverlayCorner Corner;
        public float Scale;
        public float HorizontalOffset;
        public float VerticalOffset;
        public float AnimationSpeed;

        public static SoulRecorderOverlaySettings Default
        {
            get
            {
                return new SoulRecorderOverlaySettings
                {
                    Corner = SoulRecorderOverlayCorner.BottomRight,
                    Scale = 0.78f,
                    AnimationSpeed = 1f
                };
            }
        }
    }

    public struct SoulRecorderOverlayRect
    {
        public float X, Y, Width, Height;
        public float CenterX { get { return X + Width * 0.5f; } }
        public float CenterY { get { return Y + Height * 0.5f; } }
    }

    public struct SoulRecorderOverlayPose
    {
        public SoulRecorderOverlayState State;
        public SoulRecorderOverlayRect Recorder;
        public SoulRecorderOverlayRect Cassette;
        public float CassetteRotationDegrees;
        public float RecorderAlpha;
        public float CassetteAlpha;
        public float RecorderScale;
        public bool LedOn;
    }

    /// <summary>
    /// Resolution-independent choreography shared verbatim by runtime IMGUI and
    /// the Unity offline preview renderer. Coordinates use a top-left origin.
    /// </summary>
    public static class SoulRecorderOverlayTimeline
    {
        public const float InsertEnterSeconds = 0.28f;
        public const float InsertRotateSeconds = 0.36f;
        public const float InsertApproachSeconds = 0.28f;
        public const float InsertHalfSeconds = 0.16f;
        public const float InsertSeatedSeconds = 0.16f;
        public const float EjectStartSeconds = 0.24f;
        public const float EjectPopSeconds = 0.16f;
        public const float EjectClearSeconds = 0.28f;
        public const float EjectExitSeconds = 0.33f;
        public const float PresentationFadeSeconds = 0.26f;
        public const float RecorderWidthFraction = 0.20f;
        public const float RecorderAspect = 1.45f;
        public const float CassetteWidthFractionOfRecorder = 0.58f;
        public const float CassetteAspect = 0.64f;
        public const float BaseMarginPixels = 40f;
        public const float StartCassetteRotation = 31f;

        public static float InsertionSeconds(float speed)
        {
            return (InsertEnterSeconds + InsertRotateSeconds +
                InsertApproachSeconds + InsertHalfSeconds +
                InsertSeatedSeconds) / SafeSpeed(speed);
        }

        public static float EjectionMotionSeconds(float speed)
        {
            return (EjectStartSeconds + EjectPopSeconds +
                EjectClearSeconds) / SafeSpeed(speed);
        }

        public static float EjectionTotalSeconds(float speed)
        {
            return (EjectStartSeconds + EjectPopSeconds +
                EjectClearSeconds + EjectExitSeconds) / SafeSpeed(speed);
        }

        public static float EjectionAudioStopSeconds(float speed)
        {
            return (EjectStartSeconds + EjectPopSeconds) / SafeSpeed(speed);
        }

        public static SoulRecorderOverlayPose SampleInsertion(int width,
            int height, float elapsed, SoulRecorderOverlaySettings settings)
        {
            float time = Math.Max(0f, elapsed) * SafeSpeed(settings.AnimationSpeed);
            SoulRecorderOverlayPose pose = BasePose(width, height, settings);
            SoulRecorderOverlayRect recorder = pose.Recorder;
            SoulRecorderOverlayRect start = CassetteAt(recorder, 1.04f, 0.77f);
            SoulRecorderOverlayRect rotate = CassetteAt(recorder, 0.93f, 0.64f);
            SoulRecorderOverlayRect approach = CassetteAt(recorder, 0.69f, 0.575f);
            SoulRecorderOverlayRect half = CassetteAt(recorder, 0.575f, 0.575f);
            SoulRecorderOverlayRect seated = CassetteAt(recorder, 0.49f, 0.575f);

            if (time < InsertEnterSeconds)
            {
                float p = Smooth(time / InsertEnterSeconds);
                pose.State = SoulRecorderOverlayState.InsertEnter;
                pose.Recorder.Y += Lerp(34f, 0f, p);
                pose.RecorderAlpha = p;
                pose.Cassette = Offset(start, recorder.Width * 0.16f,
                    recorder.Height * 0.07f);
                pose.CassetteAlpha = Smooth(Clamp01((p - 0.28f) / 0.72f));
                pose.CassetteRotationDegrees = StartCassetteRotation;
                pose.RecorderScale = 0.985f + 0.015f * p;
                return pose;
            }
            time -= InsertEnterSeconds;
            if (time < InsertRotateSeconds)
            {
                float p = Smooth(time / InsertRotateSeconds);
                pose.State = SoulRecorderOverlayState.InsertRotate;
                pose.Cassette = Lerp(start, rotate, p);
                pose.CassetteRotationDegrees = Lerp(StartCassetteRotation, 0f, p);
                return pose;
            }
            time -= InsertRotateSeconds;
            if (time < InsertApproachSeconds)
            {
                float p = Smooth(time / InsertApproachSeconds);
                pose.State = SoulRecorderOverlayState.InsertApproach;
                pose.Cassette = Lerp(rotate, approach, p);
                return pose;
            }
            time -= InsertApproachSeconds;
            if (time < InsertHalfSeconds)
            {
                float p = Smooth(time / InsertHalfSeconds);
                pose.State = SoulRecorderOverlayState.InsertHalf;
                pose.Cassette = Lerp(approach, half, p);
                pose.Recorder.X += Lerp(0f, -2f, Settle(p));
                return pose;
            }
            time -= InsertHalfSeconds;
            if (time < InsertSeatedSeconds)
            {
                float p = Smooth(time / InsertSeatedSeconds);
                pose.State = SoulRecorderOverlayState.InsertSeated;
                pose.Cassette = Lerp(half, seated, p);
                pose.Recorder.X += Lerp(-2f, 0f, p);
                pose.LedOn = p >= 0.72f;
                return pose;
            }
            pose.State = SoulRecorderOverlayState.Playing;
            pose.Cassette = seated;
            pose.LedOn = true;
            return pose;
        }

        public static SoulRecorderOverlayPose SampleEjection(int width,
            int height, float elapsed, SoulRecorderOverlaySettings settings)
        {
            float time = Math.Max(0f, elapsed) * SafeSpeed(settings.AnimationSpeed);
            SoulRecorderOverlayPose pose = BasePose(width, height, settings);
            SoulRecorderOverlayRect recorder = pose.Recorder;
            SoulRecorderOverlayRect seated = CassetteAt(recorder, 0.49f, 0.575f);
            SoulRecorderOverlayRect half = CassetteAt(recorder, 0.575f, 0.575f);
            SoulRecorderOverlayRect clear = CassetteAt(recorder, 0.93f, 0.64f);
            SoulRecorderOverlayRect exit = Offset(CassetteAt(recorder, 1.04f, 0.77f),
                recorder.Width * 0.16f, recorder.Height * 0.07f);

            if (time < EjectStartSeconds)
            {
                float p = Smooth(time / EjectStartSeconds);
                pose.State = SoulRecorderOverlayState.EjectStart;
                pose.Recorder.Y += Lerp(28f, 0f, p);
                pose.RecorderAlpha = p;
                pose.Cassette = seated;
                pose.CassetteAlpha = p;
                pose.LedOn = p < 0.55f;
                return pose;
            }
            time -= EjectStartSeconds;
            if (time < EjectPopSeconds)
            {
                float p = Smooth(time / EjectPopSeconds);
                pose.State = SoulRecorderOverlayState.EjectPop;
                pose.Cassette = Lerp(seated, half, p);
                pose.Recorder.X += Lerp(0f, 2f, Settle(p));
                return pose;
            }
            time -= EjectPopSeconds;
            if (time < EjectClearSeconds)
            {
                float p = Smooth(time / EjectClearSeconds);
                pose.State = SoulRecorderOverlayState.EjectClear;
                pose.Cassette = Lerp(half, clear, p);
                return pose;
            }
            time -= EjectClearSeconds;
            if (time < EjectExitSeconds)
            {
                float p = Smooth(time / EjectExitSeconds);
                pose.State = SoulRecorderOverlayState.EjectExit;
                pose.Cassette = Lerp(clear, exit, p);
                pose.CassetteRotationDegrees = Lerp(0f, StartCassetteRotation, p);
                pose.CassetteAlpha = 1f - Smooth(Clamp01((p - 0.45f) / 0.55f));
                pose.RecorderAlpha = 1f - Smooth(p);
                pose.Recorder.Y += Lerp(0f, 22f, p);
                return pose;
            }
            pose.State = SoulRecorderOverlayState.Hidden;
            pose.RecorderAlpha = 0f;
            pose.CassetteAlpha = 0f;
            pose.Cassette = exit;
            return pose;
        }

        public static SoulRecorderOverlayPose ApplyExitFade(
            SoulRecorderOverlayPose pose, float elapsed, float speed)
        {
            float p = Smooth(Math.Max(0f, elapsed) * SafeSpeed(speed) /
                PresentationFadeSeconds);
            pose.RecorderAlpha *= 1f - p;
            pose.CassetteAlpha *= 1f - p;
            pose.Recorder.Y += Lerp(0f, 20f, p);
            if (p >= 1f) pose.State = SoulRecorderOverlayState.Hidden;
            return pose;
        }

        private static SoulRecorderOverlayPose BasePose(int screenWidth,
            int screenHeight, SoulRecorderOverlaySettings settings)
        {
            float scale = Clamp(settings.Scale, 0.65f, 1.5f);
            float width = screenWidth * RecorderWidthFraction * scale;
            float height = width * RecorderAspect;
            float maximumHeight = screenHeight * 0.76f;
            if (height > maximumHeight)
            {
                float ratio = maximumHeight / height;
                width *= ratio;
                height *= ratio;
            }
            float resolutionScale = Clamp(screenHeight / 1080f, 0.75f, 1.5f);
            float margin = Clamp(BaseMarginPixels * resolutionScale, 32f, 48f);
            SoulRecorderOverlayRect recorder = new SoulRecorderOverlayRect
            {
                X = screenWidth - margin - width - settings.HorizontalOffset,
                Y = screenHeight - margin - height - settings.VerticalOffset,
                Width = width,
                Height = height
            };
            return new SoulRecorderOverlayPose
            {
                State = SoulRecorderOverlayState.Idle,
                Recorder = recorder,
                Cassette = CassetteAt(recorder, 1.04f, 0.77f),
                RecorderAlpha = 1f,
                CassetteAlpha = 1f,
                RecorderScale = 1f
            };
        }

        private static SoulRecorderOverlayRect CassetteAt(
            SoulRecorderOverlayRect recorder, float centerX, float centerY)
        {
            float width = recorder.Width * CassetteWidthFractionOfRecorder;
            float height = width * CassetteAspect;
            return new SoulRecorderOverlayRect
            {
                X = recorder.X + recorder.Width * centerX - width * 0.5f,
                Y = recorder.Y + recorder.Height * centerY - height * 0.5f,
                Width = width,
                Height = height
            };
        }

        private static SoulRecorderOverlayRect Offset(
            SoulRecorderOverlayRect value, float x, float y)
        {
            value.X += x; value.Y += y; return value;
        }

        private static SoulRecorderOverlayRect Lerp(
            SoulRecorderOverlayRect a, SoulRecorderOverlayRect b, float t)
        {
            return new SoulRecorderOverlayRect
            {
                X = Lerp(a.X, b.X, t), Y = Lerp(a.Y, b.Y, t),
                Width = Lerp(a.Width, b.Width, t),
                Height = Lerp(a.Height, b.Height, t)
            };
        }

        private static float Settle(float value)
        {
            return (float)Math.Sin(Clamp01(value) * Math.PI);
        }

        private static float Smooth(float value)
        {
            float t = Clamp01(value); return t * t * (3f - 2f * t);
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * Clamp01(t);
        }

        private static float SafeSpeed(float value)
        {
            return Clamp(value, 0.5f, 2f);
        }

        private static float Clamp01(float value) { return Clamp(value, 0f, 1f); }
        private static float Clamp(float value, float minimum, float maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}
