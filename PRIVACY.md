# Privacy

Schedule I Control Center performs game, save, backup, and diagnostic work
locally. It does not upload this information to Intelligence Database.

The graphical application checks the public GitHub Releases API for updates
over HTTPS when it starts and when the user selects **Check for updates**. The
request identifies the application and installed version through a standard
HTTP user-agent and necessarily exposes ordinary connection metadata, such as
the user's IP address, to GitHub. It does not include the Windows user name,
save paths or contents, diagnostic reports, command history, or settings.

The latest stable release version, release notes, asset URL, size, and SHA-256
digest are cached locally so the Version & Updates page can show its last-known status
while offline. A release package is downloaded from GitHub only after the user
selects **Download and install**. Update archives, verification metadata,
managed-file manifests, rollback copies, and the last installation result are
stored locally under the Control Center update directories.

The application reads the selected local Schedule I installation and save
environment to provide its documented controls. Backups, configuration,
diagnostic incidents, and exported diagnostic reports remain on the user's
computer. The startup greeting reads the current Windows user name for local
display only; it is not stored or transmitted by the application.

Diagnostic exports redact common credential forms and do not intentionally
include save contents. Users should still review a report before sharing it.

The packaged MelonLoader runtime is an independent third-party component with
its own notices and policies.

GitHub processes update-check and download connections under its own privacy
statement: https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement
