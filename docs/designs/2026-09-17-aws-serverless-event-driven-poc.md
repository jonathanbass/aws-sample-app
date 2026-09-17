# AWS Serverless Event-Driven POC — Design

Date: 2026-09-17
Status: Agreed in brainstorm, not yet implemented
Source requirements: `plan.md`

## 1. Problem & Purpose

Prove event-driven architecture on AWS PaaS with a minimal vertical slice: the browser submits a piece of text, it travels through a service boundary as an event, and a second service pushes it back to the browser in real time.

Hard constraint discovered during design: **the AWS account has no free-tier runway left** (opened before 2025-07-15, more than 12 months old). Only the perpetual always-free allowances apply — Lambda 1M req/month, SQS 1M req/month, DynamoDB 25GB. Anything always-on (EC2, RDS, App Runner, Fargate) bills from day one. The design is therefore constrained to scale-to-zero services.

### Two things in `plan.md` that do not hold on AWS

1. **SignalR cannot run on Lambda.** SignalR needs a process that holds the connection. API Gateway's WebSocket API routes on `$connect` / `$disconnect` / `$default`, which is not compatible with SignalR's handshake and protocol. A Redis backplane does not help — there is no long-lived instance to subscribe.
2. **Wolverine's outbox cannot run on Lambda, and does not support DynamoDB.** Wolverine's own [serverless guidance](https://wolverinefx.net/guide/serverless.html) prescribes `DurabilityMode.Serverless`, which explicitly turns off the background inbox/outbox processes. A frozen Lambda has no background worker to run the recovery sweep. Separately, Wolverine's envelope storage is PostgreSQL, SQL Server, MySQL, SQLite, Oracle, RavenDB or CosmosDB — **there is no DynamoDB provider** — and a relational DB is the single largest recurring cost on this account. Together these two facts remove every reason Wolverine was in `plan.md`, which is why it was dropped (D11).

## 2. Decisions

| # | Decision | Rationale | Rejected alternatives |
|---|---|---|---|
| D1 | Real-time push via **API Gateway WebSocket API**, not SignalR. Frontend uses the native browser `WebSocket`. | Only fully-serverless option; scales to zero; the genuine AWS-PaaS answer. | SignalR hub on App Runner/Fargate/EC2 (~$5–25/mo, always-on, no free tier left). Separate SignalR relay service (3rd deployable, still always-on). AppSync Events / IoT Core (managed, but a different client SDK anyway — so no SignalR benefit retained). |
| D2 | Outbox implemented on **DynamoDB + its Lambda trigger (DynamoDB Streams)**, not Wolverine's outbox. | Perpetually free at POC volume, no VPC, no DB bill. The stream provides 24h retention, per-key ordering, at-least-once delivery and automatic batch retry — i.e. the recovery agent, for free. | Wolverine outbox on RDS Postgres + EventBridge sweeper Lambda (~$12/mo after trial, plus VPC networking or a public DB endpoint). EC2 box running service 1 + Postgres in Docker (~$7/mo, always-on, a VM to patch). Neon/Supabase free Postgres (second vendor, cross-cloud hop). Aurora DSQL — **ruled out**: no FKs, no sequences, no triggers, one DDL per transaction, and the EF provider is a community project not verified against a live cluster. |
| D3 | **SQS FIFO**, not standard. | Gives ordering for the UI's append-to-list, and content-based dedup over a 5-minute window — which unlocks D4 later with no infra change. Cost difference is irrelevant at POC volume. | Standard SQS (would need an idempotency table to adopt D4 later, plus a queue recreate). |
| D4 | **Stream-only path first.** Inline SQS send from the API Lambda is a deliberate later option, not built now. | YAGNI. The ~125ms stream hop is already snappy. FIFO-from-the-start means adopting inline send later is a code change, not an infra change. | Building inline-send + backstop up front (extra dedup and race handling for latency nobody has complained about yet). |
| D5 | **One monorepo**, three path-filtered GitHub Actions workflows. | Independent deployability is a property of the pipeline, not the repo count. The shared event contract stays a project reference instead of a published NuGet package. | Three repos (contract changes become publish-then-consume cycles). Two repos (backend + web). |
| D6 | **Terraform owns infrastructure; code deploys are separate.** `lifecycle { ignore_changes = [source_code_hash, s3_key] }` on each Lambda. | Genuine independent deploys from a single Terraform stack — no cross-stack remote-state lookups. Infra changes rarely; code changes constantly. | Per-service Terraform stacks (cross-stack state/SSM plumbing, too much for a POC). |
| D7 | **GitHub OIDC** role assumption; no long-lived AWS keys. | No secrets to rotate or leak. | Access keys stored in GitHub secrets. |
| D8 | **S3 native state locking** (`use_lockfile = true`), no DynamoDB lock table. | Terraform 1.11+ supports it; `dynamodb_table` is deprecated. One less resource to manage. | DynamoDB lock table (deprecated path). |
| D9 | **Amplify app declared in Terraform; GitHub repo connected once by hand in the console.** | The Terraform→Amplify→GitHub connection is known-broken for modern tokens (see §5). Connecting once in the console uses the modern GitHub App, after which Amplify builds on push with no Actions workflow at all. | Classic `ghp_` PAT in Terraform (long-lived credential, expires, contradicts D7). Building the SPA in Actions (loses PR previews, more to write). S3 + CloudFront (cheaper, perpetual free tier — but departs from `plan.md` and hand-rolls SPA routing and cache headers). |
| D10 | **Cold starts explicitly out of scope.** No SnapStart, no keep-warm, no ReadyToRun, no Native AOT. | User decision: it is a POC, judged on the second call. SnapStart for .NET is also not free (see §5). | — |
| D11 | **Wolverine dropped. Plain ASP.NET Core Minimal API + AWS SDK for .NET.** | With D1 and D2, every feature Wolverine was chosen for is gone: no outbox (no DynamoDB provider), no listener loop (the event source mapping delivers), and its retry policies are redundant against — and can conflict with — SQS redrive + ESM retry attempts. What remained was endpoint mapping and mediator-style dispatch, which Minimal APIs and DI already do. Dropping it also removes the largest unvalidated risk in the plan (Wolverine's runtime codegen vs the ASP.NET-on-Lambda shim) and makes the outbox explicit, readable application code — which suits a POC whose purpose is to *demonstrate* the pattern. | Keep Wolverine as a thin dispatcher (framework weight and cold-start codegen for no capability). Move service 1 to a persistent host + Postgres so the real Wolverine outbox runs (~$7/mo always-on — **rejected on cost**, and the only option that would genuinely prove Wolverine). |
| D12 | **Region `eu-west-1` (Dublin). Broadcast to all connections. No auth.** | User decisions. Broadcast keeps the Consumer trivial; auth is out of scope for a POC. | Per-connection filtering; Cognito or API-key auth on both APIs. |
| D13 | **Lambda-first: no ASP.NET Core hosting anywhere.** All four Lambdas are plain `FunctionHandler` entry points over AWS event types (`APIGatewayHttpApiV2ProxyRequest`, `DynamoDBEvent`, `SQSEvent`), each wrapping a small constructor-injected service. | Refines D11. With one HTTP endpoint, `Amazon.Lambda.AspNetCoreServer.Hosting` bought only a routing table and a cold-start shim. Dropping it makes all four functions structurally identical, removes a dependency, and removes the last framework between the code and the runtime. User explicitly chose Lambda-first and accepted a testing change to preserve it. | ASP.NET Minimal API on Lambda (an odd-one-out shape for one endpoint; shim on every cold start). `Amazon.Lambda.Annotations` source generators (more magic, not less). |
| D14 | **Testing: unit tests over small injectable services + handler contract tests. No BDD Context pattern.** | User decision. Handler contract tests construct a real AWS event, invoke the real `FunctionHandler`, and assert the real response type — testing the production entry point rather than a hosting abstraction. `WebApplicationFactory` is not available and not wanted (D13). | `WebApplicationFactory` API tests (require ASP.NET hosting, contradicting D13). BDD Context split (ceremony disproportionate to the logic). LocalStack-based integration tests (yak-shave before any feature works; DynamoDB Local is enough). |

## 3. Architecture

```
Browser (React SPA on Amplify Hosting)
   │  (1) POST /messages {text}                   (6) push {text}
   ▼                                                    ▲
API Gateway HTTP API ──► Lambda: Ingest.Api      API Gateway WebSocket API
                            │                          ▲
                            │ (2) TransactWriteItems   │ (5) PostToConnection
                            ▼                          │
                   DynamoDB: messages           Lambda: Notifier.Consumer
                   ├─ domain item                      ▲
                   └─ outbox item                      │ (4) SQS FIFO trigger
                            │                          │
                            │ (3) DynamoDB trigger     │
                            ▼                          │
                   Lambda: Ingest.OutboxRelay ──► SQS FIFO

                   DynamoDB: connections ◄── Lambda: Notifier.Connections
                   (read by Consumer)          ($connect / $disconnect, TTL)
```

### Service 1 — Ingest (deploys as one unit, two Lambdas)

- **`Ingest.Api`** — API Gateway HTTP API → plain Lambda handler over `APIGatewayHttpApiV2ProxyRequest` (D13, no ASP.NET). Writes the domain item **and** an outbox item in a single `TransactWriteItems`. That transaction is the unit of work, written explicitly rather than delegated to a framework.
- **`Ingest.OutboxRelay`** — invoked by the DynamoDB trigger on the `messages` table stream. Maps the outbox item to the `TextSubmitted` event and sends it to SQS FIFO via `AmazonSQSClient.SendMessageAsync`, with `MessageGroupId` and `MessageDeduplicationId` derived from the outbox item id.

### Service 2 — Notifier (deploys as one unit, two Lambdas)

- **`Notifier.Consumer`** — SQS FIFO event source mapping. Reads live connection ids from the `connections` table and calls API Gateway Management API `PostToConnection` for each.
- **`Notifier.Connections`** — handles `$connect` / `$disconnect`, writing and removing connection ids with a TTL so stale rows self-clean.

### Latency budget (warm)

| Hop | Warm |
|---|---|
| Browser → API GW → `Ingest.Api` | 20–50ms |
| `TransactWriteItems` | 10–20ms |
| DynamoDB trigger pickup | 0–250ms (~125ms avg) |
| `OutboxRelay` → SQS | 20–30ms |
| SQS pickup → `Consumer` | 50–100ms |
| `PostToConnection` → browser | 20–50ms |
| **Total** | **~250–500ms** |

**Terraform detail that follows from this:** do NOT set `maximum_batching_window_in_seconds` on either event source mapping. It defaults to 0 ("invoke as soon as records are available"). Setting it is how people accidentally add whole seconds.

## 4. Repo & Deployment Layout

```
aws-sample-app/
├─ src/
│  ├─ Contracts/                      TextSubmitted event (project reference, not NuGet)
│  ├─ Ingest/
│  │  ├─ Ingest.Api/
│  │  └─ Ingest.OutboxRelay/
│  └─ Notifier/
│     ├─ Notifier.Consumer/
│     └─ Notifier.Connections/
├─ web/                               React SPA (Vite + shadcn/ui + Tailwind)
├─ amplify.yml                        monorepo build spec, appRoot: web
├─ infra/
│  ├─ bootstrap/                      applied ONCE from a laptop with admin creds
│  └─ main/                           everything else
└─ .github/workflows/
   ├─ infra.yml                       paths: infra/main/**
   ├─ ingest.yml                      paths: src/Ingest/**, src/Contracts/**
   └─ notifier.yml                    paths: src/Notifier/**, src/Contracts/**
```

No `web.yml` — Amplify builds the SPA itself on push (D9).

**`infra/bootstrap` creates exactly three things**, resolving the chicken-and-egg where CI cannot run Terraform until its own state bucket and role exist:

1. Versioned, encrypted, public-access-blocked S3 bucket for Terraform state.
2. GitHub OIDC provider (`token.actions.githubusercontent.com`).
3. CI IAM role, trust scoped to `repo:<owner>/aws-sample-app:ref:refs/heads/main`.

## 5. Key Facts Verified During Recon

Cite these rather than re-deriving them.

- **DynamoDB "Lambda trigger" IS DynamoDB Streams.** There is no other DynamoDB→Lambda trigger. The console's "Create trigger" enables the stream and creates an event source mapping. In Terraform: `stream_enabled = true` + `stream_view_type = "NEW_IMAGE"` on the table, plus an `aws_lambda_event_source_mapping` on `stream_arn`. From the handler's point of view it is push — it receives a `DynamoDBEvent` and returns. Source: <https://docs.aws.amazon.com/lambda/latest/dg/with-ddb.html>
- **Lambda polls each stream shard at a base rate of 4/second**, and by default invokes as soon as records are available (batching window defaults to 0). The polling happens in AWS's managed poller fleet, not in the account. Same source.
- **Stream reads via a Lambda trigger are not billed.** "You are not charged for GetRecords API calls invoked through DynamoDB triggers on AWS Lambda" — unless running on Lambda Managed Instances. Source: <https://aws.amazon.com/dynamodb/pricing/on-demand/>
- **SnapStart is free only for Java.** For .NET you pay for snapshot cache (per GB-hour, 3-hour minimum, charged continuously **per published version** while active) plus a per-restore charge. Every CI deploy publishes a version that accrues cost until pruned. Several blog posts claim otherwise and are wrong. Source: <https://docs.aws.amazon.com/lambda/latest/dg/snapstart.html>
- **Wolverine has no DynamoDB support.** Envelope storage is PostgreSQL, SQL Server, MySQL, SQLite, Oracle, RavenDB and CosmosDB, plus Marten/Polecat/EF Core as the application persistence layer. The Amazon S3 and Azure Blob Storage integrations listed nearby in the docs are large-message claim-checks, **not** inbox/outbox persistence. Source: <https://wolverinefx.net/guide/durability/> — this is the fact that settled D11.
- **Wolverine does support SQS FIFO** (`Envelope.GroupId` → `MessageGroupId`, `DeduplicationId` via `DeliveryOptions`). Recorded only because it was verified before D11 dropped Wolverine; it no longer affects the design. The AWS SDK sets both fields directly on `SendMessageRequest`.
- **Terraform 1.11+ does S3 native state locking** via `use_lockfile = true`; `dynamodb_table` is deprecated. Source: <https://developer.hashicorp.com/terraform/language/backend/s3>
- **Amplify + Terraform + GitHub is janky.** `oauth_token` still wires the deprecated OAuth path rather than the GitHub App (terraform-provider-aws#25122); `access_token` works only with classic `ghp_` PATs, not fine-grained ones (#31643); GitHub App tokens exceed a 255-char validation cap (#49565). Hence D9.
- **Amplify monorepo needs both** an `applications:` array with `appRoot: web` in `amplify.yml` AND the `AMPLIFY_MONOREPO_APP_ROOT` env var. AWS docs state that for apps created via CloudFormation (and therefore Terraform) this variable **must be set manually** — Terraform sets it in `environment_variables`. Source: <https://docs.aws.amazon.com/amplify/latest/userguide/monorepo-configuration.html>
- **AWS free tier changed 2025-07-15.** Accounts opened before that date get the classic 12-month tier; accounts opened after get $100–200 in credits and only 6 months of free EC2/RDS. This account is pre-cutoff and over a year old, so neither applies.

## 6. Which Global Rules Apply

Most of the user's global rules are Azure/Bennetts-specific and **do not apply here**:

| Rule | Applies? | Note |
|---|---|---|
| `dotnet.md`, `dotnet-domain.md`, `dotnet-services.md`, `dotnet-response-dtos.md` | **Yes** | General C# conventions carry over unchanged. |
| `dotnet-handlers.md` | **Partly** | Naming and thin-handler guidance applies to the Minimal API endpoint handlers. Wolverine-specific mechanics do not (D11). |
| `wolverine.md` | **No** | Wolverine was dropped (D11). Its cross-framework JSON casing guidance is still a useful reference when choosing serialization options — see §8. |
| `dotnet-testing.md`, `dotnet-nsubstitute-shared-mocks.md`, `dotnet-tracing.md` | **Yes** | |
| `typescript.md`, `react-nextjs*.md` | **Partly** | This is a Vite SPA, not Next.js — no server actions, no App Router. TypeScript and testing conventions apply. |
| `dotnet-bicep.md`, `dotnet-integration-events*.md` | **No** | Azure/Bicep/ASB-specific. Terraform + SQS here. |
| `dotnet-ef-core.md`, `dotnet-flyway.md` | **No** | No EF Core, no relational DB, no migrations. |
| `dotnet-contract-tests.md` | **No** | Depends on a central Bennetts contract repo + Specmatic that do not exist for this project. |
| `dotnet-wolverine-sagas.md`, `dotnet-saga-structure.md` | **No** | No sagas in scope. |
| `dotnet-bennetts-feature-management.md` | **No** | LaunchDarkly wrapper; also `plan.md` forbids Bennetts naming. |

`plan.md` forbids Bennetts naming anywhere in the software.

## 7. Database Changes

**No EF Core migrations and no Flyway scripts.** There is no relational database. Both DynamoDB tables are Terraform-managed infrastructure:

| Table | Keys | Notes |
|---|---|---|
| `messages` | PK on message id; outbox item distinguished by sort key | Stream enabled, `NEW_IMAGE`. Domain item + outbox item written in one `TransactWriteItems`. |
| `connections` | PK on connection id | TTL attribute so stale connections self-clean. |

## 8. Testing Impact

Greenfield — everything is new. Per the user's global instructions, TDD via the `superpowers:test-driven-development` skill is mandatory before writing code.

New tests to create:

- **Unit** — outbox item ↔ `TextSubmitted` mapping; the transaction-item builder; connection-id TTL calculation.
- **Serialization round-trip** — `TextSubmitted` across the SQS boundary. We now own both ends, so pick one `JsonSerializerOptions` (`JsonSerializerDefaults.Web`, i.e. camelCase) and share it between relay and consumer. The lesson from `wolverine.md` still holds: a casing mismatch does **not** throw, it silently binds every field to its default, so assert every field explicitly in a round-trip test rather than trusting case-insensitive matching.
- **Handler contract tests** (D14) — construct a real AWS event (`APIGatewayHttpApiV2ProxyRequest`, `DynamoDBEvent`, `SQSEvent`), invoke the real `FunctionHandler`, assert the real response type and status. DynamoDB Local behind them for persistence. This is the production entry point, so it is the right contract to pin.
- **Structural rule that makes the above cheap:** every Lambda's logic lives in a small constructor-injected service with no AWS types in its public signature; the `FunctionHandler` is three lines of glue and is deliberately not unit-tested. This is what "small, SOLID, very unit testable" buys.
- **Frontend** — Vitest + Testing Library for the submit form and the WebSocket-driven list, with the socket faked.
- **End-to-end smoke** — `wscat` connected to the WebSocket URL plus `curl` to the HTTP API, asserting the text comes back. This is the real proof and should be the acceptance test for each backend phase.

No existing tests to amend — the repo is empty.

## 9. Assumptions

### Shape-determining, VERIFIED this session

All of §5. In particular: SignalR-on-Lambda is impossible (D1), Wolverine has no DynamoDB provider and its outbox is relational-only (D2, D11), and the DynamoDB trigger is the stream (D2).

**The previous largest risk is now retired.** An earlier draft flagged "does `WolverineFx.Http`'s runtime codegen survive the ASP.NET-on-Lambda shim?" as the biggest unvalidated assumption, with Phase 2 existing mainly to spike it. D11 removes Wolverine entirely, so that question no longer exists. `Amazon.Lambda.AspNetCoreServer.Hosting` running a plain Minimal API is a well-trodden, AWS-supported path.

### Validate early, low blast radius

- Amplify app created by Terraform with the repo connected once by hand behaves correctly, and `aws_amplify_branch` can be managed after the manual connect (may need `ignore_changes` on `repository` and token fields).
- `PostToConnection` on a stale connection id returns `GoneException` — the Consumer must catch it and delete the row rather than fail the whole batch.
- DynamoDB trigger delivery is **at-least-once**; `OutboxRelay` must be idempotent. FIFO dedup on the outbox item id covers this, but only within the 5-minute dedup window.
- `starting_position = "TRIM_HORIZON"` is required — AWS warns that `LATEST` can miss events during event source mapping creation and updates, which is exactly when a fresh `terraform apply` happens.

## 10. Resolved & Open Items

Resolved (see D12):

- **Region: `eu-west-1` (Dublin).**
- **Broadcast:** every connected browser receives every message. No per-connection filtering.
- **Auth: none.** Both the HTTP API and the WebSocket API are open. Accepted for a POC — but this means the deployed demo is effectively a public chat room, so treat the URL accordingly and do not leave it running indefinitely.

Still open:

- CloudWatch log retention must be set explicitly on every log group (the default is never-expire, which slowly costs money). 7 days.
- `plan.md` still names Wolverine and SignalR. It is the original requirements doc, so it has deliberately been left untouched — this design supersedes it. Worth a short "superseded by docs/designs/…" note at its top if it is going to be read by anyone else.
