using System;
using System.Linq;
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

    internal sealed class TarkovMusicSettingsApplyPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            Type controller = AccessTools.TypeByName("EFT.Settings.Sound.SoundSettingsController");
            if (controller == null)
            {
                throw new TypeLoadException("EFT.Settings.Sound.SoundSettingsController");
            }

            MethodInfo method = AccessTools.Method(
                controller,
                "AdjustSoundValue",
                new[] { typeof(string), typeof(int) });

            if (method == null)
            {
                throw new MissingMethodException(
                    controller.FullName,
                    "AdjustSoundValue(String, Int32)");
            }

            return method;
        }

        [PatchPrefix]
        private static void PatchPrefix(
            [HarmonyArgument(0)] string mixerParameter,
            [HarmonyArgument(1)] ref int value)
        {
            if (Plugin.Settings == null ||
                !Plugin.Settings.MuteTarkovMusic ||
                string.IsNullOrEmpty(mixerParameter))
            {
                return;
            }

            if (mixerParameter.IndexOf("Music", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                value = 0;
            }
        }
    }

    internal sealed class TarkovForceApplyVolumePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            Type soundSettingsGroup = AccessTools.TypeByName("EFT.Settings.Sound.SoundSettingsGroup");
            if (soundSettingsGroup == null)
            {
                throw new TypeLoadException("EFT.Settings.Sound.SoundSettingsGroup");
            }

            MethodInfo method = AccessTools.Method(soundSettingsGroup, "ForceApplyVolumeSettings");
            if (method == null)
            {
                throw new MissingMethodException(soundSettingsGroup.FullName, "ForceApplyVolumeSettings");
            }

            return method;
        }

        [PatchPostfix]
        private static void PatchPostfix()
        {
            if (Plugin.Settings != null && Plugin.Settings.MuteTarkovMusic)
            {
                Plugin.TarkovMusicMuter?.ApplyNow();
            }
        }
    }

    internal sealed class TarkovSoundSettingsTabPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            Type soundSettingsTab = AccessTools.TypeByName("EFT.UI.Settings.SoundSettingsTab");
            if (soundSettingsTab == null)
            {
                throw new TypeLoadException("EFT.UI.Settings.SoundSettingsTab");
            }

            MethodInfo method = soundSettingsTab
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(candidate => candidate.Name == "Show")
                .OrderByDescending(candidate => candidate.GetParameters().Length)
                .FirstOrDefault();

            if (method == null)
            {
                throw new MissingMethodException(soundSettingsTab.FullName, "Show");
            }

            return method;
        }

        [PatchPostfix]
        private static void PatchPostfix()
        {
            if (Plugin.Settings != null && Plugin.Settings.MuteTarkovMusic)
            {
                Plugin.TarkovMusicMuter?.ApplyNow();
            }
        }
    }
}
