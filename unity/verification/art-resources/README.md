# Art resource sampling harness

This harness is research tooling outside the UPM package. It loads supplied game
AssetBundle copies into a dedicated Unity **2021.3.39f1 Windows** project, renders
to GPU targets, saves PNG/JSON, unloads its resources and exits. It does not
implement the editor's art library or execute the game's custom MonoBehaviours.

Use a new storage root on the intended data drive. The only accepted project
layout is `<probeRoot>/UnityProbeProject`; the runner checks this path and batch
mode before executing. Do not use the interactive workbench project.

## Inputs

- Copy `ArknightsArtProbe.cs.txt` to `UnityProbeProject/Assets/Editor/ArknightsArtProbe.cs`.
- Make Newtonsoft.Json 3.2.2 available in the project. Required Unity modules:
  assetbundle, animation, audio, imageconversion, imgui, jsonserialize,
  particlesystem, physics, physics2d, terrain, terrainphysics and uielements.
- Place processed bundle copies under `<probeRoot>/Bundles`, preserving paths.
- `offline-bundles-manifest.json`: object with `sourceRoot` and `records`, each
  record containing `relative` and absolute `output`. The raw negative control
  additionally reads the original `bg_lmc.ab` below `sourceRoot`.
- Optional `extra-load-paths.json`: array of `{ "relative": "...", "path": "..." }`.
  Preserve a separate provenance record for reused historical dependencies.

The campaign manifests name the actual sample packages. They also preserve
original/output hashes, Unity version, platform, CAB identity and decoded-node
equality. The harness itself does not decode proprietary compression and does
not copy or modify original game packages.

## Invocation

```text
Unity.exe -batchmode -projectPath <probeRoot>/UnityProbeProject
  -executeMethod ArknightsArtProbe.Run
  -probeRoot <probeRoot> -probeOutput <new-output-directory>
  -probeMode <mode> -GICacheFolder <cache-on-data-drive>
  -logFile <new-log-path>
```

Start the process hidden. Scope TEMP, TMP and UPM_CACHE_ROOT to the desired data
drive. Do not use `-nographics`: these tests require rendering. Do not use
`-quit`: PlayMode scene tests continue over Editor updates and exit themselves.
Run one owned process at a time, record its PID and exact start time, and confirm
exit before editing the harness or starting another run. Existing reports are
not overwritten.

| Mode | Samples |
| --- | --- |
| `static` | Raw compression negative control, textures, materials, model, Cubemap, particles, initial animation and logic tiles |
| `scene` | 5-10 scene, runtime components, original/disabled/restored lightmaps, async unload |
| `effects` | Fixed-camera particle time samples; initial animation probe |
| `particles` | Only the two particle Prefabs, after correcting seed initialization order |
| `bindings` | Scene materials, model-material binding, normal map, isolated material animation, Sprite, initial TrailRenderer |
| `transform` | Electric tower Legacy clips and map effect Avatar/Transform clips |
| `trail` | Original TrailRenderer, baked mesh and diagnostic material |
| `trail-live` | TrailRenderer with path input across PlayMode updates |
| `trail-control` | Original, explicit width reset and new Unity component comparison |

Read `samples[].exception`, `fatal`, logs and captures separately. `completed`
means the workflow finished; it is not an all-samples-pass flag. A blank image
may be an animation endpoint, a logic-only Prefab, missing input or a rendering
failure. Renderer counts and `Shader.isSupported` alone do not prove appearance.

## Current campaign

See [2026-11-09 results](../../evidence/ART_RESOURCE_SAMPLING_20261109.md).
The campaign stores large assets/images outside the repository. Its final
harness is archived here; reports from earlier diagnostic runs retain their
original observations and are not relabelled as reruns of this final source.
