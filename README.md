# Underthrone (codename)

A first-person dark-fantasy action MMO: Souls-style combat, Diablo-style loot, and a
Daggerfall-scale world drawn with 8-direction billboard sprites and a chunky retro render.

This repo is the **Phase 1 combat slice**: one arena, several players online together, Barrow
Ghouls that telegraph and punish, and server-authoritative dodge/parry/block that stays fair
at ~100 ms of ping.

## Layout

| Path | What it is |
|---|---|
| `shared/` | Simulation rules + wire protocol in plain C#. A Unity local package **and** compiled by the .NET server, so client prediction and the server run identical code. |
| `dotnet/Underthrone.Server` | Authoritative game server (.NET 8, UDP via LiteNetLib, 30 Hz fixed tick). |
| `dotnet/Underthrone.Tests` | xUnit tests: combat timings, latency forgiveness, prediction, protocol, world, and a real UDP round trip. |
| `client/` | Unity 6 (6000.5) client: first-person controller with prediction, interpolated remote entities, 8-direction billboards, retro low-res render. |
| `docs/ARCHITECTURE.md` | How the netcode and combat fit together. |

## Run it

Requires the .NET 8 SDK and Unity 6000.5.

```bash
# Tests
dotnet test

# Server (UDP 7777)
dotnet run --project dotnet/Underthrone.Server -c Release
```

Client:
1. Unity Hub > **Add project from disk** > select `client/`. First import takes a few minutes.
2. If Unity asks to enable the new Input System backend, choose **Yes** (it restarts).
3. Menu **Underthrone > Create Dev Scene**, then press **Play** with the server running.
4. For a second player, make a build (File > Build Profiles) and run it alongside the editor.

Controls: WASD move, mouse look, LMB attack, RMB block (raise it just before a hit lands to
parry), Space dodge, Esc frees the cursor.

The ghoul tints warm while it winds up (the telegraph) and blue-grey while staggered (your
punish window). Art is generated placeholder silhouettes until real sprite sheets go in;
`DirectionalSpriteSet` takes Aseprite exports in S, SW, W, NW, N, NE, E, SE order.

## Status

- [x] Shared deterministic player/enemy simulation and combat rules
- [x] Binary protocol, server tick loop, lag compensation, defender-favoured hit checks
- [x] Client prediction + reconciliation, snapshot interpolation
- [x] 8-direction billboards, retro low-res camera, dev HUD
- [ ] Real sprite art, hit-stop, audio, animation frames
- [ ] Instanced dungeons, shrines, corpse run
- [ ] Accounts + PostgreSQL persistence, loot
- [ ] Streaming world zones, layering, interest management
