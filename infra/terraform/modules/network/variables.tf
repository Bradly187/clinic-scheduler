variable "name_prefix" {
  type        = string
  description = "Prefix for resource names, e.g. clinic-scheduler-prod."
}

variable "vpc_cidr" {
  type        = string
  description = "CIDR block for the VPC."
  default     = "10.20.0.0/16"
}

variable "azs" {
  type        = list(string)
  description = "Availability zones to spread subnets across (one per subnet index)."
}

variable "public_subnet_cidrs" {
  type        = list(string)
  description = "CIDR blocks for the public subnets (ALB + Fargate tasks)."
}

variable "private_subnet_cidrs" {
  type        = list(string)
  description = "CIDR blocks for the private subnets (RDS)."
}

variable "container_port" {
  type        = number
  description = "Port the app container listens on (used for the app security group)."
  default     = 8080
}

variable "alb_ingress_cidrs" {
  type        = list(string)
  description = "CIDRs allowed to reach the ALB on 80/443."
  default     = ["0.0.0.0/0"]
}

variable "tags" {
  type        = map(string)
  description = "Tags applied to all resources."
  default     = {}
}
