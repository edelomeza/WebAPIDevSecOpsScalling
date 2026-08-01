#!/usr/bin/env bash
#
# collect-quality-metrics.sh — Paso 4.19
#
# Recolecta las métricas de calidad de las 3 herramientas del pipeline y las
# publica como variables de entorno para la seccion QualityMetrics: de la app
# (pasos 4.16/4.17/4.18). El archivo generado (default: quality-metrics.env)
# se consume con docker-compose (env_file) o `set -a; source` para alimentar
# los gauges test_coverage_percent / mutation_score / sonar_quality_gate_passed
# / p95_latency_ms en el arranque de WebAPIDevSecOps.
#
# Fuentes:
#   1. SonarCloud API  -> coverage + quality_gate_status
#      (env: SONAR_TOKEN, SONAR_PROJECT_KEY, SONAR_ORG; opcional SONAR_API_BASE_URL)
#   2. Reporte Stryker -> mutation-report.json (mutationScore)
#      (env: STRYKER_REPORT; si no, busca el mas reciente en MutationTest/StrykerOutput/)
#   3. Reporte NBomber -> report.json (P95 por escenario; se usa el maximo)
#      (env: NBOMBER_REPORT; si no, busca el mas reciente en PerformanceTest/)
#
# Salida: quality-metrics.env (sobrescribe). Requiere: curl, jq, find (GNU).
# Si una fuente no esta disponible se emite WARN y la metrica queda en 0/false
# (comportamiento por diseno: la suite de performance 4.7-4.11 no corre en CI).

set -euo pipefail
LC_NUMERIC=C

OUT_FILE="${QUALITY_METRICS_OUT_FILE:-quality-metrics.env}"
SONAR_API_BASE_URL="${SONAR_API_BASE_URL:-https://sonarcloud.io}"
SONAR_TOKEN="${SONAR_TOKEN:-}"
SONAR_ORG="${SONAR_ORG:-}"
SONAR_PROJECT_KEY="${SONAR_PROJECT_KEY:-}"
STRYKER_REPORT="${STRYKER_REPORT:-}"
NBOMBER_REPORT="${NBOMBER_REPORT:-}"

info() { printf '[INFO] %s\n' "$*" >&2; }
warn() { printf '[WARN] %s\n' "$*" >&2; }

# --- 1. Cobertura + Quality Gate desde SonarCloud API ----------------------
coverage="0"
gate_passed="false"

if [[ -n "${SONAR_TOKEN}" && -n "${SONAR_PROJECT_KEY}" && -n "${SONAR_ORG}" ]]; then
  response="$(curl -sf --get "${SONAR_API_BASE_URL}/api/measures/component" \
    --data-urlencode "component=${SONAR_PROJECT_KEY}" \
    --data-urlencode "organization=${SONAR_ORG}" \
    --data-urlencode "metricKeys=coverage,quality_gate_status" \
    -H "Authorization: Bearer ${SONAR_TOKEN}" 2>/dev/null || true)"

  if [[ -n "${response}" ]]; then
    raw_coverage="$(printf '%s' "${response}" \
      | jq -r '.component.measures[]? | select(.metric=="coverage") | .value' 2>/dev/null || true)"
    gate_status="$(printf '%s' "${response}" \
      | jq -r '.component.measures[]? | select(.metric=="quality_gate_status") | .value' 2>/dev/null || true)"

    if [[ "${raw_coverage}" =~ ^[0-9]+(\.[0-9]+)?$ ]]; then
      coverage="${raw_coverage}"
    else
      warn "Medida 'coverage' no valida en respuesta SonarCloud ('${raw_coverage:-vacio}') — cobertura en 0"
    fi

    if [[ "${gate_status}" == "OK" ]]; then
      gate_passed="true"
    else
      warn "Quality gate SonarCloud NO OK ('${gate_status:-vacio}') — gate en false"
    fi
  else
    warn "SonarCloud API no respondio — coverage/gate en 0/false"
  fi
else
  warn "SONAR_TOKEN/SONAR_PROJECT_KEY/SONAR_ORG no configurados — coverage/gate en 0/false"
fi

# --- 2. Mutation score desde el reporte de Stryker --------------------------
if [[ -z "${STRYKER_REPORT}" ]]; then
  STRYKER_REPORT="$(find MutationTest -name 'mutation-report.json' -path '*StrykerOutput*' \
    -printf '%T@ %p\n' 2>/dev/null | sort -rn | head -1 | cut -d' ' -f2-)"
fi

if [[ -n "${STRYKER_REPORT}" && -f "${STRYKER_REPORT}" ]]; then
  mutation_score="$(jq -r '(.mutationScore // .mutation_score // (
      [.files[].mutants[]? | select(.status != "Ignored")] as $m
      | (($m | map(select(.status == "Killed" or .status == "Timeout")) | length)) as $detected
      | (($m | length) - ($m | map(select(.status == "CompileError")) | length)) as $den
      | (if $den > 0 then $detected * 100.0 / $den else 0 end)
    ))' "${STRYKER_REPORT}")"
  mutation_score="$(printf '%.2f' "${mutation_score}")"
  info "Mutation score ${mutation_score}% desde ${STRYKER_REPORT}"
else
  mutation_score="0.00"
  warn "Reporte Stryker no encontrado (STRYKER_REPORT o MutationTest/StrykerOutput/) — mutation score en 0"
fi

# --- 3. P95 desde el reporte de NBomber (maximo por escenario) ---------------
if [[ -z "${NBOMBER_REPORT}" ]]; then
  NBOMBER_REPORT="$(find PerformanceTest -name 'report.json' -path '*reports*' \
    -printf '%T@ %p\n' 2>/dev/null | sort -rn | head -1 | cut -d' ' -f2-)"
fi

if [[ -n "${NBOMBER_REPORT}" && -f "${NBOMBER_REPORT}" ]]; then
  p95="$(jq '[.nodeStats.scenarios[]? | .ok?.latency? | (.percent95 // .Percent95 // 0)] | max // 0' \
    "${NBOMBER_REPORT}")"
  p95="$(printf '%.2f' "${p95}")"
  info "P95 (max por escenario) ${p95}ms desde ${NBOMBER_REPORT}"
else
  p95="0.00"
  warn "Reporte NBomber no encontrado (NBOMBER_REPORT o PerformanceTest/reports/) — P95 en 0"
fi

# --- 4. Publicar --------------------------------------------------------------
tmp="$(mktemp)"
printf 'QualityMetrics__TestCoveragePercent=%s\n' "${coverage}" > "${tmp}"
printf 'QualityMetrics__MutationScore=%s\n' "${mutation_score}" >> "${tmp}"
printf 'QualityMetrics__SonarQualityGatePassed=%s\n' "${gate_passed}" >> "${tmp}"
printf 'QualityMetrics__P95LatencyMs=%s\n' "${p95}" >> "${tmp}"
mv "${tmp}" "${OUT_FILE}"

echo "=== Métricas de calidad consolidadas ($(date -u +%Y-%m-%dT%H:%M:%SZ)) ==="
printf '%-38s %s\n' 'Cobertura (%)' "${coverage}"
printf '%-38s %s\n' 'Mutation score (%)' "${mutation_score}"
printf '%-38s %s\n' 'Sonar quality gate PASS' "${gate_passed}"
printf '%-38s %s\n' 'P95 latencia max (ms)' "${p95}"
printf '%-38s %s\n' 'Publicado en' "${OUT_FILE}"
