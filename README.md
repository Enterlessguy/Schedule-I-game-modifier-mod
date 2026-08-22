# Schedule I Control Center V2

Schedule I Control Center is a Windows companion application for reviewed live
controls and guarded offline save tools, presented in the Intelligence Database
visual identity.

V2 introduces the redesigned control-center interface, clearer workspace
navigation, the Intelligence Database startup sequence, expanded diagnostics,
and a one-click attach-and-launch package.

> Read [RELEASE_NOTES_V2.md](RELEASE_NOTES_V2.md) for the complete V2 summary.

The complete Intelligence Database source for the launcher, graphical
application, command-line application, live bridge, protocol, and regression
tests is published under the [MIT licence](LICENSE) in
[`ScheduleI-ControlCenter/`](ScheduleI-ControlCenter/).

## Install and launch

1. Download and extract the V2 package outside the Schedule I installation.
2. Run the root `ScheduleIControlCenter.exe`.
3. Confirm the detected Schedule I folder when prompted.
4. The launcher attaches the packaged runtime and opens the Control Center.
5. Start Schedule I, load the intended save, and confirm the readiness badges
   before using live controls.

The root launcher does not require elevation, modify saves, delete files, or
contact a network service. It copies only the packaged runtime into the
confirmed game folder and records an install summary for troubleshooting.

## V2 workspaces

- **Overview** — readiness, recommended workspaces, and ordered command logs.
- **Market intelligence** — fair-value synchronization, sell-price and deal
  limits, and customer allowances.
- **Player & inventory** — inventory capacity, virtual hotbar pages, movement
  speed, preview, apply, and verification.
- **Business operations** — laundering limits and reviewed property controls.
- **Drug effects** — supported product and effect workflows.
- **Save & safety** — backup, validation, console enablement, and protected
  offline operations.
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

V2 keeps deal totals and product unit prices as separate controls. Eligible
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

The application operates locally and does not transmit user information. See
[PRIVACY.md](PRIVACY.md), [SECURITY.md](SECURITY.md), and the
[code signing policy](SIGNING_POLICY.md).

## Release identity

- Control Center: **V2** (`2.0.0.0`)
- Bridge: **v1.1.0**
- Reviewed Schedule I build: **0.4.6f13**

This project is an independent utility and is not affiliated with the Schedule
I developers or publisher.
