# Project Horizon build, branch and release policy

This document implements PDF v2.0 section 37.

## Branches

- `main` - releasable integration branch. Every push must pass the repository CI workflow.
- `develop` - normal integration branch for the next release.
- `feature/*` - feature work branched from `develop`.
- `fix/*` - defect fixes. Branch from the branch that owns the defect.
- `release/*` - release stabilization. Only fixes, version/changelog work and release metadata.

Pull requests are the merge gate. The `CI / quality` and `CI / debug-exports` jobs should
be configured as required checks for `main`, `develop` and `release/*` in GitHub branch
protection. Repository files cannot enforce GitHub branch-protection settings by themselves.

## CI contract

Every pull request runs `.github/workflows/ci.yml`:

1. restore NuGet dependencies;
2. build C# with `ContinuousIntegrationBuild=true` and warnings as errors;
3. run the standalone section-36 xUnit suite and enforce coverage thresholds;
4. validate every repository JSON file and the normative Industry Content v2 schema;
5. run the isolated save-migration/recovery tests;
6. install the official Godot 4.7.1 .NET editor and mono export templates;
7. export primary Windows x64 and Linux x86_64 Debug profiles;
8. export Windows x64 and Linux x86_64 Compatibility/OpenGL Debug profiles;
9. upload all four debug exports as workflow artifacts.

The export job uses the Godot editor binary with `--headless`. It never uses an export
template as the editor executable.

## Local quality gate

Windows:

```bat
tools\run-section37-quality.cmd
```

Linux/macOS shell:

```bash
./tools/run-section37-quality.sh
```

These commands do not download the 1+ GiB Godot export-template archive. Headless exports
are exercised by GitHub CI, or locally by setting `GODOT_BIN` and running:

```bash
./tools/ci/export-project.sh debug
```

## Versioning

`VERSION` is the application release version. It is intentionally independent of:

- SQLite save schema version;
- Industry Content schema/catalog version;
- `ProjectHorizonGenerator.Version`.

A release tag must be exactly `v<VERSION>` (for example `v0.1.0-alpha.144`). The release
workflow refuses mismatched tags or a version missing from `CHANGELOG.md`.

## Release dry-run and tagged release

Before creating a tag, run `.github/workflows/release.yml` manually with GitHub Actions
`workflow_dispatch`. The manual run performs the complete Release build, tests, coverage,
validation, primary + Compatibility Windows/Linux headless exports, symbols and packaging stages, uploads the
release evidence artifact, and deliberately does **not** publish a GitHub Release.

Pushing a matching `v<VERSION>` tag starts the same workflow. It additionally verifies
that the tag matches `VERSION` exactly and, after every gate passes, publishes the packaged
artifacts as the GitHub Release. The pipeline creates:

- `ProjectHorizon-<version>-windows-x64.zip`;
- `ProjectHorizon-<version>-linux-x86_64.tar.gz`;
- `ProjectHorizon-<version>-windows-x64-compatibility.zip`;
- `ProjectHorizon-<version>-linux-x86_64-compatibility.tar.gz`;
- `ProjectHorizon-<version>-symbols.zip`;
- `release-manifest.json`;
- `SHA256SUMS.txt`;
- `VERSION`;
- `CHANGELOG.md`;
- `RELEASE_NOTES.md`.

The symbols package contains Project Horizon portable PDBs from the Release build/export
pipeline. `SHA256SUMS.txt` covers the distributable archives and metadata before they are
attached to the GitHub Release.

## Godot toolchain pin

CI pins `GODOT_VERSION=4.7.1` and downloads from the official
`godotengine/godot-builds` release assets:

- `Godot_v4.7.1-stable_mono_linux_x86_64.zip`;
- `Godot_v4.7.1-stable_mono_export_templates.tpz`.

Changing the engine version requires updating the Godot .NET SDK reference, CI pin and
export templates together.

## Renderer profiles

Desktop release engineering treats the renderer fallback as a separately exportable profile, not
as an undocumented launch flag:

| Preset | Intended renderer | Driver |
|---|---|---|
| `Windows Desktop` | `mobile` | Vulkan when available; engine fallback allowed |
| `Linux` | `mobile` | Vulkan when available; engine fallback allowed |
| `Windows Desktop Compatibility` | `gl_compatibility` | `opengl3` |
| `Linux Compatibility` | `gl_compatibility` | `opengl3` |

The Compatibility presets set the custom feature `compatibility`; `project.godot` uses that feature
to override `renderer/rendering_method` to `gl_compatibility`. Runtime startup prints the actual
rendering method and driver through `RendererProfileDiagnostics`; acceptance is based on that
evidence rather than on the preset name alone.

`tools/ci/export-project.sh debug|release` exports all four profiles. The tagged release packages
the two primary archives, the two Compatibility archives, symbols, manifest and SHA-256 checksums.
