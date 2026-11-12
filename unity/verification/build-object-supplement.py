"""Pack verified supplemental visuals without modifying the PRTSMap atlas."""
from __future__ import annotations

import argparse
from collections import Counter
from hashlib import sha256
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

GROUPS = {
    "封锁地块": ("#d5a970", "LOCK", "trap_004_iceblock trap_017_wooden_broke trap_047_mandrablock trap_180_dsblock trap_1107_acblock"),
    "水位": ("#52b6e8", "TIDE", "trap_042_tidectrl"),
    "地表效果": ("#9ee2a1", "GRID", "trap_061_creep trap_098_mire trap_103_ymdgct"),
    "战场规则": ("#c89cff", "CTRL", "trap_091_brctrl trap_162_lrctrl trap_179_muctrl trap_251_buftrp trap_767_duelcdt"),
    "环境危害": ("#efa4c7", "HAZ", "trap_1112_acblzd trap_115_gasctrl"),
    "方向控制": ("#76dee1", "GRAV", "trap_121_gractrl"),
    "敌人生成": ("#fb9a81", "SPAWN", "trap_146_dhdcr"),
    "区域切换": ("#68dace", "ZONE", "trap_250_hlctrl"),
    "检查点": ("#f8d772", "FLAG", "trap_484_xbcp"),
    "召唤卡": ("#a7b0f4", "CARD", "trap_293_suishan"),
    "全场攻击": ("#ff866d", "ATK", "trap_335_totalattack"),
}
LABELS = {
    "wiki-avatar": "原版头像 · Wiki",
    "game-ui": "原版游戏图标",
    "model": "原模型缩略图",
    "spine": "原 Spine 姿势缩略图",
    "effect": "原版特效代表帧",
    "mechanism": "机制标记",
    "weather-marker": "机制标记 · 原版天气图标",
}


def read(path: Path):
    return json.loads(path.read_bytes())


def normalized_image(path: Path, *, effect: bool = False) -> Image.Image:
    with Image.open(path) as source:
        image = source.convert("RGBA")
    if effect:
        # Unity's transparent render target contains premultiplied RGB. Restore straight alpha for the icon shader.
        image = Image.frombytes("RGBa", image.size, image.tobytes()).convert("RGBA")
    bounds = image.getchannel("A").point(lambda v: 255 if v > 24 else 0).getbbox()
    if bounds is None:
        raise ValueError(f"Empty image: {path}")
    image = image.crop(bounds)
    size = 51 if effect else 58
    image.thumbnail((size, size), Image.Resampling.LANCZOS)
    canvas = Image.new("RGBA", (64, 64))
    if effect:
        draw = ImageDraw.Draw(canvas)
        draw.ellipse((4, 4, 59, 59), fill="#2b3847", outline="#91b1cb", width=2)
    canvas.alpha_composite(image, ((64 - image.width) // 2, (64 - image.height) // 2))
    return canvas


def badge(color: str, glyph: str) -> Image.Image:
    image = Image.new("RGBA", (128, 128))
    d = ImageDraw.Draw(image)
    d.rounded_rectangle((14, 14, 114, 114), radius=28, fill="#17222cf5", outline=color, width=5)
    if glyph == "LOCK":
        d.arc((48, 36, 80, 77), 180, 360, fill=color, width=5)
        d.rounded_rectangle((43, 58, 85, 88), radius=6, outline=color, width=5)
        d.line((64, 67, 64, 79), fill=color, width=5)
    elif glyph == "TIDE":
        for y in (52, 67, 82):
            d.line([(34, y), (45, y-5), (57, y), (69, y+5), (81, y), (94, y-5)], fill=color, width=5)
    elif glyph == "GRID":
        for x in (43, 65):
            for y in (42, 64):
                d.rounded_rectangle((x, y, x+17, y+17), radius=3, outline=color, width=4)
    elif glyph == "CTRL":
        d.ellipse((41, 41, 87, 87), outline=color, width=5)
        d.ellipse((59, 59, 69, 69), fill=color)
        for a, b in (((64, 31), (64, 49)), ((64, 79), (64, 97)), ((31, 64), (49, 64)), ((79, 64), (97, 64))):
            d.line((a, b), fill=color, width=5)
    elif glyph == "HAZ":
        d.line([(64, 36), (90, 85), (38, 85), (64, 36)], fill=color, width=5)
        d.line((64, 53, 64, 68), fill=color, width=5)
        d.ellipse((61, 74, 67, 80), fill=color)
    elif glyph == "GRAV":
        d.line((64, 39, 64, 86), fill=color, width=6)
        d.line([(46, 68), (64, 86), (82, 68)], fill=color, width=6)
    elif glyph == "SPAWN":
        d.line((43, 64, 83, 64), fill=color, width=6)
        d.line([(67, 49), (83, 64), (67, 79)], fill=color, width=6)
        d.ellipse((33, 60, 41, 68), fill=color)
    elif glyph == "ZONE":
        d.rectangle((39, 39, 89, 89), outline=color, width=4)
        d.line((39, 76, 89, 53), fill=color, width=5)
    elif glyph == "FLAG":
        d.line((47, 38, 47, 90), fill=color, width=5)
        d.polygon([(49, 41), (83, 41), (75, 53), (83, 65), (49, 65)], fill=color)
    elif glyph == "CARD":
        d.rounded_rectangle((46, 37, 82, 91), radius=6, outline=color, width=5)
        d.line((54, 53, 74, 53), fill=color, width=4)
        d.line((54, 65, 74, 65), fill=color, width=4)
    elif glyph == "ATK":
        d.polygon([(69, 36), (48, 68), (63, 68), (57, 92), (81, 56), (66, 56)], fill=color)
    else:
        d.text((64, 63), glyph, anchor="mm", fill=color, font=ImageFont.load_default(size=30))
    return image.resize((64, 64), Image.Resampling.LANCZOS)


def build(audit_dir: Path, package: Path):
    original = read(audit_dir.parent / "object-art-match-research-20261104.json")
    remaining = read(audit_dir / "remaining-object-visuals-20261104.json")
    last = read(audit_dir / "last-25-visual-source-audit.json")
    jobs = {x["id"]: x for x in read(audit_dir / "thumbnail-jobs.json")["jobs"]}
    markers = {
        id: (name, color, glyph)
        for name, (color, glyph, ids) in GROUPS.items()
        for id in ids.split()
    }
    assert set(markers) | {"trap_036_storm"} == {
        id for id, row in last["items"].items() if row["classification"] == "no-direct-visual-source"
    }
    rendered = audit_dir / "rendered-thumbnails"
    atlas = Image.new("RGBA", (2048, 1024))
    icons = {}
    for i, (id, name) in enumerate(sorted(original["missing"].items())):
        provenance = {}
        if id in markers:
            group, color, glyph = markers[id]
            image, kind = badge(color, glyph), "mechanism"
            provenance = {"mechanism": group, "symbol": glyph}
        elif id == "trap_036_storm":
            source = last["items"][id]["semanticIcons"]["images"]["weather_sandstorm"]
            image, kind = normalized_image(audit_dir / source["png"]), "weather-marker"
            provenance = {"mechanism": "天气", "asset": source["container"], "sourceSha256": source["sha256"]}
        elif id in jobs and (rendered / f"{id}.png").is_file():
            job = jobs[id]
            file = rendered / f"{id}.png"
            image, kind = normalized_image(file, effect=job["kind"] == "effect"), job["kind"]
            provenance = {
                "asset": job.get("asset", job.get("source")),
                "bundle": job.get("bundle", job.get("sourceBundle")),
                "sourceSha256": sha256(file.read_bytes()).hexdigest(),
            }
        else:
            wiki = [r["image"] for r in original["wikiMatches"].get(id, []) if r["image"] in original["verifiedWikiImages"]]
            verified = [(key, x) for key, x in remaining["exports"].items() if key.startswith(id + "--") and x.get("file")]
            if wiki:
                url = wiki[0]
                h = original["verifiedWikiImages"][url]["sha256"]
                image, kind = normalized_image(audit_dir / "wiki-verified-icons" / f"{h}.png"), "wiki-avatar"
                provenance = {"url": url, "sourceSha256": h}
            elif verified:
                key, source = verified[0]
                image, kind = normalized_image(audit_dir / source["file"]), "game-ui"
                provenance = {"binding": key.split("--", 1)[1], "iconKind": source["kind"], "sourceSha256": source["sha256"]}
            else:
                raise ValueError(f"No verified visual for {id}")
        x, y = 4 + i % 28 * 72, 4 + i // 28 * 72
        atlas.alpha_composite(image, (x, y))
        label = LABELS[kind]
        if "mechanism" in provenance:
            label += " · " + provenance["mechanism"]
        icons[id] = {
            "name": name,
            "kind": kind,
            "label": label,
            "rect": [x / atlas.width, 1 - (y + 64) / atlas.height, 64 / atlas.width, 64 / atlas.height],
            **provenance,
        }
    catalog = package / "Editor" / "Catalog"
    atlas.save(catalog / "prts-object-supplement-atlas.png", optimize=True)
    counts = Counter(row["kind"] for row in icons.values())
    manifest = {"version": 1, "sourceVersion": "26-10-22-14-02-11-51447808", "counts": counts, "icons": icons}
    (catalog / "prts-object-supplement.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(dict(counts))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("audit_dir", type=Path)
    parser.add_argument("package", type=Path)
    args = parser.parse_args()
    build(args.audit_dir, args.package)
