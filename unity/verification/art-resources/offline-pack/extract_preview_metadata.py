"""Extract the serialized camera and environment inputs for the Unity art preview."""
import argparse
import hashlib
import json
from collections import defaultdict
from pathlib import Path

import UnityPy


UNITY_HEADERS = {"m_GameObject", "m_Enabled", "m_Script", "m_Name"}
CAMERAS = {
    "act49side_camera_move_plugin": "Act49sideCameraMovePlugin",
    "act55side_camera_move_plugin": "CameraMoveAdvancedPlugin",
    "common_camera_dither_plugin": "CommonDitherCameraPlugin",
    "common_camera_move_half_second_plugin": "CommonCameraMovePlugin",
    "common_camera_move_plugin": "CommonCameraMovePlugin",
    "mainline_17_camera_move_plugin": "CameraMoveAdvancedPlugin",
    "rogue_4_boss4": "CameraRoguelikePluginRL04Boss4",
}


def properties(tree):
    return {key: value for key, value in tree.items() if key not in UNITY_HEADERS}


def camera_record(entry, obj):
    components = obj.read_typetree()["m_Component"]
    behaviours = []
    for component in components:
        reference = component["component"]
        if reference["m_FileID"] == 0:
            target = obj.assets_file.objects[reference["m_PathID"]]
            if target.type.name == "MonoBehaviour":
                behaviours.append(target)
    if len(behaviours) != 1:
        raise ValueError(f"{entry['key']}: expected one camera controller")
    controller = behaviours[0]
    tree = controller.read_typetree()
    class_name = CAMERAS[entry["key"].rsplit("/", 1)[-1]]
    record = {
        "type": class_name,
        "objectId": str(controller.path_id),
        "fields": properties(tree),
    }
    if class_name == "CameraRoguelikePluginRL04Boss4":
        # PC 2.7.81 dump.cs, CameraRoguelikePluginRL04Boss4.MOVE_TIME.
        record["constants"] = {"MOVE_TIME": 5.0}
    return record


def profile_record(obj):
    tree = obj.read_typetree()
    fields = properties(tree)
    keys = {
        "settings": "PostProcessProfile",
        "shadowTint": "SceneEffectProfile",
        "shadowMapSize": "HGShadowProfile",
        "depthFullRes": "WaterEffectProfile",
        "rtSize": "FogProfile",
        "levelPacks": "HighlightLevelTileProfile",
        "gapForUseAnim": "HighlightPolluteTileProfile",
        "initState": "HighlightTileProfile",
        "highlightMapSize": "HGSceneHighlightProfile",
        "useBuiltinForwardSRPBatcher": "SceneSRPProfile",
        "spineClip": "SceneSpineIntersectProfile",
    }
    class_name = next((label for key, label in keys.items() if key in fields), "EnvironmentProfile")
    result = {"type": class_name, "objectId": str(obj.path_id), "fields": fields}
    if class_name == "PostProcessProfile":
        settings = []
        for reference in fields["settings"]:
            if reference["m_PathID"] == 0 or reference["m_FileID"] != 0:
                continue
            setting = obj.assets_file.objects[reference["m_PathID"]].read_typetree()
            settings.append({"type": setting["m_Name"], "fields": properties(setting)})
        result["settings"] = settings
    return result


def preserve_file_ids(value):
    if isinstance(value, dict):
        return {key: str(item) if key == "m_PathID" else preserve_file_ids(item)
                for key, item in value.items()}
    if isinstance(value, (list, tuple)):
        return [preserve_file_ids(item) for item in value]
    return value


def extract(root):
    catalog_bytes = (root / "catalog.json").read_bytes()
    catalog = json.loads(catalog_bytes)
    sources = defaultdict(list)
    for entry in catalog["assets"]:
        if entry["kind"] == "camera" or (
            entry["kind"] == "map-art" and entry["unityPath"].endswith(".asset")
        ):
            sources[entry["bundle"]].append(entry)

    records = {}
    for bundle, entries in sources.items():
        env = UnityPy.load(str(root / "Bundles" / bundle))
        container = {path.casefold(): pointer for path, pointer in env.container.items()}
        for entry in entries:
            obj = container[entry["unityPath"].casefold()].deref()
            if entry["kind"] == "camera" and obj.type.name == "GameObject":
                record = camera_record(entry, obj)
            elif entry["kind"] == "map-art" and obj.type.name == "MonoBehaviour":
                record = profile_record(obj)
            else:
                raise ValueError(f"{entry['key']}: unexpected serialized type {obj.type.name}")
            records[entry["key"]] = preserve_file_ids(record)
    return {
        "formatVersion": 1,
        "catalogSha256": hashlib.sha256(catalog_bytes).hexdigest(),
        "records": records,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("pack", type=Path)
    args = parser.parse_args()
    data = extract(args.pack)
    target = args.pack / "preview-metadata.json"
    temporary = target.with_suffix(".tmp")
    temporary.write_text(json.dumps(data, ensure_ascii=False, allow_nan=False,
                                    separators=(",", ":"), sort_keys=True) + "\n", encoding="utf-8")
    temporary.replace(target)
    print(f"{len(data['records'])} records, {target.stat().st_size} bytes")


if __name__ == "__main__":
    main()
