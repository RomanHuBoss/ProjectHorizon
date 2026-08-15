# Changelog

All notable changes to Project Horizon are recorded in this file.

The project uses Semantic Versioning for application releases. Content schema,
save schema and procedural-generator versions are versioned independently.

## [Unreleased]

### Changed

- Future changes intended for the next tagged release are recorded here.

## [0.1.0-alpha.140] - 2026-08-15

### Added

- Automated pull-request quality gate for dependency restore, warnings-as-errors
  C# build, xUnit/coverage verification, JSON validation and save-migration tests.
- Headless Godot 4.7.1 .NET debug exports for Windows x64 and Linux x86_64.
- Release pipeline with a non-publishing manual dry-run and matching-tag publication, producing
  Windows/Linux release packages, a separate portable-PDB symbols archive, SHA-256 checksums
  and a machine-readable release manifest.
- Repository-level version, changelog and build/release documentation.

### Changed

- Section 36 verification is promoted to a mandatory CI gate before exports.
- Git/LFS and ignore policy is documented as part of the release contract.
