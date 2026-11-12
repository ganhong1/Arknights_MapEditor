"""Package reviewed Options blackboard notes without inferring allowed values."""

import argparse
import hashlib
import json
from pathlib import Path


def build(source: Path) -> dict:
    records = {}
    raw = source.read_bytes()
    for line in raw.decode("utf-8-sig").splitlines():
        if not line.startswith("| ") or line.startswith("| 键 |"):
            continue
        columns = [part.strip() for part in line.strip().strip("|").split("|")]
        if len(columns) != 6:
            raise ValueError(f"Unexpected constraint table row: {line}")
        key, representation, fallback, constraint, evidence, _ = columns
        if key in records:
            raise ValueError(f"Duplicate constraint key: {key}")
        records[key] = {
            "representation": representation,
            "fallback": fallback,
            "constraint": constraint,
            "evidence": evidence,
            "status": "sample-only" if evidence == "未定位" else "consumer-partial",
        }
    if not records:
        raise ValueError("No reviewed blackboard entries found")
    return {
        "version": 1,
        "context": "固定 PC 2.7.81；部分消费者规则，不是全部允许值或跨模式生效保证。",
        "source": {"file": source.name, "sha256": hashlib.sha256(raw).hexdigest()},
        "keys": records,
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    result = build(args.source)
    args.output.write_text(
        json.dumps(result, ensure_ascii=False, separators=(",", ":")) + "\n",
        encoding="utf-8",
    )
    print(f"Packaged {len(result['keys'])} reviewed keys")


if __name__ == "__main__":
    main()
