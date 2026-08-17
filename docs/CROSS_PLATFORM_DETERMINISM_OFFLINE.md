# TASK-212 - Cross-Platform Determinism & Offline-First Runtime

## Scope

TASK-212 converts Technical Specification v2.0 acceptance criteria 41.1, 41.2 and 41.11 into executable contracts without changing deterministic generation output.

- Windows x64 and Linux x64 are the required desktop platform families.
- The same reviewed `golden-seeds.v1.json` must pass on both operating systems.
- `ProjectHorizonGenerator.Version` remains the explicit compatibility boundary for intentional generation changes.
- Single-player has no mandatory network dependency; cloud/network features, if introduced later, must remain optional.

## Determinism envelope

`CrossPlatformDeterminismRuntime.BuildCanonicalWorldSignature` creates an invariant UTF-8/SHA-256 signature that covers:

- four coordinate-generated star systems, including negative coordinates;
- star/economy/danger and all generated planet identities/seeds;
- the first starter landable planet environment profile;
- fixed deterministic terrain samples;
- the complete baseline planetary POI fixture.

Floating values are rounded to six decimal places with explicit `MidpointRounding.AwayFromZero` and formatted with `InvariantCulture`. The runtime acceptance repeats the signature under `en-US`, `ru-RU` and `tr-TR`, restoring the player's culture afterward.

This canonical signature supplements rather than replaces the stronger reviewed golden contract from TASK-138.

## Windows/Linux parity CI

CI and Release contain a `determinism-parity` matrix for `ubuntu-latest` and `windows-latest`. Each matrix member builds the same test assembly and runs:

- `GoldenSeedTests` - shared checked-in expected world outputs/checksums;
- `CrossPlatformDeterminismTests` - canonical signature, culture, version and offline policy.

Both operating systems must therefore independently match the same reviewed golden data before downstream artifacts can be produced.

## Offline-first rule

`CrossPlatformDeterminismPolicy.SinglePlayerRequiresInternet` is `false`, cloud features are optional, and the TASK-212 static gate scans production C# for network client/socket stacks (`System.Net`, `HttpClient`, TCP/UDP sockets, WebSocket, gRPC, SignalR). The permitted mandatory production-network dependency count is zero.

Tests, CI package restore and distribution infrastructure are not gameplay runtime dependencies and are outside that source audit.

## Generator-version boundary

TASK-212 intentionally leaves `ProjectHorizonGenerator.Version = 3`. If any reviewed world-generation output changes, the existing golden manifest contract requires the generator version and golden data to be updated in the same reviewed change. Silent algorithm changes are forbidden by specification section 42.14.

## Runtime/F5 boundary

F5 proves the platform-neutral deterministic/offline policy, culture invariance and local replay in the current process. Actual criterion 41.1 launch smoke on native Windows and Linux remains a platform artifact/runtime acceptance step; the CI matrix proves native .NET deterministic parity but is not represented as a manual graphical launch.
