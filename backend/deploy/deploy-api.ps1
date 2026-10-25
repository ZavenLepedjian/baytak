# Publishes the API for Linux and deploys it to the Flex Consumption app `baytak-api-8803`.
# Run from PowerShell (do not add 2>&1 to az commands in Windows PowerShell 5.1):
#   powershell -ExecutionPolicy Bypass -File backend\deploy\deploy-api.ps1
# A portable (non runtime-specific) publish is 47 MB and timed out; the linux-x64 framework-dependent zip is the one that works.
$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot '..\src\Baytak.Api\Baytak.Api.csproj'
$out = Join-Path $env:TEMP 'baytak-api-publish'
$zip = Join-Path $env:TEMP 'baytak-api.zip'
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
if (Test-Path $zip) { Remove-Item -Force $zip }

dotnet publish $src -c Release -r linux-x64 --self-contained false -o $out --nologo -v q
# Do not use Compress-Archive: Windows PowerShell 5.1 writes backslash entry names and skips dot folders, and Azure then
# rejects the package ("Cannot find required .azurefunctions directory"). tar.exe (built into Windows 10/11) writes
# forward slashes and includes .azurefunctions.
tar.exe -a -c -f $zip -C $out .
if ($LASTEXITCODE -ne 0) { throw "zip failed ($LASTEXITCODE)" }
$entries = tar.exe -tf $zip
foreach ($required in '.azurefunctions/', 'host.json', 'Baytak.Api.dll') {
    if (-not ($entries | Where-Object { $_ -like "*$required*" })) { throw "package is missing $required" }
}
'zip MB: ' + [math]::Round((Get-Item $zip).Length / 1MB, 1)

az functionapp deployment source config-zip -g rg-northeurope -n baytak-api-8803 --src $zip --timeout 600 -o none
if ($LASTEXITCODE -ne 0) { throw "deploy failed ($LASTEXITCODE)" }
'Deployed baytak-api-8803'
