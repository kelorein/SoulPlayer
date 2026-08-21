using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT;
using EFT.UI;
using HarmonyLib;
using SoulPlayer.UI;
using SPT.Reflection.Patching;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SoulPlayer.Patches
{
    internal sealed class MenuScreenPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            // EFT frequently renames the middle controller type used by MenuScreen.Show.
            // Resolve the overload by the stable Profile + ESessionMode parameters instead
            // of compiling against the obfuscated controller class name.
            var candidates = typeof(MenuScreen)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => method.Name == "Show")
                .Select(method => new
                {
                    Method = method,
                    Parameters = method.GetParameters()
                })
                .Where(candidate =>
                    candidate.Parameters.Any(parameter => parameter.ParameterType == typeof(Profile)) &&
                    candidate.Parameters.Any(parameter => parameter.ParameterType == typeof(ESessionMode)))
                .ToArray();

            MethodBase target = candidates
                .FirstOrDefault(candidate => candidate.Parameters.Length == 3)?.Method ??
                candidates.FirstOrDefault()?.Method;

            if (target == null)
            {
                throw new MissingMethodException(
                    typeof(MenuScreen).FullName,
                    "Show(Profile, ..., ESessionMode)");
            }

            return target;
        }

        [PatchPostfix]
        private static void PatchPostfix(MenuScreen __instance)
        {
            try
            {
                Transform obsoleteButton = __instance.transform.Find("SoulPlayerButton");
                if (obsoleteButton != null)
                {
                    UnityEngine.Object.Destroy(obsoleteButton.gameObject);
                }

                TMP_Text styleSource = __instance.transform
                    .Find("HideoutButton")?
                    .GetComponentsInChildren<TMP_Text>(true)
                    .FirstOrDefault();

                SoulPlayerOverlayHost.Create(styleSource);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("Failed to prepare the SoulPlayer window: " + ex);
            }
        }
    }

    internal sealed class MenuTaskBarPatch : ModulePatch
    {
        private const string ButtonName = "SoulPlayerTaskBarButton";

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(MenuTaskBar), nameof(MenuTaskBar.Awake));
        }

        [PatchPostfix]
        private static void PatchPostfix(MenuTaskBar __instance)
        {
            try
            {
                Transform existing = __instance
                    .GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(transform => transform.name == ButtonName);

                if (existing != null)
                {
                    return;
                }

                Dictionary<EMenuType, AnimatedToggle> toggles = Traverse
                    .Create(__instance)
                    .Field("_toggleButtons")
                    .GetValue<Dictionary<EMenuType, AnimatedToggle>>();

                if (toggles == null ||
                    !toggles.TryGetValue(EMenuType.Handbook, out AnimatedToggle source) ||
                    source == null)
                {
                    Plugin.Log.LogError("SoulPlayer could not find the Handbook taskbar button.");
                    return;
                }

                GameObject clone = UnityEngine.Object.Instantiate(
                    source.gameObject,
                    source.transform.parent);

                clone.name = ButtonName;
                clone.transform.SetSiblingIndex(source.transform.GetSiblingIndex() + 1);

                DisableLocalization(clone);
                SetButtonText(clone, "MUSIC");

                AnimatedToggle toggle = clone.GetComponent<AnimatedToggle>();
                if (toggle == null)
                {
                    Plugin.Log.LogError("The cloned taskbar entry has no AnimatedToggle component.");
                    UnityEngine.Object.Destroy(clone);
                    return;
                }

                // Keep the cloned Tarkov artwork, but replace its AnimatedToggle control.
                // AnimatedToggle.OnPointerClick assumes it always belongs to a ToggleGroup;
                // detaching it causes a silent click failure. A normal Button avoids the
                // private menu routing while retaining the same visuals and animations.
                Graphic targetGraphic = toggle.targetGraphic;
                ColorBlock colors = toggle.colors;
                SpriteState spriteState = toggle.spriteState;
                AnimationTriggers animationTriggers = toggle.animationTriggers;
                Selectable.Transition transition = toggle.transition;
                Navigation navigation = toggle.navigation;
                bool interactable = toggle.interactable;

                UnityEngine.Object.DestroyImmediate(toggle);

                Button clickButton = clone.AddComponent<Button>();
                clickButton.targetGraphic = targetGraphic;
                clickButton.colors = colors;
                clickButton.spriteState = spriteState;
                clickButton.animationTriggers = animationTriggers;
                clickButton.transition = transition;
                clickButton.navigation = navigation;
                clickButton.interactable = interactable;
                clickButton.onClick.AddListener(() =>
                {
                    Plugin.Log.LogInfo("SoulPlayer MUSIC taskbar button clicked.");
                    ToggleWindow(clone);
                });

                CanvasGroup canvasGroup = clone.GetComponent<CanvasGroup>();
                if (canvasGroup != null)
                {
                    canvasGroup.interactable = true;
                    canvasGroup.blocksRaycasts = true;
                }

                clone.SetActive(true);
                Plugin.Log.LogInfo("SoulPlayer MUSIC taskbar button created.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("Failed to create the SoulPlayer taskbar button: " + ex);
            }
        }

        private static void ToggleWindow(GameObject taskBarButton)
        {
            TMP_Text styleSource = taskBarButton
                .GetComponentsInChildren<TMP_Text>(true)
                .FirstOrDefault();

            SoulPlayerOverlayHost.Create(styleSource).ToggleWindow();
        }

        private static void DisableLocalization(GameObject buttonObject)
        {
            foreach (Component component in buttonObject.GetComponentsInChildren<Component>(true))
            {
                if (component != null && component.GetType().FullName == "EFT.UI.LocalizedText")
                {
                    Behaviour behaviour = component as Behaviour;
                    if (behaviour != null)
                    {
                        behaviour.enabled = false;
                    }
                }
            }
        }

        private static void SetButtonText(GameObject buttonObject, string value)
        {
            foreach (TMP_Text label in buttonObject.GetComponentsInChildren<TMP_Text>(true))
            {
                label.text = value;
                label.ForceMeshUpdate();
            }
        }
    }
}
