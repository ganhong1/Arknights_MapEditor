"""Build offline ActionData resource candidates from the frozen level archive."""

from __future__ import annotations

import argparse
import hashlib
import json
import zipfile
from collections import defaultdict
from pathlib import Path
from typing import Any, Iterator

ARCHIVE_SHA256 = "a3e15a7e7a526f661fa34d42fc861856ec35083106781125fa566bd587392e45"
LOCAL_SHA256 = "63823cf1cf7c3fad2260e936291e86afd5f4d5bee4997c70883a1c54860155a5"
RESOLUTION_SHA256 = "1f97e173d866dd96e1c26e0b56d4bb4ae432943d20ddd56a1b7b7111558fca00"
ACTION_NAMES = (
    "SPAWN", "PREVIEW_CURSOR", "STORY", "TUTORIAL", "PLAY_BGM",
    "DISPLAY_ENEMY_INFO", "ACTIVATE_PREDEFINED", "PLAY_OPERA",
    "TRIGGER_PREDEFINED", "BATTLE_EVENTS", "WITHDRAW_PREDEFINED", "DIALOG",
    "SHOW_ALL_HIDDEN_CARDS", "EMPTY", "E_NUM",
)
RESOURCE_TYPES = {"STORY", "TUTORIAL", "DIALOG", "PLAY_BGM", "PLAY_OPERA", "BATTLE_EVENTS"}


def pinned(path: Path, expected: str) -> bytes:
    payload = path.read_bytes()
    if hashlib.sha256(payload).hexdigest() != expected:
        raise ValueError(f"Unexpected frozen input hash: {path.name}")
    return payload


def actions(document: dict[str, Any]) -> Iterator[tuple[str, dict[str, Any]]]:
    """Walk only native action collections, preserving original ordinals."""
    for wi, wave in enumerate(document.get("waves") or []):
        if not isinstance(wave, dict):
            continue
        for fi, fragment in enumerate(wave.get("fragments") or []):
            if not isinstance(fragment, dict):
                continue
            for ai, action in enumerate(fragment.get("actions") or []):
                if isinstance(action, dict):
                    yield f"waves[{wi}].fragments[{fi}].actions[{ai}]", action
    branches = document.get("branches")
    if not isinstance(branches, dict):
        return
    for branch, record in branches.items():
        if not isinstance(record, dict):
            continue
        for pi, phase in enumerate(record.get("phases") or []):
            if not isinstance(phase, dict):
                continue
            for ai, action in enumerate(phase.get("actions") or []):
                if isinstance(action, dict):
                    key = json.dumps(branch, ensure_ascii=False)
                    yield f"branches[{key}].phases[{pi}].actions[{ai}]", action


def resource_path(path: str) -> str:
    normalized = path.replace("\\", "/")
    marker = "/source/"
    return normalized.split(marker, 1)[-1] if marker in normalized else normalized


def match_resource(
    action_type: str, config: str, key: str, local: dict[str, Any], resolution: dict[str, Any]
) -> tuple[bool, str, list[str]]:
    if action_type in {"STORY", "TUTORIAL", "DIALOG"}:
        path = local.get(action_type, {}).get("paths", {}).get(key)
        if path:
            return True, "文本资源已匹配", [resource_path(path)]
        if action_type == "STORY":
            path = resolution.get("storyExtensionCandidates", {}).get(key)
            if path:
                return True, "带扩展名文本资源已匹配", [resource_path(path)]
        return False, "原版样例，资源匹配表未收录", []
    if action_type == "PLAY_BGM":
        record = resolution.get("audio", {}).get(key, {})
        paths = record.get("path", [])
        if record.get("target") is not None:
            return True, "音频事件已匹配" if record.get("direct") else "音频事件别名已匹配", paths
        return False, "原版样例，自定义触发绑定未匹配", paths
    if action_type == "PLAY_OPERA":
        matched = resolution.get("opera", {}).get(config, {}).get(key) is True
        return matched, "演出命令已匹配" if matched else "原版样例，演出命令未匹配", [f"config/leveloperaconfig/{config} / {key}"]
    return False, "原版玩法事件样例", []


def build_index(archive: Path, local: dict[str, Any], resolution: dict[str, Any]) -> dict[str, Any]:
    entries: dict[tuple[str, str, str], dict[str, Any]] = {}
    with zipfile.ZipFile(archive) as bundle:
        index = json.loads(bundle.read("library-index.json"))
        sources: dict[str, list[dict[str, Any]]] = defaultdict(list)
        for source in index["sources"]:
            sources[source["body"]].append(source)
        stages = {stage["id"]: stage for stage in index["stages"]}
        for name in sorted(bundle.namelist()):
            if not name.startswith("levels/") or not name.endswith(".json"):
                continue
            body = Path(name).stem
            document = json.loads(bundle.read(name))
            for path, action in actions(document):
                action_type = action.get("actionType")
                if type(action_type) is int and 0 <= action_type < len(ACTION_NAMES):
                    action_type = ACTION_NAMES[action_type]
                key = action.get("key")
                if not isinstance(action_type, str) or action_type not in RESOURCE_TYPES or not isinstance(key, str) or not key:
                    continue
                config = document.get("operaConfig") if action_type == "PLAY_OPERA" else ""
                config = config if isinstance(config, str) else ""
                identity = (action_type, config, key)
                if identity not in entries:
                    verified, status, resources = match_resource(action_type, config, key, local, resolution)
                    entries[identity] = {
                        "type": action_type, "operaConfig": config, "key": key, "label": key,
                        "verified": verified, "matchStatus": status, "resources": resources,
                        "occurrences": 0, "examples": [],
                    }
                entry = entries[identity]
                entry["occurrences"] += 1
                if len(entry["examples"]) < 3:
                    source = sources.get(body, [{}])[0]
                    stage_ids = source.get("stages", [])
                    stage_id = stage_ids[0] if stage_ids else ""
                    stage = stages.get(stage_id, {})
                    labels = [value for value in (stage.get("label") or [])[:2] if value]
                    entry["examples"].append({
                        "body": body, "actionPath": path, "stageId": stage_id,
                        "stageLabel": " · ".join(labels) or stage_id,
                        "source": source.get("source") or "", "revision": source.get("revision") or "",
                        "levelPath": source.get("path", name),
                    })
    return {
        "version": 1,
        "identity": {
            "finished-levels.zip": ARCHIVE_SHA256,
            "local-resource-key-receipt.json": LOCAL_SHA256,
            "action-resource-resolution-receipt.json": RESOLUTION_SHA256,
        },
        "entries": [entries[key] for key in sorted(entries)],
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--archive", type=Path, required=True)
    parser.add_argument("--local-receipt", type=Path, required=True)
    parser.add_argument("--resolution-receipt", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    pinned(args.archive, ARCHIVE_SHA256)
    local = json.loads(pinned(args.local_receipt, LOCAL_SHA256))
    resolution = json.loads(pinned(args.resolution_receipt, RESOLUTION_SHA256))
    result = build_index(args.archive, local, resolution)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")
    print(json.dumps({"entries": len(result["entries"]), "bytes": args.output.stat().st_size}))


if __name__ == "__main__":
    main()
