"""Draw the Unity skeleton layer's text-free terrain pictogram atlas."""
from pathlib import Path
import json
from PIL import Image, ImageDraw

GROUPS = {
    "hand": "tile_achand", "shop": "tile_acshop", "pool-outline": "tile_act27side",
    "scene-marker": "tile_act48side", "enter": "tile_act6fun_start tile_ftstart tile_enemygoal tile_start_dqq",
    "exit": "tile_act6fun_end tile_allygoal tile_end_dqq",
    "flask": "tile_alchemy_road tile_alchemy_wall", "balloon": "tile_balloon tile_balloon_fbd",
    "luck": "tile_bnspck_road", "imprint": "tile_character tile_characterf", "fuse": "tile_cnvfuse tile_cnvfuse_fbd",
    "water": "tile_codpsea tile_xbdpsea", "fracture": "tile_corrosion_2", "creep": "tile_creep tile_creepf",
    "realm-one": "tile_dygmny_1", "realm-two": "tile_dygmny_2", "realm-three": "tile_dygmny_3",
    "shock": "tile_empshock", "flower": "tile_flower tile_flowerf", "football": "tile_football",
    "plant": "tile_ftplant_road tile_ftplant_roadf tile_ftplant_wall", "relay": "tile_green",
    "infection": "tile_infection_dqq", "resonator": "tile_magic_circle tile_magic_circle_h",
    "chain": "tile_merope tile_rarope", "intrusion": "tile_mpprts_enemy_born",
    "pollution": "tile_pollution_road tile_pollution_roadf tile_pollution_wall tile_pollution_wallf",
    "puddle": "tile_puddle", "quicksand": "tile_quicksand",
    "rabbit-in": "tile_rabbithole_in_1", "rabbit-out": "tile_rabbithole_out_1",
    "rabbit-hidden": "tile_rabbithole_out_2",
    "swap": "tile_replace_road tile_replace_wall", "sprout": "tile_richfd",
    "bridge": "tile_sandrope tile_sandrope_2", "sleep": "tile_sleep_road tile_sleep_wall",
    "steam": "tile_steam", "toxic": "tile_toxic tile_toxichill tile_toxicroad tile_toxicwall",
    "lava": "tile_volcano_strife", "clean-water": "tile_water", "boards": "tile_woodrd",
}
WHITE = (255, 255, 255, 255)
CLEAR = (255, 255, 255, 0)


def draw_symbol(key: str) -> Image.Image:
    image = Image.new("RGBA", (128, 128), CLEAR)
    d = ImageDraw.Draw(image)

    def line(*points, width=6):
        d.line(points, fill=WHITE, width=width, joint="curve")
        r = width / 2
        for x, y in (points[0], points[-1]):
            d.ellipse((x-r, y-r, x+r, y+r), fill=WHITE)

    def ring(box, width=6):
        d.ellipse(box, outline=WHITE, width=width)

    def dot(x, y, radius=5):
        d.ellipse((x-radius, y-radius, x+radius, y+radius), fill=WHITE)

    def arrow(x0, y0, x1, y1, width=6, head=15):
        dx, dy = x1-x0, y1-y0
        length = (dx*dx+dy*dy)**.5
        x, y = dx/length, dy/length
        line((x0,y0),(x1,y1),width=width)
        line((x1-head*x+.66*head*y,y1-head*y-.66*head*x),(x1,y1),
             (x1-head*x-.66*head*y,y1-head*y+.66*head*x),width=width)

    def wave(y, width=5):
        line((30,y),(42,y-6),(54,y-6),(66,y),(78,y+6),(90,y+6),(98,y+2),width=width)

    def diamond(x, y, size):
        d.polygon([(x,y-size),(x+size,y),(x,y+size),(x-size,y)], fill=WHITE)

    if key == "hand":
        for x, y in ((39,45),(55,37),(71,31)):
            d.rounded_rectangle((x,y,x+24,y+49),radius=4,outline=WHITE,width=5)
    elif key == "shop":
        line((34,55),(42,35),(86,35),(94,55),(34,55))
        line((42,59),(42,92),(86,92),(86,59))
        line((59,90),(59,72),(74,72),(74,90),width=4)
    elif key == "pool-outline":
        ring((29,42,99,88))
        wave(64,4)
    elif key == "scene-marker":
        for x, y in ((44,44),(84,44),(44,84),(84,84)):
            diamond(x,y,8)
        ring((56,56,72,72),4)
    elif key in ("enter","exit"):
        if key == "enter":
            line((77,35),(94,35),(94,93),(77,93))
            arrow(35,64,78,64)
        else:
            line((51,35),(34,35),(34,93),(51,93))
            arrow(51,64,94,64)
    elif key == "flask":
        line((51,31),(77,31))
        line((55,33),(55,51),(39,83),(44,94),(84,94),(89,83),(73,51),(73,33))
        line((47,74),(81,74),width=4)
        dot(63,61,4)
    elif key == "balloon":
        ring((42,30,86,76))
        line((63,76),(60,84),(68,90),(64,97),width=4)
    elif key == "luck":
        line((38,77),(35,48),(50,60),(64,40),(78,60),(93,48),(90,77),(38,77))
        line((44,90),(84,90),width=5)
        dot(64,40,4)
    elif key == "imprint":
        d.rounded_rectangle((37,42,91,96),radius=6,outline=WHITE,width=6)
        line((51,32),(77,32),width=5)
        line((49,62),(79,62),width=4)
        line((49,77),(69,77),width=4)
    elif key == "fuse":
        line((33,88),(52,88),(60,71),(76,71),(83,54))
        for a,b in (((84,31),(84,44)),((97,42),(90,49)),((102,56),(94,56))):
            line(a,b,width=4)
        dot(84,54,4)
    elif key == "water":
        wave(51)
        wave(72)
    elif key == "fracture":
        line((36,40),(60,55),(53,70),(77,82),(88,94))
        line((59,56),(78,46),(92,49),width=4)
        line((53,70),(36,77),width=4)
    elif key == "creep":
        for y in (48,67,86):
            line((38,y),(51,y-8),(64,y),(77,y+8),(90,y),width=5)
        dot(91,38,4)
    elif key.startswith("realm-"):
        d.rounded_rectangle((32,35,96,93),radius=9,outline=WHITE,width=5)
        count = {"realm-one":1,"realm-two":2,"realm-three":3}[key]
        for x in {1:(64,),2:(50,78),3:(44,64,84)}[count]:
            diamond(x,64,8)
    elif key == "shock":
        d.polygon([(68,28),(43,69),(61,69),(55,99),(87,54),(69,54)],fill=WHITE)
    elif key == "lava":
        for x in (48, 64, 80):
            line((x,66),(x-4,55),(x+4,44),(x,33),width=5)
        line((31,83),(47,83),(56,75),(65,93),(75,82),(96,82),width=6)
    elif key == "flower":
        for x,y in ((64,43),(83,56),(76,79),(52,79),(45,56)):
            ring((x-10,y-10,x+10,y+10),4)
        dot(64,64,6)
    elif key == "football":
        ring((32,32,96,96))
        d.polygon([(64,48),(78,58),(73,75),(55,75),(50,58)],outline=WHITE,width=4)
        for a,b in (((64,48),(64,34)),((78,58),(92,53)),((73,75),(82,87)),((55,75),(46,87)),((50,58),(36,53))):
            line(a,b,width=4)
    elif key in ("plant","sprout"):
        line((64,92),(64,60))
        line((64,71),(46,70),(38,54),(52,53),(64,62))
        line((64,64),(75,47),(91,42),(88,58),(64,70))
        if key == "sprout":
            line((44,95),(84,95),width=4)
    elif key == "relay":
        arrow(45,52,82,52,width=5,head=12)
        arrow(82,77,45,77,width=5,head=12)
        dot(34,52,4)
        dot(94,77,4)
    elif key == "infection":
        ring((40,40,88,88))
        for x,y in ((64,34),(94,64),(64,94),(34,64)):
            dot(x,y,4)
        dot(64,64,8)
    elif key == "resonator":
        ring((42,42,86,86),5)
        ring((52,52,76,76),4)
        for a,b in (((64,27),(64,39)),((89,64),(101,64)),((64,89),(64,101)),((27,64),(39,64))):
            line(a,b,width=5)
    elif key == "chain":
        d.rounded_rectangle((31,46,70,75),radius=13,outline=WHITE,width=6)
        d.rounded_rectangle((58,54,97,83),radius=13,outline=WHITE,width=6)
    elif key == "intrusion":
        for x,y,sx,sy in ((36,36,1,1),(92,36,-1,1),(36,92,1,-1),(92,92,-1,-1)):
            line((x+13*sx,y),(x,y),(x,y+13*sy),width=5)
        arrow(64,41,64,85,width=5)
    elif key == "pollution":
        line((64,33),(92,85),(36,85),(64,33))
        for x,y in ((64,58),(53,74),(75,74)):
            dot(x,y,4)
    elif key == "puddle":
        ring((34,51,94,89),5)
        wave(68,4)
    elif key == "quicksand":
        d.arc((33,32,96,97),40,350,fill=WHITE,width=6)
        d.arc((47,45,82,82),90,370,fill=WHITE,width=5)
        dot(64,65,4)
    elif key.startswith("rabbit-"):
        d.arc((32,35,96,101),180,360,fill=WHITE,width=6)
        line((32,68),(32,90),(96,90),(96,68),width=5)
        if key == "rabbit-hidden":
            for x in (49,64,79):
                dot(x,70,4)
        else:
            arrow(64,49 if key == "rabbit-in" else 83,64,83 if key == "rabbit-in" else 49,width=5,head=11)
    elif key == "swap":
        arrow(35,50,91,50,width=6,head=14)
        arrow(91,78,35,78,width=6,head=14)
    elif key == "bridge":
        line((37,40),(37,91))
        line((91,40),(91,91))
        line((37,47),(48,57),(64,64),(80,57),(91,47),width=4)
        line((37,80),(91,80))
        for x,y in ((51,59),(64,65),(77,59)):
            line((x,y),(x,80),width=3)
    elif key == "sleep":
        d.ellipse((35,33,87,93),fill=WHITE)
        d.ellipse((55,24,97,78),fill=CLEAR)
        diamond(86,43,8)
    elif key == "steam":
        for x in (47,64,81):
            line((x,87),(x-5,73),(x+4,59),(x,42),width=5)
    elif key == "toxic":
        for x,y in ((45,56),(64,46),(82,58),(53,77),(76,80)):
            ring((x-8,y-8,x+8,y+8),4)
    elif key == "clean-water":
        line((64,32),(47,55),(42,70),(48,85),(64,93),(80,85),(86,70),(81,55),(64,32))
        d.arc((53,58,75,81),25,120,fill=WHITE,width=4)
    elif key == "boards":
        for y in (42,60,78):
            d.rounded_rectangle((35,y,93,y+12),radius=3,outline=WHITE,width=4)
    else:
        raise ValueError(key)
    return image.resize((64,64),Image.Resampling.LANCZOS)


def main():
    catalog = Path(__file__).resolve().parent.parent / "Packages/io.github.lingrs-llm-researchteam.mapeditor/Editor/Catalog"
    path = catalog / "skeleton-definitions.json"
    data = json.loads(path.read_bytes())
    symbols = list(GROUPS)
    entries = data["tiles"]
    ids = [key for group in GROUPS.values() for key in group.split()]
    assert len(ids) == len(set(ids)) == 66 and len(symbols) <= 64
    atlas = Image.new("RGBA",(512,512),CLEAR)
    for index,symbol in enumerate(symbols):
        atlas.paste(draw_symbol(symbol),(64*(index%8),64*(index//8)))
        for key in GROUPS[symbol].split():
            entries[key]["content"] = ""
            entries[key]["symbol"] = symbol
    data["symbols"] = symbols
    path.write_text(json.dumps(data,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    atlas.save(catalog / "skeleton-terrain-symbols.png",optimize=True)


if __name__ == "__main__":
    main()
