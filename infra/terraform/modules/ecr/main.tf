###############################################################################
# ECR module: container image repository for the clinic scheduler web app.
###############################################################################

resource "aws_ecr_repository" "this" {
  name                 = var.repository_name
  image_tag_mutability = "MUTABLE"
  force_delete         = true # allow `terraform destroy` even with images present

  image_scanning_configuration {
    scan_on_push = true
  }

  tags = merge(var.tags, { Name = var.repository_name })
}

resource "aws_ecr_lifecycle_policy" "this" {
  repository = aws_ecr_repository.this.name

  policy = jsonencode({
    rules = [
      {
        rulePriority = 1
        description  = "Keep only the last ${var.keep_last_images} images"
        selection = {
          tagStatus   = "any"
          countType   = "imageCountMoreThan"
          countNumber = var.keep_last_images
        }
        action = { type = "expire" }
      }
    ]
  })
}
