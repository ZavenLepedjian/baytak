# Makes the demo building look like a real building for screenshots: adds nine more demo apartments (floors 1 to 5, with
# demo residents), approves them, and issues one more receipt to the demo apartments with a realistic mix of paid and unpaid.
# Safe to run again: apartments that already exist are skipped and the receipt is only issued once per day.
# Only the demo residents (+96170000002 to +96170000016) are approved or billed. Anyone else who joined this building is left alone.
#
#   powershell -ExecutionPolicy Bypass -File backend\deploy\add-demo-apartments.ps1
#
# Uses the shared demo code from reviewer-code.local.txt (never printed) and a signed-in Azure CLI. It does not need the
# database password: everything goes through the API as the demo administrator (+96170000001), like setup-reviewer-demo.ps1.
# All people, numbers and amounts are sample data. The apartments are linked to the demo administrator's building.
param(
    [string]$ResourceGroup = "rg-northeurope",
    [string]$ApiApp = "baytak-api-8803",
    [string]$ApiBase = "https://baytak-api-8803.azurewebsites.net/api"
)

$ErrorActionPreference = "Stop"
$codeFile = Join-Path $PSScriptRoot "reviewer-code.local.txt"
if (-not (Test-Path $codeFile)) { throw "reviewer-code.local.txt is missing; run setup-reviewer-demo.ps1 first." }
$code = (Get-Content $codeFile -Raw).Trim()

$admin = "+96170000001"
$stayPending = "5B"   # the existing pending join request, kept for the "join requests" screen
# Phone, apartment, floor, description, first name, last name (fictional)
$newResidents = @(
    @("+96170000008", "1A", 1, "Garden side", "Nadine", "Hage"),
    @("+96170000009", "1B", 1, "Street side", "Joe", "Karam"),
    @("+96170000010", "1C", 1, "Back side", "Sara", "Daou"),
    @("+96170000011", "2A", 2, "Balcony", "Omar", "Haddad"),
    @("+96170000012", "2B", 2, "Corner", "Rita", "Fares"),
    @("+96170000013", "2C", 2, "Sea view", "Georges", "Nassar"),
    @("+96170000014", "3C", 3, "Back side", "Layal", "Chami"),
    @("+96170000015", "4C", 4, "Back side", "Fadi", "Abou Jaoude"),
    @("+96170000016", "5C", 5, "Penthouse", "Dana", "Salem")
)
# Who has paid the new receipt. 3A (the reviewer resident) is deliberately not in the list: it still owes.
$paidNumbers = @("1A", "1B", "1C", "2A", "2B", "3B", "4A", "4B", "5A", "5C")

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
    try { $parsed = $r.Content | ConvertFrom-Json } catch { return $r.Content }
    # Windows PowerShell 5.1 hands a JSON array back as ONE object; emit its items so callers can loop over rows.
    if ($parsed -is [System.Array]) { foreach ($item in $parsed) { $item } } else { $parsed }
}

function Sign-In([string]$phone) {
    Call POST "auth/login" @{ phoneNumber = $phone; status = ""; long = ""; lat = "" } | Out-Null
    $reply = Call POST "auth/validate" @{ phoneNumber = $phone; pincode = $code }
    if (-not $reply.auth) { throw "Could not sign in $phone (is it on the fixed-code list?)." }
    return $reply.auth
}

# 1) The new numbers must be on the fixed-code list, or they would try to send a real SMS.
$current = az functionapp config appsettings list -g $ResourceGroup -n $ApiApp --query "[?name=='Otp__FixedCodePhones'].value" -o tsv
$phones = @($current -split "," | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$missing = @($newResidents | ForEach-Object { $_[0] } | Where-Object { $phones -notcontains $_ })
if ($missing.Count -gt 0) {
    $all = ($phones + $missing) -join ","
    az functionapp config appsettings set -g $ResourceGroup -n $ApiApp -o none --settings "Otp__FixedCodePhones=$all"
    if ($LASTEXITCODE -ne 0) { throw "Could not update the demo number list." }
    Write-Host "Added $($missing.Count) numbers to the demo list. Waiting for the API to restart..."
    Start-Sleep -Seconds 60
} else {
    Write-Host "Demo number list already up to date."
}

# 2) Administrator and building.
$adminJwt = Sign-In $admin
$status = Call GET "user/status" $null $adminJwt
if ($status.userStatus -ne "Approved") { throw "The demo administrator is not approved." }

$buildings = Call GET "buildings/get" $null $adminJwt
$building = @($buildings | Where-Object { $_.label -match '^(Demo|Cedar) Residence' -or $_.buildingName -match '^(Demo|Cedar) Residence' }) | Select-Object -First 1
if (-not $building) { throw "The demo building was not found; run setup-reviewer-demo.ps1 -Phase seed first." }
$buildingId = if ($building.value) { $building.value } elseif ($building.id) { $building.id } else { @($buildings)[0].value }

# 3) Residents request their apartments (skipping any that already exist or are waiting).
$approved = @(Call GET "appartment/approved/get" $null $adminJwt)
$waiting = @(Call GET "appartment/pending/get" $null $adminJwt)
$known = @($approved + $waiting | ForEach-Object { $_.appartmentNumber })
foreach ($r in $newResidents) {
    if ($known -contains $r[1]) { Write-Host "Apartment $($r[1]) already exists, skipping."; continue }
    $jwt = Sign-In $r[0]
    Call POST "user/save/profile" @{ firstName = $r[4]; lastName = $r[5] } $jwt | Out-Null
    Call POST "appartment/request" @{ appartmentNumber = $r[1]; appartmentDescription = $r[3]; floor = $r[2]; buildingId = $buildingId } $jwt | Out-Null
    Write-Host "Requested $($r[1]) for $($r[4]) $($r[5])."
}

# 4) The administrator approves only the demo residents' requests. Other people can find and join this building too
#    (testers, reviewers); their requests and apartments are never touched or billed by this script.
$demoPhones = @("+96170000002", "+96170000003", "+96170000004", "+96170000005", "+96170000006") + @($newResidents | ForEach-Object { $_[0] })
foreach ($req in @(Call GET "appartment/pending/get" $null $adminJwt)) {
    if ($req.appartmentNumber -ne $stayPending -and $demoPhones -contains $req.phoneNumber) {
        Call POST "appartment/approve" @{ id = $req.id } $adminJwt | Out-Null
        Write-Host "Approved $($req.appartmentNumber)."
    }
}

# 5) One more receipt, issued only to the demo apartments, then the payments.
$demoIds = @(Call GET "appartment/approved/get" $null $adminJwt | Where-Object { $demoPhones -contains $_.phoneNumber } | ForEach-Object { $_.id })
if ($demoIds.Count -eq 0) { throw "No demo apartments are approved." }
$today = (Get-Date).ToString("yyyy-MM-dd")
$summary = Call GET "collection/summary" $null $adminJwt
$already = @($summary.receipts | Where-Object { $_.currency -eq "USD" -and [decimal]$_.total -eq 35 -and ([datetime]$_.date).ToString("yyyy-MM-dd") -eq $today })
if ($already.Count -eq 0) {
    $custom = @(
        @{ fieldName = "Maintenance"; type = "Amount"; value = "0" },
        @{ fieldName = "Electricity"; type = "Amount"; value = "35" },
        @{ fieldName = "Elevator"; type = "Amount"; value = "0" },
        @{ fieldName = "Note"; type = "Text"; value = "Generator fuel top-up, shared equally" }
    )
    Call POST "receipt/add" @{ currency = "USD"; appartmentIds = ($demoIds -join ","); version = "demo-seed"; receipt = @{ currency = "USD"; date = $today; customFields = $custom } } $adminJwt | Out-Null
    Write-Host "Issued the generator receipt (35 USD) to the $($demoIds.Count) demo apartments."
} else {
    Write-Host "Today's generator receipt already exists, skipping."
}

$summary = Call GET "collection/summary" $null $adminJwt
$receipt = @($summary.receipts | Where-Object { $_.currency -eq "USD" -and [decimal]$_.total -eq 35 -and ([datetime]$_.date).ToString("yyyy-MM-dd") -eq $today }) | Select-Object -First 1
if (-not $receipt) { throw "The new receipt was not found in the collection summary." }
$detail = Call GET "collection/summary?receiptId=$($receipt.id)" $null $adminJwt
foreach ($a in $detail.selected.apartments) {
    if ($paidNumbers -contains $a.apartmentNumber -and $a.status -ne "Paid") {
        Call POST "receipt/appartment/save-status" @{ appartmentId = $a.apartmentId; receiptId = $receipt.id; status = "Paid" } $adminJwt | Out-Null
    }
}

$final = Call GET "collection/summary?receiptId=$($receipt.id)" $null $adminJwt
$apts = @($final.selected.apartments)
$paid = @($apts | Where-Object { $_.status -eq "Paid" }).Count
Write-Host "Done. The newest receipt covers $($apts.Count) apartments: $paid paid, $($apts.Count - $paid) not yet."
