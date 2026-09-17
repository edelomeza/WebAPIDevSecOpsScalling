# MemoriaAWS — Plan Despliegue AWS 14 Días

**Proyecto:** `WebAPIDevSecOpsScalling` | **Imagen:** `edelomeza/webapidevsecops-scalling` (nueva, no `edelomeza/webapidevsecops` existente) | **Región:** `us-east-1` (más barata) | **Duración:** 14 días (336h) persistente para pruebas web/móvil | **Infra Option A:** `RDS db.t3.micro sqlserver-ex + EC2 t3.micro + ALB + SQS FIFO x4 + CW Logs` | **Infra Option B (externa):** `EC2 t3.micro + ALB + SQS FIFO x4 + CW Logs + DB externa 188.40.211.8/db45497 sin RDS/DBSubnetGroup/SGRDS` (ver 2.1B/3.3B/4.0B)

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
| 2.3 | **Local** | `deploy-aws.sh`/`deploy-app.sh`/`destroy-aws.sh` | `cloudformation deploy` + `scp` + `docker compose up -d` + `curl /health/ready` + `delete-stack` | `scripts/*.sh` | $0 | ✅ Concluido | set -euo pipefail, GH Secrets env, SSM sin 22, wait nativo, idempotente |
| 2.4 | **Local** | `deploy.yml` | `workflow_dispatch` `REGISTRY_IMAGE=edelomeza/webapidevsecops-scalling` | `.github/workflows/deploy.yml` | $0 | ✅ Concluido | workflow_dispatch Tag sha ya pusheado, vars CORS/ACM, concurrency deploy-production false, timeout 25, env production |
| 2.5 | **Local** | Validar | `cfn-lint`, `validate-template`, `docker-compose config`, `bash -n` | — | $0 | ✅ Concluido | cfn-lint W1011 OK, validate AWS, compose config, bash -n 3/3, actionlint - Listo despliegue |

---

### BLOQUE 3 — GitHub (imagen nueva corregida)

| # | Entorno | Paso | Descripción | Costo | Estado | DescripcionDev |
|---|---|---|---|---|---|---|
| 3.1 | **GitHub** | Docker Hub repo | Crear `hub.docker.com/repository/create` → `edelomeza/webapidevsecops-scalling` **Public** (si Private, EC2 necesitaría `docker login`) | $0 | ✅ Concluido | Public sanitizado sin secrets, sin login EC2, scalling Tag sha trazable |
| 3.2 | **GitHub** | Secrets | `Secrets: AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY, DB_PASSWORD, JWT_KEY_PROD (openssl rand -base64 32 → 44c >32B)` | $0 | ✅ Concluido | JWT 44c aislado, DB NoEcho $0, DOCKER_PAT R/W, AWS mínimas |
| 3.3 | **GitHub** | **Variables (corregido) + Option B** | `Variables: AWS_REGION=us-east-1`, `REGISTRY_IMAGE=edelomeza/webapidevsecops-scalling`, `RDS_ADDRESS=188.40.211.8`, `DB_NAME=db45497`, `CORS_ALLOWED_ORIGIN=https://localhost:7064` | $0 | ✅ Concluido | Variables Only B validadas y passthrough `deploy.yml:38-41/45-50` `RDS_ADDRESS/DB_NAME` → `docker-compose.aws.yml:29` + `Program.cs:122`, Secrets `DB_PASSWORD/JWT_KEY_PROD` NoEcho |
| 3.4 | **GitHub** | Environment | `production` con `Required reviewers: edelomeza` | $0 | ✅ Concluido | production 1/2 edelomeza+edelmezamx, main custom, vars Global, self-review off |

---

### BLOQUE 4 — AWS Deploy (14 días cuentan desde aquí)

| # | Entorno | Paso | Descripción | Costo | Estado | DescripcionDev |
|---|---|---|---|---|---|---|
| 4.0 | **GitHub** | DB externa `ExternalDbHost` | Consolidación 2.1B+3.3B: `GH Variables RDS_ADDRESS=188.40.211.8, DB_NAME=db45497` + `CFN ExternalDbHost param / CreateRDS: !Equals [ExternalDbHost,""] / ExternalDbHostUsed output` → `compose Server=${RDS_ADDRESS}` `Database=${DB_NAME:-db45497}` — si `ExternalDbHost≠""` evita crear `RDS db.t3.micro` | $0 | ✅ Concluido | Documenta consolidación; condicional evita RDS cuando hay host externo (ahorro tiempo 8-12m→3-5m y costo). Implementación IaC/script pendiente en 4.1-4.2 |
| 4.1 | **AWS** | `bash deploy-aws.sh webapidevsecops-prod` | **Option A:** `CREATE_COMPLETE` 8-12m (RDS lento). **Option B:** `CREATE_COMPLETE` 3-5m sin RDS/DBSubnetGroup/SGRDS (`ExternalDbHost=188.40.211.8`), `parameter-overrides ExternalDbHost` sin `DBPassword` | **Inicia facturación** — ver cálculo abajo (B sin RDS) | ⏳ Pendiente | **A:** VPC+RDS+ALB+SQS. **B:** `aws cloudformation deploy --parameter-overrides ImageTag, CorsAllowedOrigin, AcmCertificateArn, ExternalDbHost` `scripts/deploy-aws.sh:25-29`, `cloudformation.yml:9-13,25-30` params RDS eliminados cuando B |
| 4.2 | **EC2** | `deploy-app.sh` | `docker pull edelomeza/webapidevsecops-scalling:${sha}` + `up -d` + migr `Program.cs:464`. **Option B:** `RDS_ADDRESS=$EXTERNAL_DB_HOST` priorizado `deploy-app.sh:33` sobre `describe-stacks Output RDSAddress/ExternalDbHostUsed` → `docker-compose.aws.yml:29` `ConnectionStrings__DefaultConnection` + `DB_USER/DB_PASSWORD` `Program.cs:117-128` `SqlConnectionStringBuilder` → `ctx.Database.Migrate():487` guard `SkipMigration` | $0 | ⏳ Pendiente | EC2 UserData ya instaló docker. B: `SSM export RDS_ADDRESS DB_NAME` `deploy-app.sh:78` + health `Program.cs:154` `AddSqlServer` misma conexión externa; Guard SSM: pre-check `sqlcmd -S $RDS_ADDRESS -U $DB_USER -Q "SELECT 1"` (SSM) antes de `Migrate()`; si falla exporta `SKIP_MIGRATION=true` y log WARN sin DDL |
| 4.3 | **AWS** | Verificar | `curl http://$ALB_DNS/health/ready` `200` (tag `db` `Program.cs:585` + `AddSqlServer:154`), SQS 4 `fifo` vacías luego con mensajes tras saga. **Option B:** `/health/ready` contra `188.40.211.8/db45497` | $0 | ⏳ Pendiente | Smoke test ALB→TG→EC2→(RDS A o externa B)+SQS `ApproximateNumberOfMessages`. `deploy.yml:53-56` `curl -sf http://$ALB_DNS/health/ready` válido para B |

---

### BLOQUE 5 — Pruebas 14 días

| # | Entorno | Paso | Descripción | Costo | Estado | DescripcionDev |
|---|---|---|---|---|---|---|
| 5.1 | **Web/Móvil** | `API_BASE_URL=http://$ALB_DNS` | CORS `CORS_ALLOWED_ORIGIN=https://localhost:7064` (Razor) + `Jwt Issuer/Audience=http://$ALB_DNS` (`deploy-app.sh:78`) | incluido en ALB LCU | ⏳ Pendiente | Apps Razor (https://localhost:7064) y Kotlin (`API_BASE_URL` + `usesCleartextTraffic=true` si ALB http) apuntan a ALB DNS estable, no IP efímera |
| 5.2 | **AWS** | Operación | Mantener 24/7 14d. No parar (pruebas intermitentes desde apps) | ver total | ⏳ Pendiente | 336h continuas, sin stop para medir costo real; Nota: sin DDL/seed en runtime — no `Migrate()` automático (SkipMigration=true B); esquema solo vía SSM manual; app solo DML/SELECT contra `db45497` |

---

### BLOQUE 6 — Destrucción (día 14)

| # | Entorno | Paso | Descripción | Costo | Estado | DescripcionDev |
|---|---|---|---|---|---|---|
| 6.1 | **AWS** | `bash destroy-aws.sh webapidevsecops-prod` | `delete-stack` 8-10m (A) / 3-5m (B sin RDS) → `list-stacks` vacío, Consola `EC2 0,ALB 0,SQS 0` (+ `RDS 0` solo A) | **Corta facturación** | ⏳ Pendiente | Borrado en cascada con DeletionPolicy Delete, verifica 0 recursos. B: no borra DB externa 188.40.211.8 |
| 6.2 | **AWS** | Limpieza | `IAM Delete Access Key` + `Billing → Bills` prorrateo 14d | $0 | ⏳ Pendiente | Revoca credenciales deploy-dev y cierra costo |

---

### Cálculo 14 días (336h) — Cuenta Nueva Free Tier 12 meses

| Recurso | Tarifa `us-east-1` | Cálculo 336h | Costo 14d | Estado |
|---|---|---|---|---|
| **ALB** | $0.0225/h | 336 × 0.0225 | **$7.56** | ⏳ Pendiente |
| **ALB LCU** (tráfico pruebas bajo, ~1 LCU) | $0.008/h | 336 × 0.008 | **$2.69** | ⏳ Pendiente |
| **EC2 t3.micro** | $0.0104/h | 336h **dentro 750h free tier** | **$0.00** | ⏳ Pendiente |
| **RDS db.t3.micro sqlserver-ex (Option A)** | $0.022/h + $0.115/GB | 336h **dentro 750h free tier**, 20GB gp2 $2.30/mes ×14/30 | **$0.00** | ⏳ Pendiente (A) |
| **RDS db.t3.micro sqlserver-ex (Option B: DB externa)** | $0.022/h + $0.115/GB | **$0.00 — NO se crea** (`cloudformation.yml:198-222` eliminado, `DBSubnetGroup:123` y `SGRDS:164` eliminados) — costo externo fuera de AWS | **$0.00** | ⏳ Pendiente (B) |
| **EBS 30GB gp3** | $0.08/GB | **dentro free tier EC2 30GB** (RDS 20GB gp2 no aplica en B) | **$0.00** | ⏳ Pendiente |
| **SQS FIFO x4** | $0.50/M | <10k req <1M free | **$0.00** | ⏳ Pendiente |
| **CW Logs** | $0.50/GB | ~0.7GB 14d <5GB free | **$0.00** | ⏳ Pendiente |
| **Data Transfer** | $0.09/GB | <1GB free tier 100GB | **$0.00** | ⏳ Pendiente |
| **TOTAL A PAGAR 14 DÍAS Option A (con RDS free tier)** | | | **~$10.25** | ⏳ Pendiente |
| **TOTAL A PAGAR 14 DÍAS Option B (DB externa)** | | | **~$10.25** (ALB $7.56+LCU $2.69) → en free tier idéntico; diferencia real fuera free tier ver abajo | ⏳ Pendiente |

> **Fuera de free tier** (cuenta >12m): **Option A:** EC2 $3.49 + RDS $7.39 + storage $1.07 + EBS $1.12 + ALB $10.25 = **~$23.32** por 14d (~$50/mes). **Option B (externa):** EC2 $3.49 + EBS $1.12 + ALB $10.25 = **~$14.86** por 14d (~$32/mes) + costo DB externa fuera de AWS (RDS ahorro ~$8.46/14d, ~$18/mes).

**Monto final con tu caso (cuenta nueva, 2 semanas, `edelomeza/webapidevsecops-scalling`): Option A `~$10.25 USD` / Option B `~$10.25 USD` (~$10-11 con impuestos/LCU) — en free tier iguales; B ahorra ~$8.46 fuera free tier. Destruir el día 14 con `destroy-aws.sh` garantiza no pagar día 15. B: destroy no borra DB externa 188.40.211.8.**

---

### Orden estricto

`0.1→0.4 → 1.1→1.4 → 2.1→2.5 (2.1B/2.2B con EXTERNAL_DB_HOST) → 3.1→3.4 (3.3B vars externas) → 4.0→4.3 (4.0B validación DB externa, 4.1B sin RDS 3-5m vs 8-12m, 4.2B deploy-app.sh EXTERNAL_DB_HOST, 4.3B health contra externa) → 5.1→5.2 → 6.1→6.2`
