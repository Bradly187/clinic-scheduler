# ClinicScheduler — Infrastructure as Code (Terraform)

Reproducible AWS infrastructure for the ClinicScheduler app. Replaces the
hand-launched EC2 + docker-compose setup ([../../DEPLOYMENT_NOTES.md](../../DEPLOYMENT_NOTES.md))
with a codified, teardownable stack.

## Architecture

```
Internet
  └─ Application Load Balancer  (:80, or :443 when an ACM cert is supplied)
       └─ ECS Fargate service   (ClinicScheduler.Web container, :8080)
            └─ RDS PostgreSQL 17 (private subnets, encrypted)

Supporting: custom VPC (2 public + 2 private subnets) · ECR repo ·
CloudWatch Logs · Secrets Manager · least-privilege IAM task roles
```

The app reads its config the same way it does under docker-compose, so no app
code changes are required:

| Container env var | Source |
|---|---|
| `ASPNETCORE_ENVIRONMENT=Production` | task definition (plain env) |
| `ConnectionStrings__DefaultConnection` | Secrets Manager → built from the RDS endpoint + generated password |
| `SeedAdmin__Password` | Secrets Manager → your `seed_admin_password` |

Migrations and seeding run automatically on container startup (existing
`db.Database.Migrate()` / `DatabaseSeeder` behaviour).

## Layout

```
infra/terraform/
├── modules/
│   ├── network/    VPC, subnets, IGW, routes, ALB/app/db security groups
│   ├── ecr/        container image repository + lifecycle policy
│   ├── database/   RDS PostgreSQL + subnet group
│   └── ecs/        ALB, target group, listeners, cluster, task def, service, IAM, logs
└── environments/
    └── prod/       wires the modules; owns secrets + the random DB password
```

## Prerequisites

- Terraform >= 1.5
- AWS CLI configured with credentials (`aws sts get-caller-identity` works)
- Docker (to build & push the image)

## Usage

```bash
cd infra/terraform/environments/prod

# 1. Provide the one required secret
cp terraform.tfvars.example terraform.tfvars
#   edit terraform.tfvars -> set a strong seed_admin_password

# 2. Initialise + review
terraform init
terraform plan

# 3. Create the infra (VPC, RDS, ECR, ALB, ECS service...)
terraform apply

# 4. Build & push the image to the new ECR repo
ECR_URL=$(terraform output -raw ecr_repository_url)
REGION=$(terraform output -raw region)
aws ecr get-login-password --region "$REGION" \
  | docker login --username AWS --password-stdin "${ECR_URL%/*}"
docker build -t "$ECR_URL:latest" ../../../..          # repo root has the Dockerfile
docker push "$ECR_URL:latest"

# 5. Roll the service onto the new image
aws ecs update-service --cluster $(terraform output -raw ecs_cluster_name) \
  --service $(terraform output -raw ecs_service_name) \
  --force-new-deployment --region "$REGION"

# 6. Open the app
terraform output app_url
```

> **First apply:** the ECS service is created before any image exists in ECR, so
> tasks won't become healthy until step 4–5. That's expected — push the image,
> force a new deployment, and the service stabilises.

## HTTPS

Plain HTTP on `:80` by default (matches the app, which expects the LB to handle
TLS and skips its own HTTPS redirect in Production). To enable HTTPS, request an
ACM certificate **in the same region** for your domain, set
`acm_certificate_arn` in `terraform.tfvars`, re-apply, then point a DNS record at
`alb_dns_name`. The HTTP listener then 301-redirects to HTTPS automatically.

## Cost & teardown

Rough us-east-1 cost while running: ALB ~$16–18/mo + Fargate (0.25 vCPU) ~$9/mo +
RDS db.t4g.micro ~$12–13/mo + storage ≈ **~$40–50/mo**. No NAT gateway by design.

Tear it all down when you're not demoing:

```bash
terraform destroy
```

The database now defaults to production-safe settings: `db_deletion_protection = true`
and `db_skip_final_snapshot = false` (a final snapshot named `<prefix>-db-final` is
taken on destroy). For a sandbox you want to tear down cleanly, set both to their
permissive values in `terraform.tfvars`:

```hcl
db_deletion_protection = false
db_skip_final_snapshot = true
```

`recovery_window_in_days = 0` on the secrets still allows immediate re-creation.

The ALB health check now targets the app's `/health` endpoint (includes a database
connectivity check). When `acm_certificate_arn` is set, the container also receives
`Security__RequireHttps=true`, which turns on always-secure auth cookies in the app.

## CI/CD (GitHub Actions)

`.github/workflows/deploy.yml` builds, tests, and deploys on every push to `MVP`:

```
push to MVP
  ├─ build-and-test   dotnet restore/build + unit + integration tests
  └─ deploy           (only on Bradly187/clinic-scheduler @ MVP)
        ├─ assume AWS role via GitHub OIDC  (no stored access keys)
        ├─ docker build + push  ->  ECR  (tags: <git-sha> and latest)
        ├─ describe current task def -> render with the new image
        └─ deploy to ECS + wait for the service to stabilise
```

Auth is via the **`github_oidc` Terraform module**, which creates a scoped IAM
role the workflow federates into — no long-lived AWS keys in GitHub. The role can
only push to this project's ECR repo and roll this one ECS service.

**One-time setup after `terraform apply`:**

```bash
# In repo Settings -> Secrets and variables -> Actions, add a secret:
#   AWS_DEPLOY_ROLE_ARN = <output below>
terraform output -raw github_deploy_role_arn
```

That's the only GitHub secret required. The Terraform `aws_ecs_service` sets
`ignore_changes = [task_definition]` so the pipeline can roll new revisions
without Terraform reverting them on the next apply.

> One GitHub OIDC provider is allowed per AWS account. If you already have one,
> set `create_github_oidc_provider = false` and pass its ARN via
> `existing_github_oidc_provider_arn`.

## Remote state (recommended next step)

State is local by default so this runs out of the box. For team use / safety,
move it to S3 + DynamoDB locking:

1. Create an S3 bucket (versioned) and a DynamoDB table with a `LockID` (string)
   partition key.
2. Uncomment the `backend "s3"` block in `versions.tf` and fill in the names.
3. `terraform init -migrate-state`.

## Notes / future hardening

- Tasks run in public subnets (with locked-down SGs) to avoid NAT cost. For a
  stricter posture, move them to private subnets + a NAT gateway or VPC
  endpoints for ECR/CloudWatch/Secrets Manager.
- Consider a dedicated `/health` endpoint in the app for a tighter ALB health
  check than `/`.
- `containerInsights` is disabled to save cost; enable in `modules/ecs` for
  richer metrics.
