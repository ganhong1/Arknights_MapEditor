"""Build a pinned enemy database and full LevelData sample catalogue."""

from __future__ import annotations

import argparse
import hashlib
import json
import zipfile
from collections import defaultdict
from pathlib import Path

REVISION = "26-10-22-14-02-11-51447808"
DATABASE_SHA256 = "2a0a1c7690280bb88795a7873c31273d4aeac30c96a40ca23c24f95f430447a0"
DATABASE_URL = f"https://torappu.prts.wiki/gamedata/{REVISION}/levels/enemydata/enemy_database.json"


def encode(value: object) -> bytes:
    return json.dumps(
        value, ensure_ascii=False, separators=(",", ":"), allow_nan=False
    ).encode("utf-8")


def fingerprint(value: object) -> str:
    return json.dumps(
        value,
        sort_keys=True,
        ensure_ascii=False,
        separators=(",", ":"),
        allow_nan=False,
    )


def write_entry(archive: zipfile.ZipFile, name: str, value: object) -> None:
    info = zipfile.ZipInfo(name, date_time=(2026, 10, 22, 0, 0, 0))
    info.compress_type = zipfile.ZIP_DEFLATED
    archive.writestr(info, encode(value))


def unwrap(value: object) -> object:
    if isinstance(value, dict):
        return value.get("m_value") if value.get("m_defined") else None
    return value


def build_catalog(database: dict, levels: Path, output: Path, source: dict) -> dict:
    database_by_id = {}
    for item in database["enemies"]:
        key = item["Key"]
        if key in database_by_id:
            raise ValueError(f"Duplicate database ID: {key}")
        database_by_id[key] = item["Value"]
    samples = defaultdict(lambda: {"references": {}, "inline": {}})
    sources = defaultdict(list)
    with zipfile.ZipFile(levels) as archive:
        index = json.loads(archive.read("library-index.json"))
        stages = {row["id"]: row.get("label") for row in index["stages"]}
        for origin in index["sources"]:
            sources[origin["body"]].append(
                {
                    **origin,
                    "stageLabels": [
                        stages[key] for key in origin["stages"] if key in stages
                    ],
                }
            )
        for body, source_index in index["bodies"].items():
            content = archive.read(f"levels/{body}.json")
            if (
                hashlib.sha256(content).hexdigest()
                != index["sources"][source_index]["sha256"]
            ):
                raise ValueError(f"Level body checksum mismatch: {body}")
            document = json.loads(content)
            for section, role in (("enemyDbRefs", "references"), ("enemies", "inline")):
                for position, record in enumerate(document.get(section) or []):
                    key = (
                        record.get("id")
                        if role == "references"
                        else record.get("prefabKey") or record.get("key")
                    )
                    if not isinstance(key, str) or not key:
                        key = "<unresolved>"
                    sample = samples[key][role].setdefault(
                        fingerprint(record), {"record": record, "origins": []}
                    )
                    sample["origins"].append(
                        {"body": body, "section": section, "index": position}
                    )
    output.mkdir(parents=True, exist_ok=True)
    rows = []
    used_bodies = set()
    with zipfile.ZipFile(output / "enemy-details.zip", "w") as archive:
        for key in sorted(set(database_by_id) | set(samples)):
            db_levels = database_by_id.get(key, [])
            name, description = key, ""
            for level in sorted(db_levels, key=lambda item: item["level"]):
                data = level["enemyData"]
                if level["level"] == 0 or name == key:
                    name = unwrap(data.get("name")) or name
                    description = unwrap(data.get("description")) or description
            groups = {
                role: sorted(
                    samples[key][role].values(),
                    key=lambda item: (
                        -len(item["origins"]),
                        fingerprint(item["record"]),
                    ),
                )
                for role in ("references", "inline")
            }
            for group in groups.values():
                used_bodies.update(
                    origin["body"] for sample in group for origin in sample["origins"]
                )
            detail_name = hashlib.sha256(key.encode()).hexdigest() + ".json"
            rows.append(
                {
                    "id": key,
                    "name": name,
                    "description": description,
                    "levels": [item["level"] for item in db_levels],
                    "referenceSamples": len(groups["references"]),
                    "inlineSamples": len(groups["inline"]),
                    "hasDatabase": bool(db_levels),
                    "detail": detail_name,
                }
            )
            detail_source = (
                source
                if db_levels
                else {
                    "status": "historical-samples",
                    "samples": source.get("samples", "finished-levels.zip"),
                    "databaseMatch": False,
                }
            )
            write_entry(
                archive,
                detail_name,
                {
                    "id": key,
                    "databaseLevels": db_levels,
                    **groups,
                    "recommendations": list(range(min(3, len(groups["references"])))),
                    "source": detail_source,
                },
            )
        write_entry(
            archive,
            "sources.json",
            {body: sources[body] for body in sorted(used_bodies)},
        )
    index = {
        "version": 1,
        "client": "2.7.81",
        "revision": REVISION,
        "source": source,
        "levelArchiveSha256": hashlib.sha256(levels.read_bytes()).hexdigest(),
        "detailsSha256": hashlib.sha256(
            (output / "enemy-details.zip").read_bytes()
        ).hexdigest(),
        "entries": rows,
    }
    (output / "enemy-index.json").write_bytes(encode(index))
    return {
        "entries": len(rows),
        "databaseEntries": len(database_by_id),
        "referenceSamples": sum(row["referenceSamples"] for row in rows),
        "inlineSamples": sum(row["inlineSamples"] for row in rows),
        "bodies": len(used_bodies),
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("database", type=Path)
    parser.add_argument("levels", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    raw = args.database.read_bytes()
    digest = hashlib.sha256(raw).hexdigest()
    if digest != DATABASE_SHA256:
        raise ValueError("Database differs from the pinned 2.7.81 resource snapshot")
    source = {
        "status": "pinned-resource",
        "client": "2.7.81",
        "revision": REVISION,
        "url": DATABASE_URL,
        "sha256": digest,
        "samples": "finished-levels.zip historical source identities retained per origin",
    }
    print(
        json.dumps(
            build_catalog(json.loads(raw), args.levels, args.output, source),
            ensure_ascii=False,
        )
    )


if __name__ == "__main__":
    main()
