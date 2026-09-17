variable "aws_region" {
  description = "Region everything is deployed to."
  type        = string
  default     = "eu-west-1"
}

variable "state_bucket_name" {
  description = "Globally unique S3 bucket name for Terraform state. Must not already exist."
  type        = string
}

variable "github_owner" {
  description = "GitHub user or org that owns the repository, e.g. \"jbass\"."
  type        = string
}

variable "github_repo" {
  description = "Repository name, e.g. \"aws-sample-app\"."
  type        = string
  default     = "aws-sample-app"
}

variable "deploy_branch" {
  description = "Only workflow runs on this branch may assume the CI role."
  type        = string
  default     = "main"
}

variable "project_name" {
  description = "Prefix for IAM resources. The CI role may only manage IAM roles starting with this."
  type        = string
  default     = "aws-sample-app"
}
