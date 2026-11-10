# Puts the demo building into a state that makes honest, good-looking App Store screenshots. Safe to run again.
#
#   powershell -ExecutionPolicy Bypass -File backend\deploy\prepare-screenshot-data.ps1
#
# What it does, as the demo administrator (+96170000001), through the API with the shared demo code (never printed):
#   1. Removes ONLY the apartments you name with -RemoveApartmentIds (for example 7,8,9: testers who joined the building and
#      the administrator's own test apartment). Their receipts go with them. It refuses to remove a demo apartment
#      (+96170000002 to +96170000016). With no ids given nothing is removed.
#   2. Sets who has paid, so the dashboard tells a believable story: most residents paid the latest receipt, two are
#      unpaid for a long time, a few paid late. Receipts cannot be edited; only paid/unpaid can change, and nobody is notified.
# It changes no receipts and sends no notifications. (The building's name cannot be changed through the API once residents have
# joined it; use rename-demo-building.ps1 for that.)
param(
    [string]$ResourceGroup = "rg-northeurope",
    [string]$ApiApp = "baytak-api-8803",
    [string]$ApiBase = "https://baytak-api-8803.azurewebsites.net/api",
    [int[]]$RemoveApartmentIds = @()
)

$ErrorActionPreference = "Stop"
$codeFile = Join-Path $PSScriptRoot "reviewer-code.local.txt"
if (-not (Test-Path $codeFile)) { throw "reviewer-code.local.txt is missing." }
$code = (Get-Content $codeFile -Raw).Trim()
$admin = "+96170000001"
$demoPhones = 2..16 | ForEach-Object { "+961700000{0:00}" -f $_ }

$apiKey = az functionapp keys list -g $ResourceGroup -n $ApiApp --query "functionKeys.default" -o tsv
if (-not $apiKey) { throw "Could not read the API's host key." }

function Call([string]$method, [string]$path, $body = $null, [string]$jwt = $null) {
    $headers = @{ "x-functions-key" = $apiKey }
    if ($jwt) { $headers["Authorization"] = "Bearer $jwt" }
    $req = @{ Uri = "$ApiBase/$path"; Method = $method; Headers = $headers; UseBasicParsing = $true; ContentType = "application/json"; TimeoutSec = 120 }
    if ($null -ne $body) { $req["Body"] = ($body | ConvertTo-Json -Depth 8 -Compress) }
    try { $r = Invoke-WebRequest @req }
    catch {
        $detail = ""
        if ($_.Exception.Response) { $detail = (New-Object IO.StreamReader($_.Exception.Response.GetResponseStream())).ReadToEnd() }
        throw "$method $path failed: $detail"
    }
    try { $parsed = $r.Content | ConvertFrom-Json } catch { return $r.Content }
    # Windows PowerShell 5.1 hands a JSON array back as ONE object; emit its items so callers can loop over rows.
    if ($parsed -is [System.Array]) { foreach ($item in $parsed) { $item } } else { $parsed }
}

Call POST "auth/login" @{ phoneNumber = $admin; status = ""; long = ""; lat = "" } | Out-Null
$jwt = (Call POST "auth/validate" @{ phoneNumber = $admin; pincode = $code }).auth
if (-not $jwt) { throw "Could not sign in as the demo administrator." }

# 1) Remove exactly the apartments that were named, never a demo one.
$approved = @(Call GET "appartment/approved/get" $null $jwt)
foreach ($id in $RemoveApartmentIds) {
    $a = $approved | Where-Object { $_.id -eq $id } | Select-Object -First 1
    if (-not $a) { Write-Host "Apartment id $id is not in the building (already removed?), skipped."; continue }
    if ($demoPhones -contains $a.phoneNumber) { throw "Apartment id $id ($($a.appartmentNumber)) is a demo apartment: refusing to remove it." }
    Call POST "appartment/delete/$id" @{} $jwt | Out-Null
    Write-Host "Removed apartment $($a.appartmentNumber) (id $id)."
}
if ($RemoveApartmentIds.Count -eq 0) { Write-Host "No apartments were named for removal; leaving them in place." }

# 2) Who has paid. Receipts are found by date, currency and amount.
$script:known = $null
function Find-Receipt([string]$date, [string]$currency, [decimal]$total) {
    if ($null -eq $script:known) {
        $script:known = @()
        foreach ($id in 1..25) {
            try { $s = Call GET "collection/summary?receiptId=$id" $null $jwt } catch { continue }
            if ($s.selected) { $script:known += [pscustomobject]@{ Id = $id; Date = $s.selected.date; Currency = $s.selected.currency; Total = [decimal]$s.selected.total } }
        }
    }
    $hit = $script:known | Where-Object { $_.Date -eq $date -and $_.Currency -eq $currency -and $_.Total -eq $total } | Select-Object -First 1
    if ($hit) { return $hit.Id }
    return $null
}
function Set-Paid([int]$receiptId, [string[]]$paidNumbers) {
    $s = Call GET "collection/summary?receiptId=$receiptId" $null $jwt
    foreach ($a in @($s.selected.apartments)) {
        $want = if ($paidNumbers -contains $a.apartmentNumber) { "Paid" } else { "Pending" }
        if ($a.status -ne $want) {
            Call POST "receipt/appartment/save-status" @{ appartmentId = $a.apartmentId; receiptId = $receiptId; status = $want } $jwt | Out-Null
        }
    }
    $f = Call GET "collection/summary?receiptId=$receiptId" $null $jwt
    Write-Host ("  receipt {0} ({1} {2} {3}): {4} of {5} paid" -f $receiptId, $f.selected.date, $f.selected.currency, $f.selected.total, $f.selected.paidCount, $f.selected.count)
}

$all = @("1A","1B","1C","2A","2B","2C","3A","3B","3C","4A","4B","4C","5A","5B","5C")
$plan = @(
    # October fees, issued 25 Oct: most have paid; four have not.
    @{ date = "2026-10-25"; currency = "USD"; total = 60;  paid = ($all | Where-Object { @("3A","5A","2C","4C") -notcontains $_ }) },
    # October fees, issued 1 Oct to the five original residents: three paid (4B only now, so 30 days late); 3A and 5A never did.
    @{ date = "2026-10-01"; currency = "USD"; total = 70;  paid = @("3B","4A","4B") },
    # September in lira: everyone but 3A paid (late).
    @{ date = "2026-09-01"; currency = "LBP"; total = 4000000; paid = @("3B","4A","4B","5A") }
)
foreach ($p in $plan) {
    $id = Find-Receipt $p.date $p.currency $p.total
    if ($null -eq $id) { Write-Host "Receipt $($p.date) $($p.currency) $($p.total) not found, skipped."; continue }
    Set-Paid $id $p.paid
}

# What the screens will show.
$o = Call GET "building/overview" $null $jwt
Write-Host ""
Write-Host "ADMIN HOME"
Write-Host ("  {0}: {1} apartments" -f $o.buildingName, $o.apartmentCount)
Write-Host ("  Latest receipt {0}: {1} of {2} paid, collected {3} of {4} {5}" -f $o.latest.date, $o.latest.paidCount, $o.latest.count, $o.latest.collected, $o.latest.expected, $o.latest.currency)
Write-Host ("  Still owed: " + ((@($o.outstanding) | ForEach-Object { "{0:N0} {1} ({2} apartments)" -f $_.amount, $_.currency, $_.apartments }) -join " | "))
Write-Host "  Needs attention:"
foreach ($n in @($o.needsAttention)) {
    Write-Host ("    {0} {1}: unpaid over {2} days x{3} (oldest {4}d), paid late x{5}" -f $n.apartmentNumber, $n.residentName, $o.lateAfterDays, $n.overdueCount, $n.oldestUnpaidDays, $n.latePaidCount)
}
