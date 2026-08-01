# Análisis de la rama `Fase4` — Conocimiento adquirido y lecciones para el futuro

Este documento registra el análisis detallado de lo realizado en la rama `Fase4` (Fase 4 "Validación Profunda", 20/20 pasos de `MemoriaFinal.md`): qué se hizo, qué conocimiento no se tenía antes y qué reglas aplicar en el futuro para evitar repetir los mismos problemas.

## 1. Qué se hizo en la rama

### Commiteado (5 commits, pasos 4.1–4.5)
- Infraestructura de mutation testing: proyecto `MutationTest` (Stryker.NET 4.16.0), tool manifest, `stryker-config.json` (thresholds 80/70/60 + exclusiones), job CI `mutation-test` (push a main, reporte HTML), línea base 66.03% (2497 mutantes) → mejorado a **80.70%** con 107 tests nuevos (Wave 1: lógica de negocio/middleware + Wave 2: CRUD ventas/saga), kill-list de 567 mutantes, timeout CI 30→180 min, límites InMemory documentados.

### Sin commitear (pasos 4.6–4.20)
- **4.6–4.11 Performance**: proyecto `PerformanceTest` (NBomber) — 4 escenarios (Login, Mixto, Producto, Venta), `AuthHelper`, orchestrator en `Program.cs` con umbrales y exit codes (0 PASS / 1 thresholds / 2 suite rota).
- **4.12–4.15 Contratos Pact**: proyecto `ContractTest` — 4 pacts (login, productos, saga, ventas), `ProviderTests.cs` (verificación contra proceso real), endpoint `/provider-states` en `Program.cs` (gated por `EnableProviderStates`), job CI `contract-test` (con `continue-on-error: true`).
- **4.16–4.19 Métricas**: `Services/QualityMetricsService.cs` (Meter `WebAPIDevSecOps.QualityMetrics`, 4 gauges: test_coverage_percent, mutation_score, sonar_quality_gate_passed, p95_latency_ms), endpoint `/metrics` Prometheus, dashboard Grafana `deploy/grafana/quality-dashboard.json` (12 paneles), script `scripts/collect-quality-metrics.sh` (SonarCloud API + auto-detección de reportes Stryker/NBomber → `quality-metrics.env`).
- **4.20 Auditoría**: `Middleware/AuditHashChain.cs` (cadena de hashes SHA256 estática), integración en `AuditLoggingMiddleware`, `AuditLogEntry` +`PrevHash`/`Hash`, 9 tests de integridad/tampering.

### Housekeeping pendiente detectado
- `reports/` sin ignorar (contiene `reports/nbomber-log-2026080109.txt`, artefacto de corrida local).
- `MutationTest/StrykerOutput/` se auto-ignora con `.gitignore` anidado por corrida (6 runs) — funciona pero es frágil; conviene red de seguridad en el `.gitignore` raíz.
- Trabajo 4.6–4.20 sin commitear (commits temáticos propuestos: perf / contratos / métricas / auditoría).

## 2. Conocimiento obtenido que no se tenía (con evidencia)

### Mutation testing (4.1–4.5)
- **Cobertura ≠ detección**: 46% de cobertura de línea no predecía el mutation score (66%). El mutation score es el gate real de calidad de tests.
- **Stryker**: `project` acepta nombre de archivo, no ruta; los globs de `mutate` se resuelven **relativos al proyecto mutado, no al config** (`"**"` + `!**/Migrations/**`; con `../` todos los mutantes quedaban "Removed by mutate filter").
- **Timeout real**: el run tarda ~2h30–2h45 (2497 mutantes, 975 tests); un timeout de 30 min siempre habría abortado → 180 min.
- **Safe mode** genera CompileError (81) esperados → deben excluirse del denominador.
- **Fórmula del score**: `(Killed+Timeout)/(Killed+Timeout+Survived+NoCoverage)` — excluye CompileError, filtra Ignored. El JSON real de Stryker NO trae `mutationScore` raíz (verificado en 4.19: 1133/1404 = 80.70% exacto).
- **Límites InMemory**: hay mutantes inmatables por diseño (Include=INNER JOIN, `RandomNumberGenerator.GetInt32` no inyectable, `SaveChangesAsync` no-op) → documentarlos como supervivientes aceptados.
- **FsCheck genera espacios en bordes** → tests flaky de CI comparaban sin `.Trim()`.

### Performance (4.6–4.11)
- **NBomber 6.5 no soporta pesos por step** → selección probabilística por iteración con `RandomNumberGenerator` (cumple CA5394).
- **Los rate limits de la app saturan las suites de carga** (LoginPolicy 5/5min, AdminPolicy 200/min, ConcurrentWritesPolicy 10) → el entorno perf exige políticas relajadas o los 429 rompen los umbrales de error.
- Login único en `WithInit` + JWT compartido para no disparar Argon2id en ráfaga.
- Exit codes limpios (0/1/2) para poder gatear en CI.

### Contract testing (4.12–4.15)
- PactNet 4.x+: el verifier vive en el paquete core (`PactNet.Verifier`); `PactNet.Provider.xUnit` no existe.
- **`WebApplicationFactory` fuerza TestServer** (ignora `UseKestrel`/`UseUrls`) y el FFI de Pact necesita HTTP real → se lanza la API como **proceso real** (`dotnet WebAPIDevSecOps.dll`) en puerto libre con env vars, espera de `/health` y kill del árbol en `finally`.
- **pactSpecification v2 descarta `matchingRules`** (compara literales) → migrar a **3.0.0 con reglas planas** `{"match": "type"}` (el wrapper v3 `{"matchers": [...]}` falla con "Could not parse matcher JSON").
- `RowVersion` `byte[]{1}` InMemory → base64 `"AQ=="`.

### Métricas/observabilidad (4.16–4.19)
- **`OpenTelemetry.Exporter.Prometheus.AspNetCore` no tiene NINGÚN release estable** (33/33 prerelease) → usar 1.17.0-beta.1 alineada con el core OTel 1.17.0.
- **DI perezoso**: el singleton de métricas nunca se resolvía → el Meter no existía → el scrape no traía los gauges. Hay que resolver eager en startup (`app.Services.GetRequiredService<...>()` tras `builder.Build()`).
- **El exporter Prometheus renombra las métricas** añadiendo sufijos de unidad: `mutation_score_percent`, `p95_latency_ms_milliseconds`, `sonar_quality_gate_passed` (sin sufijo), `test_coverage_percent` (percent sin duplicar). Los nombres del Meter NO son los expuestos → verificar empíricamente (`curl /metrics`) antes de escribir PromQL o dashboards.
- NBomber report.json: `nodeStats.scenarios[].ok.latency.percent95` (camelCase).
- Script robusto: `LC_NUMERIC=C` (la coma decimal del locale rompe `printf %.2f`), `mktemp`+`mv` (escritura atómica), fallback 0/false + WARN por fuente ausente.

### Auditoría (4.20)
- Hash chain estática (patrón `TokenBlacklist`) con `lock` + `Reset()` — el estado estático **cruza tests** si no se aísla en el constructor.

### Contexto CI heredado (fases previas, fixes 3.24–3.27)
- Los exporters OTel de consola rompían el testhost (1.1M líneas de log, carrera de shutdown) → gated detrás de config (`Observability:ConsoleExport`).
- dotnet-sonarscanner 11.2.1 aborta si `projectName` va como `/d:` → usar `/n:`.
- La imagen base .NET 10 cambió `ASPNETCORE_URLS` por `ASPNETCORE_HTTP_PORTS` (dockle).
- Umbral de cobertura que nunca se cumplía porque sonar fallaba antes (75% hardcodeado vs 46% real → 45%).
- CRLF en variables de GitHub Actions (SONAR_ORG con `\r\n`) rompe jobs.

## 3. Reglas para el futuro (evitar repetir problemas)

1. **Verificar empíricamente antes de escribir** — nombres de métricas, formatos de reporte, semántica de configs. Se asumió teoría 3 veces y las 3 falló (PromQL del dashboard, `mutationScore` inexistente, gauges ausentes del scrape).
2. **Medir tiempos reales antes de fijar timeouts de CI** (Stryker 30→180 min) y umbrales de cobertura contra números reales, no objetivos deseados.
3. **Aislar estado estático en tests** (Reset en constructor) y **resolver eager** cualquier singleton que deba registrarse en un provider global.
4. **Chequear el estado de release de los paquetes** antes de elegirlos (Prometheus.AspNetCore nunca estable) y documentar la excepción.
5. **Documentar límites conocidos** (mutantes inmatables, caveats de rate limits en perf) en vez de resolverlos con hacks.
6. **Escribir tests de frontera exactos** (límites `>=`/`<`, mensajes de excepción, ambas ramas del RNG con bucle determinista) — es lo que realmente mata mutantes.
7. **Entornos de prueba con herramientas que exigen socket real** (Pact FFI): proceso real + puerto libre + cleanup en `finally`.
8. **Scripts con fallback explícito y atomicidad** (0/false + WARN, `mktemp`/`mv`, `LC_NUMERIC=C`).
9. **Gitignore defensivo**: artefactos de corridas locales (`reports/`, `StrykerOutput/`) deben estar ignorados en el raíz, no depender de `.gitignore` anidados generados por herramientas.

## Estado de verificación final (Fase 4 completa)

- Build Release: 0 errores.
- Unit: 596/596 | Integration: 352/352 | Security: 136/136.
- MemoriaFinal.md: 76/97 pasos (78%), Fase 4 al 100% (20/20).
