# Security policy

## Report privately

Private vulnerability reporting is enabled for this repository (verified during
the onboarding update). Use GitHub's
[Report a vulnerability](https://github.com/martins-vds/window-gather/security/advisories/new)
flow. Include affected versions/Windows builds, impact, a minimal reproduction
and mitigations. Do not post exploit details, credentials, private window titles,
document contents or recovery snapshots in public issues.

If the private flow is unavailable, open a public issue saying only that you
need a private reporting channel and ask `@martins-vds` to coordinate. Do not
include the vulnerability details until a private channel is agreed.
There is no promised response time or invented security-contact email.

## Supported versions

Security fixes target current `main` and the latest published GitHub release.
At this documentation update that release is **v1.0.0**. Historical local 1.2/2.x artifacts
do not have a maintenance guarantee. Release tags, not the development version
in source or a screenshot title, identify shipped builds.

## Trust and data boundaries

Window Gather is a local Windows desktop utility, not a privilege boundary.
It runs with the user's token and does not automatically elevate. Protected
or elevated applications may reject inspection, markers or movement; errors
are reported instead of silently claiming success.

Recovery metadata lives at `%LOCALAPPDATA%\WindowGather\session.json`; shortcut
settings at `shortcuts.json` in the same directory. Recovery records contain
window handles/process IDs, process-start identity, window class, monitor
identifiers and placement geometry. They do **not** store titles, browser URLs
or document contents. Do not share these files casually. They are local JSON
files, not encrypted storage, and are not a defense against another process
running as the same Windows user. Do not delete recovery casually: it holds
the return positions for borrowed windows.

The app has no telemetry, networking, auto-update or display-setting changes.
GitHub download/build tooling is separate from the runtime executable.
Use least privilege; do not bypass OS warnings on another user's behalf.

## Release integrity

Published packages are currently **unsigned**. Verify ZIP SHA-256 against
`SHA256SUMS.txt` from the same release and inspect the source/manifest.
Checksums detect mismatches; they do not replace a trusted signer or protect
against a compromised release account. Optional Azure signing is documented in
[RELEASE.md](RELEASE.md), but is not claimed active. Required signing fails closed.

Versions/commit identity and final-byte hashes are recorded in each manifest.
Assets/tags are not silently replaced. Self-contained executables extract their
bundled runtime on startup; no clean-machine verification guarantee is implied.
