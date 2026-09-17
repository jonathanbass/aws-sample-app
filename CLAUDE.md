# aws-sample-app

Personal POC proving event-driven architecture on AWS serverless. Not a Bennetts project — **no Bennetts naming anywhere in this repo**.

## HARD RULE: Claude never touches AWS directly

**The AWS CLI belongs to the user. Claude does not run it — not even read-only commands.**

Claude must not run `aws`, `sam`, `cdk`, `amplify`, `eb`, or any AWS SDK call, and must not run `terraform apply`, `destroy`, `import`, `state`, `plan` or `init`-against-a-backend. `list-*`, `describe-*` and `get-*` are **not** exempt — a read-only call is still Claude interacting with the account, and the account has the user's other live applications in it.

Claude's only route to AWS is Terraform code that CI applies.

When Claude needs information from AWS, it asks and waits, supplying the exact command for the user to run with the `!` prefix (e.g. `! aws sts get-caller-identity`).

Allowed locally: `terraform fmt`, `terraform validate`, `dotnet build`, `dotnet test`, `npm test` — none authenticate.

Full rationale: `.claude/rules/aws-cli-boundary.md`.

## Key documents

| Document | Holds |
|---|---|
| `plan.md` | Original requirements. Superseded — names Wolverine and SignalR, neither of which survived. |
| `docs/designs/2026-09-17-aws-serverless-event-driven-poc.md` | The design: decisions D1–D14 with rationale and rejected alternatives, verified recon facts, assumptions. **Read before changing architecture.** |
| `docs/plans/2026-09-17-aws-serverless-event-driven-poc.md` | Phased implementation plan with per-task acceptance criteria. |

## Architecture in one line

Browser → API Gateway HTTP API → Lambda → DynamoDB (`TransactWriteItems` = domain item + outbox item) → DynamoDB trigger → Lambda → SQS FIFO → Lambda → API Gateway WebSocket → browser.

## Non-obvious conventions

- **Lambda-first.** No ASP.NET hosting anywhere. All four Lambdas are the same shape: `FunctionHandler` over an AWS event type, wrapping a small constructor-injected service with no AWS types in its public signature.
- **No Wolverine, no EF Core, no relational database.** Most of the global `~/.claude/rules/` files are Azure/Bennetts-specific and do not apply here — see design doc §6 for the applies/does-not-apply table.
- **Terraform is applied by CI only**, never locally, except `infra/bootstrap/` which the user applies once by hand.
- **Every CloudWatch log group sets `retention_in_days = 7`.** The default never expires and slowly costs money.
- Testing: unit tests over the injectable services, plus handler contract tests that construct a real AWS event and invoke the real `FunctionHandler`. No BDD Context pattern.
