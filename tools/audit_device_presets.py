"""Read-only census of device records in original LevelData, without plugin changes."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import subprocess
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any

from tools.audit_level_modes import decode_json, level_resource

CONTAINERS = ("predefines", "hardPredefines")
ROLES = ("tokenInsts", "tokenCards")
CONTEXT_FIELDS = {
    "tokenInsts": frozenset(("position", "direction", "hidden", "alias")),
    "tokenCards": frozenset(("hidden", "alias")),
}


def canonical(value: Any) -> str:
    return json.dumps(value, sort_keys=True, ensure_ascii=False,
                      separators=(",", ":"), allow_nan=False)


def fingerprint(value: Any) -> str:
    return hashlib.sha256(canonical(value).encode("utf-8")).hexdigest()


def project_record(record: dict[str, Any], role: str) -> dict[str, Any]:
    """Comparison projection only. Excluded context remains in exact samples."""
    return {key: value for key, value in record.items() if key not in CONTEXT_FIELDS[role]}


def state(parent: dict[str, Any], key: str) -> str:
    if key not in parent:
        return "missing"
    value = parent[key]
    if value is None:
        return "null"
    if isinstance(value, list):
        return "empty" if not value else "nonempty"
    return type(value).__name__


def new_role() -> dict[str, Any]:
    return {"exactSamples": {}, "occurrences": 0, "levels": set()}


def record_key(record: Any) -> str | None:
    if not isinstance(record, dict) or not isinstance(record.get("inst"), dict):
        return None
    key = record["inst"].get("characterKey")
    return key if isinstance(key, str) and key else None


def scan_section(
    rows: list[Any], file: str, pointer: str, role: str,
    devices: dict[str, Any], level: dict[str, Any], issues: list[dict[str, Any]],
) -> None:
    for index, record in enumerate(rows):
        reference = {"level": file, "pointer": f"{pointer}/{index}"}
        key = record_key(record)
        if key is None:
            issues.append({**reference, "kind": "invalid-record", "record": record})
            continue
        role_data = devices.setdefault(key, {"roles": {}})["roles"].setdefault(role, new_role())
        sample_id = fingerprint(record)
        sample = role_data["exactSamples"].setdefault(
            sample_id, {"data": record, "references": []})
        if canonical(sample["data"]) != canonical(record):
            raise ValueError("SHA-256 collision between different records")
        sample["references"].append(reference)
        role_data["occurrences"] += 1
        role_data["levels"].add(file)
        level["records"].append({"device": key, "role": role,
                                 "sample": sample_id, "pointer": reference["pointer"]})


def scan_level(
    document: dict[str, Any], file: str, raw_hash: str,
    devices: dict[str, Any], issues: list[dict[str, Any]],
) -> dict[str, Any]:
    level: dict[str, Any] = {"sha256": raw_hash, "containerStates": {},
                             "sectionStates": {}, "records": [], "stages": []}
    for group in CONTAINERS:
        level["containerStates"][group] = state(document, group)
        container = document.get(group)
        if container is not None and not isinstance(container, dict):
            issues.append({"level": file, "pointer": f"/{group}", "kind": "invalid-container"})
            continue
        for role in ROLES:
            section = f"{group}/{role}"
            if container is None:
                level["sectionStates"][section] = "container-" + state(document, group)
                continue
            level["sectionStates"][section] = state(container, role)
            rows = container.get(role)
            if rows is None:
                continue
            if not isinstance(rows, list):
                issues.append({"level": file, "pointer": f"/{section}", "kind": "invalid-section"})
                continue
            scan_section(rows, file, f"/{section}", role, devices, level, issues)
    return level


def finish_role(role_data: dict[str, Any], role: str) -> dict[str, Any]:
    groups: dict[str, Any] = {}
    for sample_id, sample in sorted(role_data["exactSamples"].items()):
        projection = project_record(sample["data"], role)
        config_id = fingerprint(projection)
        config = groups.setdefault(config_id, {"id": config_id, "data": projection,
                                               "exactSampleIds": [], "occurrences": 0,
                                               "levels": set()})
        if canonical(config["data"]) != canonical(projection):
            raise ValueError("SHA-256 collision between different projections")
        sample["configuration"] = config_id
        config["exactSampleIds"].append(sample_id)
        config["occurrences"] += len(sample["references"])
        config["levels"].update(ref["level"] for ref in sample["references"])
    for config in groups.values():
        config["levels"] = sorted(config["levels"])
    configurations = sorted(groups.values(), key=lambda x: (-len(x["levels"]), -x["occurrences"], x["id"]))
    return {**role_data, "levels": sorted(role_data["levels"]), "configurations": configurations}


def field_profiles(devices: dict[str, Any], role: str) -> dict[str, Any]:
    """Observed field shapes and values, not complete native schemas or defaults."""
    profiles: dict[str, Any] = {}
    total = 0
    for device in devices.values():
        for sample in device["roles"].get(role, {}).get("exactSamples", {}).values():
            total += 1
            for key, value in sample["data"].items():
                profile = profiles.setdefault(key, {"presentExactSamples": 0, "types": Counter(), "values": Counter()})
                profile["presentExactSamples"] += 1
                profile["types"][type(value).__name__] += 1
                profile["values"][canonical(value)] += 1
    return {key: {"presentExactSamples": profile["presentExactSamples"],
                  "missingExactSamples": total - profile["presentExactSamples"],
                  "types": dict(sorted(profile["types"].items())),
                  "distinctValues": len(profile["values"]),
                  "mostCommonValues": [{"value": json.loads(value), "exactSamples": count}
                                       for value, count in profile["values"].most_common(8)]}
            for key, profile in sorted(profiles.items())}


def context_observations(devices: dict[str, Any], role: str) -> dict[str, Any]:
    counts: Counter[str] = Counter()
    blackboard: dict[str, Any] = {}
    for device_id, device in devices.items():
        for sample_id, sample in device["roles"].get(role, {}).get("exactSamples", {}).items():
            payload = sample["data"]
            amount = len(sample["references"])
            counts["hidden" if payload.get("hidden") is True else "notHidden"] += amount
            if payload.get("overrideTalents"):
                counts["nonemptyOverrideTalents"] += amount
            rows = payload.get("overrideSkillBlackboard")
            if not isinstance(rows, list) or not rows:
                continue
            counts["nonemptyOverrideSkillBlackboard"] += amount
            for pair in rows:
                if not isinstance(pair, dict) or not isinstance(pair.get("key"), str):
                    continue
                item = blackboard.setdefault(pair["key"], {"occurrences": 0, "devices": set(),
                                                          "example": {"device": device_id, "sample": sample_id,
                                                                      "pair": pair, "reference": sample["references"][0]}})
                item["occurrences"] += amount
                item["devices"].add(device_id)
    return {"recordCounts": dict(sorted(counts.items())),
            "observedSkillBlackboardKeys": {
                key: {**row, "devices": sorted(row["devices"])} for key, row in sorted(blackboard.items())},
            "completeNativeKeyCatalog": False}


def attach_stages(table: dict[str, Any], pairs: dict[str, Any], levels: dict[str, Any]) -> dict[str, int]:
    counts: Counter[str] = Counter()
    for key, stage in sorted(table.get("stages", {}).items()):
        path = level_resource(stage, pairs)
        if path is None:
            counts["stageEntriesWithoutBodyId"] += 1
            continue
        if path not in levels:
            counts["unresolvedStageEntries"] += 1
            continue
        levels[path]["stages"].append({
            "tableKey": key, **{field: stage[field] for field in (
                "stageId", "code", "name", "stageType", "difficulty", "diffGroup", "levelId") if field in stage}})
        counts["resolvedStageEntries"] += 1
    counts["levelsWithStageMetadata"] = sum(bool(row["stages"]) for row in levels.values())
    return dict(counts)


def summarize(devices: dict[str, Any], levels: dict[str, Any]) -> dict[str, Any]:
    roles = {}
    sections = {}
    for role in ROLES:
        rows = [(key, device["roles"][role]) for key, device in devices.items() if role in device["roles"]]
        roles[role] = {"deviceIds": len(rows), "records": sum(row["occurrences"] for _, row in rows),
                       "exactSamples": sum(len(row["exactSamples"]) for _, row in rows),
                       "configurations": sum(len(row["configurations"]) for _, row in rows),
                       "levels": len({file for _, row in rows for file in row["levels"]}),
                       "configurationCountDistribution": dict(sorted(Counter(len(row["configurations"]) for _, row in rows).items())),
                       "largestConfigurationSets": [{"id": key, "configurations": len(row["configurations"]),
                                                     "exactSamples": len(row["exactSamples"]), "levels": len(row["levels"])}
                                                    for key, row in sorted(rows, key=lambda item: (-len(item[1]["configurations"]), item[0]))[:20]]}
        for group in CONTAINERS:
            section = f"{group}/{role}"
            records = [ref for level in levels.values() for ref in level["records"]
                       if ref["pointer"].startswith(f"/{section}/")]
            sections[section] = {"records": len(records), "deviceIds": len({r["device"] for r in records}),
                                 "states": dict(sorted(Counter(level["sectionStates"].get(section, "invalid-container") for level in levels.values()).items()))}
    return {"roles": roles, "sections": sections,
            "bothRoles": sorted(key for key, item in devices.items() if len(item["roles"]) == 2),
            "deviceIds": len(devices), "validRecords": sum(x["records"] for x in roles.values()),
            "levelsWithRecords": sum(bool(level["records"]) for level in levels.values()),
            "runtimeValidated": False}


def audit_corpus(gamedata: Path) -> dict[str, Any]:
    levels: dict[str, Any] = {}
    devices: dict[str, Any] = {}
    issues: list[dict[str, Any]] = []
    errors = []
    non_levels = []
    manifest = {}
    for path in sorted((gamedata / "levels").rglob("*.json")):
        file = path.relative_to(gamedata / "levels").as_posix()
        try:
            source = path.read_bytes()
            raw_hash = hashlib.sha256(source).hexdigest()
            manifest["levels/" + file] = raw_hash
            document = decode_json(source)
            if not isinstance(document, dict) or not isinstance(document.get("mapData"), dict):
                non_levels.append(file)
                continue
            levels[file] = scan_level(document, file, raw_hash, devices, issues)
        except (OSError, ValueError, TypeError) as error:
            errors.append({"file": file, "error": str(error)})
    metadata = {}
    for name in ("excel/character_table.json", "excel/stage_table.json", "battle/battle_misc_table.json"):
        source = (gamedata / name).read_bytes()
        manifest[name] = hashlib.sha256(source).hexdigest()
        metadata[name] = decode_json(source)
    characters = metadata["excel/character_table.json"]
    for key, device in devices.items():
        definition = characters.get(key)
        device["definition"] = ({field: definition[field] for field in ("name", "profession", "subProfessionId", "prefabKey") if field in definition}
                                if isinstance(definition, dict) else None)
        device["roles"] = {role: finish_role(data, role) for role, data in sorted(device["roles"].items())}
    devices = dict(sorted(devices.items()))
    stage_counts = attach_stages(metadata["excel/stage_table.json"], metadata["battle/battle_misc_table.json"].get("levelScenePairs", {}), levels)
    static_ids = {key for key, row in characters.items() if key.startswith("trap_") or row.get("profession") == "TRAP"}
    summary = {**summarize(devices, levels), "levelCount": len(levels), "jsonFiles": len(manifest) - 3,
               "invalidRecords": sum(issue["kind"] == "invalid-record" for issue in issues),
               "staticDeviceIds": len(static_ids), "unobservedStaticDeviceIds": sorted(static_ids - devices.keys()),
               "observedOutsideStaticDeviceIds": sorted(devices.keys() - static_ids),
               "unknownDefinitionIds": sorted(key for key, row in devices.items() if row["definition"] is None),
               "stageMetadata": stage_counts}
    return {"schemaVersion": 1, "summary": summary, "devices": devices, "levels": levels,
            "fieldProfiles": {role: field_profiles(devices, role) for role in ROLES},
            "contextObservations": {role: context_observations(devices, role) for role in ROLES},
            "nonLevels": non_levels, "issues": issues, "errors": errors, "sourceHashes": manifest,
            "deduplication": {"exact": "All JSON fields, object keys sorted, arrays ordered, missing/null/empty/types preserved",
                              "projectionExcludedFields": {role: sorted(fields) for role, fields in CONTEXT_FIELDS.items()},
                              "projectionIsReadyToUseTemplate": False,
                              "originIdentity": "LevelData relative resource path. Stage aliases do not multiply origins."}}


def md(value: Any) -> str:
    return str(value).strip().replace("|", "\\|").replace("\n", " ").replace("\r", " ")


def device_name(device: dict[str, Any], key: str) -> str:
    return ((device["definition"] or {}).get("name") or key).strip()


def render_report(result: dict[str, Any]) -> str:
    summary = result["summary"]
    lines = ["# LevelData 装置配置全量核验", "", "本报告统计固定本地快照的原始记录，不证明运行生效，也不建立默认模板。", "",
             "## 范围与去重", "", f"扫描 {summary['jsonFiles']} 个 JSON，识别 {summary['levelCount']} 份 LevelData，涉及 {summary['deviceIds']} 个记录 ID。", "",
             "- 完整记录去重保留全部字段。对象键顺序忽略，数组顺序、数值类型及缺失／null／空集合差异保留。",
             "- 配置比较投影中，tokenInsts 只去掉 position、direction、hidden、alias；tokenCards 只去掉 hidden、alias，保留 initialCnt。",
             "- 投影只是辅助比较。隐藏、别名和摆放仍可能决定机制，完整记录与逐条出处保存在证据 JSON。",
             "- 常规和困难预设来源分开记录，不据容器存在推断实际启用。关卡出处按正文路径去重，stage 别名不重复计数。", "",
             "| 记录类别 | 原始条数 | 不同 ID | 完整记录去重 | 配置比较投影去重 | 正文出处数 |", "| --- | ---: | ---: | ---: | ---: | ---: |"]
    for role in ROLES:
        row = summary["roles"][role]
        lines.append(f"| {role} | {row['records']} | {row['deviceIds']} | {row['exactSamples']} | {row['configurations']} | {row['levels']} |")
    lines += ["", "## 预设容器与上下文", "", "| 位置 | 原始记录数 | ID 数 |", "| --- | ---: | ---: |"]
    for section, row in summary["sections"].items():
        lines.append(f"| {section} | {row['records']} | {row['deviceIds']} |")
    for role, observations in result["contextObservations"].items():
        lines += ["", f"{role} 的上下文观察为 `{md(canonical(observations['recordCounts']))}`。观察到 {len(observations['observedSkillBlackboardKeys'])} 种技能覆盖黑板键，不能解释为全部原生可用键。"]
    source = result.get("source", {})
    if source:
        lines += ["", f"数据版本 `{source['gitBefore']['revision']}`，相对来源 `{source['relativeToReport']}`。扫描前后 Git 身份分别记录在 JSON，全部读取文件均有原始字节 SHA-256。"]
    lines += ["", f"两类均有记录的 ID 共 {len(summary['bothRoles'])} 个。解析错误 {len(result['errors'])} 个，记录或容器问题 {len(result['issues'])} 个。", "",
              "## 每个装置总表", "", "每格依次为记录条数／完整样例数／配置投影数／正文出处数。", "",
              "| ID | 静态名称 | tokenInsts | tokenCards |", "| --- | --- | --- | --- |"]
    for key, device in result["devices"].items():
        cells = []
        for role in ROLES:
            row = device["roles"].get(role)
            cells.append(f"{row['occurrences']}／{len(row['exactSamples'])}／{len(row['configurations'])}／{len(row['levels'])}" if row else "—")
        lines.append(f"| `{md(key)}` | {md(device_name(device, key))} | {' | '.join(cells)} |")
    lines += ["", "## 配置投影最多的装置", "", "| 类别 | ID | 名称 | 配置数 | 正文出处数 |", "| --- | --- | --- | ---: | ---: |"]
    for role in ROLES:
        for row in summary["roles"][role]["largestConfigurationSets"][:5]:
            lines.append(f"| {role} | `{row['id']}` | {md(device_name(result['devices'][row['id']], row['id']))} | {row['configurations']} | {row['levels']} |")
    lines += ["", "## 静态目录覆盖边界", "", f"当前静态表按 trap_ 前缀或 TRAP 职业收集 {summary['staticDeviceIds']} 个 ID，其中 {len(summary['unobservedStaticDeviceIds'])} 个未在本次两类记录里出现。未出现不代表不能加载。", "",
              f"两类记录中静态目录以外的 ID 为 `{', '.join(summary['observedOutsideStaticDeviceIds'])}`。未在 character_table 中找到定义的 ID 数为 {len(summary['unknownDefinitionIds'])}。"]
    lines += ["", "## 索引与证据", "", "完整配置及全部正文出处见 CONFIGURATION_INDEX.md。完整原始记录、JSON 指针及源文件哈希见 device-presets.json。", "",
              "## 前几份逐条出处示例", ""]
    for key, device in list(result["devices"].items())[:3]:
        for role, row in device["roles"].items():
            sample = next(iter(row["exactSamples"].values()))
            lines += [f"- `{key}` / {role}: `{md(canonical(sample['data']))}`"]
            lines += [f"  - `{ref['level']}#{ref['pointer']}`" for ref in sample["references"][:3]]
    return "\n".join(lines) + "\n"


def render_index(result: dict[str, Any]) -> str:
    lines = ["# 装置既有配置与全部 LevelData 出处", "", "展示配置比较投影。完整记录按 SHA-256 在 device-presets.json 的 exactSamples 中查阅。", "",
             "来源按正文／预设容器聚合，方括号为原数组索引。索引保留每条记录，不把多个重复摆放计成多个关卡。", ""]
    for key, device in result["devices"].items():
        lines += [f"## {key} · {md(device_name(device, key))}", "", "### 总出处", ""]
        all_levels = sorted({file for row in device["roles"].values() for file in row["levels"]})
        for file in all_levels:
            stages = result["levels"][file]["stages"]
            labels = [" ".join(str(stage.get(field) or "").strip() for field in ("stageId", "code", "name")).strip() for stage in stages]
            lines.append(f"- `{file}`" + (f" · {md('；'.join(labels))}" if labels else " · stage_table 无名称关联"))
        lines.append("")
        for role, row in device["roles"].items():
            lines += [f"### {role}", ""]
            for number, config in enumerate(row["configurations"], 1):
                lines += [f"#### 配置 {number} · {config['id']}", "",
                          f"{config['occurrences']} 条记录，{len(config['exactSampleIds'])} 份完整样例，{len(config['levels'])} 个正文出处。", "",
                          "```json", json.dumps(config["data"], ensure_ascii=False, indent=2), "```", ""]
                sources: dict[tuple[str, str], list[int]] = defaultdict(list)
                for sample_id in config["exactSampleIds"]:
                    for ref in row["exactSamples"][sample_id]["references"]:
                        section, index = ref["pointer"].rsplit("/", 1)
                        sources[(ref["level"], section)].append(int(index))
                for (file, section), indices in sorted(sources.items()):
                    lines.append(f"- `{file}#{section}` [{', '.join(map(str, sorted(indices)))}]")
                lines += [""]
    return "\n".join(lines)


def validate_output(gamedata: Path, output: Path) -> None:
    if output.exists():
        raise ValueError("Evidence directory already exists; choose a new directory")
    if output.resolve().is_relative_to(gamedata.resolve()):
        raise ValueError("Evidence output must be outside the source data directory")


def git_identity(root: Path) -> dict[str, Any]:
    result = {}
    for name, args in (("revision", ["rev-parse", "HEAD"]),
                       ("status", ["status", "--porcelain", "--untracked-files=all", "--", "."])):
        run = subprocess.run(["git", "-C", str(root), *args], capture_output=True, text=True, check=False)
        result[name] = run.stdout.strip() if run.returncode == 0 else None
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--gamedata", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    try:
        validate_output(args.gamedata, args.output)
    except ValueError as error:
        parser.error(str(error))
    if not (args.gamedata / "levels").is_dir():
        parser.error("Missing levels directory")
    before = git_identity(args.gamedata)
    result = audit_corpus(args.gamedata)
    after = git_identity(args.gamedata)
    result["source"] = {"relativeToReport": os.path.relpath(args.gamedata, args.output).replace("\\", "/"),
                        "gitBefore": before, "gitAfter": after}
    if before != after or before["status"] or before["revision"] is None:
        result["errors"].append({"kind": "source-identity-not-clean-and-stable"})
    args.output.mkdir(parents=True, exist_ok=False)
    outputs = {"device-presets.json": json.dumps(result, ensure_ascii=False, indent=2, allow_nan=False) + "\n",
               "DEVICE_PRESET_AUDIT.md": render_report(result), "CONFIGURATION_INDEX.md": render_index(result)}
    manifest = {}
    for name, content in outputs.items():
        encoded = content.encode("utf-8")
        with (args.output / name).open("xb") as target:
            target.write(encoded)
        manifest[name] = {"sha256": hashlib.sha256(encoded).hexdigest(), "bytes": len(encoded)}
    with (args.output / "manifest.json").open("x", encoding="utf-8") as target:
        json.dump(manifest, target, ensure_ascii=False, indent=2)
        target.write("\n")
    summary = result["summary"]
    print(json.dumps({"levelCount": summary["levelCount"], "deviceIds": summary["deviceIds"],
                      "validRecords": summary["validRecords"], "errors": result["errors"],
                      "issues": len(result["issues"])}, ensure_ascii=False))
    return int(bool(result["errors"] or result["issues"]))


if __name__ == "__main__":
    raise SystemExit(main())
