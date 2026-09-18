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
echo "ALBDNS=$ALBDNS"
echo "RDS_ADDRESS=$RDS_ADDR"
echo "EC2PublicIP=$(aws cloudformation describe-stacks --stack-name "$STACK" --region "$REGION" --query "Stacks[0].Outputs[?OutputKey=='EC2PublicIP'].OutputValue" --output text)"

echo "[deploy-aws] Stack $STACK CREATE_COMPLETE"
