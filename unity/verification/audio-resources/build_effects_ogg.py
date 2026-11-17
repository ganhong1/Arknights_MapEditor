"""Build the local audition pack for the verified 2.7.81 ambience clips."""
import argparse
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
from pathlib import Path
import subprocess

import UnityPy


def encode(item: tuple, output: Path) -> tuple:
    key, wav, receipt, source = item
    digest = hashlib.sha256(wav).hexdigest()
    if digest != receipt["wavSha256"]:
        raise ValueError(f"Source WAV does not match receipt: {key}")
    relative = f"clips/{key}.ogg"
    path = output / relative
    subprocess.run(
        ["ffmpeg", "-v", "error", "-xerror", "-nostdin", "-y", "-i", "pipe:0",
         "-map", "0:a:0", "-map_metadata", "-1", "-c:a", "libvorbis",
         "-q:a", "5", "-threads", "1", str(path)],
        input=wav, check=True, capture_output=True, timeout=90,
    )
    result = subprocess.run(
        ["ffprobe", "-v", "error", "-select_streams", "a", "-show_entries",
         "stream=codec_name,sample_rate,channels,duration_ts", "-of", "json", str(path)],
        check=True, capture_output=True, timeout=30,
    )
    stream, = json.loads(result.stdout)["streams"]
    rate, channels = int(stream["sample_rate"]), stream["channels"]
    samples = int(stream["duration_ts"])
    if (stream["codec_name"], rate, channels, samples) != (
        "vorbis", receipt["sampleRate"], receipt["channels"],
        round(receipt["seconds"] * receipt["sampleRate"]),
    ):
        raise ValueError(f"Ogg channels, rate, or duration mismatch: {key}")
    subprocess.run(
        ["ffmpeg", "-v", "error", "-xerror", "-nostdin", "-i", str(path), "-f", "null", "-"],
        check=True, capture_output=True, timeout=90,
    )
    return f"audio/sound_beta_2/ambience/a_bat/{key}", {
        "file": relative, "bytes": path.stat().st_size,
        "sha256": hashlib.sha256(path.read_bytes()).hexdigest(), "sourceWavSha256": digest,
        "sampleRate": rate, "channels": channels, "samples": samples, "source": source,
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("evidence", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    root = args.evidence
    baseline = json.loads((root / "baseline-2781-clip-verification.json").read_bytes())
    if baseline["resourceVersion"] != "26-10-22-14-02-11-51447808":
        raise ValueError("Unexpected audio resource version")
    tor = json.loads((root / "tor-wav-verification.json").read_bytes())
    source_bundle = root / "android-365-stage/Staging/audio/sound_beta_2/ambience.ab"
    objects = {
        obj.path_id: obj for obj in UnityPy.load(str(source_bundle)).objects
        if obj.type.name == "AudioClip"
    }
    items = []
    for key, row in baseline["clips"].items():
        if row["status"] != "verified":
            continue
        obj = objects[int(row["pathId"])].read()
        if obj.m_Name.casefold() != key:
            raise ValueError(f"Bundle object name mismatch: {key}")
        wav, = obj.samples.values()
        items.append((key, wav, row, "2.7.81 Android AssetBundle"))
    for key in baseline["verifiedFromPreBaselineTorExports"]:
        row = tor[key]
        items.append((key, (root / "tor-wav" / f"{key}.wav").read_bytes(), row, row["url"]))
    if len(items) != 144 or len({item[0] for item in items}) != 144:
        raise ValueError("Expected 144 distinct verified clips")
    (args.output / "clips").mkdir(parents=True, exist_ok=False)
    with ThreadPoolExecutor(max_workers=6) as pool:
        clips = dict(pool.map(lambda item: encode(item, args.output), items))
    result = {"format": "Ogg Vorbis", "quality": 5, "resourceBaseline": "2.7.81",
              "clipCount": len(clips), "bytes": sum(r["bytes"] for r in clips.values()),
              "clips": dict(sorted(clips.items()))}
    (args.output / "manifest.json").write_text(
        json.dumps(result, indent=2, ensure_ascii=False) + "\n", encoding="utf-8",
    )
    print(f'{result["clipCount"]} clips, {result["bytes"]} bytes')


if __name__ == "__main__":
    main()
