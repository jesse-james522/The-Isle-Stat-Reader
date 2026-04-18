# The Isle Stat Reader

A Windows app that reads stat curves and balance attributes directly from *The Isle*'s game files — no JSON exports needed.

**Made by pretzel3819**  
https://discord.gg/xBarq4rJ2K  
https://discord.gg/R8CPP7WWqd

---

## Features

- **Stat Curves** — plots growth curves (speed, weight, stamina, etc.) with hover tooltips showing exact values
- **Balance Attributes** — shows raw balance data in a clean table with calculated survival stats (starve time, thirst time, bleed time)
- **Virtual Attack Graphs** — combines AttackPower curves with balance data to show actual damage output per attack type
- **Elder/Senior curves** — dual-curve display for dinos that have both senior and elder stat lines
- **All-Species Comparison Chart** — sortable table comparing every dino across all growth stages (0% / juvenile / subadult / adult / 87.5% / 100% / peak); includes Survival & Stamina view (starve/dehydrate times, sprint and swim durations and ranges) and an experimental Health & Blood regen view
- **Gallimimus diet-slot scaling** — Sprint Speed chart and plot show all 4 diet-slot variants with values scaled proportionally across every growth point
- **Reads directly from game files** — uses CUE4Parse to read `.pak`/`.ucas`/`.utoc` files, no FModel export step required

---

## Requirements

**.NET 8 Windows Desktop Runtime** — download from:  
https://dotnet.microsoft.com/en-us/download/dotnet/8.0  
*(pick "Desktop Runtime" under Windows x64)*

---

## Setup

1. Download and extract `TheIsleStatReader-1.0.0-win-x64.zip` from the [latest release](../../releases/latest)
2. Run `TheIsleStatReader.exe`
3. Click **Settings** and fill in:
   - **Pak Directory** — path to `steamapps/common/TheIsle/Content/Paks` in your game install, can be found via right click The Isle in Steam Manage>Browse Local Game Files.
   - **AES Key** — found in `AES_Key.txt` in the release (also provided below)
   - **Mappings File** — download `5.6.0-0+UE5-TheIsle.usmap` from the release and point to it
4. Click **Load** — the dino list will populate automatically

**AES Key (patch 5.6.0):**
```
0x376538F64EB9B743AC8A798467AA3444D771FB120C758A183DDA39847E8D9E4E
```

> On first run the app will download two small native DLLs (`oodle-data-shared.dll`, `zlib-ng2.dll`) from GitHub automatically — these are required for reading compressed game assets.

---

## Usage

- Select a dinosaur from the dropdown and click **Load**
- Pick a stat from the attribute list and click **Plot** to view its growth curve
- Click **Balance** to open the balance attributes table for that dino
- Multiple curves can be overlaid on the same plot window

---

## Building from Source

Requires .NET 8 SDK and the repo cloned with submodules:

```bash
dotnet publish -c Release -r win-x64 --self-contained false -o publish_release
```

---

## Third-Party Libraries

- [CUE4Parse](https://github.com/FabianFG/CUE4Parse) — Apache 2.0
- [OxyPlot](https://github.com/oxyplot/oxyplot) — MIT

Full license texts are included in the `THIRD_PARTY_LICENSES` folder of the release.
