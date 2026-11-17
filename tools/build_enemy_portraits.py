"""Pack the verified 2.7.81 enemy portrait evidence for the Unity editor."""

from __future__ import annotations

import argparse
import hashlib
import io
import json
from collections import Counter
from dataclasses import dataclass
from pathlib import Path

from PIL import Image

REVISION = "26-10-22-14-02-11-51447808"
PAGE = 2048
SIZE = 80
GUTTER = 2
CELL = SIZE + 2 * GUTTER
COLUMNS = PAGE // CELL
PER_PAGE = COLUMNS * COLUMNS


@dataclass(frozen=True)
class Portrait:
    path: Path
    sha256: str
    kind: str
    asset: str
    source: str
    light_background: bool = False


def read(path: Path) -> dict | list:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def load_sources(evidence: Path) -> tuple[dict, dict, dict, dict]:
    native_root = evidence / "enemy-native-icons-2781"
    followup = evidence / "enemy-asset-followup-2781"
    native = read(native_root / "native-icon-mapping.json")
    alternate = read(followup / "decoded-assets.json")
    if native["resourceVersion"] != REVISION or alternate["version"] != REVISION:
        raise ValueError("Portrait evidence revision differs from the enemy catalog.")
    exact = {
        row["key"]: Portrait(
            native_root / row["imageFile"], row["pngSha256"], "handbook",
            row["containerPath"], row["bundle"],
        )
        for row in native["entries"]
    }
    alternate_kinds = {"battle": "activity", "boss-hp": "boss", "avatar": "avatar"}
    for bundle in alternate["bundles"]:
        for row in bundle["images"]:
            if row["kind"] in alternate_kinds:
                exact.setdefault(row["id"], Portrait(
                    followup / row["file"], row["sha256"], alternate_kinds[row["kind"]],
                    row["path"], bundle["bundle"],
                ))
    objects = {
        row["name"]: row for row in read(followup / "asset-path-index.json")
        if row.get("name") and row["path"].endswith(".prefab") and "/effects/" in row["path"]
    }
    models = {
        row["id"]: row for row in read(followup / "bake-model-effect/source-receipt.json")["jobs"]
        if row["id"].startswith("enemy_")
    }
    overrides = {}
    for row in read(followup / "derived-preview-mapping.json")["entries"]:
        if row["kind"] != "spine-preview":
            continue
        for prefab in row["prefabs"]:
            image = prefab["images"][0]
            overrides[(row["id"], prefab["prefabKey"])] = Portrait(
                followup / image["file"], image["sha256"], "spine",
                image["skeleton"]["name"], image["bundle"],
            )
    for row in read(followup / "remaining-visual-resolution.json")["entries"]:
        image = row["visuals"][0]
        asset = image["asset"]
        status = row["status"]
        if status == "host-icon":
            picture = related(exact[asset], "summoner")
        elif status == "model-preview":
            model = models[asset]
            picture = Portrait(Path(image["file"]), image["sha256"], "mesh", model["sourcePrefab"], model["bundle"])
        elif status in ("linked-effect-frame", "related-mechanism-effect-frame"):
            effect = objects[asset]
            kind = "mechanism-effect" if status == "related-mechanism-effect-frame" else "effect"
            if row["id"] == "enemy_5601_entlec":
                texture = next(item for item in row["visuals"] if item["kind"] == "effect-texture")
                picture = Portrait(Path(texture["file"]), texture["sha256"], "effect-texture", texture["asset"], effect["bundle"])
            else:
                picture = Portrait(
                    Path(image["file"]), image["sha256"], kind, effect["path"], effect["bundle"],
                    light_background=row["id"] == "enemy_2132_dyfake",
                )
        else:
            raise ValueError(f"Unsupported evidence status for {row['id']}: {status}")
        for key in row["prefabKeys"]:
            overrides[(row["id"], key)] = picture
    baseline = {
        row["id"]: row["nativePrefabCandidates"]
        for row in native["comparison"] if row["baseline"] and not row["nativeExact"]
    }
    historical = {
        row["id"]: row["explicitPrefabKeys"]
        for row in read(evidence / "library-portraits-20261112/historical-prefab-candidates.json")
    }
    return exact, overrides, baseline, historical


def related(picture: Portrait, kind: str) -> Portrait:
    return Portrait(picture.path, picture.sha256, kind, picture.asset, picture.source)


def build_entries(evidence: Path, entries: list[dict]) -> dict:
    exact, overrides, baseline, historical = load_sources(evidence)
    known_keys: dict[str, set[str]] = {}
    for enemy_id, prefab_key in overrides:
        known_keys.setdefault(enemy_id, set()).add(prefab_key)
    entries_by_id = {}
    for entry in entries:
        enemy_id = entry["id"]
        variants = {}
        if enemy_id in exact:
            picture = exact[enemy_id]
        else:
            keys = historical.get(enemy_id) or baseline.get(enemy_id) or []
            keys = sorted(set(keys) | known_keys.get(enemy_id, set()))
            for key in keys:
                variants[key] = overrides.get((enemy_id, key)) or related(exact[key], "prefab")
            if not variants:
                raise ValueError(f"No verified portrait for {enemy_id}")
            picture = next(iter(variants.values()))
        entries_by_id[enemy_id] = (picture, variants)
    return entries_by_id


def pack(pictures: dict[str, Portrait], output: Path) -> dict:
    rects = {}
    page = None
    for index, (sha256, picture) in enumerate(pictures.items()):
        atlas, cell = divmod(index, PER_PAGE)
        if cell == 0:
            page = Image.new("RGBA", (PAGE, PAGE))
        assert page is not None
        payload = picture.path.read_bytes()
        if hashlib.sha256(payload).hexdigest() != sha256:
            raise ValueError(f"Portrait hash mismatch: {picture.path}")
        with Image.open(io.BytesIO(payload)) as original:
            image = original.convert("RGBA")
        if not image.getchannel("A").getbbox():
            raise ValueError(f"Empty portrait: {picture.path}")
        if picture.kind in {"effect", "mechanism-effect", "effect-texture"}:
            bounds = image.getchannel("A").point(lambda value: 255 if value >= 16 else 0).getbbox()
            if bounds is not None:
                left, top, right, bottom = bounds
                margin = max(4, round(max(right - left, bottom - top) * 0.12))
                image = image.crop((
                    max(0, left - margin), max(0, top - margin),
                    min(image.width, right + margin), min(image.height, bottom + margin),
                ))
        image.thumbnail((SIZE, SIZE), Image.Resampling.LANCZOS)
        x = cell % COLUMNS * CELL + GUTTER
        y = cell // COLUMNS * CELL + GUTTER
        page.paste(image, (x + (SIZE - image.width) // 2, y + (SIZE - image.height) // 2))
        rects[sha256] = {"page": atlas, "x": x, "y": PAGE - y - SIZE}
        if cell == PER_PAGE - 1 or index == len(pictures) - 1:
            page.save(output / f"enemy-portraits-{atlas}.png", optimize=True)
    return rects


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--catalog", type=Path, required=True)
    args = parser.parse_args()
    index = read(args.catalog / "enemy-index.json")
    if index["revision"] != REVISION:
        raise ValueError("Unexpected enemy catalog revision.")
    entries = build_entries(args.evidence, index["entries"])
    pictures = {}
    for default, variants in entries.values():
        for picture in (default, *variants.values()):
            pictures.setdefault(picture.sha256, picture)
    positions = pack(pictures, args.catalog)

    def binding(picture: Portrait) -> dict:
        result = {"image": picture.sha256, "kind": picture.kind, "asset": picture.asset, "bundle": picture.source}
        if picture.light_background:
            result["lightBackground"] = True
        return result

    output = {
        "version": 1,
        "revision": REVISION,
        "pageSize": PAGE,
        "cellSize": SIZE,
        "images": positions,
        "entries": {
            key: {"default": binding(picture), "prefabs": {prefab: binding(variant) for prefab, variant in variants.items()}}
            for key, (picture, variants) in entries.items()
        },
    }
    (args.catalog / "enemy-portraits.json").write_text(
        json.dumps(output, ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8",
    )
    print(f"{len(entries)} entries, {len(positions)} images, {Counter(p.kind for p, _ in entries.values())}")


if __name__ == "__main__":
    main()
