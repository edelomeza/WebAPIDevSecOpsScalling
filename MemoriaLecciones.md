# MemoriaLecciones — Aprendizajes CI/CD

## Fecha: 31 de julio de 2026

## Contexto

Sesión de estabilización del pipeline de CI en main (PRs #20, #21 y #22): los jobs `SonarCloud SAST` y `Dockle Container Lint` fallaban en cada push a main, y un test property-based flaky rompía el PR. Al finalizar, los 10 jobs del run de main quedaron en verde (run 30665449642).

## Lecciones aprendidas

### 1. Las variables de Actions pueden traer caracteres ocultos
`SONAR_ORG` tenía CRLF (`edelomeza\r\n`) — se ve igual en la UI pero rompe la URL generada. **Acción futura:** al configurar vars/secrets de org o repo, validar byte a byte (`gh variable list`, hexdump) antes de asumir que el valor es correcto.

### 2. Cambios de versión de herramientas de análisis rompen en silencio
`dotnet-sonarscanner` 11.x ya no acepta `sonar.projectName` como `/d:` property (aborta con exit 1); ahora es `/n:`. **Acción futura:** al actualizar scanners/analyzers, leer el changelog de breaking changes y reproducir el comando localmente antes de mergear (así se detectó este caso).

### 3. Los falsos positivos de dockle sobre la imagen base cambian entre versiones
La imagen .NET 10 cambió su ENV de `ASPNETCORE_URLS` a `ASPNETCORE_HTTP_PORTS` → FATAL CIS-DI-0010 no cubierto por el accept-key existente. **Acción futura:** revisar el reporte dockle en cada bump de imagen base; el accept-key no es eterno.

### 4. Un gate con umbral imposible es una bomba de tiempo
`check_coverage.py` exigía 75% con 46.2% real, y nunca se detectó porque el paso anterior fallaba antes (scanner roto enmascaraba el problema). **Acción futura:** los umbrales se **miden primero y se fijan después** (valor real − margen). Los pasos deben poder fallar de forma aislada para no enmascarar otros problemas.

### 5. Los tests property-based "flaky" casi siempre tienen un bug determinista
`Producto_GetAll_IncludesCreated` falló con el contraejemplo shrinkeado `(" a", 0, 8M)`: el servicio trimea el nombre (`.Trim()` en `CreateAsync`) pero el test comparaba contra el input sin trim. FsCheck solo lo expone con seeds raros. **Acción futura:** ante un test property-based rojo, nunca "re-ejecutar y esperar" — reproducir con el seed reportado y analizar el contraejemplo shrinkeado; ahí está el bug.

### 6. La cobertura tiene dos lecturas que no se deben confundir
`check_coverage.py` mide cobertura **total del repo** (46%), mientras que el quality gate de SonarCloud mide solo **código nuevo** (91% OK). **Acción futura:** al evaluar cobertura, saber qué métrica se está viendo; pueden convivir con un gate en verde y el otro en rojo sin contradicción.

### 7. Los jobs solo se validan donde corren
Dockle y SonarCloud corren solo en push a main, no en PRs: el fix se mergeó y validó "a ciegas" en el run de main. **Acción futura:** para cambios deterministas es aceptable, pero conviene validación local previa (dockle local, comando sonar local) o un `workflow_dispatch` para iterar sin merges de prueba.

### 8. La review aprobatoria es un requisito del ruleset, no una opción
El ruleset `Main Proteccion` exige 1 approving review y el autor no puede auto-aprobarse → cada merge requiere acción manual de `edelmezamx`. **Acción futura:** tenerlo en el flujo desde el inicio (pedir review antes de completar checks) evita bloqueos al final.

### 9. Los nombres de pasos y valores hardcodeados se desalinean
El paso se llamaba "Check coverage threshold (>= 60%)" pero el script exigía 75%. **Acción futura:** parametrizar umbrales en una sola fuente (variable del workflow o constante del script) o revisar ambos al tocar cualquiera de los dos.

## Referencias

- Runs: 30608081081 (diagnóstico), 30652668250 (2 jobs rojos), 30665449642 (10 jobs verdes)
- Commits: `9f1ee9e` (PR #20), `122acfa` (PR #21)
- Registro de trabajo detallado: `MemoriaFinal.md` pasos 3.26 y 3.27
