# Publishes website/public to the static website of the API's storage account (the $web container).
# Run it again after any change: it turns static website hosting on (a no-op when it already is) and overwrites the files.
# Needs a signed-in Azure CLI with access to the storage account. The account key is read by the CLI and never shown.
param(
    [string]$Account = "baytakapi8803",
    [string]$ResourceGroup = "rg-northeurope",
    [string]$Source = (Join-Path $PSScriptRoot "public")
)

$ErrorActionPreference = "Stop"

az storage blob service-properties update --account-name $Account --auth-mode key `
    --static-website --index-document index.html --404-document 404.html -o none
if ($LASTEXITCODE -ne 0) { throw "Could not turn on static website hosting." }

# Fonts and images rarely change: a week. Pages, styles, script and icons: five minutes, so edits show up quickly.
az storage blob upload-batch --account-name $Account --auth-mode key --destination '$web' --source $Source `
    --pattern "assets/fonts/*" --overwrite --content-type "font/woff2" --content-cache-control "public, max-age=604800" -o none
az storage blob upload-batch --account-name $Account --auth-mode key --destination '$web' --source $Source `
    --pattern "assets/img/*" --overwrite --content-cache-control "public, max-age=604800" -o none
az storage blob upload-batch --account-name $Account --auth-mode key --destination '$web' --source $Source `
    --pattern "demo/*" --overwrite --content-type "video/mp4" --content-cache-control "public, max-age=86400" -o none
foreach ($pattern in @("*.html", "*.css", "*.js", "*.svg", "*.txt")) {
    az storage blob upload-batch --account-name $Account --auth-mode key --destination '$web' --source $Source `
        --pattern $pattern --overwrite --content-cache-control "public, max-age=300" -o none
}
if ($LASTEXITCODE -ne 0) { throw "Upload failed." }

$endpoint = az storage account show -g $ResourceGroup -n $Account --query "primaryEndpoints.web" -o tsv
Write-Host "Published: $endpoint"
