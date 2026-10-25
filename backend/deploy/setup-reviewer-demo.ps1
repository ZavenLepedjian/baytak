# Sets up the demo accounts and the demo building that the ChatGPT app reviewers sign in to (see "Reviewer access" in the submission doc).
# Reviewers sign in with a fixed code, so no SMS is ever sent to these numbers. All data created here is sample data.
#
#   ./setup-reviewer-demo.ps1 -Phase accounts   1) generates the shared code, sets it on the API, creates the demo accounts
#   ./deploy/approve-demo-admin.ps1             2) approves the demo administrator (asks for the postgres password)
#   ./setup-reviewer-demo.ps1 -Phase seed       3) creates the building, residents, receipts and a pending join request
#
# The code is written to reviewer-code.local.txt (git-ignored) and never printed. Needs a signed-in Azure CLI.
param(
    [ValidateSet("accounts", "seed")] [string]$Phase = "accounts",
    [string]$ResourceGroup = "rg-northeurope",
    [string]$ApiApp = "baytak-api-8803",
    [string]$ApiBase = "https://baytak-api-8803.azurewebsites.net/api"
)

$ErrorActionPreference = "Stop"
$codeFile = Join-Path $PSScriptRoot "reviewer-code.local.txt"

$admin = "+96170000001"
$reviewer = "+96170000002"
# Phone, apartment, floor, description, first name, last name
$residents = @(
    @("+96170000002", "3A", 3, "Sea view", "Rania", "Aoun"),
    @("+96170000003", "3B", 3, "Corner", "Karim", "Saade"),
    @("+96170000004", "4A", 4, "Garden side", "Maya", "Khoury"),
    @("+96170000005", "4B", 4, "Street side", "Elie", "Rizk"),
    @("+96170000006", "5A", 5, "Top floor", "Lina", "Mansour")
)
$pending = @("+96170000007", "5B", 5, "Roof access", "Tarek", "Daher")
$allPhones = @($admin) + ($residents | ForEach-Object { $_[0] }) + @($pending[0])

$apiKey = az functionapp keys list -g $ResourceGroup -n $ApiApp --query "functionKeys.default" -o tsv
if (-not $apiKey) { throw "Could not read the API's host key." }

function Call([string]$method, [string]$path, $body = $null, [string]$jwt = $null) {
    $headers = @{ "x-functions-key" = $apiKey }
    if ($jwt) { $headers["Authorization"] = "Bearer $jwt" }
    $req = @{ Uri = "$ApiBase/$path"; Method = $method; Headers = $headers; UseBasicParsing = $true; ContentType = "application/json" }
    if ($null -ne $body) { $req["Body"] = ($body | ConvertTo-Json -Depth 8 -Compress) }
    try {
        $r = Invoke-WebRequest @req
    } catch {
        $detail = ""
        if ($_.Exception.Response) {
            $reader = New-Object IO.StreamReader($_.Exception.Response.GetResponseStream())
            $detail = $reader.ReadToEnd()
        }
        throw "$method $path failed: $detail"
    }
    try { return $r.Content | ConvertFrom-Json } catch { return $r.Content }
}

function Sign-In([string]$phone, [string]$status, [string]$code) {
    Call POST "auth/login" @{ phoneNumber = $phone; status = $status; long = ""; lat = "" } | Out-Null
    $reply = Call POST "auth/validate" @{ phoneNumber = $phone; pincode = $code }
    return $reply.auth
}

if ($Phase -eq "accounts") {
    $bytes = New-Object byte[] 4
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $code = ([BitConverter]::ToUInt32($bytes, 0) % 900000 + 100000).ToString()
    Set-Content -Path $codeFile -Value $code -Encoding ascii

    az functionapp config appsettings set -g $ResourceGroup -n $ApiApp -o none --settings "Otp__FixedCode=$code" "Otp__FixedCodePhones=$($allPhones -join ',')"
    if ($LASTEXITCODE -ne 0) { throw "Could not set the reviewer code on the API." }
    Write-Host "Code set on the API (saved in $codeFile). Waiting for the API to restart..."
    Start-Sleep -Seconds 45

    $null = Sign-In $admin "request-admin" $code
    foreach ($r in $residents) { $null = Sign-In $r[0] "" $code }
    $null = Sign-In $pending[0] "" $code
    Write-Host "Demo accounts created. Next: ./approve-demo-admin.ps1, then ./setup-reviewer-demo.ps1 -Phase seed"
    return
}

# ---------------------------------------------------------------- seed
if (-not (Test-Path $codeFile)) { throw "Run -Phase accounts first." }
$code = (Get-Content $codeFile -Raw).Trim()
Start-Sleep -Seconds 31 # the sign-in cool-down per number

$adminJwt = Sign-In $admin "" $code
$status = Call GET "user/status" $null $adminJwt
if ($status.userStatus -ne "Approved") { throw "The demo administrator is not approved yet. Run ./approve-demo-admin.ps1 first." }

$existing = Call GET "building/get" $null $adminJwt
if ($existing -is [string] -and $existing -notmatch "SetupBuilding") { Write-Host "Building check: $existing" }

# A point in the open sea, so the demo building never appears next to real buildings in the app's nearest-first list.
Call POST "building/save" @{
    buildingNumber = "1"; buildingName = "Demo Residence (app review)"; street = "Demo Street"; city = "Demo City"; country = "Lebanon"
    numberOfFloors = 5; latitude = 34.2; longitude = 34.9; linkedAppartments = 0
} $adminJwt | Out-Null

$fields = @(
    @{ fieldName = "Maintenance"; type = "Amount"; value = "" },
    @{ fieldName = "Electricity"; type = "Amount"; value = "" },
    @{ fieldName = "Elevator"; type = "Amount"; value = "" },
    @{ fieldName = "Note"; type = "Text"; value = "" }
)
Call POST "save-receipt-format" @{ currency = "USD"; customFields = $fields; Date = "" } $adminJwt | Out-Null

$buildings = Call GET "buildings/get" $null $adminJwt
$building = @($buildings | Where-Object { $_.label -like "Demo Residence*" -or $_.buildingName -like "Demo Residence*" }) | Select-Object -First 1
$buildingId = if ($building.value) { $building.value } elseif ($building.id) { $building.id } else { @($buildings)[0].value }

function Join-Building($row) {
    $jwt = Sign-In $row[0] "" $code
    Call POST "user/save/profile" @{ firstName = $row[4]; lastName = $row[5] } $jwt | Out-Null
    Call POST "appartment/request" @{ appartmentNumber = $row[1]; appartmentDescription = $row[3]; floor = $row[2]; buildingId = $buildingId } $jwt | Out-Null
}

foreach ($r in $residents) { Join-Building $r }
Join-Building $pending

# Approve every request except the last one, which stays pending for the "join requests" test.
$requests = Call GET "appartment/pending/get" $null $adminJwt
foreach ($req in @($requests)) {
    if ($req.appartmentNumber -ne $pending[1]) { Call POST "appartment/approve" @{ id = $req.id } $adminJwt | Out-Null }
}

function Add-Receipt([string]$currency, [string]$date, $amounts) {
    $custom = @(
        @{ fieldName = "Maintenance"; type = "Amount"; value = $amounts[0] },
        @{ fieldName = "Electricity"; type = "Amount"; value = $amounts[1] },
        @{ fieldName = "Elevator"; type = "Amount"; value = $amounts[2] },
        @{ fieldName = "Note"; type = "Text"; value = $amounts[3] }
    )
    Call POST "receipt/add" @{ currency = $currency; appartmentIds = ""; version = "demo-seed"; receipt = @{ currency = $currency; date = $date; customFields = $custom } } $adminJwt | Out-Null
}

# An older receipt in LBP, then the current one in USD (a building has one format at a time).
Call POST "save-receipt-format" @{ currency = "LBP"; customFields = $fields; Date = "" } $adminJwt | Out-Null
Add-Receipt "LBP" "2026-09-01" @("1500000", "2200000", "300000", "September fees")
Call POST "save-receipt-format" @{ currency = "USD"; customFields = $fields; Date = "" } $adminJwt | Out-Null
Add-Receipt "USD" "2026-10-01" @("25", "40", "5", "Pay by the 10th")

# Collection: apartment 3A (the reviewer resident) owes both receipts; 3B and 4A paid October; everyone but 3A paid September.
$history = Call GET "collection/summary" $null $adminJwt
$usd = $history.receipts | Where-Object { $_.currency -eq "USD" } | Select-Object -First 1
$lbp = $history.receipts | Where-Object { $_.currency -eq "LBP" } | Select-Object -First 1
function Mark-Paid([int]$receiptId, [string[]]$numbers) {
    $summary = Call GET "collection/summary?receiptId=$receiptId" $null $adminJwt
    foreach ($a in $summary.selected.apartments) {
        if ($numbers -contains $a.apartmentNumber) {
            Call POST "receipt/appartment/save-status" @{ appartmentId = $a.apartmentId; receiptId = $receiptId; status = "Paid" } $adminJwt | Out-Null
        }
    }
}
Mark-Paid $usd.id @("3B", "4A")
Mark-Paid $lbp.id @("3B", "4A", "4B", "5A")

Write-Host "Demo building ready: 5 apartments, a USD and an LBP receipt, one pending join request."
