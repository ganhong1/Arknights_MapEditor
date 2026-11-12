# P0/P1 implementation checkpoint

> Historical checkpoint. Current P0–P6 status and candidate02 evidence are in [P0_P6_CHECKPOINT_20261103.md](P0_P6_CHECKPOINT_20261103.md). Remaining statements below describe the earlier snapshot, not the latest working tree.

Status: **resumed; P1 implementation active, not fully accepted**. Updated: 2026-11-03.

## Latest verified checkpoint (supersedes old resume blockers below)

- Unity 2021.3.39f1 independent D-drive DevProject is connected through MCP.
  The existing asset-toolchain project was not modified. MCP is a development-host
  dependency, not a distributed plugin dependency.
- Graphics-enabled batch verification-05 passed 67/67. The preceding no-graphics
  run passed 65/67; the two failures required attached EditorWindow panels.
- MCP job `8d15a98299f1404c85cff67b4d2d99dd`: 78/78 passed.
- Subsequent targeted MCP job `cb7974491ed345d3a1efb9c68fa9f93c`: 16/16 passed.
  These counts overlap and must not be summed as unique tests.
- Actual domain reload now asserts a changed static GUID, retention of the original
  window, applied document and unapplied draft, and exactly one Undo subscription.
- Repeated preview creation and window destruction release owned meshes and restore
  the preceding stage. A failed preview open closes its newly created preview scene.
- Recovery envelopes retain applied JSON, invalid draft text, saved checkpoint and
  source digest separately. Invalid restores leave session/Undo unchanged; saving
  a restored document still rejects external file changes.
- Window offers explicit recovery-copy save/restore in the host Library directory.
  This is **not automatic crash recovery**. No source file is overwritten by restore.

Machine-readable results: `mcp-editmode-20261103.json` and
`mcp-lifecycle-20261103.json`. Current console query returned zero errors.

Remaining P1 gates: automatic recovery policy, full save/discard/cancel close
interaction, foreign-stage interaction, and measured coverage >=80%. P2 has not
started. Tests are Editor integration checks, not game/runtime verification.

## Completed implementation work

- Frozen feature working-tree input hashes in `web-migration-baseline.json`.
- Copied approved v0.2 plan into feature workspace without replacing Web changes.
- Added Editor-only UPM assemblies and an isolated D-drive test-project bootstrap.
- Implemented immutable LevelData JSON parsing/editing and exact fraction retention;
  unknown fields are retained, map references validated. Strict input rejects
  duplicate keys and unsupported JSON extensions. This is not full LevelData validation.
- Added UTF-8 size-limited atomic file save with optimistic digest conflict checks.
  The final check/replace interval is not a cross-process atomic compare-and-swap.
- Added serialized document session, separate saved checkpoint, Undo, UI Toolkit
  JSON window and a snapshot-only PreviewSceneStage prototype.

## Earlier verification sequence (2026-11-02)

| Run | Result | Meaning |
| --- | --- | --- |
| Initial RED | Missing Core/Storage/Session types; also missing explicit Newtonsoft reference | Tests were discovered before implementations; dependency reference was fixed separately |
| green-results | 53 passed / 0 failed | Earlier source snapshot only, not final working tree |
| verification-02 | 57 total, 54 passed, 3 failed | Unicode escaped surrogate rejection; two window tests used unattached UI fields |
| preview-red | Missing MapPreviewStage | Preview lifecycle test discovered before prototype implementation |
| verification-03 | Editor exit 1 before compilation | No valid Unity Editor license / headless entitlement; no new test results |

Failed Unicode case was addressed in strict pre-parse scanning. Window tests were
changed to attach real EditorWindow panels, and draft Undo logic was corrected.
These changes and the added preview stage are **not yet verified by a successful
final run**. Save→Undo→Redo checkpoint test passed in verification-02.

Independent review identified draft overwrite/Undo risk, escaped Unicode loss,
and insufficient real Domain Reload evidence. Fixes are in source; real reload,
crash recovery, preview-stage cleanup and final coverage remain open.

## Earlier resume order (resolved items superseded above)

1. Resolve the installed Editor's normal licensing availability; do not bypass it.
2. Run the current EditMode suite in a fresh output directory and repair failures.
3. Complete preview stage isolation and actual reload/recovery tests.
4. Add coverage instrumentation and verify ≥80% before accepting P1.
5. Only then proceed to P2 brushes/catalogs and incremental map updates.

Raw logs and full XML remain under the local D-drive `map-editor-unity` test area,
not in the public package. No emulator, private server or game process was operated.
No commit or push was made. The isolated batch Editor exited on its own.
