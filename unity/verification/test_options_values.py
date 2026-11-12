"""Contract tests for the offline Options corpus, including damaged references."""

import copy
import importlib.util
import json
import sys
from decimal import Decimal
from pathlib import Path

import pytest


SPEC = importlib.util.spec_from_file_location(
    "build_options_values", Path(__file__).with_name("build-options-values.py")
)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


def write_level(root: Path, name: str, options: object = ...) -> None:
    value = {"levelId": "sample", "mapData": {"map": [[0]], "tiles": [{}]}}
    if options is not ...:
        value["options"] = options
    (root / name).write_text(json.dumps(value), encoding="utf-8")


@pytest.mark.parametrize("raw", ['{"x":1,"x":2}', '{"x":NaN}', '{"x":Infinity}'])
def test_strict_json_rejects_ambiguous_data(raw: str) -> None:
    with pytest.raises(ValueError):
        MODULE.strict_loads(raw)


def test_types_and_decimal_precision_are_not_collapsed() -> None:
    values = MODULE.strict_loads('[true,1,1.0,"1",0.1234567890123456789]')
    assert len({MODULE.canonical(v) for v in values[:4]}) == 4
    assert values[-1] == Decimal("0.1234567890123456789")
    assert MODULE.strict_loads(MODULE.dumps_json(values)) == values
    assert isinstance(MODULE.strict_loads(MODULE.dumps_json(Decimal("1e0"))), Decimal)
    assert MODULE.canonical(Decimal("1.00")) == MODULE.canonical(Decimal("1.0"))


def test_duplicate_documents_keep_paths_but_do_not_inflate_frequency(
    tmp_path: Path,
) -> None:
    write_level(tmp_path, "a.json", {"maxCost": 99, "future": 1})
    write_level(tmp_path, "b.json", {"maxCost": 99, "future": 1})
    write_level(tmp_path, "c.json", {"maxCost": 500})
    result = MODULE.build_catalog(tmp_path, "test")
    assert result["corpus"]["levelCount"] == 3
    assert result["corpus"]["uniqueDocumentCount"] == 2
    row = next(r for r in result["fields"]["maxCost"]["samples"] if r["value"] == 99)
    assert row["count"] == 1
    assert len(row["sources"]) == 2
    assert result["fields"]["future"]["recommended"] == []
    MODULE.verify_catalog(result, tmp_path)


def test_missing_null_empty_and_non_object_options_are_distinct(tmp_path: Path) -> None:
    for name, value in [
        ("a.json", ...),
        ("b.json", None),
        ("c.json", {}),
        ("d.json", {"configBlackBoard": None}),
        ("e.json", {"configBlackBoard": []}),
        ("f.json", 12),
    ]:
        write_level(tmp_path, name, value)
    result = MODULE.build_catalog(tmp_path, "test")
    field = result["fields"]["configBlackBoard"]
    assert len(field["missingSources"]) == 4
    assert {MODULE.canonical(r["value"]) for r in field["samples"]} == {"null", "[]"}
    assert {v["state"] for v in result["options"]} == {
        "object",
        "missing",
        "null",
        "other",
    }
    assert [field["samples"][i]["value"] for i in field["recommended"]] == [[]]
    MODULE.verify_catalog(result, tmp_path)


def test_blackboard_keeps_channel_case_extensions_and_duplicate_indices(
    tmp_path: Path,
) -> None:
    first = {"key": "Flag", "value": 1.0, "valueStr": "", "future": True}
    other = {"key": "flag", "value": 1.0, "valueStr": "text"}
    write_level(tmp_path, "a.json", {"configBlackBoard": [first, first, other]})
    result = MODULE.build_catalog(tmp_path, "test")
    assert set(result["blackboard"]) == {"Flag", "flag"}
    sample = result["blackboard"]["Flag"]["samples"][0]
    assert sample["references"] == [[0, 0], [0, 1]]
    assert sample["sources"] == [0]
    assert sample["count"] == 1
    assert sample["value"]["future"] is True
    MODULE.verify_catalog(result, tmp_path)


def test_full_verifier_rejects_wrong_values_counts_and_missing_sources(
    tmp_path: Path,
) -> None:
    write_level(tmp_path, "a.json", {"maxCost": 99})
    result = MODULE.build_catalog(tmp_path, "test")
    for mutate in [
        lambda d: d["fields"]["maxCost"]["samples"][0].update(count=2),
        lambda d: d["fields"]["maxCost"]["samples"][0].update(value=100),
        lambda d: d["fields"]["maxCost"]["samples"][0].update(sources=[]),
    ]:
        bad = copy.deepcopy(result)
        mutate(bad)
        with pytest.raises(ValueError):
            MODULE.verify_catalog(bad, tmp_path)


def test_non_level_files_are_explicitly_excluded_and_invalid_json_fails(
    tmp_path: Path,
) -> None:
    write_level(tmp_path, "a.json", {"maxCost": 99})
    (tmp_path / "metadata.json").write_text('{"type":"metadata"}', encoding="utf-8")
    result = MODULE.build_catalog(tmp_path, "test")
    assert result["corpus"]["scannedJsonFiles"] == 2
    assert result["corpus"]["excluded"] == [
        {
            "path": "metadata.json",
            "reason": "no LevelData discriminator (options/mapData/levelId)",
        }
    ]
    (tmp_path / "broken.json").write_text('{"options":', encoding="utf-8")
    with pytest.raises(ValueError, match="broken.json"):
        MODULE.build_catalog(tmp_path, "test")


def test_supplemental_values_keep_provenance_without_primary_recommendations(
    tmp_path: Path,
) -> None:
    primary, cache, resource = (
        tmp_path / name for name in ("primary", "cache", "resource")
    )
    for root in (primary, cache, resource):
        root.mkdir()
    write_level(primary, "same.json", {"maxCost": 99})
    write_level(cache, "same.json", {"maxCost": 999})
    write_level(cache, "copy.json", {"maxCost": 999})
    write_level(resource, "same.json", {"maxCost": 500})
    result = MODULE.build_catalog(primary, "test", cache, resource, "resource-revision")
    group = result["fields"]["maxCost"]
    assert {row["value"] for row in group["samples"]} == {99, 500, 999}
    assert [group["samples"][i]["value"] for i in group["recommended"]] == [99]
    cached = next(row for row in group["samples"] if row["value"] == 999)
    assert cached["count"] == 1 and cached["primaryCount"] == 0
    assert len(cached["sources"]) == 2
    assert len({(row["source"], row["path"]) for row in result["levels"]}) == 4
    MODULE.verify_catalog(result, primary, cache, resource)


def test_repository_revision_is_not_used_for_untracked_bodies(
    tmp_path: Path, monkeypatch
) -> None:
    write_level(tmp_path, "a.json", {"maxCost": 99})

    def git_output(command, text=False):
        if "rev-parse" in command:
            return "repository-head\n"
        if "status" in command:
            return ""
        return b""

    monkeypatch.setattr(MODULE.subprocess, "check_output", git_output)
    info = MODULE.git_attestation(tmp_path, required=False)
    assert info["revision"] is None
    assert info["observedRepositoryRevision"] == "repository-head"
    assert info["untrackedJsonFiles"] == ["a.json"]
    with pytest.raises(ValueError):
        MODULE.git_attestation(tmp_path, required=True)


def test_command_generates_and_rereads_all_three_sources(
    tmp_path: Path, monkeypatch
) -> None:
    primary, cache, resource = (
        tmp_path / name for name in ("primary", "cache", "resource")
    )
    for root in (primary, cache, resource):
        root.mkdir()
        write_level(root, "a.json", {"maxCost": 99})
    legacy = tmp_path / "old.json"
    legacy.write_text(
        json.dumps(
            {
                "levelCount": 1,
                "fields": {"maxCost": [{"value": 99, "count": 1}]},
                "blackboardKeys": {},
            }
        ),
        encoding="utf-8",
    )

    def git_output(command, text=False):
        source = Path(command[2])
        if "rev-parse" in command:
            return source.name + "-head\n"
        if "status" in command:
            return ""
        return b"a.json\0"

    output, audit = tmp_path / "values.json", tmp_path / "audit.json"
    monkeypatch.setattr(MODULE.subprocess, "check_output", git_output)
    monkeypatch.setattr(
        sys,
        "argv",
        [
            "builder",
            "--levels",
            str(primary),
            "--supplement-levels",
            str(cache),
            "--resource-levels",
            str(resource),
            "--output",
            str(output),
            "--audit",
            str(audit),
            "--legacy",
            str(legacy),
        ],
    )
    MODULE.main()
    result, report = (
        MODULE.strict_loads(output.read_bytes()),
        MODULE.strict_loads(audit.read_bytes()),
    )
    assert report["verification"]["levelsReread"] == 3
    assert report["legacyComparison"]["differenceCount"] == 0
    assert report["outputSha256"] == MODULE.digest(output.read_bytes())
    assert result["sources"][0]["revisionBoundToBodies"] is True
    assert result["sources"][1]["revision"] is None
    assert result["sources"][2]["revision"] == "resource-head"


def test_malformed_blackboard_entries_are_audited_and_not_invented(
    tmp_path: Path,
) -> None:
    write_level(
        tmp_path, "a.json", {"configBlackBoard": [None, {"value": 4}, {"key": 2}]}
    )
    write_level(tmp_path, "b.json", {"configBlackBoard": "legacy-invalid"})
    result = MODULE.build_catalog(tmp_path, "test")
    assert len(result["corpus"]["blackboardAnomalies"]) == 4
    assert result["blackboard"] == {}
    MODULE.verify_catalog(result, tmp_path)


def test_verifier_rejects_newly_added_source_files(tmp_path: Path) -> None:
    write_level(tmp_path, "a.json", {"maxCost": 99})
    result = MODULE.build_catalog(tmp_path, "test")
    write_level(tmp_path, "b.json", {"maxCost": 500})
    with pytest.raises(ValueError, match="inventory"):
        MODULE.verify_catalog(result, tmp_path)
