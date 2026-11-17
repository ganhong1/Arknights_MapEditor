"""The action index preserves native keys and scopes opera commands by config."""

import importlib.util
import json
import zipfile
from pathlib import Path

import pytest

SPEC = importlib.util.spec_from_file_location(
    "action_builder", Path(__file__).parents[1] / "build_action_resources.py"
)
builder = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(builder)


def make_archive(tmp_path: Path, documents: list[dict]) -> Path:
    path = tmp_path / "levels.zip"
    with zipfile.ZipFile(path, "w") as bundle:
        sources = []
        for index, document in enumerate(documents):
            body = str(index)
            bundle.writestr(f"levels/{body}.json", json.dumps(document))
            sources.append({"body": body, "source": "fixture", "revision": None,
                            "path": f"level_{index}.json", "stages": ["test"]})
        bundle.writestr("library-index.json", json.dumps({"sources": sources,
            "stages": [{"id": "test", "label": None}]}))
    return path


def document(config: str, actions: list[dict]) -> dict:
    return {"operaConfig": config, "waves": [{"fragments": [{"actions": actions}]}]}


def test_opera_scopes_same_command_and_keeps_occurrences(tmp_path: Path) -> None:
    archive = make_archive(tmp_path, [document("a", [{"actionType": "PLAY_OPERA", "key": "init"}] * 4),
                                     document("b", [{"actionType": 7, "key": "init"}])])
    result = builder.build_index(archive, {}, {"opera": {"a": {"init": True}}})
    a, b = result["entries"]
    assert a["operaConfig"] == "a" and b["operaConfig"] == "b"
    assert a["verified"] is True and b["verified"] is False
    assert a["occurrences"] == 4 and len(a["examples"]) == 3
    assert a["examples"][0]["revision"] == ""


def test_branch_and_main_samples_share_identity_but_keep_exact_key(tmp_path: Path) -> None:
    key = "Obt\\Tutorial\\Training\\a.txt"
    doc = document("", [{"actionType": "STORY", "key": key}])
    doc["branches"] = {"a/b": {"phases": [{"actions": [{"actionType": 2, "key": key}]}]}}
    archive = make_archive(tmp_path, [doc])
    resolution = {"storyExtensionCandidates": {key: "D:/root/source/Resource/story/a.txt"}}
    entry = builder.build_index(archive, {}, resolution)["entries"][0]
    assert entry["key"] == key and entry["occurrences"] == 2
    assert entry["verified"] is True
    assert entry["examples"][1]["actionPath"] == 'branches["a/b"].phases[0].actions[0]'
    assert entry["resources"] == ["Resource/story/a.txt"]


def test_unresolved_audio_and_event_are_not_claimed_resource_matches() -> None:
    resolution = {"audio": {"missing": {"path": ["battle.ON_CUSTOM_TRIGGER.missing"], "target": None},
                             "alias": {"path": ["custom", "ready"], "target": {"loop": "bank"}}}}
    assert builder.match_resource("PLAY_BGM", "", "missing", {}, resolution)[0] is False
    assert builder.match_resource("PLAY_BGM", "", "alias", {}, resolution)[0] is True
    assert builder.match_resource("BATTLE_EVENTS", "", "WaitBase", {}, resolution)[0] is False
    assert builder.match_resource("TUTORIAL", "", "unknown", {}, {})[0] is False


def test_nonresource_and_invalid_keys_are_excluded(tmp_path: Path) -> None:
    archive = make_archive(tmp_path, [document("", [
        {"actionType": "SPAWN", "key": "enemy"}, {"actionType": "DIALOG", "key": None},
        {"actionType": [], "key": "invalid"}, {"actionType": True, "key": "invalid"},
        {"actionType": "DIALOG", "key": "dialog"},
    ])])
    entries = builder.build_index(archive, {"DIALOG": {"paths": {"dialog": "Resource/dialog.txt"}}}, {})["entries"]
    assert len(entries) == 1
    assert entries[0]["verified"] is True


def test_pinned_input_rejects_version_drift(tmp_path: Path) -> None:
    path = tmp_path / "input.json"
    path.write_text("{}", encoding="utf-8")
    with pytest.raises(ValueError, match="frozen input hash"):
        builder.pinned(path, "0" * 64)
