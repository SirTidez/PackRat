# PackRat

A [Schedule One](https://store.steampowered.com/app/3164500) mod that gives every player a persistent, tiered backpack — extra storage that grows alongside your criminal rank.

---

## Screenshots

*Screenshots coming soon.*

---

## Overview

PackRat adds a backpack to your character that automatically upgrades as you climb the ranks. You start with a modest Rucksack at Hoodlum, and by the time you reach Block Boss you're carrying a full Hiking Backpack with 40 slots. Your backpack contents are saved with your game, visible at shops when you're ready to sell, and — for the bigger bags — something the cops will want to check when they stop you.

---

## Tier Progression

Your backpack upgrades automatically the moment your rank crosses a tier's unlock threshold. No action needed — just keep rising.

| Tier | Name             | Slots | Unlocks At   | Police Search |
|------|------------------|:-----:|--------------|:-------------:|
| 1    | Rucksack         |   8   | Hoodlum I    |      No       |
| 2    | Small Pack       |  16   | Peddler I    |      No       |
| 3    | Duffel Bag       |  24   | Hustler I    |     Yes       |
| 4    | Tactical Pack    |  32   | Enforcer I   |     Yes       |
| 5    | Hiking Backpack  |  40   | Block Boss I |     Yes       |

---

## Features

### Extra Storage
Open your backpack at any time with the toggle key (default: **B**). Your backpack is separate from your hotbar and inventory, giving you dedicated space for stockpiling product, supplies, or anything else you need to haul around.

### Automatic Tier Upgrades
As your rank increases, your backpack upgrades itself instantly. Each new tier brings more slots — the game logs the upgrade so you can tell when it happened.

### Persistent Contents
Everything in your backpack is saved to disk when you save your game. Load back in and it's all still there.

### Shop Integration
When visiting a shop to sell, your backpack slots appear alongside your hotbar items. You can sell directly out of the backpack without shuffling things into your inventory first.

### Cart-Aware Purchasing
When buying from a shop, the purchase warning accounts for your backpack space. If your hotbar is full but the backpack has room, the game will let you know items will spill into it rather than falsely warning you that everything won't fit.

### Police Body Searches
Carrying a Duffel Bag or larger makes you a more suspicious target. If police stop and search you while you're rocking a tier 3, 4, or 5 bag, they'll ask to check the backpack too. Anything illegal inside — unpackaged product, contraband — will count against you.

> **Tip:** The Rucksack and Small Pack fly under the radar. If you're moving small amounts and don't want the extra scrutiny, stay at Peddler rank or consider what you're carrying.

### Multiplayer Support
In a multiplayer session, the host's configuration is automatically pushed to all clients when they join. Everyone plays by the host's rules — unlock ranks, slot counts, and all. Clients don't need to touch their own config files.

---

## Controls

| Action                | Default Key |
|-----------------------|:-----------:|
| Open / close backpack | `B`         |

The toggle key is fully configurable. See [Configuration](#configuration) below.

---

## Installation

1. Install [MelonLoader](https://melonwiki.xyz/) v0.7.0 or newer into Schedule One.
2. Drop `PackRat.dll` into your `Schedule One/Mods/` folder.
3. Launch the game. A config file will be created automatically at `UserData/PackRat.cfg` on first run.

---

## Configuration

PackRat's config file is located at:

```
UserData/PackRat.cfg
```

Edit this file while the game is closed. In a multiplayer session, only the **host's** config is used — changes made by clients while in-session have no effect.

### Full Example Config

```ini
[PackRat]

# Key to open and close your backpack
# Accepts any Unity KeyCode name: B, Tab, F1, Backslash, etc.
ToggleKey = B

# Tier 0 — Rucksack
# The first backpack, unlocked at Hoodlum I
Tier0_UnlockRank = Hoodlum : 1
Tier0_SlotCount = 8

# Tier 1 — Small Pack
# Unlocked at Peddler I, still under the radar
Tier1_UnlockRank = Peddler : 1
Tier1_SlotCount = 16

# Tier 2 — Duffel Bag
# Unlocked at Hustler I. Police will search this and above.
Tier2_UnlockRank = Hustler : 1
Tier2_SlotCount = 24

# Tier 3 — Tactical Pack
# Unlocked at Enforcer I
Tier3_UnlockRank = Enforcer : 1
Tier3_SlotCount = 32

# Tier 4 — Hiking Backpack
# The largest tier, unlocked at Block Boss I
Tier4_UnlockRank = Block_Boss : 1
Tier4_SlotCount = 40
```

### Config Reference

| Key | Default | Description |
|-----|---------|-------------|
| `ToggleKey` | `B` | Key to open/close the backpack. Any [Unity KeyCode](https://docs.unity3d.com/ScriptReference/KeyCode.html) name. |
| `Tier{n}_UnlockRank` | See table above | Rank required to unlock tier n. Format: `RankName : TierNumber` (1–5). |
| `Tier{n}_SlotCount` | See table above | Number of storage slots for tier n. Clamped between 1 and 40. |

**Valid rank names:**

```
Street_Rat, Hoodlum, Peddler, Hustler, Bagman,
Enforcer, Shot_Caller, Block_Boss, Underlord, Baron, Kingpin
```

> **Note:** Which tiers trigger police searches is fixed and cannot be configured. Tiers 3, 4, and 5 (Duffel Bag and above) always include the backpack in police body searches.

---

## Requirements

- [Schedule One](https://store.steampowered.com/app/3164500) (Steam)
- [MelonLoader](https://melonwiki.xyz/) v0.7.0 or newer
