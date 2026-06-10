variable "region" {
  type        = string
  description = "AWS region to deploy into."
  default     = "us-east-1"
}

variable "project_name" {
  type        = string
  description = "Project name, used as a resource name prefix."
  default     = "clinic-scheduler"
}

variable "environment" {
  type        = string
  description = "Environment name, used as a resource name suffix."
  default     = "prod"
}

# --- Network -----------------------------------------------------------------
variable "vpc_cidr" {
  type        = string
  description = "CIDR block for the VPC."
  default     = "10.20.0.0/16"
}

variable "public_subnet_cidrs" {
  type        = list(string)
  description = "Public subnet CIDRs (ALB + Fargate tasks)."
  default     = ["10.20.0.0/24", "10.20.1.0/24"]
}

variable "private_subnet_cidrs" {
  type        = list(string)
  description = "Private subnet CIDRs (RDS)."
  default     = ["10.20.10.0/24", "10.20.11.0/24"]
}

variable "alb_ingress_cidrs" {
  type        = list(string)
  description = "CIDRs allowed to reach the ALB. Narrow this to your IP for a private demo."
  default     = ["0.0.0.0/0"]
}

# --- Database ----------------------------------------------------------------
variable "db_name" {
  type        = string
  description = "Initial database name (must match the app's expectation)."
  default     = "clinic_scheduler"
}

variable "db_username" {
  type        = string
  description = "RDS master username."
  default     = "clinic_admin"
}

variable "db_instance_class" {
  type        = string
  description = "RDS instance class."
  default     = "db.t4g.micro"
}

variable "db_allocated_storage" {
  type        = number
  description = "Initial RDS storage in GiB."
  default     = 20
}

variable "db_engine_version" {
  type        = string
  description = "PostgreSQL engine version."
  default     = "17"
}

variable "db_multi_az" {
  type        = bool
  description = "Enable Multi-AZ for the database."
  default     = false
}

variable "db_deletion_protection" {
  type        = bool
  description = "Block accidental deletion of the DB instance. Disable only for sandbox teardown."
  default     = true
}

variable "db_skip_final_snapshot" {
  type        = bool
  description = "Skip the final snapshot on destroy. Set true only for sandbox teardown."
  default     = false
}

# --- App / ECS ---------------------------------------------------------------
variable "container_image" {
  type        = string
  description = "Override the container image. Empty string uses <ecr-url>:latest."
  default     = ""
}

variable "container_cpu" {
  type        = number
  description = "Fargate task CPU units."
  default     = 256
}

variable "container_memory" {
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
  description = "ASPNETCORE_ENVIRONMENT for the container."
  default     = "Production"
}

variable "health_check_path" {
  type        = string
  description = "ALB target group health check path."
  default     = "/health"
}

variable "acm_certificate_arn" {
  type        = string
  description = "ACM certificate ARN for HTTPS on the ALB. Empty string serves HTTP on :80."
  default     = ""
}

# --- CI/CD (GitHub OIDC) -----------------------------------------------------
variable "enable_github_oidc" {
  type        = bool
  description = "Create the GitHub Actions OIDC deploy role."
  default     = true
}

variable "create_github_oidc_provider" {
  type        = bool
  description = "Create the GitHub OIDC provider. Set false if the account already has one (only one per account is allowed)."
  default     = true
}

variable "existing_github_oidc_provider_arn" {
  type        = string
  description = "ARN of an existing GitHub OIDC provider, used when create_github_oidc_provider = false."
  default     = ""
}

variable "github_owner" {
  type        = string
  description = "GitHub org/user that owns the repo."
  default     = "Bradly187"
}

variable "github_repo" {
  type        = string
  description = "GitHub repository name."
  default     = "clinic-scheduler"
}

variable "deploy_branches" {
  type        = list(string)
  description = "Branches allowed to assume the deploy role."
  default     = ["MVP"]
}

# --- Secrets -----------------------------------------------------------------
variable "seed_admin_password" {
  type        = string
  description = "Initial admin account password seeded on first boot."
  sensitive   = true

  validation {
    condition = (
      length(var.seed_admin_password) >= 10 &&
      can(regex("[A-Z]", var.seed_admin_password)) &&
      can(regex("[0-9]", var.seed_admin_password)) &&
      can(regex("[^A-Za-z0-9]", var.seed_admin_password))
    )
    error_message = "seed_admin_password must be >= 10 characters and include an uppercase letter, a digit, and a special character."
  }
}
