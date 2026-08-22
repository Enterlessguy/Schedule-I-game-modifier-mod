# Schedule I Control Center V2

A task-focused Windows control center for safe offline save tools and reviewed
live Schedule I operations, presented in the Intelligence Database V2 release
branding.

> Release V2. See [RELEASE_NOTES_V2.md](RELEASE_NOTES_V2.md) for the complete
> change summary.

Bridge v1.1.0 targets Schedule I `0.4.6f13` and adds a dedicated **Sell Values**
page. It distinguishes the
vanilla `$1..$999` product unit-price range from the separate `$9,999` maximum
total used by counteroffers and handovers. On an eligible solo host, both unit
prices and the total-deal maximum can be raised through `$16,777,215` with a previewed, revision-checked, persistent
solo-host workflow. See `HANDOFF.md` and the v0.4 install record for validation
evidence and current deployment state.

## Quick start

1. Extract the ZIP to a folder outside the Schedule I installation.
2. Run the root `ScheduleIControlCenter.exe` launcher and confirm the detected
   Schedule I folder. It attaches the packaged runtime and opens the Control
   Center; do not run the launcher from inside the game folder.
3. Start Schedule I and load the save you want to control.
4. Require `GAME: RUNNING`, `BRIDGE: CONNECTED`, and `AUTHORITY: SOLO HOST` in
   the top bar.
5. Open **Sell Values** to review the total-deal ceiling and product unit-price
   range. Changing either is optional.
6. Open **Products**, choose a category, keep **Match selling price
   (recommended)** selected, and click **Preview sync**.
7. Review the complete plan, then click **Apply and verify**.
8. If native affordability is still the limiting factor, open **Customers**,
   choose **Unlocked customers** or **All customers**, preview an allowance
   factor, and review each planned per-order allowance and hard offer limit
   before applying.

### One-click attach and launch

Run `ScheduleIControlCenter.exe` from the runtime package root. It locates
Schedule I (default Steam path first, then a bounded system-wide search), asks
you to confirm attachment, copies the runtime into the game folder, and starts
the Control Center. Launcher source lives in `launcher\`; build it with
`launcher\build-launcher.cmd`. It never deletes files, needs no elevation, and
touches no saves, backups, or bridge profiles.

The Products page shows selling price, current/planned fair-market value,
customer value, and alignment. Customer value `1.00` restores the original
price/value balance; customer preferences and relationship can still affect an
individual purchase. Double-click a **Planned fair value** cell to enter a
specific value instead of using one multiplier.

## Main pages

- **Overview** - plain-language game, bridge, save, and authority status plus a
  recommended next action.
- **Products** - guided fair-market synchronization with hidden preview/revision
  bookkeeping and exact verification. Planned fair values can also be edited
  individually by double-clicking the cell.
- **Sell Values** - separate, clearly labelled controls for the vanilla `$9,999`
  counteroffer/handover total cap and `$999` product unit-price cap. The bridge
  raises both reviewed runtime paths through the exact `$16,777,215` technical boundary. Custom total caps are build-specific and persist in a bounded bridge
  sidecar; unit-price changes are verified and saved through the live game.
- **Customers** - scoped weekly-spend controls for unlocked or all customers,
  with an original-value multiplier or double-click editing of individual
  planned minimum/maximum values. The table exposes the derived per-order
  allowance and approximately three-times hard offer ceiling before apply.
- **Properties** - acquire-only live vanilla ownership, with offline fallback
  available only when the game is closed.
- **Save Tools** - full backup, all-JSON validation, and console enablement.
- **Help** - searchable quick start, terminology, safety, persistence,
  multiplayer limitations, troubleshooting, commands, and rollback guidance.
- **Advanced** - raw command output and the clearly warned sell-only offline
  editor.

## Why fair-market synchronization is required

Schedule I stores the player's selling price separately from
`ProductDefinition.MarketValue`. Customers compare the offer price against
MarketValue, so raising only the selling price lowers their value proposition.

Bridge v0.2.0 uses the reviewed
`ProductManager.CalculateProductValue(ProductDefinition,float)` path, freezes
explicit product IDs, and stores absolute per-product factors in a fixed,
save-scoped bridge profile. It never repeatedly multiplies the current value.
The profile is required because the native save persists product prices but has
no MarketValue field.

Fair-market overrides are deliberately solo-host only. MarketValue is local and
not FishNet-replicated; an unknown/remote client state deactivates the overrides
and restores vanilla values.

## Why customer allowance controls are separate

Fair-market alignment fixes the price/value comparison, but it does not remove
the customer's budget. Native code calculates adjusted weekly spend by
interpolating `CustomerData.MinWeeklySpend` and `MaxWeeklySpend` with the
customer's normalized relationship, then applying the current rank multiplier.
It divides that result by the generated order-day count to obtain the effective
per-order allowance. Counteroffer evaluation and offer-success calculation can
hard-reject an asking price above approximately three times that allowance,
producing a zero-percent chance even when the fair-market value is favorable.

Bridge v0.3 adds a narrowly scoped postfix on the exact
`CustomerData.GetAdjustedWeeklySpend(float)` overload. A configured customer's
native result is rescaled from the original range to the selected range; native
relationship/rank behavior, preferences, addiction, order scheduling, product
enjoyment, and randomness still apply. This raises the affordability envelope,
not a guarantee of purchase, and no acceptance function or hard-coded threshold
is bypassed.

Allowance ranges are stored separately in
`UserData\ScheduleIControlBridge.customer-allowances.json`. Like market factors,
they are absolute values in a bounded save-scoped profile, so repeated applies
cannot stack and an eligible full process restart reapplies them. The native
customer save has no weekly-spend field. Exact reviewed build, loaded-save,
solo-host authority, and a known zero remote-player count are required; losing
any gate clears the live override map.

## Build

Run `build.cmd` for the GUI/CLI, `build-tests.cmd` for the isolated-save smoke
tests, and `build-mod.cmd` for the MelonLoader bridge. Builds use the installed
Visual Studio Roslyn compiler plus local framework/runtime/generated assemblies.
No package restore or network download is required.

Outputs:

- `dist\ScheduleIControlCenter.exe` - graphical Control Center
- `dist\ScheduleIControlCenter.Cli.exe` - offline CLI
- `mod\ScheduleIControlBridge\bin\ScheduleIControlBridge.dll` - staged bridge

## Runtime dependencies

The GUI is an x64 .NET Framework WinForms executable and has no adjacent NuGet
or third-party DLL dependency. It uses only the Windows CLR 4 framework
assemblies and must remain below the Schedule I game directory so it can locate
`Schedule I.exe` and the save environment.

Live controls additionally require the exact reviewed game build, MelonLoader
0.7.3 (`version.dll` plus the `MelonLoader` runtime tree), the installed
`Mods\ScheduleIControlBridge.dll`, and the x64 .NET 6.0.36 runtime used by the
loader. `UserData\Loader.cfg` is loader configuration. Bridge JSON sidecars are
optional user state, not executable dependencies; saves, logs, backups, build
tools, and game binaries are not part of the dependency package.

## Safety boundaries

- Offline writes are refused while Schedule I runs; each apply creates a full
  slot backup and validates JSON.
- Live mutations require reviewed hashes, loaded-save readiness, authority,
  bounded inputs, a fresh preview/revision, and exact readback.
- Custom total-deal limits are restricted to whole values from `9999..16777215`
  and activate only for an eligible solo host. Unit-price commits use a separate
  eligible host patch that raises the reviewed `$999` runtime bound through the
  same exact technical maximum.
- Customer allowance mutation is restricted to finite `0..16777215` minimum and
  maximum values with `minimum <= maximum`; manual requests are bounded to 96
  customers and stored profiles to 128 customers per save.
- The local pipe is same-user, one-client, allowlisted, and versioned. There is
  no TCP/HTTP, remote access, arbitrary path, reflection, eval, code execution,
  or generic console passthrough.
- Property ownership is acquire-only. Un-owning remains unavailable.
- Sell-only offline pricing is under Advanced because it can recreate a
  selling-price/fair-value mismatch.
- Allowance controls do not force sales. Customer preferences, relationship,
  addiction, order cadence, product enjoyment, and native randomness remain in
  effect.
- The bridge keeps Unity running while unfocused so requests complete; the full
  game simulation therefore continues until Schedule I exits.

See `protocol\pipe-v1.md`, `HANDOFF.md`, and `InstallRecords` for the complete
contract, current validated state, hashes, scan results, and rollback details.
