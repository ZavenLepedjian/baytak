# Publishes the connector app (ChatGPT/MCP and the WhatsApp assistant) for Linux and deploys it to `baytak-mcp-8803`.
# Run from PowerShell (do not add 2>&1 to az commands in Windows PowerShell 5.1):
#   powershell -ExecutionPolicy Bypass -File backend\deploy\deploy-mcp.ps1
# Same packaging as deploy-api.ps1: a linux-x64 framework-dependent publish, zipped with tar.exe.
$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot '..\src\Baytak.Mcp\Baytak.Mcp.csproj'
$out = Join-Path $env:TEMP 'baytak-mcp-publish'
$zip = Join-Path $env:TEMP 'baytak-mcp.zip'
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
if (Test-Path $zip) { Remove-Item -Force $zip }

dotnet publish $src -c Release -r linux-x64 --self-contained false -o $out --nologo -v q
tar.exe -a -c -f $zip -C $out .
if ($LASTEXITCODE -ne 0) { throw "zip failed ($LASTEXITCODE)" }
$entries = tar.exe -tf $zip
foreach ($required in '.azurefunctions/', 'host.json', 'Baytak.Mcp.dll') {
    if (-not ($entries | Where-Object { $_ -like "*$required*" })) { throw "package is missing $required" }
}
'zip MB: ' + [math]::Round((Get-Item $zip).Length / 1MB, 1)

az functionapp deployment source config-zip -g rg-northeurope -n baytak-mcp-8803 --src $zip --timeout 600 -o none
if ($LASTEXITCODE -ne 0) { throw "deploy failed ($LASTEXITCODE)" }
'Deployed baytak-mcp-8803'
