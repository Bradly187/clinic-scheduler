provider "aws" {
  region = var.region
}

# Two AZs derived from the selected region keep the stack region-portable.
data "aws_availability_zones" "available" {
  state = "available"
}

locals {
  name_prefix = "${var.project_name}-${var.environment}"

  azs = slice(data.aws_availability_zones.available.names, 0, 2)

  common_tags = {
    Project     = var.project_name
    Environment = var.environment
    ManagedBy   = "terraform"
  }

  # Image to run: explicit override if provided, else :latest in our ECR repo.
  container_image = var.container_image != "" ? var.container_image : "${module.ecr.repository_url}:latest"

  # Npgsql connection string. Password is alphanumeric (see random_password
  # below) so no quoting/escaping is required. SSL is required to RDS.
  connection_string = join(";", [
    "Host=${module.database.address}",
    "Port=${module.database.port}",
    "Database=${var.db_name}",
    "Username=${var.db_username}",
    "Password=${random_password.db.result}",
    "SSL Mode=Require",
    "Trust Server Certificate=true",
  ])
}
