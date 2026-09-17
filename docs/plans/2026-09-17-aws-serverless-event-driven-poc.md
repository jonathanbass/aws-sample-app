# AWS Serverless Event-Driven POC — Implementation Plan

**Goal:** Browser submits text over REST; it crosses a service boundary as an event via an outbox and a queue; a second service pushes it back over a WebSocket and the browser appends it to a list.

**Architecture:** Lambda-first. Four Lambdas, all the same shape — a `FunctionHandler` over an AWS event type wrapping a small constructor-injected service. No ASP.NET hosting, no messaging framework. The outbox is a DynamoDB `TransactWriteItems` drained by the table's Lambda trigger into an SQS FIFO queue.

**Tech Stack:** .NET 10 (`dotnet10` Lambda managed runtime, ARM64), AWS SDK for .NET, `Amazon.Lambda.*Events`, Terraform >= 1.11, GitHub Actions + OIDC, React + Vite + TypeScript + Tailwind + shadcn/ui, AWS Amplify Hosting. Region `eu-west-1`.

**Testing Strategy:** Unit tests over small injectable services (xUnit), plus **handler contract tests** — construct a real AWS event, invoke the real `FunctionHandler`, assert the real response type, with DynamoDB Local behind it. Lambda adapters are three lines of glue and are deliberately not unit-tested. Vitest + Testing Library for the SPA. No BDD Context pattern. Every phase also has a manual smoke test against deployed infrastructure — for serverless that is what actually catches IAM, trigger and wiring failures.

**Design + context:** `docs/designs/2026-09-17-aws-serverless-event-driven-poc.md` — holds the *why*, the rejected alternatives (D1–D14), and the verified recon facts. Read it before starting.

**Execution:** Sequential, single session. No subagents, no parallel tracks, no rush.

---

## Conventions that apply throughout

- **Every Lambda has the same shape.** `FunctionHandler(TEvent, ILambdaContext)` → three lines of glue → a service class with no AWS types in its public signature. This is what makes the logic unit-testable without LocalStack.
- **Terraform is never applied locally** after Phase 0. It is applied by `deploy.yml` on push to `main`, always before any function code is deployed.
- **Terraform creates each Lambda with a placeholder zip** and `lifecycle { ignore_changes = [source_code_hash, filename] }`, so infrastructure and triggers exist before real code ships.
- **Every log group gets `retention_in_days = 7`.** The default is never-expire, which is how a free POC quietly starts costing money.
- **No Bennetts naming anywhere** (`plan.md` constraint).
- Applicable global rules: `dotnet.md`, `dotnet-domain.md`, `dotnet-services.md`, `dotnet-response-dtos.md`, `dotnet-testing.md`, `dotnet-tracing.md`, `typescript.md`. **Not** applicable: all Bicep/ASB/EF/Flyway/Wolverine/contract-test rules — see design doc §6.

---

## Phase 0: Bootstrap — MANUAL, done by the user, once

**Status:** Files written. Awaiting the user's `terraform apply`.

Not a development task. See `infra/bootstrap/README.md` for the full runbook.

- [ ] **Task 0.1: Install Terraform >= 1.11 and the AWS CLI**
- [ ] **Task 0.2: Apply `infra/bootstrap`** — creates the state bucket, GitHub OIDC provider, and CI role
- [ ] **Task 0.3: Set GitHub repository variables** `AWS_ROLE_ARN`, `AWS_REGION`, `TF_STATE_BUCKET`

**→ Blocks every phase below. Nothing else can be applied until the CI role exists.**

---

## Phase 1: Foundation — CI can deploy

**Commit scope:** Repo skeleton plus a working deploy pipeline. No application behaviour yet.
**Verification:** Push to `main` → the `terraform` job assumes the OIDC role and applies cleanly with no AWS credentials stored anywhere. **This proves the single riskiest piece of plumbing before any C# exists.**

### Tasks

- [ ] **Task 1.1: Terraform skeleton for the main stack**

  **Files:** Create `infra/main/main.tf`, `infra/main/variables.tf`, `infra/main/outputs.tf`
  **Acceptance criteria:**
  - `terraform` block pins `required_version >= 1.11` and the AWS provider recent enough to accept the `dotnet10` runtime string (see design doc §5 — provider issue #45864)
  - S3 backend configured with `use_lockfile = true` and **no** `dynamodb_table`
  - Provider `default_tags` sets `Project` and `ManagedBy`
  - `variables.tf` declares `aws_region` (default `eu-west-1`) and `project_name`
  **Constraints:** Backend `bucket` cannot be a variable — Terraform backends do not accept interpolation. Pass it via `-backend-config` from the workflow using `TF_STATE_BUCKET`.

- [ ] **Task 1.2: Solution and build configuration skeleton**

  **Files:** Create `src/AwsSampleApp.sln`, `src/Directory.Build.props`, `src/Directory.Packages.props`
  **Acceptance criteria:**
  - `TargetFramework` = `net10.0`, `Nullable` enabled, `TreatWarningsAsErrors` true, `ImplicitUsings` enabled
  - Central package management enabled (`ManagePackageVersionsCentrally`)
  - `dotnet build src/AwsSampleApp.sln` succeeds on an empty solution

- [ ] **Task 1.3: Deploy workflow**

  **Files:** Create `.github/workflows/deploy.yml`
  **Acceptance criteria:**
  - Triggers on push to `main`
  - Top-level `permissions: { id-token: write, contents: read }` — without `id-token: write` OIDC silently fails
  - `terraform` job uses `aws-actions/configure-aws-credentials` with `role-to-assume: ${{ vars.AWS_ROLE_ARN }}`, runs `init` with `-backend-config="bucket=${{ vars.TF_STATE_BUCKET }}"`, then `apply -auto-approve`
  - Placeholder service jobs declared with `needs: terraform` (bodies filled in later phases)
  **Constraints:** Every future code-deploy job MUST carry `needs: terraform`. That is what guarantees infrastructure-before-application ordering.

- [ ] **Task 1.4: Placeholder Lambda artifact**

  **Files:** Create `infra/main/placeholder/` with a trivial zip source, referenced via `data "archive_file"`
  **Acceptance criteria:** Every `aws_lambda_function` in later phases can point at this until its real code deploys

**→ Phase 1 complete. Push and confirm the workflow goes green before starting Phase 2.**

---

## Phase 2: Submit text and persist it

**Commit scope:** `POST /messages` works against real AWS; domain item and outbox item written atomically.
**Verification:** `curl -X POST <api-url>/messages -H 'content-type: application/json' -d '{"text":"hello"}'` → `202`, and both items visible via `aws dynamodb scan --table-name <messages>`.

### Tasks

- [ ] **Task 2.1: Tests for the message item builder**

  **Files:** Create `src/Ingest/Ingest.Api.Tests/MessageItemsTests.cs`
  **Acceptance criteria:**
  - Builds exactly two items sharing one message id
  - Domain item and outbox item are distinguishable by sort key
  - Outbox item carries the submitted text and an ISO-8601 UTC timestamp
  - Timestamp comes from an injected `TimeProvider`, so it is assertable — not `DateTime.UtcNow`

- [ ] **Task 2.2: Implement the message item builder (make tests pass)**

  **Files:** Create `src/Ingest/Ingest.Api/MessageItems.cs`
  **Constraints:** Pure, no AWS client calls, no I/O. This is the most unit-testable thing in the project — keep it that way.

- [ ] **Task 2.3: Tests for `SubmitTextService`**

  **Files:** Create `src/Ingest/Ingest.Api.Tests/SubmitTextServiceTests.cs`
  **Acceptance criteria:**
  - Calls `TransactWriteItems` exactly once, containing both items
  - Rejects null, empty and whitespace-only text
  - Rejects text over the maximum length (pick one, document it, test the boundary)
  - Returns the created message id on success
  **Constraints:** `IAmazonDynamoDB` substituted per `dotnet-nsubstitute-shared-mocks.md`

- [ ] **Task 2.4: Implement `SubmitTextService` (make tests pass)**

  **Files:** Create `src/Ingest/Ingest.Api/SubmitTextService.cs`
  **Constraints:** Constructor-injected `IAmazonDynamoDB` and `TimeProvider`. No AWS types in the public method signature — take a string, return a result record.

- [ ] **Task 2.5: Handler contract tests for the Lambda entry point**

  **Files:** Create `src/Ingest/Ingest.Api.Tests/FunctionTests.cs`
  **Acceptance criteria:**
  - Valid `APIGatewayHttpApiV2ProxyRequest` → `202` with the message id in the body
  - Empty text → `400`
  - Malformed JSON body → `400`, not an unhandled exception
  - Response `Content-Type` is `application/json`
  **Constraints:** Run against DynamoDB Local. This replaces `WebApplicationFactory` tests (design doc D13/D14).

- [ ] **Task 2.6: Implement the Lambda entry point (make tests pass)**

  **Files:** Create `src/Ingest/Ingest.Api/Function.cs`, `src/Ingest/Ingest.Api/Ingest.Api.csproj`
  **Acceptance criteria:** Deserializes the body, calls the service, maps success and validation failure to status codes
  **Constraints:** Three lines of glue plus mapping. All logic stays in the service.

- [ ] **Task 2.7: Terraform — messages table, HTTP API, Ingest.Api Lambda**

  **Files:** Create `infra/main/ingest.tf`
  **Exact values that matter:**
  - `aws_dynamodb_table` with `billing_mode = "PAY_PER_REQUEST"`, `stream_enabled = true`, `stream_view_type = "NEW_IMAGE"` (the stream is unused until Phase 3, but enabling it later forces a resource change)
  - `aws_lambda_function` with `runtime = "dotnet10"`, `architectures = ["arm64"]`, placeholder zip, `lifecycle { ignore_changes = [source_code_hash, filename] }`
  - `aws_cloudwatch_log_group` with `retention_in_days = 7`
  - IAM: `dynamodb:PutItem` and `dynamodb:TransactWriteItems` on the table only
  - `aws_apigatewayv2_api` with `protocol_type = "HTTP"`, CORS allowing the Amplify origin (widen to `*` initially, narrow in Phase 6)
  **Acceptance criteria:** `outputs.tf` exposes the HTTP API invoke URL

- [ ] **Task 2.8: Wire the `deploy-ingest` job**

  **Files:** Modify `.github/workflows/deploy.yml`
  **Acceptance criteria:**
  - `needs: terraform`
  - Path-filtered on `src/Ingest/**` and `src/Contracts/**`
  - Publishes with `dotnet publish -c Release -r linux-arm64`, zips, then `aws lambda update-function-code`

**→ Phase 2 complete. Smoke test with `curl`, confirm both items land, then commit and push.**

---

## Phase 3: The outbox drains to the queue

**Commit scope:** The outbox item becomes a `TextSubmitted` event on an SQS FIFO queue.
**Verification:** `curl` as in Phase 2, then `aws sqs receive-message` → the event is there within ~1s.

### Tasks

- [ ] **Task 3.1: Tests for `TextSubmitted` serialization round-trip**

  **Files:** Create `src/Contracts.Tests/TextSubmittedTests.cs`
  **Acceptance criteria:**
  - Round-trips through the shared `JsonSerializerOptions`
  - **Every field asserted individually.** A casing mismatch does not throw — it silently binds every field to its default. Asserting "it deserialized non-null" would pass against a completely broken contract.

- [ ] **Task 3.2: Implement `TextSubmitted` and the shared serializer options**

  **Files:** Create `src/Contracts/TextSubmitted.cs`, `src/Contracts/EventJson.cs`
  **Constraints:** One shared `JsonSerializerOptions` (`JsonSerializerDefaults.Web`) referenced by both producer and consumer. Both ends are ours, so no `[JsonPropertyName]` attributes are needed — but the round-trip test is what keeps that true.

- [ ] **Task 3.3: Tests for `OutboxRelayService`**

  **Files:** Create `src/Ingest/Ingest.OutboxRelay.Tests/OutboxRelayServiceTests.cs`
  **Acceptance criteria:**
  - Maps an outbox stream record to `TextSubmitted` and sends it
  - Sets `MessageGroupId` and `MessageDeduplicationId` from the outbox item id
  - **Ignores stream records that are not outbox items** (the domain item hits the same stream)
  - Ignores `REMOVE` events
  - A second delivery of the same record produces the same dedup id (idempotency)

- [ ] **Task 3.4: Implement `OutboxRelayService` (make tests pass)**

  **Files:** Create `src/Ingest/Ingest.OutboxRelay/OutboxRelayService.cs`
  **Constraints:** Constructor-injected `IAmazonSQS`. No `DynamoDBEvent` in the public signature — map to a domain type at the adapter boundary.

- [ ] **Task 3.5: Implement the relay Lambda adapter**

  **Files:** Create `src/Ingest/Ingest.OutboxRelay/Function.cs`, `.csproj`
  **Acceptance criteria:** Returns `StreamsEventResponse` with `BatchItemFailures` populated for partial batch failure — without this, one bad record reprocesses the whole batch forever

- [ ] **Task 3.6: Terraform — SQS FIFO queue, DLQ, relay Lambda, stream trigger**

  **Files:** Create `infra/main/messaging.tf`
  **Exact values that matter:**
  - `aws_sqs_queue` with `fifo_queue = true`, `content_based_deduplication = false` (we supply explicit dedup ids), name ending `.fifo`
  - DLQ with `redrive_policy` `maxReceiveCount = 3`
  - `aws_lambda_event_source_mapping` with `starting_position = "TRIM_HORIZON"` — **`LATEST` can miss events during mapping creation**, which is exactly when a fresh apply happens
  - **Do NOT set `maximum_batching_window_in_seconds`** — it defaults to 0; setting it adds whole seconds of latency
  - `function_response_types = ["ReportBatchItemFailures"]` to match Task 3.5
  - IAM: `dynamodb:GetRecords/GetShardIterator/DescribeStream/ListStreams` on the stream ARN, `sqs:SendMessage` on the queue
  - Log group, 7-day retention

**→ Phase 3 complete. The outbox pattern is now provable end to end.**

---

## Phase 4: WebSocket connection registry

**Commit scope:** Browsers can hold a WebSocket connection and the system knows who is connected.
**Verification:** `wscat -c <ws-url>` → row appears in the connections table; close it → row disappears.

### Tasks

- [ ] **Task 4.1: Tests for `ConnectionRegistry`**

  **Files:** Create `src/Notifier/Notifier.Connections.Tests/ConnectionRegistryTests.cs`
  **Acceptance criteria:**
  - Add writes the connection id with a TTL of now + 2 hours, using an injected `TimeProvider`
  - TTL is written as a Unix epoch **seconds** integer — DynamoDB TTL silently ignores milliseconds, and the row then never expires
  - Remove deletes by connection id
  - Removing an already-absent connection does not throw

- [ ] **Task 4.2: Implement `ConnectionRegistry` (make tests pass)**

  **Files:** Create `src/Notifier/Notifier.Connections/ConnectionRegistry.cs`

- [ ] **Task 4.3: Handler contract tests for connect/disconnect**

  **Files:** Create `src/Notifier/Notifier.Connections.Tests/FunctionTests.cs`
  **Acceptance criteria:** `$connect` returns 200 and stores the id; `$disconnect` returns 200 and removes it; both read the connection id from `requestContext.connectionId`

- [ ] **Task 4.4: Implement the connections Lambda adapter (make tests pass)**

  **Files:** Create `src/Notifier/Notifier.Connections/Function.cs`, `.csproj`
  **Constraints:** One function handles both routes, branching on `requestContext.routeKey`

- [ ] **Task 4.5: Terraform — connections table, WebSocket API, connections Lambda**

  **Files:** Create `infra/main/websocket.tf`
  **Exact values that matter:**
  - `aws_dynamodb_table` with `ttl { attribute_name = "expiresAt", enabled = true }`
  - `aws_apigatewayv2_api` with `protocol_type = "WEBSOCKET"`, `route_selection_expression = "$request.body.action"`
  - Routes for `$connect` and `$disconnect` with Lambda integrations, plus `aws_lambda_permission` for each
  - `aws_apigatewayv2_stage` with `auto_deploy = true` — without a deployed stage the URL 403s with no useful error
  - Log group, 7-day retention
  **Acceptance criteria:** `outputs.tf` exposes the `wss://` URL and the API's management endpoint (Phase 5 needs it)

- [ ] **Task 4.6: Wire the `deploy-notifier` job**

  **Files:** Modify `.github/workflows/deploy.yml`
  **Constraints:** `needs: terraform`, path-filtered on `src/Notifier/**` and `src/Contracts/**`

**→ Phase 4 complete. Smoke test with `wscat`.**

---

## Phase 5: Events push to connected browsers

**Commit scope:** The full backend round trip works.
**Verification:** `wscat` connected in one terminal, `curl` posting in another → the text arrives in `wscat` within ~1s. **This is the headline acceptance test for the whole POC.**

### Tasks

- [ ] **Task 5.1: Tests for `BroadcastService`**

  **Files:** Create `src/Notifier/Notifier.Consumer.Tests/BroadcastServiceTests.cs`
  **Acceptance criteria:**
  - Fans out to every connection in the table
  - On `GoneException` for one connection, deletes that row and **continues** to the others
  - A single stale connection does not fail the batch
  - Zero connections is a successful no-op, not an error

- [ ] **Task 5.2: Implement `BroadcastService` (make tests pass)**

  **Files:** Create `src/Notifier/Notifier.Consumer/BroadcastService.cs`
  **Constraints:** Constructor-injected `IAmazonApiGatewayManagementApi` and `IAmazonDynamoDB`

- [ ] **Task 5.3: Handler contract tests for the consumer**

  **Files:** Create `src/Notifier/Notifier.Consumer.Tests/FunctionTests.cs`
  **Acceptance criteria:** An `SQSEvent` carrying a serialized `TextSubmitted` broadcasts its text; a malformed record is reported as a batch item failure rather than throwing

- [ ] **Task 5.4: Implement the consumer Lambda adapter (make tests pass)**

  **Files:** Create `src/Notifier/Notifier.Consumer/Function.cs`, `.csproj`
  **Acceptance criteria:** Returns `SQSBatchResponse` with `BatchItemFailures`

- [ ] **Task 5.5: Terraform — consumer Lambda and SQS trigger**

  **Files:** Modify `infra/main/websocket.tf` or create `infra/main/consumer.tf`
  **Exact values that matter:**
  - `aws_lambda_event_source_mapping` on the FIFO queue, `function_response_types = ["ReportBatchItemFailures"]`, **no batching window**
  - IAM: `execute-api:ManageConnections` on the WebSocket API's `*/*/@connections/*`, plus `dynamodb:Scan` and `dynamodb:DeleteItem` on the connections table
  - The `AmazonApiGatewayManagementApi` client must be configured with the **management** endpoint (`https://{api-id}.execute-api.{region}.amazonaws.com/{stage}`), not the `wss://` URL — passed in as an environment variable
  - Log group, 7-day retention

- [ ] **Task 5.6: End-to-end smoke test script**

  **Files:** Create `scripts/smoke-test.md` (or `.ps1`)
  **Acceptance criteria:** Documents the exact `wscat` + `curl` sequence and the expected output, so the round trip is reproducible in one command after any future change

**→ Phase 5 complete. The architecture is proven. Everything after this is presentation.**

---

## Phase 6: The SPA

**Commit scope:** A working browser demo.
**Verification:** Open the Amplify URL, type text, click submit, see it appear in the list.

### Tasks

- [ ] **Task 6.1: Scaffold the SPA**

  **Files:** Create `web/` — Vite + React + TypeScript, Tailwind, shadcn/ui
  **Acceptance criteria:**
  - Theme per `plan.md`: mostly white, neutral buttons in a medium grey-blue with a hint of purple
  - Colors defined as CSS custom properties on `:root`, not hardcoded in components
  - API and WebSocket URLs come from `VITE_`-prefixed env vars, never hardcoded

- [ ] **Task 6.2: Tests for the `useMessageSocket` hook**

  **Files:** Create `web/src/hooks/useMessageSocket.test.ts`
  **Acceptance criteria:** Appends received messages in order; reconnects after an unexpected close; cleans up the socket on unmount (no listener leak)
  **Constraints:** Fake the `WebSocket` global. No network in tests.

- [ ] **Task 6.3: Implement `useMessageSocket` (make tests pass)**

  **Files:** Create `web/src/hooks/useMessageSocket.ts`

- [ ] **Task 6.4: Tests for the submit form**

  **Files:** Create `web/src/components/SubmitForm.test.tsx`
  **Acceptance criteria:** Posts the typed text; disables submit while in flight and for empty input; surfaces an error state on a failed request
  **Constraints:** Test observable behaviour, never markup or styling (`react-nextjs-testing.md`)

- [ ] **Task 6.5: Implement the form and message list (make tests pass)**

  **Files:** Create `web/src/components/SubmitForm.tsx`, `web/src/components/MessageList.tsx`, wire into `App.tsx`

- [ ] **Task 6.6: Amplify monorepo build spec**

  **Files:** Create `amplify.yml` at the repo root
  **Exact content shape:**
  ```yaml
  version: 1
  applications:
    - appRoot: web
      frontend:
        phases:
          preBuild:
            commands:
              - npm ci
          build:
            commands:
              - npm run build
        artifacts:
          baseDirectory: dist
          files:
            - '**/*'
        cache:
          paths:
            - node_modules/**/*
  ```
  **Constraints:** `appRoot` MUST equal the `AMPLIFY_MONOREPO_APP_ROOT` env var set in Task 6.7.

- [ ] **Task 6.7: Terraform — Amplify app**

  **Files:** Create `infra/main/amplify.tf`
  **Exact values that matter:**
  - `environment_variables` includes `AMPLIFY_MONOREPO_APP_ROOT = "web"` — AWS docs state this must be set manually for apps created via CloudFormation/Terraform; it is not inferred
  - Also set `VITE_API_URL` and `VITE_WS_URL` from the Phase 2 and Phase 4 outputs
  - `platform = "WEB"`
  - **No `repository`, `oauth_token` or `access_token`**, plus `lifecycle { ignore_changes = [repository, oauth_token, access_token] }` — the provider wires the deprecated OAuth path and rejects modern GitHub tokens (design doc §5)
  - A custom rewrite rule sending `/<*>` to `/index.html` with status `200` — without it, SPA deep links 404

- [ ] **Task 6.8: Connect the repository in the Amplify console — MANUAL, once**

  **Acceptance criteria:** Connect via the GitHub App, select `main`, confirm a build triggers and the deployed URL serves the SPA
  **Constraints:** Do this by hand. Do not add a token to Terraform.

- [ ] **Task 6.9: Narrow CORS**

  **Files:** Modify `infra/main/ingest.tf`
  **Acceptance criteria:** HTTP API CORS `allow_origins` is the Amplify domain rather than `*`

**→ Phase 6 complete. The POC is done.**

---

## Post-completion

- [ ] Add a `> Superseded by docs/designs/2026-09-17-...` note to the top of `plan.md`
- [ ] Confirm every log group has 7-day retention (`aws logs describe-log-groups`)
- [ ] **Tear down or accept the exposure.** There is no auth — the deployed demo broadcasts every submission to every connected browser. `terraform destroy` when you are done showing it off.
