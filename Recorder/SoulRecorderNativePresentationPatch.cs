using System.Reflection;
using EFT;
using SPT.Reflection.Patching;

namespace SoulPlayer.Recorder
{
    /// <summary>
    /// Observes EFT's native usable-item initialization and mounts SoulPlayer's
    /// recorder only when the controller owns the armed transient donor item.
    /// </summary>
    internal sealed class SoulRecorderNativePresentationPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(Player.UsableItemController).GetMethod(
                "InitializeController",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(Player), typeof(WeaponPrefab) },
                null);
        }

        [PatchPostfix]
        public static void PatchPostfix(
            Player.UsableItemController __instance,
            Player __0,
            WeaponPrefab __1)
        {
            SoulRecorderNativePresentation.TryAttach(__instance, __0, __1);
        }
    }
}
