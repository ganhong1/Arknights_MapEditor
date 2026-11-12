# Arknights LevelData Editor — development preview

Editor-only UPM package targeting Unity 2021.3.39f1. Install this directory's
`package.json` as a local package, then use **Tools → Arknights → Map Editor**.

Source now includes LevelData editing, bounded UTF-8 persistence, Unity Undo,
SceneView schematic tools, frozen terrain templates, recovery journals and separate
`.arkmap` art mappings. These are **implemented source capabilities**, not a claim
that all user workflows or P5 distribution checks have passed. The schematic is
not game rendering; art mappings do not make resources work in the game.

Read [the user guide](Documentation~/map-editor.md) for installation, document
handling, recovery limitations and removal. The self-authored BasicMap sample is
a data-editing example, not a game-ready stage or verified playable map.

Current verification is incomplete. See the repository's
`unity/evidence/P1_STATUS.md`. Do not treat this development package as a finished
map editor. No game connection or runtime assembly is included.

Use the repository `unity/prepare-dev-project.ps1` and
`unity/run-editmode-tests.ps1` with explicit local paths for an isolated D-drive
test project. Local development manifests may contain resolved machine paths;
the distributed package declares versioned dependencies only.
