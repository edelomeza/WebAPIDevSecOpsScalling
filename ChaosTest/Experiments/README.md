# Experiments de chaos

- `sql-kill.json` — paso 6.3 ✅: matar SQL Server durante POST `/api/v1/venta` (carga NBomber);
  circuit breaker se abre y los requests fallan gracefulmente (503 controlado).
- `redis-kill.json` — paso 6.2 ✅: matar Redis durante carga NBomber; la API sigue respondiendo
  con fallback a IMemoryCache (`/health/ready` 200; `/health` 503 por el check de Redis).
- `redis-latency.json` — paso 6.4 ✅: 2s de latencia de red en Redis (`tc netem`) durante carga
  NBomber; degradación graceful por timeouts + fallback IMemoryCache; recovery con `latency-off`.
  ⚠️ `tc netem` solo en host Linux (CI ubuntu OK); en Windows el paso se marca `Skipped` con WARN.

Esquema y ejemplos en `../README.md` (sección "Formato de experimento JSON").

> Nota: el `target` (`sql`, `redis`) debe coincidir con el servicio de compose desplegado
> (o nombre/label del contenedor). Ver `Resolve-ChaosContainer` en `Helpers/FaultInjector.psm1`.
