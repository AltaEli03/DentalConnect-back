---
name: empirical-verification
description: Medir antes de fijar versiones, umbrales, timeouts, nombres y formatos en DentalConnect; nunca inventarlos. Úsala al elegir una versión de paquete (NuGet o npm) o imagen, al definir un umbral de CI, o al documentar un número.
---

## Propósito
Que cada número o nombre de los docs y la configuración tenga una fuente medida y fechada.

## Cuándo usarla
Al agregar un paquete o una imagen, al fijar un timeout o un umbral, al escribir un contrato JSON o al afirmar que "funciona".

## Precondiciones
Acceso a la herramienta real (CLI, registro de paquetes, ejecución de pruebas).

## Pasos
1. **Ejecutar y capturar:** `dotnet --version`, la API de NuGet (`api.nuget.org/v3-flatcontainer/<id>/index.json`), `npm view <paquete> version` / `npm outdated`, los tags de MCR o Docker Hub, `git ls-remote` para los SHAs de las actions.
2. **Inspeccionar el formato real** (JSON del wire del endpoint, salida del comando) antes de escribir la prueba o el parser: el contrato camelCase del back lo da la respuesta serializada, no el nombre C# del DTO.
3. **Fijar el valor** con margen y citar la fuente y la fecha en la spec o en `docs/calidad/`.
4. **Confirmar que el cambio compiló** antes de leer los resultados: si el build falla, `--no-build` corre binarios viejos y el resultado es falso.
5. **Self-test** de los guardarraíles: romper a propósito y ver que fallen (p. ej. renunciar una fila de `docs/base-datos/migraciones.md` y ver el ciclo de migraciones protestar).

## Checklist
Versiones verificadas en su registro · umbrales con su medición · nombres reales (los de la HU vienen de Jira por Atlassian MCP) · self-test hecho.

## Criterios de done
Cada número de los docs cita su fuente y su fecha (p. ej. "xUnit 2.9.3 + Test.Sdk 17.14.1 medidos en DentalConnect.Tests.csproj, 09-Nov-2026").

## Límites/trampas
- No asumir la última versión "de memoria": consultar el registro.
- No asumir que existen `curl` o `wget` dentro de la imagen `aspnet`.
- En Git Bash, `/p:` de `dotnet` se convierte en una ruta: usar `-p:`.
- `status 0` no implica OK: comparar con un grupo de control.

## Referencias
`DentalConnect-back/*.csproj`, `DentalConnect-front/package.json`, `docs/calidad/`, `docs/base-datos/migraciones.md`.
