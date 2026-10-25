# One-time setup of the storage for receipt photos (safe to run again).
#   powershell -ExecutionPolicy Bypass -File backend\deploy\setup-photo-storage.ps1
#
# 1. Creates the private container "receipt-photos" in the API's storage account (no public access).
# 2. Adds a lifecycle rule that deletes a photo one day after it was uploaded if no receipt ever claimed it. The API tags every
#    upload "status=pending" and flips the tag to "attached" when a receipt takes it, so attached photos are never touched.
#    Existing lifecycle rules on the account are kept; only the rule named below is added or replaced.
# The API itself needs nothing new: it uses the account in AzureWebJobsStorage unless Photos__ConnectionString is set.
param(
    [string]$ResourceGroup = "rg-northeurope",
    [string]$Account = "baytakapi8803",
    [string]$Container = "receipt-photos"
)

$ErrorActionPreference = "Stop"
$ruleName = "delete-unattached-receipt-photos"

# Blob index tags (needed to tell pending from attached) are not available on accounts with a hierarchical namespace.
$hns = az storage account show -g $ResourceGroup -n $Account --query isHnsEnabled -o tsv
if ($LASTEXITCODE -ne 0) { throw "Could not read the storage account." }
if ($hns -eq "true") { throw "This account has a hierarchical namespace, which does not support blob index tags. Use a different account via Photos__ConnectionString." }

az storage container create --account-name $Account --name $Container --public-access off --auth-mode login -o none
if ($LASTEXITCODE -ne 0) { throw "Could not create the container (do you have the Storage Blob Data Contributor role? Try again with the account key: add --auth-mode key)." }
Write-Host "Container '$Container' is ready and private."

$rule = @{
    enabled    = $true
    name       = $ruleName
    type       = "Lifecycle"
    definition = @{
        actions = @{ baseBlob = @{ delete = @{ daysAfterCreationGreaterThan = 1 } } }
        filters = @{
            blobTypes      = @("blockBlob")
            prefixMatch    = @("$Container/")
            blobIndexMatch = @(@{ name = "status"; op = "=="; value = "pending" })
        }
    }
}

$existing = $null
try { $existing = az storage account management-policy show -g $ResourceGroup --account-name $Account -o json 2>$null | ConvertFrom-Json } catch { }
$rules = @()
if ($existing -and $existing.policy -and $existing.policy.rules) { $rules = @($existing.policy.rules | Where-Object { $_.name -ne $ruleName }) }
$policy = @{ rules = @($rules) + @($rule) } | ConvertTo-Json -Depth 12

$file = Join-Path $env:TEMP "photo-lifecycle-policy.json"
Set-Content -Path $file -Value $policy -Encoding utf8
az storage account management-policy create -g $ResourceGroup --account-name $Account --policy "@$file" -o none
if ($LASTEXITCODE -ne 0) { throw "Could not set the lifecycle policy." }
Remove-Item $file -Force
Write-Host "Lifecycle rule '$ruleName' is in place: photos never attached to a receipt are deleted after one day."
