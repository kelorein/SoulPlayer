# SoulPlayer v0.9.1 release comparison

Run `tools/release/Test-SoulPlayerReleaseComparison.ps1` with PowerShell 7, supplying `-BaselineDll`, `-CandidateDll`, `-ReportPath`, and `-SelfTest`. `-SptRoot` defaults to `D:\SPT_4.1.2` and supplies the installed Mono.Cecil metadata reader. Nothing is installed into EFT or modified in either DLL.

The baseline is pinned to the user-tested optimized DLL SHA-256:

`9A5EB4DC26FDD4909D867F0592FB6752418FF6B439C31521728047A90C710C18`

The gate constructs a graph of managed types, fields, methods, properties, events, attributes, signatures, and IL operands. It matches that graph bijectively, including all reference edges. Compiler-generated names are not used as identity; the code, declaration shape, owners, callers, and field references establish identity instead. Method and field counts, shared references, and aliasing are preserved. Partition refinement narrows candidate matches, followed by an explicit complete edge check. Ambiguous matches that exceed the search bound fail for manual review.

This is a conservative structural IL/metadata comparison. It is not a general proof that arbitrary different programs have equivalent behavior, and it does not replace EFT smoke testing.

## Exact allowances

1. **Generated symbol names:** compiler-generated type, lambda, closure, and cache-field names may differ only when the full declaration/body/reference graph matches. Ordinary type/member names, public API shape, and property names remain checked. There is no global digit stripping or blanket exemption for generated method bodies.
2. **Rebuild representation:** metadata tokens and declaration-table order, MVID, PE timestamp/checksum/layout, debug symbols/sequence points, and instruction byte offsets are not compared. Branch and switch targets use instruction ordinals; exception-handler boundaries and ordering remain checked. Short/long branch encoding is normalized, but the branch operation and target are retained.
3. **Approved version values only:** assembly version `0.9.0.0` to `0.9.1.0`, assembly file-version attribute, the BepInPlugin version argument, the exact startup version message, and the exact `LOCAL MUSIC` UI version label. Other string and numeric constants are compared, including floating-point bit patterns. Embedded managed resources must remain byte-identical; no asset change is excused as version metadata. The compiler-generated native PE version resource is part of the ignored PE representation.
4. **The named development probe:** the baseline `SoulPlayer.Utils.RecorderDiagnostics` type and nested helper types, the Plugin probe property/accessors/backing field, exactly four startup instructions that add/store that probe, and exactly three instructions that announce its hotkey. The candidate must not contain the probe. Every remaining startup instruction is compared, and any remaining production reference to an excluded symbol fails. Branches or exception regions entering an excluded instruction fail. This allowance does not remove or bypass any Harmony registration.
5. **Performance instrumentation:** the normal Release must contain zero profiler call sites. The retained conditional profiler helper must have no fields and only empty return bodies. There is no blanket exception for code merely named profiler or diagnostics.

## What remains protected

- Every retained method body, opcode, operand, branch, switch, exception region, local type, calling convention, and implementation flag.
- Constants, settings names/defaults, public/configuration shape, assembly references, and custom attributes, including Harmony patch attributes.
- Startup wiring and registrations, playback lifecycle, SoulRecorder input, mini-player placement, exact Main resume, readiness logic, and performance optimizations.
- All four embedded resources, including authored anchors and recorder overlay images.

## Gate self-checks

The `-SelfTest` option makes changes to in-memory copies only. It verifies acceptance of the genuine candidate, generated symbol renumbering, and metadata declaration reordering/MVID changes. It verifies rejection of changes to startup registration text, startup opcodes, Harmony attributes, the default M hotkey, configuration keys, layout constants, recorder branches, exact-resume instructions, readiness branches, public visibility, embedded anchors, and a renamed generated lambda with a changed body. All 15 cases must pass before the gate result is accepted.

## Expected remaining difference from the tested build

The production IL graph is unchanged under these explicit allowances. The version becomes 0.9.1; the developer recorder probe and its keyboard scan/report behavior are removed from normal Release and retained in PlacementTools. Ten generated Awake lambda names and ten associated cache-field names are renumbered as a compiler consequence. No gameplay feature is added or removed, no authored anchor is edited, and no bundle rebuild is needed.

The final candidate still needs the user's short EFT smoke test before any commit, tag, push, or GitHub release.
