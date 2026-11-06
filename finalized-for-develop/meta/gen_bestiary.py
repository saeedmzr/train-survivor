#!/usr/bin/env python3
"""Render BESTIARY.md (human-readable) from data/enemies.json + data/bosses.json."""
import json, os

OUT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
E = json.load(open(os.path.join(OUT, "data/enemies.json")))["enemies"]
B = json.load(open(os.path.join(OUT, "data/bosses.json")))["bosses"]
P = json.load(open(os.path.join(OUT, "data/projectiles.json")))
F = json.load(open(os.path.join(OUT, "data/fx.json")))
W = json.load(open(os.path.join(OUT, "data/wagons.json")))

L = ["# Bestiary & combat sheet", "",
     "Generated from `data/*.json` — that is the authoritative copy, this is the readable one.",
     "", "## Normal enemies (10 new)", "",
     "| # | Enemy | Role | HP | SPD | DMG | XP | Armour | Pack |",
     "|---|-------|------|----|-----|-----|----|--------|------|"]
for i, e in enumerate(E, 1):
    L.append("| %02d | **%s** — %s | %s | %s | %s | %s | %s | %s | %s |" % (
        i, e["name"], e["lane"], e["role"], e["hp"], e["speed"], e["dmg"], e["xp"], e["armor"], e["pack"]))
L.append("")
for i, e in enumerate(E, 1):
    L += ["### %02d · %s" % (i, e["name"]), "",
          "*%s*  " % e["role"], "",
          "- **Stats** — HP %s · speed %s m/s · damage %s · XP %s · armour %s · threat **%s**" % (
              e["hp"], e["speed"], e["dmg"], e["xp"], e["armor"], e["threat"]),
          "- **Lane** — %s · spawns in packs of %s" % (e["lane"], e["pack"])]
    for b in e["behavior"]:
        L.append("- %s" % b)
    L += ["- **Counters** — %s" % ", ".join(e["counters"]),
          "- Assets — `%s` · card `%s`" % (e["sprite"], e["card"]), ""]

L += ["## Bosses (10)", "",
      "| # | Boss | Biome | HP | Phases | Reward |", "|---|------|-------|----|--------|--------|"]
for i, b in enumerate(B, 1):
    L.append("| %02d | **%s** — %s | %s | %s | %s | %s |" % (
        i, b["name"], b["role"], b["biome"], b["hp"], len(b["phases"]), b["reward"]))
L.append("")
for i, b in enumerate(B, 1):
    L += ["### %02d · %s" % (i, b["name"]), "",
          "*%s*  " % b["role"], "",
          "- **Biome** — %s · **HP** %s · **arena** %s" % (b["biome"], b["hp"], b["arena"]),
          "- **Entry** — %s" % b["entry"],
          "- **Weakness** — %s" % b["weakness"],
          "- **Tell** — %s" % b["attackNote"],
          "- **Reward** — %s" % b["reward"], ""]
    for j, ph in enumerate(b["phases"], 1):
        L.append("**Phase %d — %s** (from %d %% HP)" % (j, ph["name"], int(ph["at"] * 100)))
        for a in ph["attacks"]:
            L.append("- %s" % a)
        L.append("")
    L += ["Assets — `%s` · card `%s`" % (b["sprite"], b["card"]), ""]

L += ["## Ammo → gun mapping", "", "| Gun | Ammo | Damage type |", "|---|---|---|"]
byid = {p["id"]: p for p in P["projectiles"]}
for gun, pid in P["gunMap"].items():
    L.append("| %s | %s (`projectiles/proj_%s.png`) | %s |" % (gun, byid[pid]["name"], pid, byid[pid]["damageType"]))
L += ["", "Enemy ranged attacks fire `bone_spike` (spitter, conductor, queen) unless noted.", ""]

L += ["## Kill & impact FX", "", "| Damage type | Impact | | Enemy | Death FX |", "|---|---|---|---|---|"]
imp = list(F["impactByDamageType"].items())
deaths = list(F["deathByEnemy"].items())
for i in range(max(len(imp), len(deaths))):
    a = "| `%s` | `%s` |" % imp[i] if i < len(imp) else "| | |"
    b = "| `%s` | `%s` |" % deaths[i] if i < len(deaths) else "| | |"
    L.append(a + b)
L += ["", "## Wagon gun rule", "",
      "- Each wagon holds **at most %d guns** and arrives with **%d**." % (
          W["rule"]["gunSlotsMax"], W["rule"]["gunSlotsAtStart"]),
      "- Second slot is unlocked by spending an *Extra Gun* upgrade on that wagon.",
      "- Unlock schedule — " + ", ".join("wagon %d @ LV %d" % (c["wagon"], c["unlockLevel"]) for c in W["convoy"]),
      "- Consequence for balance: a 5-wagon convoy caps at 10 guns, not 5 — gun count is no longer the "
      "convoy size, so turret DPS can scale without adding wagons.", ""]
open(os.path.join(OUT, "BESTIARY.md"), "w").write("\n".join(L) + "\n")
print("BESTIARY.md", os.path.getsize(os.path.join(OUT, "BESTIARY.md")))
