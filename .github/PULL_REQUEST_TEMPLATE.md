## Descripción

<!-- Explica qué hace este PR y por qué es necesario -->

## Tipo de cambio

- [ ] Bug fix (cambio no rompiente que corrige un issue)
- [ ] Nueva feature (cambio no rompiente que agrega funcionalidad)
- [ ] Breaking change (corrección o feature que rompe compatibilidad existente)
- [ ] Refactor/Mejora de código (sin cambios funcionales)
- [ ] Documentación

## Checklist de Calidad

<!-- Marca lo que aplica — el PR no se mergeará si los checks obligatorios fallan -->

### Código
- [ ] El build pasa sin errores
- [ ] No hay secretos, tokens ni cadenas de conexión hardcodeados
- [ ] No hay comentarios de código muerto sin issue vinculado
- [ ] Las migraciones de BD (si aplica) tienen Down definido

### Tests
- [ ] Los tests unitarios pasan
- [ ] Los tests de integración pasan
- [ ] Los tests de seguridad pasan
- [ ] Se agregaron tests para código nuevo (cuando aplica)

### Seguridad
- [ ] Los endpoints nuevos tienen rate limiting
- [ ] Los endpoints nuevos tienen FluentValidation
- [ ] Autenticación y autorización verificadas en endpoints nuevos

### QA automático
<!-- Estos checks los ejecuta el CI automáticamente -->
- [ ] Cobertura ≥ 75% (`python scripts/check_coverage.py`)
- [ ] Semgrep sin hallazgos ERROR/WARNING nuevos
- [ ] SonarAnalyzer sin violaciones S nuevas

> 📋 Revisa el [CHECKLIST_PR.md](../blob/main/CHECKLIST_PR.md) completo para más detalles.

## Cómo se probó

<!-- Describe cómo verificaste los cambios (comandos, capturas, etc.) -->

## Issues relacionados

<!-- Closes #issue-number -->
