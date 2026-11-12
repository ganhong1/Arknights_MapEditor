"""Build a read-only terrain projection from the exact level corpus used for terrain samples."""

import argparse
import base64
import json
import struct
import subprocess
from collections import defaultdict
from pathlib import Path


def canonical(tile):
    return json.dumps(tile, sort_keys=True, ensure_ascii=False, separators=(",", ":"), allow_nan=False)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("levels", type=Path)
    parser.add_argument("samples", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    samples = json.loads(args.samples.read_bytes())
    revision = subprocess.check_output(["git", "-C", str(args.levels), "rev-parse", "HEAD"], text=True).strip()
    changes = subprocess.check_output(
        ["git", "-C", str(args.levels), "status", "--porcelain", "--", "."], text=True
    )
    if revision != samples["revision"] or changes:
        parser.error("level corpus must match the terrain sample catalog revision")

    identifiers = {}
    definitions = []
    expected = {}
    for key, entry in samples["tiles"].items():
        for number, sample in enumerate(entry["samples"], 1):
            identifiers[canonical(sample["tileData"])] = len(definitions)
            definitions.append([key, number, sample["tileData"].get("heightType"), sample["tileData"].get("buildableType")])
            expected[key, number] = set(sample["sources"])

    maps = {}
    observed = defaultdict(set)
    for source, path in enumerate(samples["levels"]):
        map_data = json.loads((args.levels / path).read_bytes())["mapData"]
        grid = map_data["map"]
        width = len(grid[0])
        if any(len(row) != width for row in grid) or not width or width * len(grid) > 16383:
            raise ValueError(f"Unsupported preview dimensions in {path}")
        local = {}
        cells = []
        for row in grid:
            for index in row:
                if type(index) is not int or not 0 <= index < len(map_data["tiles"]):
                    raise ValueError(f"Invalid tile reference in {path}")
                if index not in local:
                    tile = map_data["tiles"][index]
                    key = canonical(tile)
                    if key not in identifiers:
                        identifiers[key] = len(definitions)
                        definitions.append([tile["tileKey"], 0, tile.get("heightType"), tile.get("buildableType")])
                    item = identifiers[key]
                    local[index] = item
                    tile_key, sample_number, *_ = definitions[item]
                    if sample_number:
                        observed[tile_key, sample_number].add(source)
                cells.append(local[index])
        packed = struct.pack(f"<{len(cells)}H", *cells)
        maps[path] = [width, len(grid), base64.b64encode(packed).decode("ascii")]

    if observed != expected:
        raise ValueError("Preview matches differ from terrain sample sources")
    result = {"version": 1, "revision": revision, "tiles": definitions, "maps": maps}
    args.output.write_text(json.dumps(result, separators=(",", ":"), ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"{len(maps)} maps, {len(definitions)} tile definitions")


if __name__ == "__main__":
    main()
