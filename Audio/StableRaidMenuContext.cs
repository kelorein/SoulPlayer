using System;
using System.Reflection;
using EFT.UI;
using EFT.UI.Screens;
using EFT.UI.SessionEnd;
using SoulPlayer.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace SoulPlayer.Audio
{
    internal static class StableRaidMenuContext
    {
        internal static readonly FieldInfo LoaderField = typeof(PreloaderUI).GetField("_loader", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        internal static readonly FieldInfo BlackImageField = typeof(PreloaderUI).GetField("_overlapBlackImage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        internal static readonly FieldInfo ResultModelField = typeof(SessionResultExitStatus).GetField("_playerModelView", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static PreloaderUI _preloader;
        private static GameObject _loader;
        private static Image _black;
        private static SessionResultExitStatus _result;
        private static PlayerModelView _model;
        private static EEftScreenType? _lastScreen;
        private static string _screenName;

        internal static void Invalidate()
        {
            _preloader = null;
            _loader = null;
            _black = null;
            _result = null;
            _model = null;
        }

        internal static RaidMenuEvidence Read(bool returnScreenShown)
        {
#if SOULPLAYER_PERF
            SoulPlayer.Utils.RecurringWorkProfiler.Begin(SoulPlayer.Utils.RecurringWorkArea.Readiness);
            try
            {
#endif
            RaidMenuEvidence evidence = new RaidMenuEvidence
            {
                ReturnScreenShown = returnScreenShown, Screen = "Unavailable",
                PlayerPresent = GameState.HasLiveRaidPlayer(),
                PlayerAlive = GameState.HasAliveRaidPlayer(), ResultModel = "NotApplicable"
            };
            EftScreenManager manager = EftScreenManager.Instance;
            if (manager != null && manager.CurrentScreenController != null)
            {
                EEftScreenType type = manager.CurrentScreenController.ScreenType;
                if (_lastScreen != type) { _lastScreen = type; _screenName = type.ToString(); }
                evidence.Screen = _screenName;
                evidence.RecognizedScreen = IsReturnScreen(type);
                UIScreen screen;
                if (manager.TryGetScreen(type, out screen) && screen != null)
                {
                    evidence.ScreenActive = screen.isActiveAndEnabled;
                    SessionResultExitStatus result = screen as SessionResultExitStatus;
                    if (result != null)
                    {
                        // Advisory only: portrait loading is not the menu/scene
                        // readiness authority, and may not exist on other screens.
                        try
                        {
                            if (_result != result)
                            {
                                _result = result;
                                _model = ResultModelField == null ? null : ResultModelField.GetValue(result) as PlayerModelView;
                            }
                            evidence.ResultModel = _model == null ? "Unavailable" :
                                _model.LoadingComplete ? "Complete" : "Loading";
                        }
                        catch { evidence.ResultModel = "Unavailable"; }
                    }
                }
            }
            if (PreloaderUI.Instantiated)
            {
                PreloaderUI preloader = PreloaderUI.Instance;
                if (_preloader != preloader)
                {
                    _preloader = preloader;
                    _loader = LoaderField == null ? null : LoaderField.GetValue(preloader) as GameObject;
                    _black = BlackImageField == null ? null : BlackImageField.GetValue(preloader) as Image;
                }
                GameObject loader = _loader;
                Image black = _black;
                evidence.Preloader = loader == null ? (bool?)null : loader.activeInHierarchy;
                evidence.BlackOverlay = black == null ? (bool?)null :
                    black.isActiveAndEnabled && black.color.a > 0.001f;
            }
            return evidence;
        #if SOULPLAYER_PERF
            }
            finally { SoulPlayer.Utils.RecurringWorkProfiler.End(SoulPlayer.Utils.RecurringWorkArea.Readiness); }
#endif
        }

        internal static bool IsReturnScreen(EEftScreenType type)
        {
            return type == EEftScreenType.MainMenu || type == EEftScreenType.Inventory ||
                type == EEftScreenType.ExitStatus || type == EEftScreenType.KillList ||
                type == EEftScreenType.SessionStatistics || type == EEftScreenType.SessionExperience ||
                type == EEftScreenType.HealthTreatment || type == EEftScreenType.ScavInventory;
        }
    }
}
