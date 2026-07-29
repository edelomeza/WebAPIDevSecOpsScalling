# Checklist de Calidad para Pull Requests

## Antes de abrir el PR

### Código
- [ ] No hay secretos, contraseñas ni tokens hardcodeados
- [ ] No hay cadenas de conexión hardcodeadas
- [ ] No hay comentarios de código muerto (`// TODO:` solo si tiene issue vinculado)
- [ ] Los nombres de variables/métodos son descriptivos (inglés)
- [ ] No hay using statements no utilizados

### Calidad y Análisis
- [ ] El build pasa sin errores: `dotnet build -c Release --no-restore`
- [ ] SonarAnalyzer.CSharp no agrega nuevas violaciones S (severidad warning o mayor)
- [ ] Complejidad ciclomática ≤ 10 por método (S1541)
- [ ] Complejidad cognitiva ≤ 15 por método (S3776)

### Tests
- [ ] Los tests existen y pasan:
  - UnitTest: `dotnet test UnitTest/UnitTest.csproj -c Release --no-build`
  - IntegrationTest: `dotnet test IntegrationTest/IntegrationTest.csproj -c Release --no-build`
  - SecurityTest: `dotnet test SecurityTest/SecurityTest.csproj -c Release --no-build`
- [ ] Cobertura de código nuevo ≥ 80%
- [ ] Cobertura total ≥ 75%: `python scripts/check_coverage.py`
- [ ] Se agregaron tests para código nuevo (unitarios y/o de integración)

### Seguridad
- [ ] Los endpoints nuevos tienen rate limiting configurado
- [ ] Los endpoints nuevos tienen validación con FluentValidation
- [ ] No se introducen vulnerabilidades de SQL injection (usar parámetros, no interpolación)
- [ ] No se introducen vulnerabilidades XSS
- [ ] Autenticación y autorización verificadas en endpoints nuevos
- [ ] Los tokens JWT no se loggean

### API y Contratos
- [ ] Los nuevos endpoints siguen el patrón `api/v{version:apiVersion}/[controller]`
- [ ] Los DTOs de request tienen validación (FluentValidation)
- [ ] Los códigos HTTP de respuesta son correctos (201 POST, 200 GET/PUT, 204 DELETE, 400/422 validación, 401/403 auth)
- [ ] No se rompen contratos existentes (Pact)

### Infraestructura
- [ ] No se introducen dependencias nuevas sin evaluación de seguridad
- [ ] Las migraciones de BD son reversibles (Down definido)
- [ ] Las configuraciones nuevas tienen valores por defecto seguros

## Durante la revisión

- [ ] El reviewer verificó que el checklist anterior se cumple
- [ ] No hay regresiones en cobertura
- [ ] Los cambios son atómicos (un PR = una feature/fix)

## Post-merge

- [ ] Verificar que el CI/CD pasa completo en main
- [ ] Verificar que SonarCloud Quality Gate pasa
- [ ] Verificar que Semgrep no encuentra hallazgos nuevos
