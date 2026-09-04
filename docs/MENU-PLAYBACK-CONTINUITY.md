# Out-of-raid menu playback continuity

## Evidence and scope

Baseline: main at `356c77d69e1e269fa27955df77f6d7fc01315a41`; 505 tests passed before changes.
The first candidate passed 542/542 offline tests. The owner subsequently confirmed
normal Main/Trader/Quest/Flea/Hideout navigation in EFT, but reproduced an interruption
during rapid post-raid OK/Next. The original menu fixes below were established from
source and offline regression tests; the follow-up evidence is recorded separately.

- `StableRaidMenuContext.IsReturnScreen` previously recognized MainMenu, Inventory,
  and result screens only. During an unresolved post-raid session, Traders, Trader,
  FleaMarket, Hideout and other ordinary screens evaluated as
  `WaitingForReturnScreen`. `SoulAudioPlayer.RefreshRaidReadiness` passed that loss
  of readiness to `SetRaidMenuReady(false)`, which paused the active outcome cue
  and blocked the pending exact Main resume.
- `GameState.HasLiveRaidPlayer` treated any active `GameWorld.MainPlayer` as a raid
  player. The reference assembly defines `EFT.HideoutPlayer : EFT.LocalPlayer`;
  its presence must not block an otherwise verified out-of-raid menu return.
  The new exclusion applies only to menu-readiness evidence. Raid ingress and
  the deployment hook retain their existing detection behavior.
- EFT's screen-manager setter publishes a screen-change event when replacing its
  current controller, before `InitScreen` in the prepare-show path. Its
  `ShowPreviousScreen` and `CloseAllScreens` paths can clear the controller.
  A hidden/rebuilding screen or brief null-controller gap previously revoked
  readiness even after a stable ordinary menu had already been reached.

There is no demonstrated direct Stop/Pause call from normal menu navigation before
a raid or after Main resume completes. The implementation does not add a speculative
restart/recovery of stopped audio.

## Post-raid OK/Next follow-up

Owner runtime evidence: the outcome track pauses and the mini-player disappears
while quickly advancing results, then both return at Main. The installed candidate
for that test was SHA-256 `8DD16B6CAF2D091883D0C3536AA8D52CB82F1FD28EB8DD27748547D3233AD2E1`.
The inspected `D:\SPT_4.1.2\BepInEx\LogOutput.log`, line 275, records
`SessionStatistics -> SessionExperience`, time `1.963`, action `Pause`, reason
`RaidLifecycle/WaitingForReturnScreen`. Nearby state evidence has phase `Routed`,
outcome `Survived`, `screenActive=False`, player absent and both loading overlays false.

Audio cause: the screen-change callback refreshes readiness while the incoming
screen is still inactive. `MenuPlaybackContinuity` intentionally does not grant
ordinary-menu START readiness to results screens. The resulting `SetRaidMenuReady(false)`
used to pause the running source. No decoder failure, track replacement, or Main-only
handoff requirement is indicated by this evidence.

UI cause (code trace, consistent with the owner's observation): `ShouldSuspendMenuMusic`
also used `!RaidMenuReady`; the existing overlay state check then called
`SoulMiniPlayer.SetRaidMode(true)` and disabled the overlay canvas. The UI and audio
have separate consumers but the same transient readiness-loss trigger. The overlay
and audio hosts are persistent, not owned by the destroyed result/controller. Null
profile binding skips persistence work and does not own playback or the overlay.

The follow-up explicitly separates three permissions:

- START: existing outcome, screen, player-exit, loader, library and decoder checks.
- CONTINUE: proof recorded after a legitimate post-raid source start. With continuity
  enabled, result-screen, controller, manager, profile/player-reference and overlay
  transients do not revoke this proof. It never dispatches, selects, seeks or starts
  a track. Deployment clears it; the existing deployment/live-raid ingress still pauses.
- DISPLAY: stable scene readiness or that established out-of-raid playback context.
  It does not depend on `isPlaying`, so user Pause/Stop or waiting after natural EOF
  does not hide the persistent mini-player during results navigation.

User Pause/Stop and clip validity retain their independent guards. A user can pause
and resume the existing cue during a UI gap; an unstarted clip cannot bypass START.
Natural cue EOF can be observed during teardown, but a new exact Main resume still
waits for safe START readiness on any supported return screen, not specifically Main.
Disabled continuity uses legacy readiness-based pausing/hiding. Re-enabling can release
only a context pause, never a user Pause/Stop. Proof survives a setting toggle but
never a new deployment. No Unity overlay-only limitation was found in this code;
the owner has now confirmed correct rendering through rapid OK/Next on SPT 4.1.3.

`PostRaidContinuityTests` adds policy/session regressions and runtime wiring checks.
Its monotonic sample progression is an offline transport model, not native AudioSource
evidence. Allocation checks warm and execute 100,000 continuation/display decisions.
The two original source-wiring regressions were demonstrated failing before the fix.

## Ownership and stop-path audit

- `Plugin.Awake` owns `SoulAudioPlayer` on its `DontDestroyOnLoad` host;
  `SoulAudioPlayer.Initialize` creates the primary AudioSource there once at startup.
  The source is 2D and ignores listener pause. No lifetime change was needed.
- `MenuScreenPatch` binds the profile and creates/reuses the independent persistent
  overlay. UI close/deactivation does not call Stop, Pause, or recreate the audio host.
- `SoulAudioPlayer.StopCore` handles explicit Stop; `TogglePause` handles explicit
  pause; `BeginLoad` replaces the clip for an actual playback request; queue EOF
  handles repeat/next/stop. These paths retain their original behavior.
- `BeginRaidSuspension` invalidates pending decoder work and pauses the source
  after capturing the exact Main snapshot. The countdown patch remains authoritative.
  `PreserveMainForRecorder` retains the independent recorder ownership contract.
- `SetRaidMenuReady` is the context-pause boundary. `ProcessRaidPlayback` dispatches
  the routed cue/resume and clears CurrentTrack only on a terminal Finish action.
  `CancelSavedResume` still invalidates pending work for deliberate user choices.
- Audio teardown releases the clip and pending work at plugin destruction. UI
  teardown only removes UI subscriptions. Mini-player content reads the persistent
  player's `DisplayTrack`, `IsPlaying`, and `CurrentTime` directly.
- Tushonka music-volume getter/application prefixes and the persistent mixer muter
  remain unchanged. Menu transitions never restore game music or change SoulPlayer volume.

## Setting and transition behavior

F12 > Player > **Keep music playing across menus**, default **Enabled**.

Normal screens are one context: MainMenu; Inventory (stash/tasks/profile tabs);
Traders/Trader/TraderDialog; FleaMarket; Hideout and its transfer/mannequin/cultist
screens; weapon/equipment builds; profiles; handbook; settings; news/event menus.
Quest tabs and Messenger overlays can retain their parent screen: no audio action
is required when their top-level screen does not change.

After a verified ordinary-menu return, UI activation changes, loading overlays and
controller replacement do not revoke playback readiness. They never change the
track, sample position, queue, shuffle cursor, pending decoder generation or source.
Before that first verified return, outcome, active-screen, player-exit, preloader
and black-overlay gates still apply. Result screens, unknown screens, a missing
screen manager and real raid-player presence cannot acquire ordinary-menu START
readiness. They no longer revoke CONTINUE permission after a legitimate post-raid
start. New deployment clears both policies. Library rescans still gate new dispatch.

Disabling the setting immediately invalidates cached readiness and restores the
original screen whitelist and player-presence behavior. It does not start a track
or undo a deliberate Stop/Pause. New deployment resets continuity unconditionally.

Screen events produce one diagnostic per changed top-level screen:
`SoulPlayer menu playback: from=... to=... track=<id/title> time=... action=... reason=... startReady=... continueAudio=... overlayAllowed=...`.
Action describes the source state at that event. Tabs/overlays retaining the same
screen do not generate artificial events. Readiness logs use the effective policy
result. No new per-frame logging, reflection, searches, library copies or UI rebuilds
were introduced; the existing guarded post-raid readiness polling is retained.

## Validation and manual acceptance

The final candidate passed 563/563 offline tests, Release and PlacementTools builds,
package/asset validation, and performance regression checks. Before committing,
all 563 tests were rerun with `--no-build --no-restore` and passed again.

Owner-confirmed manual EFT acceptance on SPT 4.1.3, for Release DLL SHA-256
`30F5BD6CDE3179A87962863BAEC85756688C587F985212BB8A2C9FC195A29BBD`:

- Main -> Trader -> Quest -> Flea -> Hideout navigation remains continuous.
- The mini-player stays correct during ordinary navigation.
- Post-raid outcome playback survives rapid OK/Next; the mini-player no longer disappears.
- Main playback resumes correctly; no Tushonka interruption was observed.

These are owner-reported runtime results, distinct from automated verification.
The accepted DLL was not rebuilt for the commit. The broader checklists below are
retained for future regression testing; they do not imply additional manual cases
were individually confirmed beyond the observations above.

`MenuPlaybackContinuityTests` covers the requested navigation pairs, repeated
transitions without reselection, exact saved track/sample/queue preservation,
decoder permission, null-controller gaps, unavailable manager, Hideout versus raid
players, Stop/manual selection, legacy mode, result-loading gates and zero allocations
over 100,000 warmed policy evaluations. Source wiring assertions cover persistent
ownership, mini-player binding and retained Tushonka suppression. These tests do not
instantiate Unity's native AudioSource or prove audible runtime continuity.

Run `tools/Test-SoulPlayer.ps1 -SptRoot "D:\SPT_4.1.2"` for the full suite,
MenuContinuity group, raid/exact-resume/performance groups, Release and PlacementTools
builds, package audit and asset validation. No deploy or EFT launch is part of this task.

Manual EFT acceptance, on the exact reported Release DLL after a separately authorized deployment:

1. With continuity enabled and a long Main song around 1:42, traverse Main menu ->
   Stash -> Trader -> quests -> Trader -> Flea -> Hideout -> Inventory -> Profile ->
   Messenger -> Main menu. Repeat quickly, including a quest tab, Hideout enter/exit,
   UI close/reopen and a library-view reopen. Verify uninterrupted audio, unchanged
   song identity, monotonically advancing time, matching mini-player and no extra
   selection/decoder start. Preserve the transition log for the full sequence.
2. Repeat the sequence while a long Extract/Death cue is playing after a raid.
   Let the cue end in Trader or Hideout: Main must resume once at its captured time
   and retain queue/shuffle/repeat. Test direct exact resume with outcome autoplay off.
3. Deploy from normal menus: Main captures its exact position and suspends at
   deployment; exercise recorder Start/Stop/Next in raid; verify silence during
   post-raid loading, the selected outcome cue, then exact Main resume. Repeat after
   death and extraction. A new deployment during a pending resume must remain silent.
4. Stop or pause deliberately and navigate: it must stay stopped/paused. Exercise
   manual selection, media keys and volume presets. Listen for any built-in music
   overlap at every transition; other game sounds and SoulPlayer volume must persist.
5. Disable continuity and repeat the post-raid cue -> Trader/Flea/Hideout sequence:
   legacy readiness pauses/blocks as before. Re-enable in a stable menu; verify it
   releases only the context pause, not a user pause/Stop. Confirm no frame-time spikes
   or repeated unchanged-state diagnostics during menu idle and active raids.

The current continuity candidate has owner-confirmed runtime acceptance as recorded above.

Focused follow-up acceptance (only after separately authorized DLL deployment):

1. Extract with a long outcome cue. As soon as it starts, rapidly press OK/Next
   through exit status, kills, statistics, experience and treatment to Main. Verify
   uninterrupted same-track audio, advancing time, persistent correct mini-player,
   no Tushonka, and no restart on Main arrival. Repeat after death.
2. Repeat with a short cue ending during results. It must finish once; Main resumes
   at its exact saved sample position on the next safe return screen. Repeat with
   outcome autoplay off to exercise direct Main resume through subsequent results.
3. Pause, navigate, resume; then Stop and navigate. Neither deliberate state may be
   overridden. Test setting Disabled for legacy behavior, then re-enable.
4. Recheck Trader/Quest/Flea/Hideout continuity and a new deployment with SoulRecorder;
   verify raid suspension, loading silence, exact Main restoration and no frame spikes.
   Preserve transition logs with the three new permission fields for any failure.
