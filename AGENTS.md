# WebAPIDevSecOps

ASP.NET Core 10.0 Web API — JWT auth, Argon2id hashing, rate limiting, SQL Server EF Core.

## Setup inicial

```powershell
# 1. Copiar template de configuración
cp WebAPIDevSecOps/appsettings.Example.json WebAPIDevSecOps/appsettings.json

# 2. Editar appsettings.json con tu conexión local
#    O usar BD en memoria para desarrollo rápido:
#    Agregar "UseInMemoryDatabase": true en appsettings.json

# 3. Restaurar y ejecutar
dotnet restore
dotnet run --project WebAPIDevSecOps/WebAPIDevSecOps.csproj
```

## Quick commands

```powershell
dotnet restore
dotnet build -c Release --no-restore
dotnet test UnitTest/UnitTest.csproj -c Release --no-build
dotnet test IntegrationTest/IntegrationTest.csproj -c Release --no-build
dotnet test SecurityTest/SecurityTest.csproj -c Release --no-build
dotnet run --project WebAPIDevSecOps/WebAPIDevSecOps.csproj
```

CI order: `restore → build → unit tests → integration tests → security tests`. Tests require `--no-build` after build.

## Solution structure

`WebAPIDevSecOps.slnx` uses the new `.slnx` XML format (not `.sln`).

| Project | Path | Type |
|---|---|---|
| `WebAPIDevSecOps` | `WebAPIDevSecOps/` | Web API (entrypoint: `Program.cs`) |
| `UnitTest` | `UnitTest/` | xUnit unit tests |
| `IntegrationTest` | `IntegrationTest/` | xUnit + `WebApplicationFactory` |
| `SecurityTest` | `SecurityTest/` | xUnit + `WebApplicationFactory` |

## API conventions

- Route pattern: `api/v{version:apiVersion}/[controller]` — URL segment versioning, all controllers are v1.0
- JSON: `PropertyNamingPolicy = null` (preserves PascalCase)
- Dev URLs: `http://localhost:5196` / `https://localhost:7227` (launch URL: `/scalar`)
- Health: `/health` (all checks), `/health/ready` (DB only), `/health-ui`
- OpenAPI + Scalar UI available only in Development

## Security features (notable)

- **Password hashing**: Argon2id (via `Konscious.Security.Cryptography.Argon2`) with BCrypt fallback. Config in `PasswordHashing` section.
- **Token blacklist**: Static in-memory `TokenBlacklist` class. Used by `LogoutController` + inline middleware in `Program.cs`.
- **Rate limiting**: Global 1000/min fixed window. Login endpoint: 5 per 5min sliding window (`LoginPolicy`).
- **Security headers**: Set in inline middleware in `Program.cs` (X-Content-Type-Options, X-Frame-Options, HSTS, CSP, etc.)
- **JWT**: Requires 256-bit key (min 32 bytes). Config via `Jwt:Key` / `Jwt:Issuer` / `Jwt:Audience`. Overridable via `DB_USER`/`DB_PASSWORD` env vars for SQL Server.
- **Request timeout**: Configurable via `RequestTimeoutSeconds` (default 60).
- **DB connection**: Supports overriding UserID/Password via `DB_USER` / `DB_PASSWORD` env vars.

## Testing quirks

- **All tests use xUnit** + FluentAssertions + Moq
- **Unit tests** use `DbContextMock.GetDbContext()` from `UnitTest.Logic` — creates a unique InMemory database per test via `TestDbContext` (subclass that auto-sets `RowVersion` on `SegUsuario` inserts)
- **Integration & Security tests** use `WebApplicationFactory<Program>` — note the `public partial class Program { }` at the end of `Program.cs`
- Integration/security tests override `Jwt:Key`/`Jwt:Issuer`/`Jwt:Audience` via `builder.UseSetting()` in the factory
- `TokenBlacklist` is static and shared across tests — beware of test isolation if relying on its state
- Tests must specify `Jwt:Key` of sufficient length (≥32 bytes) or the app throws on startup
- `PasswordHasherService.VerifyPassword` handles both `$argon2id$` and BCrypt `$2a$`/`$2b$` hashes
- **InMemory DB for tests**: Set `builder.UseSetting("UseInMemoryDatabase", "true")` in the factory to bypass SQL Server and use EF Core InMemory (`Program.cs` checks this flag). `SegUsuario.RowVersion` has default `new byte[] { 1 }` because InMemory doesn't auto-generate `[Timestamp]` columns.
- **Security test helpers** live in `UnitTest/Common/`: `TokenHelper` (generates valid/expired/role-specific JWTs), `TestDataFactory` (creates UsuarioCreateDto/UpdateDto/DeleteDto). Security tests reference `UnitTest.csproj` for these utilities.

## Static analysis

- **Semgrep**: Custom rules in `.semgrep/semgrep.yaml`. Run via: `semgrep ci --config=auto --config=.semgrep/semgrep.yaml --error --metrics=off`
- **Editorconfig**: Extensive set of security CA rules (CA3000+ series) set to error
- **SonarCloud**: Run in CI via `dotnet-sonarscanner` with OpenCover coverage

## Key `appsettings.json` sections

`ConnectionStrings:DefaultConnection`, `Jwt` (Key/Issuer/Audience), `PasswordHashing` (MemorySize/Iterations/DegreeOfParallelism), `Resilience` (circuit breaker), `RequestTimeoutSeconds`.

## Lessons learned (Fase 4 — Validación Profunda)

Full analysis in `agent.md`. Key knowledge not previously known, with evidence:

**Mutation testing (Stryker)**
- Line coverage (46%) does not predict mutation score (66%); mutation score is the real quality gate
- `mutate` globs resolve **relative to the mutated project, not the config file** (`"**"` + `!**/Migrations/**`; `../` removed all mutants)
- Real run takes ~2h30–2h45 → CI timeout must be 180min, not 30min
- Score formula: `(Killed+Timeout)/(Killed+Timeout+Survived+NoCoverage)` — excludes CompileError, filters Ignored; the real `mutation-report.json` has NO root `mutationScore` key
- Some mutants are unkillable by design with InMemory (Include=INNER JOIN, `RandomNumberGenerator.GetInt32` not injectable, `SaveChangesAsync` no-op) → document as accepted survivors
- **Safe Mode = blind spot**: an "unidentified" compile error (e.g. CS0165 unassigned local from `statementRemoval` on `x = Foo();` with try/catch-throw pattern) makes Stryker drop **all** mutants of that method as CompileError (excluded from score → looks fine but the method is untested). Fix: assign in **all** branches (`catch { x = default!; throw ...; }`) so any single mutation still compiles
- Equivalent mutations are unkillable: `A||B||C → A&&B||C` on TOTP code validation (`IsNullOrEmpty || Length!=6 || !All(digit)`) is behavior-identical because empty ⇒ length≠6 → document, don't chase
- FsCheck generates boundary-padded strings → flaky tests if not `.Trim()`-compared

**Performance (NBomber)**
- NBomber 6.5 has no step weights → probabilistic per-iteration selection with `RandomNumberGenerator` (CA5394)
- App rate limits (LoginPolicy 5/5min, AdminPolicy 200/min, ConcurrentWritesPolicy 10) saturate load suites → relax in perf env or 429s break error thresholds
- Single login in `WithInit` + shared JWT avoids Argon2id bursts
- Clean exit codes: 0 PASS / 1 thresholds / 2 suite error

**Contract testing (Pact)**
- PactNet 4.x: verifier is in core package (`PactNet.Verifier`); `PactNet.Provider.xUnit` does not exist
- `WebApplicationFactory` forces TestServer (ignores `UseKestrel`/`UseUrls`); Pact FFI needs real HTTP → launch the API as a **real process** on a free port with env vars, wait `/health`, kill tree in `finally`
- pactSpecification **v2 discards matchingRules** (compares literals) → use **3.0.0 with flat rules** `{"match": "type"}` (v3 wrapper `{"matchers": [...]}` fails to parse)
- InMemory `RowVersion` `byte[]{1}` → base64 `"AQ=="`

**Metrics/Observability (OTel + Prometheus)**
- `OpenTelemetry.Exporter.Prometheus.AspNetCore` has NO stable release (33/33 prerelease) → pin 1.17.0-beta.1 aligned with OTel core 1.17.0
- Lazy DI singleton never registers the Meter → **resolve eagerly** after `builder.Build()` or gauges never appear in scrape
- Exporter appends unit suffixes: `mutation_score_percent`, `p95_latency_ms_milliseconds`, `sonar_quality_gate_passed` (no suffix), `test_coverage_percent` → verify real names with `curl /metrics` before writing PromQL/dashboards
- NBomber report.json: `nodeStats.scenarios[].ok.latency.percent95` (camelCase)

**Audit (hash chain)**
- Static state (like `TokenBlacklist`) crosses tests → `Reset()` in constructor; `lock` for thread safety

**CI legacy (fixes 3.24–3.27)**
- Console OTel exporters crash the testhost (1.1M log lines, shutdown race) → gate behind `Observability:ConsoleExport`
- sonarscanner 11.2.1 aborts if `projectName` passed as `/d:` → use `/n:`
- .NET 10 base image changed `ASPNETCORE_URLS` → `ASPNETCORE_HTTP_PORTS` (dockle)
- Coverage threshold must match reality (75% hardcoded vs 46% real → 45%)

**Recovery tests con Testcontainers (Fase 5)**
- Testcontainers 4.13 `MsSqlContainer` publica un puerto host **aleatorio** y, tras `StopAsync`/`StartAsync`, re-pública en un **puerto nuevo** (el viejo muere) → la connection string de la app queda obsoleta y el health nunca recupera
- Solución: `ContainerBuilder` genérico con `WithPortBinding(hostPort, 1433)` — **en v4 el orden es (hostPort, containerPort)**, invertido a v3 — el puerto fijo sí sobrevive al restart (verificado empíricamente)

**Chaos nightly E2E (Fase 6, verificado en vivo)**
- PowerShell 5.1: `Invoke-WebRequest` sin `-UseBasicParsing` usa el parser de IE y timeoutea en respuestas JSON → verify siempre status 0 (FAIL espurio); usarlo siempre en FaultInjector
- NBomber **borra el contenido de su carpeta de reportes al arrancar** → no compartir carpeta con otros artefactos: caos en `reports/`, NBomber en `perf-reports/`
- NBomber aborta la carga con "Stopping test early" si un escenario acumula demasiados fails → `Scenario.WithMaxFailCount(10M)` para que el experimento mida bajo carga real (los thresholds de carga ya son ruido para el veredicto de caos)
- StackExchange.Redis con defaults mata el fallback: con el servidor muerto cada op espera `asyncTimeout` (5s) mientras el multiplexer reencola (qs/async-ops crecen) → thread pool/cola saturadas y la API no acepta conexiones por >25s aunque el fallback a IMemoryCache exista. Fix en `Program.cs`: `ConfigurationOptions.Parse` + `AbortOnConnectFail=false`, `ConnectTimeout=2000`, `SyncTimeout=1000`, `AsyncTimeout=500`, `ConnectRetry=1`, `ReconnectRetryPolicy=ExponentialRetry(5000)` → el fallback actúa en ≤500ms y la API sigue respondiendo 200 con Redis muerto bajo carga
- `Stop-Process` sobre `dotnet run` deja el árbol hijo (la app) vivo → kill por árbol: `taskkill /PID /T /F` en Windows, `pkill -TERM -P` en Linux (Stop-LoadTree)
- Cuidado: los nodos MSBuild persistentes (`dotnet ... MSBuild.dll /nodemod...`) parecen procesos huérfanos pero son inocuos; no matarlos salvo limpieza real
- Concurrencia NBomber por escenario configurable vía env `PERF_LOGIN_USERS`/`PERF_PRODUCTO_USERS`/`PERF_VENTA_USERS`/`PERF_MIXTO_USERS` (defaults intactos) — los JSON de caos relajan a 10/20/10/20 porque Docker Desktop (NAT + CPU limitada) satura con los defaults (Argon2id a 42 verifies/s colapsa el contenedor)
- **Diagnóstico de "verify FAIL status 0" (metodología validada en vivo)**: el mismo síntoma tiene 4 causas distintas → discriminar antes de tocar código: (1) sondeo manual desde el host con `-UseBasicParsing` y timeout corto — si responde 200 y el runner no, es el parser de IE; (2) grupo control: misma carga **sin** el fallo — si pasa, el fallo es real del SUT, no del rig; (3) logs de la API como prueba de vida (los WARN de fallback demuestran que la app procesa aunque el verify falle → hambruna de recursos, no caída); (4) `Get-Process dotnet` / revisar cargas apiladas de runs previos (cada carga huérfana acumula rps). Solo tras descartar las 4 se investiga el SUT
- **Las imágenes `mcr.microsoft.com/dotnet/aspnet:10.0` NO incluyen wget/curl** (verificado: `command -v curl || command -v wget` vacío): una sonda HTTP vía `docker exec` da falso negativo 12/12 dentro del contenedor mientras el host responde 12/12 — verificar la herramienta antes de sondear desde dentro, o sondear siempre desde el host
- **Argon2id es el cuello de botella CPU de los escenarios de carga**: la firma de saturación es `[TIMING] VerifyPassword: 7456ms` (vs ~100-500ms normal) — ~42 verifies/s (cada uno ~64MB de memoria Argon2) colapsan un contenedor de 2 vCPU (Docker Desktop) y la API deja de aceptar conexiones; los escenarios de login son los más frágiles bajo caos → concurrency de login baja en entornos restringidos y arrancar la suite de perf con un solo login compartido (`WithInit`) en vez de logins por iteración
- **Firma del reconnection storm de StackExchange.Redis**: `RedisTimeoutException: Timeout awaiting response ... timeout is 500ms, command=UNLINK/HMGET, qs: N, async-ops: N, mgr: 10 of 10 available` — significa que las ops se reencolan esperando reconexión; reconocerla por la firma, no por el mensaje genérico. Ojo: `healthChecks.AddRedis()` crea su **propio multiplexer con defaults** (sin el tuning de `Program.cs`) → `/health` puede comportarse distinto a la app durante la caída (tiempos de detección diferentes)
- Seed `/provider-states` en SQL Server real: requiere `SET IDENTITY_INSERT` on/off por tabla + conexión única abierta (sin eso → error 544/500); es idempotente (2ª ejecución también 200); `docker compose down -v` destruye la BD → reseed obligatorio en cada arranque del stack
- Exit codes del runner de caos (`run-chaos.ps1`): 0 = PASS / 1 = FAIL / 2 = error de suite (JSON inválido, experimento inexistente); verify con retry 5×5s (ventana ~50s) para absorber blips transitorios de Docker Desktop; el veredicto del experimento NO depende del exit code de la carga NBomber

**Rules for the future**
1. Verify empirically before writing (metric names, report formats, config semantics)
2. Measure real runtimes before setting CI timeouts; set coverage thresholds from real numbers
3. Reset static state in tests; resolve eagerly any singleton registering in a global provider
4. Check package release status before adoption; document prerelease exceptions
5. Document known limits (unkillable mutants, rate-limit caveats) instead of hacking around them
6. Write boundary-exact tests (`>=`/`<`, exception messages, both RNG branches deterministically)
7. Tools needing a real socket (Pact FFI): real process + free port + cleanup in `finally`
8. Scripts: explicit fallbacks (0/false + WARN), `mktemp`/`mv` atomicity, `LC_NUMERIC=C`
9. Defensive .gitignore: local run artifacts (`reports/`, `StrykerOutput/`) in root, not nested tool-generated ones
10. Verify FAIL con status 0: discriminar rig vs SUT con grupo control, logs como prueba de vida y revisión de procesos apilados antes de tocar código (ver metodología en Fase 6)

## Dependabot

Weekly NuGet updates (grouped by `Microsoft.EntityFrameworkCore*` and `Microsoft.AspNetCore*`/`Microsoft.Extensions*`), monthly GitHub Actions updates.
