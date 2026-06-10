terraform {
  required_version = ">= 1.5.0"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.60"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
  }

  # Remote state (recommended once you're past local experimentation). Create
  # the bucket + lock table first, then uncomment and run `terraform init`.
  # See README.md → "Remote state".
  #
  # backend "s3" {
  #   bucket         = "clinic-scheduler-tfstate-<your-account-id>"
  #   key            = "prod/terraform.tfstate"
  #   region         = "us-east-1"
  #   dynamodb_table = "clinic-scheduler-tflock"
  #   encrypt        = true
  # }
}
