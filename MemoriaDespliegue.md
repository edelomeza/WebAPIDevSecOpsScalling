# MemoriaDespliegue — Resumen Buenas Prácticas WebAPIDevSecOpsScalling

**Proyecto:** `WebAPIDevSecOpsScalling` | **Imagen:** `edelomeza/webapidevsecops-scalling` | **Región:** `us-east-1` | **Commit base prod:** `26d4d76` (`05d7697 fix(aws): SNS permissions + B1 scope + dashboard stack prefix` `AL2023` `public DB` `health 200` `deploy 35386250891` `PR #56`) | **PostDespliegue2:** `75ac69b` → `PR #57 MERGED:6051a7f` | **PostDespliegue3:** `7f11a9b fix(api): global [Authorize]+quita caché 30s+ownership` → `PR #58 MERGED:e9a3872` `CI 35482907114 pull_request success + 35483350153 push success` → `origin/main` solo tras Merge+review | **PostDespliegue4:** `7bdd7f8 fix(docker): curl healthcheck` → `PR #59 MERGED:a62c416` | **PostDespliegue6:** `b98b6af VenCatEstado [Authorize]+cache 120s` → `096f2c4 CloudFront d38h9zydjw5wl4` → `af9eb4a fix IDs` → `PR #63 MERGED:adbf7e3` | **Stack:** `webapidevsecops-prod` `UPDATE_COMPLETE` `ALBDNS webapidevsecops-prod-alb-1055914754...` → `320769337...` → `2145640385.us-east-1.elb.amazonaws.com` + `CloudFront d38h9zydjw5wl4.cloudfront.net Deployed` `2026-09-20T16:19:14Z/20:33Z` efímero (destroy 01:00 MX) | **EC2:** `100.54.31.182` `i-046788481b3efda33` → `i-0f4fdceb8a6d2f907` → `98.92.146.130 i-0d11306f6406e5b96 2145640385 32.197.182.180` `t3.micro AL2023 ami-0bd3fbcdc633a1b1a` | **Deploy:** `35522286598 workflow_dispatch main success 5m21s 16:18Z` `35527507286 17:57Z` `ImageTagUsed e9a3872→a62c416→adbf7e3` `Health 200 sql-server 0.11s` `SQS 0`

---

## PostDespliegue2 — P2.1-P2.8 Bloqueo AdminOnly ≠ hardening (75ac69b→6051a7f)

- **P2.1 AdminOnly ≠ hardening** `ClienteController.cs:25` `ProductoController.cs:25` + `LoginService.cs:182` sin Role → `UserAccessor.cs:23` false → `403` `health 200` no detecta. Fix **Opción A** `AdminOnly→[Authorize]` + `ClienteService.cs:24 ApplyOwnershipFilter` `Program.cs:338`.
- **P2.2 Catálogo no es admin** `TipoEmpleadoController.cs:39` → `[Authorize]` lectura global.
- **P2.3 Crear usuario** `UsuarioController.cs:180` → `[Authorize]` riesgo documentado (requiere revert a seed admin).
- **P2.4 SecurityTest espejo** `5/136 FAIL Forbidden→OK` `loopback://localhost` `SecurityTest/*.cs:97,354` `ClientesSecurityTests.cs:100,377,455` `TipoEmpleado:47`.
- **P2.5 SkipMigration=true desfasa** `appsettings.Production.json:4` + `Program.cs:510 ||` → `SqlException 207 Invalid column` `db45497.public.databaseasp.net`. Fix `dotnet ef script --idempotent 438l full_migrate.sql` `SSM sqlcmd -C` `__EFMigrationsHistory` `strCreadoPorUsuario`.
- **P2.6 IAM sns:ListTopics** `cloudformation.yml:165 IneRoleEC2` → `TopicCache.cs:148 AuthorizationError` → añadir `sns:ListTopics` + nuevo `i-0f4fdceb8a6d2f907` `stack UPDATE_COMPLETE`.
- **P2.7 push≠prod** `ci-cd.yml:4 vars.REGISTRY_IMAGE:${{github.sha}}` `deploy.yml:3 workflow_dispatch` `main GH013 2 checks+review` → `push 75ac69b` no mueve `origin/main` hasta `Merge 6051a7f` `ALBDNS efímero`.
- **P2.8 RateLimit 5/5min** `Program.cs:280 AddSlidingWindowLimiter LoginPolicy 5/5min` → `429` → `LoginService.cs:109 refreshToken` `ConcurrentWritesPolicy 10`.

## PostDespliegue3 — P3.1-P3.8 Caché 30s + visibilidad global (7f11a9b→e9a3872 + 7bdd7f8)

- **P3.1 Caché 30s tardanza** `ClienteService.cs:46` `ProductoService.cs:46` `GetOrCreateAsync 30s` solo invalida `cache:{id}` `Create 143/154` nada → `GET` paginado stale 30s aunque `health 200` `POST 201`. Fix quitar caché lista `ClienteService.cs:36` `ProductoService.cs:34` directo `CountAsync+ApplyPagination` mantener `GetById 60s` `Cliente:110` `Producto:96` `RemoveAsync 213,233/217` regla `<5k consistencia>hit-ratio`.
- **P3.2 Usuario** `40,62,87,116,142,209 403` `POST 201` ok pero `GET/autocomplete 403` lista vacía.
- **P3.3 Empleado** `25,36,50,65,88,120 403` `TipoEmpleado:39` ya `[Authorize]` contraste.
- **P3.4 Venta/VentaDetalle** `25,36,50,67,96,124` todo `AdminOnly` + `VentaDetalleService.cs:25 AssertOwnershipAsync 112 SegUsuario` → `AdminOnly→[Authorize]` + `return`.
- **P3.5 Ownership filtraba** `ClienteService.cs:24 Where(strCreadoPorUsuario) 34 throw` → `return query/return` `strCreadoPorUsuario` solo auditoría `Create:156/138` `UserAccessor IsAdmin false`.
- **P3.6 Tests espejo** `4/603+2/4+6/136 FAIL Unauthorized/Forbidden→OK` `Build 0 errores 603/136/4 Passed`.
- **P3.7 Trazabilidad** `7f11a9b→PR #58 35482907114 success→Merge e9a3872→35483350153 push→deploy 35522286598 5m21s` `ALBDNS 320769337→2145640385` `35527507286`.
- **P3.8 Docker unhealthy falso** `docker ps 382ef44eb064 Up (unhealthy) FailingStreak 51 wget not found` vs `TG healthy 200 0.10s` `28x ELB 200 108ms` `compose:48 wget`. Fix `Dockerfile:32 apt-get curl` + `compose:48 curl -sf` (`aspnet:10.0` sin `wget/curl` `AGENTS Fase6`).

## PostDespliegue6 — P6.1-P6.3 VenCatEstado [Authorize]+cache 120s + CloudFront $0 https (b98b6af→096f2c4→af9eb4a→adbf7e3)

- **P6.1 VenCatEstado no mostraba estado venta (paridad TipoEmpleado 75ac69b)** `EstadoVentaController.cs:20,32` `AdminOnly→[Authorize]` `GetAll/GetById` — catálogo lectura global, `User` podía `GET /api/v1/venta` 200 con `strEstado` pero `GET /estadoventa` daba `403` y dropdown vacío. Fix paridad `TipoEmpleadoController.cs:39,59`. `VenCatEstadoService.cs:11,21,52` inyecta `ICacheService` `cache:ven-cat-estado:list/:id 120s` (vs `TipoEmpleado 120s`, evita stale 30s `P3.1`), `SecurityTest/EstadoVenta:52` `Forbidden→OK` `UnitTest/EstadoVenta/GetTests.cs:14` mock passthrough `GetOrCreateAsync→factory()`, `8/8 603/603 136/136 ✔`.
- **P6.2 CloudFront $0 https aprendizaje sin dominio** `deploy/aws/cloudformation.yml:373` `Distribution Http2and3 PriceClass_100 CloudFrontDefaultCertificate:true Origins ALB.DNSName http-only DefaultCacheBehavior redirect-to-https CachingDisabled 4135ea2d-6df8-44a3-9df3-4b5a84be39ad + AllViewer 88a5eaf4-2fd4-4709-b370-b4c650ea3fcf Outputs CloudFrontDomain d38h9zydjw5wl4.cloudfront.net` — `https://d38h9zydjw5wl4.cloudfront.net/health/ready 200 0.103s` cert `*.cloudfront.net` válido, fallback `http://ALB:2145640385` vivo. `docker-compose.aws.yml:33` `Jwt__Issuer=https://${CLOUDFRONT_DOMAIN:-${ALB_DNS}}` + `scripts/deploy-app.sh:30` autodetecta `CLOUDFRONT_DOMAIN` + `CspNonceMiddleware.cs:68` `connect-src https://*.cloudfront.net` `ForwardedHeaders:444`/`HSTS:370` ya activos.
- **P6.3 Fix IDs 404** `2c8dac5` falló `Invalid request policy does not exist` `4135ea0d→4135ea2d 9dfb→9df3 + 055f→3fcf` verificados `aws cloudfront list-cache-policies / list-origin-request-policies` `af9eb4a`, `deploy-aws.sh:44`/`deploy-app.sh:30` + `workflows/deploy.yml:60` smoke dual `curl http ALB + https CF WARN 5-15min Deployed`.

## PostDespliegue7 — P7.1-P7.2 CloudFront AllViewer + HttpsRedirection GetAll 401 vía https (5a0be59→2afef39→eed9791 PR #65)

- **Seguridad `Program.cs:187-260`** — JWT 32B mínimo `GetByteCount>=32`, `RequireHttpsMetadata`, `ValidateIssuer/Audience=true ClockSkew=Zero`, `ValidAlgorithms=[HmacSha256]` solo, `OnTokenValidated` JTI blacklist `IsBlacklistedAsync (214)` + `RequireSignedTokens`. `ForwardedHeaders XForwardedFor|Proto KnownProxies.Clear() 444` + `UseForwardedHeaders` antes de `Auth 552` — honra `https` tras `CF→ALB`. `5a0be59 Program.cs:551` `UseHttpsRedirection` solo en `Development` — `CF http-only 391` + `redirect-to-https 396` ya en edge, evita `307` loop que borra `Authorization` en `GET GetAll/autocomplete` (`401` vía `https://d38h...` vs `200` vía `http://ALB`). `CORS SecurePolicy 313` `WithOrigins` single-origin `https://localhost:7064` `AllowCredentials`, `HSTS MaxAge=365 IncludeSubDomains Preload` solo Prod `546`, `SecurityHeadersMiddleware:14` + `CspNonceMiddleware:68` `connect-src 'self' https://*.cloudfront.net`.
- **Infra CloudFront `deploy/aws/cloudformation.yml:373`** — `2afef39` corrige `OriginRequestPolicy 88a5eaf4 CORS-S3Origin (solo Origin, sin query/auth) → 216adef6 AllViewer` (`allViewer` + `all` query/cookies) + `CachePolicy 4135ea2d CachingDisabled` verificado `aws cloudfront list-*` — `GET ?texto=` + `Authorization` ahora reenviado, `GetAll 200` vía `https` probado con `nonce`. `VPC 10.0.0.0/16` + `SGALB 80/443` `SGEC2 8080` solo `SGALB`, `PriceClass_100` `CloudFrontDefaultCertificate`, `FIFO x4 + DLQ 14d`, `LogGroup 14d`.
- **Observabilidad `Program.cs:34-599` + `docker-compose.aws.yml:15`** — Serilog `audit-.json` 7d + `Enrich Machine/Env/Pid`, OTel `Prometheus` `QualityMetricsService` eager `482`, Health `/health` + `/health/ready db` + `redis/sql-server` tags `144`, `awslogs` en `UserData` + `daemon.json`.
- **Testing `603/603 UnitTest + 136/136 SecurityTest + 354/357 IntegrationTest (Recovery 3 sin Docker)`** — `WebApplicationFactory<Program> partial Program:711` + `UseInMemoryDatabase Guid` por test, `TokenHelper` JWT `role` shared, `SecurityTest/EstadoVenta` `no-store 401/403`.
- **Despliegue `scripts/deploy-app.sh:33` autodetect `CLOUDFRONT_DOMAIN/ALBDNS → JWT_ISSUER https://d38h... + CORS_ALLOWED_ORIGIN`, `SSM Zero Trust` sin `22/pem`, `Invalidate /*`, dual smoke `http ALB + https CF`.

## Infra / DevSecOps — Base

- **IaC Only B 3-5m** `cloudformation.yml:34 db45497 ami-0bd3fbcdc633a1b1a VPC 10.0.0.0/16 SGALB 80/443 SGEC2 8080←SGALB ALB→TG 8080 /health/ready` sin `RDS/NAT` `3-5m vs 8-12m`.
- **Imagen trazable** `ci-cd.yml:134 tags:${{github.sha}}` `deploy.yml:36 inputs.tag||github.sha` `vars.REGISTRY_IMAGE` `deploy/docker-compose.aws.yml:9 ${TAG}`.
- **12-Factor** `GH vars RDS_ADDRESS DB_NAME CORS https://localhost:7064` `Secrets DB_PASSWORD JWT_KEY_PROD ≥32B` `Program.cs:122,190`.
- **SSM Zero-Trust** `deploy-app.sh:59 ssm_run base64` `deploy-aws.sh:20 no-fail-on-empty-changeset` `curl /health/ready 18x10s`.
- **AL2023 compose v5** `cloudformation.yml:268` `mkdir cli-plugins curl compose v5.5.1`.
- **Polly v8** `DbResilienceService.cs:23 FailureRatio 1.0` `357 Passed`.
- **OTel Health** `Program.cs:154 AddSqlServer SELECT 1 AddRedis /health/ready db` `massTransit SQS` `DashboardService.cs:184`.
- **Ciclo 16h/8h 33%** `0 7 UTC destroy 0 15 UTC deploy 224h vs 336h $6.83`.
- **Lecciones Fase1-6** `AGENTS.md` `mutation 46%≠66% Stryker Safe Mode blind`, `NBomber no weights 429`, `Pact v2 descarta matchingRules`, `OTel beta 1.17 lazy Meter`, `Testcontainers port fijo`, `Chaos wget/curl falta Argon2id 7456ms`.

**Evidencia maestra** `Dockerfile:32` `deploy/docker-compose.aws.yml:48` `deploy/aws/cloudformation.yml:174,335` `MemoriaDespliegue.md:23-52` `AGENTS.md:52,138` `git diff 7bdd7f8` `aws describe-stacks CREATE_COMPLETE e9a3872→a62c416` `elbv2 healthy 5bcf011924bce2a7` `ssm docker ps 382ef44eb064` `curl http://2145640385/health/ready 200`

---

**URL Producción:** `http://webapidevsecops-prod-alb-2145640385.us-east-1.elb.amazonaws.com` (`1055914754→320769337→2145640385` rotación `2026-09-20T16:19Z` `35522286598`) + `https://d38h9zydjw5wl4.cloudfront.net` (`CloudFrontDefaultCertificate` `Deployed 2026-09-20T20:33Z` `4135ea2d/88a5eaf4`) | **Health:** `http://.../health/ready 200 0.109s` + `https://d38h9zy.../health/ready 200 0.103s sql-server Healthy` `2026-09-20T16:28-21:00Z 28 sondeos ELB 200 108-109ms + curl CloudFront 200` `hash chain OK` | **Razor:** `https://localhost:7064` `API_BASE_URL=https://d38h9zydjw5wl4.cloudfront.net/api/v1` (fallback `http://$ALBDNS`) `CORS_ALLOWED_ORIGIN https://localhost:7064` | **SQS:** `https://sqs.us-east-1.amazonaws.com/151105438268/webapidevsecops-prod-pedidos*.fifo x4 0/0/0/0` `LogGroup /ecs/webapidevsecops-prod 14d` | **PostDespliegue6:** `b98b6af VenCatEstado [Authorize]+cache → 096f2c4 CloudFront $0 → af9eb4a fix IDs → adbf7e3 PR #63 MERGED` `98.92.146.130 i-0d11306f6406e5b96` `CI 603/603 136/136`

*Stack `CREATE_COMPLETE 2026-09-20T16:19:14Z 24 recursos` `AL2023 ami-0bd3fbcdc633a1b1a` `ImageTagUsed a62c416` `TG 8080 /health/ready 30s healthy i-0d11306f6406e5b96:8080` `SGALB 80/443 SGEC2 8080→SGALB` `IAMRoleEC2 sns:ListTopics fix 05d7697` `P3.8 wget→curl` `EC2 184MiB/913MiB 0.63%`*
