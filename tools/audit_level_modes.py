"""Audit local LevelData evidence without inferring or executing battle effects."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import subprocess
from collections import Counter
from pathlib import Path
from typing import Any, Iterator

DIFFICULTIES = {
    "NONE": 0,
    "NORMAL": 1,
    "FOUR_STAR": 2,
    "EASY": 4,
    "SIX_STAR": 8,
    "ALL": 15,
}
PRESET_GROUPS = ("characterInsts", "tokenInsts", "characterCards", "tokenCards")


def difficulty_mask(value: Any) -> int:
    if isinstance(value, bool):
        raise ValueError("Boolean difficulty mask")
    if isinstance(value, int) and 0 <= value <= DIFFICULTIES["ALL"]:
        return value
    if isinstance(value, str):
        names = re.split(r"\s*[,|]\s*", value)
        if names and all(name in DIFFICULTIES for name in names):
            result = 0
            for name in names:
                result |= DIFFICULTIES[name]
            return result
    raise ValueError(f"Unknown difficulty mask {value!r}")


def valid_for_difficulty(mask: Any, requested: Any) -> bool:
    """PC 2.7.81 CheckValidForDifficulty predicate; no execution claim."""
    requested_bits = difficulty_mask(requested)
    return difficulty_mask(mask) & requested_bits == requested_bits


def unique_object(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"Duplicate JSON key {key!r}")
        result[key] = value
    return result


def reject_constant(value: str) -> None:
    raise ValueError(f"Non-finite JSON constant {value}")


def load_json(path: Path) -> Any:
    return decode_json(path.read_bytes())


def decode_json(source: bytes) -> Any:
    return json.loads(
        source.decode("utf-8-sig"),
        object_pairs_hook=unique_object,
        parse_constant=reject_constant,
    )


def digest(value: Any) -> str:
    encoded = json.dumps(value, sort_keys=True, ensure_ascii=False, allow_nan=False)
    return hashlib.sha256(encoded.encode("utf-8")).hexdigest()


def field_digests(document: dict[str, Any]) -> dict[str, str]:
    return {name: digest(value) for name, value in document.items()}


def changed_fields(left: dict[str, str], right: dict[str, str]) -> list[str]:
    return sorted(
        name for name in left.keys() | right.keys() if left.get(name) != right.get(name)
    )


def map_structure(data: dict[str, Any]) -> dict[str, Any]:
    rows, tiles = data.get("map"), data.get("tiles")
    if "map" in data and "tiles" in data and rows is None and tiles is None:
        return {"status": "null-layout", "cells": 0, "issues": []}
    if not isinstance(rows, list) or not isinstance(tiles, list):
        return {"status": "invalid", "cells": 0, "issues": ["map-or-tiles-type"]}
    issues = []
    widths = []
    resolved = []
    cells = 0
    for row_index, row in enumerate(rows):
        if not isinstance(row, list):
            issues.append(f"map/{row_index}: non-array row")
            continue
        widths.append(len(row))
        cells += len(row)
        resolved_row = []
        for col, index in enumerate(row):
            if type(index) is not int or not 0 <= index < len(tiles):
                issues.append(f"map/{row_index}/{col}: invalid palette index {index!r}")
            else:
                resolved_row.append(tiles[index])
        resolved.append(resolved_row)
    if len(set(widths)) > 1:
        issues.append("non-rectangular-map")
    return {
        "status": "invalid" if issues else "grid",
        "rows": len(rows),
        "widths": sorted(set(widths)),
        "cells": cells,
        "tileEntries": len(tiles),
        "resolvedTilesSha256": None if issues else digest(resolved),
        "issues": issues,
    }


def actions(document: dict[str, Any]) -> Iterator[dict[str, Any]]:
    containers = [
        fragment
        for wave in document.get("waves") or []
        for fragment in wave.get("fragments") or []
    ]
    containers.extend(
        phase
        for branch in (document.get("branches") or {}).values()
        for phase in branch.get("phases") or []
    )
    for container in containers:
        yield from container.get("actions") or []


def level_record(source: bytes, document: dict[str, Any]) -> dict[str, Any]:
    base_runes = document.get("runes") or []
    optional = document.get("optionalRunes") or {}
    if not isinstance(optional, dict):
        raise ValueError("optionalRunes must be an object, null or missing")
    all_runes = list(base_runes)
    for group in optional.values():
        if not isinstance(group, list):
            raise ValueError("optionalRunes group must be an array")
        all_runes.extend(group)
    masks = Counter(difficulty_mask(rune.get("difficultyMask")) for rune in all_runes)
    hidden_groups = Counter(
        action["hiddenGroup"]
        for action in actions(document)
        if action.get("hiddenGroup")
    )
    preset_counts = {}
    for name in ("predefines", "hardPredefines"):
        presets = document.get(name) or {}
        preset_counts[name] = {
            group: len(presets.get(group) or []) for group in PRESET_GROUPS
        }
    return {
        "sha256": hashlib.sha256(source).hexdigest(),
        "fieldDigests": field_digests(document),
        "fieldTypes": {name: type(value).__name__ for name, value in document.items()},
        "mapStructure": map_structure(document["mapData"]),
        "options": {
            name: (document.get("options") or {}).get(name)
            for name in (
                "isTrainingLevel",
                "isHardTrainingLevel",
                "isPredefinedCardsSelectable",
            )
        },
        "presetCounts": preset_counts,
        "runeMasks": {str(mask): count for mask, count in sorted(masks.items())},
        "baseRuneCount": len(base_runes),
        "baseRuneKeys": dict(Counter(rune.get("key") for rune in base_runes)),
        "baseRuneSelectionDigests": {
            name: digest(
                [
                    rune
                    for rune in base_runes
                    if valid_for_difficulty(rune["difficultyMask"], name)
                ]
            )
            for name in ("NORMAL", "FOUR_STAR", "EASY", "SIX_STAR")
        },
        "optionalRuneKeys": {key: len(value) for key, value in optional.items()},
        "optionalRuneConsumers": {
            key: [rune.get("key") for rune in value] for key, value in optional.items()
        },
        "hiddenActionGroups": dict(hidden_groups),
        "actionCount": sum(1 for _ in actions(document)),
    }


def level_resource(
    stage: dict[str, Any], scene_pairs: dict[str, Any] | None = None
) -> str | None:
    value = stage.get("levelId")
    # GetLevelDataPath performs one exact-key lookup, then uses pair.levelId.
    if isinstance(value, str) and scene_pairs is not None and value in scene_pairs:
        value = scene_pairs[value].get("levelId")
    return value.lower() + ".json" if isinstance(value, str) and value else None


def stage_relations(
    table: dict[str, Any],
    files: dict[str, Any],
    scene_pairs: dict[str, Any] | None = None,
) -> dict[str, Any]:
    stages = table.get("stages") or {}
    candidates = []
    for key, stage in stages.items():
        for relation in ("hardStagedId", "sixStarStageId"):
            if stage.get(relation):
                candidates.append((key, stage[relation], relation))
    for group, members in (table.get("diffGroupTable") or {}).items():
        if not members.get("normalId"):
            continue
        for relation in ("easyId", "toughId"):
            if members.get(relation):
                candidates.append(
                    (
                        members["normalId"],
                        members[relation],
                        f"diffGroupTable/{group}/{relation}",
                    )
                )
    edges = []
    unresolved = []
    for source, target, relation in candidates:
        left, right = stages.get(source), stages.get(target)
        item = {"source": source, "target": target, "relation": relation}
        if left is None or right is None:
            unresolved.append({**item, "reason": "missing-stage"})
            continue
        left_path, right_path = (
            level_resource(left, scene_pairs),
            level_resource(right, scene_pairs),
        )
        left_data, right_data = files.get(left_path), files.get(right_path)
        if left_data is None or right_data is None:
            unresolved.append(
                {
                    **item,
                    "reason": "missing-level-resource",
                    "resources": [left_path, right_path],
                }
            )
            continue
        edges.append(
            {
                **item,
                "mainOnly": left.get("stageType") == right.get("stageType") == "MAIN",
                "resources": [left_path, right_path],
                "requestedLevelIds": [left.get("levelId"), right.get("levelId")],
                "sameLevelResource": left_path == right_path,
                "differentFields": changed_fields(
                    left_data["fieldDigests"], right_data["fieldDigests"]
                ),
                "baseRuneSelectionDifferent": compare_rune_selection(
                    left, right, left_data, right_data
                ),
            }
        )
    return {"edges": edges, "unresolved": unresolved}


def compare_rune_selection(
    left: dict[str, Any],
    right: dict[str, Any],
    left_data: dict[str, Any],
    right_data: dict[str, Any],
) -> bool | None:
    left_digest = left_data.get("baseRuneSelectionDigests", {}).get(
        left.get("difficulty")
    )
    right_digest = right_data.get("baseRuneSelectionDigests", {}).get(
        right.get("difficulty")
    )
    if left_digest is None or right_digest is None:
        return None
    return left_digest != right_digest


def six_star_bindings(
    table: dict[str, Any],
    files: dict[str, Any],
    scene_pairs: dict[str, Any] | None = None,
) -> dict[str, Any]:
    definitions = table.get("sixStarRuneData") or {}
    records = []
    issues = []
    for stage_id, stage in (table.get("stages") or {}).items():
        if stage.get("difficulty") != "SIX_STAR":
            continue
        source = files.get(level_resource(stage, scene_pairs))
        for group in ("advancedRuneIdList1", "advancedRuneIdList2"):
            for rune_id in stage.get(group) or []:
                definition = definitions.get(rune_id)
                key = (
                    definition.get("runeKey") if isinstance(definition, dict) else None
                )
                found = source is not None and key in source["optionalRuneKeys"]
                record = {
                    "stageId": stage_id,
                    "group": group,
                    "runeId": rune_id,
                    "runeKey": key,
                    "levelBindingFound": found,
                }
                records.append(record)
                if not found:
                    issues.append(record)
    return {"records": records, "issues": issues, "runtimeValidated": False}


def corpus_summary(files: dict[str, Any], non_levels: list[str]) -> dict[str, Any]:
    fields: dict[str, Counter] = {}
    masks: Counter = Counter()
    schemas: Counter = Counter()
    for record in files.values():
        schema = tuple(sorted(record["fieldTypes"]))
        schemas[schema] += 1
        for name, kind in record["fieldTypes"].items():
            fields.setdefault(name, Counter())[kind] += 1
        masks.update(record["runeMasks"])
    return {
        "levelCount": len(files),
        "nonLevelCount": len(non_levels),
        "fieldPresence": {
            name: {
                "present": sum(kinds.values()),
                "missing": len(files) - sum(kinds.values()),
                "types": dict(kinds),
            }
            for name, kinds in sorted(fields.items())
        },
        "schemaVariants": [
            {"fields": list(schema), "count": count}
            for schema, count in schemas.most_common()
        ],
        "runeMasks": dict(masks),
        "hardTrainingLevels": [
            name
            for name, row in files.items()
            if row["options"]["isHardTrainingLevel"] is True
        ],
        "nonemptyHardPredefines": {
            name: row["presetCounts"]["hardPredefines"]
            for name, row in files.items()
            if any(row["presetCounts"]["hardPredefines"].values())
        },
        "levelsWithOptionalRunes": [
            name for name, row in files.items() if row["optionalRuneKeys"]
        ],
        "levelsWithHiddenActionGroups": sum(
            bool(row["hiddenActionGroups"]) for row in files.values()
        ),
        "mapStatuses": dict(
            Counter(row["mapStructure"]["status"] for row in files.values())
        ),
        "gridCells": sum(row["mapStructure"]["cells"] for row in files.values()),
        "mapIssues": {
            name: row["mapStructure"]
            for name, row in files.items()
            if row["mapStructure"]["status"] != "grid"
        },
        "runtimeValidated": False,
        "scope": "Observed local serialized configuration. Presence or difference does not establish execution or complete mode write coverage.",
    }


def audit_corpus(gamedata: Path) -> dict[str, Any]:
    files = {}
    non_levels = []
    errors = []
    for path in sorted((gamedata / "levels").rglob("*.json")):
        relative = path.relative_to(gamedata / "levels").as_posix()
        try:
            source = path.read_bytes()
            document = decode_json(source)
            if not isinstance(document, dict) or not isinstance(
                document.get("mapData"), dict
            ):
                non_levels.append(relative)
                continue
            files[relative] = level_record(source, document)
        except (ValueError, OSError, TypeError, AttributeError, KeyError) as error:
            errors.append({"file": relative, "error": str(error)})
    table_path = gamedata / "excel" / "stage_table.json"
    table_source = table_path.read_bytes()
    table = decode_json(table_source)
    misc_path = gamedata / "battle" / "battle_misc_table.json"
    misc_source = misc_path.read_bytes() if misc_path.exists() else None
    scene_pairs = (
        decode_json(misc_source).get("levelScenePairs")
        if misc_source is not None
        else None
    )
    return {
        "summary": corpus_summary(files, non_levels),
        "files": files,
        "nonLevels": non_levels,
        "errors": errors,
        "stageTableSha256": hashlib.sha256(table_source).hexdigest(),
        "battleMiscSha256": hashlib.sha256(misc_source).hexdigest()
        if misc_source is not None
        else None,
        "levelScenePairs": scene_pairs,
        "stageRelations": stage_relations(table, files, scene_pairs),
        "sixStarBindings": six_star_bindings(table, files, scene_pairs),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--gamedata", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    if args.output.resolve().is_relative_to(args.gamedata.resolve()):
        parser.error("Evidence output must be outside the source data directory")
    if args.output.exists():
        parser.error("Use a new output file to preserve prior evidence")
    if not (args.gamedata / "levels").is_dir():
        parser.error("Missing levels directory")
    result = audit_corpus(args.gamedata)
    result["sourceRelativeToReport"] = os.path.relpath(
        args.gamedata, args.output.parent
    ).replace("\\", "/")
    revision = subprocess.run(
        ["git", "-C", str(args.gamedata), "rev-parse", "HEAD"],
        capture_output=True,
        text=True,
        check=False,
    )
    result["gitRevision"] = (
        revision.stdout.strip() if revision.returncode == 0 else None
    )
    status = subprocess.run(
        [
            "git",
            "-C",
            str(args.gamedata),
            "status",
            "--porcelain",
            "--untracked-files=all",
            "--",
            ".",
        ],
        capture_output=True,
        text=True,
        check=False,
    )
    result["gitStatus"] = status.stdout.splitlines() if status.returncode == 0 else None
    tracked = subprocess.run(
        ["git", "-C", str(args.gamedata), "ls-files", "--", "levels"],
        capture_output=True,
        text=True,
        check=False,
    )
    result["untrackedLevelFiles"] = (
        sorted(
            set("levels/" + name for name in result["files"])
            - set(tracked.stdout.splitlines())
        )
        if tracked.returncode == 0
        else None
    )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("x", encoding="utf-8") as target:
        json.dump(result, target, ensure_ascii=False, indent=2, allow_nan=False)
        target.write("\n")
    print(
        json.dumps(
            {
                "levels": result["summary"]["levelCount"],
                "errors": len(result["errors"]),
                "relations": len(result["stageRelations"]["edges"]),
                "unresolvedRelations": len(result["stageRelations"]["unresolved"]),
                "sixStarBindingIssues": len(result["sixStarBindings"]["issues"]),
            },
            ensure_ascii=False,
        )
    )
    return 1 if result["errors"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
