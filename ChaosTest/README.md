# ChaosTest — Infraestructura de caos (Fase 6, paso 6.1)

Infraestructura para inyectar fallos controlados sobre los contenedores de la API
(Redis, SQL Server) y validar la tolerancia a fallos del sistema. Es la base sobre la que
corren los experimentos 6.2-6.4 (matar Redis, matar SQL Server, latencia de red) y el job
nocturno 6.5 (`chaos-nightly.yml`).

## Prerrequisitos

- Docker CLI con daemon en ejecución (compose v2: `docker compose`)
- PowerShell 5.1 o PowerShell Core (pwsh)
- Inyección de **latencia** (`latency`/`latency-off`) requiere **host Linux** (`tc netem`).
  En Windows el fallo se marca como `Skipped` con WARN; usar `pause`/`unpause` como proxy
  de degradación (limitación documentada, no hackeada).

## Estructura

```
ChaosTest/
  run-chaos.ps1               Orquestador: ejecuta experimento JSON, verifica, reporta
  Helpers/
    FaultInjector.psm1        Módulo de inyección de fallos Docker (kill/start/pause/latency)
  Experiments/                Experimentos JSON (se crean en 6.2-6.4)
```

## Uso

```powershell
# Catálogo de fallos soportados
powershell -File ChaosTest/run-chaos.ps1 -ListFaults

# Ejecutar un experimento (dry-run primero)
powershell -File ChaosTest/run-chaos.ps1 -Experiment .\Experiments\redis-kill.json -DryRun
powershell -File ChaosTest/run-chaos.ps1 -Experiment .\Experiments\redis-kill.json
```

Exit codes (alineados con NBomber): `0` = PASS, `1` = al menos un paso o verificación
falló, `2` = error de suite (JSON inválido, experimento inexistente, parámetros faltantes).

El reporte se escribe en `reports/chaos-<experimento>-<timestamp>.json` (raíz del repo,
directorio ya ignorado por git).

## Fallos soportados

| Action | Efecto | Uso |
|---|---|---|
| `kill` | `docker kill` (SIGKILL) al contenedor | Simular caída de infraestructura |
| `start` | `docker start` del contenedor | Recuperación tras `kill` |
| `pause` | `docker pause` (congela el proceso) | Proxy de degradación en cualquier host |
| `unpause` | `docker unpause` | Recuperación tras `pause` |
| `latency` | `tc netem delay <ms>` vía `docker exec` (solo Linux) | Degradación por latencia de red |
| `latency-off` | Elimina el `qdisc netem` (solo Linux) | Recuperación tras `latency` |

Resolución de contenedores por orden: servicio de compose (`docker compose ps -aq <svc>`),
nombre (`docker ps -aq --filter name=`), label `com.docker.compose.service=`. Usa `-a`
(contenedores detenidos incluidos) para que la recuperación (`start`) encuentre contenedores
matados con `kill`.

## Formato de experimento JSON

```json
{
  "name": "sql-kill",
  "description": "Matar SQL Server durante POST /api/v1/venta; circuit breaker se abre, request falla graceful",
  "target": "sql",
  "load": {
    "command": "dotnet",
    "args": ["run", "--project", "PerformanceTest/PerformanceTest.csproj", "-c", "Release", "--no-build"],
    "warmupSeconds": 30,
    "env": {
      "PERF_API_BASE_URL": "http://localhost:8080"
    }
  },
  "steps": [
    {
      "name": "matar sql server",
      "action": "kill",
      "verify": {
        "method": "GET",
        "url": "http://localhost:8080/health/ready",
        "expectedStatus": 503
      }
    }
  ],
  "recovery": [
    { "action": "start", "target": "sql" }
  ]
}
```

- `target` a nivel raíz es el default; cada `step` puede sobrescribirlo.
- `load` es opcional: arranca un proceso de carga (NBomber) antes de los pasos, espera
  `warmupSeconds` y lo detiene en el bloque `finally`. `env` inyecta variables de entorno
  (se restauran al final). Si la carga muere prematuramente solo se emite WARN: la carga es
  el conductor del experimento, no su veredicto. Sus logs van a `reports/load-*.out.log` /
  `reports/load-*.err.log`.
- `verify` es opcional: petición HTTP tras el fallo; `status` distinto a `expectedStatus`
  marca el paso como fallido. El código 0 significa conexión fallida (timeout/rechazo).
- `recovery` se ejecuta siempre al final (bloque `finally`), incluso si un paso falló.

## Experimentos planificados

| Paso | Archivo | Escenario | Estado |
|---|---|---|---|
| 6.2 | `Experiments/redis-kill.json` | Matar Redis durante carga NBomber; API sigue con fallback en memoria | ✅ |
| 6.3 | `Experiments/sql-kill.json` | Matar SQL durante POST `/api/v1/venta`; circuit breaker se abre, falla graceful | ✅ |
| 6.4 | `Experiments/redis-latency.json` | 2s de latencia en Redis; degradación graceful (timeouts/fallbacks) | ✅ |

Con los tres experimentos creados, la Fase 6 solo tiene pendiente el bloque AWS (6.6-6.12), el
job nocturno 6.5 y el kube-bench opcional 6.12.

El paso 6.5 consumirá estos experimentos desde un job nocturno `.github/workflows/chaos-nightly.yml`
que publicará el reporte generado por `run-chaos.ps1`.
