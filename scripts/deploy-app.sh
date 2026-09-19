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

# Deploy via SSM con env inyectados (NoEcho via GH Secrets -> env)
echo "[deploy-app] docker compose pull + up -d via SSM (Tag=$TAG)..."
SSM_CMD="export TAG=$TAG STACK_NAME=$STACK AWS_REGION=$REGION RDS_ADDRESS=$RDS_ADDRESS DB_NAME=$DB_NAME SKIP_MIGRATION=$SKIP_MIGRATION DB_USER=$DB_USER DB_PASSWORD='$DB_PASSWORD' JWT_KEY_PROD='$JWT_KEY_PROD' ALB_DNS=$ALBDNS CORS_ALLOWED_ORIGIN='$CORS_ALLOWED_ORIGIN' JWT_ISSUER=http://$ALBDNS JWT_AUDIENCE=http://$ALBDNS && cd /home/ec2-user && /usr/bin/docker compose -f docker-compose.aws.yml pull && /usr/bin/docker compose -f docker-compose.aws.yml up -d && /usr/bin/docker ps || (docker compose -f docker-compose.aws.yml pull && docker compose -f docker-compose.aws.yml up -d && docker ps)"
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

echo "[deploy-app] Deploy OK Tag=$TAG ALB=http://$ALBDNS/health/ready"
