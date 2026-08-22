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

This confirms that SPT already has a working first-person utility-item/hand-animation framework, but not the exact Audio Recorder system from current live EFT.

## Implication for SoulPlayer

Do not attempt to hook a nonexistent recorder class in this SPT build.

Next research step is to compare the current live EFT `Assembly-CSharp.dll` with the SPT assembly and identify the recorder/tape classes and their hand-animation entry points. If the live implementation depends on asset bundles that are absent from SPT, SoulPlayer must not redistribute copied BSG assets. In that case the preferred fallback is to adapt an existing SPT-owned runtime hand-controller path (for example the radio-transmitter/compass path) around a legally distributable recorder model.

## Development probe

`Utils/RecorderDiagnostics.cs` remains useful for runtime discovery of loaded asset names/animation objects if later testing suggests recorder-related assets exist in the SPT client despite the managed implementation being absent.
