# Tag-driven Window Gather releases

The strategy adapts the [desktop-computer-use release pipeline at
3a660fec](https://github.com/martins-vds/desktop-computer-use-mcp-server/blob/3a660fec2b8e0a32296ba4203c61c3b6f8564dd1/.github/workflows/release.yml)
to the existing WinUI utility. It publishes Windows x64 and ARM64 only. Old
1.2, 2.0.0 and 2.0.1 artifacts are retained; neither desktop behavior nor
recovery/settings formats are changed by release engineering.

## One version source

Release tags must be canonical stable `vMAJOR.MINOR.PATCH`, for example `v2.1.0`.
Missing `v`, prereleases, truncated versions, leading zeros and components
outside 0..65534 are rejected (65535 is reserved by CLR assembly versioning).
The current development default remains 2.0.1; a release tag overrides it.

| Metadata for `v2.1.0` | Value |
| --- | --- |
| Version / PackageVersion / InformationalVersion | `2.1.0` |
| AssemblyVersion / FileVersion / embedded WinUI native manifest | `2.1.0.0` |
| Window title | `Window Gather 2.1.0`, derived from the actual CLR assembly |
| RepositoryCommit / release manifest commit | Full tagged source commit, separate from product version |

Release publishing explicitly passes all version properties,
`IncludeSourceRevisionInInformationalVersion=false` and
`ContinuousIntegrationBuild=true`. The native manifest is copied and versioned
under ignored `obj`, preserving DPI, compatibility and long-path declarations.
The source manifest and legacy frontend version are not rewritten.
Managed assembly, native PE product/file versions, architecture, embedded
manifest and assembly source metadata are inspected without executing the app.

The pinned C# 14, SDK, packages and ordinary lock files stay in effect.
RID/tag-specific restore graphs use `obj\packages.release-<RID>-<tag>.lock.json`;
they never overwrite ordinary checked-in locks.

## GitHub workflow

After this pipeline is reviewed and present on the intended source commit:

```powershell
git tag v2.1.0
git push origin v2.1.0
# Or release manually (a new tag uses the selected workflow commit):
gh workflow run release.yml -f tag=v2.1.0 -f artifact_signing=disabled
```

No tag, remote push or actual GitHub Release is created by local packaging.
The workflow triggers on pushed `v*` tags and supports manual dispatch with
required `tag` and `artifact_signing` (`auto`, `enabled`, `disabled`) inputs.
For an existing tag, manual dispatch resolves it and checks out its exact
commit in every test/build/package/release job, not the dispatch branch's source.
For a new tag, it pins the dispatch event's full commit SHA in every job.
The final publication job creates that tag only after quality gates, final
package verification and GitHub authentication succeed. A normal non-force
tag push reserves the exact source identity; concurrent conflicting creation
fails rather than moving or replacing a tag. No tag is created during preparation.
For a new tag, both preparation and publication compare the source's workflow
files with the current default branch. GitHub's automatic token cannot acquire
`Workflows: write`; creating a tag for a different workflow revision is rejected
even with `contents: write`. If main's workflows change during a release, the
job fails with an actionable message rather than retargeting the artifacts.
Dispatch a new run from current main, or let an authorized maintainer create
the exact original source tag before retrying. Neither repository-wide write
defaults nor a nonexistent YAML `workflows` permission fixes this restriction.
Tags from before this pipeline exists cannot use its packaging scripts.

This matches the reference's manual first-release capability, but avoids its
unconditional branch checkout and asset-clobber fallback. A missing tag is
distinguished from Git authentication/transport failure; only a missing tag
on a manual dispatch may use the dispatch SHA. Tag push events must still
resolve their existing tag and match the event source.

The workflow gates both RIDs on portable tests, dependency checks, coverage,
method CRAP, release contracts, frontend builds and both core mutation gates.
Hosted runners never execute native desktop or GUI operation tests.
Those remain separate local interactive suites touching owned windows only.

Unsigned packaging has read-only repository permissions and no OIDC privilege
or Azure login. Required signing runs in a separate protected `artifact-signing`
job with `id-token: write`; only final publication receives `contents: write`.
Azure actions are pinned to the reference's immutable SHAs.

All action references are pinned to verified stable release commits (reviewed
2026-10-09). The JavaScript actions use Node 24 on GitHub-hosted
`windows-latest`; self-hosted runners would need at least runner 2.327.1.
Release preparation/publication retain credentials for authenticated Git
fetch/push. Artifact uploads explicitly keep `archive: true`,
preserving named build/release containers instead of direct-file uploads;
downloads decompress that outer container and fail on digest mismatch.
The inner distributable ZIP and its checksums are unchanged.

The final job verifies both archives and their exact committed source, writes
`SHA256SUMS.txt`, checks (or reserves a new manually requested tag for) the exact
artifact source, and creates `Window Gather vX.Y.Z` with generated notes. Authentication/API errors
fail rather than being treated as an absent release.
Existing assets are **immutable**: an identical rerun does nothing, and any
changed/missing/additional asset fails without `--clobber`. A partial publication
requires deliberate maintainer recovery; it is never silently repaired or
replaced. Changing signing policy or producing different signed bytes requires
a new version/tag. ZIP entry timestamps are taken from the source commit,
although byte reproducibility across changed tools/runners is not promised.

## Local, unsigned packages

From a clean committed checkout, with the pinned SDK on Windows:

```powershell
.\scripts\Release.ps1 -Tag v2.1.0 -Runtime win-x64
.\scripts\Release.ps1 -Tag v2.1.0 -Runtime win-arm64
```

A local candidate tag need not exist; the script does not create one.
If it exists, it must identify HEAD. `-ExpectedCommit <SHA>` additionally pins
the checkout. Existing output directories are rejected instead of overwritten.
`-Stage Build`, `ValidateBuild` and `Package` split building from signing for CI;
normal local `All` builds and packages unsigned. `-SigningRequired` fails unless
a valid timestamped signature has been applied between Build and Package.
SHA-256 digest/RFC3161 timestamp verification uses SignTool plus Authenticode;
there is no unsigned fallback when signing is required.

Outputs under `artifacts\releases\v2.1.0\<RID>` include the staged portable folder,
build identity, checksums and respectively:

- `window-gather-windows-x64-v2.1.0.zip`
- `window-gather-windows-arm64-v2.1.0.zip`

Each archive contains `WindowGather.WinUI.exe`, usage `README.md`, `VERSION`,
`release-manifest.json`, internal `SHA256SUMS.txt` and `source.zip`.
The source ZIP is the exact Git archive of the recorded commit, including the
commit comment; verification compares its hash to a fresh archive of that tree.
The manifest records tag/version/assemblyVersion, component `Window Gather`,
RID, commit, Authenticode state/provider and hashes of the **final** EXE/source.
Outer checksums describe final distributable archives, after any signing.

These are unpackaged, self-contained WinUI single executables **with runtime
extraction**, not extraction-free apps. Publishing preserves
`WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`,
`SelfContained=true`, `EnableMsixTooling=true`, `PublishSingleFile=true`,
`IncludeAllContentForSelfExtract=true` and native-library extraction.

Choose x64 for AMD64 Windows or ARM64 for native ARM64 Windows
(`$env:PROCESSOR_ARCHITECTURE`). Extract the archive and run the EXE directly:

```powershell
Get-FileHash .\window-gather-windows-x64-v2.1.0.zip -Algorithm SHA256
# Compare with the release SHA256SUMS.txt before extracting.
.\WindowGather.WinUI.exe
```

Do not prefix it with `dotnet`. No separate .NET/Windows App SDK installation
is required. Exit an older frontend through its tray before running a new one.
Do not bypass OS/security warnings on someone else's behalf.

## Optional signing readiness (manual external setup)

Default `auto` releases are unsigned while repository variable
`AZURE_ARTIFACT_SIGNING_ENABLED` is unset or exactly `false`. Exactly `true`
requires signing; other values fail validation. Explicit `enabled` requires
signing even if the default is false; `disabled` never logs into Azure.

Before enabling signing, maintainers must arrange Azure identity verification,
an Artifact Signing account/certificate profile and a federated OIDC identity
authorized to sign that profile. Create/protect the GitHub `artifact-signing`
environment with appropriate reviewers/tag restrictions and configure:

| Scope | Names |
| --- | --- |
| Repository variable | `AZURE_ARTIFACT_SIGNING_ENABLED` |
| Protected environment secrets | `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` |
| Environment/repository variables | `AZURE_ARTIFACT_SIGNING_ENDPOINT`, `AZURE_ARTIFACT_SIGNING_ACCOUNT`, `AZURE_ARTIFACT_SIGNING_PROFILE` |

OIDC federation must match this repository's protected environment, not a copied
identity from the reference repository. Enable GitHub Actions and allow the
workflow's scoped release write permission; any ruleset restricting tag/release
creation must allow the chosen maintainer/workflow. This implementation creates
no roles, secrets, Azure resources, signing accounts or paid infrastructure.
Signed mode remains structurally validated only until these external resources
are configured and a real signing run is explicitly authorized.

## Verification

`scripts\Test-Release.ps1` runs in the ordinary verification pipeline and uses
mocked network/GitHub commands for publication contracts. It covers strict
version bounds/overflow, signing policies, archive identity/hashes, repeatable
entry timestamps, manual first releases, annotated/existing tag source selection,
tag-creation races, auth failures and immutable reruns. Local packaging must also
exercise a tag override different from the development version and both RIDs.
Real signing, hosted Actions execution and clean-machine startup are distinct
checks; local metadata/package validation does not establish those claims.

An old failed run's **Re-run** button retains its original workflow/source SHA.
After a pipeline correction, dispatch a new run from the corrected branch instead.
Manual dispatch with a new tag is a real release request: it can create a tag
and publish assets after the gates pass, not merely validate the pipeline.
