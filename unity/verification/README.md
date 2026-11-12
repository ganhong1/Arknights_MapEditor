# Clean-package acceptance

## Finished-level corpus

Build the ZIP with Python 3.9+ from a completed semantic audit and its original
source root. The builder reads the audited representatives, verifies their
SHA-256 digests and writes a single archive without expanding a second corpus.

```text
python unity/verification/build-finished-levels.py <audit-directory> <game-data-source-root> <package>/Editor/Catalog/finished-levels.zip
```

For a full graphics audit, prepare an isolated Unity 2021.3 project with the
local package. Set `ARK_MAP_LEVEL_RENDER_REPORT` to a new absolute JSONL path.
Run Edit Mode tests with graphics enabled and filter to
`FinishedLevelLibraryTests`. Each record identifies the archive body, parse
and render status, mesh chunk count, non-background pixels, fallback tile IDs
and missing visible preset entity icons. The report writes a final total after all
bodies and verifies preview-scene cleanup. The corpus test is skipped during
ordinary Edit Mode runs.

The separate [art resource sampling harness](art-resources/README.md) tests
external AssetBundle rendering in an isolated Unity project. It is independent
of package acceptance and does not run in the interactive map workbench.

These tools are outside the UPM package. The C# file is a `.cs.txt` template;
it does not compile into the product. Unity 2021.3.39f1 with Windows build support
is required for the real Player build. Use a separate, disposable project and
keep it closed between phases. Never point setup at a development project.

1. Prepare an empty host with `../prepare-dev-project.ps1`, supplying
   `-ProjectPath`, `-SharedPackages`, `-PackageArchive` and optionally `-Coverage`.
2. Copy its generated `Packages/manifest.json` to an external baseline file.
   Preserve this file through all phases. It identifies the candidate archive.
3. Run `setup-package-acceptance.ps1 -ProjectPath $project -BaselineManifest $baseline`.
   Setup requires empty Assets, matching manifest bytes and an unopened project.
   It copies the harness under `Assets/MapEditorAcceptance/Editor` and records
   the baseline hash. It does not start Unity.
4. Run `../run-editmode-tests.ps1` against the project if needed, with `-Coverage`
   when coverage dependencies were included. Do not overlap Editor owners.
5. Invoke `run-package-acceptance.ps1` three times, with `-Phase installed`, then
   `uninstall`, then `reinstall`. Each invocation requires `-EditorPath`,
   `-ProjectPath`, `-BaselineManifest`, and a **new** `-OutputPath`.

The runner starts Unity hidden and waits for its exit. Uninstall removes only
this package and its testables entry from the baseline; reinstall restores that
baseline. It does not delete Assets, scenes, caches, or other directories.
The setup marker and baseline hash must match before manifest mutation.

Each phase verifies the Editor assembly presence/absence, exclusion from the
Player compilation list, an actual Win64 Player build, and absence of MapEditor
DLLs in that Player. A self-created scene and text asset remain in
`Assets/MapEditorAcceptance`; their content is checked across all three phases.
Results are written to `acceptance.json`, alongside `editor.log` and `Player/`.
An unavailable build module is a failed acceptance prerequisite, not a pass.
This is a packaging/isolation check, not a game-runtime or visual-rendering test.

Use storage on the desired disk for the project, baseline and outputs. TEMP,
TMP and UPM cache are scoped to the output and restored after execution. The
tools contain no machine-specific installation or storage paths.

## Interactive 60 × 60 Editor performance

`MapEditorInteractivePerformance.cs.txt` is an opt-in harness, not package code.
Use a separate disposable Unity 2021.3 project with the candidate package installed.
Copy the template to `Assets/Editor/MapEditorInteractivePerformance.cs` in that
project. Create `.mapeditor-interactive-performance` in its project root, containing
exactly `Arknights.MapEditor.InteractivePerformance.v1` (a trailing newline is allowed).

Start that dedicated Editor with the following arguments, substituting paths:

```text
-projectPath <dedicated-project> -executeMethod MapEditorInteractivePerformance.Begin
-mapEditorInteractivePerformance -mapEditorPerformanceOutput <new-absolute-report.json>
-logFile <new-editor.log>
```

Do **not** pass `-batchmode`, `-nographics` or `-quit`. The harness opens its own
MapEditorWindow and reuses the existing SceneView (creating one only if absent), invokes the actual `OpenPreview` and bound paint
callback, and leaves the full JSON inspector and normal Refresh/Undo paths enabled.
No automatic initialization hook is installed. It refuses to run without both the
explicit flag and project marker, or to overwrite an existing report.

After at least three seconds, ten recorded EditorLoop samples and two observed
SceneView repaints, it performs five rounds of 100 single-cell strokes on a 60 × 60
document. Two tile definitions alternate at the same cell so every stroke is a
real edit. Each next stroke waits for the prior EditorLoop sample to complete.
The command-bearing sample is read at a later EditorApplication update;
`EditorLoop` must report `TimeNanoseconds`, converted to milliseconds by `/ 1e6`.
The recorder explicitly selects `CollectOnlyOnCurrentThread`, without the default
`SumAllSamplesInFrame` or wrapping. Thus samples represent completed main-thread
EditorLoop **invocations**, not sums of multiple loops within a profiler frame.
Each must have `Count == 1` and must enclose its synchronous command duration
(0.05 ms timer tolerance); these checks are not waived when instrumentation fails.
Report diagnostics preserve the issued index, available count, raw Value/Count,
neighboring samples, command time and repaint count, including the failing sample.
Command stopwatch samples and full CPU EditorLoop invocation samples are distinct report
arrays with nearest-rank p95. This does not measure GPU presentation latency.
Every round must include at least one actual repaint of the owned SceneView;
missing repaint, aggregated/empty samples or unavailable markers fail the run.

The report records hardware, Unity version, non-batch mode, sample/repaint counts,
per-round and aggregate p95, failure details and cleanup result. `valid` means the
measurement protocol completed, not that a performance budget was met. The internal
deadline is 180 seconds after Begin (initial import time excluded). Success or
failure closes only harness-owned windows, restores the previous stage, writes a
create-only report and exits this explicitly authorized dedicated Editor. Import or
compile failures happen before Begin; the launching process must separately bound
that startup and inspect its log. Keep project, cache and output paths on the
desired data disk. Do not run this harness in an existing user editing session.

Recorder contract sources: installed Unity 2021.3.39f1 SDK
`Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.xml`, and Unity's
[2021.3 recorder options implementation](https://github.com/Unity-Technologies/UnityCsReference/blob/2021.3/Runtime/Profiler/ScriptBindings/ProfilerRecorder.bindings.cs),
[sample Count definition](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorderSample.Count.html),
[per-frame aggregation option](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorderOptions.SumAllSamplesInFrame.html).
The first default-options run was rejected before any timing was accepted; its
report lacked raw Count/Value diagnostics, so aggregation is a plausible mechanism,
not a retrospectively proven numerical cause. Only a new diagnostic run can verify
that the explicit options and invocation-to-command correlation hold on this Editor.

Instrumentation diagnostics: warmup still incomplete after ten seconds emits one
snapshot (not a repeating log); every 25 completed strokes emits bounded progress.
The final report always snapshots recorder Valid/IsRunning/Count/LastValue and
Profiler.enabled/profileEditor before disposal. The deadline is unchanged.
For a separately approved diagnostic rerun, `-mapEditorEnableEditorProfiling`
enables the public `UnityEngine.Profiling.Profiler.enabled` and
`UnityEditorInternal.ProfilerDriver.profileEditor` properties, plus the Editor
recording switch `UnityEditorInternal.ProfilerDriver.enabled`, only in this marked
dedicated host, records these instrumentation settings, and restores all on exit.
It does not enable deep profiling. Compare reports only with their profiling
settings noted; enabling profiling has its own measurement overhead.
The Editor recording switch follows Unity 2021.3's own
[ProfilerWindow.SetRecordingEnabled](https://github.com/Unity-Technologies/UnityCsReference/blob/2021.3/Modules/ProfilerEditor/ProfilerWindow/ProfilerWindow.cs)
and [ProfilerSession bindings](https://github.com/Unity-Technologies/UnityCsReference/blob/2021.3/Modules/ProfilerEditor/Public/ProfilerAPI.bindings.cs).
Diagnostics also read `connectedProfiler`, `lastFrameIndex`, and CPU-area enablement;
the harness does not assign guessed connection identifiers or change CPU-area settings.

Measured Unity 2021.3 non-aggregated marker boundary: the fourth instrumented run
showed that `Count` includes the currently open EditorLoop slot. Reading index
`Count-at-issue` on the next update selected a new open slot with negative Value,
while its preceding slot had the positive completed command-bearing duration.
The harness therefore records `issueIndex = Count-at-issue - 1`, waits for
`Count > issueIndex + 1`, and reads the original index. It preserves issue-time
Count/Value as diagnostics and still rejects nonpositive/aggregated samples or a
duration shorter than the enclosed command. The earlier failed report remains
failed evidence; no measurements are rewritten or retroactively accepted.
The dedicated test requires exactly one SceneView and records before/measurement
view counts. Existing extra SceneViews fail the precondition rather than being
silently closed. A reused view's pivot/rotation/size/orthographic settings are
restored; only a SceneView explicitly created by this harness is closed.

Runs05/06 both yielded500 independently checked samples. Before issuing a stroke, the current slot must have negative Value and Count1; otherwise the harness refuses it. Run06 uses immutable document caching and the default collapsed advanced JSON panel: commandp95 19.8717ms, CPU EditorLoop invocationp95 25.8167ms (five rounds, each100 repaints). Run05 measured38.9749/55.8755ms. Reports remain distinct; expanded JSON and GPU presentation are not measured.

GPU presentation follow-up uses PresentMon 2.6.0 in a separately authorized elevated process. Capture is scoped by the dedicated Editor PID and a unique ETW session; input tracking is disabled. The harness waits for an explicit capture-ready file before the measured phase, and records Windows QueryPerformanceCounter/Frequency directly. Mono Stopwatch ticks must not be assumed to share ETW's QPC epoch. Analyze only presents inside the measured Windows-QPC interval, grouped by swap chain; do not mix startup, warmup, unrelated processes, dropped frames, or CPU invocation duration into displayed-frame latency. The capture process has a bounded timer and both test processes must exit. No screenshot or visual inspection is involved.
