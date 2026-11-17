"""Build the local, independently verifiable Ogg Vorbis audition library."""
import argparse
from concurrent.futures import ThreadPoolExecutor, as_completed
from hashlib import sha256 as new_sha256
import json
from pathlib import Path
import subprocess


def sha256(path):
    digest = new_sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def run(*command):
    return subprocess.run(
        command, check=True, stdin=subprocess.DEVNULL, capture_output=True,
        text=True, encoding="utf-8", errors="replace", timeout=180,
    ).stdout


def make_clip(key, clip, source_root, output, ffmpeg, ffprobe):
    source_file = (source_root / clip["file"]).resolve(strict=True)
    if not source_file.is_relative_to(source_root) or sha256(source_file) != clip["flacSha256"]:
        raise ValueError(f"Source does not match the verified receipt: {key}")
    dest = output / "clips" / source_file.with_suffix(".ogg").name
    temporary = dest.with_suffix(".partial.ogg")
    try:
        run(ffmpeg, "-v", "error", "-xerror", "-nostdin", "-y",
            "-i", str(source_file), "-map", "0:a:0", "-map_metadata", "-1",
            "-c:a", "libvorbis", "-q:a", "5", "-threads", "1", str(temporary))
        info = json.loads(run(ffprobe, "-v", "error", "-select_streams", "a",
            "-show_entries", "stream=codec_name,sample_rate,channels,time_base,duration_ts",
            "-of", "json", str(temporary)))["streams"]
        if len(info) != 1:
            raise ValueError(f"Expected one audio stream: {key}")
        stream = info[0]
        if (stream["codec_name"], int(stream["sample_rate"]), int(stream["channels"]),
            stream["time_base"], int(stream["duration_ts"])) != (
                "vorbis", clip["sampleRate"], clip["channels"],
                "1/" + str(clip["sampleRate"]), clip["frames"]):
            raise ValueError(f"Vorbis stream metadata did not match the source: {key}: {stream}")
        run(ffmpeg, "-v", "error", "-xerror", "-nostdin", "-threads", "1",
            "-i", str(temporary), "-map", "0:a:0", "-f", "null", "-")
        temporary.replace(dest)
    finally:
        temporary.unlink(missing_ok=True)
    return key, {
        "file": dest.relative_to(output).as_posix(),
        "bytes": dest.stat().st_size,
        "sha256": sha256(dest),
        "sourceBytes": source_file.stat().st_size,
        "sourceFlacSha256": clip["flacSha256"],
        "sampleRate": clip["sampleRate"],
        "channels": clip["channels"],
        "samples": clip["frames"],
        "sourceNonzeroPcm": clip["nonzeroPcm"],
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("index", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--ffmpeg", default="ffmpeg")
    parser.add_argument("--ffprobe", default="ffprobe")
    parser.add_argument("--jobs", type=int, default=6)
    args = parser.parse_args()
    source_root = args.index.resolve(strict=True).parent
    data = json.loads(args.index.read_text(encoding="utf-8-sig"))
    targets = {key: clip for key, clip in data["clips"].items()
               if clip["status"] == "decoded" and "/music/" in key}
    names = {Path(clip["file"]).name for clip in targets.values()}
    if not targets or len(names) != len(targets):
        raise ValueError("Missing music clips or duplicated output filenames")
    args.output.mkdir(parents=True, exist_ok=False)
    (args.output / "clips").mkdir()
    clips = {}
    errors = []
    with ThreadPoolExecutor(max_workers=args.jobs) as pool:
        pending = {pool.submit(make_clip, key, clip, source_root, args.output, args.ffmpeg, args.ffprobe): key
                   for key, clip in targets.items()}
        for count, task in enumerate(as_completed(pending), 1):
            try:
                key, result = task.result()
                clips[key] = result
            except (OSError, ValueError, subprocess.SubprocessError) as issue:
                errors.append({"clip": pending[task], "error": str(issue),
                               "details": getattr(issue, "stderr", "")})
            if count % 50 == 0 or count == len(targets):
                print(f"{count}/{len(targets)} processed, {len(errors)} failed", flush=True)
    if errors:
        (args.output / "errors.json").write_text(json.dumps(errors, indent=2), encoding="utf-8")
        raise SystemExit(f"{len(errors)} clips failed. See errors.json.")
    manifest = {
        "format": "Ogg Vorbis",
        "quality": 5,
        "resourceBaseline": "2.7.81",
        "sourceIndexSha256": sha256(args.index),
        "clipCount": len(clips),
        "sourceBytes": sum(clip["sourceBytes"] for clip in clips.values()),
        "bytes": sum(clip["bytes"] for clip in clips.values()),
        "clips": dict(sorted(clips.items())),
    }
    (args.output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: value for key, value in manifest.items() if key != "clips"}), flush=True)


if __name__ == "__main__":
    main()
