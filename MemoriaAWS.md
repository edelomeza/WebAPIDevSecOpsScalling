# MemoriaAWS — Plan Despliegue AWS 14 Días

**Proyecto:** `WebAPIDevSecOpsScalling` | **Imagen:** `edelomeza/webapidevsecops-scalling` (nueva, no `edelomeza/webapidevsecops` existente) | **Región:** `us-east-1` (más barata) | **Duración:** 14 días (336h) persistente para pruebas web/móvil | **Infra:** `RDS db.t3.micro sqlserver-ex + EC2 t3.micro + ALB + SQS FIFO x4 + CW Logs`

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
| 2.1 | **Local** | `cloudformation.yml` | 14 recursos `us-east-1` con `image: edelomeza/webapidevsecops-scalling:${TAG}` en UserData. `VPC 10.0.0.0/16`, `SubnetPublicA/B`, `SubnetPrivateA/B`, `DBSubnetGroup`, `SGALB(80)`, `SGEC2(8080←SGALB)`, `SGRDS(1433←SGEC2)`, `IAMRoleEC2`, `RDS db.t3.micro sqlserver-ex 20GB gp2 SingleAZ`, `EC2 t3.micro AL2023`, `SQS FIFO x4`, `LogGroup`, `ALB+TG:8080 /health/ready` | `deploy/aws/cloudformation.yml` ~280l | $0 | ✅ Concluido | IaC sin NAT ($32 ahorro), ALB 80+443 ACM, SG 80/443, CORS param, Delete, Tag sha, NoEcho |
| 2.2 | **Local** | `docker-compose.aws.yml` | `api: edelomeza/webapidevsecops-scalling:${TAG} 8080:8080` + `redis:7-alpine` env `RDS_ADDRESS`, `CORS_ALLOWED_ORIGIN`, `Jwt__Key`, `awslogs` | `deploy/docker-compose.aws.yml` ~50l | $0 | ✅ Concluido | api:${TAG}+redis efimero sin volumen, awslogs 14d, RateLimiting env 5000/5/1000 (Production.json estricto) |
| 2.3 | **Local** | `deploy-aws.sh`/`deploy-app.sh`/`destroy-aws.sh` | `cloudformation deploy` + `scp` + `docker compose up -d` + `curl /health/ready` + `delete-stack` | `scripts/*.sh` | $0 | ✅ Concluido | set -euo pipefail, GH Secrets env, SSM sin 22, wait nativo, idempotente |
| 2.4 | **Local** | `deploy.yml` | `workflow_dispatch` `REGISTRY_IMAGE=edelomeza/webapidevsecops-scalling` | `.github/workflows/deploy.yml` | $0 | ✅ Concluido | workflow_dispatch Tag sha ya pusheado, vars CORS/ACM, concurrency deploy-production false, timeout 25, env production |
| 2.5 | **Local** | Validar | `cfn-lint`, `validate-template`, `docker-compose config`, `bash -n` | — | $0 | ✅ Concluido | cfn-lint W1011 OK, validate AWS, compose config, bash -n 3/3, actionlint - Listo despliegue |

---

### BLOQUE 3 — GitHub (imagen nueva corregida)

| # | Entorno | Paso | Descripción | Costo | Estado | DescripcionDev |
|---|---|---|---|---|---|---|
| 3.1 | **GitHub** | Docker Hub repo | Crear `hub.docker.com/repository/create` → `edelomeza/webapidevsecops-scalling` **Public** (si Private, EC2 necesitaría `docker login`) | $0 | ✅ Concluido | Public sanitizado sin secrets, sin login EC2, scalling Tag sha trazable |
| 3.2 | **GitHub** | Secrets | `Secrets: AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY, DB_PASSWORD, JWT_KEY_PROD (openssl rand -base64 32 → 44c >32B)` | $0 | ✅ Concluido | JWT 44c aislado, DB NoEcho $0, DOCKER_PAT R/W, AWS mínimas |
| 3.3 | **GitHub** | **Variables (corregido)** | `Variables: AWS_REGION=us-east-1`, **`REGISTRY_IMAGE=edelomeza/webapidevsecops-scalling`** (nueva, no `edelomeza/webapidevsecops` del otro proyecto). `ci-cd.yml:123` fallback cambiar a `edelomeza/webapidevsecops-scalling` | $0 | ✅ Concluido | REGISTRY_IMAGE scalling Global, AWS_REGION us-east-1, ci-cd.yml 9x sin fallback var limpio |
| 3.4 | **GitHub** | Environment | `production` con `Required reviewers: edelomeza` | $0 | ✅ Concluido | production 1/2 edelomeza+edelmezamx, main custom, vars Global, self-review off |

---

### BLOQUE 4 — AWS Deploy (14 días cuentan desde aquí)

| # | Entorno | Paso | Descripción | Costo | Estado | DescripcionDev |
|---|---|---|---|---|---|---|
| 4.1 | **AWS** | `bash deploy-aws.sh webapidevsecops-prod` | `CREATE_COMPLETE` 8-12m (RDS lento) | **Inicia facturación** — ver cálculo abajo | ⏳ Pendiente | CloudFormation crea VPC+RDS+ALB+SQS en orden con dependencias |
| 4.2 | **EC2** | `deploy-app.sh` | `docker pull edelomeza/webapidevsecops-scalling:${sha}` + `up -d` + migr `Program.cs:464` | $0 | ⏳ Pendiente | EC2 UserData ya instaló docker, compose hace pull de nueva imagen |
| 4.3 | **AWS** | Verificar | `curl http://$ALB_DNS/health/ready` `200`, SQS 4 `fifo` vacías luego con mensajes tras saga | $0 | ⏳ Pendiente | Smoke test ALB→TG→EC2→RDS y SQS ApproximateNumberOfMessages |

---

### BLOQUE 5 — Pruebas 14 días

| # | Entorno | Paso | Descripción | Costo | Estado | DescripcionDev |
|---|---|---|---|---|---|---|
| 5.1 | **Web/Móvil** | `API_BASE_URL=http://$ALB_DNS` | CORS `CORS_ALLOWED_ORIGIN` + `Jwt Issuer` = `$ALB_DNS` | incluido en ALB LCU | ⏳ Pendiente | Apps web/móvil apuntan a ALB DNS estable, no IP efímera |
| 5.2 | **AWS** | Operación | Mantener 24/7 14d. No parar (pruebas intermitentes desde apps) | ver total | ⏳ Pendiente | 336h continuas, sin stop para medir costo real |

---

### BLOQUE 6 — Destrucción (día 14)

| # | Entorno | Paso | Descripción | Costo | Estado | DescripcionDev |
|---|---|---|---|---|---|---|
| 6.1 | **AWS** | `bash destroy-aws.sh webapidevsecops-prod` | `delete-stack` 8-10m → `list-stacks` vacío, Consola `EC2 0,RDS 0,ALB 0,SQS 0` | **Corta facturación** | ⏳ Pendiente | Borrado en cascada con DeletionPolicy Delete, verifica 0 recursos |
| 6.2 | **AWS** | Limpieza | `IAM Delete Access Key` + `Billing → Bills` prorrateo 14d | $0 | ⏳ Pendiente | Revoca credenciales deploy-dev y cierra costo |

---

### Cálculo 14 días (336h) — Cuenta Nueva Free Tier 12 meses

| Recurso | Tarifa `us-east-1` | Cálculo 336h | Costo 14d | Estado |
|---|---|---|---|---|
| **ALB** | $0.0225/h | 336 × 0.0225 | **$7.56** | ⏳ Pendiente |
| **ALB LCU** (tráfico pruebas bajo, ~1 LCU) | $0.008/h | 336 × 0.008 | **$2.69** | ⏳ Pendiente |
| **EC2 t3.micro** | $0.0104/h | 336h **dentro 750h free tier** | **$0.00** | ⏳ Pendiente |
| **RDS db.t3.micro sqlserver-ex** | $0.022/h + $0.115/GB | 336h **dentro 750h free tier**, 20GB gp2 $2.30/mes ×14/30 | **$0.00** | ⏳ Pendiente |
| **EBS 30GB gp3** | $0.08/GB | **dentro free tier EC2 30GB** | **$0.00** | ⏳ Pendiente |
| **SQS FIFO x4** | $0.50/M | <10k req <1M free | **$0.00** | ⏳ Pendiente |
| **CW Logs** | $0.50/GB | ~0.7GB 14d <5GB free | **$0.00** | ⏳ Pendiente |
| **Data Transfer** | $0.09/GB | <1GB free tier 100GB | **$0.00** | ⏳ Pendiente |
| **TOTAL A PAGAR 14 DÍAS** | | | **~$10.25** | ⏳ Pendiente |

> **Fuera de free tier** (cuenta >12m): EC2 $3.49 + RDS $7.39 + storage $1.07 + EBS $1.12 + ALB $10.25 = **~$23.32** por 14d (~$50/mes).

**Monto final con tu caso (cuenta nueva, 2 semanas, `edelomeza/webapidevsecops-scalling`): `~$10.25 USD` (~$10-11 con impuestos/LCU). Destruir el día 14 con `destroy-aws.sh` garantiza no pagar día 15.**

---

### Orden estricto

`0.1→0.4 → 1.1→1.4 → 2.1→2.5 → 3.1→3.4 → 4.1→4.3 → 5.1→5.2 → 6.1→6.2`
