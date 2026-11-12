"""Contracts for a read-only device preset census, independent of the plugin."""

import copy
import json
from pathlib import Path

import pytest

from tools import audit_device_presets as audit
from tools import verify_device_presets as verify


def record(key="trap_box", **fields):
    return {"inst": {"characterKey": key, "level": 1}, **fields}


def level(insts=None, cards=None, **fields):
    return {
        "mapData": {"map": [[0]], "tiles": [{"tileKey": "tile_road"}]},
        "predefines": {"tokenInsts": insts or [], "tokenCards": cards or []},
        **fields,
    }


def corpus(tmp_path, documents, stages=None, pairs=None, characters=None):
    root = tmp_path / "gamedata"
    for path, data in documents.items():
        write_json(root / "levels" / path, data)
    write_json(root / "excel/stage_table.json", {"stages": stages or {}})
    write_json(root / "battle/battle_misc_table.json", {"levelScenePairs": pairs or {}})
    write_json(root / "excel/character_table.json", characters or {})
    return root


def write_json(path: Path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value), encoding="utf-8")


def test_exact_key_order_dedup_preserves_array_order_and_presence():
    assert audit.fingerprint({"x": 1, "y": 2}) == audit.fingerprint({"y": 2, "x": 1})
    assert len({audit.fingerprint(x) for x in ({}, {"x": None}, {"x": []})}) == 3
    assert audit.fingerprint({"x": [1, 2]}) != audit.fingerprint({"x": [2, 1]})
    assert audit.fingerprint({"x": 1}) != audit.fingerprint({"x": 1.0})


def test_projection_excludes_only_explicit_context_without_mutating_input():
    source = record(position={"row": 0, "col": 0}, direction="UP", hidden=True,
                    alias="a", futureField={"blackboard": [1, 2]})
    before = copy.deepcopy(source)
    projected = audit.project_record(source, "tokenInsts")
    assert projected == record(futureField={"blackboard": [1, 2]})
    assert source == before
    assert audit.project_record(record(initialCnt=7, hidden=False), "tokenCards") == record(initialCnt=7)


def test_positions_separate_exact_samples_but_not_projection(tmp_path):
    one = record(position={"row": 0, "col": 0}, direction="UP", alias="a")
    two = record(position={"row": 0, "col": 1}, direction="RIGHT", alias="b")
    root = corpus(tmp_path, {"a.json": level([one, two, one]), "b.json": level([one])})
    result = audit.audit_corpus(root)
    role = result["devices"]["trap_box"]["roles"]["tokenInsts"]
    assert role["occurrences"] == 4
    assert len(role["exactSamples"]) == 2
    assert len(role["configurations"]) == 1
    assert len(role["levels"]) == 2
    assert sorted(len(x["references"]) for x in role["exactSamples"].values()) == [1, 3]
    assert role["configurations"][0]["occurrences"] == 4
    assert len(result["levels"]["a.json"]["records"]) == 3


def test_roles_and_predefine_containers_are_independent(tmp_path):
    same = record(initialCnt=2)
    root = corpus(tmp_path, {"a.json": level([same], [same], hardPredefines={
        "tokenInsts": [same], "tokenCards": []})})
    result = audit.audit_corpus(root)
    device = result["devices"]["trap_box"]
    assert set(device["roles"]) == {"tokenInsts", "tokenCards"}
    insts = device["roles"]["tokenInsts"]
    assert len(insts["exactSamples"]) == 1
    refs = next(iter(insts["exactSamples"].values()))["references"]
    assert {ref["pointer"] for ref in refs} == {
        "/predefines/tokenInsts/0", "/hardPredefines/tokenInsts/0"}
    assert result["summary"]["sections"]["hardPredefines/tokenInsts"]["records"] == 1


def test_card_quantity_is_part_of_configuration(tmp_path):
    root = corpus(tmp_path, {"a.json": level(cards=[record(initialCnt=1), record(initialCnt=9)])})
    role = audit.audit_corpus(root)["devices"]["trap_box"]["roles"]["tokenCards"]
    assert len(role["configurations"]) == 2


def test_nontrap_and_unknown_definitions_are_not_filtered(tmp_path):
    root = corpus(tmp_path, {"a.json": level([record("token_tower"), record("unknown")])},
                  characters={"token_tower": {"name": "Tower", "profession": "TOKEN"},
                              "trap_absent": {"name": "Absent", "profession": "TRAP"}})
    result = audit.audit_corpus(root)
    assert set(result["devices"]) == {"token_tower", "unknown"}
    assert result["summary"]["unobservedStaticDeviceIds"] == ["trap_absent"]
    assert result["summary"]["unknownDefinitionIds"] == ["unknown"]


def test_blackboard_observations_preserve_records_and_count_context(tmp_path):
    item = record(hidden=True, overrideSkillBlackboard=[
        {"key": "branch_id", "valueStr": "branch_a"},
        {"key": "future", "value": 3}, {"key": "future", "value": 4}],
        overrideTalents=[{"prefabKey": "talent_a", "blackboard": []}])
    root = corpus(tmp_path, {"a.json": level([item, item])})
    result = audit.audit_corpus(root)
    observations = result["contextObservations"]["tokenInsts"]
    assert observations["recordCounts"] == {
        "hidden": 2, "nonemptyOverrideSkillBlackboard": 2, "nonemptyOverrideTalents": 2}
    assert observations["observedSkillBlackboardKeys"]["future"]["occurrences"] == 4
    assert observations["completeNativeKeyCatalog"] is False
    assert next(iter(result["devices"]["trap_box"]["roles"]["tokenInsts"]["exactSamples"].values()))["data"] == item


def test_shared_level_stage_metadata_does_not_multiply_sources(tmp_path):
    stages = {"normal": {"stageId": "normal", "levelId": "Obt/A", "name": "A"},
              "hard": {"stageId": "hard", "levelId": "Obt/B", "difficulty": "FOUR_STAR"}}
    pairs = {"Obt/B": {"levelId": "Obt/A", "sceneId": "Wrong"},
             "Obt/A": {"levelId": "Obt/C"}}
    root = corpus(tmp_path, {"obt/a.json": level([record()]), "obt/c.json": level()},
                  stages=stages, pairs=pairs)
    result = audit.audit_corpus(root)
    # One mapping step only: normal resolves C, hard resolves A.
    assert [x["stageId"] for x in result["levels"]["obt/a.json"]["stages"]] == ["hard"]
    assert result["devices"]["trap_box"]["roles"]["tokenInsts"]["levels"] == ["obt/a.json"]


def test_stage_without_body_is_distinct_from_missing_body(tmp_path):
    root = corpus(tmp_path, {"a.json": level()}, stages={
        "story": {"levelId": None}, "missing": {"levelId": "Absent"}})
    counts = audit.audit_corpus(root)["summary"]["stageMetadata"]
    assert counts["stageEntriesWithoutBodyId"] == 1
    assert counts["unresolvedStageEntries"] == 1


def test_missing_null_empty_sections_are_distinct(tmp_path):
    root = corpus(tmp_path, {"a.json": level(predefines=None),
                            "b.json": level(predefines={}), "c.json": level()})
    result = audit.audit_corpus(root)
    assert result["levels"]["a.json"]["containerStates"]["predefines"] == "null"
    assert result["levels"]["b.json"]["sectionStates"]["predefines/tokenInsts"] == "missing"
    assert result["levels"]["c.json"]["sectionStates"]["predefines/tokenInsts"] == "empty"
    assert result["levels"]["a.json"]["containerStates"]["hardPredefines"] == "missing"


@pytest.mark.parametrize("bad", [None, 2, {}, {"inst": {}}, {"inst": {"characterKey": ""}}])
def test_bad_record_is_reported_with_pointer_and_not_lost(tmp_path, bad):
    root = corpus(tmp_path, {"a.json": level([bad, record()])})
    result = audit.audit_corpus(root)
    assert result["summary"]["validRecords"] == 1
    assert result["summary"]["invalidRecords"] == 1
    assert result["issues"][0]["pointer"] == "/predefines/tokenInsts/0"


def test_malformed_section_and_nonlevel_json_are_accounted_for(tmp_path):
    root = corpus(tmp_path, {"a.json": level(predefines={"tokenInsts": "bad"}),
                            "metadata.json": {"metadata": 1}})
    result = audit.audit_corpus(root)
    assert result["summary"]["levelCount"] == 1
    assert result["nonLevels"] == ["metadata.json"]
    assert result["issues"][0]["pointer"] == "/predefines/tokenInsts"


def test_invalid_json_cannot_be_silently_counted_as_nonlevel(tmp_path):
    root = corpus(tmp_path, {"a.json": level()})
    (root / "levels/bad.json").write_text('{"mapData":{},"mapData":{}}', encoding="utf-8")
    result = audit.audit_corpus(root)
    assert len(result["errors"]) == 1
    assert result["summary"]["jsonFiles"] == 2


def test_report_contains_each_sample_and_origin(tmp_path):
    root = corpus(tmp_path, {"a.json": level([record(position={"row": -1, "col": 0}, hidden=True)])})
    result = audit.audit_corpus(root)
    report = audit.render_report(result)
    assert "trap_box" in report
    assert "a.json" in report
    assert "predefines/tokenInsts/0" in report
    assert "position" in report
    assert result["summary"]["runtimeValidated"] is False


def test_output_refuses_source_tree_and_existing_directory(tmp_path):
    root = corpus(tmp_path, {"a.json": level()})
    with pytest.raises(ValueError, match="outside"):
        audit.validate_output(root, root / "reports")
    with pytest.raises(ValueError, match="exist"):
        audit.validate_output(root, root)
    audit.validate_output(root, tmp_path / "new-evidence")


def test_independent_verifier_roundtrips_every_original_record(tmp_path):
    root = corpus(tmp_path, {"a.json": level([record(), record()], [record(initialCnt=2)]),
                            "b.json": level([record("token_tower")])})
    result = audit.audit_corpus(root)
    checked = verify.verify_result(root, result)
    assert checked["errors"] == []
    assert checked["verifiedRecords"] == 4
    assert checked["verifiedSourceHashes"] == 5


@pytest.mark.parametrize("target", ["sample", "pointer", "source", "summary", "projection"])
def test_independent_verifier_detects_changed_or_missing_evidence(tmp_path, target):
    root = corpus(tmp_path, {"a.json": level([record()])})
    result = audit.audit_corpus(root)
    role = result["devices"]["trap_box"]["roles"]["tokenInsts"]
    sample = next(iter(role["exactSamples"].values()))
    if target == "sample":
        sample["data"]["future"] = True
    elif target == "pointer":
        sample["references"][0]["pointer"] = "/predefines/tokenInsts/9"
    elif target == "source":
        write_json(root / "levels/a.json", level([record("changed")]))
    elif target == "summary":
        result["summary"]["validRecords"] += 1
    else:
        role["configurations"][0]["data"]["future"] = True
    assert verify.verify_result(root, result)["errors"]


def test_verifier_rejects_cross_role_reclassification(tmp_path):
    root = corpus(tmp_path, {"a.json": level([record()])})
    result = audit.audit_corpus(root)
    roles = result["devices"]["trap_box"]["roles"]
    roles["tokenCards"] = roles.pop("tokenInsts")
    assert any("record-role-mismatch" in error for error in verify.verify_result(root, result)["errors"])


def test_cli_publishes_new_evidence_without_changing_source(tmp_path, monkeypatch, capsys):
    root = corpus(tmp_path, {"a.json": level([record()])})
    before = {p.relative_to(root): p.read_bytes() for p in root.rglob("*.json")}
    output = tmp_path / "evidence"
    monkeypatch.setattr(audit, "git_identity", lambda _: {"revision": "fixed", "status": ""})
    monkeypatch.setattr("sys.argv", ["audit", "--gamedata", str(root), "--output", str(output)])
    assert audit.main() == 0
    assert len(json.loads((output / "manifest.json").read_bytes())) == 3
    assert (output / "CONFIGURATION_INDEX.md").is_file()
    assert before == {p.relative_to(root): p.read_bytes() for p in root.rglob("*.json")}
    assert json.loads(capsys.readouterr().out)["deviceIds"] == 1
