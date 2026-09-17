# ---------------------------------------------------------------------------
# Phase 2 - Ingest: messages table, HTTP API, submit Lambda
# ---------------------------------------------------------------------------

locals {
  messages_table_name  = "${var.project_name}-messages"
  submit_function_name = "${var.project_name}-ingest-submit"
}

# ---------------------------------------------------------------------------
# DynamoDB - single table holding the domain item and the outbox item
# ---------------------------------------------------------------------------

resource "aws_dynamodb_table" "messages" {
  name         = local.messages_table_name
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "pk"
  range_key    = "sk"

  attribute {
    name = "pk"
    type = "S"
  }

  attribute {
    name = "sk"
    type = "S"
  }

  # The stream is enabled NOW even though nothing consumes it until Phase 3.
  # Turning it on later is a change to a live table; enabling it up front costs
  # nothing (streams have no idle charge) and keeps Phase 3 additive.
  stream_enabled   = true
  stream_view_type = "NEW_IMAGE"
}

# ---------------------------------------------------------------------------
# Lambda execution role
# ---------------------------------------------------------------------------

data "aws_iam_policy_document" "lambda_assume_role" {
  statement {
    effect  = "Allow"
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["lambda.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "ingest_submit" {
  # MUST keep the project_name prefix: the CI role's IAM permissions are scoped
  # to roles named "${var.project_name}-*" (see infra/bootstrap/main.tf).
  name               = "${local.submit_function_name}-role"
  assume_role_policy = data.aws_iam_policy_document.lambda_assume_role.json
}

data "aws_iam_policy_document" "ingest_submit" {
  statement {
    sid    = "WriteMessagesTransactionally"
    effect = "Allow"

    # Least privilege: write-only, and only this table. TransactWriteItems is
    # authorised via PutItem on the target table, but naming it explicitly
    # documents intent and survives a policy-simulator review.
    actions = [
      "dynamodb:PutItem",
      "dynamodb:TransactWriteItems",
    ]

    resources = [aws_dynamodb_table.messages.arn]
  }

  statement {
    sid    = "WriteLogs"
    effect = "Allow"

    actions = [
      "logs:CreateLogStream",
      "logs:PutLogEvents",
    ]

    resources = ["${aws_cloudwatch_log_group.ingest_submit.arn}:*"]
  }
}

resource "aws_iam_role_policy" "ingest_submit" {
  name   = "${local.submit_function_name}-policy"
  role   = aws_iam_role.ingest_submit.id
  policy = data.aws_iam_policy_document.ingest_submit.json
}

# ---------------------------------------------------------------------------
# Lambda
# ---------------------------------------------------------------------------

# Created explicitly rather than letting Lambda auto-create it on first invoke:
# an auto-created group has NO retention and keeps logs forever.
resource "aws_cloudwatch_log_group" "ingest_submit" {
  name              = "/aws/lambda/${local.submit_function_name}"
  retention_in_days = var.log_retention_days
}

resource "aws_lambda_function" "ingest_submit" {
  function_name = local.submit_function_name
  role          = aws_iam_role.ingest_submit.arn

  # Executable-style handler (Amazon.Lambda.RuntimeSupport): the handler string
  # is just the assembly name, not Assembly::Type::Method.
  runtime = "dotnet10"
  handler = "Ingest.Api"

  architectures = ["arm64"]
  memory_size   = 512
  timeout       = 10

  filename         = data.archive_file.placeholder.output_path
  source_code_hash = data.archive_file.placeholder.output_base64sha256

  environment {
    variables = {
      MESSAGES_TABLE_NAME = aws_dynamodb_table.messages.name
    }
  }

  # Real code is pushed by the deploy-ingest job via update-function-code.
  # Without this, every terraform apply would revert the function to the
  # placeholder zip.
  lifecycle {
    ignore_changes = [filename, source_code_hash]
  }

  depends_on = [
    aws_iam_role_policy.ingest_submit,
    aws_cloudwatch_log_group.ingest_submit,
  ]
}

# ---------------------------------------------------------------------------
# HTTP API
# ---------------------------------------------------------------------------

resource "aws_apigatewayv2_api" "ingest" {
  name          = "${var.project_name}-ingest"
  protocol_type = "HTTP"

  # Wide open for now. Phase 6 narrows allow_origins to the Amplify domain
  # once that domain exists.
  cors_configuration {
    allow_origins = ["*"]
    allow_methods = ["POST", "OPTIONS"]
    allow_headers = ["content-type"]
  }
}

resource "aws_apigatewayv2_integration" "ingest_submit" {
  api_id                 = aws_apigatewayv2_api.ingest.id
  integration_type       = "AWS_PROXY"
  integration_uri        = aws_lambda_function.ingest_submit.invoke_arn
  payload_format_version = "2.0"
}

resource "aws_apigatewayv2_route" "submit_message" {
  api_id    = aws_apigatewayv2_api.ingest.id
  route_key = "POST /messages"
  target    = "integrations/${aws_apigatewayv2_integration.ingest_submit.id}"
}

resource "aws_apigatewayv2_stage" "ingest" {
  api_id = aws_apigatewayv2_api.ingest.id
  name   = "$default"

  # Without auto_deploy the stage never serves the routes and every request
  # 404s with no useful diagnostic.
  auto_deploy = true
}

resource "aws_lambda_permission" "ingest_submit" {
  statement_id  = "AllowExecutionFromApiGateway"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.ingest_submit.function_name
  principal     = "apigateway.amazonaws.com"
  source_arn    = "${aws_apigatewayv2_api.ingest.execution_arn}/*/*"
}
