# MemoriaSaga — Puente Legacy → Saga transparente

## 1. Trazabilidad de artefacto (lección manifest unknown 1157156)

| Antes | Después |
|-------|---------|
| `on.push.paths-ignore: [**.md]` global → merge docs nunca hizo `docker-build` → `manifest unknown` en `i-0aeabe014027b7d55` | Job `changes` (`dorny/paths-filter@v3`, 20 patrones `**.cs/csproj`, `Dockerfile`, `deploy/**`, `.github/workflows/**`) → `build-and-test` solo si `code=='true'`; `docker-build` con `always() && (success\|\|skipped)` siempre en `main` |
| `docker compose pull` fallaba tarde | Preflight Hub API `registry.hub.docker.com/v2/.../tags/$TAG` + fallback `manifest inspect`/`imagetools` + `pull \|\| { echo manifest unknown; exit 1;}` |

**Regla: filtrar por job, nunca la imagen.**

## 2. Saga como adaptador transparente (sin frontend)

- **Dual-write misma Tx** `VenVenta` / `VenPedido` (Pendiente) en `VentaService.CreateAsync:210` y `VenVentaDetalle` / `VenPedidoDetalle` en `VentaDetalleService:101` + acumulación `decTotal` — frontend sigue `POST /venta → N×POST /ventadetalle → PUT /venta/2`.
- **Correlación** `LegacyVentaId int?` único filtrado `VenPedido.cs:12` + `AppDbContext.cs:85` + migración `20260922023016` idempotente `scripts/sql/PostDespliegue9_LegacyVentaId.sql` (para DB externa `SKIP_MIGRATION=true`).
- **Finalize determinístico `PUT 1→2`** `VenCatEstado 1="En compra", 2="Fin compra"` — `VentaService.UpdateAsync:141` captura `estadoPrevio`, recalcula `decTotal` desde DB y publica `1× PedidoCreadoEvent` con retry `3×` (`PublishWithRetry`); re-`PUT 2→2` no-op. Sin debounce.
- **Saga único owner stock (criterio 9)** — `VentaDetalleService` no descuenta con flag ON; solo `StockValidatorConsumer.cs:54` decrementa bajo locks ordenados por producto + `RowVersion` optimistic. Idempotencia: `if(Pendiente != Pendiente) return` y `_finalizeLocks` por venta, doble consume no redecrementa.
- **Recuperación** `POST /Ventas/admin/pedidos/{id}/republish` + `GET` pendientes (`VentasAdminController`, `VentasPedidoService:RepublicarPendienteAsync`) con recálculo y retry (`AdminOnly`).
- **Flag** `Feature:SagaBridge` (`appsettings.Example` false, `Development` true) permite rollback sin deploy.

## 3. CORS y infra 12-Factor

- **Fix `Program.cs:313 GetAllowedOrigin`** — env `CORS_ALLOWED_ORIGIN` primero, ignorar `PLACEHOLDER` de `Production.json` → preflight `204` ya trae `Access-Control-Allow-Origin` (antes vacío).
- Scripts SQL idempotente + `ImageTag ${{github.sha}}` trazable + SSM ZeroTrust + CloudFront `AllViewer 216adef6` + `CachingDisabled 4135ea2d` ya consolidados (MemoriaDespliegue P7).

## 4. Calidad y testing (cierre de flake)

- **Nuevos tests 14:** `UnitTest/Venta/SagaBridgeTests.cs:10` (espejo, flag off, no-doble-stock, doble PUT, PUT1, sin detalles, consumer idempotente/concurrente) + `IntegrationTest/Saga/SagaBridgeIntegrationTests.cs:4` E2E con flag ON (flujo completo → terminal, doble PUT, 400 sin detalles, republish admin — timeout 30s + stock condicional Facturado 98 / Compensado 100 para compensar Pago 90%).
- **Verificación:** Unit `613/613`, Integ `358/361` (3 RecoveryTests exigen Docker), Sec `136/136`. Ajuste fixtures legacy a BridgeOff explícito (`Venta/UpdateTests`, `RaceConditionTests`) + `StockValidatorConsumerTests` estado Pendiente y PedidoNoExiste → no-op.
- **Coverage fix** `RedisFailureTests.cs:14 IDisposable` + null-guard + factory esqueleteada `RedisFailureTestsFactory.cs` (dbName en ctor, sin `IDisposable` propio) — suite `361` con XPlat Code Coverage ya no `[Cleanup Failure]`.

## 5. Lecciones PostDespliegue11 — tests deterministas (cierre flake `Create_ValidDto_ReturnsCorrectData`)

1. **Tests deterministas: aislar el no-determinismo en la frontera** — El flake venía de una carrera real (`PublishAsync` → consumers in-process → `GetByIdAsync`). En vez de parchar el síntoma con reintentos o `Task.Delay`, se eliminó la fuente de no-determinismo sustituyendo `IEventPublisher` por un no-op en la factory de la clase. Principio: un test que depende del scheduler no es un test, es un sorteo.
2. **Seguir el precedente del repo, no inventar mecanismos** — Se usó `ConfigureServices` + remover descriptor + re-registrar, el patrón exacto ya establecido en `RedisFailureTestsFactory.cs:37-44`. (El primer intento con `ConfigureTestServices` ni compiló — se corrigió al estándar del codebase en vez de forzar algo ajeno.)
3. **Diseñar para la sustituibilidad (seam)** — Funcionó sin tocar producción porque el código ya dependía de la abstracción `IEventPublisher` (una sola firma `PublishAsync<T>`), no de MassTransit directamente. La inversión de dependencias pagó su dividendo en testeabilidad.
4. **Blast radius mínimo** — Un archivo, +21 líneas, cero cambios en prod, cero cambios en otras suites. La cobertura E2E real de la saga sigue intacta en `SagaBridgeIntegrationTests` (otra clase).
5. **No romper lo que ya era tolerante** — Antes de aislar, se verificó que ningún test de la clase dependiera de transiciones por consumers (los asserts de estado ya eran `BeOneOf`). Con consumers aislados los pedidos siempre quedan `Pendiente`: todo se volvió más determinista, nada se debilitó.
6. **Documentar el porqué, no solo el qué** — El comentario en el código explica la carrera, cita la cadena (`StockValidado→Pagado→Facturado`) y deja guía futura: si alguien necesita transiciones reales, debe usar su propia factory. Regla del repo (AGENTS.md #5): documentar límites en vez de hackear.
7. **Verificación empírica antes de declarar victoria** — Build 0 errores + clase 5×16/16 + suite 358/358. Y honestidad ambiental: `RecoveryTests` se excluyó localmente por falta de Docker (corren en CI), declarado en vez de oculto.
8. **Un PR por preocupación** — Rama nueva (`PostDespliegue11`) en vez de contaminar el PR #80 en revisión (fix del circuit-breaker). Historial lineal y reversible por tema.

---

**En síntesis:** Legacy = fachada compatible; `VenPedido` = representación saga; `PUT 2` = señal de cierre; saga = owner de stock/compensación; CI traza artefacto, preflight fail-fast, flag y recuperación hacen el puente reversible.
