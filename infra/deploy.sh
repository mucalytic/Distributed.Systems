#!/usr/bin/env bash
# Provisions all Azure resources and deploys the three services to Container Apps.
# Idempotent: safe to re-run; the unique-name suffix is persisted in infra/.suffix.
set -euo pipefail
cd "$(dirname "$0")/.."

LOCATION=northeurope
RG=order-system-rg
ACA_ENV=ordersys-env

SUFFIX_FILE=infra/.suffix
[[ -f $SUFFIX_FILE ]] || openssl rand -hex 3 > "$SUFFIX_FILE"
SUFFIX=$(cat "$SUFFIX_FILE")

SB_NAMESPACE=ordersys-sb-$SUFFIX      # globally unique
COSMOS_ACCOUNT=ordersys-cosmos-$SUFFIX # globally unique
ACR_NAME=ordersysacr$SUFFIX            # alphanumeric only

echo "==> az extensions & resource providers"
az extension add --name containerapp --upgrade --only-show-errors
for ns in Microsoft.App Microsoft.OperationalInsights Microsoft.ServiceBus Microsoft.DocumentDB Microsoft.ContainerRegistry; do
  az provider register -n $ns --wait
done

echo "==> Resource group $RG in $LOCATION"
az group create -n "$RG" -l "$LOCATION" -o none

echo "==> Service Bus namespace $SB_NAMESPACE (Standard tier — NServiceBus pub/sub needs topics)"
az servicebus namespace create -g "$RG" -n "$SB_NAMESPACE" --sku Standard -o none

echo "==> Cosmos DB account $COSMOS_ACCOUNT (serverless) — the slow one, ~5-10 min"
# westeurope: northeurope had no capacity for new Cosmos accounts (2026-07).
# The account region is independent of the resource group region.
az cosmosdb create -g "$RG" -n "$COSMOS_ACCOUNT" --capabilities EnableServerless \
    --locations regionName=westeurope failoverPriority=0 isZoneRedundant=false -o none
az cosmosdb sql database create -g "$RG" -a "$COSMOS_ACCOUNT" -n ordersdb -o none
az cosmosdb sql container create -g "$RG" -a "$COSMOS_ACCOUNT" -d ordersdb -n orders   --partition-key-path /id -o none
az cosmosdb sql container create -g "$RG" -a "$COSMOS_ACCOUNT" -d ordersdb -n invoices --partition-key-path /id -o none

echo "==> Container registry $ACR_NAME"
az acr create -g "$RG" -n "$ACR_NAME" --sku Basic --admin-enabled true -o none

echo "==> Container Apps environment $ACA_ENV"
az containerapp env create -g "$RG" -n "$ACA_ENV" -l "$LOCATION" -o none

echo "==> Building images in ACR (cloud build — no local docker push needed)"
for proj in Orders.Api Orders.Worker Billing.Worker; do
  img=$(echo "$proj" | tr '[:upper:].' '[:lower:]-')
  az acr build -r "$ACR_NAME" -t "$img:v1" --build-arg PROJECT="$proj" .
done

echo "==> Collecting connection strings"
SB_CONN=$(az servicebus namespace authorization-rule keys list -g "$RG" \
  --namespace-name "$SB_NAMESPACE" -n RootManageSharedAccessKey \
  --query primaryConnectionString -o tsv)
COSMOS_CONN=$(az cosmosdb keys list -g "$RG" -n "$COSMOS_ACCOUNT" \
  --type connection-strings --query 'connectionStrings[0].connectionString' -o tsv)
ACR_SERVER=$(az acr show -n "$ACR_NAME" --query loginServer -o tsv)
ACR_USER=$(az acr credential show -n "$ACR_NAME" --query username -o tsv)
ACR_PASS=$(az acr credential show -n "$ACR_NAME" --query 'passwords[0].value' -o tsv)

common_args=(-g "$RG" --environment "$ACA_ENV"
  --registry-server "$ACR_SERVER" --registry-username "$ACR_USER" --registry-password "$ACR_PASS"
  --secrets "sb-conn=$SB_CONN" "cosmos-conn=$COSMOS_CONN"
  --env-vars "ServiceBus__ConnectionString=secretref:sb-conn" "Cosmos__ConnectionString=secretref:cosmos-conn"
  --cpu 0.25 --memory 0.5Gi --min-replicas 1 --max-replicas 1)

echo "==> Deploying workers (first, so their installers create the queues/topics)"
az containerapp create -n orders-worker  --image "$ACR_SERVER/orders-worker:v1"  "${common_args[@]}" -o none
az containerapp create -n billing-worker --image "$ACR_SERVER/billing-worker:v1" "${common_args[@]}" -o none

echo "==> Deploying API with external ingress"
az containerapp create -n orders-api --image "$ACR_SERVER/orders-api:v1" "${common_args[@]}" \
  --ingress external --target-port 8080 -o none

FQDN=$(az containerapp show -n orders-api -g "$RG" --query properties.configuration.ingress.fqdn -o tsv)
echo ""
echo "=================================================================="
echo "  Done. Place an order with:"
echo "  curl -si -X POST https://$FQDN/orders"
echo "=================================================================="
