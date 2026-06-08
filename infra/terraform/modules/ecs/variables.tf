variable "name_prefix" {
  type        = string
  description = "Prefix for resource names; also used as the container/family name."
}

variable "region" {
  type        = string
  description = "AWS region (for the CloudWatch logs driver)."
}

variable "vpc_id" {
  type        = string
  description = "VPC the ALB and targets live in."
}

variable "public_subnet_ids" {
  type        = list(string)
  description = "Public subnets for the ALB and Fargate tasks."
}

variable "alb_security_group_id" {
  type        = string
  description = "Security group for the ALB."
}

variable "app_security_group_id" {
  type        = string
  description = "Security group for the Fargate tasks."
}

variable "container_image" {
  type        = string
  description = "Full image reference (e.g. <ecr-url>:latest)."
}

variable "container_port" {
  type        = number
  description = "Port the container listens on."
  default     = 8080
}

variable "cpu" {
  type        = number
  description = "Fargate task CPU units (256 = 0.25 vCPU)."
  default     = 256
}

variable "memory" {
  type        = number
  description = "Fargate task memory in MiB."
  default     = 512
}

variable "desired_count" {
  type        = number
  description = "Number of running tasks."
  default     = 1
}

variable "aspnetcore_environment" {
  type        = string
  description = "ASPNETCORE_ENVIRONMENT value."
  default     = "Production"
}

variable "connection_string_secret_arn" {
  type        = string
  description = "Secrets Manager ARN holding the Npgsql connection string."
}

variable "seed_admin_password_secret_arn" {
  type        = string
  description = "Secrets Manager ARN holding the seed admin password."
}

variable "secret_arns" {
  type        = list(string)
  description = "All secret ARNs the execution role may read."
}

variable "acm_certificate_arn" {
  type        = string
  description = "ACM cert ARN for HTTPS. Empty string serves plain HTTP on :80."
  default     = ""
}

variable "health_check_path" {
  type        = string
  description = "Target group health check path."
  default     = "/"
}

variable "log_retention_days" {
  type        = number
  description = "CloudWatch log retention."
  default     = 14
}

variable "tags" {
  type        = map(string)
  description = "Tags applied to all resources."
  default     = {}
}
