# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

This record scopes the product/documentation website, not the Windows executable.

## Users

People with multiple Windows displays who temporarily want their application
windows on one display. Contributors need a separate, practical onboarding path.
The owner approved a screenshot-led site using the existing Windows-native identity.

## Product Purpose

Window Gather borrows windows from other displays, then restores only those
borrowed windows. Destination-resident windows stay untouched.

## Capabilities and Constraints

The current published release is identified by its GitHub tag, not historical
local development versions. Windows x64 and ARM64 packages are self-contained
with runtime extraction. Recovery is local and cannot resurrect closed windows.
No telemetry, analytics or automatic elevation. Current source can temporarily
change the Windows primary display only through an off-by-default gathering option.
An independent off-by-default option lets recorded new windows follow the primary
on explicit Restore. These options are not in the existing v1.0.0 release.
Current source also has an off-by-default Start with Windows option that registers
the current user's executable for quiet tray launch after sign-in. Windows startup
approval/policy remains authoritative; this is not in v1.0.0 either.
The owner selected the MIT License; third-party licenses remain separate.
Pages setup is not authorized. Private vulnerability reporting is enabled.

## Evidence on Hand

Canonical usage: `WindowGather/README.md`. Architecture: `ARCHITECTURE.md`.
Releases: `RELEASE.md`. Real WinUI test composition supports safely simulated
desktop data. Screenshots must come from that executable and be labeled.
No customer claims, benchmarks or testimonials are available.

## Product Principles

- Demonstrate the real interface, not a fabricated product mockup.
- Explain borrowing, resident exclusion and pending recovery plainly.
- Keep installation and contributor instructions short and actionable.
- Share canonical documentation between repository and website.
- Never conflate local validation with deployed or clean-machine verification.
