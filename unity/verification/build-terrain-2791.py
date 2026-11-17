"""Generate the supplemental Unity terrain catalog from verified 2.7.91 snapshots."""

import argparse
import hashlib
import json
from pathlib import Path
import runpy

REVISION = "26-11-13-06-37-23-89286612"
LEVEL_URL = "https://torappu.prts.wiki/gamedata/" + REVISION + "/"
IDS = {"tile_acbag", "tile_acempty", "tile_duel_grassland", "tile_speedup", "tile_trench"}
MODE_DESCRIPTIONS = {
    "tile_acbag": "装备区。卫戍协议·规程中，用于存放待分配的装备。",
    "tile_acempty": "卫戍协议地图中用于隔开各块战场的空地块。该格不可部署、不可通行。",
}
READINGS = {
    "tile_duel_grassland": ["yinnimokuai", "ynmk"],
    "tile_speedup": ["youhuamokuai", "yhmk"],
    "tile_trench": ["zhanhao", "zh"],
}
ENUMS = {
    "heightType": {0: "LOWLAND"},
    "buildableType": {3: "ALL"},
    "passableMask": {0: "NONE", 3: "ALL"},
    "playerSideMask": {0: "ALL"},
}
BLACKBOARD = {
    "tile_speedup": [
        {"key":"attack_speed","kind":"number","field":"value","observed":[[25.0,None]],
         "condition":"优化模块的我方角色攻击速度加值。","evidence":"卫戍协议 m12 的 TileData 和 tile_speedup Buff 模板"},
        {"key":"move_speed","kind":"number","field":"value","observed":[[1.5,None]],
         "condition":"优化模块的敌方移动速度倍率。","evidence":"卫戍协议 m12 的 TileData 和 tile_speedup Buff 模板"},
    ],
    "tile_trench": [
        {"key":"dsjkingdom2","kind":"reference","observed":[[2.0,None]],
         "evidence":"TL-S-4 的特定战壕格记录。"},
    ],
}


def read(path: Path) -> dict:
    return json.loads(path.read_bytes())


def write(path: Path, content: dict) -> None:
    path.write_text(json.dumps(content, ensure_ascii=False, separators=(",", ":"), allow_nan=False)
                    + "\n", encoding="utf-8")


def confirm_levels(evidence: Path) -> None:
    for path, row in read(evidence / "tor-level-receipts.json").items():
        relative = Path(path)
        if not path.startswith("levels/") or ".." in relative.parts or row.get("url") != LEVEL_URL + path:
            raise ValueError(f"Invalid level source: {path}")
        if hashlib.sha256((evidence / "TOR" / relative).read_bytes()).hexdigest() != row["sha256"]:
            raise ValueError(f"Level source checksum mismatch: {path}")


def stage_search(paths: list[str], stage: dict) -> dict:
    by_path = {}
    for key, row in stage["stages"].items():
        level = row.get("levelId")
        if level:
            by_path.setdefault(level.lower() + ".json", []).append((key, row))
    result = {}
    for path in paths:
        refs = by_path.get(path.lower(), [])
        names = {f"{row['code']} · {row['name']}" for _, row in refs if row.get("code") and row.get("name")}
        if not names and "/act3autochess/" in path:
            names = {"卫戍协议 · " + Path(path).stem.removeprefix("level_act3autochess_")}
        result[path] = {"names": sorted(names), "ids": sorted({key for key, _ in refs})}
    return {"terrain": READINGS, "stages": result}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("evidence", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    confirm_levels(args.evidence)
    stage = read(args.evidence / "tor-tables/stage_table.json")
    prefab = read(args.evidence / "prefab-evidence.json")
    if prefab["source"]["sourceType"] != "2.7.91 Windows" or set(prefab["tiles"]) != IDS:
        raise ValueError("Unexpected prefab version or terrain IDs")
    script_dir = Path(__file__).resolve().parent
    sample_builder = runpy.run_path(str(script_dir / "build-terrain-samples.py"))
    source_builder = runpy.run_path(str(script_dir / "build-terrain-source-maps.py"))
    levels = args.evidence / "TOR/levels"
    paths, samples = sample_builder["scan"](levels, IDS)
    if set(samples) != IDS or len(paths) != len(read(args.evidence / "tor-level-receipts.json")):
        raise ValueError("Level corpus and terrain targets do not agree")

    tiles = {}
    entries = []
    templates = {}
    for key in sorted(IDS):
        configs = list(samples[key].values())
        configs.sort(key=lambda row: (-len(row["sources"]), sample_builder["canonical"](row["tileData"])))
        tiles[key] = {"recommended": sample_builder["recommendations"](configs), "samples": configs}
        info = stage["tileInfo"].get(key) or {}
        entries.append({"id": key, "category": "terrain", "name": info.get("name",""),
                        "description": info.get("description") or MODE_DESCRIPTIONS[key],
                        "template": None, "visualDefinitionAvailable": True})
        raw = prefab["tiles"][key]["data"]
        templates[key] = {field: ENUMS[field][value] if field in ENUMS else value for field,value in raw.items()}
        templates[key]["tileKey"] = key
    samples = {"version":1,"revision":REVISION,"levels":paths,"tiles":tiles}
    args.output.mkdir(parents=True, exist_ok=True)
    write(args.output / "terrain-catalog-2791.json", {"version":1,"entries":entries})
    write(args.output / "terrain-samples-2791.json", samples)
    write(args.output / "terrain-search-metadata-2791.json", stage_search(paths, stage))
    write(args.output / "terrain-source-maps-2791.json", source_builder["build_projection"](levels,samples))
    write(args.output / "terrain-blackboard-2791.json", {"version":1,"assetVersion":REVISION,
                                                        "tiles":BLACKBOARD,"templates":templates})
    print(f"{len(paths)} original maps, {len(tiles)} variants, {sum(len(v['samples']) for v in tiles.values())} samples")


if __name__ == "__main__":
    main()
