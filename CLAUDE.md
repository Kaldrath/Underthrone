# CLAUDE.md

Underthrone (codename): first-person Souls-combat, Diablo-loot MMO with Daggerfall-style
billboard sprites. Read `docs/ARCHITECTURE.md` before changing netcode or combat.

## Commands

```bash
dotnet test                                                # all tests (must stay green)
dotnet run --project dotnet/Underthrone.Server -c Release  # server on UDP 7777
```

The Unity client (`client/`, Unity 6000.5) can't be compiled outside the editor; keep Unity
code thin and put logic in `shared/` where `dotnet test` covers it.

## Rules

- **Gameplay rules live in `shared/Runtime` only**, as plain C# (no UnityEngine, C# 9,
  netstandard2.1). The client predicts with the same code the server runs; duplicating a rule
  on one side breaks prediction.
- **Timings are ticks** (`SimConstants.TICK_RATE` = 30). Tune in `CombatTuning`; update the
  tests that pin tick counts in the same change.
- **The server is authoritative.** Clients send inputs only. Any new action needs: input bit,
  `PlayerSim` handling, protocol field if the client must see it, and tests.
- **Protocol changes bump `Messages.PROTOCOL_VERSION`.** Readers must throw `ProtocolException`
  on bad data, never crash.
- **8-direction sprite order is S, SW, W, NW, N, NE, E, SE** (index 0-7) everywhere: Aseprite
  tags, `DirectionalSpriteSet`, `Billboard8Dir`.
- No allocations in per-tick server or client hot paths (reuse `PacketWriter`, lists).
- Braces always; explicit access modifiers; `sealed` by default; `_camelCase` private fields;
  `UPPER_SNAKE_CASE` consts.
- Unity `.meta` files: commit them with their asset once Unity has generated them.
