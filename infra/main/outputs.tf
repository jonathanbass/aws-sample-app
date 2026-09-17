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
