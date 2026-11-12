"""Extract the original, cell-referenced TileData samples for the Unity catalog."""

import argparse
import json
import subprocess
from collections import defaultdict
from pathlib import Path
from typing import Any


TileData = dict[str, Any]
Sample = dict[str, Any]


def canonical(tile: TileData) -> str:
    return json.dumps(tile, sort_keys=True, ensure_ascii=False, separators=(",", ":"), allow_nan=False)


def scan(levels: Path, tile_ids: set[str]) -> tuple[list[str], dict[str, dict[str, Sample]]]:
    samples: dict[str, dict[str, Sample]] = defaultdict(dict)
    paths = []
    for path in sorted(levels.rglob("*.json")):
        level = json.loads(path.read_bytes())
        map_data = level.get("mapData") if isinstance(level, dict) else None
        if not isinstance(map_data, dict):
            continue
        grid, tiles = map_data.get("map"), map_data.get("tiles")
        if not isinstance(grid, list) or not isinstance(tiles, list):
            continue
        used = {index for row in grid if isinstance(row, list) for index in row
                if type(index) is int and 0 <= index < len(tiles)}
        source_index = len(paths)
        paths.append(path.relative_to(levels).as_posix())
        for index in sorted(used):
            tile = tiles[index]
            if not isinstance(tile, dict) or tile.get("tileKey") not in tile_ids:
                continue
            sample = samples[tile["tileKey"]].setdefault(canonical(tile), {
                "tileData": tile, "exampleIndex": index, "sources": [],
            })
            if not sample["sources"] or sample["sources"][-1] != source_index:
                sample["sources"].append(source_index)
    return paths, samples


def recommendations(rows: list[Sample]) -> list[int]:
    def rank(index: int) -> tuple[int, int, str]:
        sample = rows[index]
        tile = sample["tileData"]
        return (len(tile.get("blackboard") or []) + len(tile.get("effects") or []),
                -len(sample["sources"]), canonical(tile))

    fields = {"tileKey", "heightType", "buildableType", "passableMask", "playerSideMask", "blackboard", "effects"}
    complete = (index for index, sample in enumerate(rows) if fields <= sample["tileData"].keys())
    return sorted(complete, key=rank)[:3]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--levels", type=Path, required=True)
    parser.add_argument("--catalog", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    if not args.levels.is_dir():
        parser.error("the level directory must exist")
    revision = subprocess.check_output(
        ["git", "-C", str(args.levels), "rev-parse", "HEAD"], text=True,
    ).strip()
    dirty = subprocess.check_output(
        ["git", "-C", str(args.levels), "status", "--porcelain", "--", "."], text=True,
    )
    if dirty.strip():
        parser.error("the input level directory must match the recorded Git revision")
    catalog = json.loads(args.catalog.read_bytes())
    tile_ids = {entry["id"] for entry in catalog["entries"] if not entry.get("visualOnly")}
    paths, samples = scan(args.levels, tile_ids)
    if not samples:
        parser.error("no matching samples found")

    result = {"version": 1, "revision": revision, "levels": paths, "tiles": {}}
    for tile_id, configs in sorted(samples.items()):
        rows = list(configs.values())
        rows.sort(key=lambda row: (-len(row["sources"]), canonical(row["tileData"])))
        result["tiles"][tile_id] = {"recommended": recommendations(rows), "samples": rows}
    with args.output.open("w", encoding="utf-8", newline="\n") as output:
        output.write(json.dumps(result, ensure_ascii=False, separators=(",", ":"), allow_nan=False) + "\n")
    print(f"{len(paths)} levels, {len(samples)} tiles, {sum(map(len, samples.values()))} samples")


if __name__ == "__main__":
    main()
