variable "aws_region" { type = string, default = "us-east-1" }
variable "project_name" { type = string, default = "dentalconnect" }
variable "frontend_origin" { type = string }
variable "database_url_secret_arn" { type = string, sensitive = true }
variable "db_instance_class" { type = string, default = "db.t3.micro" }
variable "container_image" { type = string, default = "public.ecr.aws/docker/library/node:22-alpine" }

