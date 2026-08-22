# Schedule I Control Center V2.2.1

Schedule I Control Center is a Windows companion application for reviewed live
controls and guarded offline save tools, presented in the Intelligence Database
visual identity.

V2.2.1 combines the redesigned control-center interface, clearer workspace
navigation, the Intelligence Database startup sequence, expanded diagnostics,
one-click attach-and-launch packaging, guarded GitHub Release updates, and
configurable two-direction inventory paging that remains available across in-game interfaces.

> Read [RELEASE_NOTES_V2.2.1.md](RELEASE_NOTES_V2.2.1.md) for the V2.2.1 patch summary.

The complete Intelligence Database source for the launcher, graphical
application, command-line application, live bridge, protocol, and regression
tests is published under the [MIT licence](LICENSE) in
[`ScheduleI-ControlCenter/`](ScheduleI-ControlCenter/).

## Install and launch

1. Download and extract the V2.2.1 package outside the Schedule I installation.
2. Run the root `ScheduleIControlCenter.exe`.
3. Confirm the detected Schedule I folder when prompted.
4. The launcher attaches the packaged runtime and opens the Control Center.
5. Start Schedule I, load the intended save, and confirm the readiness badges
   before using live controls.

The root launcher does not require elevation, modify saves, delete files, or
contact a network service. It copies only the packaged runtime into the
confirmed game folder and records an install summary for troubleshooting.

## V2.2.1 workspaces

- **Overview** — readiness, recommended workspaces, and ordered command logs.
- **Market intelligence** — fair-value synchronization, sell-price and deal
  limits, and customer allowances.
- **Player & inventory** — inventory capacity, configurable previous/next page hotkeys,
  virtual hotbar pages, movement speed, preview, apply, and verification.
- **Business operations** — laundering limits and reviewed property controls.
- **Drug effects** — supported product and effect workflows.
- **Save & safety** — backup, validation, console enablement, and protected
  offline operations.
- **Version & Updates** — installed-version identity, automatic stable-release checks, cached offline metadata,
  release notes, verified full-package download, and transactional install.
- **Help center** — quick-start guidance, terminology, safety rules, rollback,
  and common troubleshooting steps.
- **Diagnostics** — health checks, incident history, reasoning, evidence,
  remediation guidance, and redacted report export.

Advanced and highly technical tools remain at the end of the navigation so the
main workflows stay comprehensible.

## Readiness and safety

Live actions require the reviewed Schedule I build, a loaded save, the local
bridge, and solo-host authority. Previewed operations use revision checks and
readback verification before reporting success.

Offline writes are refused while Schedule I is running. Every supported offline
apply creates a full backup and validates the affected JSON before replacement.
The local bridge is same-user, one-client, allowlisted, and versioned; it does
not expose TCP/HTTP access or arbitrary command execution.

V2.2.1 keeps deal totals and product unit prices as separate controls. Eligible
solo-host workflows can raise the reviewed vanilla bounds through the exact
technical maximum of `$16,777,215`, with preview and verification gates.

## Package contents

- `ScheduleIControlCenter.exe` — root attach-and-launch executable
- `ScheduleI-ControlCenter\dist\ScheduleIControlCenter.exe` — graphical app
- `ScheduleI-ControlCenter\dist\ScheduleIControlCenter.Cli.exe` — offline CLI
- `Mods\ScheduleIControlBridge.dll` — reviewed live bridge
- `MelonLoader\` and `version.dll` — packaged loader runtime
- `UserData\Loader.cfg` — loader configuration

## Troubleshooting

Open **Diagnostics** first. Refresh health, select the latest incident, and
follow its reasoning and next actions. Redacted reports omit save contents and
common credential material.

If attachment fails, confirm that the package was fully extracted, rerun the
root launcher outside the game directory, and verify that Steam's Schedule I
installation is accessible to the current Windows user.

## Trust and privacy

Control and save operations remain local. On startup, the graphical app makes
a read-only HTTPS request to this repository's public GitHub Releases API to
check for a newer stable version. It does not upload a user name, save data,
diagnostic report, or application settings. A release ZIP is downloaded only
after the user selects **Download and install**. See
[PRIVACY.md](PRIVACY.md), [SECURITY.md](SECURITY.md), and the
[code signing policy](SIGNING_POLICY.md).

## Version & application updates

Updates are sourced only from stable, versioned GitHub Releases in this public
repository. The Control Center accepts the documented complete-package asset,
requires a GitHub-provided SHA-256 digest, validates executable publisher and
version metadata, rejects unsafe archive paths, backs up every replaced or
removed managed file, and rolls the transaction back if installation fails.

The package is synchronized as a unit, so releases can update the GUI, bridge,
launcher, CLI, documentation, and supporting runtime files. Files created by
the user—saves, Control Center backups, diagnostics, install records, and update
history—are outside the managed release manifest and are not removed. A stable
release must use a newer semantic version tag for installed copies to detect it.

## Release identity

- Control Center: **V2.2.1** (`2.2.1.0`)
- Bridge: **v1.3.1**
- Reviewed Schedule I build: **0.4.6f13**

This project is an independent utility and is not affiliated with the Schedule
I developers or publisher.
