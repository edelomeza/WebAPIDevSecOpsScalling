# MemoriaPlan — WebAPIDevSecOps → AWS Saga Escalable

## Estado Actual
- Rama: `PrimeraEtapa` ✅ Creada
- Inicio: Julio 2026
- Objetivo: Desarrollo local primero → Deploy temporal AWS Free Tier → Destrucción

---

## 1. Objetivo Global

Transformar la API actual (monolito síncrono con estado en memoria) en una arquitectura **stateless, escalable horizontalmente y orientada a eventos**, utilizando Redis como cache distribuido y MassTransit + SQS para un saga coreográfica de ventas.

### Enfoque

- **Paralelismo**: Saga nuevo (`VenPedido`) no toca el CRUD legacy (`VenVenta`)
- **Stateless**: `ConcurrentDictionary` + `IMemoryCache` → Redis
- **Event-Driven**: MassTransit con transporte InMemory (local) y AmazonSQS (AWS)
- **DevSecOps**: JWT, rate limiting, security headers, Argon2id, análisis estático
- **Learn by doing**: Manual primero (AWS Console) → automatizar después (CloudFormation)
- **Temporal**: Despliegue en AWS Free Tier para validación, luego destrucción

---

## 2. Diagrama de Arquitectura Objetivo

```
                         ┌──────────────────┐
                         │   Web Pública     │
                         │   (Frontend)      │
                         └────────┬─────────┘
                                  │ HTTP :80
                                  ▼
                         ┌──────────────────┐
                         │   ALB (AWS)       │
                         │  Internet-facing  │
                         └────────┬─────────┘
                                  │ :8080
                    ┌─────────────┼─────────────┐
                    │             │             │
                    ▼             ▼             ▼
            ┌────────────┐ ┌────────────┐ ┌────────────┐
            │  EC2 #1    │ │  EC2 #2    │ │  EC2 #3    │
            │  API .NET  │ │  API .NET  │ │  API .NET  │
            │  + Redis   │ │  + Redis   │ │  + Redis   │
            └─────┬──────┘ └─────┬──────┘ └─────┬──────┘
                  │              │              │
        ┌─────────┴──────────────┴──────────────┴─────────┐
        │                                                 │
        ▼                                                 ▼
┌─────────────────┐                               ┌────────────────┐
│  SQL Server     │                               │  AWS SQS       │
│  (Hosting       │                               │  FIFO Queues   │
│   Externo)      │                               │  4 colas + DLQ │
└─────────────────┘                               └────────────────┘
                                                          │
                                                          ▼
                                                  ┌────────────────┐
                                                  │  CloudWatch    │
                                                  │  Logs + Alarmas│
                                                  └────────────────┘
```

---

## 3. Estructura Final del Proyecto

```
WebAPIDevSecOps/
├── Controllers/
│   ├── VentaController.cs              ← Sin cambios
│   ├── VentaDetalleController.cs        ← Sin cambios
│   ├── ClienteController.cs             ← Sin cambios
│   ├── ProductoController.cs            ← Sin cambios
│   ├── EmpleadoController.cs            ← Sin cambios
│   ├── TipoEmpleadoController.cs        ← Sin cambios
│   ├── UsuarioController.cs             ← Sin cambios
│   ├── EstadoVentaController.cs         ← Sin cambios
│   ├── LoginController.cs              ← Sin cambios
│   ├── LogoutController.cs             ← Sin cambios
│   ├── TestController.cs               ← Sin cambios
│   ├── VentasPedidoController.cs       ← NUEVO
│   ├── VentasPagoController.cs         ← NUEVO
│   ├── VentasFacturaController.cs      ← NUEVO
│   └── VentasDashboardController.cs    ← NUEVO
├── Services/
│   ├── TokenBlacklist.cs               ← ELIMINAR
│   ├── TokenBlacklistService.cs        ← NUEVO
│   ├── CacheService.cs                 ← NUEVO
│   ├── LoginService.cs                 ← MODIFICADO (IMemoryCache → IDistributedCache)
│   ├── VentasPedidoService.cs          ← NUEVO
│   ├── PagoService.cs                  ← NUEVO
│   ├── FacturaService.cs               ← NUEVO
│   ├── CompensationService.cs          ← NUEVO
│   ├── DashboardService.cs             ← NUEVO
│   └── ... (resto sin cambios)
├── Interfaces/
│   ├── ITokenBlacklistService.cs       ← NUEVO
│   ├── ICacheService.cs                ← NUEVO
│   ├── IVentasPedidoService.cs         ← NUEVO
│   ├── IPagoService.cs                 ← NUEVO
│   ├── IFacturaService.cs              ← NUEVO
│   ├── ICompensationService.cs         ← NUEVO
│   ├── IDashboardService.cs            ← NUEVO
│   └── ... (resto sin cambios)
├── Models/
│   ├── SegTokenBlacklist.cs            ← ELIMINAR
│   ├── VenPedido.cs                    ← NUEVO
│   ├── VenPedidoDetalle.cs             ← NUEVO
│   ├── VenPedidoPago.cs                ← NUEVO
│   ├── VenPedidoFactura.cs             ← NUEVO
│   └── ... (resto sin cambios)
├── Events/                             ← NUEVO
│   ├── PedidoCreadoEvent.cs
│   ├── StockValidadoEvent.cs
│   ├── StockRechazadoEvent.cs
│   ├── PagoProcesadoEvent.cs
│   ├── PagoRechazadoEvent.cs
│   ├── FacturaGeneradoEvent.cs
│   └── FacturaRechazadaEvent.cs
├── Consumers/                          ← NUEVO
│   ├── StockValidatorConsumer.cs
│   ├── PagoConsumer.cs
│   ├── FacturaConsumer.cs
│   └── CompensationConsumer.cs
├── Middleware/
│   ├── CorrelationIdMiddleware.cs      ← NUEVO
│   ├── SecurityHeadersMiddleware.cs    ← NUEVO
│   ├── CspNonceMiddleware.cs           ← NUEVO
│   ├── RequestTimeoutMiddleware.cs     ← Existente
│   ├── AuditLoggingMiddleware.cs       ← Existente
│   └── ExceptionHandlingMiddleware.cs  ← Existente
├── Dto/
│   ├── PedidoCreateDto.cs              ← NUEVO
│   ├── PedidoResponseDto.cs            ← NUEVO
│   ├── PagoDto.cs                      ← NUEVO
│   ├── FacturaDto.cs                   ← NUEVO
│   ├── DashboardDto.cs                 ← NUEVO
│   └── ... (37 DTOs existentes)
├── Program.cs                          ← MODIFICADO
├── Context/AppDbContext.cs             ← MODIFICADO (+4 DbSets)
└── WebAPIDevSecOps.csproj              ← MODIFICADO (+ NuGets)
```

---

## 4. Modelos de Datos

### Tablas Existentes (NO se modifican)

```sql
VenVenta              ← CRUD legacy, intacto
  id (int PK), idCliCliente, idSegUsuario, idVenCatEstado,
  dteFechaHoraCompra, strClaveVenta, RowVersion

VenVentaDetalle       ← CRUD legacy, intacto
  id (int PK), idVenVenta, idProProducto, intPiezaVenta, decTotalVenta, RowVersion

VenCatEstado, CliCliente, ProProducto, SegUsuario, EmpEmpleado, EmpCatTipoEmpleado
  ← Todos intactos
```

### Tablas Nuevas (Saga — VenPedido)

```sql
VenPedido             ← Pedido del saga (Guid PK, paralelo a VenVenta)
├── id                  uniqueidentifier (Guid) PK
├── idCliCliente        int             FK → CliCliente
├── dteFechaPedido      datetime2       NOT NULL
├── decTotal            decimal(18,2)   NOT NULL
├── strEstadoSaga       nvarchar(50)    NOT NULL
│                       -- Pendiente | StockValidado | PagoProcesado
│                       -- | Facturado | Rechazado | Reembolsado
├── strMotivoRechazo    nvarchar(500)   NULL
└── RowVersion          timestamp       concurrencia

VenPedidoDetalle      ← Líneas del pedido
├── id                  int             PK (identity)
├── idVenPedido         uniqueidentifier FK → VenPedido
├── idProProducto       int             FK → ProProducto
├── intCantidad         int             NOT NULL
├── decPrecioUnitario   decimal(18,2)   NOT NULL
└── RowVersion          timestamp       concurrencia

VenPedidoPago         ← Pago asociado al pedido
├── id                  int             PK (identity)
├── idVenPedido         uniqueidentifier FK → VenPedido
├── decMonto            decimal(18,2)   NOT NULL
├── strMetodoPago       nvarchar(50)    NULL
├── strIdTransaccion    nvarchar(100)   NULL (unique index)
├── strEstado           nvarchar(20)    NOT NULL (Autorizado|Rechazado|Reembolsado)
├── dteFechaPago        datetime2       NOT NULL
└── RowVersion          timestamp       concurrencia

VenPedidoFactura      ← Factura del pedido
├── id                  int             PK (identity)
├── idVenPedido         uniqueidentifier FK → VenPedido
├── strFolioFactura     nvarchar(50)    NOT NULL (unique)
├── strRFC              nvarchar(13)    NULL
├── decTotal            decimal(18,2)   NOT NULL
├── dteFechaEmision     datetime2       NOT NULL
├── strEstado           nvarchar(20)    NOT NULL (Emitida|Cancelada)
└── RowVersion          timestamp       concurrencia
```

### Diagrama de Relaciones

```
CliCliente ──┐
             ├── VenVenta (legacy, int PK, NO se toca)
             │
             └── VenPedido (saga, Guid PK)
                      │
                      ├── VenPedidoDetalle ── ProProducto
                      ├── VenPedidoPago
                      └── VenPedidoFactura
```

---

## 5. Flujo del Saga (Eventos y Estado)

```
POST /api/v1/Ventas/pedido
         │
         ▼ 202 Accepted
┌─────────────────────────────┐
│  VentasPedidoService        │─── publica ──▶ PedidoCreadoEvent
│  Crea VenPedido             │                (SQS: pedidos.fifo)
│  strEstadoSaga = Pendiente  │
└─────────────────────────────┘
         │
         ▼
┌─────────────────────────────┐
│ StockValidatorConsumer      │
│  Valida existencias         │
│  Descuenta stock si OK      │
└─────────────────────────────┘
         │
         ├── ÉXITO ──▶ StockValidadoEvent ──▶ strEstadoSaga = StockValidado
         │                 (SQS: pedidos-pago.fifo)
         │
         └── FALLA ──▶ StockRechazadoEvent ──▶ strEstadoSaga = Rechazado (FIN)
         │
         ▼ (solo si éxito)
┌─────────────────────────────┐
│  PagoConsumer               │
│  Simula pasarela (90% OK)   │
└─────────────────────────────┘
         │
         ├── ÉXITO ──▶ PagoProcesadoEvent ──▶ strEstadoSaga = PagoProcesado
         │                 (SQS: pedidos-factura.fifo)
         │
         └── FALLA ──▶ PagoRechazadoEvent ──▶ Compensation (libera stock)
         │                                    strEstadoSaga = Rechazado
         ▼ (solo si éxito)
┌─────────────────────────────┐
│  FacturaConsumer            │
│  Genera folio secuencial    │
└─────────────────────────────┘
         │
         ├── ÉXITO ──▶ FacturaGeneradoEvent ──▶ strEstadoSaga = Facturado (FIN)
         │
         └── FALLA ──▶ FacturaRechazadaEvent ──▶ Compensation
                                                   (reembolsa pago + libera stock)
                                                   strEstadoSaga = Reembolsado
```

---

## 6. Costos AWS Estimados

| Servicio | Uso | Costo | Free Tier |
|----------|-----|-------|-----------|
| EC2 t2.micro | ~5 días (120h) | $0 | ✅ 750h/mes gratis |
| ALB | ~5 días (120h) | ~$2.70 | ❌ $0.0225/h |
| SQS | ~3 días | $0 | ✅ 1M requests/mes |
| CloudWatch Logs | ~5 días | $0 | ✅ 5GB gratis |
| CloudWatch Alarmas | 4 alarmas | $0 | ✅ 10 gratis |
| SNS | Notificaciones | $0 | ✅ 1M publicaciones |
| VPC / IAM / SG | ~5 días | $0 | ✅ Siempre gratis |
| Data Transfer | ~1GB | $0 | ✅ 1er GB gratis |
| **Total** | | **~$2.70 USD** | Cubierto por $300 créditos |

---

## 7. Etapas de Implementación

---

### Etapa 0 — Infraestructura Local y DX

**Objetivo:** Tener entorno local listo con Redis en Docker.
**Aprendizaje:** Docker Compose multi-servicio, conexión BD externa.

| Paso | Actividad | Archivo | Estado |
|------|-----------|---------|--------|
| 0.1 | Crear rama `PrimeraEtapa` | — | ✅ |
| 0.2 | Crear `docker-compose.local.yml` con redis + api | `deploy/docker-compose.local.yml` | ✅ |
| 0.3 | Agregar `appsettings.Development.json` con `Redis:ConnectionString` | `appsettings.Development.json` | ✅ |
| 0.4 | Crear directorios `Events/` y `Consumers/` | — | ✅ |
| 0.5 | Verificar compilación: `dotnet build` | — | ✅ |
| 0.6 | Verificar tests existentes | `dotnet test` | ✅ |

```yaml
# deploy/docker-compose.local.yml
services:
  redis:
    image: redis:7-alpine
    ports: ["6379:6379"]
    command: redis-server --maxmemory 512mb --maxmemory-policy allkeys-lru
  api:
    build:
      context: ..
      dockerfile: Dockerfile
    ports: ["8080:8080"]
    depends_on: [redis]
    environment:
      - ConnectionStrings:DefaultConnection=...
      - Redis:ConnectionString=redis:6379
```

---

### Etapa 1 — Stateless con Redis

**Objetivo:** Migrar `ConcurrentDictionary` + `IMemoryCache` a Redis.
**Aprendizaje:** Cache local vs distribuido, escalado horizontal, expiración automática.

#### Diagrama

```
Antes (NO escalable):
  EC2 #1 ── ConcurrentDictionary (tokens A, B, C)
  EC2 #2 ── ConcurrentDictionary (tokens D, E, F)
  → Token blacklist NO compartida entre instancias

Después (Escalable):
  EC2 #1 ──┐
            ├── Redis (blacklist:{jti}, attempts:{user}, lockout:{user})
  EC2 #2 ──┘
  → Todas las instancias comparten el mismo estado
```

| Paso | Actividad | Archivo | Estado |
|------|-----------|---------|--------|
| 1.1 | Agregar `StackExchangeRedis` NuGet | `.csproj` | ✅ |
| 1.2 | `AddMemoryCache()` → `AddStackExchangeRedisCache()` | `Program.cs:262` | ✅ |
| 1.3 | Agregar `.AddRedis()` health check | `Program.cs:118` | ✅ |
| 1.4 | Crear `ITokenBlacklistService` | `Interfaces/ITokenBlacklistService.cs` | ✅ |
| 1.5 | Crear `TokenBlacklistService` (IDistributedCache, clave blacklist:{jti}) | `Services/TokenBlacklistService.cs` | ✅ |
| 1.6 | Eliminar `Services/TokenBlacklist.cs` | — | ✅ |
| 1.7 | Eliminar `Models/SegTokenBlacklist.cs` | — | ✅ |
| 1.8 | Eliminar `DbSet<SegTokenBlacklist>` de AppDbContext | `Context/AppDbContext.cs` | ✅ |
| 1.9 | Eliminar `TokenBlacklist.Initialize()` de Program.cs | `Program.cs:335` | ✅ |
| 1.10 | Middleware token: `ITokenBlacklistService` inyectado | `Program.cs:366-377` | ✅ |
| 1.11 | Migrar LoginService: `IMemoryCache` → `IDistributedCache` | `Services/LoginService.cs` | ✅ |
| 1.12 | Actualizar tests Login | `UnitTest/Login/` | ✅ |
| 1.13 | Crear tests TokenBlacklistService | `UnitTest/Login/TokenBlacklistServiceTests.cs` | ✅ |
| 1.14 | Ejecutar y verificar tests | `dotnet test UnitTest` | ✅ |

> **Nota 1.2:** Reemplaza el caché en memoria local (IMemoryCache) por Redis distribuido (IDistributedCache), permitiendo que todas las instancias EC2 compartan el mismo estado de caché (blacklist, intentos de login, etc.).

> **Nota 1.4:** Abstrae el blacklist de tokens JWT usando IDistributedCache (Redis) en lugar de la clase estática TokenBlacklist. Métodos: AddAsync(jti, expiry) e IsBlacklistedAsync(jti). Permite que el blacklist sea compartido entre instancias EC2 al estar en Redis.

> **Nota 1.5:** Implementa ITokenBlacklistService usando IDistributedCache (Redis) con clave `blacklist:{jti}`. AddAsync guarda el JTI en Redis con TTL hasta expiración del token; IsBlacklistedAsync verifica si existe.

> **Nota 1.9:** Eliminado TokenBlacklist.Initialize() — se reemplaza por inyección de ITokenBlacklistService.

> **Nota 1.10:** Reemplaza el middleware inline que usaba TokenBlacklist.IsBlacklisted(token) por una versión que inyecta ITokenBlacklistService vía context.RequestServices. Cambios: (1) arregla compilación, (2) cambia de estático a inyectado, (3) extrae el JTI del token, (4) convierte a async.

> **Nota 1.11:** Migra LoginService de IMemoryCache a IDistributedCache. Con Redis, el conteo de intentos fallidos y bloqueos es compartido entre instancias EC2. Claves: `attempts:{user}` (TTL 30 min) y `lockout:{user}` (TTL 15 min). TryGetValue → GetStringAsync, Set → SetStringAsync, Remove → RemoveAsync.

---

### Etapa 2 — Cache Distribuido (Cache-Aside)

**Objetivo:** Cachear respuestas CRUD en Redis.
**Aprendizaje:** Patrón Cache-Aside, TTL, invalidación.

#### Diagrama

```
GET /api/productos/5
         │
         ▼
   ┌──────────┐
   │  Redis?   │
   │ cache:producto:5
   └─────┬────┘
         │
    ┌────┴────┐
    │  HIT    │  ───▶ Devolver JSON (1ms)
    └─────────┘
         │ MISS
         ▼
   ┌──────────┐
   │ Factory   │  ───▶ SQL Server (50ms)
   │ GetById() │
   └─────┬────┘
         │
         ▼
   ┌──────────┐
   │ Redis SET │  ───▶ TTL 60s + Return
   └──────────┘
```

| Paso | Actividad | Clave Redis | TTL | Estado |
|------|-----------|-------------|-----|--------|
| 2.1 | Crear `ICacheService` + `CacheService` | Genérico | Configurable | 🔲 |
| 2.2 | Registrar `ICacheService` en DI | — | — | 🔲 |
| 2.3 | Envolver `ProductoService` GETs | `cache:producto:{id}` | 60s | 🔲 |
| 2.4 | Envolver `ProductoService` listas | `cache:productos:page{p}:size{s}` | 30s | 🔲 |
| 2.5 | Invalidar `ProductoService` writes | `RemoveAsync` | — | 🔲 |
| 2.6 | Envolver `ClienteService` GETs | `cache:cliente:{id}` | 60s | 🔲 |
| 2.7 | Envolver `ClienteService` listas | `cache:clientes:page{p}:size{s}` | 30s | 🔲 |
| 2.8 | Envolver `EmpleadoService` GETs | `cache:empleado:{id}` | 60s | 🔲 |
| 2.9 | Envolver `TipoEmpleadoService` GETs | `cache:tipo-empleado:{id}` | 120s | 🔲 |
| 2.10 | Envolver `TipoEmpleadoService` listas | `cache:tipo-empleado:list` | 120s | 🔲 |
| 2.11 | Envolver `UsuarioService` GETs (sin strPWD) | `cache:usuario:{id}` | 60s | 🔲 |
| 2.12 | Agregar `[ResponseCache]` en controllers GETs | Controllers | 30s | 🔲 |
| 2.13 | Crear `CacheServiceTests` | `UnitTest/Services/CacheServiceTests.cs` | — | 🔲 |
| 2.14 | Ejecutar tests | `dotnet test` | — | 🔲 |

---

### Etapa 3 — Modelos Saga + BD

**Objetivo:** Crear 4 tablas nuevas sin modificar existentes.
**Aprendizaje:** Guid PK, migraciones EF Core, relaciones.

| Paso | Actividad | Archivo | Estado |
|------|-----------|---------|--------|
| 3.1 | Crear `VenPedido.cs` | `Models/VenPedido.cs` | 🔲 |
| 3.2 | Crear `VenPedidoDetalle.cs` | `Models/VenPedidoDetalle.cs` | 🔲 |
| 3.3 | Crear `VenPedidoPago.cs` | `Models/VenPedidoPago.cs` | 🔲 |
| 3.4 | Crear `VenPedidoFactura.cs` | `Models/VenPedidoFactura.cs` | 🔲 |
| 3.5 | Agregar 4 DbSets en AppDbContext | `Context/AppDbContext.cs` | 🔲 |
| 3.6 | Configurar FKs (Restrict), unique indexes | `Context/AppDbContext.cs` | 🔲 |
| 3.7 | `dotnet ef migrations add SagaVentas` | Migración | 🔲 |
| 3.8 | Verificar migración genera SQL correcto | — | 🔲 |

---

### Etapa 4 — Eventos del Saga

**Objetivo:** Definir contratos de mensajería.
**Aprendizaje:** Diseño de eventos, serialización.

| Paso | Evento | Propiedades | Estado |
|------|--------|-------------|--------|
| 4.1 | `PedidoCreadoEvent` | PedidoId (Guid), ClienteId, Total, Detalles[], FechaCreacion | 🔲 |
| 4.2 | `StockValidadoEvent` | PedidoId | 🔲 |
| 4.3 | `StockRechazadoEvent` | PedidoId, Motivo | 🔲 |
| 4.4 | `PagoProcesadoEvent` | PedidoId, IdTransaccion, Monto | 🔲 |
| 4.5 | `PagoRechazadoEvent` | PedidoId, Motivo | 🔲 |
| 4.6 | `FacturaGeneradoEvent` | PedidoId, FolioFactura | 🔲 |
| 4.7 | `FacturaRechazadaEvent` | PedidoId, Motivo | 🔲 |

---

### Etapa 5 — Servicios Saga

**Objetivo:** Implementar lógica de negocio del saga (pedidos, pagos, facturas, compensación).
**Aprendizaje:** Servicios desacoplados, compensación (rollback), simulación de integraciones.

| Paso | Servicio | Métodos clave | Lógica | Estado |
|------|----------|---------------|--------|--------|
| 5.1 | `VentasPedidoService` | `CrearPedidoAsync` | Valida cliente+productos, calcula total, crea VenPedido+Detalles, publica evento | 🔲 |
| 5.2 | `PagoService` | `ProcesarPagoAsync`, `ReembolsarPagoAsync` | Simula 90% éxito, crea VenPedidoPago | 🔲 |
| 5.3 | `FacturaService` | `GenerarFacturaAsync`, `CancelarFacturaAsync` | Folio F-{año}-{seq} desde Redis, crea VenPedidoFactura | 🔲 |
| 5.4 | `CompensationService` | CompensarPorPagoRechazado, CompensarPorFacturaRechazada | Nivel 1: liberar stock. Nivel 2: reembolsar + liberar | 🔲 |
| 5.5 | Registrar servicios en DI | `Program.cs` | — | 🔲 |

---

### Etapa 6 — MassTransit + Consumers SQS

**Objetivo:** Conectar servicios vía eventos con MassTransit.
**Aprendizaje:** MassTransit, consumidores, InMemory vs SQS, Test Harness.

#### Diagrama

```
Desarrollo Local:
  MassTransit (InMemory) ─── Colas en memoria
  └── Sin dependencia AWS ─── Pruebas rápidas

Producción AWS:
  MassTransit (AmazonSQS) ─── SQS FIFO Queues
  ├── pedidos.fifo          ─── StockValidatorConsumer
  ├── pedidos-pago.fifo     ─── PagoConsumer
  ├── pedidos-factura.fifo  ─── FacturaConsumer
  └── pedidos-dlq.fifo      ─── Dead Letter
```

| Paso | Consumer | Cola SQS | Evento entrada | Evento salida | Estado |
|------|----------|----------|----------------|---------------|--------|
| 6.1 | `StockValidatorConsumer` | `pedidos.fifo` | PedidoCreadoEvent | StockValidadoEvent / StockRechazadoEvent | 🔲 |
| 6.2 | `PagoConsumer` | `pedidos-pago.fifo` | StockValidadoEvent | PagoProcesadoEvent / PagoRechazadoEvent | 🔲 |
| 6.3 | `FacturaConsumer` | `pedidos-factura.fifo` | PagoProcesadoEvent | FacturaGeneradoEvent / FacturaRechazadaEvent | 🔲 |
| 6.4 | `CompensationConsumer` | Multi-cola | PagoRechazadoEvent, FacturaRechazadaEvent | — | 🔲 |

| Paso | Actividad | Archivo | Estado |
|------|-----------|---------|--------|
| 6.5 | Agregar NuGets MassTransit + SQS | `.csproj` | 🔲 |
| 6.6 | Configurar MassTransit InMemory (local) | `Program.cs` | 🔲 |
| 6.7 | Configurar MassTransit AmazonSQS (AWS) | `Program.cs` | 🔲 |

---

### Etapa 7 — Controllers Saga

**Objetivo:** Exponer endpoints REST para el nuevo módulo de ventas saga.
**Aprendizaje:** REST async con 202 Accepted + Location, polling de estado.

| Endpoint | Controller | Método | Response | Estado |
|----------|------------|--------|----------|--------|
| POST `/api/v1/Ventas/pedido` | `VentasPedidoController` | Crear pedido | 202 Accepted + Location | 🔲 |
| GET `/api/v1/Ventas/pedido/{id}` | `VentasPedidoController` | Estado saga | 200 OK | 🔲 |
| GET `/api/v1/Ventas/pedido` | `VentasPedidoController` | Listar | 200 OK paginado | 🔲 |
| GET `/api/v1/Ventas/pago/{id}` | `VentasPagoController` | Detalle pago | 200 OK | 🔲 |
| GET `/api/v1/Ventas/pago/pedido/{idPedido}` | `VentasPagoController` | Pagos x pedido | 200 OK | 🔲 |
| GET `/api/v1/Ventas/factura/{id}` | `VentasFacturaController` | Detalle factura | 200 OK | 🔲 |
| GET `/api/v1/Ventas/factura/pedido/{idPedido}` | `VentasFacturaController` | Facturas x pedido | 200 OK | 🔲 |
| — | `VentasDashboardController` | Dashboard | 200 OK | 🔲 |

**DevSecOps:** Todos protegidos con `[Authorize(Policy = "AdminOnly")]` y `[EnableRateLimiting("AdminPolicy")]`.

---

### Etapa 8 — Middleware Refactoring

**Objetivo:** Extraer lógica inline de `Program.cs` a middlewares dedicados.
**Aprendizaje:** Pipeline HTTP, separación de concerns.

| Middleware | Origen | Destino | Estado |
|-----------|--------|---------|--------|
| Correlation ID | Nuevo | `Middleware/CorrelationIdMiddleware.cs` | 🔲 |
| Security Headers | `Program.cs:379-408` | `Middleware/SecurityHeadersMiddleware.cs` | 🔲 |
| CSP Nonce + Scalar | `Program.cs:413-455` | `Middleware/CspNonceMiddleware.cs` | 🔲 |
| Token Blacklist | `Program.cs:366-377` | (ya se mueve inline en Etapa 1) | 🔲 |

**Pipeline final:**
```
CorrelationIdMiddleware → RequestTimeoutMiddleware → SecurityHeadersMiddleware
→ CspNonceMiddleware → AuditLoggingMiddleware → ExceptionHandlingMiddleware
```

---

### Etapa 9 — Rate Limiting Avanzado

**Objetivo:** Proteger endpoints administrativos y writes con políticas específicas.
**Aprendizaje:** Rate limiting con `System.Threading.RateLimiting`.

| Política | Límite | Ámbito | Estado |
|----------|--------|--------|--------|
| `AdminPolicy` | 200 requests/min | Todos los controllers | 🔲 |
| `ConcurrentWritesPolicy` | 10 concurrentes | POST/PUT/DELETE | 🔲 |
| `LoginPolicy` (existente) | 5 requests/5min | Login | ✅ Ya existe |

---

### Etapa 10 — Dashboard de Ventas

**Objetivo:** Visualizar métricas del saga en tiempo real.
**Aprendizaje:** Consultas agregadas EF, métricas de negocio.

| Endpoint | Datos | Estado |
|----------|-------|--------|
| `GET /api/v1/Ventas/dashboard` | Total pedidos hoy, ventas hoy, pedidos por EstadoSaga, profundidad SQS | 🔲 |
| `GET /api/v1/Ventas/saga/{id}/diagrama` | Línea de tiempo del saga (eventos + timestamps) | 🔲 |

---

### Etapa 11 — Tests

**Objetivo:** Garantizar calidad antes del deploy a AWS.
**Aprendizaje:** MassTransit Test Harness, mocks de Redis, pruebas de integración asíncronas.

| Proyecto | Archivo | Prueba | Estado |
|----------|---------|--------|--------|
| UnitTest | `TokenBlacklistServiceTests.cs` | Add, IsBlacklisted, expiración | 🔲 |
| UnitTest | `CacheServiceTests.cs` | Cache hit/miss, factory se ejecuta solo en miss | 🔲 |
| UnitTest | `LoginTestUnit.cs` (modificado) | Mocks IDistributedCache | 🔲 |
| UnitTest | `VentasPedidoServiceTests.cs` | Evento publicado con datos correctos | 🔲 |
| UnitTest | `PagoServiceTests.cs` | 90% éxito, 10% fallo | 🔲 |
| UnitTest | `FacturaServiceTests.cs` | Folio único generado | 🔲 |
| UnitTest | `CompensationServiceTests.cs` | Stock liberado, pagos reembolsados | 🔲 |
| UnitTest | `StockValidatorConsumerTests.cs` | Stock suficiente/insuficiente | 🔲 |
| UnitTest | `PagoConsumerTests.cs` | Evento correcto según resultado pago | 🔲 |
| UnitTest | `FacturaConsumerTests.cs` | Factura generada/rechazada | 🔲 |
| UnitTest | `CorrelationIdTests.cs` | CorrelationId en response | 🔲 |
| UnitTest | `SecurityHeadersTests.cs` | Headers presentes | 🔲 |
| IntegrationTest | `PedidoSagaTests.cs` | POST → 202 → polling hasta Facturado | 🔲 |
| IntegrationTest | `RateLimiterTests.cs` | N+1 requests → 429 | 🔲 |

---

### Etapa 12 — Deploy Manual AWS

**Objetivo:** Configurar AWS Console y desplegar.
**Aprendizaje:** AWS Console, Security Groups, ALB, EC2, SQS.

**Costo:** ~$2.70 USD (cubierto por $300 créditos)

#### Día 1 — Red y Seguridad

| Paso | Actividad | Servicio AWS | Estado |
|------|-----------|-------------|--------|
| 12.1 | Crear IAM user admin + Access Key | IAM | 🔲 |
| 12.2 | Guardar Access Key en GitHub Secrets | GitHub | 🔲 |
| 12.3 | Crear VPC 10.0.0.0/16 | VPC | 🔲 |
| 12.4 | Crear 2 subnets públicas 10.0.1.0/24, 10.0.2.0/24 | VPC | 🔲 |
| 12.5 | Crear Internet Gateway + Route Table (0.0.0.0/0 → IGW) | VPC | 🔲 |
| 12.6 | Crear SG-ALB: inbound 80/443 desde 0.0.0.0/0 | EC2 | 🔲 |
| 12.7 | Crear SG-EC2: inbound 8080 desde SG-ALB, 22 desde IP personal | EC2 | 🔲 |
| 12.8 | SG-EC2 outbound: 1433 a IP del SQL Server externo | EC2 | 🔲 |

#### Día 2 — ALB + EC2

| Paso | Actividad | Servicio AWS | Estado |
|------|-----------|-------------|--------|
| 12.9 | Crear IAM Role EC2 con policies SQS + CloudWatch Logs | IAM | 🔲 |
| 12.10 | Crear Target Group HTTP:8080, health check /health/ready | EC2 | 🔲 |
| 12.11 | Crear ALB internet-facing, listener 80 → TG | EC2 | 🔲 |
| 12.12 | Lanzar EC2 t2.micro con Amazon Linux 2023 + IAM Role | EC2 | 🔲 |
| 12.13 | SSH a EC2, instalar Docker + docker-compose | EC2 | 🔲 |

#### Día 3 — SQS + Deploy App

| Paso | Actividad | Servicio AWS | Estado |
|------|-----------|-------------|--------|
| 12.14 | Crear cola `pedidos.fifo` (FIFO, dedup, maxReceiveCount 3 → DLQ) | SQS | 🔲 |
| 12.15 | Crear cola `pedidos-pago.fifo` | SQS | 🔲 |
| 12.16 | Crear cola `pedidos-factura.fifo` | SQS | 🔲 |
| 12.17 | Crear cola `pedidos-dlq.fifo` (retention 14d) | SQS | 🔲 |
| 12.18 | Copiar `docker-compose.aws.yml` a EC2 via SCP | — | 🔲 |
| 12.19 | Configurar env vars en EC2 | — | 🔲 |
| 12.20 | `docker-compose up -d` | — | 🔲 |
| 12.21 | Verificar `/health` y `/health/ready` | — | 🔲 |
| 12.22 | Cambiar MassTransit de InMemory a AmazonSQS | `Program.cs` | 🔲 |
| 12.23 | Probar endpoints desde ALB DNS | — | 🔲 |
| 12.24 | Probar saga completo desde web pública | — | 🔲 |

---

### Etapa 13 — Automatización y Destrucción

**Objetivo:** Crear CloudFormation, destruir, redeploy automático, destrucción final.
**Aprendizaje:** Infraestructura como Código, AWS CLI, ciclo completo de vida.

#### 13.1 — Automatización

| Paso | Actividad | Archivo | Estado |
|------|-----------|---------|--------|
| 13.1 | Crear CloudFormation template | `deploy/aws/cloudformation.yml` | 🔲 |
| 13.2 | Incluir VPC, subnets, IGW, SG, ALB, TG, EC2, SQS×4, IAM, CW | — | 🔲 |
| 13.3 | Crear `deploy-aws.sh` | `scripts/deploy-aws.sh` | 🔲 |
| 13.4 | Crear `deploy-app.sh` | `scripts/deploy-app.sh` | 🔲 |

#### 13.2 — Destrucción y Redeploy

| Paso | Actividad | Comando | Estado |
|------|-----------|---------|--------|
| 13.5 | Destruir infraestructura manual | `aws cloudformation delete-stack` | 🔲 |
| 13.6 | Verificar AWS Console: 0 recursos | — | 🔲 |
| 13.7 | Redeploy automático | `bash scripts/deploy-aws.sh` | 🔲 |
| 13.8 | Deploy app automático | `bash scripts/deploy-app.sh` | 🔲 |
| 13.9 | Smoke test automático | curl health + curl saga | 🔲 |
| 13.10 | Verificar funciona igual que manual | — | 🔲 |

#### 13.3 — Destrucción Final

| Paso | Actividad | Comando | Estado |
|------|-----------|---------|--------|
| 13.11 | Destruir todo | `aws cloudformation delete-stack` | 🔲 |
| 13.12 | Verificar AWS Console: 0 recursos (EC2, ALB, SQS, CW) | — | 🔲 |
| 13.13 | Eliminar Access Key IAM | — | 🔲 |

---

## 8. DevSecOps Checklist Transversal

| Práctica | Dónde se aplica | Estado |
|----------|-----------------|--------|
| JWT con key 256-bit | Program.cs (existente) | ✅ Existente |
| Argon2id password hashing | PasswordHasherService | ✅ Existente |
| Rate limiting por endpoint | Etapa 9 | 🔲 |
| Security headers (CSP, HSTS, X-Frame-Options) | Etapa 8 | 🔲 |
| CSP con nonce para Scalar | Etapa 8 | 🔲 |
| Correlation ID para trazabilidad | Etapa 8 | 🔲 |
| Token blacklist en Redis (no ConcurrentDictionary) | Etapa 1 | 🔲 |
| No cachear datos sensibles (strPWD) | Etapa 2 | 🔲 |
| Secretos en variables de entorno | Etapa 12 | 🔲 |
| SQL injection prevention (EF Core) | Existente | ✅ Existente |
| Análisis estático (Semgrep + EditorConfig) | Existente | ✅ Existente |

---

## 9. Convención Redis Keys

| Prefix | Ejemplo | TTL | Creado por | Estado |
|--------|---------|-----|------------|--------|
| `blacklist:{jti}` | `blacklist:a1b2c3` | Hasta expiración JWT | TokenBlacklistService | 🔲 |
| `attempts:{user}` | `attempts:admin` | 30 min | LoginService | 🔲 |
| `lockout:{user}` | `lockout:admin` | 15 min | LoginService | 🔲 |
| `cache:producto:{id}` | `cache:producto:5` | 60 s | CacheService | 🔲 |
| `cache:productos:page{p}:size{s}` | `cache:productos:page1:size20` | 30 s | CacheService | 🔲 |
| `cache:cliente:{id}` | `cache:cliente:3` | 60 s | CacheService | 🔲 |
| `cache:clientes:page{p}:size{s}` | `cache:clientes:page1:size20` | 30 s | CacheService | 🔲 |
| `cache:empleado:{id}` | `cache:empleado:7` | 60 s | CacheService | 🔲 |
| `cache:tipo-empleado:{id}` | `cache:tipo-empleado:1` | 120 s | CacheService | 🔲 |
| `cache:tipo-empleado:list` | `cache:tipo-empleado:list` | 120 s | CacheService | 🔲 |
| `cache:usuario:{id}` | `cache:usuario:1` | 60 s | CacheService | 🔲 |

---

## 10. Resumen de Avance

| Etapa | Tareas | Completadas | Avance |
|-------|--------|-------------|--------|
| 0 — Infra Local | 6 | 1 | 17% |
| 1 — Stateless + Redis | 14 | 0 | 0% |
| 2 — Cache Distribuido | 14 | 0 | 0% |
| 3 — Modelos Saga | 8 | 0 | 0% |
| 4 — Eventos | 7 | 0 | 0% |
| 5 — Servicios Saga | 5 | 0 | 0% |
| 6 — MassTransit | 7 | 0 | 0% |
| 7 — Controllers | 8 | 0 | 0% |
| 8 — Middleware | 5 | 0 | 0% |
| 9 — Rate Limiting | 3 | 0 | 0% |
| 10 — Dashboard | 2 | 0 | 0% |
| 11 — Tests | 14 | 0 | 0% |
| 12 — Deploy AWS | 24 | 0 | 0% |
| 13 — Automatización | 13 | 0 | 0% |
| **Global** | **130 tareas** | **1** | **<1%** |
