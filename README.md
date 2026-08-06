# Schedule I Control Center

Unofficial, task-focused control center and modifier for **Schedule I**
(Steam app `3164500`). It brings together live fair-market synchronization,
customer affordability scaling, sell-value / deal-limit controls, and safe
offline save tools in one window.

**Version:** v0.5.0 (bridge v0.5.0, protocol v1)

**Release ZIP password:** `INTEL DATABASE`

---

## What's in the package

- `ScheduleIControlCenter.exe` - one-click launcher: finds the game, attaches
  the runtime on confirmation, and opens the Control Center.
- `ScheduleI-ControlCenter\dist\ScheduleIControlCenter.exe` - the Control
  Center GUI.
- `ScheduleI-ControlCenter\dist\ScheduleIControlCenter.Cli.exe` - offline CLI.
- `Mods\ScheduleIControlBridge.dll` - the v0.5.0 in-game bridge mod.
- `version.dll` + `MelonLoader\` - MelonLoader 0.7.3.2525 runtime tree with a
  null-safety-patched Il2CppInterop 1.5.3 generator (see "0.4.6f11
  compatibility" below).
- `UserData\Loader.cfg` - loader configuration.
- `CHECKSUMS-SHA256.txt` - SHA-256 hashes of the key files.

The package intentionally contains **no save files, backups, logs, or bridge
settings profiles** - those stay on your machine.

---

## Requirements

- 64-bit Windows
- .NET Framework 4.8.1 (Control Center GUI and launcher)
- x64 .NET 6.0.36 runtime (MelonLoader)
- Schedule I `0.4.6f11`, Steam build `24484559` (for live mutation support)

---

## Quick start (one-click)

1. Close Schedule I and the Control Center.
2. Extract the release ZIP (password: `INTEL DATABASE`) anywhere, or copy the
   whole `ScheduleI-Control-Center-v0.5.0` folder wherever you like.
3. Run `ScheduleIControlCenter.exe` - the launcher next to this readme.
4. The launcher searches for Schedule I (default Steam path first, then Steam
   library folders, then a bounded system-wide search) and shows the folder it
   found.
5. Click **Attach and launch**. The runtime is copied into the game folder and
   the Control Center starts automatically.

The launcher never deletes files, needs no administrator rights, and does not
touch saves, backups, or bridge settings. Files that are already present and
identical are skipped; older versions are kept as `.bak-<timestamp>` next to
the file before replacement. A small attach record is written to
`ScheduleI-ControlCenter\InstallRecords`.

If you only want to run the Control Center without re-attaching, click
**Launch only**.

---

## Manual install

1. Close Schedule I and the Control Center.
2. Extract the folder's **contents** into the Schedule I installation root:

   `C:\Program Files (x86)\Steam\steamapps\common\Schedule I`

3. Allow the matching folders to merge.
4. Start Schedule I, load a save, then run:

   `ScheduleI-ControlCenter\dist\ScheduleIControlCenter.exe`

The Control Center must stay below the game directory - it searches upward for
`Schedule I.exe` and will not provide live/game-aware features otherwise.

---

## Using the Control Center

The top bar shows the three readiness states: `GAME: RUNNING`,
`BRIDGE: CONNECTED`, and `AUTHORITY: SOLO HOST`. Live mutation controls stay
unavailable until all three are green.

### Overview
Plain-language game, bridge, save, and authority status plus the recommended
next action.

### Sell Values
Separates the native `$9,999` maximum **total** used in counteroffers and
handovers from the separate `$1..$999` product **unit-price** range. A custom
total-deal maximum (`9999..999999`) can be previewed and applied, or restored
to the native default. Unit-price changes are verified and saved through the
live game.

### Products
Guided fair-market synchronization: selling price, current/planned fair-market
value, customer value, and alignment. **Match selling price (recommended)**,
an absolute multiplier, or double-click a **Planned fair value** cell for an
exact value - then **Preview sync** and **Apply and verify**.

### Customers
Scoped weekly-spend controls (unlocked or all customers) using an
original-value multiplier or per-customer manual ranges. The table exposes the
derived per-order allowance and the roughly three-times hard offer ceiling
before you apply.

### Properties
Acquire-only live vanilla ownership, with an offline fallback available only
when the game is closed. Un-owning is intentionally unavailable.

### Save Tools
Full slot backup, all-JSON validation, and console enablement.

### Help
Searchable quick start, concepts, live/offline behavior, safety, persistence,
multiplayer limits, troubleshooting, commands, and rollback.

### Advanced
Raw command output and the clearly warned sell-only offline editor.

---

## Why fair-market synchronization?

Schedule I stores the selling price separately from
`ProductDefinition.MarketValue`. Customers compare the offer price against
market value, so raising only the selling price lowers their value proposition.
The bridge patches the reviewed
`ProductManager.CalculateProductValue(ProductDefinition,float)` path and stores
absolute per-product factors in a bounded, save-scoped sidecar - never stacking
multipliers across refreshes or restarts.

Fair-market overrides are solo-host only because market value is local and not
FishNet-replicated.

## Why customer allowance controls?

Customers have a native budget derived from their weekly-spend range,
relationship interpolation, and rank multiplier. Counteroffer and
offer-success calculations can hard-reject an asking price above roughly three
times the per-order allowance. The bridge rescales only
`CustomerData.GetAdjustedWeeklySpend(float)` from the original range to the
configured range; preferences, relationship, addiction, order scheduling,
product enjoyment, and randomness still apply. It raises the affordability
envelope - it does not force a sale.

## Sell Values: total cap vs unit price

The `$9,999` maximum is the total entered in counteroffers and handovers. The
`$1..$999` range is the unit price per product. They are independent controls;
the Sell Values page keeps them clearly separated. A custom total is applied
through three reviewed Harmony replacements (`CounterofferInterface.Send`,
`HandoverScreen.PriceChanged`, and `HandoverScreen.DonePressed`) and persists
in a bridge-sidecar profile. The native `MaxPrice` static wrappers are
intentionally untouched because the reviewed native methods inline their
`$9,999` constants.

## 0.4.6f11 compatibility

Schedule I `0.4.6f11` (Steam build `24484559`) ships a new IL2CPP metadata
dump that crashes the interop generator bundled with every released MelonLoader
and Il2CppInterop (`Pass11ComputeTypeSpecifics` null reference, followed by
unresolvable stripped Unity types such as `Camera+GateFitMode`). This package
therefore ships a **patched Il2CppInterop 1.5.3 generator**: three surgical
null-safety guards that mirror the generator's own safe fallbacks (unresolvable
field types are treated as non-blittable instead of crashing). The patch is
applied only to `MelonLoader\net6\Il2CppInterop.Generator.dll` and is
documented in `CHECKSUMS-SHA256.txt`; the rest of the MelonLoader tree is the
official 0.7.3.2525 nightly. Interop for this exact game build is pre-generated
in the package, so no on-demand generation is needed on first run.

The same update also changed the game's counteroffer/handover UI internals:
`CounterofferInterface` moved to the `UI.Phone` namespace, the price controls
now use the shared `AmountSelector` component, and the old
`HandoverScreenPriceSelector` was removed. The bridge v0.4.1 patches were
re-targeted accordingly.

### v0.4.2 fix: persistence recovery

When an older build-scoped market/allowance profile is rejected after a game
update (or a profile file is unreadable), the bridge now starts clean with a
fresh empty profile and persistence ready, so the Products and Customers tabs
work immediately. The rejected file is left untouched on disk and is replaced
atomically on the next apply. Previously the bridge stayed disabled for the
whole session after rejecting an incompatible profile, which made the market
and allowance operations unavailable.

### v0.4.3 fix: manual cell editing

Double-clicking a planned value in the Products, Sell Values, or Customers
grids no longer crashes with "cannot commit or quit a cell value change" when
another cell edit is still pending. The grid now commits or cancels the
pending edit before moving. Manual fair-value targets remain intentionally
bounded to 0.1x-10x of the product's original value, with the exact factor
shown when a target is out of range.

### v0.5.0: Business Laundering and Effects tabs

- **Business Laundering** - change the daily laundering ceiling per owned
  business (the native Laundromat limit is $2,000/day). Set one limit for all
  owned businesses or edit each business individually; the bridge re-applies
  the ceiling every game day and when an operation starts.
- **Effects** - edit the price increase of every mixed drug effect (flat
  change, multiplier, and base multiple) and adjust physical intensity where
  the game stores it as a real field (for example Bright-Eyed eye-glow
  emission and light intensity). Values the game compiles in as constants
  (height scale, speed multipliers, seizure jitter, durations, health drains)
  are shown read-only with an explanation, because the game has no writable
  storage for them and any write to the constant crashes the game.
- The launcher now **replaces old versions automatically**: MelonLoader and
  the Control Center binaries are swapped wholesale (old copies removed after
  a successful replacement, with automatic rollback if the copy fails), so
  upgrading over an older install can no longer leave stale files behind.
  User data such as saves, bridge profiles, install records, and backups is
  never touched.

Security hardening in v0.5.0: the bridge re-validates every value read back
from its effect and laundering profiles on load, so a tampered or corrupt
profile file can never push out-of-range values into the game.

---

## Safety notes

- Same-user, one-client Windows named pipe with bounded, versioned JSON.
- No TCP/HTTP listener, remote access, arbitrary paths, reflection, eval, or
  code execution.
- Mutations require the exact reviewed build, a loaded save, solo-host
  authority, and a known count of zero remote players.
- Offline writes are refused while the game runs and create a full slot backup
  before applying.
- The bridge keeps Unity running while unfocused so requests complete; the
  game simulation continues until Schedule I exits.
- This is an unofficial fan tool. Use at your own risk; keep save backups.

## Rollback

- Close the game. Remove or rename `Mods\ScheduleIControlBridge.dll` to disable
  the bridge; native values return on the next launch.
- Remove or rename root `version.dll` to bypass MelonLoader entirely.
- Saved unit-price or ownership changes are native save data and are reversed
  through a reviewed live change or a save backup.

---

## Building from source

This runtime package intentionally excludes source and build tools. The
developer project (GUI/CLI source, bridge source, tests, pipe protocol, and
install records) is maintained separately and builds with `build.cmd`,
`build-mod.cmd`, and `build-tests.cmd` using the local Visual Studio Roslyn
compiler - no package restore or network downloads.

## Checksums

`CHECKSUMS-SHA256.txt` in this folder lists SHA-256 hashes for the launcher,
GUI, CLI, bridge DLL, `version.dll`, `Loader.cfg`, and `MelonLoader.dll`.
Verify them after download before use.

---

## Credits

Created and maintained by **Enterless / Intel Database**.

- Website: https://www.inteldatabase.org
- Email: enterless@inteldatabase.org

Not affiliated with the game's developers or Steam.
