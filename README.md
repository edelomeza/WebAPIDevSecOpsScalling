# WebAPIDevSecOps

> **This project applies DevSecOps best practices** throughout its lifecycle: static analysis (SAST), dynamic testing (DAST), dependency scanning, SBOM generation, container scanning, automated security testing, and a CI/CD pipeline with security built into every stage.

![Build](https://img.shields.io/badge/build-passing-brightgreen)
![License](https://img.shields.io/badge/license-MIT-green)

Scalable REST API built with ASP.NET Core 10: JWT authentication, Argon2id hashing, Redis distributed cache, event-driven messaging, and SQL Server — backed by a full DevSecOps pipeline with quality assurance (QA) and metrics.

> For detailed development conventions, test quirks, and quick commands, see [AGENTS.md](AGENTS.md).

---

## About

| Aspect | Detail |
|---|---|
| **Scalability (Stateless)** | Horizontally scalable architecture: shared state lives in Redis instead of in-memory (token blacklist, login attempts, lockouts, and cache are distributed across instances) |
| **Distributed Cache** | Key-value cache-aside on Redis (`StackExchangeRedis`), per-key TTLs (30–120s), invalidation on writes, in-memory fallback if Redis is down |
| **Message Queues** | Choreographed sales saga with MassTransit (InMemory transport locally): 7 events, 4 consumers, flow pedido → stock → pago → factura with compensation |
| **Metrics** | OpenTelemetry + Prometheus (`/metrics`): test coverage, mutation score, SonarCloud quality gate, P95 latency — visualized in a Grafana dashboard |
| **QA Implementation** | 6 test suites (unit 596, integration 352, security 136, database, contract Pact, mutation Stryker), performance testing (NBomber), fuzzing (RESTler), quality gates enforced in CI |

---

## Tech Stack

| Category | Technologies |
|---|---|
| **Runtime** | .NET 10, ASP.NET Core 10 |
| **Database** | SQL Server (EF Core) / InMemory (development and tests) |
| **Distributed Cache** | Redis via StackExchange (IDistributedCache) |
| **Messaging** | MassTransit (InMemory transport, saga events) |
| **Authentication** | JWT Bearer (HMAC-SHA256), refresh tokens, 2FA TOTP |
| **Hashing** | Argon2id (Konscious) + BCrypt fallback |
| **API Docs** | OpenAPI + Scalar UI |
| **Logging** | Serilog (JSON files + console) |
| **Resilience** | Polly (circuit breaker) |
| **Observability** | OpenTelemetry + Prometheus exporter + Grafana |
| **Validation** | FluentValidation |
| **Testing** | xUnit, Moq, FluentAssertions, FsCheck, Testcontainers, Pact, Stryker.NET, NBomber, RESTler |
| **Static Analysis** | SonarAnalyzer, SonarCloud, Semgrep |
| **Container Security** | Trivy, Dockle, Cosign (keyless signing) |

---

## Security Features

| Feature | Detail |
|---|---|
| **JWT** | 60 min token, HMAC-SHA256, clock skew zero, issuer/audience validation, `ValidAlgorithms` enforced (prevents algorithm confusion) |
| **Password** | Argon2id (64 MB memory, 3 iterations) + BCrypt fallback |
| **Rate Limiting** | Global 1000 req/min, Login 5 req/5min, 2FA verify 10 req/5min (sliding window), Admin 200 req/min, Concurrent writes 10 |
| **Token Blacklist** | Logout invalidates token immediately — stored in **Redis** (`blacklist:{jti}`), shared across instances; in-memory fallback if Redis is down |
| **Account Lockout** | 5 failed attempts lock the account for 15 minutes (`attempts:{user}` / `lockout:{user}` in Redis — shared across instances) |
| **Refresh Tokens** | 7-day tokens stored hashed, rotated on every use, revocable |
| **2FA (TOTP)** | Setup + verify endpoints, two-step login flow (temporal token + TOTP code) |
| **Object-Level Auth** | Ownership checks in services: users can only access/modify their own resources, Admins bypass (`ForbiddenAccessException` → 403) |
| **Security Headers** | CSP, HSTS (365 days, production only), X-Frame-Options, X-Content-Type-Options, X-XSS-Protection, Referrer-Policy, Permissions-Policy |
| **Exception Handling** | Middleware mapping exceptions to correct HTTP codes (409, 404, 403, 400, 500) |
| **Audit Trail** | Audit logging + SHA-256 hash chain (tamper-evident: any alteration breaks the chain) |
| **Assembly Integrity** | Startup check verifies assembly hash (`AssemblyIntegrity:ExpectedHash`) |
| **Request Timeout** | 60 seconds, configurable |
| **Kestrel Limits** | Max 1000 concurrent connections, 1 MB body size |
| **CORS** | Single allowed origin (https://localhost:5097) |
| **Anti-enumeration** | Fake hash to prevent timing attacks on login |

---

## Sales Saga (Event-Driven)

A choreographed saga runs in parallel to the legacy `VenVenta` CRUD (which is untouched). Orders flow through independent steps coordinated by events:

```
POST /api/v1/Ventas/pedido
         │
         ▼ 202 Accepted
┌─────────────────────────────┐
│  VentasPedidoService        │──▶ PedidoCreadoEvent
│  Creates VenPedido (Pendiente)
└─────────────────────────────┘
         ▼
┌─────────────────────────────┐
│  StockValidatorConsumer     │
│  Validates stock, discounts │
└─────────────────────────────┘
         ├── OK ──▶ StockValidadoEvent ──▶ PagoConsumer (90% success simulation)
         └── FAIL ─▶ StockRechazadoEvent ──▶ Rechazado (END)
                      ▼
┌─────────────────────────────┐
│  PagoConsumer               │
└─────────────────────────────┘
         ├── OK ──▶ PagoProcesadoEvent ──▶ FacturaConsumer (sequential folio F-{year}-{seq})
         └── FAIL ─▶ PagoRechazadoEvent ──▶ Compensation (release stock)
                      ▼
┌─────────────────────────────┐
│  FacturaConsumer            │
└─────────────────────────────┘
         ├── OK ──▶ FacturaGeneradoEvent ──▶ Facturado (END)
         └── FAIL ─▶ FacturaRechazadaEvent ──▶ Compensation (refund + release stock)
```

**Saga states:** `Pendiente | StockValidado | PagoProcesado | Facturado | Rechazado | Reembolsado`

| Event | Published by | Advances to |
|---|---|---|
| `PedidoCreadoEvent` | VentasPedidoService | Pendiente → Stock validation |
| `StockValidadoEvent` | StockValidatorConsumer | StockValidado |
| `StockRechazadoEvent` | StockValidatorConsumer | Rechazado (END) |
| `PagoProcesadoEvent` | PagoConsumer | PagoProcesado |
| `PagoRechazadoEvent` | PagoConsumer | Rechazado + release stock |
| `FacturaGeneradoEvent` | FacturaConsumer | Facturado (END) |
| `FacturaRechazadaEvent` | FacturaConsumer | Reembolsado (refund + release stock) |

**Consumers:** `StockValidatorConsumer`, `PagoConsumer`, `FacturaConsumer`, `CompensationConsumer`.

Transport is **MassTransit InMemory** for local development (no AWS dependency). AmazonSQS transport is planned for production.

---

## Redis Caching

Distributed key-value cache-aside on Redis via `IDistributedCache`:

- **HIT** → return from Redis (~1ms) | **MISS** → query SQL Server, store with TTL, return
- Writes invalidate affected keys (`RemoveAsync`)
- Fallback to `IMemoryCache` with warning log if Redis is unavailable

| Key | TTL | Purpose |
|---|---|---|
| `blacklist:{jti}` | Until JWT expiry | Token blacklist (shared across instances) |
| `attempts:{user}` | 30 min | Failed login attempts counter |
| `lockout:{user}` | 15 min | Account lockout |
| `cache:producto:{id}` / `cache:productos:page{p}:size{s}` | 60s / 30s | Product by ID / paginated list |
| `cache:cliente:{id}` / `cache:clientes:page{p}:size{s}` | 60s / 30s | Client by ID / paginated list |
| `cache:empleado:{id}` | 60s | Employee by ID |
| `cache:tipo-empleado:{id}` / `cache:tipo-empleado:list` | 120s | Employee type catalog |
| `cache:usuario:{id}` | 60s | User by ID (password never cached) |

---

## Observability

| Endpoint / Tool | Detail |
|---|---|
| `/metrics` | Prometheus metrics via OpenTelemetry exporter |
| `/health` | All health checks (SQL Server + Redis) |
| `/health/ready` | Database-only readiness check |
| `/health-ui` | Health checks UI |
| **Grafana** | `deploy/grafana/quality-dashboard.json` |

Exported gauges: `test_coverage_percent`, `mutation_score_percent`, `sonar_quality_gate_passed`, `p95_latency_ms` (populated by `scripts/collect-quality-metrics.sh` from SonarCloud / Stryker / NBomber reports).

---

## Quick Setup

```powershell
# 1. Copy configuration template
cp WebAPIDevSecOps/appsettings.Example.json WebAPIDevSecOps/appsettings.json

# 2. Edit appsettings.json with your local connection
#    Or use in-memory database for quick development:
#    Add "UseInMemoryDatabase": true in appsettings.json

# 3. Restore dependencies and run
dotnet restore
dotnet run --project WebAPIDevSecOps/WebAPIDevSecOps.csproj
```

**With Redis (recommended for cache + distributed features):**

```powershell
# Start Redis (and optionally the API) with Docker Compose
docker compose -f deploy/docker-compose.local.yml up -d
```

**Development URLs:**  
- `http://localhost:5196`  
- `https://localhost:7227`  
- API documentation: `/scalar`

---

## API Endpoints

| Method | Route | Auth | Description |
|---|---|---|---|
| `POST` | `/api/v1/login/login` | No | User authentication |
| `POST` | `/api/v1/login2fa/login` | No | 2FA login step 1 (temporal token) |
| `POST` | `/api/v1/login2fa/verify` | No | 2FA login step 2 (TOTP code) |
| `POST` | `/api/v1/auth/2fa/setup` | Authorize | Enable 2FA (TOTP secret + QR) |
| `POST` | `/api/v1/auth/2fa/verify` | Authorize | Verify TOTP code and enable 2FA |
| `POST` | `/api/v1/refresh/refresh` | No | Rotate refresh token → new JWT |
| `POST` | `/api/v1/logout/logout` | No | Invalidate JWT token (Redis blacklist) |
| `GET` | `/api/v1/test/secure` | Authorize | Auth smoke test |
| `GET` | `/api/v1/usuario` | AdminOnly | Paginated user list |
| `GET` | `/api/v1/usuario/{id}` | AdminOnly | Get user by ID |
| `GET` | `/api/v1/usuario/buscar` | AdminOnly | Search users by text |
| `GET` | `/api/v1/usuario/autocomplete` | AdminOnly | User autocomplete |
| `POST` | `/api/v1/usuario` | AdminOnly | Create user |
| `PUT` | `/api/v1/usuario/{id}` | AdminOnly | Update user |
| `DELETE` | `/api/v1/usuario/{id}` | AdminOnly | Delete user |
| `GET` | `/api/v1/cliente` | AdminOnly | Paginated client list |
| `GET` | `/api/v1/cliente/{id}` | AdminOnly | Get client by ID |
| `GET` | `/api/v1/cliente/buscar` | AdminOnly | Search clients by text |
| `GET` | `/api/v1/cliente/autocomplete` | AdminOnly | Client autocomplete |
| `POST` | `/api/v1/cliente` | AdminOnly | Create client |
| `PUT` | `/api/v1/cliente/{id}` | AdminOnly | Update client |
| `DELETE` | `/api/v1/cliente/{id}` | AdminOnly | Delete client |
| `GET` | `/api/v1/tipoempleado` | AdminOnly | Employee type catalog |
| `GET` | `/api/v1/tipoempleado/{id}` | AdminOnly | Employee type by ID |
| `GET` | `/api/v1/empleado` | AdminOnly | Paginated employee list |
| `GET` | `/api/v1/empleado/{id}` | AdminOnly | Get employee by ID |
| `GET` | `/api/v1/empleado/buscar` | AdminOnly | Search employees by text |
| `POST` | `/api/v1/empleado` | AdminOnly | Create employee |
| `PUT` | `/api/v1/empleado/{id}` | AdminOnly | Update employee |
| `DELETE` | `/api/v1/empleado/{id}` | AdminOnly | Delete employee |
| `GET` | `/api/v1/producto` | AdminOnly | Paginated product list |
| `GET` | `/api/v1/producto/{id}` | AdminOnly | Get product by ID |
| `GET` | `/api/v1/producto/buscar` | AdminOnly | Search products by text |
| `POST` | `/api/v1/producto` | AdminOnly | Create product |
| `PUT` | `/api/v1/producto/{id}` | AdminOnly | Update product |
| `DELETE` | `/api/v1/producto/{id}` | AdminOnly | Delete product |
| `GET` | `/api/v1/estadoventa` | AdminOnly | Sale status catalog |
| `GET` | `/api/v1/estadoventa/{id}` | AdminOnly | Sale status by ID |
| `GET` | `/api/v1/venta` | AdminOnly | Paginated sale list |
| `GET` | `/api/v1/venta/{id}` | AdminOnly | Get sale by ID |
| `GET` | `/api/v1/venta/buscar` | AdminOnly | Search sales by folio/client |
| `POST` | `/api/v1/venta` | AdminOnly | Create sale |
| `GET` | `/api/v1/ventadetalle` | AdminOnly | Paginated detail list |
| `GET` | `/api/v1/ventadetalle/{id}` | AdminOnly | Get detail by ID |
| `GET` | `/api/v1/ventadetalle/buscarproducto` | AdminOnly | Product autocomplete for sales |
| `POST` | `/api/v1/ventadetalle` | AdminOnly | Add product to sale |
| `POST` | `/api/v1/Ventas/pedido` | Authorize | Create sales order (202 Accepted, saga starts) |
| `GET` | `/api/v1/Ventas/pedido/{id}` | Authorize | Get order + saga state |
| `GET` | `/api/v1/Ventas/pedido` | Authorize | Paginated order list |
| `GET` | `/api/v1/Ventas/pago/{id}` | Authorize | Get payment by ID |
| `GET` | `/api/v1/Ventas/pago` | Authorize | Paginated payment list |
| `GET` | `/api/v1/Ventas/factura/{id}` | Authorize | Get invoice by ID |
| `GET` | `/api/v1/Ventas/factura` | Authorize | Paginated invoice list |
| `GET` | `/health` | No | Full health checks |
| `GET` | `/health/ready` | No | Database-only health check |
| `GET` | `/health-ui` | No | Health checks UI |
| `GET` | `/metrics` | No | Prometheus metrics |

---

## Data Model

| Entity | Table | Description |
|---|---|---|
| `SegUsuario` | `SegUsuario` | Application users (login, roles) |
| `CliCliente` | `CliCliente` | Client catalog |
| `EmpCatTipoEmpleado` | `EmpCatTipoEmpleado` | Employee type catalog |
| `EmpEmpleado` | `EmpEmpleado` | Employee catalog |
| `ProProducto` | `ProProducto` | Product catalog |
| `VenCatEstado` | `VenCatEstado` | Sale status catalog |
| `VenVenta` | `VenVenta` | Sales header (legacy CRUD, untouched) |
| `VenVentaDetalle` | `VenVentaDetalle` | Sales detail lines (legacy CRUD, untouched) |
| `VenPedido` | `VenPedido` | Saga order header (Guid PK) |
| `VenPedidoDetalle` | `VenPedidoDetalle` | Saga order lines |
| `VenPedidoPago` | `VenPedidoPago` | Saga payment record |
| `VenPedidoFactura` | `VenPedidoFactura` | Saga invoice record |

---

## Project Structure

```
WebAPIDevSecOps/
├── WebAPIDevSecOps/            # Main API
│   ├── Program.cs               # Entrypoint and middleware pipeline
│   ├── appsettings.json         # Local configuration (not versioned)
│   ├── appsettings.Example.json # Configuration template (versioned)
│   ├── Controllers/             # API endpoints (17 files, incl. saga + auth)
│   ├── Services/                # Business logic (incl. saga + cache + metrics)
│   ├── Interfaces/              # Service contracts
│   ├── Events/                  # Saga event contracts (7 events)
│   ├── Consumers/               # MassTransit consumers (4)
│   ├── Middleware/              # Correlation ID, security headers, CSP nonce,
│   │                            # audit logging (+ hash chain), timeout, exceptions
│   ├── Context/                 # EF Core DbContext
│   ├── Models/                  # Database entities (12 entities)
│   ├── Dto/                     # Request/Response models + Validators
│   └── Migrations/              # EF Core migrations
├── UnitTest/                    # Unit tests (596 tests)
├── IntegrationTest/             # Integration tests (352 tests)
├── SecurityTest/                # Security tests (136 tests)
├── DatabaseTest/                # DB migration tests (Testcontainers SQL Server)
├── ContractTest/                # Contract tests (Pact)
├── MutationTest/                # Mutation testing (Stryker.NET)
├── PerformanceTest/             # Performance tests (NBomber)
├── fuzzing/                     # RESTler fuzzing config + helpers
├── deploy/                      # Docker Compose local + Grafana dashboard
├── scripts/                     # Coverage check + quality metrics collector
└── .semgrep/                    # Custom Semgrep rules
```

---

## CI/CD Pipeline

```
push → build-and-test → docker-build (+Cosign) → dockle → sonarcloud
     → database-test → mutation-test → contract-test → semgrep
     → fuzz (RESTler) → DAST (ZAP) ×2 → PR Quality Gate
```

| Stage | Tool | What it checks |
|---|---|---|
| **Build & Test** | xUnit + Moq + FluentAssertions | Unit (596), integration (352), security (136) tests, vulnerable dependencies, SBOM (CycloneDX) |
| **Docker Build** | Docker + Cosign | Build and push image, keyless sign, verify signature, Trivy scan (HIGH/CRITICAL blocks) |
| **Container Lint** | Dockle | Dockerfile/container best practices |
| **SAST** | SonarCloud + Semgrep | Static analysis + custom rules; coverage threshold ≥ 45% |
| **Database Test** | Testcontainers | Migrations apply (12 tables), rollback, seed data |
| **Mutation Test** | Stryker.NET | Mutation score as real quality gate |
| **Contract Test** | Pact | Provider contract verification against consumer expectations |
| **Fuzzing** | RESTler | Automated API fuzzing from OpenAPI spec |
| **DAST** | OWASP ZAP | Dynamic attacks using OpenAPI spec (push + PR) |
| **PR Quality Gate** | GitHub Script | Verifies build-and-test + semgrep, comments results on PR |

---

## Testing

```powershell
# Unit tests
dotnet test UnitTest/UnitTest.csproj

# Integration tests
dotnet test IntegrationTest/IntegrationTest.csproj

# Security tests
dotnet test SecurityTest/SecurityTest.csproj

# Database tests (requires Docker for Testcontainers)
dotnet test DatabaseTest/DatabaseTest.csproj

# Contract tests
dotnet test ContractTest/ContractTest.csproj

# Mutation testing (long-running)
dotnet stryker --project WebAPIDevSecOps/WebAPIDevSecOps.csproj

# Performance tests
dotnet run --project PerformanceTest/PerformanceTest.csproj

# All tests
dotnet test UnitTest/UnitTest.csproj
dotnet test IntegrationTest/IntegrationTest.csproj
dotnet test SecurityTest/SecurityTest.csproj
```

Additional QA techniques:
- **Property-based testing** (FsCheck): random CRUD sequences validated for transactional integrity
- **Race conditions**: parallel writes tested (5 concurrent sales → only 1 succeeds)
- **Recovery**: Redis failure fallback, circuit breaker open/half-open/closed tests

---

## Configuration

Key sections in `appsettings.json`:

| Section | Purpose |
|---|---|
| `ConnectionStrings:DefaultConnection` | SQL Server connection string |
| `UseInMemoryDatabase` | Use EF Core InMemory instead of SQL Server (dev/tests) |
| `Jwt` | Key (min 32 bytes), Issuer, Audience |
| `PasswordHashing` | MemorySize, Iterations, DegreeOfParallelism |
| `Resilience` | Circuit breaker parameters |
| `RequestTimeoutSeconds` | Global request timeout |
| `Observability:ConsoleExport` | Gate for console OTel exporters (off by default) |
| `QualityMetrics` | TestCoveragePercent, MutationScore, SonarQualityGatePassed, P95LatencyMs |
| `AssemblyIntegrity:ExpectedHash` | Expected assembly hash for startup integrity check |
| `HealthChecksUI` | Health checks UI endpoints and evaluation settings |

Database credentials can be overridden via environment variables: `DB_USER` and `DB_PASSWORD`.

---

## Database Migrations

```powershell
# Add a new migration
dotnet ef migrations add MigrationName --project WebAPIDevSecOps --context AppDbContext

# Apply migrations to database
dotnet ef database update --project WebAPIDevSecOps --context AppDbContext

# Remove last migration (if not applied)
dotnet ef migrations remove --project WebAPIDevSecOps --context AppDbContext
```

---

## License

MIT
