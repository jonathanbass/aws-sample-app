# Outputs are added by the phase that creates the resource they expose.
#
# Phase 2 (ingest)     -> http_api_url
# Phase 4 (websocket)  -> websocket_url, websocket_management_endpoint
# Phase 6 (amplify)    -> amplify_default_domain
#
# Phase 1 creates no addressable resources, so there is nothing to output yet.

output "aws_region" {
  description = "Region this stack is deployed to."
  value       = var.aws_region
}

output "http_api_url" {
  description = "Base URL of the ingest HTTP API. POST {url}/messages to submit text."
  value       = aws_apigatewayv2_stage.ingest.invoke_url
}

output "messages_table_name" {
  description = "DynamoDB table holding message and outbox items."
  value       = aws_dynamodb_table.messages.name
}

output "ingest_submit_function_name" {
  description = "Lambda the deploy-ingest workflow job pushes code to."
  value       = aws_lambda_function.ingest_submit.function_name
}

output "text_submitted_queue_url" {
  description = "SQS FIFO queue the relay publishes TextSubmitted events to."
  value       = aws_sqs_queue.text_submitted.url
}

output "ingest_outbox_relay_function_name" {
  description = "Lambda the deploy-ingest workflow job pushes relay code to."
  value       = aws_lambda_function.ingest_outbox_relay.function_name
}

output "websocket_url" {
  description = "wss:// URL the SPA connects to."
  value       = aws_apigatewayv2_stage.notifier.invoke_url
}

output "websocket_management_endpoint" {
  description = "HTTPS endpoint the Phase 5 consumer calls PostToConnection on. NOT the wss:// URL."
  value       = "https://${aws_apigatewayv2_api.notifier.id}.execute-api.${var.aws_region}.amazonaws.com/${aws_apigatewayv2_stage.notifier.name}"
}

output "connections_table_name" {
  description = "DynamoDB table holding live WebSocket connection ids."
  value       = aws_dynamodb_table.connections.name
}

output "notifier_connections_function_name" {
  description = "Lambda the deploy-notifier workflow job pushes code to."
  value       = aws_lambda_function.notifier_connections.function_name
}
