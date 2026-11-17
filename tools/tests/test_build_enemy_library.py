"""Enemy catalogue preserves native records and independently versioned origins."""

import hashlib
import importlib.util
import json
import zipfile
from pathlib import Path

import pytest

SPEC = importlib.util.spec_from_file_location(
    "enemy_builder", Path(__file__).parents[1] / "build_enemy_library.py"
)
builder = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(builder)


def archive_at(tmp_path, documents, corrupt=False):
    path = tmp_path / "levels.zip"
    sources = []
    bodies = {}
    with zipfile.ZipFile(path, "w") as archive:
        for index, document in enumerate(documents):
            content = builder.encode(document)
            body = hashlib.sha256(content).hexdigest()
            bodies[body] = index
            sources.append(
                {
                    "body": body,
                    "sha256": "invalid" if corrupt else body,
                    "stages": ["stage_a"],
                    "path": f"levels/{index}.json",
                    "revision": "historical-fixed",
                    "source": "fixture",
                }
            )
            archive.writestr(f"levels/{body}.json", content)
        archive.writestr(
            "library-index.json",
            builder.encode(
                {
                    "bodies": bodies,
                    "sources": sources,
                    "stages": [
                        {"id": "stage_a", "label": ["A-1", "Fixture", "NORMAL"]}
                    ],
                }
            ),
        )
    return path


def database():
    return {
        "enemies": [
            {
                "Key": "enemy_a",
                "Value": [
                    {
                        "level": 0,
                        "enemyData": {"name": {"m_defined": True, "m_value": "敌人甲"}},
                    },
                    {
                        "level": 1,
                        "enemyData": {
                            "attributes": {"maxHp": {"m_defined": True, "m_value": 0}}
                        },
                    },
                ],
            }
        ]
    }


def detail(output, key):
    index = json.loads((output / "enemy-index.json").read_bytes())
    entry = next(row for row in index["entries"] if row["id"] == key)
    with zipfile.ZipFile(output / "enemy-details.zip") as archive:
        return (
            entry,
            json.loads(archive.read(entry["detail"])),
            json.loads(archive.read("sources.json")),
        )


def test_full_record_dedup_and_distinct_overrides(tmp_path):
    reference = {"id": "enemy_a", "useDb": True, "level": 0, "overwrittenData": None}
    levels = archive_at(
        tmp_path,
        [
            {"enemyDbRefs": [reference, {**reference, "level": 1}]},
            {"enemyDbRefs": [reference], "mapId": "other"},
        ],
    )
    output = tmp_path / "out"
    result = builder.build_catalog(database(), levels, output, {"revision": "pinned"})
    entry, record, sources = detail(output, "enemy_a")
    assert result["referenceSamples"] == 2
    assert entry["levels"] == [0, 1]
    assert record["references"][0]["record"] == reference
    assert len(record["references"][0]["origins"]) == 2
    assert (
        record["databaseLevels"][1]["enemyData"]["attributes"]["maxHp"]["m_value"] == 0
    )
    assert all(item[0]["revision"] == "historical-fixed" for item in sources.values())


def test_inline_local_identity_and_unknown_database_are_retained(tmp_path):
    inline = {"key": "local_b", "prefabKey": "enemy_a", "attributes": {"maxHp": 12}}
    levels = archive_at(
        tmp_path,
        [
            {
                "enemies": [inline],
                "enemyDbRefs": [{"id": "enemy_unknown", "useDb": False}],
            }
        ],
    )
    output = tmp_path / "out"
    builder.build_catalog(database(), levels, output, {})
    assert detail(output, "enemy_a")[1]["inline"][0]["record"] == inline
    unknown = detail(output, "enemy_unknown")
    assert unknown[0]["hasDatabase"] is False
    assert unknown[1]["databaseLevels"] == []


def test_corrupt_body_is_rejected(tmp_path):
    levels = archive_at(tmp_path, [{}], corrupt=True)
    with pytest.raises(ValueError, match="checksum"):
        builder.build_catalog(database(), levels, tmp_path / "out", {})


def test_generation_is_reproducible(tmp_path):
    levels = archive_at(tmp_path, [{"enemyDbRefs": [{"id": "enemy_a"}]}])
    for name in ("a", "b"):
        builder.build_catalog(database(), levels, tmp_path / name, {})
    for name in ("enemy-index.json", "enemy-details.zip"):
        assert (tmp_path / "a" / name).read_bytes() == (
            tmp_path / "b" / name
        ).read_bytes()


def test_duplicate_database_ids_rejected(tmp_path):
    data = database()
    data["enemies"].append(data["enemies"][0])
    with pytest.raises(ValueError, match="Duplicate"):
        builder.build_catalog(data, tmp_path / "unused.zip", tmp_path / "out", {})
