import io
import struct
import wave
from pathlib import Path

import pytest

from decode_audio_clips import event_state, inspect_pcm, source_path


def make_wav(*values: int) -> bytes:
    output = io.BytesIO()
    with wave.open(output, "wb") as stream:
        stream.setparams((1, 2, 8000, 0, "NONE", "not compressed"))
        stream.writeframes(struct.pack(f"<{len(values)}h", *values))
    return output.getvalue()


def test_pcm_inspection_checks_the_clip_shape() -> None:
    pcm = inspect_pcm(make_wav(0, -42, 27, 0), 1, 8000, 4 / 8000)
    assert pcm.frames == 4
    assert pcm.nonzero
    assert pcm.data == struct.pack("<4h", 0, -42, 27, 0)


def test_silence_is_valid_pcm() -> None:
    assert not inspect_pcm(make_wav(0, 0), 1, 8000, 2 / 8000).nonzero


@pytest.mark.parametrize(
    "channels,rate,duration", [(2, 8000, 0), (1, 44100, 0), (1, 8000, 1)]
)
def test_metadata_mismatch_is_rejected(
    channels: int, rate: int, duration: float
) -> None:
    with pytest.raises(ValueError):
        inspect_pcm(make_wav(1, 2), channels, rate, duration)


@pytest.mark.parametrize(
    "path", ["../bad.ab", "/bad.ab", "C:/bad.ab", "audio/../../bad.ab"]
)
def test_source_paths_cannot_escape_bundle_root(tmp_path: Path, path: str) -> None:
    with pytest.raises(ValueError):
        source_path(tmp_path, path)


def test_event_requires_one_complete_source_version() -> None:
    event = {"sources": [{"clips": ["A", "B"]}, {"clips": ["C", "D"]}]}
    assert event_state(event, {"a", "d"}) == "partial"
    assert event_state(event, {"c", "d"}) == "complete"
    assert event_state(event, {"other"}) == "unavailable"
    assert event_state({"sources": [{"clips": []}]}, set()) == "unavailable"
