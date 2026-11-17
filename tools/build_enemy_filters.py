"""Build the Unity enemy search and filter index from fixed 2.7.81 resources."""

from __future__ import annotations

import argparse
import hashlib
import html
import json
import re
import struct
import zipfile
from pathlib import Path

from build_enemy_library import DATABASE_SHA256, REVISION, encode, unwrap

HANDBOOK_SHA256 = "df08b536436abd79e52a5c21dc143a54e03de64d605ebab2b8d22e5eb24716ed"
TERMS_SHA256 = "b887cd88a7d699c6a8bae2acd6e86b313df9f626236a171c01b51347c0ab8bb3"
ATTRIBUTES = {
    "hp": ("maxHP", "maxHp", 100),
    "atk": ("attack", "atk", 0),
    "def": ("def", "def", 0),
    "move": ("moveSpeed", "moveSpeed", 1),
    "interval": ("attackSpeed", "baseAttackTime", 1),
    "res": ("magicRes", "magicResistance", 0),
    "element": ("enemyDamageRes", "epDamageResistance", 0),
    "damageRes": ("enemyRes", "epResistance", 0),
}
RANK = {"NORMAL": "普通", "ELITE": "精英", "BOSS": "领袖"}
ATTACK = {"MELEE": ["近战"], "RANGED": ["远程"], "ALL": ["近战", "远程"], "NONE": ["不攻击"]}
MOTION = {"WALK": "地面", "FLY": "飞行"}
DAMAGE = {"PHYSIC": "物理", "MAGIC": "法术", "HEAL": "治疗", "NO_DAMAGE": "无"}
MARKUP = re.compile(r"<[@$][^>]+>|</>")
TERM = re.compile(r"<\$([^>]+)>")
FIXED_MOVE_ID = "enemy_2056_smedzi"


def pinned(path: Path, expected_hash: str) -> dict:
    payload = path.read_bytes()
    if hashlib.sha256(payload).hexdigest() != expected_hash:
        raise ValueError(f"Unexpected 2.7.81 input hash: {path}")
    return json.loads(payload)


def plain(text: str | None) -> str:
    return html.unescape(MARKUP.sub("", text or "")).strip()


def single(number: float) -> float:
    return struct.unpack("<f", struct.pack("<f", number))[0]


def grade(field: str, value: float, bands: list[dict]) -> str | None:
    value = single(value)
    for band in bands:
        interval = band[field]
        lower, upper = interval["min"], interval["max"]
        if value >= single(lower) and (upper == -1 or value < single(upper)):
            return band["classLevel"]
    return None


def profile(data: dict, handbook: dict, races: dict, bands: list[dict], enemy_id: str, base: bool) -> dict:
    rank = RANK.get(handbook.get("enemyLevel") or unwrap(data.get("levelType")))
    attack = ATTACK.get(unwrap(data.get("applyWay")))
    motion = MOTION.get(unwrap(data.get("motion")))
    damage = [DAMAGE[value] for value in handbook.get("damageType") or []]
    raw_tags = data.get("enemyTags")
    race = [races[tag]["raceName"] for tag in unwrap(raw_tags) or [] if tag in races]
    if not race and (handbook or isinstance(raw_tags, dict) and raw_tags.get("m_defined")):
        race = ["其他"]
    labels = {"rank": [rank] if rank else None, "race": race, "attack": attack, "damage": damage,
              "motion": [motion] if motion else None}
    attributes = data.get("attributes") or {}
    if not handbook.get("invisibleDetail"):
        for key, (field, attr, default) in ATTRIBUTES.items():
            wrapper = attributes.get(attr)
            value = unwrap(wrapper)
            if key == "move" and enemy_id == FIXED_MOVE_ID and base:
                if wrapper != {"m_defined": False, "m_value": 0.0}:
                    raise ValueError("The audited Kreisson moveSpeed input has changed.")
                value = wrapper["m_value"]
            if value is None and base:
                value = default
            if isinstance(value, (int, float)):
                rating = grade(field, value, bands)
                if rating:
                    labels[key] = [rating]
    return {key: values for key, values in labels.items() if values}


def build(database: dict, handbook: dict, terms: dict, catalog: dict, archive: Path) -> dict:
    if catalog["revision"] != REVISION:
        raise ValueError("Enemy catalog revision differs from the pinned inputs.")
    levels = {row["Key"]: row["Value"] for row in database["enemies"]}
    dictionary = handbook["enemyData"]
    races, bands = handbook["raceData"], handbook["levelInfoList"]
    result = {}
    with zipfile.ZipFile(archive) as details:
        for row in catalog["entries"]:
            enemy_id = row["id"]
            hb = dictionary.get(enemy_id, {})
            abilities = [entry["text"] for entry in hb.get("abilityList") or [] if entry.get("text")]
            database_levels = levels.get(enemy_id, [])
            if database_levels:
                level_zero = next(item["enemyData"] for item in database_levels if item["level"] == 0)
                definitions = [level_zero]
            else:
                history = json.loads(details.read(row["detail"]))
                definitions = [
                    sample["record"].get("overwrittenData") or {}
                    for sample in history["references"] if not sample["record"]["useDb"]
                ]
                definitions.extend(sample["record"] for sample in history["inline"])
            filters = {}
            search_text = [plain(row.get("description")), plain(hb.get("description")), *map(plain, abilities)]
            search_text.extend(
                plain(terms[key]["description"])
                for key in dict.fromkeys(TERM.findall("\n".join(abilities)))
            )
            names = [row["name"]]
            for definition in definitions:
                current = profile(definition, hb, races, bands, enemy_id, bool(database_levels))
                filters.setdefault(encode(current), current)
                names.append(plain(unwrap(definition.get("name"))))
                search_text.append(plain(unwrap(definition.get("description"))))
            result[enemy_id] = {
                "name": plain(hb.get("name")) or next((n for n in names[1:] if n), row["name"]),
                "number": hb.get("enemyIndex") or "",
                "order": hb.get("sortId"),
                "public": bool(hb) and not hb["hideInHandbook"],
                "abilities": "\n".join(map(plain, abilities)),
                "search": list(dict.fromkeys(s for s in [*names, *search_text] if s and s != enemy_id)),
                "profiles": list(filters.values()),
            }
    return {"version": 1, "revision": REVISION, "entries": result}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    for option in ("database", "handbook", "constants", "catalog", "output"):
        parser.add_argument(option, type=Path)
    args = parser.parse_args()
    index = args.catalog / "enemy-index.json"
    data = build(
        pinned(args.database, DATABASE_SHA256), pinned(args.handbook, HANDBOOK_SHA256),
        pinned(args.constants, TERMS_SHA256)["termDescriptionDict"],
        json.loads(index.read_bytes()), args.catalog / "enemy-details.zip",
    )
    args.output.write_bytes(encode(data) + b"\n")
    print(f"{len(data['entries'])} enemy filter entries")


if __name__ == "__main__":
    main()
