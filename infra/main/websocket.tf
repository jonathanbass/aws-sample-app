# ---------------------------------------------------------------------------
# Phase 4 - the browser connection registry
#
# API Gateway holds the WebSocket connection. The browser cannot be addressed
# directly, so the system records each connection id in DynamoDB. Phase 5 reads
# that table to decide who to push an event to.
# ---------------------------------------------------------------------------

locals {
  connections_table_name    = "${var.project_name}-connections"
  connections_function_name = "${var.project_name}-notifier-connections"
}

# ---------------------------------------------------------------------------
# Connections table
# ---------------------------------------------------------------------------

resource "aws_dynamodb_table" "connections" {
  name         = local.connections_table_name
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "connectionId"

  attribute {
    name = "connectionId"
    type = "S"
  }

  # API Gateway closes a WebSocket after two hours, and a $disconnect can be
  # missed. Without this TTL a dead connection id stays for ever and every
  # Phase 5 broadcast tries to push to it.
  ttl {
    attribute_name = "expiresAt"
    enabled        = true
  }
}

# ---------------------------------------------------------------------------
# Connections Lambda
# ---------------------------------------------------------------------------

resource "aws_iam_role" "notifier_connections" {
  name               = "${local.connections_function_name}-role"
  assume_role_policy = data.aws_iam_policy_document.lambda_assume_role.json
}

data "aws_iam_policy_document" "notifier_connections" {
  statement {
    sid       = "RecordConnections"
    effect    = "Allow"
    actions   = ["dynamodb:PutItem", "dynamodb:DeleteItem"]
    resources = [aws_dynamodb_table.connections.arn]
  }

  statement {
    sid       = "WriteLogs"
    effect    = "Allow"
    actions   = ["logs:CreateLogStream", "logs:PutLogEvents"]
    resources = ["${aws_cloudwatch_log_group.notifier_connections.arn}:*"]
  }
}

resource "aws_iam_role_policy" "notifier_connections" {
  name   = "${local.connections_function_name}-policy"
  role   = aws_iam_role.notifier_connections.id
  policy = data.aws_iam_policy_document.notifier_connections.json
}

resource "aws_cloudwatch_log_group" "notifier_connections" {
  name              = "/aws/lambda/${local.connections_function_name}"
  retention_in_days = var.log_retention_days
}

resource "aws_lambda_function" "notifier_connections" {
  function_name = local.connections_function_name
  role          = aws_iam_role.notifier_connections.arn

  runtime = "dotnet10"
  handler = "Notifier.Connections"

  architectures = ["arm64"]
  memory_size   = 512
  timeout       = 10

  filename         = data.archive_file.placeholder.output_path
  source_code_hash = data.archive_file.placeholder.output_base64sha256

  environment {
    variables = {
      CONNECTIONS_TABLE_NAME = aws_dynamodb_table.connections.name
    }
  }

  lifecycle {
    ignore_changes = [filename, source_code_hash]
  }

  depends_on = [
    aws_iam_role_policy.notifier_connections,
    aws_cloudwatch_log_group.notifier_connections,
  ]
}

# ---------------------------------------------------------------------------
# WebSocket API
# ---------------------------------------------------------------------------

resource "aws_apigatewayv2_api" "notifier" {
  name          = "${var.project_name}-notifier"
  protocol_type = "WEBSOCKET"

  # Required for a WebSocket API even though this project defines no custom
  # routes. API Gateway reads this expression from each inbound frame to pick
  # a route; $connect and $disconnect are matched before it is used.
  route_selection_expression = "$request.body.action"
}

resource "aws_apigatewayv2_integration" "notifier_connections" {
  api_id           = aws_apigatewayv2_api.notifier.id
  integration_type = "AWS_PROXY"
  integration_uri  = aws_lambda_function.notifier_connections.invoke_arn

  # A WebSocket route delivers the version 1 proxy payload. Program.cs reads
  # APIGatewayProxyRequest to match.
  integration_method = "POST"
}

resource "aws_apigatewayv2_route" "connect" {
  api_id    = aws_apigatewayv2_api.notifier.id
  route_key = "$connect"
  target    = "integrations/${aws_apigatewayv2_integration.notifier_connections.id}"
}

resource "aws_apigatewayv2_route" "disconnect" {
  api_id    = aws_apigatewayv2_api.notifier.id
  route_key = "$disconnect"
  target    = "integrations/${aws_apigatewayv2_integration.notifier_connections.id}"
}

resource "aws_apigatewayv2_stage" "notifier" {
  api_id = aws_apigatewayv2_api.notifier.id
  name   = "live"

  # Without auto_deploy the stage never serves the routes, and every connection
  # attempt fails with no useful diagnostic.
  auto_deploy = true
}

resource "aws_lambda_permission" "notifier_connections" {
  statement_id  = "AllowExecutionFromWebSocketApi"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.notifier_connections.function_name
  principal     = "apigateway.amazonaws.com"
  source_arn    = "${aws_apigatewayv2_api.notifier.execution_arn}/*/*"
}
