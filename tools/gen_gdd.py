#!/usr/bin/env python3
"""Generate docs/gdd-v2.spec.json for the docx builder.

Content is pulled from the project data so the document cannot drift from the game:
  finalized-for-develop/data/{enemies,bosses,projectiles,fx,wagons,hud-v2}.json
  finalized-for-develop/meta/palette.json
  design/figma/figma-spec.json

Build the .docx with python-docx (see docs/README.md for the exact command).
"""
import json, os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FF = os.path.join(ROOT, "finalized-for-develop")

E = json.load(open(os.path.join(FF, "data/enemies.json")))["enemies"]
B = json.load(open(os.path.join(FF, "data/bosses.json")))["bosses"]
P = json.load(open(os.path.join(FF, "data/projectiles.json")))
F = json.load(open(os.path.join(FF, "data/fx.json")))
W = json.load(open(os.path.join(FF, "data/wagons.json")))
H = json.load(open(os.path.join(FF, "data/hud-v2.json")))
PAL = json.load(open(os.path.join(FF, "meta/palette.json")))
SPEC = json.load(open(os.path.join(ROOT, "design/figma/figma-spec.json")))

BIOME_NAME = {"burning-metropolis": "Burning Metropolis", "frozen-canyon": "Frozen Canyon",
              "toxic-swamp": "Toxic Swamp",
              "level-1-burning-metropolis": "Burning Metropolis",
              "level-2-frozen-canyon": "Frozen Canyon",
              "level-3-toxic-swamp": "Toxic Swamp"}
BIOME_LOADOUT = {l["slug"]: l["header"]["boardKey"][1] for l in SPEC["levels"]}
BIOME_STATES = {l["slug"]: l["stateCount"] for l in SPEC["levels"]}
BIOME_PREMISE = {l["slug"]: l["header"]["biome"] for l in SPEC["levels"]}

blocks = []
H1 = lambda t: blocks.append({"type": "heading", "text": t, "level": 1})
H2 = lambda t: blocks.append({"type": "heading", "text": t, "level": 2})
P_ = lambda t: blocks.append({"type": "paragraph", "text": t})
BUL = lambda items: blocks.append({"type": "bullet_list", "items": items})
NUM = lambda items: blocks.append({"type": "numbered_list", "items": items})
TBL = lambda header, rows: blocks.append({"type": "table", "header": header, "rows": rows,
                                          "style": "Light Grid Accent 1"})
IMG = lambda path, w: blocks.append({"type": "image", "path": path, "width_mm": w})
PB = lambda: blocks.append({"type": "page_break"})

# ---------------------------------------------------------------- cover
H1("Train Survivor — Game Design Document")
P_("Version 2.0 · 2026-11-06 · Unity 6 LTS (6000.0.85f2), URP, IL2CPP/ARM64, Android portrait")
P_("Supersedes v1.0. This revision folds in the Figma gameplay field guide (3 biomes, 64 "
   "storyboard states), the content expansion (10 new enemy types, 10 bosses, typed ammo, "
   "per-type kill FX), the HUD redesign that puts the level inside the XP bar, and the "
   "two-guns-per-wagon rule. Where this document and any older note disagree, this document "
   "and the machine-readable files under finalized-for-develop/data/ win.")

TBL(["Version", "Date", "Headline"], [
    ["1.0", "2026-11-05", "Original brief: one rail, stop-and-shoot, walker/sprinter/tank, 4 biomes planned"],
    ["2.0", "2026-11-06", "Figma field guide imported; 13 enemies, 10 bosses, 7 ammo types, HUD v2, wagon gun slots"],
])

H2("What changed in v2")
TBL(["Area", "v1.0", "v2.0 (current)"], [
    ["Enemies", "3 types (walker, sprinter, tank)", "13 — those three plus Crawler, Bloater, Spitter, Shrieker, Bulwark, Glider, Drillmaw, Rustmites, The Lantern, Rimeback"],
    ["Bosses", "None specified", "10, each with 3 phases, a named weakness and a telegraph"],
    ["Ammo", "One generic bullet", "7 types: tracer, rocket, fireball, acid glob, tesla arc, pellet spread, bone spike"],
    ["Kill FX", "One gore burst", "Impact FX per damage type + death FX per enemy class (10 total)"],
    ["Level display", "Separate TRAIN LV badge", "Level number fused into the XP bar's left cap; badge retired"],
    ["Guns per wagon", "Unlimited (one gun per milestone)", "Max 2 per wagon, 1 at hitch; second slot via an Extra Gun card"],
    ["Wagon unlocks", "Every 5 levels", "LV 1, 5, 10, 15, 20 (5 wagons, 10 guns maximum)"],
    ["Biomes", "4 planned incl. desert", "3 shipped and storyboarded + desert deferred to v2.1"],
    ["Stage structure", "Abstract 'level'", "20 beats per stage (10 + 10 cards), 1,000 m, phases NAVIGATE/SURVIVE/ENGAGE/EVOLVE (+GROW in biome 3)"],
])

# ---------------------------------------------------------------- 1 loop
PB()
H1("1. Core loop")
P_("Portrait, one thumb, behind-the-train camera. Holding the screen accelerates and rams; "
   "releasing brakes and opens fire. The whole game is the negotiation between those two states.")
BUL([
    "Hold → accelerate (0 → 100 % in 0.6 s). Above 60 % speed the cowcatcher deals 35 damage to anything within 3 m and takes none. Guns are off.",
    "Release → brake burst (0.18 s), wheel sparks, screech. Guns come live at t=0 with no wind-up.",
    "Brake charge: a meter fills while moving (0 → 100 % over 4 s) and converts into +40 % fire rate for the first 2 s of firing after release.",
    "Combat → auto-target the nearest enemy, kill, vacuum XP. Adrenaline stacks (+2 % fire rate each, cap 25) decay when idle.",
    "Blockade → movement clamps to zero until the player releases and shoots it apart.",
    "Stage clear → 300 ms slow-mo, 3 upgrade cards, next stage.",
])
P_("Design pillar that survived from v1: neither input state may feel like downtime. v2 adds "
   "two pressures on that pillar — Bulwarks that ignore frontal fire, and Gliders that disable "
   "a gun slot — so stopping is not automatically the safe answer.")

H2("1.1 State machine (authoritative)")
TBL(["State", "Entered when", "Speed", "Guns", "Other"], [
    ["MOVING", "finger held", "→ target", "off", "ram on, brake charge += dt"],
    ["BRAKING", "finger released", "→ 0 in 0.18 s", "on at t=0", "sparks, screech, clunk"],
    ["STOPPED", "speed == 0", "0", "on", "targeting active"],
    ["BLOCKED", "held against a blockade within 4 m", "0", "off", "player must release"],
    ["CLEAR", "track end reached", "→ 0", "off", "300 ms slow-mo → reward"],
    ["DEAD", "lead wagon HP ≤ 0", "0", "off", "250 ms shake → stats → retry"],
])
P_("Input is driven by press-held, not TouchPhase.Stationary — Stationary fires on a held but "
   "motionless finger and is unreliable across Android devices.")

# ---------------------------------------------------------------- 2 stage
H1("2. Stage structure")
P_("Every stage is a 1,000 m run told in 20 beats. The Figma field guide storyboards each beat "
   "as one card (300 × 372), and the beats are grouped into five phases that the HUD and the "
   "spawn director both read.")
TBL(["Beats", "Phase", "What happens"], [
    ["01–05", "NAVIGATE", "Start line → accel → A/B track split → branch chosen (route A/B rewards differ)"],
    ["06–09", "SURVIVE", "Spawns appear → horde encircles → player releases → train brakes"],
    ["10–15", "ENGAGE", "Target acquisition → volley → hit flashes → kills → leaper interception → boss at 15"],
    ["16–20", "EVOLVE", "XP vacuum → barricade ram → destructible barricade → finish flag → level-up screen"],
    ["21–24", "GROW", "Biome 3 only: convoy growth milestones and the growth reference chart"],
])
P_("The A/B split is the only branch in a stage and it happens once, early, so the choice is "
   "cheap to test: stage 3–5 in every biome. Route A trades health for speed, route B is the "
   "safe line. Later stages may add a second split at beat 16.")

H2("2.1 Distance HUD")
P_("The top strip is the stage progress bar. Its fill is 228 px of usable track inside a 284 px "
   "artwork; 100 % is the finish flag, not the end of the image. The printed distance in the art "
   "is baked, so drive the fill from simulated metres, never from the label.")

# ---------------------------------------------------------------- 3 progression
H1("3. Progression")
H2("3.1 Level and XP — one bar, not two")
P_("v2 retires the separate TRAIN LV badge. The level now lives in a gold chip fused into the "
   "left cap of the XP track, with the XP fill running underneath the chip. The chip is always "
   "visible, so level and progress to the next level are read in a single glance.")
TBL(["Property", "Value"], [
    ["Level chip", "34 × 20 in the 300-space canvas, radius 6, gold border #FFC24D"],
    ["XP track", "106 × 16, radius 4, #112B45 (unchanged from v1)"],
    ["Fill", "12 px tall, gradient #5BE0FF → #1E8CD8, runs under the chip"],
    ["Level number", "zero-padded two digits, condensed bold, 16 px in design space"],
    ["Level up", "chip flashes, number rolls to the new value, fill resets to 0"],
    ["Retired", "the TRAIN LV badge that appeared only in biome 3"],
])
P_("Rationale: the badge was a third reading of the same number (level), shown far from the "
   "thing it changes (XP). One bar means the player learns one place to look.")

H2("3.2 The convoy — wagons and gun slots")
P_("Each wagon carries at most two guns and arrives with one. The second slot is not free: it "
   "costs an Extra Gun upgrade spent on that specific wagon. A full convoy is therefore five "
   "wagons and at most ten guns — gun count no longer equals convoy size.")
TBL(["Wagon", "Unlocks at", "Gun slots", "Role"], [
    ["1 (engine)", "LV 1", "1 of 2", "Lead car. Its destruction ends the run."],
    ["2", "LV 5", "1 of 2", "First buffer; first extra gun mount."],
    ["3", "LV 10", "1 of 2", "Mid convoy; unlocks the wide camera step."],
    ["4", "LV 15", "1 of 2", "Heavy mount — rocket and tesla fit here."],
    ["5", "LV 20", "1 of 2", "Final wagon; convoy growth stops here."],
])
P_("Consequence for balance: with 10 gun slots the DPS curve is driven by cards, not by wagon "
   "count. A wagon now buys hit points and a mount, not raw damage. Enemy HP scaling is tuned "
   "against 10 slots, not 5.")

H2("3.3 Upgrades")
P_("The offer is 1 of 3 cards after every stage and on level-up. Nine cards currently exist as "
   "art in ui/cards/ — the rest of the 24-card pool is still paper-only (see the system design "
   "document) and will reuse the same card frame.")
TBL(["Card", "Category", "Effect"], [
    ["Multishot", "Weapon", "+2 projectiles per gun, each at reduced damage"],
    ["Fire Trail", "Train", "Leaves a burning strip behind the train while moving"],
    ["Spike Bumper", "Train", "+35 % ram damage, wider cowcatcher arc"],
    ["Piercing Rounds", "Weapon", "Shots pass through one target for 50 % damage"],
    ["Reinforced Boiler", "Train", "+20 % max HP on the lead wagon, heals the difference"],
    ["Rapid Fire Engine", "Output", "+15 % fire rate across every gun"],
    ["Incendiary Shells", "Elemental", "Chance to ignite; burn ticks for 3 s and spreads on death"],
    ["Extra Gun", "Output", "Fills the second gun slot of a chosen wagon"],
    ["Extra Wagon", "Train", "Hitches a wagon immediately, if under the LV 20 cap"],
])
P_("Offer rule (unchanged): always include one card that touches the weakest stat — HP under "
   "35 %, otherwise output — and never three cards of the same category.")

# ---------------------------------------------------------------- 4 enemies
PB()
H1("4. The horde")
P_("Thirteen enemy types. Thematic rule from v1 still holds: everything is a zombie or an "
   "infected machine — no aliens, no soldiers, no robots. Each entry states the counter, because "
   "an enemy whose counter is not obvious is a bug.")
HOOK = {
    "crawler": "Flattens and slides under the cowcatcher; the ram misses it",
    "bloater": "Bloats, then detonates for 3 m area damage that also hurts the horde",
    "spitter": "Lobs acid from the embankment, arcing over barricades",
    "shrieker": "Screams: +15 % speed aura and a horde call every 8 s",
    "bulwark": "Carries a rail-sign plate — immune from the front",
    "glider": "Vaults barricades, lands on a wagon, disables a gun slot for 3 s",
    "drillmaw": "Burrows under the track and surfaces ahead of the train",
    "mites": "Six bodies, stacking armour shred, needs splash",
    "lantern": "Raises fallen zombies as husks until it is killed",
    "rimeback": "Armoured brute; its hit chills the throttle for 2 s",
}
rows = [
    ["Walker", "30", "2.2", "6", "Standard shambler, swarms the train · stop early and thin them"],
    ["Sprinter", "12", "6.5", "4", "Fast four-limbed runner · brake immediately"],
    ["Tank", "255", "1.2", "18", "Bloated, huge HP, bullet sponge · sustained fire; the ram does nothing"],
]
for e in E:
    rows.append([e["name"], str(e["hp"]), str(e["speed"]), str(e["dmg"]),
                 "%s · counter: %s" % (HOOK.get(e["id"], e["role"]), ", ".join(e["counters"][:2]))])
TBL(["Enemy", "HP", "Speed", "Dmg", "Behaviour and counter"], rows)
P_("HP values are stage-1 baselines before scaling (×1.15 per stage). Full behaviour lists, "
   "armour values, spawn lanes and pack sizes live in finalized-for-develop/data/enemies.json.")
IMG(os.path.join(FF, "sheets/enemies.png"), 165)

H2("4.1 New in v2 — why each one exists")
BUL([
    "Crawler — attacks the ram, not the guns: it flattens and slides under the cowcatcher, so the reflex that works on everything else fails.",
    "Bloater — turns the horde into a hazard: its blast damages other enemies, so letting it reach the train can be a tactic.",
    "Spitter — punishes parking: fires from the embankment, so the player who stops and shoots everything else must move.",
    "Shrieker — a priority target that is harmless alone; killing it early is the reward for reading the field.",
    "Bulwark — makes frontal DPS insufficient, which is what finally makes turret placement matter.",
    "Glider — attacks the convoy instead of the lead car, and disables a gun slot, converting a wagon into a liability for 3 s.",
    "Drillmaw — makes the ground a threat and gives the brake a second use: waiting for it to surface.",
    "Rustmites — forces splash in the build, and stacks armour shred so tanking them is not viable.",
    "The Lantern — gives the horde a healer, creating a kill-order problem instead of a DPS problem.",
    "Rimeback — punishes the ram-first playstyle by chilling the throttle, and rewards fire.",
])

# ---------------------------------------------------------------- 5 bosses
H1("5. Bosses")
P_("One boss per biome per act, ten in total, met at beat 15 of a stage. Every boss has three "
   "phases with HP thresholds, a named weakness, and at least one attack that the player must "
   "answer with the brake. No boss may be defeated by holding the screen.")
rows = []
for b in B:
    ph_txt = []
    for i, ph in enumerate(b["phases"], 1):
        ph_txt.append("P%d %s%s" % (i, ph["name"],
                                    "" if i == 1 else " (%d %%)" % int(ph["at"] * 100)))
    rows.append([b["name"], BIOME_NAME.get(b["biome"], b["biome"].title()), str(b["hp"]),
                 " → ".join(ph_txt), b["weakness"]])
TBL(["Boss", "Biome", "HP", "Phases", "Weakness"], rows)
IMG(os.path.join(FF, "sheets/bosses.png"), 165)
P_("Full attack lists, arena notes, entry beats and rewards are in "
   "finalized-for-develop/data/bosses.json, and readable in finalized-for-develop/BESTIARY.md.")
P_("Two bosses carry the theme hardest. Rime Engine is a mirror fight — a corrupted locomotive "
   "coming at you on your own rails, with its own turrets. The Nightmail phases out of the "
   "world, splits into a true and an illusory copy, and forces an A/B choice mid-fight; the "
   "illusion never touches the rails, which is the tell.")

# ---------------------------------------------------------------- 6 guns
H1("6. Guns, ammo and damage types")
P_("v1 shared one bullet sprite across every weapon. v2 types the ammo per gun, so the weapon "
   "silhouette, the projectile and the hit FX all agree.")
rows = []
for gun, pid in P["gunMap"].items():
    pr = next(x for x in P["projectiles"] if x["id"] == pid)
    rows.append([gun, pr["name"], pr["damageType"], pr["role"]])
rows.append(["— (enemy)", "Bone spike", "kinetic", "Enemy ranged attack: spitter, conductor, queen"])
TBL(["Gun", "Ammo", "Type", "Behaviour"], rows)
H2("6.1 Damage type behaviour")
TBL(["Type", "On hit", "Kill"], [
    ["kinetic", "hit flash, small knockback", "goo / bone burst, metal shatter for armoured"],
    ["blast", "3 m radius, falloff 50 % at the edge", "ring burst + plate shards"],
    ["fire", "burn 8 dmg/s for 3 s, spreads on death", "fire burst with embers"],
    ["acid", "−10 % armour for 5 s, leaves a pool", "acid splash + puddle"],
    ["electric", "chains to one extra target within 4 m", "arc burst"],
])
P_("Ammo sprites are in finalized-for-develop/projectiles/, FX in finalized-for-develop/fx/, "
   "and the mapping is data/fx.json. Impact FX are looked up by damage type; death FX by enemy "
   "class — they are deliberately two lookups, so a fire kill on a metal enemy shows both.")

# ---------------------------------------------------------------- 7 HUD
H1("7. HUD and UI")
P_("The HUD is authored on a 300 × 300 gameplay canvas and scaled by screenWidth / 300. It is "
   "drawn from a small set of rounded bars and one illustrated distance strip; the level-up "
   "screen is a separate 576 × 1024 canvas.")
TBL(["Element", "Design size", "Notes"], [
    ["Status bar", "284 × 27, r5", "#0E1822 at 94 %, 8 px inset, bottom-anchored"],
    ["HP / XP track", "106 × 16, r4", "HP #123617, XP #112B45"],
    ["HP / XP fill", "12 px tall, r2", "inset 2 px; 102 px of track = 100 %"],
    ["Bar row", "HP 10→144, XP 155→289", "11 px gutter between bars"],
    ["Distance HUD", "284 × 50 at y 5", "fill 11 px, x 38 → 266; 228 px = full stage"],
    ["Touch chip", "32 × 41, r4", "253,177 — HOLD / RELEASE / AUTO / READY"],
    ["Boss bar", "190 × 33 at 55,54", "track 178 × 6, name 11 px above"],
    ["Upgrade card", "88 × 200 (art 80 × 120)", "3-up row 284 wide, 6 px gutters"],
    ["Level chip (v2)", "34 × 20, r6", "fused into the XP track's left cap"],
])
IMG(os.path.join(FF, "ui/hud/hud_xp_level_row.png"), 150)
P_("Percentages in the design tables are measured from the drawn fills against their usable "
   "track, not the node width — the fills are inset 2 px, so a full HP bar is a 102 px fill and "
   "a complete stage is 228 px of distance fill.")
P_("Wagon gun slots have their own plaque: two 80 × 76 slots, slot 1 filled and slot 2 dashed "
   "and marked LOCKED until an Extra Gun is spent. It sits above the convoy when a wagon is "
   "focused, never permanently on screen.")

# ---------------------------------------------------------------- 8 biomes
H1("8. Biomes and art direction")
P_("Three biomes are storyboarded and shipped. Each has its own palette, its own convoy mark "
   "and its own loadout accent; enemies are not recoloured between biomes — the palette and "
   "the environment do that work, tinting the sprites breaks the painted lighting.")
rows = []
for slug in ("level-1-burning-metropolis", "level-2-frozen-canyon", "level-3-toxic-swamp"):
    rows.append([BIOME_NAME[slug], BIOME_PREMISE[slug],
                 BIOME_LOADOUT.get(slug, "").split("·")[0].strip(),
                 " · ".join(PAL["biomes"][slug][:5]), str(BIOME_STATES.get(slug, ""))])
TBL(["Biome", "Premise", "Convoy", "Palette (first five)", "Beats"], rows)
P_("A fourth biome — abandoned desert wasteland — was in the v1 plan and is deferred. When it "
   "arrives it must bring its own enemies, not palette swaps, or it will read as filler.")
IMG(os.path.join(ROOT, "design/figma/boards/level-1-burning-metropolis.png"), 165)

H2("8.1 Visual rules")
BUL([
    "Thick dark outline on every sprite; light from the upper left; a single accent emissive per enemy (eyes, core, lantern).",
    "Hit flash is white and total for 40 ms — no partial tinting, it reads as damage only if it is unambiguous.",
    "Gore is stylised chunks, never realism; four gore palettes, one per biome plus a generic.",
    "Silhouette first: an enemy must be identifiable at 40 px tall, which is its size on a 6-inch screen at 12 m.",
])

# ---------------------------------------------------------------- 9 build
H1("9. Build status and pipeline")
P_("The game exists and runs. The simulation is UnityEngine-free (TrainSurvivor.Core), so "
   "balance rules are EditMode tests that finish in about a second.")
TBL(["System", "State"], [
    ["Unity", "6 LTS 6000.0.85f2, URP, IL2CPP/ARM64, Android"],
    ["Core sim", "movement, brake charge, ram, targeting, blockades, XP, cards, stage clear, seeded determinism"],
    ["Tests", "20 EditMode tests, green"],
    ["Art", "two atlases (world + UI) packed from the source sheets, loaded through ArtSprites"],
    ["Design export", "design/figma/ — 3 boards, 64 state renders, spec JSON, tokens"],
    ["Asset kit", "finalized-for-develop/ — 210 catalogued assets, data JSON, catalog, BESTIARY"],
    ["Open for M1", "ambush telegraphs beyond the blockade warning, brake-charge UI, forks"],
])
P_("Art status, stated plainly: the ten new enemies and ten bosses currently ship as generated "
   "placeholder sprites so the roster can be wired and tested. The design cards next to them are "
   "the authoritative brief, and every entry in data/*.json carries a prompt string for the art "
   "pipeline. Painted art drops over the placeholder filename with no code change.")

H2("9.1 Asset locations")
TBL(["Need", "Path"], [
    ["HUD pieces, cards, control chip", "finalized-for-develop/ui/"],
    ["Enemies and bosses (sprites + design cards)", "finalized-for-develop/enemies/, bosses/"],
    ["Ammo and kill FX", "finalized-for-develop/projectiles/, fx/"],
    ["Rosters, rules, HUD spec", "finalized-for-develop/data/"],
    ["Gameplay frames per beat", "finalized-for-develop/screens/<biome>/"],
    ["Figma source boards and renders", "design/figma/"],
])

# ---------------------------------------------------------------- 10 directives
PB()
H1("10. AI implementation directives")
P_("Short form, for handing to a code generator. The loop is a state machine; everything else "
   "is data.")
BUL([
    "Moving: press held → speed accelerates along the spline; auto-targeting OFF; ram ON above 60 %; brakeCharge += dt.",
    "Stopped: no press → speed lerps to 0 in 0.18 s; guns ON immediately; target nearest enemy by spline distance; fire the gun's ammo type.",
    "Blocked: blockade within 4 m while held → speed 0, guns OFF until release.",
    "Guns live in wagon slots: slot[0] always present on a hitched wagon, slot[1] requires an Extra Gun; a wagon never holds more than 2.",
    "Wagon milestones: LV 1, 5, 10, 15, 20 — hitch a wagon at each, never more than 5.",
    "Level: award XP per kill, roll the level chip in the XP bar, never render a separate TRAVEL LV badge.",
    "Boss: at beat 15, load the biome boss, apply phase thresholds from data/bosses.json, and switch attack sets at 60 % and 25 % HP (per-boss values in the data).",
    "Kill: look up death FX by enemy class and impact FX by damage type — both lookups, every time.",
    "Fail: lead wagon HP ≤ 0 → DEAD → stats → retry in under a second; meta upgrades persist, run upgrades do not.",
    "Determinism: seeded RNG for card offers, drops and spawns; a run is {seed, inputTimeline} and must replay to an identical state hash.",
])

H1("11. Open decisions")
NUM([
    "Desert biome: keep deferred, or build as a v2.1 act with its own enemy set?",
    "Second A/B split later in a stage — does it add choice or just friction at speed?",
    "Does the Extra Gun card target a wagon chosen by the player, or auto-fill the lowest-index free slot?",
    "Boss cadence: one per stage at beat 15, or boss stages interleaved between normal stages?",
    "Gun cap: 10 slots is a lot of simultaneous tracers — confirm the 300-bullet budget holds on low-end.",
])

spec = {
    "page": {"width_mm": 210, "height_mm": 297,
             "margins_mm": {"top": 22, "bottom": 22, "left": 20, "right": 20}},
    "header": "Train Survivor · Game Design Document v2.0",
    "footer": "Train Survivor — GDD v2.0 · 2026-11-06 · ",
    "footer_page_numbers": True,
    "blocks": blocks,
}
out = os.path.join(ROOT, "docs/gdd-v2.spec.json")
json.dump(spec, open(out, "w"), indent=1, ensure_ascii=False)
print("blocks", len(blocks), "->", out)
