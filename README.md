# Garbati.OrchestratorApi

GARBATI's transaction orchestrator, built with ASP.NET Core 8, EF Core and PostgreSQL. It runs the ordered steps of a customer operation — account opening, transfers — across [IdentityApi](https://github.com/AbubakarImam/Garbati.IdentityApi) and [LedgerApi](https://github.com/AbubakarImam/LedgerApi). It owns no truth of its own about customers, products or balances; it only decides the order things happen in and tracks in-flight state so a crash or retry never leaves things half-done.

The design, including every trade-off, is in [docs/DESIGN.md](docs/DESIGN.md) — start there for the *why*. This README covers the *how*. See [`garbati-platform/ARCHITECTURE.md`](https://github.com/AbubakarImam/garbati-platform/blob/main/ARCHITECTURE.md) for how this service fits the rest of the platform: **Channel → Orchestrator → IdentityApi → Orchestrator → LedgerApi**.

## What this build covers

This is OrchestratorApi's **first** build task (`garbati-platform` T-022) and its entire v1 scope in one go — `docs/DESIGN.md`'s whole point is that this version is deliberately minimal. It covers:

- `AccountOpenings` — a durable record of the 4-step account-opening sequence (create customer → demo-stub KYC → create ledger account → attach mandate), resumable from wherever it failed.
- `POST /v1/transfers` — authorization check with IdentityApi, then an ordinary single transfer on LedgerApi. No fee, no tax, no reversal.

It deliberately does **not** build fee/tax (no ProductApi yet), reversal, joint-account multi-approval logic of its own, or real Channel/customer authentication. See `docs/DESIGN.md` Section 1.1 for exactly why, and Appendix A for this build's own judgment calls (notably: grant self-verification was left out, and tests run against mocked HTTP clients, not real locally-run IdentityApi/LedgerApi instances — see the PR description for the reasoning and what that means wasn't proven end-to-end).

## Core pieces

- **AccountOpening** — the operation's own durable state: `OperationId` (public, generated), `CustomerId`/`LedgerAccountId` (set as each step succeeds), `CurrentStep`, `Status` (`InProgress`/`Complete`/`Failed`), `FailureReason`. A retry resumes from `CurrentStep`, using derived, deterministic idempotency keys (`orch:{operationId}:create-customer`, `...:create-account`) so an already-completed step is a safe no-op.
- **Clients/** — this service as a client of IdentityApi and LedgerApi: `IIdentityApiClient`/`ILedgerApiClient`, backed by `IHttpClientFactory`-managed `HttpClient`s carrying this service's own `X-Api-Key` to each, and propagating `X-Correlation-ID` downstream.
- **Transfer** — no persistent entity of its own (both downstream calls are already independently idempotent); only an audit trail.
- **AuditLogs** — one row per operation-level event (step started/succeeded/failed, transfer authorized/denied/posted), staged on the same `DbContext` as the change it describes.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (pinned by `global.json`)
- [Docker](https://www.docker.com/) (for the local database and for the tests)

## Getting started

**1. Start PostgreSQL.** The Development connection string expects it on port `5435` (LedgerApi uses `5433`, IdentityApi uses `5434`):

```bash
docker run -d --name orchestrator-postgres -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=orchestratorapi -p 5435:5432 postgres:16-alpine
```

**2. Apply the migrations.** `dotnet-ef` is pinned in the repo's tool manifest:

```bash
dotnet tool restore
dotnet ef database update --project src/Garbati.OrchestratorApi
```

**3. Point it at real IdentityApi/LedgerApi instances.** Edit `src/Garbati.OrchestratorApi/appsettings.Development.json`'s `DownstreamServices` section with their actual base URLs and the API keys they've issued this service (see each sibling's own README for how to issue one with the right scopes — this service needs `identity.customer.write`, `identity.kyc.manage`, `identity.mandate.write`, `identity.authorization.check` from IdentityApi, and `ledger.accounts`, `ledger.transfer` from LedgerApi).

**4. Run the API:**

```bash
dotnet run --project src/Garbati.OrchestratorApi --launch-profile http
```

Swagger UI is at the printed `http://localhost:{port}/swagger` (click **Authorize** and paste `dev-demo-driver-key` — the plaintext behind the dev client configured in `appsettings.Development.json`).

## Running the tests

```bash
dotnet test
```

Service- and controller-level tests run against a real PostgreSQL container started by [Testcontainers](https://dotnet.testcontainers.org/), so Docker must be running. `IIdentityApiClient`/`ILedgerApiClient` are replaced with hand-rolled fakes in every automated test (see `docs/DESIGN.md` Appendix A judgment call #2) — no real IdentityApi/LedgerApi instance is needed to run this suite, and none is contacted.

## Endpoints

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/v1/account-openings` | Runs the 4-step sequence in one call. Returns the operation; `200` if it reached `Complete`, `422` if it ended `Failed` (with `failureReason`/`currentStep`) |
| `GET` | `/v1/account-openings/{id}` | Current state |
| `POST` | `/v1/account-openings/{id}/retry` | Re-runs from `currentStep` onward. A no-op (not an error) if already `Complete` |
| `POST` | `/v1/transfers` | Authorization check, then an ordinary LedgerApi transfer. Returns the transfer result plus the authorization decision id — never the grant token |

Funding is **not** an Orchestrator endpoint — call LedgerApi's `POST /api/deposits` directly (`docs/DESIGN.md` 1.8).

### Status codes

| Status | Meaning |
|---|---|
| `200` | Success |
| `400` | Request failed validation |
| `401` | Missing or unknown API key |
| `403` | The API key lacks the endpoint's scope |
| `404` | Account opening not found |
| `409` | Idempotency key reused with a different request body |
| `422` | Account opening ended `Failed`, LedgerApi reported a failed transfer leg, or IdentityApi denied the transfer's authorization (with `decisionId`/`reasonCode`) |
| `500` | Unexpected error (including an unexpected failure calling IdentityApi/LedgerApi during a transfer — an account-opening step failure is always caught and recorded instead, never a bare 500) |

### Idempotency

`POST /v1/account-openings` requires a client-supplied `idempotencyKey` (1-128 characters: letters, digits, `-`, `_`, `.` or `:`, and must not start with the reserved `orch:` prefix), scoped to the calling API client — the same scheme LedgerApi's `POST /api/accounts` and IdentityApi's `POST /v1/customers` use. A retry with the same key and the same other fields resumes/returns the same operation, not a second one; the same key with different fields gets **409**.

`POST /v1/transfers` also requires an `idempotencyKey`, forwarded to both IdentityApi's authorization check and LedgerApi's transfer call — there is no Orchestrator-side dedup table for transfers, since both downstream calls are already independently idempotent (`docs/DESIGN.md` 1.5).

### The caller-supplied `requestedBy` gap

`docs/DESIGN.md` Section 1.6: there is no real Channel service yet, so `POST /v1/transfers`' `requestedBy` is whatever `CustomerId` the caller puts in the request body — nothing authenticates that it's actually that customer. This is a known, deliberate simplification, not a solved authentication story; state it plainly wherever this is demoed.

## Authentication

Same pattern as LedgerApi/IdentityApi (`ARCHITECTURE.md` Section 5): every request needs an `X-Api-Key` header, and each key is granted **scopes**:

| Scope | Endpoints |
|---|---|
| `orchestrator.account-openings` | `POST /v1/account-openings`, `GET .../{id}`, `POST .../{id}/retry` |
| `orchestrator.transfers` | `POST /v1/transfers` |

A missing or unknown key gets **401**; a valid key without the endpoint's scope gets **403**. This is a separate concern from the credentials this service presents *to* IdentityApi/LedgerApi as their client — see `DownstreamServices` in [Getting started](#getting-started).

### Development keys

`appsettings.Development.json` configures one local-only client. Never use this outside your machine.

| Client | Plaintext key (dev only) | Scopes |
|---|---|---|
| `dev-demo-driver` | `dev-demo-driver-key` | `account-openings`, `transfers` |

### Issuing a key

Only the SHA-256 hash of a key is configured, never the key itself:

```bash
KEY="opk_$(openssl rand -base64 32 | tr '+/' '-_' | tr -d '=')"; echo "$KEY"; printf %s "$KEY" | openssl dgst -sha256 -binary | base64
```

## Logging and audit

Every request gets a correlation id (`X-Correlation-ID`), propagated to every downstream call this service makes to IdentityApi/LedgerApi so one operation's logs line up across all three services. Audit rows (`audit_logs`) are written in the same database transaction as the change they describe. The signed grant IdentityApi issues for a transfer is never logged, persisted or returned by this service — only its decision id.

This is deliberately all GARBATI ships today — no metrics/tracing/alerting stack. See `garbati-platform`'s `OBSERVABILITY.md` for why, and what to reach for if you need more.

## Deployment

The `Dockerfile` builds two images:

```bash
docker build -t orchestratorapi .                                   # the API
docker build --target migrator -t orchestratorapi-migrator .        # applies pending migrations, then exits
```

GitHub Actions (`.github/workflows/ci.yml`) runs the tests and builds both images on every pull request and push to `main`. There is no `deploy` job yet — this service has no App Service, database server or federated credential set up (see the comment in `ci.yml`).

## Project layout

```
src/Garbati.OrchestratorApi/
  Controllers/      HTTP endpoints (AccountOpeningsController, TransfersController)
  Services/         Business logic (AccountOpeningService, TransferService)
  Clients/          This service as a client of IdentityApi/LedgerApi:
                      Interfaces/, Implementations/ (HTTP clients), Contracts/ (downstream DTOs)
  Entities/         AccountOpening, AuditLog
  Data/             OrchestratorDbContext and model configuration
  Migrations/       EF Core migrations
  Validation/       Request validators, IdempotencyKeyRule
  Middleware/       Correlation id, exception-to-status mapping
  Auditing/         Audit trail (IAuditLogger stages rows on the request's DbContext)
  Authorization/    API-key authentication and scopes (for callers of THIS service)
  Configuration/    DownstreamServicesOptions (this service's own outbound credentials)
  Contracts/        This service's own request/response records
  Exceptions/
tests/Garbati.OrchestratorApi.Tests/
  Services/         AccountOpeningService/TransferService tests against PostgreSQL (Testcontainers)
                     and hand-rolled fake downstream clients
  Clients/          IdentityApiClient/LedgerApiClient tests against a fake HttpMessageHandler
  Controllers/, Authorization/, Configuration/, Data/, Validation/, Fixtures/
docs/DESIGN.md      Design document, decisions log, and Appendix A (T-022 build notes)
```

## Out of scope

Fee/tax (no ProductApi). Reversal (IdentityApi's reversal support exists but Orchestrator-side wiring was never part of this design). Joint-account/multi-holder-approval logic of the Orchestrator's own (IdentityApi already handles `RequiredApprovals > 1` server-side). Real Channel/customer authentication. Grant self-verification (left out this build — see `docs/DESIGN.md` Appendix A judgment call #1). See `docs/DESIGN.md` Section 1.1 and "Explicitly out of scope" in the task file for the full list and why.
