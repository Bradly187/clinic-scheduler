output "app_url" {
  description = "Public URL of the application via the ALB."
  value       = var.acm_certificate_arn == "" ? "http://${module.ecs.alb_dns_name}" : "https://${module.ecs.alb_dns_name}"
}

output "alb_dns_name" {
  value = module.ecs.alb_dns_name
}

output "ecr_repository_url" {
  description = "Push images here (docker tag ... <this>:latest)."
  value       = module.ecr.repository_url
}

output "ecs_cluster_name" {
  value = module.ecs.cluster_name
}

output "ecs_service_name" {
  value = module.ecs.service_name
}

output "cloudwatch_log_group" {
  value = module.ecs.log_group_name
}

output "rds_address" {
  description = "RDS endpoint hostname (private)."
  value       = module.database.address
  sensitive   = true
}

output "region" {
  value = var.region
}

output "github_deploy_role_arn" {
  description = "Set this as the AWS_DEPLOY_ROLE_ARN secret in the GitHub repo."
  value       = var.enable_github_oidc ? module.github_oidc[0].deploy_role_arn : null
}
