using System;
using System.Reflection;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace SoulPlayer.Patches
{
    internal sealed class TarkovMusicVolumePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            Type soundSettingsGroup = AccessTools.TypeByName("EFT.Settings.Sound.SoundSettingsGroup");
            if (soundSettingsGroup == null)
            {
                throw new TypeLoadException("EFT.Settings.Sound.SoundSettingsGroup");
            }

            MethodInfo getter = AccessTools.PropertyGetter(soundSettingsGroup, "MusicVolumeValue") ??
                                AccessTools.Method(soundSettingsGroup, "get_MusicVolumeValue");

            if (getter == null)
            {
                throw new MissingMethodException(soundSettingsGroup.FullName, "get_MusicVolumeValue");
            }

            return getter;
        }

        [PatchPostfix]
        private static void PatchPostfix(ref int __result)
        {
            if (Plugin.Settings != null && Plugin.Settings.MuteTarkovMusic)
            {
                __result = 0;
            }
        }
    }
}
