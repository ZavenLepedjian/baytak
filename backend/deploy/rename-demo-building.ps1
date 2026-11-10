# Renames the demo building. The API refuses this once residents have joined (it protects them from details changing under
# them), so it is one SQL line. psql asks for the postgres admin password; it is never stored or shown by this script.
#   powershell -ExecutionPolicy Bypass -File backend\deploy\rename-demo-building.ps1
param(
    [string]$Name = "Cedar Residence",
    [string]$Server = "studio.postgres.database.azure.com",
    [string]$Database = "baytak",
    [string]$AdminPhone = "+96170000001",
    [string]$Psql = "C:\Program Files\PostgreSQL\18\bin\psql.exe"
)

if ($Name -match "'") { throw "Use a name without an apostrophe." }
# The SQL goes through a file: Windows PowerShell 5.1 strips the double quotes of an argument passed to a native program.
$file = Join-Path ([IO.Path]::GetTempPath()) "rename-demo-building.sql"
$sql = "UPDATE ""Buildings"" SET ""BuildingName"" = '$Name' WHERE ""AdminUserId"" = (SELECT ""Id"" FROM ""Users"" WHERE ""PhoneNumber"" = '$AdminPhone');"
Set-Content -Path $file -Value $sql -Encoding ascii
try {
    & $Psql "host=$Server port=5432 dbname=$Database user=postgres sslmode=require" -v ON_ERROR_STOP=1 -f $file
    if ($LASTEXITCODE -ne 0) { throw "psql failed, so nothing was renamed." }
} finally {
    Remove-Item $file -ErrorAction SilentlyContinue
}
Write-Host "Done: the demo building is now called '$Name'."
