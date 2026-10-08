# Infraestructura de producción

Este directorio documenta el despliegue previsto: S3 y CloudFront para el frontend, ECR, ECS Fargate y ALB para la API, RDS PostgreSQL privado y SES/SMTP para el correo. La infraestructura no se aplica automáticamente desde CI: requiere secretos de AWS configurados por el responsable de la cuenta y una revisión de costos. Use variables de entorno o GitHub Secrets; nunca archivos `.tfvars` con claves.

Variables requeridas: `AWS_REGION`, `DATABASE_URL`, `JWT_SECRET`, `EMAIL_PROVIDER`, `EMAIL_FROM`, `SMTP_HOST`, `SMTP_PORT`, `SMTP_USER`, `SMTP_PASSWORD`, `FRONTEND_URL`.
