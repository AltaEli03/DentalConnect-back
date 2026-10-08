terraform {
  required_version = ">= 1.6"
  required_providers { aws = { source = "hashicorp/aws", version = "~> 5.0" } }
}

provider "aws" { region = var.aws_region }

# The account owner supplies networking, database credentials and approved cost limits.
# This module intentionally has no apply-time defaults for paid services.
variable "aws_region" { type = string, default = "us-east-1" }
variable "database_url" { type = string, sensitive = true }
variable "jwt_secret" { type = string, sensitive = true }
variable "frontend_url" { type = string }

resource "aws_ecr_repository" "api" { name = "dentalconnect-api" force_delete = false }

output "ecr_repository_url" { value = aws_ecr_repository.api.repository_url }
