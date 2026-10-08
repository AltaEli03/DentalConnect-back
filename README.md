# DentalConnect-back

API de ejemplo para demostrar despliegue continuo con AWS Lambda.

## Endpoints

- `/`: información de la API.
- `/health`: estado de disponibilidad.

## Despliegue automático

Cada `push` a `main` ejecuta `.github/workflows/deploy-aws.yml`.
El flujo obtiene credenciales temporales por OIDC, publica `lambda_function.py`
en AWS Lambda y consulta `/health` para comprobar el resultado.

Variables de GitHub Actions: `BACKEND_FUNCTION_NAME`, `BACKEND_API_URL` y
`AWS_BACKEND_ROLE_ARN`. Son identificadores, no secretos. No se usan claves
de AWS permanentes ni se guardan cadenas de conexión en el repositorio.

Si el backend incorpora una base de datos o una API de terceros, sus secretos
deben configurarse como variables de entorno de Lambda o en AWS Secrets Manager,
nunca en este repositorio ni en el navegador.
