"""Build the Unity archive from a verified LevelData semantic audit."""

import argparse
import hashlib
import json
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile, ZipInfo


def read_json(path: Path) -> dict:
    return json.loads(path.read_bytes())


def write_entry(archive: ZipFile, name: str, data: bytes) -> None:
    archive.writestr(ZipInfo(name), data, compress_type=ZIP_DEFLATED, compresslevel=6)


def stage_modes(roots: dict[str, Path]) -> dict[str, str]:
    modes = {}
    for source in ("ArknightsGameResource", "ArknightsGameData"):
        data = read_json(roots[source].parent / "excel/stage_table.json")
        for stage in data["stages"].values():
            for key, mode in (("hardStagedId", "HARD"), ("sixStarStageId", "SIX_STAR")):
                if stage.get(key):
                    modes[stage[key]] = mode
        for group in data["diffGroupTable"].values():
            for key, mode in (("easyId", "EASY"), ("toughId", "TOUGH")):
                if group.get(key):
                    modes[group[key]] = mode
    return modes


def build(audit: Path, source_root: Path, output: Path) -> None:
    index = read_json(audit / "library-index.json")
    bodies = read_json(audit / "typed-bodies-final.json")["groups"]
    ownership = read_json(audit / "ownership.json")
    roots = {
        "ArknightsGameData": source_root / "ArknightsGameData/zh_CN/gamedata/levels",
        "PRTSMap-cache": source_root / "prtsMap-source/data/levels",
        "ArknightsGameResource": source_root / "ArknightsGameResource/gamedata/levels",
    }
    sources = index["sources"]
    modes = stage_modes(roots)
    for stage in index["stages"]:
        if stage["id"] in modes:
            stage["mode"] = modes[stage["id"]]
    if set(bodies) != {source["body"] for source in sources}:
        raise ValueError("The audit and the index list different body IDs.")
    details = {}
    for row in ownership["rows"]:
        for number in row["sourceIndices"]:
            details[number] = row["bindings"]
    for number, source in enumerate(sources):
        if number in details:
            source["bindings"] = details[number]
    index["version"] = 1
    index["bodies"] = {key: row["representative"] for key, row in bodies.items()}
    staging = output.with_suffix(".tmp")
    with staging.open("xb") as stream:
        try:
            with ZipFile(stream, "w") as archive:
                for key, row in sorted(bodies.items()):
                    if len(key) != 64 or any(char not in "0123456789abcdef" for char in key):
                        raise ValueError(f"Invalid body identity {key}")
                    source = sources[row["representative"]]
                    if source["body"] != key:
                        raise ValueError(f"Invalid representative for {key}")
                    root = roots[source["source"]].resolve()
                    path = (root / source["path"]).resolve()
                    if not path.is_relative_to(root):
                        raise ValueError(f"Source escaped its root: {source['path']}")
                    raw = path.read_bytes()
                    if hashlib.sha256(raw).hexdigest() != source["sha256"]:
                        raise ValueError(f"Representative changed: {path}")
                    write_entry(archive, f"levels/{key}.json", raw)
                manifest = json.dumps(index, ensure_ascii=False, separators=(",", ":")).encode()
                write_entry(archive, "library-index.json", manifest)
            stream.flush()
        except BaseException:
            stream.close()
            staging.unlink()
            raise
    staging.replace(output)
    print(f"{len(bodies)} LevelData, {len(index['stages'])} stage IDs, {output.stat().st_size} ZIP bytes")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("audit", type=Path)
    parser.add_argument("source_root", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    build(args.audit, args.source_root, args.output)
