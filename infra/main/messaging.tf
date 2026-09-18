# ---------------------------------------------------------------------------
# Phase 3 - the outbox drains to the queue
#
# The DynamoDB stream on the messages table triggers the relay Lambda. The
# relay maps each OUTBOX item to a TextSubmitted event and sends it to an SQS
# FIFO queue. The stream is the recovery agent: it retains records for 24
# hours, retries a failed batch, and delivers at least once.
# ---------------------------------------------------------------------------

locals {
  relay_function_name = "${var.project_name}-ingest-outbox-relay"
  events_queue_name   = "${var.project_name}-text-submitted.fifo"
}

# ---------------------------------------------------------------------------
# SQS FIFO queue and its dead-letter queue
# ---------------------------------------------------------------------------

resource "aws_sqs_queue" "text_submitted_dead_letter" {
  name       = "${var.project_name}-text-submitted-dead-letter.fifo"
  fifo_queue = true
}

resource "aws_sqs_queue" "text_submitted" {
  name = local.events_queue_name

  # FIFO gives two things this project needs: order, so the SPA list matches
  # submission order, and deduplication, so an at-least-once stream delivery
  # does not show the same message twice.
  fifo_queue = true

  # The relay supplies an explicit MessageDeduplicationId (the message id).
  # Content-based deduplication would hash the body instead, which would wrongly
  # collapse two different messages that carry identical text.
  content_based_deduplication = false

  # Long enough for the consumer Lambda in Phase 5 to finish and delete.
  visibility_timeout_seconds = 30

  redrive_policy = jsonencode({
    deadLetterTargetArn = aws_sqs_queue.text_submitted_dead_letter.arn
    maxReceiveCount     = 3
  })
}

# ---------------------------------------------------------------------------
# Relay Lambda execution role
# ---------------------------------------------------------------------------

resource "aws_iam_role" "ingest_outbox_relay" {
  name               = "${local.relay_function_name}-role"
  assume_role_policy = data.aws_iam_policy_document.lambda_assume_role.json
}

data "aws_iam_policy_document" "ingest_outbox_relay" {
  statement {
    sid    = "ReadTheMessagesStream"
    effect = "Allow"

    actions = [
      "dynamodb:GetRecords",
      "dynamodb:GetShardIterator",
      "dynamodb:DescribeStream",
      "dynamodb:ListStreams",
    ]

    # The STREAM arn, not the table arn. They are different resources.
    resources = [aws_dynamodb_table.messages.stream_arn]
  }

  statement {
    sid       = "SendTheEvent"
    effect    = "Allow"
    actions   = ["sqs:SendMessage"]
    resources = [aws_sqs_queue.text_submitted.arn]
  }

  statement {
    sid       = "WriteLogs"
    effect    = "Allow"
    actions   = ["logs:CreateLogStream", "logs:PutLogEvents"]
    resources = ["${aws_cloudwatch_log_group.ingest_outbox_relay.arn}:*"]
  }
}

resource "aws_iam_role_policy" "ingest_outbox_relay" {
  name   = "${local.relay_function_name}-policy"
  role   = aws_iam_role.ingest_outbox_relay.id
  policy = data.aws_iam_policy_document.ingest_outbox_relay.json
}

# ---------------------------------------------------------------------------
# Relay Lambda
# ---------------------------------------------------------------------------

resource "aws_cloudwatch_log_group" "ingest_outbox_relay" {
  name              = "/aws/lambda/${local.relay_function_name}"
  retention_in_days = var.log_retention_days
}

resource "aws_lambda_function" "ingest_outbox_relay" {
  function_name = local.relay_function_name
  role          = aws_iam_role.ingest_outbox_relay.arn

  runtime = "dotnet10"
  handler = "Ingest.OutboxRelay"

  architectures = ["arm64"]
  memory_size   = 512
  timeout       = 30

  filename         = data.archive_file.placeholder.output_path
  source_code_hash = data.archive_file.placeholder.output_base64sha256

  environment {
    variables = {
      OUTBOX_QUEUE_URL = aws_sqs_queue.text_submitted.url
    }
  }

  lifecycle {
    ignore_changes = [filename, source_code_hash]
  }

  depends_on = [
    aws_iam_role_policy.ingest_outbox_relay,
    aws_cloudwatch_log_group.ingest_outbox_relay,
  ]
}

# ---------------------------------------------------------------------------
# The DynamoDB trigger
# ---------------------------------------------------------------------------

resource "aws_lambda_event_source_mapping" "outbox_stream" {
  event_source_arn  = aws_dynamodb_table.messages.stream_arn
  function_name     = aws_lambda_function.ingest_outbox_relay.arn
  starting_position = "TRIM_HORIZON"

  # TRIM_HORIZON, never LATEST. AWS warns that stream polling is eventually
  # consistent during mapping creation, so LATEST can miss events - which is
  # exactly when a first terraform apply happens.

  # maximum_batching_window_in_seconds is deliberately NOT set. It defaults to
  # 0, which means "invoke as soon as records are available". Any value adds
  # whole seconds of latency to the round trip.

  # Matches the StreamsEventResponse the batch processor returns. Without this
  # the failure report is ignored and a failed record is dropped.
  function_response_types = ["ReportBatchItemFailures"]

  maximum_retry_attempts = 3
}
