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
