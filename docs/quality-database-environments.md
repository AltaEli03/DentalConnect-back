# Calidad, ambientes y versionamiento de datos

## Arquitectura y persistencia

El backend usa ASP.NET Core Web API sobre .NET 10, con proyectos independientes para API, Application, Domain e Infrastructure, además de DentalConnect.Tests. La persistencia relacional es PostgreSQL mediante EF Core/Npgsql. Las migraciones fuente viven en DentalConnect.Infrastructure/Persistence/Migrations y su historial se conserva en Git. Las credenciales de producción se inyectan desde AWS Secrets Manager; los workflows de CI usan una instancia efímera local de PostgreSQL y valores de prueba no reutilizables.

## Ambientes

| Ambiente | Configuración | Uso |
| --- | --- | --- |
| Development | appsettings.Development.json / variables de entorno; PostgreSQL local opcional | Desarrollo, sin secretos de producción |
| Test/CI | GitHub Actions, PostgreSQL 16 efímero, variables de prueba | Pruebas xUnit, integración y ciclo de migración |
| Production | Variables de GitHub Actions para identificadores no secretos; AWS Secrets Manager para credenciales DB; tareas ECS | Despliegue AWS; no se guardan claves en Git |

Los valores específicos no secretos de despliegue se configuran en GitHub Actions Variables. Las contraseñas permanecen en Secrets Manager. Las cadenas de conexión de pruebas solo apuntan al servicio PostgreSQL efímero dentro del runner.

## Calidad automatizada

Se seleccionó GitHub CodeQL para análisis estático de seguridad C# porque se integra con GitHub Actions y publica resultados SARIF en Security / Code scanning. El workflow .github/workflows/codeql.yml ejecuta consultas security-extended al modificar main o Sprint-7-Emanuel, en PR hacia main y semanalmente. El build y xUnit se ejecutan en .github/workflows/sprint5-tests.yml, que además conserva cobertura Cobertura y resumen como artefacto por 90 días.

El análisis CodeQL debe revisarse en cada ejecución: documentar alertas nuevas, severidad, falsos positivos justificados y correcciones; no se declaran métricas hasta que GitHub termine el análisis. El primer escaneo de la rama constituye la línea base registrada en el run de Actions; los siguientes permiten comparar hallazgos. ESLint y Vitest se mantienen como controles del frontend en su repositorio.

## Migraciones y reversión

La herramienta de versionamiento elegida es EF Core Migrations, compatible con Npgsql y versionada en el mismo repositorio que el modelo. El workflow .github/workflows/database-migrations.yml utiliza una base PostgreSQL 16 aislada y, antes de aceptar los cambios, aplica todas las migraciones, retrocede una migración y vuelve a aplicarla. La ejecución publica el resultado en el resumen de GitHub Actions y adjunta evidencia; no conecta con RDS ni modifica datos de producción.

Para crear migraciones nuevas, generar los archivos con dotnet-ef, revisar tanto Up como Down y confirmar el resultado del flujo de ciclo de vida antes de integrar. No modificar el esquema manualmente en producción.

## Pruebas y cobertura

DentalConnect.Tests es un proyecto independiente con xUnit. GitHub Actions ejecuta restauración, build Release y pruebas con PostgreSQL para escenarios que requieren integridad/concurrencia. La cobertura de líneas y ramas se produce en formato Cobertura; el porcentaje y los archivos se consultan en el artefacto backend-test-coverage del run correspondiente. La cobertura no sustituye pruebas de reglas de negocio; las brechas deben convertirse en casos de prueba y registrarse en el PR.

## Evidencia que conserva GitHub

- CodeQL SARIF: artefacto codeql-backend-<commit> y Security / Code scanning.
- Pruebas y cobertura: artefacto backend-test-coverage.
- Aplicación, reversión y reaplicación: resumen y artefacto database-migration-lifecycle.
- Historial de migraciones: archivos versionados en DentalConnect.Infrastructure/Persistence/Migrations.

Los artefactos de evidencia se conservan 90 días. Para la revisión académica, guardar el enlace al run, su estado, métricas reales de cobertura y conteo/severidad de alertas al terminar cada ejecución.
