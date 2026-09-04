using System;
using System.Reflection;
using EFT;
using EFT.UI.Matchmaker;
using SPT.Reflection.Patching;

namespace SoulPlayer.Patches
{
    internal sealed class RaidDeploymentPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(MatchmakerFinalCountdown).GetMethod("Show",
                new[] { typeof(Profile), typeof(DateTime) });
        }

        [PatchPrefix]
        private static void PatchPrefix()
        {
            if (Plugin.AudioPlayer != null) Plugin.AudioPlayer.BeginRaidSuspension();
        }
    }
}
