#!/usr/bin/env bash
set -euo pipefail

LOCATION="${LOCATION:-switzerlandnorth}"
RG="${RG:-dietflow}"
GITHUB_REPO="${GITHUB_REPO:-Bileysh/DietFlow}"
IMAGE="${IMAGE:-ghcr.io/bileysh/dietflow:latest}"
PROJECT="src/DietFlow.Web"

SUB_ID=$(az account show --query id -o tsv)
SUFFIX=$(echo -n "$SUB_ID" | sha1sum | cut -c1-6)
LOGS="dietflow-logs"
INSIGHTS="dietflow-insights"
STORAGE="dietflow${SUFFIX}"
PG="dietflow-pg-${SUFFIX}"
ENV_NAME="dietflow-env"
APP="dietflow-web"
FUNC="dietflow-func-${SUFFIX}"

secret() { dotnet user-secrets list --project "$PROJECT" 2>/dev/null | grep -E "^$1 = " | sed 's/^[^=]*= //' || true; }

echo "==> Resource group $RG ($LOCATION)"
az group create -n "$RG" -l "$LOCATION" -o none

echo "==> Log Analytics + Application Insights (C9)"
az monitor log-analytics workspace create -g "$RG" -n "$LOGS" -l "$LOCATION" -o none
LOGS_ID=$(az monitor log-analytics workspace show -g "$RG" -n "$LOGS" --query customerId -o tsv)
LOGS_KEY=$(az monitor log-analytics workspace get-shared-keys -g "$RG" -n "$LOGS" --query primarySharedKey -o tsv)
az monitor app-insights component create -g "$RG" -a "$INSIGHTS" -l "$LOCATION" --workspace "$LOGS" -o none
INSIGHTS_CS=$(az monitor app-insights component show -g "$RG" -a "$INSIGHTS" --query connectionString -o tsv)

echo "==> Storage account $STORAGE: фото (Blob, C19) і сховище для Azure Functions"
az storage account create -g "$RG" -n "$STORAGE" -l "$LOCATION" --sku Standard_LRS --kind StorageV2 \
  --min-tls-version TLS1_2 --allow-blob-public-access true -o none
STORAGE_CS=$(az storage account show-connection-string -g "$RG" -n "$STORAGE" --query connectionString -o tsv)

echo "==> PostgreSQL Flexible Server $PG (Burstable B1ms — безкоштовно 12 міс. для Azure for Students)"
PG_PASSWORD=$(secret "Azure:PostgresPassword")
if [ -z "$PG_PASSWORD" ]; then
  PG_PASSWORD="Df$(openssl rand -hex 16)!"
  dotnet user-secrets set "Azure:PostgresPassword" "$PG_PASSWORD" --project "$PROJECT" > /dev/null
fi
if ! az postgres flexible-server show -g "$RG" -n "$PG" -o none 2>/dev/null; then
  az postgres flexible-server create -g "$RG" -n "$PG" -l "$LOCATION" \
    --tier Burstable --sku-name Standard_B1ms --storage-size 32 --version 17 \
    --admin-user dietflow --admin-password "$PG_PASSWORD" \
    --public-access 0.0.0.0 --database-name dietflow --yes -o none
fi
az postgres flexible-server parameter set -g "$RG" -s "$PG" --name azure.extensions --value PG_TRGM -o none
MY_IP=$(curl -s https://api.ipify.org)
az postgres flexible-server firewall-rule create -g "$RG" -n "$PG" --rule-name local-dev \
  --start-ip-address "$MY_IP" --end-ip-address "$MY_IP" -o none
DB_CS="Host=${PG}.postgres.database.azure.com;Port=5432;Database=dietflow;Username=dietflow;Password=${PG_PASSWORD};SSL Mode=Require"
dotnet user-secrets set "Azure:DbConnection" "$DB_CS" --project "$PROJECT" > /dev/null

echo "==> Azure Functions $FUNC (B7, Flex Consumption)"
if ! az functionapp show -g "$RG" -n "$FUNC" -o none 2>/dev/null; then
  az functionapp create -g "$RG" -n "$FUNC" --storage-account "$STORAGE" \
    --flexconsumption-location "$LOCATION" --runtime dotnet-isolated --runtime-version 10.0 \
    --app-insights "$INSIGHTS" -o none
fi
FUNC_HOST=$(az functionapp show -g "$RG" -n "$FUNC" --query defaultHostName -o tsv)

echo "==> Container Apps environment + застосунок $APP (C2, C4)"
az containerapp env create -g "$RG" -n "$ENV_NAME" -l "$LOCATION" \
  --logs-workspace-id "$LOGS_ID" --logs-workspace-key "$LOGS_KEY" -o none
if ! az containerapp show -g "$RG" -n "$APP" -o none 2>/dev/null; then
  az containerapp create -g "$RG" -n "$APP" --environment "$ENV_NAME" --image "$IMAGE" \
    --target-port 8080 --ingress external --min-replicas 1 --max-replicas 1 --cpu 0.5 --memory 1.0Gi -o none
fi
APP_FQDN=$(az containerapp show -g "$RG" -n "$APP" --query properties.configuration.ingress.fqdn -o tsv)

SECRETS=("db=$DB_CS" "storage=$STORAGE_CS" "insights=$INSIGHTS_CS")
ENVS=("ASPNETCORE_ENVIRONMENT=Production" "Database__MigrateOnStartup=true"
  "ConnectionStrings__Default=secretref:db"
  "Storage__Provider=AzureBlob" "Storage__AzureBlobConnectionString=secretref:storage"
  "APPLICATIONINSIGHTS_CONNECTION_STRING=secretref:insights"
  "Site__BaseUrl=https://$APP_FQDN" "Site__NormsApiUrl=https://$FUNC_HOST/api/norms")
add_secret() { # add_secret <ім'я секрету> <ключ user-secrets> <змінна оточення>
  local value; value=$(secret "$2")
  if [ -n "$value" ]; then SECRETS+=("$1=$value"); ENVS+=("$3=secretref:$1"); fi
}
add_secret gemini Gemini:ApiKey Gemini__ApiKey
add_secret google-id Authentication:Google:ClientId Authentication__Google__ClientId
add_secret google-secret Authentication:Google:ClientSecret Authentication__Google__ClientSecret
add_secret telegram Telegram:BotToken Telegram__BotToken
[ -n "$(secret Admin:Emails:0)" ] && ENVS+=("Admin__Emails__0=$(secret Admin:Emails:0)")
[ -n "$(secret Telegram:BotUsername)" ] && ENVS+=("Telegram__BotUsername=$(secret Telegram:BotUsername)")

az containerapp secret set -g "$RG" -n "$APP" -o none --secrets "${SECRETS[@]}"
az containerapp update -g "$RG" -n "$APP" -o none --set-env-vars "${ENVS[@]}"

az functionapp cors add -g "$RG" -n "$FUNC" --allowed-origins "https://$APP_FQDN" "http://localhost:5213" -o none 2>/dev/null || true

echo "==> Доступ GitHub Actions (OIDC, без паролів): роль Contributor лише на групу ресурсів"
APP_ID=$(az ad app list --display-name dietflow-github --query "[0].appId" -o tsv)
if [ -z "$APP_ID" ]; then
  APP_ID=$(az ad app create --display-name dietflow-github --query appId -o tsv)
  az ad sp create --id "$APP_ID" -o none
fi
RG_ID=$(az group show -n "$RG" --query id -o tsv)
az role assignment create --assignee "$APP_ID" --role Contributor --scope "$RG_ID" -o none 2>/dev/null || true
az ad app federated-credential create --id "$APP_ID" --parameters "{
  \"name\": \"github-production\",
  \"issuer\": \"https://token.actions.githubusercontent.com\",
  \"subject\": \"repo:${GITHUB_REPO}:environment:production\",
  \"audiences\": [\"api://AzureADTokenExchange\"]
}" -o none 2>/dev/null || true

cat <<EOF

==================== Готово ====================
Сайт:            https://$APP_FQDN
Azure Function:  https://$FUNC_HOST/api/norms
Google OAuth → додайте Authorized redirect URI:  https://$APP_FQDN/signin-google

GitHub → Settings → Secrets and variables → Actions → Variables:
  AZURE_CLIENT_ID        = $APP_ID
  AZURE_TENANT_ID        = $(az account show --query tenantId -o tsv)
  AZURE_SUBSCRIPTION_ID  = $SUB_ID
  AZURE_RESOURCE_GROUP   = $RG
  AZURE_CONTAINERAPP     = $APP
  AZURE_FUNCTIONAPP      = $FUNC
EOF
