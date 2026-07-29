# MemoriaMQA — Plan Integral de Calidad y QA
## WebAPIDevSecOps — Cobertura 100% ISO 25010 + OWASP ASVS L2

---

## 📋 CHECKLIST GLOBAL DE SEGUIMIENTO

| Fase | Nombre | Tareas | ✅ Completadas | 🔄 En Progreso | ⬜ Pendientes |
|------|--------|--------|---|---|---|
| **F0** | Diagnóstico inicial | 10 | ⬜ | ⬜ | ⬜ |
| **F1** | Fortalecer métricas existentes | 8 | ⬜ | ⬜ | ⬜ |
| **F2** | Mutation Testing (Stryker) | 5 | ⬜ | ⬜ | ⬜ |
| **F3** | Performance Testing (NBomber) | 6 | ⬜ | ⬜ | ⬜ |
| **F4** | DB Migration Testing | 5 | ⬜ | ⬜ | ⬜ |
| **F5** | Contract Testing (Pact) | 4 | ⬜ | ⬜ | ⬜ |
| **F6** | Concurrencia y Tolerancia a Fallos | 8 | ⬜ | ⬜ | ⬜ |
| **F7** | Hardening de Autenticación (2FA + Refresh) | 10 | ⬜ | ⬜ | ⬜ |
| **F8** | Chaos Engineering | 5 | ⬜ | ⬜ | ⬜ |
| **F9** | Propiedad de Acceso (Object-Level Auth) | 4 | ⬜ | ⬜ | ⬜ |
| **F10** | Observabilidad y Dashboard de Calidad | 6 | ⬜ | ⬜ | ⬜ |
| **F11** | PR Quality Gate + Checklist | 4 | ⬜ | ⬜ | ⬜ |
| **F12** | Pentesting Automatizado + Hardening Runtime | 6 | ⬜ | ⬜ | ⬜ |

**Total: ~81 tareas** | **⬜ 0%**

---

## FASE 0 — Diagnóstico del Estado Actual

**Objetivo:** Documentar la línea base actual para medir el progreso.

| # | Tarea | Descripción | Archivos | ✅ |
|---|---|---|---|---|
| 0.1 | Ejecutar tests actuales y registrar pass/fail | `dotnet test` en UnitTest, IntegrationTest, SecurityTest. Guardar output para comparativa futura | — | ⬜ |
| 0.2 | Medir cobertura actual | Ejecutar `dotnet test --collect:"XPlat Code Coverage"` con `coverage.runsettings`. Reportar % actual | `coverage.runsettings` | ⬜ |
| 0.3 | Correr Semgrep y contar reglas activas | `semgrep ci --config=auto --config=.semgrep/semgrep.yaml --metrics=off`. Contar hallazgos por severidad | `.semgrep/semgrep.yaml` | ⬜ |
| 0.4 | Verificar calidad SonarCloud | Revisar Quality Gate actual, deuda técnica, duplicación | SonarCloud dashboard | ⬜ |
| 0.5 | Listar dependencias desactualizadas | `dotnet list package --outdated` | — | ⬜ |
| 0.6 | Verificar estado de Docker y contenedores | `docker images`, listar vulnerabilidades Trivy actuales | `Dockerfile` | ⬜ |
| 0.7 | Revisar fuzzing RESTler | Ejecutar RESTler dry-run, verificar config | `fuzzing/restler_settings.json` | ⬜ |
| 0.8 | Documentar línea base de rendimiento | Sin tests de performance hoy — registrar que es 0 | — | ⬜ |
| 0.9 | Verificar health checks | Llamar `/health`, `/health/ready`, `/health-ui` | `Program.cs` | ⬜ |
| 0.10 | Publicar diagnóstico en MemoriaMQA.md | Agregar sección "Diagnóstico QA" con métricas actuales | `MemoriaMQA.md` | ⬜ |

---

## FASE 1 — Fortalecer Métricas Existentes

**Objetivo:** Subir umbrales y agregar métricas de mantenibilidad.

| # | Tarea | Descripción | Archivos | ✅ |
|---|---|---|---|---|
| 1.1 | Subir cobertura 30%→75% en script | En `check_coverage.py:23` cambiar `if pct < 30` → `if pct < 75`. Esto hará que el CI falle si el código actual no alcanza 75% | `scripts/check_coverage.py` | ⬜ |
| 1.2 | Agregar exclusiones en coverage.runsettings | Excluir `[WebAPIDevSecOps.Models]*`, `[WebAPIDevSecOps.Dto]*`, `[WebAPIDevSecOps.Migrations]*` para no penalizar código que no requiere tests | `coverage.runsettings` | ⬜ |
| 1.3 | Agregar SonarAnalyzer.CSharp al build | En `Directory.Build.props`, agregar package `SonarAnalyzer.CSharp` version 10.* con `PrivateAssets=all`. Esto corre análisis en cada build | `Directory.Build.props` | ⬜ |
| 1.4 | Configurar thresholds de complejidad y MI | Agregar propiedades en `Directory.Build.props` para `RunAnalyzersDuringBuild=true`, `EnforceCodeStyleInBuild=true`. Configurar en SonarCloud: complejidad ≤15, MI ≥60, duplicación <3% | `Directory.Build.props` + SonarCloud | ⬜ |
| 1.5 | Agregar 8 reglas nuevas de Semgrep | Implementar: `log-sensitive-data`, `sql-injection-raw`, `missing-cors-validation`, `disabled-rate-limit`, `exception-without-log`, `loop-logging`, `insecure-deserialization`, `hardcoded-cryptokeys` | `.semgrep/semgrep.yaml` | ⬜ |
| 1.6 | Subir exigencia cobertura código nuevo a 80% | Configurar en SonarCloud: `sonar.new.coverage.requirement=80` vía `sonar-project.properties` o UI | SonarCloud | ⬜ |
| 1.7 | Crear sonar-project.properties | Archivo de configuración SonarCloud para el proyecto: key, name, exclusions, thresholds | `sonar-project.properties` (nuevo) | ⬜ |
| 1.8 | Verificar que build falla si se violan thresholds | Prueba forzada: crear método con complejidad >15, verificar que build falla | — | ⬜ |

---

## FASE 2 — Mutation Testing con Stryker.NET

**Objetivo:** Validar que los tests realmente ejercen el código (detecta tests falsos positivos).

| # | Tarea | Descripción | Archivos | ✅ |
|---|---|---|---|---|
| 2.1 | Crear proyecto MutationTest | Nuevo proyecto `MutationTest/MutationTest.csproj` con target net10.0, referencia a `Stryker.NET` | `MutationTest/MutationTest.csproj` (nuevo) | ⬜ |
| 2.2 | Configurar stryker-config.json | Crear archivo de configuración: proyecto objetivo = `WebAPIDevSecOps.csproj`, test-projects = `UnitTest`, thresholds high=80, low=70, break=60. Excluir `Migrations/`, `Models/`, `Dto/`, `Program.cs` | `MutationTest/stryker-config.json` (nuevo) | ⬜ |
| 2.3 | Agregar Stryker al CI (no-PR) | Job `mutation-test` en CI, solo en push a main (no en PR). Timeout 30min. Publica reporte HTML como artifact | `.github/workflows/ci-cd.yml` | ⬜ |
| 2.4 | Ejecutar Stryker y medir línea base | `dotnet stryker --config-file MutationTest/stryker-config.json`. Reportar mutation score actual | — | ⬜ |
| 2.5 | Mejorar tests donde mutation score sea bajo | Identificar mutantes sobrevivientes y agregar tests que los maten. Repetir hasta score ≥70% | Archivos de test varios | ⬜ |

---

## FASE 3 — Performance Testing con NBomber

**Objetivo:** Establecer línea base de rendimiento y detectar regresiones.

| # | Tarea | Descripción | Archivos | ✅ |
|---|---|---|---|---|
| 3.1 | Crear proyecto PerformanceTest | Nuevo proyecto `PerformanceTest/PerformanceTest.csproj` con NBomber, NBomber.Http, OutputType Exe. Referencia a WebAPIDevSecOps | `PerformanceTest/PerformanceTest.csproj` (nuevo) | ⬜ |
| 3.2 | Escenario Login | Escenario NBomber: POST `/api/v1/login` con rampa 5→50 usuarios concurrentes, 2min. Umbral P95 < 500ms, error < 0.1% | `PerformanceTest/Scenarios/LoginScenario.cs` (nuevo) | ⬜ |
| 3.3 | Escenario Productos (GET) | Escenario NBomber: GET `/api/v1/producto` con 100 usuarios constantes, 2min. Umbral P95 < 200ms, error < 0.1% | `PerformanceTest/Scenarios/ProductoScenario.cs` (nuevo) | ⬜ |
| 3.4 | Escenario Venta (POST) | Escenario NBomber: POST `/api/v1/venta` con rampa 5→30 usuarios, 2min. Umbral P95 < 1000ms, error < 0.5% | `PerformanceTest/Scenarios/VentaScenario.cs` (nuevo) | ⬜ |
| 3.5 | Escenario Mixto (60% GET, 20% POST login, 20% POST venta) | Escenario combinado con 80 usuarios, 3min. Umbral promedio < 800ms, error < 0.5% | `PerformanceTest/Scenarios/MixtoScenario.cs` (nuevo) | ⬜ |
| 3.6 | Program.cs con thresholds y reporte | `PerformanceTest/Program.cs` que corre todos los escenarios, verifica thresholds, y genera reporte HTML | `PerformanceTest/Program.cs` (nuevo) | ⬜ |

---

## FASE 4 — Database Migration Testing con Testcontainers

**Objetivo:** Verificar que las migraciones funcionan contra SQL Server real (no InMemory).

| # | Tarea | Descripción | Archivos | ✅ |
|---|---|---|---|---|
| 4.1 | Crear proyecto DatabaseTest | Nuevo proyecto `DatabaseTest/DatabaseTest.csproj` con xUnit, FluentAssertions, Testcontainers.MsSql, EF Core SQL Server | `DatabaseTest/DatabaseTest.csproj` (nuevo) | ⬜ |
| 4.2 | Test: Todas las migraciones pueden aplicarse | Iniciar contenedor SQL Server, ejecutar `context.Database.MigrateAsync()`, verificar que existen 9 tablas | `DatabaseTest/MigrationTests.cs` (nuevo) | ⬜ |
| 4.3 | Test: Rollback de migraciones funciona | Aplicar migraciones, hacer rollback a "0", verificar que `__EFMigrationsHistory` queda vacío | `DatabaseTest/MigrationTests.cs` | ⬜ |
| 4.4 | Test: Seed data se inserta correctamente | Aplicar migraciones, verificar que tablas catálogo tienen datos seed | `DatabaseTest/MigrationTests.cs` | ⬜ |
| 4.5 | Job de CI para DatabaseTest | Nuevo job `database-test` en CI, solo en push a main (no PR). Timeout 15min | `.github/workflows/ci-cd.yml` | ⬜ |

---

## FASE 5 — Contract Testing con Pact

**Objetivo:** Garantizar que la API no rompe contratos con consumidores.

| # | Tarea | Descripción | Archivos | ✅ |
|---|---|---|---|---|
| 5.1 | Crear proyecto ContractTest | Nuevo proyecto `ContractTest/ContractTest.csproj` con PactNet, xUnit, WebApplicationFactory | `ContractTest/ContractTest.csproj` (nuevo) | ⬜ |
| 5.2 | Definir contrato Pact para endpoints críticos | Crear archivo Pact JSON que define expectativas de consumidores para login, productos, ventas | `ContractTest/pacts/` (nuevo) | ⬜ |
| 5.3 | Implementar Provider Tests | `ProviderTests.cs` que verifica que la API cumple el contrato Pact. Usa `PactVerifier` + `WebApplicationFactory` | `ContractTest/ProviderTests.cs` (nuevo) | ⬜ |
| 5.4 | Job de CI para contract testing | Agregar job `contract-test` en CI, solo en PR. `continue-on-error: true` (informativo inicialmente) | `.github/workflows/ci-cd.yml` | ⬜ |

---

## FASE 6 — Concurrencia y Tolerancia a Fallos

**Objetivo:** Detectar race conditions y asegurar que la app sobrevive a fallos de infraestructura.

| # | Tarea | Descripción | Archivos | ✅ |
|---|---|---|---|---|
| 6.1 | Test: Race condition en venta/stock | Unit test: producto con existencia=1, disparar 5 POST `/api/v1/venta` en paralelo. Solo 1 debe ser exitosa, 4 deben fallar con 400 | `UnitTest/Controllers/RaceConditionTests.cs` (nuevo) | ⬜ |
| 6.2 | Test: Redis caído con fallback | Integration test: configurar Redis con puerto inválido. Login debe funcionar usando fallback en memoria | `IntegrationTest/RedisFailureTests.cs` (nuevo) | ⬜ |
| 6.3 | Implementar fallback para Redis caído | En `LoginService` y `TokenBlacklistService`: try-catch alrededor de Redis, si falla usar `IMemoryCache` como fallback. Loggear warning | `Services/LoginService.cs`, `Services/TokenBlacklistService.cs` | ⬜ |
| 6.4 | Test: Circuit breaker de Polly se abre | Unit test: simular 5 `SaveChangesAsync` fallidos, verificar que `BrokenCircuitException` se lanza | `UnitTest/Services/DbResilienceServiceTests.cs` (nuevo) | ⬜ |
| 6.5 | Test: Circuit breaker se medio-abre | Unit test: después de que se abre, esperar el sampling duration y verificar que permite 1 request de prueba | `UnitTest/Services/DbResilienceServiceTests.cs` | ⬜ |
| 6.6 | Test: Circuit breaker se cierra | Unit test: después de medio-abierto con éxito, verificar que el circuito se cierra y requests pasan normalmente | `UnitTest/Services/DbResilienceServiceTests.cs` | ⬜ |
| 6.7 | Test: SQL Server caído — health check retorna 503 | Integration test: detener contenedor SQL, llamar `/health/ready` → 503. Reiniciar → 200 | `IntegrationTest/RecoveryTests.cs` (nuevo) | ⬜ |
| 6.8 | Test: Recuperación tras caída de BD | Integration test: detener BD, esperar, reiniciar, verificar que app vuelve a estado saludable | `IntegrationTest/RecoveryTests.cs` | ⬜ |

---

## FASE 7 — Hardening de Autenticación (2FA + Refresh Tokens)

**Objetivo:** Cerrar brechas OWASP ASVS V2 (autenticación) y V3 (sesión).

| # | Tarea | Descripción | Archivos | ✅ |
|---|---|---|---|---|
| 7.1 | Agregar RefreshTokenService | Nuevo servicio que emite refresh tokens (Guid, expiración 7 días), los almacena hasheados en BD, y los rota (cada uso revoca anterior) | `Services/RefreshTokenService.cs`, `Interfaces/IRefreshTokenService.cs` (nuevos) | ⬜ |
| 7.2 | Endpoint POST /api/v1/auth/refresh | Nuevo controller que acepta refresh token, valida, rota, y devuelve nuevo JWT + nuevo refresh token | `Controllers/RefreshController.cs` (nuevo) | ⬜ |
| 7.3 | Modelo SegRefreshToken | Nueva tabla en BD: `Id`, `TokenHash`, `JwtId`, `ExpiryDate`, `IsRevoked`, `CreatedAt` | `Models/SegRefreshToken.cs` + DbSet | ⬜ |
| 7.4 | Endpoint POST /api/v1/auth/2fa/setup | Habilitar 2FA para el usuario: genera secreto TOTP, devuelve QR uri | `Controllers/TwoFactorController.cs` (nuevo) | ⬜ |
| 7.5 | Endpoint POST /api/v1/auth/2fa/verify | Verificar código TOTP de 6 dígitos, habilitar 2FA | `Controllers/TwoFactorController.cs` | ⬜ |
| 7.6 | Login con 2FA | Si usuario tiene 2FA habilitado, login devuelve `requires_2fa: true` + token temporal. Segundo paso verifica TOTP y emite JWT real | `Services/LoginService.cs` (modificado) | ⬜ |
| 7.7 | Endpoint POST /api/v1/auth/change-password | Cambio de contraseña: verifica contraseña actual, aplica Argon2id, guarda nueva | `Controllers/PasswordController.cs` (nuevo) | ⬜ |
| 7.8 | Endpoint POST /api/v1/auth/recover | Recuperación de contraseña: envía token por email (simulado), permite reset | `Controllers/PasswordController.cs` | ⬜ |
| 7.9 | JWT ValidAlgorithms en Program.cs | Agregar `ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 }` en `TokenValidationParameters` para evitar algorithm confusion | `Program.cs` | ⬜ |
| 7.10 | Test: algoritmo None es rechazado | Security test: generar JWT con alg "none", verificar 401 | `SecurityTest/JwtAlgorithmConfusionTests.cs` (nuevo) | ⬜ |

---

## FASE 8 — Chaos Engineering

**Objetivo:** Validar tolerancia a fallos inyectando caos controlado.

| # | Tarea | Descripción | Archivos | ✅ |
|---|---|---|---|---|
| 8.1 | Crear proyecto ChaosTest | Nuevo proyecto `ChaosTest/ChaosTest.csproj` con `ChaosToolkit` o scripts PowerShell para matar servicios | `ChaosTest/` (nuevo) | ⬜ |
| 8.2 | Experimento: Matar Redis durante operación | Mientras se ejecuta un escenario de carga (NBomber), matar el contenedor Redis. Verificar que la API sigue respondiendo con fallback | `ChaosTest/Experiments/redis-kill.json` (nuevo) | ⬜ |
| 8.3 | Experimento: Matar SQL Server durante operación | Mientras se ejecuta POST `/api/v1/venta`, matar SQL Server. Verificar circuit breaker se abre y request falla gracefulmente | `ChaosTest/Experiments/sql-kill.json` (nuevo) | ⬜ |
| 8.4 | Experimento: Latencia de red en Redis | Inyectar latencia de 2s en Redis. Verificar que la API degrada gracefulmente (timeouts, fallbacks) | `ChaosTest/Experiments/redis-latency.json` (nuevo) | ⬜ |
| 8.5 | Job de CI para Chaos Testing | Job nocturno (cron) que ejecuta experiments y publica reporte | `.github/workflows/chaos-nightly.yml` (nuevo) | ⬜ |

---

## FASE 9 — Autorización a Nivel Objeto (Object-Level Auth)

**Objetivo:** Cerrar brecha OWASP ASVS V4 — ownership de recursos.

| # | Tarea | Descripción | Archivos | ✅ |
|---|---|---|---|---|
| 9.1 | Test: Usuario A no puede ver cliente del usuario B | Integration test: crear Cliente con usuario A, intentar GET con token del usuario B → 403 | `IntegrationTest/AuthorizationTests.cs` (nuevo) | ⬜ |
| 9.2 | Test: Usuario A no puede modificar producto del usuario B | Integration test: crear Producto con usuario A, intentar PUT/PATCH con token del usuario B → 403 | `IntegrationTest/AuthorizationTests.cs` | ⬜ |
| 9.3 | Test: Admin puede ver/modificar cualquier recurso | Integration test: Admin hace GET/PUT sobre recurso que no le pertenece → 200 | `IntegrationTest/AuthorizationTests.cs` | ⬜ |
| 9.4 | Implementar ownership checks en servicios | En `ClienteService`, `ProductoService`, etc., verificar que el usuario autenticado es el dueño del recurso o es Admin. Si no, lanzar `ForbiddenAccessException` | `Services/*Service.cs` | ⬜ |

---

## FASE 10 — Observabilidad y Dashboard de Calidad

**Objetivo:** Exponer métricas de calidad en tiempo real.

| # | Tarea | Descripción | Archivos | ✅ |
|---|---|---|---|---|
| 10.1 | Agregar OpenTelemetry a WebAPIDevSecOps | Agregar packages: `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.EntityFrameworkCore`, `OpenTelemetry.Exporter.Console` | `WebAPIDevSecOps.csproj`, `Program.cs` | ⬜ |
| 10.2 | Configurar métricas de calidad en OpenTelemetry | Exponer: `test_coverage_percent`, `mutation_score`, `sonar_quality_gate_passed`, `p95_latency_ms` | `Program.cs` | ⬜ |
| 10.3 | Crear endpoint /metrics | Endpoint que expone métricas en formato Prometheus (vía `OpenTelemetry.Exporter.Prometheus.AspNetCore` o manual) | `Program.cs` | ⬜ |
| 10.4 | Dashboard en JSON | Crear `quality-dashboard.json` con consultas para visualizar métricas en Grafana | `deploy/grafana/quality-dashboard.json` (nuevo) | ⬜ |
| 10.5 | Script de recolección de métricas post-CI | `scripts/collect-quality-metrics.sh` que después del CI llama SonarCloud API, Stryker report, NBomber report y publica métricas | `scripts/collect-quality-metrics.sh` (nuevo) | ⬜ |
| 10.6 | Agregar integridad de audit logs | Hash chain en AuditLoggingMiddleware: cada log contiene hash del log anterior. Test que verifica integridad | `Middleware/AuditLoggingMiddleware.cs` (modificado) | ⬜ |

---

## FASE 11 — PR Quality Gate + Checklist

**Objetivo:** Automatizar proceso de calidad en cada Pull Request.

| # | Tarea | Descripción | Archivos | ✅ |
|---|---|---|---|---|
| 11.1 | Crear CHECKLIST_PR.md | Checklist de calidad para PRs: cobertura ≥80%, tests nuevos, rate limiting, validación, sin secretos, etc. | `CHECKLIST_PR.md` (nuevo) | ⬜ |
| 11.2 | Job pr-quality-gate en CI | Job que revisa que todos los checks (build, test, semgrep, coverage) pasaron en el PR. Usa `actions/github-script` | `.github/workflows/ci-cd.yml` | ⬜ |
| 11.3 | PR comment automático con resultados | Comentario en PR con estado de: Build, Tests, Coverage, Semgrep, Mutation Score | `.github/workflows/ci-cd.yml` | ⬜ |
| 11.4 | Template de PR en GitHub | Crear `.github/PULL_REQUEST_TEMPLATE.md` con checklist integrado | `.github/PULL_REQUEST_TEMPLATE.md` (nuevo) | ⬜ |

---

## FASE 12 — Pentesting Automatizado + Hardening Runtime

**Objetivo:** Validar seguridad a nivel runtime y contenedor.

| # | Tarea | Descripción | Archivos | ✅ |
|---|---|---|---|---|
| 12.1 | Agregar dockle al CI | Escaneo de Dockerfile y contenedor: mejores prácticas, no root, sin secretos. Fallar en HIGH/CRITICAL | `.github/workflows/ci-cd.yml` | ⬜ |
| 12.2 | Agregar kube-bench (si aplica) | Si se usa Kubernetes, escaneo CIS benchmark. Si no, documentar como opcional | — | ⬜ |
| 12.3 | Pruebas de penetración básicas con OWASP ZAP en CI | El CI ya tiene ZAP — asegurar que se ejecuta en cada PR contra el build actual | `.github/workflows/ci-cd.yml` | ⬜ |
| 12.4 | Integrity check en startup | En `Program.cs`, al iniciar, verificar firma de assemblies propios con `System.Security.Cryptography` | `Program.cs` | ⬜ |
| 12.5 | Property-based testing con FsCheck | Test de integridad transaccional: generar secuencias de operaciones CRUD aleatorias y verificar que el estado final es consistente | `UnitTest/PropertyBased/TransactionIntegrityTests.cs` (nuevo) | ⬜ |
| 12.6 | Reporte de hardening | Job que genera reporte combinado: Trivy + dockle + ZAP + Semgrep como artifact único | `.github/workflows/ci-cd.yml` | ⬜ |

---

## 📊 RESUMEN DE CARGA ESTIMADA

| Fase | Semanas | Esfuerzo (horas) | Prioridad |
|------|---------|-------------------|-----------|
| F0 — Diagnóstico | 1 | 4h | 🔴 Alta |
| F1 — Métricas existentes | 1 | 8h | 🔴 Alta |
| F2 — Mutation Testing | 1 | 12h | 🔴 Alta |
| F3 — Performance | 1 | 16h | 🔴 Alta |
| F4 — DB Migration | 1 | 8h | 🟡 Media |
| F5 — Contract Testing | 1 | 8h | 🟡 Media |
| F6 — Concurrencia/Fallos | 2 | 20h | 🔴 Alta |
| F7 — Autenticación | 2 | 24h | 🔴 Alta |
| F8 — Chaos Engineering | 1 | 12h | 🟡 Media |
| F9 — Object-Level Auth | 1 | 8h | 🔴 Alta |
| F10 — Observabilidad | 1 | 12h | 🟡 Media |
| F11 — PR Quality Gate | 1 | 6h | 🔴 Alta |
| F12 — Pentesting/Runtime | 1 | 8h | 🟡 Media |
| **Total** | **~15 semanas** | **~146h** | |

---

## COBERTURA FINAL ESPERADA

| Estándar | Sin Plan | Con F1-F6 | Con F1-F12 |
|---|---|---|---|
| **ISO 25010** (8 características x subcaracterísticas) | 55% | 88% | **~97%** |
| **OWASP ASVS L2** (~14 categorías, ~250 requisitos) | 68% | 90% | **~98%** |
| **Brechas críticas** | 10 | 4 | **1** (2FA MFA Level 3) |

La única brecha remanente después de F12 sería **2FA/MFA Level 3** (OWASP ASVS V2.8), que es nivel 3 y está fuera del alcance L2. El 100% realista es ~98-99% — ese 1-2% restante corresponde a requisitos de nivel 3 que no aplican a una API interna.

---

## 📈 PROGRESO

| Fecha | Fase | Avance | Notas |
|-------|------|--------|-------|
| — | — | 0% | Pendiente de inicio |
