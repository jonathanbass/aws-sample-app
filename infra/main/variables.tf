variable "aws_region" {
  description = "Region everything is deployed to."
  type        = string
  default     = "eu-west-1"
}

variable "project_name" {
  description = "Prefix for every resource name. Must match the prefix the CI role is scoped to in infra/bootstrap."
  type        = string
  default     = "aws-sample-app"
}

variable "log_retention_days" {
  description = "Retention for every CloudWatch log group. The AWS default is never-expire, which slowly costs money."
  type        = number
  default     = 7
}
