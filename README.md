# Garbati.OrchestratorApi

GARBATI's transaction orchestrator. It runs the ordered steps of a customer operation — account opening, transfers — across [IdentityApi](https://github.com/AbubakarImam/Garbati.IdentityApi) and [LedgerApi](https://github.com/AbubakarImam/LedgerApi). It owns no truth of its own about customers, products or balances; it only decides the order things happen in and tracks in-flight state so a crash or retry never leaves things half-done.

**No code exists yet.** This repository currently holds only the design: [`docs/DESIGN.md`](docs/DESIGN.md) is the source of truth.

## Where this fits

See [`garbati-platform/ARCHITECTURE.md`](https://github.com/AbubakarImam/garbati-platform/blob/main/ARCHITECTURE.md). In short: **Channel → Orchestrator → IdentityApi → Orchestrator → LedgerApi**.

## Status

Design v1 (minimal), ratified 16 October 2026 (`garbati-platform` decision D-1, revised that day). Deliberately covers only **account opening and a plain transfer** — no fee, no reversal, no ProductApi, no Channel service. See `docs/DESIGN.md` Section 1.1 for exactly why those were cut, and Section 3 for what has to change to add each one back.
