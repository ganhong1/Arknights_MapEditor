"""Build the incremental device catalog from pinned 2.7.91 game data."""

from __future__ import annotations

import argparse
from collections import defaultdict
from hashlib import sha256
import json
from pathlib import Path
import re

from pypinyin import Style, lazy_pinyin

REVISION = "26-11-13-06-37-23-89286612"
ROLES = ("tokenInsts", "tokenCards")
LAYERS = ("predefines", "hardPredefines")


def read(path: Path) -> dict:
    return json.loads(path.read_bytes())


def compact(value: object, *, sorted_keys: bool = False) -> str:
    return json.dumps(value, ensure_ascii=False, separators=(",", ":"), sort_keys=sorted_keys, allow_nan=False)


def write(path: Path, value: object) -> None:
    path.write_text(compact(value) + "\n", encoding="utf-8")


def plain(value: str | None) -> str:
    return re.sub(r"<(?:[@$][^<>]+|/)>", "", value or "").strip()


def level_samples(evidence: Path, ids: set[str]) -> tuple[list[str], dict]:
    levels = []
    index = defaultdict(lambda: defaultdict(dict))
    for path, receipt in sorted(read(evidence / "tor-level-receipts.json").items()):
        relative = Path(path)
        if (relative.is_absolute() or not path.startswith("levels/") or ".." in relative.parts
                or receipt.get("url") != f"https://torappu.prts.wiki/gamedata/{REVISION}/{path}"):
            raise ValueError(f"Invalid LevelData receipt: {path}")
        raw = (evidence / "TOR" / relative).read_bytes()
        if sha256(raw).hexdigest() != receipt["sha256"]:
            raise ValueError(f"LevelData checksum mismatch: {path}")
        level_id = len(levels)
        levels.append(path.removeprefix("levels/"))
        doc = json.loads(raw)
        for layer in LAYERS:
            for role in ROLES:
                for slot, original in enumerate((doc.get(layer) or {}).get(role) or []):
                    key = (original.get("inst") or {}).get("characterKey")
                    if key not in ids:
                        continue
                    normalized = {k: v for k, v in original.items()
                                  if role != "tokenInsts" or k not in ("position", "direction")}
                    stamp = compact(normalized, sorted_keys=True)
                    bucket = index[key][role].setdefault(stamp, (normalized, {}))[1]
                    raw_key = compact(original, sorted_keys=True)
                    bucket.setdefault(raw_key, (original, []))[1].append([level_id, layer, slot])
    return levels, index


def format_role(buckets: dict) -> dict:
    ranked = sorted(buckets.items(), key=lambda pair: (
        bool(pair[1][0].get("hidden")), bool(pair[1][0].get("alias")),
        -sum(len(refs) for _, refs in pair[1][1].values()), pair[0]))
    samples = []
    for stamp, (record, originals) in ranked:
        samples.append({
            "id": sha256(stamp.encode()).hexdigest(),
            "data": record,
            "originals": [
                {"id": sha256(raw.encode()).hexdigest(), "data": original, "references": refs}
                for raw, (original, refs) in originals.items()
            ],
        })
    return {"samples": samples, "recommended": list(range(min(3, len(samples))))}


def mode_samples(activity: dict, ids: set[str]) -> dict:
    season = activity["activity"]["AUTOCHESS"]["act3autochess"]["V3_1"]
    source = "activity_table.activity.AUTOCHESS.act3autochess.V3_1"
    result = defaultdict(list)
    for chess_id, record in season["trapChessDataDict"].items():
        if record["charId"] not in ids:
            continue
        effect_id = record["effectId"]
        shop = [row for row in season["trapShopChessDatas"].values()
                if chess_id in (row["itemId"], row["goldenItemId"])]
        title = "金色装备" if record["isGolden"] else "普通装备"
        result[record["charId"]].append({
            "label": f"{title} · {chess_id}", "source": f"{source}.trapChessDataDict.{chess_id}",
            "data": {
                "chess": record,
                "shop": shop,
                "effect": season["effectInfoDataDict"].get(effect_id),
                "buffs": season["effectBuffInfoDataDict"].get(effect_id),
            },
        })
    for key, record in activity["autoChessData"]["skinDataDict"].items():
        if record.get("skinTrapId") in ids:
            result[record["skinTrapId"]].append({
                "label": record["name"], "source": f"activity_table.autoChessData.skinDataDict.{key}",
                "data": {"theme": record},
            })
    return result


def description(char: dict, mode: list[dict], skills: dict) -> str:
    if mode:
        effects = []
        for item in mode:
            payload = item["data"]
            if "theme" in payload:
                theme = payload["theme"]
                return theme["name"] + "。 " + plain(theme["usageDesc"])
            effect = payload.get("effect") or {}
            text = plain(effect.get("effectDesc")).replace("{showtag_value_1}", "当前累计的")
            if text:
                label = "金色" if payload["chess"]["isGolden"] else "普通"
                effects.append(f"{label} · {text}")
        if effects:
            return "\n".join(effects)
    first = plain(char.get("description"))
    extra = [plain(skill["levels"][0].get("description")) for skill in skills.values()]
    return "\n".join(dict.fromkeys(filter(None, (first, *extra))))


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("evidence", type=Path)
    parser.add_argument("activity", type=Path)
    parser.add_argument("catalog", type=Path)
    args = parser.parse_args()
    base = read(args.evidence / "26-10-22-character_table.json")
    current = read(args.evidence / "26-11-13-character_table.json")
    skills = read(args.evidence / "26-11-13-skill_table.json")
    ids = {key for key in current.keys() - base.keys() if current[key]["profession"] == "TRAP"}
    assets = read(args.evidence / "pc-candidates.json")
    prefabs = {row["key"].rsplit("/", 1)[-1]: row["unityPath"] for row in assets
               if row["key"].startswith("battle/prefabs/[uc]tokens/")}
    if prefabs.keys() != ids:
        raise ValueError("Windows prefabs and character table changes do not match")
    levels, presets = level_samples(args.evidence, ids)
    modes = mode_samples(read(args.activity), ids)
    if ids != presets.keys() | modes.keys():
        raise ValueError("An added device lacks both a LevelData record and a mode configuration")
    entries = []
    sample_dir = args.catalog / "DeviceSamples"
    sample_dir.mkdir(parents=True, exist_ok=True)
    for key in sorted(ids):
        definition = current[key]
        role_data = {role: format_role(cases) for role, cases in presets[key].items()}
        filename = key + ".json" if role_data else None
        if filename:
            write(sample_dir / filename, {"version": 3, "id": key, "roles": role_data})
        name = definition["name"]
        skill_data = {slot["skillId"]: skills[slot["skillId"]] for slot in definition["skills"] if slot["skillId"]}
        modes[key].sort(key=lambda item: bool(item["data"].get("chess", {}).get("isGolden")))
        entries.append({
            "id": key, "name": name, "description": description(definition, modes[key], skill_data), "file": filename,
            "pinyin": ["".join(lazy_pinyin(name)), "".join(lazy_pinyin(name, style=Style.FIRST_LETTER))],
            "roles": {role: {"configurations": len(data["samples"])} for role, data in role_data.items()},
            "modeConfigurations": modes[key], "prefab": prefabs[key],
            "definition": {
                "phases": definition["phases"], "talents": definition["talents"],
                "skills": definition["skills"], "skillData": skill_data,
            },
        })
    write(args.catalog / "device-index-2791.json", {"version": 3, "revision": REVISION, "levels": levels, "entries": entries})
    print(f"{len(entries)} new IDs, {len(levels)} LevelData sources, {sum(bool(row['file']) for row in entries)} LevelData devices")


if __name__ == "__main__":
    main()
