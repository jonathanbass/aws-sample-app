terraform {
  required_version = ">= 1.11"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.65"
    }
    archive = {
      source  = "hashicorp/archive"
      version = "~> 2.7"
    }
  }

  # Partial configuration: `bucket` is supplied by CI via
  #   terraform init -backend-config="bucket=$TF_STATE_BUCKET"
  # because backend blocks cannot use variables or interpolation.
  #
  # use_lockfile gives native S3 state locking (Terraform 1.11+). There is
  # deliberately NO dynamodb_table - that argument is deprecated.
  backend "s3" {
    key          = "main/terraform.tfstate"
    region       = "eu-west-1"
    encrypt      = true
    use_lockfile = true
  }
}

provider "aws" {
  region = var.aws_region

  default_tags {
    tags = {
      Project   = var.project_name
      ManagedBy = "terraform"
    }
  }
}

# ---------------------------------------------------------------------------
# Placeholder Lambda artifact
# ---------------------------------------------------------------------------
# Terraform creates every Lambda function and its triggers with this zip, and
# ignores code changes thereafter. Real code is pushed separately by the
# deploy workflow via `aws lambda update-function-code`.
#
# This is what makes "Terraform first, then apps" meaningful: the function,
# its IAM role and its event source mappings all exist before any C# ships.
# A function created with this zip will fail if invoked - that is expected and
# harmless, because the real code deploys immediately afterwards.

data "archive_file" "placeholder" {
  type        = "zip"
  output_path = "${path.module}/.build/placeholder.zip"

  source {
    content  = "placeholder - replaced by the deploy workflow"
    filename = "placeholder.txt"
  }
}

# ---------------------------------------------------------------------------
# Amplify is deliberately NOT managed here.
#
# The AWS provider cannot connect an Amplify app to GitHub. `oauth_token`
# wires the deprecated OAuth path, and `access_token` accepts only classic
# ghp_ tokens, not the fine-grained ones GitHub now issues. CodeConnections
# support is still an open enhancement request on the provider
# (hashicorp/terraform-provider-aws#32610).
#
# The console only offers repository connection when an app is CREATED, not
# for an existing one. So an app created by Terraform can never be connected
# by either route.
#
# The app was therefore created in the Amplify console from GitHub, and it
# builds on push. Its settings live in the console and in amplify.yml at the
# repository root. Do not add aws_amplify_app back: it would create a second,
# empty app beside the working one.
#
# See the design document, decision D9.
# ---------------------------------------------------------------------------
