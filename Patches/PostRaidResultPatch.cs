using System;
using System.Linq;
using System.Reflection;
using EFT;
using EFT.UI.SessionEnd;
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
        private static void PatchPostfix(object[] __args)
        {
            try
            {
                ExitStatus? outcome = null;
                foreach (object argument in __args)
                {
                    if (argument is ExitStatus)
                    {
                        outcome = (ExitStatus)argument;
                        break;
                    }
                }

                if (!outcome.HasValue)
                {
                    Plugin.Log.LogWarning("Post-raid result callback did not contain an ExitStatus argument.");
                    return;
                }

                Plugin.Log.LogInfo("Raid result signal detected: " + outcome.Value + ".");
                Plugin.PostRaidCoordinator.Queue(outcome.Value);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("Post-raid music failed: " + ex);
            }
        }
    }
}
