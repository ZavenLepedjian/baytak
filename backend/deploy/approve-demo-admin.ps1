# Approves the demo administrator for the ChatGPT reviewers. There is no back-office screen yet, so this is one SQL line.
# psql asks for the postgres admin password; it is never stored or shown by this script.
param(
    [string]$Server = "studio.postgres.database.azure.com",
    [string]$Database = "baytak",
    [string]$AdminPhone = "+96170000001",
    [string]$Psql = "C:\Program Files\PostgreSQL\18\bin\psql.exe"
)

# The SQL goes through a file: Windows PowerShell 5.1 strips the double quotes of an argument passed to a native program.
$file = Join-Path ([IO.Path]::GetTempPath()) "approve-demo-admin.sql"
$sql = "UPDATE ""Users"" SET ""Status"" = 'Approved' WHERE ""PhoneNumber"" = '$AdminPhone' AND ""Role"" = 'Admin';"
Set-Content -Path $file -Value $sql -Encoding ascii
try {
    & $Psql "host=$Server port=5432 dbname=$Database user=postgres sslmode=require" -v ON_ERROR_STOP=1 -f $file
    if ($LASTEXITCODE -ne 0) { throw "psql failed, so nothing was approved." }
} finally {
    Remove-Item $file -ErrorAction SilentlyContinue
}
Write-Host "Done. Now run ./setup-reviewer-demo.ps1 -Phase seed"
