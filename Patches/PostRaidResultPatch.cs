using System;
using System.Linq;
using System.Reflection;
using EFT;
using EFT.UI.SessionEnd;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace SoulPlayer.Patches
{
    internal sealed class PostRaidResultPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(SessionResultExitStatus)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Single(method =>
                    method.Name == "Show" &&
                    method.GetParameters().Any(parameter => parameter.ParameterType == typeof(ExitStatus)));
        }

        [PatchPostfix]
        private static void PatchPostfix([HarmonyArgument(3)] ExitStatus outcome)
        {
            try
            {
                Plugin.Log.LogInfo("Raid result detected: " + outcome + ".");
                Plugin.AudioPlayer.PlayPostRaid(outcome);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("Post-raid music failed: " + ex);
            }
        }
    }
}
