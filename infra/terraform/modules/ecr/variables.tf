variable "repository_name" {
  type        = string
  description = "Name of the ECR repository."
}

variable "keep_last_images" {
  type        = number
  description = "How many images to retain before the lifecycle policy expires older ones."
  default     = 10
}

variable "tags" {
  type        = map(string)
  description = "Tags applied to all resources."
  default     = {}
}
