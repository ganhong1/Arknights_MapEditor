"""Validate documentation contracts and local links without network access."""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path
from typing import Any
from urllib.parse import unquote, urlsplit

from jsonschema import Draft202012Validator, FormatChecker

ROOT = Path(__file__).resolve().parents[2]


def load(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def validate_relative_path(value: str) -> None:
    if (
        not value.strip()
        or value.startswith("/")
        or any(c in value for c in ':\\\0*?"<>|')
    ):
        raise ValueError("Resource path must be relative and use forward slashes")
    for part in value.split("/"):
        stem = part.split(".")[0].upper()
        if (
            not part
            or part in (".", "..")
            or part.endswith((" ", "."))
            or any(ord(c) < 32 or 127 <= ord(c) <= 159 for c in part)
        ):
            raise ValueError("Ambiguous resource path segment")
        if stem in {"CON", "PRN", "AUX", "NUL"} or re.fullmatch(
            r"(?:COM|LPT)[1-9]", stem
        ):
            raise ValueError("Reserved resource path segment")


def validate_example(kind: str, value: Any, docs: Path) -> None:
    schema = load(docs / "schemas" / f"{kind}.schema.json")
    Draft202012Validator.check_schema(schema)
    Draft202012Validator(schema, format_checker=FormatChecker()).validate(value)
    if kind == "leveldata":
        rows, tiles = value["mapData"]["map"], value["mapData"]["tiles"]
        for row in rows:
            if len(row) != len(rows[0]):
                raise ValueError("Map must be rectangular")
            for index in row:
                if (
                    type(index) is not int
                    or index >= len(tiles)
                    or not isinstance(tiles[index], dict)
                ):
                    raise ValueError("Map must reference an existing tile object")
    elif kind == "arkmap":
        if type(value["version"]) is not int:
            raise ValueError("Project version must be an integer token")
        validate_relative_path(value["levelDataPath"])
        keys = set()
        for item in value["artMappings"]:
            validate_relative_path(item["resourcePath"])
            if item["key"] in keys:
                raise ValueError("Art mapping keys must be unique")
            keys.add(item["key"])


def validate_openapi(value: Any) -> None:
    if value.get("openapi") != "3.2.1" or value.get("paths") != {}:
        raise ValueError(
            "Current HTTP contract must use OpenAPI 3.2.1 with no callable paths"
        )
    if not all(
        value.get("info", {}).get(key) for key in ("title", "version", "description")
    ):
        raise ValueError("OpenAPI info requires title, version and description")
    if value.get("servers"):
        raise ValueError("No HTTP service is implemented")


def validate_links(docs: Path) -> int:
    count = 0
    for path in docs.rglob("*.md"):
        for target in re.findall(
            r"\[[^\]]*\]\(([^)]+)\)", path.read_text(encoding="utf-8")
        ):
            parsed = urlsplit(target)
            if parsed.scheme or not parsed.path:
                continue
            resolved = (path.parent / unquote(parsed.path)).resolve()
            if not resolved.is_file():
                raise ValueError(
                    f"Missing local link: {path.relative_to(docs)} -> {target}"
                )
            count += 1
    return count


def validate(docs: Path) -> dict[str, int]:
    examples = list((docs / "schemas" / "examples").glob("*.json"))
    for path in (docs / "schemas").glob("*.schema.json"):
        Draft202012Validator.check_schema(load(path))
    for path in examples:
        validate_example(path.stem, load(path), docs)
    validate_openapi(load(docs / "http" / "openapi.json"))
    return {
        "examples": len(examples),
        "localLinks": validate_links(docs),
        "httpEndpoints": 0,
    }


def validate_log(path: Path, docs: Path) -> dict[str, int]:
    """Validate new events while reporting legacy records separately."""
    checked = legacy = 0
    schema = load(docs / "schemas/diagnostic-event.schema.json")
    validator = Draft202012Validator(schema, format_checker=FormatChecker())
    with path.open(encoding="utf-8-sig") as source:
        while line := source.readline(1024 * 1024):
            if len(line) >= 1024 * 1024:
                raise ValueError("Diagnostic log line exceeds 1 MiB")
            if not line.strip():
                continue
            event = json.loads(line)
            if "schemaVersion" not in event:
                legacy += 1
                continue
            validator.validate(event)
            checked += 1
    return {"validatedEvents": checked, "legacyEvents": legacy}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--docs", type=Path, default=ROOT / "docs")
    parser.add_argument("--diagnostic-log", type=Path)
    args = parser.parse_args()
    result = validate(args.docs)
    if args.diagnostic_log:
        result.update(validate_log(args.diagnostic_log, args.docs))
    print(json.dumps(result, ensure_ascii=False))
