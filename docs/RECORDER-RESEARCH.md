# Recorder research

## SPT managed-code inspection

Inspected the user's current SPT `EscapeFromTarkov_Data/Managed` assemblies offline on 2026-08-22, including both:

- `Assembly-CSharp.dll`
- `Assembly-CSharp.dll.spt-bak`

### Result

The current SPT client does **not** expose the newer EFT Audio Recorder / collectible audio-tape implementation in managed code.

No cassette/audio-tape/Audio Recorder symbols or user-string literals were found in the current `Assembly-CSharp.dll` or the `.spt-bak` copy.

The existing special-item hands infrastructure does contain reusable nearby systems, notably:

- `ObjectInHandsAnimator.ShowCompass`
- `ObjectInHandsAnimator.ShowRadioTransmitter`
- `ItemHandsController.ToggleCompassState`
- `ItemHandsController.SetCompassState`
- `ItemHandsController.CurrentRadioTransmitterState`
- `EFT.RadioTransmitterController`
- `EFT.ClientItems.ClientSpecItems.RadioTransmitterView`
- the existing `UsableItemController` / first-person utility-item operation framework

This confirms that SPT already has a working first-person utility-item/hand-animation framework, but not the exact Audio Recorder system from current live EFT.

## Live EFT IL2CPP inspection

Inspected the user's current live EFT IL2CPP files offline on 2026-08-22:

- `GameAssembly.dll`
- `EscapeFromTarkov_Data/il2cpp_data/Metadata/global-metadata.dat`

The metadata header identifies the live build as **EFT 1.1.0.1.46911**.

BSG's transformed IL2CPP metadata body was decoded locally for interoperability research only. No game assets or decrypted metadata are committed or redistributed with SoulPlayer.

### Recorder implementation confirmed

The live build contains the exact physical recorder/tape system needed for the SoulPlayer design. Relevant live managed types include:

- `CommonAssets.Scripts.Audio.AudioTapesCollection`
- `EFT.InventoryLogic.RecorderHandler`
- `EFT.InventoryLogic.RecorderItemTemplate`
- `EFT.InventoryLogic.RecorderItem`
- `EFT.InventoryLogic.TapeItemTemplate`
- `EFT.InventoryLogic.TapeItem`
- `EFT.IRecorderController`
- `EFT.Player.RecorderHandsController`
- `EFT.Player.RecorderHandsController.RecorderInHandsOperation`
- `EFT.Player.ClientRecorderHandsController`
- `EFT.PlayerRecorderController`
- `EFT.ObservedPlayerRecorderController`
- `EFT.NextObservedPlayer.ObservedPlayerRecorderHandsController`
- `EFT.NextObservedPlayer.RecorderAnimationHandsController`
- `EFT.NextObservedPlayer.UseRecorderCommandMessage`
- `EFT.RecorderInputTranslator`
- `EFT.TapeSubtitlesStorage`
- `EFT.UI.RecorderPanel`
- `UI.RecorderRaidPanel`
- `EFT.UI.UiRecorderController`
- `EFT.UI.TapeCollectedNotificationView`
- `EFT.UI.TapeProgressNotificationView`

Source-path metadata shows the first-person controller is implemented under the same general utility-item hierarchy already present in SPT:

```text
Assets/CommonAssets/Scripts/Player/ObjectInHands/Controllers/UsableItemController/ItemControllers/Recorder/RecorderHandsController.cs
Assets/Scripts/Player/ClientUsableItemController/ClientItemControllers/ClientRecorder/ClientRecorderHandsController.cs
Assets/CommonAssets/Scripts/Player/PlayerRecorderController.cs
Assets/CommonAssets/Scripts/Inventory/Components/RecorderHandler.cs
Assets/CommonAssets/Scripts/Audio/AudioTapesCollection.cs
Assets/Scripts/UI/RecorderRaidPanel.cs
```

### Useful live behavior/symbols

Recovered symbol names confirm the intended interaction flow rather than only the presence of similarly named classes:

- `UseRecorder`
- `PlayStopRecorderAnimation`
- `PlayStopRecorderSound`
- `PlayTape`
- `PlayTapeAudio`
- `InsertTape`
- `EjectTape`
- `SetTape`
- `SetTapeObject`
- `InsertTapeHandler`
- `EjectTapeHandler`
- `PlayTapeHandler`
- `TryFindRecorderInSpecialSlots`
- `TryFindEquippedRecorder`
- `TryFindEquippedRecorderInInventory`
- `TryGetNextTape`
- `GetNextTape`
- `FindTapes`
- `IsRecorderActive`
- `IsRecorderPlaying`
- `IsRecorderEnabled`
- `ActiveTape`
- `TAPE_BONE_NAME`
- `CHANGE_TAPE_DELAY`
- `RecorderButtonPressedDelegate`

Live UI/localization metadata also contains recorder-specific tape collection/progress strings such as:

- `$item_recorder_tape`
- `tapes/tapeFoundNotify`
- `Tapes/Playback{0}{1}`
- `Tapes/StopKey{0}`

### Architecture conclusion

The user's remembered tutorial interaction is real and maps almost exactly to the planned SoulPlayer feature:

**physical recorder -> first-person recorder hands controller -> insert/eject tape operation -> play/stop animation -> active tape -> audio playback -> raid UI/collection notification**.

However, current SPT does not contain these new live-only recorder classes, so SoulPlayer cannot simply invoke `RecorderHandsController` on SPT 4.1.x.

The important positive result is that live EFT built the recorder on the **same `UsableItemController` family that SPT already has**. Therefore the practical SoulPlayer path is to reproduce only the missing recorder-specific layer while reusing SPT's existing first-person utility-item controller/operation infrastructure.

## Next implementation step

1. Map live recorder concepts onto SPT's existing `RadioTransmitterController` / `UsableItemController` operation model.
2. Build a minimal `SoulRecorderController` backend with one active/unlocked tape and play/stop state, without committing any BSG assets.
3. Prototype equip/use behavior with an existing SPT runtime utility-item path first.
4. Scan SPT's installed asset catalogs/bundles for recorder/tape prefab or animation names. If the live assets are absent, do not redistribute them from EFT; use a legally distributable recorder/cassette model and animations instead.
5. After the one-tape prototype works, connect it to SoulPlayer's persistent cassette collection and map-spawn system.

## Development probe

`Utils/RecorderDiagnostics.cs` remains useful for runtime discovery of loaded asset names/animation objects if later testing suggests recorder-related assets exist in the SPT client despite the managed implementation being absent.
