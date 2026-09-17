---
description: Claude must never invoke the AWS CLI or touch AWS directly. The user owns all AWS interaction; Claude's only route to AWS is Terraform code that CI applies.
paths:
  - "**/*.tf"
  - "**/*.tfvars"
  - "**/*.yml"
  - "**/*.yaml"
  - "**/*.cs"
  - "**/*.ts"
  - "**/*.tsx"
---

# AWS Interaction Boundary

**The AWS CLI belongs to the user. Claude does not run it. Ever.**

This is not a permissions question or a risk-tolerance question — it is a division of responsibility. The user drives AWS. Claude writes the Terraform that describes AWS.

## What Claude must NOT do

- Run `aws ...` in any form — Bash, PowerShell, a script, or a heredoc.
- Run any other tool that authenticates to AWS: `sam`, `cdk`, `amplify`, `eb`, `copilot`, or an AWS SDK call from a throwaway script.
- Run `terraform apply`, `terraform destroy`, `terraform import`, or `terraform state` subcommands.
- Run `terraform plan` or `terraform init` against a configured backend — both authenticate to AWS and read live state.
- Inspect, audit, list or "just check" anything in the account, **including read-only calls**. A read-only call is still Claude interacting with AWS.

The read-only exemption is the one that needs stating explicitly, because it is the one that feels harmless. It is not exempt. `describe-*`, `list-*` and `get-*` are all out.

## What Claude MAY do

- Write, edit and review Terraform, workflow YAML, and application code.
- Run purely local commands that never authenticate: `terraform fmt`, `terraform validate`, `dotnet build`, `dotnet test`, `npm test`.
- Read AWS **documentation** and provider schemas.

## When Claude needs something from AWS

Ask. Give the user the exact command to run, and wait.

In Claude Code the user can run it inline by prefixing with `!`, e.g. `! aws sts get-caller-identity`, so the output lands in the conversation. Offer the command that way rather than running it.

## Why

1. **It is the user's account, with the user's other live applications in it.** Claude poking around — even read-only — is scope Claude was not given.
2. **Terraform is the single source of truth.** Anything Claude did through the CLI would be drift: a real resource that no `.tf` file describes, invisible to the next `plan`, and a surprise to whoever runs it next.
3. **Everything is applied by CI, not locally** (see `docs/designs/2026-09-17-aws-serverless-event-driven-poc.md`, D6/D7). A local apply from Claude would bypass the pipeline that is supposed to be the only path to production.

## The one exception, which is the user's, not Claude's

`infra/bootstrap/` is applied once, by hand, by the user. It cannot be applied by CI because it creates the role CI uses to authenticate. Claude writes those files; Claude never runs them.
