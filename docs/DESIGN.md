# Garbati.OrchestratorApi — Design Document (v1, minimal)

**Status:** Design v1 (minimal) — ratified and **built** (`garbati-platform` T-022, `feat/foundation`, 18 October 2026 — see Appendix A for build notes and judgment calls).
**Last updated:** 18 October 2026
**Service name:** `OrchestratorApi` (`Garbati.OrchestratorApi`)
**Platform:** Garbati — see [`garbati-platform/ARCHITECTURE.md`](https://github.com/AbubakarImam/garbati-platform/blob/main/ARCHITECTURE.md) Sections 2–4 and 10 for the platform-wide flows this document implements a minimal slice of; this document never repeats that file, only this service's detail.
**Decision history:** `garbati-platform/TASKS.md` D-1 (demo scope, revised 16 Oct 2026), D-6 (token-verification timing), D-10 (hosting, still open).

## 1. Purpose and scope

The Orchestrator is the single place that runs the ordered steps of a customer operation across services. It owns **no truth of its own** about customers, products or balances — it calls IdentityApi (who may do this?) and LedgerApi (do it), in order, and tracks the in-flight state of a multi-step operation so a crash or retry never leaves things silently half-done.

### 1.1 What this version covers (D-1, revised 16 Oct 2026)

The demo slice was originally going to include a fee and a reversal. Both were cut from scope for this version, for concrete reasons, not just to save time:

| Originally planned | Cut because | Revisit when |
|---|---|---|
| Transfer **with fee** | Decision P-4 says ProductApi computes fees, the Orchestrator only posts. ProductApi doesn't exist yet, and having the Orchestrator invent its own fee would be a real money-rule decision made by the wrong component. | ProductApi is designed and built. |
| **Reverse** the transfer | IdentityApi's reversal authority (who may reverse, what approval is needed) was deliberately left undesigned — `garbati-platform` T-015. Building a reversal flow now would mean inventing an authorization rule nobody has actually decided. | T-015 is designed. |

**v1 covers exactly:** open a customer and a ledger account for them, fund the account (LedgerApi's existing mock deposit), and transfer between two accounts. No fee, no tax, no reversal, no standing orders, no joint-account approval flow (IdentityApi supports it, but the demo doesn't need it — single-holder accounts only for now).

### 1.2 What "minimal" means architecturally

This version only calls **IdentityApi** and **LedgerApi**. It does not call ProductApi (doesn't exist) or ScheduleApi (doesn't exist). The account-opening sequence (1.3) skips `garbati-platform/ARCHITECTURE.md` Section 10's step 5 ("bind product version") entirely — there is no product to bind to yet. This is a deliberate, documented gap, not an oversight: adding Product-binding back in is future work, tracked implicitly by ProductApi's own build order (`ARCHITECTURE.md` Section 7).

### 1.3 Account-opening sequence (implements `ARCHITECTURE.md` Section 10, minus Product)

| Step | Service | If it fails |
|---|---|---|
| 1. Create customer (pending) | IdentityApi | Nothing else exists yet; retry. |
| 2. KYC verified | IdentityApi | Stop; customer stays pending. **v1 demo stub (decision #6): KYC is auto-approved immediately** — there is no real KYC provider integration (explicitly out of scope in IdentityApi's own design). This is a demo simplification, not a real verification, and must never be mistaken for one. |
| 3. Create ledger account (idempotency key) | LedgerApi | Retry with the same key; LedgerApi's own idempotency guarantees this never creates a second account. |
| 4. Attach mandate | IdentityApi | Account exists but cannot be used (no mandate); retry just this step. |
| 5. Mark opening complete | Orchestrator | The operation record shows the current step (1.4). |

### 1.4 Operation state (new entity, decision #1)

Account opening spans two services across four real steps with no single atomic transaction possible — if step 3 succeeds and step 4 fails, the Orchestrator must know to retry only step 4, never redo step 3. This needs durable state of its own, which is exactly the "in-flight operation state" `ARCHITECTURE.md`'s service map assigns to the Orchestrator.

`AccountOpenings` table:

- `Id` (the operation id, returned to the caller)
- `IdempotencyKey` (caller-supplied; retrying the same opening returns the same operation, never starts a second one — decision #2)
- `CustomerId` (set once step 1 succeeds; null before that)
- `LedgerAccountId` (set once step 3 succeeds; null before that)
- `CurrentStep` (`CreateCustomer`, `VerifyKyc`, `CreateLedgerAccount`, `AttachMandate`, `Complete`)
- `Status` (`InProgress`, `Complete`, `Failed`)
- `FailureReason` (nullable)
- `CreatedAt`, `CompletedAt`

**Retry behavior (decision #2):** `POST /v1/account-openings/{id}/retry` re-runs the sequence from `CurrentStep` onward. Every downstream call (to IdentityApi or LedgerApi) uses a **derived, deterministic idempotency key** — `orch:{operationId}:create-customer`, `orch:{operationId}:create-account`, `orch:{operationId}:attach-mandate` — so a step that already succeeded is a safe no-op if accidentally re-invoked (IdentityApi's and LedgerApi's own idempotency rules return the stored result rather than redoing the work), and only the genuinely incomplete step does new work. This mirrors the same pattern LedgerApi already uses for chain-leg keys (`chain:{chainId}:{sequence}`, decision #45) — a system-generated, caller-unreachable key namespace, not a new mechanism.

### 1.5 Transfer flow (implements `ARCHITECTURE.md` Section 4.1, minus Product)

```text
Caller (Swagger, standing in for a real Channel — see 1.6)
  -> Orchestrator: POST /v1/transfers
       - calls IdentityApi: POST /v1/authorization/requests
           (single-holder case; the requester's own approval satisfies M=1 immediately,
            per Garbati.IdentityApi/docs/DESIGN.md 3.8.1 — no joint-account flow needed here)
       - IdentityApi issues a signed grant if authorized
       - Orchestrator may verify the grant itself as defence in depth (DESIGN.md 3.4)
       - calls LedgerApi: POST /api/transfers (ordinary single transfer, no fee/tax legs)
           (the grant is NOT yet forwarded into this call — see 1.7, D-6)
       - returns the transfer result plus the authorization decision id (jti),
         so the operation is traceable end to end without logging the grant itself
```

No new Orchestrator-side persistent state is needed for a transfer: unlike account opening, this is a short, synchronous call chain where both IdentityApi's grant issuance and LedgerApi's transfer posting are already independently idempotent. If the Orchestrator crashes mid-call, the caller retries with the same idempotency key and both downstream services return their already-stored results rather than double-acting.

### 1.6 Where "the Channel" is, for this demo (decision #3)

No Channel service exists yet, and building one is out of scope here. For this demo, **the caller of the Orchestrator's own API supplies the `CustomerId` directly in the request** — there is no real authenticated session standing behind it. This is a known, deliberate simplification, and it is the exact same gap IdentityApi's own design already flags as unresolved (`Garbati.IdentityApi/docs/DESIGN.md` Section 10, item 5 — the BOLA/"channel identity mapping" open question): nothing here should be read as a solved authentication story. State this plainly wherever the demo is shown, the same way D-6's token-verification gap is stated plainly.

### 1.7 Token issuance vs. verification, for this version (decision #4, applying D-6)

IdentityApi issues a **real** signed grant for every transfer (its own design does not change for a minimal Orchestrator — see `Garbati.IdentityApi/docs/DESIGN.md` decision #27). The Orchestrator may verify that grant itself before proceeding (defence in depth, matching IdentityApi's design 3.4). What this version does **not** do is forward the grant into LedgerApi's transfer request for LedgerApi to independently re-verify — because LedgerApi's own request contract has no field for it yet (that field, and the verification logic, is decision #47, deliberately deferred per D-6 until OrchestratorApi exists to call it — which is now, but #47 itself is still a separate, not-yet-built LedgerApi task). So: the chain of trust runs Orchestrator → IdentityApi (real check) → Orchestrator → LedgerApi (ordinary call, no independent re-check yet). This is the exact "marked seam" D-6 anticipated, now made concrete: once LedgerApi decision #47 is built, this design's transfer flow gains one more field in its LedgerApi call and one more verification step — nothing else about this document changes.

### 1.8 Fund (deposit) step — no IdentityApi call (decision #5)

`POST /v1/account-openings/{id}/fund` (or the caller may call LedgerApi's `POST /api/deposits` directly — see 1.9) does **not** go through IdentityApi. LedgerApi's deposit endpoint is explicitly a **mock** funding flow simulating money entering from outside the system (LedgerApi's own decision #15) — it is not a customer-initiated operation requiring mandate authorization in the first place, in the real world or in this demo. This is consistent with LedgerApi's own documented scope, not a new gap introduced here.

### 1.9 Minimal API surface

| Endpoint | Purpose |
|---|---|
| `POST /v1/account-openings` | Runs steps 1–4 of 1.3 in one call; auto-advances through the demo KYC stub. Returns the operation, including `customerId` and `ledgerAccountId` once complete. |
| `GET /v1/account-openings/{id}` | Current state — which step, and `customerId`/`ledgerAccountId` if set. |
| `POST /v1/account-openings/{id}/retry` | Re-runs from `CurrentStep` onward (1.4). |
| `POST /v1/transfers` | The flow in 1.5. Body: `idempotencyKey`, `requestedBy` (CustomerId — see 1.6), `debitAccountId`, `creditAccountId`, `amount`, `currency`, `narration`. |

Funding is **not** a new Orchestrator endpoint — the demo calls LedgerApi's existing `POST /api/deposits` directly, exactly as LedgerApi's own README walkthrough already does, since 1.8 established that step needs no orchestration.

### 1.10 Service identities and scopes

The Orchestrator authenticates to both downstream services as a trusted service client, using the scopes each service already defines — no new scopes needed on either side:

- **From IdentityApi:** `identity.customer.write`, `identity.kyc.manage`, `identity.mandate.write`, `identity.authorization.check`.
- **From LedgerApi:** `ledger.accounts` (create account), `ledger.transfer` (transfers).

### 1.11 Failure handling

| Result | Behaviour |
|---|---|
| Any step of account opening fails | `AccountOpenings.Status` becomes `Failed` with `FailureReason`; `CurrentStep` shows where. `POST .../retry` resumes from there. |
| IdentityApi denies authorization for a transfer | Stop; do not call LedgerApi. Return the denial reason, not a fabricated success. |
| IdentityApi times out/unavailable | Fail closed (matches `Garbati.IdentityApi/docs/DESIGN.md` decision #10) — do not call LedgerApi. |
| LedgerApi times out after the transfer call | Retry with the **same** idempotency key to learn the real outcome (matches LedgerApi's own existing guidance) — never blindly issue a new transfer. |

### 1.12 Audit

One audit row per operation-level event: account-opening step started/completed/failed, transfer authorized/posted/denied. Includes the correlation id and, for a transfer, the authorization decision id (`jti`) — never the grant token itself (matches both services' existing logging rules).

---

## 2. Decisions log

| # | Decision | Options considered | Decision and status |
|---|---|---|---|
| 1 | Operation state ownership | No persistent state (fully stateless); durable `AccountOpenings` table | **Confirmed:** durable state for account opening only (1.4). Transfer needs none — it leans on IdentityApi's and LedgerApi's own idempotency. |
| 2 | Account-opening retry mechanism | Resume from last successful step; always restart from step 1 | **Confirmed:** resume from `CurrentStep`, using derived deterministic idempotency keys per downstream call so already-completed steps are safe no-ops. |
| 3 | Channel identity for this demo | Build a minimal real Channel; trust a caller-supplied `CustomerId` directly | **Confirmed, with the gap stated plainly:** caller-supplied `CustomerId`, no real authentication behind it. Matches IdentityApi's own flagged BOLA/channel-mapping open question — not a solved problem. |
| 4 | Token forwarding to LedgerApi | Forward the grant now (needs a LedgerApi field that doesn't exist); don't forward it yet | **Confirmed (applying D-6):** IdentityApi still issues a real grant and the Orchestrator may self-verify it, but it is not forwarded into LedgerApi's transfer call until decision #47 exists on the LedgerApi side. |
| 5 | Funding authorization | Route the fund step through IdentityApi; call LedgerApi's mock deposit directly | **Confirmed:** no IdentityApi call. LedgerApi's deposit endpoint is already documented as a mock funding flow, not a customer-mandate operation. |
| 6 | KYC verification for the demo | Require a real provider integration; auto-approve immediately as a stated stub | **Confirmed:** auto-approve, explicitly labeled a demo stub. Real provider integration is out of scope for both this service and IdentityApi's own v2 design. |
| 7 | Fee/tax in this version | Have the Orchestrator compute a fee itself; omit fees entirely until ProductApi exists | **Confirmed (D-1, revised 16 Oct 2026): omit entirely.** Computing a fee here would put a money-rule decision in the wrong component (P-4). |
| 8 | Reversal in this version | Call LedgerApi's reversal with no IdentityApi check; omit reversal entirely | **Confirmed (D-1, revised 16 Oct 2026): omit entirely**, until `garbati-platform` T-015 defines reversal authority. |
| 9 | Repository/deployment shape | Own repo now, merge into one deployable with ProductApi later; design as if already merged | **Chosen for now, not fully settled:** its own repo/deployable, since ProductApi does not exist yet. `garbati-platform` D-10 (Orchestrator + Product in one deployable) stays open — revisit when ProductApi is actually built, rather than restructuring speculatively now. |

---

## 3. Open questions and next steps

1. **D-10 (hosting).** Whether Orchestrator and Product end up in one deployable is still open — decision #9 above explains why this document doesn't need to resolve it yet.
2. **Fee/tax reintroduction.** Once ProductApi exists, the transfer flow (1.5) gains a ProductApi call between the authorization check and the LedgerApi call, and the LedgerApi call becomes a chain (decision #45) instead of a plain transfer. Nothing else in this document should need to change.
3. **Reversal reintroduction.** Once `garbati-platform` T-015 defines reversal authority, this document needs a new section mirroring 1.5, calling whatever IdentityApi endpoint T-015 produces before calling LedgerApi's reversal/chain-reversal endpoint.
4. **Token forwarding.** Once LedgerApi decision #47 exists, 1.7's "marked seam" closes: the transfer call gains a grant field, and LedgerApi becomes the final independent verifier, matching `Garbati.IdentityApi/docs/DESIGN.md` 3.4's full design.
5. **Real Channel / authentication.** Decision #3's gap (trusting a caller-supplied `CustomerId`) must be closed before this is ever exposed to real customers, not just recruiters viewing Swagger.
6. **Joint-account transfers.** This version only exercises IdentityApi's single-holder path. A real joint-account transfer through the Orchestrator (handling a `Pending` approval result, not just `Approved`/denied) is unexercised until a demo or real need calls for it.

---

## Appendix A. T-022 build notes (project skeleton, account opening, transfer)

Built everything in this document's v1 scope in one task, on `feat/foundation`. Two things the task file left as judgment calls, plus the smaller decisions made along the way:

1. **Grant self-verification (1.7/3.4): skipped for this build, not built.** Verifying IdentityApi's ES256 grant locally would need `Microsoft.IdentityModel.JsonWebTokens`/`Tokens` — packages IdentityApi itself already uses to *sign*, but genuinely new to OrchestratorApi. `garbati-platform/AGENTS.md`'s human-only gates list "adds a new dependency" explicitly; this is exactly that, not a borderline case. Beyond the dependency question: LedgerApi does not verify the grant either yet (decision #47 is still deferred), so self-verification here would be pure defence-in-depth with no second consumer downstream acting differently on a failed check — the Orchestrator already doesn't call LedgerApi on an IdentityApi denial, which is the actual safety property that matters today. Revisit once decision #47 exists and this already needs a config shape for IdentityApi's public key anyway.
2. **Testing against mocked HTTP clients, not real locally-run IdentityApi/LedgerApi instances.** `IIdentityApiClient`/`ILedgerApiClient` are plain interfaces; tests use hand-rolled fakes (`Tests/Fixtures/FakeIdentityApiClient`, `FakeLedgerApiClient` — no mocking library, matching this platform's existing test style) with call-count assertions, plus a separate, smaller set of `Clients/` tests that feed a fake `HttpMessageHandler` real response bodies copied from each sibling's actual controllers/middleware, to check this service's own JSON parsing and status-code handling against their real shapes. **What this does not prove:** that LedgerApi's and IdentityApi's *currently deployed* code still returns exactly those shapes — only that this service's code is right about what it read on 18 October 2026. A drift in either sibling's contract would not be caught by this suite; it would only surface as a `DownstreamApiException` at runtime. Chosen over real locally-run instances because standing up two more full services (migrations, seeded API keys, port coordination) inside this task's CI run was a lot of added infrastructure for a first build, and would make `test` flakier/slower for every future PR to this repo. A manual, one-off smoke test against real local instances (not part of CI) is the natural next check before treating this as demo-ready; see the PR description for whether that was actually run.
3. **Two scopes, not one (`orchestrator.account-openings`, `orchestrator.transfers`), not `orchestrator.operations`.** DESIGN.md Section 1.10 only defines what the Orchestrator presents *to* IdentityApi/LedgerApi as a client; nothing in this document decides what it demands of its own callers, so this was genuinely open. A single shared scope was the first instinct, but it also breaks the standard "every scope, held alone vs. every scope except this one" test shape this platform's other two services both use (`AllButClient` would hold *zero* scopes when there is only one to exclude, which `ApiKeyOptionsValidator` correctly refuses as a misconfiguration) — a sign the single-scope design doesn't actually fit the platform's own conventions, not just a test inconvenience to work around.
4. **`AttachMandateRequest` has no `IdempotencyKey` field at all** (confirmed against `Garbati.IdentityApi.Contracts.Requests.AttachMandateRequest`'s live source, not assumed). A retry of just the `AttachMandate` step therefore cannot reuse the derived-key trick steps 1/3 use. Instead, `IdentityApiClient.AttachMandateAsync` turns IdentityApi's 409 (`DuplicateActiveMandateException` — "already has an active mandate on this account") into a specific `MandateAlreadyAttachedException`, which `AccountOpeningService`'s retry path treats as proof the step already succeeded, not a new failure.
5. **KYC demo stub hardcodes `KycTier: "Tier2"`** (decision #6; changed from `"Tier1"` by T-025 — see Appendix B). This assumes the target jurisdiction has a `Tier2` policy seeded on the IdentityApi side — true for `"NG"` in its current seed migration, not guaranteed for an arbitrary `JurisdictionCode` a caller supplies. A real KYC provider would supply a real tier per customer instead of this constant; flagged rather than silently worked around.
6. **A repeat `POST /v1/account-openings` with the same key and body resumes the operation, not just echoes a stored snapshot.** T-016/T-017's idempotency convention (return the stored result, don't redo the work) is mirrored for *creating* the row, but because this operation is multi-step, "the stored result" for an incomplete operation is itself an invitation to keep making progress — the same derived per-step keys that make `POST .../retry` safe make this safe too. A different body with the same key is still a 409, unchanged from the established convention.
7. **Build-time gotcha worth recording:** the Dockerfile's two-stage `COPY csproj → restore → COPY full src → publish --no-restore` pattern (copied from LedgerApi/IdentityApi) silently breaks without a `.dockerignore` excluding `bin/`/`obj/` — a local `obj/project.assets.json` (from this machine's own `dotnet restore`) gets copied over the container's freshly-restored one in the second `COPY`, and `dotnet publish --no-restore` then fails looking for packages that were "restored" for the wrong OS/architecture. Both sibling repos already have this `.dockerignore`; this repo didn't (no code existed yet), so it's added here.

See the PR description for how this was tested, what was run and not run, and the full LEARNING_LOG entry.

---

## Appendix B. T-025 fix (demo KYC tier mismatch)

Live end-to-end verification against real deployed Azure infrastructure (18 Oct 2026, immediately after
T-024's fix) found that a real transfer against a demo-opened account always came back denied with
IdentityApi's `422 OperationNotEligibleForKycTier` — a correctly-formed response, not a bug in the
authorization call itself (T-024 already fixed that). The cause: `AccountOpeningService`'s `DemoKycTier`
constant was `"Tier1"`, and IdentityApi's own placeholder `KycTierPolicies` seed data only lists `"View"`
for `Tier1` on `"NG"` — `"Tier2"`/`"Tier3"` list `View`/`Credit`/`Debit`. No account opened through the
demo flow at Tier1 could ever pass the Debit check a transfer needs, regardless of anything else being
correct. Fixed by changing the constant to `"Tier2"` — still a placeholder/demo-stub value on both sides
(neither repo's seed data is a confirmed real KYC-tier policy), just the lowest tier that actually
supports the "account opening, plain transfer" scope T-022 was built for. `IdentityApi`'s seed data and
`MandateEligibility` were not touched; they were already correct. See the PR description for the test
added and the full LEARNING_LOG entry.
