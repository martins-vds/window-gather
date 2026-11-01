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
Current source supports standard-user execution only and rejects elevated or
SYSTEM launches before desktop initialization. Do not run portable packages as
administrator: single-file runtime extraction occurs before the managed launch
check and that check cannot secure an elevated extraction directory. Protected
or elevated applications may reject inspection, markers or movement; errors
are reported instead of silently claiming success. Historical releases are unchanged.

Recovery metadata lives at `%LOCALAPPDATA%\WindowGather\session.json`; shortcut
settings at `shortcuts.json` in the same directory. Recovery records contain
window handles/process IDs, process-start identity, window class, monitor
identifiers and placement geometry. They do **not** store titles, browser URLs
or document contents. Do not share these files casually. They are local JSON
files, not encrypted storage, and are not a defense against another process
running as the same Windows user. Current storage restricts directory/file ACLs
to the owning user and SYSTEM, rejects symlink/junction paths and uses exclusive,
random-name temporary files with flush-to-disk and atomic replacement.
These checks are defense in depth, not a boundary against a hostile same-user
process racing filesystem operations. Do not delete recovery casually: it holds
the return positions for borrowed windows.

The app has no telemetry, networking, auto-update or display-setting changes.
GitHub download/build tooling is separate from the runtime executable.
Use least privilege; do not bypass OS warnings on another user's behalf.

Shortcut-triggered Gather/Restore now requires a foreground confirmation whose
default is Cancel. Gather resolves its destination before showing the dialog.
This limits unsolicited shortcut movement and accidental exposure on shared
displays. Windows messages and per-window markers do not authenticate another
process in the same user/session; a malicious same-user process can manipulate
the desktop directly. Buttons/tray movement remains explicit user action.

## Release integrity

Published packages are currently **unsigned**. Verify ZIP SHA-256 against
`SHA256SUMS.txt` from the same release and inspect the source/manifest.
Checksums detect mismatches; they do not replace a trusted signer or protect
against a compromised release account. Optional Azure signing is documented in
[RELEASE.md](RELEASE.md), but is not claimed active. Signing remains optional by
maintainer decision; unsigned publisher authenticity is an accepted residual risk.
Required signing fails closed and an enabled repository policy cannot be
downgraded with a manual `disabled` input.

The repository's main branch requires pull requests and passing verification,
including administrators. Stable release tags prohibit updates/deletion.
The `release` and `artifact-signing` environments require maintainer approval,
disallow administrator bypass, and allow only main or `v*` tags. These settings
are GitHub configuration, not guarantees enforced solely by checked-in YAML;
maintainers should audit them periodically. Account administrators can still
change the configuration, and approvals do not prove a build is benign.

Publishing uses checked-in per-RID/per-format NuGet graphs, locked restore and
`--no-restore` publication. Actions are SHA-pinned; ordinary verification tokens
are explicitly read-only with checkout credential persistence disabled.

Versions/commit identity and final-byte hashes are recorded in each manifest.
Assets/tags are not silently replaced. Self-contained executables extract their
bundled runtime on startup; no clean-machine verification guarantee is implied.
