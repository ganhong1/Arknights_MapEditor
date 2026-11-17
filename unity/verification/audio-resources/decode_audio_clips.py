"""Verify the fixed AudioClip inventory and export lossless PCM16 listening files."""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import math
import subprocess
import tempfile
import wave
from collections import Counter, defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from UnityPy.classes import AudioClip


@dataclass(frozen=True)
class Pcm:
    data: bytes
    frames: int
    nonzero: bool


def source_path(root: Path, relative: str) -> Path:
    parts = relative.split("/")
    if any(part in {"", ".", ".."} or ":" in part or "\\" in part for part in parts):
        raise ValueError(f"Invalid resource path: {relative}")
    result = root.joinpath(*parts).resolve()
    if not result.is_relative_to(root.resolve()):
        raise ValueError(f"Resource path escapes the root: {relative}")
    return result


def read_json(path: Path) -> dict:
    return json.loads(path.read_bytes())


def write_json(path: Path, value: dict) -> None:
    with path.open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(value, stream, ensure_ascii=False, indent=2)
        stream.write("\n")


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def read_inventory(receipt: Path) -> dict:
    groups = defaultdict(list)
    for key, row in read_json(receipt)["results"].items():
        if row["status"] != "audio-clip" or row["classId"] != 83:
            raise ValueError(f"{key}: expected a verified AudioClip")
        groups[row["bundle"]].append((key, row))
    return dict(groups)


def group_sha(group: list) -> str:
    hashes = {row["sha256"] for _, row in group}
    if len(hashes) != 1:
        raise ValueError("Conflicting source bundle hashes in the object receipt")
    return hashes.pop()


def prepare(receipt: Path, source: Path, output: Path) -> None:
    files = []
    for relative, group in read_inventory(receipt).items():
        path = source_path(source, relative)
        files.append(
            {
                "path": relative,
                "source": str(path),
                "sourceType": "installation",
                "sourceSha256": group_sha(group),
                "sourceBytes": path.stat().st_size,
            }
        )
    write_json(output, {"files": files})


def read_payload(clip: AudioClip) -> tuple[bytes, dict]:
    from UnityPy.helpers.ResourceReader import get_resource_data

    if clip.m_AudioData:
        return bytes(clip.m_AudioData), {"location": "inline"}
    resource = clip.m_Resource
    if (
        clip.object_reader is None
        or resource is None
        or resource.m_Offset < 0
        or resource.m_Size <= 0
    ):
        raise ValueError("AudioClip has no valid stream reference")
    data = get_resource_data(
        resource.m_Source,
        clip.object_reader.assets_file,
        resource.m_Offset,
        resource.m_Size,
    )
    if len(data) != resource.m_Size:
        raise ValueError(
            f"Short resource stream: expected {resource.m_Size}, got {len(data)}"
        )
    return data, {
        "location": "stream",
        "path": resource.m_Source,
        "offset": resource.m_Offset,
        "bytes": resource.m_Size,
    }


def decode_wav(data: bytes, name: str, channels: int, rate: int) -> bytes:
    if data.startswith(b"RIFF"):
        return data
    if not data.startswith(b"FSB5"):
        raise ValueError(f"Unsupported stream signature {data[:8].hex()}")
    import fmod_toolkit

    samples = fmod_toolkit.raw_to_wav(
        data, name, channels, rate, convert_pcm_float=True
    )
    if len(samples) != 1:
        raise ValueError(f"Expected one FSB sample, got {len(samples)}")
    return next(iter(samples.values()))


def inspect_pcm(wav: bytes, channels: int, rate: int, duration: float) -> Pcm:
    with wave.open(io.BytesIO(wav), "rb") as reader:
        if (reader.getnchannels(), reader.getframerate(), reader.getsampwidth()) != (
            channels,
            rate,
            2,
        ):
            raise ValueError("Decoded PCM format disagrees with AudioClip metadata")
        frames = reader.getnframes()
        if (
            frames <= 0
            or not math.isfinite(duration)
            or abs(frames - duration * rate) > 2
        ):
            raise ValueError(
                f"Decoded frame count {frames} disagrees with {duration:g}s at {rate}Hz"
            )
        pcm = reader.readframes(frames)
        if len(pcm) != frames * channels * 2:
            raise ValueError("Decoded wav contains truncated PCM")
    return Pcm(pcm, frames, any(pcm))


def run_ffmpeg(args: list[str], data: bytes | None = None) -> bytes:
    result = subprocess.run(
        ["ffmpeg", "-nostdin", "-hide_banner", "-v", "error", "-xerror", *args],
        input=data,
        capture_output=True,
        timeout=120,
        check=False,
    )
    if result.returncode:
        raise RuntimeError(result.stderr.decode("utf-8", errors="replace").strip())
    return result.stdout


def export_flac(target: Path, wav: bytes, pcm: Pcm) -> None:
    with tempfile.TemporaryDirectory(prefix="clip-", dir=target.parent) as scratch:
        temporary = Path(scratch) / target.name
        run_ffmpeg(
            [
                "-f",
                "wav",
                "-i",
                "pipe:0",
                "-map_metadata",
                "-1",
                "-c:a",
                "flac",
                "-sample_fmt",
                "s16",
                "-compression_level",
                "3",
                str(temporary),
            ],
            wav,
        )
        restored = run_ffmpeg(
            ["-i", str(temporary), "-c:a", "pcm_s16le", "-f", "s16le", "pipe:1"]
        )
        if restored != pcm.data:
            raise ValueError("FLAC round trip changed decoded PCM")
        temporary.replace(target)


def decode_clip(key: str, row: dict, objects: dict, output: Path) -> dict:
    obj = objects[(row["cab"], int(row["pathId"]))]
    clip = obj.parse_as_object()
    channels, rate, duration = clip.m_Channels, clip.m_Frequency, clip.m_Length
    data, stream = read_payload(clip)
    wav = decode_wav(data, clip.m_Name, channels, rate)
    pcm = inspect_pcm(wav, channels, rate, duration)
    name = digest(key.encode("utf-8"))[:24] + ".flac"
    target = output / "clips" / name
    export_flac(target, wav, pcm)
    return {
        **row,
        "status": "decoded",
        "name": clip.m_Name,
        "stream": {
            **stream,
            "sha256": digest(data),
            "signature": data[:4].decode("ascii"),
        },
        "compressionFormat": clip.m_CompressionFormat,
        "channels": channels,
        "sampleRate": rate,
        "frames": pcm.frames,
        "seconds": pcm.frames / rate,
        "nonzeroPcm": pcm.nonzero,
        "pcm16Sha256": digest(pcm.data),
        "file": f"clips/{name}",
        "fileSize": target.stat().st_size,
        "flacSha256": digest(target.read_bytes()),
    }


def decode_bundle(
    relative: str, group: list, stage: Path, info: dict, output: Path
) -> dict:
    import UnityPy

    if info["sourceSha256"] != group_sha(group):
        raise ValueError("Staged bundle source hash differs from the object receipt")
    path = source_path(stage, relative)
    if path.stat().st_size != info["intermediateBytes"]:
        raise ValueError("Staged bundle size differs from the node receipt")
    environment = UnityPy.load(path.read_bytes())
    clips = {
        (obj.assets_file.name, obj.path_id): obj
        for obj in environment.objects
        if obj.type.value == 83
    }
    results = {}
    for key, row in group:
        try:
            results[key] = decode_clip(key, row, clips, output)
        except (
            KeyError,
            ValueError,
            OSError,
            RuntimeError,
            wave.Error,
            subprocess.SubprocessError,
        ) as error:
            results[key] = failure(row, "clip", error)
    return results


def failure(row: dict, stage: str, error: Exception) -> dict:
    return {
        **row,
        "status": "failed",
        "stage": stage,
        "error": f"{type(error).__name__}: {error}",
    }


def event_state(event: dict, decoded: set[str]) -> str:
    candidates = [
        {clip.lower() for clip in source["clips"]} for source in event["sources"]
    ]
    if any(keys and keys <= decoded for keys in candidates):
        return "complete"
    if any(keys & decoded for keys in candidates):
        return "partial"
    return "unavailable"


def event_rows(manifests: list[Path], decoded: set[str]) -> tuple[list[dict], dict]:
    coverage = defaultdict(Counter)
    rows = []
    for manifest in manifests:
        for event in read_json(manifest)["events"]:
            status = event_state(event, decoded)
            rows.append({**event, "decodeStatus": status})
            coverage[event["field"]][status] += 1
    return rows, dict(coverage)


def index_events(manifests: list[Path], receipts: list[Path], output: Path) -> None:
    clips = {}
    for receipt in receipts:
        for key, row in read_json(receipt)["results"].items():
            if row["status"] != "decoded":
                continue
            path = source_path(receipt.parent, row["file"])
            stored = path.relative_to(output.parent.resolve()).as_posix()
            if key in clips and clips[key]["pcm16Sha256"] != row["pcm16Sha256"]:
                raise ValueError(f"Conflicting decoded versions for {key}")
            clips[key] = {**row, "file": stored}
    events, totals = event_rows(manifests, set(clips))
    write_json(output, {"totalsByEvent": totals, "events": events, "clips": clips})


def decode(receipt: Path, manifest: Path, work: Path, output: Path) -> int:
    groups = read_inventory(receipt)
    staged = {row["path"]: row for row in read_json(work / "source-nodes.json")}
    output.mkdir()
    (output / "clips").mkdir()
    results = {}
    for index, (relative, group) in enumerate(groups.items(), 1):
        try:
            results.update(
                decode_bundle(
                    relative, group, work / "Staging", staged[relative], output
                )
            )
        except (KeyError, ValueError, OSError, RuntimeError) as error:
            results.update({key: failure(row, "bundle", error) for key, row in group})
        if index % 20 == 0 or index == len(groups):
            print(
                f"{index}/{len(groups)} bundles, {len(results)} clips checked",
                flush=True,
            )
    states = Counter(row["status"] for row in results.values())
    decoded = {key for key, row in results.items() if row["status"] == "decoded"}
    _, coverage = event_rows([manifest], decoded)
    write_json(
        output / "decode-receipt.json",
        {
            "objectReceiptSha256": digest(receipt.read_bytes()),
            "manifestSha256": digest(manifest.read_bytes()),
            "decoder": "UnityPy / fmod_toolkit → PCM16 → FFmpeg FLAC; PCM round trip verified",
            "bundleCount": len(groups),
            "states": states,
            "fileBytes": sum(row.get("fileSize", 0) for row in results.values()),
            "totalsByEvent": coverage,
            "results": results,
        },
    )
    print(dict(states), flush=True)
    return int(states["failed"] > 0)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    plan = commands.add_parser("prepare")
    for name in ("receipt", "source", "output"):
        plan.add_argument(name, type=Path)
    run = commands.add_parser("decode")
    for name in ("receipt", "manifest", "work", "output"):
        run.add_argument(name, type=Path)
    index = commands.add_parser("index")
    index.add_argument("--manifest", type=Path, action="append", required=True)
    index.add_argument("--receipt", type=Path, action="append", required=True)
    index.add_argument("output", type=Path)
    args = parser.parse_args()
    if args.command == "prepare":
        prepare(args.receipt, args.source, args.output)
        return 0
    if args.command == "index":
        index_events(args.manifest, args.receipt, args.output)
        return 0
    return decode(args.receipt, args.manifest, args.work, args.output)


if __name__ == "__main__":
    raise SystemExit(main())
