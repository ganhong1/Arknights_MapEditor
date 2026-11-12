import copy

from tools import build_device_library as build


def test_two_roles_unknown_records_and_legacy_entries_are_preserved():
    record = {"inst": {"characterKey": "token_tower"}, "future": [2, 1], "alias": "original"}
    role = {"configurations": [{"id": "config", "data": {"future": [2, 1]}, "exactSampleIds": ["raw"]}],
            "exactSamples": {"raw": {"data": record, "references": [
                {"level": "a.json", "pointer": "/hardPredefines/tokenCards/3"}]}}, "levels": ["a.json"]}
    evidence = {"levels": {"a.json": {}}, "devices": {"token_tower": {"roles": {"tokenCards": role}}},
                "source": {"gitBefore": {"revision": "fixed"}}}
    before = copy.deepcopy(evidence)
    index, files = build.build_library(evidence, {"trap_absent": {"name": " Absent "}},
                                      {"historicalOnly": [{"id": "trap_old", "name": "Old"}]})
    assert len(index["entries"]) == 3
    assert len(files) == 1
    packaged = files["token_tower.json"]["roles"]["tokenCards"]
    assert packaged["samples"][0]["originals"][0]["data"] == record
    assert packaged["samples"][0]["originals"][0]["references"] == [[0, "hardPredefines", 3]]
    assert next(row for row in index["entries"] if row["id"] == "trap_absent")["name"] == "Absent"
    assert evidence == before


def test_recommendations_are_distinct_existing_configs_and_cap_at_three():
    role = {"configurations": [], "exactSamples": {}}
    for i in range(5):
        key = str(i)
        role["configurations"].append({"id": key, "data": {}, "exactSampleIds": [key]})
        role["exactSamples"][key] = {"data": {"inst": {}, "initialCnt": i},
                                     "references": [{"level": "a.json", "pointer": "/predefines/tokenCards/0"}]}
    result = build.build_role(role, {"a.json": 0}, "tokenCards")
    assert len(set(result["recommended"])) == 3
    assert all(0 <= index < len(result["samples"]) for index in result["recommended"])

def test_only_placement_is_excluded_and_originals_keep_their_values():
    base = {"inst": {"characterKey": "trap_a"}, "hidden": False, "alias": "x",
            "position": {"row": 1, "col": 2}, "direction": "UP", "future": [2, 1]}
    moved = {**base, "position": {"row": 4, "col": 5}, "direction": "LEFT"}
    hidden = {**base, "hidden": True}
    alias = {**base, "alias": "y"}
    role = {"exactSamples": {str(i): {"data": record, "references": [
        {"level": path, "pointer": f"/predefines/tokenInsts/{i}"}]}
        for i, (record, path) in enumerate([(base, "a"), (moved, "b"), (hidden, "a"), (alias, "a")])}}
    before = copy.deepcopy(role)
    result = build.build_role(role, {"a": 0, "b": 1}, "tokenInsts")
    assert len(result["samples"]) == 3
    merged = next(s for s in result["samples"] if len(s["originals"]) == 2)
    assert "position" not in merged["data"] and "direction" not in merged["data"]
    assert merged["data"]["future"] == [2, 1]
    assert {o["data"]["direction"] for o in merged["originals"]} == {"UP", "LEFT"}
    assert {ref[0] for o in merged["originals"] for ref in o["references"]} == {0, 1}
    assert len(set(result["recommended"])) == 3
    assert role == before


def test_cards_and_nonplacement_state_remain_distinct():
    records = [
        {"inst": {}, "initialCnt": 1},
        {"inst": {}, "initialCnt": 2},
        {"inst": {}, "initialCnt": 1, "alias": None},
        {"inst": {}, "initialCnt": 1, "overrideSkillBlackboard": []},
        {"inst": {}, "initialCnt": 1, "overrideSkillBlackboard": None},
    ]
    role = {"exactSamples": {str(i): {"data": record, "references": [
        {"level": "a", "pointer": f"/predefines/tokenCards/{i}"}]} for i, record in enumerate(records)}}
    result = build.build_role(role, {"a": 0}, "tokenCards")
    assert len(result["samples"]) == len(records)
    for sample in result["samples"]:
        assert sample["data"] == sample["originals"][0]["data"]
