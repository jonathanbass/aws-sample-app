# Bootstrap — run this ONCE, by hand

This is the only Terraform in the repo that is not applied by CI, and the only
thing you ever run locally.

**Why it has to be manual:** every other stack is applied by GitHub Actions,
which authenticates by assuming an IAM role via OIDC and stores its state in an
S3 bucket. This stack creates that role, that OIDC provider, and that bucket.
It cannot be created by the thing that needs it to already exist.

It creates exactly three things:

1. A versioned, encrypted, public-access-blocked S3 bucket for Terraform state.
2. The GitHub Actions OIDC identity provider.
3. An IAM role that only `main` of your repo can assume.

After this, you never touch Terraform locally again.

---

## Prerequisites

| Tool | Check | Notes |
|---|---|---|
| Terraform >= 1.11 | `terraform version` | 1.11 is required for native S3 state locking (`use_lockfile`) |
| AWS CLI, authenticated as an admin | `aws sts get-caller-identity` | Must return YOUR account. This is the one time admin rights are needed. |

If `aws sts get-caller-identity` fails, authenticate first. In this session you
can run it inline by prefixing with `!`, e.g. `! aws configure`.

---

## Steps

**1. Pick a globally unique bucket name and fill in your GitHub username.**

```bash
cd infra/bootstrap
cp terraform.tfvars.example terraform.tfvars
```

Edit `terraform.tfvars`:

- `state_bucket_name` — must be globally unique across all of AWS. Add a random
  suffix, e.g. `aws-sample-app-tfstate-7f3a91`.
- `github_owner` — your GitHub username (the owner segment of the repo URL).

**2. Apply.**

```bash
terraform init
terraform plan          # expect 8 resources to add
terraform apply
```

**3. Record the outputs.** `terraform apply` prints:

```
ci_role_arn       = "arn:aws:iam::123456789012:role/aws-sample-app-ci"
state_bucket_name = "aws-sample-app-tfstate-7f3a91"
aws_region        = "eu-west-1"
```

**4. Add them to GitHub.** Repository → Settings → Secrets and variables →
Actions → **Variables** tab (these are not secrets — the role ARN is useless
without a workflow run on `main` of this exact repo):

| Name | Value |
|---|---|
| `AWS_ROLE_ARN` | the `ci_role_arn` output |
| `AWS_REGION` | `eu-west-1` |
| `TF_STATE_BUCKET` | the `state_bucket_name` output |

**5. Verify the role is assumable** — this is the thing worth proving before
building anything on top of it. The first push to `main` after Phase 0's
`deploy.yml` exists should show the `terraform` job authenticating with no
stored AWS credentials anywhere.

---

## Gotchas

**`EntityAlreadyExists` on the OIDC provider.** You already have a GitHub OIDC
provider in this account from another project — there can only be one per
account. Import it instead of creating a second:

```bash
terraform import aws_iam_openid_connect_provider.github \
  arn:aws:iam::<your-account-id>:oidc-provider/token.actions.githubusercontent.com
```

**`BucketAlreadyExists`.** S3 bucket names are global across every AWS account,
not just yours. Pick a different suffix.

**State lives locally, in this directory, and is gitignored.** Losing
`terraform.tfstate` here does not break anything that is already deployed — the
bucket, provider and role keep working. You would just need to `terraform
import` the three resources if you ever wanted to change them. Given this stack
is applied once and then left alone, that is an acceptable trade rather than a
problem to solve.

**Permissions are deliberately broad.** The CI role gets `PowerUserAccess` plus
scoped IAM rights over roles named `aws-sample-app-*`. That is wider than you
would grant at work. Narrowing it to exact actions per service is a rabbit hole
that would block every phase; it is a conscious POC trade-off, not an oversight.
The role still cannot touch IAM outside this project's prefix, and cannot modify
itself.
