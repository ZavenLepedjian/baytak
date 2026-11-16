# Connects the WhatsApp assistant: stores its settings on the two Azure apps.
# Run it yourself in a PowerShell window (it asks for secrets and never shows or logs them):
#   powershell -ExecutionPolicy Bypass -File backend\deploy\setup-whatsapp.ps1
#
# Have these ready (see backend\src\Baytak.Mcp\README.md, "WhatsApp assistant"):
#   from Meta:          the app secret, a system-user access token, the phone number id
#   from OpenAI:        an API key (platform.openai.com, API keys) for the project that may use the model
#
# What it does:
#   1. Makes the service key the assistant uses to sign people in, and stores it on both apps (kept if it already exists;
#      -RotateServiceKey makes a new one).
#   2. Makes the webhook verify token and saves it to backend\deploy\whatsapp-verify-token.local.txt (git-ignored),
#      for you to paste into Meta's webhook form.
#   3. Stores the Meta and OpenAI values you type.
# Nothing is switched on until every value is there: until then the webhook answers 404.
param(
    [string]$ResourceGroup = 'rg-northeurope',
    [string]$ApiApp = 'baytak-api-8803',
    [string]$McpApp = 'baytak-mcp-8803',
    [switch]$RotateServiceKey
)
$ErrorActionPreference = 'Stop'

function New-Secret([int]$bytes) {
    $buffer = New-Object byte[] $bytes
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($buffer)
    return [Convert]::ToBase64String($buffer).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Read-Hidden([string]$prompt) {
    $secure = Read-Host -Prompt $prompt -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer).Trim() }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}

function Read-Required([string]$prompt, [switch]$Hidden) {
    while ($true) {
        if ($Hidden) { $value = Read-Hidden $prompt } else { $value = Read-Host -Prompt $prompt }
        # Copying from a web page can bring invisible direction marks and zero-width characters along (they look like nothing,
        # but are not digits or letters). None of these values contains one, or a space, so they are all dropped.
        $value = ($value -replace '[\p{Cf}\p{Zs}\p{Cc}]', '')
        if ($value.Length -gt 0) { return $value }
        Write-Host '  A value is needed.'
    }
}

# Settings go through a temporary file, so no secret appears on a command line. The file is deleted straight after.
function Set-AppSettings([string]$app, [hashtable]$settings) {
    $file = Join-Path $env:TEMP ("baytak-settings-" + [Guid]::NewGuid().ToString('N') + '.json')
    try {
        $utf8 = New-Object System.Text.UTF8Encoding($false)
        [IO.File]::WriteAllText($file, ($settings | ConvertTo-Json), $utf8)
        az functionapp config appsettings set -g $ResourceGroup -n $app --settings "@$file" -o none
        if ($LASTEXITCODE -ne 0) { throw "Could not store settings on $app ($LASTEXITCODE)" }
    }
    finally { if (Test-Path $file) { Remove-Item -Force $file } }
}

function Test-AppSetting([string]$app, [string]$name) {
    $names = az functionapp config appsettings list -g $ResourceGroup -n $app --query "[].name" -o tsv
    if ($LASTEXITCODE -ne 0) { throw "Could not read the setting names of $app. Are you signed in (az login)?" }
    return @($names) -contains $name
}

Write-Host 'WhatsApp assistant setup'
Write-Host '------------------------'

# 1. The service key, shared by the API and the assistant.
$hasKey = (Test-AppSetting $ApiApp 'Service__Key') -and (Test-AppSetting $McpApp 'WhatsApp__ApiServiceKey')
if ($hasKey -and -not $RotateServiceKey) {
    Write-Host 'Service key: already set on both apps, kept.'
}
else {
    $serviceKey = New-Secret 48
    Set-AppSettings $ApiApp @{ 'Service__Key' = $serviceKey }
    Set-AppSettings $McpApp @{ 'WhatsApp__ApiServiceKey' = $serviceKey }
    $serviceKey = $null
    Write-Host 'Service key: created and stored on both apps.'
}

# 2. The verify token, for Meta's webhook form.
$tokenFile = Join-Path $PSScriptRoot 'whatsapp-verify-token.local.txt'
if (Test-Path $tokenFile) { $verifyToken = (Get-Content $tokenFile -Raw).Trim() } else { $verifyToken = '' }
if ($verifyToken.Length -lt 20) {
    $verifyToken = New-Secret 24
    [IO.File]::WriteAllText($tokenFile, $verifyToken)
}

# 3. What Meta and OpenAI gave you.
Write-Host ''
Write-Host 'From Meta (developers.facebook.com, your app):'
$phoneNumberId = Read-Required '  Phone number id (WhatsApp > API Setup; digits, not the phone number)'
$appSecret = Read-Required '  App secret (App settings > Basic)' -Hidden
$accessToken = Read-Required '  System-user access token' -Hidden
Write-Host ''
Write-Host 'From OpenAI (platform.openai.com):'
$model = (Read-Host -Prompt '  Model name (press Enter for gpt-6-luna)').Trim()
if ($model.Length -eq 0) { $model = 'gpt-6-luna' }
$openAiKey = Read-Required '  API key' -Hidden

if ($phoneNumberId -notmatch '^[0-9]{8,20}$') { throw 'The phone number id should be digits only (it is not the phone number).' }

Set-AppSettings $McpApp @{
    'WhatsApp__VerifyToken'      = $verifyToken
    'WhatsApp__AppSecret'        = $appSecret
    'WhatsApp__AccessToken'      = $accessToken
    'WhatsApp__PhoneNumberId'    = $phoneNumberId
    'WhatsApp__OpenAiModel'      = $model
    'WhatsApp__OpenAiKey'        = $openAiKey
}
$appSecret = $null; $accessToken = $null; $openAiKey = $null

$hostName = az functionapp show -g $ResourceGroup -n $McpApp --query "properties.defaultHostName" -o tsv
if (-not $hostName) { $hostName = az functionapp show -g $ResourceGroup -n $McpApp --query "defaultHostName" -o tsv }

Write-Host ''
Write-Host 'Stored. Now, in Meta (WhatsApp > Configuration > Webhook):'
Write-Host "  Callback URL:  https://$hostName/whatsapp/webhook"
Write-Host "  Verify token:  open $tokenFile and paste its content"
Write-Host '  Then subscribe to the "messages" field.'
Write-Host ''
Write-Host 'Both apps must run the new code first: backend\deploy\deploy-api.ps1 and backend\deploy\deploy-mcp.ps1.'
