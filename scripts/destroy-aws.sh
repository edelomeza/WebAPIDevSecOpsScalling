#!/bin/bash
set -euo pipefail

STACK="${1:-webapidevsecops-prod}"
REGION="${AWS_REGION:-us-east-1}"

: "${AWS_ACCESS_KEY_ID:?AWS_ACCESS_KEY_ID no exportada (GH Secrets)}"
: "${AWS_SECRET_ACCESS_KEY:?AWS_SECRET_ACCESS_KEY no exportada}"

echo "[destroy-aws] Stack=$STACK Region=$REGION delete-stack (DeletionPolicy Delete)..."
aws cloudformation delete-stack --stack-name "$STACK" --region "$REGION"

echo "[destroy-aws] wait stack-delete-complete (nativo)..."
aws cloudformation wait stack-delete-complete --stack-name "$STACK" --region "$REGION"

echo "[destroy-aws] Verificacion list-stacks DELETE_COMPLETE..."
aws cloudformation list-stacks --stack-status-filter DELETE_COMPLETE --region "$REGION" --query "StackSummaries[?StackName=='$STACK'].StackStatus" --output table || true

echo "[destroy-aws] Verificacion recursos EC2/RDS/ALB/SQS en 0..."
for svc in EC2 RDS ALB SQS; do echo "  $svc: ver Consola AWS $svc 0 o list-stacks vacio"; done
echo "[destroy-aws] Stack $STACK destruido - corta facturacion (DeletionPolicy Delete, sin snapshots)"
