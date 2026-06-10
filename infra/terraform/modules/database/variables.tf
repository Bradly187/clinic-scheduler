variable "name_prefix" {
  type        = string
  description = "Prefix for resource names."
}

variable "subnet_ids" {
  type        = list(string)
  description = "Private subnet IDs for the DB subnet group."
}

variable "db_security_group_id" {
  type        = string
  description = "Security group that permits Postgres from the app tier."
}

variable "db_name" {
  type        = string
  description = "Initial database name."
  default     = "clinic_scheduler"
}

variable "username" {
  type        = string
  description = "Master username."
  default     = "clinic_admin"
}

variable "password" {
  type        = string
  description = "Master password (provided by the root module; alphanumeric to keep the connection string simple)."
  sensitive   = true
}

variable "engine_version" {
  type        = string
  description = "PostgreSQL engine version (major-only is allowed, e.g. \"17\")."
  default     = "17"
}

variable "instance_class" {
  type        = string
  description = "RDS instance class."
  default     = "db.t4g.micro"
}

variable "allocated_storage" {
  type        = number
  description = "Initial storage in GiB."
  default     = 20
}

variable "max_allocated_storage" {
  type        = number
  description = "Storage autoscaling ceiling in GiB (0 disables autoscaling)."
  default     = 50
}

variable "multi_az" {
  type        = bool
  description = "Enable Multi-AZ for HA (roughly doubles DB cost)."
  default     = false
}

variable "backup_retention_period" {
  type        = number
  description = "Days of automated backups to retain."
  default     = 7
}

variable "deletion_protection" {
  type        = bool
  description = "Block accidental deletion of the DB instance."
  default     = true
}

variable "skip_final_snapshot" {
  type        = bool
  description = "Skip the final snapshot on destroy (set true only for sandbox teardown)."
  default     = false
}

variable "tags" {
  type        = map(string)
  description = "Tags applied to all resources."
  default     = {}
}
