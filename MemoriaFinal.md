# MemoriaFinal — Plan Fusionado
## WebAPIDevSecOps — Arquitectura Saga + Calidad/QA

**Origen:** `MemoriaPlan.md` (Arquitectura Saga) + `MemoriaMQA.md` (Métricas de Calidad y QA)

**Leyenda:** `[MP]` = MemoriaPlan, `[MQA]` = MemoriaMQA

---

## FASE 1 — Fundación de Calidad

*Semana 1-3 | Establece métricas, gates y hardening antes de escribir código nuevo*

| # | Paso | Origen | Tarea | Descripción | Archivos | Depende de |
|---|---|---|---|---|---|---|
| 1.1 | ✅ | `[MQA] F0` | Diagnóstico inicial | **Hecho.** Tests: 741/741 OK (Unit 357, Integ 250, Security 134). Cobertura: 45.9% (4194/9128). Deps desactualizadas: 10. Semgrep: no disponible local. | — | — |
| 1.2 | ✅ | `[MQA] F1.1` | Subir cobertura 30%→75% | Cambiado threshold de 30 a 75. Fix deprecation warning de `find()` | `scripts/check_coverage.py` | 1.1 |
| 1.3 | ✅ | `[MQA] F1.2` | Exclusiones coverage | Excluido `[WebAPIDevSecOps.Models]*`, `[WebAPIDevSecOps.Dto]*`, `[WebAPIDevSecOps.Migrations]*` | `coverage.runsettings` | 1.2 |
| 1.4 | ✅ | `[MQA] F1.3` | SonarAnalyzer.CSharp | Agregado `SonarAnalyzer.CSharp` 10.* con `PrivateAssets=all` en `Directory.Build.props`. Build 0 errores, 345 warnings (incluye nuevos S8969/S1481) | `Directory.Build.props` | 1.3 |
| 1.5 | ✅ | `[MQA] F1.4` | Thresholds complejidad/MI | Configurado `RunAnalyzersDuringBuild=true`, `EnforceCodeStyleInBuild=true`, `AnalysisLevel=latest`, `AnalysisMode=All` en `Directory.Build.props`. Build 0 errores. ⚠️ SonarCloud thresholds (complejidad ≤15, MI ≥60, duplicación <3%) pendientes de configurar en UI de SonarCloud | `Directory.Build.props` + SonarCloud | 1.4 |
| 1.6 | ✅ | `[MQA] F1.5` | 8 reglas nuevas Semgrep | Implementadas: `log-sensitive-data`, `sql-injection-raw`, `missing-cors-validation`, `disabled-rate-limit`, `exception-without-log`, `loop-logging`, `insecure-deserialization`, `hardcoded-cryptokeys` | `.semgrep/semgrep.yaml` | 1.1 |
| 1.7 | ✅ | `[MQA] F1.6-1.7` | Exigencia 80% código nuevo | Creado `sonar-project.properties` con `sonar.new.coverage.requirement=80`, `sonar.coverage.acceptance.requirement=75`, exclusions para Migrations/Models/Dto/Program.cs | `sonar-project.properties` (nuevo) | 1.5 |
| 1.8 | ✅ | `[MQA] F1.8` | Verificar build falla con violaciones | Creado método CC=17 >15, build con S1541+S3776 como errors → falla. Limpiado. ⚠️ Program.cs (CC=39) y EmpleadoService.cs (CC=12) tienen violaciones preexistentes | — | 1.7 |
| 1.9 | ✅ | `[MQA] F11.1` | Crear CHECKLIST_PR.md | Creado con secciones: código, calidad/tests, seguridad, API, infraestructura, review, post-merge | `CHECKLIST_PR.md` (nuevo) | 1.8 |
| 1.10 | ✅ | `[MQA] F11.2-11.3` | Job pr-quality-gate en CI | Agregado job `pr-quality-gate` con GitHub Script: verifica build-and-test + semgrep, postea/actualiza comentario en PR con resultados | `.github/workflows/ci-cd.yml` | 1.9 |
| 1.11 | ✅ | `[MQA] F11.4` | Template PR en GitHub | Creado `.github/PULL_REQUEST_TEMPLATE.md` con checklist de código, tests, seguridad y QA automático. Referencia a CHECKLIST_PR.md para versión completa | `.github/PULL_REQUEST_TEMPLATE.md` (nuevo) | 1.10 |
| 1.12 | ✅ | `[MQA] F6.2` | Test: Redis caído con fallback | Creado `RedisFailureTests.cs` con `FailingDistributedCache` que lanza excepción. Tests esperan login OK y 401 con credenciales inválidas. ⚠️ Fallará hasta implementar fallback en 1.13 | `IntegrationTest/RedisFailureTests.cs` (nuevo) | 1.1 |
| 1.13 | ✅ | `[MQA] F6.3` | Implementar fallback Redis caído | Try-catch en cada operación Redis de `LoginService` y `TokenBlacklistService`. Fallback a `IMemoryCache` con warning log. `AddMemoryCache()` registrado en `Program.cs`. Tests 357/357 OK, RedisFailureTests 2/2 OK | `Services/LoginService.cs`, `Services/TokenBlacklistService.cs`, `Program.cs` | 1.12 |
| 1.14 | ✅ | `[MQA] F6.4-6.6` | Tests circuit breaker Polly | 3 tests: circuito se abre tras MinimumThroughput fallos, se cierra tras half-open exitoso, se reabre tras half-open fallido. 360/360 tests OK | `UnitTest/Services/DbResilienceServiceTests.cs` (nuevo) | 1.1 |
| 1.15 | ✅ | `[MQA] F7.9` | JWT ValidAlgorithms | Agregado `ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 }` en `TokenValidationParameters` en `Program.cs:207` | `Program.cs` | 1.1 |
| 1.16 | ⬜ | `[MQA] F7.10` | Test: algoritmo None es rechazado | Security test: generar JWT con alg "none", verificar 401 | `SecurityTest/JwtAlgorithmConfusionTests.cs` (nuevo) | 1.15 |
| 1.17 | ⬜ | `[MQA] F10.1` | OpenTelemetry básico | Agregar packages: `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.EntityFrameworkCore`, `OpenTelemetry.Exporter.Console` | `WebAPIDevSecOps.csproj`, `Program.cs` | 1.1 |

---

## FASE 2 — Servicios Saga + Concurrencia

*Semana 4-8 | Implementa lógica de negocio del saga con calidad desde el inicio*

| # | Paso | Origen | Tarea | Descripción | Archivos | Depende de |
|---|---|---|---|---|---|---|
| 2.1 | ✅ | `[MP] Etapa 5.2` | PagoService | `ProcesarPagoAsync` (simula 90% éxito), `ReembolsarPagoAsync`. Crea registro en `VenPedidoPago` | `Services/PagoService.cs` (nuevo) | 1.17 |
| 2.2 | ✅ | `[MP] Etapa 5.3` | FacturaService | `GenerarFacturaAsync` con folio `F-{año}-{seq}` desde Redis, `CancelarFacturaAsync`. Crea registro en `VenPedidoFactura` | `Services/FacturaService.cs` (nuevo) | 2.1 |
| 2.3 | ✅ | `[MP] Etapa 5.4` | CompensationService | Nivel 1: `CompensarPorPagoRechazado` — liberar stock. Nivel 2: `CompensarPorFacturaRechazada` — reembolsar pago + liberar stock | `Services/CompensationService.cs` (nuevo) | 2.2 |
| 2.4 | ✅ | `[MP] Etapa 5.5` | Registrar servicios en DI | `Program.cs`: AddScoped para PagoService, FacturaService, CompensationService | `Program.cs` | 2.3 |
| 2.5 | ✅ | `[MP] Etapa 6.5` | NuGets MassTransit + SQS | Agregar packages MassTransit y MassTransit.AmazonSQS al .csproj | `WebAPIDevSecOps.csproj` | 1.17 |
| 2.6 | ✅ | `[MP] Etapa 6.6` | Configurar MassTransit InMemory | Configurar bus InMemory para desarrollo local (sin dependencia AWS) | `Program.cs` | 2.5 |
| 2.7 | ✅ | `[MP] Etapa 6.1` | StockValidatorConsumer | Consume `PedidoCreadoEvent`, valida existencias, descuenta stock si OK, publica `StockValidadoEvent` o `StockRechazadoEvent` | `Consumers/StockValidatorConsumer.cs` (nuevo) | 2.6 |
| 2.8 | ✅ | `[MP] Etapa 6.2` | PagoConsumer | Consume `StockValidadoEvent`, llama a PagoService, publica `PagoProcesadoEvent` o `PagoRechazadoEvent` | `Consumers/PagoConsumer.cs` (nuevo) | 2.7 |
| 2.9 | ✅ | `[MP] Etapa 6.3` | FacturaConsumer | Consume `PagoProcesadoEvent`, llama a FacturaService, publica `FacturaGeneradoEvent` o `FacturaRechazadaEvent` | `Consumers/FacturaConsumer.cs` (nuevo) | 2.8 |
| 2.10 | ✅ | `[MP] Etapa 6.4` | CompensationConsumer | Escucha `PagoRechazadoEvent` + `FacturaRechazadaEvent`, ejecuta compensación según nivel | `Consumers/CompensationConsumer.cs` (nuevo) | 2.9 |
| 2.11 | ❌ | `[MP] Etapa 6.7` | Configurar MassTransit AmazonSQS | Configurar transporte SQS para producción (condicional por variable de entorno) | `Program.cs` | 2.10 |
| 2.12 | ✅ | `[MQA] F6.1` | Test: Race condition en venta/stock | Producto con existencia=1, 5 POST `/api/v1/venta` en paralelo → solo 1 éxito, 4 fallos 400 | `UnitTest/Controllers/RaceConditionTests.cs` (nuevo) | 2.1 |
| 2.13 | ✅ | `[MQA] F9.1` | Test: Usuario A no ve cliente del B | Integration test: crear Cliente con usuario A, GET con token de B → 403 | `IntegrationTest/AuthorizationTests.cs` (nuevo) | 1.1 |
| 2.14 | ✅ | `[MQA] F9.2` | Test: Usuario A no modifica producto del B | Integration test: crear Producto con A, PUT con token de B → 403 | `IntegrationTest/AuthorizationTests.cs` | 2.13 |
| 2.15 | ✅ | `[MQA] F9.3` | Test: Admin puede ver/modificar cualquier recurso | Integration test: Admin hace GET/PUT sobre recurso ajeno → 200 | `IntegrationTest/AuthorizationTests.cs` | 2.14 |
| 2.16 | ✅ | `[MQA] F9.4` | Implementar ownership checks | En `ClienteService`, `ProductoService`, etc.: verificar que usuario autenticado es dueño o es Admin. Si no, `ForbiddenAccessException` | `Services/*Service.cs` | 2.15 |
| 2.17 | ✅ | `[MQA] F12.5` | Property-based testing (FsCheck) | Tests de integridad transaccional: generar secuencias CRUD aleatorias, verificar estado final consistente | `UnitTest/PropertyBased/TransactionIntegrityTests.cs` (nuevo) | 1.1 |

---

## FASE 3 — Controllers Saga + Middleware + Auth

*Semana 9-13 | Expone endpoints saga, refactoriza middleware, agrega autenticación fuerte*

| # | Paso | Origen | Tarea | Descripción | Archivos | Depende de |
|---|---|---|---|---|---|---|
| 3.1 | ⬜ | `[MP] Etapa 7` | VentasPedidoController | POST `/api/v1/Ventas/pedido` (202 Accepted + Location header), GET por id, GET listar paginado | `Controllers/VentasPedidoController.cs` (nuevo) | 2.4 |
| 3.2 | ⬜ | `[MP] Etapa 7` | VentasPagoController | GET detalle pago por id, GET pagos por idPedido | `Controllers/VentasPagoController.cs` (nuevo) | 2.4 |
| 3.3 | ⬜ | `[MP] Etapa 7` | VentasFacturaController | GET detalle factura por id, GET facturas por idPedido | `Controllers/VentasFacturaController.cs` (nuevo) | 2.4 |
| 3.4 | ⬜ | `[MP] Etapa 8` | CorrelationIdMiddleware | Nuevo middleware: asigna CorrelationId (Guid) a cada request, lo propaga en response header y en logs | `Middleware/CorrelationIdMiddleware.cs` (nuevo) | 1.17 |
| 3.5 | ⬜ | `[MP] Etapa 8` | SecurityHeadersMiddleware | Extraer lógica de headers de seguridad de `Program.cs:379-408` a middleware dedicado (X-Content-Type-Options, X-Frame-Options, HSTS, CSP) | `Middleware/SecurityHeadersMiddleware.cs` (nuevo) | 3.4 |
| 3.6 | ⬜ | `[MP] Etapa 8` | CspNonceMiddleware | Extraer lógica CSP con nonce para Scalar de `Program.cs:413-455` a middleware dedicado | `Middleware/CspNonceMiddleware.cs` (nuevo) | 3.5 |
| 3.7 | ⬜ | `[MP] Etapa 9` | Rate Limiting Avanzado | Agregar `AdminPolicy` (200 requests/min) y `ConcurrentWritesPolicy` (10 concurrentes para POST/PUT/DELETE) | `Program.cs` | 3.6 |
| 3.8 | ⬜ | `[MQA] F4.1` | Crear proyecto DatabaseTest | Nuevo proyecto con xUnit + FluentAssertions + Testcontainers.MsSql + EF Core SQL Server | `DatabaseTest/DatabaseTest.csproj` (nuevo) | 1.1 |
| 3.9 | ⬜ | `[MQA] F4.2` | Test: Todas las migraciones se aplican | Iniciar contenedor SQL Server, ejecutar `context.Database.MigrateAsync()`, verificar que existen las 9 tablas | `DatabaseTest/MigrationTests.cs` (nuevo) | 3.8 |
| 3.10 | ⬜ | `[MQA] F4.3` | Test: Rollback de migraciones funciona | Aplicar migraciones, hacer rollback a "0", verificar que `__EFMigrationsHistory` queda vacío | `DatabaseTest/MigrationTests.cs` | 3.9 |
| 3.11 | ⬜ | `[MQA] F4.4` | Test: Seed data se inserta correctamente | Aplicar migraciones, verificar tablas catálogo tienen datos seed | `DatabaseTest/MigrationTests.cs` | 3.10 |
| 3.12 | ⬜ | `[MQA] F4.5` | Job CI para DatabaseTest | Nuevo job `database-test` en CI, solo en push a main (no PR). Timeout 15min | `.github/workflows/ci-cd.yml` | 3.11 |
| 3.13 | ⬜ | `[MQA] F7.1` | RefreshTokenService | Servicio que emite refresh tokens (Guid, expiración 7 días), los almacena hasheados en BD, los rota (cada uso revoca anterior) | `Services/RefreshTokenService.cs`, `Interfaces/IRefreshTokenService.cs` (nuevos) | 1.1 |
| 3.14 | ⬜ | `[MQA] F7.2` | Endpoint POST /api/v1/auth/refresh | Acepta refresh token, valida, rota, devuelve nuevo JWT + nuevo refresh token | `Controllers/RefreshController.cs` (nuevo) | 3.13 |
| 3.15 | ⬜ | `[MQA] F7.3` | Modelo SegRefreshToken | Nueva tabla: `Id` (int PK), `TokenHash` (nvarchar), `JwtId` (nvarchar), `ExpiryDate` (datetime2), `IsRevoked` (bit), `CreatedAt` (datetime2) | `Models/SegRefreshToken.cs` + DbSet en `AppDbContext.cs` | 3.14 |
| 3.16 | ⬜ | `[MQA] F7.4` | Endpoint POST /api/v1/auth/2fa/setup | Genera secreto TOTP para el usuario, devuelve QR uri para app autenticadora | `Controllers/TwoFactorController.cs` (nuevo) | 3.15 |
| 3.17 | ⬜ | `[MQA] F7.5` | Endpoint POST /api/v1/auth/2fa/verify | Verifica código TOTP de 6 dígitos ingresado por usuario, habilita 2FA | `Controllers/TwoFactorController.cs` | 3.16 |
| 3.18 | ⬜ | `[MQA] F7.6` | Login con 2FA | Si usuario tiene 2FA habilitado, login devuelve `requires_2fa: true` + token temporal. Segundo paso verifica TOTP y emite JWT real | `Services/LoginService.cs` (modificado) | 3.17 |
| 3.19 | ⬜ | `[MQA] F7.7` | Endpoint POST /api/v1/auth/change-password | Verifica contraseña actual, aplica Argon2id, guarda nueva contraseña | `Controllers/PasswordController.cs` (nuevo) | 3.18 |
| 3.20 | ⬜ | `[MQA] F7.8` | Endpoint POST /api/v1/auth/recover | Recuperación de contraseña: envía token por email (simulado), permite reset | `Controllers/PasswordController.cs` | 3.19 |
| 3.21 | ⬜ | `[MQA] F12.1` | Agregar dockle al CI | Escaneo de Dockerfile: mejores prácticas, no root, sin secretos. Fallar en HIGH/CRITICAL | `.github/workflows/ci-cd.yml` | 1.17 |
| 3.22 | ⬜ | `[MQA] F12.3` | OWASP ZAP en cada PR | Asegurar que el escaneo ZAP existente se ejecuta contra el build actual en cada PR | `.github/workflows/ci-cd.yml` | 3.21 |
| 3.23 | ⬜ | `[MQA] F12.4` | Integrity check en startup | En `Program.cs`, al iniciar verificar firma de assemblies propios con `System.Security.Cryptography` | `Program.cs` | 3.22 |

---

## FASE 4 — Validación Profunda

*Semana 14-18 | Mutation testing, performance, contratos, dashboard de calidad*

| # | Paso | Origen | Tarea | Descripción | Archivos | Depende de |
|---|---|---|---|---|---|---|
| 4.1 | ⬜ | `[MQA] F2.1` | Crear proyecto MutationTest | Nuevo proyecto con target net10.0, referencia a Stryker.NET | `MutationTest/MutationTest.csproj` (nuevo) | 1.1 |
| 4.2 | ⬜ | `[MQA] F2.2` | Configurar stryker-config.json | Thresholds high=80, low=70, break=60. Excluir Migrations/, Models/, Dto/, Program.cs | `MutationTest/stryker-config.json` (nuevo) | 4.1 |
| 4.3 | ⬜ | `[MQA] F2.3` | Job CI mutation-test | Stryker en push a main (no PR), timeout 30min, publica reporte HTML como artifact | `.github/workflows/ci-cd.yml` | 4.2 |
| 4.4 | ⬜ | `[MQA] F2.4` | Ejecutar Stryker línea base | `dotnet stryker --config-file MutationTest/stryker-config.json`. Reportar mutation score actual | — | 4.3 |
| 4.5 | ⬜ | `[MQA] F2.5` | Mejorar tests donde mutation score bajo | Identificar mutantes sobrevivientes, agregar tests que los maten. Repetir hasta score ≥70% | Archivos de test varios | 4.4 |
| 4.6 | ⬜ | `[MQA] F3.1` | Crear proyecto PerformanceTest | NBomber + NBomber.Http, OutputType Exe. Referencia a WebAPIDevSecOps | `PerformanceTest/PerformanceTest.csproj` (nuevo) | 1.1 |
| 4.7 | ⬜ | `[MQA] F3.2` | Escenario Login NBomber | POST `/api/v1/login` con rampa 5→50 usuarios concurrentes, 2min. Umbral P95 < 500ms, error < 0.1% | `PerformanceTest/Scenarios/LoginScenario.cs` (nuevo) | 4.6 |
| 4.8 | ⬜ | `[MQA] F3.3` | Escenario Productos GET | GET `/api/v1/producto` con 100 usuarios constantes, 2min. Umbral P95 < 200ms, error < 0.1% | `PerformanceTest/Scenarios/ProductoScenario.cs` (nuevo) | 4.7 |
| 4.9 | ⬜ | `[MQA] F3.4` | Escenario Venta POST | POST `/api/v1/venta` con rampa 5→30 usuarios, 2min. Umbral P95 < 1000ms, error < 0.5% | `PerformanceTest/Scenarios/VentaScenario.cs` (nuevo) | 4.8 |
| 4.10 | ⬜ | `[MQA] F3.5` | Escenario Mixto | 60% GET producto, 20% POST login, 20% POST venta. 80 usuarios, 3min. Promedio < 800ms, error < 0.5% | `PerformanceTest/Scenarios/MixtoScenario.cs` (nuevo) | 4.9 |
| 4.11 | ⬜ | `[MQA] F3.6` | Program.cs + thresholds + reporte | Programa principal que corre todos los escenarios, verifica thresholds, genera reporte HTML | `PerformanceTest/Program.cs` (nuevo) | 4.10 |
| 4.12 | ⬜ | `[MQA] F5.1` | Crear proyecto ContractTest | PactNet + xUnit + WebApplicationFactory | `ContractTest/ContractTest.csproj` (nuevo) | 1.1 |
| 4.13 | ⬜ | `[MQA] F5.2` | Definir contrato Pact | Archivo JSON con expectativas de consumidores para endpoints críticos (login, productos, ventas, saga) | `ContractTest/pacts/` (nuevo) | 4.12 |
| 4.14 | ⬜ | `[MQA] F5.3` | Provider Tests | `PactVerifier` + `WebApplicationFactory` que verifica que la API cumple el contrato | `ContractTest/ProviderTests.cs` (nuevo) | 4.13 |
| 4.15 | ⬜ | `[MQA] F5.4` | Job CI contract-test | Solo en PR, `continue-on-error: true` (informativo inicialmente) | `.github/workflows/ci-cd.yml` | 4.14 |
| 4.16 | ⬜ | `[MQA] F10.2` | Métricas de calidad en OpenTelemetry | Exponer: `test_coverage_percent`, `mutation_score`, `sonar_quality_gate_passed`, `p95_latency_ms` | `Program.cs` | 1.17 |
| 4.17 | ⬜ | `[MQA] F10.3` | Endpoint /metrics | Endpoint que expone métricas en formato Prometheus | `Program.cs` | 4.16 |
| 4.18 | ⬜ | `[MQA] F10.4` | Dashboard Grafana JSON | Crear JSON con consultas para visualizar métricas de calidad en Grafana | `deploy/grafana/quality-dashboard.json` (nuevo) | 4.17 |
| 4.19 | ⬜ | `[MQA] F10.5` | Script métricas post-CI | Script que llama SonarCloud API + Stryker report + NBomber report y publica métricas consolidadas | `scripts/collect-quality-metrics.sh` (nuevo) | 4.18 |
| 4.20 | ⬜ | `[MQA] F10.6` | Integridad de audit logs | Hash chain en `AuditLoggingMiddleware`: cada log contiene hash del log anterior. Test que verifica integridad de la cadena | `Middleware/AuditLoggingMiddleware.cs` (modificado) | 4.19 |

---

## FASE 5 — Finalización Saga + QA Residual

*Semana 18-20 | Completar dashboard saga, tests de recuperación, hardening*

| # | Paso | Origen | Tarea | Descripción | Archivos | Depende de |
|---|---|---|---|---|---|---|
| 5.1 | ⬜ | `[MP] Etapa 10` | VentasDashboardController | GET `/api/v1/Ventas/dashboard`: total pedidos hoy, ventas hoy, pedidos por EstadoSaga, profundidad colas SQS | `Controllers/VentasDashboardController.cs` (nuevo) | 3.3 |
| 5.2 | ⬜ | `[MP] Etapa 10` | Saga timeline endpoint | GET `/api/v1/Ventas/saga/{id}/diagrama`: línea de tiempo del saga con eventos y timestamps | `Controllers/VentasDashboardController.cs` | 5.1 |
| 5.3 | ⬜ | `[MP] Etapa 11` | 14 tests saga pendientes | Unit tests: VentasPedidoService, PagoService, FacturaService, CompensationService, StockValidatorConsumer, PagoConsumer, FacturaConsumer, CorrelationIdMiddleware, SecurityHeadersMiddleware | Archivos de test varios | 3.7 |
| 5.4 | ⬜ | `[MQA] F6.7` | Test: SQL Server caído → health 503 | Integration test: detener contenedor SQL, llamar `/health/ready` → 503. Reiniciar → 200 | `IntegrationTest/RecoveryTests.cs` | 4.5 |
| 5.5 | ⬜ | `[MQA] F6.8` | Test: Recuperación tras caída de BD | Integration test: detener BD, esperar, reiniciar, verificar app vuelve a estado saludable | `IntegrationTest/RecoveryTests.cs` | 5.4 |
| 5.6 | ⬜ | `[MQA] F12.6` | Reporte hardening combinado | Job CI que genera artifact único: Trivy + dockle + ZAP + Semgrep | `.github/workflows/ci-cd.yml` | 3.23 |

---

## FASE 6 — Chaos Engineering + Deploy AWS

*Semana 20-24 | Validación de tolerancia a fallos y despliegue en producción*

| # | Paso | Origen | Tarea | Descripción | Archivos | Depende de |
|---|---|---|---|---|---|---|
| 6.1 | ⬜ | `[MQA] F8.1` | Crear proyecto ChaosTest | Proyecto con ChaosToolkit o scripts PowerShell para inyectar fallos controlados | `ChaosTest/` (nuevo) | 5.6 |
| 6.2 | ⬜ | `[MQA] F8.2` | Experimento: Matar Redis | Durante carga NBomber, matar contenedor Redis. Verificar API sigue respondiendo con fallback en memoria | `ChaosTest/Experiments/redis-kill.json` (nuevo) | 6.1 |
| 6.3 | ⬜ | `[MQA] F8.3` | Experimento: Matar SQL Server | Durante POST `/api/v1/venta`, matar SQL. Verificar circuit breaker se abre y request falla gracefulmente | `ChaosTest/Experiments/sql-kill.json` (nuevo) | 6.2 |
| 6.4 | ⬜ | `[MQA] F8.4` | Experimento: Latencia de red en Redis | Inyectar 2s de latencia en Redis. Verificar API degrada gracefulmente (timeouts, fallbacks activados) | `ChaosTest/Experiments/redis-latency.json` (nuevo) | 6.3 |
| 6.5 | ⬜ | `[MQA] F8.5` | Job CI chaos-nightly | Workflow nocturno (cron) que ejecuta experiments y publica reporte de resultados | `.github/workflows/chaos-nightly.yml` (nuevo) | 6.4 |
| 6.6 | ⬜ | `[MP] Etapa 12.1-12.8` | Día 1 — Red y Seguridad AWS | Crear VPC 10.0.0.0/16, 2 subnets públicas, Internet Gateway, Security Groups (ALB + EC2), IAM user con Access Key | AWS Console | 5.3 |
| 6.7 | ⬜ | `[MP] Etapa 12.9-12.13` | Día 2 — ALB + EC2 | Crear IAM Role EC2 (SQS + CloudWatch), Target Group HTTP:8080 con health check `/health/ready`, ALB internet-facing, lanzar EC2 t2.micro, instalar Docker | AWS Console | 6.6 |
| 6.8 | ⬜ | `[MP] Etapa 12.14-12.24` | Día 3 — SQS + Deploy App | Crear 4 colas FIFO (pedidos, pedidos-pago, pedidos-factura, pedidos-dlq) con DLQ configurado. Copiar docker-compose a EC2 via SCP. Configurar env vars. `docker-compose up -d`. Verificar health. Cambiar MassTransit de InMemory a AmazonSQS | AWS Console + EC2 | 6.7 |
| 6.9 | ⬜ | `[MP] Etapa 13.1-13.4` | Automatización CloudFormation | Crear template CloudFormation con VPC, subnets, IGW, SG, ALB, TG, EC2, SQS x4, IAM Role, CloudWatch Logs. Crear scripts `deploy-aws.sh` y `deploy-app.sh` | `deploy/aws/cloudformation.yml`, `scripts/deploy-aws.sh`, `scripts/deploy-app.sh` (nuevos) | 6.8 |
| 6.10 | ⬜ | `[MP] Etapa 13.5-13.10` | Destrucción y Redeploy Automático | `aws cloudformation delete-stack`, verificar 0 recursos en AWS Console, redeploy con `bash scripts/deploy-aws.sh`, smoke test automático con curl a health + saga endpoints | — | 6.9 |
| 6.11 | ⬜ | `[MP] Etapa 13.11-13.13` | Destrucción Final | Destruir todo con `aws cloudformation delete-stack`. Verificar AWS Console: 0 recursos (EC2, ALB, SQS, CloudWatch). Eliminar Access Key IAM | AWS Console | 6.10 |
| 6.12 | ⬜ | `[MQA] F12.2` | kube-bench (opcional) | Si se utiliza Kubernetes en el deploy, agregar escaneo CIS benchmark. Si no, documentar como mejora futura | — | 6.10 |

---

## 📊 RESUMEN DE CARGA

| Fase | Nombre | Pasos | Semanas | Esfuerzo (horas) | Prioridad |
|------|--------|-------|---------|-------------------|-----------|
| 1 | Fundación de Calidad | 17 | 3 | 20h | 🔴 Alta |
| 2 | Servicios Saga + Concurrencia | 17 | 5 | 40h | 🔴 Alta |
| 3 | Controllers Saga + Middleware + Auth | 23 | 5 | 45h | 🔴 Alta |
| 4 | Validación Profunda | 20 | 5 | 38h | 🟡 Media |
| 5 | Finalización Saga + QA Residual | 6 | 3 | 16h | 🟡 Media |
| 6 | Chaos Engineering + Deploy AWS | 12 | 4 | 30h | 🟡 Media |
| **Total** | | **95** | **~25** | **~189h** | |

---

## 📈 COBERTURA ESPERADA

| Estándar | Actual | Post-Fase 3 | Post-Fase 6 |
|---|---|---|---|
| **ISO 25010** (8 características x subcaracterísticas) | 55% | 88% | **~97%** |
| **OWASP ASVS L2** (~14 categorías, ~250 requisitos) | 68% | 90% | **~98%** |
| **Cobertura de código** | ~30% | ≥75% | ≥80% |
| **Mutation score** | ❌ Sin medir | ≥60% | ≥70% |
| **Performance P95 login** | ❌ Sin medir | <500ms | <500ms |
| **Performance P95 GET** | ❌ Sin medir | <200ms | <200ms |
| **Tolerancia a fallos (Redis/SQL caído)** | ❌ No existe | Fallback implementado | Chaos validated |

La única brecha remanente post-Fase 6 es **2FA/MFA Level 3** (OWASP ASVS V2.8), que es requisito de nivel 3 fuera del alcance L2. El 100% realista es ~98-99%.

---

## ✓ CHECKLIST DE SEGUIMIENTO FUSIONADO

### FASE 1 — Fundación de Calidad (17 pasos)
```
✅ 1.1  ✅ 1.2  ✅ 1.3  ✅ 1.4  ✅ 1.5  ✅ 1.6  ✅ 1.7  ✅ 1.8
✅ 1.9  ✅ 1.10 ✅ 1.11 ✅ 1.12 ✅ 1.13 ✅ 1.14 ✅ 1.15 ▢ 1.16
▢ 1.17
```

### FASE 2 — Servicios Saga + Concurrencia (17 pasos)
```
▢ 2.1  ▢ 2.2  ▢ 2.3  ▢ 2.4  ▢ 2.5  ▢ 2.6  ▢ 2.7  ▢ 2.8
▢ 2.9  ▢ 2.10 ▢ 2.11 ▢ 2.12 ▢ 2.13 ▢ 2.14 ▢ 2.15 ▢ 2.16
▢ 2.17
```

### FASE 3 — Controllers Saga + Middleware + Auth (23 pasos)
```
▢ 3.1  ▢ 3.2  ▢ 3.3  ▢ 3.4  ▢ 3.5  ▢ 3.6  ▢ 3.7  ▢ 3.8
▢ 3.9  ▢ 3.10 ▢ 3.11 ▢ 3.12 ▢ 3.13 ▢ 3.14 ▢ 3.15 ▢ 3.16
▢ 3.17 ▢ 3.18 ▢ 3.19 ▢ 3.20 ▢ 3.21 ▢ 3.22 ▢ 3.23
```

### FASE 4 — Validación Profunda (20 pasos)
```
▢ 4.1  ▢ 4.2  ▢ 4.3  ▢ 4.4  ▢ 4.5  ▢ 4.6  ▢ 4.7  ▢ 4.8
▢ 4.9  ▢ 4.10 ▢ 4.11 ▢ 4.12 ▢ 4.13 ▢ 4.14 ▢ 4.15 ▢ 4.16
▢ 4.17 ▢ 4.18 ▢ 4.19 ▢ 4.20
```

### FASE 5 — Finalización Saga + QA Residual (6 pasos)
```
▢ 5.1  ▢ 5.2  ▢ 5.3  ▢ 5.4  ▢ 5.5  ▢ 5.6
```

### FASE 6 — Chaos Engineering + Deploy AWS (12 pasos)
```
▢ 6.1  ▢ 6.2  ▢ 6.3  ▢ 6.4  ▢ 6.5  ▢ 6.6
▢ 6.7  ▢ 6.8  ▢ 6.9  ▢ 6.10 ▢ 6.11 ▢ 6.12
```

---

**Total: 95 pasos | ✅ 15% completado (15/95)**

```
Progreso: ███████████████░░░░░ 15%
```
