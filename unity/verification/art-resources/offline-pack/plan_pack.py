"""Select candidate assets and their physical bundle closure from the pinned PC manifest."""
import argparse
import hashlib
import json
from collections import deque
from pathlib import Path
import struct


MANIFEST = "41ae9b221e84c6ff47871ca907c538d2.idx"
MANIFEST_HASH = "237d76bb32fde6c2810574fbdc581593bca268a8245d2b03970839f66c301287"
CURRENT_DEPENDENCIES = {
    "anon/88b3c3ecaaefa6003de730686dd5ac53.bin": "anon/bac85f8fe65f27a2b7ea13fff6de62e8.bin",
    "anon/dfba18b16546da2b44cbbe0fa4019066.bin": "anon/e553a7736ee5daa079938ed261d3d1ca.bin",
}
CURRENT_DEPENDENCY_HASHES = {
    "anon/bac85f8fe65f27a2b7ea13fff6de62e8.bin": "10fca9cc0cc0fde445e5133c004ef08f776640fb99440e3cb8949fa7d9541c99",
    "anon/e553a7736ee5daa079938ed261d3d1ca.bin": "cf29e8dc73c1c1595da296781ffe7c1d3dbe16a0d339f87242e966244a3b7bc9",
}
SCENE = "scenes/obt/main/level_main_05-10/level_main_05-10"


def read_manifest(source):
    raw = (source / MANIFEST).read_bytes()
    if hashlib.sha256(raw).hexdigest() != MANIFEST_HASH:
        raise ValueError("The input is outside the pinned PC resource snapshot.")
    data = memoryview(raw)[128:]

    def uint(offset):
        return struct.unpack_from("<I", data, offset)[0]

    def field(table, slot):
        vtable = table - struct.unpack_from("<i", data, table)[0]
        length = struct.unpack_from("<H", data, vtable)[0]
        if 4 + 2 * slot >= length:
            return None
        value = struct.unpack_from("<H", data, vtable + 4 + 2 * slot)[0]
        return table + value if value else None

    def vector(address):
        vector_base = address + uint(address)
        return range(vector_base + 4, vector_base + 4 + 4 * uint(vector_base), 4)

    def text(address):
        if address is None:
            return ""
        start = address + uint(address)
        return bytes(data[start + 4:start + 4 + uint(start)]).decode("utf-8")

    def tables(address):
        return [item + uint(item) for item in vector(address)]

    root = uint(0)
    bundle_tables = tables(field(root, 1))
    paths = [text(field(t, 0)) for t in bundle_tables]
    bundle_dependencies = {}
    for path, table in zip(paths, bundle_tables):
        slot = field(table, 3)
        bundle_dependencies[path] = [paths[uint(item)] for item in vector(slot)] if slot else []
    assert len(bundle_dependencies) == 13596
    assert all(bundle in bundle_dependencies for refs in bundle_dependencies.values() for bundle in refs)
    assets = []
    for table in tables(field(root, 2)):
        key = text(field(table, 0))
        unity_path = text(field(table, 3))
        slot = field(table, 1)
        assets.append({"key": key, "unityPath": unity_path, "bundle": paths[uint(slot) if slot else 0]})
    assert len(assets) == 132134
    return assets, bundle_dependencies


def candidate_kind(item):
    key = item["key"]
    path = item["unityPath"]
    if key.startswith("scenes/") and path.endswith(".unity"):
        return "scene"
    if key.startswith("arts/maps/"):
        return "map-art"
    if key.startswith("battle/prefabs/effects/") and key.rsplit("/", 1)[-1].startswith(("mapeff_", "map_", "screen_")):
        return "effect"
    if ("/cameraplugin/" in key or key.startswith("ui/levelrelatedcameraplugin/")) and path.endswith(".prefab"):
        return "camera"
    return None


def closure(initial, graph):
    found = set(initial)
    pending = deque(found)
    while pending:
        for dependency in graph[pending.popleft()]:
            if dependency not in found:
                found.add(dependency)
                pending.append(dependency)
    return found


def write_json(filename, value):
    filename.write_text(json.dumps(value, ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", required=True, type=Path)
    parser.add_argument("--sample-report", required=True, type=Path)
    parser.add_argument("--verified-refs", required=True, type=Path)
    parser.add_argument("--out", required=True, type=Path)
    args = parser.parse_args()
    assets, graph = read_manifest(args.source)
    by_key = {item["key"]: item | {"kind": kind} for item in assets if (kind := candidate_kind(item))}
    assert len(by_key) == 7874
    known_effect_keys = {
        "battle/prefabs/effects/map_038_sign_glow_02",
        "battle/prefabs/effects/mapeff_waterternado_01",
        "battle/prefabs/effects/mapeff_v077_change_01",
        "battle/prefabs/effects/screen_lmc_fog_02",
    }
    extra_dependencies = {}
    references = json.loads(args.verified_refs.read_text("utf-8"))
    for row in references["results"]:
        if row["key"] == "rogue_4_boss4":
            key = "ui/rogueliketopic/topics/rogue_4/cameraplugin/rogue_4_boss4"
        else:
            key = "battle/prefabs/effects/" + row["key"].lower()
        assert key in by_key
        extra_dependencies.setdefault(by_key[key]["bundle"], set()).update(
            CURRENT_DEPENDENCIES[entry["bundle"]] for entry in row["entries"] if entry["status"] == "cached-anonymous"
        )

    for name, digest in CURRENT_DEPENDENCY_HASHES.items():
        assert name in graph
        assert hashlib.sha256((args.source / name).read_bytes()).hexdigest() == digest

    probe = json.loads(args.sample_report.read_text("utf-8"))
    sample_scene_bundles = {row["relative"] for row in probe["bundleLoads"] if row["loaded"]}
    fixture_dependencies = sample_scene_bundles - set(graph)
    if fixture_dependencies:
        raise ValueError(f"Missing known 5-10 bundle paths: {sorted(fixture_dependencies)}")

    args.out.mkdir(parents=True, exist_ok=True)
    selected = {
        key: item for key, item in by_key.items()
        if key == SCENE or key.startswith("arts/maps/map_lm_center/") or key in known_effect_keys
    }
    for scope, items in (("sample", selected), ("full", by_key)):
        roots = {item["bundle"] for item in items.values()}
        if scope == "sample":
            roots.update(sample_scene_bundles)
        installed = closure(roots, graph)
        if scope == "full":
            assert len(installed) == 5539
        packages = set(installed)
        for bundle in sort_paths(installed):
            packages.update(extra_dependencies.get(bundle, ()))
        graph_rows = []
        sources = []
        for bundle in sort_paths(packages):
            deps = closure(graph[bundle], graph) - {bundle}
            for dependency in list(deps) + [bundle]:
                deps.update(extra_dependencies.get(dependency, ()))
            assert deps <= packages, (bundle, deps - packages)
            graph_rows.append({"path": bundle, "dependencies": sort_paths(deps)})
            source = args.source.resolve() / bundle
            sources.append({"path": bundle, "source": str(source), "sourceType": "pc-installed",
                            "sourceSha256": CURRENT_DEPENDENCY_HASHES.get(bundle),
                            "sourceBytes": source.stat().st_size})
        info = {
            "formatVersion": 1,
            "gameBaseline": "PC 2.7.81",
            "resourceVersion": "26-10-20-11-10-38-04170990",
            "sourceManifestSha256": MANIFEST_HASH,
            "unityVersion": "2021.3.39f1",
            "platform": "Windows",
            "compression": "LZ4",
            "scope": scope,
        }
        write_json(args.out / f"{scope}-catalog.json", info | {"assets": [items[key] for key in sorted(items)], "bundles": graph_rows})
        write_json(args.out / f"{scope}-sources.json", info | {"files": sources})
        print(scope, len(items), len(graph_rows), sum(row["sourceBytes"] for row in sources))


def sort_paths(values):
    return sorted(values, key=lambda path: (path.casefold(), path))


if __name__ == "__main__":
    main()
