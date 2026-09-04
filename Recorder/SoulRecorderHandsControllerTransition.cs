using System;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace SoulPlayer.Recorder
{
    internal interface ISoulRecorderHandsControllerTransition
    {
        event Action<bool, string> InteractionReleased;

        bool IsAcquiring { get; }
        bool IsOwned { get; }
        bool IsRestoring { get; }
        bool IsBusy { get; }
        Player Player { get; }

        void Acquire(Player player, Action<bool> completed);
        void Restore(string reason);
        void Abandon(string reason);
        void ManualUpdate(float unscaledTime);
    }

    /// <summary>
    /// Uses EFT's supported empty-hands transition while SoulPlayer owns the
    /// packaged recorder presentation, then restores the saved item through the
    /// normal hands-controller pipeline.
    /// Tokenized bookkeeping prevents late callbacks from affecting a later cycle.
    /// </summary>
    internal sealed class SptSoulRecorderNativeHandsControllerTransition :
        ISoulRecorderHandsControllerTransition
    {
        private const float AcquireObservationGraceSeconds = 0.10f;
        private const float AcquireTimeoutSeconds = 3f;
        private const float RestoreHolsterTimeoutSeconds = 1.5f;
        private const float RestoreTimeoutSeconds = 5f;

        private readonly SoulRecorderHandsOwnershipState<Item> _ownership =
            new SoulRecorderHandsOwnershipState<Item>();
        private readonly SoulRecorderHandsRestoreObservationGate<
            Player.AbstractHandsController, Item> _restoreObservation =
                new SoulRecorderHandsRestoreObservationGate<
                    Player.AbstractHandsController, Item>();
        private Player _player;
        private string _previousControllerName;
        private bool _cancelAcquisition;
        private bool _restoreCancelledAcquisition;
        private float _acquireStartedAt;
        private int _acquireToken;
        private Action<bool> _acquireCompleted;
        private string _deferredRestoreReason;
        private float _restoreStartedAt;
        private int _restoreToken;
        private string _restoreReason;
        private int _restoreAttempt;
        private bool _restoreRecoveryStarted;

        public event Action<bool, string> InteractionReleased;

        public bool IsAcquiring
        {
            get { return _ownership.Phase == SoulRecorderHandsOwnershipPhase.Acquiring; }
        }

        public bool IsOwned { get { return _ownership.IsOwned; } }

        public bool IsRestoring
        {
            get { return _ownership.Phase == SoulRecorderHandsOwnershipPhase.Restoring; }
        }

        public bool IsBusy { get { return _ownership.IsBusy; } }
        public Player Player { get { return _player; } }

        public void Acquire(Player player, Action<bool> completed)
        {
            if (player == null || completed == null)
            {
                completed?.Invoke(false);
                return;
            }

            if (IsOwned && _player == player)
            {
                completed(true);
                return;
            }

            Player.AbstractHandsController previousController = player.HandsController;
            Item previousItem = previousController == null ? null : previousController.Item;
            int acquireToken;
            if (!_ownership.BeginAcquire(previousItem, out acquireToken))
            {
                Plugin.Log.LogInfo(
                    "SoulRecorder HANDS acquire rejected: transition is already busy.");
                completed(false);
                return;
            }

            _player = player;
            _restoreObservation.Begin(previousController);
            _previousControllerName = previousController == null
                ? "<none>"
                : previousController.GetType().FullName;
            _cancelAcquisition = false;
            _restoreCancelledAcquisition = false;
            _acquireStartedAt = Time.unscaledTime;
            _acquireToken = acquireToken;
            _acquireCompleted = completed;

            Plugin.Log.LogInfo(
                "SoulRecorder HANDS acquire -> controller=" + _previousControllerName +
                ", item=" + DescribeItem(previousItem) + ".");
            Plugin.Log.LogInfo(
                "SoulRecorder HANDS empty-hands presentation requested.");

            try
            {
                player.DropCurrentController(() =>
                {
                    try
                    {
                        player.DestroyController();
                        Plugin.Log.LogInfo(
                            "SoulRecorder HANDS SetEmptyHands requested.");
                        player.SetEmptyHands(result =>
                        {
                            bool succeeded = result.Succeed && result.Value != null;
                            if (succeeded)
                            {
                                Plugin.Log.LogInfo(
                                    "SoulRecorder HANDS empty confirmed; starting packaged " +
                                    "recorder presentation.");
                                CompleteNativeAcquire(acquireToken, true, completed);
                                return;
                            }
                            FailNativeAcquire(
                                acquireToken,
                                "SetEmptyHands failed: " +
                                (string.IsNullOrEmpty(result.Error)
                                    ? "no empty-hands controller returned"
                                    : result.Error),
                                completed);
                        });
                    }
                    catch (Exception ex)
                    {
                        FailNativeAcquire(
                            acquireToken,
                            "empty-hands acquisition failed: " + ex,
                            completed);
                    }
                }, false, null);
            }
            catch (Exception ex)
            {
                FailNativeAcquire(
                    acquireToken,
                    "empty-hands equip threw: " + ex,
                    completed);
            }
        }

        private void CompleteNativeAcquire(
            int acquireToken,
            bool succeeded,
            Action<bool> completed)
        {
            if (!IsAcquiring)
            {
                return;
            }

            if (_cancelAcquisition)
            {
                Action<bool> cancellationCompleted = TakeAcquireCompletion(completed);
                bool restoreCancelled = _restoreCancelledAcquisition;
                _cancelAcquisition = false;
                _restoreCancelledAcquisition = false;
                if (_ownership.ConfirmEmptyHands(acquireToken))
                {
                    if (succeeded)
                    {
                        Plugin.Log.LogInfo(
                            "SoulRecorder HANDS empty controller confirmed after cancellation.");
                    }
                    if (restoreCancelled)
                    {
                        DeferRestore("cancelled recorder acquisition");
                    }
                    else
                    {
                        Abandon("cancelled terminal recorder acquisition");
                    }
                }
                else
                {
                    _ownership.FailAcquire(acquireToken);
                    ReleaseInteraction(
                        false,
                        "cancelled empty-hands acquisition did not complete");
                }
                cancellationCompleted(false);
                return;
            }

            if (succeeded && _ownership.ConfirmEmptyHands(acquireToken))
            {
                Action<bool> acquisitionCompleted = TakeAcquireCompletion(completed);
                Plugin.Log.LogInfo(
                    "SoulRecorder HANDS empty-hands ownership confirmed.");
                acquisitionCompleted(true);
                return;
            }

            FailNativeAcquire(
                acquireToken,
                "empty-hands acquisition was not confirmed",
                completed);
        }

        private void FailNativeAcquire(
            int acquireToken,
            string detail,
            Action<bool> completed)
        {
            if (!IsAcquiring)
            {
                return;
            }

            Action<bool> acquisitionCompleted = TakeAcquireCompletion(completed);
            Plugin.Log.LogWarning("SoulRecorder HANDS " + detail + ".");
            if (_ownership.ConfirmEmptyHands(acquireToken))
            {
                DeferRestore("failed empty-hands acquisition recovery: " + detail);
                acquisitionCompleted(false);
                return;
            }

            _ownership.FailAcquire(acquireToken);
            acquisitionCompleted(false);
            ReleaseInteraction(false, detail);
        }

        public void Restore(string reason)
        {
            if (IsAcquiring)
            {
                _cancelAcquisition = true;
                _restoreCancelledAcquisition = true;
                Plugin.Log.LogInfo(
                    "SoulRecorder HANDS restore deferred until pending empty-hands " +
                    "acquisition completes (" + reason + ").");
                return;
            }

            Item previousItem;
            int restoreToken;
            if (!_ownership.BeginRestore(out restoreToken, out previousItem))
            {
                return;
            }

            _restoreToken = restoreToken;
            _restoreReason = reason ?? "interaction completed";
            _restoreStartedAt = Time.unscaledTime;
            _restoreRecoveryStarted = false;
            int restoreAttempt = ++_restoreAttempt;
            Plugin.Log.LogInfo(
                "SoulRecorder HANDS restore requested -> " +
                DescribeItem(previousItem) + ".");

            if (_player == null)
            {
                CompleteRestore(false, "local player is no longer available");
                return;
            }

            if (_player.HandsController == null)
            {
                Plugin.Log.LogInfo(
                    "SoulRecorder HANDS no active controller remains; restoring the " +
                    "saved item directly.");
                RestorePreviousItemDirectly(
                    _player,
                    previousItem,
                    restoreToken,
                    restoreAttempt,
                    "direct SetInHands after empty-controller failure");
                return;
            }

            try
            {
                Player restoringPlayer = _player;
                restoringPlayer.DropCurrentController(() =>
                {
                    if (!IsRestoring || restoreToken != _restoreToken ||
                        restoreAttempt != _restoreAttempt || _player == null)
                    {
                        return;
                    }

                    try
                    {
                        restoringPlayer.DestroyController();
                        Plugin.Log.LogInfo(
                            "SoulRecorder HANDS recorder presentation holster completed.");
                        if (previousItem == null)
                        {
                            CompleteRestore(true, "previous controller had no item");
                            return;
                        }
                        RestorePreviousItemDirectly(
                            restoringPlayer,
                            previousItem,
                            restoreToken,
                            restoreAttempt,
                            "SetInHands callback");
                    }
                    catch (Exception ex)
                    {
                        CompleteRestore(false, "presentation holster/restore exception: " + ex.Message);
                    }
                }, false, previousItem);
            }
            catch (Exception ex)
            {
                CompleteRestore(false, "presentation holster request exception: " + ex.Message);
            }
        }

        private void RestorePreviousItemDirectly(
            Player restoringPlayer,
            Item previousItem,
            int restoreToken,
            int restoreAttempt,
            string successDetail)
        {
            if (!IsRestoring || restoreToken != _restoreToken ||
                restoreAttempt != _restoreAttempt)
            {
                return;
            }
            if (previousItem == null)
            {
                CompleteRestore(true, "previous controller had no item");
                return;
            }

            try
            {
                _restoreObservation.MarkRestoreRequested();
                Plugin.Log.LogInfo(
                    "SoulRecorder HANDS SetInHands restore request dispatched.");
                restoringPlayer.SetInHands(previousItem, result =>
                {
                    if (!IsRestoring || restoreToken != _restoreToken ||
                        restoreAttempt != _restoreAttempt)
                    {
                        return;
                    }

                    if (result.Succeed && result.Value != null)
                    {
                        CompleteRestore(true, successDetail);
                    }
                    else
                    {
                        CompleteRestore(
                            false,
                            string.IsNullOrEmpty(result.Error)
                                ? "SetInHands returned no controller"
                                : result.Error);
                    }
                });
            }
            catch (Exception ex)
            {
                CompleteRestore(false, "direct SetInHands exception: " + ex);
            }
        }

        public void ManualUpdate(float unscaledTime)
        {
            if (IsAcquiring)
            {
                Player.AbstractHandsController acquiringController =
                    _player == null ? null : _player.HandsController;
                if (unscaledTime - _acquireStartedAt >=
                        AcquireObservationGraceSeconds &&
                    acquiringController is IEmptyHandsController)
                {
                    Plugin.Log.LogInfo(
                        "SoulRecorder HANDS empty controller observed active before " +
                        "the callback; completing acquisition safely.");
                    CompleteNativeAcquire(
                        _acquireToken,
                        true,
                        _acquireCompleted ?? (_ => { }));
                    return;
                }

                if (unscaledTime - _acquireStartedAt >= AcquireTimeoutSeconds)
                {
                    FailNativeAcquire(
                        _acquireToken,
                        "empty-hands acquisition timed out",
                        _acquireCompleted ?? (_ => { }));
                }
                return;
            }

            if (IsOwned && !string.IsNullOrEmpty(_deferredRestoreReason))
            {
                string reason = _deferredRestoreReason;
                _deferredRestoreReason = null;
                Restore(reason);
                return;
            }

            if (!IsRestoring)
            {
                return;
            }

            Item expected = _ownership.PreviousItem;
            Player.AbstractHandsController restoredController =
                _player == null ? null : _player.HandsController;
            if (_restoreObservation.CanComplete(
                    restoredController,
                    restoredController == null ? null : restoredController.Item,
                    expected))
            {
                CompleteRestore(true, "new active hands controller observed after SetInHands");
                return;
            }

            float restoreElapsed = unscaledTime - _restoreStartedAt;
            if (!_restoreRecoveryStarted &&
                !_restoreObservation.IsRestoreRequested &&
                restoreElapsed >= RestoreHolsterTimeoutSeconds)
            {
                BeginForcedRestoreRecovery(expected);
                return;
            }

            if (restoreElapsed >= RestoreTimeoutSeconds)
            {
                CompleteRestore(false, "SetInHands callback timed out");
            }
        }

        private void BeginForcedRestoreRecovery(Item previousItem)
        {
            if (!IsRestoring || _player == null || _restoreRecoveryStarted)
            {
                return;
            }

            _restoreRecoveryStarted = true;
            int restoreToken = _restoreToken;
            int restoreAttempt = ++_restoreAttempt;
            Player restoringPlayer = _player;
            Plugin.Log.LogWarning(
                "SoulRecorder HANDS normal stock-controller holster stalled; " +
                "forcing controller cleanup and restoring the saved item.");

            try
            {
                SoulRecorderNativePresentation.Release(
                    "forced weapon-restore recovery");
                restoringPlayer.DestroyController();
                RestorePreviousItemDirectly(
                    restoringPlayer,
                    previousItem,
                    restoreToken,
                    restoreAttempt,
                    "forced recovery SetInHands callback");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning(
                    "SoulRecorder HANDS forced weapon-restore recovery threw: " + ex);
            }
        }

        public void Abandon(string reason)
        {
            if (IsAcquiring)
            {
                _cancelAcquisition = true;
                _restoreCancelledAcquisition = false;
            }

            bool wasBusy = IsBusy;
            _ownership.Abandon();
            ClearSavedRuntimeState();
            if (wasBusy)
            {
                Plugin.Log.LogInfo(
                    "SoulRecorder HANDS interaction released without weapon restoration (" +
                    reason + ").");
                RaiseReleased(false, reason);
            }
        }

        private void CompleteRestore(bool succeeded, string detail)
        {
            int token = _restoreToken;
            if (!_ownership.CompleteRestore(token))
            {
                return;
            }

            string reason = _restoreReason;
            if (succeeded)
            {
                Plugin.Log.LogInfo(
                    "SoulRecorder HANDS restore completed (" + detail + ").");
            }
            else
            {
                Plugin.Log.LogWarning(
                    "SoulRecorder HANDS restore failed after " + reason + ": " + detail +
                    ". Interaction ownership was released to prevent a permanent lock.");
            }

            ClearSavedRuntimeState();
            ReleaseInteraction(succeeded, detail);
        }

        private void ReleaseInteraction(bool restorationSucceeded, string detail)
        {
            ClearSavedRuntimeState();
            Plugin.Log.LogInfo("SoulRecorder HANDS interaction released.");
            RaiseReleased(restorationSucceeded, detail);
        }

        private void RaiseReleased(bool restorationSucceeded, string detail)
        {
            Action<bool, string> handler = InteractionReleased;
            if (handler == null)
            {
                return;
            }

            foreach (Action<bool, string> subscriber in handler.GetInvocationList())
            {
                try
                {
                    subscriber(restorationSucceeded, detail);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning(
                        "SoulRecorder HANDS release subscriber failed: " + ex.Message);
                }
            }
        }

        private void ClearSavedRuntimeState()
        {
            SoulRecorderNativePresentation.Release("hands ownership cleared");
            _player = null;
            _previousControllerName = null;
            _acquireStartedAt = 0f;
            _acquireToken = 0;
            _acquireCompleted = null;
            _deferredRestoreReason = null;
            _restoreStartedAt = 0f;
            _restoreToken = 0;
            _restoreReason = null;
            _restoreAttempt = 0;
            _restoreRecoveryStarted = false;
            _cancelAcquisition = false;
            _restoreCancelledAcquisition = false;
            _restoreObservation.Reset();
        }

        private Action<bool> TakeAcquireCompletion(Action<bool> fallback)
        {
            Action<bool> completed = _acquireCompleted ?? fallback ?? (_ => { });
            _acquireStartedAt = 0f;
            _acquireToken = 0;
            _acquireCompleted = null;
            return completed;
        }

        private void DeferRestore(string reason)
        {
            _deferredRestoreReason = reason ?? "empty-hands acquisition recovery";
            Plugin.Log.LogInfo(
                "SoulRecorder HANDS restore queued for the next update (" +
                _deferredRestoreReason + ").");
        }

        private static string DescribeItem(Item item)
        {
            if (item == null)
            {
                return "<none>";
            }

            return "template=" + item.TemplateId + ", id=" + item.Id;
        }

        private static string DescribeController(
            Player.AbstractHandsController controller)
        {
            if (controller == null)
            {
                return "<null>";
            }

            return controller.GetType().FullName + "[" +
                DescribeItem(controller.Item) + "]";
        }
    }
}
