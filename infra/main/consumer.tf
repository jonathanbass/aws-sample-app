# ---------------------------------------------------------------------------
# Phase 5 - events push to connected browsers
#
# The SQS FIFO queue triggers this Lambda. It reads every live connection id
# from the connections table and pushes the event to each open socket through
# the API Gateway management endpoint. This is the join between the two halves
# of the round trip.
# ---------------------------------------------------------------------------

locals {
  consumer_function_name = "${var.project_name}-notifier-consumer"
}

resource "aws_iam_role" "notifier_consumer" {
  name               = "${local.consumer_function_name}-role"
  assume_role_policy = data.aws_iam_policy_document.lambda_assume_role.json
}

data "aws_iam_policy_document" "notifier_consumer" {
  statement {
    sid    = "ReadTheQueue"
    effect = "Allow"

    actions = [
      "sqs:ReceiveMessage",
      "sqs:DeleteMessage",
      "sqs:GetQueueAttributes",
    ]

    resources = [aws_sqs_queue.text_submitted.arn]
  }

  statement {
    sid    = "ReadAndPruneConnections"
    effect = "Allow"

    # Scan to list every open socket. DeleteItem to remove one that has gone.
    actions = [
      "dynamodb:Scan",
      "dynamodb:DeleteItem",
    ]

    resources = [aws_dynamodb_table.connections.arn]
  }

  statement {
    sid    = "PushToBrowsers"
    effect = "Allow"

    # execute-api:ManageConnections is what PostToConnection needs. It is NOT
    # execute-api:Invoke, which governs calling the API itself.
    actions   = ["execute-api:ManageConnections"]
    resources = ["${aws_apigatewayv2_api.notifier.execution_arn}/*/*/@connections/*"]
  }

  statement {
    sid       = "WriteLogs"
    effect    = "Allow"
    actions   = ["logs:CreateLogStream", "logs:PutLogEvents"]
    resources = ["${aws_cloudwatch_log_group.notifier_consumer.arn}:*"]
  }
}

resource "aws_iam_role_policy" "notifier_consumer" {
  name   = "${local.consumer_function_name}-policy"
  role   = aws_iam_role.notifier_consumer.id
  policy = data.aws_iam_policy_document.notifier_consumer.json
}

resource "aws_cloudwatch_log_group" "notifier_consumer" {
  name              = "/aws/lambda/${local.consumer_function_name}"
  retention_in_days = var.log_retention_days
}

resource "aws_lambda_function" "notifier_consumer" {
  function_name = local.consumer_function_name
  role          = aws_iam_role.notifier_consumer.arn

  runtime = "dotnet10"
  handler = "Notifier.Consumer"

  architectures = ["arm64"]
  memory_size   = 512

  # Must stay below the queue's visibility_timeout_seconds of 30. A function
  # that runs longer than the visibility timeout lets SQS hand the same
  # message to a second invocation, and the browser shows it twice.
  timeout = 25

  filename         = data.archive_file.placeholder.output_path
  source_code_hash = data.archive_file.placeholder.output_base64sha256

  environment {
    variables = {
      CONNECTIONS_TABLE_NAME = aws_dynamodb_table.connections.name

      # The https:// MANAGEMENT endpoint, not the wss:// URL. PostToConnection
      # is an HTTPS call; pointing this at wss:// fails at runtime.
      WEBSOCKET_MANAGEMENT_ENDPOINT = "https://${aws_apigatewayv2_api.notifier.id}.execute-api.${var.aws_region}.amazonaws.com/${aws_apigatewayv2_stage.notifier.name}"
    }
  }

  lifecycle {
    ignore_changes = [filename, source_code_hash]
  }

  depends_on = [
    aws_iam_role_policy.notifier_consumer,
    aws_cloudwatch_log_group.notifier_consumer,
  ]
}

resource "aws_lambda_event_source_mapping" "text_submitted" {
  event_source_arn = aws_sqs_queue.text_submitted.arn
  function_name    = aws_lambda_function.notifier_consumer.arn

  # maximum_batching_window_in_seconds is deliberately NOT set. It defaults to
  # 0, so a message invokes the function as soon as it arrives.

  # Matches the SQSBatchResponse the processor returns. Without this the
  # failure report is ignored and a poison message takes the whole batch with
  # it to the dead-letter queue.
  function_response_types = ["ReportBatchItemFailures"]
}
