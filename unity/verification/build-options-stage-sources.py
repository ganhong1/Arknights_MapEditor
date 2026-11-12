"""Index the stage entries that reference each file in the Options catalog."""

import argparse
import hashlib
import json
import subprocess
import sys
from collections import defaultdict
from collections.abc import Iterator
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools"))
from audit_level_modes import level_resource  # noqa: E402

PRIMARY = "ArknightsGameData"
RESOURCE = "ArknightsGameResource"
CACHE = "PRTSMap-cache"


def read_table(repo: Path, revision: str, path: str) -> dict:
    return json.loads(
        subprocess.check_output(["git", "-C", str(repo), "show", f"{revision}:{path}"])
    )


def stage_rows(repo: Path, revision: str, prefix: str) -> Iterator[tuple[str, dict]]:
    def table(name: str) -> dict:
        return read_table(repo, revision, f"{prefix}/excel/{name}_table.json")

    yield from table("stage")["stages"].items()
    yield from table("roguelike")["stages"].items()
    for topic in table("roguelike_topic")["details"].values():
        yield from topic.get("stages", {}).items()
    for stages in table("sandbox_perm")["detail"].values():
        for season in stages.values():
            yield from season.get("stageData", {}).items()
    for activity in table("sandbox")["sandboxActTables"].values():
        yield from activity.get("stageDatas", {}).items()
    yield from table("climb_tower")["levels"].items()
    for stage in table("handbook_info")["handbookStageData"].values():
        yield stage["stageId"], stage
    for season in table("crisis_v2")["recalRuneData"]["seasons"].values():
        yield from season.get("stages", {}).items()


def build(catalog_bytes: bytes, source_root: Path) -> dict:
    catalog = json.loads(catalog_bytes)
    revisions = {source["id"]: source["revision"] for source in catalog["sources"]}
    by_path: dict[str, dict[str, set[str]]] = {}
    metadata: dict[str, list[str]] = {}
    for kind, prefix in ((PRIMARY, "zh_CN/gamedata"), (RESOURCE, "gamedata")):
        repo = source_root / kind
        revision = revisions[kind]
        pairs = read_table(repo, revision, f"{prefix}/battle/battle_misc_table.json")[
            "levelScenePairs"
        ]
        reverse: dict[str, set[str]] = defaultdict(set)
        for stage_id, stage in stage_rows(repo, revision, prefix):
            path = level_resource(stage, pairs)
            if path is None:
                continue
            reverse[path].add(stage_id)
            for replacement in stage.get("levelReplaceIds") or ():
                replacement_path = level_resource({"levelId": replacement}, pairs)
                if replacement_path is not None:
                    reverse[replacement_path].add(stage_id)
            metadata.setdefault(
                stage_id,
                [
                    stage_id,
                    stage.get("code") or stage.get("levelCode") or "",
                    stage.get("name") or stage.get("levelName") or "",
                    stage.get("difficulty") or "",
                ],
            )
        by_path[kind] = reverse

    levels = catalog["levels"]
    by_body: dict[tuple[str, str], set[str]] = defaultdict(set)
    for source in levels:
        kind = source["source"]
        if kind in by_path:
            path = source["path"].lower()
            by_body[(path, source["documentId"])].update(by_path[kind].get(path, ()))

    stage_ids = sorted(metadata)
    index = {stage_id: i for i, stage_id in enumerate(stage_ids)}
    links = []
    path_matches = []
    for source_id, source in enumerate(levels):
        kind, path = source["source"], source["path"].lower()
        if kind == CACHE:
            matched = by_body.get((path, source["documentId"]), set())
            if not matched:
                primary = by_path[PRIMARY].get(path)
                if primary and primary == by_path[RESOURCE].get(path):
                    matched = primary
                    path_matches.append(source_id)
        else:
            matched = by_path[kind].get(path, set())
        links.append(sorted(index[stage_id] for stage_id in matched))

    return {
        "catalogSha256": hashlib.sha256(catalog_bytes).hexdigest(),
        "stages": [metadata[stage_id] for stage_id in stage_ids],
        "links": links,
        "cachePathMatches": path_matches,
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("catalog", type=Path)
    parser.add_argument("source_root", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    result = build(args.catalog.read_bytes(), args.source_root)
    args.output.write_text(
        json.dumps(result, ensure_ascii=False, separators=(",", ":")) + "\n",
        encoding="utf-8",
    )
    matched = sum(bool(link) for link in result["links"])
    print(f"{matched}/{len(result['links'])} files linked to stage entries")


if __name__ == "__main__":
    main()
