#!/bin/bash
set -euo pipefail

STACK="${1:-webapidevsecops-prod}"
TAG="${2:-latest}"
REGION="${AWS_REGION:-us-east-1}"

: "${AWS_ACCESS_KEY_ID:?AWS_ACCESS_KEY_ID no exportada (inyectar via GH Secrets)}"
: "${AWS_SECRET_ACCESS_KEY:?AWS_SECRET_ACCESS_KEY no exportada (inyectar via GH Secrets)}"
: "${DB_PASSWORD:?DB_PASSWORD no exportada (GH Secrets)}"

echo "[deploy-aws] Stack=$STACK Tag=$TAG Region=$REGION"
aws --version >/dev/null
if [[ ! -f "deploy/aws/cloudformation.yml" ]]; then
  echo "ERROR: deploy/aws/cloudformation.yml no encontrado" >&2
  exit 1
fi

echo "[deploy-aws] cloudformation deploy (idempotente)..."
aws cloudformation deploy \
  --stack-name "$STACK" \
  --template-file deploy/aws/cloudformation.yml \
  --capabilities CAPABILITY_NAMED_IAM \
  --region "$REGION" \
  --parameter-overrides \
    ImageTag="$TAG" \
    DBPassword="$DB_PASSWORD" \
    ExternalDbHost="${RDS_ADDRESS:-188.40.211.8}" \
    DBName="${DB_NAME:-db45497}" \
    SagaBridgeEnabled="${SAGA_BRIDGE:-true}" \
    CorsAllowedOrigin="${CORS_ALLOWED_ORIGIN:-}" \
    AcmCertificateArn="${ACM_CERTIFICATE_ARN:-}" \
  --no-fail-on-empty-changeset

echo "[deploy-aws] wait stack-create/update-complete (nativo, sin timeout Bash)..."
if ! aws cloudformation wait stack-create-complete --stack-name "$STACK" --region "$REGION" 2>/dev/null; then
  aws cloudformation wait stack-update-complete --stack-name "$STACK" --region "$REGION"
fi

echo "[deploy-aws] Outputs:"
aws cloudformation describe-stacks --stack-name "$STACK" --region "$REGION" --query "Stacks[0].Outputs" --output table || true

ALBDNS=$(aws cloudformation describe-stacks --stack-name "$STACK" --region "$REGION" --query "Stacks[0].Outputs[?OutputKey=='ALBDNS'].OutputValue" --output text)
RDS_ADDR=$(aws cloudformation describe-stacks --stack-name "$STACK" --region "$REGION" --query "Stacks[0].Outputs[?OutputKey=='RDSAddress'].OutputValue" --output text)
CLOUDFRONT=$(aws cloudformation describe-stacks --stack-name "$STACK" --region "$REGION" --query "Stacks[0].Outputs[?OutputKey=='CloudFrontDomain'].OutputValue" --output text 2>/dev/null || echo "None")
CLOUDFRONT_URL=$(aws cloudformation describe-stacks --stack-name "$STACK" --region "$REGION" --query "Stacks[0].Outputs[?OutputKey=='CloudFrontUrl'].OutputValue" --output text 2>/dev/null || echo "None")
echo "ALBDNS=$ALBDNS"
echo "RDS_ADDRESS=$RDS_ADDR"
echo "CLOUDFRONT=$CLOUDFRONT"
echo "CLOUDFRONT_URL=$CLOUDFRONT_URL"
echo "EC2PublicIP=$(aws cloudformation describe-stacks --stack-name "$STACK" --region "$REGION" --query "Stacks[0].Outputs[?OutputKey=='EC2PublicIP'].OutputValue" --output text)"

# Esperar SSM online (evita race con deploy-app.sh)
EC2_ID=$(aws ec2 describe-instances --region "$REGION" --filters "Name=tag:aws:cloudformation:stack-name,Values=$STACK" "Name=instance-state-name,Values=running" --query "Reservations[0].Instances[0].InstanceId" --output text 2>/dev/null || echo "None")
if [[ -n "$EC2_ID" && "$EC2_ID" != "None" ]]; then
  echo "[deploy-aws] Esperando SSM online EC2_ID=$EC2_ID (hasta 120s)..."
  for i in $(seq 1 24); do
    PING=$(aws ssm describe-instance-information --region "$REGION" --filters "Key=InstanceIds,Values=$EC2_ID" --query "InstanceInformationList[0].PingStatus" --output text 2>/dev/null || echo "None")
    if [[ "$PING" == "Online" ]]; then echo "[deploy-aws] SSM Online (intento $i)"; break; fi
    echo "[deploy-aws] SSM $PING intento $i/24..."
    sleep 5
    if [[ "$i" -eq 24 ]]; then echo "[deploy-aws] WARN SSM no Online tras 120s, continuo (deploy-app hara ensure-docker)"; fi
  done
fi

echo "[deploy-aws] Stack $STACK CREATE_COMPLETE"
