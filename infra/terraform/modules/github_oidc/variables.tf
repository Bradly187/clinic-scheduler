variable "name_prefix" {
  type        = string
  description = "Prefix for resource names."
}

variable "github_owner" {
  type        = string
  description = "GitHub org/user that owns the repo (e.g. Bradly187)."
}

variable "github_repo" {
  type        = string
  description = "GitHub repository name (e.g. clinic-scheduler)."
}

variable "allowed_branches" {
  type        = list(string)
  description = "Branches whose workflow runs may assume the deploy role."
  default     = ["MVP"]
}

variable "create_oidc_provider" {
  type        = bool
  description = "Create the GitHub OIDC provider. Set false if the account already has one."
  default     = true
}

variable "existing_oidc_provider_arn" {
  type        = string
  description = "ARN of an existing GitHub OIDC provider (used when create_oidc_provider = false)."
  default     = ""
}

variable "region" {
  type        = string
  description = "AWS region (for building the ECS service ARN)."
}

variable "account_id" {
  type        = string
  description = "AWS account ID (for building the ECS service ARN)."
}

variable "ecr_repository_arn" {
  type        = string
  description = "ECR repository the pipeline may push to."
}

variable "ecs_cluster_name" {
  type        = string
  description = "ECS cluster name."
}

variable "ecs_service_name" {
  type        = string
  description = "ECS service name the pipeline may update."
}

variable "execution_role_arn" {
  type        = string
  description = "ECS task execution role ARN (for iam:PassRole)."
}

variable "task_role_arn" {
  type        = string
  description = "ECS task role ARN (for iam:PassRole)."
}

variable "tags" {
  type        = map(string)
  description = "Tags applied to all resources."
  default     = {}
}
