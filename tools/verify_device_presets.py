"""Independently round-trip an audit to every source JSON pointer and payload."""

from __future__ import annotations

import argparse
import hashlib
import json
from collections import Counter
from pathlib import Path
from typing import Any

from tools.audit_level_modes import decode_json


def encoded(value: Any) -> bytes:
    return json.dumps(value, sort_keys=True, ensure_ascii=False,
                      separators=(",", ":"), allow_nan=False).encode("utf-8")


def sha(value: Any) -> str:
    return hashlib.sha256(encoded(value)).hexdigest()


def verify_result(gamedata: Path, result: dict[str, Any]) -> dict[str, Any]:
    errors = []
    source_records: dict[tuple[str, str], Any] = {}
    level_count = 0
    hashes = 0
    source_files = {path.relative_to(gamedata).as_posix()
                    for path in (gamedata / "levels").rglob("*.json")}
    expected_files = {name for name in result["sourceHashes"] if name.startswith("levels/")}
    if source_files != expected_files:
        errors.append("source-file-set-mismatch")
    for name, expected_hash in result["sourceHashes"].items():
        path = gamedata / name
        if not path.is_file():
            errors.append(f"source-missing:{name}")
            continue
        raw = path.read_bytes()
        if hashlib.sha256(raw).hexdigest() != expected_hash:
            errors.append(f"source-hash-mismatch:{name}")
        hashes += 1
        if not name.startswith("levels/"):
            continue
        document = decode_json(raw)
        if not isinstance(document, dict) or not isinstance(document.get("mapData"), dict):
            continue
        level_count += 1
        file = name.removeprefix("levels/")
        for group in ("predefines", "hardPredefines"):
            container = document.get(group)
            if not isinstance(container, dict):
                continue
            for role in ("tokenInsts", "tokenCards"):
                records = container.get(role)
                if not isinstance(records, list):
                    continue
                for index, record in enumerate(records):
                    source_records[file, f"/{group}/{role}/{index}"] = record
    seen: Counter[tuple[str, str]] = Counter()
    roles: Counter[str] = Counter()
    for key, device in result["devices"].items():
        for role, data in device["roles"].items():
            role_levels = set()
            count = 0
            for sample_id, sample in data["exactSamples"].items():
                payload = sample["data"]
                if sha(payload) != sample_id or payload.get("inst", {}).get("characterKey") != key:
                    errors.append(f"sample-identity-mismatch:{key}/{sample_id}")
                for ref in sample["references"]:
                    location = ref["level"], ref["pointer"]
                    if ref["pointer"].split("/")[2] != role:
                        errors.append(f"record-role-mismatch:{location}")
                    seen[location] += 1
                    count += 1
                    roles[role] += 1
                    role_levels.add(ref["level"])
                    if location not in source_records or encoded(source_records[location]) != encoded(payload):
                        errors.append(f"source-record-mismatch:{location}")
            if count != data["occurrences"] or sorted(role_levels) != data["levels"]:
                errors.append(f"role-count-mismatch:{key}/{role}")
            member_ids = []
            for config in data["configurations"]:
                if sha(config["data"]) != config["id"]:
                    errors.append(f"projection-hash-mismatch:{config['id']}")
                references = []
                for sample_id in config["exactSampleIds"]:
                    member_ids.append(sample_id)
                    sample = data["exactSamples"][sample_id]
                    excluded = ({"position", "direction", "hidden", "alias"}
                                if role == "tokenInsts" else {"hidden", "alias"})
                    projection = {field: value for field, value in sample["data"].items() if field not in excluded}
                    if encoded(projection) != encoded(config["data"]) or sample["configuration"] != config["id"]:
                        errors.append(f"projection-member-mismatch:{sample_id}")
                    references.extend(sample["references"])
                if len(references) != config["occurrences"] or sorted({r["level"] for r in references}) != config["levels"]:
                    errors.append(f"projection-count-mismatch:{config['id']}")
            if Counter(member_ids) != Counter({sample_id: 1 for sample_id in data["exactSamples"]}):
                errors.append(f"projection-partition-mismatch:{key}/{role}")
    if set(seen) != set(source_records) or any(count != 1 for count in seen.values()):
        errors.append("missing-or-duplicate-source-references")
    if sum(seen.values()) != result["summary"]["validRecords"]:
        errors.append("summary-count-mismatch")
    if level_count != result["summary"]["levelCount"] or len(result["devices"]) != result["summary"]["deviceIds"]:
        errors.append("summary-corpus-count-mismatch")
    for role, count in roles.items():
        summary = result["summary"]["roles"][role]
        rows = [device["roles"][role] for device in result["devices"].values() if role in device["roles"]]
        calculated = {"records": count, "deviceIds": len(rows),
                      "exactSamples": sum(len(row["exactSamples"]) for row in rows),
                      "configurations": sum(len(row["configurations"]) for row in rows),
                      "levels": len({file for row in rows for file in row["levels"]})}
        if any(summary[field] != value for field, value in calculated.items()):
            errors.append(f"summary-role-count-mismatch:{role}")
    return {"verifiedSourceHashes": hashes, "verifiedRecords": sum(seen.values()),
            "sourceRecords": len(source_records), "errors": errors}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--gamedata", required=True, type=Path)
    parser.add_argument("--evidence", required=True, type=Path)
    args = parser.parse_args()
    result = verify_result(args.gamedata, decode_json(args.evidence.read_bytes()))
    print(json.dumps(result, ensure_ascii=False))
    return int(bool(result["errors"]))


if __name__ == "__main__":
    raise SystemExit(main())
