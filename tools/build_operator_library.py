"""Build the Unity preset operator library from one game data revision and the level archive."""

from __future__ import annotations

import argparse
import hashlib
import json
import zipfile
from collections import defaultdict
from pathlib import Path

CLIENT = "2.7.81"
REVISION = "26-10-22-14-02-11-51447808"
TABLES = ("character", "char_patch", "skill", "uniequip", "skin", "favor")
SECTIONS = ("predefines", "hardPredefines")
ROLES = ("characterInsts", "characterCards")


def encode(value: object) -> bytes:
    return json.dumps(value, ensure_ascii=False, separators=(",", ":")).encode()


def read_tables(directory: Path) -> tuple[dict, dict[str, str]]:
    tables = {}
    digests = {}
    for name in TABLES:
        data = (directory / f"{name}_table.json").read_bytes()
        tables[name] = json.loads(data)
        digests[f"{name}_table.json"] = hashlib.sha256(data).hexdigest()
    return tables, digests


def forms_for(character_id: str, tables: dict, skins: dict) -> list[dict]:
    patch = tables["char_patch"]["infos"].get(character_id)
    ids = patch["tmplIds"] if patch else [character_id]
    default = patch["default"] if patch else character_id
    result = []
    for form in ids:
        info = tables["char_patch"]["patchChars"].get(form) or tables["character"][form]
        skills = []
        for entry in info["skills"]:
            skill_id = entry["skillId"]
            levels = tables["skill"][skill_id]["levels"]
            skills.append(
                {
                    "id": skill_id,
                    "name": levels[0]["name"],
                    "levelCount": len(levels),
                    "unlock": entry.get("unlockCond"),
                }
            )
        modules = []
        for equip_id in tables["uniequip"]["charEquip"].get(form, []):
            item = tables["uniequip"]["equipDict"][equip_id]
            modules.append(
                {
                    "id": equip_id,
                    "name": item["uniEquipName"],
                    "maxLevel": max(map(int, item.get("itemCost") or {"1": []})),
                    "phase": item.get("unlockEvolvePhase"),
                    "level": item.get("unlockLevel"),
                    "type": item.get("type"),
                }
            )
        result.append(
            {
                "id": form,
                "default": form == default,
                "profession": info["profession"],
                "subProfession": tables["uniequip"]["subProfDict"]
                .get(info.get("subProfessionId"), {}).get("subProfessionName"),
                "position": info.get("position"),
                "tags": info.get("tagList") or [],
                "phaseCaps": [phase["maxLevel"] for phase in info["phases"]],
                "potentialMax": info["maxPotentialLevel"],
                "skills": skills,
                "modules": modules,
                "skins": skins[form],
            }
        )
    return result


def module_labels(table: dict) -> dict[str, str]:
    labels = {}
    ordinals = ("一模", "二模", "三模")
    special = {"ISW": "集成特限模", "RA": "生息特限模", "SO": "特勤模"}
    for ids in table["charEquip"].values():
        modules = [table["equipDict"][key] for key in ids]
        regular = []
        for module in modules:
            key = module["uniEquipId"]
            if module["type"] == "INITIAL":
                labels[key] = "基础模组"
            elif module["isSpecialEquip"]:
                labels[key] = special.get(module["typeName1"], "特限模")
            else:
                regular.append(module)
        regular.sort(key=lambda module: module["uniEquipGetTime"])
        for index, module in enumerate(regular):
            labels[module["uniEquipId"]] = ordinals[index] if index < len(ordinals) else f"第 {index + 1} 模"
    return labels


def load_samples(path: Path, owners: dict[str, str]) -> tuple[dict, dict, dict]:
    samples = defaultdict(lambda: defaultdict(dict))
    sources = defaultdict(list)
    unmatched = defaultdict(int)
    with zipfile.ZipFile(path) as archive:
        index = json.loads(archive.read("library-index.json"))
        stages = {row["id"]: row["label"] for row in index["stages"]}
        for source in index["sources"]:
            sources[source["body"]].append(
                {
                    "path": source["path"],
                    "revision": source.get("revision"),
                    "source": source["source"],
                    "stages": [stages[key] for key in source["stages"] if key in stages],
                }
            )
        for body, source_index in index["bodies"].items():
            content = archive.read(f"levels/{body}.json")
            if hashlib.sha256(content).hexdigest() != index["sources"][source_index]["sha256"]:
                raise ValueError(f"Invalid level body checksum: {body}")
            level = json.loads(content)
            for section in SECTIONS:
                for role in ROLES:
                    entries = (level.get(section) or {}).get(role) or []
                    for position, original in enumerate(entries):
                        key = original.get("inst", {}).get("characterKey")
                        owner = owners.get(key)
                        if owner is None:
                            unmatched[key or "<missing>"] += 1
                            continue
                        record = dict(original)
                        if role == "characterInsts":
                            record.pop("position", None)
                            record.pop("direction", None)
                        fingerprint = json.dumps(record, sort_keys=True, ensure_ascii=False)
                        entry = samples[owner][role].setdefault(
                            fingerprint, {"record": record, "origins": []}
                        )
                        origin = {"body": body, "section": section, "index": position}
                        if role == "characterInsts":
                            for field in ("position", "direction"):
                                if field in original:
                                    origin[field] = original[field]
                        entry["origins"].append(origin)
    return samples, sources, unmatched


def build(directory: Path, levels: Path, output: Path) -> dict:
    tables, digests = read_tables(directory)
    characters = {
        key: value for key, value in tables["character"].items()
        if key.startswith("char_") and value["profession"] not in {"TRAP", "TOKEN"}
    }
    owners = {key: key for key in characters}
    for operator, forms in tables["char_patch"]["infos"].items():
        if operator in characters:
            owners.update(dict.fromkeys(forms["tmplIds"], operator))

    skins = defaultdict(list)
    for key, skin in tables["skin"]["charSkins"].items():
        form = skin.get("tmplId") or skin.get("charId")
        label = (skin.get("displaySkin") or {}).get("skinName") or key
        skins[form].append({"id": key, "name": label})

    samples, sources, unmatched = load_samples(levels, owners)
    used_bodies = set()
    rows = []
    with zipfile.ZipFile(output / "operator-details.zip", "w", zipfile.ZIP_DEFLATED) as archive:
        for key, info in characters.items():
            forms = forms_for(key, tables, skins)
            groups = {
                role: sorted(samples[key][role].values(), key=lambda item: -len(item["origins"]))
                for role in ROLES
            }
            for group in groups.values():
                used_bodies.update(origin["body"] for item in group for origin in item["origins"])
            rows.append(
                {
                    "id": key,
                    "name": info["name"],
                    "profession": info["profession"],
                    "rarity": info["rarity"],
                    "formIds": [form for form, owner in owners.items() if owner == key],
                    "filterForms": [
                        {field: form[field] for field in ("profession", "subProfession", "position", "tags")}
                        for form in forms
                    ],
                    "instances": len(groups["characterInsts"]),
                    "cards": len(groups["characterCards"]),
                }
            )
            details = {
                "description": info.get("itemUsage"),
                "forms": forms,
                **groups,
            }
            archive.writestr(f"{key}.json", encode(details))
        archive.writestr("sources.json", encode({body: sources[body] for body in sorted(used_bodies)}))

    favor = [
        [frame["data"]["favorPoint"], frame["data"]["battlePhase"]]
        for frame in tables["favor"]["favorFrames"]
    ]
    index = {
        "version": 1,
        "client": CLIENT,
        "revision": REVISION,
        "tableHashes": digests,
        "levelArchiveSha256": hashlib.sha256(levels.read_bytes()).hexdigest(),
        "favorFrames": favor,
        "moduleLabels": module_labels(tables["uniequip"]),
        "entries": rows,
    }
    (output / "operator-index.json").write_bytes(encode(index))
    return {
        "operators": len(rows),
        "instanceSamples": sum(row["instances"] for row in rows),
        "cardSamples": sum(row["cards"] for row in rows),
        "levelBodies": len(used_bodies),
        "unmatched": dict(sorted(unmatched.items())),
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("tables", type=Path)
    parser.add_argument("levels", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    print(json.dumps(build(args.tables, args.levels, args.output), ensure_ascii=False))


if __name__ == "__main__":
    main()
