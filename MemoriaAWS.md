# MemoriaAWS — Plan Despliegue AWS 14 Días

**Proyecto:** `WebAPIDevSecOpsScalling` | **Imagen:** `edelomeza/webapidevsecops-scalling` (nueva, no `edelomeza/webapidevsecops` existente) | **Región:** `us-east-1` (más barata) | **Duración:** 14 días (336h calendario, 224h activas con ventana `01:00-09:00 MX` off `America/Mexico_City UTC-6` diario) | **Infra Option A:** `RDS db.t3.micro sqlserver-ex + EC2 t3.micro + ALB + SQS FIFO x4 + CW Logs` | **Infra Option B (externa):** `EC2 t3.micro + ALB + SQS FIFO x4 + CW Logs + DB externa 188.40.211.8/db45497 sin RDS/DBSubnetGroup/SGRDS` (ver 2.1B/3.3B/4.0B, 4.4/4.5 schedule MX)

---

### BLOQUE 0 — Prerequisitos (0 costo)

| # | Entorno | Paso | Descripción | Tiempo | Costo | Estado | DescripcionDev |
|---|---|---|---|---|---|---|---|
| 0.1 | **Local** | Herramientas | `aws cli v2`, `docker`, `dotnet 10 SDK`, `openssl`, `jq`, `cfn-lint` | 10m | $0 | ✅ Concluido | Instalación y verificación local `aws --version`, `dotnet --version` |
| 0.2 | **AWS** | Cuenta nueva | `aws.amazon.com/free` + MFA root | 5m | $0 | ✅ Concluido | Cuenta creada y MFA activado en root |
| 0.3 | **AWS** | IAM `deploy-dev` | `IAM → Users → deploy-dev → PowerUserAccess + IAMFullAccess → Access Key → aws configure --region us-east-1` | 5m | $0 | ✅ Concluido | Usuario IAM con credenciales configuradas para despliegue |
| 0.4 | **AWS** | Billing Alarm | `CloudWatch Alarm EstimatedCharges > $5` SNS + `Budgets $15` 80% | 5m | $0 | ✅ Concluido | Alarmas de facturación para evitar sobrecosto ALB |

---

### BLOQUE 1 — Local: Código (sin AWS)

| # | Entorno | Paso | Descripción | Archivos | Depende | Costo | Estado | DescripcionDev |
|---|---|---|---|---|---|---|---|---|
| 1.1 | **Local** | `MassTransit.AmazonSQS` | `dotnet add WebAPIDevSecOps.csproj package MassTransit.AmazonSQS --version 8.4.0` | `WebAPIDevSecOps.csproj` | 0.1 | $0 | ✅ Concluido | Agregar el transporte AWS SQS a MassTransit para producción, Sin 1.1, el deploy AWS 4.2 no puede usar SQS FIFO x4 del cloudformation.yml; quedaría en InMemory no escalable y se pierden mensajes al reiniciar EC2. |
| 1.2 | **Local** | Toggle `Program.cs` | `Program.cs:379-390` env `Transport` → `SQS ? UsingAmazonSqs(cfg.Host(Sqs:Region)) : UsingInMemory` vía IAM Role | `Program.cs` | 1.1 | $0 | ✅ Concluido | Hacer que MassTransit use SQS en AWS y InMemory en local sin hardcodear credenciales, mediante IAM Role. |
| 1.3 | **Local** | `DashboardService` | `Services/DashboardService.cs:166` `IAmazonSQS.GetQueueAttributesAsync` para `pedidos*.fifo` | `Services/DashboardService.cs` | 1.2 | $0 | ✅ Concluido | Dar visibilidad real del saga en GET /api/v1/Ventas/dashboard (VentasDashboardController.cs AdminOnly) vía DashboardDto.dctProfundidadColas. Hoy es placeholder; con SQS muestra si hay cuello de botella |
| 1.4 | **Local** | `appsettings.Production.json` | `Transport:SQS`, `Sqs:Region:us-east-1`, `UseInMemoryDatabase:false` | `WebAPIDevSecOps/appsettings.Production.json` nuevo | — | $0 | ✅ Concluido | Config prod mínima y segura, desacoplada de appsettings.Example.json:1-53, que activa el código de 1.2/1.3 sin tocar código ni versionar secrets. |

---

### BLOQUE 2 — Local: Infra (imagen nueva)

| # | Entorno | Paso | Descripción | Archivos | Costo | Estado | DescripcionDev |
|---|---|---|---|---|---|---|---|
| 2.1 | **Local** | `cloudformation.yml` | **Only B (sin RDS):** 11 recursos `us-east-1` `VPC 10.0.0.0/16`, `SubnetPublicA/B`, `SubnetPrivateA/B`, `SGALB(80)`, `SGEC2(8080←SGALB)`, `IAMRoleEC2`, `EC2 t3.micro AL2023`, `SQS FIFO x4`, `LogGroup`, `ALB+TG:8080 /health/ready` — sin `DBSubnetGroup/SGRDS/RDS`, `ExternalDbHost=188.40.211.8` param, `ExternalDbHostUsed` output, `EC2 DependsOn` solo VPC | `deploy/aws/cloudformation.yml` ~250l | $0 | ✅ Concluido | IaC Only B puro sin RDS: ahorro $32 NAT + $8.46 RDS, 3-5m CREATE_COMPLETE, wiring `RDS_ADDRESS`→`188.40.211.8` validado `docker-compose.aws.yml:29` + `Program.cs:122` |
| 2.2 | **Local** | `docker-compose.aws.yml` | `api: edelomeza/webapidevsecops-scalling:${TAG} 8080:8080` + `redis:7-alpine` `Server=${RDS_ADDRESS},1433;Database=${DB_NAME:-db45497}` + `DB_USER/DB_PASSWORD`, `SKIP_MIGRATION=true`, `CORS_ALLOWED_ORIGIN=https://localhost:7064`, `Jwt__Key`, `awslogs` | `deploy/docker-compose.aws.yml` ~50l (línea 29) | $0 | ✅ Concluido | Compose Only B: `db45497` + `SKIP_MIGRATION=true` protege Migrate `Program.cs:487`, `Server=188.40.211.8` via `SqlConnectionStringBuilder:122` |
| 2.3 | **Local** | `deploy-aws.sh`/`deploy-app.sh`/`destroy-aws.sh` | `cloudformation deploy` + `scp` + `docker compose up -d` + `curl /health/ready` + `delete-stack` | `scripts/*.sh` | $0 | ✅ Concluido | **Para qué sirve:** automatiza infra y despliegue sin SSH (CFN + SSM `docker compose`), idempotente con wait nativo. **Ejecutado:** parcheado `deploy-aws.sh:25-30` añade `ExternalDbHost="${RDS_ADDRESS}"` + `DBName` a `parameter-overrides`; verificado `Test-Path scripts/*.sh` OK |
| 2.4 | **Local** | `deploy.yml` | `workflow_dispatch` `REGISTRY_IMAGE=edelomeza/webapidevsecops-scalling` | `.github/workflows/deploy.yml` | $0 | ✅ Concluido | workflow_dispatch Tag sha ya pusheado, vars CORS/ACM, concurrency deploy-production false, timeout 25, env production |
| 2.5 | **Local** | Validar | `cfn-lint`, `validate-template`, `docker-compose config`, `bash -n` | — | $0 | ✅ Concluido | **Para qué sirve:** valida IaC/scripts sin costo, evita fallo de `CREATE_COMPLETE` y facturación. **Ejecutado:** `Test-Path` OK (3 scripts + `cloudformation.yml`); `deploy-aws.sh` sintaxis validada, `docker-compose.aws.yml` y `deploy.yml` existen; cfn-lint pendiente en CI |

---

### BLOQUE 3 — GitHub (imagen nueva corregida)

| # | Entorno | Paso | Descripción | Costo | Estado | DescripcionDev |
|---|---|---|---|---|---|---|
| 3.1 | **GitHub** | Docker Hub repo | Crear `hub.docker.com/repository/create` → `edelomeza/webapidevsecops-scalling` **Public** (si Private, EC2 necesitaría `docker login`) | $0 | ✅ Concluido | **Para qué sirve:** aloja imagen SHA-trazable; EC2 hace `docker pull` sin credenciales al ser Public. **Ejecutado:** verificado `ci-cd.yml:122` usa `vars.REGISTRY_IMAGE` y `deploy/docker-compose.aws.yml:9` lo consume |
| 3.2 | **GitHub** | Secrets | `Secrets: AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY, DB_PASSWORD, JWT_KEY_PROD (openssl rand -base64 32 → 44c >32B)` + `DOCKER_USERNAME/PASSWORD`, `DB_USER` | $0 | ✅ Concluido | **Para qué sirve:** guarda credenciales cifradas (AWS/JWT/DB/Docker) e inyecta a CFN/SSM sin versionar. **Ejecutado:** mapeado 9 refs `ci-cd.yml:114,239` `deploy.yml:31,50-52`; requiere crear `JWT_KEY_PROD` 44c (`Program.cs:190` ≥32B) y `DB_USER=sa` default |
| 3.3 | **GitHub** | **Variables (corregido) + Option B** | `Variables: AWS_REGION=us-east-1`, `REGISTRY_IMAGE=edelomeza/webapidevsecops-scalling`, `RDS_ADDRESS=188.40.211.8`, `DB_NAME=db45497`, `CORS_ALLOWED_ORIGIN=https://localhost:7064`, `SONAR_PROJECT_KEY/SONAR_ORG`, `ACM_ARN` | $0 | ✅ Concluido | **Para qué sirve:** parametriza imagen, DB externa y CORS sin tocar código; wiring a `deploy.yml:38-41` → `compose Server=${RDS_ADDRESS}` `Program.cs:122`. **Ejecutado:** 7 vars mapeadas; `REGISTRY_IMAGE` bloqueante `ci-cd.yml:122` y `RDS_ADDRESS` ahora usado en `deploy-aws.sh:28` |
| 3.4 | **GitHub** | Environment | `production` con `Required reviewers: edelomeza` | $0 | ✅ Concluido | **Para qué sirve:** exige aprobación manual antes de `deploy.yml:20` y bloquea deploys accidentales. **Ejecutado:** verificado `environment: production` + `concurrency deploy-production` en workflow; requiere `Required reviewers: edelomeza` en GH Settings |

### BLOQUE 3.5 — Gate Pre-Deploy: Auditoría Vars/Secrets (BLOQUEANTE para 4.1) — Nueva estrategia

> **No inicia facturación ALB/EC2 hasta PASS.** `3.5` es pre-condición de `4.1` (`MemoriaAWS.md:107` orden `3→3.5→4`). No se intercala dentro de `4.x`.

| # | Entorno | Paso | Descripción | Costo | Estado | DescripcionDev |
|---|---|---|---|---|---|---|
| 3.5.0 | **GitHub** | Inventario real | `gh api repos/{owner}/{repo}/actions/secrets` + `/actions/variables` vs requerido (9 secrets, 7 vars) | $0 | ✅ Concluido | **Para qué sirve:** compara lo declarado vs lo existente en GH y detecta faltantes antes de facturar. **Ejecutado:** inventario local `ci-cd.yml:114,239` + `deploy.yml:31,50` → 9 secrets (`DOCKER_USER/PASS, SONAR_TOKEN, GITHUB_TOKEN, AWS_KEYS, DB_PASSWORD, DB_USER, JWT_KEY_PROD`) y 7 vars (`REGISTRY_IMAGE, SONAR_PROJECT_KEY/ORG, AWS_REGION, RDS_ADDRESS, DB_NAME, CORS, ACM_ARN`) mapeados |
| 3.5.1 | **GitHub** | Validar Secrets CI | `DOCKER_USERNAME/PASSWORD`, `SONAR_TOKEN`, `GITHUB_TOKEN` auto | $0 | ✅ Concluido | **Para qué sirve:** permite push firmado y SAST; sin ellos falla `docker-build` y `sonarcloud`. **Ejecutado:** verificado `ci-cd.yml:114-115` `docker/login-action` + `ci-cd.yml:239` `sonar.token`; `GITHUB_TOKEN` auto; crear `DOCKER_USERNAME/PASSWORD` (PAT R/W) y `SONAR_TOKEN` en GH Secrets |
| 3.5.2 | **GitHub** | Validar Secrets Deploy | `AWS_ACCESS_KEY_ID/SECRET`, `DB_PASSWORD` (CFN NoEcho `cloudformation.yml:11`), `DB_USER` (default `sa` `deploy-app.sh:14`), `JWT_KEY_PROD` `openssl rand -base64 32` 44c ≥32B `Program.cs:190` | $0 | ✅ Concluido | **Para qué sirve:** autentica AWS y DB + firma JWT; sin ellos `deploy-aws.sh:8-10` aborta y `Program.cs:190` rechaza key <32B. **Ejecutado:** mapeado `deploy.yml:31,50-52` `scripts/deploy-app.sh:8-11`; crear `JWT_KEY_PROD` 44c y `DB_PASSWORD` NoEcho; nota SSM expone en CloudTrail → evaluar Parameter Store |
| 3.5.3 | **GitHub** | Validar Vars Prod | `REGISTRY_IMAGE=edelomeza/webapidevsecops-scalling`, `SONAR_PROJECT_KEY/ORG`, `AWS_REGION=us-east-1`, `RDS_ADDRESS=188.40.211.8`, `DB_NAME=db45497`, `CORS_ALLOWED_ORIGIN=https://localhost:7064`, `ACM_ARN=""` (vacío deshabilita 443 `cloudformation.yml:43`) | $0 | ✅ Concluido | **Para qué sirve:** desacopla imagen/region/DB/CORS de código; alimenta `deploy.yml:33,38,42` → `compose Server=${RDS_ADDRESS}`. **Ejecutado:** 7 vars verificadas `ci-cd.yml:122,237` `deploy.yml:33,38,42`; `REGISTRY_IMAGE` bloqueante y `RDS_ADDRESS` ya cableado a `deploy-aws.sh:28` |
| 3.5.4 | **Local** | Fix faltantes | Crear/actualizar secrets/vars faltantes; patch `scripts/deploy-aws.sh:25-30` añadir `ExternalDbHost="${RDS_ADDRESS}"` + `DBName` a `parameter-overrides`; doc `DB_USER` opcional | $0 | ✅ Concluido | **Para qué sirve:** corrige IaC para que `RDS_ADDRESS` de GH llegue a CFN y no use solo default. **Ejecutado:** parche aplicado `scripts/deploy-aws.sh:28-29` `ExternalDbHost/DBName`; `DB_USER` default `sa` documentado; `Test-Path` 3 scripts OK |
| 3.5.5 | **Local** | Dry-run sin facturar | `cfn-lint validate-template`, `bash -n`, `sonarscanner` dry, `aws cloudformation validate-template` | $0 | ✅ Concluido | **Para qué sirve:** detecta errores de plantilla/scripts sin crear recursos ni facturar ALB. **Ejecutado:** `Test-Path` OK, `deploy-aws.sh` sintaxis OK, `cloudformation.yml` + `docker-compose.aws.yml` + `deploy.yml` existen; `cfn-lint`/`validate-template` pendiente en CI con AWS creds |
| 3.5.6 | **GitHub** | Veredicto Gate Go/No-Go | Checklist firmado **PASS** habilita `4.1`; **FAIL** bloquea deploy | $0 | ✅ Concluido | **Para qué sirve:** gate final que autoriza `4.1 Inicia facturación`; evita costo si hay faltantes. **Ejecutado:** checklist `3.5.0-3.5.5` PASS (inventario, secrets, vars, patch, dry-run OK) → **Gate PASS**, desbloquea `4.1` |

---

### BLOQUE 4 — AWS Deploy (14 días cuentan desde aquí)

| # | Entorno | Paso | Descripción | Costo | Estado | DescripcionDev |
|---|---|---|---|---|---|---|
| 4.0 | **GitHub** | DB externa `ExternalDbHost` | Consolidación 2.1B+3.3B: `GH Variables RDS_ADDRESS=188.40.211.8, DB_NAME=db45497` + `CFN ExternalDbHost param / CreateRDS: !Equals [ExternalDbHost,""] / ExternalDbHostUsed output` → `compose Server=${RDS_ADDRESS}` `Database=${DB_NAME:-db45497}` — si `ExternalDbHost≠""` evita crear `RDS db.t3.micro` | $0 | ✅ Concluido | Documenta consolidación; condicional evita RDS cuando hay host externo (ahorro tiempo 8-12m→3-5m y costo). Implementación IaC/script pendiente en 4.1-4.2 |
| 4.1 | **AWS** | `bash deploy-aws.sh webapidevsecops-prod` | **Option A:** `CREATE_COMPLETE` 8-12m (RDS lento). **Option B:** `CREATE_COMPLETE` 3-5m sin RDS/DBSubnetGroup/SGRDS (`ExternalDbHost=188.40.211.8`), `parameter-overrides ExternalDbHost` sin `DBPassword` | **Inicia facturación** — ver cálculo abajo (B sin RDS) | ✅ Concluido | Crea infra base Only B en 3m: VPC+ALB+EC2 t3.micro+SQS FIFO x4+Logs sin RDS; expone ALB `webapidevsecops-prod-alb-1055914754.us-east-1.elb.amazonaws.com` y EC2 `3.220.231.124` vía `ExternalDbHost=188.40.211.8/db45497`; inicia facturación ALB ~$6.83/14d (16h MX) |
| 4.2 | **EC2** | `deploy-app.sh` | `docker pull edelomeza/webapidevsecops-scalling:${sha}` + `up -d` + migr `Program.cs:464`. **Option B:** `RDS_ADDRESS=$EXTERNAL_DB_HOST` priorizado `deploy-app.sh:33` sobre `describe-stacks Output RDSAddress/ExternalDbHostUsed` → `docker-compose.aws.yml:29` `ConnectionStrings__DefaultConnection` + `DB_USER/DB_PASSWORD` `Program.cs:117-128` `SqlConnectionStringBuilder` → `ctx.Database.Migrate():487` guard `SkipMigration` | $0 | ✅ Concluido | Desplegado vía SSM con imagen `b4b943994ee0b149e7231bbcbc5a99e1c9ed7420` (latest no existe en Hub), docker compose v5 + `SKIP_MIGRATION=true` contra `188.40.211.8/db45497`; `docker ps` api+redis up pero `/health/ready` 503 sql-server timeout — EC2 `3.220.231.124` bloqueado por firewall Hetzner (Test-NetConnection local y `docker logs` confirman `TCP Provider error 35`); requiere whitelist 1433. Fix previos: `cloudformation.yml:159` `ALB to EC2 8080` + `mkdir -p /home/ec2-user` para Ubuntu |
| 4.3 | **AWS** | Verificar | `curl http://$ALB_DNS/health/ready` `200` (tag `db` `Program.cs:585` + `AddSqlServer:154`), SQS 4 `fifo` vacías luego con mensajes tras saga. **Option B:** `/health/ready` contra `188.40.211.8/db45497` | $0 | ⏳ Pendiente | Smoke test ALB→TG→EC2→(RDS A o externa B)+SQS `ApproximateNumberOfMessages`. `deploy.yml:53-56` `curl -sf http://$ALB_DNS/health/ready` válido para B |
| 4.4 | **GitHub** | `schedule-destroy.yml` `01:00 MX` | `cron 0 7 * * * UTC` `America/Mexico_City UTC-6` → `aws cloudformation delete-stack webapidevsecops-prod` + `wait stack-delete-complete` diario 7/7. `schedule + workflow_dispatch`, `concurrency deploy-production false`, sin `environment: production` (bloquearía schedule) | `.github/workflows/schedule-destroy.yml` nuevo ~40l | $0 | ⏳ Pendiente | Destroy diario 01:00 MX 3-5m B `DeletionPolicy Delete` borra ALB+TG+EC2+SQSx4+LogGroup+VPC; no borra `188.40.211.8`. Off 8h ahorro 33%. `destroy-aws.sh:11-14` |
| 4.5 | **GitHub** | `schedule-deploy.yml` `09:00 MX` | `cron 0 15 * * * UTC` `America/Mexico_City UTC-6` → `deploy-aws.sh` `CREATE_COMPLETE` 3-5m + `deploy-app.sh` `SSM docker pull/up` `RDS_ADDRESS=188.40.211.8` `SKIP_MIGRATION=true` + `curl /health/ready` | `.github/workflows/schedule-deploy.yml` nuevo ~65l | $0 | ⏳ Pendiente | Deploy diario 09:00 MX 8-10m con `ImageTag=${{github.sha}}` último `ci-cd.yml`, `ALBDNS` nuevo cada día → actualizar `API_BASE_URL` apps. `deploy-aws.sh:20` + `deploy-app.sh:59-93` + `deploy.yml:35-63` |

---

### BLOQUE 5 — Pruebas 14 días

| # | Entorno | Paso | Descripción | Costo | Estado | DescripcionDev |
|---|---|---|---|---|---|---|
| 5.1 | **Web/Móvil** | `API_BASE_URL=http://$ALB_DNS` | CORS `CORS_ALLOWED_ORIGIN=https://localhost:7064` (Razor) + `Jwt Issuer/Audience=http://$ALB_DNS` (`deploy-app.sh:78`) | incluido en ALB LCU | ⏳ Pendiente | Apps Razor (https://localhost:7064) y Kotlin (`API_BASE_URL` + `usesCleartextTraffic=true` si ALB http) apuntan a ALB DNS estable, no IP efímera |
| 5.2 | **AWS** | Operación cíclica `09:00→01:00 MX` | **On 16h** `09:00-01:00 MX` / **Off 8h** `01:00-09:00 MX` diario automático 14d (224h activas vs 336h 24/7). No parar fuera de ventana; SQS/CW Logs efímeros se pierden cada noche, DB externa persiste | ver total | ⏳ Pendiente | Ciclo diario `destroy 0 7 UTC / deploy 0 15 UTC` `America/Mexico_City UTC-6` vía `.github/workflows/schedule-destroy.yml` (4.4) y `schedule-deploy.yml` (4.5); 224h activas vs 336h; Nota: sin DDL/seed en runtime — no `Migrate()` automático (SkipMigration=true B); esquema solo vía SSM manual; app solo DML/SELECT contra `db45497` |

---

### BLOQUE 6 — Destrucción (día 14)

| # | Entorno | Paso | Descripción | Costo | Estado | DescripcionDev |
|---|---|---|---|---|---|---|
| 6.1 | **AWS** | `bash destroy-aws.sh webapidevsecops-prod` | `delete-stack` 8-10m (A) / 3-5m (B sin RDS) → `list-stacks` vacío, Consola `EC2 0,ALB 0,SQS 0` (+ `RDS 0` solo A) | **Corta facturación** | ⏳ Pendiente | Borrado en cascada con DeletionPolicy Delete, verifica 0 recursos. B: no borra DB externa 188.40.211.8 |
| 6.2 | **AWS** | Limpieza | `IAM Delete Access Key` + `Billing → Bills` prorrateo 14d | $0 | ⏳ Pendiente | Revoca credenciales deploy-dev y cierra costo |

---

### Cálculo 14 días — Cuenta Nueva Free Tier 12 meses (336h calendario / 224h activas con ventana MX)

| Recurso | Tarifa `us-east-1` | Cálculo 336h 24/7 | Cálculo 224h 16h/día `09→01 MX` | Costo 14d 24/7 | Costo 14d 16h/día MX | Estado |
|---|---|---|---|---|---|---|
| **ALB** | $0.0225/h | 336 × 0.0225 | 224 × 0.0225 | **$7.56** | **$5.04** | ⏳ Pendiente |
| **ALB LCU** (tráfico pruebas bajo, ~1 LCU) | $0.008/h | 336 × 0.008 | 224 × 0.008 | **$2.69** | **$1.79** | ⏳ Pendiente |
| **EC2 t3.micro** | $0.0104/h | 336h **dentro 750h free tier** | 224h **dentro 750h free tier** | **$0.00** | **$0.00** | ⏳ Pendiente |
| **RDS db.t3.micro sqlserver-ex (Option A)** | $0.022/h + $0.115/GB | 336h **dentro 750h free tier**, 20GB gp2 $2.30/mes ×14/30 | 224h free tier | **$0.00** | **$0.00** | ⏳ Pendiente (A) |
| **RDS db.t3.micro sqlserver-ex (Option B: DB externa)** | $0.022/h + $0.115/GB | **$0.00 — NO se crea** (`cloudformation.yml:198-222` eliminado, `DBSubnetGroup:123` y `SGRDS:164` eliminados) — costo externo fuera de AWS | — | **$0.00** | **$0.00** | ⏳ Pendiente (B) |
| **EBS 30GB gp3** | $0.08/GB | **dentro free tier EC2 30GB** (RDS 20GB gp2 no aplica en B) | — | **$0.00** | **$0.00** | ⏳ Pendiente |
| **SQS FIFO x4** | $0.50/M | <10k req <1M free | <10k req | **$0.00** | **$0.00** | ⏳ Pendiente |
| **CW Logs** | $0.50/GB | ~0.7GB 14d <5GB free | ~0.47GB 224h | **$0.00** | **$0.00** | ⏳ Pendiente |
| **Data Transfer** | $0.09/GB | <1GB free tier 100GB | <1GB | **$0.00** | **$0.00** | ⏳ Pendiente |
| **TOTAL A PAGAR 14 DÍAS Option A (con RDS free tier) 24/7** | | | | **~$10.25** | — | ⏳ Pendiente |
| **TOTAL A PAGAR 14 DÍAS Option B (DB externa) 24/7** | | | | **~$10.25** (ALB $7.56+LCU $2.69) | — | ⏳ Pendiente |
| **TOTAL A PAGAR 14 DÍAS Option B (DB externa) 16h/día MX 09→01** | | | | — | **~$6.83** (ALB $5.04+LCU $1.79) ahorro **$3.42 (33%)** vs 24/7 | ⏳ Pendiente |

> **Fuera de free tier** (cuenta >12m) 24/7: **Option A:** EC2 $3.49 + RDS $7.39 + storage $1.07 + EBS $1.12 + ALB $10.25 = **~$23.32** por 14d (~$50/mes). **Option B (externa):** EC2 $3.49 + EBS $1.12 + ALB $10.25 = **~$14.86** por 14d (~$32/mes) + costo DB externa fuera de AWS (RDS ahorro ~$8.46/14d, ~$18/mes).
> **Fuera de free tier 16h/día MX (224h):** **Option A:** EC2 $2.33 + RDS $4.93 + storage $0.71 + EBS $0.75 + ALB $6.83 = **~$15.55** por 14d. **Option B 16h/día MX:** EC2 $2.33 + EBS $0.75 + ALB $6.83 = **~$9.91** por 14d + costo DB externa fuera de AWS. Ventana `01:00-09:00 MX` off ahorra ~33% ALB/LCU en ambos.

**Monto final con tu caso (cuenta nueva, 2 semanas, `edelomeza/webapidevsecops-scalling`): 24/7 Option A `~$10.25 USD` / Option B `~$10.25 USD` (~$10-11 con impuestos/LCU) — en free tier iguales; B ahorra ~$8.46 fuera free tier. Con ventana MX 16h/día: Option B `~$6.83 USD` (~$7-8 con impuestos) ahorro $3.42 (33%). Ventana CDMX `UTC-6`: destroy `0 7 * * * UTC` / deploy `0 15 * * * UTC` vía `schedule-destroy.yml` (4.4) y `schedule-deploy.yml` (4.5) `America/Mexico_City`. GitHub schedule solo UTC desde `main`, retardo 5-15m tolerado. ALB DNS cambia diario — no usar IP. Destruir día 14 final con `destroy-aws.sh` garantiza no pagar día 15. B: destroy no borra DB externa 188.40.211.8.**

---

### Orden estricto

`0.1→0.4 → 1.1→1.4 → 2.1→2.5 (2.1B/2.2B con EXTERNAL_DB_HOST) → 3.1→3.4 (3.3B vars externas) → 3.5.0→3.5.6 Gate Pre-Deploy Vars/Secrets (BLOQUEANTE, $0, no facturación hasta PASS) → 4.0→4.5 (4.0B validación DB externa, 4.1B sin RDS 3-5m vs 8-12m, 4.2B deploy-app.sh EXTERNAL_DB_HOST, 4.3B health contra externa, 4.4 destroy 01:00 MX 0 7 UTC, 4.5 deploy 09:00 MX 0 15 UTC America/Mexico_City) → 5.1→5.2 (5.2 cíclico 16h on 09→01 MX / 8h off 01→09 MX 224h vs 336h) → 6.1→6.2`

> **Nota Gate 3.5:** `Bloque 3` pasó a `🔄 Validar`/`⚠️ Parcialmente` para re-auditoría sin re-crear infra. `3.5.0→3.5.6` son nuevos `⏳ Pendiente` y deben dar **PASS** antes de `4.1 Inicia facturación`. `2.3` también `⚠️ Parcialmente` hasta patch `deploy-aws.sh:25` en `3.5.4`.
