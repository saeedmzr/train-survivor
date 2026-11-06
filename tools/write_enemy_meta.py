#!/usr/bin/env python3
"""Write art-work/enemies/<id>/meta.json — design + art + Unity facts in one file.

Sources merged here:
  art-work/enemies/<id>/{card,sprite,sprite_hit,sprite_dead}.png   the art
  art-work/enemies/_extract.json                                  extraction report
  art-work/enemies/_unity.json                                    atlas/prefab mapping
  finalized-for-develop/data/enemies.json                         stats and behaviour

The visual descriptions are hand-written from looking at each card (they are the part
no script can infer), the palette is measured from the cutout.
"""
import json, os, collections
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ART = os.path.join(ROOT, "art-work/enemies")
DATA = json.load(open(os.path.join(ROOT, "finalized-for-develop/data/enemies.json")))
BY_ID = {e["id"]: e for e in DATA["enemies"]}
BY_ID["walker"] = {"id": "walker", "name": "Walker", "role": "standard shambler, swarms the train",
                   "hp": 30, "speed": 2.2, "dmg": 6, "xp": 1, "armor": 0, "pack": "4–8", "lane": "rails",
                   "threat": "low", "behavior": ["walks in straight lines at the train"],
                   "counters": ["stop early and thin them", "ram"]}
BY_ID["sprinter"] = {"id": "sprinter", "name": "Sprinter", "role": "fast four-limbed runner",
                     "hp": 12, "speed": 6.5, "dmg": 4, "xp": 1, "armor": 0, "pack": "3–5", "lane": "flanks",
                     "threat": "low", "behavior": ["closes distance fast", "leaps at the lead car"],
                     "counters": ["brake immediately", "shotgun"]}
BY_ID["tank"] = {"id": "tank", "name": "Tank", "role": "bloated brute, bullet sponge",
                 "hp": 255, "speed": 1.2, "dmg": 18, "xp": 6, "armor": 2, "pack": "1", "lane": "rails",
                 "threat": "high", "behavior": ["absorbs fire meant for weaker enemies", "smashes barricades aside"],
                 "counters": ["sustained fire", "rocket", "the ram does nothing"]}

# measured from the cards; this is the part that cannot be scripted
LOOK = {
 "crawler": dict(
   silhouette="Low, long quadruped — an armoured scrap-hound with a finned back, splayed legs and a heavy jaw.",
   pose="side three-quarter, crouched to lunge, tail curling up",
   features=["spine of jagged plates with glowing green core vents",
             "exposed shoulder actuator and piston legs",
             "needle-tooth jaw with dripping saliva",
             "chain-link tail tip"]),
 "bloater": dict(
   silhouette="Spherical gas-bag body on four insectile legs, dominated by a huge mouth and two saucer eyes.",
   pose="front three-quarter, standing wide, flanked by vapour",
   features=["riveted steel belly with glowing green and purple blisters",
             "stitched skull plate over the mouth",
             "hydraulic leg struts",
             "constant vapour leak"]),
 "bulwark": dict(
   silhouette="Heavy humanoid hunched behind a door-sized rail sign used as a shield.",
   pose="front view, planted, shield towards camera",
   features=["shield with yellow/black hazard chevrons and rivets",
             "spiked shoulder pad",
             "single glowing eye slit",
             "plated boots and braced legs"]),
 "drillmaw": dict(
   silhouette="Drill-headed worm rearing out of the ballast: conical screw head, gear collar, clawed forelimbs.",
   pose="front three-quarter, emerging from rubble",
   features=["star-shaped drill with a glowing amber core",
             "stacked gear rings behind the head",
             "hazard-striped collar",
             "churned dirt and sparks at the base"]),
 "glider": dict(
   silhouette="Small winged drone-creature: barrel body, two large eyes, rag wings, dangling talons.",
   pose="front view, airborne, thrusters firing",
   features=["patched canvas wings on steel spars",
             "twin blue thruster flames",
             "clawed feet tucked under the body",
             "stub exhaust pipe on the back"]),
 "lantern": dict(
   silhouette="Tall robed skeleton conductor in a wide-brimmed witch hat, holding a caged lantern on a chain.",
   pose="three-quarter front, standing, lamp arm raised",
   features=["caged lantern with a cyan flame on a chain",
             "glowing cyan eye sockets in a stitched skull",
             "mechanical forearm and exposed rib cage",
             "rope belt over a ragged robe"]),
 "rimback": dict(
   silhouette="Massive armoured beast, low and wide, crowned with ice spikes over the back and a plated head.",
   pose="front three-quarter, crouched, shoulders forward",
   features=["ridge of pale-blue ice crystals",
             "frost-breathing jaw with tusks",
             "exhaust stacks venting smoke",
             "cracked rust plates on the shoulders"]),
 "shrieker": dict(
   silhouette="Skeletal zombie with two gramophone horns growing out of its head, arms flung out, screaming.",
   pose="front view, mid-scream",
   features=["twin brass horn cones",
             "concentric green sound rings",
             "tattered coat over exposed ribs",
             "ember sparks off the shoulders"]),
 "spitter": dict(
   silhouette="Quadruped carrying a huge cannon-like acid sac on its back, aimed forward, jaws open.",
   pose="side three-quarter, braced on four legs",
   features=["glowing green tank/cannon strapped to the spine",
             "drip-covered needle teeth",
             "hazard-yellow eye",
             "long tail counterweight"]),
 "sprinter": dict(
   silhouette="Emaciated running zombie caught mid-stride, on fire.",
   pose="side view, leaning into a sprint",
   features=["full-body flame silhouette",
             "burning feet trailing embers",
             "exposed ribs and shoulder blade",
             "glowing orange eyes"]),
 "tank": dict(
   silhouette="Hulking humanoid brute in work overalls, one arm in an armoured gauntlet.",
   pose="front view, planted, fists closed",
   features=["chains and padlock across the chest",
             "spiked shoulder plate",
             "riveted bracer on the left arm",
             "blood-flecked skin, pale glowing eyes"]),
 "walker": dict(
   silhouette="Shambling zombie in a torn shirt and trousers, arms hanging, head tilted.",
   pose="front view, mid-step",
   features=["glowing green wound across the torso",
             "torn sleeves and hanging jaw",
             "ember glow on one shoulder",
             "sickly green highlights over washed grey"]),
}


def palette_of(path, n=6, min_luma=52):
    """Hues of the creature: near-black outline and shadow pixels are dropped first."""
    im = Image.open(path).convert("RGBA")
    px = []
    for r, g, b, a in im.get_flattened_data():
        if a < 140:
            continue
        if 0.299 * r + 0.587 * g + 0.114 * b < min_luma:
            continue
        px.append((r, g, b, a))
    if not px:
        return []
    q = Image.new("RGB", (len(px), 1))
    q.putdata([(r, g, b) for r, g, b, _ in px])
    q = q.quantize(colors=n, method=Image.MEDIANCUT).convert("RGB")
    counts = collections.Counter(q.get_flattened_data())
    return ["#%02X%02X%02X" % c for c, _ in counts.most_common(n)]


def main():
    ext = json.load(open(os.path.join(ART, "_extract.json")))
    uni = json.load(open(os.path.join(ART, "_unity.json")))
    catalog = []
    for art_id, u in uni["sprites"].items():
        d = os.path.join(ART, art_id)
        gid = u["id"]
        design = BY_ID.get(gid, {})
        sprite = os.path.join(d, "sprite.png")
        if not os.path.exists(sprite):
            continue
        look = LOOK.get(art_id, {})
        im = Image.open(sprite)
        meta = {
            "id": gid,
            "artFolder": art_id,
            "name": design.get("name", art_id.title()),
            "type": "normal enemy",
            "biomes": ["burning-metropolis", "frozen-canyon", "toxic-swamp"],
            "stats": {k: design.get(k) for k in ("hp", "speed", "dmg", "xp", "armor", "pack", "lane", "threat")},
            "ability": {"name": design.get("name", ""), "role": design.get("role", ""),
                        "behaviour": design.get("behavior", []), "counters": design.get("counters", [])},
            "art": {
                "sourceCard": "art-work/enemies/%s/card.png" % art_id,
                "sprites": {
                    "sprite": {"file": "art-work/enemies/%s/sprite.png" % art_id, "px": list(im.size),
                               "coverage": ext.get(art_id, {}).get("coverage")},
                    "hit": "art-work/enemies/%s/sprite_hit.png" % art_id,
                    "dead": "art-work/enemies/%s/sprite_dead.png" % art_id,
                    "preview": "art-work/enemies/%s/cutout_preview.png" % art_id,
                },
                "extraction": {k: ext.get(art_id, {}).get(k) for k in
                               ("method", "sourceBox", "components", "blob", "trimmed")},
                "palette": palette_of(sprite),
                "silhouette": look.get("silhouette"),
                "pose": look.get("pose"),
                "features": look.get("features", []),
                "notes": "Side-on card art; the creature is cut from the card, title and stat panel removed.",
            },
            "unity": {
                "sprite": u["sprite"], "heightM": u["heightM"], "atlasPx": u["px"],
                "atlas": "unity/Assets/_Project/Art/Resources/Art/atlas_enemies.png",
                "individual": "unity/Assets/_Project/Art/Sprites/Enemies/%s.png" % art_id,
                "prefab": "unity/Assets/_Project/Prefabs/Enemies/%s.prefab" % gid,
            },
            "design": {"dataFile": "finalized-for-develop/data/enemies.json", "card": "finalized-for-develop/enemies/cards/card_%s.png" % gid},
        }
        json.dump(meta, open(os.path.join(d, "meta.json"), "w"), indent=1, ensure_ascii=False)
        catalog.append(meta)
        print("%-9s %-8s %s" % (art_id, gid, ", ".join(meta["art"]["palette"][:3])))

    json.dump({"source": "art-work/enemies", "count": len(catalog),
               "enemies": [{k: m[k] for k in ("id", "artFolder", "name", "stats", "unity", "art")} for m in catalog]},
              open(os.path.join(ART, "_catalog.json"), "w"), indent=1)
    print("wrote", len(catalog), "meta.json + _catalog.json")


if __name__ == "__main__":
    main()
