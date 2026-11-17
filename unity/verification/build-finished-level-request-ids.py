"""Build request levelId bindings for the pinned finished-levels archive."""

import argparse
import json
from pathlib import Path
from zipfile import ZipFile


def read_json(path: Path) -> dict:
    return json.loads(path.read_bytes())


def resource_key(path: str) -> str:
    return path.lower().removesuffix(".json")


def request_ids(game_data: Path) -> dict[tuple[str, str], str]:
    excel = game_data / "excel"
    pairs = read_json(game_data / "battle/battle_misc_table.json")["levelScenePairs"]
    tables = [
        read_json(excel / "stage_table.json")["stages"],
        read_json(excel / "roguelike_table.json")["stages"],
        *(
            topic["stages"]
            for topic in read_json(excel / "roguelike_topic_table.json")["details"].values()
        ),
    ]
    result = {}
    for table in tables:
        for stage, entry in table.items():
            level_id = entry.get("levelId")
            if not level_id:
                continue
            # Match the requested resource itself or the body mapped by ResourceRouter.
            mapped = pairs.get(level_id, {}).get("levelId")
            for path in (level_id, mapped):
                if path:
                    key = (stage, resource_key(path))
                    if result.setdefault(key, level_id) != level_id:
                        raise ValueError(f"Conflicting request IDs for {key}")
    return result


def build(archive: Path, source_root: Path, output: Path) -> None:
    with ZipFile(archive) as source:
        index = json.loads(source.read("library-index.json"))
    data = request_ids(source_root / "ArknightsGameData/zh_CN/gamedata")
    resource = request_ids(source_root / "ArknightsGameResource/gamedata")
    bindings: dict[str, dict[str, str]] = {}
    count = 0
    for source in index["sources"]:
        path = source["path"]
        table = resource if source["source"] == "ArknightsGameResource" else data
        for stage in source["stages"]:
            if level_id := table.get((stage, resource_key(path))):
                # Keep source versions separate if the same association was renamed.
                versions = bindings.setdefault(stage, {})
                versions[source["source"] + "/" + path] = level_id
                count += 1
    output.write_text(
        json.dumps(bindings, ensure_ascii=False, sort_keys=True, separators=(",", ":")) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    print(f"{count} source bindings for {len(bindings)} stage IDs")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=Path)
    parser.add_argument("source_root", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    build(args.archive, args.source_root, args.output)
