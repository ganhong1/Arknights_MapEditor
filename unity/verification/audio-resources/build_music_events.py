"""Build the battle BGM picker index from the fixed PC 2.7.81 table and decoded clips."""

import argparse
import hashlib
import json
import struct
from pathlib import Path

NATIVE_SHA256 = "5e96594c42e740c4acbe6c6da928878d206dd34e14ea81083d25dcdb5adccfc8"
OPENING = "battle.ON_GAME_READY."
TRIGGER = "battle.ON_CUSTOM_TRIGGER."


class AudioTable:
    """Read only the native table slots used by this catalog."""

    def __init__(self, data):
        if hashlib.sha256(data).hexdigest() != NATIVE_SHA256:
            raise ValueError("Expected the PC 2.7.81 audio table TextAsset.")
        self.data = data
        self.root = 128 + self.uint(128)

    def uint(self, offset):
        return struct.unpack_from("<I", self.data, offset)[0]

    def short(self, offset):
        return struct.unpack_from("<H", self.data, offset)[0]

    def field(self, table, slot):
        start = table - struct.unpack_from("<i", self.data, table)[0]
        index = start + 4 + slot * 2
        value = self.short(index) if index < start + self.short(start) else 0
        return table + value if value else None

    def string(self, table, slot):
        field = self.field(table, slot)
        if field is None:
            return None
        start = field + self.uint(field)
        return self.data[start + 4:start + 4 + self.uint(start)].decode("utf-8")

    def float(self, table, slot):
        field = self.field(table, slot)
        return struct.unpack_from("<f", self.data, field)[0] if field is not None else None

    def rows(self, slot):
        field = self.field(self.root, slot)
        start = field + self.uint(field)
        for at in range(start + 4, start + 4 + self.uint(start) * 4, 4):
            yield at + self.uint(at)


def build(native, decoded):
    banks = {
        native.string(row, 6): {
            "intro": native.string(row, 0),
            "loop": native.string(row, 1),
            "crossfade": native.float(row, 3) or 0,
            "delay": native.float(row, 4) or 0,
        }
        for row in native.rows(0)
    }
    names = {
        native.string(row, 2): native.string(row, 1).strip()
        for row in native.rows(5)
        if native.string(row, 1)
    }
    aliases = {native.string(row, 0): native.string(row, 1) for row in native.rows(9)}

    def clips(bank):
        for part in ("intro", "loop"):
            path = bank[part]
            receipt = decoded["clips"].get(path.lower()) if path else None
            clip = {"asset": path} if path else None
            if receipt and receipt.get("status") == "decoded":
                clip.update(file=receipt["file"], sha256=receipt["flacSha256"],
                            sampleRate=receipt["sampleRate"], frames=receipt["frames"],
                            channels=receipt["channels"])
            if clip:
                yield part, clip

    def entry(event, bank_name, reference_event=None, suggested_key=None):
        prefix = OPENING if event.startswith(OPENING) else TRIGGER
        bank = banks[bank_name]
        key = event[len(prefix):]
        return {
            "opening": prefix == OPENING,
            "key": key,
            "label": names.get(bank_name) or names.get(event) or key,
            "event": event,
            "bank": bank_name if reference_event is None else None,
            "reference": reference_event,
            "suggestedKey": suggested_key,
            "crossfade": bank["crossfade"],
            "delay": bank["delay"],
            **dict(clips(bank)),
        }

    rows = {}
    for event in sorted(banks.keys() | aliases.keys()):
        if not event or not event.startswith((OPENING, TRIGGER)):
            continue
        bank_name = aliases.get(event, event)
        if bank_name in banks:
            rows[event] = entry(event, bank_name)

    for record in decoded["events"]:
        candidate = record.get("previewCandidate")
        if not candidate or record["field"] not in ("bgmEvent", "PLAY_BGM"):
            continue
        event = (OPENING if record["field"] == "bgmEvent" else TRIGGER) + record["key"]
        reference = OPENING + candidate["key"]
        bank_name = aliases.get(reference, reference)
        if event in rows or bank_name not in banks:
            raise ValueError(f"Unexpected binding or missing candidate for {event}.")
        replacement = candidate["key"] if record["field"] == "bgmEvent" else None
        rows[event] = entry(event, bank_name, reference, replacement)
    return {
        "version": 1,
        "gameVersion": "2.7.81",
        "audioTableSha256": NATIVE_SHA256,
        "entries": list(rows.values()),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("native_table", type=Path)
    parser.add_argument("decoded_index", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    decoded = json.loads(args.decoded_index.read_text(encoding="utf-8-sig"))
    index = build(AudioTable(args.native_table.read_bytes()), decoded)
    args.output.write_text(json.dumps(index, ensure_ascii=False, separators=(",", ":")) + "\n",
                           encoding="utf-8")


if __name__ == "__main__":
    main()
