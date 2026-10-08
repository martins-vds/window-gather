# Window Gather 2.0.0 portable release

The release uses the verified x64, self-contained WinUI single-file configuration.
It is one distributable EXE with runtime extraction, not an extraction-free app.
The old 1.2 frontend/history is retained. Recovery schema 1, storage paths,
numeric shortcut settings and the shared mutex are unchanged.

From a clean, committed checkout on Windows with the pinned SDK:

```powershell
.\scripts\Release.ps1
```

The script checks executable/manifest version consistency, publishes the EXE,
archives the exact Git commit's source, packages usage documentation and writes
SHA-256 checksums. It refuses to overwrite an existing versioned release directory.
No GitHub Release is created or uploaded by this script.

The persistent output directory is
`artifacts\releases\WindowGather-2.0.0-win-x64`:

| Path | Content |
| --- | --- |
| `portable\WindowGather.WinUI.exe` | Version 2.0.0, x64 self-contained application |
| `portable\README.md` | Usage, shortcuts, recovery and limitations |
| `portable\source` | Exact committed repository source, excluding build outputs |
| `portable\RELEASE.json` | Version, source commit, RID and EXE checksum |
| `portable\SHA256SUMS.txt` | EXE SHA-256 |
| `WindowGather-2.0.0-win-x64-portable.zip` | Complete portable folder contents |
| `SHA256SUMS.txt` | ZIP and EXE SHA-256 |

Extract the ZIP, then open `WindowGather.WinUI.exe`. Exit an older frontend first.
The application is unsigned. Do not bypass a Windows security warning on behalf
of someone else; follow your organization's policy for inspected unsigned tools.

For safe local startup verification, inspect the versioned title and existing
recovery/shortcut UI, then use Exit without gathering, restoring, forgetting or
saving settings. Local startup is not evidence of clean-machine support: no
clean VM/runtime-free Windows installation was available during migration
verification. Native ARM64 publishing and the other quality measurements are
documented separately in `ARCHITECTURE.md`.
