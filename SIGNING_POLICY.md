# Code signing policy

Schedule I Control Center is maintained, reviewed, and released by Intelligence
Database. Repository write access and release approval require multi-factor
authentication.

The project is applying for free code signing provided by SignPath.io,
certificate by SignPath Foundation. Until that application is approved, GitHub
release notes clearly identify whether an artifact is signed.

Only the Control Center launcher, graphical application, command-line
application, and Schedule I Control Bridge built from this repository are
eligible for the project signing process. Upstream binaries are never signed as
Intelligence Database software.

Every signing request must correspond to a tagged source revision, pass the
documented build and test checks, and receive manual release approval from
Intelligence Database. Product names and versions in the binaries must match the
approved release.

The application privacy statement is available in [PRIVACY.md](PRIVACY.md).
