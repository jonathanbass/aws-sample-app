terraform {
  required_version = ">= 1.11"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }

  # Deliberately LOCAL state. This stack creates the bucket that every other
  # stack stores its state in, so it cannot store its own state there.
  # Run once, then never again. See README.md.
}

provider "aws" {
  region = var.aws_region

  default_tags {
    tags = {
      Project   = var.project_name
      ManagedBy = "terraform-bootstrap"
    }
  }
}

data "aws_caller_identity" "current" {}

# ---------------------------------------------------------------------------
# 1. Terraform state bucket
# ---------------------------------------------------------------------------

resource "aws_s3_bucket" "state" {
  bucket = var.state_bucket_name
}

resource "aws_s3_bucket_versioning" "state" {
  bucket = aws_s3_bucket.state.id

  versioning_configuration {
    status = "Enabled"
  }
}

resource "aws_s3_bucket_server_side_encryption_configuration" "state" {
  bucket = aws_s3_bucket.state.id

  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

resource "aws_s3_bucket_public_access_block" "state" {
  bucket = aws_s3_bucket.state.id

  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

# No DynamoDB lock table: Terraform 1.11+ locks natively in S3 via
# use_lockfile = true, configured in infra/main/main.tf.

# ---------------------------------------------------------------------------
# 2. GitHub OIDC identity provider
# ---------------------------------------------------------------------------

resource "aws_iam_openid_connect_provider" "github" {
  url             = "https://token.actions.githubusercontent.com"
  client_id_list  = ["sts.amazonaws.com"]

  # AWS no longer validates this thumbprint for GitHub's IdP - it uses its own
  # trust store - but the API still requires the field to be populated.
  thumbprint_list = ["6938fd4d98bab03faadb97b34396831e3780aea1"]
}

# ---------------------------------------------------------------------------
# 3. CI role assumed by GitHub Actions
# ---------------------------------------------------------------------------

data "aws_iam_policy_document" "ci_trust" {
  statement {
    effect  = "Allow"
    actions = ["sts:AssumeRoleWithWebIdentity"]

    principals {
      type        = "Federated"
      identifiers = [aws_iam_openid_connect_provider.github.arn]
    }

    condition {
      test     = "StringEquals"
      variable = "token.actions.githubusercontent.com:aud"
      values   = ["sts.amazonaws.com"]
    }

    # Scoped to one branch of one repo. A run on any other branch, or in a
    # fork, cannot assume this role.
    condition {
      test     = "StringEquals"
      variable = "token.actions.githubusercontent.com:sub"
      values   = ["repo:${var.github_owner}/${var.github_repo}:ref:refs/heads/${var.deploy_branch}"]
    }
  }
}

resource "aws_iam_role" "ci" {
  name               = "${var.project_name}-ci"
  description        = "Assumed by GitHub Actions via OIDC to deploy ${var.project_name}."
  assume_role_policy = data.aws_iam_policy_document.ci_trust.json
}

# PowerUserAccess covers DynamoDB, SQS, Lambda, API Gateway, Amplify, logs and
# S3, but explicitly denies IAM. Lambda execution roles therefore need the
# scoped grant below.
#
# NOTE: this is broader than you would grant in a work account. It is a
# deliberate POC trade-off - tightening it to exact actions is a rabbit hole
# that would block every phase.
resource "aws_iam_role_policy_attachment" "ci_power_user" {
  role       = aws_iam_role.ci.name
  policy_arn = "arn:aws:iam::aws:policy/PowerUserAccess"
}

data "aws_iam_policy_document" "ci_iam" {
  statement {
    effect = "Allow"

    actions = [
      "iam:CreateRole",
      "iam:DeleteRole",
      "iam:GetRole",
      "iam:TagRole",
      "iam:UntagRole",
      "iam:UpdateRole",
      "iam:PassRole",
      "iam:AttachRolePolicy",
      "iam:DetachRolePolicy",
      "iam:ListAttachedRolePolicies",
      "iam:PutRolePolicy",
      "iam:DeleteRolePolicy",
      "iam:GetRolePolicy",
      "iam:ListRolePolicies",
    ]

    # Only roles this project owns. The CI role cannot touch anything else in
    # the account, including itself.
    resources = [
      "arn:aws:iam::${data.aws_caller_identity.current.account_id}:role/${var.project_name}-*"
    ]
  }
}

resource "aws_iam_role_policy" "ci_iam" {
  name   = "${var.project_name}-ci-iam"
  role   = aws_iam_role.ci.id
  policy = data.aws_iam_policy_document.ci_iam.json
}
