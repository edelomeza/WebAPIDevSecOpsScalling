#!/bin/bash
set -euo pipefail

STACK="${1:-webapidevsecops-prod}"
TAG="${2:-latest}"
REGION="${AWS_REGION:-us-east-1}"

: "${AWS_ACCESS_KEY_ID:?AWS_ACCESS_KEY_ID no exportada (GH Secrets)}"
: "${AWS_SECRET_ACCESS_KEY:?AWS_SECRET_ACCESS_KEY no exportada}"
: "${DB_PASSWORD:?DB_PASSWORD no exportada (GH Secrets)}"
: "${JWT_KEY_PROD:?JWT_KEY_PROD no exportada (GH Secrets, >=32B)}"

CORS_ALLOWED_ORIGIN="${CORS_ALLOWED_ORIGIN:-}"
DB_USER="${DB_USER:-sa}"
ALB_DNS_OVERRIDE="${ALB_DNS:-}"

echo "[deploy-app] Stack=$STACK Tag=$TAG Region=$REGION"

if [[ ! -f "deploy/docker-compose.aws.yml" ]]; then
  echo "ERROR: deploy/docker-compose.aws.yml no encontrado" >&2
  exit 1
fi

ALBDNS="${ALB_DNS_OVERRIDE}"
if [[ -z "$ALBDNS" ]]; then
  ALBDNS=$(aws cloudformation describe-stacks --stack-name "$STACK" --region "$REGION" --query "Stacks[0].Outputs[?OutputKey=='ALBDNS'].OutputValue" --output text 2>/dev/null || true)
fi
if [[ -z "$ALBDNS" || "$ALBDNS" == "None" ]]; then
  echo "ERROR: No se pudo obtener ALBDNS del stack $STACK" >&2
  exit 1
fi

# CloudFront $0 HTTPS (opcional, si el stack ya lo expone)
CLOUDFRONT_DOMAIN="${CLOUDFRONT_DOMAIN:-}"
if [[ -z "$CLOUDFRONT_DOMAIN" ]]; then
  CLOUDFRONT_DOMAIN=$(aws cloudformation describe-stacks --stack-name "$STACK" --region "$REGION" --query "Stacks[0].Outputs[?OutputKey=='CloudFrontDomain'].OutputValue" --output text 2>/dev/null || echo "")
  if [[ "$CLOUDFRONT_DOMAIN" == "None" ]]; then CLOUDFRONT_DOMAIN=""; fi
fi
if [[ -n "$CLOUDFRONT_DOMAIN" ]]; then
  echo "[deploy-app] CLOUDFRONT_DOMAIN=$CLOUDFRONT_DOMAIN (https via CloudFront)"
else
  echo "[deploy-app] CLOUDFRONT_DOMAIN vacio -> fallback http://ALB (aprendizaje sin https o stack previo)"
fi

# Only B: priorizar RDS_ADDRESS inyectado (GH vars 188.40.211.8) sobre CFN
if [[ -n "${RDS_ADDRESS:-}" && "$RDS_ADDRESS" != "None" ]]; then
  echo "[deploy-app] Usando RDS_ADDRESS inyectado $RDS_ADDRESS (vars.RDS_ADDRESS)"
else
  RDS_ADDRESS=$(aws cloudformation describe-stacks --stack-name "$STACK" --region "$REGION" --query "Stacks[0].Outputs[?OutputKey=='ExternalDbHostUsed'].OutputValue" --output text 2>/dev/null || echo "")
  if [[ -z "$RDS_ADDRESS" || "$RDS_ADDRESS" == "None" ]]; then
    RDS_ADDRESS=$(aws cloudformation describe-stacks --stack-name "$STACK" --region "$REGION" --query "Stacks[0].Outputs[?OutputKey=='RDSAddress'].OutputValue" --output text 2>/dev/null || echo "")
  fi
  if [[ -z "$RDS_ADDRESS" || "$RDS_ADDRESS" == "None" ]]; then
    RDS_ADDRESS=$(aws cloudformation describe-stacks --stack-name "$STACK" --region "$REGION" --query "Stacks[0].Outputs[? contains(OutputKey,'RDS')].OutputValue" --output text 2>/dev/null || echo "external-sql")
  fi
fi
# Auto SKIP_MIGRATION para externa 188.40.211.8
SKIP_MIGRATION="${SKIP_MIGRATION:-}"
if [[ "$RDS_ADDRESS" == "188.40.211.8" ]]; then SKIP_MIGRATION="true"; fi
DB_NAME="${DB_NAME:-db45497}"
# SagaBridge ON permanente en prod (inyecta Feature__SagaBridge al compose via SSM).
# Reversible sin rebuild: SAGA_BRIDGE=false bash scripts/deploy-app.sh <stack> <tag>
SAGA_BRIDGE="${SAGA_BRIDGE:-true}"

# EC2 InstanceId via tag cloudformation stack-name (SSM Zero Trust, sin 22/pem)
EC2_ID=$(aws ec2 describe-instances --region "$REGION" --filters "Name=tag:aws:cloudformation:stack-name,Values=$STACK" "Name=instance-state-name,Values=running" --query "Reservations[0].Instances[0].InstanceId" --output text 2>/dev/null || true)
if [[ -z "$EC2_ID" || "$EC2_ID" == "None" ]]; then
  echo "ERROR: No se encontro EC2 InstanceId para stack $STACK (SSM requiere EC2 running)" >&2
  exit 1
fi
echo "[deploy-app] ALBDNS=$ALBDNS RDS_ADDRESS=$RDS_ADDRESS EC2_ID=$EC2_ID"

# Helper: SSM send-command + wait
ssm_run() {
  local desc="$1"; shift
  local cmd="$*"
  echo "[ssm] $desc"
  local cid
  cid=$(aws ssm send-command --instance-ids "$EC2_ID" --document-name "AWS-RunShellScript" --region "$REGION" --parameters "commands=[\"$cmd\"]" --query "Command.CommandId" --output text)
  echo "[ssm] CommandId=$cid"
  for _ in $(seq 1 48); do
    sleep 5
    local status
    status=$(aws ssm list-command-invocations --command-id "$cid" --details --region "$REGION" --query "CommandInvocations[0].Status" --output text 2>/dev/null || echo "Pending")
    echo "[ssm] status=$status"
    if [[ "$status" == "Success" ]]; then return 0; fi
    if [[ "$status" == "Failed" || "$status" == "Cancelled" || "$status" == "TimedOut" ]]; then
      aws ssm list-command-invocations --command-id "$cid" --details --region "$REGION" --output table || true
      echo "ERROR: SSM command $cid $status" >&2
      return 1
    fi
  done
  echo "ERROR: SSM command $cid timeout (240s)" >&2
  return 1
}

# Transferir docker-compose.aws.yml via SSM (sin scp/pem)
echo "[deploy-app] Transfiriendo docker-compose.aws.yml via SSM..."
# Codificar en base64 para evitar quoting
COMPOSE_B64=$(base64 -w 0 deploy/docker-compose.aws.yml 2>/dev/null || base64 -b 0 deploy/docker-compose.aws.yml)
ssm_run "crear docker-compose.aws.yml" "echo $COMPOSE_B64 | base64 -d > /home/ec2-user/docker-compose.aws.yml && ls -l /home/ec2-user/docker-compose.aws.yml"

# Preflight: asegurar docker + compose plugin (fix race UserData / docker: command not found)
# Idempotente: si UserData aun no termino o dnf update fallo, instala aqui sin abortar deploy
echo "[deploy-app] Preflight docker via SSM (espera hasta 180s)..."
ENSURE_DOCKER='set -e; if ! command -v docker >/dev/null 2>&1; then echo "[ensure-docker] docker no encontrado, instalando..."; dnf install -y docker aws-cli || dnf install -y docker; systemctl enable --now docker; fi; for i in $(seq 1 36); do if command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then break; fi; echo "[ensure-docker] esperando docker $i/36..."; sleep 5; if ! command -v docker >/dev/null 2>&1; then dnf install -y docker && systemctl enable --now docker; fi; done; docker --version || { echo "ERROR: docker no disponible tras espera" >&2; systemctl status docker 2>&1 || true; cat /var/log/cloud-init-output.log 2>&1 | tail -n 100 || true; cat /var/log/user-data.log 2>&1 | tail -n 100 || true; exit 1; }; if ! docker compose version >/dev/null 2>&1; then echo "[ensure-docker] instalando docker compose plugin..."; mkdir -p /usr/local/lib/docker/cli-plugins; curl -L --retry 5 --retry-delay 5 --fail https://github.com/docker/compose/releases/latest/download/docker-compose-linux-x86_64 -o /usr/local/lib/docker/cli-plugins/docker-compose; chmod +x /usr/local/lib/docker/cli-plugins/docker-compose; ln -sf /usr/local/lib/docker/cli-plugins/docker-compose /usr/local/bin/docker-compose; docker compose version; fi; systemctl is-active docker >/dev/null 2>&1 || systemctl restart docker; echo "[ensure-docker] docker listo: $(docker --version) $(docker compose version 2>&1 | head -n 1)"'
# Escapar comillas para send-command (evita romper JSON)
ENSURE_ESCAPED=$(printf '%s' "$ENSURE_DOCKER" | sed 's/"/\\"/g')
ssm_run "ensure docker" "$ENSURE_ESCAPED"

# JWT Issuer/Audience: si hay CloudFront usa https://<cf> (cert valido), sino fallback http://ALB
if [[ -n "$CLOUDFRONT_DOMAIN" ]]; then
  JWT_ISSUER="https://$CLOUDFRONT_DOMAIN"
  JWT_AUDIENCE="https://$CLOUDFRONT_DOMAIN"
else
  JWT_ISSUER="http://$ALBDNS"
  JWT_AUDIENCE="http://$ALBDNS"
fi
echo "[deploy-app] JWT_ISSUER=$JWT_ISSUER JWT_AUDIENCE=$JWT_AUDIENCE"

# Preflight: verificar que la imagen TAG existe en Docker Hub antes de SSM (evita manifest unknown tardio)
REGISTRY_IMAGE="${REGISTRY_IMAGE:-edelomeza/webapidevsecops-scalling}"
echo "[deploy-app] Preflight: verificando imagen ${REGISTRY_IMAGE}:${TAG} en Docker Hub..."
# Intenta Hub API publica; si falla intenta docker manifest inspect local
if curl -sf "https://registry.hub.docker.com/v2/repositories/${REGISTRY_IMAGE}/tags/${TAG}" >/dev/null 2>&1; then
  echo "[deploy-app] Preflight OK: imagen ${REGISTRY_IMAGE}:${TAG} existe (Hub API)"
else
  if command -v docker >/dev/null 2>&1 && docker manifest inspect "${REGISTRY_IMAGE}:${TAG}" >/dev/null 2>&1; then
    echo "[deploy-app] Preflight OK: imagen ${REGISTRY_IMAGE}:${TAG} existe (docker manifest inspect)"
  elif command -v docker >/dev/null 2>&1 && docker buildx imagetools inspect "${REGISTRY_IMAGE}:${TAG}" >/dev/null 2>&1; then
    echo "[deploy-app] Preflight OK: imagen ${REGISTRY_IMAGE}:${TAG} existe (buildx imagetools)"
  else
    echo "[deploy-app] ERROR: imagen ${REGISTRY_IMAGE}:${TAG} no encontrada (manifest unknown)" >&2
    echo "[deploy-app] Causa probable: commit solo .md sin build Docker (antes filtrado por paths-ignore) o push de CI fallo" >&2
    echo "[deploy-app] Accion inmediata (trazabilidad): re-ejecutar CI/CD con workflow_dispatch para ese SHA o usar ultimo SHA con codigo:" >&2
    echo "  git log --oneline origin/main --diff-filter=AM -- '**.cs' 'Dockerfile' 'deploy/**' '.github/workflows/**' | head -5" >&2
    echo "  # Luego: bash scripts/deploy-app.sh $STACK <ultimo-sha-con-codigo>" >&2
    echo "[deploy-app] Alternativa trazable: gh workflow run \"CI/CD Pipeline\" --ref main  # reconstruye ${TAG} si es HEAD" >&2
    exit 1
  fi
fi

# Deploy via SSM con env inyectados (NoEcho via GH Secrets -> env)
echo "[deploy-app] docker compose pull + up -d via SSM (Tag=$TAG)..."
SSM_CMD="export TAG=$TAG STACK_NAME=$STACK AWS_REGION=$REGION RDS_ADDRESS=$RDS_ADDRESS DB_NAME=$DB_NAME SKIP_MIGRATION=$SKIP_MIGRATION SAGA_BRIDGE=$SAGA_BRIDGE DB_USER=$DB_USER DB_PASSWORD='$DB_PASSWORD' JWT_KEY_PROD='$JWT_KEY_PROD' ALB_DNS=$ALBDNS CLOUDFRONT_DOMAIN=$CLOUDFRONT_DOMAIN CORS_ALLOWED_ORIGIN='$CORS_ALLOWED_ORIGIN' JWT_ISSUER=$JWT_ISSUER JWT_AUDIENCE=$JWT_AUDIENCE StackName=$STACK STACK_NAME=$STACK && cd /home/ec2-user && ( /usr/bin/docker compose -f docker-compose.aws.yml pull || { echo \"ERROR: manifest unknown para ${REGISTRY_IMAGE}:$TAG - verifica ci-cd.yml changes job y que la imagen fue publicada\" >&2; exit 1; }) && /usr/bin/docker compose -f docker-compose.aws.yml up -d && /usr/bin/docker ps || (docker compose -f docker-compose.aws.yml pull || { echo \"ERROR: manifest unknown retry\" >&2; exit 1; } && docker compose -f docker-compose.aws.yml up -d && docker ps)"
# Escapar comillas para send-command
ESCAPED=$(printf '%s' "$SSM_CMD" | sed 's/"/\\"/g')
ssm_run "docker compose up" "$ESCAPED"

echo "[deploy-app] Esperando health /health/ready via ALB..."
for i in $(seq 1 18); do
  if curl -sf "http://$ALBDNS/health/ready" >/dev/null; then
    echo "[deploy-app] health 200 OK (intento $i)"
    break
  fi
  echo "[deploy-app] health no listo intento $i/18..."
  sleep 10
  if [[ "$i" -eq 18 ]]; then
    echo "ERROR: /health/ready no respondio 200 tras 180s" >&2
    curl -i "http://$ALBDNS/health/ready" || true
    exit 1
  fi
done

# Verificacion https via CloudFront si existe (no bloqueante, 30s)
if [[ -n "$CLOUDFRONT_DOMAIN" ]]; then
  echo "[deploy-app] Verificando https CloudFront https://$CLOUDFRONT_DOMAIN/health/ready (hasta 60s, CF puede tardar Deploy)..."
  for j in $(seq 1 6); do
    if curl -sf "https://$CLOUDFRONT_DOMAIN/health/ready" >/dev/null; then
      echo "[deploy-app] CloudFront https 200 OK (intento $j)"
      break
    fi
    echo "[deploy-app] CloudFront https no listo intento $j/6..."
    sleep 10
  done
  if ! curl -sf "https://$CLOUDFRONT_DOMAIN/health/ready" >/dev/null; then
    echo "[deploy-app] WARN https://$CLOUDFRONT_DOMAIN/health/ready no responde aun (CF puede tardar 5-15 min en Deployed), ALB http ya OK" >&2
  fi
fi

echo "[deploy-app] Deploy OK Tag=$TAG ALB=http://$ALBDNS/health/ready CLOUDFRONT=${CLOUDFRONT_DOMAIN:+https://$CLOUDFRONT_DOMAIN/health/ready}"
