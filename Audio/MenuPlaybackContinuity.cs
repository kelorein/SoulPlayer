using EFT.UI.Screens;

namespace SoulPlayer.Audio
{
    // Menu navigation never owns a track, queue, decoder or AudioSource. This
    // policy only distinguishes ordinary UI rebuilds from raid-return loading.
    internal sealed class MenuPlaybackContinuity
    {
        private bool _normalMenuReady;

        internal void Reset() { _normalMenuReady = false; }

        internal RaidReadinessReason Evaluate(RaidMenuEvidence evidence,
            bool enabled, bool outcomeSettled, bool libraryReady)
        {
            RaidReadinessReason scene = evidence.Evaluate(outcomeSettled, true);
            if (!enabled || !outcomeSettled || evidence.PlayerPresent ||
                (!evidence.NormalMenuScreen && !evidence.MenuControllerTransition) || !evidence.ReturnScreenShown)
            {
                Reset();
                return evidence.Evaluate(outcomeSettled, libraryReady);
            }

            // Require a real stable ordinary menu first. Result screens retain
            // the strict START gate; ongoing audio has a separate permission.
            if (evidence.NormalMenuScreen && scene == RaidReadinessReason.Ready) _normalMenuReady = true;
            if (!_normalMenuReady) return evidence.Evaluate(outcomeSettled, libraryReady);

            // Once back in ordinary menus, a screen rebuild or Hideout loader
            // must not pause the current cue or invalidate the saved Main.
            // EFT also clears its controller between views. That gap can retain
            // an established context, but cannot establish a return on its own.
            return libraryReady ? RaidReadinessReason.Ready : RaidReadinessReason.WaitingForLibrary;
        }

        internal static bool IsNormalMenuScreen(EEftScreenType screen)
        {
            switch (screen)
            {
                case EEftScreenType.MainMenu:
                case EEftScreenType.Inventory:
                case EEftScreenType.Traders:
                case EEftScreenType.Trader:
                case EEftScreenType.TraderDialog:
                case EEftScreenType.FleaMarket:
                case EEftScreenType.Hideout:
                case EEftScreenType.HideoutAreaItemsTransfer:
                case EEftScreenType.HideoutAreaMannequinEquipment:
                case EEftScreenType.HideoutCircleOfCultists:
                case EEftScreenType.WeaponModding:
                case EEftScreenType.TransferItems:
                case EEftScreenType.OtherPlayerProfile:
                case EEftScreenType.Handbook:
                case EEftScreenType.EditBuild:
                case EEftScreenType.EquipmentBuilds:
                case EEftScreenType.Settings:
                case EEftScreenType.NewsHub:
                case EEftScreenType.ObtainPrestige:
                case EEftScreenType.EventDialog:
                case EEftScreenType.ProfileEditor:
                case EEftScreenType.DressRoom:
                    return true;
                default:
                    // Unknown, matchmaking, deployment, battle and result
                    // screens must never establish ordinary-menu continuity.
                    return false;
            }
        }

        internal static bool IsBlockingPlayer(bool present, bool hideoutPlayer, bool enabled)
        {
            return present && (!enabled || !hideoutPlayer);
        }
    }

    // Proof of an actual post-raid start survives transient UI evidence loss.
    // This never grants permission to dispatch/decode another track, nor does it
    // play/unpause anything. User controls, clip validity and EOF remain owned by
    // the player. Deployment clears the proof before capturing/suspending Main.
    internal sealed class PostRaidPlaybackContinuity
    {
        private bool _started;

        internal void Reset() { _started = false; }

        internal void PlaybackStarted(bool postRaid, bool playing)
        {
            if (postRaid && playing) _started = true;
        }

        internal bool CanContinue(bool enabled, bool outcomeSettled)
        {
            return enabled && outcomeSettled && _started;
        }

        internal bool CanDisplay(bool enabled, bool outcomeSettled, bool sceneReady)
        {
            // The persistent overlay is safe on results screens even while a
            // clip is user-paused, stopped, or naturally awaiting its successor.
            return sceneReady || CanContinue(enabled, outcomeSettled);
        }
    }
}
