output "ci_role_arn" {
  description = "Set this as the GitHub Actions repository variable AWS_ROLE_ARN."
  value       = aws_iam_role.ci.arn
}

output "state_bucket_name" {
  description = "Set this as the GitHub Actions repository variable TF_STATE_BUCKET, and put it in infra/main/main.tf."
  value       = aws_s3_bucket.state.id
}

output "aws_region" {
  description = "Region the CI workflow should target."
  value       = var.aws_region
}
