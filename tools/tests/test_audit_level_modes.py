import importlib.util
import json
import sys
from pathlib import Path

import pytest

SOURCE = Path(__file__).parents[1] / "audit_level_modes.py"
SPEC = importlib.util.spec_from_file_location("audit_level_modes", SOURCE)
audit = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(audit)


@pytest.mark.parametrize(
    ("value", "expected"),
    [("NORMAL", 1), ("ALL", 15), ("NONE", 0), (3, 3), ("NORMAL, FOUR_STAR", 3)],
)
def test_difficulty_mask_accepts_native_representations(value, expected):
    assert audit.difficulty_mask(value) == expected


@pytest.mark.parametrize("value", [True, -1, 16, "UNKNOWN", None, "NORMAL,"])
def test_difficulty_mask_rejects_unknown_or_ambiguous_values(value):
    with pytest.raises(ValueError):
        audit.difficulty_mask(value)


@pytest.mark.parametrize(
    ("mask", "requested", "expected"),
    [
        ("ALL", "NORMAL", True),
        ("NORMAL", "FOUR_STAR", False),
        (3, 3, True),
        (3, 6, False),
        ("NONE", "NORMAL", False),
        ("NONE", "NONE", True),
    ],
)
def test_native_difficulty_predicate_requires_all_requested_bits(
    mask, requested, expected
):
    assert audit.valid_for_difficulty(mask, requested) is expected


def test_json_duplicate_keys_are_rejected(tmp_path):
    source = tmp_path / "bad.json"
    source.write_text('{"runes": [], "runes": null}', encoding="utf-8")
    with pytest.raises(ValueError, match="Duplicate"):
        audit.load_json(source)


def test_missing_null_and_empty_remain_distinct():
    empty = audit.field_digests({})
    null = audit.field_digests({"optionalRunes": None})
    present = audit.field_digests({"optionalRunes": {}})
    assert audit.changed_fields(empty, null) == ["optionalRunes"]
    assert audit.changed_fields(null, present) == ["optionalRunes"]


def test_palette_reordering_does_not_change_resolved_tiles():
    left = audit.map_structure(
        {"map": [[0, 1]], "tiles": [{"tileKey": "a"}, {"tileKey": "b"}]}
    )
    right = audit.map_structure(
        {"map": [[1, 0]], "tiles": [{"tileKey": "b"}, {"tileKey": "a"}]}
    )
    assert left["status"] == "grid"
    assert left["resolvedTilesSha256"] == right["resolvedTilesSha256"]


@pytest.mark.parametrize("index", [True, -1, 1])
def test_invalid_palette_indices_are_reported(index):
    result = audit.map_structure({"map": [[index]], "tiles": [{}]})
    assert result["status"] == "invalid"
    assert len(result["issues"]) == 1


def test_null_layout_is_separate_from_a_missing_or_invalid_layout():
    assert audit.map_structure({"map": None, "tiles": None})["status"] == "null-layout"
    assert audit.map_structure({})["status"] == "invalid"
    assert audit.map_structure({"map": [None], "tiles": []})["status"] == "invalid"
    assert audit.map_structure({"map": [[], [0]], "tiles": [{}]})["status"] == "invalid"


def test_relation_uses_explicit_identity_and_reports_missing_resources():
    stages = {
        "normal": {
            "levelId": "Obt/Main/map",
            "stageType": "MAIN",
            "hardStagedId": "hard",
        },
        "hard": {"levelId": "Obt/Main/map", "stageType": "MAIN"},
        "broken": {"levelId": "missing", "sixStarStageId": "absent"},
    }
    files = {"obt/main/map.json": {"fieldDigests": {"mapData": "same"}}}
    result = audit.stage_relations({"stages": stages}, files)
    assert result["edges"][0]["sameLevelResource"] is True
    assert result["edges"][0]["differentFields"] == []
    assert result["unresolved"][0]["target"] == "absent"


def test_native_level_mapping_is_exact_single_hop_and_uses_level_not_scene():
    pairs = {
        "Easy": {"levelId": "Normal", "sceneId": "Art"},
        "Normal": {"levelId": "Other"},
    }
    assert audit.level_resource({"levelId": "Easy"}, pairs) == "normal.json"
    assert audit.level_resource({"levelId": "easy"}, pairs) == "easy.json"


def test_level_mapping_resolves_a_relation_without_inventing_a_missing_file():
    table = {
        "stages": {
            "normal": {"levelId": "Normal", "hardStagedId": "easy"},
            "easy": {"levelId": "Easy"},
        }
    }
    files = {"normal.json": {"fieldDigests": {"mapData": "same"}}}
    result = audit.stage_relations(table, files, {"Easy": {"levelId": "Normal"}})
    assert not result["unresolved"]
    assert result["edges"][0]["sameLevelResource"] is True


def test_corpus_distinguishes_nonlevels_and_invalid_json(tmp_path):
    levels = tmp_path / "levels"
    excel = tmp_path / "excel"
    levels.mkdir()
    excel.mkdir()
    (excel / "stage_table.json").write_text('{"stages": {}}', encoding="utf-8")
    document = {
        "mapData": {"map": [[0]], "tiles": [{"tileKey": "tile_road"}]},
        "runes": [{"difficultyMask": "FOUR_STAR"}],
        "optionalRunes": None,
        "predefines": {"tokenInsts": []},
    }
    (levels / "map.json").write_text(json.dumps(document), encoding="utf-8")
    (levels / "other.json").write_text('{"enemyDb": []}', encoding="utf-8")
    (levels / "bad.json").write_text('{"broken":', encoding="utf-8")
    result = audit.audit_corpus(tmp_path)
    assert result["summary"]["levelCount"] == 1
    assert result["summary"]["nonLevelCount"] == 1
    assert len(result["errors"]) == 1
    assert result["files"]["map.json"]["runeMasks"] == {"2": 1}
    assert result["summary"]["runtimeValidated"] is False


def test_six_star_binding_requires_the_exact_optional_package():
    table = {
        "stages": {
            "six": {
                "levelId": "map",
                "difficulty": "SIX_STAR",
                "advancedRuneIdList1": ["good", "bad"],
            }
        },
        "sixStarRuneData": {
            "good": {"runeKey": "package"},
            "bad": {"runeKey": "absent"},
        },
    }
    result = audit.six_star_bindings(
        table, {"map.json": {"optionalRuneKeys": {"package": 2}}}
    )
    assert len(result["records"]) == 2
    assert [row["runeId"] for row in result["issues"]] == ["bad"]
    assert result["runtimeValidated"] is False


def test_cli_keeps_source_bytes_and_does_not_overwrite_evidence(tmp_path, monkeypatch):
    gamedata = tmp_path / "source"
    (gamedata / "levels").mkdir(parents=True)
    (gamedata / "excel").mkdir()
    stage_table = gamedata / "excel" / "stage_table.json"
    stage_table.write_bytes(b'{"stages": {}}')
    output = tmp_path / "evidence" / "audit.json"
    monkeypatch.setattr(
        sys, "argv", ["audit", "--gamedata", str(gamedata), "--output", str(output)]
    )
    assert audit.main() == 0
    before = output.read_bytes()
    with pytest.raises(SystemExit):
        audit.main()
    assert output.read_bytes() == before
    assert stage_table.read_bytes() == b'{"stages": {}}'


def test_cli_rejects_writing_into_the_source_tree(tmp_path, monkeypatch):
    monkeypatch.setattr(
        sys,
        "argv",
        [
            "audit",
            "--gamedata",
            str(tmp_path),
            "--output",
            str(tmp_path / "audit.json"),
        ],
    )
    with pytest.raises(SystemExit):
        audit.main()
    assert not (tmp_path / "audit.json").exists()
