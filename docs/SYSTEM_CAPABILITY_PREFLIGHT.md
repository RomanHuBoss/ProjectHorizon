# TASK-206 - System Capability Preflight

TASK-206 turns Technical Specification section 28 into a runtime diagnostic policy.
It is advisory: it reports whether known evidence satisfies the player minimum or
recommended configuration and suggests a graphics profile, but it does not rewrite
the user's saved graphics profile.

## Evaluated evidence

Portable runtime evidence:

- Windows 10+ x64 or Linux x86_64;
- logical processor count (used as the conservative portable CPU-count proxy);
- physical RAM from `OS.GetMemoryInfo()` when available;
- active Godot rendering method/driver;
- video adapter name/vendor/type and API version;
- free space on the filesystem backing `user://`;
- current Godot video-memory usage as diagnostic context only.

The specification also requires SSD and dedicated-GPU VRAM capacity thresholds.
Godot/.NET do not expose a reliable, portable total-VRAM/SSD-medium query across all
supported backends, so TASK-206 reports those as unknown instead of fabricating a
capacity or failing a valid integrated-GPU configuration.

## Tiers

- `Unsupported`: a known mandatory requirement fails (OS/x64, CPU, known RAM,
  renderer, known storage capacity, known dedicated-VRAM capacity or known SSD).
- `Minimum`: no known minimum requirement fails.
- `Recommended`: minimum passes and the known recommended CPU/RAM/storage,
  primary-renderer and dedicated-GPU policy passes.

Recommended presentation is advisory only:

- Unsupported -> Compatibility;
- Minimum on Compatibility renderer -> Compatibility;
- Minimum on primary renderer -> Low;
- Recommended -> Medium.

High is never auto-selected by TASK-206 because section 28 defines hardware
minimum/recommended configurations, not a guarantee for the High presentation
profile.

## Runtime evidence

Startup prints `TASK-206 system capability ...` followed by a `TASK-206 ... READY`
contract line. F5 evaluates synthetic minimum/recommended/fallback cases plus the
live capture and prints `TASK-206 system capability acceptance PASS/FAIL`.

A live machine that is below the minimum can still produce an acceptance `PASS`:
the acceptance verifies that the detector correctly reports `minimumLive=0`; it does
not falsely claim that the machine satisfies section 28.
