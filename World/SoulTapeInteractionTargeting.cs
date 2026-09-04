namespace SoulPlayer.World
{
    /// <summary>
    /// Pure tuning policy for the forgiving screen-center interaction cone.
    /// Runtime geometry and occlusion remain in the world controller.
    /// </summary>
    internal static class SoulTapeInteractionTargeting
    {
        internal const float AcquireAngleDegrees = 2.5f;
        internal const float RetainAngleDegrees = 3.5f;

        internal static float GetAllowedAngle(bool isCurrentTarget)
        {
            return isCurrentTarget ? RetainAngleDegrees : AcquireAngleDegrees;
        }

        internal static bool IsWithinCone(float angleDegrees, bool isCurrentTarget)
        {
            return angleDegrees >= 0f &&
                   angleDegrees <= GetAllowedAngle(isCurrentTarget);
        }

        internal static bool LooksLikeAuxiliaryCameraName(string cameraName)
        {
            string value = (cameraName ?? string.Empty).ToLowerInvariant();
            return value.Contains("optic") ||
                   value.Contains("scope") ||
                   value.Contains("weapon") ||
                   value.Contains("firearm") ||
                   value.Contains("overlay") ||
                   value.Contains("ui camera");
        }
    }
}
