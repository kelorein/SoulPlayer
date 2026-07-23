# Contributing to SoulPlayer

Bug reports, focused improvements, and compatibility fixes are welcome.

## Before opening an issue

1. Install the latest SoulPlayer release.
2. Confirm the issue on SPT 3.9.8.
3. Check existing issues for duplicates.
4. Collect the relevant section of `BepInEx/LogOutput.log`.

## Building locally

SoulPlayer targets .NET Framework 4.7.2 and compiles against an existing SPT 3.9.8 installation.

```powershell
dotnet build SoulPlayer.csproj -c Release -p:SptRoot="C:\Path\To\SPT"
```

The SPT installation is required only for compile-time game and BepInEx references. Do not commit SPT or Escape from Tarkov files.

## Pull requests

- Keep each pull request focused on one change.
- Match the existing C# style.
- Avoid per-frame allocations and repeated Unity scene searches.
- Disable hidden interface work during raids.
- Describe the SPT version and test scenario used.
- Update `CHANGELOG.md` when behavior changes.

SoulPlayer's source is MIT-licensed. Bundled third-party runtime libraries retain their original licenses as documented in `THIRD-PARTY-NOTICES.txt`.

