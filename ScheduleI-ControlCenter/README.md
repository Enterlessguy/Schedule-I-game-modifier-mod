# Schedule I Control Center source

This directory contains the complete Intelligence Database source for Schedule
I Control Center V2.

## Layout

- `src/` — WinForms graphical application and offline command-line application.
- `launcher/` — bounded attach-and-launch executable.
- `mod/ScheduleIControlBridge/` — local MelonLoader bridge for reviewed live
  operations.
- `protocol/` — versioned local named-pipe contract.
- `tests/` — diagnostics, save-safety, and inventory paging regression tests.
- `src/UpdateService.cs` — stable GitHub Release discovery, verified package
  download, transactional synchronization, rollback, and update history.

Generated binaries, game assemblies, save data, backups, runtime logs, local
configuration, and install records are not source and must not be committed.

## Local build

The Windows build scripts compile the launcher, graphical application,
command-line application, tests, and bridge. The bridge build additionally
requires the compatible MelonLoader runtime and locally generated Schedule I
reference assemblies. These dependencies are referenced for compilation only
and are not project source.

Run:

```text
build.cmd
build-tests.cmd
launcher\build-launcher.cmd
build-mod.cmd
```

The source is released under the repository's [MIT licence](../LICENSE).
Third-party components retain their own terms; see
[THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md).

## Safety boundaries

- Offline writes are refused while Schedule I is running and create backups
  before replacement.
- Live mutations require the reviewed game build, a loaded save, the local
  same-user bridge, and solo-host authority.
- The bridge exposes an allowlisted local protocol, not arbitrary command or
  remote network execution.
- Property ownership is acquire-only and live value changes are previewed and
  verified before success is reported.
- Application updates accept only the complete package from this repository's
  stable GitHub Releases, verify its SHA-256 digest and executable metadata,
  and preserve user-generated state outside the managed package manifest.
