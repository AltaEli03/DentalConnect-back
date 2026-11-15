# Guía de revisión y aceptación de DentalConnect

Esta guía separa evidencia automatizada de comprobaciones que requieren AWS, el navegador publicado o cuentas de prueba. No marcar una prueba manual como exitosa sin ejecutarla y anotar el resultado.

## Repositorios y ramas

- Frontend: https://github.com/AltaEli03/DentalConnect-front
- Backend: https://github.com/AltaEli03/DentalConnect-back
- Integración del backend: `develop`; cambios nuevos mediante ramas `feature/*` y PR.
- Despliegue de backend: `.github/workflows/deploy-aws.yml` publica en ECS únicamente cuando se integra un push a `main`; antes ejecuta build .NET, xUnit, validación de Terraform, ejecución seed y health check HTTPS.

## Validación automatizada registrada

| Área | Evidencia | Estado comprobado |
| --- | --- | --- |
| Backend xUnit/PostgreSQL y cobertura | [Actions 43735597335](https://github.com/AltaEli03/DentalConnect-back/actions/runs/43735597335) | Éxito en `develop`, commit `4b9267b` |
| Migraciones aplicar/revertir/reaplicar | [Actions 43735597231](https://github.com/AltaEli03/DentalConnect-back/actions/runs/43735597231) | Éxito en PostgreSQL efímero; no es restauración RDS |
| Análisis estático backend | [CodeQL 43735597431](https://github.com/AltaEli03/DentalConnect-back/actions/runs/43735597431) | Ejecución exitosa; consultar Security / Code scanning para hallazgos |
| Frontend lint, 91 pruebas, 23 pares de contraste y build PWA | PR [#25](https://github.com/AltaEli03/DentalConnect-front/pull/25) | CI aprobado e integrado a `develop` |

Los resultados cambian con cada commit. Consultar el SHA y la conclusión del run antes de una demostración.

## Prueba manual antes de la revisión

Completar en el sitio HTTPS actual y adjuntar capturas sin mostrar secretos ni datos personales reales:

1. Abrir el listado de consultorios y entrar a un detalle; confirmar que el API responde.
2. Iniciar como paciente verificado; revisar citas e historial propios.
3. Iniciar como usuario de consultorio; consultar solicitudes propias y probar acciones permitidas.
4. Intentar acceso cruzado con folio/ID de prueba ajeno y verificar una denegación sin datos.
5. Recorrer la interfaz con teclado, foco visible y enlace de salto; revisar etiquetas y mensajes con lector de pantalla.
6. Probar 360 px, 768 px, escritorio y zoom 200 %.
7. En AWS, verificar HTTPS del health endpoint, variables de GitHub Actions y secretos en Secrets Manager.
8. Para TEC-11, realizar recuperación a un entorno de prueba con datos ficticios. No restaurar sobre producción.

## Registro de ejecución manual

| Fecha/hora | URL y commit publicado | Casos aprobados | Defectos / responsable |
| --- | --- | --- | --- |
| Pendiente | Pendiente de validación del despliegue AWS | Pendiente | Pendiente |

## Incidencias conocidas y siguientes acciones

- La sesión de AWS estaba expirada en la inspección del 15-Nov-2026; no se pudo verificar CloudWatch, ECS, health endpoint ni la restauración de respaldo en esa sesión.
- Iniciar sesión en AWS y ejecutar los puntos 7 y 8 con la configuración existente; no crear recursos hasta confirmar costo y límites de la cuenta.
- DENT-33 está en pruebas; falta llenar la matriz manual de accesibilidad y aislamiento de roles sobre el despliegue HTTPS.
