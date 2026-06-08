###############################################################################
# Root module for the "prod" environment. Wires the network, database, ECR and
# ECS modules together and stores runtime secrets in Secrets Manager.
###############################################################################

# Random, alphanumeric master password for RDS. Alphanumeric avoids any chars
# that would need escaping inside the Npgsql connection string or that RDS
# rejects (/, @, ", space).
data "aws_caller_identity" "current" {}

resource "random_password" "db" {
  length  = 32
  special = false
}

module "network" {
  source = "../../modules/network"

  name_prefix          = local.name_prefix
  vpc_cidr             = var.vpc_cidr
  azs                  = local.azs
  public_subnet_cidrs  = var.public_subnet_cidrs
  private_subnet_cidrs = var.private_subnet_cidrs
  container_port       = 8080
  alb_ingress_cidrs    = var.alb_ingress_cidrs
  tags                 = local.common_tags
}

module "ecr" {
  source = "../../modules/ecr"

  repository_name = local.name_prefix
  tags            = local.common_tags
}

module "database" {
  source = "../../modules/database"

  name_prefix          = local.name_prefix
  subnet_ids           = module.network.private_subnet_ids
  db_security_group_id = module.network.db_security_group_id
  db_name              = var.db_name
  username             = var.db_username
  password             = random_password.db.result
  engine_version       = var.db_engine_version
  instance_class       = var.db_instance_class
  allocated_storage    = var.db_allocated_storage
  multi_az             = var.db_multi_az
  tags                 = local.common_tags
}

# --- Secrets -----------------------------------------------------------------
# recovery_window_in_days = 0 lets `terraform destroy` remove secrets
# immediately (otherwise the names are reserved for 7-30 days).
resource "aws_secretsmanager_secret" "connection_string" {
  name                    = "${local.name_prefix}/connection-string"
  description             = "Npgsql connection string for the clinic scheduler app"
  recovery_window_in_days = 0
  tags                    = local.common_tags
}

resource "aws_secretsmanager_secret_version" "connection_string" {
  secret_id     = aws_secretsmanager_secret.connection_string.id
  secret_string = local.connection_string
}

resource "aws_secretsmanager_secret" "seed_admin_password" {
  name                    = "${local.name_prefix}/seed-admin-password"
  description             = "Seed admin account password for the clinic scheduler app"
  recovery_window_in_days = 0
  tags                    = local.common_tags
}

resource "aws_secretsmanager_secret_version" "seed_admin_password" {
  secret_id     = aws_secretsmanager_secret.seed_admin_password.id
  secret_string = var.seed_admin_password
}

module "ecs" {
  source = "../../modules/ecs"

  name_prefix           = local.name_prefix
  region                = var.region
  vpc_id                = module.network.vpc_id
  public_subnet_ids     = module.network.public_subnet_ids
  alb_security_group_id = module.network.alb_security_group_id
  app_security_group_id = module.network.app_security_group_id

  container_image        = local.container_image
  container_port         = 8080
  cpu                    = var.container_cpu
  memory                 = var.container_memory
  desired_count          = var.desired_count
  aspnetcore_environment = var.aspnetcore_environment
  health_check_path      = var.health_check_path
  acm_certificate_arn    = var.acm_certificate_arn

  connection_string_secret_arn   = aws_secretsmanager_secret.connection_string.arn
  seed_admin_password_secret_arn = aws_secretsmanager_secret.seed_admin_password.arn
  secret_arns = [
    aws_secretsmanager_secret.connection_string.arn,
    aws_secretsmanager_secret.seed_admin_password.arn,
  ]

  tags = local.common_tags
}

# --- CI/CD: GitHub Actions OIDC deploy role ----------------------------------
module "github_oidc" {
  count  = var.enable_github_oidc ? 1 : 0
  source = "../../modules/github_oidc"

  name_prefix                = local.name_prefix
  github_owner               = var.github_owner
  github_repo                = var.github_repo
  allowed_branches           = var.deploy_branches
  create_oidc_provider       = var.create_github_oidc_provider
  existing_oidc_provider_arn = var.existing_github_oidc_provider_arn

  region             = var.region
  account_id         = data.aws_caller_identity.current.account_id
  ecr_repository_arn = module.ecr.repository_arn
  ecs_cluster_name   = module.ecs.cluster_name
  ecs_service_name   = module.ecs.service_name
  execution_role_arn = module.ecs.execution_role_arn
  task_role_arn      = module.ecs.task_role_arn

  tags = local.common_tags
}
