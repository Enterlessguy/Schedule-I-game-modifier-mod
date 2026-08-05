Schedule I Control Center v0.4.0 - non-game runtime package
==========================================================

This folder mirrors files relative to the Schedule I installation root.

Full documentation: README.md in this folder. Release ZIP password for
distribution: INTEL DATABASE.

One-click attach and launch (recommended)
-----------------------------------------
1. Close Schedule I and the Control Center.
2. Run ScheduleIControlCenter.exe from this folder (the file next to this
   readme).
3. The launcher searches for Schedule I - first in the default Steam location,
   then across your fixed drives - and shows the folder it found.
4. Confirm "Attach and launch" when asked. It copies this package's contents
   into the game folder, then starts the Control Center automatically.

The launcher never deletes files and does not need administrator rights. It
does not touch saves, backups, or bridge setting profiles. Files that already
exist and are identical are left alone; if an older version is present, it is
kept next to the file as a .bak-<timestamp> copy before being replaced. A small
attach record is written to ScheduleI-ControlCenter\InstallRecords.

Installation / restore
----------------------
1. Close Schedule I and the Control Center.
2. Extract this folder's CONTENTS into:
   C:\Program Files (x86)\Steam\steamapps\common\Schedule I
3. Allow matching folders to merge.
4. Start Schedule I, load a save, then run:
   ScheduleI-ControlCenter\dist\ScheduleIControlCenter.exe

The Control Center must remain below the Schedule I game directory. It searches
upward for Schedule I.exe and will not provide live/game-aware features if the
EXE is copied to an unrelated folder.

Included
--------
- ScheduleIControlCenter.exe (launcher: locate, attach, and launch)
- ScheduleI-ControlCenter\dist\ScheduleIControlCenter.exe
- ScheduleI-ControlCenter\dist\ScheduleIControlCenter.Cli.exe
- Mods\ScheduleIControlBridge.dll (v0.4.0)
- version.dll and the installed MelonLoader 0.7.3 runtime tree
- UserData\Loader.cfg

Intentionally excluded
----------------------
- Schedule I game binaries/data (Steam supplies them)
- Saves, Control Center backups, loader logs, crash dumps, and source/build tools
- Bridge market/allowance/deal-limit JSON profiles and their .bak files; these
  are optional user settings, not executable dependencies

System prerequisites (documented, not copied as loose files)
-----------------------------------------------------------
- 64-bit Windows
- .NET Framework 4.8.1 for the WinForms Control Center and launcher
- 64-bit .NET 6.0.36 runtime for this installed MelonLoader build
- Schedule I 0.4.5f2, Steam build 22829923, for live mutation support

Loose copies of installed .NET system files are not portable installers, so
they are not included. Both required runtimes are already installed on the
system from which this package was produced.

Safety
------
The package contains no save files and does not choose a custom deal maximum.
After restore, the deal-total maximum remains the native $9,999 until a user
previews and explicitly applies another value in Sell Values.
