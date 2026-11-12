"""Build and independently reread versioned Options values and every source reference."""

import argparse
import hashlib
import json
import subprocess
from collections import Counter, defaultdict
from decimal import Decimal
from pathlib import Path
from typing import Any


PRIMARY = "ArknightsGameData"
SUPPLEMENT = "PRTSMap-cache"
RESOURCE = "ArknightsGameResource"
FIELD_TYPES = {
    "characterLimit": "int",
    "maxLifePoint": "int",
    "initialCost": "int",
    "maxCost": "int",
    "costIncreaseTime": "float",
    "moveMultiplier": "float",
    "steeringEnabled": "bool",
    "reachableCheckIgnoreStartTile": "bool",
    "isTrainingLevel": "bool",
    "isHardTrainingLevel": "bool",
    "isPredefinedCardsSelectable": "bool",
    "displayRestTime": "bool",
    "maxPlayTime": "float",
    "functionDisableMask": "mask",
    "configBlackBoard": "list",
}


def _pairs(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("duplicate JSON member: " + key)
        result[key] = value
    return result


def _reject_constant(value: str) -> None:
    raise ValueError("non-finite JSON number: " + value)


def strict_loads(raw: bytes | str) -> Any:
    return json.loads(
        raw,
        object_pairs_hook=_pairs,
        parse_float=Decimal,
        parse_constant=_reject_constant,
    )


def dumps_json(value: Any, *, sort_keys: bool = False) -> str:
    """Preserve decimal precision and JSON numeric kind without a float round trip."""
    if isinstance(value, Decimal):
        if not value.is_finite():
            raise ValueError("non-finite Decimal")
        if sort_keys:
            sign, digits, exponent = value.as_tuple()
            digits = list(digits)
            while len(digits) > 1 and digits[-1] == 0:
                digits.pop()
                exponent += 1
            value = Decimal((sign, tuple(digits), exponent))
        text = str(value)
        return text if "." in text or "e" in text.lower() else text + ".0"
    if isinstance(value, dict):
        keys = sorted(value) if sort_keys else value.keys()
        return (
            "{"
            + ",".join(
                json.dumps(k, ensure_ascii=False)
                + ":"
                + dumps_json(value[k], sort_keys=sort_keys)
                for k in keys
            )
            + "}"
        )
    if isinstance(value, list):
        return "[" + ",".join(dumps_json(v, sort_keys=sort_keys) for v in value) + "]"
    return json.dumps(value, ensure_ascii=False, separators=(",", ":"), allow_nan=False)


def canonical(value: Any) -> str:
    return dumps_json(value, sort_keys=True)


def digest(raw: bytes) -> str:
    return hashlib.sha256(raw).hexdigest()


def option_state(level: dict[str, Any]) -> dict[str, Any]:
    if "options" not in level:
        return {"state": "missing", "value": None}
    value = level["options"]
    state = (
        "object" if isinstance(value, dict) else "null" if value is None else "other"
    )
    return {"state": state, "value": value}


def recommendable(field: str, value: Any) -> bool:
    kind = FIELD_TYPES.get(field)
    if kind == "int":
        return type(value) is int and -(2**31) <= value < 2**31
    if kind == "float":
        return type(value) in (int, float, Decimal) and abs(value) <= Decimal(
            "3.4028234663852886e38"
        )
    if kind == "bool":
        return type(value) is bool
    if kind == "mask":
        return (
            isinstance(value, str) or type(value) is int and -(2**31) <= value < 2**31
        )
    return kind == "list" and isinstance(value, list)


def _record(
    groups: dict[str, Any], value: Any, source: int, index: int | None = None
) -> None:
    row = groups.setdefault(
        canonical(value), {"value": value, "sources": set(), "references": []}
    )
    row["sources"].add(source)
    if index is not None:
        row["references"].append([source, index])


def _finish(
    groups: dict[str, Any], levels: list[dict[str, Any]], field: str | None
) -> dict[str, Any]:
    rows = []
    for row in groups.values():
        sources = sorted(row["sources"])
        count = len({levels[i]["documentId"] for i in sources})
        primary_count = len(
            {levels[i]["documentId"] for i in sources if levels[i]["source"] == PRIMARY}
        )
        result = {
            "value": row["value"],
            "sources": sources,
            "count": count,
            "primaryCount": primary_count,
        }
        if field is None:
            result["references"] = sorted(row["references"])
        rows.append(result)
    rows.sort(
        key=lambda row: (-row["primaryCount"], -row["count"], canonical(row["value"]))
    )
    candidates = [
        i
        for i, row in enumerate(rows)
        if row["primaryCount"] > 0
        and (field is None or recommendable(field, row["value"]))
    ]
    result = {"samples": rows, "recommended": candidates[:3]}
    if field is not None:
        present = {i for row in rows for i in row["sources"]}
        result["missingSources"] = [i for i in range(len(levels)) if i not in present]
    return result


def build_catalog(
    level_root: Path,
    revision: str,
    supplement: Path | None = None,
    resource: Path | None = None,
    resource_revision: str | None = None,
) -> dict[str, Any]:
    levels, options, source_reports, excluded, anomalies = [], [], [], [], []
    options_index: dict[str, int] = {}
    fields: dict[str, Any] = {field: {} for field in FIELD_TYPES}
    blackboard: dict[str, Any] = defaultdict(dict)
    roots = [(PRIMARY, level_root, revision)] + (
        [(SUPPLEMENT, supplement, None)] if supplement else []
    )
    if resource:
        roots.append((RESOURCE, resource, resource_revision))
    for source_name, root, source_revision in roots:
        manifest, source_excluded = [], []
        source_start = len(levels)
        paths = sorted(root.rglob("*.json"))
        for path in paths:
            relative = path.relative_to(root).as_posix()
            raw = path.read_bytes()
            try:
                level = strict_loads(raw)
            except (ValueError, UnicodeError) as error:
                raise ValueError(f"{source_name}/{relative}: {error}") from error
            manifest.append([relative, digest(raw)])
            if not isinstance(level, dict) or not (
                {"options", "mapData", "levelId"} & level.keys()
            ):
                entry = {
                    "path": relative,
                    "reason": "no LevelData discriminator (options/mapData/levelId)",
                }
                source_excluded.append(entry)
                excluded.append(
                    entry
                    if source_name == PRIMARY
                    else {**entry, "source": source_name}
                )
                continue
            state = option_state(level)
            identity = canonical(state)
            if identity not in options_index:
                options_index[identity] = len(options)
                options.append(state)
            source_id = len(levels)
            levels.append(
                {
                    "path": relative,
                    "sha256": digest(raw),
                    "documentId": digest(canonical(level).encode("utf-8")),
                    "levelId": level.get("levelId", ""),
                    "mapId": level.get("mapId", ""),
                    "options": options_index[identity],
                    "source": source_name,
                    "sourceRevision": source_revision,
                }
            )
            if state["state"] != "object":
                continue
            for key, value in state["value"].items():
                _record(fields.setdefault(key, {}), value, source_id)
            records = state["value"].get("configBlackBoard")
            if records is not None and not isinstance(records, list):
                anomalies.append(
                    {
                        "source": source_id,
                        "path": "options.configBlackBoard",
                        "value": records,
                    }
                )
            if isinstance(records, list):
                for index, value in enumerate(records):
                    if isinstance(value, dict) and isinstance(value.get("key"), str):
                        _record(blackboard[value["key"]], value, source_id, index)
                    else:
                        anomalies.append(
                            {
                                "source": source_id,
                                "path": f"options.configBlackBoard[{index}]",
                                "value": value,
                            }
                        )
        source_reports.append(
            {
                "id": source_name,
                "revision": source_revision,
                "scannedJsonFiles": len(paths),
                "levelCount": len(levels) - source_start,
                "excluded": source_excluded,
                "manifestSha256": digest(canonical(manifest).encode("utf-8")),
            }
        )
    states = Counter(options[row["options"]]["state"] for row in levels)
    result = {
        "version": 1,
        "revision": revision,
        "sources": source_reports,
        "corpus": {
            "scannedJsonFiles": sum(s["scannedJsonFiles"] for s in source_reports),
            "levelCount": len(levels),
            "uniqueDocumentCount": len({r["documentId"] for r in levels}),
            "optionStates": dict(states),
            "excluded": excluded,
            "blackboardAnomalies": anomalies,
            "recommendationPolicy": "primary source unique document frequency; at most three; samples are not a valid-value whitelist",
        },
        "levels": levels,
        "options": options,
        "fields": {key: _finish(groups, levels, key) for key, groups in fields.items()},
        "blackboard": {
            key: _finish(groups, levels, None)
            for key, groups in sorted(blackboard.items())
        },
    }
    result["corpus"]["fieldValueCount"] = sum(
        len(v["samples"]) for v in result["fields"].values()
    )
    result["corpus"]["blackboardKeyCount"] = len(blackboard)
    result["corpus"]["blackboardValueCount"] = sum(
        len(v["samples"]) for v in result["blackboard"].values()
    )
    result["corpus"]["blackboardOccurrenceCount"] = sum(
        len(r["references"])
        for v in result["blackboard"].values()
        for r in v["samples"]
    )
    return result


def _require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def verify_catalog(
    result: dict[str, Any],
    root: Path,
    supplement: Path | None = None,
    resource: Path | None = None,
) -> dict[str, int]:
    """Reread source bytes and verify surjective field/entry provenance, not just examples."""
    levels = result["levels"]
    roots = {PRIMARY: root, SUPPLEMENT: supplement, RESOURCE: resource}
    original_fields: dict[str, dict[str, set[int]]] = defaultdict(
        lambda: defaultdict(set)
    )
    original_blackboard: dict[str, dict[str, set[tuple[int, int]]]] = defaultdict(
        lambda: defaultdict(set)
    )
    seen = set()
    read_hashes = {}
    source_info = {source["id"]: source for source in result["sources"]}
    for source_id, source in enumerate(levels):
        source_root = roots.get(source["source"])
        _require(source_root is not None, "missing source root")
        _require(
            source["sourceRevision"] == source_info[source["source"]]["revision"],
            "source revision mismatch",
        )
        path = (source_root / source["path"]).resolve()
        _require(path.is_relative_to(source_root.resolve()), "source path escapes root")
        _require(
            (source["source"], source["path"]) not in seen, "duplicate source identity"
        )
        seen.add((source["source"], source["path"]))
        raw = path.read_bytes()
        level = strict_loads(raw)
        _require(digest(raw) == source["sha256"], "source bytes changed")
        read_hashes[(source["source"], source["path"])] = source["sha256"]
        _require(
            digest(canonical(level).encode("utf-8")) == source["documentId"],
            "document identity mismatch",
        )
        state = option_state(level)
        _require(
            canonical(state) == canonical(result["options"][source["options"]]),
            "options snapshot mismatch",
        )
        if state["state"] != "object":
            continue
        for key, value in state["value"].items():
            original_fields[key][canonical(value)].add(source_id)
        records = state["value"].get("configBlackBoard")
        if isinstance(records, list):
            for index, value in enumerate(records):
                if isinstance(value, dict) and isinstance(value.get("key"), str):
                    original_blackboard[value["key"]][canonical(value)].add(
                        (source_id, index)
                    )
    for source in result["sources"]:
        source_root = roots[source["id"]]
        manifest = []
        for path in sorted(source_root.rglob("*.json")):
            relative = path.relative_to(source_root).as_posix()
            known = read_hashes.get((source["id"], relative))
            manifest.append([relative, known if known else digest(path.read_bytes())])
        _require(
            digest(canonical(manifest).encode("utf-8")) == source["manifestSha256"],
            "source inventory changed",
        )
        _require(
            len(manifest) == source["scannedJsonFiles"],
            "source inventory count mismatch",
        )
    for field, group in result["fields"].items():
        expected = original_fields.get(field, {})
        actual = {
            canonical(row["value"]): set(row["sources"]) for row in group["samples"]
        }
        _require(
            actual == expected and len(actual) == len(group["samples"]),
            "field provenance mismatch: " + field,
        )
        present = {index for indices in actual.values() for index in indices}
        _require(
            group["missingSources"] == sorted(set(range(len(levels))) - present),
            "missing-state mismatch: " + field,
        )
        _verify_samples(group, levels, field)
    _require(
        set(result["fields"]) == set(FIELD_TYPES) | set(original_fields),
        "field universe mismatch",
    )
    _require(
        set(result["blackboard"]) == set(original_blackboard),
        "blackboard universe mismatch",
    )
    for key, group in result["blackboard"].items():
        actual = {
            canonical(row["value"]): {tuple(r) for r in row["references"]}
            for row in group["samples"]
        }
        _require(
            actual == original_blackboard[key] and len(actual) == len(group["samples"]),
            "blackboard provenance mismatch: " + key,
        )
        for row in group["samples"]:
            _require(
                row["sources"] == sorted({r[0] for r in row["references"]}),
                "entry sources mismatch",
            )
            _require(
                len(row["references"]) == len({tuple(r) for r in row["references"]}),
                "duplicate entry reference",
            )
        _verify_samples(group, levels, None)
    return {
        "levelsReread": len(levels),
        "fieldValuesVerified": sum(
            len(g["samples"]) for g in result["fields"].values()
        ),
        "blackboardValuesVerified": sum(
            len(g["samples"]) for g in result["blackboard"].values()
        ),
        "blackboardOccurrencesVerified": result["corpus"]["blackboardOccurrenceCount"],
    }


def _verify_samples(
    group: dict[str, Any], levels: list[dict[str, Any]], field: str | None
) -> None:
    ranks = [
        (-row["primaryCount"], -row["count"], canonical(row["value"]))
        for row in group["samples"]
    ]
    _require(ranks == sorted(ranks), "sample ranking mismatch")
    for row in group["samples"]:
        _require(
            row["sources"] == sorted(set(row["sources"])), "duplicate/unsorted source"
        )
        _require(
            row["count"] == len({levels[i]["documentId"] for i in row["sources"]}),
            "frequency mismatch",
        )
        _require(
            row["primaryCount"]
            == len(
                {
                    levels[i]["documentId"]
                    for i in row["sources"]
                    if levels[i]["source"] == PRIMARY
                }
            ),
            "primary frequency mismatch",
        )
    expected = [
        i
        for i, row in enumerate(group["samples"])
        if row["primaryCount"] > 0
        and (field is None or recommendable(field, row["value"]))
    ][:3]
    _require(group["recommended"] == expected, "recommendation mismatch")


def compare_legacy(result: dict[str, Any], old: dict[str, Any]) -> dict[str, Any]:
    """Legacy scan counted files/occurrences; compare those, not new deduplicated weights."""
    differences = []
    primary = {i for i, row in enumerate(result["levels"]) if row["source"] == PRIMARY}
    for section, old_section in [
        ("fields", "fields"),
        ("blackboard", "blackboardKeys"),
    ]:
        current = result[section]
        for key in set(current) | set(old[old_section]):
            rows = current.get(key, {}).get("samples", [])
            actual = {
                canonical(r["value"]): (
                    len([x for x in r["sources"] if x in primary])
                    if section == "fields"
                    else len([x for x in r["references"] if x[0] in primary])
                )
                for r in rows
            }
            actual = {key: count for key, count in actual.items() if count}
            expected = {
                canonical(r["value"]): r["count"] for r in old[old_section].get(key, [])
            }
            if actual != expected:
                differences.append(
                    {
                        "section": section,
                        "key": key,
                        "actual": actual,
                        "legacy": expected,
                    }
                )
    return {
        "legacyLevelCount": old["levelCount"],
        "primaryLevelCount": len(primary),
        "differenceCount": len(differences),
        "differences": differences,
    }


def git_attestation(root: Path, *, required: bool) -> dict[str, Any]:
    head = subprocess.check_output(
        ["git", "-C", str(root), "rev-parse", "HEAD"], text=True
    ).strip()
    dirty = subprocess.check_output(
        ["git", "-C", str(root), "status", "--porcelain", "--", "."], text=True
    )
    tracked = {
        path
        for path in subprocess.check_output(
            ["git", "-C", str(root), "ls-files", "-z", "--", "."]
        )
        .decode("utf-8")
        .split("\0")
        if path.endswith(".json")
    }
    actual = {path.relative_to(root).as_posix() for path in root.rglob("*.json")}
    untracked = sorted(actual - tracked)
    bound = not dirty.strip() and not untracked and actual == tracked
    if required and not bound:
        raise ValueError(
            "primary JSON bodies must be tracked and match the recorded Git revision"
        )
    return {
        "revision": head if bound else None,
        "observedRepositoryRevision": head,
        "revisionBoundToBodies": bound,
        "trackedJsonFiles": len(actual & tracked),
        "untrackedJsonFiles": untracked,
        "worktreeClean": not dirty.strip(),
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--levels", type=Path, required=True)
    parser.add_argument("--supplement-levels", type=Path)
    parser.add_argument("--resource-levels", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--audit", type=Path, required=True)
    parser.add_argument("--legacy", type=Path)
    args = parser.parse_args()
    if any(
        path and not path.is_dir()
        for path in (args.levels, args.supplement_levels, args.resource_levels)
    ):
        parser.error("input directories must exist")
    primary_info = git_attestation(args.levels, required=True)
    resource_info = (
        git_attestation(args.resource_levels, required=False)
        if args.resource_levels
        else None
    )
    revision = primary_info["revision"]
    resource_revision = resource_info["revision"] if resource_info else None
    result = build_catalog(
        args.levels,
        revision,
        args.supplement_levels,
        args.resource_levels,
        resource_revision,
    )
    for source in result["sources"]:
        if source["id"] == PRIMARY:
            source.update(primary_info)
        elif source["id"] == RESOURCE:
            source.update(resource_info)
    encoded = dumps_json(result).encode("utf-8") + b"\n"
    verification = verify_catalog(
        strict_loads(encoded), args.levels, args.supplement_levels, args.resource_levels
    )
    legacy = (
        compare_legacy(result, strict_loads(args.legacy.read_bytes()))
        if args.legacy
        else None
    )
    if primary_info != git_attestation(args.levels, required=True):
        raise ValueError("primary source changed while building")
    if resource_info and resource_info != git_attestation(
        args.resource_levels, required=False
    ):
        raise ValueError("resource source changed while building")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(encoded)
    report = {
        "revision": revision,
        "sources": result["sources"],
        "corpus": result["corpus"],
        "verification": verification,
        "legacyComparison": legacy,
        "outputBytes": len(encoded),
        "outputSha256": digest(encoded),
    }
    args.audit.parent.mkdir(parents=True, exist_ok=True)
    args.audit.write_text(dumps_json(report) + "\n", encoding="utf-8")
    print(
        json.dumps(
            {
                "sources": result["sources"],
                "corpus": result["corpus"],
                "verification": verification,
                "legacyDifferenceCount": legacy["differenceCount"] if legacy else None,
                "outputBytes": len(encoded),
            },
            ensure_ascii=False,
            indent=2,
        )
    )


if __name__ == "__main__":
    main()
