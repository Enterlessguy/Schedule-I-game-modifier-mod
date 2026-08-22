# Schedule I Control Center V2.1

## Release summary

V2.1 is the packaged Intelligence Database release of the Schedule I Control
Center. It combines the redesigned dark-blue control surface with the reviewed
live bridge and offline save tools in one attach-and-launch package.

## V2.1 package refresh

### 2.1.0 — guarded application updates

- Added stable GitHub Release discovery, cached offline release metadata,
  verified full-package download, transactional synchronization, rollback, and
  a dedicated Version & Updates workspace.
- Added a cube-branded post-update changelog screen.

- Replaced the generic Windows executable icon with a crisp multi-resolution
  Intelligence Database cube and Schedule I compatibility badge across the
  launcher, graphical Control Center, and CLI.
- Prevented status polling and price workflows from reading or writing unsafe
  generated IL2CPP price-bound fields. On affected systems those native field
  accesses could terminate Schedule I before a managed exception was available.
- Published the Intelligence Database-owned Control Center, bridge, launcher,
  protocol, and regression-test source under the MIT License.

## Highlights

- Reworked the WinForms interface into grouped workspace pages for overview,
  market intelligence, player and inventory, business operations, drug effects,
  save and safety, help, and diagnostics.
- Added the bounded Intelligence Database intro splash with the blue cube
  branding and a per-user welcome message.
- Added command logs and a diagnostics/troubleshooting workspace that records
  safe exception context, operation failures, readiness gates, and exportable
  reports without exposing save contents or secrets.
- Added reviewed market workflows for fair-value synchronization, deal and
  unit-price controls, customer allowances, and business laundering limits.
- Added player inventory paging and movement-speed controls with preview,
  apply, persistence, and verification flows.
- Kept offline save protection, backups, validation, console enablement, and
  acquire-only property controls behind explicit safety gates.
- Polished the numeric inputs, table/list surfaces, page scrolling, command
  status presentation, and responsive layout sizing used by the control pages.
- Shipped the one-click root launcher together with the runtime package. It
  locates Schedule I, confirms the target, attaches the runtime, records an
  install summary, and launches the installed Control Center without elevation
  or network access.

## Safety and compatibility

Live actions remain restricted to the reviewed Schedule I build, loaded save,
solo-host authority, and the local same-user bridge. Offline writes require the
game to be closed and create a complete backup before replacing targeted JSON.
The launcher does not delete files, modify saves, or contact a network service.

The package includes the x64 .NET Framework Control Center, CLI, reviewed
MelonLoader bridge runtime, and launcher. See `README.md` for installation,
rollback, and troubleshooting details.
