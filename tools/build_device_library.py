"""Package the frozen preset census into a lightweight index and per-device samples."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
from pathlib import Path
from typing import Any

from tools.audit_device_presets import canonical, fingerprint
from tools.audit_level_modes import decode_json

COMMON_FIELDS = frozenset(("inst", "skillIndex", "mainSkillLvl", "skinId", "tmplId",
                          "overrideSkillBlackboard", "overrideTalents", "uniEquipIds",
                          "showSpIllust", "masterInfos", "hidden", "alias"))


def build_role(data: dict[str, Any], levels: dict[str, int], role: str) -> dict[str, Any]:
    if role not in ("tokenInsts", "tokenCards"):
        raise ValueError("Unsupported device role")
    # Only map placement is excluded. Hidden, aliases, unknown fields and card
    # quantities remain part of the configuration identity.
    excluded = {"position", "direction"} if role == "tokenInsts" else set()
    configurations: dict[str, Any] = {}
    for sample_id, sample in sorted(data["exactSamples"].items()):
        configuration = {key: value for key, value in sample["data"].items() if key not in excluded}
        identity = fingerprint(configuration)
        row = configurations.setdefault(identity, {"id": identity, "data": configuration, "originals": []})
        if canonical(row["data"]) != canonical(configuration):
            raise ValueError("Configuration fingerprint collision")
        refs = []
        for ref in sample["references"]:
            _, section, _, index = ref["pointer"].split("/")
            refs.append([levels[ref["level"]], section, int(index)])
        row["originals"].append({"id": sample_id, "data": sample["data"], "references": refs})
    samples = [configurations[key] for key in sorted(configurations)]

    def rank(index: int) -> tuple:
        row = samples[index]
        record = row["data"]
        identifiers = sum(bool(pair.get("valueStr")) for pair in record.get("overrideSkillBlackboard") or [])
        source_count = len({ref[0] for original in row["originals"] for ref in original["references"]})
        return (identifiers, len(COMMON_FIELDS - record.keys()),
                len(record.get("overrideSkillBlackboard") or []) + len(record.get("overrideTalents") or []),
                bool(record.get("hidden")), bool(record.get("alias")), -source_count, row["id"])

    return {"recommended": sorted(range(len(samples)), key=rank)[:3], "samples": samples}


def build_library(evidence: dict[str, Any], characters: dict[str, Any], legacy: dict[str, Any]) -> tuple[dict, dict]:
    paths = sorted(evidence["levels"])
    levels = {path: index for index, path in enumerate(paths)}
    historic = {row["id"]: row["name"] for row in legacy["historicalOnly"]}
    ids = set(evidence["devices"]) | set(historic) | {
        key for key, value in characters.items() if key.startswith("trap_") or value.get("profession") == "TRAP"}
    entries = []
    files = {}
    for key in sorted(ids):
        if not re.fullmatch(r"[A-Za-z0-9_]+", key):
            raise ValueError(f"Unsafe device resource key: {key!r}")
        definition = characters.get(key, {})
        observed = evidence["devices"].get(key, {"roles": {}})
        filename = key + ".json" if observed["roles"] else None
        roles = {}
        packed_roles = {}
        for role, data in observed["roles"].items():
            packed_roles[role] = build_role(data, levels, role)
            roles[role] = {"configurations": len(packed_roles[role]["samples"]),
                           "exactSamples": len(data["exactSamples"]), "sources": len(data["levels"])}
        entries.append({"id": key, "name": (definition.get("name") or historic.get(key) or key).strip() or key,
                        "description": definition.get("description") or "", "file": filename,
                        "roles": roles, "legacyOnly": key in historic})
        if filename:
            files[filename] = {"version": 3, "id": key, "roles": packed_roles}
    return {"version": 3, "revision": evidence["source"]["gitBefore"]["revision"],
            "evidenceSha256": None, "levels": paths, "entries": entries,
            "recommendationPolicy": "Up to three existing configurations, fewer textual identifiers, complete fields, fewer overrides, broader source coverage. Not a guarantee of standalone use."}, files


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("evidence", "characters", "legacy-roster", "output"):
        parser.add_argument("--" + name, required=True, type=Path)
    args = parser.parse_args()
    raw = args.evidence.read_bytes()
    evidence = decode_json(raw)
    if evidence["errors"] or evidence["issues"]:
        parser.error("Census has unresolved parsing or record issues")
    if hashlib.sha256(args.characters.read_bytes()).hexdigest() != evidence["sourceHashes"]["excel/character_table.json"]:
        parser.error("Character definitions do not match the census snapshot")
    if args.output.exists():
        parser.error("Use a new output directory")
    index, files = build_library(evidence, decode_json(args.characters.read_bytes()), decode_json(args.legacy_roster.read_bytes()))
    index["evidenceSha256"] = hashlib.sha256(raw).hexdigest()
    args.output.mkdir(parents=True)
    (args.output / "DeviceSamples").mkdir()
    for name, value in [("device-index.json", index), *(("DeviceSamples/" + name, data) for name, data in files.items())]:
        with (args.output / name).open("x", encoding="utf-8") as target:
            json.dump(value, target, ensure_ascii=False, separators=(",", ":"), allow_nan=False)
            target.write("\n")
    print(json.dumps({"entries": len(index["entries"]), "sampleFiles": len(files)}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
