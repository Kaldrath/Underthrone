# Architecture

## One simulation, two runtimes

`shared/Runtime` holds every gameplay rule as plain C# with no UnityEngine dependency
(`noEngineReferences` in its asmdef, `netstandard2.1` + C# 9 in its csproj). Unity loads it as a
local package (`client/Packages/manifest.json` -> `file:../../shared`); the server compiles the
same files through `dotnet/Underthrone.Shared`. Anything that builds in `dotnet build` builds
in Unity.

- `Sim/PlayerSim` — movement, stamina, light attack (windup/active/recovery), dodge with
  i-frames, block with a parry window, stagger, death. All timings are in ticks
  (`CombatTuning`, 30 ticks = 1 s).
- `Sim/EnemySim` — Barrow Ghoul: chase, 500 ms telegraph that tracks then commits, strike,
  punish window; poise during its windup, staggered by a parry.
- `Sim/CombatResolver` — hit geometry and the defender-favoured strike check.
- `Protocol/` — little-endian binary messages; a truncated packet throws `ProtocolException`
  and the server disconnects that peer.
- `Client/ClientPrediction`, `Client/SnapshotInterpolator` — pure-C# netcode used by Unity.

## Netcode

**Server authority.** The server owns every body. Clients send inputs, never positions.

**Inputs.** Each client tick sends its last 4 inputs over an unreliable channel, so a lost
packet is covered by the next one. The server queues inputs by tick, ignores stale or
duplicate ones, and processes one per tick; if none arrived it repeats the last (held buttons
stay held, no new press fires).

**Prediction + reconciliation (local player).** The client steps `PlayerSim` with its own input
immediately. Each snapshot carries the player's full authoritative state and the last input
tick the server processed; the client resets to that state and replays the inputs the server
hasn't seen. With no interference the replay lands exactly where prediction was (tested), and
small corrections are eased in visually.

**Interpolation (everyone else).** Remote players and monsters are drawn 3 ticks (100 ms) in
the past, blended between the two snapshots around that time. Discrete state (action, health)
never shows early.

**Lag compensation, two directions.**
- *Your hits:* the server tests a swing against where the target was when you saw it: one-way
  latency + the 3-tick interpolation buffer, capped at `MAX_LAG_COMPENSATION_TICKS` (200 ms).
- *Their hits on you ("favour the defender"):* a strike landing at tick T is checked against
  your last few ticks of defence (same cap). If you were in i-frames or a parry window inside
  that window, it counts, because you pressed it in time on your screen.

## Combat feel targets

Slightly slower than Dark Souls so it survives ~100 ms: 267 ms player windup, 500 ms ghoul
telegraph, 233 ms dodge i-frames, 200 ms parry window. Tune in `CombatTuning`; tests pin the
tick counts.

## Scaling plan (later phases)

- Zones as separate server processes; open-world layers of ~100-200 players.
- Interest management on a spatial grid (Daggerfall-style terrain tiles double as the grid),
  so snapshots only carry nearby entities.
- Instanced dungeons for 1-6 players where the hardest fights live.
- Accounts, characters and items in PostgreSQL behind a login/world-directory service.
- Monster pathfinding with DotRecast navmeshes on the server.
