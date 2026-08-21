# PackRat

A [Schedule One](https://store.steampowered.com/app/3164500) mod that gives every player a persistent, tiered backpack — extra storage that grows alongside your criminal rank.

---

## Screenshots

### Backpack Inventory

![PackRat backpack inventory view](https://raw.githubusercontent.com/SirTidez/PackRat/master/assets/images/inventory.png)

### Hardware Store / Shop Integration

![PackRat shop interface integration](https://raw.githubusercontent.com/SirTidez/PackRat/master/assets/images/shop_interface.png)

### Tactical Pack Tier Example

![PackRat tactical pack example](https://raw.githubusercontent.com/SirTidez/PackRat/master/assets/images/tac_pack.png)

### Deal Handover UI (Backpack Integration)

![PackRat backpack in deal handover UI](https://raw.githubusercontent.com/SirTidez/PackRat/master/assets/images/handover.png)

---

## Overview

PackRat adds a backpack to your character that you upgrade by **purchasing tiers at the Hardware Store**. Once you've reached the required rank for a tier, you buy it with account funds and receive a backpack item in your inventory. **Select that item in your hotbar and press B** to consume it, apply the tier to your backpack, and open the backpack. Your backpack contents are saved with your game, visible at shops when you're ready to sell, and — for the bigger bags — something the cops will want to check when they stop you.

---

## Tier Progression

Backpack tiers are **purchased at the Hardware Store** (not automatic). Each tier appears in the store once you've reached its required rank. Buy the tier with account funds, then **select the backpack item in your hotbar and press B** to apply it and open the backpack.

| Tier | Name             | Slots | Can Buy At   | Police Search |
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

### Controller Support
PackRat supports controllers through Schedule I's native input and UI-navigation systems. Press the game's **Interact** face button to open or close the backpack (or apply a selected backpack tier item), then use the normal controller UI navigation to move between backpack slots, the hotbar, and PackRat controls. Sort tabs, search, paging, product metrics, settings, and integrated storage and handover controls are all selectable.

World interactions always take priority: PackRat opens only when there is no hovered or active interactable, so the same button continues to work normally on doors, stations, and other game objects. See [Controller Support](docs/controller-support.md) for the full control reference, navigation behavior, and Steam keyboard requirements.

### Backpack Browser and Organization
The backpack browser is built for large, paged bags. Search by item name, quality, category, drug type, base strain, or derived mixed product; filter and sort the visible contents; mark favorite items; and use recent-item history to return to what you just handled. `Organize` groups contents by type, name, and quality, while `Stack` consolidates compatible stacks into the earliest available slots.

### Settings and Appearance
Use the in-game settings cog to configure per-view position and scale, keyboard navigation, themes, a custom primary color, smart routing, and the optional product-metrics drawer. Settings are mirrored to `UserData/PackRat.cfg` and apply live while the backpack is open.

### Hardware Store Tiers
Backpack tiers are bought at the **Hardware Store** (both locations). Once you've reached the required rank for a tier, it appears in the store; purchase it with **account funds** (not cash). You receive a backpack item in your inventory. **Select that item in your hotbar and press B** to consume it, apply the tier to your backpack, and open the backpack. Each tier brings more slots; the game logs the upgrade when you use the item.

### Persistent Contents
Everything in your backpack is saved to disk when you save your game. Load back in and it's all still there.

### Shop Integration
When visiting a shop to sell, your backpack slots appear alongside your hotbar items. You can sell directly out of the backpack without shuffling things into your inventory first.

### Deal Handover Integration
During deal handovers, the backpack browser appears alongside the deal UI so you can move required items directly from your bag. Required products are highlighted, and the `Auto-Fill Deal` control can fill matching deal slots from the available sources. If your last driven vehicle is within 20 meters (the same condition the base game uses for vehicle storage access), a vehicle/backpack source toggle appears in the browser header.

### Storage Transfers and Smart Routing
Storage containers include bulk-move controls for selected product/category groups in either direction between the container and backpack. Marijuana strain selections include their derived mixed products. Smart routing can also direct products, seeds, mixers, and reagents into the backpack during supported quick-move actions.

### Product Metrics
An optional expandable metrics drawer lists product quantities, values, and active-order quantities for matching strains. Choose which metrics appear from the in-game backpack settings.

### Cart-Aware Purchasing
When buying from a shop, the purchase warning accounts for your backpack space. If your hotbar is full but the backpack has room, the game will let you know items will spill into it rather than falsely warning you that everything won't fit.

### Police Body Searches
Carrying a Duffel Bag or larger makes you a more suspicious target. If police stop and search you while you're rocking a tier 3, 4, or 5 bag, they'll ask to check the backpack too. Anything illegal inside — unpackaged product, contraband — will count against you.

> **Tip:** The Rucksack and Small Pack fly under the radar. If you're moving small amounts and don't want the extra scrutiny, stay at Peddler rank or consider what you're carrying.
> **Config:** Set `EnableSearch = false` in `UserData/PackRat.cfg` if you want to disable backpack police searches entirely.

### Multiplayer Support
In a multiplayer session, the host's configuration is automatically pushed to all clients when they join, and the host now acts as the authoritative source for backpack state. Clients request and apply the host's synced backpack snapshot instead of relying on their own local save state, so unlocked tiers and backpack contents stay aligned across the session. Clients don't need to touch their own config files.

---

## Controls

| Action | Keyboard | Controller |
|--------|:--------:|------------|
| Open / close unlocked backpack | `B` by default | **Interact** (the west face button: `X` on Xbox-style controllers, `Square` on PlayStation-style controllers) when no world object is interactable. |
| **Use** backpack item (apply tier) | Select the backpack tier item in the hotbar and press `B`. | Select the tier item in the hotbar and press **Interact** when no world object is interactable. |
| Navigate backpack UI | Arrow keys / existing keyboard navigation | Use Schedule I's standard controller UI navigation. PackRat controls and item slots participate in one selection path. |
| Search backpack contents | Click the search field and type. | Select Search and submit it. Steam's gamepad keyboard opens on Steam Deck or in Steam Big Picture when the Steam overlay is available. |

Pagination supports the left/right and up/down arrow keys. Keyboard navigation covers PackRat controls and settings; item drag/drop remains mouse-driven. Controller search can still focus on desktop, but Steam's on-screen gamepad keyboard is unavailable outside supported Steam modes; use a physical keyboard there.

The toggle key is fully configurable. See [Configuration](#configuration) below.

---

## Installation

1. Install [MelonLoader](https://melonwiki.xyz/) v0.7.0 or newer into Schedule One.
2. Choose the correct runtime build and place its DLL in `Schedule One/Mods/`:
   - **Current/Main/Beta Schedule One (IL2CPP):** `PackRat-IL2CPP.dll`
   - **Alternate/Alternate Beta Schedule One (Mono):** `PackRat-Mono.dll`
3. Nexus users can select the matching runtime in the FOMOD installer. Thunderstore bundles both runtime DLLs; install the DLL that matches your game runtime when performing a manual installation.
4. Launch the game. A config file will be created automatically at `UserData/PackRat.cfg` on first run.

---

## Configuration

PackRat's config file is located at:

```
UserData/PackRat.cfg
```

Edit this file while the game is closed. In a multiplayer session, only the **host's** config is used — changes made by clients while in-session have no effect.

The browser, layout, appearance, routing, and metrics options are best adjusted through the in-game settings cog; PackRat mirrors those changes directly to this config file.

### Core Config Example

```ini
[PackRat]

# Key to open and close your backpack
# Accepts any Unity KeyCode name: B, Tab, F1, Backslash, etc.
ToggleKey = B

# When false, police body searches will not check backpack contents
EnableSearch = true

# Tier 0 — Rucksack
# First backpack; can buy at Hardware Store at Hoodlum I
Tier0_UnlockRank = Hoodlum : 1
Tier0_SlotCount = 8
Tier0_Price = 25

# Tier 1 — Small Pack
# Can buy at Peddler I; still under the radar
Tier1_UnlockRank = Peddler : 1
Tier1_SlotCount = 16
Tier1_Price = 75

# Tier 2 — Duffel Bag
# Can buy at Hustler I. Police will search this and above.
Tier2_UnlockRank = Hustler : 1
Tier2_SlotCount = 24
Tier2_Price = 150

# Tier 3 — Tactical Pack
# Can buy at Enforcer I
Tier3_UnlockRank = Enforcer : 1
Tier3_SlotCount = 32
Tier3_Price = 300

# Tier 4 — Hiking Backpack
# Largest tier; can buy at Block Boss I
Tier4_UnlockRank = Block_Boss : 1
Tier4_SlotCount = 40
Tier4_Price = 500
```

### Config Reference

| Key | Default | Description |
|-----|---------|-------------|
| `ToggleKey` | `B` | Key to open/close the backpack and to use a backpack item in the hotbar. Any [Unity KeyCode](https://docs.unity3d.com/ScriptReference/KeyCode.html) name. |
| `EnableSearch` | `true` | When `false`, police body searches never inspect the backpack, even for Duffel Bag and larger tiers. |
| `Tier{n}_UnlockRank` | See table above | Rank required before the tier appears at the Hardware Store. Format: `RankName : TierNumber` (1–5). |
| `Tier{n}_SlotCount` | See table above | Number of storage slots for tier n. Minimum 1; no fixed maximum. The backpack UI paginates larger capacities. |
| `Tier{n}_Price` | 25, 75, 150, 300, 500 | Price (account funds) to buy tier n at the Hardware Store. |

**Valid rank names:**

```
Street_Rat, Hoodlum, Peddler, Hustler, Bagman,
Enforcer, Shot_Caller, Block_Boss, Underlord, Baron, Kingpin
```

> **Note:** The searchable tiers are still fixed. When `EnableSearch = true`, only tiers 3, 4, and 5 (Duffel Bag and above) are included in police body searches.

---

## Requirements

- [Schedule One](https://store.steampowered.com/app/3164500) (Steam)
- [MelonLoader](https://melonwiki.xyz/) v0.7.0 or newer
