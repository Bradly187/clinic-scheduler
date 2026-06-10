output "deploy_role_arn" {
  description = "Role ARN for GitHub Actions to assume (set as the AWS_DEPLOY_ROLE_ARN secret)."
  value       = aws_iam_role.deploy.arn
}

output "oidc_provider_arn" {
  value = local.oidc_provider_arn
}
