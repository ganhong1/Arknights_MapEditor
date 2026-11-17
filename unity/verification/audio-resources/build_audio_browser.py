"""Build the 2.7.81 audio browser index from versioned native records and receipts."""
import argparse
import hashlib
import json
from pathlib import Path

VERSION = "2.7.81"
RESOURCE_VERSION = "26-10-22-14-02-11-51447808"
TABLE_SHA256 = "45268b51d57b691fa88bbfb6e9d0c52e09f8f060787a44b59afe94a871954a2f"
PREFIX = "audio/sound_beta_2/ambience/a_bat/"

USAGE = {
    "环境底声/水域与海岸": {0},
    "环境底声/地热与熔岩": {1},
    "天气与全图机制/气象": {140, 158, 159},
    "天气与全图机制/全场状态变化": {10, 253},
    "天气与全图机制/地图事件": {131, 132},
    "地块音效/位移与形态变化": {105, 106},
    "地块音效/地表状态变化": {2, 46, 86, 87},
    "地块音效/踩踏与触发": {34, 35},
    "装置音效/出现与布设": {107, 125, 126},
    "装置音效/启动与蓄能": {11, 15, 17, 19, 22, 23, 84, 93, 96, 97, 99, 122,
                       127, 129, 130, 133, 152, 163, 224, 229, 245, 248, 250},
    "装置音效/停止与复位": {26, 27, 94, 98, 123, 173, 246},
    "装置音效/损毁与退场": {78, 79, 170, 172},
    "关卡演出/剧情演出": {9},
    "关卡演出/阶段转场": {95, 137},
    "关卡演出/镜头与画面效果": set(range(143, 150)),
    "玩法与操作反馈/操作与解锁": {109, 117, 142, 193, 225, 228},
    "玩法与操作反馈/提示与倒计时": {108, 141, 162, 194, 226},
    "玩法与操作反馈/结果与重置": {240, 241, 242},
    "玩法与操作反馈/模式规则触发": {72, 120, 121},
}
LABELS = {
    "beach": "海岸水声", "magma": "熔岩环境声", "volcano": "地表喷发",
    "icespread": "寒霜扩散", "fire": "芦苇燃烧", "lghtsrk": "天气雷击",
    "fltgrdp": "地块浮起", "fltgrdwn": "地块落下", "pkcld": "云瓶解锁",
    "cldhnt": "云瓶图标出现", "jrwndlw": "打开全部云瓶", "cldbm": "云瓶破裂",
    "blcntdw": "点球倒计时", "blghtcrg": "雷电蓄能", "blghtsrk": "雷电落下",
    "buildingshaking_1": "建筑震动 1", "buildingshaking_2": "建筑震动 2",
    "tidectrl_shallow": "潮汐装置启动 · 浅水", "tidectrl_deep": "潮汐装置启动 · 深水",
    "tidectrloop_shallow": "潮汐装置运行 · 浅水", "tidectrloop_deep": "潮汐装置运行 · 深水",
    "tidectrlend_shallow": "潮汐装置停止 · 浅水", "tidectrlend_deep": "潮汐装置停止 · 深水",
}


def usage(index: int, event: str) -> list[str]:
    explicit = [name for name, values in USAGE.items() if index in values]
    if explicit:
        return explicit
    signal = event.split(".")[1]
    if signal == "ON_UNIT_BORN":
        return ["装置音效/出现与布设"]
    if signal == "ON_UNIT_DEAD":
        return ["装置音效/损毁与退场"]
    return ["装置音效/运作与释放"]


def clip_metadata(asset: str, clips: dict, source_hash_key: str) -> dict:
    result = {"asset": asset}
    record = clips.get(asset.lower())
    if record:
        result.update(file=record["file"], sha256=record[source_hash_key],
                      sampleRate=record["sampleRate"], frames=record["samples"],
                      channels=record["channels"], source=record.get("source"))
    return result


def native_music(table: dict, music_manifest: dict) -> list[dict]:
    banks = {row["name"]: row for row in table["bgmBanks"]}
    aliases = table["bankAlias"]
    if len(banks) != 577 or len(aliases) != 230 or banks.keys() & aliases.keys():
        raise ValueError("Unexpected counts or conflicting original music events")
    labels = {}
    for record in table["musics"]:
        bank = aliases.get(record["bank"], record["bank"])
        labels.setdefault(bank, []).append((record["id"], (record["name"] or "").strip()))
    result = []
    for event in sorted(banks.keys() | aliases.keys()):
        name = aliases.get(event, event)
        bank = banks[name]
        lookup = labels.get(name, [])
        title = next((label for _, label in lookup if label), event.split(".", 2)[-1])
        row = {"event": event, "bank": name, "label": title,
               "terms": " ".join(item for pair in lookup for item in pair),
               "crossfade": bank["crossfade"], "delay": bank["delay"]}
        for part in ("intro", "loop"):
            if asset := bank[part]:
                row[part] = clip_metadata(asset, music_manifest["clips"], "sourceFlacSha256")
        result.append(row)
    return result


def native_effects(table: dict, evidence: dict, manifest: dict) -> list[dict]:
    target = evidence["scope"][RESOURCE_VERSION]
    rows = [row for row in table["soundFXBanks"] if any(
        (sound.get("asset") or "").lower().startswith(PREFIX)
        for sound in (row["sounds"] or []) if sound)]
    if len(rows) != target["registrySoundEffectEvents"]:
        raise ValueError("Sound registry and scoped evidence differ")
    result = []
    for item in target["matchedRows"]:
        row = rows[item["i"]]
        event = row["name"]
        if event not in {item["n"], "battle." + item["n"]}:
            raise ValueError(f"Mismatched source event: {event}")
        choices = []
        for sound in row["sounds"]:
            asset = sound["asset"]
            key = asset.rsplit("/", 1)[-1].lower()
            if key not in item["a"]:
                continue
            clip = clip_metadata(asset, manifest["clips"], "sourceWavSha256")
            if "file" not in clip:
                raise ValueError(f"Missing verified preview: {asset}")
            choices.append({"clip": clip, "weight": sound["weight"], "is2D": sound["is2D"],
                            "label": LABELS.get(key.removeprefix("a_bat_"), key)})
        if {x["clip"]["asset"].rsplit("/", 1)[-1].lower() for x in choices} != set(item["a"]):
            raise ValueError(f"Mismatched preview assets for {event}")
        result.append({"event": event, "bank": event, "label": choices[0]["label"],
                       "usage": usage(item["i"], event), "effects": choices,
                       "effectLoop": row["loop"], "mixer": row["customMixerGroup"]})
    if (len(result), len({x["clip"]["asset"].lower() for row in result for x in row["effects"]})) != (227, 144):
        raise ValueError("Expected 227 sound effect events and 144 clips")
    return result


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("table", "evidence", "music_manifest", "effects_manifest", "output"):
        parser.add_argument(name, type=Path)
    args = parser.parse_args()
    raw = args.table.read_bytes()
    table_hash = hashlib.sha256(raw).hexdigest()
    if table_hash != TABLE_SHA256:
        raise ValueError("Expected the fixed 2.7.81 audio export")
    table = json.loads(raw)
    manifests = [json.loads(path.read_bytes()) for path in
                 (args.music_manifest, args.effects_manifest)]
    if any(part["resourceBaseline"] != VERSION for part in manifests):
        raise ValueError("Audition manifest baseline mismatch")
    index = {"version": 1, "gameVersion": VERSION, "resourceVersion": RESOURCE_VERSION,
             "tableSha256": table_hash,
             "music": native_music(table, manifests[0]),
             "effects": native_effects(table, json.loads(args.evidence.read_bytes()), manifests[1])}
    args.output.write_text(json.dumps(index, ensure_ascii=False, separators=(",", ":")) + "\n",
                           encoding="utf-8")


if __name__ == "__main__":
    main()
