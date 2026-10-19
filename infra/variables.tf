variable "aws_region" {
  type    = string
  default = "us-east-1"
}

variable "project_name" {
  type    = string
  default = "dentalconnect"
}

variable "frontend_origin" {
  type = string
}

variable "cognito_user_pool_id" {
  type = string
}

variable "cognito_client_id" {
  type = string
}

variable "db_instance_class" {
  type    = string
  default = "db.t3.micro"
}

variable "github_repository" {
  type        = string
  description = "GitHub owner/repository trusted to deploy from main."
  default     = "AltaEli03/DentalConnect-back"
}

variable "bootstrap_image" {
  type    = string
  # ECS service starts at desired_count=0; the first successful CI deployment
  # replaces this placeholder with the app image before starting a task.
  default = "public.ecr.aws/docker/library/nginx:alpine"
}
