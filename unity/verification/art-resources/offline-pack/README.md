# Offline art pack build and verification

Use Unity **2021.3.39f1** on Windows. The offline pack contains a searchable candidate index and standard LZ4 AssetBundles. The package is installable independently of the game files. In the Unity workbench, open the art library and choose the ArtPack directory. Open the layered gallery to view textures, materials, models, and effects from that installation.

## Build

1. Choose a working directory and an installation directory on the data drive. Run `plan_pack.py` with `--source` (the PC AssetBundle root), `--sample-report` (the 5-10 probe report), `--verified-refs` (the prefab preload object receipt), and `--out` (the plan directory). The PC manifest and two current-version CAB providers are pinned by SHA-256.
2. Copy the selected scope's catalog to the installation as `catalog.json`. Run `bundle_nodes.mjs prepare <sources.json> <work> <installation> <unity-js/src>` with the existing tsx loader. The preparer writes uncompressed UnityFS intermediates to `<work>/Staging` and records the original hash and decoded node hash.
3. Copy `BuildOfflineArtPack.cs` into `Assets/Editor/` of an isolated project named `UnityVerification`. Set `ARK_PACK_WORK` to the working directory and `ARK_OFFLINE_ART_PACK` to the installation directory. Run Unity with `-batchmode -nographics -executeMethod ArkMapEditor.Verification.BuildOfflineArtPack.Run`. Unity recompresses the intermediates through `BuildCompression.LZ4Runtime`. Output becomes visible after each successful atomic move.
4. Run the same Node tool with `verify`. It uses the standard UnityFS reader, checks the compression markers and round-trips every node. Keep `bundle-integrity.json` with the installable pack. The staging directory is disposable after verification.
5. For a partial restart, use a new work directory containing a `paths.txt` of missing targets and set `ARK_PACK_STAGING` to the existing staging root. Preserve incomplete files separately before rerunning.

## Tests

- Run `python -B -m unittest -v test_plan_pack` from this folder.
- Copy `OfflineArtPackTests.cs` into an EditMode test assembly and `OfflineArtPackSceneTests.cs` into a PlayMode test assembly in `UnityVerification`. The harness used Unity Test Framework 1.1.33 and NUnit 1.0.6. Test assemblies use Unity's `TestAssemblies` reference. Fixture lookup requires only `ARK_OFFLINE_ART_PACK`.
- Run `-runTests -testPlatform EditMode` for inventory, scene address, prefabs, material, async loading and release. Run `-runTests -testPlatform PlayMode -force-d3d11` for the 5-10 map scene. Pass separate local `-testResults` and `-logFile` paths. The PlayMode test covers scene lighting, an authored-camera GPU readback and teardown.
- Verification distinguishes file integrity, recorded dependency closure and representative engine tests. Each distinct map or effect still has its own preview coverage.

The scope records the 7,874 candidates from the fixed PC resource selection. Some candidates require a game-side initialization path or further classification before they can be installed as editor choices.
### Camera and environment preview index

Use the existing pack as the input for `python extract_preview_metadata.py <ArtPack>` with UnityPy 1.25.4. The command writes `preview-metadata.json` beside `catalog.json`. Keep those two files with the Bundles directory when moving the pack. The generated index records seven camera components and 311 environment profiles, with 64-bit path IDs stored as strings and the SHA-256 of the catalog. The Unity editor reads it when previewing the corresponding entries.
