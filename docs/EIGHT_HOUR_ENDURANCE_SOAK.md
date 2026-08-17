# TASK-214 — Eight-Hour Automated Endurance & Recovery Soak

Technical Specification §41.16 requires an automated eight-hour run without a critical failure. TASK-214 adds an explicit real-time certification harness instead of treating short unit/stress tests as equivalent evidence.

## Certification boundary

- F5 validates the harness, failure detectors and synthetic workload model only.
- A release-quality §41.16 certificate requires one uninterrupted real-time run of at least 8 hours.
- A shorter `endurance_soak start <hours>` run is a smoke test and prints `ownerCertification=0` even when it passes.
- A previous checkpoint left in `Running` state is reported as an interrupted run and cannot be resumed into a certificate.

## Long-run workload

The live game continues to run normally. In parallel TASK-214 performs bounded, domain-only synthetic probes every 30 seconds: procedural galaxy generation/replay, kilometre-scale world-streaming planning and deterministic terrain sampling. It does not create Godot Nodes or meshes from worker threads.

Persistence testing uses a separate diagnostic SQLite database under `user://diagnostics`; the player's primary save slot is not used as the endurance test database.

Cadence:

- 1 s — runtime health sample;
- 30 s — synthetic galaxy/streaming/terrain workload;
- 60 s — JSONL heartbeat + latest checkpoint marker;
- 5 min — isolated transactional diagnostic save;
- 15 min — SQLite diagnostics / `integrity_check`.

Hard failure conditions include:

- any new terrain worker failure;
- diagnostic SQLite `integrity_check` other than `ok`;
- more than one concurrent SaveDatabase writer;
- managed-memory growth above 768 MiB from the run baseline;
- queued terrain/world/database work with no progress for more than 120 seconds;
- synthetic domain workload invariant failure;
- inability to write the heartbeat/checkpoint artifact.

## Starting the run

Developer console (`Ctrl+Shift+D` in a debug build or with `--developer`):

```text
endurance_soak start 8
endurance_soak status
endurance_soak stop
```

Automatic command-line start:

```text
--developer --endurance-soak=8
```

Convenience launchers:

```text
tools\run-task214-endurance.cmd <path-to-Godot-4.7.1-mono.exe>
./tools/run-task214-endurance.sh [godot-binary]
```

Artifacts are written to the Godot user directory in `diagnostics/`:

- `task214-endurance-<UTC-run-id>.jsonl` — append-only heartbeat history;
- `task214-endurance-latest.json` — atomic latest checkpoint marker;
- `task214-endurance-<UTC-run-id>.db` (+ SQLite backup/WAL as applicable) — isolated persistence workload.

A valid certification ends with `TASK-214 eight-hour endurance CERTIFICATION PASS` and `ownerCertification=1`.
