# Windows infrastructure safety checks

Run `dotnet run --project .\WindowGather.Infrastructure.Windows.Tests`.
These dependency-free tests use injected native seams; they never invoke native display-setting APIs.
The durable-contract cases use isolated, automatically deleted directories beneath the current checkout
to verify primary recovery JSON, compatibility with the old recovery schema, malformed primary data,
and gathering-preference defaults/validation/persistence. No real application settings are read or written.
Tracker tests use an injected lifecycle event source and never install native hooks.
Startup tests inject an in-memory Run entry: no real registry entry is read, written or removed.
They cover quoted executable paths, per-user/app-specific scope, explicit enable/disable,
relocated paths, refusal to overwrite unknown commands, Windows' command-length boundary
and visible backend failures. Windows Startup apps approval and actual post-sign-in launch
remain separate live-system checks, not claims made by these tests.

The adapter accepts only uniquely identified, extended active physical displays with stable
DisplayConfig paths and matching active/persisted modes. It deliberately rejects cloned displays,
pending registry changes (including enabled modes persisted for inactive outputs), unreadable inactive
registry settings, and restart requests. Consequently some otherwise normal disconnected/docked setups
are deliberately unavailable rather than risking re-enabling a persisted output.
Only `DM_POSITION` is requested; complete original DEVMODEW blobs, including driver extras, are retained.
Effective monitor scale and DisplayConfig rotation/scaling/refresh paths are checked before/after changes.
Mode comparison ignores `dmLogPixels` only when neither snapshot declares
`DM_LOGPIXELS` valid. On the reported machine, all four active outputs returned
144 there while persisted outputs returned zero, with the flag absent in both;
those invalid bytes are not evidence of a pending display change. Valid values
and validity mismatches still block changes, and the original bytes are preserved
in native requests. Geometry, mode, opaque driver bytes and concurrent-change/
rollback safeguards remain unchanged. Regression checks exercise both successful
staging and failed-apply rollback without changing real display settings.

`CDS_TEST` validates each request against the live layout, not a complete future
multi-monitor transaction. Preflight tests the new primary at the origin and
other displays at their current positions; testing the old primary at its future
nonzero position independently produces `DISP_CHANGE_FAILED` on the reported
machine. Full-mode requests fail the same way. The translated layout is still
staged with `CDS_UPDATEREGISTRY | CDS_NORESET`, checked in full before global
apply, and verified afterward with guarded rollback on failure. Preflight does
not prove that the driver will accept the combined transaction. Driver preflight
errors remain fatal before staging; no failure code is ignored or bypassed.

Registry staging is required by Windows for multi-monitor changes. A failed transaction restores only
known app-owned positions and never applies an original topology after a hotplug, mode/scale change,
identity change, or unrelated registry edit. Such races produce an explicit error directing the user
to Windows Display settings. Windows exposes no atomic compare-and-swap for these APIs, so external
display changes racing the final guarded native call cannot be entirely prevented.
